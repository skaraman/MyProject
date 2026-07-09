using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public partial class SpriteWithNormals {
  void CancelPendingRequest() {
    if (HasPendingLoadRequest() || _hasDeferredRequest) {
      if (ShouldLogFetch) {
        LogSpriteFetch(
          "cancel_pending",
          "pending_color='" + (_pendingColorAddress ?? "") + "' pending_normal='" + (_pendingNormalAddress ?? "") + "'" +
          " has_deferred=" + (_hasDeferredRequest ? 1 : 0)
        );
      }
    }
    ReleaseLease(ref _pendingColorLease);
    ReleaseLease(ref _pendingNormalLease);
    _pendingColorAddress = _pendingNormalAddress = "";
    _pendingColorSliceAddress = _pendingNormalSliceAddress = "";
    _pendingRetargetAllowedFrame = 0;
    _deferredOverwriteAllowedFrame = 0;
    _pendingSupplementWaitStartedAt = -1f;
    _pendingLoadRequestVersion = 0;
    _pendingLoadPair = default;
    _hasDeferredRequest = false;
    _deferredRequest = default;
    _requestVersion++;
  }

  void ReleaseActiveLeases() {
    ReleaseLease(ref _activeColorLease);
    ReleaseLease(ref _activeNormalLease);
  }

  static void ReleaseLease(ref TextureResidencyCache.Lease lease) {
    if (lease == null) return;
    lease.Release();
    lease = null;
  }

#if UNITY_EDITOR
  static void QueueEditorPreviewRefresh(SpriteWithNormals target) {
    if (target == null) return;
    pendingEditorPreviewRefreshTargets.Add(target);
    if (pendingEditorPreviewRefreshQueued) return;
    pendingEditorPreviewRefreshQueued = true;
    EditorApplication.delayCall += FlushQueuedEditorPreviewRefreshes;
  }

  static void FlushQueuedEditorPreviewRefreshes() {
    pendingEditorPreviewRefreshQueued = false;
    if (Application.isPlaying) {
      pendingEditorPreviewRefreshTargets.Clear();
      return;
    }

    var refreshedCount = RefreshTargetsInEditor(pendingEditorPreviewRefreshTargets);
    pendingEditorPreviewRefreshTargets.Clear();
    if (refreshedCount > 0) {
      Debug.Log("[SpriteWithNormals] Auto-refreshed edit-mode previews after scene object enable. targets=" + refreshedCount);
    }
  }

  static int RefreshTargetsInEditor(IEnumerable<SpriteWithNormals> targets) {
    if (targets == null) return 0;
    var refreshedCount = 0;
    foreach (var target in targets) {
      if (target == null || EditorUtility.IsPersistent(target)) continue;
      if (!target.enabled) continue;
      if (!target.gameObject.scene.IsValid()) continue;

      target.ForceUpdateSpriteAndNormal(target.ResolveRefreshFrame());
      refreshedCount++;
    }
    return refreshedCount;
  }

  public static void RefreshAllInEditor() {
    var targets = Resources.FindObjectsOfTypeAll<SpriteWithNormals>();
    if (targets == null || targets.Length <= 0) return;

    var refreshedCount = RefreshTargetsInEditor(targets);
    if (refreshedCount > 0) {
      Debug.Log("[SpriteWithNormals] Refreshed edit-mode previews after atlas import. targets=" + refreshedCount);
    }
  }
#endif

  bool HasPendingLoadRequest() {
    return _pendingColorLease != null || _pendingNormalLease != null;
  }

  void BeginPendingLoadRequest(int requestVersion, SpriteAddressPair pair) {
    _pendingLoadRequestVersion = requestVersion;
    _pendingLoadPair = pair;
    _pendingSupplementWaitStartedAt = -1f;
  }

  bool TryAdvancePendingLoadRequest() {
    if (!HasPendingLoadRequest()) return false;

    TextureResidencyCache.PumpOncePerFrame();
    if (_pendingColorLease != null && !_pendingColorLease.IsDone) {
      return true;
    }

    if (_pendingSupplementWaitStartedAt < 0f) {
      _pendingSupplementWaitStartedAt = Time.realtimeSinceStartup;
    }

    var allowBlockingEditorSliceFallback = false;
    if (ShouldWaitForPendingSpriteMapSupplement(
          _pendingColorLease,
          _pendingLoadPair.colorAddress,
          _pendingNormalLease,
          _pendingLoadPair.normalAddress)) {
      if ((Time.realtimeSinceStartup - _pendingSupplementWaitStartedAt) < ResolveEditorSpriteMapSupplementWaitTimeoutSeconds()) {
        return true;
      }
      allowBlockingEditorSliceFallback = true;
      if (ShouldLogFetch) {
        LogSpriteFetch("load_sprite_supplement_wait_timeout", "request_version=" + _pendingLoadRequestVersion);
      }
    }

    CompleteLoadedSprites(
      _pendingLoadRequestVersion,
      _pendingColorLease,
      _pendingNormalLease,
      _pendingLoadPair,
      allowBlockingEditorSliceFallback
    );
    return true;
  }

  void ClearPendingState() {
    _pendingColorLease = _pendingNormalLease = null;
    _pendingColorAddress = _pendingNormalAddress = "";
    _pendingColorSliceAddress = _pendingNormalSliceAddress = "";
    _pendingRetargetAllowedFrame = 0;
    _deferredOverwriteAllowedFrame = 0;
    _pendingSupplementWaitStartedAt = -1f;
    _pendingLoadRequestVersion = 0;
    _pendingLoadPair = default;
  }

  void TryStartDeferredRequest() {
    if (!_hasDeferredRequest) return;
    var deferred = _deferredRequest;
    _hasDeferredRequest = false;
    _deferredRequest = default;
    _deferredOverwriteAllowedFrame = 0;
    if (ShouldLogFetch) {
      LogSpriteFetch("start_deferred", "color='" + (deferred.colorAddress ?? "") + "' normal='" + (deferred.normalAddress ?? "") + "'");
    }
    QueueRuntimeLoad(deferred);
  }

  static bool AddressEquals(string left, string right) {
    if (string.IsNullOrEmpty(left)) return string.IsNullOrEmpty(right);
    if (string.IsNullOrEmpty(right)) return false;
    return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
  }

  bool ShouldDeferTargetRetargetForOutstandingMiss(
    SpriteAddressPair nextPair,
    string lookupLibraryName,
    string lookupLabelPrefix,
    string lookupCategory
  ) {
    if (_pendingColorLease == null || _pendingColorLease.IsDone) return false;
    if (Time.frameCount >= _pendingRetargetAllowedFrame) return false;
    if (!string.Equals(lookupLibraryName, _lastLookupLibraryName, StringComparison.OrdinalIgnoreCase)) return false;
    if (!string.Equals(lookupLabelPrefix, _lastLookupLabelPrefix, StringComparison.Ordinal)) return false;
    if (!string.Equals(lookupCategory, _lastLookupCategory, StringComparison.Ordinal)) return false;
    if (AddressEquals(nextPair.RuntimeColorAddress, _pendingColorAddress) &&
        AddressEquals(nextPair.RuntimeNormalAddress, _pendingNormalAddress)) {
      return false;
    }
    return true;
  }

  int ResolveOutstandingRetargetCooldownFrames() {
    var multiplier = Mathf.Clamp(_adaptiveCooldownMultiplier, 1, AdaptiveCooldownMaxMultiplier);
    return OutstandingMissRetargetCooldownFrames * multiplier;
  }

  int ResolveDeferredOverwriteCooldownFrames() {
    var multiplier = Mathf.Clamp(_adaptiveCooldownMultiplier, 1, AdaptiveCooldownMaxMultiplier);
    return DeferredRequestOverwriteCooldownFrames * multiplier;
  }

  void RecordAdaptiveStaleCompletion() {
    var frame = Time.frameCount;
    if (_adaptiveStaleWindowStartFrame < 0 || frame - _adaptiveStaleWindowStartFrame > AdaptiveCooldownWindowFrames) {
      _adaptiveStaleWindowStartFrame = frame;
      _adaptiveStaleCountInWindow = 0;
    }
    _adaptiveStaleCountInWindow++;
    if (_adaptiveStaleCountInWindow < AdaptiveCooldownTriggerCount) return;
    _adaptiveStaleCountInWindow = 0;
    _adaptiveStaleWindowStartFrame = frame;
    _adaptiveCooldownMultiplier = Mathf.Min(_adaptiveCooldownMultiplier + 1, AdaptiveCooldownMaxMultiplier);
    _adaptiveCooldownLastDecayFrame = frame;
  }

  void RecordAdaptiveStableCompletion() {
    var frame = Time.frameCount;
    if (_adaptiveCooldownMultiplier <= 1) return;
    if (_adaptiveCooldownLastDecayFrame >= 0 && frame - _adaptiveCooldownLastDecayFrame < AdaptiveCooldownDecayFrames) return;
    _adaptiveCooldownMultiplier = Mathf.Max(_adaptiveCooldownMultiplier - 1, 1);
    _adaptiveCooldownLastDecayFrame = frame;
  }

  int ResolveLookupFrameForPendingMiss(
    int requestedFrame,
    string lookupLibraryName,
    string lookupLabelPrefix,
    string lookupCategory
  ) {
    var desiredFrame = Mathf.Max(requestedFrame, 1);
    if (!Application.isPlaying) return desiredFrame;
    if (_pendingColorLease == null || _pendingColorLease.IsDone) return desiredFrame;
    if (!_hasLastLookup) return desiredFrame;
    if (!string.Equals(lookupLibraryName, _lastLookupLibraryName, StringComparison.OrdinalIgnoreCase)) return desiredFrame;
    if (!string.Equals(lookupLabelPrefix, _lastLookupLabelPrefix, StringComparison.Ordinal)) return desiredFrame;
    if (!string.Equals(lookupCategory, _lastLookupCategory, StringComparison.Ordinal)) return desiredFrame;
    if (desiredFrame <= _lastLookupFrame) return desiredFrame;
    return Mathf.Min(desiredFrame, _lastLookupFrame + MaxFrameAdvanceWhileMissPending);
  }

  static void AddUniqueAddress(List<string> addresses, string address, HashSet<string> seenAddresses = null, int maxUniqueAddresses = int.MaxValue) {
    var maxAddresses = Mathf.Max(maxUniqueAddresses, 1);
    if (addresses.Count >= maxAddresses) return;
    var normalized = address ?? "";
    if (string.IsNullOrWhiteSpace(normalized)) return;
    if (seenAddresses != null) {
      if (!seenAddresses.Add(normalized)) return;
      if (addresses.Count >= maxAddresses) return;
      addresses.Add(normalized);
      return;
    }
    for (var i = 0; i < addresses.Count; i++) {
      if (string.Equals(addresses[i], normalized, StringComparison.OrdinalIgnoreCase)) return;
    }
    if (addresses.Count >= maxAddresses) return;
    addresses.Add(normalized);
  }

  void TrackTrimmedMetadataWarmupCandidate(string colorAddress) {
    if (!useTrimmedAtlasOffset) return;
    if (string.IsNullOrWhiteSpace(colorAddress)) return;
    TrimmedSpriteOffsetResolver.RegisterWarmupMetadataCandidate(colorAddress);
  }

  void PrimeTrimmedMetadataWarmup(string colorAddress) {
    if (!useTrimmedAtlasOffset) return;
    if (string.IsNullOrWhiteSpace(colorAddress)) return;
    TrackTrimmedMetadataWarmupCandidate(colorAddress);
    TrimmedSpriteOffsetResolver.GetMetadataState(
      colorAddress,
      pump: false,
      requestIfNeeded: true,
      allowImmediateEditorLoad: ShouldAllowImmediateTrimmedMetadataWarmup()
    );
  }

  bool IsTrimmedMetadataReady(string colorAddress) {
    return GetTrimmedMetadataState(colorAddress).IsCommitReady();
  }

  SpriteColdLoadState GetTrimmedMetadataState(string colorAddress, bool requestIfNeeded = false) {
    if (!useTrimmedAtlasOffset) return SpriteColdLoadState.Ready;
    if (string.IsNullOrWhiteSpace(colorAddress)) return SpriteColdLoadState.Ready;
    TrackTrimmedMetadataWarmupCandidate(colorAddress);
    return TrimmedSpriteOffsetResolver.GetMetadataState(
      colorAddress,
      pump: false,
      requestIfNeeded: requestIfNeeded,
      allowImmediateEditorLoad: requestIfNeeded && ShouldAllowImmediateTrimmedMetadataWarmup()
    );
  }

  static bool ShouldAllowImmediateTrimmedMetadataWarmup() {
#if UNITY_EDITOR
    if (!Application.isEditor || !Application.isPlaying) return false;
    return SpriteStreamingLoadingState.IsLoadingOverlayActive || StreamingWarmOrchestrator.IsWarmGateRunning;
#else
    return false;
#endif
  }

  static void WarmupAddress(string address, TextureResidencyCache.LoadPriority priority = TextureResidencyCache.LoadPriority.Warmup) {
    if (string.IsNullOrWhiteSpace(address)) return;
    if (priority == TextureResidencyCache.LoadPriority.Immediate) {
      priority = TextureResidencyCache.LoadPriority.Warmup;
    }
    if (IsAddressWarmupRequestedThisFrame(address)) return;
    if (!TryConsumeWarmupRequestBudget()) return;
    if (ShouldThrottleWarmupRequests()) return;
    MarkAddressWarmupRequestedThisFrame(address);
    TextureResidencyCache.RequestLoad(address, priority);
  }

  static bool ShouldThrottleWarmupRequests() {
    if (!Application.isPlaying) return false;
    var frame = Time.frameCount;
    if (warmupThrottleFrame != frame) {
      warmupThrottleFrame = frame;
      var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
      warmupThrottleActive =
        queue.queuedCount >= WarmupQueueThrottleThreshold ||
        queue.inFlightCount >= WarmupInFlightThrottleThreshold;
    }
    return warmupThrottleActive;
  }

  static bool IsOverlayWarmGateActive() {
    // The protected loading interval starts as soon as the overlay is up, not only after
    // the warm gate begins tracking session work. Startup freezes were slipping through
    // that earlier gap and triggering editor-only fallback/materialization work.
    return SpriteStreamingLoadingState.IsLoadingOverlayActive || StreamingWarmOrchestrator.IsWarmGateRunning;
  }

  bool ShouldDeferRuntimeRefreshForOverlay() {
    if (!Application.isPlaying) return false;
    if (!IsOverlayWarmGateActive()) return false;
    return IsNonCriticalEnvironmentLibrary();
  }

  bool ShouldDeferRuntimeLoadForOverlay() {
    if (!Application.isPlaying) return false;
    if (!IsOverlayWarmGateActive()) return false;
    return IsNonCriticalEnvironmentLibrary();
  }

  bool IsNonCriticalEnvironmentLibrary() {
    return !string.IsNullOrWhiteSpace(libraryName) &&
           libraryName.StartsWith("Environments/", StringComparison.OrdinalIgnoreCase);
  }

  static bool TryConsumeWarmupRequestBudget() {
    if (!Application.isPlaying) return true;
    var frame = Time.frameCount;
    if (warmupBudgetFrame != frame) {
      warmupBudgetFrame = frame;
      warmupRequestsIssuedThisFrame = 0;
      warmupAddressesRequestedThisFrame.Clear();
    }
    if (warmupRequestsIssuedThisFrame >= WarmupRequestBudgetPerFrame) return false;
    warmupRequestsIssuedThisFrame++;
    return true;
  }

  static bool IsAddressWarmupRequestedThisFrame(string address) {
    if (Time.frameCount != warmupBudgetFrame) return false;
    return warmupAddressesRequestedThisFrame.Contains(address);
  }

  static void MarkAddressWarmupRequestedThisFrame(string address) {
    warmupAddressesRequestedThisFrame.Add(address);
  }

  bool TryGetLoadedSpritesForPair(
    SpriteAddressPair pair,
    out Sprite colorSprite,
    out Sprite normalSprite,
    out string sourceTag
  ) {
    colorSprite = null;
    normalSprite = null;
    sourceTag = "none";

    var colorFromLocal = TryGetLocalLoadedSprite(pair.colorAddress, out colorSprite);
    if (!colorFromLocal) {
      if (!TextureResidencyCache.TryGetLoadedSprite(pair.colorAddress, out colorSprite, pump: false) || colorSprite == null) {
        sourceTag = "miss";
        return false;
      }
      CacheLocalLoadedSprite(pair.colorAddress, colorSprite);
    }

    var normalFromLocal = true;
    if (!string.IsNullOrWhiteSpace(pair.normalAddress)) {
      normalFromLocal = TryGetLocalLoadedSprite(pair.normalAddress, out normalSprite);
      if (!normalFromLocal) {
        TextureResidencyCache.TryGetLoadedSprite(pair.normalAddress, out normalSprite, pump: false);
        if (normalSprite != null) CacheLocalLoadedSprite(pair.normalAddress, normalSprite);
      }
    }

    colorSprite = ResolveExpectedSliceSprite(colorSprite, pair.colorAddress, "color");
    normalSprite = ResolveExpectedSliceSprite(normalSprite, pair.normalAddress, "normal");
    if (colorSprite == null) {
      sourceTag = "slice_mismatch";
      return false;
    }
    sourceTag = ResolveCacheSourceTag(colorFromLocal, normalFromLocal, pair.normalAddress);
    return true;
  }

  bool TryGetLocalLoadedSprite(string address, out Sprite sprite) {
    sprite = null;
    var normalizedAddress = address ?? "";
    if (string.IsNullOrWhiteSpace(normalizedAddress)) return false;
    if (!_localLoadedSpriteByAddress.TryGetValue(normalizedAddress, out sprite)) return false;
    if (sprite != null) return true;
    _localLoadedSpriteByAddress.Remove(normalizedAddress);
    return false;
  }

  void CacheLocalLoadedSprite(string address, Sprite sprite) {
    var normalizedAddress = address ?? "";
    if (string.IsNullOrWhiteSpace(normalizedAddress) || sprite == null) return;
    if (_localLoadedSpriteByAddress.Count >= MaxLocalSpriteCacheEntries &&
        !_localLoadedSpriteByAddress.ContainsKey(normalizedAddress)) {
      _localLoadedSpriteByAddress.Clear();
    }
    _localLoadedSpriteByAddress[normalizedAddress] = sprite;
  }

  static string ResolveCacheSourceTag(bool colorFromLocal, bool normalFromLocal, string normalAddress) {
    var hasNormal = !string.IsNullOrWhiteSpace(normalAddress);
    if (!hasNormal) return colorFromLocal ? "local" : "global";
    if (colorFromLocal && normalFromLocal) return "local";
    if (!colorFromLocal && !normalFromLocal) return "global";
    return "mixed";
  }

  void ResetPairLookupCaches() {
    _pairLookupHitCache.Clear();
    _pairLookupMissCache.Clear();
  }

  void ResetTransientResolveRetryState() {
    _transientResolveRetryLibraryName = "";
    _transientResolveRetryLabelPrefix = "";
    _transientResolveRetryCategory = "";
    _transientResolveRetryFrame = int.MinValue;
    _transientResolveRetryCount = 0;
  }

  bool IsSameTransientResolveRetryKey(SpriteLookupKey lookupKey) {
    return _transientResolveRetryFrame == lookupKey.frame &&
           string.Equals(_transientResolveRetryLibraryName, lookupKey.libraryName, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(_transientResolveRetryLabelPrefix, lookupKey.labelPrefix, StringComparison.Ordinal) &&
           string.Equals(_transientResolveRetryCategory, lookupKey.category, StringComparison.Ordinal);
  }

  bool TryScheduleTransientResolveRetry(SpriteLookupKey lookupKey) {
    if (!Application.isPlaying) return false;

    if (!IsSameTransientResolveRetryKey(lookupKey)) {
      _transientResolveRetryLibraryName = lookupKey.libraryName ?? "";
      _transientResolveRetryLabelPrefix = lookupKey.labelPrefix ?? "";
      _transientResolveRetryCategory = lookupKey.category ?? "";
      _transientResolveRetryFrame = lookupKey.frame;
      _transientResolveRetryCount = 0;
    }

    if (_transientResolveRetryCount >= ResolveMissRetryLimit) return false;

    _transientResolveRetryCount++;
    ResetPairLookupCaches();
    SpriteAddressResolver.InvalidateLookup(lookupKey, reloadShard: _transientResolveRetryCount == 1);
    _hasLastLookup = false;
    _hasLastResolveError = true;
    _nextInternalRetryFrame = Time.frameCount + InternalRetryFrames;

    Debug.LogWarning(
      "[SpriteWithNormals] Retrying transient resolve miss on " + gameObject.name +
      " attempt=" + _transientResolveRetryCount + "/" + ResolveMissRetryLimit +
      " key=(" + lookupKey + ")" +
      " reload_shard=" + (_transientResolveRetryCount == 1 ? 1 : 0)
    );
    return true;
  }

  static bool HasBlankCategoryAndLabelPrefix(SpriteLookupKey lookupKey) {
    return string.IsNullOrWhiteSpace(lookupKey.category) &&
           string.IsNullOrWhiteSpace(lookupKey.labelPrefix);
  }

  bool TryApplyBlankSelectionEmptyFallback(SpriteLookupKey lookupKey) {
    if (!HasBlankCategoryAndLabelPrefix(lookupKey)) return false;

    return TryApplyEmptyFallback(lookupKey, "resolve_use_blank_selection_empty_fallback");
  }

  bool TryApplyNoPrefixStaticEmptyFallback(SpriteLookupKey lookupKey) {
    if (isAnimation || !string.IsNullOrWhiteSpace(lookupKey.labelPrefix)) return false;

    return TryApplyEmptyFallback(lookupKey, "resolve_use_empty_fallback");
  }

  bool TryApplyExplicitEmptyLabelFallback(SpriteLookupKey lookupKey) {
    if (!string.Equals((lookupKey.labelPrefix ?? "").Trim(), "Empty", StringComparison.OrdinalIgnoreCase)) return false;

    return TryApplyEmptyFallback(lookupKey, "resolve_use_explicit_empty_label_fallback");
  }

  bool TryApplyEmptyFallback(SpriteLookupKey lookupKey, string logStage) {
    var fallbackPair = SpriteAddressPair.Create(EmptySpriteAssetPath, "");
    if (ShouldLogFetch) {
      LogSpriteFetch(
        logStage,
        "key='" + lookupKey + "' color='" + (fallbackPair.colorAddress ?? "") + "'"
      );
    }

    ResetTransientResolveRetryState();
    _hasLastResolveError = false;
    _hasLastLookup = true;
    _lastLookupLibraryName = lookupKey.libraryName ?? "";
    _lastLookupLabelPrefix = lookupKey.labelPrefix ?? "";
    _lastLookupCategory = lookupKey.category ?? "";
    _lastLookupFrame = lookupKey.frame;
    _targetColorAddress = fallbackPair.RuntimeColorAddress ?? "";
    _targetNormalAddress = fallbackPair.RuntimeNormalAddress ?? "";
    _targetColorSliceAddress = fallbackPair.colorAddress ?? "";
    _targetNormalSliceAddress = fallbackPair.normalAddress ?? "";

    if (Application.isPlaying) {
      CancelPendingRequest();
      ReleaseActiveLeases();
    }

#if UNITY_EDITOR
    if (Application.isEditor) {
      ApplyEditorPreview(fallbackPair, lookupKey);
      return true;
    }
#endif

    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    ClearRenderedSprites();
    return true;
  }

  SpriteAddressPair StripUnavailableRuntimeNormalAddress(SpriteAddressPair pair, SpriteLookupKey lookupKey) {
    if (!string.IsNullOrWhiteSpace(pair.RuntimeNormalAddress) &&
        (pair.RuntimeNormalAddress.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
         pair.RuntimeNormalAddress.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))) {
      pair.normalAddress = "";
      pair.normalAtlasAddress = "";
      pair.normalSpriteName = "";
    }

#if UNITY_EDITOR
    if (!Application.isPlaying || !Application.isEditor) return pair;
    if (string.IsNullOrWhiteSpace(pair.RuntimeNormalAddress)) return pair;
    if (IsEditorRuntimeAtlasAddressAvailable(pair.RuntimeNormalAddress)) return pair;

    var warningKey = "runtime_missing_normal_addressable|" + pair.RuntimeNormalAddress;
    if (_editorPreviewNormalMissWarnings.Add(warningKey)) {
      Debug.LogWarning(
        "[SpriteWithNormals] Dropped unavailable runtime normal atlas on " + gameObject.name +
        " key=(" + lookupKey + ")" +
        " color='" + (pair.RuntimeColorAddress ?? "") + "'" +
        " normal='" + (pair.RuntimeNormalAddress ?? "") + "'"
      );
    }

    pair.normalAddress = "";
    pair.normalAtlasAddress = "";
    pair.normalSpriteName = "";
#endif
    return pair;
  }

  bool TryResolvePairCached(SpriteLookupKey key, out SpriteAddressPair pair, out bool pending) {
    pair = default;
    pending = false;

    var cacheKey = new PairLookupCacheKey(key.libraryName, key.labelPrefix, key.category, key.frame);
    if (_pairLookupHitCache.TryGetValue(cacheKey, out pair)) return true;
    if (_pairLookupMissCache.Contains(cacheKey)) return false;

    if (!TryResolvePair(key, out var resolvedPair)) {
      pending = IsResolvePending(key);
      if (!pending) CachePairLookupMiss(cacheKey);
      return false;
    }

    if (!resolvedPair.HasColor) {
      CachePairLookupMiss(cacheKey);
      return false;
    }

    CachePairLookupHit(cacheKey, resolvedPair);
    pair = resolvedPair;
    return true;
  }

  void CachePairLookupHit(PairLookupCacheKey cacheKey, SpriteAddressPair pair) {
    EnsurePairLookupCacheCapacity();
    _pairLookupMissCache.Remove(cacheKey);
    _pairLookupHitCache[cacheKey] = pair;
  }

  void CachePairLookupMiss(PairLookupCacheKey cacheKey) {
    EnsurePairLookupCacheCapacity();
    _pairLookupHitCache.Remove(cacheKey);
    _pairLookupMissCache.Add(cacheKey);
  }

  void EnsurePairLookupCacheCapacity() {
    if (_pairLookupHitCache.Count < MaxPairLookupCacheEntries &&
        _pairLookupMissCache.Count < MaxPairLookupCacheEntries) return;

    // A simple bounded cache avoids runaway growth without introducing per-frame eviction overhead.
    _pairLookupHitCache.Clear();
    _pairLookupMissCache.Clear();
  }
}
