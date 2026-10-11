using System.Numerics;
using Raylib_cs;

namespace StellarAscension;

/// <summary>Deals the AI offers you: a message popup when one arrives (Accept, Decline, or Later to answer from the
/// Diplomacy window), and the pending offer in the Diplomacy window until it expires.</summary>
public sealed partial class App
{
    /// <summary>Accept / Decline / Later buttons along the bottom of `r`. Returns "accept", "decline", "later" or "".</summary>
    string OfferButtons(Rectangle r, string? cant, bool later)
    {
        float bw = later ? (r.Width - 12) / 3 : (r.Width - 6) / 2;
        string pick = "";
        if (G.Button(new Rectangle(r.X, r.Y, bw, 40), "Accept offer", cant == null, tip: cant, size: 17)) pick = "accept";
        if (G.Button(new Rectangle(r.X + bw + 6, r.Y, bw, 40), "Decline offer", size: 17)) pick = "decline";
        if (later && G.Button(new Rectangle(r.X + (bw + 6) * 2, r.Y, bw, 40), "Later", tip: "Answer from the Diplomacy window before it expires.", size: 17)) pick = "later";
        return pick;
    }

    /// <summary>The goods each way, with icons: what you receive on the first line, what you give on the second.</summary>
    float OfferGoods(float x, float y, IEnumerable<(Texture2D? icon, string text, bool toYou)> goods)
    {
        float y0 = y;
        foreach (var toYou in new[] { true, false })
        {
            var mine = goods.Where(g => g.toYou == toYou).ToList();
            if (mine.Count == 0) continue;
            G.Text(toYou ? "You receive" : "You give", x, y + 6, 15, toYou ? Gfx.Good : Gfx.Muted);
            float xx = x + 100;
            foreach (var (icon, text, _) in mine)
            {
                if (icon != null) { Gfx.DrawFit(icon, new Rectangle(xx, y, 28, 28), Color.White); xx += 32; }
                G.Text(text, xx, y + 6, 15, Gfx.Ink);
                xx += G.Measure(text, 15) + 18;
            }
            y += 34;
        }
        return y - y0;
    }

    // ---------------------------------------------------------------- Act 1

    IEnumerable<(Texture2D?, string, bool)> Goods1(Content c, Act1Offer o)
    {
        if (o.Give != "") yield return (ResourceIcon(o.Give), c.Text("LOC_" + o.Give + "_NAME"), true);
        if (o.Gold > 0) yield return (null, $"{o.Gold} gold a turn", true);
        if (o.Borders) yield return (null, "Open borders", true);
        if (o.Get != "") yield return (ResourceIcon(o.Get), c.Text("LOC_" + o.Get + "_NAME"), false);
        if (o.Gold < 0) yield return (null, $"{-o.Gold} gold a turn", false);
        if (o.Borders) yield return (null, "Open borders", false);
    }

    void Answer1(Act1State s, Content c, Act1Offer o, string pick)
    {
        if (pick == "accept") Diplomacy1.Accept(s, c, o);
        else if (pick == "decline") Diplomacy1.Decline(s, o);
        else if (pick == "later") o.Seen = true;
        if (pick != "") SaveNow();
    }

    void OfferPopup1(Act1State s, Content c)
    {
        if (_overlay1 != "" || _attackPrompt != null || s.Outcome != "") return;
        if (s.Offers.FirstOrDefault(o => !o.Seen) is not { } o) return;
        var them = s.Civs[o.From];
        Raylib.DrawRectangle(0, 0, (int)Gfx.W, (int)Gfx.H, new Color(0, 0, 0, 90));
        G.Block(new Rectangle(0, 0, Gfx.W, Gfx.H));
        var r = new Rectangle(Gfx.W / 2 - 360, Gfx.H / 2 - 190, 720, 380);
        G.Panel(r);
        var pic = new Rectangle(r.X + 20, r.Y + 20, 210, 280);
        Raylib.DrawRectangleRounded(pic, 0.05f, 4, new Color(10, 14, 26, 255));
        if (LeaderArt(them.Leader) is { } art) Gfx.DrawCover(art, pic, Color.White); else Gfx.DrawFit(CivIcon(them.Leader), pic, Color.White);
        float px = r.X + 248, pw = r.Width - 268, py = r.Y + 20;
        G.Text(o.Kind == "alliance" ? "An offer of alliance" : "A trade offer", px, py, 26, Gfx.Gold, true); py += 34;
        G.Text($"{Act1.LeaderName(c, them)} of {Act1.CivName(c, them)}", px, py, 17, Gfx.Ink); py += 28;
        py += G.Para(Diplomacy1.Describe(s, c, o), px, py, 16, pw, Gfx.Muted) + 10;
        if (o.Kind != "alliance") py += OfferGoods(px, py, Goods1(c, o)) + 4;
        int op = Diplomacy1.Opinion(s, c, them, s.Player).value;
        G.Text($"Opinion of you: {op:+0;-0;0}  ·  answer within {Math.Max(1, o.Expires - s.Turn)} turn(s)", px, py, 15, Gfx.Muted);
        Answer1(s, c, o, OfferButtons(new Rectangle(px, r.Y + r.Height - 60, pw, 40), Diplomacy1.CantAccept(s, c, o), true));
    }

