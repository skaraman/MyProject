using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

public class SpriteWithNormals : MonoBehaviour {
  const float EditorSpriteMapSupplementWaitTimeoutSeconds = 0.5f;
  const float EditorSpriteMapSupplementWaitTimeoutDuringOverlaySeconds = 2.0f;
  const float EditorSliceResolveRetryGraceSeconds = 6.0f;
  const string EmptySpriteAssetPath = "Assets/Sprites/Core/Empty.png";
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
             string.Equals(labelPrefix, other.labelPrefix, StringComparison.Ordinal) &&
             string.Equals(animation, other.animation, StringComparison.Ordinal);
    }

    public override bool Equals(object obj) {
      return obj is PairLookupCacheKey other && Equals(other);
    }

    public override int GetHashCode() {
      unchecked {
        var hash = 17;
        hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(libraryName ?? "");
        hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(labelPrefix ?? "");
        hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(animation ?? "");
        hash = (hash * 31) + frame;
        return hash;
      }
    }
  }

  readonly struct PaddedSpriteCacheKey : IEquatable<PaddedSpriteCacheKey> {
    public readonly ulong sourceSpriteId;
    public readonly int marginX;
    public readonly int marginY;
    public readonly bool useNormalFill;

    public PaddedSpriteCacheKey(ulong sourceSpriteId, int marginX, int marginY, bool useNormalFill) {
      this.sourceSpriteId = sourceSpriteId;
      this.marginX = marginX;
      this.marginY = marginY;
      this.useNormalFill = useNormalFill;
    }

    public bool Equals(PaddedSpriteCacheKey other) {
      return sourceSpriteId == other.sourceSpriteId &&
             marginX == other.marginX &&
             marginY == other.marginY &&
             useNormalFill == other.useNormalFill;
    }

    public override bool Equals(object obj) {
      return obj is PaddedSpriteCacheKey other && Equals(other);
    }

    public override int GetHashCode() {
      unchecked {
        var hash = sourceSpriteId.GetHashCode();
        hash = (hash * 397) ^ marginX;
        hash = (hash * 397) ^ marginY;
        hash = (hash * 397) ^ (useNormalFill ? 1 : 0);
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
  [Header("Shader Margins")]
  [SerializeField, Min(0f)] float shaderMarginPixelsX;
  [SerializeField, Min(0f)] float shaderMarginPixelsY;
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
  const int ResolveMissRetryLimit = 2;
  // FPS-first mode: front-load animation residency during loading gates and avoid
  // runtime look-ahead request churn during gameplay.
  const int PrefetchAheadFrames = 0;
  const int InternalRetryFrames = 2;
  const int MaxPairLookupCacheEntries = 2048;
  static readonly bool ForceDisableDebugLogsForPerfPass = true;
  static int WarmupRequestBudgetPerFrame => Application.isMobilePlatform ? 64 : 256;
  const int WarmupQueueThrottleThreshold = 2048;
  const int WarmupInFlightThrottleThreshold = 128;
  const int OverlayEnableRefreshBudgetPerFrame = 8;
  const int GameplayEnableRefreshBudgetPerFrame = 24;
  static readonly int NormalMapPropertyId = Shader.PropertyToID("_NormalMap");
  static readonly Color32 TransparentPaddingColor = new(0, 0, 0, 0);
  static readonly Color32 FlatNormalPaddingColor = new(128, 128, 255, 255);
  const int MaxLocalSpriteCacheEntries = 4096;
  const int MaxGeneratedPaddedSpriteCacheEntries = 256;
  static int warmupBudgetFrame = -1;
  static int warmupRequestsIssuedThisFrame;
  static int warmupThrottleFrame = -1;
  static bool warmupThrottleActive;
  static readonly HashSet<string> warmupAddressesRequestedThisFrame = new(StringComparer.OrdinalIgnoreCase);
  static readonly Queue<SpriteWithNormals> pendingRuntimeEnableRefreshQueue = new();
  static readonly HashSet<SpriteWithNormals> pendingRuntimeEnableRefreshSet = new();
  static int pendingRuntimeEnableRefreshFrame = -1;
#if UNITY_EDITOR
  static readonly HashSet<SpriteWithNormals> pendingEditorPreviewRefreshTargets = new();
  static bool pendingEditorPreviewRefreshQueued;
  static readonly Dictionary<string, bool> editorRuntimeAtlasAvailabilityByPath = new(StringComparer.OrdinalIgnoreCase);
#endif

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
  string _transientResolveRetryLibraryName = "";
  string _transientResolveRetryLabelPrefix = "";
  string _transientResolveRetryCategory = "";
  int _transientResolveRetryFrame = int.MinValue;
  int _transientResolveRetryCount;
  bool _hasDeferredRequest;
  SpriteAddressPair _deferredRequest;
  static Texture2D _fallbackNormalTexture;
  int _nextInternalRetryFrame;
  readonly HashSet<string> _sliceMismatchWarnings = new(StringComparer.OrdinalIgnoreCase);
  int _staleCompletionStreak;
  int _staleFallbackLogFrame = -1;
  int _pendingRetargetAllowedFrame;
  int _deferredOverwriteAllowedFrame;
  ulong _lastAppliedNormalTextureId = ulong.MaxValue;
  string _appliedColorSliceAddress = "";
  Vector3 _lastAppliedTrimmedOffsetLocalUnits;
  bool _hasAppliedTrimmedOffset;
  Vector3 _trimmedOffsetBaseLocalPosition;
  bool _hasTrimmedOffsetBaseLocalPosition;
  [SerializeField, HideInInspector] Vector3 serializedAppliedTrimmedOffsetLocalUnits;
  [SerializeField, HideInInspector] bool serializedHasAppliedTrimmedOffset;
  [SerializeField, HideInInspector] Vector3 serializedTrimmedOffsetBaseLocalPosition;
  [SerializeField, HideInInspector] bool serializedHasTrimmedOffsetBaseLocalPosition;
  int _adaptiveCooldownMultiplier = 1;
  int _adaptiveStaleWindowStartFrame = -1;
  int _adaptiveStaleCountInWindow;
  int _adaptiveCooldownLastDecayFrame = -1;
  readonly HashSet<string> _editorPreviewNormalMissWarnings = new(StringComparer.OrdinalIgnoreCase);

  TextureResidencyCache.Lease _pendingColorLease;
  TextureResidencyCache.Lease _pendingNormalLease;
  TextureResidencyCache.Lease _activeColorLease;
  TextureResidencyCache.Lease _activeNormalLease;
  SpriteAddressPair _pendingLoadPair;
  float _pendingSupplementWaitStartedAt = -1f;
  int _pendingLoadRequestVersion;
  readonly Dictionary<PairLookupCacheKey, SpriteAddressPair> _pairLookupHitCache = new();
  readonly HashSet<PairLookupCacheKey> _pairLookupMissCache = new();
  readonly Dictionary<string, Sprite> _localLoadedSpriteByAddress = new(StringComparer.OrdinalIgnoreCase);
  readonly Dictionary<PaddedSpriteCacheKey, Sprite> _generatedPaddedSprites = new();
  readonly List<Sprite> _generatedPaddedSpriteAssets = new();
  readonly List<Texture2D> _generatedPaddedTextures = new();
  readonly HashSet<PaddedSpriteCacheKey> _paddingCreationWarnings = new();
  static int cachedUiLayer = int.MinValue;
  static bool uiSortingLayerCacheInitialized;
  static int cachedMyUiSortingLayerId = int.MinValue;
  static int cachedMyUi2SortingLayerId = int.MinValue;

  void Awake() {
    _renderer = GetComponent<SpriteRenderer>();
    _mpb = new MaterialPropertyBlock();
    SyncSerializedTrimmedOffsetState();
    SyncRendererVisibility();
  }

  void OnEnable() {
    SyncSerializedTrimmedOffsetState();
    RefreshTrimmedOffsetForCurrentSprite();
    if (!Application.isPlaying) {
      QueueAutoRefreshOnEnable();
      return;
    }
    _nextInternalRetryFrame = 0;
    _pendingRetargetAllowedFrame = 0;
    _deferredOverwriteAllowedFrame = 0;
    _lastAppliedNormalTextureId = ulong.MaxValue;
    _adaptiveCooldownMultiplier = 1;
    _adaptiveStaleWindowStartFrame = -1;
    _adaptiveStaleCountInWindow = 0;
    _adaptiveCooldownLastDecayFrame = -1;
    SpriteUiPinService.Register(this);
    QueueAutoRefreshOnEnable();
  }

  void Update() {
    if (!Application.isPlaying || !enabled || !gameObject.activeInHierarchy) return;
    FlushQueuedRuntimeEnableRefreshes();
    TrimmedSpriteOffsetResolver.PumpDeferredRuntimeLoads();
    if (TryAdvancePendingLoadRequest()) return;
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
    DiscardQueuedRuntimeRefresh(this);
    if (Application.isPlaying) {
      SpriteUiPinService.Unregister(this);
    }
    ResetTransientResolveRetryState();
    ResetAppliedTrimmedOffsetState(clearSliceAddress: false);
    ClearGeneratedPaddedSprites();
    if (!Application.isPlaying) return;
    CancelPendingRequest();
    ReleaseActiveLeases();
    ResetPairLookupCaches();
    _localLoadedSpriteByAddress.Clear();
    _sliceMismatchWarnings.Clear();
    _lastAppliedNormalTextureId = ulong.MaxValue;
  }

  void OnDestroy() {
    DiscardQueuedRuntimeRefresh(this);
    if (Application.isPlaying) {
      SpriteUiPinService.Unregister(this);
    }
    ResetTransientResolveRetryState();
    ResetAppliedTrimmedOffsetState();
    CancelPendingRequest();
    ReleaseActiveLeases();
    ResetPairLookupCaches();
    _localLoadedSpriteByAddress.Clear();
    ClearGeneratedPaddedSprites();
    _sliceMismatchWarnings.Clear();
    _lastAppliedNormalTextureId = ulong.MaxValue;
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

  public void SetShaderMarginPixels(float x, float y) {
    var nextX = Mathf.Max(0f, x);
    var nextY = Mathf.Max(0f, y);
    if (Mathf.Approximately(shaderMarginPixelsX, nextX) &&
        Mathf.Approximately(shaderMarginPixelsY, nextY)) return;

    shaderMarginPixelsX = nextX;
    shaderMarginPixelsY = nextY;
    RefreshShaderMarginPadding();
  }

  public void RefreshShaderMarginPadding() {
    ClearGeneratedPaddedSprites();
    ForceUpdateSpriteAndNormal(isAnimation ? Mathf.Max(_lastRequestedFrame, 1) : 0);
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

  int ResolveRefreshFrame() {
    return isAnimation ? Mathf.Max(_lastRequestedFrame, 1) : 0;
  }

  bool HasAutoRefreshInputs() {
    return !string.IsNullOrWhiteSpace(libraryName) && !string.IsNullOrWhiteSpace(category);
  }

  void QueueAutoRefreshOnEnable() {
    if (!HasAutoRefreshInputs()) return;
#if UNITY_EDITOR
    if (!Application.isPlaying) {
      QueueEditorPreviewRefresh(this);
      return;
    }
#endif
    if (!enabled || !gameObject.activeInHierarchy) return;
    QueueRuntimeRefreshOnEnable(this);
  }

  static void QueueRuntimeRefreshOnEnable(SpriteWithNormals target) {
    if (target == null) return;
    if (!pendingRuntimeEnableRefreshSet.Add(target)) return;
    pendingRuntimeEnableRefreshQueue.Enqueue(target);
  }

  static void DiscardQueuedRuntimeRefresh(SpriteWithNormals target) {
    if (target == null) return;
    pendingRuntimeEnableRefreshSet.Remove(target);
  }

  static void FlushQueuedRuntimeEnableRefreshes() {
    if (!Application.isPlaying) {
      pendingRuntimeEnableRefreshQueue.Clear();
      pendingRuntimeEnableRefreshSet.Clear();
      pendingRuntimeEnableRefreshFrame = -1;
      return;
    }

    var frame = Time.frameCount;
    if (pendingRuntimeEnableRefreshFrame == frame) return;
    pendingRuntimeEnableRefreshFrame = frame;

    var budget = IsOverlayWarmGateActive()
      ? OverlayEnableRefreshBudgetPerFrame
      : GameplayEnableRefreshBudgetPerFrame;
    budget = Mathf.Max(budget, 1);

    var processed = 0;
    var remaining = pendingRuntimeEnableRefreshQueue.Count;
    while (processed < budget && remaining > 0 && pendingRuntimeEnableRefreshQueue.Count > 0) {
      remaining--;
      var target = pendingRuntimeEnableRefreshQueue.Dequeue();
      if (target == null) continue;
      pendingRuntimeEnableRefreshSet.Remove(target);
      if (!target.enabled || !target.gameObject.activeInHierarchy) continue;
      if (!target.HasAutoRefreshInputs()) continue;
      if (target.ShouldDeferRuntimeRefreshForOverlay()) {
        QueueRuntimeRefreshOnEnable(target);
        continue;
      }
      target.ForceUpdateSpriteAndNormal(target.ResolveRefreshFrame());
      processed++;
    }
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
      pair = StripUnavailableRuntimeNormalAddress(pair, lookupKey);
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
    pair = StripUnavailableRuntimeNormalAddress(pair, lookupKey);
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
    pair = StripUnavailableRuntimeNormalAddress(pair, lookupKey);
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
      TrackTrimmedMetadataWarmupCandidate(staticPair.RuntimeColorAddress);
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
      pair = StripUnavailableRuntimeNormalAddress(pair, lookupKey);
      TrackTrimmedMetadataWarmupCandidate(pair.RuntimeColorAddress);
      AddUniqueAddress(outAddresses, pair.RuntimeColorAddress, seenAddresses, maxAddresses);
      AddUniqueAddress(outAddresses, pair.RuntimeNormalAddress, seenAddresses, maxAddresses);
    }
  }

  public bool IsUiTarget() {
    if (GetComponentInParent<Canvas>(true) != null) return true;
    if (IsUnderUiLayer()) return true;
    return HasUiSortingLayerInHierarchy();
  }

  bool IsUnderUiLayer() {
    var uiLayer = ResolveUiLayer();
    if (uiLayer < 0) return false;

    for (var current = transform; current != null; current = current.parent) {
      if (current.gameObject.layer == uiLayer) {
        return true;
      }
    }

    return false;
  }

  bool HasUiSortingLayerInHierarchy() {
    CacheUiSortingLayerIds();
    if (_renderer == null) {
      _renderer = GetComponent<SpriteRenderer>();
    }

    if (IsUiSortingLayerId(ResolveSortingLayerId(_renderer))) {
      return true;
    }

    for (var current = transform.parent; current != null; current = current.parent) {
      if (!current.TryGetComponent<SpriteRenderer>(out var parentRenderer)) continue;
      if (IsUiSortingLayerId(ResolveSortingLayerId(parentRenderer))) {
        return true;
      }
    }

    return false;
  }

  static int ResolveUiLayer() {
    if (cachedUiLayer != int.MinValue) return cachedUiLayer;
    cachedUiLayer = LayerMask.NameToLayer("UI");
    return cachedUiLayer;
  }

  static void CacheUiSortingLayerIds() {
    if (uiSortingLayerCacheInitialized) return;

    cachedMyUiSortingLayerId = int.MinValue;
    cachedMyUi2SortingLayerId = int.MinValue;

    var sortingLayers = SortingLayer.layers;
    for (var i = 0; i < sortingLayers.Length; i++) {
      var sortingLayer = sortingLayers[i];
      if (string.Equals(sortingLayer.name, "MyUI", StringComparison.Ordinal)) {
        cachedMyUiSortingLayerId = sortingLayer.id;
        continue;
      }

      if (string.Equals(sortingLayer.name, "MyUI2", StringComparison.Ordinal)) {
        cachedMyUi2SortingLayerId = sortingLayer.id;
      }
    }

    uiSortingLayerCacheInitialized = true;
  }

  static bool IsUiSortingLayerId(int sortingLayerId) {
    return sortingLayerId == cachedMyUiSortingLayerId || sortingLayerId == cachedMyUi2SortingLayerId;
  }

  static int ResolveSortingLayerId(SpriteRenderer renderer) {
    return renderer != null ? renderer.sortingLayerID : int.MinValue;
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
        string.Equals(lookupLabelPrefix, _lastLookupLabelPrefix, StringComparison.Ordinal) &&
        string.Equals(lookupCategory, _lastLookupCategory, StringComparison.Ordinal)) {
      LogSpriteFetch("skip_same_lookup");
      return;
    }

    var lookupKey = new SpriteLookupKey(lookupLibraryName, lookupLabelPrefix, lookupCategory, lookupFrame);
    if (TryApplyBlankSelectionEmptyFallback(lookupKey)) return;
    if (TryApplyNoPrefixStaticEmptyFallback(lookupKey)) return;
    if (!TryResolvePairCached(lookupKey, out var pair, out var pending)) {
      if (pending) {
        LogSpriteFetch("resolve_pending", "key='" + lookupKey + "'");
        return;
      }
      if (TryApplyExplicitEmptyLabelFallback(lookupKey)) return;
      if (TryScheduleTransientResolveRetry(lookupKey)) {
        LogSpriteFetch("resolve_retry_scheduled", "key='" + lookupKey + "' attempt=" + _transientResolveRetryCount);
        return;
      }
      LogSpriteFetch("resolve_failed", "key='" + lookupKey + "'");
      ReportResolveError(lookupKey);
      return;
    }
    pair = StripUnavailableRuntimeNormalAddress(pair, lookupKey);
    LogSpriteFetch(
      "resolve_success",
      "key='" + lookupKey + "' color='" + (pair.colorAddress ?? "") + "' normal='" + (pair.normalAddress ?? "") + "'"
    );

    ResetTransientResolveRetryState();
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
    if (ShouldDeferRuntimeLoadForOverlay()) {
      _deferredRequest = pair;
      _hasDeferredRequest = true;
      return;
    }
    if (HasPendingLoadRequest()) {
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
    var hasPendingState = HasPendingLoadRequest() || _hasDeferredRequest;
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
        BeginPendingLoadRequest(requestVersion, pair);
        return;
      }
      LogSpriteFetch("load_color_immediate_complete", "request_version=" + requestVersion);
      CompleteLoadedSprites(requestVersion, colorLease, normalLease, pair);
      return;
    }

    _pendingRetargetAllowedFrame = Time.frameCount + ResolveOutstandingRetargetCooldownFrames();
    LogSpriteFetch("load_wait_async", "request_version=" + requestVersion);
    BeginPendingLoadRequest(requestVersion, pair);
  }

  TextureResidencyCache.LoadPriority ResolveActiveFrameLoadPriority() {
    if (!Application.isPlaying) return TextureResidencyCache.LoadPriority.Warmup;
    if (_isInternalTickRequest) return TextureResidencyCache.LoadPriority.Warmup;
    if (IsOverlayWarmGateActive()) return TextureResidencyCache.LoadPriority.Warmup;
    return TextureResidencyCache.LoadPriority.Immediate;
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
        (HasPendingLoadRequest() || _hasDeferredRequest)) {
      CancelPendingRequest();
    }

    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    if (_renderer != null) ApplySprites(colorSprite, normalSprite, pair.colorAddress);

    ReleaseActiveLeases();
    return true;
  }

  void CompleteLoadedSprites(
    int requestVersion,
    TextureResidencyCache.Lease colorLease,
    TextureResidencyCache.Lease normalLease,
    SpriteAddressPair pair,
    bool allowBlockingEditorSliceFallback = false
  ) {
    if (requestVersion != _requestVersion) {
      LogSpriteFetch("complete_version_mismatch", "incoming=" + requestVersion + " current=" + _requestVersion);
      ReleaseLease(ref colorLease);
      ReleaseLease(ref normalLease);
      return;
    }

    var staleCompletion =
      !AddressEquals(pair.RuntimeColorAddress, _targetColorAddress) ||
      !AddressEquals(pair.RuntimeNormalAddress, _targetNormalAddress);
    if (staleCompletion) {
      ClearPendingState();
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
    var allowBlockingEditorSliceFallbackNow = allowBlockingEditorSliceFallback;
#if UNITY_EDITOR
    if (!allowBlockingEditorSliceFallbackNow &&
        colorLease != null &&
        colorLease == _pendingColorLease &&
        _pendingSupplementWaitStartedAt >= 0f) {
      allowBlockingEditorSliceFallbackNow =
        (Time.realtimeSinceStartup - _pendingSupplementWaitStartedAt) >= ResolveEditorSpriteMapSupplementWaitTimeoutSeconds();
    }
#endif
    var colorSprite = ResolveLeaseSprite(colorLease, colorSliceAddress);
#if UNITY_EDITOR
    colorSprite ??= TryResolveEditorSliceFallback(colorSliceAddress, "color", allowBlockingEditorSliceFallbackNow);
#endif
    if (colorSprite == null) {
#if UNITY_EDITOR
      if (TryKeepPendingSliceResolve(colorLease, colorSliceAddress, allowBlockingEditorSliceFallbackNow)) {
        return;
      }
#endif
      ClearPendingState();
      ClearRenderedSprites();
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
#if UNITY_EDITOR
    normalSprite ??= TryResolveEditorSliceFallback(normalSliceAddress, "normal", allowBlockingEditorSliceFallbackNow);
#endif
    normalSprite = ResolveExpectedSliceSprite(normalSprite, normalSliceAddress, "normal");
    if (normalSprite != null) CacheLocalLoadedSprite(normalSliceAddress, normalSprite);
    if (normalLease != null && normalLease.IsDone && normalSprite == null && !string.IsNullOrWhiteSpace(normalSliceAddress))
      Debug.LogError($"[SpriteWithNormals] Failed to resolve normal sprite '{normalSliceAddress}' on {gameObject.name}");
    LogSpriteFetch(
      "complete_apply",
      "color='" + (colorSprite != null ? colorSprite.name : "") + "' normal='" + (normalSprite != null ? normalSprite.name : "") + "'"
    );

    ClearPendingState();
    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    if (_renderer != null) ApplySprites(colorSprite, normalSprite, colorSliceAddress);
    RecordAdaptiveStableCompletion();

    ReleaseActiveLeases();
    _activeColorLease = colorLease;
    _activeNormalLease = normalLease;
    TryStartDeferredRequest();
  }

#if UNITY_EDITOR
  bool TryKeepPendingSliceResolve(
    TextureResidencyCache.Lease colorLease,
    string colorSliceAddress,
    bool allowBlockingEditorSliceFallback
  ) {
    if (!Application.isEditor || !Application.isPlaying) return false;
    if (colorLease == null || colorLease != _pendingColorLease) return false;
    if (!SpriteSliceAddressUtility.TryParseSliceAddress(colorSliceAddress, out _, out _)) return false;

    var startedAt = _pendingSupplementWaitStartedAt;
    if (startedAt < 0f) {
      startedAt = Time.realtimeSinceStartup;
      _pendingSupplementWaitStartedAt = startedAt;
    }

    var elapsed = Time.realtimeSinceStartup - startedAt;
    if (elapsed > (ResolveEditorSpriteMapSupplementWaitTimeoutSeconds() + EditorSliceResolveRetryGraceSeconds)) {
      return false;
    }

    if (allowBlockingEditorSliceFallback) {
      TryForceImportPendingSliceAsset(colorSliceAddress);
    }

    var warningKey = "pending_slice_retry|" + colorSliceAddress;
    if (_sliceMismatchWarnings.Add(warningKey)) {
      Debug.LogWarning(
        "[SpriteWithNormals] Delaying unresolved slice resolve on " + gameObject.name +
        " address='" + colorSliceAddress + "'" +
        " elapsed_ms=" + Mathf.RoundToInt(elapsed * 1000f) +
        " allow_editor_fallback=" + (allowBlockingEditorSliceFallback ? 1 : 0)
      );
    }

    return true;
  }
#endif

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
    if (HasPendingLoadRequest() || _hasDeferredRequest) return;
    if (ShouldThrottleWarmupRequests()) return;

    for (var offset = 1; offset <= PrefetchAheadFrames; offset++) {
      var warmupKey = new SpriteLookupKey(libraryName, labelPrefix, category, lookupFrame + offset);
      if (!TryResolvePairCached(warmupKey, out var warmupPair, out var pending)) {
        if (pending) return;
        break;
      }
      warmupPair = StripUnavailableRuntimeNormalAddress(warmupPair, warmupKey);
      if (!AddressEquals(warmupPair.RuntimeColorAddress, currentPair.RuntimeColorAddress) || !AddressEquals(warmupPair.RuntimeNormalAddress, currentPair.RuntimeNormalAddress)) {
        WarmupAddress(warmupPair.RuntimeColorAddress, TextureResidencyCache.LoadPriority.Warmup);
        WarmupAddress(warmupPair.RuntimeNormalAddress, TextureResidencyCache.LoadPriority.Warmup);
      }
    }
  }

  static Sprite ResolveLeaseSprite(TextureResidencyCache.Lease lease, string sliceOrAtlasAddress) {
    if (lease == null || !lease.IsDone || !lease.IsSuccess) return null;
    if (ShouldAvoidBlockingEditorSpriteFallback()) {
      if (lease.TryGetSpriteByAddressWithoutEditorSupplement(sliceOrAtlasAddress, out var deferredSprite)) return deferredSprite;
    }
    else if (lease.TryGetSpriteByAddress(sliceOrAtlasAddress, out var sprite)) {
      return sprite;
    }
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
    return lease.NeedsPendingSpriteMapSupplement(sliceOrAtlasAddress);
  }

  static float ResolveEditorSpriteMapSupplementWaitTimeoutSeconds() {
    return IsOverlayWarmGateActive()
      ? EditorSpriteMapSupplementWaitTimeoutDuringOverlaySeconds
      : EditorSpriteMapSupplementWaitTimeoutSeconds;
  }

  static bool ShouldAvoidBlockingEditorSpriteFallback() {
#if UNITY_EDITOR
    return Application.isEditor &&
           (StreamingWarmOrchestrator.IsWarmGateRunning || SpriteStreamingLoadingState.IsProtectedLoadingOverlayActive);
#else
    return false;
#endif
  }

  void ApplySprites(Sprite colorSprite, Sprite normalSprite, string colorSliceAddress) {
    var renderedColorSprite = ResolveRenderedSprite(colorSprite, useNormalFill: false);
    if (_renderer.sprite != renderedColorSprite) {
      _renderer.sprite = renderedColorSprite;
    }
    ApplyConfiguredTrimmedOffset(colorSprite, colorSliceAddress);
    var renderedNormalSprite = ResolveRenderedSprite(normalSprite, useNormalFill: true);
    var normalTexture = renderedNormalSprite != null ? renderedNormalSprite.texture : GetFallbackNormalTexture();
    var normalTextureId = ObjectEntityId.GetRawValue(normalTexture);
    if (normalTextureId == _lastAppliedNormalTextureId) return;

    _mpb ??= new MaterialPropertyBlock();
    _renderer.GetPropertyBlock(_mpb);
    if (normalTexture != null) _mpb.SetTexture(NormalMapPropertyId, normalTexture);
    _renderer.SetPropertyBlock(_mpb);
    _lastAppliedNormalTextureId = normalTextureId;
  }

  void ClearRenderedSprites() {
    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    if (_renderer != null) ApplySprites(null, null, "");
  }

  Sprite ResolveRenderedSprite(Sprite sourceSprite, bool useNormalFill) {
    if (sourceSprite == null) return null;
    if (!TryGetShaderMarginPixels(out var marginX, out var marginY)) return sourceSprite;

    var key = new PaddedSpriteCacheKey(ObjectEntityId.GetRawValue(sourceSprite), marginX, marginY, useNormalFill);
    if (_generatedPaddedSprites.TryGetValue(key, out var cachedSprite) && cachedSprite != null) {
      return cachedSprite;
    }

    if (!TryCreatePaddedSprite(sourceSprite, marginX, marginY, useNormalFill, out var paddedSprite)) {
      return sourceSprite;
    }

    CacheGeneratedPaddedSprite(key, paddedSprite);
    return paddedSprite;
  }

  bool TryGetShaderMarginPixels(out int marginX, out int marginY) {
    marginX = Mathf.Max(0, Mathf.CeilToInt(shaderMarginPixelsX));
    marginY = Mathf.Max(0, Mathf.CeilToInt(shaderMarginPixelsY));
    return marginX > 0 || marginY > 0;
  }

  bool TryCreatePaddedSprite(Sprite sourceSprite, int marginX, int marginY, bool useNormalFill, out Sprite paddedSprite) {
    paddedSprite = null;
    if (sourceSprite == null || sourceSprite.texture == null) return false;

    var textureRect = sourceSprite.textureRect;
    var sourceX = Mathf.RoundToInt(textureRect.x);
    var sourceY = Mathf.RoundToInt(textureRect.y);
    var sourceWidth = Mathf.RoundToInt(textureRect.width);
    var sourceHeight = Mathf.RoundToInt(textureRect.height);
    if (sourceWidth <= 0 || sourceHeight <= 0) return false;

    var paddedWidth = sourceWidth + (marginX * 2);
    var paddedHeight = sourceHeight + (marginY * 2);
    if (paddedWidth <= 0 || paddedHeight <= 0) return false;

    Color[] sourcePixels;
    try {
      sourcePixels = sourceSprite.texture.GetPixels(sourceX, sourceY, sourceWidth, sourceHeight);
    }
    catch (Exception ex) {
      WarnPaddingCreationFailureOnce(sourceSprite, marginX, marginY, useNormalFill, ex);
      return false;
    }

    var paddedTexture = new Texture2D(paddedWidth, paddedHeight, TextureFormat.RGBA32, false, useNormalFill) {
      filterMode = sourceSprite.texture.filterMode,
      wrapMode = TextureWrapMode.Clamp,
      name = sourceSprite.name + "__paddingTexture"
    };
    paddedTexture.hideFlags = HideFlags.HideAndDontSave;

    var fillColor = useNormalFill ? FlatNormalPaddingColor : TransparentPaddingColor;
    var fillPixels = BuildFillPixels(paddedWidth, paddedHeight, fillColor);
    paddedTexture.SetPixels32(fillPixels);
    paddedTexture.SetPixels(marginX, marginY, sourceWidth, sourceHeight, sourcePixels);
    paddedTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

    var paddedPivot = new Vector2(sourceSprite.pivot.x + marginX, sourceSprite.pivot.y + marginY);
    var paddedBorder = sourceSprite.border + new Vector4(marginX, marginY, marginX, marginY);
    paddedSprite = Sprite.Create(
      paddedTexture,
      new Rect(0f, 0f, paddedWidth, paddedHeight),
      new Vector2(
        paddedPivot.x / paddedWidth,
        paddedPivot.y / paddedHeight),
      sourceSprite.pixelsPerUnit,
      0u,
      SpriteMeshType.FullRect,
      paddedBorder
    );

    if (paddedSprite == null) {
      DestroyGeneratedObject(paddedTexture);
      return false;
    }

    paddedSprite.name = sourceSprite.name + "__paddingSprite";
    paddedSprite.hideFlags = HideFlags.HideAndDontSave;
    _generatedPaddedTextures.Add(paddedTexture);
    _generatedPaddedSpriteAssets.Add(paddedSprite);
    return true;
  }

  static Color32[] BuildFillPixels(int width, int height, Color32 fillColor) {
    var pixelCount = Mathf.Max(0, width * height);
    var pixels = new Color32[pixelCount];
    for (var i = 0; i < pixelCount; i++) pixels[i] = fillColor;
    return pixels;
  }

  void CacheGeneratedPaddedSprite(PaddedSpriteCacheKey key, Sprite paddedSprite) {
    if (_generatedPaddedSprites.Count >= MaxGeneratedPaddedSpriteCacheEntries &&
        !_generatedPaddedSprites.ContainsKey(key)) {
      ClearGeneratedPaddedSprites();
    }

    _generatedPaddedSprites[key] = paddedSprite;
  }

  void ClearGeneratedPaddedSprites() {
    _generatedPaddedSprites.Clear();
    _paddingCreationWarnings.Clear();

    for (var i = 0; i < _generatedPaddedSpriteAssets.Count; i++) {
      var sprite = _generatedPaddedSpriteAssets[i];
      DestroyGeneratedObject(sprite);
    }
    _generatedPaddedSpriteAssets.Clear();

    for (var i = 0; i < _generatedPaddedTextures.Count; i++) {
      var texture = _generatedPaddedTextures[i];
      DestroyGeneratedObject(texture);
    }
    _generatedPaddedTextures.Clear();
  }

  static void DestroyGeneratedObject(UnityEngine.Object obj) {
    if (obj == null) return;
    if (Application.isPlaying) {
      Destroy(obj);
      return;
    }
    DestroyImmediate(obj);
  }

  void WarnPaddingCreationFailureOnce(Sprite sourceSprite, int marginX, int marginY, bool useNormalFill, Exception ex) {
    var key = new PaddedSpriteCacheKey(ObjectEntityId.GetRawValue(sourceSprite), marginX, marginY, useNormalFill);
    if (!_paddingCreationWarnings.Add(key)) return;

    Debug.LogWarning(
      "[SpriteWithNormals] Failed to create shader padding for '" + name +
      "' sprite='" + sourceSprite.name +
      "' margin_x=" + marginX +
      " margin_y=" + marginY +
      " normal_fill=" + (useNormalFill ? 1 : 0) +
      " reason='" + ex.Message + "'"
    );
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
    var appliedPositionOffsetLocalUnits = ScaleTrimmedOffsetForTransform(offsetLocalUnits);
    var baseLocalPosition = ResolveTrimmedOffsetBaseLocalPosition(offsetLocalUnits, appliedPositionOffsetLocalUnits);
    transform.localPosition = baseLocalPosition + appliedPositionOffsetLocalUnits;
    _lastAppliedTrimmedOffsetLocalUnits = appliedPositionOffsetLocalUnits;
    _hasAppliedTrimmedOffset = true;
    PersistTrimmedOffsetState();
    LogSpriteApply(
      "offset_applied",
      colorSprite,
      null,
      "address='" + (colorSliceAddress ?? "") + "'" +
      " source_offset=(" + offsetLocalUnits.x.ToString("0.###") + "," + offsetLocalUnits.y.ToString("0.###") + ")" +
      " applied_offset=(" + appliedPositionOffsetLocalUnits.x.ToString("0.###") + "," + appliedPositionOffsetLocalUnits.y.ToString("0.###") + ")"
    );
  }

  void ClearAppliedTrimmedOffset() {
    if (_hasTrimmedOffsetBaseLocalPosition) {
      transform.localPosition = _trimmedOffsetBaseLocalPosition;
    } else if (_hasAppliedTrimmedOffset) {
      transform.localPosition -= _lastAppliedTrimmedOffsetLocalUnits;
    }
    _lastAppliedTrimmedOffsetLocalUnits = Vector3.zero;
    _hasAppliedTrimmedOffset = false;
    PersistTrimmedOffsetState();
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

  void SyncSerializedTrimmedOffsetState() {
    NormalizeSerializedTrimmedOffsetState();
    _lastAppliedTrimmedOffsetLocalUnits = serializedAppliedTrimmedOffsetLocalUnits;
    _hasAppliedTrimmedOffset = serializedHasAppliedTrimmedOffset;
    _trimmedOffsetBaseLocalPosition = serializedTrimmedOffsetBaseLocalPosition;
    _hasTrimmedOffsetBaseLocalPosition = serializedHasTrimmedOffsetBaseLocalPosition;
  }

  void NormalizeSerializedTrimmedOffsetState() {
    var hadAppliedState = serializedHasAppliedTrimmedOffset;
    var hadBaseState = serializedHasTrimmedOffsetBaseLocalPosition;
    if (!hadAppliedState && !hadBaseState) return;

    var currentLocalPosition = transform.localPosition;
    var serializedAppliedOffset = serializedAppliedTrimmedOffsetLocalUnits;
    var serializedBasePosition = hadBaseState
      ? serializedTrimmedOffsetBaseLocalPosition
      : currentLocalPosition - serializedAppliedOffset;
    var expectedAppliedPosition = serializedBasePosition + serializedAppliedOffset;
    var normalizedBasePosition = LooksLikeDuplicatedTrimmedOffsetBase(serializedBasePosition, serializedAppliedOffset)
      ? Vector3.zero
      : serializedBasePosition;

    var restoredBasePosition = false;
    if (hadAppliedState && ApproximatelyVector3(currentLocalPosition, expectedAppliedPosition)) {
      if (!ApproximatelyVector3(currentLocalPosition, normalizedBasePosition)) {
        transform.localPosition = normalizedBasePosition;
        restoredBasePosition = true;
      }
    }

    if (restoredBasePosition) {
      Debug.Log(
        "[SpriteWithNormals] Normalized persisted trimmed offset state for '" + name +
        "'. current=(" + currentLocalPosition.x.ToString("0.###") + "," + currentLocalPosition.y.ToString("0.###") +
        ") base=(" + serializedBasePosition.x.ToString("0.###") + "," + serializedBasePosition.y.ToString("0.###") +
        ") normalized_base=(" + normalizedBasePosition.x.ToString("0.###") + "," + normalizedBasePosition.y.ToString("0.###") +
        ") applied=(" + serializedAppliedOffset.x.ToString("0.###") + "," + serializedAppliedOffset.y.ToString("0.###") + ")");
    }

    serializedAppliedTrimmedOffsetLocalUnits = Vector3.zero;
    serializedHasAppliedTrimmedOffset = false;
    serializedTrimmedOffsetBaseLocalPosition = Vector3.zero;
    serializedHasTrimmedOffsetBaseLocalPosition = false;

#if UNITY_EDITOR
    if (!Application.isPlaying && restoredBasePosition) {
      UnityEditor.EditorUtility.SetDirty(transform);
      UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
  }

  static bool LooksLikeDuplicatedTrimmedOffsetBase(Vector3 serializedBasePosition, Vector3 serializedAppliedOffset) {
    return ComponentsLookDuplicated(serializedBasePosition.x, serializedAppliedOffset.x) &&
           ComponentsLookDuplicated(serializedBasePosition.y, serializedAppliedOffset.y) &&
           ComponentsLookDuplicated(serializedBasePosition.z, serializedAppliedOffset.z);
  }

  static bool ComponentsLookDuplicated(float baseValue, float appliedValue) {
    var maxAbs = Mathf.Max(Mathf.Abs(baseValue), Mathf.Abs(appliedValue));
    if (maxAbs <= 0.0005f) return true;
    if (Mathf.Sign(baseValue) != Mathf.Sign(appliedValue)) return false;
    return Mathf.Abs(baseValue - appliedValue) <= Mathf.Max(0.1f, maxAbs * 0.1f);
  }

  void PersistTrimmedOffsetState() {
    if (!ShouldPersistTrimmedOffsetState()) {
      if (serializedHasAppliedTrimmedOffset || serializedHasTrimmedOffsetBaseLocalPosition ||
          serializedAppliedTrimmedOffsetLocalUnits != Vector3.zero ||
          serializedTrimmedOffsetBaseLocalPosition != Vector3.zero) {
        serializedAppliedTrimmedOffsetLocalUnits = Vector3.zero;
        serializedHasAppliedTrimmedOffset = false;
        serializedTrimmedOffsetBaseLocalPosition = Vector3.zero;
        serializedHasTrimmedOffsetBaseLocalPosition = false;
      }
      return;
    }

    serializedAppliedTrimmedOffsetLocalUnits = _lastAppliedTrimmedOffsetLocalUnits;
    serializedHasAppliedTrimmedOffset = _hasAppliedTrimmedOffset;
    serializedTrimmedOffsetBaseLocalPosition = _trimmedOffsetBaseLocalPosition;
    serializedHasTrimmedOffsetBaseLocalPosition = _hasTrimmedOffsetBaseLocalPosition;
  }

  static bool ShouldPersistTrimmedOffsetState() {
    return !Application.isPlaying;
  }

  Vector3 ResolveTrimmedOffsetBaseLocalPosition(Vector3 sourceOffsetLocalUnits, Vector3 appliedOffsetLocalUnits) {
    if (_hasTrimmedOffsetBaseLocalPosition) {
      return _trimmedOffsetBaseLocalPosition;
    }

    var currentLocalPosition = transform.localPosition;
    var baseLocalPosition = InferTrimmedOffsetBaseLocalPosition(currentLocalPosition, sourceOffsetLocalUnits, appliedOffsetLocalUnits);
    _trimmedOffsetBaseLocalPosition = baseLocalPosition;
    _hasTrimmedOffsetBaseLocalPosition = true;
    PersistTrimmedOffsetState();
    return baseLocalPosition;
  }

  Vector3 InferTrimmedOffsetBaseLocalPosition(Vector3 currentLocalPosition, Vector3 sourceOffsetLocalUnits, Vector3 appliedOffsetLocalUnits) {
    if (ApproximatelyVector3(currentLocalPosition, Vector3.zero)) {
      return Vector3.zero;
    }

    if (ApproximatelyVector3(currentLocalPosition, sourceOffsetLocalUnits) ||
        ApproximatelyVector3(currentLocalPosition, sourceOffsetLocalUnits * 2f) ||
        ApproximatelyVector3(currentLocalPosition, appliedOffsetLocalUnits) ||
        ApproximatelyVector3(currentLocalPosition, appliedOffsetLocalUnits * 2f)) {
      Debug.Log(
        "[SpriteWithNormals] Normalized edit-mode trimmed offset baseline for '" + name +
        "'. current=(" + currentLocalPosition.x.ToString("0.###") + "," + currentLocalPosition.y.ToString("0.###") +
        ") source_offset=(" + sourceOffsetLocalUnits.x.ToString("0.###") + "," + sourceOffsetLocalUnits.y.ToString("0.###") +
        ") applied_offset=(" + appliedOffsetLocalUnits.x.ToString("0.###") + "," + appliedOffsetLocalUnits.y.ToString("0.###") + ")");
      return Vector3.zero;
    }

    return _hasAppliedTrimmedOffset
      ? currentLocalPosition - _lastAppliedTrimmedOffsetLocalUnits
      : currentLocalPosition;
  }

  static bool ApproximatelyVector3(Vector3 left, Vector3 right, float epsilon = 0.0015f) {
    return Mathf.Abs(left.x - right.x) <= epsilon &&
           Mathf.Abs(left.y - right.y) <= epsilon &&
           Mathf.Abs(left.z - right.z) <= epsilon;
  }

  Vector3 ScaleTrimmedOffsetForTransform(Vector3 offsetLocalUnits) {
    var localScale = transform.localScale;
    var scaleX = Mathf.Abs(localScale.x);
    var scaleY = Mathf.Abs(localScale.y);
    if (scaleX <= 0f) scaleX = 1f;
    if (scaleY <= 0f) scaleY = 1f;
    return new Vector3(offsetLocalUnits.x * scaleX, offsetLocalUnits.y * scaleY, offsetLocalUnits.z);
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
    if (HasPendingLoadRequest() || _hasDeferredRequest) {
      LogSpriteFetch(
        "cancel_pending",
        "pending_color='" + (_pendingColorAddress ?? "") + "' pending_normal='" + (_pendingNormalAddress ?? "") + "'" +
        " has_deferred=" + (_hasDeferredRequest ? 1 : 0)
      );
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
      LogSpriteFetch("load_editor_supplement_wait_timeout", "request_version=" + _pendingLoadRequestVersion);
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
    LogSpriteFetch(
      logStage,
      "key='" + lookupKey + "' color='" + (fallbackPair.colorAddress ?? "") + "'"
    );

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
        string.Equals(_lastResolveErrorLabelPrefix, lookupKey.labelPrefix, StringComparison.Ordinal) &&
        string.Equals(_lastResolveErrorCategory, lookupKey.category, StringComparison.Ordinal)) return;

    _hasLastResolveError = true;
    _lastResolveErrorLibraryName = lookupKey.libraryName ?? "";
    _lastResolveErrorLabelPrefix = lookupKey.labelPrefix ?? "";
    _lastResolveErrorCategory = lookupKey.category ?? "";
    Debug.LogError($"[SpriteWithNormals] No sprite mapping found for {lookupKey} on {gameObject.name}");
  }

  bool TryResolvePair(SpriteLookupKey key, out SpriteAddressPair pair) => SpriteAddressResolver.TryResolve(key, out pair, gameObject);
  bool IsResolvePending(SpriteLookupKey key) => SpriteAddressResolver.IsLookupPending(key, gameObject);

  Sprite ResolveExpectedSliceSprite(Sprite loadedSprite, string sliceAddress, string channel) {
    if (loadedSprite == null) return null;
    if (!SpriteSliceAddressUtility.TryParseSliceAddress(sliceAddress, out _, out var expectedSpriteName)) return loadedSprite;
    if (string.Equals(loadedSprite.name, expectedSpriteName, StringComparison.Ordinal)) return loadedSprite;
    if (SpriteSliceAddressUtility.HasEquivalentNumericLabel(loadedSprite.name, expectedSpriteName)) return loadedSprite;

#if UNITY_EDITOR
    if (!ShouldAvoidBlockingEditorSpriteFallback() &&
        Application.isEditor &&
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

  static bool ShouldLogVerboseEditorFallbackDebug() {
    if (ForceDisableDebugLogsForPerfPass) return false;
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    if (!SpriteStreamingRuntimeSettings.EnableDiagnostics) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }

#if UNITY_EDITOR
  Sprite TryResolveEditorSliceFallback(string sliceAddress, string channel, bool allowDuringOverlayFallback = false) {
    if (!Application.isEditor || string.IsNullOrWhiteSpace(sliceAddress)) return null;
    if (ShouldAvoidBlockingEditorSpriteFallback() && !allowDuringOverlayFallback) {
      var deferredKey = $"{channel}|editor_fallback_deferred|{sliceAddress}";
      if (_sliceMismatchWarnings.Add(deferredKey) && ShouldLogVerboseEditorFallbackDebug()) {
        Debug.Log(
          "[SpriteWithNormals] Deferred editor slice fallback on " + gameObject.name +
          " channel=" + channel +
          " address='" + sliceAddress + "'" +
          " overlay_active=1"
        );
      }
      return null;
    }
    if (!SpriteAddressResolver.TryLoadEditorSprite(sliceAddress, out var editorSprite) || editorSprite == null) {
      if (!allowDuringOverlayFallback || !TryForceImportPendingSliceAsset(sliceAddress)) {
        return null;
      }
      if (!SpriteAddressResolver.TryLoadEditorSprite(sliceAddress, out editorSprite) || editorSprite == null) {
        return null;
      }
    }

    var key = allowDuringOverlayFallback
      ? $"{channel}|editor_fallback_after_timeout|{sliceAddress}"
      : $"{channel}|editor_fallback|{sliceAddress}";
    if (_sliceMismatchWarnings.Add(key)) {
      if (ShouldLogVerboseEditorFallbackDebug()) {
        Debug.LogWarning(
          "[SpriteWithNormals] Editor slice fallback on " + gameObject.name +
          " channel=" + channel +
          " address='" + sliceAddress + "'" +
          " after_timeout=" + (allowDuringOverlayFallback ? 1 : 0) +
          " sprite='" + editorSprite.name + "'"
        );
      }
    }

    return editorSprite;
  }

  bool TryForceImportPendingSliceAsset(string sliceAddress) {
    if (!Application.isEditor || string.IsNullOrWhiteSpace(sliceAddress)) return false;
    if (!SpriteSliceAddressUtility.TryParseSliceAddress(sliceAddress, out var atlasAssetPath, out _)) return false;
    if (string.IsNullOrWhiteSpace(atlasAssetPath)) return false;

    AssetDatabase.ImportAsset(
      atlasAssetPath,
      ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate
    );
    return true;
  }
#endif

#if UNITY_EDITOR
  public static void InvalidateEditorRuntimeAtlasAvailabilityCache() {
    editorRuntimeAtlasAvailabilityByPath.Clear();
  }

  static bool IsEditorRuntimeAtlasAddressAvailable(string runtimeAddress) {
    var atlasAssetPath = runtimeAddress ?? "";
    if (SpriteSliceAddressUtility.TryParseSliceAddress(atlasAssetPath, out var parsedAtlasAssetPath, out _)) {
      atlasAssetPath = parsedAtlasAssetPath;
    }

    atlasAssetPath = atlasAssetPath.Trim();
    if (string.IsNullOrWhiteSpace(atlasAssetPath)) return false;
    if (editorRuntimeAtlasAvailabilityByPath.TryGetValue(atlasAssetPath, out var cachedAvailable)) {
      return cachedAvailable;
    }

    var available = false;
    var groupFolderPath = Path.Combine("Assets", "AddressableAssetsData", "AssetGroups");
    if (Directory.Exists(groupFolderPath)) {
      var expectedAddressLine = "m_Address: " + atlasAssetPath;
      var groupAssetPaths = Directory.GetFiles(groupFolderPath, "*.asset", SearchOption.TopDirectoryOnly);
      for (var i = 0; i < groupAssetPaths.Length; i++) {
        var groupAssetPath = groupAssetPaths[i];
        if (string.IsNullOrWhiteSpace(groupAssetPath)) continue;

        try {
          foreach (var line in File.ReadLines(groupAssetPath)) {
            if (!string.Equals(line.Trim(), expectedAddressLine, StringComparison.Ordinal)) continue;
            available = true;
            break;
          }
        }
        catch {
          continue;
        }

        if (available) break;
      }
    }

    editorRuntimeAtlasAvailabilityByPath[atlasAssetPath] = available;
    return available;
  }

  void ApplyEditorPreview(SpriteAddressPair pair, SpriteLookupKey lookupKey) {
    if (!SpriteAddressResolver.TryLoadEditorSprite(pair.colorAddress, out var colorSprite) || colorSprite == null) {
      Debug.LogError($"[SpriteWithNormals] Editor preview color sprite not found for '{pair.colorAddress}' ({lookupKey})");
      return;
    }
    SpriteAddressResolver.TryLoadEditorSprite(pair.normalAddress, out var normalSprite);
    if (!string.IsNullOrWhiteSpace(pair.normalAddress) &&
        normalSprite == null &&
        _editorPreviewNormalMissWarnings.Add(pair.normalAddress)) {
      Debug.LogWarning($"[SpriteWithNormals] Editor preview normal sprite not found for '{pair.normalAddress}' ({lookupKey})");
    }
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
      if (!TryResolveLibraryShardPath(normalizedLibrary, out var shardPath)) return false;
      var normalizedShardPath = NormalizeToken(shardPath);
      if (string.IsNullOrWhiteSpace(normalizedShardPath)) return false;
      if (shardOptionsByPath.TryGetValue(normalizedShardPath, out options)) return options != null;
      options = ParseShardOptions(normalizedShardPath);
      shardOptionsByPath[normalizedShardPath] = options;
      return options != null;
    }

    static bool TryResolveLibraryShardPath(string requestedLibrary, out string shardPath) {
      shardPath = "";
      if (string.IsNullOrWhiteSpace(requestedLibrary)) return false;

      if (shardPathByLibrary.TryGetValue(requestedLibrary, out shardPath) && !string.IsNullOrWhiteSpace(shardPath)) {
        return true;
      }

      var slash = requestedLibrary.LastIndexOf('/');
      var leafName = slash >= 0 && slash < requestedLibrary.Length - 1
        ? NormalizeToken(requestedLibrary.Substring(slash + 1))
        : NormalizeToken(requestedLibrary);
      if (string.IsNullOrWhiteSpace(leafName)) return false;

      var suffix = "/" + leafName;
      string matchedLibrary = "";
      foreach (var pair in shardPathByLibrary) {
        if (!string.Equals(pair.Key, leafName, StringComparison.OrdinalIgnoreCase) &&
            !pair.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) {
          continue;
        }

        if (!string.IsNullOrWhiteSpace(matchedLibrary) &&
            !string.Equals(matchedLibrary, pair.Key, StringComparison.OrdinalIgnoreCase)) {
          return false;
        }

        matchedLibrary = pair.Key;
        shardPath = pair.Value;
      }

      return !string.IsNullOrWhiteSpace(shardPath);
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
        loadIssue = $"Sprite index manifest is missing or empty at '{SpriteStreamingConfig.ManifestAssetPath}'. Run Tools > Sprite Streaming > 4) Rebuild Runtime Index.";
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
        if (!labelPrefixSets.TryGetValue(cat, out var set)) labelPrefixSets[cat] = set = new HashSet<string>(StringComparer.Ordinal);
        set.Add(labelPrefix);
      }

      var orderedCategories = new List<string>(categories);
      orderedCategories.Sort(StringComparer.OrdinalIgnoreCase);
      var orderedPrefixes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
      foreach (var kvp in labelPrefixSets) {
        var ordered = new List<string>(kvp.Value);
        ordered.Sort(StringComparer.Ordinal);
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
  SerializedProperty shaderMarginPixelsXProperty;
  SerializedProperty shaderMarginPixelsYProperty;

  void OnEnable() {
    libraryNameProperty = serializedObject.FindProperty(nameof(SpriteWithNormals.libraryName));
    labelPrefixProperty = serializedObject.FindProperty(nameof(SpriteWithNormals.labelPrefix));
    categoryProperty = serializedObject.FindProperty(nameof(SpriteWithNormals.category));
    isAnimationProperty = serializedObject.FindProperty("isAnimation");
    doNotRenderProperty = serializedObject.FindProperty("doNotRender");
    useTrimmedAtlasOffsetProperty = serializedObject.FindProperty("useTrimmedAtlasOffset");
    shaderMarginPixelsXProperty = serializedObject.FindProperty("shaderMarginPixelsX");
    shaderMarginPixelsYProperty = serializedObject.FindProperty("shaderMarginPixelsY");
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
    EditorGUI.BeginChangeCheck();
    EditorGUILayout.PropertyField(shaderMarginPixelsXProperty, new GUIContent("Shader Margin X", "Transparent padding, in source pixels, added on the left and right before rendering."));
    EditorGUILayout.PropertyField(shaderMarginPixelsYProperty, new GUIContent("Shader Margin Y", "Transparent padding, in source pixels, added on the top and bottom before rendering."));
    var shaderMarginChanged = EditorGUI.EndChangeCheck();

    var loadIssue = SpriteIndexInspectorData.GetLoadIssue();
    if (!string.IsNullOrWhiteSpace(loadIssue)) EditorGUILayout.HelpBox(loadIssue, MessageType.Warning);

    var serializedChanged = serializedObject.ApplyModifiedProperties();
    if (serializedChanged || changed) MarkTargetsDirty(targets);
    if (trimmedOffsetToggleChanged || shaderMarginChanged) {
      foreach (var obj in targets) {
        var t = obj as SpriteWithNormals;
        if (t == null) continue;
        if (shaderMarginChanged) {
          t.RefreshShaderMarginPadding();
        } else {
          var refreshFrame = t.IsAnimation ? Mathf.Max(t.LastRequestedFrame, 1) : 0;
          t.ForceUpdateSpriteAndNormal(refreshFrame);
        }
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
