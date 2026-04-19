# Underwater Stereo Blur Setup

This folder contains a stereo-compatible VRChat underwater blur effect:

- UnderwaterBlur.shader
- UnderwaterBlurController.cs

The effect works by rendering an inside-facing blur sphere around the local camera while underwater, then fading it in and out via UdonSharp.

## What You Need In Scene

1. A trigger volume that represents your underwater area.
2. A sphere mesh used as the blur bubble.
3. A material using the CozyCon/UnderwaterBlur shader.
4. The UnderwaterBlurController component on the trigger object.

## Step-by-Step Setup

1. Create blur material
- Create a new Material.
- Set Shader to CozyCon/UnderwaterBlur.
- Keep defaults initially.

2. Create blur sphere
- Create a Sphere GameObject (name suggestion: BlurSphere).
- Scale it so the player camera is comfortably inside (for example around 3 to 5 meters depending on your space).
- Assign the blur material to the sphere Mesh Renderer.
- Do not add a collider to this sphere.

3. Create underwater trigger zone
- Create an empty GameObject (name suggestion: UnderwaterZone).
- Add a BoxCollider or SphereCollider.
- Enable Is Trigger.
- Size this collider to match the underwater region.

4. Add controller script
- Add UnderwaterBlurController to UnderwaterZone.
- Drag BlurSphere Mesh Renderer into the blurSphereRenderer field.
- Start with these values:
  - fadeInSpeed: 3.0
  - fadeOutSpeed: 2.0
  - maxEffectBlend: 1.0

5. Test in VRChat ClientSim or play mode
- Enter the trigger volume: blur should fade in.
- Exit the trigger volume: blur should fade out and renderer should disable after fade completes.

## Recommended Hierarchy

- UnderwaterZone (Collider Is Trigger + UnderwaterBlurController)
- BlurSphere (MeshRenderer with UnderwaterBlur material)

BlurSphere can be a child of UnderwaterZone or elsewhere, as long as the Renderer reference is assigned.

## Prefab Setup Checklist

Use this as a quick verification list for a ready-to-drop setup.

1. Create object names
- UnderwaterZone
- BlurSphere

2. Configure UnderwaterZone
- Add BoxCollider or SphereCollider.
- Enable Is Trigger.
- Add UnderwaterBlurController.
- In UnderwaterBlurController, set blurSphereRenderer to BlurSphere -> MeshRenderer.
- Keep defaults unless needed:
  - fadeInSpeed: 3.0
  - fadeOutSpeed: 2.0
  - maxEffectBlend: 1.0

3. Configure BlurSphere
- Use a Sphere mesh.
- Assign a material using shader CozyCon/UnderwaterBlur.
- Remove/avoid colliders on BlurSphere.
- Scale so local camera stays inside while underwater.

4. Configure blur material
- Shader: CozyCon/UnderwaterBlur
- Starting values:
  - Blur Radius: 0.010
  - Water Tint Color: (0.06, 0.28, 0.55, 0.50)
  - Fog Blend Strength: 0.25
  - Distortion Strength: 0.004
  - Wave Animation Speed: 0.80
  - Wave Scale: 6.0
  - Chromatic Shift: 0.003
  - Edge Blur Boost: 1.5

5. Runtime checks
- On scene start: BlurSphere renderer should be disabled.
- On entering UnderwaterZone: blur fades in.
- On leaving UnderwaterZone: blur fades out, then renderer disables.

## Shader Tuning Guide

Use the blur material properties to shape the look:

- Blur Radius: core blur amount.
- Water Tint Color: underwater color tint.
- Fog Blend Strength: how strongly tint overlays the blurred scene.
- Distortion Strength: refraction wobble intensity.
- Wave Animation Speed: motion speed of distortion.
- Wave Scale: size/frequency of wave pattern.
- Chromatic Shift: subtle red/blue separation.
- Edge Blur Boost: extra blur toward screen edges.
- Effect Blend: runtime fade value controlled by UnderwaterBlurController.

## Performance Notes

- The renderer is disabled by script when fully faded out to avoid unnecessary draw cost.
- Keep Blur Radius and Distortion Strength moderate, especially for Quest.
- Use only one active blur sphere per local player view for best results.

## Troubleshooting

