using Godot;

// In-editor bake: open this script in the Godot Script editor and File -> Run (Ctrl+Shift+X). Reads the seed
// + extent from settings.cfg [world] (via a fresh ConfigFile — the Config autoload isn't running in editor).
[Tool]
public partial class WorldBakeTool : EditorScript
{
    public override void _Run()
    {
        var (seed, extent) = BakeSettings.Resolve();
        Error err = WorldBaker.Bake(seed, extent);
        GD.Print(err == Error.Ok ? ":: bake OK" : $":: bake FAILED ({err})");
    }
}
