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

public static class SpriteIndexBuilder {
  static readonly Regex spriteRefRegex = new(@"^\s*m_Sprite(?:Override)?: \{fileID:\s*([^,]+), guid:\s*([0-9a-fA-F]{32}),", RegexOptions.Compiled);
  static readonly Regex guidRegex = new(@"guid:\s*([0-9a-fA-F]{32})", RegexOptions.Compiled);
  static readonly Regex labelFrameRegex = new(@"^(.*)_(\d+)$", RegexOptions.Compiled);

  static class BuildContext {
    public const string ManualRuntimeIndex = "Manual Runtime Index";
    public const string ManualAddressablesBuild = "Manual Addressables Build";
    public const string PlayerPrebuild = "Player Prebuild";
  }

  static class BuilderConfig {
    public const string SourceRootFolder = SpriteStreamingConfig.SourceRootFolder;
    public const string RuntimeIndexFolder = SpriteStreamingConfig.RuntimeIndexFolder;
    public const string ManifestAssetPath = SpriteStreamingConfig.ManifestAssetPath;
    public const string IncludeAssetPath = SpriteStreamingConfig.IncludeAssetPath;
    public const string SettingsAssetPath = SpriteStreamingConfig.SettingsAssetPath;
    public const string TextureAddressablesGroupName = SpriteStreamingConfig.TextureAddressablesGroupName;
    public const string IndexAddressablesGroupName = SpriteStreamingConfig.IndexAddressablesGroupName;
    public const string DefaultManifestAddress = SpriteStreamingConfig.DefaultManifestAddress;
    public const string AtlasMetadataAddressablesLabel = SpriteStreamingConfig.AtlasMetadataAddressablesLabel;
    public const string SpriteWithNormalsScriptPath = "Assets/Scripts/Util/Game/SpriteWithNormals.cs";
    public const string SyntheticTextureLabelPrefix = "ss_bundle_";
    public const int SyntheticTextureLabelFolderDepth = 3;
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
    Standard = 1,
    Grouped = 2,
  }

  sealed class BuildState {
    public readonly AddressableAssetSettings addressables;
    public readonly AddressableAssetGroup textureGroup;
    public readonly AddressableAssetGroup indexGroup;
    public readonly List<string> errors = new();
    public readonly Dictionary<string, Dictionary<long, string>> addressCacheByGuid = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, Dictionary<string, string>> spriteAddressByNameCacheByGuid = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string> derivedNormalAtlasPathByColorAtlas = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, bool> assetExistsByPath = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string> atlasMetadataAssetPathByAtlasPath = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, AtlasMetadataKind> atlasMetadataKindByPath = new(StringComparer.OrdinalIgnoreCase);
    public int schemaRepairs;
    public readonly HashSet<string> runtimeAmbiguityWarnings = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, int> syntheticTextureLabelCounts = new(StringComparer.OrdinalIgnoreCase);
    public int syntheticTextureLabelAssignments;
    public readonly HashSet<string> activeTextureAssetPaths = new(StringComparer.OrdinalIgnoreCase);
    public readonly HashSet<string> activeAtlasMetadataAssetPaths = new(StringComparer.OrdinalIgnoreCase);
    public bool projectGroupedMetadataScanCompleted;
    public bool projectHasGroupedMetadataAssets;
    public int missingNormalLibraryCount;
    public int autoDerivedNormalAddressCount;
    public int missingNormalAddressCount;
    public int skippedColorRowCount;
    public int skippedColorLibraryCount;

    public BuildState(AddressableAssetSettings addressables, AddressableAssetGroup textureGroup, AddressableAssetGroup indexGroup) {
      this.addressables = addressables;
      this.textureGroup = textureGroup;
      this.indexGroup = indexGroup;
    }
  }

  [MenuItem("Tools/Sprite Streaming/5) Rebuild Runtime Index")]
  public static void RebuildRuntimeIndexMenu() {
    RebuildRuntimeIndex(logResult: true, failOnError: false);
  }

  [MenuItem("Tools/Sprite Streaming/7) Build Index + Addressables")]
  public static void RebuildRuntimeIndexAndBuildAddressablesMenu() {
    RebuildRuntimeIndexAndBuildAddressables(logResult: true, cleanCachesBeforeBuild: false);
  }

