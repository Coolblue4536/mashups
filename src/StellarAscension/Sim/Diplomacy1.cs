namespace StellarAscension;

/// <summary>Act 1 diplomacy: civilizations meet when their units or borders come close; an envoy must reach a civ
/// before trade or alliances (war and peace need only a meeting). Trade deals share a resource, pay gold each turn
/// or open borders for a time; allies join each other's defensive wars.</summary>
public static class Diplomacy1
{
    /// <summary>What civ `who` thinks of civ `of`: -100 hostile to +100 friendly, with the reasons.</summary>
    public static (int value, List<string> why) Opinion(Act1State s, Content c, CivState who, CivState of)
    {
        var why = new List<string>();
        int v = 0;
        void Add(int n, string text) { if (n == 0) return; v += n; why.Add($"{(n > 0 ? "+" : "")}{n} {text}"); }
        Add(Act1.Trait(of).Opinion, "their way with people");
        if (s.Relations(who.Id, of.Id)) Add(10, "envoys exchanged");
        if (s.Allied(who.Id, of.Id)) Add(30, "allies");
        Add(Math.Min(20, s.Deals.Count(d => d.Until >= s.Turn && (d.From == who.Id && d.To == of.Id || d.From == of.Id && d.To == who.Id)) * 10), "trade");
        if (s.AtWar(who.Id, of.Id)) Add(-60, "at war");
        if (s.Majors.Any(x => x.Id != who.Id && x.Id != of.Id && s.AtWar(x.Id, who.Id) && s.AtWar(x.Id, of.Id))) Add(15, "a common enemy");
        int close = s.CitiesOf(who.Id).SelectMany(a => s.CitiesOf(of.Id).Select(b => Hex.Distance(a.X, a.Y, b.X, b.Y, s.W))).DefaultIfEmpty(99).Min();
        if (close <= 6) Add(-10, "borders too close");
        if (!who.IsPlayer) Add(-(int)Math.Round(who.Aggression * 15), "their ambitions");
        return (Math.Clamp(v, -100, 100), why);
    }

    // ------------------------------------------------------------------ meeting and envoys

    public static void Meet(Act1State s, Content c, CivState a, CivState b)
    {
        if (a.Id == b.Id || a.IsTribe || b.IsTribe || a.Met.Contains(b.Id)) return;
        a.Met.Add(b.Id); b.Met.Add(a.Id);
        if (a.IsPlayer || b.IsPlayer)
        {
            var other = a.IsPlayer ? b : a;
            s.Events.Add(new Act1Event(s.Turn, "met", $"You have met {Act1.CivName(c, other)}, led by {Act1.LeaderName(c, other)}. Send an envoy to open relations.", s.Player.Id));
        }
    }

    public static string? CantSendEnvoy(Act1State s, CivState from, CivState to)
    {
        if (!s.HasMet(from.Id, to.Id)) return "You haven't met them.";
        if (from.Envoys.ContainsKey(to.Id)) return from.Envoys[to.Id] > s.Turn ? $"Your envoy arrives in {from.Envoys[to.Id] - s.Turn} turn(s)." : "Relations are open.";
        if (s.AtWar(from.Id, to.Id)) return "Not while at war.";
        if (from.Gold < Rules.Act1EnvoyGold) return $"Needs {Rules.Act1EnvoyGold} gold.";
        return null;
    }

    public static bool SendEnvoy(Act1State s, CivState from, CivState to)
    {
        if (CantSendEnvoy(s, from, to) != null) return false;
        from.Gold -= Rules.Act1EnvoyGold;
        from.Envoys[to.Id] = s.Turn + (int)Rules.Act1EnvoyTurns;
        return true;
    }

    // ------------------------------------------------------------------ deals

    static int Value(Content c, string resource) =>
        c.MapResource.TryGetValue(resource, out var r) ? r.Class switch { "strategic" => 40, "luxury" => 25, _ => 12 } : 10;

    /// <summary>Resources a civ can offer (seen and inside its borders).</summary>
    public static List<string> Offerable(Act1State s, Content c, CivState civ) =>
        s.Tiles.Where(t => t.Resource != null && t.City >= 0 && s.City(t.City)?.Owner == civ.Id && Military.CanSee(c, civ, t.Resource)).Select(t => t.Resource!).Distinct().OrderBy(r => r).ToList();

