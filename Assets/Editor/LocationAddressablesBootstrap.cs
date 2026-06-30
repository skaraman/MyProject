#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class LocationAddressablesBootstrap {
  static bool initialized;

  static LocationAddressablesBootstrap() {
    if (initialized) return;
    initialized = true;
    EditorApplication.delayCall += () => EnsureLocationAddressables(logResult: false, saveAndRefresh: false);
  }

  public static void SyncLocationAddressablesMenu() {
    EnsureLocationAddressables(logResult: true, saveAndRefresh: true);
  }

  public static bool SyncLocationAddressables(bool logResult, bool saveAndRefresh) {
    return EnsureLocationAddressables(logResult, saveAndRefresh);
  }

  static bool EnsureLocationAddressables(bool logResult, bool saveAndRefresh) {
    if (!RuntimePrefabAddressables.TryGetSettingsAndDefaultGroup(
          nameof(LocationAddressablesBootstrap),
          logResult,
          out var settings,
          out var defaultGroup)) {
      return false;
    }

    var registry = AssetDatabase.LoadAssetAtPath<ActiveContentRegistry>(ContentPackPipeline.ActiveRegistryAssetPath);
    IEnumerable<LocationInfo> locations = registry != null && registry.ExternalContentActive
      ? registry.Locations
      : LocationEnemyData.locations.Values;

    var changed = false;
    var syncedCount = 0;
    foreach (var location in locations) {
      var assetPath = RuntimePrefabAddressables.NormalizeAssetPath(
        location?.locationPrefabData != null ? location.locationPrefabData.AssetPath : ""
      );
      if (string.IsNullOrWhiteSpace(assetPath)) continue;

      syncedCount++;
      if (RuntimePrefabAddressables.EnsurePrefabEntry(
            settings,
            defaultGroup,
            assetPath,
            nameof(LocationAddressablesBootstrap))) {
        changed = true;
      }
    }

    if (changed && saveAndRefresh) {
      AssetDatabase.SaveAssets();
      AssetDatabase.Refresh();
    }

    if (logResult) {
      Debug.Log(
        "[LocationAddressablesBootstrap] Synced location prefab Addressables entries. count=" + syncedCount +
        " changed=" + changed + "."
      );
    }

    return changed;
  }
}
#endif
