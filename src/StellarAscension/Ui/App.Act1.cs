using System.Numerics;
using Raylib_cs;

namespace StellarAscension;

public sealed partial class App
{
    // camera (world pixels at zoom 1) and selection
    Vector2 _cam1;
    float _zoom1 = 1f;
    int _selCity = -1;
    bool _settlerMode;
    string _overlay1 = "";
    int _seenEvents1;
    readonly List<(string text, float until)> _toasts = new();
    const float HexR = 34;
    static readonly float HexW = MathF.Sqrt(3) * HexR;

    Act1State A1 => Game!.Act1!;

    void EnterAct1()
    {
        Current = Screen.Act1;
        var s = A1;
        var cap = s.CitiesOf(s.Player.Id).FirstOrDefault(c => c.Capital) ?? s.CitiesOf(s.Player.Id).FirstOrDefault();
        if (cap != null) { CenterOn(cap.X, cap.Y); _selCity = cap.Id; }
        _seenEvents1 = s.Events.Count;
        _overlay1 = s.Player.Researching == null && s.Outcome == "" ? "research" : "";
    }

    Vector2 TileCenter(int x, int y) => new((x + ((y & 1) == 1 ? 0.5f : 0)) * HexW, y * HexR * 1.5f);
    float MapPixelW => A1.W * HexW;

    void CenterOn(int x, int y)
    {
        var c = TileCenter(x, y);
        _cam1 = c - new Vector2(Gfx.W / 2 / _zoom1, Gfx.H / 2 / _zoom1);
    }

    Vector2 ToScreen(Vector2 world) => (world - _cam1) * _zoom1;
    Vector2 ToWorld(Vector2 screen) => screen / _zoom1 + _cam1;

    (int x, int y)? TileAt(Vector2 screen)
    {
        var w = ToWorld(screen);
        int bestX = -1, bestY = -1;
        float best = float.MaxValue;
        int row = (int)MathF.Round(w.Y / (HexR * 1.5f));
        for (int y = row - 1; y <= row + 1; y++)
        {
            if (y < 0 || y >= A1.H) continue;
            float fx = w.X / HexW - ((y & 1) == 1 ? 0.5f : 0);
            for (int x = (int)MathF.Floor(fx) - 1; x <= (int)MathF.Floor(fx) + 1; x++)
            {
                var c = TileCenter(x, y);
                float d = Vector2.DistanceSquared(c, w);
                if (d < best) { best = d; bestX = x; bestY = y; }
            }
        }
        if (bestY < 0) return null;
        return (((bestX % A1.W) + A1.W) % A1.W, bestY);
    }

    static Color CivColor(Content c, CivState civ, bool secondary = false)
    {
        var d = Act1.CivDef(c, civ);
        if (d == null) return Color.Gray;
        var (r, g, b) = secondary ? d.Secondary : d.Primary;
        return new Color(r, g, b, (byte)255);
    }

    // ---------------------------------------------------------------- frame

    void DrawAct1(float dt)
    {
        G.UseAct1Theme();
        var s = A1;
        var c = Content!;
        HandleMapInput1(dt);
        DrawMap1(s, c);
        TopBar1(s, c);
        Notifications1(s);
        if (_selCity >= 0 && s.City(_selCity) is { } city && city.Owner == s.Player.Id) CityPanel(s, c, city);
        else if (_selCity >= 0 && s.City(_selCity) is { } other) ForeignCityPanel(s, c, other);
        EndTurnButton(s, c);
        if (_overlay1 == "research") ResearchOverlay(s, c);
        else if (_overlay1 == "race") RaceOverlay(s, c);
        else if (_overlay1 == "wonders") WondersOverlay(s, c);
        else if (_overlay1 == "menu") MenuOverlay();
        if (s.Outcome != "") OutcomePopup1(s, c);
        Toasts();
    }

