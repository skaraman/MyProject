using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static class TrimmedSpriteOffsetResolver {
  [Serializable]
  sealed class TrimmedAtlasOffsetPayload {
    public List<TrimmedSpriteOffsetEntry> sprites = new();
  }

  [Serializable]
  sealed class TrimmedSpriteOffsetEntry {
    public string name;
    public PixelPoint offsetFromCellCenterPx;
  }

  [Serializable]
  struct PixelPoint {
    public float x;
    public float y;
  }

  sealed class AtlasOffsets {
    readonly Dictionary<string, Vector2> exactOffsetsBySpriteName = new(StringComparer.Ordinal);

    public void Set(string spriteName, Vector2 offsetPx) {
      if (string.IsNullOrWhiteSpace(spriteName)) return;
      exactOffsetsBySpriteName[spriteName] = offsetPx;
    }

    public bool TryGet(string spriteName, out Vector2 offsetPx) {
      offsetPx = Vector2.zero;
      var normalizedSpriteName = spriteName ?? "";
      if (string.IsNullOrWhiteSpace(normalizedSpriteName)) return false;
      if (exactOffsetsBySpriteName.TryGetValue(normalizedSpriteName, out offsetPx)) return true;

      foreach (var pair in exactOffsetsBySpriteName) {
        if (!SpriteSliceAddressUtility.HasEquivalentNumericLabel(pair.Key, normalizedSpriteName)) continue;
        offsetPx = pair.Value;
        return true;
      }

      return false;
    }
  }

  static readonly Dictionary<string, AtlasOffsets> loadedAtlasOffsets = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> missingAtlasOffsets = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, AsyncOperationHandle<TextAsset>> pendingLoads = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, AsyncOperationHandle<IList<IResourceLocation>>> pendingLocationChecks = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, List<Action>> pendingCallbacks = new(StringComparer.OrdinalIgnoreCase);
  static readonly Stack<List<Action>> pendingCallbackListPool = new();
  static readonly HashSet<string> warmupEligibleAtlasPaths = new(StringComparer.OrdinalIgnoreCase);
  static readonly Queue<string> pendingOverlayWarmupLoadQueue = new();
  static readonly HashSet<string> pendingOverlayWarmupLoadSet = new(StringComparer.OrdinalIgnoreCase);
  static readonly Queue<string> pendingWarmGateRuntimeLoadQueue = new();
  static readonly HashSet<string> pendingWarmGateRuntimeLoadSet = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> deferredEditorMetadataLoadLogs = new(StringComparer.OrdinalIgnoreCase);
  static int runtimeLoadPumpFrame = -1;

  static bool HasPendingOptionalOffsetOperation(string atlasAssetPath) {
    if (string.IsNullOrWhiteSpace(atlasAssetPath)) return false;
    return pendingLoads.ContainsKey(atlasAssetPath) || pendingLocationChecks.ContainsKey(atlasAssetPath);
  }
  /// <summary>
  /// Returns true when both the warm gate queue and all async Addressables operations are drained.
  /// Use this to verify the loading pipeline is fully idle before signaling "Loaded" state.
  /// </summary>
  public static bool IsWarmGateLoadIdle() {
    return pendingWarmGateRuntimeLoadQueue.Count <= 0 && pendingLoads.Count <= 0 && pendingLocationChecks.Count <= 0;
}

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetOnDomainReload() {
    foreach (var pair in pendingLoads) {
      if (!pair.Value.IsValid()) continue;
      Addressables.Release(pair.Value);
    }
    foreach (var pair in pendingLocationChecks) {
      if (!pair.Value.IsValid()) continue;
      Addressables.Release(pair.Value);
    }

    loadedAtlasOffsets.Clear();
    missingAtlasOffsets.Clear();
    pendingLoads.Clear();
    pendingLocationChecks.Clear();
    pendingCallbacks.Clear();
    pendingCallbackListPool.Clear();
    warmupEligibleAtlasPaths.Clear();
    pendingOverlayWarmupLoadQueue.Clear();
    pendingOverlayWarmupLoadSet.Clear();
    pendingWarmGateRuntimeLoadQueue.Clear();
    pendingWarmGateRuntimeLoadSet.Clear();
    deferredEditorMetadataLoadLogs.Clear();
    runtimeLoadPumpFrame = -1;
  }

  public static void InvalidateAtlas(string atlasAssetPath) {
    var normalizedAtlasPath = NormalizeAtlasPath(atlasAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedAtlasPath)) return;

    loadedAtlasOffsets.Remove(normalizedAtlasPath);
    missingAtlasOffsets.Remove(normalizedAtlasPath);
    pendingOverlayWarmupLoadSet.Remove(normalizedAtlasPath);
    pendingWarmGateRuntimeLoadSet.Remove(normalizedAtlasPath);
    deferredEditorMetadataLoadLogs.Remove(normalizedAtlasPath);

    if (pendingLoads.TryGetValue(normalizedAtlasPath, out var loadHandle)) {
      pendingLoads.Remove(normalizedAtlasPath);
      if (loadHandle.IsValid()) {
        Addressables.Release(loadHandle);
      }
    }
    if (pendingLocationChecks.TryGetValue(normalizedAtlasPath, out var locationHandle)) {
      pendingLocationChecks.Remove(normalizedAtlasPath);
      if (locationHandle.IsValid()) {
        Addressables.Release(locationHandle);
      }
    }

    pendingCallbacks.Remove(normalizedAtlasPath);
  }

  static bool ShouldLogVerboseOverlayMetadataDebug() {
    if (!Application.isPlaying) return false;
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    if (!SpriteStreamingRuntimeSettings.EnableDiagnostics) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }

  static List<Action> RentPendingCallbackList() {
    if (pendingCallbackListPool.Count > 0) {
      var pooled = pendingCallbackListPool.Pop();
      pooled.Clear();
      return pooled;
    }
    return new List<Action>();
  }

  static void ReturnPendingCallbackList(List<Action> callbacks) {
    if (callbacks == null) return;
    callbacks.Clear();
    pendingCallbackListPool.Push(callbacks);
  }

  public static void PumpDeferredRuntimeLoads() {
    if (!Application.isPlaying) return;
    if (pendingOverlayWarmupLoadQueue.Count <= 0 && pendingWarmGateRuntimeLoadQueue.Count <= 0) return;

    var frame = Time.frameCount;
    if (runtimeLoadPumpFrame == frame) return;
    runtimeLoadPumpFrame = frame;

    PumpOverlayWarmupRuntimeLoads();
    if (pendingWarmGateRuntimeLoadQueue.Count <= 0) return;
    if (StreamingWarmOrchestrator.IsWarmGateRunning) return;

    var budget = Math.Max(ResolveDeferredRuntimeLoadBudgetPerFrame(), 1);
    var processed = 0;
    while (processed < budget && pendingWarmGateRuntimeLoadQueue.Count > 0) {
      var atlasAssetPath = NormalizeAtlasPath(pendingWarmGateRuntimeLoadQueue.Dequeue());
      if (string.IsNullOrWhiteSpace(atlasAssetPath)) continue;
      if (!pendingWarmGateRuntimeLoadSet.Remove(atlasAssetPath)) continue;
      if (loadedAtlasOffsets.ContainsKey(atlasAssetPath)) continue;
      if (missingAtlasOffsets.Contains(atlasAssetPath)) continue;
      if (HasPendingOptionalOffsetOperation(atlasAssetPath)) continue;
      StartOptionalOffsetRuntimeLoad(atlasAssetPath);
      processed++;
    }
  }

  public static int QueueWarmupAtlasMetadataBatch(IList<string> atlasAddresses, int startInclusive = 0, int count = int.MaxValue) {
    if (atlasAddresses == null || atlasAddresses.Count <= 0) return 0;
    var start = Mathf.Clamp(startInclusive, 0, atlasAddresses.Count);
    var requestedCount = count == int.MaxValue ? atlasAddresses.Count - start : Math.Max(count, 0);
    var endExclusive = (int)Math.Min((long)atlasAddresses.Count, (long)start + requestedCount);
    var queued = 0;
    for (var i = start; i < endExclusive; i++) {
      if (!IsOptionalOffsetWarmupCandidateRegistered(atlasAddresses[i])) continue;
      if (QueueOverlayWarmupOptionalOffsetLoad(atlasAddresses[i])) queued++;
    }
    return queued;
  }

  public static int PrimeMetadataBatch(
    IList<string> atlasAddresses,
    bool allowImmediateEditorLoad = false,
    int startInclusive = 0,
    int count = int.MaxValue
  ) {
    if (atlasAddresses == null || atlasAddresses.Count <= 0) return 0;
    var start = Mathf.Clamp(startInclusive, 0, atlasAddresses.Count);
    var requestedCount = count == int.MaxValue ? atlasAddresses.Count - start : Math.Max(count, 0);
    var endExclusive = (int)Math.Min((long)atlasAddresses.Count, (long)start + requestedCount);
    var primed = 0;
    for (var i = start; i < endExclusive; i++) {
      if (!IsOptionalOffsetWarmupCandidateRegistered(atlasAddresses[i])) continue;
      if (TryPrimeOptionalOffsetLoad(atlasAddresses[i], allowImmediateEditorLoad)) primed++;
    }
    return primed;
  }

  public static void RegisterWarmupMetadataCandidate(string atlasOrSliceAddress) {
    if (!TryResolveWarmupOptionalOffsetCandidateAtlasPath(atlasOrSliceAddress, out var atlasAssetPath)) return;
    warmupEligibleAtlasPaths.Add(atlasAssetPath);
  }

  public static bool IsReady(
    string atlasOrSliceAddress,
    bool pump = false,
    bool requestIfNeeded = false,
    bool allowImmediateEditorLoad = false
  ) {
    return GetMetadataState(atlasOrSliceAddress, pump, requestIfNeeded, allowImmediateEditorLoad).IsCommitReady();
  }

  public static SpriteColdLoadState GetMetadataState(
    string atlasOrSliceAddress,
    bool pump = false,
    bool requestIfNeeded = false,
    bool allowImmediateEditorLoad = false
  ) {
    if (!TryResolveWarmupOptionalOffsetCandidateAtlasPath(atlasOrSliceAddress, out var atlasAssetPath)) {
      return SpriteColdLoadState.Ready;
    }

    if (requestIfNeeded) {
      warmupEligibleAtlasPaths.Add(atlasAssetPath);
    }
    else if (!warmupEligibleAtlasPaths.Contains(atlasAssetPath)) {
      return SpriteColdLoadState.Ready;
    }

    if (loadedAtlasOffsets.ContainsKey(atlasAssetPath)) {
      return SpriteColdLoadState.Ready;
    }

    if (missingAtlasOffsets.Contains(atlasAssetPath)) {
      return SupportsWarmupOptionalOffsetMetadata(atlasAssetPath)
        ? SpriteColdLoadState.Missing
        : SpriteColdLoadState.Ready;
    }

    if (requestIfNeeded) {
      TryPrimeOptionalOffsetLoad(atlasAssetPath, allowImmediateEditorLoad);
    }

    if (pump) {
      PumpDeferredRuntimeLoads();
    }

    if (loadedAtlasOffsets.ContainsKey(atlasAssetPath)) {
      return SpriteColdLoadState.Ready;
    }

    if (missingAtlasOffsets.Contains(atlasAssetPath)) {
      return SupportsWarmupOptionalOffsetMetadata(atlasAssetPath)
        ? SpriteColdLoadState.Missing
        : SpriteColdLoadState.Ready;
    }

    return SpriteColdLoadState.Pending;
  }

  public static bool TryGetExactOffset(string sliceAddress, out Vector2 offsetPx, Action onReady = null) {
    offsetPx = Vector2.zero;
    if (!TryParseSliceAddress(sliceAddress, out var atlasAssetPath, out var spriteName)) return false;
    if (TryGetLoadedOffset(atlasAssetPath, spriteName, out offsetPx)) return true;
    if (ShouldSkipOptionalOffsetAtlas(atlasAssetPath, "lookup")) return false;
    if (missingAtlasOffsets.Contains(atlasAssetPath)) return false;

#if UNITY_EDITOR
    if (TryDeferEditorMetadataLoadDuringOverlay(atlasAssetPath, onReady)) {
      return false;
    }

    if (TryLoadEditorOffsets(atlasAssetPath)) {
      return TryGetLoadedOffset(atlasAssetPath, spriteName, out offsetPx);
    }

    if (Application.isEditor) return false;
#endif

    if (!Application.isPlaying) return false;
    RegisterPendingCallback(atlasAssetPath, onReady);
    StartOptionalOffsetRuntimeLoad(atlasAssetPath);
    return false;
  }

  public static bool TryGetExactLocalOffset(
    string sliceAddress,
    Sprite sprite,
    out Vector3 localOffset,
    bool flipX = false,
    bool flipY = false,
    Action onReady = null) {
    localOffset = Vector3.zero;
    if (sprite == null) return false;
    if (!TryGetExactOffsetForSprite(sliceAddress, sprite, out var offsetPx, onReady)) return false;

    localOffset = ConvertOffsetPixelsToLocalUnits(offsetPx, sprite, flipX, flipY);
    return true;
  }

  public static Vector3 ConvertOffsetPixelsToLocalUnits(Vector2 offsetPx, Sprite sprite, bool flipX = false, bool flipY = false) {
    var pixelsPerUnit = sprite != null && sprite.pixelsPerUnit > 0f ? sprite.pixelsPerUnit : 100f;
    var x = offsetPx.x / pixelsPerUnit;
    var y = offsetPx.y / pixelsPerUnit;
    if (flipX) x = -x;
    if (flipY) y = -y;
    return new Vector3(x, y, 0f);
  }

  static bool TryGetLoadedOffset(string atlasAssetPath, string spriteName, out Vector2 offsetPx) {
    offsetPx = Vector2.zero;
    if (!loadedAtlasOffsets.TryGetValue(atlasAssetPath, out var atlasOffsets) || atlasOffsets == null) return false;
    return atlasOffsets.TryGet(spriteName, out offsetPx);
  }

  static bool TryGetExactOffsetForSprite(string sliceAddress, Sprite sprite, out Vector2 offsetPx, Action onReady) {
    offsetPx = Vector2.zero;
    if (!TryParseSliceAddress(sliceAddress, out var atlasAssetPath, out var expectedSpriteName)) return false;

    var loadedSpriteName = sprite != null ? (sprite.name ?? "") : "";
    if (TryGetLoadedOffsetForCandidates(atlasAssetPath, loadedSpriteName, expectedSpriteName, out offsetPx)) return true;
    if (ShouldSkipOptionalOffsetAtlas(atlasAssetPath, "lookup_for_sprite")) return false;
    if (missingAtlasOffsets.Contains(atlasAssetPath)) return false;

#if UNITY_EDITOR
    if (TryDeferEditorMetadataLoadDuringOverlay(atlasAssetPath, onReady)) {
      return false;
    }

    if (TryLoadEditorOffsets(atlasAssetPath)) {
      return TryGetLoadedOffsetForCandidates(atlasAssetPath, loadedSpriteName, expectedSpriteName, out offsetPx);
    }

    if (Application.isEditor) return false;
#endif

    if (!Application.isPlaying) return false;
    RegisterPendingCallback(atlasAssetPath, onReady);
    StartOptionalOffsetRuntimeLoad(atlasAssetPath);
    return false;
  }

  static bool TryGetLoadedOffsetForCandidates(string atlasAssetPath, string primarySpriteName, string fallbackSpriteName, out Vector2 offsetPx) {
    if (TryGetLoadedOffset(atlasAssetPath, primarySpriteName, out offsetPx)) return true;
    if (string.Equals(primarySpriteName, fallbackSpriteName, StringComparison.Ordinal)) return false;
    return TryGetLoadedOffset(atlasAssetPath, fallbackSpriteName, out offsetPx);
  }

