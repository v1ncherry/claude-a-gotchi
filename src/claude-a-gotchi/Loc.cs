using System.Runtime.InteropServices;
using System.Text.Json;

namespace ClaudeAGotchi;

// Translations. Each language is a flat JSON file (lang/en.json, lang/de.json, ...) built into the program.
// Files in %LOCALAPPDATA%\claude-a-gotchi\lang override or add languages without rebuilding.
static class Loc
{
    sealed record Lang(string Code, string Name, Dictionary<string, string> Texts);

    static readonly Dictionary<string, Lang> langs = new(StringComparer.OrdinalIgnoreCase);
    static Lang cur, fallback;
    public static readonly List<string> Errors = new();

    public static string Code => cur?.Code ?? "en";

    // Languages that can be picked in the settings, in a stable order (English first).
    public static IReadOnlyList<(string Code, string Name)> Available =>
        langs.Values.OrderBy(l => l.Code == "en" ? "" : l.Name).Select(l => (l.Code, l.Name)).ToList();

    public static void Load()
    {
        langs.Clear();
        Errors.Clear();
        var asm = typeof(Loc).Assembly;
        foreach (var res in asm.GetManifestResourceNames().Where(n => n.Replace('\\', '/').StartsWith("lang/")))
        {
            using var s = asm.GetManifestResourceStream(res);
            using var r = new StreamReader(s);
            Add(r.ReadToEnd(), res);
        }
        string user = Path.Combine(AppPaths.Data, "lang");
        if (Directory.Exists(user))
            foreach (var f in Directory.GetFiles(user, "*.json"))
                try { Add(File.ReadAllText(f), f); } catch (Exception ex) { Errors.Add(f + ": " + ex.Message); }
        langs.TryGetValue("en", out fallback);
    }

    // Parses one language file. Returns an error text, or null when it is fine.
    public static string Add(string json, string source)
    {
        try
        {
            using var d = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var texts = new Dictionary<string, string>();
            foreach (var p in d.RootElement.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String) texts[p.Name] = p.Value.GetString();
            if (!texts.TryGetValue("_code", out var code) || string.IsNullOrWhiteSpace(code)) throw new FormatException("\"_code\" is missing");
            string name = texts.TryGetValue("_name", out var n) ? n : code;
            if (langs.TryGetValue(code, out var existing))
                foreach (var kv in texts) existing.Texts[kv.Key] = kv.Value;   // user file overrides single texts
            else
                langs[code] = new Lang(code, name, texts);
            return null;
        }
        catch (Exception ex)
        {
            string e = source + ": " + ex.Message;
            Errors.Add(e);
            return e;
        }
    }

    // "auto" follows the Windows display language; unknown languages fall back to English.
    public static void Use(string setting)
    {
        string code = setting;
        if (string.IsNullOrEmpty(code) || code == "auto") code = WindowsLanguage();
        cur = langs.TryGetValue(code, out var l) ? l : fallback;
    }

    public static string T(string key, params object[] args)
    {
        string s = null;
        if (cur != null && cur.Texts.TryGetValue(key, out var a)) s = a;
        else if (fallback != null && fallback.Texts.TryGetValue(key, out var b)) s = b;
        s ??= key;
        return args.Length > 0 ? string.Format(s, args) : s;
    }

    // Name of a costume or other item in the current language: "name.de", then "name".
    public static string Pick(IReadOnlyDictionary<string, string> names, string fallbackName)
    {
        if (names != null && names.TryGetValue(Code, out var n)) return n;
        if (names != null && names.TryGetValue("", out var d)) return d;
        return fallbackName;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetUserPreferredUILanguages(uint flags, out uint count, char[] buffer, ref uint size);

    // The program runs with invariant culture, so the language is read from Windows directly ("de-DE" -> "de").
    public static string WindowsLanguage()
    {
        try
        {
            uint size = 0;
            GetUserPreferredUILanguages(0x8, out _, null, ref size);   // MUI_LANGUAGE_NAME
            var buf = new char[size];
            if (GetUserPreferredUILanguages(0x8, out _, buf, ref size))
            {
                string first = new string(buf).Split('\0')[0];
                if (first.Length >= 2) return first[..2].ToLowerInvariant();
            }
        }
        catch { }
        return "en";
    }

    // Number of tokens in a short form: "1.5M" / "1,5 Mio."
    public static string Tokens(long n)
    {
        string dec = T("fmt.decimal");
        if (n >= 1_000_000) return T("fmt.millions", (n / 1_000_000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace(".", dec));
        if (n >= 1_000) return T("fmt.thousands", n / 1000);
        return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string Time(DateTimeOffset t) => t.ToLocalTime().ToString(T("fmt.time"), System.Globalization.CultureInfo.InvariantCulture);
    public static string DayTime(DateTimeOffset t) => t.ToLocalTime().ToString(T("fmt.dayTime"), System.Globalization.CultureInfo.InvariantCulture);
}
