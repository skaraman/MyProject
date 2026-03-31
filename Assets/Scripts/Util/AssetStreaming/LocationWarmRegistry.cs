using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct LocationWarmRegistryEntry {
  public string locationId;
  public LocationWarmProfile profile;
}

[CreateAssetMenu(fileName = "LocationWarmRegistry", menuName = "Sprite Streaming/Location Warm Registry")]
public class LocationWarmRegistry : ScriptableObject {
  [SerializeField] LocationWarmProfile defaultProfile;
  [SerializeField] List<LocationWarmRegistryEntry> locations = new();

  public LocationWarmProfile DefaultProfile => defaultProfile;

  public bool TryGetProfile(string locationId, out LocationWarmProfile profile) {
    profile = null;
    var normalized = Normalize(locationId);
    if (string.IsNullOrWhiteSpace(normalized)) return false;
    if (locations == null || locations.Count <= 0) return false;

    for (var i = 0; i < locations.Count; i++) {
      var entry = locations[i];
      var key = Normalize(entry.locationId);
      if (!string.Equals(key, normalized, StringComparison.OrdinalIgnoreCase)) continue;
      if (entry.profile == null) return false;
      profile = entry.profile;
      return true;
    }

    return false;
  }

  static string Normalize(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }
}

public static class LocationWarmRegistryRuntime {
  const string ResourcePath = "LocationWarmRegistry";
  static bool loaded;
  static LocationWarmRegistry registry;
  static LocationWarmProfile runtimeFallbackDefault;
  static readonly HashSet<string> missingLocationWarnings = new(StringComparer.OrdinalIgnoreCase);

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetOnDomainReload() {
    loaded = false;
    registry = null;
    runtimeFallbackDefault = null;
    missingLocationWarnings.Clear();
  }

  static LocationWarmRegistry Registry {
    get {
      if (loaded) return registry;
      loaded = true;
      registry = Resources.Load<LocationWarmRegistry>(ResourcePath);
      return registry;
    }
  }

  public static LocationWarmProfile ResolveForLocation(string locationId) {
    var normalized = Normalize(locationId);
    if (!string.IsNullOrWhiteSpace(normalized) &&
        ActiveContentRegistryRuntime.TryGetWarmProfile(normalized, out var externalProfile) &&
        externalProfile != null) {
      return externalProfile;
    }

    var asset = Registry;
    if (asset != null) {
      if (!string.IsNullOrWhiteSpace(normalized) && asset.TryGetProfile(normalized, out var mapped) && mapped != null) {
        return mapped;
      }
      if (asset.DefaultProfile != null) {
        if (!string.IsNullOrWhiteSpace(normalized)) {
          WarnMissingLocationOnce(normalized, "default profile");
        }
        return asset.DefaultProfile;
      }
    }

    if (runtimeFallbackDefault == null) {
      runtimeFallbackDefault = ScriptableObject.CreateInstance<LocationWarmProfile>();
      runtimeFallbackDefault.name = "LocationWarmProfile_RuntimeFallback_DomeCity";
      runtimeFallbackDefault.hideFlags = HideFlags.HideAndDontSave;
      // populate with any globally configured critical labels so we at least warm
      // something when an unknown location is played.  reflection is needed because
      // the lists are private; this is purely a best‑effort shim.
      try {
        var type = typeof(LocationWarmProfile);
        var labelsField = type.GetField("criticalAddressableLabels",
                                       System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (labelsField != null) {
          var list = labelsField.GetValue(runtimeFallbackDefault) as List<string>;
          if (list != null) {
            list.AddRange(SpriteStreamingRuntimeSettings.CriticalAddressableLabels ?? Array.Empty<string>());
          }
        }
        var addrsField = type.GetField("criticalDirectAddresses",
                                       System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (addrsField != null) {
          var list = addrsField.GetValue(runtimeFallbackDefault) as List<string>;
          if (list != null) {
            // no global direct addresses defined today, but leave placeholder
          }
        }
      } catch (Exception ) {
      }
    }

    if (!string.IsNullOrWhiteSpace(normalized)) {
      WarnMissingLocationOnce(normalized, "runtime fallback profile");
    }
    return runtimeFallbackDefault;
  }

  static string Normalize(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }

  static void WarnMissingLocationOnce(string locationId, string fallbackLabel) {
    if (!missingLocationWarnings.Add(locationId)) return;

  }
}
