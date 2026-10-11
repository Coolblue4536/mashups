using System.Numerics;
using Raylib_cs;

namespace StellarAscension;

/// <summary>Act 2 close-ups: the battle window (both fleets, their hulls, the fight month by month, and the choice to
/// fight on or make an emergency FTL jump) and the system view (the star and its planets in orbit), Stellaris style.</summary>
public sealed partial class App
{
    int _battleSel = -1;

    /// <summary>An empire's picture: its leader or species portrait, or (when the install has none) an emblem in its colours.</summary>
    void EmpireBadge(Empire e, Rectangle r, Color tint)
    {
        if (EmpirePic(e) is { } pic) { Gfx.DrawFit(pic, r, tint); return; }
        var cpos = new Vector2(r.X + r.Width / 2, r.Y + r.Height / 2);
        float rad = MathF.Min(r.Width, r.Height) / 2;
        Raylib.DrawCircleV(cpos, rad, new Color((byte)(e.ColorR * 0.45f), (byte)(e.ColorG * 0.45f), (byte)(e.ColorB * 0.45f), tint.A));
        Raylib.DrawCircleLinesV(cpos, rad, EColor(e, tint.A));
        Raylib.DrawCircleLinesV(cpos, rad * 0.82f, EColor(e, tint.A / 2));
        G.TextCentered(e.Name.Length > 0 ? e.Name[..1] : "?", r, rad * 1.1f, EText(e), true);
    }

    ShipRenderer? _shipArt;
    ShipRenderer? ShipArt => _shipArt ??= _stellarisPath != null && GameLocator.IsStellaris(_stellarisPath) ? new ShipRenderer(_stellarisPath) : null;

    /// <summary>The empire's own Stellaris ship, rendered in its colours (null for a frame or two while it is baked).</summary>
    Texture2D? ShipSprite(string id, Empire e) =>
        ShipArt is { } art ? art.Sprite(art.Culture(e.ShipCulture, e.CivType + e.Name), id, new Color((byte)e.ColorR, (byte)e.ColorG, (byte)e.ColorB, (byte)255)) : null;

    /// <summary>A ship picture fitted in r, bow to the right: the rendered model, else Stellaris's painting.</summary>
    void DrawShipIn(string id, Empire e, Rectangle r)
    {
        if (ShipSprite(id, e) is { } t)
        {
            float len = Math.Min(r.Width, r.Height * t.Width / t.Height);
            ShipRenderer.Draw(t, new Vector2(r.X + r.Width / 2, r.Y + r.Height / 2), len, true, Color.White);
        }
        else Gfx.DrawFit(ShipPic(id), r, Color.White);
    }

    /// <summary>How long a ship of this class is drawn in a scene, relative to a corvette.</summary>
    static float ShipScale(string id) => id switch { "battleship" => 2.1f, "cruiser" => 1.7f, "destroyer" => 1.3f, "corvette" => 1f, _ => 1.1f };

    /// <summary>A ship in a scene: the rendered Stellaris model with its engines glowing (or, until it is baked, the class
    /// silhouette in the empire's colour). `size` is a corvette's length in pixels.</summary>
    void ShipGlyph(string shipId, Empire e, Vector2 at, float size, bool facingRight)
    {
        if (ShipSprite(shipId, e) is { } spr)
        {
            float len = size * ShipScale(shipId) * 1.6f;
            Raylib.BeginBlendMode(BlendMode.Additive);
            float flick = 0.85f + 0.15f * MathF.Sin((float)Raylib.GetTime() * 13 + at.X);
            Raylib.DrawCircleGradient(at + new Vector2(facingRight ? -len * 0.4f : len * 0.4f, 0), len * 0.16f, new Color((byte)120, (byte)200, (byte)255, (byte)(110 * flick)), new Color(0, 0, 0, 0));
            Raylib.EndBlendMode();
            ShipRenderer.Draw(spr, at, len, facingRight, Color.White);
            return;
        }
        Raylib.BeginBlendMode(BlendMode.Additive);
        Raylib.DrawCircleGradient(at + new Vector2(facingRight ? -size * 0.45f : size * 0.45f, 0), size * 0.5f, new Color(120, 200, 255, 120), new Color(0, 0, 0, 0));
        Raylib.EndBlendMode();
        Raylib.DrawCircleV(at, size * 0.52f, new Color(6, 14, 20, 200));
        Raylib.DrawCircleLinesV(at, size * 0.52f, EColor(e, 220));
        if (St("st_class_" + shipId) is { } t)
            Raylib.DrawTexturePro(t, new Rectangle(0, 0, t.Width, t.Height), new Rectangle(at.X, at.Y, size * 0.75f, size * 0.75f), new Vector2(size * 0.375f, size * 0.375f), facingRight ? 90 : -90, EText(e));
    }

