namespace StellarAscension;

/// <summary>
/// Act 1 units and war, Civ VI style: one military unit per tile, movement points and terrain costs, embarking
/// after Shipbuilding, melee and ranged combat with Civ VI's damage curve, city strength and hit points,
/// capturing cities, upgrades along each unit line, and gold upkeep. Pure rules: no drawing, no AI choices.
/// </summary>
public static class Military
{
    public static UnitInfo Info(Content c, UnitState u) => c.Unit[u.Type];
    public static bool IsUnit(string id) => id.StartsWith("UNIT_", StringComparison.Ordinal);
    public static bool IsCivilian(UnitState u) => u.Type == "UNIT_BUILDER";

    /// <summary>Extra strength from the civilization's trait and exotic sites it holds.</summary>
    static double CivStrength(Act1State s, CivState civ) => civ.IsTribe ? 0 : Act1.Trait(civ).UnitStrength + Exotics.Bonus(s, civ, "unit_strength");

    /// <summary>Whether the civ knows the unit's technology (resources aside).</summary>
    public static string? CantTrainBasic(CivState civ, UnitInfo u) => TechOk(civ, u) ? null : "Needs its technology.";

    // ------------------------------------------------------------------ what a civ can train

    static readonly Dictionary<int, (string key, HashSet<string> set)> _resCache = new();

    /// <summary>The strategic resources inside the civ's borders that it can see (Iron after Bronze Working, ...).</summary>
    public static HashSet<string> OwnedResources(Act1State s, Content c, CivState civ)
    {
        string key = $"{s.Turn}:{s.NextCityId}:{s.Cities.Count(x => x.Owner == civ.Id)}:{civ.Techs.Count}:{s.Deals.Count}:{s.GetHashCode()}";
        if (_resCache.TryGetValue(civ.Id, out var hit) && hit.key == key) return hit.set;
        var set = new HashSet<string>();
        foreach (var t in s.Tiles)
            if (t.Resource != null && t.City >= 0 && s.City(t.City)?.Owner == civ.Id && CanSee(c, civ, t.Resource)) set.Add(t.Resource);
        // Resources traded to it.
        foreach (var d in s.Deals.Where(d => d.Kind == "resource" && d.To == civ.Id && d.Until >= s.Turn)) set.Add(d.Resource);
        _resCache[civ.Id] = (key, set);
        return set;
    }

    public static bool CanSee(Content c, CivState civ, string resource) =>
        !c.MapResource.TryGetValue(resource, out var r) || r.PrereqTech == "none" || civ.Techs.Contains(r.PrereqTech);

    static bool TechOk(CivState civ, UnitInfo u) => u.PrereqTech == "none" || civ.Techs.Contains(u.PrereqTech);

    /// <summary>Why a city can't train this unit, or null.</summary>
    public static string? CantTrain(Act1State s, Content c, CityState city, string type)
    {
        var civ = s.Civs[city.Owner];
        if (!c.Unit.TryGetValue(type, out var u)) return "Unknown.";
        if (!TechOk(civ, u)) return $"Needs {c.Name(u.PrereqTech)}.";
        if (u.Resource != "none" && !OwnedResources(s, c, civ).Contains(u.Resource)) return $"Needs {c.Text("LOC_" + u.Resource + "_NAME")} in your territory.";
        // Obsolete once a later unit of its line can be trained.
        var next = u.Upgrade;
        for (int guard = 0; next != "none" && guard < 8 && c.Unit.TryGetValue(next, out var n); guard++, next = n.Upgrade)
            if (TechOk(civ, n) && (n.Resource == "none" || OwnedResources(s, c, civ).Contains(n.Resource))) return $"Replaced by {c.Text(n.NameLoc)}.";
        return null;
    }

    public static double Cost(Content c, string type) => c.Unit[type].Cost;

    // ------------------------------------------------------------------ strength and levels

    public static int Level(UnitState u) => u.Xp >= 90 ? 3 : u.Xp >= 45 ? 2 : u.Xp >= 15 ? 1 : 0;
    public const int LevelStrength = 4;

    /// <summary>City combat strength: the best land unit its owner could train (by tech), plus a base and a capital bonus.</summary>
    public static double CityStrength(Act1State s, Content c, CityState city)
    {
        var civ = s.Civs[city.Owner];
        int best = c.Units.Where(u => TechOk(civ, u) && u.Line is not ("siege" or "civilian")).Select(u => u.Combat).DefaultIfEmpty(20).Max();
        return Rules.CityBaseStrength + best * 0.9 + (city.Capital ? 3 : 0);
    }

    static bool Embarked(Act1State s, UnitState u) => !Defs.Terrains[s.At(u.X, u.Y).Terrain].Land;

