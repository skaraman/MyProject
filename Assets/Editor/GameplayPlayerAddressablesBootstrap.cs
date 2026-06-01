#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

[InitializeOnLoad]
public static class GameplayPlayerAddressablesBootstrap {
  static bool initialized;

  static GameplayPlayerAddressablesBootstrap() {
    if (initialized) return;
    initialized = true;
    EditorApplication.delayCall += () => EnsureGameplayPlayerAddressables(logResult: false, saveAndRefresh: false);
  }

  public static void SyncGameplayPlayerAddressablesMenu() {
    EnsureGameplayPlayerAddressables(logResult: true, saveAndRefresh: true);
  }

  public static bool SyncGameplayPlayerAddressables(bool logResult, bool saveAndRefresh) {
    return EnsureGameplayPlayerAddressables(logResult, saveAndRefresh);
  }

  static bool EnsureGameplayPlayerAddressables(bool logResult, bool saveAndRefresh) {
    var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
    if (settings == null) {
      if (logResult) {
        Debug.LogWarning("[GameplayPlayerAddressablesBootstrap] Addressables settings were not found while syncing the gameplay player prefab.");
      }
      return false;
    }

    var defaultGroup = settings.DefaultGroup;
    if (defaultGroup == null) {
      if (logResult) {
        Debug.LogWarning("[GameplayPlayerAddressablesBootstrap] Default Addressables group was not found while syncing the gameplay player prefab.");
      }
      return false;
    }

    var assetPath = ResolvePreferredPlayerPrefabPath();
    var changed = EnsureGameplayPlayerPrefabEntry(settings, defaultGroup, assetPath);

    if (changed && saveAndRefresh) {
      AssetDatabase.SaveAssets();
      AssetDatabase.Refresh();
    }

    if (logResult) {
      Debug.Log(
        "[GameplayPlayerAddressablesBootstrap] Synced gameplay player prefab Addressables entry." +
        " asset_path='" + assetPath + "'" +
        " changed=" + changed + "."
      );
    }

    return changed;
  }

  static string ResolvePreferredPlayerPrefabPath() {
    var resolvedAssetPath = NormalizeAssetPath(
      ActiveContentRegistryRuntime.ResolveCoreAssetPath(GameplayCoreAssetPaths.EsperanzaPrefabAssetPath)
    );
    if (!string.IsNullOrWhiteSpace(AssetDatabase.AssetPathToGUID(resolvedAssetPath))) {
      return resolvedAssetPath;
    }

    return NormalizeAssetPath(GameplayCoreAssetPaths.EsperanzaPrefabAssetPath);
  }

  static bool EnsureGameplayPlayerPrefabEntry(
    AddressableAssetSettings settings,
    AddressableAssetGroup defaultGroup,
    string assetPath
  ) {
    if (settings == null || defaultGroup == null || string.IsNullOrWhiteSpace(assetPath)) return false;

    var guid = AssetDatabase.AssetPathToGUID(assetPath);
    if (string.IsNullOrWhiteSpace(guid)) {
      Debug.LogWarning(
        "[GameplayPlayerAddressablesBootstrap] Gameplay player prefab asset was not found for path '" + assetPath + "'."
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

    if (!string.Equals(entry.address, assetPath, StringComparison.Ordinal)) {
      entry.SetAddress(assetPath, false);
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
