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
      var physicalRoot = ContentPackPipeline.GetPhysicalPath(root);
      if (string.IsNullOrWhiteSpace(physicalRoot) || !Directory.Exists(physicalRoot)) continue;

      var files = DiscoverLogicalLibraryFiles(physicalRoot);

      for (var i = 0; i < files.Length; i++) {
        var physicalPath = NormalizePath(files[i]);
        var projectPath = ContentPackPipeline.ToProjectAssetPath(physicalPath);
        var relative = projectPath.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)
          ? projectPath.Substring(root.Length + 1)
          : projectPath;
        var key = RemoveExtension(relative);
        if (result.ContainsKey(key)) continue;
        result[key] = projectPath;
      }
    }

    return result;
  }

  static string[] DiscoverLogicalLibraryFiles(string physicalRoot) {
    if (string.IsNullOrWhiteSpace(physicalRoot) || !Directory.Exists(physicalRoot)) {
      return Array.Empty<string>();
    }

    var customFiles = Directory.GetFiles(physicalRoot, "*" + BuilderConfig.CustomSpriteLibraryExtension, SearchOption.AllDirectories);
    var legacyFiles = Directory.GetFiles(physicalRoot, "*" + BuilderConfig.LegacySpriteLibraryExtension, SearchOption.AllDirectories);
    Array.Sort(customFiles, StringComparer.OrdinalIgnoreCase);
    Array.Sort(legacyFiles, StringComparer.OrdinalIgnoreCase);

    var files = new List<string>(customFiles.Length + legacyFiles.Length);
    files.AddRange(customFiles);
    files.AddRange(legacyFiles);
    return files.ToArray();
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

}
#endif
