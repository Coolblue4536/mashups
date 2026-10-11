namespace StellarAscension;

/// <summary>Act 2 diplomacy, Stellaris style: first contact when ships or borders meet; an envoy then needs a year to
/// establish communications with the new empire or species, after which trade deals (resources each month) and
/// alliances are possible. Envoys can also be left with an empire to improve relations. War and peace need contact only.</summary>
public static class Diplomacy2
{
    static readonly Dictionary<string, double> Worth = new() { ["energy"] = 1, ["minerals"] = 1, ["food"] = 1, ["alloys"] = 2.5, ["unity"] = 1.5, ["influence"] = 5 };
    public static readonly string[] Tradeable = { "energy", "minerals", "food", "alloys", "unity" };

    public static int Envoys(Act2State s, Empire e) => (int)(Rules.Act2Envoys + Act2.Stat(s, e, "envoys_add"));
    public static int FreeEnvoys(Act2State s, Empire e) => Envoys(s, e) - e.EnvoyTasks.Count;

    public static (int value, List<string> why) Opinion(Act2State s, Empire who, Empire of)
    {
        var why = new List<string>();
        int v = 0;
        void Add(int n, string text) { if (n == 0) return; v += n; why.Add($"{(n > 0 ? "+" : "")}{n} {text}"); }
        if (Defs.CivTrait.TryGetValue(of.Trait, out var t)) Add(t.Opinion, "their manner");
        if (s.HasComms(who.Id, of.Id)) Add(10, "communications");
        if (of.EnvoyTasks.TryGetValue(who.Id, out var task) && task < 0) Add(25, "their envoy among us");
        if (s.Allied(who.Id, of.Id)) Add(30, "allies");
        Add(Math.Min(20, s.Deals.Count(d => d.Until >= s.Month && (d.From == who.Id && d.To == of.Id || d.From == of.Id && d.To == who.Id)) * 5), "trade");
        if (s.AtWar(who.Id, of.Id)) Add(-60, "at war");
        if (s.Empires.Any(x => x.Alive && x.Id != who.Id && x.Id != of.Id && s.AtWar(x.Id, who.Id) && s.AtWar(x.Id, of.Id))) Add(15, "a common enemy");
        if (Act2Ai.Neighbors(s, who).Contains(of)) Add(-10, "border friction");
        if (who.Kind != of.Kind) Add(-10, "alien ways");
        if (!who.IsPlayer) Add(-(int)Math.Round(who.Aggression * 15), "their ambitions");
        return (Math.Clamp(v, -100, 100), why);
    }

    public static void Contact(Act2State s, Empire a, Empire b)
    {
        if (a.Id == b.Id || a.Contacts.Contains(b.Id)) return;
        a.Contacts.Add(b.Id); b.Contacts.Add(a.Id);
        if (a.IsPlayer || b.IsPlayer)
        {
            var other = a.IsPlayer ? b : a;
            s.Events.Add(new Act2Event(s.Month, "contact", $"First contact with the {other.Name}" + (other.Kind == "alien" ? ", a species never met before" : "") + ". Send an envoy to establish communications.", s.Player.Id));
        }
    }

    public static string? CantEstablish(Act2State s, Empire a, Empire b)
    {
        if (!s.HasContact(a.Id, b.Id)) return "No contact yet: your ships or borders must meet theirs.";
        if (s.HasComms(a.Id, b.Id)) return "Communications are established.";
        if (a.EnvoyTasks.TryGetValue(b.Id, out var m) && m > 0) return $"Your envoy is working on it ({m - s.Month} months left).";
        if (FreeEnvoys(s, a) <= 0) return "No free envoy.";
        return null;
    }

    public static bool Establish(Act2State s, Empire a, Empire b)
    {
        if (CantEstablish(s, a, b) != null) return false;
        a.EnvoyTasks[b.Id] = s.Month + (int)Rules.Act2ContactMonths;
        return true;
    }

