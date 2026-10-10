using System.Numerics;

namespace GrandGalactic;

/// <summary>The whole run: one table of card stacks split into star-system areas, a shared resource pool, recipes,
/// moons, combat, packs and the three acts. No rendering.</summary>
public sealed partial class Sim
{
    public const float CardW = 120, CardH = 160, StackStep = 30;
    /// <summary>Size of one star-system area, and the gap between neighbouring areas.</summary>
    public const float SysW = 1500, SysH = 1000, SysGap = 140;

    public readonly Random Rng;
    public readonly EthicDef Ethic;
    public readonly DifficultyDef Diff;
    public readonly int MoonSeconds;
    public readonly CrisisDef Crisis;
    public readonly SpeciesDef Species;
    /// <summary>Levels of each repeatable (infinite) technology.</summary>
    public readonly Dictionary<string, int> RepLevels = new();
    /// <summary>Upkeep went unpaid at the last moon's end: work is slower and ships hit softer until it is paid.</summary>
    public bool EnergyDeficit;
    /// <summary>The crisis is beaten and the player chose to keep playing.</summary>
    public bool Endless;
    public readonly Board Table = new();
    public readonly List<StarSystem> Systems = new();
    public readonly HashSet<string> Techs = new();
    public readonly HashSet<string> Discovered = new();
    /// <summary>Milestones the tutorial checks (pack_bought, researched, traveled, fitted, admiral, battle_won, sold, opened_book).</summary>
    public readonly HashSet<string> Flags = new();
    /// <summary>Card ids a recipe has produced at least once.</summary>
    public readonly HashSet<string> Made = new();
    public readonly HashSet<string> SkippedSteps = new();
    public readonly List<string> Messages = new();
    public readonly List<SimEvent> Events = new();
    public readonly Func<string, string> Name;
    /// <summary>Systems added since the UI last looked (it scrolls to show them).</summary>
    public readonly List<StarSystem> NewSystems = new();
    /// <summary>The shared resource pool (Energy, Food, Minerals...): resources are counters, usable from any system.</summary>
    public readonly Dictionary<string, int> Res = new();
    /// <summary>Resources gained since the UI last looked, and where on the table (it floats them up to the counters).</summary>
    public readonly List<(string Id, int N, Vector2 At)> Gains = new();

    public int Moon = 1;
    public float MoonTime;
    public int Act = 1;
    public RunState State = RunState.Playing;
    public string EndReason = "";
    public bool RiftOpen, BossArrived;
    public int CrisisMoon;
    int _uid, _stackId, _guardianOpened, _nameRound, _sysCount;
    readonly List<string> _unusedNames = new(Defs.Rules.SystemNames);

    public StarSystem Home => Systems[0];
    public Vector2 BoundsMin { get; private set; }
    public Vector2 BoundsMax { get; private set; }

