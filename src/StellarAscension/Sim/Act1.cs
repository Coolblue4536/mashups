namespace StellarAscension;

public readonly record struct Yields(double Food, double Production, double Gold, double Science)
{
    public static Yields operator +(Yields a, Yields b) => new(a.Food + b.Food, a.Production + b.Production, a.Gold + b.Gold, a.Science + b.Science);
}

/// <summary>A city's yields this turn, with the numbers the city panel shows.</summary>
public sealed record CityReport(Yields Total, double FoodEaten, double FoodSurplus, double GrowthNeeded, int TurnsToGrow,
    List<(int x, int y)> Worked, int TurnsToBuild, double BuildCost);

/// <summary>Act 1 rules: the Civ VI part. Everything is a pure function of the state and the content.</summary>
public static class Act1
{
    public const string Settler = "settler";

    /// <summary>The Civ VI resources (set once the content is read), for tile yields.</summary>
    public static Dictionary<string, MapResource> ResourceDefs = new();

    // ------------------------------------------------------------------ new game

    public static Act1State NewGame(GameSetup setup, Content content)
    {
        var size = Defs.MapSize.TryGetValue(setup.MapSize, out var ms) ? ms : Defs.MapSize["standard"];
        var rng = new Rng(setup.Seed == 0 ? (ulong)Environment.TickCount64 : setup.Seed);
        var s = new Act1State { Setup = setup, W = size.Width, H = size.Height, Rng = rng, Year = (int)Rules.Act1StartYear };
        Act1Map.Generate(s, content, rng, size.NaturalWonders);
        int rivals = Math.Clamp(setup.Rivals, 1, size.MaxRivals);
        var starts = Act1Map.StartPositions(s, rivals + 1, rng);
        var player = content.Civs.FirstOrDefault(c => c.Type == setup.Civ && c.Leader == setup.Leader) ?? content.Civs.First(c => c.Type == setup.Civ || setup.Civ == "");
        var others = content.Civs.Where(c => c.Type != player.Type).GroupBy(c => c.Type).Select(g => g.ToList()).ToList();
        rng.Shuffle(others);
        var diff = Defs.Difficulty.TryGetValue(setup.Difficulty, out var d) ? d : Defs.Difficulty["DIFFICULTY_PRINCE"];
        var picks = new List<Civ6Civ> { player };
        foreach (var g in others.Take(Math.Min(rivals, starts.Count - 1))) picks.Add(rng.Pick(g));
        for (int i = 0; i < picks.Count; i++)
        {
            var c = new CivState { Id = i, CivType = picks[i].Type, Leader = picks[i].Leader, IsPlayer = i == 0, YieldMult = i == 0 ? 1 : diff.AiYieldMult,
                                   Aggression = 0.2 + rng.Double() * 0.6 };
            s.Civs.Add(c);
            FoundCity(s, content, c, starts[i].x, starts[i].y);
        }
        Act1Map.PlaceResources(s, content, rng, starts.Take(s.Civs.Count).ToList());
        // Everyone starts with a Warrior beside the capital, as in Civ VI.
        foreach (var c in s.Civs)
            if (Military.Spawn(s, content, c, "UNIT_WARRIOR", starts[c.Id].x, starts[c.Id].y) is { } w) w.Moves = content.Unit[w.Type].Moves;
        foreach (var c in s.Civs.Where(c => !c.IsPlayer)) Act1Ai.ChooseResearch(s, content, c);
        Reveal(s);
        s.Events.Add(new Act1Event(1, "start", $"{CivName(content, s.Player)} is founded. Win the race to space: complete the Moon Landing first.", 0));
        return s;
    }

    // ------------------------------------------------------------------ names

    public static Civ6Civ? CivDef(Content content, CivState c) => content.Civs.FirstOrDefault(x => x.Type == c.CivType && x.Leader == c.Leader);
    public static string CivName(Content content, CivState c) => CivDef(content, c) is { } d ? content.Text(d.DescLoc) : c.CivType;
    public static string CivAdjective(Content content, CivState c) => CivDef(content, c) is { } d ? content.Text(d.AdjLoc) : c.CivType;
    public static string LeaderName(Content content, CivState c) => CivDef(content, c) is { } d ? content.Text(d.LeaderNameLoc) : c.Leader;

