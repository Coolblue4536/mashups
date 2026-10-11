using System.Numerics;
using Raylib_cs;

namespace StellarAscension;

/// <summary>Act 1 battle visuals: units glide between tiles and lunge when they strike, damage numbers float up,
/// a Civ VI-style result banner shows the fight, and attacks are confirmed with a preview of the odds.</summary>
public sealed partial class App
{
    readonly Dictionary<int, Vector2> _anim = new();
    readonly Dictionary<int, (Vector2 target, float t0)> _lunge = new();
    readonly List<(Vector2 world, string text, Color col, float t0)> _floaters = new();
    (int unit, int x, int y)? _attackPrompt;
    int _seenBattles;
    BattleNote? _banner;
    float _bannerAt;

    /// <summary>The unit's drawn position (world): it eases toward its tile, so moves are seen, not jumps.</summary>
    Vector2 AnimPos(UnitState u, Vector2 world)
    {
        if (!_anim.TryGetValue(u.Id, out var cur) || Vector2.Distance(cur, world) > MapPixelW / 3) cur = world;
        float k = 1 - MathF.Exp(-Raylib.GetFrameTime() * 9);
        cur += (world - cur) * k;
        if (Vector2.Distance(cur, world) < 0.5f) cur = world;
        _anim[u.Id] = cur;
        return cur;
    }

    /// <summary>Picks up fights that happened since the last frame (yours, or ones you can see).</summary>
    void WatchBattles(Act1State s, Content c)
    {
        if (_seenBattles > s.Battles.Count) _seenBattles = 0;
        float now = (float)Raylib.GetTime();
        for (int i = _seenBattles; i < s.Battles.Count; i++)
        {
            var n = s.Battles[i];
            bool mine = n.AttackerCiv == s.Player.Id || n.DefenderCiv == s.Player.Id;
            if (!mine && !Act1.Visible(s, n.X, n.Y)) continue;
            _floaters.Add((TileCenter(n.X, n.Y) + new Vector2(0, -10), n.Killed ? "Destroyed" : n.Captured ? "Captured!" : $"-{Math.Round(n.ToDefender)}", n.Captured ? Gfx.Gold : Gfx.Bad, now));
            if (n.ToAttacker >= 1) _floaters.Add((TileCenter(n.FromX, n.FromY) + new Vector2(0, -10), $"-{Math.Round(n.ToAttacker)}", new Color(255, 170, 90, 255), now + 0.15f));
            if (s.Units.FirstOrDefault(u => u.Owner == n.AttackerCiv && (u.X == n.FromX && u.Y == n.FromY || u.X == n.X && u.Y == n.Y)) is { } a)
                _lunge[a.Id] = (TileCenter(n.X, n.Y), now);
            if (mine) { _banner = n; _bannerAt = now; }
        }
        _seenBattles = s.Battles.Count;
    }

    void DrawFloaters()
    {
        float now = (float)Raylib.GetTime();
        _floaters.RemoveAll(f => now - f.t0 > 1.8f);
        foreach (var (w, text, col, t0) in _floaters)
        {
            float t = now - t0;
            if (t < 0) continue;
            var p = ToScreen(w) - new Vector2(0, t * 40);
            byte a = (byte)Math.Clamp(255 * (1.8f - t) / 0.8f, 0, 255);
            float fs = 22 * Math.Clamp(_zoom1, 0.8f, 1.4f);
            float tw = G.Measure(text, fs, true);
            G.Text(text, p.X - tw / 2 + 2, p.Y + 2, fs, new Color((byte)0, (byte)0, (byte)0, a), true);
            G.Text(text, p.X - tw / 2, p.Y, fs, new Color(col.R, col.G, col.B, a), true);
        }
    }

    Texture2D? NotePicture(string type, int civ, Act1State s) =>
        type == "city" ? CivSymbol(s.Civs[civ].CivType) : UnitPortrait(type);

