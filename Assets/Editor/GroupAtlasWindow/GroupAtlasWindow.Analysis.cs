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

    var requireSingleSourceContract = IsValidAutoFindSourceRootPath(sourceAtlasPaths);
    scannedCandidates = CollectGroupCandidates(
      sourceRootPath,
      sourceAtlasPaths,
      sanitizedOutputName,
      requireSingleSourceContract,
      out error);
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
    AtlasAuthoringLog.Verbose(
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
    bool requireSingleSourceContract,
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
    var hasMixedForms = false;
    var hasMixedVariants = false;
    var hasMixedPartCodes = false;
    var detectedSkin = false;
    var hasMixedSkinGearContract = false;
    var candidate = new GroupCandidate {
      outputName = sanitizedOutputName,
      usesFolderStructureSpriteNames = !requireSingleSourceContract
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
        var hasDifferentForm = !string.Equals(detectedForm, form, StringComparison.OrdinalIgnoreCase);
        var hasDifferentVariant = !string.Equals(detectedVariant, variant, StringComparison.OrdinalIgnoreCase);
        var hasDifferentSkinContract = detectedSkin != isSkin;
        if (hasDifferentForm) {
          hasMixedForms = true;
        }

        if (hasDifferentVariant) {
          hasMixedVariants = true;
        }

        if (hasDifferentSkinContract) {
          hasMixedSkinGearContract = true;
        }

        if (requireSingleSourceContract &&
            (hasDifferentForm || hasDifferentVariant || hasDifferentSkinContract)) {
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

    candidate.form = hasMixedForms ? MixedGroupToken : detectedForm;
    candidate.variant = hasMixedVariants ? MixedGroupToken : detectedVariant;
    candidate.partCode = hasMixedPartCodes ? MixedGroupToken : detectedPartCode;
    candidate.isSkin = detectedSkin && !hasMixedSkinGearContract;
    candidate.hasMixedSkinGearContract = hasMixedSkinGearContract;
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
    var normalizedToken = (token ?? "").Trim();
    if (string.IsNullOrWhiteSpace(normalizedToken)) {
      return "";
    }

    if (PartCodeByToken.TryGetValue(normalizedToken, out var mappedPartCode) &&
        !string.IsNullOrWhiteSpace(mappedPartCode)) {
      return mappedPartCode;
    }

    return normalizedToken;
  }

  static string ResolvePartToken(string code) {
    var normalizedCode = (code ?? "").Trim();
    if (string.IsNullOrWhiteSpace(normalizedCode)) {
      return "";
    }

    if (PartTokenByCode.TryGetValue(normalizedCode, out var mappedPartToken) &&
        !string.IsNullOrWhiteSpace(mappedPartToken)) {
      return mappedPartToken;
    }

    return normalizedCode;
  }

  static bool IsKnownPartCodeToken(string token) {
    var normalizedToken = (token ?? "").Trim();
    if (string.IsNullOrWhiteSpace(normalizedToken)) {
      return false;
    }

    if (PartCodeByToken.ContainsKey(normalizedToken)) {
      return true;
    }

    return PartTokenByCode.ContainsKey(normalizedToken);
  }

  static Dictionary<string, string> BuildPartTokenByCode() {
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var pair in PartCodeByToken) {
      var partToken = (pair.Key ?? "").Trim();
      var partCode = (pair.Value ?? "").Trim();
      if (string.IsNullOrWhiteSpace(partToken)) {
        continue;
      }

      if (string.IsNullOrWhiteSpace(partCode)) {
        continue;
      }

      if (result.ContainsKey(partCode)) {
        continue;
      }

      result.Add(partCode, partToken);
    }

    return result;
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

  static bool ContainsSkinSegment(string[] relativeSegments) {
    if (relativeSegments == null) {
      return false;
    }

    for (var i = 0; i < relativeSegments.Length; i++) {
      if (string.Equals(relativeSegments[i], SkinFormName, StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
  }

  static bool TryParseSkinSourceAtlasPath(
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
    if (relativeSegments == null || relativeSegments.Length <= 0) return false;

    for (var skinIndex = 0; skinIndex < relativeSegments.Length; skinIndex++) {
      if (!string.Equals(relativeSegments[skinIndex], SkinFormName, StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      if (TryResolveSkinPartAndCategory(sourceFolderPath, relativeSegments, skinIndex, out category, out partCode)) {
        form = SkinFormName;
        variant = SkinVariantName;
        isSkin = true;
        return true;
      }
    }

    return false;
  }

  static bool TryResolveSkinPartAndCategory(
    string sourceFolderPath,
    string[] relativeSegments,
    int skinIndex,
    out string category,
    out string partCode) {
    category = "";
    partCode = "";

    if (TryResolveSkinPartCode(relativeSegments, skinIndex + 1, out partCode)) {
      category = ResolveSkinCategory(sourceFolderPath, relativeSegments, skinIndex);
      return !string.IsNullOrWhiteSpace(category);
    }

    if (TryResolveSkinPartCode(relativeSegments, skinIndex - 1, out partCode)) {
      category = ResolveSkinCategory(sourceFolderPath, relativeSegments, skinIndex - 1);
      return !string.IsNullOrWhiteSpace(category);
    }

    return false;
  }

  static bool TryResolveSkinPartCode(string[] relativeSegments, int partIndex, out string partCode) {
    partCode = "";
    if (relativeSegments == null) return false;
    if (partIndex < 0 || partIndex >= relativeSegments.Length) return false;

    var partToken = (relativeSegments[partIndex] ?? "").Trim();
    if (string.IsNullOrWhiteSpace(partToken)) return false;
    if (string.Equals(partToken, SkinFormName, StringComparison.OrdinalIgnoreCase)) return false;
    if (TryParseWrappedDescriptorToken(partToken, out _, out _, out _)) return false;

    partCode = ResolvePartCode(partToken);
    return !string.IsNullOrWhiteSpace(partCode);
  }

  static string ResolveSkinCategory(string sourceFolderPath, string[] relativeSegments, int clusterStartIndex) {
    if (TryResolveNearestCategoryToken(relativeSegments, clusterStartIndex, out var category)) {
      return category;
    }

    return System.IO.Path.GetFileName(NormalizePath(sourceFolderPath).TrimEnd('/'));
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

    if (TryParseSkinSourceAtlasPath(sourceFolderPath, relativeSegments, out category, out form, out variant, out partCode, out isSkin)) {
      return !string.IsNullOrWhiteSpace(fileBase);
    }

    if (ContainsSkinSegment(relativeSegments)) {
      return false;
    }

    if (TryParseTo2TransitionSourceAtlasPath(relativeSegments, out category, out form, out variant, out partCode, out isSkin)) {
      return !string.IsNullOrWhiteSpace(fileBase);
    }

    if (TryParseWrappedDescriptorSourceAtlasPath(relativeSegments, out category, out form, out variant, out partCode, out isSkin)) {
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

    var filteredSourceAtlasPaths = new List<string>(sourceAtlasPaths.Count);
    for (var i = 0; i < sourceAtlasPaths.Count; i++) {
      var assetPath = sourceAtlasPaths[i];
      if (SpriteAtlasSourceFilter.HasIgnoredSubfolderInPath(sourceRootPath, assetPath)) {
        continue;
      }

      filteredSourceAtlasPaths.Add(assetPath);
    }

    sourceAtlasPaths = filteredSourceAtlasPaths;
    if (sourceAtlasPaths.Count <= 0) {
      error = "No source atlas assets remained after ignored subfolders were filtered out.";
      return false;
    }

    return true;
  }

  string BuildSourceRootPath(List<string> sourceAtlasPaths) {
    if (IsValidAutoFindSourceRootPath(sourceAtlasPaths)) {
      return ResolveNearestParseableSourceRootPath(autoFindSourceRootPath, sourceAtlasPaths);
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

    var commonRootPath = string.Join("/", sharedSegments);
    return ResolveNearestParseableSourceRootPath(commonRootPath, sourceAtlasPaths);
  }

  static string ResolveNearestParseableSourceRootPath(string commonRootPath, List<string> sourceAtlasPaths) {
    var fallbackRootPath = NormalizePath(commonRootPath).TrimEnd('/');
    var candidateRootPath = fallbackRootPath;
    while (!string.IsNullOrWhiteSpace(candidateRootPath)) {
      if (CanParseAllSourceAtlasPaths(candidateRootPath, sourceAtlasPaths)) {
        return candidateRootPath;
      }

      var rawParentPath = System.IO.Path.GetDirectoryName(candidateRootPath);
      var parentPath = NormalizePath(rawParentPath).TrimEnd('/');
      if (string.IsNullOrWhiteSpace(parentPath)) {
        break;
      }

      if (string.Equals(parentPath, candidateRootPath, StringComparison.OrdinalIgnoreCase)) {
        break;
      }

      candidateRootPath = parentPath;
    }

    return fallbackRootPath;
  }

  static bool CanParseAllSourceAtlasPaths(string sourceRootPath, List<string> sourceAtlasPaths) {
    if (string.IsNullOrWhiteSpace(sourceRootPath)) {
      return false;
    }

    if (sourceAtlasPaths == null || sourceAtlasPaths.Count <= 0) {
      return false;
    }

    for (var i = 0; i < sourceAtlasPaths.Count; i++) {
      if (!TryParseSourceAtlasPath(
            sourceRootPath,
            sourceAtlasPaths[i],
            out _,
            out _,
            out _,
            out _,
            out _,
            out _)) {
        return false;
      }
    }

    return true;
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
