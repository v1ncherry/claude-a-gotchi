using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeAGotchi;

// Link to Claude Code: hooks call "claude-a-gotchi.exe --hook <event>", which writes the state to state.json,
// and the running app reads it. Nothing else is read from the hook input than the working folder and error flags.
static class HookBridge
{
    static readonly string[] Events = { "UserPromptSubmit", "PostToolUse", "Notification", "Stop", "SessionEnd" };
    const string Marker = "claude-a-gotchi.exe";
    const string OldMarker = "Claudegotchi.exe";

    public static string ClaudeSettingsPath => Path.Combine(AppPaths.ClaudeDir, "settings.json");
    static bool Debug => Environment.GetEnvironmentVariable("CLAUDE_A_GOTCHI_DEBUG") == "1";

    public static void HandleHook(string evt)
    {
        string input = "";
        // A windowed app has no console, so the input stream is read directly; wait at most 2 s.
        try
        {
            var read = Task.Run(() => { using var sr = new StreamReader(Console.OpenStandardInput()); return sr.ReadToEnd(); });
            if (read.Wait(2000)) input = read.Result;
        }
        catch (Exception ex) { if (Debug) AppPaths.LogLine("Hook input error: " + ex.GetType().Name); }
        if (Debug) AppPaths.LogLine("Hook " + evt + ": " + input.Length + " characters of input");

        string state = evt switch
        {
            "Notification" => "ask",
            "Stop" => "done",
            "SessionEnd" => "idle",
            _ => "work",
        };
        string cwd = "";
        try
        {
            if (input.Length > 0)
            {
                var n = JsonNode.Parse(input);
                cwd = n?["cwd"]?.GetValue<string>() ?? "";
                if (evt == "PostToolUse" && n?["tool_response"] is JsonObject o)
                {
                    bool isErr = o["is_error"] is JsonValue v && v.GetValueKind() == JsonValueKind.True;
                    bool hasErr = o["error"] is JsonNode e && e.ToString().Length > 0;
                    if (isErr || hasErr) state = "error";
                }
            }
        }
        catch { }

        Directory.CreateDirectory(AppPaths.Data);
        var obj = new JsonObject
        {
            ["state"] = state,
            ["event"] = evt,
            ["cwd"] = cwd,
            ["t"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        string tmp = AppPaths.State + ".tmp" + Environment.ProcessId;
        File.WriteAllText(tmp, obj.ToJsonString());
        File.Move(tmp, AppPaths.State, true);
    }

    public static bool IsInstalled() => SettingsContain(Marker);
    public static bool HasOldHooks() => SettingsContain(OldMarker);

    static bool SettingsContain(string s)
    {
        try { return File.Exists(ClaudeSettingsPath) && File.ReadAllText(ClaudeSettingsPath).Contains(s); }
        catch { return false; }
    }

    public static void Install(string exePath)
    {
        var root = Load();
        Remove(root);
        var hooks = root["hooks"] as JsonObject;
        if (hooks == null) { hooks = new JsonObject(); root["hooks"] = hooks; }
        string cmdPath = exePath.Replace('\\', '/');
        foreach (var ev in Events)
        {
            var arr = hooks[ev] as JsonArray;
            if (arr == null) { arr = new JsonArray(); hooks[ev] = arr; }
            var group = new JsonObject();
            if (ev == "PostToolUse") group["matcher"] = "*";
            group["hooks"] = new JsonArray(new JsonObject
            {
                ["type"] = "command",
                ["command"] = "\"" + cmdPath + "\" --hook " + ev,
                ["timeout"] = 5,
            });
            arr.Add(group);
        }
        Save(root);
    }

    public static void Uninstall()
    {
        if (!File.Exists(ClaudeSettingsPath)) return;
        var root = Load();
        Remove(root);
        Save(root);
    }

    static JsonObject Load()
    {
        if (!File.Exists(ClaudeSettingsPath)) return new JsonObject();
        var text = File.ReadAllText(ClaudeSettingsPath);
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
        var node = JsonNode.Parse(text, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return node as JsonObject ?? throw new InvalidOperationException("settings.json has an unexpected format.");
    }

    // Removes only our own entries (new and old name); every other hook stays untouched.
    static void Remove(JsonObject root)
    {
        if (root["hooks"] is not JsonObject hooks) return;
        foreach (var ev in hooks.Select(kv => kv.Key).ToList())
        {
            if (hooks[ev] is not JsonArray arr) continue;
            for (int i = arr.Count - 1; i >= 0; i--)
            {
                if (arr[i] is JsonObject g && g["hooks"] is JsonArray hs)
                {
                    for (int j = hs.Count - 1; j >= 0; j--)
                    {
                        string cmd = hs[j]?["command"]?.ToString() ?? "";
                        if (cmd.Contains(Marker) || cmd.Contains(OldMarker)) hs.RemoveAt(j);
                    }
                    if (hs.Count == 0) arr.RemoveAt(i);
                }
            }
            if (arr.Count == 0) hooks.Remove(ev);
        }
        if (hooks.Count == 0) root.Remove("hooks");
    }

    // Keeps a backup of the previous settings.json next to it.
    static void Save(JsonObject root)
    {
        Directory.CreateDirectory(AppPaths.ClaudeDir);
        if (File.Exists(ClaudeSettingsPath))
            File.Copy(ClaudeSettingsPath, ClaudeSettingsPath + ".claude-a-gotchi-backup", true);
        File.WriteAllText(ClaudeSettingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
