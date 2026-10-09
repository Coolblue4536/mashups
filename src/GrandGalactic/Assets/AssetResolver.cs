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

    public ImageData? Image(AssetRefDef a)
    {
        foreach (var l in a.Lookup)
        {
            if (a.Game == "stellaris" && St != null)
            {
                if (l == "portrait:player")
                {
                    if (PortraitPath != null && ImageData.FromFile(PortraitPath) is { } p) { Hit(a, $"{l} → {Rel(PortraitPath)}"); return p; }
                    continue;
                }
                if (St.Lookup(l) is { } hit && ImageData.FromFile(hit.path, hit.frames) is { } img)
                {
                    Hit(a, $"{l} → {Rel(hit.path)}" + (hit.frames > 1 ? $" (frame 1 of {hit.frames})" : ""));
                    return img;
                }
            }
            if (a.Game == "stacklands" && Sl != null && Sl.LookupImage(l) is { } sh && sh.load() is { } simg)
            {
                Hit(a, $"{l} → {sh.asset.Type} '{sh.asset.Name}' {simg.W}x{simg.H}");
                return simg;
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
