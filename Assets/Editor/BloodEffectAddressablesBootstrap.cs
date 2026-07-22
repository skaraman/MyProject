#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class BloodEffectAddressablesBootstrap {
  static bool initialized;

  static BloodEffectAddressablesBootstrap() {
    if (initialized) return;
    initialized = true;
    EditorApplication.delayCall += () => SyncBloodEffectAddressables(
      logResult: false,
      saveAndRefresh: false
    );
  }

  public static bool SyncBloodEffectAddressables(bool logResult, bool saveAndRefresh) {
    if (!RuntimePrefabAddressables.TryGetSettingsAndDefaultGroup(
          nameof(BloodEffectAddressablesBootstrap),
          logResult,
          out var settings,
          out var defaultGroup)) {
      return false;
    }

    var syncedCount = 0;
    var changed = EnsureEntries(
      HurtBloodSplatter.BloodSprayAssetPaths,
      settings,
      defaultGroup,
      ref syncedCount
    );
    changed |= EnsureEntries(
      HurtBloodSplatter.BloodPuddleAssetPaths,
      settings,
      defaultGroup,
      ref syncedCount
    );

    if (changed && saveAndRefresh) {
      AssetDatabase.SaveAssets();
      AssetDatabase.Refresh();
    }

    if (logResult) {
      Debug.Log(
        "[BloodEffectAddressablesBootstrap] Synced blood effect sprite Addressables entries." +
        " count=" + syncedCount +
        " changed=" + changed + "."
      );
    }

    return changed;
  }

  static bool EnsureEntries(
    IReadOnlyList<string> assetPaths,
    UnityEditor.AddressableAssets.Settings.AddressableAssetSettings settings,
    UnityEditor.AddressableAssets.Settings.AddressableAssetGroup defaultGroup,
    ref int syncedCount
  ) {
    var changed = false;
    for (var i = 0; i < assetPaths.Count; i++) {
      syncedCount++;
      if (RuntimePrefabAddressables.EnsureAssetEntry(
            settings,
            defaultGroup,
            assetPaths[i],
            nameof(BloodEffectAddressablesBootstrap),
            "blood effect sprite")) {
        changed = true;
      }
    }
    return changed;
  }
}
#endif
