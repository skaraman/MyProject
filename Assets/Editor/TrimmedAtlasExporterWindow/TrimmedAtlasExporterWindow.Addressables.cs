#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

public sealed partial class TrimmedAtlasExporterWindow {
  internal static void EnsureMetadataAddressable(string metadataAssetPath, bool saveAssets = true) {
    var normalizedAssetPath = ResolveRuntimeMetadataAssetPath(metadataAssetPath);
    if (!IsRuntimeMetadataAssetPath(normalizedAssetPath)) return;

    var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
    if (settings == null) return;

    var group = settings.FindGroup(SpriteStreamingConfig.IndexAddressablesGroupName);
    if (group == null) {
      group = settings.CreateGroup(
        SpriteStreamingConfig.IndexAddressablesGroupName,
        false,
        false,
        false,
        null,
        typeof(BundledAssetGroupSchema),
        typeof(ContentUpdateGroupSchema)
      );
    }

    if (group == null) return;

    var guid = AssetDatabase.AssetPathToGUID(normalizedAssetPath);
    if (string.IsNullOrWhiteSpace(guid)) return;

    RemoveStaleEntriesWithAddress(settings, guid, normalizedAssetPath);
    var entry = settings.FindAssetEntry(guid);
    if (entry == null || entry.parentGroup != group) {
      entry = settings.CreateOrMoveEntry(guid, group, false, false);
    }

    if (entry == null) return;
    if (!string.Equals(entry.address, normalizedAssetPath, StringComparison.Ordinal)) {
      entry.SetAddress(normalizedAssetPath, false);
    }
    settings.AddLabel(SpriteStreamingConfig.AtlasMetadataAddressablesLabel, false);
    entry.SetLabel(SpriteStreamingConfig.AtlasMetadataAddressablesLabel, true, true, false);

    EditorUtility.SetDirty(group);
    EditorUtility.SetDirty(settings);
    if (saveAssets) {
      AssetDatabase.SaveAssets();
    }
  }

  static void RemoveStaleEntriesWithAddress(
    UnityEditor.AddressableAssets.Settings.AddressableAssetSettings settings,
    string activeGuid,
    string address
  ) {
    if (settings == null || string.IsNullOrWhiteSpace(activeGuid) || string.IsNullOrWhiteSpace(address)) return;

    var staleGuids = new System.Collections.Generic.List<string>();
    var groups = settings.groups;
    if (groups == null) return;

    for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++) {
      var candidateGroup = groups[groupIndex];
      if (candidateGroup == null || candidateGroup.entries == null) continue;

      foreach (var candidateEntry in candidateGroup.entries) {
        if (candidateEntry == null) continue;
        if (string.Equals(candidateEntry.guid, activeGuid, StringComparison.OrdinalIgnoreCase)) continue;
        if (!string.Equals(candidateEntry.address, address, StringComparison.Ordinal)) continue;
        staleGuids.Add(candidateEntry.guid);
      }
    }

    for (var i = 0; i < staleGuids.Count; i++) {
      settings.RemoveAssetEntry(staleGuids[i], false);
    }
  }
}
#endif
