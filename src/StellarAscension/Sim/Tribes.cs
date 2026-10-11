namespace StellarAscension;

/// <summary>Act 1's tribes, Civ VI's barbarians and goody huts: camps in the wilds that send raiders after nearby
/// civilizations (until the modern age), cleared by moving a unit in (for gold), and tribal villages that reward
/// the first unit to enter.</summary>
public static class Tribes
{
    public static void Setup(Act1State s, Content content, Rng rng, List<(int x, int y)> starts)
    {
        var land = Enumerable.Range(0, s.Tiles.Length).Where(i => Defs.Terrains[s.Tiles[i].Terrain].Land && Defs.Terrains[s.Tiles[i].Terrain].Passable).ToList();
        int camps = (int)Math.Round(land.Count / 100.0 * Rules.TribeCampsPer100Land), villages = (int)Math.Round(land.Count / 100.0 * Rules.TribeVillagesPer100Land);
        var placed = new List<(int x, int y)>();
        bool Free(int i) { var t = s.Tiles[i]; return t.Natural == null && t.Exotic == null && t.City < 0 && !t.Camp && !t.Village && s.CityAt(i % s.W, i / s.W) == null; }
        foreach (var i in land.OrderBy(_ => rng.Double()))
        {
            if (placed.Count >= camps) break;
            int x = i % s.W, y = i / s.W;
            if (y < 2 || y > s.H - 3 || !Free(i) || starts.Any(p => Hex.Distance(p.x, p.y, x, y, s.W) < 7) || placed.Any(p => Hex.Distance(p.x, p.y, x, y, s.W) < 6)) continue;
            s.Tiles[i].Camp = true; s.Tiles[i].Feature = -1;
            placed.Add((x, y));
        }
        int v = 0;
        foreach (var i in land.OrderBy(_ => rng.Double()))
        {
            if (v >= villages) break;
            int x = i % s.W, y = i / s.W;
            if (y < 1 || y > s.H - 2 || !Free(i) || starts.Any(p => Hex.Distance(p.x, p.y, x, y, s.W) < 3) || placed.Any(p => Hex.Distance(p.x, p.y, x, y, s.W) < 3)) continue;
            s.Tiles[i].Village = true;
            placed.Add((x, y));
            v++;
        }
    }

    /// <summary>The best raider the tribes can field: what most civilizations already know.</summary>
    static UnitInfo? Raider(Act1State s, Content content)
    {
        var tribe = s.Tribe!;
        var majors = s.Majors.Where(c => c.Alive).ToList();
        tribe.Techs = content.Techs.Where(t => majors.Count(c => c.Techs.Contains(t.Type)) * 2 >= majors.Count).Select(t => t.Type).ToList();
        return content.Units.Where(u => u.Line is "melee" or "ranged" or "light_cavalry" or "anti_cavalry" && (u.PrereqTech == "none" || tribe.Techs.Contains(u.PrereqTech)))
            .OrderByDescending(u => u.Combat + u.Ranged).ThenBy(_ => s.Rng.Double()).FirstOrDefault();
    }

    public static void Turn(Act1State s, Content content)
    {
        var tribe = s.Tribe;
        if (tribe == null) return;
        int topEra = s.Majors.Where(c => c.Alive).Select(c => Act1.EraIndex(content, c)).DefaultIfEmpty(0).Max();
        // Camps send raiders out.
        if (topEra < Rules.TribeLastEra && s.Turn > 15)
            for (int i = 0; i < s.Tiles.Length; i++)
            {
                if (!s.Tiles[i].Camp) continue;
                int x = i % s.W, y = i / s.W;
                if ((s.Turn + i) % (int)Rules.TribeSpawnTurns != 0) continue;
                if (s.UnitsOf(tribe.Id).Count(u => u.HomeX == x && u.HomeY == y) >= (topEra == 0 ? 1 : 2)) continue;
                if (Raider(s, content) is { } r && Military.Spawn(s, content, tribe, r.Type, x, y) is { } raider)
                {
                    raider.HomeX = x; raider.HomeY = y;
                    raider.Moves = r.Moves;
                }
            }
        // Raiders: strike what they can, else head for the nearest civilization near their camp, else roam.
        foreach (var u in s.UnitsOf(tribe.Id).ToList())
        {
            if (!s.Units.Contains(u)) continue;
            var info = content.Unit[u.Type];
            u.Moves = info.Moves;
            int hx = u.HomeX >= 0 ? u.HomeX : u.X, hy = u.HomeY >= 0 ? u.HomeY : u.Y;
            if (TryStrike(s, content, u)) continue;
            var target = s.Units.Where(o => o.Owner != tribe.Id && Hex.Distance(o.X, o.Y, hx, hy, s.W) <= 6).Select(o => (o.X, o.Y))
                .Concat(s.Cities.Where(c => Hex.Distance(c.X, c.Y, hx, hy, s.W) <= 6).Select(c => (c.X, c.Y)))
                .OrderBy(t => Hex.Distance(t.Item1, t.Item2, u.X, u.Y, s.W)).Select(t => ((int, int)?)t).FirstOrDefault();
            if (target is { } tg)
            {
                int stop = info.IsRanged ? info.Range : 1;
                if (Military.PathTo(s, u, tg.Item1, tg.Item2, (x, y) => Hex.Distance(x, y, tg.Item1, tg.Item2, s.W) <= stop, 1500) is { } path) Military.Step(s, u, path);
                if (s.Units.Contains(u)) TryStrike(s, content, u);
                // Pillage an improvement it stands on.
                var tile = s.At(u.X, u.Y);
                if (s.Units.Contains(u) && tile.Improvement != null && tile.City >= 0 && s.Rng.Chance(0.4))
                {
                    var owner = s.City(tile.City)!.Owner;
                    tile.Improvement = null;
                    if (s.Civs[owner].IsPlayer) s.Events.Add(new Act1Event(s.Turn, "pillage", "Tribal raiders pillaged an improvement.", owner, u.X, u.Y));
                }
            }
            else
            {
                var spot = Hex.Within(hx, hy, 3, s.W, s.H).Where(t => Military.CanEnter(s, u, t.x, t.y, true)).OrderBy(_ => s.Rng.Double()).Select(t => ((int, int)?)t).FirstOrDefault();
                if (spot is { } sp && Military.PathTo(s, u, sp.Item1, sp.Item2, null, 400) is { } p) Military.Step(s, u, p);
            }
        }
    }

