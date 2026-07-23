# Game Design Document (GDD)

Version: 0.1  
Last Updated: 2026-03-31  
Project Root: `Assets/Scripts`

## 1) Game Vision
Build a 2D action RPG combat experience with smooth, high-FPS animation streaming, gear-driven character appearance, and scalable enemy encounters across locations.

Core requirement: no visible freeze/unfreeze or blank sprite frames during animation changes.

## 2) Player-Facing Product Flow

### Main Menu
- `Start`: Load default/preset settings, spawn player in first location, spawn enemies, enter combat.
- `Load`: Load saved settings and saved location state, spawn player and enemies, enter combat.
- `Settings`: Audio, visual, and controls configuration.

### Gameplay
- Player combat + movement + jumping with transition animations.
- Gear can be changed from menu screens only.
- Fade-to-black transition is available between gameplay and gear menu, and can be used as a loading gate.

## 3) Gameplay Content Scope

### Player Animation Groups
- Attacks: 6 total
- Movement: 4 total (`run`, `sprint`, `walk`, `stance`)
- Jumping: 4 total (`jump`, `double jump`, `fall`, `land`)
- Transition support: each group uses `To`/transition animations

### Character Appearance System
- All outfit parts have corresponding sprite animation data for the animation groups above.
- Only equipped gear sets need to be actively loaded.
- Gear changes happen in menu context, not live during core combat loop.
Esperanza has forms -
each Form has its own ablities and equipment
when Esperanza changes forms her abilities and equipment change to that form
All forms have these default animations -
Walk
Run
Sprint
Stance
Breathe
Jump
JumpDouble
JumpLanding
JumpFalling
Dance
Block
Dodge
and all the defined x_To_Y for these at Interrupts.cs where X and Y are one of the animations above (eg WalkToRun)
Forms have 8 unique moves which can vary depending on player choice but they will always only be 8

### Enemy System
- Enemies do not change gear.
- Each enemy archetype loads only its current appearance set.
- Many copies of the same enemy may be active simultaneously.

### Effects System
- Player and enemy attack animations have associated VFX animations (Effects).
- Effects must be streamed and warmed like core animation sprites.

### Dialog System
- Every gameplay location has authored dialog content.
- Each character in that location owns a dialog chain.
- Dialog progression is driven by player `seen` state so repeat visits can continue from the next unseen line instead of replaying first-contact dialog.
- Each dialog node has a `trigger` field.
- Empty `trigger` or `auto` means that node belongs to the next auto-play chunk.
- Any other `trigger` means that chunk waits for the matching `MessageBus` message.
- Chunk order is determined by authored list order, not by re-sorting at runtime.
- The runtime progression key is effectively:
  `locationId + speakerId + lineNumber`
- Dialog UI belongs to `Core`; location dialog data belongs to the location's slice.
- Portrait ownership follows speaker ownership:
  Esperanza expressions for all forms are `Core`; enemy portraits are slice-owned; ally portraits are slice-owned.
- Portrait library ownership follows `speakerId`.

## 4) Content Packaging Model

The game structure is closer to Diablo 2 than to a purely level-by-level arcade game:

- the player makes and progresses one character
- the game moves through distinct zones
- each zone has its own enemies
- the character and enemies bring effects with them into combat

That leads to three packaging layers for content loading and build scaling.

Current concrete pack IDs:

- `Core`
- `Slice_DomeCity_Imp_Base`
- `Slice_Homebase_Placeholder`
- `Slice_SunkenCave_Placeholder`
- `Episode_01`

Current dependency rule:

- `Episode_01 -> Slice_DomeCity_Imp_Base + Slice_Homebase_Placeholder + Slice_SunkenCave_Placeholder`
- each slice depends on `Core`
- current stand-up scope is `Core + Slice_DomeCity_Imp_Base` only

### Core

`Core` is the shared baseline that should exist in nearly every build.

Current `Core` definition:

- Esperanza skin movement/state set:
  `Walk`, `Run`, `Sprint`, `Dash`, `Dodge`, `Block`, `Jump`, `JumpDouble`, `JumpLanding`, `JumpFalling`, `Stance`, `Breathe`, `Dance`
