## Performance Goal

Ship smooth 60 FPS gameplay with fast, honest loading and no visible pop-in after reveal.

## Play Mode Loading Optimization (2026-05-02)

### Root Cause Analysis

**Problem**: `StartGame()` warm gating was hitting the hard timeout in Play mode because:
1. **Soft timeout too aggressive**: [`startWarmTimeoutSeconds`](Assets/Scripts/SceneManager/SingleSceneManager.cs:176) = `2.0s` insufficient for disk I/O
2. **Ratio too high**: [`startWarmRequiredRatio`](Assets/Scripts/SceneManager/SingleSceneManager.cs:178) = `0.97` treated non-critical warm tails as reveal blockers
3. **Hard bypass delay**: System waited up to [`startWarmHardTimeoutSeconds`](Assets/Scripts/SceneManager/SingleSceneManager.cs:177) = `10s` before forcing unlock when soft timeout failed

**Diagnosis**: Play mode Addressables load from disk with no bundle cache. Critical readiness is the reveal contract; broad ratio, full queue idle, deferred activation, and large animation tails should not own reveal.

### Solution: Aggressive Lazy Loading v2

1. **Increased soft timeout**: [`startWarmTimeoutSeconds`](Assets/Scripts/SceneManager/SingleSceneManager.cs:176) from `2.0s` to `4.0s`
2. **Reduced ratio threshold**: [`startWarmRequiredRatio`](Assets/Scripts/SceneManager/SingleSceneManager.cs:178) from `0.97` to `0.35`
3. **Hard bypass retained**: [`startWarmHardTimeoutSeconds`](Assets/Scripts/SceneManager/SingleSceneManager.cs:177) = `10s`
4. **Shrunk pre-unlock work**: [`WaitForStreamingIdleBeforeUnlock()`](Assets/Scripts/SceneManager/SingleSceneManager.cs:4527) now uses bounded queue release and smaller visible/animation warmup caps
5. **BuildScriptFastMode**: Already configured as Play Mode builder ([`SpriteAddressCatalogBuilder.cs:1060`](Assets/Editor/SpriteAddressCatalogBuilder.cs:1060))

**Expected Impact**:
- Gameplay unlock within 2-4 seconds (soft timeout) instead of 10s hard bypass
- Critical assets (player, location, spawn enemies) still block reveal via [`IsBlockingScopeReady()`](Assets/Scripts/SceneManager/SingleSceneManager.cs:1590)
- Non-critical sprites load lazily post-reveal via deferred warmup

## Recent Progress

- 2026-05-13: Play Mode and BuildAndRun both load and play from active content packs.
- `../MyProjectContent` is now the authoritative content source.
- `Assets/ContentStage` remains the only project-local runtime/editor mirror.
- Primary content builds stage existing external packs, audit them, rebuild the runtime index, apply hotsets, and build Addressables.
- Project-local duplicate sprite payloads under `Assets/Sprites` are being retired after matching external pack copies are verified.
- Legacy transition menu entries and old slice-id migration mapping have been removed from the editor pipeline.
- 2026-05-15: runtime-index GUID address-map caching now caches empty probe results, removing the repeated `ResolveSpriteAddress: Caching address map` flood.
- 2026-05-15: external-enabled sprite library and texture discovery now uses staged active pack roots only, not the full `Assets/Sprites` tree.
- 2026-05-15: reveal release now uses critical-ready plus bounded queue thresholds; deferred location activation no longer blocks reveal.
- 2026-05-15: pre-unlock warmup is capped to smaller first-frame windows, and active gear staging now filters to equipped body-part leaf packs.
- 2026-05-16: Core export now explicitly owns scene-only runtime UI libraries (`MainMenu/MainMenu`, `Items/Items`, `Dialog/DialogUI`, `UI/CharUI`, `UI/MapMenus`, `UI/SelectMenus`) so staged runtime indexes can resolve Play-mode UI sprite rows from external packs.
- 2026-05-16: Added `Assets/ContentManifest.json` and Play-mode content preflight so stale authoring/content-packing state fails with an actionable Content Pipeline error before runtime sprite lookup errors.
- 2026-05-31: Split `ContentPackPipeline` into focused editor partials under `Assets/Editor/ContentPackPipeline` without changing the loading/staging contracts.
- 2026-05-31: Reorganized `Assets/Scripts/Util/AssetStreaming` into focused addressing, atlas metadata, configuration, diagnostics, runtime residency, resolver, texture cache, and warm orchestration folders while preserving script `.meta` GUIDs.
- 2026-05-31: Resolved compiler warning CS0618 in `ProfilerWindowInterface.cs` by wrapping the `EntityId` to `int` conversion inside a `#pragma warning disable CS0618` block.
- 2026-05-31: Root-caused `InvalidKeyException Key=atlas/atlas_13` to stale runtime-index rows that violated the staged-content contract. Runtime shards now validate and emit staged slice addresses like `Assets/.../atlas.png[spriteName]`, external-enabled discovery stays on staged active roots, and the residency cache rejects unsupported relative atlas keys before Addressables.
- 2026-05-31: Root-caused the 40+ minute `Build Active Content (Smart)` stall to runtime-index rebuild resolving large Esperanza gear libraries through inactive/local gear GUIDs. The builder now builds a small active texture GUID index from staged `.meta` files, filters `.spriteLib` rows to active staged texture GUIDs before resolver work, and only loads sprite subassets for active staged textures.
- 2026-05-31: Restored scene-only Core UI libraries (`Dialog/DialogUI`, `Items/Items`, `MainMenu/MainMenu`, `UI/CharUI`, `UI/MapMenus`, `UI/SelectMenus`) as explicit Core-owned libraries so scene references are exported and staged with active Core content instead of failing the runtime-index rebuild.
- 2026-05-31: Resolved the Build asset version error (Import Error Code: 4) caused by AllIn1ShaderImporter attempting to rewrite shaders inside background Asset Import Worker processes, by adding an AssetDatabase.IsAssetImportWorkerProcess() guard to avoid redundant shader writes.

