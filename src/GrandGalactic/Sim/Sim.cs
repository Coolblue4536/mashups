using System.Numerics;

namespace GrandGalactic;

/// <summary>The whole run: one table of card stacks split into star-system areas, recipes, moons, combat, packs and the
/// three acts. No rendering.</summary>
public sealed class Sim
{
    public const float CardW = 120, CardH = 160, StackStep = 30;
    /// <summary>Size of one star-system area, and the gap between neighbouring areas.</summary>
    public const float SysW = 1500, SysH = 1000, SysGap = 140;

    public readonly Random Rng;
    public readonly EthicDef Ethic;
    public readonly DifficultyDef Diff;
    public readonly int MoonSeconds;
    public readonly CrisisDef Crisis;
    public readonly Board Table = new();
    public readonly List<StarSystem> Systems = new();
    public readonly HashSet<string> Techs = new();
    public readonly HashSet<string> Discovered = new();
    public readonly List<string> Messages = new();
    public readonly List<SimEvent> Events = new();
    public readonly Func<string, string> Name;
    /// <summary>Systems added since the UI last looked (it scrolls to show them).</summary>
    public readonly List<StarSystem> NewSystems = new();

    public int Moon = 1;
    public float MoonTime;
    public int Act = 1;
    public RunState State = RunState.Playing;
    public string EndReason = "";
    public bool RiftOpen, BossArrived;
    public int CrisisMoon;
    int _uid, _stackId, _guardianOpened, _nameRound;
    readonly List<string> _unusedNames = new(Defs.Rules.SystemNames);

    public StarSystem Home => Systems[0];
    public Vector2 BoundsMin { get; private set; }
    public Vector2 BoundsMax { get; private set; }

    public Sim(EthicDef ethic, int seed, Func<string, string>? name = null, DifficultyDef? difficulty = null, MoonLengthDef? moon = null)
    {
        Rng = new Random(seed);
        Ethic = ethic;
        Diff = difficulty ?? Defs.DefaultDifficulty;
        MoonSeconds = (moon ?? Defs.DefaultMoonLength).Seconds;
        CrisisMoon = Diff.CrisisMoon;
        Crisis = Defs.Crises[Rng.Next(Defs.Crises.Length)];
        Name = name ?? (id => Defs.Card.TryGetValue(id, out var d) ? d.Name : id);
        var homeSys = Defs.Systems.First(s => s.Kind == "home");
        AddSystem(homeSys);

        var center = Home.Center;
        var start = new List<string>();
        foreach (var a in Defs.Rules.StartCards) for (int i = 0; i < a.N; i++) start.Add(a.Card);
        for (int i = 0; i < Defs.Rules.StartWorkers; i++) start.Add(ethic.WorkerCard);
        foreach (var a in ethic.BonusCards) for (int i = 0; i < a.N; i++) start.Add(a.Card);
        foreach (var a in Diff.BonusCards) for (int i = 0; i < a.N; i++) start.Add(a.Card);
        int k = 0;
        foreach (var id in start)
        {
            var pos = center + new Vector2(-560 + (k % 8) * 150, -260 + (k / 8) * 200);
            var c = Spawn(id, pos, jitter: false);
            if (id == "homeworld") c.Claimed = true;
            k++;
        }
        foreach (var t in ethic.StartTechs) Techs.Add(t);
        OpenPack(Defs.Pack[ethic.FreePack], center + new Vector2(0, 340), free: true);
    }

    // ---------- cards and stacks ----------

    /// <summary>Area slots spiral outward from the capital, ring by ring, nearest first.</summary>
    static readonly List<(int x, int y)> Slots = new() { (0, 0) };
    static int _ringsBuilt;

    static (int x, int y) SlotAt(int n)
    {
        while (Slots.Count <= n)
        {
            int ring = ++_ringsBuilt;
            var r = new List<(int x, int y)>();
            for (int x = -ring; x <= ring; x++)
                for (int y = -ring; y <= ring; y++)
                    if (Math.Max(Math.Abs(x), Math.Abs(y)) == ring) r.Add((x, y));
            Slots.AddRange(r.OrderBy(p => Math.Abs(p.x) + Math.Abs(p.y)).ThenBy(p => Math.Atan2(p.y, p.x)));
        }
        return Slots[n];
    }

