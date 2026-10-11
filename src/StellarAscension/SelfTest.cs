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
        Check(content.Techs.Count(t => !t.PreFtl) == 68 && content.Techs.Count(t => t.PreFtl) == Defs.PreftlTechs.Length, $"Civ VI base tech tree read ({content.Techs.Count(t => !t.PreFtl)} techs) plus {Defs.PreftlTechs.Length} pre-FTL");
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
        Check(a1.Majors.Count() == 4 && a1.Cities.Count == 4 && a1.Tribe != null, "new game: player and 3 rivals, each with a capital, and the tribes");
        Check(a1.Tiles.Count(t => t.Natural != null) == Defs.MapSize["small"].NaturalWonders, "natural wonders placed");
        Check(a1.Tiles.Any(t => t.Revealed) && a1.Tiles.Any(t => !t.Revealed), "fog of war: some land seen, some not");
        Check(content.Units.Count == Defs.Units.Length && content.Units.All(u => u.Line == "civilian" || u.Combat > 0), $"Civ VI unit roster read ({content.Units.Count} units, Builder included)");
        Check(civ6.Icon("ICON_UNIT_WARRIOR_WHITE") != null && civ6.Icon("ICON_UNIT_TANK_PORTRAIT") != null && civ6.Icon("ICON_RESOURCE_IRON") != null, "unit flags, unit portraits and resource icons decoded");
        Check(a1.Majors.All(cv => a1.UnitsOf(cv.Id).Count() == 1), "every civ starts with a Warrior");
        Check(a1.Tiles.Count(t => t.Resource != null) > a1.Tiles.Length / 20, $"resources on the map ({a1.Tiles.Count(t => t.Resource != null)})");
        Check(a1.Majors.All(cv => a1.CitiesOf(cv.Id).First() is var cp && Hex.Within(cp.X, cp.Y, 4, a1.W, a1.H).Any(t => a1.At(t.x, t.y).Resource == "RESOURCE_IRON")
                                && Hex.Within(cp.X, cp.Y, 3, a1.W, a1.H).Any(t => a1.At(t.x, t.y).Resource == "RESOURCE_HORSES")), "Horses and Iron near every start");
        Check(UnitRules(content), "units: movement, combat, capturing a city, upgrades, peace");
        Check(a1.Tiles.Any(t => t.Camp) && a1.Tiles.Any(t => t.Village), "tribal camps and villages in the wilds");
        Check(a1.Tiles.Count(t => t.Exotic != null) >= 2, $"exotic sites on the map ({string.Join(", ", a1.Tiles.Where(t => t.Exotic != null).Select(t => t.Exotic))})");
        Check(a1.Majors.All(cv => Defs.CivTrait.ContainsKey(cv.Trait)), "every civilization has its trait");
        Check(NewSystems(content), "builders, tribes, diplomacy, engagements, pre-FTL research");
        while (a1.Outcome == "" && a1.Turn < 120) { Act1Ai.TakeTurn(a1, content, a1.Player); Act1Ai.PlaceSettlers(a1, content, a1.Player); Act1.EndTurn(a1, content); }
        Check(a1.Tiles.Any(t => t.Built?.StartsWith("district:") == true), "districts stand on their own tiles");
        Check(RoundTrip(new SaveGame { Act1 = a1 }), "Act 1 save and load give the same game");
        while (a1.Outcome == "") { Act1Ai.TakeTurn(a1, content, a1.Player); Act1Ai.PlaceSettlers(a1, content, a1.Player); Act1.EndTurn(a1, content); }
        Check(a1.Outcome == "space" && a1.SpaceOrder.Contains(a1.Player.Id), $"Act 1 ends with the player in space (turn {a1.Turn}, {Act1.YearText(a1.Year)})");
        Check(a1.Year is > 1800 and < 2150, "the calendar reaches the space age");

        // ---- handoff and galaxy
        var a2 = Handoff.FromAct1(a1, content);
        Check(a2.Empires.Count(e => e.Kind == "civ") == a1.Majors.Count(cv => cv.Alive) && a2.Empires.Any(e => e.Kind == "alien"), "every surviving civ becomes an empire, plus Stellaris aliens");
        Check(a2.Empires.Count(e => e.Kind == "alien") == Defs.MapSize["small"].Aliens, "the map size's number of alien empires");
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

        Check(a2.Player.Techs.Contains("tech_space_exploration") && a2.Player.Techs.Contains("tech_corvettes"), "pre-FTL research carries into space");
        Check(a2.Empires.Where(e => e.Kind == "alien").All(e => Defs.Act2Techs.Where(t => t.Tier == 0).All(t => e.Techs.Contains(t.Id))), "native species know every pre-FTL technology");
        Check(a2.Systems.SelectMany(x => x.Planets).Count(p => p.Exotic != null) == (int)Rules.ExoticsAct2, "exotic worlds in the galaxy");
        Check(a2.Empires.All(e => Defs.CivTrait.ContainsKey(e.Trait)) && a2.Player.Modifiers.Any(m => m.StartsWith("sa_trait")), "traits carry into space");
        Check(SpaceSystems(a2, content), "space: tech tiers and exotic gates, battles, emergency FTL, contact and communications");
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
        foreach (var cv in rs.Civs) cv.Aggression = 0; // and no wars, so the race decides it
        while (rs.Outcome == "") { Act1Ai.TakeTurn(rs, content, rs.Player); Act1Ai.PlaceSettlers(rs, content, rs.Player); Act1.EndTurn(rs, content); }
        Check(rs.Outcome == "defeat" && rs.SpaceOrder[0] != rs.Player.Id, "Ruthless: a rival reaching space first ends the game");

        // ---- galaxy options: size, aliens, AI difficulty
        var g = new GameSetup { Civ = civ.Type, Leader = civ.Leader, MapSize = "small", Rivals = 2, Seed = 4242, SpaceRace = "relaxed", GalaxySize = "huge", Aliens = 5, GalaxyDifficulty = "grand_admiral" };
        var ga1 = Act1.NewGame(g, content);
        while (ga1.Outcome == "") { Act1Ai.TakeTurn(ga1, content, ga1.Player); Act1Ai.PlaceSettlers(ga1, content, ga1.Player); Act1.EndTurn(ga1, content); }
        if (ga1.Outcome == "space")
        {
            var ga2 = Handoff.FromAct1(ga1, content);
            int emp = ga2.Empires.Count;
            Check(ga2.Empires.Count(e => e.Kind == "alien") == 5, "chosen number of alien empires");
            Check(ga2.Systems.Count >= emp * 20, $"huge galaxy: {ga2.Systems.Count} systems for {emp} empires");
            Check(ga2.Empires.Where(e => !e.IsPlayer).All(e => Math.Abs(e.AiMult - Defs.GalaxyDifficulty["grand_admiral"].AiMult) < 1e-9), "galaxy AI difficulty applies to every AI empire");
        }
        else Check(false, $"galaxy options game reached space ({ga1.Outcome})");

        Log.Info(_fail == 0 ? "self-test: ALL PASS" : $"self-test: {_fail} FAILED");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>A small war on a fresh map: a unit moves, fights, takes a city at 0 HP, upgrades with gold, and peace sends units home.</summary>
    static bool UnitRules(Content content)
    {
        var civ = content.Civs.First(c => c.Leader == "LEADER_TRAJAN");
        var s = Act1.NewGame(new GameSetup { Civ = civ.Type, Leader = civ.Leader, MapSize = "small", Rivals = 1, Seed = 5150, SpaceRace = "standard" }, content);
        var me = s.Civs[0]; var them = s.Civs[1];
        var w = s.UnitsOf(me.Id).First();
        bool ok = true;
        void Need(bool cond, string what) { if (!cond) { Log.Info("   unit rules: " + what); ok = false; } }
        var reach = Military.Reachable(s, w);
        Need(reach.Count > 1 && reach.Keys.All(k => Military.EnterCost(s, me, k.x, k.y) >= 0), "a Warrior can move, never onto impassable tiles");
        var city = s.CitiesOf(them.Id).First();
        Need(!Military.CanEnter(s, w, city.X, city.Y, true), "foreign cities block movement");
        // Put the Warrior next to the rival capital, declare war, and knock the city down.
        var spot = Hex.Neighbors(city.X, city.Y, s.W, s.H).First(t => Military.EnterCost(s, me, t.x, t.y) > 0 && s.UnitAt(t.x, t.y) == null && Defs.Terrains[s.At(t.x, t.y).Terrain].Land);
        (w.X, w.Y) = spot; w.Moves = 2; w.Hp = 100;
        Need(!Military.CanAttack(s, content, w, city.X, city.Y), "no attacks at peace");
        Military.DeclareWar(s, content, me, them);
        Need(s.AtWar(me.Id, them.Id) && Military.CanAttack(s, content, w, city.X, city.Y), "war allows the attack");
        s.Units.RemoveAll(u => u.Owner == them.Id);
        city.Hp = 1;
        Military.Attack(s, content, w, city.X, city.Y);
        Need(city.Owner == me.Id && w.X == city.X && w.Y == city.Y, "a melee unit takes a city at 0 HP and moves in");
        Need(!them.Alive || s.CitiesOf(them.Id).Any(), "a civ with no cities left is destroyed");
        // Upgrade: Warrior to Swordsman needs Iron Working and Iron in the borders.
        var s2 = Act1.NewGame(new GameSetup { Civ = civ.Type, Leader = civ.Leader, MapSize = "small", Rivals = 1, Seed = 5151, SpaceRace = "standard" }, content);
        var me2 = s2.Civs[0];
        var w2 = s2.UnitsOf(me2.Id).First();
        var cap = s2.CitiesOf(me2.Id).First();
        (w2.X, w2.Y) = (cap.X, cap.Y);
        w2.Moves = 2;
        foreach (var t in new[] { "TECH_MINING", "TECH_BRONZE_WORKING", "TECH_IRON_WORKING" }) me2.Techs.Add(t);
        s2.Tiles.First(t => t.City == cap.Id && s2.Tiles[cap.Y * s2.W + cap.X] != t).Resource = "RESOURCE_IRON";
        s2.Turn++; // refresh the owned-resources cache
        me2.Gold = 500;
        Need(Military.UpgradeTarget(s2, content, w2)?.Type == "UNIT_SWORDSMAN", "Warrior upgrades to Swordsman once Iron Working and Iron are in hand");
        Need(Military.Upgrade(s2, content, w2) && w2.Type == "UNIT_SWORDSMAN" && me2.Gold < 500, "upgrading costs gold");
        Need(Military.CantTrain(s2, content, cap, "UNIT_WARRIOR") != null, "obsolete units can't be trained");
        // Peace: units in the other side's land go home.
        var s3 = Act1.NewGame(new GameSetup { Civ = civ.Type, Leader = civ.Leader, MapSize = "small", Rivals = 1, Seed = 5152, SpaceRace = "standard" }, content);
        var a = s3.Civs[0]; var b = s3.Civs[1];
        Military.DeclareWar(s3, content, a, b);
        var u3 = s3.UnitsOf(a.Id).First();
        int idx = Enumerable.Range(0, s3.Tiles.Length).First(i => s3.Tiles[i].City >= 0 && s3.City(s3.Tiles[i].City)!.Owner == b.Id && s3.CityAt(i % s3.W, i / s3.W) == null
                                                                && s3.UnitAt(i % s3.W, i / s3.W) == null && Military.EnterCost(s3, a, i % s3.W, i / s3.W) > 0);
        (u3.X, u3.Y) = (idx % s3.W, idx / s3.W);
        s3.Turn += (int)Rules.WarMinTurns + 1;
        Military.MakePeace(s3, content, a, b);
        Need(!s3.AtWar(a.Id, b.Id) && (!s3.Units.Contains(u3) || s3.OwnerAt(u3.X, u3.Y) != b.Id), "peace sends units out of the other's borders");
        return ok;
    }

    /// <summary>Builders, the tribes, Act 1 diplomacy, multi-turn fights and pre-FTL research, on fresh maps.</summary>
    static bool NewSystems(Content content)
    {
        bool ok = true;
        void Need(bool cond, string what) { if (!cond) { Log.Info("   new systems: " + what); ok = false; } }
        var civ = content.Civs.First(c => c.Leader == "LEADER_TRAJAN");
        var s = Act1.NewGame(new GameSetup { Civ = civ.Type, Leader = civ.Leader, MapSize = "small", Rivals = 2, Seed = 6060, SpaceRace = "standard" }, content);
        var me = s.Player;
        var cap = s.CitiesOf(me.Id).First();
        // A Builder farms a grassland or plains tile in the borders.
        var spot = Hex.Within(cap.X, cap.Y, 2, s.W, s.H).FirstOrDefault(t => (t.x != cap.X || t.y != cap.Y) && s.At(t.x, t.y).City == cap.Id
                     && Builders.CantImprove(s, content, me, t.x, t.y, Defs.Improvement["IMPROVEMENT_FARM"]) == null);
        var b = Military.Spawn(s, content, me, "UNIT_BUILDER", spot.x, spot.y)!;
        b.Charges = 3; b.Moves = 2; (b.X, b.Y) = spot;
        double before = Act1.TileYield(s, spot.x, spot.y).Food;
        Need(Builders.Improve(s, content, b, "IMPROVEMENT_FARM") && Act1.TileYield(s, spot.x, spot.y).Food == before + 1 && b.Charges == 2, "a Builder's farm adds food and uses a charge");
        Need(Military.CanEnter(s, b, cap.X, cap.Y, true) || s.MilitaryAt(cap.X, cap.Y) != null, "a Builder can share a tile with a soldier");
        // Tribes: a camp sends a raider; a unit walking in clears it.
        var tribe = s.Tribe!;
        var campIdx = Enumerable.Range(0, s.Tiles.Length).First(i => s.Tiles[i].Camp);
        int cx = campIdx % s.W, cy = campIdx / s.W;
        for (int t = 0; t < 40 && !s.UnitsOf(tribe.Id).Any(); t++) { s.Turn++; Tribes.Turn(s, content); }
        Need(s.UnitsOf(tribe.Id).Any(), "tribal camps send raiders");
        Need(s.AtWar(me.Id, tribe.Id) && !Diplomacy1.CantSendEnvoy(s, me, tribe)!.Equals(null), "the tribes are at war with everyone and can't be talked to");
        s.Units.RemoveAll(u => u.Owner == tribe.Id);
        var w = s.UnitsOf(me.Id).First(u => !Military.IsCivilian(u));
        double gold = me.Gold;
        Military.MoveTo(s, w, cx, cy, 0);
        Need(!s.At(cx, cy).Camp && me.Gold >= gold + Rules.TribeCampGold, "clearing a camp pays gold");
        // Diplomacy: meet, envoy, trade, alliance.
        var other = s.Majors.First(x => !x.IsPlayer);
        Diplomacy1.Meet(s, content, me, other);
        Need(s.HasMet(me.Id, other.Id) && !s.Relations(me.Id, other.Id) && Diplomacy1.CantDeal(s, me, other) != null, "meeting alone doesn't open trade");
        me.Gold = 200;
        Need(Diplomacy1.SendEnvoy(s, me, other) && !s.Relations(me.Id, other.Id), "an envoy takes time to arrive");
        s.Turn += (int)Rules.Act1EnvoyTurns;
        Need(s.Relations(me.Id, other.Id), "relations open when the envoy arrives");
        Need(Diplomacy1.Propose(s, content, me, other, "", "", 5, true, force: true) && s.OpenBorders(other.Id, me.Id) && s.Deals.Any(d => d.Kind == "gold"), "a trade deal (gold a turn, open borders)");
        s.Alliances.Add(new[] { Math.Min(me.Id, other.Id), Math.Max(me.Id, other.Id) });
        var third = s.Majors.First(x => !x.IsPlayer && x.Id != other.Id);
        Military.DeclareWar(s, content, third, me);
        Need(s.AtWar(third.Id, other.Id), "an ally joins a defensive war");
        // An engagement lasts until one side withdraws.
        var a = s.UnitsOf(me.Id).First(u => !Military.IsCivilian(u));
        var foe = s.UnitsOf(third.Id).First();
        var nb = Hex.Neighbors(a.X, a.Y, s.W, s.H).First(t => Military.EnterCost(s, third, t.x, t.y) > 0 && s.UnitAt(t.x, t.y) == null && s.CityAt(t.x, t.y) == null);
        (foe.X, foe.Y) = nb; a.Hp = foe.Hp = 100; a.Moves = 2; a.Attacked = false;
        foe.Type = a.Type = "UNIT_SPEARMAN";
        Military.Attack(s, content, a, foe.X, foe.Y);
        Need(s.Units.Contains(a) && s.Units.Contains(foe) && a.Engaged == foe.Id, "a melee fight both survive goes on");
        double hpBefore = a.Hp + foe.Hp;
        Military.NewTurn(s, content);
        Need(!s.Units.Contains(a) || !s.Units.Contains(foe) || a.Hp + foe.Hp < hpBefore, "the battle goes on next turn");
        if (s.Units.Contains(a) && s.Units.Contains(foe) && a.Engaged >= 0) { a.Moves = 2; Need(Military.Withdraw(s, content, a) && a.Engaged < 0, "a unit can withdraw from a fight"); }
        // The Moon Landing needs the core pre-FTL research.
        foreach (var t in content.Techs.Where(t => !t.PreFtl)) if (!me.Techs.Contains(t.Type)) me.Techs.Add(t.Type);
        me.SatelliteDone = true;
        if (!cap.Buildings.Contains("DISTRICT_SPACEPORT")) cap.Buildings.Add("DISTRICT_SPACEPORT");
        Need(Act1.CantBuild(s, content, cap, "PROJECT_LAUNCH_MOON_LANDING")?.StartsWith("Needs pre-FTL") == true, "no Moon Landing without pre-FTL research");
        foreach (var t in Defs.PreftlTechs.Where(t => t.Core)) me.Techs.Add(t.Id);
        Need(Act1.CantBuild(s, content, cap, "PROJECT_LAUNCH_MOON_LANDING") == null, "the Moon Landing opens with the core pre-FTL research");
        return ok;
    }

    /// <summary>Act 2: tech tiers and exotic gates, a battle over several months, emergency FTL and coming home, contact and communications.</summary>
    static bool SpaceSystems(Act2State real, Content content)
    {
        bool ok = true;
        void Need(bool cond, string what) { if (!cond) { Log.Info("   space: " + what); ok = false; } }
        var s = JsonSerializer.Deserialize<SaveGame>(JsonSerializer.Serialize(new SaveGame { Phase = "act2", Act2 = real }))!.Act2!;
        var me = s.Player;
        Need(!Act2.CanResearch(s, me, "tech_zero_point_power") && !Act2.CanResearch(s, me, "tech_mega_engineering"), "top-tier research is out of reach at the start");
        Need(Defs.Act2Techs.All(t => t.Requires.All(r => Defs.Act2Tech[r].Tier <= t.Tier)), "every technology needs only its own tier or lower");
        Need(Defs.Act2Techs.GroupBy(t => t.Tier).OrderBy(g => g.Key).Select(g => g.Average(t => t.Cost)).Zip(Defs.Act2Techs.GroupBy(t => t.Tier).OrderBy(g => g.Key).Skip(1).Select(g => g.Average(t => t.Cost))).All(p => p.Second > p.First), "costs rise tier by tier");
        var exoticTech = Defs.Act2Techs.First(t => t.Feature != "none" && !Exotics.Known(s, me).Contains(t.Feature));
        foreach (var q in exoticTech.Requires) if (!me.Techs.Contains(q)) me.Techs.Add(q);
        Need(!Act2.CanResearch(s, me, exoticTech.Id), "an exotic technology needs its site");
        me.Exotics.Add(exoticTech.Feature);
        Need(Act2.CanResearch(s, me, exoticTech.Id), "holding the site opens it");
        // Contact and communications.
        var other = s.Empires.First(e => !e.IsPlayer && e.Kind == "alien");
        me.Contacts.Remove(other.Id); other.Contacts.Remove(me.Id); me.Comms.Remove(other.Id); other.Comms.Remove(me.Id);
        Need(Diplomacy2.CantEstablish(s, me, other) != null, "no envoy without contact");
        Diplomacy2.Contact(s, me, other);
        Need(Diplomacy2.Establish(s, me, other) && !s.HasComms(me.Id, other.Id), "an envoy sets out");
        for (int m = 0; m < Rules.Act2ContactMonths + 1; m++) { s.Month++; Diplomacy2.Month(s, content); }
        Need(s.HasComms(me.Id, other.Id) && Diplomacy2.CantDeal(s, me, other) == null, "communications established after a year");
        // A battle over several months, then emergency FTL.
        var home = s.Sys(me.Capital);
        var enemy = s.Empires.First(e => !e.IsPlayer && e.Id != other.Id);
        Act2.DeclareWar(s, me, enemy);
        var mine = new Fleet { Id = s.NextFleetId++, Owner = me.Id, System = home.Id, Name = "Test Fleet", Ships = new() { ["cruiser"] = 6 } };
        var theirs = new Fleet { Id = s.NextFleetId++, Owner = enemy.Id, System = home.Id, Name = "Raiders", Ships = new() { ["cruiser"] = 6 } };
        s.Fleets.Add(mine); s.Fleets.Add(theirs);
        for (int m = 0; m < 3; m++) Act2.Tick(s, content);
        var battle = s.Battles.LastOrDefault(b => b.System == home.Id);
        Need(battle != null && !battle.Over && battle.Months >= 3 && s.Fleets.Contains(mine) && s.Fleets.Contains(theirs), "a battle between equal fleets lasts several months");
        int shipsBefore = mine.Ships.Values.Sum();
        Need(Act2.EmergencyFtl(s, content, mine) && mine.MiaMonths > 0 && mine.Ships.Values.Sum() < shipsBefore, "emergency FTL: some ships lost, the fleet goes missing");
        Need(!s.Active.Contains(mine), "a missing fleet is out of the fight");
        mine.System = theirs.System = s.Sys(home.Lanes[0]).Id; s.Fleets.Remove(theirs);
        for (int m = 0; m < Rules.EmergencyFtlMonths + 1; m++) Act2.Tick(s, content);
        Need(s.Fleets.Contains(mine) && mine.MiaMonths == 0 && mine.System == me.Capital, "it turns up at home later");
        return ok;
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
