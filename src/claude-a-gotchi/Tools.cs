using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using static ClaudeAGotchi.Sprites;

namespace ClaudeAGotchi;

// Command line tools for contributors and the build: costume preview and check, README pictures, GIF frames.
static class Tools
{
    static readonly Color Bg = C("#2c5f55"), Floor = C("#16201e"), Ink = C("#e9f1ed"), Dim = C("#9fb8b0"), Bad = C("#ff8a80");

    static void Init(string lang = "en")
    {
        Loc.Load();
        Loc.Use(lang);
        Costumes.Load();
    }

    // ---------- Costume preview (used by the pull request check) ----------
    // claude-a-gotchi --preview costumes/hats/party.txt out.png
    public static int Preview(string file, string outPng)
    {
        Init();
        var errors = new List<string>();
        var c = Costumes.Parse(File.ReadAllText(file), file.Replace('\\', '/'), errors);
        if (c == null)
        {
            foreach (var e in errors) Console.Error.WriteLine(e);
            RenderErrors(outPng, file, errors);
            return 1;
        }
        Costumes.Put(c);
        RenderCostume(c, outPng);
        Console.WriteLine($"{c.Type} \"{c.Id}\" ({c.Name}) ok, {c.W}x{c.H} pixels -> {outPng}");
        return 0;
    }

    static void RenderCostume(Costume c, string outPng)
    {
        var st = new Settings();
        var art = new Art(st) { K = 6 };
        int w = 760, h = 300;
        using var bmp = Canvas(w, h, out var g, h - 64);
        art.G = g;
        Title(g, c.Name + "   ·   " + c.Type + " · " + c.Id, 16, 12);
        if (c.Names.Count > 1)
            Small(g, string.Join("   ", c.Names.Where(n => n.Key != "").Select(n => n.Key + ": " + n.Value)), 16, 40);
        int floor = h - 64;
        switch (c.Type)
        {
            case "hat":
            case "outfit":
            {
                var poses = new (int Dir, string Emo, string Pose, bool Walk)[] { (1, "happy", "stand", false), (-1, "neutral", "stand", false), (1, "surprised", "air", true), (-1, "focused", "sit", false) };
                for (int i = 0; i < poses.Length; i++)
                {
                    var o = new DrawOpts { Dir = poses[i].Dir, Emo = poses[i].Emo, Pose = poses[i].Pose, Walking = poses[i].Walk, Moving = poses[i].Walk, Now = poses[i].Walk ? 160 : 0 };
                    if (c.Type == "hat") { o.Hat = c.Id; o.Outfit = "none"; } else { o.Outfit = c.Id; o.Hat = "none"; }
                    art.Clawd(100 + i * 150, floor, o);
                }
                if (c.Type == "outfit" && c.InBed)
                {
                    art.K = 3;
                    art.BedWithClawd(650, floor - 30 * 3, 16, "sleep", 0, outfit: c.Id);
                }
                break;
            }
            case "color":
            {
                string[] emos = { "neutral", "happy", "love", "focused" };
                for (int i = 0; i < emos.Length; i++) art.Clawd(100 + i * 150, floor, new DrawOpts { Color = c.Id, Emo = emos[i], Hat = "none", Outfit = "none" });
                art.K = 3;
                art.BedWithClawd(650, floor - 30 * 3, 16, "sleep", 0, color: c.Id);
                break;
            }
            case "cover":
            case "pillow":
            {
                art.K = 6;
                int bx = 40, by = floor - 30 * 6 + 24;
                if (c.Type == "cover") art.Bed(bx, by, 12, null, "wood", c.Id, "white");
                else art.BedWithClawd(bx, by, 18, "sleep", 0, "wood", "dots", c.Id);
                art.K = 6;
                if (c.Type == "cover") art.CoverArea(300, 70, c.Id, 0, 0, 72, 26, false);
                else
                {
                    using var b1 = new SolidBrush(c.Main); using var b2 = new SolidBrush(c.Shade);
                    g.FillRectangle(b1, 320, 110, 240, 70); g.FillRectangle(b2, 330, 180, 220, 14);
                }
                break;
            }
        }
        bmp.Save(outPng, ImageFormat.Png);
    }