    /// <summary>Add a star system as a new area of the table. Random rows roll their planets and extras and get a random name.</summary>
    public StarSystem AddSystem(SystemDef sys)
    {
        string name = sys.Name;
        if (sys.Kind == "random")
        {
            if (_unusedNames.Count == 0)
            {
                _nameRound++;
                _unusedNames.AddRange(Defs.Rules.SystemNames.Select(n => $"{n} {new[] { "II", "III", "IV", "V", "VI" }[Math.Min(_nameRound - 1, 4)]}"));
            }
            int pick = Rng.Next(_unusedNames.Count);
            name = _unusedNames[pick];
            _unusedNames.RemoveAt(pick);
        }
        var slot = SlotAt(Systems.Count);
        var origin = new Vector2(slot.x * (SysW + SysGap), slot.y * (SysH + SysGap));
        var z = new StarSystem { Index = Systems.Count, Sys = sys, Name = name, Kind = sys.Kind == "random" ? sys.Name : "", Origin = origin,
                                 Size = new Vector2(SysW, SysH), Slot = slot, Claimed = sys.Kind == "home" };
        Systems.Add(z);
        BoundsMin = Vector2.Min(Systems.Count == 1 ? origin : BoundsMin, origin);
        BoundsMax = Vector2.Max(Systems.Count == 1 ? origin + z.Size : BoundsMax, origin + z.Size);

        // Stars across the top, everything else in a loose grid below.
        for (int i = 0; i < sys.StarCards.Length; i++)
            Spawn(sys.StarCards[i], origin + new Vector2(SysW / 2 - CardW / 2 + (i - (sys.StarCards.Length - 1) / 2f) * 240, 70), jitter: false);
        var cards = new List<string>();
        foreach (var a in sys.FixedCards) for (int i = 0; i < a.N; i++) cards.Add(a.Card);
        if (sys.PlanetPool.Length > 0)
        {
            int planets = Rng.Next(sys.PlanetsMin, sys.PlanetsMax + 1);
            for (int i = 0; i < planets; i++) cards.Add(Roll(sys.PlanetPool.Select(p => (p.Card, p.Weight))));
        }
        foreach (var e in sys.Extras)
            if (Rng.NextDouble() < e.Chance)
                for (int i = 0; i < e.N; i++) cards.Add(e.Card);
        for (int k = 0; k < cards.Count; k++)
            Spawn(cards[k], origin + new Vector2(170 + (k % 6) * 200, 300 + (k / 6) * 240), jitter: true);
        return z;
    }

    /// <summary>The star system whose area holds a table position (null in the gaps between systems).</summary>
    public StarSystem? SystemAt(Vector2 p) => Systems.FirstOrDefault(z => z.Contains(p));

    public static Vector2 CardCenter(Stack s) => s.Pos + new Vector2(CardW / 2, CardH / 2);

    public IEnumerable<Stack> StacksIn(StarSystem z) => Table.Stacks.Where(s => !s.Traveling && z.Contains(CardCenter(s)));

    // ---------- claiming systems ----------

    public int ClaimedCount => Systems.Count(z => z.Claimed);

    /// <summary>Why a system can't be claimed right now, or null when it can.</summary>
    public string? ClaimBlock(StarSystem z)
    {
        if (z.Claimed) return $"{z.Name} is already yours.";
        if (ClaimedCount >= Defs.Rules.ClaimLimit)
            return $"Claim limit reached: you own {ClaimedCount}/{Defs.Rules.ClaimLimit} systems.";
        if (StacksIn(z).Any(s => s.HasHostile) || Table.Battles.Any(b => z.Contains(b.Pos)))
            return $"Clear the hostiles out of {z.Name} before claiming it.";
        return null;
    }

