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
                if (i > 0 && Environment.GetEnvironmentVariable("SA_VERBOSE") == null) return;
                if (st.Turn % 25 != 0) return;
                var y = Act1.CivYields(st, content, st.Player);
                Log.Info($"  turn {st.Turn} {Act1.YearText(st.Year)}: cities {st.CitiesOf(st.Player.Id).Count()}, pop {st.CitiesOf(st.Player.Id).Sum(c => c.Pop)}, "
                         + $"food {y.Food:0} prod {y.Production:0} gold {y.Gold:0} sci {y.Science:0}, techs {st.Player.Techs.Count}, era {Act1.EraIndex(content, st.Player)}, "
                         + $"rival best techs {st.Civs.Where(c => !c.IsPlayer).Max(c => c.Techs.Count)}, units {st.Units.Count} ({string.Join(",", st.Civs.Select(c => st.UnitsOf(c.Id).Count()))}), "
                         + $"best unit {st.Units.Select(u => content.Unit[u.Type]).OrderByDescending(u => u.Combat).Select(u => u.Type[5..]).FirstOrDefault()}, wars {st.Wars.Count}, alive {st.Civs.Count(c => c.Alive)}");
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

    public static Act2State PlayAct2(Content content, Act1State a1, Action<Act2State>? everyMonth = null, Action<Act2State>? answer = null)
    {
        var s = Handoff.FromAct1(a1, content);
        Log.Info($"   galaxy: {s.Systems.Count} systems, {s.Systems.Sum(x => x.Lanes.Count) / 2} lanes, homes (habitable, claimable within 3 jumps): "
                 + string.Join(" ", Galaxy.Fairness(s).Select(f => $"({f.habitable},{f.claimable})")));
        while (s.Outcome == "")
        {
            answer?.Invoke(s);
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

    const string LongName = "\u0398\u03b5\u03c3\u03c3\u03b1\u03bb\u03bf\u03bd\u03af\u03ba\u03b7-\u03a0\u03cc\u03bb\u03b7 \u03c4\u03c9\u03bd \u0392\u03b1\u03c3\u03b9\u03bb\u03ad\u03c9\u03bd";

    /// <summary>--matrix: ten games across the setup options. Each checks the handoff and that every name can be drawn.</summary>
    public static int Matrix(Content content)
    {
        // space race, map size, rivals, difficulty, player pace (1.6 = first to space, 1 = middle, 0.55 = last), long non-English names, game length
        var runs = new (string race, string size, int rivals, string diff, double pace, bool rename, string length)[]
        {
            ("relaxed", "small", 1, "DIFFICULTY_PRINCE", 1.6, false, "short"),
            ("relaxed", "large", 10, "DIFFICULTY_KING", 0.55, false, "standard"),
            ("relaxed", "standard", 5, "DIFFICULTY_SETTLER", 1.0, true, "long"),
            ("standard", "small", 3, "DIFFICULTY_PRINCE", 1.0, false, "standard"),
            ("standard", "large", 10, "DIFFICULTY_PRINCE", 1.6, true, "standard"),
            ("standard", "standard", 5, "DIFFICULTY_EMPEROR", 0.55, false, "short"),
            ("standard", "standard", 7, "DIFFICULTY_DEITY", 1.0, false, "long"),
            ("ruthless", "small", 2, "DIFFICULTY_PRINCE", 1.6, false, "standard"),
            ("ruthless", "standard", 5, "DIFFICULTY_PRINCE", 0.55, false, "standard"),
            ("ruthless", "large", 7, "DIFFICULTY_CHIEFTAIN", 1.0, true, "standard"),
        };
        string[] others = { "\u041d\u043e\u0432\u0433\u043e\u0440\u043e\u0434-\u0421\u0435\u0432\u0435\u0440\u0441\u043a\u0438\u0439 \u0412\u0435\u043b\u0438\u043a\u0438\u0439",
                            "\u014csaka-Ky\u014dto no Miyako Sh\u014dgunate", "Reykjav\u00edkurborg \u00c6\u00f0ri \u00deingvellir" };
        int fails = 0, offers1 = 0, offers2 = 0;
        void Check(bool ok, string what) { if (!ok) { fails++; Log.Info("   FAIL " + what); } }
        for (int i = 0; i < runs.Length; i++)
        {
            var (race, size, rivals, diff, pace, rename, length) = runs[i];
            var civ = content.Civs[(i * 5 + 3) % content.Civs.Count];
            var setup = new GameSetup { Civ = civ.Type, Leader = civ.Leader, MapSize = size, SpaceRace = race, Rivals = rivals, Difficulty = diff, GameLength = length, Seed = (ulong)(9100 + i),
                                        GalaxySize = Defs.GalaxySizes[i % Defs.GalaxySizes.Length].Id, Aliens = i % 5, GalaxyDifficulty = Defs.GalaxyDifficulties[i % Defs.GalaxyDifficulties.Length].Id };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var a1 = Act1.NewGame(setup, content);
            a1.Civs[0].YieldMult = pace;
            if (rename) a1.CitiesOf(a1.Player.Id).First().Name = LongName;
            int renamed = 0, got1 = 0, took1 = 0;
            while (a1.Outcome == "")
            {
                foreach (var o in a1.Offers.ToList())
                {
                    got1++;
                    if (got1 % 3 == 0) Diplomacy1.Decline(a1, o); else if (Diplomacy1.Accept(a1, content, o)) took1++;
                }
                Act1Ai.TakeTurn(a1, content, a1.Player); Act1Ai.PlaceSettlers(a1, content, a1.Player); Act1.EndTurn(a1, content);
                if (rename) foreach (var c in a1.CitiesOf(a1.Player.Id).Where(c => !c.Capital).Take(others.Length)) if (!others.Contains(c.Name) && renamed < others.Length) c.Name = others[renamed++];
            }
            int wonders = a1.Cities.Sum(c => c.Wonders.Count);
            int place = a1.SpaceOrder.IndexOf(a1.Player.Id) + 1;
            string where = place == 0 ? "never reached space" : $"#{place} of {a1.SpaceOrder.Count} in space";
            Log.Info($"matrix {i + 1}: {race}, {size}, {rivals} rivals, {Defs.Difficulty[diff].Name}, {length} game{(rename ? ", long non-English names" : "")}; {Act1.CivName(content, a1.Player)}: act 1 {a1.Outcome} at turn {a1.Turn} ({Act1.YearText(a1.Year)}), player {where}, {wonders} wonders, {a1.Tiles.Count(t => t.Natural != null)} natural wonders, "
                     + $"{a1.Units.Count} units (best {a1.Units.Select(u => content.Unit[u.Type]).OrderByDescending(u => u.Combat).Select(u => content.Text(u.NameLoc)).FirstOrDefault()}), {a1.Civs.Count(c => !c.Alive)} civs destroyed, {a1.Cities.Count(c => c.Founder != c.Owner)} cities captured, {got1} offers to the player ({took1} accepted)");
            offers1 += got1;
            Check(a1.Majors.Count() == rivals + 1, "civ count");
            Check(a1.Cities.Select(c => c.Name).Concat(a1.Majors.Select(c => Act1.CivName(content, c))).All(n => !Gfx.Fold(n).Contains('?')), "every Act 1 name drawable");
            if (race == "ruthless") Check((a1.Outcome == "defeat") == (a1.SpaceOrder.Count > 0 && a1.SpaceOrder[0] != a1.Player.Id), "Ruthless: defeat exactly when a rival is first");
            if (a1.Outcome != "space") continue;
            int got2 = 0, took2 = 0;
            var s = PlayAct2(content, a1, answer: st =>
            {
                foreach (var o in st.Offers.ToList())
                {
                    got2++;
                    if (got2 % 3 == 0) Diplomacy2.Decline(st, o); else if (Diplomacy2.Accept(st, o)) took2++;
                }
            });
            offers2 += got2;
            Check(s.Empires.Count(e => e.Kind == "civ") == a1.Majors.Count(cv => cv.Alive), "every surviving civ became an empire");
            Check(s.Systems.Count(x => x.Home) == s.Empires.Count, "one home per empire");
            Check(Handoff.Bfs(s, 0).Count == s.Systems.Count, "galaxy connected");
            var bad = s.Systems.Select(x => x.Name).Concat(s.Systems.SelectMany(x => x.Planets).Select(p => p.Name)).Concat(s.Empires.Select(e => e.Name)).Where(n => !Gfx.Drawable(Gfx.Fold(n)) || Gfx.Fold(n).Contains('?') && !n.Contains('?')).ToList();
            Check(bad.Count == 0, "every Act 2 name drawable: " + string.Join(", ", bad.Select(n => $"{n} ({string.Join(" ", n.EnumerateRunes().Where(r => !Gfx.Drawable(r.ToString())).Select(r => $"U+{r.Value:X4}"))})")));
            // (The player may have been conquered by the end, so look at the home systems, not the player's capital.)
            Check(!rename || s.Systems.Any(x => x.Home && x.Planets.Any(p => p.Homeworld && p.Name == LongName)), "renamed capital carried to the homeworld");
            Check(!rename || s.Systems.SelectMany(x => x.Planets).Any(p => others.Contains(p.Name)), "renamed cities name the other home planets");
            Check(s.Outcome != "", "act 2 ended");
            Check(s.Empires.Count(e => e.Kind == "alien") == setup.Aliens, "chosen number of alien empires");
            Log.Info($"   act 2 {s.Outcome} in {s.Year}; {setup.GalaxySize} galaxy, {s.Systems.Count} systems, {setup.Aliens} aliens, AI {setup.GalaxyDifficulty}; top scores " + string.Join(", ", s.Empires.OrderByDescending(e => e.Score).Take(4).Select(e => $"{e.Adjective}{(e.IsPlayer ? "*" : "")} {e.Score}")) + $"; player {s.Player.Score}; {got2} offers to the player ({took2} accepted); {sw.Elapsed.TotalSeconds:0.0}s");
        }
        Check(offers1 > 0, "AI civs offer the player deals in Act 1");
        Check(offers2 > 0, "AI empires offer the player deals in Act 2");
        Log.Info(fails == 0 ? "matrix: ALL PASS" : $"matrix: {fails} FAILED");
        return fails == 0 ? 0 : 1;
    }
}