#if UNITY_EDITOR
  static bool TryLoadEditorOffsets(string atlasAssetPath) {
    if (loadedAtlasOffsets.ContainsKey(atlasAssetPath)) return true;
    if (ShouldSkipOptionalOffsetAtlas(atlasAssetPath, "editor_load")) return false;
    if (missingAtlasOffsets.Contains(atlasAssetPath)) return false;

    var metadataAssetPath = BuildOptionalOffsetMetadataAssetPath(atlasAssetPath);
    var metadataAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(metadataAssetPath);
    if (metadataAsset == null || string.IsNullOrWhiteSpace(metadataAsset.text)) {
      missingAtlasOffsets.Add(atlasAssetPath);
      return false;
    }

    if (!TryParseAtlasOffsets(metadataAsset.text, out var atlasOffsets)) {
      missingAtlasOffsets.Add(atlasAssetPath);
      return false;
    }

    loadedAtlasOffsets[atlasAssetPath] = atlasOffsets;
    return true;
  }

  static bool TryDeferEditorMetadataLoadDuringOverlay(string atlasAssetPath, Action onReady) {
    if (!ShouldAvoidBlockingEditorMetadataLoad()) return false;
    RegisterPendingCallback(atlasAssetPath, onReady);
    var warmGateRunning = StreamingWarmOrchestrator.IsWarmGateRunning;
    if (warmGateRunning) {
      QueueWarmGateOptionalOffsetLoad(atlasAssetPath);
    }
    else {
      QueueOverlayWarmupOptionalOffsetLoad(atlasAssetPath);
    }
    if (deferredEditorMetadataLoadLogs.Add(atlasAssetPath) && ShouldLogVerboseOverlayMetadataDebug()) {
      Debug.Log(
        "[TrimmedSpriteOffsetResolver] " +
        (warmGateRunning ? "Queued editor metadata load until warm gate clears" : "Deferred editor metadata load") +
        " atlas='" + atlasAssetPath + "'" +
        " overlay_active=" + (SpriteStreamingLoadingState.IsLoadingOverlayActive ? 1 : 0) +
        " warm_gate_running=" + (StreamingWarmOrchestrator.IsWarmGateRunning ? 1 : 0)
      );
    }
    return true;
  }

  static bool ShouldAvoidBlockingEditorMetadataLoad() {
    if (!Application.isEditor || !Application.isPlaying) return false;
    return SpriteStreamingLoadingState.IsProtectedLoadingOverlayActive || StreamingWarmOrchestrator.IsWarmGateRunning;
  }
