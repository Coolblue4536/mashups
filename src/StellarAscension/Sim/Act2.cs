namespace StellarAscension;

/// <summary>An empire's monthly numbers, as the top bar and tooltips show them.</summary>
public sealed class EmpireReport
{
    public readonly Dictionary<string, double> Income = new();
    public readonly Dictionary<string, double> Mult = new();
    public double NavalCap, NavalUsed, FleetPower;
    public int Pops, Jobs;
    public readonly List<(string id, string source)> ActiveModifiers = new();
}

/// <summary>Act 2 rules: the Stellaris part. One call to Tick is one month.</summary>
public static class Act2
{
    public static readonly string[] Res = { "energy", "minerals", "food", "alloys", "research", "unity", "influence" };

    // ------------------------------------------------------------------ modifiers

    public static List<(string id, string source)> ActiveModifiers(Act2State s, Empire e)
    {
        var list = e.Modifiers.Select(m => (m, "legacy")).ToList();
        foreach (var p in s.PlanetsOf(e.Id))
            foreach (var w in p.Wonders)
                if (Defs.Wonder.TryGetValue(w, out var wd)) list.Add((wd.Modifier, w));
        foreach (var sys in s.SystemsOf(e.Id))
            if (sys.RareModifier != null) list.Add((sys.RareModifier, "rare:" + sys.Id));
        return list;
    }

    public static double Stat(Act2State s, Empire e, string stat, List<(string id, string source)>? active = null)
    {
        double v = 0;
        foreach (var (id, _) in active ?? ActiveModifiers(s, e))
            if (Defs.Modifier.TryGetValue(id, out var m) && m.Stat == stat) v += m.Value;
        return v;
    }

    // ------------------------------------------------------------------ planets

    public static int DistrictSlots(Planet p) => Math.Max(1, p.Size / 3);
    public static int DistrictsBuilt(Planet p) => p.Districts.Values.Sum() + p.Queue.Count(q => Defs.District.ContainsKey(q.Id));
    public static int BuildingSlots(Planet p) => 1 + (int)(p.Pops / Rules.Act2BuildingSlotsPerPops);
    public static int BuildingsBuilt(Planet p) => p.Buildings.Count + p.Queue.Count(q => Defs.PlanetBuilding.ContainsKey(q.Id));
    public static double MaxPops(Planet p) => p.Size + 3 * (p.Districts.TryGetValue("district_city", out var c) ? c : 0);
    public static bool Habitable(Planet p) => Defs.PlanetClass.TryGetValue(p.Class, out var pc) && pc.Habitable;

    /// <summary>Every job on the planet: (resource, output per job, source).</summary>
    public static List<(string resource, double perJob, string source)> Jobs(Planet p)
    {
        var jobs = new List<(string, double, string)>();
        foreach (var b in p.Buildings)
            if (Defs.PlanetBuilding.TryGetValue(b, out var bd))
                for (int i = 0; i < bd.Jobs; i++) jobs.Add((bd.Resource, bd.PerJob, b));
        foreach (var (d, n) in p.Districts)
            if (Defs.District.TryGetValue(d, out var dd))
                for (int i = 0; i < n * (int)Rules.Act2DistrictJobs; i++) jobs.Add((dd.Resource, dd.PerJob, d));
        return jobs;
    }

    /// <summary>The jobs the planet's pops actually work, in the order they take them: enough farming to feed the
    /// world first, then specialist jobs (buildings), then energy, minerals and the rest.</summary>
    public static List<(string resource, double perJob, string source)> WorkedJobs(Planet p)
    {
        int pops = (int)Math.Floor(p.Pops);
        var all = Jobs(p);
        var worked = new List<(string resource, double perJob, string source)>();
        var rest = new List<(string resource, double perJob, string source)>();
        double feed = p.Pops * Rules.Act2FoodPerPop + 1;
        // (Identical jobs are separate jobs: keep duplicates, no set operations here.)
        foreach (var j in all)
        {
            if (j.resource == "food" && worked.Count < pops && feed > 0) { worked.Add(j); feed -= j.perJob; }
            else rest.Add(j);
        }
        rest = rest.OrderBy(j => Defs.PlanetBuilding.ContainsKey(j.source) ? 0 : j.resource switch
            { "energy" => 1, "minerals" => 2, "alloys" => 3, "food" => 4, _ => 5 }).ToList();
        foreach (var j in rest)
        {
            if (worked.Count >= pops) break;
            worked.Add(j);
        }
        return worked;
    }

