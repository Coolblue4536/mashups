using System.Text.Json;

namespace StellarAscension;

/// <summary>--selftest: checks the readers on this PC's installs and plays both acts headless. Prints ALL PASS or the failures.</summary>
public static class SelfTest
{
    static int _fail;

    static void Check(bool ok, string what)
    {
        Log.Info((ok ? "PASS " : "FAIL ") + what);
        if (!ok) _fail++;
    }

    public static int Run(string? civ6Path, string? stellarisPath)
    {
        _fail = 0;
        if (civ6Path == null || stellarisPath == null)
        {
            Log.Info("self-test needs both games installed (Civilization VI and Stellaris)");
            return 2;
        }
        var civ6 = new Civ6Data(civ6Path);
        var st = new StellarisData(stellarisPath);
        var content = new Content(civ6, st);

        // ---- readers
        Check(content.Civs.Count >= 18, $"Civ VI civilizations read ({content.Civs.Count})");
        Check(content.Techs.Count == 68, $"Civ VI base tech tree read ({content.Techs.Count} techs)");
        Check(civ6.Icon("ICON_TECH_ROCKETRY") is { W: > 64 }, "Civ VI tech icon decoded from Icons.blp");
        Check(civ6.Icon("ICON_BUILDING_PYRAMIDS") is { W: > 64 }, "Civ VI wonder icon decoded");
        Check(civ6.LeaderPicture("LEADER_TRAJAN") is { H: > 500 }, "Civ VI leader picture decoded");
        Check(civ6.MapTexture("Features_Forest_Visible") is { W: 256 }, "Civ VI strategic-view sprite decoded (BC2)");
        Check(civ6.MapTexture("TER_Grass_B", 512) is { W: 512 }, "Civ VI terrain texture decoded (BC3, smaller mip)");
        foreach (var t in Defs.Terrains.Where(t => t.Ground != "none"))
            Check(civ6.MapTexture(t.Ground, 64) != null, $"terrain ground {t.Ground}");
        foreach (var w in Defs.Wonders) Check(civ6.MapTexture(w.Sprite, 64) != null, $"wonder sprite {w.Sprite}");
        Check(st.StarNames.Count > 100 && st.Prescripted.Count > 10 && st.Music.Count > 0, "Stellaris star names, empires and music read");
        var res = new AssetResolver(civ6, st);
        foreach (var a in Defs.AssetRefs.Where(a => a.Game == "stellaris" && a.Kind == "image")) Check(res.Stellaris(a.Id) != null, $"Stellaris picture {a.Id}");

        // ---- Act 1
        var civ = content.Civs.First(c => c.Leader == "LEADER_TRAJAN");
        var setup = new GameSetup { Civ = civ.Type, Leader = civ.Leader, MapSize = "small", Rivals = 3, Seed = 777, SpaceRace = "standard" };
        var a1 = Act1.NewGame(setup, content);
        Check(a1.Civs.Count == 4 && a1.Cities.Count == 4, "new game: player and 3 rivals, each with a capital");
        Check(a1.Tiles.Count(t => t.Natural != null) == Defs.MapSize["small"].NaturalWonders, "natural wonders placed");
        Check(a1.Tiles.Any(t => t.Revealed) && a1.Tiles.Any(t => !t.Revealed), "fog of war: some land seen, some not");
        while (a1.Outcome == "" && a1.Turn < 120) { Act1Ai.TakeTurn(a1, content, a1.Player); Act1Ai.PlaceSettlers(a1, content, a1.Player); Act1.EndTurn(a1, content); }
        Check(a1.Tiles.Any(t => t.Built?.StartsWith("district:") == true), "districts stand on their own tiles");
        Check(RoundTrip(new SaveGame { Act1 = a1 }), "Act 1 save and load give the same game");
        while (a1.Outcome == "") { Act1Ai.TakeTurn(a1, content, a1.Player); Act1Ai.PlaceSettlers(a1, content, a1.Player); Act1.EndTurn(a1, content); }
        Check(a1.Outcome == "space" && a1.SpaceOrder.Contains(a1.Player.Id), $"Act 1 ends with the player in space (turn {a1.Turn}, {Act1.YearText(a1.Year)})");
        Check(a1.Year is > 1800 and < 2150, "the calendar reaches the space age");

        // ---- handoff and galaxy
        var a2 = Handoff.FromAct1(a1, content);
        Check(a2.Empires.Count(e => e.Kind == "civ") == a1.Civs.Count && a2.Empires.Any(e => e.Kind == "alien"), "every civ becomes an empire, plus Stellaris aliens");
        Check(a2.Systems.Count(x => x.Home) == a2.Empires.Count, "one home system per empire");
        Check(Handoff.Bfs(a2, 0).Count == a2.Systems.Count, "every system is reachable by hyperlane");
        Check(!LanesCross(a2), "no two hyperlanes cross");
        var homes = a2.Systems.Where(x => x.Home).ToList();
        var lanes = homes.Select(h => h.Lanes.Count).Distinct().Count();
        Check(lanes == 1, "every home has the same number of hyperlanes (one identical arm each)");
        var hw = a2.Sys(a2.Player.Capital).Planets.First(p => p.Homeworld);
        Check(hw.Name == a1.CitiesOf(a1.Player.Id).First(c => c.Capital).Name, "homeworld named after the capital");
        Check(hw.Wonders.Count == a1.CitiesOf(a1.Player.Id).Sum(c => c.Wonders.Count), "wonders become homeworld features");
        var naturals = a1.Tiles.Where(t => t.Natural != null).Select(t => content.Name(t.Natural!)).Distinct().Count();
        Check(a2.Systems.Count(x => x.RareFeature != null) == Math.Min(8, naturals), "natural wonders become rare systems");
        Check(a2.Systems.Where(x => x.RareFeature != null).All(x => x.Owner == a2.Empires.First(e => e.SpaceRank == 1).Id), "the first civ in space owns the rare systems");
        var legacy = Defs.TechLegacy.Count(l => a1.Player.Techs.Contains(l.CivTech));
        Check(a2.Player.Modifiers.Count(m => m.StartsWith("sa_legacy")) == legacy, "researched legacy techs become starting bonuses");

        // ---- Act 2
        var start = Act2.Report(a2, a2.Player);
        Log.Info("Act 2 start income: " + string.Join(", ", start.Income.Select(kv => $"{kv.Key} {kv.Value:+0.0;-0.0}")) + " | jobs: " +
                 string.Join(", ", Act2.WorkedJobs(hw).GroupBy(j => j.source).Select(g => $"{g.Key} x{g.Count()}")) + $" | pops {hw.Pops}");
        Check(start.Income.All(kv => kv.Value >= 0), "Act 2 starts with no resource running down");
        int turns = 0;
        while (a2.Outcome == "" && turns < 400)
        {
            Act2Ai.Think(a2, content, a2.Player);
            Act2.EndTurn(a2, content);
            turns++;
            if (turns == 20) Check(RoundTrip(new SaveGame { Phase = "act2", Act1 = a1, Act2 = a2 }), "Act 2 save and load give the same game");
        }
        Check(a2.Outcome != "", $"Act 2 ends ({a2.Outcome} in {a2.Year} after {turns} turns)");
        Check(a2.Empires.Where(e => e.Alive).All(e => a2.PlanetsOf(e.Id).Any() || a2.SystemsOf(e.Id).Any()), "living empires still hold something");

        // ---- Ruthless: a rival first means no Act 2
        var r = new GameSetup { Civ = civ.Type, Leader = civ.Leader, MapSize = "small", Rivals = 3, Seed = 99, SpaceRace = "ruthless" };
        var rs = Act1.NewGame(r, content);
        rs.Civs[0].YieldMult = 0.3; // a weak player, so a rival lands first
        while (rs.Outcome == "") { Act1Ai.TakeTurn(rs, content, rs.Player); Act1Ai.PlaceSettlers(rs, content, rs.Player); Act1.EndTurn(rs, content); }
        Check(rs.Outcome == "defeat" && rs.SpaceOrder[0] != rs.Player.Id, "Ruthless: a rival reaching space first ends the game");

        Log.Info(_fail == 0 ? "self-test: ALL PASS" : $"self-test: {_fail} FAILED");
        return _fail == 0 ? 0 : 1;
    }

    static bool RoundTrip(SaveGame g)
    {
        var a = JsonSerializer.Serialize(g);
        var b = JsonSerializer.Serialize(JsonSerializer.Deserialize<SaveGame>(a));
        return a == b;
    }

    static bool LanesCross(Act2State s)
    {
        var edges = s.Systems.SelectMany(x => x.Lanes.Where(n => n > x.Id).Select(n => (a: x.Id, b: n))).ToList();
        static float Cross(StarSystem o, StarSystem a, StarSystem b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        for (int i = 0; i < edges.Count; i++)
            for (int j = i + 1; j < edges.Count; j++)
            {
                var (a, b) = edges[i]; var (c, d) = edges[j];
                if (a == c || a == d || b == c || b == d) continue;
                var A = s.Sys(a); var B = s.Sys(b); var C = s.Sys(c); var D = s.Sys(d);
                if ((Cross(C, D, A) > 0) != (Cross(C, D, B) > 0) && (Cross(A, B, C) > 0) != (Cross(A, B, D) > 0)) return true;
            }
        return false;
    }
}
