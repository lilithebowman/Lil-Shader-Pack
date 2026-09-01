# Lil-Shader-Pack

Lilithe's custom Unity shader pack and editor toolkit for materials, map generation, scene tooling, and utility workflows.

This repository is not a single installed package; it is a collection of Unity asset files, shader source, and editor scripts that can be imported into a Unity project and used as needed.

## What is included

### Shaders

- `Lil Standard.shader` — a simple custom surface shader with albedo, normal map support, triplanar mapping, and alpha modes.
- `ORME-Standard-Shader.shader` — a more advanced physically based shader built for ORME-style packed textures and parallax features.
- `ORME-Standard-Shader-Guide.md` — documentation for the ORME shader, including property meanings and recommended values.
- `UnderwaterStereoBlur/UnderwaterBlur.shader` and its controller script — a fullscreen underwater blur effect for stereo or camera-based post-processing use.

### Editor tools

The `Editor/` folder contains utility scripts for managing materials and assets in Unity, including:

- material generation and material inspection tools
- mesh and atlas utilities
- duplicate material cleanup
- OBJ export
- mesh combine helpers
- texture/UV utilities
- scene and performance analysis tools
- various custom asset-generation windows

### CozyConTools

The `CozyConTools/` folder contains scene-building and geometry-generation utilities, including:

- Bezier path and rope generation
- mesh surface patch generation
- bridge and billboard generation helpers
- building/scene modeling aids
- model and asset management scripts

### Unity packages included

The repository also includes two `.unitypackage` files for convenience:

- `LocalFPSOverlay.unitypackage`
- `MosaicTilePainter.unitypackage`

These are importable Unity packages for specific tools or scene enhancements and are generally intended to be imported through Unity's standard package import flow.

## Recommended Unity usage

### 1. Import the project assets into Unity

- Create a Unity project or open an existing one.
- Copy the repository contents into your Unity project's `Assets` folder, or import the relevant Unity package files directly.
- Keep the folder structure intact so shader and editor script references continue to resolve correctly.

### 2. Use the shader materials

Open a Material in Unity, then choose one of the custom shaders:

- `Lilithe/Lil Standard`
- `Lilithe/ORME-Standard-Shader`

You can assign textures on the material inspector:

#### For `Lilithe/Lil Standard`

- `MainTex` — albedo/base color map
- `BumpMap` — normal map
- `Color` — tint color
- `Glossiness` — smoothness
- `Metallic` — metallic value
- `Enable Normal Map` / `Enable Triplanar Mapping` — optional surface features
- Blend mode controls allow Opaque, Cutout, Transparent, or Fade behavior

#### For `Lilithe/ORME-Standard-Shader`

- `MainTex` — albedo color map
- `Normal Map` — tangent-space normal map
- `Height Map` — parallax/height map
- `ORME` map — packed map where:
  - `R` = Occlusion
  - `G` = Roughness
  - `B` = Metallic
  - `A` = Emission mask
- `Render Mode` — sets material transparency behavior
- `Use Height Map`, `Use SPOM`, `Use ORME`, and `Use Triplanar` — optional advanced features

The guide in `Shaders/ORME-Standard-Shader-Guide.md` contains more detail on these properties and recommended starting values.

### 3. Configure render modes and alpha

The custom shaders expose blend and cutout controls, usually through a render mode property and alpha/cutoff values. Typical settings:

- Opaque: for solid surfaces
- Cutout: for foliage, decals, or masked materials
- Fade/Transparent: for glass or soft alpha effects

### 4. Use the editor utilities

Many scripts in `Editor/` are editor-only tools. They often appear as custom Unity menu items or windows in the Editor UI. Common uses include:

- generating ORME maps from source textures
- cleaning duplicate materials
- exporting meshes to OBJ
- combining meshes or building atlases
- setting up scene data and material workflows

### 5. Optional post-processing effect

The underwater blur shader is meant to be used with its controller script in a Unity scene, usually attached to a camera or used in a custom post-processing pipeline. It is not required for standard material setup.

## Notes and expectations

- This repository is aimed at custom content creation and workflow automation in Unity rather than a general-purpose public package.
- Some files are editor-only and will not affect gameplay unless used in the editor or a scene setup.
- Shader behavior and advanced options may be tuned per asset and project, especially when using parallax, triplanar mapping, atlas UVs, and mobile/VR hardware.

## Documentation

For the ORME shader, see:

- `Shaders/ORME-Standard-Shader-Guide.md`

For the included custom tools, inspect the files under `Editor/` and `CozyConTools/` to understand each tool's intended workflow.

## License

This project is provided under the terms of the included LICENSE file.