    static double Defense(Act1State s, Content c, UnitState d, UnitState? attacker)
    {
        var info = Info(c, d);
        double str = info.Combat + Level(d) * LevelStrength - (100 - d.Hp) / 10 + CivStrength(s, s.Civs[d.Owner]);
        if (Embarked(s, d)) return Math.Max(5, str * 0.4);
        var tile = s.At(d.X, d.Y);
        if (Defs.Terrains[tile.Terrain].Id.EndsWith("_HILLS") || tile.Feature >= 0 && Defs.Features[tile.Feature].Id is "FEATURE_FOREST" or "FEATURE_JUNGLE") str += Rules.TerrainDefenseBonus;
        if (d.Fortified) str += Rules.FortifyBonus;
        if (attacker != null && info.Line == "anti_cavalry" && Info(c, attacker).IsCavalry) str += Rules.AntiCavalryBonus;
        return str;
    }

    static double Offense(Act1State s, Content c, UnitState a, UnitState? defender, bool vsCity)
    {
        var info = Info(c, a);
        double str = info.IsRanged ? info.Ranged : info.Combat;
        if (info.IsRanged && vsCity && !info.Siege) str -= Rules.RangedVsCityPenalty;
        if (info.Siege && !vsCity) str -= 10;
        if (defender != null && info.Line == "anti_cavalry" && Info(c, defender).IsCavalry) str += Rules.AntiCavalryBonus;
        return str + Level(a) * LevelStrength - (100 - a.Hp) / 10 + CivStrength(s, s.Civs[a.Owner]);
    }

    static double Damage(double diff) => Rules.CombatDamage * Math.Exp(0.04 * diff);

    // ------------------------------------------------------------------ movement

    /// <summary>Movement cost to enter a tile (Civ VI: hills and woods +1 each), or -1 if the unit can't go there.</summary>
    public static int EnterCost(Act1State s, CivState civ, int x, int y)
    {
        var tile = s.At(x, y);
        var t = Defs.Terrains[tile.Terrain];
        if (tile.Natural != null) return -1;
        if (!t.Land)
        {
            if (!civ.Techs.Contains("TECH_SHIPBUILDING") && !(Act1.Trait(civ).EmbarkEarly && t.Id == "TERRAIN_COAST")) return -1;
            if (t.Id == "TERRAIN_OCEAN" && !civ.Techs.Contains("TECH_CARTOGRAPHY")) return -1;
            return 1;
        }
        if (!t.Passable) return -1;
        int cost = 1;
        if (t.Id.EndsWith("_HILLS")) cost++;
        if (tile.Feature >= 0 && Defs.Features[tile.Feature].Id is "FEATURE_FOREST" or "FEATURE_JUNGLE" or "FEATURE_MARSH") cost++;
        return cost;
    }

    /// <summary>Whether the unit may stand on (end) or pass through a tile: borders are closed unless at war,
    /// foreign cities and units block, and only one military unit stands on a tile.</summary>
    public static bool CanEnter(Act1State s, UnitState u, int x, int y, bool stop)
    {
        var civ = s.Civs[u.Owner];
        if (EnterCost(s, civ, x, y) < 0) return false;
        if (s.CityAt(x, y) is { } city && city.Owner != u.Owner) return false;
        int owner = s.OwnerAt(x, y);
        if (owner >= 0 && owner != u.Owner && !s.AtWar(owner, u.Owner) && !s.OpenBorders(owner, u.Owner)) return false;
        bool civilian = IsCivilian(u);
        foreach (var other in s.Units)
        {
            if (other.X != x || other.Y != y || other.Id == u.Id) continue;
            if (other.Owner != u.Owner)
            {
                // A lone enemy Builder is captured by a soldier walking in; anything else foreign blocks.
                if (!civilian && IsCivilian(other) && s.AtWar(other.Owner, u.Owner)) continue;
                return false;
            }
            if (stop && IsCivilian(other) == civilian) return false;
        }
        return true;
    }

    /// <summary>Tiles the unit can reach this turn, with the movement it would have left.</summary>
    public static Dictionary<(int x, int y), double> Reachable(Act1State s, UnitState u)
    {
        var best = new Dictionary<(int x, int y), double> { [(u.X, u.Y)] = u.Moves };
        var open = new PriorityQueue<(int x, int y), double>();
        open.Enqueue((u.X, u.Y), -u.Moves);
        var civ = s.Civs[u.Owner];
        while (open.TryDequeue(out var p, out var negLeft))
        {
            double left = -negLeft;
            if (left < best[p] - 1e-9 || left <= 0) continue;
            bool fromWater = !Defs.Terrains[s.At(p.x, p.y).Terrain].Land;
            foreach (var n in Hex.Neighbors(p.x, p.y, s.W, s.H))
            {
                if (!CanEnter(s, u, n.x, n.y, false)) continue;
                bool toWater = !Defs.Terrains[s.At(n.x, n.y).Terrain].Land;
                // Civ V/VI-style: a unit with any movement left may enter; embarking or landing ends its move.
                double after = fromWater != toWater ? 0 : Math.Max(0, left - EnterCost(s, civ, n.x, n.y));
                if (best.TryGetValue(n, out var have) && have >= after) continue;
                best[n] = after;
                if (after > 0) open.Enqueue(n, -after);
            }
        }
        // Tiles it can pass through but not stop on (its own units) are not destinations.
        foreach (var k in best.Keys.ToList())
            if ((k.x != u.X || k.y != u.Y) && !CanEnter(s, u, k.x, k.y, true)) best.Remove(k);
        return best;
    }