    /// <summary>When a stack looks like a claim attempt that can't happen, say why (once per change to the stack).</summary>
    void ExplainBlockedClaim(Stack s)
    {
        if (!s.Cards.Any(c => c.Def.Category == "star") || !s.Cards.Any(c => c.Def.Id == "construction_ship")) return;
        if (SystemAt(CardCenter(s)) is { } z && ClaimBlock(z) is { } why && !z.Claimed) Messages.Add(why);
    }

    // ---------- travel between systems ----------

    public static bool HasShip(Stack s) => s.Cards.Any(c => c.Def.HasTag("ship"));

    public float TravelSeconds(StarSystem a, StarSystem b) =>
        Math.Max(1, Math.Max(Math.Abs(a.Slot.X - b.Slot.X), Math.Abs(a.Slot.Y - b.Slot.Y))) * Defs.Rules.TravelSecondsPerJump;

    /// <summary>Send a stack (it must hold a ship) from one star system to a spot in another; it arrives after the travel time.</summary>
    public bool StartTravel(Stack s, Vector2 to)
    {
        var from = SystemAt(CardCenter(s));
        var dest = SystemAt(to + new Vector2(CardW / 2, CardH / 2));
        if (from == null || dest == null || from == dest || !HasShip(s)) return false;
        s.TravelFrom = s.Pos;
        s.TravelTo = Clamp(to);
        s.TravelT = 0;
        s.TravelDur = TravelSeconds(from, dest);
        s.Active = null;
        s.Progress = 0;
        return true;
    }

    void TickTravel(float dt)
    {
        foreach (var s in Table.Stacks.Where(s => s.Traveling).ToList())
        {
            s.TravelT += dt;
            float k = Math.Clamp(s.TravelT / s.TravelDur, 0, 1);
            s.Pos = Vector2.Lerp(s.TravelFrom, s.TravelTo, k);
            if (k >= 1)
            {
                s.TravelDur = 0;
                s.Dirty = true;
                if (SystemAt(CardCenter(s)) is { } z) Messages.Add($"Arrived at {z.Name}.");
            }
        }
    }

    /// <summary>Roll a random system type by weight (what a survey finds).</summary>
    public SystemDef RollSystemType() => Roll(Defs.Systems.Where(s => s.Kind == "random").Select(s => (s, s.Weight)));

    public Card NewCard(string id)
    {
        var def = Defs.Card[id];
        int hp = def.IsHostile ? (int)MathF.Round(def.Hp * Diff.EnemyHpMult) : def.Hp;
        var c = new Card { Uid = ++_uid, Def = def, Hp = hp, MaxHp = hp, AttackTimer = def.AttackCd, AggroTimer = Defs.Rules.EnemyAggroSeconds * (0.6f + (float)Rng.NextDouble()) };
        if (def.IsPlanet && def.ColonizeWith == "none") c.Claimed = true;
        if (id == Crisis.RiftCard) c.SpawnTimer = RiftSpawnEvery * 0.5f;
        if (def.Category == "tech") Techs.Add(id);
        return c;
    }

    public Card Spawn(string id, Vector2 pos, bool jitter = true)
    {
        var c = NewCard(id);
        var s = NewStack(pos + (jitter ? new Vector2(Rng.Next(-40, 41), Rng.Next(-40, 41)) : Vector2.Zero));
        Add(s, c);
        return c;
    }

    public Stack NewStack(Vector2 pos)
    {
        var s = new Stack { Id = ++_stackId, Pos = Clamp(pos) };
        Table.Stacks.Add(s);
        return s;
    }

    public Vector2 Clamp(Vector2 p) => new(Math.Clamp(p.X, BoundsMin.X, BoundsMax.X - CardW), Math.Clamp(p.Y, BoundsMin.Y, BoundsMax.Y - CardH));

    void Add(Stack s, Card c)
    {
        c.Stack = s;
        c.Battle = null;
        s.Cards.Add(c);
        s.Dirty = true;
    }

