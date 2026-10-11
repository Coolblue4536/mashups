using System.Numerics;
using Raylib_cs;

namespace StellarAscension;

public sealed partial class App
{
    Vector2 _cam2;
    float _zoom2 = 1f;
    int _selSystem = -1, _selFleet = -1, _selPlanet = -1;
    string _overlay2 = "";
    float _speed2;      // 0 = paused
    float _monthClock;
    int _seenEvents2;

    Act2State A2 => Game!.Act2!;

    void EnterAct2()
    {
        Current = Screen.Act2;
        var s = A2;
        _speed2 = 0;
        _zoom2 = 1f;
        if (s.Player.Capital >= 0) { CenterOn2(s.Player.Capital); _selSystem = s.Player.Capital; }
        _seenEvents2 = Math.Max(0, s.Events.Count - 3);
        if (s.Player.Researching == null) _overlay2 = "research";
    }

    void CenterOn2(int sys)
    {
        var x = A2.Sys(sys);
        _cam2 = new Vector2(x.X, x.Y) - new Vector2(Gfx.W / 2 / _zoom2, Gfx.H / 2 / _zoom2);
    }

    Vector2 S2(float x, float y) => (new Vector2(x, y) - _cam2) * _zoom2;
    Vector2 W2(Vector2 screen) => screen / _zoom2 + _cam2;

    static Color EColor(Empire e, int a = 255) => new((byte)e.ColorR, (byte)e.ColorG, (byte)e.ColorB, (byte)a);

    string StName(string key, string fallback) => Act2.Name(Content!, key, fallback);
    string ResName(string id) => Defs.Resource.TryGetValue(id, out var r) ? r.Name : id;
    Texture2D? ResIcon(string id) => St(Defs.Resource[id].Art);
    Texture2D? ShipPic(string id) => St(Defs.Ship[id].Art);
    Texture2D? PlanetPic(string cls) => G.Tex("planet:" + cls, () => Res.PlanetIcon(cls));
    Texture2D? EmpirePic(Empire e) => e.Kind == "civ" ? CivIcon(e.LeaderType) : G.Tex("portrait:" + e.Portrait, () => Res.Portrait(e.Portrait));

    // ---------------------------------------------------------------- frame

    void DrawAct2(float dt)
    {
        G.UseAct2Theme();
        var s = A2;
        var c = Content!;
        HandleInput2(dt);
        if (_speed2 > 0 && s.Outcome == "" && _overlay2 is "" or "fleet")
        {
            _monthClock += dt * _speed2;
            while (_monthClock >= Rules.Act2SecondsPerMonth && s.Outcome == "")
            {
                _monthClock -= (float)Rules.Act2SecondsPerMonth;
                Act2.Tick(s, c);
                if (s.Month % 12 == 0) SaveNow();
            }
        }
        DrawGalaxy(s, c);
        TopBar2(s, c);
        Events2(s);
        if (_selFleet >= 0 && s.Fleets.FirstOrDefault(f => f.Id == _selFleet) is { } fleet) FleetPanel(s, c, fleet);
        else _selFleet = -1;
        if (_selSystem >= 0) SystemPanel(s, c, s.Sys(_selSystem));
        if (_selPlanet >= 0 && s.Systems.SelectMany(x => x.Planets).FirstOrDefault(p => p.Id == _selPlanet) is { } planet && planet.Owner == s.Player.Id) PlanetPanel(s, c, planet);
        else _selPlanet = -1;
        if (_overlay2 == "research") Research2(s, c);
        else if (_overlay2 == "ships") Shipyard2(s, c);
        else if (_overlay2 == "empires") Empires2(s, c);
        else if (_overlay2 == "menu") MenuOverlay();
        if (s.Outcome != "") EndPopup2(s, c);
        Toasts();
    }

