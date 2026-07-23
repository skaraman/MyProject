using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

public partial class SpriteWithNormals : MonoBehaviour {
  static readonly Unity.Profiling.ProfilerMarker ResolvePairProfilerMarker = new("SpriteWithNormals.ResolvePair");
  static readonly Unity.Profiling.ProfilerMarker ApplyLoadedPairProfilerMarker = new("SpriteWithNormals.ApplyLoadedPair");
  static readonly Unity.Profiling.ProfilerMarker RenderApplyProfilerMarker = new("SpriteWithNormals.RenderApply");
  const float EditorSpriteMapSupplementWaitTimeoutSeconds = 0.5f;
  const float EditorSpriteMapSupplementWaitTimeoutDuringOverlaySeconds = 2.0f;
  const float EditorSliceResolveRetryGraceSeconds = 6.0f;
  const string EmptySpriteAssetPath = "Packages/com.skaraman.myprojectcontent/Core/Sprites/Core/Empty.png";

  public string libraryName = "";
  public string labelPrefix = "";
  public string category = "Breathe";

  [SerializeField] bool isAnimation = true;
  [SerializeField] bool doNotRender;
  bool externalVisualSuppressed;
  [SerializeField] bool useTrimmedAtlasOffset;
  [Header("Shader Margins")]
  [SerializeField, Min(0f)] float shaderMarginPixelsX;
  [SerializeField, Min(0f)] float shaderMarginPixelsY;
  [Header("Debug")]
  [SerializeField] bool enableDebugSpriteFetchLogs = false;
  [SerializeField] bool enableDebugSpriteApplyLogs = false;

