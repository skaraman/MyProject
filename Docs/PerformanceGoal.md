## Performance Goal

Ship smooth 60 FPS gameplay with fast, honest loading and no visible pop-in after reveal.

## Current Objective

1. Keep loading heartbeat gaps at `<= 2.0s`.
2. Keep loading progress smooth and truthful.
3. Finish reveal-critical work under black so gameplay appears stable on first frame.
4. Continue moving toward dynamic smart loading driven by relevance and location context.
5. Stand up `Core + Slice_DomeCity_Imp_Base` before doing any further episode content work.

## Loading Pipeline Contract

- Keep gameplay loading consistent and ordered:
  `player -> location -> enemies -> ui -> dialog`
- Keep gameplay hidden behind black until location activation, deferred location work, critical streaming, UI readiness, and dialog readiness are settled.
- `Start Game` and `Load Game` both run through the gameplay loading overlay flow.
- Gameplay location is staged once and consumed from `ApplyGameplayStateUnderBlack()`.
- Main-menu transitions clear back to logical `mainmenu`.
- Gameplay location requests reject `mainmenu` and resolve in this order:
  `preferred -> current -> last_known -> default`
- Progress stages use runtime readiness instead of hierarchy-only checks:
  - `player`: player bootstrap ready
  - `location`: no blocking location activation work
  - `enemies`: gameplay warm gate complete
  - `ui`: gameplay UI active and dialog UI resolved
  - `dialog`: dialog controller active, UI resolved, dialog state ready
- Visible percent is stage-bounded:
  - `player`: `0% -> 18%`
  - `location`: `18% -> 42%`
  - `enemies`: `42% -> 72%`
  - `ui`: `72% -> 86%`
  - `dialog`: `86% -> 94%`
  - `finalizing reveal`: capped below release until the explicit `Ready` handoff
- Pre-release percent is not auto-promoted to `99%`.

## Current Truth

Source: latest Unity `Editor.log` runs on `2026-03-24` and `2026-03-30`.

Observed:
- `[SingleSceneManager][LoadingHeartbeatGap] gap_s=3.768` was logged at `2026-03-24T21:53:59.136Z`.
- Loading status moved quickly to `80%+`, then spent a long time in `Preparing player` / `Warming critical`.
- `[SingleSceneManager][LoadingStatus] percent=99 detail='Ready'` was logged at `2026-03-24T21:54:28.829Z`.
- Gameplay-side activation did not begin until `2026-03-24T21:54:34.743Z`.
- Queued location dialog work was still being released after that activation window, with dialog start at `2026-03-24T21:54:36.913Z`.
- On the latest rerun, `[SingleSceneManager][RevealSettle] timeout elapsed_s=2.014` was logged at `2026-03-24T22:45:01.037Z` while queue, resolver, and player were already ready.
- That timeout still reported `location_activation_pending=1`, which points at location activation state bookkeeping rather than live stream backlog.
- After the deferred-activation fix, the next `2026-03-24` rerun no longer showed visible post-fade pop-in in gameplay.
- The same rerun still logged `[SingleSceneManager][LoadingHeartbeatGap] gap_s=6.436` at `2026-03-24T22:57:45.527Z`.
- That rerun also logged multiple `[TextureResidencyCache][CompletionDiag]` spikes between `202.8ms` and `488.7ms` during the protected loading window.
- The latest `2026-03-24` rerun confirmed reveal handoff is no longer the long pole, but still logged `[SingleSceneManager][LoadingHeartbeatGap] gap_s=14.032` at `2026-03-24T23:07:30.841Z`.
- That same rerun logged protected-overlay completion spikes at `226.5ms`, `212.2ms`, `270.9ms`, `286.4ms`, and `506.6ms`.
- A later `2026-03-30` run logged:
  - `2026-03-30T23:06:55.891Z` -> `18% / Activating location`
  - `2026-03-30T23:07:05.424Z` -> `95% / Preparing enemies`
  - `2026-03-30T23:07:22.203Z` -> `99% / Preparing UI`
- The active problem is smoothness and reveal timing, not a total loading failure.

Interpretation:
- `Ready` must not appear before gameplay activation and reveal settle are actually complete.
- Late pipeline stages must not borrow end-stage percent headroom before they are actually ready.
- Reveal-critical activation still needs to stay hidden behind the overlay.
- Deferred or same-location carry-over activation must not block reveal once blocking activation is done.
- The next optimization target is large completion/finalization work and gameplay handoff cost, not reveal correctness.
- Reveal correctness is now good enough that the active target is protected-overlay completion cost.
- Queue/completion bursts are still large enough to hurt perceived speed and smoothness.

## Current Implementation Update

- `SingleSceneManager` now tracks a monotonic gameplay loading stage:
  `Player -> Location -> Enemies -> Ui -> Dialog -> FinalizingReveal`
- Stage advancement is latched and does not move backward on transient readiness checks.
- Each stage contributes both a target percent and a ceiling percent.
- Visible percent cannot enter a later stage's range until the earlier stage is actually complete.
- Visible percent is no longer auto-promoted to `99%` before release.
- Reveal settle now also requires `ui_ready` and `dialog_ready`, not just queue, resolver, and player readiness.
- Runtime re-check is still pending after this patch.

## Latest Verified Baseline

- Latest verified good flow before this patch: `2026-03-26` around `22:05`.
- `LoadGameFlow` reached gameplay with `current_location=DomeCity`.
- Gameplay activation completed before reveal settle finished.
- Reveal settled after the queue drained and then released.
- The main remaining log noise on that run was a brief end-stage status bounce between `Finalizing reveal` and `Draining queue`.

## Cache Contract

- Session-global UI cache:
  - `MainMenu`
  - `PauseMenu`
  - shared font atlases
