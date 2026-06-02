using Godot;

// Headless model-gen entry: ./run.sh modelgen runs scenes/tools/ModelGen.tscn, whose root is this node. Writes
// the Task-8 test glTF in _Ready, then quits (0 = OK, 1 = failed). The C# assembly must be built first — run.sh
// documents `build && modelgen`. Mirrors BakeRunner.
public partial class ModelGenRunner : Node
{
    public override void _Ready()
    {
        Error err = TestModelGen.Export();
        GetTree().Quit(err == Error.Ok ? 0 : 1);
    }
}
