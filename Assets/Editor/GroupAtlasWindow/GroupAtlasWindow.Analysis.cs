#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed partial class GroupAtlasWindow : EditorWindow {
  void AnalyzeFolder() {
    if (!TryGetOutputFolderPath(out _, true)) return;
    var sanitizedOutputName = GetSanitizedOutputName();
    if (!TryCollectSelectedSourceAtlasPaths(out var sourceAtlasPaths, out var sourceRootPath, out var error)) {
      EditorUtility.DisplayDialog("Invalid Source Atlases", error, "OK");
      return;
    }

    scannedCandidates = CollectGroupCandidates(sourceRootPath, sourceAtlasPaths, sanitizedOutputName, out error);
    if (!string.IsNullOrWhiteSpace(error)) {
      EditorUtility.DisplayDialog("Analyze Failed", error, "OK");
      scannedCandidates = new List<GroupCandidate>();
      analyzedSelectionSignature = "";
      analyzedSourceRootPath = "";
      return;
    }

    analyzedSelectionSignature = BuildSelectionSignature();
    analyzedSourceRootPath = sourceRootPath;
    var totalAtlasCount = 0;
    var skinCandidateCount = 0;
    for (var i = 0; i < scannedCandidates.Count; i++) {
      var candidate = scannedCandidates[i];
      if (candidate == null) {
        continue;
      }

      totalAtlasCount += candidate.sourceAtlases?.Count ?? 0;
      if (IsSkinCandidate(candidate)) {
        skinCandidateCount++;
      }
    }
    Debug.Log(
      "[GearGroupAtlas] Selection analysis complete." +
      " source_root='" + sourceRootPath + "'" +
      " output_name='" + sanitizedOutputName + "'" +
      " candidates=" + scannedCandidates.Count +
      " gear_candidates=" + (scannedCandidates.Count - skinCandidateCount) +
      " skin_candidates=" + skinCandidateCount +
      " matched_atlases=" + totalAtlasCount);
  }

  List<GroupCandidate> CollectGroupCandidates(
    string sourceRootPath,
    List<string> sourceAtlasPaths,
    string sanitizedOutputName,
    out string error) {
    error = "";
    var candidates = new List<GroupCandidate>();
    if (sourceAtlasPaths == null || sourceAtlasPaths.Count <= 0) {
      error = "No source atlas assets were selected.";
      return candidates;
    }

    string detectedForm = "";
    string detectedVariant = "";
    string detectedPartCode = "";
    var hasMixedPartCodes = false;
    var detectedSkin = false;
    var candidate = new GroupCandidate {
      outputName = sanitizedOutputName
    };

    for (var i = 0; i < sourceAtlasPaths.Count; i++) {
      var assetPath = sourceAtlasPaths[i];
      if (!TryParseSourceAtlasPath(sourceRootPath, assetPath, out var category, out var form, out var variant, out var partCode, out var fileBase, out var isSkin)) {
        error = "Could not parse source atlas path '" + assetPath + "'.";
        return new List<GroupCandidate>();
      }

      if (i == 0) {
        detectedForm = form;
        detectedVariant = variant;
        detectedPartCode = partCode;
        detectedSkin = isSkin;
      }
      else {
        if (!string.Equals(detectedForm, form, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(detectedVariant, variant, StringComparison.OrdinalIgnoreCase) ||
            detectedSkin != isSkin) {
          error = "All selected atlases must share the same form/variant and skin-vs-gear contract.";
          return new List<GroupCandidate>();
        }

        if (!string.Equals(detectedPartCode, partCode, StringComparison.OrdinalIgnoreCase)) {
          hasMixedPartCodes = true;
        }
      }

      candidate.sourceAtlases.Add(new SourceAtlasRecord {
        category = category,
        form = form,
        variant = variant,
        partCode = partCode,
        atlasPath = assetPath,
        normalAtlasPath = "",
        fileBase = fileBase
      });
    }

    candidate.form = detectedForm;
    candidate.variant = detectedVariant;
    candidate.partCode = hasMixedPartCodes ? "Mixed" : detectedPartCode;
    candidate.isSkin = detectedSkin;
    FinalizeCandidate(candidate);
    candidates.Add(candidate);
    return candidates;
  }

  static int CompareCandidates(GroupCandidate left, GroupCandidate right) {
    var formCompare = SpriteSliceAddressUtility.CompareNaturally(left?.form, right?.form);
    if (formCompare != 0) return formCompare;

    var variantCompare = SpriteSliceAddressUtility.CompareNaturally(left?.variant, right?.variant);
    if (variantCompare != 0) return variantCompare;

    var partCompare = SpriteSliceAddressUtility.CompareNaturally(left?.partCode, right?.partCode);
    if (partCompare != 0) return partCompare;

    return SpriteSliceAddressUtility.CompareNaturally(BuildCandidateAnimationSummary(left), BuildCandidateAnimationSummary(right));
  }

  static string BuildCandidateAnimationSummary(GroupCandidate candidate) {
    if (candidate?.sourceCategories == null || candidate.sourceCategories.Count <= 0) return "";
    return string.Join("|", candidate.sourceCategories);
  }

  static void FinalizeCandidate(GroupCandidate candidate) {
    if (candidate == null) return;

    if (candidate.sourceAtlases == null) {
      candidate.sourceAtlases = new List<SourceAtlasRecord>();
    }

    var orderedAtlases = new List<SourceAtlasRecord>(candidate.sourceAtlases.Count);
    var sourceCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var normalAtlasCount = 0;
    for (var i = 0; i < candidate.sourceAtlases.Count; i++) {
      var record = candidate.sourceAtlases[i];
      if (record == null) {
        continue;
      }

      orderedAtlases.Add(record);
      var category = (record.category ?? "").Trim();
      if (!string.IsNullOrWhiteSpace(category)) {
        sourceCategories.Add(category);
      }
      if (!string.IsNullOrWhiteSpace(record.normalAtlasPath)) {
        normalAtlasCount++;
      }
    }

    orderedAtlases.Sort(CompareSourceAtlasRecords);
    candidate.sourceAtlases = orderedAtlases;
    candidate.sourceCategories = new List<string>(sourceCategories);
    candidate.sourceCategories.Sort(SpriteSliceAddressUtility.NaturalStringComparer);
    candidate.normalAtlasCount = normalAtlasCount;
  }

  static string ResolvePartCode(string token) {
    if (string.IsNullOrWhiteSpace(token)) return "";
    if (PartCodeByToken.TryGetValue(token.Trim(), out var mappedPartCode) && !string.IsNullOrWhiteSpace(mappedPartCode)) {
      return mappedPartCode;
    }

    return token.Trim();
  }

  static bool IsKnownPartCodeToken(string token) {
    var resolvedPartCode = ResolvePartCode(token);
    if (string.IsNullOrWhiteSpace(resolvedPartCode)) return false;
    foreach (var value in PartCodeByToken.Values) {
      if (string.Equals(value, resolvedPartCode, StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
  }

  static bool TryParseWrappedDescriptorToken(string token, out string form, out string variant, out bool isSkin) {
    form = "";
    variant = "";
    isSkin = false;
    if (string.IsNullOrWhiteSpace(token)) return false;

    var normalizedToken = token.Trim();
    if (string.Equals(normalizedToken, SkinFormName, StringComparison.OrdinalIgnoreCase)) {
      form = SkinFormName;
      variant = SkinVariantName;
      isSkin = true;
      return true;
    }

    var separatorIndex = normalizedToken.IndexOf('_');
    if (separatorIndex <= 0 || separatorIndex >= normalizedToken.Length - 1) return false;

    form = normalizedToken.Substring(0, separatorIndex).Trim();
    variant = normalizedToken.Substring(separatorIndex + 1).Trim();
    return !string.IsNullOrWhiteSpace(form) && !string.IsNullOrWhiteSpace(variant);
  }

  static bool TryGetSourceRelativeDirectorySegments(
    string sourceFolderPath,
    string assetPath,
    out string[] relativeSegments,
    out string fileBase) {
    relativeSegments = Array.Empty<string>();
    fileBase = "";

    var normalizedSourceFolderPath = NormalizePath(sourceFolderPath).TrimEnd('/');
    var normalizedAssetPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedSourceFolderPath) || string.IsNullOrWhiteSpace(normalizedAssetPath)) return false;
    if (!normalizedAssetPath.StartsWith(normalizedSourceFolderPath + "/", StringComparison.OrdinalIgnoreCase)) return false;

    var relativePath = normalizedAssetPath.Substring(normalizedSourceFolderPath.Length + 1);
    var pathSegments = relativePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
    if (pathSegments.Length < 2) return false;

    fileBase = System.IO.Path.GetFileNameWithoutExtension(pathSegments[pathSegments.Length - 1]);
    if (string.IsNullOrWhiteSpace(fileBase)) return false;

    var segments = new List<string>(pathSegments.Length - 1);
    for (var i = 0; i < pathSegments.Length - 1; i++) {
      var segment = (pathSegments[i] ?? "").Trim();
      if (!string.IsNullOrWhiteSpace(segment)) {
        segments.Add(segment);
      }
    }

    relativeSegments = segments.ToArray();
    return relativeSegments.Length > 0;
  }

  static bool TryResolveNearestCategoryToken(string[] relativeSegments, int clusterStartIndex, out string category) {
    category = "";
    if (relativeSegments == null || clusterStartIndex <= 0) return false;

    for (var i = clusterStartIndex - 1; i >= 0; i--) {
      var token = (relativeSegments[i] ?? "").Trim();
      if (string.IsNullOrWhiteSpace(token)) continue;
      category = token;
      return true;
    }

    return false;
  }

  static bool TryResolveAdjacentPartCode(string[] relativeSegments, int descriptorIndex, out int partIndex, out string partCode) {
    partIndex = -1;
    partCode = "";
    if (relativeSegments == null || descriptorIndex < 0 || descriptorIndex >= relativeSegments.Length) return false;

    var candidates = new List<(int index, string token)>();
    TryAddAdjacentPartCandidate(relativeSegments, descriptorIndex - 1, candidates);
    TryAddAdjacentPartCandidate(relativeSegments, descriptorIndex + 1, candidates);
    if (candidates.Count <= 0) return false;

    candidates.Sort(CompareAdjacentPartCandidates);
    var resolvedPartCode = ResolvePartCode(candidates[0].token);
    if (string.IsNullOrWhiteSpace(resolvedPartCode)) return false;

    partIndex = candidates[0].index;
    partCode = resolvedPartCode;
    return true;
  }

  static void TryAddAdjacentPartCandidate(string[] relativeSegments, int index, List<(int index, string token)> candidates) {
    if (relativeSegments == null || candidates == null) return;
    if (index < 0 || index >= relativeSegments.Length) return;

    var token = (relativeSegments[index] ?? "").Trim();
    if (string.IsNullOrWhiteSpace(token)) return;
    if (TryParseWrappedDescriptorToken(token, out _, out _, out _)) return;
    candidates.Add((index, token));
  }

  static bool TryParseWrappedDescriptorSourceAtlasPath(
    string[] relativeSegments,
    out string category,
    out string form,
    out string variant,
    out string partCode,
    out bool isSkin) {
    category = "";
    form = "";
    variant = "";
    partCode = "";
    isSkin = false;
    if (relativeSegments == null || relativeSegments.Length < 3) return false;

    for (var descriptorIndex = relativeSegments.Length - 1; descriptorIndex >= 0; descriptorIndex--) {
      if (!TryParseWrappedDescriptorToken(relativeSegments[descriptorIndex], out form, out variant, out isSkin)) continue;
      if (!TryResolveAdjacentPartCode(relativeSegments, descriptorIndex, out var partIndex, out partCode)) continue;
      if (!TryResolveNearestCategoryToken(relativeSegments, Math.Min(descriptorIndex, partIndex), out category)) continue;
      return !string.IsNullOrWhiteSpace(category) &&
             !string.IsNullOrWhiteSpace(form) &&
             !string.IsNullOrWhiteSpace(variant) &&
             !string.IsNullOrWhiteSpace(partCode);
    }

    return false;
  }

  static bool TryParseTo2TransitionSourceAtlasPath(
    string[] relativeSegments,
    out string category,
    out string form,
    out string variant,
    out string partCode,
    out bool isSkin) {
    category = "";
    form = "";
    variant = "";
    partCode = "";
    isSkin = false;
    if (relativeSegments == null || relativeSegments.Length < 3 || relativeSegments.Length > 4) return false;

    var leadingCategory = (relativeSegments[0] ?? "").Trim();
    if (!string.Equals(leadingCategory, "To2", StringComparison.OrdinalIgnoreCase)) return false;
    if (!TryParseWrappedDescriptorToken(relativeSegments[relativeSegments.Length - 1], out form, out variant, out isSkin) || isSkin) {
      return false;
    }

    partCode = ResolvePartCode(relativeSegments[relativeSegments.Length - 2]);
    if (string.IsNullOrWhiteSpace(partCode)) return false;

    category = "To2";
    return !string.IsNullOrWhiteSpace(form) && !string.IsNullOrWhiteSpace(variant);
  }

  static bool TryParseDirectGearSourceAtlasPath(
    string[] relativeSegments,
    out string category,
    out string form,
    out string variant,
    out string partCode,
    out bool isSkin) {
    category = "";
    form = "";
    variant = "";
    partCode = "";
    isSkin = false;
    if (relativeSegments == null || relativeSegments.Length < 4) return false;

    partCode = ResolvePartCode(relativeSegments[relativeSegments.Length - 1]);
    variant = (relativeSegments[relativeSegments.Length - 2] ?? "").Trim();
    form = (relativeSegments[relativeSegments.Length - 3] ?? "").Trim();
    if (string.IsNullOrWhiteSpace(partCode) ||
        string.IsNullOrWhiteSpace(form) ||
        string.IsNullOrWhiteSpace(variant) ||
        TryParseWrappedDescriptorToken(form, out _, out _, out _)) {
      return false;
    }

    if (!TryResolveNearestCategoryToken(relativeSegments, relativeSegments.Length - 3, out category)) {
      return false;
    }

    return !string.IsNullOrWhiteSpace(category);
  }

  static bool TryParseRootRelativeGearSourceAtlasPath(
    string sourceFolderPath,
    string[] relativeSegments,
    out string category,
    out string form,
    out string variant,
    out string partCode,
    out bool isSkin) {
    category = "";
    form = "";
    variant = "";
    partCode = "";
    isSkin = false;
    if (relativeSegments == null || relativeSegments.Length != 3) return false;

    category = System.IO.Path.GetFileName(NormalizePath(sourceFolderPath).TrimEnd('/'));
    form = (relativeSegments[0] ?? "").Trim();
    variant = (relativeSegments[1] ?? "").Trim();
    partCode = ResolvePartCode(relativeSegments[2]);
    return !string.IsNullOrWhiteSpace(category) &&
           !string.IsNullOrWhiteSpace(form) &&
           !string.IsNullOrWhiteSpace(variant) &&
           !string.IsNullOrWhiteSpace(partCode);
  }

  static bool TryParseImplicitSkinSourceAtlasPath(
    string[] relativeSegments,
    out string category,
    out string form,
    out string variant,
    out string partCode,
    out bool isSkin) {
    category = "";
    form = "";
    variant = "";
    partCode = "";
    isSkin = false;
    if (relativeSegments == null || relativeSegments.Length != 3) return false;

    partCode = ResolvePartCode(relativeSegments[relativeSegments.Length - 1]);
    if (!string.Equals(partCode, "e", StringComparison.OrdinalIgnoreCase)) return false;

    category = (relativeSegments[0] ?? "").Trim();
    var sourceForm = (relativeSegments[1] ?? "").Trim();
    if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(sourceForm)) return false;
    if (TryParseWrappedDescriptorToken(sourceForm, out _, out _, out _)) return false;

    form = SkinFormName;
    variant = SkinVariantName;
    isSkin = true;
    return true;
  }

  static bool TryParseSourceAtlasPath(
    string sourceFolderPath,
    string assetPath,
    out string category,
    out string form,
    out string variant,
    out string partCode,
    out string fileBase,
    out bool isSkin) {
    category = "";
    form = "";
    variant = "";
    partCode = "";
    fileBase = "";
    isSkin = false;

    if (!TryGetSourceRelativeDirectorySegments(sourceFolderPath, assetPath, out var relativeSegments, out fileBase)) {
      return false;
    }

    if (TryParseTo2TransitionSourceAtlasPath(relativeSegments, out category, out form, out variant, out partCode, out isSkin)) {
      return !string.IsNullOrWhiteSpace(fileBase);
    }

    if (TryParseWrappedDescriptorSourceAtlasPath(relativeSegments, out category, out form, out variant, out partCode, out isSkin)) {
      return !string.IsNullOrWhiteSpace(fileBase);
    }

    if (TryParseImplicitSkinSourceAtlasPath(relativeSegments, out category, out form, out variant, out partCode, out isSkin)) {
      return !string.IsNullOrWhiteSpace(fileBase);
    }

    if (TryParseDirectGearSourceAtlasPath(relativeSegments, out category, out form, out variant, out partCode, out isSkin)) {
      return !string.IsNullOrWhiteSpace(fileBase);
    }

    if (TryParseRootRelativeGearSourceAtlasPath(sourceFolderPath, relativeSegments, out category, out form, out variant, out partCode, out isSkin)) {
      return !string.IsNullOrWhiteSpace(fileBase);
    }
    return false;
  }

  static bool IsSupportedColorAtlas(string assetPath) {
    return string.Equals(System.IO.Path.GetExtension(assetPath), ".png", StringComparison.OrdinalIgnoreCase);
  }

  static bool IsGeneratedNormalAtlasAssetPath(string assetPath) {
    var fileName = System.IO.Path.GetFileNameWithoutExtension(assetPath ?? "");
    return fileName.EndsWith("_N", StringComparison.OrdinalIgnoreCase);
  }

  static bool ShouldSkipOutputAsset(string assetPath, string sanitizedOutputSubfolder) {
    if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(sanitizedOutputSubfolder)) return false;
    var marker = "/" + sanitizedOutputSubfolder.Trim('/') + "/";
    return assetPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
  }

  static bool IsSupportedCandidateRelativePath(string sourceFolderPath, string assetPath) {
    return TryGetSourceRelativeDirectorySegments(sourceFolderPath, assetPath, out var relativeSegments, out _) &&
           relativeSegments.Length >= 3;
  }

  bool TryCollectSelectedSourceAtlasPaths(out List<string> sourceAtlasPaths, out string sourceRootPath, out string error) {
    sourceAtlasPaths = new List<string>();
    sourceRootPath = "";
    error = "";

    var distinctPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < sourceAtlases.Count; i++) {
      var atlas = sourceAtlases[i];
      if (atlas == null) continue;
      var assetPath = NormalizePath(AssetDatabase.GetAssetPath(atlas));
      if (string.IsNullOrWhiteSpace(assetPath)) continue;
      if (!distinctPaths.Add(assetPath)) continue;
      if (!IsSupportedColorAtlas(assetPath)) {
        error = "Only color PNG atlases are supported. Invalid asset: " + assetPath;
        return false;
      }
      if (IsGeneratedNormalAtlasAssetPath(assetPath)) {
        error = "Normal atlas inputs are not supported. Remove: " + assetPath;
        return false;
      }
      if (SpriteAtlasSourceFilter.HasIgnoredFolderInPath(assetPath)) {
        error = "Selected atlas is inside an ignored folder: " + assetPath;
        return false;
      }

      sourceAtlasPaths.Add(assetPath);
    }

    if (sourceAtlasPaths.Count <= 0) {
      error = "Add at least one source atlas.";
      return false;
    }

    sourceRootPath = BuildSourceRootPath(sourceAtlasPaths);
    if (string.IsNullOrWhiteSpace(sourceRootPath) || !AssetDatabase.IsValidFolder(sourceRootPath)) {
      error = "Could not resolve a common source root folder from the selected atlases.";
      return false;
    }

    return true;
  }

  string BuildSourceRootPath(List<string> sourceAtlasPaths) {
    if (IsValidAutoFindSourceRootPath(sourceAtlasPaths)) {
      return autoFindSourceRootPath;
    }

    return BuildCommonSourceRootPath(sourceAtlasPaths);
  }

  bool IsValidAutoFindSourceRootPath(List<string> sourceAtlasPaths) {
    if (string.IsNullOrWhiteSpace(autoFindSourceRootPath)) return false;
    if (!AssetDatabase.IsValidFolder(autoFindSourceRootPath)) return false;
    if (sourceAtlasPaths == null || sourceAtlasPaths.Count <= 0) return false;

    var rootPath = NormalizePath(autoFindSourceRootPath).TrimEnd('/');
    for (var i = 0; i < sourceAtlasPaths.Count; i++) {
      var assetPath = NormalizePath(sourceAtlasPaths[i]);
      if (!assetPath.StartsWith(rootPath + "/", StringComparison.OrdinalIgnoreCase)) {
        return false;
      }
    }

    return true;
  }

  static string BuildCommonSourceRootPath(List<string> sourceAtlasPaths) {
    if (sourceAtlasPaths == null || sourceAtlasPaths.Count <= 0) return "";

    string[] sharedSegments = null;
    for (var i = 0; i < sourceAtlasPaths.Count; i++) {
      var assetPath = NormalizePath(sourceAtlasPaths[i]);
      if (string.IsNullOrWhiteSpace(assetPath)) continue;
      var folderPath = NormalizePath(System.IO.Path.GetDirectoryName(assetPath));
      if (string.IsNullOrWhiteSpace(folderPath)) continue;
      var currentSegments = folderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
      if (sharedSegments == null) {
        sharedSegments = currentSegments;
        continue;
      }

      var maxLength = Math.Min(sharedSegments.Length, currentSegments.Length);
      var sharedLength = 0;
      while (sharedLength < maxLength &&
             string.Equals(sharedSegments[sharedLength], currentSegments[sharedLength], StringComparison.OrdinalIgnoreCase)) {
        sharedLength++;
      }

      if (sharedLength <= 0) return "";
      var nextSharedSegments = new string[sharedLength];
      Array.Copy(sharedSegments, nextSharedSegments, sharedLength);
      sharedSegments = nextSharedSegments;
    }

    if (sharedSegments == null || sharedSegments.Length <= 0) return "";
    return string.Join("/", sharedSegments);
  }

  static int CompareSourceAtlasRecords(SourceAtlasRecord left, SourceAtlasRecord right) {
    if (ReferenceEquals(left, right)) return 0;
    if (left == null) return -1;
    if (right == null) return 1;

    var categoryComparison = SpriteSliceAddressUtility.NaturalStringComparer.Compare(left.category, right.category);
    if (categoryComparison != 0) return categoryComparison;

    var fileBaseComparison = SpriteSliceAddressUtility.NaturalStringComparer.Compare(left.fileBase, right.fileBase);
    if (fileBaseComparison != 0) return fileBaseComparison;

    return SpriteSliceAddressUtility.NaturalStringComparer.Compare(left.atlasPath, right.atlasPath);
  }

  static int CompareAdjacentPartCandidates((int index, string token) left, (int index, string token) right) {
    var leftKnown = IsKnownPartCodeToken(left.token);
    var rightKnown = IsKnownPartCodeToken(right.token);
    if (leftKnown != rightKnown) {
      return leftKnown ? -1 : 1;
    }

    return right.index.CompareTo(left.index);
  }
}
#endif
