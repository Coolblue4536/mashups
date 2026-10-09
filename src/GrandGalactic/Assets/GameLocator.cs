using System.Text.RegularExpressions;

namespace GrandGalactic;

/// <summary>Finds the player's Stellaris (passed by Melty as --stellaris "{game}") and Stacklands (found through Steam).</summary>
public static class GameLocator
{
    public static bool IsStellaris(string? dir) =>
        dir != null && Directory.Exists(Path.Combine(dir, "common")) && Directory.Exists(Path.Combine(dir, "gfx"))
        && Directory.Exists(Path.Combine(dir, "localisation"));

    public static bool IsStacklands(string? dir) => dir != null && Directory.Exists(Path.Combine(dir, "Stacklands_Data"));

    public static string? FindStellaris(string? given, Settings s) => Find(given, s.StellarisPath, "Stellaris", IsStellaris);
    public static string? FindStacklands(string? given, Settings s) => Find(given, s.StacklandsPath, "Stacklands", IsStacklands);

    static string? Find(string? given, string? remembered, string folder, Func<string?, bool> ok)
    {
        if (!string.IsNullOrWhiteSpace(given))
        {
            var g = given.Trim().Trim('"');
            if (File.Exists(g)) g = Path.GetDirectoryName(g)!;
            if (ok(g)) return Path.GetFullPath(g);
            Log.Info($"locator: '{given}' is not a {folder} install");
        }
        if (ok(remembered)) return remembered;
        foreach (var lib in SteamLibraries())
        {
            var d = Path.Combine(lib, "steamapps", "common", folder);
            if (ok(d)) return d;
        }
        return null;
    }

    public static IEnumerable<string> SteamLibraries()
    {
        var roots = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key?.GetValue("SteamPath") is string p) roots.Add(p.Replace('/', '\\'));
            }
            catch (Exception e) { Log.Info($"locator: registry read failed ({e.Message})"); }
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
                if (key?.GetValue("InstallPath") is string p) roots.Add(p);
            }
            catch { /* not fatal */ }
            roots.Add(@"C:\Program Files (x86)\Steam");
            roots.Add(@"C:\Program Files\Steam");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            roots.Add(Path.Combine(home, ".steam", "steam"));
            roots.Add(Path.Combine(home, ".local", "share", "Steam"));
            roots.Add(Path.Combine(home, "Library", "Application Support", "Steam"));
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(Directory.Exists))
        {
            if (seen.Add(Path.GetFullPath(root))) yield return root;
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            string text;
            try { text = File.ReadAllText(vdf); } catch { continue; }
            foreach (System.Text.RegularExpressions.Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
            {
                var lib = m.Groups[1].Value.Replace("\\\\", "\\");
                if (Directory.Exists(lib) && seen.Add(Path.GetFullPath(lib))) yield return lib;
            }
        }
    }
}
