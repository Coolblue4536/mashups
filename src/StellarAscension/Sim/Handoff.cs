namespace StellarAscension;

/// <summary>Turns a finished Act 1 into the Act 2 galaxy (README: "What you play → Act 2").</summary>
public static class Handoff
{
    static readonly string[] SkipClasses = { "HUM", "MACHINE", "MINDWARDEN", "INF" };

    public static Act2State FromAct1(Act1State a1, Content content)
    {
        var size = Defs.MapSize.TryGetValue(a1.Setup.MapSize, out var ms) ? ms : Defs.MapSize["standard"];
        var length = Defs.GameLength.TryGetValue(a1.Setup.GameLength, out var gl) ? gl : Defs.GameLength["standard"];
        var gd = Defs.GalaxyDifficulty.TryGetValue(a1.Setup.GalaxyDifficulty, out var gdd) ? gdd : Defs.GalaxyDifficulty["captain"];
        var gs = Defs.GalaxySize.TryGetValue(a1.Setup.GalaxySize, out var gsd) ? gsd : Defs.GalaxySize["medium"];
        int alienCount = a1.Setup.Aliens >= 0 ? a1.Setup.Aliens : size.Aliens;
        var race = Defs.SpaceRaceById.TryGetValue(a1.Setup.SpaceRace, out var sr) ? sr : Defs.SpaceRaceById["standard"];
        var rng = new Rng(a1.Rng.Next());
        var s = new Act2State { Setup = a1.Setup, EndYear = length.EndYear, Rng = rng };

        // ---- empires: every Act 1 civ (player first), then Stellaris's own aliens
        var civs = a1.Civs.Where(c => c.Alive).OrderByDescending(c => c.IsPlayer).ToList();
        foreach (var c in civs)
        {
            var def = Act1.CivDef(content, c);
            int rank = a1.SpaceOrder.IndexOf(c.Id) + 1;
            var e = new Empire
            {
                Id = s.Empires.Count, Name = Act1.CivName(content, c), Adjective = Act1.CivAdjective(content, c), Leader = Act1.LeaderName(content, c),
                IsPlayer = c.IsPlayer, Kind = "civ", CivType = c.CivType, LeaderType = c.Leader, SpaceRank = rank == 0 ? a1.SpaceOrder.Count + 1 : rank,
                ColorR = def?.Primary.r ?? 200, ColorG = def?.Primary.g ?? 200, ColorB = def?.Primary.b ?? 200,
                AiMult = c.IsPlayer ? 1 : gd.AiMult, Aggression = (0.35 + rng.Double() * 0.4) * gd.Aggression,
            };
            // Civ VI's primary colours can be near-white or near-black; keep them visible on a dark galaxy.
            if (e.ColorR + e.ColorG + e.ColorB < 120) { e.ColorR = def?.Secondary.r ?? 160; e.ColorG = def?.Secondary.g ?? 160; e.ColorB = def?.Secondary.b ?? 160; }
            foreach (var l in Defs.TechLegacy) if (c.Techs.Contains(l.CivTech)) e.Modifiers.Add(l.Modifier);
            s.Empires.Add(e);
        }
        int playerRank = s.Player.SpaceRank;
        foreach (var e in s.Empires)
        {
            if (e.IsPlayer && playerRank == 1) e.Modifiers.AddRange(race.PlayerFirstModifiers);
            if (!e.IsPlayer && e.SpaceRank < playerRank) e.Modifiers.AddRange(race.RivalLeadModifiers);
        }
        var aliens = (content.Stellaris?.Prescripted ?? new List<PrescriptedEmpire>())
            .Where(p => !SkipClasses.Contains(p.SpeciesClass) && p.Portrait.Length > 0 && !p.Key.EndsWith("machine_age"))
            .GroupBy(p => p.Name).Select(g => g.First()).ToList();
        rng.Shuffle(aliens);
        for (int i = 0; i < alienCount; i++)
        {
            var p = i < aliens.Count ? aliens[i] : null;
            var hue = rng.Double();
            var (r, g, b) = Hsv(hue, 0.55, 0.9);
            s.Empires.Add(new Empire
            {
                Id = s.Empires.Count, Name = p?.Name ?? $"Alien Empire {i + 1}", Adjective = p?.Adjective ?? "Alien", Leader = p?.SpeciesName ?? "",
                Kind = "alien", Portrait = p?.Portrait ?? "", SpeciesClass = p?.SpeciesClass ?? "", ColorR = r, ColorG = g, ColorB = b,
                AiMult = gd.AiMult, Aggression = (0.3 + rng.Double() * 0.5) * gd.Aggression,
            });
        }
        DistinctColors(s, content);
        foreach (var e in s.Empires)
            foreach (var res in Defs.Resources) e.Stock[res.Id] = res.Start;

        // ---- galaxy
        var homes = Galaxy.Make(s, content, rng, s.Empires.Count * gs.SystemsPerEmpire, s.Empires.Count);
        rng.Shuffle(homes); // which arm each empire gets

        // ---- homes, spread out
        for (int i = 0; i < s.Empires.Count; i++) SetUpHome(s, a1, content, s.Empires[i], s.Sys(homes[i]), civs.ElementAtOrDefault(i), rng);

        // ---- rare systems from Civ VI's natural wonders, near the first civilization to reach space
        var first = s.Empires.Where(e => e.Kind == "civ").OrderBy(e => e.SpaceRank).First();
        var naturals = a1.Tiles.Where(t => t.Natural != null).Select(t => t.Natural!).Distinct()
            .GroupBy(n => content.Name(n)).Select(g => g.First()).Take(8).ToList();
        var near = Bfs(s, first.Capital).Where(x => x.dist >= 1 && x.dist <= 8 && s.Sys(x.id).Owner < 0 && !s.Sys(x.id).Home).Select(x => x.id).ToList();
        foreach (var n in naturals)
        {
            var pool = near.Where(id => s.Sys(id).RareModifier == null).ToList();
            if (pool.Count == 0) pool = s.Systems.Where(x => x.Owner < 0 && x.RareModifier == null && !x.Home).Select(x => x.Id).ToList();
            if (pool.Count == 0) break;
            var sys = s.Sys(pool.OrderBy(id => Bfs(s, first.Capital).First(b => b.id == id).dist).ThenBy(_ => rng.Double()).First());
            sys.RareModifier = content.NaturalDef(n).Modifier;
            sys.RareFeature = n;
            sys.Name = content.Name(n);
            sys.Owner = first.Id;
            if (!sys.SurveyedBy.Contains(first.Id)) sys.SurveyedBy.Add(first.Id);
            if (!sys.Planets.Any(p => Defs.PlanetClass.TryGetValue(p.Class, out var pc) && pc.Habitable))
                sys.Planets.Add(NewPlanet(s, sys, "pc_gaia", rng.Range(14, 20), rng));
            for (int k = 0; k < sys.Planets.Count; k++) sys.Planets[k].Name = $"{sys.Name} {Roman(k + 1)}";
        }
        s.Act1Summary.Add($"Reached space: " + string.Join(", ", a1.SpaceOrder.Select((id, i) => $"{i + 1}. {Act1.CivName(content, a1.Civs[id])}")));
        s.Events.Add(new Act2Event(0, "start", $"{s.Player.Name} enters the space age. The galaxy holds {s.Empires.Count - 1} other empires.", s.Player.Id, s.Player.Capital));
        if (naturals.Count > 0)
            s.Events.Add(new Act2Event(0, "rare", $"{first.Name} reached space first and holds {naturals.Count} rare star system{(naturals.Count == 1 ? "" : "s")} made from the natural wonders of Earth.", first.Id));
        return s;
    }

