#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class SpriteStreamingTextureImportPolicy {
  public static bool Apply(TextureImporter importer, bool forceMultipleSpriteImportMode) {
    if (importer == null) return false;

    var changed = false;

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

      if (!platformChanged) continue;
      importer.SetPlatformTextureSettings(settings);
      changed = true;
    }

    return changed;
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