    /// <summary>The pending offer from this civ in the Diplomacy window. Returns the height used.</summary>
    float OfferBox1(Act1State s, Content c, CivState them, float x, float y, float w)
    {
        if (s.Offers.FirstOrDefault(o => o.From == them.Id) is not { } o) return 0;
        float h = 52 + (o.Kind == "alliance" ? 0 : 34 * Goods1(c, o).Select(g => g.Item3).Distinct().Count()) + 48;
        var box = new Rectangle(x, y, w, h);
        Raylib.DrawRectangleRounded(box, 0.08f, 4, new Color(G.PanelEdge.R, G.PanelEdge.G, G.PanelEdge.B, (byte)40));
        Raylib.DrawRectangleRoundedLinesEx(box, 0.08f, 4, 1, Gfx.Gold);
        G.Text($"Their offer ({Math.Max(1, o.Expires - s.Turn)} turn(s) left): " + (o.Kind == "alliance" ? "an alliance" : "a trade deal"), x + 12, y + 10, 16, Gfx.Gold, true);
        float yy = y + 38;
        if (o.Kind != "alliance") yy += OfferGoods(x + 12, yy, Goods1(c, o));
        Answer1(s, c, o, OfferButtons(new Rectangle(x + 12, yy + 4, Math.Min(360, w - 24), 40), Diplomacy1.CantAccept(s, c, o), false));
        return h + 10;
    }

    // ---------------------------------------------------------------- Act 2

    IEnumerable<(Texture2D?, string, bool)> Goods2(Act2Offer o)
    {
        if (o.Give != "") yield return (ResIcon(o.Give), $"{o.GiveAmount:0} {o.Give} a month", true);
        if (o.Get != "") yield return (ResIcon(o.Get), $"{o.GetAmount:0} {o.Get} a month", false);
    }

    void Answer2(Act2State s, Act2Offer o, string pick)
    {
        if (pick == "accept") Diplomacy2.Accept(s, o);
        else if (pick == "decline") Diplomacy2.Decline(s, o);
        else if (pick == "later") o.Seen = true;
    }

    void OfferPopup2(Act2State s, Content c)
    {
        if (_overlay2 != "" || s.Outcome != "") return;
        if (s.Offers.FirstOrDefault(o => !o.Seen) is not { } o) return;
        var them = s.Empires[o.From];
        Raylib.DrawRectangle(0, 0, (int)Gfx.W, (int)Gfx.H, new Color(0, 0, 0, 90));
        G.Block(new Rectangle(0, 0, Gfx.W, Gfx.H));
        var r = new Rectangle(Gfx.W / 2 - 360, Gfx.H / 2 - 170, 720, 340);
        G.Panel(r);
        Raylib.DrawRectangle((int)r.X + 20, (int)r.Y + 20, 4, 220, EColor(them));
        EmpireBadge(them, new Rectangle(r.X + 30, r.Y + 20, 200, 220), Color.White);
        float px = r.X + 248, pw = r.Width - 268, py = r.Y + 20;
        G.Text(o.Kind == "alliance" ? "An offer of alliance" : "A trade offer", px, py, 26, Gfx.Gold, true); py += 34;
        G.Text($"{them.Leader} of the {them.Name}", px, py, 17, EText(them)); py += 28;
        py += G.Para(Diplomacy2.Describe(s, o), px, py, 16, pw, Gfx.Muted) + 10;
        if (o.Kind != "alliance") py += OfferGoods(px, py, Goods2(o)) + 4;
        int op = Diplomacy2.Opinion(s, them, s.Player).value;
        int turns = Math.Max(1, (int)Math.Ceiling((o.Expires - s.Month) / (double)Act2.MonthsPerTurn));
        G.Text($"Opinion of you: {op:+0;-0;0}  ·  answer within {turns} turn(s)", px, py, 15, Gfx.Muted);
        Answer2(s, o, OfferButtons(new Rectangle(px, r.Y + r.Height - 60, pw, 40), Diplomacy2.CantAccept(s, o), true));
    }

    float OfferBox2(Act2State s, Empire them, float x, float y, float w)
    {
        if (s.Offers.FirstOrDefault(o => o.From == them.Id) is not { } o) return 0;
        float h = 52 + (o.Kind == "alliance" ? 0 : 34 * Goods2(o).Select(g => g.Item3).Distinct().Count()) + 44;
        var box = new Rectangle(x, y, w, h);
        Raylib.DrawRectangleRounded(box, 0.08f, 4, new Color(G.PanelEdge.R, G.PanelEdge.G, G.PanelEdge.B, (byte)40));
        Raylib.DrawRectangleRoundedLinesEx(box, 0.08f, 4, 1, Gfx.Gold);
        int turns = Math.Max(1, (int)Math.Ceiling((o.Expires - s.Month) / (double)Act2.MonthsPerTurn));
        G.Text($"Their offer ({turns} turn(s) left): " + (o.Kind == "alliance" ? "an alliance" : "a trade deal"), x + 12, y + 10, 16, Gfx.Gold, true);
        float yy = y + 38;
        if (o.Kind != "alliance") yy += OfferGoods(x + 12, yy, Goods2(o));
        Answer2(s, o, OfferButtons(new Rectangle(x + 12, yy + 2, Math.Min(360, w - 24), 40), Diplomacy2.CantAccept(s, o), false));
        return h + 10;
    }
}
