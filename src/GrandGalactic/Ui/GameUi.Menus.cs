using System.Numerics;
using Raylib_cs;

namespace GrandGalactic;

/// <summary>Menus over the table: a card's build/refit menu, the Esc menu (save, load, settings, exit), the Blueprint
/// book and the end screen.</summary>
public sealed partial class GameUi
{
    // ---------- shared bits ----------

    /// <summary>What a blueprint makes, in a few words: "Shipyard", "Research Red Laser", "Claim the system".</summary>
    string RecipeTitle(RecipeDef r)
    {
        var give = r.Outputs.SelectMany(o => o.Give).FirstOrDefault();
        if (r.Effect == "learn") return $"Research {_res.CardName(r.Station)}";
        if (r.Effect.StartsWith("repeat:"))
        {
            var tid = r.Effect[7..];
            return $"{_res.CardName(tid).Replace(" (repeatable)", "")} level {(_sim?.RepLevels.GetValueOrDefault(tid) ?? 0) + 1}";
        }
        if (r.Effect == "set_flag:claimed") return r.Outputs.Length > 0 ? "Colonise a planet" : "Build an outpost";
        if (r.Outputs.Length > 1 && r.Inputs.FirstOrDefault(i => !Sim.IsResource(i.Card) && !i.Card.StartsWith("tag:")) is { } what)
            return $"Explore the {_res.CardName(what.Card)}";
        if (give != null && give.Card != "station.yield")
            return (give.N > 1 ? $"{give.N} " : "") + _res.CardName(give.Card);
        return r.Effect switch
        {
            "claim_system" => "Claim the system",
            "set_flag:claimed" => "Build an outpost",
            "repair" => "Repair a warship",
            "open_board:random" => "Survey a new system",
            "open_board:guardian" => "Trace a guardian signal",
            _ => give?.Card == "station.yield" ? (r.Station == "tag:star" ? "Study a star" : "Work a district or planet") : r.Desc,
        };
    }

    /// <summary>The card whose picture stands for a blueprint: what it makes, else its station.</summary>
    static string? RecipeThumb(RecipeDef r)
    {
        if (r.Effect.StartsWith("repeat:")) return r.Effect[7..];
        var give = r.Outputs.SelectMany(o => o.Give).FirstOrDefault(g => g.Card != "station.yield");
        if (give != null && Defs.Card.ContainsKey(give.Card)) return give.Card;
        if (Defs.Card.ContainsKey(r.Station)) return r.Station;
        var inp = r.Inputs.FirstOrDefault(i => Defs.Card.ContainsKey(i.Card) && !Sim.IsResource(i.Card));
        return inp?.Card ?? r.Station switch { "tag:star" => "yellow_star", "tag:workplace" => "agriculture_district", "tag:habitable" => "continental_world", _ => null };
    }

    void DrawThumb(string? cardId, Rectangle box)
    {
        Raylib.DrawRectangleRounded(box, 0.15f, 6, new Color(16, 20, 38, 255));
        if (cardId != null && Defs.Card.TryGetValue(cardId, out var d))
        {
            var col = Hex(Defs.Category[d.Category].Color);
            DrawCardArt(d, new Rectangle(box.X + 2, box.Y + 2, box.Width - 4, box.Height - 4), col);
            Raylib.DrawRectangleRoundedLinesEx(box, 0.15f, 6, 2, col);
        }
    }

