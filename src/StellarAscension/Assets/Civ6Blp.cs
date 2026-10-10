using System.Buffers.Binary;

namespace StellarAscension;

/// <summary>
/// Reads pictures out of a Civilization VI .blp package (Firaxis's own container; format notes on
/// civ6.fandom.com/wiki/BLP plus what Stellar Ascension's tests checked against the real files).
///
/// A version-2 package is: a 0x400-byte file header, the package data (a type-info package, then the
/// package's own objects), then "big data" (the pixels). Two kinds of picture are read:
/// - BLP::TextureEntry: a whole texture (uncompressed RGBA8 with mips), e.g. a leader's full portrait.
/// - ForgeUI::BCTexturePackageEntry: a UI atlas such as "Tech128", stored as unique bs×bs RGBA tiles
///   followed by one tile index per tile position. Atlases are found by the FNV-1a hash of their name.
/// </summary>
public sealed class Civ6Blp
{
    public readonly string Path;
    readonly byte[] _pkg;          // package data only (small); big data is read from the file on demand
    readonly uint _bigDataOffset;
    public readonly List<TextureEntry> Textures = new();
    public readonly Dictionary<uint, AtlasEntry> Atlases = new();

    public sealed record TextureEntry(uint NameHash, ushort Format, int W, int H, ulong Offset, uint Size);
    public sealed record AtlasEntry(uint NameHash, int W, int H, uint TilePixels, int TileSize, int BytesPerIndex, long Offset, uint Size);

    const uint DxgiRgba8 = 28;

    public static uint Fnv1a(string s)
    {
        uint h = 0x811c9dc5;
        foreach (char c in s) { h ^= (byte)c; h *= 0x01000193; }
        return h;
    }

    Civ6Blp(string path, byte[] pkg, uint bigDataOffset)
    {
        Path = path; _pkg = pkg; _bigDataOffset = bigDataOffset;
    }

