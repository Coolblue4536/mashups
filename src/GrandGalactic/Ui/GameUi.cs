using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace GrandGalactic;

/// <summary>The window: missing-game screen, empire pick, the card boards, and the end screen.</summary>
public sealed partial class GameUi
{
    enum Screen { Loading, Missing, Empire, Play, End }

    const int TopBar = 100, RightPanel = 230;
    readonly Settings _settings;
    string? _stellarisPath, _stacklandsPath;
    readonly bool _dev;
    readonly (string path, float at)? _shot;
    readonly string? _autoEthic;

    Screen _screen = Screen.Loading;
    AssetResolver _res = new(null, null);
    Sim? _sim;
    Vector2? _camGoal;
    float? _camZoomGoal;
    Camera2D _cam;
    Stack? _drag;
    Vector2 _dragOffset, _dragFrom;
    bool _paused, _codex;
    float _speed = 1f, _clock;
    readonly List<(string text, float t)> _toasts = new();

    // empire pick
    int _portraitPage, _portrait = -1;
    EthicDef _ethic = Defs.Ethics[0];
    DifficultyDef _diff = Defs.DefaultDifficulty;
    MoonLengthDef _moon = Defs.DefaultMoonLength;

    // loaded content
    readonly Dictionary<string, Texture2D?> _tex = new();
    readonly Dictionary<int, Texture2D?> _portraitTex = new();
    readonly Dictionary<string, Sound?> _snd = new();
    Music? _music;
    Font _font;
    bool _customFont;

    public GameUi(Settings settings, string? stellaris, string? stacklands, bool devNoAssets, (string, float)? screenshotAfter, string? autoEthic)
    {
        _settings = settings;
        _stellarisPath = stellaris;
        _stacklandsPath = stacklands;
        _dev = devNoAssets;
        _shot = screenshotAfter;
        _autoEthic = autoEthic;
        _diff = Defs.Difficulties.FirstOrDefault(d => d.Id == settings.Difficulty) ?? Defs.DefaultDifficulty;
        _moon = Defs.MoonLengths.FirstOrDefault(m => m.Id == settings.MoonLength) ?? Defs.DefaultMoonLength;
    }