    /// <summary>One input as a chip: an icon and a count (red when the pool is short). Returns its width.</summary>
    float Chip(RecipeInput i, float x, float y, float h, bool checkPool)
    {
        var sim = _sim!;
        bool res = Sim.IsResource(i.Card);
        string label = res ? $"{i.N}" : i.Card.StartsWith("tag:") ? sim.InputName(i) : (i.N > 1 ? $"{i.N} " : "") + _res.CardName(i.Card);
        bool shortOf = res && checkPool && sim.Have(i.Card) < i.N;
        float ts = h * 0.58f, w = (res || !i.Card.StartsWith("tag:") ? h : 0) + Measure(label, ts).X + 12;
        var r = new Rectangle(x, y, w, h);
        Raylib.DrawRectangleRounded(r, 0.4f, 6, shortOf ? new Color(80, 30, 30, 255) : new Color(34, 40, 70, 255));
        float tx = x + 6;
        if (res && Tex(Defs.Card[i.Card].Art) is { } icon) { DrawFit(icon, new Rectangle(x + 3, y + 2, h - 4, h - 4), Color.White); tx = x + h; }
        else if (!i.Card.StartsWith("tag:")) { DrawThumb(i.Card, new Rectangle(x + 2, y + 2, h - 4, h - 4)); tx = x + h; }
        Text(label, tx, y + (h - ts) / 2 - 1, ts, shortOf ? new Color(255, 160, 140, 255) : Color.RayWhite);
        return w;
    }

    // ---------- a card's menu: build orders, refits, the fleet's admiral ----------

    Card? _menuCard;
    Stack? _menuStack;
    Vector2 _menuAt;
    Rectangle _menuRect;

    void OpenCardMenu(Stack s, Card c, Vector2 at)
    {
        var sim = _sim!;
        if (s.Root.EmpireId is { } eid && sim.EmpireOf(eid) is { } emp) { _diplo = emp; return; }
        bool any = sim.OrdersFor(c).Any(r => sim.Blueprint(r) != Sim.BlueprintState.Locked) || c.Parts.Count > 0 || c.Admiral != null;
        if (!any) return;
        _menuCard = c;
        _menuStack = s;
        _menuAt = at;
    }

    void CloseCardMenu() { _menuCard = null; _menuStack = null; }

