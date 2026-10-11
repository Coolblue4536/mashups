using System.Numerics;
using Raylib_cs;

namespace StellarAscension;

public sealed partial class App
{
    GameSetup _setup = new();
    int _civPick = -1;

    public Texture2D? CivIcon(string leader) => G.Tex("leadericon:" + leader, () => Res.Civ6Icon("ICON_" + leader));
    public Texture2D? CivSymbol(string civ) => G.Tex("civsym:" + civ, () => Res.Civ6Icon("ICON_" + civ));
    public Texture2D? LeaderArt(string leader) => G.Tex("leaderart:" + leader, () => Res.LeaderPicture(leader));
    public Texture2D? St(string asset) => G.Tex("st:" + asset, () => Res.Stellaris(asset));
    public Texture2D? Icon(string civ6Icon) => G.Tex("civ:" + civ6Icon, () => Res.Civ6Icon(civ6Icon));

    void Background(bool stars, string asset = "st_galaxy_background")
    {
        var bg = stars ? St(asset) : null;
        if (bg != null) Gfx.DrawCover(bg, new Rectangle(0, 0, Gfx.W, Gfx.H), new Color(140, 140, 160, 255));
        Raylib.DrawRectangleGradientV(0, 0, (int)Gfx.W, (int)Gfx.H, new Color(4, 6, 14, 60), new Color(4, 6, 14, 230));
    }

    // ---------------------------------------------------------------- title

    void DrawTitle()
    {
        G.UseAct1Theme();
        Background(true);
        float cx = Gfx.W / 2;
        G.TextCentered("Stellar Ascension", new Rectangle(0, Gfx.H * 0.16f, Gfx.W, 80), 76, Gfx.Gold, true);
        G.TextCentered("Civilization VI  ×  Stellaris", new Rectangle(0, Gfx.H * 0.16f + 84, Gfx.W, 30), 26, Gfx.Ink);
        G.TextCentered("Build a civilization, win the race to space, then take the same empire to the stars.", new Rectangle(0, Gfx.H * 0.16f + 122, Gfx.W, 24), 19, Gfx.Muted);
        float y = Gfx.H * 0.45f, bw = 340;
        if (Game != null)
        {
            string what = Game.Phase == "act2" && Game.Act2 != null ? $"Act 2 · {Game.Act2.Player.Name} · {Game.Act2.Year}"
                        : Game.Act1 != null ? $"Act 1 · {Act1.CivName(Content!, Game.Act1.Player)} · {Act1.YearText(Game.Act1.Year)}" : "";
            if (G.Button(new Rectangle(cx - bw / 2, y, bw, 52), "Continue", tip: what)) Resume();
            G.TextCentered(what, new Rectangle(cx - 300, y + 56, 600, 20), 16, Gfx.Muted);
            y += 86;
        }
        if (G.Button(new Rectangle(cx - bw / 2, y, bw, 52), "New Game")) OpenSetup();
        y += 66;
        if (G.Button(new Rectangle(cx - bw / 2, y, bw / 2 - 6, 44), Settings.MusicVolume > 0 ? "Music: on" : "Music: off", size: 18))
        {
            Settings.MusicVolume = Settings.MusicVolume > 0 ? 0 : 0.35f;
            if (Settings.MusicVolume == 0 && _music is { } m) { Raylib.StopMusicStream(m); Raylib.UnloadMusicStream(m); _music = null; }
            Settings.Save();
        }
        if (G.Button(new Rectangle(cx + 6, y, bw / 2 - 6, 44), Settings.Fullscreen ? "Windowed" : "Fullscreen", size: 18))
        {
            Settings.Fullscreen = !Settings.Fullscreen;
            Raylib.ToggleBorderlessWindowed();
            Settings.Save();
        }
        y += 58;
        if (G.Button(new Rectangle(cx - bw / 2, y, bw, 44), "Quit", size: 18)) Quit();
        G.TextCentered("Pictures, names and music are read from your own Civilization VI and Stellaris each time the game runs.", new Rectangle(0, Gfx.H - 40, Gfx.W, 20), 15, Gfx.Muted);
    }