    /// <summary>Every empire gets a colour clearly different from the others (Civ VI's secondary colour first, else a new hue).</summary>
    static void DistinctColors(Act2State s, Content content)
    {
        static double Dist((int r, int g, int b) a, (int r, int g, int b) b) =>
            Math.Sqrt(2 * (a.r - b.r) * (a.r - b.r) + 4 * (a.g - b.g) * (a.g - b.g) + 3 * (a.b - b.b) * (a.b - b.b));
        var used = new List<(int, int, int)>();
        double hue = 0.13;
        foreach (var e in s.Empires)
        {
            var cand = new List<(int, int, int)> { (e.ColorR, e.ColorG, e.ColorB) };
            if (e.Kind == "civ" && content.Civs.FirstOrDefault(c => c.Type == e.CivType && c.Leader == e.LeaderType) is { } d)
                cand.Add((d.Secondary.r, d.Secondary.g, d.Secondary.b));
            for (int k = 0; k < 24; k++) { cand.Add(Hsv(hue % 1, 0.65, 0.95)); hue += 0.618034; }
            // The first bright candidate far from every colour in use; with many empires, the one farthest from them.
            var bright = cand.Where(c => (c.Item1 + c.Item2 + c.Item3) > 150).ToList();
            var pick = bright.FirstOrDefault(c => used.All(u => Dist(u, c) > 160));
            if (pick == default) pick = bright.MaxBy(c => used.Count == 0 ? 0 : used.Min(u => Dist(u, c)));
            used.Add(pick);
            (e.ColorR, e.ColorG, e.ColorB) = pick;
        }
    }

