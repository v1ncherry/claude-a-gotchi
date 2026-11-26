using System.Net.Http.Headers;
using System.Text.Json;

namespace ClaudeAGotchi;

// Looks at most once a day whether GitHub has a newer release. Only the public release list is fetched;
// nothing about the user is sent. A newer version shows up as a link in the settings, nothing installs by itself.
static class Updates
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    static bool running;

    public static bool Available(Settings st) =>
        Version.TryParse(st.LatestVersion, out var v) && v > AppInfo.Version;

    public static string ReleasesUrl => AppInfo.RepoUrl + "/releases/latest";

    public static void CheckAsync(Settings st, Action changed)
    {
        if (running) return;
        if (st.LastUpdateCheck is DateTime last && DateTime.UtcNow - last < TimeSpan.FromHours(23)) return;
        running = true;
        Task.Run(async () =>
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/" + AppInfo.Repo + "/releases/latest");
                req.Headers.UserAgent.ParseAdd(AppInfo.Name + "/" + AppInfo.VersionText);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                using var res = await Http.SendAsync(req);
                st.LastUpdateCheck = DateTime.UtcNow;
                if (res.IsSuccessStatusCode)
                {
                    using var d = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                    string tag = d.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
                    st.LatestVersion = tag.TrimStart('v', 'V');
                }
                st.Save();
                changed?.Invoke();
            }
            catch (Exception ex) { AppPaths.LogLine("Update check failed: " + ex.GetType().Name); }
            finally { running = false; }
        });
    }
}