    // ------------------------------------------------------------------ tiles

    /// <summary>A tile's yields; its resource counts once the civ can see it (civ null: the tile's owner, or always if unowned).</summary>
    public static Yields TileYield(Act1State s, int x, int y, CivState? civ = null)
    {
        var tile = s.At(x, y);
        civ ??= tile.City >= 0 && s.City(tile.City) is { } oc ? s.Civs[oc.Owner] : null;
        if (tile.Natural != null)
        {
            var n = Defs.NaturalWonder.TryGetValue(tile.Natural, out var nd) ? nd : Defs.NaturalWonder["DEFAULT"];
            return new Yields(n.Food, n.Production, n.Gold, n.Science);
        }
        var t = Defs.Terrains[tile.Terrain];
        double f = t.Food, p = t.Production, g = t.Gold, sc = t.Science;
        if (tile.Feature >= 0)
        {
            var fe = Defs.Features[tile.Feature];
            f += fe.Food; p += fe.Production; g += fe.Gold; sc += fe.Science;
        }
        if (tile.Resource != null && ResourceDefs.TryGetValue(tile.Resource, out var r) && (civ == null || r.PrereqTech == "none" || civ.Techs.Contains(r.PrereqTech)))
        {
            f += r.Yield.Food; p += r.Yield.Production; g += r.Yield.Gold; sc += r.Yield.Science;
        }
        return new Yields(f, p, g, sc);
    }

    public static bool Workable(Act1State s, int x, int y) => Defs.Terrains[s.At(x, y).Terrain].Passable || s.At(x, y).Natural != null || !Defs.Terrains[s.At(x, y).Terrain].Land;

    public static double TileValue(Act1State s, int x, int y)
    {
        if (!Workable(s, x, y)) return 0;
        var y0 = TileYield(s, x, y);
        return y0.Food * 1.3 + y0.Production * 1.1 + y0.Gold * 0.5 + y0.Science * 0.9;
    }

    public static bool CanSettleTerrain(Act1State s, int x, int y)
    {
        var tile = s.At(x, y);
        var t = Defs.Terrains[tile.Terrain];
        return t.Land && t.Passable && tile.Natural == null;
    }

    public static bool CanFoundCity(Act1State s, CivState civ, int x, int y)
    {
        if (y < 1 || y >= s.H - 1 || !CanSettleTerrain(s, x, y)) return false;
        if (civ.IsPlayer && !s.At(x, y).Revealed) return false;
        if (s.At(x, y).Built != null) return false;
        if (s.UnitAt(x, y) is { } u && u.Owner != civ.Id) return false;
        var owner = s.At(x, y).City;
        if (owner >= 0 && s.City(owner)?.Owner != civ.Id) return false;
        int min = (int)Rules.CityMinDistance;
        return s.Cities.All(c => Hex.Distance(c.X, c.Y, x, y, s.W) >= min);
    }

    public static CityState FoundCity(Act1State s, Content content, CivState civ, int x, int y)
    {
        var def = CivDef(content, civ);
        var names = new List<string>();
        if (def != null)
        {
            if (def.CapitalLoc != null) names.Add(content.Text(def.CapitalLoc));
            names.AddRange(def.CityLocs.Select(l => content.Text(l)).Where(n => !names.Contains(n)));
        }
        string name;
        do
        {
            name = civ.NextCityName < names.Count ? names[civ.NextCityName] : $"{CivAdjective(content, civ)} City {civ.NextCityName + 1}";
            civ.NextCityName++;
        } while (s.Cities.Any(c => c.Name == name));
        var city = new CityState { Id = s.NextCityId++, Owner = civ.Id, X = x, Y = y, Name = name, Capital = !s.CitiesOf(civ.Id).Any(), Founded = s.Turn,
                                   Founder = civ.Id, Hp = Rules.CityMaxHp };
        s.Cities.Add(city);
        foreach (var (tx, ty) in Hex.Within(x, y, (int)Rules.CityRadius, s.W, s.H))
            if (s.At(tx, ty).City < 0) s.At(tx, ty).City = city.Id;
        s.At(x, y).City = city.Id;
        foreach (var (tx, ty) in Hex.Within(x, y, (int)Rules.CityRadius, s.W, s.H))
            if (s.At(tx, ty).Natural is { } nw && s.At(tx, ty).City == city.Id)
                s.Events.Add(new Act1Event(s.Turn, "natural", $"{city.Name} claims {content.Name(nw)}.", civ.Id, tx, ty));
        return city;
    }