## Current Target

- First target is measurement, not another speculative optimization.
- Load Game into `DomeCity` must identify one dominant elapsed-time owner before changing preload breadth or reveal gates.
- Keep reveal honest: never show gameplay until player first frame, BG, FG/Static, spawn-critical enemies, gameplay UI shell, and dialog shell are ready.
- Defer non-visible and non-critical tails when data proves they are holding reveal.
- Baseline remains `Core + Form_Base + equipped Gear_* + Slice_DomeCity_Imp_Base`.

## Current Diagnosis

- The actual zone load is slow because multiple shared contracts can still hold black: location resolver barrier, staged prefab activation, warm-gate planning/enqueue, pre-unlock queue settle, and reveal settle.
- Recent evidence showed `DomeCity` prefab resolution succeeds, then loading can park at `Activating location` with `pipeline_location_ready=0`.
- 2026-04-25 `09:29:59Z` crash attempt did not reach gameplay loading. Unity crashed in `UnityEditor.Search.LMDBIndexStorage.GetDocuments`; generated `Library/Search` contained a `59.9 GB` LMDB index and was moved to `Crash/UnitySearch_LMDB_corrupt_20260425_092959`.
- `UnitySearchCacheGuard` now moves generated `Library/Search` into `Crash/` when the cache or any single index file exceeds `2 GB`.
- Current instrumentation now emits aggregate timing only:
  `[LocationManager][LocationLoadTiming]` and `[SingleSceneManager][LoadTiming]`.

## Loading Contract V2

- Ordered reveal-critical path:
  `request location -> resolve prefab -> resolver barrier -> instantiate staged prefab -> activate BG + FG/Static -> warm player/enemy/UI/dialog criticals -> pre-unlock settle -> reveal settle`.
- Location activation capacity is only a throttle, not a readiness owner.
- Resolver shard readiness belongs to the resolver barrier only.
- Dynamic/destructible location content is background work unless explicitly promoted by measured first-frame need.
- Warm-gate success is critical-ready first, ratio second.
- Progress text must name the active owner: location, warm gate, pre-unlock, or reveal settle.

## Measurement Protocol

1. Run `Load Game` into `DomeCity`.
2. Capture latest `Editor.log`.
3. Compare:
   - `[LocationManager][LocationLoadTiming] stage=prefab_resolved`
   - `[LocationManager][LocationLoadTiming] stage=resolver_barrier`
   - `[LocationManager][LocationLoadTiming] stage=prefab_instantiated`
   - `[LocationManager][LocationLoadTiming] stage=stage_activation`
   - `[SingleSceneManager][WarmScope]`
   - `[SingleSceneManager][LoadTiming] stage=warm_gate`
   - `[SingleSceneManager][LoadTiming] stage=pre_unlock`
   - `[SingleSceneManager][LoadTiming] stage=reveal_settle`
4. Name the largest elapsed owner and only then change the relevant contract.
5. Repeat the same check for `Start Game` and compare against `Load Game`.

## Decision Rules

- If resolver barrier is slow: start resolver/library warmup earlier, before black is fully opaque.
- If activation is slow: keep BG + FG/Static reveal-blocking and move Dynamic/Destruct to deferred activation.
- If warm gate is slow: shrink critical warm scope to current room, player first frame, spawn-critical enemies, and dialog shell.
- If pre-unlock is slow: reduce visible prefetch breadth and move animation-frame tails to deferred post-reveal warmup.
- If reveal settle is slow: release on critical-ready plus bounded outstanding queue instead of full queue idle.
- Do not accept a local fix until the log proves the issue is isolated.

## Archived Ledger

## Content Pack Pipeline Status

- The editor pipeline now has two primary buttons:
  - `Tools/Content Pack/1) Build Active Content (Smart)`
  - `Tools/Content Pack/2) Build Active Content (Clean)`