    public void Remove(Card c)
    {
        var s = c.Stack;
        if (s != null)
        {
            s.Cards.Remove(c);
            s.Dirty = true;
            c.Stack = null;
            if (s.Cards.Count == 0) Table.Stacks.Remove(s);
        }
        if (c.Battle != null)
        {
            c.Battle.Players.Remove(c);
            c.Battle.Hostiles.Remove(c);
            c.Battle = null;
        }
    }

    public IEnumerable<Card> AllCards => Table.AllCards;

    /// <summary>Lift card at index and everything above it into a new stack (Stacklands-style pick-up).</summary>
    public Stack Split(Stack s, int index)
    {
        if (index == 0) return s;
        var ns = NewStack(s.Pos + new Vector2(0, index * StackStep));
        var moving = s.Cards.Skip(index).ToList();
        foreach (var c in moving) { s.Cards.Remove(c); Add(ns, c); }
        s.Dirty = true;
        return ns;
    }

    public bool CanStack(Stack moving, Stack target) =>
        moving != target && !moving.HasHostile && !target.HasHostile
        && moving.Cards.Count + target.Cards.Count <= Defs.Rules.MaxStack;

    public bool StackOnto(Stack moving, Stack target)
    {
        if (!CanStack(moving, target)) return false;
        foreach (var c in moving.Cards.ToList()) { moving.Cards.Remove(c); Add(target, c); }
        Table.Stacks.Remove(moving);
        Events.Add(SimEvent.Drop);
        return true;
    }

    // ---------- recipes ----------

    public Match? FindMatch(Stack s)
    {
        if (s.Cards.Count < 2 || s.HasHostile) return null;
        foreach (var r in Defs.Recipes)
        {
            if (r.RequiresTech != "none" && !Techs.Contains(r.RequiresTech)) continue;
            if (r.RequiresSystem != "any")
            {
                var z = SystemAt(CardCenter(s));
                if (z == null || z.Claimed != (r.RequiresSystem == "claimed")) continue;
                if (r.Effect == "claim_system" && ClaimBlock(z) != null) continue;
            }
            foreach (var st in s.Cards)
            {
                if (!StationOk(r, st)) continue;
                var rest = s.Cards.Where(c => c != st).ToList();
                var consumed = new List<Card>();
                bool ok = true;
                foreach (var inp in r.Inputs.OrderBy(i => i.Card.StartsWith("tag:") ? 1 : 0))
                {
                    var pool = rest.Where(c => inp.Card.StartsWith("tag:") ? c.Def.HasTag(inp.Card[4..]) : c.Def.Id == inp.Card)
                        .Take(inp.N).ToList();
                    if (pool.Count < inp.N) { ok = false; break; }
                    foreach (var c in pool) { rest.Remove(c); if (!inp.Keep) consumed.Add(c); }
                }
                if (!ok) continue;
                if (rest.Any(c => c.Def.BoostTag != r.Tag)) continue;
                float dur = r.Time < 0 ? st.Def.YieldTime : r.Time;
                foreach (var b in rest) dur /= b.Def.BoostMult;
                return new Match(r, st, consumed, dur);
            }
        }
        return null;
    }

    static bool StationOk(RecipeDef r, Card st)
    {
        bool kindOk = r.Station switch
        {
            "has:yield" => st.Def.Yield != "none",
            var t when t.StartsWith("tag:") => st.Def.HasTag(t[4..]),
            var id => st.Def.Id == id,
        };
        if (!kindOk) return false;
        return r.RequiresFlag switch
        {
            "claimed" => !st.Def.IsPlanet || st.Claimed,
            "unclaimed" => st.Def.IsPlanet && !st.Claimed,
            _ => true,
        };
    }

    void TickRecipes(float dt)
    {
        foreach (var s in Table.Stacks.ToList())
        {
            if (s.Traveling) continue;
            if (s.Dirty)
            {
                s.Dirty = false;
                var m = FindMatch(s);
                if (m == null) ExplainBlockedClaim(s);
                if (m == null || m.Value.Recipe != s.Active || m.Value.Station != s.ActiveStation)
                {
                    s.Active = m?.Recipe;
                    s.ActiveStation = m?.Station;
                    s.Progress = 0;
                    s.Duration = m?.Duration ?? 0;
                }
                else s.Duration = m.Value.Duration;
            }
            if (s.Active == null || s.Dragging) continue;
            s.Progress += dt;
            if (s.Progress >= s.Duration) Complete(s);
        }
    }

