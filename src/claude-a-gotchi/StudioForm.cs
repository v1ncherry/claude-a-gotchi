using System.Diagnostics;
using System.Drawing.Drawing2D;
using static ClaudeAGotchi.Sprites;

namespace ClaudeAGotchi;

// Settings window: live preview on the left; look, bed and settings on the right.
sealed class StudioForm : PaintedForm
{
    public const int Wd = 780, Ht = 640;

    readonly PetForm pet;
    readonly Art art;
    readonly Stopwatch sw = Stopwatch.StartNew();
    readonly System.Windows.Forms.Timer anim = new() { Interval = 100 };
    double nextBlink = 1200, happyUntil, sadPreviewUntil;
    RectangleF preview;
    bool hooks, auto;
    string msg = "";
    public int Tab;

    Settings St => pet.St;

    public StudioForm(PetForm p, int tab)
    {
        pet = p;
        Tab = tab;
        art = new Art(p.St);
        Text = AppInfo.Name;
        Icon = p.Icon;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        hooks = HookBridge.IsInstalled();
        try { auto = Autostart.Enabled(); } catch { }
        anim.Tick += (_, _) =>
        {
            double now = sw.Elapsed.TotalMilliseconds;
            if (now > nextBlink) { art.BlinkUntil = now + 140; nextBlink = now + 2200 + Random.Shared.NextDouble() * 3500; }
            Invalidate(Rectangle.Round(preview));
        };
        DpiChanged += (_, _) => BeginInvoke(new Action(() => { ClientSize = new Size((int)S(Wd), (int)S(Ht)); Invalidate(); }));
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ClientSize = new Size((int)S(Wd), (int)S(Ht));
        CenterToScreen();
        anim.Start();
    }