    public static string? CantImproveRelations(Act2State s, Empire a, Empire b)
    {
        if (!s.HasComms(a.Id, b.Id)) return "Establish communications first.";
        if (a.EnvoyTasks.ContainsKey(b.Id)) return "An envoy is already there.";
        if (FreeEnvoys(s, a) <= 0) return "No free envoy.";
        return null;
    }

    public static void ImproveRelations(Act2State s, Empire a, Empire b) { if (CantImproveRelations(s, a, b) == null) a.EnvoyTasks[b.Id] = -1; }
    public static void Recall(Empire a, int b) => a.EnvoyTasks.Remove(b);

    // ------------------------------------------------------------------ deals and alliances

    public static string? CantDeal(Act2State s, Empire a, Empire b)
    {
        if (!s.HasComms(a.Id, b.Id)) return "Establish communications first.";
        if (s.AtWar(a.Id, b.Id)) return "Not while at war.";
        return null;
    }

    /// <summary>How the AI side values a deal: it gets `get` x getAmount a month and gives `give` x giveAmount (accepts at 0+).</summary>
    public static double Score(Act2State s, Empire ai, Empire other, string get, double getAmount, string give, double giveAmount)
    {
        var r = Act2.Report(s, ai);
        if (give != "" && r.Income[give] - giveAmount < -2 && ai.Stock.GetValueOrDefault(give) < giveAmount * 24) return -100; // can't afford it
        double v = (get != "" ? Worth[get] * getAmount : 0) - (give != "" ? Worth[give] * giveAmount : 0);
        return v + Opinion(s, ai, other).value / 20.0;
    }

    /// <summary>a gives b `give` x giveAmount a month, b gives a `get` x getAmount, for act2_trade_years.</summary>
    public static bool Propose(Act2State s, Empire a, Empire b, string give, double giveAmount, string get, double getAmount, bool force = false)
    {
        if (CantDeal(s, a, b) != null) return false;
        if (!force && !b.IsPlayer && Score(s, b, a, give, giveAmount, get, getAmount) < 0) return false;
        int until = s.Month + (int)Rules.Act2TradeYears * 12;
        if (give != "" && giveAmount > 0) s.Deals.Add(new TradeDeal { From = a.Id, To = b.Id, Resource = give, Amount = giveAmount, Until = until });
        if (get != "" && getAmount > 0) s.Deals.Add(new TradeDeal { From = b.Id, To = a.Id, Resource = get, Amount = getAmount, Until = until });
        if (a.IsPlayer || b.IsPlayer)
            s.Events.Add(new Act2Event(s.Month, "deal", $"Trade deal with the {(a.IsPlayer ? b : a).Name} for {Rules.Act2TradeYears} years: {giveAmount:0} {give} a month for {getAmount:0} {get}.", s.Player.Id));
        return true;
    }

    public static string? CantAlly(Act2State s, Empire a, Empire b)
    {
        if (CantDeal(s, a, b) is { } why) return why;
        if (s.Allied(a.Id, b.Id)) return "Already allies.";
        int op = Opinion(s, b, a).value;
        if (!b.IsPlayer && op < 30) return $"They don't trust you enough (opinion {op}, needs 30).";
        return null;
    }

    public static bool Ally(Act2State s, Empire a, Empire b)
    {
        if (CantAlly(s, a, b) != null) return false;
        s.Alliances.Add(new[] { Math.Min(a.Id, b.Id), Math.Max(a.Id, b.Id) });
        s.Events.Add(new Act2Event(s.Month, "alliance", $"The {a.Name} and the {b.Name} are now allies.", a.IsPlayer || b.IsPlayer ? s.Player.Id : a.Id));
        return true;
    }

    // ------------------------------------------------------------------ the month

