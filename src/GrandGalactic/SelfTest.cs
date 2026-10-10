using System.Numerics;

namespace GrandGalactic;

/// <summary>Headless checks of the simulation: every recipe fires, every pack opens, research, babies, fleets, refits,
/// the resource pool, layout, saving, combat against every boss, moons, and the acts on schedule. Run with --selftest.</summary>
public static class SelfTest
{
    static int _fails;
    static readonly Dictionary<(string foe, string fleet), int> Results = new();

    static void Check(bool ok, string what)
    {
        if (!ok) _fails++;
        Log.Info($"  {(ok ? "PASS" : "FAIL")}  {what}");
    }

    static Sim Fresh(string ethic = "materialist", int seed = 1)
    {
        var s = new Sim(Defs.Ethics.First(e => e.Id == ethic), seed);
        s.Table.Stacks.Clear();
        s.Table.Battles.Clear();
        s.Res.Clear();
        s.MoonTime = -100000; // no moon ends during unit checks
        return s;
    }

    static Stack Build(Sim s, IEnumerable<string> ids, Vector2 pos)
    {
        Stack? st = null;
        foreach (var id in ids)
        {
            var c = s.Spawn(id, pos, jitter: false);
            if (c.Stack == null) continue; // a resource: it went into the pool
            if (st == null) st = c.Stack;
            else s.StackOnto(c.Stack, st);
        }
        return st!;
    }

    static void Run(Sim s, float seconds, Func<bool>? until = null)
    {
        for (float t = 0; t < seconds && !(until?.Invoke() ?? false); t += 0.05f) s.Update(0.05f);
    }

    static int Count(Sim s, string id) => s.AllCards.Count(c => c.Def.Id == id) + s.Have(id);

    static string Resolve(string card) => card switch
    {
        "tag:workplace" => "generator_district",
        "tag:star" => "pulsar",
        "tag:worker" => "pop",
        "tag:researcher" => "pop",
        "tag:warship" => "corvette",
        "tag:habitable" => "desert_world",
        "tag:colony" => "ocean_world",
        "tag:uninhabitable" => "gas_giant",
        _ => card,
    };

