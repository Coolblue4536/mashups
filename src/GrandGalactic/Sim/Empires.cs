using System.Numerics;

namespace GrandGalactic;

/// <summary>A rival empire: met through an Envoy, then traded with, spied on and fought.</summary>
public sealed class Empire
{
    public required EmpireDef Def;
    public bool Contacted;
    /// <summary>0 unknown, 1 contacted, 2 their systems and rough strength, 3 exact fleet and stockpile.</summary>
    public int Intel;
    public float IntelProgress;
    /// <summary>peace, war, tributary.</summary>
    public string Status = "peace";
    /// <summary>humiliation, claim or tributary (while at war); who declared it.</summary>
    public string WarGoal = "";
    public bool TheyDeclared;
    public int GoalSystem = -1;
    public int WarSince;
    public float StrengthLoss;
    public int HumiliatedUntil;
    public int LastRaid;
    public readonly List<EmpireSystem> Systems = new();
    public readonly List<TradeOffer> Offers = new();
    /// <summary>The table area of their home system, once contacted (its Index).</summary>
    public int Area = -1;
}

public sealed class EmpireSystem
{
    public required string Name;
    public readonly List<string> Planets = new();
    public bool Capital;
    public bool Occupied;
}

public sealed record TradeOffer(string Give, int GiveN, string Get, int GetN)
{
    public bool Used { get; set; }
}

/// <summary>A fleet invading a rival empire. Time stands still at home until it returns.</summary>
public sealed class Invasion
{
    public required Empire Emp;
    public readonly List<Card> Fleet = new();
    public Vector2 ReturnPos;
    public Battle? Fight;
    public int Target = -1;
    public bool Won;
}

public sealed partial class Sim
{
    public readonly List<Empire> Empires = new();
    public Invasion? War;

    public Empire? EmpireOf(string? id) => Empires.FirstOrDefault(e => e.Def.Id == id);

    static readonly string[] EmpireSystemSuffix = { "Prime", "Secundus", "Tertius", "Quartus", "Quintus" };

    /// <summary>Choose this run's rival empires and their systems.</summary>
    void PickEmpires()
    {
        var pool = Defs.Empires.OrderBy(_ => Rng.Next()).Take(Math.Min(Defs.Rules.EmpireCount, Defs.Empires.Length));
        var planets = Defs.Cards.Where(c => c.IsPlanet && c.Id != "homeworld").Select(c => c.Id).ToList();
        foreach (var d in pool)
        {
            var e = new Empire { Def = d };
            for (int i = 0; i < d.Systems; i++)
            {
                var sys = new EmpireSystem { Name = $"{d.Adjective} {EmpireSystemSuffix[i]}", Capital = i == 0 };
                for (int k = 0, n = Rng.Next(1, 4); k < n; k++) sys.Planets.Add(planets[Rng.Next(planets.Count)]);
                e.Systems.Add(sys);
            }
            Empires.Add(e);
        }
    }

    /// <summary>An empire's fleet strength now (on the same scale as your fleets' Strength).</summary>
    public int EmpireStrength(Empire e)
    {
        float s = (e.Def.BaseStrength + e.Def.Growth * MathF.Pow(Moon, 1.3f)) * Diff.EnemyHpMult;
        if (e.HumiliatedUntil > Moon) s *= 0.7f;
        return Math.Max(5, (int)(s - e.StrengthLoss));
    }

    /// <summary>Your total warship strength (all fleets on the table).</summary>
    public int PlayerStrength => Strength(AllCards.Where(c => c.Def.HasTag("warship") && !c.Def.IsHostile), 1.2f);

    void MakeContact()
    {
        var e = Empires.Where(x => !x.Contacted).OrderBy(_ => Rng.Next()).FirstOrDefault();
        if (e == null) { Messages.Add("No other empires answer your Envoy."); return; }
        e.Contacted = true;
        e.Intel = 1;
        Flags.Add("contacted");
        var z = AddSystem(RollSystemType());
        z.Name = e.Systems[0].Name;
        z.Kind = $"{e.Def.Name} capital";
        z.Owner = e.Def.Id;
        e.Area = z.Index;
        foreach (var h in StacksIn(z).Where(x => x.HasHostile).ToList()) foreach (var c in h.Cards.ToList()) Remove(c);
        foreach (var pl in StacksIn(z).Where(x => x.Root.Def.IsPlanet)) pl.Root.Claimed = false;
        var cap = Spawn("empire_capital", z.Center - new Vector2(CardW / 2, CardH / 2), jitter: false);
        cap.EmpireId = e.Def.Id;
        foreach (var s in StacksIn(z)) if (s.Glide is { } g) { s.Pos = g; s.Glide = null; }
        NewTradeOffers(e);
        NewSystems.Add(z);
        Messages.Add($"First contact: the {e.Def.Name}! Their home system joins your table. Click their capital card for diplomacy.");
        Events.Add(SimEvent.Done);
    }