    static void RenderErrors(string outPng, string file, List<string> errors)
    {
        try
        {
            using var bmp = Canvas(760, 60 + 22 * errors.Count, out var g, -1);
            Title(g, "Problems in " + Path.GetFileName(file), 16, 12);
            for (int i = 0; i < errors.Count; i++) Small(g, "• " + errors[i].Split(": ", 2).Last(), 16, 46 + 22 * i, Bad);
            bmp.Save(outPng, ImageFormat.Png);
        }
        catch { }
    }

    // ---------- Check every costume and language file (used by the build) ----------
    // claude-a-gotchi --check <repo folder>
    public static int Check(string root)
    {
        Init();
        int bad = 0;
        var seen = new Dictionary<string, string>();
        foreach (var f in Directory.GetFiles(Path.Combine(root, "costumes"), "*.txt", SearchOption.AllDirectories).OrderBy(x => x))
        {
            var errors = new List<string>();
            string rel = Path.GetRelativePath(root, f).Replace('\\', '/');
            var c = Costumes.Parse(File.ReadAllText(f), rel, errors);
            foreach (var e in errors) { Console.Error.WriteLine(e); bad++; }
            if (c == null) continue;
            string folder = rel.Split('/').Length > 2 ? rel.Split('/')[1] : "";
            string expected = c.Type == "hat" ? "hats" : c.Type == "outfit" ? "outfits" : c.Type == "color" ? "colors" : c.Type == "cover" ? "covers" : "pillows";
            if (folder != expected) { Console.Error.WriteLine($"{rel}: a {c.Type} belongs in costumes/{expected}/"); bad++; }
            if (Path.GetFileNameWithoutExtension(f) != c.Id) { Console.Error.WriteLine($"{rel}: the file should be called {c.Id}.txt"); bad++; }
            if (seen.TryGetValue(c.Type + "/" + c.Id, out var other)) { Console.Error.WriteLine($"{rel}: id \"{c.Id}\" is already used by {other}"); bad++; }
            seen[c.Type + "/" + c.Id] = rel;
        }
        Console.WriteLine($"{seen.Count} costume files checked");

        var langDir = Path.Combine(root, "lang");
        var en = ReadKeys(Path.Combine(langDir, "en.json"));
        foreach (var f in Directory.GetFiles(langDir, "*.json"))
        {
            string err = Loc.Add(File.ReadAllText(f), Path.GetFileName(f));
            if (err != null) { Console.Error.WriteLine(err); bad++; continue; }
            var keys = ReadKeys(f);
            var missing = en.Except(keys).ToList();
            if (missing.Count > 0) Console.WriteLine($"{Path.GetFileName(f)}: {missing.Count} texts not translated yet (English is used): {string.Join(", ", missing.Take(8))}");
            var unknown = keys.Except(en).Where(k => !k.StartsWith("_")).ToList();
            if (unknown.Count > 0) { Console.Error.WriteLine($"{Path.GetFileName(f)}: unknown keys {string.Join(", ", unknown)}"); bad++; }
        }
        Console.WriteLine(bad == 0 ? "all good" : bad + " problem(s)");
        return bad == 0 ? 0 : 1;
    }

    static HashSet<string> ReadKeys(string file)
    {
        using var d = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
        return d.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
    }

