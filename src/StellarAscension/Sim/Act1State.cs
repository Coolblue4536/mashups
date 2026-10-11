using System.Text.Json.Serialization;

namespace StellarAscension;

/// <summary>Choices made on the New Game screen.</summary>
public sealed class GameSetup
{
    public string Civ { get; set; } = "";
    public string Leader { get; set; } = "";
    public int Rivals { get; set; } = 5;
    public string MapSize { get; set; } = "standard";
    public string SpaceRace { get; set; } = "standard";
    public string Difficulty { get; set; } = "DIFFICULTY_PRINCE";
    public string GameLength { get; set; } = "standard";
    /// <summary>Act 2: galaxy size (galaxy_sizes), number of Stellaris alien empires (-1: the map size's default) and AI difficulty (galaxy_difficulties).</summary>
    public string GalaxySize { get; set; } = "medium";
    public int Aliens { get; set; } = -1;
    public string GalaxyDifficulty { get; set; } = "captain";
    public ulong Seed { get; set; }
}

public sealed class Tile
{
    public int Terrain { get; set; }
    public int Feature { get; set; } = -1;
    public string? Natural { get; set; }
    /// <summary>Id of the city whose territory this tile is in, or -1.</summary>
    public int City { get; set; } = -1;
    /// <summary>A district ("district:Campus") or a wonder (its BUILDING_ id) standing on the tile, or null.</summary>
    public string? Built { get; set; }
    /// <summary>The player has seen this tile (fog of war).</summary>
    public bool Revealed { get; set; }
    /// <summary>A Civ VI resource on the tile (RESOURCE_WHEAT...), or null.</summary>
    public string? Resource { get; set; }
    /// <summary>A Builder's improvement (IMPROVEMENT_FARM...), or null.</summary>
    public string? Improvement { get; set; }
    /// <summary>A tribal camp stands here (raiders come from it).</summary>
    public bool Camp { get; set; }
    /// <summary>A tribal village (Civ VI's goody hut): the first unit to enter gets a reward.</summary>
    public bool Village { get; set; }
    /// <summary>An exotic site (exotic_sites id), or null.</summary>
    public string? Exotic { get; set; }
}