    public static EmpireReport Report(Act2State s, Empire e)
    {
        var r = new EmpireReport();
        var active = ActiveModifiers(s, e);
        r.ActiveModifiers.AddRange(active);
        foreach (var k in Res) { r.Income[k] = 0; r.Mult[k] = Stat(s, e, k + "_mult", active); }
        double minersUse = 0;
        foreach (var p in s.PlanetsOf(e.Id))
        {
            int pops = (int)Math.Floor(p.Pops);
            r.Pops += pops;
            r.Jobs += Jobs(p).Count;
            foreach (var (res, per, src) in WorkedJobs(p))
            {
                r.Income[res] += per;
                if (src == "district_industrial") minersUse += 3;
            }
            r.Income["food"] -= p.Pops * Rules.Act2FoodPerPop;
            foreach (var b in p.Buildings) if (Defs.PlanetBuilding.TryGetValue(b, out var bd)) r.Income["energy"] -= bd.UpkeepEnergy;
        }
        if (e.Capital >= 0 && s.Sys(e.Capital).Owner == e.Id) r.Income["influence"] += Rules.Act2BaseInfluence;
        // Multipliers apply to what jobs produce; flat bonuses (wonders) come after; the AI's difficulty scales output.
        foreach (var k in Res)
        {
            double gross = Math.Max(0, r.Income[k]);
            double net = r.Income[k] - gross;
            r.Income[k] = gross * (1 + r.Mult[k]) * (e.IsPlayer ? 1 : e.AiMult) + net + Stat(s, e, k + "_add", active);
        }
        r.Income["minerals"] -= minersUse;
        // Ships
        r.NavalCap = Rules.Act2BaseNavalCap + Stat(s, e, "naval_cap_add", active);
        foreach (var f in s.Fleets.Where(f => f.Owner == e.Id))
            foreach (var (id, n) in f.Ships)
            {
                var sd = Defs.Ship[id];
                r.NavalUsed += sd.NavalCap * n;
                r.Income["energy"] -= sd.UpkeepEnergy * n;
            }
        if (r.NavalUsed > r.NavalCap) r.Income["energy"] -= (r.NavalUsed - r.NavalCap) * 1.5;
        r.FleetPower = s.Fleets.Where(f => f.Owner == e.Id).Sum(f => Power(s, e, f, active));
        return r;
    }

    public static double Power(Act2State s, Empire e, Fleet f, List<(string, string)>? active = null) =>
        f.Ships.Sum(kv => Defs.Ship[kv.Key].Power * kv.Value) * (1 + Stat(s, e, "ship_damage_mult", active)) * (e.IsPlayer ? 1 : e.AiMult);

    public static bool IsMilitary(Fleet f) => f.Ships.Keys.Any(k => Defs.Ship[k].Role == "military");

    // ------------------------------------------------------------------ the turn (act2_months_per_turn months)

    public static int MonthsPerTurn => Math.Max(1, (int)Rules.Act2MonthsPerTurn);
    public static int TurnOf(Act2State s) => s.Month / MonthsPerTurn + 1;

    /// <summary>Ends the player's turn: the galaxy runs month by month until the next turn (or the end of the game).</summary>
    public static void EndTurn(Act2State s, Content content)
    {
        for (int i = 0; i < MonthsPerTurn && s.Outcome == ""; i++) Tick(s, content);
    }

    // ------------------------------------------------------------------ the month

