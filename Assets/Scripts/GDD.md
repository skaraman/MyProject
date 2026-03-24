# Game Design Document (GDD)

Version: 0.1  
Last Updated: 2026-02-24  
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

### Enemy System
- Enemies do not change gear.
- Each enemy archetype loads only its current appearance set.
- Many copies of the same enemy may be active simultaneously.

### Effects System
- Player and enemy attack animations have associated VFX animations (Effects).
- Effects must be streamed and warmed like core animation sprites.

## 4) Runtime Asset Strategy (Authoritative)
Algorithm in use:
- `Appearance-set streaming`
- `Prewarm gate on animation switch`
- `Pinned hotset with owner classes`

Policy:
- Pin scope: `Player + All Spawned Enemies + All UI`
- Set breadth: `Current + Next windows` (+ bounded predicted interrupts)
- Pressure policy: `Protect Player Pins` (demote Enemy/UI/Effect first)

## 5) Loading Plan by Scenario

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

## 6) Save/Load Requirements
- Save data must include:
  - Location/location
  - Equipped gear per slot/form
  - Relevant gameplay state needed for immediate combat resume
- Load flow must drive prewarm using saved appearance/location context before unpausing gameplay.

## 7) Performance and Quality Targets
- Transition-frame p99: `<= 16.7 ms` on baseline hardware.
- Hard spike cap after warm cycle: no single transition `> 25 ms`.
- No blank sprite flashes during animation switch windows.
- No unresolved mapping spam in normal gameplay.