    /// <summary>Shortest path (ignoring this turn's movement) to the first tile satisfying the goal, by A*.</summary>
    public static List<(int x, int y)>? PathTo(Act1State s, UnitState u, int tx, int ty, Func<int, int, bool>? goal = null, int maxNodes = 6000)
    {
        goal ??= (x, y) => x == tx && y == ty;
        var civ = s.Civs[u.Owner];
        var g = new Dictionary<(int, int), double> { [(u.X, u.Y)] = 0 };
        var prev = new Dictionary<(int, int), (int, int)>();
        var open = new PriorityQueue<(int x, int y), double>();
        open.Enqueue((u.X, u.Y), 0);
        int nodes = 0;
        while (open.TryDequeue(out var p, out _))
        {
            if ((p.x != u.X || p.y != u.Y) && goal(p.x, p.y) && CanEnter(s, u, p.x, p.y, true))
            {
                var path = new List<(int x, int y)>();
                for (var k = p; k != (u.X, u.Y); k = prev[k]) path.Add(k);
                path.Reverse();
                return path;
            }
            if (++nodes > maxNodes) break;
            foreach (var n in Hex.Neighbors(p.x, p.y, s.W, s.H))
            {
                if (!CanEnter(s, u, n.x, n.y, false)) continue;
                double cost = g[p] + EnterCost(s, civ, n.x, n.y) + (Defs.Terrains[s.At(n.x, n.y).Terrain].Land ? 0 : 0.5);
                if (g.TryGetValue(n, out var have) && have <= cost) continue;
                g[n] = cost;
                prev[n] = p;
                open.Enqueue(n, cost + Hex.Distance(n.x, n.y, tx, ty, s.W));
            }
        }
        return null;
    }

    /// <summary>Moves the unit along a path as far as its movement allows this turn. Returns true if it moved.</summary>
    public static bool Step(Act1State s, UnitState u, List<(int x, int y)> path)
    {
        if (u.Moves <= 0) return false;
        var reach = Reachable(s, u);
        (int x, int y)? last = null;
        foreach (var p in path)
        {
            if (!reach.ContainsKey(p))
            {
                if (s.UnitAt(p.x, p.y) is { } own && own.Owner == u.Owner) continue; // passing through one of its own units
                break;
            }
            last = p;
            if (reach[p] <= 0) break;
        }
        if (last is not { } to) return false;
        MoveTo(s, u, to.x, to.y, reach[to]);
        return true;
    }

    public static void MoveTo(Act1State s, UnitState u, int x, int y, double movesLeft)
    {
        // Walking away from a fight: the enemy gets a parting blow.
        if (u.Engaged >= 0 && s.Unit(u.Engaged) is { } foe && Hex.Distance(x, y, foe.X, foe.Y, s.W) > 1 && Content.Current is { } cc)
        {
            u.Hp -= Damage(Offense(s, cc, foe, u, false) - Defense(s, cc, u, foe)) * Rules.WithdrawHit;
            foe.Engaged = -1; u.Engaged = -1;
            if (u.Hp <= 0) { s.Units.Remove(u); return; }
        }
        u.X = x; u.Y = y;
        u.Moves = movesLeft;
        u.Acted = true;
        u.Fortified = false;
        if (u.TargetX == x && u.TargetY == y) { u.TargetX = u.TargetY = -1; }
        if (Content.Current is { } c) Arrive(s, c, u);
    }

    /// <summary>Things that happen on arrival: capture an enemy Builder, clear a camp, open a village.</summary>
    static void Arrive(Act1State s, Content c, UnitState u)
    {
        if (!IsCivilian(u))
            foreach (var b in s.Units.Where(o => o.X == u.X && o.Y == u.Y && o.Owner != u.Owner && IsCivilian(o)).ToList())
            {
                b.Owner = u.Owner;
                if (s.Civs[u.Owner].IsTribe) s.Units.Remove(b);
                Log(s, $"A {c.Text(Info(c, b).NameLoc)} was captured.", u.Owner, b.Owner, u.X, u.Y);
            }
        var t = s.At(u.X, u.Y);
        if (t.Camp || t.Village) Tribes.Enter(s, c, u);
    }

