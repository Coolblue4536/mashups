namespace GrandGalactic;

// One record type per sheet; the rows themselves are generated into Generated/Defs.g.cs.

public sealed record CategoryDef(string Id, string Label, string Color, string FrameRef, bool Draggable, bool IsCombatantDefault);

public sealed record CardDef(string Id, string NameLoc, string Name, string Category, string[] Tags, string Art, int Value,
    int Hp, int Attack, float AttackCd, float Shield, float Armor, float ShieldRegen, float HullRegen, float Evasion, int Slots, string Weapon,
    int FoodUpkeep, int EnergyUpkeep, string BoostTag, float BoostMult, string Yield,
    float YieldTime, string ColonizeWith, string Desc)
{
    public bool HasTag(string tag) => Array.IndexOf(Tags, tag) >= 0;
    public bool IsHostile => Category is "enemy" or "boss";
    public bool IsPlanet => Category == "planet";
}

public sealed record ComponentDef(string Id, string NameLoc, string Name, string Kind, string TechNameLoc, string TechName, string RequiresTech,
    int Damage, float Cooldown, float VsShield, float VsArmor, float VsHull, float PierceShield, float PierceArmor, float Shield, float ShieldRegen,
    float Armor, float ArmorRegen, float Hull, float HullRegen, float Evasion, string Special, int MinSlots, string Desc)
{
    /// <summary>Torpedoes and missiles: they fly past shields but not armour, and Flak can shoot them down.</summary>
    public bool IsExplosive => PierceShield >= 1 && PierceArmor < 1;
}

public sealed record Amount(string Card, int N);
public sealed record TutorialStep(string Id, string Text, string Hint, string DoneWhen);
public sealed record BlueprintTab(string Tab, string[] Prefixes);
public sealed record RecipeInput(string Card, int N, bool Keep);
public sealed record Outcome(int Weight, Amount[] Give);

public sealed record RecipeDef(string Id, string Station, bool StationKeep, RecipeInput[] Inputs, string RequiresFlag,
    string RequiresSystem, string RequiresTech, float Time, string Tag, Outcome[] Outputs, string Effect, string Desc);

public sealed record LootDef(string Card, Amount[] Drops);
public sealed record PackEntry(string Card, int Weight);
public sealed record PackDef(string Id, string Name, int Cost, int Draws, int UnlockAct, string Art, PackEntry[] Contents, string Desc);

public sealed record EthicDef(string Id, string NameLoc, string Name, string Art, string WorkerCard, Amount[] BonusCards,
    string[] StartTechs, string FreePack, string Desc);

public sealed record Extra(string Card, float Chance, int N);
public sealed record SystemDef(string Id, string Name, string Kind, int Weight, Amount[] FixedCards, string[] StarCards,
    int PlanetsMin, int PlanetsMax, PackEntry[] PlanetPool, Extra[] Extras, string Desc);

public sealed record CrisisDef(string Id, string NameLoc, string Name, string RiftCard, string MinionCard, string BossCard,
    float SpawnEvery, string Warning, string Desc);

public sealed record DifficultyDef(string Id, string Name, float EnemyHpMult, float EnemyAttackMult, int RaidEveryMoons,
    int Act2Moon, int CrisisMoon, int BossDelayMoons, float RiftSpawnMult, float PackCostMult, Amount[] BonusCards, string Desc);
public sealed record MoonLengthDef(string Id, string Name, int Seconds, string Desc);

public sealed record AssetRefDef(string Id, string Game, string Kind, string[] Lookup, string UsedBy, bool Verified, string Note);
public sealed record GameSystemDef(string Id, string Game, string What, string Source, string Method, string Impl, bool Verified);

public static partial class Defs
{
    public static readonly Dictionary<string, CardDef> Card = Cards.ToDictionary(c => c.Id);
    public static readonly Dictionary<string, CategoryDef> Category = Categories.ToDictionary(c => c.Id);
    public static readonly Dictionary<string, LootDef> LootOf = Loot.ToDictionary(l => l.Card);
    public static readonly Dictionary<string, PackDef> Pack = Packs.ToDictionary(p => p.Id);
    public static readonly Dictionary<string, AssetRefDef> Asset = AssetRefs.ToDictionary(a => a.Id);
    public static readonly Dictionary<string, ComponentDef> Component = Components.ToDictionary(c => c.Id);
    public static DifficultyDef DefaultDifficulty => Difficulties.First(d => d.Id == Rules.DefaultDifficulty);
    public static MoonLengthDef DefaultMoonLength => MoonLengths.First(m => m.Id == Rules.DefaultMoonLength);
}
