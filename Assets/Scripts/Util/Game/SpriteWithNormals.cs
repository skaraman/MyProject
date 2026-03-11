using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

public class SpriteWithNormals : MonoBehaviour {
  const float EditorSpriteMapSupplementWaitTimeoutSeconds = 0.5f;
  readonly struct PairLookupCacheKey : IEquatable<PairLookupCacheKey> {
    public readonly string libraryName;
    public readonly string labelPrefix;
    public readonly string animation;
    public readonly int frame;

    public PairLookupCacheKey(string libraryName, string labelPrefix, string animation, int frame) {
      this.libraryName = libraryName ?? "";
      this.labelPrefix = labelPrefix ?? "";
      this.animation = animation ?? "";
      this.frame = frame;
    }

    public bool Equals(PairLookupCacheKey other) {
      return frame == other.frame &&
             string.Equals(libraryName, other.libraryName, StringComparison.OrdinalIgnoreCase) &&
             string.Equals(labelPrefix, other.labelPrefix, StringComparison.OrdinalIgnoreCase) &&
             string.Equals(animation, other.animation, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object obj) {
      return obj is PairLookupCacheKey other && Equals(other);
    }

    public override int GetHashCode() {
      unchecked {
        var hash = 17;
        hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(libraryName ?? "");
        hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(labelPrefix ?? "");
        hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(animation ?? "");
        hash = (hash * 31) + frame;
        return hash;
      }
    }
  }

  public string libraryName = "";
  public string labelPrefix = "";
  public string category = "Breathe";

  [SerializeField] bool isAnimation = true;
  [SerializeField] bool doNotRender;
  [SerializeField] bool useTrimmedAtlasOffset;
  [Header("Debug")]
  [SerializeField] bool enableDebugSpriteFetchLogs = false;
  [SerializeField] bool enableDebugSpriteApplyLogs = false;

  SpriteRenderer _renderer;
  MaterialPropertyBlock _mpb;
  int _lastRequestedFrame;
  int _lastExternalRequestFrame = int.MinValue;
  bool _isInternalTickRequest;

  const int ExternalDriverHoldFrames = 2;
  const int OutstandingMissRetargetCooldownFrames = 2;
  const int DeferredRequestOverwriteCooldownFrames = 2;
  const int MaxFrameAdvanceWhileMissPending = 1;
  const int AdaptiveCooldownWindowFrames = 90;
  const int AdaptiveCooldownTriggerCount = 6;
  const int AdaptiveCooldownMaxMultiplier = 3;
  const int AdaptiveCooldownDecayFrames = 120;
  // FPS-first mode: front-load animation residency during loading gates and avoid
  // runtime look-ahead request churn during gameplay.
  const int PrefetchAheadFrames = 0;
  const int InternalRetryFrames = 2;
  const int MaxPairLookupCacheEntries = 2048;
  static readonly bool ForceDisableDebugLogsForPerfPass = true;
  static int WarmupRequestBudgetPerFrame => Application.isMobilePlatform ? 64 : 256;
  const int WarmupQueueThrottleThreshold = 2048;
  const int WarmupInFlightThrottleThreshold = 128;
  static readonly int NormalMapPropertyId = Shader.PropertyToID("_NormalMap");
  const int MaxLocalSpriteCacheEntries = 4096;
  static int warmupBudgetFrame = -1;
  static int warmupRequestsIssuedThisFrame;
  static int warmupThrottleFrame = -1;
  static bool warmupThrottleActive;
  static readonly HashSet<string> warmupAddressesRequestedThisFrame = new(StringComparer.OrdinalIgnoreCase);

  int _requestVersion;
  string _targetColorAddress = "";
  string _targetNormalAddress = "";
  string _targetColorSliceAddress = "";
  string _targetNormalSliceAddress = "";
  string _pendingColorAddress = "";
  string _pendingNormalAddress = "";
  string _pendingColorSliceAddress = "";
  string _pendingNormalSliceAddress = "";
  string _lastLookupLibraryName = "";
  string _lastLookupLabelPrefix = "";
  string _lastLookupCategory = "";
  int _lastLookupFrame = int.MinValue;
  bool _hasLastLookup;
  bool _hasLastResolveError;
  string _lastResolveErrorLibraryName = "";
  string _lastResolveErrorLabelPrefix= "";
  string _lastResolveErrorCategory = "";
  bool _hasDeferredRequest;
  SpriteAddressPair _deferredRequest;
  static Texture2D _fallbackNormalTexture;
  int _nextInternalRetryFrame;
  readonly HashSet<string> _sliceMismatchWarnings = new(StringComparer.OrdinalIgnoreCase);
  int _staleCompletionStreak;
  int _staleFallbackLogFrame = -1;
  int _pendingRetargetAllowedFrame;
  int _deferredOverwriteAllowedFrame;
  int _lastAppliedNormalTextureId = int.MinValue;
  string _appliedColorSliceAddress = "";
  Vector3 _lastAppliedTrimmedOffsetLocalUnits;
  bool _hasAppliedTrimmedOffset;
  int _adaptiveCooldownMultiplier = 1;
  int _adaptiveStaleWindowStartFrame = -1;
  int _adaptiveStaleCountInWindow;
  int _adaptiveCooldownLastDecayFrame = -1;

  Coroutine _pendingLoadRoutine;
  TextureResidencyCache.Lease _pendingColorLease;
  TextureResidencyCache.Lease _pendingNormalLease;
  TextureResidencyCache.Lease _activeColorLease;
  TextureResidencyCache.Lease _activeNormalLease;
  readonly Dictionary<PairLookupCacheKey, SpriteAddressPair> _pairLookupHitCache = new();
  readonly HashSet<PairLookupCacheKey> _pairLookupMissCache = new();
  readonly Dictionary<string, Sprite> _localLoadedSpriteByAddress = new(StringComparer.OrdinalIgnoreCase);

  void Awake() {
    _renderer = GetComponent<SpriteRenderer>();
    _mpb = new MaterialPropertyBlock();
    SyncRendererVisibility();
  }

  void OnEnable() {
    RefreshTrimmedOffsetForCurrentSprite();
    if (!Application.isPlaying) return;
    _nextInternalRetryFrame = 0;
    _pendingRetargetAllowedFrame = 0;
    _deferredOverwriteAllowedFrame = 0;
    _lastAppliedNormalTextureId = int.MinValue;
    _adaptiveCooldownMultiplier = 1;
    _adaptiveStaleWindowStartFrame = -1;
    _adaptiveStaleCountInWindow = 0;
    _adaptiveCooldownLastDecayFrame = -1;
    SpriteUiPinService.Register(this);
  }

  void Update() {
    if (!Application.isPlaying || !enabled || !gameObject.activeInHierarchy) return;
    if (Time.frameCount - _lastExternalRequestFrame <= ExternalDriverHoldFrames) return;

    // Internal retry path is only needed while unresolved/deferred state exists.
    var needsInternalRetry = _hasDeferredRequest || !_hasLastLookup || _hasLastResolveError;
    if (!needsInternalRetry) return;
    SyncRendererVisibility();
    if (doNotRender) return;
    if (Time.frameCount < _nextInternalRetryFrame) return;
    _nextInternalRetryFrame = Time.frameCount + InternalRetryFrames;

    TextureResidencyCache.PumpOncePerFrame();
    _isInternalTickRequest = true;
    try { UpdateSpriteAndNormal(_lastRequestedFrame); }
    finally { _isInternalTickRequest = false; }
  }

  void OnDisable() {
    if (Application.isPlaying) {
      SpriteUiPinService.Unregister(this);
    }
    ResetAppliedTrimmedOffsetState(clearSliceAddress: false);
    if (!Application.isPlaying) return;
    CancelPendingRequest();
    ReleaseActiveLeases();
    ResetPairLookupCaches();
    _localLoadedSpriteByAddress.Clear();
    _sliceMismatchWarnings.Clear();
    _lastAppliedNormalTextureId = int.MinValue;
  }

  void OnDestroy() {
    if (Application.isPlaying) {
      SpriteUiPinService.Unregister(this);
    }
    ResetAppliedTrimmedOffsetState();
    CancelPendingRequest();
    ReleaseActiveLeases();
    ResetPairLookupCaches();
    _localLoadedSpriteByAddress.Clear();
    _sliceMismatchWarnings.Clear();
    _lastAppliedNormalTextureId = int.MinValue;
  }

  public bool IsAnimation => isAnimation;
  public bool DoNotRender => doNotRender;
  public int LastRequestedFrame => _lastRequestedFrame;

  public void SetAnimation(string value) {
    var previous = category ?? "";
    var next = value ?? "";
    if (!string.Equals(previous, next, StringComparison.Ordinal)) {
      LogSpriteFetch("set_category", "from='" + previous + "' to='" + next + "'");
    }
    category = value;
    _hasLastLookup = false;
  }
  public void SetLibraryName(string value) { libraryName = value; _hasLastLookup = false; ResetPairLookupCaches(); }
  public void SetLabelPrefix(string value) { labelPrefix = value; _hasLastLookup = false; ResetPairLookupCaches(); }
  public void SetIsAnimation(bool value) { isAnimation = value; _hasLastLookup = false; ResetPairLookupCaches(); }
  public void SetUseTrimmedAtlasOffset(bool value) {
    if (useTrimmedAtlasOffset == value) return;
    useTrimmedAtlasOffset = value;
    RefreshTrimmedOffsetForCurrentSprite();
  }

  public void SetDoNotRender(bool value) {
    if (doNotRender == value) return;
    doNotRender = value;
    _hasLastLookup = false;
    ResetPairLookupCaches();
    SyncRendererVisibility();
  }

  public void ForceUpdateSpriteAndNormal() => ForceUpdateSpriteAndNormal(_lastRequestedFrame);

  public void ForceUpdateSpriteAndNormal(int frame) {
    _targetColorAddress = "";
    _targetNormalAddress = "";
    _targetColorSliceAddress = "";
    _targetNormalSliceAddress = "";
    _hasLastLookup = false;
    ResetPairLookupCaches();
    UpdateSpriteAndNormal(frame);
  }

  public void PrimeAnimationWindow(string categoryOverride, int startFrame, int endFrame, int lookAheadFrames) {
    if (!Application.isPlaying || !enabled || !gameObject.activeInHierarchy) return;
    if (doNotRender) return;
    if (!isAnimation) return;

    var lookupLibraryName = libraryName ?? "";
    var lookupLabelPrefix = labelPrefix ?? "";
    var lookupCategory = string.IsNullOrWhiteSpace(categoryOverride) ? (category ?? "") : categoryOverride;
    if (string.IsNullOrWhiteSpace(lookupLibraryName) || string.IsNullOrWhiteSpace(lookupCategory)) return;

    var minFrame = Mathf.Max(Mathf.Min(startFrame, endFrame), 1);
    var maxFrame = Mathf.Max(Mathf.Max(startFrame, endFrame), minFrame);
    var extraFrames = Mathf.Max(lookAheadFrames, 0);
    var warmupMax = maxFrame + extraFrames;

    for (var frame = minFrame; frame <= warmupMax; frame++) {
      var lookupKey = new SpriteLookupKey(lookupLibraryName, lookupLabelPrefix, lookupCategory, frame);
      if (!TryResolvePairCached(lookupKey, out var pair, out _)) continue;
      if (!pair.HasColor) continue;
      var priority = TextureResidencyCache.LoadPriority.Warmup;
      WarmupAddress(pair.RuntimeColorAddress, priority);
      WarmupAddress(pair.RuntimeNormalAddress, priority);
    }
  }

  // returns true if the color sprite is ready; `colorReadyOnly` is set when the
  // color is available but the normal map is still pending, providing a hint for
  // callers that may choose a gentler fallback.
  public bool IsFrameReady(int frame, out bool colorReadyOnly, string categoryOverride = null) {
    colorReadyOnly = false;
    if (!Application.isPlaying) return true;
    if (!enabled || !gameObject.activeInHierarchy || doNotRender) return true;

    var lookupLibraryName = libraryName ?? "";
    var lookupLabelPrefix = labelPrefix ?? "";
    var lookupCategory = string.IsNullOrWhiteSpace(categoryOverride) ? (category ?? "") : categoryOverride;
    var lookupFrame = isAnimation ? Mathf.Max(frame, 1) : 0;

    var lookupKey = new SpriteLookupKey(lookupLibraryName, lookupLabelPrefix, lookupCategory, lookupFrame);
    if (!TryResolvePairCached(lookupKey, out var pair, out var pending)) {
      if (pending) return false;
      return true;
    }
    if (!pair.HasColor) return true;
    var ready = TextureResidencyCache.IsAtlasReady(pair.RuntimeColorAddress, pump: false);
    if (ready && pair.HasNormal &&
        !TextureResidencyCache.IsAtlasReady(pair.RuntimeNormalAddress, pump: false)) {
      colorReadyOnly = true;
    }
    return ready;
  }

  public bool TryGetFrameAddressPair(int frame, out SpriteAddressPair pair, string categoryOverride = null) {
    pair = default;

    var lookupLibraryName = libraryName ?? "";
    var lookupLabelPrefix = labelPrefix ?? "";
    var lookupCategory = string.IsNullOrWhiteSpace(categoryOverride) ? (category ?? "") : categoryOverride;
    if (string.IsNullOrWhiteSpace(lookupLibraryName) || string.IsNullOrWhiteSpace(lookupCategory)) return false;

    var lookupFrame = isAnimation ? Mathf.Max(frame, 1) : 0;
    var lookupKey = new SpriteLookupKey(lookupLibraryName, lookupLabelPrefix, lookupCategory, lookupFrame);
    if (!TryResolvePairCached(lookupKey, out pair, out _)) return false;
    return pair.HasColor;
  }

  public void CollectAnimationWindowAddresses(
    string categoryOverride,
    int startFrame,
    int endFrame,
    int lookAheadFrames,
    List<string> outAddresses,
    int maxUniqueAddresses = int.MaxValue
  ) {
    CollectAnimationWindowAddresses(categoryOverride, startFrame, endFrame, lookAheadFrames, outAddresses, null, maxUniqueAddresses);
  }

  public void CollectAnimationWindowAddresses(
    string categoryOverride,
    int startFrame,
    int endFrame,
    int lookAheadFrames,
    List<string> outAddresses,
    HashSet<string> seenAddresses,
    int maxUniqueAddresses = int.MaxValue
  ) {
    if (outAddresses == null) return;
    var maxAddresses = Mathf.Max(maxUniqueAddresses, 1);
    if (outAddresses.Count >= maxAddresses) return;

    var lookupLibraryName = libraryName ?? "";
    var lookupLabelPrefix = labelPrefix ?? "";
    var lookupCategory = string.IsNullOrWhiteSpace(categoryOverride) ? (category ?? "") : categoryOverride;
    if (string.IsNullOrWhiteSpace(lookupLibraryName) || string.IsNullOrWhiteSpace(lookupCategory)) return;

    if (!isAnimation) {
      if (!TryGetFrameAddressPair(0, out var staticPair, lookupCategory)) return;
      AddUniqueAddress(outAddresses, staticPair.RuntimeColorAddress, seenAddresses, maxAddresses);
      AddUniqueAddress(outAddresses, staticPair.RuntimeNormalAddress, seenAddresses, maxAddresses);
      return;
    }

    var minFrame = Mathf.Max(Mathf.Min(startFrame, endFrame), 1);
    var maxFrame = Mathf.Max(Mathf.Max(startFrame, endFrame), minFrame);
    var lookAhead = Mathf.Max(lookAheadFrames, 0);
    var finalFrame = maxFrame + lookAhead;

    for (var frame = minFrame; frame <= finalFrame; frame++) {
      if (outAddresses.Count >= maxAddresses) break;
      var lookupKey = new SpriteLookupKey(lookupLibraryName, lookupLabelPrefix, lookupCategory, frame);
      if (!TryResolvePairCached(lookupKey, out var pair, out _)) continue;
      AddUniqueAddress(outAddresses, pair.RuntimeColorAddress, seenAddresses, maxAddresses);
      AddUniqueAddress(outAddresses, pair.RuntimeNormalAddress, seenAddresses, maxAddresses);
    }
  }

  public bool IsUiTarget() {
    return GetComponentInParent<Canvas>(true) != null;
  }

  public void UpdateSpriteAndNormal(int frame) {
    _lastRequestedFrame = frame;
    if (Application.isPlaying && !_isInternalTickRequest) _lastExternalRequestFrame = Time.frameCount;
    if (Application.isPlaying && (!enabled || !gameObject.activeInHierarchy)) return;

    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    if (_renderer == null) return;
    SyncRendererVisibility();
    if (doNotRender) return;

    var lookupLibraryName = libraryName ?? "";
    var lookupCategory = category ?? "";
    var lookupLabelPrefix = labelPrefix ?? "";
    var lookupFrame = isAnimation
      ? ResolveLookupFrameForPendingMiss(frame, lookupLibraryName, lookupLabelPrefix, lookupCategory)
      : 0;
    PunchLeftTraceGate.LogFrameRequest(
      gameObject.name,
      lookupCategory,
      lookupFrame,
      lookupLibraryName,
      lookupLabelPrefix
    );
    LogSpriteFetch(
      "lookup_begin",
      "lib='" + lookupLibraryName + "' label='" + lookupLabelPrefix + "' category='" + lookupCategory + "'" +
      " frame=" + lookupFrame + " internal=" + (_isInternalTickRequest ? 1 : 0)
    );

    if (_hasLastLookup &&
        lookupFrame == _lastLookupFrame &&
        string.Equals(lookupLibraryName, _lastLookupLibraryName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(lookupLabelPrefix, _lastLookupLabelPrefix, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(lookupCategory, _lastLookupCategory, StringComparison.OrdinalIgnoreCase)) {
      LogSpriteFetch("skip_same_lookup");
      return;
    }

    var lookupKey = new SpriteLookupKey(lookupLibraryName, lookupLabelPrefix, lookupCategory, lookupFrame);
    if (!TryResolvePairCached(lookupKey, out var pair, out var pending)) {
      if (pending) {
        LogSpriteFetch("resolve_pending", "key='" + lookupKey + "'");
        return;
      }
      LogSpriteFetch("resolve_failed", "key='" + lookupKey + "'");
      ReportResolveError(lookupKey);
      return;
    }
    LogSpriteFetch(
      "resolve_success",
      "key='" + lookupKey + "' color='" + (pair.colorAddress ?? "") + "' normal='" + (pair.normalAddress ?? "") + "'"
    );

    _hasLastResolveError = false;
    _hasLastLookup = true;
    _lastLookupLibraryName = lookupLibraryName;
    _lastLookupLabelPrefix = lookupLabelPrefix;
    _lastLookupCategory = lookupCategory;
    _lastLookupFrame = lookupFrame;

    if (AddressEquals(pair.colorAddress, _targetColorSliceAddress) &&
        AddressEquals(pair.normalAddress, _targetNormalSliceAddress)) {
      LogSpriteFetch("skip_same_target_addresses");
      return;
    }

    if (Application.isPlaying && ShouldDeferTargetRetargetForOutstandingMiss(pair)) {
      return;
    }

    _targetColorAddress = pair.RuntimeColorAddress ?? "";
    _targetNormalAddress = pair.RuntimeNormalAddress ?? "";
    _targetColorSliceAddress = pair.colorAddress ?? "";
    _targetNormalSliceAddress = pair.normalAddress ?? "";

    if (Application.isPlaying) {
      if (TryApplyLoadedSpritesFromCache(pair, cancelPendingIfAny: true)) {
        LogSpriteFetch("use_cached_pair");
        return;
      }

      if (isAnimation && PrefetchAheadFrames > 0 && !IsOverlayWarmGateActive()) {
        WarmupUpcomingAnimationFrames(lookupLibraryName, lookupLabelPrefix, lookupCategory, lookupFrame, pair);
      }
      LogSpriteFetch(
        "fetch_miss_queue_runtime_load",
        "color='" + (_targetColorAddress ?? "") + "' normal='" + (_targetNormalAddress ?? "") + "'"
      );
      QueueRuntimeLoad(pair, cacheKnownMiss: true);
      return;
    }

#if UNITY_EDITOR
    ApplyEditorPreview(pair, lookupKey);
#endif
  }

  public void FlipSprite(bool flip) {
    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    if (_renderer != null) {
      _renderer.flipX = flip;
      RefreshTrimmedOffsetForCurrentSprite();
    }
  }

  void QueueRuntimeLoad(SpriteAddressPair pair, bool cacheKnownMiss = false) {
    if (_pendingLoadRoutine != null) {
      if (AddressEquals(_pendingColorAddress, pair.RuntimeColorAddress) && AddressEquals(_pendingNormalAddress, pair.RuntimeNormalAddress)) {
        LogSpriteFetch("queue_skip_same_pending", "color='" + (_pendingColorAddress ?? "") + "' normal='" + (_pendingNormalAddress ?? "") + "'");
        return;
      }
      if (_hasDeferredRequest) {
        if (AddressEquals(_deferredRequest.RuntimeColorAddress, pair.RuntimeColorAddress) &&
            AddressEquals(_deferredRequest.RuntimeNormalAddress, pair.RuntimeNormalAddress)) {
          LogSpriteFetch("queue_skip_same_deferred", "color='" + (pair.colorAddress ?? "") + "' normal='" + (pair.normalAddress ?? "") + "'");
          return;
        }
        if (Time.frameCount < _deferredOverwriteAllowedFrame) {
          LogSpriteFetch(
            "queue_deferred_overwrite_throttled",
            "incoming_color='" + (pair.colorAddress ?? "") + "' incoming_normal='" + (pair.normalAddress ?? "") + "'"
          );
          return;
        }
      }
      _deferredRequest = pair;
      _hasDeferredRequest = true;
      _deferredOverwriteAllowedFrame = Time.frameCount + ResolveDeferredOverwriteCooldownFrames();
      LogSpriteFetch("queue_deferred", "color='" + (pair.colorAddress ?? "") + "' normal='" + (pair.normalAddress ?? "") + "'");
      return;
    }
    LogSpriteFetch("queue_start_now");
    StartRuntimeLoad(pair, cacheKnownMiss);
  }

  void StartRuntimeLoad(SpriteAddressPair pair, bool cacheKnownMiss = false) {
    LogSpriteFetch("load_start", "color='" + (pair.colorAddress ?? "") + "' normal='" + (pair.normalAddress ?? "") + "'");
    var hasPendingState = _pendingLoadRoutine != null ||
                          _pendingColorLease != null ||
                          _pendingNormalLease != null ||
                          _hasDeferredRequest;
    if (!cacheKnownMiss) {
      if (!hasPendingState && TryApplyLoadedSpritesFromCache(pair)) {
        LogSpriteFetch("use_fastpath_cache_hit");
        _pendingColorAddress = _pendingNormalAddress = "";
        TryStartDeferredRequest();
        return;
      }
    }

    if (hasPendingState) {
      CancelPendingRequest();
    }
    _pendingColorAddress = pair.RuntimeColorAddress ?? "";
    _pendingNormalAddress = pair.RuntimeNormalAddress ?? "";
    _pendingColorSliceAddress = pair.colorAddress ?? "";
    _pendingNormalSliceAddress = pair.normalAddress ?? "";

    if (!cacheKnownMiss) {
      if (TryApplyLoadedSpritesFromCache(pair)) {
        LogSpriteFetch("use_cache_hit_applied");
        _pendingColorAddress = _pendingNormalAddress = "";
        _pendingColorSliceAddress = _pendingNormalSliceAddress = "";
        TryStartDeferredRequest();
        return;
      }
    }
    LogSpriteFetch("fetch_cache_miss");

    var colorPriority = ResolveActiveFrameLoadPriority();

    var colorLease = TextureResidencyCache.AcquireAsync(pair.RuntimeColorAddress, colorPriority);
    if (colorLease == null) {
      Debug.LogError($"[SpriteWithNormals] Failed to request color atlas '{pair.RuntimeColorAddress}' on {gameObject.name}");
      _pendingColorAddress = _pendingNormalAddress = "";
      _pendingColorSliceAddress = _pendingNormalSliceAddress = "";
      TryStartDeferredRequest();
      return;
    }
    LogSpriteFetch("load_requested_color", "priority=" + colorPriority + " is_done=" + (colorLease.IsDone ? 1 : 0));

    // Color drives visual frame readiness; keep normal map on warmup priority to reduce queue pressure.
    var normalPriority = TextureResidencyCache.LoadPriority.Warmup;
    var normalLease = string.IsNullOrWhiteSpace(pair.RuntimeNormalAddress)
      ? null
      : TextureResidencyCache.AcquireAsync(pair.RuntimeNormalAddress, normalPriority);

    if (!string.IsNullOrWhiteSpace(pair.RuntimeNormalAddress) && normalLease == null)
      Debug.LogError($"[SpriteWithNormals] Failed to request normal atlas '{pair.RuntimeNormalAddress}' on {gameObject.name}");
    else if (!string.IsNullOrWhiteSpace(pair.RuntimeNormalAddress))
      LogSpriteFetch("load_requested_normal", "priority=" + normalPriority + " is_done=" + (normalLease != null && normalLease.IsDone ? 1 : 0));

    _pendingColorLease = colorLease;
    _pendingNormalLease = normalLease;
    var requestVersion = ++_requestVersion;
    if (colorLease.IsDone) {
      if (ShouldWaitForPendingSpriteMapSupplement(colorLease, pair.colorAddress, normalLease, pair.normalAddress)) {
        LogSpriteFetch("load_wait_editor_supplement", "request_version=" + requestVersion);
        _pendingLoadRoutine = StartCoroutine(ApplyLoadedSprites(requestVersion, colorLease, normalLease, pair));
        return;
      }
      LogSpriteFetch("load_color_immediate_complete", "request_version=" + requestVersion);
      CompleteLoadedSprites(requestVersion, colorLease, normalLease, pair);
      return;
    }

    _pendingRetargetAllowedFrame = Time.frameCount + ResolveOutstandingRetargetCooldownFrames();
    LogSpriteFetch("load_wait_async", "request_version=" + requestVersion);
    _pendingLoadRoutine = StartCoroutine(ApplyLoadedSprites(requestVersion, colorLease, normalLease, pair));
  }

  TextureResidencyCache.LoadPriority ResolveActiveFrameLoadPriority() {
    if (!Application.isPlaying) return TextureResidencyCache.LoadPriority.Warmup;
    if (_isInternalTickRequest) return TextureResidencyCache.LoadPriority.Warmup;
    if (IsOverlayWarmGateActive()) return TextureResidencyCache.LoadPriority.Warmup;
    return TextureResidencyCache.LoadPriority.Immediate;
  }

  IEnumerator ApplyLoadedSprites(int requestVersion, TextureResidencyCache.Lease colorLease, TextureResidencyCache.Lease normalLease, SpriteAddressPair pair) {
    while (colorLease != null && !colorLease.IsDone) {
      TextureResidencyCache.PumpOncePerFrame();
      yield return null;
    }

    var supplementWaitStartedAt = Time.realtimeSinceStartup;
    while (ShouldWaitForPendingSpriteMapSupplement(colorLease, pair.colorAddress, normalLease, pair.normalAddress)) {
      if ((Time.realtimeSinceStartup - supplementWaitStartedAt) >= EditorSpriteMapSupplementWaitTimeoutSeconds) {
        LogSpriteFetch("load_editor_supplement_wait_timeout", "request_version=" + requestVersion);
        break;
      }
      TextureResidencyCache.PumpOncePerFrame();
      yield return null;
    }

    CompleteLoadedSprites(requestVersion, colorLease, normalLease, pair);
  }

  bool TryApplyLoadedSpritesFromCache(SpriteAddressPair pair, bool cancelPendingIfAny = false) {
    if (!TryGetLoadedSpritesForPair(pair, out var colorSprite, out var normalSprite, out var sourceTag)) {
      LogSpriteFetch("use_lookup_miss_color", "address='" + (pair.colorAddress ?? "") + "' source='local+global'");
      return false;
    }
    LogSpriteFetch(
      "use_lookup_hit_color",
      "address='" + (pair.colorAddress ?? "") + "' sprite='" + colorSprite.name + "' source='" + sourceTag + "'"
    );
    if (!string.IsNullOrWhiteSpace(pair.normalAddress)) {
      LogSpriteFetch(
        normalSprite != null ? "use_lookup_hit_normal" : "use_lookup_miss_normal",
        "address='" + (pair.normalAddress ?? "") + "'" +
        (normalSprite != null ? " sprite='" + normalSprite.name + "'" : "") +
        " source='" + sourceTag + "'"
      );
    }

    if (cancelPendingIfAny &&
        (_pendingLoadRoutine != null || _pendingColorLease != null || _pendingNormalLease != null || _hasDeferredRequest)) {
      CancelPendingRequest();
    }

    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    if (_renderer != null) ApplySprites(colorSprite, normalSprite, pair.colorAddress);

    ReleaseActiveLeases();
    return true;
  }

  void CompleteLoadedSprites(int requestVersion, TextureResidencyCache.Lease colorLease, TextureResidencyCache.Lease normalLease, SpriteAddressPair pair) {
    if (requestVersion != _requestVersion) {
      LogSpriteFetch("complete_version_mismatch", "incoming=" + requestVersion + " current=" + _requestVersion);
      ReleaseLease(ref colorLease);
      ReleaseLease(ref normalLease);
      return;
    }

    ClearPendingState();

    var staleCompletion =
      !AddressEquals(pair.RuntimeColorAddress, _targetColorAddress) ||
      !AddressEquals(pair.RuntimeNormalAddress, _targetNormalAddress);
    if (staleCompletion) {
      RecordAdaptiveStaleCompletion();
      LogSpriteFetch(
        "complete_stale",
        "requested_color='" + (pair.colorAddress ?? "") + "' target_color='" + (_targetColorAddress ?? "") + "'" +
        " requested_normal='" + (pair.normalAddress ?? "") + "' target_normal='" + (_targetNormalAddress ?? "") + "'"
      );
      if (TryApplyStaleCompletionFallback(colorLease, normalLease, pair)) {
        LogSpriteFetch("complete_stale_fallback_applied");
        TryStartDeferredRequest();
        return;
      }
      // Keep one stale-drop before fallback so very short races still favor newest request.
      LogSpriteFetch("complete_stale_dropped");
      ReleaseLease(ref colorLease);
      ReleaseLease(ref normalLease);
      TryStartDeferredRequest();
      return;
    }
    _staleCompletionStreak = 0;

    var colorSliceAddress = string.IsNullOrWhiteSpace(_targetColorSliceAddress) ? pair.colorAddress : _targetColorSliceAddress;
    var normalSliceAddress = string.IsNullOrWhiteSpace(_targetNormalSliceAddress) ? pair.normalAddress : _targetNormalSliceAddress;
    var colorSprite = ResolveLeaseSprite(colorLease, colorSliceAddress);
    if (colorSprite == null) {
      LogSpriteFetch("complete_color_missing", "address='" + (colorSliceAddress ?? "") + "'");
      Debug.LogError($"[SpriteWithNormals] Failed to resolve color sprite '{colorSliceAddress}' on {gameObject.name}");
      ReleaseLease(ref colorLease);
      ReleaseLease(ref normalLease);
      TryStartDeferredRequest();
      return;
    }
    colorSprite = ResolveExpectedSliceSprite(colorSprite, colorSliceAddress, "color");
    CacheLocalLoadedSprite(colorSliceAddress, colorSprite);

    var normalSprite = ResolveLeaseSprite(normalLease, normalSliceAddress);
    normalSprite = ResolveExpectedSliceSprite(normalSprite, normalSliceAddress, "normal");
    if (normalSprite != null) CacheLocalLoadedSprite(normalSliceAddress, normalSprite);
    if (normalLease != null && normalLease.IsDone && normalSprite == null && !string.IsNullOrWhiteSpace(normalSliceAddress))
      Debug.LogError($"[SpriteWithNormals] Failed to resolve normal sprite '{normalSliceAddress}' on {gameObject.name}");
    LogSpriteFetch(
      "complete_apply",
      "color='" + (colorSprite != null ? colorSprite.name : "") + "' normal='" + (normalSprite != null ? normalSprite.name : "") + "'"
    );

    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    if (_renderer != null) ApplySprites(colorSprite, normalSprite, colorSliceAddress);
    RecordAdaptiveStableCompletion();

    ReleaseActiveLeases();
    _activeColorLease = colorLease;
    _activeNormalLease = normalLease;
    TryStartDeferredRequest();
  }

  bool TryApplyStaleCompletionFallback(
    TextureResidencyCache.Lease colorLease,
    TextureResidencyCache.Lease normalLease,
    SpriteAddressPair pair
  ) {
    _staleCompletionStreak++;
    if (_staleCompletionStreak < 2) return false;

    var colorSprite = ResolveLeaseSprite(colorLease, pair.colorAddress);
    if (colorSprite == null) return false;
    colorSprite = ResolveExpectedSliceSprite(colorSprite, pair.colorAddress, "color");
    CacheLocalLoadedSprite(pair.colorAddress, colorSprite);

    var normalSprite = ResolveLeaseSprite(normalLease, pair.normalAddress);
    normalSprite = ResolveExpectedSliceSprite(normalSprite, pair.normalAddress, "normal");
    if (normalSprite != null) CacheLocalLoadedSprite(pair.normalAddress, normalSprite);

    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    if (_renderer != null) ApplySprites(colorSprite, normalSprite, pair.colorAddress);

    if (!ForceDisableDebugLogsForPerfPass && _staleFallbackLogFrame != Time.frameCount) {
      _staleFallbackLogFrame = Time.frameCount;
      Debug.LogWarning(
        "[SpriteWithNormals] Applied stale-frame fallback on " + gameObject.name +
        " streak=" + _staleCompletionStreak +
        " requested_frame=" + _lastRequestedFrame
      );
    }

    _staleCompletionStreak = 0;
    ReleaseActiveLeases();
    _activeColorLease = colorLease;
    _activeNormalLease = normalLease;
    return true;
  }

  void WarmupUpcomingAnimationFrames(string libraryName, string labelPrefix, string category, int lookupFrame, SpriteAddressPair currentPair) {
    if (lookupFrame <= 0 || string.IsNullOrWhiteSpace(libraryName) || string.IsNullOrWhiteSpace(category)) return;
    if (_pendingLoadRoutine != null || _hasDeferredRequest) return;
    if (ShouldThrottleWarmupRequests()) return;

    for (var offset = 1; offset <= PrefetchAheadFrames; offset++) {
      var warmupKey = new SpriteLookupKey(libraryName, labelPrefix, category, lookupFrame + offset);
      if (!TryResolvePairCached(warmupKey, out var warmupPair, out var pending)) {
        if (pending) return;
        break;
      }
      if (!AddressEquals(warmupPair.RuntimeColorAddress, currentPair.RuntimeColorAddress) || !AddressEquals(warmupPair.RuntimeNormalAddress, currentPair.RuntimeNormalAddress)) {
        WarmupAddress(warmupPair.RuntimeColorAddress, TextureResidencyCache.LoadPriority.Warmup);
        WarmupAddress(warmupPair.RuntimeNormalAddress, TextureResidencyCache.LoadPriority.Warmup);
      }
    }
  }

  static Sprite ResolveLeaseSprite(TextureResidencyCache.Lease lease, string sliceOrAtlasAddress) {
    if (lease == null || !lease.IsDone || !lease.IsSuccess) return null;
    if (lease.TryGetSpriteByAddress(sliceOrAtlasAddress, out var sprite)) return sprite;
    if (SpriteSliceAddressUtility.TryParseSliceAddress(sliceOrAtlasAddress, out _, out _)) return null;
    return lease.Sprite;
  }

  static bool ShouldWaitForPendingSpriteMapSupplement(
    TextureResidencyCache.Lease colorLease,
    string colorSliceAddress,
    TextureResidencyCache.Lease normalLease,
    string normalSliceAddress
  ) {
    return ShouldWaitForPendingSpriteMapSupplement(colorLease, colorSliceAddress) ||
           ShouldWaitForPendingSpriteMapSupplement(normalLease, normalSliceAddress);
  }

  static bool ShouldWaitForPendingSpriteMapSupplement(TextureResidencyCache.Lease lease, string sliceOrAtlasAddress) {
    if (!Application.isEditor) return false;
    if (lease == null || !lease.IsDone || !lease.IsSuccess) return false;
    if (!lease.HasPendingSpriteMapSupplement) return false;
    if (!SpriteSliceAddressUtility.TryParseSliceAddress(sliceOrAtlasAddress, out _, out _)) return false;
    return !lease.TryGetSpriteByAddress(sliceOrAtlasAddress, out _);
  }

  void ApplySprites(Sprite colorSprite, Sprite normalSprite, string colorSliceAddress) {
    if (_renderer.sprite != colorSprite) {
      _renderer.sprite = colorSprite;
    }
    ApplyConfiguredTrimmedOffset(colorSprite, colorSliceAddress);
    var normalTexture = normalSprite != null ? normalSprite.texture : GetFallbackNormalTexture();
    var normalTextureId = normalTexture != null ? normalTexture.GetInstanceID() : 0;
    if (normalTextureId == _lastAppliedNormalTextureId) return;

    _mpb ??= new MaterialPropertyBlock();
    _renderer.GetPropertyBlock(_mpb);
    if (normalTexture != null) _mpb.SetTexture(NormalMapPropertyId, normalTexture);
    _renderer.SetPropertyBlock(_mpb);
    _lastAppliedNormalTextureId = normalTextureId;
  }

  void ApplyConfiguredTrimmedOffset(Sprite colorSprite, string colorSliceAddress) {
    _appliedColorSliceAddress = colorSliceAddress ?? "";
    if (!useTrimmedAtlasOffset || colorSprite == null || string.IsNullOrWhiteSpace(_appliedColorSliceAddress)) {
      ResetAppliedTrimmedOffsetState(clearSliceAddress: colorSprite == null || !useTrimmedAtlasOffset);
      return;
    }

    if (!TryResolveTrimmedOffsetLocalUnits(_appliedColorSliceAddress, colorSprite, out var offsetLocalUnits)) {
      ClearAppliedTrimmedOffset();
      return;
    }

    ApplyTrimmedOffsetLocal(offsetLocalUnits, colorSprite, _appliedColorSliceAddress);
  }

  bool TryResolveTrimmedOffsetLocalUnits(string colorSliceAddress, Sprite colorSprite, out Vector3 offsetLocalUnits) {
    offsetLocalUnits = Vector3.zero;
    if (colorSprite == null || string.IsNullOrWhiteSpace(colorSliceAddress)) return false;
    var flipX = _renderer != null && _renderer.flipX;
    var flipY = _renderer != null && _renderer.flipY;
    return TrimmedSpriteOffsetResolver.TryGetExactLocalOffset(
      colorSliceAddress,
      colorSprite,
      out offsetLocalUnits,
      flipX,
      flipY,
      OnTrimmedOffsetMetadataReady);
  }

  void ApplyTrimmedOffsetLocal(Vector3 offsetLocalUnits, Sprite colorSprite, string colorSliceAddress) {
    var currentLocalPosition = transform.localPosition;
    var baseLocalPosition = _hasAppliedTrimmedOffset
      ? currentLocalPosition - _lastAppliedTrimmedOffsetLocalUnits
      : currentLocalPosition;
    transform.localPosition = baseLocalPosition + offsetLocalUnits;
    _lastAppliedTrimmedOffsetLocalUnits = offsetLocalUnits;
    _hasAppliedTrimmedOffset = true;
    LogSpriteApply(
      "offset_applied",
      colorSprite,
      null,
      "address='" + (colorSliceAddress ?? "") + "'" +
      " local_offset=(" + offsetLocalUnits.x.ToString("0.###") + "," + offsetLocalUnits.y.ToString("0.###") + ")"
    );
  }

  void ClearAppliedTrimmedOffset() {
    if (!_hasAppliedTrimmedOffset) return;
    transform.localPosition -= _lastAppliedTrimmedOffsetLocalUnits;
    _lastAppliedTrimmedOffsetLocalUnits = Vector3.zero;
    _hasAppliedTrimmedOffset = false;
  }

  void ResetAppliedTrimmedOffsetState(bool clearSliceAddress = true) {
    ClearAppliedTrimmedOffset();
    if (clearSliceAddress) {
      _appliedColorSliceAddress = "";
    }
  }

  void RefreshTrimmedOffsetForCurrentSprite() {
    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    var currentSprite = _renderer != null ? _renderer.sprite : null;
    ApplyConfiguredTrimmedOffset(currentSprite, _appliedColorSliceAddress);
  }

  void OnTrimmedOffsetMetadataReady() {
    if (!useTrimmedAtlasOffset) return;
    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    if (_renderer == null || _renderer.sprite == null) return;
    if (string.IsNullOrWhiteSpace(_appliedColorSliceAddress)) return;
    ApplyConfiguredTrimmedOffset(_renderer.sprite, _appliedColorSliceAddress);
  }

  static Texture2D GetFallbackNormalTexture() {
    if (_fallbackNormalTexture != null) return _fallbackNormalTexture;
    _fallbackNormalTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) {
      name = "SpriteWithNormals_FallbackNormal",
      wrapMode = TextureWrapMode.Repeat,
      filterMode = FilterMode.Bilinear,
      hideFlags = HideFlags.HideAndDontSave
    };
    _fallbackNormalTexture.SetPixel(0, 0, new Color(0.5f, 0.5f, 1f, 1f));
    _fallbackNormalTexture.Apply(false, true);
    return _fallbackNormalTexture;
  }

  void CancelPendingRequest() {
    if (_pendingLoadRoutine != null || _pendingColorLease != null || _pendingNormalLease != null || _hasDeferredRequest) {
      LogSpriteFetch(
        "cancel_pending",
        "pending_color='" + (_pendingColorAddress ?? "") + "' pending_normal='" + (_pendingNormalAddress ?? "") + "'" +
        " has_deferred=" + (_hasDeferredRequest ? 1 : 0)
      );
    }
    if (_pendingLoadRoutine != null) { StopCoroutine(_pendingLoadRoutine); _pendingLoadRoutine = null; }
    ReleaseLease(ref _pendingColorLease);
    ReleaseLease(ref _pendingNormalLease);
    _pendingColorAddress = _pendingNormalAddress = "";
    _pendingColorSliceAddress = _pendingNormalSliceAddress = "";
    _pendingRetargetAllowedFrame = 0;
    _deferredOverwriteAllowedFrame = 0;
    _staleCompletionStreak = 0;
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

  void ClearPendingState() {
    _pendingLoadRoutine = null;
    _pendingColorLease = _pendingNormalLease = null;
    _pendingColorAddress = _pendingNormalAddress = "";
    _pendingColorSliceAddress = _pendingNormalSliceAddress = "";
    _pendingRetargetAllowedFrame = 0;
    _deferredOverwriteAllowedFrame = 0;
  }

  void TryStartDeferredRequest() {
    if (!_hasDeferredRequest) return;
    var deferred = _deferredRequest;
    _hasDeferredRequest = false;
    _deferredRequest = default;
    _deferredOverwriteAllowedFrame = 0;
    LogSpriteFetch("start_deferred", "color='" + (deferred.colorAddress ?? "") + "' normal='" + (deferred.normalAddress ?? "") + "'");
    QueueRuntimeLoad(deferred);
  }

  static bool AddressEquals(string left, string right) {
    if (string.IsNullOrEmpty(left)) return string.IsNullOrEmpty(right);
    if (string.IsNullOrEmpty(right)) return false;
    return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
  }

  bool ShouldDeferTargetRetargetForOutstandingMiss(SpriteAddressPair nextPair) {
    if (_pendingColorLease == null || _pendingColorLease.IsDone) return false;
    if (Time.frameCount >= _pendingRetargetAllowedFrame) return false;
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
    if (!string.Equals(lookupLabelPrefix, _lastLookupLabelPrefix, StringComparison.OrdinalIgnoreCase)) return desiredFrame;
    if (!string.Equals(lookupCategory, _lastLookupCategory, StringComparison.OrdinalIgnoreCase)) return desiredFrame;
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
    return SpriteStreamingLoadingState.IsLoadingOverlayActive && StreamingWarmOrchestrator.IsWarmGateRunning;
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

  void SyncRendererVisibility() {
    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    if (_renderer != null) _renderer.enabled = !doNotRender;
  }

  void LogSpriteFetch(string stage, string details = "") {
    if (ForceDisableDebugLogsForPerfPass) return;
    if (!enableDebugSpriteFetchLogs || !Application.isPlaying) return;
    var normalizedStage = stage ?? "";
    var mode = normalizedStage.StartsWith("use_", StringComparison.Ordinal)
      ? "use"
      : normalizedStage.StartsWith("fetch_", StringComparison.Ordinal)
        ? "fetch"
        : "state";
    Debug.Log(
      "[SpriteWithNormals][Fetch] object='" + gameObject.name +
      "' category='" + (category ?? "") +
      "' requested_frame=" + _lastRequestedFrame +
      " request_version=" + _requestVersion +
      " mode='" + mode + "'" +
      " stage='" + normalizedStage + "'" +
      (string.IsNullOrWhiteSpace(details) ? "" : " " + details)
    );
  }

  void LogSpriteApply(string stage, Sprite colorSprite, Sprite normalSprite, string details = "") {
    if (ForceDisableDebugLogsForPerfPass) return;
    if (!enableDebugSpriteApplyLogs || !Application.isPlaying) return;
    Debug.Log(
      "[SpriteWithNormals][Apply] object='" + gameObject.name +
      "' category='" + (category ?? "") +
      "' requested_frame=" + _lastRequestedFrame +
      " stage='" + (stage ?? "") + "'" +
      " color='" + (colorSprite != null ? colorSprite.name : "") +
      "' normal='" + (normalSprite != null ? normalSprite.name : "") + "'" +
      (string.IsNullOrWhiteSpace(details) ? "" : " " + details)
    );
  }

  void ReportResolveError(SpriteLookupKey lookupKey) {
    if (_hasLastResolveError &&
        string.Equals(_lastResolveErrorLibraryName, lookupKey.libraryName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(_lastResolveErrorLabelPrefix, lookupKey.labelPrefix, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(_lastResolveErrorCategory, lookupKey.category, StringComparison.OrdinalIgnoreCase)) return;

    _hasLastResolveError = true;
    _lastResolveErrorLibraryName = lookupKey.libraryName ?? "";
    _lastResolveErrorLabelPrefix = lookupKey.labelPrefix ?? "";
    _lastResolveErrorCategory = lookupKey.category ?? "";
    Debug.LogError($"[SpriteWithNormals] No sprite mapping found for {lookupKey} on {gameObject.name}");
  }

  static bool TryResolvePair(SpriteLookupKey key, out SpriteAddressPair pair) => SpriteAddressResolver.TryResolve(key, out pair);
  static bool IsResolvePending(SpriteLookupKey key) => SpriteAddressResolver.IsLookupPending(key);

  Sprite ResolveExpectedSliceSprite(Sprite loadedSprite, string sliceAddress, string channel) {
    if (loadedSprite == null) return null;
    if (!SpriteSliceAddressUtility.TryParseSliceAddress(sliceAddress, out _, out var expectedSpriteName)) return loadedSprite;
    if (string.Equals(loadedSprite.name, expectedSpriteName, StringComparison.Ordinal)) return loadedSprite;
    if (SpriteSliceAddressUtility.HasEquivalentNumericLabel(loadedSprite.name, expectedSpriteName)) return loadedSprite;

#if UNITY_EDITOR
    if (Application.isEditor &&
        SpriteAddressResolver.TryLoadEditorSprite(sliceAddress, out var editorSprite) &&
        editorSprite != null) {
      WarnSliceMismatchOnce(sliceAddress, channel, loadedSprite.name, expectedSpriteName, corrected: true);
      return editorSprite;
    }
#endif

    WarnSliceMismatchOnce(sliceAddress, channel, loadedSprite.name, expectedSpriteName, corrected: false);
    return loadedSprite;
  }

  void WarnSliceMismatchOnce(string sliceAddress, string channel, string loadedName, string expectedName, bool corrected) {
    var key = $"{channel}|{sliceAddress}";
    if (!_sliceMismatchWarnings.Add(key)) return;

    Debug.LogWarning(
      "[SpriteWithNormals] Slice mismatch on " + gameObject.name +
      " channel=" + channel +
      " expected='" + expectedName + "'" +
      " loaded='" + (loadedName ?? "") + "'" +
      " address='" + (sliceAddress ?? "") + "'" +
      " corrected=" + (corrected ? 1 : 0)
    );
  }

#if UNITY_EDITOR
  void ApplyEditorPreview(SpriteAddressPair pair, SpriteLookupKey lookupKey) {
    if (!SpriteAddressResolver.TryLoadEditorSprite(pair.colorAddress, out var colorSprite) || colorSprite == null) {
      Debug.LogError($"[SpriteWithNormals] Editor preview color sprite not found for '{pair.colorAddress}' ({lookupKey})");
      return;
    }
    SpriteAddressResolver.TryLoadEditorSprite(pair.normalAddress, out var normalSprite);
    if (!string.IsNullOrWhiteSpace(pair.normalAddress) && normalSprite == null)
      Debug.LogError($"[SpriteWithNormals] Editor preview normal sprite not found for '{pair.normalAddress}' ({lookupKey})");
    ApplySprites(colorSprite, normalSprite, pair.colorAddress);
  }
#endif
}

#if UNITY_EDITOR
[CanEditMultipleObjects]
[CustomEditor(typeof(SpriteWithNormals))]
public class SpriteWithNormalsEditor : Editor {
  readonly struct DropdownOption {
    public readonly string label;
    public readonly string value;
    public DropdownOption(string label, string value) { this.label = label ?? ""; this.value = value ?? ""; }
  }

  sealed class ShardSelectionOptions {
    public readonly List<string> categories;
    public readonly Dictionary<string, List<string>> labelPrefixesByCategory;
    public ShardSelectionOptions(List<string> categories, Dictionary<string, List<string>> labelPrefixesByCategory) {
      this.categories = categories ?? new List<string>();
      this.labelPrefixesByCategory = labelPrefixesByCategory ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    }
  }

  static class SpriteIndexInspectorData {
    static readonly Dictionary<string, string> shardPathByLibrary = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, ShardSelectionOptions> shardOptionsByPath = new(StringComparer.OrdinalIgnoreCase);
    static string manifestTextCache;
    static string loadIssue;

    public static void Invalidate() {
      manifestTextCache = null;
      loadIssue = "";
      shardPathByLibrary.Clear();
      shardOptionsByPath.Clear();
    }

    public static string GetLoadIssue() { EnsureManifestParsed(); return loadIssue; }

    public static List<string> GetLibraries() {
      EnsureManifestParsed();
      var libraries = new List<string>(shardPathByLibrary.Keys);
      libraries.Sort(StringComparer.OrdinalIgnoreCase);
      return libraries;
    }

    public static List<string> GetCategories(string libraryName) {
      if (!TryGetShardOptions(libraryName, out var options)) return new List<string>();
      return new List<string>(options.categories);
    }

    public static List<string> GetLabelPrefixes(string libraryName, string category) {
      if (!TryGetShardOptions(libraryName, out var options)) return new List<string>();
      var normalizedCategory = NormalizeToken(category);
      if (string.IsNullOrWhiteSpace(normalizedCategory)) return new List<string>();
      return options.labelPrefixesByCategory.TryGetValue(normalizedCategory, out var prefixes) && prefixes != null
        ? new List<string>(prefixes)
        : new List<string>();
    }

    static bool TryGetShardOptions(string libraryName, out ShardSelectionOptions options) {
      options = null;
      EnsureManifestParsed();
      var normalizedLibrary = NormalizeToken(libraryName);
      if (string.IsNullOrWhiteSpace(normalizedLibrary) || !shardPathByLibrary.TryGetValue(normalizedLibrary, out var shardPath)) return false;
      var normalizedShardPath = NormalizeToken(shardPath);
      if (string.IsNullOrWhiteSpace(normalizedShardPath)) return false;
      if (shardOptionsByPath.TryGetValue(normalizedShardPath, out options)) return options != null;
      options = ParseShardOptions(normalizedShardPath);
      shardOptionsByPath[normalizedShardPath] = options;
      return options != null;
    }

    static void EnsureManifestParsed() {
      var manifestAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(SpriteStreamingConfig.ManifestAssetPath);
      var manifestText = manifestAsset?.text ?? "";
      if (string.Equals(manifestText, manifestTextCache, StringComparison.Ordinal)) return;

      manifestTextCache = manifestText;
      loadIssue = "";
      shardPathByLibrary.Clear();
      shardOptionsByPath.Clear();

      if (string.IsNullOrWhiteSpace(manifestText)) {
        loadIssue = $"Sprite index manifest is missing or empty at '{SpriteStreamingConfig.ManifestAssetPath}'. Run Tools > Sprite Streaming > 2) Rebuild Runtime Index.";
        return;
      }

      foreach (var rawLine in manifestText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
        var line = rawLine.TrimStart('\uFEFF');
        if (line.StartsWith("#", StringComparison.Ordinal)) continue;
        var cols = line.Split('\t');
        if (cols.Length < 3) continue;
        var lib = NormalizeToken(Unescape(cols[0]));
        var shard = NormalizeToken(Unescape(cols[2]));
        if (!string.IsNullOrWhiteSpace(lib) && !string.IsNullOrWhiteSpace(shard)) shardPathByLibrary[lib] = shard;
      }

      if (shardPathByLibrary.Count == 0)
        loadIssue = $"Sprite index manifest was found but contains no library rows at '{SpriteStreamingConfig.ManifestAssetPath}'. Rebuild the runtime index.";
    }

    static ShardSelectionOptions ParseShardOptions(string shardPath) {
      var shardAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(shardPath);
      if (shardAsset == null || string.IsNullOrWhiteSpace(shardAsset.text)) return null;

      var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      var labelPrefixSets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

      foreach (var rawLine in shardAsset.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
        var line = rawLine.TrimStart('\uFEFF');
        if (line.StartsWith("#", StringComparison.Ordinal)) continue;
        var cols = line.Split('\t');
        if (cols.Length < 2) continue;
        var labelPrefix = NormalizeToken(Unescape(cols[0]));
        var cat = NormalizeToken(Unescape(cols[1]));
        if (string.IsNullOrWhiteSpace(cat)) continue;
        categories.Add(cat);
        if (!labelPrefixSets.TryGetValue(cat, out var set)) labelPrefixSets[cat] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        set.Add(labelPrefix);
      }

      var orderedCategories = new List<string>(categories);
      orderedCategories.Sort(StringComparer.OrdinalIgnoreCase);
      var orderedPrefixes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
      foreach (var kvp in labelPrefixSets) {
        var ordered = new List<string>(kvp.Value);
        ordered.Sort(StringComparer.OrdinalIgnoreCase);
        orderedPrefixes[kvp.Key] = ordered;
      }

      return new ShardSelectionOptions(orderedCategories, orderedPrefixes);
    }

    static string NormalizeToken(string value) {
      if (string.IsNullOrWhiteSpace(value)) return "";
      var trimmed = value.Trim();
      if (trimmed.Length >= 2) {
        var first = trimmed[0]; var last = trimmed[trimmed.Length - 1];
        if (first == '"' && last == '"') trimmed = trimmed.Substring(1, trimmed.Length - 2);
        else if (first == '\'' && last == '\'') trimmed = trimmed.Substring(1, trimmed.Length - 2).Replace("''", "'");
      }
      return string.IsNullOrWhiteSpace(trimmed) ? "" : trimmed.Trim();
    }

    static string Unescape(string value) =>
      string.IsNullOrEmpty(value) ? "" : value.Replace("\\t", "\t").Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\\\", "\\");
  }

  SerializedProperty libraryNameProperty;
  SerializedProperty labelPrefixProperty;
  SerializedProperty categoryProperty;
  SerializedProperty isAnimationProperty;
  SerializedProperty doNotRenderProperty;
  SerializedProperty useTrimmedAtlasOffsetProperty;

  void OnEnable() {
    libraryNameProperty = serializedObject.FindProperty(nameof(SpriteWithNormals.libraryName));
    labelPrefixProperty = serializedObject.FindProperty(nameof(SpriteWithNormals.labelPrefix));
    categoryProperty = serializedObject.FindProperty(nameof(SpriteWithNormals.category));
    isAnimationProperty = serializedObject.FindProperty("isAnimation");
    doNotRenderProperty = serializedObject.FindProperty("doNotRender");
    useTrimmedAtlasOffsetProperty = serializedObject.FindProperty("useTrimmedAtlasOffset");
  }

  public override void OnInspectorGUI() {
    serializedObject.Update();

    var changed = DrawDropdown(libraryNameProperty, "Library Name", BuildLibraryOptions());
    changed |= DrawDropdown(categoryProperty, "Category", BuildCategoryOptions(libraryNameProperty.stringValue));
    changed |= DrawDropdown(labelPrefixProperty, "Label Prefix", BuildLabelPrefixOptions(libraryNameProperty.stringValue, categoryProperty.stringValue));
    EditorGUILayout.PropertyField(isAnimationProperty, new GUIContent("Is Animation", "When enabled, frame input drives this label prefix as an animation loop."));
    EditorGUILayout.PropertyField(doNotRenderProperty, new GUIContent("Do Not Render", "When enabled, this component keeps the SpriteRenderer disabled."));
    EditorGUI.BeginChangeCheck();
    EditorGUILayout.PropertyField(useTrimmedAtlasOffsetProperty, new GUIContent("Use Trimmed Atlas Offset", "When enabled, this component reads sibling atlas offset metadata and repositions the sprite to match its original slot."));
    var trimmedOffsetToggleChanged = EditorGUI.EndChangeCheck();

    var loadIssue = SpriteIndexInspectorData.GetLoadIssue();
    if (!string.IsNullOrWhiteSpace(loadIssue)) EditorGUILayout.HelpBox(loadIssue, MessageType.Warning);

    var serializedChanged = serializedObject.ApplyModifiedProperties();
    if (serializedChanged || changed) MarkTargetsDirty(targets);
    if (trimmedOffsetToggleChanged) {
      foreach (var obj in targets) {
        var t = obj as SpriteWithNormals;
        if (t == null) continue;
        var refreshFrame = t.IsAnimation ? Mathf.Max(t.LastRequestedFrame, 1) : 0;
        t.ForceUpdateSpriteAndNormal(refreshFrame);
      }
    }

    EditorGUILayout.BeginHorizontal();
    if (!Application.isPlaying && GUILayout.Button("Refresh Sprite + Normal")) {
      foreach (var obj in targets) {
        var t = obj as SpriteWithNormals;
        if (t == null) continue;
        t.ForceUpdateSpriteAndNormal(t.IsAnimation ? 1 : 0);
        EditorUtility.SetDirty(t);
        PrefabUtility.RecordPrefabInstancePropertyModifications(t);
      }
    }
    if (GUILayout.Button("Reload Index", GUILayout.Width(104f))) SpriteIndexInspectorData.Invalidate();
    EditorGUILayout.EndHorizontal();
  }

  static List<DropdownOption> BuildLibraryOptions() {
    var options = new List<DropdownOption> { new("(Empty)", "") };
    foreach (var lib in SpriteIndexInspectorData.GetLibraries())
      if (!string.IsNullOrWhiteSpace(lib)) options.Add(new(lib, lib));
    return options;
  }

  static List<DropdownOption> BuildCategoryOptions(string libraryName) {
    var options = new List<DropdownOption> { new("(Empty)", "") };
    foreach (var cat in SpriteIndexInspectorData.GetCategories(libraryName))
      if (!string.IsNullOrWhiteSpace(cat)) options.Add(new(cat, cat));
    return options;
  }

  static List<DropdownOption> BuildLabelPrefixOptions(string libraryName, string category) {
    var options = new List<DropdownOption> { new("(No Prefix)", "") };
    foreach (var prefix in SpriteIndexInspectorData.GetLabelPrefixes(libraryName, category))
      if (!string.IsNullOrWhiteSpace(prefix)) options.Add(new(prefix, prefix));
    return options;
  }

  static bool DrawDropdown(SerializedProperty property, string label, List<DropdownOption> options) {
    if (property == null) return false;
    if (options == null || options.Count == 0) { EditorGUILayout.PropertyField(property, new GUIContent(label)); return false; }

    var propertyValue = property.stringValue ?? "";
    var localOptions = options;
    var currentIndex = IndexOfValue(localOptions, propertyValue);
    if (currentIndex < 0) {
      localOptions = new List<DropdownOption>(options.Count + 1) {
        new(string.IsNullOrWhiteSpace(propertyValue) ? "(Empty)" : propertyValue + " (current)", propertyValue)
      };
      localOptions.AddRange(options);
      currentIndex = 0;
    }

    var display = new string[localOptions.Count];
    for (var i = 0; i < localOptions.Count; i++) display[i] = localOptions[i].label;

    var previousMixedState = EditorGUI.showMixedValue;
    EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
    EditorGUI.BeginChangeCheck();
    var selectedIndex = EditorGUILayout.Popup(new GUIContent(label), currentIndex, display);
    var popupChanged = EditorGUI.EndChangeCheck();
    EditorGUI.showMixedValue = previousMixedState;

    if (selectedIndex < 0 || selectedIndex >= localOptions.Count) selectedIndex = currentIndex;
    var selectedValue = localOptions[selectedIndex].value ?? "";
    if (!popupChanged && string.Equals(propertyValue, selectedValue, StringComparison.Ordinal)) return false;
    property.stringValue = selectedValue;
    return true;
  }

  static int IndexOfValue(List<DropdownOption> options, string value) {
    var target = value ?? "";
    for (var i = 0; i < options.Count; i++)
      if (string.Equals(options[i].value, target, StringComparison.Ordinal)) return i;
    return -1;
  }

  static void MarkTargetsDirty(UnityEngine.Object[] objectTargets) {
    foreach (var obj in objectTargets) {
      var t = obj as SpriteWithNormals;
      if (t == null) continue;
      EditorUtility.SetDirty(t);
      PrefabUtility.RecordPrefabInstancePropertyModifications(t);
      var scene = t.gameObject.scene;
      if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
    }
  }
}
#endif
