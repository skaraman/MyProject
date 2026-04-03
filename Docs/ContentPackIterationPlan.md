# Content Pack Iteration Plan

## Purpose

Use this as the working loop for scaling gameplay content so builds can contain a very large asset set while day-to-day development only stages a smaller active slice.

## Current Transition Workflow

The project is currently migrating from mixed legacy content roots into authoritative external content packs under `../MyProjectContent`.

Primary daily workflow:

- `Tools/Content Pipeline/1) Build Active Content (Smart)`
- `Tools/Content Pipeline/2) Build Active Content (Clean)`

Transition workflow:

- `Tools/Content Pipeline/Transition/1) Analyze Ownership + Duplicates`
- `Tools/Content Pipeline/Transition/2) Export Missing Pack Content`
- `Tools/Content Pipeline/Transition/3) Stage Active Packs`
- `Tools/Content Pipeline/Transition/4) Audit Legacy Dependencies`
- `Tools/Content Pipeline/Transition/5) Rebuild Runtime Index`
- `Tools/Content Pipeline/Transition/6) Build Addressables`
- `Tools/Content Pipeline/Transition/7) Full Migration Pass (Smart)`
- `Tools/Content Pipeline/Transition/8) Full Migration Pass (Clean)`

Behavior rules:

- `Smart` is the default daily button and skips external export writes when the destination file already exists.
- `Clean` is the recovery button and recreates or overwrites external content when corruption or stale output is suspected.
- external pack outputs in `../MyProjectContent` are the intended authoritative destination
- staged content in `Assets/ContentStage` is the runtime/editor mirror
- duplicate content in `Assets/Sprites`, `Assets/Generated`, and `../MyProjectContent` is migration debt and must be surfaced by audit rather than silently tolerated

## Runtime Authority Rule

- authoring roots under `Assets/Sprites` and related project folders remain production/editing inputs
- `../MyProjectContent` is the authoritative exported content-pack destination
- `Assets/ContentStage` is the intentional runtime/editor mirror for active packs
- runtime resolves through active-pack ownership and staged paths first, not through the full dev tree
- duplication to reduce is legacy/source duplication across authoring roots and exported pack roots, not the existence of the stage mirror itself

## Packaging Model

This project now uses five content ownership layers:

- `Core`
- `Form`
- `Gear`
- `Slice`
- `Episode Pack`

Current concrete pack IDs:

- `Core`
- `Form_Base`
- equipped `Gear_*` packs discovered from `Assets/Sprites/Characters/Esperanza/GroupedGearAtlases/<Form>/<GearCode>/<Leaf>`
- `Slice_DomeCity_Imp_Base`
- `Slice_Homebase_Placeholder`
- `Slice_SunkenCave_Placeholder`
- `Episode_01`

Current dependency rule:

- `Episode_01 -> Slice_DomeCity_Imp_Base + Slice_Homebase_Placeholder + Slice_SunkenCave_Placeholder`
- `Slice_DomeCity_Imp_Base -> Core + Form_Base`
- active gameplay gear is additive: `Core + Form_Base + equipped Gear_* + Slice_DomeCity_Imp_Base`
- each slice depends on `Core`
- each form depends on `Core`
- each `Gear_*` pack depends on `Core`
- current stand-up scope is `Core + Form_Base + equipped Gear_* + Slice_DomeCity_Imp_Base`

## Why This Performs Well

- preload only the content that known scene flow and player choice make likely to be needed
- keep ownership narrow so runtime avoids broad scans, broad residency, and accidental fallback to the full project tree
- use `Assets/ContentStage` as the stable runtime/editor mirror so active content resolves through predictable paths
- keep the loading contract ordered:
  `player -> location -> enemies -> ui -> dialog`

### Core

`Core` is the globally shared runtime base that should exist in nearly every build and should keep always-present player/UI dependencies hot.

For your current definition, `Core` contains:

- Esperanza skin movement/state set:
  `Walk`, `Run`, `Sprint`, `Dash`, `Dodge`, `Block`, `Jump`, `JumpDouble`, `JumpLanding`, `JumpFalling`, `Stance`, `Breathe`, `Dance`
- all Esperanza `xToY` transitions for those states
- all global UI
- dialog UI
- all fonts
- main menu
- select menus
- character UI
- map UI
- Esperanza portrait expressions for all forms

### Slice

A `slice` is the smallest independently stageable gameplay unit that should run with `Core` and without the full dev project tree, and it supplies the current location/enemy/dialog payload for the scene.

For your current definition, a slice contains:

- one gameplay location
- that location prefab and warm profile
- that location's dialog snapshot
- one enemy set for that location
- one Esperanza combat form for that slice

Current example slice:

- `Location`: `DomeCity`
- `Enemy`: `Imp`
- `Esperanza form`: `Base`
- `Dialog`: `DomeCity` location dialog
- `Dialog chains`: one chain per character in `DomeCity`
- `Dialog trigger rule`: empty or `auto` plays immediately; any other trigger waits for the matching `MessageBus` message
- current slice-local Esperanza combat moves:
  `PunchRight`, `PunchLeft`, `KickRight`, `KickLeft`, `Blast`

