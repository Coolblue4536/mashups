namespace GrandGalactic;

/// <summary>Turns an asset_refs row into real content from the player's games, trying its lookups in order.</summary>
public sealed class AssetResolver
{
    public readonly StellarisData? St;
    public readonly StacklandsData? Sl;
    public string? PortraitPath;
    public readonly Dictionary<string, string> Hits = new();
    public readonly HashSet<string> Misses = new();

    public AssetResolver(StellarisData? st, StacklandsData? sl) { St = st; Sl = sl; }

    void Hit(AssetRefDef a, string what) => Hits[a.Id] = what;

    void Miss(AssetRefDef a)
    {
        if (Misses.Add(a.Id)) Log.Info($"asset miss: {a.Id} ({a.Game} {a.Kind}; tried {string.Join(", ", a.Lookup)})");
    }

    /// <summary>"file:gfx/x.dds@0.4,0,0.35,1" crops the picture to that part (fractions of its width and height),
    /// e.g. the ship out of a wide event picture.</summary>
    static (string lookup, float[]? crop) SplitCrop(string l)
    {
        int at = l.LastIndexOf('@');
        if (at < 0) return (l, null);
        var f = l[(at + 1)..].Split(',').Select(v => float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) ? x : -1).ToArray();
        return f.Length == 4 && f.All(x => x >= 0 && x <= 1) ? (l[..at], f) : (l, null);
    }

    static ImageData Cropped(ImageData img, float[]? f) =>
        f == null ? img : img.Crop((int)(f[0] * img.W), (int)(f[1] * img.H), Math.Max(1, (int)(f[2] * img.W)), Math.Max(1, (int)(f[3] * img.H)));

    /// <summary>Small icons are enlarged with a high-quality filter once, at load, so the GPU only ever shrinks them.</summary>
    static ImageData Crisp(ImageData img)
    {
        int m = Math.Max(img.W, img.H);
        // The tiniest icons get a gentler sharpen: a strong one exaggerates their pixel steps.
        return m >= 128 ? img : img.UpscaleSharp(Math.Clamp((int)MathF.Ceiling(192f / m), 2, 8), m <= 32 ? 0.2f : 0.55f);
    }

    public ImageData? Image(AssetRefDef a)
    {
        foreach (var full in a.Lookup)
        {
            var (l, crop) = SplitCrop(full);
            if (a.Game == "stellaris" && St != null)
            {
                if (l == "portrait:player")
                {
                    if (PortraitPath != null && ImageData.FromFile(PortraitPath) is { } p) { Hit(a, $"{l} → {Rel(PortraitPath)}"); return p; }
                    continue;
                }
                if (St.Lookup(l) is { } hit && ImageData.FromFile(hit.path, hit.frames, hit.frame) is { } img)
                {
                    Hit(a, $"{full} → {Rel(hit.path)}" + (hit.frames > 1 ? $" (frame {hit.frame} of {hit.frames})" : "") + (crop != null ? " (cropped)" : ""));
                    return Crisp(Cropped(img, crop));
                }
            }
            if (a.Game == "stacklands" && Sl != null && Sl.LookupImage(l) is { } sh && sh.load() is { } simg)
            {
                Hit(a, $"{full} → {sh.asset.Type} '{sh.asset.Name}' {simg.W}x{simg.H}");
                return Crisp(Cropped(simg, crop));
            }
        }
        Miss(a);
        return null;
    }

    public (byte[] data, string ext)? Sound(AssetRefDef a)
    {
        if (Sl == null) { Miss(a); return null; }
        foreach (var l in a.Lookup)
            if (Sl.LookupAudio(l) is { } h && h.load() is { } snd)
            {
                Hit(a, $"{l} → AudioClip '{h.asset.Name}' ({snd.ext}, {snd.data.Length} bytes)");
                return snd;
            }
        Miss(a);
        return null;
    }

    public byte[]? Font(AssetRefDef a)
    {
        if (Sl == null) { Miss(a); return null; }
        foreach (var l in a.Lookup)
            if (Sl.LookupFont(l) is { } h && h.load() is { } bytes)
            {
                Hit(a, $"{l} → Font '{h.asset.Name}' ({bytes.Length} bytes)");
                return bytes;
            }
        Miss(a);
        return null;
    }

    string Rel(string p) => St != null ? Path.GetRelativePath(St.Root, p).Replace('\\', '/') : p;

    /// <summary>The player-facing name of a card: Stellaris's own localisation when it has the key.</summary>
    public string CardName(string id)
    {
        var d = Defs.Card[id];
        return St?.Text(d.NameLoc) is { Length: > 0 and < 40 } t ? t : d.Name;
    }
}
