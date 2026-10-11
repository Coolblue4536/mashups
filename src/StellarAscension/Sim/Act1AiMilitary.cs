namespace StellarAscension;

/// <summary>How Act 1 AIs (and the player's civ in automated runs) use units: a garrison in every city, an army
/// sized to their cities and wars, upgrades when gold allows, war on weaker neighbours and peace when it stops paying.</summary>
public static partial class Act1Ai
{
    static readonly string[] ArmyMix = { "melee", "ranged", "melee", "anti_cavalry", "heavy_cavalry", "ranged", "light_cavalry", "siege" };

    static IEnumerable<UnitState> Army(Act1State s, CivState civ) => s.UnitsOf(civ.Id);

    /// <summary>How many units the civ wants: a garrison per city, more at war or in later eras.</summary>
    public static int WantedUnits(Act1State s, Content c, CivState civ)
    {
        int cities = s.CitiesOf(civ.Id).Count();
        bool war = s.Wars.Any(w => w.A == civ.Id || w.B == civ.Id);
        int era = Act1.EraIndex(c, civ);
        return cities + (war ? cities + 2 : (int)Math.Ceiling(cities * (0.3 + 0.1 * Math.Min(era, 4))));
    }

    /// <summary>The unit a city should train next for a balanced army, or null.</summary>
    static string? BestUnit(Act1State s, Content c, CivState civ, CityState city)
    {
        var trainable = c.Units.Where(u => u.Line != "recon" && Military.CantTrain(s, c, city, u.Type) == null).ToList();
        if (trainable.Count == 0) return null;
        var have = Army(s, civ).GroupBy(u => c.Unit[u.Type].Line).ToDictionary(g => g.Key, g => g.Count());
        bool war = s.Wars.Any(w => w.A == civ.Id || w.B == civ.Id);
        int total = have.Values.Sum();
        foreach (var line in ArmyMix.Concat(new[] { "melee" }))
        {
            if (line == "siege" && !war) continue;
            int want = (int)Math.Ceiling((total + 1) * ArmyMix.Count(l => l == line) / (double)ArmyMix.Length);
            if (have.GetValueOrDefault(line) >= want) continue;
            var best = trainable.Where(u => u.Line == line).OrderByDescending(u => Math.Max(u.Combat, u.Ranged)).FirstOrDefault();
            if (best != null) return best.Type;
        }
        return trainable.OrderByDescending(u => Math.Max(u.Combat, u.Ranged)).First().Type;
    }

    /// <summary>Score for training a unit now (0: don't).</summary>
    static double UnitScore(Act1State s, Content c, CivState civ, CityState city, string type)
    {
        int have = Army(s, civ).Count() + s.CitiesOf(civ.Id).Count(x => x.Build != null && Military.IsUnit(x.Build));
        int want = WantedUnits(s, c, civ);
        if (have >= want) return 0;
        bool war = s.Wars.Any(w => w.A == civ.Id || w.B == civ.Id);
        double cost = c.Unit[type].Cost;
        double gold = Military.Upkeep(s, c, civ);
        if (!war && gold > Act1.CivYields(s, c, civ).Gold * 0.6 + civ.Gold / 40) return 0; // can't afford more upkeep
        return (war ? 6.0 : have < s.CitiesOf(civ.Id).Count() ? 3.0 : 1.2) / Math.Sqrt(cost);
    }

    // ------------------------------------------------------------------ the turn

    public static void Units(Act1State s, Content c, CivState civ)
    {
        if (!civ.IsPlayer) Diplomacy(s, c, civ);
        // Upgrades, oldest first, keeping a reserve for buying.
        foreach (var u in Army(s, civ).OrderBy(u => c.Unit[u.Type].Combat).ToList())
            if (Military.UpgradeTarget(s, c, u) is { } to && civ.Gold - Military.UpgradeCost(c, u, to) > 40) Military.Upgrade(s, c, u);

        var cities = s.CitiesOf(civ.Id).ToList();
        if (cities.Count == 0) return;
        var enemies = s.Civs.Where(o => o.Alive && s.AtWar(o.Id, civ.Id)).Select(o => o.Id).ToHashSet();
        // Garrisons: the unit nearest each empty city goes home.
        var garrisoned = new HashSet<int>();
        foreach (var city in cities)
        {
            var on = s.UnitAt(city.X, city.Y);
            if (on != null && on.Owner == civ.Id) { garrisoned.Add(on.Id); continue; }
            var g = Army(s, civ).Where(u => !garrisoned.Contains(u.Id) && u.Moves > 0 && c.Unit[u.Type].Line != "siege")
                .OrderBy(u => Hex.Distance(u.X, u.Y, city.X, city.Y, s.W)).FirstOrDefault();
            if (g == null || enemies.Count > 0 && Hex.Distance(g.X, g.Y, city.X, city.Y, s.W) > 6) continue;
            garrisoned.Add(g.Id);
            if (Military.PathTo(s, g, city.X, city.Y) is { } path) Military.Step(s, g, path);
        }
        foreach (var u in Army(s, civ).ToList())
        {
            if (!s.Units.Contains(u)) continue;
            var info = c.Unit[u.Type];
            // Shoot or strike whatever is in reach first.
            TryAttack(s, c, u, info);
            if (!s.Units.Contains(u) || u.Moves <= 0) continue;
            if (garrisoned.Contains(u.Id) && s.CityAt(u.X, u.Y) is { } home && home.Owner == civ.Id) { u.Fortified = true; continue; }
            if (u.Hp < 45)
            {
                var safe = cities.OrderBy(x => Hex.Distance(x.X, x.Y, u.X, u.Y, s.W)).First();
                if (Hex.Distance(safe.X, safe.Y, u.X, u.Y, s.W) > 1 && Military.PathTo(s, u, safe.X, safe.Y, (x, y) => Hex.Distance(x, y, safe.X, safe.Y, s.W) <= 1) is { } back) Military.Step(s, u, back);
                else u.Fortified = true;
                continue;
            }
            if (enemies.Count > 0)
            {
                // March on the nearest enemy city: melee next to it, ranged within range.
                var target = s.Cities.Where(x => enemies.Contains(x.Owner)).OrderBy(x => Hex.Distance(x.X, x.Y, u.X, u.Y, s.W)).FirstOrDefault();
                if (target != null && Hex.Distance(target.X, target.Y, u.X, u.Y, s.W) <= 18)
                {
                    int stop = info.IsRanged ? info.Range : 1;
                    if (Hex.Distance(target.X, target.Y, u.X, u.Y, s.W) > stop
                        && Military.PathTo(s, u, target.X, target.Y, (x, y) => Hex.Distance(x, y, target.X, target.Y, s.W) <= stop, 4000) is { } path)
                        Military.Step(s, u, path);
                    if (s.Units.Contains(u)) TryAttack(s, c, u, info);
                    continue;
                }
            }
            // At peace: stand fortified near home.
            if (s.OwnerAt(u.X, u.Y) != civ.Id)
            {
                var near = cities.OrderBy(x => Hex.Distance(x.X, x.Y, u.X, u.Y, s.W)).First();
                if (Military.PathTo(s, u, near.X, near.Y, (x, y) => s.OwnerAt(x, y) == civ.Id) is { } p) Military.Step(s, u, p);
            }
            else u.Fortified = true;
        }
    }