    public static (int r, int g, int b) Hsv(double h, double sat, double v)
    {
        double c = v * sat, x = c * (1 - Math.Abs(h * 6 % 2 - 1)), m = v - c;
        var (r, g, b) = (int)(h * 6) switch { 0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x), 3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x) };
        return ((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }

    public static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", 6 => "VI", 7 => "VII", 8 => "VIII", _ => n.ToString() };

    static Planet NewPlanet(Act2State s, StarSystem sys, string cls, int size, Rng rng) =>
        new() { Id = s.NextPlanetId++, Class = cls, Size = size, Name = sys.Name };

    public static List<(int id, int dist)> Bfs(Act2State s, int start)
    {
        var dist = new Dictionary<int, int> { [start] = 0 };
        var q = new Queue<int>();
        q.Enqueue(start);
        var outp = new List<(int, int)>();
        while (q.Count > 0)
        {
            int c = q.Dequeue();
            outp.Add((c, dist[c]));
            foreach (var n in s.Sys(c).Lanes)
                if (!dist.ContainsKey(n)) { dist[n] = dist[c] + 1; q.Enqueue(n); }
        }
        return outp;
    }

    static void SetUpHome(Act2State s, Act1State a1, Content content, Empire e, StarSystem sys, CivState? civ, Rng rng)
    {
        sys.Home = true;
        sys.Owner = e.Id;
        sys.SurveyedBy.Add(e.Id);
        sys.Star = "sc_g";
        e.Capital = sys.Id;
        var cities = civ == null ? new List<CityState>() : a1.CitiesOf(civ.Id).OrderByDescending(c => c.Capital).ThenByDescending(c => c.Pop).ToList();
        string capitalName = cities.FirstOrDefault()?.Name ?? (e.Kind == "alien" ? sys.Name : e.Adjective + " Prime");
        sys.Name = capitalName;
        // Homeworld + a fixed set of other worlds, so every empire starts on equal footing.
        sys.Planets.Clear();
        var hw = new Planet
        {
            Id = s.NextPlanetId++, Class = rng.Pick(new[] { "pc_continental", "pc_ocean", "pc_tropical" }), Size = 18, Owner = e.Id, Homeworld = true,
            Name = e.Kind == "alien" ? $"{sys.Name} Prime" : capitalName, Pops = Rules.Act2HomePops,
        };
        hw.Districts["district_city"] = 1;
        hw.Districts["district_generator"] = 2;
        hw.Districts["district_mining"] = 2;
        hw.Districts["district_farming"] = 2;
        hw.Buildings.AddRange(new[] { "building_research_lab_1", "building_foundry_1", "building_bureaucratic_1" });
        if (civ != null) hw.Wonders.AddRange(a1.CitiesOf(civ.Id).SelectMany(c => c.Wonders));
        sys.Planets.Add(hw);
        foreach (var (cls, sz) in new[] { ("pc_barren", 10), ("pc_gas_giant", 24), ("pc_molten", 8), ("pc_asteroid", 2) })
            sys.Planets.Add(new Planet { Id = s.NextPlanetId++, Class = cls, Size = sz });
        int maxNames = 8;
        for (int k = 1; k < sys.Planets.Count; k++)
            sys.Planets[k].Name = k < cities.Count && k < maxNames ? cities[k].Name : $"{sys.Name} {Roman(k + 1)}";
        // Starting ships: a science ship, a construction ship and three corvettes.
        foreach (var role in new[] { "science", "construction" })
            s.Fleets.Add(new Fleet { Id = s.NextFleetId++, Owner = e.Id, System = sys.Id, Ships = { [role] = 1 }, Name = Defs.Ship[role].Id });
        s.Fleets.Add(new Fleet { Id = s.NextFleetId++, Owner = e.Id, System = sys.Id, Ships = { ["corvette"] = 3 }, Name = $"{e.Adjective} Home Fleet" });
    }
}
