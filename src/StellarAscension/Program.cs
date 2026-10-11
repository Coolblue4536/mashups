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

        if (args.Contains("--selftest")) return SelfTest.Run(civ6, stellaris);
        if (Arg("--probe") is { } probeOut) return Probe.Run(civ6, stellaris, probeOut);
        if (Arg("--blpinfo") is { } blpPath)
        {
            var b = Civ6Blp.Open(blpPath);
            Log.Info($"{blpPath}: {b?.Textures.Count} textures, {b?.Atlases.Count} atlases");
            if (b != null) foreach (var a in b.Atlases.Values.Take(5)) Log.Info($"  atlas {a}");
            return 0;
        }
        if (Arg("--act1balance") is { } runs)
        {
            if (civ6 == null) { Log.Info("needs Civilization VI"); return 2; }
            var content = new Content(new Civ6Data(civ6), null);
            return Playtest.Act1Balance(content, int.Parse(runs), Arg("--size") ?? "standard", Arg("--difficulty") ?? "DIFFICULTY_PRINCE");
        }
        if (args.Contains("--matrix"))
        {
            if (civ6 == null || stellaris == null) { Log.Info("--matrix needs both games installed"); return 2; }
            return Playtest.Matrix(new Content(new Civ6Data(civ6), new StellarisData(stellaris)));
        }
        if (Arg("--fullbalance") is { } fruns)
        {
            if (civ6 == null) { Log.Info("needs Civilization VI"); return 2; }
            var content = new Content(new Civ6Data(civ6), stellaris != null ? new StellarisData(stellaris) : null);
            return Playtest.FullBalance(content, int.Parse(fruns), Arg("--size") ?? "small");
        }
        var app = new App(settings, civ6, stellaris,
            Arg("--screenshot") is { } shot ? (shot, float.Parse(Arg("--screenshot-at") ?? "6", System.Globalization.CultureInfo.InvariantCulture)) : null,
            Arg("--demo"));
        if (Arg("--uiscript") is { } script) app.LoadScript(script, Arg("--uiout") ?? Path.Combine(Paths.DataDir, "uiscript"));
        app.Run();
        return 0;
    }
}
