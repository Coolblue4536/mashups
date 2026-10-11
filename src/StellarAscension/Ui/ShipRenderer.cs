using System.Numerics;
using System.Text.RegularExpressions;
using Raylib_cs;

namespace StellarAscension;

/// <summary>Stellaris's own ship models, rendered. Each ship is assembled from the install's .mesh sections (a corvette is
/// one piece; a destroyer a bow and a stern; cruisers and battleships a bow, mid and stern, joined where the frame says),
/// lit and painted in the empire's colour where Stellaris paints it, and baked once into a sprite seen from the side and
/// a little above, bow to the right. Sprites are baked between frames (never inside another render pass).</summary>
public sealed class ShipRenderer
{
    public const int SpriteW = 512, SpriteH = 200;

    readonly string _shipsDir;
    readonly Dictionary<string, List<(Mesh mesh, PdxMesh.Part part, Vector3 at)>?> _models = new();
    readonly Dictionary<string, Texture2D?> _tex = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, RenderTexture2D?> _sprites = new();
    readonly List<(string key, string culture, string ship, Color team)> _pending = new();
    Shader _shader;
    Material _mat;
    bool _shaderReady;
    int _locTeam, _locView, _locLight, _locHasNormal;

    public ShipRenderer(string stellarisRoot) => _shipsDir = Path.Combine(stellarisRoot, "gfx", "models", "ships");

    /// <summary>Ship styles the install has (Stellaris "graphical cultures" with ships).</summary>
    public static readonly string[] EarthCultures = { "humanoid_01", "mammalian_01", "avian_01", "reptilian_01", "molluscoid_01", "arthropoid_01", "fungoid_01" };

    /// <summary>Styles that render well here (psionic and mindwarden hulls are near-black without Stellaris's effects;
    /// some styles have no warships).</summary>
    static readonly HashSet<string> Good = new() { "aquatic_01", "arthropoid_01", "avian_01", "cybernetics_01", "fungoid_01", "humanoid_01", "infernal_01",
        "lithoid_01", "mammalian_01", "molluscoid_01", "necroid_01", "nemesis_01", "plantoid_01", "reptilian_01", "synthetics_01", "toxoid_01" };

    /// <summary>The ship style to use: the empire's own when it renders well, else one picked from its name.</summary>
    public string Culture(string wanted, string seed)
    {
        if (Good.Contains(wanted) && Directory.Exists(Path.Combine(_shipsDir, wanted))) return wanted;
        int h = Math.Abs(seed.Aggregate(23, (a, ch) => a * 31 + ch));
        return EarthCultures[h % EarthCultures.Length];
    }

    // ---------------------------------------------------------------- the sprite cache

    /// <summary>The baked sprite, or null while it waits to be baked (or if the install lacks the model).</summary>
    public Texture2D? Sprite(string culture, string ship, Color team)
    {
        string key = $"{culture}|{ship}|{team.R},{team.G},{team.B}";
        if (_sprites.TryGetValue(key, out var rt)) return rt?.Texture;
        if (!_pending.Any(p => p.key == key)) _pending.Add((key, culture, ship, team));
        return null;
    }

    public bool Pending => _pending.Count > 0;

    /// <summary>--ships: bakes every style's warships and saves them as PNGs (needs a window).</summary>
    public int Export(string outDir)
    {
        Directory.CreateDirectory(outDir);
        int n = 0;
        foreach (var culture in Directory.GetDirectories(_shipsDir).Select(Path.GetFileName).Where(d => d!.EndsWith("_01")).Select(d => d!))
            foreach (var ship in new[] { "corvette", "destroyer", "cruiser", "battleship", "science", "construction", "colony" })
            {
                if (Model(culture, ship) == null) { Log.Info($"ships: {culture} {ship} not found"); continue; }
                if (Bake(culture, ship, new Color(200, 60, 50, 255)) is not { } rt) continue;
                var img = Raylib.LoadImageFromTexture(rt.Texture);
                Raylib.ImageFlipVertical(ref img);
                Raylib.ExportImage(img, Path.Combine(outDir, $"{culture}__{ship}.png"));
                Raylib.UnloadImage(img);
                n++;
            }
        Log.Info($"ships: {n} exported to {outDir}");
        return 0;
    }

