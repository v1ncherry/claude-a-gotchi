using System.Drawing.Imaging;
using static ClaudeAGotchi.Sprites;

namespace ClaudeAGotchi;

sealed class DrawOpts
{
    public bool Lying, Walking, Moving, Typing, Excl, Sweat;
    public string Emo = "neutral", Pose = "stand";
    public string Hat, Outfit, Color;      // null = from the settings
    public double Now;
    public int Dir = 1, Gx, Gy;
}

// Draws Clawd and his bed. Used by the desktop window, the quick panel, the settings window and the image tools.
sealed class Art
{
    public readonly Gfx Gfx = new();
    public Settings St;
    public double BlinkUntil = -1, StretchUntil = -1, OvStart;
    public string OvE;

    public Art(Settings st) { St = st; }

    public int K { get => Gfx.K; set => Gfx.K = value; }
    public Graphics G { get => Gfx.G; set => Gfx.G = value; }

    public Color Body(string id = null) => Costumes.GetOr("color", id ?? St.BodyColor)?.Body ?? C("#da7756");

    public void Clawd(int ax, int ay, DrawOpts o)
    {
        var c = Body(o.Color);
        var lid = Shade(c, .72);
        var outfit = Costumes.Get("outfit", o.Outfit ?? St.Outfit);
        if (outfit != null && (outfit.Pixels == null || (o.Lying && !outfit.InBed))) outfit = null;
        string hatId = o.Hat ?? St.Hat;
        int dir = o.Dir;
        int yo = o.Pose == "sit" ? 2 : o.Pose == "squash" ? 1 : 0, bt = -16 + yo, fr = (int)(o.Now / 160);

        if (outfit?.Layer == "back") Layer(ax, ay, outfit, bt, dir, o.Moving, fr);

        Gfx.R(ax, ay, -8, bt, 16, 12, c);
        if (!o.Lying)
        {
            int[] legs = { -8, -4, 2, 6 };
            for (int i = 0; i < 4; i++)
            {
                int lift = (o.Walking && (i % 2) == (fr % 2)) ? 1 : 0;
                Gfx.R(ax, ay, legs[i], bt + 12, 2, -(bt + 12) - lift, c);
            }
            int ay0 = bt + 4, dl = 0, dr = 0;
            if (o.Now < StretchUntil || o.Pose == "held") ay0 = bt - 2;
            else if (o.Pose == "air") ay0 = bt + 3;
            else if (o.Typing) { dl = fr % 2 == 1 ? -1 : 0; dr = fr % 2 == 1 ? 0 : -1; }
            Gfx.R(ax, ay, -12, ay0 + dl, 4, 4, c);
            Gfx.R(ax, ay, 8, ay0 + dr, 4, 4, c);
        }

        if (outfit?.Layer == "face") Layer(ax, ay, outfit, bt, dir, o.Moving, fr);

        var e = Emos[o.Emo];
        var pal = new Dictionary<char, Color> { ['X'] = C("#111111"), ['R'] = C("#ff3d6e"), ['Y'] = C("#ffd23f"), ['L'] = lid, ['W'] = System.Drawing.Color.White };
        int gx = e.Gaze && !o.Lying ? o.Gx : 0, gy = e.Gaze && !o.Lying ? o.Gy : 0;
        EyeP lp = e.L, rp = e.R;
        if (e.Blink && o.Now < BlinkUntil) { lp = new EyeP(new[] { "LL", "XX" }); rp = null; gx = 0; gy = 0; }
        if (o.Emo == "wink" && OvE == "wink")
        {
            double t = o.Now - OvStart;
            rp = t < 120 ? new EyeP(SQ) : t < 220 ? new EyeP(new[] { "LL", "XX" }) : t < 760 ? new EyeP(new[] { "XXX" }, 0, 1) : t < 860 ? new EyeP(new[] { "LL", "XX" }) : new EyeP(SQ);
        }
        Eye(ax, ay, lp, -6 + gx, bt + 2 + gy, false, pal);
        if (rp != null) Eye(ax, ay, rp, 4 + gx, bt + 2 + gy, false, pal);
        else Eye(ax, ay, lp, 4 + gx, bt + 2 + gy, true, pal);
        if (e.Tear) Gfx.R(ax, ay, -6, bt + 4 + fr % 3, 1, 1, C("#6cc6ff"));

        if (outfit?.Layer == "front") Layer(ax, ay, outfit, bt, dir, o.Moving, fr);

        var hat = o.Lying ? NightCap : Costumes.Get("hat", hatId);
        if (hat?.Pixels != null) Layer(ax, ay, hat, bt, dir, false, fr);

        if (!o.Lying)
        {
            if (o.Excl && fr % 5 < 3) Gfx.Pat(ax, ay, Excl.P, Excl.Pal, 12, bt - 7, false);
            if (o.Emo == "confused") Gfx.Pat(ax, ay, Quest.P, Quest.Pal, 11, bt - 8, false);
            if (o.Emo == "angry") Gfx.Pat(ax, ay, Anger.P, Anger.Pal, 7, bt - 7, false);
            if (o.Sweat && fr % 3 != 2) Gfx.Pat(ax, ay, Sweat.P, Sweat.Pal, dir > 0 ? -11 : 9, bt - 1, false);
        }
    }

