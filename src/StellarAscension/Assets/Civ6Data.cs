using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;

namespace StellarAscension;

/// <summary>One Civ VI database row: column name → value (attribute rows and element rows read the same way).</summary>
public sealed class Civ6Row : Dictionary<string, string>
{
    public Civ6Row() : base(StringComparer.Ordinal) { }
    public string? Get(string col) => TryGetValue(col, out var v) ? v : null;
    public int Int(string col, int def = 0) => int.TryParse(Get(col), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;
    public bool Bool(string col) => Get(col) is "true" or "True" or "1";
}

public sealed record Civ6Civ(string Type, string Leader, string NameLoc, string DescLoc, string AdjLoc, string LeaderNameLoc, bool Female,
    string? CapitalLoc, List<string> CityLocs, (byte r, byte g, byte b) Primary, (byte r, byte g, byte b) Secondary);

/// <summary>
/// Reads what Stellar Ascension needs from the player's own Civilization VI install: the gameplay tables
/// (civilizations, leaders, techs, wonders, natural wonders, terrain), English text, icon atlases from the
/// .blp packages, leader portraits, and fonts. Nothing is copied into the game; it is all read at run time.
/// Base game plus every civilization/expansion DLC the player owns (scenario DLC is left out).
/// </summary>
public sealed class Civ6Data
{
    public readonly string Root;
    public readonly Dictionary<string, List<Civ6Row>> Tables = new(StringComparer.Ordinal);
    public readonly Dictionary<string, string> Text = new(StringComparer.Ordinal);
    /// <summary>ICON_* name → (atlas name, index).</summary>
    public readonly Dictionary<string, (string atlas, int index)> Icons = new(StringComparer.Ordinal);
    /// <summary>Atlas name (e.g. ICON_ATLAS_TECH) → (texture name e.g. Tech128, icon size, icons per row).</summary>
    public readonly Dictionary<string, (string file, int size, int perRow)> AtlasDefs = new(StringComparer.Ordinal);
    /// <summary>LEADER_* → (BLP package name, entry name) for the full-size leader picture.</summary>
    public readonly Dictionary<string, (string package, string entry)> LeaderArt = new(StringComparer.Ordinal);
    readonly List<string> _dataDirs = new();
    readonly List<string> _blpDirs = new();
    readonly Dictionary<string, Civ6Blp?> _blps = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, ImageData?> _atlasCache = new(StringComparer.Ordinal);

    public Civ6Data(string root)
    {
        Root = root;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _dataDirs.Add(Path.Combine(root, "Base", "Assets"));
        _blpDirs.Add(Path.Combine(root, "Base", "Platforms", "Windows", "BLPs"));
        var dlc = Path.Combine(root, "DLC");
        if (Directory.Exists(dlc))
            foreach (var d in Directory.GetDirectories(dlc).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var name = System.IO.Path.GetFileName(d);
                if (name.Contains("Scenario", StringComparison.OrdinalIgnoreCase) || name.Contains("Royale", StringComparison.OrdinalIgnoreCase)) continue;
                _dataDirs.Add(d);
                var blp = Path.Combine(d, "Platforms", "Windows", "BLPs");
                if (Directory.Exists(blp)) _blpDirs.Add(blp);
            }
        foreach (var dir in _dataDirs) LoadXmlTree(dir);
        LoadArtDefs();
        Log.Info($"civ6: {Tables.Count} tables, {Text.Count} text keys, {Icons.Count} icons, {AtlasDefs.Count} atlases, {LeaderArt.Count} leader pictures from {_dataDirs.Count} folders in {sw.ElapsedMilliseconds} ms");
    }

    static readonly HashSet<string> WantTables = new(StringComparer.Ordinal)
    {
        "Civilizations", "Leaders", "CivilizationLeaders", "CityNames", "PlayerColors", "Colors", "Technologies", "TechnologyPrereqs",
        "Buildings", "Features", "Feature_YieldChanges", "Feature_ValidTerrains", "Terrains", "Terrain_YieldChanges", "Projects", "Districts", "Eras",
        "IconDefinitions", "IconTextureAtlases", "Resources", "Resource_YieldChanges", "Improvements", "Difficulties"
    };

