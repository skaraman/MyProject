#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed partial class GroupAtlasWindow : EditorWindow {
  bool TryWriteMetadata(
    string atlasAssetPath,
    GroupCandidate candidate,
    string representativeSourceAtlasAssetPath,
    AtlasPage page,
    bool isNormalMetadata,
    out string metadataAssetPath,
    out string editorImportMetadataJson,
    out string error) {
    metadataAssetPath = "";
    editorImportMetadataJson = "";
    error = "";
    var sourceCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var sourceAtlasPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (page?.items != null) {
      for (var i = 0; i < page.items.Count; i++) {
        var item = page.items[i];
        if (item == null) continue;
        if (!string.IsNullOrWhiteSpace(item.sourceCategory)) {
          sourceCategories.Add(item.sourceCategory.Trim());
        }

        var sourceAtlasPath = NormalizePath(isNormalMetadata ? item.normalSourceAtlasPath : item.colorSourceAtlasPath);
        if (!string.IsNullOrWhiteSpace(sourceAtlasPath)) {
          sourceAtlasPaths.Add(sourceAtlasPath);
        }
      }
    }

    var orderedSourceCategories = new List<string>(sourceCategories);
    orderedSourceCategories.Sort(SpriteSliceAddressUtility.NaturalStringComparer);
    var primarySourceCategory = orderedSourceCategories.Count > 0
      ? orderedSourceCategories[0]
      : (candidate.sourceCategories != null && candidate.sourceCategories.Count > 0 ? candidate.sourceCategories[0] : "");

    var payload = new GroupedAtlasMetadataPayload {
      groupKey = IsSkinCandidate(candidate) ? SkinGroupKey : BuildOutputFilePrefix(candidate),
      category = primarySourceCategory,
      form = candidate.form,
      variant = candidate.variant,
      partCode = candidate.partCode,
      fileBase = "",
      sourceKind = isNormalMetadata ? "normal" : "color",
      representativeSourceAtlasAssetPath = NormalizePath(representativeSourceAtlasAssetPath),
      sourceAtlasCount = sourceAtlasPaths.Count > 0 ? sourceAtlasPaths.Count : candidate.sourceAtlases?.Count ?? 0,
      pageIndex = page.pageIndex,
      atlasWidth = page.width,
      atlasHeight = page.height,
      padding = padding
    };
    TrimmedAtlasExporterWindow.GetSourceImporterSnapshot(representativeSourceAtlasAssetPath, out payload.spritePixelsPerUnit, out payload.spriteMeshType);
    if (orderedSourceCategories.Count > 0) {
      payload.sourceCategories.AddRange(orderedSourceCategories);
    }
    else if (candidate.sourceCategories != null && candidate.sourceCategories.Count > 0) {
      payload.sourceCategories.AddRange(candidate.sourceCategories);
    }

    var orderedMetadataItems = new List<PackedSpriteBuildItem>();
    for (var i = 0; i < page.items.Count; i++) {
      var item = page.items[i];
      if (item != null) {
        orderedMetadataItems.Add(item);
      }
    }
    orderedMetadataItems.Sort(CompareMetadataItems);
    for (var i = 0; i < orderedMetadataItems.Count; i++) {
      var item = orderedMetadataItems[i];
      payload.sprites.Add(new GroupedAtlasSpriteMetadata {
        name = item.outputSpriteName,
        empty = item.empty,
        sourceCategory = item.sourceCategory,
        sourceAtlasAssetPath = isNormalMetadata ? item.normalSourceAtlasPath : item.colorSourceAtlasPath,
        sourceSpriteName = item.sourceSpriteName,
        sourcePartCode = item.sourcePartCode,
        trimRectInSourceSprite = item.trimRectInSourceSprite,
        packedRect = item.packedRect,
        offsetFromCellCenterPx = item.offsetFromCellCenterPx
      });
    }

    try {
      metadataAssetPath = BuildRuntimeMetadataAssetPath(atlasAssetPath);
      editorImportMetadataJson = JsonUtility.ToJson(payload, true);
      WriteJsonPayload(metadataAssetPath, editorImportMetadataJson);
      DeleteEditorMetadataAsset(atlasAssetPath);
      return true;
    }
    catch (Exception ex) {
      error = ex.Message;
      return false;
    }
  }

  static GroupedAtlasRuntimePayload BuildRuntimeGroupedMetadata(GroupedAtlasMetadataPayload payload) {
    var runtimePayload = new GroupedAtlasRuntimePayload {
      metadataKind = payload?.metadataKind ?? "grouped",
      spritePixelsPerUnit = payload?.spritePixelsPerUnit ?? 100f,
      spriteMeshType = payload?.spriteMeshType ?? (int)SpriteMeshType.Tight
    };
    if (payload?.sprites == null || payload.sprites.Count <= 0) return runtimePayload;

    runtimePayload.sprites.Capacity = payload.sprites.Count;
    for (var i = 0; i < payload.sprites.Count; i++) {
      var sprite = payload.sprites[i];
      if (sprite == null || string.IsNullOrWhiteSpace(sprite.name)) continue;
      runtimePayload.sprites.Add(new GroupedAtlasRuntimeSpriteMetadata {
        name = sprite.name,
        empty = sprite.empty,
        packedRect = sprite.packedRect
      });
    }

    return runtimePayload;
  }

  static void WriteJsonPayload(string assetPath, string jsonText) {
    var normalizedAssetPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath)) return;

    var fullPath = Path.GetFullPath(normalizedAssetPath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? "");
    File.WriteAllText(fullPath, jsonText ?? "");
  }

  static void DeleteEditorMetadataAsset(string atlasAssetPath) {
    var editorMetadataAssetPath = BuildEditorMetadataAssetPath(atlasAssetPath);
    if (string.IsNullOrWhiteSpace(editorMetadataAssetPath)) {
      return;
    }

    if (AssetDatabase.LoadMainAssetAtPath(editorMetadataAssetPath) == null) {
      return;
    }

    AssetDatabase.DeleteAsset(editorMetadataAssetPath);
  }

  static string NormalizePath(string assetPath) {
    var normalized = TrimmedAtlasExporterWindow.NormalizeAssetPath(assetPath);
    return string.IsNullOrWhiteSpace(normalized) ? "" : normalized.Replace("\\", "/");
  }

  static bool TryConvertFullPathToAssetPath(string fullPath, out string assetPath) {
    assetPath = "";
    if (string.IsNullOrWhiteSpace(fullPath)) return false;

    var normalizedInput = fullPath.Replace("\\", "/").Trim();
    if (normalizedInput.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) {
      assetPath = normalizedInput;
      return true;
    }

    var projectRoot = Directory.GetCurrentDirectory().Replace("\\", "/").TrimEnd('/');
    if (normalizedInput.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase)) {
      assetPath = normalizedInput.Substring(projectRoot.Length + 1);
      return assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
    }

    return false;
  }

  static string BuildRuntimeMetadataAssetPath(string atlasAssetPath) {
    return TrimmedAtlasExporterWindow.BuildRuntimeMetadataAssetPath(atlasAssetPath);
  }

  static string BuildEditorMetadataAssetPath(string atlasAssetPath) {
    return TrimmedAtlasExporterWindow.BuildEditorMetadataAssetPath(atlasAssetPath);
  }

  static string ResolveExistingTrimmedMetadataReadPath(string runtimeMetadataAssetPath) {
    return ResolveMetadataReadFullPath(runtimeMetadataAssetPath);
  }

  static string ResolveMetadataReadFullPath(string runtimeMetadataAssetPath) {
    var normalizedRuntimeMetadataAssetPath = TrimmedAtlasExporterWindow.ResolveRuntimeMetadataAssetPath(runtimeMetadataAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedRuntimeMetadataAssetPath)) return "";

    var editorMetadataAssetPath = TrimmedAtlasExporterWindow.BuildEditorMetadataAssetPathFromRuntimeMetadata(normalizedRuntimeMetadataAssetPath);
    if (TryGetExistingMetadataFullPath(editorMetadataAssetPath, out var editorMetadataFullPath)) {
      return editorMetadataFullPath;
    }

    return TryGetExistingMetadataFullPath(normalizedRuntimeMetadataAssetPath, out var runtimeMetadataFullPath)
      ? runtimeMetadataFullPath
      : "";
  }

  static bool TryGetExistingMetadataFullPath(string metadataAssetPath, out string metadataFullPath) {
    metadataFullPath = "";
    var normalizedMetadataAssetPath = NormalizePath(metadataAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedMetadataAssetPath)) return false;

    var candidateFullPath = Path.GetFullPath(normalizedMetadataAssetPath);
    if (!File.Exists(candidateFullPath)) return false;

    metadataFullPath = candidateFullPath;
    return true;
  }

  static bool TryReadMetadataJson(string runtimeMetadataAssetPath, out string jsonText, out string error) {
    jsonText = "";
    error = "";

    var metadataFullPath = ResolveMetadataReadFullPath(runtimeMetadataAssetPath);
    if (string.IsNullOrWhiteSpace(metadataFullPath)) {
      error = "Metadata file not found.";
      return false;
    }

    try {
      jsonText = File.ReadAllText(metadataFullPath);
      return true;
    }
    catch (Exception ex) {
      error = ex.Message;
      return false;
    }
  }

  static bool TryLoadGroupedMetadataPayload(string runtimeMetadataAssetPath, out GroupedAtlasMetadataPayload payload, out string error) {
    payload = null;
    error = "";
    if (GeneratedAtlasImportMetadataStore.TryReadForRuntimeMetadata(runtimeMetadataAssetPath, out var importerJsonText)) {
      try {
        payload = JsonUtility.FromJson<GroupedAtlasMetadataPayload>(importerJsonText);
      }
      catch (Exception ex) {
        error = ex.Message;
        return false;
      }

      if (payload != null) {
        if (payload.sprites == null) {
          payload.sprites = new List<GroupedAtlasSpriteMetadata>();
        }

        if (payload.sourceCategories == null) {
          payload.sourceCategories = new List<string>();
        }

        return true;
      }
    }

    if (!TryReadMetadataJson(runtimeMetadataAssetPath, out var jsonText, out error)) return false;

    try {
      payload = JsonUtility.FromJson<GroupedAtlasMetadataPayload>(jsonText);
    }
    catch (Exception ex) {
      error = ex.Message;
      return false;
    }

    if (payload == null) {
      error = "Grouped metadata payload was empty.";
      return false;
    }

    if (payload.sprites == null) {
      payload.sprites = new List<GroupedAtlasSpriteMetadata>();
    }

    if (payload.sourceCategories == null) {
      payload.sourceCategories = new List<string>();
    }
    return true;
  }

  string BuildCandidateOutputFolderPath(string outputFolderPath, GroupCandidate candidate, string sanitizedOutputName) {
    if (candidate == null || candidate.sourceAtlases == null || candidate.sourceAtlases.Count <= 0) {
      return "";
    }

    var normalizedOutputFolderPath = NormalizePath(outputFolderPath).TrimEnd('/');
    if (string.IsNullOrWhiteSpace(normalizedOutputFolderPath)) {
      return "";
    }

    return normalizedOutputFolderPath;
  }

  void AssignUniquePageOutputPaths(string outputFolderPath, GroupCandidate candidate, List<AtlasPage> pages, bool includeNormalAtlases) {
    if (string.IsNullOrWhiteSpace(outputFolderPath) || candidate == null || pages == null) {
      return;
    }

    var reservedAssetPaths = CollectReservedGroupedOutputAssetPaths(outputFolderPath);
    for (var pageListIndex = 0; pageListIndex < pages.Count; pageListIndex++) {
      var page = pages[pageListIndex];
      if (page == null) {
        continue;
      }

      var assignedPageIndex = FindNextAvailableGroupedPageIndex(
        outputFolderPath,
        candidate,
        page.pageIndex,
        includeNormalAtlases,
        reservedAssetPaths);
      ApplyAssignedGroupedPageIndex(page, assignedPageIndex);

      page.colorAtlasPath = BuildPageAtlasAssetPath(outputFolderPath, candidate, assignedPageIndex, false);
      ReserveGroupedOutputAssetPath(reservedAssetPaths, page.colorAtlasPath);
      if (includeNormalAtlases) {
        page.normalAtlasPath = BuildPageAtlasAssetPath(outputFolderPath, candidate, assignedPageIndex, true);
        ReserveGroupedOutputAssetPath(reservedAssetPaths, page.normalAtlasPath);
      }
      else {
        page.normalAtlasPath = "";
      }
    }
  }

  string BuildPageAtlasAssetPath(string outputFolderPath, GroupCandidate candidate, int pageIndex, bool isNormalAtlas) {
    var fileName = BuildPageAtlasFileBase(candidate, pageIndex) + (isNormalAtlas ? "_N" : "") + ".png";
    return NormalizePath(outputFolderPath.TrimEnd('/') + "/" + fileName);
  }

  static string BuildPageAtlasFileBase(GroupCandidate candidate, int pageIndex) {
    var pageNumber = (pageIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    if (!HasExplicitOutputName(candidate)) {
      return pageNumber;
    }

    return BuildOutputFilePrefix(candidate) + "_p" + pageNumber;
  }

  static bool HasExplicitOutputName(GroupCandidate candidate) {
    return !string.IsNullOrWhiteSpace(candidate?.outputName);
  }

  static bool IsExistingGroupedPageMetadataPath(string metadataPath, GroupCandidate candidate) {
    var fileName = Path.GetFileNameWithoutExtension(metadataPath ?? "");
    if (string.IsNullOrWhiteSpace(fileName)) {
      return false;
    }

    if (!HasExplicitOutputName(candidate)) {
      return IsNumberedPageFileBase(fileName);
    }

    var filePrefix = BuildOutputFilePrefix(candidate) + "_p";
    return fileName.StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase);
  }

  static bool IsNumberedPageFileBase(string fileName) {
    return int.TryParse(
      fileName,
      System.Globalization.NumberStyles.Integer,
      System.Globalization.CultureInfo.InvariantCulture,
      out var pageNumber) &&
      pageNumber > 0;
  }

  static string BuildOutputFilePrefix(GroupCandidate candidate) {
    if (candidate == null) return "Grouped";
    if (!string.IsNullOrWhiteSpace(candidate.outputName)) {
      return candidate.outputName;
    }
    if (IsSkinCandidate(candidate)) {
      return SkinGroupKey + "_" + (candidate.partCode ?? "part");
    }

    return (candidate.form ?? "Form") + "_" + (candidate.variant ?? "Variant") + "_" + (candidate.partCode ?? "part");
  }

  HashSet<string> CollectReservedGroupedOutputAssetPaths(string outputFolderPath) {
    var reservedAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (string.IsNullOrWhiteSpace(outputFolderPath)) {
      return reservedAssetPaths;
    }

    var fullOutputFolderPath = Path.GetFullPath(outputFolderPath);
    if (!Directory.Exists(fullOutputFolderPath)) {
      return reservedAssetPaths;
    }

    var existingFilePaths = Directory.GetFiles(fullOutputFolderPath, "*", SearchOption.TopDirectoryOnly);
    for (var fileIndex = 0; fileIndex < existingFilePaths.Length; fileIndex++) {
      if (!TryConvertFullPathToAssetPath(existingFilePaths[fileIndex], out var assetPath)) {
        continue;
      }

      reservedAssetPaths.Add(NormalizePath(assetPath));
    }

    return reservedAssetPaths;
  }

  int FindNextAvailableGroupedPageIndex(
    string outputFolderPath,
    GroupCandidate candidate,
    int startingPageIndex,
    bool includeNormalAtlases,
    HashSet<string> reservedAssetPaths) {
    var pageIndex = Math.Max(0, startingPageIndex);
    while (true) {
      var colorAtlasPath = BuildPageAtlasAssetPath(outputFolderPath, candidate, pageIndex, false);
      if (HasReservedGroupedOutputPath(reservedAssetPaths, colorAtlasPath)) {
        pageIndex++;
        continue;
      }

      if (includeNormalAtlases) {
        var normalAtlasPath = BuildPageAtlasAssetPath(outputFolderPath, candidate, pageIndex, true);
        if (HasReservedGroupedOutputPath(reservedAssetPaths, normalAtlasPath)) {
          pageIndex++;
          continue;
        }
      }

      return pageIndex;
    }
  }

  static void ApplyAssignedGroupedPageIndex(AtlasPage page, int assignedPageIndex) {
    if (page == null) {
      return;
    }

    page.pageIndex = assignedPageIndex;
    if (page.items == null) {
      return;
    }

    for (var itemIndex = 0; itemIndex < page.items.Count; itemIndex++) {
      var item = page.items[itemIndex];
      if (item == null) {
        continue;
      }

      item.pageIndex = assignedPageIndex;
    }
  }

  static bool HasReservedGroupedOutputPath(HashSet<string> reservedAssetPaths, string atlasAssetPath) {
    if (reservedAssetPaths == null || string.IsNullOrWhiteSpace(atlasAssetPath)) {
      return false;
    }

    var normalizedAtlasAssetPath = NormalizePath(atlasAssetPath);
    if (reservedAssetPaths.Contains(normalizedAtlasAssetPath) ||
        reservedAssetPaths.Contains(normalizedAtlasAssetPath + ".meta")) {
      return true;
    }

    var runtimeMetadataAssetPath = BuildRuntimeMetadataAssetPath(normalizedAtlasAssetPath);
    if (!string.IsNullOrWhiteSpace(runtimeMetadataAssetPath) &&
        (reservedAssetPaths.Contains(runtimeMetadataAssetPath) ||
         reservedAssetPaths.Contains(runtimeMetadataAssetPath + ".meta"))) {
      return true;
    }

    var editorMetadataAssetPath = BuildEditorMetadataAssetPath(normalizedAtlasAssetPath);
    return !string.IsNullOrWhiteSpace(editorMetadataAssetPath) &&
           (reservedAssetPaths.Contains(editorMetadataAssetPath) ||
            reservedAssetPaths.Contains(editorMetadataAssetPath + ".meta"));
  }

  static void ReserveGroupedOutputAssetPath(HashSet<string> reservedAssetPaths, string atlasAssetPath) {
    if (reservedAssetPaths == null || string.IsNullOrWhiteSpace(atlasAssetPath)) {
      return;
    }

    var normalizedAtlasAssetPath = NormalizePath(atlasAssetPath);
    reservedAssetPaths.Add(normalizedAtlasAssetPath);
    reservedAssetPaths.Add(normalizedAtlasAssetPath + ".meta");

    var runtimeMetadataAssetPath = BuildRuntimeMetadataAssetPath(normalizedAtlasAssetPath);
    if (!string.IsNullOrWhiteSpace(runtimeMetadataAssetPath)) {
      reservedAssetPaths.Add(runtimeMetadataAssetPath);
      reservedAssetPaths.Add(runtimeMetadataAssetPath + ".meta");
    }

    var editorMetadataAssetPath = BuildEditorMetadataAssetPath(normalizedAtlasAssetPath);
    if (string.IsNullOrWhiteSpace(editorMetadataAssetPath)) {
      return;
    }

    reservedAssetPaths.Add(editorMetadataAssetPath);
    reservedAssetPaths.Add(editorMetadataAssetPath + ".meta");
  }

  static string ResolveSpriteNameCategory(GroupCandidate candidate, SourceAtlasRecord record) {
    if (candidate != null && candidate.usesFolderStructureSpriteNames) {
      var folderCategory = BuildAtlasFolderSpriteNameCategory(record?.atlasPath);
      if (!string.IsNullOrWhiteSpace(folderCategory)) {
        return folderCategory;
      }
    }

    if (!string.IsNullOrWhiteSpace(candidate?.outputName)) {
      return candidate.outputName.Trim();
    }

    return record?.category;
  }

  static string BuildAtlasFolderSpriteNameCategory(string atlasPath) {
    var normalizedPath = NormalizePath(atlasPath);
    if (string.IsNullOrWhiteSpace(normalizedPath)) {
      return "";
    }

    var folderPath = NormalizePath(Path.GetDirectoryName(normalizedPath));
    if (string.IsNullOrWhiteSpace(folderPath)) {
      return "";
    }

    var segments = folderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
    if (segments.Length <= 0) {
      return "";
    }

    var startIndex = Math.Max(0, segments.Length - ManualSpriteNameFolderDepth);
    var tokens = new List<string>();
    for (var i = startIndex; i < segments.Length; i++) {
      var token = SanitizeSpriteNameToken(segments[i]);
      if (!string.IsNullOrWhiteSpace(token)) {
        tokens.Add(token);
      }
    }

    return tokens.Count > 0 ? string.Join("_", tokens) : "";
  }

  static string BuildGroupedSpriteName(string partCode, string spriteNameCategory, string sourceSpriteName) {
    var normalizedCategory = string.IsNullOrWhiteSpace(spriteNameCategory) ? "Anim" : spriteNameCategory.Trim();
    var normalizedSpriteName = string.IsNullOrWhiteSpace(sourceSpriteName) ? "sprite" : sourceSpriteName.Trim();
    if (normalizedSpriteName.StartsWith(normalizedCategory + "_", StringComparison.OrdinalIgnoreCase)) {
      return (partCode ?? "part") + "__" + normalizedSpriteName;
    }

    return (partCode ?? "part") + "__" + normalizedCategory + "_" + normalizedSpriteName;
  }

  static void AddMetadataAssetPaths(ICollection<string> assetPaths, string atlasAssetPath) {
    if (assetPaths == null) return;
    var runtimeMetadataAssetPath = BuildRuntimeMetadataAssetPath(atlasAssetPath);
    if (!string.IsNullOrWhiteSpace(runtimeMetadataAssetPath)) {
      assetPaths.Add(runtimeMetadataAssetPath);
    }
  }

  static Dictionary<string, ExistingTrimmedAtlasSpriteMetadata> LoadTrimmedSourceMetadataByName(string atlasPath) {
    var dictionary = new Dictionary<string, ExistingTrimmedAtlasSpriteMetadata>(StringComparer.Ordinal);
    if (string.IsNullOrWhiteSpace(atlasPath)) return dictionary;

    var runtimeMetadataAssetPath = BuildRuntimeMetadataAssetPath(atlasPath);
    if (GeneratedAtlasImportMetadataStore.TryRead(atlasPath, out var importerJsonText)) {
      TryPopulateTrimmedMetadataDictionary(importerJsonText, atlasPath, dictionary);
      if (dictionary.Count > 0) {
        return dictionary;
      }
    }

    var metadataFullPath = ResolveMetadataReadFullPath(runtimeMetadataAssetPath);
    if (string.IsNullOrWhiteSpace(metadataFullPath) || !File.Exists(metadataFullPath)) {
      return dictionary;
    }

    try {
      TryPopulateTrimmedMetadataDictionary(File.ReadAllText(metadataFullPath), metadataFullPath, dictionary);
    }
    catch (Exception ex) {
      AtlasAuthoringLog.VerboseWarning("[GearGroupAtlas] Failed to load source trimmed metadata from '" + metadataFullPath + "': " + ex.Message);
    }

    return dictionary;
  }

  static void TryPopulateTrimmedMetadataDictionary(
    string jsonText,
    string sourceLabel,
    Dictionary<string, ExistingTrimmedAtlasSpriteMetadata> dictionary) {
    if (dictionary == null || string.IsNullOrWhiteSpace(jsonText)) {
      return;
    }

    try {
      var payload = JsonUtility.FromJson<ExistingTrimmedAtlasMetadataPayload>(jsonText);
      if (payload == null || payload.sprites == null) {
        return;
      }

      for (var i = 0; i < payload.sprites.Count; i++) {
        var sprite = payload.sprites[i];
        if (sprite != null && !string.IsNullOrWhiteSpace(sprite.name)) {
          dictionary[sprite.name] = sprite;
        }
      }
    }
    catch (Exception ex) {
      AtlasAuthoringLog.VerboseWarning("[GearGroupAtlas] Failed to parse source trimmed metadata from '" + sourceLabel + "': " + ex.Message);
    }
  }

  static int CompareMetadataItems(PackedSpriteBuildItem left, PackedSpriteBuildItem right) {
    if (ReferenceEquals(left, right)) return 0;
    if (left == null) return -1;
    if (right == null) return 1;

    var categoryComparison = SpriteSliceAddressUtility.NaturalStringComparer.Compare(left.sourceCategory, right.sourceCategory);
    if (categoryComparison != 0) return categoryComparison;

    var sourceSpriteComparison = SpriteSliceAddressUtility.NaturalStringComparer.Compare(left.sourceSpriteName, right.sourceSpriteName);
    if (sourceSpriteComparison != 0) return sourceSpriteComparison;

    return SpriteSliceAddressUtility.NaturalStringComparer.Compare(left.outputSpriteName, right.outputSpriteName);
  }
}
#endif
