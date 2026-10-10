namespace StellarAscension;

public static class Program
{
    public static int Main(string[] args)
    {
        Log.Init();
        string? Arg(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        var settings = Settings.Load();
        var civ6 = GameLocator.FindCiv6(Arg("--civ6") ?? Environment.GetEnvironmentVariable("SA_CIV6"), settings);
        var stellaris = GameLocator.FindStellaris(Arg("--stellaris") ?? Environment.GetEnvironmentVariable("SA_STELLARIS"), settings);
        Log.Info($"Civilization VI: {civ6 ?? "NOT FOUND"}");
        Log.Info($"Stellaris: {stellaris ?? "NOT FOUND"}");
        if (civ6 != null) settings.Civ6Path = civ6;
        if (stellaris != null) settings.StellarisPath = stellaris;
        settings.Save();

        if (Arg("--probe") is { } probeOut) return Probe.Run(civ6, stellaris, probeOut);
        if (Arg("--blpinfo") is { } blpPath)
        {
            var b = Civ6Blp.Open(blpPath);
            Log.Info($"{blpPath}: {b?.Textures.Count} textures, {b?.Atlases.Count} atlases");
            if (b != null) foreach (var a in b.Atlases.Values.Take(5)) Log.Info($"  atlas {a}");
            return 0;
        }
        Log.Info("nothing to do yet");
        return 0;
    }
}
