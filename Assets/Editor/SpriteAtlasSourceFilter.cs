internal static class SpriteAtlasSourceFilter {
  internal static string IgnoredFolderSummary => GeneratedAtlasBuildSurrogateUtility.MetadataExcludedFolderSummary;

  internal static bool HasIgnoredFolderInPath(string assetPathOrFolderPath) {
    return GeneratedAtlasBuildSurrogateUtility.HasMetadataExcludedFolderInPath(assetPathOrFolderPath);
  }
}
