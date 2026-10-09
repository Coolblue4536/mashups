using System.Numerics;

namespace GrandGalactic;

/// <summary>Headless checks of the simulation: every recipe fires, every pack opens, combat against every boss
/// resolves, moons feed and starve, and the acts arrive on schedule. Run with --selftest.</summary>
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
        s.MoonTime = -100000; // no moon ends during unit checks
        return s;
    }

    static Stack Build(Sim s, IEnumerable<string> ids, Vector2 pos)
    {
        Stack? st = null;
        foreach (var id in ids)
        {
            var c = s.Spawn(id, pos, jitter: false);
            if (st == null) st = c.Stack!;
            else s.StackOnto(c.Stack!, st);
        }
        return st!;
    }

    static string Resolve(string card) => card switch
    {
        "tag:workplace" => "generator_district",
        "tag:star" => "pulsar",
        "tag:worker" => "pop",
        "tag:warship" => "corvette",
        "tag:habitable" => "desert_world",
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
            var ids = new List<string> { Resolve(r.Station) };
            foreach (var i in r.Inputs) for (int k = 0; k < i.N; k++) ids.Add(Resolve(i.Card));
            // Recipes that need an unclaimed system run in a freshly surveyed one.
            var at = r.RequiresSystem == "unclaimed" ? s.AddSystem(s.RollSystemType()).Center : new Vector2(800, 600);
            if (r.RequiresSystem == "unclaimed")
                foreach (var h in s.Table.Stacks.Where(x => x.HasHostile && s.SystemAt(Sim.CardCenter(x))?.Index > 0).ToList())
                    foreach (var c in h.Cards.ToList()) s.Remove(c);
            var st = Build(s, ids, at);
            if (r.Effect == "repair") foreach (var c in st.Cards.Where(c => c.Def.HasTag("warship"))) c.Hp = 1;
            var outIds = r.Outputs.SelectMany(o => o.Give).Select(g => g.Card == "station.yield" ? Defs.Card[Resolve(r.Station)].Yield : g.Card).ToHashSet();
            int before = s.AllCards.Count(c => outIds.Contains(c.Def.Id)), boards = s.Systems.Count;
            for (int i = 0; i < 2400 && !s.Discovered.Contains(r.Id); i++) s.Update(0.05f);
            bool fired = s.Discovered.Contains(r.Id);
            bool effect = r.Effect switch
            {
                "open_board:random" or "open_board:guardian" => s.Systems.Count == boards + 1,
                "set_flag:claimed" => s.AllCards.Any(c => c.Def.Id == Resolve(r.Station) && c.Claimed),
                "claim_system" => s.SystemAt(at)?.Claimed == true,
                "repair" => st.Cards.Where(c => c.Def.HasTag("warship")).All(c => c.Hp >= c.MaxHp),
                _ => true,
            };
            bool produced = r.Outputs.Length == 0 || s.AllCards.Count(c => outIds.Contains(c.Def.Id)) > before - (outIds.Contains(Resolve(r.Station)) && !r.StationKeep ? 1 : 0) || outIds.Count == 0;
            Check(fired && effect && produced, $"recipe {r.Id,-16} fires ({string.Join(" + ", ids)})");
        }

        Log.Info("Self-test: recipe does not fire without its tech");
        {
            var s = Fresh();
            var st = Build(s, new[] { "shipyard", "alloys", "alloys", "alloys", "alloys" }, new Vector2(800, 600));
            s.Techs.Remove("tech_destroyers");
            for (int i = 0; i < 600; i++) s.Update(0.05f);
            Check(!s.Discovered.Contains("s_destroyer"), "Destroyer needs Destroyers tech");
        }

        Log.Info("Self-test: packs");
        foreach (var p in Defs.Packs)
        {
            var s = Fresh();
            s.Act = 3;
            var st = Build(s, Enumerable.Repeat("energy", s.PackCost(p) + 1), new Vector2(400, 400));
            int before = s.AllCards.Count();
            bool ok = s.BuyPack(st, p, new Vector2(900, 900));
            int after = s.AllCards.Count();
            Check(ok && after == before - s.PackCost(p) + p.Draws, $"pack {p.Id} costs {p.Cost} and gives {p.Draws}");
        }
        {
            var s = Fresh();
            var st = Build(s, new[] { "energy", "energy" }, new Vector2(400, 400));
            Check(!s.BuyPack(st, Defs.Pack["pack_exploration"], Vector2.Zero), "pack refuses too few Energy");
            var st2 = Build(s, new[] { "energy", "energy", "energy", "energy", "energy", "energy", "energy", "energy", "energy", "energy" }, new Vector2(400, 700));
            Check(!s.BuyPack(st2, Defs.Pack["pack_frontier"], Vector2.Zero), "Act 2 pack locked in Act 1");
        }

        Log.Info("Self-test: random star systems");
        {
            var types = new Dictionary<string, int>();
            var planetCounts = new Dictionary<int, int>();
            int totalCards = 0;
            for (int run = 0; run < 300; run++)
            {
                var s = Fresh(seed: 1000 + run);
                var sys = s.RollSystemType();
                var z = s.AddSystem(sys);
                var inSys = s.StacksIn(z).ToList();
                types[sys.Id] = types.GetValueOrDefault(sys.Id) + 1;
                int planets = inSys.Count(x => x.Root.Def.IsPlanet);
                planetCounts[planets] = planetCounts.GetValueOrDefault(planets) + 1;
                totalCards += inSys.Count;
                if (!inSys.Any(x => x.Root.Def.Category == "star")) { Check(false, $"{sys.Id} has a star card"); break; }
            }
            Log.Info("  INFO  types: " + string.Join(", ", types.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));
            Log.Info("  INFO  planets per system: " + string.Join(", ", planetCounts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}: {kv.Value}")));
            Check(Defs.Systems.Where(x => x.Kind == "random").All(x => types.ContainsKey(x.Id)), "every random system type turns up in 300 surveys");
            Check(planetCounts.ContainsKey(0) && planetCounts.Keys.Max() >= 4, "planet counts vary (some empty, some with 4+)");

            var run2 = Fresh(seed: 77);
            var sci = Build(run2, new[] { "science_ship" }, new Vector2(300, 300));
            for (int i = 0; i < 40; i++)
            {
                var st = Build(run2, new[] { "uncharted_system" }, new Vector2(300, 700));
                run2.StackOnto(st, sci);
                for (int k = 0; k < 400 && sci.Cards.Count > 1; k++) run2.Update(0.05f);
            }
            var names = run2.Systems.Skip(1).Select(x => x.Name).ToList();
            bool apart = run2.Systems.All(a => run2.Systems.All(b => a == b || !a.Contains(b.Center)));
            Check(run2.Systems.Count == 41 && names.Distinct().Count() == names.Count && apart,
                $"surveying is unlimited: 40 surveys add {run2.Systems.Count - 1} systems, none overlapping, all names unique");
        }

        Log.Info("Self-test: blueprint book");
        {
            var s = Fresh();
            Check(Defs.Recipes.All(r => Sim.BlueprintTab(r) != "Other"), $"all {Defs.Recipes.Length} recipes appear in a Blueprint book tab");
            Check(s.Blueprint(Defs.Recipes.First(r => r.Id == "r_red_laser")) == Sim.BlueprintState.Known, "tier-1 research (Red Lasers) is visible from the start");
            var blue = Defs.Recipes.First(r => r.Id == "r_blue_laser");
            Check(s.Blueprint(blue) == Sim.BlueprintState.Locked, "Blue Laser research shows as locked until Red Lasers are researched");
            s.Techs.Add("tech_red_laser");
            Check(s.Blueprint(blue) == Sim.BlueprintState.Known && s.Blueprint(Defs.Recipes.First(r => r.Id == "s_red_laser")) == Sim.BlueprintState.Known,
                "researching Red Lasers reveals the Red Laser build and Blue Laser research blueprints");
            Check(s.UsedIn(Defs.Card["alloys"]).Count() >= 5 && s.UsedIn(Defs.Card["alloys"]).All(r => s.Blueprint(r) != Sim.BlueprintState.Locked) && s.UsedIn(Defs.Card["red_laser"]).Any(r => r.Id == "r_blue_laser"),
                "hovering a card lists the blueprints it is used in");
        }

        Log.Info("Self-test: tutorial");
        {
            var s = new Sim(Defs.Ethics[0], 3);
            Check(s.CurrentStep?.Id == Defs.Tutorial[0].Id, $"a new run starts on tutorial step 1: {Defs.Tutorial[0].Text}");
            var pop = s.StacksIn(s.Home).First(x => x.Root.Def.Id == s.Ethic.WorkerCard);
            var hw = s.StacksIn(s.Home).First(x => x.Root.Def.Id == "homeworld");
            s.StackOnto(pop, hw);
            for (int i = 0; i < 300 && !s.Discovered.Contains("w_yield"); i++) s.Update(0.05f);
            Check(s.StepDone(Defs.Tutorial[0]) && s.CurrentStep?.Id == Defs.Tutorial[1].Id, "putting a Pop on the Homeworld completes step 1 and moves on");
            s.SkippedSteps.Add(Defs.Tutorial[1].Id);
            Check(s.CurrentStep?.Id == Defs.Tutorial[2].Id, "Skip moves past a step");
            var e = s.StacksIn(s.Home).Where(x => x.Root.Def.Id == "energy").ToList();
            var first = e[0];
            foreach (var x in e.Skip(1)) s.StackOnto(x, first);
            s.BuyPack(first, Defs.Packs[0], s.Home.Center);
            Check(s.Flags.Contains("pack_bought") && s.StepDone(Defs.Tutorial.First(t => t.Id == "pack")), "buying a pack ticks the pack step");
        }
        {
            // A slow first-time player must not starve while still learning: the first moon waits for the Food step.
            var s = new Sim(Defs.Ethics[0], 4) { TutorialOn = true };
            for (int i = 0; i < 4000; i++) s.Update(0.05f); // 200 s, over two standard moons
            Check(s.Moon == 1 && s.MoonTime == 0 && s.State == RunState.Playing, "with the tutorial on, moon 1 waits until Food is grown");
            s.SkippedSteps.Add(Defs.Rules.TutorialMoonWaitsFor);
            s.Update(1f);
            Check(s.MoonTime > 0, "skipping the Food step starts the moon clock");
            var off = new Sim(Defs.Ethics[0], 4);
            off.Update(1f);
            Check(off.MoonTime > 0, "with the tutorial off, the moon clock runs from the start");
        }

        Log.Info("Self-test: crowded systems keep their cards");
        {
            var s = Fresh();
            s.AddSystem(s.RollSystemType());
            for (int i = 0; i < 70; i++) s.Spawn("energy", s.Home.Center);
            for (int i = 0; i < 600; i++) s.Update(0.05f);
            int outside = s.Table.Stacks.Count(x => x.Root.Def.Id == "energy" && s.SystemAt(Sim.CardCenter(x)) != s.Home);
            Check(outside == 0, $"70 Energy piled in the capital stay in the capital ({outside} pushed out)");
        }
        {
            // Cards dumped in one spot, plus a battle, spread out until nothing overlaps anything.
            var s = Fresh();
            foreach (var id in new[] { "minerals", "food", "alloys", "research", "unity", "minerals", "food", "energy", "alloys", "research" })
                for (int i = 0; i < 2; i++) s.Spawn(id, s.Home.Center);
            var raider = s.Spawn("pirate_raider", s.Home.Center, jitter: false);
            s.Attack(s.Spawn("corvette", s.Home.Center).Stack!, raider);
            for (int i = 0; i < 40; i++) s.Update(0.05f); // 2 s: settled, battle still on
            Check(s.Table.Battles.Count == 1, "the battle is still being fought for the overlap check");
            var stacks = s.Table.Stacks.Where(x => !x.Traveling && s.SystemAt(Sim.CardCenter(x)) == s.Home).ToList();
            var bad = new List<string>();
            for (int i = 0; i < stacks.Count; i++)
                for (int j = i + 1; j < stacks.Count; j++)
                    if (Sim.Hit(Sim.StackArea(stacks[i]), Sim.StackArea(stacks[j]), 0)) bad.Add($"{stacks[i].Root.Def.Id}@{stacks[i].Pos} vs {stacks[j].Root.Def.Id}@{stacks[j].Pos}");
            foreach (var x in stacks)
            {
                if (Sim.Hit(Sim.StackArea(x), Sim.TitleArea(s.Home), 0)) bad.Add($"{x.Root.Def.Id}@{x.Pos} vs title");
                foreach (var bt in s.Table.Battles) if (Sim.Hit(Sim.StackArea(x), Sim.BattleArea(bt), 0)) bad.Add($"{x.Root.Def.Id}@{x.Pos} vs battle {Sim.BattleArea(bt)}");
            }
            Check(bad.Count == 0, $"{stacks.Count} stacks and the battle in the capital overlap nothing {string.Join("; ", bad)}");
        }

        {
            // A capital filled past capacity still overlaps nothing: resources and technologies gather into piles.
            var s = Fresh();
            var ids = new[] { "minerals", "food", "alloys", "research", "unity", "energy", "consumer_goods" };
            for (int i = 0; i < 70; i++) s.Spawn(ids[i % ids.Length], s.Home.Center);
            foreach (var t in new[] { "tech_red_laser", "tech_mass_driver", "tech_deflector", "tech_space_torpedoes", "tech_robotics", "tech_terraforming",
                                      "tech_afterburners", "tech_nanocomposite_armor", "tech_blue_laser", "tech_railgun" })
                s.Spawn(t, s.Home.Center);
            for (int i = 0; i < 6; i++) s.Spawn("pop", s.Home.Center);
            for (int i = 0; i < 120; i++) s.Update(0.05f);
            var stacks = s.Table.Stacks.Where(x => !x.Traveling && s.SystemAt(Sim.CardCenter(x)) == s.Home).ToList();
            int overlaps = 0;
            for (int i = 0; i < stacks.Count; i++)
                for (int j = i + 1; j < stacks.Count; j++)
                    if (Sim.Hit(Sim.StackArea(stacks[i]), Sim.StackArea(stacks[j]), 0)) overlaps++;
            int cards = s.AllCards.Count(c => ids.Contains(c.Def.Id) || c.Def.Category == "tech" || c.Def.Id == "pop");
            Check(overlaps == 0 && cards >= 70 + 10 + 6, $"an over-full capital overlaps nothing ({stacks.Count} stacks, {overlaps} overlaps, no cards lost)");
            int outside = stacks.Count(x => x.Pos.Y + Sim.StackHeight(x) > s.Home.Origin.Y + s.Home.Size.Y + 0.5f || x.Pos.X + Sim.CardW > s.Home.Origin.X + s.Home.Size.X + 0.5f);
            Check(outside == 0, $"even tall piles stay inside the capital's border ({outside} hang over)");
        }

        Log.Info("Self-test: claiming systems");
        {
            var s = Fresh();
            Check(s.Home.Claimed && s.ClaimedCount == 1, "you start owning only your capital");
            // A colony ship can't settle a planet in a system you don't own.
            var far = s.AddSystem(s.RollSystemType());
            var col = Build(s, new[] { "desert_world", "colony_ship" }, far.Center);
            for (int i = 0; i < 600; i++) s.Update(0.05f);
            Check(!s.Discovered.Contains("c_colonize"), "colonising needs the system claimed first");
            // Hostiles in a system block its claim.
            var amoeba = s.Spawn("space_amoeba", far.Origin + new Vector2(1200, 700), jitter: false);
            var claim = Build(s, new[] { "yellow_star", "construction_ship", "influence", "influence" }, far.Origin + new Vector2(200, 650));
            for (int i = 0; i < 20; i++) s.Update(0.05f); // long enough to try, short of the amoeba's first attack
            Check(!far.Claimed && s.Messages.Any(m => m.Contains("Clear the hostiles")), "a system with hostiles in it can't be claimed (and the game says why)");
            foreach (var c in amoeba.Stack?.Cards.ToList() ?? new List<Card>()) s.Remove(c);
            claim.Dirty = true;
            for (int i = 0; i < 600 && !far.Claimed; i++) s.Update(0.05f);
            for (int i = 0; i < 600 && !s.Discovered.Contains("c_colonize"); i++) { col.Dirty = true; s.Update(0.05f); }
            Check(far.Claimed && s.Discovered.Contains("c_colonize"), "Construction Ship + 2 Influence on its star claims it; then colonising works");
            // The claim limit.
            int tries = 0;
            while (s.ClaimedCount < Defs.Rules.ClaimLimit && tries++ < 20)
            {
                var z = s.AddSystem(s.RollSystemType());
                foreach (var h in s.StacksIn(z).Where(x => x.HasHostile).ToList()) foreach (var c in h.Cards.ToList()) s.Remove(c);
                Build(s, new[] { "yellow_star", "construction_ship", "influence", "influence" }, z.Origin + new Vector2(200, 650));
                for (int i = 0; i < 600 && !z.Claimed; i++) s.Update(0.05f);
            }
            var over = s.AddSystem(s.RollSystemType());
            foreach (var h in s.StacksIn(over).Where(x => x.HasHostile).ToList()) foreach (var c in h.Cards.ToList()) s.Remove(c);
            s.Messages.Clear();
            Build(s, new[] { "yellow_star", "construction_ship", "influence", "influence" }, over.Origin + new Vector2(200, 650));
            for (int i = 0; i < 600; i++) s.Update(0.05f);
            Check(s.ClaimedCount == Defs.Rules.ClaimLimit && !over.Claimed && s.Messages.Any(m => m.Contains("Claim limit")),
                $"claim limit: you can own {Defs.Rules.ClaimLimit} systems; the next claim is refused with a message");
        }

        Log.Info("Self-test: travel between systems");
        {
            var s = Fresh();
            var near = s.AddSystem(s.RollSystemType());   // next to the capital
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
            var st = Build(s, new[] { "alloys", "alloys", "precursor_artifact" }, new Vector2(400, 400));
            int got = s.Sell(st);
            Check(got == 18 && s.AllCards.Count(c => c.Def.Id == "energy") == 18, "selling 2 Alloys + Artifact gives 18 Energy");
            var other = s.AddSystem(s.RollSystemType());
            var raider = s.Spawn("pirate_raider", other.Center + new Vector2(200, 0), jitter: false);
            var homePop = Build(s, new[] { "pop" }, s.Home.Center);
            for (int i = 0; i < 400; i++) s.Update(0.05f);
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
            float sh = target.Shield, hp = target.Hp;
            t.Hit(new Gun { Profile = Defs.Component["space_torpedoes"], Damage = 10, Cooldown = 1 }, 1, new Card { Def = target.Def, Hp = 100, MaxHp = 100, Shield = 50, MaxShield = 50 }, 0);
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
            for (int i = 0; i < 200; i++) regen.Update(0.05f);
            Check(bs.Hp >= 19, $"Regenerative Hull Tissue heals the hull ({bs.Hp:0} after 10 s, from 10)");
            int dodged = 0;
            var ev = new Card { Def = Defs.Card["corvette"], Hp = 1e6f, MaxHp = 1e6f, Evasion = 0.4f };
            for (int i = 0; i < 1000; i++) if (t.Hit(new Gun { Damage = 1, Cooldown = 1 }, 1, ev, 0) == 0) dodged++;
            Check(dodged is > 330 and < 470, $"evasion dodges shots ({dodged}/1000 at 40%)");
        }

        Log.Info("Self-test: fitting components and admirals");
        {
            var s = Fresh();
            var corvette = s.Spawn("corvette", new Vector2(300, 300), jitter: false);
            Check(s.Fit(s.Spawn("red_laser", new Vector2(300, 600), jitter: false), corvette) == null && corvette.Guns.Count == 2,
                "a Red Laser fits a Corvette and adds a gun");
            Check(s.Fit(s.Spawn("deflector", new Vector2(300, 600), jitter: false), corvette) is { } full && full.Contains("no free slots"),
                "a Corvette has 1 slot; the second part is refused");
            var cruiser = s.Spawn("cruiser", new Vector2(600, 300), jitter: false);
            Check(s.Fit(s.Spawn("tachyon_lance", new Vector2(600, 600), jitter: false), cruiser) is { } big && big.Contains("bigger hull"),
                "a Tachyon Lance won't fit a Cruiser");
            var bs = s.Spawn("battleship", new Vector2(900, 300), jitter: false);
            float armour = bs.MaxArmor, shields = bs.MaxShield;
            s.Fit(s.Spawn("neutronium_armor", new Vector2(900, 600), jitter: false), bs);
            s.Fit(s.Spawn("improved_deflector", new Vector2(900, 600), jitter: false), bs);
            Check(bs.MaxArmor == armour + 55 && bs.MaxShield == shields + 30 && bs.ShieldRegen == 2, "armour and shield parts raise the ship's armour, shields and recharge");
            var adm = s.Spawn("admiral", new Vector2(900, 800), jitter: false);
            Check(s.AssignAdmiral(adm, bs) == null && bs.Admiral == adm && !s.AllCards.Contains(adm), "an Admiral takes command of a Battleship (and rides aboard)");
            var bt = new Battle();
            bt.Players.Add(bs);
            Check(Sim.PlayerMult(bt) == Defs.Card["admiral"].BoostMult, "the assigned Admiral boosts the fleet's damage");
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
            for (int i = 0; i < 600; i++) s.Update(0.05f);
            Check(drake.Battle == null, "Guardians sleep until you attack them");
            var raider = s.Spawn("pirate_raider", new Vector2(1400, 900), jitter: false);
            Build(s, new[] { "pop" }, new Vector2(1200, 900));
            for (int i = 0; i < 400; i++) s.Update(0.05f);
            Check(!s.AllCards.Any(c => c.Def.Id == "pop") || raider.Battle != null || !s.AllCards.Contains(raider), "raiders attack your Pops");
        }

        Log.Info("Self-test: moons");
        {
            var s = Fresh();
            Build(s, new[] { "pop" }, new Vector2(300, 300));
            Build(s, new[] { "pop" }, new Vector2(600, 300));
            Build(s, new[] { "food", "food" }, new Vector2(900, 300));
            Build(s, new[] { "homeworld" }, new Vector2(1200, 300));
            s.MoonTime = s.MoonSeconds - 0.01f;
            s.Update(0.05f);
            Check(s.Moon == 2 && s.AllCards.Count(c => c.Def.Id == "pop") == 1 && !s.AllCards.Any(c => c.Def.Id == "food"),
                "moon end: 2 Food feeds one Pop, the other starves");
        }
        {
            var s = Fresh("machine");
            Build(s, new[] { "drone", "drone" }, new Vector2(300, 300));
            Build(s, new[] { "energy" }, new Vector2(900, 300));
            s.MoonTime = s.MoonSeconds - 0.01f;
            s.Update(0.05f);
            Check(s.AllCards.Count(c => c.Def.Id == "drone") == 1, "moon end: Drones run on Energy");
        }

        Log.Info("Self-test: acts and crisis timeline, every difficulty");
        foreach (var diff in Defs.Difficulties)
        foreach (var crisis in Defs.Crises)
        {
            int seed = 0;
            Sim s;
            do s = new Sim(Defs.Ethics[0], ++seed, null, diff, Defs.MoonLengths[0]); while (s.Crisis != crisis);
            // Food for the whole run in full piles, and a big home guard so the run survives to the boss, laid out on the
            // capital board like a player would.
            for (int i = 0; i < 25; i++) Build(s, Enumerable.Repeat("food", Defs.Rules.MaxStack), s.Home.Origin + new Vector2(60 + (i % 9) * 150, 620 + (i / 9) * 110));
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
            for (int i = 0; i < 600; i++) s.Update(0.05f);
            Check(s.Discovered.Count > 0 || s.Systems.Count >= 1, $"{e.Id}: 30 s of play runs without error");
        }

        Log.Info(_fails == 0 ? "Self-test: ALL PASS" : $"Self-test: {_fails} FAILED");
        return _fails == 0 ? 0 : 1;
    }
}