#endif

  static void QueueWarmGateOptionalOffsetLoad(string atlasAssetPath) {
    var normalizedAtlasPath = NormalizeAtlasPath(atlasAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedAtlasPath)) return;
    if (ShouldSkipOptionalOffsetAtlas(normalizedAtlasPath, "warm_gate_queue")) return;
    if (loadedAtlasOffsets.ContainsKey(normalizedAtlasPath)) return;
    if (missingAtlasOffsets.Contains(normalizedAtlasPath)) return;
    if (HasPendingOptionalOffsetOperation(normalizedAtlasPath)) return;
    if (!pendingWarmGateRuntimeLoadSet.Add(normalizedAtlasPath)) return;
    pendingWarmGateRuntimeLoadQueue.Enqueue(normalizedAtlasPath);
  }

  static bool QueueOverlayWarmupOptionalOffsetLoad(string atlasOrSliceAddress) {
    if (!TryResolveWarmupOptionalOffsetAtlasPath(atlasOrSliceAddress, out var normalizedAtlasPath)) return false;
    if (loadedAtlasOffsets.ContainsKey(normalizedAtlasPath)) return false;
    if (missingAtlasOffsets.Contains(normalizedAtlasPath)) return false;
    if (HasPendingOptionalOffsetOperation(normalizedAtlasPath)) return false;
    if (!pendingOverlayWarmupLoadSet.Add(normalizedAtlasPath)) return false;
    pendingOverlayWarmupLoadQueue.Enqueue(normalizedAtlasPath);
    return true;
  }

  static bool TryPrimeOptionalOffsetLoad(string atlasOrSliceAddress, bool allowImmediateEditorLoad) {
    if (!TryResolveWarmupOptionalOffsetAtlasPath(atlasOrSliceAddress, out var atlasAssetPath)) return false;
    if (loadedAtlasOffsets.ContainsKey(atlasAssetPath)) return true;
    if (missingAtlasOffsets.Contains(atlasAssetPath)) return true;
    if (HasPendingOptionalOffsetOperation(atlasAssetPath)) return true;

#if UNITY_EDITOR
    if (allowImmediateEditorLoad && Application.isEditor) {
      if (TryLoadEditorOffsets(atlasAssetPath)) return true;
      if (missingAtlasOffsets.Contains(atlasAssetPath)) return true;
    }
#endif

    if (!Application.isPlaying) return false;
    StartOptionalOffsetRuntimeLoad(atlasAssetPath);
    return true;
  }

  static int ResolveDeferredRuntimeLoadBudgetPerFrame() {
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive) return 1;
    return 4;
  }

  static int ResolveOverlayWarmupRuntimeLoadBudgetPerFrame() {
    if (SpriteStreamingLoadingState.IsProtectedLoadingOverlayActive || StreamingWarmOrchestrator.IsWarmGateRunning) return 1;
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive) return 2;
    return 4;
  }

  static void PumpOverlayWarmupRuntimeLoads() {
    if (pendingOverlayWarmupLoadQueue.Count <= 0) return;
    var budget = Math.Max(ResolveOverlayWarmupRuntimeLoadBudgetPerFrame(), 1);
    var processed = 0;
    while (processed < budget && pendingOverlayWarmupLoadQueue.Count > 0) {
      var atlasAssetPath = NormalizeAtlasPath(pendingOverlayWarmupLoadQueue.Dequeue());
      if (string.IsNullOrWhiteSpace(atlasAssetPath)) continue;
      if (!pendingOverlayWarmupLoadSet.Remove(atlasAssetPath)) continue;
      if (loadedAtlasOffsets.ContainsKey(atlasAssetPath)) continue;
      if (missingAtlasOffsets.Contains(atlasAssetPath)) continue;
      if (HasPendingOptionalOffsetOperation(atlasAssetPath)) continue;
      StartOptionalOffsetRuntimeLoad(atlasAssetPath);
      processed++;
    }
  }

  static void StartOptionalOffsetRuntimeLoad(string atlasAssetPath) {
    if (string.IsNullOrWhiteSpace(atlasAssetPath)) return;
    if (loadedAtlasOffsets.ContainsKey(atlasAssetPath)) return;
    if (ShouldSkipOptionalOffsetAtlas(atlasAssetPath, "runtime_load")) return;
    if (missingAtlasOffsets.Contains(atlasAssetPath)) return;
    if (HasPendingOptionalOffsetOperation(atlasAssetPath)) return;

    var metadataAssetPath = BuildOptionalOffsetMetadataAssetPath(atlasAssetPath);
    if (string.IsNullOrWhiteSpace(metadataAssetPath)) {
      missingAtlasOffsets.Add(atlasAssetPath);
      return;
    }
#if UNITY_EDITOR
    if (!OptionalOffsetMetadataAssetExistsInEditor(metadataAssetPath)) {
      missingAtlasOffsets.Add(atlasAssetPath);
      return;
    }
#else
    var locationHandle = Addressables.LoadResourceLocationsAsync(metadataAssetPath, typeof(TextAsset));
    pendingLocationChecks[atlasAssetPath] = locationHandle;
    locationHandle.Completed += operation => CompleteRuntimeLocationCheck(atlasAssetPath, metadataAssetPath, operation);
    return;
#endif
    StartResolvedOptionalOffsetRuntimeLoad(atlasAssetPath, metadataAssetPath);
  }

  static void StartResolvedOptionalOffsetRuntimeLoad(string atlasAssetPath, string metadataAssetPath) {
    if (string.IsNullOrWhiteSpace(atlasAssetPath) || string.IsNullOrWhiteSpace(metadataAssetPath)) return;
    var loadStartedAt = ShouldMeasureRuntimeLoadStartCosts() ? Time.realtimeSinceStartup : 0f;
    AsyncOperationHandle<TextAsset> loadHandle;
    try {
      loadHandle = Addressables.LoadAssetAsync<TextAsset>(metadataAssetPath);
    }
    catch (Exception ex) {
      missingAtlasOffsets.Add(atlasAssetPath);
      if (ShouldLogVerboseOverlayMetadataDebug()) {
        Debug.Log(
          "[TrimmedSpriteOffsetResolver][MetadataMissing]" +
          " atlas='" + atlasAssetPath + "'" +
          " metadata='" + metadataAssetPath + "'" +
          " source=runtime_load" +
          " reason='" + ex.GetType().Name + "'");
      }
      return;
    }
    MaybeLogSlowRuntimeLoadStart(atlasAssetPath, metadataAssetPath, loadStartedAt);
    pendingLoads[atlasAssetPath] = loadHandle;
    loadHandle.Completed += operation => CompleteRuntimeLoad(atlasAssetPath, operation);
  }

  static void CompleteRuntimeLocationCheck(
    string atlasAssetPath,
    string metadataAssetPath,
    AsyncOperationHandle<IList<IResourceLocation>> operation
  ) {
    pendingLocationChecks.Remove(atlasAssetPath);

    var hasLocation = operation.Status == AsyncOperationStatus.Succeeded &&
                      operation.Result != null &&
                      operation.Result.Count > 0;

    if (operation.IsValid()) {
      Addressables.Release(operation);
    }

    if (!hasLocation) {
      missingAtlasOffsets.Add(atlasAssetPath);
      NotifyPendingCallbacks(atlasAssetPath);
      return;
    }

    StartResolvedOptionalOffsetRuntimeLoad(atlasAssetPath, metadataAssetPath);
  }

  static void CompleteRuntimeLoad(string atlasAssetPath, AsyncOperationHandle<TextAsset> operation) {
    pendingLoads.Remove(atlasAssetPath);

    if (operation.Status == AsyncOperationStatus.Succeeded &&
        operation.Result != null &&
        TryParseAtlasOffsets(operation.Result.text, out var atlasOffsets)) {
      loadedAtlasOffsets[atlasAssetPath] = atlasOffsets;
      missingAtlasOffsets.Remove(atlasAssetPath);
    } else {
      missingAtlasOffsets.Add(atlasAssetPath);
    }

    if (operation.IsValid()) {
      Addressables.Release(operation);
    }

    // Completion callback triggers final pump cycle to drain any remaining queued items before Loaded state.
    if (pendingLoads.Count <= 0 && pendingWarmGateRuntimeLoadQueue.Count > 0) {
      PumpDeferredRuntimeLoads();
    }
    NotifyPendingCallbacks(atlasAssetPath);
  }

  static void RegisterPendingCallback(string atlasAssetPath, Action onReady) {
    if (onReady == null || string.IsNullOrWhiteSpace(atlasAssetPath)) return;
    if (!pendingCallbacks.TryGetValue(atlasAssetPath, out var callbacks) || callbacks == null) {
      callbacks = RentPendingCallbackList();
      pendingCallbacks[atlasAssetPath] = callbacks;
    }

    for (var i = 0; i < callbacks.Count; i++) {
      if (callbacks[i] == onReady) return;
    }

    callbacks.Add(onReady);
  }

  static void NotifyPendingCallbacks(string atlasAssetPath) {
    if (!pendingCallbacks.TryGetValue(atlasAssetPath, out var callbacks) || callbacks == null || callbacks.Count <= 0) {
      pendingCallbacks.Remove(atlasAssetPath);
      return;
    }

    pendingCallbacks.Remove(atlasAssetPath);
    for (var i = 0; i < callbacks.Count; i++) {
      callbacks[i]?.Invoke();
    }
    ReturnPendingCallbackList(callbacks);
  }

  static bool ShouldMeasureRuntimeLoadStartCosts() {
    if (!Application.isPlaying) return false;
    if (!SpriteStreamingLoadingState.IsLoadingOverlayActive && !StreamingWarmOrchestrator.IsWarmGateRunning) return false;
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    if (!SpriteStreamingRuntimeSettings.EnableDiagnostics) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }

  static void MaybeLogSlowRuntimeLoadStart(string atlasAssetPath, string metadataAssetPath, float startedAt) {
    if (!ShouldMeasureRuntimeLoadStartCosts()) return;
    var elapsedMs = Mathf.Max((Time.realtimeSinceStartup - startedAt) * 1000f, 0f);
    if (elapsedMs < 25f) return;
    Debug.LogWarning(
      "[TrimmedSpriteOffsetResolver][LoadStartDiag] start_ms=" + elapsedMs.ToString("0.0") +
      " atlas='" + (atlasAssetPath ?? "") + "'" +
      " metadata='" + (metadataAssetPath ?? "") + "'" +
      " overlay_active=" + (SpriteStreamingLoadingState.IsLoadingOverlayActive ? 1 : 0) +
      " warm_gate_running=" + (StreamingWarmOrchestrator.IsWarmGateRunning ? 1 : 0)
    );
  }

  static bool TryParseSliceAddress(string sliceAddress, out string atlasAssetPath, out string spriteName) {
    atlasAssetPath = "";
    spriteName = "";
    if (!SpriteSliceAddressUtility.TryParseSliceAddress(sliceAddress, out var parsedAtlasAssetPath, out var parsedSpriteName)) return false;

    atlasAssetPath = NormalizeAtlasPath(parsedAtlasAssetPath);
    spriteName = parsedSpriteName ?? "";
    return !string.IsNullOrWhiteSpace(atlasAssetPath) && !string.IsNullOrWhiteSpace(spriteName);
  }

  static bool TryResolveWarmupOptionalOffsetAtlasPath(string atlasOrSliceAddress, out string atlasAssetPath) {
    atlasAssetPath = "";
    if (string.IsNullOrWhiteSpace(atlasOrSliceAddress)) return false;
    if (TryParseSliceAddress(atlasOrSliceAddress, out var parsedAtlasAssetPath, out _)) {
      atlasAssetPath = parsedAtlasAssetPath;
    }
    else {
      atlasAssetPath = NormalizeAtlasPath(atlasOrSliceAddress);
    }
    if (string.IsNullOrWhiteSpace(atlasAssetPath)) return false;
    if (ShouldSkipOptionalOffsetAtlas(atlasAssetPath, "warmup_queue")) return false;
    return SupportsWarmupOptionalOffsetMetadata(atlasAssetPath);
  }

  static bool TryResolveWarmupOptionalOffsetCandidateAtlasPath(string atlasOrSliceAddress, out string atlasAssetPath) {
    atlasAssetPath = "";
    if (string.IsNullOrWhiteSpace(atlasOrSliceAddress)) return false;
    if (TryParseSliceAddress(atlasOrSliceAddress, out var parsedAtlasAssetPath, out _)) {
      atlasAssetPath = parsedAtlasAssetPath;
    }
    else {
      atlasAssetPath = NormalizeAtlasPath(atlasOrSliceAddress);
    }
    if (string.IsNullOrWhiteSpace(atlasAssetPath)) return false;
    return SupportsWarmupOptionalOffsetMetadata(atlasAssetPath);
  }

  static bool IsOptionalOffsetWarmupCandidateRegistered(string atlasOrSliceAddress) {
    if (!TryResolveWarmupOptionalOffsetCandidateAtlasPath(atlasOrSliceAddress, out var atlasAssetPath)) return false;
    return warmupEligibleAtlasPaths.Contains(atlasAssetPath);
  }

  static bool SupportsWarmupOptionalOffsetMetadata(string atlasAssetPath) {
    var extension = Path.GetExtension(atlasAssetPath);
    if (string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)) return false;
    if (string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)) return false;
    if (!SupportsOptionalOffsetMetadata(atlasAssetPath)) return false;
