namespace StellarAscension;

/// <summary>How AI empires play Act 2 (and the player's empire in automated runs). Same rules as the player;
/// only their output is scaled by the difficulty.</summary>
public static class Act2Ai
{
    public static void Think(Act2State s, Content content, Empire e)
    {
        var r = Act2.Report(s, e);
        Upkeep(s, e, r);
        Research(s, e);
        Planets(s, e, r);
        Ships(s, e, r);
        Civilians(s, e);
        War(s, e, r);
        Military(s, e);
    }

    /// <summary>A fleet the economy can no longer pay for is cut down, smallest warships first.</summary>
    static void Upkeep(Act2State s, Empire e, EmpireReport r)
    {
        double energy = r.Income["energy"];
        while (energy < 0 && e.Stock["energy"] < 100)
        {
            var pick = s.Fleets.Where(f => f.Owner == e.Id).SelectMany(f => f.Ships.Where(kv => kv.Value > 0 && Defs.Ship[kv.Key].Role == "military").Select(kv => (f, id: kv.Key)))
                .OrderBy(x => Defs.Ship[x.id].Power).FirstOrDefault();
            if (pick.f == null) break;
            if (--pick.f.Ships[pick.id] == 0) pick.f.Ships.Remove(pick.id);
            if (pick.f.Ships.Count == 0) s.Fleets.Remove(pick.f);
            energy += Defs.Ship[pick.id].UpkeepEnergy + (r.NavalUsed > r.NavalCap ? Defs.Ship[pick.id].NavalCap * 1.5 : 0);
            r.NavalUsed -= Defs.Ship[pick.id].NavalCap;
        }
    }

    static void Research(Act2State s, Empire e)
    {
        if (e.Researching != null && Act2.CanResearch(e, e.Researching)) return;
        var options = Defs.Act2Techs.Where(t => Act2.CanResearch(e, t.Id)).ToList();
        e.Researching = options.Count == 0 ? null : s.Rng.Weighted(options, t => (t.Unlocks != "none" ? 2.0 : 1.0) / t.Cost).Id;
    }

    static void Planets(Act2State s, Empire e, EmpireReport r)
    {
        foreach (var p in s.PlanetsOf(e.Id).OrderByDescending(p => p.Pops))
        {
            if (p.Queue.Count > 0) continue;
            // Free jobs are wasted; build what's most needed only when pops are close to filling the jobs there are.
            // ...unless minerals are piling up: then industry turns them into alloys for ships and outposts.
            bool surplus = e.Stock["minerals"] > 600 && r.Income["minerals"] > 8;
            if (Act2.Jobs(p).Count > p.Pops + 2 && !surplus) continue;
            string? want = null;
            if (Act2.BuildingsBuilt(p) < Act2.BuildingSlots(p) && r.Income["energy"] > 4 && r.Income["food"] > 0)
            {
                int labs = s.PlanetsOf(e.Id).Sum(x => x.Buildings.Count(b => b == "building_research_lab_1"));
                want = r.Income["alloys"] < 4 ? (e.Techs.Contains("tech_alloys_1") && !p.Buildings.Contains("building_factory_1") ? "building_factory_1" : "building_foundry_1")
                     : labs < 2 + s.PlanetsOf(e.Id).Count() / 2 ? "building_research_lab_1"
                     : r.Income["food"] < 3 && e.Techs.Contains("tech_food_processing_1") ? "building_hydroponics_farm"
                     : s.Rng.Chance(0.5) ? "building_research_lab_1" : "building_holo_theatres";
            }
            if ((want == null || Act2.CantBuildOnPlanet(s, e, p, want) != null) && Act2.DistrictsBuilt(p) < Act2.DistrictSlots(p))
            {
                double pops = r.Pops;
                want = r.Income["food"] < 2 + pops * 0.05 ? "district_farming"
                     : r.Income["energy"] < 6 + pops * 0.15 ? "district_generator"
                     : r.Income["minerals"] < 8 + pops * 0.3 ? "district_mining"
                     : p.Pops >= Act2.MaxPops(p) - 1 ? "district_city"
                     : surplus ? "district_industrial"
                     : s.Rng.Pick(new[] { "district_generator", "district_mining", "district_industrial", "district_city" });
            }
            if (want != null) Act2.BuildOnPlanet(s, e, p, want);
        }
    }

