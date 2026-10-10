using System.Numerics;

namespace GrandGalactic;

/// <summary>A scripted player for --playtest: plays whole runs with only the moves a person can make (move cards,
/// click orders, buy packs, sell, fit, attack) and logs how the run went, to find dead ends and pacing problems.</summary>
public static class Playtest
{
    public sealed record Report(string Ethic, int Seed, RunState State, string End, int Moon, float Seconds, List<string> Timeline);

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

    public static Report Play(EthicDef ethic, int seed, DifficultyDef diff)
    {
        var s = new Sim(ethic, seed, null, diff);
        var log = new List<string>();
        var seen = new HashSet<string>();
        void Mark(string key, string text) { if (seen.Add(key)) log.Add($"moon {s.Moon,2} {s.MoonTime,3:0}s: {text}"); }
        float t = 0, think = 0;
        int lastMoon = 1;
        while (s.State == RunState.Playing && t < 60 * 60 && s.Moon <= diff.CrisisMoon + diff.BossDelayMoons + 8)
        {
            s.Update(0.1f);
            t += 0.1f;
            foreach (var m in s.Messages)
            {
                if (m.Contains("starved")) log.Add($"moon {s.Moon,2}: {m}");
                if (m.StartsWith("Researched")) Mark("r" + m, m.Split('!')[0]);
                if (m.StartsWith("Act ")) Mark(m[..5], m.Split('.')[0]);
                if (m.Contains("has arrived")) { Mark("boss", m); log.Add("fleet at boss: " + string.Join(", ", s.AllCards.Where(c => c.Def.HasTag("warship")).GroupBy(c => c.Def.Id + "(" + c.Parts.Count + " parts)").Select(g => $"{g.Count()} {g.Key}"))); }
                if (m.Contains("Raiders")) log.Add($"moon {s.Moon,2}: raid");
                if (m.Contains("lost in battle")) log.Add($"moon {s.Moon,2}: {m}");
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
        return new Report(ethic.Id, seed, s.State, s.EndReason, s.Moon, t, log);
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
            bool needFood = eat > 0 && foodRate < eat + 0.5f;
            bool needResearch = Mine(s).Any(x => x.Root.Def.Category == "tech" && x.Cards.Count > 1) && s.Have("research") < 12;
            bool needAlloys = HasCard("shipyard") && s.Have("alloys") < 12 && s.Have("minerals") >= 2;
            Stack? to = needFood ? FreeYield(d => d.Yield == "food") : null;
            to ??= needResearch ? FreeYield(d => d.Yield == "research") : null;
            to ??= needAlloys ? Free("alloy_foundry") : null;
            to ??= s.Have("minerals") < 15 ? FreeYield(d => d.Yield == "minerals") : null;
            to ??= FreeYield(d => d.Yield == "energy");
            to ??= FreeYield(d => d.Yield is "research" or "food" or "minerals" or "unity" or "consumer_goods");
            to ??= Free("alloy_foundry");
            if (to == null) break;
            if (to.Root.Def.Yield == "food") foodRate += 90f / to.Root.Def.YieldTime;
            s.StackOnto(w, to);
        }
        // Grow: two Pops on a City District when Food is comfortable.
        if (Mine(s).FirstOrDefault(x => x.Root.Def.Id == "city_district" && x.Cards.Count == 1) is { } city && s.Have("food") > eat * 2 + 4)
            for (int i = 0; i < 2; i++)
                if (Lone(s, c => c.Def.Id == "pop") is { } p) s.StackOnto(p, city);
                else if (Mine(s).FirstOrDefault(x => x.Root.Def.Id == "homeworld" && x.Cards.Any(c => c.Def.Id == "pop")) is { } hw)
                    s.StackOnto(Lift(s, hw.Cards.First(c => c.Def.Id == "pop")), city);

        // Construction Ship orders: farms to feed everyone, then the core buildings.
        foreach (var cs in Mine(s).Where(x => x.Cards.Count == 1 && x.Root.Def.Id == "construction_ship" && !Busy(x)).ToList())
        {
            int farms = CountCard("agriculture_district");
            var wants = new List<string>();
            if (farms * 90f / Defs.Card["agriculture_district"].YieldTime < eat + 1 && s.Ethic.WorkerCard == "pop") wants.Add("b_agriculture");
            if (!HasCard("shipyard")) wants.Add("b_shipyard");
            if (!HasCard("alloy_foundry")) wants.Add("b_foundry");
            if (!HasCard("research_lab")) wants.Add("b_lab");
            if (!HasCard("city_district") && s.Ethic.WorkerCard == "pop") wants.Add("b_city");
            if (CountCard("generator_district") < 2) wants.Add("b_generator");
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
        // Clutter goes to the Market.
        foreach (var junk in Mine(s).Where(x => x.Cards.Count == 1 && x.Root.Def.Category == "planet" && s.SystemAt(Sim.CardCenter(x)) == home && !x.Root.Claimed).ToList())
            s.Sell(junk);

        // Packs: blueprints while there's research to do, then the military once ships can be built.
        int blueprints = Mine(s).Count(x => x.Root.Def.Category == "tech");
        int warshipsNow = s.AllCards.Count(c => c.Def.HasTag("warship"));
        int workers = s.AllCards.Count(c => c.Def.HasTag("worker"));
        string pack = workers < 5 && s.Ethic.WorkerCard == "pop" ? "pack_society" : blueprints < 2 ? "pack_research"
            : HasCard("shipyard") && warshipsNow < s.FleetSize ? "pack_military" : s.Rng.Next(2) == 0 ? "pack_research" : "pack_industry";
        int upkeep = s.AllCards.Where(c => c.Def.Category == "person").Sum(c => c.Def.EnergyUpkeep);
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
            float power = fleets.Sum(f => f.Cards.Sum(c => c.Guns.Sum(g => g.Damage / g.Cooldown)) * f.Cards.Sum(c => c.MaxHp + c.MaxShield + c.MaxArmor));
            float threat = foe.Guns.Sum(g => g.Damage / g.Cooldown) * (foe.MaxHp + foe.MaxShield + foe.MaxArmor);
            bool crisis = foe.Def.Category == "boss" || foe.Def.HasTag("crisis");
            if (fleets.Count > 0 && (power > threat * 1.5f || (crisis && s.BossArrived && foe.Def.Id == s.Crisis.BossCard)))
            {
                foreach (var f in fleets.Where(x => s.Table.Stacks.Contains(x))) s.Attack(f, foe);
                mark("fight" + foe.Def.Id, $"attacks {foe.Def.Id} with {fleets.Sum(Sim.Warships)} warships");
                break;
            }
        }
    }

}