    void Resume()
    {
        if (Game == null) return;
        if (Game.Phase == "act2" && Game.Act2 != null) EnterAct2();
        else if (Game.Phase == "handoff" && Game.Act2 != null) Current = Screen.Handoff;
        else if (Game.Act1 != null) EnterAct1();
    }

    // ---------------------------------------------------------------- new game

    void OpenSetup()
    {
        var c = Content!;
        _setup = new GameSetup
        {
            MapSize = Settings.MapSize ?? "standard", SpaceRace = Settings.SpaceRace ?? "standard", Difficulty = Settings.Difficulty ?? "DIFFICULTY_PRINCE",
            GameLength = "standard", Seed = (ulong)DateTime.Now.Ticks,
        };
        _setup.Rivals = Settings.Rivals > 0 ? Settings.Rivals : Defs.MapSize[_setup.MapSize].DefaultRivals;
        _civPick = Math.Max(0, c.Civs.FindIndex(x => x.Leader == Settings.Civ));
        Current = Screen.Setup;
    }

    void DrawSetup()
    {
        G.UseAct1Theme();
        Background(true);
        var c = Content!;
        G.Text("New Game", 40, 26, 40, Gfx.Gold, true);
        if (G.Button(new Rectangle(Gfx.W - 170, 28, 130, 40), "Back", size: 18)) Current = Screen.Title;

        // Civilization list
        var list = new Rectangle(40, 90, 380, Gfx.H - 130);
        G.Panel(list);
        G.Text("Your civilization", list.X + 16, list.Y + 12, 22, Gfx.Ink);
        var inner = new Rectangle(list.X + 8, list.Y + 48, list.Width - 16, list.Height - 56);
        float rowH = 58, y0 = G.BeginScroll("civs", inner, c.Civs.Count * rowH);
        for (int i = 0; i < c.Civs.Count; i++)
        {
            var civ = c.Civs[i];
            var r = new Rectangle(inner.X, y0 + i * rowH, inner.Width - 10, rowH - 6);
            if (r.Y + r.Height < inner.Y || r.Y > inner.Y + inner.Height) continue;
            if (G.IconButton(r, CivIcon(civ.Leader), c.Text(civ.LeaderNameLoc), c.Text(civ.DescLoc), selected: i == _civPick) && (G.Hover(inner) || G.ScriptActive)) _civPick = i;
        }
        G.EndScroll();

        // Leader
        var pick = c.Civs[Math.Clamp(_civPick, 0, c.Civs.Count - 1)];
        var mid = new Rectangle(list.X + list.Width + 20, 90, Math.Max(300, Gfx.W - list.Width - 500 - 80), Gfx.H - 130);
        G.Panel(mid);
        var art = LeaderArt(pick.Leader);
        if (art != null) Gfx.DrawFit(art, new Rectangle(mid.X + 10, mid.Y + 10, mid.Width - 20, mid.Height - 130), Color.White);
        else Gfx.DrawFit(CivIcon(pick.Leader), new Rectangle(mid.X + mid.Width / 2 - 120, mid.Y + 60, 240, 240), Color.White);
        var sym = CivSymbol(pick.Type);
        var col = new Color(pick.Primary.r, pick.Primary.g, pick.Primary.b, (byte)255);
        Raylib.DrawCircle((int)(mid.X + 60), (int)(mid.Y + mid.Height - 70), 40, col);
        Gfx.DrawFit(sym, new Rectangle(mid.X + 30, mid.Y + mid.Height - 100, 60, 60), new Color(pick.Secondary.r, pick.Secondary.g, pick.Secondary.b, (byte)255));
        G.Text(c.Text(pick.LeaderNameLoc), mid.X + 116, mid.Y + mid.Height - 104, 28, Gfx.Ink, true);
        G.Text(c.Text(pick.DescLoc) + (pick.CapitalLoc != null ? $" · capital {c.Text(pick.CapitalLoc)}" : ""), mid.X + 116, mid.Y + mid.Height - 66, 19, Gfx.Muted);

        // Options
        var opt = new Rectangle(Gfx.W - 460, 90, 420, Gfx.H - 130);
        G.Panel(opt);
        float x = opt.X + 18, y = opt.Y + 14, w = opt.Width - 36;
        G.Text("Map", x, y, 20, Gfx.Gold); y += 28;
        float bw = (w - 12) / 3;
        for (int i = 0; i < Defs.MapSizes.Length; i++)
        {
            var m = Defs.MapSizes[i];
            if (G.Button(new Rectangle(x + i * (bw + 6), y, bw, 36), m.Name, selected: _setup.MapSize == m.Id, tip: m.Desc, size: 18))
            {
                _setup.MapSize = m.Id;
                _setup.Rivals = m.DefaultRivals;
            }
        }
        y += 46;
        var ms = Defs.MapSize[_setup.MapSize];
        G.Text($"Rivals: {_setup.Rivals}", x, y + 6, 20, Gfx.Ink);
        if (G.Button(new Rectangle(x + w - 96, y, 44, 34), "-", _setup.Rivals > 1)) _setup.Rivals--;
        if (G.Button(new Rectangle(x + w - 44, y, 44, 34), "+", _setup.Rivals < ms.MaxRivals)) _setup.Rivals++;
        y += 48;
        G.Text("Space Race", x, y, 20, Gfx.Gold); y += 28;
        for (int i = 0; i < Defs.SpaceRace.Length; i++)
        {
            var sr = Defs.SpaceRace[i];
            if (G.Button(new Rectangle(x + i * (bw + 6), y, bw, 36), sr.Name, selected: _setup.SpaceRace == sr.Id, size: 18)) _setup.SpaceRace = sr.Id;
        }
        y += 42;
        y += G.Para(Defs.SpaceRaceById[_setup.SpaceRace].Desc, x, y, 16, w, Gfx.Muted) + 10;
        G.Text("Difficulty", x, y, 20, Gfx.Gold); y += 28;
        float dw = (w - 18) / 4;
        for (int i = 0; i < Defs.Difficulties.Length; i++)
        {
            var d = Defs.Difficulties[i];
            var name = c.Text(d.NameLoc) is { Length: > 0 } n && !n.StartsWith("LOC_") ? n : d.Name;
            if (G.Button(new Rectangle(x + i % 4 * (dw + 6), y + i / 4 * 40, dw, 34), name, selected: _setup.Difficulty == d.Id, tip: d.Desc, size: 16)) _setup.Difficulty = d.Id;
        }
        y += 90;
        G.Text("Act 2 ends", x, y, 20, Gfx.Gold); y += 28;
        for (int i = 0; i < Defs.GameLengths.Length; i++)
        {
            var gl = Defs.GameLengths[i];
            if (G.Button(new Rectangle(x + i * (bw + 6), y, bw, 36), $"{gl.Name} ({gl.EndYear})", selected: _setup.GameLength == gl.Id, size: 16)) _setup.GameLength = gl.Id;
        }
        y += 50;
        if (G.Button(new Rectangle(x, opt.Y + opt.Height - 70, w, 54), "Start Game", size: 24))
        {
            _setup.Civ = pick.Type;
            _setup.Leader = pick.Leader;
            Settings.Civ = pick.Leader; Settings.MapSize = _setup.MapSize; Settings.SpaceRace = _setup.SpaceRace; Settings.Difficulty = _setup.Difficulty; Settings.Rivals = _setup.Rivals;
            Settings.Save();
            Game = new SaveGame { Phase = "act1", Act1 = Act1.NewGame(_setup, c) };
            SaveNow();
            EnterAct1();
        }
    }
}
