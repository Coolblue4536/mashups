using Raylib_cs;

namespace StellarAscension;

/// <summary>The window and its screens: missing games, title, new game, Act 1, the handoff, Act 2, the end.</summary>
public sealed partial class App
{
    public enum Screen { Loading, Missing, Title, Setup, Act1, Handoff, Act2, End }

    public readonly Settings Settings;
    public readonly Gfx G = new();
    string? _civ6Path, _stellarisPath;
    public Content? Content;
    public AssetResolver Res = new(null, null);
    public Screen Current = Screen.Loading;
    public SaveGame? Game;
    bool _quit;
    float _clock;
    string? _missing;
    readonly (string path, float at)? _shot;
    readonly string? _demo;
    Music? _music;
    int _track = -1;

    public App(Settings settings, string? civ6, string? stellaris, (string, float)? screenshot, string? demo)
    {
        Settings = settings;
        _civ6Path = civ6;
        _stellarisPath = stellaris;
        _shot = screenshot;
        _demo = demo;
    }

    public void Run()
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint);
        Raylib.InitWindow(1600, 900, "Stellar Ascension: Civilization VI x Stellaris");
        Raylib.SetWindowMinSize(1200, 720);
        if (Environment.GetEnvironmentVariable("SA_WINDOW") is { } size && size.Split('x') is [var w, var h]) Raylib.SetWindowSize(int.Parse(w), int.Parse(h));
        if (Settings.Fullscreen) Raylib.ToggleBorderlessWindowed();
        Raylib.InitAudioDevice();
        Raylib.SetExitKey(KeyboardKey.Null);
        G.Body = G.Title = Raylib.GetFontDefault();
        while (!Raylib.WindowShouldClose() && !_quit)
        {
            float dt = Math.Min(Raylib.GetFrameTime(), 0.1f);
            _clock += dt;
            if (Current == Screen.Loading) Load();
            UpdateMusic();
            G.BeginFrame();
            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(6, 8, 18, 255));
            switch (Current)
            {
                case Screen.Missing: DrawMissing(); break;
                case Screen.Title: DrawTitle(); break;
                case Screen.Setup: DrawSetup(); break;
                case Screen.Act1: DrawAct1(dt); break;
                case Screen.Handoff: DrawHandoff(); break;
                case Screen.Act2: DrawAct2(dt); break;
                case Screen.End: DrawEnd(); break;
            }
            G.EndFrame();
            Raylib.EndDrawing();
            if (_shot is { } s && _clock >= s.at)
            {
                Raylib.TakeScreenshot(Path.GetFileName(s.path));
                var made = Path.Combine(Directory.GetCurrentDirectory(), Path.GetFileName(s.path));
                if (File.Exists(made) && Path.GetFullPath(made) != Path.GetFullPath(s.path)) File.Move(made, s.path, true);
                Log.Info($"screenshot saved: {s.path}");
                break;
            }
        }
        if (Current is Screen.Act1 or Screen.Act2 or Screen.Handoff) SaveNow();
        if (_music is { } m) Raylib.UnloadMusicStream(m);
        Raylib.CloseAudioDevice();
        Raylib.CloseWindow();
    }

    public void Quit() => _quit = true;

    // ---------------------------------------------------------------- loading

    void Load()
    {
        if (!GameLocator.IsCiv6(_civ6Path) || !GameLocator.IsStellaris(_stellarisPath))
        {
            _missing = !GameLocator.IsCiv6(_civ6Path) && !GameLocator.IsStellaris(_stellarisPath) ? "Civilization VI and Stellaris"
                     : !GameLocator.IsCiv6(_civ6Path) ? "Civilization VI" : "Stellaris";
            Current = Screen.Missing;
            return;
        }
        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(6, 8, 18, 255));
        G.Text("Reading your Civilization VI and Stellaris...", 40, 40, 28, Gfx.Ink);
        Raylib.EndDrawing();
        try
        {
            var civ6 = new Civ6Data(_civ6Path!);
            var st = new StellarisData(_stellarisPath!);
            Content = new Content(civ6, st);
            Res = new AssetResolver(civ6, st);
            G.LoadFonts(civ6.FontFile("HelveticaNeue.ttf") ?? civ6.FontFile("MinionPro-Regular.otf"), civ6.FontFile("MinionPro-Bold.otf") ?? civ6.FontFile("MinionPro-Regular.otf"));
            if (Content.Civs.Count == 0) throw new InvalidOperationException("no civilizations found in Civilization VI");
        }
        catch (Exception e)
        {
            Log.Info($"loading failed: {e}");
            _missing = $"a readable copy of the games ({e.Message})";
            Current = Screen.Missing;
            return;
        }
        Game = SaveGame.Read();
        Current = Screen.Title;
        if (_demo != null) StartDemo(_demo);
    }

    void DrawMissing()
    {
        G.UseAct1Theme();
        var r = new Rectangle(Gfx.W / 2 - 360, Gfx.H / 2 - 170, 720, 340);
        G.Panel(r);
        G.Text("Stellar Ascension", r.X + 30, r.Y + 26, 34, Gfx.Gold, true);
        G.Para($"Stellar Ascension needs {_missing} installed on this PC. It reads their pictures, names and music from your own copies each time it runs, and ships none of them.\n\n"
               + "Install both through Steam (or start the game with --civ6 \"<folder>\" --stellaris \"<folder>\"), then press Retry.",
               r.X + 30, r.Y + 84, 20, r.Width - 60, Gfx.Ink);
        if (G.Button(new Rectangle(r.X + 30, r.Y + r.Height - 64, 160, 42), "Retry"))
        {
            _civ6Path = GameLocator.FindCiv6(null, Settings);
            _stellarisPath = GameLocator.FindStellaris(null, Settings);
            Current = Screen.Loading;
        }
        if (G.Button(new Rectangle(r.X + r.Width - 190, r.Y + r.Height - 64, 160, 42), "Quit")) Quit();
    }

    // ---------------------------------------------------------------- music (Stellaris's own tracks, from the install)

    void UpdateMusic()
    {
        var tracks = Content?.Stellaris?.Music;
        if (tracks == null || tracks.Count == 0 || Settings.MusicVolume <= 0) return;
        bool want = Current is Screen.Title or Screen.Setup or Screen.Handoff or Screen.Act2 or Screen.End or Screen.Act1;
        if (!want) return;
        if (_music is { } m)
        {
            Raylib.UpdateMusicStream(m);
            Raylib.SetMusicVolume(m, Settings.MusicVolume);
            if (Raylib.GetMusicTimePlayed(m) < Raylib.GetMusicTimeLength(m) - 0.5f) return;
            Raylib.UnloadMusicStream(m);
            _music = null;
        }
        _track = (_track + 1 + Random.Shared.Next(tracks.Count - 1)) % tracks.Count;
        try
        {
            var mus = Raylib.LoadMusicStream(tracks[_track]);
            mus.Looping = false;
            Raylib.PlayMusicStream(mus);
            Raylib.SetMusicVolume(mus, Settings.MusicVolume);
            _music = mus;
        }
        catch (Exception e) { Log.Info($"music: {e.Message}"); }
    }

    // ---------------------------------------------------------------- saving

    public void SaveNow()
    {
        if (Game == null) return;
        try { Game.Write(); }
        catch (Exception e) { Log.Info($"save failed: {e.Message}"); }
    }

    // ---------------------------------------------------------------- demo (test aid): auto-plays to a point, e.g. for screenshots

    void StartDemo(string demo)
    {
        var parts = demo.Split(':');
        int n = parts.Length > 1 ? int.Parse(parts[1]) : 60;
        ulong seed = parts.Length > 2 ? ulong.Parse(parts[2]) : 42;
        var civ = Content!.Civs.FirstOrDefault(c => c.Leader == (parts.Length > 3 ? parts[3] : "LEADER_TRAJAN")) ?? Content.Civs[0];
        var setup = new GameSetup { Civ = civ.Type, Leader = civ.Leader, MapSize = "small", Rivals = 3, Seed = seed, SpaceRace = "standard" };
        var a1 = Act1.NewGame(setup, Content);
        void AutoTurn()
        {
            Act1Ai.TakeTurn(a1, Content, a1.Player);
            Act1Ai.PlaceSettlers(a1, Content, a1.Player);
            Act1.EndTurn(a1, Content);
        }
        if (parts[0] == "act1")
        {
            for (int i = 0; i < n && a1.Outcome == ""; i++) AutoTurn();
            if (a1.Player.Researching == null) Act1Ai.ChooseResearch(a1, Content, a1.Player);
            Game = new SaveGame { Phase = "act1", Act1 = a1 };
            EnterAct1();
            return;
        }
        while (a1.Outcome == "") AutoTurn();
        if (a1.Outcome != "space") { Game = new SaveGame { Phase = "act1", Act1 = a1 }; EnterAct1(); return; }
        Game = new SaveGame { Phase = "handoff", Act1 = a1, Act2 = Handoff.FromAct1(a1, Content) };
        if (parts[0] == "handoff") { Current = Screen.Handoff; return; }
        var s = Game.Act2!;
        for (int i = 0; i < n && s.Outcome == ""; i++)
        {
            Act2Ai.Think(s, Content, s.Player);
            Act2.EndTurn(s, Content);
        }
        Game.Phase = "act2";
        EnterAct2();
    }
}
