using System.Text.Json.Serialization;

namespace StellarAscension;

public sealed class Planet
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Class { get; set; } = "pc_barren";
    public int Size { get; set; }
    public int Owner { get; set; } = -1;
    public double Pops { get; set; }
    public Dictionary<string, int> Districts { get; set; } = new();
    public List<string> Buildings { get; set; } = new();
    /// <summary>Wonders from Act 1 standing on this world (homeworld features).</summary>
    public List<string> Wonders { get; set; } = new();
    public bool Homeworld { get; set; }
    /// <summary>Construction queue: (district or building id, months left).</summary>
    public List<QueueItem> Queue { get; set; } = new();
}

public sealed class QueueItem
{
    public string Id { get; set; } = "";
    public double MonthsLeft { get; set; }
}

public sealed class StarSystem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public string Star { get; set; } = "sc_g";
    public List<Planet> Planets { get; set; } = new();
    public List<int> Lanes { get; set; } = new();
    public int Owner { get; set; } = -1;
    /// <summary>Empires that have surveyed this system (its planets are known to them).</summary>
    public List<int> SurveyedBy { get; set; } = new();
    /// <summary>A rare system made from a Civ VI natural wonder: the modifier its holder gets (or null).</summary>
    public string? RareModifier { get; set; }
    public string? RareFeature { get; set; }
    public double StarbaseDamage { get; set; }
    /// <summary>Months an enemy has held the system unopposed (capture at act2_capture_months).</summary>
    public double CaptureProgress { get; set; }
    public int CaptureBy { get; set; } = -1;
    public bool Home { get; set; }
}

public sealed class Fleet
{
    public int Id { get; set; }
    public int Owner { get; set; }
    public int System { get; set; }
    /// <summary>Ship design id → count.</summary>
    public Dictionary<string, int> Ships { get; set; } = new();
    /// <summary>Systems still to travel through (next first).</summary>
    public List<int> Path { get; set; } = new();
    /// <summary>Distance travelled along the current hyperlane.</summary>
    public double LaneProgress { get; set; }
    /// <summary>"", "survey", "outpost", "colonize".</summary>
    public string Order { get; set; } = "";
    public int OrderSystem { get; set; } = -1;
    public int OrderPlanet { get; set; } = -1;
    public double OrderMonths { get; set; }
    public double Damage { get; set; }
    public string Name { get; set; } = "";
}

public sealed class Empire
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Adjective { get; set; } = "";
    public string Leader { get; set; } = "";
    public bool IsPlayer { get; set; }
    /// <summary>"civ" (from Act 1) or "alien" (Stellaris's own).</summary>
    public string Kind { get; set; } = "civ";
    public string CivType { get; set; } = "";
    public string LeaderType { get; set; } = "";
    public string Portrait { get; set; } = "";
    public string SpeciesClass { get; set; } = "";
    public int ColorR { get; set; }
    public int ColorG { get; set; }
    public int ColorB { get; set; }
    public bool Alive { get; set; } = true;
    public Dictionary<string, double> Stock { get; set; } = new();
    public List<string> Techs { get; set; } = new();
    public string? Researching { get; set; }
    public double ResearchProgress { get; set; }
    /// <summary>Permanent modifiers (legacy techs, space race, Act 2 techs). Wonders and rare systems are added while held.</summary>
    public List<string> Modifiers { get; set; } = new();
    /// <summary>Position in the Act 1 space race (1 = first), 0 for aliens.</summary>
    public int SpaceRank { get; set; }
    public double Aggression { get; set; } = 0.5;
    public double AiMult { get; set; } = 1;
    public int Capital { get; set; } = -1;
    /// <summary>Ship builds at the capital's shipyard: (ship id, months left).</summary>
    public List<QueueItem> Shipyard { get; set; } = new();
    public double Score { get; set; }
    public int NextAiThink { get; set; }
}

public sealed class Nebula
{
    public float X { get; set; }
    public float Y { get; set; }
    public float R { get; set; }
    public float Rotation { get; set; }
    public int Tint { get; set; }
}

public sealed class War
{
    public int A { get; set; }
    public int B { get; set; }
    public int StartMonth { get; set; }
}

public sealed record Act2Event(int Month, string Kind, string Text, int Empire, int System = -1);

public sealed class Act2State
{
    public GameSetup Setup { get; set; } = new();
    public int Month { get; set; }
    public int EndYear { get; set; } = 2300;
    public List<StarSystem> Systems { get; set; } = new();
    public List<Empire> Empires { get; set; } = new();
    public List<Fleet> Fleets { get; set; } = new();
    public List<War> Wars { get; set; } = new();
    public List<Nebula> Nebulae { get; set; } = new();
    public float GalaxyRadius { get; set; }
    public List<Act2Event> Events { get; set; } = new();
    public Rng Rng { get; set; } = new(1);
    public int NextFleetId { get; set; }
    public int NextPlanetId { get; set; }
    /// <summary>"" while playing; "won_score", "won_conquest", "lost_score", "defeated".</summary>
    public string Outcome { get; set; } = "";
    /// <summary>How Act 1 ended, for the summary screen.</summary>
    public List<string> Act1Summary { get; set; } = new();

    [JsonIgnore] public int Year => (int)Rules.Act2StartYear + Month / 12;
    [JsonIgnore] public Empire Player => Empires.First(e => e.IsPlayer);
    public StarSystem Sys(int id) => Systems[id];
    public IEnumerable<Planet> PlanetsOf(int empire) => Systems.SelectMany(s => s.Planets).Where(p => p.Owner == empire);
    public IEnumerable<StarSystem> SystemsOf(int empire) => Systems.Where(s => s.Owner == empire);
    public bool AtWar(int a, int b) => a != b && Wars.Any(w => w.A == a && w.B == b || w.A == b && w.B == a);
}