    void Complete(Stack s)
    {
        var m = FindMatch(s);
        s.Progress = 0;
        s.Dirty = true;
        if (m == null) { s.Active = null; return; }
        var (r, st, consumed, _) = m.Value;
        var outPos = s.Pos + new Vector2(CardW + 30, 0);
        foreach (var c in consumed) Remove(c);
        if (!r.StationKeep) Remove(st);

        if (r.Outputs.Length > 0)
        {
            var o = Roll(r.Outputs.Select(x => (x, x.Weight)));
            foreach (var g in o.Give)
            {
                var id = g.Card == "station.yield" ? st.Def.Yield : g.Card;
                for (int i = 0; i < g.N; i++)
                {
                    bool newTech = Defs.Card[id].Category == "tech" && !Techs.Contains(id);
                    Spawn(id, outPos + new Vector2(0, i * 12));
                    if (newTech) Messages.Add($"Researched {Name(id)}!");
                }
            }
        }
        switch (r.Effect)
        {
            case "claim_system":
                if (SystemAt(CardCenter(s)) is { } claimed)
                {
                    claimed.Claimed = true;
                    Messages.Add($"{claimed.Name} is now yours ({ClaimedCount}/{Defs.Rules.ClaimLimit} systems). Colonise its planets!");
                }
                break;
            case "set_flag:claimed":
                st.Claimed = true;
                Messages.Add($"{Name(st.Def.Id)} is now part of your empire.");
                break;
            case "open_board:random": OpenSystem("random", outPos); break;
            case "open_board:guardian": OpenSystem("guardian", outPos); break;
        }
        Discovered.Add(r.Id);
        Events.Add(SimEvent.Done);
    }

    void OpenSystem(string kind, Vector2 pos)
    {
        SystemDef? sys = kind == "guardian"
            ? Defs.Systems.Where(x => x.Kind == "guardian").Skip(_guardianOpened).FirstOrDefault()
            : RollSystemType();
        if (sys == null)
        {
            Messages.Add("The survey found only empty space - and a little salvage.");
            for (int i = 0; i < 4; i++) Spawn("energy", pos);
            return;
        }
        if (kind == "guardian") _guardianOpened++;
        var z = AddSystem(sys);
        Messages.Add(kind == "guardian" ? $"Found {z.Name}!" : $"Surveyed {z.Name}: {z.Kind.ToLowerInvariant()}, {StacksIn(z).Count(s => s.Root.Def.IsPlanet)} planets.");
        NewSystems.Add(z);
    }

    T Roll<T>(IEnumerable<(T item, int weight)> table)
    {
        var list = table.ToList();
        int total = list.Sum(x => x.weight), r = Rng.Next(total);
        foreach (var (item, w) in list) { if (r < w) return item; r -= w; }
        return list[^1].item;
    }

    // ---------- packs and market ----------

    public IEnumerable<PackDef> AvailablePacks => Defs.Packs.Where(p => p.UnlockAct <= Act);
    public int PackCost(PackDef p) => Math.Max(1, (int)MathF.Round(p.Cost * Diff.PackCostMult));
    float RiftSpawnEvery => Crisis.SpawnEvery * Diff.RiftSpawnMult;

    public bool BuyPack(Stack moving, PackDef pack, Vector2 spawnAt)
    {
        if (pack.UnlockAct > Act || moving.Cards.Any(c => c.Def.Id != "energy") || moving.Cards.Count < PackCost(pack)) return false;
        foreach (var c in moving.Cards.Take(PackCost(pack)).ToList()) Remove(c);
        OpenPack(pack, spawnAt, free: false);
        return true;
    }