  [MenuItem("Tools/Sprite Streaming/7b) Build Index + Addressables (Clean)")]
  public static void RebuildRuntimeIndexAndBuildAddressablesCleanMenu() {
    RebuildRuntimeIndexAndBuildAddressables(logResult: true, cleanCachesBeforeBuild: true);
  }

  [MenuItem("Tools/Sprite Streaming/6) Configure Addressables Defaults")]
  public static void ConfigureAddressablesDefaultsMenu() {
    var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
    if (settings == null) {
      Debug.LogError("[SpriteIndexBuilder] Addressables settings were not found.");
      return;
    }

    ConfigureAddressablesBuilderDefaults(settings, logResult: true);
  }

  [MenuItem("Tools/Sprite Streaming/0) Run Essential Pipeline (Sequential)")]
  public static void RunEssentialPipelineSequentialMenu() {
    const int stepCount = 7;
    var startedAt = EditorApplication.timeSinceStartup;
    var aborted = false;

    Debug.Log("[SpriteIndexBuilder] [Essential Pipeline] Deferring intermediate asset refreshes until rebuild/build steps.");

    bool RunStep(int stepIndex, string stepName, Func<bool> action) {
      var stepLabel = "[SpriteIndexBuilder] [Essential Pipeline] Step " + stepIndex + "/" + stepCount + " - " + stepName;
      Debug.Log(stepLabel + " (start)");

      try {
        EditorUtility.DisplayProgressBar("Sprite Streaming", stepName + "...", (float)(stepIndex - 1) / stepCount);
        if (!action()) {
          Debug.LogError(stepLabel + " failed. Aborting pipeline.");
          aborted = true;
          return false;
        }
      }
      catch (Exception ex) {
        Debug.LogError(stepLabel + " threw an exception. Aborting pipeline.\n" + ex);
        aborted = true;
        return false;
      }

      Debug.Log(stepLabel + " (done)");
      return true;
    }

    try {
      if (!RunStep(1, "Clean build caches", () => {
        CleanAddressablesBuildCaches(logResult: true);
        return true;
      })) return;

      if (!RunStep(2, "Sync location profiles from prefabs", () => {
        LocationWarmProfileBootstrap.SyncLocationWarmAssets(logResult: true, saveAndRefresh: false);
        return true;
      })) return;

      if (!RunStep(3, "Apply unified import flow", () => {
        return SpriteStreamingHotsetConfigurator.ApplyUnifiedImportFlow(saveAndRefreshAtEnd: false, logResult: true);
      })) return;

      if (!RunStep(4, "Rebuild runtime index", () => {
        return RebuildRuntimeIndex(logResult: true, failOnError: false);
      })) return;

      if (!RunStep(5, "Apply gameplay + location hotset", () => {
        return SpriteStreamingHotsetConfigurator.ApplyPerformanceHotset(
          rebuildRuntimeIndexFirst: false,
          saveAndRefreshAtEnd: false,
          logResult: true
        );
      })) return;

      if (!RunStep(6, "Configure Addressables defaults", () => {
        var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
        if (settings == null) {
          Debug.LogError("[SpriteIndexBuilder] [Essential Pipeline] Addressables settings were not found.");
          return false;
        }
        ConfigureAddressablesBuilderDefaults(settings, logResult: true);
        return true;
      })) return;

      if (!RunStep(7, "Build Addressables content", () =>
        BuildAddressablesContent(logResult: true, cleanCachesBeforeBuild: false)
      )) return;
    }
    finally {
      EditorUtility.ClearProgressBar();
      var duration = (float)(EditorApplication.timeSinceStartup - startedAt);
      var result = aborted ? "aborted" : "completed";
      Debug.Log("[SpriteIndexBuilder] [Essential Pipeline] " + result + " in " + duration.ToString("0.00", CultureInfo.InvariantCulture) + "s.");
    }
  }

  public static bool RebuildRuntimeIndexAndBuildAddressables(bool logResult, bool cleanCachesBeforeBuild = false) {
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
      if (!RebuildRuntimeIndexInternal(logResult: false, failOnError: true, contextLabel: contextLabel)) {
        Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] Runtime index rebuild failed.");
        return false;
      }

