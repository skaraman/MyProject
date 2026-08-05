#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class SpriteStreamingTextureImportPolicy {
  const int AutomaticTextureFormat = -1;

  public static bool Apply(TextureImporter importer, bool forceMultipleSpriteImportMode) {
    if (importer == null) return false;

    var changed = false;
    var isPairedLightingDataAtlas = IsPairedLightingDataAtlasPath(importer.assetPath);

    if (importer.textureType != TextureImporterType.Sprite) {
      importer.textureType = TextureImporterType.Sprite;
      changed = true;
    }

    if (forceMultipleSpriteImportMode && importer.spriteImportMode != SpriteImportMode.Multiple) {
      importer.spriteImportMode = SpriteImportMode.Multiple;
      changed = true;
    }

    if (importer.mipmapEnabled) {
      importer.mipmapEnabled = false;
      changed = true;
    }

    if (importer.filterMode != FilterMode.Point) {
      importer.filterMode = FilterMode.Point;
      changed = true;
    }

    if (importer.crunchedCompression) {
      importer.crunchedCompression = false;
      changed = true;
    }

    if (isPairedLightingDataAtlas && ApplyPairedLightingDataPolicy(importer)) {
      changed = true;
    }

    var platformTargets = BuildPlatformTargetList();
    for (var i = 0; i < platformTargets.Count; i++) {
      var platformName = platformTargets[i];
      if (string.IsNullOrWhiteSpace(platformName)) continue;

      var settings = importer.GetPlatformTextureSettings(platformName);
      var platformChanged = false;

      if (!settings.overridden) {
        settings.overridden = true;
        platformChanged = true;
      }

      if (settings.crunchedCompression) {
        settings.crunchedCompression = false;
        platformChanged = true;
      }

      if (isPairedLightingDataAtlas && settings.compressionQuality != 100) {
        settings.compressionQuality = 100;
        platformChanged = true;
      }

      if (TryResetTransparentSpritePlatformFormat(importer, platformName, settings)) {
        platformChanged = true;
      }

      if (!platformChanged) continue;
      importer.SetPlatformTextureSettings(settings);
      changed = true;
    }

    return changed;
  }

  public static bool ApplyPairedLightingDataPolicy(TextureImporter importer) {
    if (importer == null || !IsPairedLightingDataAtlasPath(importer.assetPath)) return false;

    var changed = false;
    if (importer.sRGBTexture) {
      importer.sRGBTexture = false;
      changed = true;
    }

    if (importer.textureCompression != TextureImporterCompression.CompressedHQ) {
      importer.textureCompression = TextureImporterCompression.CompressedHQ;
      changed = true;
    }

    if (importer.compressionQuality != 100) {
      importer.compressionQuality = 100;
      changed = true;
    }

    return changed;
  }

  public static bool IsPairedNormalAtlasPath(string assetPath) {
    return TryGetPairedColorAtlasPath(assetPath, "N.png", out var pairedColorPath) &&
           AssetPathExists(pairedColorPath);
  }

  public static bool IsPairedSpecularAtlasPath(string assetPath) {
    return TryGetPairedColorAtlasPath(assetPath, "S.png", out var pairedColorPath) &&
           AssetPathExists(pairedColorPath);
  }

  public static bool IsPairedLightingDataAtlasPath(string assetPath) {
    return IsPairedNormalAtlasPath(assetPath) || IsPairedSpecularAtlasPath(assetPath);
  }

  public static bool TryGetPairedNormalAtlasPath(string colorAssetPath, out string normalAssetPath) {
    normalAssetPath = "";
    if (string.IsNullOrWhiteSpace(colorAssetPath) ||
        !string.Equals(Path.GetExtension(colorAssetPath), ".png", StringComparison.OrdinalIgnoreCase)) return false;

    var colorStem = Path.GetFileNameWithoutExtension(colorAssetPath);
    if (string.IsNullOrWhiteSpace(colorStem) || IsPairedLightingDataAtlasPath(colorAssetPath)) return false;

    normalAssetPath = colorAssetPath.Substring(0, colorAssetPath.Length - 4) + "N.png";
    return true;
  }

  public static bool TryGetPairedSpecularAtlasPath(string colorAssetPath, out string specularAssetPath) {
    specularAssetPath = "";
    if (string.IsNullOrWhiteSpace(colorAssetPath) ||
        !string.Equals(Path.GetExtension(colorAssetPath), ".png", StringComparison.OrdinalIgnoreCase)) return false;

    var colorStem = Path.GetFileNameWithoutExtension(colorAssetPath);
    if (string.IsNullOrWhiteSpace(colorStem) || IsPairedLightingDataAtlasPath(colorAssetPath)) return false;

    specularAssetPath = colorAssetPath.Substring(0, colorAssetPath.Length - 4) + "S.png";
    return true;
  }

  public static bool TryGetPairedColorAtlasPath(string lightingDataAssetPath, out string colorAssetPath) {
    if (TryGetPairedColorAtlasPath(lightingDataAssetPath, "N.png", out colorAssetPath)) return true;
    return TryGetPairedColorAtlasPath(lightingDataAssetPath, "S.png", out colorAssetPath);
  }

  static bool TryGetPairedColorAtlasPath(string lightingDataAssetPath, string suffix, out string colorAssetPath) {
    colorAssetPath = "";
    if (string.IsNullOrWhiteSpace(lightingDataAssetPath) ||
        string.IsNullOrWhiteSpace(suffix) ||
        !string.Equals(Path.GetExtension(lightingDataAssetPath), ".png", StringComparison.OrdinalIgnoreCase)) return false;

    var lightingDataStem = Path.GetFileNameWithoutExtension(lightingDataAssetPath);
    var suffixStem = Path.GetFileNameWithoutExtension(suffix);
    if (string.IsNullOrWhiteSpace(lightingDataStem) ||
        string.IsNullOrWhiteSpace(suffixStem) ||
        !lightingDataStem.EndsWith(suffixStem, StringComparison.Ordinal)) return false;

    colorAssetPath = lightingDataAssetPath.Substring(0, lightingDataAssetPath.Length - suffix.Length) + ".png";
    return true;
  }

  static bool AssetPathExists(string assetPath) {
    if (string.IsNullOrWhiteSpace(assetPath)) return false;
    if (File.Exists(assetPath)) return true;
    return AssetImporter.GetAtPath(assetPath) != null;
  }

  static bool TryResetTransparentSpritePlatformFormat(TextureImporter importer, string platformName, TextureImporterPlatformSettings settings) {
    if (importer == null) return false;
    if (settings == null) return false;
    if (!importer.alphaIsTransparency) return false;

    var serializedFormat = (int)settings.format;
    if (serializedFormat == AutomaticTextureFormat) return false;

    Debug.Log(
      "[SpriteStreamingTextureImportPolicy] Reset explicit platform format to Automatic for transparent sprite. path=" +
      importer.assetPath +
      " platform=" + platformName +
      " format=" + serializedFormat);

    settings.format = (TextureImporterFormat)AutomaticTextureFormat;
    return true;
  }

  static List<string> BuildPlatformTargetList() {
    var list = new List<string>();

    var activeTarget = EditorUserBuildSettings.activeBuildTarget;
    var activeName = BuildPipeline.GetBuildTargetName(activeTarget);
    if (!string.IsNullOrWhiteSpace(activeName)) list.Add(activeName);

    AddUnique(list, "Standalone");
    AddUnique(list, "Android");
    AddUnique(list, "iPhone");
    return list;
  }

  static void AddUnique(List<string> values, string value) {
    if (values == null || string.IsNullOrWhiteSpace(value)) return;
    for (var i = 0; i < values.Count; i++) {
      if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase)) return;
    }
    values.Add(value);
  }
}
#endif
