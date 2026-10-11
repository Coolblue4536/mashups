namespace StellarAscension;

/// <summary>How rival civilizations play Act 1. They follow exactly the player's rules; only their yields are
/// scaled by the difficulty. Also drives the player's civ in automated runs (self-test, balance).</summary>
public static partial class Act1Ai
{
    static HashSet<string>? _spacePath;

    /// <summary>Every tech on the way to the Moon Landing (Satellites and its ancestors).</summary>
    static HashSet<string> SpacePath(Content content)
    {
        if (_spacePath != null) return _spacePath;
        var set = new HashSet<string>();
        var stack = new Stack<string>(Defs.Projects.Select(p => p.RequiresTech).Concat(Defs.Buildings.Where(b => b.Id == "DISTRICT_SPACEPORT").Select(b => b.RequiresTech)));
        while (stack.Count > 0)
        {
            var t = stack.Pop();
            if (!content.Tech.TryGetValue(t, out var ti) || !set.Add(t)) continue;
            foreach (var p in ti.Prereqs) stack.Push(p);
        }
        return _spacePath = set;
    }

    public static void ChooseResearch(Act1State s, Content content, CivState civ)
    {
        var options = Act1.Researchable(content, civ).ToList();
        if (options.Count == 0) { civ.Researching = null; return; }
        var path = SpacePath(content);
        int era = Act1.EraIndex(content, civ);
        civ.Researching = s.Rng.Weighted(options, t =>
            (path.Contains(t.Type) ? (era >= 2 ? 6.0 : 1.8) : 1.0)
            * (Defs.Buildings.Any(b => b.RequiresTech == t.Type) ? 1.4 : 1.0)
            / Math.Pow(t.Cost, 1.3)).Type;
    }

    public static int TargetCities(Act1State s)
    {
        double area = s.W * s.H / (36.0 * 22.0);
        double perCiv = area * (s.Setup.Rivals <= 3 ? 1.0 : 3.5 / Math.Max(3.5, s.Civs.Count - 0.5) * 1.2);
        return Math.Max(3, (int)Math.Round(Rules.AiMaxCitiesSmall * Math.Max(1, perCiv)));
    }

    public static void TakeTurn(Act1State s, Content content, CivState civ)
    {
        if (civ.Researching == null) ChooseResearch(s, content, civ);
        Units(s, content, civ);
        foreach (var city in s.CitiesOf(civ.Id).ToList())
        {
            if (city.Build != null && Act1.CantBuild(s, content, city, city.Build) == null) continue;
            city.Build = ChooseBuild(s, content, civ, city);
        }
        // Spend gold on cheap buildings, keeping a reserve.
        foreach (var city in s.CitiesOf(civ.Id).OrderByDescending(c => c.Pop))
        {
            if (city.Build == null || !Act1.CanBuy(city.Build)) continue;
            double cost = Act1.BuyCost(s, content, city);
            if (civ.Gold - cost > 60) Act1.Buy(s, content, city);
        }
    }

    static double Value(Act1State s, Content content, CityState city, int food, int prod, int gold, int sci, int fp, int pp, int gp, int sp, bool empire)
    {
        var r = Act1.Report(s, content, city).Total;
        double v = food * 1.1 + prod * 1.3 + gold * 0.5 + sci * 1.2
                   + r.Food * fp / 100.0 * 1.1 + r.Production * pp / 100.0 * 1.3 + r.Gold * gp / 100.0 * 0.5 + r.Science * sp / 100.0 * 1.2;
        return empire ? v * Math.Max(1, s.CitiesOf(city.Owner).Count()) : v;
    }

