#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Profiling;


public static partial class SpriteIndexBuilder {
  static bool RunAddressablesPlayerBuildPass(
    string contextLabel,
    string passLabel,
    string progressText,
    float progress,
    bool logResult,
    out AddressablesPlayerBuildResult result
  ) {
    result = null;
    EditorUtility.DisplayProgressBar("Sprite Streaming", progressText, progress);

    var phaseSuffix = string.Equals(passLabel, "final", StringComparison.OrdinalIgnoreCase)
      ? ""
      : "_" + passLabel;
    LogAddressablesBuildMemorySnapshot(contextLabel, "before_build" + phaseSuffix);
    AddressableAssetSettings.BuildPlayerContent(out result);
    LogAddressablesBuildMemorySnapshot(contextLabel, "after_build" + phaseSuffix);

    if (result == null) {
      Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "][" + passLabel + "] Addressables build did not return a result.");
      return false;
    }
    if (!string.IsNullOrWhiteSpace(result.Error)) {
      Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "][" + passLabel + "] Addressables build failed: " + result.Error);
      return false;
    }

    if (logResult) {
      Debug.Log(
        "[SpriteIndexBuilder] [" + contextLabel + "][" + passLabel + "] Build complete. output='" + (result.OutputPath ?? "") +
        "' locations=" + result.LocationCount +
        " duration=" + result.Duration.ToString("0.00", CultureInfo.InvariantCulture) + "s"
      );
    }

    return true;
  }

  static List<TextureBuildChunk> BuildManagedTextureBuildChunks(List<AddressableAssetGroup> managedTextureGroups) {
    var chunks = new List<TextureBuildChunk>();
    if (managedTextureGroups == null || managedTextureGroups.Count == 0) return chunks;

    var groupInfos = new List<(AddressableAssetGroup group, long approxSourceBytes, int entryCount)>(managedTextureGroups.Count);
    for (var i = 0; i < managedTextureGroups.Count; i++) {
      var group = managedTextureGroups[i];
      if (group == null) continue;
      var approxSourceBytes = GetApproxTextureGroupSourceBytes(group, out var entryCount);
      groupInfos.Add((group, approxSourceBytes, entryCount));
    }

    groupInfos.Sort((left, right) => {
      var byBytes = right.approxSourceBytes.CompareTo(left.approxSourceBytes);
      if (byBytes != 0) return byBytes;
      return string.Compare(left.group?.Name, right.group?.Name, StringComparison.OrdinalIgnoreCase);
    });

    TextureBuildChunk currentChunk = null;
    for (var i = 0; i < groupInfos.Count; i++) {
      var info = groupInfos[i];
      if (currentChunk == null ||
          (currentChunk.approxSourceBytes >= BuilderConfig.ChunkedTextureBuildTargetApproxBytes && currentChunk.groups.Count > 0)) {
        currentChunk = new TextureBuildChunk();
        chunks.Add(currentChunk);
      }

      currentChunk.groups.Add(info.group);
      currentChunk.approxSourceBytes += info.approxSourceBytes;
      currentChunk.entryCount += info.entryCount;
    }

    return chunks;
  }

  static long GetApproxTextureGroupSourceBytes(AddressableAssetGroup group, out int entryCount) {
    entryCount = 0;
    if (group == null) return 0;

    var totalApproxBytes = 0L;
    foreach (var entry in group.entries) {
      if (entry == null) continue;
      entryCount++;
      var assetPath = NormalizePath(AssetDatabase.GUIDToAssetPath(entry.guid));
      if (string.IsNullOrWhiteSpace(assetPath)) {
        assetPath = NormalizePath(entry.AssetPath);
      }

      totalApproxBytes += GetApproxAssetSourceBytes(assetPath, out _, out _);
    }

    return totalApproxBytes;
  }

  static Dictionary<AddressableAssetGroup, bool> CaptureIncludeInBuildStates(AddressableAssetSettings settings) {
    var states = new Dictionary<AddressableAssetGroup, bool>();
    if (settings == null || settings.groups == null) return states;

    for (var i = 0; i < settings.groups.Count; i++) {
      var group = settings.groups[i];
      var schema = group?.GetSchema<BundledAssetGroupSchema>();
      if (schema == null) continue;
      states[group] = schema.IncludeInBuild;
    }

    return states;
  }

  static void ApplyChunkIncludeInBuildSelection(
    AddressableAssetSettings settings,
    Dictionary<AddressableAssetGroup, bool> originalStates,
    IReadOnlyCollection<AddressableAssetGroup> activeGroups
  ) {
    if (settings == null || originalStates == null) return;

    var activeGroupSet = new HashSet<AddressableAssetGroup>(activeGroups ?? Array.Empty<AddressableAssetGroup>());
    foreach (var pair in originalStates) {
      var group = pair.Key;
      var schema = group?.GetSchema<BundledAssetGroupSchema>();
      if (schema == null) continue;

      var shouldInclude = activeGroupSet.Contains(group);
      if (schema.IncludeInBuild == shouldInclude) continue;

      schema.IncludeInBuild = shouldInclude;
      EditorUtility.SetDirty(schema);
      if (group != null) {
        EditorUtility.SetDirty(group);
      }
    }

    EditorUtility.SetDirty(settings);
  }

  static void RestoreIncludeInBuildStates(AddressableAssetSettings settings, Dictionary<AddressableAssetGroup, bool> originalStates) {
    if (settings == null || originalStates == null) return;

    foreach (var pair in originalStates) {
      var group = pair.Key;
      var schema = group?.GetSchema<BundledAssetGroupSchema>();
      if (schema == null) continue;
      if (schema.IncludeInBuild == pair.Value) continue;

      schema.IncludeInBuild = pair.Value;
      EditorUtility.SetDirty(schema);
      if (group != null) {
        EditorUtility.SetDirty(group);
      }
    }

    EditorUtility.SetDirty(settings);
  }

  static string FormatByteCount(long bytes) {
    if (bytes < 1024L) return bytes + " B";
    if (bytes < 1024L * 1024L) return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
    if (bytes < 1024L * 1024L * 1024L) return (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
    return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
  }

  static List<AddressableAssetGroup> GetManagedTextureGroups(AddressableAssetSettings settings) {
    var result = new List<AddressableAssetGroup>();
    if (settings == null || settings.groups == null) return result;
    for (var i = 0; i < settings.groups.Count; i++) {
      var group = settings.groups[i];
      if (group == null) continue;
      if (group.Name.StartsWith(BuilderConfig.ManagedTextureGroupPrefix, StringComparison.OrdinalIgnoreCase)) {
        result.Add(group);
      }
    }
    return result;
  }

  static long GetApproxAssetSourceBytes(string assetPath, out string extension, out bool isDirectory) {
    extension = "";
    isDirectory = false;
    if (string.IsNullOrWhiteSpace(assetPath)) return 0;

    extension = Path.GetExtension(assetPath).ToLowerInvariant();
    if (Directory.Exists(assetPath)) {
      isDirectory = true;
      return 0;
    }

    try {
      var info = new FileInfo(assetPath);
      return info.Exists ? info.Length : 0;
    }
    catch {
      return 0;
    }
  }

  static bool ValidateAddressablesPackedBuildSpriteSliceRisk(AddressableAssetSettings settings, string contextLabel) {
    if (settings == null) return true;

    var totalEstimatedSlices = 0L;
    var riskCandidates = new List<(string assetPath, int sliceCount)>();

    for (var gi = 0; gi < settings.groups.Count; gi++) {
      var group = settings.groups[gi];
      if (group == null) continue;
      var schema = group.GetSchema<BundledAssetGroupSchema>();
      if (schema == null || !schema.IncludeInBuild) continue;

      foreach (var entry in group.entries) {
        if (entry == null) continue;
        var assetPath = NormalizePath(entry.AssetPath);
        if (string.IsNullOrWhiteSpace(assetPath)) continue;

        var sliceCount = EstimateSpriteSliceCount(assetPath);
        totalEstimatedSlices += sliceCount;
        if (sliceCount > 0) {
          riskCandidates.Add((assetPath, sliceCount));
        }
      }
    }

    if (totalEstimatedSlices <= BuilderConfig.MaxEstimatedSpriteSlicesForPackedBuild) return true;

    riskCandidates.Sort((a, b) => b.sliceCount.CompareTo(a.sliceCount));
    var sb = new StringBuilder();
    sb.Append("[SpriteIndexBuilder] [").Append(contextLabel)
      .Append("] WARNING: Estimated sprite slice count exceeds budget.")
      .Append(" estimated=").Append(totalEstimatedSlices)
      .Append(" budget=").Append(BuilderConfig.MaxEstimatedSpriteSlicesForPackedBuild);

    var shown = Math.Min(BuilderConfig.MaxLoggedSpriteSliceRiskCandidates, riskCandidates.Count);
    for (var i = 0; i < shown; i++) {
      sb.Append("\n  ").Append(riskCandidates[i].assetPath).Append(" slices~").Append(riskCandidates[i].sliceCount);
    }

    Debug.LogWarning(sb.ToString());
    return true; // warn but don't block
  }

  static int EstimateSpriteSliceCount(string assetPath) {
    if (string.IsNullOrWhiteSpace(assetPath)) return 0;
    if (!assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
        !assetPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) &&
        !assetPath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) &&
        !assetPath.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) &&
        !assetPath.EndsWith(".psd", StringComparison.OrdinalIgnoreCase)) {
      return 0;
    }

    if (cachedSpriteSliceCountsByAssetPath.TryGetValue(assetPath, out var cached)) return cached;

    var metaPath = assetPath + ".meta";
    if (!File.Exists(metaPath)) {
      cachedSpriteSliceCountsByAssetPath[assetPath] = 0;
      return 0;
    }

    var count = 0;
    foreach (var line in File.ReadLines(metaPath)) {
      var trimmed = line.Trim();
      if (trimmed.StartsWith("- serializedVersion:", StringComparison.Ordinal) ||
          trimmed.StartsWith("serializedVersion:", StringComparison.Ordinal) &&
          line.StartsWith("    - ", StringComparison.Ordinal)) {
        count++;
      }
    }

    cachedSpriteSliceCountsByAssetPath[assetPath] = count;
    return count;
  }

  static void LogAddressablesBuildPlan(AddressableAssetSettings settings, string contextLabel) {
    if (settings == null) return;

    var totalGroups = 0;
    var totalEntries = 0;
    var totalApproxBytes = 0L;
    var includedGroups = 0;

    for (var i = 0; i < settings.groups.Count; i++) {
      var group = settings.groups[i];
      if (group == null) continue;
      totalGroups++;
      var schema = group.GetSchema<BundledAssetGroupSchema>();
      if (schema == null || !schema.IncludeInBuild) continue;
      includedGroups++;
      totalEntries += group.entries.Count;
      totalApproxBytes += GetApproxTextureGroupSourceBytes(group, out _);
    }

    Debug.Log(
      "[SpriteIndexBuilder] [" + contextLabel + "] Addressables build plan:" +
      " groups=" + totalGroups +
      " includedGroups=" + includedGroups +
      " entries=" + totalEntries +
      " approxSource=" + FormatByteCount(totalApproxBytes)
    );
  }

  static void LogAddressablesBuildMemorySnapshot(string contextLabel, string phase) {
    var mono = Profiler.GetMonoHeapSizeLong();
    var monoUsed = Profiler.GetMonoUsedSizeLong();
    var total = Profiler.GetTotalAllocatedMemoryLong();
    Debug.Log(
      "[SpriteIndexBuilder] [" + contextLabel + "][mem:" + phase + "]" +
      " mono=" + FormatByteCount(mono) +
      " monoUsed=" + FormatByteCount(monoUsed) +
      " totalAlloc=" + FormatByteCount(total)
    );
  }

  static void ReleaseEditorBuildPrepMemory(string contextLabel) {
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    LogAddressablesBuildMemorySnapshot(contextLabel, "after_gc");
  }

  // Menu items moved to ContentPackPipeline.cs for unified workflow

  public static bool PrepareForPlayerBuild(bool logResult, bool failOnError) {
    if (logResult) {
      Debug.Log(
        "[SpriteIndexBuilder] [" + BuildContext.PlayerPrebuild + "] Preparing staged active packs, auditing staged content, and rebuilding the runtime index for player build. Addressables content build is handled by Unity's build pipeline."
      );
    }
    return RebuildRuntimeIndexInternal(logResult, failOnError, BuildContext.PlayerPrebuild, prepareSelectedPacks: false);
  }

  /// <summary>
  /// Builds addressables for active content packs.
  /// </summary>
  public static bool BuildActiveContentMenu(bool logResult = true, bool cleanCachesBeforeBuild = false) {
    return BuildAddressablesContentPrepared(
      contextLabel: "Active Content Menu",
      logResult: logResult,
      cleanCachesBeforeBuild: cleanCachesBeforeBuild
    );
  }

}
#endif
