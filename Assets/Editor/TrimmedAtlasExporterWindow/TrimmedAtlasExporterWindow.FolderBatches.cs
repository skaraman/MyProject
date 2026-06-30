#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed partial class TrimmedAtlasExporterWindow {
  List<string> CollectSourceAtlasPaths(string sourceFolderPath) {
    var atlasPathsByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var normalizedSourceFolderPath = NormalizeAssetPath(sourceFolderPath).TrimEnd('/');
    var ignoredFolderSkippedCount = 0;
    var textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { normalizedSourceFolderPath });
    for (var i = 0; i < textureGuids.Length; i++) {
      var assetPath = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(textureGuids[i]));
      if (string.IsNullOrWhiteSpace(assetPath)) continue;
      if (SpriteAtlasSourceFilter.HasIgnoredFolderInPath(assetPath)) {
        ignoredFolderSkippedCount++;
        continue;
      }
      if (!IsSupportedSourceTextureAssetPath(assetPath)) continue;
      if (IsGeneratedNormalAtlasAssetPath(assetPath)) continue;
      if (ShouldSkipGeneratedOutput(assetPath)) continue;

      var parentFolderPath = NormalizeAssetPath(Path.GetDirectoryName(assetPath));
      if (!includeSubfolders && !string.Equals(parentFolderPath, normalizedSourceFolderPath, StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      var key = parentFolderPath + "|" + Path.GetFileNameWithoutExtension(assetPath);
      if (atlasPathsByKey.TryGetValue(key, out var existingPath)) {
        atlasPathsByKey[key] = PreferSourceAtlasPath(existingPath, assetPath);
        continue;
      }

      atlasPathsByKey[key] = assetPath;
    }

    var atlasPaths = new List<string>(atlasPathsByKey.Count);
    foreach (var atlasPath in atlasPathsByKey.Values) {
      atlasPaths.Add(atlasPath);
    }
    atlasPaths.Sort(SpriteSliceAddressUtility.CompareNaturally);
    Debug.Log(
      "[TrimAtlasExport] Source atlas scan complete." +
      " source='" + normalizedSourceFolderPath + "'" +
      " matched=" + atlasPaths.Count +
      " ignored_folder_skipped=" + ignoredFolderSkippedCount +
      " ignored_folders='" + SpriteAtlasSourceFilter.IgnoredFolderSummary + "'");
    return atlasPaths;
  }

  List<SourceAtlasExportBatch> CollectSourceAtlasExportBatches(string sourceFolderPath) {
    var sourceAtlasPaths = CollectSourceAtlasPaths(sourceFolderPath);
    var sourcePathsByFolder = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < sourceAtlasPaths.Count; i++) {
      var sourcePath = sourceAtlasPaths[i];
      var parentFolderPath = NormalizeAssetPath(Path.GetDirectoryName(sourcePath));
      if (!sourcePathsByFolder.TryGetValue(parentFolderPath, out var folderSourcePaths)) {
        folderSourcePaths = new List<string>();
        sourcePathsByFolder[parentFolderPath] = folderSourcePaths;
      }

      folderSourcePaths.Add(sourcePath);
    }

    var exportBatches = new List<SourceAtlasExportBatch>(sourceAtlasPaths.Count);
    var orderedFolderPaths = new List<string>(sourcePathsByFolder.Keys);
    orderedFolderPaths.Sort(SpriteSliceAddressUtility.NaturalStringComparer);
    for (var folderIndex = 0; folderIndex < orderedFolderPaths.Count; folderIndex++) {
      var folderPath = orderedFolderPaths[folderIndex];
      var folderSourcePaths = sourcePathsByFolder[folderPath];
      var numericSourcePaths = new List<string>();
      var nonNumericSourcePaths = new List<string>();

      for (var sourceIndex = 0; sourceIndex < folderSourcePaths.Count; sourceIndex++) {
        var sourcePath = folderSourcePaths[sourceIndex];
        if (TryParseNumericSourceName(sourcePath, out _)) {
          numericSourcePaths.Add(sourcePath);
          continue;
        }

        nonNumericSourcePaths.Add(sourcePath);
      }

      if (numericSourcePaths.Count > 1) {
        numericSourcePaths.Sort(CompareNumericSourcePaths);
        var groupedOutputName = BuildNumericBatchOutputName(folderPath, numericSourcePaths);
        nonNumericSourcePaths.RemoveAll(sourcePath => ShouldSkipGroupedNumericOutput(sourcePath, groupedOutputName));
        exportBatches.Add(new SourceAtlasExportBatch {
          sourceFolderPath = folderPath,
          primarySourcePath = numericSourcePaths[0],
          outputName = groupedOutputName,
          groupedNumericSiblings = true,
          sourcePaths = numericSourcePaths
        });
      }
      else if (numericSourcePaths.Count == 1) {
        exportBatches.Add(BuildSingleSourceExportBatch(numericSourcePaths[0], folderPath));
      }

      nonNumericSourcePaths.Sort(SpriteSliceAddressUtility.CompareNaturally);
      for (var sourceIndex = 0; sourceIndex < nonNumericSourcePaths.Count; sourceIndex++) {
        exportBatches.Add(BuildSingleSourceExportBatch(nonNumericSourcePaths[sourceIndex], folderPath));
      }
    }

    exportBatches.Sort(CompareExportBatchPaths);
    return exportBatches;
  }

  static void AddFailureLog(List<string> failureLogs, string sourcePath, string error) {
    if (failureLogs == null || failureLogs.Count >= 20) return;
    failureLogs.Add((sourcePath ?? "<unknown>") + " :: " + (string.IsNullOrWhiteSpace(error) ? "Unknown export failure." : error));
  }

  bool ShouldSkipGeneratedOutput(string assetPath) {
    if (string.IsNullOrWhiteSpace(assetPath)) return false;
    if (IsGeneratedTrimmedOutputAssetPath(assetPath)) return true;

    var fileName = Path.GetFileNameWithoutExtension(assetPath);
    var parentFolderPath = NormalizeAssetPath(Path.GetDirectoryName(assetPath));
    if (TryExtractGeneratedOutputBaseName(fileName, out var generatedBaseName)) {
      for (var i = 0; i < SupportedSourceExtensions.Length; i++) {
        var candidatePath = NormalizeAssetPath(parentFolderPath + "/" + generatedBaseName + SupportedSourceExtensions[i]);
        if (string.Equals(candidatePath, assetPath, StringComparison.OrdinalIgnoreCase)) continue;
        if (!File.Exists(Path.GetFullPath(candidatePath))) continue;
        return true;
      }
    }

    var prefix = GetSanitizedOutputPrefix();
    if (!writePrefixedCopy || string.IsNullOrWhiteSpace(prefix)) return false;

    var prefixWithSeparator = prefix + "_";
    if (!fileName.StartsWith(prefixWithSeparator, StringComparison.OrdinalIgnoreCase)) return false;

    var originalName = fileName.Substring(prefixWithSeparator.Length);
    if (string.IsNullOrWhiteSpace(originalName)) return false;

    for (var i = 0; i < SupportedSourceExtensions.Length; i++) {
      var candidatePath = NormalizeAssetPath(parentFolderPath + "/" + originalName + SupportedSourceExtensions[i]);
      if (string.Equals(candidatePath, assetPath, StringComparison.OrdinalIgnoreCase)) continue;
      if (!File.Exists(Path.GetFullPath(candidatePath))) continue;
      return true;
    }

    return false;
  }

  static bool IsGeneratedTrimmedOutputAssetPath(string assetPath) {
    var normalizedAssetPath = NormalizeAssetPath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath)) return false;

    var metadataAssetPath = BuildEditorMetadataAssetPath(normalizedAssetPath);
    var metadataFullPath = Path.GetFullPath(metadataAssetPath);
    if (!File.Exists(metadataFullPath)) {
      metadataAssetPath = BuildRuntimeMetadataAssetPath(normalizedAssetPath);
      metadataFullPath = Path.GetFullPath(metadataAssetPath);
    }
    if (!File.Exists(metadataFullPath)) return false;

    ExistingTrimmedAtlasMetadata metadata;
    try {
      metadata = JsonUtility.FromJson<ExistingTrimmedAtlasMetadata>(File.ReadAllText(metadataFullPath));
    }
    catch {
      return false;
    }

    if (metadata == null || string.IsNullOrWhiteSpace(metadata.exportedAtlasAssetPath)) return false;
    if (!string.Equals(NormalizeAssetPath(metadata.exportedAtlasAssetPath), normalizedAssetPath, StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    return string.Equals(metadata.metadataKind, "trimmed", StringComparison.OrdinalIgnoreCase) ||
           !string.IsNullOrWhiteSpace(metadata.coordinateOrigin);
  }

  static string PreferSourceAtlasPath(string existingPath, string candidatePath) {
    var existingPriority = GetSourceExtensionPriority(existingPath);
    var candidatePriority = GetSourceExtensionPriority(candidatePath);
    if (candidatePriority < existingPriority) return candidatePath;
    if (candidatePriority > existingPriority) return existingPath;
    return string.Compare(candidatePath, existingPath, StringComparison.OrdinalIgnoreCase) < 0 ? candidatePath : existingPath;
  }

  static int GetSourceExtensionPriority(string assetPath) {
    var extension = Path.GetExtension(assetPath);
    for (var i = 0; i < SupportedSourceExtensions.Length; i++) {
      if (string.Equals(extension, SupportedSourceExtensions[i], StringComparison.OrdinalIgnoreCase)) {
        return i;
      }
    }

    return int.MaxValue;
  }

  static SourceAtlasExportBatch BuildSingleSourceExportBatch(string sourcePath, string sourceFolderPath) {
    return new SourceAtlasExportBatch {
      sourceFolderPath = NormalizeAssetPath(string.IsNullOrWhiteSpace(sourceFolderPath) ? Path.GetDirectoryName(sourcePath) : sourceFolderPath),
      primarySourcePath = sourcePath,
      outputName = Path.GetFileNameWithoutExtension(sourcePath),
      groupedNumericSiblings = false,
      sourcePaths = new List<string> { sourcePath }
    };
  }

  static int CompareExportBatchPaths(SourceAtlasExportBatch left, SourceAtlasExportBatch right) {
    var folderComparison = SpriteSliceAddressUtility.CompareNaturally(left?.sourceFolderPath, right?.sourceFolderPath);
    if (folderComparison != 0) {
      return folderComparison;
    }

    return SpriteSliceAddressUtility.CompareNaturally(left?.primarySourcePath, right?.primarySourcePath);
  }

  static bool TryParseNumericSourceName(string sourcePath, out int numericValue) {
    numericValue = 0;
    if (string.IsNullOrWhiteSpace(sourcePath)) return false;
    return int.TryParse(Path.GetFileNameWithoutExtension(sourcePath), out numericValue);
  }

  bool TryExtractGeneratedOutputBaseName(string fileName, out string baseName) {
    baseName = "";
    if (string.IsNullOrWhiteSpace(fileName)) return false;

    var workingName = fileName.Trim();
    var prefix = GetSanitizedOutputPrefix();
    var prefixWithSeparator = prefix + "_";
    if (writePrefixedCopy &&
        !string.IsNullOrWhiteSpace(prefix) &&
        workingName.StartsWith(prefixWithSeparator, StringComparison.OrdinalIgnoreCase)) {
      workingName = workingName.Substring(prefixWithSeparator.Length);
    }

    var pageMarkerIndex = workingName.LastIndexOf("_p", StringComparison.OrdinalIgnoreCase);
    if (pageMarkerIndex <= 0 || pageMarkerIndex >= workingName.Length - 2) return false;
    for (var i = pageMarkerIndex + 2; i < workingName.Length; i++) {
      if (!char.IsDigit(workingName[i])) return false;
    }

    baseName = workingName.Substring(0, pageMarkerIndex);
    return !string.IsNullOrWhiteSpace(baseName);
  }

  static int CompareNumericSourcePaths(string leftPath, string rightPath) {
    var leftHasNumber = TryParseNumericSourceName(leftPath, out var leftValue);
    var rightHasNumber = TryParseNumericSourceName(rightPath, out var rightValue);
    if (leftHasNumber && rightHasNumber) {
      var numericComparison = leftValue.CompareTo(rightValue);
      if (numericComparison != 0) return numericComparison;
    }

    return SpriteSliceAddressUtility.CompareNaturally(leftPath, rightPath);
  }

  static string BuildNumericBatchOutputName(string folderPath, List<string> sourcePaths) {
    var outputName = ExtractTrailingPathSegment(folderPath);
    if (string.IsNullOrWhiteSpace(outputName) && sourcePaths != null && sourcePaths.Count > 0) {
      outputName = Path.GetFileNameWithoutExtension(sourcePaths[0]);
    }

    if (sourcePaths != null) {
      for (var i = 0; i < sourcePaths.Count; i++) {
        if (!string.Equals(Path.GetFileNameWithoutExtension(sourcePaths[i]), outputName, StringComparison.OrdinalIgnoreCase)) continue;
        return outputName + "_atlas";
      }
    }

    return outputName;
  }

  void DeleteLegacyGeneratedOutputs(SourceAtlasExportBatch batch, string outputName, string exportedAtlasPath) {
    if (batch == null || batch.sourcePaths == null || batch.sourcePaths.Count <= 0 || string.IsNullOrWhiteSpace(outputName)) return;

    var normalizedExportedAtlasPath = NormalizeAssetPath(exportedAtlasPath);
    var normalizedExportFolderPath = NormalizeAssetPath(Path.GetDirectoryName(normalizedExportedAtlasPath));
    var inspectedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var sourceIndex = 0; sourceIndex < batch.sourcePaths.Count; sourceIndex++) {
      var sourceFolderPath = NormalizeAssetPath(Path.GetDirectoryName(batch.sourcePaths[sourceIndex]));
      if (string.IsNullOrWhiteSpace(sourceFolderPath) || !inspectedFolders.Add(sourceFolderPath)) continue;
      if (string.Equals(sourceFolderPath, normalizedExportFolderPath, StringComparison.OrdinalIgnoreCase)) continue;

      var fullSourceFolderPath = Path.GetFullPath(sourceFolderPath);
      if (!Directory.Exists(fullSourceFolderPath)) continue;

      var candidateFiles = Directory.GetFiles(fullSourceFolderPath, "*.png", SearchOption.TopDirectoryOnly);
      for (var candidateIndex = 0; candidateIndex < candidateFiles.Length; candidateIndex++) {
        if (!TryConvertFullPathToAssetPath(candidateFiles[candidateIndex], out var candidateAssetPath)) continue;
        if (string.Equals(candidateAssetPath, normalizedExportedAtlasPath, StringComparison.OrdinalIgnoreCase)) continue;
        if (!MatchesGeneratedOutputName(candidateAssetPath, outputName)) continue;
        if (!IsGeneratedTrimmedOutputAssetPath(candidateAssetPath)) continue;

        DeleteGeneratedOutputAssetPair(candidateAssetPath);
      }
    }
  }

  bool MatchesGeneratedOutputName(string assetPath, string outputName) {
    var fileName = Path.GetFileNameWithoutExtension(assetPath);
    if (string.Equals(fileName, outputName, StringComparison.OrdinalIgnoreCase)) return true;
    return TryExtractGeneratedOutputBaseName(fileName, out var generatedBaseName) &&
           string.Equals(generatedBaseName, outputName, StringComparison.OrdinalIgnoreCase);
  }

  static void DeleteGeneratedOutputAssetPair(string atlasAssetPath) {
    var normalizedAtlasAssetPath = NormalizeAssetPath(atlasAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedAtlasAssetPath)) return;

    DeleteMetadataAsset(BuildRuntimeMetadataAssetPath(normalizedAtlasAssetPath));
    DeleteMetadataAsset(BuildEditorMetadataAssetPath(normalizedAtlasAssetPath));

    if (AssetDatabase.LoadMainAssetAtPath(normalizedAtlasAssetPath) != null) {
      AssetDatabase.DeleteAsset(normalizedAtlasAssetPath);
    }
  }

  static void DeleteMetadataAsset(string metadataAssetPath) {
    if (string.IsNullOrWhiteSpace(metadataAssetPath)) return;
    if (AssetDatabase.LoadMainAssetAtPath(metadataAssetPath) == null) return;
    AssetDatabase.DeleteAsset(metadataAssetPath);
  }

  static bool TryConvertFullPathToAssetPath(string fullPath, out string assetPath) {
    assetPath = "";
    if (string.IsNullOrWhiteSpace(fullPath)) return false;

    var normalizedFullPath = NormalizeAssetPath(Path.GetFullPath(fullPath));
    var normalizedAssetsRoot = NormalizeAssetPath(Application.dataPath).TrimEnd('/');
    if (string.IsNullOrWhiteSpace(normalizedAssetsRoot) ||
        !normalizedFullPath.StartsWith(normalizedAssetsRoot + "/", StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    assetPath = "Assets/" + normalizedFullPath.Substring(normalizedAssetsRoot.Length + 1);
    return true;
  }

  static string ExtractTrailingPathSegment(string path) {
    var normalizedPath = NormalizeAssetPath(path).TrimEnd('/');
    if (string.IsNullOrWhiteSpace(normalizedPath)) return "";
    var separatorIndex = normalizedPath.LastIndexOf('/');
    return separatorIndex >= 0 ? normalizedPath.Substring(separatorIndex + 1) : normalizedPath;
  }

  bool ShouldSkipGroupedNumericOutput(string assetPath, string groupedOutputName) {
    if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(groupedOutputName)) return false;

    var fileName = Path.GetFileNameWithoutExtension(assetPath);
    if (string.Equals(fileName, groupedOutputName, StringComparison.OrdinalIgnoreCase)) {
      return true;
    }
    if (TryExtractGeneratedOutputBaseName(fileName, out var generatedBaseName) &&
        string.Equals(generatedBaseName, groupedOutputName, StringComparison.OrdinalIgnoreCase)) {
      return true;
    }

    var prefix = GetSanitizedOutputPrefix();
    if (!writePrefixedCopy || string.IsNullOrWhiteSpace(prefix)) return false;
    if (string.Equals(fileName, prefix + "_" + groupedOutputName, StringComparison.OrdinalIgnoreCase)) {
      return true;
    }
    return false;
  }

  string BuildOutputAtlasAssetPath(string outputName, string outputFolderPath) {
    var outputFileName = writePrefixedCopy && !string.IsNullOrWhiteSpace(GetSanitizedOutputPrefix())
      ? GetSanitizedOutputPrefix() + "_" + outputName + ".png"
      : outputName + ".png";
    return (outputFolderPath.TrimEnd('/') + "/" + outputFileName).Replace("\\", "/");
  }

  string GetSanitizedOutputPrefix() {
    if (string.IsNullOrWhiteSpace(outputPrefix)) return "";

    var invalidChars = Path.GetInvalidFileNameChars();
    var sanitizedChars = new char[outputPrefix.Length];
    var count = 0;
    for (var i = 0; i < outputPrefix.Length; i++) {
      var c = outputPrefix[i];
      if (Array.IndexOf(invalidChars, c) >= 0) {
        sanitizedChars[count++] = '_';
        continue;
      }

      sanitizedChars[count++] = c;
    }

    return new string(sanitizedChars, 0, count).Trim().Trim('_');
  }

}
#endif
