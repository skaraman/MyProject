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
    readonly Dictionary<string, Vector2> offsetsByNumericLabel = new(StringComparer.Ordinal);

    public void Set(string spriteName, Vector2 offsetPx) {
      if (string.IsNullOrWhiteSpace(spriteName)) return;
      exactOffsetsBySpriteName[spriteName] = offsetPx;
      if (TryExtractNumericLabel(spriteName, out var numericLabel)) {
        if (!offsetsByNumericLabel.ContainsKey(numericLabel)) {
          offsetsByNumericLabel[numericLabel] = offsetPx;
        }
      }
    }

    public bool TryGet(string spriteName, out Vector2 offsetPx) {
      offsetPx = Vector2.zero;
      var normalizedSpriteName = spriteName ?? "";
      if (string.IsNullOrWhiteSpace(normalizedSpriteName)) return false;
      if (exactOffsetsBySpriteName.TryGetValue(normalizedSpriteName, out offsetPx)) return true;

      if (!SpriteSliceAddressUtility.CanUseNumericLabelFallback(normalizedSpriteName)) {
        return false;
      }

      if (TryExtractNumericLabel(normalizedSpriteName, out var queryNumeric)) {
        if (offsetsByNumericLabel.TryGetValue(queryNumeric, out offsetPx)) {
          return true;
        }
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
  static readonly Dictionary<string, bool> warmupOptionalOffsetSupportByAtlasPath = new(StringComparer.OrdinalIgnoreCase);
  static readonly Queue<string> pendingOverlayWarmupLoadQueue = new();
  static readonly HashSet<string> pendingOverlayWarmupLoadSet = new(StringComparer.OrdinalIgnoreCase);
  static readonly Queue<string> pendingWarmGateRuntimeLoadQueue = new();
  static readonly HashSet<string> pendingWarmGateRuntimeLoadSet = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> deferredEditorMetadataLoadLogs = new(StringComparer.OrdinalIgnoreCase);
  static readonly List<string> reloadInvalidationAtlasPathScratch = new();
  static int warmupOptionalOffsetSupportRegistryVersion = -1;
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
    warmupOptionalOffsetSupportByAtlasPath.Clear();
    pendingOverlayWarmupLoadQueue.Clear();
    pendingOverlayWarmupLoadSet.Clear();
    pendingWarmGateRuntimeLoadQueue.Clear();
    pendingWarmGateRuntimeLoadSet.Clear();
    deferredEditorMetadataLoadLogs.Clear();
    reloadInvalidationAtlasPathScratch.Clear();
    warmupOptionalOffsetSupportRegistryVersion = -1;
    runtimeLoadPumpFrame = -1;
  }

  public static void InvalidateAtlas(string atlasAssetPath) {
    var normalizedAtlasPath = NormalizeAtlasPath(atlasAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedAtlasPath)) return;

    loadedAtlasOffsets.Remove(normalizedAtlasPath);
    missingAtlasOffsets.Remove(normalizedAtlasPath);
    warmupOptionalOffsetSupportByAtlasPath.Remove(normalizedAtlasPath);
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
      var atlasAssetPath = pendingWarmGateRuntimeLoadQueue.Dequeue();
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
    // The slice address owns identity. Reading Sprite.name marshals a new string.
    if (!TryGetExactOffset(sliceAddress, out var offsetPx, onReady)) return false;

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

#if UNITY_EDITOR
  static bool TryLoadEditorOffsets(string atlasAssetPath) {
    if (loadedAtlasOffsets.ContainsKey(atlasAssetPath)) return true;
    if (ShouldSkipOptionalOffsetAtlas(atlasAssetPath, "editor_load")) return false;
    if (missingAtlasOffsets.Contains(atlasAssetPath)) return false;

    var metadataAssetPath = ResolveEditorMetadataAssetPath(atlasAssetPath);
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
      RuntimeLog.Log(
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
      var atlasAssetPath = pendingOverlayWarmupLoadQueue.Dequeue();
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
    StartResolvedOptionalOffsetRuntimeLoad(atlasAssetPath, metadataAssetPath);
#else
    var contentVersion = ActiveContentRegistryRuntime.ReloadVersion;
    var locationHandle = Addressables.LoadResourceLocationsAsync(metadataAssetPath, typeof(TextAsset));
    pendingLocationChecks[atlasAssetPath] = locationHandle;
    locationHandle.Completed += operation => CompleteRuntimeLocationCheck(
      atlasAssetPath,
      metadataAssetPath,
      contentVersion,
      operation
    );
    return;
#endif
  }

  static void StartResolvedOptionalOffsetRuntimeLoad(string atlasAssetPath, string metadataAssetPath) {
    if (string.IsNullOrWhiteSpace(atlasAssetPath) || string.IsNullOrWhiteSpace(metadataAssetPath)) return;
#if UNITY_EDITOR
    TryLoadEditorOffsets(atlasAssetPath);
    NotifyPendingCallbacks(atlasAssetPath);
    return;
#else
    var loadStartedAt = ShouldMeasureRuntimeLoadStartCosts() ? Time.realtimeSinceStartup : 0f;
    AsyncOperationHandle<TextAsset> loadHandle;
    try {
      loadHandle = Addressables.LoadAssetAsync<TextAsset>(metadataAssetPath);
    }
    catch (Exception ex) {
      missingAtlasOffsets.Add(atlasAssetPath);
      if (ShouldLogVerboseOverlayMetadataDebug()) {
        RuntimeLog.Log(
          "[TrimmedSpriteOffsetResolver][MetadataMissing]" +
          " atlas='" + atlasAssetPath + "'" +
          " metadata='" + metadataAssetPath + "'" +
          " source=runtime_load" +
          " reason='" + ex.GetType().Name + "'");
      }
      return;
    }
    MaybeLogSlowRuntimeLoadStart(atlasAssetPath, metadataAssetPath, loadStartedAt);
    var contentVersion = ActiveContentRegistryRuntime.ReloadVersion;
    pendingLoads[atlasAssetPath] = loadHandle;
    loadHandle.Completed += operation => CompleteRuntimeLoad(atlasAssetPath, contentVersion, operation);
#endif
  }

  static void CompleteRuntimeLocationCheck(
    string atlasAssetPath,
    string metadataAssetPath,
    int contentVersion,
    AsyncOperationHandle<IList<IResourceLocation>> operation
  ) {
    if (contentVersion != ActiveContentRegistryRuntime.ReloadVersion) {
      if (operation.IsValid()) {
        Addressables.Release(operation);
      }
      return;
    }
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

  static void CompleteRuntimeLoad(
    string atlasAssetPath,
    int contentVersion,
    AsyncOperationHandle<TextAsset> operation
  ) {
    if (contentVersion != ActiveContentRegistryRuntime.ReloadVersion) {
      if (operation.IsValid()) {
        Addressables.Release(operation);
      }
      return;
    }
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
    RefreshWarmupOptionalOffsetSupportCacheVersion();
    var normalizedAtlasPath = NormalizeAtlasPath(atlasAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedAtlasPath)) return false;
    if (warmupOptionalOffsetSupportByAtlasPath.TryGetValue(normalizedAtlasPath, out var cachedSupport)) {
      return cachedSupport;
    }

    var supportsMetadata = true;
    var extension = Path.GetExtension(normalizedAtlasPath);
    if (string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)) {
      supportsMetadata = false;
    }
    if (string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)) {
      supportsMetadata = false;
    }
    if (supportsMetadata && !SupportsOptionalOffsetMetadata(normalizedAtlasPath)) {
      supportsMetadata = false;
    }
#if UNITY_EDITOR
    if (supportsMetadata) {
      var metadataAssetPath = BuildOptionalOffsetMetadataAssetPath(normalizedAtlasPath);
      supportsMetadata = OptionalOffsetMetadataAssetExistsInEditor(metadataAssetPath);
    }
#endif
    warmupOptionalOffsetSupportByAtlasPath[normalizedAtlasPath] = supportsMetadata;
    return supportsMetadata;
  }

  static void RefreshWarmupOptionalOffsetSupportCacheVersion() {
    var registryVersion = ActiveContentRegistryRuntime.ReloadVersion;
    if (warmupOptionalOffsetSupportRegistryVersion == registryVersion) return;

    reloadInvalidationAtlasPathScratch.Clear();
    CollectReloadInvalidationAtlasPaths(loadedAtlasOffsets.Keys);
    CollectReloadInvalidationAtlasPaths(missingAtlasOffsets);
    CollectReloadInvalidationAtlasPaths(pendingLoads.Keys);
    CollectReloadInvalidationAtlasPaths(pendingLocationChecks.Keys);
    for (var i = 0; i < reloadInvalidationAtlasPathScratch.Count; i++) {
      InvalidateAtlas(reloadInvalidationAtlasPathScratch[i]);
    }
    reloadInvalidationAtlasPathScratch.Clear();
    pendingCallbacks.Clear();
    warmupEligibleAtlasPaths.Clear();
    pendingOverlayWarmupLoadQueue.Clear();
    pendingOverlayWarmupLoadSet.Clear();
    pendingWarmGateRuntimeLoadQueue.Clear();
    pendingWarmGateRuntimeLoadSet.Clear();
    deferredEditorMetadataLoadLogs.Clear();
    warmupOptionalOffsetSupportByAtlasPath.Clear();
    warmupOptionalOffsetSupportRegistryVersion = registryVersion;
  }

  static void CollectReloadInvalidationAtlasPaths(IEnumerable<string> atlasPaths) {
    if (atlasPaths == null) return;
    foreach (var atlasPath in atlasPaths) {
      if (string.IsNullOrWhiteSpace(atlasPath)) continue;
      if (reloadInvalidationAtlasPathScratch.Contains(atlasPath)) continue;
      reloadInvalidationAtlasPathScratch.Add(atlasPath);
    }
  }

  static bool SupportsOptionalOffsetMetadata(string atlasAssetPath) {
    return GeneratedAtlasBuildSurrogateUtility.CanAtlasPathUseMetadata(atlasAssetPath);
  }

  static bool TryParseAtlasOffsets(string jsonText, out AtlasOffsets atlasOffsets) {
    atlasOffsets = null;
    if (string.IsNullOrWhiteSpace(jsonText)) return false;

    // Direct linear scanner to avoid structural C# GC allocations.
    // The schema is: "name": "spriteName", "offsetFromCellCenterPx": { "x": val, "y": val }
    atlasOffsets = new AtlasOffsets();
    var index = 0;
    var len = jsonText.Length;

    while (index < len) {
      // Find "name"
      var nameKeyIdx = jsonText.IndexOf("\"name\"", index, StringComparison.Ordinal);
      if (nameKeyIdx == -1) break;

      // Find the colon after "name"
      var colonIdx = jsonText.IndexOf(':', nameKeyIdx + 6);
      if (colonIdx == -1) break;

      // Find opening quote for the string value
      var openQuote = jsonText.IndexOf('\"', colonIdx + 1);
      if (openQuote == -1) break;

      // Find closing quote
      var closeQuote = jsonText.IndexOf('\"', openQuote + 1);
      if (closeQuote == -1) break;

      var spriteName = jsonText.Substring(openQuote + 1, closeQuote - openQuote - 1);

      // Now find "offsetFromCellCenterPx"
      var offsetKeyIdx = jsonText.IndexOf("\"offsetFromCellCenterPx\"", closeQuote + 1, StringComparison.Ordinal);
      if (offsetKeyIdx == -1) break;

      // Find "x" and "y" values after offsetKeyIdx
      var xKeyIdx = jsonText.IndexOf("\"x\"", offsetKeyIdx, StringComparison.Ordinal);
      if (xKeyIdx == -1) break;
      var xColon = jsonText.IndexOf(':', xKeyIdx + 3);
      if (xColon == -1) break;

      // Scan the float value for x
      var xStart = xColon + 1;
      while (xStart < len && char.IsWhiteSpace(jsonText[xStart])) {
        xStart++;
      }
      var xEnd = xStart;
      while (xEnd < len && (char.IsDigit(jsonText[xEnd]) || jsonText[xEnd] == '.' || jsonText[xEnd] == '-' || jsonText[xEnd] == '+' || jsonText[xEnd] == 'e' || jsonText[xEnd] == 'E')) {
        xEnd++;
      }
      var xVal = 0f;
      TryParseFloat(jsonText, xStart, xEnd - 1, out xVal);

      var yKeyIdx = jsonText.IndexOf("\"y\"", xEnd, StringComparison.Ordinal);
      if (yKeyIdx == -1) break;
      var yColon = jsonText.IndexOf(':', yKeyIdx + 3);
      if (yColon == -1) break;

      var yStart = yColon + 1;
      while (yStart < len && char.IsWhiteSpace(jsonText[yStart])) {
        yStart++;
      }
      var yEnd = yStart;
      while (yEnd < len && (char.IsDigit(jsonText[yEnd]) || jsonText[yEnd] == '.' || jsonText[yEnd] == '-' || jsonText[yEnd] == '+' || jsonText[yEnd] == 'e' || jsonText[yEnd] == 'E')) {
        yEnd++;
      }
      var yVal = 0f;
      TryParseFloat(jsonText, yStart, yEnd - 1, out yVal);

      atlasOffsets.Set(spriteName, new Vector2(xVal, yVal));

      // Advance search index
      index = yEnd;
    }

    return true;
  }

  static string BuildOptionalOffsetMetadataAssetPath(string atlasAssetPath) {
    return GeneratedAtlasBuildSurrogateUtility.BuildMetadataAssetPath(atlasAssetPath);
  }

