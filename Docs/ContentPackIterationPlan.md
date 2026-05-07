# Content Pack Iteration Plan

## Authoring Workflow

Artists place images into `Assets/Sprites` organized by source context (environment, enemies, UI, etc.). The content pipeline then packs these assets into external content packs based on their folder structure.

### Daily Artist Workflow

1. **Place new art in Assets/Sprites**
   - Organize by semantic category: `Environment/`, `Enemies/`, `UI/`, `Characters/`
   - Example paths:
     - `Assets/Sprites/Environments/DomeCity/Walls.png`
     - `Assets/Sprites/Enemies/Imp/AttackSprites.png`
     - `Assets/Sprites/UI/MainMenu/ButtonAtlas.png`

2. **Pack assets into external content packs**
   - Use the Tools menu to export assets from project-local to external root:
     - `Tools/Content Pipeline/Export Selected Assets` (drag-and-drop selection)
     - `Tools/Content Pipeline/Build Active Content (Smart)` (packs currently active pack set)
     - `Tools/Content Pipeline/Transition/2) Export Missing Pack Content` (fills gaps in existing packs)

3. **Verify external exports**
   - Check that assets appear in the correct external folder structure:
     - `D:\localDev\Unity\MyProjectContent\Slices\Slice_DomeCity_Imp_Base\Sprites\Environments\DomeCity\Walls.png`
     - `D:\localDev\Unity\MyProjectContent\Core\Sprites\UI\MainMenu\ButtonAtlas.png`

### Pack Assignment Rules (Automated)

The pipeline assigns assets to packs based on source folder hierarchy:

| Source Folder Pattern | Target Pack | External Path |
|----------------------|-------------|---------------|
| `Assets/Sprites/Environments/<Location>/<Subfolder>` | Slice pack | `../MyProjectContent/Slices/<SlicePack>/Sprites/Environments/<Location>/<Subfolder>` |
| `Assets/Sprites/Enemies/<EnemyType>/<Subfolder>` | Slice pack | `../MyProjectContent/Slices/<SlicePack>/Sprites/Enemies/<EnemyType>/<Subfolder>` |
| `Assets/Sprites/UI/<Category>` | Core pack | `../MyProjectContent/Core/Sprites/UI/<Category>` |
| `Assets/Sprites/Characters/Esperanza/*` | Gear/Form packs | `../MyProjectContent/Gears/<GearPack>/Sprites/...` or `../MyProjectContent/Forms/<FormPack>/Sprites/...` |

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
- that location prefab 
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
- validate location load loop for all staged packs
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
- "I changed the location prefab. Read the log and retune the pipeline."


<!-- 1. Fix ContentPackPipeline.cs (The Source)
In your loading/content pipeline, you must ensure that the pendingWarmGateRuntimeLoadQueue is not just populated, but actually drained and resolved before the gameplay scene is considered "Loaded." You need a completion callback from the Addressables handles in pendingLoads that triggers a final Pump() call.

2. Implement a "Loading" state in FontText (The Consumer)
Modify your FontText or SpriteWithNormals script to check if its required address is still in the pendingLoads or pendingWarmGateRuntimeLoadQueue. If it is, the component should remain invisible or show a placeholder until the resolver reports IsCommitReady().

3. Check PumpDeferredRuntimeLoads implementation
You should look at the code inside PumpDeferredRuntimeLoads. It needs to be robust enough to handle the case where an Addressable load is still Incomplete. If it's just checking if a handle exists rather than checking .IsDone, you'll get exactly this "partial rendering" behavior. -->