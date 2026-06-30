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

  static IReadOnlyList<string> cachedTextureRoots = null;
  static HashSet<string> cachedTextureRootsSet = null;

  static void CacheTextureRoots() {
    if (cachedTextureRoots != null) return;
    cachedTextureRoots = ContentPackPipeline.GetTextureSearchRoots();
    cachedTextureRootsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (cachedTextureRoots != null) {
      for (var i = 0; i < cachedTextureRoots.Count; i++) {
        var root = NormalizePath(cachedTextureRoots[i]).TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(root)) {
          cachedTextureRootsSet.Add(root);
        }
      }
    }
  }

  public static void ClearCachedTextureRoots() {
    cachedTextureRoots = null;
    cachedTextureRootsSet = null;
  }

  public static void ClearCachedSpriteSliceEstimates(string reason = "") {
    var clearedCount = cachedSpriteSliceCountsByAssetPath.Count;
    cachedSpriteSliceCountsByAssetPath.Clear();
    ClearCachedTextureRoots();
    if (clearedCount <= 0) return;

    Debug.Log(
      "[SpriteIndexBuilder] Cleared cached sprite slice estimates." +
      " previous_entries=" + clearedCount +
      (string.IsNullOrWhiteSpace(reason) ? "" : " reason='" + reason + "'")
    );
  }

  static string NormalizePath(string value) =>
    string.IsNullOrWhiteSpace(value) ? "" : value.Replace('\\', '/').Trim();

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
    public readonly HashSet<string> addressCacheBuiltSilentlyByGuid = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, Dictionary<string, string>> spriteAddressByNameCacheByGuid = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string> derivedNormalAtlasPathByColorAtlas = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string> runtimeTextureAssetPathBySourceAssetPath = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, bool> assetExistsByPath = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string> atlasMetadataAssetPathByAtlasPath = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, AtlasMetadataKind> atlasMetadataKindByPath = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, AddressableAssetGroup> textureGroupsByName = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string> activeTextureAssetPathByGuid = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, Dictionary<long, string>> activeSpriteAddressByFileIdByGuid = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<long, List<string>> activeSpriteAddressesByFileId = new();
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
    public bool activeTextureGuidIndexBuilt;

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

  // Content pack builds are driven by Tools/ContentPackIterationUI.py.

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
      ClearCachedTextureRoots();
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
      ClearCachedTextureRoots();
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
      var runtimeAddressablesChanged =
        GameplayPlayerAddressablesBootstrap.SyncGameplayPlayerAddressables(logResult: false, saveAndRefresh: false) |
        ProjectileAddressablesBootstrap.SyncProjectileAddressables(logResult: false, saveAndRefresh: false) |
        LocationAddressablesBootstrap.SyncLocationAddressables(logResult: false, saveAndRefresh: false) |
        RuntimeMaterialAddressablesBootstrap.SyncRuntimeMaterialAddressables(logResult: false, saveAndRefresh: false);
      if (runtimeAddressablesChanged && logResult) {
        Debug.Log("[SpriteIndexBuilder] [" + contextLabel + "] Synced runtime prefab/material Addressables entries before build.");
      }

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

}
#endif
