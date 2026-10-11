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
