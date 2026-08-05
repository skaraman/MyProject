#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static partial class SpriteIndexBuilder {

  static Dictionary<string, SpriteRef> ParseLibraryRows(string path, List<string> errors) {
    // Keep row keys case-sensitive so color/normal joins require exact label/category case.
    var rows = new Dictionary<string, SpriteRef>(StringComparer.Ordinal);
    var physicalPath = ContentPackPipeline.GetPhysicalPath(path);
    if (!File.Exists(physicalPath)) {
      errors.Add("Missing sprite library file '" + path + "'.");
      return rows;
    }

    string currentCategory = null;
    bool insideOverrideEntries = false;
    string currentLabel = null;
    SpriteRef? currentSpriteRef = null;

    void ClearLabel() {
      currentLabel = null;
      currentSpriteRef = null;
    }

    void FlushLabel() {
      if (string.IsNullOrWhiteSpace(currentCategory) || string.IsNullOrWhiteSpace(currentLabel)) {
        ClearLabel();
        return;
      }

      if (!currentSpriteRef.HasValue) {
        ClearLabel();
        return;
      }

      var key = currentCategory + "\u001f" + currentLabel;
      rows[key] = currentSpriteRef.Value;
      ClearLabel();
    }

    foreach (var rawLine in File.ReadLines(physicalPath)) {
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

      // Regex guard: skip regex matching if the line doesn't contain m_Sprite
      var spriteMatch = line.Contains("m_Sprite") ? spriteRefRegex.Match(line) : Match.Empty;
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

  static Dictionary<string, List<ShardRow>> DiscoverCustomSpriteSheetRows(BuildState state) {
    var result = new Dictionary<string, List<ShardRow>>(StringComparer.OrdinalIgnoreCase);
    var manifests = DiscoverStagedContentPackManifestPaths();
    for (var manifestIndex = 0; manifestIndex < manifests.Count; manifestIndex++) {
      var manifestPath = manifests[manifestIndex];
      var physicalManifestPath = ContentPackPipeline.GetPhysicalPath(manifestPath);
      if (string.IsNullOrWhiteSpace(physicalManifestPath) || !File.Exists(physicalManifestPath)) continue;

      CustomSheetManifest manifest;
      try {
        manifest = JsonUtility.FromJson<CustomSheetManifest>(File.ReadAllText(physicalManifestPath));
      }
      catch (Exception ex) {
        state?.errors.Add("Failed to parse content pack manifest '" + manifestPath + "'. error='" + ex.Message + "'");
        continue;
      }

      if (manifest?.authoringSources == null || manifest.authoringSources.Count <= 0) continue;

      var stageRoot = NormalizePath(Path.GetDirectoryName(manifestPath));
      for (var sourceIndex = 0; sourceIndex < manifest.authoringSources.Count; sourceIndex++) {
        AddCustomSpriteSheetRows(state, result, stageRoot, manifest.authoringSources[sourceIndex], manifestPath);
      }
    }

    return result;
  }

  static List<string> DiscoverStagedContentPackManifestPaths() {
    var result = new List<string>();
    var textureRoots = ContentPackPipeline.GetTextureSearchRoots();
    for (var i = 0; i < textureRoots.Count; i++) {
      var textureRoot = NormalizePath(textureRoots[i]).TrimEnd('/');
      if (string.IsNullOrWhiteSpace(textureRoot)) continue;

      var stageRoot = textureRoot.EndsWith("/Sprites", StringComparison.OrdinalIgnoreCase)
        ? textureRoot.Substring(0, textureRoot.Length - "/Sprites".Length)
        : textureRoot;
      var manifestPath = NormalizePath(stageRoot + "/ContentPackManifest.json");
      if (result.Contains(manifestPath, StringComparer.OrdinalIgnoreCase)) continue;
      if (!File.Exists(ContentPackPipeline.GetPhysicalPath(manifestPath))) continue;
      result.Add(manifestPath);
    }

    return result;
  }

  static void AddCustomSpriteSheetRows(
    BuildState state,
    Dictionary<string, List<ShardRow>> rowsByLibrary,
    string stageRoot,
    CustomSheetSource source,
    string manifestPath
  ) {
    if (source == null || rowsByLibrary == null) return;
    if (!string.Equals((source.sourceType ?? "").Trim(), "sprite_sheet", StringComparison.OrdinalIgnoreCase)) return;

    var libraryName = SpriteAddressResolver.NormalizeNamePart(source.libraryName);
    var category = (source.category ?? "").Trim();
    var labelPrefix = (source.labelPrefix ?? "").Trim();
    string labelFilter = (source.label ?? "").Trim();
    if (string.IsNullOrWhiteSpace(libraryName) ||
        string.IsNullOrWhiteSpace(category) ||
        string.IsNullOrWhiteSpace(labelPrefix)) {
      state?.errors.Add("Sprite sheet source is missing libraryName, category, or labelPrefix in '" + manifestPath + "'.");
      return;
    }

    var colorAssetPath = BuildStagedAuthoringSourceAssetPath(stageRoot, source.targetFolder, source.assetPath);
    var normalAssetPath = BuildStagedAuthoringSourceAssetPath(stageRoot, source.targetFolder, source.normalAssetPath);
    var specularAssetPath = BuildStagedAuthoringSourceAssetPath(stageRoot, source.targetFolder, source.specularAssetPath);
    if (string.IsNullOrWhiteSpace(source.normalAssetPath) || IsLegacyJpegSpriteAddress(normalAssetPath)) {
      if (SpriteStreamingTextureImportPolicy.TryGetPairedNormalAtlasPath(colorAssetPath, out var pairedNormalAssetPath) &&
          File.Exists(ContentPackPipeline.GetPhysicalPath(pairedNormalAssetPath))) {
        normalAssetPath = pairedNormalAssetPath;
      }
      else if (IsLegacyJpegSpriteAddress(normalAssetPath)) {
        normalAssetPath = "";
      }
    }
    if (string.IsNullOrWhiteSpace(source.specularAssetPath) || IsLegacyJpegSpriteAddress(specularAssetPath)) {
      if (SpriteStreamingTextureImportPolicy.TryGetPairedSpecularAtlasPath(colorAssetPath, out var pairedSpecularAssetPath) &&
          File.Exists(ContentPackPipeline.GetPhysicalPath(pairedSpecularAssetPath))) {
        specularAssetPath = pairedSpecularAssetPath;
      }
      else if (IsLegacyJpegSpriteAddress(specularAssetPath)) {
        specularAssetPath = "";
      }
    }
    if (string.IsNullOrWhiteSpace(colorAssetPath) || !File.Exists(ContentPackPipeline.GetPhysicalPath(colorAssetPath))) {
      state?.errors.Add("Sprite sheet source texture was not found. manifest='" + manifestPath + "' asset='" + colorAssetPath + "'");
      return;
    }

    var colorSpriteNames = ReadSpriteNamesFromTextureMeta(colorAssetPath);
    if (colorSpriteNames.Count <= 0) {
      colorSpriteNames.Add(Path.GetFileNameWithoutExtension(colorAssetPath));
    }

    var normalSpriteNames = new HashSet<string>(StringComparer.Ordinal);
    if (!string.IsNullOrWhiteSpace(source.normalAssetPath) &&
        (string.IsNullOrWhiteSpace(normalAssetPath) || !File.Exists(ContentPackPipeline.GetPhysicalPath(normalAssetPath)))) {
      state?.errors.Add("Sprite sheet normal texture was not found. manifest='" + manifestPath + "' asset='" + normalAssetPath + "'");
    }
    else if (!string.IsNullOrWhiteSpace(normalAssetPath) && File.Exists(ContentPackPipeline.GetPhysicalPath(normalAssetPath))) {
      var names = ReadSpriteNamesFromTextureMeta(normalAssetPath);
      for (var i = 0; i < names.Count; i++) {
        normalSpriteNames.Add(names[i]);
      }
      if (normalSpriteNames.Count <= 0) {
        normalSpriteNames.Add(Path.GetFileNameWithoutExtension(normalAssetPath));
      }
    }

    var specularSpriteNames = new HashSet<string>(StringComparer.Ordinal);
    if (!string.IsNullOrWhiteSpace(source.specularAssetPath) &&
        (string.IsNullOrWhiteSpace(specularAssetPath) || !File.Exists(ContentPackPipeline.GetPhysicalPath(specularAssetPath)))) {
      state?.errors.Add("Sprite sheet specular texture was not found. manifest='" + manifestPath + "' asset='" + specularAssetPath + "'");
    }
    else if (!string.IsNullOrWhiteSpace(specularAssetPath) && File.Exists(ContentPackPipeline.GetPhysicalPath(specularAssetPath))) {
      var names = ReadSpriteNamesFromTextureMeta(specularAssetPath);
      for (var i = 0; i < names.Count; i++) {
        specularSpriteNames.Add(names[i]);
      }
      if (specularSpriteNames.Count <= 0) {
        specularSpriteNames.Add(Path.GetFileNameWithoutExtension(specularAssetPath));
      }
    }

    if (!rowsByLibrary.TryGetValue(libraryName, out var rows)) {
      rows = new List<ShardRow>();
      rowsByLibrary[libraryName] = rows;
    }

    if (state != null) {
      state.activeTextureAssetPaths.Add(colorAssetPath);
    }
    if (state != null && !string.IsNullOrWhiteSpace(normalAssetPath) && normalSpriteNames.Count > 0) {
      state.activeTextureAssetPaths.Add(normalAssetPath);
    }
    if (state != null && !string.IsNullOrWhiteSpace(specularAssetPath) && specularSpriteNames.Count > 0) {
      state.activeTextureAssetPaths.Add(specularAssetPath);
    }

    for (var i = 0; i < colorSpriteNames.Count; i++) {
      var spriteName = colorSpriteNames[i];
      if (string.IsNullOrWhiteSpace(spriteName)) continue;
      if (!ShouldIncludeCustomSpriteSheetSprite(spriteName, labelFilter)) continue;

      ParseLabel(spriteName, out _, out var frame);
      var colorAddress = SpriteSliceAddressUtility.BuildSliceAddress(colorAssetPath, spriteName);
      var normalAddress = "";
      if (!string.IsNullOrWhiteSpace(normalAssetPath)) {
        var normalSpriteName = normalSpriteNames.Contains(spriteName)
          ? spriteName
          : colorSpriteNames.Count == 1 && normalSpriteNames.Count == 1
            ? normalSpriteNames.First()
            : "";
        if (!string.IsNullOrWhiteSpace(normalSpriteName)) {
          normalAddress = SpriteSliceAddressUtility.BuildSliceAddress(normalAssetPath, normalSpriteName);
        }
      }
      var specularAddress = "";
      if (!string.IsNullOrWhiteSpace(specularAssetPath)) {
        var specularSpriteName = specularSpriteNames.Contains(spriteName)
          ? spriteName
          : colorSpriteNames.Count == 1 && specularSpriteNames.Count == 1
            ? specularSpriteNames.First()
            : "";
        if (!string.IsNullOrWhiteSpace(specularSpriteName)) {
          specularAddress = SpriteSliceAddressUtility.BuildSliceAddress(specularAssetPath, specularSpriteName);
        }
      }

      if (!ValidateRuntimeAtlasAddress(state, colorAddress, libraryName + "/" + category + ":" + spriteName + " (sheet color)", recordError: true)) {
        continue;
      }
      if (!string.IsNullOrWhiteSpace(normalAddress) &&
          !ValidateRuntimeAtlasAddress(state, normalAddress, libraryName + "/" + category + ":" + spriteName + " (sheet normal)", recordError: false)) {
        normalAddress = "";
      }
      if (!string.IsNullOrWhiteSpace(specularAddress) &&
          !ValidateRuntimeAtlasAddress(state, specularAddress, libraryName + "/" + category + ":" + spriteName + " (sheet specular)", recordError: false)) {
        specularAddress = "";
      }

      rows.Add(new ShardRow(labelPrefix, category, frame, colorAddress, normalAddress, specularAddress));
    }
  }

  static bool ShouldIncludeCustomSpriteSheetSprite(string spriteName, string labelFilter) {
    if (string.IsNullOrWhiteSpace(labelFilter)) return true;
    if (string.IsNullOrWhiteSpace(spriteName)) return false;

    return string.Equals(
      spriteName.Trim(),
      labelFilter.Trim(),
      StringComparison.Ordinal
    );
  }

  static string BuildStagedAuthoringSourceAssetPath(string stageRoot, string targetFolder, string assetPath) {
    var normalizedStageRoot = NormalizePath(stageRoot).TrimEnd('/');
    var normalizedTargetFolder = NormalizeAuthoringTargetFolder(targetFolder);
    var normalizedAssetPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedStageRoot) ||
        string.IsNullOrWhiteSpace(normalizedTargetFolder) ||
        string.IsNullOrWhiteSpace(normalizedAssetPath)) {
      return "";
    }

    return NormalizePath(normalizedStageRoot + "/" + normalizedTargetFolder + "/" + Path.GetFileName(normalizedAssetPath));
  }

  static string NormalizeAuthoringTargetFolder(string targetFolder) {
    var normalized = NormalizePath(targetFolder).Trim('/');
    if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) {
      normalized = normalized.Substring("Assets/".Length);
    }
    var segments = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
    if (segments.Length > 1 && string.Equals(segments[0], "Core", StringComparison.OrdinalIgnoreCase)) {
      normalized = string.Join("/", segments.Skip(1));
    }
    else if (segments.Length > 2 &&
             (string.Equals(segments[0], "Forms", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(segments[0], "Gears", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(segments[0], "Slices", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(segments[0], "Episodes", StringComparison.OrdinalIgnoreCase))) {
      normalized = string.Join("/", segments.Skip(2));
    }
    return NormalizePath(normalized).Trim('/');
  }

  static List<string> ReadSpriteNamesFromTextureMeta(string assetPath) {
    var result = new List<string>();
    var physicalMetaPath = ContentPackPipeline.GetPhysicalPath(assetPath + ".meta");
    var byFileId = ReadSpriteNamesByFileIdFromMeta(physicalMetaPath);
    foreach (var spriteName in byFileId.Values) {
      if (!string.IsNullOrWhiteSpace(spriteName)) {
        result.Add(spriteName);
      }
    }
    result.Sort(SpriteSliceAddressUtility.NaturalStringComparer);
    return result;
  }

  static HashSet<string> GetCachedSpriteNamesFromTextureMeta(BuildState state, string assetPath) {
    var normalizedAssetPath = NormalizePath(assetPath);
    if (state == null || string.IsNullOrWhiteSpace(normalizedAssetPath)) {
      return new HashSet<string>(StringComparer.Ordinal);
    }

    if (state.spriteNamesByTextureAssetPath.TryGetValue(normalizedAssetPath, out var cachedNames)) {
      return cachedNames;
    }

    var names = ReadSpriteNamesFromTextureMeta(normalizedAssetPath);
    cachedNames = new HashSet<string>(names, StringComparer.Ordinal);
    state.spriteNamesByTextureAssetPath[normalizedAssetPath] = cachedNames;
    return cachedNames;
  }

  static bool TryResolveCompanionSpriteName(
    BuildState state,
    string companionAtlasPath,
    string colorSpriteName,
    out string companionSpriteName
  ) {
    companionSpriteName = "";
    var normalizedAtlasPath = NormalizePath(companionAtlasPath);
    if (string.IsNullOrWhiteSpace(normalizedAtlasPath) || string.IsNullOrWhiteSpace(colorSpriteName)) {
      return false;
    }

    var companionSpriteNames = GetCachedSpriteNamesFromTextureMeta(state, normalizedAtlasPath);
    if (companionSpriteNames.Contains(colorSpriteName)) {
      companionSpriteName = colorSpriteName;
      return true;
    }

    var singleSpriteName = Path.GetFileNameWithoutExtension(normalizedAtlasPath);
    if (companionSpriteNames.Count == 0 ||
        (companionSpriteNames.Count == 1 && companionSpriteNames.Contains(singleSpriteName))) {
      companionSpriteName = singleSpriteName;
      return true;
    }

    return false;
  }

  static string ResolveSpriteAddress(BuildState state, SpriteRef spriteRef, string context, bool recordError = true) {
    if (string.IsNullOrWhiteSpace(spriteRef.guid)) {
      if (recordError) {
        state.errors.Add("Missing GUID while resolving .");
      }
      return "";
    }

    var byFileId = GetAddressMapForGuid(state, spriteRef.guid, recordError);

    if (byFileId.TryGetValue(spriteRef.fileId, out var address)) {
      return address;
    }

    var targetUnsigned = unchecked((ulong)spriteRef.fileId);
    foreach (var pair in byFileId) {
      if (unchecked((ulong)pair.Key) != targetUnsigned) continue;
      return pair.Value;
    }

    var fallbackAddress = TryResolveSpriteAddressFromContext(byFileId, context);
    if (!string.IsNullOrWhiteSpace(fallbackAddress)) {
      return fallbackAddress;
    }

    fallbackAddress = TryResolveActiveSpriteAddressByFileId(state, spriteRef.fileId, context);
    if (!string.IsNullOrWhiteSpace(fallbackAddress)) {
      return fallbackAddress;
    }

    if (recordError) {
      Debug.LogWarning($"[SpriteIndexBuilder] ResolveSpriteAddress: Failed to resolve FileID {spriteRef.fileId} in GUID {spriteRef.guid}. Context: {context}. Map size: {byFileId.Count}.");
    }

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
    if (string.IsNullOrWhiteSpace(sliceAddress)) {
      failureReason = "Empty slice address.";
      if (recordError) state.errors.Add("Empty atlas address in context '" + context + "'.");
      return false;
    }

    // Runtime shards must point to the staged atlas owner plus exact sprite name.
    if (!SpriteSliceAddressUtility.TryParseSliceAddress(sliceAddress, out var parsedAtlasAssetPath, out var spriteName)) {
      failureReason = "Runtime sprite address must use Assets/.../atlas.png[spriteName] format.";
      if (recordError) {
        state.errors.Add(
          "Invalid runtime sprite address '" + sliceAddress + "'" +
          " in context '" + context + "'. " + failureReason
        );
      }
      return false;
    }

    var atlasAssetPath = NormalizePath(parsedAtlasAssetPath);
    if (string.IsNullOrWhiteSpace(spriteName) || string.IsNullOrWhiteSpace(atlasAssetPath)) {
      failureReason = "Runtime sprite address is missing an atlas path or sprite name.";
      if (recordError) {
        state.errors.Add(
          "Invalid runtime sprite address '" + sliceAddress + "'" +
          " in context '" + context + "'. " + failureReason
        );
      }
      return false;
    }

    if (!IsActiveRuntimeTextureAssetPath(atlasAssetPath)) {
      failureReason = "Atlas path is not under the active runtime texture roots.";
      if (recordError) {
        state.errors.Add(
          "Invalid runtime atlas path '" + atlasAssetPath + "'" +
          " in context '" + context + "'. " + failureReason
        );
      }
      return false;
    }

    if (state.activeTextureAssetPaths.Contains(atlasAssetPath)) return true;

    failureReason = "No staged texture entry found for atlas path '" + atlasAssetPath + "'.";
    if (recordError) {
      state.errors.Add(
        "Unresolved runtime atlas path '" + atlasAssetPath + "'" +
        " in context '" + context + "'."
      );
    }
    return false;
  }

  // ─── Address map cache ────────────────────────────────────────────────────

  static Dictionary<long, string> GetAddressMapForGuid(BuildState state, string guid, bool recordError) {
    if (state.addressCacheByGuid.TryGetValue(guid, out var cached)) return cached;

    var map = new Dictionary<long, string>();
    state.addressCacheByGuid[guid] = map;

    BuildActiveTextureGuidIndex(state);
    if (!state.activeTextureAssetPathByGuid.TryGetValue(guid, out var activeTextureAssetPath)) {
      if (recordError) state.errors.Add("GUID '" + guid + "' did not resolve to an asset path.");
      return map;
    }

    var assetPath = NormalizePath(activeTextureAssetPath);
    if (string.IsNullOrWhiteSpace(assetPath)) return map;
    if (!IsActiveRuntimeTextureAssetPath(assetPath)) return map;
    state.activeTextureAssetPaths.Add(assetPath);

    // High performance sub-asset loading: load ONLY sprite representations without textures
    var objects = AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath);
    var foundSprite = false;
    for (var i = 0; i < objects.Length; i++) {
      if (objects[i] is not UnityEngine.Sprite sprite) continue;
      foundSprite = true;
      if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out var objGuid, out long fileId)) continue;
      if (!string.Equals(objGuid, guid, StringComparison.OrdinalIgnoreCase)) continue;

      var spriteName = string.IsNullOrWhiteSpace(sprite.name) ? Path.GetFileNameWithoutExtension(assetPath) : sprite.name.Trim();
      var address = SpriteSliceAddressUtility.BuildSliceAddress(assetPath, spriteName);
      if (string.IsNullOrWhiteSpace(address)) continue;
      map[fileId] = address;
    }

    // Safe fallback to loading the main asset if it was imported as a single sprite
    if (!foundSprite) {
      var mainSprite = AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(assetPath);
      if (mainSprite != null) {
        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mainSprite, out var objGuid, out long fileId)) {
          if (string.Equals(objGuid, guid, StringComparison.OrdinalIgnoreCase)) {
            var spriteName = string.IsNullOrWhiteSpace(mainSprite.name) ? Path.GetFileNameWithoutExtension(assetPath) : mainSprite.name.Trim();
            var address = SpriteSliceAddressUtility.BuildSliceAddress(assetPath, spriteName);
            if (!string.IsNullOrWhiteSpace(address)) {
              map[fileId] = address;
            }
          }
        }
      }
    }

    AddMetaSpriteAddressMap(state, guid, map);

    // Check runtimeTextureAssetPathBySourceAssetPath for remapped paths
    if (state.runtimeTextureAssetPathBySourceAssetPath.TryGetValue(assetPath, out var remappedPath)) {
      var remappedGuid = AssetDatabase.AssetPathToGUID(remappedPath);
      if (!string.IsNullOrWhiteSpace(remappedGuid) && !state.addressCacheByGuid.ContainsKey(remappedGuid)) {
        // Populate remapped entry (don't recurse infinitely)
        state.addressCacheByGuid[remappedGuid] = map;
      }
    }

    return map;
  }

  static void AddMetaSpriteAddressMap(BuildState state, string guid, Dictionary<long, string> map) {
    if (state == null || map == null || string.IsNullOrWhiteSpace(guid)) return;
    if (!state.activeSpriteAddressByFileIdByGuid.TryGetValue(guid, out var metaMap)) return;

    foreach (var pair in metaMap) {
      if (map.ContainsKey(pair.Key)) continue;
      if (string.IsNullOrWhiteSpace(pair.Value)) continue;
      map[pair.Key] = pair.Value;
    }
  }

  static bool IsRuntimeTextureAssetPath(string assetPath) {
    var normalizedPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedPath)) return false;
    if (!normalizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
        !normalizedPath.StartsWith("Packages/com.skaraman.myprojectcontent/", StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    var extension = Path.GetExtension(normalizedPath);
    return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase);
  }

  static bool IsActiveRuntimeTextureAssetPath(string assetPath) {
    var normalizedPath = NormalizePath(assetPath);
    if (!IsRuntimeTextureAssetPath(normalizedPath)) return false;

    CacheTextureRoots();
    if (cachedTextureRootsSet == null) return false;

    foreach (var root in cachedTextureRootsSet) {
      if (string.Equals(normalizedPath, root, StringComparison.OrdinalIgnoreCase) ||
          normalizedPath.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
  }

  static string TryResolveSpriteAddressFromContext(Dictionary<long, string> byFileId, string context) {
    if (byFileId == null || byFileId.Count == 0 || string.IsNullOrWhiteSpace(context)) return "";
    var spaceIdx = context.IndexOf(' ');
    var trimmedContext = spaceIdx > 0 ? context.Substring(0, spaceIdx) : context;
    
    // Support both "category:label" and "category/label" format by extracting segment after the last colon or slash
    var lastColon = trimmedContext.LastIndexOf(':');
    var lastSlash = trimmedContext.LastIndexOf('/');
    var candidateIdx = Math.Max(lastColon, lastSlash);
    var candidate = candidateIdx >= 0 ? trimmedContext.Substring(candidateIdx + 1) : trimmedContext;

    foreach (var pair in byFileId) {
      if (string.IsNullOrWhiteSpace(pair.Value)) continue;
      var addrLeaf = pair.Value;
      if (!SpriteSliceAddressUtility.TryParseSliceAddress(addrLeaf, out _, out addrLeaf)) {
        var addrSlash = addrLeaf.LastIndexOf('/');
        if (addrSlash >= 0) addrLeaf = addrLeaf.Substring(addrSlash + 1);
      }
      if (string.Equals(addrLeaf, candidate, StringComparison.OrdinalIgnoreCase)) return pair.Value;
    }
    return "";
  }

  static string TryResolveActiveSpriteAddressByFileId(BuildState state, long fileId, string context) {
    if (state == null) return "";
    BuildActiveTextureGuidIndex(state);
    if (!state.activeSpriteAddressesByFileId.TryGetValue(fileId, out var candidates) || candidates.Count == 0) {
      return "";
    }

    if (candidates.Count == 1) {
      MarkActiveTextureAddress(state, candidates[0]);
      return candidates[0];
    }

    var tokens = ExtractContextTokens(context);
    var bestScore = 0;
    var bestCount = 0;
    string bestAddress = "";
    for (var i = 0; i < candidates.Count; i++) {
      var score = ScoreSpriteAddressForContext(candidates[i], tokens, context);
      if (score > bestScore) {
        bestScore = score;
        bestCount = 1;
        bestAddress = candidates[i];
      }
      else if (score == bestScore && score > 0) {
        bestCount++;
      }
    }

    if (bestScore <= 0 || bestCount != 1 || string.IsNullOrWhiteSpace(bestAddress)) {
      return "";
    }

    MarkActiveTextureAddress(state, bestAddress);
    return bestAddress;
  }

  static List<string> ExtractContextTokens(string context) {
    var result = new List<string>();
    if (string.IsNullOrWhiteSpace(context)) return result;

    var spaceIdx = context.IndexOf(' ');
    var prefix = spaceIdx > 0 ? context.Substring(0, spaceIdx) : context;
    var colonIdx = prefix.IndexOf(':');
    var pathPart = colonIdx >= 0 ? prefix.Substring(0, colonIdx) : prefix;
    AddContextTokens(result, pathPart);
    if (colonIdx >= 0 && colonIdx < prefix.Length - 1) {
      AddContextTokens(result, prefix.Substring(colonIdx + 1));
    }
    return result;
  }

  static void AddContextTokens(List<string> result, string value) {
    if (result == null || string.IsNullOrWhiteSpace(value)) return;

    var start = 0;
    for (var i = 0; i <= value.Length; i++) {
      if (i < value.Length && char.IsLetterOrDigit(value[i])) continue;
      AddContextToken(result, value.Substring(start, i - start));
      start = i + 1;
    }
  }

  static void AddContextToken(List<string> result, string token) {
    var normalized = (token ?? "").Trim().ToLowerInvariant();
    if (!IsContextTokenUseful(normalized)) return;

    AddUniqueContextToken(result, normalized);
    if (normalized.Length > 3 && normalized.EndsWith("s", StringComparison.Ordinal)) {
      AddUniqueContextToken(result, normalized.Substring(0, normalized.Length - 1));
    }
  }

  static bool IsContextTokenUseful(string normalized) {
    if (string.IsNullOrWhiteSpace(normalized) || normalized.Length < 2) return false;
    return !string.Equals(normalized, "core", StringComparison.Ordinal) &&
           !string.Equals(normalized, "sprite", StringComparison.Ordinal) &&
           !string.Equals(normalized, "sprites", StringComparison.Ordinal) &&
           !string.Equals(normalized, "spritelibraries", StringComparison.Ordinal) &&
           !string.Equals(normalized, "color", StringComparison.Ordinal) &&
           !string.Equals(normalized, "normal", StringComparison.Ordinal);
  }

  static void AddUniqueContextToken(List<string> result, string normalized) {
    if (result == null || string.IsNullOrWhiteSpace(normalized)) return;
    for (var i = 0; i < result.Count; i++) {
      if (string.Equals(result[i], normalized, StringComparison.Ordinal)) return;
    }
    result.Add(normalized);
  }

  static int ScoreSpriteAddressForContext(string address, List<string> tokens, string context) {
    if (tokens == null || tokens.Count == 0 || string.IsNullOrWhiteSpace(address)) return 0;

    var atlasPath = NormalizePath(address).ToLowerInvariant();
    var spriteName = "";
    if (SpriteSliceAddressUtility.TryParseSliceAddress(address, out var parsedAtlasPath, out var parsedSpriteName)) {
      atlasPath = NormalizePath(parsedAtlasPath).ToLowerInvariant();
      spriteName = (parsedSpriteName ?? "").Trim().ToLowerInvariant();
    }

    var score = 0;
    for (var i = 0; i < tokens.Count; i++) {
      var token = tokens[i];
      if (atlasPath.Contains("/" + token + "/", StringComparison.Ordinal)) {
        score += 12;
      }
      else if (atlasPath.Contains(token, StringComparison.Ordinal)) {
        score += 3;
      }

      if (string.Equals(spriteName, token, StringComparison.Ordinal)) {
        score += 6;
      }
      else if (!string.IsNullOrWhiteSpace(spriteName) && spriteName.Contains(token, StringComparison.Ordinal)) {
        score += 1;
      }
    }

    var extension = Path.GetExtension(atlasPath);
    if (!string.IsNullOrWhiteSpace(context) &&
        context.Contains("(color)", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)) {
      score += 2;
    }
    else if (!string.IsNullOrWhiteSpace(context) &&
             context.Contains("(normal)", StringComparison.OrdinalIgnoreCase) &&
             IsConventionNormalPngAssetPath(atlasPath)) {
      score += 2;
    }

    return score;
  }

  static void MarkActiveTextureAddress(BuildState state, string sliceAddress) {
    if (state == null || string.IsNullOrWhiteSpace(sliceAddress)) return;
    if (!SpriteSliceAddressUtility.TryParseSliceAddress(sliceAddress, out var atlasAssetPath, out _)) return;

    atlasAssetPath = NormalizePath(atlasAssetPath);
    if (IsActiveRuntimeTextureAssetPath(atlasAssetPath)) {
      state.activeTextureAssetPaths.Add(atlasAssetPath);
    }
  }

  static bool TryResolveDerivedNormalAddress(BuildState state, string colorAddress, out string normalAddress) {
    normalAddress = "";
    if (string.IsNullOrWhiteSpace(colorAddress)) return false;

    if (SpriteSliceAddressUtility.TryParseSliceAddress(colorAddress, out var colorAtlasPath, out var spriteName)) {
      colorAtlasPath = NormalizePath(colorAtlasPath);
      if (!state.pairedNormalAtlasPathByColorAtlasPath.TryGetValue(colorAtlasPath, out var normalAtlasPath)) {
        normalAtlasPath = "";
        if (SpriteStreamingTextureImportPolicy.TryGetPairedNormalAtlasPath(colorAtlasPath, out var candidateNormalAtlasPath)) {
          candidateNormalAtlasPath = NormalizePath(candidateNormalAtlasPath);
          if (state.activeTextureAssetPaths.Contains(candidateNormalAtlasPath)) {
            normalAtlasPath = candidateNormalAtlasPath;
          }
        }
        state.pairedNormalAtlasPathByColorAtlasPath[colorAtlasPath] = normalAtlasPath;
      }

      if (!string.IsNullOrWhiteSpace(normalAtlasPath)) {
        if (!TryResolveCompanionSpriteName(state, normalAtlasPath, spriteName, out var normalSpriteName)) {
          return false;
        }
        normalAddress = SpriteSliceAddressUtility.BuildSliceAddress(normalAtlasPath, normalSpriteName);
        return true;
      }
    }

    // Heuristic: replace "/Color/" or "_Color" with "/Normal/" or "_Normal" in address
    string candidate = null;
    if (colorAddress.Contains("/Color/", StringComparison.OrdinalIgnoreCase)) {
      candidate = colorAddress.Replace("/Color/", "/Normal/");
    }
    else if (colorAddress.EndsWith("_Color", StringComparison.OrdinalIgnoreCase)) {
      candidate = colorAddress.Substring(0, colorAddress.Length - "_Color".Length) + "_Normal";
    }

    if (!string.IsNullOrWhiteSpace(candidate)) {
      foreach (var guidEntry in state.addressCacheByGuid) {
        foreach (var pair in guidEntry.Value) {
          if (string.Equals(pair.Value, candidate, StringComparison.OrdinalIgnoreCase)) {
            normalAddress = candidate;
            return true;
          }
        }
      }
    }

    return false;
  }

  static bool TryResolveDerivedSpecularAddress(BuildState state, string colorAddress, out string specularAddress) {
    specularAddress = "";
    if (string.IsNullOrWhiteSpace(colorAddress)) return false;

    if (SpriteSliceAddressUtility.TryParseSliceAddress(colorAddress, out var colorAtlasPath, out var spriteName)) {
      colorAtlasPath = NormalizePath(colorAtlasPath);
      if (!state.pairedSpecularAtlasPathByColorAtlasPath.TryGetValue(colorAtlasPath, out var specularAtlasPath)) {
        specularAtlasPath = "";
        if (SpriteStreamingTextureImportPolicy.TryGetPairedSpecularAtlasPath(colorAtlasPath, out var candidateSpecularAtlasPath)) {
          candidateSpecularAtlasPath = NormalizePath(candidateSpecularAtlasPath);
          if (state.activeTextureAssetPaths.Contains(candidateSpecularAtlasPath)) {
            specularAtlasPath = candidateSpecularAtlasPath;
          }
        }
        state.pairedSpecularAtlasPathByColorAtlasPath[colorAtlasPath] = specularAtlasPath;
      }

      if (!string.IsNullOrWhiteSpace(specularAtlasPath)) {
        if (!TryResolveCompanionSpriteName(state, specularAtlasPath, spriteName, out var specularSpriteName)) {
          return false;
        }
        specularAddress = SpriteSliceAddressUtility.BuildSliceAddress(specularAtlasPath, specularSpriteName);
        return true;
      }
    }

    string candidate = null;
    if (colorAddress.Contains("/Color/", StringComparison.OrdinalIgnoreCase)) {
      candidate = colorAddress.Replace("/Color/", "/Specular/");
    }
    else if (colorAddress.EndsWith("_Color", StringComparison.OrdinalIgnoreCase)) {
      candidate = colorAddress.Substring(0, colorAddress.Length - "_Color".Length) + "_Specular";
    }

    if (!string.IsNullOrWhiteSpace(candidate)) {
      foreach (var guidEntry in state.addressCacheByGuid) {
        foreach (var pair in guidEntry.Value) {
          if (string.Equals(pair.Value, candidate, StringComparison.OrdinalIgnoreCase)) {
            specularAddress = candidate;
            return true;
          }
        }
      }
    }

    return false;
  }

  static bool IsLegacyJpegSpriteAddress(string spriteAddress) {
    if (string.IsNullOrWhiteSpace(spriteAddress)) return false;
    var atlasPath = spriteAddress;
    if (SpriteSliceAddressUtility.TryParseSliceAddress(spriteAddress, out var parsedAtlasPath, out _)) {
      atlasPath = parsedAtlasPath;
    }
    var extension = Path.GetExtension(atlasPath);
    return string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase);
  }

  static bool IsConventionNormalPngAssetPath(string assetPath) {
    return !string.IsNullOrWhiteSpace(assetPath) &&
           string.Equals(Path.GetExtension(assetPath), ".png", StringComparison.OrdinalIgnoreCase) &&
           Path.GetFileNameWithoutExtension(assetPath).EndsWith("N", StringComparison.OrdinalIgnoreCase);
  }

}
#endif