    /// <summary>How much the AI side likes a deal (it accepts at 0 or more).</summary>
    public static int Score(Act1State s, Content c, CivState ai, CivState other, string give, string get, int goldPerTurn, bool borders)
    {
        int v = Opinion(s, c, ai, other).value / 4;
        var mine = Military.OwnedResources(s, c, ai);
        if (get != "") v += mine.Contains(get) ? 3 : Value(c, get);       // the AI receives
        if (give != "") v -= Value(c, give) / 2;                           // sharing costs it little
        v += goldPerTurn * 6;                                              // gold the other side pays each turn (negative: the AI pays)
        if (borders) v -= ai.Aggression > 0.6 ? 15 : 4;
        return v;
    }

    public static string? CantDeal(Act1State s, CivState a, CivState b)
    {
        if (!s.Relations(a.Id, b.Id)) return "Send an envoy first.";
        if (s.AtWar(a.Id, b.Id)) return "Not while at war.";
        return null;
    }

    /// <summary>a offers b: a gives resource `give` (or "") and gold per turn; b gives resource `get` (or ""); borders: both open them.</summary>
    public static bool Propose(Act1State s, Content c, CivState a, CivState b, string give, string get, int goldPerTurn, bool borders, bool force = false)
    {
        if (CantDeal(s, a, b) != null) return false;
        if (!force && !b.IsPlayer && Score(s, c, b, a, get, give, goldPerTurn, borders) < 0) return false;
        int until = s.Turn + (int)Rules.Act1TradeTurns;
        if (give != "") s.Deals.Add(new DealState { From = a.Id, To = b.Id, Kind = "resource", Resource = give, Until = until });
        if (get != "") s.Deals.Add(new DealState { From = b.Id, To = a.Id, Kind = "resource", Resource = get, Until = until });
        if (goldPerTurn > 0) s.Deals.Add(new DealState { From = a.Id, To = b.Id, Kind = "gold", Gold = goldPerTurn, Until = until });
        if (borders) { s.Deals.Add(new DealState { From = a.Id, To = b.Id, Kind = "borders", Until = until }); s.Deals.Add(new DealState { From = b.Id, To = a.Id, Kind = "borders", Until = until }); }
        if (a.IsPlayer || b.IsPlayer)
        {
            var parts = new List<string>();
            if (give != "") parts.Add($"{c.Text("LOC_" + give + "_NAME")} to {Act1.CivName(c, b)}");
            if (get != "") parts.Add($"{c.Text("LOC_" + get + "_NAME")} to {Act1.CivName(c, a)}");
            if (goldPerTurn > 0) parts.Add($"{goldPerTurn} gold a turn to {Act1.CivName(c, b)}");
            if (borders) parts.Add("open borders");
            s.Events.Add(new Act1Event(s.Turn, "deal", $"Trade deal for {Rules.Act1TradeTurns} turns: " + string.Join(", ", parts) + ".", s.Player.Id));
        }
        return true;
    }

    public static string? CantAlly(Act1State s, Content c, CivState a, CivState b)
    {
        if (CantDeal(s, a, b) is { } why) return why;
        if (s.Allied(a.Id, b.Id)) return "Already allies.";
        if (s.Majors.Any(x => s.AtWar(x.Id, b.Id) && s.Allied(x.Id, a.Id))) return "You're allied with their enemy.";
        int op = Opinion(s, c, b, a).value;
        if (!b.IsPlayer && op < 30) return $"They don't trust you enough yet (opinion {op}, needs 30).";
        return null;
    }

    public static bool Ally(Act1State s, Content c, CivState a, CivState b)
    {
        if (CantAlly(s, c, a, b) != null) return false;
        s.Alliances.Add(new[] { Math.Min(a.Id, b.Id), Math.Max(a.Id, b.Id) });
        s.Events.Add(new Act1Event(s.Turn, "alliance", $"{Act1.CivName(c, a)} and {Act1.CivName(c, b)} are now allies.", a.IsPlayer || b.IsPlayer ? s.Player.Id : a.Id));
        return true;
    }

    // ------------------------------------------------------------------ offers to the player

    static string Res(Content c, string r) => c.Text("LOC_" + r + "_NAME");

