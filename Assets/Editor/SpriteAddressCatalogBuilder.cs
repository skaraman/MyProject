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

public static class SpriteIndexBuilder {
  static readonly Regex spriteRefRegex = new(@"^\s*m_Sprite(?:Override)?: \{fileID:\s*([^,]+), guid:\s*([0-9a-fA-F]{32}),", RegexOptions.Compiled);
  static readonly Regex guidRegex = new(@"guid:\s*([0-9a-fA-F]{32})", RegexOptions.Compiled);
  static readonly Regex labelFrameRegex = new(@"^(.*)_(\d+)$", RegexOptions.Compiled);
  static readonly Regex serializedObjectHeaderRegex = new(@"^--- !u!(\d+) &([^\s]+)", RegexOptions.Compiled);

  static class BuildContext {
    public const string ManualRuntimeIndex = "Manual Runtime Index";
    public const string ManualAddressablesBuild = "Manual Addressables Build";
    public const string PlayerPrebuild = "Player Prebuild";
  }

  static class BuilderConfig {
    public const string SourceRootFolder = SpriteStreamingConfig.SourceRootFolder;
    public const string TextureSourceRootFolder = SpriteStreamingConfig.TextureSourceRootFolder;
    public const string RuntimeIndexFolder = SpriteStreamingConfig.RuntimeIndexFolder;
    public const string ManifestAssetPath = SpriteStreamingConfig.ManifestAssetPath;
    public const string IncludeAssetPath = SpriteStreamingConfig.IncludeAssetPath;
    public const string SettingsAssetPath = SpriteStreamingConfig.SettingsAssetPath;
    public const string TextureAddressablesGroupName = SpriteStreamingConfig.TextureAddressablesGroupName;
    public const string IndexAddressablesGroupName = SpriteStreamingConfig.IndexAddressablesGroupName;
    public const string DefaultManifestAddress = SpriteStreamingConfig.DefaultManifestAddress;
    public const string AtlasMetadataAddressablesLabel = SpriteStreamingConfig.AtlasMetadataAddressablesLabel;
    public const string GroupedAtlasBuildSurrogateRootFolder = SpriteStreamingConfig.GroupedAtlasBuildSurrogateRootFolder;
    public const string SpriteWithNormalsScriptPath = "Assets/Scripts/Util/Game/SpriteWithNormals.cs";
    public const string SyntheticTextureLabelPrefix = "ss_bundle_";
    public const string ManagedTextureGroupSeparator = "__";
    public const string ManagedTextureGroupPrefix = TextureAddressablesGroupName + ManagedTextureGroupSeparator;
    public const int SyntheticTextureLabelFolderDepth = 3;
    public const int GroupedGearSyntheticTextureLabelFolderDepth = 5;
    public const int MaxSpriteCountForEditorLocalIdSupplement = 128;
    public const long ChunkedTextureBuildTargetApproxBytes = 16L * 1024L * 1024L;
    public const long MaxEstimatedSpriteSlicesForPackedBuild = 750000;
    public const int MaxLoggedSpriteSliceRiskCandidates = 10;
  }

  static readonly Dictionary<string, int> cachedSpriteSliceCountsByAssetPath = new(StringComparer.OrdinalIgnoreCase);

  public static void ClearCachedSpriteSliceEstimates(string reason = "") {
    var clearedCount = cachedSpriteSliceCountsByAssetPath.Count;
    cachedSpriteSliceCountsByAssetPath.Clear();
    if (clearedCount <= 0) return;

    Debug.Log(
      "[SpriteIndexBuilder] Cleared cached sprite slice estimates." +
      " previous_entries=" + clearedCount +
      (string.IsNullOrWhiteSpace(reason) ? "" : " reason='" + reason + "'")
    );
  }

