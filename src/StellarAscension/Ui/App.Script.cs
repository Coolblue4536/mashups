using Raylib_cs;

namespace StellarAscension;

/// <summary>
/// UI scripts (test aid, --uiscript file): drive the real screens step by step, pressing buttons by their label,
/// clicking map tiles and systems, taking screenshots. Every press goes through the same code a mouse click does.
/// One command per line:
///   click &lt;label start&gt;   press a button; fails if no such enabled button appears within 3 s
///   see &lt;label start&gt;     check a button with that label is on screen
///   shot &lt;name&gt;           screenshot to the --uiout folder
///   wait &lt;seconds&gt;
///   turns1 &lt;n&gt;            Act 1: play n turns with the End Turn button (choices made by the player AI)
///   settle               Act 1: place the waiting Settler on the best site by clicking its tile
///   city &lt;name&gt;           Act 1: select a city by clicking its tile ("capital" for yours)
///   finish1              Act 1: play the rest of Act 1 fast (headless), back to the screen
///   turns2 &lt;n&gt;            Act 2: play n turns with the End Turn button
///   system &lt;name&gt;         Act 2: left-click a system on the map ("home" for yours)
///   fleet &lt;role&gt;          Act 2: select your first fleet with that role (science, construction, colony, military)
///   order &lt;system&gt;        Act 2: right-click a system with the selected fleet
///   galaxy               Act 2: zoom out to the whole galaxy
///   quit
/// </summary>
public sealed partial class App
{
    readonly List<string> _script = new();
    int _scriptLine;
    float _scriptWait, _scriptStarted = -1;
    string _scriptOut = ".";
    readonly List<string> _scriptLog = new();
    int _scriptFails;
    (int x, int y)? _scriptTile;
    int _scriptSystem = -1, _scriptOrder = -1;
    int _scriptTurnsLeft;
    string _scriptMode = "";

