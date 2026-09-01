# Plant Foliage Tool

## Location
- Menu path: **Lilithe/Tools/Plant Foliage**
- Script path: `Assets/Editor/CozyConTools/PlantFoliage/PlantFoliageWindow.cs`

## What It Does
Plant Foliage scatters prefab instances on the mesh surface of a selected target GameObject.

Placed instances are created under a root-level scene object named `Planted Foliage` (customizable in the tool).

## Setup
1. Open Unity and select **Lilithe/Tools/Plant Foliage**.
2. Assign a **Target GameObject** (must contain MeshFilter components on itself or children).
3. Add one or more prefabs in **Foliage Prefabs**.

## Placement Modes
- **Density**: Uses `objects per square meter` and computes count from total mesh area.
- **FixedCount**: Uses an explicit object count.

## Placement Options
- **Parent Name**: Root-level scene parent used for all spawned foliage.
- **Clear Existing Children**: Deletes existing children under the parent before planting.
- **Align To Surface Normal**: Aligns instances to surface normals.
- **Prefab Upright Axis**: Which local axis of the prefab should point perpendicular from the surface.
- **Only Upward Facing Surfaces**: Ignores underside/downward-facing triangles.
- **Min Upward Dot**: Controls how strict the upward filter is (higher = flatter/upward only).
- **Surface Offset**: Pushes spawned objects away from the surface normal.
- **Random Yaw**: Adds random rotation around local up.
- **Min Scale / Max Scale**: Random uniform scale range per instance.
- **Manual Brush Radius**: Radius used by the Scene view brush when manual planting is active.
- **Manual Plant Density**: Number of instances dispersed per manual click.
- **Random Seed**: Deterministic random sequence.
- **Batch Size Per Update**: Number of objects processed each editor update tick.

## Manual Planting Mode
Manual mode lets you place foliage interactively in Scene view with a brush preview.

1. Click **Plant Manually** in the Plant Foliage window.
2. Move the mouse over the target mesh to see a highlighted circular brush region on the surface.
3. Left-click and release once to apply one planting pass in that region.
4. Click again for additional passes.

Behavior notes:
- Planting occurs once per click-release cycle (`MouseDown` then `MouseUp`).
- Each pass places up to **Manual Plant Density** instances, sampled from triangles inside the brush area.
- Use **Stop Manual Planting** to exit manual mode.

## Staged Processing
Planting runs in stages so Unity stays responsive:
1. Optional clearing of existing planted children.
2. Batched spawning of new instances.
3. Finalization and Undo grouping.

The tool displays stage status in the window and a cancelable progress bar.

## Buttons
- **Plant Foliage**: Performs placement using current settings.
- **Plant Manually** / **Stop Manual Planting**: Toggles click-based Scene view planting with brush preview.
- **Cancel Planting**: Stops an in-progress run and keeps already created objects.
- **Clear Planted Foliage**: Removes existing planted children under the configured parent.

## Notes
- Tool supports Undo for creation/deletion in editor.
- Works with MeshFilter meshes (not SkinnedMeshRenderer sampling).