    void OpenPack(PackDef pack, Vector2 at, bool free)
    {
        for (int i = 0; i < pack.Draws; i++)
            Spawn(Roll(pack.Contents.Select(e => (e.Card, e.Weight))), at + new Vector2((i - pack.Draws / 2f) * (CardW + 20), 0));
        Events.Add(SimEvent.PackOpen);
        if (free) Messages.Add($"Your {Ethic.Name} start: a free {pack.Name} pack.");
    }

    public int Sell(Stack moving)
    {
        var pos = moving.Pos;
        int total = 0;
        foreach (var c in moving.Cards.ToList())
        {
            if (c.Def.Value <= 0 || c.Def.IsHostile || c.Def.Id == "energy") continue;
            total += c.Def.Value;
            Remove(c);
        }
        for (int i = 0; i < total; i++) Spawn("energy", pos + new Vector2(0, -CardH - 20));
        if (total > 0) Events.Add(SimEvent.Sell);
        return total;
    }

    // ---------- combat ----------

    static bool Attackable(Card c) => !c.Def.IsHostile && c.Def.Hp > 0;

    public void Attack(Stack moving, Card hostile)
    {
        var fighters = moving.Cards.Where(Attackable).ToList();
        if (fighters.Count == 0) return;
        foreach (var c in fighters) Remove(c);
        JoinBattle(fighters, hostile);
    }

    public void JoinBattleOf(Stack moving, Battle battle) => Attack(moving, battle.Hostiles.First());

    void JoinBattle(List<Card> players, Card hostile)
    {
        var battle = hostile.Battle;
        if (battle == null)
        {
            var pos = hostile.Stack?.Pos ?? Home.Center;
            Remove(hostile);
            battle = new Battle { Pos = pos };
            battle.Hostiles.Add(hostile);
            hostile.Battle = battle;
            Table.Battles.Add(battle);
            // Starbases and idle warships in the same star system rally to the fight.
            var here = SystemAt(pos + new Vector2(CardW / 2, CardH / 2));
            foreach (var s in Table.Stacks.Where(s => !s.Traveling && here != null && here.Contains(CardCenter(s))).ToList())
                foreach (var c in s.Cards.ToList())
                    if (c.Def.Id == "starbase" || (c.Def.HasTag("warship") && s.Active == null && !s.Dragging))
                    {
                        Remove(c);
                        players.Add(c);
                    }
        }
        foreach (var c in players.Distinct())
        {
            c.Battle = battle;
            c.AttackTimer = c.Def.AttackCd * (0.5f + 0.5f * (float)Rng.NextDouble());
            battle.Players.Add(c);
        }
    }

    float PlayerMult(Battle bt)
    {
        float m = 1f;
        foreach (var g in bt.Players.Where(p => p.Def.BoostTag == "combat").GroupBy(p => p.Def.Id)) m *= g.First().Def.BoostMult;
        return m;
    }

    void TickBattles(float dt)
    {
        foreach (var bt in Table.Battles.ToList())
        {
            float pm = PlayerMult(bt);
            foreach (var c in bt.Players.Concat(bt.Hostiles).ToList())
            {
                if (c.Battle != bt || c.Def.Attack <= 0) continue;
                c.AttackTimer -= dt;
                if (c.AttackTimer > 0) continue;
                c.AttackTimer = c.Def.AttackCd;
                var foes = c.Def.IsHostile ? bt.Players : bt.Hostiles;
                if (foes.Count == 0) break;
                var t = foes[Rng.Next(foes.Count)];
                int dmg = (int)MathF.Round(c.Def.Attack * (c.Def.IsHostile ? Diff.EnemyAttackMult : pm));
                t.Hp -= Math.Max(1, dmg);
                Events.Add(SimEvent.Hit);
                if (t.Hp <= 0) Kill(bt, t);
                if (State != RunState.Playing) return;
            }
            if (bt.Hostiles.Count == 0 || bt.Players.Count == 0) EndBattle(bt);
        }
    }

