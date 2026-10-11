using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace StellarAscension;

/// <summary>The small immediate-mode UI kit both acts draw with: fonts, textures from the players' installs,
/// panels, buttons, tooltips and scroll areas. Panels register the screen area they cover, so a click on a
/// panel never falls through to the map underneath.</summary>
public sealed class Gfx
{
    public Font Body, Title;
    public bool CustomFont;
    readonly Dictionary<string, Texture2D?> _tex = new();
    readonly List<Rectangle> _blockers = new(), _blockersNext = new();
    string? _tooltip;
    bool _clickTaken;
    public readonly Dictionary<string, float> Scroll = new();
    /// <summary>UI scripts (test aid): the topmost enabled button whose label starts with this is pressed, as if
    /// clicked. Topmost means drawn last, as with the mouse: a window covers the panels under it.</summary>
    public string? ScriptClick;
    public bool ScriptClicked;
    public readonly List<string> LabelsThisFrame = new();
    int _scriptMatches, _scriptTarget = -1;

    bool Scripted(string label, bool enabled)
    {
        LabelsThisFrame.Add(label);
        if (ScriptClick == null || !enabled || !label.StartsWith(ScriptClick, StringComparison.OrdinalIgnoreCase)) return false;
        if (_scriptMatches++ != _scriptTarget) return false; // last frame's matches tell which one is on top
        ScriptClick = null;
        ScriptClicked = true;
        return true;
    }

    // Theme: Civ VI's navy and gold for Act 1, Stellaris's teal for Act 2.
    public static readonly Color Ink = new(236, 232, 220, 255);
    public static readonly Color Muted = new(160, 166, 180, 255);
    public static readonly Color Gold = new(222, 186, 104, 255);
    public static readonly Color Good = new(130, 210, 120, 255);
    public static readonly Color Bad = new(235, 110, 95, 255);
    public Color PanelBg = new(16, 24, 42, 236);
    public Color PanelEdge = new(150, 124, 70, 255);
    public Color ButtonBg = new(36, 50, 80, 255);
    public Color ButtonHot = new(58, 80, 122, 255);

    public void UseAct1Theme()
    {
        PanelBg = new(16, 24, 42, 236); PanelEdge = new(150, 124, 70, 255); ButtonBg = new(36, 50, 80, 255); ButtonHot = new(58, 80, 122, 255);
    }

    public void UseAct2Theme()
    {
        PanelBg = new(8, 26, 30, 236); PanelEdge = new(60, 150, 150, 255); ButtonBg = new(20, 54, 60, 255); ButtonHot = new(32, 86, 92, 255);
    }

    public static float W => Raylib.GetScreenWidth();
    public static float H => Raylib.GetScreenHeight();
    /// <summary>In UI scripts the real pointer is ignored, so it can't hover or click anything.</summary>
    public static bool IgnoreMouse;
    /// <summary>UI scripts can point a pretend mouse at the screen (for hover cards in screenshots).</summary>
    public static Vector2? FakeMouse;
    public static Vector2 Mouse => FakeMouse ?? (IgnoreMouse ? new Vector2(-9999, -9999) : Raylib.GetMousePosition());

    /// <summary>The characters the fonts are built with: Latin (with accents), Greek, Cyrillic and common punctuation.</summary>
    public static readonly int[] Glyphs = Enumerable.Range(32, 95).Concat(Enumerable.Range(0xA0, 0x24F - 0xA0 + 1)).Concat(Enumerable.Range(0x370, 0x90)).Concat(Enumerable.Range(0x400, 0x100))
        .Concat(new[] { 0x2013, 0x2014, 0x2018, 0x2019, 0x201C, 0x201D, 0x2022, 0x2026, 0x2192, 0x2190, 0x00D7 }).Distinct().ToArray();
    static readonly HashSet<int> GlyphSet = Glyphs.ToHashSet();
    /// <summary>True if every character of the text has a glyph in the game's fonts.</summary>
    public static bool Drawable(string text) => text.EnumerateRunes().All(r => GlyphSet.Contains(r.Value));

