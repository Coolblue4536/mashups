using System.Numerics;
using System.Text;

namespace StellarAscension;

/// <summary>Reads Stellaris's binary .mesh files ("@@b@"): a tree of objects ("[" repeated by depth, then a name) holding
/// typed properties ("!", name, 'i'/'f'/'s', count, values). Each shape's mesh has positions (p), normals (n), UVs (u0),
/// triangles (tri) and a material naming its shader and textures; locators mark attachment points.</summary>
public sealed class PdxMesh
{
    public sealed class Part
    {
        public required float[] P, N, U;
        public float[]? T;
        public required int[] Tri;
        public string Shader = "", Diffuse = "", Normal = "", Specular = "";
        public Vector3 Min, Max;
    }

    public readonly List<Part> Parts = new();
    public readonly Dictionary<string, Vector3> Locators = new(StringComparer.OrdinalIgnoreCase);

    sealed class Node
    {
        public string Name = "";
        public int Depth;
        public readonly Dictionary<string, object> Props = new();
        public readonly List<Node> Kids = new();
        public Node? Kid(string name) => Kids.FirstOrDefault(k => k.Name == name);
        public float[]? F(string k) => Props.TryGetValue(k, out var v) ? v as float[] : null;
        public int[]? I(string k) => Props.TryGetValue(k, out var v) ? v as int[] : null;
        public string S(string k) => Props.TryGetValue(k, out var v) && v is string[] s && s.Length > 0 ? s[0] : "";
    }

    public static PdxMesh? Load(string path)
    {
        try { return Parse(File.ReadAllBytes(path)); }
        catch (Exception e) { Log.Info($"mesh: could not read {path} ({e.Message})"); return null; }
    }

    static PdxMesh? Parse(byte[] d)
    {
        if (d.Length < 4 || d[0] != '@' || d[1] != '@' || d[2] != 'b' || d[3] != '@') return null;
        var root = new Node();
        var stack = new List<Node> { root };
        int pos = 4;
        while (pos < d.Length)
        {
            if (d[pos] == '!')
            {
                int n = d[pos + 1];
                string name = Encoding.ASCII.GetString(d, pos + 2, n);
                char t = (char)d[pos + 2 + n];
                pos += 3 + n;
                int cnt = BitConverter.ToInt32(d, pos); pos += 4;
                object v;
                if (t == 'i') { var a = new int[cnt]; Buffer.BlockCopy(d, pos, a, 0, cnt * 4); pos += cnt * 4; v = a; }
                else if (t == 'f') { var a = new float[cnt]; Buffer.BlockCopy(d, pos, a, 0, cnt * 4); pos += cnt * 4; v = a; }
                else
                {
                    var a = new string[cnt];
                    for (int i = 0; i < cnt; i++)
                    {
                        int len = BitConverter.ToInt32(d, pos); pos += 4;
                        a[i] = Encoding.ASCII.GetString(d, pos, len).TrimEnd('\0'); pos += len;
                    }
                    v = a;
                }
                stack[^1].Props[name] = v;
            }
            else if (d[pos] == '[')
            {
                int depth = 0;
                while (d[pos] == '[') { depth++; pos++; }
                int end = Array.IndexOf(d, (byte)0, pos);
                var node = new Node { Name = Encoding.ASCII.GetString(d, pos, end - pos), Depth = depth };
                pos = end + 1;
                while (stack[^1].Depth >= depth && stack.Count > 1) stack.RemoveAt(stack.Count - 1);
                stack[^1].Kids.Add(node);
                stack.Add(node);
            }
            else throw new InvalidDataException($"unexpected byte {d[pos]} at {pos}");
        }
        var m = new PdxMesh();
        foreach (var shape in root.Kid("object")?.Kids ?? new())
            foreach (var mesh in shape.Kids.Where(k => k.Name == "mesh"))
            {
                var mat = mesh.Kid("material");
                string shader = mat?.S("shader") ?? "";
                if (shader == "Collision" || mesh.F("p") is not { } p || mesh.I("tri") is not { } tri) continue;
                var part = new Part
                {
                    P = p, N = mesh.F("n") ?? new float[p.Length], U = mesh.F("u0") ?? new float[p.Length / 3 * 2], Tri = tri,
                    T = mesh.F("ta"), Shader = shader, Diffuse = mat?.S("diff") ?? "", Normal = mat?.S("n") ?? "", Specular = mat?.S("spec") ?? "",
                };
                // Bounds from the vertices actually used (the stored aabb can include other pieces).
                var mn = new Vector3(float.MaxValue); var mx = new Vector3(float.MinValue);
                foreach (int i in tri) { var v = new Vector3(p[i * 3], p[i * 3 + 1], p[i * 3 + 2]); mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
                part.Min = mn; part.Max = mx;
                m.Parts.Add(part);
            }
        foreach (var loc in root.Kid("locator")?.Kids ?? new())
            if (loc.F("p") is { Length: 3 } lp) m.Locators[loc.Name] = new Vector3(lp[0], lp[1], lp[2]);
        return m;
    }

    public Vector3 Min => Parts.Count == 0 ? Vector3.Zero : Parts.Select(p => p.Min).Aggregate(Vector3.Min);
    public Vector3 Max => Parts.Count == 0 ? Vector3.Zero : Parts.Select(p => p.Max).Aggregate(Vector3.Max);
}