    void DrawCardMenu()
    {
        var sim = _sim!;
        var c = _menuCard!;
        var s = _menuStack!;
        var orders = sim.OrdersFor(c).Where(r => sim.Blueprint(r) != Sim.BlueprintState.Locked).ToList();
        const float W = 420, rowH = 58;
        float h = 56 + (orders.Count > 0 ? 30 + orders.Count * (rowH + 6) : 0) + (c.Parts.Count > 0 ? 30 + c.Parts.Count * 44 : 0) + (c.Admiral != null ? 74 : 0)
                  + (s.Order != null ? 40 + s.Queue.Count * 34 : 0) + 10;
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        h = Math.Min(h, sh - TopBar - 20);
        var r = new Rectangle(Math.Clamp(_menuAt.X + 14, 8, sw - W - 8), Math.Clamp(_menuAt.Y - 30, TopBar + 8, sh - h - 8), W, h);
        _menuRect = r;
        _uiRects.Add(r);
        Raylib.DrawRectangleRounded(r, 0.04f, 6, new Color(10, 12, 28, 248));
        Raylib.DrawRectangleRoundedLinesEx(r, 0.04f, 6, 2, new Color(120, 140, 220, 200));
        DrawThumb(c.Def.Id, new Rectangle(r.X + 12, r.Y + 10, 36, 36));
        Text(_res.CardName(c.Def.Id), r.X + 58, r.Y + 10, 22, Color.RayWhite);
        Text("Esc or click outside to close", r.X + 58, r.Y + 34, 13, Color.Gray);
        float y = r.Y + 56;
        var mouse = Raylib.GetMousePosition();
        bool click = Raylib.IsMouseButtonPressed(MouseButton.Left);
        Raylib.BeginScissorMode((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
        if (s.Order != null)
        {
            Text($"Now: {RecipeTitle(s.Order)}", r.X + 14, y + 6, 17, new Color(140, 230, 150, 255));
            if (Button(new Rectangle(r.X + r.Width - 104, y, 90, 28), "Cancel", false, 15))
            {
                // Cancelling the current job starts the next queued one.
                var next = s.Queue.FirstOrDefault();
                var rest = s.Queue.Skip(1).ToList();
                sim.SetOrder(s, next);
                foreach (var q in rest) s.Queue.Add(q);
                Toast("Order cancelled.");
            }
            y += 40;
            for (int i = 0; i < s.Queue.Count; i++)
            {
                Text($"{i + 2}. {RecipeTitle(s.Queue[i])}", r.X + 24, y + 4, 16, Color.LightGray);
                if (Button(new Rectangle(r.X + r.Width - 104, y, 90, 26), "Remove", false, 14)) { s.Queue.RemoveAt(i); break; }
                y += 34;
            }
        }
        if (orders.Count > 0)
        {
            bool queueing = s.Order != null && s.Order.Tag == "build";
            Text(queueing ? $"Add to the queue ({1 + s.Queue.Count}/{Sim.QueueMax}) - paid from your pool" : "Build - the cost comes from your pool", r.X + 14, y + 4, 16, new Color(240, 200, 90, 255));
            y += 30;
            foreach (var o in orders)
            {
                var row = new Rectangle(r.X + 10, y, r.Width - 20, rowH);
                bool hover = Raylib.CheckCollisionPointRec(mouse, row);
                bool current = s.Order == o;
                Raylib.DrawRectangleRounded(row, 0.15f, 6, current ? new Color(40, 80, 60, 255) : hover ? new Color(44, 52, 92, 255) : new Color(24, 28, 52, 255));
                DrawThumb(RecipeThumb(o), new Rectangle(row.X + 6, row.Y + 5, rowH - 10, rowH - 10));
                Text(RecipeTitle(o), row.X + rowH + 4, row.Y + 6, 18, Color.RayWhite);
                var t = $"{o.Time:0}s";
                Text(t, row.X + row.Width - Measure(t, 15).X - 10, row.Y + 8, 15, Color.LightGray);
                float cx = row.X + rowH + 4;
                foreach (var i in o.Inputs) cx += Chip(sim.InputNow(o, i), cx, row.Y + 30, 22, true) + 5;
                if (hover && click)
                {
                    bool queued = s.Order != null && s.Order.Tag == "build" && o.Tag == "build";
                    if (sim.QueueOrder(s, o) is { } full) { Toast(full); Raylib.EndScissorMode(); return; }
                    var cards = o.Inputs.Where(i => !Sim.IsResource(i.Card)).ToList();
                    Toast(queued ? $"{RecipeTitle(o)} queued ({1 + s.Queue.Count}/{Sim.QueueMax})."
                        : cards.Count > 0 ? $"{RecipeTitle(o)}: now add {string.Join(" and ", cards.Select(sim.InputName))} to the stack."
                        : sim.Shortfall(o) is { } need ? $"{RecipeTitle(o)} ordered. {need}: it starts when your pool has enough." : $"{RecipeTitle(o)} ordered.");
                    if (queued && 1 + s.Queue.Count < Sim.QueueMax) { Raylib.EndScissorMode(); return; } // keep the menu open to queue more
                    CloseCardMenu();
                    Raylib.EndScissorMode();
                    return;
                }
                y += rowH + 6;
            }
        }
        if (c.Parts.Count > 0)
        {
            Text(c.Battle != null ? "Fitted parts (can't refit mid-battle)" : "Fitted parts - take one off to refit", r.X + 14, y + 4, 16, new Color(240, 200, 90, 255));
            y += 30;
            for (int i = 0; i < c.Parts.Count; i++)
            {
                var p = c.Parts[i];
                var row = new Rectangle(r.X + 10, y, r.Width - 20, 38);
                Raylib.DrawRectangleRounded(row, 0.2f, 6, new Color(24, 28, 52, 255));
                if (Tex("st_comp_" + p.Id) is { } icon) DrawFit(icon, new Rectangle(row.X + 6, row.Y + 3, 32, 32), Color.White);
                Text(_res.CardName(p.Id), row.X + 46, row.Y + 9, 17, Color.RayWhite);
                if (Button(new Rectangle(row.X + row.Width - 96, row.Y + 5, 88, 28), "Remove", false, 15))
                {
                    if (sim.Unfit(c, i) is { Length: > 0 } why) Toast(why);
                    else Toast($"{_res.CardName(p.Id)} taken off; it's beside the ship. Drop a better part on the ship to fit it.");
                    break;
                }
                y += 44;
            }
        }
        if (c.Admiral != null)
        {
            Text("Fleet Admiral", r.X + 14, y + 4, 16, new Color(240, 200, 90, 255));
            Wrapped($"Commands this fleet: its ships hit {Defs.Card["admiral"].BoostMult - 1:P0} harder.", r.X + 14, y + 26, r.Width - 140, 14, Color.LightGray, 2);
            if (Button(new Rectangle(r.X + r.Width - 104, y + 22, 90, 28), "Relieve", false, 15)) { sim.RelieveAdmiral(c); Toast("The Admiral steps off the fleet."); }
        }
        Raylib.EndScissorMode();
    }

    // ---------- the Market: trade surplus from the pool for Energy ----------

    bool _market;
    Rectangle _marketRect;

    void DrawMarket()
    {
        var sim = _sim!;
        var items = ShownResources().Where(id => id != "energy" && sim.Have(id) > 0).ToList();
        var m = MarketRect();
        float w = 380, h = 64 + Math.Max(1, items.Count) * 44;
        var r = new Rectangle(m.X - w - 12, Math.Max(TopBar + 8, m.Y + m.Height - h), w, h);
        _marketRect = r;
        _uiRects.Add(r);
        Raylib.DrawRectangleRounded(r, 0.05f, 6, new Color(14, 12, 6, 248));
        Raylib.DrawRectangleRoundedLinesEx(r, 0.05f, 6, 2, new Color(220, 180, 80, 220));
        Text("Market - trade for Energy", r.X + 14, r.Y + 10, 20, Color.RayWhite);
        Text("Half price. Click outside or Esc to close.", r.X + 14, r.Y + 36, 14, Color.Gray);
        if (items.Count == 0) { Text("Nothing in your pool to trade yet.", r.X + 14, r.Y + 66, 16, Color.LightGray); return; }
        float y = r.Y + 62;
        foreach (var id in items)
        {
            if (Tex(Defs.Card[id].Art) is { } icon) DrawFit(icon, new Rectangle(r.X + 12, y + 4, 30, 30), Color.White);
            Text($"{_res.CardName(id)}  {sim.Have(id)}", r.X + 50, y + 10, 17, Color.RayWhite);
            int n = Math.Min(5, sim.Have(id));
            if (Button(new Rectangle(r.X + r.Width - 196, y + 4, 92, 32), $"{n} > +{Sim.TradeValue(id, n)}", false, 15))
                if (sim.SellResource(id, n) is { Length: > 0 } why) Toast(why);
            int all = sim.Have(id);
            if (Button(new Rectangle(r.X + r.Width - 98, y + 4, 86, 32), $"All +{Sim.TradeValue(id, all)}", false, 15))
                if (sim.SellResource(id, all) is { Length: > 0 } why) Toast(why);
            y += 44;
        }
    }

    // ---------- Esc menu: save, load, settings, exit ----------

    bool _escMenu, _quit;

    void SaveGame(bool quiet = false)
    {
        if (_sim?.War != null) { if (!quiet) Toast("Finish the invasion first (bring the fleet home), then save."); return; }
        try
        {
            _sim!.SaveTo(Sim.SavePath, _res.PortraitPath ?? "");
            if (!quiet) Toast("Game saved.");
        }
        catch (Exception e)
        {
            Log.Info($"save failed: {e}");
            Toast("Could not save the game (see the log).");
        }
    }

    void LoadGame()
    {
        var d = Sim.ReadSave(Sim.SavePath);
        if (d == null) { Toast("Could not read the save."); return; }
        try
        {
            if (d.Portrait.Length > 0) { _res.PortraitPath = d.Portrait; _tex.Remove("st_portrait_player"); }
            _sim = new Sim(d, id => _res.CardName(id));
            _ethic = _sim.Ethic;
            _camGoal = null;
            _camZoomGoal = null;
            _cam = new Camera2D { Zoom = 0.62f, Target = _sim.Home.Center };
            _screen = Screen.Play;
            _escMenu = false;
            _codex = false;
            _paused = false;
            CloseCardMenu();
            _tutorialSeen.Clear();
            foreach (var t in Defs.Tutorial) if (_sim.StepDone(t)) _tutorialSeen.Add(t.Id);
            _toasts.Clear();
            Toast($"Loaded: moon {_sim.Moon}, {_sim.Systems.Count} systems.");
        }
        catch (Exception e)
        {
            Log.Info($"load failed: {e}");
            Toast("That save could not be loaded (see the log).");
        }
    }

    void ApplyWindowMode()
    {
        bool on = Raylib.IsWindowState(ConfigFlags.BorderlessWindowMode);
        if (on != _settings.Fullscreen) Raylib.ToggleBorderlessWindowed();
    }

    void DrawEscMenu()
    {
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 150));
        var r = new Rectangle(sw / 2f - 240, sh / 2f - 265, 480, 530);
        Raylib.DrawRectangleRounded(r, 0.05f, 6, new Color(12, 14, 32, 250));
        Raylib.DrawRectangleRoundedLinesEx(r, 0.05f, 6, 2, new Color(240, 200, 90, 200));
        Text("Paused", r.X + r.Width / 2 - Measure("Paused", 36).X / 2, r.Y + 18, 36, Color.RayWhite);
        float x = r.X + 40, w = r.Width - 80, y = r.Y + 76;
        if (Button(new Rectangle(x, y, w, 46), "Resume", false, 22)) _escMenu = false;
        y += 56;
        if (Button(new Rectangle(x, y, w / 2 - 6, 46), "Save game", false, 22)) SaveGame();
        bool has = Sim.HasSave;
        if (Button(new Rectangle(x + w / 2 + 6, y, w / 2 - 6, 46), has ? "Load game" : "No save yet", false, 22) && has) { LoadGame(); return; }
        y += 64;
        Text("Settings", x, y, 22, new Color(240, 200, 90, 255));
        y += 34;
        void Volume(string label, Func<float> get, Action<float> set)
        {
            Text(label, x, y + 8, 19, Color.LightGray);
            var v = $"{get() * 100:0}%";
            Text(v, x + w - 130 + 33 - Measure(v, 19).X / 2 + 32, y + 8, 19, Color.RayWhite);
            if (Button(new Rectangle(x + w - 130, y, 40, 36), "-", false, 22)) { set(Math.Clamp(MathF.Round(get() * 10 - 1) / 10, 0, 1)); _settings.Save(); }
            if (Button(new Rectangle(x + w - 40, y, 40, 36), "+", false, 22)) { set(Math.Clamp(MathF.Round(get() * 10 + 1) / 10, 0, 1)); _settings.Save(); }
            y += 46;
        }
        Volume("Music volume", () => _settings.MusicVolume, v => { _settings.MusicVolume = v; if (_music is { } m) Raylib.SetMusicVolume(m, v); });
        Volume("Sound volume", () => _settings.Volume, v => _settings.Volume = v);
        void Toggle(string label, bool on, Action flip)
        {
            Text(label, x, y + 8, 19, Color.LightGray);
            if (Button(new Rectangle(x + w - 130, y, 130, 36), on ? "On" : "Off", on, 18)) { flip(); _settings.Save(); }
            y += 46;
        }
        Toggle("Tutorial", _settings.Tutorial, () => _settings.Tutorial = !_settings.Tutorial);
        Toggle("Fullscreen", _settings.Fullscreen, () => { _settings.Fullscreen = !_settings.Fullscreen; ApplyWindowMode(); });
        y += 14;
        if (Button(new Rectangle(x, y, w / 2 - 6, 46), "Main menu", false, 20)) { SaveGame(quiet: true); _escMenu = false; _sim = null; _screen = Screen.Empire; return; }
        if (Button(new Rectangle(x + w / 2 + 6, y, w / 2 - 6, 46), "Quit game", false, 20)) { SaveGame(quiet: true); _quit = true; }
        Text("Main menu and Quit save your game first. Esc resumes.", x, r.Y + r.Height - 30, 15, Color.Gray);
    }

