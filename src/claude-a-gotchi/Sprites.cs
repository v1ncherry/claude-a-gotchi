namespace ClaudeAGotchi;

// Everything drawn on one grid: 1 character = 1 Clawd pixel.
// Clawd's coordinates are relative to his feet (bottom center): body x -8..8, y -16..-4.
// Costumes (hats, outfits, colors, covers, pillows) are not here: they are text files in /costumes.
static class Sprites
{
    public static Color C(string h) => Color.FromArgb(
        Convert.ToInt32(h.Substring(1, 2), 16), Convert.ToInt32(h.Substring(3, 2), 16), Convert.ToInt32(h.Substring(5, 2), 16));

    static Dictionary<char, Color> Pal(params (char ch, string hex)[] e)
    {
        var d = new Dictionary<char, Color>();
        foreach (var (ch, hex) in e) d[ch] = C(hex);
        return d;
    }

    public sealed record EyeP(string[] P, int Ox = 0, int Oy = 0);

    public sealed class Emo
    {
        public EyeP L, R;
        public bool Gaze, Blink, Tear;
    }

    public static readonly string[] SQ = { "XX", "XX" };

    public static readonly string[] EmoOrder =
        { "neutral", "happy", "love", "surprised", "angry", "sad", "tired", "focused", "star", "confused", "wink", "sleep" };

    public static readonly Dictionary<string, Emo> Emos = new()
    {
        ["neutral"] = new Emo { L = new EyeP(SQ), Gaze = true, Blink = true },
        ["happy"] = new Emo { L = new EyeP(new[] { ".XX.", "X..X" }, -1) },
        ["love"] = new Emo { L = new EyeP(new[] { "RR.RR", "RRRRR", ".RRR.", "..R.." }, -1, -1) },
        ["surprised"] = new Emo { L = new EyeP(new[] { "WXX", "XXX", "XXX" }, -1, -1), R = new EyeP(new[] { "WXX", "XXX", "XXX" }, 0, -1), Gaze = true },
        ["angry"] = new Emo { L = new EyeP(new[] { "X..", "XX.", "XXX" }, -1, -1) },
        ["sad"] = new Emo { L = new EyeP(new[] { "..X", ".XX", "XXX" }, -1, -1), Tear = true },
        ["tired"] = new Emo { L = new EyeP(new[] { "LL", "XX" }) },
        ["focused"] = new Emo { L = new EyeP(SQ, 0, 1), Blink = true },
        ["star"] = new Emo { L = new EyeP(new[] { "..Y..", ".YYY.", "YYYYY", ".YYY.", "Y...Y" }, -1, -2) },
        ["confused"] = new Emo { L = new EyeP(SQ), R = new EyeP(new[] { "X" }, 0, 1) },
        ["wink"] = new Emo { L = new EyeP(SQ), Gaze = true },
        ["sleep"] = new Emo { L = new EyeP(new[] { "X..X", ".XX." }, -1) },
    };

    // The night cap is not a costume: Clawd always wears it in bed.
    public static readonly Costume NightCap = MakeNightCap();

    static Costume MakeNightCap()
    {
        var c = new Costume
        {
            Type = "hat", Id = "nightcap", AtX = -9, AtY = -7, Mirror = "left", BuiltIn = true,
            Pixels = new[] {
                "................WW..", "................WW..", "..............BBB...", "............BBBB....", "..........BBBBB.....",
                ".......BBBBBBB......", "....BBBBBBBBBB......", "..BBBBBBBBBBBBBB....", ".WWWWWWWWWWWWWWWW..." },
        };
        c.Pal['B'] = C("#4f6bd8");
        c.Pal['W'] = C("#f4f4f4");
        return c;
    }

    // Size on the desktop: how many screen pixels one Clawd pixel gets at 100 % scaling.
    public static readonly string[] SizeOrder = { "mini", "small", "medium" };
    public static readonly Dictionary<string, int> Sizes = new() { ["mini"] = 2, ["small"] = 3, ["medium"] = 4 };

    // Bed frames are drawn in code (Art.Bed), only their names are translated.
    public static readonly string[] FrameOrder = { "wood", "metal", "futon", "canopy" };