    static int Count(Act2State s, Empire e, string role) =>
        s.Fleets.Where(f => f.Owner == e.Id).Sum(f => f.Ships.Where(kv => Defs.Ship[kv.Key].Role == role).Sum(kv => kv.Value))
        + e.Shipyard.Count(q => Defs.Ship[q.Id].Role == role);

    static void Ships(Act2State s, Empire e, EmpireReport r)
    {
        bool unsurveyed = s.Systems.Any(x => !x.SurveyedBy.Contains(e.Id));
        if (unsurveyed && Count(s, e, "science") < 2) Act2.QueueShip(s, e, "science");
        if (Count(s, e, "construction") < 2 && Frontier(s, e).Any()) Act2.QueueShip(s, e, "construction");
        if (Count(s, e, "colony") < (e.Stock["food"] > 600 ? 2 : 1) && ColonyTargets(s, e).Count() > Count(s, e, "colony") && e.Stock["food"] > 250) Act2.QueueShip(s, e, "colony");
        // Warships up to naval capacity, best hull first, keeping alloys for outposts.
        double reserve = Rules.Act2OutpostAlloys;
        foreach (var sd in Defs.Ships.Where(x => x.Role == "military").OrderByDescending(x => x.Power))
        {
            while (r.NavalUsed + sd.NavalCap <= r.NavalCap && e.Stock["alloys"] - sd.CostAlloys >= reserve && e.Shipyard.Count < 4
                   && r.Income["energy"] > sd.UpkeepEnergy + 2 && Act2.CantQueueShip(s, e, sd.Id) == null)
            {
                Act2.QueueShip(s, e, sd.Id);
                r.NavalUsed += sd.NavalCap;
                r.Income["energy"] -= sd.UpkeepEnergy;
            }
        }
    }

    /// <summary>Unowned systems this empire has surveyed, next to its borders.</summary>
    static IEnumerable<StarSystem> Frontier(Act2State s, Empire e) =>
        s.SystemsOf(e.Id).SelectMany(x => x.Lanes).Distinct().Select(s.Sys)
            .Where(x => x.Owner < 0 && x.SurveyedBy.Contains(e.Id));

    static IEnumerable<(StarSystem sys, Planet p)> ColonyTargets(Act2State s, Empire e) =>
        s.SystemsOf(e.Id).SelectMany(x => x.Planets.Where(p => p.Owner < 0 && Act2.Habitable(p)).Select(p => (x, p)));

    static void Civilians(Act2State s, Empire e)
    {
        foreach (var f in s.Fleets.Where(f => f.Owner == e.Id && !Act2.IsMilitary(f) && f.Order == "" && f.Path.Count == 0).ToList())
        {
            var role = f.Ships.Keys.First();
            var taken = s.Fleets.Where(x => x.Owner == e.Id && x.Order != "").Select(x => (x.Order, x.OrderSystem, x.OrderPlanet)).ToHashSet();
            var dist = Handoff.Bfs(s, f.System).ToDictionary(x => x.id, x => x.dist);
            switch (role)
            {
                case "science":
                    var target = s.Systems.Where(x => !x.SurveyedBy.Contains(e.Id) && !taken.Contains(("survey", x.Id, -1))
                                                      && !s.Fleets.Any(m => m.System == x.Id && Act2.IsMilitary(m) && s.AtWar(m.Owner, e.Id)))
                        .OrderBy(x => dist.GetValueOrDefault(x.Id, 999)).FirstOrDefault();
                    if (target != null) Act2.Order(s, f, "survey", target.Id);
                    break;
                case "construction":
                    var site = Frontier(s, e).Where(x => !taken.Contains(("outpost", x.Id, -1)))
                        .OrderByDescending(x => (x.RareModifier != null ? 6 : 0) + x.Planets.Count(Act2.Habitable) * 3 + x.Planets.Count)
                        .ThenBy(x => dist.GetValueOrDefault(x.Id, 999)).FirstOrDefault();
                    if (site != null) Act2.Order(s, f, "outpost", site.Id);
                    break;
                case "colony":
                    var c = ColonyTargets(s, e).Where(t => !taken.Contains(("colonize", t.sys.Id, t.p.Id)))
                        .OrderByDescending(t => t.p.Size).ThenBy(t => dist.GetValueOrDefault(t.sys.Id, 999)).FirstOrDefault();
                    if (c.p != null) Act2.Order(s, f, "colonize", c.sys.Id, c.p.Id);
                    break;
            }
        }
    }

