using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class SoundEffectManifestData {
  public List<SoundEffectManifestEntry> effects = new();
}

[Serializable]
public sealed class SoundEffectManifestEntry {
  public string id;
  public string clip;
  public float volume = 1f;
}

public sealed class SoundEffectDefinition {
  public string id;
  public string clipAddress;
  public float volume;
}

public static class SoundEffectManifestCatalog {
  public const string AssetPath = "Assets/SoundEffects/SoundEffectManifest.json";

  public static bool TryBuildDefinitions(
    TextAsset manifestAsset,
    out Dictionary<string, SoundEffectDefinition> definitions
  ) {
    definitions = new Dictionary<string, SoundEffectDefinition>(StringComparer.OrdinalIgnoreCase);
    if (manifestAsset == null) {
      Debug.LogError("[SoundEffectManifest] Manifest asset is missing.");
      return false;
    }

    SoundEffectManifestData manifest;
    try {
      manifest = JsonUtility.FromJson<SoundEffectManifestData>(manifestAsset.text);
    }
    catch (Exception exception) {
      Debug.LogError("[SoundEffectManifest] Invalid JSON. error='" + exception.Message + "'");
      return false;
    }

    if (manifest == null || manifest.effects == null) {
      Debug.LogError("[SoundEffectManifest] The effects array is missing.");
      return false;
    }

    for (var i = 0; i < manifest.effects.Count; i++) {
      var entry = manifest.effects[i];
      if (!TryBuildDefinition(entry, i, out var definition)) {
        return false;
      }

      if (definitions.ContainsKey(definition.id)) {
        Debug.LogError("[SoundEffectManifest] Duplicate effect id '" + definition.id + "'.");
        return false;
      }

      definitions.Add(definition.id, definition);
    }

    return true;
  }

  static bool TryBuildDefinition(
    SoundEffectManifestEntry entry,
    int index,
    out SoundEffectDefinition definition
  ) {
    definition = null;
    if (entry == null || string.IsNullOrWhiteSpace(entry.id)) {
      Debug.LogError("[SoundEffectManifest] Effect entry " + index + " has no id.");
      return false;
    }

    if (string.IsNullOrWhiteSpace(entry.clip)) {
      Debug.LogError("[SoundEffectManifest] Effect '" + entry.id + "' has no clip.");
      return false;
    }

    definition = new SoundEffectDefinition {
      id = entry.id.Trim(),
      clipAddress = entry.clip.Replace("\\", "/").Trim(),
      volume = Mathf.Clamp01(entry.volume)
    };
    return true;
  }
}
