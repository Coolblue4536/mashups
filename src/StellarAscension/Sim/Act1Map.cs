namespace StellarAscension;

/// <summary>Makes the Act 1 world: continents from wrapping value noise, Civ VI terrain by latitude and moisture,
/// hills and mountain ranges, features, natural wonders and start positions.</summary>
public static partial class Act1Map
{
    public static int TerrainIndex(string id) => Array.FindIndex(Defs.Terrains, t => t.Id == id);
    public static int FeatureIndex(string id) => Array.FindIndex(Defs.Features, f => f.Id == id);

    sealed class Noise
    {
        readonly double[,] _grid;
        readonly int _gw, _gh;
        public Noise(Rng rng, int gw, int gh)
        {
            _gw = gw; _gh = gh;
            _grid = new double[gw, gh + 1];
            for (int x = 0; x < gw; x++) for (int y = 0; y <= gh; y++) _grid[x, y] = rng.Double();
        }
        static double S(double t) => t * t * (3 - 2 * t);
        /// <summary>u in [0,1) wraps; v in [0,1].</summary>
        public double At(double u, double v)
        {
            double fx = u * _gw, fy = v * _gh;
            int x0 = (int)Math.Floor(fx), y0 = Math.Min((int)Math.Floor(fy), _gh - 1);
            double tx = S(fx - x0), ty = S(fy - y0);
            int xa = ((x0 % _gw) + _gw) % _gw, xb = (xa + 1) % _gw;
            double a = _grid[xa, y0] + (_grid[xb, y0] - _grid[xa, y0]) * tx;
            double b = _grid[xa, y0 + 1] + (_grid[xb, y0 + 1] - _grid[xa, y0 + 1]) * tx;
            return a + (b - a) * ty;
        }
    }

    static double[] Field(Rng rng, int w, int h, int baseCells, int octaves)
    {
        var layers = Enumerable.Range(0, octaves).Select(o => new Noise(rng, baseCells << o, Math.Max(2, (baseCells << o) * h / w))).ToList();
        var f = new double[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double u = (x + ((y & 1) == 1 ? 0.5 : 0)) / w, v = (y + 0.5) / h, sum = 0, amp = 1, norm = 0;
                foreach (var l in layers) { sum += l.At(u, v) * amp; norm += amp; amp *= 0.5; }
                f[y * w + x] = sum / norm;
            }
        return f;
    }

    static double Percentile(IEnumerable<double> vals, double p)
    {
        var s = vals.OrderBy(v => v).ToArray();
        return s.Length == 0 ? 0 : s[Math.Clamp((int)(p * (s.Length - 1)), 0, s.Length - 1)];
    }