    public static void Tick(Act2State s, Content content)
    {
        if (s.Outcome != "") return;
        s.Month++;
        foreach (var e in s.Empires.Where(e => e.Alive)) Economy(s, content, e);
        foreach (var e in s.Empires.Where(e => e.Alive)) Shipyard(s, content, e);
        foreach (var f in s.Fleets.ToList()) Move(s, content, f);
        Combat(s, content);
        Capture(s, content);
        foreach (var e in s.Empires.Where(e => e.Alive && !e.IsPlayer))
            if (s.Month >= e.NextAiThink) { Act2Ai.Think(s, content, e); e.NextAiThink = s.Month + 2; }
        Peace(s, content);
        foreach (var e in s.Empires) e.Score = Score(s, e);
        CheckEnd(s, content);
    }

    static void Economy(Act2State s, Content content, Empire e)
    {
        var r = Report(s, e);
        foreach (var k in Res) e.Stock[k] = e.Stock.GetValueOrDefault(k) + (k == "research" ? 0 : r.Income[k]);
        // Research goes straight into the current technology.
        if (e.Researching != null && !e.Techs.Contains(e.Researching))
        {
            e.ResearchProgress += Math.Max(0, r.Income["research"]);
            var t = Defs.Act2Tech[e.Researching];
            if (e.ResearchProgress >= t.Cost)
            {
                e.ResearchProgress -= t.Cost;
                e.Techs.Add(t.Id);
                if (t.Modifier != "none") e.Modifiers.Add(t.Modifier);
                if (e.IsPlayer) s.Events.Add(new Act2Event(s.Month, "tech", $"Researched {Name(content, t.NameLoc, t.Id)}.", e.Id));
                e.Researching = null;
            }
        }
        // Deficits: energy and minerals stop at zero; starving worlds shrink.
        foreach (var k in new[] { "energy", "minerals", "alloys", "influence", "unity" }) e.Stock[k] = Math.Max(0, e.Stock[k]);
        bool starving = e.Stock["food"] < 0;
        if (starving) e.Stock["food"] = 0;
        double growth = 1 + Stat(s, e, "pop_growth_mult");
        double build = 1 + Stat(s, e, "build_speed_mult");
        foreach (var p in s.PlanetsOf(e.Id))
        {
            if (starving) p.Pops = Math.Max(1, p.Pops - 1.0 / 12);
            else if (p.Pops < MaxPops(p)) p.Pops = Math.Min(MaxPops(p), p.Pops + growth / Rules.Act2PopGrowthMonths * (e.IsPlayer ? 1 : e.AiMult));
            if (p.Queue.Count > 0)
            {
                var q = p.Queue[0];
                q.MonthsLeft -= build;
                if (q.MonthsLeft <= 0)
                {
                    p.Queue.RemoveAt(0);
                    if (Defs.District.ContainsKey(q.Id)) p.Districts[q.Id] = p.Districts.GetValueOrDefault(q.Id) + 1;
                    else p.Buildings.Add(q.Id);
                    if (e.IsPlayer) s.Events.Add(new Act2Event(s.Month, "built", $"{p.Name}: {Name(content, Defs.District.TryGetValue(q.Id, out var dd) ? dd.NameLoc : Defs.PlanetBuilding[q.Id].NameLoc, q.Id)} finished.", e.Id));
                }
            }
        }
    }

