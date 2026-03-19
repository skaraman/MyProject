## Performance Goal

Ship smooth 60 FPS gameplay with reliable first-play animation and effect playback.

Current objective:
1. Keep loading heartbeat gaps at `<= 2.0s`.
2. Keep location prefab loading smooth under the overlay.
3. Continue moving toward dynamic smart loading driven by relevance and location context.

## Stable Checkpoint

This file now tracks the current stable checkpoint, not the full experiment history.

Baseline architecture to preserve:
- Runtime sprite residency is atlas-based.
- Gameplay does not depend on per-slice Addressables loads.
- Pre-unlock warmup is collect-only and does not drive live renderer playback.
- Critical player, gear, nearby-threat, and core-effect work stays under the loading overlay / warm gate.
- Enemy residency stays relevance-based rather than `pinAllSpawnedEnemies`.
- Location warm content is prefab-driven through `LocationWarmProfile`.
- Grouped gear atlases build through surrogate assets so runtime can keep atlas behavior without packed-build sprite fan-out.
- Unsupported trimmed-metadata atlas families such as `_Bounces`, `Effects`, and `Expressions` are skipped instead of queued for guaranteed-fail metadata loads.

## Latest Verified Run

Source: latest Unity `Editor.log` run on `2026-03-18`.

Observed:
- `MainMenu -> Gameplay` transition completed successfully.
- `ready_to_reveal` logged at `2026-03-18T23:48:17.972Z`.
- `reveal_complete` logged at `2026-03-18T23:48:20.124Z`.
- No `[SingleSceneManager][LoadingHeartbeatGap]` entries were present in the latest log scan.
- The main notable runtime spike in the latest log was:
  - `[TextureResidencyCache][CompletionDiag] total_ms=242.4`
- Metadata-skip logs for excluded atlas families are expected and now confirm the skip path is active instead of failing loads.

Interpretation:
- This is a stable working checkpoint.
- The old catastrophic freeze signature is not the active state in the latest log.
- Remaining work is now smoothness and load-shaping, not emergency recovery.

## Active Tracking

### Primary target

Reduce any loading heartbeat gaps to `<= 2.0s` while preserving the current stable startup / reveal path.

### Current focus

Improve location prefab loading smoothness and dynamic smart loading:
- preload only the content that is relevant to the current location, player state, and nearby threats
- keep high-value content ready before reveal
- avoid queue bursts that front-load low-value work
- expand or contract warm sets based on actual relevance instead of static over-pin behavior

### What to measure next

1. Re-run `Start New Game` and keep checking for any `[SingleSceneManager][LoadingHeartbeatGap] > 2.0s`.
2. Watch whether location-prefab-driven warm content stays smooth when new locations or room-adjacent content enter scope.
3. If a new hitch appears, correlate it first with:
   - location prefab activation
   - atlas completion bursts
   - queue depth / deferred work
   - any editor-only fallback or metadata path reappearing

## Archived Summary

Previous detailed investigation has been intentionally condensed.

What was solved or established:
- atlas residency replaced the old per-slice gameplay streaming model
- loading overlay ownership was tightened so critical first-play work happens under protection
- warmup was reduced to collect-only planning instead of renderer-driving playback
- enemy residency moved to relevance-based pinning
- grouped atlas runtime-index joins were repaired using supplemental local-ID data
- grouped gear atlas builds were reshaped through surrogates and build-risk guardrails
- trimmed metadata warmup now skips unsupported atlas families instead of doing known-failing work
- multiple earlier freezes were traced through queue pressure, fallback behavior, metadata work, and build fan-out until the current stable baseline was reached

Use git history if the full chronological investigation is needed again. This file should stay short and reflect only the current checkpoint plus the active goal.

## Do Not Regress

- Do not restore per-slice gameplay asset loads.
- Do not move critical gear/load/apply work back out of the loading overlay.
- Do not restore renderer-driving preload loops.
- Do not return to `pinAllSpawnedEnemies` as the default residency policy.
- Do not treat editor pipeline success alone as proof of runtime smoothness.
