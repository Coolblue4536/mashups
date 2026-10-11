using System.Numerics;
using Raylib_cs;

namespace StellarAscension;

/// <summary>Act 1 units on screen, Civ VI style: unit flags in the owner's colours, the selected unit's reach in blue
/// and targets in red, a unit panel, right-click orders, resources on the map, and the tile card with yields.</summary>
public sealed partial class App
{
    int _selUnit = -1;
    bool _yieldLens;
    Vector2? _rightPress;
    readonly List<(Rectangle r, int unit)> _unitHits = new();
    Dictionary<(int x, int y), double>? _reach;
    string _reachKey = "";

    Texture2D? UnitSymbol(string type) => Icon("ICON_" + type + "_WHITE") ?? Icon("ICON_" + type);
    Texture2D? UnitPortrait(string type) => Icon("ICON_" + type + "_PORTRAIT") ?? Icon("ICON_" + type);
    Texture2D? ResourceIcon(string type, bool fog = false) => (fog ? Icon("ICON_" + type + "_FOW") : null) ?? Icon("ICON_" + type);
    Texture2D? YieldIcon(string yieldId) => Icon(Defs.Yield[yieldId].Icon);

    UnitState? SelectedUnit(Act1State s) => _selUnit >= 0 && s.Unit(_selUnit) is { } u && u.Owner == s.Player.Id ? u : null;

    Dictionary<(int x, int y), double> Reach(Act1State s, UnitState u)
    {
        string key = $"{u.Id}:{u.X}:{u.Y}:{u.Moves}:{s.Turn}:{s.Units.Count}:{s.Wars.Count}";
        if (_reach == null || key != _reachKey) { _reach = Military.Reachable(s, u); _reachKey = key; }
        return _reach;
    }

    static string UnitName(Content c, UnitState u) => c.Text(c.Unit[u.Type].NameLoc);

    // ---------------------------------------------------------------- on the map

    /// <summary>Resource icons (those the player can see) and the yields lens.</summary>
    void DrawTileExtras(Act1State s, Content c, List<(Vector2 p, Vector2 w, int x, int y)> tiles, (int x, int y)? hover)
    {
        float size = HexW * _zoom1;
        foreach (var (p, w, x, y) in tiles)
        {
            var tile = s.At(x, y);
            if (!tile.Revealed) continue;
            if (tile.Resource is { } res && Military.CanSee(c, s.Player, res) && tile.Built == null && s.CityAt(x, y) == null)
            {
                float rs = size * 0.34f;
                var at = p + new Vector2(-size * 0.2f, size * 0.12f);
                Raylib.DrawCircleV(at, rs * 0.62f, new Color(16, 22, 34, 200));
                bool seen = Act1.Visible(s, x, y);
                Gfx.DrawFit(ResourceIcon(res, !seen), new Rectangle(at.X - rs / 2, at.Y - rs / 2, rs, rs), seen ? Color.White : new Color(210, 200, 180, 220));
            }
            // The lens covers the land you can see now (Civ VI's yield icons); hovering shows any tile's.
            bool lens = _yieldLens && Act1.Visible(s, x, y) || hover is { } h && h.x == x && h.y == y;
            if (lens && Act1.Workable(s, x, y) && s.CityAt(x, y) == null) DrawYields(s, p, x, y, size);
        }
    }

    /// <summary>Civ VI's yield icons on a tile: one icon per yield with its amount.</summary>
    void DrawYields(Act1State s, Vector2 p, int x, int y, float size)
    {
        var yv = Act1.TileYield(s, x, y, s.OwnerAt(x, y) >= 0 ? null : s.Player);
        var list = new List<(string id, double v)> { ("food", yv.Food), ("production", yv.Production), ("gold", yv.Gold), ("science", yv.Science) }.Where(t => t.v > 0).ToList();
        if (list.Count == 0) return;
        float ic = Math.Clamp(size * 0.3f, 14, 34), gap = ic * 0.95f;
        float x0 = p.X - (list.Count - 1) * gap / 2;
        for (int i = 0; i < list.Count; i++)
        {
            var at = new Vector2(x0 + i * gap, p.Y + size * 0.26f);
            Raylib.DrawCircleV(at, ic * 0.56f, new Color(10, 14, 24, 190));
            Gfx.DrawFit(YieldIcon(list[i].id), new Rectangle(at.X - ic / 2, at.Y - ic / 2, ic, ic), Color.White);
            if (list[i].v > 1) G.TextCentered(Gfx.Num(list[i].v), new Rectangle(at.X - ic / 2, at.Y + ic * 0.15f, ic, ic * 0.7f), ic * 0.62f, Gfx.Ink);
        }
    }

