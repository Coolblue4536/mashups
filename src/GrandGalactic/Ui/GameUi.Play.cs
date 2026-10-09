using System.Numerics;
using Raylib_cs;

namespace GrandGalactic;

public sealed partial class GameUi
{
    Board Table => _sim!.Table;

    /// <summary>Smoothly scroll the camera to a star system.</summary>
    void GoTo(StarSystem z) => _camGoal = z.Center;
    Rectangle BoardView => new(0, TopBar, Raylib.GetScreenWidth() - RightPanel, Raylib.GetScreenHeight() - TopBar);

    void Update(float dt)
    {
        if (_music is { } m) Raylib.UpdateMusicStream(m);
        if (_screen is not (Screen.Play or Screen.End) || _sim == null) return;
        var sim = _sim;

        if (Raylib.IsKeyPressed(KeyboardKey.Space)) _paused = !_paused;
        if (Raylib.IsKeyPressed(KeyboardKey.Tab)) { _codex = !_codex; sim.Flags.Add("opened_book"); }
        if (Raylib.IsKeyPressed(KeyboardKey.One)) _speed = 1;
        if (Raylib.IsKeyPressed(KeyboardKey.Two)) _speed = 2;
        if (Raylib.IsKeyPressed(KeyboardKey.Three)) _speed = 4;
        for (int i = 0; i < Math.Min(9, sim.Systems.Count); i++)
            if (Raylib.IsKeyPressed(KeyboardKey.F1 + i)) GoTo(sim.Systems[i]);
        if (Raylib.IsKeyPressed(KeyboardKey.Z)) _camZoomGoal = _cam.Zoom > 0.3f ? 0.2f : 0.62f; // whole empire / close up
        foreach (var z in sim.NewSystems) { GoTo(z); Toast($"{z.Name} is now part of your table. Drag cards there freely."); }
        sim.NewSystems.Clear();

        // Camera: right or middle drag pans, wheel zooms, WASD pans.
        var view = BoardView;
        _cam.Offset = new Vector2(view.X + view.Width / 2, view.Y + view.Height / 2);
        if (Raylib.IsMouseButtonDown(MouseButton.Right) || Raylib.IsMouseButtonDown(MouseButton.Middle))
        {
            _cam.Target -= Raylib.GetMouseDelta() / _cam.Zoom;
            _camGoal = null;
        }
        if (_camGoal is { } goal)
        {
            _cam.Target = Vector2.Lerp(_cam.Target, goal, Math.Min(1, dt * 6));
            if (Vector2.Distance(_cam.Target, goal) < 4) _camGoal = null;
        }
        if (_camZoomGoal is { } zg)
        {
            _cam.Zoom += (zg - _cam.Zoom) * Math.Min(1, dt * 6);
            if (Math.Abs(_cam.Zoom - zg) < 0.005f) _camZoomGoal = null;
        }
        var pan = new Vector2((Raylib.IsKeyDown(KeyboardKey.D) ? 1 : 0) - (Raylib.IsKeyDown(KeyboardKey.A) ? 1 : 0),
                              (Raylib.IsKeyDown(KeyboardKey.S) ? 1 : 0) - (Raylib.IsKeyDown(KeyboardKey.W) ? 1 : 0));
        _cam.Target += pan * 900 * dt / _cam.Zoom;
        if (pan != Vector2.Zero) _camGoal = null;
        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0 && !_codex && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), view))
        {
            var before = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), _cam);
            _cam.Zoom = Math.Clamp(_cam.Zoom * (1 + wheel * 0.1f), 0.12f, 1.6f);
            _camZoomGoal = null;
            _cam.Target += before - Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), _cam);
        }
        _cam.Target = Vector2.Clamp(_cam.Target, sim.BoundsMin, sim.BoundsMax);

        if (_screen == Screen.Play) HandleMouse();
        sim.TutorialOn = TutorialShown;
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
        var b = Table;
        for (int k = b.Stacks.Count - 1; k >= 0; k--)
        {
            var s = b.Stacks[k];
            if (s == except || s.Traveling) continue;
            for (int i = s.Cards.Count - 1; i >= 0; i--)
            {
                var r = new Rectangle(s.Pos.X, s.Pos.Y + i * Sim.StackStep, Sim.CardW, i == s.Cards.Count - 1 ? Sim.CardH : Sim.StackStep);
                if (Raylib.CheckCollisionPointRec(p, r)) return (s, i);
            }
        }
        return null;
    }

    Battle? PickBattle(Vector2 p) => Table.Battles.FirstOrDefault(bt => Raylib.CheckCollisionPointRec(p, BattleRect(bt)));

    static Rectangle BattleRect(Battle bt)
    {
        var (p, s) = Sim.BattleArea(bt);
        return new Rectangle(p.X, p.Y, s.X, s.Y);
    }

    void HandleMouse()
    {
        var sim = _sim!;
        var mouse = Raylib.GetMousePosition();
        bool overBoard = Raylib.CheckCollisionPointRec(mouse, BoardView);

        if (_drag == null && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            // System names in the top bar scroll the camera there.
            var bar = BarSystems(out _);
            for (int i = 0; i < bar.Count; i++)
                if (Raylib.CheckCollisionPointRec(mouse, TabRect(i))) { GoTo(bar[i]); return; }
            if (overBoard && Pick(MouseWorld) is { } hit && !hit.s.Cards[hit.i].Def.IsHostile
                && Defs.Category[hit.s.Cards[hit.i].Def.Category].Draggable)
            {
                _drag = sim.Split(hit.s, hit.i);
                _dragFrom = _drag.Pos;
                _drag.Dragging = true;
                _dragOffset = MouseWorld - _drag.Pos;
                var b = Table;
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
        var dropPos = d.Pos;
        // Right panel: packs and market. The stack goes back where it came from, so packs open (and Energy from
        // sales appears) in the system the cards were picked up in.
        bool onPanel = PackRects().Any(pr => Raylib.CheckCollisionPointRec(mouse, pr.Item2)) || Raylib.CheckCollisionPointRec(mouse, MarketRect());
        if (onPanel) d.Pos = _dragFrom;
        foreach (var (pack, r) in PackRects())
            if (Raylib.CheckCollisionPointRec(mouse, r))
            {
                var at = _dragFrom + new Vector2(Sim.CardW + 60, 0);
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
        var w = MouseWorld;
        // Into another star system: only a stack with a ship can go, and the trip takes time.
        var fromSys = sim.SystemAt(_dragFrom + new Vector2(Sim.CardW / 2, Sim.CardH / 2));
        var toSys = sim.SystemAt(dropPos + new Vector2(Sim.CardW / 2, Sim.CardH / 2));
        if (toSys == null && fromSys != null) { d.Pos = _dragFrom; Toast("Cards live inside star systems - drop it on one."); return; }
        if (fromSys != null && toSys != null && fromSys != toSys)
        {
            d.Pos = _dragFrom;
            if (sim.StartTravel(d, dropPos))
                Toast($"En route to {toSys.Name}: arrives in {sim.TravelSeconds(fromSys, toSys):0} seconds.");
            else Toast($"Only stacks with a ship can travel to {toSys.Name}. Put your Pops and cargo on a ship.");
            return;
        }
        if (PickBattle(w) is { } bt) { sim.JoinBattleOf(d, bt); return; }
        if (Pick(w, d) is { } hit)
        {
            var target = hit.s.Cards[hit.i];
            if (target.Def.IsHostile) { sim.Attack(d, target); return; }
            // A single ship component dropped on a ship fits into a slot; an admiral takes command of a warship.
            if (d.Cards.Count == 1 && (d.Root.Def.Category == "component" || (d.Root.Def.Id == "admiral" && target.Def.HasTag("warship"))))
            {
                var host = hit.s.Cards.LastOrDefault(x => d.Root.Def.Id == "admiral" ? x.Def.HasTag("warship") : Sim.CanFit(x)) ?? target;
                var part = d.Root;
                var why = part.Def.Id == "admiral" ? sim.AssignAdmiral(part, host) : sim.Fit(part, host);
                if (why == null) { Toast(part.Def.Id == "admiral" ? $"Admiral now commands the {_res.CardName(host.Def.Id)}." : $"{_res.CardName(part.Def.Id)} fitted to {_res.CardName(host.Def.Id)}."); return; }
                if (part.Def.Category == "component") { Toast(why); d.Pos = _dragFrom; return; }
            }
            if (!sim.StackOnto(d, hit.s) && hit.s.Cards.Count + d.Cards.Count > Defs.Rules.MaxStack) Toast("That stack is full.");
            return;
        }
        sim.Events.Add(SimEvent.Drop);
    }

    void Bounce(Stack d)
    {
        if (d.Cards.Any()) d.Pos = _dragFrom;
    }

    Rectangle TabRect(int i) => new(8 + i * 112, 6, 106, 46);

    /// <summary>The systems listed in the top bar: owned ones first, then the most recently found, as many as fit.</summary>
    List<StarSystem> BarSystems(out int hidden)
    {
        var sim = _sim!;
        int fit = Math.Max(3, (Raylib.GetScreenWidth() - 820) / 112);
        var order = sim.Systems.Where(z => z.Claimed).Concat(sim.Systems.Where(z => !z.Claimed).Reverse()).ToList();
        hidden = Math.Max(0, order.Count - fit);
        return order.Take(fit).ToList();
    }

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
        var b = Table;

        // The table: each star system is an area, drawn on the Stacklands board texture (or plain space).
        Raylib.BeginScissorMode((int)view.X, (int)view.Y, (int)view.Width, (int)view.Height);
        Raylib.BeginMode2D(_cam);
        _inWorld = true;
        var bg = Tex("sl_board_bg");
        var stars = new Random(7);
        for (int i = 0; i < 500; i++)
            Raylib.DrawCircle((int)(sim.BoundsMin.X - 600 + stars.Next((int)(sim.BoundsMax.X - sim.BoundsMin.X + 1200))),
                (int)(sim.BoundsMin.Y - 600 + stars.Next((int)(sim.BoundsMax.Y - sim.BoundsMin.Y + 1200))), stars.Next(1, 4), new Color(255, 255, 255, stars.Next(30, 140)));
        foreach (var z in sim.Systems)
        {
            var r = new Rectangle(z.Origin.X, z.Origin.Y, z.Size.X, z.Size.Y);
            if (bg is { } t) Raylib.DrawTexturePro(t, new Rectangle(0, 0, t.Width, t.Height), r, Vector2.Zero, 0, new Color(150, 160, 200, 255));
            else Raylib.DrawRectangleRec(r, new Color(18, 24, 48, 255));
            bool fight = b.Battles.Any(bt => z.Contains(bt.Pos));
            Raylib.DrawRectangleLinesEx(r, z.Claimed ? 8 : 4, fight ? new Color(255, 90, 90, 220) : z.Claimed ? new Color(240, 200, 90, 210) : new Color(120, 130, 160, 110));
            if (!z.Claimed)
            {
                var hint = sim.ClaimBlock(z) is { } why && why.StartsWith("Claim limit") ? why : "Unclaimed: Construction Ship + 2 Influence on its star";
                float hs = Math.Max(24, 13 / _cam.Zoom);
                while (hs > 12 && Measure(hint, hs).X > z.Size.X - 56) hs -= 1;
                Text(hint, z.Origin.X + 28, z.Origin.Y + z.Size.Y - hs - 20, hs, new Color(255, 230, 160, 110));
            }
            // Labels keep a readable size on screen however far you zoom out.
            float big = Math.Max(52, 26 / _cam.Zoom), small = Math.Max(28, 15 / _cam.Zoom);
            Text(z.Name.ToUpperInvariant(), z.Origin.X + 24, z.Origin.Y + 16, big, new Color(255, 255, 255, 70));
            var kind = z.Index == 0 ? "Capital system" : z.Kind.Length > 0 ? z.Kind : "Guardian system";
            Text(kind, z.Origin.X + 28, z.Origin.Y + 22 + big, small, new Color(255, 255, 255, 55));
        }
        foreach (var s in b.Stacks.Where(s => s.Traveling))
        {
            var a = s.TravelFrom + new Vector2(Sim.CardW / 2, Sim.CardH / 2);
            var e = s.TravelTo + new Vector2(Sim.CardW / 2, Sim.CardH / 2);
            Raylib.DrawLineEx(a, e, 5, new Color(140, 200, 255, 120));
            Raylib.DrawCircleV(e, 14, new Color(140, 200, 255, 160));
            var left = $"{Math.Max(0, s.TravelDur - s.TravelT):0}s";
            Text(left, s.Pos.X + Sim.CardW + 8, s.Pos.Y, 28, new Color(180, 220, 255, 230));
        }
        foreach (var s in b.Stacks) if (!s.Dragging) DrawStack(s);
        foreach (var bt in b.Battles) DrawBattle(bt);
        foreach (var s in b.Stacks) if (s.Dragging) DrawStack(s);
        Raylib.EndMode2D();
        _inWorld = false;
        Raylib.EndScissorMode();

        DrawTopBar();
        DrawRightPanel();
        DrawTooltip();
        DrawTutorial();
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

    static readonly Color Ink = new(34, 30, 40, 255);

    /// <summary>Fills the box with the picture, cropping its edges, so wide event pictures fill the art window.</summary>
    static void DrawCover(Texture2D t, Rectangle box, Color tint)
    {
        float s = Math.Max(box.Width / t.Width, box.Height / t.Height);
        float sw = box.Width / s, sh = box.Height / s;
        Raylib.DrawTexturePro(t, new Rectangle((t.Width - sw) / 2, (t.Height - sh) / 2, sw, sh), box, Vector2.Zero, 0, tint);
    }

    void DrawCard(Card c, Rectangle r, bool lifted)
    {
        var cat = Defs.Category[c.Def.Category];
        var col = Hex(cat.Color);
        // A soft shadow under every card (deeper while held), then the Stacklands frame in the category colour.
        float lift = lifted ? 9 : 3;
        Raylib.DrawRectangleRounded(new Rectangle(r.X + lift * 0.6f, r.Y + lift, r.Width, r.Height), 0.1f, 6, new Color(0, 0, 0, lifted ? 90 : 55));
        if (Tex(cat.FrameRef) is { } frame)
            Raylib.DrawTexturePro(frame, new Rectangle(0, 0, frame.Width, frame.Height), r, Vector2.Zero, 0, col);
        else
        {
            Raylib.DrawRectangleRounded(r, 0.08f, 6, col);
            Raylib.DrawRectangleRoundedLinesEx(r, 0.08f, 6, 3, Ink);
        }

        // Title.
        var name = _res.CardName(c.Def.Id);
        float size = 16;
        while (size > 10 && Measure(name, size).X > r.Width - 18) size -= 0.5f;
        Text(name, r.X + 9, r.Y + 8 + (16 - size) / 2, size, Ink);

        // Art window: a rounded pane of space with the picture inside. Wide pictures fill it; icons sit with a margin.
        var art = new Rectangle(r.X + 9, r.Y + 28, r.Width - 18, r.Height - 60);
        Raylib.DrawRectangleRounded(art, 0.12f, 6, new Color(16, 20, 38, 255));
        if (Tex(c.Def.Art) is { } t)
        {
            float aspect = (float)t.Width / t.Height, box = art.Width / art.Height;
            if (aspect > box * 1.35f || aspect < box / 1.35f || (t.Width >= 120 && t.Height >= 120)) DrawCover(t, art, Color.White);
            else DrawFit(t, new Rectangle(art.X + 6, art.Y + 6, art.Width - 12, art.Height - 12), Color.White);
        }
        Raylib.DrawRectangleRoundedLinesEx(art, 0.12f, 6, 2, new Color(col.R / 3, col.G / 3, col.B / 3, 200));

        // Footer: hull and bars for fighting cards, else the value; planets say whether they are yours.
        float fy = r.Y + r.Height - 27;
        bool fighter = c.MaxHp > 0 && (c.Def.Attack > 0 || c.MaxShield > 0 || c.MaxArmor > 0 || c.Def.IsHostile || c.Def.Slots > 0);
        if (fighter)
        {
            float bx = r.X + 34, bw = r.Width - 44;
            void Bar(float y, float v, float max, Color fill)
            {
                Raylib.DrawRectangleRounded(new Rectangle(bx, y, bw, 5), 1f, 4, new Color(0, 0, 0, 110));
                if (max > 0 && v > 0) Raylib.DrawRectangleRounded(new Rectangle(bx, y, Math.Max(5, bw * Math.Clamp(v / max, 0, 1)), 5), 1f, 4, fill);
            }
            float hk = c.Hp / Math.Max(1, c.MaxHp);
            int bars = (c.MaxShield > 0 ? 1 : 0) + (c.MaxArmor > 0 ? 1 : 0) + 1;
            float y0 = fy + 13 - bars * 4;
            if (c.MaxShield > 0) { Bar(y0, c.Shield, c.MaxShield, new Color(90, 170, 255, 255)); y0 += 8; }
            if (c.MaxArmor > 0) { Bar(y0, c.Armor, c.MaxArmor, new Color(235, 190, 90, 255)); y0 += 8; }
            Bar(y0, c.Hp, c.MaxHp, hk > 0.5f ? new Color(90, 210, 110, 255) : hk > 0.25f ? new Color(240, 170, 60, 255) : new Color(230, 70, 60, 255));
            if (Tex("sl_heart") is { } heart) DrawFit(heart, new Rectangle(r.X + 8, fy + 2, 22, 22), new Color(225, 70, 80, 255));
            var hp = $"{MathF.Ceiling(c.Hp)}";
            Text(hp, r.X + 19 - Measure(hp, 11).X / 2, fy + 7, 11, Color.RayWhite);
            // Fitted parts as small icons along the top of the art, free slots as empty pips; the admiral as a gold tag.
            for (int i = 0; i < Math.Max(c.Parts.Count, c.Def.Slots); i++)
            {
                var ir = new Rectangle(art.X + 4 + i * 22, art.Y + 4, 20, 20);
                Raylib.DrawRectangleRounded(ir, 0.3f, 4, new Color(10, 14, 30, 220));
                if (i < c.Parts.Count && Tex("st_comp_" + c.Parts[i].Id) is { } pt) DrawFit(pt, ir, Color.White);
                else if (i >= c.Parts.Count) Raylib.DrawRectangleRoundedLinesEx(ir, 0.3f, 4, 1, new Color(150, 170, 220, 160));
            }
            if (c.Admiral != null)
            {
                var tag = new Rectangle(art.X + 4, art.Y + art.Height - 20, Measure("Admiral", 12).X + 10, 16);
                Raylib.DrawRectangleRounded(tag, 0.5f, 4, new Color(30, 24, 8, 220));
                Text("Admiral", tag.X + 5, tag.Y + 2, 12, new Color(255, 215, 100, 255));
            }
        }
        else if (c.Def.Value > 0)
        {
            if (Tex("sl_coin") is { } coin) DrawFit(coin, new Rectangle(r.X + 8, fy + 2, 20, 20), Color.White);
            Text($"{c.Def.Value}", r.X + 31, fy + 4, 16, Ink);
        }
        if (c.Def.IsPlanet && c.Def.ColonizeWith != "none")
        {
            var tag = c.Claimed ? "Colonised" : "Unclaimed";
            Text(tag, r.X + r.Width - 10 - Measure(tag, 12).X, fy + 6, 12, c.Claimed ? new Color(20, 100, 40, 255) : new Color(110, 70, 20, 255));
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
        var listed = BarSystems(out int hidden);
        if (hidden > 0) Text($"+{hidden} more (Z to see all)", TabRect(listed.Count).X + 4, 20, 15, Color.LightGray);
        for (int i = 0; i < listed.Count; i++)
        {
            var r = TabRect(i);
            var z = listed[i];
            bool fight = sim.Table.Battles.Any(bt => z.Contains(bt.Pos));
            bool here = z.Contains(_cam.Target);
            Raylib.DrawRectangleRounded(r, 0.3f, 6, fight ? new Color(130, 40, 50, 255) : here ? new Color(70, 100, 190, 255) : new Color(32, 38, 66, 255));
            if (z.Claimed) Raylib.DrawRectangleRoundedLinesEx(r, 0.3f, 6, 2, new Color(240, 200, 90, 220));
            var label = z.Name;
            float size = 17;
            while (size > 11 && Measure(label, size).X > r.Width - 12) size -= 1;
            Text(label, r.X + 6, r.Y + 5, size, Color.RayWhite);
            var kind = z.Index == 0 ? "Capital" : z.Kind.Length > 0 ? z.Kind : "Guardian";
            float ks = 12;
            while (ks > 9 && Measure(kind, ks).X > r.Width - 12) ks -= 1;
            Text(kind, r.X + 6, r.Y + 27, ks, new Color(200, 210, 255, 200));
        }
        int food = sim.AllCards.Count(c => c.Def.Id == "food");
        int eat = sim.AllCards.Where(c => c.Def.Category == "person").Sum(c => c.Def.FoodUpkeep);
        int energy = sim.AllCards.Count(c => c.Def.Id == "energy");
        var info = $"{sim.Diff.Name}  |  Owned {sim.ClaimedCount}/{Defs.Rules.ClaimLimit}  |  Moon {sim.Moon}  |  Act {sim.Act}  |  Food {food}/{eat} needed  |  Energy {energy}  |  x{_speed:0}";
        var w = Measure(info, 20).X;
        Text(info, sw - w - 20, 8, 20, food < eat ? new Color(255, 160, 120, 255) : Color.RayWhite);
        var bar = new Rectangle(sw - w - 20, 36, w, 10);
        Raylib.DrawRectangleRec(bar, new Color(40, 44, 70, 255));
        Raylib.DrawRectangleRec(new Rectangle(bar.X, bar.Y, bar.Width * sim.MoonTime / sim.MoonSeconds, bar.Height), new Color(240, 210, 120, 255));
    }

    static Color Shade(Color c, float k) =>
        new((byte)Math.Clamp(c.R * k, 0, 255), (byte)Math.Clamp(c.G * k, 0, 255), (byte)Math.Clamp(c.B * k, 0, 255), c.A);

    /// <summary>A booster pack: a foil wrapper in the pack's colour with crimped zig-zag ends, a seal stripe, a shine,
    /// and the pack's picture in a window on the front.</summary>
    static void DrawBooster(Rectangle r, Color col, Texture2D? art, bool lifted)
    {
        float tooth = 5, crimp = 9;
        if (lifted) r = new Rectangle(r.X, r.Y - 3, r.Width, r.Height);
        var ink = new Color(25, 22, 30, 255);
        // shadow
        Raylib.DrawRectangle((int)(r.X + 3), (int)(r.Y + tooth + 3), (int)r.Width, (int)(r.Height - 2 * tooth), new Color(0, 0, 0, 90));
        // wrapper body with a light-to-dark gradient
        var body = new Rectangle(r.X, r.Y + tooth, r.Width, r.Height - 2 * tooth);
        Raylib.DrawRectangleGradientV((int)body.X, (int)body.Y, (int)body.Width, (int)body.Height, Shade(col, 1.25f), Shade(col, 0.8f));
        // crimped ends: a darker band with seal lines and zig-zag teeth
        var band = Shade(col, 0.62f);
        foreach (float y in new[] { body.Y, body.Y + body.Height - crimp })
        {
            Raylib.DrawRectangle((int)body.X, (int)y, (int)body.Width, (int)crimp, band);
            for (float x = body.X + 3; x < body.X + body.Width - 1; x += 3)
                Raylib.DrawLine((int)x, (int)y + 1, (int)x, (int)(y + crimp - 1), Shade(col, 0.45f));
        }
        for (float x = body.X; x < body.X + body.Width - 0.5f; x += tooth * 2)
        {
            float w = MathF.Min(tooth * 2, body.X + body.Width - x);
            Raylib.DrawTriangle(new Vector2(x + w / 2, r.Y), new Vector2(x, body.Y + 0.5f), new Vector2(x + w, body.Y + 0.5f), band);
            Raylib.DrawTriangle(new Vector2(x, body.Y + body.Height - 0.5f), new Vector2(x + w / 2, r.Y + r.Height), new Vector2(x + w, body.Y + body.Height - 0.5f), band);
        }
        // picture window
        var win = new Rectangle(body.X + 5, body.Y + crimp + 4, body.Width - 10, body.Height - 2 * crimp - 8);
        Raylib.DrawRectangleRounded(win, 0.2f, 6, new Color(14, 18, 34, 255));
        if (art is { } t) DrawCover(t, new Rectangle(win.X + 1, win.Y + 1, win.Width - 2, win.Height - 2), Color.White);
        Raylib.DrawRectangleRoundedLinesEx(win, 0.2f, 6, 2, Shade(col, 1.45f));
        // foil shine across the wrapper
        Raylib.DrawTriangle(new Vector2(body.X + body.Width * 0.15f, body.Y), new Vector2(body.X, body.Y + body.Height * 0.45f), new Vector2(body.X + body.Width * 0.38f, body.Y), new Color(255, 255, 255, 55));
        Raylib.DrawTriangle(new Vector2(body.X + body.Width * 0.38f, body.Y), new Vector2(body.X, body.Y + body.Height * 0.45f), new Vector2(body.X, body.Y + body.Height * 0.62f), new Color(255, 255, 255, 30));
        // hand-drawn style outline, like the Stacklands cards
        Raylib.DrawRectangleLinesEx(body, 2, ink);
    }

    void DrawRightPanel()
    {
        var sim = _sim!;
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        Raylib.DrawRectangle(sw - RightPanel, TopBar, RightPanel, sh - TopBar, new Color(14, 16, 34, 245));
        Text("Packs - drop Energy", sw - RightPanel + 12, TopBar + 10, 19, Color.LightGray);
        foreach (var (p, r) in PackRects())
        {
            bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r) && _drag != null;
            Raylib.DrawRectangleRounded(r, 0.12f, 6, hover ? new Color(80, 110, 200, 255) : new Color(36, 42, 74, 255));
            DrawBooster(new Rectangle(r.X + 7, r.Y + 4, 50, r.Height - 8), Hex(p.Color), Tex(p.Art), hover);
            float ts = 20;
            while (ts > 14 && Measure(p.Name, ts).X > r.Width - 76) ts -= 0.5f;
            Text(p.Name, r.X + 66, r.Y + 10, ts, Color.RayWhite);
            Text($"{sim.PackCost(p)} Energy, {p.Draws} cards", r.X + 66, r.Y + 40, 15, new Color(240, 210, 120, 255));
        }
        var m = MarketRect();
        bool mh = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), m) && _drag != null;
        Raylib.DrawRectangleRounded(m, 0.12f, 6, mh ? new Color(160, 130, 50, 255) : new Color(70, 58, 30, 255));
        Text(Defs.Rules.SellSlotName, m.X + 14, m.Y + 12, 26, Color.RayWhite);
        Wrapped("Drop cards here to sell them for Energy Credits", m.X + 14, m.Y + 46, m.Width - 28, 16, Color.LightGray);
        Text("Tab blueprints - Space pause", sw - RightPanel + 12, m.Y - 44, 14, Color.Gray);
        Text("1-3 speed - Z zoom out/in", sw - RightPanel + 12, m.Y - 24, 14, Color.Gray);
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
        if (c.MaxHp > 0 && (c.MaxShield > 0 || c.MaxArmor > 0 || c.Guns.Count > 0 || c.Def.Slots > 0))
        {
            lines += $"\nHull {MathF.Ceiling(c.Hp)}/{c.MaxHp}" + (c.MaxArmor > 0 ? $"  Armour {MathF.Ceiling(c.Armor)}/{c.MaxArmor}" : "")
                   + (c.MaxShield > 0 ? $"  Shields {MathF.Ceiling(c.Shield)}/{c.MaxShield} (+{c.ShieldRegen:0.#}/s)" : "")
                   + (c.HullRegen > 0 ? $"  Heals {c.HullRegen:0.#}/s" : "") + (c.Evasion > 0 ? $"  Dodge {c.Evasion:P0}" : "");
            if (c.Guns.Count > 0) lines += "\nWeapons: " + string.Join(", ", c.Guns.Select(g => $"{g.Profile?.Name ?? "Guns"} {g.Damage:0}/{g.Cooldown:0.#}s"));
            if (c.Def.Slots > 0) lines += $"\nSlots {c.Parts.Count}/{c.Def.Slots}" + (c.Parts.Count < c.Def.Slots ? " - drop ship components here to fit them" : "")
                                       + (c.Def.HasTag("warship") ? (c.Admiral != null ? " - Admiral aboard" : " - drop an Admiral here to assign them") : "");
        }
        var uses = _sim!.UsedIn(c.Def).Take(4).ToList();
        if (uses.Count > 0) lines += "\nUsed in: " + string.Join(" | ", uses.Select(BlueprintLine));
        if (Defs.Component.TryGetValue(c.Def.Id, out var cp) && cp.Kind == "weapon")
            lines += $"\nDamage {cp.Damage} every {cp.Cooldown:0.#}s - vs shields x{cp.VsShield:0.##}, armour x{cp.VsArmor:0.##}, hull x{cp.VsHull:0.##}"
                   + (cp.PierceShield >= 1 ? (cp.PierceArmor >= 1 ? " - ignores shields and armour" : " - flies past shields") : "");
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
        float maxW = BoardView.Width - 60;
        foreach (var (text, t) in Enumerable.Reverse(_toasts).Take(5))
        {
            byte a = (byte)(255 * Math.Clamp(7 - (_clock - t), 0, 1));
            float size = 20;
            while (size > 13 && Measure(text, size).X > maxW) size -= 1;
            var sz = Measure(text, size);
            Raylib.DrawRectangleRounded(new Rectangle(16, y - 6, Math.Min(sz.X, maxW) + 24, 34), 0.3f, 6, new Color((byte)10, (byte)12, (byte)26, (byte)(a * 0.85f)));
            Text(text, 28, y + (20 - size) / 2, size, new Color((byte)255, (byte)255, (byte)255, a));
            y -= 42;
        }
    }

    /// <summary>A blueprint as one line: its recipe, then what it makes (taken from the outputs, so it can't go missing).</summary>
    string BlueprintLine(RecipeDef e)
    {
        if (e.Desc.Contains('=') || e.Desc.Contains(':') || e.Outputs.Length == 0) return e.Desc;
        string Give(Outcome o) => string.Join(" + ", o.Give.Select(g => (g.N > 1 ? $"{g.N} " : "") + (g.Card == "station.yield" ? "its yield" : _res.CardName(g.Card))));
        return e.Desc + " = " + (e.Outputs.Length == 1 ? Give(e.Outputs[0]) : "one of: " + string.Join(" / ", e.Outputs.Select(Give)));
    }

    readonly HashSet<string> _tutorialSeen = new();

    /// <summary>The tutorial checklist: the current step, its hint, progress, and Skip / Hide.</summary>
    /// <summary>The player's tutorial setting, except in --screenshot captures, which show the game without it.</summary>
    bool TutorialShown => _settings.Tutorial && _shot == null;

    void DrawTutorial()
    {
        var sim = _sim!;
        if (!TutorialShown || _codex || _screen != Screen.Play) return;
        foreach (var t in Defs.Tutorial)
            if (sim.StepDone(t) && !sim.SkippedSteps.Contains(t.Id) && _tutorialSeen.Add(t.Id) && _clock > 1) Toast($"Tutorial: {t.Text} - done!");
        var step = sim.CurrentStep;
        if (step == null) return;
        int done = Defs.Tutorial.Count(sim.StepDone), n = Array.IndexOf(Defs.Tutorial, step) + 1;
        var r = new Rectangle(16, TopBar + 12, 430, 150);
        Raylib.DrawRectangleRounded(r, 0.08f, 6, new Color(12, 16, 38, 235));
        Raylib.DrawRectangleRoundedLinesEx(r, 0.08f, 6, 2, new Color(240, 200, 90, 200));
        Text($"Tutorial  {done}/{Defs.Tutorial.Length}", r.X + 14, r.Y + 10, 18, new Color(240, 200, 90, 255));
        Text(step.Text, r.X + 14, r.Y + 34, 21, Color.RayWhite);
        Wrapped(step.Hint, r.X + 14, r.Y + 62, r.Width - 28, 16, Color.LightGray, 3);
        if (sim.MoonHeld) Text("The first moon waits until you have grown Food.", r.X + 14, r.Y + r.Height - 24, 15, new Color(240, 200, 90, 255));
        if (Button(new Rectangle(r.X + r.Width - 150, r.Y + 8, 66, 24), "Skip", false, 15)) sim.SkippedSteps.Add(step.Id);
        if (Button(new Rectangle(r.X + r.Width - 78, r.Y + 8, 64, 24), "Hide", false, 15)) { _settings.Tutorial = false; _settings.Save(); Toast("Tutorial hidden. Turn it back on from the empire screen."); }
    }

    int _bookTab;
    float _bookScroll;

    /// <summary>The Blueprint book: every recipe by tab, known ones in full, locked ones with what unlocks them.</summary>
    void DrawCodex()
    {
        var sim = _sim!;
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        var r = new Rectangle(60, 70, sw - RightPanel - 120, sh - 130);
        Raylib.DrawRectangleRounded(r, 0.02f, 6, new Color(10, 12, 26, 248));
        Raylib.DrawRectangleRoundedLinesEx(r, 0.02f, 6, 2, new Color(120, 140, 220, 160));
        Text("Blueprint book", r.X + 24, r.Y + 14, 30, Color.RayWhite);
        Text("Tab closes - Q/E or click to change tab - wheel scrolls", r.X + 270, r.Y + 24, 16, Color.Gray);

        var tabs = Defs.Rules.BlueprintTabs.Select(t => t.Tab).ToList();
        if (Raylib.IsKeyPressed(KeyboardKey.Q)) { _bookTab = (_bookTab + tabs.Count - 1) % tabs.Count; _bookScroll = 0; }
        if (Raylib.IsKeyPressed(KeyboardKey.E)) { _bookTab = (_bookTab + 1) % tabs.Count; _bookScroll = 0; }
        float tx = r.X + 24;
        for (int i = 0; i < tabs.Count; i++)
        {
            var list = Defs.Recipes.Where(x => Sim.BlueprintTab(x) == tabs[i]).ToList();
            int known = list.Count(x => sim.Blueprint(x) != Sim.BlueprintState.Locked);
            var label = $"{tabs[i]} {known}/{list.Count}";
            float w = Measure(label, 19).X + 28;
            if (Button(new Rectangle(tx, r.Y + 56, w, 38), label, i == _bookTab, 19)) { _bookTab = i; _bookScroll = 0; }
            tx += w + 8;
        }

        var entries = Defs.Recipes.Where(x => Sim.BlueprintTab(x) == tabs[_bookTab])
            .OrderBy(x => sim.Blueprint(x) == Sim.BlueprintState.Locked ? 1 : 0).ToList();
        var area = new Rectangle(r.X + 20, r.Y + 106, r.Width - 40, r.Height - 120);
        float rowH = 30, total = entries.Count * rowH;
        _bookScroll = Math.Clamp(_bookScroll - Raylib.GetMouseWheelMove() * 60, 0, Math.Max(0, total - area.Height));
        Raylib.BeginScissorMode((int)area.X, (int)area.Y, (int)area.Width, (int)area.Height);
        float y = area.Y - _bookScroll;
        foreach (var e in entries)
        {
            if (y > area.Y - rowH && y < area.Y + area.Height)
            {
                var st = sim.Blueprint(e);
                string mark = st switch { Sim.BlueprintState.Made => "[made]", Sim.BlueprintState.Known => "[ ok ]", _ => "[lock]" };
                var col = st switch { Sim.BlueprintState.Made => new Color(140, 230, 150, 255), Sim.BlueprintState.Known => Color.RayWhite, _ => new Color(130, 130, 150, 255) };
                Text(mark, area.X, y, 17, col);
                float time = e.Time < 0 ? 0 : e.Time;
                var line = BlueprintLine(e) + (time > 0 ? $"  ({time:0}s)" : "");
                if (st == Sim.BlueprintState.Locked) line += $"  - needs {_res.CardName(e.RequiresTech)}";
                float size = 18;
                while (size > 12 && Measure(line, size).X > area.Width - 90) size -= 1;
                Text(line, area.X + 80, y, size, col);
            }
            y += rowH;
        }
        Raylib.EndScissorMode();
    }

    void DrawEnd()
    {
        var sim = _sim!;
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 170));
        var title = sim.State == RunState.Won ? "THE GALAXY IS SAVED" : "YOUR EMPIRE HAS FALLEN";
        Text(title, sw / 2f - Measure(title, 56).X / 2, sh / 2f - 120, 56, sim.State == RunState.Won ? Color.Gold : new Color(255, 110, 100, 255));
        var why = $"{sim.EndReason}  (Moon {sim.Moon}, {sim.Systems.Count} systems)";
        Text(why, sw / 2f - Measure(why, 24).X / 2, sh / 2f - 40, 24, Color.RayWhite);
        if (Button(new Rectangle(sw / 2f - 130, sh / 2f + 30, 260, 60), "New run", false, 28)) { _sim = null; _screen = Screen.Empire; }
    }
}