    /// <summary>Bakes waiting sprites (call outside BeginDrawing).</summary>
    public void BakePending(int max = 3)
    {
        for (int i = 0; i < max && _pending.Count > 0; i++)
        {
            var (key, culture, ship, team) = _pending[0];
            _pending.RemoveAt(0);
            try { _sprites[key] = Bake(culture, ship, team); }
            catch (Exception e) { Log.Info($"ship {key}: {e.Message}"); _sprites[key] = null; }
        }
    }

    /// <summary>Draws a sprite centred on `at`, `length` pixels long, bow to the right (or left).</summary>
    public static void Draw(Texture2D t, Vector2 at, float length, bool facingRight, Color tint)
    {
        float h = length * t.Height / t.Width;
        // Render textures are stored upside down: flip back; a negative width mirrors the ship.
        var src = new Rectangle(0, t.Height, facingRight ? t.Width : -t.Width, -t.Height);
        Raylib.DrawTexturePro(t, src, new Rectangle(at.X - length / 2, at.Y - h / 2, length, h), Vector2.Zero, 0, tint);
    }

    // ---------------------------------------------------------------- the model

    static string[] SectionPatterns(string ship) => ship switch
    {
        "corvette" => new[] { "_corvette" },
        "destroyer" => new[] { "_destroyer_bow", "_destroyer_stern" },
        "cruiser" => new[] { "_cruiser_bow", "_cruiser_mid", "_cruiser_stern" },
        "battleship" => new[] { "_battleship_bow", "_battleship_mid", "_battleship_stern" },
        "science" => new[] { "_science" },
        "construction" => new[] { "_construction" },
        "colony" => new[] { "_colony" },
        _ => new[] { "_corvette" },
    };

    static readonly Regex Skip = new("frame|_bot|forcefield|_death|_dead|_wreck|_station|_damaged|_lod|_shield", RegexOptions.IgnoreCase);

