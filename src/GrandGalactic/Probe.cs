using System.Runtime.InteropServices;
using System.Text.Json;
using Raylib_cs;

namespace GrandGalactic;

/// <summary>--probe &lt;folder&gt;: report what the mashup finds in this PC's Stellaris and Stacklands, and save each resolved
/// picture as a PNG (kept on this PC only) so every asset_refs row can be checked by eye.</summary>
public static class Probe
{
    public static int Run(string? stellaris, string? stacklands, string outDir)
    {
        Directory.CreateDirectory(Path.Combine(outDir, "png"));
        var st = stellaris != null ? new StellarisData(stellaris) : null;
        StacklandsData? sl = null;
        string? slError = null;
        if (stacklands != null)
        {
            try { sl = new StacklandsData(stacklands); }
            catch (Exception e) { slError = e.ToString(); Log.Info($"probe: Stacklands read failed: {e}"); }
        }
        var res = new AssetResolver(st, sl) { PortraitPath = st?.Portraits.FirstOrDefault() is { } p ? st.FilePath(p.TextureFile) : null };
        var refs = new List<object>();
        foreach (var a in Defs.AssetRefs)
        {
            string? file = null;
            string size = "";
            if (a.Kind == "image" && res.Image(a) is { } img)
            {
                file = $"png/{a.Id}.png";
                Save(img, Path.Combine(outDir, file));
                size = $"{img.W}x{img.H}";
            }
            else if (a.Kind is "sound" or "music" && res.Sound(a) is { } snd)
            {
                file = $"audio/{a.Id}.{snd.ext}";
                Directory.CreateDirectory(Path.Combine(outDir, "audio"));
                File.WriteAllBytes(Path.Combine(outDir, file), snd.data);
            }
            else if (a.Kind == "font") res.Font(a);
            refs.Add(new { a.Id, a.Game, a.Kind, hit = res.Hits.GetValueOrDefault(a.Id), file, size });
        }
        var names = Defs.Cards.Select(c => new { c.Id, sheet = c.Name, game = res.CardName(c.Id) });
        var report = new
        {
            version = typeof(Probe).Assembly.GetName().Version?.ToString(),
            stellaris = st == null ? null : new
            {
                path = st.Root,
                images = st.Files.Count,
                loc = st.Loc.Count,
                sprites = st.Sprites.Keys.OrderBy(k => k).ToArray(),
                planetClasses = st.PlanetIcons,
                portraits = st.Portraits.Select(p => $"{p.Group}/{p.Name}: {p.TextureFile}").ToArray(),
                eventPictures = st.Files.Keys.Where(k => k.StartsWith("gfx/event_pictures/")).OrderBy(k => k).ToArray(),
            },
            stacklands = sl == null ? (object?)new { error = slError ?? "not found" } : new
            {
                path = sl.Root,
                unity = sl.UnityVersion,
                textures = sl.Textures.Select(t => t.Name).OrderBy(n => n).ToArray(),
                sprites = sl.Sprites.Select(t => t.Name).OrderBy(n => n).ToArray(),
                audio = sl.Audio.Select(t => t.Name).OrderBy(n => n).ToArray(),
                fonts = sl.Fonts.Select(t => t.Name).OrderBy(n => n).ToArray(),
            },
            refs,
            names,
            misses = res.Misses.ToArray(),
        };
        var path = Path.Combine(outDir, "probe.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Log.Info($"probe: {res.Hits.Count} of {Defs.AssetRefs.Length} assets found, {res.Misses.Count} missing → {path}");
        return 0;
    }

    public static unsafe void Save(ImageData img, string path)
    {
        var ptr = Raylib.MemAlloc((uint)img.Rgba.Length);
        Marshal.Copy(img.Rgba, 0, (IntPtr)ptr, img.Rgba.Length);
        var im = new Image { Data = ptr, Width = img.W, Height = img.H, Mipmaps = 1, Format = PixelFormat.UncompressedR8G8B8A8 };
        Raylib.ExportImage(im, path);
        Raylib.UnloadImage(im);
    }
}
