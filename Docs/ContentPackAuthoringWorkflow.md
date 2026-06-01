# Content Pack Authoring Workflow

## Purpose

Use this document when adding or changing gameplay content under the content-pack structure.

Default rule:

- use `Tools > Content Pack > 1) Build Active Content (Smart)` for normal day-to-day changes
- use `Tools > Content Pack > 2) Build Active Content (Clean)` only when output looks stale or structurally wrong

## Build Safety Net

Unity `Build` and `Build and Run` now run a preflight automatically before the player build:

- prepare staged active packs
- audit staged content
- rebuild the sprite runtime index

Normal `Build` and `Build and Run` use the smart/incremental preflight path.
Use `Tools > Content Pack > 2) Build Active Content (Clean)` only when you intentionally want the non-incremental fallback.

Use `Build Active Content (Smart)` as the normal authoring step anyway. The build preflight is a safety net, not the primary daily workflow.

## Primary Workflow

After most content changes:

1. Drop or update source art under `Assets/Sprites/Characters` or `Assets/Sprites/Environments`.
2. Update `Assets/ContentManifest.json` so new game content is declared before tooling runs.
   Core locations (`MainMenu`, `Homebase`/`Safehouse`) are always loaded and do not belong in the manifest.
3. Run the relevant Sprite Streaming authoring tool if the source requires atlas/offset processing.
4. Ensure the relevant pack is active or selected for the test you want to run.
5. Run `Tools > Content Pack > 1) Build Active Content (Smart)`.
6. Test in play mode or `Build and Run`.

Use this clean fallback only when needed:

1. Run `Tools > Content Pack > 2) Build Active Content (Clean)`.
2. Re-test.

## When To Use Which Tool

### Esperanza Animations

Use:

- `Tools > Content Pack > 1) Build Active Content (Smart)`

Use authoring tools first only if you changed atlas authoring output:

- `Tools > Authoring > Trim Atlas + Export Offsets`

### Gear Items

If you changed grouped gear atlas source content:

- `Tools > Authoring > Group Esperanza Gear Atlases`

Then run:

- `Tools > Content Pack > 1) Build Active Content (Smart)`

Notes:

- active runtime content should only include the equipped `Gear_*` packs you want to test
- if a gear item is inactive, it may be pruned from the runtime index and that is expected

### Effects

If the effect uses ordinary sprite assets:

- `Tools > Content Pack > 1) Build Active Content (Smart)`

If the effect required trimmed atlas authoring:

- `Tools > Authoring > Trim Atlas + Export Offsets`
- `Tools > Content Pack > 1) Build Active Content (Smart)`

### Locations

After adding or changing location-owned content:

- `Tools > Content Pack > 1) Build Active Content (Smart)`

If you need to reset selection to the current baseline slice:

- `Tools > Content Pack > 1) Build Active Content (Smart)`

Then run:

- `Tools > Content Pack > 1) Build Active Content (Smart)`

### Enemies

After adding or changing enemy-owned content:

- `Tools > Content Pack > 1) Build Active Content (Smart)`

## Debug Steps

Use the normal Smart build when checking pack staging and runtime index output:

- `Tools > Content Pack > 1) Build Active Content (Smart)`

The Smart build is the main verification step because it:

1. stages active packs
2. audits active packs
3. rebuilds the sprite runtime index

It does not export project-local source art. Use `Build Active Content` when artist-authored files under `Assets/Sprites` need to move into `D:\localDev\Unity\MyProjectContent`.

Play mode runs a content preflight. If source art, authored sprite libraries, external packs, or the runtime index are out of sync, the Console error should say whether to update `Assets/ContentManifest.json`, run authoring, or run `Tools > Content Pack > 1) Build Active Content (Smart)`.

## Authoring Rules

- runtime sprite loading assumes sprites belong to atlases when appropriate
- runtime `atlas.json` is optional and contains only `SpriteWithNormals` offset data
- runtime `atlas.json` should only exist when authored trim/group workflows produced real non-zero offsets
- no tool should regenerate packed-rect slice-definition metadata into runtime `atlas.json`

## Quick Reference

### Normal change

- `Tools > Content Pack > 1) Build Active Content (Smart)`

### Stale or suspicious output

- `Tools > Content Pack > 2) Build Active Content (Clean)`

### Atlas trim/offset authoring changed

- `Tools > Authoring > Trim Atlas + Export Offsets`
- then `Tools > Content Pack > 1) Build Active Content (Smart)`

### Esperanza grouped gear atlas source changed

- `Tools > Authoring > Group Esperanza Gear Atlases`
- then `Tools > Content Pack > 1) Build Active Content (Smart)`

### Need to verify pack staging/index

- `Tools > Content Pack > 1) Build Active Content (Smart)`