    void Kill(Battle bt, Card c)
    {
        Remove(c);
        if (c.Def.Id == "homeworld") { Lose("Your homeworld has fallen."); return; }
        if (!c.Def.IsHostile) { Messages.Add($"{Name(c.Def.Id)} was lost in battle."); return; }
        if (Defs.LootOf.TryGetValue(c.Def.Id, out var loot))
            foreach (var d in loot.Drops)
                for (int i = 0; i < d.N; i++) Spawn(d.Card, bt.Pos + new Vector2(CardW * 2, 0));
        if (c.Def.Id == Crisis.BossCard) { Win(); return; }
        if (c.Def.Id == Crisis.RiftCard) { Messages.Add($"The rift collapses... {Name(Crisis.BossCard)} comes in person!"); SpawnBoss(); }
        if (c.Def.HasTag("guardian")) Messages.Add($"The {Name(c.Def.Id)} is defeated! Its hoard is yours.");
    }

    void EndBattle(Battle bt)
    {
        Table.Battles.Remove(bt);
        int i = 0;
        foreach (var c in bt.Players.Concat(bt.Hostiles).ToList())
        {
            c.Battle = null;
            var s = NewStack(bt.Pos + new Vector2((i % 4) * (CardW + 16), (i / 4) * (CardH + 16)));
            Add(s, c);
            i++;
        }
    }

    void TickHostiles(float dt)
    {
        foreach (var c in Table.Stacks.SelectMany(s => s.Cards).Concat(Table.Battles.SelectMany(x => x.Hostiles)).Where(c => c.Def.IsHostile).ToList())
        {
            if (c.Def.Id == Crisis.RiftCard)
            {
                c.SpawnTimer -= dt;
                if (c.SpawnTimer <= 0)
                {
                    c.SpawnTimer = RiftSpawnEvery;
                    var pos = (c.Stack?.Pos ?? c.Battle?.Pos ?? Home.Center) + new Vector2(0, CardH + 40);
                    var m = Spawn(Crisis.MinionCard, pos);
                    m.AggroTimer = 2f;
                }
                continue;
            }
            if (c.Battle != null || c.Def.HasTag("guardian") || c.Def.Attack <= 0) continue;
            c.AggroTimer -= dt;
            if (c.AggroTimer > 0) continue;
            c.AggroTimer = Defs.Rules.EnemyAggroSeconds * (0.8f + 0.4f * (float)Rng.NextDouble());
            // Hostiles only go after cards in their own star system (or, in the gaps, anything close by).
            var me = CardCenter(c.Stack!);
            var mine = SystemAt(me);
            var targets = Table.Stacks.Where(s => !s.Dragging && !s.Traveling && s.Cards.Any(Attackable)
                && (mine != null ? mine.Contains(CardCenter(s)) : Vector2.Distance(CardCenter(s), me) < 900)).ToList();
            if (targets.Count == 0) continue;
            // Raiders go for your defenders first (warships, starbases), then whatever is closest.
            var ts = targets.OrderBy(s => s.Cards.Any(x => x.Def.HasTag("warship") || x.Def.Id == "starbase") ? 0 : 1)
                .ThenBy(s => Vector2.Distance(s.Pos, c.Stack!.Pos)).First();
            var fighters = ts.Cards.Where(Attackable).ToList();
            foreach (var f in fighters) Remove(f);
            JoinBattle(fighters, c);
        }
    }

    // ---------- moons and acts ----------