    List<(Mesh mesh, PdxMesh.Part part, Vector3 at)>? Model(string culture, string ship)
    {
        string key = culture + "|" + ship;
        if (_models.TryGetValue(key, out var m)) return m;
        var dir = Path.Combine(_shipsDir, culture);
        if (!Directory.Exists(dir)) return _models[key] = null;
        var files = Directory.GetFiles(dir, "*.mesh").Select(Path.GetFileName).Where(f => f != null && !Skip.IsMatch(f)).Select(f => f!).OrderBy(f => f.Length).ThenBy(f => f, StringComparer.Ordinal).ToList();
        // Each section: the first variant by name, except a mid, which is the longest (some sets include short filler mids).
        static bool Drawn(PdxMesh.Part p) => !p.Shader.Contains("Alpha") && !p.Shader.Contains("Additive") && !p.Shader.Contains("Snare");
        (float lo, float hi) Span(PdxMesh m) { var d = m.Parts.Where(Drawn).ToList(); return d.Count == 0 ? (0, 0) : (d.Min(x => x.Min.Z), d.Max(x => x.Max.Z)); }
        var sections = new List<PdxMesh>();
        foreach (var pat in SectionPatterns(ship))
        {
            var cands = files.Where(x => x.Contains(culture + pat, StringComparison.OrdinalIgnoreCase)).ToList();
            PdxMesh? pick = null;
            if (pat.EndsWith("_mid"))
                pick = cands.Select(f => PdxMesh.Load(Path.Combine(dir, f))).Where(x => x is { Parts.Count: > 0 }).OrderByDescending(x => { var (lo, hi) = Span(x!); return hi - lo; }).FirstOrDefault();
            else
                foreach (var f in cands) if (PdxMesh.Load(Path.Combine(dir, f)) is { Parts.Count: > 0 } mm && mm.Parts.Any(Drawn)) { pick = mm; break; }
            if (pick == null) return _models[key] = null;
            sections.Add(pick);
        }
        // Where the sections join (Stellaris's convention): a bow's origin is its back joint and a stern's its front joint;
        // two-section ships meet at 0; a mid's front and back joints are the frame's part2 locators (else its own ends).
        var at = new Vector3[sections.Count];
        if (sections.Count == 3)
        {
            var frameFile = Directory.GetFiles(dir, "*frame.mesh").Select(Path.GetFileName).FirstOrDefault(x => x!.Contains($"{culture}_{ship}_frame", StringComparison.OrdinalIgnoreCase));
            var frame = frameFile != null ? PdxMesh.Load(Path.Combine(dir, frameFile)) : null;
            var (midLo, midHi) = Span(sections[1]);
            float front = frame?.Locators.TryGetValue("part2_front_locator", out var fl) == true && Math.Abs(fl.Z - midLo) < 0.35f * (midHi - midLo) ? fl.Z : midLo;
            float back = frame?.Locators.TryGetValue("part2_back_locator", out var bl) == true && Math.Abs(bl.Z - midHi) < 0.35f * (midHi - midLo) ? bl.Z : midHi;
            at[0] = new Vector3(0, 0, front); at[1] = Vector3.Zero; at[2] = new Vector3(0, 0, back);
        }
        // Some sets aren't authored with the joint at the origin: close any gap so the sections touch.
        for (int i = 0; i + 1 < sections.Count; i++)
        {
            var (aLo, aHi) = Span(sections[i]); var (bLo, bHi) = Span(sections[i + 1]);
            float back = at[i].Z + aHi, front = at[i + 1].Z + bLo;
            float overlap = 0.04f * Math.Min(aHi - aLo, bHi - bLo);
            if (front > back - overlap)
            {
                float shift = front - back + overlap;
                if (i == 0) at[0].Z += shift; else at[i + 1].Z -= shift;
            }
        }
        var list = new List<(Mesh, PdxMesh.Part, Vector3)>();
        for (int i = 0; i < sections.Count; i++)
            foreach (var part in sections[i].Parts.Where(Drawn))
                if (Upload(part) is { } mesh) list.Add((mesh, part, at[i]));
        return _models[key] = list.Count > 0 ? list : null;
    }

    static unsafe Mesh? Upload(PdxMesh.Part p)
    {
        int vc = p.P.Length / 3;
        if (vc == 0 || vc > 65535 || p.Tri.Length == 0 || p.Tri.Max() >= vc) return null;
        var mesh = new Mesh { VertexCount = vc, TriangleCount = p.Tri.Length / 3 };
        mesh.Vertices = Raylib.New<float>((uint)p.P.Length);
        mesh.Normals = Raylib.New<float>((uint)p.P.Length);
        mesh.TexCoords = Raylib.New<float>((uint)(vc * 2));
        mesh.Indices = Raylib.New<ushort>((uint)p.Tri.Length);
        for (int i = 0; i < p.P.Length; i++) { mesh.Vertices[i] = p.P[i]; mesh.Normals[i] = i < p.N.Length ? p.N[i] : 0; }
        for (int i = 0; i < vc * 2; i++) mesh.TexCoords[i] = i < p.U.Length ? p.U[i] : 0;
        for (int i = 0; i < p.Tri.Length; i++) mesh.Indices[i] = (ushort)p.Tri[i];
        if (p.T is { } t && t.Length >= vc * 4)
        {
            mesh.Tangents = Raylib.New<float>((uint)(vc * 4));
            for (int i = 0; i < vc * 4; i++) mesh.Tangents[i] = t[i];
        }
        Raylib.UploadMesh(ref mesh, false);
        return mesh;
    }

    Texture2D? Tex(string culture, string file)
    {
        if (file == "") return null;
        var path = Path.Combine(_shipsDir, culture, file);
        if (_tex.TryGetValue(path, out var t)) return t;
        if (!File.Exists(path)) return _tex[path] = null;
        var img = ImageData.FromFile(path);
        if (img == null) return _tex[path] = null;
        unsafe
        {
            fixed (byte* px = img.Rgba)
            {
                var im = new Image { Data = px, Width = img.W, Height = img.H, Mipmaps = 1, Format = PixelFormat.UncompressedR8G8B8A8 };
                var tex = Raylib.LoadTextureFromImage(im);
                Raylib.GenTextureMipmaps(ref tex);
                Raylib.SetTextureFilter(tex, TextureFilter.Trilinear);
                Raylib.SetTextureWrap(tex, TextureWrap.Repeat);
                return _tex[path] = tex;
            }
        }
    }

