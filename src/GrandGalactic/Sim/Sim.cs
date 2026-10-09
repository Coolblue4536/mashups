using System.Numerics;

namespace GrandGalactic;

/// <summary>The whole run: boards of card stacks, recipes, moons, combat, packs and the three acts. No rendering.</summary>
public sealed class Sim
{
    public const float CardW = 120, CardH = 160, StackStep = 30;
    public const float BoardW = 2600, BoardH = 1600;

    public readonly Random Rng;
    public readonly EthicDef Ethic;
    public readonly DifficultyDef Diff;
    public readonly int MoonSeconds;
    public readonly CrisisDef Crisis;
    public readonly List<Board> Boards = new();
    public readonly HashSet<string> Techs = new();
    public readonly HashSet<string> Discovered = new();
    public readonly List<string> Messages = new();
    public readonly List<SimEvent> Events = new();
    public readonly Func<string, string> Name;

    public int Moon = 1;
    public float MoonTime;
    public int Act = 1;
    public RunState State = RunState.Playing;
    public string EndReason = "";
    public bool RiftOpen, BossArrived;
    public int CrisisMoon;
    int _uid, _stackId, _guardianOpened;
    readonly List<string> _unusedNames = new(Defs.Rules.SystemNames);

    public Board Home => Boards[0];

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
        var home = AddBoard(homeSys);

