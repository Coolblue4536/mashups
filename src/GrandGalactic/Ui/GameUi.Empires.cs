using System.Numerics;
using Raylib_cs;

namespace GrandGalactic;

/// <summary>Rival empires on screen: the diplomacy panel (intel, trade, war goals, peace) and the invasion screen.
/// Time stops while any of them is open.</summary>
public sealed partial class GameUi
{
    Empire? _diplo;
    bool _trade, _goalPick;

    bool EmpireScreenOpen => _diplo != null || _sim?.War != null || _intro;

    void CloseDiplomacy() { _diplo = null; _trade = false; _goalPick = false; }

    static string Personality(Empire e) => e.Def.Personality switch { "aggressive" => "Aggressive", "peaceful" => "Peaceful", _ => "Balanced" };

    string StatusText(Empire e) => e.Status switch
    {
        "war" => (e.TheyDeclared ? "AT WAR (they declared)" : "AT WAR") + (e.WarGoal.Length > 0 && !e.TheyDeclared ? $" - goal: {_sim!.GoalText(e)}" : ""),
        "tributary" => "Your tributary (pays you every moon)",
        _ => e.HumiliatedUntil > _sim!.Moon ? $"At peace (humiliated until moon {e.HumiliatedUntil})" : "At peace",
    };

    /// <summary>The welcome card at the start of a tutorial run: the whole game in four lines. Time waits for it.</summary>
    bool _intro;

    void DrawIntro()
    {
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        var r = new Rectangle(sw / 2f - 360, sh / 2f - 240, 720, 480);
        DrawPanelFrame(r, new Color(240, 200, 90, 220));
        Text("Welcome to Grand Galactic", r.X + 30, r.Y + 24, 32, Color.RayWhite);
        var lines = new[]
        {
            ("Cards stack to make things.", "Drag a card onto another: a Pop on your Homeworld makes Energy, a Pop on a farm makes Food."),
            ("Resources live in the top bar.", "Energy, Food, Minerals and the rest are counters you can spend from any system."),
            ("Click a card to give it orders.", "Click your Construction Ship to build, a Shipyard for ships, a rival's capital for diplomacy."),
            ("Follow the gold glow.", "The checklist (top left) says what to do next, and the cards to use glow gold."),
        };
        float y = r.Y + 80;
        foreach (var (head, body) in lines)
        {
            Text(head, r.X + 30, y, 21, new Color(240, 200, 90, 255));
            Wrapped(body, r.X + 30, y + 26, r.Width - 60, 17, Color.LightGray, 2);
            y += 74;
        }
        Text("Everyone eats at the end of each moon (the first is free). Space pauses, Tab opens the blueprint book, Esc the menu.",
             r.X + 30, r.Y + r.Height - 92, 15, Color.Gray);
        if (Button(new Rectangle(r.X + r.Width / 2 - 110, r.Y + r.Height - 66, 220, 50), "Let's go", false, 24)) _intro = false;
    }