    void HandleInput2(float dt)
    {
        float pan = 1100 * dt / _zoom2;
        if (Raylib.IsKeyDown(KeyboardKey.A) || Raylib.IsKeyDown(KeyboardKey.Left)) _cam2.X -= pan;
        if (Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.Right)) _cam2.X += pan;
        if (Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.Up)) _cam2.Y -= pan;
        if (Raylib.IsKeyDown(KeyboardKey.S) || Raylib.IsKeyDown(KeyboardKey.Down)) _cam2.Y += pan;
        if (Raylib.IsKeyPressed(KeyboardKey.Space)) _speed2 = _speed2 > 0 ? 0 : 1;
        if (Raylib.IsKeyPressed(KeyboardKey.One)) _speed2 = 1;
        if (Raylib.IsKeyPressed(KeyboardKey.Two)) _speed2 = 2;
        if (Raylib.IsKeyPressed(KeyboardKey.Three)) _speed2 = 4;
        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            if (_overlay2 != "") _overlay2 = "";
            else if (_selFleet >= 0) _selFleet = -1;
            else if (_selPlanet >= 0) _selPlanet = -1;
            else _overlay2 = "menu";
        }
        if (_overlay2 != "" || !G.MouseOnMap) return;
        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0)
        {
            var before = W2(Gfx.Mouse);
            _zoom2 = Math.Clamp(_zoom2 * (wheel > 0 ? 1.12f : 1 / 1.12f), 0.25f, 3f);
            _cam2 += before - W2(Gfx.Mouse);
        }
        if (Raylib.IsMouseButtonDown(MouseButton.Middle)) _cam2 -= Raylib.GetMouseDelta() / _zoom2;
    }

    int? SystemAt(Act2State s, Vector2 screen)
    {
        int? best = null; float bd = 26 * 26;
        foreach (var x in s.Systems)
        {
            float d = Vector2.DistanceSquared(S2(x.X, x.Y), screen);
            if (d < bd) { bd = d; best = x.Id; }
        }
        return best;
    }

    Vector2 FleetPos(Act2State s, Fleet f)
    {
        var a = s.Sys(f.System);
        if (f.Path.Count == 0) return new Vector2(a.X, a.Y);
        var b = s.Sys(f.Path[0]);
        float len = MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        float t = len <= 0 ? 0 : Math.Clamp((float)f.LaneProgress / len, 0, 1);
        return new Vector2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }

    void DrawGalaxy(Act2State s, Content c)
    {
        var me = s.Player;
        Starfield();
        // Territory
        foreach (var x in s.Systems.Where(x => x.Owner >= 0))
        {
            var p = S2(x.X, x.Y);
            Raylib.DrawCircleV(p, 64 * _zoom2, EColor(s.Empires[x.Owner], 40));
        }
        foreach (var x in s.Systems.Where(x => x.Owner >= 0))
        {
            var p = S2(x.X, x.Y);
            Raylib.DrawCircleLinesV(p, 64 * _zoom2, EColor(s.Empires[x.Owner], 90));
        }
        // Lanes
        foreach (var x in s.Systems)
            foreach (var n in x.Lanes.Where(n => n > x.Id))
            {
                var y = s.Sys(n);
                bool owned = x.Owner >= 0 && x.Owner == y.Owner;
                Raylib.DrawLineEx(S2(x.X, x.Y), S2(y.X, y.Y), owned ? 2f : 1.2f, owned ? EColor(s.Empires[x.Owner], 170) : new Color(120, 160, 200, 80));
            }
        // Stars
        var hover = _overlay2 == "" && G.MouseOnMap ? SystemAt(s, Gfx.Mouse) : null;
        foreach (var x in s.Systems)
        {
            var p = S2(x.X, x.Y);
            if (p.X < -60 || p.Y < -60 || p.X > Gfx.W + 60 || p.Y > Gfx.H + 60) continue;
            var sc = Defs.StarClass.TryGetValue(x.Star, out var scd) ? scd : Defs.StarClasses[0];
            float r = 13 * Math.Clamp(_zoom2, 0.6f, 1.6f);
            var tex = St(sc.Art);
            Raylib.DrawCircleV(p, r * 0.9f, new Color(Gfx.Hex(sc.Color).R, Gfx.Hex(sc.Color).G, Gfx.Hex(sc.Color).B, (byte)90));
            // Stellaris's map star pictures are drawn additively (their black is "no light").
            if (tex != null)
            {
                Raylib.BeginBlendMode(BlendMode.Additive);
                Gfx.DrawFit(tex, new Rectangle(p.X - r * 1.6f, p.Y - r * 1.6f, r * 3.2f, r * 3.2f), Color.White);
                Raylib.EndBlendMode();
            }
            else Raylib.DrawCircleV(p, r * 0.55f, Gfx.Hex(sc.Color));
            if (x.RareFeature != null) Gfx.DrawFit(Icon("ICON_" + x.RareFeature), new Rectangle(p.X + r * 0.6f, p.Y - r * 2.1f, r * 1.8f, r * 1.8f), Color.White);
            if (x.Id == _selSystem) Raylib.DrawCircleLinesV(p, r * 1.5f, Gfx.Gold);
            if (hover == x.Id) Raylib.DrawCircleLinesV(p, r * 1.7f, Gfx.Ink);
            if (_zoom2 > 0.55f)
            {
                var name = x.Name;
                float fs = Math.Clamp(14 * _zoom2, 11, 18);
                var col = x.Owner >= 0 ? EColor(s.Empires[x.Owner]) : x.SurveyedBy.Contains(me.Id) ? Gfx.Ink : Gfx.Muted;
                G.Text(name, p.X - G.Measure(name, fs) / 2, p.Y + r * 1.3f, fs, col);
                if (x.Home && x.Owner >= 0) Raylib.DrawCircleLinesV(p, r * 2.0f, col);
            }
        }
        // Fleets
        var groups = s.Fleets.GroupBy(f => (f.System, f.Path.Count > 0 ? f.Path[0] : -1, f.Owner)).ToList();
        foreach (var g in groups)
        {
            int k = 0;
            foreach (var f in g)
            {
                var wp = FleetPos(s, f);
                var p = S2(wp.X, wp.Y) + new Vector2(-18 - k * 7, -18 + (f.Owner == me.Id ? 0 : 10));
                var e = s.Empires[f.Owner];
                bool mil = Act2.IsMilitary(f);
                float sz = mil ? 9 : 6;
                Raylib.DrawTriangle(p + new Vector2(0, -sz), p + new Vector2(-sz * 0.8f, sz * 0.7f), p + new Vector2(sz * 0.8f, sz * 0.7f), EColor(e));
                Raylib.DrawTriangleLines(p + new Vector2(0, -sz), p + new Vector2(-sz * 0.8f, sz * 0.7f), p + new Vector2(sz * 0.8f, sz * 0.7f), Color.Black);
                if (f.Id == _selFleet)
                {
                    Raylib.DrawCircleLinesV(p, sz + 4, Gfx.Gold);
                    var last = wp;
                    foreach (var n in f.Path) { var y = s.Sys(n); Raylib.DrawLineEx(S2(last.X, last.Y), S2(y.X, y.Y), 2, Gfx.Gold); last = new Vector2(y.X, y.Y); }
                }
                var hit = new Rectangle(p.X - sz - 2, p.Y - sz - 2, sz * 2 + 4, sz * 2 + 4);
                if (_overlay2 == "" && G.Hover(hit))
                {
                    G.Tip($"{f.Name} ({e.Name})\n" + string.Join(", ", f.Ships.Select(kv => $"{kv.Value} {StName(Defs.Ship[kv.Key].NameLoc, kv.Key)}")) + (mil ? $"\nPower {Gfx.Num(Act2.Power(s, e, f))}" : ""));
                    if (f.Owner == me.Id && G.Clicked(hit)) { _selFleet = f.Id; _selPlanet = -1; }
                }
                k++;
            }
        }
        // Clicks
        if (hover is { } hv && _overlay2 == "")
        {
            if (Raylib.IsMouseButtonPressed(MouseButton.Left) && !G.ClickTaken) { _selSystem = hv; _selPlanet = -1; }
            if (Raylib.IsMouseButtonPressed(MouseButton.Right) && _selFleet >= 0 && s.Fleets.FirstOrDefault(f => f.Id == _selFleet) is { } sf)
                SmartOrder(s, sf, hv);
        }
        else if (_overlay2 == "" && G.MouseOnMap && Raylib.IsMouseButtonPressed(MouseButton.Left) && !G.ClickTaken) { _selSystem = -1; _selPlanet = -1; }
        if (_overlay2 == "" && G.MouseOnMap && Raylib.IsMouseButtonDown(MouseButton.Right) && hover == null) _cam2 -= Raylib.GetMouseDelta() / _zoom2;
    }

    /// <summary>A fixed field of faint stars behind the galaxy, drifting a little with the camera.</summary>
    void Starfield()
    {
        Raylib.DrawRectangleGradientV(0, 0, (int)Gfx.W, (int)Gfx.H, new Color(4, 8, 20, 255), new Color(10, 6, 24, 255));
        var rng = new Rng(7);
        for (int i = 0; i < 700; i++)
        {
            float x = (float)(rng.Double() * 3000), y = (float)(rng.Double() * 2000), depth = 0.15f + (float)rng.Double() * 0.25f;
            float sx = ((x - _cam2.X * depth) % Gfx.W + Gfx.W) % Gfx.W, sy = ((y - _cam2.Y * depth) % Gfx.H + Gfx.H) % Gfx.H;
            byte a = (byte)(60 + rng.Int(150));
            Raylib.DrawPixelV(new Vector2(sx, sy), new Color((byte)200, (byte)210, (byte)255, a));
            if (i % 9 == 0) Raylib.DrawCircleV(new Vector2(sx, sy), 1.2f, new Color((byte)180, (byte)200, (byte)255, (byte)(a / 2)));
        }
    }

    /// <summary>Right-click: the obvious order for the ship and system (survey, outpost, or just move).</summary>
    void SmartOrder(Act2State s, Fleet f, int sys)
    {
        var me = s.Player;
        var x = s.Sys(sys);
        var role = f.Ships.Keys.First();
        string order = Defs.Ship[role].Role switch
        {
            "science" when !x.SurveyedBy.Contains(me.Id) => "survey",
            "construction" when x.Owner < 0 && x.SurveyedBy.Contains(me.Id) => "outpost",
            _ => "",
        };
        Act2.Order(s, f, order, sys);
        _selSystem = sys;
    }

    // ---------------------------------------------------------------- top bar

    void TopBar2(Act2State s, Content c)
    {
        var me = s.Player;
        var bar = new Rectangle(0, 0, Gfx.W, 50);
        Raylib.DrawRectangleRec(bar, new Color(6, 20, 24, 245));
        Raylib.DrawLineEx(new Vector2(0, 50), new Vector2(Gfx.W, 50), 1.5f, G.PanelEdge);
        G.Block(bar);
        Raylib.DrawCircle(26, 25, 18, EColor(me));
        Gfx.DrawFit(EmpirePic(me), new Rectangle(10, 9, 32, 32), Color.White);
        var rep = Act2.Report(s, me);
        float x = 54;
        foreach (var id in Act2.Res)
        {
            double stock = id == "research" ? me.ResearchProgress : me.Stock.GetValueOrDefault(id);
            double inc = rep.Income[id];
            Gfx.DrawFit(ResIcon(id), new Rectangle(x, 12, 26, 26), Color.White);
            string txt = id == "research" ? $"+{Gfx.Num(inc)}" : $"{Gfx.Num(stock)}";
            G.Text(txt, x + 30, 8, 18, Gfx.Ink);
            if (id != "research") G.Text(Gfx.Signed(inc), x + 30, 27, 14, inc < 0 ? Gfx.Bad : Gfx.Good);
            float wdt = 36 + Math.Max(G.Measure(txt, 18), 40);
            var r = new Rectangle(x, 4, wdt, 42);
            if (G.Hover(r)) G.Tip($"{ResName(id)}: {Defs.Resource[id].Desc}" + (id == "research" ? "" : $"\n{Gfx.Num(stock)} in stock, {Gfx.Signed(inc)} a month."));
            x += wdt + 10;
        }
        G.Text($"Fleet {Gfx.Num(rep.NavalUsed)}/{Gfx.Num(rep.NavalCap)}", x + 6, 16, 17, rep.NavalUsed > rep.NavalCap ? Gfx.Bad : Gfx.Muted);
        x += 130;
        if (G.Button(new Rectangle(x, 8, 110, 34), "Research", size: 16, selected: _overlay2 == "research")) _overlay2 = _overlay2 == "research" ? "" : "research";
        if (G.Button(new Rectangle(x + 116, 8, 100, 34), "Shipyard", size: 16, selected: _overlay2 == "ships")) _overlay2 = _overlay2 == "ships" ? "" : "ships";
        if (G.Button(new Rectangle(x + 222, 8, 100, 34), "Empires", size: 16, selected: _overlay2 == "empires")) _overlay2 = _overlay2 == "empires" ? "" : "empires";
        // Date and speed
        string date = $"{s.Year}.{s.Month % 12 + 1:00}";
        float rx = Gfx.W - 330;
        G.Text(date, rx, 14, 22, Gfx.Gold);
        float[] speeds = { 0, 1, 2, 4 };
        for (int i = 0; i < 4; i++)
            if (G.Button(new Rectangle(rx + 84 + i * 42, 9, 38, 32), i == 0 ? "II" : new string('>', i), size: 16, selected: _speed2 == speeds[i], tip: i == 0 ? "Pause (Space)" : $"Speed {i} ({i})"))
                _speed2 = speeds[i];
        if (G.Button(new Rectangle(Gfx.W - 76, 9, 68, 32), "Menu", size: 16, tip: "Save, quit, or go back to the title (Esc)")) _overlay2 = _overlay2 == "menu" ? "" : "menu";
        if (_speed2 == 0) G.TextCentered("Paused: press Space or a speed button to let time run", new Rectangle(0, 56, Gfx.W, 22), 16, Gfx.Gold);
    }

    void Events2(Act2State s)
    {
        var me = s.Player;
        for (int i = _seenEvents2; i < s.Events.Count; i++)
        {
            var e = s.Events[i];
            if (e.Empire == me.Id || e.Kind is "war" or "peace" or "eliminated" or "captured" or "end" or "rare")
                _toasts.Add((e.Text, (float)Raylib.GetTime() + 7));
            if (e.Kind == "war" && e.Text.Contains(me.Name) && _speed2 > 0) _speed2 = 0;
        }
        _seenEvents2 = s.Events.Count;
        var recent = s.Events.Where(e => e.Empire == me.Id || e.Kind is "war" or "peace" or "eliminated" or "captured").TakeLast(6).Reverse().ToList();
        float y = Gfx.H - 20 - recent.Count * 26;
        foreach (var e in recent)
        {
            var r = new Rectangle(12, y, 470, 24);
            Raylib.DrawRectangleRounded(r, 0.4f, 4, new Color(4, 16, 18, 200));
            G.Text(G.Clip($"{(int)Rules.Act2StartYear + e.Month / 12}  {e.Text}", 15, r.Width - 14), r.X + 8, r.Y + 4, 15, Gfx.Ink);
            if (G.Hover(r)) G.Tip(e.Text);
            if (e.System >= 0 && G.Clicked(r)) { CenterOn2(e.System); _selSystem = e.System; }
            G.Block(r);
            y += 26;
        }
    }

    // ---------------------------------------------------------------- panels

    void SystemPanel(Act2State s, Content c, StarSystem x)
    {
        var me = s.Player;
        var panel = new Rectangle(12, 60, 380, Math.Min(Gfx.H - 260, 120 + Math.Max(1, x.Planets.Count) * 60 + 120));
        G.Panel(panel);
        float px = panel.X + 14, y = panel.Y + 12, w = panel.Width - 28;
        G.Text(x.Name, px, y, 24, Gfx.Gold, true);
        if (G.Button(new Rectangle(panel.X + panel.Width - 38, y, 26, 26), "×", size: 16)) { _selSystem = -1; return; }
        y += 32;
        var owner = x.Owner >= 0 ? s.Empires[x.Owner] : null;
        G.Text($"{StName(Defs.StarClass.TryGetValue(x.Star, out var sc) ? sc.NameLoc : x.Star, x.Star)}" + (owner != null ? $" · {owner.Name}" : " · unclaimed"), px, y, 16, owner != null ? EColor(owner) : Gfx.Muted);
        y += 24;
        if (x.RareFeature != null)
        {
            Gfx.DrawFit(Icon("ICON_" + x.RareFeature), new Rectangle(px, y, 36, 36), Color.White);
            y += G.Para($"Rare system from {c.Name(x.RareFeature)}: whoever holds it gets {Defs.Modifier[x.RareModifier!].EffectText.Replace(" while you hold this system", "")}.", px + 44, y, 15, w - 44, Gfx.Gold) + 6;
            y = Math.Max(y, y);
        }
        bool known = x.SurveyedBy.Contains(me.Id);
        if (!known) { G.Text("Not surveyed: send a science ship to see its planets.", px, y, 15, Gfx.Muted); y += 24; }
        else
        {
            foreach (var p in x.Planets)
            {
                var r = new Rectangle(px, y, w, 54);
                string cls = StName(p.Class, p.Class);
                string line2 = p.Owner >= 0 ? $"{s.Empires[p.Owner].Name} · {Math.Floor(p.Pops)} pops" : Act2.Habitable(p) ? $"{cls} · size {p.Size} · habitable" : $"{cls} · size {p.Size}";
                bool mine = p.Owner == me.Id;
                if (G.IconButton(r, PlanetPic(p.Class), p.Name + (p.Homeworld ? " (homeworld)" : ""), line2, selected: p.Id == _selPlanet, tip: p.Wonders.Count > 0 ? "Wonders: " + string.Join(", ", p.Wonders.Select(c.Name)) : null) && mine)
                    _selPlanet = p.Id;
                y += 58;
                if (y > panel.Y + panel.Height - 110) break;
            }
        }
        // Orders for the selected ship
        if (_selFleet >= 0 && s.Fleets.FirstOrDefault(f => f.Id == _selFleet) is { } fl)
        {
            y = panel.Y + panel.Height - 96;
            var role = Defs.Ship[fl.Ships.Keys.First()].Role;
            float bw = (w - 6) / 2;
            if (G.Button(new Rectangle(px, y, bw, 38), "Move here", fl.System != x.Id || fl.Path.Count > 0, size: 16)) Act2.Order(s, fl, "", x.Id);
            if (role == "science" && G.Button(new Rectangle(px + bw + 6, y, bw, 38), "Survey", !known, size: 16)) Act2.Order(s, fl, "survey", x.Id);
            if (role == "construction")
            {
                bool can = x.Owner < 0 && known;
                if (G.Button(new Rectangle(px + bw + 6, y, bw, 38), "Build outpost", can, size: 16,
                        tip: $"Claims the system: {Rules.Act2OutpostAlloys} alloys, {Rules.Act2OutpostInfluence} influence, {Rules.Act2OutpostBuildMonths} months." + (known ? "" : " Survey it first.")))
                    Act2.Order(s, fl, "outpost", x.Id);
            }
            if (role == "colony")
            {
                var target = x.Planets.Where(p => p.Owner < 0 && Act2.Habitable(p)).OrderByDescending(p => p.Size).FirstOrDefault();
                bool can = target != null && x.Owner == me.Id;
                if (G.Button(new Rectangle(px + bw + 6, y, bw, 38), target != null ? $"Colonize {target.Name}" : "Colonize", can, size: 15,
                        tip: x.Owner != me.Id ? "You can only colonize in systems you own." : target == null ? "No free habitable planet here." : $"Founds a colony after {Rules.Act2ColonizeMonths} months."))
                    Act2.Order(s, fl, "colonize", x.Id, target!.Id);
            }
            G.Text("Right-click a system on the map to move or act there.", px, y + 46, 14, Gfx.Muted);
        }
    }

    void FleetPanel(Act2State s, Content c, Fleet f)
    {
        var me = s.Player;
        var panel = new Rectangle(Gfx.W - 392, Gfx.H - 250, 380, 236);
        G.Panel(panel);
        float px = panel.X + 14, y = panel.Y + 10, w = panel.Width - 28;
        G.Text(f.Name.Length > 0 && !Defs.Ship.ContainsKey(f.Name) ? f.Name : StName(Defs.Ship[f.Ships.Keys.First()].NameLoc, f.Name), px, y, 22, Gfx.Gold, true);
        if (G.Button(new Rectangle(panel.X + panel.Width - 38, y, 26, 26), "×", size: 16)) { _selFleet = -1; return; }
        y += 32;
        string where = f.Path.Count > 0 ? $"Travelling to {s.Sys(f.Path[^1]).Name}" : $"At {s.Sys(f.System).Name}";
        string doing = f.Order switch
        {
            "survey" => f.Path.Count > 0 ? " to survey it" : $" · surveying ({Gfx.Num(f.OrderMonths)}/{Rules.Act2SurveyMonths} months)",
            "outpost" => f.Path.Count > 0 ? " to build an outpost" : f.OrderMonths == 0 ? " · waiting for alloys and influence" : $" · building an outpost ({Gfx.Num(f.OrderMonths)}/{Rules.Act2OutpostBuildMonths})",
            "colonize" => f.Path.Count > 0 ? " to found a colony" : $" · founding a colony ({Gfx.Num(f.OrderMonths)}/{Rules.Act2ColonizeMonths})",
            _ => "",
        };
        y += G.Para(where + doing, px, y, 16, w, Gfx.Ink) + 6;
        float x = px;
        foreach (var (id, n) in f.Ships)
        {
            Gfx.DrawFit(ShipPic(id), new Rectangle(x, y, 64, 48), Color.White);
            G.Text($"{n}× {StName(Defs.Ship[id].NameLoc, id)}", x, y + 50, 14, Gfx.Ink);
            x += 92;
            if (x > panel.X + panel.Width - 80) break;
        }
        y += 74;
        if (Act2.IsMilitary(f))
        {
            G.Text($"Power {Gfx.Num(Act2.Power(s, me, f))}" + (f.Damage > 0 ? $" · damage {Gfx.Num(f.Damage)}" : ""), px, y, 16, Gfx.Muted);
            y += 24;
        }
        if (me.Capital >= 0 && G.Button(new Rectangle(px, panel.Y + panel.Height - 46, 160, 34), "Return home", f.System != me.Capital || f.Path.Count > 0, size: 15))
            Act2.Order(s, f, "", me.Capital);
    }

    void PlanetPanel(Act2State s, Content c, Planet p)
    {
        var me = s.Player;
        var panel = new Rectangle(Gfx.W - 392, 60, 380, Gfx.H - 320);
        G.Panel(panel);
        float px = panel.X + 14, y = panel.Y + 10, w = panel.Width - 28;
        Gfx.DrawFit(PlanetPic(p.Class), new Rectangle(px, y, 56, 56), Color.White);
        G.Text(p.Name, px + 64, y + 2, 22, Gfx.Gold, true);
        G.Text($"{StName(p.Class, p.Class)} · size {p.Size}", px + 64, y + 30, 15, Gfx.Muted);
        if (G.Button(new Rectangle(panel.X + panel.Width - 38, y, 26, 26), "×", size: 16)) { _selPlanet = -1; return; }
        y += 64;
        var jobs = Act2.Jobs(p);
        G.Text($"{Math.Floor(p.Pops)} / {Act2.MaxPops(p)} pops · {jobs.Count} jobs" + (p.Pops > jobs.Count ? $" · {Math.Floor(p.Pops) - jobs.Count} unemployed" : ""), px, y, 16, p.Pops > jobs.Count + 1 ? Gfx.Bad : Gfx.Ink);
        y += 24;
        foreach (var wd in p.Wonders)
        {
            Gfx.DrawFit(Icon("ICON_" + wd), new Rectangle(px, y, 26, 26), Color.White);
            G.Text(G.Clip($"{c.Name(wd)}: {Defs.Modifier[Defs.Wonder[wd].Modifier].EffectText}", 14, w - 34), px + 32, y + 5, 14, Gfx.Gold);
            y += 28;
        }
        G.Text($"Districts {Act2.DistrictsBuilt(p)}/{Act2.DistrictSlots(p)}", px, y, 17, Gfx.Gold); y += 24;
        float bw = (w - 6) / 2;
        int i = 0;
        foreach (var d in Defs.Districts)
        {
            var r = new Rectangle(px + i % 2 * (bw + 6), y + i / 2 * 50, bw, 46);
            int have = p.Districts.GetValueOrDefault(d.Id);
            string? why = Act2.CantBuildOnPlanet(s, me, p, d.Id);
            if (G.IconButton(r, St(d.Art), $"{StName(d.NameLoc, d.Id)} ({have})", $"+1 · {d.CostMinerals} minerals", why == null, tip: $"{d.Desc} Each gives {Rules.Act2DistrictJobs} jobs making {d.PerJob} {ResName(d.Resource)}." + (why != null ? "\n" + why : "")))
                Act2.BuildOnPlanet(s, me, p, d.Id);
            i++;
        }
        y += (Defs.Districts.Length + 1) / 2 * 50 + 6;
        G.Text($"Buildings {Act2.BuildingsBuilt(p)}/{Act2.BuildingSlots(p)}", px, y, 17, Gfx.Gold); y += 24;
        var area = new Rectangle(px - 4, y, w + 8, panel.Y + panel.Height - y - 70);
        var blds = Defs.PlanetBuildings.ToList();
        float yy = G.BeginScroll("bld" + p.Id, area, (blds.Count + 1) / 2 * 50);
        i = 0;
        foreach (var b in blds)
        {
            var r = new Rectangle(px + i % 2 * (bw + 6), yy + i / 2 * 50, bw - 4, 46);
            int have = p.Buildings.Count(x => x == b.Id);
            string? why = Act2.CantBuildOnPlanet(s, me, p, b.Id);
            if (G.IconButton(r, St(b.Art), $"{StName(b.NameLoc, b.Id)}" + (have > 0 ? $" ({have})" : ""), $"{b.CostMinerals} minerals", why == null,
                    tip: $"{b.Desc} {b.Jobs} job{(b.Jobs == 1 ? "" : "s")} making {b.PerJob} {ResName(b.Resource)}, upkeep {b.UpkeepEnergy} energy." + (why != null ? "\n" + why : "")) && G.Hover(area))
                Act2.BuildOnPlanet(s, me, p, b.Id);
            i++;
        }
        G.EndScroll();
        y = panel.Y + panel.Height - 62;
        G.Text("Building now", px, y, 15, Gfx.Gold);
        G.Text(p.Queue.Count == 0 ? "nothing" : string.Join(", ", p.Queue.Select(q => $"{StName(Defs.District.TryGetValue(q.Id, out var dd) ? dd.NameLoc : Defs.PlanetBuilding[q.Id].NameLoc, q.Id)} ({Math.Ceiling(q.MonthsLeft)} mo)")),
               px, y + 22, 15, Gfx.Ink);
    }

    // ---------------------------------------------------------------- overlays

    Rectangle Overlay2(string title)
    {
        Raylib.DrawRectangle(0, 0, (int)Gfx.W, (int)Gfx.H, new Color(0, 0, 0, 120));
        G.Block(new Rectangle(0, 0, Gfx.W, Gfx.H));
        var r = new Rectangle(Gfx.W / 2 - Math.Min(1100, Gfx.W - 80) / 2, 70, Math.Min(1100, Gfx.W - 80), Gfx.H - 120);
        G.Panel(r);
        G.Text(title, r.X + 22, r.Y + 16, 30, Gfx.Gold, true);
        if (G.Button(new Rectangle(r.X + r.Width - 54, r.Y + 14, 38, 34), "×", size: 20)) _overlay2 = "";
        return r;
    }

    void Research2(Act2State s, Content c)
    {
        var me = s.Player;
        var r = Overlay2("Research");
        double inc = Math.Max(0.1, Act2.Report(s, me).Income["research"]);
        G.Text($"{me.Techs.Count} of {Defs.Act2Techs.Length} technologies · {Gfx.Num(inc)} research a month", r.X + 24, r.Y + 58, 17, Gfx.Muted);
        float cw = (r.Width - 60) / 3, ch = 92;
        int i = 0;
        foreach (var t in Defs.Act2Techs.OrderBy(t => me.Techs.Contains(t.Id)).ThenBy(t => !Act2.CanResearch(me, t.Id)).ThenBy(t => t.Cost))
        {
            var cell = new Rectangle(r.X + 22 + i % 3 * (cw + 8), r.Y + 92 + i / 3 * (ch + 8), cw, ch);
            bool known = me.Techs.Contains(t.Id), can = Act2.CanResearch(me, t.Id), sel = me.Researching == t.Id;
            int months = (int)Math.Ceiling(Math.Max(0, t.Cost - (sel ? me.ResearchProgress : 0)) / inc);
            string effect = t.Modifier != "none" ? Defs.Modifier[t.Modifier].EffectText : t.Unlocks != "none" ? "Unlocks " + StName(Defs.Ship.TryGetValue(t.Unlocks, out var sd) ? sd.NameLoc : Defs.PlanetBuilding[t.Unlocks].NameLoc, t.Unlocks) : "";
            string line2 = known ? "Researched · " + effect : can ? $"{months} months · {effect}" : $"Needs {StName(t.Requires, t.Requires)}";
            if (G.IconButton(cell, St(t.Art), StName(t.NameLoc, t.Id), line2, can || known, sel, tip: effect) && can)
            {
                me.Researching = t.Id;
                _overlay2 = "";
            }
            i++;
        }
    }

    void Shipyard2(Act2State s, Content c)
    {
        var me = s.Player;
        var r = Overlay2("Shipyard");
        G.Text($"Ships are built at your capital, {(me.Capital >= 0 ? s.Sys(me.Capital).Name : "(none)")}, one after another.", r.X + 24, r.Y + 58, 17, Gfx.Muted);
        float cw = (r.Width - 60) / 2, ch = 110;
        int i = 0;
        foreach (var sd in Defs.Ships)
        {
            var cell = new Rectangle(r.X + 22 + i % 2 * (cw + 16), r.Y + 92 + i / 2 * (ch + 10), cw, ch);
            string? why = Act2.CantQueueShip(s, me, sd.Id);
            Raylib.DrawRectangleRounded(cell, 0.08f, 6, G.ButtonBg);
            Gfx.DrawFit(ShipPic(sd.Id), new Rectangle(cell.X + 8, cell.Y + 8, 150, cell.Height - 16), Color.White);
            G.Text(StName(sd.NameLoc, sd.Id), cell.X + 170, cell.Y + 10, 20, Gfx.Ink, true);
            string cost = string.Join(", ", new[] { (sd.CostAlloys, "alloys"), (sd.CostMinerals, "minerals"), (sd.CostFood, "food") }.Where(x => x.Item1 > 0).Select(x => $"{x.Item1} {x.Item2}"));
            G.Text($"{cost} · {sd.BuildMonths} months", cell.X + 170, cell.Y + 38, 15, Gfx.Muted);
            G.Text(sd.Role == "military" ? $"Power {sd.Power}, hull {sd.Hull}, fleet size {sd.NavalCap}" : sd.Role switch { "science" => "Surveys systems.", "construction" => "Builds outposts that claim systems.", _ => "Founds a colony on a habitable world." }, cell.X + 170, cell.Y + 58, 15, Gfx.Muted);
            if (G.Button(new Rectangle(cell.X + cell.Width - 120, cell.Y + cell.Height - 42, 108, 34), "Build", why == null, tip: why, size: 16)) Act2.QueueShip(s, me, sd.Id);
            i++;
        }
        float y = r.Y + 92 + (Defs.Ships.Length + 1) / 2 * (ch + 10) + 6;
        G.Text("Queue: " + (me.Shipyard.Count == 0 ? "empty" : string.Join(", ", me.Shipyard.Select(q => $"{StName(Defs.Ship[q.Id].NameLoc, q.Id)} ({Math.Ceiling(q.MonthsLeft)} mo)"))), r.X + 24, y, 17, Gfx.Ink);
    }

    void Empires2(Act2State s, Content c)
    {
        var me = s.Player;
        var r = Overlay2("Empires");
        float y = r.Y + 70;
        G.Text("Empire", r.X + 110, y, 16, Gfx.Gold); G.Text("Systems", r.X + 520, y, 16, Gfx.Gold); G.Text("Fleet", r.X + 620, y, 16, Gfx.Gold); G.Text("Score", r.X + 720, y, 16, Gfx.Gold);
        y += 26;
        var area = new Rectangle(r.X + 10, y, r.Width - 20, r.Height - (y - r.Y) - 12);
        var list = s.Empires.OrderByDescending(e => e.Alive).ThenByDescending(e => e.Score).ToList();
        float yy = G.BeginScroll("empires", area, list.Count * 78);
        foreach (var e in list)
        {
            var row = new Rectangle(r.X + 20, yy, r.Width - 40, 72);
            Gfx.DrawFit(EmpirePic(e), new Rectangle(row.X, row.Y + 4, 64, 64), e.Alive ? Color.White : new Color(255, 255, 255, 90));
            G.Text(e.Name + (e.IsPlayer ? " (you)" : e.Alive ? "" : " (fallen)"), row.X + 90, row.Y + 8, 20, e.Alive ? EColor(e) : Gfx.Muted);
            string sub = e.Kind == "civ" ? $"Led by {e.Leader} · reached space #{e.SpaceRank}" : "Native to this galaxy";
            if (!e.IsPlayer && s.AtWar(me.Id, e.Id)) sub += " · AT WAR";
            G.Text(sub, row.X + 90, row.Y + 38, 15, s.AtWar(me.Id, e.Id) ? Gfx.Bad : Gfx.Muted);
            G.Text($"{s.SystemsOf(e.Id).Count()}", row.X + 500, row.Y + 22, 18, Gfx.Ink);
            G.Text(Gfx.Num(s.Fleets.Where(f => f.Owner == e.Id).Sum(f => Act2.Power(s, e, f))), row.X + 600, row.Y + 22, 18, Gfx.Ink);
            G.Text($"{e.Score}", row.X + 700, row.Y + 22, 18, Gfx.Ink);
            if (!e.IsPlayer && e.Alive)
            {
                var b = new Rectangle(row.X + row.Width - 170, row.Y + 18, 160, 36);
                if (s.AtWar(me.Id, e.Id))
                {
                    bool ok = Act2.WouldAcceptPeace(s, me, e);
                    if (G.Button(b, "Propose peace", ok, size: 16, tip: ok ? "They will accept." : "They won't accept yet (a war lasts at least a year, and they must not be winning).") && G.Hover(area))
                        Act2.MakePeace(s, me, e);
                }
                else if (G.Button(b, "Declare war", size: 16, tip: "Your fleets can then fight theirs and take their systems.") && G.Hover(area))
                    Act2.DeclareWar(s, me, e);
            }
            yy += 78;
        }
        G.EndScroll();
    }

    void EndPopup2(Act2State s, Content c)
    {
        _speed2 = 0;
        Raylib.DrawRectangle(0, 0, (int)Gfx.W, (int)Gfx.H, new Color(0, 0, 0, 150));
        G.Block(new Rectangle(0, 0, Gfx.W, Gfx.H));
        var r = new Rectangle(Gfx.W / 2 - 360, Gfx.H / 2 - 240, 720, 480);
        G.Panel(r);
        bool won = s.Outcome.StartsWith("won");
        G.TextCentered(won ? "Victory" : s.Outcome == "defeated" ? "Your empire has fallen" : "The galaxy has a new leader", new Rectangle(r.X, r.Y + 20, r.Width, 44), 38, won ? Gfx.Gold : Gfx.Bad, true);
        G.TextCentered(s.Outcome switch
        {
            "won_conquest" => "You hold most of the settled galaxy.",
            "won_score" => $"The year {s.EndYear}: your empire leads the galaxy.",
            "lost_score" => $"The year {s.EndYear}: another empire leads the galaxy.",
            _ => "Your last world was taken.",
        }, new Rectangle(r.X, r.Y + 70, r.Width, 26), 19, Gfx.Ink);
        float y = r.Y + 112;
        foreach (var e in s.Empires.OrderByDescending(e => e.Score).Take(7))
        {
            Gfx.DrawFit(EmpirePic(e), new Rectangle(r.X + 60, y, 40, 40), Color.White);
            G.Text(e.Name + (e.IsPlayer ? " (you)" : ""), r.X + 112, y + 8, 19, e.IsPlayer ? Gfx.Gold : EColor(e));
            G.TextRight($"{e.Score}", r.X + r.Width - 60, y + 8, 19, Gfx.Ink);
            y += 44;
        }
        if (G.Button(new Rectangle(r.X + r.Width / 2 - 150, r.Y + r.Height - 70, 300, 50), "Back to title", size: 20))
        {
            File.Delete(SaveGame.DefaultPath);
            Game = null;
            Current = Screen.Title;
        }
    }

    void DrawEnd() => Current = Screen.Title;
}
