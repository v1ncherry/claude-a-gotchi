using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;

namespace ClaudeAGotchi;

static class AppInfo
{
    public const string Name = "claude-a-gotchi";
    public const string Repo = "v1ncherry/claude-a-gotchi";
    public const string RepoUrl = "https://github.com/" + Repo;
    // Ko-fi page name (ko-fi.com/<name>). Empty: the "Support" link in the settings stays hidden.
    public const string KofiName = "";
    public static Version Version => typeof(AppInfo).Assembly.GetName().Version ?? new Version(0, 0);
    public static string VersionText => Version.ToString(3);
    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, Name + ".exe");
}

static class AppPaths
{
    static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static string Data => Path.Combine(Local, AppInfo.Name);
    public static string State => Path.Combine(Data, "state.json");
    public static string SettingsFile => Path.Combine(Data, "settings.json");
    public static string Log => Path.Combine(Data, "log.txt");
    public static string ClaudeDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    // The app was called "Claudegotchi" before it was published.
    public static string OldData => Path.Combine(Local, "Claudegotchi");
    public static string OldInstall => Path.Combine(Local, "Programs", "Claudegotchi");

    public static void LogLine(string s)
    {
        try
        {
            Directory.CreateDirectory(Data);
            var fi = new FileInfo(Log);
            if (fi.Exists && fi.Length > 200_000) fi.Delete();
            File.AppendAllText(Log, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + s + Environment.NewLine);
        }
        catch { }
    }
}

sealed class Settings
{
    public string Hat { get; set; } = "none";
    public string Outfit { get; set; } = "none";
    public string BodyColor { get; set; } = "classic";
    public string Size { get; set; } = "small";
    public string BedFrame { get; set; } = "wood";
    public string BedCover { get; set; } = "dots";
    public string Pillow { get; set; } = "lavender";
    public string BarStyle { get; set; } = "text";       // "text" = labels and percent, "bars" = bars only
    public bool ShowHourglass { get; set; } = true;
    public int SadAt { get; set; } = 90;                 // from this % of the 5-hour limit he cries and sweats (100 = never)
    public string ScreenName { get; set; } = "";
    public bool UsageEnabled { get; set; } = false;      // exact limits via the unofficial endpoint, only after consent
    public bool Napping { get; set; } = false;
    public string Language { get; set; } = "auto";
    public bool Welcomed { get; set; } = false;          // the welcome window was shown and closed
    public DateTime? LastUpdateCheck { get; set; }
    public string LatestVersion { get; set; } = "";

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new Settings(); }
        catch { return new Settings(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Data);
            string tmp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, AppPaths.SettingsFile, true);
        }
        catch { }
    }
}

static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool Enabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(AppInfo.Name) != null;
    }

    public static void Set(bool on)
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue(AppInfo.Name, "\"" + AppInfo.ExePath + "\"");
        else k.DeleteValue(AppInfo.Name, false);
    }

    // Removes the entry of the old "Claudegotchi". Returns true if there was one.
    public static bool RemoveOld()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (k?.GetValue("Claudegotchi") == null) return false;
        k.DeleteValue("Claudegotchi", false);
        return true;
    }
}

// One-time move from the unpublished "Claudegotchi" to claude-a-gotchi: settings, hooks and autostart.
static class Migration
{
    public static void Run()
    {
        try
        {
            bool hadOld = Directory.Exists(AppPaths.OldData) || Directory.Exists(AppPaths.OldInstall);
            if (!hadOld) return;
            // Only the installed app takes over; a test build started from somewhere else leaves everything alone.
            string programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
            if (!AppInfo.ExePath.StartsWith(programs, StringComparison.OrdinalIgnoreCase)) return;
            AppPaths.LogLine("Found the old Claudegotchi, taking over its settings");

            foreach (var p in Process.GetProcessesByName("Claudegotchi"))
                try { p.Kill(); p.WaitForExit(3000); } catch { }

            string oldSettings = Path.Combine(AppPaths.OldData, "settings.json");
            if (File.Exists(oldSettings) && !File.Exists(AppPaths.SettingsFile))
            {
                Directory.CreateDirectory(AppPaths.Data);
                File.Copy(oldSettings, AppPaths.SettingsFile);
                var st = Settings.Load();
                st.Welcomed = true;   // they know Clawd already and agreed to the limit query back then
                st.Save();
            }

            if (HookBridge.HasOldHooks()) HookBridge.Install(AppInfo.ExePath);
            if (Autostart.RemoveOld()) Autostart.Set(true);

            TryDelete(AppPaths.OldInstall);
            TryDelete(AppPaths.OldData);
        }
        catch (Exception ex) { AppPaths.LogLine("Migration failed: " + ex.Message); }
    }

    // A file can still be busy for a moment right after the old app was stopped, so try a few times.
    static void TryDelete(string dir)
    {
        for (int i = 0; i < 5 && Directory.Exists(dir); i++)
        {
            try { Directory.Delete(dir, true); }
            catch (Exception ex) { if (i == 4) AppPaths.LogLine("Could not delete " + dir + ": " + ex.Message); else Thread.Sleep(400); }
        }
    }
}
