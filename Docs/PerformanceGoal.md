# Performance Goal

Updated: 2026-07-03

## Latest Note

- 2026-07-03: current performance target is Core/main-menu boot only; gameplay scenes that expect the player, enemies, projectiles, or effects are deferred until the scene/playground structure is split.
- 2026-07-02: editor atlas authoring pass removed per-folder forced GC/unload from trim export and switched grouped atlas export to deterministic full repack from source instead of loading/reusing prior output pages

## Objective

Make the loading pipeline consistent, fast, and measurable.

Current scope:

- boot to the main menu
- validate Core UI/font/menu content
- do not require gameplay player, location, enemy, projectile, or effect content
- treat packs with gameplay ownership or gameplay asset roots as the signal that gameplay content was requested
- defer `LoadGameFlow -> DomeCity` until dedicated gameplay/playground scenes are separated from menu startup

The primary workflow for the current scope is:

```powershell
python .\Tools\ContentPackIterationUI.py --set-active Core --build-smart
```

Then run Play Mode to the main menu and verify from `%LOCALAPPDATA%\Unity\Editor\Editor.log`.

## Verified Inputs

Checked before this reset:

- `Docs\PerformanceGoal.md`
- `Docs\ContentPackCodeAudit.md`
- `Docs\ContentPackIterationPlan.md`
- `Docs\GarbageCollectionInUnity.md`
- `%LOCALAPPDATA%\Unity\Editor\Editor.log`
- `git status --short`

Latest editor-log evidence:

- `2026-07-01T04:15Z`: Unity `6000.5.1f1` batch compile passed after fixing EditorThemes path resolution noise; `Tundra build success (0.51 seconds)` and no `DirectoryNotFoundException` in the compile log.
- Unity version: `6000.5.1f1`
- compile/import state: `Tundra build success (0.51 seconds)`
- latest log end: `2026-07-01T04:15:43.503Z`
- latest run type: editor batch compile/import/domain reload/refresh, not gameplay
- latest search-cache guard: locked search cache scheduled for purge, `total_gb=4.77`, `largest_gb=4.77`
- no fresh post-reset `LoadGameFlow` runtime baseline is present in the latest editor log

Current content-pack evidence from docs:

- external root resolves from `Packages\manifest.json` to `D:\localDev\Unity\Esperanza\MyProjectContent`
- project-local package mirror is `Packages/com.skaraman.myprojectcontent`
- Python UI owns pack authoring, exact active selection, and Smart build
- Python UI treats packs as self-isolated: no pack dependency column, editor field, default dependency inference, or active-pack dependency expansion
- Python UI fallback external root is `..\MyProjectContent` instead of a hardcoded machine path
- Unity content-pack backend uses the same `..\MyProjectContent` fallback and no longer expands active packs through pack dependencies
- Unity content-pack menu actions are removed
- next runtime measurement waits on a passing Core Smart content-pack build and main-menu startup

## Performance Targets

Current Editor Play Mode target:

- main menu reaches first usable frame in under `5s`
- no missing main-menu UI sprites
- no missing font sprites
- no `SpriteRuntimeIndex/Shard/UI/Fonts` Addressables key failure
- no gameplay player, location, enemy, projectile, or effect dependency is required for menu startup
- no `[CompletionDiag]` over `50ms` during menu reveal
- no `[LoadingHeartbeatGap]` over `2.0s`

Deferred gameplay target:

- `LoadGameFlow` into `DomeCity` reveals gameplay in under `15s`
- `warm_gate` completes without hard timeout
- `unlock_gameplay_from_black` starts with `queued <= 32`
- `unlock_gameplay_from_black` starts with `in_flight <= 4`
- deferred non-critical tail remains deferred at unlock
- no `[CompletionDiag]` over `50ms` during protected overlay or reveal settle
- no `[LoadingHeartbeatGap]` over `2.0s`
- no visible first-frame missing player, active location, spawn-critical enemies, gameplay UI shell, or dialog shell

Current player-build target after editor menu flow is stable:

- main menu first usable frame in under `3s`
- no recurring menu `GC.Alloc` after warmup
- no recurring debug logs from menu frame paths during a normal performance run

