# Pipe Modeling Tool

## Overview

The Pipe Modeling Tool is a Unity Editor utility for quickly laying out pipe paths directly in the Scene view.

It supports two workflows:

- Create Pipe mode for placing nodes by clicking in the scene.
- Edit Pipe mode for selecting nodes and adjusting node and material parameters.

Pipe runs are generated as straight segments, with bezier elbows only at valid angled corners.

## Location

- Script: Assets/Editor/CozyConTools/PipeModelingTool.cs
- Menu: Lilithe -> Tools -> Pipe Modeling Tool

## Features

- Scene-click node placement.
- Node visuals rendered as debug axes with a clickable sphere center.
- Automatic straight segment generation between node pairs.
- Bezier corner elbows for 45 and 90 degree-style turns.
- Per-node parameter editing directly in the same tool window.
- Scene handle movement for selected nodes.
- Manual node position editing via X/Y/Z fields (transform-style).
- Global pipe material plus per-node material overrides.
- Duplicate current draft into a new independent pipe draft.
- One-click controls to rebuild or clear generated geometry.
- Live scene rebuild when settings or node transforms change.

## Tool Modes

### Create Pipe Mode

Use this mode to build a pipe path by placing nodes.

1. Open the tool window from the menu.
2. Enable Create Pipe Mode.
3. Left-click in the Scene view to place the first node.
4. Continue clicking to add additional nodes.
5. Straight segments are generated between nodes, and corner elbows are generated where valid corner angles are detected.

Placement behavior:

- If a collider is hit by the click ray, the node is placed on the hit point.
- If no collider is hit, the click is projected onto a fallback ground plane.

### Edit Pipe Mode

Use this mode to tweak shape and node placement.

1. Enable Edit Pipe Mode.
2. Click a node handle in Scene view.
3. The selected node parameters appear in the same Pipe Modeling Tool window.
4. Adjust values and the pipe rebuilds automatically.

When a node is selected, its position can also be moved with a Scene position handle.

## Node Parameters

The selected node editor currently includes:

- Position: manual X/Y/Z world position entry.
- Corner Angle: `Auto`, `Degree45`, or `Degree90`.
- Corner Size: controls corner trim and elbow extent.
- Segment Material: material override for the node's outgoing straight segment.
- Corner Material: material override for the elbow at that node.

These values are applied when generating neighboring straight/corner pieces.

## Window Controls

- Create Pipe Mode: toggles click-to-add workflow.
- Edit Pipe Mode: toggles node selection and parameter editing.
- Pipe Radius: sets tube thickness for generated segments.
- Tube Radial Segments: controls roundness of the tube cross-section.
- Curve Path Segments: controls smoothness along each curve.
- Pipe Material: global material used unless node overrides are set.
- New Pipe: resets and starts a fresh draft.
- Duplicate Pipe: clones the current draft into a new `PipeDraft_*` object.
- Rebuild Geometry: regenerates segment meshes from current nodes/values.
- Delete Last Node: removes the most recently added node.
- Clear Pipe: removes all generated tool objects.
- Reset All Materials To Default: clears global and per-node material overrides.

## Generated Scene Structure

The tool creates and manages a root object named PipeDraft with child containers:

- Nodes: stores node marker objects.
- Segments: stores generated pipe mesh objects.

## Usage Example

Use the following quick workflow to create a simple S-shaped pipe:

1. Open Unity and load your target scene.
2. Open the tool from Lilithe -> Tools -> Pipe Modeling Tool.
3. Click New Pipe.
4. Enable Create Pipe Mode.
5. Click three or more points in Scene view to place nodes in an S pattern.
6. Disable Create Pipe Mode and enable Edit Pipe Mode.
7. Click the middle node to reveal node parameters in the same window.
8. Set Corner Angle to `Degree90` or `Degree45` and adjust Corner Size.
9. Optionally edit Position fields directly or move the selected node with the Scene position handle.
10. Set a global Pipe Material, then override Segment Material or Corner Material on selected nodes if needed.
11. Tune Pipe Radius, Tube Radial Segments, and Curve Path Segments for your target look and performance.

Expected result:

- Straight pipe runs with clean corner elbows.
- Stable seam orientation between connected pieces (reduced twist artifacts).
- Rounder profile with higher Tube Radial Segments.

## Changelog

### v1.0.0 - 2026-07-03

- Added Pipe Modeling Tool editor window.
- Added Create Pipe mode with Scene view click-to-place node workflow.
- Added Edit Pipe mode with selectable node handles.
- Added Node Parameters dialog for Curve Angle and Handle Scale.
- Added bezier tube mesh generation between node pairs.
- Added global controls for Pipe Radius, Tube Radial Segments, and Curve Path Segments.
- Added utility actions: New Pipe, Rebuild Geometry, Delete Last Node, and Clear Pipe.

### v1.1.0 - 2026-07-03

- Changed node visualization to debug axes plus clickable sphere centers.
- Changed editing workflow from popup dialog to inline selected-node tools in the main window.
- Changed generation to straight segments with corner-only bezier elbows.
- Added corner angle mode (`Auto`, `Degree45`, `Degree90`) and corner size controls.
- Added manual node position editing (Vector3 X/Y/Z entry).
- Added live rebuild updates for node and global setting changes.
- Fixed outward triangle winding and improved seam orientation continuity to reduce twist/pinch artifacts.
- Added global pipe material and per-node segment/corner material overrides.
- Added reset action to clear all material overrides.
- Added Duplicate Pipe to clone current draft into a new independent `PipeDraft_*` root.

## Validation

- Script compile status was checked via diagnostics.
- No compile errors were reported for the tool script.

## Notes

- The current implementation uses generated runtime mesh objects in the scene.
- If no global or per-node material is assigned, a default lit material is used.
- Corner pieces are only generated for supported angled junctions.
