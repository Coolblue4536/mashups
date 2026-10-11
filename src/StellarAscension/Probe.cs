namespace StellarAscension;

/// <summary>--probe &lt;dir&gt;: reads both installs and writes sample pictures and a report, to check the readers on a real PC.</summary>
public static class Probe
{
    public static int Run(string? civ6, string? stellaris, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var report = new List<string>();
        void Save(string name, ImageData? img)
        {
            if (img == null) { report.Add($"MISSING {name}"); return; }
            var p = Path.Combine(outDir, name + ".png");
            ImageWriter.SavePng(img, p);
            report.Add($"ok {name} {img.W}x{img.H}");
        }
        if (civ6 != null)
        {
            var c = new Civ6Data(civ6);
            var civs = c.Civilizations();
            report.Add($"civ6: {civs.Count} civilization/leader pairs");
            foreach (var v in civs) report.Add($"  {c.T(v.DescLoc)} / {c.T(v.LeaderNameLoc)} ({(v.Female ? "F" : "M")}) capital {c.T(v.CapitalLoc)}, {v.CityLocs.Count} city names, color {v.Primary}");
            report.Add($"civ6: {c.Techs().Count} techs");
            foreach (var n in new[] { "ICON_TECH_ROCKETRY", "ICON_TECH_ASTROLOGY", "ICON_BUILDING_PYRAMIDS", "ICON_BUILDING_EIFFEL_TOWER", "ICON_FEATURE_EVEREST",
                         "ICON_FEATURE_GALAPAGOS", "ICON_LEADER_TRAJAN", "ICON_CIVILIZATION_ROME", "ICON_PROJECT_LAUNCH_MOON_LANDING", "ICON_DISTRICT_SPACEPORT",
                         "ICON_BUILDING_LIBRARY", "ICON_UNIT_SETTLER", "ICON_YIELD_FOOD_5", "ICON_TERRAIN_GRASS" })
                Save(n, c.Icon(n));
            Save("leader_TRAJAN", c.LeaderPicture("LEADER_TRAJAN"));
            foreach (var n in new[] { "TER_Grass_B", "TER_Plains_B", "TER_Coast_B", "FOW_Ground_Grass_2k", "Features_Forest_Visible", "Cities_Classical_Visible",
                         "Districts_Campus_Visible", "Wonders_Pyramids_Visible", "SV_TerrainMountain_Grassland_Color_02", "SV_TerrainHexOcean_Color", "Improvements_Farm_Visible" })
                Save("map_" + n, c.MapTexture(n, 512));
            Save("leader_VICTORIA", c.LeaderPicture("LEADER_VICTORIA"));
            report.Add($"font MinionPro: {c.FontFile("MinionPro-Regular.otf") ?? "missing"}");
        }
        if (stellaris != null)
        {
            var s = new StellarisData(stellaris);
            foreach (var l in new[] { "planetclass:pc_continental", "planetclass:pc_gas_giant", "sprite:GFX_resource_energy", "sprite:GFX_resource_minerals" })
                if (s.Lookup(l) is { } hit) Save("st_" + l.Replace(':', '_'), ImageData.FromFile(hit.path, hit.frames, hit.frame)); else report.Add("MISSING " + l);
        }
        File.WriteAllLines(Path.Combine(outDir, "probe.txt"), report);
        foreach (var r in report) Log.Info(r);
        return report.Any(r => r.StartsWith("MISSING")) ? 1 : 0;
    }
}

public static class ImageWriter
{
    public static unsafe void SavePng(ImageData img, string path)
    {
        fixed (byte* p = img.Rgba)
        {
            var im = new Raylib_cs.Image { Data = p, Width = img.W, Height = img.H, Mipmaps = 1, Format = Raylib_cs.PixelFormat.UncompressedR8G8B8A8 };
            Raylib_cs.Raylib.ExportImage(im, path);
        }
    }
}