Deferred gameplay player-build target:

- gameplay reveal in under `8s`
- gameplay frames stay under `16.7ms` outside intentional loading work
- no recurring gameplay `GC.Alloc` after warmup
- no recurring debug logs from frame paths during a normal performance run

## Loading Contract

One owner:

- `SingleSceneManager` owns the gameplay loading flow
- location, spawner, gear, animation, dialog, and runtime-cache systems provide inputs
- no subsystem starts a competing protected-transition warm gate
- one gameplay load creates one warm-orchestrator run

One reveal-critical model:

- reveal waits only on first visible frame requirements
- warm-plan tails can stay post-reveal unless proven first-frame critical
- logs must report both `reveal_critical_ready` and `warm_plan_critical_ready`
- timeout paths must expose the missing owner and readiness contract

One deferred-tail policy:

- pre-unlock may enqueue only reveal-critical prefixes
- full gear libraries, animation tails, dynamic location work, background sprites, and atlas expansion remain deferred
- reveal settle must not drain hundreds of non-critical requests
- any handoff threshold violation is a loading-contract failure, not a tuning issue

One location barrier:

- resolver readiness, staged activation, and streaming readiness feed one reveal barrier
- BG and FG/Static are reveal-critical
- dynamic/destructible location content is deferred unless measured as first-frame critical
- activation diagnostics name the blocking stage and target counts

One finalization budget:

- reveal-critical texture finalization must fit the protected-overlay budget
- non-critical finalization must wait until after reveal
- completion diagnostics must include address, owner/source, priority, reveal-critical flag, and finalize phase

One GC policy:

- follow `Docs\GarbageCollectionInUnity.md`
- debug logs are gated during performance runs
- avoid repeated `Regex`, LINQ, closure, string-concat, and array-returning API usage in load and frame paths
- reuse scratch lists, dictionaries, collider path buffers, and request-summary buffers

## Work Order

### Pass 1: Restore Content Bootstrap

Goal:

Make the Core content pipeline valid before measuring main-menu loading.

Run:

```powershell
python .\Tools\ContentPackIterationUI.py --set-active Core --build-smart
```

Verify:

- Smart export succeeds
- active pack stage succeeds
- staged content audit succeeds
- runtime index rebuild reports nonzero Core UI/font/menu libraries and shards
- Addressables build succeeds
- runtime index, main-menu sprites, and font sprites use package paths
- no `Assets/Generated` or `Assets/ContentStage` runtime references remain

Acceptance:

- `Editor.log` contains a complete passing Smart content-pack run
- main-menu measurement can test loading performance instead of missing content

### Pass 2: Measure One Menu Example

Goal:

Use one concrete runtime example before batch work.

Run Play Mode:

```text
Application start -> Main Menu
```

Capture from `Editor.log`:

- total menu startup time
- Core runtime index readiness
- UI/font shard readiness
- menu reveal timing
- `[CompletionDiag]` over `50ms`
- `[LoadingHeartbeatGap]` over `2.0s`
- first visible missing content, if any

Acceptance:

- one dominant elapsed-time owner is known
- no code change starts without an owner and contract failure

### Pass 3: Enforce Warm Ownership

Goal:

Prevent competing protected-transition warm runs.

Required behavior:

- `SingleSceneManager` owns the only gameplay warm gate
- `LocationLoadingPipeline` and spawner paths defer local warmup while a protected gameplay load is active
- enemy archetype inputs either contain real prefab roots or log a clear no-target reason
- warm orchestrator runs are not canceled by sibling systems during the same load

Acceptance:

- one gameplay load creates one warm-orchestrator run
- location and spawner warmups feed the same run
- equivalent sibling flows use the same owner contract

### Pass 4: Enforce Reveal-Critical Scheduling

Goal:

Keep only first-frame work on the protected path.

Required behavior:

- player current pose and first animation window are reveal-critical
- active location BG and FG/Static are reveal-critical
- spawn-critical enemy first frames are reveal-critical
- gameplay UI shell and dialog shell are reveal-critical
- full gear, full animation libraries, dynamic location content, and background warmup are post-reveal

