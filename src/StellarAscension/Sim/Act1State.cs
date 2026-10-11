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
    /// <summary>"" while playing; "space" when the player reached space; "defeat" when a rival won on Ruthless.</summary>
    public string Outcome { get; set; } = "";

    public Tile At(int x, int y) => Tiles[y * W + x];
    [JsonIgnore] public CivState Player => Civs.First(c => c.IsPlayer);
    public IEnumerable<CityState> CitiesOf(int civ) => Cities.Where(c => c.Owner == civ);
    public CityState? CityAt(int x, int y) => Cities.FirstOrDefault(c => c.X == x && c.Y == y);
    public CityState? City(int id) => Cities.FirstOrDefault(c => c.Id == id);
}