    // ------------------------------------------------------------------ yields

    public static double GrowthNeeded(int pop) =>
        Rules.GrowthBase + Rules.GrowthStep * (pop - 1) + Math.Floor(Math.Pow(pop - 1, Rules.GrowthExponent));

    static IEnumerable<(string id, int food, int prod, int gold, int sci, int foodPct, int prodPct, int goldPct, int sciPct)> Effects(Act1State s, CityState city)
    {
        foreach (var b in city.Buildings)
            if (Defs.Building.TryGetValue(b, out var d))
                yield return (b, d.Food, d.Production, d.Gold, d.Science, d.FoodPct, d.ProductionPct, d.GoldPct, d.SciencePct);
        foreach (var w in city.Wonders)
            if (Defs.Wonder.TryGetValue(w, out var d) && d.Scope == "city")
                yield return (w, d.Food, d.Production, d.Gold, d.Science, d.FoodPct, d.ProductionPct, d.GoldPct, d.SciencePct);
        foreach (var other in s.CitiesOf(city.Owner))
            foreach (var w in other.Wonders)
                if (Defs.Wonder.TryGetValue(w, out var d) && d.Scope == "empire")
                    yield return (w, d.Food, d.Production, d.Gold, d.Science, d.FoodPct, d.ProductionPct, d.GoldPct, d.SciencePct);
    }

    /// <summary>The tiles the city works: its own tile plus its best tiles, one per citizen (favouring food while it's short).</summary>
    public static List<(int x, int y)> WorkTiles(Act1State s, CityState city)
    {
        var owned = Hex.Within(city.X, city.Y, (int)Rules.CityRadius, s.W, s.H)
            .Where(t => s.At(t.x, t.y).City == city.Id && !(t.x == city.X && t.y == city.Y) && s.At(t.x, t.y).Built == null && Workable(s, t.x, t.y)).ToList();
        var worked = new List<(int x, int y)>();
        double food = Math.Max(Rules.CityCenterMinFood, TileYield(s, city.X, city.Y).Food);
        for (int i = 0; i < city.Pop && owned.Count > 0; i++)
        {
            bool hungry = food - (worked.Count + 1) * Rules.FoodPerPop < 2;
            var best = owned.OrderByDescending(t =>
            {
                var y0 = TileYield(s, t.x, t.y);
                return y0.Food * (hungry ? 2.2 : 1.1) + y0.Production * 1.2 + y0.Gold * 0.5 + y0.Science * 0.9;
            }).ThenBy(t => t.y * s.W + t.x).First();
            owned.Remove(best);
            worked.Add(best);
            food += TileYield(s, best.x, best.y).Food;
        }
        return worked;
    }

    public static CityReport Report(Act1State s, Content content, CityState city)
    {
        var civ = s.Civs[city.Owner];
        var worked = WorkTiles(s, city);
        var center = TileYield(s, city.X, city.Y);
        double f = Math.Max(Rules.CityCenterMinFood, center.Food), p = Math.Max(Rules.CityCenterMinProduction, center.Production),
               g = center.Gold, sc = center.Science + Rules.CityCenterScience;
        foreach (var (x, y) in worked)
        {
            var t = TileYield(s, x, y);
            f += t.Food; p += t.Production; g += t.Gold; sc += t.Science;
        }
        sc += city.Pop * Rules.SciencePerPop;
        g += city.Pop * Rules.GoldPerPop;
        if (city.Capital) { g += Rules.CapitalBonusGold; sc += Rules.CapitalBonusScience; }
        double fp = 0, pp = 0, gp = 0, sp = 0;
        foreach (var e in Effects(s, city))
        {
            f += e.food; p += e.prod; g += e.gold; sc += e.sci;
            fp += e.foodPct; pp += e.prodPct; gp += e.goldPct; sp += e.sciPct;
        }
        double m = civ.YieldMult;
        f *= (1 + fp / 100) * (1 + (m - 1) * 0.5);
        p *= (1 + pp / 100) * m;
        g *= (1 + gp / 100) * m;
        sc *= (1 + sp / 100) * m;
        double eaten = city.Pop * Rules.FoodPerPop, surplus = f - eaten, need = GrowthNeeded(city.Pop);
        int turnsToGrow = surplus > 0 ? (int)Math.Ceiling(Math.Max(0, need - city.Food) / surplus) : -1;
        double cost = city.Build != null ? BuildCost(s, content, civ, city.Build) : 0;
        int turnsToBuild = city.Build != null && p > 0 ? (int)Math.Ceiling(Math.Max(0, cost - Progress(city, city.Build)) / p) : -1;
        return new CityReport(new Yields(f, p, g, sc), eaten, surplus, need, turnsToGrow, worked, Math.Max(turnsToBuild, city.Build != null ? 1 : -1), cost);
    }