This means the slice owns the zone-specific encounter content and location dialog, while `Core` owns the always-present player locomotion, dialog UI, and broader interface baseline.

### Form

A `form` pack is the player-choice combat layer for one Esperanza form and supplies the currently active combat payload.

Current example form pack:

- `Form_Base`

For your current definition, a form contains:

- form-specific projectile prefabs
- form-specific combat effects and VFX
- form-specific combat animation payloads that are not part of the always-present locomotion baseline

Current baseline form-owned example:

- `BlastBall`

### Gear

A `gear` pack is the player-choice visual equipment layer for Esperanza and supplies only the currently equipped visual payload.

For the current rollout:

- each equippable grouped gear atlas leaf folder becomes its own pack
- pack ids are generated from folder structure:
  `Gear_<Form>_<GearCode>_<Leaf>`
- example:
  `Assets/Sprites/Characters/Esperanza/GroupedGearAtlases/Aqua/aa/p`
  becomes `Gear_Aqua_aa_p`
- daily smart/clean builds stage only the currently equipped gear packs for the active Esperanza form
- project-wide analysis still discovers all gear packs

Current rules:

- `Skin` stays in `Core`
- form combat effects stay in `Form_Base`
- equippable grouped gear atlas leaves move to `Gear_*`
- runtime active gear pack ids are derived from the equipped gear set for the current active form
- grouped gear atlases remain atlas assets
- staged grouped gear atlas sprite slices come from Unity importer data in `.meta`
- runtime `.json` for grouped gear atlases remains an offset-only placement payload and is not the slice-definition authority
- runtime `atlas.json` must only come from authored trimmed-atlas export output; build/runtime-index steps must not regenerate `packedRect` or other slice-definition metadata into runtime `atlas.json`
- if an authored trimmed-atlas export resolves every sprite to zero placement offset, it should not emit a runtime `atlas.json`

### Episode Pack

An `episode pack` is a larger progression bundle made from multiple slices plus any shared progression spaces, and it composes multi-slice progression without becoming the runtime authority for unrelated scene-local content.

For your current definition, an episode pack contains:

- `Location1` including enemies
- `Homebase`
- `Location2` including enemies

This is the Diablo 2 style layer:

- the player keeps building one character
- the game moves through distinct zones
- each zone brings different enemies
- the character and enemies bring effects with them between encounters
- dialog progression persists with the player while authored dialog content stays owned by the relevant location slice
- current placeholder follow-up slices are `Homebase` and `SunkenCave`

## Dialog Ownership Rule

The current dialog rule is:

- every location has dialog
- every character in that location has a dialog chain
- player progress through dialog is tracked with a `seen` factor
- each dialog node has a `trigger`
- empty or `auto` trigger means auto-play chunk
- any other trigger means listen for that `MessageBus` message
- chunk order follows authored list order
- the authored dialog content lives with the location slice
- the `seen` progression state lives in save data, not in the content pack
- `Core` owns the dialog UI shell
- Esperanza expressions for all forms are `Core`
- enemy portraits are slice-owned
- ally portraits are slice-owned
- portrait ownership follows `speakerId`

### Working Validation Rule

The working test for a valid slice is:

- enable `Core` plus that slice
- run `Start New Game` and `Load Game`
- reach gameplay without hidden dependency on the full project tree
- load `player -> location -> enemies -> ui -> dialog` in a logical way

The working test for a valid episode pack is:

- enable `Core` plus the episode pack's slices
- move between its locations/homebase without needing unrelated episodes
- keep persistent character progression intact across those zones

## Codex-Automated Already

- remap core gameplay prefab paths through the staged core pack at runtime
- remap projectile prefab paths through the active staged pack set at runtime
- remap grouped gear atlas asset paths through active staged `Gear_*` packs before falling back to broader roots
- sync staged player, projectile, and location prefab Addressables entries after active-pack staging
- include projectile prefabs in the core pack seed set
- discover `Gear_*` packs automatically from grouped gear atlas leaf folders
- stage active equipped `Gear_*` packs under `Assets/ContentStage/Gears`
- add runtime content-pack summary logging
- add gameplay-core validation to content-pack audit
- fix staged warm profile registration so profiles resolve to the correct location id instead of always using `DomeCity`
- make stage-time validation enforce the same gameplay-core and pack-policy checks that audit enforces
- add a one-shot editor command for:
  `stage -> audit -> rebuild runtime index`
- add pack-policy validation for:
  location snapshot ownership, dialog snapshot ownership, warm profile ownership, and `defaultLocationId` ambiguity
- add content-pack fields to `[SingleSceneManager][LoadingStatus]` so reduced-pack runs are easier to read

## Designer Input Needed First

