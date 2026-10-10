using System.Numerics;

namespace GrandGalactic;

/// <summary>A scripted player for --playtest: plays whole runs with only the moves a person can make (move cards,
/// click orders, buy packs, sell, fit, attack) and logs how the run went, to find dead ends and pacing problems.</summary>
public static class Playtest
{
    public sealed record Report(string Ethic, int Seed, RunState State, string End, int Moon, float Seconds, List<string> Timeline)
    {
        /// <summary>Moon each milestone was first reached (warship, destroyers, cruisers, battleships, colony...).</summary>
        public Dictionary<string, int> Milestones { get; } = new();
        /// <summary>Numbers when the crisis began (people, warships, strength, techs, colonies, systems).</summary>
        public Dictionary<string, int> AtCrisis { get; } = new();
    }

    /// <summary>--balance N: N scripted runs on every difficulty (the ethics take turns), summarised.</summary>
    public static int Balance(int n)
    {
        foreach (var d in Defs.Difficulties)
        {
            var reports = new List<Report>();
            for (int i = 0; i < n; i++) reports.Add(Play(Defs.Ethics[i % Defs.Ethics.Length], 100 + i, d));
            int wins = reports.Count(r => r.State == RunState.Won);
            Log.Info($"== {d.Name}: {wins}/{n} won (act 2 moon {d.Act2Moon}, crisis moon {d.CrisisMoon}, boss by moon {d.CrisisMoon + d.BossDelayMoons})");
            foreach (var g in reports.Where(r => r.State != RunState.Won).GroupBy(r => r.End)) Log.Info($"     lost x{g.Count()}: {g.Key} (moons {string.Join(",", g.Select(r => r.Moon))})");
            foreach (var key in new[] { "warship", "tech_destroyers", "tech_cruisers", "tech_battleships", "tech_titans", "colony", "outpost", "claim", "baby",
                                        "contact", "attacked by empire", "tributary", "megastructure", "energy deficit", "infinite research" })
            {
                var got = reports.Where(r => r.Milestones.ContainsKey(key)).Select(r => r.Milestones[key]).ToList();
                Log.Info($"     {key,-17} reached in {got.Count}/{n} runs" + (got.Count > 0 ? $", avg moon {got.Average():0.0}" : ""));
            }
            var crisis = reports.Where(r => r.AtCrisis.Count > 0).ToList();
            if (crisis.Count > 0)
                Log.Info("     at crisis (avg): " + string.Join(", ", crisis[0].AtCrisis.Keys.Select(k => $"{k} {crisis.Average(r => r.AtCrisis.GetValueOrDefault(k)):0.#}")));
            foreach (var r in reports) Log.Info($"     {r.Ethic,-12} seed {r.Seed}: {r.State} moon {r.Moon} - {r.End}");
        }
        return 0;
    }

    public static int RunCli(string[] args)
    {
        int runs = 0, wins = 0;
        foreach (var e in Defs.Ethics.Where(e => Environment.GetEnvironmentVariable("GG_PT_ETHIC") is not { } only || e.Id == only))
            for (int seed = 1; seed <= 2; seed++)
            {
                var r = Play(e, seed, Defs.DefaultDifficulty);
                runs++;
                if (r.State == RunState.Won) wins++;
                Log.Info($"== {r.Ethic} seed {r.Seed}: {r.State} at moon {r.Moon} ({r.Seconds / 60:0} min) {r.End}");
                foreach (var line in r.Timeline) Log.Info("     " + line);
            }
        Log.Info($"Playtest: {wins}/{runs} runs won");
        return 0;
    }