    public static string Describe(Act1State s, Content c, Act1Offer o)
    {
        string them = Act1.CivName(c, s.Civs[o.From]);
        if (o.Kind == "alliance") return $"{them} proposes an alliance: each of you joins the other's defensive wars, and borders open.";
        var parts = new List<string>();
        if (o.Give != "") parts.Add($"they share their {Res(c, o.Give)}");
        if (o.Gold > 0) parts.Add($"they pay you {o.Gold} gold a turn");
        if (o.Get != "") parts.Add($"you share your {Res(c, o.Get)}");
        if (o.Gold < 0) parts.Add($"you pay them {-o.Gold} gold a turn");
        if (o.Borders) parts.Add("both open borders");
        return $"{them} offers a trade deal for {Rules.Act1TradeTurns} turns: {string.Join(", ", parts)}.";
    }

    public static string? CantAccept(Act1State s, Content c, Act1Offer o)
    {
        var them = s.Civs[o.From];
        if (!them.Alive) return "They are gone.";
        if (o.Kind == "alliance") return CantAlly(s, c, them, s.Player);
        if (CantDeal(s, them, s.Player) is { } why) return why;
        if (o.Get != "" && !Offerable(s, c, s.Player).Contains(o.Get)) return $"You no longer have {Res(c, o.Get)}.";
        if (o.Give != "" && !Offerable(s, c, them).Contains(o.Give)) return $"They no longer have {Res(c, o.Give)}.";
        return null;
    }

    public static bool Accept(Act1State s, Content c, Act1Offer o)
    {
        s.Offers.Remove(o);
        if (CantAccept(s, c, o) != null) return false;
        var them = s.Civs[o.From];
        if (o.Kind == "alliance") return Ally(s, c, them, s.Player);
        return o.Gold >= 0 ? Propose(s, c, them, s.Player, o.Give, o.Get, o.Gold, o.Borders, force: true)
                           : Propose(s, c, s.Player, them, o.Get, o.Give, -o.Gold, o.Borders, force: true);
    }

    public static void Decline(Act1State s, Act1Offer o)
    {
        s.Offers.Remove(o);
        s.NextOffer[o.From] = Math.Max(s.NextOffer.GetValueOrDefault(o.From), s.Turn + 20);
    }

    /// <summary>An AI civ with open relations offers the player a deal: a resource swap, gold for a resource it wants,
    /// a resource for the player's gold, open borders, or an alliance if it's friendly.</summary>
    public static Act1Offer? MakeOffer(Act1State s, Content c, CivState ai)
    {
        var p = s.Player;
        int op = Opinion(s, c, ai, p).value;
        if (op < -10) return null;
        Act1Offer? offer = null;
        if (op >= 40 && CantAlly(s, c, ai, p) == null && s.Rng.Chance(0.35)) offer = new Act1Offer { From = ai.Id, Kind = "alliance" };
        else
        {
            var aiHas = Military.OwnedResources(s, c, ai); var pHas = Military.OwnedResources(s, c, p);
            var want = Offerable(s, c, p).FirstOrDefault(r => !aiHas.Contains(r) && !s.Deals.Any(d => d.To == ai.Id && d.Resource == r));
            var give = Offerable(s, c, ai).FirstOrDefault(r => !pHas.Contains(r) && !s.Deals.Any(d => d.To == p.Id && d.Resource == r));
            if (want != null && give != null) offer = new Act1Offer { From = ai.Id, Give = give, Get = want };
            else if (want != null && ai.Gold > 120) offer = new Act1Offer { From = ai.Id, Get = want, Gold = Math.Clamp(Value(c, want) / 6, 2, 6) };
            else if (give != null && p.Gold > 80) offer = new Act1Offer { From = ai.Id, Give = give, Gold = -Math.Clamp(Value(c, give) / 8, 1, 4) };
            else if (op >= 15 && !s.OpenBorders(ai.Id, p.Id)) offer = new Act1Offer { From = ai.Id, Borders = true };
        }
        if (offer == null) return null;
        offer.Expires = s.Turn + 5;
        s.Offers.Add(offer);
        s.NextOffer[ai.Id] = s.Turn + 15;
        s.Events.Add(new Act1Event(s.Turn, "offer", Describe(s, c, offer) + " Answer in the Diplomacy window.", p.Id));
        return offer;
    }

    // ------------------------------------------------------------------ the turn