    // ---------------------------------------------------------------- battle window

    void BattleWindow(Act2State s, Content c)
    {
        var me = s.Player;
        var battle = s.Battles.LastOrDefault(b => b.System == _battleSel) ?? s.Battles.LastOrDefault(b => b.Sides.Contains(me.Id));
        if (battle == null) { _overlay2 = ""; return; }
        var sys = s.Sys(battle.System);
        var r = Overlay2(battle.Over ? $"Battle of {sys.Name}: {battle.Result}" : $"Battle of {sys.Name}");
        float now = (float)Raylib.GetTime();
        // The scene: the star behind, two fleets facing each other, weapons fire between them.
        var scene = new Rectangle(r.X + 16, r.Y + 62, r.Width - 32, 360);
        Raylib.DrawRectangleRounded(scene, 0.04f, 6, new Color(4, 8, 18, 255));
        Raylib.BeginScissorMode((int)scene.X, (int)scene.Y, (int)scene.Width, (int)scene.Height);
        var rng = new Rng(11);
        for (int i = 0; i < 160; i++) Raylib.DrawPixelV(new Vector2(scene.X + (float)rng.Double() * scene.Width, scene.Y + (float)rng.Double() * scene.Height), new Color(200, 210, 255, 90 + rng.Int(120)));
        var sc = Defs.StarClass.TryGetValue(sys.Star, out var scd) ? scd : Defs.StarClasses[0];
        Raylib.BeginBlendMode(BlendMode.Additive);
        if (St(sc.Art) is { } star) Gfx.DrawFit(star, new Rectangle(scene.X + scene.Width / 2 - 160, scene.Y - 120, 320, 320), new Color(255, 255, 255, 160));
        Raylib.EndBlendMode();
        var mineSide = battle.Sides.Where(x => x == me.Id || s.Allied(x, me.Id)).ToList();
        if (mineSide.Count == 0) mineSide.Add(battle.Sides[0]);
        var theirSide = battle.Sides.Where(x => !mineSide.Contains(x)).ToList();
        var spots = new Dictionary<bool, List<Vector2>> { [true] = new(), [false] = new() };
        void Side(List<int> owners, bool left)
        {
            var fleets = s.Active.Where(f => f.System == battle.System && owners.Contains(f.Owner) && Act2.IsMilitary(f)).ToList();
            var ships = fleets.SelectMany(f => f.Ships.SelectMany(kv => Enumerable.Repeat((id: kv.Key, owner: f.Owner), kv.Value))).OrderByDescending(k => Defs.Ship[k.id].Power).Take(12).ToList();
            // Formation: the big ships hold the back line, the small ones screen in front; three rows, staggered.
            float half = scene.Width / 2;
            for (int i = 0; i < ships.Count; i++)
            {
                int col = i / 3, row = i % 3;
                float bob = MathF.Sin(now * 1.3f + i * 1.7f) * 3;
                float fromEdge = 95 + col * Math.Min(150, (half - 190) / 3.2f);
                float sx = left ? scene.X + fromEdge : scene.X + scene.Width - fromEdge;
                float sy = scene.Y + 70 + row * 105 + (col % 2) * 34 + bob;
                spots[left].Add(new Vector2(sx, sy));
                ShipGlyph(ships[i].id, s.Empires[ships[i].owner], new Vector2(sx, sy), 76, left);
            }
            if (owners.Contains(sys.Owner) && sys.Owner >= 0 && St("st_starbase_icon") is { } sbi)
                Gfx.DrawFit(sbi, new Rectangle(left ? scene.X + 14 : scene.X + scene.Width - 54, scene.Y + scene.Height - 54, 40, 40), EText(s.Empires[sys.Owner]));
        }
        Side(mineSide, true);
        Side(theirSide, false);
        if (!battle.Over)
        {
            // Lasers and missiles crossing between the lines.
            var lr = new Rng((ulong)(now * 8));
            for (int i = 0; i < 10 && spots[true].Count > 0 && spots[false].Count > 0; i++)
            {
                float t = (now * 1.7f + i * 0.37f) % 1f;
                var a = spots[true][lr.Int(spots[true].Count)] + new Vector2(30, 0);
                var b = spots[false][lr.Int(spots[false].Count)] - new Vector2(30, 0);
                if (i % 2 == 1) (a, b) = (b, a);
                var head = a + (b - a) * t;
                var tail = a + (b - a) * MathF.Max(0, t - 0.12f);
                Raylib.BeginBlendMode(BlendMode.Additive);
                Raylib.DrawLineEx(tail, head, 2.5f, i % 3 == 0 ? new Color(255, 120, 80, 220) : new Color(120, 220, 255, 220));
                if (t > 0.9f) Raylib.DrawCircleGradient(b, 14, new Color(255, 200, 120, 200), new Color(0, 0, 0, 0));
                Raylib.EndBlendMode();
            }
        }
        Raylib.EndScissorMode();
        // Each side's strength.
        void Stats(List<int> owners, float x, bool alignRight)
        {
            var fleets = s.Active.Where(f => f.System == battle.System && owners.Contains(f.Owner) && Act2.IsMilitary(f)).ToList();
            double power = fleets.Sum(f => Act2.Power(s, s.Empires[f.Owner], f));
            double hull = fleets.Sum(f => f.Ships.Sum(kv => Defs.Ship[kv.Key].Hull * kv.Value) * (1 + Act2.Stat(s, s.Empires[f.Owner], "ship_hull_mult")));
            double dmg = fleets.Sum(f => f.Damage);
            float y = scene.Y + scene.Height + 10;
            var names = string.Join(", ", owners.Select(o => s.Empires[o].Name));
            var lead = s.Empires[owners[0]];
            EmpireBadge(lead, new Rectangle(alignRight ? x - 56 : x, y, 56, 56), Color.White);
            float tx = alignRight ? x - 66 : x + 66;
            void T(string text, float yy, float size, Color col) { if (alignRight) G.TextRight(text, tx, yy, size, col); else G.Text(text, tx, yy, size, col); }
            T(G.Clip(names, 18, 360), y, 18, EText(lead));
            int count = fleets.Sum(f => f.Ships.Values.Sum());
            T($"{count} ships · power {Gfx.Num(power)} · lost {battle.ShipsLost.Where(kv => owners.Contains(kv.Key)).Sum(kv => kv.Value)}", y + 24, 15, Gfx.Muted);
            var bar = new Rectangle(alignRight ? x - 360 : x, y + 62, 360, 10);
            Raylib.DrawRectangleRounded(bar, 0.5f, 4, new Color(30, 30, 30, 230));
            float frac = hull > 0 ? (float)Math.Clamp(1 - dmg / hull, 0, 1) : 0;
            Raylib.DrawRectangleRounded(new Rectangle(alignRight ? bar.X + bar.Width * (1 - frac) : bar.X, bar.Y, bar.Width * frac, bar.Height), 0.5f, 4, frac > 0.5f ? Gfx.Good : frac > 0.25f ? Gfx.Rgb(230, 190, 60) : Gfx.Bad);
            T($"hull {Gfx.Num(Math.Max(0, hull - dmg))}/{Gfx.Num(hull)}", y + 76, 13, Gfx.Muted);
        }
        if (mineSide.Count > 0) Stats(mineSide, r.X + 24, false);
        if (theirSide.Count > 0) Stats(theirSide, r.X + r.Width - 24, true);
        G.TextCentered(battle.Over ? $"Over after {battle.Months} months" : $"Month {battle.Months}", new Rectangle(r.X, scene.Y + scene.Height + 14, r.Width, 22), 18, Gfx.Gold, true);
        // The fight, month by month.
        float ly = scene.Y + scene.Height + 110;
        G.Text("The fight so far", r.X + 24, ly, 17, Gfx.Gold); ly += 24;
        foreach (var line in battle.Log.TakeLast(Math.Max(1, (int)((r.Y + r.Height - 70 - ly) / 19))).Reverse())
        { G.Text(G.Clip(line, 14, r.Width - 48), r.X + 24, ly, 14, Gfx.Ink); ly += 19; }
        // Choices
        var mine = s.Active.Where(f => f.System == battle.System && f.Owner == me.Id && Act2.IsMilitary(f)).ToList();
        float by = r.Y + r.Height - 58;
        if (!battle.Over && G.Button(new Rectangle(r.X + r.Width / 2 - 330, by, 200, 44), "Fight on", size: 18, tip: "Keep fighting next turn.")) _overlay2 = "";
        if (!battle.Over && G.Button(new Rectangle(r.X + r.Width / 2 - 110, by, 220, 44), "Emergency FTL", mine.Count > 0, size: 18,
                tip: $"Your fleets here jump out blind: each loses about {Rules.EmergencyFtlLoss * 100:0}% of its ships and is missing for about {Rules.EmergencyFtlMonths / Act2.MonthsPerTurn} turns, then turns up at your capital."))
        {
            foreach (var f in mine) Act2.EmergencyFtl(s, c, f);
            _overlay2 = "";
            SaveNow();
        }
        if (G.Button(new Rectangle(r.X + r.Width / 2 + 130, by, 200, 44), "View system", size: 18)) { _selSystem = battle.System; _overlay2 = "system"; }
    }

