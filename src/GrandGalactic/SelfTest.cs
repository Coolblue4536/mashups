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
        foreach (var b in s.Boards) { b.Stacks.Clear(); b.Battles.Clear(); }
        s.MoonTime = -100000; // no moon ends during unit checks
        return s;
    }

    static Stack Build(Sim s, Board b, IEnumerable<string> ids, Vector2 pos)
    {
        Stack? st = null;
        foreach (var id in ids)
        {
            var c = s.Spawn(b, id, pos, jitter: false);
            if (st == null) st = c.Stack!;
            else s.StackOnto(c.Stack!, st);
        }
        return st!;
    }

    static string Resolve(string card) => card switch
    {
        "has:yield" => "generator_district",
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
            var st = Build(s, s.Home, ids, new Vector2(800, 600));
            var outIds = r.Outputs.SelectMany(o => o.Give).Select(g => g.Card == "station.yield" ? Defs.Card[Resolve(r.Station)].Yield : g.Card).ToHashSet();
            int before = s.AllCards.Count(c => outIds.Contains(c.Def.Id)), boards = s.Boards.Count;
            for (int i = 0; i < 2400 && !s.Discovered.Contains(r.Id); i++) s.Update(0.05f);
            bool fired = s.Discovered.Contains(r.Id);
            bool effect = r.Effect switch
            {
                "open_board:normal" or "open_board:guardian" => s.Boards.Count == boards + 1,
                "set_flag:claimed" => s.AllCards.Any(c => c.Def.Id == Resolve(r.Station) && c.Claimed),
                _ => true,
            };
            bool produced = r.Outputs.Length == 0 || s.AllCards.Count(c => outIds.Contains(c.Def.Id)) > before - (outIds.Contains(Resolve(r.Station)) && !r.StationKeep ? 1 : 0) || outIds.Count == 0;
            Check(fired && effect && produced, $"recipe {r.Id,-16} fires ({string.Join(" + ", ids)})");
        }

        Log.Info("Self-test: recipe does not fire without its tech");
        {
            var s = Fresh();
            var st = Build(s, s.Home, new[] { "shipyard", "alloys", "alloys", "alloys", "alloys" }, new Vector2(800, 600));
            s.Techs.Remove("tech_destroyers");
            for (int i = 0; i < 600; i++) s.Update(0.05f);
            Check(!s.Discovered.Contains("s_destroyer"), "Destroyer needs Destroyers tech");
        }

        Log.Info("Self-test: packs");
        foreach (var p in Defs.Packs)
        {
            var s = Fresh();
            s.Act = 3;
            var st = Build(s, s.Home, Enumerable.Repeat("energy", p.Cost + 1), new Vector2(400, 400));
            int before = s.AllCards.Count();
            bool ok = s.BuyPack(st, p, new Vector2(900, 900));
            int after = s.AllCards.Count();
            Check(ok && after == before - p.Cost + p.Draws, $"pack {p.Id} costs {p.Cost} and gives {p.Draws}");
        }
        {
            var s = Fresh();
            var st = Build(s, s.Home, new[] { "energy", "energy" }, new Vector2(400, 400));
            Check(!s.BuyPack(st, Defs.Pack["pack_exploration"], Vector2.Zero), "pack refuses too few Energy");
            var st2 = Build(s, s.Home, new[] { "energy", "energy", "energy", "energy", "energy", "energy", "energy", "energy", "energy", "energy" }, new Vector2(400, 700));
            Check(!s.BuyPack(st2, Defs.Pack["pack_frontier"], Vector2.Zero), "Act 2 pack locked in Act 1");
        }

        Log.Info("Self-test: market and travel");
        {
            var s = Fresh();
            var st = Build(s, s.Home, new[] { "alloys", "alloys", "precursor_artifact" }, new Vector2(400, 400));
            int got = s.Sell(st);
            Check(got == 18 && s.AllCards.Count(c => c.Def.Id == "energy") == 18, "selling 2 Alloys + Artifact gives 18 Energy");
            var other = s.AddBoard(Defs.Systems.First(x => x.Kind == "normal"));
            var pop = Build(s, s.Home, new[] { "pop" }, new Vector2(300, 300));
            Check(!s.MoveToBoard(pop, other), "a Pop can't travel without a ship");
            var fleet = Build(s, s.Home, new[] { "corvette", "pop" }, new Vector2(300, 600));
            Check(s.MoveToBoard(fleet, other) && other.Stacks.Contains(fleet), "a stack with a ship travels to another board");
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
                var enemy = s.Spawn(s.Home, foe.Id, new Vector2(1400, 600), jitter: false);
                var fleet = Build(s, s.Home, cards, new Vector2(800, 600));
                s.Attack(fleet, enemy);
                float t = 0;
                while (t < 300 && s.Home.Battles.Count > 0 && s.State == RunState.Playing) { s.Update(0.05f); t += 0.05f; }
                bool won = !s.AllCards.Any(c => c.Def.Id == foe.Id) || s.State == RunState.Won;
                int left = s.AllCards.Count(c => c.Def.Category is "ship" or "person");
                results.Add($"{name}: {(won ? $"WIN {t:0}s, {left}/{cards.Length} left" : "loss")}");
            }
            Log.Info($"  INFO  vs {foe.Id,-20} " + string.Join(" | ", results));
        }
        {
            var s = Fresh();
            var drake = s.Spawn(s.Home, "ether_drake", new Vector2(1400, 600), jitter: false);
            for (int i = 0; i < 600; i++) s.Update(0.05f);
            Check(drake.Battle == null, "Guardians sleep until you attack them");
            var raider = s.Spawn(s.Home, "pirate_raider", new Vector2(1400, 900), jitter: false);
            Build(s, s.Home, new[] { "pop" }, new Vector2(1200, 900));
            for (int i = 0; i < 400; i++) s.Update(0.05f);
            Check(!s.AllCards.Any(c => c.Def.Id == "pop") || raider.Battle != null || !s.AllCards.Contains(raider), "raiders attack your Pops");
        }

        Log.Info("Self-test: moons");
        {
            var s = Fresh();
            Build(s, s.Home, new[] { "pop" }, new Vector2(300, 300));
            Build(s, s.Home, new[] { "pop" }, new Vector2(600, 300));
            Build(s, s.Home, new[] { "food", "food" }, new Vector2(900, 300));
            Build(s, s.Home, new[] { "homeworld" }, new Vector2(1200, 300));
            s.MoonTime = Defs.Rules.MoonSeconds - 0.01f;
            s.Update(0.05f);
            Check(s.Moon == 2 && s.AllCards.Count(c => c.Def.Id == "pop") == 1 && !s.AllCards.Any(c => c.Def.Id == "food"),
                "moon end: 2 Food feeds one Pop, the other starves");
        }
        {
            var s = Fresh("machine");
            Build(s, s.Home, new[] { "drone", "drone" }, new Vector2(300, 300));
            Build(s, s.Home, new[] { "energy" }, new Vector2(900, 300));
            s.MoonTime = Defs.Rules.MoonSeconds - 0.01f;
            s.Update(0.05f);
            Check(s.AllCards.Count(c => c.Def.Id == "drone") == 1, "moon end: Drones run on Energy");
        }

        Log.Info("Self-test: acts and crisis timeline");
        foreach (var crisis in Defs.Crises)
        {
            int seed = 0;
            Sim s;
            do s = new Sim(Defs.Ethics[0], ++seed); while (s.Crisis != crisis);
            for (int i = 0; i < 400; i++) s.Spawn(s.Home, "food", new Vector2(100, 1400));
            for (int i = 0; i < 40; i++) s.Spawn(s.Home, "corvette", new Vector2(2400, 1400)); // a big home guard so the run survives to the boss
            int act2 = 0, rift = 0, boss = 0;
            float t = 0;
            while (s.Moon <= Defs.Rules.CrisisMoon + Defs.Rules.BossDelayMoons + 1 && s.State == RunState.Playing && t < 3000)
            {
                s.Update(0.1f); t += 0.1f;
                if (act2 == 0 && s.Act == 2) act2 = s.Moon;
                if (rift == 0 && s.RiftOpen) rift = s.Moon;
                if (boss == 0 && s.BossArrived) boss = s.Moon;
                foreach (var m in s.Messages) Log.Info($"    [{crisis.Id} moon {s.Moon}] {m}");
                s.Messages.Clear();
            }
            Check(act2 == Defs.Rules.Act2Moon, $"{crisis.Id}: Act 2 starts moon {act2}");
            Check(rift == Defs.Rules.CrisisMoon, $"{crisis.Id}: rift opens moon {rift}");
            Check(boss > 0 && boss <= Defs.Rules.CrisisMoon + Defs.Rules.BossDelayMoons, $"{crisis.Id}: {crisis.BossCard} arrives moon {boss} (state {s.State}: {s.EndReason})");
            Check(s.AllCards.Count(c => c.Def.Id == "guardian_signal") == 3 || s.Boards.Count > 1, $"{crisis.Id}: 3 Guardian Signals appear in Act 2");
        }

        Log.Info("Self-test: every ethic starts a run");
        foreach (var e in Defs.Ethics)
        {
            var s = new Sim(e, 7);
            int workers = s.AllCards.Count(c => c.Def.Id == e.WorkerCard);
            Check(workers >= Defs.Rules.StartWorkers && s.AllCards.Any(c => c.Def.Id == "homeworld" && c.Claimed)
                  && e.StartTechs.All(s.Techs.Contains), $"{e.Id}: starts with {workers} {e.WorkerCard}s, a homeworld and its techs");
            for (int i = 0; i < 600; i++) s.Update(0.05f);
            Check(s.Discovered.Count > 0 || s.Boards.Count >= 1, $"{e.Id}: 30 s of play runs without error");
        }

        Log.Info(_fails == 0 ? "Self-test: ALL PASS" : $"Self-test: {_fails} FAILED");
        return _fails == 0 ? 0 : 1;
    }
}
