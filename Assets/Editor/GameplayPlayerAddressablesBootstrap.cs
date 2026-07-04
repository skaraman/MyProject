#if UNITY_EDITOR
using UnityEditor;
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
    if (!ContentPackPipeline.IsGameplayContentRequestedForConfiguredSelection()) {
      if (logResult) {
        Debug.Log("[GameplayPlayerAddressablesBootstrap] Skipped gameplay player Addressables sync. gameplay_content_requested=false.");
      }
      return false;
    }

    if (!RuntimePrefabAddressables.TryGetSettingsAndDefaultGroup(
          nameof(GameplayPlayerAddressablesBootstrap),
          logResult,
          out var settings,
          out var defaultGroup)) {
      return false;
    }

    var assetPath = ResolvePreferredPlayerPrefabPath();
    var changed = RuntimePrefabAddressables.EnsurePrefabEntry(
      settings,
      defaultGroup,
      assetPath,
      nameof(GameplayPlayerAddressablesBootstrap)
    );

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
    var resolvedAssetPath = RuntimePrefabAddressables.NormalizeAssetPath(
      ActiveContentRegistryRuntime.ResolveActiveContentAssetPath(GameplayCoreAssetPaths.EsperanzaPrefabAssetPath)
    );
    if (!string.IsNullOrWhiteSpace(AssetDatabase.AssetPathToGUID(resolvedAssetPath))) {
      return resolvedAssetPath;
    }

    return RuntimePrefabAddressables.NormalizeAssetPath(GameplayCoreAssetPaths.EsperanzaPrefabAssetPath);
  }
}
#endif
