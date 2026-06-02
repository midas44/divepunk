using Godot;
using System.Collections.Generic;

// Builds a tile's render batches + colliders from its bucketed PlacedObjects. The MultiMesh upload is
// harvested from CityChunk (UseColors/UseCustomData set BEFORE InstanceCount; per-instance transform/color/
// custom). Instance transforms are TILE-LOCAL (origin - tileCenter) so the tile node can sit at the tile
// centre for per-tile VisibilityRange culling while MODEL_MATRIX still yields the true WORLD position the
// building shader's world-space window grid needs (TASK04 §4).
public static class TileBuilder
{
    public static List<MultiMeshInstance3D> BuildMultiMeshes(List<PlacedObject> objects, Vector3 tileCenter, float viewEnd, float fadeMargin)
    {
        // Bucket by ObjectType -> one MultiMesh per type present in this tile.
        var byType = new Dictionary<int, List<PlacedObject>>();
        foreach (PlacedObject o in objects)
        {
            int t = (int)o.Type;
            if (!byType.TryGetValue(t, out List<PlacedObject> list)) { list = new(); byType[t] = list; }
            list.Add(o);
        }

        var result = new List<MultiMeshInstance3D>();
        foreach (KeyValuePair<int, List<PlacedObject>> kv in byType)
        {
            List<PlacedObject> list = kv.Value;
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,           // MUST precede InstanceCount; feeds the shader COLOR
                UseCustomData = true,       // ...and INSTANCE_CUSTOM (the window lighting profile)
                Mesh = MeshRegistry.GetMesh((ObjectType)kv.Key),
            };
            mm.InstanceCount = list.Count;
            for (int i = 0; i < list.Count; i++)
            {
                PlacedObject o = list[i];
                mm.SetInstanceTransform(i, new Transform3D(o.Xform.Basis, o.Xform.Origin - tileCenter));
                mm.SetInstanceColor(i, o.Tint);            // rgb = white window base, a = lit fraction
                mm.SetInstanceCustomData(i, o.Custom);     // variation, grid class, accent hue, accent amount
            }
            result.Add(new MultiMeshInstance3D
            {
                Name = $"Batch_{(ObjectType)kv.Key}",
                Multimesh = mm,
                VisibilityRangeEnd = viewEnd,
                VisibilityRangeEndMargin = fadeMargin,
                VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,  // dithered fade into fog
            });
        }
        return result;
    }

    // One axis-aligned box StaticBody3D per building. A box approximates round/taper/shard (over-covers; fine
    // for arcade bounce). DEFAULT physics layer (1, "ship") — exactly like the Task-2 test field — so the car
    // (mask 1) bounces and the existing impulse->damage path fires with NO Ship change. Added to the "building"
    // group for future contact attribution. Transforms are tile-local (the tile node is at tileCenter). The
    // box SIZE comes from the basis scale; the body carries only the yaw rotation (no scaled collision shape).
    public static List<StaticBody3D> BuildColliders(List<PlacedObject> objects, Vector3 tileCenter, PhysicsMaterial mat)
    {
        var bodies = new List<StaticBody3D>(objects.Count);
        foreach (PlacedObject o in objects)
        {
            var body = new StaticBody3D { PhysicsMaterialOverride = mat };
            body.AddToGroup("building");
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = o.Xform.Basis.Scale } });
            body.Transform = new Transform3D(o.Xform.Basis.Orthonormalized(), o.Xform.Origin - tileCenter);
            bodies.Add(body);
        }
        return bodies;
    }
}