  public static void InvalidateCachedSpriteSliceEstimate(string assetPath) {
    var normalizedAssetPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath)) return;
    cachedSpriteSliceCountsByAssetPath.Remove(normalizedAssetPath);
  }

  readonly struct SpriteRef {
    public readonly string guid;
    public readonly long fileId;

    public SpriteRef(string guid, long fileId) {
      this.guid = guid;
      this.fileId = fileId;
    }
  }

  readonly struct ShardRow {
    public readonly string labelPrefix;
    public readonly string category;
    public readonly int frame;
    public readonly string colorAddress;
    public readonly string normalAddress;

    public ShardRow(string labelPrefix, string category, int frame, string colorAddress, string normalAddress) {
      this.labelPrefix = labelPrefix;
      this.category = category;
      this.frame = frame;
      this.colorAddress = colorAddress;
      this.normalAddress = normalAddress;
    }
  }

  readonly struct ManifestRow {
    public readonly string libraryName;
    public readonly string address;
    public readonly string assetPath;
    public readonly int rowCount;
    public readonly string contentHash;

    public ManifestRow(string libraryName, string address, string assetPath, int rowCount, string contentHash) {
      this.libraryName = libraryName;
      this.address = address;
      this.assetPath = assetPath;
      this.rowCount = rowCount;
      this.contentHash = contentHash;
    }
  }

  enum AtlasMetadataKind : byte {
    Invalid = 0,
    OffsetOnly = 1,
    Grouped = 2,
  }

  [Serializable]
  sealed class OffsetOnlyAtlasMetadataPayload {
    public List<OffsetOnlyAtlasSpriteMetadata> sprites = new();
  }

  [Serializable]
  sealed class OffsetOnlyAtlasSpriteMetadata {
    public OffsetOnlyAtlasPixelPoint offsetFromCellCenterPx;
  }

  [Serializable]
  struct OffsetOnlyAtlasPixelPoint {
    public float x;
    public float y;
  }

  sealed class BuildState {
    public readonly AddressableAssetSettings addressables;
    public readonly AddressableAssetGroup indexGroup;
    public readonly string contextLabel;
    public readonly bool logResult;
    public readonly List<string> errors = new();
    public readonly Dictionary<string, Dictionary<long, string>> addressCacheByGuid = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, Dictionary<string, string>> spriteAddressByNameCacheByGuid = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string> derivedNormalAtlasPathByColorAtlas = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string> runtimeTextureAssetPathBySourceAssetPath = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, bool> assetExistsByPath = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string> atlasMetadataAssetPathByAtlasPath = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, AtlasMetadataKind> atlasMetadataKindByPath = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, AddressableAssetGroup> textureGroupsByName = new(StringComparer.OrdinalIgnoreCase);
    public int schemaRepairs;
    public readonly HashSet<string> runtimeAmbiguityWarnings = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, int> syntheticTextureLabelCounts = new(StringComparer.OrdinalIgnoreCase);
    public int syntheticTextureLabelAssignments;
    public readonly HashSet<string> activeTextureAssetPaths = new(StringComparer.OrdinalIgnoreCase);
    public readonly HashSet<string> activeTextureGroupNames = new(StringComparer.OrdinalIgnoreCase);
    public readonly HashSet<string> activeAtlasMetadataAssetPaths = new(StringComparer.OrdinalIgnoreCase);
    public bool projectGroupedMetadataScanCompleted;
    public bool projectHasGroupedMetadataAssets;
    public int missingNormalLibraryCount;
    public int autoDerivedNormalAddressCount;
    public int missingNormalAddressCount;
    public int skippedColorRowCount;
    public int skippedColorLibraryCount;
    public readonly List<string> skippedColorLibrarySummaries = new();
    public int supplementedLocalIdAtlasCount;
    public int supplementedLocalIdCount;
    public int skippedHeavyLocalIdSupplementAtlasCount;
    public int skippedHeavyLocalIdSupplementSpriteCount;
    public int groupedAtlasSurrogateCount;
    public int groupedAtlasSurrogateCopyCount;

    public BuildState(AddressableAssetSettings addressables, AddressableAssetGroup indexGroup, string contextLabel, bool logResult) {
      this.addressables = addressables;
      this.indexGroup = indexGroup;
      this.contextLabel = contextLabel;
      this.logResult = logResult;
    }
  }

  sealed class BundleBuildDiagnostic {
    public readonly string label;
    public int entryCount;
    public int fileCount;
    public int folderEntryCount;
    public long approxSourceBytes;
    public string largestAssetPath = "";
    public long largestAssetBytes;

    public BundleBuildDiagnostic(string label) {
      this.label = label;
    }
  }

  sealed class AssetBuildDiagnostic {
    public readonly string assetPath;
    public readonly string bundleLabel;
    public readonly long approxSourceBytes;

    public AssetBuildDiagnostic(string assetPath, string bundleLabel, long approxSourceBytes) {
      this.assetPath = assetPath;
      this.bundleLabel = bundleLabel;
      this.approxSourceBytes = approxSourceBytes;
    }
  }

  sealed class SpriteSliceBuildDiagnostic {
    public readonly string assetPath;
    public readonly string bundleLabel;
    public readonly int spriteCount;
    public readonly long approxSourceBytes;

    public SpriteSliceBuildDiagnostic(string assetPath, string bundleLabel, int spriteCount, long approxSourceBytes) {
      this.assetPath = assetPath;
      this.bundleLabel = bundleLabel;
      this.spriteCount = spriteCount;
      this.approxSourceBytes = approxSourceBytes;
    }
  }

  sealed class TextureBuildChunk {
    public readonly List<AddressableAssetGroup> groups = new();
    public long approxSourceBytes;
    public int entryCount;
  }

  // Menu items moved to ContentPackPipeline.cs for unified workflow

  static bool RunFullBuildPipeline(bool logResult, bool cleanCachesBeforeBuild, bool useChunkedWarmup) {
    var pipelineLabel = cleanCachesBeforeBuild ? "Build Active Content (Clean)" : "Build Active Content";
    var stepCount = cleanCachesBeforeBuild ? 7 : 6;
    var startedAt = EditorApplication.timeSinceStartup;
    var aborted = false;
    var stepIndex = 1;

    if (logResult) {
      Debug.Log(
        "[SpriteIndexBuilder] [" + pipelineLabel + "] Deferring intermediate asset refreshes until rebuild/build steps."
      );
    }

    bool RunStep(int stepIndex, string stepName, Func<bool> action) {
      var stepLabel = "[SpriteIndexBuilder] [" + pipelineLabel + "] Step " + stepIndex + "/" + stepCount + " - " + stepName;
      if (logResult) {
        Debug.Log(stepLabel + " (start)");
      }

      try {
        EditorUtility.DisplayProgressBar("Sprite Streaming", stepName + "...", (float)(stepIndex - 1) / stepCount);
        if (!action()) {
          Debug.LogError(stepLabel + " failed. Aborting pipeline.");
          aborted = true;
          return false;
        }
      }
      catch (Exception ex) {
        Debug.LogError(stepLabel + " threw an exception. Aborting pipeline.\n" + ex.Message);
        aborted = true;
        return false;
      }

      if (logResult) {
        Debug.Log(stepLabel + " (done)");
      }
      return true;
    }

    try {
      if (cleanCachesBeforeBuild) {
        if (!RunStep(stepIndex++, "Clean build caches", () => {
          CleanAddressablesBuildCaches(logResult: logResult);
          return true;
        })) return false;
      }


      if (!RunStep(stepIndex++, "Apply unified import flow", () => {
        return SpriteStreamingHotsetConfigurator.ApplyUnifiedImportFlow(saveAndRefreshAtEnd: false, logResult: logResult);
      })) return false;

      if (!RunStep(stepIndex++, "Rebuild runtime index", () => {
        return RebuildRuntimeIndex(logResult: logResult, failOnError: false);
      })) return false;

      if (!RunStep(stepIndex++, "Apply gameplay + location hotset", () => {
        return SpriteStreamingHotsetConfigurator.ApplyPerformanceHotset(
          rebuildRuntimeIndexFirst: false,
          saveAndRefreshAtEnd: false,
          logResult: logResult
        );
      })) return false;

      if (!RunStep(stepIndex++, "Configure Addressables defaults", () => {
        var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
        if (settings == null) {
          Debug.LogError("[SpriteIndexBuilder] [" + pipelineLabel + "] Addressables settings were not found.");
          return false;
        }
        ConfigureAddressablesBuilderDefaults(settings, logResult: logResult);
        return true;
      })) return false;

      if (!RunStep(stepIndex++, "Build Addressables content", () =>
        BuildAddressablesContent(logResult: logResult, cleanCachesBeforeBuild: false, useChunkedWarmup: useChunkedWarmup)
      )) return false;
    }
    finally {
      EditorUtility.ClearProgressBar();
      if (logResult) {
        var duration = (float)(EditorApplication.timeSinceStartup - startedAt);
        var result = aborted ? "aborted" : "completed";
        Debug.Log("[SpriteIndexBuilder] [" + pipelineLabel + "] " + result + " in " + duration.ToString("0.00", CultureInfo.InvariantCulture) + "s.");
      }
    }

    return !aborted;
  }

  public static bool RebuildRuntimeIndexAndBuildAddressables(bool logResult, bool cleanCachesBeforeBuild = false, bool useChunkedWarmup = false) {
    const string contextLabel = BuildContext.ManualAddressablesBuild;
    try {
      if (logResult) {
        Debug.Log("[SpriteIndexBuilder] [" + contextLabel + "] Starting runtime index + Addressables content build.");
      }

      EditorUtility.DisplayProgressBar("Sprite Streaming", "Configuring Addressables defaults...", 0.1f);
      var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
      if (settings == null) {
        Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] Addressables settings were not found.");
        return false;
      }

      ConfigureAddressablesBuilderDefaults(settings, logResult);

      EditorUtility.DisplayProgressBar("Sprite Streaming", "Rebuilding runtime index...", 0.45f);
      if (!RebuildRuntimeIndexInternal(logResult: false, failOnError: true, contextLabel: contextLabel, prepareSelectedPacks: true)) {
        Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] Runtime index rebuild failed.");
        return false;
      }

      return BuildAddressablesContent(
        logResult: logResult,
        cleanCachesBeforeBuild: cleanCachesBeforeBuild,
        contextLabel: contextLabel,
        useChunkedWarmup: useChunkedWarmup
      );
    }
    catch (BuildFailedException ex) {
      Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] Build failed: " + ex.Message);
      return false;
    }
    catch (Exception ex) {
      Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] Build failed with exception: " + ex.Message);
      return false;
    }
    finally {
      EditorUtility.ClearProgressBar();
    }
  }

  public static bool RebuildRuntimeIndex(bool logResult, bool failOnError) {
    return RebuildRuntimeIndexInternal(logResult, failOnError, BuildContext.ManualRuntimeIndex, prepareSelectedPacks: true);
  }

  public static bool RebuildRuntimeIndexPrepared(string contextLabel, bool logResult, bool failOnError) {
    var resolvedContextLabel = string.IsNullOrWhiteSpace(contextLabel) ? BuildContext.ManualRuntimeIndex : contextLabel.Trim();
    return RebuildRuntimeIndexInternal(logResult, failOnError, resolvedContextLabel, prepareSelectedPacks: false);
  }

  public static bool BuildAddressablesContentPrepared(
    string contextLabel,
    bool logResult,
    bool cleanCachesBeforeBuild,
    bool useChunkedWarmup = false
  ) {
    var resolvedContextLabel = string.IsNullOrWhiteSpace(contextLabel) ? BuildContext.ManualAddressablesBuild : contextLabel.Trim();
    return BuildAddressablesContent(
      logResult: logResult,
      cleanCachesBeforeBuild: cleanCachesBeforeBuild,
      contextLabel: resolvedContextLabel,
      useChunkedWarmup: useChunkedWarmup
    );
  }

  static bool BuildAddressablesContent(
    bool logResult,
    bool cleanCachesBeforeBuild,
    string contextLabel = BuildContext.ManualAddressablesBuild,
    bool useChunkedWarmup = false
  ) {
    try {
      if (!EnsureEditorReadyForAddressablesBuild(contextLabel)) {
        return false;
      }

      var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
      if (settings == null) {
        Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] Addressables settings were not found.");
        return false;
      }

      ConfigureAddressablesBuilderDefaults(settings, logResult: false);
      AssetDatabase.SaveAssets();
      AssetDatabase.Refresh();
      LogAddressablesBuildMemorySnapshot(contextLabel, "after_refresh");

      if (cleanCachesBeforeBuild) {
        EditorUtility.DisplayProgressBar("Sprite Streaming", "Cleaning Addressables build cache...", 0.72f);
        CleanAddressablesBuildCaches(logResult);
        LogAddressablesBuildMemorySnapshot(contextLabel, "after_clean_caches");
      }

      LogAddressablesBuildPlan(settings, contextLabel);
      if (!ValidateAddressablesPackedBuildSpriteSliceRisk(settings, contextLabel)) {
        return false;
      }
      ReleaseEditorBuildPrepMemory(contextLabel);
      if (useChunkedWarmup && !WarmManagedTextureGroupBuildCache(settings, contextLabel, logResult)) {
        return false;
      }

      ReleaseEditorBuildPrepMemory(contextLabel + " Final");
      if (!RunAddressablesPlayerBuildPass(
            contextLabel,
            passLabel: "final",
            progressText: "Building Addressables content...",
            progress: 0.85f,
            logResult: logResult,
            out var result)) {
        return false;
      }

      return true;
    }
    catch (BuildFailedException ex) {
      LogAddressablesBuildMemorySnapshot(contextLabel, "build_failed_exception");
      Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] Build failed: " + ex.Message);
      return false;
    }
    catch (Exception ex) {
      LogAddressablesBuildMemorySnapshot(contextLabel, "build_exception");
      Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] Build failed with exception: " + ex.Message);
      return false;
    }
  }

  static bool EnsureEditorReadyForAddressablesBuild(string contextLabel) {
    if (!EditorApplication.isPlayingOrWillChangePlaymode) return true;

    if (EditorApplication.isPlaying) {
      Debug.LogWarning(
        "[SpriteIndexBuilder] [" + contextLabel + "] Addressables build was requested while the editor was in play mode. Exiting play mode."
      );
      EditorApplication.isPlaying = false;
    }

    Debug.LogError(
      "[SpriteIndexBuilder] [" + contextLabel + "] Cannot build Addressables during play mode. Wait for play mode to stop, then rerun the pipeline."
    );
    return false;
  }

  static bool WarmManagedTextureGroupBuildCache(AddressableAssetSettings settings, string contextLabel, bool logResult) {
    if (settings == null) return false;

    var managedTextureGroups = GetManagedTextureGroups(settings)
      .Where(group => group != null && group.entries.Count > 0)
      .ToList();
    if (managedTextureGroups.Count <= 1) {
      if (logResult) {
        Debug.Log("[SpriteIndexBuilder] [" + contextLabel + "][ChunkWarmup] Skipped because managed texture group count is " + managedTextureGroups.Count + ".");
      }
      return true;
    }

    var chunks = BuildManagedTextureBuildChunks(managedTextureGroups);
    if (chunks.Count <= 1) {
      if (logResult) {
        Debug.Log("[SpriteIndexBuilder] [" + contextLabel + "][ChunkWarmup] Skipped because managed texture groups collapsed into one build chunk.");
      }
      return true;
    }

    var originalStates = CaptureIncludeInBuildStates(settings);
    try {
      for (var i = 0; i < chunks.Count; i++) {
        var chunk = chunks[i];
        ApplyChunkIncludeInBuildSelection(settings, originalStates, chunk.groups);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var chunkLabel = "chunk_" + (i + 1);
        if (logResult) {
          Debug.Log(
            "[SpriteIndexBuilder] [" + contextLabel + "][ChunkWarmup] Starting " + (i + 1) + "/" + chunks.Count +
            " groups=" + chunk.groups.Count +
            " entries=" + chunk.entryCount +
            " approxSource=" + FormatByteCount(chunk.approxSourceBytes) +
            " groupNames=" + string.Join(", ", chunk.groups.Select(group => group.Name))
          );
        }

        ReleaseEditorBuildPrepMemory(contextLabel + " Warmup " + (i + 1));
        if (!RunAddressablesPlayerBuildPass(
              contextLabel,
              passLabel: chunkLabel,
              progressText: "Warming Addressables chunk " + (i + 1) + "/" + chunks.Count + "...",
              progress: 0.78f,
              logResult: logResult,
              out _)) {
          return false;
        }
      }

      return true;
    }
    finally {
      RestoreIncludeInBuildStates(settings, originalStates);
      AssetDatabase.SaveAssets();
      AssetDatabase.Refresh();
    }
  }

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

  static bool RebuildRuntimeIndexInternal(bool logResult, bool failOnError, string contextLabel, bool prepareSelectedPacks) {
    var addressableSettings = AddressableAssetSettingsDefaultObject.GetSettings(true);
    if (addressableSettings == null) {
      Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] Addressables settings were not found.");
      if (failOnError) throw new BuildFailedException("Addressables settings were not found.");
      return false;
    }

    if (prepareSelectedPacks) {
      if (!ContentPackPipeline.PrepareSelectedPacksForRuntimeIndex(contextLabel, logResult)) {
        Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] Content pack staging failed.");
        if (failOnError) throw new BuildFailedException("Content pack staging failed.");
        return false;
      }
    }
    else if (string.Equals(contextLabel, BuildContext.PlayerPrebuild, StringComparison.Ordinal)) {
      if (!ContentPackPipeline.PrepareSelectedPacksForPlayerBuild(contextLabel, logResult)) {
        Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] Player-build content pack preparation failed.");
        if (failOnError) throw new BuildFailedException("Player-build content pack preparation failed.");
        return false;
      }
    }

    EnsureFolderExists(Path.GetDirectoryName(BuilderConfig.SettingsAssetPath));
    EnsureFolderExists(BuilderConfig.RuntimeIndexFolder);

    var streamingSettings = EnsureStreamingSettingsAsset();
    var includeAsset = EnsureIncludeAsset();
    var manifestAssetPath = EnsureManifestAssetPath();

    var textureGroup = EnsureAddressableGroup(addressableSettings, BuilderConfig.TextureAddressablesGroupName, contextLabel, logResult, out var textureSchemaRepairs);
    var indexGroup = EnsureAddressableGroup(addressableSettings, BuilderConfig.IndexAddressablesGroupName, contextLabel, logResult, out var indexSchemaRepairs);
    var state = new BuildState(addressableSettings, indexGroup, contextLabel, logResult);
    state.schemaRepairs = textureSchemaRepairs + indexSchemaRepairs;
    state.textureGroupsByName[textureGroup.Name] = textureGroup;
    if (!ValidateAddressableGroupsPreflight(state, contextLabel, failOnError)) {
      LogRuntimeIndexSummary(contextLabel, false, libraryNameCount: 0, shardCount: 0, schemaRepairs: state.schemaRepairs, errorCount: state.errors.Count);
      return false;
    }

    var librariesByKey = DiscoverLibraryPaths();
    ReportDuplicateShortNameAmbiguities(librariesByKey);
    var guidToLibraryName = DiscoverGuidToLibraryName(librariesByKey);
    var requestedLibraryReferences = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    var requestedLibraryNames = CollectRequestedLibraryNames(librariesByKey, guidToLibraryName, includeAsset, requestedLibraryReferences);

    var shardAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var manifestEntries = new List<ManifestRow>();
    var builtCanonicalLibraryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    var orderedLibraryNames = requestedLibraryNames.ToList();
    orderedLibraryNames.Sort(StringComparer.Ordinal);

    Debug.Log($"[SpriteIndexBuilder] RebuildRuntimeIndexInternal: Processing {orderedLibraryNames.Count} requested library names.");

    for (var i = 0; i < orderedLibraryNames.Count; i++) {
      var requestedLibraryName = orderedLibraryNames[i];
      var libraryName = ResolveCanonicalLibraryName(requestedLibraryName, librariesByKey, state.runtimeAmbiguityWarnings, contextLabel);
      if (string.IsNullOrWhiteSpace(libraryName)) {
        var error =
          "Missing color library for requested libraryName '" + requestedLibraryName + "'." +
          BuildRequestedLibraryReferenceSuffix(requestedLibraryName, requestedLibraryReferences);
        Debug.LogError($"[SpriteIndexBuilder] Failed to resolve canonical library name for '{requestedLibraryName}'.");
        state.errors.Add(error);
        continue;
      }

      if (!builtCanonicalLibraryNames.Add(libraryName)) {
        continue;
      }

      if (!librariesByKey.TryGetValue(libraryName, out var colorLibraryPath)) {
        Debug.LogError($"[SpriteIndexBuilder] Library '{libraryName}' not found in librariesByKey. Expected path.");
        state.errors.Add(
          "Missing color library for libraryName '" + libraryName + "' (requested '" + requestedLibraryName + "')." +
          BuildRequestedLibraryReferenceSuffix(requestedLibraryName, requestedLibraryReferences));
        continue;
      }

      var colorRows = ParseLibraryRows(colorLibraryPath, state.errors);
      var normalLibraryName = libraryName + "N";
      var hasNormalLibrary = librariesByKey.TryGetValue(normalLibraryName, out var normalLibraryPath);
      if (!hasNormalLibrary) {
        state.missingNormalLibraryCount++;
      }

      var normalRows = hasNormalLibrary
        ? ParseLibraryRows(normalLibraryPath, state.errors)
        : new Dictionary<string, SpriteRef>(StringComparer.Ordinal);
      if (colorRows.Count == 0) {
        state.skippedColorLibraryCount++;
        Debug.LogWarning(
          "[SpriteIndexBuilder] [" + contextLabel + "] Skipped sprite library because it produced zero color rows." +
          " libraryName='" + libraryName + "'" +
          " path='" + colorLibraryPath + "'" +
          " requested='" + requestedLibraryName + "'");
        continue;
      }

      var shardRows = new List<ShardRow>(colorRows.Count);
      var skippedColorRowsForLibrary = 0;
      var failedResolveCount = 0;
      var failedValidateCount = 0;
      string sampleResolveContext = "";
      string sampleValidateFailure = "";
      string sampleValidateContext = "";
      foreach (var pair in colorRows) {
        var separator = pair.Key.IndexOf('\u001f');
        if (separator <= 0 || separator >= pair.Key.Length - 1) {
          state.errors.Add("Invalid row key '" + pair.Key + "' in '" + colorLibraryPath + "'.");
          continue;
        }

        var category = pair.Key.Substring(0, separator);
        var label = pair.Key.Substring(separator + 1);
        ParseLabel(label, out var labelPrefix, out var frame);

        var colorContext = libraryName + "/" + category + ":" + label + " (color)";
        var colorAddress = ResolveSpriteAddress(state, pair.Value, colorContext, recordError: false);
        if (string.IsNullOrWhiteSpace(colorAddress)) {
          skippedColorRowsForLibrary++;
          failedResolveCount++;
          if (string.IsNullOrWhiteSpace(sampleResolveContext)) {
            sampleResolveContext = colorContext +
              " guid='" + (pair.Value.guid ?? "") + "'" +
              " fileId=" + pair.Value.fileId.ToString(CultureInfo.InvariantCulture);
          }
          continue;
        }
        if (!ValidateRuntimeAtlasAddress(state, colorAddress, colorContext, out var validationFailure, recordError: false)) {
          skippedColorRowsForLibrary++;
          failedValidateCount++;
          if (string.IsNullOrWhiteSpace(sampleValidateFailure)) {
            sampleValidateFailure = validationFailure ?? "";
            sampleValidateContext = colorContext + " address='" + colorAddress + "'";
          }
          continue;
        }

        var normalAddress = "";
        var autoDerivedNormal = false;
        if (normalRows.TryGetValue(pair.Key, out var normalRef)) {
          normalAddress = ResolveSpriteAddress(state, normalRef, normalLibraryName + "/" + category + ":" + label + " (normal)", recordError: false);
          if (!string.IsNullOrWhiteSpace(normalAddress) &&
              !ValidateRuntimeAtlasAddress(state, normalAddress, normalLibraryName + "/" + category + ":" + label + " (normal)", recordError: false)) {
            normalAddress = "";
          }
        }

        if (string.IsNullOrWhiteSpace(normalAddress) &&
            TryResolveDerivedNormalAddress(state, colorAddress, out normalAddress)) {
          autoDerivedNormal = true;
        }

        if (!string.IsNullOrWhiteSpace(normalAddress) &&
            !ValidateRuntimeAtlasAddress(state, normalAddress, normalLibraryName + "/" + category + ":" + label + " (derived normal)", recordError: false)) {
          normalAddress = "";
          autoDerivedNormal = false;
        }

        if (autoDerivedNormal) {
          state.autoDerivedNormalAddressCount++;
        }
        else if (string.IsNullOrWhiteSpace(normalAddress)) {
          state.missingNormalAddressCount++;
        }

        shardRows.Add(new ShardRow(labelPrefix, category, frame, colorAddress, normalAddress));
      }

      if (skippedColorRowsForLibrary > 0) {
        state.skippedColorRowCount += skippedColorRowsForLibrary;
        TrackSkippedColorLibrarySummary(state, libraryName, requestedLibraryName, skippedColorRowsForLibrary, colorRows.Count);
      }

      if (shardRows.Count == 0) {
        state.skippedColorLibraryCount++;
        if (ShouldLogLibraryDiagnostics(libraryName, requestedLibraryName)) {
          Debug.LogError(
            "[SpriteIndexBuilder] Library produced zero shard rows." +
            " libraryName='" + libraryName + "'" +
            " requested='" + requestedLibraryName + "'" +
            " path='" + colorLibraryPath + "'" +
            " colorRows=" + colorRows.Count +
            " failedResolveCount=" + failedResolveCount +
            " failedValidateCount=" + failedValidateCount +
            (string.IsNullOrWhiteSpace(sampleResolveContext) ? "" : " sampleResolve='" + sampleResolveContext + "'") +
            (string.IsNullOrWhiteSpace(sampleValidateContext) ? "" : " sampleValidate='" + sampleValidateContext + "'") +
            (string.IsNullOrWhiteSpace(sampleValidateFailure) ? "" : " validateReason='" + sampleValidateFailure + "'")
          );
        }
        continue;
      }

      shardRows.Sort((left, right) => {
        var byCategory = string.Compare(left.category, right.category, StringComparison.Ordinal);
        if (byCategory != 0) return byCategory;
        var byLabelPrefix = string.Compare(left.labelPrefix, right.labelPrefix, StringComparison.Ordinal);
        if (byLabelPrefix != 0) return byLabelPrefix;
        return left.frame.CompareTo(right.frame);
      });

      var shardBody = BuildShardBody(shardRows);
      var shardPath = BuildShardAssetPath(libraryName);
      WriteIfChanged(shardPath, shardBody);
      shardAssetPaths.Add(NormalizePath(shardPath));

      var shardAddress = "SpriteRuntimeIndex/Shard/" + libraryName;
      EnsureAddressableEntry(addressableSettings, indexGroup, shardPath, shardAddress);

      manifestEntries.Add(new ManifestRow(
        libraryName,
        shardAddress,
        shardPath,
        shardRows.Count,
        ComputeHash(shardBody)
      ));
    }

    CleanupStaleTextureEntries(state);
    CleanupStaleShardAssets(shardAssetPaths);
    CleanupStaleIndexEntries(state, indexGroup, shardAssetPaths, manifestAssetPath, state.activeAtlasMetadataAssetPaths);

    manifestEntries.Sort((left, right) => string.Compare(left.libraryName, right.libraryName, StringComparison.Ordinal));
    if (librariesByKey.TryGetValue("UI/Fonts", out var uiFontsLibraryPath) &&
        manifestEntries.All(entry => !string.Equals(entry.libraryName, "UI/Fonts", StringComparison.OrdinalIgnoreCase))) {
      Debug.LogError(
        "[SpriteIndexBuilder] Missing manifest entry for UI/Fonts after runtime index rebuild." +
        " libraryPath='" + uiFontsLibraryPath + "'" +
        " requestedLibraryCount=" + orderedLibraryNames.Count +
        " activeTextureCount=" + state.activeTextureAssetPaths.Count
      );
    }
    WriteManifestTextAsset(manifestAssetPath, manifestEntries);

    var manifestAddress = streamingSettings == null || string.IsNullOrWhiteSpace(streamingSettings.manifestAddress)
      ? BuilderConfig.DefaultManifestAddress
      : streamingSettings.manifestAddress.Trim();
    EnsureAddressableEntry(addressableSettings, indexGroup, BuilderConfig.ManifestAssetPath, manifestAddress);

    if (streamingSettings != null) {
      EditorUtility.SetDirty(streamingSettings);
    }
    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh();

      if (state.errors.Count > 0) {
        var limitedErrors = state.errors.Take(50).ToList();
        for (var i = 0; i < limitedErrors.Count; i++) {
          Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] " + limitedErrors[i]);
        }
      if (state.errors.Count > limitedErrors.Count) {
        Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] Additional errors omitted: " + (state.errors.Count - limitedErrors.Count));
      }
      LogSkippedColorRowSummary(contextLabel, state);
      LogSyntheticTextureLabelSummary(contextLabel, state);
      LogAtlasMetadataSummary(contextLabel, state);
      LogNormalAddressSummary(contextLabel, state);
      LogLocalIdSupplementSummary(contextLabel, state);
      LogGroupedAtlasBuildSurrogateSummary(contextLabel, state);
      LogRuntimeIndexSummary(contextLabel, false, manifestEntries.Count, shardAssetPaths.Count, state.schemaRepairs, state.errors.Count);

      if (failOnError) {
        throw new BuildFailedException("Sprite runtime index generation failed with " + state.errors.Count + " errors.");
      }
      return false;
    }

    LogSkippedColorRowSummary(contextLabel, state);
    LogSyntheticTextureLabelSummary(contextLabel, state);
    LogAtlasMetadataSummary(contextLabel, state);
    LogNormalAddressSummary(contextLabel, state);
    LogLocalIdSupplementSummary(contextLabel, state);
    LogGroupedAtlasBuildSurrogateSummary(contextLabel, state);
    LogRuntimeIndexSummary(contextLabel, true, manifestEntries.Count, shardAssetPaths.Count, state.schemaRepairs, 0);

    return true;
  }

  static void CleanAddressablesBuildCaches(bool logResult) {
    var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    var cacheFolders = new[] {
      "Library/BuildCache",
      "Library/com.unity.addressables",
      "Library/com.unity.scriptablebuildpipeline"
    };

    var deletedCount = 0;
    for (var i = 0; i < cacheFolders.Length; i++) {
      var relative = cacheFolders[i];
      var fullPath = Path.GetFullPath(Path.Combine(projectRoot, relative));
      try {
        if (!Directory.Exists(fullPath)) continue;
        Directory.Delete(fullPath, true);
        deletedCount++;
      }
      catch (Exception ex) {
        Debug.LogWarning("[SpriteIndexBuilder] Failed to delete cache folder '" + relative + "': " + ex.Message);
      }
    }

    AssetDatabase.Refresh();

    if (logResult) {
      Debug.Log("[SpriteIndexBuilder] Addressables cache cleanup complete. deletedFolders=" + deletedCount);
    }
  }

  static void ConfigureAddressablesBuilderDefaults(AddressableAssetSettings settings, bool logResult) {
    if (settings == null) return;
    if (settings.DataBuilders == null || settings.DataBuilders.Count == 0) return;

    var changed = false;
    var packedModeIndex = FindDataBuilderIndex<BuildScriptPackedMode>(settings);
    if (packedModeIndex >= 0 && settings.ActivePlayerDataBuilderIndex != packedModeIndex) {
      settings.ActivePlayerDataBuilderIndex = packedModeIndex;
      changed = true;
    }

    var fastModeIndex = FindDataBuilderIndex<BuildScriptFastMode>(settings);
    if (fastModeIndex >= 0 && settings.ActivePlayModeDataBuilderIndex != fastModeIndex) {
      settings.ActivePlayModeDataBuilderIndex = fastModeIndex;
      changed = true;
    }

    if (!settings.OptimizeCatalogSize) {
      settings.OptimizeCatalogSize = true;
      changed = true;
    }

    if (settings.DisableVisibleSubAssetRepresentations) {
      settings.DisableVisibleSubAssetRepresentations = false;
      changed = true;
    }

    if (settings.BuildAddressablesWithPlayerBuild != AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer) {
      settings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
      changed = true;
    }

    if (!changed) {
      if (logResult) {
        Debug.Log("[SpriteIndexBuilder] Addressables defaults already configured (Player=Packed, Play Mode=Fast, OptimizeCatalog=True, DisableVisibleSubAssets=False, BuildAddressablesWithPlayer=False).");
      }
      return;
    }

    EditorUtility.SetDirty(settings);
    AssetDatabase.SaveAssets();
    if (logResult) {
      Debug.Log("[SpriteIndexBuilder] Addressables defaults updated (Player=Packed, Play Mode=Fast, OptimizeCatalog=True, DisableVisibleSubAssets=False, BuildAddressablesWithPlayer=False).");
    }
  }

  static int FindDataBuilderIndex<TBuilder>(AddressableAssetSettings settings) where TBuilder : ScriptableObject {
    if (settings == null || settings.DataBuilders == null) return -1;

    for (var i = 0; i < settings.DataBuilders.Count; i++) {
      if (settings.DataBuilders[i] is TBuilder) return i;
    }

    return -1;
  }

  static SpriteStreamingSettings EnsureStreamingSettingsAsset() {
    EnsureFolderExists(Path.GetDirectoryName(BuilderConfig.SettingsAssetPath));
    var asset = AssetDatabase.LoadAssetAtPath<SpriteStreamingSettings>(BuilderConfig.SettingsAssetPath);
    if (asset == null) {
      asset = ScriptableObject.CreateInstance<SpriteStreamingSettings>();
      if (asset == null) return null;
      AssetDatabase.CreateAsset(asset, BuilderConfig.SettingsAssetPath);
    }

    var settingsChanged = UpgradeLegacyStreamingSettings(asset);
    if (settingsChanged) {
      EditorUtility.SetDirty(asset);
    }
    AssetDatabase.SaveAssets();
    return asset;
  }

  static bool UpgradeLegacyStreamingSettings(SpriteStreamingSettings asset) {
    if (asset == null) return false;

    var changed = false;
    var upgradedDesktopStarts = false;
    if (asset.usePlatformAddressableStartPresets &&
        asset.maxAddressableStartsPerFrame == 8 &&
        asset.desktopMaxAddressableStartsPerFrame == 8) {
      asset.maxAddressableStartsPerFrame = 16;
      asset.desktopMaxAddressableStartsPerFrame = 16;
      changed = true;
      upgradedDesktopStarts = true;
    }

    if (asset.loadingOverlayMaxAddressableStartsPerFrame < asset.maxAddressableStartsPerFrame) {
      asset.loadingOverlayMaxAddressableStartsPerFrame = asset.maxAddressableStartsPerFrame;
      changed = true;
    }
    if (asset.desktopLoadingOverlayMaxAddressableStartsPerFrame < asset.desktopMaxAddressableStartsPerFrame) {
      asset.desktopLoadingOverlayMaxAddressableStartsPerFrame = asset.desktopMaxAddressableStartsPerFrame;
      changed = true;
    }
    if (asset.mobileLoadingOverlayMaxAddressableStartsPerFrame < asset.mobileMaxAddressableStartsPerFrame) {
      asset.mobileLoadingOverlayMaxAddressableStartsPerFrame = asset.mobileMaxAddressableStartsPerFrame;
      changed = true;
    }

    if (upgradedDesktopStarts) {
      Debug.Log(
        "[SpriteIndexBuilder] Upgraded legacy desktop addressable start budget." +
        " gameplay_starts=" + asset.desktopMaxAddressableStartsPerFrame +
        " overlay_starts=" + asset.desktopLoadingOverlayMaxAddressableStartsPerFrame
      );
    }

    return changed;
  }

  static SpriteStreamingInclude EnsureIncludeAsset() {
    var asset = AssetDatabase.LoadAssetAtPath<SpriteStreamingInclude>(BuilderConfig.IncludeAssetPath);
    if (asset != null) return asset;

    EnsureFolderExists(Path.GetDirectoryName(BuilderConfig.IncludeAssetPath));
    asset = ScriptableObject.CreateInstance<SpriteStreamingInclude>();
    if (asset == null) return null;
    AssetDatabase.CreateAsset(asset, BuilderConfig.IncludeAssetPath);
    AssetDatabase.SaveAssets();
    return asset;
  }

  static string EnsureManifestAssetPath() {
    EnsureFolderExists(Path.GetDirectoryName(BuilderConfig.ManifestAssetPath));
    return BuilderConfig.ManifestAssetPath;
  }

  static string ResolveCanonicalLibraryName(
    string requestedLibraryName,
    Dictionary<string, string> librariesByKey,
    HashSet<string> runtimeAmbiguityWarnings,
    string contextLabel
  ) {
    var normalizedRequested = SpriteAddressResolver.NormalizeNamePart(requestedLibraryName);
    if (string.IsNullOrWhiteSpace(normalizedRequested)) return "";

    if (librariesByKey.ContainsKey(normalizedRequested)) {
      return normalizedRequested;
    }

    var suffix = "/" + normalizedRequested;
    var matches = new List<string>();

    foreach (var key in librariesByKey.Keys) {
      if (IsNormalVariantLibraryName(key, librariesByKey)) continue;
      if (!key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
      matches.Add(key);
    }

    if (matches.Count == 1) return matches[0];

    if (matches.Count > 1) {
      matches.Sort(StringComparer.OrdinalIgnoreCase);
      var ambiguityWarning = BuildShortKeyAmbiguityError(normalizedRequested, matches, librariesByKey);
      if (runtimeAmbiguityWarnings != null && runtimeAmbiguityWarnings.Add(ambiguityWarning)) {
        Debug.LogWarning(
          "[SpriteIndexBuilder] [" + contextLabel + "] " + ambiguityWarning +
          " Using '" + matches[0] + "' for this build. Use canonical full-path libraryName to remove this warning."
        );
      }
      return matches[0];
    }

    if (TryResolveMovedLibraryName(normalizedRequested, librariesByKey, out var remappedLibraryName, out var remapWarning)) {
      if (runtimeAmbiguityWarnings != null &&
          !string.IsNullOrWhiteSpace(remapWarning) &&
          runtimeAmbiguityWarnings.Add(remapWarning)) {
        Debug.LogWarning("[SpriteIndexBuilder] [" + contextLabel + "] " + remapWarning);
      }
      return remappedLibraryName;
    }

    return "";
  }

  static bool TryResolveMovedLibraryName(
    string requestedLibraryName,
    Dictionary<string, string> librariesByKey,
    out string remappedLibraryName,
    out string remapWarning
  ) {
    remappedLibraryName = "";
    remapWarning = "";
    if (string.IsNullOrWhiteSpace(requestedLibraryName) || librariesByKey == null || librariesByKey.Count == 0) return false;

    var slash = requestedLibraryName.LastIndexOf('/');
    if (slash < 0 || slash >= requestedLibraryName.Length - 1) return false;

    var leafName = SpriteAddressResolver.NormalizeNamePart(requestedLibraryName.Substring(slash + 1));
    if (string.IsNullOrWhiteSpace(leafName)) return false;

    var suffix = "/" + leafName;
    var matches = new List<string>();
    foreach (var key in librariesByKey.Keys) {
      if (IsNormalVariantLibraryName(key, librariesByKey)) continue;
      if (!string.Equals(key, leafName, StringComparison.OrdinalIgnoreCase) &&
          !key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      if (!ContainsIgnoreCase(matches, key)) {
        matches.Add(key);
      }
    }

    if (matches.Count != 1) return false;

    remappedLibraryName = matches[0];
    if (!string.Equals(remappedLibraryName, requestedLibraryName, StringComparison.OrdinalIgnoreCase)) {
      remapWarning =
        "Remapped missing libraryName '" + requestedLibraryName +
        "' to canonical '" + remappedLibraryName +
        "' by unique leaf name '" + leafName + "'.";
    }
    return !string.IsNullOrWhiteSpace(remappedLibraryName);
  }

  static string BuildShortKeyAmbiguityError(string shortKey, List<string> matches, Dictionary<string, string> librariesByKey) {
    var folders = new List<string>();
    var matchDetails = new List<string>();
    for (var i = 0; i < matches.Count; i++) {
      var match = matches[i];
      if (string.IsNullOrWhiteSpace(match)) continue;

      if (librariesByKey != null && librariesByKey.TryGetValue(match, out var libraryPath) && !string.IsNullOrWhiteSpace(libraryPath)) {
        matchDetails.Add(match + " (" + NormalizePath(libraryPath) + ")");
      } else {
        matchDetails.Add(match);
      }

      var slash = match.LastIndexOf('/');
      var folder = slash > 0 ? match.Substring(0, slash) : "(root)";
      if (!ContainsIgnoreCase(folders, folder)) {
        folders.Add(folder);
      }
    }

    folders.Sort(StringComparer.OrdinalIgnoreCase);
    var folderPhrase = JoinAsEnglishList(folders);
    var matchPhrase = "'" + string.Join("', '", matchDetails) + "'";

    return "Short name '" + shortKey + "' appears in multiple places (" + folderPhrase + "). Matches " + matchPhrase + ". Use canonical full-path libraryName.";
  }

  static string JoinAsEnglishList(List<string> values) {
    if (values == null || values.Count == 0) return "(none)";
    if (values.Count == 1) return values[0];
    if (values.Count == 2) return values[0] + " and " + values[1];
    return string.Join(", ", values.Take(values.Count - 1)) + ", and " + values[values.Count - 1];
  }

  static bool ContainsIgnoreCase(List<string> values, string candidate) {
    if (values == null || string.IsNullOrWhiteSpace(candidate)) return false;
    for (var i = 0; i < values.Count; i++) {
      if (string.Equals(values[i], candidate, StringComparison.OrdinalIgnoreCase)) return true;
    }
    return false;
  }

  static void ReportDuplicateShortNameAmbiguities(Dictionary<string, string> librariesByKey) {
    if (librariesByKey == null || librariesByKey.Count == 0) return;

    var shortNameCandidates = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    var reported = new List<string>();
    foreach (var key in librariesByKey.Keys) {
      if (string.IsNullOrWhiteSpace(key)) continue;
      if (IsNormalVariantLibraryName(key, librariesByKey)) continue;

      var slash = key.LastIndexOf('/');
      if (slash < 0 || slash >= key.Length - 1) continue;

      var shortName = SpriteAddressResolver.NormalizeNamePart(key.Substring(slash + 1));
      if (string.IsNullOrWhiteSpace(shortName)) continue;

      if (!shortNameCandidates.TryGetValue(shortName, out var matches)) {
        matches = new List<string>();
        shortNameCandidates[shortName] = matches;
      }

      if (!ContainsIgnoreCase(matches, key)) {
        matches.Add(key);
      }
    }

    foreach (var pair in shortNameCandidates) {
      var shortName = pair.Key;
      var matches = pair.Value;
      if (matches == null || matches.Count <= 1) continue;

      matches.Sort(StringComparer.OrdinalIgnoreCase);
      var ambiguityWarning = BuildShortKeyAmbiguityError(shortName, matches, librariesByKey);
      if (!ContainsIgnoreCase(reported, ambiguityWarning)) {
        reported.Add(ambiguityWarning);
        Debug.LogWarning("[SpriteIndexBuilder] " + ambiguityWarning);
      }
    }
  }

  static Dictionary<string, string> DiscoverLibraryPaths() {
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var roots = ContentPackPipeline.GetSpriteLibrarySearchRoots();
    for (var rootIndex = 0; rootIndex < roots.Count; rootIndex++) {
      var root = NormalizePath(roots[rootIndex]);
      if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

      var files = Directory.GetFiles(root, "*.spriteLib", SearchOption.AllDirectories);
      Array.Sort(files, StringComparer.Ordinal);

      for (var i = 0; i < files.Length; i++) {
        var path = NormalizePath(files[i]);
        var relative = path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)
          ? path.Substring(root.Length + 1)
          : path;
        var key = RemoveExtension(relative);
        if (result.ContainsKey(key)) continue;
        result[key] = path;
      }
    }

    return result;
  }

  static Dictionary<string, string> DiscoverGuidToLibraryName(Dictionary<string, string> librariesByKey) {
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var pair in librariesByKey) {
      if (IsNormalVariantLibraryName(pair.Key, librariesByKey)) continue;
      var guid = AssetDatabase.AssetPathToGUID(pair.Value);
      if (string.IsNullOrWhiteSpace(guid)) continue;
      result[guid] = pair.Key;
    }
    return result;
  }

  static HashSet<string> CollectRequestedLibraryNames(
    Dictionary<string, string> librariesByKey,
    Dictionary<string, string> guidToLibraryName,
    SpriteStreamingInclude includeAsset,
    Dictionary<string, List<string>> requestedLibraryReferences
  ) {
    var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (librariesByKey != null) {
      foreach (var key in librariesByKey.Keys) {
        if (string.IsNullOrWhiteSpace(key)) continue;
        if (IsNormalVariantLibraryName(key, librariesByKey)) continue;
        var normalizedLibraryLibraryName = SpriteAddressResolver.NormalizeNamePart(key);
        if (!string.IsNullOrWhiteSpace(normalizedLibraryLibraryName)) {
          result.Add(normalizedLibraryLibraryName);
        }
      }
    }

    var assetsRoot = NormalizePath("Assets");

    CollectLibraryNamesFromFiles(assetsRoot, "*.unity", guidToLibraryName, result, requestedLibraryReferences);
    CollectLibraryNamesFromFiles(assetsRoot, "*.prefab", guidToLibraryName, result, requestedLibraryReferences);

    if (includeAsset != null && includeAsset.libraryNames != null) {
      for (var i = 0; i < includeAsset.libraryNames.Count; i++) {
        var normalized = SpriteAddressResolver.NormalizeNamePart(includeAsset.libraryNames[i]);
        if (!string.IsNullOrWhiteSpace(normalized)) {
          result.Add(normalized);
          AddRequestedLibraryReference(
            requestedLibraryReferences,
            normalized,
            "SpriteStreamingInclude entry in '" + NormalizePath(BuilderConfig.IncludeAssetPath) + "'");
        }
      }
    }

    return result;
  }

  static bool IsNormalVariantLibraryName(string key, Dictionary<string, string> librariesByKey) {
    if (string.IsNullOrWhiteSpace(key) || librariesByKey == null) return false;
    if (!key.EndsWith("N", StringComparison.OrdinalIgnoreCase)) return false;
    if (key.Length <= 1) return false;

    var candidateColorLibraryName = key.Substring(0, key.Length - 1);
    return librariesByKey.ContainsKey(candidateColorLibraryName);
  }

  static void CollectLibraryNamesFromFiles(
    string rootPath,
    string pattern,
    Dictionary<string, string> guidToLibraryName,
    HashSet<string> target,
    Dictionary<string, List<string>> requestedLibraryReferences
  ) {
    if (!Directory.Exists(rootPath)) return;
    var files = Directory.GetFiles(rootPath, pattern, SearchOption.AllDirectories);
    Array.Sort(files, StringComparer.Ordinal);

    for (var i = 0; i < files.Length; i++) {
      CollectLibraryNamesFromSerializedFile(NormalizePath(files[i]), guidToLibraryName, target, requestedLibraryReferences);
    }
  }

  static void CollectLibraryNamesFromSerializedFile(
    string path,
    Dictionary<string, string> guidToLibraryName,
    HashSet<string> target,
    Dictionary<string, List<string>> requestedLibraryReferences
  ) {
    if (!File.Exists(path)) return;

    var spriteWithNormalsGuid = AssetDatabase.AssetPathToGUID(BuilderConfig.SpriteWithNormalsScriptPath);
    var gameObjectNameByFileId = BuildGameObjectNameByFileId(path);
    var insideMonoBehaviour = false;
    var insideSpriteWithNormals = false;
    var currentMonoBehaviourGameObjectFileId = "";
    var pendingGameObjectFileId = "";
    var pendingLibraryName = "";
    var pendingColorKey = "";
    var pendingColorLibraryGuid = "";

    void BeginSpriteWithNormalsBlock() {
      insideSpriteWithNormals = true;
      pendingGameObjectFileId = currentMonoBehaviourGameObjectFileId;
      pendingLibraryName = "";
      pendingColorKey = "";
      pendingColorLibraryGuid = "";
    }

    void FlushPending() {
      if (!insideSpriteWithNormals) return;
      insideSpriteWithNormals = false;

      var resolved = pendingLibraryName;
      if (string.IsNullOrWhiteSpace(resolved)) resolved = pendingColorKey;
      if (string.IsNullOrWhiteSpace(resolved) &&
          !string.IsNullOrWhiteSpace(pendingColorLibraryGuid) &&
          guidToLibraryName.TryGetValue(pendingColorLibraryGuid, out var mappedLibraryName)) {
        resolved = mappedLibraryName;
      }

      var normalized = SpriteAddressResolver.NormalizeNamePart(resolved);
      if (!string.IsNullOrWhiteSpace(normalized)) {
        target.Add(normalized);
        AddRequestedLibraryReference(
          requestedLibraryReferences,
          normalized,
          BuildRequestedLibraryReference(path, pendingGameObjectFileId, gameObjectNameByFileId));
      }

      pendingGameObjectFileId = "";
      pendingLibraryName = "";
      pendingColorKey = "";
      pendingColorLibraryGuid = "";
    }

    foreach (var rawLine in File.ReadLines(path)) {
      var line = rawLine ?? "";
      if (line.StartsWith("--- !u!", StringComparison.Ordinal)) {
        FlushPending();
        insideMonoBehaviour = line.StartsWith("--- !u!114", StringComparison.Ordinal);
        currentMonoBehaviourGameObjectFileId = "";
      }

      if (!insideMonoBehaviour) continue;

      var trimmed = line.Trim();

      if (TryReadFileIdReference(trimmed, "m_GameObject", out var gameObjectFileId)) {
        currentMonoBehaviourGameObjectFileId = gameObjectFileId;
      }

      if (!insideSpriteWithNormals &&
          !string.IsNullOrWhiteSpace(spriteWithNormalsGuid) &&
          trimmed.StartsWith("m_Script:", StringComparison.Ordinal)) {
        var scriptGuidMatch = guidRegex.Match(trimmed);
        if (scriptGuidMatch.Success &&
            string.Equals(scriptGuidMatch.Groups[1].Value, spriteWithNormalsGuid, StringComparison.OrdinalIgnoreCase)) {
          BeginSpriteWithNormalsBlock();
          continue;
        }
      }

      if (!insideSpriteWithNormals &&
          trimmed.StartsWith("m_EditorClassIdentifier:", StringComparison.Ordinal) &&
          trimmed.Contains("SpriteWithNormals", StringComparison.Ordinal)) {
        BeginSpriteWithNormalsBlock();
        continue;
      }

      if (!insideSpriteWithNormals) continue;

      if (TryReadScalar(trimmed, "libraryName", out var libraryNameValue) ||
          TryReadScalar(trimmed, "LibraryName", out libraryNameValue) ||
          TryReadScalar(trimmed, "_libraryName", out libraryNameValue) ||
          TryReadScalar(trimmed, "libraryName", out libraryNameValue)) {
        pendingLibraryName = libraryNameValue;
        continue;
      }

      if (TryReadScalar(trimmed, "colorKey", out var colorKeyValue)) {
        pendingColorKey = colorKeyValue;
        continue;
      }

      if (trimmed.StartsWith("colorLibrary:", StringComparison.Ordinal)) {
        var guidMatch = guidRegex.Match(trimmed);
        if (guidMatch.Success) {
          pendingColorLibraryGuid = guidMatch.Groups[1].Value;
        }
      }
    }

    FlushPending();
  }

  static Dictionary<string, string> BuildGameObjectNameByFileId(string path) {
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    if (!File.Exists(path)) return result;

    var insideGameObject = false;
    var currentFileId = "";
    foreach (var rawLine in File.ReadLines(path)) {
      var line = rawLine ?? "";
      if (TryReadSerializedObjectHeader(line, out var classId, out var fileId)) {
        insideGameObject = classId == 1;
        currentFileId = insideGameObject ? fileId : "";
        continue;
      }

      if (!insideGameObject || string.IsNullOrWhiteSpace(currentFileId)) continue;
      var trimmed = line.Trim();
      if (!TryReadScalar(trimmed, "m_Name", out var gameObjectName)) continue;
      if (string.IsNullOrWhiteSpace(gameObjectName)) continue;

      result[currentFileId] = gameObjectName;
      insideGameObject = false;
      currentFileId = "";
    }

    return result;
  }

  static void AddRequestedLibraryReference(
    Dictionary<string, List<string>> requestedLibraryReferences,
    string requestedLibraryName,
    string reference
  ) {
    if (requestedLibraryReferences == null ||
        string.IsNullOrWhiteSpace(requestedLibraryName) ||
        string.IsNullOrWhiteSpace(reference)) {
      return;
    }

    if (!requestedLibraryReferences.TryGetValue(requestedLibraryName, out var references)) {
      references = new List<string>();
      requestedLibraryReferences[requestedLibraryName] = references;
    }

    if (!ContainsIgnoreCase(references, reference)) {
      references.Add(reference);
    }
  }

  static string BuildRequestedLibraryReference(string assetPath, string gameObjectFileId, Dictionary<string, string> gameObjectNameByFileId) {
    var normalizedPath = NormalizePath(assetPath);
    if (!string.IsNullOrWhiteSpace(gameObjectFileId) &&
        gameObjectNameByFileId != null &&
        gameObjectNameByFileId.TryGetValue(gameObjectFileId, out var gameObjectName) &&
        !string.IsNullOrWhiteSpace(gameObjectName)) {
      return "GameObject '" + gameObjectName + "' in '" + normalizedPath + "'";
    }

    if (!string.IsNullOrWhiteSpace(gameObjectFileId)) {
      return "GameObject fileID '" + gameObjectFileId + "' in '" + normalizedPath + "'";
    }

    return "SpriteWithNormals reference in '" + normalizedPath + "'";
  }

  static string BuildRequestedLibraryReferenceSuffix(
    string requestedLibraryName,
    Dictionary<string, List<string>> requestedLibraryReferences
  ) {
    if (requestedLibraryReferences == null ||
        string.IsNullOrWhiteSpace(requestedLibraryName) ||
        !requestedLibraryReferences.TryGetValue(requestedLibraryName, out var references) ||
        references == null ||
        references.Count == 0) {
      return "";
    }

    references.Sort(StringComparer.OrdinalIgnoreCase);
    var shownCount = Math.Min(3, references.Count);
    var shownReferences = references.Take(shownCount).ToList();
    var summary = " Referenced by " + string.Join("; ", shownReferences);
    if (references.Count > shownCount) {
      summary += "; and " + (references.Count - shownCount) + " more";
    }

    return summary + ".";
  }

  static Dictionary<string, SpriteRef> ParseLibraryRows(string path, List<string> errors) {
    // Keep row keys case-sensitive so color/normal joins require exact label/category case.
    var rows = new Dictionary<string, SpriteRef>(StringComparer.Ordinal);
    if (!File.Exists(path)) {
      errors.Add("Missing sprite library file '" + path + "'.");
      return rows;
    }

    string currentCategory = null;
    bool insideOverrideEntries = false;
    string currentLabel = null;
    SpriteRef? currentSpriteRef = null;

    void FlushLabel() {
      if (string.IsNullOrWhiteSpace(currentCategory) || string.IsNullOrWhiteSpace(currentLabel)) {
        currentLabel = null;
        currentSpriteRef = null;
        return;
      }

      if (!currentSpriteRef.HasValue) {
        currentLabel = null;
        currentSpriteRef = null;
        return;
      }

      var key = currentCategory + "\u001f" + currentLabel;
      rows[key] = currentSpriteRef.Value;
      currentLabel = null;
      currentSpriteRef = null;
    }

    foreach (var rawLine in File.ReadLines(path)) {
      var line = rawLine ?? "";

      if (line.StartsWith("  - m_Name:", StringComparison.Ordinal)) {
        FlushLabel();
        currentCategory = DecodeScalar(line.Substring("  - m_Name:".Length));
        insideOverrideEntries = false;
        continue;
      }

      if (line.StartsWith("    m_OverrideEntries:", StringComparison.Ordinal)) {
        insideOverrideEntries = true;
        continue;
      }

      if (!insideOverrideEntries) continue;

      if (line.StartsWith("    - m_Name:", StringComparison.Ordinal)) {
        FlushLabel();
        currentLabel = DecodeScalar(line.Substring("    - m_Name:".Length));
        currentSpriteRef = null;
        continue;
      }

      if (string.IsNullOrWhiteSpace(currentLabel)) continue;

      var spriteMatch = spriteRefRegex.Match(line);
      if (!spriteMatch.Success) continue;

      if (!long.TryParse(spriteMatch.Groups[1].Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fileId)) {
        continue;
      }

      var guid = spriteMatch.Groups[2].Value.Trim();
      currentSpriteRef = new SpriteRef(guid, fileId);
    }

    FlushLabel();
    return rows;
  }

  static string ResolveSpriteAddress(BuildState state, SpriteRef spriteRef, string context, bool recordError = true) {
    if (string.IsNullOrWhiteSpace(spriteRef.guid)) {
      if (recordError) {
        state.errors.Add("Missing GUID while resolving .");
      }
      return "";
    }

    if (!state.addressCacheByGuid.TryGetValue(spriteRef.guid, out var byFileId)) {
      Debug.Log($"[SpriteIndexBuilder] ResolveSpriteAddress: Caching address map for GUID {spriteRef.guid} )");
      byFileId = BuildAddressMapForGuid(state, spriteRef.guid, recordError);
      if (recordError || byFileId.Count > 0) {
        state.addressCacheByGuid[spriteRef.guid] = byFileId;
      }
    }

    if (byFileId.TryGetValue(spriteRef.fileId, out var address)) return address;

    if (recordError) {
      Debug.LogWarning($"[SpriteIndexBuilder] ResolveSpriteAddress: Failed to resolve FileID {spriteRef.fileId} in GUID {spriteRef.guid}. Context: {context}. Map size: {byFileId.Count}.");
    }

    var targetUnsigned = unchecked((ulong)spriteRef.fileId);
    foreach (var pair in byFileId) {
      if (unchecked((ulong)pair.Key) != targetUnsigned) continue;
      return pair.Value;
    }

    var fallbackAddress = TryResolveSpriteAddressFromContext(byFileId, context);
    if (!string.IsNullOrWhiteSpace(fallbackAddress)) return fallbackAddress;

    if (recordError) {
      state.errors.Add("Could not resolve sprite fileID '" + spriteRef.fileId + "' for GUID '" + spriteRef.guid + "'.");
    }
    return "";
  }

  static bool ValidateRuntimeAtlasAddress(BuildState state, string sliceAddress, string context, bool recordError = true) {
    return ValidateRuntimeAtlasAddress(state, sliceAddress, context, out _, recordError);
  }

  static bool ValidateRuntimeAtlasAddress(
    BuildState state,
    string sliceAddress,
    string context,
    out string failureReason,
    bool recordError = true
  ) {
    failureReason = "";
    if (state == null || string.IsNullOrWhiteSpace(sliceAddress)) return false;

    if (!SpriteSliceAddressUtility.TryParseSliceAddress(sliceAddress, out var atlasAssetPath, out var spriteName)) {
      return FailRuntimeAtlasAddress(state, "Invalid slice address '" + sliceAddress + "' (" + context + ").", recordError, out failureReason);
    }

    var normalizedAtlasPath = NormalizePath(atlasAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedAtlasPath) || string.IsNullOrWhiteSpace(spriteName)) {
      return FailRuntimeAtlasAddress(
        state,
        "Slice address '" + sliceAddress + "' did not resolve atlas path + sprite name (" + context + ").",
        recordError,
        out failureReason
      );
    }

    if (!state.activeTextureAssetPaths.Contains(normalizedAtlasPath)) {
      return FailRuntimeAtlasAddress(
        state,
        "Atlas path '" + normalizedAtlasPath + "' was not registered in texture group (" + context + ").",
        recordError,
        out failureReason
      );
    }

    if (state.addressables == null) return true;
    var guid = AssetDatabase.AssetPathToGUID(normalizedAtlasPath);
    if (string.IsNullOrWhiteSpace(guid)) {
      return FailRuntimeAtlasAddress(
        state,
        "Atlas path '" + normalizedAtlasPath + "' has no GUID (" + context + ").",
        recordError,
        out failureReason
      );
    }

    var entry = state.addressables.FindAssetEntry(guid);
    if (entry == null || entry.parentGroup == null || !IsManagedTextureGroup(entry.parentGroup)) {
      return FailRuntimeAtlasAddress(
        state,
        "Atlas path '" + normalizedAtlasPath + "' is not in texture addressables group (" + context + ").",
        recordError,
        out failureReason
      );
    }
    if (state.activeTextureGroupNames.Count > 0 && !state.activeTextureGroupNames.Contains(entry.parentGroup.Name)) {
      return FailRuntimeAtlasAddress(
        state,
        "Atlas path '" + normalizedAtlasPath + "' is not in an active managed texture group (" + context + ").",
        recordError,
        out failureReason
      );
    }

    if (!string.Equals(entry.address, normalizedAtlasPath, StringComparison.Ordinal)) {
      return FailRuntimeAtlasAddress(
        state,
        "Atlas path '" + normalizedAtlasPath + "' has addressable key '" + entry.address + "' instead of atlas asset path (" + context + ").",
        recordError,
        out failureReason
      );
    }

    return true;
  }

  static bool FailRuntimeAtlasAddress(BuildState state, string message, bool recordError, out string failureReason) {
    failureReason = message ?? "";
    if (recordError && state != null && !string.IsNullOrWhiteSpace(message)) {
      state.errors.Add(message);
    }
    return false;
  }

  static bool ShouldLogLibraryDiagnostics(string libraryName, string requestedLibraryName) {
    if (string.IsNullOrWhiteSpace(libraryName) && string.IsNullOrWhiteSpace(requestedLibraryName)) return false;
    if (string.Equals(libraryName, "UI/Fonts", StringComparison.OrdinalIgnoreCase)) return true;
    if (string.Equals(requestedLibraryName, "UI/Fonts", StringComparison.OrdinalIgnoreCase)) return true;
    return false;
  }

  static string TryResolveSpriteAddressFromContext(Dictionary<long, string> byFileId, string context) {
    if (byFileId == null || byFileId.Count == 0) return "";

    var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var pair in byFileId) {
      var spriteName = ExtractSpriteNameFromAddress(pair.Value);
      if (string.IsNullOrWhiteSpace(spriteName)) continue;
      byName[spriteName] = pair.Value;
    }

    if (byName.Count == 0) return "";
    if (byName.Count == 1) return byName.Values.FirstOrDefault() ?? "";

    var label = ExtractLabelFromContext(context);
    if (string.IsNullOrWhiteSpace(label)) return "";

    if (byName.TryGetValue(label, out var exact)) return exact;

    ParseLabel(label, out var labelPrefix, out var frame);
    var category = ExtractCategoryFromContext(context);
    if (frame > 0) {
      var frameText = frame.ToString(CultureInfo.InvariantCulture);

      if (byName.TryGetValue("1_" + frameText, out var oneFrame)) return oneFrame;
      if (byName.TryGetValue(frameText, out var numericFrame)) return numericFrame;
      if (!string.IsNullOrWhiteSpace(labelPrefix) && byName.TryGetValue(labelPrefix + "_" + frameText, out var labelPrefixFrame)) return labelPrefixFrame;

      var suffix = "_" + frameText;
      string singleSuffixMatch = null;
      foreach (var pair in byName) {
        if (pair.Key.Equals(frameText, StringComparison.OrdinalIgnoreCase) ||
            pair.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) {
          if (singleSuffixMatch != null) {
            singleSuffixMatch = null;
            break;
          }
          singleSuffixMatch = pair.Value;
        }
      }

      if (!string.IsNullOrWhiteSpace(singleSuffixMatch)) return singleSuffixMatch;

      if (!string.IsNullOrWhiteSpace(category)) {
        string singleCategoryFrameMatch = null;
        foreach (var pair in byName) {
          if (!SpriteNameMatchesCategoryAndFrame(pair.Key, category, frame)) continue;
          if (singleCategoryFrameMatch != null) {
            singleCategoryFrameMatch = null;
            break;
          }
          singleCategoryFrameMatch = pair.Value;
        }

        if (!string.IsNullOrWhiteSpace(singleCategoryFrameMatch)) return singleCategoryFrameMatch;
      }
    }

    return "";
  }

  static string ExtractCategoryFromContext(string context) {
    if (string.IsNullOrWhiteSpace(context)) return "";
    var colon = context.LastIndexOf(':');
    if (colon <= 0) return "";

    var slash = context.LastIndexOf('/', colon - 1);
    var start = slash >= 0 ? slash + 1 : 0;
    if (start >= colon) return "";

    var category = context.Substring(start, colon - start);
    return string.IsNullOrWhiteSpace(category) ? "" : category.Trim();
  }

  static string ExtractLabelFromContext(string context) {
    if (string.IsNullOrWhiteSpace(context)) return "";
    var colon = context.LastIndexOf(':');
    if (colon < 0 || colon >= context.Length - 1) return "";

    var suffixStart = context.LastIndexOf(" (", StringComparison.Ordinal);
    if (suffixStart <= colon) suffixStart = context.Length;

    var label = context.Substring(colon + 1, suffixStart - colon - 1);
    return string.IsNullOrWhiteSpace(label) ? "" : label.Trim();
  }

  static string ExtractSpriteNameFromAddress(string address) {
    if (string.IsNullOrWhiteSpace(address)) return "";
    var close = address.LastIndexOf(']');
    if (close <= 0 || close != address.Length - 1) return "";
    var open = address.LastIndexOf('[', close - 1);
    if (open < 0 || open >= close - 1) return "";
    return address.Substring(open + 1, close - open - 1);
  }

  static bool SpriteNameMatchesCategoryAndFrame(string spriteName, string category, int frame) {
    if (string.IsNullOrWhiteSpace(spriteName) || string.IsNullOrWhiteSpace(category) || frame <= 0) return false;

    var normalizedName = spriteName.Trim();
    var nameBody = normalizedName;
    var doubleUnderscoreIndex = normalizedName.IndexOf("__", StringComparison.Ordinal);
    if (doubleUnderscoreIndex >= 0 && doubleUnderscoreIndex < normalizedName.Length - 2) {
      nameBody = normalizedName.Substring(doubleUnderscoreIndex + 2);
    }

    var categoryPrefix = category + "_";
    if (!nameBody.StartsWith(categoryPrefix, StringComparison.OrdinalIgnoreCase)) return false;

    var lastUnderscore = nameBody.LastIndexOf('_');
    if (lastUnderscore < 0 || lastUnderscore >= nameBody.Length - 1) return false;

    var frameText = nameBody.Substring(lastUnderscore + 1);
    return int.TryParse(frameText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedFrame) &&
           parsedFrame == frame;
  }

  static bool TryResolveDerivedNormalAddress(BuildState state, string colorAddress, out string normalAddress) {
    normalAddress = "";
    if (string.IsNullOrWhiteSpace(colorAddress)) return false;

    if (SpriteSliceAddressUtility.TryParseSliceAddress(colorAddress, out var colorAtlasAssetPath, out var colorSpriteName)) {
      if (!TryDeriveSiblingNormalAtlasAssetPath(state, colorAtlasAssetPath, out var normalAtlasAssetPath)) return false;
      return TryResolveSpriteAddressByName(state, normalAtlasAssetPath, colorSpriteName, out normalAddress);
    }

    if (!TryDeriveSiblingNormalAtlasAssetPath(state, colorAddress, out var atlasOnlyNormalAddress)) return false;
    normalAddress = atlasOnlyNormalAddress;
    return true;
  }

  static bool TryDeriveSiblingNormalAtlasAssetPath(BuildState state, string colorAtlasAssetPath, out string normalAtlasAssetPath) {
    normalAtlasAssetPath = "";
    var normalizedColorAtlasAssetPath = NormalizePath(colorAtlasAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedColorAtlasAssetPath)) return false;

    if (state != null &&
        state.derivedNormalAtlasPathByColorAtlas.TryGetValue(normalizedColorAtlasAssetPath, out var cachedNormalAtlasAssetPath)) {
      normalAtlasAssetPath = cachedNormalAtlasAssetPath ?? "";
      return !string.IsNullOrWhiteSpace(normalAtlasAssetPath);
    }

    var extension = Path.GetExtension(normalizedColorAtlasAssetPath);
    if (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)) return false;

    var jpgCandidate = NormalizePath(Path.ChangeExtension(normalizedColorAtlasAssetPath, ".jpg"));
    if (AssetExistsAtPath(state, jpgCandidate)) {
      normalAtlasAssetPath = jpgCandidate;
      if (state != null) {
        state.derivedNormalAtlasPathByColorAtlas[normalizedColorAtlasAssetPath] = normalAtlasAssetPath;
      }
      return true;
    }

    var jpegCandidate = NormalizePath(Path.ChangeExtension(normalizedColorAtlasAssetPath, ".jpeg"));
    if (AssetExistsAtPath(state, jpegCandidate)) {
      normalAtlasAssetPath = jpegCandidate;
      if (state != null) {
        state.derivedNormalAtlasPathByColorAtlas[normalizedColorAtlasAssetPath] = normalAtlasAssetPath;
      }
      return true;
    }

    if (state != null) {
      state.derivedNormalAtlasPathByColorAtlas[normalizedColorAtlasAssetPath] = "";
    }
    return false;
  }

  static bool AssetExistsAtPath(BuildState state, string assetPath) {
    var normalizedAssetPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath)) return false;

    if (state != null && state.assetExistsByPath.TryGetValue(normalizedAssetPath, out var cachedExists)) {
      return cachedExists;
    }

    var exists = !string.IsNullOrWhiteSpace(AssetDatabase.AssetPathToGUID(normalizedAssetPath));
    if (state != null) {
      state.assetExistsByPath[normalizedAssetPath] = exists;
    }
    return exists;
  }

  static bool TryResolveSpriteAddressByName(BuildState state, string atlasAssetPath, string spriteName, out string address) {
    address = "";
    if (state == null || string.IsNullOrWhiteSpace(atlasAssetPath) || string.IsNullOrWhiteSpace(spriteName)) return false;

    var normalizedAtlasAssetPath = NormalizePath(atlasAssetPath);
    var guid = AssetDatabase.AssetPathToGUID(normalizedAtlasAssetPath);
    if (string.IsNullOrWhiteSpace(guid)) return false;

    if (!state.addressCacheByGuid.TryGetValue(guid, out var byFileId)) {
      byFileId = BuildAddressMapForGuid(state, guid, recordError: false);
      if (byFileId.Count > 0) {
        state.addressCacheByGuid[guid] = byFileId;
      }
    }

    if (!state.spriteAddressByNameCacheByGuid.TryGetValue(guid, out var bySpriteName)) {
      bySpriteName = BuildSpriteAddressByNameMap(byFileId);
      if (bySpriteName.Count > 0) {
        state.spriteAddressByNameCacheByGuid[guid] = bySpriteName;
      }
    }

    if (bySpriteName != null && bySpriteName.TryGetValue(spriteName, out var exactAddress)) {
      address = exactAddress;
      return true;
    }

    string numericFallbackAddress = null;
    foreach (var pair in byFileId) {
      var candidateAddress = pair.Value;
      var candidateSpriteName = ExtractSpriteNameFromAddress(candidateAddress);
      if (string.IsNullOrWhiteSpace(candidateSpriteName)) continue;
      if (numericFallbackAddress == null &&
          SpriteSliceAddressUtility.HasEquivalentNumericLabel(candidateSpriteName, spriteName)) {
        numericFallbackAddress = candidateAddress;
      }
    }

    if (string.IsNullOrWhiteSpace(numericFallbackAddress)) return false;
    address = numericFallbackAddress;
    return true;
  }

  static Dictionary<string, string> BuildSpriteAddressByNameMap(Dictionary<long, string> byFileId) {
    var bySpriteName = new Dictionary<string, string>(StringComparer.Ordinal);
    if (byFileId == null || byFileId.Count <= 0) return bySpriteName;

    foreach (var pair in byFileId) {
      var spriteName = ExtractSpriteNameFromAddress(pair.Value);
      if (string.IsNullOrWhiteSpace(spriteName)) continue;
      bySpriteName[spriteName] = pair.Value;
    }

    return bySpriteName;
  }

  static Dictionary<long, string> BuildAddressMapForGuid(BuildState state, string guid, bool recordError = true) {
    var map = new Dictionary<long, string>();
    var sourcePath = NormalizePath(AssetDatabase.GUIDToAssetPath(guid));
    if (string.IsNullOrWhiteSpace(sourcePath)) {
      if (recordError) {
        Debug.LogError($"[SpriteIndexBuilder] BuildAddressMapForGuid: GUID {guid} resolved to empty path.");
        state.errors.Add("GUID '" + guid + "' does not map to an asset path.");
      }
      return map;
    }

    EnsureAddressableTextureEntry(state, sourcePath);
    var runtimeAtlasAssetPath = ResolveRuntimeTextureAssetPathForBuild(state, sourcePath);
    var metaPath = sourcePath + ".meta";
    if (!File.Exists(metaPath)) {
      if (recordError) {
        Debug.LogError($"[SpriteIndexBuilder] BuildAddressMapForGuid: Meta file missing for {sourcePath} (GUID {guid}).");
        state.errors.Add("Meta file was not found for GUID '" + guid + "' at path '" + metaPath + "'.");
      }
      return map;
    }

    if (!TryParseSpriteSheetInternalIdTable(metaPath, map, out var parseError)) {
      if (recordError) {
        Debug.LogError($"[SpriteIndexBuilder] BuildAddressMapForGuid: Failed to parse sprite sheet table for {sourcePath}: {parseError}");
        state.errors.Add("Failed to parse spriteSheet.sprites table for GUID '" + guid + "' at path '" + metaPath + "'" +
                         (string.IsNullOrWhiteSpace(parseError) ? "." : ": " + parseError));
      }
      return map;
    }

    if (!TryParseNameFileIdTable(metaPath, map, out var nameTableError)) {
      if (recordError) {
        Debug.LogError($"[SpriteIndexBuilder] BuildAddressMapForGuid: Failed to parse name table for {sourcePath}: {nameTableError}");
        state.errors.Add("Failed to parse nameFileIdTable for GUID '" + guid + "' at path '" + metaPath + "'" +
                         (string.IsNullOrWhiteSpace(nameTableError) ? "." : ": " + nameTableError));
      }
      return map;
    }

    var supplementedLocalIdCount = 0;
    if (map.Count > BuilderConfig.MaxSpriteCountForEditorLocalIdSupplement) {
      if (state != null) {
        state.skippedHeavyLocalIdSupplementAtlasCount++;
        state.skippedHeavyLocalIdSupplementSpriteCount += map.Count;
      }
    } else {
      supplementedLocalIdCount = SupplementAddressMapWithEditorLocalFileIds(sourcePath, map);
      if (supplementedLocalIdCount > 0 && state != null) {
        state.supplementedLocalIdAtlasCount++;
        state.supplementedLocalIdCount += supplementedLocalIdCount;
      }
    }

    var keys = map.Keys.ToList();
    for (var i = 0; i < keys.Count; i++) {
      var localId = keys[i];
      var spriteName = map[localId];
      map[localId] = runtimeAtlasAssetPath + "[" + spriteName + "]";
    }

    if (map.Count == 0) {
      if (recordError) {
        Debug.LogWarning($"[SpriteIndexBuilder] BuildAddressMapForGuid: No sprites found in {sourcePath} (GUID {guid}).");
        state.errors.Add("No sprite sub-assets found for GUID '" + guid + "' at path '" + sourcePath + "'.");
      }
    } else {
      Debug.Log(
        $"[SpriteIndexBuilder] BuildAddressMapForGuid: Mapped {map.Count} sprites for {sourcePath}" +
        (string.Equals(runtimeAtlasAssetPath, sourcePath, StringComparison.OrdinalIgnoreCase) ? "" : $" runtimeAtlas={runtimeAtlasAssetPath}") +
        (supplementedLocalIdCount > 0 ? $" (supplementedLocalIds={supplementedLocalIdCount})" : "")
      );
    }

    return map;
  }

  static int SupplementAddressMapWithEditorLocalFileIds(string assetPath, Dictionary<long, string> target) {
    if (target == null || string.IsNullOrWhiteSpace(assetPath)) return 0;

    var supplemented = 0;

    void AddSpriteIds(UnityEngine.Object[] assets) {
      if (assets == null || assets.Length == 0) return;

      for (var i = 0; i < assets.Length; i++) {
        if (assets[i] is not Sprite sprite) continue;
        if (string.IsNullOrWhiteSpace(sprite.name)) continue;
        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out _, out long localFileId)) continue;
        if (target.TryGetValue(localFileId, out var existingName) &&
            string.Equals(existingName, sprite.name, StringComparison.Ordinal)) {
          continue;
        }

        target[localFileId] = sprite.name;
        supplemented++;
      }
    }

    AddSpriteIds(AssetDatabase.LoadAllAssetsAtPath(assetPath));
    AddSpriteIds(AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath));
    return supplemented;
  }

  static bool TryParseSpriteSheetInternalIdTable(string metaPath, Dictionary<long, string> target, out string error) {
    error = "";
    if (target == null) {
      error = "Target dictionary is null.";
      return false;
    }

    var insideSpriteSheet = false;
    var spriteSheetIndent = -1;
    var insideSpritesList = false;
    var spritesListIndent = -1;
    var insideSpriteEntry = false;
    var spriteEntryIndent = -1;
    string currentSpriteName = "";

    void BeginSpriteEntry(int indent, string trimmedLine) {
      insideSpriteEntry = true;
      spriteEntryIndent = indent;
      currentSpriteName = "";

      if (!trimmedLine.StartsWith("- ", StringComparison.Ordinal)) return;
      var remainder = trimmedLine.Substring(2).TrimStart();
      if (TryReadScalar(remainder, "name", out var parsedName) && !string.IsNullOrWhiteSpace(parsedName)) {
        currentSpriteName = parsedName;
      }
    }

    foreach (var rawLine in File.ReadLines(metaPath)) {
      var line = rawLine ?? "";
      var trimmed = line.Trim();
      var indent = line.Length - line.TrimStart().Length;

      if (!insideSpriteSheet) {
        if (trimmed.StartsWith("spriteSheet:", StringComparison.Ordinal)) {
          insideSpriteSheet = true;
          spriteSheetIndent = indent;
        }
        continue;
      }

      if (!string.IsNullOrWhiteSpace(trimmed) && indent <= spriteSheetIndent) {
        insideSpriteSheet = false;
        insideSpritesList = false;
        insideSpriteEntry = false;
        currentSpriteName = "";

        if (trimmed.StartsWith("spriteSheet:", StringComparison.Ordinal)) {
          insideSpriteSheet = true;
          spriteSheetIndent = indent;
        }
        continue;
      }

      if (!insideSpritesList) {
        if (trimmed.StartsWith("sprites:", StringComparison.Ordinal)) {
          insideSpritesList = true;
          spritesListIndent = indent;
        }
        continue;
      }

      if (!string.IsNullOrWhiteSpace(trimmed) &&
          (indent < spritesListIndent || (indent == spritesListIndent && !trimmed.StartsWith("- ", StringComparison.Ordinal)))) {
        insideSpritesList = false;
        insideSpriteEntry = false;
        currentSpriteName = "";
        continue;
      }

      if (trimmed.StartsWith("- ", StringComparison.Ordinal)) {
        BeginSpriteEntry(indent, trimmed);
        continue;
      }

      if (!insideSpriteEntry) continue;

      if (!string.IsNullOrWhiteSpace(trimmed) && indent <= spriteEntryIndent) {
        insideSpriteEntry = false;
        currentSpriteName = "";
        if (trimmed.StartsWith("- ", StringComparison.Ordinal)) {
          BeginSpriteEntry(indent, trimmed);
        }
        continue;
      }

      if (TryReadScalar(trimmed, "name", out var spriteNameValue) && !string.IsNullOrWhiteSpace(spriteNameValue)) {
        currentSpriteName = spriteNameValue;
        continue;
      }

      if (string.IsNullOrWhiteSpace(currentSpriteName)) continue;
      if (!trimmed.StartsWith("internalID:", StringComparison.Ordinal)) continue;

      var idValue = trimmed.Substring("internalID:".Length).Trim();
      if (long.TryParse(idValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId)) {
        target[parsedId] = currentSpriteName;
      }
    }

    return true;
  }

  static bool TryParseNameFileIdTable(string metaPath, Dictionary<long, string> target, out string error) {
    error = "";
    if (target == null) {
      error = "Target dictionary is null.";
      return false;
    }

    if (TryParseLegacyNameFileIdTable(metaPath, target, out error)) {
      return true;
    }

    return TryParseInternalIdToNameTable(metaPath, target, out error);
  }

  static bool TryParseLegacyNameFileIdTable(string metaPath, Dictionary<long, string> target, out string error) {
    error = "";
    if (target == null) {
      error = "Target dictionary is null.";
      return false;
    }

    var inTable = false;
    var tableIndent = -1;
    var foundTable = false;
    var parsedAny = false;

    foreach (var rawLine in File.ReadLines(metaPath)) {
      var line = rawLine ?? "";
      var trimmed = line.Trim();
      var indent = line.Length - line.TrimStart().Length;

      if (!inTable) {
        if (trimmed.StartsWith("nameFileIdTable:", StringComparison.Ordinal)) {
          inTable = true;
          tableIndent = indent;
          foundTable = true;
        }
        continue;
      }

      if (string.IsNullOrWhiteSpace(trimmed)) continue;

      if (indent <= tableIndent) {
        inTable = false;
        if (trimmed.StartsWith("nameFileIdTable:", StringComparison.Ordinal)) {
          inTable = true;
          tableIndent = indent;
        }
        continue;
      }

      var separatorIndex = trimmed.IndexOf(':');
      if (separatorIndex <= 0 || separatorIndex >= trimmed.Length - 1) continue;

      var spriteName = DecodeScalar(trimmed.Substring(0, separatorIndex));
      if (string.IsNullOrWhiteSpace(spriteName)) continue;

      var idValue = trimmed.Substring(separatorIndex + 1).Trim();
      if (!long.TryParse(idValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fileId)) continue;

      target[fileId] = spriteName;
      parsedAny = true;
    }

    return foundTable && parsedAny;
  }

  static bool TryParseInternalIdToNameTable(string metaPath, Dictionary<long, string> target, out string error) {
    error = "";
    if (target == null) {
      error = "Target dictionary is null.";
      return false;
    }

    var inTable = false;
    var tableIndent = -1;
    var currentEntryIndent = -1;
    long? currentFileId = null;

    void FlushEntry(string spriteName) {
      if (!currentFileId.HasValue || string.IsNullOrWhiteSpace(spriteName)) return;
      target[currentFileId.Value] = spriteName;
    }

    foreach (var rawLine in File.ReadLines(metaPath)) {
      var line = rawLine ?? "";
      var trimmed = line.Trim();
      var indent = line.Length - line.TrimStart().Length;

      if (!inTable) {
        if (trimmed.StartsWith("internalIDToNameTable:", StringComparison.Ordinal)) {
          inTable = true;
          tableIndent = indent;
        }
        continue;
      }

      if (string.IsNullOrWhiteSpace(trimmed)) continue;

      if (indent < tableIndent || (indent == tableIndent && !trimmed.StartsWith("- ", StringComparison.Ordinal))) {
        break;
      }

      if (trimmed.StartsWith("- first:", StringComparison.Ordinal)) {
        currentEntryIndent = indent;
        currentFileId = null;
        continue;
      }

      if (currentEntryIndent >= 0 && indent <= currentEntryIndent) {
        currentEntryIndent = -1;
        currentFileId = null;
      }

      if (trimmed.StartsWith("213:", StringComparison.Ordinal)) {
        var idValue = trimmed.Substring("213:".Length).Trim();
        if (long.TryParse(idValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fileId)) {
          currentFileId = fileId;
        }
        continue;
      }

      if (!trimmed.StartsWith("second:", StringComparison.Ordinal)) continue;

      var spriteName = DecodeScalar(trimmed.Substring("second:".Length));
      FlushEntry(spriteName);
      currentEntryIndent = -1;
      currentFileId = null;
    }

    return true;
  }

  static string BuildShardBody(List<ShardRow> rows) {
    var sb = new StringBuilder(rows.Count * 48);
    for (var i = 0; i < rows.Count; i++) {
      var row = rows[i];
      sb
        .Append(Escape(row.labelPrefix)).Append('\t')
        .Append(Escape(row.category)).Append('\t')
        .Append(row.frame.ToString(CultureInfo.InvariantCulture)).Append('\t')
        .Append(Escape(row.colorAddress)).Append('\t')
        .Append(Escape(row.normalAddress))
        .Append('\n');
    }
    return sb.ToString();
  }

  static string BuildShardAssetPath(string libraryName) {
    var safe = libraryName.Replace('\\', '_').Replace('/', '_').Replace(':', '_');
    var hash = ComputeHash(libraryName).Substring(0, 12);
    return NormalizePath(BuilderConfig.RuntimeIndexFolder + "/" + safe + "_" + hash + ".bytes");
  }

  static void CleanupStaleShardAssets(HashSet<string> activePaths) {
    var folder = NormalizePath(BuilderConfig.RuntimeIndexFolder);
    if (!AssetDatabase.IsValidFolder(folder)) return;

    var bytesFiles = Directory.GetFiles(folder, "*.bytes", SearchOption.TopDirectoryOnly);
    for (var i = 0; i < bytesFiles.Length; i++) {
      var assetPath = NormalizePath(bytesFiles[i]);
      if (activePaths.Contains(assetPath)) continue;
      AssetDatabase.DeleteAsset(assetPath);
    }
  }

  static void CleanupStaleTextureEntries(BuildState state) {
    if (state == null || state.addressables == null) return;

    var activeAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var activePath in state.activeTextureAssetPaths) {
      var normalized = NormalizePath(activePath);
      if (!string.IsNullOrWhiteSpace(normalized)) {
        activeAssetPaths.Add(normalized);
      }
    }

    var managedGroups = GetManagedTextureGroups(state.addressables).ToList();
    var emptyChunkGroups = new List<AddressableAssetGroup>();

    for (var groupIndex = 0; groupIndex < managedGroups.Count; groupIndex++) {
      var textureGroup = managedGroups[groupIndex];
      if (textureGroup == null || textureGroup.Settings == null) continue;

      var staleGuids = new List<string>();
      foreach (var entry in textureGroup.entries) {
        if (entry == null || string.IsNullOrWhiteSpace(entry.guid)) continue;
        var assetPath = NormalizePath(AssetDatabase.GUIDToAssetPath(entry.guid));
        if (string.IsNullOrWhiteSpace(assetPath) || !activeAssetPaths.Contains(assetPath)) {
          staleGuids.Add(entry.guid);
        }
      }

      for (var i = 0; i < staleGuids.Count; i++) {
        textureGroup.Settings.RemoveAssetEntry(staleGuids[i], false);
      }

      if (!string.Equals(textureGroup.Name, BuilderConfig.TextureAddressablesGroupName, StringComparison.OrdinalIgnoreCase) &&
          IsManagedTextureGroup(textureGroup) &&
          textureGroup.entries.Count == 0) {
        emptyChunkGroups.Add(textureGroup);
      }
    }

    for (var i = 0; i < emptyChunkGroups.Count; i++) {
      state.addressables.RemoveGroup(emptyChunkGroups[i]);
      state.textureGroupsByName.Remove(emptyChunkGroups[i].Name);
    }
  }

  static bool IsManagedTextureGroup(AddressableAssetGroup group) {
    return group != null && IsManagedTextureGroupName(group.Name);
  }

  static bool IsManagedTextureGroupName(string groupName) {
    if (string.IsNullOrWhiteSpace(groupName)) return false;
    return string.Equals(groupName, BuilderConfig.TextureAddressablesGroupName, StringComparison.OrdinalIgnoreCase) ||
           groupName.StartsWith(BuilderConfig.ManagedTextureGroupPrefix, StringComparison.OrdinalIgnoreCase);
  }

  static IEnumerable<AddressableAssetGroup> GetManagedTextureGroups(AddressableAssetSettings settings) {
    if (settings == null || settings.groups == null) yield break;

    for (var i = 0; i < settings.groups.Count; i++) {
      var group = settings.groups[i];
      if (!IsManagedTextureGroup(group)) continue;
      yield return group;
    }
  }

  static string BuildManagedTextureGroupName(string syntheticLabel) {
    return string.IsNullOrWhiteSpace(syntheticLabel)
      ? BuilderConfig.TextureAddressablesGroupName
      : BuilderConfig.ManagedTextureGroupPrefix + syntheticLabel;
  }

  static AddressableAssetGroup GetOrCreateManagedTextureGroup(BuildState state, string groupName) {
    if (state == null || state.addressables == null || string.IsNullOrWhiteSpace(groupName)) return null;

    if (state.textureGroupsByName.TryGetValue(groupName, out var cachedGroup) && cachedGroup != null) {
      return cachedGroup;
    }

    var group = EnsureAddressableGroup(state.addressables, groupName, state.contextLabel, state.logResult, out var schemaRepairs);
    state.schemaRepairs += schemaRepairs;
    if (group != null) {
      state.textureGroupsByName[group.Name] = group;
    }
    return group;
  }

  static void CleanupStaleIndexEntries(
    BuildState state,
    AddressableAssetGroup indexGroup,
    HashSet<string> activeShardPaths,
    string manifestAssetPath,
    HashSet<string> activeAtlasMetadataAssetPaths
  ) {
    if (indexGroup == null || indexGroup.Settings == null) return;

    var activeAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var runtimeIndexFolder = NormalizePath(BuilderConfig.RuntimeIndexFolder);
    if (activeShardPaths != null) {
      foreach (var shardPath in activeShardPaths) {
        var normalizedShardPath = NormalizePath(shardPath);
        if (!string.IsNullOrWhiteSpace(normalizedShardPath)) {
          activeAssetPaths.Add(normalizedShardPath);
        }
      }
    }

    var normalizedManifestPath = NormalizePath(manifestAssetPath);
    if (!string.IsNullOrWhiteSpace(normalizedManifestPath)) {
      activeAssetPaths.Add(normalizedManifestPath);
    }
    if (activeAtlasMetadataAssetPaths != null) {
      foreach (var metadataAssetPath in activeAtlasMetadataAssetPaths) {
        var normalizedMetadataPath = NormalizePath(metadataAssetPath);
        if (!string.IsNullOrWhiteSpace(normalizedMetadataPath)) {
          activeAssetPaths.Add(normalizedMetadataPath);
        }
      }
    }

    var staleGuids = new List<string>();
    foreach (var entry in indexGroup.entries) {
      if (entry == null || string.IsNullOrWhiteSpace(entry.guid)) continue;

      var assetPath = NormalizePath(AssetDatabase.GUIDToAssetPath(entry.guid));
      var isManagedEntry =
        (!string.IsNullOrWhiteSpace(runtimeIndexFolder) &&
         assetPath.StartsWith(runtimeIndexFolder + "/", StringComparison.OrdinalIgnoreCase)) ||
        string.Equals(assetPath, normalizedManifestPath, StringComparison.OrdinalIgnoreCase) ||
        HasAddressableLabel(entry, BuilderConfig.AtlasMetadataAddressablesLabel) ||
        LooksLikeAtlasMetadataAssetPath(state, assetPath) ||
        LooksLikeAtlasMetadataAssetPath(state, entry.address);
      if (!isManagedEntry) continue;

      if (string.IsNullOrWhiteSpace(assetPath) || !activeAssetPaths.Contains(assetPath)) {
        staleGuids.Add(entry.guid);
      }
    }

    for (var i = 0; i < staleGuids.Count; i++) {
      indexGroup.Settings.RemoveAssetEntry(staleGuids[i], false);
    }
  }

  static AddressableAssetGroup EnsureAddressableGroup(
    AddressableAssetSettings settings,
    string groupName,
    string contextLabel,
    bool logResult,
    out int schemaRepairs
  ) {
    schemaRepairs = 0;
    var group = settings.FindGroup(groupName);
    if (group == null) {
      group = settings.CreateGroup(
        groupName,
        false,
        false,
        false,
        null,
        typeof(BundledAssetGroupSchema),
        typeof(ContentUpdateGroupSchema)
      );
    }

    if (group == null) {
      return settings.DefaultGroup;
    }

    schemaRepairs += EnsureAddressableGroupSchema<BundledAssetGroupSchema>(settings, group, groupName, contextLabel, logResult);
    schemaRepairs += EnsureAddressableGroupSchema<ContentUpdateGroupSchema>(settings, group, groupName, contextLabel, logResult);
    schemaRepairs += EnsureAddressableGroupBuildDefaults(group, groupName, contextLabel, logResult);
    if (schemaRepairs > 0) {
      EditorUtility.SetDirty(group);
      EditorUtility.SetDirty(settings);
      AssetDatabase.SaveAssets();
    }

    return group;
  }

  static int EnsureAddressableGroupSchema<TSchema>(
    AddressableAssetSettings settings,
    AddressableAssetGroup group,
    string groupName,
    string contextLabel,
    bool logResult
  ) where TSchema : AddressableAssetGroupSchema {
    if (group == null) return 0;
    if (group.GetSchema<TSchema>() != null) return 0;

    var schema = group.AddSchema<TSchema>();
    if (schema == null) return 0;

    EditorUtility.SetDirty(schema);
    if (settings != null) {
      EditorUtility.SetDirty(settings);
    }

    if (logResult) {
      Debug.Log("[SpriteIndexBuilder] [" + contextLabel + "] Repaired Addressables group '" + groupName + "' by adding schema '" + typeof(TSchema).Name + "'.");
    }

    return 1;
  }

  static int EnsureAddressableGroupBuildDefaults(
    AddressableAssetGroup group,
    string groupName,
    string contextLabel,
    bool logResult
  ) {
    if (group == null || string.IsNullOrWhiteSpace(groupName)) return 0;

    var schema = group.GetSchema<BundledAssetGroupSchema>();
    if (schema == null) return 0;

    var changed = false;

    if (IsManagedTextureGroupName(groupName)) {
      if (schema.BundleMode != BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel) {
        schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel;
        changed = true;
      }
    }

    // Enforce LZ4 compression and caching for both texture and index groups to improve runtime loading performance.
    if (schema.Compression != BundledAssetGroupSchema.BundleCompressionMode.LZ4) {
      schema.Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;
      changed = true;
    }

    if (!schema.UseAssetBundleCache) {
      schema.UseAssetBundleCache = true;
      changed = true;
    }

    if (!changed) return 0;

    EditorUtility.SetDirty(schema);
    EditorUtility.SetDirty(group);

    if (logResult) {
      Debug.Log(
        "[SpriteIndexBuilder] [" + contextLabel + "] Updated Addressables group '" +
        groupName + "' defaults (Compression=LZ4, Cache=True) to optimize runtime performance."
      );
    }

    return 1;
  }

  static bool ValidateAddressableGroupsPreflight(BuildState state, string contextLabel, bool failOnError) {
    if (state == null || state.addressables == null) {
      if (failOnError) throw new BuildFailedException("Addressables settings are not available for preflight.");
      return false;
    }

    var valid = true;
    var textureGroups = GetManagedTextureGroups(state.addressables).ToList();
    if (textureGroups.Count <= 0) {
      state.errors.Add("Addressables managed texture groups with prefix '" + BuilderConfig.TextureAddressablesGroupName + "' are missing.");
      valid = false;
    } else {
      for (var i = 0; i < textureGroups.Count; i++) {
        var textureGroup = textureGroups[i];
        if (HasRequiredGroupSchemas(textureGroup)) continue;
        state.errors.Add("Addressables managed texture group '" + textureGroup.Name + "' is missing required schemas.");
        valid = false;
      }
    }

    if (state.indexGroup == null) {
      state.errors.Add("Addressables group '" + BuilderConfig.IndexAddressablesGroupName + "' is missing.");
      valid = false;
    } else if (!HasRequiredGroupSchemas(state.indexGroup)) {
      state.errors.Add("Addressables group '" + BuilderConfig.IndexAddressablesGroupName + "' is missing required schemas.");
      valid = false;
    }

    if (valid) return true;

    Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] Addressables preflight failed. Ensure required schemas are present.");
    if (failOnError) {
      throw new BuildFailedException("Addressables preflight failed.");
    }

    return false;
  }

  static bool HasRequiredGroupSchemas(AddressableAssetGroup group) {
    if (group == null) return false;
    return group.GetSchema<BundledAssetGroupSchema>() != null &&
           group.GetSchema<ContentUpdateGroupSchema>() != null;
  }

  static void LogRuntimeIndexSummary(
    string contextLabel,
    bool success,
    int libraryNameCount,
    int shardCount,
    int schemaRepairs,
    int errorCount
  ) {
    var message =
      "[SpriteIndexBuilder] [" + contextLabel + "] Runtime index " + (success ? "succeeded" : "failed") +
      ". libraryNames=" + libraryNameCount +
      " shards=" + shardCount +
      " schemaRepairs=" + schemaRepairs +
      " errors=" + errorCount;

    if (success) {
      Debug.Log(message);
      return;
    }

    Debug.LogError(message);
  }

  static void LogSyntheticTextureLabelSummary(string contextLabel, BuildState state) {
    if (state == null || state.syntheticTextureLabelCounts == null || state.syntheticTextureLabelCounts.Count == 0) return;

    var labelCount = state.syntheticTextureLabelCounts.Count;
    var assignmentCount = Math.Max(state.syntheticTextureLabelAssignments, 0);
    var averagePerLabel = labelCount > 0 ? (float)assignmentCount / labelCount : 0f;

    Debug.Log(
      "[SpriteIndexBuilder] [" + contextLabel + "] Synthetic texture bundle labels assigned." +
      " entries=" + assignmentCount +
      " labels=" + labelCount +
      " defaultDepth=" + BuilderConfig.SyntheticTextureLabelFolderDepth +
      " groupedGearDepth=" + BuilderConfig.GroupedGearSyntheticTextureLabelFolderDepth +
      " avgPerLabel=" + averagePerLabel.ToString("0.00", CultureInfo.InvariantCulture)
    );

    var topLabels = state.syntheticTextureLabelCounts
      .OrderByDescending(pair => pair.Value)
      .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
      .Take(10)
      .ToList();
    if (topLabels.Count == 0) return;

    var topSb = new StringBuilder(256);
    for (var i = 0; i < topLabels.Count; i++) {
      if (i > 0) topSb.Append(", ");
      topSb.Append(topLabels[i].Key).Append('=').Append(topLabels[i].Value);
    }

    Debug.Log("[SpriteIndexBuilder] [" + contextLabel + "] Top synthetic texture bundle labels: " + topSb);
  }

  static void LogAddressablesBuildPlan(AddressableAssetSettings settings, string contextLabel) {
    if (settings == null) return;

    var textureGroups = GetManagedTextureGroups(settings).ToList();
    if (textureGroups.Count == 0) {
      Debug.LogWarning("[SpriteIndexBuilder] [" + contextLabel + "][BuildDiag] Managed texture Addressables groups were not found before build.");
      return;
    }

    var schema = textureGroups[0].GetSchema<BundledAssetGroupSchema>();
    var bundleMode = schema != null ? schema.BundleMode.ToString() : "Unknown";
    var compression = schema != null ? schema.Compression.ToString() : "Unknown";

    var bundleDiagnostics = new Dictionary<string, BundleBuildDiagnostic>(StringComparer.OrdinalIgnoreCase);
    var assetDiagnostics = new List<AssetBuildDiagnostic>();
    var totalApproxBytes = 0L;
    var unlabeledEntries = 0;
    var multiLabelEntries = 0;
    var totalEntryCount = 0;

    for (var groupIndex = 0; groupIndex < textureGroups.Count; groupIndex++) {
      var textureGroup = textureGroups[groupIndex];
      if (textureGroup == null) continue;
      totalEntryCount += textureGroup.entries.Count;

      foreach (var entry in textureGroup.entries) {
        if (entry == null) continue;

        var assetPath = NormalizePath(AssetDatabase.GUIDToAssetPath(entry.guid));
        if (string.IsNullOrWhiteSpace(assetPath)) {
          assetPath = NormalizePath(entry.AssetPath);
        }

        var syntheticLabel = GetSyntheticTextureBundleLabel(entry, out var syntheticLabelCount);
        if (syntheticLabelCount > 1) {
          multiLabelEntries++;
        }

        if (string.IsNullOrWhiteSpace(syntheticLabel)) {
          unlabeledEntries++;
          continue;
        }

        var approxSourceBytes = GetApproxAssetSourceBytes(assetPath, out var fileCount, out var isFolderEntry);
        totalApproxBytes += approxSourceBytes;
        assetDiagnostics.Add(new AssetBuildDiagnostic(assetPath, syntheticLabel, approxSourceBytes));

        if (!bundleDiagnostics.TryGetValue(syntheticLabel, out var bundleDiagnostic)) {
          bundleDiagnostic = new BundleBuildDiagnostic(syntheticLabel);
          bundleDiagnostics[syntheticLabel] = bundleDiagnostic;
        }

        bundleDiagnostic.entryCount++;
        bundleDiagnostic.fileCount += fileCount;
        if (isFolderEntry) {
          bundleDiagnostic.folderEntryCount++;
        }
        bundleDiagnostic.approxSourceBytes += approxSourceBytes;

        if (approxSourceBytes > bundleDiagnostic.largestAssetBytes) {
          bundleDiagnostic.largestAssetBytes = approxSourceBytes;
          bundleDiagnostic.largestAssetPath = assetPath;
        }
      }
    }

    Debug.Log(
      "[SpriteIndexBuilder] [" + contextLabel + "][BuildDiag] Texture build plan." +
      " groups=" + textureGroups.Count +
      " bundleMode=" + bundleMode +
      " compression=" + compression +
      " disableVisibleSubAssets=" + (settings.DisableVisibleSubAssetRepresentations ? 1 : 0) +
      " nonRecursive=" + (settings.NonRecursiveBuilding ? 1 : 0) +
      " entries=" + totalEntryCount +
      " labelledBundles=" + bundleDiagnostics.Count +
      " unlabeledEntries=" + unlabeledEntries +
      " multiLabelEntries=" + multiLabelEntries +
      " approxSource=" + FormatByteCount(totalApproxBytes)
    );

    var topBundles = bundleDiagnostics.Values
      .OrderByDescending(bundle => bundle.approxSourceBytes)
      .ThenBy(bundle => bundle.label, StringComparer.OrdinalIgnoreCase)
      .Take(10)
      .ToList();
    for (var i = 0; i < topBundles.Count; i++) {
      var bundle = topBundles[i];
      Debug.Log(
        "[SpriteIndexBuilder] [" + contextLabel + "][BuildDiag] BundleCandidate#" + (i + 1) +
        " label='" + bundle.label + "'" +
        " approxSource=" + FormatByteCount(bundle.approxSourceBytes) +
        " entries=" + bundle.entryCount +
        " files=" + bundle.fileCount +
        " folders=" + bundle.folderEntryCount +
        " largestAsset='" + bundle.largestAssetPath + "'" +
        " largestAssetSize=" + FormatByteCount(bundle.largestAssetBytes)
      );
    }

    var topAssets = assetDiagnostics
      .OrderByDescending(asset => asset.approxSourceBytes)
      .ThenBy(asset => asset.assetPath, StringComparer.OrdinalIgnoreCase)
      .Take(10)
      .ToList();
    for (var i = 0; i < topAssets.Count; i++) {
      var asset = topAssets[i];
      Debug.Log(
        "[SpriteIndexBuilder] [" + contextLabel + "][BuildDiag] AssetCandidate#" + (i + 1) +
        " bundle='" + asset.bundleLabel + "'" +
        " approxSource=" + FormatByteCount(asset.approxSourceBytes) +
        " assetPath='" + asset.assetPath + "'"
      );
    }
  }

  static bool ValidateAddressablesPackedBuildSpriteSliceRisk(AddressableAssetSettings settings, string contextLabel) {
    if (settings == null) return false;

    var textureGroups = GetManagedTextureGroups(settings).ToList();
    if (textureGroups.Count == 0) return true;

    var seenAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var diagnostics = new List<SpriteSliceBuildDiagnostic>();
    long totalSpriteSlices = 0;

    for (var groupIndex = 0; groupIndex < textureGroups.Count; groupIndex++) {
      var textureGroup = textureGroups[groupIndex];
      if (textureGroup == null) continue;

      foreach (var entry in textureGroup.entries) {
        if (entry == null) continue;

        var assetPath = NormalizePath(AssetDatabase.GUIDToAssetPath(entry.guid));
        if (string.IsNullOrWhiteSpace(assetPath)) {
          assetPath = NormalizePath(entry.AssetPath);
        }
        if (string.IsNullOrWhiteSpace(assetPath) || !seenAssetPaths.Add(assetPath)) continue;
        if (!TryGetEstimatedSpriteSliceCount(assetPath, out var spriteCount)) continue;

        totalSpriteSlices += spriteCount;
        var bundleLabel = GetSyntheticTextureBundleLabel(entry, out _);
        var approxSourceBytes = GetApproxAssetSourceBytes(assetPath, out _, out _);
        diagnostics.Add(new SpriteSliceBuildDiagnostic(assetPath, bundleLabel, spriteCount, approxSourceBytes));
      }
    }

    if (diagnostics.Count == 0) return true;

    var heavyAtlasCount = diagnostics.Count(diagnostic => diagnostic.spriteCount > BuilderConfig.MaxSpriteCountForEditorLocalIdSupplement);
    var maxAtlasSliceCount = diagnostics.Max(diagnostic => diagnostic.spriteCount);

    Debug.Log(
      "[SpriteIndexBuilder] [" + contextLabel + "][BuildRisk] Packed build sprite slice estimate." +
      " atlasAssets=" + diagnostics.Count +
      " spriteSlices=" + totalSpriteSlices +
      " heavyAtlases=" + heavyAtlasCount +
      " maxAtlasSlices=" + maxAtlasSliceCount +
      " threshold=" + BuilderConfig.MaxEstimatedSpriteSlicesForPackedBuild
    );

    var topDiagnostics = diagnostics
      .OrderByDescending(diagnostic => diagnostic.spriteCount)
      .ThenBy(diagnostic => diagnostic.assetPath, StringComparer.OrdinalIgnoreCase)
      .Take(BuilderConfig.MaxLoggedSpriteSliceRiskCandidates)
      .ToList();
    for (var i = 0; i < topDiagnostics.Count; i++) {
      var diagnostic = topDiagnostics[i];
      Debug.Log(
        "[SpriteIndexBuilder] [" + contextLabel + "][BuildRisk] SliceCandidate#" + (i + 1) +
        " bundle='" + diagnostic.bundleLabel + "'" +
        " spriteCount=" + diagnostic.spriteCount +
        " approxSource=" + FormatByteCount(diagnostic.approxSourceBytes) +
        " assetPath='" + diagnostic.assetPath + "'"
      );
    }

    if (totalSpriteSlices <= BuilderConfig.MaxEstimatedSpriteSlicesForPackedBuild) return true;

    Debug.LogError(
      "[SpriteIndexBuilder] [" + contextLabel + "][BuildRisk] Aborting packed Addressables build because estimated sprite slice fan-out " +
      totalSpriteSlices + " exceeds safety threshold " + BuilderConfig.MaxEstimatedSpriteSlicesForPackedBuild +
      ". The stock packed SBP path still enumerates sprite subobjects for every atlas in this content set."
    );
    return false;
  }

  static bool TryGetEstimatedSpriteSliceCount(string assetPath, out int spriteCount) {
    spriteCount = 0;
    var normalizedAssetPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath)) return false;

    if (cachedSpriteSliceCountsByAssetPath.TryGetValue(normalizedAssetPath, out spriteCount)) {
      return true;
    }

    var extension = Path.GetExtension(normalizedAssetPath);
    if (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    if (AssetImporter.GetAtPath(normalizedAssetPath) is TextureImporter importer &&
        importer.spriteImportMode == SpriteImportMode.Single) {
      spriteCount = 1;
      cachedSpriteSliceCountsByAssetPath[normalizedAssetPath] = spriteCount;
      return true;
    }

    var metaPath = normalizedAssetPath + ".meta";
    if (!File.Exists(metaPath)) return false;

    var map = new Dictionary<long, string>();
    var parsedAny = false;
    if (TryParseSpriteSheetInternalIdTable(metaPath, map, out _)) {
      parsedAny = true;
    }
    if (TryParseNameFileIdTable(metaPath, map, out _)) {
      parsedAny = true;
    }
    if (!parsedAny) return false;

    spriteCount = Math.Max(map.Count, 1);
    cachedSpriteSliceCountsByAssetPath[normalizedAssetPath] = spriteCount;
    return true;
  }

  static void LogAddressablesBuildMemorySnapshot(string contextLabel, string phase) {
    var managedBytes = GC.GetTotalMemory(false);
    var monoUsedBytes = Profiler.GetMonoUsedSizeLong();
    var monoHeapBytes = Profiler.GetMonoHeapSizeLong();
    var totalAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong();
    var totalReservedBytes = Profiler.GetTotalReservedMemoryLong();
    var totalUnusedReservedBytes = Profiler.GetTotalUnusedReservedMemoryLong();

    long processPrivateBytes = 0;
    long processWorkingSetBytes = 0;
    try {
      using (var process = System.Diagnostics.Process.GetCurrentProcess()) {
        process.Refresh();
        processPrivateBytes = process.PrivateMemorySize64;
        processWorkingSetBytes = process.WorkingSet64;
      }
    }
    catch {
      processPrivateBytes = 0;
      processWorkingSetBytes = 0;
    }

    Debug.Log(
      "[SpriteIndexBuilder] [" + contextLabel + "][BuildMemory] phase='" + phase + "'" +
      " managed=" + FormatByteCount(managedBytes) +
      " monoUsed=" + FormatByteCount(monoUsedBytes) +
      " monoHeap=" + FormatByteCount(monoHeapBytes) +
      " totalAllocated=" + FormatByteCount(totalAllocatedBytes) +
      " totalReserved=" + FormatByteCount(totalReservedBytes) +
      " totalUnusedReserved=" + FormatByteCount(totalUnusedReservedBytes) +
      " processPrivate=" + FormatByteCount(processPrivateBytes) +
      " processWorkingSet=" + FormatByteCount(processWorkingSetBytes)
    );
  }

  static void ReleaseEditorBuildPrepMemory(string contextLabel) {
    Debug.Log("[SpriteIndexBuilder] [" + contextLabel + "][BuildMemory] Releasing editor build prep memory before Addressables build.");
    LogAddressablesBuildMemorySnapshot(contextLabel, "before_editor_unload");
    EditorUtility.UnloadUnusedAssetsImmediate();
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    LogAddressablesBuildMemorySnapshot(contextLabel, "after_editor_unload");
  }

  static void LogNormalAddressSummary(string contextLabel, BuildState state) {
    if (state == null) return;
    if (state.missingNormalLibraryCount <= 0 &&
        state.autoDerivedNormalAddressCount <= 0 &&
        state.missingNormalAddressCount <= 0) {
      return;
    }

    Debug.Log(
      "[SpriteIndexBuilder] [" + contextLabel + "] Normal address automation." +
      " missingNormalLibraries=" + state.missingNormalLibraryCount +
      " autoDerivedNormals=" + state.autoDerivedNormalAddressCount +
      " fallbackFlatNormals=" + state.missingNormalAddressCount
    );
  }

  static void LogLocalIdSupplementSummary(string contextLabel, BuildState state) {
    if (state == null) return;
    if (state.supplementedLocalIdAtlasCount <= 0 &&
        state.skippedHeavyLocalIdSupplementAtlasCount <= 0) {
      return;
    }

    Debug.Log(
      "[SpriteIndexBuilder] [" + contextLabel + "] Editor local ID supplement." +
      " threshold=" + BuilderConfig.MaxSpriteCountForEditorLocalIdSupplement +
      " supplementedAtlases=" + state.supplementedLocalIdAtlasCount +
      " supplementedIds=" + state.supplementedLocalIdCount +
      " skippedHeavyAtlases=" + state.skippedHeavyLocalIdSupplementAtlasCount +
      " skippedHeavySprites=" + state.skippedHeavyLocalIdSupplementSpriteCount
    );
  }

  static void LogGroupedAtlasBuildSurrogateSummary(string contextLabel, BuildState state) {
    if (state == null) return;
    if (state.groupedAtlasSurrogateCount <= 0 && state.groupedAtlasSurrogateCopyCount <= 0) return;

    Debug.Log(
      "[SpriteIndexBuilder] [" + contextLabel + "] Grouped atlas build surrogates." +
      " activeSurrogates=" + state.groupedAtlasSurrogateCount +
      " copiedOrUpdated=" + state.groupedAtlasSurrogateCopyCount +
      " surrogateRoot='" + BuilderConfig.GroupedAtlasBuildSurrogateRootFolder + "'"
    );
  }

  static string ResolveRuntimeTextureAssetPathForBuild(BuildState state, string sourceAssetPath) {
    var normalizedSourceAssetPath = NormalizePath(sourceAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedSourceAssetPath)) return "";
    if (state != null &&
        state.runtimeTextureAssetPathBySourceAssetPath.TryGetValue(normalizedSourceAssetPath, out var cachedRuntimeAssetPath) &&
        !string.IsNullOrWhiteSpace(cachedRuntimeAssetPath)) {
      return cachedRuntimeAssetPath;
    }

    var runtimeAssetPath = normalizedSourceAssetPath;
    if (GeneratedAtlasBuildSurrogateUtility.TryBuildSurrogatePath(normalizedSourceAssetPath, out var surrogateAtlasAssetPath)) {
      if (EnsureGroupedAtlasBuildSurrogate(normalizedSourceAssetPath, surrogateAtlasAssetPath, out var copiedAny)) {
        runtimeAssetPath = surrogateAtlasAssetPath;
        if (state != null) {
          state.groupedAtlasSurrogateCount++;
          if (copiedAny) {
            state.groupedAtlasSurrogateCopyCount++;
          }
        }
      }
    }

    if (string.Equals(runtimeAssetPath, normalizedSourceAssetPath, StringComparison.OrdinalIgnoreCase)) {
      var stagedCoreAssetPath = TryResolveExistingCoreStageTextureAssetPath(normalizedSourceAssetPath);
      if (!string.IsNullOrWhiteSpace(stagedCoreAssetPath)) {
        runtimeAssetPath = stagedCoreAssetPath;
      }
    }

    if (state != null) {
      state.runtimeTextureAssetPathBySourceAssetPath[normalizedSourceAssetPath] = runtimeAssetPath;
    }
    return runtimeAssetPath;
  }

  static string TryResolveExistingCoreStageTextureAssetPath(string sourceAssetPath) {
    var normalizedSourceAssetPath = NormalizePath(sourceAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedSourceAssetPath)) return "";
    if (!normalizedSourceAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return "";
    if (GeneratedAtlasBuildSurrogateUtility.IsBuildSurrogatePath(normalizedSourceAssetPath) ||
        GeneratedAtlasBuildSurrogateUtility.IsContentStagePath(normalizedSourceAssetPath)) {
      return normalizedSourceAssetPath;
    }

    var stagedCoreAssetPath = NormalizePath(
      ContentPackPipeline.StageCoreAssetPath + "/" + normalizedSourceAssetPath.Substring("Assets/".Length)
    );
    return string.IsNullOrWhiteSpace(AssetDatabase.AssetPathToGUID(stagedCoreAssetPath))
      ? ""
      : stagedCoreAssetPath;
  }

  static bool EnsureGroupedAtlasBuildSurrogate(string sourceAtlasAssetPath, string surrogateAtlasAssetPath, out bool copiedAny) {
    copiedAny = false;
    var normalizedSourceAtlasAssetPath = NormalizePath(sourceAtlasAssetPath);
    var normalizedSurrogateAtlasAssetPath = NormalizePath(surrogateAtlasAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedSourceAtlasAssetPath) ||
        string.IsNullOrWhiteSpace(normalizedSurrogateAtlasAssetPath)) {
      return false;
    }

    var sourceMetadataAssetPath = GeneratedAtlasBuildSurrogateUtility.BuildMetadataAssetPath(normalizedSourceAtlasAssetPath);
    var surrogateMetadataAssetPath = GeneratedAtlasBuildSurrogateUtility.BuildMetadataAssetPath(normalizedSurrogateAtlasAssetPath);
    if (string.IsNullOrWhiteSpace(sourceMetadataAssetPath) || string.IsNullOrWhiteSpace(surrogateMetadataAssetPath)) {
      return false;
    }

    if (!CopyAssetFileIfChanged(normalizedSourceAtlasAssetPath, normalizedSurrogateAtlasAssetPath, out var copiedAtlas)) {
      Debug.LogWarning(
        "[SpriteIndexBuilder] Failed to prepare grouped atlas build surrogate." +
        " sourceAtlas='" + normalizedSourceAtlasAssetPath + "'" +
        " surrogateAtlas='" + normalizedSurrogateAtlasAssetPath + "'"
      );
      return false;
    }
    copiedAny |= copiedAtlas;

    if (!CopyAssetFileIfChanged(sourceMetadataAssetPath, surrogateMetadataAssetPath, out var copiedMetadata)) {
      Debug.LogWarning(
        "[SpriteIndexBuilder] Failed to prepare grouped atlas surrogate metadata." +
        " sourceMetadata='" + sourceMetadataAssetPath + "'" +
        " surrogateMetadata='" + surrogateMetadataAssetPath + "'"
      );
      return false;
    }
    copiedAny |= copiedMetadata;

    if (copiedAny) {
      AssetDatabase.ImportAsset(surrogateMetadataAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
      AssetDatabase.ImportAsset(normalizedSurrogateAtlasAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
    }

    return !string.IsNullOrWhiteSpace(AssetDatabase.AssetPathToGUID(normalizedSurrogateAtlasAssetPath));
  }

  static bool CopyAssetFileIfChanged(string sourceAssetPath, string targetAssetPath, out bool copied) {
    copied = false;
    var normalizedSourceAssetPath = NormalizePath(sourceAssetPath);
    var normalizedTargetAssetPath = NormalizePath(targetAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedSourceAssetPath) || string.IsNullOrWhiteSpace(normalizedTargetAssetPath)) {
      return false;
    }

    var sourceFullPath = Path.GetFullPath(normalizedSourceAssetPath);
    if (!File.Exists(sourceFullPath)) {
      return false;
    }

    var targetFullPath = Path.GetFullPath(normalizedTargetAssetPath);
    var targetDirectory = Path.GetDirectoryName(targetFullPath);
    if (string.IsNullOrWhiteSpace(targetDirectory)) {
      return false;
    }
    Directory.CreateDirectory(targetDirectory);

    var shouldCopy = !File.Exists(targetFullPath);
    if (!shouldCopy) {
      var sourceInfo = new FileInfo(sourceFullPath);
      var targetInfo = new FileInfo(targetFullPath);
      shouldCopy = sourceInfo.Length != targetInfo.Length ||
                   sourceInfo.LastWriteTimeUtc != targetInfo.LastWriteTimeUtc;
    }

    if (!shouldCopy) {
      return true;
    }

    File.Copy(sourceFullPath, targetFullPath, overwrite: true);
    File.SetLastWriteTimeUtc(targetFullPath, File.GetLastWriteTimeUtc(sourceFullPath));
    copied = true;
    return true;
  }

  static void LogSkippedColorRowSummary(string contextLabel, BuildState state) {
    if (state == null) return;
    if (state.skippedColorRowCount <= 0 && state.skippedColorLibraryCount <= 0) return;

    var summary = "[SpriteIndexBuilder] [" + contextLabel + "] Skipped unresolved color sprite references." +
      " skippedRows=" + state.skippedColorRowCount +
      " skippedLibraries=" + state.skippedColorLibraryCount;
    if (state.skippedColorLibrarySummaries.Count > 0) {
      summary += " samples=" + string.Join(" | ", state.skippedColorLibrarySummaries);
    }

    Debug.LogWarning(summary);
  }

  static void TrackSkippedColorLibrarySummary(
    BuildState state,
    string libraryName,
    string requestedLibraryName,
    int skippedRows,
    int totalRows
  ) {
    if (state == null || state.skippedColorLibrarySummaries == null) return;
    if (state.skippedColorLibrarySummaries.Count >= 8) return;

    state.skippedColorLibrarySummaries.Add(
      "library='" + (libraryName ?? "") + "'" +
      " requested='" + (requestedLibraryName ?? "") + "'" +
      " skippedRows=" + skippedRows +
      "/" + totalRows
    );
  }

  static void LogAtlasMetadataSummary(string contextLabel, BuildState state) {
    if (state == null || state.activeAtlasMetadataAssetPaths == null) return;
    var metadataCount = state.activeAtlasMetadataAssetPaths.Count;
    if (metadataCount <= 0) return;

    var groupedAtlasCount = CountGroupedAtlasTextureAssetPaths(state, state.activeTextureAssetPaths);

    Debug.Log(
      "[SpriteIndexBuilder] [" + contextLabel + "] Atlas metadata entries synced." +
      " metadataAssets=" + metadataCount +
      " streamedAtlases=" + state.activeTextureAssetPaths.Count +
      " groupedAtlasesReferenced=" + groupedAtlasCount
    );

    if (groupedAtlasCount <= 0 && ProjectHasGroupedAtlasMetadataAssets(state)) {
      Debug.LogWarning(
        "[SpriteIndexBuilder] [" + contextLabel + "] Grouped atlas metadata was found in the project, but the rebuilt runtime index referenced zero grouped atlases." +
        " If you expect grouped atlas streaming, rebind the sprite libraries first and rebuild the runtime index again."
      );
    }
  }

  static void EnsureAddressableTextureEntry(BuildState state, string assetPath) {
    if (state == null || state.addressables == null) return;
    var normalizedSourceAssetPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedSourceAssetPath)) return;
    var normalizedAssetPath = ResolveRuntimeTextureAssetPathForBuild(state, normalizedSourceAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath)) {
      normalizedAssetPath = normalizedSourceAssetPath;
    }

    if (!state.activeTextureAssetPaths.Add(normalizedAssetPath)) {
      return;
    }

    var guid = AssetDatabase.AssetPathToGUID(normalizedAssetPath);
    if (string.IsNullOrWhiteSpace(guid)) return;

    var syntheticLabel = BuildSyntheticTextureBundleLabel(normalizedSourceAssetPath);
    var targetGroupName = BuildManagedTextureGroupName(syntheticLabel);
    var targetGroup = GetOrCreateManagedTextureGroup(state, targetGroupName);
    if (targetGroup == null) return;
    state.activeTextureGroupNames.Add(targetGroup.Name);

    var changed = false;
    var entry = state.addressables.FindAssetEntry(guid);
    if (entry == null || entry.parentGroup != targetGroup) {
      entry = state.addressables.CreateOrMoveEntry(guid, targetGroup, false, false);
      changed = entry != null;
    }
    if (entry == null) return;

    if (!string.Equals(entry.address, normalizedAssetPath, StringComparison.Ordinal)) {
      entry.SetAddress(normalizedAssetPath, false);
      changed = true;
    }

    if (!string.IsNullOrWhiteSpace(syntheticLabel)) {
      state.addressables.AddLabel(syntheticLabel, false);
      if (ApplySyntheticBundleLabel(entry, syntheticLabel)) {
        changed = true;
      }
      state.syntheticTextureLabelAssignments++;
      if (!state.syntheticTextureLabelCounts.TryGetValue(syntheticLabel, out var count)) count = 0;
      state.syntheticTextureLabelCounts[syntheticLabel] = count + 1;
    }

    if (changed) {
      EditorUtility.SetDirty(targetGroup);
      EditorUtility.SetDirty(state.addressables);
    }

    EnsureAtlasMetadataEntry(state, normalizedAssetPath);
  }

  static void EnsureAtlasMetadataEntry(BuildState state, string atlasAssetPath) {
    if (state == null) return;
    if (!TryGetAtlasMetadataAssetPath(state, atlasAssetPath, out var metadataAssetPath)) return;

    state.activeAtlasMetadataAssetPaths.Add(metadataAssetPath);
    if (state.addressables == null || state.indexGroup == null) return;

    EnsureAddressableEntry(state.addressables, state.indexGroup, metadataAssetPath, metadataAssetPath);
    ApplyManagedLabel(state.addressables, state.indexGroup, metadataAssetPath, BuilderConfig.AtlasMetadataAddressablesLabel);
  }

  static bool TryGetAtlasMetadataAssetPath(BuildState state, string atlasAssetPath, out string metadataAssetPath) {
    metadataAssetPath = "";
    var normalizedAtlasPath = NormalizePath(atlasAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedAtlasPath)) return false;

    if (state != null && state.atlasMetadataAssetPathByAtlasPath.TryGetValue(normalizedAtlasPath, out var cachedMetadataAssetPath)) {
      metadataAssetPath = cachedMetadataAssetPath ?? "";
      return !string.IsNullOrWhiteSpace(metadataAssetPath);
    }

    var candidateAssetPath = NormalizePath(Path.ChangeExtension(normalizedAtlasPath, ".json"));
    if (!LooksLikeAtlasMetadataAssetPath(state, candidateAssetPath)) {
      if (state != null) {
        state.atlasMetadataAssetPathByAtlasPath[normalizedAtlasPath] = "";
      }
      return false;
    }

    metadataAssetPath = candidateAssetPath;
    if (state != null) {
      state.atlasMetadataAssetPathByAtlasPath[normalizedAtlasPath] = metadataAssetPath;
    }
    return true;
  }

  static bool LooksLikeAtlasMetadataAssetPath(BuildState state, string assetPath) {
    return GetAtlasMetadataKind(state, assetPath) != AtlasMetadataKind.Invalid;
  }

  static int CountGroupedAtlasTextureAssetPaths(BuildState state, HashSet<string> textureAssetPaths) {
    if (textureAssetPaths == null || textureAssetPaths.Count <= 0) return 0;

    var groupedCount = 0;
    foreach (var textureAssetPath in textureAssetPaths) {
      if (!TryGetAtlasMetadataAssetPath(state, textureAssetPath, out var metadataAssetPath)) continue;
      if (GetAtlasMetadataKind(state, metadataAssetPath) != AtlasMetadataKind.Grouped) continue;
      groupedCount++;
    }

    return groupedCount;
  }

  static bool ProjectHasGroupedAtlasMetadataAssets(BuildState state) {
    if (state == null) return false;
    if (state.projectGroupedMetadataScanCompleted) {
      return state.projectHasGroupedMetadataAssets;
    }

    state.projectGroupedMetadataScanCompleted = true;
    var searchRoots = ContentPackPipeline.GetTextureSearchRoots();
    for (var rootIndex = 0; rootIndex < searchRoots.Count; rootIndex++) {
      var sourceRoot = NormalizePath(searchRoots[rootIndex]);
      if (string.IsNullOrWhiteSpace(sourceRoot)) continue;

      var fullSourceRoot = Path.GetFullPath(sourceRoot);
      if (!Directory.Exists(fullSourceRoot)) continue;

      foreach (var metadataFullPath in Directory.EnumerateFiles(fullSourceRoot, "*.json", SearchOption.AllDirectories)) {
        var normalizedAssetPath = NormalizePath(metadataFullPath);
        if (GetAtlasMetadataKind(state, normalizedAssetPath) != AtlasMetadataKind.Grouped) continue;
        state.projectHasGroupedMetadataAssets = true;
        break;
      }

      if (state.projectHasGroupedMetadataAssets) {
        break;
      }
    }

    return state.projectHasGroupedMetadataAssets;
  }

  static bool LooksLikeGroupedAtlasMetadataAssetPath(BuildState state, string assetPath) {
    return GetAtlasMetadataKind(state, assetPath) == AtlasMetadataKind.Grouped;
  }

  static AtlasMetadataKind GetAtlasMetadataKind(BuildState state, string assetPath) {
    var normalizedAssetPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath)) return AtlasMetadataKind.Invalid;
    if (state != null && state.atlasMetadataKindByPath.TryGetValue(normalizedAssetPath, out var cachedKind)) {
      return cachedKind;
    }

    var metadataKind = AtlasMetadataKind.Invalid;
    if (normalizedAssetPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
      var surrogateRoot = NormalizePath(BuilderConfig.GroupedAtlasBuildSurrogateRootFolder);
      var underKnownAtlasRoot = false;
      var searchRoots = ContentPackPipeline.GetTextureSearchRoots();
      for (var rootIndex = 0; rootIndex < searchRoots.Count; rootIndex++) {
        var sourceRoot = NormalizePath(searchRoots[rootIndex]);
        if (string.IsNullOrWhiteSpace(sourceRoot)) continue;
        if (!normalizedAssetPath.StartsWith(sourceRoot + "/", StringComparison.OrdinalIgnoreCase)) continue;
        underKnownAtlasRoot = true;
        break;
      }

      if (!underKnownAtlasRoot &&
          !string.IsNullOrWhiteSpace(surrogateRoot) &&
          normalizedAssetPath.StartsWith(surrogateRoot + "/", StringComparison.OrdinalIgnoreCase)) {
        underKnownAtlasRoot = true;
      }
      if (underKnownAtlasRoot) {
        var fullPath = Path.GetFullPath(normalizedAssetPath);
        if (File.Exists(fullPath)) {
          try {
            var jsonText = File.ReadAllText(fullPath);
            if (!string.IsNullOrWhiteSpace(jsonText) &&
                jsonText.IndexOf("\"sprites\"", StringComparison.Ordinal) >= 0 &&
                jsonText.IndexOf("\"offsetFromCellCenterPx\"", StringComparison.Ordinal) >= 0 &&
                HasMeaningfulRuntimeOffsetMetadata(jsonText)) {
              metadataKind = IsGroupedOffsetMetadataAssetPath(normalizedAssetPath)
                ? AtlasMetadataKind.Grouped
                : AtlasMetadataKind.OffsetOnly;
            }
          }
          catch {
            metadataKind = AtlasMetadataKind.Invalid;
          }
        }
      }
    }

    if (state != null) {
      state.atlasMetadataKindByPath[normalizedAssetPath] = metadataKind;
    }
    return metadataKind;
  }

  static bool IsGroupedOffsetMetadataAssetPath(string metadataAssetPath) {
    var normalizedMetadataAssetPath = NormalizePath(metadataAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedMetadataAssetPath)) return false;
    if (GeneratedAtlasBuildSurrogateUtility.IsBuildSurrogatePath(normalizedMetadataAssetPath)) return true;
    return GeneratedAtlasBuildSurrogateUtility.IsGroupedGearAtlasPath(normalizedMetadataAssetPath);
  }

  static bool HasMeaningfulRuntimeOffsetMetadata(string jsonText) {
    if (string.IsNullOrWhiteSpace(jsonText)) return false;

    OffsetOnlyAtlasMetadataPayload payload;
    try {
      payload = JsonUtility.FromJson<OffsetOnlyAtlasMetadataPayload>(jsonText);
    }
    catch {
      return false;
    }

    if (payload?.sprites == null || payload.sprites.Count <= 0) return false;
    for (var i = 0; i < payload.sprites.Count; i++) {
      var sprite = payload.sprites[i];
      if (Mathf.Abs(sprite.offsetFromCellCenterPx.x) > 0.001f ||
          Mathf.Abs(sprite.offsetFromCellCenterPx.y) > 0.001f) {
        return true;
      }
    }

    return false;
  }

  static bool ApplySyntheticBundleLabel(AddressableAssetEntry entry, string syntheticLabel) {
    if (entry == null || string.IsNullOrWhiteSpace(syntheticLabel)) return false;
    if (entry.labels == null) return false;

    var changed = false;
    var remove = new List<string>();
    foreach (var label in entry.labels) {
      if (string.IsNullOrWhiteSpace(label)) continue;
      if (!label.StartsWith(BuilderConfig.SyntheticTextureLabelPrefix, StringComparison.OrdinalIgnoreCase)) continue;
      if (string.Equals(label, syntheticLabel, StringComparison.OrdinalIgnoreCase)) continue;
      remove.Add(label);
    }

    for (var i = 0; i < remove.Count; i++) {
      entry.SetLabel(remove[i], false, false, false);
      changed = true;
    }

    if (!HasAddressableLabel(entry, syntheticLabel)) {
      entry.SetLabel(syntheticLabel, true, true, false);
      changed = true;
    }

    return changed;
  }

  static void ApplyManagedLabel(AddressableAssetSettings settings, AddressableAssetGroup group, string assetPath, string label) {
    if (settings == null || group == null || string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(label)) return;
    var guid = AssetDatabase.AssetPathToGUID(assetPath);
    if (string.IsNullOrWhiteSpace(guid)) return;

    var entry = settings.FindAssetEntry(guid);
    if (entry == null || entry.parentGroup != group) return;

    settings.AddLabel(label, false);
    entry.SetLabel(label, true, true, false);
  }

  static bool HasAddressableLabel(AddressableAssetEntry entry, string label) {
    if (entry == null || string.IsNullOrWhiteSpace(label) || entry.labels == null) return false;
    foreach (var existing in entry.labels) {
      if (string.Equals(existing, label, StringComparison.OrdinalIgnoreCase)) return true;
    }
    return false;
  }

  static string GetSyntheticTextureBundleLabel(AddressableAssetEntry entry, out int syntheticLabelCount) {
    syntheticLabelCount = 0;
    if (entry == null || entry.labels == null) return "";

    string firstMatch = "";
    foreach (var existing in entry.labels) {
      if (string.IsNullOrWhiteSpace(existing)) continue;
      if (!existing.StartsWith(BuilderConfig.SyntheticTextureLabelPrefix, StringComparison.OrdinalIgnoreCase)) continue;
      syntheticLabelCount++;
      if (string.IsNullOrWhiteSpace(firstMatch)) {
        firstMatch = existing;
      }
    }

    return firstMatch;
  }

  static long GetApproxAssetSourceBytes(string assetPath, out int fileCount, out bool isFolderEntry) {
    fileCount = 0;
    isFolderEntry = false;

    var fullPath = GetProjectAssetFullPath(assetPath);
    if (string.IsNullOrWhiteSpace(fullPath)) return 0;

    if (File.Exists(fullPath)) {
      fileCount = 1;
      try {
        return new FileInfo(fullPath).Length;
      }
      catch {
        return 0;
      }
    }

    if (!Directory.Exists(fullPath)) return 0;

    isFolderEntry = true;
    var totalBytes = 0L;
    try {
      var files = Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories);
      for (var i = 0; i < files.Length; i++) {
        var normalized = NormalizePath(files[i]);
        if (normalized.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
        fileCount++;
        totalBytes += new FileInfo(files[i]).Length;
      }
    }
    catch {
      return totalBytes;
    }

    return totalBytes;
  }

  static string BuildSyntheticTextureBundleLabel(string assetPath) {
    var normalizedAssetPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath)) return BuilderConfig.SyntheticTextureLabelPrefix + "misc";

    var folderPath = NormalizePath(Path.GetDirectoryName(normalizedAssetPath));
    if (string.IsNullOrWhiteSpace(folderPath)) return BuilderConfig.SyntheticTextureLabelPrefix + "misc";

    string relativeFolder;
    const string charactersRoot = "Assets/Sprites/Characters/";
    const string spritesRoot = "Assets/Sprites/";
    const string assetsRoot = "Assets/";

    if (folderPath.StartsWith(charactersRoot, StringComparison.OrdinalIgnoreCase)) {
      relativeFolder = folderPath.Substring(charactersRoot.Length);
    } else if (folderPath.StartsWith(spritesRoot, StringComparison.OrdinalIgnoreCase)) {
      relativeFolder = folderPath.Substring(spritesRoot.Length);
    } else if (folderPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase)) {
      relativeFolder = folderPath.Substring(assetsRoot.Length);
    } else {
      relativeFolder = folderPath;
    }

    var maxSegments = GetSyntheticTextureLabelDepth(normalizedAssetPath);
    relativeFolder = LimitSyntheticLabelDepth(relativeFolder, maxSegments);
    var sanitized = SanitizeSyntheticLabelToken(relativeFolder);
    if (string.IsNullOrWhiteSpace(sanitized)) sanitized = "misc";
    return BuilderConfig.SyntheticTextureLabelPrefix + sanitized;
  }

  static int GetSyntheticTextureLabelDepth(string normalizedAssetPath) {
    if (string.IsNullOrWhiteSpace(normalizedAssetPath)) return BuilderConfig.SyntheticTextureLabelFolderDepth;

    return GeneratedAtlasBuildSurrogateUtility.IsGroupedGearAtlasPath(normalizedAssetPath)
      ? BuilderConfig.GroupedGearSyntheticTextureLabelFolderDepth
      : BuilderConfig.SyntheticTextureLabelFolderDepth;
  }

  static string LimitSyntheticLabelDepth(string relativeFolder, int maxSegments) {
    if (string.IsNullOrWhiteSpace(relativeFolder)) return "";
    if (maxSegments <= 0) return relativeFolder;

    var normalized = relativeFolder.Replace('\\', '/').Trim('/');
    if (string.IsNullOrWhiteSpace(normalized)) return "";

    var parts = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length <= maxSegments) return normalized;

    return string.Join("/", parts.Take(maxSegments));
  }

  static string SanitizeSyntheticLabelToken(string value) {
    if (string.IsNullOrWhiteSpace(value)) return "";

    var normalized = value.Replace('\\', '/').Trim();
    var sb = new StringBuilder(normalized.Length);
    var previousUnderscore = false;
    for (var i = 0; i < normalized.Length; i++) {
      var ch = char.ToLowerInvariant(normalized[i]);
      if (char.IsLetterOrDigit(ch)) {
        sb.Append(ch);
        previousUnderscore = false;
        continue;
      }

      if (previousUnderscore) continue;
      sb.Append('_');
      previousUnderscore = true;
    }

    var output = sb.ToString().Trim('_');
    return string.IsNullOrWhiteSpace(output) ? "" : output;
  }

  static void EnsureAddressableEntry(AddressableAssetSettings settings, AddressableAssetGroup group, string assetPath, string address) {
    var normalizedAssetPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath)) return;

    var guid = AssetDatabase.AssetPathToGUID(normalizedAssetPath);
    if (string.IsNullOrWhiteSpace(guid) && File.Exists(normalizedAssetPath)) {
      AssetDatabase.ImportAsset(normalizedAssetPath, ImportAssetOptions.ForceSynchronousImport);
      guid = AssetDatabase.AssetPathToGUID(normalizedAssetPath);
    }

    if (string.IsNullOrWhiteSpace(guid)) {
      Debug.LogWarning(
        "[SpriteIndexBuilder] Skipped Addressables registration because the asset is not imported." +
        " assetPath='" + normalizedAssetPath + "'" +
        " address='" + address + "'"
      );
      return;
    }

    var changed = false;
    var entry = settings.FindAssetEntry(guid);
    if (entry == null || entry.parentGroup != group) {
      entry = settings.CreateOrMoveEntry(guid, group, false, false);
      changed = entry != null;
    }
    if (entry == null) return;

    if (!string.Equals(entry.address, address, StringComparison.Ordinal)) {
      entry.SetAddress(address, false);
      changed = true;
    }

    if (changed) {
      if (group != null) {
        EditorUtility.SetDirty(group);
      }
      EditorUtility.SetDirty(settings);
    }
  }

  static bool TryReadScalar(string trimmedLine, string fieldName, out string value) {
    var prefix = fieldName + ":";
    if (!trimmedLine.StartsWith(prefix, StringComparison.Ordinal)) {
      value = "";
      return false;
    }

    value = DecodeScalar(trimmedLine.Substring(prefix.Length));
    return true;
  }

  static bool TryReadFileIdReference(string trimmedLine, string fieldName, out string fileId) {
    fileId = "";
    var prefix = fieldName + ":";
    if (!trimmedLine.StartsWith(prefix, StringComparison.Ordinal)) {
      return false;
    }

    var fileIdToken = "fileID:";
    var fileIdIndex = trimmedLine.IndexOf(fileIdToken, StringComparison.Ordinal);
    if (fileIdIndex < 0) return false;

    var startIndex = fileIdIndex + fileIdToken.Length;
    var endIndex = trimmedLine.IndexOfAny(new[] { ',', '}' }, startIndex);
    if (endIndex < 0) {
      endIndex = trimmedLine.Length;
    }

    fileId = trimmedLine.Substring(startIndex, endIndex - startIndex).Trim();
    return !string.IsNullOrWhiteSpace(fileId);
  }

  static bool TryReadSerializedObjectHeader(string line, out int classId, out string fileId) {
    classId = 0;
    fileId = "";
    if (string.IsNullOrWhiteSpace(line)) return false;

    var match = serializedObjectHeaderRegex.Match(line);
    if (!match.Success) return false;
    if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out classId)) {
      classId = 0;
      return false;
    }

    fileId = match.Groups[2].Value.Trim();
    return !string.IsNullOrWhiteSpace(fileId);
  }

  static string DecodeScalar(string value) {
    if (string.IsNullOrWhiteSpace(value)) return "";
    var trimmed = value.Trim();
    if (trimmed.Length >= 2) {
      var first = trimmed[0];
      var last = trimmed[trimmed.Length - 1];
      if ((first == '"' && last == '"') || (first == '\'' && last == '\'')) {
        trimmed = trimmed.Substring(1, trimmed.Length - 2);
        if (first == '\'') {
          // YAML single-quoted scalars escape apostrophes as doubled single-quotes.
          trimmed = trimmed.Replace("''", "'");
        }
      }
    }
    return trimmed;
  }

  static void ParseLabel(string label, out string labelPrefix, out int frame) {
    labelPrefix = label ?? "";
    frame = 0;
    if (string.IsNullOrWhiteSpace(label)) return;

    if (int.TryParse(label, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericOnly)) {
      frame = numericOnly;
      labelPrefix = "";
      return;
    }

    var match = labelFrameRegex.Match(label);
    if (!match.Success) return;

    labelPrefix = match.Groups[1].Value;
    if (!int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out frame)) {
      frame = 0;
    }
  }

  static void WriteIfChanged(string path, string contents) {
    path = NormalizePath(path);
    EnsureFolderExists(Path.GetDirectoryName(path));

    if (File.Exists(path)) {
      var existing = File.ReadAllText(path);
      if (string.Equals(existing, contents, StringComparison.Ordinal)) return;
    }

    File.WriteAllText(path, contents, new UTF8Encoding(false));
  }

  static string Escape(string value) {
    if (string.IsNullOrEmpty(value)) return "";
    return value
      .Replace("\\", "\\\\")
      .Replace("\t", "\\t")
      .Replace("\r", "\\r")
      .Replace("\n", "\\n");
  }

  static string ComputeManifestHash(List<ManifestRow> entries) {
    var sb = new StringBuilder(entries.Count * 48);
    for (var i = 0; i < entries.Count; i++) {
      var entry = entries[i];
      sb.Append(entry.libraryName).Append('|').Append(entry.rowCount).Append('|').Append(entry.contentHash).Append('\n');
    }
    return ComputeHash(sb.ToString());
  }

  static string ComputeHash(string value) {
    var bytes = Encoding.UTF8.GetBytes(value ?? "");
    byte[] hashBytes;
    using (var sha = SHA256.Create()) {
      hashBytes = sha.ComputeHash(bytes);
    }
    var sb = new StringBuilder(hashBytes.Length * 2);
    for (var i = 0; i < hashBytes.Length; i++) {
      sb.Append(hashBytes[i].ToString("x2", CultureInfo.InvariantCulture));
    }
    return sb.ToString();
  }

  static string RemoveExtension(string value) {
    var normalized = NormalizePath(value);
    return normalized.EndsWith(".spriteLib", StringComparison.OrdinalIgnoreCase)
      ? normalized.Substring(0, normalized.Length - ".spriteLib".Length)
      : normalized;
  }

  static string GetProjectAssetFullPath(string assetPath) {
    var normalizedAssetPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath)) return "";

    var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    return Path.GetFullPath(Path.Combine(projectRoot, normalizedAssetPath));
  }

  static string NormalizePath(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Replace('\\', '/').Trim();
  }

  static string FormatByteCount(long bytes) {
    if (bytes <= 0) return "0B";

    var units = new[] { "B", "KB", "MB", "GB", "TB" };
    double value = bytes;
    var unitIndex = 0;
    while (value >= 1024d && unitIndex < units.Length - 1) {
      value /= 1024d;
      unitIndex++;
    }

    var format = unitIndex == 0 ? "0" : "0.0";
    return value.ToString(format, CultureInfo.InvariantCulture) + units[unitIndex];
  }

  static void EnsureFolderExists(string folderPath) {
    if (string.IsNullOrWhiteSpace(folderPath)) return;
    var normalized = NormalizePath(folderPath);
    if (AssetDatabase.IsValidFolder(normalized)) return;

    var parts = normalized.Split('/');
    if (parts.Length == 0) return;

    var current = parts[0];
    for (var i = 1; i < parts.Length; i++) {
      var next = current + "/" + parts[i];
      if (!AssetDatabase.IsValidFolder(next)) {
        AssetDatabase.CreateFolder(current, parts[i]);
      }
      current = next;
    }
  }

  static void WriteManifestTextAsset(string manifestAssetPath, List<ManifestRow> rows) {
    var sb = new StringBuilder(rows.Count * 72);
    sb.Append("#hash\t").Append(ComputeManifestHash(rows)).Append('\n');

    for (var i = 0; i < rows.Count; i++) {
      var row = rows[i];
      sb
        .Append(Escape(row.libraryName)).Append('\t')
        .Append(Escape(row.address)).Append('\t')
        .Append(Escape(row.assetPath)).Append('\t')
        .Append(row.rowCount.ToString(CultureInfo.InvariantCulture)).Append('\t')
        .Append(Escape(row.contentHash))
        .Append('\n');
    }

    WriteIfChanged(manifestAssetPath, sb.ToString());
  }
}

public class SpriteIndexBuildProcessor : IPreprocessBuildWithReport {
  public int callbackOrder => 0;

  public void OnPreprocessBuild(BuildReport report) {
    if (SpriteIndexBuilder.PrepareForPlayerBuild(logResult: true, failOnError: true)) return;
    throw new BuildFailedException("Sprite runtime index prebuild step failed.");
  }
}
#endif