    static void Shipyard(Act2State s, Content content, Empire e)
    {
        if (e.Shipyard.Count == 0 || e.Capital < 0 || s.Sys(e.Capital).Owner != e.Id) return;
        var q = e.Shipyard[0];
        q.MonthsLeft -= 1 + Stat(s, e, "ship_build_speed_mult");
        if (q.MonthsLeft > 0) return;
        e.Shipyard.RemoveAt(0);
        var sd = Defs.Ship[q.Id];
        Fleet? f = null;
        if (sd.Role == "military")
            f = s.Fleets.FirstOrDefault(x => x.Owner == e.Id && x.System == e.Capital && x.Path.Count == 0 && IsMilitary(x));
        if (f == null)
        {
            f = new Fleet { Id = s.NextFleetId++, Owner = e.Id, System = e.Capital, Name = sd.Role == "military" ? $"{e.Adjective} Fleet {s.Fleets.Count(x => x.Owner == e.Id && IsMilitary(x)) + 1}" : sd.Id };
            s.Fleets.Add(f);
        }
        f.Ships[q.Id] = f.Ships.GetValueOrDefault(q.Id) + 1;
        if (e.IsPlayer) s.Events.Add(new Act2Event(s.Month, "ship", $"A new {Name(content, sd.NameLoc, sd.Id)} is ready at {s.Sys(e.Capital).Name}.", e.Id, e.Capital));
    }

    // ------------------------------------------------------------------ orders the player (or AI) can give

    public static string? CantQueueShip(Act2State s, Empire e, string ship)
    {
        var sd = Defs.Ship[ship];
        if (sd.RequiresTech != "none" && !e.Techs.Contains(sd.RequiresTech)) return "Needs its technology.";
        if (e.Stock.GetValueOrDefault("alloys") < sd.CostAlloys) return $"Needs {sd.CostAlloys} alloys.";
        if (e.Stock.GetValueOrDefault("minerals") < sd.CostMinerals) return $"Needs {sd.CostMinerals} minerals.";
        if (e.Stock.GetValueOrDefault("food") < sd.CostFood) return $"Needs {sd.CostFood} food.";
        if (e.Capital < 0 || s.Sys(e.Capital).Owner != e.Id) return "You have no capital shipyard.";
        if (e.Shipyard.Count >= 6) return "The shipyard queue is full.";
        return null;
    }

    public static bool QueueShip(Act2State s, Empire e, string ship)
    {
        if (CantQueueShip(s, e, ship) != null) return false;
        var sd = Defs.Ship[ship];
        e.Stock["alloys"] -= sd.CostAlloys; e.Stock["minerals"] -= sd.CostMinerals; e.Stock["food"] -= sd.CostFood;
        e.Shipyard.Add(new QueueItem { Id = ship, MonthsLeft = sd.BuildMonths });
        return true;
    }

    public static string? CantBuildOnPlanet(Act2State s, Empire e, Planet p, string id)
    {
        if (p.Owner != e.Id) return "Not your world.";
        if (p.Queue.Count >= 3) return "This world's queue is full.";
        if (Defs.District.TryGetValue(id, out var d))
        {
            if (DistrictsBuilt(p) >= DistrictSlots(p)) return "No room for more districts.";
            if (e.Stock.GetValueOrDefault("minerals") < d.CostMinerals) return $"Needs {d.CostMinerals} minerals.";
            return null;
        }
        if (Defs.PlanetBuilding.TryGetValue(id, out var b))
        {
            if (b.RequiresTech != "none" && !e.Techs.Contains(b.RequiresTech)) return "Needs its technology.";
            if (BuildingsBuilt(p) >= BuildingSlots(p)) return $"No free building slot (one per {Rules.Act2BuildingSlotsPerPops} pops).";
            if (e.Stock.GetValueOrDefault("minerals") < b.CostMinerals) return $"Needs {b.CostMinerals} minerals.";
            return null;
        }
        return "Unknown.";
    }

    public static bool BuildOnPlanet(Act2State s, Empire e, Planet p, string id)
    {
        if (CantBuildOnPlanet(s, e, p, id) != null) return false;
        int cost = Defs.District.TryGetValue(id, out var d) ? d.CostMinerals : Defs.PlanetBuilding[id].CostMinerals;
        e.Stock["minerals"] -= cost;
        p.Queue.Add(new QueueItem { Id = id, MonthsLeft = Defs.District.ContainsKey(id) ? 6 : 8 });
        return true;
    }

    public static bool CanResearch(Empire e, string tech) =>
        Defs.Act2Tech.TryGetValue(tech, out var t) && !e.Techs.Contains(tech) && (t.Requires == "none" || e.Techs.Contains(t.Requires));