    // ---------- Pictures for the README ----------
    // claude-a-gotchi --gallery docs/images [en|de]
    public static int Gallery(string dir, string lang)
    {
        Init(lang);
        Directory.CreateDirectory(dir);
        var art = new Art(new Settings());

        // Hats, outfits, colors in a row each, with names
        Row(dir, "hats.png", Costumes.Hats.Where(h => h.Id != "none").ToList(), (c, x, floor) => art.Clawd(x, floor, new DrawOpts { Hat = c.Id, Outfit = "none", Emo = "happy" }), art);
        Row(dir, "outfits.png", Costumes.Outfits.Where(h => h.Id != "none").ToList(), (c, x, floor) => art.Clawd(x, floor, new DrawOpts { Outfit = c.Id, Hat = "none", Emo = "happy" }), art);
        Row(dir, "colors.png", Costumes.Colors.ToList(), (c, x, floor) => art.Clawd(x, floor, new DrawOpts { Color = c.Id, Hat = "none", Outfit = "none" }), art);

        // Emotions
        {
            string[] emos = { "neutral", "happy", "love", "surprised", "focused", "star", "confused", "angry", "sad", "tired", "sleep" };
            int cell = 110, w = cell * emos.Length;
            using var bmp = Canvas(w, 150, out var g, 112);
            art.G = g; art.K = 4;
            for (int i = 0; i < emos.Length; i++)
                art.Clawd(i * cell + cell / 2, 112, new DrawOpts { Emo = emos[i], Hat = "none", Outfit = "none", Sweat = emos[i] == "sad", Excl = emos[i] == "surprised" });
            bmp.Save(Path.Combine(dir, "emotions.png"), ImageFormat.Png);
        }

        // Bed frames (with Clawd asleep) and covers
        {
            var covers = Costumes.Covers.Select(c => c.Id).ToList();
            var pillows = Costumes.Pillows.Select(c => c.Id).ToList();
            int cell = 170;
            using var bmp = Canvas(cell * FrameOrder.Length, 200, out var g, -1);
            art.G = g; art.K = 4;
            for (int i = 0; i < FrameOrder.Length; i++)
            {
                int bx = i * cell + (cell - 120) / 2, by = 200 - 30 * 4 - 14;
                art.BedWithClawd(bx, by, 16, "sleep", 0, FrameOrder[i], covers[i % covers.Count], pillows[i % pillows.Count]);
            }
            bmp.Save(Path.Combine(dir, "beds.png"), ImageFormat.Png);

            using var bmp2 = Canvas(130 * covers.Count, 150, out var g2, -1);
            art.G = g2; art.K = 3;
            for (int i = 0; i < covers.Count; i++)
            {
                int bx = i * 130 + 20, by = 150 - 30 * 3 - 30;
                art.Bed(bx, by, 14, null, "wood", covers[i], "white");
                Small(g2, Costumes.Get("cover", covers[i]).Name, i * 130 + 8, 126, Dim, 120);
            }
            bmp2.Save(Path.Combine(dir, "covers.png"), ImageFormat.Png);
        }

        // Limit plate variants
        {
            using var bmp = Canvas(4 * 200, 180, out var g, -1);
            art.G = g; art.K = 3;
            var v = new (string Style, double? A, double? B, bool Glass, double Gp)[]
            { ("text", 42, 18, true, 0.4), ("text", 87, 64, false, 1), ("bars", 63, 30, true, 0.75), ("text", 100, 71, true, 1) };
            for (int i = 0; i < v.Length; i++)
            {
                int bx = i * 200 + 55, by = 180 - 30 * 3 - 6;
                if (v[i].A >= 100) art.BedWithClawd(bx, by, 16, "sleep", 0, "wood", "dots", "lavender");
                else art.Bed(bx, by, 18, null, "wood", "dots", "lavender");
                art.LimitBars(bx, by, v[i].A, v[i].B, v[i].Style, "wood", v[i].Glass, v[i].Gp, 1e9, 0);
            }
            bmp.Save(Path.Combine(dir, "limits.png"), ImageFormat.Png);
        }
        Console.WriteLine("pictures written to " + dir);
        return 0;
    }