Blur never appears:
- Confirm UnderwaterZone collider has Is Trigger enabled.
- Confirm UnderwaterBlurController is on the same GameObject as that trigger collider.
- Confirm blurSphereRenderer is assigned.
- Confirm BlurSphere material uses CozyCon/UnderwaterBlur.

Blur always visible:
- Confirm the controller starts with Effect Blend at zero (script does this in Start).
- Check for another script changing the material effect blend.

No fade, only pop in/out:
- Increase fadeInSpeed and fadeOutSpeed gradually.
- Verify Update is running and there are no Udon compile/runtime errors.

Stereo mismatch issues:
- Keep this shader as-is for stereo macros and screenspace texture sampling.
- Avoid replacing screenspace sampling macros with plain tex2D patterns.
# Underwater Blur Setup

This folder includes a local-player stereo blur system for VRChat.

- Shader: BlurryUnderWater.compute
- Driver script: Assets/UnderwaterStereoBlur/UnderwaterStereoBlur.cs
- Trigger helper script: Assets/UnderwaterStereoBlur/PlayerViewBlurZone.cs

## What This System Does

When blur is enabled for the local player:

1. A camera renders to captureTexture.
2. BlurryUnderWater.compute applies a 5x5 Gaussian blur.
3. The blurred result is written to stereoTexture.
4. A renderer in front of the local player head displays stereoTexture.

When blur is disabled, the overlay and source camera are disabled.

## Required Scene Objects

1. A Camera for view capture.
2. A RenderTexture for raw capture (captureTexture).
3. A RenderTexture for blurred output (stereoTexture).
4. A Renderer (usually a quad or plane) that will display stereoTexture in front of the player.
5. An object with UnderwaterStereoBlur attached.
6. Optional: a trigger object with PlayerViewBlurZone attached.

## RenderTexture Requirements

The output RenderTexture (stereoTexture) must:

1. Use dimensions appropriate for your quality target.
2. Have Random Write enabled.
3. Be assigned to UnderwaterStereoBlur stereoTexture.

If Random Write is disabled, compute blur dispatch is skipped and output is unblurred.

## UnderwaterStereoBlur Inspector Setup

On the object with UnderwaterStereoBlur:

1. Assign sourceCamera.
2. Assign blurShader to BlurryUnderWater.compute.
3. Assign captureTexture.
4. Assign stereoTexture.
5. Assign stereoOverlayRenderer.
6. Tune overlayDistance and overlaySize.
7. Tune updateInterval for performance.

If blurShader is left empty, the script still works but does not run compute blur.

Recommended starting values:

- overlayDistance: 0.32
- overlaySize: 0.70 x 0.40
- updateInterval: 1 to 2

## Material Setup For Overlay Renderer

Use a material on the overlay renderer that samples _MainTex.
The script writes stereoTexture to the renderer material _MainTex.

## Event Wiring

UnderwaterStereoBlur exposes these public methods:

- OnUnderwaterEnter
- OnUnderwaterExit
- EnableBlur
- DisableBlur
- ToggleBlur

Call them from your existing water/event flow, or use PlayerViewBlurZone for trigger-based activation.

PlayerViewBlurZone setup:

1. Add a trigger collider to a GameObject (Is Trigger enabled).
2. Add PlayerViewBlurZone to the same object.
3. Assign blurTarget to the UdonBehaviour that has UnderwaterStereoBlur.
4. Keep default enter/exit event names unless you changed them.
5. Ensure autoTrigger is enabled.

## Stereo Compatibility

BlurryUnderWater.compute includes StereoDoubleWide handling.
When single-pass double-wide stereo is active, blur samples are clamped per eye so left/right eye pixels do not bleed together.

UnderwaterStereoBlur sets StereoDoubleWide automatically by checking sourceCamera.stereoEnabled.

## Troubleshooting

No blur appears:

1. Confirm blurShader is assigned.
2. Confirm captureTexture and stereoTexture are assigned.
3. Confirm stereoTexture has Random Write enabled.
4. Confirm stereoOverlayRenderer is assigned and uses a material that displays _MainTex.
5. Confirm OnUnderwaterEnter is being called.

Overlay appears but does not move with head:

1. Confirm Networking.LocalPlayer is valid at runtime.
2. Confirm the script object is active and not disabled by another script.

Strong performance cost:

1. Lower captureTexture and stereoTexture resolution.
2. Increase updateInterval.
3. Reduce camera rendering cost on sourceCamera.