    /// <summary>Civ VI's combat result: both sides' pictures, the damage each way, and what happened.</summary>
    void BattleBanner(Act1State s, Content c)
    {
        if (_banner is not { } n) return;
        float age = (float)Raylib.GetTime() - _bannerAt;
        if (age > 4) { _banner = null; return; }
        byte a = (byte)Math.Clamp(255 * Math.Min(age * 4, (4 - age) * 2), 0, 240);
        if (_overlay1 != "") return;
        var r = new Rectangle(Math.Max(Gfx.W / 2 - 330, 490), Gfx.H - 210, 660, 120);
        if (r.X + r.Width > Gfx.W - 410) r.Y = Gfx.H - 340;
        Raylib.DrawRectangleRounded(r, 0.2f, 6, new Color((byte)10, (byte)14, (byte)26, a));
        Raylib.DrawRectangleRoundedLinesEx(r, 0.2f, 6, 1.5f, new Color(G.PanelEdge.R, G.PanelEdge.G, G.PanelEdge.B, a));
        var white = new Color((byte)255, (byte)255, (byte)255, a);
        var ac = s.Civs[n.AttackerCiv]; var dc = s.Civs[n.DefenderCiv];
        Raylib.DrawCircleV(new Vector2(r.X + 62, r.Y + 60), 46, new Color(UnitColor(c, ac).R, UnitColor(c, ac).G, UnitColor(c, ac).B, a));
        Gfx.DrawFit(UnitPortrait(n.AttackerType), new Rectangle(r.X + 14, r.Y + 12, 96, 96), white);
        Raylib.DrawCircleV(new Vector2(r.X + r.Width - 62, r.Y + 60), 46, new Color(UnitColor(c, dc).R, UnitColor(c, dc).G, UnitColor(c, dc).B, a));
        Gfx.DrawFit(NotePicture(n.DefenderType, n.DefenderCiv, s), new Rectangle(r.X + r.Width - 110, r.Y + 12, 96, 96), white);
        bool youAttacked = n.AttackerCiv == s.Player.Id;
        string head = n.Captured ? "City captured!" : n.Killed ? (youAttacked ? "Victory" : "Your unit was destroyed")
                    : n.AttackerHp <= 0 ? (youAttacked ? "Your attacker fell" : "Their attacker fell") : "The battle goes on";
        var headCol = n.Captured || n.Killed && youAttacked || n.AttackerHp <= 0 && !youAttacked ? Gfx.Gold : n.Killed || n.AttackerHp <= 0 ? Gfx.Bad : Gfx.Ink;
        G.TextCentered(head, new Rectangle(r.X, r.Y + 10, r.Width, 30), 24, new Color(headCol.R, headCol.G, headCol.B, a), true);
        G.TextCentered($"{c.Text(c.Unit.TryGetValue(n.AttackerType, out var au) ? au.NameLoc : n.AttackerType)}   vs   {n.DefenderName}", new Rectangle(r.X, r.Y + 44, r.Width, 22), 17, white);
        G.TextCentered($"dealt {Math.Round(n.ToDefender)}  ·  took {Math.Round(n.ToAttacker)}", new Rectangle(r.X, r.Y + 70, r.Width, 22), 17, new Color(Gfx.Muted.R, Gfx.Muted.G, Gfx.Muted.B, a));
        // Health after the fight
        void Bar(float x, double hp, double max)
        {
            var b = new Rectangle(x, r.Y + r.Height - 14, 96, 6);
            Raylib.DrawRectangleRec(b, new Color((byte)30, (byte)30, (byte)30, a));
            Raylib.DrawRectangleRec(new Rectangle(b.X, b.Y, b.Width * (float)Math.Clamp(hp / max, 0, 1), b.Height), new Color(Gfx.Good.R, Gfx.Good.G, Gfx.Good.B, a));
        }
        Bar(r.X + 14, n.AttackerHp, 100);
        Bar(r.X + r.Width - 110, n.DefenderHp, n.DefenderType == "city" ? Rules.CityMaxHp : 100);
    }

