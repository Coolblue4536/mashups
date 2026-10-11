using System.Numerics;
using Raylib_cs;

namespace StellarAscension;

/// <summary>Diplomacy in both acts. Act 1, Civ VI style: civilizations you have met, their leader and trait, what they think
/// of you, and envoys, trade, alliances, war and peace. Act 2, Stellaris style: contacts, envoys establishing communications
/// or improving relations, monthly trade, alliances, war and peace.</summary>
public sealed partial class App
{
    int _dipSel = -1;
    string _dipGive = "", _dipGet = "";
    int _dipGold;
    bool _dipBorders;
    double _dipGiveAmt = 3, _dipGetAmt = 3;

    void Chip(ref float x, float y, string text, Color col)
    {
        float w = G.Measure(text, 13) + 14;
        Raylib.DrawRectangleRounded(new Rectangle(x, y, w, 20), 0.5f, 4, new Color(col.R, col.G, col.B, (byte)60));
        Raylib.DrawRectangleRoundedLinesEx(new Rectangle(x, y, w, 20), 0.5f, 4, 1, col);
        G.Text(text, x + 7, y + 3, 13, col);
        x += w + 6;
    }

    // ---------------------------------------------------------------- Act 1

    void Diplomacy1Overlay(Act1State s, Content c)
    {
        var me = s.Player;
        var r = Overlay("Diplomacy");
        var others = s.Majors.Where(x => !x.IsPlayer).OrderByDescending(x => s.HasMet(me.Id, x.Id)).ThenByDescending(x => x.Alive).ToList();
        if (_dipSel < 0 || _dipSel >= s.Civs.Count || s.Civs[_dipSel].IsPlayer || s.Civs[_dipSel].IsTribe || !s.HasMet(me.Id, _dipSel))
            _dipSel = others.FirstOrDefault(x => s.HasMet(me.Id, x.Id))?.Id ?? -1;
        // List
        var list = new Rectangle(r.X + 16, r.Y + 64, 300, r.Height - 80);
        float y = list.Y;
        foreach (var o in others)
        {
            bool met = s.HasMet(me.Id, o.Id);
            var row = new Rectangle(list.X, y, list.Width, 60);
            y += 64;
            if (row.Y + row.Height > list.Y + list.Height) break;
            bool sel = o.Id == _dipSel;
            Raylib.DrawRectangleRounded(row, 0.15f, 4, sel ? new Color(G.PanelEdge.R, G.PanelEdge.G, G.PanelEdge.B, (byte)90) : G.Hover(row) && met ? G.ButtonHot : G.ButtonBg);
            if (met)
            {
                Gfx.DrawFit(CivIcon(o.Leader), new Rectangle(row.X + 6, row.Y + 6, 48, 48), o.Alive ? Color.White : new Color(255, 255, 255, 80));
                G.Text(G.Clip(Act1.CivName(c, o), 16, row.Width - 70), row.X + 62, row.Y + 8, 16, o.Alive ? Gfx.Ink : Gfx.Muted);
                float cx = row.X + 62;
                if (!o.Alive) Chip(ref cx, row.Y + 32, "Destroyed", Gfx.Muted);
                else if (s.AtWar(me.Id, o.Id)) Chip(ref cx, row.Y + 32, "At war", Gfx.Bad);
                else if (s.Allied(me.Id, o.Id)) Chip(ref cx, row.Y + 32, "Ally", Gfx.Good);
                else if (s.Relations(me.Id, o.Id)) Chip(ref cx, row.Y + 32, "Relations", Gfx.Gold);
                else if (me.Envoys.ContainsKey(o.Id)) Chip(ref cx, row.Y + 32, "Envoy on the way", Gfx.Muted);
                else Chip(ref cx, row.Y + 32, "Met", Gfx.Muted);
                if (s.Offers.Any(x => x.From == o.Id)) Chip(ref cx, row.Y + 32, "Offer", Gfx.Good);
                if (G.HitArea(row, Act1.CivName(c, o))) _dipSel = o.Id;
            }
            else
            {
                Raylib.DrawCircleV(new Vector2(row.X + 30, row.Y + 30), 22, new Color(40, 44, 60, 255));
                G.TextCentered("?", new Rectangle(row.X + 8, row.Y + 8, 44, 44), 26, Gfx.Muted, true);
                G.Text("Unmet civilization", row.X + 62, row.Y + 20, 16, Gfx.Muted);
            }
        }
        if (_dipSel < 0)
        {
            G.Para("You haven't met anyone yet. Explore with your units: civilizations meet when their units or borders come within a few tiles of each other.",
                   r.X + 340, r.Y + 90, 18, r.Width - 380, Gfx.Muted);
            return;
        }
        var them = s.Civs[_dipSel];
        var pane = new Rectangle(r.X + 332, r.Y + 64, r.Width - 348, r.Height - 80);
        // Leader
        var art = LeaderArt(them.Leader);
        var pic = new Rectangle(pane.X, pane.Y, 230, 300);
        Raylib.DrawRectangleRounded(pic, 0.05f, 4, new Color(10, 14, 26, 255));
        if (art != null) Gfx.DrawCover(art, pic, Color.White); else Gfx.DrawFit(CivIcon(them.Leader), pic, Color.White);
        float px = pane.X + 248, py = pane.Y, pw = pane.Width - 248;
        G.Text(Act1.CivName(c, them), px, py, 26, Gfx.Gold, true); py += 34;
        G.Text(Act1.LeaderName(c, them), px, py, 18, Gfx.Ink); py += 26;
        var tr = Act1.Trait(them);
        G.Text($"{c.TraitTitle(them.CivType)} ({tr.Name})", px, py, 16, Gfx.Gold); py += 20;
        py += G.Para(tr.Desc, px, py, 15, pw, Gfx.Muted) + 8;
        var (op, why) = Diplomacy1.Opinion(s, c, them, me);
        G.Text($"Opinion of you: {op:+0;-0;0}", px, py, 18, op >= 30 ? Gfx.Good : op < 0 ? Gfx.Bad : Gfx.Ink);
        if (G.Hover(new Rectangle(px, py, 260, 22))) G.Tip(why.Count > 0 ? string.Join("\n", why) : "Nothing either way yet.");
        py += 26;
        float cx2 = px;
        if (s.AtWar(me.Id, them.Id)) Chip(ref cx2, py, "At war", Gfx.Bad);
        if (s.Allied(me.Id, them.Id)) Chip(ref cx2, py, "Allies", Gfx.Good);
        if (s.Relations(me.Id, them.Id)) Chip(ref cx2, py, "Relations open", Gfx.Gold);
        if (s.OpenBorders(them.Id, me.Id)) Chip(ref cx2, py, "Open borders", Gfx.Good);
        foreach (var d in s.Deals.Where(d => d.Until >= s.Turn && (d.From == me.Id && d.To == them.Id || d.From == them.Id && d.To == me.Id) && d.Kind != "borders"))
            Chip(ref cx2, py, (d.Kind == "gold" ? $"{d.Gold} gold/turn" : c.Text("LOC_" + d.Resource + "_NAME")) + (d.From == me.Id ? " to them" : " to you") + $" ({d.Until - s.Turn}t)", Gfx.Muted);
        py += 30;
        // Actions
        float bw = (pw - 12) / 3;
        string? envoyWhy = Diplomacy1.CantSendEnvoy(s, me, them);
        if (G.Button(new Rectangle(px, py, bw, 38), "Send envoy", envoyWhy == null, tip: envoyWhy ?? $"{Rules.Act1EnvoyGold} gold; arrives in {Rules.Act1EnvoyTurns} turns and opens relations (trade, alliance).", size: 16))
        { Diplomacy1.SendEnvoy(s, me, them); SaveNow(); }
        string? allyWhy = Diplomacy1.CantAlly(s, c, me, them);
        if (G.Button(new Rectangle(px + bw + 6, py, bw, 38), "Propose alliance", allyWhy == null, tip: allyWhy ?? "Allies join each other's defensive wars and share their borders.", size: 16))
        { Diplomacy1.Ally(s, c, me, them); SaveNow(); }
        if (s.AtWar(me.Id, them.Id))
        {
            bool ok = Military.WouldAcceptPeace(s, c, them, me);
            if (G.Button(new Rectangle(px + (bw + 6) * 2, py, bw, 38), "Make peace", ok, tip: ok ? "They will accept." : $"Not yet (wars last at least {Rules.WarMinTurns} turns, and they must not be winning).", size: 16))
            { Military.MakePeace(s, c, me, them); SaveNow(); }
        }
        else if (G.Button(new Rectangle(px + (bw + 6) * 2, py, bw, 38), "Declare war", them.Alive, tip: "Breaks any alliance and deals; their allies join them.", size: 16))
        { Military.DeclareWar(s, c, me, them); SaveNow(); }
        py += 50;
        py += OfferBox1(s, c, them, px, py, pw);
        // Trade
        G.Text("Trade", px, py, 20, Gfx.Gold, true); py += 28;
        if (Diplomacy1.CantDeal(s, me, them) is { } dealWhy) { G.Text(dealWhy, px, py, 15, Gfx.Muted); return; }
        var theirs = Diplomacy1.Offerable(s, c, them).Where(x => !Military.OwnedResources(s, c, me).Contains(x)).ToList();
        var mine = Diplomacy1.Offerable(s, c, me).Where(x => !Military.OwnedResources(s, c, them).Contains(x)).ToList();
        void Pick(string label, List<string> opts, ref string chosen, ref float yy)
        {
            G.Text(label, px, yy + 6, 15, Gfx.Ink);
            float xx = px + 110;
            foreach (var o in opts.Take(8))
            {
                var b = new Rectangle(xx, yy, 40, 34);
                bool on = chosen == o;
                Raylib.DrawRectangleRounded(b, 0.2f, 4, on ? new Color(G.PanelEdge.R, G.PanelEdge.G, G.PanelEdge.B, (byte)130) : G.ButtonBg);
                Gfx.DrawFit(ResourceIcon(o), new Rectangle(b.X + 4, b.Y + 3, 32, 28), Color.White);
                if (G.Hover(b)) G.Tip(c.Text("LOC_" + o + "_NAME"));
                if (G.HitArea(b, c.Text("LOC_" + o + "_NAME"))) chosen = on ? "" : o;
                xx += 44;
            }
            if (opts.Count == 0) G.Text("nothing new to offer", xx, yy + 8, 14, Gfx.Muted);
            yy += 40;
        }
        Pick("They give", theirs, ref _dipGet, ref py);
        Pick("You give", mine, ref _dipGive, ref py);
        G.Text($"Gold a turn: {_dipGold}", px, py + 8, 15, Gfx.Ink);
        if (G.Button(new Rectangle(px + 140, py, 34, 32), "-", _dipGold > 0)) _dipGold = Math.Max(0, _dipGold - 1);
        if (G.Button(new Rectangle(px + 178, py, 34, 32), "+", _dipGold < 20)) _dipGold++;
        if (G.Button(new Rectangle(px + 240, py, 170, 32), _dipBorders ? "Open borders: yes" : "Open borders: no", selected: _dipBorders, size: 15)) _dipBorders = !_dipBorders;
        py += 44;
        bool any = _dipGet != "" || _dipGive != "" || _dipGold > 0 || _dipBorders;
        int score = Diplomacy1.Score(s, c, them, me, _dipGet, _dipGive, _dipGold, _dipBorders);
        string verdict = !any ? "Pick what to trade." : score >= 0 ? "They would accept." : "They would refuse: offer more.";
        if (G.Button(new Rectangle(px, py, 200, 40), "Propose deal", any && score >= 0, tip: $"For {Rules.Act1TradeTurns} turns. A shared resource counts as yours (for units that need it).", size: 17))
        {
            Diplomacy1.Propose(s, c, me, them, _dipGive, _dipGet, _dipGold, _dipBorders);
            _dipGive = _dipGet = ""; _dipGold = 0; _dipBorders = false;
            SaveNow();
        }
        G.Text(verdict, px + 214, py + 10, 16, score >= 0 && any ? Gfx.Good : Gfx.Muted);
    }