    public void LoadScript(string path, string outDir)
    {
        _script.AddRange(File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")));
        _scriptOut = outDir;
        Gfx.IgnoreMouse = true;
        Directory.CreateDirectory(outDir);
    }

    void ScriptLog(bool ok, string what)
    {
        var line = (ok ? "PASS " : "FAIL ") + what;
        _scriptLog.Add(line);
        Log.Info("uiscript " + line);
        if (!ok) _scriptFails++;
    }

    /// <summary>Called once per frame after drawing.</summary>
    void RunScript(float dt)
    {
        if (_script.Count == 0 || Current == Screen.Loading) return;
        if (_scriptLine != _scriptStepLine) { _scriptStepLine = _scriptLine; _scriptStepStart = (float)Raylib.GetTime(); }
        else if ((float)Raylib.GetTime() - _scriptStepStart > 120)
        {
            ScriptLog(false, $"step stuck for 2 minutes: {(_scriptLine < _script.Count ? _script[_scriptLine] : "end")} (mode {_scriptMode})");
            FinishScript(); return;
        }
        // A press asked for last frame: did a button take it?
        if (_scriptMode == "click")
        {
            if (G.ScriptClicked) { ScriptLog(true, _script[_scriptLine]); _scriptMode = ""; _scriptLine++; _scriptWait = 0.4f; }
            else if ((float)Raylib.GetTime() - _scriptStarted > 3)
            {
                ScriptLog(false, $"{_script[_scriptLine]}: no such enabled button (on screen: {string.Join(" | ", G.LabelsThisFrame.Distinct().Take(30))})");
                G.ScriptClick = null; _scriptMode = ""; _scriptLine++;
            }
            else G.ScriptClick = _script[_scriptLine][6..].Trim();
            return;
        }
        if (_scriptWait > 0) { _scriptWait -= dt; return; }
        if (_scriptMode == "turns1") { ScriptTurn1(); return; }
        if (_scriptMode == "turns2") { ScriptTurn2(); return; }
        if (_scriptLine >= _script.Count) { FinishScript(); return; }
        var cmd = _script[_scriptLine];
        var arg = cmd.Contains(' ') ? cmd[(cmd.IndexOf(' ') + 1)..].Trim() : "";
        var verb = cmd.Split(' ')[0];
        switch (verb)
        {
            case "click":
                _scriptMode = "click"; _scriptStarted = (float)Raylib.GetTime(); G.ScriptClick = arg;
                return;
            case "see":
                ScriptLog(G.LabelsThisFrame.Any(l => l.StartsWith(arg, StringComparison.OrdinalIgnoreCase)), "see " + arg);
                break;
            case "shot":
                var file = Path.Combine(_scriptOut, arg + ".png");
                Raylib.TakeScreenshot(Path.GetFileName(file));
                var made = Path.Combine(Directory.GetCurrentDirectory(), Path.GetFileName(file));
                if (File.Exists(made) && Path.GetFullPath(made) != Path.GetFullPath(file)) File.Move(made, file, true);
                ScriptLog(File.Exists(file), "shot " + arg);
                break;
            case "wait": _scriptWait = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture); break;
            case "turns1": _scriptTurnsLeft = int.Parse(arg); _scriptMode = "turns1"; _scriptLine++; return;
            case "turns2": _scriptTurnsLeft = int.Parse(arg); _scriptMode = "turns2"; _scriptLine++; return;
            case "settle":
                if (Current == Screen.Act1 && A1.Player.PendingSettlers > 0 && Act1Ai.BestSite(A1, A1.Player) is { } site)
                {
                    int before = A1.Cities.Count;
                    _settlerMode = true; _scriptTile = site;
                    _scriptWait = 0.3f;
                    _pendingCheck = () => ScriptLog(A1.Cities.Count == before + 1, $"settle at {site}");
                }
                else ScriptLog(false, "settle: no Settler waiting or no site");
                break;
            case "city":
                if (Current == Screen.Act1 && (arg == "capital" ? A1.CitiesOf(A1.Player.Id).FirstOrDefault(c => c.Capital) : A1.Cities.FirstOrDefault(c => c.Name.StartsWith(arg))) is { } city) { _scriptTile = (city.X, city.Y); CenterOn(city.X, city.Y); ScriptLog(true, "city " + arg); }
                else ScriptLog(false, "city " + arg);
                break;
            case "finish1":
                if (Current == Screen.Act1)
                {
                    var s = A1;
                    while (s.Outcome == "") { Act1Ai.TakeTurn(s, Content!, s.Player); Act1Ai.PlaceSettlers(s, Content!, s.Player); Act1.EndTurn(s, Content!); }
                    _selUnit = -1;
                    _overlay1 = "";
                    ScriptLog(true, $"finish1: {s.Outcome} at turn {s.Turn}");
                }
                break;
            case "system":
                if (Current == Screen.Act2 && (arg == "home" ? A2.Sys(A2.Player.Capital) : A2.Systems.FirstOrDefault(x => x.Name.StartsWith(arg))) is { } sys) { _scriptSystem = sys.Id; CenterOn2(sys.Id); ScriptLog(true, "system " + arg); }
                else ScriptLog(false, "system " + arg);
                break;
            case "fleet":
                if (Current == Screen.Act2 && A2.Fleets.FirstOrDefault(f => f.Owner == A2.Player.Id && f.Ships.Keys.Any(k => Defs.Ship[k].Role == arg)) is { } fl) { _selFleet = fl.Id; ScriptLog(true, "fleet " + arg); }
                else ScriptLog(false, "fleet " + arg);
                break;
            case "order":
                if (Current == Screen.Act2 && A2.Systems.FirstOrDefault(x => x.Name.StartsWith(arg)) is { } to) { _scriptOrder = to.Id; ScriptLog(true, "order " + arg); }
                else ScriptLog(false, "order " + arg);
                break;
            case "galaxy": // Act 2: zoom out to the whole galaxy
                if (Current == Screen.Act2) { FitGalaxy(); _selSystem = -1; _selPlanet = -1; ScriptLog(true, "galaxy"); }
                break;
            case "hover": // hover <dx> <dy>: point the pretend mouse at a tile relative to your capital ("hover off" stops)
                if (arg == "off") { Gfx.FakeMouse = null; ScriptLog(true, "hover off"); break; }
                if (Current == Screen.Act1 && A1.CitiesOf(A1.Player.Id).FirstOrDefault(x => x.Capital) is { } cap)
                {
                    var parts = arg.Split(' ');
                    int hx = ((cap.X + int.Parse(parts[0])) % A1.W + A1.W) % A1.W, hy = Math.Clamp(cap.Y + int.Parse(parts[1]), 0, A1.H - 1);
                    Gfx.FakeMouse = TileScreen(hx, hy);
                    ScriptLog(true, $"hover {hx},{hy}");
                }
                break;
            case "unit": // select your first unit (or the first of a type: unit UNIT_WARRIOR)
                if (Current == Screen.Act1 && A1.UnitsOf(A1.Player.Id).FirstOrDefault(u => arg.Length == 0 || u.Type == arg) is { } su)
                { _selUnit = su.Id; _selCity = -1; CenterOn(su.X, su.Y); ScriptLog(true, "unit " + su.Type); }
                else ScriptLog(false, "unit " + arg);
                break;
            case "zoom": // Act 2: zoom <factor> about the screen centre
                if (Current == Screen.Act2)
                {
                    var centre = new System.Numerics.Vector2(Gfx.W / 2, Gfx.H / 2);
                    var before = W2(centre);
                    _zoom2 = _zoom2Target = Math.Clamp(_zoom2 * float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture), ZoomMin, ZoomMax);
                    _cam2 += before - W2(centre);
                    ScriptLog(true, "zoom " + arg);
                }
                break;
            case "hoversys": // Act 2: point the pretend mouse at a system ("home")
                if (Current == Screen.Act2 && (arg == "home" ? A2.Sys(A2.Player.Capital) : A2.Systems.FirstOrDefault(x => x.Name.StartsWith(arg))) is { } hsys)
                { Gfx.FakeMouse = S2(hsys.X, hsys.Y); ScriptLog(true, "hoversys " + hsys.Name); }
                break;
            case "meetall": // Act 1 test aid: meet every civilization and open relations with the first
                if (Current == Screen.Act1)
                {
                    var pl = A1.Player;
                    foreach (var o in A1.Majors.Where(o => !o.IsPlayer)) Diplomacy1.Meet(A1, Content!, pl, o);
                    var first = A1.Majors.First(o => !o.IsPlayer);
                    pl.Envoys[first.Id] = A1.Turn;
                    _dipSel = first.Id;
                    ScriptLog(true, "meetall");
                }
                break;
            case "builder": // Act 1 test aid: a Builder next to your capital, selected
                if (Current == Screen.Act1 && A1.CitiesOf(A1.Player.Id).FirstOrDefault(x => x.Capital) is { } bc)
                {
                    var spot = Hex.Within(bc.X, bc.Y, 2, A1.W, A1.H).Where(t => A1.At(t.x, t.y).City == bc.Id && (t.x != bc.X || t.y != bc.Y) && Builders.Options(A1, Content!, A1.Player, t.x, t.y).Any()).Select(t => ((int, int)?)t).FirstOrDefault() ?? (bc.X, bc.Y);
                    var bu = Military.Spawn(A1, Content!, A1.Player, "UNIT_BUILDER", spot.Item1, spot.Item2);
                    if (bu != null) { bu.Charges = 3; bu.Moves = 2; _selUnit = bu.Id; _selCity = -1; CenterOn(bu.X, bu.Y); }
                    ScriptLog(bu != null, "builder");
                }
                break;
            case "attackprompt": // Act 1 test aid: a tribal raider next to your first soldier, and the attack preview
                if (Current == Screen.Act1 && A1.UnitsOf(A1.Player.Id).FirstOrDefault(u => !Military.IsCivilian(u)) is { } su2)
                {
                    var nb = Hex.Neighbors(su2.X, su2.Y, A1.W, A1.H).FirstOrDefault(t => Military.EnterCost(A1, A1.Player, t.x, t.y) > 0 && A1.UnitAt(t.x, t.y) == null && A1.CityAt(t.x, t.y) == null);
                    var raider = Military.Spawn(A1, Content!, A1.Tribe!, "UNIT_SWORDSMAN", nb.x, nb.y);
                    if (raider != null) { (raider.X, raider.Y) = nb; su2.Moves = 2; su2.Attacked = false; _selUnit = su2.Id; CenterOn(su2.X, su2.Y); _attackPrompt = (su2.Id, nb.x, nb.y); }
                    ScriptLog(raider != null, "attackprompt");
                }
                break;
            case "offer": // test aid: an AI makes you an offer (Act 1: the first civ you have relations with; Act 2: an alien empire)
                if (Current == Screen.Act1)
                {
                    var from = A1.Majors.First(o => !o.IsPlayer && A1.Relations(o.Id, A1.Player.Id));
                    A1.Offers.Clear();
                    Act1Offer? offerMade = null;
                    for (int k = 0; k < 40 && offerMade == null; k++) { A1.NextOffer.Clear(); offerMade = Diplomacy1.MakeOffer(A1, Content!, from); if (offerMade?.Kind == "alliance") { A1.Offers.Clear(); offerMade = null; } }
                    if (offerMade == null) { offerMade = new Act1Offer { From = from.Id, Gold = 3, Borders = true, Expires = A1.Turn + 5 }; A1.Offers.Add(offerMade); }
                    ScriptLog(true, "offer " + Diplomacy1.Describe(A1, Content!, offerMade));
                }
                else if (Current == Screen.Act2)
                {
                    var me2 = A2.Player;
                    var from = A2.Empires.Where(e => !e.IsPlayer && e.Alive).OrderBy(e => e.Kind == "alien" ? 0 : 1).First();
                    Diplomacy2.Contact(A2, me2, from);
                    if (!me2.Comms.Contains(from.Id)) me2.Comms.Add(from.Id);
                    if (!from.Comms.Contains(me2.Id)) from.Comms.Add(me2.Id);
                    A2.Offers.Clear();
                    Act2Offer? offerMade = null;
                    for (int k = 0; k < 40 && offerMade == null; k++) { offerMade = Diplomacy2.MakeOffer(A2, from); if (offerMade?.Kind == "alliance") { A2.Offers.Clear(); offerMade = null; } }
                    if (offerMade == null) { offerMade = new Act2Offer { From = from.Id, Give = "energy", GiveAmount = 5, Get = "minerals", GetAmount = 4, Expires = A2.Month + 18 }; A2.Offers.Add(offerMade); }
                    _dipSel = from.Id;
                    ScriptLog(true, "offer " + Diplomacy2.Describe(A2, offerMade));
                }
                break;
            case "spacebattle": // Act 2 test aid: war with a neighbour and their fleet at your capital
                if (Current == Screen.Act2)
                {
                    var me2 = A2.Player;
                    var foe2 = A2.Empires.Where(e => !e.IsPlayer && e.Alive).OrderBy(e => e.Kind == "alien" ? 0 : 1).First();
                    Diplomacy2.Contact(A2, me2, foe2);
                    Act2.DeclareWar(A2, me2, foe2);
                    A2.Fleets.Add(new Fleet { Id = A2.NextFleetId++, Owner = foe2.Id, System = me2.Capital, Name = foe2.Adjective + " Raiders", Ships = new() { ["destroyer"] = 3, ["corvette"] = 5 } });
                    if (!A2.Fleets.Any(f => f.Owner == me2.Id && f.System == me2.Capital && Act2.IsMilitary(f)))
                        A2.Fleets.Add(new Fleet { Id = A2.NextFleetId++, Owner = me2.Id, System = me2.Capital, Name = "Home Guard", Ships = new() { ["corvette"] = 6, ["destroyer"] = 2 } });
                    for (int m = 0; m < 2; m++) Act2.Tick(A2, Content!);
                    _battleSel = me2.Capital; _overlay2 = "battle";
                    ScriptLog(A2.Battles.Any(b => !b.Over), "spacebattle");
                }
                break;
            case "systemview": if (Current == Screen.Act2) { _selSystem = arg == "home" || arg == "" ? A2.Player.Capital : A2.Systems.First(x => x.Name.StartsWith(arg)).Id; _overlay2 = "system"; ScriptLog(true, "systemview"); } break;
            case "exotic": // Act 1 test aid: show the first exotic site (revealed) under the pretend mouse
                if (Current == Screen.Act1 && Enumerable.Range(0, A1.Tiles.Length).FirstOrDefault(i => A1.Tiles[i].Exotic != null) is var ei && A1.Tiles[ei].Exotic != null)
                {
                    int ex = ei % A1.W, ey = ei / A1.W;
                    foreach (var (tx, ty) in Hex.Within(ex, ey, 3, A1.W, A1.H)) A1.At(tx, ty).Revealed = true;
                    CenterOn(ex, ey);
                    _selUnit = -1; _selCity = -1;
                    Gfx.FakeMouse = new System.Numerics.Vector2(Gfx.W / 2, Gfx.H / 2);
                    ScriptLog(true, "exotic " + A1.Tiles[ei].Exotic);
                }
                break;
            case "overlay": if (Current == Screen.Act1) _overlay1 = arg; else if (Current == Screen.Act2) _overlay2 = arg; ScriptLog(true, "overlay " + arg); break;
            case "lens": _yieldLens = arg != "off"; ScriptLog(true, "lens " + arg); break;
            case "quit": FinishScript(); return;
            default: ScriptLog(false, "unknown command " + cmd); break;
        }
        _scriptLine++;
        _scriptWait = Math.Max(_scriptWait, 0.25f);
    }

    Action? _pendingCheck;
    int _scriptSettleTries;
    float _scriptPressAt;
    float _scriptStepStart;
    int _scriptStepLine = -1;

    void ScriptTurn1()
    {
        // The press lands a frame or two later (the topmost button is found from the frame before); wait up to 3 s.
        if (G.ScriptClick != null && (float)Raylib.GetTime() - _scriptPressAt < 3) return;
        G.ScriptClick = null;
        if (_pendingCheck != null) { _pendingCheck(); _pendingCheck = null; }
        if (_scriptTurnsLeft <= 0 || Current != Screen.Act1 || A1.Outcome != "") { _scriptMode = ""; return; }
        var s = A1; var p = s.Player;
        _overlay1 = "";
        // Settle by clicking a tile the player can see; the AI's pick may lie in the fog, then let the AI place it.
        if (p.PendingSettlers > 0 && _scriptSettleTries++ < 2 && Act1Ai.BestSite(s, p) is { } site && Act1.CanFoundCity(s, p, site.x, site.y))
        { _settlerMode = true; _scriptTile = site; _scriptWait = 0.05f; return; }
        _scriptSettleTries = 0;
        if (p.PendingSettlers > 0) { Act1Ai.PlaceSettlers(s, Content!, p); _settlerMode = false; }
        if (p.Researching == null) Act1Ai.ChooseResearch(s, Content!, p);
        Act1Ai.TakeTurn(s, Content!, p); // choose production and move units the way the AI would
        foreach (var u in s.UnitsOf(p.Id)) u.Skipped = true;
        foreach (var idle in s.CitiesOf(p.Id).Where(x => x.Build == null))
            if (Act1.AvailableBuilds(s, Content!, idle).FirstOrDefault() is { } b) Act1.SetBuild(s, Content!, idle, b);
        if (p.PendingSettlers > 0) return; // the AI bought a Settler: place it first (next frame)
        _overlay1 = "";
        int turn = s.Turn;
        G.ScriptClick = "Next turn"; _scriptPressAt = (float)Raylib.GetTime();
        _pendingCheck = () => { if (s.Turn != turn + 1) ScriptLog(false, $"turn {turn}: Next turn did not advance (on screen: {string.Join(" | ", G.LabelsThisFrame.Distinct().TakeLast(6))})"); };
        _scriptTurnsLeft--;
        _scriptWait = 0.03f;
    }

    void ScriptTurn2()
    {
        // The press lands a frame or two later (the topmost button is found from the frame before); wait up to 3 s.
        if (G.ScriptClick != null && (float)Raylib.GetTime() - _scriptPressAt < 3) return;
        G.ScriptClick = null;
        if (_pendingCheck != null) { _pendingCheck(); _pendingCheck = null; }
        if (_scriptTurnsLeft <= 0 || Current != Screen.Act2 || A2.Outcome != "") { _scriptMode = ""; return; }
        var s = A2;
        Act2Ai.Think(s, Content!, s.Player);
        _overlay2 = "";
        int month = s.Month;
        G.ScriptClick = "End turn"; _scriptPressAt = (float)Raylib.GetTime();
        _pendingCheck = () => { if (s.Month <= month) ScriptLog(false, $"month {month}: End turn did not advance"); };
        _scriptTurnsLeft--;
        _scriptWait = 0.03f;
    }

    void FinishScript()
    {
        ScriptLog(_scriptFails == 0, $"script finished: {_scriptLog.Count(l => l.StartsWith("PASS"))} passed, {_scriptFails} failed");
        File.WriteAllLines(Path.Combine(_scriptOut, "uiscript.log"), _scriptLog);
        _script.Clear();
        Quit();
    }
}
