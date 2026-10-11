using System.Text.Json;

namespace StellarAscension;

/// <summary>The whole game in one JSON file: Act 1 until the handoff, then Act 2 (Act 1 is kept for the summary).</summary>
public sealed class SaveGame
{
    public int Version { get; set; } = 1;
    /// <summary>"act1", "handoff", "act2".</summary>
    public string Phase { get; set; } = "act1";
    public Act1State? Act1 { get; set; }
    public Act2State? Act2 { get; set; }
    public DateTime Saved { get; set; }

    static readonly JsonSerializerOptions Opt = new() { IncludeFields = false, WriteIndented = false };

    public static string DefaultPath => Path.Combine(Paths.DataDir, "save.json");

    public void Write(string? path = null)
    {
        Saved = DateTime.Now;
        var p = path ?? DefaultPath;
        var tmp = p + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Opt));
        File.Move(tmp, p, true);
    }

    public static SaveGame? Read(string? path = null)
    {
        var p = path ?? DefaultPath;
        if (!File.Exists(p)) return null;
        try { return JsonSerializer.Deserialize<SaveGame>(File.ReadAllText(p), Opt); }
        catch (Exception e) { Log.Info($"save: could not read {p} ({e.Message})"); return null; }
    }
}
