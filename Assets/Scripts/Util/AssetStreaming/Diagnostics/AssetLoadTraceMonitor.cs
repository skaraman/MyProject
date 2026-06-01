using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static class AssetLoadTraceMonitor {
  const int MaxLinesPerFile = 50000;
  const float FlushIntervalSeconds = 1f;
  const int FlushLineThreshold = 32;
  const int MaxBufferedChars = 64 * 1024;

  sealed class TrackedAsset {
    public ulong instanceId;
    public string assetType;
    public string objectName;
    public string assetPath;
    public string scenePath;
    public long firstSeenUtcTicks;
    public float firstSeenRealtime;
    public int firstSeenFrame;
    public long currentBytes;
    public long maxBytes;
    public int lastSeenSweepId;
  }

  readonly struct ScanSpec {
    public readonly Type assetType;
    public readonly string label;

    public ScanSpec(Type assetType, string label) {
      this.assetType = assetType;
      this.label = label ?? "";
    }
  }

  readonly struct MemorySnapshot {
    public readonly long monoUsedBytes;
    public readonly long monoHeapBytes;
    public readonly long gcTotalBytes;
    public readonly int gcGen0Count;
    public readonly int gcGen1Count;
    public readonly int gcGen2Count;
    public readonly long totalAllocatedBytes;
    public readonly long totalReservedBytes;
    public readonly long totalUnusedReservedBytes;

    public MemorySnapshot(
      long monoUsedBytes,
      long monoHeapBytes,
      long gcTotalBytes,
      int gcGen0Count,
      int gcGen1Count,
      int gcGen2Count,
      long totalAllocatedBytes,
      long totalReservedBytes,
      long totalUnusedReservedBytes
    ) {
      this.monoUsedBytes = Math.Max(monoUsedBytes, 0L);
      this.monoHeapBytes = Math.Max(monoHeapBytes, 0L);
      this.gcTotalBytes = Math.Max(gcTotalBytes, 0L);
      this.gcGen0Count = Math.Max(gcGen0Count, 0);
      this.gcGen1Count = Math.Max(gcGen1Count, 0);
      this.gcGen2Count = Math.Max(gcGen2Count, 0);
      this.totalAllocatedBytes = Math.Max(totalAllocatedBytes, 0L);
      this.totalReservedBytes = Math.Max(totalReservedBytes, 0L);
      this.totalUnusedReservedBytes = Math.Max(totalUnusedReservedBytes, 0L);
    }
  }

  static readonly ScanSpec[] scanSpecs = {
    new(typeof(Texture), "Texture"),
    new(typeof(Material), "Material"),
    new(typeof(Mesh), "Mesh"),
    new(typeof(AudioClip), "AudioClip"),
    new(typeof(TextAsset), "TextAsset"),
    new(typeof(Font), "Font"),
    new(typeof(Shader), "Shader"),
    new(typeof(AnimationClip), "AnimationClip"),
    new(typeof(RuntimeAnimatorController), "RuntimeAnimatorController")
  };

  static readonly Dictionary<ulong, TrackedAsset> trackedAssets = new();
  static readonly List<ulong> staleAssetIds = new(64);
  static readonly StringBuilder bufferBuilder = new(MaxBufferedChars);
  static readonly StringBuilder rowBuilder = new(2048);
  static readonly string headerLine =
    "kind,utc_iso,realtime_s,delta_ms,frame,source,stage,address,asset_type,object_name,instance_id,object_bytes,tracked_active_count,tracked_active_bytes,texture_resident_bytes,texture_queue_queued,texture_queue_inflight,texture_deferred_pending,texture_deferred_flushed,texture_deferred_total,texture_deferred_promoted,texture_session_expected,texture_session_scheduled,texture_session_completed,runtime_queue_queued,runtime_queue_inflight,runtime_queue_preparing,runtime_queue_loaded,loading_overlay_active,warm_gate_running,mono_used_bytes,mono_heap_bytes,gc_total_bytes,gc_gen0_count,gc_gen1_count,gc_gen2_count,total_alloc_bytes,total_reserved_bytes,total_unused_reserved_bytes,scene_path,asset_path,detail,error";

  static StreamWriter writer;
  static string tracePath = "";
  static string traceDirectory = "";
  static string traceFileStem = "";
  static int traceFileIndex;
  static int currentFileLineCount;
  static bool initializationFailed;
  static int pendingLineCount;
  static float nextFlushAt;
  static int scanIndex;
  static int sweepId;
  static long trackedAssetBytes;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetOnDomainReload() {
    CloseWriter();
    trackedAssets.Clear();
    staleAssetIds.Clear();
    bufferBuilder.Clear();
    rowBuilder.Clear();
    tracePath = "";
    traceDirectory = "";
    traceFileStem = "";
    traceFileIndex = 0;
    currentFileLineCount = 0;
    initializationFailed = false;
    pendingLineCount = 0;
    nextFlushAt = 0f;
    scanIndex = 0;
    sweepId = 0;
    trackedAssetBytes = 0L;
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void EnsureStartupMonitoring() {
    if (!IsEnabled) return;
    SpriteStreamingDiagnosticsRunner.EnsureInstance();
    EnsureInitialized();
  }

  static bool IsEnabled {
    get {
      if (!Application.isPlaying) return false;
      if (!Application.isEditor && !Debug.isDebugBuild) return false;
      return SpriteStreamingRuntimeSettings.EnableDiagnostics || SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs;
    }
  }

  public static string CurrentTracePath => tracePath;

  public static void RecordEvent(
    string source,
    string stage,
    string address = "",
    UnityEngine.Object asset = null,
    string assetTypeOverride = "",
    string objectNameOverride = "",
    long objectBytesOverride = long.MinValue,
    string scenePathOverride = "",
    string assetPathOverride = "",
    string detail = "",
    string error = "",
    string kind = "event"
  ) {
    if (!IsEnabled) return;
    SpriteStreamingDiagnosticsRunner.EnsureInstance();
    EnsureInitialized();
    if (writer == null) return;

    AppendRow(
      kind: kind,
      source: source,
      stage: stage,
      address: address,
      asset: asset,
      assetTypeOverride: assetTypeOverride,
      objectNameOverride: objectNameOverride,
      objectBytesOverride: objectBytesOverride,
      scenePathOverride: scenePathOverride,
      assetPathOverride: assetPathOverride,
      detail: detail,
      error: error
    );
    MaybeFlush(Time.unscaledTime);
  }

  internal static void Tick() {
    if (!IsEnabled) return;
    EnsureInitialized();
    if (writer == null) return;

    var now = Time.unscaledTime;

    if (ShouldRunDiscoveryScanThisFrame()) {
      ScanNextType();
    }
    MaybeFlush(now);
  }

  static bool ShouldRunDiscoveryScanThisFrame() {
    if (StreamingWarmOrchestrator.IsWarmGateRunning) return false;
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive) return false;
    return true;
  }

  internal static void Shutdown(string reason) {
    if (writer == null) return;
    try {
      AppendRow(
        kind: "snapshot",
        source: "AssetLoadTraceMonitor",
        stage: "shutdown",
        detail: "reason=" + NormalizeToken(reason)
      );
      Flush();
    }
    catch {
    }
    finally {
      CloseWriter();
    }
  }

  static void EnsureInitialized() {
    if (writer != null || initializationFailed) return;

    try {
      traceDirectory = Path.Combine(Application.persistentDataPath, "Diagnostics");
      Directory.CreateDirectory(traceDirectory);
      traceFileStem = "asset-load-trace-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
      traceFileIndex = 0;
      OpenNextTraceFile();
      nextFlushAt = Time.unscaledTime + FlushIntervalSeconds;

      AppendRow(
        kind: "snapshot",
        source: "AssetLoadTraceMonitor",
        stage: "start",
        detail:
          "path=" + tracePath +
          " scan_types=" + scanSpecs.Length +
          " max_lines_per_file=" + MaxLinesPerFile
      );
    }
    catch (Exception ex) {
      initializationFailed = true;
      tracePath = "";
      writer = null;
      Debug.LogWarning(
        "[AssetLoadTraceMonitor] Failed to initialize" +
        " error='" + ex.Message + "'"
      );
    }
  }

  static void OpenNextTraceFile() {
    traceFileIndex++;
    tracePath = Path.Combine(
      traceDirectory,
      traceFileStem + "-part" + traceFileIndex.ToString("0000", CultureInfo.InvariantCulture) + ".csv"
    );
    writer = new StreamWriter(tracePath, false, new UTF8Encoding(false), 8192);
    bufferBuilder.Clear();
    pendingLineCount = 0;
    currentFileLineCount = 0;
    WriteHeader();
  }

  static void RotateTraceFile() {
    var previousPath = tracePath;
    var previousLineCount = currentFileLineCount;
    CloseWriter();
    OpenNextTraceFile();
    AppendRow(
      kind: "snapshot",
      source: "AssetLoadTraceMonitor",
      stage: "rotate",
      detail:
        "previous_path=" + previousPath +
        " previous_lines=" + previousLineCount +
        " next_path=" + tracePath
    );
  }

  static void WriteHeader() {
    bufferBuilder.AppendLine(headerLine);
    currentFileLineCount++;
  }

  static void ScanNextType() {
    if (scanSpecs.Length <= 0) return;
    if (scanIndex == 0) {
      sweepId = sweepId == int.MaxValue ? 1 : sweepId + 1;
    }

    var spec = scanSpecs[scanIndex];
    ScanLoadedObjects(spec);

    scanIndex++;
    if (scanIndex < scanSpecs.Length) return;

    scanIndex = 0;
    ReleaseStaleTrackedAssets();
  }

  static void ScanLoadedObjects(ScanSpec spec) {
    if (spec.assetType == null) return;
    UnityEngine.Object[] objects;
    try {
      objects = Resources.FindObjectsOfTypeAll(spec.assetType);
    }
    catch (Exception ex) {
      AppendRow(
        kind: "event",
        source: "Discovery",
        stage: "scan_error",
        assetTypeOverride: spec.label,
        detail: "scan_type=" + spec.label,
        error: ex.Message
      );
      return;
    }

    if (objects == null || objects.Length <= 0) return;
    for (var i = 0; i < objects.Length; i++) {
      var obj = objects[i];
      if (!ShouldTrackDiscoveredAsset(obj)) continue;
      TrackDiscoveredAsset(spec, obj);
    }
  }

  static void TrackDiscoveredAsset(ScanSpec spec, UnityEngine.Object obj) {
    if (obj == null) return;

    var objectBytes = ResolveObjectBytes(obj, long.MinValue);
    var assetPath = ResolveAssetPath(obj);
    if (objectBytes <= 0 && string.IsNullOrWhiteSpace(assetPath) && !(obj is GameObject)) return;

    var instanceId = ObjectEntityId.GetRawValue(obj);
    var scenePath = ResolveScenePath(obj);
    var objectName = ResolveObjectName(obj);
    var assetType = obj.GetType().Name;

    if (!trackedAssets.TryGetValue(instanceId, out var tracked) || tracked == null) {
      tracked = new TrackedAsset {
        instanceId = instanceId,
        assetType = assetType,
        objectName = objectName,
        assetPath = assetPath,
        scenePath = scenePath,
        firstSeenUtcTicks = DateTime.UtcNow.Ticks,
        firstSeenRealtime = Time.realtimeSinceStartup,
        firstSeenFrame = Time.frameCount,
        currentBytes = Math.Max(objectBytes, 0L),
        maxBytes = Math.Max(objectBytes, 0L),
        lastSeenSweepId = sweepId
      };
      trackedAssets[instanceId] = tracked;
      trackedAssetBytes += tracked.currentBytes;

      AppendRow(
        kind: "event",
        source: "Discovery",
        stage: "discover",
        asset: obj,
        objectBytesOverride: objectBytes,
        scenePathOverride: scenePath,
        assetPathOverride: assetPath,
        detail: BuildDiscoveryDetail(spec.label, obj)
      );
      return;
    }

    tracked.lastSeenSweepId = sweepId;
    tracked.assetType = assetType;
    tracked.objectName = objectName;
    if (!string.IsNullOrWhiteSpace(scenePath)) tracked.scenePath = scenePath;
    if (!string.IsNullOrWhiteSpace(assetPath)) tracked.assetPath = assetPath;

    var clampedBytes = Math.Max(objectBytes, 0L);
    var delta = clampedBytes - tracked.currentBytes;
    tracked.currentBytes = clampedBytes;
    if (tracked.currentBytes > tracked.maxBytes) {
      tracked.maxBytes = tracked.currentBytes;
    }
    trackedAssetBytes += delta;
    if (trackedAssetBytes < 0L) trackedAssetBytes = 0L;
  }

  static void ReleaseStaleTrackedAssets() {
    staleAssetIds.Clear();
    foreach (var pair in trackedAssets) {
      var tracked = pair.Value;
      if (tracked == null) continue;
      if (tracked.lastSeenSweepId == sweepId) continue;
      staleAssetIds.Add(pair.Key);
    }

    for (var i = 0; i < staleAssetIds.Count; i++) {
      var instanceId = staleAssetIds[i];
      if (!trackedAssets.TryGetValue(instanceId, out var tracked) || tracked == null) continue;

      AppendRow(
        kind: "event",
        source: "Discovery",
        stage: "release",
        assetTypeOverride: tracked.assetType,
        objectNameOverride: tracked.objectName,
        objectBytesOverride: tracked.currentBytes,
        scenePathOverride: tracked.scenePath,
        assetPathOverride: tracked.assetPath,
        detail: BuildReleaseDetail(tracked)
      );

      trackedAssetBytes -= tracked.currentBytes;
      if (trackedAssetBytes < 0L) trackedAssetBytes = 0L;
      trackedAssets.Remove(instanceId);
    }

    staleAssetIds.Clear();
  }

  static bool ShouldTrackDiscoveredAsset(UnityEngine.Object obj) {
    if (obj == null) return false;
    if (obj is Component) return false;
#if UNITY_EDITOR
    if (obj is MonoScript) return false;
#endif
    if (obj is GameObject go && go.scene.IsValid() && go.scene.isLoaded) {
      return false;
    }

    var assetPath = ResolveAssetPath(obj);
    if (!string.IsNullOrWhiteSpace(assetPath)) {
      if (assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) {
        return false;
      }
      if (assetPath.StartsWith("Packages\\", StringComparison.OrdinalIgnoreCase)) {
        return false;
      }
      if (assetPath.StartsWith("Library/", StringComparison.OrdinalIgnoreCase)) {
        return false;
      }
      if (assetPath.StartsWith("Library\\", StringComparison.OrdinalIgnoreCase)) {
        return false;
      }
    }

    return true;
  }

  static string BuildDiscoveryDetail(string scanLabel, UnityEngine.Object obj) {
    return
      "scan_type=" + NormalizeToken(scanLabel) +
      " hide_flags=" + obj.hideFlags +
      " persistent=" + (IsPersistentAsset(obj) ? 1 : 0);
  }

  static string BuildReleaseDetail(TrackedAsset tracked) {
    if (tracked == null) return "";
    var lifetimeSeconds = Mathf.Max(Time.realtimeSinceStartup - tracked.firstSeenRealtime, 0f);
    return
      "first_seen_frame=" + tracked.firstSeenFrame +
      " lifetime_s=" + lifetimeSeconds.ToString("0.000", CultureInfo.InvariantCulture) +
      " max_bytes=" + tracked.maxBytes;
  }

  static void AppendRow(
    string kind,
    string source,
    string stage,
    string address = "",
    UnityEngine.Object asset = null,
    string assetTypeOverride = "",
    string objectNameOverride = "",
    long objectBytesOverride = long.MinValue,
    string scenePathOverride = "",
    string assetPathOverride = "",
    string detail = "",
    string error = ""
  ) {
    if (writer == null) return;
    if (currentFileLineCount >= MaxLinesPerFile) {
      RotateTraceFile();
    }

    var textureQueue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var deferred = TextureResidencyCache.GetDeferredSnapshot();
    var textureSession = TextureResidencyCache.GetSessionSnapshot();
    var runtimeQueue = RuntimeAssetCache.GetQueueSnapshot();
    var memory = CaptureMemorySnapshot();

    var assetType = ResolveAssetType(asset, assetTypeOverride);
    var objectName = ResolveObjectName(asset, objectNameOverride);
    var instanceId = asset != null ? ObjectEntityId.GetRawValue(asset) : 0UL;
    var objectBytes = ResolveObjectBytes(asset, objectBytesOverride);
    var scenePath = string.IsNullOrWhiteSpace(scenePathOverride) ? ResolveScenePath(asset) : scenePathOverride;
    if (string.IsNullOrWhiteSpace(scenePath)) {
      scenePath = GetActiveScenePath();
    }
    var assetPath = string.IsNullOrWhiteSpace(assetPathOverride) ? ResolveAssetPath(asset) : assetPathOverride;

    rowBuilder.Clear();
    AppendStringField(rowBuilder, kind, isFirst: true);
    AppendStringField(rowBuilder, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
    AppendFloatField(rowBuilder, Time.realtimeSinceStartup, "0.000");
    AppendFloatField(rowBuilder, Time.unscaledDeltaTime * 1000f, "0.000");
    AppendIntField(rowBuilder, Time.frameCount);
    AppendStringField(rowBuilder, source);
    AppendStringField(rowBuilder, stage);
    AppendStringField(rowBuilder, NormalizeToken(address));
    AppendStringField(rowBuilder, assetType);
    AppendStringField(rowBuilder, objectName);
    AppendULongField(rowBuilder, instanceId);
    AppendLongField(rowBuilder, objectBytes);
    AppendIntField(rowBuilder, trackedAssets.Count);
    AppendLongField(rowBuilder, trackedAssetBytes);
    AppendLongField(rowBuilder, TextureResidencyCache.EstimatedResidentBytes);
    AppendIntField(rowBuilder, textureQueue.queuedCount);
    AppendIntField(rowBuilder, textureQueue.inFlightCount);
    AppendIntField(rowBuilder, deferred.pendingCount);
    AppendIntField(rowBuilder, deferred.flushedThisFrame);
    AppendIntField(rowBuilder, deferred.totalDeferredCount);
    AppendIntField(rowBuilder, deferred.totalPromotedCount);
    AppendIntField(rowBuilder, textureSession.expectedTotal);
    AppendIntField(rowBuilder, textureSession.scheduledTotal);
    AppendIntField(rowBuilder, textureSession.completedTotal);
    AppendIntField(rowBuilder, runtimeQueue.queuedCount);
    AppendIntField(rowBuilder, runtimeQueue.inFlightCount);
    AppendIntField(rowBuilder, runtimeQueue.preparingCount);
    AppendIntField(rowBuilder, runtimeQueue.loadedCount);
    AppendIntField(rowBuilder, SpriteStreamingLoadingState.IsLoadingOverlayActive ? 1 : 0);
    AppendIntField(rowBuilder, StreamingWarmOrchestrator.IsWarmGateRunning ? 1 : 0);
    AppendLongField(rowBuilder, memory.monoUsedBytes);
    AppendLongField(rowBuilder, memory.monoHeapBytes);
    AppendLongField(rowBuilder, memory.gcTotalBytes);
    AppendIntField(rowBuilder, memory.gcGen0Count);
    AppendIntField(rowBuilder, memory.gcGen1Count);
    AppendIntField(rowBuilder, memory.gcGen2Count);
    AppendLongField(rowBuilder, memory.totalAllocatedBytes);
    AppendLongField(rowBuilder, memory.totalReservedBytes);
    AppendLongField(rowBuilder, memory.totalUnusedReservedBytes);
    AppendStringField(rowBuilder, scenePath);
    AppendStringField(rowBuilder, assetPath);
    AppendStringField(rowBuilder, NormalizeToken(detail));
    AppendStringField(rowBuilder, NormalizeToken(error));
    bufferBuilder.AppendLine(rowBuilder.ToString());
    currentFileLineCount++;
    pendingLineCount++;
  }

  static MemorySnapshot CaptureMemorySnapshot() {
    return new MemorySnapshot(
      monoUsedBytes: Profiler.GetMonoUsedSizeLong(),
      monoHeapBytes: Profiler.GetMonoHeapSizeLong(),
      gcTotalBytes: GC.GetTotalMemory(false),
      gcGen0Count: GC.CollectionCount(0),
      gcGen1Count: GC.CollectionCount(1),
      gcGen2Count: GC.CollectionCount(2),
      totalAllocatedBytes: Profiler.GetTotalAllocatedMemoryLong(),
      totalReservedBytes: Profiler.GetTotalReservedMemoryLong(),
      totalUnusedReservedBytes: Profiler.GetTotalUnusedReservedMemoryLong()
    );
  }

  static string ResolveAssetType(UnityEngine.Object asset, string assetTypeOverride) {
    if (!string.IsNullOrWhiteSpace(assetTypeOverride)) return assetTypeOverride.Trim();
    return asset != null ? asset.GetType().Name : "";
  }

  static string ResolveObjectName(UnityEngine.Object asset, string objectNameOverride = "") {
    if (!string.IsNullOrWhiteSpace(objectNameOverride)) return objectNameOverride.Trim();
    if (asset == null) return "";
    return string.IsNullOrWhiteSpace(asset.name) ? asset.GetType().Name : asset.name.Trim();
  }

  static long ResolveObjectBytes(UnityEngine.Object asset, long objectBytesOverride) {
    if (objectBytesOverride != long.MinValue) return Math.Max(objectBytesOverride, 0L);
    if (asset == null) return 0L;
    try {
      return Math.Max(Profiler.GetRuntimeMemorySizeLong(asset), 0L);
    }
    catch {
      return 0L;
    }
  }

  static string ResolveScenePath(UnityEngine.Object asset) {
    if (asset == null) return "";
    if (asset is GameObject go) {
      var scene = go.scene;
      return scene.IsValid() ? scene.path ?? "" : "";
    }
    return "";
  }

  static string GetActiveScenePath() {
    var scene = SceneManager.GetActiveScene();
    return scene.IsValid() ? scene.path ?? "" : "";
  }

  static string ResolveAssetPath(UnityEngine.Object asset) {
    if (asset == null) return "";
#if UNITY_EDITOR
    try {
      return AssetDatabase.GetAssetPath(asset) ?? "";
    }
    catch {
      return "";
    }
#else
    return "";
#endif
  }

  static bool IsPersistentAsset(UnityEngine.Object asset) {
    if (asset == null) return false;
#if UNITY_EDITOR
    return EditorUtility.IsPersistent(asset);
#else
    if (asset is GameObject go) return !go.scene.IsValid();
    return true;
#endif
  }

  static void MaybeFlush(float now) {
    if (writer == null) return;
    if (pendingLineCount <= 0) return;
    if (bufferBuilder.Length < MaxBufferedChars && pendingLineCount < FlushLineThreshold && now < nextFlushAt) return;
    Flush();
    nextFlushAt = now + FlushIntervalSeconds;
  }

  static void Flush() {
    if (writer == null || bufferBuilder.Length <= 0) return;
    writer.Write(bufferBuilder.ToString());
    writer.Flush();
    bufferBuilder.Clear();
    pendingLineCount = 0;
  }

  static void CloseWriter() {
    if (writer == null) return;
    try {
      if (bufferBuilder.Length > 0) {
        writer.Write(bufferBuilder.ToString());
        writer.Flush();
      }
      writer.Dispose();
    }
    catch {
    }
    finally {
      writer = null;
      bufferBuilder.Clear();
      pendingLineCount = 0;
      currentFileLineCount = 0;
    }
  }

  static string NormalizeToken(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }

  static void AppendStringField(StringBuilder builder, string value, bool isFirst = false) {
    if (!isFirst) builder.Append(',');
    if (string.IsNullOrEmpty(value)) return;

    var requiresQuotes = false;
    for (var i = 0; i < value.Length; i++) {
      var ch = value[i];
      if (ch != '"' && ch != ',' && ch != '\n' && ch != '\r') continue;
      requiresQuotes = true;
      break;
    }

    if (!requiresQuotes) {
      builder.Append(value);
      return;
    }

    builder.Append('"');
    for (var i = 0; i < value.Length; i++) {
      var ch = value[i];
      if (ch == '"') builder.Append('"');
      builder.Append(ch);
    }
    builder.Append('"');
  }

  static void AppendIntField(StringBuilder builder, int value) {
    builder.Append(',');
    builder.Append(value);
  }

  static void AppendLongField(StringBuilder builder, long value) {
    builder.Append(',');
    builder.Append(value);
  }

  static void AppendULongField(StringBuilder builder, ulong value) {
    builder.Append(',');
    builder.Append(value);
  }

  static void AppendFloatField(StringBuilder builder, float value, string format) {
    builder.Append(',');
    builder.Append(value.ToString(format, CultureInfo.InvariantCulture));
  }
}
