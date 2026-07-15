using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class SingleSceneManager {
  enum PreUnlockQueuePressure {
    Normal,
    Moderate,
    High,
    Critical
  }

  IEnumerator RunPreUnlockAnimationWarmupSequence(bool includeVisibleSpriteReprefetch) {
    var playerController = ResolvePlayerAnimationController();
    BuildEnemyAnimationControllerSnapshot(preUnlockEnemyControllerScratch);

    yield return WarmAnimationPlaybackBeforeUnlock(playerController, preUnlockEnemyControllerScratch);

    BuildControllerAnimationFrameAddressSnapshot(
      playerController,
      preUnlockEnemyControllerScratch,
      preUnlockAddressScratch
    );

    if (preUnlockAddressScratch.Count > 0) {
      var blockingPrefixCount = ResolvePreUnlockBlockingPrefixCount(preUnlockAddressScratch);
      var preloadPasses = Mathf.Max(preUnlockAnimationFramePreloadPasses, 1);
      if (blockingPrefixCount > 0) {
        for (var pass = 0; pass < preloadPasses; pass++) {
          yield return PreloadAnimationAddressBatch(
            preUnlockAddressScratch,
            0,
            blockingPrefixCount,
            resetLoadingProgress: pass == 0,
            trackResidentPins: true,
            settleAfterEnqueue: false
          );
          yield return WaitForPreUnlockWarmupQueueSettle();
          if (pass + 1 < preloadPasses) {
            yield return null;
          }
        }
      }
    }

    if (includeVisibleSpriteReprefetch && preUnlockReprefetchVisibleSpritesAfterAnimationWarmup) {
      yield return PreloadVisibleSpriteWindowsUnderBlack(preUnlockEnemyControllerScratch);
      yield return WaitForPreUnlockWarmupQueueSettle();
    }
  }

  IEnumerator WaitForPreUnlockWarmupQueueSettle() {
    var timeoutSeconds = Mathf.Max(preUnlockWarmupQueueSettleTimeoutSeconds, 0f);
    if (timeoutSeconds <= 0f) yield break;

    var startedAt = Time.realtimeSinceStartup;
    while (true) {
      TextureResidencyCache.Pump();
      var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
      var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
      if (queue.queuedCount <= 0 && queue.inFlightCount <= 0 && deferredPending <= 0) {
        yield break;
      }

      var resolverIdle = SpriteRuntimeResolver.IsWarmupIdle();
      var playerReady = IsPlayerFirstFrameReady();
      var hasBlockingProgress = TryGetBlockingProgressState(
        out _,
        out _,
        out _,
        out var blockingCriticalReady,
        out var blockingHardBypassUsed
      );
      var settledForBlockingReady =
        hasBlockingProgress &&
        resolverIdle &&
        playerReady &&
        (blockingCriticalReady || blockingHardBypassUsed) &&
        IsQueueWithinBlockingReadyThresholds(queue, deferredPending);
      if (settledForBlockingReady) {
        if (ShouldLogLoadingProgressDebug()) {
          ResolveBlockingReadyQueueThresholds(out var maxOutstanding, out var maxInFlight);
          RuntimeLog.Log(
            "[SingleSceneManager][PreUnlockSettle] early_release" +
            " queued=" + queue.queuedCount +
            " in_flight=" + queue.inFlightCount +
            " deferred=" + deferredPending +
            " max_outstanding=" + maxOutstanding +
            " max_in_flight=" + maxInFlight
          );
        }
        yield break;
      }

      if ((Time.realtimeSinceStartup - startedAt) >= timeoutSeconds) {
        yield break;
      }
      yield return null;
    }
  }

  IEnumerator PreloadVisibleSpriteWindowsUnderBlack(List<AnimationController> enemyControllers = null) {
    if (!enablePreUnlockVisibleSpritePrefetch || !Application.isPlaying) yield break;

    var targets = ResolvePreUnlockVisibleSpriteTargets();
    if (targets == null || targets.Length <= 0) yield break;

    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var maxAddresses = ResolvePreUnlockMaxAddresses(queue);
    var addresses = preUnlockAddressScratch;
    var seenAddresses = preUnlockSeenAddressScratch;
    addresses.Clear();
    seenAddresses.Clear();
    var animationFrames = Mathf.Max(preUnlockPrefetchAnimationFrames, 1);
    var lookAheadFrames = ResolvePreUnlockLookAheadFrames(queue);
    var frameJumpClamp = Mathf.Max(preUnlockPrefetchFrameJumpClamp, 1);
    SortTargetsByPriority(targets);

    for (var i = 0; i < targets.Length; i++) {
      var target = targets[i];
      if (target == null) continue;
      if (!target.isActiveAndEnabled || target.DoNotRender) continue;
      if (!preUnlockPrefetchIncludeUiTargets && target.IsUiTarget()) continue;

      var startFrame = target.IsAnimation
        ? Mathf.Max(Mathf.Max(target.LastRequestedFrame, 1) - (frameJumpClamp - 1), 1)
        : 0;
      var endFrame = target.IsAnimation ? Mathf.Max(startFrame + animationFrames - 1, startFrame) : 0;

      target.CollectAnimationWindowAddresses(
        target.category,
        startFrame,
        endFrame,
        lookAheadFrames,
        addresses,
        seenAddresses,
        maxAddresses
      );

      if (addresses.Count >= maxAddresses) break;
    }

    if (enablePreUnlockControllerAnimationPrefetch && addresses.Count < maxAddresses) {
      var playerController = ResolvePlayerAnimationController();
      if (playerController != null) {
        playerController.CollectAnimationStartAddresses(
          addresses,
          seenAddresses,
          framesPerAnimation: animationFrames,
          maxAnimations: Mathf.Max(preUnlockPlayerAnimationStarts, 1),
          maxAddresses: maxAddresses
        );
      }

      var enemyMaxAnimations = Mathf.Max(preUnlockEnemyAnimationStartsPerController, 0);
      if (enemyMaxAnimations > 0 && addresses.Count < maxAddresses) {
        if (enemyControllers != null && enemyControllers.Count > 0) {
          for (var i = 0; i < enemyControllers.Count; i++) {
            if (addresses.Count >= maxAddresses) break;
            var controller = enemyControllers[i];
            if (controller == null) continue;
            controller.CollectAnimationStartAddresses(
              addresses,
              seenAddresses,
              framesPerAnimation: animationFrames,
              maxAnimations: enemyMaxAnimations,
              maxAddresses: maxAddresses
            );
          }
        }
        else {
          var activeEnemies = ResolveActiveEnemyControllers();
          for (var i = 0; i < activeEnemies.Length; i++) {
            if (addresses.Count >= maxAddresses) break;
            var enemy = activeEnemies[i];
            if (enemy == null || enemy.Controller == null) continue;
            enemy.Controller.CollectAnimationStartAddresses(
              addresses,
              seenAddresses,
              framesPerAnimation: animationFrames,
              maxAnimations: enemyMaxAnimations,
              maxAddresses: maxAddresses
            );
          }
        }
      }
    }

    if (preUnlockPrefetchExpandAtlasSiblings && addresses.Count > 0 && addresses.Count < maxAddresses) {
      var maxSiblingsPerSeed = Mathf.Clamp(preUnlockPrefetchMaxAtlasSiblingsPerSeed, 1, 256);
      var siblingScratch = preUnlockAtlasSiblingScratch;
      if (siblingScratch.Capacity < maxSiblingsPerSeed) {
        siblingScratch.Capacity = maxSiblingsPerSeed;
      }
      var seedCount = addresses.Count;
      for (var i = 0; i < seedCount; i++) {
        if (addresses.Count >= maxAddresses) break;
        var seedAddress = addresses[i];
        if (string.IsNullOrWhiteSpace(seedAddress)) continue;

        siblingScratch.Clear();
        if (!SpriteRuntimeResolver.TryCollectAtlasSiblingAddresses(seedAddress, siblingScratch, maxSiblingsPerSeed)) continue;

        for (var s = 0; s < siblingScratch.Count; s++) {
          if (addresses.Count >= maxAddresses) break;
          var siblingAddress = siblingScratch[s];
          if (string.IsNullOrWhiteSpace(siblingAddress)) continue;
          if (!seenAddresses.Add(siblingAddress)) continue;
          addresses.Add(siblingAddress);
        }
      }
    }

    if (addresses.Count <= 0) yield break;

    yield return PreloadAnimationAddressBatch(addresses, resetLoadingProgress: false);
  }

  void SortTargetsByPriority(SpriteWithNormals[] targets) {
    if (targets == null || targets.Length <= 1) return;
    var player = ResolvePlayerGearController();
    var playerPos = player != null ? player.transform.position : Vector3.zero;
    var hasPlayer = player != null;

    Array.Sort(targets, (a, b) => {
      if (a == null && b == null) return 0;
      if (a == null) return 1;
      if (b == null) return -1;
      if (!hasPlayer) return 0;
      var distA = (a.transform.position - playerPos).sqrMagnitude;
      var distB = (b.transform.position - playerPos).sqrMagnitude;
      return distA.CompareTo(distB);
    });
  }

  IEnumerator WarmAnimationPlaybackBeforeUnlock(
    AnimationController playerController = null,
    List<AnimationController> enemyControllers = null
  ) {
    if (!enablePreUnlockAnimationPlaybackWarmup || !Application.isPlaying) yield break;

    var passes = ResolvePreUnlockPlaybackPasses();
    var controllersPerFrame = Mathf.Max(preUnlockAnimationWarmupControllersPerFrame, 1);
    var warmedControllers = 0;
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var maxAddresses = ResolvePreUnlockMaxAddresses(queue);
    var addresses = preUnlockAddressScratch;
    var seenAddresses = preUnlockSeenAddressScratch;
    addresses.Clear();
    seenAddresses.Clear();

    SetLoadingBlackscreenHold(true);

    if (playerController == null) {
      playerController = ResolvePlayerAnimationController();
    }

    var playerGear = ResolvePlayerGearController();
    var playerAtlasesManaged = playerGear != null && playerGear.SceneAppearanceAtlasPinsManaged;
    if (playerController != null && !playerAtlasesManaged) {
      var playerMaxAnimations = Mathf.Max(preUnlockPlayerAnimationStarts, 1);
      playerController.CollectWarmPlaybackAddresses(
        addresses,
        seenAddresses,
        passCount: passes,
        maxAnimations: playerMaxAnimations,
        maxAddresses: maxAddresses
      );
      warmedControllers++;
      if (warmedControllers % controllersPerFrame == 0) {
        yield return null;
      }
    }
    var playerWarmPlaybackAddressCount = addresses.Count;

    if (preUnlockWarmEnemyAnimationPlayback) {
      if (enemyControllers == null || enemyControllers.Count <= 0) {
        BuildEnemyAnimationControllerSnapshot(preUnlockEnemyControllerScratch);
        enemyControllers = preUnlockEnemyControllerScratch;
      }

      var maxEnemies = ResolvePreUnlockEnemyControllerCap(enemyControllers != null ? enemyControllers.Count : 0);
      var enemyMaxAnimations = Mathf.Max(preUnlockEnemyAnimationStartsPerController, 0);
      var warmedEnemies = 0;
      for (var i = 0; i < enemyControllers.Count; i++) {
        if (maxEnemies > 0 && warmedEnemies >= maxEnemies) break;
        if (enemyMaxAnimations <= 0) break;
        var controller = enemyControllers[i];
        if (controller == null) continue;
        controller.CollectWarmPlaybackAddresses(
          addresses,
          seenAddresses,
          passCount: passes,
          maxAnimations: enemyMaxAnimations,
          maxAddresses: maxAddresses
        );
        warmedControllers++;
        warmedEnemies++;
        if (addresses.Count >= maxAddresses) break;
        if (warmedControllers % controllersPerFrame == 0) {
          yield return null;
        }
      }
    }

    if (addresses.Count > 0) {
      var blockingPrefixCount = ResolvePreUnlockBlockingPrefixCount(addresses, playerWarmPlaybackAddressCount);
      if (blockingPrefixCount > 0) {
        yield return PreloadAnimationAddressBatch(
          addresses,
          0,
          blockingPrefixCount,
          resetLoadingProgress: true,
          trackResidentPins: true,
          settleAfterEnqueue: false
        );
        yield return WaitForPreUnlockWarmupQueueSettle();
      }
    }
  }

  IEnumerator PreloadAllControllerAnimationFrames(
    AnimationController playerController = null,
    List<AnimationController> enemyControllers = null
  ) {
    if (!Application.isPlaying) yield break;

    if (playerController == null) {
      playerController = ResolvePlayerAnimationController();
    }

    if (enemyControllers == null) {
      BuildEnemyAnimationControllerSnapshot(preUnlockEnemyControllerScratch);
      enemyControllers = preUnlockEnemyControllerScratch;
    }

    BuildControllerAnimationFrameAddressSnapshot(
      playerController,
      enemyControllers,
      preUnlockAddressScratch
    );

    yield return PreloadAnimationAddressBatch(preUnlockAddressScratch, resetLoadingProgress: true);
  }

  AnimationController ResolvePlayerAnimationController() {
    var player = ResolvePlayerGearController();
    if (player == null) return null;
    return player.Controller;
  }

  void BuildEnemyAnimationControllerSnapshot(List<AnimationController> outControllers) {
    if (outControllers == null) return;
    outControllers.Clear();

    var activeEnemies = ResolveActiveEnemyControllers();
    if (activeEnemies.Length <= 0) return;

    var player = ResolvePlayerGearController();
    var hasPlayer = player != null;
    var playerPosition = hasPlayer ? player.transform.position : Vector3.zero;
    var maxDistance = Mathf.Max(preUnlockAnimationWarmupEnemyDistance, 0f);
    var maxDistanceSqr = maxDistance > 0f ? maxDistance * maxDistance : -1f;

    // Collect filtered enemies with their squared distances, then sort nearest-first so
    // their animation addresses land at the front of the preload list (deterministic ordering).
    var filteredScratch = preUnlockFilteredEnemyScratch;
    filteredScratch.Clear();
    for (var i = 0; i < activeEnemies.Length; i++) {
      var enemy = activeEnemies[i];
      if (enemy == null || enemy.Controller == null) continue;
      var sqrDist = 0f;
      if (hasPlayer) {
        var delta = enemy.transform.position - playerPosition;
        sqrDist = delta.sqrMagnitude;
        if (maxDistanceSqr > 0f && sqrDist > maxDistanceSqr) continue;
      }
      filteredScratch.Add((sqrDist, enemy.Controller));
    }

    if (hasPlayer && filteredScratch.Count > 1) {
      filteredScratch.Sort((a, b) => a.sqrDist.CompareTo(b.sqrDist));
    }

    for (var i = 0; i < filteredScratch.Count; i++) {
      outControllers.Add(filteredScratch[i].controller);
    }
  }

  void BuildControllerAnimationFrameAddressSnapshot(
    AnimationController playerController,
    List<AnimationController> enemyControllers,
    List<string> outAddresses
  ) {
    if (outAddresses == null) return;

    var maxAddresses = ResolvePreUnlockMaxAddresses(TextureResidencyCache.GetQueueSnapshot(pump: false));
    var seenAddresses = preUnlockSeenAddressScratch;
    outAddresses.Clear();
    seenAddresses.Clear();
    var animationFrames = Mathf.Max(preUnlockPrefetchAnimationFrames, 1);

    var playerGear = ResolvePlayerGearController();
    var playerAtlasesManaged = playerGear != null && playerGear.SceneAppearanceAtlasPinsManaged;
    if (playerController != null && !playerAtlasesManaged) {
      playerController.CollectAnimationStartAddresses(
        outAddresses,
        seenAddresses,
        framesPerAnimation: animationFrames,
        maxAnimations: Mathf.Max(preUnlockPlayerAnimationStarts, 1),
        maxAddresses: maxAddresses
      );
    }
    preUnlockLastPlayerAddressCount = outAddresses.Count;

    if (outAddresses.Count < maxAddresses) {
      var enemyMaxAnimations = Mathf.Max(preUnlockEnemyAnimationStartsPerController, 0);
      if (enemyMaxAnimations <= 0) return;
      if (enemyControllers != null) {
        for (var i = 0; i < enemyControllers.Count; i++) {
          if (outAddresses.Count >= maxAddresses) break;
          var controller = enemyControllers[i];
          if (controller == null) continue;
          controller.CollectAnimationStartAddresses(
            outAddresses,
            seenAddresses,
            framesPerAnimation: animationFrames,
            maxAnimations: enemyMaxAnimations,
            maxAddresses: maxAddresses
          );
        }
      }
      else {
        var activeEnemies = ResolveActiveEnemyControllers();
        for (var i = 0; i < activeEnemies.Length; i++) {
          if (outAddresses.Count >= maxAddresses) break;
          var enemy = activeEnemies[i];
          if (enemy == null || enemy.Controller == null) continue;
          enemy.Controller.CollectAnimationStartAddresses(
            outAddresses,
            seenAddresses,
            framesPerAnimation: animationFrames,
            maxAnimations: enemyMaxAnimations,
            maxAddresses: maxAddresses
          );
        }
      }
    }
  }

  IEnumerator PreloadAnimationAddressBatch(List<string> addresses, bool resetLoadingProgress) {
    if (addresses == null || addresses.Count <= 0) yield break;
    yield return PreloadAnimationAddressBatch(
      addresses,
      0,
      addresses.Count,
      resetLoadingProgress,
      trackResidentPins: true,
      settleAfterEnqueue: true
    );
  }

  IEnumerator PreloadAnimationAddressBatch(
    List<string> addresses,
    int startInclusive,
    int entryCount,
    bool resetLoadingProgress,
    bool trackResidentPins,
    bool settleAfterEnqueue
  ) {
    if (addresses == null || addresses.Count <= 0 || entryCount <= 0) yield break;
    var start = Mathf.Clamp(startInclusive, 0, addresses.Count);
    var endExclusive = Mathf.Clamp(start + Mathf.Max(entryCount, 0), start, addresses.Count);
    var requestedCount = endExclusive - start;
    if (requestedCount <= 0) yield break;
    if (trackResidentPins) {
      AccumulatePreUnlockResidentPins(addresses, start, requestedCount);
    }

    if (resetLoadingProgress) {
      ResetLoadingProgressForPhase();
    }

    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var enqueueBudget = ResolvePreUnlockEnqueueBudget(queue);

    // Process in batches to avoid stalling the main thread while filling the request queue.
    // Matches Aguide.txt "Batched load" guidance (50-200).
    // Tune this value based on platform performance (lower for mobile).
    const int BatchSize = 100;
    var processedCount = 0;

    while (processedCount < requestedCount) {
      var remaining = requestedCount - processedCount;
      var chunkCount = Mathf.Min(BatchSize, remaining);
      var chunkStart = start + processedCount;
      QueuePreUnlockTrimmedMetadataWarmup(addresses, chunkStart, chunkCount);
      yield return TextureResidencyCache.RequestLoadBatchThrottled(
        addresses,
        chunkStart,
        chunkCount,
        TextureResidencyCache.LoadPriority.Warmup,
        // Atlas-first preload: ensure sibling slices from the same atlas are resident before gameplay unlock.
        allowAtlasExpansion: true,
        enqueueBudgetPerFrame: enqueueBudget
      );

      processedCount += chunkCount;
      if (settleAfterEnqueue) {
        yield return WaitForPreUnlockWarmupQueueSettle();
      }

      queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
      enqueueBudget = ResolvePreUnlockEnqueueBudget(queue);
    }
  }

  void QueuePreUnlockTrimmedMetadataWarmup(List<string> addresses, int startInclusive, int count) {
    if (addresses == null || addresses.Count <= 0 || count <= 0) return;
    var queuedAtlasMetadata = TrimmedSpriteOffsetResolver.QueueWarmupAtlasMetadataBatch(addresses, startInclusive, count);
    if (queuedAtlasMetadata <= 0) return;

    TrimmedSpriteOffsetResolver.PumpDeferredRuntimeLoads();
    if (!ShouldLogLoadingProgressDebug()) return;
    RuntimeLog.Log(
      "[SingleSceneManager][PreUnlockMetadataWarmup] start=" + startInclusive +
      " count=" + count +
      " queued_atlas_metadata=" + queuedAtlasMetadata
    );
  }

  SpriteWithNormals[] ResolvePreUnlockVisibleSpriteTargets() {
    var now = Time.realtimeSinceStartup;
    var refreshSeconds = Mathf.Max(preUnlockTargetCacheRefreshSeconds, 0f);
    var hasCache = preUnlockVisibleSpriteTargetsCache != null && preUnlockVisibleSpriteTargetsCache.Length > 0;
    var cacheExpired = refreshSeconds <= 0f ||
      preUnlockVisibleSpriteTargetsCacheRefreshedAt < 0f ||
      (now - preUnlockVisibleSpriteTargetsCacheRefreshedAt) >= refreshSeconds;
    if (hasCache && !cacheExpired) {
      return preUnlockVisibleSpriteTargetsCache;
    }

    preUnlockVisibleSpriteTargetsCache =
      FindObjectsByType<SpriteWithNormals>(FindObjectsInactive.Exclude) ??
      Array.Empty<SpriteWithNormals>();
    preUnlockVisibleSpriteTargetsCacheRefreshedAt = now;
    return preUnlockVisibleSpriteTargetsCache;
  }

  void InvalidatePreUnlockTargetCache() {
    preUnlockVisibleSpriteTargetsCache = Array.Empty<SpriteWithNormals>();
    preUnlockVisibleSpriteTargetsCacheRefreshedAt = -1f;
  }

  int ResolvePreUnlockMaxAddresses(TextureResidencyCache.QueueSnapshot queue) {
    var configuredMax = Mathf.Max(preUnlockPrefetchMaxAddresses, 1);
    var configuredMin = Mathf.Clamp(preUnlockPrefetchMinAddresses, 1, configuredMax);
    var pressure = ResolvePreUnlockQueuePressure(queue);
    var scale = 1f;

    if (SystemInfo.systemMemorySize <= 8192) {
      scale = 0.45f;
    }
    else if (SystemInfo.systemMemorySize <= 12288) {
      scale = 0.65f;
    }
    else if (SystemInfo.systemMemorySize <= 16384) {
      scale = 0.8f;
    }

    if (pressure == PreUnlockQueuePressure.Critical) {
      scale *= 0.5f;
    }
    else if (pressure == PreUnlockQueuePressure.High) {
      scale *= 0.7f;
    }

    var scaled = Mathf.RoundToInt(configuredMax * scale);
    return Mathf.Clamp(scaled, configuredMin, configuredMax);
  }

  int ResolvePreUnlockLookAheadFrames(TextureResidencyCache.QueueSnapshot queue) {
    var lookAhead = Mathf.Max(preUnlockPrefetchLookAheadFrames, 0);
    if (lookAhead <= 0) return 0;
    var pressure = ResolvePreUnlockQueuePressure(queue);
    if (pressure == PreUnlockQueuePressure.Critical) return 0;
    if (pressure == PreUnlockQueuePressure.High) return Mathf.Min(lookAhead, 1);
    return lookAhead;
  }

  int ResolvePreUnlockPlaybackPasses() {
    var passes = Mathf.Max(preUnlockAnimationPlaybackPasses, 1);
    if (passes <= 1) return 1;

    if (SystemInfo.systemMemorySize <= 12288) return 1;
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var pressure = ResolvePreUnlockQueuePressure(queue);
    if (pressure == PreUnlockQueuePressure.High ||
        pressure == PreUnlockQueuePressure.Critical) {
      return 1;
    }
    return passes;
  }

  int ResolvePreUnlockEnemyControllerCap(int availableCount) {
    if (availableCount <= 0) return 0;
    if (preUnlockAnimationWarmupMaxEnemyControllers > 0) {
      return Mathf.Min(preUnlockAnimationWarmupMaxEnemyControllers, availableCount);
    }

    var autoCap = 6;
    if (SystemInfo.systemMemorySize <= 8192) {
      autoCap = 2;
    }
    else if (SystemInfo.systemMemorySize <= 12288) {
      autoCap = 4;
    }
    return Mathf.Min(autoCap, availableCount);
  }

  int ResolvePreUnlockEnqueueBudget(TextureResidencyCache.QueueSnapshot queue) {
    var budget = Mathf.Clamp(preUnlockPrefetchEnqueueBudgetPerFrame, 50, 200);
    var pressure = ResolvePreUnlockQueuePressure(queue);
    if (pressure == PreUnlockQueuePressure.Critical) return Mathf.Min(budget, 60);
    if (pressure == PreUnlockQueuePressure.High) return Mathf.Min(budget, 90);
    if (pressure == PreUnlockQueuePressure.Moderate) return Mathf.Min(budget, 120);
    return budget;
  }

  int ResolveWarmupPriorityPrefixCount(int totalAddressCount, TextureResidencyCache.QueueSnapshot queue, int playerAddressCount = 0) {
    if (totalAddressCount <= 0) return 0;
    var pressure = ResolvePreUnlockQueuePressure(queue);
    int queueBasedCount;
    if (pressure == PreUnlockQueuePressure.Critical) {
      queueBasedCount = Mathf.Clamp(totalAddressCount / 3, 32, totalAddressCount);
    }
    else if (pressure == PreUnlockQueuePressure.High) {
      queueBasedCount = Mathf.Clamp((totalAddressCount * 2) / 3, 64, totalAddressCount);
    }
    else {
      return totalAddressCount;
    }
    // Floor at player address count so all player-critical sprites always get Warmup priority
    // regardless of queue pressure, reflecting measured time-to-first-ready-frame for the player.
    return Mathf.Clamp(Mathf.Max(queueBasedCount, playerAddressCount), min: 0, max: totalAddressCount);
  }

  static PreUnlockQueuePressure ResolvePreUnlockQueuePressure(TextureResidencyCache.QueueSnapshot queue) {
    if (queue.queuedCount >= 1400 || queue.inFlightCount >= 192) {
      return PreUnlockQueuePressure.Critical;
    }
    if (queue.queuedCount >= 900 || queue.inFlightCount >= 128) {
      return PreUnlockQueuePressure.High;
    }
    if (queue.queuedCount >= 500 || queue.inFlightCount >= 64) {
      return PreUnlockQueuePressure.Moderate;
    }
    return PreUnlockQueuePressure.Normal;
  }
}
