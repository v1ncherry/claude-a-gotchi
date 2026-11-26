using System.Globalization;
using System.Text.RegularExpressions;

namespace ClaudeAGotchi;

// One costume file: a hat, an outfit, a body color, a blanket cover or a pillow.
// Format: see CONTRIBUTING.md. Short version:
//
//   type: hat
//   id: party
//   name: Party hat
//   name.de: Partyhut
//   at: -5, -9
//   colors:
//     P = #e25c9a
//   pixels:
//     ..PP..
//
sealed class Costume
{
    public string Type, Id, Source, Author;
    public readonly Dictionary<string, string> Names = new();   // "" = default (English), "de" = German, ...
    public int Order = 1000;
    public string[] Pixels;
    public readonly Dictionary<char, Color> Pal = new();
    public int AtX, AtY;
    public string Mirror = "none";     // hat/outfit: "none", "left" (mirror when Clawd looks left), "right"
    public string Layer = "front";     // outfit: "back" (behind the body), "face" (under the eyes), "front"
    public bool InBed;                 // outfit: still worn while sleeping
    public int Flutter = -1;           // outfit: rows from here on wave while Clawd moves (cape)
    public Color Body, Base, Band, Main, Shade;
    public bool BuiltIn;

    public string Name => Loc.Pick(Names, Id);
    public int W => Pixels?.Length > 0 ? Pixels[0].Length : 0;
    public int H => Pixels?.Length ?? 0;
}

static class Costumes
{
    public static readonly string[] Types = { "hat", "outfit", "color", "cover", "pillow" };
    public static readonly Dictionary<string, List<Costume>> All = Types.ToDictionary(t => t, _ => new List<Costume>());
    public static readonly List<string> Errors = new();

    static readonly Costume NoHat = new() { Type = "hat", Id = "none", Order = -1, BuiltIn = true };
    static readonly Costume NoOutfit = new() { Type = "outfit", Id = "none", Order = -1, BuiltIn = true };

    public static string UserFolder => Path.Combine(AppPaths.Data, "costumes");

    public static IReadOnlyList<Costume> Hats => All["hat"];
    public static IReadOnlyList<Costume> Outfits => All["outfit"];
    public static IReadOnlyList<Costume> Colors => All["color"];
    public static IReadOnlyList<Costume> Covers => All["cover"];
    public static IReadOnlyList<Costume> Pillows => All["pillow"];

    public static Costume Get(string type, string id) => All[type].FirstOrDefault(c => c.Id == id);
    // Falls back to the first entry, so a deleted costume never breaks drawing.
    public static Costume GetOr(string type, string id) => Get(type, id) ?? All[type].FirstOrDefault();

    public static string NoneName(string type) => Loc.T(type == "hat" ? "look.noHat" : "look.noOutfit");

    public static void Load()
    {
        foreach (var l in All.Values) l.Clear();
        Errors.Clear();
        All["hat"].Add(NoHat);
        All["outfit"].Add(NoOutfit);
        var asm = typeof(Costumes).Assembly;
        foreach (var res in asm.GetManifestResourceNames().Where(n => n.Replace('\\', '/').StartsWith("costumes/")).OrderBy(n => n))
        {
            using var s = asm.GetManifestResourceStream(res);
            using var r = new StreamReader(s);
            var c = Parse(r.ReadToEnd(), res.Replace('\\', '/'), Errors);
            if (c != null) { c.BuiltIn = true; Put(c); }
        }
        try
        {
            if (Directory.Exists(UserFolder))
                foreach (var f in Directory.GetFiles(UserFolder, "*.txt", SearchOption.AllDirectories).OrderBy(f => f))
                {
                    var c = Parse(File.ReadAllText(f), f, Errors);
                    if (c != null) Put(c);
                }
        }
        catch (Exception ex) { Errors.Add(UserFolder + ": " + ex.Message); }
        foreach (var l in All.Values) l.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
        foreach (var e in Errors) AppPaths.LogLine("Costume: " + e);
    }

    // Adds a costume or replaces one with the same type and id (own files override built-in ones).
    public static void Put(Costume c)
    {
        var list = All[c.Type];
        int i = list.FindIndex(x => x.Id == c.Id);
        if (i >= 0) list[i] = c; else list.Add(c);
    }

    static readonly Regex IdRx = new("^[a-z0-9][a-z0-9-]{0,31}$");
    static readonly Regex KeyRx = new(@"^([a-zA-Z][a-zA-Z0-9.\-]*)\s*:\s*(.*)$");
    static readonly Regex ColorRx = new(@"^([A-Za-z0-9])\s*=\s*(#[0-9a-fA-F]{6})$");
    const int MaxW = 40, MaxH = 30;