    public void Run()
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint);
        Raylib.InitWindow(1600, 900, "Grand Galactic - Stellaris x Stacklands");
        Raylib.SetWindowMinSize(1100, 700);
        if (_settings.Fullscreen) Raylib.ToggleBorderlessWindowed();
        Raylib.InitAudioDevice();
        Raylib.SetExitKey(KeyboardKey.Null);
        _font = Raylib.GetFontDefault();
        _cam = new Camera2D { Zoom = 0.75f };

        while (!Raylib.WindowShouldClose() && !_quit)
        {
            float dt = Math.Min(Raylib.GetFrameTime(), 0.1f);
            _clock += dt;
            if (_screen == Screen.Loading) LoadGames();
            HandleFileDrop();
            Update(dt);
            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(8, 10, 22, 255));
            switch (_screen)
            {
                case Screen.Missing: DrawMissing(); break;
                case Screen.Empire: DrawEmpire(); break;
                case Screen.Play:
                case Screen.End: DrawPlay(); break;
            }
            if (_dev) Text("DEV BUILD - no game files loaded (layout test only, not gameplay footage)", 12, Raylib.GetScreenHeight() - 26, 16, Color.Orange);
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
        if (_music is { } m) Raylib.UnloadMusicStream(m);
        if (_musicData != IntPtr.Zero) Marshal.FreeHGlobal(_musicData);
        Raylib.CloseAudioDevice();
        Raylib.CloseWindow();
    }

    // ---------- loading ----------

    void LoadGames()
    {
        if (_dev) { _screen = Screen.Empire; AutoStart(); return; }
        bool okSt = GameLocator.IsStellaris(_stellarisPath), okSl = GameLocator.IsStacklands(_stacklandsPath);
        if (!okSt || !okSl) { _screen = Screen.Missing; return; }
        // Show a frame while reading both installs.
        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(8, 10, 22, 255));
        Text("Reading your Stellaris and Stacklands...", 40, 40, 28, Color.RayWhite);
        Raylib.EndDrawing();
        StellarisData? st = null;
        StacklandsData? sl = null;
        try { st = new StellarisData(_stellarisPath!); }
        catch (Exception e) { Log.Info($"stellaris read failed: {e}"); }
        try { sl = new StacklandsData(_stacklandsPath!); }
        catch (Exception e) { Log.Info($"stacklands read failed: {e}"); }
        if (st == null || sl == null) { _screen = Screen.Missing; _loadError = st == null ? "Stellaris" : "Stacklands"; return; }
        _res = new AssetResolver(st, sl);
        if (_res.Font(Defs.Asset["sl_font"]) is { } ttf)
        {
            // Bake large, with mipmaps and trilinear filtering, so text stays clean at every size the UI and the
            // zoomed board draw it. Latin-1 plus the punctuation the sheets and Stellaris names use.
            var glyphs = Enumerable.Range(32, 224).Concat(new[] { 0x2013, 0x2014, 0x2018, 0x2019, 0x201C, 0x201D, 0x2022, 0x2026, 0x2192 }).ToArray();
            _font = Raylib.LoadFontFromMemory(".ttf", ttf, 96, glyphs, glyphs.Length);
            var tex = _font.Texture;
            Raylib.GenTextureMipmaps(ref tex);
            _font.Texture = tex;
            Raylib.SetTextureFilter(_font.Texture, TextureFilter.Trilinear);
            _customFont = true;
        }
        if (_res.Sound(Defs.Asset["sl_music"]) is { } mus) LoadMusic(mus.data, mus.ext);
        _screen = Screen.Empire;
        AutoStart();
    }

    /// <summary>The music file's bytes in unmanaged memory: raylib streams from them for as long as the music plays,
    /// so they must never move or be collected (a managed array can be, which crashed the game).</summary>
    IntPtr _musicData;

    unsafe void LoadMusic(byte[] data, string ext)
    {
        _musicData = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, _musicData, data.Length);
        var type = System.Text.Encoding.ASCII.GetBytes("." + ext + "\0");
        Music music;
        fixed (byte* t = type) music = Raylib.LoadMusicStreamFromMemory((sbyte*)t, (byte*)_musicData, data.Length);
        Raylib.SetMusicVolume(music, _settings.MusicVolume);
        Raylib.PlayMusicStream(music);
        _music = music;
    }

    string? _loadError;
    readonly HashSet<string> _iconTex = new();

    void AutoStart()
    {
        if (_autoEthic == null) return;
        _ethic = Defs.Ethics.FirstOrDefault(e => e.Id == _autoEthic) ?? Defs.Ethics[0];
        _portrait = _res.St?.Portraits.Count > 0 ? 0 : -1;
        StartRun();
        if (_shot != null)
        {
            Demo.Setup(_sim!);
            // Close enough to read the cards, centred on the battle (or the capital), without the start-up message.
            _sim!.Update(0.05f); // one tick so the layout has placed everything
            var bt = _sim.Table.Battles.FirstOrDefault();
            _cam.Zoom = 0.9f;
            _cam.Target = bt != null ? Sim.BattleArea(bt) is var a ? a.Pos + a.Size / 2 - new Vector2(360, 170) : default : _sim.Home.Center;
            if (Environment.GetEnvironmentVariable("GG_SHOT_TECH") == "1") // test aid: a row of tech cards to look at their art
            {
                var at = _sim.Home.Origin + new Vector2(700, 760);
                foreach (var id in new[] { "tech_corvettes", "tech_red_laser", "tech_mass_driver", "tech_space_torpedoes", "tech_deflector",
                                           "tech_nanocomposite_armor", "tech_afterburners", "tech_robotics", "tech_terraforming", "tech_battleships" })
                    _sim.Spawn(id, at, jitter: false);
                _sim.Update(0.05f);
                _cam.Target = at + new Vector2(0, -40);
            }
            _toasts.Clear();
        }
        if (_dev && _shot != null) // layout test: a few surveyed systems, zoomed out
        {
            for (int i = 0; i < 4; i++) _sim!.AddSystem(_sim.RollSystemType());
            _sim!.Systems[2].Claimed = true;
            // a fitted battleship with an admiral, mid-fight with a marauder, to check bars and part icons
            var bs = _sim.Spawn("battleship", _sim.Home.Center + new Vector2(-300, 250), jitter: false);
            foreach (var p in new[] { "gamma_laser", "kinetic_artillery", "improved_deflector", "neutronium_armor" })
                _sim.Fit(_sim.Spawn(p, _sim.Home.Center, jitter: false), bs);
            _sim.AssignAdmiral(_sim.Spawn("admiral", _sim.Home.Center, jitter: false), bs);
            _sim.Attack(bs.Stack!, _sim.Spawn("marauder_raider", _sim.Home.Center + new Vector2(-300, -100), jitter: false));
            var fleet = _sim.StacksIn(_sim.Home).FirstOrDefault(x => Sim.HasShip(x));
            if (fleet != null) _sim.StartTravel(fleet, _sim.Systems[1].Center);
            _sim!.NewSystems.Clear();
            if (Environment.GetEnvironmentVariable("GG_DEV_BOOK") == "1") { _codex = true; _sim.Techs.Add("tech_red_laser"); _sim.Discovered.Add("s_red_laser"); }
            _cam.Zoom = 0.9f;
            _cam.Target = _sim.Home.Center + new Vector2(-150, 50);
        }
    }

    void HandleFileDrop()
    {
        if (!Raylib.IsFileDropped()) return;
        var files = Raylib.GetDroppedFiles();
        foreach (var f in files)
        {
            var d = Directory.Exists(f) ? f : Path.GetDirectoryName(f);
            if (GameLocator.IsStellaris(d)) { _stellarisPath = d; _settings.StellarisPath = d; }
            if (GameLocator.IsStacklands(d)) { _stacklandsPath = d; _settings.StacklandsPath = d; }
        }
        _settings.Save();
        if (_screen == Screen.Missing) _screen = Screen.Loading;
    }

    void StartRun()
    {
        if (_res.St != null && _portrait >= 0) _res.PortraitPath = _res.St.FilePath(_res.St.Portraits[_portrait].TextureFile);
        _tex.Remove("st_portrait_player");
        _settings.Difficulty = _diff.Id;
        _settings.MoonLength = _moon.Id;
        _settings.Save();
        // --screenshot runs use a fixed seed, so the capture is the same every time.
        _sim = new Sim(_ethic, _shot != null ? 1 : Environment.TickCount, id => _res.CardName(id), _diff, _moon);
        _camGoal = null;
        _camZoomGoal = null;
        _cam = new Camera2D { Zoom = 0.62f, Target = _sim.Home.Center };
        _screen = Screen.Play;
        Toast($"{_ethic.Name} empire founded. Your pool (top bar) is empty: put Pops to work on the Homeworld and districts, then click a pack to buy it. Esc opens the menu.");
    }

    // ---------- content ----------

    unsafe Texture2D? Tex(string assetId)
    {
        if (_tex.TryGetValue(assetId, out var t)) return t;
        Texture2D? result = null;
        if (!_dev && Defs.Asset.TryGetValue(assetId, out var a) && _res.Image(a) is { } img)
        {
            result = Upload(img);
            // Transparent corners mean an icon (drawn with a margin); an opaque picture fills its window.
            bool Clear(int x, int y) => img.Rgba[(y * img.W + x) * 4 + 3] < 128;
            if (Clear(0, 0) || Clear(img.W - 1, 0) || Clear(0, img.H - 1) || Clear(img.W - 1, img.H - 1)) _iconTex.Add(assetId);
        }
        _tex[assetId] = result;
        return result;
    }

    static unsafe Texture2D Upload(ImageData img)
    {
        var ptr = Raylib.MemAlloc((uint)img.Rgba.Length);
        Marshal.Copy(img.Rgba, 0, (IntPtr)ptr, img.Rgba.Length);
        var im = new Image { Data = ptr, Width = img.W, Height = img.H, Mipmaps = 1, Format = PixelFormat.UncompressedR8G8B8A8 };
        var tex = Raylib.LoadTextureFromImage(im);
        Raylib.GenTextureMipmaps(ref tex);
        Raylib.SetTextureFilter(tex, TextureFilter.Trilinear);
        Raylib.UnloadImage(im);
        return tex;
    }

    Texture2D? PortraitTex(int i)
    {
        if (_portraitTex.TryGetValue(i, out var t)) return t;
        Texture2D? r = null;
        var p = _res.St!.FilePath(_res.St.Portraits[i].TextureFile);
        if (p != null && ImageData.FromFile(p) is { } img) r = Upload(img);
        _portraitTex[i] = r;
        return r;
    }

    void Play(SimEvent e)
    {
        string? id = e switch
        {
            SimEvent.Pickup => "sl_snd_pickup",
            SimEvent.Drop => "sl_snd_drop",
            SimEvent.PackOpen => "sl_snd_pack",
            SimEvent.Sell => "sl_snd_sell",
            SimEvent.Hit => "sl_snd_hit",
            SimEvent.Done => "sl_snd_done",
            SimEvent.MoonEnd or SimEvent.Warning or SimEvent.Win or SimEvent.Lose => "sl_snd_moon",
            _ => null,
        };
        if (id == null || _dev) return;
        if (!_snd.TryGetValue(id, out var s))
        {
            s = null;
            if (_res.Sound(Defs.Asset[id]) is { } d)
            {
                var wave = Raylib.LoadWaveFromMemory("." + d.ext, d.data);
                if (wave.FrameCount > 0) s = Raylib.LoadSoundFromWave(wave);
                Raylib.UnloadWave(wave);
            }
            _snd[id] = s;
        }
        if (s is { } snd)
        {
            Raylib.SetSoundVolume(snd, _settings.Volume);
            Raylib.PlaySound(snd);
        }
    }

    void Toast(string text) => _toasts.Add((text, _clock));

    // ---------- text helpers ----------

    /// <summary>True between BeginMode2D and EndMode2D (board text scales with the camera).</summary>
    bool _inWorld;

    void Text(string s, float x, float y, float size, Color c)
    {
        // Screen text snaps to whole pixels so glyph edges stay sharp; board text moves smoothly with the camera.
        var at = _inWorld ? new Vector2(x, y) : new Vector2(MathF.Round(x), MathF.Round(y));
        Raylib.DrawTextEx(_font, s, at, size, _customFont ? 0 : size / 10f, c);
    }

    Vector2 Measure(string s, float size) => Raylib.MeasureTextEx(_font, s, size, _customFont ? 0 : size / 10f);

    /// <summary>Draws text wrapped to a width; returns the height used.</summary>
    float Wrapped(string s, float x, float y, float w, float size, Color c, int maxLines = 99)
    {
        var words = s.Split(' ');
        string line = "";
        float yy = y;
        int lines = 0;
        foreach (var word in words)
        {
            var test = line.Length == 0 ? word : line + " " + word;
            if (Measure(test, size).X > w && line.Length > 0)
            {
                if (++lines > maxLines) return yy - y;
                Text(line, x, yy, size, c);
                yy += size * 1.15f;
                line = word;
            }
            else line = test;
        }
        if (line.Length > 0 && ++lines <= maxLines) { Text(line, x, yy, size, c); yy += size * 1.15f; }
        return yy - y;
    }

    static Color Hex(string hex, byte a = 255) =>
        new(Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16), a);

    bool Button(Rectangle r, string label, bool active = false, float size = 22)
    {
        var m = Raylib.GetMousePosition();
        bool hover = Raylib.CheckCollisionPointRec(m, r);
        Raylib.DrawRectangleRounded(r, 0.25f, 8, active ? new Color(90, 140, 220, 255) : hover ? new Color(60, 70, 110, 255) : new Color(36, 42, 70, 255));
        Raylib.DrawRectangleRoundedLinesEx(r, 0.25f, 8, 2, new Color(140, 160, 220, 200));
        var sz = Measure(label, size);
        Text(label, r.X + (r.Width - sz.X) / 2, r.Y + (r.Height - sz.Y) / 2, size, Color.RayWhite);
        return hover && Raylib.IsMouseButtonPressed(MouseButton.Left);
    }

    void DrawFit(Texture2D t, Rectangle box, Color tint)
    {
        float s = Math.Min(box.Width / t.Width, box.Height / t.Height);
        float w = t.Width * s, h = t.Height * s;
        Raylib.DrawTexturePro(t, new Rectangle(0, 0, t.Width, t.Height), new Rectangle(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h), Vector2.Zero, 0, tint);
    }

    // ---------- missing game ----------

    void DrawMissing()
    {
        int w = Raylib.GetScreenWidth();
        Text("Grand Galactic needs both of your games", 60, 60, 40, Color.RayWhite);
        float y = 140;
        void Row(string game, string? path, bool ok, string how)
        {
            Text($"{(ok ? "Found" : "Missing")}: {game}", 60, y, 30, ok ? Color.Green : new Color(255, 120, 110, 255));
            y += 40;
            y += Wrapped(ok ? path! : how, 80, y, w - 160, 22, Color.LightGray) + 24;
        }
        Row("Stellaris", _stellarisPath, GameLocator.IsStellaris(_stellarisPath),
            "Melty starts this mashup from your Stellaris folder. If you launched it some other way, start it with --stellaris \"<your Stellaris folder>\" or drop that folder onto this window.");
        Row("Stacklands", _stacklandsPath, GameLocator.IsStacklands(_stacklandsPath),
            "Every card frame, pack, sound and font here comes from your own copy of Stacklands. Install Stacklands from Steam (store.steampowered.com/app/1948280), then press Retry - or drop your Stacklands folder (the one holding Stacklands_Data) onto this window.");
        if (_loadError != null) Wrapped($"Your {_loadError} folder was found but could not be read; see the log in %LOCALAPPDATA%\\GrandGalactic\\logs\\latest.log.", 60, y, w - 120, 22, Color.Orange);
        if (Button(new Rectangle(60, Raylib.GetScreenHeight() - 110, 200, 56), "Retry"))
        {
            _stacklandsPath ??= GameLocator.FindStacklands(null, _settings);
            _stellarisPath ??= GameLocator.FindStellaris(null, _settings);
            _loadError = null;
            _screen = Screen.Loading;
        }
    }

    // ---------- empire pick ----------

    void DrawEmpire()
    {
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        if (Tex("st_title_background") is { } bg)
            Raylib.DrawTexturePro(bg, new Rectangle(0, 0, bg.Width, bg.Height), new Rectangle(0, 0, sw, sh), Vector2.Zero, 0, new Color(255, 255, 255, 90));
        Text("GRAND GALACTIC", 50, 30, 54, Color.RayWhite);
        Text("Found your empire", 54, 90, 26, new Color(170, 190, 255, 255));

        // Portraits from the player's Stellaris.
        var ports = _res.St?.Portraits ?? new List<PortraitInfo>();
        const int cols = 6, rows = 4, cell = 112;
        float px = 50, py = 140;
        Text("Species", px, py, 24, Color.LightGray);
        py += 34;
        int perPage = cols * rows, pages = Math.Max(1, (ports.Count + perPage - 1) / perPage);
        if (ports.Count == 0) Wrapped(_dev ? "(no Stellaris loaded)" : "Your Stellaris draws species portraits in 3D, so there are no flat portraits to pick from. Your people use Stellaris's pop icon.", px, py, cols * cell, 20, Color.Gray);
        for (int i = 0; i < perPage; i++)
        {
            int idx = _portraitPage * perPage + i;
            if (idx >= ports.Count) break;
            var r = new Rectangle(px + (i % cols) * cell, py + (i / cols) * cell, cell - 8, cell - 8);
            bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
            Raylib.DrawRectangleRec(r, idx == _portrait ? new Color(70, 110, 200, 255) : hover ? new Color(50, 56, 90, 255) : new Color(26, 30, 52, 255));
            if (PortraitTex(idx) is { } t) DrawFit(t, new Rectangle(r.X + 4, r.Y + 4, r.Width - 8, r.Height - 8), Color.White);
            if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left)) _portrait = idx;
        }
        float gy = py + rows * cell + 6;
        if (pages > 1)
        {
            if (Button(new Rectangle(px, gy, 60, 40), "<")) _portraitPage = (_portraitPage + pages - 1) % pages;
            Text($"{_portraitPage + 1}/{pages}", px + 76, gy + 8, 22, Color.LightGray);
            if (Button(new Rectangle(px + 150, gy, 60, 40), ">")) _portraitPage = (_portraitPage + 1) % pages;
        }
        if (_portrait >= 0) Text(ports[_portrait].Group + " / " + ports[_portrait].Name, px + 230, gy + 8, 20, Color.LightGray);

        // Difficulty and moon length.
        float oy = gy + 56, bw = (cols * cell - 24) / 4f;
        Text("Difficulty", px, oy, 22, Color.LightGray);
        Text(_diff.Desc, px + 130, oy + 3, 17, Color.Gray);
        for (int i = 0; i < Defs.Difficulties.Length; i++)
            if (Button(new Rectangle(px + i * (bw + 8), oy + 28, bw, 40), Defs.Difficulties[i].Name, Defs.Difficulties[i] == _diff, 19))
                _diff = Defs.Difficulties[i];
        oy += 84;
        Text("Moon length", px, oy, 22, Color.LightGray);
        Text(_moon.Desc, px + 150, oy + 3, 17, Color.Gray);
        for (int i = 0; i < Defs.MoonLengths.Length; i++)
            if (Button(new Rectangle(px + i * (bw + 8), oy + 28, bw, 40), $"{Defs.MoonLengths[i].Name} ({Defs.MoonLengths[i].Seconds}s)", Defs.MoonLengths[i] == _moon, 18))
                _moon = Defs.MoonLengths[i];

        // Ethics.
        float ex = px + cols * cell + 60, ey = 174, ew = Math.Max(420, sw - ex - 60);
        Text("Ethics", ex, 140, 24, Color.LightGray);
        foreach (var e in Defs.Ethics)
        {
            var r = new Rectangle(ex, ey, ew, 104);
            bool sel = e == _ethic, hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
            Raylib.DrawRectangleRounded(r, 0.12f, 8, sel ? new Color(60, 90, 170, 255) : hover ? new Color(40, 46, 80, 255) : new Color(24, 28, 50, 230));
            if (Tex(e.Art) is { } icon) DrawFit(icon, new Rectangle(r.X + 12, r.Y + 14, 76, 76), Color.White);
            var name = _res.St?.Text(e.NameLoc) ?? e.Name;
            Text(name, r.X + 104, r.Y + 12, 28, Color.RayWhite);
            Wrapped(e.Desc, r.X + 104, r.Y + 48, r.Width - 120, 19, Color.LightGray, 2);
            if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left)) _ethic = e;
            ey += 116;
        }
        if (Button(new Rectangle(ex + 280, ey + 20, 200, 44), _settings.Tutorial ? "Tutorial: On" : "Tutorial: Off", _settings.Tutorial, 19))
        {
            _settings.Tutorial = !_settings.Tutorial;
            _settings.Save();
        }
        bool ready = _portrait >= 0 || ports.Count == 0;
        if (Button(new Rectangle(ex, ey + 10, 260, 64), ready ? "Begin" : "Pick a species", false, 30) && ready) StartRun();
        if (Sim.HasSave && Button(new Rectangle(ex + 500, ey + 10, 220, 64), "Continue", false, 28)) LoadGame();
        Text($"Stellaris: {_stellarisPath ?? "-"}", ex, sh - 58, 16, Color.Gray);
        Text($"Stacklands: {_stacklandsPath ?? "-"}", ex, sh - 36, 16, Color.Gray);
    }
}
