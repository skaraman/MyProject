using System;
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

  static readonly StringBuilder bufferBuilder = new(MaxBufferedChars);
  static readonly StringBuilder rowBuilder = new(2048);
  static readonly string headerLine =
    "kind,utc_iso,realtime_s,delta_ms,frame,source,stage,address,asset_type,object_name,instance_id,object_bytes,tracked_active_count,tracked_active_bytes,texture_resident_bytes,texture_queue_queued,texture_queue_inflight,texture_deferred_pending,texture_deferred_flushed,texture_deferred_total,texture_deferred_promoted,texture_session_expected,texture_session_scheduled,texture_session_completed,runtime_queue_queued,runtime_queue_inflight,runtime_queue_preparing,runtime_queue_loaded,loading_overlay_active,warm_gate_running,mono_used_bytes,mono_heap_bytes,gc_total_bytes,gc_gen0_count,gc_gen1_count,gc_gen2_count,total_alloc_bytes,total_reserved_bytes,total_unused_reserved_bytes,scene_path,asset_path,detail,error";
  static readonly string inventoryHeaderLine =
    "utc_iso,reason,asset_type,object_name,instance_id,object_bytes,persistent,hide_flags,scene_path,asset_path";

  static StreamWriter writer;
  static string tracePath = "";
  static string traceDirectory = "";
  static string traceFileStem = "";
  static string inventoryPath = "";
  static int traceFileIndex;
  static int currentFileLineCount;
  static bool initializationFailed;
  static bool inventoryWritten;
  static int pendingLineCount;
  static float nextFlushAt;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetOnDomainReload() {
    CloseWriter();
    bufferBuilder.Clear();
    rowBuilder.Clear();
    tracePath = "";
    traceDirectory = "";
    traceFileStem = "";
    inventoryPath = "";
    traceFileIndex = 0;
    currentFileLineCount = 0;
    initializationFailed = false;
    inventoryWritten = false;
    pendingLineCount = 0;
    nextFlushAt = 0f;
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void EnsureStartupMonitoring() {
    if (!IsEnabled) return;
    SpriteStreamingDiagnosticsRunner.EnsureInstance();
    EnsureInitialized();
  }

  public static bool IsEnabled {
    get {
      if (!Application.isPlaying) return false;
      if (!Application.isEditor && !Debug.isDebugBuild) return false;
      return SpriteStreamingRuntimeSettings.EnableDiagnostics;
    }
  }

  public static string CurrentTracePath => tracePath;
  public static string CurrentInventoryPath => inventoryPath;

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
    MaybeFlush(now);
  }

  internal static void Shutdown(string reason) {
    if (writer == null) return;
    try {
      var inventoryDetail = WriteLoadedAssetInventory(reason);
      if (!string.IsNullOrWhiteSpace(inventoryDetail)) {
        AppendRow(
          kind: "snapshot",
          source: "AssetLoadTraceMonitor",
          stage: "asset_inventory",
          detail: inventoryDetail
        );
      }

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

  static string WriteLoadedAssetInventory(string reason) {
    if (inventoryWritten) return "";
    inventoryWritten = true;

    var startedAt = Time.realtimeSinceStartup;
    var normalizedReason = NormalizeToken(reason);
    var capturedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    var directory = string.IsNullOrWhiteSpace(traceDirectory)
      ? Path.Combine(Application.persistentDataPath, "Diagnostics")
      : traceDirectory;

    try {
      Directory.CreateDirectory(directory);
      inventoryPath = Path.Combine(
        directory,
        "loaded-asset-inventory-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv"
      );

      var scannedCount = 0;
      var writtenCount = 0;
      var totalBytes = 0L;
      var objects = Resources.FindObjectsOfTypeAll<UnityEngine.Object>();
      using (var inventoryWriter = new StreamWriter(inventoryPath, false, new UTF8Encoding(false), 8192)) {
        inventoryWriter.WriteLine(inventoryHeaderLine);
        if (objects != null) {
          for (var i = 0; i < objects.Length; i++) {
            var obj = objects[i];
            scannedCount++;
            if (!ShouldWriteInventoryAsset(obj, out var objectBytes, out var assetPath)) continue;

            totalBytes += objectBytes;
            WriteInventoryRow(inventoryWriter, capturedAt, normalizedReason, obj, objectBytes, assetPath);
            writtenCount++;
          }
        }
      }

      var elapsedMs = Mathf.Max((Time.realtimeSinceStartup - startedAt) * 1000f, 0f);
      return
        "path=" + inventoryPath +
        " scanned=" + scannedCount +
        " written=" + writtenCount +
        " bytes=" + totalBytes +
        " elapsed_ms=" + elapsedMs.ToString("0.000", CultureInfo.InvariantCulture);
    }
    catch (Exception ex) {
      inventoryPath = "";
      return "inventory_error=" + NormalizeToken(ex.Message);
    }
  }

  static bool ShouldWriteInventoryAsset(UnityEngine.Object obj, out long objectBytes, out string assetPath) {
    objectBytes = 0L;
    assetPath = "";
    if (obj == null) return false;
    if (obj is Component) return false;
#if UNITY_EDITOR
    if (obj is MonoScript) return false;
#endif
    if (obj is GameObject go && go.scene.IsValid() && go.scene.isLoaded) {
      return false;
    }

    objectBytes = ResolveObjectBytes(obj, long.MinValue);
    assetPath = ResolveAssetPath(obj);
    if (objectBytes > 0L) return true;
    if (!string.IsNullOrWhiteSpace(assetPath)) return true;
    return IsPersistentAsset(obj);
  }

  static void WriteInventoryRow(
    StreamWriter inventoryWriter,
    string capturedAt,
    string reason,
    UnityEngine.Object asset,
    long objectBytes,
    string assetPath
  ) {
    if (inventoryWriter == null || asset == null) return;

    rowBuilder.Clear();
    AppendStringField(rowBuilder, capturedAt, isFirst: true);
    AppendStringField(rowBuilder, reason);
    AppendStringField(rowBuilder, ResolveAssetType(asset, ""));
    AppendStringField(rowBuilder, ResolveObjectName(asset));
    AppendULongField(rowBuilder, ObjectEntityId.GetRawValue(asset));
    AppendLongField(rowBuilder, objectBytes);
    AppendIntField(rowBuilder, IsPersistentAsset(asset) ? 1 : 0);
    AppendStringField(rowBuilder, asset.hideFlags.ToString());
    AppendStringField(rowBuilder, ResolveScenePath(asset));
    AppendStringField(rowBuilder, assetPath);
    inventoryWriter.WriteLine(rowBuilder.ToString());
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
          " discovery=shutdown_inventory" +
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
    AppendIntField(rowBuilder, 0);
    AppendLongField(rowBuilder, 0L);
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
    try {
      return EditorUtility.IsPersistent(asset);
    }
    catch {
      return false;
    }
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