    // A hat or outfit picture. "at" is its top-left corner relative to the top center of the body.
    // Mirroring flips it around Clawd's center line; "flutter" rows shift against the walking direction.
    void Layer(int ax, int ay, Costume cs, int bt, int dir, bool moving, int fr)
    {
        bool flip = cs.Mirror == "left" ? dir < 0 : cs.Mirror == "right" && dir > 0;
        int x0 = flip ? -(cs.W + cs.AtX) : cs.AtX, y0 = bt + cs.AtY;
        if (cs.Flutter >= 0 && moving && fr % 2 == 1)
        {
            Gfx.Pat(ax, ay, cs.Pixels, cs.Pal, x0, y0, flip, 0, cs.Flutter);
            Gfx.Pat(ax, ay, cs.Pixels, cs.Pal, x0 - dir, y0, flip, cs.Flutter);
        }
        else Gfx.Pat(ax, ay, cs.Pixels, cs.Pal, x0, y0, flip);
    }

    void Eye(int ax, int ay, EyeP e, int sx, int sy, bool mirror, Dictionary<char, Color> pal)
    {
        int w = e.P[0].Length;
        Gfx.Pat(ax, ay, e.P, pal, mirror ? sx + 2 - w - e.Ox : sx + e.Ox, sy + e.Oy, mirror);
    }

    // Bed seen slightly from above: headboard, pillow, blanket, footboard. 30 x 30 pixels (the canopy reaches higher).
    public void Bed(int bx, int by, int T, Action<int> between, string frame = null, string cover = null, string pillow = null)
    {
        frame ??= St.BedFrame; cover ??= St.BedCover; pillow ??= St.Pillow;
        if (!FrameOrder.Contains(frame)) frame = "wood";
        Color B = C("#8b5a2b"), D = C("#6e4420"), S = C("#f4efe6"), Sd = C("#ddd5c8");
        Color M = C("#3b3f46"), Mh = C("#7a818c"), Lw = C("#d1ab73"), Ld = C("#a9834f");
        Color Fa = C("#eadcf2"), Fs = C("#c9b3d9");
        void R(int x, int y, int w, int h, Color c) => Gfx.R(bx, by, x, y, w, h, c);

        switch (frame)
        {
            case "metal":
                R(0, 1, 2, 7, M); R(28, 1, 2, 7, M); R(0, 0, 2, 1, Mh); R(28, 0, 2, 1, Mh);
                R(2, 2, 26, 1, M); R(2, 6, 26, 1, M);
                for (int x = 5; x < 27; x += 4) R(x, 3, 1, 3, M);
                R(0, 7, 30, 1, M);
                break;
            case "futon":
                break;
            case "canopy":
                R(0, -10, 2, 11, B); R(28, -10, 2, 11, B);
                R(-1, -14, 32, 3, Fa); R(-1, -11, 32, 1, Fs);
                for (int x = -1; x < 31; x += 2) R(x, -10, 1, 1, Fs);
                R(-1, -10, 1, 16, Fs); R(30, -10, 1, 16, Fs);
                R(1, 0, 28, 1, B); R(0, 1, 30, 6, B); R(0, 7, 30, 1, D);
                foreach (int x in new[] { 6, 12, 17, 23 }) R(x, 2, 1, 4, D);
                break;
            default:
                R(1, 0, 28, 1, B); R(0, 1, 30, 6, B); R(0, 7, 30, 1, D);
                foreach (int x in new[] { 6, 12, 17, 23 }) R(x, 2, 1, 4, D);
                break;
        }

        R(1, 8, 28, 18, S); R(1, 8, 1, 18, Sd); R(28, 8, 1, 18, Sd);
        var pc = Costumes.GetOr("pillow", pillow);
        Color pm = pc?.Main ?? C("#dcd7f3"), ps = pc?.Shade ?? C("#b4acdc");
        R(7, 9, 16, 1, pm); R(6, 10, 18, 4, pm); R(7, 14, 16, 1, ps);
        between?.Invoke(0);
        if (T < 26) CoverArea(bx, by, cover, 0, T, 30, 26 - T, true);
        between?.Invoke(1);

        switch (frame)
        {
            case "metal":
                R(0, 26, 30, 1, M); R(0, 27, 30, 1, Mh); R(0, 26, 2, 4, M); R(28, 26, 2, 4, M);
                break;
            case "futon":
                R(0, 26, 30, 3, Lw); R(0, 29, 30, 1, Ld);
                break;
            case "canopy":
                R(0, 26, 30, 2, B); R(0, 28, 30, 1, D); R(0, 20, 2, 10, B); R(28, 20, 2, 10, B);
                break;
            default:
                R(0, 26, 30, 2, B); R(0, 28, 30, 1, D); R(1, 29, 2, 1, D); R(27, 29, 2, 1, D);
                break;
        }
    }

