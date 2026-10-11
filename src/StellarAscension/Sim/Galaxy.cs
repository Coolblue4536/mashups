namespace StellarAscension;

/// <summary>
/// Makes the Act 2 galaxy: a Stellaris-style spiral with one arm per empire. One arm (its stars, planets and
/// hyperlanes) is made at random, then turned around the core once for every empire, so every empire's
/// neighbourhood is identical (fair by construction) while each game's layout is new. Hyperlanes never cross, and
/// star names differ arm to arm.
/// </summary>
public static class Galaxy
{
    public const int HabitableNearHome = 3;
    const float MinGap = 46, MaxLane = 135;

    static readonly string[] FallbackStars = { "Aldebaran", "Achernar", "Rigel", "Vega", "Deneb", "Mira", "Polaris", "Sirius", "Procyon", "Capella", "Spica", "Antares" };

    static (float x, float y) Rot((float x, float y) p, double a) =>
        ((float)(p.x * Math.Cos(a) - p.y * Math.Sin(a)), (float)(p.x * Math.Sin(a) + p.y * Math.Cos(a)));

    static float Dist((float x, float y) a, (float x, float y) b) => MathF.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));

    /// <summary>Builds the galaxy and returns one home system per empire (in arm order).</summary>
    public static List<int> Make(Act2State s, Content content, Rng rng, int systems, int empires)
    {
        int E = Math.Max(1, empires), m = Math.Max(9, (int)Math.Ceiling(systems / (double)E));
        float R = 62f * MathF.Sqrt(E * m) * 1.05f;
        s.GalaxyRadius = R;
        double wedge = 2 * Math.PI / E;
        double twist = (rng.Chance(0.5) ? 1 : -1) * (2.0 + rng.Double() * 1.4);

        // ---- one arm: stars spread along a spiral curve, kept apart from every copy of themselves and each other
        var arm = new List<(float x, float y)>();
        int guard = 0;
        while (arm.Count < m && guard++ < m * 5000)
        {
            double r = R * (0.2 + 0.8 * Math.Sqrt(rng.Double()));
            double spread = wedge * (0.18 + 0.22 * (r / R));
            double phi = wedge / 2 + twist * (r / R) + Gauss(rng) * spread;
            var p = ((float)(r * Math.Cos(phi)), (float)(r * Math.Sin(phi)));
            bool ok = true;
            for (int k = 0; k < E && ok; k++)
            {
                var q = Rot(p, k * wedge);
                if (k > 0 && Dist(p, q) < MinGap) ok = false;
                foreach (var a in arm) if (Dist(a, q) < MinGap) { ok = false; break; }
            }
            if (ok) arm.Add(p);
        }
        m = arm.Count;
        var pts = new List<(float x, float y)>();
        for (int k = 0; k < E; k++) foreach (var a in arm) pts.Add(Rot(a, k * wedge));
        int Idx(int k, int i) => ((k % E) + E) % E * m + i;

        // ---- systems; stars and planets are made for the first arm and copied to the others
        var names = (content.Stellaris?.StarNames.Count > 0 ? content.Stellaris.StarNames : FallbackStars.ToList()).Distinct().ToList();
        rng.Shuffle(names);
        for (int j = 0; j < pts.Count; j++)
            s.Systems.Add(new StarSystem { Id = j, X = pts[j].x, Y = pts[j].y, Name = j < names.Count ? names[j] : $"System {j + 1}" });
        var stars = new string[m];
        var planets = new List<(string cls, int size)>[m];
        for (int i = 0; i < m; i++)
        {
            stars[i] = rng.Weighted(Defs.StarClasses, c => c.Weight).Id;
            planets[i] = new();
            int count = stars[i].Contains("black_hole") || stars[i].Contains("pulsar") || stars[i].Contains("neutron") ? rng.Range(0, 2) : rng.Range(1, 6);
            for (int c = 0; c < count; c++)
            {
                var pc = rng.Weighted(Defs.PlanetClasses, x => x.Weight);
                planets[i].Add((pc.Id, rng.Range(pc.SizeMin, pc.SizeMax)));
            }
        }

        // ---- hyperlanes: every lane is added in all its turned copies at once, so the arms stay identical
        var edges = new List<(int a, int b)>();
        bool Crosses(int a, int b) => edges.Any(e => e.a != a && e.a != b && e.b != a && e.b != b && Intersect(pts[a], pts[b], pts[e.a], pts[e.b]));
        bool TryClass(int i, int dk, int j, bool forceConnect = false)
        {
            var copies = Enumerable.Range(0, E).Select(k => (a: Idx(k, i), b: Idx(k + dk, j))).Where(e => e.a != e.b).Distinct().ToList();
            if (copies.Count == 0 || copies.Any(e => s.Systems[e.a].Lanes.Contains(e.b))) return false;
            if (!forceConnect && copies.Any(e => s.Systems[e.a].Lanes.Count >= 4 || s.Systems[e.b].Lanes.Count >= 4)) return false;
            foreach (var e in copies) if (Crosses(e.a, e.b)) return false;
            for (int x = 0; x < copies.Count; x++)
                for (int y = x + 1; y < copies.Count; y++)
                {
                    var (p, q) = (copies[x], copies[y]);
                    if (p.a != q.a && p.a != q.b && p.b != q.a && p.b != q.b && Intersect(pts[p.a], pts[p.b], pts[q.a], pts[q.b])) return false;
                }
            foreach (var e in copies)
            {
                edges.Add(e);
                s.Systems[e.a].Lanes.Add(e.b);
                s.Systems[e.b].Lanes.Add(e.a);
            }
            return true;
        }
        var classes = new List<(int i, int dk, int j, float d)>();
        for (int i = 0; i < m; i++)
            foreach (var t in Enumerable.Range(0, pts.Count).Where(t => t != Idx(0, i)).OrderBy(t => Dist(pts[Idx(0, i)], pts[t])).Take(5))
                classes.Add((i, t / m, t % m, Dist(pts[Idx(0, i)], pts[t])));
        foreach (var c in classes.OrderBy(c => c.d).ThenBy(c => c.i).ThenBy(c => c.j))
            if (c.d < MaxLane) TryClass(c.i, c.dk, c.j);
        while (true)
        {
            var comp = Handoff.Bfs(s, 0).Select(x => x.id).ToHashSet();
            if (comp.Count == s.Systems.Count) break;
            bool linked = false;
            foreach (var c in Enumerable.Range(0, m).SelectMany(i => Enumerable.Range(0, pts.Count).Select(t => (i, dk: t / m, j: t % m, d: Dist(pts[Idx(0, i)], pts[t]))))
                         .Where(c => comp.Contains(Idx(0, c.i)) != comp.Contains(Idx(c.dk, c.j))).OrderBy(c => c.d))
                if (TryClass(c.i, c.dk, c.j, true)) { linked = true; break; }
            if (!linked) break;
        }

        // ---- home: the same spot in every arm, out of the core, well connected, far from the neighbouring arms' homes
        int bestHome = 0; double bestScore = double.MinValue;
        for (int i = 0; i < m; i++)
        {
            var x = s.Systems[Idx(0, i)];
            float r = MathF.Sqrt(x.X * x.X + x.Y * x.Y);
            if (x.Lanes.Count < 2 || stars[i].Contains("black_hole") || stars[i].Contains("pulsar") || r < R * 0.4f) continue;
            var d = Handoff.Bfs(s, Idx(0, i)).ToDictionary(b => b.id, b => b.dist);
            int gap = E == 1 ? 9 : Enumerable.Range(1, E - 1).Min(k => d.GetValueOrDefault(Idx(k, i), 99));
            double score = Math.Min(gap, 8) * 10 + x.Lanes.Count * 2 - Math.Abs(r / R - 0.65) * 10;
            if (score > bestScore) { bestScore = score; bestHome = i; }
        }
        // The arm's worlds near home: exactly HabitableNearHome habitable ones within three jumps (counted in arm 0).
        var nearHome = Handoff.Bfs(s, Idx(0, bestHome)).Where(b => b.dist is >= 1 and <= 3).Select(b => b.id).ToList();
        var habClasses = Defs.PlanetClasses.Where(c => c.Habitable && c.Id != "pc_gaia").ToList();
        string[] barren = { "pc_barren", "pc_barren_cold", "pc_toxic", "pc_frozen", "pc_molten" };
        int habCount = 0;
        foreach (var id in nearHome)
        {
            int i = id % m;
            for (int c = 0; c < planets[i].Count; c++)
            {
                if (!Defs.PlanetClass[planets[i][c].cls].Habitable) continue;
                if (habCount < HabitableNearHome) { habCount++; planets[i][c] = (planets[i][c].cls, Math.Clamp(planets[i][c].size, 12, 18)); }
                else planets[i][c] = (rng.Pick(barren), planets[i][c].size);
            }
        }
        foreach (var id in nearHome.Where(id => id % m != bestHome))
        {
            if (habCount >= HabitableNearHome) break;
            int i = id % m;
            if (planets[i].Any(p => Defs.PlanetClass[p.cls].Habitable) || stars[i].Contains("black_hole")) continue;
            planets[i].Add((rng.Pick(habClasses).Id, rng.Range(12, 16)));
            habCount++;
        }
        // Copy the arm to every empire.
        for (int k = 0; k < E; k++)
            for (int i = 0; i < m; i++)
            {
                var sys = s.Systems[Idx(k, i)];
                sys.Star = stars[i];
                foreach (var (cls, size) in planets[i])
                    sys.Planets.Add(new Planet { Id = s.NextPlanetId++, Class = cls, Size = size, Name = $"{sys.Name} {Handoff.Roman(sys.Planets.Count + 1)}" });
            }
        // Nebulae for the backdrop, the same in every arm.
        int neb = 1 + rng.Int(2);
        for (int n = 0; n < neb; n++)
        {
            var p = arm[rng.Int(arm.Count)];
            float nr = R * (0.1f + (float)rng.Double() * 0.08f), rot = (float)(rng.Double() * 360);
            int tint = rng.Int(3);
            for (int k = 0; k < E; k++) { var q = Rot(p, k * wedge); s.Nebulae.Add(new Nebula { X = q.x, Y = q.y, R = nr, Rotation = rot + (float)(k * wedge * 180 / Math.PI), Tint = tint }); }
        }
        return Enumerable.Range(0, E).Select(k => Idx(k, bestHome)).ToList();
    }

    static double Gauss(Rng rng)
    {
        double u1 = Math.Max(1e-9, rng.Double()), u2 = rng.Double();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    static bool Intersect((float x, float y) p1, (float x, float y) p2, (float x, float y) p3, (float x, float y) p4)
    {
        static float Cross((float x, float y) o, (float x, float y) a, (float x, float y) b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        float d1 = Cross(p3, p4, p1), d2 = Cross(p3, p4, p2), d3 = Cross(p1, p2, p3), d4 = Cross(p1, p2, p4);
        return (d1 > 0) != (d2 > 0) && (d3 > 0) != (d4 > 0);
    }

    /// <summary>For tests and the balance report: each home's free habitable worlds and systems with planets within 3 jumps.</summary>
    public static List<(int home, int habitable, int claimable)> Fairness(Act2State s)
    {
        return s.Systems.Where(x => x.Home).Select(h =>
        {
            var near = Handoff.Bfs(s, h.Id).Where(b => b.dist is >= 1 and <= 3).Select(b => s.Sys(b.id)).Where(x => !x.Home && x.RareFeature == null).ToList();
            return (h.Id, near.Sum(x => x.Planets.Count(p => Act2.Habitable(p) && p.Owner < 0)), near.Count(x => x.Planets.Count > 0));
        }).ToList();
    }
}
