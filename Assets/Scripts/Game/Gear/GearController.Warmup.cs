using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class GearController {
  private void PrimeSpriteStreamingWarmup() {
    if (!Application.isPlaying) return;

    // Ideal runtime behavior keeps this set tight: only currently visible or
    // imminent libraries. Oversized warmup sets increase startup queue pressure.
    var libraries = spriteWarmupLibraryScratch;
    libraries.Clear();
    CollectLibraries(GearObjects, libraries);
    CollectLibraries(SkinObjects, libraries);
    if (effectNode != null && !string.IsNullOrWhiteSpace(effectNode.libraryName)) {
      libraries.Add(effectNode.libraryName.Trim());
    }

    SpriteRuntimeResolver.WarmupLibraries(libraries);
    libraries.Clear();
  }

  private void PrimeControllerAnimationWarmup() {
    if (!Application.isPlaying || !prewarmAnimationStartsOnLoad) return;
    // Keep first action inputs smooth by warming a wider startup frame window.
    var warmFrames = Mathf.Max(prewarmFramesPerAnimation, MinimumPlayerWarmFramesAtStartup);
    animationController?.PrimeAnimationStarts(CorePlayerWarmAnimationKeys, warmFrames);
    PrimeLinkedEffectAnimationWarmup(Animations.Esperanza, "controller_startup");
  }

  static void CollectLibraries(GameObject[] objects, HashSet<string> libraries) {
    if (objects == null || libraries == null) return;
    for (var i = 0; i < objects.Length; i++) {
      var go = objects[i];
      if (go == null) continue;
      var sn = go.GetComponent<SpriteWithNormals>();
      if (sn == null || string.IsNullOrWhiteSpace(sn.libraryName)) continue;
      libraries.Add(sn.libraryName.Trim());
    }
  }

  public int CollectPersistentSkinStartupAddresses(
    List<string> outAddresses,
    HashSet<string> seenAddresses = null,
    int maxUniqueAddresses = int.MaxValue
  ) {
    if (outAddresses == null || maxUniqueAddresses <= 0) {
      return 0;
    }

    if (!runtimeInitialized) {
      return 0;
    }

    EnsurePersistentWarmPlanCache();
    var startingCount = outAddresses.Count;
    CollectPersistentStartupAddresses(
      SkinObjects,
      outAddresses,
      seenAddresses,
      maxUniqueAddresses
    );
    CollectPersistentStartupAddresses(
      GearObjects,
      outAddresses,
      seenAddresses,
      maxUniqueAddresses
    );
    var collectedCount = Mathf.Max(outAddresses.Count - startingCount, 0);
    return collectedCount;
  }

  public int CollectPersistentEffectStartupAddresses(
    List<string> outAddresses,
    HashSet<string> seenAddresses = null,
    int maxUniqueAddresses = int.MaxValue
  ) {
    EnsurePersistentWarmPlanCache();
    var collectedCount = CollectPersistentEffectStartupAddresses(
      outAddresses,
      persistentWarmAnimationScratch,
      seenAddresses,
      maxUniqueAddresses
    );
    return collectedCount;
  }

  public int CollectPersistentEffectStartupAddresses(
    List<string> outAddresses,
    IReadOnlyList<string> animationKeys,
    HashSet<string> seenAddresses = null,
    int maxUniqueAddresses = int.MaxValue
  ) {
    if (outAddresses == null || animationKeys == null || animationKeys.Count <= 0 || maxUniqueAddresses <= 0) {
      return 0;
    }

    if (!runtimeInitialized || effectNode == null) {
      return 0;
    }

    var startingCount = outAddresses.Count;
    var warmFrames = ResolveCoreEffectWarmFramesAtStartup();
    linkedEffectWarmKeyScratch.Clear();
    linkedEffectWarmKeySeenScratch.Clear();
    AnimationLinkUtility.CollectLinkedEffectKeys(
      Animations.Esperanza,
      animationKeys,
      linkedEffectWarmKeyScratch,
      linkedEffectWarmKeySeenScratch
    );
    CollectPersistentEffectStartupAddresses(Effects.Esperanza, linkedEffectWarmKeyScratch, outAddresses, seenAddresses, maxUniqueAddresses);
    CollectPersistentEffectStartupAddresses(Effects.Things, linkedEffectWarmKeyScratch, outAddresses, seenAddresses, maxUniqueAddresses);
    CollectPersistentEffectStartupAddresses(Effects.Imp, linkedEffectWarmKeyScratch, outAddresses, seenAddresses, maxUniqueAddresses);
    linkedProjectileWarmKeyScratch.Clear();
    linkedProjectileWarmKeySeenScratch.Clear();
    AnimationLinkUtility.CollectLinkedProjectileKeys(
      Animations.Esperanza,
      animationKeys,
      linkedProjectileWarmKeyScratch,
      linkedProjectileWarmKeySeenScratch
    );
    if (projectileManager != null &&
        linkedProjectileWarmKeyScratch.Count > 0 &&
        outAddresses.Count < maxUniqueAddresses) {
      projectileManager.EnsurePoolsReady(linkedProjectileWarmKeyScratch);
      projectileManager.CollectPersistentStartupAddresses(
        linkedProjectileWarmKeyScratch,
        outAddresses,
        seenAddresses,
        maxUniqueAddresses,
        warmFrames
      );
    }
    linkedEffectWarmKeyScratch.Clear();
    linkedEffectWarmKeySeenScratch.Clear();
    linkedProjectileWarmKeyScratch.Clear();
    linkedProjectileWarmKeySeenScratch.Clear();
    return Mathf.Max(outAddresses.Count - startingCount, 0);
  }

  public int CollectBootstrapSkinStartupAddresses(
    List<string> outAddresses,
    HashSet<string> seenAddresses = null,
    int maxUniqueAddresses = int.MaxValue
  ) {
    if (outAddresses == null || maxUniqueAddresses <= 0) {
      return 0;
    }

    EnsurePersistentWarmPlanCache();
    var startingCount = outAddresses.Count;
    CollectPersistentStartupAddresses(
      SkinObjects,
      outAddresses,
      seenAddresses,
      maxUniqueAddresses
    );
    CollectPersistentStartupAddresses(
      GearObjects,
      outAddresses,
      seenAddresses,
      maxUniqueAddresses
    );
    var collectedCount = Mathf.Max(outAddresses.Count - startingCount, 0);
    return collectedCount;
  }

  public int CollectPersistentProjectileStartupAssetAddresses(
    List<string> outAddresses,
    IReadOnlyList<string> animationKeys,
    HashSet<string> seenAddresses = null,
    int maxUniqueAddresses = int.MaxValue
  ) {
    if (outAddresses == null || animationKeys == null || animationKeys.Count <= 0 || maxUniqueAddresses <= 0) {
      return 0;
    }

    if (!runtimeInitialized) {
      return 0;
    }

    ResolveProjectileManagerReference("startup_asset_collection");
    if (projectileManager == null) {
      return 0;
    }

    var startingCount = outAddresses.Count;
    linkedProjectileWarmKeyScratch.Clear();
    linkedProjectileWarmKeySeenScratch.Clear();
    AnimationLinkUtility.CollectLinkedProjectileKeys(
      Animations.Esperanza,
      animationKeys,
      linkedProjectileWarmKeyScratch,
      linkedProjectileWarmKeySeenScratch
    );
    if (linkedProjectileWarmKeyScratch.Count > 0) {
      projectileManager.CollectPersistentStartupAssetAddresses(
        linkedProjectileWarmKeyScratch,
        outAddresses,
        seenAddresses,
        maxUniqueAddresses
      );
    }
    linkedProjectileWarmKeyScratch.Clear();
    linkedProjectileWarmKeySeenScratch.Clear();
    return Mathf.Max(outAddresses.Count - startingCount, 0);
  }

  int ResolveCoreEffectWarmFramesAtStartup() {
    var configuredFrames = Mathf.Max(SpriteStreamingRuntimeSettings.AnimationWarmupFrames, 1);
    return Mathf.Max(configuredFrames, MinimumCoreEffectWarmFramesAtStartup);
  }

  void CollectPersistentStartupAddresses(
    GameObject[] objects,
    List<string> outAddresses,
    HashSet<string> seenAddresses,
    int maxUniqueAddresses
  ) {
    if (objects == null || objects.Length == 0 || outAddresses == null) {
      return;
    }

    for (var i = 0; i < objects.Length; i++) {
      if (outAddresses.Count >= maxUniqueAddresses) {
        return;
      }

      var go = objects[i];
      if (go == null) continue;
      var target = go.GetComponent<SpriteWithNormals>();
      CollectPersistentStartupAddresses(
        target,
        outAddresses,
        seenAddresses,
        maxUniqueAddresses
      );
    }
  }

  void CollectPersistentStartupAddresses(
    SpriteWithNormals target,
    List<string> outAddresses,
    HashSet<string> seenAddresses,
    int maxUniqueAddresses
  ) {
    if (target == null || target.DoNotRender || outAddresses == null || maxUniqueAddresses <= 0) {
      return;
    }

    if (!target.IsAnimation) {
      target.CollectAnimationAtlasAddresses(
        target.category,
        0,
        0,
        outAddresses,
        seenAddresses,
        maxUniqueAddresses
      );
      return;
    }

    for (var animationIndex = 0; animationIndex < persistentWarmAnimationScratch.Count; animationIndex++) {
      if (outAddresses.Count >= maxUniqueAddresses) {
        return;
      }

      var animationName = persistentWarmAnimationScratch[animationIndex];
      if (!Animations.Esperanza.TryGetValue(animationName, out var animationData) || animationData == null) {
        continue;
      }

      var category = ResolveEsperanzaAnimationCategory(animationName, animationData);
      var clipStart = Mathf.Max(animationData.start, 1);
      var clipEnd = Mathf.Max(animationData.end, clipStart);
      target.CollectAnimationAtlasAddresses(
        category,
        clipStart,
        clipEnd,
        outAddresses,
        seenAddresses,
        maxUniqueAddresses
      );
    }
  }

  void QueueStartupAppearanceWarmup(string source, bool pauseUntilReady) {
    if (!Application.isPlaying) return;
    if (!runtimeInitialized) return;
    if (string.IsNullOrWhiteSpace(startupAppearanceWarmOwnerId)) return;
    if (!isActiveAndEnabled || !gameObject.activeInHierarchy) return;

    StopStartupAppearanceWarmup();
    startupAppearanceWarmupRoutine = StartCoroutine(WarmStartupAppearanceRoutine(source, pauseUntilReady));
  }

  void StopStartupAppearanceWarmup() {
    if (startupAppearanceWarmupRoutine != null) {
      StopCoroutine(startupAppearanceWarmupRoutine);
      startupAppearanceWarmupRoutine = null;
    }
    if (startupAppearanceWarmupPausedAnimation) {
      animationController?.ResumeAnimation();
      startupAppearanceWarmupPausedAnimation = false;
    }

    startupAppearanceWarmupAddressScratch.Clear();
    startupAppearanceWarmupSeenAddressScratch.Clear();
    if (!string.IsNullOrWhiteSpace(startupAppearanceWarmOwnerId)) {
      TextureResidencyCache.ReleaseOwnerPins(startupAppearanceWarmOwnerId);
    }
  }

  static bool ShouldPauseStartupAppearanceWarmupUntilReady() {
    return SpriteStreamingLoadingState.IsLoadingOverlayActive ||
           StreamingWarmOrchestrator.IsWarmGateRunning;
  }

  IEnumerator WarmStartupAppearanceRoutine(string source, bool pauseUntilReady) {
    startupAppearanceWarmupAddressScratch.Clear();
    startupAppearanceWarmupSeenAddressScratch.Clear();

    var maxAddresses = Mathf.Clamp(SpriteStreamingRuntimeSettings.PinBudgetPlayerAddresses, 128, 768);
    var addressCollectStartedAt = Time.realtimeSinceStartup;
    var collectedCount = CollectStartupAppearanceAddresses(
      startupAppearanceWarmupAddressScratch,
      startupAppearanceWarmupSeenAddressScratch,
      maxAddresses
    );
    while (collectedCount <= 0 &&
           startupAppearanceWarmupAddressScratch.Count <= 0 &&
           Time.realtimeSinceStartup - addressCollectStartedAt < StartupAppearanceAddressCollectionTimeoutSeconds) {
      yield return null;
      collectedCount = CollectStartupAppearanceAddresses(
        startupAppearanceWarmupAddressScratch,
        startupAppearanceWarmupSeenAddressScratch,
        maxAddresses
      );
    }

    if (collectedCount <= 0 || startupAppearanceWarmupAddressScratch.Count <= 0) {
      if (ShouldLogRuntimeInitDebug()) {
        Debug.LogWarning(
          "[GearController][StartupAppearanceWarmup] stage=skip_no_addresses" +
          " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
          " object=" + gameObject.name +
          " current_animation='" + (animationController != null ? animationController.CurrentAnimation : "") + "'" +
          " default_animation='" + (defaultAnimation ?? "") + "'" +
          " skin_objects=" + (SkinObjects != null ? SkinObjects.Length : 0) +
          " gear_objects=" + (GearObjects != null ? GearObjects.Length : 0) +
          " elapsed_ms=" + ((Time.realtimeSinceStartup - addressCollectStartedAt) * 1000f).ToString("0.0")
        );
      }
      startupAppearanceWarmupAddressScratch.Clear();
      startupAppearanceWarmupSeenAddressScratch.Clear();
      startupAppearanceWarmupRoutine = null;
      yield break;
    }

    var loadingOverlayActive = SpriteStreamingLoadingState.IsLoadingOverlayActive;
    var overlayWarmGateManaged = loadingOverlayActive &&
                                 StreamingWarmOrchestrator.IsWarmGateRunning;
    var loadPriority = loadingOverlayActive
      ? TextureResidencyCache.LoadPriority.Warmup
      : TextureResidencyCache.LoadPriority.Immediate;
    var enqueueBudget = pauseUntilReady ? 192 : 128;
    var waitTimeoutSeconds = loadingOverlayActive
      ? 1.5f
      : (pauseUntilReady ? 0.75f : 0.25f);
    var pausedAnimation = false;
    var startedAt = Time.realtimeSinceStartup;

    if (pauseUntilReady && animationController != null && animationController.IsPlaying) {
      animationController.PauseAnimation();
      startupAppearanceWarmupPausedAnimation = true;
      pausedAnimation = true;
    }

    TextureResidencyCache.UpdateOwnerPins(
      startupAppearanceWarmOwnerId,
      TextureResidencyCache.PinClass.Player,
      startupAppearanceWarmupAddressScratch,
      loadPriority
    );
    yield return TextureResidencyCache.RequestLoadBatchThrottled(
      startupAppearanceWarmupAddressScratch,
      loadPriority,
      allowAtlasExpansion: true,
      enqueueBudgetPerFrame: enqueueBudget,
      warmGateManaged: overlayWarmGateManaged
    );
    if (ShouldAllowImmediateStartupTrimmedMetadataLoad()) {
      TrimmedSpriteOffsetResolver.PrimeMetadataBatch(
        startupAppearanceWarmupAddressScratch,
        allowImmediateEditorLoad: true
      );
    }
    else {
      TrimmedSpriteOffsetResolver.QueueWarmupAtlasMetadataBatch(
        startupAppearanceWarmupAddressScratch
      );
    }

    var readyCount = CountReadyStartupAppearanceSamples(out var totalReadySamples);
    while (readyCount < totalReadySamples &&
           Time.realtimeSinceStartup - startedAt < waitTimeoutSeconds) {
      yield return null;
      readyCount = CountReadyStartupAppearanceSamples(out totalReadySamples);
    }

    if (pausedAnimation) {
      animationController.ResumeAnimation();
      startupAppearanceWarmupPausedAnimation = false;
    }

    if (string.Equals(source, "load_gear", StringComparison.OrdinalIgnoreCase)) {
      equippedStartupWarmupCompleted = true;
      TryStartPendingEquipWarmup("startup_warmup_complete");
    }

    if (ShouldLogRuntimeInitDebug()) {
      RuntimeLog.Log(
        "[GearController][StartupAppearanceWarmup]" +
        " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
        " object=" + gameObject.name +
        " addresses=" + startupAppearanceWarmupAddressScratch.Count +
        " ready=" + readyCount + "/" + totalReadySamples +
        " priority=" + loadPriority +
        " paused=" + (pausedAnimation ? 1 : 0) +
        " elapsed_ms=" + ((Time.realtimeSinceStartup - startedAt) * 1000f).ToString("0.0")
      );
    }

    TextureResidencyCache.ReleaseOwnerPins(startupAppearanceWarmOwnerId);
    startupAppearanceWarmupAddressScratch.Clear();
    startupAppearanceWarmupSeenAddressScratch.Clear();
    startupAppearanceWarmupRoutine = null;
  }

  int CollectStartupAppearanceAddresses(
    List<string> outAddresses,
    HashSet<string> seenAddresses = null,
    int maxUniqueAddresses = int.MaxValue
  ) {
    if (outAddresses == null || maxUniqueAddresses <= 0) {
      return 0;
    }

    if (!TryResolveStartupAnimationData(out var animationName, out var animationData)) {
      return 0;
    }
    var category = ResolveEsperanzaAnimationCategory(animationName, animationData);
    var startFrame = Mathf.Max(animationData.start, 1);
    var endFrame = Mathf.Max(animationData.end, startFrame);

    var startingCount = outAddresses.Count;
    CollectStartupAppearanceAddresses(
      SkinObjects,
      category,
      startFrame,
      endFrame,
      outAddresses,
      seenAddresses,
      maxUniqueAddresses
    );
    CollectStartupAppearanceAddresses(
      GearObjects,
      category,
      startFrame,
      endFrame,
      outAddresses,
      seenAddresses,
      maxUniqueAddresses
    );
    return Mathf.Max(outAddresses.Count - startingCount, 0);
  }

  bool TryResolveStartupAnimationWindow(out string category, out int startFrame, out int endFrame) {
    category = "";
    startFrame = 1;
    endFrame = 1;

    if (!TryResolveStartupAnimationData(out var animationName, out var animationData)) {
      return false;
    }

    category = ResolveEsperanzaAnimationCategory(animationName, animationData);
    if (string.IsNullOrWhiteSpace(category)) {
      return false;
    }

    startFrame = Mathf.Max(animationData.start, 1);
    var warmFrames = Mathf.Max(prewarmFramesPerAnimation, MinimumPlayerWarmFramesAtStartup);
    endFrame = Mathf.Max(startFrame, Mathf.Min(Mathf.Max(animationData.end, startFrame), startFrame + warmFrames - 1));
    return true;
  }

  bool TryResolveStartupAnimationData(out string animationName, out AnimData animationData) {
    animationName = animationController != null && !string.IsNullOrWhiteSpace(animationController.CurrentAnimation)
      ? animationController.CurrentAnimation.Trim()
      : (string.IsNullOrWhiteSpace(defaultAnimation) ? "" : defaultAnimation.Trim());
    if (!string.IsNullOrWhiteSpace(animationName) &&
        Animations.Esperanza.TryGetValue(animationName, out animationData) &&
        animationData != null) {
      return true;
    }

    foreach (var pair in Animations.Esperanza) {
      if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null) {
        continue;
      }

      animationName = pair.Key;
      animationData = pair.Value;
      return true;
    }

    animationData = null;
    animationName = "";
    return false;
  }

  void CollectStartupAppearanceAddresses(
    GameObject[] objects,
    string category,
    int startFrame,
    int endFrame,
    List<string> outAddresses,
    HashSet<string> seenAddresses,
    int maxUniqueAddresses
  ) {
    if (objects == null || objects.Length == 0 || outAddresses == null || maxUniqueAddresses <= 0) {
      return;
    }

    for (var i = 0; i < objects.Length; i++) {
      if (outAddresses.Count >= maxUniqueAddresses) {
        return;
      }

      var go = objects[i];
      if (go == null) continue;
      var target = go.GetComponent<SpriteWithNormals>();
      if (target == null || target.DoNotRender) continue;
      target.CollectAnimationAtlasAddresses(
        category,
        startFrame,
        endFrame,
        outAddresses,
        seenAddresses,
        maxUniqueAddresses
      );
    }
  }

  int CountReadyStartupAppearanceSamples(out int totalSampleCount) {
    totalSampleCount = 0;
    if (!TryResolveStartupAnimationWindow(out var category, out var startFrame, out var endFrame)) {
      return 0;
    }

    var readyCount = 0;
    CountAnimationWindowReadiness(SkinObjects, category, startFrame, endFrame, ref readyCount, ref totalSampleCount);
    CountAnimationWindowReadiness(GearObjects, category, startFrame, endFrame, ref readyCount, ref totalSampleCount);
    return readyCount;
  }

  public int CountBootstrapSkinStartupReadySamples(out int totalSampleCount) {
    return CountPersistentSkinStartupReadySamples(out totalSampleCount);
  }

  public int CountPersistentSkinStartupReadySamples(out int totalSampleCount) {
    totalSampleCount = 0;
    var readyCount = 0;
    EnsurePersistentWarmPlanCache();
    CountPersistentStartupReadiness(SkinObjects, ref readyCount, ref totalSampleCount);
    CountPersistentStartupReadiness(GearObjects, ref readyCount, ref totalSampleCount);
    return readyCount;
  }

  void CountPersistentStartupReadiness(
    GameObject[] objects,
    ref int readyCount,
    ref int totalSampleCount
  ) {
    if (objects == null || objects.Length == 0) {
      return;
    }

    for (var i = 0; i < objects.Length; i++) {
      var go = objects[i];
      if (go == null) continue;
      var target = go.GetComponent<SpriteWithNormals>();
      CountPersistentStartupReadiness(target, ref readyCount, ref totalSampleCount);
    }
  }

  void CountPersistentStartupReadiness(
    SpriteWithNormals target,
    ref int readyCount,
    ref int totalSampleCount
  ) {
    if (target == null || target.DoNotRender) {
      return;
    }

    if (!target.IsAnimation) {
      CountAnimationWindowReadiness(target, target.category, 0, 0, ref readyCount, ref totalSampleCount);
      return;
    }

    // Persistent atlas collection may cover an entire clip, but startup only
    // promises the initial warm window. Scanning every frame of every planned
    // animation here repeats the same resolve/readiness work and can turn the
    // reveal coroutine into a multi-million-allocation frame.
    var warmFrames = Mathf.Max(prewarmFramesPerAnimation, MinimumPlayerWarmFramesAtStartup);
    for (var animationIndex = 0; animationIndex < persistentWarmAnimationScratch.Count; animationIndex++) {
      var animationName = persistentWarmAnimationScratch[animationIndex];
      if (!Animations.Esperanza.TryGetValue(animationName, out var animationData) || animationData == null) {
        continue;
      }

      var category = ResolveEsperanzaAnimationCategory(animationName, animationData);
      var clipStart = Mathf.Max(animationData.start, 1);
      var clipEnd = Mathf.Min(
        Mathf.Max(animationData.end, clipStart),
        clipStart + warmFrames - 1
      );
      CountAnimationWindowReadiness(target, category, clipStart, clipEnd, ref readyCount, ref totalSampleCount);
    }
  }

  void CountAnimationWindowReadiness(
    GameObject[] objects,
    string category,
    int startFrame,
    int endFrame,
    ref int readyCount,
    ref int totalSampleCount
  ) {
    if (objects == null || objects.Length == 0) {
      return;
    }

    for (var i = 0; i < objects.Length; i++) {
      var go = objects[i];
      if (go == null) continue;
      var target = go.GetComponent<SpriteWithNormals>();
      if (target == null || target.DoNotRender) continue;
      CountAnimationWindowReadiness(target, category, startFrame, endFrame, ref readyCount, ref totalSampleCount);
    }
  }

  static void CountAnimationWindowReadiness(
    SpriteWithNormals target,
    string category,
    int startFrame,
    int endFrame,
    ref int readyCount,
    ref int totalSampleCount
  ) {
    if (target == null || string.IsNullOrWhiteSpace(category)) {
      return;
    }

    if (!target.IsAnimation) {
      if (!target.TryGetFrameAddressPair(0, out _, category)) return;
      totalSampleCount += 1;
      if (target.GetFrameColdLoadState(0, out _, category).IsCommitReady()) {
        readyCount += 1;
      }
      return;
    }

    var minFrame = Mathf.Max(Mathf.Min(startFrame, endFrame), 1);
    var maxFrame = Mathf.Max(Mathf.Max(startFrame, endFrame), minFrame);
    for (var frame = minFrame; frame <= maxFrame; frame++) {
      if (!target.TryGetFrameAddressPair(frame, out _, category)) continue;
      totalSampleCount += 1;
      if (target.GetFrameColdLoadState(frame, out _, category).IsCommitReady()) {
        readyCount += 1;
      }
    }
  }

  static bool ShouldAllowImmediateStartupTrimmedMetadataLoad() {
#if UNITY_EDITOR
    if (!Application.isEditor || !Application.isPlaying) return false;
    return !SpriteStreamingLoadingState.IsLoadingOverlayActive &&
           !StreamingWarmOrchestrator.IsWarmGateRunning;
#else
    return false;
#endif
  }

  void CollectPersistentEffectStartupAddresses(
    Dictionary<string, EffectData> effects,
    IReadOnlyList<string> effectKeys,
    List<string> outAddresses,
    HashSet<string> seenAddresses,
    int maxUniqueAddresses
  ) {
    if (effects == null || effectNode == null || outAddresses == null || maxUniqueAddresses <= 0) {
      return;
    }

    for (var i = 0; i < effectKeys.Count; i++) {
      if (outAddresses.Count >= maxUniqueAddresses) {
        return;
      }

      var effectKey = string.IsNullOrWhiteSpace(effectKeys[i]) ? "" : effectKeys[i].Trim();
      if (string.IsNullOrWhiteSpace(effectKey) || !effects.TryGetValue(effectKey, out var effectData) || effectData == null) {
        continue;
      }

      var startFrame = Mathf.Max(effectData.start, 1);
      var endFrame = Mathf.Max(effectData.end, startFrame);
      effectNode.CollectAnimationAtlasAddresses(
        effectKey,
        startFrame,
        endFrame,
        outAddresses,
        seenAddresses,
        maxUniqueAddresses
      );
    }
  }

  void PrimeCoreCombatEffectWarmup(string source) {
    if (!Application.isPlaying) return;
    if (string.IsNullOrWhiteSpace(coreEffectWarmOwnerId)) return;

    coreEffectWarmupAddressScratch.Clear();
    coreEffectWarmupSeenAddressScratch.Clear();
    var maxAddresses = Mathf.Max(SpriteStreamingRuntimeSettings.PinBudgetEffectAddresses, 1);
    var collectedCount = CollectPersistentEffectStartupAddresses(
      coreEffectWarmupAddressScratch,
      CoreCombatWarmAnimationKeys,
      coreEffectWarmupSeenAddressScratch,
      maxAddresses
    );
    if (collectedCount <= 0 || coreEffectWarmupAddressScratch.Count <= 0) {
      if (ShouldLogRuntimeInitDebug()) {
        Debug.LogWarning(
          "[GearController] CoreCombatEffectWarmupSkipped" +
          " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
          " object=" + gameObject.name +
          " effect_library='" + (effectNode != null ? effectNode.libraryName : "") + "'" +
          " addresses=" + coreEffectWarmupAddressScratch.Count
        );
      }
      coreEffectWarmupAddressScratch.Clear();
      coreEffectWarmupSeenAddressScratch.Clear();
      return;
    }

    TextureResidencyCache.UpdateOwnerPins(
      coreEffectWarmOwnerId,
      TextureResidencyCache.PinClass.Effect,
      coreEffectWarmupAddressScratch,
      TextureResidencyCache.LoadPriority.Warmup
    );
    TextureResidencyCache.RequestLoadBatch(
      coreEffectWarmupAddressScratch,
      TextureResidencyCache.LoadPriority.Warmup,
      allowAtlasExpansion: true
    );

    if (ShouldLogRuntimeInitDebug()) {
      RuntimeLog.Log(
        "[GearController] CoreCombatEffectWarmup" +
        " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
        " object=" + gameObject.name +
        " addresses=" + coreEffectWarmupAddressScratch.Count +
        " animations=" + CoreCombatWarmAnimationKeys.Length +
        " owner=" + coreEffectWarmOwnerId
      );
    }

    coreEffectWarmupAddressScratch.Clear();
    coreEffectWarmupSeenAddressScratch.Clear();
  }

  void ReleaseCoreCombatEffectWarmupPins() {
    if (string.IsNullOrWhiteSpace(coreEffectWarmOwnerId)) return;
    TextureResidencyCache.ReleaseOwnerPins(coreEffectWarmOwnerId);
  }

  void QueueWarmupForEquippedCharacter(Dictionary<string, string> equippedPartPrefixes) {
    if (!Application.isPlaying || !queueEquippedAnimationWarmup) return;
    if (SceneAppearanceAtlasPinsManaged) return;
    pendingEquipWarmupPartPrefixes = equippedPartPrefixes != null
      ? new Dictionary<string, string>(equippedPartPrefixes, StringComparer.OrdinalIgnoreCase)
      : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    if (!equippedStartupWarmupCompleted) return;
    TryStartPendingEquipWarmup("queue_request");
  }

  void HandleAbilityLoadoutChanged(string formName) {
    var activeForm = EsperanzaForms.GetActive();
    if (!string.Equals(formName, activeForm, StringComparison.OrdinalIgnoreCase)) return;

    InvalidatePersistentWarmPlanCache();
    QueueWarmupForEquippedCharacter(equipPartPrefixScratch);
  }

  void TryStartPendingEquipWarmup(string source) {
    if (!Application.isPlaying || !queueEquippedAnimationWarmup) return;
    if (pendingEquipWarmupPartPrefixes == null) return;
    if (!equippedStartupWarmupCompleted && startupAppearanceWarmupRoutine != null) return;
    if (!isActiveAndEnabled || !gameObject.activeInHierarchy) {
      if (ShouldLogRuntimeInitDebug()) {
        RuntimeLog.Log(
          "[GearController] DeferredEquipWarmup" +
          " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
          " object=" + gameObject.name +
          " enabled=" + (isActiveAndEnabled ? 1 : 0) +
          " active_self=" + (gameObject.activeSelf ? 1 : 0) +
          " active_hierarchy=" + (gameObject.activeInHierarchy ? 1 : 0)
        );
      }
      return;
    }

    StopEquipWarmupQueue();
    equipWarmupRoutine = StartCoroutine(WarmEquippedCharacterRoutine(pendingEquipWarmupPartPrefixes));
  }

  void StopEquipWarmupQueue() {
    if (equipWarmupRoutine == null) return;
    StopCoroutine(equipWarmupRoutine);
    equipWarmupRoutine = null;
    equipWarmAnimationScratch.Clear();
    equipWarmAnimationSeenScratch.Clear();
  }

  IEnumerator WarmEquippedCharacterRoutine(Dictionary<string, string> equippedPartPrefixes) {
    if (!Application.isPlaying) {
      equipWarmupRoutine = null;
      yield break;
    }

    equipWarmupAddressScratch.Clear();
    equipWarmupSeenAddressScratch.Clear();
    BuildEquippedAnimationWarmPlan();
    var loadingOverlayActive = SpriteStreamingLoadingState.IsLoadingOverlayActive;
    var overlayWarmGateManaged = loadingOverlayActive &&
                                 StreamingWarmOrchestrator.IsWarmGateRunning;

    yield return CollectSkinWarmupAddresses(loadingOverlayActive);
    yield return CollectEquippedGearWarmupAddresses(equippedPartPrefixes, loadingOverlayActive);

    var queuedAddressCount = equipWarmupAddressScratch.Count;
    if (queuedAddressCount > 0) {
      var enqueueBudget = Mathf.Max(equipWarmupEnqueueBudgetPerFrame, 50);
      if (!loadingOverlayActive) {
        // Keep gameplay-time warmup background-friendly so it does not create trigger hitches.
        enqueueBudget = Mathf.Min(enqueueBudget, 64);
      }
      yield return TextureResidencyCache.RequestLoadBatchThrottled(
        equipWarmupAddressScratch,
        TextureResidencyCache.LoadPriority.Warmup,
        // Atlas-first preload for equipped parts so frame slices resolve from resident atlases in gameplay.
        allowAtlasExpansion: true,
        enqueueBudgetPerFrame: enqueueBudget,
        warmGateManaged: overlayWarmGateManaged
      );
    }

    if (logEquipWarmupSummary) {
      RuntimeLog.Log(
        "[GearController] EquipWarmupComplete" +
        " queued=" + queuedAddressCount +
        " unique=" + equipWarmupSeenAddressScratch.Count +
        " mapped_parts=" + (equippedPartPrefixes != null ? equippedPartPrefixes.Count : 0) +
        " overlay_active=" + (loadingOverlayActive ? 1 : 0) +
        " warm_gate_running=" + (overlayWarmGateManaged ? 1 : 0)
      );
    }

    equipWarmupAddressScratch.Clear();
    equipWarmupSeenAddressScratch.Clear();
    equipWarmAnimationScratch.Clear();
    equipWarmAnimationSeenScratch.Clear();
    if (ReferenceEquals(pendingEquipWarmupPartPrefixes, equippedPartPrefixes)) {
      pendingEquipWarmupPartPrefixes = null;
    }
    equipWarmupRoutine = null;
  }

  void PrimeEquippedAnimationStartsIfLoading() {
    if (!Application.isPlaying) return;
    if (!SpriteStreamingLoadingState.IsLoadingOverlayActive) return;
    var warmFrames = Mathf.Max(prewarmFramesPerAnimation, MinimumPlayerWarmFramesAtStartup);
    animationController?.PrimeAnimationStarts(CorePlayerWarmAnimationKeys, warmFrames);
    PrimeLinkedEffectAnimationWarmup(Animations.Esperanza, "equip_loading");
  }

  void BuildEquippedAnimationWarmPlan() {
    BuildCharacterAnimationWarmPlan(
      equipWarmAnimationScratch,
      equipWarmAnimationSeenScratch
    );
  }

  void BuildCharacterAnimationWarmPlan(
    List<string> animationPlan,
    HashSet<string> seenAnimations
  ) {
    animationPlan.Clear();
    seenAnimations.Clear();

    for (var i = 0; i < DefaultCharacterAnimationKeys.Length; i++) {
      AddCharacterWarmAnimation(DefaultCharacterAnimationKeys[i], animationPlan, seenAnimations);
    }

    AddCharacterWarmAnimation(defaultAnimation, animationPlan, seenAnimations);
    if (animationController != null) {
      AddCharacterWarmAnimation(animationController.CurrentAnimation, animationPlan, seenAnimations);
    }

    var activeForm = EsperanzaForms.GetActive();
    if (AttacksMapToForms.all.TryGetValue(activeForm, out var activeActionMap) &&
        activeActionMap != null) {
      foreach (var action in activeActionMap) {
        AddCharacterWarmAnimation(action.Value, animationPlan, seenAnimations);
      }
    }

    var equippedAbilities = EsperanzaAbilityLoadouts.GetAbilitiesView(activeForm);
    for (var i = 0; i < equippedAbilities.Count; i++) {
      AddCharacterWarmAnimation(equippedAbilities[i], animationPlan, seenAnimations);
    }


  }

  void EnsurePersistentWarmPlanCache() {
    var activeForm = EsperanzaForms.GetActive() ?? "";
    var contentVersion = ActiveContentRegistryRuntime.ReloadVersion;
    if (persistentWarmPlanAppearanceRevision == appearanceRevision &&
        persistentWarmPlanContentVersion == contentVersion &&
        string.Equals(persistentWarmPlanForm, activeForm, StringComparison.OrdinalIgnoreCase)) {
      return;
    }

    BuildCharacterAnimationWarmPlan(
      persistentWarmAnimationScratch,
      persistentWarmAnimationSeenScratch
    );
    persistentWarmPlanAppearanceRevision = appearanceRevision;
    persistentWarmPlanContentVersion = contentVersion;
    persistentWarmPlanForm = activeForm;
  }

  void InvalidatePersistentWarmPlanCache() {
    persistentWarmPlanAppearanceRevision = -1;
    persistentWarmPlanContentVersion = -1;
    persistentWarmPlanForm = "";
    persistentWarmAnimationScratch.Clear();
    persistentWarmAnimationSeenScratch.Clear();
  }

  bool AddCharacterWarmAnimation(
    string animationName,
    List<string> animationPlan,
    HashSet<string> seenAnimations
  ) {
    if (string.IsNullOrWhiteSpace(animationName)) return false;
    if (!Animations.Esperanza.TryGetValue(animationName, out var animation) || animation == null) {
      return false;
    }
    if (!seenAnimations.Add(animationName)) return false;
    animationPlan.Add(animationName);
    return true;
  }

  IEnumerator CollectSkinWarmupAddresses(bool overlayWarmGateActive) {
    if (SkinObjects == null || SkinObjects.Length == 0) yield break;

    for (var i = 0; i < SkinObjects.Length; i++) {
      var go = SkinObjects[i];
      if (go == null) continue;
      var target = go.GetComponent<SpriteWithNormals>();
      if (target == null) continue;
      yield return CollectPlannedEsperanzaAnimationAtlasAddresses(target, overlayWarmGateActive);
    }
  }

  IEnumerator CollectEquippedGearWarmupAddresses(Dictionary<string, string> equippedPartPrefixes, bool overlayWarmGateActive) {
    if (GearObjects == null || GearObjects.Length == 0) yield break;
    if (equippedPartPrefixes == null || equippedPartPrefixes.Count == 0) yield break;

    for (var i = 0; i < GearObjects.Length; i++) {
      var go = GearObjects[i];
      if (go == null) continue;
      if (!equippedPartPrefixes.TryGetValue(go.name, out var mappedPrefix)) continue;
      var target = go.GetComponent<SpriteWithNormals>();
      if (target == null) continue;

      var normalizedMappedPrefix = mappedPrefix ?? "";
      if (!string.Equals(target.labelPrefix ?? "", normalizedMappedPrefix, StringComparison.OrdinalIgnoreCase)) {
        target.SetLabelPrefix(normalizedMappedPrefix);
      }

      yield return CollectPlannedEsperanzaAnimationAtlasAddresses(target, overlayWarmGateActive);
    }
  }

  IEnumerator CollectPlannedEsperanzaAnimationAtlasAddresses(
    SpriteWithNormals target,
    bool overlayWarmGateActive
  ) {
    if (target == null) yield break;
    if (!target.IsAnimation) {
      target.CollectAnimationAtlasAddresses(
        target.category,
        0,
        0,
        equipWarmupAddressScratch,
        equipWarmupSeenAddressScratch
      );
      yield break;
    }

    var chunkFrames = overlayWarmGateActive
      ? Mathf.Max(equipWarmupFrameChunk, 1)
      : Mathf.Clamp(equipWarmupFrameChunk, 4, 8);
    var chunksPerFrame = overlayWarmGateActive
      ? Mathf.Max(equipWarmupChunksPerFrame, 1)
      : 1;
    var chunksSinceYield = 0;

    for (var animationIndex = 0; animationIndex < equipWarmAnimationScratch.Count; animationIndex++) {
      var animationName = equipWarmAnimationScratch[animationIndex];
      if (!Animations.Esperanza.TryGetValue(animationName, out var anim) || anim == null) continue;

      var category = ResolveEsperanzaAnimationCategory(animationName, anim);
      var clipStart = Mathf.Max(anim.start, 1);
      var clipEnd = Mathf.Max(anim.end, clipStart);
      for (var frameStart = clipStart; frameStart <= clipEnd; frameStart += chunkFrames) {
        var frameEnd = Mathf.Min(frameStart + chunkFrames - 1, clipEnd);
        target.CollectAnimationAtlasAddressesUncached(
          category,
          frameStart,
          frameEnd,
          equipWarmupAddressScratch,
          equipWarmupSeenAddressScratch
        );
        chunksSinceYield++;
        if (chunksSinceYield < chunksPerFrame) continue;
        chunksSinceYield = 0;
        yield return null;
      }
    }
  }

  static string ResolveEsperanzaAnimationCategory(string animationName, AnimData anim) {
    if (anim == null) return animationName ?? "";
    return anim.To == 1 ? "To" : anim.To == 2 ? "To2" : animationName;
  }

  void PrimeLinkedEffectAnimationWarmup(Dictionary<string, AnimData> animationManifest, string source) {
    if (!Application.isPlaying || !effectControllerInitialized || animationManifest == null || animationManifest.Count <= 0) {
      return;
    }

    linkedEffectWarmKeyScratch.Clear();
    linkedEffectWarmKeySeenScratch.Clear();
    AnimationLinkUtility.CollectLinkedEffectKeys(
      animationManifest,
      CorePlayerWarmAnimationKeys,
      linkedEffectWarmKeyScratch,
      linkedEffectWarmKeySeenScratch
    );
    if (linkedEffectWarmKeyScratch.Count > 0) {
      effectAnimationController.PrimeAnimationStarts(linkedEffectWarmKeyScratch, 1);
    }

    if (linkedEffectWarmKeyScratch.Count <= 0 || !ShouldLogRuntimeInitDebug()) {
      linkedEffectWarmKeyScratch.Clear();
      linkedEffectWarmKeySeenScratch.Clear();
      return;
    }

    RuntimeLog.Log(
      "[GearController] PrimedLinkedEffects" +
      " source=" + NormalizeDebugValue(source) +
      " object=" + gameObject.name +
      " count=" + linkedEffectWarmKeyScratch.Count
    );
    linkedEffectWarmKeyScratch.Clear();
    linkedEffectWarmKeySeenScratch.Clear();
  }
}