    // Blanket pattern in a rectangle (Clawd pixels). The cover's picture is repeated like tiles,
    // starting at the top-left corner below the fold. With fold: white turned-down edge and a light band on top.
    public void CoverArea(int ox, int oy, string id, int x, int y, int w, int h, bool fold)
    {
        var cv = Costumes.GetOr("cover", id);
        if (cv == null || w <= 0 || h <= 0) return;
        Gfx.R(ox, oy, x, y, w, h, cv.Base);
        int start = y;
        if (fold)
        {
            Gfx.R(ox, oy, x, y, w, 1, C("#f4efe6"));
            if (h > 1) Gfx.R(ox, oy, x, y + 1, w, 1, cv.Band);
            start = y + 2;
        }
        int tw = cv.W, th = cv.H;
        for (int r = start; r < y + h; r++)
        {
            string row = cv.Pixels[(r - start) % th];
            for (int cx = x; cx < x + w; cx++)
            {
                char ch = row[(cx - x) % tw];
                if (ch != '.' && cv.Pal.TryGetValue(ch, out var col)) Gfx.R(ox, oy, cx, r, 1, 1, col);
            }
        }
    }

    // Two small pixel bars above the bed: 5 hours and 7 days. null = unknown.
    // style "text": with labels and percent, "bars": bars only. Optionally the hourglass (glassP = 0..1 elapsed).
    public void LimitBars(int bx, int by, double? p5, double? pw, string style = null, string frame = null,
                          bool showGlass = false, double glassP = 1, double glassFlip = 1e9, double now = 0)
    {
        frame ??= St.BedFrame;
        style ??= St.BarStyle;
        bool text = style != "bars";
        int top = (frame == "canopy" ? -15 : 0) - 17;
        Color edge = C("#0c100f"), plate = C("#1d2321"), txt = C("#e9f1ed"), dim = C("#6f7c77"), empty = C("#38403d");
        void R(int x, int y, int w, int h, Color c) => Gfx.R(bx, by, x, y, w, h, c);
        // Layout: label, bar, number, hourglass on the right (the bars get shorter then)
        int px0, pw0, labelX = 0, barX, barW, numRight = 0, glassX = 0, ph0 = 16;
        if (text && showGlass) { px0 = -11; pw0 = 44; labelX = -9; barX = -1; barW = 11; numRight = 21; glassX = 23; }
        else if (text) { px0 = -3; pw0 = 36; labelX = -1; barX = 7; barW = 13; numRight = 31; }
        else { px0 = -1; pw0 = 32; barX = 1; barW = showGlass ? 19 : 28; glassX = 21; }
        // Plate with rounded pixel corners
        R(px0 + 1, top, pw0 - 2, 1, edge); R(px0, top + 1, pw0, ph0 - 2, edge); R(px0 + 1, top + ph0 - 1, pw0 - 2, 1, edge);
        R(px0 + 1, top + 1, pw0 - 2, ph0 - 2, plate);

        void Text(int x, int y, string s, Color c)
        {
            var pal = new Dictionary<char, Color> { ['X'] = c };
            foreach (char ch in s) { if (Glyphs.TryGetValue(ch, out var gl)) Gfx.Pat(bx, by, gl, pal, x, y, false); x += 4; }
        }
        void Row(int y, string label, double? v)
        {
            int x0 = barX, w = barW, inner = w - 2;
            if (text) Text(labelX, y, label, txt);
            R(x0, y, w, 5, edge);
            R(x0 + 1, y + 1, inner, 3, empty);
            if (v is double pv)
            {
                pv = Math.Clamp(pv, 0, 100);
                int f = (int)Math.Round(inner * pv / 100);
                Color c = pv >= 80 ? C("#ec6a55") : pv >= 50 ? C("#e2b043") : C("#5cc188");
                if (f > 0) R(x0 + 1, y + 1, f, 3, c);
                if (text) { string num = ((int)Math.Round(pv)).ToString(); Text(numRight - (num.Length * 4 - 1) + 1, y, num, txt); }
            }
            else
            {
                for (int i = 0; i < inner; i += 2) R(x0 + 1 + i, y + 2, 1, 1, dim);
                if (text) Text(numRight - 2, y, "?", dim);
            }
        }
        Row(top + 2, Loc.T("plate.fiveHours"), p5);
        Row(top + 9, Loc.T("plate.week"), pw);
        if (showGlass) Hourglass(bx, by, glassX, top + 3, glassP, glassFlip, now);
    }