    static double Progress(CityState c, string id) => c.Progress.TryGetValue(id, out var v) ? v : 0;

    public static Yields CivYields(Act1State s, Content content, CivState civ)
    {
        var total = new Yields(0, 0, 0, 0);
        foreach (var c in s.CitiesOf(civ.Id)) total += Report(s, content, c).Total;
        return total;
    }

    // ------------------------------------------------------------------ research

    public static bool CanResearch(Content content, CivState civ, string tech) =>
        content.Tech.TryGetValue(tech, out var t) && !civ.Techs.Contains(tech) && t.Prereqs.All(civ.Techs.Contains);

    public static IEnumerable<TechInfo> Researchable(Content content, CivState civ) => content.Techs.Where(t => CanResearch(content, civ, t.Type));

    public static int EraIndex(Content content, CivState civ) =>
        civ.Techs.Count == 0 ? 0 : civ.Techs.Max(t => content.Tech.TryGetValue(t, out var ti) ? ti.EraIndex : 0);

    // ------------------------------------------------------------------ builds

    public static double BuildCost(Act1State s, Content content, CivState civ, string id) => id switch
    {
        Settler => Math.Round((Rules.SettlerCost + Rules.SettlerCostStep * civ.SettlersBuilt) * Rules.Act1BuildCostMult),
        _ when content.Unit.TryGetValue(id, out var u) => u.Cost,
        _ when content.BuildingCost.TryGetValue(id, out var b) => b,
        _ when content.WonderCost.TryGetValue(id, out var w) => w,
        _ when content.ProjectCost.TryGetValue(id, out var p) => p,
        _ => 9999,
    };

    public static bool IsCoastal(Act1State s, CityState c) =>
        Hex.Neighbors(c.X, c.Y, s.W, s.H).Any(n => !Defs.Terrains[s.At(n.x, n.y).Terrain].Land);

    public static string Kind(string id) =>
        id == Settler || Military.IsUnit(id) ? "unit" : Defs.Wonder.ContainsKey(id) ? "wonder" : Defs.Project.ContainsKey(id) ? "project" : "building";

    /// <summary>Why a build isn't possible right now, or null when it is.</summary>
    public static string? CantBuild(Act1State s, Content content, CityState city, string id)
    {
        var civ = s.Civs[city.Owner];
        if (id == Settler)
        {
            if (city.Pop < Rules.SettlerMinPop) return $"Needs a size {Rules.SettlerMinPop} city.";
            return null;
        }
        if (Military.IsUnit(id)) return Military.CantTrain(s, content, city, id);
        if (Defs.Building.TryGetValue(id, out var b))
        {
            if (city.Buildings.Contains(id)) return "Already built here.";
            if (b.RequiresTech != "none" && !civ.Techs.Contains(b.RequiresTech)) return $"Needs {content.Name(b.RequiresTech)}.";
            if (b.RequiresBuilding != "none" && !city.Buildings.Contains(b.RequiresBuilding)) return $"Needs a {content.Name(b.RequiresBuilding)} first.";
            if (b.Coastal && !IsCoastal(s, city)) return "Coastal cities only.";
            return null;
        }
        if (Defs.Wonder.TryGetValue(id, out var w))
        {
            if (s.WonderOwner.ContainsKey(id)) return "Already built in the world.";
            if (!civ.Techs.Contains(w.RequiresTech)) return $"Needs {content.Name(w.RequiresTech)}.";
            if (s.CitiesOf(civ.Id).Any(c => c.Id != city.Id && c.Build == id)) return "Already being built in another of your cities.";
            return null;
        }
        if (Defs.Project.TryGetValue(id, out var p))
        {
            if (!civ.Techs.Contains(p.RequiresTech)) return $"Needs {content.Name(p.RequiresTech)}.";
            if (!city.Buildings.Contains(p.RequiresBuilding)) return $"Needs a {content.Name(p.RequiresBuilding)} in this city.";
            if (p.Kind == "satellite" && civ.SatelliteDone) return "Already launched.";
            if (p.Kind == "moon" && !civ.SatelliteDone) return $"Launch the {content.Name("PROJECT_LAUNCH_EARTH_SATELLITE")} first.";
            if (p.Kind == "moon" && civ.MoonTurn > 0) return "Already done.";
            if (s.CitiesOf(civ.Id).Any(c => c.Id != city.Id && c.Build == id)) return "Already being built in another of your cities.";
            return null;
        }
        return "Unknown.";
    }

