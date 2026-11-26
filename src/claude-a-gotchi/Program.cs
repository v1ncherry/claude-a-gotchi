using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ClaudeAGotchi;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        string a0 = args.Length > 0 ? args[0] : "";
        string Arg(int i, string def = null) => args.Length > i ? args[i] : def;

        // Called from a Claude Code hook: write the state and exit at once, without any window.
        // This path runs on every tool call, so it must stay small and fast.
        if (a0 == "--hook" && args.Length >= 2)
        {
            try { HookBridge.HandleHook(args[1]); } catch { }
            return 0;
        }

        switch (a0)
        {
            // Used by the installer and for manual setup. Without a path the hooks point to this exe.
            case "--install-hooks":
                try { HookBridge.Install(Arg(1, AppInfo.ExePath)); return 0; } catch { return 1; }
            case "--uninstall-hooks":
                try { HookBridge.Uninstall(); return 0; } catch { return 1; }
            case "--autostart":
                try { Autostart.Set(Arg(1) == "on"); return 0; } catch { return 1; }
            // Tools for contributors and the build (see CONTRIBUTING.md); they print to the console.
            case "--preview" when args.Length >= 3:
                return RunTool(() => Tools.Preview(args[1], args[2]));
            case "--check":
                return RunTool(() => Tools.Check(Arg(1, ".")));
            case "--gallery":
                return RunTool(() => Tools.Gallery(Arg(1, "docs/images"), Arg(2, "en")));
            case "--frames":
                return RunTool(() => Tools.Frames(Arg(1, "frames")));
            case "--ui-test" when args.Length >= 3:
                return RunUiShot(args[1], args[2], Arg(3, "en"));
        }
        return RunGui();
    }

    [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

    // This is a window app; to print to the terminal that started it, attach to that terminal's console.
    static int RunTool(Func<int> run)
    {
        AttachConsole(-1);
        try { return run(); }
        catch (Exception ex) { System.Console.Error.WriteLine(ex.Message); return 2; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int RunGui()
    {
        using var mutex = new Mutex(true, "Local\\claude-a-gotchi.single", out bool first);
        if (!first) return 0;
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, e) => AppPaths.LogLine("Error: " + e.Exception);
        Migration.Run();
        Loc.Load();
        var st = Settings.Load();
        Loc.Use(st.Language);
        Costumes.Load();
        Application.Run(new PetForm(st));
        return 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int RunUiShot(string which, string path, string lang)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        return Tools.UiShot(which, path, lang);
    }
}
