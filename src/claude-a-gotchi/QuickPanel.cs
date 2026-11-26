using System.Diagnostics;

namespace ClaudeAGotchi;

// Quick panel that opens above the bed when it is clicked (or from the tray icon).
sealed class QuickPanel : PaintedForm
{
    readonly PetForm pet;
    readonly Art art;
    readonly float scale;
    readonly Stopwatch sw = Stopwatch.StartNew();
    readonly System.Windows.Forms.Timer anim = new() { Interval = 120 };
    double nextBlink = 1500;
    RectangleF avatar;

    public QuickPanel(PetForm p, Point anchor)
    {
        pet = p;
        art = new Art(p.St);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Text = AppInfo.Name;
        var screen = Screen.FromPoint(anchor);
        scale = Native.ScreenDpi(screen) / 96f;
        int w = (int)(320 * scale), h = (int)(328 * scale);
        var wa = screen.WorkingArea;
        int x = Math.Clamp(anchor.X - w / 2, wa.Left + 8, wa.Right - w - 8);
        int y = anchor.Y - h - (int)(10 * scale);
        if (y < wa.Top + 8) y = Math.Min(anchor.Y + (int)(10 * scale), wa.Bottom - h - 8);
        Bounds = new Rectangle(x, y, w, h);
        DpiChanged += (_, e) => e.Cancel = true;
        anim.Tick += (_, _) =>
        {
            double now = sw.Elapsed.TotalMilliseconds;
            if (now > nextBlink) { art.BlinkUntil = now + 140; nextBlink = now + 2500 + Random.Shared.NextDouble() * 3000; }
            Invalidate(Rectangle.Round(avatar));
        };
    }

    protected override float Sc => scale;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x20000;                 // drop shadow
            cp.ExStyle |= Native.WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override void OnShown(EventArgs e) { base.OnShown(e); anim.Start(); }
    protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); if (!IsDisposed) Close(); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); base.OnKeyDown(e); }
    protected override void OnFormClosed(FormClosedEventArgs e) { anim.Dispose(); base.OnFormClosed(e); }

    protected override void PaintUi(Graphics g)
    {
        float p = S(16), w = ClientSize.Width, y = p;
        double now = sw.Elapsed.TotalMilliseconds;

        // Header: Clawd, name, what he is doing
        avatar = new RectangleF(p, y, S(52), S(52));
        Ui.Fill(g, avatar, S(12), Theme.Card2);
        using (var clip = Ui.Round(avatar, S(12)))
        {
            g.SetClip(clip);
            PixelMode(g, true);
            art.G = g; art.K = Math.Max(1, (int)Math.Round(2 * scale));
            art.Clawd((int)(avatar.X + avatar.Width / 2), (int)(avatar.Bottom - S(5)), new DrawOpts { Emo = pet.PanelEmotion, Now = now, Dir = 1 });
            PixelMode(g, false);
            g.ResetClip();
        }
        Ui.Text(g, "Clawd", Ui.F(S(16), true), Theme.Text, new RectangleF(p + S(66), y + S(4), w - p * 2 - S(66), S(22)));
        Ui.Text(g, pet.StatusText(), Ui.F(S(12)), Theme.Muted, new RectangleF(p + S(66), y + S(28), w - p * 2 - S(66), S(18)));
        y += S(52) + S(14);

        // Limits
        var card = new RectangleF(p, y, w - 2 * p, S(112));
        Ui.Fill(g, card, S(10), Theme.Card);
        Ui.Stroke(g, card, S(10), Theme.Border);
        var lim = pet.Limits();
        LimitRow(g, card, card.Y + S(10), Loc.T("panel.fiveHours"), lim.P5, lim.Est5);
        LimitRow(g, card, card.Y + S(46), Loc.T("panel.week"), lim.PW, lim.EstW);
        string note = lim.Reset + " · " + lim.Tokens + (lim.Est5 || lim.EstW ? " · " + Loc.T("panel.estimated") : "");
        Ui.Text(g, note, Ui.F(S(11.5f)), Theme.Muted, new RectangleF(card.X + S(14), card.Y + S(84), card.Width - S(28), S(18)));
        y += S(112) + S(12);

        // Main actions
        float half = (w - 2 * p - S(10)) / 2;
        Button(g, new RectangleF(p, y, half, S(58)), Loc.T("panel.chat"), Loc.T("panel.chatSub"), true, () => { Close(); pet.OpenChat(); });
        Button(g, new RectangleF(p + half + S(10), y, half, S(58)), Loc.T("panel.code"), pet.ProjectName, false, () => { Close(); pet.OpenCode(); });
        y += S(58) + S(10);

        float third = (w - 2 * p - S(16)) / 3;
        Button(g, new RectangleF(p, y, third, S(38)), Loc.T("panel.customize"), null, false, () => { Close(); pet.OpenStudio(0); });
        Button(g, new RectangleF(p + third + S(8), y, third, S(38)), Loc.T(pet.St.Napping ? "panel.wake" : "panel.sleep"), null, false, () => { Close(); pet.ToggleNap(); });
        Button(g, new RectangleF(p + 2 * (third + S(8)), y, third, S(38)), Loc.T("panel.settings"), null, false, () => { Close(); pet.OpenStudio(2); });
    }

    void LimitRow(Graphics g, RectangleF card, float y, string label, double? v, bool est)
    {
        Ui.Text(g, label, Ui.F(S(12)), Theme.Muted, new RectangleF(card.X + S(14), y, card.Width / 2, S(20)));
        string val = v is double d ? (d >= 100 && !est ? Loc.T("panel.reached") : (est ? "≈ " : "") + d.ToString("0") + " %") : Loc.T("panel.unknown");
        bool red = v >= 100 && !est;
        Ui.Text(g, val, Ui.F(S(13), true), red ? Theme.Danger : v.HasValue ? Theme.Text : Theme.Muted,
            new RectangleF(card.X + card.Width / 2, y, card.Width / 2 - S(14), S(20)), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        var bar = new RectangleF(card.X + S(14), y + S(25), card.Width - S(28), S(6));
        Ui.Fill(g, bar, S(3), Theme.Track);
        if (v is double pct && pct > 0)
        {
            var fill = bar; fill.Width = (float)(bar.Width * Math.Min(1, pct / 100));
            Ui.Fill(g, fill, S(3), pct >= 80 ? Theme.Danger : pct >= 50 ? Theme.Hex("#e2b043") : Theme.Accent);
        }
    }
}
