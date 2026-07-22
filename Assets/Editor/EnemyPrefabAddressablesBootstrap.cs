#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class EnemyPrefabAddressablesBootstrap {
  const string EnemyPrefabRoot = "Assets/Prefabs/Enemies";

  static bool initialized;

  static EnemyPrefabAddressablesBootstrap() {
    if (initialized) return;
    initialized = true;
    EditorApplication.delayCall += () => EnsureEnemyPrefabAddressables(logResult: false, saveAndRefresh: false);
  }

  public static void SyncEnemyPrefabAddressablesMenu() {
    EnsureEnemyPrefabAddressables(logResult: true, saveAndRefresh: true);
  }

  public static bool SyncEnemyPrefabAddressables(bool logResult, bool saveAndRefresh) {
    return EnsureEnemyPrefabAddressables(logResult, saveAndRefresh);
  }

  static bool EnsureEnemyPrefabAddressables(bool logResult, bool saveAndRefresh) {
    if (!ContentPackPipeline.IsGameplayContentRequestedForConfiguredSelection()) {
      if (logResult) {
        Debug.Log("[EnemyPrefabAddressablesBootstrap] Skipped enemy prefab Addressables sync. gameplay_content_requested=false.");
      }
      return false;
    }

    if (!RuntimePrefabAddressables.TryGetSettingsAndDefaultGroup(
          nameof(EnemyPrefabAddressablesBootstrap),
          logResult,
          out var settings,
          out var defaultGroup)) {
      return false;
    }

    var changed = false;
    var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { EnemyPrefabRoot });
    for (var i = 0; i < prefabGuids.Length; i++) {
      var assetPath = RuntimePrefabAddressables.NormalizeAssetPath(
        AssetDatabase.GUIDToAssetPath(prefabGuids[i])
      );
      if (string.IsNullOrWhiteSpace(assetPath)) continue;

      if (RuntimePrefabAddressables.EnsurePrefabEntry(
            settings,
            defaultGroup,
            assetPath,
            nameof(EnemyPrefabAddressablesBootstrap))) {
        changed = true;
      }
    }

    if (changed && saveAndRefresh) {
      AssetDatabase.SaveAssets();
      AssetDatabase.Refresh();
    }

    if (logResult) {
      Debug.Log(
        "[EnemyPrefabAddressablesBootstrap] Synced enemy prefab Addressables entries." +
        " count=" + prefabGuids.Length +
        " changed=" + changed + "."
      );
    }

    return changed;
  }
}
#endif
