using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

public static class ProfilerReportExporter {
  const string OutputFolderName = "ProfilerCaptures";
  const string LatestReportFileName = "latest.csv";

  struct FrameTiming {
    public double startTimeMs;
    public double gpuTimeMs;
    public bool hasGpuTiming;
  }

  [MenuItem("Tools/Diagnostics/Export Selected Profiler Frame for Codex")]
  static void ExportSelectedProfilerFrame() {
    var profilerWindow = EditorWindow.GetWindow<ProfilerWindow>();
    var selectedFrameIndex = Convert.ToInt32(profilerWindow.selectedFrameIndex);

    if (selectedFrameIndex < profilerWindow.firstAvailableFrameIndex ||
        selectedFrameIndex > profilerWindow.lastAvailableFrameIndex) {
      EditorUtility.DisplayDialog(
        "Profiler Export",
        "Select a captured frame in the Profiler window first.",
        "OK"
      );
      return;
    }

    ExportFrame(selectedFrameIndex);
  }

  [MenuItem("Tools/Diagnostics/Export Worst Buffered Profiler Frame for Codex")]
  static void ExportWorstBufferedProfilerFrame() {
    var firstFrameIndex = ProfilerDriver.firstFrameIndex;
    var lastFrameIndex = ProfilerDriver.lastFrameIndex;
    var worstFrameIndex = -1;
    var worstPlayerLoopTimeMs = double.MinValue;

    for (var frameIndex = firstFrameIndex; frameIndex <= lastFrameIndex; frameIndex++) {
      if (!TryGetPlayerLoopTimeMs(frameIndex, out var playerLoopTimeMs)) continue;
      if (playerLoopTimeMs <= worstPlayerLoopTimeMs) continue;

      worstPlayerLoopTimeMs = playerLoopTimeMs;
      worstFrameIndex = frameIndex;
    }

    if (worstFrameIndex < 0) {
      EditorUtility.DisplayDialog(
        "Profiler Export",
        "The Profiler has no readable PlayerLoop frames.",
        "OK"
      );
      return;
    }

    ExportFrame(worstFrameIndex);
  }

  [MenuItem("Tools/Diagnostics/Export Set of Profiler Frames for Codex")]
  static void ExportSetOfProfilerFrames() {
    var firstFrameIndex = ProfilerDriver.firstFrameIndex;
    var lastFrameIndex = ProfilerDriver.lastFrameIndex;

    if (firstFrameIndex < 0 || lastFrameIndex < firstFrameIndex) {
      EditorUtility.DisplayDialog(
        "Profiler Export",
        "The Profiler has no readable buffered frames.",
        "OK"
      );
      return;
    }

    // A whole profiler buffer can contain millions of individual samples. Keep this
    // export compact enough to analyze as a set; use the selected/worst-frame commands
    // when the aggregate points to a sample that needs object-level attribution.
    ExportFrameRange(firstFrameIndex, lastFrameIndex, mergeSamples: true);
  }

  static bool TryGetPlayerLoopTimeMs(int frameIndex, out double playerLoopTimeMs) {
    playerLoopTimeMs = 0d;
    using (var frameData = ProfilerDriver.GetHierarchyFrameDataView(
      frameIndex,
      0,
      HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
      HierarchyFrameDataView.columnTotalTime,
      false
    )) {
      if (frameData == null || !frameData.valid) return false;

      var rootItemId = frameData.GetRootItemID();
      var childItems = new List<int>();
      frameData.GetItemChildren(rootItemId, childItems);
      for (var i = 0; i < childItems.Count; i++) {
        var itemId = childItems[i];
        if (!string.Equals(frameData.GetItemName(itemId), "PlayerLoop", StringComparison.Ordinal)) continue;

        playerLoopTimeMs = frameData.GetItemColumnDataAsDouble(
          itemId,
          HierarchyFrameDataView.columnTotalTime
        );
        return true;
      }
    }

    return false;
  }

  static void ExportFrame(int frameIndex) {
    ExportFrameRange(frameIndex, frameIndex, mergeSamples: false);
  }

  static void ExportFrameRange(int startFrameIndex, int endFrameIndex, bool mergeSamples) {
    var outputPath = GetOutputPath();
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

    var exportedThreadCount = WriteFrameReport(startFrameIndex, endFrameIndex, mergeSamples, outputPath);
    if (exportedThreadCount <= 0) {
      EditorUtility.DisplayDialog(
        "Profiler Export",
        "The selected frame range has no readable CPU hierarchy data.",
        "OK"
      );
      return;
    }

    EditorGUIUtility.systemCopyBuffer = outputPath;
    EditorUtility.RevealInFinder(outputPath);

    var frameCount = endFrameIndex - startFrameIndex + 1;
    if (frameCount == 1) {
      Debug.Log(
        "[ProfilerReportExporter] Exported frame " +
        (startFrameIndex + 1) +
        " to '" +
        outputPath +
        "'."
      );
    } else {
      Debug.Log(
        "[ProfilerReportExporter] Exported " +
        frameCount +
        " frames (" +
        (startFrameIndex + 1) +
        ".." +
        (endFrameIndex + 1) +
        ") to '" +
        outputPath +
        "'."
      );
    }
  }