    // ---------------------------------------------------------------- the shader

    const string Vs = @"#version 330
in vec3 vertexPosition; in vec2 vertexTexCoord; in vec3 vertexNormal; in vec4 vertexTangent;
uniform mat4 mvp; uniform mat4 matModel; uniform mat4 matNormal;
out vec2 uv; out vec3 wpos; out vec3 wn; out vec4 wt;
void main() {
    uv = vertexTexCoord;
    wpos = (matModel * vec4(vertexPosition, 1.0)).xyz;
    wn = normalize((matNormal * vec4(vertexNormal, 0.0)).xyz);
    wt = vec4((matModel * vec4(vertexTangent.xyz, 0.0)).xyz, vertexTangent.w);
    gl_Position = mvp * vec4(vertexPosition, 1.0);
}";

    // Stellaris ship textures: diffuse (grey hull detail), a properties map whose blue channel is shine and whose alpha marks
    // the paint (the empire's colour), and a normal map with X in alpha and Y in green.
    const string Fs = @"#version 330
in vec2 uv; in vec3 wpos; in vec3 wn; in vec4 wt;
uniform sampler2D texture0; uniform sampler2D texture1; uniform sampler2D texture2;
uniform vec3 teamColor; uniform vec3 viewPos; uniform vec3 lightDir; uniform int hasNormal;
out vec4 finalColor;
void main() {
    vec4 d = texture(texture0, uv);
    vec4 p = texture(texture1, uv);
    vec3 N = normalize(wn);
    if (hasNormal == 1 && dot(wt.xyz, wt.xyz) > 0.01) {
        vec4 nm = texture(texture2, uv);
        vec3 T = normalize(wt.xyz - N * dot(N, wt.xyz));
        vec3 B = cross(N, T) * (wt.w < 0.0 ? -1.0 : 1.0);
        vec2 xy = vec2(nm.a, nm.g) * 2.0 - 1.0;
        float z = sqrt(max(0.0, 1.0 - dot(xy, xy)));
        N = normalize(T * xy.x + B * xy.y + N * z);
    }
    vec3 base = d.rgb;
    float paint = clamp(p.a, 0.0, 1.0);
    base = mix(base, base * (0.35 + teamColor * 1.5), paint * 0.6);
    vec3 L = normalize(lightDir);
    vec3 V = normalize(viewPos - wpos);
    float lam = max(dot(N, L), 0.0);
    vec3 col = base * (0.16 + 1.25 * lam * vec3(1.0, 0.96, 0.9));
    col += base * 0.25 * max(dot(N, normalize(vec3(0.3, -0.5, 0.6))), 0.0) * vec3(0.5, 0.6, 0.9);
    vec3 H = normalize(L + V);
    col += pow(max(dot(N, H), 0.0), 28.0) * (0.15 + p.b) * 0.9 * vec3(1.0, 0.98, 0.95);
    col += pow(1.0 - max(dot(N, V), 0.0), 3.0) * 0.18 * vec3(0.6, 0.75, 1.0);
    col += d.rgb * p.r * 2.0;
    col = vec3(1.0) - exp(-col * 1.45);   // soft tone curve: bright hulls keep their detail
    finalColor = vec4(col, 1.0);
}";