- Smart mode stages active external packs and builds Addressables without rewriting source content.
- Clean mode keeps the cache-clean recovery path.
- `../MyProjectContent` is the authoritative content source.
- `Assets/ContentStage` is the intentional runtime/editor mirror for active packs because it gives runtime stable, predictable asset paths.
- Transition audit now reports:
  - legacy `Assets/Generated` references
  - duplicate sprite content between project-local and external roots
  - staged content dependencies that still point outside stage roots
  - staged code references as informational output instead of content leaks
  - pack-ownership findings across `Core`, `Form`, `Slice`, and `Episode`
- The remaining cleanup target is removal of `Assets/Generated` references and non-identical sprite-library overlaps.
- The stage mirror in `Assets/ContentStage` is intentional.
- `Core + Form_Base + equipped Gear_* + Slice_DomeCity_Imp_Base` remains the runtime validation baseline.
- Placeholder packs are now exempt from ownership warnings during transition analysis and are logged as informational deferrals instead.
- The prior staged dependency blocker was narrowed to script references from staged prefabs.
  Those references are now tracked as informational code dependencies instead of content-leak failures.
- Staged Esperanza grouped gear atlases now treat Unity-imported sprite subassets as the authoritative slice source.
  Runtime atlas `.json` remains offset-only for placement data and should no longer be used to synthesize staged grouped-gear sprites during loading.
- Runtime performance comes from ownership hierarchy plus preload intent:
  `player -> location -> enemies -> ui -> dialog`.
- Player-choice packs such as `Form` and `Gear` are staged and resolved from known active state instead of keeping broad duplicate content resident.

## Investigation Ledger - Esperanza Playground

Context:
- Scene under test: `Assets/Scenes/esperPlayground.unity`
- Scope assumption: this scene is a player-only startup test, so no location, enemy, UI, or dialog loading should be allowed to explain multi-second body-part assembly.
- Success condition: all reveal-critical Esperanza parts appear at their final trimmed offsets on effectively one startup frame.
- Progress source of truth: use `[SingleSceneManager][OptimalLoadingProgress]` for stage progress and pair it with `[AnimationController][StartupSync]`, `[GearController][StartupAppearanceWarmup]`, and `[SpriteWithNormals][Offset]` to confirm the visible result.

Latest Evidence - `2026-04-02 01:19`:
- `2026-04-02T01:19:41.459Z`: `[AnimationController][StartupSync] stage=begin`
- `2026-04-02T01:19:46.153Z`: `[GearController][StartupAppearanceWarmup] source=enable addresses=288 ready=0/288 elapsed_ms=395.1`
- `2026-04-02T01:19:46.596Z` through `2026-04-02T01:19:47.557Z`: first zero-to-offset skin-part repositions are already happening while startup hold is still active
- `2026-04-02T01:19:48.489Z` through `2026-04-02T01:19:49.684Z`: first zero-to-offset base-part repositions still happen one by one
- `2026-04-02T01:19:49.690Z`: `[AnimationController][StartupSync] stage=ready elapsed_ms=8231.7`
- Measured span from startup-sync begin to the last first-time base-part reposition: about `8.231s`
- Measured span from the last first-time base-part reposition to startup-sync ready: about `0.006s`
- Observable result: startup gating timing is now close to correct, but the actor is still visibly assembling during the hold window, so visual suppression is failing even when the logical gate stays active

Assumed Problem Diagnostics:
- `DIAG-001 AddressResolutionStarvation`
  Hypothesis: startup warmup still cannot resolve the exact visible slice addresses early enough to build a complete first-frame request set.
  Evidence: older runs logged `[GearController][StartupAppearanceWarmup] stage=skip_no_addresses`, but the latest run collected `288` addresses.
  Status: `mitigated`
- `DIAG-002 FalseReadyBeforeFirstPositionedFrame`
  Hypothesis: startup gating reaches `ready` before every reveal-critical base part has received its first correct trimmed offset.
  Evidence: older runs showed `stage=ready` before first-time base offsets finished, but the latest run reached `stage=ready` only after the last first-time base-part reposition.
  Status: `mitigated`
- `DIAG-003 PiecewiseBasePartResolution`
  Hypothesis: base-part sprite or trim readiness still becomes available incrementally, causing visible zero-to-offset jumps per object instead of a single batched reveal.
  Evidence: the latest run still staggers first-time base-part repositions across `FootLeft`, `ArmLeft`, `ArmRight`, `CalfLeft`, `FootRight`, `CalfRight`, `ThighLeft`, `Pelvis`, `ThighRight`, and `Torso` from `01:19:48.489Z` through `01:19:49.684Z`.
  Status: `active`
- `DIAG-004 PlaygroundScopeMismatch`
  Hypothesis: the playground scene is still following a generalized gameplay warmup path instead of a strict player-only first-frame contract.
  Evidence: the observed startup cost is still measured in multi-second staged assembly even though this scene is intended only to validate Esperanza mechanics.
  Status: `active`
