using System.Text;

namespace GrandGalactic;

/// <summary>Minimal reader for Paradox (Clausewitz) script: key = value, key = { ... }, bare list values, # comments.</summary>
public sealed class CwNode
{
    public string? Key;
    public string? Value;
    public List<CwNode>? Children;

    public IEnumerable<CwNode> Kids => Children ?? Enumerable.Empty<CwNode>();
    public string? Get(string key) => Kids.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;

    public IEnumerable<CwNode> Descendants()
    {
        foreach (var c in Kids)
        {
            yield return c;
            foreach (var d in c.Descendants()) yield return d;
        }
    }

    public static CwNode Parse(string text)
    {
        var toks = Tokenize(text);
        int i = 0;
        var root = new CwNode { Children = new() };
        ParseBlock(toks, ref i, root);
        return root;
    }

    public static CwNode ParseFile(string path)
    {
        try { return Parse(File.ReadAllText(path, Encoding.UTF8)); }
        catch (Exception e) { Log.Info($"clausewitz: could not read {path} ({e.Message})"); return new CwNode { Children = new() }; }
    }

    static bool IsOp(string t) => t is "=" or "<" or ">" or "<=" or ">=" or "!=" or "?=";

    static void ParseBlock(List<string> t, ref int i, CwNode into)
    {
        while (i < t.Count)
        {
            var tok = t[i++];
            if (tok == "}") return;
            if (tok == "{")
            {
                var anon = new CwNode { Children = new() };
                ParseBlock(t, ref i, anon);
                into.Children!.Add(anon);
                continue;
            }
            if (i < t.Count && IsOp(t[i]))
            {
                i++;
                var node = new CwNode { Key = tok };
                if (i < t.Count && t[i] == "{")
                {
                    i++;
                    node.Children = new();
                    ParseBlock(t, ref i, node);
                }
                else if (i < t.Count) node.Value = t[i++];
                into.Children!.Add(node);
            }
            else into.Children!.Add(new CwNode { Value = tok });
        }
    }

    static List<string> Tokenize(string s)
    {
        var list = new List<string>();
        int i = 0, n = s.Length;
        var sb = new StringBuilder();
        while (i < n)
        {
            char c = s[i];
            if (c == '﻿' || char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '#') { while (i < n && s[i] != '\n') i++; continue; }
            if (c == '{' || c == '}') { list.Add(c.ToString()); i++; continue; }
            if (c is '=' or '<' or '>' or '!' or '?')
            {
                if (i + 1 < n && s[i + 1] == '=') { list.Add(s.Substring(i, 2)); i += 2; }
                else { list.Add(c.ToString()); i++; }
                continue;
            }
            if (c == '"')
            {
                i++;
                sb.Clear();
                while (i < n && s[i] != '"') { if (s[i] == '\\' && i + 1 < n) i++; sb.Append(s[i]); i++; }
                i++;
                list.Add(sb.ToString());
                continue;
            }
            sb.Clear();
            while (i < n && !char.IsWhiteSpace(s[i]) && s[i] is not ('{' or '}' or '=' or '<' or '>' or '#' or '"')) sb.Append(s[i++]);
            list.Add(sb.ToString());
        }
        return list;
    }
}