    public void LoadFonts(string? bodyFile, string? titleFile)
    {
        Body = Title = Raylib.GetFontDefault();
        var glyphs = Glyphs;
        Font Load(string file)
        {
            var bytes = File.ReadAllBytes(file);
            var f = Raylib.LoadFontFromMemory(Path.GetExtension(file).ToLowerInvariant(), bytes, 64, glyphs, glyphs.Length);
            var t = f.Texture;
            Raylib.GenTextureMipmaps(ref t);
            f.Texture = t;
            Raylib.SetTextureFilter(f.Texture, TextureFilter.Trilinear);
            return f;
        }
        try
        {
            if (bodyFile != null) { Body = Load(bodyFile); CustomFont = true; }
            Title = titleFile != null ? Load(titleFile) : Body;
        }
        catch (Exception e) { Log.Info($"font: could not load ({e.Message})"); }
    }

    // ---------------------------------------------------------------- frame

    public void BeginFrame()
    {
        _blockers.Clear();
        _blockers.AddRange(_blockersNext);
        _blockersNext.Clear();
        _tooltip = null;
        _clickTaken = false;
        LabelsThisFrame.Clear();
        ScriptClicked = false;
        _scriptTarget = ScriptClick == null ? -1 : _scriptMatches - 1;
        _scriptMatches = 0;
    }

    public void EndFrame()
    {
        if (_tooltip != null) DrawTooltip(_tooltip);
    }

    /// <summary>Marks screen area as UI for this and the next frame.</summary>
    public void Block(Rectangle r) => _blockersNext.Add(r);

    /// <summary>True when the mouse is over no panel (so the map may take the input).</summary>
    public bool MouseOnMap => !_blockers.Any(b => Raylib.CheckCollisionPointRec(Mouse, b)) && !_blockersNext.Any(b => Raylib.CheckCollisionPointRec(Mouse, b));

    public bool Hover(Rectangle r) => Raylib.CheckCollisionPointRec(Mouse, r);

    public bool Clicked(Rectangle r)
    {
        if (_clickTaken || !Hover(r) || !Raylib.IsMouseButtonPressed(MouseButton.Left)) return false;
        _clickTaken = true;
        return true;
    }

    public bool ClickTaken => _clickTaken;
    public void TakeClick() => _clickTaken = true;

    public void Tip(string text) => _tooltip = text;

    // ---------------------------------------------------------------- textures

    public unsafe Texture2D? Tex(string key, Func<ImageData?> load)
    {
        if (_tex.TryGetValue(key, out var t)) return t;
        ImageData? img = null;
        try { img = load(); } catch (Exception e) { Log.Info($"texture {key}: {e.Message}"); }
        if (img == null) return _tex[key] = null;
        fixed (byte* p = img.Rgba)
        {
            var im = new Image { Data = p, Width = img.W, Height = img.H, Mipmaps = 1, Format = PixelFormat.UncompressedR8G8B8A8 };
            var tex = Raylib.LoadTextureFromImage(im);
            Raylib.GenTextureMipmaps(ref tex);
            Raylib.SetTextureFilter(tex, TextureFilter.Trilinear);
            return _tex[key] = tex;
        }
    }

