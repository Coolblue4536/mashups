using System.Numerics;
using Raylib_cs;

namespace StellarAscension;

/// <summary>Act 2: the galaxy, laid out like Stellaris's: resources across the top, the menu rail on the left,
/// the outliner on the right, the selected system or planet in the left column, smooth zoom to the cursor.</summary>
public sealed partial class App
{
    Vector2 _cam2;
    float _zoom2 = 1f, _zoom2Target = 1f;
    Vector2 _zoomAnchor;
    int _selSystem = -1, _selFleet = -1, _selPlanet = -1;
    string _overlay2 = "";
    int _seenEvents2;
    bool _outliner = true;
    Vector2? _rightPress2;
    bool _rightDragged2;

    const float TopH = 46, RailW = 64;
    const float ZoomMin = 0.18f, ZoomMax = 3.2f;

    Act2State A2 => Game!.Act2!;

    void EnterAct2()
    {
        Current = Screen.Act2;
        var s = A2;
        _zoom2 = _zoom2Target = 1f;
        if (s.Player.Capital >= 0) { CenterOn2(s.Player.Capital); _selSystem = s.Player.Capital; }
        _seenEvents2 = Math.Max(0, s.Events.Count - 3);
        if (s.Player.Researching == null) _overlay2 = "research";
    }

    void CenterOn2(int sys)
    {
        var x = A2.Sys(sys);
        _cam2 = new Vector2(x.X, x.Y) - new Vector2(Gfx.W / 2 / _zoom2, Gfx.H / 2 / _zoom2);
    }

    /// <summary>Zooms out to show the whole galaxy.</summary>
    void FitGalaxy()
    {
        float R = A2.GalaxyRadius > 0 ? A2.GalaxyRadius : 600;
        _zoom2 = _zoom2Target = Math.Clamp((Gfx.H - TopH - 40) / (R * 2.25f), ZoomMin, 1f);
        _cam2 = new Vector2(0, TopH / 2 / _zoom2) - new Vector2(Gfx.W / 2 / _zoom2, Gfx.H / 2 / _zoom2);
    }

    Vector2 S2(float x, float y) => (new Vector2(x, y) - _cam2) * _zoom2;
    Vector2 W2(Vector2 screen) => screen / _zoom2 + _cam2;

