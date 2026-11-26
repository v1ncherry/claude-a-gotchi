using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ClaudeAGotchi;

// Windows 11 style colors, light or dark depending on the Windows setting.
static class Theme
{
    public static bool Dark;
    public static Color Bg, Card, Card2, Border, Text, Muted, Accent, AccentHot, AccentText, Danger, Track;

    static Theme() => Apply();

    public static void Apply()
    {
        Dark = DetectDark();
        if (Dark)
        {
            Bg = Hex("#1f1f1f"); Card = Hex("#2b2b2b"); Card2 = Hex("#333333"); Border = Hex("#3d3d3d");
            Text = Hex("#f3f3f3"); Muted = Hex("#a8a8a8"); Track = Hex("#454545");
            Accent = Hex("#d97757"); AccentHot = Hex("#e48a6c"); AccentText = Hex("#1b120d"); Danger = Hex("#ff8a80");
        }
        else
        {
            Bg = Hex("#f3f3f3"); Card = Hex("#ffffff"); Card2 = Hex("#f7f7f7"); Border = Hex("#e3e3e3");
            Text = Hex("#1b1b1b"); Muted = Hex("#5e5e5e"); Track = Hex("#e1e1e1");
            Accent = Hex("#c96442"); AccentHot = Hex("#b5583a"); AccentText = Hex("#ffffff"); Danger = Hex("#c42b1c");
        }
    }

    static bool DetectDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    public static Color Hex(string h) => Sprites.C(h);
    public static Color Mix(Color a, Color b, double t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
}

static class Ui
{
    static readonly Dictionary<string, Font> fonts = new();
    static string family;