- `DIAG-005 StartupVisualSuppressionBypassed`
  Hypothesis: startup hold remains logically active, but the target renderers are being re-enabled during that hold, so partial startup frames still leak to screen.
  Evidence: on the latest run, first zero-to-offset repositions happen from `01:19:46.596Z` onward while `[AnimationController][StartupSync] stage=ready` does not occur until `01:19:49.690Z`.
  Inference: direct `SpriteRenderer.enabled = false` suppression is overwritten by `SpriteWithNormals.SyncRendererVisibility()`, which only respected `doNotRender`.
  Status: `active`
- `DIAG-006 StartupWarmupNonBlockingOnEnable`
  Hypothesis: the initial `source=enable` startup warmup still collects addresses but does not actually wait long enough to make them ready before the hold path has to do the real work.
  Evidence: the latest run logged `addresses=288 ready=0/288 elapsed_ms=395.1` for the `source=enable` warmup pass.
  Status: `active`

Algorithm Attempts:
- `ALG-001 StartupSyncHold`
  Goal: block the initial visible animation frame until the startup frame is declared ready.
  Code areas: `AnimationController`
  Result: `partial`
  Notes: the latest run shows `stage=ready` after the last first-time base-part reposition, so logical gate timing improved, but the actor still assembled visibly during the hold window.
- `ALG-002 ExactSliceStartupWarmup`
  Goal: prewarm the current visible Esperanza animation window using exact slice addresses for startup appearance parts.
  Code areas: `GearController`, `AddressableSpriteCache`
  Result: `partial`
  Notes: the latest run collected `288` startup addresses instead of skipping, but the `source=enable` pass still finished with `ready=0/288`, so the hold path still had to absorb the real wait.
- `ALG-003 TrimmedMetadataGating`
  Goal: require trim metadata before first-frame readiness so visible parts do not appear centered and jump later.
  Code areas: `SpriteWithNormals`, `TrimmedSpriteOffsetResolver`
  Result: `partial`
  Notes: readiness is stricter and now aligns `stage=ready` more closely with the last first-time base-part reposition, but visible startup assembly still occurs because suppression is leaking.
- `ALG-004 EditorRuntimeIndexDirectResolve`
  Goal: load the runtime sprite index manifest and shards synchronously in editor play mode so startup warmup can resolve exact addresses immediately.
  Code areas: `SpriteRuntimeResolver`
  Result: `validated`
  Notes: the latest run no longer logged `stage=skip_no_addresses` and collected `288` startup addresses.
- `ALG-005 StartupRendererHide`
  Goal: hide the actor during startup hold by toggling `SpriteRenderer.enabled`.
  Code areas: `AnimationController`
  Result: `failed`
  Notes: this did not stop visible assembly because `SpriteWithNormals.SyncRendererVisibility()` re-enabled the renderers during runtime updates.
- `ALG-006 SpriteLevelStartupVisualSuppression`
  Goal: move startup-hold suppression into `SpriteWithNormals` so runtime visibility sync cannot undo it.
  Code areas: `AnimationController`, `SpriteWithNormals`
  Result: `active`
  Notes: current worktree patch introduces sprite-level external suppression; next playground rerun must verify that no first-time zero-to-offset reposition is visible before startup reveal.
- `ALG-007 EditorStartupAtlasReadyFastPath`
  Goal: let editor play-mode startup resolve atlas sprite maps and trimmed metadata immediately for the current Esperanza startup slice instead of waiting on paced supplement queues.
  Code areas: `AddressableSpriteCache`, `GearController`
  Result: `active`
  Notes: current worktree patch routes startup readiness checks through immediate editor atlas supplementation and immediate startup metadata priming; next playground rerun must verify that startup-sync duration drops materially from the current ~`8.4s`.
- `ALG-008 AssetLoadTraceMonitor`
  Goal: write one correlated runtime trace for asset queue/start/complete/release events, discovery/release scans, memory counters, and GC activity so load gaps can be diagnosed from data instead of guesswork.
  Code areas: `AssetLoadTraceMonitor`, `AddressableSpriteCache`, `RuntimeAssetCache`, `SpriteStreamingDiagnostics`
  Result: `active`
  Notes: current worktree patch writes `asset-load-trace-*.csv` to `Application.persistentDataPath/Diagnostics`; next rerun should use that trace to identify burst starts, completion cliffs, memory spikes, and assets that bypass the intended warm path.
- `ALG-009 AtlasMetadataPayloadSplit`
  Goal: stop writing editor-only atlas metadata into the runtime `.json` payloads so startup only carries the fields the runtime loaders actually consume.
  Code areas: `TrimmedAtlasExporterWindow`, `EsperanzaGearGroupAtlasWindow`, `GeneratedAtlasImportPostprocessor`
  Result: `active`
  Notes: current worktree patch writes lean runtime atlas metadata plus full `.editor.json` sidecars, the import/rebind editor paths now prefer the sidecars so runtime metadata no longer needs to carry authoring-only fields, and `Assets/Editor/backup/strip_atlas_json_to_offsets.py` can scan all `.json` files under a folder, validate sprite-metadata payload shape, and batch-rewrite supported files down to sprite name plus offset `x/y` from either CLI or a basic folder-picker UI.