    static void Row(string dir, string name, List<Costume> items, Action<Costume, int, int> draw, Art art)
    {
        int cell = 120, w = Math.Max(1, items.Count) * cell;
        using var bmp = Canvas(w, 170, out var g, 120);
        art.G = g; art.K = 4;
        for (int i = 0; i < items.Count; i++)
        {
            draw(items[i], i * cell + cell / 2, 120);
            Small(g, items[i].Name, i * cell + 4, 136, Dim, cell - 8, true);
        }
        bmp.Save(Path.Combine(dir, name), ImageFormat.Png);
    }

    // ---------- Frames for the animated GIFs (tools/make-media.ps1 turns them into GIFs) ----------
    // claude-a-gotchi --frames <folder>
    public static int Frames(string dir)
    {
        Init();
        Scene(Path.Combine(dir, "bedtime"), 360, 240, 4, 5600, Bedtime);
        Scene(Path.Combine(dir, "throw"), 360, 240, 4, 3600, Throw);
        Scene(Path.Combine(dir, "moods"), 200, 150, 5, 7700, Moods);
        Scene(Path.Combine(dir, "costumes"), 200, 170, 5, 4800, Dressup);
        Console.WriteLine("frames written to " + dir);
        return 0;
    }

    const int Fps = 25;

    static void Scene(string dir, int w, int h, int k, int ms, Action<Art, Graphics, double, int, int> frame)
    {
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
        var art = new Art(new Settings()) { K = k };
        int n = ms * Fps / 1000;
        for (int i = 0; i < n; i++)
        {
            using var bmp = Canvas(w, h, out var g, h - 3 * k);
            art.G = g;
            frame(art, g, i * 1000.0 / Fps, w, h);
            bmp.Save(Path.Combine(dir, i.ToString("0000") + ".png"), ImageFormat.Png);
        }
    }

    static double Ease(double v) => v < .5 ? 2 * v * v : 1 - Math.Pow(-2 * v + 2, 2) / 2;
    static double Clamp01(double v) => Math.Clamp(v, 0, 1);

    // Limit reached: Clawd walks to his bed, hops in, tucks himself in and falls asleep.
    static void Bedtime(Art art, Graphics g, double t, int w, int h)
    {
        int k = art.K, floor = h - 3 * k, bx = 56 * k, by = floor - 30 * k;
        double standX = 43, inX = 71;
        art.LimitBars(bx, by, t < 1600 ? 96 + t / 400 : 100, 64, "text", "wood", true, 1, 1e9, t);
        if (t < 1600)
        {
            double x = 10 + (standX - 10) * (t / 1600);
            art.Bed(bx, by, 18, null, "wood", "dots", "lavender");
            art.Clawd((int)(x * k), floor, new DrawOpts { Emo = "tired", Walking = true, Moving = true, Now = t, Dir = 1, Hat = "none", Outfit = "none" });
        }
        else if (t < 2100)
        {
            double u = (t - 1600) / 500, x = standX + (inX - standX) * u, hh = 3 * u + 12 * 4 * u * (1 - u);
            art.Bed(bx, by, 24, null, "wood", "dots", "lavender");
            art.Clawd((int)(x * k), floor - (int)(hh * k), new DrawOpts { Emo = "tired", Pose = "air", Now = t, Dir = 1, Hat = "none", Outfit = "none" });
        }
        else
        {
            double tuck = Clamp01((t - 2100) / 1000);
            int T = tuck < 1 ? (int)Math.Round(24 - 8 * Ease(tuck)) : 16 + (Math.Sin(t / 650) > 0.2 ? 1 : 0);
            art.BedWithClawd(bx, by, T, tuck > 0.55 ? "sleep" : "tired", t, "wood", "dots", "lavender");
            if (t > 3100)
                for (double z0 = 3100; z0 < t; z0 += 1100)
                {
                    double age = t - z0;
                    if (age > 2000) continue;
                    double zx = bx + 24 * k + 0.015 * age, zh = 26 * k + 0.02 * age;
                    art.Gfx.Pat((int)zx, by + 30 * k - (int)zh, Parts["z"].P, Parts["z"].Pal, -1, -3, false);
                }
        }
    }

