using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

public class SpriteWithNormals : MonoBehaviour {
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

  SpriteRenderer _renderer;
  MaterialPropertyBlock _mpb;
  int _lastRequestedFrame;
  int _lastExternalRequestFrame = int.MinValue;
  bool _isInternalTickRequest;

  const int ExternalDriverHoldFrames = 2;
  const int PrefetchAheadFrames = 2;
  const int InternalRetryFrames = 2;
  const int MaxPairLookupCacheEntries = 2048;
  static readonly int NormalMapPropertyId = Shader.PropertyToID("_NormalMap");

  int _requestVersion;
  string _targetColorAddress = "";
  string _targetNormalAddress = "";
  string _pendingColorAddress = "";
  string _pendingNormalAddress = "";
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

  Coroutine _pendingLoadRoutine;
  TextureResidencyCache.Lease _pendingColorLease;
  TextureResidencyCache.Lease _pendingNormalLease;
  TextureResidencyCache.Lease _activeColorLease;
  TextureResidencyCache.Lease _activeNormalLease;
  readonly Dictionary<PairLookupCacheKey, SpriteAddressPair> _pairLookupHitCache = new();
  readonly HashSet<PairLookupCacheKey> _pairLookupMissCache = new();

  void Awake() {
    _renderer = GetComponent<SpriteRenderer>();
    _mpb = new MaterialPropertyBlock();
    SyncRendererVisibility();
  }

  void OnEnable() {
    if (!Application.isPlaying) return;
    _nextInternalRetryFrame = 0;
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

    TextureResidencyCache.Pump();
    _isInternalTickRequest = true;
    try { UpdateSpriteAndNormal(_lastRequestedFrame); }
    finally { _isInternalTickRequest = false; }
  }

  void OnDisable() {
    if (Application.isPlaying) {
      SpriteUiPinService.Unregister(this);
    }
    if (!Application.isPlaying) return;
    CancelPendingRequest();
    ReleaseActiveLeases();
    ResetPairLookupCaches();
    _sliceMismatchWarnings.Clear();
  }

  void OnDestroy() {
    if (Application.isPlaying) {
      SpriteUiPinService.Unregister(this);
    }
    CancelPendingRequest();
    ReleaseActiveLeases();
    ResetPairLookupCaches();
    _sliceMismatchWarnings.Clear();
  }

  public bool IsAnimation => isAnimation;
  public bool DoNotRender => doNotRender;
  public int LastRequestedFrame => _lastRequestedFrame;