    // Reads a costume file. Problems are added to errors (with the file name); returns null if it cannot be used.
    public static Costume Parse(string text, string source, List<string> errors)
    {
        var c = new Costume { Source = source };
        var problems = new List<string>();
        var rows = new List<string>();
        var keys = new Dictionary<string, string>();
        string section = null;
        int lineNo = 0;
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            lineNo++;
            string line = raw.Trim();
            if (line.Length == 0) { if (section == "pixels" && rows.Count > 0) section = "done"; continue; }
            if (line.StartsWith('#')) continue;
            if (section == "colors")
            {
                var cm = ColorRx.Match(line);
                if (cm.Success) { c.Pal[cm.Groups[1].Value[0]] = Sprites.C(cm.Groups[2].Value); continue; }
                section = null;
            }
            if (section == "pixels")
            {
                if (!KeyRx.IsMatch(line)) { rows.Add(line); continue; }
                section = "done";
            }
            var m = KeyRx.Match(line);
            if (!m.Success) { problems.Add($"line {lineNo}: expected \"key: value\", got \"{Short(line)}\""); continue; }
            string key = m.Groups[1].Value.ToLowerInvariant(), val = m.Groups[2].Value.Trim();
            if (key == "colors") { section = "colors"; continue; }
            if (key == "pixels") { section = "pixels"; if (val.Length > 0) rows.Add(val); continue; }
            if (key.StartsWith("name"))
            {
                string lang = key.Length > 5 && key[4] == '.' ? key[5..] : "";
                if (val.Length == 0 || val.Length > 40) problems.Add($"line {lineNo}: names must be 1 to 40 characters");
                else c.Names[lang] = val;
                continue;
            }
            keys[key] = val;
        }

        c.Type = Get(keys, "type")?.ToLowerInvariant();
        c.Id = Get(keys, "id");
        c.Author = Get(keys, "author");
        if (c.Type == null || !Types.Contains(c.Type)) problems.Add("\"type\" must be one of: " + string.Join(", ", Types));
        if (c.Id == null || !IdRx.IsMatch(c.Id)) problems.Add("\"id\" must be lowercase letters, digits and dashes (for example: pirate-hat)");
        else if (c.Id == "none") problems.Add("\"none\" is reserved");
        if (!c.Names.ContainsKey("")) problems.Add("\"name\" (English) is missing");
        if (Get(keys, "order") is string ord && int.TryParse(ord, NumberStyles.Integer, CultureInfo.InvariantCulture, out int o)) c.Order = o;

        bool needsPixels = c.Type is "hat" or "outfit" or "cover";
        if (needsPixels)
        {
            if (rows.Count == 0) problems.Add("\"pixels\" is missing");
            else
            {
                int w = rows[0].Length;
                if (rows.Any(r => r.Length != w)) problems.Add("all pixel rows must have the same length");
                if (w > MaxW || rows.Count > MaxH) problems.Add($"the picture may be at most {MaxW} x {MaxH} pixels");
                var unknown = rows.SelectMany(r => r).Where(ch => ch != '.' && !c.Pal.ContainsKey(ch)).Distinct().ToList();
                if (unknown.Count > 0) problems.Add("these letters have no color under \"colors\": " + string.Join(" ", unknown));
                c.Pixels = rows.ToArray();
            }
        }

        switch (c.Type)
        {
            case "hat":
            case "outfit":
                if (!Point2(Get(keys, "at"), out c.AtX, out c.AtY)) problems.Add("\"at\" must be two numbers, for example: at: -5, -9");
                c.Mirror = (Get(keys, "mirror") ?? "none").ToLowerInvariant();
                if (c.Mirror is not ("none" or "left" or "right")) problems.Add("\"mirror\" must be none, left or right");
                if (c.Type == "outfit")
                {
                    c.Layer = (Get(keys, "layer") ?? "front").ToLowerInvariant();
                    if (c.Layer is not ("back" or "face" or "front")) problems.Add("\"layer\" must be back, face or front");
                    c.InBed = YesNo(Get(keys, "in-bed"));
                    if (Get(keys, "flutter") is string fl)
                    {
                        if (int.TryParse(fl, out int f) && f >= 0) c.Flutter = f;
                        else problems.Add("\"flutter\" must be a row number");
                    }
                }
                break;
            case "color":
                if (!Hex(Get(keys, "body"), out c.Body)) problems.Add("\"body\" must be a color like #da7756");
                break;
            case "cover":
                if (!Hex(Get(keys, "base"), out c.Base)) problems.Add("\"base\" must be a color like #4f6bd8");
                if (!Hex(Get(keys, "band"), out c.Band)) problems.Add("\"band\" must be a color like #8aa0ee");
                break;
            case "pillow":
                if (!Hex(Get(keys, "main"), out c.Main)) problems.Add("\"main\" must be a color like #dcd7f3");
                if (!Hex(Get(keys, "shade"), out c.Shade)) problems.Add("\"shade\" must be a color like #b4acdc");
                break;
        }

        if (problems.Count == 0) return c;
        foreach (var p in problems) errors.Add(source + ": " + p);
        return null;
    }

    static string Get(Dictionary<string, string> k, string key) => k.TryGetValue(key, out var v) && v.Length > 0 ? v : null;
    static string Short(string s) => s.Length > 30 ? s[..30] + "…" : s;
    static bool YesNo(string v) => v != null && (v.Equals("yes", StringComparison.OrdinalIgnoreCase) || v.Equals("true", StringComparison.OrdinalIgnoreCase));

    static bool Hex(string v, out Color c)
    {
        c = Color.Empty;
        if (v == null || !Regex.IsMatch(v, "^#[0-9a-fA-F]{6}$")) return false;
        c = Sprites.C(v);
        return true;
    }

    static bool Point2(string v, out int x, out int y)
    {
        x = y = 0;
        if (v == null) return false;
        var p = v.Split(',');
        return p.Length == 2 && int.TryParse(p[0].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out x)
                             && int.TryParse(p[1].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out y);
    }
}