    // ---------------------------------------------------------------- Act 2

    void Diplomacy2Pane(Act2State s, Content c, Rectangle r)
    {
        var me = s.Player;
        var list = s.Empires.Where(e => !e.IsPlayer).OrderByDescending(e => s.HasContact(me.Id, e.Id)).ThenByDescending(e => e.Alive).ThenByDescending(e => e.Score).ToList();
        if (_dipSel < 0 || _dipSel >= s.Empires.Count || s.Empires[_dipSel].IsPlayer || !s.HasContact(me.Id, _dipSel))
            _dipSel = list.FirstOrDefault(e => s.HasContact(me.Id, e.Id))?.Id ?? -1;
        int free = Diplomacy2.FreeEnvoys(s, me), total = Diplomacy2.Envoys(s, me);
        G.Text($"Envoys: {free} free of {total}", r.X + 24, r.Y + 58, 16, Gfx.Muted);
        var listR = new Rectangle(r.X + 16, r.Y + 84, 320, r.Height - 100);
        float yy = G.BeginScroll("dip2", listR, list.Count * 66);
        foreach (var e in list)
        {
            var row = new Rectangle(listR.X, yy, listR.Width - 12, 60);
            yy += 66;
            if (row.Y + row.Height < listR.Y || row.Y > listR.Y + listR.Height) continue;
            bool contact = s.HasContact(me.Id, e.Id), sel = e.Id == _dipSel;
            Raylib.DrawRectangleRounded(row, 0.15f, 4, sel ? new Color(G.PanelEdge.R, G.PanelEdge.G, G.PanelEdge.B, (byte)90) : G.Hover(row) && contact ? G.ButtonHot : G.ButtonBg);
            if (contact)
            {
                Raylib.DrawRectangle((int)row.X, (int)row.Y + 6, 4, 48, EColor(e));
                EmpireBadge(e, new Rectangle(row.X + 8, row.Y + 6, 48, 48), e.Alive ? Color.White : new Color(255, 255, 255, 80));
                G.Text(G.Clip(e.Name, 16, row.Width - 70), row.X + 64, row.Y + 8, 16, e.Alive ? EText(e) : Gfx.Muted);
                float cx = row.X + 64;
                if (!e.Alive) Chip(ref cx, row.Y + 32, "Fallen", Gfx.Muted);
                else if (s.AtWar(me.Id, e.Id)) Chip(ref cx, row.Y + 32, "At war", Gfx.Bad);
                else if (s.Allied(me.Id, e.Id)) Chip(ref cx, row.Y + 32, "Ally", Gfx.Good);
                else if (s.HasComms(me.Id, e.Id)) Chip(ref cx, row.Y + 32, "Communications", Gfx.Gold);
                else if (me.EnvoyTasks.TryGetValue(e.Id, out var m) && m > 0) Chip(ref cx, row.Y + 32, $"Envoy: {m - s.Month} mo", Gfx.Muted);
                else Chip(ref cx, row.Y + 32, "Contact", Gfx.Muted);
                if (s.Offers.Any(x => x.From == e.Id)) Chip(ref cx, row.Y + 32, "Offer", Gfx.Good);
                if ((G.Hover(listR) || G.ScriptActive) && G.HitArea(row, e.Name)) _dipSel = e.Id;
            }
            else
            {
                Raylib.DrawCircleV(new Vector2(row.X + 32, row.Y + 30), 22, new Color(20, 40, 44, 255));
                G.TextCentered("?", new Rectangle(row.X + 10, row.Y + 8, 44, 44), 26, Gfx.Muted, true);
                G.Text("Unknown empire", row.X + 64, row.Y + 20, 16, Gfx.Muted);
            }
        }
        G.EndScroll();
        var pane = new Rectangle(r.X + 352, r.Y + 84, r.Width - 368, r.Height - 100);
        if (_dipSel < 0) { G.Para("No contacts yet. Send science ships out: you meet an empire when your ships enter its space or your borders touch.", pane.X, pane.Y + 10, 18, pane.Width, Gfx.Muted); return; }
        var them = s.Empires[_dipSel];
        EmpireBadge(them, new Rectangle(pane.X, pane.Y, 150, 150), Color.White);
        float px = pane.X + 166, py = pane.Y, pw = pane.Width - 166;
        G.Text(them.Name, px, py, 26, EText(them), true); py += 32;
        G.Text(them.Kind == "civ" ? $"Led by {them.Leader} · from Earth, reached space #{them.SpaceRank}" : $"{them.Leader} · native to this galaxy", px, py, 15, Gfx.Muted); py += 22;
        if (Defs.CivTrait.TryGetValue(them.Trait, out var tr)) { G.Text($"{tr.Name}: {tr.Desc}", px, py, 14, Gfx.Gold); py += 22; }
        var (op, why) = Diplomacy2.Opinion(s, them, me);
        G.Text($"Opinion of you: {op:+0;-0;0}", px, py, 18, op >= 30 ? Gfx.Good : op < 0 ? Gfx.Bad : Gfx.Ink);
        if (G.Hover(new Rectangle(px, py, 260, 22))) G.Tip(why.Count > 0 ? string.Join("\n", why) : "Nothing either way yet.");
        py += 28;
        float cx2 = px;
        foreach (var d in s.Deals.Where(d => d.Until >= s.Month && (d.From == me.Id && d.To == them.Id || d.From == them.Id && d.To == me.Id)))
            Chip(ref cx2, py, $"{d.Amount:0} {d.Resource}/mo {(d.From == me.Id ? "to them" : "to you")}", Gfx.Muted);
        py += 30;
        py = Math.Max(py, pane.Y + 160);
        float bw = (pane.Width - 12) / 3;
        string? estWhy = Diplomacy2.CantEstablish(s, me, them);
        if (!s.HasComms(me.Id, them.Id))
        {
            if (G.Button(new Rectangle(pane.X, py, bw, 40), "Establish communications", estWhy == null, tip: estWhy ?? $"Sends an envoy: about {Rules.Act2ContactMonths / Act2.MonthsPerTurn} turns to learn to talk with them.", size: 15))
                Diplomacy2.Establish(s, me, them);
        }
        else if (me.EnvoyTasks.TryGetValue(them.Id, out var task) && task < 0)
        {
            if (G.Button(new Rectangle(pane.X, py, bw, 40), "Recall envoy", tip: "Frees the envoy for another task.", size: 15)) Diplomacy2.Recall(me, them.Id);
        }
        else
        {
            string? imWhy = Diplomacy2.CantImproveRelations(s, me, them);
            if (G.Button(new Rectangle(pane.X, py, bw, 40), "Improve relations", imWhy == null, tip: imWhy ?? "Leaves an envoy with them: +25 opinion while it stays.", size: 15)) Diplomacy2.ImproveRelations(s, me, them);
        }
        string? allyWhy = Diplomacy2.CantAlly(s, me, them);
        if (G.Button(new Rectangle(pane.X + bw + 6, py, bw, 40), "Propose alliance", allyWhy == null, tip: allyWhy ?? "Allies join each other's defensive wars.", size: 15)) Diplomacy2.Ally(s, me, them);
        if (s.AtWar(me.Id, them.Id))
        {
            bool ok = Act2.WouldAcceptPeace(s, me, them);
            if (G.Button(new Rectangle(pane.X + (bw + 6) * 2, py, bw, 40), "Propose peace", ok, tip: ok ? "They will accept." : "They won't accept yet (a war lasts at least a year, and they must not be winning).", size: 15)) Act2.MakePeace(s, me, them);
        }
        else if (G.Button(new Rectangle(pane.X + (bw + 6) * 2, py, bw, 40), "Declare war", them.Alive, tip: "Your fleets can then fight theirs and take their systems. Their allies join them.", size: 15))
            Act2.DeclareWar(s, me, them);
        py += 56;
        py += OfferBox2(s, them, pane.X, py, pane.Width);
        G.Text("Trade (each month, for " + Rules.Act2TradeYears + " years)", pane.X, py, 19, Gfx.Gold, true); py += 30;
        if (Diplomacy2.CantDeal(s, me, them) is { } dealWhy) { G.Text(dealWhy, pane.X, py, 15, Gfx.Muted); return; }
        void Row(string label, ref string res, ref double amt, ref float yy)
        {
            G.Text(label, pane.X, yy + 8, 15, Gfx.Ink);
            float xx = pane.X + 110;
            foreach (var k in Diplomacy2.Tradeable)
            {
                var b = new Rectangle(xx, yy, 40, 34);
                bool on = res == k;
                Raylib.DrawRectangleRounded(b, 0.2f, 4, on ? new Color(G.PanelEdge.R, G.PanelEdge.G, G.PanelEdge.B, (byte)130) : G.ButtonBg);
                Gfx.DrawFit(ResIcon(k), new Rectangle(b.X + 6, b.Y + 4, 28, 26), Color.White);
                if (G.HitArea(b, label + " " + k)) res = on ? "" : k;
                xx += 44;
            }
            G.Text($"{amt:0}/mo", xx + 10, yy + 8, 15, Gfx.Ink);
            if (G.Button(new Rectangle(xx + 70, yy, 32, 32), "-", amt > 1)) amt = Math.Max(1, amt - 1);
            if (G.Button(new Rectangle(xx + 106, yy, 32, 32), "+", amt < 40)) amt += 1;
            yy += 42;
        }
        Row("They give", ref _dipGet, ref _dipGetAmt, ref py);
        Row("You give", ref _dipGive, ref _dipGiveAmt, ref py);
        bool any = _dipGet != "" || _dipGive != "";
        double score = any ? Diplomacy2.Score(s, them, me, _dipGive, _dipGiveAmt, _dipGet, _dipGetAmt) : -1;
        if (G.Button(new Rectangle(pane.X, py + 4, 200, 40), "Propose deal", any && score >= 0, size: 17))
        {
            Diplomacy2.Propose(s, me, them, _dipGive, _dipGive != "" ? _dipGiveAmt : 0, _dipGet, _dipGet != "" ? _dipGetAmt : 0);
            _dipGive = _dipGet = "";
        }
        G.Text(!any ? "Pick what to trade." : score >= 0 ? "They would accept." : "They would refuse: offer more.", pane.X + 214, py + 14, 16, any && score >= 0 ? Gfx.Good : Gfx.Muted);
    }
}
