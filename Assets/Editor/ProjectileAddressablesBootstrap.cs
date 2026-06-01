#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
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
    var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
    if (settings == null) {
      if (logResult) {
        Debug.LogWarning("[ProjectileAddressablesBootstrap] Addressables settings were not found while syncing projectile prefabs.");
      }
      return false;
    }

    var defaultGroup = settings.DefaultGroup;
    if (defaultGroup == null) {
      if (logResult) {
        Debug.LogWarning("[ProjectileAddressablesBootstrap] Default Addressables group was not found while syncing projectile prefabs.");
      }
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
      if (EnsureProjectileAddressableEntry(settings, defaultGroup, address)) {
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
      var normalizedResolvedAddress = NormalizeAssetPath(resolvedAddress);
      if (!string.IsNullOrWhiteSpace(AssetDatabase.AssetPathToGUID(normalizedResolvedAddress))) {
        return normalizedResolvedAddress;
      }
    }

    return NormalizeAssetPath(data.prefabAddress);
  }

  static bool EnsureProjectileAddressableEntry(
    AddressableAssetSettings settings,
    AddressableAssetGroup defaultGroup,
    string assetPath
  ) {
    if (settings == null || defaultGroup == null || string.IsNullOrWhiteSpace(assetPath)) return false;

    var normalizedAssetPath = NormalizeAssetPath(assetPath);
    var guid = AssetDatabase.AssetPathToGUID(normalizedAssetPath);
    if (string.IsNullOrWhiteSpace(guid)) {
      Debug.LogWarning(
        "[ProjectileAddressablesBootstrap] Projectile prefab asset was not found for path '" + normalizedAssetPath + "'."
      );
      return false;
    }

    var changed = false;
    var entry = settings.FindAssetEntry(guid);
    if (entry == null) {
      entry = settings.CreateOrMoveEntry(guid, defaultGroup, false, false);
      changed = entry != null;
    }

    if (entry == null) return changed;

    if (!string.Equals(entry.address, normalizedAssetPath, StringComparison.Ordinal)) {
      entry.SetAddress(normalizedAssetPath, false);
      changed = true;
    }

    if (changed) {
      if (entry.parentGroup != null) {
        EditorUtility.SetDirty(entry.parentGroup);
      }
      EditorUtility.SetDirty(settings);
    }

    return changed;
  }

  static string NormalizeAssetPath(string assetPath) {
    return string.IsNullOrWhiteSpace(assetPath) ? "" : assetPath.Replace("\\", "/").Trim();
  }
}
#endif