      return BuildAddressablesContent(logResult: logResult, cleanCachesBeforeBuild: cleanCachesBeforeBuild, contextLabel: contextLabel);
    }
    catch (BuildFailedException ex) {
      Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] Build failed: " + ex.Message);
      return false;
    }
    catch (Exception ex) {
      Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] Build failed with exception: " + ex);
      return false;
    }
    finally {
      EditorUtility.ClearProgressBar();
    }
  }

  public static bool RebuildRuntimeIndex(bool logResult, bool failOnError) {
    return RebuildRuntimeIndexInternal(logResult, failOnError, BuildContext.ManualRuntimeIndex);
  }

  static bool BuildAddressablesContent(bool logResult, bool cleanCachesBeforeBuild, string contextLabel = BuildContext.ManualAddressablesBuild) {
    try {
      AssetDatabase.SaveAssets();
      AssetDatabase.Refresh();

      if (cleanCachesBeforeBuild) {
        EditorUtility.DisplayProgressBar("Sprite Streaming", "Cleaning Addressables build cache...", 0.72f);
        CleanAddressablesBuildCaches(logResult);
      }

      EditorUtility.DisplayProgressBar("Sprite Streaming", "Building Addressables content...", 0.85f);
      AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
      if (result == null) {
        Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] Addressables build did not return a result.");
        return false;
      }
      if (!string.IsNullOrWhiteSpace(result.Error)) {
        Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] Addressables build failed: " + result.Error);
        return false;
      }

      if (logResult) {
        Debug.Log(
          "[SpriteIndexBuilder] [" + contextLabel + "] Build complete. output='" + (result.OutputPath ?? "") +
          "' locations=" + result.LocationCount +
          " duration=" + result.Duration.ToString("0.00", CultureInfo.InvariantCulture) + "s"
        );
      }

      return true;
    }
    catch (BuildFailedException ex) {
      Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] Build failed: " + ex.Message);
      return false;
    }
    catch (Exception ex) {
      Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] Build failed with exception: " + ex);
      return false;
    }
  }

  [MenuItem("Tools/Sprite Streaming/1) Clean Build Caches")]
  public static void CleanAddressablesBuildCachesMenu() {
    CleanAddressablesBuildCaches(logResult: true);
  }

  public static bool PrepareForPlayerBuild(bool logResult, bool failOnError) {
    if (logResult) {
      Debug.Log("[SpriteIndexBuilder] [" + BuildContext.PlayerPrebuild + "] Preparing runtime index for player build. Addressables content build is handled by Unity's build pipeline.");
    }
    return RebuildRuntimeIndexInternal(logResult, failOnError, BuildContext.PlayerPrebuild);
  }

  static bool RebuildRuntimeIndexInternal(bool logResult, bool failOnError, string contextLabel) {
    var addressableSettings = AddressableAssetSettingsDefaultObject.GetSettings(true);
    if (addressableSettings == null) {
      Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] Addressables settings were not found.");
      if (failOnError) throw new BuildFailedException("Addressables settings were not found.");
      return false;
    }

    EnsureFolderExists(Path.GetDirectoryName(BuilderConfig.SettingsAssetPath));
    EnsureFolderExists(BuilderConfig.RuntimeIndexFolder);

    var streamingSettings = EnsureStreamingSettingsAsset();
    var includeAsset = EnsureIncludeAsset();
    var manifestAssetPath = EnsureManifestAssetPath();

    var textureGroup = EnsureAddressableGroup(addressableSettings, BuilderConfig.TextureAddressablesGroupName, contextLabel, logResult, out var textureSchemaRepairs);
    var indexGroup = EnsureAddressableGroup(addressableSettings, BuilderConfig.IndexAddressablesGroupName, contextLabel, logResult, out var indexSchemaRepairs);
    var state = new BuildState(addressableSettings, textureGroup, indexGroup);
    state.schemaRepairs = textureSchemaRepairs + indexSchemaRepairs;
    if (!ValidateAddressableGroupsPreflight(state, contextLabel, failOnError)) {
      LogRuntimeIndexSummary(contextLabel, false, libraryNameCount: 0, shardCount: 0, schemaRepairs: state.schemaRepairs, errorCount: state.errors.Count);
      return false;
    }

    var librariesByKey = DiscoverLibraryPaths();
    ReportDuplicateShortNameAmbiguities(librariesByKey);
    var guidToLibraryName = DiscoverGuidToLibraryName(librariesByKey);
    var requestedLibraryNames = CollectRequestedLibraryNames(librariesByKey, guidToLibraryName, includeAsset);

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
        Debug.LogError($"[SpriteIndexBuilder] Failed to resolve canonical library name for '{requestedLibraryName}'.");
        state.errors.Add("Missing color library for requested libraryName '" + requestedLibraryName + "'.");
        continue;
      }

      if (!builtCanonicalLibraryNames.Add(libraryName)) {
        continue;
      }

      if (!librariesByKey.TryGetValue(libraryName, out var colorLibraryPath)) {
        Debug.LogError($"[SpriteIndexBuilder] Library '{libraryName}' not found in librariesByKey. Expected path.");
        state.errors.Add("Missing color library for libraryName '" + libraryName + "' (requested '" + requestedLibraryName + "').");
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
          continue;
        }
        if (!ValidateRuntimeAtlasAddress(state, colorAddress, colorContext, recordError: false)) {
          skippedColorRowsForLibrary++;
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
        Debug.LogWarning(
          "[SpriteIndexBuilder] [" + contextLabel + "] Skipped unresolved color rows while rebuilding runtime index." +
          " libraryName='" + libraryName + "'" +
          " skippedRows=" + skippedColorRowsForLibrary +
          " totalRows=" + colorRows.Count);
      }

      if (shardRows.Count == 0) {
        state.skippedColorLibraryCount++;
        Debug.LogWarning(
          "[SpriteIndexBuilder] [" + contextLabel + "] Skipped sprite library because all color rows were unresolved." +
          " libraryName='" + libraryName + "'" +
          " requested='" + requestedLibraryName + "'" +
          " totalRows=" + colorRows.Count);
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

    CleanupStaleTextureEntries(textureGroup, state.activeTextureAssetPaths);
    CleanupStaleShardAssets(shardAssetPaths);
    CleanupStaleIndexEntries(state, indexGroup, shardAssetPaths, manifestAssetPath, state.activeAtlasMetadataAssetPaths);

    manifestEntries.Sort((left, right) => string.Compare(left.libraryName, right.libraryName, StringComparison.Ordinal));
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

    if (!changed) {
      if (logResult) {
        Debug.Log("[SpriteIndexBuilder] Addressables defaults already configured (Player=Packed, Play Mode=Fast).");
      }
      return;
    }

    EditorUtility.SetDirty(settings);
    AssetDatabase.SaveAssets();
    if (logResult) {
      Debug.Log("[SpriteIndexBuilder] Addressables defaults updated (Player=Packed, Play Mode=Fast, OptimizeCatalog=True).");
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
    var asset = AssetDatabase.LoadAssetAtPath<SpriteStreamingSettings>(BuilderConfig.SettingsAssetPath);
    if (asset != null) return asset;

    asset = ScriptableObject.CreateInstance<SpriteStreamingSettings>();
    if (asset == null) return null;
    AssetDatabase.CreateAsset(asset, BuilderConfig.SettingsAssetPath);
    AssetDatabase.SaveAssets();
    return asset;
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

    return "";
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
    var root = NormalizePath(BuilderConfig.SourceRootFolder);
    if (!Directory.Exists(root)) return result;

    var files = Directory.GetFiles(root, "*.spriteLib", SearchOption.AllDirectories);
    Array.Sort(files, StringComparer.Ordinal);

    for (var i = 0; i < files.Length; i++) {
      var path = NormalizePath(files[i]);
      var relative = path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)
        ? path.Substring(root.Length + 1)
        : path;
      var key = RemoveExtension(relative);
      result[key] = path;
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
    SpriteStreamingInclude includeAsset
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

    CollectLibraryNamesFromFiles(assetsRoot, "*.unity", guidToLibraryName, result);
    CollectLibraryNamesFromFiles(assetsRoot, "*.prefab", guidToLibraryName, result);

    if (includeAsset != null && includeAsset.libraryNames != null) {
      for (var i = 0; i < includeAsset.libraryNames.Count; i++) {
        var normalized = SpriteAddressResolver.NormalizeNamePart(includeAsset.libraryNames[i]);
        if (!string.IsNullOrWhiteSpace(normalized)) {
          result.Add(normalized);
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

  static void CollectLibraryNamesFromFiles(string rootPath, string pattern, Dictionary<string, string> guidToLibraryName, HashSet<string> target) {
    if (!Directory.Exists(rootPath)) return;
    var files = Directory.GetFiles(rootPath, pattern, SearchOption.AllDirectories);
    Array.Sort(files, StringComparer.Ordinal);

    for (var i = 0; i < files.Length; i++) {
      CollectLibraryNamesFromSerializedFile(NormalizePath(files[i]), guidToLibraryName, target);
    }
  }

  static void CollectLibraryNamesFromSerializedFile(string path, Dictionary<string, string> guidToLibraryName, HashSet<string> target) {
    if (!File.Exists(path)) return;

    var spriteWithNormalsGuid = AssetDatabase.AssetPathToGUID(BuilderConfig.SpriteWithNormalsScriptPath);
    var insideMonoBehaviour = false;
    var insideSpriteWithNormals = false;
    var pendingLibraryName = "";
    var pendingColorKey = "";
    var pendingColorLibraryGuid = "";

    void BeginSpriteWithNormalsBlock() {
      insideSpriteWithNormals = true;
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
      }

      pendingLibraryName = "";
      pendingColorKey = "";
      pendingColorLibraryGuid = "";
    }

    foreach (var rawLine in File.ReadLines(path)) {
      var line = rawLine ?? "";
      if (line.StartsWith("--- !u!", StringComparison.Ordinal)) {
        FlushPending();
        insideMonoBehaviour = line.StartsWith("--- !u!114", StringComparison.Ordinal);
      }

      if (!insideMonoBehaviour) continue;

      var trimmed = line.Trim();

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
        state.errors.Add("Missing GUID while resolving " + context + ".");
      }
      return "";
    }

    if (!state.addressCacheByGuid.TryGetValue(spriteRef.guid, out var byFileId)) {
      Debug.Log($"[SpriteIndexBuilder] ResolveSpriteAddress: Caching address map for GUID {spriteRef.guid} (Context: {context})");
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
      state.errors.Add("Could not resolve sprite fileID '" + spriteRef.fileId + "' for GUID '" + spriteRef.guid + "' (" + context + ").");
    }
    return "";
  }

  static bool ValidateRuntimeAtlasAddress(BuildState state, string sliceAddress, string context, bool recordError = true) {
    if (state == null || string.IsNullOrWhiteSpace(sliceAddress)) return false;
    bool Fail(string message) {
      if (recordError) {
        state.errors.Add(message);
      }
      return false;
    }

    if (!SpriteSliceAddressUtility.TryParseSliceAddress(sliceAddress, out var atlasAssetPath, out var spriteName)) {
      return Fail("Invalid slice address '" + sliceAddress + "' (" + context + ").");
    }

    var normalizedAtlasPath = NormalizePath(atlasAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedAtlasPath) || string.IsNullOrWhiteSpace(spriteName)) {
      return Fail("Slice address '" + sliceAddress + "' did not resolve atlas path + sprite name (" + context + ").");
    }

    if (!state.activeTextureAssetPaths.Contains(normalizedAtlasPath)) {
      return Fail("Atlas path '" + normalizedAtlasPath + "' was not registered in texture group (" + context + ").");
    }

    if (state.addressables == null || state.textureGroup == null) return true;
    var guid = AssetDatabase.AssetPathToGUID(normalizedAtlasPath);
    if (string.IsNullOrWhiteSpace(guid)) {
      return Fail("Atlas path '" + normalizedAtlasPath + "' has no GUID (" + context + ").");
    }

    var entry = state.addressables.FindAssetEntry(guid);
    if (entry == null || entry.parentGroup != state.textureGroup) {
      return Fail("Atlas path '" + normalizedAtlasPath + "' is not in texture addressables group (" + context + ").");
    }

    if (!string.Equals(entry.address, normalizedAtlasPath, StringComparison.Ordinal)) {
      return Fail(
        "Atlas path '" + normalizedAtlasPath + "' has addressable key '" + entry.address + "' instead of atlas asset path (" + context + ")."
      );
    }

    return true;
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
    var path = NormalizePath(AssetDatabase.GUIDToAssetPath(guid));
    if (string.IsNullOrWhiteSpace(path)) {
      if (recordError) {
        Debug.LogError($"[SpriteIndexBuilder] BuildAddressMapForGuid: GUID {guid} resolved to empty path.");
        state.errors.Add("GUID '" + guid + "' does not map to an asset path.");
      }
      return map;
    }

    EnsureAddressableTextureEntry(state, path);
    var metaPath = path + ".meta";
    if (!File.Exists(metaPath)) {
      if (recordError) {
        Debug.LogError($"[SpriteIndexBuilder] BuildAddressMapForGuid: Meta file missing for {path} (GUID {guid}).");
        state.errors.Add("Meta file was not found for GUID '" + guid + "' at path '" + metaPath + "'.");
      }
      return map;
    }

    if (!TryParseSpriteSheetInternalIdTable(metaPath, map, out var parseError)) {
      if (recordError) {
        Debug.LogError($"[SpriteIndexBuilder] BuildAddressMapForGuid: Failed to parse sprite sheet table for {path}: {parseError}");
        state.errors.Add("Failed to parse spriteSheet.sprites table for GUID '" + guid + "' at path '" + metaPath + "'" +
                         (string.IsNullOrWhiteSpace(parseError) ? "." : ": " + parseError));
      }
      return map;
    }

    if (!TryParseNameFileIdTable(metaPath, map, out var nameTableError)) {
      if (recordError) {
        Debug.LogError($"[SpriteIndexBuilder] BuildAddressMapForGuid: Failed to parse name table for {path}: {nameTableError}");
        state.errors.Add("Failed to parse nameFileIdTable for GUID '" + guid + "' at path '" + metaPath + "'" +
                         (string.IsNullOrWhiteSpace(nameTableError) ? "." : ": " + nameTableError));
      }
      return map;
    }

    var supplementedLocalIdCount = SupplementAddressMapWithEditorLocalFileIds(path, map);

    var keys = map.Keys.ToList();
    for (var i = 0; i < keys.Count; i++) {
      var localId = keys[i];
      var spriteName = map[localId];
      map[localId] = path + "[" + spriteName + "]";
    }

    if (map.Count == 0) {
      if (recordError) {
        Debug.LogWarning($"[SpriteIndexBuilder] BuildAddressMapForGuid: No sprites found in {path} (GUID {guid}).");
        state.errors.Add("No sprite sub-assets found for GUID '" + guid + "' at path '" + path + "'.");
      }
    } else {
      Debug.Log(
        $"[SpriteIndexBuilder] BuildAddressMapForGuid: Mapped {map.Count} sprites for {path}" +
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

    var inTable = false;
    var tableIndent = -1;

    foreach (var rawLine in File.ReadLines(metaPath)) {
      var line = rawLine ?? "";
      var trimmed = line.Trim();
      var indent = line.Length - line.TrimStart().Length;

      if (!inTable) {
        if (trimmed.StartsWith("nameFileIdTable:", StringComparison.Ordinal)) {
          inTable = true;
          tableIndent = indent;
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

  static void CleanupStaleTextureEntries(AddressableAssetGroup textureGroup, HashSet<string> activeTextureAssetPaths) {
    if (textureGroup == null || textureGroup.Settings == null) return;

    var activeAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (activeTextureAssetPaths != null) {
      foreach (var activePath in activeTextureAssetPaths) {
        var normalized = NormalizePath(activePath);
        if (!string.IsNullOrWhiteSpace(normalized)) {
          activeAssetPaths.Add(normalized);
        }
      }
    }

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

    if (string.Equals(groupName, BuilderConfig.TextureAddressablesGroupName, StringComparison.Ordinal)) {
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
    if (state.textureGroup == null) {
      state.errors.Add("Addressables group '" + BuilderConfig.TextureAddressablesGroupName + "' is missing.");
      valid = false;
    } else if (!HasRequiredGroupSchemas(state.textureGroup)) {
      state.errors.Add("Addressables group '" + BuilderConfig.TextureAddressablesGroupName + "' is missing required schemas.");
      valid = false;
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
      " depth=" + BuilderConfig.SyntheticTextureLabelFolderDepth +
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

  static void LogSkippedColorRowSummary(string contextLabel, BuildState state) {
    if (state == null) return;
    if (state.skippedColorRowCount <= 0 && state.skippedColorLibraryCount <= 0) return;

    Debug.LogWarning(
      "[SpriteIndexBuilder] [" + contextLabel + "] Skipped unresolved color sprite references." +
      " skippedRows=" + state.skippedColorRowCount +
      " skippedLibraries=" + state.skippedColorLibraryCount);
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
    if (state == null || state.addressables == null || state.textureGroup == null) return;
    var normalizedAssetPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath)) return;

    if (!state.activeTextureAssetPaths.Add(normalizedAssetPath)) {
      return;
    }

    var guid = AssetDatabase.AssetPathToGUID(normalizedAssetPath);
    if (string.IsNullOrWhiteSpace(guid)) return;

    var entry = state.addressables.FindAssetEntry(guid);
    if (entry == null || entry.parentGroup != state.textureGroup) {
      entry = state.addressables.CreateOrMoveEntry(guid, state.textureGroup, false, false);
    }
    if (entry == null) return;

    if (!string.Equals(entry.address, normalizedAssetPath, StringComparison.Ordinal)) {
      entry.SetAddress(normalizedAssetPath, false);
    }

    var syntheticLabel = BuildSyntheticTextureBundleLabel(normalizedAssetPath);
    if (!string.IsNullOrWhiteSpace(syntheticLabel)) {
      state.addressables.AddLabel(syntheticLabel, false);
      ApplySyntheticBundleLabel(entry, syntheticLabel);
      state.syntheticTextureLabelAssignments++;
      if (!state.syntheticTextureLabelCounts.TryGetValue(syntheticLabel, out var count)) count = 0;
      state.syntheticTextureLabelCounts[syntheticLabel] = count + 1;
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
    var sourceRoot = NormalizePath(SpriteStreamingConfig.TextureSourceRootFolder);
    if (string.IsNullOrWhiteSpace(sourceRoot)) return false;

    var fullSourceRoot = Path.GetFullPath(sourceRoot);
    if (!Directory.Exists(fullSourceRoot)) return false;

    foreach (var metadataFullPath in Directory.EnumerateFiles(fullSourceRoot, "*.json", SearchOption.AllDirectories)) {
      var normalizedAssetPath = NormalizePath(metadataFullPath);
      if (GetAtlasMetadataKind(state, normalizedAssetPath) != AtlasMetadataKind.Grouped) continue;
      state.projectHasGroupedMetadataAssets = true;
      break;
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
      var sourceRoot = NormalizePath(SpriteStreamingConfig.TextureSourceRootFolder);
      var underSourceRoot = string.IsNullOrWhiteSpace(sourceRoot) ||
                            normalizedAssetPath.StartsWith(sourceRoot + "/", StringComparison.OrdinalIgnoreCase);
      if (underSourceRoot) {
        var fullPath = Path.GetFullPath(normalizedAssetPath);
        if (File.Exists(fullPath)) {
          try {
            var jsonText = File.ReadAllText(fullPath);
            if (!string.IsNullOrWhiteSpace(jsonText) &&
                jsonText.IndexOf("\"sprites\"", StringComparison.Ordinal) >= 0 &&
                jsonText.IndexOf("\"offsetFromCellCenterPx\"", StringComparison.Ordinal) >= 0 &&
                (jsonText.IndexOf("\"sourceAtlasAssetPath\"", StringComparison.Ordinal) >= 0 ||
                 jsonText.IndexOf("\"exportedAtlasAssetPath\"", StringComparison.Ordinal) >= 0)) {
              metadataKind =
                jsonText.IndexOf("\"groupKey\"", StringComparison.Ordinal) >= 0 &&
                jsonText.IndexOf("\"sourceCategories\"", StringComparison.Ordinal) >= 0
                  ? AtlasMetadataKind.Grouped
                  : AtlasMetadataKind.Standard;
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

  static void ApplySyntheticBundleLabel(AddressableAssetEntry entry, string syntheticLabel) {
    if (entry == null || string.IsNullOrWhiteSpace(syntheticLabel)) return;
    if (entry.labels == null) return;

    var remove = new List<string>();
    foreach (var label in entry.labels) {
      if (string.IsNullOrWhiteSpace(label)) continue;
      if (!label.StartsWith(BuilderConfig.SyntheticTextureLabelPrefix, StringComparison.OrdinalIgnoreCase)) continue;
      if (string.Equals(label, syntheticLabel, StringComparison.OrdinalIgnoreCase)) continue;
      remove.Add(label);
    }

    for (var i = 0; i < remove.Count; i++) {
      entry.SetLabel(remove[i], false, false, false);
    }

    entry.SetLabel(syntheticLabel, true, true, false);
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

    relativeFolder = LimitSyntheticLabelDepth(relativeFolder, BuilderConfig.SyntheticTextureLabelFolderDepth);
    var sanitized = SanitizeSyntheticLabelToken(relativeFolder);
    if (string.IsNullOrWhiteSpace(sanitized)) sanitized = "misc";
    return BuilderConfig.SyntheticTextureLabelPrefix + sanitized;
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

  static string NormalizePath(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Replace('\\', '/').Trim();
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
