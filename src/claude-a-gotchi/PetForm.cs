using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;
using static ClaudeAGotchi.Sprites;

namespace ClaudeAGotchi;

sealed class Platform
{
    public string Id;
    public double H, X1, X2;
    public bool Work, Home;
    public int WinLeft;
    public bool Ok => X2 - X1 > 12;
}

sealed class JumpState
{
    public double X0, H0, X1, H1, T, Dur, A;
    public string To;
    public Action OnLand;
}

sealed class Particle { public string T; public double X, H, Vx, Vh, Life, Age; }

sealed record LimitInfo(double? P5, bool Est5, double? PW, bool EstW, string Reset, string Tokens, bool Reached);

// A see-through, borderless window in the bottom right corner of the chosen screen.
// Only Clawd, his bed and the limit plate are visible and clickable; clicks pass through everything else.
sealed class PetForm : Form
{
    const double AskAngryMs = 60_000;

    readonly Settings st;
    readonly bool demo;   // for screenshots: fixed example values instead of real data
    readonly Art art;
    readonly Random rng = new();
    readonly Stopwatch sw = Stopwatch.StartNew();
    readonly System.Windows.Forms.Timer timer = new();
    NotifyIcon tray;
    QuickPanel panel;
    StudioForm studio;
    WelcomeForm welcome;
    double panelClosedAt = -1e9;
    LayeredSurface surf;   // whole work area as a picture with alpha; the window shows the part "view" of it
    Bitmap buf;
    Graphics bg;
    bool viewMoved = true; // window moved or resized: send everything with the next frame

    int W, H, k = 3;
    double s = 1, bedL, standX, last, homeL, homeTop;
    Rectangle prevDirty;
    bool fullRedraw = true;
    Rectangle area;   // whole work area of the screen (screen coordinates)
    Rectangle view;   // the part of it the window covers right now (local coordinates)
    DateTimeOffset? glassReset;
    double flipStart = -1e9;
    readonly Dictionary<string, Platform> plats = new();
    Platform Floor => plats["floor"];

    // Figure
    double px, ph, actT = 2500, walkTo, squashUntil, stretchUntil, nextHop;
    string plat = "floor", act = "stand";
    int dir = -1;
    JumpState jump;
    string ss = "awake";
    double tuckT, bedT = 18;
    string mode = "idle";
    double askSince, lastEvent;
    string lastCwd = "";
    string ovE;
    double ovStart, ovUntil, blinkUntil, nextBlink, nextGlance;
    int glX, glY;
    Point? mouse;
    double hoverStart;
    bool inPet;
    readonly List<double> clicks = new();
    readonly List<Particle> parts = new();
    double nextFx;

    // Picking up and throwing
    bool held, flying, mouseIsDown;
    double grabDx, grabDh, flyVx, flyVh, flyTopH;
    int bounces;
    Point downPt;
    string downTarget = "";
    readonly List<(double T, double X, double H)> trail = new();

    // Data
    UsageInfo usage = new();
    long localTokens = -1;
    readonly LocalLog localLog = new();
    DateTimeOffset? localLimit, reset5;
    double? est5, estW;
    string localLimitType = "";
    bool scanning;
    double nextUsage, nextLocal, nextPlat, nextTop, nextState, nextUpdate = 30_000;
    long lastStateT;
    bool hiddenForFullscreen;
    IntPtr lastFg;
    Native.RECT lastFgRect;

    bool Lying => ss == "tuck" || ss == "sleep" || ss == "untuck";
    bool PanelOpen => panel != null && !panel.IsDisposed;
    bool LimitReached => localLimit.HasValue && localLimit.Value > DateTimeOffset.UtcNow;
    bool WantSleep => st.Napping || LimitReached || (st.UsageEnabled && usage.FiveHour >= 100);
    double Now => sw.Elapsed.TotalMilliseconds;
    // Sad and sweating from the threshold in the settings (100 = never)
    bool Worried => st.SadAt < 100 && Limits().P5 is double p && p >= st.SadAt;

    public Settings St => st;
    public bool Demo => demo;