    /// <summary>Player order: go to a tile (this turn if reachable, else over several turns).</summary>
    public static bool Order(Act1State s, UnitState u, int x, int y)
    {
        var reach = Reachable(s, u);
        if (reach.TryGetValue((x, y), out var left) && (x != u.X || y != u.Y)) { MoveTo(s, u, x, y, left); u.TargetX = u.TargetY = -1; return true; }
        if (!CanEnter(s, u, x, y, true)) return false;
        var path = PathTo(s, u, x, y);
        if (path == null) return false;
        u.TargetX = x; u.TargetY = y;
        Step(s, u, path);
        return true;
    }

    /// <summary>Carries on a multi-turn move. Called at the start of the owner's turn.</summary>
    public static void ContinueMove(Act1State s, UnitState u)
    {
        if (u.TargetX < 0) return;
        var path = PathTo(s, u, u.TargetX, u.TargetY);
        if (path == null) { u.TargetX = u.TargetY = -1; return; }
        Step(s, u, path);
    }

    // ------------------------------------------------------------------ combat

    public sealed record Odds(double ToDefender, double ToAttacker, bool Kills, bool Captures, string Target);

    public static bool IsEnemyAt(Act1State s, UnitState u, int x, int y) =>
        s.MilitaryAt(x, y) is { } d && s.AtWar(d.Owner, u.Owner) || s.CityAt(x, y) is { } c && s.AtWar(c.Owner, u.Owner);

    public static bool CanAttack(Act1State s, Content c, UnitState u, int x, int y)
    {
        if (u.Moves <= 0 || u.Attacked || IsCivilian(u) || !IsEnemyAt(s, u, x, y)) return false;
        var info = Info(c, u);
        int d = Hex.Distance(u.X, u.Y, x, y, s.W);
        if (info.IsRanged) return d >= 1 && d <= info.Range && !Embarked(s, u);
        if (Embarked(s, u)) return false;
        return d == 1 && EnterCost(s, s.Civs[u.Owner], x, y) >= 0;
    }

    /// <summary>Expected damage either way (no luck), for the attack preview.</summary>
    public static Odds Preview(Act1State s, Content c, UnitState a, int x, int y)
    {
        var info = Info(c, a);
        if (s.MilitaryAt(x, y) is { } d && s.AtWar(d.Owner, a.Owner) && s.CityAt(x, y) == null)
        {
            double atk = Offense(s, c, a, d, false), def = Defense(s, c, d, a);
            double to = Damage(atk - def), back = info.IsRanged ? 0 : Damage(def - atk);
            return new Odds(to, back, to >= d.Hp, false, c.Text(Info(c, d).NameLoc));
        }
        var city = s.CityAt(x, y)!;
        double a2 = Offense(s, c, a, null, true), cs = CityStrength(s, c, city);
        double toCity = Damage(a2 - cs), backCity = info.IsRanged ? 0 : Damage(cs - a2);
        return new Odds(toCity, backCity, false, !info.IsRanged && toCity >= city.Hp, city.Name);
    }

    static double Luck(Act1State s) => 0.8 + s.Rng.Double() * 0.4;