    public static IEnumerable<string> AllBuilds(Content content) =>
        new[] { Settler }.Concat(content.Units.Select(u => u.Type)).Concat(Defs.Buildings.Select(b => b.Id)).Concat(Defs.Wonders.Select(w => w.Id)).Concat(Defs.Projects.Select(p => p.Id));

    public static List<string> AvailableBuilds(Act1State s, Content content, CityState city) =>
        AllBuilds(content).Where(id => CantBuild(s, content, city, id) == null).ToList();

    public static bool CanBuy(string id) => Kind(id) is "unit" or "building";

    public static double BuyCost(Act1State s, Content content, CityState city)
    {
        if (city.Build == null || !CanBuy(city.Build)) return double.PositiveInfinity;
        var civ = s.Civs[city.Owner];
        return Math.Ceiling(Math.Max(0, BuildCost(s, content, civ, city.Build) - Progress(city, city.Build)) * Rules.BuyGoldPerProduction);
    }

    public static bool Buy(Act1State s, Content content, CityState city)
    {
        var civ = s.Civs[city.Owner];
        double cost = BuyCost(s, content, city);
        if (city.Build == null || double.IsInfinity(cost) || civ.Gold < cost) return false;
        civ.Gold -= cost;
        city.Progress[city.Build] = BuildCost(s, content, civ, city.Build);
        Complete(s, content, city, city.Build);
        return true;
    }

    // ------------------------------------------------------------------ turn

    /// <summary>The calendar follows the most advanced civilization through the eras (sheet: eras.start_year).</summary>
    public static int CalendarYear(Act1State s, Content content)
    {
        double best = 0;
        foreach (var civ in s.Civs.Where(c => c.Alive))
        {
            int era = EraIndex(content, civ);
            var inEra = content.Techs.Where(t => t.EraIndex == era).ToList();
            double frac = inEra.Count == 0 ? 0 : inEra.Count(t => civ.Techs.Contains(t.Type)) / (double)inEra.Count;
            best = Math.Max(best, era + frac);
        }
        int e = Math.Min((int)best, Defs.Eras.Length - 1);
        double start = Defs.Eras[e].StartYear, next = e + 1 < Defs.Eras.Length ? Defs.Eras[e + 1].StartYear : 2030;
        return (int)Math.Round(start + (next - start) * Math.Clamp(best - e, 0, 1));
    }

    public static string YearText(int year) => year < 0 ? $"{-year} BC" : $"{year} AD";