/// <summary>An Act 1 land unit on the map.</summary>
public sealed class UnitState
{
    public int Id { get; set; }
    public int Owner { get; set; }
    public string Type { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public double Hp { get; set; } = 100;
    /// <summary>Movement points left this turn.</summary>
    public double Moves { get; set; }
    public bool Attacked { get; set; }
    /// <summary>Moved or fought this turn (no healing).</summary>
    public bool Acted { get; set; }
    public bool Fortified { get; set; }
    /// <summary>The player chose to skip this unit this turn.</summary>
    public bool Skipped { get; set; }
    /// <summary>A multi-turn move: the unit keeps heading here each turn (-1: none).</summary>
    public int TargetX { get; set; } = -1;
    public int TargetY { get; set; } = -1;
    /// <summary>Combat experience: every 15 / 45 / 90 makes it a level stronger.</summary>
    public int Xp { get; set; }
    /// <summary>Builder: improvements it can still make.</summary>
    public int Charges { get; set; }
    /// <summary>Locked in a fight with this unit (-1: none); a round is fought each turn until one dies or withdraws.</summary>
    public int Engaged { get; set; } = -1;
    public int EngageRounds { get; set; }
    /// <summary>Tribes: the camp this raider came from.</summary>
    public int HomeX { get; set; } = -1;
    public int HomeY { get; set; } = -1;
}

/// <summary>An Act 1 trade deal: From gives To access to a resource, or gold each turn, or open borders, until Until.</summary>
/// <summary>A deal an AI civ offers the player: it gives resource Give, asks for resource Get, Gold a turn
/// (positive: they pay you; negative: you pay them) and maybe open borders; or an alliance. Answer before Expires (turn).</summary>
public sealed class Act1Offer
{
    public int From { get; set; }
    public string Kind { get; set; } = "trade";
    public string Give { get; set; } = "";
    public string Get { get; set; } = "";
    public int Gold { get; set; }
    public bool Borders { get; set; }
    public int Expires { get; set; }
    public bool Seen { get; set; }
}

public sealed class DealState
{
    public int From { get; set; }
    public int To { get; set; }
    /// <summary>"resource", "gold" or "borders".</summary>
    public string Kind { get; set; } = "";
    public string Resource { get; set; } = "";
    public int Gold { get; set; }
    public int Until { get; set; }
}

public sealed class WarState
{
    public int A { get; set; }
    public int B { get; set; }
    public int Since { get; set; }
}

public sealed class CivState
{
    public int Id { get; set; }
    public string CivType { get; set; } = "";
    public string Leader { get; set; } = "";
    public bool IsPlayer { get; set; }
    public bool Alive { get; set; } = true;
    public List<string> Techs { get; set; } = new();
    public string? Researching { get; set; }
    public double ResearchProgress { get; set; }
    public double Gold { get; set; }
    public int SettlersBuilt { get; set; }
    /// <summary>Settlers trained and waiting for a city site (the player places theirs on the map).</summary>
    public int PendingSettlers { get; set; }
    public bool SatelliteDone { get; set; }
    /// <summary>Turn the civ completed the Moon Landing (0: not yet).</summary>
    public int MoonTurn { get; set; }
    public double YieldMult { get; set; } = 1;
    public int NextCityName { get; set; }
    /// <summary>How readily this AI goes to war (0-1).</summary>
    public double Aggression { get; set; } = 0.5;
    /// <summary>civ_traits id.</summary>
    public string Trait { get; set; } = "";
    /// <summary>The tribes (barbarians): at war with everyone, never in the space race.</summary>
    public bool IsTribe { get; set; }
    /// <summary>Civs this one has met.</summary>
    public List<int> Met { get; set; } = new();
    /// <summary>Civ id → turn its envoy arrives (relations open from then on).</summary>
    public Dictionary<int, int> Envoys { get; set; } = new();
}

public sealed class CityState
{
    public int Id { get; set; }
    public int Owner { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public string Name { get; set; } = "";
    public int Pop { get; set; } = 1;
    public double Food { get; set; }
    /// <summary>What the city is building: "settler", a building, a wonder, or a project id. Null: nothing.</summary>
    public string? Build { get; set; }
    /// <summary>Production put into each thing so far (switching keeps it, as in Civ VI).</summary>
    public Dictionary<string, double> Progress { get; set; } = new();
    public List<string> Buildings { get; set; } = new();
    public List<string> Wonders { get; set; } = new();
    public bool Capital { get; set; }
    public int Founded { get; set; }
    /// <summary>Production carried into the next build (overflow, or a share of a wonder lost to a rival).</summary>
    public double Carry { get; set; }
    public double Hp { get; set; } = 200;
    /// <summary>Last turn the city was attacked (it doesn't heal that turn).</summary>
    public int LastAttacked { get; set; } = -10;
    /// <summary>The civ that founded the city (stays when it is captured).</summary>
    public int Founder { get; set; } = -1;
}

public sealed record Act1Event(int Turn, string Kind, string Text, int Civ, int X = -1, int Y = -1);

public sealed class Act1State
{
    public GameSetup Setup { get; set; } = new();
    public int W { get; set; }
    public int H { get; set; }
    public Tile[] Tiles { get; set; } = Array.Empty<Tile>();
    public List<CivState> Civs { get; set; } = new();
    public List<CityState> Cities { get; set; } = new();
    public int Turn { get; set; } = 1;
    public int Year { get; set; } = -4000;
    public Rng Rng { get; set; } = new(1);
    /// <summary>Wonder → civ id that built it.</summary>
    public Dictionary<string, int> WonderOwner { get; set; } = new();
    /// <summary>Civ ids in the order they completed the Moon Landing.</summary>
    public List<int> SpaceOrder { get; set; } = new();
    public List<Act1Event> Events { get; set; } = new();
    public int NextCityId { get; set; }
    public List<UnitState> Units { get; set; } = new();
    public int NextUnitId { get; set; }
    public List<WarState> Wars { get; set; } = new();
    public List<DealState> Deals { get; set; } = new();
    public List<Act1Offer> Offers { get; set; } = new();
    /// <summary>Civ id -> the turn it may next offer the player a deal.</summary>
    public Dictionary<int, int> NextOffer { get; set; } = new();
    /// <summary>Alliances (pairs of civ ids, lower first).</summary>
    public List<int[]> Alliances { get; set; } = new();
    /// <summary>Recent fights for the screen (attacker unit, defender unit or city, damage each way, turn).</summary>
    [JsonIgnore] public List<BattleNote> Battles { get; } = new();
    /// <summary>"" while playing; "space" when the player reached space; "defeat" when a rival won on Ruthless; "conquered" when the player lost every city.</summary>
    public string Outcome { get; set; } = "";

