# Optimal Loading Progress

## Objective

- Keep gameplay loading consistent and ordered: `player -> location -> enemies -> ui -> dialog`.
- Keep gameplay hidden behind black until location activation, deferred location work, and critical streaming are settled.

## Current Load Flow

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
- Reference: `Docs/LoadingCacheManifest.md`

## Latest Verified State

- Latest checked editor run: `2026-03-26` around `22:05`.
- `LoadGameFlow` reached gameplay with `current_location=DomeCity`.
- Gameplay activation completed before reveal settle finished.
- Reveal settled after the queue drained and then released.
- The flow looked correct on that run; the main remaining log noise was a brief end-stage status bounce between `Finalizing reveal` and `Draining queue`.

## Diagnostics To Watch

- `[SingleSceneManager][GameplayLocation]`
- `[SingleSceneManager][EnvironmentCache]`
- `[SingleSceneManager][LoadingStatus]`
- `[SingleSceneManager][RevealSettle]`

## Next Checks

- Repeat the `Shift+Escape -> Load Game` loop several times in one session.
- Confirm loading percent/text appears every run.
- Confirm no environment pop-in after fade begins.
- Confirm environment cache rotates as `current/previous` and never treats `mainmenu` as a gameplay environment.

# Loading Cache Manifest

Purpose
- Keep loading behavior predictable by defining which assets stay resident, which rotate, and which are transient warmup-only.

Session-Global Cache
- `MainMenu` UI: keep cached for the full app session through `RuntimeAssetCache.GlobalUi` warmups plus persistent UI atlas pins.
- `PauseMenu` UI: same policy as `MainMenu`; pause/menu UI should not cold-load after the first time it appears.
- Shared loading/menu fonts: keep pinned for the full session.

Persistent Character Cache
- ESPER core appearance atlases: keep pinned for the full session.
- ESPER core effect atlases: keep pinned for the full session.
- Gear-specific transient appearance content may still stream, but the startup body/effect baseline must remain resident.

Environment Hot Cache
- Keep exactly `2` gameplay environments hot:
  - slot `current`: the active gameplay environment
  - slot `previous`: the most recently displaced gameplay environment
- `mainmenu` is never treated as an environment hot-cache slot.
- Environment hot cache rotates only when a new gameplay location is remembered.
- Environment hot cache is sourced from `LocationWarmProfile.CollectEnvironmentCacheLists(...)`.
- Environment prefab assets are bounded to the same `2`-environment rule.

Transient Cache
- Warm-gate and pre-unlock resident pins are temporary and should be released after gameplay reveal settles.
- Enemy runtime residency remains dynamic and distance-based.
- Session runtime assets that are not menu-global should still be clearable on main-menu return.

Implementation Owners
- Menu/session UI runtime assets: `RuntimeAssetCache`
- Persistent fonts/player baseline: `SingleSceneManager`
- Environment hot cache rotation: `SingleSceneManager`
- Environment prefab LRU cache: `LocationPrefabData`

Rules For New Cache Work
- If an asset category is required in nearly every session, prefer a persistent owner.
- If an asset category is location-specific, prefer the two-slot environment hot cache over a permanent cache.
- If a cache does not have an explicit eviction rule, it is incomplete.
