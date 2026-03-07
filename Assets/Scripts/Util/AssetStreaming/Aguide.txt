Addressables at scale (thousands of assets): quick guide

Q: Is there a hard limit?
- No built-in hard cap on asset count or groups.
- Practical limits are memory, catalog size, build time, and editor responsiveness.

What usually breaks first
- RAM spikes: too many bundles/assets loaded at once.
- Catalog bloat: too many tiny bundles slows startup/update checks.
- Editor slowdown: large group lists become painful to manage.

Core rule
- Group assets by lifecycle: load together, unload together.

Recommended structure
- Use labels as your primary loading unit (feature, level, category).
- Prefer `Pack Together By Label` for related content.
- Use `Pack Separately` only for truly independent assets.
- Avoid one giant "pack together" group unless always used together.

Loading patterns (broad strokes)
1) Label-based load (small/moderate sets)
```
handle = LoadAssetsAsync(label)
wait handle
use results
release handle when category is no longer needed
```

2) Batched load (large sets, 1000+)
```
locations = ResolveLocations(addresses/keys)
for each chunk in locations (start with 50-200):
    batchHandle = LoadAssetsAsync(chunk)
    wait batchHandle
    process/store only what is needed
    release batchHandle if not retained
release locations handle
```

3) On-demand cache (very large games)
```
if key in cache: return cached
handle = LoadAssetAsync(key)
wait handle
cache result
return result
```

Critical operating rules
- Never load "everything" at boot.
- Always release handles when done.
- Profile with Addressables Event Viewer + Memory Profiler.
- Tune chunk size per platform (mobile/VR usually smaller).

Build/pack tips
- Use LZ4 for faster runtime reads.
- Use remote groups for large live content.
- Keep bundle count reasonable (not one-asset-per-bundle).

Scale expectations
- Tens of thousands of addressable assets is normal if:
  - content is label-organized,
  - loads are batched/on-demand,
  - memory is actively profiled and released.

--- Implementation Review (AssetStreaming/) ---
Status: VALIDATED

Correct Implementations:
1. Batching: `StreamingWarmOrchestrator` and `SingleSceneManager` use 100-item chunks (matches 50-200 guidance).
2. Lifecycle: `StreamingWarmOrchestrator` sorts by lifecycle labels (spawn/idle) before loading.
3. Catalog Scale: `SpriteRuntimeResolver` implements a sharded manifest system to mitigate catalog bloat.
4. Smoothness: `AnimationController` implements lookahead (pinning) to prevent frame drops.

Notes/Warnings:
- `SingleSceneManager` pre-unlock prefetch (12k addresses) is aggressive; monitor memory on low-end devices.
- Ensure `TextureResidencyCache` properly releases handles from `RequestLoadBatchThrottled` to avoid leaks (Guide: "Always release handles").