Acceptance:

- unlock begins with `queued <= 32` and `in_flight <= 4`
- deferred tail is still deferred at unlock
- reveal settle does not process non-critical backlog

### Pass 5: Bound Finalization And GC

Goal:

Remove protected-overlay spikes and steady-state gameplay allocations.

Required behavior:

- no protected/reveal `CompletionDiag` over `50ms`
- no single non-critical finalize runs during protected overlay
- recurring gameplay paths do not allocate after warmup
- animation hitbox and bounce tween setup reuses path buffers and callback state
- normal performance runs do not emit recurring frame-path logs

Acceptance:

- no steady-state `GC.Alloc` during idle or movement after warmup
- projectile fire, enemy spawn, enemy attack, player attack, and dialog-active gameplay have measured allocation evidence
- gameplay frames remain under `16.7ms` outside intentional load work

### Pass 6: Batch Evaluate Sibling Flows

Goal:

Prove the shared contract works beyond one example.

Evaluate after `DomeCity` passes:

- new game into default gameplay
- load game into another saved location
- location transition from gameplay
- respawn or restore flow
- non-base form packs
- equipped gear packs
- placeholder or replacement slice definitions
- episode composition

Acceptance:

- all sibling flows share the same warm owner, reveal-critical model, deferred-tail policy, finalization budget, and GC policy
- failures route through the shared resolver, cache, readiness, or staging contract
- no per-feature patch is accepted unless the issue is proven isolated

## Fresh Status

### Current Baseline

Status: empty.

Fill after the next verified run.

```text
date:
flow:
target:
content build result:
runtime result:
total flow ms:
dominant owner:
blocking contract:
next fix:
```

### Active Findings

Status: active.

```text
1. Import settings are mostly speed-friendly: Asset Pipeline v2, async shader compile on, crunch off, mipmaps off on almost all textures, readable off on almost all textures.
2. Cache/Accelerator is disabled. Local Library cache is active; cross-project/team reimport cache is not.
3. Current editor open spent 211.258s in Asset Database refresh. Dominant import cost is sliced Esperanza sprite textures: Dance 1108 imports / 1120.0 worker-sec, To 283 imports / 711.8 worker-sec.
4. Top slow texture was Assets/Sprites/Characters/Esperanza/To/Base/ac/t/t.png at 4.078s with 1056 sprite rects.
```

### Completed This Pass

Status: in progress.