    // ---------- the Blueprint book ----------

    int _bookTab;
    float _bookScroll;

    /// <summary>The Blueprint book: every blueprint you know, by tab, as picture tiles. Unresearched blueprints don't appear.</summary>
    void DrawCodex()
    {
        var sim = _sim!;
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        var r = new Rectangle(40, TopBar + 12, sw - RightPanel - 80, sh - TopBar - 40);
        _uiRects.Add(r);
        Raylib.DrawRectangle(0, TopBar, sw - RightPanel, sh - TopBar, new Color(0, 0, 0, 120));
        Raylib.DrawRectangleRounded(r, 0.015f, 6, new Color(12, 14, 30, 250));
        Raylib.DrawRectangleRoundedLinesEx(r, 0.015f, 6, 2, new Color(240, 200, 90, 190));
        Text("Blueprint book", r.X + 24, r.Y + 14, 32, Color.RayWhite);
        Text("Game paused while open - Tab or Esc closes - Q/E or click to change tab - wheel scrolls", r.X + 290, r.Y + 26, 16, Color.Gray);

        var tabs = Defs.Rules.BlueprintTabs.Select(t => t.Tab).ToList();
        if (Raylib.IsKeyPressed(KeyboardKey.Q)) { _bookTab = (_bookTab + tabs.Count - 1) % tabs.Count; _bookScroll = 0; }
        if (Raylib.IsKeyPressed(KeyboardKey.E)) { _bookTab = (_bookTab + 1) % tabs.Count; _bookScroll = 0; }
        bool Shown(RecipeDef x) => sim.Blueprint(x) != Sim.BlueprintState.Locked;
        float tx = r.X + 24;
        for (int i = 0; i < tabs.Count; i++)
        {
            int known = Defs.Recipes.Count(x => Sim.BlueprintTab(x) == tabs[i] && Shown(x));
            var label = $"{tabs[i]}  {known}";
            float w = Measure(label, 19).X + 30;
            if (Button(new Rectangle(tx, r.Y + 58, w, 38), label, i == _bookTab, 19)) { _bookTab = i; _bookScroll = 0; }
            tx += w + 8;
        }

        var area = new Rectangle(r.X + 20, r.Y + 108, r.Width - 40, r.Height - 122);
        var entries = Defs.Recipes.Where(x => Sim.BlueprintTab(x) == tabs[_bookTab] && Shown(x))
            .OrderBy(x => sim.Blueprint(x) == Sim.BlueprintState.Made ? 0 : 1).ToList();
        bool research = tabs[_bookTab] == "Research";
        float top = 0;
        if (research)
        {
            // How research works, above the technologies already learned.
            var help = new Rectangle(area.X, area.Y, area.Width, 92);
            Raylib.DrawRectangleRounded(help, 0.1f, 6, new Color(22, 34, 60, 255));
            Text("How research works", help.X + 14, help.Y + 8, 19, new Color(240, 200, 90, 255));
            Wrapped("Blueprint cards come from packs (Research, Military, Industry, Frontier). Stack a Pop on one to research it, or a Scientist " +
                    "to do it twice as fast; Research is paid from your pool. Some need an earlier technology first (Red Laser before Blue Laser). " +
                    "Get a Scientist from the Research pack, or click a Research Lab, pick Scientist and add a Pop (+2 Research).",
                    help.X + 14, help.Y + 32, help.Width - 28, 15, Color.LightGray, 3);
            top = 104;
        }
        if (research || tabs[_bookTab] == "Ships & Parts") top += DrawHullLadder(new Rectangle(area.X, area.Y + top, area.Width, 96)) + 12;
        if (entries.Count == 0)
        {
            Text(research ? "Nothing researched yet." : "No blueprints here yet: research technologies to unlock them.", area.X + 10, area.Y + top + 10, 20, Color.Gray);
            return;
        }
        const float tileW = 372, tileH = 128, gap = 12;
        int cols = Math.Max(1, (int)((area.Width + gap) / (tileW + gap)));
        float tw = (area.Width - gap * (cols - 1)) / cols;
        int rows = (entries.Count + cols - 1) / cols;
        float total = top + rows * (tileH + gap);
        _bookScroll = Math.Clamp(_bookScroll - Raylib.GetMouseWheelMove() * 80, 0, Math.Max(0, total - area.Height));
        Raylib.BeginScissorMode((int)area.X, (int)area.Y, (int)area.Width, (int)area.Height);
        for (int k = 0; k < entries.Count; k++)
        {
            var e = entries[k];
            var t = new Rectangle(area.X + (k % cols) * (tw + gap), area.Y + top + (k / cols) * (tileH + gap) - _bookScroll, tw, tileH);
            if (t.Y > area.Y + area.Height || t.Y + t.Height < area.Y) continue;
            DrawTile(e, t, research);
        }
        Raylib.EndScissorMode();
    }

