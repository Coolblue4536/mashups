using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;

namespace GrandGalactic;

public sealed record UnityAssetRef(AssetsFileInstance File, AssetFileInfo Info, string Name, AssetClassID Type);

/// <summary>Reads textures, sprites, sounds and the font straight from the player's own Stacklands install (Unity assets).</summary>
public sealed class StacklandsData
{
    public readonly string Root;
    readonly string _data;
    readonly AssetsManager _am = new();
    public readonly List<UnityAssetRef> Textures = new(), Sprites = new(), Audio = new(), Fonts = new();
    public string UnityVersion = "?";

    public StacklandsData(string root)
    {
        Root = root;
        _data = Path.Combine(root, "Stacklands_Data");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var tpk = Path.Combine(AppContext.BaseDirectory, "classdata.tpk");
        _am.LoadClassPackage(tpk);
        bool cldb = false;
        foreach (var f in Directory.EnumerateFiles(_data, "*.assets").OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                     .Concat(Directory.EnumerateFiles(_data, "level*").Where(f => Path.GetExtension(f) == "")))
        {
            AssetsFileInstance inst;
            try { inst = _am.LoadAssetsFile(f, false); }
            catch (Exception e) { Log.Info($"stacklands: skip {Path.GetFileName(f)} ({e.Message})"); continue; }
            if (!cldb)
            {
                UnityVersion = inst.file.Metadata.UnityVersion;
                _am.LoadClassDatabaseFromPackage(UnityVersion);
                cldb = true;
            }
            foreach (var info in inst.file.AssetInfos)
            {
                var type = (AssetClassID)info.TypeId;
                var list = type switch
                {
                    AssetClassID.Texture2D => Textures,
                    AssetClassID.Sprite => Sprites,
                    AssetClassID.AudioClip => Audio,
                    AssetClassID.Font => Fonts,
                    _ => null,
                };
                if (list == null) continue;
                string name;
                try { name = AssetHelper.GetAssetNameFast(inst.file, _am.ClassDatabase, info); }
                catch { continue; }
                list.Add(new UnityAssetRef(inst, info, name, type));
            }
        }
        Log.Info($"stacklands: Unity {UnityVersion}; {Textures.Count} textures, {Sprites.Count} sprites, {Audio.Count} sounds, {Fonts.Count} fonts in {sw.ElapsedMilliseconds} ms");
    }

    static UnityAssetRef? FindIn(List<UnityAssetRef> list, string arg, bool exact)
    {
        var needle = arg.ToLowerInvariant();
        return list.Where(a => exact ? string.Equals(a.Name, arg, StringComparison.OrdinalIgnoreCase) : a.Name.ToLowerInvariant().Contains(needle))
            .OrderBy(a => a.Name.Length).ThenBy(a => a.Name, StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>tex:Name, texfind:part (sprites first, then textures).</summary>
    public (UnityAssetRef asset, Func<ImageData?> load)? LookupImage(string lookup)
    {
        int c = lookup.IndexOf(':');
        string kind = lookup[..c], arg = lookup[(c + 1)..];
        bool exact = kind == "tex";
        if (kind is not ("tex" or "texfind")) return null;
        if (FindIn(Sprites, arg, exact) is { } sp) return (sp, () => LoadSprite(sp));
        if (FindIn(Textures, arg, exact) is { } tx) return (tx, () => LoadTexture(tx.File, tx.Info));
        return null;
    }

    public (UnityAssetRef asset, Func<(byte[] data, string ext)?> load)? LookupAudio(string lookup)
    {
        int c = lookup.IndexOf(':');
        if (lookup[..c] != "audiofind") return null;
        return FindIn(Audio, lookup[(c + 1)..], false) is { } a ? (a, () => LoadAudio(a)) : null;
    }

    public (UnityAssetRef asset, Func<byte[]?> load)? LookupFont(string lookup)
    {
        int c = lookup.IndexOf(':');
        if (lookup[..c] != "fontfind") return null;
        var arg = lookup[(c + 1)..];
        var hit = arg.Length == 0 ? Fonts.FirstOrDefault(f => LoadFont(f) != null) : FindIn(Fonts, arg, false);
        return hit != null ? (hit, () => LoadFont(hit)) : null;
    }

    ImageData? LoadTexture(AssetsFileInstance inst, AssetFileInfo info)
    {
        try
        {
            var bf = _am.GetBaseField(inst, info);
            var tf = TextureFile.ReadTextureFile(bf);
            var raw = tf.FillPictureData(inst);
            if (raw == null || raw.Length == 0) return null;
            var bgra = tf.DecodeTextureRaw(raw, true);
            if (bgra == null) return null;
            return ImageData.FromBgraBottomUp(bgra, tf.m_Width, tf.m_Height);
        }
        catch (Exception e)
        {
            Log.Info($"stacklands: texture decode failed ({e.Message})");
            return null;
        }
    }

    ImageData? LoadSprite(UnityAssetRef sp)
    {
        try
        {
            var bf = _am.GetBaseField(sp.File, sp.Info);
            var texPtr = bf["m_RD"]["texture"];
            var ext = _am.GetExtAsset(sp.File, texPtr["m_FileID"].AsInt, texPtr["m_PathID"].AsLong, true);
            if (ext.info == null) return null;
            var tex = LoadTexture(ext.file, ext.info);
            if (tex == null) return null;
            var rect = bf["m_RD"]["textureRect"];
            float x = rect["x"].AsFloat, y = rect["y"].AsFloat, w = rect["width"].AsFloat, h = rect["height"].AsFloat;
            // Unity rects start bottom-left; ImageData is top-left.
            return tex.Crop((int)x, (int)(tex.H - y - h), (int)w, (int)h);
        }
        catch (Exception e)
        {
            Log.Info($"stacklands: sprite '{sp.Name}' failed ({e.Message})");
            return null;
        }
    }

    (byte[], string)? LoadAudio(UnityAssetRef a)
    {
        try
        {
            var bf = _am.GetBaseField(a.File, a.Info);
            var res = bf["m_Resource"];
            var src = res["m_Source"].AsString;
            long off = res["m_Offset"].AsLong, size = res["m_Size"].AsLong;
            if (string.IsNullOrEmpty(src) || size <= 0) return null;
            var path = Path.Combine(_data, Path.GetFileName(src.Replace("archive:/", "")));
            if (!File.Exists(path)) return null;
            var buf = new byte[size];
            using (var fs = File.OpenRead(path)) { fs.Seek(off, SeekOrigin.Begin); fs.ReadExactly(buf); }
            if (!Fmod5Sharp.FsbLoader.TryLoadFsbFromByteArray(buf, out var bank) || bank == null || bank.Samples.Count == 0) return null;
            if (!bank.Samples[0].RebuildAsStandardFileFormat(out var data, out var ext) || data == null) return null;
            return (data, ext ?? "ogg");
        }
        catch (Exception e)
        {
            Log.Info($"stacklands: audio '{a.Name}' failed ({e.Message})");
            return null;
        }
    }

    byte[]? LoadFont(UnityAssetRef f)
    {
        try
        {
            var bf = _am.GetBaseField(f.File, f.Info);
            var arr = bf["m_FontData"]["Array"];
            if (arr.IsDummy) return null;
            byte[] bytes = arr.Value?.ValueType == AssetValueType.ByteArray ? arr.AsByteArray : arr.Children.Select(c => (byte)c.AsInt).ToArray();
            return bytes.Length > 1000 ? bytes : null;
        }
        catch (Exception e)
        {
            Log.Info($"stacklands: font '{f.Name}' failed ({e.Message})");
            return null;
        }
    }
}
