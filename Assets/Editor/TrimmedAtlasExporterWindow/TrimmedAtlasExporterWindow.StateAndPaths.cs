#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed partial class TrimmedAtlasExporterWindow {
  bool TryGetSourcePath(out string sourcePath, bool showDialog) {
    sourcePath = "";
    if (sourceTexture == null) {
      if (showDialog) EditorUtility.DisplayDialog("Missing Source Atlas", "Select a source atlas first.", "OK");
      return false;
    }

    sourcePath = AssetDatabase.GetAssetPath(sourceTexture);
    if (!string.IsNullOrWhiteSpace(sourcePath)) return true;
    if (showDialog) EditorUtility.DisplayDialog("Invalid Source Atlas", "Could not resolve the source atlas asset path.", "OK");
    return false;
  }

  bool TryGetSourceFolderPath(out string sourceFolderPath, bool showDialog) {
    sourceFolderPath = "";
    if (sourceFolder == null) {
      if (showDialog) EditorUtility.DisplayDialog("Missing Source Folder", "Select a source folder first.", "OK");
      return false;
    }

    sourceFolderPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(sourceFolder));
    if (AssetDatabase.IsValidFolder(sourceFolderPath)) return true;
    if (showDialog) EditorUtility.DisplayDialog("Invalid Source Folder", "Could not resolve the source folder asset path.", "OK");
    return false;
  }

  bool HasFreshAnalysis(string sourcePath) {
    return analyzedAtlas != null &&
           analyzedBuildItems != null &&
           analyzedPreviewTexture != null &&
           string.Equals(analyzedSourcePath, sourcePath, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(analyzedSettingsSignature, BuildAnalysisSignature(sourcePath), StringComparison.Ordinal);
  }

  string BuildAnalysisSignature(string sourcePath) {
    return string.Join(
      "|",
      sourcePath ?? "",
      cellWidth,
      cellHeight,
      maxAtlasWidth,
      padding,
      alphaThreshold,
      treatNearWhiteAsEmpty ? 1 : 0,
      nearWhiteThreshold,
      ignoreDistantStrayIslands ? 1 : 0,
      strayIslandGapCutoffPx,
      strayIslandMaxPixels,
      preserveSpriteNames ? 1 : 0);
  }

  void CacheAnalysis(string sourcePath, TrimmedAtlasExport exportData, List<TrimmedSpriteBuildData> buildItems, Texture2D previewTexture) {
    InvalidateAnalysis();
    analyzedSourcePath = sourcePath;
    analyzedSettingsSignature = BuildAnalysisSignature(sourcePath);
    analyzedAtlas = exportData;
    analyzedBuildItems = buildItems;
    analyzedPreviewTexture = previewTexture;
  }

  void InvalidateAnalysis() {
    analyzedAtlas = null;
    analyzedBuildItems = null;
    analyzedSourcePath = "";
    analyzedSettingsSignature = "";
    selectedSliceIndex = -1;
    if (analyzedPreviewTexture != null) {
      DestroyImmediate(analyzedPreviewTexture);
      analyzedPreviewTexture = null;
    }
  }

  void SelectDefaultSlice() {
    selectedSliceIndex = 0;
    if (analyzedAtlas == null || analyzedAtlas.sprites == null) return;
    for (var i = 0; i < analyzedAtlas.sprites.Count; i++) {
      if (analyzedAtlas.sprites[i].empty) continue;
      selectedSliceIndex = i;
      return;
    }
  }

  void OnDisable() {
    InvalidateAnalysis();
  }

  void OnDestroy() {
    InvalidateAnalysis();
  }

  internal static bool TryLoadTextureFromDisk(string assetPath, out Texture2D texture, out string error) {
    texture = null;
    error = "";
    var fullPath = Path.GetFullPath(assetPath);
    if (!File.Exists(fullPath)) {
      error = "Atlas file does not exist on disk: " + fullPath;
      return false;
    }

    var bytes = File.ReadAllBytes(fullPath);
    texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
    if (!ImageConversion.LoadImage(texture, bytes, false)) {
      DestroyImmediate(texture);
      texture = null;
      error = "Failed to decode atlas file: " + assetPath;
      return false;
    }

    texture.filterMode = FilterMode.Point;
    texture.wrapMode = TextureWrapMode.Clamp;
    return true;
  }

  static List<Sprite> LoadSourceSprites(string sourcePath) {
    if (string.IsNullOrWhiteSpace(sourcePath)) return new List<Sprite>();

    var assets = AssetDatabase.LoadAllAssetsAtPath(sourcePath);
    var sprites = new List<Sprite>(assets.Length);
    for (var i = 0; i < assets.Length; i++) {
      if (assets[i] is Sprite sprite) {
        sprites.Add(sprite);
      }
    }

    return sprites;
  }

  Dictionary<int, string> BuildSpriteNameLookup(IEnumerable<Sprite> sprites, int columns, int rows, int gridCellWidth, int gridCellHeight) {
    var result = new Dictionary<int, string>();
    if (sprites == null || columns <= 0 || rows <= 0 || gridCellWidth <= 0 || gridCellHeight <= 0) return result;

    foreach (var sprite in sprites) {
      if (sprite == null) continue;
      var rect = sprite.rect;
      var column = Mathf.FloorToInt(rect.xMin / gridCellWidth);
      var row = Mathf.FloorToInt(rect.yMin / gridCellHeight);
      if (column < 0 || column >= columns || row < 0 || row >= rows) continue;
      result[(row * columns) + column] = sprite.name;
    }

    return result;
  }

  static bool DoesSpriteCoverEntireTexture(Sprite sprite, Texture2D texture) {
    if (sprite == null || texture == null) return false;
    var rect = sprite.rect;
    return Mathf.Abs(rect.xMin) <= 0.01f &&
           Mathf.Abs(rect.yMin) <= 0.01f &&
           Mathf.Abs(rect.width - texture.width) <= 0.01f &&
           Mathf.Abs(rect.height - texture.height) <= 0.01f;
  }

  static PixelRect RoundSpriteRectToPixelRect(Rect rect, int atlasWidth, int atlasHeight) {
    var xMin = Mathf.Clamp(Mathf.RoundToInt(rect.xMin), 0, Math.Max(0, atlasWidth - 1));
    var yMin = Mathf.Clamp(Mathf.RoundToInt(rect.yMin), 0, Math.Max(0, atlasHeight - 1));
    var xMax = Mathf.Clamp(Mathf.RoundToInt(rect.xMax), xMin + 1, Math.Max(xMin + 1, atlasWidth));
    var yMax = Mathf.Clamp(Mathf.RoundToInt(rect.yMax), yMin + 1, Math.Max(yMin + 1, atlasHeight));
    return new PixelRect(xMin, yMin, xMax - xMin, yMax - yMin);
  }

  static int CountDistinctCellOrigins(IEnumerable<float> values) {
    var distinctOrigins = new HashSet<int>();
    if (values == null) return 0;

    foreach (var value in values) {
      distinctOrigins.Add(Mathf.RoundToInt(value));
    }

    return distinctOrigins.Count;
  }

  internal static string NormalizeAssetPath(string assetPath) {
    return string.IsNullOrWhiteSpace(assetPath) ? "" : assetPath.Replace("\\", "/").Trim();
  }

  internal static string BuildRuntimeMetadataAssetPath(string atlasAssetPath) {
    var normalizedAtlasAssetPath = NormalizeAssetPath(atlasAssetPath);
    return string.IsNullOrWhiteSpace(normalizedAtlasAssetPath)
      ? ""
      : NormalizeAssetPath(Path.ChangeExtension(normalizedAtlasAssetPath, ".json"));
  }

  internal static string BuildEditorMetadataAssetPath(string atlasAssetPath) {
    var runtimeMetadataAssetPath = BuildRuntimeMetadataAssetPath(atlasAssetPath);
    return BuildEditorMetadataAssetPathFromRuntimeMetadata(runtimeMetadataAssetPath);
  }

  internal static string BuildEditorMetadataAssetPathFromRuntimeMetadata(string metadataAssetPath) {
    var normalizedMetadataAssetPath = NormalizeAssetPath(metadataAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedMetadataAssetPath)) return "";
    if (normalizedMetadataAssetPath.EndsWith(EditorMetadataSuffix, StringComparison.OrdinalIgnoreCase)) {
      return normalizedMetadataAssetPath;
    }
    if (!normalizedMetadataAssetPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return "";
    return normalizedMetadataAssetPath.Substring(0, normalizedMetadataAssetPath.Length - ".json".Length) + EditorMetadataSuffix;
  }

  internal static string ResolveRuntimeMetadataAssetPath(string metadataAssetPath) {
    var normalizedMetadataAssetPath = NormalizeAssetPath(metadataAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedMetadataAssetPath)) return "";
    if (normalizedMetadataAssetPath.EndsWith(EditorMetadataSuffix, StringComparison.OrdinalIgnoreCase)) {
      return normalizedMetadataAssetPath.Substring(0, normalizedMetadataAssetPath.Length - EditorMetadataSuffix.Length) + ".json";
    }
    return normalizedMetadataAssetPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
      ? normalizedMetadataAssetPath
      : "";
  }

  internal static bool IsEditorMetadataAssetPath(string assetPath) {
    return NormalizeAssetPath(assetPath).EndsWith(EditorMetadataSuffix, StringComparison.OrdinalIgnoreCase);
  }

  internal static bool IsRuntimeMetadataAssetPath(string assetPath) {
    var normalizedAssetPath = NormalizeAssetPath(assetPath);
    return normalizedAssetPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
           !normalizedAssetPath.EndsWith(EditorMetadataSuffix, StringComparison.OrdinalIgnoreCase);
  }

  static bool IsSupportedSourceTextureAssetPath(string assetPath) {
    var extension = Path.GetExtension(assetPath);
    for (var i = 0; i < SupportedSourceExtensions.Length; i++) {
      if (string.Equals(extension, SupportedSourceExtensions[i], StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
  }

  static bool IsGeneratedNormalAtlasAssetPath(string assetPath) {
    var fileName = Path.GetFileNameWithoutExtension(assetPath ?? "");
    return fileName.EndsWith("_N", StringComparison.OrdinalIgnoreCase);
  }

  static string ResolveSpriteName(Dictionary<int, string> namesByIndex, int index, string atlasName, int fallbackIndex) {
    if (namesByIndex != null && namesByIndex.TryGetValue(index, out var name) && !string.IsNullOrWhiteSpace(name)) {
      return name;
    }

    return atlasName + "_" + (fallbackIndex + 1);
  }

  string ResolveConfiguredOutputFolderPath() {
    if (outputFolder == null) return "";
    var folderPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(outputFolder));
    return AssetDatabase.IsValidFolder(folderPath) ? folderPath : "";
  }

  string ResolveOutputFolderPath(string sourcePath) {
    return ResolveOutputFolderPath(sourcePath, "");
  }

  string ResolveOutputFolderPath(string sourcePath, string sourceRootPath) {
    var configuredOutputFolderPath = ResolveConfiguredOutputFolderPath();
    if (!string.IsNullOrWhiteSpace(configuredOutputFolderPath)) {
      var relativeSourceFolder = BuildRelativeSourceFolderPath(sourcePath, sourceRootPath);
      if (!string.IsNullOrWhiteSpace(relativeSourceFolder)) {
        return ResolveEyeOutputFolderPath(
          sourcePath,
          configuredOutputFolderPath,
          NormalizeAssetPath(configuredOutputFolderPath + "/" + relativeSourceFolder));
      }

      return ResolveEyeOutputFolderPath(sourcePath, configuredOutputFolderPath, configuredOutputFolderPath);
    }

    var sourceDirectoryPath = NormalizeAssetPath(Path.GetDirectoryName(sourcePath));
    return sourceDirectoryPath;
  }

  static string ResolveEyeOutputFolderPath(string sourcePath, string configuredOutputRootPath, string resolvedOutputFolderPath) {
    var normalizedResolvedOutputFolderPath = NormalizeAssetPath(resolvedOutputFolderPath);
    if (!IsEyePartSourceFolder(sourcePath)) return normalizedResolvedOutputFolderPath;

    var outputRootPath = NormalizeAssetPath(configuredOutputRootPath);
    if (string.IsNullOrWhiteSpace(outputRootPath) &&
        !TryGetCharacterSpriteRootPath(sourcePath, out outputRootPath)) {
      return normalizedResolvedOutputFolderPath;
    }

    var normalizedSkinOutputFolderPath = NormalizeAssetPath(outputRootPath.TrimEnd('/') + "/Skin/e");
    if (string.Equals(normalizedResolvedOutputFolderPath, normalizedSkinOutputFolderPath, StringComparison.OrdinalIgnoreCase)) {
      return normalizedResolvedOutputFolderPath;
    }

    AtlasAuthoringLog.Verbose(
      "[TrimAtlasExport] Redirecting eye atlas output to the shared skin folder." +
      " source='" + NormalizeAssetPath(sourcePath) + "'" +
      " from='" + normalizedResolvedOutputFolderPath + "'" +
      " to='" + normalizedSkinOutputFolderPath + "'");
    return normalizedSkinOutputFolderPath;
  }

  static string BuildRelativeSourceFolderPath(string sourcePath, string sourceRootPath) {
    var normalizedRootPath = NormalizeAssetPath(sourceRootPath).TrimEnd('/');
    if (string.IsNullOrWhiteSpace(normalizedRootPath)) return "";

    var normalizedSourceFolderPath = NormalizeAssetPath(Path.GetDirectoryName(sourcePath)).TrimEnd('/');
    if (string.Equals(normalizedSourceFolderPath, normalizedRootPath, StringComparison.OrdinalIgnoreCase)) {
      return "";
    }

    var prefix = normalizedRootPath + "/";
    if (!normalizedSourceFolderPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
      return "";
    }

    return normalizedSourceFolderPath.Substring(prefix.Length).Trim('/');
  }

  static bool IsEyePartSourceFolder(string sourcePath) {
    var sourceFolderPath = NormalizeAssetPath(Path.GetDirectoryName(sourcePath));
    return string.Equals(ExtractTrailingPathSegment(sourceFolderPath), "e", StringComparison.OrdinalIgnoreCase);
  }

  static bool TryGetCharacterSpriteRootPath(string assetPath, out string characterRootPath) {
    characterRootPath = "";
    var normalizedAssetPath = NormalizeAssetPath(assetPath).Trim('/');
    if (string.IsNullOrWhiteSpace(normalizedAssetPath)) return false;

    var segments = normalizedAssetPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
    if (segments.Length < 4) return false;
    if (!string.Equals(segments[0], "Assets", StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(segments[1], "Sprites", StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(segments[2], "Characters", StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    characterRootPath = segments[0] + "/" + segments[1] + "/" + segments[2] + "/" + segments[3];
    return !string.IsNullOrWhiteSpace(characterRootPath);
  }
}
#endif
