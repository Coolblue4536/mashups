namespace GrandGalactic;

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
        if (args.Contains("--selftest")) return SelfTest.Run();
        if (args.Contains("--playtest")) return Playtest.RunCli(args);
        if (args.Contains("--bossprobe")) return Playtest.BossProbe();

        var settings = Settings.Load();
        var stellaris = GameLocator.FindStellaris(Arg("--stellaris") ?? Environment.GetEnvironmentVariable("GRANDGALACTIC_STELLARIS"), settings);
        var stacklands = GameLocator.FindStacklands(Arg("--stacklands") ?? Environment.GetEnvironmentVariable("GRANDGALACTIC_STACKLANDS"), settings);
        Log.Info($"Stellaris: {stellaris ?? "NOT FOUND"}");
        Log.Info($"Stacklands: {stacklands ?? "NOT FOUND"}");
        if (stellaris != null) settings.StellarisPath = stellaris;
        if (stacklands != null) settings.StacklandsPath = stacklands;
        settings.Save();

        if (Arg("--probe") is { } probeOut) return Probe.Run(stellaris, stacklands, probeOut);

        var ui = new GameUi(settings, stellaris, stacklands, devNoAssets: args.Contains("--dev-no-assets"),
            screenshotAfter: Arg("--screenshot") is { } shot ? (shot, float.Parse(Arg("--screenshot-at") ?? "8")) : null,
            autoEthic: Arg("--ethic"));
        ui.Run();
        return 0;
    }
}
