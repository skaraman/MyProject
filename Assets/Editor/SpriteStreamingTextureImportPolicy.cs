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
    var isPairedNormalAtlas = IsPairedNormalAtlasPath(importer.assetPath);

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

    if (isPairedNormalAtlas && ApplyPairedNormalMapDataPolicy(importer)) {
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

      if (isPairedNormalAtlas && settings.compressionQuality != 100) {
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

  public static bool ApplyPairedNormalMapDataPolicy(TextureImporter importer) {
    if (importer == null || !IsPairedNormalAtlasPath(importer.assetPath)) return false;

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
    if (!TryGetPairedColorAtlasPath(assetPath, out var pairedColorPath)) return false;
    return File.Exists(pairedColorPath);
  }

  public static bool TryGetPairedNormalAtlasPath(string colorAssetPath, out string normalAssetPath) {
    normalAssetPath = "";
    if (string.IsNullOrWhiteSpace(colorAssetPath) ||
        !string.Equals(Path.GetExtension(colorAssetPath), ".png", StringComparison.OrdinalIgnoreCase)) return false;

    var colorStem = Path.GetFileNameWithoutExtension(colorAssetPath);
    if (string.IsNullOrWhiteSpace(colorStem) || colorStem.EndsWith("N", StringComparison.Ordinal)) return false;

    normalAssetPath = colorAssetPath.Substring(0, colorAssetPath.Length - 4) + "N.png";
    return true;
  }

  public static bool TryGetPairedColorAtlasPath(string normalAssetPath, out string colorAssetPath) {
    colorAssetPath = "";
    if (string.IsNullOrWhiteSpace(normalAssetPath) ||
        !string.Equals(Path.GetExtension(normalAssetPath), ".png", StringComparison.OrdinalIgnoreCase)) return false;

    var normalStem = Path.GetFileNameWithoutExtension(normalAssetPath);
    if (string.IsNullOrWhiteSpace(normalStem) || !normalStem.EndsWith("N", StringComparison.Ordinal)) return false;

    colorAssetPath = normalAssetPath.Substring(0, normalAssetPath.Length - "N.png".Length) + ".png";
    return true;
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