  public void SetAnimation(string value) { category = value; _hasLastLookup = false; ResetPairLookupCaches(); }
  public void SetLibraryName(string value) { libraryName = value; _hasLastLookup = false; ResetPairLookupCaches(); }
  public void SetLabelPrefix(string value) { labelPrefix = value; _hasLastLookup = false; ResetPairLookupCaches(); }
  public void SetIsAnimation(bool value) { isAnimation = value; _hasLastLookup = false; ResetPairLookupCaches(); }

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
      WarmupAddress(pair.colorAddress, TextureResidencyCache.LoadPriority.Warmup);
      WarmupAddress(pair.normalAddress, TextureResidencyCache.LoadPriority.Warmup);
    }
  }

  public bool IsFrameReady(int frame, string categoryOverride = null) {
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
    return TextureResidencyCache.IsReady(pair.colorAddress, pump: false);
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
      AddUniqueAddress(outAddresses, staticPair.colorAddress, seenAddresses, maxAddresses);
      AddUniqueAddress(outAddresses, staticPair.normalAddress, seenAddresses, maxAddresses);
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
      AddUniqueAddress(outAddresses, pair.colorAddress, seenAddresses, maxAddresses);
      AddUniqueAddress(outAddresses, pair.normalAddress, seenAddresses, maxAddresses);
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
    var lookupFrame = isAnimation ? Mathf.Max(frame, 1) : 0;

    if (_hasLastLookup &&
        lookupFrame == _lastLookupFrame &&
        string.Equals(lookupLibraryName, _lastLookupLibraryName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(lookupLabelPrefix, _lastLookupLabelPrefix, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(lookupCategory, _lastLookupCategory, StringComparison.OrdinalIgnoreCase)) return;

    var lookupKey = new SpriteLookupKey(lookupLibraryName, lookupLabelPrefix, lookupCategory, lookupFrame);
    if (!TryResolvePairCached(lookupKey, out var pair, out var pending)) {
      if (pending) return;
      ReportResolveError(lookupKey);
      return;
    }

    _hasLastResolveError = false;
    _hasLastLookup = true;
    _lastLookupLibraryName = lookupLibraryName;
    _lastLookupLabelPrefix = lookupLabelPrefix;
    _lastLookupCategory = lookupCategory;
    _lastLookupFrame = lookupFrame;

    if (pair.colorAddress == _targetColorAddress && pair.normalAddress == _targetNormalAddress) return;

    _targetColorAddress = pair.colorAddress ?? "";
    _targetNormalAddress = pair.normalAddress ?? "";

    if (Application.isPlaying) {
      if (isAnimation) WarmupUpcomingAnimationFrames(lookupLibraryName, lookupLabelPrefix, lookupCategory, lookupFrame, pair);
      QueueRuntimeLoad(pair);
      return;
    }

#if UNITY_EDITOR
    ApplyEditorPreview(pair, lookupKey);
#endif
  }

  public void FlipSprite(bool flip) {
    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    if (_renderer != null) _renderer.flipX = flip;
  }

  void QueueRuntimeLoad(SpriteAddressPair pair) {
    if (_pendingLoadRoutine != null) {
      if (AddressEquals(_pendingColorAddress, pair.colorAddress) && AddressEquals(_pendingNormalAddress, pair.normalAddress)) return;
      _deferredRequest = pair;
      _hasDeferredRequest = true;
      return;
    }
    StartRuntimeLoad(pair);
  }

  void StartRuntimeLoad(SpriteAddressPair pair) {
    CancelPendingRequest();
    _pendingColorAddress = pair.colorAddress ?? "";
    _pendingNormalAddress = pair.normalAddress ?? "";

    if (TryApplyLoadedSpritesFromCache(pair)) {
      _pendingColorAddress = _pendingNormalAddress = "";
      TryStartDeferredRequest();
      return;
    }

    var colorLease = TextureResidencyCache.AcquireAsync(pair.colorAddress, TextureResidencyCache.LoadPriority.Immediate);
    if (colorLease == null) {
      Debug.LogError($"[SpriteWithNormals] Failed to request color address '{pair.colorAddress}' on {gameObject.name}");
      _pendingColorAddress = _pendingNormalAddress = "";
      TryStartDeferredRequest();
      return;
    }

    var normalLease = string.IsNullOrWhiteSpace(pair.normalAddress)
      ? null
      : TextureResidencyCache.AcquireAsync(pair.normalAddress, TextureResidencyCache.LoadPriority.Immediate);
    if (!string.IsNullOrWhiteSpace(pair.normalAddress) && normalLease == null)
      Debug.LogError($"[SpriteWithNormals] Failed to request normal address '{pair.normalAddress}' on {gameObject.name}");

    _pendingColorLease = colorLease;
    _pendingNormalLease = normalLease;
    var requestVersion = ++_requestVersion;
    if (colorLease.IsDone) {
      CompleteLoadedSprites(requestVersion, colorLease, normalLease, pair);
      return;
    }

    _pendingLoadRoutine = StartCoroutine(ApplyLoadedSprites(requestVersion, colorLease, normalLease, pair));
  }

  IEnumerator ApplyLoadedSprites(int requestVersion, TextureResidencyCache.Lease colorLease, TextureResidencyCache.Lease normalLease, SpriteAddressPair pair) {
    while (colorLease != null && !colorLease.IsDone) {
      TextureResidencyCache.Pump();
      yield return null;
    }

    CompleteLoadedSprites(requestVersion, colorLease, normalLease, pair);
  }

  bool TryApplyLoadedSpritesFromCache(SpriteAddressPair pair) {
    if (!TextureResidencyCache.TryGetLoadedSprite(pair.colorAddress, out var colorSprite) || colorSprite == null) {
      return false;
    }

    Sprite normalSprite = null;
    if (!string.IsNullOrWhiteSpace(pair.normalAddress)) {
      TextureResidencyCache.TryGetLoadedSprite(pair.normalAddress, out normalSprite);
    }
    colorSprite = ResolveExpectedSliceSprite(colorSprite, pair.colorAddress, "color");
    normalSprite = ResolveExpectedSliceSprite(normalSprite, pair.normalAddress, "normal");

    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    if (_renderer != null) ApplySprites(colorSprite, normalSprite);

    ReleaseActiveLeases();
    return true;
  }

  void CompleteLoadedSprites(int requestVersion, TextureResidencyCache.Lease colorLease, TextureResidencyCache.Lease normalLease, SpriteAddressPair pair) {
    if (requestVersion != _requestVersion) {
      ReleaseLease(ref colorLease);
      ReleaseLease(ref normalLease);
      return;
    }

    ClearPendingState();

    var colorSprite = colorLease != null && colorLease.IsSuccess ? colorLease.Sprite : null;
    if (colorSprite == null) {
      Debug.LogError($"[SpriteWithNormals] Failed to load color sprite '{pair.colorAddress}' on {gameObject.name}");
      ReleaseLease(ref colorLease);
      ReleaseLease(ref normalLease);
      TryStartDeferredRequest();
      return;
    }
    colorSprite = ResolveExpectedSliceSprite(colorSprite, pair.colorAddress, "color");

    var normalSprite = normalLease != null && normalLease.IsDone && normalLease.IsSuccess ? normalLease.Sprite : null;
    normalSprite = ResolveExpectedSliceSprite(normalSprite, pair.normalAddress, "normal");
    if (normalLease != null && normalLease.IsDone && normalSprite == null)
      Debug.LogError($"[SpriteWithNormals] Failed to load normal sprite '{pair.normalAddress}' on {gameObject.name}");

    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    if (_renderer != null) ApplySprites(colorSprite, normalSprite);

    ReleaseActiveLeases();
    _activeColorLease = colorLease;
    _activeNormalLease = normalLease;
    TryStartDeferredRequest();
  }

  void WarmupUpcomingAnimationFrames(string libraryName, string labelPrefix, string category, int lookupFrame, SpriteAddressPair currentPair) {
    if (lookupFrame <= 0 || string.IsNullOrWhiteSpace(libraryName) || string.IsNullOrWhiteSpace(category)) return;
    if (_pendingLoadRoutine != null || _hasDeferredRequest) return;

    for (var offset = 1; offset <= PrefetchAheadFrames; offset++) {
      var warmupKey = new SpriteLookupKey(libraryName, labelPrefix, category, lookupFrame + offset);
      if (!TryResolvePairCached(warmupKey, out var warmupPair, out var pending)) {
        if (pending) return;
        break;
      }
      if (!AddressEquals(warmupPair.colorAddress, currentPair.colorAddress) || !AddressEquals(warmupPair.normalAddress, currentPair.normalAddress)) {
        WarmupAddress(warmupPair.colorAddress, TextureResidencyCache.LoadPriority.Warmup);
        WarmupAddress(warmupPair.normalAddress, TextureResidencyCache.LoadPriority.Warmup);
      }
    }
  }

  void ApplySprites(Sprite colorSprite, Sprite normalSprite) {
    _renderer.sprite = colorSprite;
    _mpb ??= new MaterialPropertyBlock();
    _renderer.GetPropertyBlock(_mpb);
    var normalTexture = normalSprite != null ? normalSprite.texture : GetFallbackNormalTexture();
    if (normalTexture != null) _mpb.SetTexture(NormalMapPropertyId, normalTexture);
    _renderer.SetPropertyBlock(_mpb);
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
    if (_pendingLoadRoutine != null) { StopCoroutine(_pendingLoadRoutine); _pendingLoadRoutine = null; }
    ReleaseLease(ref _pendingColorLease);
    ReleaseLease(ref _pendingNormalLease);
    _pendingColorAddress = _pendingNormalAddress = "";
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
  }

  void TryStartDeferredRequest() {
    if (!_hasDeferredRequest) return;
    var deferred = _deferredRequest;
    _hasDeferredRequest = false;
    _deferredRequest = default;
    QueueRuntimeLoad(deferred);
  }

  static bool AddressEquals(string left, string right) {
    if (string.IsNullOrEmpty(left)) return string.IsNullOrEmpty(right);
    if (string.IsNullOrEmpty(right)) return false;
    return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
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
    TextureResidencyCache.RequestLoad(address, priority);
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
    ApplySprites(colorSprite, normalSprite);
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

  void OnEnable() {
    libraryNameProperty = serializedObject.FindProperty(nameof(SpriteWithNormals.libraryName));
    labelPrefixProperty = serializedObject.FindProperty(nameof(SpriteWithNormals.labelPrefix));
    categoryProperty = serializedObject.FindProperty(nameof(SpriteWithNormals.category));
    isAnimationProperty = serializedObject.FindProperty("isAnimation");
    doNotRenderProperty = serializedObject.FindProperty("doNotRender");
  }

  public override void OnInspectorGUI() {
    serializedObject.Update();

    var changed = DrawDropdown(libraryNameProperty, "Library Name", BuildLibraryOptions());
    changed |= DrawDropdown(categoryProperty, "Category", BuildCategoryOptions(libraryNameProperty.stringValue));
    changed |= DrawDropdown(labelPrefixProperty, "Label Prefix", BuildLabelPrefixOptions(libraryNameProperty.stringValue, categoryProperty.stringValue));
    EditorGUILayout.PropertyField(isAnimationProperty, new GUIContent("Is Animation", "When enabled, frame input drives this label prefix as an animation loop."));
    EditorGUILayout.PropertyField(doNotRenderProperty, new GUIContent("Do Not Render", "When enabled, this component keeps the SpriteRenderer disabled."));

    var loadIssue = SpriteIndexInspectorData.GetLoadIssue();
    if (!string.IsNullOrWhiteSpace(loadIssue)) EditorGUILayout.HelpBox(loadIssue, MessageType.Warning);

    if (serializedObject.ApplyModifiedProperties() || changed) MarkTargetsDirty(targets);

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