    /// <summary>Unit flags: Civ VI's shield in the owner's colours with the unit's symbol, and a health bar.</summary>
    void DrawUnits(Act1State s, Content c, List<(Vector2 p, Vector2 w, int x, int y)> tiles)
    {
        _unitHits.Clear();
        var me = s.Player;
        float fs = Math.Clamp(30 * _zoom1, 18, 44);
        foreach (var (p, w, x, y) in tiles)
        {
            var u = s.UnitAt(x, y);
            if (u == null || !s.At(x, y).Revealed || u.Owner != me.Id && !Act1.Visible(s, x, y)) continue;
            var civ = s.Civs[u.Owner];
            var col = CivColor(c, civ);
            var col2 = CivColor(c, civ, true);
            var at = s.CityAt(x, y) != null ? p + new Vector2(-HexW * _zoom1 * 0.3f, HexR * _zoom1 * 0.38f) : p + new Vector2(0, -HexR * _zoom1 * 0.12f);
            float wdt = fs * 0.86f, hgt = fs;
            // Shield: a rounded top and a pointed bottom.
            var body = new Rectangle(at.X - wdt / 2, at.Y - hgt / 2, wdt, hgt * 0.7f);
            var shadow = new Color(0, 0, 0, 110);
            Raylib.DrawRectangleRounded(new Rectangle(body.X + 2, body.Y + 3, body.Width, body.Height), 0.35f, 6, shadow);
            Raylib.DrawTriangle(new Vector2(body.X + 2, body.Y + body.Height + 2), new Vector2(at.X + 2, at.Y + hgt / 2 + 3), new Vector2(body.X + body.Width + 2, body.Y + body.Height + 2), shadow);
            Raylib.DrawRectangleRounded(body, 0.35f, 6, col);
            Raylib.DrawTriangle(new Vector2(body.X, body.Y + body.Height - 1), new Vector2(at.X, at.Y + hgt / 2), new Vector2(body.X + body.Width, body.Y + body.Height - 1), col);
            Raylib.DrawRectangleRoundedLinesEx(Inflate(body, 1), 0.35f, 6, 1.5f, u.Id == _selUnit ? Gfx.Gold : col2);
            Gfx.DrawFit(UnitSymbol(u.Type), new Rectangle(at.X - wdt * 0.36f, at.Y - hgt * 0.44f, wdt * 0.72f, wdt * 0.72f), col2);
            // Health
            if (u.Hp < 100)
            {
                var hb = new Rectangle(at.X - wdt / 2, at.Y + hgt / 2 + 2, wdt, 4);
                Raylib.DrawRectangleRec(hb, new Color(20, 20, 20, 220));
                Raylib.DrawRectangleRec(new Rectangle(hb.X, hb.Y, hb.Width * (float)(u.Hp / 100), hb.Height), u.Hp > 60 ? Gfx.Good : u.Hp > 30 ? Gfx.Rgb(230, 190, 60) : Gfx.Bad);
            }
            // Level pips, fortified mark, and "has moves" dot for the player's units.
            for (int k = 0; k < Military.Level(u); k++) Raylib.DrawCircleV(new Vector2(body.X + 4 + k * 6, body.Y - 3), 2.4f, Gfx.Gold);
            if (u.Fortified) Raylib.DrawRectangleRoundedLinesEx(Inflate(body, 3), 0.35f, 6, 1.2f, new Color(255, 255, 255, 140));
            if (u.Owner == me.Id && u.Moves > 0 && !u.Fortified && !u.Skipped) Raylib.DrawCircleV(new Vector2(body.X + body.Width, body.Y), 3.5f, Gfx.Good);
            if (u.Owner != me.Id && s.AtWar(u.Owner, me.Id)) Raylib.DrawCircleV(new Vector2(body.X + body.Width, body.Y), 3.5f, Gfx.Bad);
            _unitHits.Add((new Rectangle(at.X - wdt / 2, at.Y - hgt / 2, wdt, hgt), u.Id));
        }
    }

