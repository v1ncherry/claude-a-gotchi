using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ClaudeAGotchi;

sealed class UsageInfo
{
    public double? FiveHour;
    public DateTimeOffset? FiveReset;
    public double? Week;
    public string Status = "not asked yet";
}

static class UsageService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    // Unofficial: the same request Claude Code uses for /usage. The token only ever goes to api.anthropic.com.
    // Only used after the user agreed to it in the welcome window or the settings.
    public static async Task<UsageInfo> FetchAsync()
    {
        var u = new UsageInfo();
        try
        {
            string path = Path.Combine(AppPaths.ClaudeDir, ".credentials.json");
            if (!File.Exists(path)) { u.Status = "no Claude Code login found"; return u; }
            string token;
            using (var doc = JsonDocument.Parse(File.ReadAllText(path)))
            {
                if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var o)) { u.Status = "no subscription login found"; return u; }
                token = o.GetProperty("accessToken").GetString();
                if (o.TryGetProperty("expiresAt", out var e) && e.ValueKind == JsonValueKind.Number &&
                    DateTimeOffset.FromUnixTimeMilliseconds(e.GetInt64()) < DateTimeOffset.UtcNow)
                { u.Status = "login expired, start Claude Code once"; return u; }
            }
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("anthropic-beta", "oauth-2025-04-20");
            req.Headers.UserAgent.ParseAdd(AppInfo.Name + "/" + AppInfo.VersionText);
            using var res = await Http.SendAsync(req);
            string body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) { u.Status = "request failed (" + (int)res.StatusCode + ")"; return u; }
            using var j = JsonDocument.Parse(body);
            (u.FiveHour, u.FiveReset) = Read(j.RootElement, "five_hour");
            (u.Week, _) = Read(j.RootElement, "seven_day");
            u.Status = u.FiveHour.HasValue ? "ok" : "no limit data";
        }
        catch (Exception ex) { u.Status = "request failed (" + ex.GetType().Name + ")"; }
        return u;
    }

    static (double?, DateTimeOffset?) Read(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var x) || x.ValueKind != JsonValueKind.Object) return (null, null);
        double? p = x.TryGetProperty("utilization", out var ut) && ut.ValueKind == JsonValueKind.Number ? ut.GetDouble() : null;
        DateTimeOffset? r = null;
        if (x.TryGetProperty("resets_at", out var ra) && ra.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(ra.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)) r = d;
        return (p, r);
    }

}

// Reads the local Claude logs (~/.claude/projects/**/*.jsonl), also those of the Code tab in the desktop app.
// Only newly appended lines are read. From them it works out:
//  - whether a limit is reached and when it resets (entries with usage_limit_reached),
//  - the tokens in the current 5-hour window,
//  - an estimate in percent: how many tokens had earlier windows used when the limit hit?
// Nothing leaves the computer.
sealed class LocalLog
{
    static readonly TimeSpan Five = TimeSpan.FromHours(5), Week = TimeSpan.FromDays(7), Keep = TimeSpan.FromDays(30);

    readonly Dictionary<string, long> offsets = new();
    readonly List<(DateTime T, long N)> entries = new();
    readonly HashSet<string> ids = new();
    readonly List<(DateTime At, DateTimeOffset Until, string Type)> rejects = new();
    DateTime lastOk = DateTime.MinValue;
    bool dirty;

    public sealed record Snapshot(long Tokens5h, DateTimeOffset? LimitUntil, string LimitType,
        double? Est5h, DateTimeOffset? Reset5h, double? EstWeek, int Samples);