    /// <summary>Resolves an attack. Returns a line for the event log.</summary>
    public static string Attack(Act1State s, Content c, UnitState a, int x, int y)
    {
        var info = Info(c, a);
        var aName = Act1.CivAdjective(c, s.Civs[a.Owner]) + " " + c.Text(info.NameLoc);
        a.Attacked = true;
        a.Acted = true;
        a.Fortified = false;
        a.Moves = info.IsRanged || !info.IsCavalry ? 0 : Math.Max(0, a.Moves - 1);
        string text;
        int fromX = a.X, fromY = a.Y;
        if (s.MilitaryAt(x, y) is { } d && s.CityAt(x, y) == null)
        {
            double atk = Offense(s, c, a, d, false), def = Defense(s, c, d, a);
            double to = Damage(atk - def) * Luck(s), back = info.IsRanged ? 0 : Damage(def - atk) * Luck(s);
            d.Hp -= to; a.Hp -= back;
            a.Xp += 5; d.Xp += 4;
            string dName = Act1.CivAdjective(c, s.Civs[d.Owner]) + " " + c.Text(Info(c, d).NameLoc);
            text = $"{aName} attacked {dName}: {Math.Round(to)} damage dealt, {Math.Round(back)} taken.";
            bool killed = d.Hp <= 0;
            if (killed)
            {
                s.Units.Remove(d);
                a.Xp += 5;
                text = $"{aName} destroyed {dName}" + (back > 0 ? $" (took {Math.Round(back)} damage)." : ".");
                if (!info.IsRanged && a.Hp > 0 && CanEnter(s, a, x, y, true)) { a.X = x; a.Y = y; Arrive(s, c, a); } // melee advances
            }
            if (a.Hp <= 0) { s.Units.Remove(a); text = $"{aName} was destroyed attacking {dName}."; }
            // Both still standing after a melee clash: they stay locked in battle, a round each turn, until one falls or withdraws.
            if (!info.IsRanged && !killed && a.Hp > 0 && s.Units.Contains(d)) { a.Engaged = d.Id; d.Engaged = a.Id; a.EngageRounds = d.EngageRounds = 0; text += " The battle goes on."; }
            Note(s, a, d.Owner, d.Type, dName, to, back, Math.Max(0, a.Hp), Math.Max(0, d.Hp), killed, false, x, y, fromX, fromY, text);
            Log(s, text, a.Owner, d.Owner, x, y);
            return text;
        }
        var city = s.CityAt(x, y)!;
        double a2 = Offense(s, c, a, null, true), cs = CityStrength(s, c, city);
        double toCity = Damage(a2 - cs) * Luck(s), backCity = info.IsRanged ? 0 : Damage(cs - a2) * Luck(s);
        city.Hp = Math.Max(0, city.Hp - toCity);
        city.LastAttacked = s.Turn;
        a.Hp -= backCity;
        a.Xp += 4;
        text = $"{aName} attacked {city.Name}: {Math.Round(toCity)} damage" + (backCity > 0 ? $", took {Math.Round(backCity)}." : ".");
        int cityOwner = city.Owner;
        bool captured = false;
        if (a.Hp <= 0) { s.Units.Remove(a); text = $"{aName} was destroyed attacking {city.Name}."; }
        else if (!info.IsRanged && city.Hp <= 0)
        {
            text = Capture(s, c, city, a);
            captured = true;
        }
        Note(s, a, cityOwner, "city", city.Name, toCity, backCity, Math.Max(0, a.Hp), city.Hp, false, captured, x, y, fromX, fromY, text);
        Log(s, text, a.Owner, city.Owner, x, y);
        return text;
    }

    static void Note(Act1State s, UnitState a, int defCiv, string defType, string defName, double to, double back, double aHp, double dHp, bool killed, bool captured, int x, int y, int fx, int fy, string text)
    {
        s.Battles.Add(new BattleNote(s.Turn, a.Owner, a.Type, defCiv, defType, defName, to, back, aHp, dHp, killed, captured, x, y, fx, fy, text));
        if (s.Battles.Count > 40) s.Battles.RemoveAt(0);
    }

    /// <summary>Engaged pairs fight another round at the start of each turn.</summary>
    static void EngagementRounds(Act1State s, Content c)
    {
        foreach (var a in s.Units.Where(u => u.Engaged >= 0).ToList())
        {
            if (!s.Units.Contains(a) || a.Engaged < 0) continue;
            var d = s.Unit(a.Engaged);
            if (d == null || d.Engaged != a.Id || Hex.Distance(a.X, a.Y, d.X, d.Y, s.W) != 1 || !s.AtWar(a.Owner, d.Owner) || ++a.EngageRounds > Rules.Act1EngageRounds)
            {
                a.Engaged = -1; if (d != null && d.Engaged == a.Id) d.Engaged = -1;
                continue;
            }
            d.EngageRounds = a.EngageRounds;
            double sa = Offense(s, c, a, d, false), sd = Defense(s, c, d, a);
            double toD = Damage(sa - sd) * 0.6 * Luck(s), toA = Damage(sd - sa) * 0.6 * Luck(s);
            d.Hp -= toD; a.Hp -= toA;
            a.Acted = d.Acted = true;
            string an = Act1.CivAdjective(c, s.Civs[a.Owner]) + " " + c.Text(Info(c, a).NameLoc), dn = Act1.CivAdjective(c, s.Civs[d.Owner]) + " " + c.Text(Info(c, d).NameLoc);
            string text = $"The battle between {an} and {dn} goes on: {Math.Round(toD)} and {Math.Round(toA)} damage.";
            if (d.Hp <= 0) { s.Units.Remove(d); a.Xp += 6; a.Engaged = -1; text = $"{an} won its battle with {dn}."; }
            if (a.Hp <= 0) { s.Units.Remove(a); d.Xp += 6; d.Engaged = -1; text = $"{dn} won its battle with {an}."; }
            Note(s, a, d.Owner, d.Type, dn, toD, toA, Math.Max(0, a.Hp), Math.Max(0, d.Hp), d.Hp <= 0, false, d.X, d.Y, a.X, a.Y, text);
            Log(s, text, a.Owner, d.Owner, d.X, d.Y);
        }
    }