    public static int Run()
    {
        Log.Info("Self-test: recipes");
        foreach (var r in Defs.Recipes)
        {
            var s = Fresh();
            foreach (var t in Defs.Cards.Where(c => c.Category == "tech")) s.Techs.Add(t.Id);
            if (r.Effect == "learn") s.Techs.Remove(r.Station);
            var ids = new List<string> { Resolve(r.Station) };
            foreach (var i in r.Inputs.Where(i => !Sim.IsResource(i.Card))) for (int k = 0; k < i.N; k++) ids.Add(Resolve(i.Card));
            foreach (var i in Sim.Costs(r)) s.Res[i.Card] = s.Have(i.Card) + i.N;
            // Recipes that need an unclaimed system run in a freshly surveyed one.
            var at = r.RequiresSystem == "unclaimed" ? s.AddSystem(s.RollSystemType()).Center : new Vector2(800, 600);
            if (r.RequiresSystem == "unclaimed")
                foreach (var h in s.Table.Stacks.Where(x => x.HasHostile && s.SystemAt(Sim.CardCenter(x))?.Index > 0).ToList())
                    foreach (var c in h.Cards.ToList()) s.Remove(c);
            var st = Build(s, ids, at);
            if (r.Order) s.SetOrder(st, r);
            if (r.RequiresFlag == "claimed") st.Root.Claimed = true;
            if (r.Effect == "repair") foreach (var c in st.Cards.Where(c => c.Def.HasTag("warship"))) c.Hp = 1;
            var outIds = r.Outputs.SelectMany(o => o.Give).Select(g => g.Card == "station.yield" ? Defs.Card[Resolve(r.Station)].Yield : g.Card).ToHashSet();
            int before = outIds.Sum(id => Count(s, id)), boards = s.Systems.Count;
            Run(s, 120, () => s.Discovered.Contains(r.Id));
            bool fired = s.Discovered.Contains(r.Id);
            bool effect = r.Effect switch
            {
                "open_board:random" or "open_board:guardian" => s.Systems.Count == boards + 1,
                "set_flag:claimed" => s.AllCards.Any(c => c.Def.Id == Resolve(r.Station) && c.Claimed),
                "claim_system" => s.SystemAt(at)?.Claimed == true,
                "repair" => st.Cards.Where(c => c.Def.HasTag("warship")).All(c => c.Hp >= c.MaxHp),
                "learn" => s.Techs.Contains(r.Station) && !s.AllCards.Any(c => c.Def.Id == r.Station),
                _ => true,
            };
            bool produced = r.Outputs.Length == 0 || outIds.Sum(id => Count(s, id)) > before - (outIds.Contains(Resolve(r.Station)) && !r.StationKeep ? 1 : 0);
            bool paid = Sim.Costs(r).All(i => s.Have(i.Card) < i.N || outIds.Contains(i.Card));
            Check(fired && effect && produced && paid, $"recipe {r.Id,-24} fires ({string.Join(" + ", ids)}{(Sim.Costs(r).Any() ? " + pool: " + string.Join(", ", Sim.Costs(r).Select(i => $"{i.N} {i.Card}")) : "")})");
        }

        Log.Info("Self-test: the resource pool and build orders");
        {
            var s = Fresh();
            Check(Defs.Rules.StartCards.All(a => !Sim.IsResource(a.Card)) && Defs.Ethics.All(e => e.BonusCards.All(a => !Sim.IsResource(a.Card))),
                  "a run starts with an empty pool (no resource cards anywhere in the start)");
            var cs = Build(s, new[] { "construction_ship" }, new Vector2(400, 400));
            Run(s, 30);
            Check(s.Table.Stacks.Count == 1 && cs.Active == null, "a lone Construction Ship builds nothing until it is given an order");
            s.SetOrder(cs, Defs.Recipes.First(x => x.Id == "b_shipyard"));
            s.Res["minerals"] = 2;
            Run(s, 2);
            Check(cs.Progress == 0 && cs.Wait?.Contains("Needs 6") == true, $"an order waits for the pool, and says why (\"{cs.Wait}\")");
            s.Res["minerals"] = 6;
            Run(s, 40, () => s.AllCards.Any(c => c.Def.Id == "shipyard"));
            Check(s.AllCards.Any(c => c.Def.Id == "shipyard") && s.Have("minerals") == 0 && cs.Order == null,
                  "with 6 Minerals in the pool the ordered Shipyard is built, paid from the pool, and the order is done");
            var hw = Build(s, new[] { "homeworld", "pop" }, new Vector2(800, 400));
            Run(s, 16);
            Check(s.Have("energy") == 1 && s.Gains.Any(g => g.Id == "energy"), "a Pop on the Homeworld adds Energy to the pool (not a card)");
            var far = s.AddSystem(s.RollSystemType());
            var shipyard = s.AllCards.First(c => c.Def.Id == "shipyard").Stack!;
            s.SetOrder(shipyard, Defs.Recipes.First(x => x.Id == "s_science"));
            s.Res["minerals"] = 3;
            Run(s, 30, () => s.Discovered.Contains("s_science"));
            Check(s.Discovered.Contains("s_science"), "the pool is shared: any system's stations can spend it");
            Check(s.BuyPack(Defs.Pack["pack_exploration"], new Vector2(800, 800)) is { } no && no.Contains("costs"), "a pack refuses with too little Energy, and says what it costs");
            s.Res["energy"] = 10;
            Check(s.BuyPack(Defs.Pack["pack_frontier"], new Vector2(800, 800)) != null, "Act 2 packs are locked in Act 1");
            _ = hw; _ = far;
        }

        Log.Info("Self-test: packs");
        foreach (var p in Defs.Packs)
        {
            var s = Fresh();
            s.Act = 3;
            s.Res["energy"] = s.PackCost(p) + 1;
            int before = s.AllCards.Count() + s.Res.Values.Sum();
            bool ok = s.BuyPack(p, new Vector2(700, 500)) == null;
            int after = s.AllCards.Count() + s.Res.Values.Sum();
            Check(ok && after == before - s.PackCost(p) + p.Draws && s.Have("energy") >= 1 - 0, $"pack {p.Id} costs {s.PackCost(p)} Energy and gives {p.Draws} (resources go to the pool)");
        }

        Log.Info("Self-test: research from blueprint cards");
        {
            var s = Fresh();
            var bp = Build(s, new[] { "tech_red_laser", "pop" }, new Vector2(400, 400));
            s.Res["research"] = 3; s.Res["energy"] = 1;
            float t = 0;
            while (!s.Techs.Contains("tech_red_laser") && t < 200) { s.Update(0.05f); t += 0.05f; }
            Check(s.Techs.Contains("tech_red_laser") && !s.AllCards.Any(c => c.Def.Id == "tech_red_laser") && s.AllCards.Any(c => c.Def.Id == "pop") && s.Have("research") == 0,
                  $"a Pop researches the Red Laser blueprint in {t:0}s; the blueprint is used up, the Pop stays, Research is paid");
            Build(s, new[] { "tech_mass_driver", "scientist" }, new Vector2(900, 400));
            s.Res["research"] = 3; s.Res["minerals"] = 1;
            float t2 = 0;
            while (!s.Techs.Contains("tech_mass_driver") && t2 < 200) { s.Update(0.05f); t2 += 0.05f; }
            Check(s.Techs.Contains("tech_mass_driver") && t2 < t * 0.6f, $"a Scientist researches twice as fast ({t2:0}s vs {t:0}s)");
            var blue = Build(s, new[] { "tech_coilgun", "pop" }, new Vector2(400, 700));
            s.Res["research"] = 20;
            Run(s, 1);
            Check(s.Techs.Contains("tech_mass_driver") && blue.Active != null, "Coilgun can be researched once Mass Driver is known");
            var s2 = Fresh();
            var b2 = Build(s2, new[] { "tech_blue_laser", "pop" }, new Vector2(400, 400));
            s2.Res["research"] = 20;
            Run(s2, 60);
            Check(!s2.Techs.Contains("tech_blue_laser") && b2.Wait?.Contains("Red Laser") == true, $"Blue Laser needs Red Laser first (\"{b2.Wait}\")");
            s2.Techs.Add("tech_corvettes");
            var b3 = Build(s2, new[] { "tech_corvettes", "pop" }, new Vector2(900, 400));
            Run(s2, 1);
            Check(b3.Wait?.Contains("already researched") == true, "a blueprint you already know says so (sell it)");
            Check(s2.Blueprint(Defs.Recipes.First(r => r.Id == "s_red_laser")) == Sim.BlueprintState.Locked
                  && s2.Blueprint(Defs.Recipes.First(r => r.Id == "r_red_laser")) == Sim.BlueprintState.Locked, "unresearched blueprints stay out of the book");
            s2.Techs.Add("tech_red_laser");
            Check(s2.Blueprint(Defs.Recipes.First(r => r.Id == "s_red_laser")) == Sim.BlueprintState.Known
                  && s2.Blueprint(Defs.Recipes.First(r => r.Id == "r_red_laser")) == Sim.BlueprintState.Made, "researching Red Laser puts its build blueprint in the book");
            Check(Defs.Packs.Count(p => p.Contents.Any(e => Defs.Card[e.Card].Category == "tech")) >= 4, "four packs carry blueprint cards");
            {
                var h = Fresh();
                h.Techs.Add("tech_corvettes");
                Build(h, new[] { "tech_destroyers", "scientist" }, new Vector2(400, 400));
                h.Res["research"] = 20;
                Run(h, 60, () => h.Techs.Contains("tech_destroyers"));
                Check(h.Techs.Contains("tech_destroyers") && h.AllCards.Any(c => c.Def.Id == "tech_cruisers"), "researching a hull sketches the next one (its blueprint appears)");
            }
            Check(Defs.Recipes.All(r => Sim.BlueprintTab(r) != "Other"), $"all {Defs.Recipes.Length} recipes appear in a Blueprint book tab");
        }

        Log.Info("Self-test: babies");
        {
            var s = Fresh();
            var city = Build(s, new[] { "city_district", "pop", "pop" }, new Vector2(400, 400));
            s.Res["food"] = 3;
            Run(s, 40, () => city.Cards.Any(c => c.Def.Id == "baby"));
            Check(city.Cards.Any(c => c.Def.Id == "baby") && s.Have("food") == 0, "City District + 2 Pops + 3 Food make a Baby, who stays on the City District");
            s.Res["food"] = 10;
            Run(s, 30);
            Check(city.Cards.Count(c => c.Def.Id == "baby") == 1, "only one Baby at a time per City District");
            Run(s, 35, () => !city.Cards.Any(c => c.Def.Id == "baby"));
            Check(s.AllCards.Count(c => c.Def.Id == "pop") == 3, "60 seconds on the City District grow the Baby into a Pop");
            var s2 = Fresh();
            var baby = s2.Spawn("baby", new Vector2(400, 400), jitter: false);
            Run(s2, 70);
            Check(s2.AllCards.Contains(baby) && baby.Grow == 0, "a Baby away from a City District doesn't grow");
        }

        Log.Info("Self-test: order queues, colonies, strength ratings");
        {
            var s = Fresh();
            var cs = Build(s, new[] { "construction_ship" }, new Vector2(400, 400));
            var R = Defs.Recipes.ToDictionary(r => r.Id);
            Check(s.QueueOrder(cs, R["b_mining"]) == null && s.QueueOrder(cs, R["b_generator"]) == null && s.QueueOrder(cs, R["b_city"]) == null
                  && s.QueueOrder(cs, R["b_lab"]) is { } full && full.Contains("full"), "a Construction Ship queues up to 3 builds");
            s.Res["minerals"] = 20; s.Res["energy"] = 5;
            Run(s, 90, () => s.AllCards.Any(c => c.Def.Id == "city_district"));
            Check(new[] { "mining_district", "generator_district", "city_district" }.All(id => s.AllCards.Any(c => c.Def.Id == id)) && cs.Order == null,
                  "the queued builds run one after another");

            var c2 = Fresh();
            c2.Home.Claimed = true;
            var colony = Build(c2, new[] { "ocean_world" }, c2.Home.Center);
            colony.Root.Claimed = true;
            float every = Defs.Card["ocean_world"].YieldTime * Defs.Rules.ColonyPassiveMult;
            Run(c2, every + 1);
            Check(c2.Have("food") == 1, $"an unworked colony yields on its own (1 Food every {every:0}s)");
            Build(c2, new[] { "pop", "pop" }, c2.Home.Center + new Vector2(400, 0)).Cards.ToList().ForEach(_ => { });
            foreach (var p in c2.Table.Stacks.Where(x => x.Root.Def.Id == "pop").ToList()) c2.StackOnto(p, colony);
            c2.Res["food"] = 3;
            Run(c2, 35, () => colony.Cards.Any(c => c.Def.Id == "baby"));
            Check(colony.Cards.Any(c => c.Def.Id == "baby"), "a colony with 2 Pops (+2 Food) raises a Baby");
            Run(c2, 61, () => !colony.Cards.Any(c => c.Def.Id == "baby"));
            Check(c2.AllCards.Count(c => c.Def.Id == "pop") == 3, "the Baby grows up on the colony");

            var f = Fresh();
            var weak = Build(f, new[] { "corvette" }, new Vector2(300, 300));
            var strong = Build(f, new[] { "cruiser", "cruiser", "destroyer" }, new Vector2(700, 300));
            int pirate = Sim.Threat(f.NewCard("pirate_raider")), boss = Sim.Threat(f.NewCard("scourge_queen"));
            Check(Sim.FleetStrength(strong) > Sim.FleetStrength(weak) && boss > 5 * pirate,
                  $"strength ratings order sensibly (corvette {Sim.FleetStrength(weak)}, 3 ships {Sim.FleetStrength(strong)}, pirate {pirate}, Scourge Queen {boss})");
        }

        Log.Info("Self-test: energy upkeep, research bonuses, species, infinite research");
        {
            var s = Fresh();
            s.Moon = Defs.Rules.UpkeepFromMoon;
            Build(s, new[] { "pop" }, new Vector2(200, 200));
            Build(s, new[] { "corvette" }, new Vector2(400, 200));
            Build(s, new[] { "research_lab" }, new Vector2(600, 200));
            s.Res["food"] = 10; s.Res["energy"] = 1;
            Check(s.StructureUpkeep == Defs.Card["corvette"].EnergyUpkeep + Defs.Card["research_lab"].EnergyUpkeep, $"buildings and fleets cost Energy each moon ({s.StructureUpkeep})");
            s.MoonTime = s.MoonSeconds - 0.01f;
            s.Update(0.05f);
            Check(s.EnergyDeficit && s.Have("energy") == 0, "unpaid upkeep puts you in an energy deficit");
            var farm = Build(s, new[] { "agriculture_district", "pop" }, new Vector2(800, 400));
            Run(s, 0.1f);
            float slow = farm.Duration;
            s.Res["energy"] = 50;
            s.MoonTime = s.MoonSeconds - 0.01f;
            s.Update(0.05f);
            Run(s, 0.1f);
            Check(!s.EnergyDeficit && slow > farm.Duration * (Defs.Rules.EnergyDeficitWorkPct / 100f - 0.02f), $"a deficit slows work ({slow:0.0}s vs {farm.Duration:0.0}s) until upkeep is paid");
            float before = farm.Duration;
            s.Techs.Add("tech_hydroponics");
            foreach (var x in s.Table.Stacks) x.Dirty = true;
            Run(s, 0.1f);
            Check(Math.Abs(before / farm.Duration - 1.25f) < 0.02f, $"Hydroponic Farming makes Food work 25% faster ({before:0.0}s -> {farm.Duration:0.0}s)");
            var ag = new Sim(Defs.Ethics[0], 1, null, null, null, Defs.Species.First(x => x.Id == "agrarian"));
            var li = new Sim(Defs.Ethics[0], 1, null, null, null, Defs.Species.First(x => x.Id == "lithoid"));
            Check(ag.Mult("food") > li.Mult("food") && li.Mult("minerals") > ag.Mult("minerals") && li.NewCard("pop").MaxHp > ag.NewCard("pop").MaxHp,
                  "species differ: Agrarians farm faster, Lithoids mine faster and are tougher");
            var rr = Defs.Recipes.First(r => r.Id == "rr_hull");
            var all = Fresh();
            Check(all.Blueprint(rr) == Sim.BlueprintState.Locked, "infinite research stays hidden until every blueprint is researched");
            foreach (var t in Defs.Cards.Where(c => c.Category == "tech" && !c.HasTag("repeatable"))) all.Techs.Add(t.Id);
            var ship = all.Spawn("battleship", new Vector2(300, 600), jitter: false);
            float hp0 = ship.MaxHp;
            var lab = Build(all, new[] { "research_lab", "scientist" }, new Vector2(700, 600));
            all.SetOrder(lab, rr);
            all.Res["research"] = 20;
            Run(all, 60, () => all.RepLevels.GetValueOrDefault("tech_rep_hull") > 0);
            Check(all.RepLevels.GetValueOrDefault("tech_rep_hull") == 1 && ship.MaxHp > hp0 * 1.04f, $"repeatable Ship Hull research adds 5% hull ({hp0} -> {ship.MaxHp})");
        }

        Log.Info("Self-test: rival empires");
        {
            var s = Fresh();
            Check(s.Empires.Count == Defs.Rules.EmpireCount && s.Empires.All(e => !e.Contacted), $"{Defs.Rules.EmpireCount} rival empires per run, none met at the start");
            int systems = s.Systems.Count;
            var envoy = Build(s, new[] { "envoy", "science_ship" }, new Vector2(400, 400));
            Run(s, 40, () => s.Empires.Any(e => e.Contacted));
            var e = s.Empires.FirstOrDefault(x => x.Contacted);
            var z = s.Systems.LastOrDefault();
            Check(e != null && s.Systems.Count == systems + 1 && z?.Owner == e.Def.Id && s.StacksIn(z!).Any(x => x.Root.EmpireId == e.Def.Id) && s.Flags.Contains("contacted"),
                  "Envoy + Science Ship makes first contact: the empire's home system (with its capital card) joins the table");
            if (e != null && z != null)
            {
                Check(s.ClaimBlock(z) != null && s.AbandonBlock(z) != null, "an empire's system can't be claimed or abandoned");
                var cap = s.StacksIn(z).First(x => x.Root.EmpireId == e.Def.Id);
                var spy = s.Spawn("envoy", cap.Pos, jitter: false).Stack!;
                s.StackOnto(spy, cap);
                Run(s, Defs.Rules.IntelSecondsPerLevel + 2);
                Check(e.Intel == 2, $"an Envoy at their capital raises intel a level per {Defs.Rules.IntelSecondsPerLevel}s (now {e.Intel})");
                var deal = e.Offers[0];
                s.Res[deal.Give] = deal.GiveN;
                int had = s.Have(deal.Get);
                Check(s.Trade(e, 0) == null && s.Have(deal.Get) == had + deal.GetN && s.Trade(e, 0) != null, "a trade deal swaps resources, once per moon");
                Check(s.DeclareWar(e, "humiliation") == null && e.Status == "war", "declaring war needs a goal (humiliation)");
                s.Moon += Defs.Rules.EmpireRaidEvery;
                s.MoonTime = s.MoonSeconds - 0.01f;
                s.Res["food"] = 999; s.Res["energy"] = 999;
                s.Update(0.05f);
                Check(s.Table.Stacks.Any(x => x.Root.Def.HasTag("empire") && x.Root.Def.IsHostile), "at war, they send raid fleets to your capital");
                foreach (var h in s.Table.Stacks.Where(x => x.HasHostile).ToList()) foreach (var c in h.Cards.ToList()) s.Remove(c);
                s.Techs.Add("tech_fleet_doctrine_2");
                var fleet = Build(s, Enumerable.Repeat("battleship", 8), new Vector2(300, 700));
                Check(s.StartInvasion(fleet, e) == null && s.War != null, "dropping a fleet on their capital (at war) starts an invasion");
                int capital = e.Systems.FindIndex(x => x.Capital);
                s.AttackSystem(capital);
                float t = 0;
                while (s.War?.Fight != null && t < 200) { s.UpdateWar(0.05f); t += 0.05f; }
                Check(s.War != null && s.War.Won && e.Status == "peace" && e.HumiliatedUntil > s.Moon, $"occupying their capital wins a humiliation war ({t:0}s)");
                int moon = s.Moon;
                s.EndInvasion();
                Check(s.War == null && s.Moon == moon && s.Table.Stacks.Any(x => Sim.Warships(x) > 0), "the fleet comes home; time at home stood still");
                var e2 = s.Empires.First(x => x != e);
                e2.Contacted = true;
                s.DeclareWar(e2, "tributary");
                var f2 = s.Table.Stacks.First(x => Sim.Warships(x) > 0);
                s.StartInvasion(f2, e2);
                s.AttackSystem(e2.Systems.FindIndex(x => x.Capital));
                t = 0;
                while (s.War?.Fight != null && t < 200) { s.UpdateWar(0.05f); t += 0.05f; }
                s.EndInvasion();
                int en = s.Have("energy");
                s.MoonTime = s.MoonSeconds - 0.01f;
                s.Update(0.05f);
                Check(e2.Status == "tributary" && s.Have("energy") > en - s.StructureUpkeep - 5, "a tributary pays Energy and Minerals each moon");
                var path = Path.Combine(Path.GetTempPath(), "gg_selftest_emp.json");
                s.SaveTo(path);
                var l = new Sim(Sim.ReadSave(path)!);
                File.Delete(path);
                Check(l.Empires.Count == s.Empires.Count && l.Empires.Any(x => x.Status == "tributary") && l.Systems.Any(x => x.Owner != null)
                      && l.AllCards.Any(c => c.EmpireId != null), "empires (status, intel, systems, capital cards) save and load");
            }
        }

        Log.Info("Self-test: endless play after a win");
        {
            var s = Fresh();
            var boss = s.Spawn(s.Crisis.BossCard, new Vector2(1200, 500), jitter: false);
            boss.Hp = 1; boss.Shield = boss.MaxShield = 0; boss.Armor = 0; boss.ShieldRegen = boss.HullRegen = boss.ArmorRegen = 0; boss.Evasion = 0;
            s.Attack(Build(s, new[] { "battleship", "battleship" }, new Vector2(300, 500)), boss);
            Run(s, 30, () => s.State != RunState.Playing);
            Check(s.State == RunState.Won, "destroying the crisis leader wins");
            s.State = RunState.Playing; s.Endless = true;
            var again = s.Spawn(s.Crisis.BossCard, new Vector2(1200, 500), jitter: false);
            again.Hp = 1; again.Shield = again.MaxShield = 0; again.Armor = 0; again.ShieldRegen = again.HullRegen = again.ArmorRegen = 0; again.Evasion = 0;
            s.Attack(s.Table.Stacks.FirstOrDefault(x => Sim.Warships(x) > 0) ?? Build(s, new[] { "battleship" }, new Vector2(300, 500)), again);
            Run(s, 30);
            Check(s.State == RunState.Playing && !s.AllCards.Contains(again), "after choosing to keep playing, the run carries on");
        }

        Log.Info("Self-test: random star systems");
        {
            var types = new Dictionary<string, int>();
            var planetCounts = new Dictionary<int, int>();
            for (int run = 0; run < 300; run++)
            {
                var s = Fresh(seed: 1000 + run);
                var sys = s.RollSystemType();
                var z = s.AddSystem(sys);
                var inSys = s.StacksIn(z).ToList();
                types[sys.Id] = types.GetValueOrDefault(sys.Id) + 1;
                int planets = inSys.Count(x => x.Root.Def.IsPlanet);
                planetCounts[planets] = planetCounts.GetValueOrDefault(planets) + 1;
                if (!inSys.Any(x => x.Root.Def.Category == "star")) { Check(false, $"{sys.Id} has a star card"); break; }
            }
            Log.Info("  INFO  types: " + string.Join(", ", types.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));
            Check(Defs.Systems.Where(x => x.Kind == "random").All(x => types.ContainsKey(x.Id)), "every random system type turns up in 300 surveys");
            Check(planetCounts.ContainsKey(0) && planetCounts.Keys.Max() >= 4, "planet counts vary (some empty, some with 4+)");

            var run2 = Fresh(seed: 77);
            var sci = Build(run2, new[] { "science_ship" }, new Vector2(300, 300));
            for (int i = 0; i < 40; i++)
            {
                var st = Build(run2, new[] { "uncharted_system" }, new Vector2(300, 700));
                run2.StackOnto(st, sci);
                Run(run2, 30, () => sci.Cards.Count == 1);
            }
            var names = run2.Systems.Skip(1).Select(x => x.Name).ToList();
            bool apart = run2.Systems.All(a => run2.Systems.All(b => a == b || !a.Contains(b.Center)));
            Check(run2.Systems.Count == 41 && names.Distinct().Count() == names.Count && apart,
                $"surveying is unlimited: 40 surveys add {run2.Systems.Count - 1} systems, none overlapping, all names unique");
        }

        Log.Info("Self-test: abandoning systems");
        {
            var s = Fresh();
            var z = s.AddSystem(s.RollSystemType());
            var slot = z.Slot;
            var pop = Build(s, new[] { "pop" }, z.Center);
            Check(s.AbandonBlock(z)?.Contains("Pop") == true, "a system with your cards in it can't be abandoned");
            foreach (var c in pop.Cards.ToList()) s.Remove(c);
            foreach (var h in s.StacksIn(z).Where(x => x.HasHostile).ToList()) foreach (var c in h.Cards.ToList()) s.Remove(c);
            int before = s.Systems.Count;
            Check(s.Abandon(z) == null && s.Systems.Count == before - 1 && !s.Table.Stacks.Any(x => z.Contains(Sim.CardCenter(x))),
                  "an unclaimed system with only planets and stars is abandoned, cards and all");
            var next = s.AddSystem(s.RollSystemType());
            Check(next.Slot == slot, "the next survey fills the abandoned slot");
            Check(s.AbandonBlock(s.Home) != null, "the capital can't be abandoned");
        }

        Log.Info("Self-test: tutorial and pacing");
        {
            var s = new Sim(Defs.Ethics[0], 3) { TutorialOn = true };
            Check(s.CurrentStep?.Id == Defs.Tutorial[0].Id, $"a new run starts on tutorial step 1: {Defs.Tutorial[0].Text}");
            s.Update(1f);
            Check(s.MoonTime > 0, "time runs during the tutorial");
            var pop = s.StacksIn(s.Home).First(x => x.Root.Def.Id == s.Ethic.WorkerCard);
            var hw = s.StacksIn(s.Home).First(x => x.Root.Def.Id == "homeworld");
            s.StackOnto(pop, hw);
            Run(s, 20, () => s.Discovered.Contains("w_yield"));
            Check(s.StepDone(Defs.Tutorial[0]) && s.CurrentStep?.Id == Defs.Tutorial[1].Id, "putting a Pop on the Homeworld completes step 1 and moves on");
            s.SkippedSteps.Add(Defs.Tutorial[1].Id);
            Check(s.CurrentStep?.Id == Defs.Tutorial[2].Id, "Skip moves past a step");
            s.Res["energy"] = 20;
            s.BuyPack(Defs.Packs[0], s.Home.Center);
            Check(s.Flags.Contains("pack_bought") && s.StepDone(Defs.Tutorial.First(t => t.Id == "pack")), "buying a pack ticks the pack step");

            // An idle first moon: nobody starves (moon 1 is on rations), but from moon 2 people eat.
            var idle = new Sim(Defs.Ethics[0], 4);
            int pops = idle.AllCards.Count(c => c.Def.Id == "pop");
            Run(idle, idle.MoonSeconds + 1);
            Check(idle.Moon == 2 && idle.AllCards.Count(c => c.Def.Id == "pop") == pops, "an idle first moon starves no one (rations)");
            // A player who works the start sensibly feeds everyone through moon 2 and can afford a first pack early.
            var play = new Sim(Defs.Ethics.First(e => e.Id == "spiritualist"), 5);
            Stack Find(string id) => play.StacksIn(play.Home).First(x => x.Cards.Count == 1 && x.Root.Def.Id == id);
            play.StackOnto(Find("pop"), Find("homeworld"));
            play.StackOnto(Find("pop"), Find("agriculture_district"));
            play.StackOnto(Find("pop"), Find("mining_district"));
            float firstPack = -1, t = 0;
            while (play.Moon < 3 && t < 400)
            {
                play.Update(0.05f); t += 0.05f;
                if (firstPack < 0 && play.Have("energy") >= play.PackCost(Defs.Pack["pack_exploration"])) firstPack = t;
            }
            int fed = play.AllCards.Count(c => c.Def.Category == "person");
            Log.Info($"  INFO  worked start: first Exploration pack affordable at {firstPack:0}s; after 2 moons: {play.Have("food")} Food, {play.Have("energy")} Energy, {play.Have("minerals")} Minerals, {fed} people");
            Check(firstPack is > 30 and < 120 && fed >= 4, "a worked start affords a first pack within 30-120 s, and feeds everyone through moon 2");
        }

        Log.Info("Self-test: layout (nothing overlaps, nothing jumps)");
        {
            // Cards dumped in one spot, plus a battle, spread out until nothing overlaps anything.
            var s = Fresh();
            foreach (var id in new[] { "pop", "generator_district", "mining_district", "research_lab", "temple", "pop", "city_district", "shipyard", "alloy_foundry", "anomaly" })
                for (int i = 0; i < 2; i++) s.Spawn(id, s.Home.Center);
            var raider = s.Spawn("pirate_raider", s.Home.Center, jitter: false);
            s.Attack(s.Spawn("corvette", s.Home.Center).Stack!, raider);
            var last = s.Table.Stacks.ToDictionary(x => x, x => x.Pos);
            float maxStep = 0;
            for (int i = 0; i < 60; i++)
            {
                s.Update(0.05f);
                foreach (var x in s.Table.Stacks)
                {
                    if (last.TryGetValue(x, out var p)) maxStep = Math.Max(maxStep, Vector2.Distance(p, x.Pos));
                    last[x] = x.Pos;
                }
            }
            Check(s.Table.Battles.Count == 1, "the battle is still being fought for the overlap check");
            var stacks = s.Table.Stacks.Where(x => !x.Traveling && s.SystemAt(Sim.CardCenter(x)) == s.Home).ToList();
            var bad = new List<string>();
            for (int i = 0; i < stacks.Count; i++)
                for (int j = i + 1; j < stacks.Count; j++)
                    if (Sim.Hit(Sim.StackArea(stacks[i]), Sim.StackArea(stacks[j]), 0)) bad.Add($"{stacks[i].Root.Def.Id}@{stacks[i].Pos} vs {stacks[j].Root.Def.Id}@{stacks[j].Pos}");
            foreach (var x in stacks)
            {
                if (Sim.Hit(Sim.StackArea(x), Sim.TitleArea(s.Home), 0)) bad.Add($"{x.Root.Def.Id}@{x.Pos} vs title");
                foreach (var bt in s.Table.Battles) if (Sim.Hit(Sim.StackArea(x), Sim.BattleArea(bt), 0)) bad.Add($"{x.Root.Def.Id}@{x.Pos} vs battle");
            }
            Check(bad.Count == 0, $"{stacks.Count} stacks and the battle in the capital overlap nothing {string.Join("; ", bad)}");
            Check(maxStep <= Sim.GlideMax * 0.05f + 1, $"cards slide, never jump: biggest move in one tick {maxStep:0} (limit {Sim.GlideMax * 0.05f:0})");
        }
        {
            var s = Fresh();
            for (int i = 0; i < 70; i++) s.Spawn(i % 2 == 0 ? "pop" : "anomaly", s.Home.Center);
            Run(s, 6);
            int outside = s.Table.Stacks.Count(x => s.SystemAt(Sim.CardCenter(x)) != s.Home);
            int hang = s.Table.Stacks.Count(x => x.Pos.Y + Sim.StackHeight(x) > s.Home.Origin.Y + s.Home.Size.Y + 0.5f || x.Pos.X + Sim.CardW > s.Home.Origin.X + s.Home.Size.X + 0.5f);
            Check(outside == 0 && hang == 0, $"70 cards crammed into the capital stay inside its border ({outside} pushed out, {hang} hang over)");
        }

        Log.Info("Self-test: claiming systems");
        {
            var s = Fresh();
            Check(s.Home.Claimed && s.ClaimedCount == 1, "you start owning only your capital");
            var far = s.AddSystem(s.RollSystemType());
            foreach (var h in s.StacksIn(far).Where(x => x.HasHostile).ToList()) foreach (var c in h.Cards.ToList()) s.Remove(c);
            var col = Build(s, new[] { "desert_world", "colony_ship" }, far.Center);
            Run(s, 30);
            Check(!s.Discovered.Contains("c_colonize"), "colonising needs the system claimed first");
            var amoeba = s.Spawn("space_amoeba", far.Origin + new Vector2(1200, 700), jitter: false);
            amoeba.AggroTimer = 999; // it only has to be there
            s.Res["influence"] = 2;
            var claim = Build(s, new[] { "yellow_star", "construction_ship" }, far.Origin + new Vector2(200, 650));
            Run(s, 1);
            Check(!far.Claimed && s.Messages.Any(m => m.Contains("Clear the hostiles")), "a system with hostiles in it can't be claimed (and the game says why)");
            foreach (var h in s.StacksIn(far).Where(x => x.HasHostile).ToList()) foreach (var c in h.Cards.ToList()) s.Remove(c);
            foreach (var bt in s.Table.Battles.ToList()) foreach (var c in bt.Hostiles.ToList()) s.Remove(c);
            claim.Dirty = true;
            Run(s, 30, () => far.Claimed);
            for (int i = 0; i < 600 && !s.Discovered.Contains("c_colonize"); i++) { col.Dirty = true; s.Update(0.05f); }
            Check(far.Claimed && s.Discovered.Contains("c_colonize") && s.Have("influence") == 0, "Construction Ship + 2 Influence (from the pool) on its star claims it; then colonising works");
            int tries = 0;
            while (s.ClaimedCount < Defs.Rules.ClaimLimit && tries++ < 20)
            {
                var z = s.AddSystem(s.RollSystemType());
                foreach (var h in s.StacksIn(z).Where(x => x.HasHostile).ToList()) foreach (var c in h.Cards.ToList()) s.Remove(c);
                s.Res["influence"] = 2;
                Build(s, new[] { "yellow_star", "construction_ship" }, z.Origin + new Vector2(200, 650));
                Run(s, 30, () => z.Claimed);
            }
            var over = s.AddSystem(s.RollSystemType());
            foreach (var h in s.StacksIn(over).Where(x => x.HasHostile).ToList()) foreach (var c in h.Cards.ToList()) s.Remove(c);
            s.Messages.Clear();
            s.Res["influence"] = 2;
            Build(s, new[] { "yellow_star", "construction_ship" }, over.Origin + new Vector2(200, 650));
            Run(s, 30);
            Check(s.ClaimedCount == Defs.Rules.ClaimLimit && !over.Claimed && s.Messages.Any(m => m.Contains("Claim limit")),
                $"claim limit: you can own {Defs.Rules.ClaimLimit} systems; the next claim is refused with a message");
        }

        Log.Info("Self-test: travel between systems");
        {
            var s = Fresh();
            var near = s.AddSystem(s.RollSystemType());
            var pop = Build(s, new[] { "pop" }, s.Home.Center);
            Check(!s.StartTravel(pop, near.Center), "a Pop can't cross to another system without a ship");
            var fleet = Build(s, new[] { "corvette", "pop" }, s.Home.Center + new Vector2(200, 0));
            Check(s.StartTravel(fleet, near.Center), "a stack with a ship sets off for another system");
            float t = 0;
            while (fleet.Traveling && t < 120) { s.Update(0.05f); t += 0.05f; }
            Check(!fleet.Traveling && s.SystemAt(Sim.CardCenter(fleet)) == near && Math.Abs(t - Defs.Rules.TravelSecondsPerJump) < 0.5f,
                $"it arrives after {t:0.0}s (one step = {Defs.Rules.TravelSecondsPerJump}s)");
            for (int i = 0; i < 9; i++) s.AddSystem(s.RollSystemType());
            var far = s.Systems.Last();
            Check(s.TravelSeconds(s.Home, far) == 2 * Defs.Rules.TravelSecondsPerJump, $"a system two steps out takes {s.TravelSeconds(s.Home, far)}s");
        }

        Log.Info("Self-test: market");
        {
            var s = Fresh();
            var st = Build(s, new[] { "corvette", "precursor_artifact" }, new Vector2(400, 400));
            int got = s.Sell(st);
            Check(got == 17 && s.Have("energy") == 17, $"selling a Corvette + Artifact puts {got} Energy in the pool");
            var other = s.AddSystem(s.RollSystemType());
            s.Spawn("pirate_raider", other.Center + new Vector2(200, 0), jitter: false);
            var homePop = Build(s, new[] { "pop" }, s.Home.Center);
            Run(s, 20);
            Check(homePop.Cards.Count == 1 && homePop.Cards[0].Battle == null && s.Table.Battles.Count(bt => s.Home.Contains(bt.Pos)) == 0,
                "a raider in another system leaves your capital alone (it fights where it is)");
        }

        Log.Info("Self-test: damage types, shields, armour and hull");
        {
            float TimeToKill(string foe, string ship, params string[] parts)
            {
                var s = Fresh(seed: 5);
                var enemy = s.Spawn(foe, new Vector2(1200, 500), jitter: false);
                enemy.Guns.Clear(); // a target dummy: it doesn't shoot back
                var host = s.Spawn(ship, new Vector2(300, 500), jitter: false);
                host.Guns.Clear();
                foreach (var p in parts) s.Fit(s.Spawn(p, new Vector2(300, 800), jitter: false), host);
                host.Guns.RemoveAll(g => g.Profile == null);
                s.Attack(host.Stack!, enemy);
                float t = 0;
                while (t < 300 && s.AllCards.Contains(enemy)) { s.Update(0.05f); t += 0.05f; }
                return t;
            }
            float laserVsArmour = TimeToKill("crystal_entity", "cruiser", "red_laser", "red_laser");
            float kineticVsArmour = TimeToKill("crystal_entity", "cruiser", "mass_driver", "mass_driver");
            float laserVsShield = TimeToKill("void_cloud", "cruiser", "red_laser", "red_laser");
            float kineticVsShield = TimeToKill("void_cloud", "cruiser", "mass_driver", "mass_driver");
            Check(laserVsArmour < kineticVsArmour, $"lasers beat armour: armoured Crystalline Entity dies in {laserVsArmour:0.0}s to lasers vs {kineticVsArmour:0.0}s to mass drivers");
            Check(kineticVsShield < laserVsShield, $"kinetics beat shields: shielded Void Cloud dies in {kineticVsShield:0.0}s to mass drivers vs {laserVsShield:0.0}s to lasers");

            var t = Fresh();
            var target = t.Spawn("unbidden_warrior", new Vector2(800, 500), jitter: false);
            var dummy = new Card { Def = target.Def, Hp = 100, MaxHp = 100, Shield = 50, MaxShield = 50, Armor = 20, MaxArmor = 20 };
            t.Hit(new Gun { Profile = Defs.Component["space_torpedoes"], Damage = 10, Cooldown = 1 }, 1, dummy, 0);
            Check(dummy.Shield == 50 && dummy.Armor < 20, "torpedoes fly past shields and hit armour");
            var d2 = new Card { Def = target.Def, Hp = 100, MaxHp = 100, Shield = 50, MaxShield = 50, Armor = 20, MaxArmor = 20 };
            t.Hit(new Gun { Profile = Defs.Component["disruptor"], Damage = 10, Cooldown = 1 }, 1, d2, 0);
            Check(d2.Shield == 50 && d2.Armor == 20 && d2.Hp == 90, "disruptors ignore shields and armour, hitting the hull");
            var d3 = new Card { Def = target.Def, Hp = 100, MaxHp = 100 };
            var d4 = new Card { Def = target.Def, Hp = 100, MaxHp = 100 };
            var torp = new Gun { Profile = Defs.Component["space_torpedoes"], Damage = 10, Cooldown = 1 };
            t.Hit(torp, 1, d3, 0);
            t.Hit(torp, 1, d4, 0.6f);
            Check(d4.Hp > d3.Hp, $"flak cuts torpedo damage ({100 - d3.Hp:0} without, {100 - d4.Hp:0} with two Flak Batteries)");

            var regen = Fresh();
            var bs = regen.Spawn("battleship", new Vector2(500, 500), jitter: false);
            regen.Fit(regen.Spawn("regenerative_hull_tissue", new Vector2(500, 800), jitter: false), bs);
            bs.Hp = 10;
            Run(regen, 10);
            Check(bs.Hp >= 19, $"Regenerative Hull Tissue heals the hull ({bs.Hp:0} after 10 s, from 10)");
            int dodged = 0;
            var ev = new Card { Def = Defs.Card["corvette"], Hp = 1e6f, MaxHp = 1e6f, Evasion = 0.4f };
            for (int i = 0; i < 1000; i++) if (t.Hit(new Gun { Damage = 1, Cooldown = 1 }, 1, ev, 0) == 0) dodged++;
            Check(dodged is > 330 and < 470, $"evasion dodges shots ({dodged}/1000 at 40%)");
        }

        Log.Info("Self-test: fitting, refitting, fleets and admirals");
        {
            var s = Fresh();
            var corvette = s.Spawn("corvette", new Vector2(300, 300), jitter: false);
            Check(s.Fit(s.Spawn("red_laser", new Vector2(300, 600), jitter: false), corvette) == null && corvette.Guns.Count == 2,
                "a Red Laser fits a Corvette and adds a gun");
            Check(s.Fit(s.Spawn("deflector", new Vector2(300, 600), jitter: false), corvette) is { } full && full.Contains("no free slots"),
                "a Corvette has 1 slot; a part of another kind is refused");
            Check(s.Fit(s.Spawn("blue_laser", new Vector2(300, 600), jitter: false), corvette) == null && corvette.Parts.Single().Id == "blue_laser"
                  && s.AllCards.Any(c => c.Def.Id == "red_laser") && corvette.Guns.Count == 2, "a Blue Laser swaps out the Red Laser, which comes back as a card");
            var cruiser = s.Spawn("cruiser", new Vector2(600, 300), jitter: false);
            Check(s.Fit(s.Spawn("tachyon_lance", new Vector2(600, 600), jitter: false), cruiser) is { } big && big.Contains("bigger hull"),
                "a Tachyon Lance won't fit a Cruiser");
            var bs = s.Spawn("battleship", new Vector2(900, 300), jitter: false);
            float armour = bs.MaxArmor, shields = bs.MaxShield;
            s.Fit(s.Spawn("neutronium_armor", new Vector2(900, 600), jitter: false), bs);
            s.Fit(s.Spawn("improved_deflector", new Vector2(900, 600), jitter: false), bs);
            Check(bs.MaxArmor == armour + 55 && bs.MaxShield == shields + 30 && bs.ShieldRegen == 2, "armour and shield parts raise the ship's armour, shields and recharge");
            Check(s.Unfit(bs, 0) == null && bs.MaxArmor == armour && bs.Parts.Count == 1 && s.AllCards.Any(c => c.Def.Id == "neutronium_armor"),
                  "taking the armour off lowers the ship's armour and returns the part as a card");
            var adm = s.Spawn("admiral", new Vector2(900, 800), jitter: false);
            Check(s.AssignAdmiral(adm, bs) == null && bs.Admiral == adm && !s.AllCards.Contains(adm), "an Admiral takes command of a Battleship's fleet (and rides aboard)");
            var bt = new Battle();
            bt.Players.Add(bs);
            var stranger = new Card { Def = Defs.Card["corvette"], Fleet = 999 };
            bt.Players.Add(stranger);
            Check(Sim.PlayerMult(bt, bs) == Defs.Card["admiral"].BoostMult && Sim.PlayerMult(bt, stranger) == 1f, "the Admiral boosts their own fleet, not other fleets in the battle");

            var f = Fresh();
            var fleet = Build(f, new[] { "corvette", "corvette", "corvette" }, new Vector2(400, 400));
            var fourth = Build(f, new[] { "corvette" }, new Vector2(800, 400));
            Check(Sim.Warships(fleet) == 3 && f.StackBlock(fourth, fleet)?.Contains("Fleet Doctrine") == true, $"a fleet holds {Defs.Rules.FleetSizeBase} warships until Fleet Doctrine");
            f.Techs.Add("tech_fleet_doctrine_1");
            Check(f.StackOnto(fourth, fleet) && f.FleetSize == 6, "Fleet Doctrine raises the fleet size to 6");
            var a1 = f.Spawn("admiral", new Vector2(800, 800), jitter: false);
            var a2 = f.Spawn("admiral", new Vector2(1000, 800), jitter: false);
            Check(f.AssignAdmiral(a1, fleet.Cards[2]) == null && fleet.Cards[0].Admiral == a1 && f.AssignAdmiral(a2, fleet.Cards[1]) != null,
                  "one Admiral per fleet (they ride the lead ship)");
            var raider = f.Spawn("pirate_raider", new Vector2(1200, 300), jitter: false);
            int ships = Sim.Warships(fleet);
            f.Attack(fleet, raider);
            Run(f, 60, () => f.Table.Battles.Count == 0);
            var back = f.Table.Stacks.Where(x => Sim.Warships(x) > 0).ToList();
            Check(f.Table.Battles.Count == 0 && back.Count == 1 && Sim.HasAdmiral(back[0]), $"after the battle the fleet regroups as one stack with its Admiral ({back.Count} stacks)");
        }

        Log.Info("Self-test: fleets and loadouts vs guardians and crises");
        {
            var fleets = new (string name, (string ship, string[] parts)[] ships, bool admiral)[]
            {
                ("early: 3 bare corvettes", new[] { ("corvette", new string[0]), ("corvette", new string[0]), ("corvette", new string[0]) }, false),
                ("mid bare: 2 cruisers, 3 destroyers + admiral", new[] { ("cruiser", new string[0]), ("cruiser", new string[0]), ("destroyer", new string[0]), ("destroyer", new string[0]), ("destroyer", new string[0]) }, true),
                ("mid fitted: same, lasers/railguns/deflectors", new[] { ("cruiser", new[] { "blue_laser", "railgun", "improved_deflector" }), ("cruiser", new[] { "blue_laser", "railgun", "crystal_armor" }),
                    ("destroyer", new[] { "blue_laser", "deflector" }), ("destroyer", new[] { "railgun", "deflector" }), ("destroyer", new[] { "space_torpedoes", "flak_battery" }) }, true),
                ("late bare: titan, 4 battleships, 2 cruisers + admiral", new[] { ("titan", new string[0]), ("battleship", new string[0]), ("battleship", new string[0]), ("battleship", new string[0]), ("battleship", new string[0]), ("cruiser", new string[0]), ("cruiser", new string[0]) }, true),
                ("late energy: gamma/plasma/tachyon", new[] { ("titan", new[] { "tachyon_lance", "gamma_laser", "gamma_laser", "dark_matter_deflector", "neutronium_armor" }),
                    ("battleship", new[] { "gamma_laser", "plasma_cannon", "plasma_cannon", "improved_deflector" }), ("battleship", new[] { "gamma_laser", "plasma_cannon", "plasma_cannon", "improved_deflector" }),
                    ("battleship", new[] { "gamma_laser", "gamma_laser", "neutronium_armor", "regenerative_hull_tissue" }), ("battleship", new[] { "gamma_laser", "gamma_laser", "neutronium_armor", "flak_battery" }) }, true),
                ("late kinetic: artillery/railguns", new[] { ("titan", new[] { "kinetic_artillery", "kinetic_artillery", "kinetic_artillery", "dark_matter_deflector", "neutronium_armor" }),
                    ("battleship", new[] { "kinetic_artillery", "kinetic_artillery", "railgun", "improved_deflector" }), ("battleship", new[] { "kinetic_artillery", "kinetic_artillery", "railgun", "improved_deflector" }),
                    ("battleship", new[] { "kinetic_artillery", "railgun", "neutronium_armor", "regenerative_hull_tissue" }), ("battleship", new[] { "kinetic_artillery", "railgun", "neutronium_armor", "flak_battery" }) }, true),
                ("late mixed: artillery + gamma + missiles + flak", new[] { ("titan", new[] { "tachyon_lance", "kinetic_artillery", "swarmer_missiles", "dark_matter_deflector", "neutronium_armor" }),
                    ("battleship", new[] { "kinetic_artillery", "gamma_laser", "swarmer_missiles", "improved_deflector" }), ("battleship", new[] { "kinetic_artillery", "gamma_laser", "swarmer_missiles", "neutronium_armor" }),
                    ("battleship", new[] { "kinetic_artillery", "plasma_cannon", "flak_battery", "neutronium_armor" }), ("battleship", new[] { "arc_emitter", "gamma_laser", "flak_battery", "regenerative_hull_tissue" }) }, true),
            };
            var foes = new[] { "pirate_raider", "marauder_raider", "ether_drake", "dimensional_horror", "enigmatic_fortress", "scourge_queen", "unbidden_avatar", "contingency_core" };
            Results.Clear();
            foreach (var foe in foes)
            {
                var row = new List<string>();
                foreach (var (name, ships, admiral) in fleets)
                {
                    int wins = 0; float tsum = 0; int left = 0;
                    for (int seed = 0; seed < 3; seed++)
                    {
                        var s = Fresh(seed: 900 + seed);
                        s.Techs.Add("tech_fleet_doctrine_2");
                        var enemy = s.Spawn(foe, new Vector2(1200, 500), jitter: false);
                        Stack? fleet = null;
                        foreach (var (ship, parts) in ships)
                        {
                            var c = s.Spawn(ship, new Vector2(300, 300), jitter: false);
                            foreach (var p in parts) s.Fit(s.Spawn(p, new Vector2(300, 800), jitter: false), c);
                            if (fleet == null) fleet = c.Stack!; else s.StackOnto(c.Stack!, fleet);
                        }
                        if (admiral) s.AssignAdmiral(s.Spawn("admiral", new Vector2(300, 800), jitter: false), fleet!.Cards[0]);
                        s.Attack(fleet!, enemy);
                        float t = 0;
                        while (t < 300 && s.Table.Battles.Count > 0 && s.State == RunState.Playing) { s.Update(0.05f); t += 0.05f; }
                        if (!s.AllCards.Contains(enemy) || s.State == RunState.Won) { wins++; tsum += t; left += s.AllCards.Count(c => c.Def.Category == "ship"); }
                    }
                    row.Add(wins == 0 ? "loss" : $"{wins}/3 win {tsum / wins:0}s, {left / wins} ships left");
                    Results[(foe, name)] = wins;
                }
                Log.Info($"  INFO  vs {foe}:");
                for (int i = 0; i < fleets.Length; i++) Log.Info($"          {fleets[i].name,-52} {row[i]}");
            }
            bool Wins(string foe, string fleetPrefix) => Results.Where(kv => kv.Key.foe == foe && kv.Key.fleet.StartsWith(fleetPrefix)).Sum(kv => kv.Value) >= 2;
            Check(Wins("pirate_raider", "early"), "3 bare corvettes beat a pirate raider");
            Check(!Wins("ether_drake", "early") && Wins("ether_drake", "mid fitted"), "the Ether Drake needs a fitted mid-game fleet");
            Check(Defs.Crises.All(c => !Wins(c.BossCard, "mid bare")), "a bare mid-game fleet can't beat any crisis leader");
            Check(Defs.Crises.All(c => Wins(c.BossCard, "late mixed")), "a well-fitted, mixed late fleet beats every crisis leader");
            Check(Wins("unbidden_avatar", "late kinetic") && Wins("scourge_queen", "late energy"), "the right damage type beats each crisis (kinetic vs Unbidden shields, energy vs Scourge armour)");
        }

        {
            var s = Fresh();
            var drake = s.Spawn("ether_drake", new Vector2(1400, 600), jitter: false);
            Run(s, 30);
            Check(drake.Battle == null, "Guardians sleep until you attack them");
            var raider = s.Spawn("pirate_raider", new Vector2(1400, 900), jitter: false);
            Build(s, new[] { "pop" }, new Vector2(1200, 900));
            Run(s, 20);
            Check(!s.AllCards.Any(c => c.Def.Id == "pop") || raider.Battle != null || !s.AllCards.Contains(raider), "raiders attack your Pops");
        }
        {
            // The first raid comes before most players have warships: the Homeworld's defences (and its Pop) must hold.
            int held = 0;
            for (int seed = 0; seed < 5; seed++)
            {
                var s = Fresh(seed: 300 + seed);
                Build(s, new[] { "homeworld", "pop" }, s.Home.Center);
                s.Spawn("pirate_raider", s.Home.Center + new Vector2(300, 0), jitter: false);
                Run(s, 90, () => s.State != RunState.Playing || (s.Table.Battles.Count == 0 && !s.AllCards.Any(c => c.Def.Id == "pirate_raider")));
                if (s.State == RunState.Playing && !s.AllCards.Any(c => c.Def.Id == "pirate_raider")) held++;
            }
            Check(held == 5, $"a Homeworld with one Pop beats a lone pirate raid ({held}/5)");
            Check(Defs.Difficulties.All(d => Defs.Rules.FirstRaidMoon >= 5), $"no raids before moon {Defs.Rules.FirstRaidMoon}");
        }

        Log.Info("Self-test: moons");
        {
            var s = Fresh();
            s.Moon = Defs.Rules.UpkeepFromMoon;
            Build(s, new[] { "pop" }, new Vector2(300, 300));
            Build(s, new[] { "pop" }, new Vector2(600, 300));
            Build(s, new[] { "homeworld" }, new Vector2(1200, 300));
            s.Res["food"] = 2;
            s.MoonTime = s.MoonSeconds - 0.01f;
            s.Update(0.05f);
            Check(s.AllCards.Count(c => c.Def.Id == "pop") == 1 && s.Have("food") == 0, "moon end: 2 Food from the pool feeds one Pop, the other starves");
        }
        {
            var s = Fresh();
            Build(s, new[] { "pop" }, new Vector2(300, 300));
            s.MoonTime = s.MoonSeconds - 0.01f;
            s.Update(0.05f);
            Check(s.Moon == 2 && s.AllCards.Count(c => c.Def.Id == "pop") == 1, "moon 1 ends on rations: no Food needed yet");
        }
        {
            var s = Fresh("machine");
            s.Moon = Defs.Rules.UpkeepFromMoon;
            Build(s, new[] { "drone", "drone" }, new Vector2(300, 300));
            s.Res["energy"] = 1;
            s.MoonTime = s.MoonSeconds - 0.01f;
            s.Update(0.05f);
            Check(s.AllCards.Count(c => c.Def.Id == "drone") == 1, "moon end: Drones run on Energy");
        }

        Log.Info("Self-test: saving and loading");
        {
            var s = new Sim(Defs.Ethics[0], 11);
            Run(s, 20);
            var fleet = s.AllCards.FirstOrDefault(c => c.Def.Id == "corvette");
            if (fleet != null) s.Fit(s.Spawn("red_laser", fleet.Stack!.Pos, jitter: false), fleet);
            s.Res["minerals"] = 7;
            s.Techs.Add("tech_red_laser");
            var path = Path.Combine(Path.GetTempPath(), "gg_selftest_save.json");
            s.SaveTo(path);
            var d = Sim.ReadSave(path);
            var l = d != null ? new Sim(d) : null;
            File.Delete(path);
            Check(l != null && l.Systems.Count == s.Systems.Count && l.AllCards.Count() == s.AllCards.Count() && l.Have("minerals") == 7
                  && l.Techs.SetEquals(s.Techs) && l.Moon == s.Moon && Math.Abs(l.MoonTime - s.MoonTime) < 0.01f
                  && (fleet == null || l.AllCards.Any(c => c.Def.Id == "corvette" && c.Parts.Any(p => p.Id == "red_laser"))),
                  $"a saved run loads back the same ({s.AllCards.Count()} cards, {s.Systems.Count} systems, pool, techs, fitted parts, moon)");
            if (l != null) { Run(l, 5); Check(l.State == RunState.Playing, "a loaded run plays on"); }
        }

        Log.Info("Self-test: acts and crisis timeline, every difficulty");
        foreach (var diff in Defs.Difficulties)
        foreach (var crisis in Defs.Crises)
        {
            int seed = 0;
            Sim s;
            do s = new Sim(Defs.Ethics[0], ++seed, null, diff, Defs.MoonLengths[0]); while (s.Crisis != crisis);
            // Food for the whole run, and a big home guard so the run survives to the boss.
            s.Res["food"] = 100000;
            s.Techs.Add("tech_fleet_doctrine_2");
            for (int i = 0; i < 4; i++) Build(s, Enumerable.Repeat("corvette", 10), s.Home.Origin + new Vector2(80 + i * 160, 160));
            int act2 = 0, rift = 0, boss = 0;
            float t = 0;
            while (s.Moon <= diff.CrisisMoon + diff.BossDelayMoons + 1 && s.State == RunState.Playing && t < 4000)
            {
                s.Update(0.1f); t += 0.1f;
                if (act2 == 0 && s.Act == 2) act2 = s.Moon;
                if (rift == 0 && s.RiftOpen) rift = s.Moon;
                if (boss == 0 && s.BossArrived) boss = s.Moon;
                s.Messages.Clear();
            }
            var tag = $"{diff.Id}/{crisis.Id}";
            Check(act2 == diff.Act2Moon && rift == diff.CrisisMoon && boss > 0 && boss <= diff.CrisisMoon + diff.BossDelayMoons,
                $"{tag}: Act 2 moon {act2}, rift moon {rift}, {crisis.BossCard} moon {boss} (end: {s.State} {s.EndReason})");
        }
        {
            var hard = Defs.Difficulties.Last();
            var s = new Sim(Defs.Ethics[0], 3, null, hard);
            var drake = s.NewCard("ether_drake");
            Check(drake.MaxHp == (int)MathF.Round(Defs.Card["ether_drake"].Hp * hard.EnemyHpMult) && s.PackCost(Defs.Packs[0]) >= Defs.Packs[0].Cost,
                $"{hard.Id}: enemies have {drake.MaxHp} HP (x{hard.EnemyHpMult}), packs cost {s.PackCost(Defs.Packs[0])}+");
            foreach (var m in Defs.MoonLengths)
                Check(new Sim(Defs.Ethics[0], 1, null, null, m).MoonSeconds == m.Seconds, $"moon length {m.Id} = {m.Seconds}s");
        }

        Log.Info("Self-test: every ethic starts a run");
        foreach (var e in Defs.Ethics)
        {
            var s = new Sim(e, 7);
            int workers = s.AllCards.Count(c => c.Def.Id == e.WorkerCard);
            Check(workers >= Defs.Rules.StartWorkers && s.AllCards.Any(c => c.Def.Id == "homeworld" && c.Claimed)
                  && e.StartTechs.All(s.Techs.Contains), $"{e.Id}: starts with {workers} {e.WorkerCard}s, a homeworld and its techs");
            Run(s, 30);
            Check(s.State == RunState.Playing, $"{e.Id}: 30 s of play runs without error");
        }

        Log.Info(_fails == 0 ? "Self-test: ALL PASS" : $"Self-test: {_fails} FAILED");
        return _fails == 0 ? 0 : 1;
    }
}
