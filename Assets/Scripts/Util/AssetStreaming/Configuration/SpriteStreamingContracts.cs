using System;
using System.Collections.Generic;
using System.IO;

public static class SpriteStreamingConfig {
  public const string CustomSpriteLibraryExtension = ".spriteSheetLib";
  public const string LegacySpriteLibraryExtension = ".spriteLib";
  public const string SourceRootFolder = "Packages/com.skaraman.myprojectcontent/Core/Sprites/SpriteLibraries";
  public const string TextureSourceRootFolder = "Packages/com.skaraman.myprojectcontent/Core/Sprites";
  public const string EsperanzaExpressionAtlasSourceRoot = "Assets/Sprites/Characters/Esperanza/_Expressions";
  public const string EsperanzaExpressionAtlasSourcePathWithoutExtension = EsperanzaExpressionAtlasSourceRoot + "/Base/atlas_atlas";
  public const string GroupedAtlasBuildSurrogateRootFolder = "Assets/Generated/SpriteStreamingBuildSurrogates";
  public const string RuntimeIndexFolder = "Assets/Sprites/SpriteLibraries/RuntimeIndex";
  public const string ManifestAssetPath = "Assets/Sprites/SpriteLibraries/SpriteIndexManifest.bytes";
  public const string IncludeAssetPath = "Assets/Sprites/SpriteLibraries/SpriteStreamingInclude.asset";
  public const string SettingsAssetPath = "Assets/Resources/SpriteStreamingSettings.asset";

  public const string TextureAddressablesGroupName = "SpriteTextures";
  public const string IndexAddressablesGroupName = "SpriteRuntimeIndex";
  public const string DefaultManifestAddress = "SpriteRuntimeIndex/Manifest";
  public const string AtlasMetadataAddressablesLabel = "ss_atlas_metadata";

  public static string BuildEsperanzaExpressionAtlasSourcePath(string extension) {
    return BuildEsperanzaExpressionAtlasSourcePath("Base", extension);
  }

  public static string BuildEsperanzaExpressionAtlasSourcePath(string form, string extension) {
    var normalizedExtension = string.IsNullOrWhiteSpace(extension)
      ? ".png"
      : extension.Trim();

    if (!normalizedExtension.StartsWith(".", StringComparison.Ordinal)) {
      normalizedExtension = "." + normalizedExtension;
    }

    var normalizedForm = EsperanzaForms.ResolveFormKey(form);
    if (string.IsNullOrWhiteSpace(normalizedForm)) {
      normalizedForm = "Base";
    }

    return EsperanzaExpressionAtlasSourceRoot + "/" + normalizedForm + "/atlas_atlas" + normalizedExtension;
  }
}

public static class GeneratedAtlasBuildSurrogateUtility {
  const string ContentStageRootFolder = "Packages/com.skaraman.myprojectcontent";
  static readonly string[] MetadataExcludedFolderNames = Array.Empty<string>();
  static readonly HashSet<string> MetadataExcludedFolderNameSet = new(MetadataExcludedFolderNames, StringComparer.OrdinalIgnoreCase);

  public static string MetadataExcludedFolderSummary => string.Join(", ", MetadataExcludedFolderNames);

  public static string NormalizePath(string assetPath) {
    if (string.IsNullOrWhiteSpace(assetPath)) return "";
    return assetPath.Trim().Replace("\\", "/");
  }

  public static bool HasMetadataExcludedFolderInPath(string assetPathOrFolderPath) {
    if (MetadataExcludedFolderNameSet.Count <= 0) return false;
    var normalizedFolderPath = NormalizeFolderPath(assetPathOrFolderPath);
    if (string.IsNullOrWhiteSpace(normalizedFolderPath)) return false;

    var segments = normalizedFolderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
    for (var i = 0; i < segments.Length; i++) {
      if (MetadataExcludedFolderNameSet.Contains(segments[i])) return true;
    }

    return false;
  }