    /// <summary>Ends the player's turn: every civ's cities grow and build, research advances, rivals act.</summary>
    public static void EndTurn(Act1State s, Content content)
    {
        if (s.Outcome != "") return;
        var order = s.Civs.Where(c => c.Alive).ToList();
        s.Rng.Shuffle(order);
        foreach (var civ in order.Where(c => !c.IsPlayer)) { if (civ.Alive) Act1Ai.TakeTurn(s, content, civ); if (s.Outcome != "") return; }
        foreach (var civ in order.Where(c => c.Alive))
        {
            double science = 0, gold = 0;
            foreach (var city in s.CitiesOf(civ.Id).ToList())
            {
                var r = Report(s, content, city);
                science += r.Total.Science;
                gold += r.Total.Gold;
                Grow(s, city, r);
                if (city.Build != null)
                {
                    double add = r.Total.Production + city.Carry;
                    city.Carry = 0;
                    city.Progress[city.Build] = Progress(city, city.Build) + add;
                    if (Progress(city, city.Build) >= BuildCost(s, content, civ, city.Build))
                    {
                        city.Carry = Progress(city, city.Build) - BuildCost(s, content, civ, city.Build);
                        Complete(s, content, city, city.Build);
                    }
                }
            }
            civ.Gold += gold;
            Research(s, content, civ, science);
            if (s.Outcome != "") break;
        }
        foreach (var civ in s.Civs.Where(c => !c.IsPlayer && c.Alive)) Act1Ai.PlaceSettlers(s, content, civ);
        // Cities fire on enemies next to them, then everyone's units get their moves back and heal.
        foreach (var civ in s.Civs.Where(c => c.Alive).ToList()) Military.CityStrikes(s, content, civ);
        Military.NewTurn(s, content);
        foreach (var u in s.UnitsOf(s.Player.Id).Where(u => u.TargetX >= 0).ToList()) Military.ContinueMove(s, u);
        s.Year = Math.Max(s.Year + 1, CalendarYear(s, content));
        Reveal(s);
        s.Turn++;
        if (s.Outcome == "" && (s.Turn > Rules.Act1MaxTurns || s.Year >= Rules.Act1EndYear)) TimeLimit(s, content);
    }

    static void Grow(Act1State s, CityState city, CityReport r)
    {
        city.Food += r.FoodSurplus;
        if (city.Food >= r.GrowthNeeded)
        {
            city.Food -= r.GrowthNeeded;
            city.Pop++;
            if (s.Civs[city.Owner].IsPlayer) s.Events.Add(new Act1Event(s.Turn, "grow", $"{city.Name} grew to size {city.Pop}.", city.Owner, city.X, city.Y));
        }
        else if (city.Food < 0)
        {
            if (city.Pop > 1)
            {
                city.Pop--;
                city.Food = GrowthNeeded(city.Pop) / 2;
                if (s.Civs[city.Owner].IsPlayer) s.Events.Add(new Act1Event(s.Turn, "starve", $"{city.Name} is starving and lost a citizen.", city.Owner, city.X, city.Y));
            }
            else city.Food = 0;
        }
    }

    static void Research(Act1State s, Content content, CivState civ, double science)
    {
        if (civ.Researching == null || !CanResearch(content, civ, civ.Researching))
        {
            civ.Researching = null;
            civ.ResearchProgress += science; // kept for the next choice, as Civ VI does with overflow
            if (!civ.IsPlayer) Act1Ai.ChooseResearch(s, content, civ);
            return;
        }
        civ.ResearchProgress += science;
        var t = content.Tech[civ.Researching];
        if (civ.ResearchProgress >= t.Cost)
        {
            civ.ResearchProgress -= t.Cost;
            civ.Techs.Add(t.Type);
            civ.Researching = null;
            if (civ.IsPlayer)
            {
                var legacy = Defs.TechLegacy.FirstOrDefault(l => l.CivTech == t.Type);
                s.Events.Add(new Act1Event(s.Turn, "tech", $"Researched {content.Name(t.Type)}." + (legacy != null ? $" Space Age legacy: {legacy.Title}." : ""), civ.Id));
            }
            else Act1Ai.ChooseResearch(s, content, civ);
        }
    }

