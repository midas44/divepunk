using Godot;

// One object placed in the baked world: its type, where/how big it is, and the per-instance shader channels
// that drive shaders/building.gdshader VERBATIM (so visuals swap to glTF later with zero re-bake). The bake
// stores DESCRIPTORS, never meshes — runtime instantiates the mesh from Type via the MeshRegistry (Task 4).
//
// A Resource serialises only its [Export] members — a plain field/property is NOT saved. Every persisted
// field below is [Export].
[GlobalClass]
public partial class PlacedObject : Resource
{
    [Export] public ObjectType Type { get; set; } = ObjectType.BuildingBox;

    // Basis carries footprint/height as non-uniform scale (fx, height, fz); Origin is the world centre.
    [Export] public Transform3D Xform { get; set; } = Transform3D.Identity;

    // -> MultiMesh COLOR. rgb = white window base; a = lit fraction. (building.gdshader, lines 7-8.)
    [Export] public Color Tint { get; set; } = new Color(0.9f, 0.93f, 0.97f, 0.16f);

    // -> INSTANCE_CUSTOM = (variation, grid_class, accent_hue, accent_amount). (building.gdshader, line 9.)
    [Export] public Color Custom { get; set; } = new Color(0.3f, 0.5f, 0.5f, 0.05f);

    // Reserved for future use (e.g. collidable/landmark bits, per-object decorrelation). Unused in Task 3.
    [Export] public int Flags { get; set; } = 0;
    [Export] public int SubSeed { get; set; } = 0;
}