- `ALG-010 ProtectedAtlasOwnerPrimaryLoad`
  Goal: keep atlas-backed UI loads on the owning atlas address during protected startup/main-menu loading so direct subasset loads do not fall back to slice-key requests like `atlas.png[f1]`.
  Code areas: `AddressableSpriteCache`
  Result: `active`
  Notes: staged Main Menu atlases already preserve multi-sprite import data in `.meta`, and zero-offset runtime atlas `.json` is intentionally absent there; the current worktree patch corrects the protected single-address load path to request the owner atlas instead of a slice-key address so `Esperanza`, flowers, skulls, and button sprites can resolve from direct atlas subassets again.
- `ALG-011 MainMenuAtlasLogicalSlicePriority`
  Goal: make built main-menu atlas resolution prefer runtime-index logical slice names like `s`, `e1`, `f1`, `skull1`, and `idle`, while still tolerating Unity internal atlas names as secondary data.
  Code areas: `AddressableSpriteCache`
  Result: `active`
  Notes: current worktree patch now prefers runtime-index slice-key loads for atlas-backed direct subasset requests when sibling slice addresses exist, registers logical-name aliases into `spritesByName` from those sibling addresses, and adds targeted atlas-map diagnostics for `MainMenu` and `UI/Fonts` so incomplete runtime atlas-name translation can be confirmed directly from `Editor.log`.

Iteration Rules:
- Add a new dated evidence block after every playground rerun using exact timestamps from `Editor.log`.
- Keep each algorithm entry stable by name; only update `Result`, `Notes`, and linked diagnostics as evidence changes.
- Do not mark the issue solved until no reveal-critical Esperanza part performs its first zero-to-offset reposition after startup `ready`.
- Treat `[SingleSceneManager][OptimalLoadingProgress]` as the progress contract and treat `[SpriteWithNormals][Offset]` as the visible truth check.

## Checkpoint - 2026-03-31

- `Core + Form_Base + equipped Gear_* + Slice_DomeCity_Imp_Base` now stands up and reaches live gameplay.
- The game runs and plays through the first slice after the current content-pack and sprite-streaming pipeline pass.
- `DomeCity` is the current validated gameplay checkpoint and should remain the only active slice until cleanup and stabilization are done.
- The current non-blocking issue is repeated dialog debug noise after auto dialog is exhausted:
  `[DialogController][TryBuildTriggeredSequence] No unseen lines remain location='DomeCity' trigger='auto'`
- The next pass is cleanup and signal quality, not emergency recovery.

## Checkpoint - 2026-04-03 Clean Build Validation

- `Tools/Content Pack/2) Build Active Content (Clean)` now stands up the reduced-pack baseline without blocking the gameplay load path.
- `2026-04-03T06:14:52.362Z`: `[SingleSceneManager][LoadingStatus] percent=86 detail='Preparing UI'` while `current_location=DomeCity` and the run stayed on the staged external-content path.
- `2026-04-03T06:14:59.686Z`: `[SingleSceneManager][RevealHandoff] stage=streaming_idle_complete`
- `2026-04-03T06:15:03.577Z`: `[SingleSceneManager][RevealSettle] exit elapsed_s=3.876 stable_s=3.259 stable_frames=4`
- `2026-04-03T06:15:03.606Z`: `[SingleSceneManager][LoadingHeartbeatGap] gap_s=3.563 acceptable_s=2.000`
- The current load is no longer blocked by migration or ownership failures.
  The active runtime blocker is late reveal-window sprite work:
  offset-only atlas metadata fallbacks plus delayed unresolved slice resolves for staged Core, Form, Gear, and Slice assets.

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

## Cold-Load Contract Checkpoint - 2026-04-05

- Shared sprite cold-load state is now `Ready`, `Pending`, `Missing`, or `ExplicitEmpty`.
- Exact atlas-slice readiness now comes from exact request resolution, not from owner-atlas completion alone.
- Trimmed metadata readiness now follows the same contract:
  supported metadata that is still loading stays `Pending`, unsupported metadata families are treated as `Ready`, and only definitive failures become `Missing`.
- `SpriteWithNormals` now preserves the last committed sprite, normal texture, trimmed offset, and visibility while a replacement request is still `Pending`.
- `ExplicitEmpty` remains the only intentional clear path for sprite consumers during cold load.
- Glyphs now keep the last committed glyph visible while a replacement glyph is `Pending`, and text layout keeps fallback or cached metrics so first-load races do not collapse lines.
- Gear startup warmup and player bootstrap readiness now count commit-ready sprite samples through `SpriteWithNormals` instead of combining raw cache booleans at the manager layer.
- Atlas-backed runtime UI and font slices now queue exact-slice supplements in player builds too, instead of leaving exact slice recovery as an editor-only overlay path.
- Atlas-backed direct subasset primary loads now resolve one exact requested slice first, then let the shared supplement path fill additional requested slices; the cache no longer treats sibling slice union loads as a commit-safe primary path.
- Next rerun evidence is still required for this checkpoint:
  confirm no first-time zero-to-offset reveal before `[AnimationController][StartupSync] stage=ready`, no pending clears/collapse, and no false-ready counts during cold startup.

