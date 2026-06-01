using Godot;

// Resolves (seed, extent) for the bake from settings.cfg [world], using a FRESH ConfigFile so it works in
// BOTH the editor (WorldBakeTool) and headless (BakeRunner) — neither has the Config autoload running.
public static class BakeSettings
{
    public static (int seed, float extent) Resolve()
    {
        var cfg = new ConfigFile();
        int seed = 1337;            // defaults (match settings.cfg [world])
        float extent = 8000.0f;
        if (cfg.Load("res://settings/settings.cfg") == Error.Ok)
        {
            seed = (int)cfg.GetValue("world", "seed", seed);
            extent = (float)cfg.GetValue("world", "extent", extent);
        }
        return (seed, extent);
    }
}