    void EnsureShader()
    {
        if (_shaderReady) return;
        _shader = Raylib.LoadShaderFromMemory(Vs, Fs);
        unsafe
        {
            _shader.Locs[(int)ShaderLocationIndex.MapAlbedo] = Raylib.GetShaderLocation(_shader, "texture0");
            _shader.Locs[(int)ShaderLocationIndex.MapMetalness] = Raylib.GetShaderLocation(_shader, "texture1");
            _shader.Locs[(int)ShaderLocationIndex.MapNormal] = Raylib.GetShaderLocation(_shader, "texture2");
            _shader.Locs[(int)ShaderLocationIndex.MatrixModel] = Raylib.GetShaderLocation(_shader, "matModel");
            _shader.Locs[(int)ShaderLocationIndex.MatrixNormal] = Raylib.GetShaderLocation(_shader, "matNormal");
        }
        _locTeam = Raylib.GetShaderLocation(_shader, "teamColor");
        _locView = Raylib.GetShaderLocation(_shader, "viewPos");
        _locLight = Raylib.GetShaderLocation(_shader, "lightDir");
        _locHasNormal = Raylib.GetShaderLocation(_shader, "hasNormal");
        _mat = Raylib.LoadMaterialDefault();
        _mat.Shader = _shader;
        _shaderReady = true;
    }

    // ---------------------------------------------------------------- baking

    RenderTexture2D? Bake(string culture, string ship, Color team)
    {
        var model = Model(culture, ship) ?? Model("mammalian_01", ship);
        if (model == null) return null;
        EnsureShader();
        // Bounds of the assembled ship.
        var lo = new Vector3(float.MaxValue); var hi = new Vector3(float.MinValue);
        foreach (var (_, part, at) in model) { lo = Vector3.Min(lo, part.Min + at); hi = Vector3.Max(hi, part.Max + at); }
        var centre = (lo + hi) / 2;
        // Seen from the side and above, a little toward the bow; the bow (-Z) points to screen right.
        var dir = Vector3.Normalize(new Vector3(1f, 0.62f, -0.28f));
        var up0 = Vector3.UnitY;
        var right = Vector3.Normalize(Vector3.Cross(-dir, up0));
        var up = Vector3.Cross(right, -dir);
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? lo.X : hi.X, (i & 2) == 0 ? lo.Y : hi.Y, (i & 4) == 0 ? lo.Z : hi.Z) - centre;
            float x = Vector3.Dot(corner, right), y = Vector3.Dot(corner, up);
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        var target = centre + right * (minX + maxX) / 2 + up * (minY + maxY) / 2;
        float aspect = SpriteW / (float)SpriteH;
        float viewH = Math.Max(maxY - minY, (maxX - minX) / aspect) * 1.06f;
        float dist = (hi - lo).Length() * 2 + 10;
        var cam = new Camera3D { Position = target + dir * dist, Target = target, Up = up, FovY = viewH, Projection = CameraProjection.Orthographic };

        var rt = Raylib.LoadRenderTexture(SpriteW, SpriteH);
        Raylib.SetTextureFilter(rt.Texture, TextureFilter.Bilinear);
        var tc = new Vector3(team.R / 255f, team.G / 255f, team.B / 255f);
        var light = Vector3.Normalize(dir * 0.6f + up * 0.9f - right * 0.5f);
        Raylib.SetShaderValue(_shader, _locTeam, tc, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_shader, _locView, cam.Position, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_shader, _locLight, light, ShaderUniformDataType.Vec3);
        Raylib.BeginTextureMode(rt);
        Raylib.ClearBackground(new Color(0, 0, 0, 0));
        Raylib.BeginMode3D(cam);
        foreach (var (mesh, part, at) in model)
        {
            var mat = _mat;
            var diff = Tex(culture, part.Diffuse) ?? Tex("mammalian_01", part.Diffuse);
            var spec = Tex(culture, part.Specular);
            var nrm = Tex(culture, part.Normal);
            unsafe
            {
                mat.Maps[(int)MaterialMapIndex.Albedo].Texture = diff ?? default;
                mat.Maps[(int)MaterialMapIndex.Metalness].Texture = spec ?? default;
                mat.Maps[(int)MaterialMapIndex.Normal].Texture = nrm ?? default;
            }
            Raylib.SetShaderValue(_shader, _locHasNormal, nrm != null && part.T != null ? 1 : 0, ShaderUniformDataType.Int);
            Raylib.DrawMesh(mesh, mat, Raymath.MatrixTranslate(at.X, at.Y, at.Z));
        }
        Raylib.EndMode3D();
        Raylib.EndTextureMode();
        return rt;
    }
}