    /// <summary>The selected unit's reach (blue), its targets (red) and the path to the hovered tile.</summary>
    void DrawSelection(Act1State s, Content c, List<(Vector2 p, Vector2 w, int x, int y)> tiles, (int x, int y)? hover)
    {
        if (SelectedUnit(s) is not { } u) return;
        float r = HexR * _zoom1;
        var reach = u.Moves > 0 ? Reach(s, u) : new();
        foreach (var (p, w, x, y) in tiles)
        {
            if (reach.ContainsKey((x, y)) && (x != u.X || y != u.Y)) Raylib.DrawPoly(p, 6, r * 0.96f, 30, new Color(70, 150, 255, 60));
            if (Military.CanAttack(s, c, u, x, y)) Raylib.DrawPolyLinesEx(p, 6, r * 0.9f, 30, 3, Gfx.Bad);
        }
        if (hover is not { } h || Military.CanAttack(s, c, u, h.x, h.y) || h.x == u.X && h.y == u.Y) return;
        var path = reach.ContainsKey(h) ? null : Military.CanEnter(s, u, h.x, h.y, true) && s.At(h.x, h.y).Revealed ? Military.PathTo(s, u, h.x, h.y, null, 3000) : null;
        var pts = new List<Vector2> { TileScreen(u.X, u.Y) };
        if (reach.ContainsKey(h)) pts.Add(TileScreen(h.x, h.y));
        else if (path != null) pts.AddRange(path.Select(t => TileScreen(t.x, t.y)));
        for (int i = 1; i < pts.Count; i++) Raylib.DrawLineEx(pts[i - 1], pts[i], 3, new Color(255, 255, 255, 200));
        if (pts.Count > 1)
        {
            Raylib.DrawCircleV(pts[^1], 6, Color.White);
            if (path != null)
            {
                int turns = (int)Math.Ceiling(path.Sum(t => Math.Max(1, Military.EnterCost(s, s.Player, t.x, t.y))) / (double)c.Unit[u.Type].Moves);
                G.TextCentered($"{turns}", new Rectangle(pts[^1].X - 12, pts[^1].Y - 30, 24, 20), 18, Gfx.Gold);
            }
        }
    }

    /// <summary>A tile's screen position nearest the camera (the map wraps east-west).</summary>
    Vector2 TileScreen(int x, int y)
    {
        var w = TileCenter(x, y);
        var p = ToScreen(w);
        float mw = MapPixelW * _zoom1;
        if (p.X < -mw / 2) p.X += mw;
        if (p.X > Gfx.W + mw / 2) p.X -= mw;
        return p;
    }

    /// <summary>Clicks on the map with a unit selected: move or attack. Returns true if the click was used.</summary>
    bool UnitClick(Act1State s, Content c, (int x, int y) h, bool right)
    {
        if (SelectedUnit(s) is not { } u) return false;
        if (Military.CanAttack(s, c, u, h.x, h.y))
        {
            Military.Attack(s, c, u, h.x, h.y);
            Act1.Reveal(s);
            if (!s.Units.Contains(u)) _selUnit = -1;
            SaveNow();
            return true;
        }
        if (h.x == u.X && h.y == u.Y) return false;
        bool reach = u.Moves > 0 && Reach(s, u).ContainsKey(h);
        if (reach || right)
        {
            if (Military.Order(s, u, h.x, h.y)) { Act1.Reveal(s); SaveNow(); return true; }
        }
        return false;
    }

    // ---------------------------------------------------------------- the unit panel