    public static void Complete(Act1State s, Content content, CityState city, string id)
    {
        var civ = s.Civs[city.Owner];
        city.Progress.Remove(id);
        city.Build = null;
        switch (Kind(id))
        {
            case "unit" when id != Settler:
                var u = Military.Spawn(s, content, civ, id, city.X, city.Y);
                string un = content.Text(content.Unit[id].NameLoc);
                if (civ.IsPlayer) s.Events.Add(new Act1Event(s.Turn, "trained", u != null ? $"{city.Name} trained a {un}." : $"{city.Name} trained a {un}, but there was no room for it.", civ.Id, city.X, city.Y));
                break;
            case "unit":
                civ.SettlersBuilt++;
                civ.PendingSettlers++;
                city.Pop = Math.Max(1, city.Pop - 1);
                if (civ.IsPlayer) s.Events.Add(new Act1Event(s.Turn, "settler", $"{city.Name} trained a Settler. Choose where to found the new city.", civ.Id, city.X, city.Y));
                break;
            case "building":
                city.Buildings.Add(id);
                if (Defs.Building[id].District != "none") PlaceStructure(s, city, "district:" + Defs.Building[id].District, Defs.Building[id].District == "Harbor");
                if (civ.IsPlayer) s.Events.Add(new Act1Event(s.Turn, "built", $"{city.Name} built {content.Name(id)}.", civ.Id, city.X, city.Y));
                break;
            case "wonder":
                city.Wonders.Add(id);
                PlaceStructure(s, city, id, false);
                s.WonderOwner[id] = civ.Id;
                s.Events.Add(new Act1Event(s.Turn, "wonder", $"{CivName(content, civ)} completed {content.Name(id)} in {city.Name}.", civ.Id, city.X, city.Y));
                // Everyone else building it loses it; a share of their progress carries to their next build.
                foreach (var other in s.Cities.Where(c => c.Id != city.Id && (c.Build == id || c.Progress.ContainsKey(id))))
                {
                    other.Carry += Progress(other, id) * Rules.WonderLostRefund;
                    other.Progress.Remove(id);
                    if (other.Build == id) other.Build = null;
                    if (s.Civs[other.Owner].IsPlayer)
                        s.Events.Add(new Act1Event(s.Turn, "wonder_lost", $"{content.Name(id)} was finished elsewhere first. {other.Name} keeps half its progress for its next build.", other.Owner, other.X, other.Y));
                }
                break;
            case "project":
                var p = Defs.Project[id];
                if (p.Kind == "satellite")
                {
                    civ.SatelliteDone = true;
                    s.Events.Add(new Act1Event(s.Turn, "satellite", $"{CivName(content, civ)} launched the {content.Name(id)}.", civ.Id, city.X, city.Y));
                }
                else if (p.Kind == "moon") ReachSpace(s, content, civ, city);
                break;
        }
    }

    static void ReachSpace(Act1State s, Content content, CivState civ, CityState city)
    {
        if (civ.MoonTurn > 0) return;
        civ.MoonTurn = s.Turn;
        s.SpaceOrder.Add(civ.Id);
        if (civ.IsPlayer)
        {
            s.Outcome = "space";
            s.Events.Add(new Act1Event(s.Turn, "space", $"{CivName(content, civ)} completed the Moon Landing. Act 1 is complete: your civilization has reached space.", civ.Id, city.X, city.Y));
            return;
        }
        var race = Defs.SpaceRaceById.TryGetValue(s.Setup.SpaceRace, out var sr) ? sr : Defs.SpaceRaceById["standard"];
        bool first = s.SpaceOrder.Count == 1;
        s.Events.Add(new Act1Event(s.Turn, "rival_space", $"{CivName(content, civ)} completed the Moon Landing" + (first ? " first. They will start Act 2 owning the rare star systems." : "."), civ.Id, city.X, city.Y));
        if (race.DefeatIfBeaten && first)
        {
            s.Outcome = "defeat";
            s.Events.Add(new Act1Event(s.Turn, "defeat", $"{CivName(content, civ)} won the race to space. On Ruthless, only the winner goes to the stars: your game is over.", civ.Id));
        }
    }

    /// <summary>At the turn limit, everyone who hasn't landed is ranked by technologies and Act 1 ends.</summary>
    static void TimeLimit(Act1State s, Content content)
    {
        foreach (var c in s.Civs.Where(c => c.Alive && c.MoonTurn == 0).OrderByDescending(c => c.Techs.Count))
        {
            c.MoonTurn = s.Turn;
            s.SpaceOrder.Add(c.Id);
        }
        var race = Defs.SpaceRaceById.TryGetValue(s.Setup.SpaceRace, out var sr) ? sr : Defs.SpaceRaceById["standard"];
        s.Outcome = race.DefeatIfBeaten && s.SpaceOrder[0] != s.Player.Id ? "defeat" : "space";
        s.Events.Add(new Act1Event(s.Turn, "time", "The age of exploration has run its course: the civilizations go to space in order of their science.", s.Player.Id));
    }

    // ------------------------------------------------------------------ districts and wonders on the map (Civ VI style)