```text
1. Tools\ContentPackIterationUI.py removed pack dependency display, editing, manifest writes, default inference, and active-selection expansion.
2. Tools\ContentPackIterationUI.py removed inferred gameplay planned rows and now lists `slices[].packs` plus actual external pack folders/manifests.
3. ContentPackPipeline removed hardcoded external-root fallback, pack dependency expansion, dependency manifest writes, and placeholder/episode planned packs.
4. Tools\ContentPackIterationUI.py fallback external root is `..\MyProjectContent`.
5. Assets\ContentManifest.json now uses explicit `slices` and `episodes`; `Core` contains only `Core`, and `DomeCity_Imp_Base` lists `Form_Base`, `DomeCity`, and `Imp`.
6. Tools\ContentPackIterationUI.py can create, edit, list, and remove ContentManifest slices and pack ids from the UI or CLI.
7. Tools\ContentPackIterationUI.py removed category from the pack/source UI and added double-click source browsing plus source preview.
8. Verified sprite library pack loading contract: one `.spriteLib` authoring source seeds dependency export, staged libraries are discovered under `Sprites\SpriteLibraries`, staged textures under `Sprites`, and runtime index rows emit full `Assets/.../atlas.png[spriteName]` sprite addresses instead of `internalIDToNameTable` ids.
9. Tools\ContentPackIterationUI.py double-click now opens the pack editor for source add/edit/remove, and the main window Verify button checks the selected pack manifest/source contract before Smart build.
10. Form packs now own form-specific UI, items, main-character form animations, attack payloads, effects, projectiles, and other form-specific objects; the active DomeCity example now selects `Core,Form_Base,DomeCity,Imp`.
11. Tools\SpriteLibraryMultiEditor.py added multi-library `.spriteLib` editing with drag/drop category and label copy/move for form library rebuilding.
12. Tools\SpriteLibraryMultiEditor fixed tree identity, preview GUID resolution, dirty/save updates, and internal drag/drop category/label copying across open `.spriteLib` documents.
13. Tools\SpriteLibraryMultiEditor\create_empty_label_libraries.py creates empty `Category_Label.spriteLib` assets from a source library.
14. Tools\ContentPackIterationUI.py Add Library Source now accepts multiple `.spriteLib` files and auto-fills target folders.
15. Tools\ContentPackIterationUI.py can delete packs from the external root and clears active-selection plus manifest-slice references.
16. Tools\newTools.py split into Tools\newTools_lib modules under 500 lines.
17. GroupAtlasWindow blank output names now export numbered atlas pages: `1.png`, `2.png`, etc.
18. GroupAtlasWindow source atlas picker added Auto Find for underscore-key folder lookup.
19. GroupAtlasWindow Auto Find now supports `root/*/Aqua/aa/t` and `root/*/t/Aqua_aa` while preserving root-relative category parsing.
20. TrimmedAtlasExporterWindow resolves folder export metadata reimport, source cleanup, and memory cleanup per folder so one sibling failure does not suppress cleanup for successful folders.
21. TrimmedAtlasExporterWindow now treats same-name `.json` sidecars as existing trimmed atlas outputs so folder export skips them as inputs and overwrites them as targets.
22. TrimmedAtlasExporterWindow source cleanup now deletes packed source atlases through one batch delete contract, resolves asset paths from the project root, and logs delete/failure counts.
23. TrimmedAtlasExporterWindow now writes full editor import sidecars before asset import and persists importer metadata without the second forced atlas reimport.
24. GroupAtlasWindow now uses the same single-import sidecar contract, avoiding a second forced grouped-atlas reimport after export.
25. TrimmedAtlasExporterWindow now validates every exported atlas target and import-metadata contract before deleting source atlases, so overwrite cleanup cannot leave a processed folder empty.
```

### Runtime Measurements

Status: empty.

```text
total_flow_ms:
location_activation_ms:
warm_gate_ms:
warm_requests:
reveal_critical_ready:
warm_plan_critical_ready:
pre_unlock_ms:
unlock_queued:
unlock_in_flight:
unlock_deferred:
reveal_settle_ms:
completion_diag_over_50ms:
heartbeat_gap_over_2s:
visible_missing_content:
```

### GC And Frame Measurements

Status: empty.

```text
profiler_source:
idle_gc_alloc:
movement_gc_alloc:
player_attack_gc_alloc:
projectile_spawn_gc_alloc:
enemy_spawn_gc_alloc:
enemy_attack_gc_alloc:
dialog_active_gc_alloc:
worst_gameplay_frame_ms:
recurring_logs:
```

## Do Not Do

- do not measure gameplay loading before content bootstrap is valid
- do not patch individual missing Addressables keys before the manifest/staging contract is proven
- do not add another Unity content-pack menu workflow
- do not reintroduce `Assets/Generated` or `Assets/ContentStage` as runtime content roots
- do not hide readiness mismatches behind longer timeouts
- do not optimize broad preload breadth before identifying the dominant owner from logs

## Sprite Library Multi Editor

- 2026-06-10: fixed pane ownership so the category tree is added, packed, and visible after library selection.
- 2026-06-10: preserved treeview caret open/closed states on UI refresh.
- 2026-06-10: implemented undo/redo (Ctrl-Z / Ctrl-Y / Ctrl-Shift-Z) with automated state snapshots.
- 2026-06-10: allowed right click on library (document) in tree or listbox to delete labels without specific prefix from all categories.
- 2026-06-11: fixed tree drag selection so shift-selected categories and labels stay grouped when dragging from a selected row.
- 2026-06-11: added document right click Move and Suffix to merge other loaded libraries into the selected library with source-name suffixes.
- 2026-06-14: added right click Add Category and Rename Category actions to Sprite Library Multi Editor.