    /// <summary>Attacks the best target in reach if the odds are good.</summary>
    static void TryAttack(Act1State s, Content c, UnitState u, UnitInfo info)
    {
        if (u.Moves <= 0 || u.Attacked) return;
        int reach = info.IsRanged ? info.Range : 1;
        var options = Hex.Within(u.X, u.Y, reach, s.W, s.H).Where(t => Military.CanAttack(s, c, u, t.x, t.y))
            .Select(t => (t, odds: Military.Preview(s, c, u, t.x, t.y))).ToList();
        double Score((int x, int y) t, Military.Odds o)
        {
            if (o.Captures) return 1000;
            if (o.Kills) return 500 + o.ToDefender;
            bool city = s.CityAt(t.x, t.y) != null;
            if (info.IsRanged) return city && !info.Siege ? o.ToDefender * 0.5 : o.ToDefender;
            // Melee: only if it hurts them more than us, and we'd survive comfortably.
            if (o.ToAttacker > u.Hp * 0.6) return -1;
            return o.ToDefender - o.ToAttacker * (city ? 0.8 : 1.1);
        }
        var best = options.Select(x => (x.t, score: Score(x.t, x.odds))).Where(x => x.score > 0).OrderByDescending(x => x.score).FirstOrDefault();
        if (best.score > 0) Military.Attack(s, c, u, best.t.x, best.t.y);
    }

    /// <summary>AIs declare war on clearly weaker neighbours now and then, and make peace when it stops paying.</summary>
    static void Diplomacy(Act1State s, Content c, CivState civ)
    {
        foreach (var w in s.Wars.Where(w => w.A == civ.Id || w.B == civ.Id).ToList())
        {
            var other = s.Civs[w.A == civ.Id ? w.B : w.A];
            if (other.IsPlayer) continue; // the player asks for peace themselves
            if (Military.WouldAcceptPeace(s, c, civ, other) && Military.WouldAcceptPeace(s, c, other, civ) && s.Rng.Chance(0.25)) Military.MakePeace(s, c, civ, other);
        }
        if (s.Turn < Rules.AiWarMinTurn || s.Wars.Any(w => w.A == civ.Id || w.B == civ.Id)) return;
        if (Act1.EraIndex(c, civ) >= 6 && civ.SatelliteDone) return; // racing to the Moon now
        // Harder difficulties make AIs readier for war; on the easiest they rarely start one.
        double mood = s.Setup.Difficulty switch
        {
            "DIFFICULTY_SETTLER" => 0.25, "DIFFICULTY_CHIEFTAIN" => 0.45, "DIFFICULTY_WARLORD" => 0.7, "DIFFICULTY_PRINCE" => 1.0,
            "DIFFICULTY_KING" => 1.15, "DIFFICULTY_EMPEROR" => 1.3, "DIFFICULTY_IMMORTAL" => 1.45, _ => 1.6,
        };
        if (!s.Rng.Chance(0.02 * civ.Aggression * mood)) return;
        double mine = Military.ArmyPower(s, c, civ.Id);
        var mineCities = s.CitiesOf(civ.Id).ToList();
        var target = s.Civs.Where(o => o.Alive && o.Id != civ.Id)
            .Select(o => (o, dist: s.CitiesOf(o.Id).SelectMany(x => mineCities.Select(m => Hex.Distance(x.X, x.Y, m.X, m.Y, s.W))).DefaultIfEmpty(99).Min()))
            .Where(x => x.dist <= 10 && mine > Military.ArmyPower(s, c, x.o.Id) * Rules.AiWarPowerRatio + 50)
            .OrderBy(x => x.dist).Select(x => x.o).FirstOrDefault();
        if (target != null) Military.DeclareWar(s, c, civ, target);
    }
}
