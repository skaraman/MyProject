#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed partial class EsperanzaGearGroupAtlasWindow : EditorWindow {
  bool TryWriteMetadata(
    string atlasAssetPath,
    GroupCandidate candidate,
    string representativeSourceAtlasAssetPath,
    AtlasPage page,
    bool isNormalMetadata,
    out string metadataAssetPath,
    out string error) {
    metadataAssetPath = "";
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

    var orderedSourceCategories = sourceCategories
      .OrderBy(category => category, SpriteSliceAddressUtility.NaturalStringComparer)
      .ToList();
    var primarySourceCategory = orderedSourceCategories.FirstOrDefault() ?? candidate.sourceCategories.FirstOrDefault() ?? "";

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

    var orderedMetadataItems = page.items
      .Where(item => item != null)
      .OrderBy(item => item.sourceCategory, SpriteSliceAddressUtility.NaturalStringComparer)
      .ThenBy(item => item.sourceSpriteName, SpriteSliceAddressUtility.NaturalStringComparer)
      .ThenBy(item => item.outputSpriteName, SpriteSliceAddressUtility.NaturalStringComparer)
      .ToList();
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
      WriteJsonPayload(metadataAssetPath, JsonUtility.ToJson(BuildRuntimeGroupedMetadata(payload), true));
      WriteJsonPayload(BuildEditorMetadataAssetPath(atlasAssetPath), JsonUtility.ToJson(payload, true));
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

  string BuildCandidateOutputFolderPath(string sourceRootPath, GroupCandidate candidate, string sanitizedOutputSubfolder) {
    if (candidate == null || candidate.sourceAtlases == null || candidate.sourceAtlases.Count <= 0) {
      return "";
    }

    var normalizedSourceRootPath = NormalizePath(sourceRootPath).TrimEnd('/');
    if (string.IsNullOrWhiteSpace(normalizedSourceRootPath) || string.IsNullOrWhiteSpace(sanitizedOutputSubfolder)) {
      return "";
    }

    var outputFolderPath = normalizedSourceRootPath + "/" + sanitizedOutputSubfolder.Trim('/');
    if (IsSkinCandidate(candidate)) {
      return NormalizePath(outputFolderPath + "/" + SkinFormName + "/" + candidate.partCode);
    }

    return NormalizePath(outputFolderPath + "/" + candidate.form + "/" + candidate.variant + "/" + candidate.partCode);
  }

  string BuildPageAtlasAssetPath(string outputFolderPath, GroupCandidate candidate, int pageIndex, bool isNormalAtlas) {
    var fileName = BuildOutputFilePrefix(candidate) + "_p" + (pageIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + (isNormalAtlas ? "_N" : "") + ".png";
    return NormalizePath(outputFolderPath.TrimEnd('/') + "/" + fileName);
  }

  static string BuildOutputFilePrefix(GroupCandidate candidate) {
    if (candidate == null) return "Grouped";
    if (IsSkinCandidate(candidate)) {
      return SkinGroupKey + "_" + (candidate.partCode ?? "part");
    }

    return (candidate.form ?? "Form") + "_" + (candidate.variant ?? "Variant") + "_" + (candidate.partCode ?? "part");
  }

  static string BuildGroupedSpriteName(string partCode, string sourceCategory, string sourceSpriteName) {
    var normalizedCategory = string.IsNullOrWhiteSpace(sourceCategory) ? "Anim" : sourceCategory.Trim();
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

    var editorMetadataAssetPath = BuildEditorMetadataAssetPath(atlasAssetPath);
    if (!string.IsNullOrWhiteSpace(editorMetadataAssetPath)) {
      assetPaths.Add(editorMetadataAssetPath);
    }
  }

  static Dictionary<string, ExistingTrimmedAtlasSpriteMetadata> LoadTrimmedSourceMetadataByName(string atlasPath) {
    var dictionary = new Dictionary<string, ExistingTrimmedAtlasSpriteMetadata>(StringComparer.Ordinal);
    if (string.IsNullOrWhiteSpace(atlasPath)) return dictionary;

    var runtimeMetadataAssetPath = BuildRuntimeMetadataAssetPath(atlasPath);
    var metadataFullPath = ResolveMetadataReadFullPath(runtimeMetadataAssetPath);
    if (string.IsNullOrWhiteSpace(metadataFullPath) || !File.Exists(metadataFullPath)) {
      return dictionary;
    }

    try {
      var jsonText = File.ReadAllText(metadataFullPath);
      var payload = JsonUtility.FromJson<ExistingTrimmedAtlasMetadataPayload>(jsonText);
      if (payload != null && payload.sprites != null) {
        for (var i = 0; i < payload.sprites.Count; i++) {
          var sprite = payload.sprites[i];
          if (sprite != null && !string.IsNullOrWhiteSpace(sprite.name)) {
            dictionary[sprite.name] = sprite;
          }
        }
      }
    }
    catch (Exception ex) {
      Debug.LogWarning("[GearGroupAtlas] Failed to load source trimmed metadata from '" + metadataFullPath + "': " + ex.Message);
    }

    return dictionary;
  }
}
#endif