    public static void Draw(Texture2D? t, Rectangle r, Color tint)
    {
        if (t is not { } tex) return;
        Raylib.DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height), r, Vector2.Zero, 0, tint);
    }

    /// <summary>Draw keeping the picture's shape, centred in r.</summary>
    public static void DrawFit(Texture2D? t, Rectangle r, Color tint)
    {
        if (t is not { } tex) return;
        float s = Math.Min(r.Width / tex.Width, r.Height / tex.Height);
        float w = tex.Width * s, h = tex.Height * s;
        Raylib.DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height), new Rectangle(r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h), Vector2.Zero, 0, tint);
    }

    /// <summary>Fill r with the picture, cropping the overflow (like CSS cover).</summary>
    public static void DrawCover(Texture2D? t, Rectangle r, Color tint)
    {
        if (t is not { } tex) return;
        float s = Math.Max(r.Width / tex.Width, r.Height / tex.Height);
        float sw = r.Width / s, sh = r.Height / s;
        Raylib.DrawTexturePro(tex, new Rectangle((tex.Width - sw) / 2, (tex.Height - sh) / 2, sw, sh), r, Vector2.Zero, 0, tint);
    }

    // ---------------------------------------------------------------- text

    public void Text(string s, float x, float y, float size, Color c, bool title = false) =>
        Raylib.DrawTextEx(title ? Title : Body, Fold(s), new Vector2(MathF.Round(x), MathF.Round(y)), size, size / 32f, c);

    public float Measure(string s, float size, bool title = false) => Raylib.MeasureTextEx(title ? Title : Body, Fold(s), size, size / 32f).X;

    static readonly Dictionary<string, string> _folded = new();
    /// <summary>The base letter of each character U+1E00 to U+1EFF (Vietnamese and other accented Latin), from Unicode's decompositions.</summary>
    const string LatinExtendedAdditionalBase = "AaBbBbBbCcDdDdDdDdDdEeEeEeEeEeFfGgHhHhHhHhHhIiIiKkKkKkLlLlLlLlMmMmMmNnNnNnNnOoOoOoOoPpPpRrRrRrRrSsSsSsSsSsTtTtTtTtUuUuUuUuUuVvVvWwWwWwWwWwXxXxYyZzZzZzhtwy??????AaAaAaAaAaAaAaAaAaAaAaAaEeEeEeEeEeEeEeEeIiIiOoOoOoOoOoOoOoOoOoOoOoOoUuUuUuUuUuUuUuYyYyYyYy??????";

    /// <summary>Text the fonts can draw: a letter the fonts lack loses its accent (Vietnamese "Hạ Long" shows as "Ha Long");
    /// anything else missing becomes "?".</summary>
    public static string Fold(string s)
    {
        if (Drawable(s)) return s;
        if (_folded.TryGetValue(s, out var f)) return f;
        var sb = new System.Text.StringBuilder();
        foreach (var r in s.EnumerateRunes())
        {
            if (GlyphSet.Contains(r.Value)) { sb.Append(r.ToString()); continue; }
            // (Normalize() can't decompose with this build's invariant globalization, so the accented Latin block is a table.)
            sb.Append(r.Value is >= 0x1E00 and < 0x1F00 ? LatinExtendedAdditionalBase[r.Value - 0x1E00] : '?');
        }
        return _folded[s] = sb.ToString();
    }

    public void TextCentered(string s, Rectangle r, float size, Color c, bool title = false) =>
        Text(s, r.X + (r.Width - Measure(s, size, title)) / 2, r.Y + (r.Height - size) / 2, size, c, title);

    public void TextRight(string s, float right, float y, float size, Color c) => Text(s, right - Measure(s, size), y, size, c);

    public List<string> Wrap(string s, float size, float width)
    {
        var lines = new List<string>();
        foreach (var para in s.Split('\n'))
        {
            var line = "";
            foreach (var word in para.Split(' '))
            {
                var t = line.Length == 0 ? word : line + " " + word;
                if (Measure(t, size) > width && line.Length > 0) { lines.Add(line); line = word; }
                else line = t;
            }
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>Wrapped text; returns the height used.</summary>
    public float Para(string s, float x, float y, float size, float width, Color c, float lineGap = 1.25f)
    {
        var lines = Wrap(s, size, width);
        for (int i = 0; i < lines.Count; i++) Text(lines[i], x, y + i * size * lineGap, size, c);
        return lines.Count * size * lineGap;
    }

    public float ParaHeight(string s, float size, float width, float lineGap = 1.25f) => Wrap(s, size, width).Count * size * lineGap;

    // ---------------------------------------------------------------- widgets

    public void Panel(Rectangle r, bool block = true)
    {
        Raylib.DrawRectangleRounded(r, 0.04f, 6, PanelBg);
        Raylib.DrawRectangleRoundedLinesEx(r, 0.04f, 6, 1.5f, PanelEdge);
        if (block) Block(r);
    }

    public bool Button(Rectangle r, string label, bool enabled = true, bool selected = false, string? tip = null, float size = 20)
    {
        bool hot = enabled && Hover(r);
        var bg = selected ? new Color(PanelEdge.R, PanelEdge.G, PanelEdge.B, (byte)140) : hot ? ButtonHot : ButtonBg;
        if (!enabled) bg = new Color(bg.R, bg.G, bg.B, (byte)110);
        Raylib.DrawRectangleRounded(r, 0.25f, 6, bg);
        Raylib.DrawRectangleRoundedLinesEx(r, 0.25f, 6, selected ? 2f : 1f, selected ? Gold : PanelEdge);
        TextCentered(label, r, size, enabled ? Ink : Muted);
        if (tip != null && Hover(r)) Tip(tip);
        Block(r);
        return Scripted(label, enabled) || enabled && Clicked(r);
    }

    /// <summary>A button with an icon on the left and up to two lines of text.</summary>
    public bool IconButton(Rectangle r, Texture2D? icon, string line1, string? line2, bool enabled = true, bool selected = false, string? tip = null, Color? iconTint = null)
    {
        bool hot = enabled && Hover(r);
        var bg = selected ? new Color(PanelEdge.R, PanelEdge.G, PanelEdge.B, (byte)120) : hot ? ButtonHot : ButtonBg;
        if (!enabled) bg = new Color((byte)(bg.R / 2), (byte)(bg.G / 2), (byte)(bg.B / 2), (byte)200);
        Raylib.DrawRectangleRounded(r, 0.15f, 6, bg);
        Raylib.DrawRectangleRoundedLinesEx(r, 0.15f, 6, selected ? 2f : 1f, selected ? Gold : PanelEdge);
        float s = r.Height - 8;
        DrawFit(icon, new Rectangle(r.X + 4, r.Y + 4, s, s), iconTint ?? (enabled ? Color.White : new Color(255, 255, 255, 130)));
        float tx = r.X + s + 12;
        if (line2 == null) Text(Clip(line1, 18, r.Width - s - 18), tx, r.Y + (r.Height - 18) / 2, 18, enabled ? Ink : Muted);
        else
        {
            Text(Clip(line1, 18, r.Width - s - 18), tx, r.Y + r.Height / 2 - 20, 18, enabled ? Ink : Muted);
            Text(Clip(line2, 15, r.Width - s - 18), tx, r.Y + r.Height / 2 + 2, 15, enabled ? Muted : new Color(120, 120, 120, 255));
        }
        if (tip != null && Hover(r)) Tip(tip);
        Block(r);
        return Scripted(line1, enabled) || enabled && Clicked(r);
    }

    /// <summary>A custom-drawn clickable area that UI scripts can press by its label, like a button.</summary>
    public bool HitArea(Rectangle r, string label) => Scripted(label, true) || Clicked(r);

    /// <summary>Scripted presses come from outside the scroll area too; this lets list rows accept them.</summary>
    public bool ScriptActive => ScriptClicked;

    public string Clip(string s, float size, float width)
    {
        if (Measure(s, size) <= width) return s;
        while (s.Length > 1 && Measure(s + "…", size) > width) s = s[..^1];
        return s + "…";
    }

    void DrawTooltip(string text)
    {
        float size = 17, width = Math.Min(420, Measure(text, size) + 24);
        var lines = Wrap(text, size, width - 20);
        float h = lines.Count * size * 1.25f + 16;
        var m = Mouse;
        float x = Math.Min(m.X + 18, W - width - 6), y = m.Y + 22;
        if (y + h > H - 6) y = m.Y - h - 8;
        var r = new Rectangle(x, y, width, h);
        Raylib.DrawRectangleRounded(r, 0.08f, 6, new Color(6, 8, 16, 245));
        Raylib.DrawRectangleRoundedLinesEx(r, 0.08f, 6, 1, Gold);
        for (int i = 0; i < lines.Count; i++) Text(lines[i], x + 10, y + 8 + i * size * 1.25f, size, Ink);
    }

    /// <summary>A vertical scroll area: call Begin, draw rows at the y it returns plus your own offset, then End.</summary>
    public float BeginScroll(string id, Rectangle r, float contentHeight)
    {
        float off = Scroll.GetValueOrDefault(id);
        if (Hover(r))
        {
            off -= Raylib.GetMouseWheelMove() * 60;
        }
        off = Math.Clamp(off, 0, Math.Max(0, contentHeight - r.Height));
        Scroll[id] = off;
        Raylib.BeginScissorMode((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
        if (contentHeight > r.Height)
        {
            float bar = r.Height * r.Height / contentHeight, by = r.Y + (r.Height - bar) * off / (contentHeight - r.Height);
            Raylib.DrawRectangleRounded(new Rectangle(r.X + r.Width - 5, by, 4, bar), 1, 4, new Color(PanelEdge.R, PanelEdge.G, PanelEdge.B, (byte)160));
        }
        return r.Y - off;
    }

    public void EndScroll() => Raylib.EndScissorMode();

    public static Color Rgb(int r, int g, int b, int a = 255) => new((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255), (byte)Math.Clamp(a, 0, 255));

    public static Color Hex(string hex)
    {
        hex = hex.TrimStart('#');
        return new Color(Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16), (byte)255);
    }

    public static string Num(double v) => Math.Abs(v) >= 1000 ? $"{v / 1000:0.#}k" : Math.Abs(v - Math.Round(v)) < 0.05 ? $"{v:0}" : $"{v:0.#}";
    public static string Signed(double v) => (v >= 0 ? "+" : "") + Num(v);
}
