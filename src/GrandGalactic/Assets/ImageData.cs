namespace GrandGalactic;

/// <summary>An RGBA8 picture in memory, top row first.</summary>
public sealed class ImageData
{
    public int W, H;
    public required byte[] Rgba;

    /// <summary>Load an image; for a horizontal strip of `frames` icons, keep only `frame` (1-based).</summary>
    public static ImageData? FromFile(string path, int frames = 1, int frame = 1)
    {
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            ImageData? img = ext == ".dds" ? FromDds(path) : FromRaylib(path);
            if (img != null && frames > 1)
            {
                // Strips aren't always an exact multiple of their frame count: place each frame by rounding, not by
                // multiplying a truncated width, or later frames drift.
                int f = Math.Clamp(frame, 1, frames);
                int x0 = (int)Math.Round(img.W * (f - 1) / (double)frames), x1 = (int)Math.Round(img.W * f / (double)frames);
                img = img.Crop(x0, 0, x1 - x0, img.H);
            }
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

    /// <summary>Enlarge a small icon smoothly (Catmull-Rom bicubic on premultiplied alpha, so edges get no dark fringe),
    /// then sharpen it a little. Cards draw art far bigger than Stellaris's 18-58 px icons; stretching those on the
    /// GPU looks blurry, while a clean high-quality enlargement drawn smaller looks crisp.</summary>
    public ImageData UpscaleSharp(int f, float sharpen = 0.55f)
    {
        if (f <= 1) return this;
        int w = W * f, h = H * f;
        // premultiplied float source
        var src = new float[W * H * 4];
        for (int i = 0; i < W * H; i++)
        {
            float a = Rgba[i * 4 + 3] / 255f;
            src[i * 4] = Rgba[i * 4] / 255f * a; src[i * 4 + 1] = Rgba[i * 4 + 1] / 255f * a; src[i * 4 + 2] = Rgba[i * 4 + 2] / 255f * a; src[i * 4 + 3] = a;
        }
        static float Cr(float t)
        {
            t = Math.Abs(t);
            return t < 1 ? 1.5f * t * t * t - 2.5f * t * t + 1 : t < 2 ? -0.5f * t * t * t + 2.5f * t * t - 4 * t + 2 : 0;
        }
        var big = new float[w * h * 4];
        Span<float> wx = stackalloc float[4], wy = stackalloc float[4];
        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / f - 0.5f;
            int y0 = (int)MathF.Floor(v);
            for (int k = 0; k < 4; k++) wy[k] = Cr(v - (y0 - 1 + k));
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / f - 0.5f;
                int x0 = (int)MathF.Floor(u);
                for (int k = 0; k < 4; k++) wx[k] = Cr(u - (x0 - 1 + k));
                float r = 0, g = 0, b = 0, a = 0;
                for (int j = 0; j < 4; j++)
                {
                    int sy = Math.Clamp(y0 - 1 + j, 0, H - 1);
                    for (int i = 0; i < 4; i++)
                    {
                        int sx = Math.Clamp(x0 - 1 + i, 0, W - 1), o = (sy * W + sx) * 4;
                        float k = wx[i] * wy[j];
                        r += src[o] * k; g += src[o + 1] * k; b += src[o + 2] * k; a += src[o + 3] * k;
                    }
                }
                int d = (y * w + x) * 4;
                big[d] = r; big[d + 1] = g; big[d + 2] = b; big[d + 3] = a;
            }
        }
        // unsharp mask against a 3x3 blur, then back to straight alpha
        var o8 = new byte[w * h * 4];
        Span<float> px = stackalloc float[4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int d = (y * w + x) * 4;
                for (int c = 0; c < 4; c++)
                {
                    float sum = 0;
                    for (int j = -1; j <= 1; j++)
                        for (int i = -1; i <= 1; i++)
                            sum += big[(Math.Clamp(y + j, 0, h - 1) * w + Math.Clamp(x + i, 0, w - 1)) * 4 + c] * (i == 0 && j == 0 ? 4 : i == 0 || j == 0 ? 2 : 1);
                    float blur = sum / 16f, val = big[d + c];
                    px[c] = val + sharpen * (val - blur);
                }
                float al = Math.Clamp(px[3], 0, 1);
                o8[d + 3] = (byte)(al * 255 + 0.5f);
                for (int c = 0; c < 3; c++)
                    o8[d + c] = al > 0.002f ? (byte)(Math.Clamp(px[c] / al, 0, 1) * 255 + 0.5f) : (byte)0;
            }
        return new ImageData { W = w, H = h, Rgba = o8 };
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