  static int WriteFrameReport(
    int startFrameIndex,
    int endFrameIndex,
    bool mergeSamples,
    string outputPath
  ) {
    var exportedThreadCount = 0;

    using (var writer = new StreamWriter(outputPath, false, new UTF8Encoding(false))) {
      WriteHeader(writer);

      for (var frameIndex = startFrameIndex; frameIndex <= endFrameIndex; frameIndex++) {
        TryGetFrameTiming(frameIndex, out var timing);

        for (var threadIndex = 0; ; threadIndex++) {
          using (var frameData = ProfilerDriver.GetHierarchyFrameDataView(
            frameIndex,
            threadIndex,
            mergeSamples
              ? HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName
              : HierarchyFrameDataView.ViewModes.Default,
            HierarchyFrameDataView.columnTotalTime,
            false
          )) {
            if (frameData == null || !frameData.valid) break;

            WriteThreadRows(writer, frameData, threadIndex, timing, mergeSamples);
            exportedThreadCount++;
          }
        }
      }
    }

    return exportedThreadCount;
  }

  static void WriteHeader(StreamWriter writer) {
    writer.WriteLine(
      "frame_index,frame_display_index,frame_time_ms,frame_fps,frame_start_ms," +
      "frame_gpu_ms,has_gpu_timing,thread_index,samples_merged,thread_group,thread_name," +
      "depth,path,name,object_name," +
      "total_ms,self_ms,calls,gc_alloc_bytes"
    );
  }

  static void WriteThreadRows(
    StreamWriter writer,
    HierarchyFrameDataView frameData,
    int threadIndex,
    FrameTiming timing,
    bool samplesMerged
  ) {
    var pendingItems = new Stack<int>();
    var childItems = new List<int>();
    pendingItems.Push(frameData.GetRootItemID());

    while (pendingItems.Count > 0) {
      var itemId = pendingItems.Pop();
      WriteItemRow(writer, frameData, itemId, threadIndex, timing, samplesMerged);

      childItems.Clear();
      frameData.GetItemChildren(itemId, childItems);
      for (var i = childItems.Count - 1; i >= 0; i--) {
        pendingItems.Push(childItems[i]);
      }
    }
  }

  static void WriteItemRow(
    StreamWriter writer,
    HierarchyFrameDataView frameData,
    int itemId,
    int threadIndex,
    FrameTiming timing,
    bool samplesMerged
  ) {
    WriteInteger(writer, frameData.frameIndex);
    WriteInteger(writer, frameData.frameIndex + 1);
    WriteNumber(writer, frameData.frameTimeMs);
    WriteNumber(writer, frameData.frameFps);
    WriteNumber(writer, timing.startTimeMs);
    WriteNumber(writer, timing.gpuTimeMs);
    WriteInteger(writer, timing.hasGpuTiming ? 1 : 0);
    WriteInteger(writer, threadIndex);
    WriteInteger(writer, samplesMerged ? 1 : 0);
    WriteText(writer, frameData.threadGroupName);
    WriteText(writer, frameData.threadName);
    WriteInteger(writer, frameData.GetItemDepth(itemId));
    WriteText(writer, frameData.GetItemPath(itemId));
    WriteText(writer, frameData.GetItemName(itemId));
    WriteText(writer, frameData.GetItemColumnData(itemId, HierarchyFrameDataView.columnObjectName));
    WriteNumber(writer, frameData.GetItemColumnDataAsDouble(itemId, HierarchyFrameDataView.columnTotalTime));
    WriteNumber(writer, frameData.GetItemColumnDataAsDouble(itemId, HierarchyFrameDataView.columnSelfTime));
    WriteNumber(writer, frameData.GetItemColumnDataAsDouble(itemId, HierarchyFrameDataView.columnCalls));
    WriteNumber(writer, frameData.GetItemColumnDataAsDouble(itemId, HierarchyFrameDataView.columnGcMemory), true);
  }

  static void TryGetFrameTiming(int frameIndex, out FrameTiming timing) {
    timing = default(FrameTiming);

    using (var frameData = ProfilerDriver.GetRawFrameDataView(frameIndex, 0)) {
      if (frameData == null || !frameData.valid) return;

      timing.startTimeMs = frameData.frameStartTimeMs;
      timing.gpuTimeMs = frameData.frameGpuTimeMs;
      timing.hasGpuTiming = timing.gpuTimeMs > 0d;
    }
  }

  static void WriteText(StreamWriter writer, string value) {
    writer.Write('"');
    writer.Write((value ?? "").Replace("\"", "\"\""));
    writer.Write("\",");
  }

  static void WriteInteger(StreamWriter writer, long value) {
    writer.Write(value.ToString(CultureInfo.InvariantCulture));
    writer.Write(',');
  }

  static void WriteNumber(StreamWriter writer, double value, bool endRow = false) {
    writer.Write(value.ToString("0.####", CultureInfo.InvariantCulture));
    writer.Write(endRow ? '\n' : ',');
  }

  static string GetOutputPath() {
    var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    return Path.Combine(projectRoot, OutputFolderName, LatestReportFileName);
  }
}
