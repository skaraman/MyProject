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

## 8) Development Progress Tracker

Status legend:
- `Done`
- `In Progress`
- `Planned`
- `Blocked`

| ID | Feature | Status | Notes |
|---|---|---|---|
| GDD-001 | Main menu supports Start/Load/Settings flow | Planned | Validate current implementation and scene transitions. |
| GDD-002 | Start flow loads first location + player + enemies | Planned | Hook final preload checkpoint before combat unlock. |
| GDD-003 | Load flow restores saved location + player + enemies | Planned | Requires deterministic prewarm on load path. |
| GDD-004 | Settings screen (audio/visual/controls) | Planned | Confirm persistence and runtime apply behavior. |
| GDD-005 | Player animation coverage (6 attack, 4 move, 4 jump + To) | In Progress | Data exists; verify complete asset mappings for all gear variants. |
| GDD-006 | Gear-change only in menu with fade/loading | Planned | Integrate explicit prewarm phase before gameplay return. |
| GDD-007 | Enemy fixed appearance-set loading | In Progress | Owner pin class in place; add wave/location preload pass. |
| GDD-008 | Effects loaded for attack/event animations | In Progress | Effect controllers wired; add first-use location preload set. |
| GDD-009 | Appearance-set streaming core | Done | Runtime extraction + owner pin updates implemented. |
| GDD-010 | Prewarm animation switch gate | Done | Existing gate active with timeout and first-frame readiness checks. |
| GDD-011 | Pinned hotset owner model | Done | Player/Enemy/UI/Effect classes with owner lifecycle. |
| GDD-012 | Pressure policy protects player pins first | Done | Demotion order implemented: Enemy -> UI -> Effect -> Player. |
| GDD-013 | UI pinning service (active UI targets) | Done | Throttled refresh + owner release on deactivate/destroy. |
| GDD-014 | Location preload service for player/enemy/effects | Planned | Next major task for deterministic first-combat smoothness. |
| GDD-015 | Diagnostics HUD + CSV for streaming/pins | Done | Includes pinned owner/address/demotion metrics. |
| GDD-016 | Performance acceptance pass (p99, spike cap) | Planned | Run formal validation on baseline hardware. |
| GDD-017 | Build pipeline check (runtime index + addressables + hotset) | In Progress | Command flow documented; verify in CI/manual build checklist. |

## 9) Current Implementation Notes
- Runtime currently supports:
  - owner-based appearance pin updates
  - animation switch prewarm gate behavior
  - pin pressure demotion with player protection
  - UI pinning refresh loop
  - diagnostics for queue/load/wait/pin state
- Next recommended implementation milestone:
  - add a dedicated location-load prewarm orchestrator to close the first-wave hitch risk.

## 10) Change Log
- 2026-02-24: Initial GDD created from current game design + streaming strategy baseline.
