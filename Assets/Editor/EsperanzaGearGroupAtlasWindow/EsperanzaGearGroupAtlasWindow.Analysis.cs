#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed partial class EsperanzaGearGroupAtlasWindow : EditorWindow {
  void AnalyzeFolder() {
    if (!TryGetSourceFolderPath(out var sourceFolderPath, true)) return;
    var sanitizedOutputSubfolder = GetSanitizedOutputSubfolderName();
    if (string.IsNullOrWhiteSpace(sanitizedOutputSubfolder)) {
      EditorUtility.DisplayDialog("Invalid Output Subfolder", "Provide a valid output subfolder name for grouped atlas exports.", "OK");
      return;
    }

    scannedCandidates = CollectGroupCandidates(sourceFolderPath, sanitizedOutputSubfolder);
    analyzedSourceFolderPath = sourceFolderPath;
    var totalAtlasCount = scannedCandidates.Sum(candidate => candidate.sourceAtlases.Count);
    var skinCandidateCount = scannedCandidates.Count(IsSkinCandidate);
    Debug.Log(
      "[GearGroupAtlas] Scan complete." +
      " source='" + sourceFolderPath + "'" +
      " candidates=" + scannedCandidates.Count +
      " gear_candidates=" + (scannedCandidates.Count - skinCandidateCount) +
      " skin_candidates=" + skinCandidateCount +
      " matched_atlases=" + totalAtlasCount);
  }

  List<GroupCandidate> CollectGroupCandidates(string sourceFolderPath, string sanitizedOutputSubfolder) {
    var candidatesByKey = new Dictionary<string, GroupCandidate>(StringComparer.OrdinalIgnoreCase);
    var outputSkippedCount = 0;
    var ignoredFolderSkippedCount = 0;
    var shallowSkippedCount = 0;
    var parseRejectedCount = 0;

    var textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { sourceFolderPath });
    for (var i = 0; i < textureGuids.Length; i++) {
      var assetPath = NormalizePath(AssetDatabase.GUIDToAssetPath(textureGuids[i]));
      if (!IsSupportedColorAtlas(assetPath)) continue;
      if (IsGeneratedNormalAtlasAssetPath(assetPath)) continue;
      if (SpriteAtlasSourceFilter.HasIgnoredFolderInPath(assetPath)) {
        ignoredFolderSkippedCount++;
        continue;
      }
      if (ShouldSkipOutputAsset(assetPath, sanitizedOutputSubfolder)) {
        outputSkippedCount++;
        continue;
      }

      if (!IsSupportedCandidateRelativePath(sourceFolderPath, assetPath)) {
        shallowSkippedCount++;
        continue;
      }

      if (!TryParseSourceAtlasPath(sourceFolderPath, assetPath, out var category, out var form, out var variant, out var partCode, out var fileBase, out var isSkin)) {
        parseRejectedCount++;
        if (parseRejectedCount <= 10) {
          Debug.LogWarning("[GearGroupAtlas] Rejected candidate path. asset='" + assetPath + "'");
        }
        continue;
      }

      var record = new SourceAtlasRecord {
        category = category,
        form = form,
        variant = variant,
        partCode = partCode,
        atlasPath = assetPath,
        normalAtlasPath = "",
        fileBase = fileBase
      };

      var candidateKey = BuildCandidateKey(record, isSkin);
      if (!candidatesByKey.TryGetValue(candidateKey, out var candidate) || candidate == null) {
        candidate = new GroupCandidate {
          form = form,
          variant = variant,
          partCode = partCode,
          isSkin = isSkin
        };
        candidatesByKey[candidateKey] = candidate;
      }

      candidate.sourceAtlases.Add(record);
    }

    var candidates = candidatesByKey.Values.ToList();
    for (var i = 0; i < candidates.Count; i++) {
      var candidate = candidates[i];
      FinalizeCandidate(candidate);
    }

    candidates.Sort(CompareCandidates);
    Debug.Log(
      "[GearGroupAtlas] Candidate path scan." +
      " source='" + sourceFolderPath + "'" +
      " textures=" + textureGuids.Length +
      " ignored_folder_skipped=" + ignoredFolderSkippedCount +
      " ignored_folders='" + SpriteAtlasSourceFilter.IgnoredFolderSummary + "'" +
      " output_skipped=" + outputSkippedCount +
      " shallow_skipped=" + shallowSkippedCount +
      " parse_rejected=" + parseRejectedCount +
      " matched=" + candidates.Sum(candidate => candidate?.sourceAtlases?.Count ?? 0));
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

  static string BuildCandidateKey(SourceAtlasRecord record, bool isSkin) {
    if (record == null) return "";
    if (isSkin) {
      return SkinGroupKey + "|" + (record.partCode ?? "");
    }

    return (record.form ?? "") + "|" + (record.variant ?? "") + "|" + (record.partCode ?? "");
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

    candidate.sourceAtlases = candidate.sourceAtlases
      .Where(record => record != null)
      .OrderBy(record => record.category, SpriteSliceAddressUtility.NaturalStringComparer)
      .ThenBy(record => record.fileBase, SpriteSliceAddressUtility.NaturalStringComparer)
      .ThenBy(record => record.atlasPath, SpriteSliceAddressUtility.NaturalStringComparer)
      .ToList();

    candidate.sourceCategories = candidate.sourceAtlases
      .Select(record => (record.category ?? "").Trim())
      .Where(category => !string.IsNullOrWhiteSpace(category))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .OrderBy(category => category, SpriteSliceAddressUtility.NaturalStringComparer)
      .ToList();

    candidate.normalAtlasCount = candidate.sourceAtlases.Count(record => !string.IsNullOrWhiteSpace(record.normalAtlasPath));
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
    return PartCodeByToken.Values.Contains(resolvedPartCode, StringComparer.OrdinalIgnoreCase);
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

    relativeSegments = pathSegments
      .Take(pathSegments.Length - 1)
      .Select(segment => (segment ?? "").Trim())
      .Where(segment => !string.IsNullOrWhiteSpace(segment))
      .ToArray();
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

    var orderedCandidates = candidates
      .OrderByDescending(candidate => IsKnownPartCodeToken(candidate.token))
      .ThenByDescending(candidate => candidate.index)
      .ToList();
    var resolvedPartCode = ResolvePartCode(orderedCandidates[0].token);
    if (string.IsNullOrWhiteSpace(resolvedPartCode)) return false;

    partIndex = orderedCandidates[0].index;
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
}
#endif