        var center = new Vector2(BoardW / 2, BoardH / 2);
        var start = new List<string>();
        foreach (var a in Defs.Rules.StartCards) for (int i = 0; i < a.N; i++) start.Add(a.Card);
        for (int i = 0; i < Defs.Rules.StartWorkers; i++) start.Add(ethic.WorkerCard);
        foreach (var a in ethic.BonusCards) for (int i = 0; i < a.N; i++) start.Add(a.Card);
        foreach (var a in Diff.BonusCards) for (int i = 0; i < a.N; i++) start.Add(a.Card);
        int k = 0;
        foreach (var id in start)
        {
            var pos = center + new Vector2(-560 + (k % 8) * 150, -260 + (k / 8) * 200);
            var c = Spawn(home, id, pos, jitter: false);
            if (id == "homeworld") c.Claimed = true;
            k++;
        }
        foreach (var t in ethic.StartTechs) Techs.Add(t);
        OpenPack(Defs.Pack[ethic.FreePack], home, center + new Vector2(0, 380), free: true);
    }

    // ---------- cards and stacks ----------

    /// <summary>Open a board for a system row. Random rows roll their planets and extras and get a random name.</summary>
    public Board AddBoard(SystemDef sys)
    {
        string name = sys.Name;
        if (sys.Kind == "random")
        {
            int pick = Rng.Next(_unusedNames.Count);
            name = _unusedNames[pick];
            _unusedNames.RemoveAt(pick);
        }
        var b = new Board { Index = Boards.Count, Sys = sys, Name = name, Kind = sys.Kind == "random" ? sys.Name : "" };
        Boards.Add(b);

        // Stars across the top, everything else in a loose grid below.
        for (int i = 0; i < sys.StarCards.Length; i++)
            Spawn(b, sys.StarCards[i], new Vector2(BoardW / 2 - 80 + (i - (sys.StarCards.Length - 1) / 2f) * 260, 130), jitter: false);
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
            Spawn(b, cards[k], new Vector2(BoardW / 2 - 450 + (k % 5) * 220, BoardH / 2 - 380 + (k / 5) * 260), jitter: true);
        return b;
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

    public Card Spawn(Board b, string id, Vector2 pos, bool jitter = true)
    {
        var c = NewCard(id);
        var s = NewStack(b, pos + (jitter ? new Vector2(Rng.Next(-40, 41), Rng.Next(-40, 41)) : Vector2.Zero));
        Add(s, c);
        return c;
    }

    public Stack NewStack(Board b, Vector2 pos)
    {
        var s = new Stack { Id = ++_stackId, Pos = Clamp(pos) };
        b.Stacks.Add(s);
        return s;
    }

    static Vector2 Clamp(Vector2 p) => new(Math.Clamp(p.X, 0, BoardW - CardW), Math.Clamp(p.Y, 0, BoardH - CardH));

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
            if (s.Cards.Count == 0) BoardOf(s)?.Stacks.Remove(s);
        }
        if (c.Battle != null)
        {
            c.Battle.Players.Remove(c);
            c.Battle.Hostiles.Remove(c);
            c.Battle = null;
        }
    }

    public Board? BoardOf(Stack s) => Boards.FirstOrDefault(b => b.Stacks.Contains(s));

    public IEnumerable<Card> AllCards => Boards.SelectMany(b => b.AllCards);

    /// <summary>Lift card at index and everything above it into a new stack (Stacklands-style pick-up).</summary>
    public Stack Split(Stack s, int index)
    {
        if (index == 0) return s;
        var b = BoardOf(s)!;
        var ns = NewStack(b, s.Pos + new Vector2(0, index * StackStep));
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
        var b = BoardOf(moving)!;
        foreach (var c in moving.Cards.ToList()) { moving.Cards.Remove(c); Add(target, c); }
        b.Stacks.Remove(moving);
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

    void TickRecipes(Board b, float dt)
    {
        foreach (var s in b.Stacks.ToList())
        {
            if (s.Dirty)
            {
                s.Dirty = false;
                var m = FindMatch(s);
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
            if (s.Progress >= s.Duration) Complete(b, s);
        }
    }

    void Complete(Board b, Stack s)
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
                    Spawn(b, id, outPos + new Vector2(0, i * 12));
                    if (newTech) Messages.Add($"Researched {Name(id)}!");
                }
            }
        }
        switch (r.Effect)
        {
            case "set_flag:claimed":
                st.Claimed = true;
                Messages.Add($"{Name(st.Def.Id)} is now part of your empire.");
                break;
            case "open_board:random": OpenBoard("random", b, outPos); break;
            case "open_board:guardian": OpenBoard("guardian", b, outPos); break;
        }
        Discovered.Add(r.Id);
        Events.Add(SimEvent.Done);
    }

    void OpenBoard(string kind, Board from, Vector2 pos)
    {
        SystemDef? sys = kind == "guardian"
            ? Defs.Systems.Where(x => x.Kind == "guardian").Skip(_guardianOpened).FirstOrDefault()
            : RollSystemType();
        // Guardian boards always fit; random ones stop at the cap (leaving room for unopened guardians).
        int reserved = kind == "guardian" ? 0 : Defs.Systems.Count(x => x.Kind == "guardian") - _guardianOpened;
        if (sys == null || Boards.Count + reserved >= Defs.Rules.MaxSystems)
        {
            Messages.Add("The survey found only empty space - and a little salvage.");
            for (int i = 0; i < 4; i++) Spawn(from, "energy", pos);
            return;
        }
        if (kind == "guardian") _guardianOpened++;
        var b = AddBoard(sys);
        Messages.Add(kind == "guardian" ? $"Found {b.Name}!" : $"Surveyed {b.Name}: {b.Kind.ToLowerInvariant()}, {b.Stacks.Count(s => s.Root.Def.IsPlanet)} planets.");
    }

    T Roll<T>(IEnumerable<(T item, int weight)> table)
    {
        var list = table.ToList();
        int total = list.Sum(x => x.weight), r = Rng.Next(total);
        foreach (var (item, w) in list) { if (r < w) return item; r -= w; }
        return list[^1].item;
    }

    // ---------- packs, market, travel ----------

    public IEnumerable<PackDef> AvailablePacks => Defs.Packs.Where(p => p.UnlockAct <= Act);
    public int PackCost(PackDef p) => Math.Max(1, (int)MathF.Round(p.Cost * Diff.PackCostMult));
    float RiftSpawnEvery => Crisis.SpawnEvery * Diff.RiftSpawnMult;

    public bool BuyPack(Stack moving, PackDef pack, Vector2 spawnAt)
    {
        if (pack.UnlockAct > Act || moving.Cards.Any(c => c.Def.Id != "energy") || moving.Cards.Count < PackCost(pack)) return false;
        var b = BoardOf(moving)!;
        foreach (var c in moving.Cards.Take(PackCost(pack)).ToList()) Remove(c);
        OpenPack(pack, b, spawnAt, free: false);
        return true;
    }

    void OpenPack(PackDef pack, Board b, Vector2 at, bool free)
    {
        for (int i = 0; i < pack.Draws; i++)
            Spawn(b, Roll(pack.Contents.Select(e => (e.Card, e.Weight))), at + new Vector2((i - pack.Draws / 2f) * (CardW + 20), 0));
        Events.Add(SimEvent.PackOpen);
        if (free) Messages.Add($"Your {Ethic.Name} start: a free {pack.Name} pack.");
    }

    public int Sell(Stack moving)
    {
        var b = BoardOf(moving)!;
        var pos = moving.Pos;
        int total = 0;
        foreach (var c in moving.Cards.ToList())
        {
            if (c.Def.Value <= 0 || c.Def.IsHostile || c.Def.Id == "energy") continue;
            total += c.Def.Value;
            Remove(c);
        }
        for (int i = 0; i < total; i++) Spawn(b, "energy", pos + new Vector2(0, -CardH - 20));
        if (total > 0) Events.Add(SimEvent.Sell);
        return total;
    }

    public static bool CanTravel(Stack s) => s.Cards.Any(c => c.Def.HasTag("ship"));

    public bool MoveToBoard(Stack moving, Board to)
    {
        var from = BoardOf(moving)!;
        if (from == to || !CanTravel(moving)) return false;
        from.Stacks.Remove(moving);
        moving.Pos = Clamp(new Vector2(BoardW / 2 + Rng.Next(-200, 200), BoardH / 2 + 300));
        to.Stacks.Add(moving);
        moving.Dirty = true;
        return true;
    }

    // ---------- combat ----------

    static bool Attackable(Card c) => !c.Def.IsHostile && c.Def.Hp > 0;

    public void Attack(Stack moving, Card hostile)
    {
        var b = BoardOf(moving) ?? Boards.First(x => x.Battles.Any(bt => bt.Hostiles.Contains(hostile)));
        var fighters = moving.Cards.Where(Attackable).ToList();
        if (fighters.Count == 0) return;
        foreach (var c in fighters) Remove(c);
        JoinBattle(b, fighters, hostile);
    }

    public void JoinBattleOf(Stack moving, Battle battle) => Attack(moving, battle.Hostiles.First());

    void JoinBattle(Board b, List<Card> players, Card hostile)
    {
        var battle = hostile.Battle;
        if (battle == null)
        {
            var pos = hostile.Stack?.Pos ?? new Vector2(BoardW / 2, BoardH / 2);
            Remove(hostile);
            battle = new Battle { Board = b, Pos = pos };
            battle.Hostiles.Add(hostile);
            hostile.Battle = battle;
            b.Battles.Add(battle);
            // Starbases and idle warships on this board rally to the fight.
            foreach (var s in b.Stacks.ToList())
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

    void TickBattles(Board b, float dt)
    {
        foreach (var bt in b.Battles.ToList())
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
                if (t.Hp <= 0) Kill(b, bt, t);
                if (State != RunState.Playing) return;
            }
            if (bt.Hostiles.Count == 0 || bt.Players.Count == 0) EndBattle(b, bt);
        }
    }

    void Kill(Board b, Battle bt, Card c)
    {
        Remove(c);
        if (c.Def.Id == "homeworld") { Lose("Your homeworld has fallen."); return; }
        if (!c.Def.IsHostile) { Messages.Add($"{Name(c.Def.Id)} was lost in battle."); return; }
        if (Defs.LootOf.TryGetValue(c.Def.Id, out var loot))
            foreach (var d in loot.Drops)
                for (int i = 0; i < d.N; i++) Spawn(b, d.Card, bt.Pos + new Vector2(CardW * 2, 0));
        if (c.Def.Id == Crisis.BossCard) { Win(); return; }
        if (c.Def.Id == Crisis.RiftCard) { Messages.Add($"The rift collapses... {Name(Crisis.BossCard)} comes in person!"); SpawnBoss(); }
        if (c.Def.HasTag("guardian")) Messages.Add($"The {Name(c.Def.Id)} is defeated! Its hoard is yours.");
    }

    void EndBattle(Board b, Battle bt)
    {
        b.Battles.Remove(bt);
        int i = 0;
        foreach (var c in bt.Players.Concat(bt.Hostiles).ToList())
        {
            c.Battle = null;
            var s = NewStack(b, bt.Pos + new Vector2((i % 4) * (CardW + 16), (i / 4) * (CardH + 16)));
            Add(s, c);
            i++;
        }
    }

    void TickHostiles(Board b, float dt)
    {
        foreach (var c in b.Stacks.SelectMany(s => s.Cards).Concat(b.Battles.SelectMany(x => x.Hostiles)).Where(c => c.Def.IsHostile).ToList())
        {
            if (c.Def.Id == Crisis.RiftCard)
            {
                c.SpawnTimer -= dt;
                if (c.SpawnTimer <= 0)
                {
                    c.SpawnTimer = RiftSpawnEvery;
                    var pos = (c.Stack?.Pos ?? c.Battle?.Pos ?? new Vector2(BoardW / 2, BoardH / 2)) + new Vector2(0, CardH + 40);
                    var m = Spawn(b, Crisis.MinionCard, pos);
                    m.AggroTimer = 2f;
                }
                continue;
            }
            if (c.Battle != null || c.Def.HasTag("guardian") || c.Def.Attack <= 0) continue;
            c.AggroTimer -= dt;
            if (c.AggroTimer > 0) continue;
            c.AggroTimer = Defs.Rules.EnemyAggroSeconds * (0.8f + 0.4f * (float)Rng.NextDouble());
            var targets = b.Stacks.Where(s => !s.Dragging && s.Cards.Any(Attackable)).ToList();
            if (targets.Count == 0) continue;
            // Raiders go for your defenders first (warships, starbases), then whatever is closest.
            var ts = targets.OrderBy(s => s.Cards.Any(x => x.Def.HasTag("warship") || x.Def.Id == "starbase") ? 0 : 1)
                .ThenBy(s => Vector2.Distance(s.Pos, c.Stack!.Pos)).First();
            var fighters = ts.Cards.Where(Attackable).ToList();
            foreach (var f in fighters) Remove(f);
            JoinBattle(b, fighters, c);
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
        var homeC = new Vector2(BoardW / 2, BoardH / 2);
        if (Moon == Diff.Act2Moon)
        {
            Act = 2;
            for (int i = 0; i < 3; i++) Spawn(Home, "guardian_signal", homeC + new Vector2(-300 + i * 160, -500));
            Messages.Add("Act 2 - Guardians. Strange signals... Frontier and Strategic packs are now on sale.");
            Events.Add(SimEvent.Warning);
        }
        if (Moon == CrisisMoon && !RiftOpen)
        {
            Act = 3;
            RiftOpen = true;
            Spawn(Home, Crisis.RiftCard, homeC + new Vector2(500, -450), jitter: false);
            Messages.Add($"Act 3 - {Crisis.Name}! {Crisis.Warning}");
            Events.Add(SimEvent.Warning);
        }
        if (RiftOpen && !BossArrived && Moon >= CrisisMoon + Diff.BossDelayMoons) SpawnBoss();
        if (Moon >= 3 && Moon % Diff.RaidEveryMoons == 0)
        {
            Spawn(Home, Act >= 2 && Moon % (Diff.RaidEveryMoons * 2) == 0 ? "marauder_raider" : "pirate_raider", new Vector2(120, 120));
            Messages.Add("Raiders have entered your capital system!");
        }
    }

    void SpawnBoss()
    {
        if (BossArrived) return;
        BossArrived = true;
        var boss = Spawn(Home, Crisis.BossCard, new Vector2(BoardW / 2 + 650, BoardH / 2 - 200), jitter: false);
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
        foreach (var b in Boards.ToList())
        {
            if (State != RunState.Playing) return;
            TickRecipes(b, dt);
            TickBattles(b, dt);
            TickHostiles(b, dt);
            Separate(b, dt);
        }
    }

    public static float StackHeight(Stack s) => CardH + StackStep * (s.Cards.Count - 1);

    void Separate(Board b, float dt)
    {
        var list = b.Stacks;
        for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count; j++)
            {
                Stack a = list[i], c = list[j];
                if (a.Dragging || c.Dragging) continue;
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
