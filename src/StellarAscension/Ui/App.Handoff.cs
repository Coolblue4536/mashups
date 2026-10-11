using System.Numerics;
using Raylib_cs;

namespace StellarAscension;

public sealed partial class App
{
    /// <summary>Between the acts: what your Civ VI game became in the galaxy.</summary>
    void DrawHandoff()
    {
        G.UseAct2Theme();
        Background(true, "st_handoff_background");
        var s2 = Game!.Act2!;
        var a1 = Game.Act1!;
        var c = Content!;
        var me = s2.Player;
        G.TextCentered("To the Stars", new Rectangle(0, 26, Gfx.W, 50), 48, Gfx.Gold, true);
        G.TextCentered($"{Act1.YearText(a1.Year)}: {me.Name} has reached space. Act 2 begins in {s2.Year}.", new Rectangle(0, 82, Gfx.W, 26), 21, Gfx.Ink);

        float colW = (Gfx.W - 100) / 3, top = 130, h = Gfx.H - top - 110;
        var left = new Rectangle(30, top, colW, h);
        var mid = new Rectangle(50 + colW, top, colW, h);
        var right = new Rectangle(70 + colW * 2, top, colW, h);
        G.Panel(left); G.Panel(mid); G.Panel(right);

        // The race to space
        float x = left.X + 16, y = left.Y + 14, w = left.Width - 32;
        G.Text("The race to space", x, y, 22, Gfx.Gold, true); y += 36;
        for (int i = 0; i < a1.SpaceOrder.Count && y < left.Y + h - 60; i++)
        {
            var civ = a1.Civs[a1.SpaceOrder[i]];
            Gfx.DrawFit(CivIcon(civ.Leader), new Rectangle(x, y, 48, 48), Color.White);
            G.Text($"{i + 1}. {Act1.CivName(c, civ)}" + (civ.IsPlayer ? " (you)" : ""), x + 58, y + 4, 18, civ.IsPlayer ? Gfx.Gold : Gfx.Ink);
            var e2 = s2.Empires.FirstOrDefault(e => e.CivType == civ.CivType && e.Kind == "civ");
            string bonus = e2 == null ? "" : string.Join(", ", e2.Modifiers.Where(m => m.StartsWith("sa_space_race")).Select(m => Defs.Modifier[m].Name));
            G.Text(G.Clip(i == 0 ? "First: owns the rare systems" + (bonus.Length > 0 ? " · " + bonus : "") : bonus.Length > 0 ? bonus : $"Landed turn {civ.MoonTurn}", 15, w - 60), x + 58, y + 28, 15, Gfx.Muted);
            y += 56;
        }
        var notLanded = a1.Civs.Where(cv => !a1.SpaceOrder.Contains(cv.Id)).ToList();
        if (notLanded.Count > 0)
        {
            y += 6;
            G.Para("Still on the ground, they reach space too and join Act 2: " + string.Join(", ", notLanded.Select(cv => Act1.CivName(c, cv))) + ".", x, y, 15, w, Gfx.Muted);
        }

        // Your empire
        x = mid.X + 16; y = mid.Y + 14; w = mid.Width - 32;
        G.Text("Your empire", x, y, 22, Gfx.Gold, true); y += 34;
        var home = s2.Sys(me.Capital);
        var hw = home.Planets.First(p => p.Homeworld);
        y += G.Para($"{me.Name}, led by {me.Leader}. Your homeworld {hw.Name} orbits in the {home.Name} system; its other worlds are named after your largest cities.", x, y, 16, w, Gfx.Ink) + 10;
        var legacy = me.Modifiers.Where(m => m.StartsWith("sa_legacy")).ToList();
        G.Text($"Space Age legacy ({legacy.Count} of {Defs.TechLegacy.Length})", x, y, 18, Gfx.Gold); y += 26;
        var area = new Rectangle(x - 4, y, w + 8, mid.Y + h - y - 12);
        var items = legacy.Select(m => (icon: Icon("ICON_" + Defs.TechLegacy.First(l => l.Modifier == m).CivTech), a: Defs.Modifier[m].Name, b: Defs.Modifier[m].EffectText))
            .Concat(hw.Wonders.Select(wd => (icon: Icon("ICON_" + wd), a: c.Name(wd), b: "Homeworld feature: " + Defs.Modifier[Defs.Wonder[wd].Modifier].EffectText))).ToList();
        float yy = G.BeginScroll("legacy", area, items.Count * 46);
        foreach (var (icon, a, b) in items)
        {
            Gfx.DrawFit(icon, new Rectangle(x, yy + 2, 40, 40), Color.White);
            G.Text(G.Clip(a, 17, w - 50), x + 48, yy + 2, 17, Gfx.Ink);
            G.Text(G.Clip(b, 15, w - 50), x + 48, yy + 22, 15, Gfx.Muted);
            yy += 46;
        }
        if (items.Count == 0) G.Text("No legacy technologies or wonders.", x, yy, 16, Gfx.Muted);
        G.EndScroll();

        // The galaxy
        x = right.X + 16; y = right.Y + 14; w = right.Width - 32;
        G.Text("The galaxy", x, y, 22, Gfx.Gold, true); y += 34;
        var rare = s2.Systems.Where(sy => sy.RareFeature != null).ToList();
        if (rare.Count > 0)
        {
            var owner = s2.Empires[rare[0].Owner];
            G.Text($"Rare star systems held by {owner.Name}", x, y, 17, Gfx.Gold); y += 24;
            foreach (var sy in rare.Take(8))
            {
                Gfx.DrawFit(Icon("ICON_" + sy.RareFeature), new Rectangle(x, y, 34, 34), Color.White);
                G.Text(G.Clip($"{sy.Name}: {Defs.Modifier[sy.RareModifier!].EffectText}", 15, w - 44), x + 42, y + 8, 15, Gfx.Ink);
                y += 38;
            }
            y += 6;
        }
        G.Text("Empires", x, y, 17, Gfx.Gold); y += 24;
        foreach (var e in s2.Empires.Where(e => !e.IsPlayer))
        {
            if (y > right.Y + h - 44) break;
            var pic = e.Kind == "civ" ? CivIcon(e.LeaderType) : G.Tex("portrait:" + e.Portrait, () => Res.Portrait(e.Portrait));
            Gfx.DrawFit(pic, new Rectangle(x, y, 40, 40), Color.White);
            G.Text(G.Clip(e.Name, 17, w - 50), x + 48, y + 2, 17, Gfx.Rgb(e.ColorR, e.ColorG, e.ColorB));
            G.Text(e.Kind == "civ" ? $"From Earth · led by {e.Leader}" : "Stellaris: native to this galaxy", x + 48, y + 22, 14, Gfx.Muted);
            y += 46;
        }

        if (G.Button(new Rectangle(Gfx.W / 2 - 180, Gfx.H - 86, 360, 58), "Begin Act 2", size: 24))
        {
            Game.Phase = "act2";
            SaveNow();
            EnterAct2();
        }
    }
}
