using System.Numerics;
using Raylib_cs;

namespace GrandGalactic;

public sealed partial class GameUi
{
    Board Table => _sim!.Table;

    /// <summary>Smoothly scroll the camera to a star system.</summary>
    void GoTo(StarSystem z) => _camGoal = z.Center;
    Rectangle BoardView => new(0, TopBar, Raylib.GetScreenWidth() - RightPanel, Raylib.GetScreenHeight() - TopBar);

    /// <summary>Screen areas drawn over the board last frame (tutorial, menus, buttons): clicks there aren't board clicks.</summary>
    readonly List<Rectangle> _uiRects = new();
    bool OverUi(Vector2 m) => _uiRects.Any(r => Raylib.CheckCollisionPointRec(m, r));

    // A press on a card becomes a drag once the mouse moves; a press and release in place is a click (opens its menu).
    (Stack s, int i, Vector2 at)? _press;
    Vector2 _rightDown;

    void Update(float dt)
    {
        if (_music is { } m) Raylib.UpdateMusicStream(m);
        if (_screen is not (Screen.Play or Screen.End) || _sim == null) return;
        var sim = _sim;

        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            if (_market) _market = false;
            else if (_menuCard != null) CloseCardMenu();
            else if (_escMenu) _escMenu = false;
            else if (_codex) _codex = false;
            else if (_screen == Screen.Play) _escMenu = true;
        }
        if (!_escMenu)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Space)) _paused = !_paused;
            if (Raylib.IsKeyPressed(KeyboardKey.Tab)) { _codex = !_codex; sim.Flags.Add("opened_book"); CloseCardMenu(); }
            if (Raylib.IsKeyPressed(KeyboardKey.One)) _speed = 1;
            if (Raylib.IsKeyPressed(KeyboardKey.Two)) _speed = 2;
            if (Raylib.IsKeyPressed(KeyboardKey.Three)) _speed = 4;
            for (int i = 0; i < Math.Min(9, sim.Systems.Count); i++)
                if (Raylib.IsKeyPressed(KeyboardKey.F1 + i)) GoTo(sim.Systems[i]);
            if (Raylib.IsKeyPressed(KeyboardKey.Z)) _camZoomGoal = _cam.Zoom > 0.3f ? 0.2f : 0.62f; // whole empire / close up
        }
        foreach (var z in sim.NewSystems) { GoTo(z); Toast($"{z.Name} is now part of your table. Drag cards there freely."); }
        sim.NewSystems.Clear();

        // Camera: right or middle drag pans, wheel zooms, WASD pans.
        var view = BoardView;
        _cam.Offset = new Vector2(view.X + view.Width / 2, view.Y + view.Height / 2);
        bool menus = _escMenu || _codex;
        if (!menus && (Raylib.IsMouseButtonDown(MouseButton.Right) || Raylib.IsMouseButtonDown(MouseButton.Middle)))
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
        if (!menus)
        {
            var pan = new Vector2((Raylib.IsKeyDown(KeyboardKey.D) ? 1 : 0) - (Raylib.IsKeyDown(KeyboardKey.A) ? 1 : 0),
                                  (Raylib.IsKeyDown(KeyboardKey.S) ? 1 : 0) - (Raylib.IsKeyDown(KeyboardKey.W) ? 1 : 0));
            _cam.Target += pan * 900 * dt / _cam.Zoom;
            if (pan != Vector2.Zero) _camGoal = null;
            float wheel = Raylib.GetMouseWheelMove();
            if (wheel != 0 && _menuCard == null && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), view))
            {
                var before = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), _cam);
                _cam.Zoom = Math.Clamp(_cam.Zoom * (1 + wheel * 0.1f), 0.12f, 1.6f);
                _camZoomGoal = null;
                _cam.Target += before - Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), _cam);
            }
        }
        _cam.Target = Vector2.Clamp(_cam.Target, sim.BoundsMin, sim.BoundsMax);

        if (_screen == Screen.Play && !menus) HandleMouse();
        sim.TutorialOn = TutorialShown;
        if (!_paused && !_escMenu && !_codex && _screen == Screen.Play) sim.Update(dt * _speed); // the book and the menu pause the game

        foreach (var msg in sim.Messages) Toast(msg);
        sim.Messages.Clear();
        foreach (var (id, n, at) in sim.Gains) AddFloater(id, n, at);
        sim.Gains.Clear();
        int hits = 0;
        foreach (var e in sim.Events)
        {
            if (e != SimEvent.Hit || hits++ < 1) Play(e);
            if (e == SimEvent.MoonEnd && sim.State == RunState.Playing) SaveGame(quiet: true); // autosave every moon
        }
        sim.Events.Clear();
        _toasts.RemoveAll(t => _clock - t.t > 7);
        if (_menuCard != null && (_menuCard.Stack == null || _menuCard.Stack != _menuStack)) CloseCardMenu();
        if (sim.State != RunState.Playing && _screen == Screen.Play) { _screen = Screen.End; _escMenu = false; }
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

    /// <summary>Where a bought pack's cards land: the middle of the system you're looking at.</summary>
    Vector2 PackDropPoint()
    {
        var sim = _sim!;
        var z = sim.SystemAt(_cam.Target) ?? sim.Home;
        return sim.ClampIn(_cam.Target - new Vector2(Sim.CardW / 2, Sim.CardH / 2), z);
    }

    void HandleMouse()
    {
        var sim = _sim!;
        var mouse = Raylib.GetMousePosition();
        bool overBoard = Raylib.CheckCollisionPointRec(mouse, BoardView) && !OverUi(mouse);

        // The trade panel and a card's menu take their own clicks.
        if (_market)
        {
            if (Raylib.IsMouseButtonPressed(MouseButton.Left) && !Raylib.CheckCollisionPointRec(mouse, _marketRect) && !Raylib.CheckCollisionPointRec(mouse, MarketRect())) _market = false;
            else if (Raylib.IsMouseButtonPressed(MouseButton.Left)) return;
        }
        if (_menuCard != null)
        {
            if ((Raylib.IsMouseButtonPressed(MouseButton.Left) || Raylib.IsMouseButtonPressed(MouseButton.Right)) && !Raylib.CheckCollisionPointRec(mouse, _menuRect))
                CloseCardMenu();
            else return;
        }

        if (Raylib.IsMouseButtonPressed(MouseButton.Right)) _rightDown = mouse;
        if (Raylib.IsMouseButtonReleased(MouseButton.Right) && Vector2.Distance(mouse, _rightDown) < 6 && overBoard && Pick(MouseWorld) is { } rhit)
            OpenCardMenu(rhit.s, rhit.s.Cards[rhit.i], mouse);

        if (_drag == null && _press == null && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            // System names in the top bar scroll the camera there.
            var bar = BarSystems(out _);
            for (int i = 0; i < bar.Count; i++)
                if (Raylib.CheckCollisionPointRec(mouse, TabRect(i))) { GoTo(bar[i]); return; }
            // The Market trades surplus from the pool; packs are bought with a click.
            if (Raylib.CheckCollisionPointRec(mouse, MarketRect())) { _market = !_market; return; }
            foreach (var (pack, r) in PackRects())
                if (Raylib.CheckCollisionPointRec(mouse, r))
                {
                    if (sim.BuyPack(pack, PackDropPoint()) is { } why) { Toast(why); sim.Events.Add(SimEvent.Warning); }
                    return;
                }
            if (overBoard && Pick(MouseWorld) is { } hit) _press = (hit.s, hit.i, mouse);
        }

        if (_press is { } p)
        {
            var card = p.s.Cards.ElementAtOrDefault(p.i);
            // Moved far enough (even in the frame it was let go, for a quick flick): a drag. Else a release is a click.
            if (card == null || !Table.Stacks.Contains(p.s)) _press = null;
            else if (Vector2.Distance(mouse, p.at) <= 6 && Raylib.IsMouseButtonReleased(MouseButton.Left))
            {
                _press = null;
                OpenCardMenu(p.s, card, mouse);
            }
            else if (Vector2.Distance(mouse, p.at) > 6)
            {
                _press = null;
                if (!card.Def.IsHostile && Defs.Category[card.Def.Category].Draggable)
                {
                    var start = Raylib.GetScreenToWorld2D(p.at, _cam);
                    _drag = sim.Split(p.s, p.i);
                    _dragFrom = _drag.Pos;
                    _drag.Dragging = true;
                    _dragOffset = start - _drag.Pos;
                    Table.Stacks.Remove(_drag);
                    Table.Stacks.Add(_drag); // draw on top
                    sim.Events.Add(SimEvent.Pickup);
                }
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
        if (PackRects().Any(pr => Raylib.CheckCollisionPointRec(mouse, pr.Item2)))
        {
            d.Pos = _dragFrom;
            Toast("Packs are bought with Energy from your pool: just click a pack.");
            return;
        }
        if (Raylib.CheckCollisionPointRec(mouse, MarketRect()))
        {
            d.Pos = _dragFrom;
            int got = sim.Sell(d);
            Toast(got > 0 ? $"Sold for {got} Energy." : "Nothing in that stack can be sold.");
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
            // A single ship component dropped on a ship fits into a slot (or swaps out a weaker part); an admiral takes
            // command of a fleet.
            bool admiral = d.Cards.Count == 1 && d.Root.Def.Id == "admiral" && hit.s.Cards.Any(x => x.Def.HasTag("warship"));
            if (d.Cards.Count == 1 && (d.Root.Def.Category == "component" || admiral))
            {
                var part = d.Root;
                var host = admiral ? hit.s.Cards.First(x => x.Def.HasTag("warship"))
                    : Sim.CanFit(target) ? target : hit.s.Cards.LastOrDefault(Sim.CanFit) ?? target;
                int before = host.Parts.Count;
                var why = admiral ? sim.AssignAdmiral(part, host) : sim.Fit(part, host);
                if (why == null)
                {
                    Toast(admiral ? $"The Admiral takes command of this fleet ({Sim.Warships(hit.s)}/{sim.FleetSize} warships)."
                        : host.Parts.Count == before ? $"{_res.CardName(part.Def.Id)} swapped in on the {_res.CardName(host.Def.Id)}; the old part is beside it."
                        : $"{_res.CardName(part.Def.Id)} fitted to {_res.CardName(host.Def.Id)}.");
                    return;
                }
                Toast(why);
                d.Pos = _dragFrom;
                return;
            }
            if (sim.StackBlock(d, hit.s) is { Length: > 0 } block) { Toast(block); d.Pos = _dragFrom; return; }
            sim.StackOnto(d, hit.s);
            return;
        }
        sim.Events.Add(SimEvent.Drop);
    }

    Rectangle TabRect(int i) => new(8 + i * 112, 6, 106, 46);

    /// <summary>The systems listed in the top bar: owned ones first, then the most recently found, as many as fit.</summary>
    List<StarSystem> BarSystems(out int hidden)
    {
        var sim = _sim!;
        int fit = Math.Max(3, (Raylib.GetScreenWidth() - 760) / 112);
        var order = sim.Systems.Where(z => z.Claimed).Concat(sim.Systems.Where(z => !z.Claimed).Reverse()).ToList();
        hidden = Math.Max(0, order.Count - fit);
        return order.Take(fit).ToList();
    }

    IEnumerable<(PackDef, Rectangle)> PackRects()
    {
        float x = Raylib.GetScreenWidth() - RightPanel + 12, y = TopBar + 40;
        var packs = _sim!.AvailablePacks.ToList();
        // Rows shrink on short windows so every pack stays above the Market.
        float room = MarketRect().Y - 52 - y, h = Math.Clamp(room / Math.Max(1, packs.Count) - 8, 52, 80);
        foreach (var p in packs)
        {
            yield return (p, new Rectangle(x, y, RightPanel - 24, h));
            y += h + 8;
        }
    }

    Rectangle MarketRect() => new(Raylib.GetScreenWidth() - RightPanel + 12, Raylib.GetScreenHeight() - 130, RightPanel - 24, 112);

    // ---------- drawing ----------

    void DrawPlay()
    {
        var sim = _sim!;
        var view = BoardView;
        var b = Table;
        _uiRects.Clear();

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
        foreach (var s in b.Stacks) if (!s.Dragging) DrawStackLabels(s);
        foreach (var s in b.Stacks) if (s.Dragging) DrawStack(s);
        Raylib.EndMode2D();
        _inWorld = false;
        Raylib.EndScissorMode();

        DrawSystemPanel();
        DrawTopBar();
        DrawRightPanel();
        DrawFloaters();
        if (_menuCard == null && !_escMenu) DrawTooltip();
        DrawTutorial();
        DrawToasts();
        if (_codex) DrawCodex();
        if (_menuCard != null) DrawCardMenu();
        if (_market) DrawMarket();
        if (_paused && !_escMenu) Text("PAUSED (Space)", view.Width / 2 - 100, TopBar + 14, 30, Color.Yellow);
        if (_screen == Screen.End) DrawEnd();
        if (_escMenu) DrawEscMenu();
    }

    void DrawStack(Stack s)
    {
        for (int i = 0; i < s.Cards.Count; i++)
            DrawCard(s.Cards[i], new Rectangle(s.Pos.X, s.Pos.Y + i * Sim.StackStep, Sim.CardW, Sim.CardH), s.Dragging);
        if (s.Active != null && s.Duration > 0 && s.Wait == null)
        {
            var r = new Rectangle(s.Pos.X, s.Pos.Y - 18, Sim.CardW, 12);
            Raylib.DrawRectangleRounded(r, 0.5f, 6, new Color(0, 0, 0, 180));
            Raylib.DrawRectangleRounded(new Rectangle(r.X + 2, r.Y + 2, (r.Width - 4) * Math.Clamp(s.Progress / s.Duration, 0, 1), r.Height - 4), 0.5f, 6, new Color(120, 230, 140, 255));
        }
    }

    /// <summary>Above a stack: why it is waiting, and for a fleet its size and Admiral.</summary>
    void DrawStackLabels(Stack s)
    {
        var sim = _sim!;
        if (s.Wait is { } wait)
        {
            // Readable at any zoom: at least ~12 px on screen, wider than the card if it must be.
            float size = Math.Max(15, 12 / _cam.Zoom), maxW = Math.Max(300, 300 / _cam.Zoom * 0.6f);
            while (size > 11 && Measure(wait, size).X > maxW) size -= 0.5f;
            var sz = Measure(wait, size);
            float w = Math.Min(sz.X, maxW) + 14, x = s.Pos.X + Sim.CardW / 2 - w / 2;
            var r = new Rectangle(x, s.Pos.Y - sz.Y - 9, w, sz.Y + 5);
            Raylib.DrawRectangleRounded(r, 0.5f, 6, new Color(40, 26, 6, 225));
            Raylib.DrawRectangleRoundedLinesEx(r, 0.5f, 6, 1.5f, new Color(240, 190, 80, 230));
            Text(wait, r.X + 7, r.Y + 2.5f, size, new Color(255, 220, 140, 255));
        }
        int ships = Sim.Warships(s);
        if (ships > 0 && (ships > 1 || Sim.HasAdmiral(s)) && s.Wait == null && s.Active == null)
        {
            var label = $"Fleet {ships}/{sim.FleetSize}" + (Sim.HasAdmiral(s) ? " - Admiral" : "");
            var sz = Measure(label, 13);
            var r = new Rectangle(s.Pos.X + Sim.CardW - sz.X - 14, s.Pos.Y - 20, sz.X + 12, 17);
            Raylib.DrawRectangleRounded(r, 0.5f, 6, new Color(12, 30, 40, 225));
            Raylib.DrawRectangleRoundedLinesEx(r, 0.5f, 6, 1.5f, Sim.HasAdmiral(s) ? new Color(255, 215, 100, 230) : new Color(140, 210, 230, 200));
            Text(label, r.X + 6, r.Y + 2, 13, Sim.HasAdmiral(s) ? new Color(255, 225, 140, 255) : new Color(190, 235, 245, 255));
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

    /// <summary>A card's picture in a box: its painted scene (with the icon as a badge), or its art.</summary>
    void DrawCardArt(CardDef d, Rectangle art, Color frame)
    {
        if (d.Scene != "none" && Tex(d.Scene) is { } scene)
        {
            DrawCover(scene, art, Color.White);
            if (Tex(d.Art) is { } badge)
            {
                float bs = art.Width * 0.46f;
                var br = new Rectangle(art.X + art.Width - bs - 4, art.Y + art.Height - bs - 4, bs, bs);
                Raylib.DrawRectangleRounded(new Rectangle(br.X - 2, br.Y - 2, br.Width + 4, br.Height + 4), 0.2f, 6, new Color(10, 12, 24, 235));
                if (_iconTex.Contains(d.Art)) DrawFit(badge, new Rectangle(br.X + 3, br.Y + 3, br.Width - 6, br.Height - 6), Color.White);
                else DrawCover(badge, br, Color.White);
                Raylib.DrawRectangleRoundedLinesEx(new Rectangle(br.X - 2, br.Y - 2, br.Width + 4, br.Height + 4), 0.2f, 6, 2, Shade(frame, 1.2f));
            }
        }
        else if (Tex(d.Art) is { } t)
        {
            if (_iconTex.Contains(d.Art)) DrawFit(t, new Rectangle(art.X + art.Width * 0.06f, art.Y + art.Height * 0.06f, art.Width * 0.88f, art.Height * 0.88f), Color.White);
            else DrawCover(t, art, Color.White);
        }
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

        // Art window: a rounded pane of space with the picture inside.
        var art = new Rectangle(r.X + 9, r.Y + 28, r.Width - 18, r.Height - 60);
        Raylib.DrawRectangleRounded(art, 0.12f, 6, new Color(16, 20, 38, 255));
        DrawCardArt(c.Def, art, col);
        Raylib.DrawRectangleRoundedLinesEx(art, 0.12f, 6, 2, new Color(col.R / 3, col.G / 3, col.B / 3, 200));

        // Footer: hull and bars for fighting cards, else the value; planets say whether they are yours.
        float fy = r.Y + r.Height - 27;
        bool fighter = c.MaxHp > 0 && (c.Def.Attack > 0 || c.MaxShield > 0 || c.MaxArmor > 0 || c.Def.IsHostile || c.Def.Slots > 0);
        if (c.Def.HasTag("baby"))
        {
            // A Baby shows how far it has grown (it only grows on a City District).
            float k = Math.Clamp(c.Grow / Defs.Rules.BabyGrowSeconds, 0, 1);
            var bar = new Rectangle(r.X + 10, fy + 9, r.Width - 20, 9);
            Raylib.DrawRectangleRounded(bar, 1f, 4, new Color(0, 0, 0, 110));
            Raylib.DrawRectangleRounded(new Rectangle(bar.X, bar.Y, Math.Max(9, bar.Width * k), bar.Height), 1f, 4, new Color(240, 150, 190, 255));
            bool onCity = c.Stack?.Cards.Any(x => x.Def.Id == "city_district") == true;
            var t = onCity ? $"grows in {Math.Max(0, Defs.Rules.BabyGrowSeconds - c.Grow):0}s" : "put on a City District";
            Text(t, r.X + r.Width / 2 - Measure(t, 11).X / 2, fy - 4, 11, Ink);
        }
        else if (fighter)
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
                const string label = "Fleet Admiral";
                var tag = new Rectangle(art.X + 4, art.Y + art.Height - 20, Measure(label, 12).X + 10, 16);
                Raylib.DrawRectangleRounded(tag, 0.5f, 4, new Color(30, 24, 8, 220));
                Text(label, tag.X + 5, tag.Y + 2, 12, new Color(255, 215, 100, 255));
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
        if (c.Def.Category == "tech" && _sim != null)
        {
            var known = _sim.Techs.Contains(c.Def.Id);
            var tag = known ? "Known" : "Blueprint";
            Text(tag, r.X + r.Width - 10 - Measure(tag, 12).X, fy + 6, 12, known ? new Color(110, 70, 20, 255) : new Color(20, 80, 40, 255));
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

    // ---------- top bar: systems, the resource pool, the moon ----------

    static readonly string[] CoreResources = { "energy", "food", "minerals", "alloys", "consumer_goods", "research", "unity", "influence" };
    readonly Dictionary<string, Rectangle> _chip = new();
    readonly Dictionary<string, float> _chipPulse = new();

    IEnumerable<string> ShownResources() =>
        CoreResources.Concat(Defs.Cards.Where(c => c.Category == "resource" && !CoreResources.Contains(c.Id) && _sim!.Have(c.Id) > 0).Select(c => c.Id));

    void DrawTopBar()
    {
        var sim = _sim!;
        int sw = Raylib.GetScreenWidth();
        Raylib.DrawRectangle(0, 0, sw, TopBar, new Color(14, 16, 34, 255));
        Raylib.DrawLine(0, TopBar - 1, sw, TopBar - 1, new Color(60, 70, 120, 255));
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
        var info = $"{sim.Diff.Name}  |  Owned {sim.ClaimedCount}/{Defs.Rules.ClaimLimit}  |  Moon {sim.Moon}  |  Act {sim.Act}  |  x{_speed:0}";
        var w = Measure(info, 20).X;
        Text(info, sw - w - 20, 8, 20, Color.RayWhite);
        var bar = new Rectangle(sw - w - 20, 36, w, 10);
        Raylib.DrawRectangleRec(bar, new Color(40, 44, 70, 255));
        Raylib.DrawRectangleRec(new Rectangle(bar.X, bar.Y, bar.Width * sim.MoonTime / sim.MoonSeconds, bar.Height), new Color(240, 210, 120, 255));
        Text($"{Math.Max(0, sim.MoonSeconds - sim.MoonTime):0}s", bar.X - 40, 32, 15, new Color(240, 210, 120, 255));

        // The resource pool: one counter per resource, usable from every system.
        int eat = sim.AllCards.Where(c => c.Def.Category == "person").Sum(c => c.Def.FoodUpkeep);
        int power = sim.AllCards.Where(c => c.Def.Category == "person").Sum(c => c.Def.EnergyUpkeep);
        float x = 12, y = 60;
        _chip.Clear();
        foreach (var id in ShownResources())
        {
            int n = sim.Have(id);
            string sub = id == "food" && eat > 0 ? $"-{eat}/moon" : id == "energy" && power > 0 ? $"-{power}/moon" : "";
            bool shortOf = (id == "food" && n < eat) || (id == "energy" && n < power);
            float tw = Math.Max(Measure($"{n}", 22).X, sub.Length > 0 ? Measure(sub, 12).X : 0);
            var r = new Rectangle(x, y, 44 + tw, 34);
            float pulse = _chipPulse.GetValueOrDefault(id);
            Raylib.DrawRectangleRounded(r, 0.4f, 6, pulse > 0 ? new Color((byte)50, (byte)(70 + 80 * pulse), (byte)60, (byte)255) : new Color(28, 32, 58, 255));
            Raylib.DrawRectangleRoundedLinesEx(r, 0.4f, 6, 1.5f, shortOf ? new Color(255, 120, 100, 255) : new Color(80, 90, 140, 255));
            if (Tex(Defs.Card[id].Art) is { } icon) DrawFit(icon, new Rectangle(r.X + 5, r.Y + 3, 28, 28), n > 0 ? Color.White : new Color(255, 255, 255, 110));
            if (sub.Length > 0)
            {
                Text($"{n}", r.X + 38, r.Y + 1, 20, shortOf ? new Color(255, 150, 130, 255) : Color.RayWhite);
                Text(sub, r.X + 38, r.Y + 20, 12, shortOf ? new Color(255, 150, 130, 255) : new Color(190, 195, 220, 255));
            }
            else Text($"{n}", r.X + 38, r.Y + 6, 22, n > 0 ? Color.RayWhite : new Color(150, 155, 180, 255));
            _chip[id] = r;
            x += r.Width + 8;
            if (x > sw - RightPanel - 60) break;
        }
        foreach (var k in _chipPulse.Keys.ToList()) _chipPulse[k] = Math.Max(0, _chipPulse[k] - Raylib.GetFrameTime() * 2);
        // Hovering a counter names it.
        var m = Raylib.GetMousePosition();
        foreach (var (id, r) in _chip)
            if (Raylib.CheckCollisionPointRec(m, r))
            {
                var tip = $"{_res.CardName(id)}: {Defs.Card[id].Desc}";
                var sz = Measure(tip, 16);
                var tr = new Rectangle(Math.Min(r.X, sw - sz.X - 30), r.Y + r.Height + 6, sz.X + 16, 26);
                Raylib.DrawRectangleRounded(tr, 0.3f, 6, new Color(10, 12, 26, 240));
                Text(tip, tr.X + 8, tr.Y + 4, 16, Color.RayWhite);
            }
    }

    // Resources flying from where they were made up to their counter.
    readonly List<(string id, int n, Vector2 from, float t)> _floaters = new();

    void AddFloater(string id, int n, Vector2 world)
    {
        var from = Raylib.GetWorldToScreen2D(world, _cam);
        if (!Raylib.CheckCollisionPointRec(from, BoardView)) { _chipPulse[id] = 1; return; }
        _floaters.Add((id, n, from, 0));
    }

    void DrawFloaters()
    {
        float dt = Raylib.GetFrameTime();
        for (int i = _floaters.Count - 1; i >= 0; i--)
        {
            var (id, n, from, t) = _floaters[i];
            t += dt;
            const float rise = 0.55f, fly = 0.6f;
            if (t > rise + fly) { _floaters.RemoveAt(i); _chipPulse[id] = 1; continue; }
            _floaters[i] = (id, n, from, t);
            var lifted = from - new Vector2(0, 46 * Math.Min(1, t / rise));
            Vector2 at = lifted;
            if (t > rise && _chip.TryGetValue(id, out var chip))
            {
                float k = (t - rise) / fly;
                k = k * k * (3 - 2 * k);
                at = Vector2.Lerp(lifted, new Vector2(chip.X + 19, chip.Y + 17), k);
            }
            if (Tex(Defs.Card[id].Art) is { } icon) DrawFit(icon, new Rectangle(at.X - 15, at.Y - 15, 30, 30), Color.White);
            if (t < rise + fly * 0.5f)
            {
                var txt = $"+{n}";
                Text(txt, at.X + 16, at.Y - 11, 22, new Color(255, 255, 255, (int)(255 * Math.Clamp(1.6f - t, 0, 1))));
            }
        }
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
        Text("Packs - click to buy", sw - RightPanel + 12, TopBar + 10, 19, Color.LightGray);
        var mouse = Raylib.GetMousePosition();
        (PackDef p, Rectangle r)? hovered = null;
        foreach (var (p, r) in PackRects())
        {
            bool hover = Raylib.CheckCollisionPointRec(mouse, r) && _drag == null && !_escMenu;
            bool afford = sim.Have("energy") >= sim.PackCost(p);
            if (hover) hovered = (p, r);
            Raylib.DrawRectangleRounded(r, 0.12f, 6, hover ? (afford ? new Color(80, 110, 200, 255) : new Color(90, 60, 70, 255)) : new Color(36, 42, 74, 255));
            DrawBooster(new Rectangle(r.X + 6, r.Y + 3, 60, r.Height - 6), Hex(p.Color), Tex(p.Art), hover && afford);
            float ts = 20;
            while (ts > 13 && Measure(p.Name, ts).X > r.Width - 82) ts -= 0.5f;
            var costCol = afford ? new Color(240, 210, 120, 255) : new Color(200, 130, 120, 255);
            if (r.Height >= 72)
            {
                Text(p.Name, r.X + 74, r.Y + 13, ts, Color.RayWhite);
                Text($"{sim.PackCost(p)} Energy", r.X + 74, r.Y + 41, 15, costCol);
                Text($"{p.Draws} cards", r.X + 74, r.Y + 58, 14, new Color(200, 205, 230, 255));
            }
            else
            {
                Text(p.Name, r.X + 74, r.Y + 6, ts, Color.RayWhite);
                Text($"{sim.PackCost(p)} Energy, {p.Draws} cards", r.X + 74, r.Y + r.Height - 22, 14, costCol);
            }
        }
        var m = MarketRect();
        bool mh = Raylib.CheckCollisionPointRec(mouse, m) && _drag != null;
        Raylib.DrawRectangleRounded(m, 0.12f, 6, mh ? new Color(160, 130, 50, 255) : new Color(70, 58, 30, 255));
        Text(Defs.Rules.SellSlotName, m.X + 14, m.Y + 12, 26, Color.RayWhite);
        Wrapped("Drop cards here to sell them; click to trade surplus resources for Energy", m.X + 14, m.Y + 46, m.Width - 28, 15, Color.LightGray);
        Text("Tab blueprints - Space pause", sw - RightPanel + 12, m.Y - 44, 14, Color.Gray);
        Text("1-3 speed - Z zoom - Esc menu", sw - RightPanel + 12, m.Y - 24, 14, Color.Gray);

        if (hovered is { } h)
        {
            // What's in the pack.
            var names = h.p.Contents.OrderByDescending(e => e.Weight).Select(e => _res.CardName(e.Card)).Distinct().Take(10).ToList();
            var lines = $"{h.p.Name} pack - {sim.PackCost(h.p)} Energy (you have {sim.Have("energy")})\n{h.p.Desc}\nMay contain: {string.Join(", ", names)}" +
                        (h.p.Contents.Count() > 10 ? "..." : "");
            DrawTipBox(lines, new Vector2(h.r.X - 360, h.r.Y), 340);
        }
    }

    void DrawTipBox(string lines, Vector2 at, float width)
    {
        var parts = lines.Split('\n');
        float h = 16 + parts.Sum(p => 26 + (Measure(p, 18).X > width - 20 ? 22 * (int)(Measure(p, 18).X / (width - 20)) : 0));
        if (at.X + width > Raylib.GetScreenWidth()) at.X -= width + 36;
        if (at.X < 4) at.X = 4;
        if (at.Y + h > Raylib.GetScreenHeight()) at.Y = Raylib.GetScreenHeight() - h - 8;
        Raylib.DrawRectangleRounded(new Rectangle(at.X, at.Y, width, h), 0.1f, 6, new Color(10, 12, 26, 240));
        Raylib.DrawRectangleRoundedLinesEx(new Rectangle(at.X, at.Y, width, h), 0.1f, 6, 1.5f, new Color(90, 100, 160, 200));
        float y = at.Y + 8;
        for (int i = 0; i < parts.Length; i++)
            y += Wrapped(parts[i], at.X + 10, y, width - 20, i == 0 ? 20 : 17, i == 0 ? Color.RayWhite : Color.LightGray) + 4;
    }

    void DrawTooltip()
    {
        if (_drag != null || !Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), BoardView) || OverUi(Raylib.GetMousePosition())) return;
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
        var sim = _sim!;
        var lines = _res.CardName(c.Def.Id) + "\n" + c.Def.Desc;
        if (c.MaxHp > 0 && (c.MaxShield > 0 || c.MaxArmor > 0 || c.Guns.Count > 0 || c.Def.Slots > 0))
        {
            lines += $"\nHull {MathF.Ceiling(c.Hp)}/{c.MaxHp}" + (c.MaxArmor > 0 ? $"  Armour {MathF.Ceiling(c.Armor)}/{c.MaxArmor}" : "")
                   + (c.MaxShield > 0 ? $"  Shields {MathF.Ceiling(c.Shield)}/{c.MaxShield} (+{c.ShieldRegen:0.#}/s)" : "")
                   + (c.HullRegen > 0 ? $"  Heals {c.HullRegen:0.#}/s" : "") + (c.Evasion > 0 ? $"  Dodge {c.Evasion:P0}" : "");
            if (c.Guns.Count > 0) lines += "\nWeapons: " + string.Join(", ", c.Guns.Select(g => $"{g.Profile?.Name ?? "Guns"} {g.Damage:0}/{g.Cooldown:0.#}s"));
            if (c.Def.Slots > 0) lines += $"\nSlots {c.Parts.Count}/{c.Def.Slots}" + (c.Parts.Count < c.Def.Slots ? " - drop ship components here to fit them" : " - drop a better part to swap, or click to take one off");
            if (c.Def.HasTag("warship"))
                lines += c.Admiral != null ? $"\nFleet Admiral aboard: this fleet's ships hit {Defs.Card["admiral"].BoostMult - 1:P0} harder."
                    : $"\nStack up to {sim.FleetSize} warships into a fleet; drop an Admiral on it to command it.";
        }
        if (c.Def.Category == "tech")
        {
            var r = Defs.Recipes.FirstOrDefault(x => x.Effect == "learn" && x.Station == c.Def.Id);
            if (sim.Techs.Contains(c.Def.Id)) lines += "\nAlready researched: sell it at the Market.";
            else if (r != null)
                lines += $"\nResearch: put a Pop ({r.Time:0}s) or a Scientist ({r.Time / Defs.Card["scientist"].BoostMult:0}s) on it; costs {string.Join(", ", Sim.Costs(r).Select(i => $"{i.N} {_res.CardName(i.Card)}"))}."
                         + (r.RequiresTech != "none" && !sim.Techs.Contains(r.RequiresTech) ? $"\nNeeds {_res.CardName(r.RequiresTech)} researched first." : "");
        }
        var orders = sim.OrdersFor(c).Where(r => sim.Blueprint(r) != Sim.BlueprintState.Locked).ToList();
        if (orders.Count > 0) lines += $"\nClick to choose what it makes ({orders.Count} blueprints).";
        else
        {
            var uses = sim.UsedIn(c.Def).Take(4).ToList();
            if (uses.Count > 0) lines += "\nUsed in: " + string.Join(" | ", uses.Select(BlueprintLine));
        }
        if (Defs.Component.TryGetValue(c.Def.Id, out var cp) && cp.Kind == "weapon")
            lines += $"\nDamage {cp.Damage} every {cp.Cooldown:0.#}s - vs shields x{cp.VsShield:0.##}, armour x{cp.VsArmor:0.##}, hull x{cp.VsHull:0.##}"
                   + (cp.PierceShield >= 1 ? (cp.PierceArmor >= 1 ? " - ignores shields and armour" : " - flies past shields") : "");
        if (s?.Order != null) lines += $"\nOrder: {s.Order.Desc}";
        if (s?.Active != null && s.Wait == null) lines += $"\nWorking: {s.Active.Desc} ({Math.Max(0, s.Duration - s.Progress):0}s)";
        if (s?.Wait != null) lines += $"\nWaiting: {s.Wait}";
        DrawTipBox(lines, Raylib.GetMousePosition() + new Vector2(18, 18), 360);
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

    /// <summary>The player's tutorial setting, except in --screenshot captures, which show the game without it.</summary>
    bool TutorialShown => _settings.Tutorial && _shot == null;

    /// <summary>The tutorial checklist: the current step, its hint, progress, and Skip / Hide.</summary>
    void DrawTutorial()
    {
        var sim = _sim!;
        if (!TutorialShown || _codex || _screen != Screen.Play) return;
        foreach (var t in Defs.Tutorial)
            if (sim.StepDone(t) && !sim.SkippedSteps.Contains(t.Id) && _tutorialSeen.Add(t.Id) && _clock > 1) Toast($"Tutorial: {t.Text} - done!");
        var step = sim.CurrentStep;
        if (step == null) return;
        int done = Defs.Tutorial.Count(sim.StepDone);
        var r = new Rectangle(16, TopBar + 12, 440, 150);
        _uiRects.Add(r);
        Raylib.DrawRectangleRounded(r, 0.08f, 6, new Color(12, 16, 38, 235));
        Raylib.DrawRectangleRoundedLinesEx(r, 0.08f, 6, 2, new Color(240, 200, 90, 200));
        Text($"Tutorial  {done}/{Defs.Tutorial.Length}", r.X + 14, r.Y + 10, 18, new Color(240, 200, 90, 255));
        Text(step.Text, r.X + 14, r.Y + 34, 21, Color.RayWhite);
        Wrapped(step.Hint, r.X + 14, r.Y + 62, r.Width - 28, 16, Color.LightGray, 4);
        if (Button(new Rectangle(r.X + r.Width - 150, r.Y + 8, 66, 24), "Skip", false, 15)) sim.SkippedSteps.Add(step.Id);
        if (Button(new Rectangle(r.X + r.Width - 78, r.Y + 8, 64, 24), "Hide", false, 15)) { _settings.Tutorial = false; _settings.Save(); Toast("Tutorial hidden. Turn it back on from the Esc menu."); }
    }

    // ---------- the system you're looking at: abandon an unwanted one ----------

    StarSystem? _abandonArmed;
    float _abandonAt;

    void DrawSystemPanel()
    {
        var sim = _sim!;
        if (_screen != Screen.Play || _escMenu || _codex || _cam.Zoom < 0.3f) return;
        var z = sim.SystemAt(_cam.Target);
        if (z == null || z == sim.Home || z.Claimed) return;
        var view = BoardView;
        var r = new Rectangle(view.X + view.Width - 316, view.Y + 10, 300, 74);
        _uiRects.Add(r);
        Raylib.DrawRectangleRounded(r, 0.15f, 6, new Color(12, 16, 38, 225));
        Raylib.DrawRectangleRoundedLinesEx(r, 0.15f, 6, 1.5f, new Color(120, 130, 170, 200));
        Text($"{z.Name} - unclaimed", r.X + 12, r.Y + 8, 18, Color.RayWhite);
        bool armed = _abandonArmed == z && _clock - _abandonAt < 4;
        var why = sim.AbandonBlock(z);
        if (Button(new Rectangle(r.X + 12, r.Y + 36, r.Width - 24, 30), armed ? "Click again to abandon it" : "Abandon this system", armed, 16))
        {
            if (why != null) Toast(why);
            else if (!armed) { _abandonArmed = z; _abandonAt = _clock; }
            else
            {
                _abandonArmed = null;
                sim.Abandon(z);
                GoTo(sim.Home);
            }
        }
    }
}
