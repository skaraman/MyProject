# Fire Effect Iteration

## Goal

Build a sprite fire effect preview workflow that can produce a convincing "flames on top of / around the sprite" look, not a generic full-rectangle heat/noise fill.

Current preview entry point:
- `Tools > Shader Preview > AllIn1 Effect Preview`

Current implementation inputs:
- Source sprite: `Assets/Sprites/Core/Empty.png`
- Fire template material: `Assets/Plugins/AllIn1SpriteShader/Demo/Materials/Fire.mat`

## Visual Target

Desired look from the reference:
- Mostly transparent canvas
- Flame mass concentrated near the lower center
- Upward directional motion
- Thin wispy edges and tapered flame tongues
- Strong silhouette variation instead of uniform rectangular coverage
- Hot bright cores with darker orange/red body
- Soft smoky dissipation above the main flame body

Current look:
- Rectangular fill
- Repeating noise texture
- Little or no believable flame silhouette
- Hard lower block / banding
- Too much coverage, not enough negative space

## Root Cause

The current AllIn1-based fire is shader-styling a mostly full mask, so it reads as "animated noisy gradient" instead of "fire shape."

The main problem is not color tuning.

The main problem is shape generation:
- alpha silhouette
- directional flow
- layered falloff
- breakup at the edges

## Decision Path

### Phase 1: Push AllIn1 as far as it can go

Use AllIn1 only if we can create the shape with:
- a dedicated flame mask texture
- clipping
- fade
- distortion
- wave
- flicker
- gradient

This is acceptable only if it gets us close to:
- sparse transparency
- upward flame tongues
- non-rectangular silhouette

### Phase 2: Switch to custom shader if needed

If AllIn1 still looks like a textured rectangle after mask work, stop iterating on parameter tuning and build a dedicated shader.

Likely custom shader features:
- vertical alpha falloff
- layered noise distortion
- flame mask remap
- emissive core ramp
- edge erosion
- optional smoke layer

## Iteration Log

### Iteration 0

Status:
- complete

What we learned:
- AllIn1 already has enough controls to test color, glow, flicker, and distortion quickly
- It does not automatically generate a convincing flame silhouette
- Using `Empty.png` literally is not useful by itself because it is fully transparent
- The preview window therefore needs either:
  - a generated solid mask, or
  - a dedicated flame mask texture

Assessment:
- The current result is not failing because of the wrong orange values
- It is failing because the effect has no believable flame shape

Next action:
- test a real flame-shaped alpha mask in the preview window before deciding on a custom shader

## Next Experiments

1. Use atlas sprite inputs in the preview window for the main fire silhouette plus fade/distortion patterns from `Assets/Sprites/Core/Textures`.
2. Test whether AllIn1 can produce acceptable flame tongues once the mask is non-rectangular.
3. If not, create a custom shader prototype focused on silhouette and upward flow first.

## Iteration 1

Status:
- complete

What changed:
- The preview window now accepts sprite sub-assets from `Assets/Sprites/Core/Textures` for:
  - the main mask
  - the fade/fade-burn texture
  - the distortion texture
- Those texture choices are exposed as dropdowns listing the available atlas sprite options so iteration can happen directly in the tool window.
- Atlas sprite rects are remapped into the shader's scale-and-tiling properties so a selected sub-sprite is sampled as an isolated region instead of the full atlas.

What this enables:
- We can now test whether the visual gap is fundamentally a mask-shape problem using the assets already in the project.
- We are no longer blocked on `Empty.png` or a generated solid square for the first silhouette experiments.

Remaining limitation:
- This is still the AllIn1 material path. If the result remains a noisy rectangle after trying shaped masks and pattern atlases, that is the decision point for a dedicated custom shader.

### Iteration 2

Status:
- complete

What changed:
- The preview window no longer feeds full atlas textures plus UV remap into the preview material for atlas sprite choices.
- It now builds a temporary cropped texture for each selected atlas sprite and assigns that sliced texture directly to the shader slot.

Why:
- The prior preview path still let the shader operate on the full atlas texture, which made the texture selectors behave like "whole atlas" picks instead of isolated sprite picks.
- The plugin's own runtime atlas helper uses atlas UV bounds for scene renderers, but in this editor preview tool the more reliable approach is slicing the selected sprite into its own temporary texture first.

Result:
- `Main Mask`, `Fade`, and `Distort` selectors now represent the actual chosen sprite region, not the whole atlas sheet.

### Iteration 3

Status:
- in progress

Direction change:
- Stop treating this as an AllIn1 tuning problem.
- Move to a dedicated fire shader path, with Shader Graph as the preferred authoring surface for iteration.

What exists now:
- A reusable flame core function at `Assets/Shaders/Fire/FirePreviewCore.hlsl`
- A matching preview shader at `Assets/Shaders/Fire/FirePreview.shader`
- A graph-building guide in `FireShaderGraphGuide.md`

Working rule:
- Use Shader Graph for property exposure, texture routing, and fast visual iteration.
- Use the custom function file for the hard flame field math instead of rebuilding that math from dozens of nodes.

## Working Rule

Do not spend more time tuning AllIn1 numeric properties if the silhouette is still rectangular.

Less code is better, but not at the cost of chasing the wrong abstraction.