    public static void Generate(Act1State s, Content content, Rng rng, int naturalCount)
    {
        int w = s.W, h = s.H;
        var elev = Field(rng, w, h, 5, 4);
        var moist = Field(rng, w, h, 4, 3);
        // Oceans at the poles and a little at the map's seam, so continents form in the middle.
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double lat = Math.Abs((y + 0.5) / h * 2 - 1);
                elev[y * w + x] -= Math.Pow(lat, 3) * 0.35;
            }
        double seaLevel = Percentile(elev, 0.60);
        var land = elev.Select(e => e > seaLevel).ToArray();
        var landElev = elev.Where((e, i) => land[i]).ToArray();
        double hillLevel = Percentile(landElev, 0.72), mountainLevel = Percentile(landElev, 0.94);
        s.Tiles = new Tile[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                double lat = Math.Abs((y + 0.5) / h * 2 - 1);
                string id;
                if (!land[i]) id = "TERRAIN_OCEAN";
                else
                {
                    double m = moist[i];
                    string b = lat > 0.86 ? "SNOW" : lat > 0.7 ? "TUNDRA"
                        : lat < 0.45 && m < 0.42 ? "DESERT"
                        : m > 0.52 ? "GRASS" : "PLAINS";
                    id = "TERRAIN_" + b + (elev[i] > mountainLevel ? "_MOUNTAIN" : elev[i] > hillLevel ? "_HILLS" : "");
                }
                s.Tiles[i] = new Tile { Terrain = TerrainIndex(id) };
            }
        int coast = TerrainIndex("TERRAIN_COAST");
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (!land[y * w + x] && Hex.Neighbors(x, y, w, h).Any(n => land[n.y * w + n.x])) s.Tiles[y * w + x].Terrain = coast;
        // Features
        for (int i = 0; i < s.Tiles.Length; i++)
        {
            var t = Defs.Terrains[s.Tiles[i].Terrain];
            if (!t.Land || !t.Passable) continue;
            foreach (var f in Defs.Features)
                if (f.Terrains.Contains(t.Id) && rng.Chance(f.Density * (f.Id == "FEATURE_JUNGLE" ? (Math.Abs(((i / w) + 0.5) / h * 2 - 1) < 0.35 ? 1.8 : 0.1) : 1)))
                { s.Tiles[i].Feature = FeatureIndex(f.Id); break; }
        }
        PlaceNaturalWonders(s, content, rng, naturalCount);
    }

    static void PlaceNaturalWonders(Act1State s, Content content, Rng rng, int count)
    {
        var pool = content.NaturalWonders.ToList();
        rng.Shuffle(pool);
        var placed = new List<(int x, int y)>();
        foreach (var (type, isLand) in pool)
        {
            if (placed.Count >= count) break;
            var cands = new List<(int x, int y)>();
            for (int y = 2; y < s.H - 2; y++)
                for (int x = 0; x < s.W; x++)
                {
                    var tile = s.At(x, y);
                    var t = Defs.Terrains[tile.Terrain];
                    if (tile.Natural != null) continue;
                    if (isLand ? !(t.Land && t.Passable) : t.Id != "TERRAIN_COAST") continue;
                    if (placed.Any(p => Hex.Distance(p.x, p.y, x, y, s.W) < 7)) continue;
                    if (isLand && Hex.Neighbors(x, y, s.W, s.H).Count(n => Defs.Terrains[s.At(n.x, n.y).Terrain].Land) < 4) continue;
                    cands.Add((x, y));
                }
            if (cands.Count == 0) continue;
            var c = rng.Pick(cands);
            s.At(c.x, c.y).Natural = type;
            s.At(c.x, c.y).Feature = -1;
            placed.Add(c);
        }
    }

    /// <summary>Scatters Civ VI's resources by its own frequencies and valid terrains, then makes sure every start
    /// has a food resource, Horses and Iron within reach (Civ VI's "balanced" strategic resources).</summary>
    public static void PlaceResources(Act1State s, Content content, Rng rng, List<(int x, int y)> starts)
    {
        if (content.MapResources.Count == 0) return;
        bool Fits(MapResource r, int x, int y)
        {
            var tile = s.At(x, y);
            if (tile.Resource != null || tile.Natural != null || s.CityAt(x, y) != null) return false;
            var t = Defs.Terrains[tile.Terrain];
            if (tile.Feature >= 0 && r.Features.Contains(Defs.Features[tile.Feature].Id)) return true;
            return r.Terrains.Contains(t.Id) && (tile.Feature < 0 || r.Features.Length == 0 && Defs.Features[tile.Feature].Id is "FEATURE_FOREST" or "FEATURE_JUNGLE");
        }
        for (int y = 1; y < s.H - 1; y++)
            for (int x = 0; x < s.W; x++)
            {
                if (!rng.Chance(Rules.MapResourceDensity)) continue;
                bool water = !Defs.Terrains[s.At(x, y).Terrain].Land;
                var cands = content.MapResources.Where(r => (water ? r.SeaFrequency : r.Frequency) > 0 && Fits(r, x, y)).ToList();
                if (cands.Count == 0) continue;
                s.At(x, y).Resource = rng.Weighted(cands, r => water ? r.SeaFrequency : r.Frequency).Type;
            }
        foreach (var (sx, sy) in starts)
        {
            void Ensure(Func<MapResource, bool> which, int min, int max)
            {
                var near = Hex.Within(sx, sy, max, s.W, s.H).Where(t => Hex.Distance(sx, sy, t.x, t.y, s.W) >= min).ToList();
                if (near.Any(t => s.At(t.x, t.y).Resource is { } r && content.MapResource.TryGetValue(r, out var d) && which(d))) return;
                var opts = content.MapResources.Where(which).ToList();
                if (opts.Count == 0) return;
                foreach (var t in near.OrderBy(_ => rng.Double()))
                    if (opts.Where(r => Fits(r, t.x, t.y)).Select(r => r.Type).FirstOrDefault() is { } type) { s.At(t.x, t.y).Resource = type; return; }
                // No suitable ground nearby: put it on any free land tile in reach (Civ VI also adjusts starts for balance).
                foreach (var t in near.OrderBy(_ => rng.Double()))
                {
                    var tile = s.At(t.x, t.y);
                    if (tile.Resource == null && tile.Natural == null && s.CityAt(t.x, t.y) == null && Defs.Terrains[tile.Terrain].Land && Defs.Terrains[tile.Terrain].Passable)
                    { tile.Resource = opts[0].Type; return; }
                }
            }
            Ensure(r => r.Class == "bonus" && r.Yield.Food > 0 && r.Frequency > 0, 1, 2);
            Ensure(r => r.Type == "RESOURCE_HORSES", 1, 3);
            Ensure(r => r.Type == "RESOURCE_IRON", 2, 4);
            Ensure(r => r.Type == "RESOURCE_NITER", 2, 5);
        }
    }

    /// <summary>Good, well-spread start tiles: flat grass or plains with food and production around.</summary>
    public static List<(int x, int y)> StartPositions(Act1State s, int count, Rng rng)
    {
        var cands = new List<(int x, int y, double score)>();
        for (int y = 3; y < s.H - 3; y++)
            for (int x = 0; x < s.W; x++)
                if (Act1.CanSettleTerrain(s, x, y))
                {
                    var tid = Defs.Terrains[s.At(x, y).Terrain].Id;
                    if (!(tid.StartsWith("TERRAIN_GRASS") || tid.StartsWith("TERRAIN_PLAINS"))) continue;
                    double score = Hex.Within(x, y, 2, s.W, s.H).Sum(t => Act1.TileValue(s, t.x, t.y));
                    if (Hex.Neighbors(x, y, s.W, s.H).Any(n => Defs.Terrains[s.At(n.x, n.y).Terrain].Id == "TERRAIN_COAST")) score += 2;
                    cands.Add((x, y, score));
                }
        if (cands.Count == 0) throw new InvalidOperationException("map has no land to start on");
        double best = cands.Max(c => c.score);
        var good = cands.Where(c => c.score >= best * 0.6).ToList();
        var starts = new List<(int x, int y)>();
        var first = good[rng.Int(good.Count)];
        starts.Add((first.x, first.y));
        while (starts.Count < count)
        {
            var next = good.Where(c => !starts.Contains((c.x, c.y)))
                .OrderByDescending(c => Math.Min(starts.Min(p => Hex.Distance(p.x, p.y, c.x, c.y, s.W)), 14) * 10 + c.score + rng.Double())
                .FirstOrDefault();
            if (next == default) break;
            starts.Add((next.x, next.y));
        }
        return starts;
    }
}