    /// <summary>--bossprobe: how a strong mid-game fleet (fitted destroyers and cruisers with an Admiral) fares against
    /// each crisis leader at several strength multipliers.</summary>
    public static int BossProbe()
    {
        foreach (var boss in Defs.Crises.Select(c => c.BossCard))
            foreach (var mult in new[] { 1.0f, 0.75f, 0.6f, 0.5f })
            {
                int wins = 0; float tsum = 0;
                for (int seed = 0; seed < 4; seed++)
                {
                    var s = new Sim(Defs.Ethics[0], 500 + seed);
                    s.Table.Stacks.Clear(); s.Table.Battles.Clear(); s.MoonTime = -1e6f;
                    s.Techs.Add("tech_fleet_doctrine_2");
                    var foe = s.Spawn(boss, new Vector2(1200, 500), jitter: false);
                    foe.MaxHp = foe.Hp = foe.MaxHp * mult; foe.MaxShield = foe.Shield = foe.MaxShield * mult; foe.MaxArmor = foe.Armor = foe.MaxArmor * mult;
                    foreach (var g in foe.Guns) g.Damage *= mult;
                    Stack? fleet = null;
                    var hulls = new (string ship, string[] parts)[]
                    {
                        ("cruiser", new[] { "blue_laser", "railgun", "improved_deflector" }), ("cruiser", new[] { "blue_laser", "railgun", "crystal_armor" }),
                        ("destroyer", new[] { "blue_laser", "deflector" }), ("destroyer", new[] { "railgun", "deflector" }), ("destroyer", new[] { "blue_laser", "nanocomposite_armor" }),
                        ("destroyer", new[] { "railgun", "nanocomposite_armor" }), ("destroyer", new[] { "space_torpedoes", "deflector" }), ("destroyer", new[] { "blue_laser", "deflector" }),
                        ("destroyer", new[] { "railgun", "deflector" }), ("destroyer", new[] { "blue_laser", "flak_battery" }),
                    };
                    foreach (var (ship, parts) in hulls)
                    {
                        var c = s.Spawn(ship, new Vector2(300, 300), jitter: false);
                        foreach (var p in parts) s.Fit(s.Spawn(p, new Vector2(300, 800), jitter: false), c);
                        if (fleet == null) fleet = c.Stack!; else s.StackOnto(c.Stack!, fleet);
                    }
                    s.AssignAdmiral(s.Spawn("admiral", new Vector2(300, 800), jitter: false), fleet!.Cards[0]);
                    s.Attack(fleet, foe);
                    float t = 0;
                    while (t < 400 && s.Table.Battles.Count > 0 && s.State == RunState.Playing) { s.Update(0.05f); t += 0.05f; }
                    if (!s.AllCards.Contains(foe) || s.State == RunState.Won) { wins++; tsum += t; }
                }
                Log.Info($"{boss,-18} x{mult:0.00}: {wins}/4 wins" + (wins > 0 ? $" in {tsum / wins:0}s" : ""));
            }
        return 0;
    }

