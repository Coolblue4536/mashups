namespace StellarAscension;

/// <summary>Turns a finished Act 1 into the Act 2 galaxy (README: "What you play → Act 2").</summary>
public static class Handoff
{
    static readonly string[] SkipClasses = { "HUM", "MACHINE", "MINDWARDEN", "INF" };
    static readonly string[] FallbackStars = { "Aldebaran", "Achernar", "Rigel", "Vega", "Deneb", "Mira", "Polaris", "Sirius", "Procyon", "Capella", "Spica", "Antares" };

    public static Act2State FromAct1(Act1State a1, Content content)
    {
        var size = Defs.MapSize.TryGetValue(a1.Setup.MapSize, out var ms) ? ms : Defs.MapSize["standard"];
        var length = Defs.GameLength.TryGetValue(a1.Setup.GameLength, out var gl) ? gl : Defs.GameLength["standard"];
        var diff = Defs.Difficulty.TryGetValue(a1.Setup.Difficulty, out var d) ? d : Defs.Difficulty["DIFFICULTY_PRINCE"];
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
                AiMult = c.IsPlayer ? 1 : diff.AiAct2Mult, Aggression = 0.35 + rng.Double() * 0.4,
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
        for (int i = 0; i < size.Aliens; i++)
        {
            var p = i < aliens.Count ? aliens[i] : null;
            var hue = rng.Double();
            var (r, g, b) = Hsv(hue, 0.55, 0.9);
            s.Empires.Add(new Empire
            {
                Id = s.Empires.Count, Name = p?.Name ?? $"Alien Empire {i + 1}", Adjective = p?.Adjective ?? "Alien", Leader = p?.SpeciesName ?? "",
                Kind = "alien", Portrait = p?.Portrait ?? "", SpeciesClass = p?.SpeciesClass ?? "", ColorR = r, ColorG = g, ColorB = b,
                AiMult = diff.AiAct2Mult, Aggression = 0.3 + rng.Double() * 0.5,
            });
        }
        foreach (var e in s.Empires)
            foreach (var res in Defs.Resources) e.Stock[res.Id] = res.Start;

        // ---- galaxy
        MakeGalaxy(s, content, rng, Math.Max(size.Act2Systems, s.Empires.Count * 8));

        // ---- homes, spread out
        var homes = SpreadHomes(s, rng, s.Empires.Count);
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

    public static (int r, int g, int b) Hsv(double h, double sat, double v)
    {
        double c = v * sat, x = c * (1 - Math.Abs(h * 6 % 2 - 1)), m = v - c;
        var (r, g, b) = (int)(h * 6) switch { 0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x), 3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x) };
        return ((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }

    public static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", 6 => "VI", 7 => "VII", 8 => "VIII", _ => n.ToString() };

    static Planet NewPlanet(Act2State s, StarSystem sys, string cls, int size, Rng rng) =>
        new() { Id = s.NextPlanetId++, Class = cls, Size = size, Name = sys.Name };

    static void MakeGalaxy(Act2State s, Content content, Rng rng, int n)
    {
        var names = (content.Stellaris?.StarNames.Count > 0 ? content.Stellaris.StarNames : FallbackStars.ToList()).Distinct().ToList();
        rng.Shuffle(names);
        double radius = 60 * Math.Sqrt(n);
        var pts = new List<(float x, float y)>();
        int guard = 0;
        while (pts.Count < n && guard++ < n * 400)
        {
            // Two spiral arms plus a core, as Stellaris's spiral galaxies.
            double t = rng.Double(), arm = rng.Int(2) * Math.PI, r = radius * (0.12 + 0.88 * Math.Sqrt(t));
            double ang = arm + t * 3.4 + (rng.Double() - 0.5) * (0.9 - 0.4 * t);
            float x = (float)(Math.Cos(ang) * r), y = (float)(Math.Sin(ang) * r * 0.8);
            if (pts.All(p => (p.x - x) * (p.x - x) + (p.y - y) * (p.y - y) > 44 * 44)) pts.Add((x, y));
        }
        for (int i = 0; i < pts.Count; i++)
        {
            var star = rng.Weighted(Defs.StarClasses, c => c.Weight).Id;
            var sys = new StarSystem { Id = i, X = pts[i].x, Y = pts[i].y, Star = star, Name = i < names.Count ? names[i] : $"System {i + 1}" };
            int count = star.Contains("black_hole") || star.Contains("pulsar") ? rng.Range(0, 2) : rng.Range(1, 6);
            for (int k = 0; k < count; k++)
            {
                var pc = rng.Weighted(Defs.PlanetClasses, c => c.Weight);
                sys.Planets.Add(new Planet { Id = s.NextPlanetId++, Class = pc.Id, Size = rng.Range(pc.SizeMin, pc.SizeMax), Name = $"{sys.Name} {Roman(k + 1)}" });
            }
            s.Systems.Add(sys);
        }
        // Hyperlanes: each system links to its nearest neighbours without crossing lanes, then every cluster is joined up.
        var edges = new List<(int a, int b)>();
        float D(int a, int b) => MathF.Sqrt((pts[a].x - pts[b].x) * (pts[a].x - pts[b].x) + (pts[a].y - pts[b].y) * (pts[a].y - pts[b].y));
        bool Crosses(int a, int b) => edges.Any(e => e.a != a && e.a != b && e.b != a && e.b != b && Intersect(pts[a], pts[b], pts[e.a], pts[e.b]));
        void Link(int a, int b)
        {
            if (a == b || s.Systems[a].Lanes.Contains(b)) return;
            edges.Add((a, b));
            s.Systems[a].Lanes.Add(b);
            s.Systems[b].Lanes.Add(a);
        }
        var candidates = new List<(int a, int b, float d)>();
        for (int a = 0; a < pts.Count; a++)
            foreach (var b in Enumerable.Range(0, pts.Count).Where(b => b != a).OrderBy(b => D(a, b)).Take(4))
                if (a < b || !candidates.Any(c => c.a == b && c.b == a)) candidates.Add((Math.Min(a, b), Math.Max(a, b), D(a, b)));
        foreach (var c in candidates.Distinct().OrderBy(c => c.d))
            if (c.d < 150 && s.Systems[c.a].Lanes.Count < 4 && s.Systems[c.b].Lanes.Count < 4 && !Crosses(c.a, c.b)) Link(c.a, c.b);
        while (true)
        {
            var comp = Bfs(s, 0).Select(x => x.id).ToHashSet();
            if (comp.Count == s.Systems.Count) break;
            var best = (a: -1, b: -1, d: float.MaxValue);
            foreach (var a in comp)
                for (int b = 0; b < pts.Count; b++)
                    if (!comp.Contains(b) && D(a, b) < best.d && !Crosses(a, b)) best = (a, b, D(a, b));
            if (best.a < 0)
                foreach (var a in comp)
                    for (int b = 0; b < pts.Count; b++)
                        if (!comp.Contains(b) && D(a, b) < best.d) best = (a, b, D(a, b));
            Link(best.a, best.b);
        }
    }

    static bool Intersect((float x, float y) p1, (float x, float y) p2, (float x, float y) p3, (float x, float y) p4)
    {
        static float Cross((float x, float y) o, (float x, float y) a, (float x, float y) b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        float d1 = Cross(p3, p4, p1), d2 = Cross(p3, p4, p2), d3 = Cross(p1, p2, p3), d4 = Cross(p1, p2, p4);
        return (d1 > 0) != (d2 > 0) && (d3 > 0) != (d4 > 0);
    }

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

    static List<int> SpreadHomes(Act2State s, Rng rng, int count)
    {
        var ok = s.Systems.Where(x => x.Lanes.Count >= 2 && !x.Star.Contains("black_hole") && !x.Star.Contains("pulsar")).Select(x => x.Id).ToList();
        var homes = new List<int> { rng.Pick(ok) };
        var dists = new Dictionary<int, Dictionary<int, int>>();
        Dictionary<int, int> D(int a) => dists.TryGetValue(a, out var d) ? d : dists[a] = Bfs(s, a).ToDictionary(x => x.id, x => x.dist);
        while (homes.Count < count)
        {
            var next = ok.Where(x => !homes.Contains(x)).OrderByDescending(x => homes.Min(h => D(h).TryGetValue(x, out var dd) ? dd : 99) + rng.Double()).First();
            homes.Add(next);
        }
        return homes;
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
        // A habitable world next door for the first colony.
        var neighbor = sys.Lanes.Select(s.Sys).Where(x => !x.Home && x.Owner < 0).OrderBy(_ => rng.Double()).FirstOrDefault();
        if (neighbor != null && !neighbor.Planets.Any(p => Defs.PlanetClass.TryGetValue(p.Class, out var pc) && pc.Habitable))
            neighbor.Planets.Add(new Planet { Id = s.NextPlanetId++, Class = rng.Pick(Defs.PlanetClasses.Where(c => c.Habitable && c.Id != "pc_gaia").ToList()).Id, Size = rng.Range(12, 16), Name = $"{neighbor.Name} {Roman(neighbor.Planets.Count + 1)}" });
        // Starting ships: a science ship, a construction ship and three corvettes.
        foreach (var role in new[] { "science", "construction" })
            s.Fleets.Add(new Fleet { Id = s.NextFleetId++, Owner = e.Id, System = sys.Id, Ships = { [role] = 1 }, Name = Defs.Ship[role].Id });
        s.Fleets.Add(new Fleet { Id = s.NextFleetId++, Owner = e.Id, System = sys.Id, Ships = { ["corvette"] = 3 }, Name = $"{e.Adjective} Home Fleet" });
    }
}
