# Content Pack Authoring Workflow

## Purpose

Use this document when adding or changing gameplay content under the new content-pack structure.

Default rule:

- use `Tools > Content Pipeline > 1) Build Active Content (Smart)` for normal day-to-day changes
- use `Tools > Content Pipeline > 2) Build Active Content (Clean)` only when output looks stale or structurally wrong

## Build Safety Net

Unity `Build` and `Build and Run` now run a preflight automatically before the player build:

- prepare staged active packs
- audit staged content
- rebuild the sprite runtime index

Normal `Build` and `Build and Run` use the smart/incremental preflight path.
Use `Tools > Content Pipeline > 2) Build Active Content (Clean)` only when you intentionally want the non-incremental fallback.

Use `Build Active Content (Smart)` as the normal authoring step anyway. The build preflight is a safety net, not the primary daily workflow.

## Primary Workflow

After most content changes:

1. Make the content change.
2. Ensure the relevant pack is active or selected for the test you want to run.
3. Run `Tools > Content Pipeline > 1) Build Active Content (Smart)`.
4. Test in play mode or `Build and Run`.

Use this clean fallback only when needed:

1. Run `Tools > Content Pipeline > 2) Build Active Content (Clean)`.
2. Re-test.

## When To Use Which Tool

### Esperanza Animations

Use:

- `Tools > Content Pipeline > 1) Build Active Content (Smart)`

Use authoring tools first only if you changed atlas authoring output:

- `Tools > Sprite Streaming > Authoring > Trim Atlas + Export Offsets`

### Gear Items

If you changed grouped gear atlas source content:

- `Tools > Sprite Streaming > Authoring > Group Esperanza Gear Atlases`

Then run:

- `Tools > Content Pipeline > 1) Build Active Content (Smart)`

Notes:

- active runtime content should only include the equipped `Gear_*` packs you want to test
- if a gear item is inactive, it may be pruned from the runtime index and that is expected

### Effects

If the effect uses ordinary sprite assets:

- `Tools > Content Pipeline > 1) Build Active Content (Smart)`

If the effect required trimmed atlas authoring:

- `Tools > Sprite Streaming > Authoring > Trim Atlas + Export Offsets`
- `Tools > Content Pipeline > 1) Build Active Content (Smart)`

### Locations

After adding or changing location-owned content:

- `Tools > Content Pipeline > 1) Build Active Content (Smart)`

If you need to reset selection to the current baseline slice:

- `Tools > Content Pipeline > Advanced > Focus First Slice`

Then run:

- `Tools > Content Pipeline > 1) Build Active Content (Smart)`

### Enemies

After adding or changing enemy-owned content:

- `Tools > Content Pipeline > 1) Build Active Content (Smart)`

## Transition-Safe Debug Steps

Use these only when checking the migration/runtime-pack pipeline itself:

- `Tools > Content Pipeline > Advanced > Stage Active Packs`
- `Tools > Content Pipeline > Advanced > Audit Active Packs`
- `Tools > Content Pipeline > Advanced > Prepare Active Packs`

`Prepare Active Packs` is the main transition-safe verification step because it:

1. refreshes exported pack content
2. stages active packs
3. audits active packs
4. rebuilds the sprite runtime index

## Authoring Rules

- runtime sprite loading assumes sprites belong to atlases when appropriate
- runtime `atlas.json` is optional and contains only `SpriteWithNormals` offset data
- runtime `atlas.json` should only exist when authored trim/group workflows produced real non-zero offsets
- no tool should regenerate packed-rect slice-definition metadata into runtime `atlas.json`

## Quick Reference

### Normal change

- `Tools > Content Pipeline > 1) Build Active Content (Smart)`

### Stale or suspicious output

- `Tools > Content Pipeline > 2) Build Active Content (Clean)`

### Atlas trim/offset authoring changed

- `Tools > Sprite Streaming > Authoring > Trim Atlas + Export Offsets`
- then `Tools > Content Pipeline > 1) Build Active Content (Smart)`

### Esperanza grouped gear atlas source changed

- `Tools > Sprite Streaming > Authoring > Group Esperanza Gear Atlases`
- then `Tools > Content Pipeline > 1) Build Active Content (Smart)`

### Need to verify pack staging/index only

- `Tools > Content Pipeline > Advanced > Prepare Active Packs`