    // Picked up, thrown onto the bed, two bounces, hops out happily.
    static void Throw(Art art, Graphics g, double t, int w, int h)
    {
        int k = art.K, floor = h - 3 * k, bx = 56 * k, by = floor - 30 * k;
        art.LimitBars(bx, by, 42, 18, "text", "wood", true, 0.4, 1e9, t);
        art.Bed(bx, by, 18, null, "wood", "dots", "lavender");
        double x, hh; string pose = "stand", emo = "happy"; int dir = 1; bool walk = false;
        if (t < 700) { x = 18 + Math.Sin(t / 120) * 1.5; hh = 30 + Math.Sin(t / 90) * 1; pose = "held"; walk = true; }
        else if (t < 1500) { double u = (t - 700) / 800; x = 18 + (71 - 18) * u; hh = 30 + (6 - 30) * u + 26 * u * (1 - u); pose = "air"; emo = "surprised"; }
        else if (t < 1850) { double u = (t - 1500) / 350; x = 71; hh = 6 + 10 * Math.Sin(Math.PI * u); pose = u < 0.15 ? "squash" : "air"; }
        else if (t < 2100) { double u = (t - 1850) / 250; x = 71; hh = 6 + 4 * Math.Sin(Math.PI * u); pose = u < 0.2 ? "squash" : "air"; }
        else if (t < 2600) { double u = (t - 2100) / 500; x = 71 + (43 - 71) * u; hh = 6 * (1 - u) + 10 * 4 * u * (1 - u); pose = "air"; dir = -1; }
        else { x = 43; hh = 0; dir = -1; emo = t < 3100 ? "star" : "happy"; pose = t < 2700 ? "squash" : "stand"; }
        art.StretchUntil = t > 2700 && t < 3300 ? t + 1 : -1;
        art.Clawd((int)(x * k), floor - (int)(hh * k), new DrawOpts { Emo = emo, Pose = pose, Walking = walk, Moving = walk, Now = t, Dir = dir, Hat = "none", Outfit = "none" });
        if (t > 2650)
            for (int i = 0; i < 7; i++)
            {
                double age = t - 2650; if (age > 900) break;
                double sx = 43 * k + Math.Cos(i * 0.9) * (12 + age * 0.03) * k * 0.5, sh = (12 + i % 3 * 4) * k + age * 0.04 * k;
                art.Gfx.Pat((int)sx, floor - (int)sh, Parts["spark"].P, Parts["spark"].Pal, -1, -3, false);
            }
    }

    // A short tour through his moods.
    static void Moods(Art art, Graphics g, double t, int w, int h)
    {
        int k = art.K, floor = h - 3 * k, cx = w / 2;
        string[] seq = { "neutral", "happy", "love", "surprised", "focused", "star", "confused", "angry", "sad", "wink", "tired" };
        int i = Math.Min(seq.Length - 1, (int)(t / 700));
        double local = t - i * 700;
        string emo = seq[i];
        art.OvE = emo == "wink" ? "wink" : null; art.OvStart = i * 700;
        art.BlinkUntil = emo == "neutral" && local > 400 && local < 540 ? t + 1 : -1;
        art.Clawd(cx, floor, new DrawOpts
        {
            Emo = emo, Now = t, Dir = 1, Hat = "none", Outfit = "none",
            Pose = emo == "focused" ? "sit" : emo == "happy" && local < 200 ? "squash" : "stand",
            Typing = emo == "focused", Excl = emo == "surprised", Sweat = emo == "sad",
        });
        if (emo == "love")
            for (double h0 = 0; h0 < local; h0 += 230)
            {
                double age = local - h0;
                art.Gfx.Pat(cx + (int)((h0 % 3 - 1) * 5 * k), floor - (int)((18 + age * 0.02) * k), Parts["heart"].P, Parts["heart"].Pal, -1, -3, false);
            }
        if (emo == "star")
            for (int s = 0; s < 6; s++)
                art.Gfx.Pat(cx + (int)(Math.Cos(s * 1.05) * (10 + local * 0.01) * k), floor - (int)((14 + Math.Sin(s * 1.05) * 6 + local * 0.006) * k), Parts["spark"].P, Parts["spark"].Pal, -1, -3, false);
        art.OvE = null; art.BlinkUntil = -1;
    }

