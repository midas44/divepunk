using Godot;

// Headless bake entry: ./run.sh bake runs scenes/tools/Bake.tscn, whose root is this node. Bakes in _Ready,
// then quits (0 = OK, 1 = failed). The C# assembly must be built first — run.sh documents `build && bake`.
public partial class BakeRunner : Node
{
    public override void _Ready()
    {
        var (seed, extent) = BakeSettings.Resolve();
        Error err = WorldBaker.Bake(seed, extent);
        GetTree().Quit(err == Error.Ok ? 0 : 1);
    }
}