    /// <summary>The road to bigger warships: each hull technology, known, next, or later. Returns the height used.</summary>
    float DrawHullLadder(Rectangle r)
    {
        var sim = _sim!;
        Raylib.DrawRectangleRounded(r, 0.1f, 6, new Color(22, 30, 54, 255));
        var next = sim.NextHull;
        Text("Warship hulls", r.X + 14, r.Y + 8, 18, new Color(240, 200, 90, 255));
        Text(next == null ? "All hulls researched." : $"Next: find a {_res.CardName(next)} blueprint (Military, Research or Frontier packs) and research it.",
             r.X + 150, r.Y + 10, 15, Color.LightGray);
        float x = r.X + 14, w = Math.Min(200, (r.Width - 28 - 4 * 24) / 5);
        for (int i = 0; i < Sim.HullLadder.Length; i++)
        {
            var id = Sim.HullLadder[i];
            bool known = sim.Techs.Contains(id), isNext = id == next;
            var box = new Rectangle(x, r.Y + 36, w, 50);
            Raylib.DrawRectangleRounded(box, 0.2f, 6, known ? new Color(30, 70, 45, 255) : isNext ? new Color(80, 64, 20, 255) : new Color(30, 32, 50, 255));
            Raylib.DrawRectangleRoundedLinesEx(box, 0.2f, 6, 1.5f, known ? new Color(120, 220, 140, 220) : isNext ? new Color(255, 210, 100, 230) : new Color(80, 86, 120, 200));
            var hullCard = Defs.Recipes.FirstOrDefault(q => q.RequiresTech == id && q.Id.StartsWith("s_"))?.Outputs[0].Give[0].Card;
            DrawThumb(hullCard ?? id, new Rectangle(box.X + 5, box.Y + 5, 40, 40));
            var name = hullCard != null ? _res.CardName(hullCard) : _res.CardName(id);
            float ns = 15;
            while (ns > 11 && Measure(name, ns).X > w - 56) ns -= 0.5f;
            Text(name, box.X + 52, box.Y + 7, ns, Color.RayWhite);
            Text(known ? "Researched" : isNext ? "Next" : "Later", box.X + 52, box.Y + 27, 13, known ? new Color(140, 230, 150, 255) : isNext ? new Color(255, 215, 110, 255) : Color.Gray);
            if (i < Sim.HullLadder.Length - 1) Text(">", x + w + 7, box.Y + 14, 20, Color.Gray);
            x += w + 24;
        }
        return r.Height;
    }