    void EndMoon()
    {
        Events.Add(SimEvent.MoonEnd);
        var people = AllCards.Where(c => c.Def.Category == "person").ToList();
        var food = AllCards.Where(c => c.Def.Id == "food").ToList();
        var energy = AllCards.Where(c => c.Def.Id == "energy").ToList();
        int starved = 0, shutdown = 0;
        foreach (var p in people)
        {
            if (p.Def.FoodUpkeep > 0)
            {
                if (food.Count >= p.Def.FoodUpkeep) { for (int i = 0; i < p.Def.FoodUpkeep; i++) { Remove(food[^1]); food.RemoveAt(food.Count - 1); } }
                else { Remove(p); starved++; continue; }
            }
            if (p.Def.EnergyUpkeep > 0)
            {
                if (energy.Count >= p.Def.EnergyUpkeep) { for (int i = 0; i < p.Def.EnergyUpkeep; i++) { Remove(energy[^1]); energy.RemoveAt(energy.Count - 1); } }
                else { Remove(p); shutdown++; }
            }
        }
        if (starved > 0) Messages.Add($"{starved} of your people starved. Grow more Food!");
        if (shutdown > 0) Messages.Add($"{shutdown} Drones shut down for lack of Energy.");
        if (!AllCards.Any(c => c.Def.Category == "person")) { Lose("No one is left to run your empire."); return; }

        Moon++;
        var homeC = Home.Center;
        if (Moon == Diff.Act2Moon)
        {
            Act = 2;
            for (int i = 0; i < 3; i++) Spawn("guardian_signal", homeC + new Vector2(-560 + i * 160, -400));
            Messages.Add("Act 2 - Guardians. Strange signals... Frontier and Strategic packs are now on sale.");
            Events.Add(SimEvent.Warning);
        }
        if (Moon == CrisisMoon && !RiftOpen)
        {
            Act = 3;
            RiftOpen = true;
            Spawn(Crisis.RiftCard, homeC + new Vector2(480, -400), jitter: false);
            Messages.Add($"Act 3 - {Crisis.Name}! {Crisis.Warning}");
            Events.Add(SimEvent.Warning);
        }
        if (RiftOpen && !BossArrived && Moon >= CrisisMoon + Diff.BossDelayMoons) SpawnBoss();
        if (Moon >= 3 && Moon % Diff.RaidEveryMoons == 0)
        {
            Spawn(Act >= 2 && Moon % (Diff.RaidEveryMoons * 2) == 0 ? "marauder_raider" : "pirate_raider", Home.Origin + new Vector2(60, 60));
            Messages.Add("Raiders have entered your capital system!");
        }
    }

    void SpawnBoss()
    {
        if (BossArrived) return;
        BossArrived = true;
        var boss = Spawn(Crisis.BossCard, Home.Center + new Vector2(560, -150), jitter: false);
        boss.AggroTimer = 6f;
        Messages.Add($"{Name(Crisis.BossCard)} has arrived. Destroy it to save the galaxy!");
        Events.Add(SimEvent.Warning);
    }

    void Win() { State = RunState.Won; EndReason = $"You destroyed {Name(Crisis.BossCard)} and saved the galaxy."; Events.Add(SimEvent.Win); }
    void Lose(string why) { if (State == RunState.Playing) { State = RunState.Lost; EndReason = why; Events.Add(SimEvent.Lose); } }

    // ---------- main tick ----------

    public void Update(float dt)
    {
        if (State != RunState.Playing) return;
        MoonTime += dt;
        if (MoonTime >= MoonSeconds) { MoonTime -= MoonSeconds; EndMoon(); }
        TickTravel(dt);
        TickRecipes(dt);
        if (State != RunState.Playing) return;
        TickBattles(dt);
        if (State != RunState.Playing) return;
        TickHostiles(dt);
        Separate(dt);
    }

    public static float StackHeight(Stack s) => CardH + StackStep * (s.Cards.Count - 1);

    void Separate(float dt)
    {
        var list = Table.Stacks;
        for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count; j++)
            {
                Stack a = list[i], c = list[j];
                if (a.Dragging || c.Dragging || a.Traveling || c.Traveling) continue;
                float ox = MathF.Min(a.Pos.X + CardW, c.Pos.X + CardW) - MathF.Max(a.Pos.X, c.Pos.X);
                float oy = MathF.Min(a.Pos.Y + StackHeight(a), c.Pos.Y + StackHeight(c)) - MathF.Max(a.Pos.Y, c.Pos.Y);
                if (ox <= 0 || oy <= 0) continue;
                var d = (c.Pos - a.Pos);
                if (d.LengthSquared() < 1) d = new Vector2(1, 0.3f);
                var push = Vector2.Normalize(d) * MathF.Min(ox, 60) * MathF.Min(1, dt * 6);
                a.Pos = Clamp(a.Pos - push / 2);
                c.Pos = Clamp(c.Pos + push / 2);
            }
    }
}