    /// <summary>Shortest hyperlane route from a to b (systems after a), or null.</summary>
    public static List<int>? Route(Act2State s, int a, int b)
    {
        if (a == b) return new List<int>();
        var prev = new Dictionary<int, int> { [a] = a };
        var q = new Queue<int>();
        q.Enqueue(a);
        while (q.Count > 0)
        {
            int c = q.Dequeue();
            if (c == b) break;
            foreach (var n in s.Sys(c).Lanes)
                if (prev.TryAdd(n, c)) q.Enqueue(n);
        }
        if (!prev.ContainsKey(b)) return null;
        var path = new List<int>();
        for (int c = b; c != a; c = prev[c]) path.Add(c);
        path.Reverse();
        return path;
    }

    public static bool Order(Act2State s, Fleet f, string order, int system, int planet = -1)
    {
        var route = Route(s, f.System, system);
        if (route == null) return false;
        if (f.Path.Count > 0 && route.Count > 0 && f.Path[0] != route[0]) f.LaneProgress = 0;
        f.Path = route;
        f.Order = order;
        f.OrderSystem = system;
        f.OrderPlanet = planet;
        f.OrderMonths = 0;
        return true;
    }

    // ------------------------------------------------------------------ movement and civilian work

    static double Lane(Act2State s, int a, int b)
    {
        var A = s.Sys(a); var B = s.Sys(b);
        return Math.Sqrt((A.X - B.X) * (A.X - B.X) + (A.Y - B.Y) * (A.Y - B.Y));
    }

    public static double Speed(Act2State s, Empire e, Fleet f) =>
        (f.Ships.Count == 0 ? 10 : f.Ships.Keys.Min(k => Defs.Ship[k].Speed)) * 4 * (1 + Stat(s, e, "ship_speed_mult"));

    static void Move(Act2State s, Content content, Fleet f)
    {
        var e = s.Empires[f.Owner];
        if (!e.Alive) { s.Fleets.Remove(f); return; }
        if (f.Path.Count > 0)
        {
            f.LaneProgress += Speed(s, e, f);
            while (f.Path.Count > 0 && f.LaneProgress >= Lane(s, f.System, f.Path[0]))
            {
                f.LaneProgress -= Lane(s, f.System, f.Path[0]);
                f.System = f.Path[0];
                f.Path.RemoveAt(0);
            }
            if (f.Path.Count == 0) f.LaneProgress = 0;
            return;
        }
        // Repairs in friendly space.
        if (s.Sys(f.System).Owner == f.Owner) f.Damage = Math.Max(0, f.Damage - 50);
        if (f.Order == "" || f.System != f.OrderSystem) return;
        var sys = s.Sys(f.System);
        switch (f.Order)
        {
            case "survey":
                if (sys.SurveyedBy.Contains(e.Id)) { f.Order = ""; return; }
                f.OrderMonths += 1 + Stat(s, e, "survey_speed_mult");
                if (f.OrderMonths >= Rules.Act2SurveyMonths)
                {
                    sys.SurveyedBy.Add(e.Id);
                    f.Order = "";
                    if (e.IsPlayer)
                    {
                        int hab = sys.Planets.Count(p => Habitable(p) && p.Owner < 0);
                        s.Events.Add(new Act2Event(s.Month, "survey", $"Surveyed {sys.Name}: {sys.Planets.Count} planet{(sys.Planets.Count == 1 ? "" : "s")}" + (hab > 0 ? $", {hab} habitable." : "."), e.Id, sys.Id));
                    }
                }
                break;
            case "outpost":
                if (sys.Owner >= 0) { f.Order = ""; return; }
                if (f.OrderMonths == 0)
                {
                    if (e.Stock["alloys"] < Rules.Act2OutpostAlloys || e.Stock["influence"] < Rules.Act2OutpostInfluence) return; // wait for resources
                    e.Stock["alloys"] -= Rules.Act2OutpostAlloys;
                    e.Stock["influence"] -= Rules.Act2OutpostInfluence;
                }
                f.OrderMonths += 1;
                if (f.OrderMonths >= Rules.Act2OutpostBuildMonths)
                {
                    sys.Owner = e.Id;
                    if (!sys.SurveyedBy.Contains(e.Id)) sys.SurveyedBy.Add(e.Id);
                    f.Order = "";
                    if (e.IsPlayer) s.Events.Add(new Act2Event(s.Month, "outpost", $"Outpost built: {sys.Name} is yours." + (sys.RareModifier != null ? $" Its rare system bonus is now yours: {Defs.Modifier[sys.RareModifier].EffectText}." : ""), e.Id, sys.Id));
                }
                break;
            case "colonize":
                var p = sys.Planets.FirstOrDefault(x => x.Id == f.OrderPlanet);
                if (p == null || p.Owner >= 0 || sys.Owner != e.Id || !Habitable(p)) { f.Order = ""; return; }
                f.OrderMonths += 1;
                if (f.OrderMonths >= Rules.Act2ColonizeMonths)
                {
                    p.Owner = e.Id;
                    p.Pops = 1;
                    s.Fleets.Remove(f);
                    if (e.IsPlayer) s.Events.Add(new Act2Event(s.Month, "colony", $"New colony founded on {p.Name}.", e.Id, sys.Id));
                }
                break;
        }
    }