    public Snapshot Scan()
    {
        var now = DateTime.UtcNow;
        long bytesRead = 0;
        string dir = Path.Combine(AppPaths.ClaudeDir, "projects");
        if (Directory.Exists(dir))
        {
            var horizon = now - Keep;
            foreach (var f in Directory.EnumerateFiles(dir, "*.jsonl", SearchOption.AllDirectories))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(f) < horizon) continue;
                    long off = offsets.TryGetValue(f, out var o) ? o : 0;
                    long len = new FileInfo(f).Length;
                    if (len < off) off = 0;
                    if (len == off) continue;
                    // Line by line, so even large logs need little memory.
                    using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
                    fs.Seek(off, SeekOrigin.Begin);
                    using var sr = new StreamReader(fs, System.Text.Encoding.UTF8, false, 64 * 1024);
                    long avail = len - off, used = 0;
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        long n = System.Text.Encoding.UTF8.GetByteCount(line) + 1;
                        if (used + n > avail) break;          // last line not completely written yet
                        used += n;
                        Line(line, now);
                    }
                    offsets[f] = off + used;
                    bytesRead += used;
                }
                catch { }
            }
        }
        // After reading a lot (mostly at startup), give the memory back to Windows.
        if (bytesRead > 8_000_000) GC.Collect(2, GCCollectionMode.Aggressive, true, true);

        if (dirty) { entries.Sort((a, b) => a.T.CompareTo(b.T)); dirty = false; }
        entries.RemoveAll(e => e.T < now - Keep);
        if (ids.Count > 100_000) ids.Clear();

        // Limit reached?
        var last = rejects.Count > 0 ? rejects.MaxBy(r => r.At) : default;
        bool limited = rejects.Count > 0 && last.Until > DateTimeOffset.UtcNow && lastOk <= last.At;

        // Current 5-hour window: starts with the first message after the previous one ended.
        DateTime? start = null;
        foreach (var e in entries) if (start == null || e.T >= start.Value + Five) start = e.T;
        if (limited && last.Type == "five_hour") start = last.Until.UtcDateTime - Five;
        long tokens = 0;
        DateTimeOffset? reset = null;
        if (start.HasValue && now < start.Value + Five)
        {
            var s0 = start.Value;
            tokens = entries.Where(e => e.T >= s0).Sum(e => e.N);
            reset = new DateTimeOffset(s0 + Five, TimeSpan.Zero);
        }

        // Estimate from earlier limit hits
        var s5 = rejects.Where(r => r.Type == "five_hour")
            .Select(r => SumBetween(r.Until.UtcDateTime - Five, r.At)).Where(x => x > 0).ToList();
        long? budget5 = Median(s5);
        double? est5 = budget5 > 0 ? Math.Min(100, tokens * 100.0 / budget5.Value) : null;

        var sw = rejects.Where(r => r.Type.Contains("seven"))
            .Select(r => SumBetween(r.Until.UtcDateTime - Week, r.At)).Where(x => x > 0).ToList();
        long? budgetW = Median(sw);
        double? estW = budgetW > 0 ? Math.Min(100, SumBetween(now - Week, now) * 100.0 / budgetW.Value) : null;

        return new Snapshot(tokens, limited ? last.Until : null, limited ? last.Type : "", est5, reset, estW, s5.Count);
    }

    long SumBetween(DateTime a, DateTime b) => entries.Where(e => e.T >= a && e.T <= b).Sum(e => e.N);

    static long? Median(List<long> v)
    {
        if (v.Count == 0) return null;
        v.Sort();
        return v[v.Count / 2];
    }

    void Line(string line, DateTime now)
    {
        bool reject = line.Contains("usage_limit_reached");
        if (!reject && !line.Contains("\"usage\"")) return;
        try
        {
            using var d = JsonDocument.Parse(line);
            var r = d.RootElement;
            if (!r.TryGetProperty("timestamp", out var ts) || ts.ValueKind != JsonValueKind.String) return;
            if (!DateTime.TryParse(ts.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)) return;
            if (reject)
            {
                if (r.TryGetProperty("apiErrorParams", out var p) && p.ValueKind == JsonValueKind.Object &&
                    p.TryGetProperty("rate_limit_info", out var info) && info.ValueKind == JsonValueKind.Object &&
                    info.TryGetProperty("resetsAt", out var ra) && ra.ValueKind == JsonValueKind.Number)
                {
                    var until = DateTimeOffset.FromUnixTimeSeconds(ra.GetInt64());
                    string type = info.TryGetProperty("rateLimitType", out var rt) ? rt.GetString() ?? "" : "";
                    if (!rejects.Any(x => x.At == t && x.Until == until)) rejects.Add((t, until, type));
                }
                return;
            }
            if (r.TryGetProperty("isApiErrorMessage", out var err) && err.ValueKind == JsonValueKind.True) return;
            if (!r.TryGetProperty("message", out var m) || m.ValueKind != JsonValueKind.Object) return;
            if (!m.TryGetProperty("usage", out var us) || us.ValueKind != JsonValueKind.Object) return;
            if (t > lastOk) lastOk = t;
            if (t < now - Keep) return;
            string id = m.TryGetProperty("id", out var mid) && mid.ValueKind == JsonValueKind.String ? mid.GetString() : null;
            if (id != null && !ids.Add(id)) return;
            entries.Add((t, Num(us, "input_tokens") + Num(us, "output_tokens") + Num(us, "cache_creation_input_tokens")));
            dirty = true;
        }
        catch { }
    }

    static long Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
}