## Current Truth

Source: latest Unity `Editor.log` runs on `2026-03-24`, `2026-03-30`, `2026-03-31`, and `2026-04-03`.

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
- The latest `2026-03-31` run reached live gameplay for the first slice and was reported as playable.
- The latest `2026-03-31` log tail still repeats:
  `[DialogController][TryBuildTriggeredSequence] No unseen lines remain location='DomeCity' trigger='auto'`
- The latest `2026-04-03` rerun still logged `[SingleSceneManager][LoadingHeartbeatGap] gap_s=2.455` at `2026-04-03T07:10:33.811Z`, but that miss now lands earlier in the protected load window instead of during reveal.
- The same rerun reached `streaming_idle_complete` at `2026-04-03T07:11:11.609Z`, applied gameplay activation at `2026-04-03T07:11:11.624Z`, and exited reveal settle at `2026-04-03T07:11:12.234Z` with `elapsed_s=0.609`.
- The strongest remaining spikes in that rerun were `[TextureResidencyCache][CompletionDiag]` bursts during protected load:
  `2026-04-03T07:10:35.450Z total_ms=224.7 register_ms=113.1`,
  `2026-04-03T07:10:56.637Z total_ms=232.9 register_ms=116.4`,
  and `2026-04-03T07:10:59.847Z total_ms=291.4` during slice atlas finalize.
- The newest rerun after reducing registration budget still logged `[SingleSceneManager][LoadingHeartbeatGap] gap_s=2.312` at `2026-04-03T07:16:43.996Z` and still showed protected-load registration bursts:
  `2026-04-03T07:16:45.283Z total_ms=225.3 register_ms=113.2` and
  `2026-04-03T07:17:04.658Z total_ms=234.3 register_ms=117.2`.
- The newest `2026-04-03T09:07` rerun kept reveal healthy but still barely missed heartbeat:
  `2026-04-03T09:07:04.604Z gap_s=2.308`,
  `2026-04-03T09:07:41.820Z gap_s=2.069`,
  with completion followup bursts at `2026-04-03T09:07:06.210Z total_ms=224.2`,
  `2026-04-03T09:07:25.859Z total_ms=231.0`,
  and `2026-04-03T09:07:29.939Z total_ms=289.9` during `ForestRuins/road/atlas.png (finalize)`.
- The current pass now reuses cached editor-imported atlas subassets for editor atlas supplementation and stops attributing aggregate completion-followup time to `register_ms`, so the next rerun should tell us whether the real remaining cost is atlas finalize or some other completion substep.
- The newest `2026-04-03T09:12` rerun confirmed the corrected diagnosis:
  `2026-04-03T09:12:16.150Z gap_s=2.330`,
  `2026-04-03T09:12:17.542Z total_ms=224.5 register_ms=0.0`,
  `2026-04-03T09:12:37.933Z total_ms=229.9 register_ms=0.0`,
  and `2026-04-03T09:12:42.019Z total_ms=291.5` on `Assets/ContentStage/Slices/Slice_DomeCity_Imp_Base/Sprites/Environments/ForestRuins/road/atlas.png (finalize)`.
- The active mitigation now defers non-immediate environment atlas finalization while the protected loading overlay is still active, so the next rerun should show whether that environment finalize was the remaining heartbeat blocker.
- The newest `2026-04-03T09:15` rerun moved that environment finalize out of the protected window, but heartbeat still barely missed at `2026-04-03T09:15:46.980Z gap_s=2.346`.
- The remaining protected-window completion bursts are now generic followup buckets:
  `2026-04-03T09:15:48.307Z total_ms=226.4 register_ms=0.0`
  and `2026-04-03T09:16:08.730Z total_ms=230.9 register_ms=0.0`,
  with no matching environment finalize spike in the same window.
- That same run also emitted a large block of runtime editor-console logs on the startup path, including many `SpriteWithNormals` trimmed-offset normalization lines before the heartbeat and large `GearButtons` refresh bursts after load. Those logs are now gated behind `enableVerboseRuntimeConsoleLogs` so the next rerun can measure the loader without that console churn.
- That confirms per-frame registration count was not the root cause; the expensive part is the registration work itself landing inside the protected load window.
- Offset-only atlas metadata fallback logs still appear, but they are now mostly post-reveal signal cleanup rather than the current heartbeat blocker.
- The active problem is now cleanup and smoothness refinement, not a total loading failure.