    // ------------------------------------------------------------------ war

    static double StarbaseHp(Act2State s, Empire e) => Rules.Act2StarbasePower * 20 * (1 + Stat(s, e, "starbase_power_mult"));

    static void Combat(Act2State s, Content content)
    {
        foreach (var sys in s.Systems)
        {
            var here = s.Fleets.Where(f => f.System == sys.Id && f.Path.Count == 0 || f.System == sys.Id && f.LaneProgress == 0).ToList();
            var parties = here.Select(f => f.Owner).Distinct().ToList();
            bool starbase = sys.Owner >= 0 && s.Empires[sys.Owner].Alive && sys.StarbaseDamage < StarbaseHp(s, s.Empires[sys.Owner]);
            if (sys.Owner >= 0 && !parties.Contains(sys.Owner) && starbase) parties.Add(sys.Owner);
            bool fight = parties.Any(a => parties.Any(b => s.AtWar(a, b) && (here.Any(f => f.Owner == a && IsMilitary(f)) || a == sys.Owner && starbase)));
            if (!fight)
            {
                if (sys.StarbaseDamage > 0 && sys.Owner >= 0) sys.StarbaseDamage = Math.Max(0, sys.StarbaseDamage - 60);
                continue;
            }
            var dealt = new Dictionary<int, double>();
            foreach (var a in parties)
            {
                var ea = s.Empires[a];
                double power = here.Where(f => f.Owner == a).Sum(f => Power(s, ea, f));
                if (a == sys.Owner && starbase) power += Rules.Act2StarbasePower * (1 + Stat(s, ea, "starbase_power_mult"));
                var enemies = parties.Where(b => s.AtWar(a, b)).ToList();
                foreach (var b in enemies) dealt[b] = dealt.GetValueOrDefault(b) + power / enemies.Count;
            }
            foreach (var (b, dmg) in dealt)
            {
                var eb = s.Empires[b];
                var fleets = here.Where(f => f.Owner == b).OrderBy(f => IsMilitary(f) ? 0 : 1).ToList();
                double left = dmg;
                // Military fleets and the starbase soak damage first; civilians die when unprotected.
                foreach (var f in fleets.Where(IsMilitary))
                {
                    if (left <= 0) break;
                    f.Damage += left; left = 0;
                    Attrition(s, content, f, eb);
                }
                if (left > 0 && b == sys.Owner && starbase)
                {
                    sys.StarbaseDamage += left; left = 0;
                    if (sys.StarbaseDamage >= StarbaseHp(s, eb))
                        s.Events.Add(new Act2Event(s.Month, "starbase", $"The starbase at {sys.Name} has been disabled.", b, sys.Id));
                }
                if (left > 0)
                    foreach (var f in fleets.Where(f => !IsMilitary(f)))
                    {
                        s.Fleets.Remove(f);
                        if (eb.IsPlayer) s.Events.Add(new Act2Event(s.Month, "lost", $"Your {f.Name} was destroyed at {sys.Name}.", b, sys.Id));
                    }
            }
        }
    }

