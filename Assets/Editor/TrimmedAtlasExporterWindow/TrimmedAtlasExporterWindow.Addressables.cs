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
}
#endif
