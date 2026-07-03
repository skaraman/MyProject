using System;
using System.IO;

internal static class SpriteAtlasSourceFilter {
  internal static string IgnoredFolderSummary {
    get {
      var metadataSummary = GeneratedAtlasBuildSurrogateUtility.MetadataExcludedFolderSummary;
      if (string.IsNullOrWhiteSpace(metadataSummary)) {
        return "subfolders starting with '_'";
      }

      return metadataSummary + ", subfolders starting with '_'";
    }
  }

  internal static bool HasIgnoredFolderInPath(string assetPathOrFolderPath) {
    return GeneratedAtlasBuildSurrogateUtility.HasMetadataExcludedFolderInPath(assetPathOrFolderPath);
  }

  internal static bool HasIgnoredSubfolderInPath(string rootAssetPath, string assetPathOrFolderPath) {
    if (HasIgnoredFolderInPath(assetPathOrFolderPath)) {
      return true;
    }

    var normalizedRootPath = NormalizeFolderPath(rootAssetPath);
    var normalizedAssetFolderPath = NormalizeFolderPath(assetPathOrFolderPath);
    if (string.IsNullOrWhiteSpace(normalizedRootPath) || string.IsNullOrWhiteSpace(normalizedAssetFolderPath)) {
      return false;
    }

    if (string.Equals(normalizedRootPath, normalizedAssetFolderPath, StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    if (!normalizedAssetFolderPath.StartsWith(normalizedRootPath + "/", StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    var relativeFolderPath = normalizedAssetFolderPath.Substring(normalizedRootPath.Length + 1);
    var relativeSegments = relativeFolderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
    for (var i = 0; i < relativeSegments.Length; i++) {
      var segment = relativeSegments[i];
      if (!string.IsNullOrWhiteSpace(segment) && segment[0] == '_') {
        return true;
      }
    }

    return false;
  }

  static string NormalizeFolderPath(string assetPathOrFolderPath) {
    var normalizedPath = GeneratedAtlasBuildSurrogateUtility.NormalizePath(assetPathOrFolderPath).Trim('/');
    if (string.IsNullOrWhiteSpace(normalizedPath)) {
      return "";
    }

    var trailingSegment = Path.GetFileName(normalizedPath);
    if (!string.IsNullOrWhiteSpace(Path.GetExtension(trailingSegment))) {
      normalizedPath = GeneratedAtlasBuildSurrogateUtility.NormalizePath(Path.GetDirectoryName(normalizedPath));
    }

    return (normalizedPath ?? "").Trim('/');
  }
}