    static void Attrition(Act2State s, Content content, Fleet f, Empire owner)
    {
        double hullMult = 1 + Stat(s, owner, "ship_hull_mult");
        while (f.Ships.Count > 0)
        {
            var weakest = f.Ships.Keys.OrderBy(k => Defs.Ship[k].Hull).First();
            double hull = Defs.Ship[weakest].Hull * hullMult;
            if (f.Damage < hull) break;
            f.Damage -= hull;
            if (--f.Ships[weakest] == 0) f.Ships.Remove(weakest);
        }
        if (f.Ships.Count == 0)
        {
            s.Fleets.Remove(f);
            s.Events.Add(new Act2Event(s.Month, "lost", $"{owner.Name}'s {f.Name} was destroyed at {s.Sys(f.System).Name}.", owner.Id, f.System));
        }
    }

    static void Capture(Act2State s, Content content)
    {
        foreach (var sys in s.Systems.Where(x => x.Owner >= 0))
        {
            var owner = s.Empires[sys.Owner];
            var attackers = s.Fleets.Where(f => f.System == sys.Id && f.Path.Count == 0 && IsMilitary(f) && s.AtWar(f.Owner, sys.Owner)).ToList();
            bool defended = s.Fleets.Any(f => f.System == sys.Id && f.Owner == sys.Owner && IsMilitary(f)) || sys.StarbaseDamage < StarbaseHp(s, owner);
            if (attackers.Count == 0 || defended) { sys.CaptureProgress = 0; sys.CaptureBy = -1; continue; }
            int by = attackers.GroupBy(f => f.Owner).OrderByDescending(g => g.Sum(f => Power(s, s.Empires[g.Key], f))).First().Key;
            if (sys.CaptureBy != by) { sys.CaptureBy = by; sys.CaptureProgress = 0; }
            sys.CaptureProgress += 1;
            if (sys.CaptureProgress < Rules.Act2CaptureMonths) continue;
            var winner = s.Empires[by];
            s.Events.Add(new Act2Event(s.Month, "captured", $"{winner.Name} took {sys.Name} from {owner.Name}.", by, sys.Id));
            sys.Owner = by;
            sys.StarbaseDamage = 0;
            sys.CaptureProgress = 0;
            if (!sys.SurveyedBy.Contains(by)) sys.SurveyedBy.Add(by);
            foreach (var p in sys.Planets.Where(p => p.Owner == owner.Id)) { p.Owner = by; p.Queue.Clear(); }
            if (owner.Capital == sys.Id)
            {
                var next = s.PlanetsOf(owner.Id).OrderByDescending(p => p.Pops).FirstOrDefault();
                owner.Capital = next != null ? s.Systems.First(x => x.Planets.Contains(next)).Id : -1;
            }
            if (!s.PlanetsOf(owner.Id).Any() && !s.SystemsOf(owner.Id).Any()) Eliminate(s, owner, winner);
        }
    }

    static void Eliminate(Act2State s, Empire loser, Empire by)
    {
        loser.Alive = false;
        s.Fleets.RemoveAll(f => f.Owner == loser.Id);
        s.Wars.RemoveAll(w => w.A == loser.Id || w.B == loser.Id);
        s.Events.Add(new Act2Event(s.Month, "eliminated", $"{loser.Name} has fallen to {by.Name}.", loser.Id));
    }

