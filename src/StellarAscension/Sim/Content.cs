namespace StellarAscension;

public sealed record TechInfo(string Type, string Era, int EraIndex, int Cost, string[] Prereqs, string NameLoc);

/// <summary>
/// Everything the simulation needs to know about the two games, built once at start-up: the design sheet
/// plus what is read from the player's Civ VI (tech tree, costs, civilizations, names). The simulation never
/// touches the installs directly, so it also runs headless (self-test, balance runs).
/// </summary>
public sealed class Content
{
    public readonly List<TechInfo> Techs = new();
    public readonly Dictionary<string, TechInfo> Tech = new(StringComparer.Ordinal);
    public readonly List<Civ6Civ> Civs;
    public readonly Dictionary<string, int> BuildingCost = new(StringComparer.Ordinal);
    public readonly Dictionary<string, int> WonderCost = new(StringComparer.Ordinal);
    public readonly Dictionary<string, int> ProjectCost = new(StringComparer.Ordinal);
    /// <summary>Natural wonders the player's Civ VI has (FeatureType → land?), sheet rows first.</summary>
    public readonly List<(string type, bool land)> NaturalWonders = new();
    public readonly Civ6Data? Civ6;
    public readonly StellarisData? Stellaris;

    static readonly string[] EraOrder = { "ERA_ANCIENT", "ERA_CLASSICAL", "ERA_MEDIEVAL", "ERA_RENAISSANCE", "ERA_INDUSTRIAL", "ERA_MODERN", "ERA_ATOMIC", "ERA_INFORMATION" };

    public Content(Civ6Data civ6, StellarisData? stellaris)
    {
        Civ6 = civ6;
        Stellaris = stellaris;
        // Base-game techs only: the expansions add a Future Era and change prerequisites, which this simpler game doesn't use.
        var basePath = Path.Combine(civ6.Root, "Base", "Assets", "Gameplay", "Data", "Technologies.xml");
        var baseTechs = File.Exists(basePath)
            ? new HashSet<string>(System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(basePath), "TechnologyType=\"(TECH_[A-Z_]+)\"").Select(m => m.Groups[1].Value))
            : new HashSet<string>();
        var prereqs = civ6.TechPrereqs();
        foreach (var r in civ6.Rows("Technologies").GroupBy(r => r.Get("TechnologyType")!).Select(g => g.First()))
        {
            var type = r.Get("TechnologyType")!;
            if (baseTechs.Count > 0 && !baseTechs.Contains(type)) continue;
            var era = r.Get("EraType") ?? "ERA_ANCIENT";
            int ei = Math.Max(0, Array.IndexOf(EraOrder, era));
            int cost = (int)Math.Round(r.Int("Cost", 50) * Rules.Act1TechCostMult);
            var pre = (prereqs.TryGetValue(type, out var p) ? p : new List<string>()).ToArray();
            var t = new TechInfo(type, era, ei, Math.Max(10, cost), pre, r.Get("Name") ?? type);
            Techs.Add(t);
        }
        foreach (var t in Techs) Tech[t.Type] = t;
        // Drop prerequisites the base tree doesn't have.
        for (int i = 0; i < Techs.Count; i++)
        {
            var t = Techs[i];
            var pre = t.Prereqs.Where(Tech.ContainsKey).ToArray();
            if (pre.Length != t.Prereqs.Length) Tech[t.Type] = Techs[i] = t with { Prereqs = pre };
        }
        Techs.Sort((a, b) => a.Cost != b.Cost ? a.Cost.CompareTo(b.Cost) : string.CompareOrdinal(a.Type, b.Type));
        Civs = civ6.Civilizations();
        foreach (var b in Defs.Buildings)
            BuildingCost[b.Id] = Scaled(civ6.Row("Buildings", "BuildingType", b.Id)?.Int("Cost") ?? civ6.Row("Districts", "DistrictType", b.Id)?.Int("Cost") ?? b.Cost, b.Cost);
        // The Spaceport is part of the space race: it gets the space cost multiplier too.
        foreach (var id in Defs.Projects.Select(p => p.RequiresBuilding).Distinct())
            if (BuildingCost.TryGetValue(id, out var c)) BuildingCost[id] = (int)Math.Round(c * Rules.Act1SpaceCostMult);
        foreach (var w in Defs.Wonders) WonderCost[w.Id] = Scaled(civ6.Row("Buildings", "BuildingType", w.Id)?.Int("Cost") ?? w.Cost, w.Cost);
        foreach (var p in Defs.Projects) ProjectCost[p.Id] = (int)Math.Round(Scaled(civ6.Row("Projects", "ProjectType", p.Id)?.Int("Cost") ?? p.Cost, p.Cost) * Rules.Act1SpaceCostMult);
        foreach (var n in Defs.NaturalWonders.Where(n => n.Id != "DEFAULT")) NaturalWonders.Add((n.Id, n.Land));
        foreach (var f in civ6.Rows("Features").Where(f => f.Bool("NaturalWonder")))
        {
            var id = f.Get("FeatureType")!;
            if (NaturalWonders.Any(n => n.type == id) || id.EndsWith("_EXTENDED")) continue;
            // Civ VI says where each natural wonder may go (Feature_ValidTerrains): water if only coast or ocean.
            var terr = civ6.Rows("Feature_ValidTerrains").Where(r => r.Get("FeatureType") == id).Select(r => r.Get("TerrainType")).ToList();
            bool water = terr.Count > 0 && terr.All(t => t is "TERRAIN_COAST" or "TERRAIN_OCEAN");
            NaturalWonders.Add((id, !water));
        }
    }

    /// <summary>Civ VI's cost scaled; the sheet's value is used when the install's is missing or 0.</summary>
    static int Scaled(int fromGame, int fromSheet) => (int)Math.Round((fromGame > 0 ? fromGame : fromSheet) * Rules.Act1BuildCostMult);

    public static string EraOf(int index) => EraOrder[Math.Clamp(index, 0, EraOrder.Length - 1)];

    public string Name(string type) => type switch
    {
        _ when type.StartsWith("TECH_") => Text(Tech.TryGetValue(type, out var t) ? t.NameLoc : "LOC_" + type + "_NAME"),
        _ => Text("LOC_" + type + "_NAME"),
    };

    /// <summary>Civ VI text for a LOC_ key (markup removed), or the fallback when the install doesn't have it.</summary>
    public string Text(string? key, string? fallback = null) => Civ6 != null ? Civ6.T(key, fallback) : fallback ?? key ?? "";

    public NaturalWonderDef NaturalDef(string type) => Defs.NaturalWonder.TryGetValue(type, out var d) ? d : Defs.NaturalWonder["DEFAULT"];
}