    // ---------------------------------------------------------------- system view

    void SystemView(Act2State s, Content c)
    {
        var me = s.Player;
        if (_selSystem < 0) { _overlay2 = ""; return; }
        var sys = s.Sys(_selSystem);
        float now = (float)Raylib.GetTime();
        G.Block(new Rectangle(0, 0, Gfx.W, Gfx.H));
        Raylib.DrawRectangleGradientV(0, 0, (int)Gfx.W, (int)Gfx.H, new Color(2, 4, 12, 255), new Color(8, 4, 18, 255));
        var rng = new Rng((ulong)(sys.Id + 3));
        for (int i = 0; i < 500; i++)
        {
            var sp = new Vector2((float)rng.Double() * Gfx.W, (float)rng.Double() * Gfx.H);
            Raylib.DrawPixelV(sp, new Color(200, 210, 255, 60 + rng.Int(150)));
        }
        if (St("st_map_nebula") is { } neb)
        {
            Raylib.BeginBlendMode(BlendMode.Additive);
            Raylib.DrawTexturePro(neb, new Rectangle(0, 0, neb.Width, neb.Height), new Rectangle(Gfx.W * 0.7f, Gfx.H * 0.3f, Gfx.H * 1.2f, Gfx.H * 1.2f), new Vector2(Gfx.H * 0.6f, Gfx.H * 0.6f), now * 0.5f, new Color(120, 90, 200, 60));
            Raylib.EndBlendMode();
        }
        var centre = new Vector2(Gfx.W / 2 + 120, Gfx.H / 2 + 20);
        var scd = Defs.StarClass.TryGetValue(sys.Star, out var sc) ? sc : Defs.StarClasses[0];
        var scol = Gfx.Hex(scd.Color);
        Raylib.BeginBlendMode(BlendMode.Additive);
        Raylib.DrawCircleGradient(centre, 220, new Color(scol.R, scol.G, scol.B, (byte)90), new Color(0, 0, 0, 0));
        if (St(scd.Art) is { } star) Gfx.DrawFit(star, new Rectangle(centre.X - 140, centre.Y - 140, 280, 280), Color.White);
        Raylib.EndBlendMode();
        // Planets on their orbits (slowly turning; the inner ones faster).
        float maxR = MathF.Min(Gfx.H / 2 - 70, Gfx.W / 2 - 200);
        int n = Math.Max(1, sys.Planets.Count);
        bool known = sys.SurveyedBy.Contains(me.Id);
        for (int i = 0; i < sys.Planets.Count; i++)
        {
            var pl = sys.Planets[i];
            float orbit = 120 + (maxR - 120) * (n == 1 ? 0.5f : i / (float)(n - 1));
            Raylib.DrawCircleLinesV(centre, orbit, new Color(120, 170, 220, 60));
            float ang = now * (0.08f / (1 + i * 0.6f)) + i * 2.1f + sys.Id;
            var pp = centre + new Vector2(MathF.Cos(ang), MathF.Sin(ang) * 0.82f) * orbit;
            float size = 26 + Math.Min(pl.Size, 25) * 1.6f;
            if (!known) { Raylib.DrawCircleV(pp, size * 0.35f, new Color(90, 100, 120, 200)); continue; }
            Gfx.DrawFit(PlanetPic(pl.Class), new Rectangle(pp.X - size / 2, pp.Y - size / 2, size, size), Color.White);
            if (pl.Owner >= 0) Raylib.DrawCircleLinesV(pp, size / 2 + 4, EColor(s.Empires[pl.Owner]));
            if (pl.Exotic != null && Defs.ExoticSite.TryGetValue(pl.Exotic, out var xd))
            {
                Raylib.BeginBlendMode(BlendMode.Additive);
                Raylib.DrawCircleGradient(pp, size, new Color(180, 120, 255, (int)(60 + 40 * MathF.Sin(now * 2))), new Color(0, 0, 0, 0));
                Raylib.EndBlendMode();
                Gfx.DrawFit(St(xd.Icon), new Rectangle(pp.X + size * 0.25f, pp.Y - size * 0.6f, 28, 28), Color.White);
            }
            string label = pl.Name + (pl.Homeworld ? " (homeworld)" : "");
            float tw = G.Measure(label, 15);
            Raylib.DrawRectangleRounded(new Rectangle(pp.X - tw / 2 - 6, pp.Y + size / 2 + 4, tw + 12, 20), 0.5f, 4, new Color(4, 10, 14, 180));
            G.Text(label, pp.X - tw / 2, pp.Y + size / 2 + 6, 15, pl.Owner >= 0 ? EText(s.Empires[pl.Owner]) : Act2.Habitable(pl) ? Gfx.Good : Gfx.Muted);
            var hit = new Rectangle(pp.X - size / 2, pp.Y - size / 2, size, size);
            if (G.Hover(hit))
            {
                string info = $"{StName(pl.Class, pl.Class)} · size {pl.Size}" + (pl.Owner >= 0 ? $" · {s.Empires[pl.Owner].Name}, {Math.Floor(pl.Pops)} pops" : Act2.Habitable(pl) ? " · habitable" : "");
                if (pl.Exotic != null && Defs.ExoticSite.TryGetValue(pl.Exotic, out var x2)) info += $"\n{x2.Name}: {Defs.Modifier[x2.Act2Modifier].EffectText}; opens {Act2.Name(c, x2.UnlockTech, x2.UnlockTech)}.";
                G.Tip(info);
                if (pl.Owner == me.Id && G.Clicked(hit)) { _selPlanet = pl.Id; _overlay2 = ""; }
            }
        }
        // Ships in the system, near the star; the starbase on its own orbit.
        var here = s.Active.Where(f => f.System == sys.Id && f.Path.Count == 0).ToList();
        for (int i = 0; i < here.Count; i++)
        {
            var f = here[i];
            float ang = -1.2f + i * 0.35f + now * 0.05f;
            var fp = centre + new Vector2(MathF.Cos(ang), MathF.Sin(ang) * 0.82f) * 95;
            var top = f.Ships.Keys.OrderByDescending(k => Defs.Ship[k].Power).First();
            ShipGlyph(top, s.Empires[f.Owner], fp, 50, MathF.Cos(ang) < 0);
            G.Text($"{f.Ships.Values.Sum()}", fp.X + 24, fp.Y - 28, 13, EText(s.Empires[f.Owner]));
        }
        if (sys.Owner >= 0 && St("st_starbase_icon") is { } sb)
        {
            var sp = centre + new Vector2(0, -70);
            Gfx.DrawFit(sb, new Rectangle(sp.X - 16, sp.Y - 16, 32, 32), EText(s.Empires[sys.Owner]));
        }
        // Header
        var head = new Rectangle(20, 20, 420, 140);
        G.Panel(head);
        G.Text(sys.Name, head.X + 16, head.Y + 12, 30, Gfx.Gold, true);
        G.Text($"{StName(scd.NameLoc, sys.Star)}" + (sys.Owner >= 0 ? $" · {s.Empires[sys.Owner].Name}" : " · unclaimed"), head.X + 16, head.Y + 52, 16, sys.Owner >= 0 ? EText(s.Empires[sys.Owner]) : Gfx.Muted);
        G.Text(known ? $"{sys.Planets.Count} planets · {sys.Planets.Count(Act2.Habitable)} habitable · {here.Count} fleets here" : "Not surveyed yet", head.X + 16, head.Y + 76, 15, Gfx.Ink);
        G.Text("Esc or Back to return to the galaxy", head.X + 16, head.Y + 106, 13, Gfx.Muted);
        if (G.Button(new Rectangle(Gfx.W - 150, 20, 130, 44), "Back", size: 18) || Raylib.IsKeyPressed(KeyboardKey.Escape)) _overlay2 = "";
    }
}