- Persistent player baseline:
  - ESPER core skin atlases
  - ESPER core effect atlases
- Environment hot cache:
  - exactly `2` gameplay environments
  - `current`: active gameplay environment
  - `previous`: most recently displaced gameplay environment
- Environment prefab retention follows the same `2`-environment bound.
- `mainmenu` is never treated as an environment hot-cache slot.
- Environment hot cache rotates only when a new gameplay location is remembered.
- Environment hot cache is sourced from `LocationWarmProfile.CollectEnvironmentCacheLists(...)`.
- Warm-gate and pre-unlock resident pins are temporary and should be released after gameplay reveal settles.
- Enemy runtime residency remains dynamic and distance-based.
- Session runtime assets that are not menu-global should still be clearable on main-menu return.
- Implementation owners:
  - menu/session UI runtime assets: `RuntimeAssetCache`
  - persistent fonts/player baseline: `SingleSceneManager`
  - environment hot cache rotation: `SingleSceneManager`
  - environment prefab LRU cache: `LocationPrefabData`
- Rules for new cache work:
  - if an asset category is required in nearly every session, prefer a persistent owner
  - if an asset category is location-specific, prefer the two-slot environment hot cache over a permanent cache
  - if a cache does not have an explicit eviction rule, it is incomplete

## Asset Streaming Heuristics

- There is no meaningful hard asset-count cap; real limits are memory, catalog size, build time, and editor responsiveness.
- Group content by lifecycle:
  assets that load together should unload together.
- Use labels or equivalent lifecycle buckets as the primary loading unit.
- Avoid loading everything at boot.
- Batch large load sets instead of issuing one giant request.
- Keep bundle count reasonable; avoid one-asset-per-bundle fragmentation unless independence is truly required.
- Always release handles when ownership ends.
- Profile memory and load behavior continuously, especially on low-end targets.

## Current Streaming Fit

- `StreamingWarmOrchestrator` and `SingleSceneManager` already batch work in `100`-item chunks, which still fits the old guide's `50-200` heuristic.
- `StreamingWarmOrchestrator` sorts warm content by lifecycle labels before loading, which matches the lifecycle-first rule.
- `SpriteRuntimeResolver` uses a sharded manifest system, which is still the right answer for catalog scale.
- `AnimationController` lookahead/pinning remains valid because it protects first-frame smoothness and reduces runtime stalls.
- `SingleSceneManager` pre-unlock prefetch is still aggressive enough to require memory checks on weaker devices.
- `TextureResidencyCache` handle release discipline remains a critical watchpoint; leaks there would directly violate the loading and memory goals.

## Architecture To Preserve

- Runtime sprite residency is atlas-based.
- Gameplay does not depend on per-slice Addressables loads.
- Pre-unlock warmup is collect-only and does not drive live renderer playback.
- Critical player, gear, nearby-threat, and core-effect work stays under the loading overlay / warm gate.
- Enemy residency stays relevance-based rather than `pinAllSpawnedEnemies`.
- Location warm content is prefab-driven through `LocationWarmProfile`.
- Grouped gear atlases build through surrogate assets so runtime can keep atlas behavior without packed-build sprite fan-out.
- Unsupported trimmed-metadata atlas families such as `_Bounces`, `Effects`, and `Expressions` are skipped instead of queued for guaranteed-fail metadata loads.

## Active Focus

- treat `DomeCity` as the only active gameplay slice for the current stand-up pass
- ignore `Homebase` and `SunkenCave` until `Slice_DomeCity_Imp_Base` is stable
- preload only content that is relevant to the current location, player state, and nearby threats
- keep high-value content ready before reveal
- keep loading percent monotonic and avoid visible stalls caused by fake headroom
- keep `Ready` hidden until gameplay activation and opaque reveal settle are complete
- finish location staged activation under the overlay so `FG/Dynamic` and `FG/Destruct` do not pop in after fade-out
- use `[SingleSceneManager][RevealSettle]` and `[SingleSceneManager][LoadingStatus]` to identify the real late blocker
- reduce queue bursts and completion bursts that front-load low-value work
- expand or contract warm sets based on actual relevance instead of static over-pin behavior

## Diagnostics To Watch

- `[SingleSceneManager][LoadingHeartbeatGap]`
- `[SingleSceneManager][LoadingStatus]`
- `[SingleSceneManager][OptimalLoadingProgress]`
- `[SingleSceneManager][RevealSettle]`
- `[SingleSceneManager][GameplayLocation]`
- `[SingleSceneManager][EnvironmentCache]`

## What To Measure Next

1. Re-run `Start New Game` and `Load Game`.
2. Confirm there are no `[SingleSceneManager][LoadingHeartbeatGap]` entries above `2.0s`.
3. Confirm `Preparing UI` never logs above `86%`.
4. Confirm `Preparing dialog` never logs above `94%`.
5. Confirm loading percent does not jump to `99%` before the explicit `Ready` release.
6. Confirm loading status does not reach `Ready` before gameplay activation is complete.
7. Confirm `[SingleSceneManager][RevealSettle]` does not time out and identifies any remaining blocker if reveal still holds.
8. Confirm no visible world, UI, or dialog pop-in occurs after black starts fading out.
9. If a hitch remains, correlate it first with:
   - location prefab activation
   - player first-frame preparation
   - atlas completion bursts
   - queue depth / deferred work

## Do Not Regress

- Do not restore per-slice gameplay asset loads.
- Do not move critical gear/load/apply work back out of the loading overlay.
- Do not restore renderer-driving preload loops.
- Do not return to `pinAllSpawnedEnemies` as the default residency policy.
- Do not treat editor pipeline success alone as proof of runtime smoothness.
