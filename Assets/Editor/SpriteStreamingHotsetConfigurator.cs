#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class SpriteStreamingHotsetConfigurator {
  const string SpriteWithNormalsScriptPath = "Assets/Scripts/Util/Game/SpriteWithNormals.cs";
  const string DefaultGameplayScenePath = "Assets/Scenes/MyCurrent.unity";
  const int AutomaticTextureFormat = -1;
  static readonly Regex guidRegex = new(@"guid:\s*([0-9a-fA-F]{32})", RegexOptions.Compiled);

  [MenuItem("Tools/Sprite Streaming/5) Apply Hotset")]
  public static void ApplyPerformanceHotsetMenu() {
    ApplyPerformanceHotset(rebuildRuntimeIndexFirst: true, saveAndRefreshAtEnd: true, logResult: true);
  }

  public static bool ApplyPerformanceHotset(bool rebuildRuntimeIndexFirst, bool saveAndRefreshAtEnd, bool logResult) {
    try {
      LocationWarmProfileBootstrap.SyncLocationWarmAssets(logResult: false, saveAndRefresh: saveAndRefreshAtEnd);

      if (rebuildRuntimeIndexFirst) {
        EditorUtility.DisplayProgressBar("Sprite Streaming", "Rebuilding runtime index...", 0.1f);
        var rebuildOk = SpriteIndexBuilder.RebuildRuntimeIndex(logResult: true, failOnError: false);
        if (!rebuildOk) {
          Debug.LogWarning("[SpriteStreamingHotsetConfigurator] Runtime index rebuild reported errors. Continuing with best-effort hotset pass.");
        }
      }

      EditorUtility.DisplayProgressBar("Sprite Streaming", "Collecting gameplay and location libraries...", 0.25f);
      var requestedNameparts = CollectGameplayAndLocationNameparts();
      IncludeOptionalNameparts(requestedNameparts);

      EditorUtility.DisplayProgressBar("Sprite Streaming", "Loading runtime index manifest...", 0.4f);
      var shardPathByNamepart = LoadManifestShardMap();
      if (shardPathByNamepart.Count == 0) {
        Debug.LogError("[SpriteStreamingHotsetConfigurator] Manifest map is empty. Rebuild runtime index first.");
        return false;
      }

      EditorUtility.DisplayProgressBar("Sprite Streaming", "Resolving hotset textures...", 0.6f);
      var hotsetTexturePaths = ResolveHotsetTextureAssetPaths(requestedNameparts, shardPathByNamepart);
      if (hotsetTexturePaths.Count == 0) {
        Debug.LogWarning("[SpriteStreamingHotsetConfigurator] No hotset texture assets were resolved.");
        return false;
      }

      EditorUtility.DisplayProgressBar("Sprite Streaming", "Applying texture importer settings...", 0.8f);
      var changedTexturePaths = ApplyStreamingImporterSettings(hotsetTexturePaths);
      if (saveAndRefreshAtEnd && changedTexturePaths.Count > 0) {
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
      }

      var changedGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (var path in changedTexturePaths) {
        var guid = AssetDatabase.AssetPathToGUID(path);
        if (!string.IsNullOrWhiteSpace(guid)) changedGuids.Add(guid);
      }

      var sizeBucket = EstimateSizeDeltaBucket(changedTexturePaths.Count);
      if (logResult) {
        Debug.Log(
          "[SpriteStreamingHotsetConfigurator] Applied performance hotset." +
          " requestedNameparts=" + requestedNameparts.Count +
          " resolvedTextures=" + hotsetTexturePaths.Count +
          " changedTextures=" + changedTexturePaths.Count +
          " changedGuids=" + changedGuids.Count +
          " sizeDeltaBucket=" + sizeBucket +
          " rebuiltIndex=" + rebuildRuntimeIndexFirst
        );
      }
      return true;
    }
    catch (Exception ex) {
      Debug.LogError("[SpriteStreamingHotsetConfigurator] Failed: " + ex);
      return false;
    }
    finally {
      EditorUtility.ClearProgressBar();
    }
  }

  [MenuItem("Tools/Sprite Streaming/3) Apply Unified Import Flow")]
  public static void ApplyUnifiedImportFlowMenu() {
    ApplyUnifiedImportFlow(saveAndRefreshAtEnd: true, logResult: true);
  }

  public static bool ApplyUnifiedImportFlow(bool saveAndRefreshAtEnd, bool logResult) {
    try {
      EditorUtility.DisplayProgressBar("Sprite Streaming", "Collecting sprite textures...", 0.2f);
      var texturePaths = CollectSourceRootTextureAssetPaths();
      if (texturePaths.Count == 0) {
        Debug.LogWarning("[SpriteStreamingHotsetConfigurator] No sprite textures were found under '" + SpriteStreamingConfig.TextureSourceRootFolder + "'.");
        return false;
      }

      EditorUtility.DisplayProgressBar("Sprite Streaming", "Applying unified importer policy...", 0.6f);
      var changedTexturePaths = ApplyStreamingImporterSettings(texturePaths);
      if (saveAndRefreshAtEnd && changedTexturePaths.Count > 0) {
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
      }

      if (logResult) {
        Debug.Log(
          "[SpriteStreamingHotsetConfigurator] Applied unified import flow." +
          " scannedTextures=" + texturePaths.Count +
          " changedTextures=" + changedTexturePaths.Count +
          " sourceRoot='" + SpriteStreamingConfig.TextureSourceRootFolder + "'"
        );
      }
      return true;
    }
    catch (Exception ex) {
      Debug.LogError("[SpriteStreamingHotsetConfigurator] Failed to apply unified import flow: " + ex);
      return false;
    }
    finally {
      EditorUtility.ClearProgressBar();
    }
  }

  static HashSet<string> CollectGameplayAndLocationNameparts() {
    var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var spriteWithNormalsGuid = AssetDatabase.AssetPathToGUID(SpriteWithNormalsScriptPath);
    if (string.IsNullOrWhiteSpace(spriteWithNormalsGuid)) return result;
    var guidToNamepart = BuildGuidToNamepartMap();

    var scenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var configuredScenes = EditorBuildSettings.scenes
      .Where(scene => scene != null && scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
      .Select(scene => NormalizePath(scene.path));
    Debug.Log($"[SpriteStreamingHotsetConfigurator] CollectGameplayAndLocationNameparts: Found {configuredScenes.Count()} configured scenes.");
    foreach (var scenePath in configuredScenes) {
      if (!string.IsNullOrWhiteSpace(scenePath)) scenes.Add(scenePath);
    }

    var gameplayScenePath = NormalizePath(DefaultGameplayScenePath);
    if (!string.IsNullOrWhiteSpace(gameplayScenePath) && File.Exists(gameplayScenePath)) {
      scenes.Add(gameplayScenePath);
    }

    foreach (var scenePath in scenes) {
      if (!File.Exists(scenePath)) {
        Debug.LogWarning($"[SpriteStreamingHotsetConfigurator] CollectGameplayAndLocationNameparts: Scene path not found: {scenePath}");
        continue;
      }
      int countBefore = result.Count;
      CollectLibraryNamesFromSerializedFile(scenePath, spriteWithNormalsGuid, guidToNamepart, result);
      int added = result.Count - countBefore;
      if (added > 0) Debug.Log($"[SpriteStreamingHotsetConfigurator] Scene '{scenePath}' contributed {added} nameparts.");
    }

    CollectLocationPrefabNameparts(spriteWithNormalsGuid, guidToNamepart, result);

    Debug.Log($"[SpriteStreamingHotsetConfigurator] CollectGameplayAndLocationNameparts: Total unique nameparts collected: {result.Count}");
    return result;
  }

  static void CollectLocationPrefabNameparts(
    string spriteWithNormalsGuid,
    Dictionary<string, string> guidToNamepart,
    HashSet<string> target
  ) {
    if (target == null) return;

    var profileGuids = AssetDatabase.FindAssets("t:LocationWarmProfile", new[] { "Assets/Resources" });
    for (var i = 0; i < profileGuids.Length; i++) {
      var assetPath = NormalizePath(AssetDatabase.GUIDToAssetPath(profileGuids[i]));
      if (string.IsNullOrWhiteSpace(assetPath)) continue;

      var profile = AssetDatabase.LoadAssetAtPath<LocationWarmProfile>(assetPath);
      if (profile == null || profile.LocationPrefab == null) continue;

      var prefabPath = NormalizePath(AssetDatabase.GetAssetPath(profile.LocationPrefab));
      if (string.IsNullOrWhiteSpace(prefabPath) || !File.Exists(prefabPath)) continue;

      var countBefore = target.Count;
      CollectLibraryNamesFromSerializedFile(prefabPath, spriteWithNormalsGuid, guidToNamepart, target);
      var added = target.Count - countBefore;
      if (added > 0) {
        Debug.Log(
          $"[SpriteStreamingHotsetConfigurator] Location prefab '{prefabPath}' contributed {added} nameparts."
        );
      }
    }
  }

  static void IncludeOptionalNameparts(HashSet<string> target) {
    if (target == null) return;
    var includeAsset = AssetDatabase.LoadAssetAtPath<SpriteStreamingInclude>(SpriteStreamingConfig.IncludeAssetPath);
    if (includeAsset == null || includeAsset.libraryNames == null) return;

    for (var i = 0; i < includeAsset.libraryNames.Count; i++) {
      var normalized = SpriteAddressResolver.NormalizeNamePart(includeAsset.libraryNames[i]);
      if (!string.IsNullOrWhiteSpace(normalized)) {
        target.Add(normalized);
      }
    }
  }

  static void CollectLibraryNamesFromSerializedFile(
    string path,
    string spriteWithNormalsGuid,
    Dictionary<string, string> guidToNamepart,
    HashSet<string> target
  ) {
    if (!File.Exists(path) || string.IsNullOrWhiteSpace(spriteWithNormalsGuid) || target == null) return;

    var insideMonoBehaviour = false;
    var insideSpriteWithNormals = false;
    var pendingNamepart = "";
    var pendingColorKey = "";
    var pendingColorLibraryGuid = "";

    void Flush() {
      if (!insideSpriteWithNormals) return;
      insideSpriteWithNormals = false;
      var resolved = pendingNamepart;
      if (string.IsNullOrWhiteSpace(resolved)) resolved = pendingColorKey;
      if (string.IsNullOrWhiteSpace(resolved) &&
          !string.IsNullOrWhiteSpace(pendingColorLibraryGuid) &&
          guidToNamepart != null &&
          guidToNamepart.TryGetValue(pendingColorLibraryGuid, out var mappedNamepart)) {
        resolved = mappedNamepart;
      }

      var normalized = SpriteAddressResolver.NormalizeNamePart(resolved);
      if (!string.IsNullOrWhiteSpace(normalized)) {
        target.Add(normalized);
      }
      pendingNamepart = "";
      pendingColorKey = "";
      pendingColorLibraryGuid = "";
    }

    foreach (var rawLine in File.ReadLines(path)) {
      var line = rawLine ?? "";
      if (line.StartsWith("--- !u!", StringComparison.Ordinal)) {
        Flush();
        insideMonoBehaviour = line.StartsWith("--- !u!114", StringComparison.Ordinal);
      }

      if (!insideMonoBehaviour) continue;
      var trimmed = line.Trim();

      if (!insideSpriteWithNormals &&
          trimmed.StartsWith("m_Script:", StringComparison.Ordinal) &&
          trimmed.IndexOf(spriteWithNormalsGuid, StringComparison.OrdinalIgnoreCase) >= 0) {
        insideSpriteWithNormals = true;
        pendingNamepart = "";
        pendingColorKey = "";
        pendingColorLibraryGuid = "";
        continue;
      }

      if (!insideSpriteWithNormals &&
          trimmed.StartsWith("m_EditorClassIdentifier:", StringComparison.Ordinal) &&
          trimmed.IndexOf("SpriteWithNormals", StringComparison.Ordinal) >= 0) {
        insideSpriteWithNormals = true;
        pendingNamepart = "";
        pendingColorKey = "";
        pendingColorLibraryGuid = "";
        continue;
      }

      if (!insideSpriteWithNormals) continue;

      if (TryReadScalar(trimmed, "libraryName", out var namepartValue) ||
          TryReadScalar(trimmed, "LibraryName", out namepartValue) ||
          TryReadScalar(trimmed, "_libraryName", out namepartValue) ||
          TryReadScalar(trimmed, "namepart", out namepartValue)) {
        pendingNamepart = namepartValue;
        continue;
      }

      if (TryReadScalar(trimmed, "colorKey", out var colorKeyValue)) {
        pendingColorKey = colorKeyValue;
        continue;
      }

      if (trimmed.StartsWith("colorLibrary:", StringComparison.Ordinal)) {
        var match = guidRegex.Match(trimmed);
        if (match.Success) {
          pendingColorLibraryGuid = match.Groups[1].Value.Trim();
        }
      }
    }

    Flush();
  }

  static Dictionary<string, string> BuildGuidToNamepartMap() {
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var pathByNamepart = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    var sourceRoot = NormalizePath(SpriteStreamingConfig.SourceRootFolder);
    if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot)) return map;

    var files = Directory.GetFiles(sourceRoot, "*.spriteLib", SearchOption.AllDirectories);
    Array.Sort(files, StringComparer.Ordinal);

    for (var i = 0; i < files.Length; i++) {
      var path = NormalizePath(files[i]);
      var relative = path.StartsWith(sourceRoot + "/", StringComparison.OrdinalIgnoreCase)
        ? path.Substring(sourceRoot.Length + 1)
        : path;
      var key = RemoveExtension(relative);
      if (string.IsNullOrWhiteSpace(key)) continue;
      pathByNamepart[key] = path;
    }

    foreach (var pair in pathByNamepart) {
      if (IsNormalVariantNamepart(pair.Key, pathByNamepart)) continue;
      var guid = AssetDatabase.AssetPathToGUID(pair.Value);
      if (string.IsNullOrWhiteSpace(guid)) continue;
      var normalized = SpriteAddressResolver.NormalizeNamePart(pair.Key);
      if (string.IsNullOrWhiteSpace(normalized)) continue;
      map[guid] = normalized;
    }

    return map;
  }

  static bool IsNormalVariantNamepart(string key, Dictionary<string, string> byNamepart) {
    if (string.IsNullOrWhiteSpace(key) || byNamepart == null) return false;
    if (!key.EndsWith("N", StringComparison.OrdinalIgnoreCase)) return false;
    if (key.Length <= 1) return false;
    var colorCandidate = key.Substring(0, key.Length - 1);
    return byNamepart.ContainsKey(colorCandidate);
  }

  static string RemoveExtension(string value) {
    var normalized = NormalizePath(value);
    return normalized.EndsWith(".spriteLib", StringComparison.OrdinalIgnoreCase)
      ? normalized.Substring(0, normalized.Length - ".spriteLib".Length)
      : normalized;
  }

  static Dictionary<string, string> LoadManifestShardMap() {
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var manifestAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(SpriteStreamingConfig.ManifestAssetPath);
    var manifestText = manifestAsset != null ? manifestAsset.text : "";
    if (string.IsNullOrWhiteSpace(manifestText)) return map;

    var lines = manifestText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    for (var i = 0; i < lines.Length; i++) {
      var line = lines[i].TrimStart('\uFEFF');
      if (line.StartsWith("#", StringComparison.Ordinal)) continue;

      var cols = line.Split('\t');
      if (cols.Length < 3) continue;
      var namepart = SpriteAddressResolver.NormalizeNamePart(Unescape(cols[0]));
      var shardPath = NormalizePath(Unescape(cols[2]));
      if (string.IsNullOrWhiteSpace(namepart) || string.IsNullOrWhiteSpace(shardPath)) continue;
      map[namepart] = shardPath;
    }

    return map;
  }

  static HashSet<string> ResolveHotsetTextureAssetPaths(HashSet<string> requestedNameparts, Dictionary<string, string> shardPathByNamepart) {
    var texturePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (requestedNameparts == null || requestedNameparts.Count == 0) return texturePaths;
    if (shardPathByNamepart == null || shardPathByNamepart.Count == 0) return texturePaths;

    Debug.Log($"[SpriteStreamingHotsetConfigurator] ResolveHotsetTextureAssetPaths: Processing {requestedNameparts.Count} requested nameparts against {shardPathByNamepart.Count} shards.");
    var remappedRequestsLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var requested in requestedNameparts) {
      var normalizedRequested = SpriteAddressResolver.NormalizeNamePart(requested);
      if (string.IsNullOrWhiteSpace(normalizedRequested)) continue;
      if (!TryResolveShardPath(normalizedRequested, shardPathByNamepart, out var shardPath, out var resolvedNamepart)) {
        Debug.LogWarning($"[SpriteStreamingHotsetConfigurator] ResolveHotsetTextureAssetPaths: Could not resolve shard path for namepart '{normalizedRequested}'.");
        continue;
      }
      if (!string.Equals(normalizedRequested, resolvedNamepart, StringComparison.OrdinalIgnoreCase) &&
          remappedRequestsLogged.Add(normalizedRequested)) {
        Debug.LogWarning(
          $"[SpriteStreamingHotsetConfigurator] ResolveHotsetTextureAssetPaths: Remapped missing namepart '{normalizedRequested}' to '{resolvedNamepart}' by unique leaf name.");
      }

      var shardAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(shardPath);
      var shardText = shardAsset != null ? shardAsset.text : "";
      if (string.IsNullOrWhiteSpace(shardText)) continue;

      foreach (var rawLine in shardText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
        var line = rawLine.TrimStart('\uFEFF');
        if (line.StartsWith("#", StringComparison.Ordinal)) continue;

        var cols = line.Split('\t');
        if (cols.Length < 5) continue;
        var colorAssetPath = ExtractAssetPathFromAddress(Unescape(cols[3]));
        var normalAssetPath = ExtractAssetPathFromAddress(Unescape(cols[4]));

        if (!string.IsNullOrWhiteSpace(colorAssetPath) && File.Exists(colorAssetPath)) {
          texturePaths.Add(colorAssetPath);
        }
        if (!string.IsNullOrWhiteSpace(normalAssetPath) && File.Exists(normalAssetPath)) {
          texturePaths.Add(normalAssetPath);
        }
      }
    }

    return texturePaths;
  }

  static HashSet<string> CollectSourceRootTextureAssetPaths() {
    var texturePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var sourceRoot = NormalizePath(SpriteStreamingConfig.TextureSourceRootFolder);
    if (string.IsNullOrWhiteSpace(sourceRoot)) return texturePaths;

    var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { sourceRoot });
    for (var i = 0; i < guids.Length; i++) {
      var path = NormalizePath(AssetDatabase.GUIDToAssetPath(guids[i]));
      if (!string.IsNullOrWhiteSpace(path)) {
        if (path.StartsWith(NormalizePath(SpriteStreamingConfig.SourceRootFolder) + "/", StringComparison.OrdinalIgnoreCase)) continue;
        texturePaths.Add(path);
      }
    }

    return texturePaths;
  }

  static bool TryResolveShardPath(
    string requestedNamepart,
    Dictionary<string, string> shardPathByNamepart,
    out string shardPath,
    out string resolvedNamepart
  ) {
    shardPath = "";
    resolvedNamepart = "";
    if (string.IsNullOrWhiteSpace(requestedNamepart) || shardPathByNamepart == null || shardPathByNamepart.Count == 0) return false;

    if (shardPathByNamepart.TryGetValue(requestedNamepart, out shardPath) && !string.IsNullOrWhiteSpace(shardPath)) {
      resolvedNamepart = requestedNamepart;
      return true;
    }

    if (TryResolveUniqueLeafNamepart(requestedNamepart, shardPathByNamepart, out resolvedNamepart, out shardPath)) {
      return true;
    }

    return false;
  }

  static bool TryResolveUniqueLeafNamepart(
    string requestedNamepart,
    Dictionary<string, string> shardPathByNamepart,
    out string resolvedNamepart,
    out string shardPath
  ) {
    resolvedNamepart = "";
    shardPath = "";
    if (string.IsNullOrWhiteSpace(requestedNamepart) || shardPathByNamepart == null || shardPathByNamepart.Count == 0) return false;

    var slash = requestedNamepart.LastIndexOf('/');
    var leafName = slash >= 0 && slash < requestedNamepart.Length - 1
      ? SpriteAddressResolver.NormalizeNamePart(requestedNamepart.Substring(slash + 1))
      : SpriteAddressResolver.NormalizeNamePart(requestedNamepart);
    if (string.IsNullOrWhiteSpace(leafName)) return false;

    var suffix = "/" + leafName;
    string matchedNamepart = "";
    string matchedShardPath = "";
    foreach (var pair in shardPathByNamepart) {
      if (!string.Equals(pair.Key, leafName, StringComparison.OrdinalIgnoreCase) &&
          !pair.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      if (!string.IsNullOrWhiteSpace(matchedNamepart) &&
          !string.Equals(matchedNamepart, pair.Key, StringComparison.OrdinalIgnoreCase)) {
        return false;
      }

      matchedNamepart = pair.Key;
      matchedShardPath = pair.Value;
    }

    if (string.IsNullOrWhiteSpace(matchedNamepart) || string.IsNullOrWhiteSpace(matchedShardPath)) return false;

    resolvedNamepart = matchedNamepart;
    shardPath = matchedShardPath;
    return true;
  }

  static HashSet<string> ApplyStreamingImporterSettings(HashSet<string> texturePaths) {
    var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (texturePaths == null || texturePaths.Count == 0) return changed;

    var orderedPaths = texturePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();

    var pendingImportPaths = new List<string>();
    for (var i = 0; i < orderedPaths.Count; i++) {
      var texturePath = orderedPaths[i];
      EditorUtility.DisplayProgressBar("Sprite Streaming", "Updating sprite importers...", 0.8f + 0.2f * ((float)(i + 1) / orderedPaths.Count));

      var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
      if (importer == null) continue;

      var importerChanged = SpriteStreamingTextureImportPolicy.Apply(importer, forceMultipleSpriteImportMode: false);
      if (!importerChanged) continue;
      AssetDatabase.WriteImportSettingsIfDirty(texturePath);
      pendingImportPaths.Add(texturePath);
      changed.Add(texturePath);
    }

    if (pendingImportPaths.Count <= 0) {
      return changed;
    }

    try {
      AssetDatabase.StartAssetEditing();
      for (var i = 0; i < pendingImportPaths.Count; i++) {
        var texturePath = pendingImportPaths[i];
        EditorUtility.DisplayProgressBar("Sprite Streaming", "Reimporting changed textures...", 0.9f + 0.1f * ((float)(i + 1) / pendingImportPaths.Count));
        AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceUpdate);
      }
    }
    finally {
      AssetDatabase.StopAssetEditing();
    }

    LogTransparentTextureFormatAudit(changed, "ApplyStreamingImporterSettings");
    return changed;
  }

  static void LogTransparentTextureFormatAudit(HashSet<string> texturePaths, string context) {
    if (texturePaths == null || texturePaths.Count <= 0) return;

    var transparentTextureCount = 0;
    var explicitFormatTextureCount = 0;
    var explicitFormatPlatformCount = 0;

    foreach (var texturePath in texturePaths) {
      var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
      if (importer == null) continue;
      if (!importer.alphaIsTransparency) continue;

      transparentTextureCount++;
      if (!HasExplicitPlatformFormat(importer, out var platformDetails)) continue;

      explicitFormatTextureCount++;
      explicitFormatPlatformCount += platformDetails.Count;
      Debug.LogWarning(
        "[SpriteStreamingHotsetConfigurator] Transparent texture still has explicit platform format after policy apply." +
        " context=" + context +
        " path=" + texturePath +
        " platforms=" + string.Join(",", platformDetails)
      );
    }

    Debug.Log(
      "[SpriteStreamingHotsetConfigurator] Transparent texture format audit." +
      " context=" + context +
      " changedTextures=" + texturePaths.Count +
      " transparentTextures=" + transparentTextureCount +
      " texturesWithExplicitFormats=" + explicitFormatTextureCount +
      " explicitPlatformFormats=" + explicitFormatPlatformCount
    );
  }

  static bool HasExplicitPlatformFormat(TextureImporter importer, out List<string> platformDetails) {
    platformDetails = new List<string>();
    if (importer == null) return false;

    CollectExplicitPlatformFormat(importer, "Standalone", platformDetails);
    CollectExplicitPlatformFormat(importer, "Android", platformDetails);
    CollectExplicitPlatformFormat(importer, "iPhone", platformDetails);
    return platformDetails.Count > 0;
  }

  static void CollectExplicitPlatformFormat(TextureImporter importer, string platformName, List<string> platformDetails) {
    if (importer == null || string.IsNullOrWhiteSpace(platformName) || platformDetails == null) return;

    var settings = importer.GetPlatformTextureSettings(platformName);
    if (!settings.overridden) return;

    var serializedFormat = (int)settings.format;
    if (serializedFormat == AutomaticTextureFormat) return;

    platformDetails.Add(platformName + ":" + serializedFormat.ToString(CultureInfo.InvariantCulture));
  }

  static string EstimateSizeDeltaBucket(int changedTextureCount) {
    if (changedTextureCount <= 0) return "none";
    if (changedTextureCount < 250) return "small";
    if (changedTextureCount < 1500) return "medium";
    return "large";
  }

  static string ExtractAssetPathFromAddress(string address) {
    if (string.IsNullOrWhiteSpace(address)) return "";
    var normalized = NormalizePath(address);
    var bracket = normalized.LastIndexOf('[');
    if (bracket > 0 && normalized.EndsWith("]", StringComparison.Ordinal)) {
      normalized = normalized.Substring(0, bracket);
    }
    return normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ? normalized : "";
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
          trimmed = trimmed.Replace("''", "'");
        }
      }
    }
    return string.IsNullOrWhiteSpace(trimmed) ? "" : trimmed;
  }

  static string Unescape(string value) {
    if (string.IsNullOrEmpty(value)) return "";
    return value
      .Replace("\\t", "\t")
      .Replace("\\n", "\n")
      .Replace("\\r", "\r")
      .Replace("\\\\", "\\");
  }

  static string NormalizePath(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Replace('\\', '/').Trim();
  }
}
#endif