    public Sim(EthicDef ethic, int seed, Func<string, string>? name = null, DifficultyDef? difficulty = null, MoonLengthDef? moon = null, SpeciesDef? species = null)
    {
        Rng = new Random(seed);
        Ethic = ethic;
        Species = species ?? Defs.Species.First(x => x.Id == Defs.Rules.DefaultSpecies);
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
            if (!IsResource(id)) k++;
        }
        foreach (var t in ethic.StartTechs) Techs.Add(t);
        PickEmpires();
        OpenPack(Defs.Pack[ethic.FreePack], center + new Vector2(0, 340), free: true);
        Gains.Clear();
        foreach (var s in Table.Stacks) if (s.Glide is { } g) { s.Pos = g; s.Glide = null; } // the opening table is dealt, not slid
    }

    // ---------- the resource pool ----------

    public static bool IsResource(string id) => Defs.Card.TryGetValue(id, out var d) && d.Category == "resource";
    public int Have(string id) => Res.GetValueOrDefault(id);

    /// <summary>Add to the pool. <paramref name="made"/>: it was produced by work (the tutorial counts that).</summary>
    public void Gain(string id, int n, Vector2 at, bool made = true)
    {
        if (n <= 0) return;
        Res[id] = Have(id) + n;
        if (made) Made.Add(id);
        Gains.Add((id, n, at));
    }

    /// <summary>The resources a recipe takes from the pool.</summary>
    public static IEnumerable<RecipeInput> Costs(RecipeDef r) => r.Inputs.Where(i => IsResource(i.Card));

    /// <summary>What the pool is short of for a recipe ("Needs 3 Minerals (have 1)"), or null when it can pay.</summary>
    public string? Shortfall(RecipeDef r)
    {
        var miss = Costs(r).Where(i => Have(i.Card) < i.N).ToList();
        return miss.Count == 0 ? null : "Needs " + string.Join(", ", miss.Select(i => $"{i.N} {Name(i.Card)} (have {Have(i.Card)})"));
    }

    bool Pay(RecipeDef r)
    {
        if (Shortfall(r) != null) return false;
        foreach (var i in Costs(r)) Res[i.Card] -= i.N;
        return true;
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

    /// <summary>Add a star system as a new area of the table, in the nearest free slot. Random rows roll their planets
    /// and extras and get a random name.</summary>
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
        int n = 0;
        while (Systems.Any(x => x.Slot == SlotAt(n))) n++; // abandoned systems free their slot
        var slot = SlotAt(n);
        var origin = new Vector2(slot.x * (SysW + SysGap), slot.y * (SysH + SysGap));
        var z = new StarSystem { Index = _sysCount++, Sys = sys, Name = name, Kind = sys.Kind == "random" ? sys.Name : "", Origin = origin,
                                 Size = new Vector2(SysW, SysH), Slot = slot, Claimed = sys.Kind == "home" };
        Systems.Add(z);
        RecalcBounds();

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
        // A new system is laid out at once (nothing slides in from nowhere).
        foreach (var s in StacksIn(z)) if (s.Glide is { } g) { s.Pos = g; s.Glide = null; }
        return z;
    }

    void RecalcBounds()
    {
        BoundsMin = Systems.Select(z => z.Origin).Aggregate(Vector2.Min);
        BoundsMax = Systems.Select(z => z.Origin + z.Size).Aggregate(Vector2.Max);
    }

    /// <summary>The star system whose area holds a table position (null in the gaps between systems).</summary>
    public StarSystem? SystemAt(Vector2 p) => Systems.FirstOrDefault(z => z.Contains(p));

    public static Vector2 CardCenter(Stack s) => s.Pos + new Vector2(CardW / 2, CardH / 2);

    public IEnumerable<Stack> StacksIn(StarSystem z) => Table.Stacks.Where(s => !s.Traveling && z.Contains(CardCenter(s)));

    // ---------- claiming and abandoning systems ----------

    public int ClaimedCount => Systems.Count(z => z.Claimed);

    /// <summary>Why a system can't be claimed right now, or null when it can.</summary>
    public string? ClaimBlock(StarSystem z)
    {
        if (z.Claimed) return $"{z.Name} is already yours.";
        if (z.Owner != null) return $"{z.Name} belongs to the {EmpireOf(z.Owner)?.Def.Name}. Win a war to claim it.";
        if (ClaimedCount >= Defs.Rules.ClaimLimit)
            return $"Claim limit reached: you own {ClaimedCount}/{Defs.Rules.ClaimLimit} systems.";
        if (StacksIn(z).Any(s => s.HasHostile) || Table.Battles.Any(b => z.Contains(b.Pos)))
            return $"Clear the hostiles out of {z.Name} before claiming it.";
        return null;
    }

    /// <summary>When a stack looks like a claim attempt that can't happen, say why (once per change to the stack).</summary>
    string? ExplainBlockedClaim(Stack s)
    {
        if (!s.Cards.Any(c => c.Def.Category == "star") || !s.Cards.Any(c => c.Def.Id == "construction_ship")) return null;
        if (SystemAt(CardCenter(s)) is { } z && ClaimBlock(z) is { } why && !z.Claimed) { Messages.Add(why); return why; }
        return null;
    }

    static bool PlayerOwned(CardDef d) => d.Category is "person" or "structure" or "ship" or "component" or "tech" or "resource";

    /// <summary>Why an unclaimed system can't be abandoned (struck off the table), or null when it can.</summary>
    public string? AbandonBlock(StarSystem z)
    {
        if (z == Home) return "Your capital can't be abandoned.";
        if (z.Owner != null) return $"{z.Name} belongs to the {EmpireOf(z.Owner)?.Def.Name}.";
        if (z.Claimed) return $"{z.Name} is yours; only unclaimed systems can be abandoned.";
        if (Table.Battles.Any(b => b.System == z || z.Contains(b.Pos))) return $"Finish the battle in {z.Name} first.";
        if (Table.Stacks.Any(s => s.Traveling && z.Contains(s.TravelTo + new Vector2(CardW / 2, CardH / 2))))
            return $"A ship is on its way to {z.Name}.";
        if (StacksIn(z).SelectMany(s => s.Cards).FirstOrDefault(c => PlayerOwned(c.Def)) is { } mine)
            return $"Move your {Name(mine.Def.Id)} out of {z.Name} first.";
        return null;
    }

    /// <summary>Strike an unwanted, unclaimed system off the table: its planets, stars and monsters go with it, and its
    /// slot is free for the next survey.</summary>
    public string? Abandon(StarSystem z)
    {
        if (AbandonBlock(z) is { } why) return why;
        foreach (var s in StacksIn(z).ToList())
        {
            foreach (var c in s.Cards) c.Stack = null;
            Table.Stacks.Remove(s);
        }
        Systems.Remove(z);
        RecalcBounds();
        Messages.Add($"{z.Name} abandoned.");
        return null;
    }

    // ---------- travel between systems ----------

    /// <summary>What can cross between systems: a stack with a ship (or an Envoy, who travels on their own).</summary>
    public static bool HasShip(Stack s) => s.Cards.Any(c => c.Def.HasTag("ship") || c.Def.HasTag("envoy"));

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
        s.Glide = null;
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
                if (SystemAt(CardCenter(s)) is { } z)
                {
                    Messages.Add($"Arrived at {z.Name}.");
                    if (FreeSpot(z, s.Pos, StackHeight(s), s) is { } p) s.Glide = p;
                }
                Flags.Add("traveled");
            }
        }
    }

    /// <summary>Roll a random system type by weight (what a survey finds).</summary>
    public SystemDef RollSystemType() => Roll(Defs.Systems.Where(s => s.Kind == "random").Select(s => (s, s.Weight)));

    public Card NewCard(string id)
    {
        var def = Defs.Card[id];
        float m = def.IsHostile ? Diff.EnemyHpMult : 1f;
        var c = new Card { Uid = ++_uid, Def = def, AttackTimer = def.AttackCd, AggroTimer = Defs.Rules.EnemyAggroSeconds * (0.6f + (float)Rng.NextDouble()) };
        Recalc(c);
        RefreshStats(c);
        c.Hp = c.MaxHp; c.Shield = c.MaxShield; c.Armor = c.MaxArmor;
        if (def.IsPlanet && def.ColonizeWith == "none") c.Claimed = true;
        if (id == Crisis.RiftCard) c.SpawnTimer = RiftSpawnEvery * 0.5f;
        return c;
    }

    /// <summary>Put a new card on the table near <paramref name="pos"/> (it slides to the nearest free spot). Resources
    /// go into the pool instead; the returned card is then not on the table.</summary>
    public Card Spawn(string id, Vector2 pos, bool jitter = true)
    {
        if (IsResource(id))
        {
            Gain(id, 1, pos + new Vector2(CardW / 2, CardH / 2), made: false);
            return NewCard(id);
        }
        var c = NewCard(id);
        var home = SystemAt(pos + new Vector2(CardW / 2, CardH / 2));
        var want = pos + (jitter ? new Vector2(Rng.Next(-40, 41), Rng.Next(-40, 41)) : Vector2.Zero);
        var s = NewStack(want);
        Add(s, c);
        if (home != null) Place(s, home, ClampIn(want, home));
        return c;
    }

    public Stack NewStack(Vector2 pos)
    {
        var s = new Stack { Id = ++_stackId, Pos = Clamp(pos) };
        Table.Stacks.Add(s);
        return s;
    }

    public Vector2 Clamp(Vector2 p) => new(Math.Clamp(p.X, BoundsMin.X, BoundsMax.X - CardW), Math.Clamp(p.Y, BoundsMin.Y, BoundsMax.Y - CardH));

    /// <summary>Keep a card inside one star system (or the table, when it isn't in one).</summary>
    public Vector2 ClampIn(Vector2 p, StarSystem? z, float height = CardH) => z == null ? Clamp(p)
        : new(Math.Clamp(p.X, z.Origin.X, z.Origin.X + z.Size.X - CardW), Math.Clamp(p.Y, z.Origin.Y, MathF.Max(z.Origin.Y, z.Origin.Y + z.Size.Y - height)));

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
        s.Glide = null;
        if (index == 0) return s;
        var ns = NewStack(s.Pos + new Vector2(0, index * StackStep));
        var moving = s.Cards.Skip(index).ToList();
        foreach (var c in moving) { s.Cards.Remove(c); Add(ns, c); }
        s.Dirty = true;
        // A build order goes with its station.
        if (s.Order is { } o && !s.Cards.Any(c => StationOk(o, c)) && ns.Cards.Any(c => StationOk(o, c)))
        {
            ns.Order = o; s.Order = null;
            ns.Queue.AddRange(s.Queue); s.Queue.Clear();
        }
        return ns;
    }

    // ---------- fleets ----------

    /// <summary>Most warships one fleet (stack) can hold: grows with Fleet Doctrine research.</summary>
    public int FleetSize
    {
        get
        {
            int n = Defs.Rules.FleetSizeBase;
            foreach (var a in Defs.Rules.FleetSizeTechs) if (Techs.Contains(a.Card)) n = Math.Max(n, a.N);
            return n;
        }
    }

    public static int Warships(Stack s) => s.Cards.Count(c => c.Def.HasTag("warship"));

    /// <summary>A rough fighting strength: the square root of (damage per second x hit points incl. shields and armour).
    /// Fleets and hostiles use the same scale, so a fleet well above a threat usually wins (damage types still matter).</summary>
    public static int Strength(IEnumerable<Card> cards, float mult = 1f)
    {
        float dps = 0, ehp = 0;
        foreach (var c in cards)
        {
            dps += c.Guns.Sum(g => g.Damage / MathF.Max(0.1f, g.Cooldown));
            ehp += MathF.Max(0, c.Hp) + c.Shield + c.Armor;
        }
        return (int)MathF.Round(MathF.Sqrt(dps * mult * ehp));
    }

    /// <summary>A fleet's strength: its warships (and starbases), with its Admiral's bonus.</summary>
    public static int FleetStrength(Stack s) =>
        Strength(s.Cards.Where(c => c.Def.HasTag("warship") || c.Def.Id == "starbase"), HasAdmiral(s) ? Defs.Card["admiral"].BoostMult : 1f);

    public static int Threat(Card hostile) => Strength(new[] { hostile });

    /// <summary>The warship hulls in research order, for the "next hull" hint.</summary>
    public static readonly string[] HullLadder = { "tech_corvettes", "tech_destroyers", "tech_cruisers", "tech_battleships", "tech_titans" };

    /// <summary>The next hull technology to look for, or null when Titans are known.</summary>
    public string? NextHull => HullLadder.FirstOrDefault(t => !Techs.Contains(t));
    public static bool HasAdmiral(Stack s) => s.Cards.Any(c => c.Admiral != null);

    /// <summary>Why one stack can't go on another (empty when it simply isn't allowed, e.g. hostiles), or null when it can.</summary>
    public string? StackBlock(Stack moving, Stack target)
    {
        if (moving == target || moving.HasHostile || target.HasHostile) return "";
        if (moving.Cards.Count + target.Cards.Count > Defs.Rules.MaxStack) return "That stack is full.";
        if (Warships(moving) > 0 && Warships(moving) + Warships(target) > FleetSize)
            return Defs.Rules.FleetSizeTechs.FirstOrDefault(a => !Techs.Contains(a.Card)) is { } next
                ? $"A fleet holds at most {FleetSize} warships. Research {Name(next.Card)} for fleets of {next.N}."
                : $"A fleet holds at most {FleetSize} warships: split them into two fleets.";
        if (HasAdmiral(moving) && HasAdmiral(target)) return "That fleet already has an Admiral; each fleet has one.";
        return null;
    }

    public bool CanStack(Stack moving, Stack target) => StackBlock(moving, target) == null;

    public bool StackOnto(Stack moving, Stack target)
    {
        if (!CanStack(moving, target)) return false;
        foreach (var c in moving.Cards.ToList()) { moving.Cards.Remove(c); Add(target, c); }
        Table.Stacks.Remove(moving);
        if (moving.Order != null && target.Order == null) target.Order = moving.Order;
        Events.Add(SimEvent.Drop);
        return true;
    }

    // ---------- recipes ----------

    static bool Matches(string slot, Card c) => slot.StartsWith("tag:") ? c.Def.HasTag(slot[4..]) : c.Def.Id == slot;

    /// <summary>Recipes a station card offers in its build menu (order recipes it can be the station of).</summary>
    public IEnumerable<RecipeDef> OrdersFor(Card station) => Defs.Recipes.Where(r => r.Order && StationOk(r, station));

    /// <summary>Give a stack a build order (or clear it with null): it works on that recipe only, once its inputs are there.</summary>
    public void SetOrder(Stack s, RecipeDef? r)
    {
        s.Order = r;
        if (r == null) s.Queue.Clear();
        s.Active = null;
        s.Progress = 0;
        s.Dirty = true;
    }

    public const int QueueMax = 3;

    /// <summary>Order a build: it starts now if the station is free, else waits in its queue (up to 3 jobs in all).
    /// Work orders (repeating) replace each other. Returns why not, or null.</summary>
    public string? QueueOrder(Stack s, RecipeDef r)
    {
        if (s.Order == null || r.Tag != "build" || s.Order.Tag != "build") { SetOrder(s, r); return null; }
        if (1 + s.Queue.Count >= QueueMax) return $"The queue is full ({QueueMax} jobs). Cancel one first.";
        s.Queue.Add(r);
        return null;
    }

    bool Available(RecipeDef r, Stack s)
    {
        if (r.RequiresTech == "all" ? !AllBlueprintsKnown : r.RequiresTech != "none" && !Techs.Contains(r.RequiresTech)) return false;
        if (r.Effect == "learn" && Techs.Contains(r.Station)) return false;
        if (r.Effect == "contact" && !Empires.Any(e => !e.Contacted)) return false;
        if (r.RequiresSystem != "any")
        {
            var z = SystemAt(CardCenter(s));
            if (z == null || z.Claimed != (r.RequiresSystem == "claimed")) return false;
            if (r.Effect == "claim_system" && ClaimBlock(z) != null) return false;
        }
        if (r.Effect == "repair" && !s.Cards.Any(c => c.Def.HasTag("warship") && Damaged(c))) return false;
        return true;
    }

    /// <summary>The recipe a stack can work on now. A stack with an order (picked from a station's menu) only works on
    /// that; order recipes never start by themselves. Resource inputs come from the pool, not the stack.</summary>
    public Match? FindMatch(Stack s)
    {
        if (s.Cards.Count == 0 || s.HasHostile) return null;
        var list = s.Order != null ? new[] { s.Order } : Defs.Recipes.Where(r => !r.Order);
        foreach (var r in list)
        {
            if (!Available(r, s)) continue;
            foreach (var st in s.Cards)
            {
                if (!StationOk(r, st)) continue;
                var rest = s.Cards.Where(c => c != st).ToList();
                var consumed = new List<Card>();
                var kept = new List<Card>();
                bool ok = true;
                foreach (var inp in r.Inputs.Where(i => !IsResource(i.Card)).OrderBy(i => i.Card.StartsWith("tag:") ? 1 : 0))
                {
                    // Prefer the card that speeds this work up (a Scientist over a Pop for research).
                    var pool = rest.Where(c => Matches(inp.Card, c)).OrderByDescending(c => c.Def.BoostTag == r.Tag ? c.Def.BoostMult : 0).Take(inp.N).ToList();
                    if (pool.Count < inp.N) { ok = false; break; }
                    foreach (var c in pool) { rest.Remove(c); (inp.Keep ? kept : consumed).Add(c); }
                }
                if (!ok) continue;
                // Anything else in the stack must help (a booster), or be a Baby along for the ride.
                if (s.Order != r && rest.Any(c => c.Def.BoostTag != r.Tag && !c.Def.HasTag("baby"))) continue; // an ordered job ignores bystanders
                if (r.Outputs.Any(o => o.Give.Any(g => g.Card == "baby")) && rest.Any(c => c.Def.HasTag("baby"))) continue;
                float dur = r.Time < 0 ? st.Def.YieldTime : r.Time;
                foreach (var b in rest.Concat(kept)) if (b.Def.BoostTag == r.Tag) dur /= b.Def.BoostMult;
                dur /= SpeedMult(r, st);
                return new Match(r, st, consumed, dur);
            }
        }
        return null;
    }

    public static bool StationOk(RecipeDef r, Card st)
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

    /// <summary>A recipe input in words: "a Pop", "a Pop or Scientist", "2 Alloys".</summary>
    public string InputName(RecipeInput i) => i.Card switch
    {
        "tag:worker" => i.N > 1 ? $"{i.N} Pops" : "a Pop",
        "tag:researcher" => "a Pop or Scientist",
        "tag:warship" => "a damaged warship",
        var t when t.StartsWith("tag:") => t[4..],
        var id => i.N > 1 ? $"{i.N} {Name(id)}" : (IsResource(id) ? $"1 {Name(id)}" : $"a {Name(id)}"),
    };

    /// <summary>Why a stack that looks like it should be working isn't (shown above it), or null.</summary>
    string? Explain(Stack s)
    {
        if (s.Order is { } o)
        {
            if (!TechOk(o)) return o.RequiresTech == "all" ? "Opens once every blueprint is researched" : $"{o.Desc}: needs {Name(o.RequiresTech)} research";
            var station = s.Cards.FirstOrDefault(c => StationOk(o, c));
            var others = s.Cards.Where(c => c != station).ToList();
            var missing = new List<string>();
            foreach (var i in o.Inputs.Where(i => !IsResource(i.Card)))
            {
                int have = others.Count(c => Matches(i.Card, c));
                if (have < i.N) missing.Add(InputName(i with { N = i.N - have }));
                others.RemoveAll(c => Matches(i.Card, c));
            }
            if (missing.Count > 0) return $"Add {string.Join(" and ", missing)}";
            return null;
        }
        if (s.Cards.Any(GrowsBabies) && !s.Cards.Any(c => c.Def.HasTag("baby")) && s.Cards.Count(c => c.Def.Id == "pop") == 1)
            return "Add a second Pop (and have 3 Food) to raise a Baby";
        var bp = s.Cards.FirstOrDefault(c => c.Def.Category == "tech");
        if (bp != null && s.Cards.Any(c => c.Def.HasTag("researcher")))
        {
            if (Techs.Contains(bp.Def.Id)) return $"{Name(bp.Def.Id)} is already researched - sell this blueprint";
            var r = Defs.Recipes.FirstOrDefault(x => x.Effect == "learn" && x.Station == bp.Def.Id);
            if (r != null && r.RequiresTech != "none" && !Techs.Contains(r.RequiresTech)) return $"Research {Name(r.RequiresTech)} first";
            return "One blueprint and one researcher per stack";
        }
        return ExplainBlockedClaim(s);
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
                if (m == null || m.Value.Recipe != s.Active || m.Value.Station != s.ActiveStation)
                {
                    s.Active = m?.Recipe;
                    s.ActiveStation = m?.Station;
                    s.Progress = 0;
                    s.Duration = m?.Duration ?? 0;
                }
                else s.Duration = m.Value.Duration;
                s.Wait = m == null ? Explain(s) : null;
            }
            if (s.Active == null || s.Dragging) continue;
            // Work starts once the pool can pay for it, and is paid for when it finishes.
            var shortBy = Shortfall(s.Active);
            if (shortBy != null && s.Progress <= 0) { s.Wait = shortBy; continue; }
            s.Wait = null;
            s.Progress = MathF.Min(s.Duration, s.Progress + dt);
            if (s.Progress >= s.Duration) Complete(s);
        }
    }

    void Complete(Stack s)
    {
        var m = FindMatch(s);
        s.Dirty = true;
        if (m == null) { s.Active = null; s.Progress = 0; return; }
        var (r, st, consumed, _) = m.Value;
        if (!Pay(r)) { s.Wait = Shortfall(r); return; } // finished, waiting on the pool
        s.Progress = 0;
        var outPos = s.Pos + new Vector2(CardW + 30, 0);
        foreach (var c in consumed) Remove(c);
        if (!r.StationKeep) Remove(st);

        if (r.Outputs.Length > 0)
        {
            var o = Roll(r.Outputs.Select(x => (x, x.Weight)));
            foreach (var g in o.Give)
            {
                var id = g.Card == "station.yield" ? st.Def.Yield : g.Card;
                if (IsResource(id)) { Gain(id, g.N, CardCenter(s)); continue; }
                for (int i = 0; i < g.N; i++)
                {
                    Made.Add(id);
                    if (Defs.Card[id].HasTag("baby") && st.Stack != null) Add(st.Stack, NewCard(id)); // a Baby stays on its City District
                    else
                    {
                        var made = Spawn(id, outPos + new Vector2(0, i * 12));
                        if (made.Def.IsPlanet && st.Def.IsPlanet && st.Claimed) made.Claimed = true; // terraformed worlds stay yours
                    }
                }
            }
        }
        if (r.Effect.StartsWith("repeat:"))
        {
            var tid = r.Effect[7..];
            RepLevels[tid] = RepLevels.GetValueOrDefault(tid) + 1;
            Messages.Add($"{Name(tid).Replace(" (repeatable)", "")} level {RepLevels[tid]}: +{Defs.Rules.RepStepPct * RepLevels[tid]}% in all.");
            RefreshAll();
        }
        if (r.Effect == "contact") MakeContact();
        switch (r.Effect)
        {
            case "learn":
                {
                    Techs.Add(st.Def.Id);
                    Flags.Add("researched");
                    RefreshAll();
                    if (AllBlueprintsKnown) Messages.Add("Every blueprint is researched! Infinite research is open: click a Research Lab.");
                    var unlocked = Defs.Recipes.Where(x => x.RequiresTech == st.Def.Id && x.Effect != "learn").ToList();
                    var size = Defs.Rules.FleetSizeTechs.FirstOrDefault(a => a.Card == st.Def.Id);
                    Messages.Add($"Researched {Name(st.Def.Id)}! " + (size != null ? $"Fleets can now hold {size.N} warships."
                        : unlocked.Count == 1 ? $"New blueprint: {unlocked[0].Desc}" : unlocked.Count > 1 ? $"{unlocked.Count} new blueprints (Tab to view)." : ""));
                    if (Array.IndexOf(HullLadder, st.Def.Id) >= 0 && NextHull is { } nextHull)
                        Messages.Add($"Next bigger hull: {Name(nextHull)}. Look for its blueprint in the Military, Research or Frontier packs.");
                    break;
                }
            case "repair":
                foreach (var c in s.Cards.Where(c => c.Def.HasTag("warship") && Damaged(c)).Take(1))
                {
                    c.Hp = c.MaxHp;
                    c.Armor = c.MaxArmor;
                    Messages.Add($"{Name(c.Def.Id)} repaired.");
                }
                break;
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
        if (r.Order && r.Tag == "build") // a build order is one job (the next queued one starts); work orders repeat
        {
            s.Order = s.Queue.Count > 0 ? s.Queue[0] : null;
            if (s.Queue.Count > 0) s.Queue.RemoveAt(0);
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
            Gain("energy", 4, pos, made: false);
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

    // ---------- tutorial ----------

    public bool StepDone(TutorialStep t) => SkippedSteps.Contains(t.Id) || t.DoneWhen.Split('|').Any(CondDone);

    bool CondDone(string cond)
    {
        int c = cond.IndexOf(':');
        string kind = cond[..c], arg = cond[(c + 1)..];
        return kind switch
        {
            "recipe" => Discovered.Contains(arg),
            "made" => Made.Contains(arg),
            "has" => AllCards.Any(x => x.Def.Id == arg),
            "has_tag" => AllCards.Any(x => x.Def.HasTag(arg)),
            "flag" => Flags.Contains(arg),
            "claimed" => ClaimedCount >= int.Parse(arg),
            "act" => Act >= int.Parse(arg),
            _ => false,
        };
    }

    /// <summary>The first tutorial step not done yet (steps can be done in any order), or null when all are done.</summary>
    public TutorialStep? CurrentStep => Defs.Tutorial.FirstOrDefault(t => !StepDone(t));

    // ---------- blueprints ----------

    public enum BlueprintState { Made, Known, Locked }

    /// <summary>Every recipe is a blueprint: Made (done at least once), Known (can be done now), or Locked behind a
    /// technology. A research blueprint counts as Made once its technology is known.</summary>
    public BlueprintState Blueprint(RecipeDef r) =>
        Discovered.Contains(r.Id) || (r.Effect == "learn" && Techs.Contains(r.Station)) ? BlueprintState.Made
        : r.Effect != "learn" && TechOk(r) ? BlueprintState.Known
        : BlueprintState.Locked;

    public static string BlueprintTab(RecipeDef r) =>
        Defs.Rules.BlueprintTabs.FirstOrDefault(t => t.Prefixes.Any(p => r.Id.StartsWith(p)))?.Tab ?? "Other";

    /// <summary>Known blueprints that use a card (as the station or an input).</summary>
    public IEnumerable<RecipeDef> UsedIn(CardDef card) => Defs.Recipes.Where(r => Blueprint(r) != BlueprintState.Locked && (
        Uses(r.Station, card) || r.Inputs.Any(i => Uses(i.Card, card))));

    static bool Uses(string slot, CardDef card) =>
        slot == card.Id || (slot.StartsWith("tag:") && card.HasTag(slot[4..]));

    // ---------- packs and market ----------

    public IEnumerable<PackDef> AvailablePacks => Defs.Packs.Where(p => p.UnlockAct <= Act);
    public int PackCost(PackDef p) => Math.Max(1, (int)MathF.Round(p.Cost * Diff.PackCostMult));
    float RiftSpawnEvery => Crisis.SpawnEvery * Diff.RiftSpawnMult;

    /// <summary>Buy a pack with Energy from the pool; its cards land around <paramref name="spawnAt"/>. Returns why not, or null.</summary>
    public string? BuyPack(PackDef pack, Vector2 spawnAt)
    {
        if (pack.UnlockAct > Act) return $"{pack.Name} packs go on sale in Act {pack.UnlockAct}.";
        int cost = PackCost(pack);
        if (Have("energy") < cost) return $"{pack.Name} costs {cost} Energy (you have {Have("energy")}).";
        Res["energy"] -= cost;
        Flags.Add("pack_bought");
        OpenPack(pack, spawnAt, free: false);
        return null;
    }

    /// <summary>False for a blueprint you already know (or already hold), or can't research yet.</summary>
    bool UsefulDraw(string id)
    {
        if (Defs.Card[id].Category != "tech") return true;
        if (Techs.Contains(id) || AllCards.Any(c => c.Def.Id == id)) return false;
        var r = Defs.Recipes.FirstOrDefault(x => x.Effect == "learn" && x.Station == id);
        return r == null || r.RequiresTech == "none" || Techs.Contains(r.RequiresTech);
    }

    void OpenPack(PackDef pack, Vector2 at, bool free)
    {
        for (int i = 0; i < pack.Draws; i++)
        {
            // Blueprints lean toward ones you can research next: a known one, or one whose prerequisite you lack, is
            // re-rolled (twice at most), so tech chains like Corvettes > Destroyers > Cruisers can actually be climbed.
            var id = Roll(pack.Contents.Select(e => (e.Card, e.Weight)));
            for (int k = 0; k < 2 && !UsefulDraw(id); k++) id = Roll(pack.Contents.Select(e => (e.Card, e.Weight)));
            Spawn(id, at + new Vector2((i - pack.Draws / 2f) * (CardW + 20), 0));
        }
        Events.Add(SimEvent.PackOpen);
        if (free) Messages.Add($"Your {Ethic.Name} start: a free {pack.Name} pack.");
    }

    /// <summary>Energy the Market pays for n of a pooled resource (half its value, at least 1).</summary>
    public static int TradeValue(string id, int n) => Math.Max(1, Defs.Card[id].Value * n / 2);

    /// <summary>Sell surplus from the pool at the Market for Energy. Returns why not, or null.</summary>
    public string? SellResource(string id, int n)
    {
        if (!IsResource(id) || id == "energy") return "";
        if (Have(id) < n) return $"You only have {Have(id)} {Name(id)}.";
        Res[id] -= n;
        Gain("energy", TradeValue(id, n), Home.Center, made: false);
        Events.Add(SimEvent.Sell);
        Flags.Add("sold");
        return null;
    }

    public static int SellValue(Card c) => c.Def.IsHostile ? 0 : c.Def.Value + c.Parts.Sum(p => Defs.Card[p.Id].Value);

    public int Sell(Stack moving)
    {
        var at = CardCenter(moving);
        int total = 0;
        foreach (var c in moving.Cards.ToList())
        {
            if (SellValue(c) <= 0) continue;
            total += SellValue(c);
            if (c.Admiral != null) Spawn("admiral", moving.Pos); // the admiral steps off before the ship is sold
            Remove(c);
        }
        Gain("energy", total, at, made: false);
        if (total > 0) { Events.Add(SimEvent.Sell); Flags.Add("sold"); }
        return total;
    }

    // ---------- ship components and admirals ----------

    /// <summary>Rebuild a card's regen, evasion and guns from its sheet row and fitted parts.</summary>
    void Recalc(Card c)
    {
        c.ShieldRegen = c.Def.ShieldRegen + c.Parts.Sum(p => p.ShieldRegen);
        c.ArmorRegen = c.Parts.Sum(p => p.ArmorRegen);
        c.HullRegen = c.Def.HullRegen + c.Parts.Sum(p => p.HullRegen);
        c.Evasion = MathF.Min(0.6f, c.Def.Evasion + c.Parts.Sum(p => p.Evasion));
        c.Guns.Clear();
        if (c.Def.Attack > 0)
            c.Guns.Add(new Gun
            {
                Profile = c.Def.Weapon != "none" ? Defs.Component[c.Def.Weapon] : null,
                Damage = c.Def.Attack * (c.Def.IsHostile ? Diff.EnemyAttackMult : 1f),
                Cooldown = c.Def.AttackCd,
                Timer = c.Def.AttackCd,
            });
        foreach (var p in c.Parts.Where(p => p.Kind == "weapon"))
            c.Guns.Add(new Gun { Profile = p, Damage = p.Damage, Cooldown = p.Cooldown, Timer = p.Cooldown * (0.5f + 0.5f * (float)Rng.NextDouble()) });
    }

    public static bool CanFit(Card host) => !host.Def.IsHostile && host.Def.Slots > 0;

    /// <summary>Fit a component card into a warship or starbase. With every slot full it replaces a fitted part of the
    /// same kind (the old part comes back as a card). Returns why not, or null when fitted.</summary>
    public string? Fit(Card part, Card host)
    {
        if (part.Def.Category != "component" || !Defs.Component.TryGetValue(part.Def.Id, out var comp)) return null;
        if (!CanFit(host)) return $"Components fit on warships and starbases, not on {Name(host.Def.Id)}.";
        if (host.Battle != null) return "Ships can't be refitted mid-battle.";
        if (comp.MinSlots > host.Def.Slots) return $"{comp.Name} needs a bigger hull (Battleship or Titan).";
        if (host.Parts.Count >= host.Def.Slots)
        {
            // Refit: swap out the weakest fitted part of the same kind.
            int old = host.Parts.Select((p, i) => (p, i)).Where(x => x.p.Kind == comp.Kind && x.p.Id != comp.Id)
                .OrderBy(x => Defs.Card[x.p.Id].Value).Select(x => x.i).DefaultIfEmpty(-1).First();
            if (old < 0)
                return $"{Name(host.Def.Id)} has no free slots ({host.Def.Slots}/{host.Def.Slots}). Click it to take a part off, or drop a better {comp.Kind} on it to swap.";
            Unfit(host, old);
        }
        Remove(part);
        host.Parts.Add(comp);
        Recalc(host);
        float s0 = host.MaxShield, a0 = host.MaxArmor, h0 = host.MaxHp;
        RefreshStats(host);
        host.Shield += host.MaxShield - s0; host.Armor += host.MaxArmor - a0; host.Hp += host.MaxHp - h0;
        Flags.Add("fitted");
        if (host.Stack != null) host.Stack.Dirty = true;
        Events.Add(SimEvent.Done);
        return null;
    }

    /// <summary>Take a fitted part off a ship; it comes back as a card beside it. Returns why not, or null.</summary>
    public string? Unfit(Card host, int index)
    {
        if (index < 0 || index >= host.Parts.Count) return "";
        if (host.Battle != null) return "Ships can't be refitted mid-battle.";
        var comp = host.Parts[index];
        host.Parts.RemoveAt(index);
        Recalc(host);
        RefreshStats(host);
        host.Shield = MathF.Min(host.Shield, host.MaxShield); host.Armor = MathF.Min(host.Armor, host.MaxArmor);
        host.Hp = MathF.Max(1, MathF.Min(host.Hp, host.MaxHp));
        Spawn(comp.Id, (host.Stack?.Pos ?? Home.Center) + new Vector2(CardW + 30, 0), jitter: false);
        if (host.Stack != null) host.Stack.Dirty = true;
        return null;
    }

    /// <summary>Put an admiral in command of a fleet (the warships stacked together). Returns why not, or null when assigned.</summary>
    public string? AssignAdmiral(Card admiral, Card ship)
    {
        if (admiral.Def.Id != "admiral") return null;
        if (!ship.Def.HasTag("warship")) return "Admirals command fleets of warships.";
        if (ship.Admiral != null || (ship.Stack != null && HasAdmiral(ship.Stack))) return "That fleet already has an Admiral; each fleet has one.";
        var lead = ship.Stack?.Cards.First(c => c.Def.HasTag("warship")) ?? ship;
        Remove(admiral);
        lead.Admiral = admiral;
        Flags.Add("admiral");
        if (lead.Stack != null) lead.Stack.Dirty = true;
        Events.Add(SimEvent.Done);
        return null;
    }

    /// <summary>Relieve a fleet's admiral: they step off onto the table.</summary>
    public void RelieveAdmiral(Card ship)
    {
        if (ship.Admiral == null || ship.Battle != null) return;
        ship.Admiral = null;
        Spawn("admiral", (ship.Stack?.Pos ?? Home.Center) + new Vector2(CardW + 30, 0), jitter: false);
    }

    static bool Damaged(Card c) => c.Hp < c.MaxHp - 0.5f || c.Armor < c.MaxArmor - 0.5f;

    /// <summary>Shields recharge, regenerating armour and hulls heal, in and out of battle.</summary>
    void TickRegen(float dt)
    {
        foreach (var c in AllCards)
        {
            if (c.MaxShield > 0 && c.Shield < c.MaxShield)
                c.Shield = MathF.Min(c.MaxShield, c.Shield + MathF.Max(c.ShieldRegen, c.Battle == null ? 2f : 0f) * dt);
            if (c.ArmorRegen > 0) c.Armor = MathF.Min(c.MaxArmor, c.Armor + c.ArmorRegen * dt);
            if (c.HullRegen > 0) c.Hp = MathF.Min(c.MaxHp, c.Hp + c.HullRegen * dt);
        }
    }

    /// <summary>One shot: dodge, then shields, then armour, then hull, each scaled by the weapon's profile.</summary>
    public float Hit(Gun g, float mult, Card target, float flakCut)
    {
        if (Rng.NextDouble() < target.Evasion) return 0;
        var p = g.Profile;
        float dmg = g.Damage * mult;
        if (p?.Special == "arc") dmg *= 0.3f + 1.4f * (float)Rng.NextDouble();
        if (p?.IsExplosive == true) dmg *= 1 - flakCut;
        float vsS = p?.VsShield ?? 1, vsA = p?.VsArmor ?? 1, vsH = p?.VsHull ?? 1;
        float through = dmg * (p?.PierceShield ?? 0), atShield = dmg - through;
        if (target.Shield > 0 && atShield > 0)
        {
            float eff = atShield * vsS, absorbed = MathF.Min(eff, target.Shield);
            target.Shield -= absorbed;
            atShield = (eff - absorbed) / vsS;
        }
        float rest = through + atShield;
        float pastArmor = rest * (p?.PierceArmor ?? 0), atArmor = rest - pastArmor;
        if (target.Armor > 0 && atArmor > 0)
        {
            float eff = atArmor * vsA, absorbed = MathF.Min(eff, target.Armor);
            target.Armor -= absorbed;
            atArmor = (eff - absorbed) / vsA;
        }
        float hull = (pastArmor + atArmor) * vsH;
        target.Hp -= hull;
        return hull;
    }

    // ---------- combat ----------

    static bool Attackable(Card c) => !c.Def.IsHostile && c.Def.Hp > 0;

    public void Attack(Stack moving, Card hostile)
    {
        var fighters = moving.Cards.Where(Attackable).ToList();
        if (fighters.Count == 0) return;
        foreach (var c in fighters) { c.Fleet = moving.Id; Remove(c); }
        JoinBattle(fighters, hostile);
    }

    public void JoinBattleOf(Stack moving, Battle battle) => Attack(moving, battle.Hostiles.First());

    void JoinBattle(List<Card> players, Card hostile)
    {
        var battle = hostile.Battle;
        if (battle == null)
        {
            var pos = hostile.Stack?.Pos ?? Home.Center;
            var here = SystemAt(pos + new Vector2(CardW / 2, CardH / 2));
            Remove(hostile);
            battle = new Battle { Pos = pos, System = here };
            battle.Hostiles.Add(hostile);
            hostile.Battle = battle;
            Table.Battles.Add(battle);
            // Starbases and idle warships in the same star system rally to the fight.
            foreach (var s in Table.Stacks.Where(s => !s.Traveling && here != null && here.Contains(CardCenter(s))).ToList())
                foreach (var c in s.Cards.ToList())
                    if (c.Def.Id == "starbase" || (c.Def.HasTag("warship") && s.Active == null && !s.Dragging))
                    {
                        c.Fleet = s.Id;
                        Remove(c);
                        players.Add(c);
                    }
        }
        if (players.Any(c => c.Def.Id == "homeworld")) { Messages.Add("Your Homeworld is under attack! Send warships to defend it."); Events.Add(SimEvent.Warning); }
        foreach (var c in players.Distinct())
        {
            c.Battle = battle;
            foreach (var g in c.Guns) g.Timer = g.Cooldown * (0.5f + 0.5f * (float)Rng.NextDouble());
            battle.Players.Add(c);
        }
    }

    /// <summary>How hard one player card hits: a Titan's aura and an Admiral fighting in person boost everyone; a fleet's
    /// own Admiral boosts the ships of that fleet.</summary>
    public static float PlayerMult(Battle bt, Card c)
    {
        float m = 1f;
        foreach (var g in bt.Players.Where(p => p.Def.BoostTag == "combat" && p.Def.Id != "admiral").GroupBy(p => p.Def.Id)) m *= g.First().Def.BoostMult;
        if (bt.Players.Any(p => p.Def.Id == "admiral") || bt.Players.Any(p => p.Admiral != null && p.Fleet == c.Fleet)) m *= Defs.Card["admiral"].BoostMult;
        return m;
    }

    static float FlakCut(IEnumerable<Card> side) => MathF.Min(0.6f, 0.3f * side.Sum(c => c.Parts.Count(p => p.Special == "flak")));

    void TickBattles(float dt)
    {
        foreach (var bt in Table.Battles.ToList())
        {
            if (!TickBattle(bt, dt)) return;
            if (bt.Hostiles.Count == 0 || bt.Players.Count == 0) EndBattle(bt);
        }
    }

    /// <summary>One battle's shots for a tick. False when the run ended.</summary>
    bool TickBattle(Battle bt, float dt)
    {
        {
            float flakVsHostiles = FlakCut(bt.Players), flakVsPlayers = FlakCut(bt.Hostiles);
            foreach (var c in bt.Players.Concat(bt.Hostiles).ToList())
            {
                if (c.Battle != bt) continue;
                foreach (var g in c.Guns)
                {
                    g.Timer -= dt;
                    if (g.Timer > 0) continue;
                    g.Timer = g.Cooldown;
                    var foes = c.Def.IsHostile ? bt.Players : bt.Hostiles;
                    if (foes.Count == 0) break;
                    var t = foes[Rng.Next(foes.Count)];
                    Hit(g, c.Def.IsHostile ? 1f : PlayerMult(bt, c) * DamageMult, t, c.Def.IsHostile ? flakVsHostiles : flakVsPlayers);
                    Events.Add(SimEvent.Hit);
                    if (t.Hp <= 0) Kill(bt, t);
                    if (State != RunState.Playing) return false;
                    if (c.Battle != bt) break;
                }
            }
            // The Homeworld's planetary defences fire into every battle in the capital system (it isn't a target there).
            if (bt.System == Home && AllCards.FirstOrDefault(x => x.Def.Id == "homeworld" && x.Stack != null) is { } hw)
                foreach (var g in hw.Guns)
                {
                    g.Timer -= dt;
                    if (g.Timer > 0 || bt.Hostiles.Count == 0) continue;
                    g.Timer = g.Cooldown;
                    var t = bt.Hostiles[Rng.Next(bt.Hostiles.Count)];
                    Hit(g, 1f, t, 0);
                    Events.Add(SimEvent.Hit);
                    if (t.Hp <= 0) Kill(bt, t);
                    if (State != RunState.Playing) return false;
                }
        }
        return true;
    }

    void Kill(Battle bt, Card c)
    {
        Remove(c);
        if (c.Def.Id == "homeworld") { Lose("Your homeworld has fallen."); return; }
        if (!c.Def.IsHostile)
        {
            Messages.Add(c.Admiral != null ? $"{Name(c.Def.Id)} was lost in battle - and its Admiral with it." : $"{Name(c.Def.Id)} was lost in battle.");
            return;
        }
        if (Defs.LootOf.TryGetValue(c.Def.Id, out var loot))
            foreach (var d in loot.Drops)
                for (int i = 0; i < d.N; i++) Spawn(d.Card, bt.Pos + new Vector2(CardW * 2, 0));
        if (c.Def.Id == Crisis.BossCard && !Endless) { Win(); return; }
        if (c.Def.Id == Crisis.RiftCard) { Messages.Add($"The rift collapses... {Name(Crisis.BossCard)} comes in person!"); SpawnBoss(); }
        if (c.Def.HasTag("guardian")) Messages.Add($"The {Name(c.Def.Id)} is defeated! Its hoard is yours.");
    }

    void EndBattle(Battle bt)
    {
        Table.Battles.Remove(bt);
        if (bt.Hostiles.Count == 0 && bt.Players.Count > 0) Flags.Add("battle_won");
        // Survivors regroup into the stacks (fleets) they fought from; hostiles stand alone.
        var groups = bt.Players.GroupBy(c => c.Fleet).Select(g => g.ToList()).Concat(bt.Hostiles.Select(h => new List<Card> { h })).ToList();
        int i = 0;
        foreach (var g in groups)
        {
            var s = NewStack(bt.Pos + new Vector2((i % 4) * (CardW + 16), (i / 4) * (CardH + 16)));
            foreach (var c in g) { c.Battle = null; Add(s, c); }
            if (bt.System != null) Place(s, bt.System, s.Pos);
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
            if (c.Battle != null || c.Def.HasTag("guardian") || c.Def.Attack <= 0 || c.Stack == null) continue;
            c.AggroTimer -= dt;
            if (c.AggroTimer > 0) continue;
            c.AggroTimer = Defs.Rules.EnemyAggroSeconds * (0.8f + 0.4f * (float)Rng.NextDouble());
            // Hostiles only go after cards in their own star system (or, in the gaps, anything close by).
            var me = CardCenter(c.Stack);
            var mine = SystemAt(me);
            var targets = Table.Stacks.Where(s => !s.Dragging && !s.Traveling && s.Cards.Any(Attackable)
                && (mine != null ? mine.Contains(CardCenter(s)) : Vector2.Distance(CardCenter(s), me) < 900)).ToList();
            if (targets.Count == 0) continue;
            // Raiders go for your defenders first (warships, starbases), then whatever is closest.
            var ts = targets.OrderBy(s => s.Cards.Any(x => x.Def.HasTag("warship") || x.Def.Id == "starbase") ? 0 : 1)
                .ThenBy(s => Vector2.Distance(s.Pos, c.Stack.Pos)).First();
            var fighters = ts.Cards.Where(Attackable).ToList();
            foreach (var f in fighters) { f.Fleet = ts.Id; Remove(f); }
            JoinBattle(fighters, c);
        }
    }

    // ---------- babies ----------

    /// <summary>Where a Baby grows: a City District, or a colonised planet.</summary>
    public static bool GrowsBabies(Card c) => c.Def.Id == "city_district" || (c.Def.HasTag("colony") && c.Claimed);

    /// <summary>Colonised planets and outposts nobody works still yield, slowly (rules.colony_passive_mult).</summary>
    void TickColonies(float dt)
    {
        foreach (var s in Table.Stacks)
        {
            if (s.Traveling) continue;
            foreach (var c in s.Cards)
            {
                bool mega = c.Def.HasTag("megastructure");
                if (!mega && (!c.Def.IsPlanet || !c.Claimed || c.Def.ColonizeWith == "none" || c.Def.Yield == "none")) continue;
                if (!mega && s.Active?.Id == "w_yield" && s.ActiveStation == c) { c.Passive = 0; continue; } // worked: the Pop is faster
                c.Passive += dt * Mult(c.Def.Yield) / (EnergyDeficit ? Defs.Rules.EnergyDeficitWorkPct / 100f : 1f);
                if (c.Passive < c.Def.YieldTime * (mega ? 1 : Defs.Rules.ColonyPassiveMult)) continue;
                c.Passive = 0;
                Gain(c.Def.Yield, 1, CardCenter(s));
            }
        }
    }

    /// <summary>Babies on a City District or a colony grow; after baby_grow_seconds one becomes a Pop beside it.</summary>
    void TickBabies(float dt)
    {
        foreach (var s in Table.Stacks.Where(s => !s.Traveling && !s.Dragging && s.Cards.Any(c => c.Def.HasTag("baby"))).ToList())
        {
            if (!s.Cards.Any(GrowsBabies)) continue;
            foreach (var b in s.Cards.Where(c => c.Def.HasTag("baby")).ToList())
            {
                b.Grow += dt * Mult("babies");
                if (b.Grow < Defs.Rules.BabyGrowSeconds) continue;
                var at = s.Pos + new Vector2(CardW + 30, 0);
                Remove(b);
                Spawn("pop", at, jitter: false);
                Made.Add("pop");
                Messages.Add("A Baby has grown up: a new Pop joins your empire!");
                Events.Add(SimEvent.Done);
            }
        }
    }

    // ---------- moons and acts ----------

    /// <summary>20 seconds before a moon ends: say so if the pool can't feed (or power) everyone.</summary>
    void WarnUpkeep()
    {
        if (Moon < Defs.Rules.UpkeepFromMoon) return;
        var people = AllCards.Where(c => c.Def.Category == "person").ToList();
        int eat = people.Sum(c => c.Def.FoodUpkeep), power = people.Sum(c => c.Def.EnergyUpkeep);
        if (Have("food") < eat) { Messages.Add($"Food is short: {Have("food")} of {eat} needed at the end of this moon. Put Pops on farms, or some will starve!"); Events.Add(SimEvent.Warning); }
        int need = power + StructureUpkeep;
        if (Have("energy") < need) { Messages.Add($"Energy is short: {Have("energy")} of {need} needed at the end of this moon (buildings, fleets{(power > 0 ? ", Drones" : "")}). Make more Energy or sell something!"); Events.Add(SimEvent.Warning); }
    }

    void EndMoon()
    {
        Events.Add(SimEvent.MoonEnd);
        var people = AllCards.Where(c => c.Def.Category == "person").ToList();
        int starved = 0, shutdown = 0;
        if (Moon < Defs.Rules.UpkeepFromMoon)
            Messages.Add("Your people lived on rations this moon. From now on everyone eats at the end of each moon: grow Food!");
        else
            foreach (var p in people)
            {
                if (p.Def.FoodUpkeep > 0)
                {
                    if (Have("food") >= p.Def.FoodUpkeep) Res["food"] -= p.Def.FoodUpkeep;
                    else { Remove(p); starved++; continue; }
                }
                if (p.Def.EnergyUpkeep > 0)
                {
                    if (Have("energy") >= p.Def.EnergyUpkeep) Res["energy"] -= p.Def.EnergyUpkeep;
                    else if (shutdown == 0) { Remove(p); shutdown++; } // one Drone goes offline per moon; the rest run on reserve power
                }
            }
        // Buildings and fleets cost Energy each moon; unpaid upkeep means a deficit (slower work, weaker ships) next moon.
        int upkeep = StructureUpkeep;
        if (Moon >= Defs.Rules.UpkeepFromMoon && upkeep > 0)
        {
            bool was = EnergyDeficit;
            if (Have("energy") >= upkeep) { Res["energy"] -= upkeep; EnergyDeficit = false; }
            else
            {
                Res["energy"] = 0;
                EnergyDeficit = true;
                Messages.Add($"Energy deficit: your buildings and fleets need {upkeep} Energy a moon. Work is slower and ships hit softer until you pay it.");
                Events.Add(SimEvent.Warning);
            }
            if (was != EnergyDeficit) foreach (var st in Table.Stacks) st.Dirty = true;
        }
        if (starved > 0) Messages.Add($"{starved} of your people starved. Grow more Food!");
        if (shutdown > 0) Messages.Add("A Drone shut down for lack of Energy. Put Drones on Energy work, or more will follow each moon!");
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
        EmpiresMoon();
        if (Moon >= Defs.Rules.FirstRaidMoon && Moon % Diff.RaidEveryMoons == 0)
        {
            Spawn(Moon >= Defs.Rules.MarauderMoon && Moon % (Diff.RaidEveryMoons * 2) == 0 ? "marauder_raider" : "pirate_raider", Home.Origin + new Vector2(60, 60));
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

    /// <summary>Set by the UI while the tutorial is shown.</summary>
    public bool TutorialOn;

    /// <summary>With the tutorial on, the first moon can wait for a tutorial step (rules.tutorial_moon_waits_for; "none" = time always runs).</summary>
    public bool MoonHeld => TutorialOn && Moon == 1 && Defs.Tutorial.FirstOrDefault(t => t.Id == Defs.Rules.TutorialMoonWaitsFor) is { } step && !StepDone(step);

    public void Update(float dt)
    {
        if (State != RunState.Playing) return;
        float before = MoonTime;
        if (!MoonHeld) MoonTime += dt;
        if (before < MoonSeconds - 20 && MoonTime >= MoonSeconds - 20) WarnUpkeep();
        if (MoonTime >= MoonSeconds) { MoonTime -= MoonSeconds; EndMoon(); }
        TickTravel(dt);
        TickRegen(dt);
        TickRecipes(dt);
        TickBabies(dt);
        TickColonies(dt);
        TickIntel(dt);
        if (State != RunState.Playing) return;
        TickBattles(dt);
        if (State != RunState.Playing) return;
        TickHostiles(dt);
        Layout(dt);
    }

    public static float StackHeight(Stack s) => CardH + StackStep * (s.Cards.Count - 1);

    // ---------- layout: nothing overlaps, nothing jumps ----------

    /// <summary>Space kept between cards, and the room above a stack for its progress bar.</summary>
    public const float Gap = 14, BarRoom = 22;
    /// <summary>Fastest a card slides across the table (table units per second).</summary>
    public const float GlideMax = 1400;

    /// <summary>The table area a battle takes: its banner and its two rows of cards.</summary>
    public static (Vector2 Pos, Vector2 Size) BattleArea(Battle bt)
    {
        int n = Math.Max(bt.Players.Count, bt.Hostiles.Count);
        return (bt.Pos - new Vector2(20, 40), new Vector2(Math.Max(2, n) * (CardW + 12) + 30, CardH * 2 + 90));
    }

    /// <summary>A system's name and kind are written in its top-left corner; cards keep clear of it.</summary>
    public static (Vector2 Pos, Vector2 Size) TitleArea(StarSystem z) => (z.Origin, new Vector2(560, 130));

    /// <summary>A stack's footprint at a position: its cards, plus room above for the progress bar while it is working.</summary>
    public static (Vector2 Pos, Vector2 Size) StackArea(Stack s, Vector2? at = null)
    {
        float top = s.Active != null || s.Wait != null ? BarRoom : 4;
        return ((at ?? s.Pos) - new Vector2(0, top), new Vector2(CardW, StackHeight(s) + top));
    }

    public static bool Hit((Vector2 Pos, Vector2 Size) a, (Vector2 Pos, Vector2 Size) b, float gap) =>
        a.Pos.X < b.Pos.X + b.Size.X + gap && b.Pos.X < a.Pos.X + a.Size.X + gap && a.Pos.Y < b.Pos.Y + b.Size.Y + gap && b.Pos.Y < a.Pos.Y + a.Size.Y + gap;

    bool Inside(Stack s, Vector2 pos, StarSystem z) =>
        pos.X >= z.Origin.X && pos.X <= z.Origin.X + z.Size.X - CardW && pos.Y - BarRoom >= z.Origin.Y && pos.Y + StackHeight(s) <= z.Origin.Y + z.Size.Y;

    /// <summary>The smallest move that takes area a clear of area b (with the gap), preferring moves that keep the stack in its system.</summary>
    Vector2 PushOut(Stack s, (Vector2 Pos, Vector2 Size) a, (Vector2 Pos, Vector2 Size) b, StarSystem z)
    {
        if (!Hit(a, b, Gap)) return Vector2.Zero;
        var moves = new[]
        {
            new Vector2(b.Pos.X - Gap - (a.Pos.X + a.Size.X), 0), new Vector2(b.Pos.X + b.Size.X + Gap - a.Pos.X, 0),
            new Vector2(0, b.Pos.Y - Gap - (a.Pos.Y + a.Size.Y)), new Vector2(0, b.Pos.Y + b.Size.Y + Gap - a.Pos.Y),
        }.OrderBy(m => m.LengthSquared()).ToList();
        return moves.FirstOrDefault(m => Inside(s, s.Pos + m, z), moves[0]);
    }

    static Vector2 Cap(Vector2 v, float max) => v.Length() > max ? v / v.Length() * max : v;

    IEnumerable<(Vector2 Pos, Vector2 Size)> FixedAreas(StarSystem z) =>
        Table.Battles.Where(b => b.System == z).Select(BattleArea).Append(TitleArea(z));

    /// <summary>The free spot nearest to <paramref name="want"/> inside z for a stack of this height, or null when z is
    /// full. Stacks already sliding somewhere count as being there.</summary>
    public Vector2? FreeSpot(StarSystem z, Vector2 want, float height, Stack? except = null)
    {
        var taken = new List<(Vector2 Pos, Vector2 Size)>();
        foreach (var s in Table.Stacks)
            if (s != except && !s.Traveling && !s.Dragging && z.Contains((s.Glide ?? s.Pos) + new Vector2(CardW / 2, CardH / 2))) taken.Add(StackArea(s, s.Glide));
        taken.AddRange(FixedAreas(z));
        // Candidate spots on a fine grid, nearest first; the first one that fits wins.
        var spots = new List<(float d, Vector2 p)>();
        for (float y = z.Origin.Y + BarRoom; y <= z.Origin.Y + z.Size.Y - height; y += (CardH + Gap) / 4)
            for (float x = z.Origin.X; x <= z.Origin.X + z.Size.X - CardW; x += (CardW + Gap) / 3)
                spots.Add((Vector2.DistanceSquared(new Vector2(x, y), want), new Vector2(x, y)));
        spots.Sort((a, b) => a.d.CompareTo(b.d));
        foreach (var (_, p) in spots)
        {
            var area = (p - new Vector2(0, BarRoom), new Vector2(CardW, height + BarRoom));
            bool free = true;
            foreach (var o in taken) if (Hit(area, o, Gap)) { free = false; break; }
            if (free) return p;
        }
        return null;
    }

    /// <summary>A new or arriving stack appears where it was made and slides to the nearest spot where it overlaps nothing.</summary>
    void Place(Stack s, StarSystem z, Vector2 want)
    {
        s.Pos = ClampIn(want, z, StackHeight(s));
        if (FreeSpot(z, want, StackHeight(s), s) is { } p && Vector2.DistanceSquared(p, s.Pos) > 1) s.Glide = p;
    }

    /// <summary>Every tick: sliding stacks move toward their spot, battles stay inside their system and clear of each
    /// other, and resting stacks are nudged apart. A stack still truly overlapping something after a while slides to the
    /// nearest free spot; nothing ever jumps.</summary>
    void Layout(float dt)
    {
        var slid = new HashSet<Stack>();
        foreach (var s in Table.Stacks)
        {
            if (s.Glide is not { } g || s.Dragging || s.Traveling) continue;
            slid.Add(s);
            var d = g - s.Pos;
            float len = d.Length(), step = MathF.Min(MathF.Max(len * MathF.Min(1, dt * 6), 400 * dt), GlideMax * dt);
            if (len <= step) { s.Pos = g; s.Glide = null; }
            else s.Pos += d / len * step;
        }
        for (int i = 0; i < Table.Battles.Count; i++)
        {
            var b = Table.Battles[i];
            if (b.System is not { } z) continue;
            var (bp, bs) = BattleArea(b);
            for (int j = 0; j < i; j++)
                if (Table.Battles[j].System == z && BattleArea(Table.Battles[j]) is var o && Hit((bp, bs), o, Gap))
                    bp.Y = o.Pos.Y + o.Size.Y + Gap;
            bp = new Vector2(Math.Clamp(bp.X, z.Origin.X, MathF.Max(z.Origin.X, z.Origin.X + z.Size.X - bs.X)),
                             Math.Clamp(bp.Y, z.Origin.Y, MathF.Max(z.Origin.Y, z.Origin.Y + z.Size.Y - bs.Y)));
            b.Pos = bp + new Vector2(20, 40);
        }
        float k = MathF.Min(1, dt * 10);
        foreach (var z in Systems)
        {
            var group = new List<Stack>();
            foreach (var s in Table.Stacks)
                if (!s.Dragging && !s.Traveling && !slid.Contains(s) && z.Contains(CardCenter(s))) group.Add(s);
            if (group.Count == 0) continue;
            var fixedAreas = FixedAreas(z).ToList();
            foreach (var s in group) s.Pos = ClampIn(s.Pos, z, StackHeight(s)); // piles grow downwards: keep them inside
            // Gentle nudges: a fraction of the way each tick, so cards drift apart instead of snapping.
            for (int i = 0; i < group.Count; i++)
                foreach (var area in fixedAreas)
                {
                    var m = PushOut(group[i], StackArea(group[i]), area, z);
                    if (m != Vector2.Zero) group[i].Pos = ClampIn(group[i].Pos + Cap(m * (k + 0.02f), GlideMax * dt), z, StackHeight(group[i]));
                }
            for (int i = 0; i < group.Count; i++)
                for (int j = i + 1; j < group.Count; j++)
                {
                    Stack a = group[i], c = group[j];
                    var m = PushOut(a, StackArea(a), StackArea(c), z);
                    if (m == Vector2.Zero) continue;
                    a.Pos = ClampIn(a.Pos + Cap(m * (k / 2 + 0.01f), GlideMax * dt / 2), z, StackHeight(a));
                    c.Pos = ClampIn(c.Pos - Cap(m * (k / 2 + 0.01f), GlideMax * dt / 2), z, StackHeight(c));
                }
            // Only a real overlap that won't resolve (a card wedged between others, a battle or the border) moves a
            // stack on, and then it slides. In a full system it waits a while before looking again.
            for (int i = 0; i < group.Count; i++)
            {
                var s = group[i];
                var area = StackArea(s);
                bool overlap = fixedAreas.Any(a => Hit(area, a, 0));
                for (int j = 0; j < group.Count && !overlap; j++) overlap = j != i && Hit(area, StackArea(group[j]), 0);
                s.Jam = overlap ? s.Jam + dt : s.Jam < 0 ? MathF.Min(0, s.Jam + dt) : 0;
                if (s.Jam < 1.2f) continue;
                if (FreeSpot(z, s.Pos, StackHeight(s), s) is { } p) { s.Glide = p; s.Jam = 0; }
                else s.Jam = -3f;
            }
        }
    }
}