  SpriteRenderer _renderer;
  MaterialPropertyBlock _mpb;
  Action _trimmedOffsetMetadataReadyCallback;
  int _lastRequestedFrame;
  string _lastRequestedCategory = "";
  string _lastRequestedLibrary = "";
  string _lastRequestedLabelPrefix = "";
  bool _lastRequestedIsAnimation;
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
  const int MaxAnimationAtlasCacheEntries = 128;
  const bool ForceDisableDebugLogsForPerfPass = true;
  static int WarmupRequestBudgetPerFrame => Application.isMobilePlatform ? 64 : 256;
  const int WarmupQueueThrottleThreshold = 2048;
  const int WarmupInFlightThrottleThreshold = 128;
  const int OverlayEnableRefreshBudgetPerFrame = 8;
  const int GameplayEnableRefreshBudgetPerFrame = 24;
  static readonly int NormalMapPropertyId = Shader.PropertyToID("_NormalMap");
  static readonly int SpriteUvRectPropertyId = Shader.PropertyToID("_SpriteUvRect");
  static readonly int SpriteEffectActivePropertyId = Shader.PropertyToID("_SpriteEffectActive");
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
  static readonly HashSet<string> editorRuntimeAtlasAddressIndex = new(StringComparer.OrdinalIgnoreCase);
  static bool editorRuntimeAtlasAddressIndexBuilt;
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
  int _pendingRetargetAllowedFrame;
  int _deferredOverwriteAllowedFrame;
  Texture _lastAppliedNormalTexture;
  bool _hasAppliedNormalTexture;
  bool _hasAppliedSpriteUvRect;
  string _appliedColorSliceAddress = "";
  Vector4 _lastSpriteUvRect;
  float _lastSpriteEffectActive;
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
  int _pairLookupContentReloadVersion = int.MinValue;
  readonly Dictionary<AnimationAtlasCacheKey, string[]> _animationAtlasAddressCache = new();
  readonly List<string> _animationAtlasBuildScratch = new(8);
  readonly HashSet<string> _animationAtlasBuildSeenScratch = new(StringComparer.OrdinalIgnoreCase);
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
    if (useTrimmedAtlasOffset) {
      _trimmedOffsetMetadataReadyCallback = OnTrimmedOffsetMetadataReady;
    }
    SyncSerializedTrimmedOffsetState();
    SyncRendererVisibility();
  }

  void OnEnable() {
    if (useTrimmedAtlasOffset) {
      _trimmedOffsetMetadataReadyCallback ??= OnTrimmedOffsetMetadataReady;
    }
    SyncSerializedTrimmedOffsetState();
    SyncRendererVisibility();
    RefreshTrimmedOffsetForCurrentSprite();
    if (!Application.isPlaying) {
      QueueAutoRefreshOnEnable();
      return;
    }
    _nextInternalRetryFrame = 0;
    _pendingRetargetAllowedFrame = 0;
    _deferredOverwriteAllowedFrame = 0;
    _lastAppliedNormalTexture = null;
    _hasAppliedNormalTexture = false;
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

    var paramsMatch = string.Equals(category, _lastRequestedCategory, StringComparison.Ordinal) &&
                      string.Equals(libraryName, _lastRequestedLibrary, StringComparison.OrdinalIgnoreCase) &&
                      string.Equals(labelPrefix, _lastRequestedLabelPrefix, StringComparison.Ordinal) &&
                      isAnimation == _lastRequestedIsAnimation;
    if (!paramsMatch) return;

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
    _lastAppliedNormalTexture = null;
    _hasAppliedNormalTexture = false;
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
    _lastAppliedNormalTexture = null;
    _hasAppliedNormalTexture = false;
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

  public void SetExternalVisualSuppressed(bool value) {
    if (externalVisualSuppressed == value) return;
    externalVisualSuppressed = value;
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
      var colorWarmupAddress = ResolveWarmupAddress(pair.colorAddress, pair.RuntimeColorAddress);
      var priority = TextureResidencyCache.LoadPriority.Warmup;
      PrimeTrimmedMetadataWarmup(colorWarmupAddress);
      WarmupAddress(pair.StreamingColorAddress, priority);
      WarmupAddress(pair.StreamingNormalAddress, priority);
    }
  }

  // returns true if the color sprite is ready; `colorReadyOnly` is set when the
  // color is available but the normal map is still pending, providing a hint for
  // callers that may choose a gentler fallback.
  public bool IsFrameReady(int frame, out bool colorReadyOnly, string categoryOverride = null) {
    return GetFrameColdLoadState(frame, out colorReadyOnly, categoryOverride).IsCommitReady();
  }

  public SpriteColdLoadState GetFrameColdLoadState(int frame, out bool colorReadyOnly, string categoryOverride = null) {
    colorReadyOnly = false;
    if (!Application.isPlaying) return SpriteColdLoadState.Ready;
    if (doNotRender) return SpriteColdLoadState.Ready;
    var explicitWarmupQuery = !string.IsNullOrWhiteSpace(categoryOverride);
    if ((!enabled || !gameObject.activeInHierarchy) && !explicitWarmupQuery) {
      return SpriteColdLoadState.Ready;
    }

    var lookupLibraryName = libraryName ?? "";
    var lookupLabelPrefix = labelPrefix ?? "";
    var lookupCategory = string.IsNullOrWhiteSpace(categoryOverride) ? (category ?? "") : categoryOverride;
    var lookupFrame = isAnimation ? Mathf.Max(frame, 1) : 0;

    var lookupKey = new SpriteLookupKey(lookupLibraryName, lookupLabelPrefix, lookupCategory, lookupFrame);
    if (IsExplicitEmptyLookup(lookupKey)) {
      return SpriteColdLoadState.ExplicitEmpty;
    }

    var isCurrentQuery = string.IsNullOrEmpty(categoryOverride);
    if (isCurrentQuery) {
      if (!_hasLastLookup || _hasDeferredRequest || HasPendingLoadRequest()) {
        return SpriteColdLoadState.Pending;
      }
    }

    if (!TryResolvePairCached(lookupKey, out var pair, out var pending)) {
      if (pending || (isCurrentQuery && IsSameTransientResolveRetryKey(lookupKey) && _transientResolveRetryCount > 0 && _transientResolveRetryCount < ResolveMissRetryLimit)) {
        return SpriteColdLoadState.Pending;
      }
      return SpriteColdLoadState.Missing;
    }
    pair = StripUnavailableRuntimeNormalAddress(pair, lookupKey);
    if (!pair.HasColor) return SpriteColdLoadState.Missing;
    var colorRequestAddress = ResolveWarmupAddress(pair.colorAddress, pair.StreamingColorAddress);
    PrimeTrimmedMetadataWarmup(colorRequestAddress);
    var colorState = TextureResidencyCache.GetRequestState(colorRequestAddress, pump: false);
    if (!colorState.IsCommitReady()) return colorState;

    if (!TryGetLoadedSpritesForPair(pair, out _, out var normalSprite, out _)) {
      var exactState = TextureResidencyCache.GetRequestState(colorRequestAddress, pump: false);
      return exactState.IsCommitReady() ? SpriteColdLoadState.Pending : exactState;
    }
    if (pair.HasNormal && normalSprite == null) {
      var normalRequestAddress = ResolveWarmupAddress(pair.normalAddress, pair.StreamingNormalAddress);
      var exactNormalState = TextureResidencyCache.GetRequestState(normalRequestAddress, pump: false);
      return exactNormalState.IsCommitReady() ? SpriteColdLoadState.Pending : exactNormalState;
    }

    var metadataState = GetTrimmedMetadataState(colorRequestAddress, requestIfNeeded: true);
    if (!metadataState.IsCommitReady()) return metadataState;

    if (pair.HasNormal &&
        TextureResidencyCache.GetRequestState(ResolveWarmupAddress(pair.normalAddress, pair.StreamingNormalAddress), pump: false) == SpriteColdLoadState.Pending) {
      colorReadyOnly = true;
    }
    return SpriteColdLoadState.Ready;
  }

  bool IsExplicitEmptyLookup(SpriteLookupKey lookupKey) {
    if (HasBlankCategoryAndLabelPrefix(lookupKey)) return true;
    if (!isAnimation && string.IsNullOrWhiteSpace(lookupKey.labelPrefix)) return true;
    return string.Equals(lookupKey.labelPrefix ?? "", "Empty", StringComparison.OrdinalIgnoreCase);
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
      var staticColorAddress = ResolveWarmupAddress(staticPair.colorAddress, staticPair.RuntimeColorAddress);
      var staticNormalAddress = ResolveWarmupAddress(staticPair.normalAddress, staticPair.RuntimeNormalAddress);
      TrackTrimmedMetadataWarmupCandidate(staticColorAddress);
      AddUniqueAddress(outAddresses, staticColorAddress, seenAddresses, maxAddresses);
      AddUniqueAddress(outAddresses, staticNormalAddress, seenAddresses, maxAddresses);
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
      var colorWarmupAddress = ResolveWarmupAddress(pair.colorAddress, pair.RuntimeColorAddress);
      var normalWarmupAddress = ResolveWarmupAddress(pair.normalAddress, pair.RuntimeNormalAddress);
      TrackTrimmedMetadataWarmupCandidate(colorWarmupAddress);
      AddUniqueAddress(outAddresses, colorWarmupAddress, seenAddresses, maxAddresses);
      AddUniqueAddress(outAddresses, normalWarmupAddress, seenAddresses, maxAddresses);
    }
  }

  public void CollectAnimationAtlasAddresses(
    string categoryOverride,
    int startFrame,
    int endFrame,
    List<string> outAddresses,
    HashSet<string> seenAddresses,
    int maxUniqueAddresses = int.MaxValue
  ) {
    if (outAddresses == null) return;
    var maxAddresses = Mathf.Max(maxUniqueAddresses, 1);
    if (outAddresses.Count >= maxAddresses) return;

    var lookupCategory = string.IsNullOrWhiteSpace(categoryOverride)
      ? (category ?? "")
      : categoryOverride;
    if (string.IsNullOrWhiteSpace(libraryName) || string.IsNullOrWhiteSpace(lookupCategory)) return;

    var minFrame = isAnimation ? Mathf.Max(Mathf.Min(startFrame, endFrame), 1) : 0;
    var maxFrame = isAnimation ? Mathf.Max(Mathf.Max(startFrame, endFrame), minFrame) : 0;
    var cacheKey = new AnimationAtlasCacheKey(
      lookupCategory,
      minFrame,
      maxFrame,
      ActiveContentRegistryRuntime.ReloadVersion
    );

    if (!_animationAtlasAddressCache.TryGetValue(cacheKey, out var atlasAddresses)) {
      atlasAddresses = BuildAnimationAtlasAddresses(lookupCategory, minFrame, maxFrame);
      if (_animationAtlasAddressCache.Count >= MaxAnimationAtlasCacheEntries) {
        _animationAtlasAddressCache.Clear();
      }
      _animationAtlasAddressCache[cacheKey] = atlasAddresses;
    }

    for (var i = 0; i < atlasAddresses.Length; i++) {
      if (outAddresses.Count >= maxAddresses) return;
      AddUniqueAddress(outAddresses, atlasAddresses[i], seenAddresses, maxAddresses);
    }
  }

  public void CollectAnimationAtlasAddressesUncached(
    string categoryOverride,
    int startFrame,
    int endFrame,
    List<string> outAddresses,
    HashSet<string> seenAddresses,
    int maxUniqueAddresses = int.MaxValue
  ) {
    if (outAddresses == null) return;
    var maxAddresses = Mathf.Max(maxUniqueAddresses, 1);
    if (outAddresses.Count >= maxAddresses) return;

    var lookupCategory = string.IsNullOrWhiteSpace(categoryOverride)
      ? (category ?? "")
      : categoryOverride;
    if (string.IsNullOrWhiteSpace(libraryName) || string.IsNullOrWhiteSpace(lookupCategory)) return;

    var minFrame = isAnimation ? Mathf.Max(Mathf.Min(startFrame, endFrame), 1) : 0;
    var maxFrame = isAnimation ? Mathf.Max(Mathf.Max(startFrame, endFrame), minFrame) : 0;
    CollectAnimationAtlasAddressRange(
      lookupCategory,
      minFrame,
      maxFrame,
      outAddresses,
      seenAddresses,
      maxAddresses
    );
  }

  string[] BuildAnimationAtlasAddresses(string lookupCategory, int minFrame, int maxFrame) {
    _animationAtlasBuildScratch.Clear();
    _animationAtlasBuildSeenScratch.Clear();

    CollectAnimationAtlasAddressRange(
      lookupCategory,
      minFrame,
      maxFrame,
      _animationAtlasBuildScratch,
      _animationAtlasBuildSeenScratch,
      int.MaxValue
    );

    var result = _animationAtlasBuildScratch.ToArray();
    _animationAtlasBuildScratch.Clear();
    _animationAtlasBuildSeenScratch.Clear();
    return result;
  }

  void CollectAnimationAtlasAddressRange(
    string lookupCategory,
    int minFrame,
    int maxFrame,
    List<string> outAddresses,
    HashSet<string> seenAddresses,
    int maxAddresses
  ) {
    for (var frame = minFrame; frame <= maxFrame; frame++) {
      if (outAddresses.Count >= maxAddresses) return;
      if (!TryGetFrameAddressPair(frame, out var pair, lookupCategory)) continue;
      AddUniqueAddress(
        outAddresses,
        pair.RuntimeColorAddress,
        seenAddresses,
        maxAddresses
      );
      AddUniqueAddress(
        outAddresses,
        pair.RuntimeNormalAddress,
        seenAddresses,
        maxAddresses
      );
    }
  }

  static string ResolveWarmupAddress(string sliceAddress, string fallbackAtlasAddress) {
    if (!string.IsNullOrWhiteSpace(sliceAddress)) return sliceAddress;
    return string.IsNullOrWhiteSpace(fallbackAtlasAddress) ? "" : fallbackAtlasAddress;
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
    EnsureContentReloadVersion();

    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    if (_renderer == null) return;
    if (doNotRender) return;

    var lookupLibraryName = libraryName ?? "";
    var lookupCategory = category ?? "";
    var lookupLabelPrefix = labelPrefix ?? "";
    _lastRequestedCategory = lookupCategory;
    _lastRequestedLibrary = lookupLibraryName;
    _lastRequestedLabelPrefix = lookupLabelPrefix;
    _lastRequestedIsAnimation = isAnimation;
    var lookupFrame = isAnimation
      ? ResolveLookupFrameForPendingMiss(frame, lookupLibraryName, lookupLabelPrefix, lookupCategory)
      : 0;
    if (ShouldLogFetch) {
      LogSpriteFetch(
        "lookup_begin",
        "lib='" + lookupLibraryName + "' label='" + lookupLabelPrefix + "' category='" + lookupCategory + "'" +
        " frame=" + lookupFrame + " internal=" + (_isInternalTickRequest ? 1 : 0)
      );
    }

    if (_hasLastLookup &&
        lookupFrame == _lastLookupFrame &&
        string.Equals(lookupLibraryName, _lastLookupLibraryName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(lookupLabelPrefix, _lastLookupLabelPrefix, StringComparison.Ordinal) &&
        string.Equals(lookupCategory, _lastLookupCategory, StringComparison.Ordinal)) {
      if (ShouldLogFetch) LogSpriteFetch("skip_same_lookup");
      return;
    }

    var lookupKey = new SpriteLookupKey(lookupLibraryName, lookupLabelPrefix, lookupCategory, lookupFrame);
    if (TryApplyBlankSelectionEmptyFallback(lookupKey)) return;
    if (TryApplyNoPrefixStaticEmptyFallback(lookupKey)) return;
    ResolvePairProfilerMarker.Begin();
    var pairResolved = TryResolvePairCached(lookupKey, out var pair, out var pending);
    ResolvePairProfilerMarker.End();
    if (!pairResolved) {
      if (pending) {
        if (ShouldLogFetch) LogSpriteFetch("resolve_pending", "key='" + lookupKey + "'");
        return;
      }
      if (TryApplyExplicitEmptyLabelFallback(lookupKey)) return;
      if (TryScheduleTransientResolveRetry(lookupKey)) {
        if (ShouldLogFetch) LogSpriteFetch("resolve_retry_scheduled", "key='" + lookupKey + "' attempt=" + _transientResolveRetryCount);
        return;
      }
      if (ShouldLogFetch) LogSpriteFetch("resolve_failed", "key='" + lookupKey + "'");
      ReportResolveError(lookupKey);
      _hasLastLookup = true;
      return;
    }
    pair = StripUnavailableRuntimeNormalAddress(pair, lookupKey);
    if (ShouldLogFetch) {
      LogSpriteFetch(
        "resolve_success",
        "key='" + lookupKey + "' color='" + (pair.colorAddress ?? "") + "' normal='" + (pair.normalAddress ?? "") + "'"
      );
    }

    ResetTransientResolveRetryState();
    _hasLastResolveError = false;
    _hasLastLookup = true;
    _lastLookupLibraryName = lookupLibraryName;
    _lastLookupLabelPrefix = lookupLabelPrefix;
    _lastLookupCategory = lookupCategory;
    _lastLookupFrame = lookupFrame;

    if (AddressEquals(pair.colorAddress, _targetColorSliceAddress) &&
        AddressEquals(pair.normalAddress, _targetNormalSliceAddress)) {
      if (ShouldLogFetch) LogSpriteFetch("skip_same_target_addresses");
      return;
    }

    if (Application.isPlaying &&
        ShouldDeferTargetRetargetForOutstandingMiss(
          pair,
          lookupLibraryName,
          lookupLabelPrefix,
          lookupCategory
        )) {
      return;
    }

    _targetColorAddress = pair.RuntimeColorAddress ?? "";
    _targetNormalAddress = pair.RuntimeNormalAddress ?? "";
    _targetColorSliceAddress = pair.colorAddress ?? "";
    _targetNormalSliceAddress = pair.normalAddress ?? "";

    if (Application.isPlaying) {
      ApplyLoadedPairProfilerMarker.Begin();
      var appliedLoadedPair = TryApplyLoadedSpritesFromCache(pair, cancelPendingIfAny: true);
      ApplyLoadedPairProfilerMarker.End();
      if (appliedLoadedPair) {
        if (ShouldLogFetch) LogSpriteFetch("use_cached_pair");
        return;
      }

      if (isAnimation && PrefetchAheadFrames > 0 && !IsOverlayWarmGateActive()) {
        WarmupUpcomingAnimationFrames(lookupLibraryName, lookupLabelPrefix, lookupCategory, lookupFrame, pair);
      }
      if (ShouldLogFetch) {
        LogSpriteFetch(
          "fetch_miss_queue_runtime_load",
          "color='" + (_targetColorAddress ?? "") + "' normal='" + (_targetNormalAddress ?? "") + "'"
        );
      }
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
        if (ShouldLogFetch) LogSpriteFetch("queue_skip_same_pending", "color='" + (_pendingColorAddress ?? "") + "' normal='" + (_pendingNormalAddress ?? "") + "'");
        return;
      }
      if (_hasDeferredRequest) {
        if (AddressEquals(_deferredRequest.RuntimeColorAddress, pair.RuntimeColorAddress) &&
            AddressEquals(_deferredRequest.RuntimeNormalAddress, pair.RuntimeNormalAddress)) {
          if (ShouldLogFetch) LogSpriteFetch("queue_skip_same_deferred", "color='" + (pair.colorAddress ?? "") + "' normal='" + (pair.normalAddress ?? "") + "'");
          return;
        }
        if (Time.frameCount < _deferredOverwriteAllowedFrame) {
          if (ShouldLogFetch) {
            LogSpriteFetch(
              "queue_deferred_overwrite_throttled",
              "incoming_color='" + (pair.colorAddress ?? "") + "' incoming_normal='" + (pair.normalAddress ?? "") + "'"
            );
          }
          return;
        }
      }
      _deferredRequest = pair;
      _hasDeferredRequest = true;
      _deferredOverwriteAllowedFrame = Time.frameCount + ResolveDeferredOverwriteCooldownFrames();
      if (ShouldLogFetch) LogSpriteFetch("queue_deferred", "color='" + (pair.colorAddress ?? "") + "' normal='" + (pair.normalAddress ?? "") + "'");
      return;
    }
    if (ShouldLogFetch) LogSpriteFetch("queue_start_now");
    StartRuntimeLoad(pair, cacheKnownMiss);
  }

  void StartRuntimeLoad(SpriteAddressPair pair, bool cacheKnownMiss = false) {
    if (ShouldLogFetch) LogSpriteFetch("load_start", "color='" + (pair.colorAddress ?? "") + "' normal='" + (pair.normalAddress ?? "") + "'");
    var hasPendingState = HasPendingLoadRequest() || _hasDeferredRequest;
    if (!cacheKnownMiss) {
      if (!hasPendingState && TryApplyLoadedSpritesFromCache(pair)) {
        if (ShouldLogFetch) LogSpriteFetch("use_fastpath_cache_hit");
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
        if (ShouldLogFetch) LogSpriteFetch("use_cache_hit_applied");
        _pendingColorAddress = _pendingNormalAddress = "";
        _pendingColorSliceAddress = _pendingNormalSliceAddress = "";
        TryStartDeferredRequest();
        return;
      }
    }
    if (ShouldLogFetch) LogSpriteFetch("fetch_cache_miss");

    var colorPriority = ResolveActiveFrameLoadPriority();

    var colorLease = TextureResidencyCache.AcquireAsync(pair.StreamingColorAddress, colorPriority);
    if (colorLease == null) {
      Debug.LogError($"[SpriteWithNormals] Failed to request color atlas '{pair.StreamingColorAddress}' on {gameObject.name}");
      _pendingColorAddress = _pendingNormalAddress = "";
      _pendingColorSliceAddress = _pendingNormalSliceAddress = "";
      TryStartDeferredRequest();
      return;
    }
    if (ShouldLogFetch) LogSpriteFetch("load_requested_color", "priority=" + colorPriority + " is_done=" + (colorLease.IsDone ? 1 : 0));

    // Color drives visual frame readiness; keep normal map on warmup priority to reduce queue pressure.
    var normalPriority = TextureResidencyCache.LoadPriority.Warmup;
    var normalLease = string.IsNullOrWhiteSpace(pair.StreamingNormalAddress)
      ? null
      : TextureResidencyCache.AcquireAsync(pair.StreamingNormalAddress, normalPriority);

    if (!string.IsNullOrWhiteSpace(pair.StreamingNormalAddress) && normalLease == null)
      Debug.LogError($"[SpriteWithNormals] Failed to request normal atlas '{pair.StreamingNormalAddress}' on {gameObject.name}");
    else if (!string.IsNullOrWhiteSpace(pair.StreamingNormalAddress)) {
      if (ShouldLogFetch) LogSpriteFetch("load_requested_normal", "priority=" + normalPriority + " is_done=" + (normalLease != null && normalLease.IsDone ? 1 : 0));
    }

    _pendingColorLease = colorLease;
    _pendingNormalLease = normalLease;
    var requestVersion = ++_requestVersion;
    if (colorLease.IsDone) {
      if (ShouldWaitForPendingSpriteMapSupplement(colorLease, pair.colorAddress, normalLease, pair.normalAddress)) {
        if (ShouldLogFetch) LogSpriteFetch("load_wait_sprite_supplement", "request_version=" + requestVersion);
        BeginPendingLoadRequest(requestVersion, pair);
        return;
      }
      if (ShouldLogFetch) LogSpriteFetch("load_color_immediate_complete", "request_version=" + requestVersion);
      CompleteLoadedSprites(requestVersion, colorLease, normalLease, pair);
      return;
    }

    _pendingRetargetAllowedFrame = Time.frameCount + ResolveOutstandingRetargetCooldownFrames();
    if (ShouldLogFetch) LogSpriteFetch("load_wait_async", "request_version=" + requestVersion);
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
      if (ShouldLogFetch) LogSpriteFetch("use_lookup_miss_color", "address='" + (pair.colorAddress ?? "") + "' source='local+global'");
      return false;
    }

    var colorRequestAddress = ResolveWarmupAddress(pair.colorAddress, pair.StreamingColorAddress);
    var colorState = TextureResidencyCache.GetRequestState(colorRequestAddress, pump: false);
    if (!colorState.IsCommitReady()) {
      if (ShouldLogFetch) LogSpriteFetch("use_lookup_pending_color", "address='" + (colorRequestAddress ?? "") + "' state='" + colorState + "'");
      return false;
    }

    var metadataState = GetTrimmedMetadataState(colorRequestAddress, requestIfNeeded: true);
    if (!metadataState.IsCommitReady()) {
      if (ShouldLogFetch) LogSpriteFetch("use_lookup_pending_metadata", "address='" + (colorRequestAddress ?? "") + "' state='" + metadataState + "'");
      return false;
    }

    if (ShouldLogFetch) {
      LogSpriteFetch(
        "use_lookup_hit_color",
        "address='" + (pair.colorAddress ?? "") + "' sprite='" + colorSprite.name + "' source='" + sourceTag + "'"
      );
    }
    if (!string.IsNullOrWhiteSpace(pair.normalAddress)) {
      if (ShouldLogFetch) {
        LogSpriteFetch(
          normalSprite != null ? "use_lookup_hit_normal" : "use_lookup_miss_normal",
          "address='" + (pair.normalAddress ?? "") + "'" +
          (normalSprite != null ? " sprite='" + normalSprite.name + "'" : "") +
          " source='" + sourceTag + "'"
        );
      }
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
      if (ShouldLogFetch) LogSpriteFetch("complete_version_mismatch", "incoming=" + requestVersion + " current=" + _requestVersion);
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
      if (ShouldLogFetch) {
        LogSpriteFetch(
          "complete_stale",
          "requested_color='" + (pair.colorAddress ?? "") + "' target_color='" + (_targetColorAddress ?? "") + "'" +
          " requested_normal='" + (pair.normalAddress ?? "") + "' target_normal='" + (_targetNormalAddress ?? "") + "'"
        );
      }
      if (ShouldLogFetch) LogSpriteFetch("complete_stale_dropped");
      ReleaseLease(ref colorLease);
      ReleaseLease(ref normalLease);
      TryStartDeferredRequest();
      return;
    }
    var colorSliceAddress = string.IsNullOrWhiteSpace(_targetColorSliceAddress) ? pair.colorAddress : _targetColorSliceAddress;
    var normalSliceAddress = string.IsNullOrWhiteSpace(_targetNormalSliceAddress) ? pair.normalAddress : _targetNormalSliceAddress;
    var colorRequestAddress = ResolveWarmupAddress(colorSliceAddress, pair.StreamingColorAddress);
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
      var colorState = TextureResidencyCache.GetRequestState(colorRequestAddress, pump: false);
      if (colorState == SpriteColdLoadState.Pending) {
        return;
      }
      if (ShouldWaitForPendingSpriteMapSupplement(colorLease, colorSliceAddress)) {
        if (_pendingLoadRequestVersion != requestVersion) {
          BeginPendingLoadRequest(requestVersion, pair);
        }
        if (ShouldLogFetch) LogSpriteFetch("complete_wait_sprite_supplement", "request_version=" + requestVersion);
        return;
      }
