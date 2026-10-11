namespace StellarAscension;

/// <summary>Civ VI's Builders: a civilian with a few charges that improves tiles inside its civ's borders
/// (farms, mines, quarries, plantations, pastures, camps, fishing boats, lumber mills, oil wells).</summary>
public static class Builders
{
    /// <summary>Why this improvement can't go on the tile for this civ, or null.</summary>
    public static string? CantImprove(Act1State s, Content c, CivState civ, int x, int y, ImprovementDef imp)
    {
        var tile = s.At(x, y);
        if (tile.City < 0 || s.City(tile.City)?.Owner != civ.Id) return "Only inside your borders.";
        if (s.CityAt(x, y) != null || tile.Built != null) return "A city or district stands here.";
        if (tile.Natural != null || tile.Exotic != null || tile.Camp) return "Can't be improved.";
        if (tile.Improvement == imp.Id) return "Already built.";
        if (imp.PrereqTech != "none" && !civ.Techs.Contains(imp.PrereqTech)) return $"Needs {c.Name(imp.PrereqTech)}.";
        bool resource = tile.Resource != null && imp.Resources.Contains(tile.Resource) && Military.CanSee(c, civ, tile.Resource);
        string? feature = tile.Feature >= 0 ? Defs.Features[tile.Feature].Id : null;
        bool fits = resource
                    || feature != null && imp.Features.Contains(feature)
                    || imp.Terrains.Contains(Defs.Terrains[tile.Terrain].Id) && (feature == null || feature == "FEATURE_FLOODPLAINS" && imp.Features.Contains(feature));
        if (!fits) return "Doesn't suit this tile.";
        // A tile with a resource takes the improvement that works it (Civ VI), so a farm can't bury iron.
        if (tile.Resource != null && Military.CanSee(c, civ, tile.Resource) && !imp.Resources.Contains(tile.Resource)
            && Defs.Improvements.Any(o => o.Resources.Contains(tile.Resource))) return "This tile's resource needs a different improvement.";
        return null;
    }

    public static IEnumerable<ImprovementDef> Options(Act1State s, Content c, CivState civ, int x, int y) =>
        Defs.Improvements.Where(i => CantImprove(s, c, civ, x, y, i) == null);

    public static bool Improve(Act1State s, Content c, UnitState u, string impId)
    {
        var civ = s.Civs[u.Owner];
        if (u.Type != "UNIT_BUILDER" || u.Charges <= 0 || u.Moves <= 0 || !Defs.Improvement.TryGetValue(impId, out var imp)) return false;
        if (CantImprove(s, c, civ, u.X, u.Y, imp) != null) return false;
        s.At(u.X, u.Y).Improvement = imp.Id;
        u.Charges--;
        u.Moves = 0;
        u.Acted = true;
        if (civ.IsPlayer) s.Events.Add(new Act1Event(s.Turn, "improved", $"Built a {c.Text(imp.NameLoc)}" + (u.Charges > 0 ? $" ({u.Charges} charge{(u.Charges == 1 ? "" : "s")} left)." : "; the Builder is used up."), civ.Id, u.X, u.Y));
        if (u.Charges <= 0) s.Units.Remove(u);
        return true;
    }

    static double Gain(ImprovementDef i) => i.Food * 1.3 + i.Production * 1.2 + i.Gold * 0.6 + i.Science;

    /// <summary>The AI (and the player's civ in automated runs): improve the best worked tile nearby.</summary>
    public static void Automate(Act1State s, Content c, UnitState u)
    {
        var civ = s.Civs[u.Owner];
        if (u.Moves <= 0) return;
        var here = Options(s, c, civ, u.X, u.Y).OrderByDescending(Gain).FirstOrDefault();
        var worked = s.CitiesOf(civ.Id).SelectMany(city => Act1.WorkTiles(s, city)).ToHashSet();
        if (here != null && (worked.Contains((u.X, u.Y)) || s.At(u.X, u.Y).Resource != null)) { Improve(s, c, u, here.Id); return; }
        (int x, int y)? best = null; double score = 0;
        foreach (var city in s.CitiesOf(civ.Id))
            foreach (var (x, y) in Hex.Within(city.X, city.Y, (int)Rules.CityRadius, s.W, s.H))
            {
                if (s.At(x, y).Improvement != null) continue;
                var opt = Options(s, c, civ, x, y).OrderByDescending(Gain).FirstOrDefault();
                if (opt == null) continue;
                double v = Gain(opt) * (worked.Contains((x, y)) ? 2 : 1) - Hex.Distance(u.X, u.Y, x, y, s.W) * 0.3;
                if (v > score && Military.CanEnter(s, u, x, y, true)) { score = v; best = (x, y); }
            }
        if (best is { } b && Military.PathTo(s, u, b.x, b.y, null, 2000) is { } path)
        {
            Military.Step(s, u, path);
            if (s.Units.Contains(u) && u.X == b.x && u.Y == b.y && u.Moves > 0 && Options(s, c, civ, u.X, u.Y).OrderByDescending(Gain).FirstOrDefault() is { } now)
                Improve(s, c, u, now.Id);
        }
    }
}
