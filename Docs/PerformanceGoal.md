## Performance Goal

Ship smooth 60 FPS gameplay with fast, honest loading and no visible pop-in after reveal.

## Current Objective

1. Keep loading heartbeat gaps at `<= 2.0s`.
2. Keep loading progress smooth and truthful.
3. Finish reveal-critical work under black so gameplay appears stable on first frame.
4. Continue moving toward dynamic smart loading driven by relevance and location context.

## Current Truth

Source: latest Unity `Editor.log` run on `2026-03-24`.

Observed:
- `[SingleSceneManager][LoadingHeartbeatGap] gap_s=3.768` was logged at `2026-03-24T21:53:59.136Z`.
- Loading status moved quickly to `80%+`, then spent a long time in `Preparing player` / `Warming critical`.
- `[SingleSceneManager][LoadingStatus] percent=99 detail='Ready'` was logged at `2026-03-24T21:54:28.829Z`.
- Gameplay-side activation did not begin until `2026-03-24T21:54:34.743Z`.
- Queued location dialog work was still being released after that activation window, with dialog start at `2026-03-24T21:54:36.913Z`.
- On the latest rerun, `[SingleSceneManager][RevealSettle] timeout elapsed_s=2.014` was logged at `2026-03-24T22:45:01.037Z` while queue, resolver, and player were already ready.
- That timeout still reported `location_activation_pending=1`, which points at location activation state bookkeeping rather than live stream backlog.
- After the deferred-activation fix, the next `2026-03-24` rerun no longer showed visible post-fade pop-in in gameplay.
- The same rerun still logged `[SingleSceneManager][LoadingHeartbeatGap] gap_s=6.436` at `2026-03-24T22:57:45.527Z`.
- That rerun also logged multiple `[TextureResidencyCache][CompletionDiag]` spikes between `202.8ms` and `488.7ms` during the protected loading window.
- The latest `2026-03-24` rerun confirmed reveal handoff is no longer the long pole, but still logged `[SingleSceneManager][LoadingHeartbeatGap] gap_s=14.032` at `2026-03-24T23:07:30.841Z`.
- That same rerun logged protected-overlay completion spikes at `226.5ms`, `212.2ms`, `270.9ms`, `286.4ms`, and `506.6ms`.
- The active problem is smoothness and reveal timing, not a total loading failure.

Interpretation:
- `Ready` must not appear before gameplay activation and reveal settle are actually complete.
- Reveal-critical activation still needs to stay hidden behind the overlay.
- Deferred or same-location carry-over activation must not block reveal once blocking activation is done.
- The next optimization target is large completion/finalization work and gameplay handoff cost, not reveal correctness.
- Reveal correctness is now good enough that the active target is protected-overlay completion cost.
- Queue/completion bursts are still large enough to hurt perceived speed and smoothness.

## Architecture To Preserve

- Runtime sprite residency is atlas-based.
- Gameplay does not depend on per-slice Addressables loads.
- Pre-unlock warmup is collect-only and does not drive live renderer playback.
- Critical player, gear, nearby-threat, and core-effect work stays under the loading overlay / warm gate.
- Enemy residency stays relevance-based rather than `pinAllSpawnedEnemies`.
- Location warm content is prefab-driven through `LocationWarmProfile`.
- Grouped gear atlases build through surrogate assets so runtime can keep atlas behavior without packed-build sprite fan-out.
- Unsupported trimmed-metadata atlas families such as `_Bounces`, `Effects`, and `Expressions` are skipped instead of queued for guaranteed-fail metadata loads.

## Active Focus

- preload only content that is relevant to the current location, player state, and nearby threats
- keep high-value content ready before reveal
- keep loading percent monotonic and avoid visible stalls caused by fake headroom
- keep `Ready` hidden until gameplay activation and opaque reveal settle are complete
- finish location staged activation under the overlay so `FG/Dynamic` and `FG/Destruct` do not pop in after fade-out
- use `[SingleSceneManager][RevealSettle]` and `[SingleSceneManager][LoadingStatus]` to identify the real late blocker
- reduce queue bursts and completion bursts that front-load low-value work
- expand or contract warm sets based on actual relevance instead of static over-pin behavior

## What To Measure Next

1. Re-run `Start New Game` and `Load Game`.
2. Confirm there are no `[SingleSceneManager][LoadingHeartbeatGap]` entries above `2.0s`.
3. Confirm loading status does not reach `Ready` before gameplay activation is complete.
4. Confirm `[SingleSceneManager][RevealSettle]` does not time out and identifies any remaining blocker if reveal still holds.
5. Confirm no visible world, UI, or dialog pop-in occurs after black starts fading out.
6. If a hitch remains, correlate it first with:
   - location prefab activation
   - player first-frame preparation
   - atlas completion bursts
   - queue depth / deferred work

## Do Not Regress

- Do not restore per-slice gameplay asset loads.
- Do not move critical gear/load/apply work back out of the loading overlay.
- Do not restore renderer-driving preload loops.
- Do not return to `pinAllSpawnedEnemies` as the default residency policy.
- Do not treat editor pipeline success alone as proof of runtime smoothness.
