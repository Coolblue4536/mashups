using System.Text.RegularExpressions;

namespace GrandGalactic;

public sealed record SpriteInfo(string Name, string TextureFile, int Frames);
public sealed record PortraitInfo(string Name, string Group, string TextureFile);

/// <summary>Reads what the mashup needs from the player's own Stellaris install: names, sprites, planet icons, portraits.</summary>
public sealed class StellarisData
{
    public readonly string Root;
    public readonly Dictionary<string, string> Loc = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, SpriteInfo> Sprites = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string> PlanetIcons = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<PortraitInfo> Portraits = new();
    /// <summary>Lower-case path relative to the install (forward slashes) → real path, for every image under gfx/.</summary>
    public readonly Dictionary<string, string> Files = new();

    static readonly Regex LocLine = new(@"^\s*([A-Za-z0-9_.\-]+):\d*\s*""(.*)""\s*(#.*)?$", RegexOptions.Compiled);
    static readonly Regex Color = new(@"§.|£[^£ ]*£?", RegexOptions.Compiled);
    static readonly Regex Ref = new(@"\$([A-Za-z0-9_.\-|]+)\$", RegexOptions.Compiled);

    public StellarisData(string root)
    {
        Root = root;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        IndexFiles();
        LoadLoc();
        LoadSprites();
        LoadPlanetClasses();
        LoadPortraits();
        Log.Info($"stellaris: {Files.Count} images, {Loc.Count} loc keys, {Sprites.Count} sprites, {PlanetIcons.Count} planet classes, {Portraits.Count} portraits in {sw.ElapsedMilliseconds} ms");
    }

    static string Rel(string root, string p) => Path.GetRelativePath(root, p).Replace('\\', '/').ToLowerInvariant();

    void IndexFiles()
    {
        var gfx = Path.Combine(Root, "gfx");
        if (!Directory.Exists(gfx)) return;
        foreach (var f in Directory.EnumerateFiles(gfx, "*.*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext is ".dds" or ".png" or ".tga") Files[Rel(Root, f)] = f;
        }
    }

    public string? FilePath(string rel)
    {
        rel = rel.Replace('\\', '/').ToLowerInvariant();
        if (Files.TryGetValue(rel, out var p)) return p;
        // .gfx files sometimes name .tga while the install ships .dds, and vice versa.
        foreach (var ext in new[] { ".dds", ".png", ".tga" })
            if (Files.TryGetValue(Path.ChangeExtension(rel, ext), out p)) return p;
        return null;
    }

    void LoadLoc()
    {
        var dir = Path.Combine(Root, "localisation");
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir, "*l_english.yml", SearchOption.AllDirectories))
        {
            foreach (var line in File.ReadLines(f))
            {
                var m = LocLine.Match(line);
                if (m.Success) Loc[m.Groups[1].Value] = m.Groups[2].Value;
            }
        }
    }

    public string? Text(string key, int depth = 0)
    {
        if (!Loc.TryGetValue(key, out var v)) return null;
        v = Color.Replace(v, "");
        if (depth < 3) v = Ref.Replace(v, m => Text(m.Groups[1].Value.Split('|')[0], depth + 1) ?? "");
        v = v.Replace("\\n", " ").Trim();
        return v.Length == 0 ? null : v;
    }

    void LoadSprites()
    {
        var dir = Path.Combine(Root, "interface");
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir, "*.gfx", SearchOption.AllDirectories))
            foreach (var n in CwNode.ParseFile(f).Descendants())
            {
                if (n.Children == null || n.Key == null || !n.Key.EndsWith("spriteType", StringComparison.OrdinalIgnoreCase)) continue;
                var name = n.Get("name");
                var tex = n.Get("texturefile") ?? n.Get("textureFile");
                if (name == null || tex == null) continue;
                int.TryParse(n.Get("noOfFrames"), out var frames);
                Sprites[name] = new SpriteInfo(name, tex, Math.Max(1, frames));
            }
    }

    void LoadPlanetClasses()
    {
        var dir = Path.Combine(Root, "common", "planet_classes");
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir, "*.txt"))
            foreach (var n in CwNode.ParseFile(f).Kids)
                if (n.Key != null && n.Key.StartsWith("pc_") && n.Get("icon") is { } icon)
                    PlanetIcons[n.Key] = icon;
    }

    void LoadPortraits()
    {
        var dir = Path.Combine(Root, "gfx", "portraits", "portraits");
        if (!Directory.Exists(dir)) return;
        var seen = new HashSet<string>();
        foreach (var f in Directory.EnumerateFiles(dir, "*.txt"))
            foreach (var n in CwNode.ParseFile(f).Descendants())
            {
                var tex = n.Children != null ? n.Get("texturefile") : null;
                if (tex == null || n.Key == null) continue;
                var rel = tex.Replace('\\', '/').ToLowerInvariant();
                if (!rel.StartsWith("gfx/models/portraits/") || FilePath(rel) == null || !seen.Add(rel)) continue;
                var parts = rel.Split('/');
                Portraits.Add(new PortraitInfo(n.Key, parts.Length > 4 ? parts[3] : "species", rel));
            }
        Portraits.Sort((a, b) => string.CompareOrdinal(a.Group + a.Name, b.Group + b.Name));
    }

    /// <summary>Resolve one lookup string from asset_refs (file:, sprite:, find:, planetclass:). Returns (path, frames) or null.</summary>
    public (string path, int frames)? Lookup(string lookup)
    {
        int c = lookup.IndexOf(':');
        if (c < 0) return null;
        string kind = lookup[..c], arg = lookup[(c + 1)..];
        switch (kind)
        {
            case "file":
                return FilePath(arg) is { } p ? (p, 1) : null;
            case "sprite":
                return SpriteFile(arg);
            case "planetclass":
                return PlanetIcons.TryGetValue(arg, out var icon) ? SpriteFile(icon) : null;
            case "find":
            {
                var needle = arg.ToLowerInvariant();
                foreach (var s in Sprites.Values.Where(s => s.Name.ToLowerInvariant().Contains(needle)).OrderBy(s => s.Name.Length).ThenBy(s => s.Name))
                    if (SpriteFile(s.Name) is { } hit) return hit;
                var file = Files.Keys.Where(k => k.Contains(needle)).OrderBy(k => k.Length).ThenBy(k => k, StringComparer.Ordinal).FirstOrDefault();
                return file != null ? (Files[file], 1) : null;
            }
        }
        return null;
    }

    (string, int)? SpriteFile(string name) =>
        Sprites.TryGetValue(name, out var s) && FilePath(s.TextureFile) is { } p ? (p, s.Frames) : null;
}