    public static bool DeclareWar(Act2State s, Empire a, Empire b)
    {
        if (a.Id == b.Id || !b.Alive || s.AtWar(a.Id, b.Id)) return false;
        s.Wars.Add(new War { A = a.Id, B = b.Id, StartMonth = s.Month });
        s.Events.Add(new Act2Event(s.Month, "war", $"{a.Name} declared war on {b.Name}.", a.Id));
        return true;
    }

    /// <summary>Whether the other side would accept peace now.</summary>
    public static bool WouldAcceptPeace(Act2State s, Empire asker, Empire other)
    {
        var w = s.Wars.FirstOrDefault(w => w.A == asker.Id && w.B == other.Id || w.A == other.Id && w.B == asker.Id);
        if (w == null) return false;
        int months = s.Month - w.StartMonth;
        if (months >= Rules.Act2WarExhaustionMonths) return true;
        if (months < 12) return false;
        double mine = Report(s, other).FleetPower, theirs = Report(s, asker).FleetPower;
        return mine < theirs * 1.3;
    }

    public static bool MakePeace(Act2State s, Empire a, Empire b)
    {
        int n = s.Wars.RemoveAll(w => w.A == a.Id && w.B == b.Id || w.A == b.Id && w.B == a.Id);
        if (n > 0) s.Events.Add(new Act2Event(s.Month, "peace", $"{a.Name} and {b.Name} made peace.", a.Id));
        return n > 0;
    }

    static void Peace(Act2State s, Content content)
    {
        foreach (var w in s.Wars.ToList())
        {
            var a = s.Empires[w.A]; var b = s.Empires[w.B];
            if (a.IsPlayer || b.IsPlayer)
            {
                if (s.Month - w.StartMonth >= Rules.Act2WarExhaustionMonths * 1.5) MakePeace(s, a, b);
                continue;
            }
            if (s.Month - w.StartMonth >= Rules.Act2WarExhaustionMonths && s.Rng.Chance(0.05)) MakePeace(s, a, b);
        }
    }

    // ------------------------------------------------------------------ score and the end

    public static double Score(Act2State s, Empire e)
    {
        if (!e.Alive) return 0;
        double pops = s.PlanetsOf(e.Id).Sum(p => p.Pops);
        int systems = s.SystemsOf(e.Id).Count();
        double power = s.Fleets.Where(f => f.Owner == e.Id).Sum(f => Power(s, e, f));
        return Math.Round(pops * Rules.Act2ScorePerPop + systems * Rules.Act2ScorePerSystem + e.Techs.Count * Rules.Act2ScorePerTech
                          + power * Rules.Act2ScorePerPower + e.Stock.GetValueOrDefault("unity") / 100);
    }

    static void CheckEnd(Act2State s, Content content)
    {
        var p = s.Player;
        if (!p.Alive) { s.Outcome = "defeated"; return; }
        int owned = s.Systems.Count(x => x.Owner >= 0);
        // Conquest: most of the settled galaxy, and a real share of the whole galaxy (not just an early lead), while every rival is reduced.
        int mine = s.SystemsOf(p.Id).Count();
        if (owned > 0 && mine >= owned * Rules.Act2ConquestShare && mine >= s.Systems.Count * 0.5 && s.Month > 120)
        {
            s.Outcome = "won_conquest";
            s.Events.Add(new Act2Event(s.Month, "end", $"{p.Name} holds most of the settled galaxy. You have won.", p.Id));
            return;
        }
        if (s.Year >= s.EndYear)
        {
            var best = s.Empires.Where(e => e.Alive).OrderByDescending(e => e.Score).First();
            s.Outcome = best.IsPlayer ? "won_score" : "lost_score";
            s.Events.Add(new Act2Event(s.Month, "end", best.IsPlayer ? $"The year {s.EndYear}: {p.Name} leads the galaxy. You have won." : $"The year {s.EndYear}: {best.Name} leads the galaxy.", p.Id));
        }
    }

    public static string Name(Content content, string locKey, string fallback) =>
        content.Stellaris?.Text(locKey) is { Length: > 0 } t ? t : fallback.Replace('_', ' ');
}