    /// <summary>Envoys at an empire's capital raise intel one level per intel_seconds_per_level.</summary>
    void TickIntel(float dt)
    {
        foreach (var s in Table.Stacks)
        {
            var cap = s.Cards.FirstOrDefault(c => c.EmpireId != null);
            if (cap == null || EmpireOf(cap.EmpireId) is not { } e) continue;
            if (!s.Cards.Any(c => c.Def.HasTag("envoy"))) continue;
            if (e.Intel >= 3) { s.Wait = $"Intel complete on the {e.Def.Name}"; continue; }
            e.IntelProgress += dt;
            float left = Defs.Rules.IntelSecondsPerLevel - e.IntelProgress;
            s.Wait = $"Gathering intel: level {e.Intel + 1} in {Math.Max(0, left):0}s";
            if (left > 0) continue;
            e.IntelProgress = 0;
            e.Intel++;
            Messages.Add($"Intel on the {e.Def.Name} is now level {e.Intel}: " + (e.Intel == 2 ? "their systems and rough fleet strength." : "their exact fleet and stockpile."));
            Events.Add(SimEvent.Done);
        }
    }

    // ---------- trade (time stops while the trade screen is open) ----------

    public void RefreshTrade(Empire e) => NewTradeOffers(e);

    void NewTradeOffers(Empire e)
    {
        e.Offers.Clear();
        float rate = e.Def.Personality switch { "peaceful" => 1.3f, "aggressive" => 0.8f, _ => 1f };
        var deals = new (string give, int gn, string get, int n)[]
        {
            ("minerals", 10, "energy", 6), ("food", 8, "energy", 5), ("energy", 8, "alloys", 3), ("energy", 6, "research", 4),
            ("alloys", 4, "energy", 9), ("research", 6, "energy", 6), ("energy", 12, "rare_crystals", 1), ("energy", 12, "exotic_gases", 1),
            ("minerals", 12, "alloys", 3), ("energy", 10, "influence", 1), ("unity", 4, "energy", 6), ("energy", 9, "food", 10),
        };
        foreach (var d in deals.OrderBy(_ => Rng.Next()).Take(4))
            e.Offers.Add(new TradeOffer(d.give, d.gn, d.get, Math.Max(1, (int)MathF.Round(d.n * rate))));
    }

    /// <summary>Take a trade offer. Returns why not, or null.</summary>
    public string? Trade(Empire e, int i)
    {
        if (e.Status == "war") return $"You are at war with the {e.Def.Name}.";
        if (i < 0 || i >= e.Offers.Count || e.Offers[i].Used) return "That offer is gone until next moon.";
        var o = e.Offers[i];
        if (Have(o.Give) < o.GiveN) return $"You need {o.GiveN} {Name(o.Give)} (you have {Have(o.Give)}).";
        Res[o.Give] -= o.GiveN;
        Gain(o.Get, o.GetN, Home.Center, made: false);
        o.Used = true;
        Events.Add(SimEvent.Sell);
        return null;
    }

    // ---------- war ----------

    /// <summary>Declare war with a goal: humiliation, claim (one of their systems) or tributary. Returns why not, or null.</summary>
    public string? DeclareWar(Empire e, string goal, int system = -1)
    {
        if (!e.Contacted) return "You haven't met them.";
        if (e.Status == "war") return "You are already at war.";
        if (goal == "claim")
        {
            if (system < 0) system = e.Systems.FindIndex(x => !x.Capital && !x.Occupied);
            if (system < 0 || system >= e.Systems.Count) return "They have no system to claim but their capital; choose humiliation or tributary.";
        }
        e.Status = "war";
        e.WarGoal = goal;
        e.GoalSystem = system;
        e.WarSince = Moon;
        e.TheyDeclared = false;
        e.LastRaid = Moon;
        foreach (var s in e.Systems) s.Occupied = false;
        Messages.Add($"War with the {e.Def.Name}! Goal: " + GoalText(e) + ". Send a fleet to their capital and drop it on their capital card to invade.");
        Events.Add(SimEvent.Warning);
        return null;
    }

    public string GoalText(Empire e) => e.WarGoal switch
    {
        "claim" => $"claim {e.Systems[Math.Clamp(e.GoalSystem, 0, e.Systems.Count - 1)].Name} (occupy it)",
        "tributary" => "make them a tributary (occupy their capital)",
        "humiliation" => "humiliate them (occupy their capital)",
        _ => "",
    };