    static string? ChooseBuild(Act1State s, Content content, CivState civ, CityState city)
    {
        var options = Act1.AvailableBuilds(s, content, city);
        if (options.Count == 0) return null;
        var cities = s.CitiesOf(civ.Id).ToList();
        // Under attack: defend first.
        if (s.Units.Any(u => s.AtWar(u.Owner, civ.Id) && Hex.Distance(u.X, u.Y, city.X, city.Y, s.W) <= 3) && BestUnit(s, content, civ, city) is { } defender) return defender;
        // Space first, once it's possible.
        foreach (var p in Defs.Projects.OrderByDescending(p => p.Kind == "moon"))
            if (options.Contains(p.Id)) return p.Id;
        if (options.Contains("DISTRICT_SPACEPORT") && !cities.Any(c => c.Buildings.Contains("DISTRICT_SPACEPORT") || c.Build == "DISTRICT_SPACEPORT"))
        {
            var best = cities.OrderByDescending(c => Act1.Report(s, content, c).Total.Production).First();
            if (best.Id == city.Id) return "DISTRICT_SPACEPORT";
        }
        var scored = new List<(string id, double score)>();
        bool wantSettlers = cities.Count + civ.PendingSettlers + cities.Count(c => c.Build == Act1.Settler) < TargetCities(s)
                            && HasSite(s, civ);
        foreach (var id in options)
        {
            double cost = Act1.BuildCost(s, content, civ, id);
            switch (Act1.Kind(id))
            {
                case "unit" when id == Act1.Settler:
                    if (wantSettlers && city.Pop >= (city.Capital ? 2 : 3)) scored.Add((id, 2.2 / Math.Sqrt(cost)));
                    break;
                case "unit":
                    if (id == BestUnit(s, content, civ, city) && UnitScore(s, content, civ, city, id) is var us and > 0) scored.Add((id, us));
                    break;
                case "building":
                    if (id == "DISTRICT_SPACEPORT") break;
                    var b = Defs.Building[id];
                    scored.Add((id, Value(s, content, city, b.Food, b.Production, b.Gold, b.Science, b.FoodPct, b.ProductionPct, b.GoldPct, b.SciencePct, false) / cost * 10));
                    break;
                case "wonder":
                    var w = Defs.Wonder[id];
                    double prod = Act1.Report(s, content, city).Total.Production;
                    if (prod < cost / 40) break; // can't win the race from here
                    scored.Add((id, Value(s, content, city, w.Food, w.Production, w.Gold, w.Science, w.FoodPct, w.ProductionPct, w.GoldPct, w.SciencePct, w.Scope == "empire") / cost * 9 * (0.6 + s.Rng.Double())));
                    break;
            }
        }
        if (scored.Count == 0) return null;
        return scored.OrderByDescending(x => x.score).First().id;
    }

    static bool HasSite(Act1State s, CivState civ) => BestSite(s, civ) != null;

    public static (int x, int y)? BestSite(Act1State s, CivState civ)
    {
        var own = s.CitiesOf(civ.Id).ToList();
        if (own.Count == 0) return null;
        (int x, int y)? best = null;
        double bestScore = 0;
        for (int y = 1; y < s.H - 1; y++)
            for (int x = 0; x < s.W; x++)
            {
                int dist = own.Min(c => Hex.Distance(c.X, c.Y, x, y, s.W));
                if (dist > 9 || !Act1.CanFoundCity(s, civ, x, y)) continue;
                double score = 0;
                foreach (var (tx, ty) in Hex.Within(x, y, (int)Rules.CityRadius, s.W, s.H))
                {
                    int owner = s.At(tx, ty).City;
                    if (owner >= 0 && s.City(owner)?.Owner != civ.Id) continue;
                    score += Act1.TileValue(s, tx, ty) * (owner >= 0 ? 0.4 : 1);
                }
                score -= dist * 1.5;
                if (score > bestScore) { bestScore = score; best = (x, y); }
            }
        return bestScore > 12 ? best : null;
    }

    public static void PlaceSettlers(Act1State s, Content content, CivState civ)
    {
        while (civ.PendingSettlers > 0)
        {
            var site = BestSite(s, civ);
            if (site == null)
            {
                // Nowhere to go: the settlers join the capital instead.
                civ.PendingSettlers--;
                var cap = s.CitiesOf(civ.Id).FirstOrDefault(c => c.Capital);
                if (cap != null) cap.Pop++;
                continue;
            }
            Act1.PlaceSettler(s, content, civ, site.Value.x, site.Value.y);
        }
    }
}
