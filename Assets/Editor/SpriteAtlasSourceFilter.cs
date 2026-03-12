using System;
using System.Collections.Generic;
using System.IO;

internal static class SpriteAtlasSourceFilter {
  static readonly string[] IgnoredFolderNames = { "_Bounces", "Effects", "Expressions" };
  static readonly HashSet<string> IgnoredFolderNameSet = new(IgnoredFolderNames, StringComparer.OrdinalIgnoreCase);

  internal static string IgnoredFolderSummary => string.Join(", ", IgnoredFolderNames);

  internal static bool HasIgnoredFolderInPath(string assetPathOrFolderPath) {
    var normalizedPath = NormalizePath(assetPathOrFolderPath).Trim('/');
    if (string.IsNullOrWhiteSpace(normalizedPath)) return false;

    var folderPath = normalizedPath;
    var trailingSegment = Path.GetFileName(normalizedPath);
    if (!string.IsNullOrWhiteSpace(Path.GetExtension(trailingSegment))) {
      folderPath = NormalizePath(Path.GetDirectoryName(normalizedPath));
    }

    if (string.IsNullOrWhiteSpace(folderPath)) return false;
    var segments = folderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
    for (var i = 0; i < segments.Length; i++) {
      if (IgnoredFolderNameSet.Contains(segments[i])) return true;
    }

    return false;
  }

  static string NormalizePath(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Replace("\\", "/").Trim();
  }
}