  public static bool CanAtlasPathUseMetadata(string atlasAssetPath) {
    var normalizedAtlasPath = NormalizePath(atlasAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedAtlasPath)) return false;
    return !HasMetadataExcludedFolderInPath(normalizedAtlasPath);
  }

  public static bool IsGroupedGearAtlasPath(string assetPath) {
    var normalizedAssetPath = NormalizePath(assetPath);
    return normalizedAssetPath.IndexOf("/GroupedGearAtlases/", StringComparison.OrdinalIgnoreCase) >= 0;
  }

  public static bool IsBuildSurrogatePath(string assetPath) {
    var normalizedAssetPath = NormalizePath(assetPath);
    var normalizedRoot = NormalizePath(SpriteStreamingConfig.GroupedAtlasBuildSurrogateRootFolder);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath) || string.IsNullOrWhiteSpace(normalizedRoot)) return false;
    return string.Equals(normalizedAssetPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
           normalizedAssetPath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase);
  }

  public static bool IsContentStagePath(string assetPath) {
    var normalizedAssetPath = NormalizePath(assetPath);
    var normalizedRoot = NormalizePath(ContentStageRootFolder);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath) || string.IsNullOrWhiteSpace(normalizedRoot)) return false;
    return string.Equals(normalizedAssetPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
           normalizedAssetPath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase);
  }

  public static bool ShouldUseImportedSpriteSubassets(string assetPath) {
    var normalizedAssetPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath)) return false;
    if (IsBuildSurrogatePath(normalizedAssetPath)) return false;
    return IsGroupedGearAtlasPath(normalizedAssetPath) && IsContentStagePath(normalizedAssetPath);
  }

  public static bool ShouldImportGroupedAtlasAsSingleSprite(string assetPath) {
    var normalizedAssetPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath)) return false;
    if (IsBuildSurrogatePath(normalizedAssetPath)) return true;
    return false;
  }

  public static bool TryBuildSurrogatePath(string sourceAtlasAssetPath, out string surrogateAtlasAssetPath) {
    surrogateAtlasAssetPath = "";
    var normalizedSourceAtlasAssetPath = NormalizePath(sourceAtlasAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedSourceAtlasAssetPath)) return false;
    if (!IsGroupedGearAtlasPath(normalizedSourceAtlasAssetPath)) return false;
    if (IsBuildSurrogatePath(normalizedSourceAtlasAssetPath)) {
      surrogateAtlasAssetPath = normalizedSourceAtlasAssetPath;
      return true;
    }

    var normalizedTextureRoot = NormalizePath(SpriteStreamingConfig.TextureSourceRootFolder);
    if (string.IsNullOrWhiteSpace(normalizedTextureRoot) ||
        !normalizedSourceAtlasAssetPath.StartsWith(normalizedTextureRoot + "/", StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    var relativePath = normalizedSourceAtlasAssetPath.Substring(normalizedTextureRoot.Length + 1);
    surrogateAtlasAssetPath = NormalizePath(SpriteStreamingConfig.GroupedAtlasBuildSurrogateRootFolder + "/" + relativePath);
    return !string.IsNullOrWhiteSpace(surrogateAtlasAssetPath);
  }

  public static string BuildMetadataAssetPath(string atlasAssetPath) {
    var normalizedAtlasAssetPath = NormalizePath(atlasAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedAtlasAssetPath)) return "";
    return NormalizePath(Path.ChangeExtension(normalizedAtlasAssetPath, ".json"));
  }

  static string NormalizeFolderPath(string assetPathOrFolderPath) {
    var normalizedPath = NormalizePath(assetPathOrFolderPath).Trim('/');
    if (string.IsNullOrWhiteSpace(normalizedPath)) return "";

    var trailingSegment = Path.GetFileName(normalizedPath);
    if (!string.IsNullOrWhiteSpace(Path.GetExtension(trailingSegment))) {
      return NormalizePath(Path.GetDirectoryName(normalizedPath));
    }

    return normalizedPath;
  }
}
