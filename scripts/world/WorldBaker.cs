using Godot;

// Bakes the world to disk and prints a validation summary. Shared by the editor tool (WorldBakeTool) and the
// headless runner (BakeRunner) so there is ONE bake path. Saving DATA needs no GPU, so this is headless-safe.
public static class WorldBaker
{
    public const string OutPath = "res://world/world_main.res";

    public static Error Bake(int seed, float extent, string path = OutPath)
    {
        // Ensure res://world/ exists (the folder is not created automatically).
        if (!DirAccess.DirExistsAbsolute("res://world"))
            DirAccess.MakeDirRecursiveAbsolute("res://world");

        WorldData data = WorldGenerator.Build(seed, extent);
        Error err = ResourceSaver.Save(data, path, ResourceSaver.SaverFlags.Compress);
        PrintSummary(data, path, err);
        return err;
    }

    // The "validator": a loud, sane-count readout. Reloads from disk with CacheMode.Ignore so it reflects the
    // SAVED bytes (not the in-memory instance) — if the save round-trip ever broke, this would catch it.
    public static void PrintSummary(WorldData inMemory, string path, Error saveErr)
    {
        const string rule = "================================================================";
        GD.Print(rule);
        GD.Print(":: DIVEPUNK world bake -- validation summary");
        GD.Print($"   save: {(saveErr == Error.Ok ? "OK" : saveErr.ToString())}   path: {path}");
        if (saveErr != Error.Ok) { GD.Print(rule); return; }

        WorldData data = ResourceLoader.Load<WorldData>(path, cacheMode: ResourceLoader.CacheMode.Ignore);
        if (data == null)
        {
            GD.PrintErr(":: reload from disk FAILED -- summarising the in-memory data instead.");
            data = inMemory;
        }

        GD.Print($"   seed={data.Seed}  extent={data.WorldExtent}  FormatVersion={data.FormatVersion}");

        // ── Heightmap ──
        int n = data.HeightmapResolution;
        float[] h = data.Heights;
        double mean = 0.0;
        for (int i = 0; i < h.Length; i++) mean += h[i];
        mean = h.Length > 0 ? mean / h.Length : 0.0;
        GD.Print($"   heightmap: {n}x{n} ({h.Length} cells)  MinY={data.MinY:F1}  MaxY={data.MaxY:F1}  meanNorm={mean:F3}");
        GD.Print($"     sanity MinY<0<MaxY: {(data.MinY < 0.0f && 0.0f < data.MaxY ? "PASS" : "FAIL")}");

        // ── Biome histogram ──
        var bcount = new int[6];
        byte[] b = data.Biomes;
        for (int i = 0; i < b.Length; i++) { int v = b[i]; if (v < bcount.Length) bcount[v]++; }
        GD.Print($"   biomes: Ocean={bcount[0]} Beach={bcount[1]} City={bcount[2]} Desert={bcount[3]} Hills={bcount[4]} Mountains={bcount[5]}");
        bool biomeOk = bcount[0] > 0 && bcount[1] > 0 && bcount[2] > 0 && (bcount[3] > 0 || bcount[4] > 0 || bcount[5] > 0);
        GD.Print($"     sanity Ocean+Beach+City + >=1 of Desert/Hills/Mountains all >0: {(biomeOk ? "PASS" : "FAIL")}");

        // ── Object histogram (per ObjectType) ──
        var ocount = new int[6];
        foreach (PlacedObject o in data.Objects) { int v = (int)o.Type; if (v < ocount.Length) ocount[v]++; }
        GD.Print($"   objects: total={data.Objects.Count}");
        GD.Print($"     by type: Box={ocount[0]} Round={ocount[1]} Prism={ocount[2]} Taper={ocount[3]} Shard={ocount[4]} Sphere={ocount[5]}  (Sphere=0 expected)");

        // ── File size ──
        long size = 0;
        using (var f = FileAccess.Open(path, FileAccess.ModeFlags.Read))
            if (f != null) size = (long)f.GetLength();
        GD.Print($"   file size: {size} bytes ({size / 1024.0:F1} KiB)");
        GD.Print(rule);
    }
}