#if UNITY_EDITOR
  static string ResolveEditorMetadataAssetPath(string atlasAssetPath) {
    var normalizedAtlasAssetPath = NormalizeAtlasPath(atlasAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedAtlasAssetPath)) {
      return "";
    }

    if (ContentPackCatalogLoader.TryResolveSourceAssetPath(normalizedAtlasAssetPath, out var sourceAtlasAssetPath)) {
      return BuildOptionalOffsetMetadataAssetPath(sourceAtlasAssetPath);
    }

    return BuildOptionalOffsetMetadataAssetPath(normalizedAtlasAssetPath);
  }
#endif

  static string NormalizeAtlasPath(string atlasAssetPath) {
    if (string.IsNullOrWhiteSpace(atlasAssetPath)) return "";

    var len = atlasAssetPath.Length;
    if (len == 0) return "";

    var needsNormalize = false;
    if (char.IsWhiteSpace(atlasAssetPath[0]) || char.IsWhiteSpace(atlasAssetPath[len - 1])) {
      needsNormalize = true;
    } else {
      for (var i = 0; i < len; i++) {
        if (atlasAssetPath[i] == '\\') {
          needsNormalize = true;
          break;
        }
      }
    }

    if (!needsNormalize) return atlasAssetPath;

    return atlasAssetPath.Trim().Replace("\\", "/");
  }