    void DrawTile(RecipeDef e, Rectangle t, bool research)
    {
        var sim = _sim!;
        bool made = sim.Blueprint(e) == Sim.BlueprintState.Made;
        Raylib.DrawRectangleRounded(t, 0.08f, 6, new Color(24, 28, 54, 255));
        Raylib.DrawRectangleRoundedLinesEx(t, 0.08f, 6, 1.5f, made ? new Color(120, 200, 140, 200) : new Color(80, 90, 140, 200));
        var thumb = new Rectangle(t.X + 10, t.Y + 10, 82, t.Height - 20);
        DrawThumb(RecipeThumb(e), thumb);
        float x = t.X + 104, w = t.Width - 114;
        var title = research ? _res.CardName(e.Station) : RecipeTitle(e);
        float ts = 20;
        while (ts > 13 && Measure(title, ts).X > w - 60) ts -= 0.5f;
        Text(title, x, t.Y + 10, ts, Color.RayWhite);
        var tag = research ? "Researched" : made ? "Made" : "New";
        var tagCol = made || research ? new Color(130, 220, 150, 255) : new Color(240, 200, 90, 255);
        Text(tag, t.X + t.Width - Measure(tag, 14).X - 10, t.Y + 12, 14, tagCol);
        if (research)
        {
            var unlocks = Defs.Recipes.Where(x => x.RequiresTech == e.Station && x.Effect != "learn").Select(RecipeTitle).ToList();
            var size = Defs.Rules.FleetSizeTechs.FirstOrDefault(a => a.Card == e.Station);
            var text = size != null ? $"Fleets hold up to {size.N} warships." : unlocks.Count > 0 ? "Unlocks: " + string.Join(", ", unlocks) : Defs.Card[e.Station].Desc;
            var next = Defs.Recipes.Where(x => x.Effect == "learn" && x.RequiresTech == e.Station).Select(x => _res.CardName(x.Station)).ToList();
            if (next.Count > 0) text += $"  Leads to: {string.Join(", ", next)}.";
            Wrapped(text, x, t.Y + 40, w, 15, Color.LightGray, 4);
            return;
        }
        // Where it's made, then what goes in.
        string station = e.Station switch
        {
            "tag:workplace" => "any workplace", "has:yield" => "anything with a yield", "tag:star" => "a star", "tag:habitable" => "a habitable planet",
            "tag:uninhabitable" => "an uninhabitable planet", var s when s.StartsWith("tag:") => s[4..], var s => _res.CardName(s),
        };
        Text($"On {station}" + (e.Order ? " (click it to order)" : "") + (e.Time > 0 ? $" - {e.Time:0}s" : ""), x, t.Y + 38, 14, new Color(190, 200, 235, 255));
        float cx = x, cy = t.Y + 60;
        foreach (var i in e.Inputs)
        {
            float cw = Measure(i.Card.StartsWith("tag:") ? sim.InputName(i) : _res.CardName(i.Card), 14).X + 40;
            if (cx + cw > t.X + t.Width - 8) { cx = x; cy += 28; }
            if (cy > t.Y + t.Height - 26) break;
            cx += Chip(sim.InputNow(e, i), cx, cy, 24, false) + 6;
        }
        if (e.Outputs.Length > 1)
            Wrapped("Finds one of: " + string.Join(" / ", e.Outputs.Select(o => string.Join(" + ", o.Give.Select(g => (g.N > 1 ? $"{g.N} " : "") + _res.CardName(g.Card))))),
                    x, cy + 30, w, 13, Color.Gray, 2);
    }