    public static Civ6Blp? Open(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            var head = new byte[0x20];
            if (f.Read(head, 0, head.Length) != head.Length || System.Text.Encoding.ASCII.GetString(head, 0, 6) != "CIVBLP") return null;
            uint pdo = U32(head, 8), pds = U32(head, 12), bdo = U32(head, 16);
            var pkg = new byte[pdo + pds];
            f.Position = 0;
            f.ReadExactly(pkg, 0, pkg.Length);
            var b = new Civ6Blp(path, pkg, bdo);
            b.Index((int)pdo, (int)(pdo + pds), (long)f.Length);
            return b;
        }
        catch (Exception e)
        {
            Log.Info($"civ6 blp: could not read {path} ({e.Message})");
            return null;
        }
    }

    static uint U32(byte[] d, int o) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o));
    static ushort U16(byte[] d, int o) => BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(o));
    static ulong U64(byte[] d, int o) => BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan(o));

    void Index(int start, int end, long fileSize)
    {
        var d = _pkg;
        // 1. The type-info package: its header ends with the fixed sizes 0x10, 0x30, 0x28, 0x10.
        int footer = -1;
        for (int o = start; o + 16 <= end; o += 1)
            if (U32(d, o) == 0x10 && U32(d, o + 4) == 0x30 && U32(d, o + 8) == 0x28 && U32(d, o + 12) == 0x10) { footer = o; break; }
        if (footer < 0) return;
        int typeBase = footer - 0x38 - 16;
        uint tempStart = U32(d, typeBase + 16 + 16), tempSize = U32(d, typeBase + 16 + 20);
        int outer = typeBase + (int)(tempStart + tempSize);

        // 2. Big-data resource sizes for the atlases: {u32 index, u32 size, u64} records numbered 0, 1, 2...
        //    They follow the type package after a short header (0x60 bytes in the files checked).
        //    A shorter numbered list can come first, so the longest run in the window wins.
        var sizes = new List<uint>();
        for (int s = outer, stop = Math.Min(end, outer + 0x400); s + 48 <= stop; s++)
        {
            if (U32(d, s) != 0 || U32(d, s + 16) != 1 || U32(d, s + 32) != 2 || U32(d, s + 4) == 0) continue;
            var run = new List<uint>();
            for (int o = s; o + 16 <= end && U32(d, o) == (uint)run.Count; o += 16) run.Add(U32(d, o + 4));
            if (run.Count > sizes.Count) sizes = run;
        }

        // 3. Whole textures: BLP::TextureEntry (0x68 bytes). Found by their shape; the offsets point into big data.
        ulong texEnd = 0;
        for (int o = outer; o + 0x68 <= end; o++)
        {
            if (U16(d, o + 0x5e) != 1 || U16(d, o + 0x60) != 1) continue;
            ushort fmt = U16(d, o + 0x58), w = U16(d, o + 0x5a), h = U16(d, o + 0x5c);
            byte mips = d[o + 0x62];
            if (fmt == 0 || fmt >= 200 || w == 0 || h == 0 || w > 16384 || h > 16384 || mips < 1 || mips > 15) continue;
            ulong off = U64(d, o + 0x20); uint size = U32(d, o + 0x28);
            if (off % 16 != 0 || size == 0 || (long)(_bigDataOffset + off + size) > fileSize) continue;
            if (fmt == DxgiRgba8 && size < (ulong)w * h * 4) continue;
            Textures.Add(new TextureEntry(U32(d, o + 0x30), fmt, w, h, off, size));
            texEnd = Math.Max(texEnd, off + size);
            o += 0x67;
        }

        // 4. Atlases: ForgeUI::BCTexturePackageEntry (0x70 bytes, flags 2), one per resource size, in the same order.
        if (sizes.Count == 0) return;
        int first = -1;
        for (int o = outer; o + 0x70 * sizes.Count <= end; o++)
        {
            if (!IsAtlas(d, o)) continue;
            bool run = true;
            for (int k = 1; k < sizes.Count && run; k++) run = IsAtlas(d, o + 0x70 * k);
            if (run) { first = o; break; }
        }
        if (first < 0) return;
        long pos = _bigDataOffset + (long)Align(texEnd, 512);
        for (int k = 0; k < sizes.Count; k++)
        {
            int o = first + 0x70 * k;
            var a = new AtlasEntry(U32(d, o + 0x40), U16(d, o + 0x54), U16(d, o + 0x56), U32(d, o + 0x64), U16(d, o + 0x68), U16(d, o + 0x6a), pos, sizes[k]);
            Atlases.TryAdd(a.NameHash, a);
            pos += (long)Align(sizes[k], 512);
        }
    }

    static bool IsAtlas(byte[] d, int o)
    {
        if (U32(d, o + 0x48) != 2) return false;
        int w = U16(d, o + 0x54), h = U16(d, o + 0x56), bs = U16(d, o + 0x68), bpi = U16(d, o + 0x6a);
        return w > 0 && h > 0 && bs is 1 or 2 or 4 or 8 or 16 or 32 && bpi is 1 or 2 or 4 && U32(d, o + 0x64) % (uint)(bs * bs) == 0;
    }

    static ulong Align(ulong v, ulong a) => (v + a - 1) / a * a;

    public AtlasEntry? FindAtlas(string name) => Atlases.TryGetValue(Fnv1a(name), out var a) ? a : null;
    public TextureEntry? FindTexture(string name) => Textures.FirstOrDefault(t => t.NameHash == Fnv1a(name));

    /// <summary>Decode a whole atlas to RGBA. Returns null when the data doesn't add up (a different layout).</summary>
    public ImageData? DecodeAtlas(AtlasEntry a)
    {
        int bs = a.TileSize, tw = (a.W + bs - 1) / bs, th = (a.H + bs - 1) / bs;
        long tileBytes = (long)a.TilePixels * 4, need = tileBytes + (long)tw * th * a.BytesPerIndex;
        if (need > a.Size) return null;
        var blob = new byte[need];
        using (var f = File.OpenRead(Path)) { f.Position = a.Offset; f.ReadExactly(blob, 0, blob.Length); }
        int unique = (int)(a.TilePixels / (uint)(bs * bs));
        int pw = tw * bs, ph = th * bs;
        var px = new byte[pw * ph * 4];
        int ip = (int)tileBytes;
        for (int ty = 0; ty < th; ty++)
            for (int tx = 0; tx < tw; tx++, ip += a.BytesPerIndex)
            {
                int idx = a.BytesPerIndex switch { 1 => blob[ip], 2 => U16(blob, ip), _ => (int)U32(blob, ip) };
                if (idx >= unique) return null;
                int src = idx * bs * bs * 4;
                for (int r = 0; r < bs; r++)
                    Buffer.BlockCopy(blob, src + r * bs * 4, px, ((ty * bs + r) * pw + tx * bs) * 4, bs * 4);
            }
        var img = new ImageData { W = pw, H = ph, Rgba = px };
        return pw == a.W && ph == a.H ? img : img.Crop(0, 0, a.W, a.H);
    }

    /// <summary>Decode the top mip of an uncompressed RGBA8 texture.</summary>
    public ImageData? DecodeTexture(TextureEntry t)
    {
        if (t.Format != DxgiRgba8) return null;
        var px = new byte[t.W * t.H * 4];
        using var f = File.OpenRead(Path);
        f.Position = _bigDataOffset + (long)t.Offset;
        f.ReadExactly(px, 0, px.Length);
        return new ImageData { W = t.W, H = t.H, Rgba = px };
    }
}