    static bool TryStrike(Act1State s, Content content, UnitState u)
    {
        var info = content.Unit[u.Type];
        int reach = info.IsRanged ? info.Range : 1;
        foreach (var t in Hex.Within(u.X, u.Y, reach, s.W, s.H).OrderBy(t => s.CityAt(t.x, t.y) != null))
        {
            if (!Military.CanAttack(s, content, u, t.x, t.y)) continue;
            var o = Military.Preview(s, content, u, t.x, t.y);
            if (o.ToAttacker > u.Hp * 0.7 || s.CityAt(t.x, t.y) != null && !info.IsRanged && o.ToAttacker > 25) continue;
            Military.Attack(s, content, u, t.x, t.y);
            return true;
        }
        return false;
    }

    /// <summary>A unit steps onto a camp or village: clear the camp, or take the village's reward.</summary>
    public static void Enter(Act1State s, Content content, UnitState u)
    {
        var tile = s.At(u.X, u.Y);
        var civ = s.Civs[u.Owner];
        if (civ.IsTribe) return;
        if (tile.Camp && u.Type != "UNIT_BUILDER")
        {
            tile.Camp = false;
            civ.Gold += Rules.TribeCampGold;
            s.Units.RemoveAll(o => o.Owner == s.Tribe?.Id && o.HomeX == u.X && o.HomeY == u.Y && Hex.Distance(o.X, o.Y, u.X, u.Y, s.W) <= 1);
            if (civ.IsPlayer) s.Events.Add(new Act1Event(s.Turn, "camp", $"Tribal camp cleared: +{Rules.TribeCampGold} gold.", civ.Id, u.X, u.Y));
        }
        if (tile.Village)
        {
            tile.Village = false;
            string text;
            switch (s.Rng.Int(5))
            {
                case 0:
                    int gold = 40 + s.Rng.Int(41);
                    civ.Gold += gold; text = $"The villagers share their treasure: +{gold} gold.";
                    break;
                case 1:
                    double sci = civ.Researching != null && content.Tech.TryGetValue(civ.Researching, out var t) ? Math.Round(t.Cost * 0.5) : 30;
                    civ.ResearchProgress += sci; text = $"The village elders share their knowledge: +{sci} science toward your research.";
                    break;
                case 2 when s.CitiesOf(civ.Id).Any():
                    var city = s.CitiesOf(civ.Id).OrderBy(c => Hex.Distance(c.X, c.Y, u.X, u.Y, s.W)).First();
                    city.Pop++; text = $"Villagers settle in {city.Name}: +1 population.";
                    break;
                case 3:
                    var best = content.Units.Where(x => x.Line is "melee" or "recon" && (x.PrereqTech == "none" || civ.Techs.Contains(x.PrereqTech)) && Military.CantTrainBasic(civ, x) == null)
                        .OrderByDescending(x => x.Combat).FirstOrDefault();
                    if (best != null && Military.Spawn(s, content, civ, best.Type, u.X, u.Y) is { } nu) { nu.Moves = 0; text = $"Young warriors join you: a free {content.Text(best.NameLoc)}."; }
                    else { civ.Gold += 50; text = "The villagers share their treasure: +50 gold."; }
                    break;
                default:
                    if (civ.IsPlayer) foreach (var (x, y) in Hex.Within(u.X, u.Y, 6, s.W, s.H)) s.At(x, y).Revealed = true;
                    civ.Gold += 20;
                    text = "The villagers share their maps of the land around, and 20 gold.";
                    break;
            }
            if (civ.IsPlayer) s.Events.Add(new Act1Event(s.Turn, "village", "Tribal village: " + text, civ.Id, u.X, u.Y));
        }
    }
}
