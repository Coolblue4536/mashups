namespace GrandGalactic;

/// <summary>An RGBA8 picture in memory, top row first.</summary>
public sealed class ImageData
{
    public int W, H;
    public required byte[] Rgba;

    public static ImageData? FromFile(string path, int frames = 1)
    {
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            ImageData? img = ext == ".dds" ? FromDds(path) : FromRaylib(path);
            if (img != null && frames > 1) img = img.Crop(0, 0, img.W / frames, img.H);
            return img;
        }
        catch (Exception e)
        {
            Log.Info($"image: could not decode {path} ({e.Message})");
            return null;
        }
    }

    static ImageData? FromDds(string path)
    {
        using var img = Pfim.Pfimage.FromFile(path);
        if (img.Compressed) img.Decompress();
        int w = img.Width, h = img.Height;
        var outp = new byte[w * h * 4];
        var d = img.Data;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                switch (img.Format)
                {
                    case Pfim.ImageFormat.Rgba32:
                    {
                        int i = y * img.Stride + x * 4;
                        outp[o] = d[i + 2]; outp[o + 1] = d[i + 1]; outp[o + 2] = d[i]; outp[o + 3] = d[i + 3];
                        break;
                    }
                    case Pfim.ImageFormat.Rgb24:
                    {
                        int i = y * img.Stride + x * 3;
                        outp[o] = d[i + 2]; outp[o + 1] = d[i + 1]; outp[o + 2] = d[i]; outp[o + 3] = 255;
                        break;
                    }
                    case Pfim.ImageFormat.Rgb8:
                    {
                        int i = y * img.Stride + x;
                        outp[o] = outp[o + 1] = outp[o + 2] = d[i]; outp[o + 3] = 255;
                        break;
                    }
                    default:
                        Log.Info($"image: {path} uses unsupported pixel format {img.Format}");
                        return null;
                }
            }
        return new ImageData { W = w, H = h, Rgba = outp };
    }

    static unsafe ImageData? FromRaylib(string path)
    {
        var im = Raylib_cs.Raylib.LoadImage(path);
        if (im.Width == 0) return null;
        Raylib_cs.Raylib.ImageFormat(ref im, Raylib_cs.PixelFormat.UncompressedR8G8B8A8);
        var bytes = new byte[im.Width * im.Height * 4];
        System.Runtime.InteropServices.Marshal.Copy((IntPtr)im.Data, bytes, 0, bytes.Length);
        var r = new ImageData { W = im.Width, H = im.Height, Rgba = bytes };
        Raylib_cs.Raylib.UnloadImage(im);
        return r;
    }

    public static ImageData FromBgraBottomUp(byte[] bgra, int w, int h)
    {
        var o = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            int src = (h - 1 - y) * w * 4, dst = y * w * 4;
            for (int x = 0; x < w; x++, src += 4, dst += 4)
            {
                o[dst] = bgra[src + 2]; o[dst + 1] = bgra[src + 1]; o[dst + 2] = bgra[src]; o[dst + 3] = bgra[src + 3];
            }
        }
        return new ImageData { W = w, H = h, Rgba = o };
    }

    public ImageData Crop(int x, int y, int w, int h)
    {
        x = Math.Clamp(x, 0, W - 1); y = Math.Clamp(y, 0, H - 1);
        w = Math.Clamp(w, 1, W - x); h = Math.Clamp(h, 1, H - y);
        var o = new byte[w * h * 4];
        for (int r = 0; r < h; r++) Buffer.BlockCopy(Rgba, ((y + r) * W + x) * 4, o, r * w * 4, w * 4);
        return new ImageData { W = w, H = h, Rgba = o };
    }
}
