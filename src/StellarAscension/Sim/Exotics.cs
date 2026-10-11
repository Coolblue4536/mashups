namespace StellarAscension;

/// <summary>Exotic sites (Stellaris's strange deposits): a few per game, on the Act 1 map and on Act 2 planets.
/// Held in Act 1 they yield more, give an empire-wide bonus once enough land around them is yours too, and at the
/// handoff let that empire research a special technology; in Act 2 the planet's holder gets a bonus and the same option.</summary>
public static class Exotics
{
    /// <summary>The sites a civ holds in Act 1 with enough of their neighbouring tiles (for the empire-wide bonus).</summary>
    public static IEnumerable<ExoticSiteDef> Held(Act1State s, CivState civ, bool needRing = true)
    {
        for (int i = 0; i < s.Tiles.Length; i++)
        {
            var t = s.Tiles[i];
            if (t.Exotic == null || t.City < 0 || s.City(t.City)?.Owner != civ.Id) continue;
            int x = i % s.W, y = i / s.W;
            if (needRing && Hex.Neighbors(x, y, s.W, s.H).Count(n => s.OwnerAt(n.x, n.y) == civ.Id) < Rules.ExoticRingNeeded) continue;
            if (Defs.ExoticSite.TryGetValue(t.Exotic, out var d)) yield return d;
        }
    }

    public static double Bonus(Act1State s, CivState civ, string stat) => Held(s, civ).Where(d => d.Act1Stat == stat).Sum(d => d.Act1Value);

    /// <summary>The exotic sites an Act 2 empire may draw on: from Act 1, and planets it holds now.</summary>
    public static HashSet<string> Known(Act2State s, Empire e)
    {
        var set = new HashSet<string>(e.Exotics);
        foreach (var sys in s.SystemsOf(e.Id))
            foreach (var p in sys.Planets)
                if (p.Exotic != null) set.Add(p.Exotic);
        return set;
    }
}

public static partial class Act1Map
{
    /// <summary>Places a few exotic sites (random from the pool) in the open, away from starts and each other.</summary>
    public static void PlaceExotics(Act1State s, Rng rng, List<(int x, int y)> starts, int count)
    {
        var pool = Defs.ExoticSites.ToList();
        rng.Shuffle(pool);
        var placed = new List<(int x, int y)>();
        foreach (var ex in pool)
        {
            if (placed.Count >= count) break;
            var cands = new List<(int x, int y)>();
            for (int y = 2; y < s.H - 2; y++)
                for (int x = 0; x < s.W; x++)
                {
                    var tile = s.At(x, y);
                    var t = Defs.Terrains[tile.Terrain];
                    if (tile.Natural != null || tile.Exotic != null || s.CityAt(x, y) != null) continue;
                    if (ex.Land ? !(t.Land && t.Passable) : t.Id != "TERRAIN_COAST") continue;
                    if (starts.Any(p => Hex.Distance(p.x, p.y, x, y, s.W) < 4)) continue;
                    if (placed.Any(p => Hex.Distance(p.x, p.y, x, y, s.W) < 6)) continue;
                    if (ex.Land && Hex.Neighbors(x, y, s.W, s.H).Count(n => Defs.Terrains[s.At(n.x, n.y).Terrain].Land) < 4) continue;
                    cands.Add((x, y));
                }
            if (cands.Count == 0) continue;
            var c = rng.Pick(cands);
            var at = s.At(c.x, c.y);
            at.Exotic = ex.Id;
            at.Feature = -1;
            at.Resource = null;
            placed.Add(c);
        }
    }
}
