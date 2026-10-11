using System.Buffers.Binary;

namespace StellarAscension;

/// <summary>Decodes the block-compressed texture formats Civ VI's packages use (BC1, BC2, BC3 colour; BC4 single channel)
/// into RGBA8. DXGI format numbers: 70-72 BC1, 73-75 BC2, 76-78 BC3, 79-81 BC4.</summary>
public static class BcDecoder
{
    public static bool Supported(int dxgi) => dxgi is >= 70 and <= 81 or 28 or 29;

    public static int BlockBytes(int dxgi) => dxgi is >= 70 and <= 72 or >= 79 and <= 81 ? 8 : 16;

    /// <summary>Bytes of one mip level.</summary>
    public static long LevelSize(int dxgi, int w, int h) =>
        dxgi is 28 or 29 ? (long)w * h * 4 : (long)Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * BlockBytes(dxgi);

    public static byte[] Decode(ReadOnlySpan<byte> data, int dxgi, int w, int h)
    {
        var outp = new byte[w * h * 4];
        if (dxgi is 28 or 29) { data[..outp.Length].CopyTo(outp); return outp; }
        int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4), bs = BlockBytes(dxgi);
        Span<byte> block = stackalloc byte[64];
        for (int by = 0; by < bh; by++)
            for (int bx = 0; bx < bw; bx++)
            {
                var b = data.Slice((by * bw + bx) * bs, bs);
                if (dxgi <= 72) Color(b, block, false);
                else if (dxgi <= 75) { Color(b[8..], block, true); Alpha4(b, block); }
                else if (dxgi <= 78) { Color(b[8..], block, true); Alpha8(b, block, 3); }
                else
                {
                    // BC4: one channel, shown as grey
                    Alpha8(b, block, 0);
                    for (int i = 0; i < 16; i++) { block[i * 4 + 1] = block[i * 4 + 2] = block[i * 4]; block[i * 4 + 3] = 255; }
                }
                for (int py = 0; py < 4; py++)
                {
                    int y = by * 4 + py;
                    if (y >= h) break;
                    for (int px = 0; px < 4; px++)
                    {
                        int x = bx * 4 + px;
                        if (x >= w) break;
                        block.Slice((py * 4 + px) * 4, 4).CopyTo(outp.AsSpan((y * w + x) * 4, 4));
                    }
                }
            }
        return outp;
    }

    static void Color(ReadOnlySpan<byte> b, Span<byte> outp, bool alwaysFour)
    {
        ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(b), c1 = BinaryPrimitives.ReadUInt16LittleEndian(b[2..]);
        Span<int> r = stackalloc int[4], g = stackalloc int[4], bl = stackalloc int[4], a = stackalloc int[4];
        static (int, int, int) Rgb(ushort c) => ((c >> 11 & 31) * 255 / 31, (c >> 5 & 63) * 255 / 63, (c & 31) * 255 / 31);
        (r[0], g[0], bl[0]) = Rgb(c0);
        (r[1], g[1], bl[1]) = Rgb(c1);
        a[0] = a[1] = a[2] = a[3] = 255;
        if (c0 > c1 || alwaysFour)
        {
            r[2] = (2 * r[0] + r[1]) / 3; g[2] = (2 * g[0] + g[1]) / 3; bl[2] = (2 * bl[0] + bl[1]) / 3;
            r[3] = (r[0] + 2 * r[1]) / 3; g[3] = (g[0] + 2 * g[1]) / 3; bl[3] = (bl[0] + 2 * bl[1]) / 3;
        }
        else
        {
            r[2] = (r[0] + r[1]) / 2; g[2] = (g[0] + g[1]) / 2; bl[2] = (bl[0] + bl[1]) / 2;
            r[3] = g[3] = bl[3] = 0; a[3] = 0;
        }
        uint idx = BinaryPrimitives.ReadUInt32LittleEndian(b[4..]);
        for (int i = 0; i < 16; i++)
        {
            int k = (int)(idx >> (2 * i) & 3);
            outp[i * 4] = (byte)r[k]; outp[i * 4 + 1] = (byte)g[k]; outp[i * 4 + 2] = (byte)bl[k]; outp[i * 4 + 3] = (byte)a[k];
        }
    }

    static void Alpha4(ReadOnlySpan<byte> b, Span<byte> outp)
    {
        ulong bits = BinaryPrimitives.ReadUInt64LittleEndian(b);
        for (int i = 0; i < 16; i++) outp[i * 4 + 3] = (byte)((bits >> (4 * i) & 15) * 17);
    }

    static void Alpha8(ReadOnlySpan<byte> b, Span<byte> outp, int channel)
    {
        int a0 = b[0], a1 = b[1];
        Span<int> pal = stackalloc int[8];
        pal[0] = a0; pal[1] = a1;
        if (a0 > a1) for (int i = 1; i < 7; i++) pal[i + 1] = ((7 - i) * a0 + i * a1) / 7;
        else { for (int i = 1; i < 5; i++) pal[i + 1] = ((5 - i) * a0 + i * a1) / 5; pal[6] = 0; pal[7] = 255; }
        ulong bits = 0;
        for (int i = 0; i < 6; i++) bits |= (ulong)b[2 + i] << (8 * i);
        for (int i = 0; i < 16; i++) outp[i * 4 + channel] = (byte)pal[(int)(bits >> (3 * i) & 7)];
    }
}