    /// <summary>Offer white peace. They accept when losing, tired of war, or when you are much stronger.</summary>
    public string? OfferPeace(Empire e)
    {
        if (e.Status != "war") return "You are not at war.";
        bool tired = Moon - e.WarSince >= 3, weaker = EmpireStrength(e) < PlayerStrength;
        if (!tired && !weaker) return $"The {e.Def.Name} refuse: they think they can win. Try again in {3 - (Moon - e.WarSince)} moons, or beat their raids.";
        e.Status = "peace";
        e.WarGoal = "";
        Messages.Add($"Peace with the {e.Def.Name}.");
        return null;
    }

    /// <summary>Each moon: empires refresh trade, pay tribute, raid you at war, and the aggressive ones may declare war.</summary>
    void EmpiresMoon()
    {
        foreach (var e in Empires.Where(x => x.Contacted))
        {
            NewTradeOffers(e);
            int str = EmpireStrength(e);
            if (e.Status == "tributary")
            {
                int t = Math.Max(1, (int)((4 + str / 15f) * Defs.Rules.TributePct / 100f));
                Gain("energy", t, Home.Center, made: false);
                Gain("minerals", t, Home.Center, made: false);
                Messages.Add($"The {e.Def.Name} pay their tribute: {t} Energy and {t} Minerals.");
            }
            if (e.Status == "war" && Moon - e.LastRaid >= Defs.Rules.EmpireRaidEvery)
            {
                e.LastRaid = Moon;
                SpawnEmpireRaid(e, (int)(str * 0.25f));
            }
            if (e.Status == "peace" && Moon >= 10 && Moon > e.HumiliatedUntil)
            {
                float chance = e.Def.Personality switch { "aggressive" => 0.15f, "balanced" => 0.05f, _ => 0f };
                if (str > PlayerStrength * 1.3f + 20 && Rng.NextDouble() < chance)
                {
                    e.Status = "war";
                    e.WarGoal = "humiliation";
                    e.TheyDeclared = true;
                    e.WarSince = Moon;
                    e.LastRaid = Moon;
                    Messages.Add($"The {e.Def.Name} declare war on you! Their raid fleets will strike your capital. Build up, beat them back, or offer peace later.");
                    Events.Add(SimEvent.Warning);
                }
            }
        }
    }

    /// <summary>Warships of a rival empire worth about this much strength (bigger hulls later in the game).</summary>
    List<Card> EmpireShips(int strength, int max)
    {
        var hulls = new List<string> { "empire_corvette" };
        if (Moon >= 7) hulls.Add("empire_destroyer");
        if (Moon >= 13) hulls.Add("empire_cruiser");
        if (Moon >= 19) hulls.Add("empire_battleship");
        var ships = new List<Card>();
        while (ships.Count < max && (ships.Count == 0 || Strength(ships) < strength))
            ships.Add(NewCard(hulls[Math.Min(hulls.Count - 1, Rng.Next(Math.Max(1, hulls.Count - 1), hulls.Count + 1) - 1)]));
        return ships;
    }

    void SpawnEmpireRaid(Empire e, int strength)
    {
        var ships = EmpireShips(Math.Max(8, strength), 4);
        int i = 0;
        foreach (var c in ships)
        {
            var s = NewStack(Home.Origin + new Vector2(80 + i * 140, 160));
            Add(s, c);
            c.AggroTimer = 4f + i;
            Place(s, Home, s.Pos);
            i++;
        }
        Messages.Add($"A {e.Def.Adjective} raid fleet ({ships.Count} ships, threat {Strength(ships)}) has entered your capital system!");
        Events.Add(SimEvent.Warning);
    }

    // ---------- invasion: time stops at home while your fleet fights system by system ----------

    /// <summary>Send a fleet into a rival empire you are at war with. Returns why not, or null.</summary>
    public string? StartInvasion(Stack fleet, Empire e)
    {
        if (e.Status != "war") return $"You are not at war with the {e.Def.Name}. Click their capital to declare war (choose a war goal).";
        if (War != null) return "A fleet is already invading.";
        var ships = fleet.Cards.Where(c => c.Def.HasTag("warship")).ToList();
        if (ships.Count == 0) return "Only warships can invade.";
        War = new Invasion { Emp = e, ReturnPos = fleet.Pos };
        foreach (var c in fleet.Cards.Where(Attackable).ToList()) { c.Fleet = fleet.Id; Remove(c); War.Fleet.Add(c); }
        Messages.Add($"Invading the {e.Def.Name}. Time stands still at home until your fleet returns.");
        return null;
    }

    /// <summary>Defenders of one of an empire's systems: the capital holds 40% of its strength, the others share the rest.</summary>
    public int SystemDefence(Empire e, int i)
    {
        var sys = e.Systems[i];
        if (sys.Occupied) return 0;
        int others = Math.Max(1, e.Systems.Count(x => !x.Capital));
        return (int)(EmpireStrength(e) * (sys.Capital ? 0.4f : 0.6f / others));
    }