    void DrawPanelFrame(Rectangle r, Color edge)
    {
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 160));
        Raylib.DrawRectangleRounded(r, 0.03f, 6, new Color(12, 14, 30, 250));
        Raylib.DrawRectangleRoundedLinesEx(r, 0.03f, 6, 2, edge);
    }

    void DrawDiplomacy()
    {
        var sim = _sim!;
        var e = _diplo!;
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        var r = new Rectangle(sw / 2f - 430, Math.Max(TopBar + 10, sh / 2f - 330), 860, 660);
        var col = Hex(e.Def.Color);
        DrawPanelFrame(r, col);
        if (Tex("st_empire_scene") is { } scene) DrawCover(scene, new Rectangle(r.X + 2, r.Y + 2, r.Width - 4, 110), new Color(255, 255, 255, 110));
        if (Tex(e.Def.Art) is { } art) DrawFit(art, new Rectangle(r.X + 20, r.Y + 16, 84, 84), Color.White);
        Text(e.Def.Name, r.X + 120, r.Y + 18, 34, Color.RayWhite);
        Text(e.Def.Desc, r.X + 122, r.Y + 60, 16, Color.LightGray);
        Text(StatusText(e), r.X + 122, r.Y + 84, 18, e.Status == "war" ? new Color(255, 120, 110, 255) : e.Status == "tributary" ? new Color(240, 210, 120, 255) : new Color(140, 230, 150, 255));
        if (Button(new Rectangle(r.X + r.Width - 110, r.Y + 14, 92, 34), "Close", false, 18)) { CloseDiplomacy(); return; }
        Text("Time is paused", r.X + r.Width - 118, r.Y + 88, 14, Color.Gray);

        // Intel, level by level.
        float y = r.Y + 128, x = r.X + 24;
        if (Tex("st_icon_intel") is { } ii) DrawFit(ii, new Rectangle(x, y, 26, 26), Color.White);
        Text($"Intel level {e.Intel} of 3", x + 34, y + 2, 20, new Color(240, 200, 90, 255));
        Text(e.Intel < 3 ? $"Drop an Envoy on their capital card to learn more ({Defs.Rules.IntelSecondsPerLevel}s per level; {e.IntelProgress:0}s gathered)." : "You know everything about them.",
             x + 230, y + 5, 15, Color.LightGray);
        y += 34;
        int str = sim.EmpireStrength(e), mine = sim.PlayerStrength;
        string strength = e.Intel >= 3 ? $"{str}" : e.Intel >= 2 ? $"about {(int)(str * 0.8f)}-{(int)(str * 1.25f)}" : "unknown (intel 2)";
        Text($"Fleet strength: {strength}     Your warships: {mine}", x, y, 18, Color.RayWhite);
        y += 28;
        if (e.Intel >= 2)
        {
            Text("Systems:", x, y, 17, Color.LightGray);
            y += 24;
            for (int i = 0; i < e.Systems.Count; i++)
            {
                var s = e.Systems[i];
                var line = $"{s.Name}{(s.Capital ? " (capital)" : "")}: " + string.Join(", ", s.Planets.Select(p => _res.CardName(p)))
                           + (e.Intel >= 3 ? $"  - defence {sim.SystemDefence(e, i)}" : "") + (s.Occupied ? "  - occupied" : "");
                Text(line, x + 14, y, 15, s.Occupied ? Color.Gray : Color.RayWhite);
                y += 20;
            }
            if (e.Intel >= 3)
            {
                Text("Stockpile (what they trade): Energy, Minerals, Alloys and Research flow freely; strategic goods are scarce.", x, y + 4, 15, Color.LightGray);
                y += 24;
            }
        }
        else { Text("Their systems: unknown (intel 2).", x, y, 16, Color.Gray); y += 24; }

        // Actions.
        y = Math.Max(y + 12, r.Y + 340);
        Raylib.DrawLine((int)r.X + 20, (int)y - 8, (int)(r.X + r.Width - 20), (int)y - 8, new Color(70, 80, 120, 255));
        if (_trade) { DrawTrade(e, new Rectangle(r.X + 20, y, r.Width - 40, r.Y + r.Height - y - 16)); return; }
        if (_goalPick) { DrawGoalPick(e, new Rectangle(r.X + 20, y, r.Width - 40, r.Y + r.Height - y - 16)); return; }
        float bx = r.X + 24;
        if (e.Status != "war")
        {
            if (Button(new Rectangle(bx, y, 220, 50), "Trade", false, 22)) _trade = true;
            if (Button(new Rectangle(bx + 236, y, 260, 50), "Declare war...", false, 22)) _goalPick = true;
        }
        else
        {
            if (Button(new Rectangle(bx, y, 240, 50), "Offer peace", false, 22))
                Toast(sim.OfferPeace(e) ?? $"The {e.Def.Name} accept peace.");
            Wrapped(e.TheyDeclared
                ? "They declared war on you: their raid fleets will hit your capital every few moons. Beat their raids, then offer peace - or declare your own goal after peace."
                : "To invade: send a fleet to their capital system (drag it there), then drop it on their capital card. Time stops at home while you fight.",
                bx + 260, y + 4, r.Width - 320, 16, Color.LightGray, 4);
        }
        Wrapped("Envoys gather intel. Trading and this screen stop time. Peaceful empires trade best; aggressive ones may declare war when they are much stronger than your fleets.",
                r.X + 24, r.Y + r.Height - 50, r.Width - 48, 15, Color.Gray, 2);
    }

    void DrawTrade(Empire e, Rectangle r)
    {
        var sim = _sim!;
        if (e.Offers.Count == 0) sim.RefreshTrade(e);
        if (Tex("st_trade_scene") is { } ts) DrawCover(ts, new Rectangle(r.X, r.Y, 200, 120), Color.White);
        Text("Trade deals (new ones each moon)", r.X + 220, r.Y, 20, new Color(240, 200, 90, 255));
        float y = r.Y + 32;
        for (int i = 0; i < e.Offers.Count; i++)
        {
            var o = e.Offers[i];
            var row = new Rectangle(r.X + 220, y, r.Width - 220, 40);
            Raylib.DrawRectangleRounded(row, 0.2f, 6, new Color(26, 30, 56, 255));
            float cx = row.X + 10;
            cx += Chip(new RecipeInput(o.Give, o.GiveN, false), cx, row.Y + 7, 26, true) + 10;
            Text("for", cx, row.Y + 10, 17, Color.LightGray);
            cx += 36;
            Chip(new RecipeInput(o.Get, o.GetN, false), cx, row.Y + 7, 26, false);
            if (o.Used) Text("done", row.X + row.Width - 80, row.Y + 10, 17, Color.Gray);
            else if (Button(new Rectangle(row.X + row.Width - 110, row.Y + 5, 100, 30), "Accept", false, 16))
                Toast(sim.Trade(e, i) ?? $"Traded {o.GiveN} {_res.CardName(o.Give)} for {o.GetN} {_res.CardName(o.Get)}.");
            y += 46;
        }
        if (Button(new Rectangle(r.X, r.Y + r.Height - 40, 200, 38), "Back", false, 18)) _trade = false;
    }

    void DrawGoalPick(Empire e, Rectangle r)
    {
        var sim = _sim!;
        Text("Choose a war goal", r.X, r.Y, 22, new Color(240, 200, 90, 255));
        var goals = new (string id, string icon, string title, string desc)[]
        {
            ("humiliation", "st_icon_humiliate", "Humiliation", "Occupy their capital. They are weaker for 5 moons; you gain Influence and Unity."),
            ("tributary", "st_icon_tributary", "Tributary", $"Occupy their capital. They pay you {Defs.Rules.TributePct}% of their income (Energy and Minerals) every moon."),
            ("claim", "st_icon_claim", "Claim a system", "Occupy the claimed system. It joins your empire with its colonies (within your claim limit)."),
        };
        float y = r.Y + 34;
        foreach (var g in goals)
        {
            var row = new Rectangle(r.X, y, r.Width, 46);
            bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), row);
            Raylib.DrawRectangleRounded(row, 0.15f, 6, hover ? new Color(60, 40, 50, 255) : new Color(30, 26, 44, 255));
            if (Tex(g.icon) is { } ic) DrawFit(ic, new Rectangle(row.X + 8, row.Y + 6, 40, 40), Color.White);
            Text(g.title, row.X + 60, row.Y + 6, 20, Color.RayWhite);
            Text(g.desc, row.X + 60, row.Y + 27, 14, Color.LightGray);
            if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                int target = g.id == "claim" ? e.Systems.FindIndex(s => !s.Capital && !s.Occupied) : -1;
                var why = sim.DeclareWar(e, g.id, target);
                if (why != null) Toast(why); else { _goalPick = false; }
                return;
            }
            y += 52;
        }
        if (Button(new Rectangle(r.X, y + 6, 200, 38), "Back", false, 18)) _goalPick = false;
    }

    void DrawInvasion()
    {
        var sim = _sim!;
        var w = sim.War!;
        var e = w.Emp;
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        var r = new Rectangle(30, TopBar + 8, sw - 60, sh - TopBar - 20);
        DrawPanelFrame(r, new Color(255, 110, 100, 230));
        if (Tex("st_war_scene") is { } ws) DrawCover(ws, new Rectangle(r.X + 2, r.Y + 2, r.Width - 4, 90), new Color(255, 255, 255, 120));
        Text($"Invasion of the {e.Def.Name}", r.X + 20, r.Y + 14, 32, Color.RayWhite);
        Text((w.Won ? "War goal achieved." : $"Goal: {sim.GoalText(e)}.") + "  Time stands still at home.", r.X + 22, r.Y + 54, 17, new Color(255, 210, 190, 255));
        // Their systems.
        float y = r.Y + 106;
        Text("Their systems - pick one to attack", r.X + 20, y, 18, new Color(240, 200, 90, 255));
        y += 28;
        for (int i = 0; i < e.Systems.Count; i++)
        {
            var s = e.Systems[i];
            var row = new Rectangle(r.X + 20, y, 360, 54);
            bool goal = e.WarGoal == "claim" ? i == e.GoalSystem : s.Capital;
            Raylib.DrawRectangleRounded(row, 0.15f, 6, s.Occupied ? new Color(30, 60, 40, 255) : new Color(40, 26, 34, 255));
            Raylib.DrawRectangleRoundedLinesEx(row, 0.15f, 6, goal ? 2.5f : 1, goal ? new Color(255, 210, 100, 255) : new Color(110, 90, 110, 200));
            Text(s.Name + (s.Capital ? " (capital)" : "") + (goal ? "  GOAL" : ""), row.X + 10, row.Y + 6, 17, Color.RayWhite);
            Text(s.Occupied ? "Occupied" : $"Defence {(e.Intel >= 2 ? sim.SystemDefence(e, i).ToString() : "?")}", row.X + 10, row.Y + 30, 14,
                 s.Occupied ? new Color(140, 230, 150, 255) : new Color(255, 190, 170, 255));
            if (!s.Occupied && w.Fight == null && !w.Won && Button(new Rectangle(row.X + row.Width - 100, row.Y + 11, 90, 32), "Attack", false, 16))
                sim.AttackSystem(i);
            y += 60;
        }
        // The fight (or your fleet between fights).
        float fx = r.X + 410, fy = r.Y + 110;
        if (w.Fight is { } bt)
        {
            int mine = Sim.Strength(bt.Players, bt.Players.Any(p => p.Admiral != null) ? Defs.Card["admiral"].BoostMult : 1f), theirs = Sim.Strength(bt.Hostiles);
            Text($"Battle for {e.Systems[w.Target].Name}:  you {mine}  vs  {theirs}", fx, fy, 22, mine >= theirs ? new Color(190, 255, 190, 255) : new Color(255, 200, 190, 255));
            for (int i = 0; i < bt.Hostiles.Count; i++)
                DrawCard(bt.Hostiles[i], new Rectangle(fx + (i % 8) * (Sim.CardW * 0.85f + 8), fy + 36 + (i / 8) * 150, Sim.CardW * 0.85f, Sim.CardH * 0.85f), false);
            float py = fy + 36 + ((bt.Hostiles.Count + 7) / 8) * 150 + 20;
            for (int i = 0; i < bt.Players.Count; i++)
                DrawCard(bt.Players[i], new Rectangle(fx + (i % 8) * (Sim.CardW * 0.85f + 8), py + (i / 8) * 150, Sim.CardW * 0.85f, Sim.CardH * 0.85f), false);
        }
        else
        {
            Text(w.Won ? "War goal achieved! Bring your fleet home." : $"Your fleet (strength {Sim.Strength(w.Fleet, w.Fleet.Any(p => p.Admiral != null) ? Defs.Card["admiral"].BoostMult : 1f)}): pick a system to attack, or go home.",
                 fx, fy, 20, Color.RayWhite);
            for (int i = 0; i < w.Fleet.Count; i++)
                DrawCard(w.Fleet[i], new Rectangle(fx + (i % 8) * (Sim.CardW * 0.85f + 8), fy + 40 + (i / 8) * 150, Sim.CardW * 0.85f, Sim.CardH * 0.85f), false);
            if (Button(new Rectangle(r.X + r.Width - 260, r.Y + r.Height - 70, 230, 52), "Return home", false, 22)) sim.EndInvasion();
        }
    }
}