    /// <summary>Pulls an engaged unit back to the safest neighbouring tile; the enemy gets a parting blow.</summary>
    public static bool Withdraw(Act1State s, Content c, UnitState u)
    {
        if (u.Engaged < 0 || s.Unit(u.Engaged) is not { } foe) return false;
        var spot = Hex.Neighbors(u.X, u.Y, s.W, s.H).Where(t => CanEnter(s, u, t.x, t.y, true) && Hex.Distance(t.x, t.y, foe.X, foe.Y, s.W) >= 2)
            .OrderByDescending(t => s.OwnerAt(t.x, t.y) == u.Owner).ThenByDescending(t => Hex.Distance(t.x, t.y, foe.X, foe.Y, s.W)).Select(t => ((int, int)?)t).FirstOrDefault();
        if (spot is not { } to) return false;
        double hit = Damage(Offense(s, c, foe, u, false) - Defense(s, c, u, foe)) * Rules.WithdrawHit * Luck(s);
        u.Hp -= hit;
        u.Engaged = -1; foe.Engaged = -1;
        if (u.Hp <= 0) { s.Units.Remove(u); return true; }
        (u.X, u.Y) = to;
        u.Moves = 0; u.Acted = true; u.Fortified = false; u.TargetX = u.TargetY = -1;
        Log(s, $"{c.Text(Info(c, u).NameLoc)} withdrew from battle (took {Math.Round(hit)} damage).", u.Owner, foe.Owner, to.Item1, to.Item2);
        return true;
    }

    static void Log(Act1State s, string text, int a, int b, int x, int y)
    {
        if (s.Civs[a].IsPlayer || s.Civs[b].IsPlayer) s.Events.Add(new Act1Event(s.Turn, "combat", text, s.Civs[a].IsPlayer ? a : b, x, y));
    }

    /// <summary>A melee unit takes a city with no hit points left: it changes hands with its buildings and wonders.</summary>
    public static string Capture(Act1State s, Content c, CityState city, UnitState by)
    {
        var loser = s.Civs[city.Owner];
        var winner = s.Civs[by.Owner];
        if (s.UnitAt(city.X, city.Y) is { } garrison) s.Units.Remove(garrison);
        by.X = city.X; by.Y = city.Y;
        bool wasCapital = city.Capital;
        city.Owner = winner.Id;
        city.Capital = false;
        city.Pop = Math.Max(1, city.Pop - Math.Max(1, city.Pop / 4));
        city.Hp = Rules.CityMaxHp * 0.25;
        city.Build = null;
        city.Progress.Clear();
        city.Carry = 0;
        foreach (var t in s.Tiles.Where(t => t.City == city.Id)) { } // the territory comes with it (tiles keep the city id)
        string text = $"{Act1.CivName(c, winner)} captured {city.Name} from {Act1.CivName(c, loser)}.";
        s.Events.Add(new Act1Event(s.Turn, "captured", text, winner.IsPlayer ? winner.Id : loser.Id, city.X, city.Y));
        // A civ that lost its capital moves it to its largest city; one with no cities left is gone.
        var rest = s.CitiesOf(loser.Id).ToList();
        if (rest.Count == 0) Eliminate(s, c, loser, winner);
        else if (wasCapital || !rest.Any(x => x.Capital)) rest.OrderByDescending(x => x.Pop).First().Capital = true;
        if (!s.CitiesOf(winner.Id).Any(x => x.Capital)) city.Capital = true;
        return text;
    }

    static void Eliminate(Act1State s, Content c, CivState civ, CivState by)
    {
        civ.Alive = false;
        civ.Researching = null;
        civ.PendingSettlers = 0;
        s.Units.RemoveAll(u => u.Owner == civ.Id);
        s.Wars.RemoveAll(w => w.A == civ.Id || w.B == civ.Id);
        s.Events.Add(new Act1Event(s.Turn, "eliminated", $"{Act1.CivName(c, civ)} has been destroyed by {Act1.CivName(c, by)}.", civ.IsPlayer ? civ.Id : by.Id));
        if (civ.IsPlayer) s.Outcome = "conquered";
    }

    // ------------------------------------------------------------------ the turn

    /// <summary>Cities shoot at an adjacent enemy unit (strongest threat first), as Civ VI's walled cities do.</summary>
    public static void CityStrikes(Act1State s, Content c, CivState civ)
    {
        foreach (var city in s.CitiesOf(civ.Id).Where(x => x.Hp > 0).ToList())
        {
            var target = s.Units.Where(u => s.AtWar(u.Owner, civ.Id) && Hex.Distance(u.X, u.Y, city.X, city.Y, s.W) <= 2)
                .OrderBy(u => u.Hp).FirstOrDefault();
            if (target == null) continue;
            double dmg = Damage(CityStrength(s, c, city) - 5 - Defense(s, c, target, null)) * Luck(s);
            target.Hp -= dmg;
            string name = Act1.CivAdjective(c, s.Civs[target.Owner]) + " " + c.Text(Info(c, target).NameLoc);
            string text = target.Hp <= 0 ? $"{city.Name}'s defenders destroyed a {name}." : $"{city.Name} fired on a {name} ({Math.Round(dmg)} damage).";
            if (target.Hp <= 0) s.Units.Remove(target);
            Log(s, text, civ.Id, target.Owner, target.X, target.Y);
        }
    }