    public PetForm(Settings settings = null, bool demo = false)
    {
        st = settings ?? Settings.Load();
        this.demo = demo;
        if (demo)
        {
            usage = new UsageInfo { FiveHour = 42, Week = 18, FiveReset = DateTimeOffset.UtcNow.AddMinutes(133), Status = "ok" };
            localTokens = 1_234_567;
            mode = "work";
        }
        art = new Art(st);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Text = AppInfo.Name;

        var icon = MakeIcon();
        Icon = icon;
        tray = new NotifyIcon { Icon = icon, Text = AppInfo.Name, Visible = true };
        tray.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right) TogglePanel(Cursor.Position); };
        timer.Interval = 33;
        timer.Tick += (_, _) => SafeTick();
        MouseDown += OnDown;
        MouseMove += OnMove;
        MouseUp += OnUp;
        DpiChanged += (_, e) => { e.Cancel = true; BeginInvoke(new Action(DoLayout)); };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnPrefChanged;
    }

    void OnDisplayChanged(object sender, EventArgs e) { if (IsHandleCreated) BeginInvoke(new Action(DoLayout)); }
    void OnPrefChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e) => Theme.Apply();

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        // WS_EX_LAYERED without a color key: the content comes through UpdateLayeredWindow, so size, position and picture
        // change together. With a color key the enlarged window was black for a moment (the screen flashed).
        get { var cp = base.CreateParams; cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_LAYERED; return cp; }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        DoLayout();
        px = Floor.X2 - 50 * s;
        try
        {
            if (File.Exists(AppPaths.State))
            {
                using var d = JsonDocument.Parse(File.ReadAllText(AppPaths.State));
                lastStateT = d.RootElement.GetProperty("t").GetInt64();
                if (d.RootElement.TryGetProperty("cwd", out var c)) lastCwd = c.GetString() ?? "";
            }
        }
        catch { }
        AppPaths.LogLine("Start " + AppInfo.VersionText + ", work area " + area + ", pixel size " + k);
        timer.Start();
        if (!st.Welcomed) BeginInvoke(new Action(OpenWelcome));
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnPrefChanged;
        if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
        if (disposing) { bg?.Dispose(); bg = null; surf?.Dispose(); surf = null; buf = null; }
        base.Dispose(disposing);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }   // drawing happens through surf.Present

    // ---------- Layout ----------
    Screen TargetScreen()
    {
        var all = Screen.AllScreens;
        return all.FirstOrDefault(x => x.DeviceName == st.ScreenName)
            ?? all.FirstOrDefault(x => !x.Primary)
            ?? Screen.PrimaryScreen;
    }

    public string ScreenDevice => TargetScreen().DeviceName;

    void DoLayout()
    {
        var sc = TargetScreen();
        var wa = sc.WorkingArea;
        s = Native.ScreenDpi(sc) / 96.0;
        // Positions are computed on the whole screen (above the taskbar), so Clawd can be carried anywhere.
        // The window itself mostly covers only his home in the bottom right corner and grows to full screen when needed.
        area = wa;
        W = wa.Width; H = wa.Height;
        k = Math.Max(1, (int)Math.Round(Sizes[Sizes.ContainsKey(st.Size) ? st.Size : "small"] * s));
        art.K = k;
        bg?.Dispose(); surf?.Dispose();
        surf = new LayeredSurface(W, H);
        buf = surf.Bitmap;
        bg = Graphics.FromImage(buf);
        bg.Clear(Color.Transparent);
        art.G = bg;
        bedL = Math.Round(W - 24 * s - 30 * k);
        standX = bedL - 13 * k;
        homeL = Math.Max(12 * k, W - 640 * s + 12 * k);
        homeTop = Math.Min(H, 560 * s);
        plats["floor"] = new Platform { Id = "floor", H = 0, X1 = 12 * k + 8 * s, X2 = standX, Home = true };
        fullRedraw = true;
        view = Rectangle.Empty;
        ApplyView(HomeView());
        RefreshPlatforms();
        if (plat == "floor") px = Math.Clamp(px, Floor.X1, Math.Max(Floor.X1, Floor.X2));
    }

    Rectangle HomeView() => Rectangle.FromLTRB(
        Math.Max(0, W - (int)(640 * s) - 24 * k), Math.Max(0, H - (int)homeTop - 48 * k), W, H);

    void ApplyView(Rectangle v)
    {
        if (v == view) return;
        view = v;
        viewMoved = true;   // the next Present applies position and size together with the picture
    }

    Point Local(Point p) => new(p.X + view.X, p.Y + view.Y);

    void RefreshPlatforms()
    {
        var fb = area;
        int rightLimit = fb.Left + (int)(bedL - 4 * s);
        var edges = WindowScanner.Scan(Handle, fb, 34 * k, (int)(40 * s), rightLimit, 26 * k);
        var seen = new HashSet<string> { "floor" };
        foreach (var e in edges)
        {
            string id = "w:" + e.Hwnd.ToInt64();
            seen.Add(id);
            double h = fb.Bottom - e.Top;
            if (plats.TryGetValue(id, out var old) && plat == id && jump == null && !held && !flying && ss == "awake")
            {
                px += e.WinLeft - old.WinLeft;   // rides along when the window is moved
                ph = h;
            }
            double x1 = e.A - fb.Left + 12 * k, x2 = e.B - fb.Left - 12 * k;
            plats[id] = new Platform { Id = id, H = h, X1 = x1, X2 = x2, Work = e.Work, WinLeft = e.WinLeft, Home = x2 >= homeL + 20 * s && h <= homeTop };
        }
        foreach (var id in plats.Keys.Where(x => !seen.Contains(x)).ToList()) plats.Remove(id);
        if (jump == null && !held && !flying && ss == "awake" && plat != "floor")
        {
            if (!plats.TryGetValue(plat, out var cur) || !cur.Ok || px < cur.X1 - 4 * s || px > cur.X2 + 4 * s) Fall();
        }
    }

    void Fall()
    {
        var below = plats.Values
            .Where(p => p.Ok && p.Id != plat && p.H < ph - 2 && px >= p.X1 && px <= p.X2)
            .OrderByDescending(p => p.H).FirstOrDefault() ?? Floor;
        StartJump(Math.Clamp(px, below.X1, Math.Max(below.X1, below.X2)), below.Id);
    }

    // ---------- Main loop ----------
    void SafeTick()
    {
        try { Tick(); }
        catch (Exception ex) { AppPaths.LogLine("Tick error: " + ex); }
    }

    void Tick()
    {
        double now = Now;
        double dt = Math.Min(50, now - last);
        last = now;

        // Check the active window every frame: maximized, restored, moved? Then react right away.
        var fg = Native.GetForegroundWindow();
        Native.GetWindowRect(fg, out var fr);
        if (fg != lastFg || fr.Left != lastFgRect.Left || fr.Top != lastFgRect.Top || fr.Right != lastFgRect.Right || fr.Bottom != lastFgRect.Bottom)
        {
            lastFg = fg; lastFgRect = fr;
            CheckFullscreen();
            nextPlat = 0;
        }
        if (now > nextTop)
        {
            nextTop = now + 1500;
            CheckFullscreen();
            if (!hiddenForFullscreen) Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
        if (now > nextPlat) { nextPlat = now + 700; RefreshPlatforms(); }
        if (now > nextState) { nextState = now + 400; PollState(now); }
        if (st.UsageEnabled && now > nextUsage) FetchUsage(now);
        if (now > nextLocal && !scanning) ScanLocal(now);
        if (now > nextUpdate) { nextUpdate = now + 3_600_000; Updates.CheckAsync(st, () => { if (IsHandleCreated) BeginInvoke(new Action(() => studio?.Invalidate())); }); }

        var cp = Cursor.Position;
        mouse = new Point(cp.X - area.Left, cp.Y - area.Top);

        Step(dt, now);
        if (hiddenForFullscreen) { timer.Interval = 100; return; }
        // Window only as big as needed: small in the corner, full screen while carried, flying or away from home.
        var home = HomeView();
        // Clamp the bottom to H: on the floor the box reached 2 pixels below the screen, counted as "away",
        // and the window switched between small and full screen with every hop.
        var petR = Rectangle.FromLTRB((int)px - 16 * k, H - (int)ph - 38 * k, (int)px + 16 * k, Math.Min(H, H - (int)ph + 2 * k));
        ApplyView(held || flying || (!Lying && !home.Contains(petR)) ? new Rectangle(0, 0, W, H) : home);
        Render(now);

        bool busy = held || flying || jump != null || act == "walk" || parts.Count > 0 || ss == "toBed" || ss == "tuck" || ss == "untuck"
                    || inPet || mode != "idle" || (ovE != null && now < ovUntil) || now < squashUntil || now < stretchUntil;
        timer.Interval = busy ? 16 : (ss == "sleep" ? 100 : 66);
    }

    void FetchUsage(double now)
    {
        nextUsage = now + 300_000;
        Task.Run(async () =>
        {
            var u = await UsageService.FetchAsync();
            if (IsHandleCreated) BeginInvoke(new Action(() =>
            {
                if (u.Status != usage.Status || u.FiveHour != usage.FiveHour)
                    AppPaths.LogLine("Limit request: " + u.Status + (u.FiveHour.HasValue ? ", 5 h = " + u.FiveHour.Value.ToString("0.#") + " %" : ""));
                usage = u;
                if (u.FiveReset.HasValue)
                {
                    double ms = (u.FiveReset.Value - DateTimeOffset.UtcNow).TotalMilliseconds;
                    if (ms > 0 && ms < 300_000) nextUsage = Now + ms + 30_000;
                }
            }));
        });
    }

    void ScanLocal(double now)
    {
        nextLocal = now + 30_000;
        scanning = true;
        Task.Run(() =>
        {
            LocalLog.Snapshot snap = null;
            try { snap = localLog.Scan(); } catch (Exception ex) { AppPaths.LogLine("Log reading error: " + ex.Message); }
            if (IsHandleCreated) BeginInvoke(new Action(() =>
            {
                scanning = false;
                if (snap == null) return;
                if (snap.LimitUntil != localLimit || localTokens < 0)
                    AppPaths.LogLine("Local logs: " + snap.Tokens5h + " tokens in the 5 h window, estimate " +
                        (snap.Est5h.HasValue ? snap.Est5h.Value.ToString("0") + " % from " + snap.Samples + " limit hits" : "not possible") + ", " +
                        (snap.LimitUntil.HasValue ? "limit reached until " + snap.LimitUntil.Value.ToString("u") : "no limit reached"));
                localTokens = snap.Tokens5h;
                localLimit = snap.LimitUntil;
                localLimitType = snap.LimitType;
                est5 = snap.Est5h; estW = snap.EstWeek; reset5 = snap.Reset5h;
            }));
        });
    }

    void CheckFullscreen()
    {
        bool full = false;
        var fg = Native.GetForegroundWindow();
        if (fg != IntPtr.Zero && fg != Handle && fg != Native.GetShellWindow())
        {
            string cls = Native.ClassOf(fg);
            if (cls != "Progman" && cls != "WorkerW" && Native.GetWindowRect(fg, out var r))
            {
                var sb = TargetScreen().Bounds;
                full = r.Left <= sb.Left && r.Top <= sb.Top && r.Right >= sb.Right && r.Bottom >= sb.Bottom;
            }
        }
        if (full == hiddenForFullscreen) return;
        hiddenForFullscreen = full;
        if (full) Hide(); else { fullRedraw = true; viewMoved = true; Show(); }
    }

    void PollState(double now)
    {
        try
        {
            if (!File.Exists(AppPaths.State)) return;
            using var d = JsonDocument.Parse(File.ReadAllText(AppPaths.State));
            var r = d.RootElement;
            long t = r.GetProperty("t").GetInt64();
            if (t == lastStateT) return;
            lastStateT = t;
            if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - t > 120_000) return;
            string state = r.GetProperty("state").GetString();
            if (r.TryGetProperty("cwd", out var c) && !string.IsNullOrEmpty(c.GetString())) lastCwd = c.GetString();
            ApplyHookState(state, now);
        }
        catch { }
    }

    void ApplyHookState(string state, double now)
    {
        lastEvent = now;
        switch (state)
        {
            case "work": SetMode("work", now); break;
            case "ask": SetMode("ask", now); break;
            case "error": SetMode("work", now); if (ss == "awake") SetEmo("confused", 2600, now); break;
            case "done":
                SetMode("idle", now);
                if (ss == "awake" && !held) { SetEmo("star", 2600, now); Burst(); if (jump == null && !flying) StartJump(px, plat, small: true); }
                break;
            default: SetMode("idle", now); break;
        }
    }

    void SetMode(string m, double now)
    {
        if (m == "ask" && mode != "ask") { askSince = now; nextHop = now; }
        mode = m;
    }

    void SetEmo(string e, double ms, double now) { ovE = e; ovStart = now; ovUntil = now + ms; }

    string Emotion(double now)
    {
        if (ovE != null && now < ovUntil) return ovE;
        if (held) return "happy";
        if (flying) return "surprised";
        if (ss == "sleep") return "sleep";
        if (ss == "tuck") return tuckT > 0.55 ? "sleep" : "tired";
        if (ss != "awake") return "tired";
        if (inPet && hoverStart > 0 && now - hoverStart > 1200) return "love";
        if (mode == "work") return "focused";
        if (mode == "ask") return now - askSince > AskAngryMs ? "angry" : "surprised";
        if (Worried) return "sad";
        return "neutral";
    }

    (double L, double B, double Wd, double Ht) PetBox() =>
        Lying ? (bedL + 7.0 * k, 14.0 * k, 16.0 * k, 12.0 * k) : (px - 12.0 * k, ph, 24.0 * k, 19.0 * k);

    void StartJump(double x1, string to, bool small = false, double? h1 = null, Action onLand = null)
    {
        double target = h1 ?? (plats.TryGetValue(to, out var p) ? p.H : 0);
        double dh = target - ph, dist = Math.Abs(x1 - px);
        jump = new JumpState
        {
            X0 = px, H0 = ph, X1 = x1, H1 = target, To = to, OnLand = onLand,
            T = small ? -60 : -160,
            Dur = small ? 300 : 480 + Math.Abs(dh) / s * 1.2 + dist / s * 0.8,
            A = small ? 8 * s : 24 * s + Math.Max(0, dh) * 0.3,
        };
        if (Math.Abs(x1 - px) > 0.5) dir = x1 > px ? 1 : -1;
    }

    void UpdJump(double dt, double now)
    {
        var j = jump;
        j.T += dt;
        if (j.T < 0) return;
        double t = Math.Min(1, j.T / j.Dur);
        px = j.X0 + (j.X1 - j.X0) * t;
        ph = j.H0 + (j.H1 - j.H0) * t + j.A * 4 * t * (1 - t);
        if (t >= 1)
        {
            px = j.X1; ph = j.H1; plat = j.To; jump = null; squashUntil = now + 130;
            j.OnLand?.Invoke();
        }
    }

    void NextAction()
    {
        var p = plats[plat];
        double r = rng.NextDouble();
        // Put down somewhere else? Sooner or later he walks back home to the corner.
        if (plat == "floor" && px < homeL) { act = "walk"; walkTo = homeL + rng.NextDouble() * 60 * s; return; }
        if (!p.Home && r < 0.35) { Fall(); act = "stand"; actT = 2000; return; }
        if (r < 0.42) { act = "stand"; actT = 2500 + rng.NextDouble() * 4000; return; }
        if (r < 0.68) { act = "walk"; walkTo = Math.Clamp(px + (rng.NextDouble() < .5 ? -1 : 1) * (20 + rng.NextDouble() * 60) * s, p.X1, Math.Max(p.X1, p.X2)); return; }
        if (r < 0.84) { act = "sit"; actT = 3000 + rng.NextDouble() * 5000; return; }
        var c = plats.Values.Where(x => x.Ok && x.Home && x.Id != plat && Math.Abs(x.H - ph) <= 320 * s).ToList();
        if (c.Count == 0) { act = "stand"; actT = 2000; return; }
        var to = c[rng.Next(c.Count)];
        double lo = Math.Max(to.X1, homeL);
        StartJump(Math.Clamp(px + (rng.NextDouble() - .5) * 90 * s, Math.Min(lo, to.X2), to.X2), to.Id);
        act = "stand"; actT = 2500 + rng.NextDouble() * 3000;
    }

    static readonly (int, int)[] Glances = { (0, 0), (0, 0), (0, 0), (-1, 0), (1, 0), (0, -1) };

    void Step(double dt, double now)
    {
        if (now > nextBlink) { blinkUntil = now + 140; nextBlink = now + (rng.NextDouble() < .2 ? 320 : 2200 + rng.NextDouble() * 3800); }
        if (now > nextGlance) { var g = Glances[rng.Next(Glances.Length)]; glX = g.Item1; glY = g.Item2; nextGlance = now + 1500 + rng.NextDouble() * 3000; }

        // Petting: mouse resting on Clawd
        var b = PetBox();
        bool nowIn = false;
        if (mouse.HasValue && !held)
        {
            double mx = mouse.Value.X, my = H - mouse.Value.Y;
            nowIn = mx >= b.L && mx <= b.L + b.Wd && my >= b.B && my <= b.B + b.Ht;
        }
        if (nowIn && !inPet) { hoverStart = now; if (ss == "awake" && !(ovE != null && now < ovUntil) && rng.NextDouble() < 0.35) SetEmo("wink", 1000, now); }
        if (!nowIn) hoverStart = 0;
        inPet = nowIn;

        if (mode == "work" && now - lastEvent > 180_000) SetMode("idle", now);
        if (mode == "ask" && now - lastEvent > 1_800_000) SetMode("idle", now);

        if (held) StepHeld(now);
        else if (flying) StepFly(dt, now);
        else StepNormal(dt, now);

        // Blanket
        if (ss == "tuck" || ss == "untuck") bedT = 24 - 8 * Ease(tuckT);
        else if (ss == "sleep") bedT = 16 + (Math.Sin(now / 650) > 0.2 ? 1 : 0);
        else { double target = ss == "hopIn" ? 24 : 18; bedT += Math.Clamp(target - bedT, -dt / 40, dt / 40); }

        // Particles
        if (now > nextFx)
        {
            if (ss == "sleep") { parts.Add(new Particle { T = "z", X = bedL + 24 * k, H = 26 * k, Vx = 0.015 * s, Vh = 0.02 * s, Life = 2000 }); nextFx = now + 1100; }
            else if (ss == "awake" && inPet && hoverStart > 0 && now - hoverStart > 1200)
            { parts.Add(new Particle { T = "heart", X = px + (rng.NextDouble() * 12 - 6) * k, H = ph + 18 * k, Vx = (rng.NextDouble() - .5) * 0.01 * s, Vh = 0.03 * s, Life = 1400 }); nextFx = now + 450; }
            else nextFx = now + 150;
        }
        foreach (var p in parts) { p.Age += dt; p.X += p.Vx * dt; p.H += p.Vh * dt; }
        parts.RemoveAll(p => p.Age >= p.Life);
    }

    void StepNormal(double dt, double now)
    {
        bool want = WantSleep;
        if (ss == "awake" && want && jump == null) ss = "toBed";
        if (ss == "toBed")
        {
            if (!want) ss = "awake";
            else if (jump == null)
            {
                if (plat != "floor") Fall();
                else
                {
                    double d = standX - px;
                    if (Math.Abs(d) < 1.5)
                    {
                        ss = "hopIn";
                        StartJump(bedL + 15 * k, "bed", h1: 3 * k, onLand: () => { ss = "tuck"; tuckT = 0; });
                    }
                    else { dir = Math.Sign(d); px += Math.Sign(d) * Math.Min(Math.Abs(d), 0.07 * s * dt); }
                }
            }
        }
        if (ss == "tuck") { tuckT = Math.Min(1, tuckT + dt / 1000); if (tuckT >= 1) ss = "sleep"; if (!want) ss = "untuck"; }
        if (ss == "sleep" && !want) { ss = "untuck"; tuckT = 1; }
        if (ss == "untuck")
        {
            tuckT = Math.Max(0, tuckT - dt / 600);
            if (tuckT <= 0) HopOutOfBed();
        }

        if (jump != null) { UpdJump(dt, now); return; }
        if (ss != "awake") return;
        if (!plats.TryGetValue(plat, out var q) || !q.Ok) { if (plat != "floor") Fall(); return; }
        px = Math.Clamp(px, q.X1, Math.Max(q.X1, q.X2)); ph = q.H;
        if (PanelOpen) { act = "stand"; return; }
        if (mode == "work")
        {
            var wp = plats.Values.Where(p => p.Work && p.Ok && p.Home && Math.Abs(p.H - ph) <= 320 * s)
                .OrderBy(p => Math.Abs(Math.Clamp(px, p.X1, p.X2) - px)).FirstOrDefault();
            if (wp != null && wp.Id != plat && !q.Work) StartJump(Math.Clamp(px, wp.X1, wp.X2), wp.Id);
            else { act = "sit"; actT = 1500; }
        }
        else if (mode == "ask")
        {
            act = "stand";
            if (now > nextHop) { StartJump(px, plat, small: true); nextHop = now + (now - askSince > AskAngryMs ? 550 : 1200); }
        }
        else if (act == "walk")
        {
            double d = walkTo - px;
            double speed = Math.Abs(d) > 200 * s ? 0.06 : 0.022;   // long way home: a bit faster
            if (Math.Abs(d) < 0.5) { act = "stand"; actT = 2000 + rng.NextDouble() * 3000; }
            else { dir = Math.Sign(d); px += Math.Sign(d) * Math.Min(Math.Abs(d), speed * s * dt); }
        }
        else { actT -= dt; if (actT <= 0) NextAction(); }
    }

    void HopOutOfBed()
    {
        ss = "hopOut"; px = bedL + 15 * k; ph = 3 * k; plat = "bed";
        StartJump(standX, "floor", onLand: () => { ss = "awake"; stretchUntil = Now + 1000; SetEmo("happy", 1800, Now); act = "stand"; actT = 2500; });
    }

    // ---------- Picking up and throwing ----------
    void StepHeld(double now)
    {
        if (!mouse.HasValue) return;
        px = Math.Clamp(mouse.Value.X + grabDx, 12 * k, W - 12 * k);
        ph = Math.Clamp(H - mouse.Value.Y + grabDh, 0, H - 34 * k);
        trail.Add((now, px, ph));
        trail.RemoveAll(t => now - t.T > 120);
        if (trail.Count > 1) { var a = trail[^2]; if (Math.Abs(px - a.X) > 0.5) dir = px > a.X ? 1 : -1; }
    }

    void StepFly(double dt, double now)
    {
        double g = 0.0028 * s, prevH = ph;
        flyVh -= g * dt;
        px += flyVx * dt; ph += flyVh * dt;
        if (ph > flyTopH) flyTopH = ph;
        double minX = 12 * k, maxX = W - 12 * k;
        if (px < minX) { px = minX; flyVx = -flyVx * 0.5; }
        if (px > maxX) { px = maxX; flyVx = -flyVx * 0.5; }
        if (ph > H - 34 * k) { ph = H - 34 * k; flyVh = Math.Min(0, flyVh); }
        if (Math.Abs(flyVx) > 0.02) dir = flyVx > 0 ? 1 : -1;
        if (flyVh >= 0) return;

        double bedTop = 6 * k;
        if (px >= bedL + 3 * k && px <= bedL + 27 * k && prevH >= bedTop && ph <= bedTop) { LandBed(now); return; }
        foreach (var p in plats.Values.OrderByDescending(p => p.H))
            if (p.Ok && p.Id != "floor" && prevH >= p.H && ph <= p.H && px >= p.X1 - 6 * k && px <= p.X2 + 6 * k) { Land(p, now); return; }
        if (ph <= 0) Land(Floor, now);
    }

    void Land(Platform p, double now)
    {
        flying = false;
        ph = p.H; plat = p.Id;
        px = Math.Clamp(px, p.X1, Math.Max(p.X1, p.X2));
        squashUntil = now + 180;
        SetEmo(flyTopH - p.H > 180 * s ? "confused" : "happy", 1300, now);
        act = "stand"; actT = 3000;
    }

    void LandBed(double now)
    {
        bounces++;
        ph = 6 * k;
        if (bounces <= 2)
        {
            flyVh = Math.Max(0.45 * s, Math.Abs(flyVh) * 0.6) * (bounces == 1 ? 1 : 0.7);   // bounces on the mattress
            flyVx *= 0.3;
            squashUntil = now + 120;
            SetEmo("happy", 900, now);
            return;
        }
        flying = false;
        if (WantSleep) { ss = "tuck"; tuckT = 0; px = bedL + 15 * k; ph = 3 * k; plat = "bed"; }
        else
        {
            ss = "hopOut"; plat = "bed";
            StartJump(standX, "floor", onLand: () => { ss = "awake"; act = "stand"; actT = 2500; SetEmo("happy", 1500, Now); });
        }
    }

    string HitTarget(Point p)
    {
        var b = PetBox();
        double mx = p.X, my = H - p.Y;
        if (mx >= b.L && mx <= b.L + b.Wd && my >= b.B && my <= b.B + b.Ht) return "pet";
        return "bed";   // everything else visible belongs to the bed or the limit plate
    }

    void OnDown(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        mouseIsDown = true;
        downPt = Local(e.Location);
        downTarget = HitTarget(downPt);
    }

    void OnMove(object sender, MouseEventArgs e)
    {
        var p = Local(e.Location);
        Cursor = held ? Cursors.SizeAll : HitTarget(p) == "pet" ? Cursors.Hand : Cursors.Default;
        if (!mouseIsDown || held || downTarget != "pet") return;
        if (Math.Abs(p.X - downPt.X) + Math.Abs(p.Y - downPt.Y) > 5 * s) PickUp(downPt);
    }

    void OnUp(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        bool was = mouseIsDown;
        mouseIsDown = false;
        if (held) { Release(); return; }
        if (!was) return;
        if (downTarget == "bed") OpenPanelAtBed();
        else ClickPet();
    }

    void PickUp(Point at)
    {
        if (ss != "awake")
        {
            if (Lying) { px = bedL + 15 * k; ph = 6 * k; }
            ss = "awake"; tuckT = 0;
        }
        jump = null; flying = false; held = true; act = "stand";
        panel?.Close();
        grabDx = px - at.X;
        grabDh = ph - (H - at.Y);
        trail.Clear();
        SetEmo("surprised", 450, Now);
    }

    void Release()
    {
        held = false;
        double now = Now;
        flyVx = 0; flyVh = 0;
        if (trail.Count > 1)
        {
            var a = trail[0];
            double span = now - a.T;
            if (span > 8) { flyVx = (px - a.X) / span; flyVh = (ph - a.H) / span; }
        }
        double max = 2.2 * s;
        flyVx = Math.Clamp(flyVx, -max, max);
        flyVh = Math.Clamp(flyVh, -max, max);
        flying = true; bounces = 0; flyTopH = ph; plat = "air";
    }

    void ClickPet()
    {
        double now = Now;
        clicks.RemoveAll(c => now - c > 2500);
        clicks.Add(now);
        if (Lying) { SetEmo("tired", 900, now); return; }
        if (ss != "awake") return;
        if (clicks.Count >= 4)
        {
            clicks.Clear();
            SetEmo("angry", 2600, now);
        }
        else SetEmo(rng.NextDouble() < 0.3 ? "wink" : "happy", 1100, now);
        if (jump == null && !flying) StartJump(px, plat, small: true);
    }

    void OpenPanelAtBed()
    {
        int top = (st.BedFrame == "canopy" ? 64 : 49) * k;
        TogglePanel(new Point(area.Left + (int)(bedL + 15 * k), area.Top + H - top));
    }

    static double Ease(double v) => v < .5 ? 2 * v * v : 1 - Math.Pow(-2 * v + 2, 2) / 2;

    void Burst()
    {
        for (int i = 0; i < 7; i++)
            parts.Add(new Particle { T = "spark", X = px + (rng.NextDouble() * 24 - 12) * k, H = ph + (10 + rng.NextDouble() * 12) * k, Vx = (rng.NextDouble() - .5) * 0.04 * s, Vh = (0.02 + rng.NextDouble() * 0.03) * s, Life = 900 });
    }

    // ---------- Drawing ----------
    // Only the area where something is visible (bed, plate, hourglass, Clawd, particles).
    Rectangle ContentRect()
    {
        int top = (30 + 15 + 17 + 12) * k;
        var r = Rectangle.FromLTRB((int)bedL - 13 * k, Math.Max(0, H - top), (int)bedL + 36 * k, H);
        if (!Lying) r = Rectangle.Union(r, new Rectangle((int)px - 16 * k, H - (int)ph - 38 * k, 32 * k, 40 * k));
        foreach (var p in parts) r = Rectangle.Union(r, new Rectangle((int)p.X - 3 * k, H - (int)p.H - 6 * k, 8 * k, 9 * k));
        r.Intersect(new Rectangle(0, 0, W, H));
        return r;
    }

    void UpdateGlass(double now)
    {
        DateTimeOffset? r = st.UsageEnabled && usage.FiveHour.HasValue ? usage.FiveReset
                          : LimitReached && localLimitType == "five_hour" ? localLimit : reset5;
        if (r.HasValue && r.Value > DateTimeOffset.UtcNow)
        {
            // A new 5-hour window? Then the hourglass turns over.
            if (glassReset.HasValue && r.Value > glassReset.Value.AddMinutes(10)) flipStart = now;
            glassReset = r;
        }
    }

    public (double P, double Flip) Glass(double now)
    {
        double p = 1;
        if (glassReset.HasValue)
            p = Math.Clamp(1 - (glassReset.Value - DateTimeOffset.UtcNow).TotalHours / 5.0, 0, 1);
        return (p, now - flipStart);
    }

    public double NowMs => Now;

    void Render(double now)
    {
        UpdateGlass(now);
        var cur = ContentRect();
        var dirty = fullRedraw ? new Rectangle(0, 0, W, H) : Rectangle.Union(cur, prevDirty);
        fullRedraw = false;
        prevDirty = cur;
        bg.SetClip(dirty);
        bg.Clear(Color.Transparent);
        art.G = bg; art.K = k;
        art.BlinkUntil = blinkUntil; art.StretchUntil = stretchUntil; art.OvE = now < ovUntil ? ovE : null; art.OvStart = ovStart;
        int bx = (int)bedL, by = H - 30 * k;
        string emo = Emotion(now);
        var lim = Limits();
        if (Lying) art.BedWithClawd(bx, by, (int)Math.Round(bedT), emo, now);
        else art.Bed(bx, by, (int)Math.Round(bedT), null);
        var gl = Glass(now);
        art.LimitBars(bx, by, lim.P5, lim.PW, null, null, st.ShowHourglass, gl.P, gl.Flip, now);
        if (!Lying)
        {
            string pose = held ? "held" : flying ? "air" : jump != null && jump.T < 0 ? "squash" : jump != null ? "air" : now < squashUntil ? "squash" : (act == "sit" && ss == "awake") ? "sit" : "stand";
            bool walking = held || (jump == null && !flying && ((ss == "awake" && act == "walk" && !PanelOpen) || ss == "toBed"));
            art.Clawd((int)Math.Round(px), H - (int)Math.Round(ph), new DrawOpts
            {
                Emo = emo, Now = now, Dir = dir, Pose = pose, Walking = walking, Moving = walking || jump != null || flying, Gx = glX, Gy = glY,
                Typing = mode == "work" && ss == "awake" && jump == null && !held && !flying && act == "sit",
                Excl = mode == "ask" && ss == "awake" && emo != "angry" && !held,
                Sweat = Worried && ss == "awake" && mode != "ask" && !held,
            });
        }
        foreach (var p in parts)
        {
            var d = Parts[p.T];
            art.Gfx.Pat((int)Math.Round(p.X), H - (int)Math.Round(p.H), d.P, d.Pal, -1, -3, false);
        }
        bg.ResetClip();
        bg.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);
        var at = new Point(area.X + view.X, area.Y + view.Y);
        if (viewMoved)
        {
            viewMoved = false;
            surf.Present(Handle, at, view, null);   // new size/position and the finished picture in one step
            return;
        }
        dirty.Offset(-view.X, -view.Y);
        dirty.Intersect(new Rectangle(0, 0, view.Width, view.Height));
        if (!dirty.IsEmpty) surf.Present(Handle, at, view, dirty);
    }

    // ---------- Quick panel, settings ----------
    void TogglePanel(Point anchor)
    {
        if (PanelOpen) { panel.Close(); return; }
        if (Now - panelClosedAt < 250) return;   // the same click just closed the panel
        panel = new QuickPanel(this, anchor);
        panel.FormClosed += (_, _) => { panel = null; panelClosedAt = Now; };
        panel.Show();
        panel.Activate();
    }

    public string PanelEmotion
    {
        get
        {
            if (Lying) return "sleep";
            string e = Emotion(Now);
            return e == "love" || e == "wink" ? "happy" : e;
        }
    }

    public string StatusText()
    {
        if (held) return Loc.T("status.carried");
        if (ss != "awake" && ss != "toBed" && ss != "hopOut")
        {
            if (LimitReached) return Loc.T("status.sleepsUntil", LimitWhen());
            return Loc.T(st.Napping ? "status.napping" : "status.sleeping");
        }
        if (ss == "toBed") return Loc.T("status.toBed");
        if (mode == "work") return Loc.T("status.working");
        if (mode == "ask")
        {
            double min = (Now - askSince) / 60_000;
            return min >= 1 ? Loc.T("status.waitingMin", (int)min) : Loc.T("status.waiting");
        }
        return Loc.T("status.calm");
    }

    string LimitWhen()
    {
        var until = localLimit.Value.ToLocalTime();
        return until - DateTimeOffset.Now > TimeSpan.FromHours(20) ? Loc.DayTime(until) : Loc.Time(until);
    }

    public LimitInfo Limits()
    {
        double? p5, pw;
        bool e5 = false, ew = false;
        DateTimeOffset? reset;
        if (st.UsageEnabled && usage.FiveHour.HasValue) { p5 = usage.FiveHour; pw = usage.Week; reset = usage.FiveReset; }
        else { p5 = est5; e5 = p5.HasValue; pw = estW; ew = pw.HasValue; reset = reset5; }
        bool reached = LimitReached;
        if (e5 && p5 >= 99) p5 = 99;   // estimate: "full" only when the limit really hits
        if (ew && pw >= 99) pw = 99;
        if (reached)
        {
            if (localLimitType.Contains("seven")) { pw = 100; ew = false; }
            else { p5 = 100; e5 = false; }
        }
        string r = reached ? Loc.T("limit.freeAt", LimitWhen()) : reset.HasValue ? Loc.T("limit.reset", Loc.Time(reset.Value)) : Loc.T("limit.noWindow");
        string tok = localTokens >= 0 ? Loc.T("limit.tokens", Loc.Tokens(localTokens)) : Loc.T("limit.reading");
        return new LimitInfo(p5, e5, pw, ew, r, tok, reached);
    }

    public string ProjectName => demo ? "my-project" : Directory.Exists(lastCwd) ? Path.GetFileName(lastCwd.TrimEnd('\\', '/')) : Loc.T("project.home");

    public void Cheer() { if (ss == "awake" && !held) SetEmo("happy", 1400, Now); }

    public void ToggleNap() { st.Napping = !st.Napping; st.Save(); }

    public void Relayout() { DoLayout(); }

    public void UsageToggled() { if (st.UsageEnabled) nextUsage = 0; }

    public void ChangeScreen(string device)
    {
        st.ScreenName = device; st.Save();
        plat = "floor"; jump = null; held = false; flying = false; ph = 0;
        DoLayout();
        px = Floor.X2 - 50 * s;
    }

    public void OpenStudio(int tab)
    {
        if (studio != null && !studio.IsDisposed) { studio.ShowTab(tab); studio.Activate(); return; }
        studio = new StudioForm(this, tab);
        studio.FormClosed += (_, _) => studio = null;
        studio.Show();
        studio.Activate();
    }

    public void OpenWelcome()
    {
        if (welcome != null && !welcome.IsDisposed) { welcome.Activate(); return; }
        welcome = new WelcomeForm(this);
        welcome.FormClosed += (_, _) => { welcome = null; st.Welcomed = true; st.Save(); };
        welcome.Show();
        welcome.Activate();
    }

    // Called after the language was changed in the settings or the welcome window.
    public void ApplyLanguage()
    {
        Loc.Use(st.Language);
        fullRedraw = true;
        panel?.Close();
    }

    public void Quit()
    {
        studio?.Close();
        welcome?.Close();
        Close();
    }

    // Opens the Claude desktop app: Store install, classic install, the claude:// link, or claude.ai as a last resort.
    public void OpenChat()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        try
        {
            if (Directory.Exists(Path.Combine(local, "Packages", "Claude_pzs8sxrjxfjjc")))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", @"shell:AppsFolder\Claude_pzs8sxrjxfjjc!Claude") { UseShellExecute = false });
                return;
            }
            string classic = Path.Combine(local, "AnthropicClaude", "claude.exe");
            if (File.Exists(classic)) { Process.Start(new ProcessStartInfo(classic) { UseShellExecute = true }); return; }
            try { Process.Start(new ProcessStartInfo("claude://") { UseShellExecute = true }); }
            catch { Process.Start(new ProcessStartInfo("https://claude.ai") { UseShellExecute = true }); }
        }
        catch (Exception ex) { Notify(Loc.T("error.chat", ex.Message)); }
    }

    public void OpenCode()
    {
        string dir = Directory.Exists(lastCwd) ? lastCwd : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        try
        {
            var psi = new ProcessStartInfo("wt.exe") { UseShellExecute = false };
            foreach (var a in new[] { "-d", dir, "powershell", "-NoExit", "-Command", "claude" }) psi.ArgumentList.Add(a);
            Process.Start(psi);
        }
        catch
        {
            try
            {
                var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = true, WorkingDirectory = dir };
                psi.ArgumentList.Add("-NoExit"); psi.ArgumentList.Add("-Command"); psi.ArgumentList.Add("claude");
                Process.Start(psi);
            }
            catch (Exception ex) { Notify(Loc.T("error.terminal", ex.Message)); }
        }
    }

    public string ToggleHooks()
    {
        try
        {
            if (HookBridge.IsInstalled())
            {
                HookBridge.Uninstall();
                return Loc.T("hooks.removed");
            }
            HookBridge.Install(AppInfo.ExePath);
            return Loc.T("hooks.added");
        }
        catch (Exception ex) { return Loc.T("error.generic", ex.Message); }
    }

    public string SetAutostart(bool on)
    {
        try { Autostart.Set(on); return Loc.T(on ? "autostart.on" : "autostart.off"); }
        catch (Exception ex) { return Loc.T("error.generic", ex.Message); }
    }

    void Notify(string text) => tray?.ShowBalloonTip(5000, AppInfo.Name, text, ToolTipIcon.Info);

    static Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using var br = new SolidBrush(C("#da7756"));
            using var bk = new SolidBrush(Color.Black);
            int ox = 4, oy = 8;
            g.FillRectangle(br, ox + 4, oy, 16, 12);
            g.FillRectangle(br, ox, oy + 4, 4, 4); g.FillRectangle(br, ox + 20, oy + 4, 4, 4);
            foreach (int lx in new[] { 4, 8, 14, 18 }) g.FillRectangle(br, ox + lx, oy + 12, 2, 4);
            g.FillRectangle(bk, ox + 6, oy + 2, 2, 2); g.FillRectangle(bk, ox + 16, oy + 2, 2, 2);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