    // 3 x 5 pixel font for the limit plate above the bed
    public static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['0'] = new[] { "XXX", "X.X", "X.X", "X.X", "XXX" }, ['1'] = new[] { ".X.", "XX.", ".X.", ".X.", "XXX" },
        ['2'] = new[] { "XXX", "..X", "XXX", "X..", "XXX" }, ['3'] = new[] { "XXX", "..X", ".XX", "..X", "XXX" },
        ['4'] = new[] { "X.X", "X.X", "XXX", "..X", "..X" }, ['5'] = new[] { "XXX", "X..", "XXX", "..X", "XXX" },
        ['6'] = new[] { "XXX", "X..", "XXX", "X.X", "XXX" }, ['7'] = new[] { "XXX", "..X", ".X.", ".X.", ".X." },
        ['8'] = new[] { "XXX", "X.X", "XXX", "X.X", "XXX" }, ['9'] = new[] { "XXX", "X.X", "XXX", "..X", "XXX" },
        ['H'] = new[] { "X.X", "X.X", "XXX", "X.X", "X.X" }, ['T'] = new[] { "XXX", ".X.", ".X.", ".X.", ".X." },
        ['D'] = new[] { "XX.", "X.X", "X.X", "X.X", "XX." },
        ['?'] = new[] { "XX.", "..X", ".X.", "...", ".X." }, ['~'] = new[] { "...", ".X.", "X.X", "...", "..." },
    };

    public sealed record Icn(string[] P, Dictionary<char, Color> Pal);
    public static readonly Icn Excl = new(new[] { "YY", "YY", "YY", "..", "YY" }, Pal(('Y', "#ffd23f")));
    public static readonly Icn Quest = new(new[] { "QQQ.", "...Q", ".QQ.", "....", ".Q.." }, Pal(('Q', "#ffffff")));
    public static readonly Icn Anger = new(new[] { "RR.RR", "R...R", ".....", "R...R", "RR.RR" }, Pal(('R', "#ff4b4b")));
    public static readonly Icn Sweat = new(new[] { ".B", "BB", "BB" }, Pal(('B', "#7cc4ff")));

    public static readonly Dictionary<string, Icn> Parts = new()
    {
        ["heart"] = new(new[] { "R.R", "RRR", ".R." }, Pal(('R', "#ff5c8a"))),
        ["z"] = new(new[] { "ZZZZ", "..Z.", ".Z..", "ZZZZ" }, Pal(('Z', "#ffffff"))),
        ["spark"] = new(new[] { ".Y.", "YYY", ".Y." }, Pal(('Y', "#ffd23f"))),
    };

    public static Color Shade(Color c, double f) =>
        Color.FromArgb((int)Math.Round(c.R * f), (int)Math.Round(c.G * f), (int)Math.Round(c.B * f));
}

// Draws whole Clawd pixels: each one is exactly k x k screen pixels.
sealed class Gfx
{
    public Graphics G;
    public int K = 3;
    readonly Dictionary<int, SolidBrush> brushes = new();

    SolidBrush B(Color c)
    {
        int key = c.ToArgb();
        if (!brushes.TryGetValue(key, out var b)) { b = new SolidBrush(c); brushes[key] = b; }
        return b;
    }

    public void R(int ax, int ay, int x, int y, int w, int h, Color c)
    {
        if (w <= 0 || h <= 0) return;
        G.FillRectangle(B(c), ax + x * K, ay + y * K, w * K, h * K);
    }

    // Draws rows [from, to) of a pattern; '.' is transparent. flip mirrors it inside its own box.
    public void Pat(int ax, int ay, string[] rows, Dictionary<char, Color> pal, int x0, int y0, bool flip, int from = 0, int to = -1)
    {
        int w = rows[0].Length;
        if (to < 0 || to > rows.Length) to = rows.Length;
        for (int r = from; r < to; r++)
            for (int i = 0; i < w; i++)
            {
                char ch = rows[r][flip ? w - 1 - i : i];
                if (ch == '.' || !pal.TryGetValue(ch, out var c)) continue;
                R(ax, ay, x0 + i, y0 + r, 1, 1, c);
            }
    }
}
