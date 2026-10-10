using System.Text.Json;

namespace StellarAscension;

public static class Paths
{
    public static string DataDir
    {
        get
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseDir)) baseDir = AppContext.BaseDirectory;
            var d = Path.Combine(baseDir, "StellarAscension");
            Directory.CreateDirectory(d);
            return d;
        }
    }
}

public static class Log
{
    static readonly object Gate = new();
    static string? _file;

    public static void Init()
    {
        var dir = Path.Combine(Paths.DataDir, "logs");
        Directory.CreateDirectory(dir);
        _file = Path.Combine(dir, "latest.log");
        File.WriteAllText(_file, $"Stellar Ascension {typeof(Log).Assembly.GetName().Version} started {DateTime.Now:u}\n");
    }

    public static void Info(string msg)
    {
        lock (Gate)
        {
            Console.WriteLine(msg);
            if (_file != null) File.AppendAllText(_file, msg + "\n");
        }
    }
}

public sealed class Settings
{
    public string? StellarisPath { get; set; }
    public string? Civ6Path { get; set; }
    public float MusicVolume { get; set; } = 0.35f;
    public float Volume { get; set; } = 0.7f;
    public bool Fullscreen { get; set; }
    public string? Civ { get; set; }
    public string? SpaceRace { get; set; }
    public string? Difficulty { get; set; }
    public string? MapSize { get; set; }
    public int Rivals { get; set; } = -1;

    static string FilePath => Path.Combine(Paths.DataDir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception e)
        {
            Log.Info($"settings: could not read ({e.Message}); using defaults");
        }
        return new Settings();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception e) { Log.Info($"settings: could not save ({e.Message})"); }
    }
}