    public static IEnumerable<Empire> Neighbors(Act2State s, Empire e)
    {
        var mine = s.SystemsOf(e.Id).Select(x => x.Id).ToHashSet();
        return s.Systems.Where(x => x.Owner >= 0 && x.Owner != e.Id && x.Lanes.Any(mine.Contains)).Select(x => s.Empires[x.Owner]).Distinct().Where(o => o.Alive);
    }

    static void War(Act2State s, Empire e, EmpireReport r)
    {
        if (s.Wars.Any(w => w.A == e.Id || w.B == e.Id)) return;
        if (s.Month < 36 || !s.Rng.Chance(e.Aggression * 0.06)) return;
        var target = Neighbors(s, e).Select(o => (o, power: Act2.Report(s, o).FleetPower)).Where(x => r.FleetPower > Math.Max(30, x.power) * Rules.Act2AiWarPowerRatio)
            .OrderBy(x => x.power).FirstOrDefault();
        if (target.o != null) Act2.DeclareWar(s, e, target.o);
    }

    static void Military(Act2State s, Empire e)
    {
        var fleets = s.Fleets.Where(f => f.Owner == e.Id && Act2.IsMilitary(f)).ToList();
        // Merge idle fleets that share a system.
        foreach (var g in fleets.Where(f => f.Path.Count == 0).GroupBy(f => f.System).Where(g => g.Count() > 1))
        {
            var keep = g.First();
            foreach (var other in g.Skip(1))
            {
                foreach (var (k, n) in other.Ships) keep.Ships[k] = keep.Ships.GetValueOrDefault(k) + n;
                keep.Damage += other.Damage;
                s.Fleets.Remove(other);
            }
        }
        fleets = s.Fleets.Where(f => f.Owner == e.Id && Act2.IsMilitary(f)).ToList();
        var enemies = s.Empires.Where(o => o.Alive && s.AtWar(e.Id, o.Id)).Select(o => o.Id).ToHashSet();
        foreach (var f in fleets.Where(f => f.Path.Count == 0))
        {
            var dist = Handoff.Bfs(s, f.System).ToDictionary(x => x.id, x => x.dist);
            if (enemies.Count == 0)
            {
                if (e.Capital >= 0 && f.System != e.Capital) Act2.Order(s, f, "", e.Capital);
                continue;
            }
            // Defend first, then attack the nearest enemy system.
            var threat = s.SystemsOf(e.Id).Where(x => s.Fleets.Any(m => m.System == x.Id && enemies.Contains(m.Owner) && Act2.IsMilitary(m)))
                .OrderBy(x => dist.GetValueOrDefault(x.Id, 999)).FirstOrDefault();
            var goal = threat ?? s.Systems.Where(x => x.Owner >= 0 && enemies.Contains(x.Owner)).OrderBy(x => dist.GetValueOrDefault(x.Id, 999)).FirstOrDefault();
            if (goal != null && goal.Id != f.System) Act2.Order(s, f, "", goal.Id);
        }
    }
}