    void UnitPanel(Act1State s, Content c, UnitState u)
    {
        var info = c.Unit[u.Type];
        var panel = new Rectangle(12, Gfx.H - 196, 460, 184);
        G.Panel(panel);
        Gfx.DrawFit(UnitPortrait(u.Type), new Rectangle(panel.X + 10, panel.Y + 10, 110, 110), Color.White);
        float x = panel.X + 132, y = panel.Y + 10;
        G.Text(UnitName(c, u), x, y, 24, Gfx.Gold, true);
        if (G.Button(new Rectangle(panel.X + panel.Width - 36, y, 26, 26), "×", size: 16)) { _selUnit = -1; return; }
        y += 32;
        string line = info.Line switch { "recon" => "Recon", "melee" => "Melee", "anti_cavalry" => "Anti-cavalry", "ranged" => "Ranged", "light_cavalry" => "Light cavalry", "heavy_cavalry" => "Heavy cavalry", _ => "Siege" };
        G.Text(line + (Military.Level(u) > 0 ? $" · veteran {Military.Level(u)} (+{Military.Level(u) * Military.LevelStrength})" : "") + $" · {u.Xp} XP", x, y, 15, Gfx.Muted);
        y += 22;
        string str = $"Strength {info.Combat}" + (info.IsRanged ? $" · {(info.Siege ? "Bombard" : "Ranged")} {info.Ranged} (range {info.Range})" : "");
        G.Text(str, x, y, 17, Gfx.Ink); y += 22;
        G.Text($"Moves {Gfx.Num(u.Moves)}/{info.Moves} · upkeep {Gfx.Num(info.Maintenance * Rules.UnitMaintenanceMult)} gold", x, y, 16, Gfx.Ink); y += 22;
        var hb = new Rectangle(x, y + 2, panel.Width - (x - panel.X) - 16, 8);
        Raylib.DrawRectangleRounded(hb, 0.5f, 4, new Color(20, 20, 20, 220));
        Raylib.DrawRectangleRounded(new Rectangle(hb.X, hb.Y, hb.Width * (float)(u.Hp / 100), hb.Height), 0.5f, 4, u.Hp > 60 ? Gfx.Good : u.Hp > 30 ? Gfx.Rgb(230, 190, 60) : Gfx.Bad);
        G.Text($"{Math.Ceiling(u.Hp)} HP", hb.X, hb.Y + 10, 14, Gfx.Muted);
        // Orders
        float by = panel.Y + panel.Height - 46, bw = (panel.Width - 20 - 15) / 4;
        if (G.Button(new Rectangle(panel.X + 10, by, bw, 36), u.Fortified ? "Fortified" : "Fortify", !u.Fortified, tip: $"+{Rules.FortifyBonus} strength when attacked; heals if it doesn't move. (F)", size: 15))
        { u.Fortified = true; u.TargetX = u.TargetY = -1; SelectNextUnit(s); }
        if (G.Button(new Rectangle(panel.X + 15 + bw, by, bw, 36), "Skip turn", u.Moves > 0, tip: "Leave this unit for this turn. (Space)", size: 15)) { u.Skipped = true; SelectNextUnit(s); }
        var to = Military.UpgradeTarget(s, c, u);
        string? why = Military.CantUpgrade(s, c, u);
        if (G.Button(new Rectangle(panel.X + 20 + bw * 2, by, bw, 36), to != null ? $"Upgrade {Military.UpgradeCost(c, u, to)}g" : "Upgrade", why == null,
                tip: to != null ? $"Becomes a {c.Text(to.NameLoc)} (strength {to.Combat}" + (to.IsRanged ? $", ranged {to.Ranged}" : "") + ")." + (why != null ? "\n" + why : "") : why, size: 15))
        { Military.Upgrade(s, c, u); SaveNow(); }
        if (G.Button(new Rectangle(panel.X + 25 + bw * 3, by, bw, 36), "Disband", tip: "Removes the unit (no more upkeep).", size: 15))
        { s.Units.Remove(u); _selUnit = -1; SaveNow(); }
        if (u.TargetX >= 0) G.Text($"Heading to a tile {Hex.Distance(u.X, u.Y, u.TargetX, u.TargetY, s.W)} away.", panel.X + 10, by - 20, 14, Gfx.Muted);
    }

