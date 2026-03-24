# Vertical Slice Findings

Date: 2026-03-22

Scope audited:
- GDD at `Assets/Scripts/GDD.md`
- current gameplay, location, combat, XP, and UI code
- current scene/prefab wiring where it materially affects the requested slice

Target slice:
1. Esperanza enters a location
2. short dialog from Esperanza appears as written text
3. enemy also speaks
4. dialog clears and gameplay begins
5. player fights enemy, health bars change from attacks, damage numbers appear
6. player can attack and change forms
7. enemy defeat grants gameplay XP and can trigger level-up presentation
8. location goals complete and show victory screen
9. transition to safehouse
10. safehouse has talkable NPCs and a teleport to current location + 1

## Overall Verdict

The slice is not vertically complete yet.

What exists now is good lower-level scaffolding:
- startup/gameplay bootstrap
- location loading
- enemy spawning
- player attack input
- form switching
- per-form XP/state
- pause-menu form/stat presentation

What is missing is the end-to-end gameplay consequence chain:
- no runtime dialog sequence system
- no combat health/damage/death loop
- no enemy defeat event
- no objective completion tracker
- no victory flow
- no safehouse/progression loop

## Slice Status

### 1. Enter a location
Status: `Present`

Evidence:
- Player bootstrap exists in `Assets/Scripts/Input/SingleSceneManager.cs:1478`
- New-game runtime state init exists in `Assets/Scripts/Game/Character/CharacterState.cs:74`
- Load-game runtime state apply exists in `Assets/Scripts/Input/SingleSceneManager.cs:2271`
- Location loading exists in `Assets/Scripts/Game/Quests/LocationManager.cs:117`
- Gameplay spawn gate sends `ReadyForSpawns` in `Assets/Scripts/Input/SingleSceneManager.cs:2220`
- Enemy spawner listens for that in `Assets/Scripts/Game/Enemies/Spawner.cs:33`
- `DomeCity` location data exists in `Assets/Scripts/Data/Locations.cs:227`
- `DomeCity` prefab has spawn rules in `Assets/Prefabs/Locations/DomeCity.prefab:3974`

Notes:
- The location system is real.
- Content breadth is still tiny: only `mainmenu` and `DomeCity` are defined in `Assets/Scripts/Data/Locations.cs:210-236`.

### 2. Esperanza speaks as written text
Status: `Missing`

Evidence:
- Repo search found no dialogue runtime/controller implementation.
- Only a generated input action map exists for dialog progress in `Assets/Scripts/Util/Input/TestActions.cs:1757-1758`.
- The scene contains a dialogue-looking sprite asset reference at `Assets/Scenes/MyCurrent.unity:162893`, but that is only `SpriteWithNormals` art, not a sequence/presentation system.

Root cause:
- There is no system that owns dialog lines, speaker identity, typewriter timing, show/hide state, or progression.

### 3. Enemy speaks too
Status: `Missing`

Evidence:
- Same root issue as above.
- No enemy speech/dialog presentation code was found in scripts, scenes, or prefabs.

### 4. Dialog clears and gameplay starts
Status: `Partial`

Evidence:
- Gameplay startup and enemy spawn flow are present.
- Dialog gating is not present.

Interpretation:
- The project can enter gameplay.
- It cannot currently do the requested narrative handoff from intro dialog to active combat because the dialog stage does not exist.

### 5. Fight enemy with health bars affected by attacks
Status: `Missing`

Evidence that attack input/hit detection exists:
- Attack input exists in `Assets/Scripts/Input/GameplayInput.cs:91-94`
- Attack handling exists in `Assets/Scripts/Input/GameplayInput.cs:516`
- Player form-wheel toggle exists in `Assets/Scripts/Input/GameplayInput.cs:101`
- Hitbox forwarding exists in `Assets/Scripts/Game/Combat/HitBox2D.cs`
- Hurtbox validation exists in `Assets/Scripts/Game/Combat/HurtBox2D.cs`
- Player prefab has hit/hurt boxes in `Assets/Prefabs/Characters/ESPER.prefab:8117` and `Assets/Prefabs/Characters/ESPER.prefab:26334`
- Imp prefab has hit/hurt boxes in `Assets/Prefabs/Enemies/Imp.prefab:123` and `Assets/Prefabs/Enemies/Imp.prefab:4341`

Evidence that damage/health consequences are missing:
- Enemy hurtbox `OnHit` has no listeners in `Assets/Prefabs/Enemies/Imp.prefab:4343-4345`
- Player hurtbox `OnHit` has no listeners in `Assets/Prefabs/Characters/ESPER.prefab:8119-8121`
- No runtime `currentHealth`, `maxHealth`, `TakeDamage`, `ApplyDamage`, `Die`, or similar gameplay health state was found in scripts
- `HealthBarControl` displays aggregate stat totals, not combat HP state, in `Assets/Scripts/Game/Character/HealthBarControl.cs:43` and `Assets/Scripts/Game/Character/HealthBarControl.cs:48`

Root cause:
- Hit validation exists, but nothing subscribes to those hits to reduce HP, update an enemy bar, kill the enemy, or damage the player.

### 6. Damage numbers appear on hit
Status: `Missing`

Evidence:
- No `DamageNumber`, floating combat text, or equivalent runtime system was found in scripts, scenes, or prefabs.

### 7. Esperanza can attack and change forms
Status: `Present`