    void LoadXmlTree(string dir)
    {
        foreach (var f in Directory.EnumerateFiles(dir, "*.xml", SearchOption.AllDirectories))
        {
            var rel = f.Substring(dir.Length).Replace('\\', '/');
            if (rel.Contains("/Scenarios/", StringComparison.OrdinalIgnoreCase) || rel.Contains("/Benchmark/", StringComparison.OrdinalIgnoreCase)) continue;
            bool text = rel.Contains("/Text/", StringComparison.OrdinalIgnoreCase) || rel.Contains("Text", StringComparison.Ordinal);
            // Only English text; other languages live in Vanilla_xx_XX.xml / *_xx_XX.xml files.
            if (Regex.IsMatch(rel, @"_(de_DE|es_ES|fr_FR|it_IT|ja_JP|ko_KR|pl_PL|pt_BR|ru_RU|zh_Hans_CN|zh_Hant_HK)\.xml$", RegexOptions.IgnoreCase)) continue;
            try { ReadXml(f); }
            catch (Exception e) { if (text) Log.Info($"civ6: skipped {rel} ({e.Message})"); }
        }
    }

    void ReadXml(string file)
    {
        var doc = new XmlDocument { XmlResolver = null };
        using (var r = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreComments = true }))
            doc.Load(r);
        var root = doc.DocumentElement;
        if (root == null) return;
        foreach (XmlNode table in root.ChildNodes)
        {
            if (table is not XmlElement t) continue;
            if (t.Name is "BaseGameText" or "LocalizedText" or "EnglishText")
            {
                foreach (XmlNode row in t.ChildNodes)
                {
                    if (row is not XmlElement re || re.Name != "Row" && re.Name != "Replace") continue;
                    var lang = re.GetAttribute("Language");
                    if (lang.Length > 0 && lang != "en_US") continue;
                    var tag = re.GetAttribute("Tag");
                    var val = re["Text"]?.InnerText ?? re.GetAttribute("Text");
                    if (tag.Length > 0 && !string.IsNullOrEmpty(val)) Text[tag] = val;
                }
                continue;
            }
            if (!WantTables.Contains(t.Name)) continue;
            if (!Tables.TryGetValue(t.Name, out var list)) Tables[t.Name] = list = new();
            foreach (XmlNode row in t.ChildNodes)
            {
                if (row is not XmlElement re || re.Name != "Row" && re.Name != "Replace") continue;
                var r = new Civ6Row();
                foreach (XmlAttribute a in re.Attributes) r[a.Name] = a.Value;
                foreach (XmlNode c in re.ChildNodes)
                    if (c is XmlElement ce && !ce.ChildNodes.OfType<XmlElement>().Any()) r[ce.Name] = ce.InnerText;
                list.Add(r);
            }
        }
    }

    public IEnumerable<Civ6Row> Rows(string table) => Tables.TryGetValue(table, out var l) ? l : Enumerable.Empty<Civ6Row>();
    public Civ6Row? Row(string table, string col, string val) => Rows(table).LastOrDefault(r => r.Get(col) == val);

    static readonly Regex Markup = new(@"\[ICON_[^\]]*\]|\[COLOR[^\]]*\]|\[ENDCOLOR\]|\[STYLE[^\]]*\]|\[ENDSTYLE\]", RegexOptions.Compiled);

    /// <summary>English text for a LOC_ key with Civ VI markup removed ([NEWLINE] becomes a line break).</summary>
    public string T(string? key, string? fallback = null)
    {
        if (key == null) return fallback ?? "";
        if (!Text.TryGetValue(key, out var v)) return fallback ?? key;
        v = v.Replace("[NEWLINE]", "\n");
        v = Markup.Replace(v, "");
        // Gendered/plural forms like "{1_Name}" or "Rome|Romes" — keep the first form.
        if (v.Contains('|') && !v.Contains(' ')) v = v.Split('|')[0];
        return Regex.Replace(v, @"[ \t]{2,}", " ").Trim();
    }

    // ---------------------------------------------------------------------------- icons

    void LoadArtDefs()
    {
        foreach (var r in Rows("IconTextureAtlases"))
            if (r.Get("Name") is { } n && r.Get("Filename") is { } file)
            {
                var size = r.Int("IconSize");
                // The same atlas name is listed once per icon size; keep the biggest.
                if (!AtlasDefs.TryGetValue(n, out var cur) || size > cur.size)
                    AtlasDefs[n] = (Path.GetFileNameWithoutExtension(file), size, Math.Max(1, r.Int("IconsPerRow", 1)));
            }
        foreach (var r in Rows("IconDefinitions"))
            if (r.Get("Name") is { } n && r.Get("Atlas") is { } atlas)
                Icons[n] = (atlas, r.Int("Index"));
        foreach (var dir in _dataDirs.Select(d => d.EndsWith("Assets") ? Path.GetDirectoryName(d)! : d))
        {
            var art = Path.Combine(dir, "ArtDefs", "FallbackLeaders.artdef");
            if (!File.Exists(art)) continue;
            try
            {
                var doc = new XmlDocument { XmlResolver = null };
                doc.Load(art);
                foreach (XmlElement el in doc.GetElementsByTagName("Element"))
                {
                    var name = el["m_Name"]?.GetAttribute("text");
                    if (name == null || !name.StartsWith("LEADER_")) continue;
                    var entry = el.GetElementsByTagName("m_EntryName").Cast<XmlElement>().FirstOrDefault()?.GetAttribute("text");
                    var pkg = el.GetElementsByTagName("m_BLPPackage").Cast<XmlElement>().FirstOrDefault()?.GetAttribute("text");
                    if (entry != null && pkg != null) LeaderArt[name] = (pkg, entry);
                }
            }
            catch (Exception e) { Log.Info($"civ6: could not read {art} ({e.Message})"); }
        }
    }

    Civ6Blp? Blp(string path)
    {
        if (!_blps.TryGetValue(path, out var b)) _blps[path] = b = Civ6Blp.Open(path);
        return b;
    }

    IEnumerable<string> BlpFiles(string sub) =>
        _blpDirs.SelectMany(d => Directory.Exists(Path.Combine(d, sub)) ? Directory.EnumerateFiles(Path.Combine(d, sub), "*.blp") : Enumerable.Empty<string>());

    /// <summary>The whole atlas texture (e.g. "Tech128"), from whichever UI package has it.</summary>
    public ImageData? AtlasTexture(string file)
    {
        if (_atlasCache.TryGetValue(file, out var img)) return img;
        img = null;
        foreach (var p in BlpFiles("UI"))
            if (Blp(p) is { } b && b.FindAtlas(file) is { } a && b.DecodeAtlas(a) is { } dec) { img = dec; break; }
        if (img == null) Log.Info($"civ6: atlas {file} not found");
        return _atlasCache[file] = img;
    }

    /// <summary>One icon (e.g. ICON_TECH_ROCKETRY), cut out of the biggest atlas that has it.</summary>
    public ImageData? Icon(string name)
    {
        if (!Icons.TryGetValue(name, out var ic)) return null;
        // Several atlases share a name at different sizes (ICON_ATLAS_TECH at 128, 256...). Try the defined one.
        if (!AtlasDefs.TryGetValue(ic.atlas, out var def)) return null;
        var tex = AtlasTexture(def.file);
        if (tex == null) return null;
        int col = ic.index % def.perRow, row = ic.index / def.perRow;
        if ((col + 1) * def.size > tex.W || (row + 1) * def.size > tex.H) return null;
        return tex.Crop(col * def.size, row * def.size, def.size, def.size);
    }

    /// <summary>The leader's full-size neutral picture (the one Civ VI shows when the 3D leader can't load).</summary>
    public ImageData? LeaderPicture(string leader)
    {
        if (!LeaderArt.TryGetValue(leader, out var a)) return null;
        foreach (var d in _blpDirs)
        {
            var p = Path.Combine(d, a.package + ".blp");
            if (File.Exists(p) && Blp(p) is { } b && b.FindTexture(a.entry) is { } t && b.DecodeTexture(t) is { } img) return img;
        }
        return null;
    }

    /// <summary>A Civ VI font file (Civ VI ships TrueType/OpenType fonts in Base/Assets/UI/Fonts).</summary>
    public string? FontFile(string name)
    {
        var dir = Path.Combine(Root, "Base", "Assets", "UI", "Fonts");
        if (!Directory.Exists(dir)) return null;
        return Directory.EnumerateFiles(dir, name, SearchOption.AllDirectories).FirstOrDefault();
    }

    // ---------------------------------------------------------------------------- civilizations

    (byte, byte, byte) ColorOf(string? name)
    {
        var c = name == null ? null : Row("Colors", "Type", name)?.Get("Color");
        var p = c?.Split(',');
        if (p == null || p.Length < 3) return (128, 128, 128);
        static byte B(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? (byte)Math.Clamp(f <= 1 && s.Contains('.') ? f * 255 : f, 0, 255) : (byte)128;
        return (B(p[0]), B(p[1]), B(p[2]));
    }

    /// <summary>Every playable civilization + leader pair the player owns.</summary>
    public List<Civ6Civ> Civilizations()
    {
        var full = Rows("Civilizations").Where(r => r.Get("StartingCivilizationLevelType") == "CIVILIZATION_LEVEL_FULL_CIV")
            .GroupBy(r => r.Get("CivilizationType")!).ToDictionary(g => g.Key, g => g.Last());
        var list = new List<Civ6Civ>();
        var seen = new HashSet<string>();
        foreach (var cl in Rows("CivilizationLeaders"))
        {
            var civ = cl.Get("CivilizationType"); var leader = cl.Get("LeaderType");
            if (civ == null || leader == null || !full.TryGetValue(civ, out var c) || !seen.Add(civ + "/" + leader)) continue;
            if (leader.Contains("BARBARIAN") || leader == "LEADER_DEFAULT") continue;
            var l = Row("Leaders", "LeaderType", leader);
            if (l == null || T(l.Get("Name")) is not { Length: > 0 } ln || ln.StartsWith("LOC_")) continue;
            var cities = Rows("CityNames").Where(r => r.Get("CivilizationType") == civ).Select(r => r.Get("CityName")!).Where(x => x != null).Distinct().ToList();
            var colors = Row("PlayerColors", "Type", leader);
            list.Add(new Civ6Civ(civ, leader, c.Get("Name") ?? civ, c.Get("Description") ?? civ, c.Get("Adjective") ?? civ, l.Get("Name")!,
                l.Get("Sex") == "Female", cl.Get("CapitalName"), cities, ColorOf(colors?.Get("PrimaryColor")), ColorOf(colors?.Get("SecondaryColor"))));
        }
        list.Sort((a, b) => string.Compare(T(a.DescLoc), T(b.DescLoc), StringComparison.Ordinal));
        return list;
    }

    /// <summary>Base-game technologies with their era and cost, and prerequisites (TechnologyPrereqs).</summary>
    public List<(string type, string era, int cost)> Techs() =>
        Rows("Technologies").GroupBy(r => r.Get("TechnologyType")!).Select(g => g.First())
            .Select(r => (r.Get("TechnologyType")!, r.Get("EraType") ?? "", r.Int("Cost"))).ToList();

    public Dictionary<string, List<string>> TechPrereqs()
    {
        var d = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var r in Rows("TechnologyPrereqs"))
            if (r.Get("Technology") is { } t && r.Get("PrereqTech") is { } p)
            {
                if (!d.TryGetValue(t, out var l)) d[t] = l = new();
                if (!l.Contains(p)) l.Add(p);
            }
        return d;
    }
}
