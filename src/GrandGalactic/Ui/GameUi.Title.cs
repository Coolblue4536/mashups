using System.Numerics;
using Raylib_cs;

namespace GrandGalactic;

/// <summary>The title screen (Continue, New Empire, Settings, Quit) and the new-empire setup screen.</summary>
public sealed partial class GameUi
{
    static readonly Color Gold = new(240, 200, 90, 255), Panel = new(14, 17, 36, 235), PanelHover = new(30, 36, 66, 240),
                          Picked = new(44, 66, 130, 245), Dim = new(150, 160, 190, 255);

    bool _titleSettings;
    string? _saveInfo;

    /// <summary>Goes to the title screen and reads the save's summary once (for the Continue button).</summary>
    void ShowTitle()
    {
        _screen = Screen.Title;
        _titleSettings = false;
        _saveInfo = null;
        if (!Sim.HasSave) return;
        var d = Sim.ReadSave(Sim.SavePath);
        if (d == null) return;
        var diff = Defs.Difficulties.FirstOrDefault(x => x.Id == d.Difficulty)?.Name ?? "";
        var ethic = Defs.Ethics.FirstOrDefault(x => x.Id == d.Ethic)?.Name ?? "";
        _saveInfo = $"{ethic} empire, moon {d.Moon} · {diff}";
    }

    /// <summary>The background art with a dark wash on the left, so text there stays readable.</summary>
    void Backdrop(byte alpha)
    {
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        if (Tex("st_title_background") is { } bg)
        {
            // Cover the window without stretching the art.
            float s = Math.Max(sw / (float)bg.Width, sh / (float)bg.Height), w = bg.Width * s, h = bg.Height * s;
            Raylib.DrawTexturePro(bg, new Rectangle(0, 0, bg.Width, bg.Height), new Rectangle((sw - w) / 2, (sh - h) / 2, w, h), Vector2.Zero, 0,
                new Color((byte)255, (byte)255, (byte)255, alpha));
        }
        Raylib.DrawRectangleGradientH(0, 0, sw, sh, new Color(4, 6, 16, 235), new Color(4, 6, 16, 40));
        Raylib.DrawRectangleGradientV(0, sh - 160, sw, 160, new Color(4, 6, 16, 0), new Color(4, 6, 16, 200));
    }

    // ---------- title ----------

    void DrawTitle()
    {
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        Backdrop(200);
        float x = Math.Max(60, sw * 0.08f), y = sh * 0.2f;
        const string title = "GRAND GALACTIC";
        float ts = Math.Clamp(sw / 15f, 64, 110);
        Text(title, x + 4, y + 5, ts, new Color(0, 0, 0, 160));
        Text(title, x, y, ts, Color.RayWhite);
        y += ts + 6;
        Raylib.DrawRectangle((int)x, (int)y, (int)Measure(title, ts).X, 3, Gold);
        y += 16;
        Text("STELLARIS  ×  STACKLANDS", x + 2, y, 22, Gold);
        y += 38;
        Text("Build a star empire one card at a time.", x + 2, y, 24, Dim);
        y += 70;

        float bw = 340, bh = 58;
        if (_saveInfo != null)
        {
            if (MenuButton(new Rectangle(x, y, bw, bh), "Continue", _saveInfo, true)) LoadGame();
            y += bh + 18 + 14;
        }
        if (MenuButton(new Rectangle(x, y, bw, bh), "New Empire", null, _saveInfo == null)) { _screen = Screen.Empire; return; }
        y += bh + 14;
        if (MenuButton(new Rectangle(x, y, bw, bh), "Settings", null, false)) _titleSettings = true;
        y += bh + 14;
        if (MenuButton(new Rectangle(x, y, bw, bh), "Quit", null, false)) _quit = true;

        Text("Art and sound come from your own Stellaris and Stacklands installs.", x + 2, sh - 44, 16, new Color(130, 136, 160, 255));
        if (_titleSettings) DrawSettingsPanel();
    }