Interpretation:
- First-slice stand-up is complete enough to use as the active baseline.
- `Ready` must not appear before gameplay activation and reveal settle are actually complete.
- Late pipeline stages must not borrow end-stage percent headroom before they are actually ready.
- Reveal-critical activation still needs to stay hidden behind the overlay.
- Deferred or same-location carry-over activation must not block reveal once blocking activation is done.
- The next optimization target is large completion/finalization work during the protected load window, not reveal correctness.
- Reveal correctness is now good enough that the active target is protected-overlay completion registration cost.
- Queue/completion bursts are still large enough to hurt perceived speed and smoothness.
- The strongest current root-cause signal is editor completion/finalize work landing in large protected-overlay bursts before reveal.
- The current mitigation is to reuse cached imported atlas subassets during editor supplement, defer non-critical environment atlas finalization until after the protected overlay is gone, and suppress non-essential runtime console spam during perf passes.
- Repeated debug logs should now be treated as signal-quality cleanup so future failures are easier to spot.

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

- Latest verified checkpoint: `2026-03-31`.
- `Core + Form_Base + equipped Gear_* + Slice_DomeCity_Imp_Base` reaches live gameplay and is playable.
- `DomeCity` is the current slice baseline.
- The pipeline can now be judged from runtime quality issues instead of startup failure.
- The main remaining known log noise is repeated `DialogController` "No unseen lines remain" spam for `trigger='auto'`.
- `2026-04-03`: the newest runtime performance pass identified `SpriteWithNormals` trimmed-offset logs as the dominant console spam source during live gameplay.
  Those offset logs are now gated off for perf passes, while `AssetLoadTraceMonitor` continues writing `Diagnostics/asset-load-trace-*.csv` without mirroring lifecycle messages into the Unity console.
- `2026-04-03`: the next GC pass added a dedicated `enableVerboseRuntimeConsoleLogs` gate so gameplay dialog, dialog-state, pause-dialog, gear-init, and mouse-map debug logs stay off during runtime by default while file diagnostics remain available.
- `2026-04-03`: removed LINQ-based runtime array combining from `GearController` startup so the player bootstrap path no longer allocates enumerators just to merge sprite target arrays.

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
- Environment hot cache is sourced from the location loading pipeline and staged runtime sources.
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
- Location warm content is prefab-driven through the location loading pipeline.
- Grouped gear atlases keep atlas behavior without broad packed-build fan-out, but staged grouped gear atlases resolve sprite slices from Unity importer data while runtime `.json` stays offset-only.
- Runtime `atlas.json` is authored offset data only. Build/runtime-index passes must not regenerate `packedRect` slice-definition metadata into runtime `atlas.json`, and zero-offset exports should not emit one.
- Unsupported trimmed-metadata atlas families such as `_Bounces`, `Effects`, and `Expressions` are skipped instead of queued for guaranteed-fail metadata loads.

## Active Focus

- treat `DomeCity` as the only active gameplay slice for the current stand-up pass
- ignore `Homebase` and `SunkenCave` until `Slice_DomeCity_Imp_Base` is stable
- keep the first slice stable and playable while cleanup work lands
- preload only content that is relevant to the current location, player state, and nearby threats
- keep high-value content ready before reveal
- keep loading percent monotonic and avoid visible stalls caused by fake headroom
- keep `Ready` hidden until gameplay activation and opaque reveal settle are complete
- finish location staged activation under the overlay so `FG/Dynamic` and `FG/Destruct` do not pop in after fade-out
- use `[SingleSceneManager][RevealSettle]` and `[SingleSceneManager][LoadingStatus]` to identify the real late blocker
- reduce queue bursts and completion bursts that front-load low-value work
- expand or contract warm sets based on actual relevance instead of static over-pin behavior
- reduce repeated non-actionable debug spam so real runtime problems stay visible

## Immediate Next Steps

1. Verify `Build Active Content (Smart)` / `Build Active Content (Clean)` no longer generates `standard` runtime atlas metadata or any packed-rect upgrade path for staged atlases.
2. Build and run the player, then confirm Main Menu `Esperanza`, skull, flower, and button sprites resolve through direct atlas sprite loading with no `metadata_synthesized_atlas` failures.
3. Confirm zero-offset atlas families such as Main Menu no longer emit runtime `atlas.json`, while authored grouped/trimmed atlases with non-zero offsets still retain their offset payloads.
4. Re-run `Start New Game` and `Load Game` after that invariant is stable and confirm whether `[SingleSceneManager][LoadingHeartbeatGap]` falls to `<= 2.0s`.
5. Verify `[SingleSceneManager][RevealSettle]` remains under target and does not regress while the protected-overlay completion path stays narrow.

## Diagnostics To Watch

- `[SingleSceneManager][LoadingHeartbeatGap]`
- `[SingleSceneManager][LoadingStatus]`
- `[SingleSceneManager][OptimalLoadingProgress]`
- `[SingleSceneManager][RevealSettle]`
- `[SingleSceneManager][GameplayLocation]`
- `[SingleSceneManager][EnvironmentCache]`
- `[AnimationController][StartupSync]`
- `[GearController][StartupAppearanceWarmup]`
- `[SpriteWithNormals][Offset]`
- `[DialogController][TryBuildTriggeredSequence]`
- `Application.persistentDataPath/Diagnostics/asset-load-trace-*.csv`