#if UNITY_EDITOR
  static bool OptionalOffsetMetadataAssetExistsInEditor(string metadataAssetPath) {
    var normalizedMetadataAssetPath = NormalizeAtlasPath(metadataAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedMetadataAssetPath)) return false;
    if (ContentPackCatalogLoader.TryResolveSourceAssetPath(
      Path.ChangeExtension(normalizedMetadataAssetPath, ".png"),
      out var sourceAtlasAssetPath)) {
      normalizedMetadataAssetPath = BuildOptionalOffsetMetadataAssetPath(sourceAtlasAssetPath);
    }
    if (string.IsNullOrWhiteSpace(normalizedMetadataAssetPath)) return false;
    if (!string.IsNullOrWhiteSpace(AssetDatabase.AssetPathToGUID(normalizedMetadataAssetPath))) return true;
    return File.Exists(Path.GetFullPath(normalizedMetadataAssetPath));
  }
#endif

  static bool TryExtractNumericLabel(string value, out string numericLabel) {
    numericLabel = null;
    if (string.IsNullOrWhiteSpace(value)) return false;

    var start = 0;
    var end = value.Length - 1;
    while (start <= end && char.IsWhiteSpace(value[start])) start++;
    while (end >= start && char.IsWhiteSpace(value[end])) end--;
    if (start > end) return false;

    if (IsNumericToken(value, start, end, out var val)) {
      numericLabel = val;
      return true;
    }

    var underscoreIndex = -1;
    for (var i = end; i >= start; i--) {
      if (value[i] == '_') {
        underscoreIndex = i;
        break;
      }
    }

    if (underscoreIndex < 0 || underscoreIndex >= end) return false;

    if (IsNumericToken(value, underscoreIndex + 1, end, out val)) {
      numericLabel = val;
      return true;
    }

    return false;
  }

  static bool IsNumericToken(string value, int start, int end, out string parsedStr) {
    parsedStr = null;
    if (start > end) return false;

    var accumulator = 0;
    for (var i = start; i <= end; i++) {
      var c = value[i];
      if (c < '0' || c > '9') return false;
      if (accumulator > 214748364 || (accumulator == 214748364 && (c - '0') > 7)) {
        return false;
      }
      accumulator = accumulator * 10 + (c - '0');
    }

    if (accumulator < 0) return false;
    parsedStr = accumulator.ToString(System.Globalization.CultureInfo.InvariantCulture);
    return true;
  }

  static bool TryParseFloat(string text, int start, int end, out float result) {
    result = 0f;
    if (start > end) return false;

    var sign = 1f;
    var i = start;
    if (text[i] == '-') {
      sign = -1f;
      i++;
    } else if (text[i] == '+') {
      i++;
    }

    double integerPart = 0.0;
    double fractionalPart = 0.0;
    var divisor = 1.0;
    var inFraction = false;
    var hasExponent = false;

    while (i <= end) {
      var c = text[i];
      if (c >= '0' && c <= '9') {
        if (inFraction) {
          fractionalPart = fractionalPart * 10.0 + (c - '0');
          divisor *= 10.0;
        } else {
          integerPart = integerPart * 10.0 + (c - '0');
        }
      } else if (c == '.') {
        if (inFraction) return false;
        inFraction = true;
      } else if (c == 'e' || c == 'E') {
        hasExponent = true;
        break;
      } else {
        break;
      }
      i++;
    }

    var value = integerPart + (fractionalPart / divisor);
    value *= sign;

    if (hasExponent) {
      i++;
      if (i <= end) {
        var expSign = 1;
        if (text[i] == '-') {
          expSign = -1;
          i++;
        } else if (text[i] == '+') {
          i++;
        }
        var exponent = 0;
        while (i <= end) {
          var c = text[i];
          if (c >= '0' && c <= '9') {
            exponent = exponent * 10 + (c - '0');
          } else {
            break;
          }
          i++;
        }
        value *= Math.Pow(10.0, exponent * expSign);
      }
    }

    result = (float)value;
    return true;
  }

  static bool ShouldSkipOptionalOffsetAtlas(string atlasAssetPath, string source) {
    var normalizedAtlasPath = NormalizeAtlasPath(atlasAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedAtlasPath)) return true;
    if (SupportsOptionalOffsetMetadata(normalizedAtlasPath)) return false;

    var firstSkip = missingAtlasOffsets.Add(normalizedAtlasPath);
    if (firstSkip && Application.isPlaying && (Application.isEditor || Debug.isDebugBuild)) {
      RuntimeLog.Log(
        "[TrimmedSpriteOffsetResolver][MetadataSkip]" +
        " atlas='" + normalizedAtlasPath + "'" +
        " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
        " excluded_folders='" + GeneratedAtlasBuildSurrogateUtility.MetadataExcludedFolderSummary + "'"
      );
    }

    return true;
  }
}