    static readonly string[] GlassTpl =
    { "CCCCCCCCC", "P.ggggg.P", "P.ggggg.P", "P..ggg..P", "P...g...P", "P..ggg..P", "P.ggggg.P", "P.ggggg.P", "CCCCCCCCC" };
    static readonly (int R, int C)[] TopSlots =
    { (3, 4), (3, 3), (3, 5), (2, 4), (2, 3), (2, 5), (2, 2), (2, 6), (1, 4), (1, 3), (1, 5), (1, 2), (1, 6) };
    static readonly (int R, int C)[] BottomSlots =
    { (7, 4), (7, 3), (7, 5), (7, 2), (7, 6), (6, 4), (6, 3), (6, 5), (6, 2), (6, 6), (5, 4), (5, 3), (5, 5) };

    // Hourglass 9 x 9: the sand runs through in 5 hours. flipT = ms since a new window started (it turns over).
    public void Hourglass(int ox, int oy, int x, int y, double p, double flipT, double now)
    {
        var g = new char[9][];
        for (int r = 0; r < 9; r++) g[r] = GlassTpl[r].ToCharArray();
        int rot = 0;
        if (flipT < 700)
        {
            p = 1;                                     // while turning: the old, empty glass
            rot = flipT < 230 ? 1 : flipT < 460 ? 2 : 0;
            if (rot == 0) p = 0;
        }
        p = Math.Clamp(p, 0, 1);
        int topCount = (int)Math.Round(13 * (1 - p)), botCount = 13 - topCount;
        for (int i = 0; i < topCount; i++) g[TopSlots[i].R][TopSlots[i].C] = 'S';
        for (int i = 0; i < botCount; i++) g[BottomSlots[i].R][BottomSlots[i].C] = 'S';
        if (p > 0 && p < 1 && rot == 0 && ((int)(now / 300)) % 2 == 0) g[4][4] = 'S';
        var pal = new Dictionary<char, Color> { ['C'] = C("#6e4420"), ['P'] = C("#8b5a2b"), ['g'] = C("#d4e9f0"), ['S'] = C("#f2c46d") };
        for (int r = 0; r < 9; r++)
            for (int c = 0; c < 9; c++)
            {
                char ch = g[r][c];
                if (ch == '.') continue;
                int dx = c, dy = r;
                if (rot == 1) { dx = 8 - r; dy = c; }
                else if (rot == 2) { dx = 8 - c; dy = 8 - r; }
                Gfx.R(ox, oy, x + dx, y + dy, 1, 1, pal[ch]);
            }
    }

    // Clawd in bed: head on the pillow, arms on the blanket.
    public void BedWithClawd(int bx, int by, int T, string emo, double now, string frame = null, string cover = null, string pillow = null,
                             string color = null, string outfit = null)
    {
        int ax = bx + 15 * K, ay = by + 27 * K;
        var c = Body(color);
        Bed(bx, by, T, layer =>
        {
            if (layer == 0) Clawd(ax, ay, new DrawOpts { Lying = true, Emo = emo, Now = now, Dir = 1, Color = color, Outfit = outfit });
            else { Gfx.R(ax, ay, -12, -12, 4, 4, c); Gfx.R(ax, ay, 8, -12, 4, 4, c); }
        }, frame, cover, pillow);
    }
}
