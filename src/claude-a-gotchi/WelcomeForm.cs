using System.Diagnostics;

namespace ClaudeAGotchi;

// Shown once on the first start: language, screen, Claude Code hooks and the consent for the exact limit query.
sealed class WelcomeForm : PaintedForm
{
    const int Wd = 560, Ht = 600;

    readonly PetForm pet;
    readonly Art art;
    readonly Stopwatch sw = Stopwatch.StartNew();
    readonly System.Windows.Forms.Timer anim = new() { Interval = 100 };
    double nextBlink = 900;
    RectangleF avatar;
    bool hooks;
    string msg = "";

    Settings St => pet.St;

    public WelcomeForm(PetForm p)
    {
        pet = p;
        art = new Art(p.St);
        Text = Loc.T("welcome.title");
        Icon = p.Icon;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        hooks = HookBridge.IsInstalled();
        anim.Tick += (_, _) =>
        {
            double now = sw.Elapsed.TotalMilliseconds;
            if (now > nextBlink) { art.BlinkUntil = now + 140; nextBlink = now + 2200 + Random.Shared.NextDouble() * 3000; }
            Invalidate(Rectangle.Round(avatar));
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

    protected override void OnFormClosed(FormClosedEventArgs e) { anim.Dispose(); base.OnFormClosed(e); }

    protected override void PaintUi(Graphics g)
    {
        float W = ClientSize.Width, H = ClientSize.Height, p = S(24), cw = W - 2 * p, y = p;
        double now = sw.Elapsed.TotalMilliseconds;
        Text = Loc.T("welcome.title");

        // Clawd waving
        avatar = new RectangleF(p, y, S(96), S(96));
        Ui.Fill(g, avatar, S(16), Theme.Hex("#2c5f55"));
        using (var clip = Ui.Round(avatar, S(16)))
        {
            g.SetClip(clip);
            PixelMode(g, true);
            art.G = g; art.K = Math.Max(1, (int)Math.Round(3 * Sc));
            bool wave = ((int)(now / 450)) % 2 == 0;
            art.Clawd((int)(avatar.X + avatar.Width / 2), (int)(avatar.Bottom - S(12)), new DrawOpts
            { Emo = "happy", Pose = wave ? "held" : "stand", Now = now, Dir = 1 });
            PixelMode(g, false);
            g.ResetClip();
        }
        float tx = avatar.Right + S(20), tw = W - tx - p;
        Ui.Text(g, Loc.T("welcome.hello"), Ui.F(S(22), true), Theme.Text, new RectangleF(tx, y + S(14), tw, S(30)));
        Ui.Text(g, Loc.T("welcome.intro"), Ui.F(S(12.5f)), Theme.Muted, new RectangleF(tx, y + S(50), tw, S(40)),
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
        y += S(96) + S(22);

        Label(g, Loc.T("settings.language"), p, y, cw, 12.5f, true);
        y += S(24);
        var langs = Loc.Available;
        var langLabels = new[] { Loc.T("settings.langAuto") }.Concat(langs.Select(l => l.Name)).ToArray();
        int lsel = St.Language == "auto" ? 0 : 1 + langs.ToList().FindIndex(l => l.Code == St.Language);
        Segmented(g, new RectangleF(p, y, cw, S(36)), langLabels, Math.Max(0, lsel), i =>
        {
            St.Language = i == 0 ? "auto" : langs[i - 1].Code;
            St.Save();
            pet.ApplyLanguage();
        });
        y += S(36) + S(16);

        Label(g, Loc.T("welcome.screen"), p, y, cw, 12.5f, true);
        y += S(24);
        var screens = Screen.AllScreens;
        var labels = ScreenLabels(pet, screens, out int sel);
        Segmented(g, new RectangleF(p, y, cw, S(36)), labels, sel, i => pet.ChangeScreen(screens[i].DeviceName));
        y += S(36) + S(18);

        Card(g, p, ref y, cw, S(76), Loc.T("settings.hooks"), Loc.T("welcome.hooksDesc"), hooks,
            () => { msg = pet.ToggleHooks(); hooks = HookBridge.IsInstalled(); });
        Card(g, p, ref y, cw, S(92), Loc.T("welcome.usage"), Loc.T("welcome.usageDesc"), St.UsageEnabled,
            () => { St.UsageEnabled = !St.UsageEnabled; St.Save(); pet.UsageToggled(); });

        if (msg.Length > 0)
            Ui.Text(g, msg, Ui.F(S(11.5f)), Theme.Accent, new RectangleF(p, y, cw, S(32)), TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);

        var go = new RectangleF(W - p - S(160), H - p - S(40), S(160), S(40));
        Button(g, go, Loc.T("welcome.go"), null, true, Close);
        Ui.Text(g, Loc.T("welcome.later"), Ui.F(S(11.5f)), Theme.Muted, new RectangleF(p, go.Y, go.X - p - S(12), go.Height),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }

    void Card(Graphics g, float x, ref float y, float w, float h, string title, string desc, bool on, Action toggle)
    {
        var r = new RectangleF(x, y, w, h);
        Ui.Fill(g, r, S(10), Theme.Card);
        Ui.Stroke(g, r, S(10), Theme.Border);
        Ui.Text(g, title, Ui.F(S(13), true), Theme.Text, new RectangleF(r.X + S(16), r.Y + S(10), r.Width - S(100), S(20)));
        Ui.Text(g, desc, Ui.F(S(11.5f)), Theme.Muted, new RectangleF(r.X + S(16), r.Y + S(31), r.Width - S(96), h - S(36)),
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
        Toggle(g, new RectangleF(r.Right - S(16) - S(44), r.Y + h / 2 - S(11), S(44), S(22)), on, toggle);
        y += h + S(10);
    }
}
