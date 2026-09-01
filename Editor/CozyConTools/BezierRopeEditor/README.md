# Bezier Rope Editor (Lilithe Tools)

## Quick start

1. Place `BezierRope.cs` and `BezierRopeEditorWindow.cs` under `Assets/Editor/CozyConTools/BezierRopeEditor/`.
2. Open Unity. Window → Lilithe → Tools → Bezier Rope Editor.
3. Create a new BezierRope GameObject or assign an existing one.

## Modes

- **Create Mode**: Click in the Scene view to add points. (Create mode: click in scene to add nodes.)
- **Edit Mode**: Click a point's sphere to select it, drag to move. Selected point shows two small control anchors (spheres) connected by debug lines; drag anchors to change tangents.

## Editing shortcuts

- **Insert point**: In Edit mode hold **Shift** and click on the curve to insert a point between segments.
- **Insert/Delete**: Use the buttons in the Editor window to insert after the selected point or delete the selected point.

## Leaves

- Assign a `Leaf Prefab` on the `BezierRope` component or let the system spawn two-sided quads.

## Notes

- The Editor uses `BezierRope` public helpers: `AddPoint`, `InsertPoint`, `RemovePoint`, `GetPointWorld`, `SetPointWorld`.
- If you want the mesh exported as an asset, I can add an export utility.