#if UNITY_EDITOR
    return OptionalOffsetMetadataAssetExistsInEditor(BuildOptionalOffsetMetadataAssetPath(atlasAssetPath));
#else
    return true;
#endif
  }

  static bool SupportsOptionalOffsetMetadata(string atlasAssetPath) {
    return GeneratedAtlasBuildSurrogateUtility.CanAtlasPathUseMetadata(atlasAssetPath);
  }

  static bool TryParseAtlasOffsets(string jsonText, out AtlasOffsets atlasOffsets) {
    atlasOffsets = null;
    if (string.IsNullOrWhiteSpace(jsonText)) return false;

    TrimmedAtlasOffsetPayload payload;
    try {
      payload = JsonUtility.FromJson<TrimmedAtlasOffsetPayload>(jsonText);
    }
    catch {
      return false;
    }

    if (payload == null) return false;

    atlasOffsets = new AtlasOffsets();
    if (payload.sprites == null) return true;

    for (var i = 0; i < payload.sprites.Count; i++) {
      var sprite = payload.sprites[i];
      if (sprite == null || string.IsNullOrWhiteSpace(sprite.name)) continue;
      atlasOffsets.Set(sprite.name, new Vector2(sprite.offsetFromCellCenterPx.x, sprite.offsetFromCellCenterPx.y));
    }

    return true;
  }

  static string BuildOptionalOffsetMetadataAssetPath(string atlasAssetPath) {
    return GeneratedAtlasBuildSurrogateUtility.BuildMetadataAssetPath(atlasAssetPath);
  }

  static string NormalizeAtlasPath(string atlasAssetPath) {
    if (string.IsNullOrWhiteSpace(atlasAssetPath)) return "";
    return atlasAssetPath.Trim().Replace("\\", "/");
  }