    /// <summary>Attack one of the invaded empire's systems. Returns why not, or null.</summary>
    public string? AttackSystem(int i)
    {
        if (War is not { } w || w.Fight != null) return "";
        if (i < 0 || i >= w.Emp.Systems.Count || w.Emp.Systems[i].Occupied) return "That system is already occupied.";
        var bt = new Battle { Pos = Vector2.Zero };
        foreach (var c in w.Fleet) { c.Battle = bt; bt.Players.Add(c); foreach (var g in c.Guns) g.Timer = g.Cooldown * (0.5f + 0.5f * (float)Rng.NextDouble()); }
        foreach (var c in EmpireShips(Math.Max(10, SystemDefence(w.Emp, i)), 10)) { c.Battle = bt; bt.Hostiles.Add(c); }
        w.Fight = bt;
        w.Target = i;
        return null;
    }

    /// <summary>The invasion's battle ticks while home is paused.</summary>
    public void UpdateWar(float dt)
    {
        if (War is not { } w || w.Fight is not { } bt) return;
        int before = Strength(bt.Hostiles);
        TickBattle(bt, dt);
        foreach (var c in bt.Players) if (c.MaxShield > 0) c.Shield = MathF.Min(c.MaxShield, c.Shield + c.ShieldRegen * dt);
        if (bt.Hostiles.Count > 0 && bt.Players.Count > 0) return;
        w.Fight = null;
        w.Fleet.Clear();
        foreach (var c in bt.Players) { c.Battle = null; c.Shield = c.MaxShield; w.Fleet.Add(c); }
        if (w.Fleet.Count == 0)
        {
            Messages.Add($"Your invasion fleet was destroyed by the {w.Emp.Def.Name}.");
            w.Emp.StrengthLoss += Math.Max(0, before - Strength(bt.Hostiles)) * 0.5f;
            War = null;
            Events.Add(SimEvent.Lose);
            return;
        }
        var sys = w.Emp.Systems[w.Target];
        sys.Occupied = true;
        w.Emp.StrengthLoss += SystemDefenceBase(w.Emp, sys) * 0.5f;
        Messages.Add($"{sys.Name} is occupied!");
        Flags.Add("battle_won");
        Events.Add(SimEvent.Win);
        bool done = w.Emp.WarGoal == "claim" ? w.Target == w.Emp.GoalSystem : sys.Capital;
        if (done) { w.Won = true; WinWar(w.Emp); }
    }

    int SystemDefenceBase(Empire e, EmpireSystem sys) => (int)(EmpireStrength(e) * (sys.Capital ? 0.4f : 0.6f / Math.Max(1, e.Systems.Count(x => !x.Capital))));

    void WinWar(Empire e)
    {
        switch (e.WarGoal)
        {
            case "humiliation":
                e.Status = "peace";
                e.HumiliatedUntil = Moon + 5;
                Gain("influence", 3, Home.Center, made: false);
                Gain("unity", 10, Home.Center, made: false);
                Messages.Add($"Victory! The {e.Def.Name} are humiliated: weaker for 5 moons. You gain 3 Influence and 10 Unity.");
                break;
            case "tributary":
                e.Status = "tributary";
                Messages.Add($"Victory! The {e.Def.Name} are now your tributary and pay you Energy and Minerals every moon.");
                break;
            case "claim":
                var sys = e.Systems[e.GoalSystem];
                e.Systems.RemoveAt(e.GoalSystem);
                e.Status = "peace";
                if (ClaimedCount < Defs.Rules.ClaimLimit)
                {
                    var z = AddSystem(RollSystemType());
                    z.Name = sys.Name;
                    z.Kind = $"Won from the {e.Def.Name}";
                    z.Claimed = true;
                    foreach (var h in StacksIn(z).Where(x => x.HasHostile).ToList()) foreach (var c in h.Cards.ToList()) Remove(c);
                    foreach (var pl in StacksIn(z).Where(x => x.Root.Def.IsPlanet)) pl.Root.Claimed = true;
                    NewSystems.Add(z);
                    Messages.Add($"Victory! {sys.Name} is yours, colonies and all.");
                }
                else Messages.Add($"Victory! {sys.Name} is freed from the {e.Def.Name}, but you are at your claim limit.");
                break;
        }
        e.WarGoal = "";
    }

    /// <summary>Bring the invasion fleet home (as one stack, where it left from); time at home starts again.</summary>
    public void EndInvasion()
    {
        if (War is not { } w || w.Fight != null) return;
        if (w.Fleet.Count > 0)
        {
            // Home means your capital system.
            var s = NewStack(Home.Center);
            foreach (var c in w.Fleet) Add(s, c);
            Place(s, Home, Home.Center);
            Messages.Add(w.Won ? "The victorious fleet is home." : "The fleet is home.");
        }
        War = null;
    }
}
