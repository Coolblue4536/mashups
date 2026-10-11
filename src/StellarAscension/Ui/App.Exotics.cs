using System.Numerics;
using Raylib_cs;

namespace StellarAscension;

/// <summary>Exotic sites on the Act 1 map, drawn as part of the land: a painted ground made here for each kind (a
/// liquid-metal lake, a gas bog, crystal-shot rock...) that spills onto the six tiles around it, living effects above it
/// (gas smoking up, ripples, crystal spires, floating motes, lightning...), and an outline round the seven tiles you need
/// to hold, with the ones inside your borders lit.</summary>
public sealed partial class App
{
    sealed record ExoticLook(Color Dark, Color Light, Color Glow);

    static ExoticLook Look(string id) => id switch
    {
        "living_metal" => new(new Color(48, 54, 66, 255), new Color(226, 232, 244, 255), new Color(190, 215, 255, 255)),
        "gas_swamp" => new(new Color(34, 40, 22, 255), new Color(96, 104, 40, 255), new Color(214, 236, 120, 255)),
        "crystal_caverns" => new(new Color(34, 32, 52, 255), new Color(92, 84, 118, 255), new Color(130, 226, 255, 255)),
        "dark_matter" => new(new Color(10, 6, 22, 255), new Color(70, 40, 120, 255), new Color(150, 90, 255, 255)),
        "nanites" => new(new Color(64, 68, 76, 255), new Color(168, 176, 186, 255), new Color(210, 235, 245, 255)),
        "ruins" => new(new Color(118, 100, 70, 255), new Color(198, 178, 134, 255), new Color(90, 232, 210, 255)),
        "zro" => new(new Color(38, 22, 58, 255), new Color(92, 62, 130, 255), new Color(178, 140, 255, 255)),
        "motes" => new(new Color(52, 38, 34, 255), new Color(112, 88, 74, 255), new Color(255, 146, 56, 255)),
        "storm_sea" => new(new Color(14, 56, 74, 255), new Color(46, 132, 150, 255), new Color(170, 236, 255, 255)),
        _ => new(new Color(40, 30, 60, 255), new Color(120, 90, 160, 255), new Color(200, 150, 255, 255)),
    };

    // ---------------------------------------------------------------- the painted ground (made once per kind)