Evidence:
- Attack input and mapped attack playback are in `Assets/Scripts/Input/GameplayInput.cs:91-94` and `Assets/Scripts/Input/GameplayInput.cs:516`
- Shared runtime form switch path is in `Assets/Scripts/Game/Character/CharacterState.cs:102`
- Pause menu uses that path in `Assets/Scripts/Input/PauseMenuInput.cs:343`
- Gameplay wheel uses that path in `Assets/Scripts/Input/GameplayFormWheelController.cs:123`
- Forms wheel object exists in the scene at `Assets/Scenes/MyCurrent.unity:173274`

Note:
- This is one of the stronger parts of the current slice.

### 8. Enemy defeat grants XP and can trigger level-up presentation
Status: `Partial`

What exists:
- Per-form XP progression exists in `Assets/Scripts/Game/Character/CharacterState.cs:130`
- Level curve progression exists in `Assets/Scripts/Game/Character/CharacterState.cs:420`
- Pause-menu XP/level display exists in `Assets/Scripts/Util/UI/PauseMenuFormProgressView.cs`
- Manual placeholder XP grant exists in `Assets/Scripts/Game/Enemies/EnemyController.cs:265`

What is missing:
- No enemy death flow calls XP grant automatically
- No level-up animation/text presentation system was found in scripts, scenes, or prefabs

Root cause:
- XP progression exists only as state mutation right now.
- The slice still lacks the gameplay event that should award it and the presentation that should celebrate it.

### 9. Location goals complete and show victory screen
Status: `Missing`

Evidence:
- Objective data types exist in `Assets/Scripts/Data/Locations.cs:7-46`
- `LocationInfo` stores objectives in `Assets/Scripts/Data/Locations.cs:145-167`
- `LocationManager` exposes `CurrentObjectives` in `Assets/Scripts/Game/Quests/LocationManager.cs:68`
- `DomeCity` defines objectives in `Assets/Scripts/Data/Locations.cs:235-236`

What is missing:
- No runtime objective tracker consumes `CurrentObjectives`
- `LocationEnemyData.totalKills` exists only as data in `Assets/Scripts/Data/Locations.cs:248`
- No victory UI/screen implementation was found
- No completion transition from combat success to another section/location was found

Root cause:
- Objectives are currently data-only.

### 10. Transition to safehouse after victory
Status: `Missing`

Evidence:
- No `safehouse` location exists in `Assets/Scripts/Data/Locations.cs`
- Defined locations are only `mainmenu` and `DomeCity` in `Assets/Scripts/Data/Locations.cs:210-236`
- No safehouse transition logic was found in scripts

### 11. Safehouse NPCs can be talked to
Status: `Missing`

Evidence:
- No NPC interaction/talk system was found in scripts
- No dialog runtime system exists to support NPC conversation

### 12. Safehouse teleport allows current location + 1 selection
Status: `Missing`

Evidence:
- No teleport/travel/progression system was found in scripts
- No location progression map exists beyond the two hardcoded locations in `Assets/Scripts/Data/Locations.cs`

## Root Causes

### Combat Consequences Layer Is Missing

The codebase has input, animation, hitboxes, hurtboxes, and enemy spawning, but it does not yet have the layer that makes combat matter:
- combat HP state
- damage application
- death/despawn handling
- enemy kill event
- player damage intake
- health bar updates from current HP instead of stat totals

This is the main blocker for the slice.

### Narrative Layer Is Missing

There is no runtime system for:
- line sequencing
- speaker switching
- on-screen text reveal
- dialog progress input ownership
- enter/exit gameplay gating around dialog

### Objective/Victory Layer Is Missing

Location objectives exist as data, but there is no runtime evaluator that:
- tracks kills/survival time
- marks goals complete
- shows victory UI
- advances the world state

### World Progression Layer Is Missing

The current location catalog is not enough for the requested slice:
- no safehouse location
- no NPC loop
- no teleport progression UI/system
- no `current location + 1` progression model

## Shortest Path To A Real Vertical Slice

### Must Build First

1. Add a minimal combat state component for both player and enemy
- current HP
- max HP from stats/base data
- `TakeDamage`
- death event

2. Subscribe hurtboxes to damage handlers
- enemy hurtbox hit reduces enemy HP
- player hurtbox hit reduces player HP

3. Add death/despawn flow
- enemy death animation or immediate despawn
- spawn manager notified
- XP granted through real death flow, not inspector button

4. Replace stat-total health HUD with combat HP HUD
- current HP / max HP
- enemy HP bar if required for the slice

### Then Build

5. Add a tiny intro dialog sequencer
- fixed line list for Esperanza and one enemy
- text box show/hide
- typewriter or instant text
- progress input
- callback into gameplay start

6. Add damage number popup on confirmed hit

7. Add level-up presentation
- listen for `GrantFormXp` result or a new `formLeveledUp` event
- show text/animation only when a level is actually gained

8. Add objective tracker
- final kill count
- survival timer
- location completion

9. Add victory flow
- victory overlay
- continue button or timed transition

10. Add safehouse content
- `safehouse` location data
- simple NPC interactables
- teleport interactable with next-location selection

## Practical Read On The Current Project

If you launch the game now, the likely result is:
- you can start and enter `DomeCity`
- enemies can spawn
- Esperanza can move, attack, and change forms
- collisions can occur
- but combat does not resolve into damage, death, XP-by-kill, victory, or safehouse progression

That means the current project is horizontally broad in systems, but the vertical slice breaks exactly where combat outcomes and game progression should begin.

## Audit Notes

- Latest checked Unity editor log path: `%LOCALAPPDATA%\\Unity\\Editor\\Editor.log`
- Latest checked editor log timestamp during this audit: `2026-03-22 8:07:16 PM`
- This document is a static code/scene audit, not a claim that the slice was fully playtested end to end