    /// <summary>Puts a district or wonder on a free tile of the city (nearest first); a district is placed once per city.</summary>
    static void PlaceStructure(Act1State s, CityState city, string what, bool water)
    {
        if (what.StartsWith("district:") && Hex.Within(city.X, city.Y, (int)Rules.CityRadius, s.W, s.H).Any(t => s.At(t.x, t.y).City == city.Id && s.At(t.x, t.y).Built == what)) return;
        var spot = Hex.Within(city.X, city.Y, (int)Rules.CityRadius, s.W, s.H)
            .Where(t => !(t.x == city.X && t.y == city.Y))
            .Select(t => (t.x, t.y, tile: s.At(t.x, t.y)))
            .Where(t => t.tile.City == city.Id && t.tile.Built == null && t.tile.Natural == null
                        && (water ? Defs.Terrains[t.tile.Terrain].Id == "TERRAIN_COAST" : Defs.Terrains[t.tile.Terrain].Land && Defs.Terrains[t.tile.Terrain].Passable))
            .OrderBy(t => Hex.Distance(city.X, city.Y, t.x, t.y, s.W)).ThenBy(t => TileValue(s, t.x, t.y)).ThenBy(t => t.y * s.W + t.x)
            .FirstOrDefault();
        if (spot.tile != null) { spot.tile.Built = what; spot.tile.Feature = -1; }
    }

    // ------------------------------------------------------------------ fog of war (the player's view)

    public const int SightRadius = 4;

    /// <summary>Tiles the player sees right now: near their cities and units, and their territory.</summary>
    public static bool Visible(Act1State s, int x, int y)
    {
        var t = s.At(x, y);
        if (t.City >= 0 && s.City(t.City)?.Owner == s.Player.Id) return true;
        return s.CitiesOf(s.Player.Id).Any(c => Hex.Distance(c.X, c.Y, x, y, s.W) <= SightRadius - 1)
            || s.UnitsOf(s.Player.Id).Any(u => Hex.Distance(u.X, u.Y, x, y, s.W) <= UnitSight(u));
    }

    public static int UnitSight(UnitState u) => u.Type is "UNIT_SCOUT" or "UNIT_RANGER" or "UNIT_HELICOPTER" ? 3 : 2;

    /// <summary>Marks what the player has seen: around their cities, the seas after Cartography, everything after Flight.</summary>
    public static void Reveal(Act1State s)
    {
        var p = s.Player;
        bool all = p.Techs.Contains("TECH_FLIGHT"), seas = p.Techs.Contains("TECH_CARTOGRAPHY");
        foreach (var c in s.CitiesOf(p.Id))
            foreach (var (x, y) in Hex.Within(c.X, c.Y, SightRadius + (c.Capital ? 1 : 0), s.W, s.H)) s.At(x, y).Revealed = true;
        foreach (var u in s.UnitsOf(p.Id))
            foreach (var (x, y) in Hex.Within(u.X, u.Y, UnitSight(u), s.W, s.H)) s.At(x, y).Revealed = true;
        if (all || seas)
            for (int i = 0; i < s.Tiles.Length; i++)
                if (all || !Defs.Terrains[s.Tiles[i].Terrain].Land) s.Tiles[i].Revealed = true;
        foreach (var t in s.Tiles.Where(t => t.City >= 0 && s.City(t.City)?.Owner == p.Id)) t.Revealed = true;
    }

    // ------------------------------------------------------------------ player actions

    public static bool PlaceSettler(Act1State s, Content content, CivState civ, int x, int y)
    {
        if (civ.PendingSettlers <= 0 || !CanFoundCity(s, civ, x, y)) return false;
        civ.PendingSettlers--;
        var c = FoundCity(s, content, civ, x, y);
        if (civ.IsPlayer) Reveal(s);
        if (civ.IsPlayer) s.Events.Add(new Act1Event(s.Turn, "city", $"Founded {c.Name}.", civ.Id, x, y));
        return true;
    }

    public static bool SetResearch(Content content, CivState civ, string tech)
    {
        if (!CanResearch(content, civ, tech)) return false;
        civ.Researching = tech;
        return true;
    }

    public static bool SetBuild(Act1State s, Content content, CityState city, string? id)
    {
        if (id != null && CantBuild(s, content, city, id) != null) return false;
        city.Build = id;
        return true;
    }
}
