#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

[InitializeOnLoad]
public static class AudioAddressablesBootstrap {
  static bool initialized;

  static AudioAddressablesBootstrap() {
    if (initialized) {
      return;
    }

    initialized = true;
    EditorApplication.delayCall += EnsureAudioAddressables;
  }

  static void EnsureAudioAddressables() {
    if (!RuntimePrefabAddressables.TryGetSettingsAndDefaultGroup(
          nameof(AudioAddressablesBootstrap),
          true,
          out var settings,
          out var defaultGroup)) {
      return;
    }

    var changed = EnsureMusicEntries(settings, defaultGroup);
    if (EnsureSoundEffectEntries(settings, defaultGroup)) {
      changed = true;
    }

    if (changed) {
      AssetDatabase.SaveAssets();
    }
  }

  static bool EnsureMusicEntries(
    AddressableAssetSettings settings,
    AddressableAssetGroup defaultGroup
  ) {
    var manifestAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(MusicManifestCatalog.AssetPath);
    if (!MusicManifestCatalog.TryBuildPlaylists(manifestAsset, out var playlists)) {
      return false;
    }

    var addresses = new List<string>();
    foreach (var playlist in playlists.Values) {
      addresses.AddRange(playlist.tracks);
    }

    return EnsureEntries(settings, defaultGroup, addresses, "music track");
  }

  static bool EnsureSoundEffectEntries(
    AddressableAssetSettings settings,
    AddressableAssetGroup defaultGroup
  ) {
    var manifestAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(SoundEffectManifestCatalog.AssetPath);
    if (!SoundEffectManifestCatalog.TryBuildDefinitions(manifestAsset, out var definitions)) {
      return false;
    }

    var addresses = new List<string>();
    foreach (var definition in definitions.Values) {
      addresses.Add(definition.clipAddress);
    }

    return EnsureEntries(settings, defaultGroup, addresses, "sound effect");
  }

  static bool EnsureEntries(
    AddressableAssetSettings settings,
    AddressableAssetGroup defaultGroup,
    List<string> addresses,
    string assetLabel
  ) {
    var changed = false;
    for (var i = 0; i < addresses.Count; i++) {
      if (RuntimePrefabAddressables.EnsureAssetEntry(
            settings,
            defaultGroup,
            addresses[i],
            nameof(AudioAddressablesBootstrap),
            assetLabel)) {
        changed = true;
      }
    }

    return changed;
  }
}
#endif
