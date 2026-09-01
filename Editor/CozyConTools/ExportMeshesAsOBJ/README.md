# Export Selected as Wavefront OBJ Model

## Location
- Menu: Lilithe/Tools/Export Selected as Wavefront OBJ Model
- Script: Assets/Editor/CozyConTools/ExportMeshesAsOBJ/ExportSelectedAsWaveformObjModelWindow.cs

## Purpose
Exports selected hierarchy objects from Unity to a Wavefront OBJ and MTL pair so they can be imported into Blender and edited outside Unity.

## Workflow
1. Select one or more root GameObjects in the Hierarchy.
2. Open Lilithe/Tools/Export Selected as Waveform OBJ Model.
3. Click Export.
4. Choose a save location in the file dialog.
5. The tool writes:
   - model_name.obj
   - model_name.mtl

## What Gets Exported
- MeshFilter + MeshRenderer objects from selected roots and children.
- SkinnedMeshRenderer objects are baked and exported.
- Submesh-to-material assignments are preserved via usemtl groups.
- Vertex positions, normals, and UVs are exported.

## Export Options
- Include Inactive Children: include disabled children in export.
- Convert To Blender Axes: flips handedness for easier Blender OBJ import orientation.
- Copy Albedo Textures: copies detected main textures into export folder and references them in MTL as map_Kd.

## Notes
- Material scalar values are exported as basic MTL values (Ka, Kd, Ks, d, illum).
- If no material exists for a submesh, Default_Material is used in the MTL.
- Texture references are best-effort and depend on texture assets existing as files on disk.