- define what is permanently `Core` versus what belongs to episode/level/slice packs
- decide whether Esperanza alternate forms stay in `Core` or become opt-in content packs
- decide which enemies are globally available versus location-owned
- decide the first 3-5 production slice IDs after `Slice_DomeCity_Imp_Base`
- define the default gameplay location for external-content builds
- define which projectile families are global versus enemy-owned
- define which locations should share an environment cache slot
- define the content naming rules for episodes, locations, enemies, dialogs, and warm profiles
- define the memory target tiers you actually care about, especially weakest PC / handheld / mobile-like budgets
- define what counts as reveal-critical for each slice:
  player, location BG, location FG static, nearby enemies, UI, dialog
- define what can intentionally stream after reveal without hurting perceived quality
- define what "partial development set" means in practice:
  one slice, one episode, one biome, or one milestone bundle
- decide whether a slice can depend on another slice, or only on `Core`

## Codex Can Automate Next

- add more pack definitions after you name the next slices
- add snapshot export/import for more locations and dialog sets
- move more built-in data lookups behind the active content registry
- add validation that every staged location has:
  location data, dialog data, warm profile, prefab path, and at least one enemy archetype source
- add validation that every staged dialog snapshot has speaker chains with valid seen progression keys
- add validation that every staged dialog snapshot uses valid trigger names for its authored chunks
- add validation that every runtime projectile key points at a staged or core-owned prefab
- add validation that every enemy type used by a location has stats and animation data
- add validation that every dialog portrait library resolves under the staged content roots
- add a single editor command for:
  export -> stage -> audit -> rebuild runtime index -> build Addressables
- add pack-aware audit output for assets still coming from the full dev tree
- add a report of which warm-profile entries are empty, oversized, or duplicated
- add a report of which environment cache assets are shared across slices
- add automated checks that `mainmenu` never enters gameplay environment-cache rotation
- add active-pack smoke tests for `Start New Game` and `Load Game` entry routing
- add a content registry diff tool so you can see exactly what changed between two design passes

## Likely Designer Follow-Up After Each Pass

- update content ownership:
  core vs slice
- update location pacing:
  first frame must be visible vs safe to stream later
- update which enemies belong to a location
- update dialog ordering and whether portraits are global or local
- update dialog trigger messages and chunk boundaries
- update location warm profiles after playtesting
- trim or expand environment cache candidates
- rename packs or split packs once build size and editor load become painful

## Joint Back-And-Forth Loop

1. You decide or change a slice boundary.
2. Codex updates pack definitions, snapshots, validation, and editor automation.
3. You stage and test the slice in Unity and review visual quality.
4. Codex reads the log and fixes pipeline, ownership, or staging issues.
5. You adjust art/content ownership again.
6. Codex re-audits and automates the next repeatable piece.

## Recommended Next Sequence

1. Decide the next real slice IDs and whether they are location-based or episode-based.
2. Decide the official `Core` ownership list:
   player prefab, base atlases, common UI, common fonts, common projectiles, shared effects.
3. Decide for each current gameplay asset category whether it is:
   `Core`, `Slice`, or `Derived from location prefab`.
4. Ask Codex to add those slice pack definitions and snapshot exporters.
5. Stage the chosen active pack set and run the audit.
6. Run `Start New Game` and `Load Game` with the reduced active pack set.
7. Capture the new `Editor.log`.
8. Ask Codex to correlate:
   loading status, optimal progress, reveal settle, gameplay location, environment cache, and heartbeat gaps.
9. Adjust warm profiles based on observed late blockers.
10. Repeat until the slice works cleanly with only the intended pack set active.

## Concrete Remaining Work List

- define next slice pack names
- define slice dependency rules
- define core projectile ownership
- define core/shared enemy ownership
- author speaker ids so portrait libraries resolve cleanly from `speakerId`
- define default external-build start location
- define reveal-critical asset policy by slice
- define post-reveal streaming policy by slice
- define weakest supported memory tier
- define pack naming and folder conventions
- define when an environment deserves hot-cache residency
- define whether location prefab ownership lives with location pack only
- define whether enemy archetype ownership lives with location pack or species pack
- add next slice pack definitions
- export next slice pack content
- import next slice snapshots into active content registry
- validate enemy stats + animations + projectile references for all staged packs
- validate dialog portrait libraries for all staged packs
- validate warm profiles for all staged packs
- stage active packs
- audit active packs
- rebuild runtime index
- build Addressables
- run reduced-pack smoke test
- capture logs
- tune warm scope
- tune environment cache membership
- tune UI/dialog readiness gating
- tune reveal-critical activation versus deferred activation
- repeat with a second slice
- turn `Slice_Homebase_Placeholder` into a real slice
- turn `Slice_SunkenCave_Placeholder` into a real slice

## Good Handoff Prompts For The Next Loop

- "I decided the next slice IDs are X, Y, Z. Add them to the pack pipeline."
- "These assets must stay in Core. Move validation and ownership around that."
- "This location should reveal with only BG + static FG, everything else can defer."
- "These enemies should be globally available, those others should be slice-local."
- "Audit the active packs after my content move and tell me what still depends on the full project tree."
- "I changed the warm profiles and location prefab. Read the log and retune the pipeline."
