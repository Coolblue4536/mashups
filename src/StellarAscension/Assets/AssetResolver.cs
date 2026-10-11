using System.Globalization;

namespace StellarAscension;

/// <summary>Turns an asset_refs row or a Civ VI icon name into a picture from the player's own installs.</summary>
public sealed class AssetResolver
{
    public readonly Civ6Data? Civ6;
    public readonly StellarisData? St;
    public readonly HashSet<string> Misses = new();

    public AssetResolver(Civ6Data? civ6, StellarisData? st) { Civ6 = civ6; St = st; }

    /// <summary>"file:gfx/x.dds@0.4,0,0.35,1" crops to that part (fractions of width and height).</summary>
    static (string lookup, float[]? crop) SplitCrop(string l)
    {
        int at = l.LastIndexOf('@');
        if (at < 0) return (l, null);
        var f = l[(at + 1)..].Split(',').Select(v => float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : -1).ToArray();
        return f.Length == 4 && f.All(x => x >= 0 && x <= 1) ? (l[..at], f) : (l, null);
    }

    static ImageData Cropped(ImageData img, float[]? f) =>
        f == null ? img : img.Crop((int)(f[0] * img.W), (int)(f[1] * img.H), Math.Max(1, (int)(f[2] * img.W)), Math.Max(1, (int)(f[3] * img.H)));

    /// <summary>Small icons are enlarged once with a high-quality filter, so the GPU only ever shrinks them.</summary>
    public static ImageData Crisp(ImageData img)
    {
        int m = Math.Max(img.W, img.H);
        return m >= 96 ? img : img.UpscaleSharp(Math.Clamp((int)MathF.Ceiling(128f / m), 2, 6), m <= 32 ? 0.2f : 0.5f);
    }

    void Miss(string what)
    {
        if (Misses.Add(what)) Log.Info($"asset miss: {what}");
    }

    /// <summary>A Stellaris picture from the asset_refs sheet.</summary>
    public ImageData? Stellaris(string assetId)
    {
        if (St == null || !Defs.AssetRef.TryGetValue(assetId, out var a)) { Miss(assetId); return null; }
        foreach (var full in a.Lookup)
        {
            var (l, crop) = SplitCrop(full);
            if (St.Lookup(l) is { } hit && ImageData.FromFile(hit.path, hit.frames, hit.frame) is { } img) return Crisp(Cropped(img, crop));
        }
        Miss(assetId);
        return null;
    }

    public ImageData? PlanetIcon(string planetClass)
    {
        if (St?.Lookup("planetclass:" + planetClass) is { } hit && ImageData.FromFile(hit.path, hit.frames, hit.frame) is { } img) return Crisp(img);
        Miss("planet " + planetClass);
        return null;
    }

    public ImageData? Portrait(string key)
    {
        if (St?.PortraitFile(key) is { } p && ImageData.FromFile(p) is { } img) return img;
        Miss("portrait " + key);
        return null;
    }

    public ImageData? Civ6Icon(string icon)
    {
        var img = Civ6?.Icon(icon);
        if (img == null) Miss(icon);
        return img;
    }

    public ImageData? LeaderPicture(string leader)
    {
        var img = Civ6?.LeaderPicture(leader);
        if (img == null) Miss("leader picture " + leader);
        return img;
    }

    public string? StText(string key) => St?.Text(key);
}
