# Content Pack Code Audit

Reevaluated: 2026-06-09

## Current Status

The loading-performance goal is still blocked at Pass 1: restore a valid content bootstrap before measuring `LoadGameFlow -> DomeCity`.

Latest verified local data:

- `python .\Tools\ContentPackIterationUI.py --list` reports planned rows for `Core`, `Form_Base`, `DomeCity`, and `Imp`.
- `python .\Tools\ContentPackIterationUI.py --manifest-list` reports `Core -> Core` and `DomeCity_Imp_Base -> Form_Base, DomeCity, Imp`.
- `Assets\Editor\ContentPackSelection.asset` currently has `activePackIds: []`.
- `D:\localDev\Unity\Esperanza\MyProjectContent` has no discovered `ContentPackManifest.json` files.
- `python -m py_compile Tools\ContentPackIterationUI.py` passes.
- Latest `%LOCALAPPDATA%\Unity\Editor\Editor.log` is editor compile/import/shutdown, not a Smart build or gameplay run. Useful evidence: Unity `6000.4.10f1`, Tundra build success in `1.37s`, `ContentPackSelection.asset` and `ContentManifest.json` imported, search cache guard scheduled purge for locked `4.77GB` cache, then editor shutdown at `2026-06-09T06:10:16`.

## Top Blockers

1. The old slice-id command is invalid against the exact-pack contract:

```powershell
python .\Tools\ContentPackIterationUI.py --set-active Slice_DomeCity_Imp_Base --build-smart
```

`Slice_DomeCity_Imp_Base` is a manifest slice id, not a pack id in the current model. The actual pack ids for that slice set are `Core`, `Form_Base`, `DomeCity`, and `Imp`. Because active selection is exact and no longer expands slices or dependencies, that command will not stage the intended content.

Use this shape once the packs are authored:

```powershell
python .\Tools\ContentPackIterationUI.py --set-active Core,Form_Base,DomeCity,Imp --build-smart
```

2. External pack authoring has not happened yet. `Core`, `Form_Base`, `DomeCity`, and `Imp` are listed from `Assets\ContentManifest.json`, but their external folders/manifests are missing, so the UI correctly marks them `Planned missing`.

3. The next Smart build would still be a content-contract test, not a runtime loading measurement. No current log contains a complete passing Smart export/stage/audit/runtime-index/Addressables run after this reset.

## Implemented Contracts

Python tool:

- Resolves external root from `Assets\Editor\ContentPackSelection.asset`, then `Packages\manifest.json`, then `..\MyProjectContent`.
- Lists explicit `Assets\ContentManifest.json` pack ids plus any actual external pack folders/manifests.
- Creates and edits external `ContentPackManifest.json` files with `authoringSources`.
- Removes old pack dependency manifest writes from authored pack manifests.
- Supports ContentManifest slice creation/edit/removal from UI and CLI.
- Main window double-click opens the pack editor for add/edit/remove source rows.
- Main window `Verify` checks selected pack manifest/source contract before Smart build.
- Form-pack inference now treats form UI, item icons, main-character form animation payloads, attacks, effects, projectiles, and other form-specific objects as `Form_*` pack candidates.
- `Build Smart` invokes Unity batchmode method `ContentPackPipeline.BuildActiveContentSmart`.

Unity content-pack backend:

- Resolves `com.skaraman.myprojectcontent` from `Packages\manifest.json` to `D:\localDev\Unity\Esperanza\MyProjectContent`.
- Uses `Packages/com.skaraman.myprojectcontent` as the staged package root.
- Builds pack definitions from real external folders and explicit `Assets\ContentManifest.json` pack ids.
- Reads existing `ContentPackManifest.json` `authoringSources` and uses them as seed/owned roots.
- Validates authoring source existence and type; direct PNG sources require a sprite-slice label that exists in Unity importer data.
- Stages active packs by exact active pack ids only.
- Fails selected unknown pack ids before falling back to an inactive registry.
- Updates active runtime Addressables for player, projectiles, locations, and runtime materials during staging.
- Searches staged active roots for sprite libraries and textures while external content is enabled.
- Runs the Smart pipeline in eight steps: ownership analysis, export, stage, legacy audit, unified import, runtime index rebuild, hotset, Addressables build.

## Contract Gaps

- The UI Verify checks manifest presence, source rows, source file existence, type, and duplicate target mapping, but direct PNG slice-label proof still depends on Unity importer data during Smart build.
- No Clean workflow should be added as a normal action yet. Keep one primary Smart action and add a cleaning variant only if stale output is proven.

## Next Required Action

Create one real pack first, then verify the end-to-end content bootstrap.

Recommended first example:

1. Open `Tools\ContentPackIterationUI.py`.
2. Create or edit `Core`, `Form_Base`, `DomeCity`, or `Imp`.
3. Add concrete source rows and save the external pack manifest.
4. Use `Verify` on the pack.
5. After all selected packs exist, run:

```powershell
python .\Tools\ContentPackIterationUI.py --set-active Core,Form_Base,DomeCity,Imp --build-smart
```

Acceptance for moving on to gameplay measurement:

- Smart export succeeds.
- Active pack stage succeeds.
- Staged content audit succeeds.
- Runtime index rebuild reports nonzero libraries and shards.
- Addressables build succeeds.
- Editor log contains no missing selected-pack directories, no `Assets/Generated` runtime references, and no staged dependency leaks.

## Do Not Measure Yet

Do not run gameplay loading performance conclusions until a passing Smart content-pack run exists in `Editor.log`. Current evidence only proves the editor imported the selection/manifest and the Python UI can enumerate planned packs.
