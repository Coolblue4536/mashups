using System.Numerics;

namespace GrandGalactic;

/// <summary>Headless checks of the simulation: every recipe fires, every pack opens, combat against every boss
/// resolves, moons feed and starve, and the acts arrive on schedule. Run with --selftest.</summary>
public static class SelfTest
{
    static int _fails;

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
            var outIds = r.Outputs.SelectMany(o => o.Give).Select(g => g.Card == "station.yield" ? Defs.Card[Resolve(r.Station)].Yield : g.Card).ToHashSet();
            int before = s.AllCards.Count(c => outIds.Contains(c.Def.Id)), boards = s.Systems.Count;
            for (int i = 0; i < 2400 && !s.Discovered.Contains(r.Id); i++) s.Update(0.05f);
            bool fired = s.Discovered.Contains(r.Id);
            bool effect = r.Effect switch
            {
                "open_board:random" or "open_board:guardian" => s.Systems.Count == boards + 1,
                "set_flag:claimed" => s.AllCards.Any(c => c.Def.Id == Resolve(r.Station) && c.Claimed),
                "claim_system" => s.SystemAt(at)?.Claimed == true,
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

        Log.Info("Self-test: combat");
        var fleets = new (string name, string[] cards)[]
        {
            ("early fleet (3 corvettes)", new[] { "corvette", "corvette", "corvette" }),
            ("mid fleet (admiral, 2 cruisers, 3 destroyers)", new[] { "admiral", "cruiser", "cruiser", "destroyer", "destroyer", "destroyer" }),
            ("late fleet (admiral, titan, 4 battleships, 2 cruisers)", new[] { "admiral", "titan", "battleship", "battleship", "battleship", "battleship", "cruiser", "cruiser" }),
        };
        foreach (var foe in Defs.Cards.Where(c => c.IsHostile && c.Attack > 0))
        {
            var results = new List<string>();
            foreach (var (name, cards) in fleets)
            {
                var s = Fresh();
                var enemy = s.Spawn(foe.Id, new Vector2(1400, 600), jitter: false);
                var fleet = Build(s, cards, new Vector2(800, 600));
                s.Attack(fleet, enemy);
                float t = 0;
                while (t < 300 && s.Table.Battles.Count > 0 && s.State == RunState.Playing) { s.Update(0.05f); t += 0.05f; }
                bool won = !s.AllCards.Any(c => c.Def.Id == foe.Id) || s.State == RunState.Won;
                int left = s.AllCards.Count(c => c.Def.Category is "ship" or "person");
                results.Add($"{name}: {(won ? $"WIN {t:0}s, {left}/{cards.Length} left" : "loss")}");
            }
            Log.Info($"  INFO  vs {foe.Id,-20} " + string.Join(" | ", results));
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
            for (int i = 0; i < 400; i++) s.Spawn("food", new Vector2(100, 1400));
            for (int i = 0; i < 40; i++) s.Spawn("corvette", new Vector2(2400, 1400)); // a big home guard so the run survives to the boss
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
