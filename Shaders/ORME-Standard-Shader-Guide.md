# ORME Standard Shader Guide

This document explains how to use `Lilithe/ORME-Standard-Shader` in Unity projects.

## What This Shader Does

`Lilithe/ORME-Standard-Shader` is a Standard-surface shader that supports:

- Albedo tinting
- Normal mapping
- Height-based parallax (POM)
- Silhouette POM (SPOM)
- ORME packed maps:
  - `R`: Occlusion
  - `G`: Roughness (converted to Smoothness)
  - `B`: Metallic
  - `A`: Emission mask
- Optional triplanar mapping
- Atlas-safe UV controls for parallax sampling

## Quick Start

1. Create or select a Material.
2. Set shader to `Lilithe/ORME-Standard-Shader`.
3. Assign textures:
   - `MainTex`: albedo
   - `BumpMap`: normal map
   - `ParallaxMap`: height map
   - `ORMEMap`: packed ORME map
4. Keep `Use ORME` enabled unless you want fallback scalar values.
5. For transparency/cutout, set `Render Mode` and tune `Alpha` / `Cutoff`.

## Texture Packing Reference

### ORME map

- Red (`R`) -> Occlusion
- Green (`G`) -> Roughness
- Blue (`B`) -> Metallic
- Alpha (`A`) -> Emission mask

Note: smoothness is computed as `1 - roughness`.

## Core Controls

### Surface and rendering

- `Render Mode`: Opaque, Cutout, Fade, Transparent
- `Culling`: Back, Front, None
- `Color`: albedo tint
- `Alpha`, `Cutoff`: transparency and alpha clipping

### Normal mapping

- `Use Normal Map`
- `Normal Map`
- `Normal Strength`

### Height and parallax

- `Use Height Map`
- `Height Map`
- `Invert Height Map`
- `Height Sample Rect (MinX, MinY, MaxX, MaxY)`
- `Height Strength`
- `POM Min Layers`, `POM Max Layers`

### SPOM and silhouette

- `Use SPOM`
- `SPOM UV Silhouette Clipping`
- `SPOM Curved Silhouette`
- `SPOM Horizon Safe Threshold`
- `SPOM Horizon Falloff Power`
- `SPOM Horizon Clip Strength`
- `SPOM Horizon Height Bias`
- `POM Smooth Kernel Radius`
- `POM UV Boundary Fade Width`
- `Grazing Fade Threshold`

### ORME response

- `Use ORME`
- `Occlusion Strength`
- `Smoothness`
- `Metallic`
- `Emission Color`

### Triplanar

- `Use Triplanar Mapping`
- `Triplanar Scale`
- `Triplanar Blend Sharpness`

## Recommended Starting Values

Use these as a baseline and adjust per asset:

- `Height Strength`: `0.01 - 0.03`
- `POM Min Layers`: `8 - 12`
- `POM Max Layers`: `20 - 32`
- `POM Smooth Kernel Radius`: `0.002 - 0.006`
- `POM UV Boundary Fade Width`: `0.03 - 0.08`
- `Grazing Fade Threshold`: `0.10 - 0.20`

## Atlas and UV Safety

If your model uses texture atlases or UV islands close to each other:

1. Set `Height Sample Rect` to the island bounds.
2. Increase `POM UV Boundary Fade Width` until boundary artifacts disappear.
3. Keep `SPOM UV Silhouette Clipping` enabled when needed.

This prevents parallax rays from causing visible edge artifacts near UV boundaries.

## Performance Guidance

- Most expensive features are POM/SPOM and high layer counts.
- For lower-end targets, reduce:
  - `Height Strength`
  - `POM Max Layers`
  - SPOM usage
- Triplanar mapping can be expensive due to multi-axis sampling.
- For mobile/VR, test quality settings directly on target hardware.

## Troubleshooting

### Grainy or dotted appearance in parallax

- Increase `POM Smooth Kernel Radius` slightly.
- Reduce `Height Strength`.
- Lower `POM Max Layers` if shimmering appears.

### Strong artifacts at grazing angles

- Increase `Grazing Fade Threshold`.
- Reduce `Height Strength`.

### Parallax leaks near atlas edges

- Increase `POM UV Boundary Fade Width`.
- Verify `Height Sample Rect` is correct for that UV island.

### Silhouette clipping too aggressive

- Lower `SPOM Horizon Clip Strength`.
- Increase `SPOM Horizon Safe Threshold`.
- Reduce `SPOM Horizon Height Bias` magnitude.

## Optional Fullscreen Smoothing

A separate fullscreen blur can smooth final frame-level noise. If included in your project, attach the effect script to a camera and tune blur radius conservatively to avoid over-softening details.

## Notes

- Emission uses ORME alpha as mask.
- Emission color primarily controls intensity and tint.
- When `Use ORME` is disabled, scalar metallic/smoothness/occlusion behavior is used.
