#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

public static class RuntimePrefabAddressables {
  public static bool TryGetSettingsAndDefaultGroup(
    string logPrefix,
    bool logWarnings,
    out AddressableAssetSettings settings,
    out AddressableAssetGroup defaultGroup
  ) {
    settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
    defaultGroup = settings != null ? settings.DefaultGroup : null;

    if (settings == null) {
      if (logWarnings) Debug.LogWarning("[" + logPrefix + "] Addressables settings were not found while syncing runtime prefabs.");
      return false;
    }

    if (defaultGroup == null) {
      if (logWarnings) Debug.LogWarning("[" + logPrefix + "] Default Addressables group was not found while syncing runtime prefabs.");
      return false;
    }

    return true;
  }

  public static bool EnsurePrefabEntry(
    AddressableAssetSettings settings,
    AddressableAssetGroup defaultGroup,
    string assetPath,
    string logPrefix
  ) {
    return EnsureAssetEntry(settings, defaultGroup, assetPath, logPrefix, "runtime prefab");
  }

  public static bool EnsureAssetEntry(
    AddressableAssetSettings settings,
    AddressableAssetGroup defaultGroup,
    string assetPath,
    string logPrefix,
    string assetLabel
  ) {
    if (settings == null || defaultGroup == null || string.IsNullOrWhiteSpace(assetPath)) return false;

    var normalizedAssetPath = NormalizeAssetPath(assetPath);
    var guid = AssetDatabase.AssetPathToGUID(normalizedAssetPath);
    if (string.IsNullOrWhiteSpace(guid)) {
      var label = string.IsNullOrWhiteSpace(assetLabel) ? "runtime asset" : assetLabel.Trim();
      Debug.LogWarning("[" + logPrefix + "] " + label + " was not found for path '" + normalizedAssetPath + "'.");
      return false;
    }

    return EnsureAssetEntryByGuid(settings, defaultGroup, guid, normalizedAssetPath);
  }

  public static bool EnsureAssetEntryByGuid(
    AddressableAssetSettings settings,
    AddressableAssetGroup defaultGroup,
    string guid,
    string assetPath
  ) {
    if (settings == null || defaultGroup == null || string.IsNullOrWhiteSpace(guid) || string.IsNullOrWhiteSpace(assetPath)) {
      return false;
    }

    var normalizedGuid = guid.Trim();
    var normalizedAssetPath = NormalizeAssetPath(assetPath);
    var changed = RemoveStaleEntriesWithAddress(settings, normalizedGuid, normalizedAssetPath);
    var entry = settings.FindAssetEntry(normalizedGuid);
    if (entry == null) {
      entry = settings.CreateOrMoveEntry(normalizedGuid, defaultGroup, false, false);
      changed = entry != null;
    }

    if (entry == null) return changed;

    if (!string.Equals(entry.address, normalizedAssetPath, StringComparison.Ordinal)) {
      entry.SetAddress(normalizedAssetPath, false);
      changed = true;
    }

    if (changed) {
      if (entry.parentGroup != null) EditorUtility.SetDirty(entry.parentGroup);
      EditorUtility.SetDirty(settings);
    }

    return changed;
  }

  static bool RemoveStaleEntriesWithAddress(
    AddressableAssetSettings settings,
    string activeGuid,
    string assetPath
  ) {
    if (settings == null || string.IsNullOrWhiteSpace(activeGuid) || string.IsNullOrWhiteSpace(assetPath)) {
      return false;
    }

    var staleGuids = new List<string>();
    var groups = settings.groups;
    if (groups == null) return false;

    for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++) {
      var group = groups[groupIndex];
      if (group == null || group.entries == null) continue;

      foreach (var entry in group.entries) {
        if (entry == null ||
            string.Equals(entry.guid, activeGuid, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(entry.address, assetPath, StringComparison.Ordinal)) {
          continue;
        }

        staleGuids.Add(entry.guid);
      }
    }

    for (var i = 0; i < staleGuids.Count; i++) {
      settings.RemoveAssetEntry(staleGuids[i], false);
    }

    return staleGuids.Count > 0;
  }

  public static string NormalizeAssetPath(string assetPath) {
    return string.IsNullOrWhiteSpace(assetPath) ? "" : assetPath.Replace("\\", "/").Trim();
  }
}
#endif