    public static Report Play(EthicDef ethic, int seed, DifficultyDef diff, int saveAtMoon = 0)
    {
        var s = new Sim(ethic, seed, null, diff);
        var log = new List<string>();
        var stones = new Dictionary<string, int>();
        var atCrisis = new Dictionary<string, int>();
        void Stone(string key) { if (!stones.ContainsKey(key)) stones[key] = s.Moon; }
        var seen = new HashSet<string>();
        void Mark(string key, string text) { if (seen.Add(key)) log.Add($"moon {s.Moon,2} {s.MoonTime,3:0}s: {text}"); }
        float t = 0, think = 0;
        int lastMoon = 1;
        while (s.State == RunState.Playing && t < 60 * 60 && s.Moon <= diff.CrisisMoon + diff.BossDelayMoons + 8)
        {
            s.Update(0.1f);
            t += 0.1f;
            if (saveAtMoon > 0 && s.Moon >= saveAtMoon) { s.SaveTo(Sim.SavePath); log.Add($"saved at moon {s.Moon} to {Sim.SavePath}"); break; }
            foreach (var m in s.Messages)
            {
                if (m.Contains("starved")) log.Add($"moon {s.Moon,2}: {m}");
                if (m.StartsWith("Researched")) Mark("r" + m, m.Split('!')[0]);
                if (m.StartsWith("Act ")) Mark(m[..5], m.Split('.')[0]);
                if (m.Contains("has arrived")) { Mark("boss", m); log.Add("fleet at boss: " + string.Join(", ", s.AllCards.Where(c => c.Def.HasTag("warship")).GroupBy(c => c.Def.Id + "(" + c.Parts.Count + " parts)").Select(g => $"{g.Count()} {g.Key}"))); }
                if (m.Contains("Raiders")) log.Add($"moon {s.Moon,2}: raid");
                if (m.Contains("lost in battle")) log.Add($"moon {s.Moon,2}: {m}");
            }
            if (s.AllCards.Any(c => c.Def.HasTag("warship"))) Stone("warship");
            foreach (var h in Sim.HullLadder) if (s.Techs.Contains(h)) Stone(h);
            if (s.AllCards.Any(c => c.Def.HasTag("colony") && c.Claimed)) Stone("colony");
            if (s.AllCards.Any(c => c.Def.HasTag("uninhabitable") && c.Claimed)) Stone("outpost");
            if (s.ClaimedCount > 1) Stone("claim");
            if (s.AllCards.Any(c => c.Def.HasTag("baby"))) Stone("baby");
            if (s.Empires.Any(e => e.Contacted)) Stone("contact");
            if (s.Empires.Any(e => e.Status == "tributary")) Stone("tributary");
            if (s.Empires.Any(e => e.Status == "war" && e.TheyDeclared)) Stone("attacked by empire");
            if (s.AllCards.Any(c => c.Def.HasTag("megastructure"))) Stone("megastructure");
            if (s.EnergyDeficit) Stone("energy deficit");
            if (s.RepLevels.Count > 0) Stone("infinite research");
            if (s.RiftOpen && atCrisis.Count == 0)
            {
                atCrisis["people"] = s.AllCards.Count(c => c.Def.Category == "person");
                atCrisis["warships"] = s.AllCards.Count(c => c.Def.HasTag("warship"));
                atCrisis["strength"] = Sim.Strength(s.AllCards.Where(c => c.Def.HasTag("warship")), 1.2f);
                atCrisis["techs"] = s.Techs.Count;
                atCrisis["colonies"] = s.AllCards.Count(c => c.Def.IsPlanet && c.Claimed && c.Def.ColonizeWith != "none");
                atCrisis["systems"] = s.ClaimedCount;
                atCrisis["boss threat"] = Sim.Threat(s.NewCard(s.Crisis.BossCard));
            }
            s.Messages.Clear();
            s.Gains.Clear();
            s.Events.Clear();
            if (s.Moon != lastMoon)
            {
                lastMoon = s.Moon;
                int people = s.AllCards.Count(c => c.Def.Category == "person");
                int ships = s.AllCards.Count(c => c.Def.HasTag("warship"));
                log.Add($"moon {s.Moon,2} start: {people} people, {ships} warships, {s.Techs.Count} techs, {s.Systems.Count} systems | " +
                        string.Join(" ", s.Res.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key}={kv.Value}")));
                if (Environment.GetEnvironmentVariable("GG_PT_DUMP") == "1")
                    foreach (var x in s.Table.Stacks.Where(x => !x.HasHostile && x.Cards.Count > 1 || x.Wait != null || x.Order != null))
                        log.Add($"           [{string.Join("+", x.Cards.Select(c => c.Def.Id))}] active={x.Active?.Id} order={x.Order?.Id} wait={x.Wait}");
            }
            think += 0.1f;
            if (think < 1f) continue;
            think = 0;
            Think(s, Mark);
        }
        log.Add("end: techs " + string.Join(",", s.Techs.Select(x => x.Replace("tech_", ""))));
        log.Add("end: ships " + string.Join(", ", s.AllCards.Where(c => c.Def.Category == "ship").GroupBy(c => c.Def.Id).Select(g => $"{g.Count()} {g.Key}")));
        var report = new Report(ethic.Id, seed, s.State, s.EndReason, s.Moon, t, log);
        foreach (var kv in stones) report.Milestones[kv.Key] = kv.Value;
        foreach (var kv in atCrisis) report.AtCrisis[kv.Key] = kv.Value;
        return report;
    }

    static IEnumerable<Stack> Mine(Sim s) => s.Table.Stacks.Where(x => !x.Traveling && !x.HasHostile);
    static Stack? Lone(Sim s, Func<Card, bool> f) => Mine(s).FirstOrDefault(x => x.Cards.Count == 1 && f(x.Root) && x.Root.Battle == null);
    static bool Busy(Stack x) => x.Active != null || x.Order != null;

    /// <summary>Take one card out of its stack (as a player would drag it off).</summary>
    static Stack Lift(Sim s, Card c)
    {
        var st = c.Stack!;
        int i = st.Cards.IndexOf(c);
        if (i == st.Cards.Count - 1) return s.Split(st, i);
        var top = s.Split(st, i);              // c and everything above it
        if (top.Cards.Count > 1)
        {
            var rest = s.Split(top, 1);        // put the cards above back
            s.StackOnto(rest, st);
        }
        return top;
    }

    /// <summary>Influence from Edicts; outposts in owned systems; claim a nearby system; colonise its habitable planets.</summary>
    static void Expand(Sim s, Action<string, string> mark)
    {
        var R = Defs.Recipes.ToDictionary(r => r.Id);
        var home = s.Home;
        // A Construction Ship left on a finished outpost or claim steps off again.
        foreach (var st in Mine(s).Where(x => x.Cards.Count > 1 && x.Active == null && x.Order == null && x.Cards.Any(c => c.Def.Id == "construction_ship")).ToList())
            Lift(s, st.Cards.First(c => c.Def.Id == "construction_ship"));
        if (s.Have("unity") >= 2 && s.Have("influence") < 3 && Mine(s).FirstOrDefault(x => x.Root.Def.Id == "homeworld") is { } hw && hw.Order == null)
            s.QueueOrder(hw, R["w_edict"]);
        var cs = Mine(s).FirstOrDefault(x => x.Cards.Count == 1 && x.Root.Def.Id == "construction_ship" && !Busy(x));
        if (cs != null && s.Have("influence") >= 1)
        {
            var here = s.SystemAt(Sim.CardCenter(cs));
            // An outpost on an uninhabitable world in a system we own (same system, no travel needed).
            if (here is { Claimed: true } && s.StacksIn(here).FirstOrDefault(x => x.Cards.Count == 1 && x.Root.Def.HasTag("uninhabitable") && !x.Root.Claimed) is { } rock)
            { s.StackOnto(cs, rock); mark("outpost", "builds an outpost"); return; }
            // Claim: travel to a quiet system with habitable worlds, then sit on its star.
            if (s.Have("influence") >= 2 && s.ClaimedCount < Defs.Rules.ClaimLimit)
            {
                if (here is { Claimed: false } && s.ClaimBlock(here) == null && s.StacksIn(here).FirstOrDefault(x => x.Root.Def.Category == "star" && x.Cards.Count == 1) is { } star)
                { s.StackOnto(cs, star); mark("claim" + here.Name, $"claims {here.Name}"); return; }
                var target = s.Systems.Where(z => !z.Claimed && s.ClaimBlock(z) == null).OrderByDescending(z => s.StacksIn(z).Count(x => x.Root.Def.HasTag("colony"))).FirstOrDefault();
                if (target != null && here != target && s.StacksIn(target).FirstOrDefault(x => x.Root.Def.Category == "star") is { } st2)
                    s.StartTravel(cs, st2.Pos + new Vector2(0, Sim.CardH + 40));
            }
            else if (here != null && here != home && !here.Claimed) s.StartTravel(cs, home.Center);
        }
        // Colonise: a Colony Ship flies to an unclaimed habitable planet in a system we own.
        if (!s.Techs.Contains("tech_colonization")) return;
        var spot = s.Systems.Where(z => z.Claimed).SelectMany(z => s.StacksIn(z)).FirstOrDefault(x => x.Cards.Count == 1 && x.Root.Def.HasTag("colony") && !x.Root.Claimed);
        if (spot == null) return;
        if (Lone(s, c => c.Def.Id == "colony_ship") is { } ship)
        {
            if (s.SystemAt(Sim.CardCenter(ship)) == s.SystemAt(Sim.CardCenter(spot))) { s.StackOnto(ship, spot); mark("colony", "colonises a planet"); }
            else s.StartTravel(ship, spot.Pos + new Vector2(Sim.CardW + 30, 0));
        }
        else if (Mine(s).FirstOrDefault(x => x.Root.Def.Id == "shipyard" && x.Order == null && x.Cards.Count == 1) is { } yard
                 && s.AllCards.Count(c => c.Def.HasTag("worker")) >= 6 && s.Shortfall(R["s_colony"]) == null && Lone(s, c => c.Def.HasTag("worker")) is { } colonist)
        {
            s.SetOrder(yard, R["s_colony"]);
            s.StackOnto(colonist, yard);
        }
    }

    /// <summary>A Dyson Sphere (or Matter Decompressor, Ring World) on the home star once researched and affordable.</summary>
    static void Megastructures(Sim s, Action<string, string> mark)
    {
        foreach (var id in new[] { "dyson_sphere", "matter_decompressor", "ring_world" })
        {
            var r = Defs.Recipes.First(x => x.Id == "b_" + id);
            if (!s.TechOk(r) || s.AllCards.Any(c => c.Def.Id == id) || s.Shortfall(r) != null) continue;
            var cs = Mine(s).FirstOrDefault(x => x.Cards.Count == 1 && x.Root.Def.Id == "construction_ship" && !Busy(x) && s.SystemAt(Sim.CardCenter(x)) == s.Home);
            var star = s.StacksIn(s.Home).FirstOrDefault(x => x.Root.Def.Category == "star" && x.Cards.Count == 1);
            if (cs == null || star == null) return;
            s.StackOnto(cs, star);
            s.SetOrder(star, r);
            mark(id, $"builds a {id.Replace('_', ' ')}");
            return;
        }
    }

    /// <summary>Meet the neighbours, make peace when attacked, and turn a much weaker empire into a tributary.</summary>
    static void Diplomacy(Sim s, Action<string, string> mark)
    {
        if (Lone(s, c => c.Def.Id == "envoy") is { } envoy && s.Empires.Any(e => !e.Contacted))
        {
            // The Science Ship usually sits on a star: take it off for the contact.
            var sci = Lone(s, c => c.Def.Id == "science_ship")
                      ?? Mine(s).Where(x => x.Active?.Id != "x_survey" && x.Cards.Any(c => c.Def.Id == "science_ship")).Select(x => Lift(s, x.Cards.First(c => c.Def.Id == "science_ship"))).FirstOrDefault();
            if (sci != null && s.SystemAt(Sim.CardCenter(sci)) == s.SystemAt(Sim.CardCenter(envoy))) s.StackOnto(envoy, sci);
        }
        foreach (var e in s.Empires.Where(x => x.Contacted))
        {
            if (e.Contacted) mark("contact" + e.Def.Id, $"meets the {e.Def.Name}");
            if (e.Status == "war" && e.TheyDeclared) { if (s.OfferPeace(e) == null) mark("peace" + s.Moon, $"makes peace with the {e.Def.Name}"); continue; }
            if (e.Status != "peace" || s.Moon < 10 || s.War != null) continue;
            int mine = s.PlayerStrength, theirs = s.EmpireStrength(e);
            var fleet = Mine(s).Where(x => Sim.Warships(x) > 0 && x.Cards.All(c => c.Def.HasTag("warship"))).OrderByDescending(Sim.FleetStrength).FirstOrDefault();
            if (fleet == null || Sim.FleetStrength(fleet) < theirs * 0.9f || mine < theirs * 1.5f) continue;
            s.DeclareWar(e, "tributary");
            if (s.StartInvasion(fleet, e) != null) continue;
            // Time stands still at home: fight it out now, system by system (capital last), then come home.
            for (int guard = 0; guard < 6 && s.War != null && !s.War.Won; guard++)
            {
                int target = e.Systems.FindIndex(x => !x.Occupied && !x.Capital);
                if (target < 0 || s.War.Fleet.Count > 3) target = e.Systems.FindIndex(x => x.Capital && !x.Occupied);
                if (target < 0 || s.AttackSystem(target) != null) break;
                for (float t = 0; s.War?.Fight != null && t < 300; t += 0.1f) s.UpdateWar(0.1f);
            }
            bool won = s.War?.Won == true;
            s.EndInvasion();
            mark("war" + e.Def.Id + s.Moon, won ? $"makes the {e.Def.Name} a tributary" : $"invades the {e.Def.Name} and fails");
            if (!won && e.Status == "war") s.OfferPeace(e);
        }
    }

    static void Think(Sim s, Action<string, string> mark)
    {
        var home = s.Home;
        int people = s.AllCards.Count(c => c.Def.Category == "person");
        int eat = s.AllCards.Where(c => c.Def.Category == "person").Sum(c => c.Def.FoodUpkeep);
        bool HasCard(string id) => s.AllCards.Any(c => c.Def.Id == id);
        int CountCard(string id) => s.AllCards.Count(c => c.Def.Id == id);

        // Sell blueprints already known; research the rest with idle Scientists, then Pops.
        foreach (var bp in Mine(s).Where(x => x.Cards.Count == 1 && x.Root.Def.Category == "tech").ToList())
        {
            if (s.Techs.Contains(bp.Root.Def.Id)) { s.Sell(bp); continue; }
            var r = Defs.Recipes.First(x => x.Effect == "learn" && x.Station == bp.Root.Def.Id);
            if (r.RequiresTech != "none" && !s.Techs.Contains(r.RequiresTech)) continue;
            if (s.Shortfall(r) != null) continue; // research only what the pool can pay for now
            var who = Lone(s, c => c.Def.Id == "scientist") ?? Lone(s, c => c.Def.HasTag("worker") && c.Def.HasTag("researcher"));
            // No one free: take a Pop off mining or energy work (never off the farms).
            if (who == null && Mine(s).Where(x => x.Root.Def.Yield is "minerals" or "energy" && !Sim.Costs(r).Any(i => i.Card == x.Root.Def.Yield) && x.Cards.Any(c => c.Def.HasTag("researcher") && c.Def.HasTag("worker"))).OrderBy(x => x.Root.Def.Yield == "energy" ? 1 : 0).FirstOrDefault() is { } job && s.Have("minerals") >= 6)
                who = Lift(s, job.Cards.First(c => c.Def.HasTag("researcher") && c.Def.HasTag("worker")));
            if (who != null) s.StackOnto(who, bp);
        }

        // Workers: food first, then research when there are blueprints to pay for, alloys once ships can be built, then minerals and energy.
        Stack? Free(string id) => Mine(s).FirstOrDefault(x => x.Root.Def.Id == id && !x.Cards.Any(c => c.Def.HasTag("worker")) && x.Order == null);
        Stack? FreeYield(Func<CardDef, bool> f) => Mine(s).FirstOrDefault(x => f(x.Root.Def) && x.Root.Def.HasTag("workplace") && (!x.Root.Def.IsPlanet || x.Root.Claimed)
            && !x.Cards.Any(c => c.Def.HasTag("worker")) && x.Cards.Count == 1 && s.SystemAt(Sim.CardCenter(x))?.Claimed == true);
        float foodRate = Mine(s).Where(x => x.Active?.Id == "w_yield" && x.ActiveStation?.Def.Yield == "food").Sum(x => 90f / Math.Max(1, x.Duration));
        for (int guard = 0; guard < 6; guard++)
        {
            var w = Lone(s, c => c.Def.HasTag("worker"));
            if (w == null) break;
            bool needFood = eat > 0 && (foodRate < eat + 3 || s.Have("food") < eat);
            bool needResearch = Mine(s).Any(x => x.Root.Def.Category == "tech" && x.Cards.Count > 1) && s.Have("research") < 12;
            bool needAlloys = HasCard("shipyard") && s.Have("alloys") < 12 && s.Have("minerals") >= 2;
            bool needEnergy = s.Have("energy") < s.StructureUpkeep + s.AllCards.Sum(c => c.Def.Category == "person" ? c.Def.EnergyUpkeep : 0) + 4;
            Stack? to = needFood ? FreeYield(d => d.Yield == "food") : null;
            to ??= needEnergy ? FreeYield(d => d.Yield == "energy") : null;
            to ??= needResearch ? FreeYield(d => d.Yield == "research") : null;
            to ??= needAlloys ? Free("alloy_foundry") : null;
            to ??= s.Have("minerals") < 15 ? FreeYield(d => d.Yield == "minerals") : null;
            to ??= needResearch && s.Have("minerals") >= 8 ? FreeYield(d => d.Yield == "research") : null;
            to ??= FreeYield(d => d.Yield == "energy");
            to ??= FreeYield(d => d.Yield is "research" or "food" or "minerals" or "unity" or "consumer_goods");
            to ??= Free("alloy_foundry");
            if (to == null || s.SystemAt(Sim.CardCenter(to)) != s.SystemAt(Sim.CardCenter(w))) break;
            if (to.Root.Def.Yield == "food") foodRate += 90f / to.Root.Def.YieldTime;
            s.StackOnto(w, to);
        }
        // Grow: two Pops on a City District when Food allows; once the Baby is made they go back to work.
        foreach (var nursery in Mine(s).Where(x => x.Cards.Any(Sim.GrowsBabies) && x.Cards.Any(c => c.Def.HasTag("baby"))).ToList())
            foreach (var p in nursery.Cards.Where(c => c.Def.Id == "pop").ToList()) Lift(s, p);
        if (Mine(s).FirstOrDefault(x => x.Root.Def.Id == "city_district" && x.Cards.Count < 3 && x.Cards.All(c => c.Def.Id is "city_district" or "pop")) is { } city && s.Have("food") >= eat / 2 + 2)
            for (int i = city.Cards.Count - 1; i < 2; i++)
                if (Lone(s, c => c.Def.Id == "pop") is { } p) s.StackOnto(p, city);
                else if (Mine(s).FirstOrDefault(x => x != city && x.Root.Def.Yield is "energy" or "minerals" or "research" or "unity" && x.Cards.Any(c => c.Def.Id == "pop")) is { } job)
                    s.StackOnto(Lift(s, job.Cards.First(c => c.Def.Id == "pop")), city);

        // Construction Ship orders: farms to feed everyone, then the core buildings.
        foreach (var cs in Mine(s).Where(x => x.Cards.Count == 1 && x.Root.Def.Id == "construction_ship" && !Busy(x)).ToList())
        {
            int farms = CountCard("agriculture_district");
            var wants = new List<string>();
            if (farms * 90f / Defs.Card["agriculture_district"].YieldTime * s.Mult("food") < eat + 3 && s.Ethic.WorkerCard == "pop") wants.Insert(0, "b_agriculture");
            if (CountCard("mining_district") < 1 + s.AllCards.Count(c => c.Def.HasTag("worker")) / 5) wants.Add("b_mining");
            if (!HasCard("city_district") && s.Ethic.WorkerCard == "pop") wants.Add("b_city");
            if (!HasCard("shipyard")) wants.Add("b_shipyard");
            if (!HasCard("alloy_foundry")) wants.Add("b_foundry");
            if (!HasCard("research_lab")) wants.Add("b_lab");
            if (CountCard("generator_district") < 1 + s.StructureUpkeep / 7) wants.Insert(0, "b_generator");
            if (s.Techs.Contains("tech_science_nexus") && !HasCard("science_nexus")) wants.Add("b_science_nexus");
            if (CountCard("mining_district") < 2) wants.Add("b_mining");
            if (CountCard("alloy_foundry") < 2) wants.Add("b_foundry");
            if (!HasCard("starbase")) wants.Add("b_starbase");
            // The first wanted building the pool can pay for now.
            var r = wants.Select(id => Defs.Recipes.First(x => x.Id == id)).FirstOrDefault(x => s.Shortfall(x) == null);
            if (r != null) { s.SetOrder(cs, r); mark("build" + r.Id, $"orders {r.Id}"); }
        }

        // Science Ship: survey, then study the home star for Research.
        if (Lone(s, c => c.Def.Id == "science_ship") is { } sci && s.SystemAt(Sim.CardCenter(sci)) == home)
        {
            if (Lone(s, c => c.Def.Id is "uncharted_system" or "guardian_signal") is { } u) s.StackOnto(u, sci);
            else if (s.StacksIn(home).FirstOrDefault(x => x.Root.Def.Category == "star" && x.Cards.Count == 1) is { } star) s.StackOnto(sci, star);
        }
        if (Lone(s, c => c.Def.Id == "scientist") is { } sc && Lone(s, c => c.Def.Id is "anomaly" or "precursor_artifact") is { } an) s.StackOnto(an, sc);
        if (Lone(s, c => c.Def.Id == "derelict_ship") is { } der && Mine(s).FirstOrDefault(x => x.Cards.Count == 1 && x.Root.Def.Id == "construction_ship" && !Busy(x)) is { } cs2)
            s.StackOnto(der, cs2);
        Expand(s, mark);
        Megastructures(s, mark);
        Diplomacy(s, mark);
        // Infinite research once every blueprint is known.
        if (s.AllBlueprintsKnown && Mine(s).FirstOrDefault(x => x.Root.Def.Id == "research_lab" && x.Order == null && x.Cards.Any(c => c.Def.HasTag("researcher"))) is { } lab)
        {
            var reps = Defs.Recipes.Where(r => r.Id.StartsWith("rr_")).ToList();
            s.SetOrder(lab, reps[s.Rng.Next(reps.Count)]);
            mark("rep", "starts infinite research");
        }
        // Machines grow by building Drones.
        if (Mine(s).FirstOrDefault(x => x.Root.Def.Id == "robot_assembly" && x.Order == null) is { } plant && s.Have("alloys") >= 2 && s.Have("energy") > s.AllCards.Count(c => c.Def.Id == "drone") + 2)
            s.SetOrder(plant, Defs.Recipes.First(r => r.Id == "w_drone"));

        // Packs: blueprints while there's research to do, then the military once ships can be built.
        int blueprints = Mine(s).Count(x => x.Root.Def.Category == "tech");
        int warshipsNow = s.AllCards.Count(c => c.Def.HasTag("warship"));
        int workers = s.AllCards.Count(c => c.Def.HasTag("worker"));
        bool needHull = s.NextHull is { } nh && !HasCard(nh) && HasCard("shipyard");
        string pack = workers < 5 && s.Ethic.WorkerCard == "pop" ? "pack_society" : needHull && s.Moon % 2 == 1 ? "pack_military" : s.Moon >= 4 && !s.Empires.Any(e => e.Contacted) && !HasCard("envoy") && s.Moon % 2 == 0 ? "pack_exploration" : blueprints < 2 ? "pack_research"
            : HasCard("shipyard") && warshipsNow < s.FleetSize ? "pack_military" : s.Rng.Next(2) == 0 ? "pack_research" : "pack_industry";
        int upkeep = s.AllCards.Where(c => c.Def.Category == "person").Sum(c => c.Def.EnergyUpkeep) + s.StructureUpkeep;
        foreach (var surplus in new[] { "minerals", "food", "research", "alloys", "unity", "consumer_goods" }) if (s.Have(surplus) > 30) s.SellResource(surplus, 10);
        if (s.Have("energy") >= s.PackCost(Defs.Pack[pack]) + 3 + upkeep) { s.BuyPack(Defs.Pack[pack], home.Center); mark("pack", "first pack bought"); }

        // Shipyard: warships up to two fleets, then parts for them.
        int warships = s.AllCards.Count(c => c.Def.HasTag("warship"));
        foreach (var yard in Mine(s).Where(x => x.Root.Def.Id == "shipyard" && !Busy(x) && x.Cards.Count == 1).ToList())
        {
            var hulls = new[] { "s_titan", "s_battleship", "s_cruiser", "s_destroyer", "s_corvette" };
            RecipeDef? pick = null;
            // The best hull researched; build it while there's room, or to replace the weakest ship (sold).
            var best = hulls.Select(id => Defs.Recipes.First(x => x.Id == id)).FirstOrDefault(r => r.RequiresTech == "none" || s.Techs.Contains(r.RequiresTech));
            var weakest = s.AllCards.Where(c => c.Def.HasTag("warship") && c.Stack != null).OrderBy(c => c.Def.Value).FirstOrDefault();
            if (best != null && s.Shortfall(best) == null)
            {
                var hull = Defs.Card[best.Outputs[0].Give[0].Card];
                if (warships < s.FleetSize * 2) pick = best;
                else if (weakest != null && weakest.Def.Value * 2 < hull.Value) { pick = best; s.Sell(Lift(s, weakest)); }
            }
            int slots = s.AllCards.Where(c => c.Def.HasTag("warship")).Sum(c => c.Def.Slots - c.Parts.Count);
            if (pick == null && slots > 0)
                pick = Defs.Recipes.Where(r => r.Id.StartsWith("s_") && Defs.Component.ContainsKey(r.Id[2..]) && s.Techs.Contains(r.RequiresTech) && s.Shortfall(r) == null)
                    .OrderByDescending(r => Defs.Card[r.Id[2..]].Value).FirstOrDefault();
            if (pick != null) { s.SetOrder(yard, pick); mark("yard" + pick.Id, $"orders {pick.Id}"); }
        }
        foreach (var part in Mine(s).Where(x => x.Cards.Count == 1 && x.Root.Def.Category == "component").ToList())
        {
            var host = s.AllCards.Where(c => c.Def.HasTag("warship") && c.Battle == null && c.Def.Slots > c.Parts.Count
                                             && Defs.Component[part.Root.Def.Id].MinSlots <= c.Def.Slots).OrderByDescending(c => c.Def.Slots).FirstOrDefault();
            if (host != null && s.Fit(part.Root, host) == null) mark("fit", "first part fitted");
        }

        // Fleets: merge lone warships into the biggest fleet with room; admirals take command.
        foreach (var ship in Mine(s).Where(x => x.Cards.Count == 1 && x.Root.Def.HasTag("warship")).ToList())
        {
            if (!s.Table.Stacks.Contains(ship)) continue;
            var fleet = Mine(s).Where(x => x != ship && Sim.Warships(x) > 0 && x.Cards.All(c => c.Def.HasTag("warship")) && s.CanStack(ship, x))
                .OrderByDescending(Sim.Warships).FirstOrDefault();
            if (fleet != null) s.StackOnto(ship, fleet);
        }
        if (Lone(s, c => c.Def.Id == "admiral") is { } adm && Mine(s).FirstOrDefault(x => Sim.Warships(x) > 0 && !Sim.HasAdmiral(x)) is { } f2)
            if (s.AssignAdmiral(adm.Root, f2.Cards.First(c => c.Def.HasTag("warship"))) == null) mark("admiral", "admiral commands a fleet");

        // Combat: fleets attack hostiles in the capital when strong enough (always against the crisis).
        var fleets = Mine(s).Where(x => Sim.Warships(x) > 0 && x.Cards.All(c => c.Def.HasTag("warship"))).OrderByDescending(x => x.Cards.Sum(c => c.MaxHp)).ToList();
        foreach (var h in s.StacksIn(home).Where(x => x.HasHostile).ToList())
        {
            var foe = h.Cards.First(c => c.Def.IsHostile);
            if (foe.Def.HasTag("guardian") || foe.Def.Id == s.Crisis.RiftCard) continue; // leave the rift: killing it brings the boss early
            float power = Sim.Strength(fleets.SelectMany(f => f.Cards.Where(c => c.Def.HasTag("warship"))), 1.2f);
            float threat = Sim.Threat(foe);
            if (fleets.Count > 0 && (power > threat * 1.3f || (s.BossArrived && foe.Def.Id == s.Crisis.BossCard && power > threat * 0.8f)))
            {
                foreach (var f in fleets.Where(x => s.Table.Stacks.Contains(x))) s.Attack(f, foe);
                mark("fight" + foe.Def.Id, $"attacks {foe.Def.Id} with {fleets.Sum(Sim.Warships)} warships");
                break;
            }
        }
    }

}