    static float Hash(int x, int y, int seed)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
    }

    static float Noise(float x, float y, int seed)
    {
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        float a = Hash(xi, yi, seed), b = Hash(xi + 1, yi, seed), c = Hash(xi, yi + 1, seed), d = Hash(xi + 1, yi + 1, seed);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    static float Fbm(float x, float y, int seed, int oct = 5)
    {
        float v = 0, amp = 0.5f, f = 1;
        for (int i = 0; i < oct; i++) { v += amp * Noise(x * f, y * f, seed + i * 17); amp *= 0.5f; f *= 2.03f; }
        return v / (1 - MathF.Pow(0.5f, oct));
    }

    static float Smooth(float e0, float e1, float x) { float t = Math.Clamp((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - 2 * t); }
    static Vector3 V(Color c) => new(c.R, c.G, c.B);

    /// <summary>A 256px ground for the kind of site, soft-edged so it blends into the land under it.</summary>
    static ImageData PaintExotic(string id)
    {
        const int N = 256;
        var img = new ImageData { W = N, H = N, Rgba = new byte[N * N * 4] };
        var look = Look(id);
        Vector3 dark = V(look.Dark), light = V(look.Light), glow = V(look.Glow);
        int seed = id.Aggregate(7, (a, ch) => a * 31 + ch);
        for (int py = 0; py < N; py++)
            for (int px = 0; px < N; px++)
            {
                float u = px / (float)N * 2 - 1, v = py / (float)N * 2 - 1;
                float r = MathF.Sqrt(u * u + v * v), ang = MathF.Atan2(v, u);
                float n = Fbm(px / 40f, py / 40f, seed);
                float n2 = Fbm(px / 12f, py / 12f, seed + 99, 3);
                Vector3 col;
                switch (id)
                {
                    case "living_metal":
                    {
                        // A mirror-bright pool: soft ripples and streaks of reflected sky.
                        // Liquid metal: dark depths, a bright sky reflection sweeping across, a thin rim of solid metal.
                        float sky = Smooth(-0.3f, 0.6f, -v * 0.8f + u * 0.35f + (n - 0.5f) * 0.6f);
                        float streak = MathF.Pow(Math.Max(0, MathF.Sin((u * 0.6f - v) * 5 + n * 3)), 14);
                        float rimBand = Smooth(0.5f, 0.62f, r + (n - 0.5f) * 0.2f);
                        col = Vector3.Lerp(dark, light, 0.15f + 0.75f * sky) + new Vector3(70, 74, 86) * streak;
                        col = Vector3.Lerp(col, new Vector3(120, 116, 108) * (0.7f + 0.6f * n2), rimBand);
                        break;
                    }
                    case "gas_swamp":
                    {
                        // Murky bog water between mossy hummocks, with pale bubbles and a sickly sheen.
                        float water = Smooth(0.42f, 0.55f, n);
                        col = Vector3.Lerp(Vector3.Lerp(dark, light, n2 * 0.9f), new Vector3(46, 52, 20) + new Vector3(60, 66, 10) * Smooth(0.6f, 0.9f, n2), water);
                        float bub = Hash(px / 9, py / 9, seed + 5) > 0.93f && MathF.Abs(px % 9 - 4) < 2 && MathF.Abs(py % 9 - 4) < 2 ? 1 : 0;
                        col += glow * 0.35f * bub * water;
                        break;
                    }
                    case "crystal_caverns":
                    {
                        // Dark rock split by glowing crystal veins.
                        float vein = 1 - Smooth(0.0f, 0.05f, MathF.Abs(n - 0.5f));
                        col = Vector3.Lerp(dark, light, n2) + glow * 0.75f * vein;
                        break;
                    }
                    case "dark_matter":
                    {
                        // A black pool turning slowly, bands of violet drawn into its centre.
                        float swirl = 0.5f + 0.5f * MathF.Sin(ang * 3 + r * 9 + n * 5);
                        col = Vector3.Lerp(dark, light, swirl * Smooth(0.05f, 0.8f, r) * 0.9f) + glow * 0.25f * MathF.Pow(swirl, 6);
                        break;
                    }
                    case "nanites":
                    {
                        // Grey metal sand, finely grained, catching the light in flecks.
                        float grain = Hash(px, py, seed);
                        col = Vector3.Lerp(dark, light, 0.25f + 0.55f * n + 0.2f * grain) + glow * 0.4f * (grain > 0.985f ? 1 : 0);
                        break;
                    }
                    case "ruins":
                    {
                        // Sand over buried paving: a grid of worn blocks.
                        int bx = px / 22, by = (py + (bx % 2) * 11) / 22;
                        float edge = Math.Min(Math.Min(px % 22, 21 - px % 22), Math.Min((py + (bx % 2) * 11) % 22, 21 - (py + (bx % 2) * 11) % 22));
                        float paved = Hash(bx, by, seed) > 0.35f && n > 0.42f ? 1 : 0;
                        col = Vector3.Lerp(light, dark, 0.25f * n2 + paved * (edge < 2 ? 0.55f : 0.15f)) + glow * 0.18f * paved * (edge < 1 ? 1 : 0);
                        break;
                    }
                    case "zro":
                    {
                        // Purple loam under the spore forest, dotted with glowing caps.
                        col = Vector3.Lerp(dark, light, n * 0.9f);
                        float cap = Hash(px / 7, py / 7, seed + 3) > 0.9f ? Smooth(3.2f, 0.5f, Vector2.Distance(new(px % 7, py % 7), new(3, 3))) : 0;
                        col += glow * 0.8f * cap;
                        break;
                    }
                    case "motes":
                    {
                        // Cracked scorched rock with molten fissures.
                        float crack = 1 - Smooth(0.0f, 0.035f, MathF.Abs(Fbm(px / 30f, py / 30f, seed + 41) - 0.5f));
                        col = Vector3.Lerp(dark, light, n2 * 0.8f) + glow * 0.9f * crack;
                        break;
                    }
                    case "storm_sea":
                    {
                        // Storm-dark water with bright churning foam.
                        float foam = Smooth(0.62f, 0.75f, Fbm(px / 18f, py / 18f, seed + 8));
                        col = Vector3.Lerp(dark, light, n * 0.8f) + glow * 0.45f * foam;
                        break;
                    }
                    default: col = Vector3.Lerp(dark, light, n); break;
                }
                // A soft, ragged edge so the site melts into the ground around it.
                float edgeR = r + (n - 0.5f) * 0.35f;
                float alpha = 1 - Smooth(0.62f, 0.98f, edgeR);
                int i = (py * N + px) * 4;
                img.Rgba[i] = (byte)Math.Clamp(col.X, 0, 255);
                img.Rgba[i + 1] = (byte)Math.Clamp(col.Y, 0, 255);
                img.Rgba[i + 2] = (byte)Math.Clamp(col.Z, 0, 255);
                img.Rgba[i + 3] = (byte)(255 * alpha);
            }
        return img;
    }

    Texture2D? ExoticGroundTex(string id) => G.Tex("exotic-ground:" + id, () => PaintExotic(id));

    // ---------------------------------------------------------------- the map passes

    static readonly float[] Facing = { 0f, -60f, -120f, 180f, 120f, 60f };
    Vector2 Dir(int k) => new(MathF.Cos(Facing[k] * MathF.PI / 180), MathF.Sin(Facing[k] * MathF.PI / 180));

    /// <summary>Under the sprites: the site's ground on its tile, spilling onto the six around it, and the outline of the
    /// area to hold (tiles inside your borders lit in the site's colour).</summary>
    void DrawExoticGround(Act1State s, List<(Vector2 p, Vector2 w, int x, int y)> tiles)
    {
        float size = HexW * _zoom1;
        foreach (var (p, w, x, y) in tiles)
        {
            var tile = s.At(x, y);
            if (!tile.Revealed || tile.Exotic is not { } id) continue;
            bool vis = Act1.Visible(s, x, y);
            var look = Look(id);
            var tint = vis ? Color.White : new Color(214, 204, 186, 235);
            var tex = ExoticGroundTex(id);
            var ring = NeighborsInOrder(x, y).ToList();
            // Spill onto the neighbours first, then the site itself on top.
            for (int k = 0; k < 6; k++)
            {
                var (nx, ny) = ring[k];
                if (ny < 0 || ny >= s.H || !s.At(nx, ny).Revealed) continue;
                // It spreads over land from a land site, over water from a sea site.
                if (Defs.Terrains[s.At(nx, ny).Terrain].Land != Defs.Terrains[tile.Terrain].Land) continue;
                var c = p + Dir(k) * size * 0.58f;
                if (tex is { } t) Raylib.DrawTexturePro(t, new Rectangle(0, 0, t.Width, t.Height), new Rectangle(c.X - size * 0.55f, c.Y - size * 0.55f, size * 1.1f, size * 1.1f),
                    Vector2.Zero, k * 53 + x * 7, new Color(tint.R, tint.G, tint.B, (byte)200));
            }
            if (tex is { } tt) Raylib.DrawTexturePro(tt, new Rectangle(0, 0, tt.Width, tt.Height), new Rectangle(p.X - size * 0.78f, p.Y - size * 0.78f, size * 1.56f, size * 1.56f), Vector2.Zero, 0, tint);
            // The area to hold: the outer edge of the seven tiles; ring tiles inside your borders glow faintly.
            float pulse = vis ? 0.75f + 0.25f * MathF.Sin((float)Raylib.GetTime() * 2.2f + x) : 0.6f;
            for (int k = 0; k < 6; k++)
            {
                var (nx, ny) = ring[k];
                if (ny < 0 || ny >= s.H) continue;
                var rp = p + Dir(k) * size;
                bool held = s.OwnerAt(nx, ny) == s.Player.Id;
                if (held) Raylib.DrawPoly(rp, 6, HexR * _zoom1 * 0.94f, 30, new Color(look.Glow.R, look.Glow.G, look.Glow.B, (byte)34));
                foreach (int dk in new[] { -1, 0, 1 })
                {
                    int e = (k + dk + 6) % 6;
                    var col = new Color(look.Glow.R, look.Glow.G, look.Glow.B, (byte)((held ? 230 : 170) * pulse));
                    EdgeLine(rp, e, new Color(10, 8, 20, 120), 0.99f, 4.5f);
                    EdgeLine(rp, e, col, 0.99f, held ? 3f : 2.2f);
                }
            }
        }
    }

    /// <summary>Above the sprites: what moves over each site (only where you can see it now).</summary>
    void DrawExoticAir(Act1State s, List<(Vector2 p, Vector2 w, int x, int y)> tiles)
    {
        float size = HexW * _zoom1;
        float t = (float)Raylib.GetTime();
        foreach (var (p, w, x, y) in tiles)
        {
            var tile = s.At(x, y);
            if (!tile.Revealed || tile.Exotic is not { } id) continue;
            bool vis = Act1.Visible(s, x, y);
            var look = Look(id);
            float tt = vis ? t : 0.4f;   // remembered sites stand still
            Color G0(byte a) => new(look.Glow.R, look.Glow.G, look.Glow.B, a);
            // Spots on the site and the tiles around it where effects rise.
            var spots = new List<Vector2> { p, p + new Vector2(-0.22f, 0.1f) * size, p + new Vector2(0.24f, 0.05f) * size };
            for (int k = 0; k < 6; k++) spots.Add(p + Dir(k) * size * 0.6f);
            Raylib.BeginBlendMode(BlendMode.Additive);
            Raylib.DrawCircleGradient(p, size * 0.7f, G0((byte)(vis ? 40 : 18)), new Color(0, 0, 0, 0));
            Raylib.EndBlendMode();
            switch (id)
            {
                case "gas_swamp":
                    // Gas smoking up from the bog: puffs that rise, swell and thin out.
                    for (int i = 0; i < 26; i++)
                    {
                        var at = spots[i % spots.Count];
                        float life = (tt * 0.2f + i * 0.0383f * 3.7f) % 1f;
                        var c = at + new Vector2(MathF.Sin(life * 4 + i) * size * 0.1f + life * size * 0.12f, -life * size * 0.9f);
                        float rad = size * (0.1f + life * 0.26f) * (i % spots.Count < 3 ? 1 : 0.75f);
                        byte a = (byte)(175 * MathF.Sin(life * MathF.PI) * (1 - life * 0.4f));
                        Raylib.DrawCircleGradient(c, rad, new Color((byte)205, (byte)228, (byte)128, a), new Color((byte)150, (byte)176, (byte)70, (byte)0));
                        Raylib.DrawCircleGradient(c + new Vector2(-rad * 0.25f, -rad * 0.25f), rad * 0.5f, new Color((byte)236, (byte)246, (byte)186, (byte)(a / 2)), new Color((byte)236, (byte)246, (byte)186, (byte)0));
                    }
                    break;
                case "living_metal":
                    // Slow rings spreading across the metal, and glints.
                    for (int i = 0; i < 3; i++)
                    {
                        float life = (tt * 0.3f + i / 3f) % 1f;
                        float rr = size * (0.06f + life * 0.42f);
                        Raylib.DrawEllipseLines((int)p.X, (int)(p.Y + size * 0.02f), rr, rr * 0.6f, new Color((byte)235, (byte)242, (byte)255, (byte)(90 * (1 - life))));
                    }
                    Raylib.BeginBlendMode(BlendMode.Additive);
                    // The sky caught in the metal: a soft sheen across the upper part of the lake.
                    for (int i = 0; i < 3; i++)
                        Raylib.DrawEllipse((int)(p.X - size * 0.08f), (int)(p.Y - size * 0.12f), size * (0.36f - i * 0.1f), size * (0.13f - i * 0.035f), new Color((byte)120, (byte)135, (byte)160, (byte)(vis ? 45 : 20)));
                    for (int i = 0; i < 4; i++)
                    {
                        float g = MathF.Max(0, MathF.Sin(tt * 1.7f + i * 2.1f));
                        var gp = p + new Vector2(MathF.Cos(i * 2.4f), MathF.Sin(i * 2.4f) * 0.6f) * size * 0.32f;
                        Raylib.DrawCircleGradient(gp, size * 0.07f * g + 1, new Color((byte)255, (byte)255, (byte)255, (byte)(200 * g)), new Color(0, 0, 0, 0));
                    }
                    Raylib.EndBlendMode();
                    break;
                case "crystal_caverns":
                    // Crystal spires breaking out of the rock, here and on the tiles around.
                    for (int i = 0; i < spots.Count; i++)
                        for (int j = 0; j < 3; j++)
                        {
                            float h = size * (i == 0 ? 0.55f : i < 3 ? 0.36f : 0.22f) * (j == 1 ? 1 : 0.62f);
                            float wdt = h * 0.34f;
                            var b = spots[i] + new Vector2((j - 1) * wdt * 0.8f, size * 0.1f);
                            float lean = (j - 1) * 0.35f + (Hash(i, x, y + j) - 0.5f) * 0.2f;
                            var top = b + new Vector2(lean * h, -h);
                            float tw = 0.75f + 0.25f * MathF.Sin(tt * 3 + i * 1.3f + j);
                            var tint2 = (i + j) % 3 == 0 ? new Vector3(200, 150, 255) : V(look.Glow);
                            // Two faces: the shadowed left and the lit right, meeting at the ridge (counter-clockwise for raylib).
                            Raylib.DrawTriangle(top, b + new Vector2(-wdt / 2, 0), b + new Vector2(wdt * 0.1f, 0), new Color((byte)(tint2.X * 0.55f * tw), (byte)(tint2.Y * 0.6f * tw), (byte)(tint2.Z * 0.85f * tw), (byte)245));
                            Raylib.DrawTriangle(top, b + new Vector2(wdt * 0.1f, 0), b + new Vector2(wdt / 2, 0), new Color((byte)Math.Min(255, tint2.X * 1.1f), (byte)Math.Min(255, tint2.Y * 1.05f), (byte)255, (byte)(235 * tw)));
                        }
                    Raylib.BeginBlendMode(BlendMode.Additive);
                    Raylib.DrawCircleGradient(p - new Vector2(0, size * 0.15f), size * 0.4f, G0((byte)(vis ? 70 : 30)), new Color(0, 0, 0, 0));
                    Raylib.EndBlendMode();
                    break;
                case "dark_matter":
                    // A slow vortex: dark motes drawn into the pool, a violet sheen at its heart.
                    for (int i = 0; i < 18; i++)
                    {
                        float life = (tt * 0.18f + i / 18f) % 1f;
                        float a = i * 2.39f + life * 5;
                        var c = p + new Vector2(MathF.Cos(a), MathF.Sin(a) * 0.62f) * size * 0.75f * (1 - life);
                        Raylib.DrawCircleV(c, size * 0.025f, new Color((byte)150, (byte)100, (byte)255, (byte)(180 * life)));
                    }
                    Raylib.BeginBlendMode(BlendMode.Additive);
                    Raylib.DrawCircleGradient(p, size * 0.22f, G0(110), new Color(0, 0, 0, 0));
                    Raylib.EndBlendMode();
                    break;
                case "nanites":
                    // A shimmering swarm crawling over the ground.
                    Raylib.BeginBlendMode(BlendMode.Additive);
                    for (int i = 0; i < 40; i++)
                    {
                        var at = spots[i % spots.Count];
                        var c = at + new Vector2(MathF.Sin(tt * (0.7f + i % 5 * 0.13f) + i), MathF.Sin(tt * (0.9f + i % 3 * 0.17f) + i * 1.7f) * 0.6f) * size * 0.16f;
                        Raylib.DrawCircleV(c, Math.Max(1, size * 0.012f), new Color((byte)215, (byte)238, (byte)250, (byte)(120 + 100 * MathF.Sin(tt * 6 + i))));
                    }
                    Raylib.EndBlendMode();
                    break;
                case "ruins":
                    // Standing stones of a vanished people, their glyphs faintly lit.
                    for (int i = 0; i < 5; i++)
                    {
                        var at = i == 0 ? p + new Vector2(0, size * 0.06f) : spots[i < 3 ? i : i + 2];
                        float h = size * (i == 0 ? 0.42f : 0.24f), wd = h * 0.3f;
                        var r = new Rectangle(at.X - wd / 2, at.Y - h, wd, h);
                        Raylib.DrawRectangleRec(r, new Color(150, 136, 108, 255));
                        Raylib.DrawRectangleRec(new Rectangle(r.X + wd * 0.55f, r.Y, wd * 0.45f, h), new Color(112, 100, 78, 255));
                        Raylib.DrawRectangleRec(new Rectangle(r.X - wd * 0.15f, r.Y - h * 0.08f, wd * 1.3f, h * 0.1f), new Color(170, 156, 126, 255));
                        float gl = 0.5f + 0.5f * MathF.Sin(tt * 1.5f + i);
                        Raylib.DrawRectangleRec(new Rectangle(r.X + wd * 0.3f, r.Y + h * 0.3f, wd * 0.2f, h * 0.4f), G0((byte)(90 + 120 * gl)));
                    }
                    break;
                case "zro":
                    // Glowing spore stalks, and spores drifting up.
                    for (int i = 0; i < spots.Count; i++)
                    {
                        float h = size * (i == 0 ? 0.36f : 0.2f);
                        var b = spots[i] + new Vector2(0, size * 0.08f);
                        Raylib.DrawLineEx(b, b - new Vector2(0, h), Math.Max(1.5f, size * 0.025f), new Color(120, 90, 160, 255));
                        float cap = size * (i == 0 ? 0.11f : 0.065f);
                        Raylib.DrawEllipse((int)b.X, (int)(b.Y - h), cap, cap * 0.55f, new Color(150, 110, 235, 255));
                        Raylib.DrawEllipse((int)b.X, (int)(b.Y - h - cap * 0.12f), cap * 0.7f, cap * 0.3f, new Color(205, 180, 255, 255));
                        Raylib.BeginBlendMode(BlendMode.Additive);
                        Raylib.DrawCircleGradient(b - new Vector2(0, h), size * 0.12f, G0(120), new Color(0, 0, 0, 0));
                        Raylib.EndBlendMode();
                    }
                    Raylib.BeginBlendMode(BlendMode.Additive);
                    for (int i = 0; i < 22; i++)
                    {
                        float life = (tt * 0.15f + i * 0.0457f) % 1f;
                        var c = spots[i % spots.Count] + new Vector2(MathF.Sin(life * 6 + i) * size * 0.1f, -life * size * 0.8f);
                        Raylib.DrawCircleV(c, Math.Max(1, size * 0.018f), G0((byte)(200 * MathF.Sin(life * MathF.PI))));
                    }
                    Raylib.EndBlendMode();
                    break;
                case "motes":
                    // Rocks floating above the ground on a glow, bobbing.
                    for (int i = 0; i < 6; i++)
                    {
                        var at = spots[i == 0 ? 0 : i + 2];
                        float bob = MathF.Sin(tt * 1.2f + i * 1.9f) * size * 0.04f;
                        float rs = size * (i == 0 ? 0.22f : 0.12f);
                        var c = at - new Vector2(0, size * (i == 0 ? 0.42f : 0.28f)) + new Vector2(0, bob);
                        Raylib.DrawEllipse((int)at.X, (int)(at.Y + size * 0.04f), rs * 0.9f, rs * 0.3f, new Color(0, 0, 0, 90));
                        Raylib.BeginBlendMode(BlendMode.Additive);
                        Raylib.DrawCircleGradient(at, rs * 1.3f, G0(90), new Color(0, 0, 0, 0));
                        Raylib.EndBlendMode();
                        Raylib.DrawPoly(c, 5, rs, i * 37 + tt * 6, new Color(92, 72, 62, 255));
                        Raylib.DrawPoly(c + new Vector2(-rs * 0.15f, -rs * 0.15f), 5, rs * 0.65f, i * 37 + tt * 6, new Color(130, 104, 88, 255));
                        Raylib.DrawPolyLinesEx(c, 5, rs, i * 37 + tt * 6, 1.5f, G0(170));
                    }
                    break;
                case "storm_sea":
                    // Lightning striking the water now and then, a flash of light around it.
                    for (int i = 0; i < 3; i++)
                    {
                        float cyc = tt * 0.6f + i * 0.41f;
                        float ph = cyc % 1f;
                        if (ph > 0.12f) continue;
                        var at = spots[(int)(cyc + i * 3) % spots.Count];
                        var top = at - new Vector2(0, size * 0.85f);
                        var prev = top;
                        var rng = new Rng((ulong)(cyc * 13 + i));
                        for (int j = 1; j <= 5; j++)
                        {
                            var nxt = Vector2.Lerp(top, at, j / 5f) + new Vector2(((float)rng.Double() - 0.5f) * size * 0.16f, 0);
                            Raylib.DrawLineEx(prev, nxt, Math.Max(1.5f, size * 0.022f), new Color((byte)225, (byte)242, (byte)255, (byte)(255 * (1 - ph / 0.12f))));
                            prev = nxt;
                        }
                        Raylib.BeginBlendMode(BlendMode.Additive);
                        Raylib.DrawCircleGradient(at, size * 0.35f, G0((byte)(150 * (1 - ph / 0.12f))), new Color(0, 0, 0, 0));
                        Raylib.EndBlendMode();
                    }
                    break;
            }
        }
    }
}
