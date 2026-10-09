using System.Numerics;
using Raylib_cs;

namespace GrandGalactic;

public sealed partial class GameUi
{
    Board CurBoard => _sim!.Boards[Math.Clamp(_board, 0, _sim.Boards.Count - 1)];
    Rectangle BoardView => new(0, TopBar, Raylib.GetScreenWidth() - RightPanel, Raylib.GetScreenHeight() - TopBar);

    void Update(float dt)
    {
        if (_music is { } m) Raylib.UpdateMusicStream(m);
        if (_screen is not (Screen.Play or Screen.End) || _sim == null) return;
        var sim = _sim;

        if (Raylib.IsKeyPressed(KeyboardKey.Space)) _paused = !_paused;
        if (Raylib.IsKeyPressed(KeyboardKey.Tab)) _codex = !_codex;
        if (Raylib.IsKeyPressed(KeyboardKey.One)) _speed = 1;
        if (Raylib.IsKeyPressed(KeyboardKey.Two)) _speed = 2;
        if (Raylib.IsKeyPressed(KeyboardKey.Three)) _speed = 4;
        for (int i = 0; i < Math.Min(9, sim.Boards.Count); i++)
            if (Raylib.IsKeyPressed(KeyboardKey.F1 + i)) _board = i;

        // Camera: right or middle drag pans, wheel zooms, WASD pans.
        var view = BoardView;
        _cam.Offset = new Vector2(view.X + view.Width / 2, view.Y + view.Height / 2);
        if (Raylib.IsMouseButtonDown(MouseButton.Right) || Raylib.IsMouseButtonDown(MouseButton.Middle))
            _cam.Target -= Raylib.GetMouseDelta() / _cam.Zoom;
        var pan = new Vector2((Raylib.IsKeyDown(KeyboardKey.D) ? 1 : 0) - (Raylib.IsKeyDown(KeyboardKey.A) ? 1 : 0),
                              (Raylib.IsKeyDown(KeyboardKey.S) ? 1 : 0) - (Raylib.IsKeyDown(KeyboardKey.W) ? 1 : 0));
        _cam.Target += pan * 900 * dt / _cam.Zoom;
        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0 && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), view))
        {
            var before = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), _cam);
            _cam.Zoom = Math.Clamp(_cam.Zoom * (1 + wheel * 0.1f), 0.3f, 1.6f);
            _cam.Target += before - Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), _cam);
        }
        _cam.Target = Vector2.Clamp(_cam.Target, Vector2.Zero, new Vector2(Sim.BoardW, Sim.BoardH));

        if (_screen == Screen.Play) HandleMouse();
        if (!_paused && _screen == Screen.Play) sim.Update(dt * _speed);

        foreach (var msg in sim.Messages) Toast(msg);
        sim.Messages.Clear();
        int hits = 0;
        foreach (var e in sim.Events) if (e != SimEvent.Hit || hits++ < 1) Play(e);
        sim.Events.Clear();
        _toasts.RemoveAll(t => _clock - t.t > 7);
        if (sim.State != RunState.Playing && _screen == Screen.Play) _screen = Screen.End;
    }

    Vector2 MouseWorld => Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), _cam);

    /// <summary>Topmost card under a world point: (stack, index in stack).</summary>
    (Stack s, int i)? Pick(Vector2 p, Stack? except = null)
    {
        var b = CurBoard;
        for (int k = b.Stacks.Count - 1; k >= 0; k--)
        {
            var s = b.Stacks[k];
            if (s == except) continue;
            for (int i = s.Cards.Count - 1; i >= 0; i--)
            {
                var r = new Rectangle(s.Pos.X, s.Pos.Y + i * Sim.StackStep, Sim.CardW, i == s.Cards.Count - 1 ? Sim.CardH : Sim.StackStep);
                if (Raylib.CheckCollisionPointRec(p, r)) return (s, i);
            }
        }
        return null;
    }

    Battle? PickBattle(Vector2 p) => CurBoard.Battles.FirstOrDefault(bt => Raylib.CheckCollisionPointRec(p, BattleRect(bt)));

    static Rectangle BattleRect(Battle bt)
    {
        int n = Math.Max(bt.Players.Count, bt.Hostiles.Count);
        return new Rectangle(bt.Pos.X - 20, bt.Pos.Y - 40, Math.Max(2, n) * (Sim.CardW + 12) + 30, Sim.CardH * 2 + 90);
    }

    void HandleMouse()
    {
        var sim = _sim!;
        var mouse = Raylib.GetMousePosition();
        bool overBoard = Raylib.CheckCollisionPointRec(mouse, BoardView);

        if (_drag == null && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            // Board tabs.
            for (int i = 0; i < sim.Boards.Count; i++)
                if (Raylib.CheckCollisionPointRec(mouse, TabRect(i))) { _board = i; return; }
            if (overBoard && Pick(MouseWorld) is { } hit && !hit.s.Cards[hit.i].Def.IsHostile
                && Defs.Category[hit.s.Cards[hit.i].Def.Category].Draggable)
            {
                _drag = sim.Split(hit.s, hit.i);
                _drag.Dragging = true;
                _dragOffset = MouseWorld - _drag.Pos;
                var b = CurBoard;
                b.Stacks.Remove(_drag);
                b.Stacks.Add(_drag); // draw on top
                sim.Events.Add(SimEvent.Pickup);
            }
        }

        if (_drag != null)
        {
            _drag.Pos = MouseWorld - _dragOffset;
            if (Raylib.IsMouseButtonReleased(MouseButton.Left))
            {
                var d = _drag;
                _drag = null;
                d.Dragging = false;
                d.Dirty = true;
                Drop(d, mouse);
            }
        }
    }

    void Drop(Stack d, Vector2 mouse)
    {
        var sim = _sim!;
        // Right panel: packs and market.
        foreach (var (pack, r) in PackRects())
            if (Raylib.CheckCollisionPointRec(mouse, r))
            {
                var at = Raylib.GetScreenToWorld2D(new Vector2(BoardView.Width - 200, BoardView.Y + BoardView.Height / 2), _cam);
                if (!sim.BuyPack(d, pack, at))
                    Toast(d.Cards.All(c => c.Def.Id == "energy") ? $"{pack.Name} costs {sim.PackCost(pack)} Energy Credits." : "Packs are bought with Energy Credits only.");
                Bounce(d);
                return;
            }
        if (Raylib.CheckCollisionPointRec(mouse, MarketRect()))
        {
            int got = sim.Sell(d);
            Toast(got > 0 ? $"Sold for {got} Energy Credits." : "Nothing in that stack can be sold.");
            Bounce(d);
            return;
        }
        for (int i = 0; i < sim.Boards.Count; i++)
            if (Raylib.CheckCollisionPointRec(mouse, TabRect(i)))
            {
                if (sim.MoveToBoard(d, sim.Boards[i])) { Toast($"Moved to {sim.Boards[i].Name}."); _board = i; }
                else Toast(i == _board ? "Already here." : "Only stacks with a ship can travel between systems.");
                Bounce(d);
                return;
            }
        var w = MouseWorld;
        if (PickBattle(w) is { } bt) { sim.JoinBattleOf(d, bt); return; }
        if (Pick(w, d) is { } hit)
        {
            var target = hit.s.Cards[hit.i];
            if (target.Def.IsHostile) { sim.Attack(d, target); return; }
            if (!sim.StackOnto(d, hit.s) && hit.s.Cards.Count + d.Cards.Count > Defs.Rules.MaxStack) Toast("That stack is full.");
            return;
        }
        sim.Events.Add(SimEvent.Drop);
    }

    void Bounce(Stack d)
    {
        if (!d.Cards.Any()) return;
        d.Pos = Raylib.GetScreenToWorld2D(new Vector2(BoardView.Width - 320, BoardView.Y + BoardView.Height / 2), _cam);
        d.Pos = Vector2.Clamp(d.Pos, Vector2.Zero, new Vector2(Sim.BoardW - Sim.CardW, Sim.BoardH - Sim.CardH));
    }

    Rectangle TabRect(int i) => new(8 + i * 112, 6, 106, 46);

    IEnumerable<(PackDef, Rectangle)> PackRects()
    {
        float x = Raylib.GetScreenWidth() - RightPanel + 12, y = TopBar + 40;
        foreach (var p in _sim!.AvailablePacks)
        {
            yield return (p, new Rectangle(x, y, RightPanel - 24, 74));
            y += 82;
        }
    }

    Rectangle MarketRect() => new(Raylib.GetScreenWidth() - RightPanel + 12, Raylib.GetScreenHeight() - 130, RightPanel - 24, 112);

    // ---------- drawing ----------

    void DrawPlay()
    {
        var sim = _sim!;
        var view = BoardView;
        var b = CurBoard;

        // Board background: the Stacklands table, or plain space.
        Raylib.BeginScissorMode((int)view.X, (int)view.Y, (int)view.Width, (int)view.Height);
        Raylib.BeginMode2D(_cam);
        if (Tex("sl_board_bg") is { } bg)
            Raylib.DrawTexturePro(bg, new Rectangle(0, 0, bg.Width, bg.Height), new Rectangle(0, 0, Sim.BoardW, Sim.BoardH), Vector2.Zero, 0, new Color(150, 160, 200, 255));
        else
        {
            Raylib.DrawRectangle(0, 0, (int)Sim.BoardW, (int)Sim.BoardH, new Color(16, 20, 40, 255));
            var rng = new Random(b.Index * 7919 + 3);
            for (int i = 0; i < 260; i++) Raylib.DrawCircle(rng.Next((int)Sim.BoardW), rng.Next((int)Sim.BoardH), rng.Next(1, 3), new Color(255, 255, 255, rng.Next(40, 160)));
        }
        Raylib.DrawRectangleLinesEx(new Rectangle(0, 0, Sim.BoardW, Sim.BoardH), 6, new Color(120, 140, 220, 120));
        Text(b.Name.ToUpperInvariant(), 30, 20, 56, new Color(255, 255, 255, 50));
        if (b.Kind.Length > 0) Text(b.Kind, 34, 82, 30, new Color(255, 255, 255, 40));

        foreach (var s in b.Stacks) if (!s.Dragging) DrawStack(s);
        foreach (var bt in b.Battles) DrawBattle(bt);
        foreach (var s in b.Stacks) if (s.Dragging) DrawStack(s);
        Raylib.EndMode2D();
        Raylib.EndScissorMode();

        DrawTopBar();
        DrawRightPanel();
        DrawTooltip();
        DrawToasts();
        if (_codex) DrawCodex();
        if (_paused) Text("PAUSED (Space)", view.Width / 2 - 100, TopBar + 14, 30, Color.Yellow);
        if (_screen == Screen.End) DrawEnd();
    }

    void DrawStack(Stack s)
    {
        for (int i = 0; i < s.Cards.Count; i++)
            DrawCard(s.Cards[i], new Rectangle(s.Pos.X, s.Pos.Y + i * Sim.StackStep, Sim.CardW, Sim.CardH), s.Dragging);
        if (s.Active != null && s.Duration > 0)
        {
            var r = new Rectangle(s.Pos.X, s.Pos.Y - 18, Sim.CardW, 12);
            Raylib.DrawRectangleRounded(r, 0.5f, 6, new Color(0, 0, 0, 180));
            Raylib.DrawRectangleRounded(new Rectangle(r.X + 2, r.Y + 2, (r.Width - 4) * Math.Clamp(s.Progress / s.Duration, 0, 1), r.Height - 4), 0.5f, 6, new Color(120, 230, 140, 255));
        }
    }

    void DrawCard(Card c, Rectangle r, bool lifted)
    {
        var cat = Defs.Category[c.Def.Category];
        var col = Hex(cat.Color);
        if (lifted) Raylib.DrawRectangleRounded(new Rectangle(r.X + 8, r.Y + 10, r.Width, r.Height), 0.08f, 6, new Color(0, 0, 0, 90));
        if (Tex(cat.FrameRef) is { } frame)
            Raylib.DrawTexturePro(frame, new Rectangle(0, 0, frame.Width, frame.Height), r, Vector2.Zero, 0, col);
        else
        {
            Raylib.DrawRectangleRounded(r, 0.08f, 6, col);
            Raylib.DrawRectangleRoundedLinesEx(r, 0.08f, 6, 3, new Color(30, 30, 40, 255));
        }
        var art = new Rectangle(r.X + 12, r.Y + 34, r.Width - 24, r.Height - 64);
        Raylib.DrawRectangleRec(art, new Color(10, 12, 26, 230));
        if (Tex(c.Def.Art) is { } t) DrawFit(t, art, Color.White);
        var name = _res.CardName(c.Def.Id);
        float size = 17;
        while (size > 11 && Measure(name, size).X > r.Width - 14) size -= 1;
        Text(name, r.X + 8, r.Y + 8, size, new Color(25, 25, 35, 255));
        // footer: value, hp, claim state
        if (c.Def.Value > 0) Text($"${c.Def.Value}", r.X + 8, r.Y + r.Height - 26, 18, new Color(40, 40, 50, 255));
        if (c.Def.Hp > 0)
        {
            var hp = $"{c.Hp}/{c.MaxHp}";
            Text(hp, r.X + r.Width - 8 - Measure(hp, 18).X, r.Y + r.Height - 26, 18, c.Hp < c.MaxHp ? new Color(170, 20, 20, 255) : new Color(40, 40, 50, 255));
        }
        if (c.Def.IsPlanet && c.Def.ColonizeWith != "none")
        {
            var tag = c.Claimed ? "Colonised" : "Unclaimed";
            Text(tag, r.X + r.Width / 2 - Measure(tag, 14).X / 2, r.Y + r.Height - 24, 14, c.Claimed ? new Color(10, 90, 30, 255) : new Color(90, 60, 10, 255));
        }
    }

    void DrawBattle(Battle bt)
    {
        var r = BattleRect(bt);
        Raylib.DrawRectangleRounded(r, 0.05f, 6, new Color(120, 20, 30, 110));
        Raylib.DrawRectangleRoundedLinesEx(r, 0.05f, 6, 4, new Color(255, 90, 90, 220));
        Text("BATTLE - drop ships here to join", r.X + 16, r.Y + 8, 20, Color.RayWhite);
        for (int i = 0; i < bt.Hostiles.Count; i++)
            DrawCard(bt.Hostiles[i], new Rectangle(r.X + 20 + i * (Sim.CardW + 12), r.Y + 36, Sim.CardW, Sim.CardH), false);
        for (int i = 0; i < bt.Players.Count; i++)
            DrawCard(bt.Players[i], new Rectangle(r.X + 20 + i * (Sim.CardW + 12), r.Y + 50 + Sim.CardH, Sim.CardW, Sim.CardH), false);
    }

    void DrawTopBar()
    {
        var sim = _sim!;
        int sw = Raylib.GetScreenWidth();
        Raylib.DrawRectangle(0, 0, sw, TopBar, new Color(14, 16, 34, 255));
        for (int i = 0; i < sim.Boards.Count; i++)
        {
            var r = TabRect(i);
            bool fight = sim.Boards[i].Battles.Count > 0;
            Raylib.DrawRectangleRounded(r, 0.3f, 6, i == _board ? new Color(70, 100, 190, 255) : fight ? new Color(130, 40, 50, 255) : new Color(32, 38, 66, 255));
            var label = sim.Boards[i].Name;
            float size = 17;
            while (size > 11 && Measure(label, size).X > r.Width - 12) size -= 1;
            Text(label, r.X + 6, r.Y + 5, size, Color.RayWhite);
            var kind = i == 0 ? "Capital" : sim.Boards[i].Kind.Length > 0 ? sim.Boards[i].Kind : "Guardian";
            float ks = 12;
            while (ks > 9 && Measure(kind, ks).X > r.Width - 12) ks -= 1;
            Text(kind, r.X + 6, r.Y + 27, ks, new Color(200, 210, 255, 200));
        }
        int food = sim.AllCards.Count(c => c.Def.Id == "food");
        int eat = sim.AllCards.Where(c => c.Def.Category == "person").Sum(c => c.Def.FoodUpkeep);
        int energy = sim.AllCards.Count(c => c.Def.Id == "energy");
        var info = $"{sim.Diff.Name}  |  Moon {sim.Moon}  |  Act {sim.Act}  |  Food {food}/{eat} needed  |  Energy {energy}  |  x{_speed:0}";
        var w = Measure(info, 20).X;
        Text(info, sw - w - 20, 8, 20, food < eat ? new Color(255, 160, 120, 255) : Color.RayWhite);
        var bar = new Rectangle(sw - w - 20, 36, w, 10);
        Raylib.DrawRectangleRec(bar, new Color(40, 44, 70, 255));
        Raylib.DrawRectangleRec(new Rectangle(bar.X, bar.Y, bar.Width * sim.MoonTime / sim.MoonSeconds, bar.Height), new Color(240, 210, 120, 255));
    }

    void DrawRightPanel()
    {
        var sim = _sim!;
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        Raylib.DrawRectangle(sw - RightPanel, TopBar, RightPanel, sh - TopBar, new Color(14, 16, 34, 245));
        Text("Packs - drop Energy", sw - RightPanel + 12, TopBar + 10, 19, Color.LightGray);
        var art = Tex("sl_pack_art");
        foreach (var (p, r) in PackRects())
        {
            bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r) && _drag != null;
            Raylib.DrawRectangleRounded(r, 0.12f, 6, hover ? new Color(80, 110, 200, 255) : new Color(36, 42, 74, 255));
            if (art is { } a) DrawFit(a, new Rectangle(r.X + 4, r.Y + 4, 54, r.Height - 8), Color.White);
            Text(p.Name, r.X + 64, r.Y + 8, 21, Color.RayWhite);
            Text($"{sim.PackCost(p)} Energy, {p.Draws} cards", r.X + 64, r.Y + 38, 15, new Color(240, 210, 120, 255));
        }
        var m = MarketRect();
        bool mh = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), m) && _drag != null;
        Raylib.DrawRectangleRounded(m, 0.12f, 6, mh ? new Color(160, 130, 50, 255) : new Color(70, 58, 30, 255));
        Text(Defs.Rules.SellSlotName, m.X + 14, m.Y + 12, 26, Color.RayWhite);
        Wrapped("Drop cards here to sell them for Energy Credits", m.X + 14, m.Y + 46, m.Width - 28, 16, Color.LightGray);
        Text("Tab recipes - Space pause", sw - RightPanel + 12, m.Y - 44, 14, Color.Gray);
        Text("1-3 speed - F1-F9 systems", sw - RightPanel + 12, m.Y - 24, 14, Color.Gray);
    }

    void DrawTooltip()
    {
        if (_drag != null || !Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), BoardView)) return;
        var w = MouseWorld;
        Card? c = null;
        Stack? s = null;
        if (Pick(w) is { } hit) { s = hit.s; c = hit.s.Cards[hit.i]; }
        else if (PickBattle(w) is { } bt)
        {
            var r = BattleRect(bt);
            int col = (int)((w.X - r.X - 20) / (Sim.CardW + 12));
            var row = w.Y - r.Y < 36 + Sim.CardH ? bt.Hostiles : bt.Players;
            if (col >= 0 && col < row.Count) c = row[col];
        }
        if (c == null) return;
        var lines = _res.CardName(c.Def.Id) + "\n" + c.Def.Desc;
        if (s?.Active != null) lines += $"\nWorking: {s.Active.Desc} ({Math.Max(0, s.Duration - s.Progress):0}s)";
        var m = Raylib.GetMousePosition() + new Vector2(18, 18);
        float width = 340;
        var parts = lines.Split('\n');
        float h = 16 + parts.Length * 26 + parts.Sum(p => Measure(p, 18).X > width - 20 ? 22 * (int)(Measure(p, 18).X / (width - 20)) : 0);
        if (m.X + width > Raylib.GetScreenWidth()) m.X -= width + 36;
        if (m.Y + h > Raylib.GetScreenHeight()) m.Y -= h + 36;
        Raylib.DrawRectangleRounded(new Rectangle(m.X, m.Y, width, h), 0.1f, 6, new Color(10, 12, 26, 235));
        float y = m.Y + 8;
        for (int i = 0; i < parts.Length; i++)
            y += Wrapped(parts[i], m.X + 10, y, width - 20, i == 0 ? 22 : 18, i == 0 ? Color.RayWhite : Color.LightGray) + 4;
    }

    void DrawToasts()
    {
        float y = Raylib.GetScreenHeight() - 50;
        foreach (var (text, t) in Enumerable.Reverse(_toasts).Take(5))
        {
            byte a = (byte)(255 * Math.Clamp(7 - (_clock - t), 0, 1));
            var sz = Measure(text, 20);
            Raylib.DrawRectangleRounded(new Rectangle(16, y - 6, sz.X + 24, 34), 0.3f, 6, new Color((byte)10, (byte)12, (byte)26, (byte)(a * 0.85f)));
            Text(text, 28, y, 20, new Color((byte)255, (byte)255, (byte)255, a));
            y -= 42;
        }
    }

    void DrawCodex()
    {
        var sim = _sim!;
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        var r = new Rectangle(80, 80, sw - RightPanel - 160, sh - 160);
        Raylib.DrawRectangleRounded(r, 0.03f, 6, new Color(10, 12, 26, 245));
        Text("Recipes you know (Tab to close)", r.X + 24, r.Y + 18, 28, Color.RayWhite);
        float y = r.Y + 64, x = r.X + 24, colW = (r.Width - 48) / 2;
        int col = 0;
        var known = Defs.Recipes.Where(rc => sim.Discovered.Contains(rc.Id) || rc.Id.StartsWith("b_") || rc.Id.StartsWith("x_survey")
            || rc.Id is "w_yield" or "s_science" or "s_construction" || (rc.RequiresTech != "none" && sim.Techs.Contains(rc.RequiresTech))).ToList();
        foreach (var rc in known)
        {
            Text("- " + rc.Desc, x + col * colW, y, 17, sim.Discovered.Contains(rc.Id) ? Color.RayWhite : Color.LightGray);
            y += 24;
            if (y > r.Y + r.Height - 30) { y = r.Y + 64; col++; if (col > 1) break; }
        }
        Text($"{Defs.Recipes.Length - known.Count} more to discover...", r.X + 24, r.Y + r.Height - 30, 17, Color.Gray);
    }

    void DrawEnd()
    {
        var sim = _sim!;
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 170));
        var title = sim.State == RunState.Won ? "THE GALAXY IS SAVED" : "YOUR EMPIRE HAS FALLEN";
        Text(title, sw / 2f - Measure(title, 56).X / 2, sh / 2f - 120, 56, sim.State == RunState.Won ? Color.Gold : new Color(255, 110, 100, 255));
        var why = $"{sim.EndReason}  (Moon {sim.Moon}, {sim.Boards.Count} systems)";
        Text(why, sw / 2f - Measure(why, 24).X / 2, sh / 2f - 40, 24, Color.RayWhite);
        if (Button(new Rectangle(sw / 2f - 130, sh / 2f + 30, 260, 60), "New run", false, 28)) { _sim = null; _screen = Screen.Empire; }
    }
}