    public static void Month(Act2State s, Content c)
    {
        // Contact: a ship in someone's system, or borders that touch.
        foreach (var f in s.Active)
        {
            var sys = s.Sys(f.System);
            if (sys.Owner >= 0 && sys.Owner != f.Owner) Contact(s, s.Empires[f.Owner], s.Empires[sys.Owner]);
            foreach (var o in s.Active.Where(o => o.System == f.System && o.Owner != f.Owner)) Contact(s, s.Empires[f.Owner], s.Empires[o.Owner]);
        }
        if (s.Month % 3 == 0)
            foreach (var sys in s.Systems.Where(x => x.Owner >= 0))
                foreach (var n in sys.Lanes)
                    if (s.Sys(n).Owner >= 0 && s.Sys(n).Owner != sys.Owner) Contact(s, s.Empires[sys.Owner], s.Empires[s.Sys(n).Owner]);
        // Envoys finishing.
        foreach (var e in s.Empires.Where(e => e.Alive))
            foreach (var (other, done) in e.EnvoyTasks.ToList())
            {
                if (!s.Empires[other].Alive) { e.EnvoyTasks.Remove(other); continue; }
                if (done > 0 && done <= s.Month)
                {
                    e.EnvoyTasks.Remove(other);
                    if (!e.Comms.Contains(other)) e.Comms.Add(other);
                    if (!s.Empires[other].Comms.Contains(e.Id)) s.Empires[other].Comms.Add(e.Id);
                    if (e.IsPlayer || s.Empires[other].IsPlayer)
                        s.Events.Add(new Act2Event(s.Month, "comms", $"Communications established with the {(e.IsPlayer ? s.Empires[other] : e).Name}: trade and alliances are now possible.", s.Player.Id));
                }
            }
        // Deals pay out each month.
        foreach (var d in s.Deals.Where(d => d.Until >= s.Month))
        {
            var from = s.Empires[d.From]; var to = s.Empires[d.To];
            double pay = Math.Min(d.Amount, Math.Max(0, from.Stock.GetValueOrDefault(d.Resource)));
            from.Stock[d.Resource] -= pay;
            to.Stock[d.Resource] = to.Stock.GetValueOrDefault(d.Resource) + pay * (1 + Act2.Stat(s, to, "trade_mult"));
        }
        s.Deals.RemoveAll(d => d.Until < s.Month || !s.Empires[d.From].Alive || !s.Empires[d.To].Alive);
        s.Alliances.RemoveAll(x => !s.Empires[x[0]].Alive || !s.Empires[x[1]].Alive);
        if (s.Month % 6 != 0) return;
        // AI empires: open communications, keep an envoy with a friend, trade surpluses, ally friends.
        foreach (var ai in s.Empires.Where(e => e.Alive && !e.IsPlayer))
        {
            foreach (var o in ai.Contacts.Select(i => s.Empires[i]).Where(o => o.Alive).OrderBy(_ => s.Rng.Double()))
                if (CantEstablish(s, ai, o) == null && s.Rng.Chance(0.5)) Establish(s, ai, o);
            var friend = ai.Comms.Select(i => s.Empires[i]).Where(o => o.Alive && !s.AtWar(ai.Id, o.Id)).OrderByDescending(o => Opinion(s, ai, o).value).FirstOrDefault();
            if (friend != null && FreeEnvoys(s, ai) > 0 && s.Rng.Chance(0.3)) ImproveRelations(s, ai, friend);
            if (s.Rng.Chance(0.25))
            {
                var r = Act2.Report(s, ai);
                string surplus = Tradeable.OrderByDescending(k => r.Income[k] / Worth[k]).First(), need = Tradeable.OrderBy(k => r.Income[k] / Worth[k]).First();
                foreach (var o in ai.Comms.Select(i => s.Empires[i]).Where(o => o.Alive && !o.IsPlayer && !s.AtWar(ai.Id, o.Id)))
                    if (surplus != need && r.Income[surplus] > 6 && Propose(s, ai, o, surplus, 3 * 1 / Worth[surplus] * Worth[need], need, 3)) break;
            }
            if (s.Rng.Chance(0.05))
                foreach (var o in ai.Comms.Select(i => s.Empires[i]).Where(o => o.Alive && !o.IsPlayer))
                    if (CantAlly(s, ai, o) == null && Opinion(s, ai, o).value >= 35) { Ally(s, ai, o); break; }
        }
    }
}