## What To Measure Next

1. Re-run `Start New Game` and `Load Game`.
2. Confirm the first slice remains playable after the latest checkpoint build.
3. Confirm repeated dialog "No unseen lines remain" logs stop once auto dialog is exhausted.
4. Confirm there are no `[SingleSceneManager][LoadingHeartbeatGap]` entries above `2.0s`.
5. Confirm `Preparing UI` never logs above `86%`.
6. Confirm `Preparing dialog` never logs above `94%`.
7. Confirm loading percent does not jump to `99%` before the explicit `Ready` release.
8. Confirm loading status does not reach `Ready` before gameplay activation is complete.
9. Confirm `[SingleSceneManager][RevealSettle]` does not time out and identifies any remaining blocker if reveal still holds.
10. Confirm no visible world, UI, or dialog pop-in occurs after black starts fading out.
11. If a hitch remains, correlate it first with:
   - location prefab activation
   - player first-frame preparation
   - atlas completion bursts
   - queue depth / deferred work
12. Review `asset-load-trace-*.csv` for:
   - queue/start bursts that do not match reveal-critical priorities
   - completion bursts that coincide with GC or memory climbs
   - assets discovered in memory without a matching loader event

## Do Not Regress

- Do not restore per-slice gameplay asset loads.
- Do not move critical gear/load/apply work back out of the loading overlay.
- Do not restore renderer-driving preload loops.
- Do not return to `pinAllSpawnedEnemies` as the default residency policy.
- Do not treat editor pipeline success alone as proof of runtime smoothness.

## Transition Checkpoint

- `Build Active Content (Clean)` now stages authoritative external packs directly.
- Exact duplicate local sprite payloads under `Assets/Sprites` were removed once matching external copies were verified by SHA-256.
- Hard blockers remain: legacy `Assets/Generated` references, staged project-tree content leaks, ownership violations, and active-pack audit failures.
- Esperanza grouped gear atlases are now being split into equipment-driven `Gear_*` packs discovered from grouped-atlas leaf folders.
- Active pack resolution now targets `Core + Form_Base + equipped Gear_* + Slice_DomeCity_Imp_Base` instead of staging broad grouped gear atlas roots under `Core` or `Form_Base`.
- The next validation focus is whether the packed Addressables sprite-slice risk drops once only the equipped `Gear_*` pack set is staged.
- Main-menu atlas slice resolution now prefers runtime-index-backed slice loads for atlas-backed direct requests, which restored the decorative main-menu art path in build.
- The remaining menu text issue was `FontText` dropping unresolved `Plate` glyphs during generation; glyph objects now stay in layout with cached/fallback metrics and request a relayout once their sprites finish loading.
- 2026-04-24: build font disappearance isolated to false-ready exact atlas slices (`atlas.png[atlas_16]` receiving `atlas_0`/`atlas_18`). Exact slice consumers now reject mismatched subassets, cache supplements no longer alias wrong sprites to requested slice keys, and atlas-backed primary loads use the owner atlas address.
- 2026-04-24: build menu art/text disappearance isolated to Addressables catalog subasset stripping. Runtime requests exact sprite slice keys, so the content pipeline now keeps visible subasset representations enabled for packed player builds.
- 2026-04-25: remaining menu misses isolated to Addressables exact subasset keys resolving the wrong sprite (`a1 -> e2`, `f1 -> skull2`, `atlas_3 -> atlas_1`). Atlas-backed primary loads now always load the owner atlas and select by sprite name from the loaded sprite set.
- 2026-04-25: follow-up build showed the older order-alias fix was overriding real loaded sprite names (`a1 -> s`, `f1 -> f13`, `atlas_3 -> atlas_0`). Runtime atlas aliasing now treats actual loaded sprite names as authoritative and only uses expected-order aliases for missing, non-conflicting names.
- 2026-04-25: build-time bottleneck isolated to repeated Addressables builds. Player builds were using the global Addressables preference and rebuilding content for ~11m; content pipeline also ran two chunk warmup builds before the final build. Defaults now explicitly disable Addressables-on-player-build and use one Addressables build pass.
- 2026-04-25: repeated main-menu popout traced to unsafe runtime atlas order aliases plus color replacement failures clearing already-rendered sprites. Atlas maps now reject positional aliases entirely; exact-slice misses queue/wait for runtime supplements, and failed async color resolves keep the current rendered sprite while logging the miss/mismatch.
- 2026-04-25: Load Game stall isolated to `DomeCity` prefab loading successfully, then `pipeline_location_ready=0` at `Activating location`. Location activation capacity was incorrectly blocked on global resolver idle after the scoped resolver barrier already owned shard readiness; activation capacity now gates texture queue pressure only and times out instead of owning readiness.
