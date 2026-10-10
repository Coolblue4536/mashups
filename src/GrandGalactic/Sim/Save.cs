using System.Numerics;
using System.Text.Json;

namespace GrandGalactic;

/// <summary>A saved run, as plain data (sheet ids and numbers only).</summary>
public sealed class SaveData
{
    public int Version = 1;
    public string Ethic = "", Difficulty = "", Crisis = "";
    public int MoonSeconds, Moon, Act, CrisisMoon, Uid, StackId, GuardianOpened, NameRound, SysCount;
    public float MoonTime;
    public bool RiftOpen, BossArrived;
    public string Portrait = "", Species = "";
    public bool EnergyDeficit, Endless;
    public Dictionary<string, int> RepLevels = new();
    public List<EmpireSave> Empires = new();
    public List<string> Techs = new(), Discovered = new(), Flags = new(), Made = new(), Skipped = new(), UnusedNames = new();
    public Dictionary<string, int> Res = new();
    public List<SysSave> Systems = new();
    public List<StackSave> Stacks = new();
    public List<BattleSave> Battles = new();
}

public sealed class EmpireSave
{
    public string Id = "", Status = "peace", WarGoal = "";
    public bool Contacted, TheyDeclared;
    public int Intel, GoalSystem = -1, WarSince, HumiliatedUntil, LastRaid, Area = -1;
    public float IntelProgress, StrengthLoss;
    public List<string> SystemNames = new(), SystemPlanets = new();
    public List<bool> SystemCapital = new(), SystemOccupied = new();
}

public sealed class SysSave
{
    public string Sys = "", Name = "", Kind = "";
    public string? Owner;
    public int Index, SlotX, SlotY;
    public bool Claimed;
}

public sealed class CardSave
{
    public string Id = "";
    public float Hp, MaxHp, Shield, MaxShield, Armor, MaxArmor, AttackTimer, AggroTimer, SpawnTimer, Grow, Passive;
    public List<string> Parts = new();
    public CardSave? Admiral;
    public bool Claimed;
    public int Fleet;
    public string? EmpireId;
}

public sealed class StackSave
{
    public int Id;
    public float X, Y, FromX, FromY, ToX, ToY, TravelT, TravelDur, Progress;
    public string? Order, Active;
    public List<string> Queue = new();
    public List<CardSave> Cards = new();
}

public sealed class BattleSave
{
    public float X, Y;
    public int System = -1;
    public List<CardSave> Players = new(), Hostiles = new();
}