    /// <summary>A large menu button; the main one is gold-edged. An optional note sits under the label.</summary>
    bool MenuButton(Rectangle r, string label, string? note, bool main)
    {
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r) && !_titleSettings;
        if (note != null) r.Height += 18;
        Raylib.DrawRectangleRounded(r, 0.2f, 8, hover ? PanelHover : main ? new Color(34, 44, 86, 240) : Panel);
        Raylib.DrawRectangleRoundedLinesEx(r, 0.2f, 8, 2, main ? Gold : hover ? new Color(140, 160, 220, 220) : new Color(70, 80, 120, 200));
        if (hover) Raylib.DrawRectangle((int)r.X - 14, (int)(r.Y + 12), 5, (int)r.Height - 24, Gold);
        Text(label, r.X + 26, r.Y + (note != null ? 12 : (r.Height - 28) / 2), 28, Color.RayWhite);
        if (note != null) Text(note, r.X + 26, r.Y + 46, 17, Dim);
        return hover && Raylib.IsMouseButtonPressed(MouseButton.Left);
    }

    void DrawSettingsPanel()
    {
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 150));
        var r = new Rectangle(sw / 2f - 240, sh / 2f - 200, 480, 400);
        Raylib.DrawRectangleRounded(r, 0.05f, 6, new Color(12, 14, 32, 250));
        Raylib.DrawRectangleRoundedLinesEx(r, 0.05f, 6, 2, Gold);
        Text("Settings", r.X + r.Width / 2 - Measure("Settings", 34).X / 2, r.Y + 18, 34, Color.RayWhite);
        float x = r.X + 40, w = r.Width - 80;
        float y = SettingsRows(x, r.Y + 80, w);
        Text($"Stellaris: {_stellarisPath ?? "-"}", x, y + 6, 14, Color.Gray);
        Text($"Stacklands: {_stacklandsPath ?? "-"}", x, y + 26, 14, Color.Gray);
        if (Button(new Rectangle(x, r.Y + r.Height - 66, w, 46), "Done", false, 22)
            || Raylib.IsKeyPressed(KeyboardKey.Escape)) _titleSettings = false;
    }

    /// <summary>Volume and on/off settings, shared by the title and the pause menu. Returns the y below them.</summary>
    float SettingsRows(float x, float y, float w)
    {
        void Volume(string label, Func<float> get, Action<float> set)
        {
            Text(label, x, y + 8, 19, Color.LightGray);
            var v = $"{get() * 100:0}%";
            Text(v, x + w - 65 - Measure(v, 19).X / 2, y + 8, 19, Color.RayWhite);
            if (Button(new Rectangle(x + w - 130, y, 40, 36), "-", false, 22)) { set(Math.Clamp(MathF.Round(get() * 10 - 1) / 10, 0, 1)); _settings.Save(); }
            if (Button(new Rectangle(x + w - 40, y, 40, 36), "+", false, 22)) { set(Math.Clamp(MathF.Round(get() * 10 + 1) / 10, 0, 1)); _settings.Save(); }
            y += 46;
        }
        void Toggle(string label, bool on, Action flip)
        {
            Text(label, x, y + 8, 19, Color.LightGray);
            if (Button(new Rectangle(x + w - 130, y, 130, 36), on ? "On" : "Off", on, 18)) { flip(); _settings.Save(); }
            y += 46;
        }
        Volume("Music volume", () => _settings.MusicVolume, v => { _settings.MusicVolume = v; if (_music is { } m) Raylib.SetMusicVolume(m, v); });
        Volume("Sound volume", () => _settings.Volume, v => _settings.Volume = v);
        Toggle("Tutorial", _settings.Tutorial, () => _settings.Tutorial = !_settings.Tutorial);
        Toggle("Fullscreen", _settings.Fullscreen, () => { _settings.Fullscreen = !_settings.Fullscreen; ApplyWindowMode(); });
        return y;
    }

    // ---------- new empire ----------

    /// <summary>A pickable row: icon, name and a short description; the picked one is gold-edged.</summary>
    bool PickRow(Rectangle r, Texture2D? icon, string name, string desc, bool picked)
    {
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
        Raylib.DrawRectangleRounded(r, 0.12f, 8, picked ? Picked : hover ? PanelHover : Panel);
        if (picked) Raylib.DrawRectangleRoundedLinesEx(r, 0.12f, 8, 2, Gold);
        float isz = Math.Min(72, r.Height - 20);
        if (icon is { } t) DrawFit(t, new Rectangle(r.X + 14, r.Y + (r.Height - isz) / 2, isz, isz), Color.White);
        float tx = r.X + isz + 30;
        bool compact = r.Height < 86; // short windows: smaller text so two lines still fit
        float ns = compact ? 21 : 24, ds = compact ? 15 : 17, dy = compact ? 33 : 40;
        Text(name, tx, r.Y + (compact ? 7 : 12), ns, picked ? Gold : Color.RayWhite);
        Wrapped(desc, tx, r.Y + dy, r.Width - (tx - r.X) - 14, ds, Color.LightGray, Math.Max(1, (int)((r.Height - dy + 6) / (ds * 1.15f))));
        return hover && Raylib.IsMouseButtonPressed(MouseButton.Left);
    }

    void Heading(int n, string label, float x, float y)
    {
        Raylib.DrawCircle((int)x + 14, (int)y + 14, 14, Gold);
        var s = n.ToString();
        Text(s, x + 14 - Measure(s, 20).X / 2, y + 4, 20, new Color(20, 20, 30, 255));
        Text(label, x + 38, y + 1, 24, Color.RayWhite);
    }

    void DrawEmpire()
    {
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        Backdrop(110);
        float m = 50, gap = 44, colW = (sw - 2 * m - gap) / 2, lx = m, rx = m + colW + gap;
        Text("New Empire", m, 30, 44, Color.RayWhite);
        bool shortWin = sh < 800;
        if (!shortWin) Text("Pick a species and ethics, set the galaxy, then begin.", m + 2, 82, 20, Dim);
        if (Button(new Rectangle(sw - m - 130, 36, 130, 44), "Back", false, 20) || Raylib.IsKeyPressed(KeyboardKey.Escape)) { ShowTitle(); return; }

        float top = shortWin ? 100 : 130, bottom = sh - 110;
        // Rows shrink on short windows so everything fits above the Begin bar.
        float rowH = Math.Clamp((bottom - top - 40) / 4 - 10, 68, 100);
        // Species rows also leave room for the Galaxy settings below them.
        float spRowH = Math.Clamp((bottom - top - 40 - 194) / 3 - 10, 64, rowH);

        // 1. Species (by traits, or a portrait from the player's Stellaris when it has flat portraits).
        Heading(1, "Species", lx, top);
        float py = top + 40;
        var ports = _res.St?.Portraits ?? new List<PortraitInfo>();
        if (ports.Count == 0)
        {
            foreach (var sp in Defs.Species)
            {
                if (PickRow(new Rectangle(lx, py, colW, spRowH), Tex(sp.Art), sp.Name, sp.Desc, sp == _species)) _species = sp;
                py += spRowH + 10;
            }
        }
        else
        {
            int cell = (int)Math.Min(104, colW / 6), cols = Math.Max(1, (int)(colW / cell)), rows = 3, perPage = cols * rows;
            int pages = Math.Max(1, (ports.Count + perPage - 1) / perPage);
            for (int i = 0; i < perPage; i++)
            {
                int idx = _portraitPage * perPage + i;
                if (idx >= ports.Count) break;
                var r = new Rectangle(lx + (i % cols) * cell, py + (i / cols) * cell, cell - 8, cell - 8);
                bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
                Raylib.DrawRectangleRounded(r, 0.1f, 6, idx == _portrait ? Picked : hover ? PanelHover : Panel);
                if (idx == _portrait) Raylib.DrawRectangleRoundedLinesEx(r, 0.1f, 6, 2, Gold);
                if (PortraitTex(idx) is { } t) DrawFit(t, new Rectangle(r.X + 4, r.Y + 4, r.Width - 8, r.Height - 8), Color.White);
                if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left)) _portrait = idx;
            }
            py += rows * cell;
            if (pages > 1)
            {
                if (Button(new Rectangle(lx, py, 50, 36), "<")) _portraitPage = (_portraitPage + pages - 1) % pages;
                Text($"{_portraitPage + 1}/{pages}", lx + 62, py + 8, 20, Color.LightGray);
                if (Button(new Rectangle(lx + 120, py, 50, 36), ">")) _portraitPage = (_portraitPage + 1) % pages;
            }
            if (_portrait >= 0) Text(ports[_portrait].Group + " / " + ports[_portrait].Name, lx + 190, py + 8, 18, Color.LightGray);
            py += 46;
        }

        // 3. Galaxy: difficulty and moon length, under the species.
        float gy = Math.Min(py + 14, bottom - 170);
        Heading(3, "Galaxy", lx, gy);
        gy += 40;
        float bw = (colW - 24) / 4f;
        Text("Difficulty", lx, gy, 19, Color.LightGray);
        Text(_diff.Desc, lx + 110, gy + 2, 16, Dim);
        for (int i = 0; i < Defs.Difficulties.Length; i++)
            if (Button(new Rectangle(lx + i * (bw + 8), gy + 26, bw, 38), Defs.Difficulties[i].Name, Defs.Difficulties[i] == _diff, 18))
                _diff = Defs.Difficulties[i];
        gy += 76;
        Text("Moon length", lx, gy, 19, Color.LightGray);
        Text(_moon.Desc, lx + 130, gy + 2, 16, Dim);
        for (int i = 0; i < Defs.MoonLengths.Length; i++)
            if (Button(new Rectangle(lx + i * (bw + 8), gy + 26, bw, 38), $"{Defs.MoonLengths[i].Name} ({Defs.MoonLengths[i].Seconds}s)", Defs.MoonLengths[i] == _moon, 17))
                _moon = Defs.MoonLengths[i];

        // 2. Ethics.
        Heading(2, "Ethics", rx, top);
        float ey = top + 40;
        foreach (var e in Defs.Ethics)
        {
            if (PickRow(new Rectangle(rx, ey, colW, rowH), Tex(e.Art), _res.St?.Text(e.NameLoc) ?? e.Name, e.Desc, e == _ethic)) _ethic = e;
            ey += rowH + 10;
        }

        // Begin bar: tutorial toggle and the Begin button, bottom right.
        bool ready = _portrait >= 0 || ports.Count == 0;
        var begin = new Rectangle(sw - m - 260, sh - 88, 260, 60);
        if (Button(new Rectangle(begin.X - 220, begin.Y + 8, 200, 44), _settings.Tutorial ? "Tutorial: On" : "Tutorial: Off", _settings.Tutorial, 18))
        {
            _settings.Tutorial = !_settings.Tutorial;
            _settings.Save();
        }
        bool hoverBegin = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), begin);
        Raylib.DrawRectangleRounded(begin, 0.25f, 8, hoverBegin ? new Color(255, 214, 110, 255) : Gold);
        var label = ready ? "Begin" : "Pick a species";
        Text(label, begin.X + (begin.Width - Measure(label, 30).X) / 2, begin.Y + 15, 30, new Color(24, 20, 10, 255));
        if (ready && hoverBegin && Raylib.IsMouseButtonPressed(MouseButton.Left)) StartRun();
        string summary = $"{_species.Name} · {_res.St?.Text(_ethic.NameLoc) ?? _ethic.Name} · {_diff.Name} · {_moon.Name} moons";
        if (ports.Count > 0) summary = $"{_res.St?.Text(_ethic.NameLoc) ?? _ethic.Name} · {_diff.Name} · {_moon.Name} moons";
        Text(summary, m, sh - 68, 20, Dim);
    }
}