    /// <summary>End of a round: units get their movement back and heal if they rested; cities heal; upkeep is paid.</summary>
    public static void NewTurn(Act1State s, Content c)
    {
        EngagementRounds(s, c);
        foreach (var u in s.Units.ToList())
        {
            var civ = s.Civs[u.Owner];
            if (!u.Acted && u.Hp < 100)
            {
                bool home = s.OwnerAt(u.X, u.Y) == u.Owner;
                double heal = home ? Rules.UnitHealOwn + (s.CityAt(u.X, u.Y) != null ? 10 : 0) : Rules.UnitHealOther;
                u.Hp = Math.Min(100, u.Hp + heal);
            }
            u.Moves = Info(c, u).Moves;
            u.Attacked = false;
            u.Acted = false;
            u.Skipped = false;
        }
        foreach (var city in s.Cities)
            if (city.LastAttacked < s.Turn - 1 || city.LastAttacked < s.Turn && city.Hp < Rules.CityMaxHp * 0.5)
                city.Hp = Math.Min(Rules.CityMaxHp, city.Hp + Rules.CityHeal);
        foreach (var civ in s.Majors.Where(x => x.Alive))
        {
            double upkeep = Upkeep(s, c, civ);
            civ.Gold -= upkeep;
            // Can't pay: units are disbanded, cheapest first, as in Civ VI.
            while (civ.Gold < 0 && s.UnitsOf(civ.Id).Any())
            {
                var u = s.UnitsOf(civ.Id).OrderBy(x => Info(c, x).Cost).First();
                s.Units.Remove(u);
                civ.Gold += Info(c, u).Maintenance * Rules.UnitMaintenanceMult;
                if (civ.IsPlayer) s.Events.Add(new Act1Event(s.Turn, "disband", $"Out of gold: a {c.Text(Info(c, u).NameLoc)} was disbanded.", civ.Id, u.X, u.Y));
            }
            civ.Gold = Math.Max(0, civ.Gold);
        }
    }

    public static double Upkeep(Act1State s, Content c, CivState civ) => s.UnitsOf(civ.Id).Sum(u => Info(c, u).Maintenance) * Rules.UnitMaintenanceMult;

    /// <summary>Puts a newly trained unit on the city, or the nearest free tile around it.</summary>
    public static UnitState? Spawn(Act1State s, Content c, CivState civ, string type, int x, int y)
    {
        var u = new UnitState { Id = s.NextUnitId++, Owner = civ.Id, Type = type, X = x, Y = y, Moves = 0 };
        if (!CanEnter(s, u, x, y, true))
        {
            var spot = Hex.Within(x, y, 3, s.W, s.H).Where(t => Defs.Terrains[s.At(t.x, t.y).Terrain].Land && CanEnter(s, u, t.x, t.y, true))
                .OrderBy(t => Hex.Distance(x, y, t.x, t.y, s.W)).Select(t => ((int, int)?)t).FirstOrDefault();
            if (spot is not { } p) return null;
            (u.X, u.Y) = p;
        }
        s.Units.Add(u);
        return u;
    }

    // ------------------------------------------------------------------ upgrades

    /// <summary>The unit this one can upgrade to now (tech and resource in hand), or null.</summary>
    public static UnitInfo? UpgradeTarget(Act1State s, Content c, UnitState u)
    {
        var civ = s.Civs[u.Owner];
        UnitInfo? best = null;
        var next = Info(c, u).Upgrade;
        for (int guard = 0; next != "none" && guard < 8 && c.Unit.TryGetValue(next, out var n); guard++, next = n.Upgrade)
            if (TechOk(civ, n) && (n.Resource == "none" || OwnedResources(s, c, civ).Contains(n.Resource))) best = n;
        return best;
    }

    public static double UpgradeCost(Content c, UnitState u, UnitInfo to) =>
        Math.Ceiling(Math.Max(10, to.Cost - Info(c, u).Cost) * Rules.UpgradeGoldPerProduction);

    /// <summary>Why the unit can't upgrade now, or null.</summary>
    public static string? CantUpgrade(Act1State s, Content c, UnitState u)
    {
        var to = UpgradeTarget(s, c, u);
        if (to == null) return "Nothing to upgrade to yet.";
        if (s.OwnerAt(u.X, u.Y) != u.Owner) return "Only inside your borders.";
        if (u.Moves <= 0 || u.Attacked) return "No moves left this turn.";
        if (s.Civs[u.Owner].Gold < UpgradeCost(c, u, to)) return $"Needs {UpgradeCost(c, u, to)} gold.";
        return null;
    }