    // Coming back to the window reloads the costume files, so own costumes show up without a restart.
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        Costumes.Load();
        Invalidate();
    }

    protected override void OnFormClosed(FormClosedEventArgs e) { anim.Dispose(); base.OnFormClosed(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); base.OnKeyDown(e); }

    public void ShowTab(int tab) { Tab = tab; msg = ""; Invalidate(); }

    protected override void OnSlideDone() => St.Save();

    void Changed()
    {
        St.Save();
        happyUntil = sw.Elapsed.TotalMilliseconds + 1300;
        pet.Cheer();
    }

    static string ItemName(string type, string id) =>
        id == "none" ? Costumes.NoneName(type) : Costumes.GetOr(type, id)?.Name ?? id;

    protected override void PaintUi(Graphics g)
    {
        float W = ClientSize.Width, H = ClientSize.Height;
        double now = sw.Elapsed.TotalMilliseconds;

        // Preview
        preview = new RectangleF(S(16), S(16), S(300), H - S(32));
        using (var path = Ui.Round(preview, S(12)))
        {
            using (var lg = new LinearGradientBrush(preview, Theme.Hex("#3f7f6b"), Theme.Hex("#1d4744"), 90f)) g.FillPath(lg, path);
            float floorY = preview.Bottom - S(70);
            g.SetClip(path);
            using (var fb = new SolidBrush(Theme.Hex("#16201e"))) g.FillRectangle(fb, preview.X, floorY, preview.Width, preview.Bottom - floorY);
            PixelMode(g, true);
            art.G = g;
            string emo = now < happyUntil ? "happy" : "neutral";
            if (Tab == 1)
            {
                art.K = Math.Max(1, (int)Math.Round(6 * Sc));
                // Center bed and plate together: with text and hourglass the plate reaches 11 pixels left of the bed
                int mid = St.BarStyle != "bars" && St.ShowHourglass ? 11 : 15;
                int bx = (int)(preview.X + preview.Width / 2) - mid * art.K, by = (int)floorY - 30 * art.K;
                art.BedWithClawd(bx, by, 16, "sleep", now);
                var lim = pet.Limits();
                var gl = pet.Glass(pet.NowMs);
                art.LimitBars(bx, by, lim.P5, lim.PW, null, null, St.ShowHourglass, gl.P, gl.Flip, now);
            }
            else
            {
                art.K = Math.Max(1, (int)Math.Round((Tab == 0 ? 7 : 6) * Sc));
                bool sad = Tab == 2 && now < sadPreviewUntil;
                art.Clawd((int)(preview.X + preview.Width / 2), (int)floorY, new DrawOpts
                { Emo = sad ? "sad" : Tab == 2 ? "happy" : emo, Sweat = sad, Now = now, Dir = 1 });
            }
            PixelMode(g, false);
            g.ResetClip();
        }
        string cap = Tab == 1 ? Loc.T("frame." + St.BedFrame) : "Clawd";
        string sub = Tab == 1 ? ItemName("cover", St.BedCover) + " · " + Loc.T("bed.pillowShort", ItemName("pillow", St.Pillow))
                   : ItemName("hat", St.Hat) + " · " + ItemName("outfit", St.Outfit) + " · " + ItemName("color", St.BodyColor);
        Ui.Text(g, cap, Ui.F(S(14), true), Color.White, new RectangleF(preview.X + S(16), preview.Bottom - S(54), preview.Width - S(32), S(20)), TextFormatFlags.HorizontalCenter);
        Ui.Text(g, sub, Ui.F(S(11.5f)), Theme.Hex("#b9cfc8"), new RectangleF(preview.X + S(16), preview.Bottom - S(32), preview.Width - S(32), S(18)), TextFormatFlags.HorizontalCenter);

        // Right side
        float x0 = S(332), cw = W - x0 - S(16), y = S(16);
        Segmented(g, new RectangleF(x0, y, cw, S(40)), new[] { Loc.T("tab.look"), Loc.T("tab.bed"), Loc.T("tab.settings") }, Tab, i => { Tab = i; msg = ""; });
        y += S(40) + S(20);
        if (Tab == 0) PaintLook(g, x0, y, cw, now);
        else if (Tab == 1) PaintBed(g, x0, y, cw);
        else PaintSettings(g, x0, y, cw, H);
    }

    void Section(Graphics g, string title, float x, ref float y, float w)
    {
        Ui.Text(g, title, Ui.F(S(12.5f), true), Theme.Text, new RectangleF(x, y, w, S(18)), TextFormatFlags.Left | TextFormatFlags.Top);
        y += S(26);
    }

    void Tile(Graphics g, RectangleF r, bool sel, Action click, Action<RectangleF> content)
    {
        bool hot = Hot(r);
        Ui.Fill(g, r, S(10), hot ? Theme.Mix(Theme.Card, Theme.Text, 0.06) : Theme.Card);
        using (var path = Ui.Round(r, S(10)))
        {
            g.SetClip(path);
            PixelMode(g, true);
            content(r);
            PixelMode(g, false);
            g.ResetClip();
        }
        if (sel) Ui.Stroke(g, RectangleF.Inflate(r, -S(1), -S(1)), S(9), Theme.Accent, S(2));
        else Ui.Stroke(g, r, S(10), hot ? Theme.Muted : Theme.Border);
        Hit(r, click);
    }

    void PaintLook(Graphics g, float x0, float y, float cw, double now)
    {
        float tile = S(52), gap = S(8);
        int k2 = Math.Max(1, (int)Math.Round(2 * Sc)), perRow = 7;

        Section(g, Loc.T("look.hat"), x0, ref y, cw);
        var hats = Costumes.Hats;
        for (int i = 0; i < hats.Count; i++)
        {
            string id = hats[i].Id;
            var r = new RectangleF(x0 + (i % perRow) * (tile + gap), y + (i / perRow) * (tile + gap), tile, tile);
            Tile(g, r, St.Hat == id, () => { St.Hat = id; Changed(); }, rr =>
            {
                art.K = k2;
                art.Clawd((int)(rr.X + rr.Width / 2), (int)rr.Bottom + 2 * k2, new DrawOpts { Hat = id, Outfit = "none", Now = now, Dir = 1 });
            });
        }
        y += Rows(hats.Count, perRow) * (tile + gap) - gap + S(18);

        Section(g, Loc.T("look.outfit"), x0, ref y, cw);
        var outfits = Costumes.Outfits;
        for (int i = 0; i < outfits.Count; i++)
        {
            string id = outfits[i].Id;
            var r = new RectangleF(x0 + (i % perRow) * (tile + gap), y + (i / perRow) * (tile + gap), tile, tile);
            Tile(g, r, St.Outfit == id, () => { St.Outfit = id; Changed(); }, rr =>
            {
                art.K = k2;
                art.Clawd((int)(rr.X + rr.Width / 2), (int)(rr.Bottom - S(5)), new DrawOpts { Hat = "none", Outfit = id, Emo = "happy", Now = now, Dir = 1 });
            });
        }
        y += Rows(outfits.Count, perRow) * (tile + gap) - gap + S(18);

        Section(g, Loc.T("look.color", ItemName("color", St.BodyColor)), x0, ref y, cw);
        float d = S(34);
        var colors = Costumes.Colors;
        int cpr = Math.Max(1, (int)((cw + S(12)) / (d + S(12))));
        for (int i = 0; i < colors.Count; i++)
        {
            string id = colors[i].Id;
            var r = new RectangleF(x0 + (i % cpr) * (d + S(12)), y + (i / cpr) * (d + S(12)), d, d);
            bool sel = St.BodyColor == id, hot = Hot(r);
            if (sel || hot)
                using (var pen = new Pen(sel ? Theme.Accent : Theme.Muted, S(2))) g.DrawEllipse(pen, RectangleF.Inflate(r, S(3), S(3)));
            using (var b = new SolidBrush(colors[i].Body)) g.FillEllipse(b, r);
            Hit(RectangleF.Inflate(r, S(4), S(4)), () => { St.BodyColor = id; Changed(); });
        }
        y += Rows(colors.Count, cpr) * (d + S(12)) - S(12) + S(20);

        Section(g, Loc.T("look.size"), x0, ref y, cw);
        int sel2 = Array.IndexOf(SizeOrder, St.Size);
        Segmented(g, new RectangleF(x0, y, cw, S(38)), SizeOrder.Select(s => Loc.T("size." + s)).ToArray(), sel2,
            i => { St.Size = SizeOrder[i]; Changed(); pet.Relayout(); });
        y += S(38) + S(14);

        Link(g, Loc.T("look.ownCostumes"), x0, y, Theme.Accent, OpenCostumeFolder);
    }

    static int Rows(int n, int perRow) => Math.Max(1, (n + perRow - 1) / perRow);

    // Opens the folder for own costume files (and creates it with a short how-to the first time).
    static void OpenCostumeFolder()
    {
        try
        {
            Directory.CreateDirectory(Costumes.UserFolder);
            string readme = Path.Combine(Costumes.UserFolder, "README.txt");
            if (!File.Exists(readme))
                File.WriteAllText(readme, Loc.T("costumes.readme", AppInfo.RepoUrl + "/blob/main/CONTRIBUTING.md"));
            Process.Start("explorer.exe", Costumes.UserFolder);
        }
        catch { }
    }

    void PaintBed(Graphics g, float x0, float y, float cw)
    {
        int k2 = Math.Max(1, (int)Math.Round(2 * Sc));
        Section(g, Loc.T("bed.frame", Loc.T("frame." + St.BedFrame)), x0, ref y, cw);
        float bw = (cw - 3 * S(10)) / 4, bh = S(104);
        for (int i = 0; i < FrameOrder.Length; i++)
        {
            string id = FrameOrder[i];
            var r = new RectangleF(x0 + i * (bw + S(10)), y, bw, bh);
            Tile(g, r, St.BedFrame == id, () => { St.BedFrame = id; Changed(); }, rr =>
            {
                art.K = k2;
                int bx = (int)(rr.X + rr.Width / 2) - 15 * k2, by = (int)(rr.Bottom - S(8)) - 30 * k2;
                art.Bed(bx, by, 18, null, id);
            });
            Ui.Text(g, Loc.T("frame." + id), Ui.F(S(11.5f)), St.BedFrame == id ? Theme.Text : Theme.Muted,
                new RectangleF(r.X, r.Bottom + S(4), r.Width, S(18)), TextFormatFlags.HorizontalCenter);
        }
        y += bh + S(36);

        Section(g, Loc.T("bed.cover", ItemName("cover", St.BedCover)), x0, ref y, cw);
        const int cpr = 6;
        float cwid = (cw - (cpr - 1) * S(8)) / cpr, ch = S(52);
        int k4 = Math.Max(1, (int)Math.Round(4 * Sc));
        var covers = Costumes.Covers;
        for (int i = 0; i < covers.Count; i++)
        {
            string id = covers[i].Id;
            var r = new RectangleF(x0 + (i % cpr) * (cwid + S(8)), y + (i / cpr) * (ch + S(8)), cwid, ch);
            Tile(g, r, St.BedCover == id, () => { St.BedCover = id; Changed(); }, rr =>
            {
                art.K = k4;
                art.CoverArea((int)rr.X, (int)rr.Y, id, 0, 0, (int)(rr.Width / k4) + 1, (int)(rr.Height / k4) + 1, true);
            });
        }
        y += Rows(covers.Count, cpr) * (ch + S(8)) - S(8) + S(20);

        Section(g, Loc.T("bed.pillow", ItemName("pillow", St.Pillow)), x0, ref y, cw);
        const int ppr = 4;
        float pw = (cw - (ppr - 1) * S(10)) / ppr, phh = S(42);
        var pillows = Costumes.Pillows;
        for (int i = 0; i < pillows.Count; i++)
        {
            var pc = pillows[i];
            string id = pc.Id;
            var r = new RectangleF(x0 + (i % ppr) * (pw + S(10)), y + (i / ppr) * (phh + S(8)), pw, phh);
            bool sel = St.Pillow == id, hot = Hot(r);
            Ui.Fill(g, r, S(10), hot ? Theme.Mix(Theme.Card, Theme.Text, 0.06) : Theme.Card);
            if (sel) Ui.Stroke(g, RectangleF.Inflate(r, -S(1), -S(1)), S(9), Theme.Accent, S(2)); else Ui.Stroke(g, r, S(10), Theme.Border);
            var sw2 = new RectangleF(r.X + S(10), r.Y + S(12), S(26), S(18));
            Ui.Fill(g, sw2, S(5), pc.Main);
            Ui.Fill(g, new RectangleF(sw2.X + S(2), sw2.Bottom - S(4), sw2.Width - S(4), S(3)), S(1.5f), pc.Shade);
            Ui.Text(g, pc.Name, Ui.F(S(12)), Theme.Text, new RectangleF(sw2.Right + S(8), r.Y, r.Right - sw2.Right - S(10), r.Height));
            Hit(r, () => { St.Pillow = id; Changed(); });
        }
        y += Rows(pillows.Count, ppr) * (phh + S(8)) - S(8) + S(20);

        Section(g, Loc.T("bed.display"), x0, ref y, cw);
        float segW = cw - S(150);
        Segmented(g, new RectangleF(x0, y, segW, S(38)), new[] { Loc.T("bed.textAndPercent"), Loc.T("bed.barsOnly") }, St.BarStyle == "bars" ? 1 : 0,
            i => { St.BarStyle = i == 1 ? "bars" : "text"; Changed(); });
        var tl = new RectangleF(x0 + segW + S(16), y, S(80), S(38));
        Ui.Text(g, Loc.T("bed.hourglass"), Ui.F(S(12.5f)), Theme.Text, tl);
        Toggle(g, new RectangleF(x0 + cw - S(44), y + S(8), S(44), S(22)), St.ShowHourglass, () => { St.ShowHourglass = !St.ShowHourglass; Changed(); });
    }

    void Row(Graphics g, float x0, ref float y, float cw, string title, string desc, bool on, Action toggle)
    {
        var r = new RectangleF(x0, y, cw, S(60));
        Ui.Fill(g, r, S(10), Theme.Card);
        Ui.Stroke(g, r, S(10), Theme.Border);
        Ui.Text(g, title, Ui.F(S(13), true), Theme.Text, new RectangleF(r.X + S(16), r.Y + S(10), r.Width - S(100), S(20)));
        Ui.Text(g, desc, Ui.F(S(11.5f)), Theme.Muted, new RectangleF(r.X + S(16), r.Y + S(31), r.Width - S(100), S(20)));
        Toggle(g, new RectangleF(r.Right - S(16) - S(44), r.Y + S(19), S(44), S(22)), on, toggle);
        y += S(60) + S(8);
    }

    void PaintSettings(Graphics g, float x0, float y, float cw, float H)
    {
        Row(g, x0, ref y, cw, Loc.T("settings.hooks"), Loc.T("settings.hooksDesc"), hooks,
            () => { msg = pet.ToggleHooks(); hooks = HookBridge.IsInstalled(); });
        Row(g, x0, ref y, cw, Loc.T("settings.usage"), Loc.T("settings.usageDesc"), St.UsageEnabled,
            () => { St.UsageEnabled = !St.UsageEnabled; St.Save(); pet.UsageToggled(); });
        Row(g, x0, ref y, cw, Loc.T("settings.autostart"), Loc.T("settings.autostartDesc"), auto,
            () => { msg = pet.SetAutostart(!auto); try { auto = Autostart.Enabled(); } catch { } });

        y += S(4);
        string thr = St.SadAt >= 100 ? Loc.T("settings.never") : Loc.T("settings.fromPercent", St.SadAt);
        Section(g, Loc.T("settings.sad", thr), x0, ref y, cw);
        Slider(g, new RectangleF(x0 + S(10), y, cw - S(20), S(24)), (St.SadAt - 50) / 50f, v =>
        {
            St.SadAt = 50 + (int)Math.Round(v * 50);
            sadPreviewUntil = sw.Elapsed.TotalMilliseconds + 1500;
        });
        y += S(28);
        Ui.Text(g, Loc.T("settings.sadHint"), Ui.F(S(11.5f)), Theme.Muted, new RectangleF(x0, y, cw, S(18)));
        y += S(32);

        Section(g, Loc.T("settings.screen"), x0, ref y, cw);
        var screens = Screen.AllScreens;
        var labels = ScreenLabels(pet, screens, out int sel);
        Segmented(g, new RectangleF(x0, y, cw, S(38)), labels, sel, i => pet.ChangeScreen(screens[i].DeviceName));
        y += S(38) + S(16);

        Section(g, Loc.T("settings.language"), x0, ref y, cw);
        var langs = Loc.Available;
        var langLabels = new[] { Loc.T("settings.langAuto") }.Concat(langs.Select(l => l.Name)).ToArray();
        int lsel = St.Language == "auto" ? 0 : 1 + langs.ToList().FindIndex(l => l.Code == St.Language);
        Segmented(g, new RectangleF(x0, y, cw, S(38)), langLabels, Math.Max(0, lsel), i =>
        {
            St.Language = i == 0 ? "auto" : langs[i - 1].Code;
            St.Save();
            pet.ApplyLanguage();
        });
        y += S(38) + S(16);

        float half = (cw - S(10)) / 2;
        Button(g, new RectangleF(x0, y, half, S(38)), Loc.T("settings.dataFolder"), null, false, () =>
        {
            Directory.CreateDirectory(AppPaths.Data);
            Process.Start("explorer.exe", AppPaths.Data);
        });
        var quit = new RectangleF(x0 + half + S(10), y, half, S(38));
        bool hot = Hot(quit);
        Ui.Fill(g, quit, S(8), hot ? Theme.Mix(Theme.Card2, Theme.Danger, 0.15) : Theme.Card2);
        Ui.Stroke(g, quit, S(8), Theme.Border);
        Ui.Text(g, Loc.T("settings.quit"), Ui.F(S(13), true), Theme.Danger, quit, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        Hit(quit, () => pet.Quit());
        y += S(38) + S(10);
        if (msg.Length > 0)
            Ui.Text(g, msg, Ui.F(S(12)), Theme.Accent, new RectangleF(x0, y, cw, S(34)), TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);

        // Footer: version, update, support, GitHub
        float fy = H - S(16) - S(18), fx = x0;
        Ui.Text(g, AppInfo.Name + " " + AppInfo.VersionText, Ui.F(S(11.5f)), Theme.Muted, new RectangleF(fx, fy, S(140), S(18)), TextFormatFlags.Left | TextFormatFlags.Top);
        fx += TextRenderer.MeasureText(g, AppInfo.Name + " " + AppInfo.VersionText, Ui.F(S(11.5f)), Size.Empty, TextFormatFlags.NoPadding).Width + S(14);
        if (Updates.Available(St))
            fx += Link(g, Loc.T("settings.update", St.LatestVersion), fx, fy, Theme.Accent, () => Open(Updates.ReleasesUrl)) + S(14);
        if (AppInfo.KofiName.Length > 0)
            fx += Link(g, Loc.T("settings.support"), fx, fy, Theme.Accent, () => Open("https://ko-fi.com/" + AppInfo.KofiName)) + S(14);
        Link(g, "GitHub", fx, fy, Theme.Muted, () => Open(AppInfo.RepoUrl));
    }

    static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