#if UNITY_EDITOR
  static bool OptionalOffsetMetadataAssetExistsInEditor(string metadataAssetPath) {
    var normalizedMetadataAssetPath = NormalizeAtlasPath(metadataAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedMetadataAssetPath)) return false;
    if (!string.IsNullOrWhiteSpace(AssetDatabase.AssetPathToGUID(normalizedMetadataAssetPath))) return true;
    return File.Exists(Path.GetFullPath(normalizedMetadataAssetPath));
  }
#endif

  static bool ShouldSkipOptionalOffsetAtlas(string atlasAssetPath, string source) {
    var normalizedAtlasPath = NormalizeAtlasPath(atlasAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedAtlasPath)) return true;
    if (SupportsOptionalOffsetMetadata(normalizedAtlasPath)) return false;

    var firstSkip = missingAtlasOffsets.Add(normalizedAtlasPath);
    if (firstSkip && Application.isPlaying && (Application.isEditor || Debug.isDebugBuild)) {
      Debug.Log(
        "[TrimmedSpriteOffsetResolver][MetadataSkip]" +
        " atlas='" + normalizedAtlasPath + "'" +
        " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
        " excluded_folders='" + GeneratedAtlasBuildSurrogateUtility.MetadataExcludedFolderSummary + "'"
      );
    }

    return true;
  }
}