    static string Family()
    {
        if (family != null) return family;
        var names = FontFamily.Families.Select(f => f.Name).ToHashSet();
        family = names.Contains("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
        return family;
    }

    public static Font F(float px, bool bold = false)
    {
        string key = px.ToString("0.0") + bold;
        if (!fonts.TryGetValue(key, out var f))
        {
            f = new Font(Family(), px, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            fonts[key] = f;
        }
        return f;
    }

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void Fill(Graphics g, RectangleF r, float rad, Color c)
    {
        using var p = Round(r, rad);
        using var b = new SolidBrush(c);
        g.FillPath(b, p);
    }

    public static void Stroke(Graphics g, RectangleF r, float rad, Color c, float w = 1)
    {
        using var p = Round(r, rad);
        using var pen = new Pen(c, w);
        g.DrawPath(pen, p);
    }

    public static void Text(Graphics g, string s, Font f, Color c, RectangleF r, TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter)
    {
        TextRenderer.DrawText(g, s, f, Rectangle.Round(r), c, flags | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);

    // Windows 11: rounded corners, dark title bar, title bar in the background color.
    public static void StyleWindow(IntPtr h, bool roundCorners)
    {
        try
        {
            int dark = Theme.Dark ? 1 : 0;
            DwmSetWindowAttribute(h, 20, ref dark, 4);
            if (roundCorners) { int round = 2; DwmSetWindowAttribute(h, 33, ref round, 4); }
            int caption = ColorTranslator.ToWin32(Theme.Bg);
            DwmSetWindowAttribute(h, 35, ref caption, 4);
            int border = ColorTranslator.ToWin32(Theme.Border);
            DwmSetWindowAttribute(h, 34, ref border, 4);
        }
        catch { }
    }
}

// Self-painted interface: cards, buttons and tiles are drawn, and their click areas are collected in a list.
abstract class PaintedForm : Form
{
    readonly List<(RectangleF R, Action A)> hits = new();
    readonly List<(RectangleF R, Action<float> Set)> sliders = new();
    protected Point Mp = new(-10000, -10000);
    int hotIndex = -1, activeSlider = -1;

    protected PaintedForm()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Bg;
        KeyPreview = true;
    }

    protected virtual float Sc => DeviceDpi / 96f;
    protected float S(float v) => v * Sc;

    protected abstract void PaintUi(Graphics g);

    protected override void OnPaint(PaintEventArgs e)
    {
        hits.Clear();
        sliders.Clear();
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.Clear(Theme.Bg);
        PaintUi(g);
    }

    protected void Hit(RectangleF r, Action a) => hits.Add((r, a));
    protected bool Hot(RectangleF r) => r.Contains(Mp);

    // Pixel art: no anti-aliasing, so every pixel stays sharp.
    protected void PixelMode(Graphics g, bool on)
    {
        g.SmoothingMode = on ? SmoothingMode.None : SmoothingMode.AntiAlias;
        g.PixelOffsetMode = on ? PixelOffsetMode.None : PixelOffsetMode.Half;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        activeSlider = sliders.FindIndex(s => RectangleF.Inflate(s.R, S(6), S(8)).Contains(e.Location));
        if (activeSlider >= 0) { SlideTo(e.X); Capture = true; }
    }

    void SlideTo(int x)
    {
        if (activeSlider < 0 || activeSlider >= sliders.Count) return;
        var s = sliders[activeSlider];
        s.Set(Math.Clamp((x - s.R.X) / s.R.Width, 0f, 1f));
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (activeSlider >= 0) { SlideTo(e.X); return; }
        Mp = e.Location;
        int idx = hits.FindLastIndex(h => h.R.Contains(Mp));
        Cursor = idx >= 0 ? Cursors.Hand : Cursors.Default;
        if (idx != hotIndex) { hotIndex = idx; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        Mp = new Point(-10000, -10000);
        hotIndex = -1;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        if (activeSlider >= 0) { activeSlider = -1; Capture = false; OnSlideDone(); Invalidate(); return; }
        int idx = hits.FindLastIndex(h => h.R.Contains(e.Location));
        if (idx >= 0) { var a = hits[idx].A; a(); if (!IsDisposed) Invalidate(); }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Ui.StyleWindow(Handle, true);
    }

    // Shared building blocks
    protected void Button(Graphics g, RectangleF r, string title, string sub, bool primary, Action a)
    {
        bool hot = Hot(r);
        Color fill = primary ? (hot ? Theme.AccentHot : Theme.Accent) : (hot ? Theme.Mix(Theme.Card2, Theme.Text, 0.06) : Theme.Card2);
        Ui.Fill(g, r, S(8), fill);
        if (!primary) Ui.Stroke(g, r, S(8), Theme.Border);
        Color tc = primary ? Theme.AccentText : Theme.Text;
        if (string.IsNullOrEmpty(sub))
            Ui.Text(g, title, Ui.F(S(13), true), tc, r, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        else
        {
            var top = new RectangleF(r.X + S(12), r.Y + r.Height / 2 - S(17), r.Width - S(24), S(18));
            var bot = new RectangleF(r.X + S(12), r.Y + r.Height / 2 + S(1), r.Width - S(24), S(16));
            Ui.Text(g, title, Ui.F(S(13.5f), true), tc, top);
            Ui.Text(g, sub, Ui.F(S(11.5f)), primary ? Theme.Mix(tc, fill, 0.25) : Theme.Muted, bot);
        }
        Hit(r, a);
    }

    protected void Toggle(Graphics g, RectangleF r, bool on, Action a)
    {
        bool hot = Hot(r);
        Color track = on ? (hot ? Theme.AccentHot : Theme.Accent) : Theme.Card2;
        Ui.Fill(g, r, r.Height / 2, track);
        if (!on) Ui.Stroke(g, r, r.Height / 2, Theme.Muted, S(1));
        float d = r.Height - S(8);
        float x = on ? r.Right - S(4) - d : r.X + S(4);
        using var b = new SolidBrush(on ? Theme.AccentText : Theme.Muted);
        g.FillEllipse(b, x, r.Y + S(4), d, d);
        Hit(r, a);
    }

    protected void Segmented(Graphics g, RectangleF r, string[] labels, int sel, Action<int> pick)
    {
        Ui.Fill(g, r, S(8), Theme.Card2);
        Ui.Stroke(g, r, S(8), Theme.Border);
        float w = (r.Width - S(8)) / labels.Length;
        for (int i = 0; i < labels.Length; i++)
        {
            var seg = new RectangleF(r.X + S(4) + i * w, r.Y + S(4), w, r.Height - S(8));
            if (i == sel) { Ui.Fill(g, seg, S(6), Theme.Card); Ui.Stroke(g, seg, S(6), Theme.Border); }
            else if (Hot(seg)) Ui.Fill(g, seg, S(6), Theme.Mix(Theme.Card2, Theme.Text, 0.05));
            Ui.Text(g, labels[i], Ui.F(S(12.5f), i == sel), i == sel ? Theme.Text : Theme.Muted, seg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            int idx = i;
            Hit(seg, () => pick(idx));
        }
    }

    // Labels for the screen picker. Screenshots (demo) show two example screens instead of the real ones.
    protected static string[] ScreenLabels(PetForm pet, Screen[] screens, out int sel)
    {
        if (pet.Demo) { sel = 1; return new[] { Loc.T("settings.screenMain") + " · 2560×1440", Loc.T("settings.screenNr", 2) + " · 1920×1080" }; }
        string cur = pet.ScreenDevice;
        sel = Array.FindIndex(screens, s => s.DeviceName == cur);
        return screens.Select((s, i) => (s.Primary ? Loc.T("settings.screenMain") : Loc.T("settings.screenNr", i + 1)) + " · " + s.Bounds.Width + "×" + s.Bounds.Height).ToArray();
    }

    protected virtual void OnSlideDone() { }
    protected bool Sliding => activeSlider >= 0;

    // Slider, value 0..1
    protected void Slider(Graphics g, RectangleF r, float v, Action<float> set)
    {
        float cy = r.Y + r.Height / 2;
        var track = new RectangleF(r.X, cy - S(2), r.Width, S(4));
        Ui.Fill(g, track, S(2), Theme.Track);
        var fill = track; fill.Width = r.Width * v;
        if (fill.Width > 0) Ui.Fill(g, fill, S(2), Theme.Accent);
        float kx = r.X + r.Width * v, kr = S(9);
        bool hot = Hot(RectangleF.Inflate(r, S(6), S(8))) || Sliding;
        using (var b = new SolidBrush(Theme.Card)) g.FillEllipse(b, kx - kr, cy - kr, 2 * kr, 2 * kr);
        using (var pen = new Pen(Theme.Border, S(1))) g.DrawEllipse(pen, kx - kr, cy - kr, 2 * kr, 2 * kr);
        float dr = hot ? S(6) : S(5);
        using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, kx - dr, cy - dr, 2 * dr, 2 * dr);
        sliders.Add((r, set));
    }

    // Clickable text, underlined while the mouse is over it. Returns its width.
    protected float Link(Graphics g, string text, float x, float y, Color c, Action a, float size = 11.5f)
    {
        var f = Ui.F(S(size));
        var sz = TextRenderer.MeasureText(g, text, f, Size.Empty, TextFormatFlags.NoPadding);
        var r = new RectangleF(x, y, sz.Width, S(size + 6));
        bool hot = Hot(r);
        Ui.Text(g, text, hot ? new Font(f, FontStyle.Underline) : f, c, r, TextFormatFlags.Left | TextFormatFlags.Top);
        Hit(r, a);
        return sz.Width;
    }

    protected void Label(Graphics g, string text, float x, float y, float w, float size = 12, bool bold = false, Color? c = null) =>
        Ui.Text(g, text, Ui.F(S(size), bold), c ?? Theme.Text, new RectangleF(x, y, w, S(size + 8)), TextFormatFlags.Left | TextFormatFlags.Top);
}