    /// <summary>Before an attack: both sides, their strength, and the expected result. Attack or think again.</summary>
    void AttackPrompt(Act1State s, Content c)
    {
        if (_attackPrompt is not { } ap) return;
        var u = s.Unit(ap.unit);
        if (u == null || !Military.CanAttack(s, c, u, ap.x, ap.y)) { _attackPrompt = null; return; }
        Raylib.DrawRectangle(0, 0, (int)Gfx.W, (int)Gfx.H, new Color(0, 0, 0, 90));
        G.Block(new Rectangle(0, 0, Gfx.W, Gfx.H));
        var r = new Rectangle(Gfx.W / 2 - 300, Gfx.H / 2 - 170, 600, 340);
        G.Panel(r);
        var o = Military.Preview(s, c, u, ap.x, ap.y);
        var info = c.Unit[u.Type];
        G.TextCentered($"Attack {o.Target}?", new Rectangle(r.X, r.Y + 14, r.Width, 34), 28, Gfx.Gold, true);
        // Attacker
        Raylib.DrawCircleV(new Vector2(r.X + 110, r.Y + 130), 64, UnitColor(c, s.Player));
        Gfx.DrawFit(UnitPortrait(u.Type), new Rectangle(r.X + 46, r.Y + 66, 128, 128), Color.White);
        G.TextCentered(UnitName(c, u), new Rectangle(r.X + 20, r.Y + 200, 180, 22), 17, Gfx.Ink);
        G.TextCentered($"{(info.IsRanged ? "Ranged " + info.Ranged : "Strength " + info.Combat)} · {Math.Ceiling(u.Hp)} HP", new Rectangle(r.X + 20, r.Y + 222, 180, 20), 15, Gfx.Muted);
        // Defender
        var d = s.MilitaryAt(ap.x, ap.y);
        var city = s.CityAt(ap.x, ap.y);
        var dciv = s.Civs[city?.Owner ?? d!.Owner];
        Raylib.DrawCircleV(new Vector2(r.X + r.Width - 110, r.Y + 130), 64, UnitColor(c, dciv));
        Gfx.DrawFit(city != null ? CivSymbol(dciv.CivType) : UnitPortrait(d!.Type), new Rectangle(r.X + r.Width - 174, r.Y + 66, 128, 128), Color.White);
        G.TextCentered(city != null ? city.Name : UnitName(c, d!), new Rectangle(r.X + r.Width - 200, r.Y + 200, 180, 22), 17, Gfx.Ink);
        G.TextCentered(city != null ? $"Strength {Math.Round(Military.CityStrength(s, c, city))} · {Math.Ceiling(city.Hp)} HP" : $"Strength {c.Unit[d!.Type].Combat} · {Math.Ceiling(d.Hp)} HP",
            new Rectangle(r.X + r.Width - 200, r.Y + 222, 180, 20), 15, Gfx.Muted);
        G.TextCentered("VS", new Rectangle(r.X, r.Y + 112, r.Width, 36), 30, Gfx.Bad, true);
        string outcome = o.Captures ? "You will take the city." : o.Kills ? "It will be destroyed." : info.IsRanged ? "A ranged strike: no damage back." : "If both survive, the battle goes on next turn.";
        G.TextCentered($"Deals about {Math.Round(o.ToDefender)}, takes about {Math.Round(o.ToAttacker)}. {outcome}", new Rectangle(r.X, r.Y + 250, r.Width, 22), 16, Gfx.Ink);
        if (G.Button(new Rectangle(r.X + r.Width / 2 - 170, r.Y + r.Height - 62, 160, 46), "Attack", size: 20) || Raylib.IsKeyPressed(KeyboardKey.Enter))
        {
            _lunge[u.Id] = (TileCenter(ap.x, ap.y), (float)Raylib.GetTime());
            Military.Attack(s, c, u, ap.x, ap.y);
            Act1.Reveal(s);
            if (!s.Units.Contains(u)) _selUnit = -1;
            _attackPrompt = null;
            SaveNow();
        }
        if (G.Button(new Rectangle(r.X + r.Width / 2 + 10, r.Y + r.Height - 62, 160, 46), "Cancel", size: 20) || Raylib.IsKeyPressed(KeyboardKey.Escape)) _attackPrompt = null;
    }
}
