#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ProjectileAddressablesBootstrap {
  static bool initialized;

  static ProjectileAddressablesBootstrap() {
    if (initialized) return;
    initialized = true;
    EditorApplication.delayCall += () => EnsureProjectileAddressables(logResult: false, saveAndRefresh: false);
  }

  public static void SyncProjectileAddressablesMenu() {
    EnsureProjectileAddressables(logResult: true, saveAndRefresh: true);
  }

  public static bool SyncProjectileAddressables(bool logResult, bool saveAndRefresh) {
    return EnsureProjectileAddressables(logResult, saveAndRefresh);
  }

  static bool EnsureProjectileAddressables(bool logResult, bool saveAndRefresh) {
    if (!RuntimePrefabAddressables.TryGetSettingsAndDefaultGroup(
          nameof(ProjectileAddressablesBootstrap),
          logResult,
          out var settings,
          out var defaultGroup)) {
      return false;
    }

    var changed = false;
    var syncedCount = 0;
    foreach (var entry in Projectiles.EnumerateAll()) {
      var key = entry.Key;
      var data = entry.Value;
      var address = ResolvePreferredProjectilePrefabPath(data);
      if (string.IsNullOrWhiteSpace(key) || data == null || string.IsNullOrWhiteSpace(address)) {
        continue;
      }

      syncedCount++;
      if (RuntimePrefabAddressables.EnsurePrefabEntry(
            settings,
            defaultGroup,
            address,
            nameof(ProjectileAddressablesBootstrap))) {
        changed = true;
      }
    }

    if (changed && saveAndRefresh) {
      AssetDatabase.SaveAssets();
      AssetDatabase.Refresh();
    }

    if (logResult) {
      Debug.Log(
        "[ProjectileAddressablesBootstrap] Synced projectile prefab Addressables entries. count=" + syncedCount +
        " changed=" + changed + "."
      );
    }

    return changed;
  }

  static string ResolvePreferredProjectilePrefabPath(ProjectileData data) {
    if (data == null) return "";

    if (data.TryGetPrefabAddress(out var resolvedAddress)) {
      var normalizedResolvedAddress = RuntimePrefabAddressables.NormalizeAssetPath(resolvedAddress);
      if (!string.IsNullOrWhiteSpace(AssetDatabase.AssetPathToGUID(normalizedResolvedAddress))) {
        return normalizedResolvedAddress;
      }
    }

    return RuntimePrefabAddressables.NormalizeAssetPath(data.prefabAddress);
  }
}
#endif