    public static void Turn(Act1State s, Content c)
    {
        var majors = s.Majors.Where(x => x.Alive).ToList();
        // Meetings: units or cities close enough.
        var presence = majors.ToDictionary(x => x.Id, x => s.UnitsOf(x.Id).Select(u => (u.X, u.Y, r: (int)Rules.Act1MeetRange))
            .Concat(s.CitiesOf(x.Id).Select(ct => (ct.X, ct.Y, r: (int)Rules.Act1MeetRange + (int)Rules.CityRadius))).ToList());
        for (int i = 0; i < majors.Count; i++)
            for (int j = i + 1; j < majors.Count; j++)
            {
                var a = majors[i]; var b = majors[j];
                if (a.Met.Contains(b.Id)) continue;
                if (presence[a.Id].Any(p => presence[b.Id].Any(q => Hex.Distance(p.X, p.Y, q.X, q.Y, s.W) <= Math.Max(p.r, q.r)))) Meet(s, c, a, b);
            }
        // Envoys arriving.
        foreach (var a in majors)
            foreach (var (b, t) in a.Envoys)
                if (t == s.Turn && (a.IsPlayer || s.Civs[b].IsPlayer))
                    s.Events.Add(new Act1Event(s.Turn, "envoy", a.IsPlayer ? $"Your envoy reached {Act1.CivName(c, s.Civs[b])}: you can now trade and form an alliance." : $"An envoy from {Act1.CivName(c, a)} has arrived: relations are open.", s.Player.Id));
        // Gold deals pay out; finished deals end.
        foreach (var d in s.Deals.Where(d => d.Kind == "gold" && d.Until >= s.Turn))
        {
            var from = s.Civs[d.From]; var to = s.Civs[d.To];
            double pay = Math.Min(d.Gold, Math.Max(0, from.Gold));
            from.Gold -= pay; to.Gold += pay;
        }
        foreach (var d in s.Deals.Where(d => d.Until == s.Turn && (s.Civs[d.From].IsPlayer || s.Civs[d.To].IsPlayer)).Take(1))
            s.Events.Add(new Act1Event(s.Turn, "deal_end", "A trade deal has ended. You can make a new one in the Diplomacy window.", s.Player.Id));
        s.Deals.RemoveAll(d => d.Until < s.Turn || !s.Civs[d.From].Alive || !s.Civs[d.To].Alive);
        s.Alliances.RemoveAll(x => !s.Civs[x[0]].Alive || !s.Civs[x[1]].Alive);
        s.Offers.RemoveAll(o => o.Expires < s.Turn || CantAccept(s, c, o) != null);
        // AIs: send envoys, swap resources and form friendships among themselves.
        foreach (var ai in majors.Where(x => !x.IsPlayer))
        {
            foreach (var o in majors.Where(o => o.Id != ai.Id && ai.Met.Contains(o.Id) && !ai.Envoys.ContainsKey(o.Id)))
                if (ai.Gold > Rules.Act1EnvoyGold + 40 && s.Rng.Chance(0.3)) SendEnvoy(s, ai, o);
            if (s.Rng.Chance(0.15))
                foreach (var o in majors.Where(o => o.Id != ai.Id && !o.IsPlayer && s.Relations(ai.Id, o.Id) && !s.AtWar(ai.Id, o.Id)))
                {
                    var mine = Offerable(s, c, ai); var theirs = Offerable(s, c, o);
                    var want = theirs.FirstOrDefault(r => !Military.OwnedResources(s, c, ai).Contains(r) && !s.Deals.Any(d => d.To == ai.Id && d.Resource == r));
                    var give = mine.FirstOrDefault(r => !Military.OwnedResources(s, c, o).Contains(r) && !s.Deals.Any(d => d.To == o.Id && d.Resource == r));
                    if (want != null && give != null && Score(s, c, o, ai, want, give, 0, false) >= 0) { Propose(s, c, ai, o, give, want, 0, false); break; }
                }
            if (s.Player.Alive && CantDeal(s, ai, s.Player) == null && !s.Offers.Any(o => o.From == ai.Id)
                && s.NextOffer.GetValueOrDefault(ai.Id) <= s.Turn && s.Rng.Chance(0.12))
                MakeOffer(s, c, ai);
            if (s.Rng.Chance(0.03))
                foreach (var o in majors.Where(o => o.Id != ai.Id && !o.IsPlayer))
                    if (CantAlly(s, c, ai, o) == null && Opinion(s, c, ai, o).value >= 35) { Ally(s, c, ai, o); break; }
        }
    }
}