#if UNITY_EDITOR
      if (TryKeepPendingSliceResolve(colorLease, colorSliceAddress, allowBlockingEditorSliceFallbackNow, "color")) {
        return;
      }
#endif
      CompleteColorResolveFailure("complete_color_missing", colorSliceAddress, ref colorLease, ref normalLease);
      Debug.LogError($"[SpriteWithNormals] Failed to resolve color sprite '{colorSliceAddress}' on {gameObject.name}");
      return;
    }
    colorSprite = ResolveExpectedSliceSprite(colorSprite, colorSliceAddress, "color");
    if (colorSprite == null) {
      var colorState = TextureResidencyCache.GetRequestState(colorRequestAddress, pump: false);
      if (colorState == SpriteColdLoadState.Pending) return;
      CompleteColorResolveFailure("complete_color_mismatch", colorSliceAddress, ref colorLease, ref normalLease);
      return;
    }
    CacheLocalLoadedSprite(colorSliceAddress, colorSprite);

    var normalSprite = ResolveLeaseSprite(normalLease, normalSliceAddress);
#if UNITY_EDITOR
    normalSprite ??= TryResolveEditorSliceFallback(normalSliceAddress, "normal", allowBlockingEditorSliceFallbackNow);
#endif
    if (normalLease != null &&
        !string.IsNullOrWhiteSpace(normalSliceAddress) &&
        normalSprite == null) {
#if UNITY_EDITOR
      if (TryKeepPendingSliceResolve(normalLease, normalSliceAddress, allowBlockingEditorSliceFallbackNow, "normal")) {
        return;
      }
#endif
    }
    normalSprite = ResolveExpectedSliceSprite(normalSprite, normalSliceAddress, "normal");
    if (normalSprite == null && !string.IsNullOrWhiteSpace(normalSliceAddress)) {
      var normalRequestAddress = ResolveWarmupAddress(normalSliceAddress, pair.StreamingNormalAddress);
      if (TextureResidencyCache.GetRequestState(normalRequestAddress, pump: false) == SpriteColdLoadState.Pending) return;
    }
    if (normalSprite != null) CacheLocalLoadedSprite(normalSliceAddress, normalSprite);
    if (normalLease != null && normalLease.IsDone && normalSprite == null && !string.IsNullOrWhiteSpace(normalSliceAddress))
      Debug.LogError($"[SpriteWithNormals] Failed to resolve normal sprite '{normalSliceAddress}' on {gameObject.name}");

    var metadataState = GetTrimmedMetadataState(colorRequestAddress, requestIfNeeded: true);
    if (metadataState == SpriteColdLoadState.Pending) {
      return;
    }

    if (ShouldLogFetch) {
      LogSpriteFetch(
        "complete_apply",
        "color='" + (colorSprite != null ? colorSprite.name : "") + "' normal='" + (normalSprite != null ? normalSprite.name : "") + "'"
      );
    }

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
    TextureResidencyCache.Lease lease,
    string sliceAddress,
    bool allowBlockingEditorSliceFallback,
    string channel
  ) {
    if (!Application.isEditor || !Application.isPlaying) return false;
    if (lease == null) return false;
    if (lease != _pendingColorLease && lease != _pendingNormalLease) return false;
    if (!SpriteSliceAddressUtility.TryParseSliceAddress(sliceAddress, out _, out _)) return false;

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
      TryForceImportPendingSliceAsset(sliceAddress);
    }

    var warningKey = "pending_slice_retry|" + channel + "|" + sliceAddress;
    if (_sliceMismatchWarnings.Add(warningKey)) {
      Debug.LogWarning(
        "[SpriteWithNormals] Delaying unresolved slice resolve on " + gameObject.name +
        " channel=" + (string.IsNullOrWhiteSpace(channel) ? "unknown" : channel) +
        " address='" + sliceAddress + "'" +
        " elapsed_ms=" + Mathf.RoundToInt(elapsed * 1000f) +
        " allow_editor_fallback=" + (allowBlockingEditorSliceFallback ? 1 : 0)
      );
    }

    return true;
  }
#endif

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
        WarmupAddress(warmupPair.StreamingColorAddress, TextureResidencyCache.LoadPriority.Warmup);
        WarmupAddress(warmupPair.StreamingNormalAddress, TextureResidencyCache.LoadPriority.Warmup);
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
    if (ShouldAvoidBlockingEditorSpriteFallback() &&
        !ShouldDeferEditorSliceFallbackForAddress(sliceOrAtlasAddress)) {
      return false;
    }
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

  static bool ShouldDeferEditorSliceFallbackForAddress(string sliceAddress) {
    if (string.IsNullOrWhiteSpace(sliceAddress)) return true;
    return sliceAddress.IndexOf("/Environments/", StringComparison.OrdinalIgnoreCase) >= 0;
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



}