- all Esperanza `xToY` transition animations for those states
- all UI
- dialog UI
- fonts
- main menu
- select menus
- character UI
- map UI
- Esperanza portrait expressions for all forms

Design meaning:

- `Core` owns the persistent player baseline and global interface
- `Core` should not need to know about a specific zone's enemy roster
- `Core` should stay stable even as episodes and zones grow

### Slice

A `slice` is the smallest independently stageable gameplay unit that should run with `Core`.

Current `Slice` definition:

- one location:
  `DomeCity`
- one enemy set:
  `Imp`
- one Esperanza combat form:
  `Base`
- slice-local Esperanza combat moves:
  `PunchRight`, `PunchLeft`, `KickRight`, `KickLeft`, `Blast`
- location dialog for `DomeCity`
- per-character dialog chains for the characters in `DomeCity`
- slice-local dialog portraits for slice-local speakers

Design meaning:

- a slice is effectively one playable zone encounter package
- it owns the zone, the local enemy content, and the local player combat form content needed there
- it also owns the location dialog content that advances while the player revisits that zone
- `Core + one slice` should be enough to boot into gameplay cleanly

### Episode Pack

An `episode pack` is a progression bundle built from multiple slices plus any shared progression space.

Current `Episode Pack` definition:

- `Slice_DomeCity_Imp_Base` for `DomeCity`
- `Slice_Homebase_Placeholder` for `Homebase`
- `Slice_SunkenCave_Placeholder` for `SunkenCave`

Design meaning:

- an episode pack is the player-facing chunk of progression
- slices are the technical/staging unit
- episode packs are the higher-level campaign unit

### Loading Consequence

For loading and staging, this means:

- `Core` should carry the persistent player/UI baseline
- `Slice` should carry zone-specific gameplay content
- `Slice` should also carry the location dialog snapshot and any slice-local speaker portraits
- `Episode Pack` should group multiple slices for progression without collapsing slice-level ownership
- effects should follow the owner that introduces them:
  player-carried effects with player/form ownership, enemy-carried effects with enemy or slice ownership

## 5) Runtime Asset Strategy (Authoritative)
Algorithm in use:
- `Appearance-set streaming`
- `Prewarm gate on animation switch`
- `Pinned hotset with owner classes`

Policy:
- Pin scope: `Player + All Spawned Enemies + All UI`
- Set breadth: `Current + Next windows` (+ bounded predicted interrupts)
- Pressure policy: `Protect Player Pins` (demote Enemy/UI/Effect first)

## 6) Loading Plan by Scenario

### A) Start Game
- During location loading/fade:
  - Warm player equipped appearance set.
  - Warm first-location enemy archetypes.
  - Warm common first-use effects.
- Enter gameplay after warm gate target is satisfied (or timeout fallback).

### B) Load Save
- During loading/fade:
  - Warm player saved gear appearance set.
  - Warm saved-location enemy archetypes.
  - Warm common effects for the loaded state.
- Enter gameplay after warm gate target is satisfied (or timeout fallback).

### C) Gear Menu Open/Close
- On menu open: pause/slow gameplay state as needed.
- On gear apply:
  - Warm newly equipped appearance set behind fade/loading overlay.
  - Return to gameplay after first-frame readiness gate.

### D) Enemy Spawn Waves
- Spawned enemies participate in owner-based pinning automatically.
- Under pressure, enemy pins are demoted before player pins.

### E) UI
- UI sprite targets are pinned with throttled refresh.
- UI owners are released when inactive/destroyed.

## 7) Save/Load Requirements
- Save data must include:
  - Location/location
  - Equipped gear per slot/form
  - Relevant gameplay state needed for immediate combat resume
- Dialog progression state per location and speaker so revisits continue from the next unseen line
- Load flow must drive prewarm using saved appearance/location context before unpausing gameplay.

## 8) Performance and Quality Targets
- Transition-frame p99: `<= 16.7 ms` on baseline hardware.
- Hard spike cap after warm cycle: no single transition `> 25 ms`.
- No blank sprite flashes during animation switch windows.
- No unresolved mapping spam in normal gameplay.
