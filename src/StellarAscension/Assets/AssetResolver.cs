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

    /// <summary>An animated portrait's texture holds the painted figure plus loose parts (jaws, eyelids, sometimes the head)
    /// that the game moves separately. Keep the figure (the largest connected opaque area), and say how whole it looks:
    /// the share of the opaque pixels it holds, and whether it is portrait-shaped.</summary>
    static (ImageData img, bool whole) Figure(ImageData img)
    {
        int w = img.W, h = img.H;
        var seen = new bool[w * h];
        var label = new int[w * h];
        int bestCount = 0, bestLabel = 0, next = 0, total = 0, bx0 = 0, by0 = 0, bx1 = w - 1, by1 = h - 1;
        var stack = new Stack<int>();
        for (int start = 0; start < w * h; start++)
        {
            if (seen[start] || img.Rgba[start * 4 + 3] < 40) continue;
            int count = 0, x0 = w, y0 = h, x1 = 0, y1 = 0, id = ++next;
            stack.Push(start); seen[start] = true;
            while (stack.Count > 0)
            {
                int i = stack.Pop(), x = i % w, y = i / w;
                count++; label[i] = id; x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                foreach (var j in new[] { i - 1, i + 1, i - w, i + w })
                {
                    if (j < 0 || j >= w * h || seen[j] || Math.Abs(j % w - x) > 1 || img.Rgba[j * 4 + 3] < 40) continue;
                    seen[j] = true; stack.Push(j);
                }
            }
            total += count;
            if (count > bestCount) { bestCount = count; bestLabel = id; bx0 = x0; by0 = y0; bx1 = x1; by1 = y1; }
        }
        if (bestCount < w * h / 20) return (img, false);
        int cw = bx1 - bx0 + 1, fullH = by1 - by0 + 1, ch = Math.Min(fullH, (int)(cw * 1.25));
        double aspect = fullH / (double)cw;
        // Whole: the figure has nearly all the paint, and stands like a bust (not a lone body or a scrap).
        bool whole = bestCount >= total * 0.85 && aspect is > 0.75 and < 2.6 && fullH > h * 0.45;
        // Not a flat colour mask: real paint varies.
        double mean = 0, var2 = 0; int n = 0;
        for (int y = by0; y <= by1; y += 3)
            for (int x = bx0; x <= bx1; x += 3)
            {
                int i = (y * w + x) * 4;
                if (img.Rgba[i + 3] < 40) continue;
                double l = img.Rgba[i] * 0.3 + img.Rgba[i + 1] * 0.59 + img.Rgba[i + 2] * 0.11;
                mean += l; var2 += l * l; n++;
            }
        if (n > 0) { mean /= n; whole &= var2 / n - mean * mean > 120; }
        // A head on top: the figure's upper part is clearly narrower than its widest row (a lone body is not).
        int Span(int y) { int a = -1, b = -1; for (int x = bx0; x <= bx1; x++) if (img.Rgba[(y * w + x) * 4 + 3] >= 40) { if (a < 0) a = x; b = x; } return a < 0 ? 0 : b - a + 1; }
        int widest = 0; for (int y = by0; y <= by1; y += 2) widest = Math.Max(widest, Span(y));
        int top = 0; for (int y = by0; y <= by0 + fullH / 6; y++) top = Math.Max(top, Span(y));
        whole &= top < widest * 0.72;
        // Loose parts of the texture sheet that fall inside the figure's box are cleared.
        for (int i = 0; i < w * h; i++) if (label[i] != 0 && label[i] != bestLabel) img.Rgba[i * 4 + 3] = 0;
        return (img.Crop(bx0, by0, cw, ch), whole);
    }

    public ImageData? RawFigure(string key) => PortraitOf(key)?.img;

    readonly Dictionary<string, (ImageData img, bool whole)?> _portraits = new(StringComparer.OrdinalIgnoreCase);

    (ImageData img, bool whole)? PortraitOf(string key)
    {
        if (_portraits.TryGetValue(key, out var hit)) return hit;
        (ImageData, bool)? r = St?.PortraitFile(key) is { } p && ImageData.FromFile(p) is { } img ? Figure(img) : null;
        return _portraits[key] = r;
    }

    /// <summary>A species' portrait: its own when it comes out whole (the sheet's portraits list, picked by eye),
    /// else a whole one from the same species group, the same one every time.</summary>
    public ImageData? Portrait(string key)
    {
        string? good = Defs.Portrait.ContainsKey(key) ? key : null;
        if (good == null && St?.PortraitInfoFor(key) is { } info)
        {
            var same = Defs.Portraits.Where(p => p.Group == info.Group).ToList();
            var pool = same.Count > 0 ? same : Defs.Portraits.ToList();
            int h = Math.Abs(key.Aggregate(17, (a, ch) => a * 31 + ch));
            good = pool[h % pool.Count].Id;
        }
        if (good != null && PortraitOf(good) is { } hit) return hit.img;
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