    // Dressing up: hats, outfits and colors change every 600 ms while he walks in place.
    static void Dressup(Art art, Graphics g, double t, int w, int h)
    {
        int k = art.K, floor = h - 3 * k;
        var looks = new (string Hat, string Outfit, string Color)[]
        {
            ("party", "bow", "classic"), ("tophat", "glasses", "night"), ("beanie", "scarf", "mint"), ("crown", "cape", "gold"),
            ("wizard", "none", "rose"), ("cap", "glasses", "classic"), ("helmet", "scarf", "classic"), ("phones", "none", "mint"),
        };
        int i = Math.Min(looks.Length - 1, (int)(t / 600));
        var l = looks[i];
        double local = t - i * 600;
        art.Clawd(w / 2, floor, new DrawOpts
        {
            Hat = l.Hat, Outfit = l.Outfit, Color = l.Color, Now = t, Dir = (i % 2 == 0) ? 1 : -1,
            Emo = local < 200 ? "surprised" : "happy", Walking = true, Moving = true, Pose = local < 120 ? "squash" : "stand",
        });
    }

    // ---------- Shared drawing helpers ----------
    static Bitmap Canvas(int w, int h, out Graphics g, int floorY)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        g = Graphics.FromImage(bmp);
        g.Clear(Bg);
        if (floorY >= 0) using (var b = new SolidBrush(Floor)) g.FillRectangle(b, 0, floorY, w, h - floorY);
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.None;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        return bmp;
    }

    static void Title(Graphics g, string s, int x, int y)
    {
        using var f = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel);
        using var b = new SolidBrush(Ink);
        g.DrawString(s, f, b, x, y);
    }

    static void Small(Graphics g, string s, int x, int y, Color? c = null, int width = 0, bool center = false)
    {
        using var f = new Font("Segoe UI", 13, FontStyle.Regular, GraphicsUnit.Pixel);
        using var b = new SolidBrush(c ?? Dim);
        if (width > 0)
        {
            using var sf = new StringFormat { Alignment = center ? StringAlignment.Center : StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            g.DrawString(s, f, b, new RectangleF(x, y, width, 20), sf);
        }
        else g.DrawString(s, f, b, x, y);
    }

    // ---------- Screenshots of the windows ----------
    // claude-a-gotchi --ui-test panel|look|bed|settings|welcome out.png [en|de]
    public static int UiShot(string which, string path, string lang)
    {
        Init(lang);
        var st = new Settings { Hat = "beanie", Outfit = "scarf", UsageEnabled = true, Welcomed = true, Language = lang };
        using var pet = new PetForm(st, demo: true);
        Form f = which switch
        {
            "panel" => new QuickPanel(pet, new Point(900, 700)),
            "welcome" => new WelcomeForm(pet),
            _ => new StudioForm(pet, which == "bed" ? 1 : which == "settings" ? 2 : 0),
        };
        f.Opacity = 0;
        f.ShowInTaskbar = false;
        f.Show();
        Application.DoEvents();
        using var bmp = new Bitmap(f.Width, f.Height);
        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
        var origin = f.PointToScreen(Point.Empty);
        var client = new Rectangle(origin.X - f.Left, origin.Y - f.Top, f.ClientSize.Width, f.ClientSize.Height);
        using var crop = bmp.Clone(client, bmp.PixelFormat);
        crop.Save(path, ImageFormat.Png);
        f.Close();
        return 0;
    }
}