    void HandleMapInput1(float dt)
    {
        float pan = 900 * dt / _zoom1;
        if (Raylib.IsKeyDown(KeyboardKey.A) || Raylib.IsKeyDown(KeyboardKey.Left)) _cam1.X -= pan;
        if (Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.Right)) _cam1.X += pan;
        if (Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.Up)) _cam1.Y -= pan;
        if (Raylib.IsKeyDown(KeyboardKey.S) || Raylib.IsKeyDown(KeyboardKey.Down)) _cam1.Y += pan;
        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            if (_settlerMode) _settlerMode = false;
            else _overlay1 = _overlay1 == "" ? "menu" : "";
        }
        if (_overlay1 != "") return;
        if (G.MouseOnMap)
        {
            float wheel = Raylib.GetMouseWheelMove();
            if (wheel != 0)
            {
                var before = ToWorld(Gfx.Mouse);
                _zoom1 = Math.Clamp(_zoom1 * (wheel > 0 ? 1.12f : 1 / 1.12f), 0.35f, 2.2f);
                _cam1 += before - ToWorld(Gfx.Mouse);
            }
            if (Raylib.IsMouseButtonDown(MouseButton.Right) || Raylib.IsMouseButtonDown(MouseButton.Middle))
                _cam1 -= Raylib.GetMouseDelta() / _zoom1;
        }
        // wrap east-west, clamp north-south
        float mw = MapPixelW;
        if (_cam1.X < -mw) _cam1.X += mw;
        if (_cam1.X > mw) _cam1.X -= mw;
        _cam1.Y = Math.Clamp(_cam1.Y, -Gfx.H / _zoom1 * 0.5f, A1.H * HexR * 1.5f - Gfx.H / _zoom1 * 0.5f);
    }

    // ---- Civ VI map pictures (strategic view + terrain textures), loaded on first use
    Texture2D? MapTex(string name, int maxSize = 512) => G.Tex("map:" + name + "@" + maxSize, () => Res.Civ6?.MapTexture(name, maxSize));

    Texture2D? GroundTex(string name)
    {
        // Civ VI lights its ground in 3D; drawn flat, the textures need brightening to read like the game.
        var t = G.Tex("ground:" + name, () =>
        {
            var img = Res.Civ6?.MapTexture(name, 1024);
            if (img == null) return null;
            float gain = name.StartsWith("FOW") ? 1.3f : 1.75f;
            for (int i = 0; i < img.Rgba.Length; i += 4)
                for (int k = 0; k < 3; k++) img.Rgba[i + k] = (byte)Math.Min(255, img.Rgba[i + k] * gain + 8);
            return img;
        });
        if (t is { } tex && !_wrapped.Contains(name)) { Raylib.SetTextureWrap(tex, TextureWrap.Repeat); _wrapped.Add(name); }
        return t;
    }
    readonly HashSet<string> _wrapped = new();
    readonly Dictionary<string, List<string>> _variants = new();

    /// <summary>The variants of a "{n}" sprite name that exist in the player's Civ VI.</summary>
    List<string> Variants(string pattern)
    {
        if (_variants.TryGetValue(pattern, out var l)) return l;
        l = new();
        if (!pattern.Contains("{n}")) l.Add(pattern);
        else for (int i = 1; i <= 6; i++) { var n = pattern.Replace("{n}", i.ToString()); if (MapTex(n) != null) l.Add(n); }
        return _variants[pattern] = l;
    }

    Texture2D? Variant(string pattern, int x, int y)
    {
        if (pattern == "none") return null;
        var l = Variants(pattern);
        return l.Count == 0 ? null : MapTex(l[(x * 7 + y * 13) % l.Count]);
    }

    /// <summary>A hex filled with a repeating texture laid in map space, so it flows from hex to hex like Civ VI's ground.</summary>
    void TexturedHex(Texture2D tex, Vector2 screen, Vector2 world, float r, Color tint)
    {
        const float uvWorld = 260f;
        Rlgl.SetTexture(tex.Id);
        Rlgl.Begin(DrawMode.Triangles);
        Rlgl.Color4ub(tint.R, tint.G, tint.B, tint.A);
        float wr = HexR * 1.005f;
        for (int i = 0; i < 6; i++)
        {
            float a1 = (30 + 60 * i) * MathF.PI / 180, a2 = (30 + 60 * (i + 1)) * MathF.PI / 180;
            var s1 = screen + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * r;
            var s2 = screen + new Vector2(MathF.Cos(a2), MathF.Sin(a2)) * r;
            var w1 = world + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * wr;
            var w2 = world + new Vector2(MathF.Cos(a2), MathF.Sin(a2)) * wr;
            Rlgl.TexCoord2f(world.X / uvWorld, world.Y / uvWorld); Rlgl.Vertex2f(screen.X, screen.Y);
            Rlgl.TexCoord2f(w2.X / uvWorld, w2.Y / uvWorld); Rlgl.Vertex2f(s2.X, s2.Y);
            Rlgl.TexCoord2f(w1.X / uvWorld, w1.Y / uvWorld); Rlgl.Vertex2f(s1.X, s1.Y);
        }
        Rlgl.End();
        Rlgl.SetTexture(0);
    }

    void Sprite(Texture2D? t, Vector2 p, float size, Color tint, float lift = 0)
    {
        if (t is not { } tex) return;
        float w = size, h = size * tex.Height / tex.Width;
        Raylib.DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height), new Rectangle(p.X - w / 2, p.Y - h / 2 - lift, w, h), Vector2.Zero, 0, tint);
    }

    // Worked-tile improvements, recomputed once a turn.
    Dictionary<int, string> _improve = new();
    int _improveTurn = -1, _improveCities = -1;

    Dictionary<int, string> Improvements(Act1State s)
    {
        if (_improveTurn == s.Turn && _improveCities == s.Cities.Sum(c => c.Pop + c.Id)) return _improve;
        _improveTurn = s.Turn; _improveCities = s.Cities.Sum(c => c.Pop + c.Id);
        _improve = new();
        foreach (var c in s.Cities)
            foreach (var (x, y) in Act1.WorkTiles(s, c))
            {
                var t = s.At(x, y);
                var ter = Defs.Terrains[t.Terrain];
                string on = t.Natural != null ? "" : t.Feature >= 0 ? Defs.Features[t.Feature].Id : !ter.Land ? "water" : ter.Id.EndsWith("_HILLS") ? "hills" : "flat";
                var imp = Defs.Improvements.FirstOrDefault(i => i.On == on);
                if (imp != null) _improve[y * s.W + x] = imp.Sprite;
            }
        return _improve;
    }

    void DrawMap1(Act1State s, Content c)
    {
        var player = s.Player;
        float mw = MapPixelW;
        var hover = G.MouseOnMap && _overlay1 == "" ? TileAt(Gfx.Mouse) : null;
        float r = HexR * _zoom1, size = HexW * _zoom1;
        Raylib.DrawRectangleGradientV(0, 0, (int)Gfx.W, (int)Gfx.H, new Color(26, 42, 60, 255), new Color(14, 24, 38, 255));
        int y0 = Math.Max(0, (int)(_cam1.Y / (HexR * 1.5f)) - 2), y1 = Math.Min(s.H - 1, (int)((_cam1.Y + Gfx.H / _zoom1) / (HexR * 1.5f)) + 2);
        var tiles = new List<(Vector2 p, Vector2 w, int x, int y)>();
        foreach (var shift in new[] { -mw, 0, mw })
            for (int y = y0; y <= y1; y++)
                for (int x = 0; x < s.W; x++)
                {
                    var w = TileCenter(x, y) + new Vector2(shift, 0);
                    var p = ToScreen(w);
                    if (p.X < -size * 2 || p.X > Gfx.W + size * 2) continue;
                    tiles.Add((p, w, x, y));
                }
        var parchment = GroundTex("FOW_Ground_Temp_1024");
        // ---- ground
        foreach (var (p, w, x, y) in tiles)
        {
            var tile = s.At(x, y);
            var t = Defs.Terrains[tile.Terrain];
            bool seen = tile.Revealed, vis = seen && Act1.Visible(s, x, y);
            if (!seen)
            {
                if (parchment is { } pt) TexturedHex(pt, p, w, r * 1.01f, new Color(206, 186, 146, 255));
                else Raylib.DrawPoly(p, 6, r, 30, new Color(120, 104, 82, 255));
                continue;
            }
            if (!t.Land)
            {
                var deep = t.Id == "TERRAIN_OCEAN";
                Raylib.DrawPoly(p, 6, r * 1.01f, 30, vis ? (deep ? new Color(28, 74, 120, 255) : new Color(52, 128, 168, 255)) : (deep ? new Color(150, 156, 146, 255) : new Color(178, 176, 150, 255)));
                var ov = MapTex(vis ? t.Overlay : t.OverlayFow);
                if (ov != null) Gfx.Draw(ov, new Rectangle(p.X - size * 0.55f, p.Y - size * 0.62f, size * 1.1f, size * 1.24f), new Color(255, 255, 255, 170));
                continue;
            }
            var g = GroundTex(vis ? t.Ground : t.GroundFow);
            if (g is { } gt) TexturedHex(gt, p, w, r * 1.01f, vis ? Color.White : new Color(240, 222, 182, 255));
            else Raylib.DrawPoly(p, 6, r, 30, Gfx.Hex(t.Color));
        }
        // ---- coastlines and a faint hex grid
        foreach (var (p, w, x, y) in tiles)
        {
            var tile = s.At(x, y);
            if (!tile.Revealed) continue;
            var t = Defs.Terrains[tile.Terrain];
            if (t.Land)
            {
                int k = 0;
                foreach (var (nx, ny) in NeighborsInOrder(x, y))
                {
                    if (ny >= 0 && ny < s.H && !Defs.Terrains[s.At(nx, ny).Terrain].Land) EdgeLine(p, k, new Color(236, 214, 160, 200), 0.95f, 3.2f);
                    k++;
                }
            }
            Raylib.DrawPolyLinesEx(p, 6, r, 30, 1, new Color(0, 0, 0, 22));
        }
        // ---- territory (under the sprites): Civ VI's double border line in the owner's colours
        foreach (var (p, w, x, y) in tiles)
        {
            var tile = s.At(x, y);
            if (!tile.Revealed || tile.City < 0 || s.City(tile.City) is not { } owner) continue;
            var col = CivColor(c, s.Civs[owner.Owner]);
            var col2 = CivColor(c, s.Civs[owner.Owner], true);
            Raylib.DrawPoly(p, 6, r * 0.98f, 30, new Color(col.R, col.G, col.B, (byte)28));
            int k = 0;
            foreach (var (nx, ny) in NeighborsInOrder(x, y))
            {
                var nOwner = ny < 0 || ny >= s.H ? -2 : s.At(nx, ny).City >= 0 ? s.City(s.At(nx, ny).City)!.Owner : -1;
                if (nOwner != owner.Owner) { EdgeLine(p, k, col, 0.93f, 4f); EdgeLine(p, k, col2, 0.84f, 1.6f); }
                k++;
            }
        }
        // ---- sprites, back to front
        var improve = Improvements(s);
        foreach (var (p, w, x, y) in tiles.OrderBy(t => t.y))
        {
            var tile = s.At(x, y);
            if (!tile.Revealed) continue;
            bool vis = Act1.Visible(s, x, y);
            var t = Defs.Terrains[tile.Terrain];
            var fogTint = vis ? Color.White : new Color(236, 224, 200, 235);
            if (t.Land && t.Overlay != "none" && tile.Natural == null && tile.Built == null && s.CityAt(x, y) == null)
                Sprite(Variant(vis ? t.Overlay : t.OverlayFow, x, y), p, size * (t.Id.EndsWith("_MOUNTAIN") ? 1.35f : 1.25f), fogTint, size * 0.08f);
            if (tile.Feature >= 0 && tile.Built == null && s.CityAt(x, y) == null)
            {
                var f = Defs.Features[tile.Feature];
                Sprite(MapTex(vis ? f.Sprite : f.SpriteFow), p, size * 1.3f, Color.White, size * 0.04f);
            }
            if (tile.Natural != null)
            {
                var nd = c.NaturalDef(tile.Natural);
                var spr = nd.Sprite != "none" ? MapTex(vis ? nd.Sprite : nd.Sprite.Replace("_Visible", "_Revealed")) ?? MapTex(nd.Sprite) : null;
                if (spr != null) Sprite(spr, p, size * 1.25f, Color.White, size * 0.1f);
                else Gfx.DrawFit(Icon("ICON_" + tile.Natural), new Rectangle(p.X - size * 0.42f, p.Y - size * 0.42f, size * 0.84f, size * 0.84f), Color.White);
            }
            if (vis && improve.TryGetValue(y * s.W + x, out var imp) && tile.Built == null) Sprite(MapTex(imp), p, size * 1.1f, Color.White, size * 0.04f);
            if (tile.Built is { } built)
            {
                if (built.StartsWith("district:"))
                {
                    string d = built[9..];
                    // District plate in the owner's colour, Civ VI's district icon, then its buildings.
                    var dc = s.City(tile.City) is { } dcity ? CivColor(c, s.Civs[dcity.Owner]) : Color.Gray;
                    Raylib.DrawPoly(p, 6, r * 0.82f, 30, new Color((byte)(dc.R / 3 + 60), (byte)(dc.G / 3 + 60), (byte)(dc.B / 3 + 60), (byte)150));
                    Sprite(MapTex($"Districts_{d}_Visible"), p - new Vector2(0, size * 0.18f), size * 0.42f, fogTint);
                    var city = s.City(tile.City);
                    if (city != null)
                    {
                        // The district's buildings, in the order they were built.
                        int k = 0;
                        foreach (var b in city.Buildings.Where(b => Defs.Building[b].District == d && Defs.Building[b].Sprite != "none"))
                            Sprite(MapTex(Defs.Building[b].Sprite), p + new Vector2((k++ - 1) * size * 0.26f, size * 0.16f), size * 0.62f, fogTint);
                    }
                }
                else if (Defs.Wonder.TryGetValue(built, out var wd)) Sprite(MapTex(wd.Sprite), p, size * 1.25f, fogTint, size * 0.1f);
            }
        }
        foreach (var (p, w, x, y) in tiles)
            if (s.At(x, y).Revealed && s.CityAt(x, y) is { } city) DrawCity(s, c, city, p, Act1.Visible(s, x, y));
        // ---- selection, settling, hover
        foreach (var (p, w, x, y) in tiles)
        {
            if (_selCity >= 0 && s.City(_selCity) is { } sel && sel.Owner == player.Id && Act1.WorkTiles(s, sel).Contains((x, y)))
                Raylib.DrawCircleV(p + new Vector2(0, r * 0.62f), 3.5f * _zoom1 + 1, Gfx.Good);
            if (_settlerMode && Act1.CanFoundCity(s, player, x, y))
                Raylib.DrawPolyLinesEx(p, 6, r * 0.9f, 30, 2.5f, new Color(140, 255, 140, 220));
            if (hover is { } hv && hv.x == x && hv.y == y) Raylib.DrawPolyLinesEx(p, 6, r, 30, 2.5f, Gfx.Gold);
        }
        // ---- clicks
        if (hover is { } h && _overlay1 == "" && Raylib.IsMouseButtonPressed(MouseButton.Left) && !G.ClickTaken)
        {
            if (_settlerMode)
            {
                if (Act1.PlaceSettler(s, c, player, h.x, h.y))
                {
                    _settlerMode = player.PendingSettlers > 0;
                    _selCity = s.CityAt(h.x, h.y)?.Id ?? _selCity;
                    SaveNow();
                }
            }
            else if (s.At(h.x, h.y).Revealed && s.CityAt(h.x, h.y) is { } clicked) _selCity = clicked.Id;
            else _selCity = -1;
        }
        if (hover is { } ht && _overlay1 == "") TileTip(s, c, ht.x, ht.y);
    }

    static Rectangle Inflate(Rectangle r, float d) => new(r.X - d, r.Y - d, r.Width + 2 * d, r.Height + 2 * d);

    IEnumerable<(int x, int y)> NeighborsInOrder(int x, int y)
    {
        // Same order as the hex edges drawn by EdgeLine: E, NE, NW, W, SW, SE.
        bool odd = (y & 1) == 1;
        var d = odd ? new[] { (1, 0), (1, -1), (0, -1), (-1, 0), (0, 1), (1, 1) } : new[] { (1, 0), (0, -1), (-1, -1), (-1, 0), (-1, 1), (0, 1) };
        foreach (var (dx, dy) in d) yield return ((((x + dx) % A1.W) + A1.W) % A1.W, y + dy);
    }

    void EdgeLine(Vector2 center, int edge, Color col, float inset = 0.97f, float width = 3.5f)
    {
        // Pointy-top hex corners at 30 + 60k degrees (screen y down). Edge k faces direction 0, -60, -120, 180, 120, 60 deg.
        float r = HexR * _zoom1 * inset;
        float facing = new[] { 0f, -60f, -120f, 180f, 120f, 60f }[edge];
        float a1 = (facing - 30) * MathF.PI / 180, a2 = (facing + 30) * MathF.PI / 180;
        var p1 = center + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * r;
        var p2 = center + new Vector2(MathF.Cos(a2), MathF.Sin(a2)) * r;
        Raylib.DrawLineEx(p1, p2, Math.Max(1.5f, width * _zoom1), col);
    }

    void DrawCity(Act1State s, Content c, CityState city, Vector2 p, bool visible)
    {
        var civ = s.Civs[city.Owner];
        var col = CivColor(c, civ);
        var col2 = CivColor(c, civ, true);
        float r = HexR * _zoom1 * 0.62f;
        // Civ VI's city picture for the owner's era (strategic view).
        var era = Defs.Eras[Math.Clamp(Act1.EraIndex(c, civ), 0, Defs.Eras.Length - 1)];
        var spr = MapTex(era.CitySprite.Replace("{v}", visible ? "Visible" : "Revealed")) ?? MapTex(era.CitySprite.Replace("{v}", "Visible"));
        if (spr != null) Sprite(spr, p, HexW * _zoom1 * (1.0f + Math.Min(city.Pop, 20) * 0.012f), Color.White, HexR * _zoom1 * 0.12f);
        else
        {
            Raylib.DrawPoly(p, 6, r * 1.15f, 30, new Color((byte)(col.R / 2), (byte)(col.G / 2), (byte)(col.B / 2), (byte)255));
            Raylib.DrawPoly(p, 6, r, 30, col);
            Gfx.DrawFit(CivSymbol(civ.CivType), new Rectangle(p.X - r * 0.8f, p.Y - r * 0.8f, r * 1.6f, r * 1.6f), col2);
        }
        // Name banner
        float fs = Math.Clamp(15 * _zoom1, 11, 22);
        string label = city.Name;
        float tw = G.Measure(label, fs) + fs * 2.4f;
        var banner = new Rectangle(p.X - tw / 2, p.Y - HexR * _zoom1 - fs * 1.2f, tw, fs * 1.5f);
        Raylib.DrawRectangleRounded(banner, 0.5f, 6, new Color((byte)(col.R / 3 + 10), (byte)(col.G / 3 + 10), (byte)(col.B / 3 + 10), (byte)230));
        Raylib.DrawRectangleRoundedLinesEx(banner, 0.5f, 6, 1.5f, col);
        Raylib.DrawCircleV(new Vector2(banner.X + fs * 0.75f, banner.Y + banner.Height / 2), fs * 0.62f, col2);
        G.TextCentered(city.Pop.ToString(), new Rectangle(banner.X, banner.Y, fs * 1.5f, banner.Height), fs * 0.9f, col);
        G.Text(label, banner.X + fs * 1.7f, banner.Y + fs * 0.22f, fs, city.Capital ? Gfx.Gold : Gfx.Ink);
        if (city.Capital) Raylib.DrawRectangleRoundedLinesEx(Inflate(banner, 2), 0.5f, 6, 1.5f, Gfx.Gold);
        if (city.Id == _selCity) Raylib.DrawPolyLinesEx(p, 6, HexR * _zoom1, 30, 3, Gfx.Gold);
    }

    void TileTip(Act1State s, Content c, int x, int y)
    {
        var tile = s.At(x, y);
        if (!tile.Revealed) { G.Tip("Unexplored. Your borders and new cities reveal the land; Cartography charts the seas, Flight the whole world."); return; }
        var t = Defs.Terrains[tile.Terrain];
        var lines = new List<string>();
        lines.Add(tile.Natural != null ? c.Name(tile.Natural) + " (natural wonder)" : c.Text(t.NameLoc) + (tile.Feature >= 0 ? ", " + c.Text(Defs.Features[tile.Feature].NameLoc) : ""));
        var y0 = Act1.TileYield(s, x, y);
        if (Act1.Workable(s, x, y)) lines.Add($"{Gfx.Num(y0.Food)} Food, {Gfx.Num(y0.Production)} Production, {Gfx.Num(y0.Gold)} Gold, {Gfx.Num(y0.Science)} Science");
        else lines.Add("Can't be worked.");
        if (tile.Natural != null) lines.Add($"In Act 2 this becomes a rare star system: {Defs.Modifier[c.NaturalDef(tile.Natural).Modifier].EffectText}.");
        if (tile.City >= 0 && s.City(tile.City) is { } owner) lines.Add($"Territory of {owner.Name} ({Act1.CivName(c, s.Civs[owner.Owner])}).");
        if (_settlerMode) lines.Add(Act1.CanFoundCity(s, s.Player, x, y) ? "Click to found a city here." : "You can't found a city here.");
        G.Tip(string.Join("\n", lines));
    }

    // ---------------------------------------------------------------- top bar

    void TopBar1(Act1State s, Content c)
    {
        var p = s.Player;
        var bar = new Rectangle(0, 0, Gfx.W, 48);
        Raylib.DrawRectangleRec(bar, new Color(12, 18, 32, 245));
        Raylib.DrawLineEx(new Vector2(0, 48), new Vector2(Gfx.W, 48), 1.5f, G.PanelEdge);
        G.Block(bar);
        var col = CivColor(c, p);
        Raylib.DrawCircle(26, 24, 18, col);
        Gfx.DrawFit(CivSymbol(p.CivType), new Rectangle(12, 10, 28, 28), CivColor(c, p, true));
        G.Text(Act1.CivName(c, p), 52, 13, 21, Gfx.Ink, true);
        var y = Act1.CivYields(s, c, p);
        float x = 60 + G.Measure(Act1.CivName(c, p), 21, true) + 30;
        void Stat(string icon, string text, string tip)
        {
            Gfx.DrawFit(Icon(icon), new Rectangle(x, 10, 28, 28), Color.White);
            G.Text(text, x + 32, 14, 20, Gfx.Ink);
            var r = new Rectangle(x, 6, 36 + G.Measure(text, 20), 36);
            if (G.Hover(r)) G.Tip(tip);
            x += r.Width + 18;
        }
        Stat("ICON_YIELD_SCIENCE_5", $"+{Gfx.Num(y.Science)}", "Science per turn, from citizens, palaces, libraries and tiles.");
        Stat("ICON_YIELD_GOLD_5", $"{Gfx.Num(p.Gold)} (+{Gfx.Num(y.Gold)})", "Gold, and gold per turn. Spend it to buy a city's current Settler or building.");
        Stat("ICON_YIELD_PRODUCTION_5", $"+{Gfx.Num(y.Production)}", "Production per turn across all your cities.");
        Stat("ICON_YIELD_FOOD_5", $"+{Gfx.Num(y.Food)}", "Food per turn across all your cities (before citizens eat).");
        // Research
        var rr = new Rectangle(x + 10, 6, 330, 36);
        if (p.Researching != null)
        {
            var t = c.Tech[p.Researching];
            double sci = Math.Max(1, y.Science);
            int turns = (int)Math.Ceiling(Math.Max(0, t.Cost - p.ResearchProgress) / sci);
            if (G.IconButton(rr, Icon("ICON_" + t.Type), $"{c.Name(t.Type)} · {turns} turn{(turns == 1 ? "" : "s")}", null, tip: "Research. Click to change."))
                _overlay1 = "research";
            Raylib.DrawRectangle((int)rr.X + 40, (int)(rr.Y + rr.Height - 6), (int)((rr.Width - 46) * Math.Min(1, p.ResearchProgress / t.Cost)), 3, Gfx.Rgb(80, 170, 240));
        }
        else if (G.Button(rr, "Choose research", size: 18)) _overlay1 = "research";
        x = rr.X + rr.Width + 12;
        if (G.Button(new Rectangle(x, 6, 120, 36), "Space Race", size: 17, selected: _overlay1 == "race")) _overlay1 = _overlay1 == "race" ? "" : "race";
        if (G.Button(new Rectangle(x + 126, 6, 110, 36), "Wonders", size: 17, selected: _overlay1 == "wonders")) _overlay1 = _overlay1 == "wonders" ? "" : "wonders";
        string when = $"Turn {s.Turn} · {Act1.YearText(s.Year)}";
        G.TextRight(when, Gfx.W - 94, 14, 20, Gfx.Gold);
        if (G.Button(new Rectangle(Gfx.W - 80, 6, 70, 36), "Menu", size: 17, tip: "Save, quit, or go back to the title (Esc)")) _overlay1 = _overlay1 == "menu" ? "" : "menu";
    }

    void Notifications1(Act1State s)
    {
        for (int i = _seenEvents1; i < s.Events.Count; i++)
        {
            var e = s.Events[i];
            if (e.Civ == s.Player.Id || e.Kind is "wonder" or "rival_space" or "satellite" or "defeat" or "time")
                _toasts.Add((e.Text, (float)Raylib.GetTime() + 7));
        }
        _seenEvents1 = s.Events.Count;
        var recent = s.Events.Where(e => e.Civ == s.Player.Id || e.Kind is "wonder" or "rival_space" or "satellite").TakeLast(6).Reverse().ToList();
        float y = Gfx.H - 20 - recent.Count * 26;
        foreach (var e in recent)
        {
            var r = new Rectangle(12, y, 470, 24);
            Raylib.DrawRectangleRounded(r, 0.4f, 4, new Color(10, 14, 26, 190));
            G.Text(G.Clip($"T{e.Turn}  {e.Text}", 15, r.Width - 14), r.X + 8, r.Y + 4, 15, e.Turn == s.Turn - 1 ? Gfx.Ink : Gfx.Muted);
            if (G.Hover(r)) G.Tip(e.Text);
            if (e.X >= 0 && G.Clicked(r)) CenterOn(e.X, e.Y);
            G.Block(r);
            y += 26;
        }
    }

    void Toasts()
    {
        float now = (float)Raylib.GetTime();
        _toasts.RemoveAll(t => t.until < now);
        if (_toasts.Count > 4) _toasts.RemoveRange(0, _toasts.Count - 4);
        float y = 60;
        foreach (var (text, until) in _toasts)
        {
            float w = Math.Min(560, G.Measure(text, 17) + 30);
            var lines = G.Wrap(text, 17, w - 24);
            var r = new Rectangle(Gfx.W / 2 - w / 2, y, w, lines.Count * 21 + 14);
            byte a = (byte)Math.Clamp((until - now) * 255, 0, 235);
            Raylib.DrawRectangleRounded(r, 0.3f, 6, new Color((byte)10, (byte)14, (byte)28, a));
            Raylib.DrawRectangleRoundedLinesEx(r, 0.3f, 6, 1, new Color(G.PanelEdge.R, G.PanelEdge.G, G.PanelEdge.B, a));
            for (int i = 0; i < lines.Count; i++) G.Text(lines[i], r.X + 12, r.Y + 7 + i * 21, 17, new Color(Gfx.Ink.R, Gfx.Ink.G, Gfx.Ink.B, a));
            y += r.Height + 6;
        }
    }

    // ---------------------------------------------------------------- city

    Texture2D? BuildIcon(string id) => id == Act1.Settler ? Icon("ICON_UNIT_SETTLER") : Icon("ICON_" + id);

    string BuildName(Content c, string id) => id == Act1.Settler ? c.Text("LOC_UNIT_SETTLER_NAME") : c.Name(id);

    string BuildDesc(Content c, string id)
    {
        if (id == Act1.Settler) return "Founds a new city. Costs the city 1 citizen.";
        if (Defs.Building.TryGetValue(id, out var b)) return b.Desc;
        if (Defs.Wonder.TryGetValue(id, out var w)) return $"World wonder: {w.Desc} In Act 2 it becomes a feature on your homeworld: {Defs.Modifier[w.Modifier].EffectText}.";
        if (Defs.Project.TryGetValue(id, out var p)) return p.Desc;
        return "";
    }

    void CityPanel(Act1State s, Content c, CityState city)
    {
        var panel = new Rectangle(Gfx.W - 400, 58, 390, Gfx.H - 140);
        G.Panel(panel);
        var rep = Act1.Report(s, c, city);
        float x = panel.X + 16, y = panel.Y + 12, w = panel.Width - 32;
        G.Text(city.Name + (city.Capital ? " (capital)" : ""), x, y, 26, Gfx.Gold, true);
        if (G.Button(new Rectangle(panel.X + panel.Width - 40, y, 28, 28), "×", size: 18)) _selCity = -1;
        y += 36;
        G.Text($"Size {city.Pop}", x, y, 19, Gfx.Ink);
        string grow = rep.FoodSurplus > 0 ? $"grows in {rep.TurnsToGrow} turn{(rep.TurnsToGrow == 1 ? "" : "s")}" : rep.FoodSurplus < 0 ? "starving" : "not growing";
        G.Text($"{Gfx.Signed(rep.FoodSurplus)} food · {grow}", x + 90, y, 17, rep.FoodSurplus < 0 ? Gfx.Bad : Gfx.Muted);
        y += 28;
        float cw = w / 4;
        void Y(string icon, double v, int i)
        {
            Gfx.DrawFit(Icon(icon), new Rectangle(x + i * cw, y, 26, 26), Color.White);
            G.Text(Gfx.Num(v), x + i * cw + 30, y + 3, 19, Gfx.Ink);
        }
        Y("ICON_YIELD_FOOD_5", rep.Total.Food, 0); Y("ICON_YIELD_PRODUCTION_5", rep.Total.Production, 1);
        Y("ICON_YIELD_GOLD_5", rep.Total.Gold, 2); Y("ICON_YIELD_SCIENCE_5", rep.Total.Science, 3);
        y += 38;
        // Current build
        G.Text("Producing", x, y, 18, Gfx.Gold);
        y += 24;
        if (city.Build != null)
        {
            double prog = city.Progress.GetValueOrDefault(city.Build);
            var br = new Rectangle(x, y, w, 52);
            G.IconButton(br, BuildIcon(city.Build), BuildName(c, city.Build), $"{rep.TurnsToBuild} turn{(rep.TurnsToBuild == 1 ? "" : "s")} · {Gfx.Num(prog)}/{Gfx.Num(rep.BuildCost)}", tip: BuildDesc(c, city.Build), selected: true);
            Raylib.DrawRectangle((int)br.X + 56, (int)(br.Y + br.Height - 6), (int)((br.Width - 62) * Math.Min(1, prog / rep.BuildCost)), 3, Gfx.Rgb(220, 150, 60));
            y += 58;
            if (Act1.CanBuy(city.Build))
            {
                double cost = Act1.BuyCost(s, c, city);
                if (G.Button(new Rectangle(x, y, w, 34), $"Buy now for {Gfx.Num(cost)} gold", s.Player.Gold >= cost, size: 17)) { Act1.Buy(s, c, city); SaveNow(); }
                y += 40;
            }
        }
        else { G.Text("Nothing. Choose something below.", x, y + 4, 17, Gfx.Bad); y += 32; }
        // Choices
        G.Text("Choose production", x, y, 18, Gfx.Gold);
        y += 26;
        var listR = new Rectangle(x - 4, y, w + 8, panel.Y + panel.Height - y - 10);
        var options = Act1.AllBuilds().Select(id => (id, why: Act1.CantBuild(s, c, city, id)))
            .Where(o => o.why == null || (Act1.Kind(o.id) != "unit" && !o.why.StartsWith("Already") && o.why != "Coastal cities only." && (Act1.Kind(o.id) == "project" || o.why.StartsWith("Needs a"))))
            .OrderBy(o => o.why != null).ThenBy(o => Act1.Kind(o.id) switch { "project" => 0, "unit" => 1, "building" => 2, _ => 3 })
            .ThenBy(o => Act1.BuildCost(s, c, s.Player, o.id)).ToList();
        float rowH = 50, yy = G.BeginScroll("build" + city.Id, listR, options.Count * rowH);
        foreach (var (id, why) in options)
        {
            var r = new Rectangle(x, yy, w - 8, rowH - 5);
            yy += rowH;
            if (r.Y + r.Height < listR.Y || r.Y > listR.Y + listR.Height) continue;
            double cost = Act1.BuildCost(s, c, s.Player, id);
            int turns = rep.Total.Production > 0 ? (int)Math.Ceiling(Math.Max(0, cost - city.Progress.GetValueOrDefault(id)) / rep.Total.Production) : 99;
            string kind = Act1.Kind(id) switch { "wonder" => "Wonder", "project" => "Project", "unit" => "Unit", _ => "Building" };
            if (G.IconButton(r, BuildIcon(id), BuildName(c, id), why ?? $"{kind} · {turns} turn{(turns == 1 ? "" : "s")} · {Gfx.Num(cost)}", enabled: why == null, selected: city.Build == id, tip: BuildDesc(c, id))
                && G.Hover(listR))
            {
                Act1.SetBuild(s, c, city, id);
                SaveNow();
            }
        }
        G.EndScroll();
    }

    void ForeignCityPanel(Act1State s, Content c, CityState city)
    {
        var civ = s.Civs[city.Owner];
        var panel = new Rectangle(Gfx.W - 400, 58, 390, 210);
        G.Panel(panel);
        G.Text(city.Name, panel.X + 16, panel.Y + 12, 26, Gfx.Gold, true);
        if (G.Button(new Rectangle(panel.X + panel.Width - 40, panel.Y + 12, 28, 28), "×", size: 18)) _selCity = -1;
        Gfx.DrawFit(CivIcon(civ.Leader), new Rectangle(panel.X + 16, panel.Y + 56, 90, 90), Color.White);
        G.Text(Act1.CivName(c, civ), panel.X + 120, panel.Y + 62, 20, Gfx.Ink);
        G.Text(Act1.LeaderName(c, civ), panel.X + 120, panel.Y + 90, 18, Gfx.Muted);
        G.Text($"Size {city.Pop}" + (city.Wonders.Count > 0 ? $" · {city.Wonders.Count} wonder{(city.Wonders.Count == 1 ? "" : "s")}" : ""), panel.X + 120, panel.Y + 118, 17, Gfx.Muted);
        if (city.Wonders.Count > 0) G.Para(string.Join(", ", city.Wonders.Select(c.Name)), panel.X + 16, panel.Y + 158, 15, panel.Width - 32, Gfx.Muted);
    }

    // ---------------------------------------------------------------- end turn

    void EndTurnButton(Act1State s, Content c)
    {
        var p = s.Player;
        var r = new Rectangle(Gfx.W - 400, Gfx.H - 74, 390, 64);
        string label;
        Action act;
        var idle = s.CitiesOf(p.Id).FirstOrDefault(x => x.Build == null && Act1.AvailableBuilds(s, c, x).Count > 0);
        if (s.Outcome != "") { label = "Act 1 is over"; act = () => { }; }
        else if (p.PendingSettlers > 0) { label = _settlerMode ? "Click a green tile to found a city" : "Place your Settler"; act = () => _settlerMode = true; }
        else if (p.Researching == null && Act1.Researchable(c, p).Any()) { label = "Choose research"; act = () => _overlay1 = "research"; }
        else if (idle != null) { label = $"Choose production in {idle.Name}"; act = () => { _selCity = idle.Id; CenterOn(idle.X, idle.Y); }; }
        else { label = "Next turn"; act = () => NextTurn1(); }
        if (G.Button(r, label, size: 22) || (Raylib.IsKeyPressed(KeyboardKey.Enter) && _overlay1 == "")) act();
        if (p.PendingSettlers > 0 && !_settlerMode && label.StartsWith("Place")) { }
    }

    void NextTurn1()
    {
        var s = A1;
        Act1.EndTurn(s, Content!);
        SaveNow();
        if (s.Player.Researching == null && s.Outcome == "") _overlay1 = "research";
        if (s.Player.PendingSettlers > 0) _settlerMode = true;
    }

    // ---------------------------------------------------------------- overlays

    Rectangle Overlay(string title)
    {
        Raylib.DrawRectangle(0, 0, (int)Gfx.W, (int)Gfx.H, new Color(0, 0, 0, 120));
        G.Block(new Rectangle(0, 0, Gfx.W, Gfx.H));
        var r = new Rectangle(Gfx.W / 2 - Math.Min(1100, Gfx.W - 80) / 2, 70, Math.Min(1100, Gfx.W - 80), Gfx.H - 120);
        G.Panel(r);
        G.Text(title, r.X + 22, r.Y + 16, 30, Gfx.Gold, true);
        if (G.Button(new Rectangle(r.X + r.Width - 54, r.Y + 14, 38, 34), "×", size: 20)) _overlay1 = "";
        return r;
    }

    void ResearchOverlay(Act1State s, Content c)
    {
        var p = s.Player;
        var r = Overlay("Research");
        double sci = Math.Max(1, Act1.CivYields(s, c, p).Science);
        G.Text($"{p.Techs.Count} of {c.Techs.Count} technologies known · {Gfx.Num(sci)} science a turn. Rocketry and Satellites lead to the Moon Landing.", r.X + 24, r.Y + 58, 17, Gfx.Muted);
        var options = Act1.Researchable(c, p).OrderBy(t => t.Cost).ToList();
        float cw = (r.Width - 60) / 3, ch = 118, x0 = r.X + 22, y0 = r.Y + 92;
        var area = new Rectangle(r.X + 10, y0, r.Width - 20, r.Height - (y0 - r.Y) - 12);
        float yy = G.BeginScroll("research", area, (options.Count + 2) / 3 * (ch + 10));
        for (int i = 0; i < options.Count; i++)
        {
            var t = options[i];
            var cell = new Rectangle(x0 + i % 3 * (cw + 8), yy + i / 3 * (ch + 10), cw, ch);
            bool sel = p.Researching == t.Type;
            int turns = (int)Math.Ceiling(Math.Max(0, t.Cost - (sel ? p.ResearchProgress : 0)) / sci);
            var unlocks = Defs.Buildings.Where(b => b.RequiresTech == t.Type).Select(b => c.Name(b.Id))
                .Concat(Defs.Wonders.Where(w => w.RequiresTech == t.Type).Select(w => c.Name(w.Id)))
                .Concat(Defs.Projects.Where(pr => pr.RequiresTech == t.Type).Select(pr => c.Name(pr.Id))).ToList();
            var legacy = Defs.TechLegacy.FirstOrDefault(l => l.CivTech == t.Type);
            string tip = c.Text("LOC_" + t.Type + "_DESCRIPTION", "") is { Length: > 0 } d && !d.StartsWith("LOC_") ? d : "";
            if (legacy != null) tip = (tip.Length > 0 ? tip + "\n\n" : "") + $"Space Age legacy: {legacy.Title}. In Stellaris: {Defs.Modifier[legacy.Modifier].EffectText}.";
            if (G.IconButton(new Rectangle(cell.X, cell.Y, cell.Width, 62), Icon("ICON_" + t.Type), c.Name(t.Type), $"{turns} turn{(turns == 1 ? "" : "s")} · {c.Text("LOC_" + t.Era + "_NAME")}",
                    selected: sel, tip: tip.Length > 0 ? tip : null) && G.Hover(area))
            {
                Act1.SetResearch(c, p, t.Type);
                SaveNow();
                _overlay1 = "";
            }
            float ty = cell.Y + 66;
            if (unlocks.Count > 0) { G.Text(G.Clip("Unlocks: " + string.Join(", ", unlocks), 14, cell.Width - 8), cell.X + 6, ty, 14, Gfx.Muted); ty += 18; }
            if (legacy != null) G.Text(G.Clip($"Space Age legacy: {legacy.Title}", 14, cell.Width - 8), cell.X + 6, ty, 14, Gfx.Gold);
        }
        G.EndScroll();
    }

    void RaceOverlay(Act1State s, Content c)
    {
        var r = Overlay("The Space Race");
        var race = Defs.SpaceRaceById[s.Setup.SpaceRace];
        G.Para($"{race.Name}: {race.Desc}", r.X + 24, r.Y + 60, 17, r.Width - 48, Gfx.Muted);
        float y = r.Y + 110;
        G.Text("Civilization", r.X + 120, y, 17, Gfx.Gold); G.Text("Era", r.X + 520, y, 17, Gfx.Gold); G.Text("Techs", r.X + 700, y, 17, Gfx.Gold);
        G.Text("Satellite", r.X + 800, y, 17, Gfx.Gold); G.Text("Moon Landing", r.X + 920, y, 17, Gfx.Gold);
        y += 30;
        foreach (var civ in s.Civs.OrderByDescending(x => x.MoonTurn > 0).ThenBy(x => x.MoonTurn).ThenByDescending(x => x.Techs.Count))
        {
            Gfx.DrawFit(CivIcon(civ.Leader), new Rectangle(r.X + 40, y, 64, 64), Color.White);
            G.Text(Act1.CivName(c, civ) + (civ.IsPlayer ? " (you)" : ""), r.X + 120, y + 8, 20, civ.IsPlayer ? Gfx.Gold : Gfx.Ink);
            G.Text(Act1.LeaderName(c, civ), r.X + 120, y + 36, 16, Gfx.Muted);
            G.Text(c.Text("LOC_" + Content.EraOf(Act1.EraIndex(c, civ)) + "_NAME"), r.X + 520, y + 20, 18, Gfx.Ink);
            G.Text($"{civ.Techs.Count}", r.X + 700, y + 20, 18, Gfx.Ink);
            G.Text(civ.SatelliteDone ? "Launched" : "—", r.X + 800, y + 20, 18, civ.SatelliteDone ? Gfx.Good : Gfx.Muted);
            int rank = s.SpaceOrder.IndexOf(civ.Id) + 1;
            G.Text(rank > 0 ? $"#{rank} (turn {civ.MoonTurn})" : "—", r.X + 920, y + 20, 18, rank == 1 ? Gfx.Gold : rank > 0 ? Gfx.Good : Gfx.Muted);
            y += 74;
        }
    }

    void WondersOverlay(Act1State s, Content c)
    {
        var r = Overlay("World Wonders");
        G.Text("Each wonder can be built once in the world. Yours become features on your homeworld in Act 2.", r.X + 24, r.Y + 60, 17, Gfx.Muted);
        float cw = (r.Width - 60) / 3, ch = 74, x0 = r.X + 22, y0 = r.Y + 96;
        var area = new Rectangle(r.X + 10, y0, r.Width - 20, r.Height - (y0 - r.Y) - 12);
        float yy = G.BeginScroll("wonders", area, (Defs.Wonders.Length + 2) / 3 * (ch + 8));
        for (int i = 0; i < Defs.Wonders.Length; i++)
        {
            var w = Defs.Wonders[i];
            var cell = new Rectangle(x0 + i % 3 * (cw + 8), yy + i / 3 * (ch + 8), cw, ch);
            string who = s.WonderOwner.TryGetValue(w.Id, out var owner) ? $"Built by {Act1.CivName(c, s.Civs[owner])}" : s.Player.Techs.Contains(w.RequiresTech) ? "Available" : $"Needs {c.Name(w.RequiresTech)}";
            G.IconButton(cell, Icon("ICON_" + w.Id), c.Name(w.Id), who, selected: s.WonderOwner.TryGetValue(w.Id, out var o2) && o2 == s.Player.Id, tip: BuildDesc(c, w.Id));
        }
        G.EndScroll();
    }

    void MenuOverlay()
    {
        Raylib.DrawRectangle(0, 0, (int)Gfx.W, (int)Gfx.H, new Color(0, 0, 0, 140));
        G.Block(new Rectangle(0, 0, Gfx.W, Gfx.H));
        var r = new Rectangle(Gfx.W / 2 - 180, Gfx.H / 2 - 150, 360, 300);
        G.Panel(r);
        G.TextCentered("Menu", new Rectangle(r.X, r.Y + 14, r.Width, 36), 30, Gfx.Gold, true);
        if (G.Button(new Rectangle(r.X + 30, r.Y + 70, 300, 46), "Resume")) { _overlay1 = ""; _overlay2 = ""; }
        if (G.Button(new Rectangle(r.X + 30, r.Y + 126, 300, 46), "Save and go to title")) { SaveNow(); _overlay1 = ""; _overlay2 = ""; Current = Screen.Title; }
        if (G.Button(new Rectangle(r.X + 30, r.Y + 182, 300, 46), "Save and quit")) { SaveNow(); Quit(); }
        G.TextCentered("The game also saves every turn.", new Rectangle(r.X, r.Y + 246, r.Width, 24), 15, Gfx.Muted);
    }

    void OutcomePopup1(Act1State s, Content c)
    {
        Raylib.DrawRectangle(0, 0, (int)Gfx.W, (int)Gfx.H, new Color(0, 0, 0, 150));
        G.Block(new Rectangle(0, 0, Gfx.W, Gfx.H));
        var r = new Rectangle(Gfx.W / 2 - 330, Gfx.H / 2 - 200, 660, 400);
        G.Panel(r);
        if (s.Outcome == "space")
        {
            Gfx.DrawFit(Icon("ICON_PROJECT_LAUNCH_MOON_LANDING"), new Rectangle(r.X + r.Width / 2 - 60, r.Y + 20, 120, 120), Color.White);
            G.TextCentered("Act 1 complete: your civilization has reached space", new Rectangle(r.X, r.Y + 150, r.Width, 36), 26, Gfx.Gold, true);
            int rank = s.SpaceOrder.IndexOf(s.Player.Id) + 1;
            G.Para(rank == 1 ? "You won the race to space. You start Act 2 with the rare star systems made from Earth's natural wonders, and the First Among the Stars bonus."
                             : $"You reached space #{rank}. {Act1.CivName(c, s.Civs[s.SpaceOrder[0]])} got there first and starts Act 2 owning the rare star systems.",
                   r.X + 40, r.Y + 200, 19, r.Width - 80, Gfx.Ink);
            if (G.Button(new Rectangle(r.X + r.Width / 2 - 160, r.Y + r.Height - 74, 320, 54), "Continue to the stars", size: 22))
            {
                Game!.Act2 = Handoff.FromAct1(s, c);
                Game.Phase = "handoff";
                SaveNow();
                Current = Screen.Handoff;
            }
        }
        else
        {
            G.TextCentered("You lost the race to space", new Rectangle(r.X, r.Y + 60, r.Width, 40), 30, Gfx.Bad, true);
            var winner = s.Civs[s.SpaceOrder[0]];
            Gfx.DrawFit(CivIcon(winner.Leader), new Rectangle(r.X + r.Width / 2 - 60, r.Y + 112, 120, 120), Color.White);
            G.Para($"{Act1.CivName(c, winner)} completed the Moon Landing first. On Ruthless, only the winner of the space race goes to the stars, so this game is over.",
                   r.X + 40, r.Y + 244, 19, r.Width - 80, Gfx.Ink);
            if (G.Button(new Rectangle(r.X + r.Width / 2 - 160, r.Y + r.Height - 74, 320, 54), "Back to title", size: 22))
            {
                File.Delete(SaveGame.DefaultPath);
                Game = null;
                Current = Screen.Title;
            }
        }
    }
}