    // ---------- end of the run ----------

    void DrawEnd()
    {
        var sim = _sim!;
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 170));
        var title = sim.State == RunState.Won ? "THE GALAXY IS SAVED" : "YOUR EMPIRE HAS FALLEN";
        Text(title, sw / 2f - Measure(title, 56).X / 2, sh / 2f - 120, 56, sim.State == RunState.Won ? Color.Gold : new Color(255, 110, 100, 255));
        var why = $"{sim.EndReason}  (Moon {sim.Moon}, {sim.Systems.Count} systems)";
        Text(why, sw / 2f - Measure(why, 24).X / 2, sh / 2f - 40, 24, Color.RayWhite);
        if (sim.State == RunState.Won && Button(new Rectangle(sw / 2f - 130, sh / 2f + 30, 260, 60), "Keep playing", false, 28))
        {
            // Endless: the galaxy is saved, but your empire carries on - rivals keep growing, raids keep coming.
            sim.State = RunState.Playing;
            sim.Endless = true;
            _screen = Screen.Play;
            Toast("Endless play: your empire carries on. Rival empires keep growing; infinite research opens once enough blueprints are known.");
            return;
        }
        if (Button(new Rectangle(sw / 2f - 130, sh / 2f + (sim.State == RunState.Won ? 104 : 30), 260, 60), "New run", false, 28))
        {
            try { if (Sim.HasSave) File.Delete(Sim.SavePath); } catch { /* an old save is harmless */ }
            _sim = null;
            _screen = Screen.Empire;
        }
    }
}
