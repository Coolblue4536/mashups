namespace StellarAscension;

/// <summary>Headless runs where the AI also plays the player's seat: pacing and balance numbers for both acts.</summary>
public static class Playtest
{
    public sealed record Act1Result(int Seed, string Civ, int Turns, int Year, string Outcome, int PlayerCities, int PlayerPop, int PlayerTechs,
        int PlayerWonders, int SpaceRank, int MaxCities, int Naturals, double Seconds);

    public static Act1Result PlayAct1(Content content, GameSetup setup, Action<Act1State>? everyTurn = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var s = Act1.NewGame(setup, content);
        while (s.Outcome == "")
        {
            var p = s.Player;
            Act1Ai.TakeTurn(s, content, p);
            Act1Ai.PlaceSettlers(s, content, p);
            Act1.EndTurn(s, content);
            everyTurn?.Invoke(s);
        }
        var pc = s.CitiesOf(s.Player.Id).ToList();
        return new Act1Result((int)setup.Seed, s.Player.CivType, s.Turn, s.Year, s.Outcome, pc.Count, pc.Sum(c => c.Pop), s.Player.Techs.Count,
            pc.Sum(c => c.Wonders.Count), s.SpaceOrder.IndexOf(s.Player.Id) + 1, s.Civs.Max(c => s.CitiesOf(c.Id).Count()),
            s.Tiles.Count(t => t.Natural != null), sw.Elapsed.TotalSeconds);
    }

    public static int Act1Balance(Content content, int runs, string size, string difficulty)
    {
        var rng = new Rng((ulong)DateTime.Now.Ticks);
        var results = new List<Act1Result>();
        for (int i = 0; i < runs; i++)
        {
            var civ = content.Civs[rng.Int(content.Civs.Count)];
            var setup = new GameSetup
            {
                Civ = civ.Type, Leader = civ.Leader, MapSize = size, Difficulty = difficulty, SpaceRace = "standard",
                Rivals = Defs.MapSize[size].DefaultRivals, Seed = (ulong)(1000 + i),
            };
            var marks = new Dictionary<string, int>();
            var r = PlayAct1(content, setup, st =>
            {
                var pl = st.Player;
                foreach (var (k, ok) in new[] { ("rocketry", pl.Techs.Contains("TECH_ROCKETRY")), ("satellites", pl.Techs.Contains("TECH_SATELLITES")),
                             ("spaceport", st.CitiesOf(pl.Id).Any(c => c.Buildings.Contains("DISTRICT_SPACEPORT"))), ("satellite", pl.SatelliteDone) })
                    if (ok && !marks.ContainsKey(k)) marks[k] = st.Turn;
                if (i > 0) return;
                if (st.Turn % 25 != 0) return;
                var y = Act1.CivYields(st, content, st.Player);
                Log.Info($"  turn {st.Turn} {Act1.YearText(st.Year)}: cities {st.CitiesOf(st.Player.Id).Count()}, pop {st.CitiesOf(st.Player.Id).Sum(c => c.Pop)}, "
                         + $"food {y.Food:0} prod {y.Production:0} gold {y.Gold:0} sci {y.Science:0}, techs {st.Player.Techs.Count}, era {Act1.EraIndex(content, st.Player)}, "
                         + $"rival best techs {st.Civs.Where(c => !c.IsPlayer).Max(c => c.Techs.Count)}");
            });
            results.Add(r);
            Log.Info($"act1 run {i + 1}: {r}");
            Log.Info("  milestones: " + string.Join(", ", marks.Select(m => $"{m.Key} t{m.Value}")));
        }
        Log.Info($"act1 {size}/{difficulty}: turns avg {results.Average(r => r.Turns):0} (min {results.Min(r => r.Turns)}, max {results.Max(r => r.Turns)}), "
                 + $"year avg {results.Average(r => r.Year):0}, player cities avg {results.Average(r => r.PlayerCities):0.0}, techs avg {results.Average(r => r.PlayerTechs):0}, "
                 + $"player first to space {results.Count(r => r.SpaceRank == 1)}/{runs}, sec/run {results.Average(r => r.Seconds):0.0}");
        return 0;
    }

    public static Act2State PlayAct2(Content content, Act1State a1, Action<Act2State>? everyMonth = null)
    {
        var s = Handoff.FromAct1(a1, content);
        Log.Info($"   galaxy: {s.Systems.Count} systems, {s.Systems.Sum(x => x.Lanes.Count) / 2} lanes, homes (habitable, claimable within 3 jumps): "
                 + string.Join(" ", Galaxy.Fairness(s).Select(f => $"({f.habitable},{f.claimable})")));
        while (s.Outcome == "")
        {
            Act2Ai.Think(s, content, s.Player);
            int before = s.Month;
            Act2.EndTurn(s, content);
            if (everyMonth != null) for (int m = before + 1; m <= s.Month; m++) if (m % 12 == 0 || m == s.Month) { everyMonth(s); break; }
            if (s.Month > 12 * 400) throw new InvalidOperationException("Act 2 never ended");
        }
        return s;
    }

    public static int FullBalance(Content content, int runs, string size)
    {
        for (int i = 0; i < runs; i++)
        {
            var civ = content.Civs[(i * 7) % content.Civs.Count];
            var setup = new GameSetup { Civ = civ.Type, Leader = civ.Leader, MapSize = size, SpaceRace = new[] { "relaxed", "standard", "ruthless" }[i % 3],
                                        Rivals = Defs.MapSize[size].DefaultRivals, Seed = (ulong)(5000 + i) };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var a1 = Act1.NewGame(setup, content);
            while (a1.Outcome == "") { Act1Ai.TakeTurn(a1, content, a1.Player); Act1Ai.PlaceSettlers(a1, content, a1.Player); Act1.EndTurn(a1, content); }
            Log.Info($"full run {i + 1}: act 1 {a1.Outcome} at turn {a1.Turn} ({Act1.YearText(a1.Year)}), space order {string.Join(",", a1.SpaceOrder)}, {sw.Elapsed.TotalSeconds:0.0}s");
            if (a1.Outcome != "space") continue;
            int wars = 0;
            var s = PlayAct2(content, a1, st =>
            {
                wars = Math.Max(wars, st.Wars.Count);
                if (i == 0 && st.Month % 120 == 0)
                    Log.Info($"   {st.Year}: " + string.Join(" | ", st.Empires.Where(e => e.Alive).Select(e =>
                        { var r = Act2.Report(st, e); return $"{e.Adjective}: sys {st.SystemsOf(e.Id).Count()} planets {st.PlanetsOf(e.Id).Count()} pops {r.Pops} fleet {r.FleetPower:0} techs {e.Techs.Count} E{r.Income["energy"]:0} M{r.Income["minerals"]:0} A{r.Income["alloys"]:0} R{r.Income["research"]:0}"; })));
            });
            Log.Info($"   act 2 {s.Outcome} in {s.Year}, {s.Empires.Count(e => e.Alive)}/{s.Empires.Count} empires alive, max wars at once {wars}, "
                     + $"scores " + string.Join(", ", s.Empires.OrderByDescending(e => e.Score).Select(e => $"{e.Adjective}{(e.IsPlayer ? "*" : "")} {e.Score}")) + $", {sw.Elapsed.TotalSeconds:0.0}s");
        }
        return 0;
    }
}