    public Tile At(int x, int y) => Tiles[y * W + x];
    [JsonIgnore] public CivState Player => Civs.First(c => c.IsPlayer);
    public IEnumerable<CityState> CitiesOf(int civ) => Cities.Where(c => c.Owner == civ);
    public CityState? CityAt(int x, int y) => Cities.FirstOrDefault(c => c.X == x && c.Y == y);
    public CityState? City(int id) => Cities.FirstOrDefault(c => c.Id == id);
    public UnitState? UnitAt(int x, int y) => MilitaryAt(x, y) ?? CivilianAt(x, y);
    public IEnumerable<UnitState> UnitsOf(int civ) => Units.Where(u => u.Owner == civ);
    public UnitState? Unit(int id) => Units.FirstOrDefault(u => u.Id == id);
    public bool AtWar(int a, int b) => a != b && (Civs[a].IsTribe || Civs[b].IsTribe || Wars.Any(w => w.A == a && w.B == b || w.A == b && w.B == a));
    /// <summary>The civilizations (not the tribes).</summary>
    [JsonIgnore] public IEnumerable<CivState> Majors => Civs.Where(c => !c.IsTribe);
    [JsonIgnore] public CivState? Tribe => Civs.FirstOrDefault(c => c.IsTribe);
    public bool Allied(int a, int b) => Alliances.Any(x => x[0] == Math.Min(a, b) && x[1] == Math.Max(a, b));
    /// <summary>Relations are open: they have met and a's envoy has arrived (or b's at a).</summary>
    public bool Relations(int a, int b) => Civs[a].Envoys.TryGetValue(b, out var t) && t <= Turn || Civs[b].Envoys.TryGetValue(a, out var u) && u <= Turn;
    public bool HasMet(int a, int b) => a == b || Civs[a].Met.Contains(b);
    public bool OpenBorders(int owner, int visitor) => Deals.Any(d => d.Kind == "borders" && d.From == owner && d.To == visitor && d.Until >= Turn) || Allied(owner, visitor);
    /// <summary>The military unit on a tile (one per tile), else a civilian (Builder).</summary>
    public UnitState? CivilianAt(int x, int y) => Units.FirstOrDefault(u => u.X == x && u.Y == y && u.Type == "UNIT_BUILDER");
    public UnitState? MilitaryAt(int x, int y) => Units.FirstOrDefault(u => u.X == x && u.Y == y && u.Type != "UNIT_BUILDER");
    /// <summary>The civ owning a tile's territory, or -1.</summary>
    public int OwnerAt(int x, int y) => At(x, y).City is var c and >= 0 && City(c) is { } city ? city.Owner : -1;
}

/// <summary>One fight, for the battle panel and the floating numbers on the map.</summary>
public sealed record BattleNote(int Turn, int AttackerCiv, string AttackerType, int DefenderCiv, string DefenderType, string DefenderName,
    double ToDefender, double ToAttacker, double AttackerHp, double DefenderHp, bool Killed, bool Captured, int X, int Y, int FromX, int FromY, string Text);