public sealed partial class Sim
{
    static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = false };
    public static string SavePath => Path.Combine(Paths.DataDir, "save.json");
    public static bool HasSave => File.Exists(SavePath);

    public SaveData ToSave()
    {
        CardSave C(Card c) => new()
        {
            Id = c.Def.Id, Hp = c.Hp, MaxHp = c.MaxHp, Shield = c.Shield, MaxShield = c.MaxShield, Armor = c.Armor, MaxArmor = c.MaxArmor,
            AttackTimer = c.AttackTimer, AggroTimer = c.AggroTimer, SpawnTimer = c.SpawnTimer, Grow = c.Grow, Passive = c.Passive, Parts = c.Parts.Select(p => p.Id).ToList(),
            Admiral = c.Admiral != null ? C(c.Admiral) : null, Claimed = c.Claimed, Fleet = c.Fleet, EmpireId = c.EmpireId,
        };
        return new SaveData
        {
            Ethic = Ethic.Id, Difficulty = Diff.Id, Crisis = Crisis.Id, MoonSeconds = MoonSeconds, Moon = Moon, Act = Act, CrisisMoon = CrisisMoon,
            Uid = _uid, StackId = _stackId, GuardianOpened = _guardianOpened, NameRound = _nameRound, SysCount = _sysCount, MoonTime = MoonTime,
            RiftOpen = RiftOpen, BossArrived = BossArrived, Techs = Techs.ToList(), Discovered = Discovered.ToList(), Flags = Flags.ToList(),
            Made = Made.ToList(), Skipped = SkippedSteps.ToList(), UnusedNames = _unusedNames.ToList(), Res = new(Res),
            Species = Species.Id, EnergyDeficit = EnergyDeficit, Endless = Endless, RepLevels = new(RepLevels),
            Empires = Empires.Select(e => new EmpireSave
            {
                Id = e.Def.Id, Status = e.Status, WarGoal = e.WarGoal, Contacted = e.Contacted, TheyDeclared = e.TheyDeclared, Intel = e.Intel,
                GoalSystem = e.GoalSystem, WarSince = e.WarSince, HumiliatedUntil = e.HumiliatedUntil, LastRaid = e.LastRaid, Area = e.Area,
                IntelProgress = e.IntelProgress, StrengthLoss = e.StrengthLoss, SystemNames = e.Systems.Select(x => x.Name).ToList(),
                SystemPlanets = e.Systems.Select(x => string.Join(",", x.Planets)).ToList(), SystemCapital = e.Systems.Select(x => x.Capital).ToList(),
                SystemOccupied = e.Systems.Select(x => x.Occupied).ToList(),
            }).ToList(),
            Systems = Systems.Select(z => new SysSave { Sys = z.Sys.Id, Name = z.Name, Kind = z.Kind, Index = z.Index, SlotX = z.Slot.X, SlotY = z.Slot.Y, Claimed = z.Claimed, Owner = z.Owner }).ToList(),
            Stacks = Table.Stacks.Select(s => new StackSave
            {
                Id = s.Id, X = (s.Glide ?? s.Pos).X, Y = (s.Glide ?? s.Pos).Y, FromX = s.TravelFrom.X, FromY = s.TravelFrom.Y, ToX = s.TravelTo.X, ToY = s.TravelTo.Y,
                TravelT = s.TravelT, TravelDur = s.TravelDur, Progress = s.Progress, Order = s.Order?.Id, Queue = s.Queue.Select(q => q.Id).ToList(), Active = s.Active?.Id, Cards = s.Cards.Select(C).ToList(),
            }).ToList(),
            Battles = Table.Battles.Select(b => new BattleSave
            {
                X = b.Pos.X, Y = b.Pos.Y, System = b.System?.Index ?? -1, Players = b.Players.Select(C).ToList(), Hostiles = b.Hostiles.Select(C).ToList(),
            }).ToList(),
        };
    }

    public void SaveTo(string path, string portrait = "")
    {
        var d = ToSave();
        d.Portrait = portrait;
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(d, Json));
        File.Move(tmp, path, true);
    }

    public static SaveData? ReadSave(string path)
    {
        try { return JsonSerializer.Deserialize<SaveData>(File.ReadAllText(path), Json); }
        catch (Exception e) { Log.Info($"save: could not read {path} ({e.Message})"); return null; }
    }

    /// <summary>Rebuild a run from a save.</summary>
    public Sim(SaveData d, Func<string, string>? name = null)
    {
        Rng = new Random(Environment.TickCount);
        Ethic = Defs.Ethics.First(e => e.Id == d.Ethic);
        Species = Defs.Species.FirstOrDefault(x => x.Id == d.Species) ?? Defs.Species.First(x => x.Id == Defs.Rules.DefaultSpecies);
        EnergyDeficit = d.EnergyDeficit; Endless = d.Endless;
        foreach (var kv in d.RepLevels) RepLevels[kv.Key] = kv.Value;
        foreach (var es in d.Empires)
        {
            if (Defs.Empires.FirstOrDefault(x => x.Id == es.Id) is not { } def) continue;
            var e = new Empire { Def = def, Status = es.Status, WarGoal = es.WarGoal, Contacted = es.Contacted, TheyDeclared = es.TheyDeclared, Intel = es.Intel,
                                 GoalSystem = es.GoalSystem, WarSince = es.WarSince, HumiliatedUntil = es.HumiliatedUntil, LastRaid = es.LastRaid, Area = es.Area,
                                 IntelProgress = es.IntelProgress, StrengthLoss = es.StrengthLoss };
            for (int i = 0; i < es.SystemNames.Count; i++)
            {
                var sys = new EmpireSystem { Name = es.SystemNames[i], Capital = es.SystemCapital.ElementAtOrDefault(i), Occupied = es.SystemOccupied.ElementAtOrDefault(i) };
                sys.Planets.AddRange((es.SystemPlanets.ElementAtOrDefault(i) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Where(Defs.Card.ContainsKey));
                e.Systems.Add(sys);
            }
            Empires.Add(e);
            if (e.Contacted) NewTradeOffers(e);
        }
        Diff = Defs.Difficulties.FirstOrDefault(x => x.Id == d.Difficulty) ?? Defs.DefaultDifficulty;
        Crisis = Defs.Crises.FirstOrDefault(x => x.Id == d.Crisis) ?? Defs.Crises[0];
        MoonSeconds = d.MoonSeconds;
        Name = name ?? (id => Defs.Card.TryGetValue(id, out var def) ? def.Name : id);
        Moon = d.Moon; Act = d.Act; CrisisMoon = d.CrisisMoon; MoonTime = d.MoonTime; RiftOpen = d.RiftOpen; BossArrived = d.BossArrived;
        _uid = d.Uid; _stackId = d.StackId; _guardianOpened = d.GuardianOpened; _nameRound = d.NameRound; _sysCount = d.SysCount;
        Techs.UnionWith(d.Techs); Discovered.UnionWith(d.Discovered); Flags.UnionWith(d.Flags); Made.UnionWith(d.Made); SkippedSteps.UnionWith(d.Skipped);
        _unusedNames.Clear(); _unusedNames.AddRange(d.UnusedNames);
        foreach (var kv in d.Res) Res[kv.Key] = kv.Value;
        var sysDefs = Defs.Systems.ToDictionary(x => x.Id);
        foreach (var z in d.Systems)
        {
            var origin = new Vector2(z.SlotX * (SysW + SysGap), z.SlotY * (SysH + SysGap));
            Systems.Add(new StarSystem { Index = z.Index, Sys = sysDefs[z.Sys], Name = z.Name, Kind = z.Kind, Origin = origin, Size = new Vector2(SysW, SysH),
                                         Slot = (z.SlotX, z.SlotY), Claimed = z.Claimed, Owner = z.Owner });
        }
        RecalcBounds();
        Card C(CardSave s)
        {
            var c = new Card { Uid = ++_uid, Def = Defs.Card[s.Id] };
            foreach (var p in s.Parts) if (Defs.Component.TryGetValue(p, out var comp)) c.Parts.Add(comp);
            Recalc(c);
            c.Hp = s.Hp; c.MaxHp = s.MaxHp; c.Shield = s.Shield; c.MaxShield = s.MaxShield; c.Armor = s.Armor; c.MaxArmor = s.MaxArmor;
            c.AttackTimer = s.AttackTimer; c.AggroTimer = s.AggroTimer; c.SpawnTimer = s.SpawnTimer; c.Grow = s.Grow; c.Passive = s.Passive; c.Claimed = s.Claimed; c.Fleet = s.Fleet;
            c.EmpireId = s.EmpireId;
            if (s.Admiral != null) c.Admiral = C(s.Admiral);
            return c;
        }
        var recipes = Defs.Recipes.ToDictionary(r => r.Id);
        foreach (var ss in d.Stacks)
        {
            var s = new Stack { Id = ss.Id, Pos = new Vector2(ss.X, ss.Y), TravelFrom = new Vector2(ss.FromX, ss.FromY), TravelTo = new Vector2(ss.ToX, ss.ToY),
                                TravelT = ss.TravelT, TravelDur = ss.TravelDur, Order = ss.Order != null ? recipes.GetValueOrDefault(ss.Order) : null };
            foreach (var q in ss.Queue) if (recipes.TryGetValue(q, out var qr)) s.Queue.Add(qr);
            foreach (var cs in ss.Cards) Add(s, C(cs));
            if (s.Cards.Count == 0) continue;
            Table.Stacks.Add(s);
            // Work in progress picks up where it was.
            if (ss.Active != null && FindMatch(s) is { } m && m.Recipe.Id == ss.Active)
            {
                s.Active = m.Recipe; s.ActiveStation = m.Station; s.Duration = m.Duration; s.Progress = Math.Min(ss.Progress, m.Duration); s.Dirty = false;
            }
        }
        foreach (var bs in d.Battles)
        {
            var bt = new Battle { Pos = new Vector2(bs.X, bs.Y), System = Systems.FirstOrDefault(z => z.Index == bs.System) };
            foreach (var cs in bs.Players) { var c = C(cs); c.Battle = bt; bt.Players.Add(c); }
            foreach (var cs in bs.Hostiles) { var c = C(cs); c.Battle = bt; bt.Hostiles.Add(c); }
            if (bt.Players.Count > 0 && bt.Hostiles.Count > 0) Table.Battles.Add(bt);
        }
    }
}