    /// <summary>Units still waiting for orders this turn.</summary>
    IEnumerable<UnitState> IdleUnits(Act1State s) =>
        s.UnitsOf(s.Player.Id).Where(u => u.Moves > 0 && !u.Fortified && !u.Skipped && u.TargetX < 0 && !u.Attacked);

    void SelectNextUnit(Act1State s)
    {
        var next = IdleUnits(s).FirstOrDefault(u => u.Id != _selUnit);
        _selUnit = next?.Id ?? -1;
        if (next != null) { CenterOn(next.X, next.Y); _selCity = -1; }
    }

    void UnitKeys(Act1State s)
    {
        if (SelectedUnit(s) is not { } u || _overlay1 != "") return;
        if (Raylib.IsKeyPressed(KeyboardKey.Space)) { u.Skipped = true; SelectNextUnit(s); }
        if (Raylib.IsKeyPressed(KeyboardKey.F)) { u.Fortified = true; u.TargetX = u.TargetY = -1; SelectNextUnit(s); }
    }

    // ---------------------------------------------------------------- the tile card

    /// <summary>Civ VI's tile tooltip: what the tile is, the resource on it, its yields as icons, and who is there.</summary>
    void TileCard(Act1State s, Content c, int x, int y)
    {
        var tile = s.At(x, y);
        var me = s.Player;
        var lines = new List<(string text, Color col)>();
        string title;
        Texture2D? res = null;
        string? resLine = null;
        Yields? yields = null;
        if (!tile.Revealed)
        {
            title = "Unexplored";
            lines.Add(("Explore with units; your borders and cities reveal the land. Cartography charts the seas, Flight the whole world.", Gfx.Muted));
        }
        else
        {
            var t = Defs.Terrains[tile.Terrain];
            title = tile.Natural != null ? c.Name(tile.Natural) : c.Text(t.NameLoc) + (tile.Feature >= 0 ? ", " + c.Text(Defs.Features[tile.Feature].NameLoc) : "");
            if (tile.Natural != null) lines.Add(($"Natural wonder. In Act 2 it becomes a rare star system: {Defs.Modifier[c.NaturalDef(tile.Natural).Modifier].EffectText}.", Gfx.Gold));
            if (tile.Resource is { } r && c.MapResource.TryGetValue(r, out var rd))
            {
                if (Military.CanSee(c, me, r))
                {
                    res = ResourceIcon(r);
                    string cls = rd.Class switch { "strategic" => "Strategic resource", "luxury" => "Luxury resource", _ => "Bonus resource" };
                    var users = c.Units.Where(u => u.Resource == r).Select(u => c.Text(u.NameLoc)).ToList();
                    resLine = $"{c.Text(rd.NameLoc)} · {cls}" + (users.Count > 0 ? $"\nNeeded for {string.Join(", ", users)}." : "");
                }
            }
            if (Act1.Workable(s, x, y)) yields = Act1.TileYield(s, x, y, s.OwnerAt(x, y) >= 0 ? null : me);
            else lines.Add(("Can't be worked.", Gfx.Muted));
            int mc = Military.EnterCost(s, me, x, y);
            lines.Add((mc < 0 ? "Impassable" + (!t.Land ? " (embarking needs Shipbuilding; open ocean, Cartography)" : "") : $"Movement cost {mc}" + (t.Id.EndsWith("_HILLS") || tile.Feature >= 0 && Defs.Features[tile.Feature].Id is "FEATURE_FOREST" or "FEATURE_JUNGLE" ? $" · +{Rules.TerrainDefenseBonus} defence" : ""), Gfx.Muted));
            if (tile.City >= 0 && s.City(tile.City) is { } owner)
            {
                var oc = s.Civs[owner.Owner];
                lines.Add(($"{owner.Name} · {Act1.CivName(c, oc)}" + (s.AtWar(oc.Id, me.Id) ? " · at war" : ""), s.AtWar(oc.Id, me.Id) ? Gfx.Bad : Gfx.Ink));
            }
            if (s.CityAt(x, y) is { } city)
                lines.Add(($"City: strength {Math.Round(Military.CityStrength(s, c, city))}, {Math.Ceiling(city.Hp)}/{Rules.CityMaxHp} HP", Gfx.Ink));
            if (s.UnitAt(x, y) is { } u && (u.Owner == me.Id || Act1.Visible(s, x, y)))
            {
                var ui = c.Unit[u.Type];
                lines.Add(($"{Act1.CivAdjective(c, s.Civs[u.Owner])} {UnitName(c, u)} · strength {ui.Combat}" + (ui.IsRanged ? $", ranged {ui.Ranged}" : "") + $" · {Math.Ceiling(u.Hp)} HP" + (u.Fortified ? " · fortified" : ""),
                           u.Owner == me.Id ? Gfx.Good : s.AtWar(u.Owner, me.Id) ? Gfx.Bad : Gfx.Ink));
            }
            if (SelectedUnit(s) is { } sel && Military.CanAttack(s, c, sel, x, y))
            {
                var o = Military.Preview(s, c, sel, x, y);
                string outcome = o.Captures ? "captures the city" : o.Kills ? "destroys it" : $"deals about {Math.Round(o.ToDefender)} damage";
                lines.Add(($"Attack: {outcome}" + (o.ToAttacker > 0 ? $", takes about {Math.Round(o.ToAttacker)}" : "") + ".", Gfx.Gold));
            }
            if (_settlerMode) lines.Add((Act1.CanFoundCity(s, me, x, y) ? "Click to found a city here." : "You can't found a city here.", _settlerMode && Act1.CanFoundCity(s, me, x, y) ? Gfx.Good : Gfx.Bad));
        }
        // Layout
        float w = 330, pad = 12;
        var wrapped = lines.SelectMany(l => G.Wrap(l.text, 15, w - pad * 2).Select(t => (t, l.col))).ToList();
        var resWrapped = resLine != null ? G.Wrap(resLine, 15, w - pad * 2 - 40) : new List<string>();
        float h = pad + 26 + (yields != null ? 40 : 0) + (res != null ? Math.Max(36, resWrapped.Count * 19) + 6 : 0) + wrapped.Count * 19 + pad;
        var m = Gfx.Mouse;
        var card = new Rectangle(m.X + 22, m.Y + 18, w, h);
        if (card.X + w > Gfx.W - 8) card.X = m.X - w - 16;
        if (card.Y + h > Gfx.H - 8) card.Y = Gfx.H - h - 8;
        Raylib.DrawRectangleRounded(new Rectangle(card.X + 3, card.Y + 4, w, h), 0.06f, 6, new Color(0, 0, 0, 120));
        Raylib.DrawRectangleRounded(card, 0.06f, 6, new Color(14, 20, 34, 240));
        Raylib.DrawRectangleRoundedLinesEx(card, 0.06f, 6, 1.2f, G.PanelEdge);
        float cy = card.Y + pad;
        G.Text(G.Clip(title, 19, w - pad * 2), card.X + pad, cy, 19, Gfx.Gold, true);
        cy += 28;
        if (yields is { } yv)
        {
            float cx = card.X + pad;
            foreach (var (id, v) in new[] { ("food", yv.Food), ("production", yv.Production), ("gold", yv.Gold), ("science", yv.Science) })
            {
                Gfx.DrawFit(YieldIcon(id), new Rectangle(cx, cy, 28, 28), v > 0 ? Color.White : new Color(255, 255, 255, 70));
                G.Text(Gfx.Num(v), cx + 31, cy + 4, 20, v > 0 ? Gfx.Ink : Gfx.Muted);
                cx += 74;
            }
            cy += 40;
        }
        if (res != null)
        {
            Gfx.DrawFit(res, new Rectangle(card.X + pad, cy, 34, 34), Color.White);
            for (int i = 0; i < resWrapped.Count; i++) G.Text(resWrapped[i], card.X + pad + 42, cy + 1 + i * 19, 15, i == 0 ? Gfx.Ink : Gfx.Muted);
            cy += Math.Max(36, resWrapped.Count * 19) + 6;
        }
        foreach (var (t, col) in wrapped) { G.Text(t, card.X + pad, cy, 15, col); cy += 19; }
    }
}