    public static bool Upgrade(Act1State s, Content c, UnitState u)
    {
        if (CantUpgrade(s, c, u) != null) return false;
        var to = UpgradeTarget(s, c, u)!;
        s.Civs[u.Owner].Gold -= UpgradeCost(c, u, to);
        u.Type = to.Type;
        u.Moves = 0;
        u.Acted = true;
        return true;
    }

    // ------------------------------------------------------------------ war and peace

    public static void DeclareWar(Act1State s, Content c, CivState a, CivState b)
    {
        if (s.AtWar(a.Id, b.Id) || !a.Alive || !b.Alive || a.Id == b.Id || a.IsTribe || b.IsTribe) return;
        s.Wars.Add(new WarState { A = a.Id, B = b.Id, Since = s.Turn });
        s.Events.Add(new Act1Event(s.Turn, "war", $"{Act1.CivName(c, a)} declared war on {Act1.CivName(c, b)}.", a.IsPlayer ? a.Id : b.Id));
        // Breaking an alliance ends it; the defender's allies join the war.
        s.Alliances.RemoveAll(x => x[0] == Math.Min(a.Id, b.Id) && x[1] == Math.Max(a.Id, b.Id));
        s.Deals.RemoveAll(d => d.From == a.Id && d.To == b.Id || d.From == b.Id && d.To == a.Id);
        foreach (var ally in s.Majors.Where(o => o.Alive && o.Id != a.Id && o.Id != b.Id && s.Allied(o.Id, b.Id) && !s.AtWar(o.Id, a.Id)).ToList())
        {
            s.Wars.Add(new WarState { A = ally.Id, B = a.Id, Since = s.Turn });
            s.Events.Add(new Act1Event(s.Turn, "war", $"{Act1.CivName(c, ally)} joins the war on {Act1.CivName(c, a)}, honouring its alliance.", ally.IsPlayer || a.IsPlayer ? (ally.IsPlayer ? ally.Id : a.Id) : b.Id));
        }
    }

    public static double ArmyPower(Act1State s, Content c, int civ) =>
        s.UnitsOf(civ).Where(u => !IsCivilian(u)).Sum(u => Math.Pow(Math.Max(1, Info(c, u).IsRanged ? Info(c, u).Ranged : Info(c, u).Combat), 1.5) * u.Hp / 100);

    /// <summary>Whether the AI side would accept peace now.</summary>
    public static bool WouldAcceptPeace(Act1State s, Content c, CivState ai, CivState other)
    {
        if (ai.IsTribe || other.IsTribe) return false;
        var w = s.Wars.FirstOrDefault(w => w.A == ai.Id && w.B == other.Id || w.A == other.Id && w.B == ai.Id);
        if (w == null) return false;
        int len = s.Turn - w.Since;
        if (len < Rules.WarMinTurns) return false;
        double mine = ArmyPower(s, c, ai.Id), theirs = ArmyPower(s, c, other.Id);
        return len > 40 || mine < theirs * 1.3;
    }

    public static void MakePeace(Act1State s, Content c, CivState a, CivState b)
    {
        if (s.Wars.RemoveAll(w => w.A == a.Id && w.B == b.Id || w.A == b.Id && w.B == a.Id) == 0) return;
        s.Events.Add(new Act1Event(s.Turn, "peace", $"{Act1.CivName(c, a)} and {Act1.CivName(c, b)} made peace.", a.IsPlayer ? a.Id : b.Id));
        // Units standing in the other's territory go home (borders close again).
        foreach (var u in s.Units.Where(u => (u.Owner == a.Id || u.Owner == b.Id) && s.OwnerAt(u.X, u.Y) is var o && o >= 0 && o != u.Owner).ToList())
        {
            var home = s.CitiesOf(u.Owner).OrderBy(x => Hex.Distance(x.X, x.Y, u.X, u.Y, s.W)).FirstOrDefault();
            var spot = home == null ? null : Hex.Within(home.X, home.Y, 3, s.W, s.H).Where(t => (s.UnitAt(t.x, t.y) == null) && s.OwnerAt(t.x, t.y) == u.Owner && EnterCost(s, s.Civs[u.Owner], t.x, t.y) >= 0)
                .OrderBy(t => Hex.Distance(home.X, home.Y, t.x, t.y, s.W)).Select(t => ((int, int)?)t).FirstOrDefault();
            if (spot is { } p) (u.X, u.Y) = p; else s.Units.Remove(u);
            u.TargetX = u.TargetY = -1;
        }
    }
}
