## Performance Goal

Ship smooth 60 FPS gameplay with reliable first-play animation and effect playback.

Priority order:
1. Eliminate gameplay hitching and spike-heavy streaming work.
2. Guarantee first visible frames for player, nearby enemies, and critical VFX.
3. Push expensive loading and warmup into loading overlays.
4. Accept longer loads and higher residency if that protects runtime smoothness.

## Current Baseline

Treat these as the new default architecture, not experiments:

- Runtime sprite residency is atlas-based.
  - Sprite libraries and shards stay text-based.
  - Runtime parses slice strings into `atlas address + sprite name`.
  - Addressables load full atlas sprite sets once.
  - Gameplay resolves slices from in-memory atlas maps.
- Loading and gear/apply work were moved under the loading overlay.
  - Save-load no longer starts gear/state work early from the menu click.
  - Gear warmup uses preload behavior under the overlay.
- Warmup is collect-only.
  - Pre-unlock warm paths gather atlas addresses.
  - They do not drive real renderers or replay animation frames.
- Enemy residency is tiered by relevance.
  - Visible / nearby / loading-overlay enemies pin.
  - `pinAllSpawnedEnemies` is no longer the intended model.
- Warm-gate policy is stricter.
  - Player, equipped gear, nearby threats, and core player effects block unlock.
  - Soft fail-open behavior for critical content was reduced.
- Location warm content is prefab-driven.
  - `LocationWarmProfile` now binds the actual location prefab by name.
  - `DomeCity` resolves from `Assets/Prefabs/Locations/DomeCity.prefab`.
- Sprite Streaming editor tools were reordered to match this flow.
  - Sync location profiles.
  - Apply import flow.
  - Apply hotset from scenes + location prefabs.
  - Rebuild runtime index.
  - Configure Addressables.
  - Build Addressables.
- Large warm-plan finalization now has a threaded CPU-only path.
  - Only sorting / set construction moved off-thread.
  - Unity object access and Addressables work remain on the main thread.

## What Past Attempts Actually Taught Us

### 1. Symptom-only animation gating tweaks were not the root fix

Many early passes changed:
- switch gate timeouts
- fail-open behavior
- transition bypasses
- stale completion handling
- request retarget cooldowns

Those may have reduced some stalls, but they did not solve the root issue when gameplay was still discovering cold sprite content at trigger time.

Guideline:
- Do not go back to micro-tuning animation gates first.
- Revisit only if fresh evidence shows residency is already correct and the remaining problem is purely animation-state CPU cost.

### 2. Per-slice gameplay streaming was the main structural mistake

The old model still behaved like:

`slice request -> Addressables sprite load -> repeated gameplay churn`

That caused first-play misses and repeated runtime work. The atlas-centric runtime path is the correct baseline for this project.

Guideline:
- Do not reintroduce gameplay-time per-slice loading paths.
- New fixes should preserve atlas residency as the primary unit.

### 3. Renderer-driving warmup was the wrong preload strategy

`WarmAllAnimationPlayback()` style warmup touched real renderers and did not scale to a Diablo 2 Resurrected-style game.

Guideline:
- Keep pre-unlock warmup as collect-only planning plus atlas loads.
- If warmup gets more expensive again, move more discovery to editor/build time instead of replaying runtime visuals.

### 4. Load timing matters as much as load volume

One root cause was save-load work starting before the loading overlay took control. That let player gear and appearance work run with gameplay-safe throttles instead of preload behavior.

Guideline:
- New systems that affect first-play visuals must start under the overlay or warm gate.
- Avoid menu-triggered or gameplay-triggered early work for critical appearance state.

### 5. D2-style games need relevance-based residency, not "all spawned"

Pinning all spawned enemies does not fit dense combat scenes. It creates churn once budgets are pressured.

Guideline:
- Keep residency focused on player, visible threats, nearby threats, current room, adjacent room, and core combat effects.
- If more coverage is needed, expand the relevant set, not the global set.

## Verified State As Of March 7, 2026

From the latest Unity `Editor.log`:

- Unity recompiled successfully.
- `Tools > Sprite Streaming > 0) Run Essential Pipeline` completed successfully.
- Addressables content build succeeded.
- `LocationWarmProfile_DomeCity.asset` serialized its `locationPrefab` reference correctly.

Observed in the same log:

- Repeated editor-side asset garbage collection events around `0.8s` while processing large asset sets.
- This is a useful signal for editor/build cost, but it is not gameplay proof.

Not yet verified after the latest runtime code changes:

- fresh gameplay profiler capture
- fresh load-save gameplay run
- confirmation that gameplay no longer falls back to slice-style misses
- effect of the threaded warm-plan finalize pass during a real warm gate

## Next Iteration Order

1. Capture a fresh gameplay profiler run after the current baseline.
2. Confirm whether first attack, first nearby enemy attack, and first room entry still cause cold atlas misses.
3. If misses remain, expand preload policy for the missing tier:
   - player
   - nearby enemy archetypes
   - current room
   - adjacent room
   - core combat VFX
4. If misses are low but spikes remain, profile CPU-side hot paths:
   - `GearController.Update()`
   - `AnimationController` switch/apply paths
   - owner pin refresh / pin mutation cost
   - warm-plan enqueue / cache pump cost
5. If warm-gate CPU cost is high, move more plan construction to editor/build data before adding more runtime complexity.

## Do Not Regress

- Do not move critical gear/load/apply work back out of the loading overlay.
- Do not restore per-slice gameplay asset loads.
- Do not restore renderer-driving preload loops.
- Do not use `pinAllSpawnedEnemies` as the primary residency policy.
- Do not judge new changes by editor pipeline success alone; require gameplay profiler evidence.