    static Color EColor(Empire e, int a = 255) => new((byte)e.ColorR, (byte)e.ColorG, (byte)e.ColorB, (byte)a);
    /// <summary>An empire's colour made light enough to read as text on the dark panels.</summary>
    public static Color EText(int r, int g, int b)
    {
        double lum = 0.3 * r + 0.59 * g + 0.11 * b, t = lum >= 150 ? 0 : (150 - lum) / (255 - lum);
        return new Color((byte)(r + (255 - r) * t), (byte)(g + (255 - g) * t), (byte)(b + (255 - b) * t), (byte)255);
    }
    static Color EText(Empire e) => EText(e.ColorR, e.ColorG, e.ColorB);
    static Color Alpha(Color c, int a) => new(c.R, c.G, c.B, (byte)Math.Clamp(a, 0, 255));

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
        DrawGalaxy(s, c);
        TopBar2(s, c);
        Rail2(s);
        Events2(s);
        if (_outliner) Outliner2(s, c);
        // Left column: the selected planet (drill-down) or system; the selected fleet below.
        var planet = _selPlanet >= 0 ? s.Systems.SelectMany(x => x.Planets).FirstOrDefault(p => p.Id == _selPlanet) : null;
        if (planet != null && planet.Owner == s.Player.Id) PlanetPanel(s, c, planet);
        else { _selPlanet = -1; if (_selSystem >= 0) SystemPanel(s, c, s.Sys(_selSystem)); }
        if (_selFleet >= 0 && s.Fleets.FirstOrDefault(f => f.Id == _selFleet) is { } fleet) FleetPanel(s, c, fleet);
        else _selFleet = -1;
        ZoomControls2(s);
        EndTurnButton2(s, c);
        if (_overlay2 == "" && _hoverSystem is { } hs && G.MouseOnMap) SystemCard(s, c, s.Sys(hs));
        if (_overlay2 == "research") Research2(s, c);
        else if (_overlay2 == "ships") Shipyard2(s, c);
        else if (_overlay2 == "empires") Empires2(s, c);
        else if (_overlay2 == "log") Log2(s);
        else if (_overlay2 == "battle") BattleWindow(s, c);
        else if (_overlay2 == "system") SystemView(s, c);
        else if (_overlay2 == "menu") MenuOverlay();
        OfferPopup2(s, c);
        if (s.Outcome != "") EndPopup2(s, c);
        Toasts(_overlay2 == "" && s.Outcome == "" && !s.Offers.Any(o => !o.Seen));
    }

    void HandleInput2(float dt)
    {
        float pan = 1100 * dt / _zoom2;
        if (Raylib.IsKeyDown(KeyboardKey.A) || Raylib.IsKeyDown(KeyboardKey.Left)) _cam2.X -= pan;
        if (Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.Right)) _cam2.X += pan;
        if (Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.Up)) _cam2.Y -= pan;
        if (Raylib.IsKeyDown(KeyboardKey.S) || Raylib.IsKeyDown(KeyboardKey.Down)) _cam2.Y += pan;
        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            if (_overlay2 != "") _overlay2 = "";
            else if (_selFleet >= 0) _selFleet = -1;
            else if (_selPlanet >= 0) _selPlanet = -1;
            else if (_selSystem >= 0) _selSystem = -1;
            else _overlay2 = "menu";
        }
        if (_overlay2 == "")
        {
            var centre = new Vector2(Gfx.W / 2, Gfx.H / 2);
            if (Raylib.IsKeyPressed(KeyboardKey.Equal) || Raylib.IsKeyPressed(KeyboardKey.KpAdd)) ZoomBy(1.4f, centre);
            if (Raylib.IsKeyPressed(KeyboardKey.Minus) || Raylib.IsKeyPressed(KeyboardKey.KpSubtract)) ZoomBy(1 / 1.4f, centre);
            if (Raylib.IsKeyPressed(KeyboardKey.Home)) FitGalaxy();
            if (Raylib.IsKeyPressed(KeyboardKey.H) && A2.Player.Capital >= 0) { _zoom2Target = Math.Max(_zoom2Target, 1f); _zoomAnchor = centre; CenterOn2(A2.Player.Capital); }
            if (Raylib.IsKeyPressed(KeyboardKey.R)) _overlay2 = "research";
            if (Raylib.IsKeyPressed(KeyboardKey.E)) _overlay2 = "empires";
            if (Raylib.IsKeyPressed(KeyboardKey.L)) _overlay2 = "log";
            if (Raylib.IsKeyPressed(KeyboardKey.O)) _outliner = !_outliner;
        }
        // Smooth zoom toward the cursor (the world point under it stays put).
        if (MathF.Abs(_zoom2 - _zoom2Target) > 0.0005f)
        {
            var before = W2(_zoomAnchor);
            _zoom2 += (_zoom2Target - _zoom2) * (1 - MathF.Exp(-dt * 14));
            if (MathF.Abs(_zoom2 - _zoom2Target) < 0.001f) _zoom2 = _zoom2Target;
            _cam2 += before - W2(_zoomAnchor);
        }
        if (_overlay2 != "" || !G.MouseOnMap) return;
        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0) ZoomBy(wheel > 0 ? 1.2f : 1 / 1.2f, Gfx.Mouse);
        if (Raylib.IsMouseButtonDown(MouseButton.Middle)) _cam2 -= Raylib.GetMouseDelta() / _zoom2;
        if (Raylib.IsMouseButtonPressed(MouseButton.Right)) { _rightPress2 = Gfx.Mouse; _rightDragged2 = false; }
        if (Raylib.IsMouseButtonDown(MouseButton.Right) && _rightPress2 is { } rp && (_rightDragged2 || Vector2.Distance(rp, Gfx.Mouse) > 6))
        {
            _rightDragged2 = true;
            _cam2 -= Raylib.GetMouseDelta() / _zoom2;
        }
    }

    void ZoomBy(float factor, Vector2 anchor)
    {
        _zoom2Target = Math.Clamp(_zoom2Target * factor, ZoomMin, ZoomMax);
        _zoomAnchor = anchor;
    }

    int? SystemAt(Act2State s, Vector2 screen)
    {
        int? best = null; float bd = MathF.Max(18, 26 * MathF.Min(1, _zoom2 * 1.4f)); bd *= bd;
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

    int? _hoverSystem;
    double _lastSysClick;

    void DrawGalaxy(Act2State s, Content c)
    {
        var me = s.Player;
        float R = s.GalaxyRadius > 0 ? s.GalaxyRadius : 600;
        // Backdrop: dark sky, then the galaxy's own light (arms, core, nebulae), all additive like Stellaris's map.
        Starfield();
        Raylib.BeginBlendMode(BlendMode.Additive);
        foreach (var x in s.Systems)
        {
            var p = S2(x.X, x.Y);
            float rr = 110 * _zoom2;
            if (p.X < -rr || p.Y < -rr || p.X > Gfx.W + rr || p.Y > Gfx.H + rr) continue;
            Raylib.DrawCircleGradient(new Vector2(p.X, p.Y), rr, new Color(70, 64, 92, 26), new Color(0, 0, 0, 0));
        }
        foreach (var n in s.Nebulae)
        {
            var p = S2(n.X, n.Y);
            float size = n.R * 2.4f * _zoom2;
            var tint = n.Tint switch { 0 => new Color(150, 120, 255, 150), 1 => new Color(90, 200, 230, 140), _ => new Color(240, 120, 170, 140) };
            if (St("st_map_nebula") is { } neb)
                Raylib.DrawTexturePro(neb, new Rectangle(0, 0, neb.Width, neb.Height), new Rectangle(p.X, p.Y, size, size), new Vector2(size / 2, size / 2), n.Rotation, tint);
        }
        var core = S2(0, 0);
        float cs = R * 0.75f * _zoom2;
        if (St("st_map_core") is { } coreTex)
            Raylib.DrawTexturePro(coreTex, new Rectangle(0, 0, coreTex.Width, coreTex.Height), new Rectangle(core.X - cs / 2, core.Y - cs / 2, cs, cs), Vector2.Zero, 0, new Color(255, 236, 210, 200));
        Raylib.DrawCircleGradient(new Vector2(core.X, core.Y), R * 0.22f * _zoom2, new Color(255, 240, 220, 120), new Color(0, 0, 0, 0));
        Raylib.EndBlendMode();

        // Territory, then each empire's name across it when zoomed out (as Stellaris does).
        DrawTerritory(s);
        EmpireNames(s);

        // Hyperlanes: a soft glow under a fine line; inside one empire, in its colour.
        float lw = Math.Clamp(_zoom2, 0.5f, 1.5f);
        foreach (var x in s.Systems)
            foreach (var n in x.Lanes.Where(n => n > x.Id))
            {
                var y = s.Sys(n);
                var a = S2(x.X, x.Y); var b = S2(y.X, y.Y);
                if (MathF.Max(a.X, b.X) < -20 || MathF.Min(a.X, b.X) > Gfx.W + 20 || MathF.Max(a.Y, b.Y) < -20 || MathF.Min(a.Y, b.Y) > Gfx.H + 20) continue;
                bool owned = x.Owner >= 0 && x.Owner == y.Owner;
                var col = owned ? EColor(s.Empires[x.Owner]) : new Color(120, 180, 255, 255);
                Raylib.DrawLineEx(a, b, 5 * lw, Alpha(col, owned ? 40 : 22));
                Raylib.DrawLineEx(a, b, (owned ? 1.8f : 1.1f) * lw, Alpha(col, owned ? 210 : 120));
            }
        // Stars: a coloured halo, then Stellaris's star sprite (additive: its black is "no light").
        _hoverSystem = _overlay2 == "" && G.MouseOnMap ? SystemAt(s, Gfx.Mouse) : null;
        var hover = _hoverSystem;
        bool far = _zoom2 < 0.5f;
        foreach (var x in s.Systems)
        {
            var p = S2(x.X, x.Y);
            if (p.X < -60 || p.Y < -60 || p.X > Gfx.W + 60 || p.Y > Gfx.H + 60) continue;
            var sc = Defs.StarClass.TryGetValue(x.Star, out var scd) ? scd : Defs.StarClasses[0];
            var col = Gfx.Hex(sc.Color);
            float r = 12 * Math.Clamp(_zoom2, 0.45f, 1.7f);
            Raylib.BeginBlendMode(BlendMode.Additive);
            Raylib.DrawCircleGradient(new Vector2(p.X, p.Y), r * 2.6f, new Color(col.R, col.G, col.B, (byte)90), new Color(0, 0, 0, 0));
            if (St(sc.Art) is { } tex) Gfx.DrawFit(tex, new Rectangle(p.X - r * 1.7f, p.Y - r * 1.7f, r * 3.4f, r * 3.4f), Color.White);
            else Raylib.DrawCircleV(p, r * 0.5f, col);
            Raylib.EndBlendMode();
            if (x.RareFeature != null) Gfx.DrawFit(Icon("ICON_" + x.RareFeature), new Rectangle(p.X + r * 0.7f, p.Y - r * 2.2f, r * 1.9f, r * 1.9f), Color.White);
            if (x.Home && x.Owner >= 0) Raylib.DrawCircleLinesV(p, r * 1.9f, EColor(s.Empires[x.Owner], 220));
            if (x.Id == _selSystem) { Raylib.DrawCircleLinesV(p, r * 1.5f, Gfx.Gold); Raylib.DrawCircleLinesV(p, r * 1.56f, Gfx.Gold); }
            if (hover == x.Id) Raylib.DrawCircleLinesV(p, r * 1.75f, Gfx.Ink);
            // Markers under the star: your colonies, free habitable worlds (once surveyed).
            if (!far && x.SurveyedBy.Contains(me.Id))
            {
                int mine = x.Planets.Count(pl => pl.Owner == me.Id), free = x.Planets.Count(pl => pl.Owner < 0 && Act2.Habitable(pl));
                float mx = p.X - (mine + free - 1) * 4.5f;
                for (int k = 0; k < mine; k++, mx += 9) Raylib.DrawCircleV(new Vector2(mx, p.Y + r * 1.15f), 3.2f, EColor(me));
                for (int k = 0; k < free; k++, mx += 9) { Raylib.DrawCircleV(new Vector2(mx, p.Y + r * 1.15f), 3.2f, new Color(20, 30, 30, 220)); Raylib.DrawCircleLinesV(new Vector2(mx, p.Y + r * 1.15f), 3.2f, Gfx.Good); }
            }
            // Names: all when close; only claimed systems when zoomed out a little; none when far.
            bool showName = !far && (_zoom2 > 0.7f || x.Owner >= 0 || x.Id == _selSystem) || x.Id == hover;
            if (showName)
            {
                float fs = Math.Clamp(13.5f * _zoom2, 11, 18);
                var oc = x.Owner >= 0 ? s.Empires[x.Owner] : null;
                var tc = oc != null ? EText(oc) : x.SurveyedBy.Contains(me.Id) ? new Color(214, 224, 236, 255) : new Color(150, 162, 182, 220);
                float tw = G.Measure(x.Name, fs);
                float ty = p.Y + r * 1.45f + (x.SurveyedBy.Contains(me.Id) && !far ? 4 : 0);
                Raylib.DrawRectangleRounded(new Rectangle(p.X - tw / 2 - 5, ty - 1, tw + 10, fs + 4), 0.5f, 4, new Color(4, 10, 14, 150));
                G.Text(x.Name, p.X - tw / 2, ty, fs, tc);
            }
        }
        // Battles: a pulsing ring of fire where fleets are fighting.
        float now = (float)Raylib.GetTime();
        foreach (var bt in s.Battles.Where(b => !b.Over))
        {
            var bs = s.Sys(bt.System);
            var bp = S2(bs.X, bs.Y);
            float pr = 22 * Math.Clamp(_zoom2, 0.6f, 1.5f) * (1 + 0.15f * MathF.Sin(now * 6));
            Raylib.BeginBlendMode(BlendMode.Additive);
            Raylib.DrawCircleGradient(bp, pr * 1.8f, new Color(255, 90, 40, 120), new Color(0, 0, 0, 0));
            for (int sp = 0; sp < 5; sp++)
            {
                float ang = now * 2.3f + sp * 1.256f;
                Raylib.DrawCircleV(bp + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * pr, 2.5f, new Color(255, 200, 120, 200));
            }
            Raylib.EndBlendMode();
            Raylib.DrawCircleLinesV(bp, pr, Gfx.Bad);
        }
        // Fleets: Stellaris's map markers (green yours, blue allies, yellow others, red enemies), with the ship class and count.
        var groups = s.Active.GroupBy(f => (f.System, f.Path.Count > 0 ? f.Path[0] : -1)).ToList();
        foreach (var g in groups)
        {
            int k = 0;
            foreach (var f in g.OrderByDescending(f => f.Owner == me.Id))
            {
                var wp = FleetPos(s, f);
                var e = s.Empires[f.Owner];
                bool mil = Act2.IsMilitary(f);
                float sz = (mil ? 13f : 11f) * Math.Clamp(_zoom2, 0.7f, 1.3f);
                var p = S2(wp.X, wp.Y) + new Vector2(18 + k * (sz * 1.6f), -18) * Math.Clamp(_zoom2, 0.7f, 1.2f);
                string rel = f.Owner == me.Id ? "own" : s.AtWar(me.Id, f.Owner) ? "hostile" : s.Allied(me.Id, f.Owner) ? "ally" : "neutral";
                // Point along the route.
                float rot = 0;
                if (f.Path.Count > 0) { var nx = s.Sys(f.Path[0]); rot = MathF.Atan2(nx.Y - wp.Y, nx.X - wp.X) * 180 / MathF.PI + 90; }
                if (St("st_fleet_" + rel) is { } mark)
                    Raylib.DrawTexturePro(mark, new Rectangle(0, 0, mark.Width, mark.Height), new Rectangle(p.X, p.Y, sz * 2, sz * 2), new Vector2(sz, sz), rot, Color.White);
                else Raylib.DrawTriangle(p + new Vector2(0, -sz), p + new Vector2(-sz * 0.8f, sz * 0.7f), p + new Vector2(sz * 0.8f, sz * 0.7f), EColor(e));
                // Ship class badge (the biggest ship in the fleet) in the owner's colour.
                var top = f.Ships.Keys.OrderByDescending(kk => Defs.Ship[kk].Power).ThenBy(kk => kk).First();
                if (_zoom2 > 0.45f && St("st_class_" + top) is { } cls)
                {
                    var bpos = p + new Vector2(sz * 0.95f, sz * 0.55f);
                    Raylib.DrawCircleV(bpos, sz * 0.55f, new Color(6, 14, 18, 220));
                    Raylib.DrawCircleLinesV(bpos, sz * 0.55f, EColor(e));
                    Gfx.DrawFit(cls, new Rectangle(bpos.X - sz * 0.4f, bpos.Y - sz * 0.4f, sz * 0.8f, sz * 0.8f), EText(e));
                }
                int ships = f.Ships.Values.Sum();
                if (ships > 1 && _zoom2 > 0.6f) G.Text(ships.ToString(), p.X - sz * 1.5f, p.Y - sz * 0.6f, 12, Gfx.Ink);
                if (f.Damage > 0 && mil)
                {
                    double hull = f.Ships.Sum(kv => Defs.Ship[kv.Key].Hull * kv.Value) * (1 + Act2.Stat(s, e, "ship_hull_mult"));
                    var hb = new Rectangle(p.X - sz, p.Y + sz + 2, sz * 2, 3);
                    Raylib.DrawRectangleRec(hb, new Color(20, 20, 20, 200));
                    Raylib.DrawRectangleRec(new Rectangle(hb.X, hb.Y, hb.Width * (float)Math.Clamp(1 - f.Damage / Math.Max(1, hull), 0, 1), 3), Gfx.Good);
                }
                if (f.Id == _selFleet)
                {
                    Raylib.DrawCircleLinesV(p, sz + 4, Gfx.Gold);
                    var last = wp;
                    foreach (var n in f.Path)
                    {
                        var y = s.Sys(n);
                        var a = S2(last.X, last.Y); var b = S2(y.X, y.Y);
                        float len = Vector2.Distance(a, b);
                        for (float t = 0; t < len; t += 12) Raylib.DrawLineEx(a + (b - a) * (t / len), a + (b - a) * (MathF.Min(len, t + 7) / len), 2.5f, Gfx.Gold);
                        last = new Vector2(y.X, y.Y);
                    }
                }
                var hit = new Rectangle(p.X - sz, p.Y - sz, sz * 2, sz * 2);
                if (_overlay2 == "" && G.Hover(hit) && G.MouseOnMap)
                {
                    G.Tip($"{f.Name} ({e.Name})\n" + string.Join(", ", f.Ships.Select(kv => $"{kv.Value} {StName(Defs.Ship[kv.Key].NameLoc, kv.Key)}")) + (mil ? $"\nPower {Gfx.Num(Act2.Power(s, e, f))}" : ""));
                    if (f.Owner == me.Id && G.Clicked(hit)) { _selFleet = f.Id; }
                }
                k++;
            }
        }
        // Clicks (and scripted ones, for UI tests). Right-click (without dragging) sends the selected fleet.
        if (_scriptSystem >= 0) { _selSystem = _scriptSystem; _selPlanet = -1; _scriptSystem = -1; }
        if (_scriptOrder >= 0 && _selFleet >= 0 && s.Fleets.FirstOrDefault(f => f.Id == _selFleet) is { } of) { SmartOrder(s, of, _scriptOrder); _scriptOrder = -1; }
        bool rightClick = Raylib.IsMouseButtonReleased(MouseButton.Right) && !_rightDragged2 && _rightPress2 != null;
        if (Raylib.IsMouseButtonReleased(MouseButton.Right)) { _rightPress2 = null; _rightDragged2 = false; }
        if (hover is { } hv && _overlay2 == "")
        {
            if (Raylib.IsMouseButtonPressed(MouseButton.Left) && !G.ClickTaken)
            {
                if (_selSystem == hv && Raylib.GetTime() - _lastSysClick < 0.4) _overlay2 = "system";
                _selSystem = hv; _selPlanet = -1; _lastSysClick = Raylib.GetTime();
            }
            if (rightClick && _selFleet >= 0 && s.Fleets.FirstOrDefault(f => f.Id == _selFleet) is { } sf) SmartOrder(s, sf, hv);
        }
        else if (_overlay2 == "" && G.MouseOnMap && Raylib.IsMouseButtonPressed(MouseButton.Left) && !G.ClickTaken) { _selSystem = -1; _selPlanet = -1; }
    }

    /// <summary>Each empire's name across its territory when zoomed out (as Stellaris does): sized to the territory,
    /// anchored on its most central system, and skipped where it would run into a bigger empire's name.</summary>
    void EmpireNames(Act2State s)
    {
        float fade = Math.Clamp((0.62f - _zoom2) / 0.2f, 0, 1);
        if (fade <= 0) return;
        var placed = new List<Rectangle>();
        foreach (var (e, own) in s.Empires.Where(e => e.Alive).Select(e => (e, own: s.Systems.Where(x => x.Owner == e.Id).ToList()))
                     .Where(t => t.own.Count > 0).OrderByDescending(t => t.e.IsPlayer).ThenByDescending(t => t.own.Count))
        {
            var centre = new Vector2(own.Average(x => x.X), own.Average(x => x.Y));
            var anchor = own.OrderBy(x => Vector2.DistanceSquared(new Vector2(x.X, x.Y), centre)).First();
            float span = own.Max(x => Vector2.Distance(new Vector2(x.X, x.Y), new Vector2(anchor.X, anchor.Y))) * 2 * _zoom2 + 60 * _zoom2;
            var p = S2(anchor.X, anchor.Y) + new Vector2(0, -22 * _zoom2);
            string name = e.Name.ToUpperInvariant();
            float fs = 26;
            while (fs > 12 && G.Measure(name, fs, true) > Math.Max(span, 90)) fs -= 1;
            float tw = G.Measure(name, fs, true);
            var rect = new Rectangle(p.X - tw / 2, p.Y - fs / 2, tw, fs);
            if (placed.Any(r => Raylib.CheckCollisionRecs(Inflate(r, 4), rect))) continue;
            placed.Add(rect);
            G.Text(name, rect.X + 1.5f, rect.Y + 1.5f, fs, Gfx.Rgb(0, 0, 0, (int)(150 * fade)), true);
            G.Text(name, rect.X, rect.Y, fs, Alpha(EText(e), (int)(215 * fade)), true);
        }
    }

    // Territory texture: rebuilt only when ownership changes.
    Texture2D? _terr;
    string _terrKey = "";
    Rectangle _terrWorld;

    void DrawTerritory(Act2State s)
    {
        string key = string.Join(",", s.Systems.Select(x => x.Owner)) + "|" + string.Join(",", s.Empires.Select(e => $"{e.ColorR}{e.ColorG}{e.ColorB}"));
        if (key != _terrKey || _terr == null)
        {
            _terrKey = key;
            if (_terr is { } old) Raylib.UnloadTexture(old);
            float R = (s.GalaxyRadius > 0 ? s.GalaxyRadius : 600) * 1.25f, cell = 4f, reach = 78f;
            int n = (int)(2 * R / cell);
            _terrWorld = new Rectangle(-R, -R, 2 * R, 2 * R);
            var owner = new int[n * n];
            var grid = new Dictionary<(int, int), List<StarSystem>>();
            foreach (var x in s.Systems) { var k = ((int)((x.X + R) / 100), (int)((x.Y + R) / 100)); if (!grid.TryGetValue(k, out var l)) grid[k] = l = new(); l.Add(x); }
            for (int gy = 0; gy < n; gy++)
                for (int gx = 0; gx < n; gx++)
                {
                    float wx = -R + (gx + 0.5f) * cell, wy = -R + (gy + 0.5f) * cell;
                    int cx = (int)((wx + R) / 100), cy = (int)((wy + R) / 100);
                    StarSystem? best = null; float bd = reach * reach;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            if (grid.TryGetValue((cx + dx, cy + dy), out var l))
                                foreach (var x in l)
                                {
                                    float d = (x.X - wx) * (x.X - wx) + (x.Y - wy) * (x.Y - wy);
                                    if (d < bd) { bd = d; best = x; }
                                }
                    owner[gy * n + gx] = best?.Owner ?? -1;
                }
            var px = new byte[n * n * 4];
            for (int gy = 0; gy < n; gy++)
                for (int gx = 0; gx < n; gx++)
                {
                    int o = owner[gy * n + gx];
                    if (o < 0) continue;
                    bool edge = false;
                    for (int dy = -1; dy <= 1 && !edge; dy++)
                        for (int dx = -1; dx <= 1 && !edge; dx++)
                        {
                            int nx = gx + dx, ny = gy + dy;
                            edge = nx < 0 || ny < 0 || nx >= n || ny >= n || owner[ny * n + nx] != o;
                        }
                    var e = s.Empires[o];
                    int i = (gy * n + gx) * 4;
                    px[i] = (byte)Math.Min(255, e.ColorR + (edge ? 50 : 0)); px[i + 1] = (byte)Math.Min(255, e.ColorG + (edge ? 50 : 0)); px[i + 2] = (byte)Math.Min(255, e.ColorB + (edge ? 50 : 0));
                    px[i + 3] = (byte)(edge ? 230 : 55);
                }
            // Soften the cell steps so borders read as smooth curves.
            for (int pass = 0; pass < 2; pass++)
            {
                var src = (byte[])px.Clone();
                for (int gy = 1; gy < n - 1; gy++)
                    for (int gx = 1; gx < n - 1; gx++)
                        for (int k = 0; k < 4; k++)
                        {
                            int sum = 0, wsum = 0;
                            for (int dy = -1; dy <= 1; dy++)
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    int j = ((gy + dy) * n + gx + dx) * 4;
                                    int wgt = (dx == 0 && dy == 0 ? 4 : dx == 0 || dy == 0 ? 2 : 1);
                                    if (k < 3 && src[j + 3] == 0) continue; // colour only from coloured cells
                                    sum += src[j + k] * wgt; wsum += wgt;
                                }
                            px[(gy * n + gx) * 4 + k] = (byte)(wsum == 0 ? 0 : sum / wsum);
                        }
            }
            unsafe
            {
                fixed (byte* p = px)
                {
                    var im = new Image { Data = p, Width = n, Height = n, Mipmaps = 1, Format = PixelFormat.UncompressedR8G8B8A8 };
                    var t = Raylib.LoadTextureFromImage(im);
                    Raylib.SetTextureFilter(t, TextureFilter.Bilinear);
                    _terr = t;
                }
            }
        }
        if (_terr is { } tex)
        {
            var a = S2(_terrWorld.X, _terrWorld.Y);
            Raylib.DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height), new Rectangle(a.X, a.Y, _terrWorld.Width * _zoom2, _terrWorld.Height * _zoom2), Vector2.Zero, 0, Color.White);
        }
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

    // ---------------------------------------------------------------- top bar, rail, zoom

    void TopBar2(Act2State s, Content c)
    {
        var me = s.Player;
        var bar = new Rectangle(0, 0, Gfx.W, TopH);
        Raylib.DrawRectangleGradientV(0, 0, (int)Gfx.W, (int)TopH, new Color(12, 30, 34, 250), new Color(4, 12, 15, 250));
        Raylib.DrawLineEx(new Vector2(0, TopH), new Vector2(Gfx.W, TopH), 1.5f, Alpha(G.PanelEdge, 200));
        G.Block(bar);
        // Emblem
        Raylib.DrawCircle(32, 23, 19, EColor(me));
        Raylib.DrawCircleLines(32, 23, 19, Gfx.Gold);
        EmpireBadge(me, new Rectangle(15, 6, 34, 34), Color.White);
        var rep = Act2.Report(s, me);
        float x = 64;
        foreach (var id in Act2.Res)
        {
            double stock = id == "research" ? me.ResearchProgress : me.Stock.GetValueOrDefault(id);
            double inc = rep.Income[id];
            string txt = id == "research" ? $"+{Gfx.Num(inc)}" : Gfx.Num(Math.Floor(stock));
            float wdt = 34 + Math.Max(G.Measure(txt, 17), G.Measure(Gfx.Signed(inc), 13)) + 8;
            var r = new Rectangle(x, 5, wdt, TopH - 10);
            bool hot = G.Hover(r);
            Raylib.DrawRectangleRounded(r, 0.3f, 4, hot ? new Color(30, 70, 76, 200) : new Color(0, 0, 0, 70));
            Gfx.DrawFit(ResIcon(id), new Rectangle(x + 4, 11, 24, 24), Color.White);
            G.Text(txt, x + 31, id == "research" ? 13 : 6, 17, Gfx.Ink);
            if (id != "research") G.Text(Gfx.Signed(inc), x + 31, 24, 13, inc < 0 ? Gfx.Bad : Gfx.Good);
            if (hot) G.Tip($"{ResName(id)}: {Defs.Resource[id].Desc}" + (id == "research" ? $"\n{Gfx.Num(inc)} research a month." : $"\n{Gfx.Num(stock)} in stock, {Gfx.Signed(inc)} a month."));
            x += wdt + 6;
        }
        var fr = new Rectangle(x + 4, 5, 96, TopH - 10);
        Raylib.DrawRectangleRounded(fr, 0.3f, 4, new Color(0, 0, 0, 70));
        G.Text("Fleet", fr.X + 8, 6, 13, Gfx.Muted);
        G.Text($"{Gfx.Num(rep.NavalUsed)}/{Gfx.Num(rep.NavalCap)}", fr.X + 8, 20, 17, rep.NavalUsed > rep.NavalCap ? Gfx.Bad : Gfx.Ink);
        if (G.Hover(fr)) G.Tip("Naval capacity used / available. Going over it costs extra energy.");
        // Date, Stellaris style, on the right.
        string date = $"{s.Year}.{s.Month % 12 + 1:00}";
        var dr = new Rectangle(Gfx.W - 250, 4, 170, TopH - 8);
        Raylib.DrawRectangleRounded(dr, 0.3f, 4, new Color(0, 0, 0, 90));
        Raylib.DrawRectangleRoundedLinesEx(dr, 0.3f, 4, 1, Alpha(G.PanelEdge, 160));
        G.TextCentered(date, new Rectangle(dr.X, dr.Y + 1, dr.Width, 22), 21, Gfx.Gold, true);
        G.TextCentered($"Turn {Act2.TurnOf(s)} · ends {s.EndYear}", new Rectangle(dr.X, dr.Y + 22, dr.Width, 16), 12, Gfx.Muted);
        if (G.Button(new Rectangle(Gfx.W - 72, 7, 64, 32), "Menu", size: 15, tip: "Save, quit, or go back to the title (Esc)")) _overlay2 = _overlay2 == "menu" ? "" : "menu";
    }

    /// <summary>The menu rail on the left, with Stellaris's own navigation icons.</summary>
    void Rail2(Act2State s)
    {
        var rail = new Rectangle(0, TopH, RailW, 7 * 58 + 10);
        Raylib.DrawRectangleGradientH(0, (int)TopH, (int)RailW, (int)rail.Height, new Color(6, 20, 24, 235), new Color(6, 20, 24, 150));
        G.Block(rail);
        float y = TopH + 8;
        void Item(string icon, string label, string key, bool selected, Action act, string? badge = null)
        {
            var r = new Rectangle(8, y, RailW - 16, 50);
            bool hot = G.Hover(r);
            if (selected || hot) Raylib.DrawRectangleRounded(r, 0.2f, 4, selected ? new Color(40, 110, 110, 150) : new Color(30, 70, 76, 120));
            var tex = St(selected || hot ? icon + "_hi" : icon) ?? St(icon);
            if (tex != null) Gfx.DrawFit(tex, new Rectangle(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4), Color.White);
            else G.TextCentered(label[..1], r, 22, Gfx.Ink);
            if (badge != null)
            {
                Raylib.DrawCircleV(new Vector2(r.X + r.Width - 4, r.Y + 6), 8, Gfx.Bad);
                G.TextCentered(badge, new Rectangle(r.X + r.Width - 12, r.Y - 2, 16, 16), 12, Color.White);
            }
            if (hot) G.Tip($"{label} ({key})");
            if (G.HitArea(r, label)) act();
            y += 58;
        }
        var me = s.Player;
        Item("st_nav_tech", "Research", "R", _overlay2 == "research", () => _overlay2 = _overlay2 == "research" ? "" : "research",
             me.Researching == null && Defs.Act2Techs.Any(t => Act2.CanResearch(s, me, t.Id)) ? "!" : null);
        Item("st_nav_designer", "Shipyard", "S", _overlay2 == "ships", () => _overlay2 = _overlay2 == "ships" ? "" : "ships");
        Item("st_nav_contacts", "Empires", "E", _overlay2 == "empires", () => _overlay2 = _overlay2 == "empires" ? "" : "empires",
             s.Wars.Any(w => w.A == me.Id || w.B == me.Id) ? "!" : null);
        Item("st_nav_planets", "Outliner", "O", _outliner, () => _outliner = !_outliner);
        Item("st_nav_log", "Event log", "L", _overlay2 == "log", () => _overlay2 = _overlay2 == "log" ? "" : "log");
        Item("st_nav_fleets", "Next idle fleet", "", false, () => NextIdleFleet(s));
        var fight = s.Battles.LastOrDefault(b => !b.Over && b.Sides.Contains(me.Id));
        if (fight != null) Item("st_class_battleship", "Battle", "", _overlay2 == "battle", () => { _battleSel = fight.System; _overlay2 = "battle"; }, "!");
    }

    void NextIdleFleet(Act2State s)
    {
        var idle = s.Fleets.Where(f => f.Owner == s.Player.Id && f.Path.Count == 0 && f.Order == "").OrderBy(f => f.Id).ToList();
        if (idle.Count == 0) return;
        var next = idle.FirstOrDefault(f => f.Id > _selFleet) ?? idle[0];
        _selFleet = next.Id;
        _selSystem = next.System;
        _selPlanet = -1;
        CenterOn2(next.System);
    }

    void ZoomControls2(Act2State s)
    {
        float bw = 44, x0 = Gfx.W - 12 - (bw * 4 + 18), y0 = Gfx.H - 72 - 54;
        if (_selFleet >= 0) y0 -= 0;
        var box = new Rectangle(x0 - 6, y0 - 6, bw * 4 + 30, bw + 12);
        Raylib.DrawRectangleRounded(box, 0.3f, 4, new Color(6, 20, 24, 200));
        G.Block(box);
        if (G.Button(new Rectangle(x0, y0, bw, bw), "+", tip: "Zoom in (mouse wheel, +)", size: 24)) ZoomBy(1.4f, new Vector2(Gfx.W / 2, Gfx.H / 2));
        if (G.Button(new Rectangle(x0 + bw + 6, y0, bw, bw), "-", tip: "Zoom out (mouse wheel, -)", size: 24)) ZoomBy(1 / 1.4f, new Vector2(Gfx.W / 2, Gfx.H / 2));
        if (G.Button(new Rectangle(x0 + (bw + 6) * 2, y0, bw, bw), "All", tip: "See the whole galaxy (Home)", size: 15)) FitGalaxy();
        if (G.Button(new Rectangle(x0 + (bw + 6) * 3, y0, bw, bw), "Cap", tip: "Back to your capital (H)", size: 15) && s.Player.Capital >= 0)
        { _zoom2 = _zoom2Target = Math.Max(_zoom2, 1f); CenterOn2(s.Player.Capital); _selSystem = s.Player.Capital; }
        // zoom level bar
        float t = (MathF.Log(_zoom2) - MathF.Log(ZoomMin)) / (MathF.Log(ZoomMax) - MathF.Log(ZoomMin));
        var zb = new Rectangle(x0, y0 + bw + 3, bw * 4 + 18, 3);
        Raylib.DrawRectangleRec(zb, new Color(30, 60, 64, 200));
        Raylib.DrawRectangleRec(new Rectangle(zb.X, zb.Y, zb.Width * t, zb.Height), G.PanelEdge);
    }

    // ---------------------------------------------------------------- news and the log

    void Events2(Act2State s)
    {
        var me = s.Player;
        for (int i = _seenEvents2; i < s.Events.Count; i++)
        {
            var e = s.Events[i];
            if (e.Empire == me.Id || e.Kind is "war" or "peace" or "eliminated" or "captured" or "end" or "rare")
                _toasts.Add((e.Text, (float)Raylib.GetTime() + 7));
        }
        _seenEvents2 = s.Events.Count;
    }

    void Log2(Act2State s)
    {
        var me = s.Player;
        var r = Overlay2("Event log");
        var list = s.Events.Where(e => e.Empire == me.Id || e.Kind is "war" or "peace" or "eliminated" or "captured" or "rare" or "end").Reverse().Take(200).ToList();
        var area = new Rectangle(r.X + 16, r.Y + 64, r.Width - 32, r.Height - 80);
        float yy = G.BeginScroll("log2", area, list.Count * 30);
        foreach (var e in list)
        {
            var row = new Rectangle(area.X, yy, area.Width - 12, 27);
            yy += 30;
            if (row.Y + row.Height < area.Y || row.Y > area.Y + area.Height) continue;
            bool hot = G.Hover(row) && G.Hover(area);
            Raylib.DrawRectangleRounded(row, 0.3f, 4, hot ? G.ButtonHot : new Color(0, 0, 0, 60));
            G.Text($"{(int)Rules.Act2StartYear + e.Month / 12}.{e.Month % 12 + 1:00}", row.X + 8, row.Y + 5, 15, Gfx.Gold);
            G.Text(G.Clip(e.Text, 15, row.Width - 110), row.X + 90, row.Y + 5, 15, e.Kind is "war" or "captured" or "eliminated" ? Gfx.Bad : Gfx.Ink);
            if (hot && e.System >= 0 && G.Clicked(row)) { CenterOn2(e.System); _selSystem = e.System; _overlay2 = ""; }
        }
        G.EndScroll();
    }

    // ---------------------------------------------------------------- outliner

    void Outliner2(Act2State s, Content c)
    {
        var me = s.Player;
        var planets = s.PlanetsOf(me.Id).OrderByDescending(p => p.Homeworld).ThenByDescending(p => p.Pops).ToList();
        var fleets = s.Fleets.Where(f => f.Owner == me.Id).OrderByDescending(Act2.IsMilitary).ThenBy(f => f.Id).ToList();
        float w = 280, top = TopH + 8, bottom = Gfx.H - 140;
        var panel = new Rectangle(Gfx.W - w - 10, top, w, bottom - top);
        float content = 34 + planets.Count * 40 + 34 + fleets.Count * 40 + 10;
        panel.Height = Math.Min(panel.Height, content + 12);
        G.Panel(panel);
        var area = new Rectangle(panel.X + 6, panel.Y + 6, panel.Width - 12, panel.Height - 12);
        float y = G.BeginScroll("outliner", area, content);
        bool inArea = G.Hover(area) || G.ScriptActive;
        G.Text($"Planets ({planets.Count})", area.X + 8, y + 6, 16, Gfx.Gold);
        y += 34;
        foreach (var p in planets)
        {
            var row = new Rectangle(area.X + 2, y, area.Width - 16, 36);
            y += 40;
            if (row.Y + row.Height < area.Y || row.Y > area.Y + area.Height) continue;
            bool sel = p.Id == _selPlanet, hot = inArea && G.Hover(row);
            Raylib.DrawRectangleRounded(row, 0.25f, 4, sel ? Alpha(G.PanelEdge, 110) : hot ? G.ButtonHot : new Color(0, 0, 0, 50));
            Gfx.DrawFit(PlanetPic(p.Class), new Rectangle(row.X + 4, row.Y + 3, 30, 30), Color.White);
            G.Text(G.Clip(p.Name, 15, row.Width - 110), row.X + 40, row.Y + 3, 15, Gfx.Ink);
            G.Text(p.Queue.Count > 0 ? "building" : "idle", row.X + 40, row.Y + 19, 12, p.Queue.Count > 0 ? Gfx.Muted : Gfx.Gold);
            G.TextRight($"{Math.Floor(p.Pops)} pops", row.X + row.Width - 8, row.Y + 9, 14, Gfx.Muted);
            if (hot && G.Clicked(row)) { var sys = s.Systems.First(x => x.Planets.Contains(p)); _selSystem = sys.Id; _selPlanet = p.Id; CenterOn2(sys.Id); }
        }
        G.Text($"Fleets ({fleets.Count})", area.X + 8, y + 6, 16, Gfx.Gold);
        y += 34;
        foreach (var f in fleets)
        {
            var row = new Rectangle(area.X + 2, y, area.Width - 16, 36);
            y += 40;
            if (row.Y + row.Height < area.Y || row.Y > area.Y + area.Height) continue;
            bool sel = f.Id == _selFleet, hot = inArea && G.Hover(row);
            Raylib.DrawRectangleRounded(row, 0.25f, 4, sel ? Alpha(G.PanelEdge, 110) : hot ? G.ButtonHot : new Color(0, 0, 0, 50));
            var first = f.Ships.Keys.First();
            Gfx.DrawFit(ShipPic(first), new Rectangle(row.X + 4, row.Y + 3, 40, 30), Color.White);
            string name = f.Name.Length > 0 && !Defs.Ship.ContainsKey(f.Name) ? f.Name : StName(Defs.Ship[first].NameLoc, first);
            G.Text(G.Clip(name, 15, row.Width - 60), row.X + 50, row.Y + 3, 15, Gfx.Ink);
            string status = f.MiaMonths > 0 ? $"missing: home in ~{Math.Ceiling(f.MiaMonths / (double)Act2.MonthsPerTurn)} turns" : s.Battles.Any(b => !b.Over && b.System == f.System && b.Sides.Contains(me.Id)) ? "IN BATTLE" : f.Order switch
            {
                "survey" => "surveying", "outpost" => "building an outpost", "colonize" => "colonizing",
                _ => f.Path.Count > 0 ? $"to {s.Sys(f.Path[^1]).Name}" : Act2.IsMilitary(f) ? $"power {Gfx.Num(Act2.Power(s, me, f))}" : "idle",
            };
            G.Text(G.Clip(status, 12, row.Width - 60), row.X + 50, row.Y + 19, 12, status == "idle" ? Gfx.Gold : Gfx.Muted);
            if (hot && G.Clicked(row) && f.MiaMonths <= 0) { _selFleet = f.Id; _selSystem = f.System; _selPlanet = -1; CenterOn2(f.System); }
        }
        G.EndScroll();
    }

    // ---------------------------------------------------------------- hover card

    void SystemCard(Act2State s, Content c, StarSystem x)
    {
        var me = s.Player;
        var owner = x.Owner >= 0 ? s.Empires[x.Owner] : null;
        bool known = x.SurveyedBy.Contains(me.Id);
        var lines = new List<(string, Color)>();
        lines.Add(($"{StName(Defs.StarClass.TryGetValue(x.Star, out var sc) ? sc.NameLoc : x.Star, x.Star)}", Gfx.Muted));
        lines.Add((owner != null ? owner.Name + (s.AtWar(me.Id, owner.Id) ? " · at war" : owner.IsPlayer ? " (you)" : "") : "Unclaimed", owner != null ? EText(owner) : Gfx.Muted));
        if (x.RareFeature != null) lines.Add(($"Rare system ({c.Name(x.RareFeature)}): {Defs.Modifier[x.RareModifier!].EffectText}", Gfx.Gold));
        if (known)
        {
            int hab = x.Planets.Count(p => Act2.Habitable(p)), free = x.Planets.Count(p => p.Owner < 0 && Act2.Habitable(p));
            lines.Add(($"{x.Planets.Count} planet{(x.Planets.Count == 1 ? "" : "s")} · {hab} habitable" + (free > 0 ? $" ({free} free)" : ""), Gfx.Ink));
        }
        else lines.Add(("Not surveyed yet.", Gfx.Muted));
        var here = s.Fleets.Where(f => f.System == x.Id && f.Path.Count == 0).GroupBy(f => f.Owner).Select(g => $"{g.Count()} {s.Empires[g.Key].Adjective}").ToList();
        if (here.Count > 0) lines.Add(("Fleets: " + string.Join(", ", here), Gfx.Muted));
        if (_selFleet >= 0) lines.Add(("Right-click to send the selected fleet here.", Gfx.Good));
        float w = 300, pad = 12;
        var wrapped = lines.SelectMany(l => G.Wrap(l.Item1, 15, w - pad * 2).Select(t => (t, l.Item2))).ToList();
        int icons = known ? Math.Min(8, x.Planets.Count) : 0;
        float h = pad + 26 + (icons > 0 ? 38 : 0) + wrapped.Count * 19 + pad;
        var m = Gfx.Mouse;
        var card = new Rectangle(m.X + 22, m.Y + 18, w, h);
        if (card.X + w > Gfx.W - 8) card.X = m.X - w - 16;
        if (card.Y + h > Gfx.H - 8) card.Y = Gfx.H - h - 8;
        Raylib.DrawRectangleRounded(new Rectangle(card.X + 3, card.Y + 4, w, h), 0.06f, 6, new Color(0, 0, 0, 120));
        Raylib.DrawRectangleRounded(card, 0.06f, 6, new Color(6, 22, 26, 240));
        Raylib.DrawRectangleRoundedLinesEx(card, 0.06f, 6, 1.2f, G.PanelEdge);
        float cy = card.Y + pad;
        G.Text(G.Clip(x.Name, 19, w - pad * 2), card.X + pad, cy, 19, Gfx.Gold, true);
        cy += 28;
        if (icons > 0)
        {
            for (int i = 0; i < icons; i++)
            {
                var p = x.Planets[i];
                var r = new Rectangle(card.X + pad + i * 34, cy, 30, 30);
                Gfx.DrawFit(PlanetPic(p.Class), r, Color.White);
                if (p.Owner >= 0) Raylib.DrawCircleLinesV(new Vector2(r.X + 15, r.Y + 15), 16, EColor(s.Empires[p.Owner]));
                else if (Act2.Habitable(p)) Raylib.DrawCircleLinesV(new Vector2(r.X + 15, r.Y + 15), 16, Gfx.Good);
            }
            cy += 38;
        }
        foreach (var (t, col) in wrapped) { G.Text(t, card.X + pad, cy, 15, col); cy += 19; }
    }

    // ---------------------------------------------------------------- panels (left column)

    Rectangle LeftColumn(float height)
    {
        float top = TopH + 8, maxBottom = Gfx.H - (_selFleet >= 0 ? 232 : 12);
        return new Rectangle(RailW + 6, top, 370, Math.Min(height, maxBottom - top));
    }

    void SystemPanel(Act2State s, Content c, StarSystem x)
    {
        var me = s.Player;
        bool fleetOrders = _selFleet >= 0;
        var panel = LeftColumn(66 + (x.RareFeature != null ? 52 : 0) + (x.SurveyedBy.Contains(me.Id) ? x.Planets.Count * 56 : 44) + (fleetOrders ? 104 : 8));
        G.Panel(panel);
        float px = panel.X + 14, y = panel.Y + 12, w = panel.Width - 28;
        var sc = Defs.StarClass.TryGetValue(x.Star, out var scd) ? scd : Defs.StarClasses[0];
        Raylib.BeginBlendMode(BlendMode.Additive);
        if (St(sc.Art) is { } star) Gfx.DrawFit(star, new Rectangle(px - 6, y - 6, 52, 52), Color.White);
        Raylib.EndBlendMode();
        G.Text(x.Name, px + 48, y, 24, Gfx.Gold, true);
        if (G.Button(new Rectangle(panel.X + panel.Width - 38, y, 26, 26), "×", size: 16)) { _selSystem = -1; return; }
        if (G.Button(new Rectangle(panel.X + panel.Width - 130, y, 86, 26), "View", size: 14, tip: "System view: the star and its planets close up.")) _overlay2 = "system";
        var owner = x.Owner >= 0 ? s.Empires[x.Owner] : null;
        G.Text($"{StName(sc.NameLoc, x.Star)}" + (owner != null ? $" · {owner.Name}" : " · unclaimed"), px + 48, y + 28, 15, owner != null ? EText(owner) : Gfx.Muted);
        y += 54;
        if (x.RareFeature != null)
        {
            Gfx.DrawFit(Icon("ICON_" + x.RareFeature), new Rectangle(px, y, 36, 36), Color.White);
            y += G.Para($"Rare system from {c.Name(x.RareFeature)}: whoever holds it gets {Defs.Modifier[x.RareModifier!].EffectText.Replace(" while you hold this system", "")}.", px + 44, y, 15, w - 44, Gfx.Gold) + 6;
        }
        bool known = x.SurveyedBy.Contains(me.Id);
        float ordersH = fleetOrders ? 100 : 0;
        if (!known) { G.Para("Not surveyed: send a science ship (right-click here with it selected) to see its planets.", px, y, 15, w, Gfx.Muted); }
        else
        {
            foreach (var p in x.Planets)
            {
                if (y > panel.Y + panel.Height - ordersH - 60) break;
                var r = new Rectangle(px, y, w, 52);
                string cls = StName(p.Class, p.Class);
                string line2 = p.Owner >= 0 ? $"{s.Empires[p.Owner].Name} · {Math.Floor(p.Pops)} pops" : Act2.Habitable(p) ? $"{cls} · size {p.Size} · habitable" : $"{cls} · size {p.Size}";
                bool mine = p.Owner == me.Id;
                if (G.IconButton(r, PlanetPic(p.Class), p.Name + (p.Homeworld ? " (homeworld)" : ""), line2, selected: p.Id == _selPlanet,
                        tip: (p.Wonders.Count > 0 ? "Wonders: " + string.Join(", ", p.Wonders.Select(c.Name)) + "\n" : "") + (mine ? "Click to manage this planet." : null)) && mine)
                    _selPlanet = p.Id;
                y += 56;
            }
        }
        // Orders for the selected ship
        if (_selFleet >= 0 && s.Fleets.FirstOrDefault(f => f.Id == _selFleet) is { } fl)
        {
            y = panel.Y + panel.Height - 92;
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
            G.Text("Or right-click a system on the map.", px, y + 46, 14, Gfx.Muted);
        }
    }

    void FleetPanel(Act2State s, Content c, Fleet f)
    {
        var me = s.Player;
        var panel = new Rectangle(RailW + 6, Gfx.H - 222, 370, 210);
        G.Panel(panel);
        float px = panel.X + 14, y = panel.Y + 10, w = panel.Width - 28;
        G.Text(f.Name.Length > 0 && !Defs.Ship.ContainsKey(f.Name) ? f.Name : StName(Defs.Ship[f.Ships.Keys.First()].NameLoc, f.Name), px, y, 22, Gfx.Gold, true);
        if (G.Button(new Rectangle(panel.X + panel.Width - 38, y, 26, 26), "×", size: 16)) { _selFleet = -1; return; }
        y += 30;
        string where = f.Path.Count > 0 ? $"Travelling to {s.Sys(f.Path[^1]).Name}" : $"At {s.Sys(f.System).Name}";
        string doing = f.Order switch
        {
            "survey" => f.Path.Count > 0 ? " to survey it" : $" · surveying ({Gfx.Num(f.OrderMonths)}/{Rules.Act2SurveyMonths} months)",
            "outpost" => f.Path.Count > 0 ? " to build an outpost" : f.OrderMonths == 0 ? " · waiting for alloys and influence" : $" · building an outpost ({Gfx.Num(f.OrderMonths)}/{Rules.Act2OutpostBuildMonths})",
            "colonize" => f.Path.Count > 0 ? " to found a colony" : $" · founding a colony ({Gfx.Num(f.OrderMonths)}/{Rules.Act2ColonizeMonths})",
            _ => "",
        };
        y += G.Para(where + doing, px, y, 15, w, Gfx.Ink) + 4;
        float x = px;
        foreach (var (id, n) in f.Ships)
        {
            Gfx.DrawFit(ShipPic(id), new Rectangle(x, y, 64, 46), Color.White);
            G.Text($"{n}× {StName(Defs.Ship[id].NameLoc, id)}", x, y + 48, 13, Gfx.Ink);
            x += 92;
            if (x > panel.X + panel.Width - 80) break;
        }
        y += 68;
        if (Act2.IsMilitary(f)) G.Text($"Power {Gfx.Num(Act2.Power(s, me, f))}" + (f.Damage > 0 ? $" · damage {Gfx.Num(f.Damage)}" : ""), px, y, 15, Gfx.Muted);
        if (me.Capital >= 0 && G.Button(new Rectangle(px, panel.Y + panel.Height - 44, 150, 32), "Return home", f.System != me.Capital || f.Path.Count > 0, size: 15))
            Act2.Order(s, f, "", me.Capital);
        G.Text("Right-click a system to send it there.", px + 160, panel.Y + panel.Height - 36, 13, Gfx.Muted);
    }

    void PlanetPanel(Act2State s, Content c, Planet p)
    {
        var me = s.Player;
        var panel = LeftColumn(Gfx.H);
        G.Panel(panel);
        float px = panel.X + 14, y = panel.Y + 10, w = panel.Width - 28;
        var sys = s.Systems.First(x => x.Planets.Contains(p));
        if (G.Button(new Rectangle(px, y, 96, 24), "< " + G.Clip(sys.Name, 13, 70), size: 13, tip: "Back to the system")) { _selPlanet = -1; _selSystem = sys.Id; return; }
        if (G.Button(new Rectangle(panel.X + panel.Width - 38, y, 26, 26), "×", size: 16)) { _selPlanet = -1; return; }
        y += 30;
        Gfx.DrawFit(PlanetPic(p.Class), new Rectangle(px, y, 60, 60), Color.White);
        G.Text(p.Name, px + 68, y + 4, 22, Gfx.Gold, true);
        G.Text($"{StName(p.Class, p.Class)} · size {p.Size}", px + 68, y + 32, 15, Gfx.Muted);
        y += 68;
        var jobs = Act2.Jobs(p);
        G.Text($"{Math.Floor(p.Pops)} / {Act2.MaxPops(p)} pops · {jobs.Count} jobs" + (p.Pops > jobs.Count ? $" · {Math.Floor(p.Pops) - jobs.Count} unemployed" : ""), px, y, 15, p.Pops > jobs.Count + 1 ? Gfx.Bad : Gfx.Ink);
        y += 22;
        foreach (var wd in p.Wonders.Take(3))
        {
            Gfx.DrawFit(Icon("ICON_" + wd), new Rectangle(px, y, 24, 24), Color.White);
            G.Text(G.Clip($"{c.Name(wd)}: {Defs.Modifier[Defs.Wonder[wd].Modifier].EffectText}", 13, w - 30), px + 30, y + 5, 13, Gfx.Gold);
            y += 26;
        }
        if (p.Wonders.Count > 3) { G.Text($"and {p.Wonders.Count - 3} more wonders", px + 30, y, 13, Gfx.Muted); y += 18; }
        G.Text($"Districts {Act2.DistrictsBuilt(p)}/{Act2.DistrictSlots(p)}", px, y, 16, Gfx.Gold); y += 22;
        float bw = (w - 6) / 2;
        int i = 0;
        foreach (var d in Defs.Districts)
        {
            var r = new Rectangle(px + i % 2 * (bw + 6), y + i / 2 * 48, bw, 44);
            int have = p.Districts.GetValueOrDefault(d.Id);
            string? why = Act2.CantBuildOnPlanet(s, me, p, d.Id);
            if (G.IconButton(r, St(d.Art), $"{StName(d.NameLoc, d.Id)} ({have})", $"+1 · {d.CostMinerals} minerals", why == null, tip: $"{d.Desc} Each gives {Rules.Act2DistrictJobs} jobs making {d.PerJob} {ResName(d.Resource)}." + (why != null ? "\n" + why : "")))
                Act2.BuildOnPlanet(s, me, p, d.Id);
            i++;
        }
        y += (Defs.Districts.Length + 1) / 2 * 48 + 6;
        G.Text($"Buildings {Act2.BuildingsBuilt(p)}/{Act2.BuildingSlots(p)}", px, y, 16, Gfx.Gold); y += 22;
        var area = new Rectangle(px - 4, y, w + 8, panel.Y + panel.Height - y - 62);
        var blds = Defs.PlanetBuildings.ToList();
        float yy = G.BeginScroll("bld" + p.Id, area, (blds.Count + 1) / 2 * 48);
        i = 0;
        foreach (var b in blds)
        {
            var r = new Rectangle(px + i % 2 * (bw + 6), yy + i / 2 * 48, bw - 4, 44);
            int have = p.Buildings.Count(x => x == b.Id);
            string? why = Act2.CantBuildOnPlanet(s, me, p, b.Id);
            if (G.IconButton(r, St(b.Art), $"{StName(b.NameLoc, b.Id)}" + (have > 0 ? $" ({have})" : ""), $"{b.CostMinerals} minerals", why == null,
                    tip: $"{b.Desc} {b.Jobs} job{(b.Jobs == 1 ? "" : "s")} making {b.PerJob} {ResName(b.Resource)}, upkeep {b.UpkeepEnergy} energy." + (why != null ? "\n" + why : "")) && (G.Hover(area) || G.ScriptActive))
                Act2.BuildOnPlanet(s, me, p, b.Id);
            i++;
        }
        G.EndScroll();
        y = panel.Y + panel.Height - 54;
        G.Text("Building now", px, y, 14, Gfx.Gold);
        G.Text(G.Clip(p.Queue.Count == 0 ? "nothing" : string.Join(", ", p.Queue.Select(q => $"{StName(Defs.District.TryGetValue(q.Id, out var dd) ? dd.NameLoc : Defs.PlanetBuilding[q.Id].NameLoc, q.Id)} ({Math.Ceiling(q.MonthsLeft)} mo)")), 14, w),
               px, y + 20, 14, Gfx.Ink);
    }

    // ---------------------------------------------------------------- overlays

    Rectangle Overlay2(string title)
    {
        Raylib.DrawRectangle(0, 0, (int)Gfx.W, (int)Gfx.H, new Color(0, 0, 0, 140));
        G.Block(new Rectangle(0, 0, Gfx.W, Gfx.H));
        var r = new Rectangle(Gfx.W / 2 - Math.Min(1100, Gfx.W - 80) / 2, 70, Math.Min(1100, Gfx.W - 80), Gfx.H - 120);
        G.Panel(r);
        Raylib.DrawRectangleGradientH((int)r.X + 2, (int)r.Y + 2, (int)r.Width - 4, 54, Alpha(G.PanelEdge, 70), new Color(0, 0, 0, 0));
        G.Text(title, r.X + 22, r.Y + 14, 30, Gfx.Gold, true);
        if (G.Button(new Rectangle(r.X + r.Width - 54, r.Y + 12, 38, 34), "×", size: 20)) _overlay2 = "";
        return r;
    }

    static readonly string[] TierNames = { "Pre-FTL", "Tier I", "Tier II", "Tier III", "Tier IV", "Tier V" };

    static Color AreaColor(string area) => area switch { "physics" => new Color(70, 150, 230, 255), "society" => new Color(90, 200, 110, 255), _ => new Color(230, 150, 60, 255) };

    string TechEffect(Act2TechDef t) =>
        string.Join(", ", t.Modifiers.Select(m => Defs.Modifier[m].EffectText).Concat(t.Unlocks != "none"
            ? new[] { "unlocks " + StName(Defs.Ship.TryGetValue(t.Unlocks, out var sd) ? sd.NameLoc : Defs.PlanetBuilding[t.Unlocks].NameLoc, t.Unlocks) } : Array.Empty<string>()));

    /// <summary>The technology tree in Stellaris's tiers: one column per tier, physics/society/engineering colour-coded.
    /// A technology opens once everything it needs is known; exotic ones also need their site.</summary>
    void Research2(Act2State s, Content c)
    {
        var me = s.Player;
        var r = Overlay2("Research");
        double inc = Math.Max(0.1, Act2.Report(s, me).Income["research"]);
        string now = me.Researching != null ? $"Researching {StName(Defs.Act2Tech[me.Researching].NameLoc, me.Researching)} ({Math.Min(100, me.ResearchProgress / Defs.Act2Tech[me.Researching].Cost * 100):0}%)" : "Choose a technology";
        G.Text($"{me.Techs.Count} of {Defs.Act2Techs.Length} known · {Gfx.Num(inc)} research a month · {now}", r.X + 24, r.Y + 58, 16, Gfx.Muted);
        float x0 = r.X + 16, y0 = r.Y + 112, colW = (r.Width - 32) / TierNames.Length, cardH = 58;
        for (int tier = 0; tier < TierNames.Length; tier++)
            G.TextCentered(TierNames[tier], new Rectangle(x0 + tier * colW, r.Y + 84, colW - 6, 22), 17, Gfx.Gold, true);
        var area = new Rectangle(r.X + 8, y0, r.Width - 16, r.Height - (y0 - r.Y) - 10);
        int rows = Defs.Act2Techs.GroupBy(t => t.Tier).Max(g => g.Count());
        float scrollY = G.BeginScroll("tree2", area, rows * (cardH + 6) + 10);
        string? hoverTip = null;
        foreach (var g in Defs.Act2Techs.GroupBy(t => t.Tier))
        {
            int i = 0;
            foreach (var t in g.OrderBy(t => t.Area).ThenBy(t => t.Cost))
            {
                var cell = new Rectangle(x0 + g.Key * colW, scrollY + i * (cardH + 6), colW - 8, cardH);
                i++;
                if (cell.Y + cell.Height < area.Y || cell.Y > area.Y + area.Height) continue;
                bool known = me.Techs.Contains(t.Id), sel = me.Researching == t.Id;
                string? why = known ? null : Act2.CantResearch(s, me, t.Id, c);
                bool can = !known && why == null, exotic = t.Feature != "none";
                bool hot = G.Hover(cell) && G.Hover(area);
                var bg = known ? new Color(30, 60, 50, 230) : sel ? new Color(40, 90, 100, 240) : can ? (hot ? G.ButtonHot : G.ButtonBg) : new Color(20, 26, 30, 220);
                Raylib.DrawRectangleRounded(cell, 0.12f, 6, bg);
                Raylib.DrawRectangle((int)cell.X, (int)cell.Y + 4, 4, (int)cell.Height - 8, AreaColor(t.Area));
                if (exotic) Raylib.DrawRectangleRoundedLinesEx(cell, 0.12f, 6, 1.5f, new Color(190, 120, 255, 220));
                if (sel) Raylib.DrawRectangleRoundedLinesEx(cell, 0.12f, 6, 2f, Gfx.Gold);
                var tint = known || can ? Color.White : new Color(255, 255, 255, 90);
                Gfx.DrawFit(St(t.Art), new Rectangle(cell.X + 8, cell.Y + 6, cardH - 12, cardH - 12), tint);
                float tx = cell.X + cardH + 2, tw = cell.Width - cardH - 8;
                G.Text(G.Clip(StName(t.NameLoc, t.Id), 15, tw), tx, cell.Y + 7, 15, known ? Gfx.Good : can ? Gfx.Ink : Gfx.Muted);
                int months = (int)Math.Ceiling(Math.Max(0, t.Cost - (sel ? me.ResearchProgress : 0)) / inc);
                string line2 = known ? "Researched" : can ? $"{t.Cost} · {months} months" : exotic && !Exotics.Known(s, me).Contains(t.Feature) ? Defs.ExoticSite[t.Feature].Name : "Locked";
                G.Text(G.Clip(line2, 13, tw), tx, cell.Y + 27, 13, exotic && !known ? new Color(200, 150, 255, 255) : Gfx.Muted);
                if (sel) Raylib.DrawRectangle((int)tx, (int)(cell.Y + cell.Height - 8), (int)(tw * Math.Min(1, me.ResearchProgress / t.Cost)), 3, Gfx.Rgb(90, 200, 220));
                if (hot)
                {
                    hoverTip = $"{StName(t.NameLoc, t.Id)} ({TierNames[t.Tier]}, {t.Area}) · cost {t.Cost}\n{TechEffect(t)}"
                               + (t.Requires.Length > 0 ? "\nNeeds: " + string.Join(", ", t.Requires.Select(q => StName(q, q))) : "")
                               + (exotic ? $"\nExotic: needs the {Defs.ExoticSite[t.Feature].Name}." : "") + (why != null ? "\n" + why : "");
                    if (can && G.Clicked(cell)) { me.Researching = t.Id; _overlay2 = ""; }
                }
                if (can && G.ScriptClick != null && G.HitArea(new Rectangle(-1, -1, 0, 0), StName(t.NameLoc, t.Id))) { me.Researching = t.Id; _overlay2 = ""; }
            }
        }
        G.EndScroll();
        if (hoverTip != null) G.Tip(hoverTip);
    }

    void Shipyard2(Act2State s, Content c)
    {
        var me = s.Player;
        var r = Overlay2("Shipyard");
        G.Text($"Ships are built at your capital, {(me.Capital >= 0 ? s.Sys(me.Capital).Name : "(none)")}, one after another.", r.X + 24, r.Y + 60, 17, Gfx.Muted);
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
            G.Text(sd.Role == "military" ? $"Power {sd.Power}, hull {sd.Hull}, fleet size {sd.NavalCap}" : sd.Role switch { "science" => "Surveys systems.", "construction" => "Builds outposts to claim systems.", _ => "Settles a habitable world." }, cell.X + 170, cell.Y + 58, 15, Gfx.Muted);
            if (G.Button(new Rectangle(cell.X + cell.Width - 120, cell.Y + cell.Height - 42, 108, 34), "Build", why == null, tip: why, size: 16)) Act2.QueueShip(s, me, sd.Id);
            i++;
        }
        float y = r.Y + 92 + (Defs.Ships.Length + 1) / 2 * (ch + 10) + 6;
        G.Text("Queue: " + (me.Shipyard.Count == 0 ? "empty" : string.Join(", ", me.Shipyard.Select(q => $"{StName(Defs.Ship[q.Id].NameLoc, q.Id)} ({Math.Ceiling(q.MonthsLeft)} mo)"))), r.X + 24, y, 17, Gfx.Ink);
    }

    void Empires2(Act2State s, Content c)
    {
        var r = Overlay2("Diplomacy");
        Diplomacy2Pane(s, c, r);
    }

    void EndTurnButton2(Act2State s, Content c)
    {
        var me = s.Player;
        var r = new Rectangle(Gfx.W - 312, Gfx.H - 66, 300, 54);
        string label = s.Outcome != "" ? "The game is over"
                     : me.Researching == null && Defs.Act2Techs.Any(t => Act2.CanResearch(s, me, t.Id)) ? "Choose research"
                     : $"End turn ({Act2.MonthsPerTurn} months)";
        if (G.Button(r, label, s.Outcome == "", size: 21) || (Raylib.IsKeyPressed(KeyboardKey.Enter) && _overlay2 == "" && s.Outcome == ""))
        {
            if (label == "Choose research") { _overlay2 = "research"; return; }
            Act2.EndTurn(s, c);
            SaveNow();
            // A battle involving you opens its window, so you can fight on or jump out.
            if (s.Battles.FirstOrDefault(b => !b.Over && b.Sides.Contains(me.Id)) is { } bt) { _battleSel = bt.System; _overlay2 = "battle"; }
        }
    }

    void EndPopup2(Act2State s, Content c)
    {
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
            EmpireBadge(e, new Rectangle(r.X + 60, y, 40, 40), Color.White);
            G.Text(e.Name + (e.IsPlayer ? " (you)" : ""), r.X + 112, y + 8, 19, e.IsPlayer ? Gfx.Gold : EText(e));
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
