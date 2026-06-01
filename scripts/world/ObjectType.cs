using Godot;

// What a PlacedObject is. Persisted as an int in the bake, so values are STABLE: APPEND new types, never
// reorder or insert (or bump WorldData.FormatVersion). Values 0..5 mirror CityChunk's `Silhouette` enum and
// GetBuildingMeshes() index order, so the Task-4 MeshRegistry can reuse those mesh factories 1:1. All six
// building types share shaders/building.gdshader and are UNIT meshes (fit 1x1x1) — the PlacedObject.Xform
// basis scale sets the real footprint/height.
//
// NOTE: a plain C# enum (no [GlobalClass] — that attribute targets `class` only and would not compile on an
// enum). It still serialises as an int inside an [Export] property; that is all the bake needs.
public enum ObjectType
{
    BuildingBox    = 0,
    BuildingRound  = 1,
    BuildingPrism  = 2,
    BuildingTaper  = 3,
    BuildingShard  = 4,
    BuildingSphere = 5,
    // Rooftop dressing + props (Antenna, Beacon, Spire, Dome, Pyramid, HexCap, LandingPad, ParkedCar) and
    // glTF set-pieces are a LATER enrichment — append here when added.
}
