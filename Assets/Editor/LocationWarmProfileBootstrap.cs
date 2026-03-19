#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

[InitializeOnLoad]
public static class LocationWarmProfileBootstrap {
  const string ResourcesFolder = "Assets/Resources";
  const string RegistryAssetPath = "Assets/Resources/LocationWarmRegistry.asset";
  const string DomeCityProfilePath = "Assets/Resources/LocationWarmProfile_DomeCity.asset";
  const string DomeCityPrefabPath = "Assets/Prefabs/Locations/DomeCity.prefab";

  static bool initialized;

  static LocationWarmProfileBootstrap() {
    if (initialized) return;
    initialized = true;
    EditorApplication.delayCall += () => EnsureLocationWarmAssets(logResult: false, saveAndRefresh: false);
  }

  [MenuItem("Tools/Sprite Streaming/Advanced/Sync Location Profiles")]
  public static void InitializeLocationWarmProfilesMenu() {
    EnsureLocationWarmAssets(logResult: true, saveAndRefresh: true);
  }

  public static bool SyncLocationWarmAssets(bool logResult, bool saveAndRefresh) {
    return EnsureLocationWarmAssets(logResult, saveAndRefresh);
  }

  static bool EnsureLocationWarmAssets(bool logResult, bool saveAndRefresh) {
    EnsureFolderExists(ResourcesFolder);

    var changed = false;
    var profile = AssetDatabase.LoadAssetAtPath<LocationWarmProfile>(DomeCityProfilePath);
    if (profile == null) {
      profile = ScriptableObject.CreateInstance<LocationWarmProfile>();
      AssetDatabase.CreateAsset(profile, DomeCityProfilePath);
      changed = true;
    }

    var profileSo = new SerializedObject(profile);
    var locationIdProperty = profileSo.FindProperty("locationId");
    if (locationIdProperty.stringValue != "DomeCity") {
      locationIdProperty.stringValue = "DomeCity";
      changed = true;
    }

    var domeCityPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DomeCityPrefabPath);
    if (domeCityPrefab != null) {
      var locationPrefabProperty = profileSo.FindProperty("locationPrefab");
      if (locationPrefabProperty.objectReferenceValue != domeCityPrefab) {
        locationPrefabProperty.objectReferenceValue = domeCityPrefab;
        changed = true;
      }
    }
    if (profileSo.ApplyModifiedPropertiesWithoutUndo()) {
      changed = true;
      EditorUtility.SetDirty(profile);
    }

    var registry = AssetDatabase.LoadAssetAtPath<LocationWarmRegistry>(RegistryAssetPath);
    if (registry == null) {
      registry = ScriptableObject.CreateInstance<LocationWarmRegistry>();
      AssetDatabase.CreateAsset(registry, RegistryAssetPath);
      changed = true;
    }

    var registrySo = new SerializedObject(registry);
    var defaultProfileProperty = registrySo.FindProperty("defaultProfile");
    if (defaultProfileProperty.objectReferenceValue != profile) {
      defaultProfileProperty.objectReferenceValue = profile;
      changed = true;
    }

    var locationsProperty = registrySo.FindProperty("locations");
    var hasDomeCity = false;
    for (var i = 0; i < locationsProperty.arraySize; i++) {
      var entry = locationsProperty.GetArrayElementAtIndex(i);
      var entryLocationIdProperty = entry.FindPropertyRelative("locationId");
      var profileProperty = entry.FindPropertyRelative("profile");
      if (!string.Equals(entryLocationIdProperty.stringValue, "DomeCity", System.StringComparison.OrdinalIgnoreCase)) continue;
      if (profileProperty.objectReferenceValue != profile) {
        profileProperty.objectReferenceValue = profile;
        changed = true;
      }
      hasDomeCity = true;
      break;
    }

    if (!hasDomeCity) {
      locationsProperty.arraySize++;
      var entry = locationsProperty.GetArrayElementAtIndex(locationsProperty.arraySize - 1);
      entry.FindPropertyRelative("locationId").stringValue = "DomeCity";
      entry.FindPropertyRelative("profile").objectReferenceValue = profile;
      changed = true;
    }

    if (registrySo.ApplyModifiedPropertiesWithoutUndo()) {
      changed = true;
      EditorUtility.SetDirty(registry);
    }

    if (EnsureLocationPrefabAddressables(logResult)) {
      changed = true;
    }

    if (changed && saveAndRefresh) {
      AssetDatabase.SaveAssets();
      AssetDatabase.Refresh();
    }

    if (logResult) {
      Debug.Log(
        "[LocationWarmProfileBootstrap] Synced location warm assets from prefab bindings: " +
        "profile='" + DomeCityProfilePath + "', registry='" + RegistryAssetPath + "'" +
        ", changed=" + changed + "."
      );
    }

    return changed;
  }

  static void EnsureFolderExists(string folderPath) {
    if (AssetDatabase.IsValidFolder(folderPath)) return;
    var normalized = folderPath.Replace("\\", "/");
    var segments = normalized.Split('/');
    var current = segments[0];
    for (var i = 1; i < segments.Length; i++) {
      var next = current + "/" + segments[i];
      if (!AssetDatabase.IsValidFolder(next)) {
        AssetDatabase.CreateFolder(current, segments[i]);
      }
      current = next;
    }
  }

  static bool EnsureLocationPrefabAddressables(bool logResult) {
    var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
    if (settings == null) {
      if (logResult) {
        Debug.LogWarning("[LocationWarmProfileBootstrap] Addressables settings were not found while syncing location prefabs.");
      }
      return false;
    }

    var defaultGroup = settings.DefaultGroup;
    if (defaultGroup == null) {
      if (logResult) {
        Debug.LogWarning("[LocationWarmProfileBootstrap] Default Addressables group was not found while syncing location prefabs.");
      }
      return false;
    }

    var changed = false;
    var syncedCount = 0;
    foreach (var pair in LocationEnemyData.locations) {
      var assetPath = NormalizeAssetPath(pair.Value?.locationPrefabData?.AssetPath);
      if (string.IsNullOrWhiteSpace(assetPath)) continue;
      syncedCount++;
      if (EnsureLocationPrefabAddressableEntry(settings, defaultGroup, assetPath)) {
        changed = true;
      }
    }

    if (logResult) {
      Debug.Log(
        "[LocationWarmProfileBootstrap] Synced location prefab Addressables entries. count=" + syncedCount +
        " changed=" + changed + "."
      );
    }

    return changed;
  }

  static bool EnsureLocationPrefabAddressableEntry(
    AddressableAssetSettings settings,
    AddressableAssetGroup defaultGroup,
    string assetPath
  ) {
    if (settings == null || defaultGroup == null || string.IsNullOrWhiteSpace(assetPath)) return false;

    var guid = AssetDatabase.AssetPathToGUID(assetPath);
    if (string.IsNullOrWhiteSpace(guid)) return false;

    var changed = false;
    var entry = settings.FindAssetEntry(guid);
    if (entry == null) {
      entry = settings.CreateOrMoveEntry(guid, defaultGroup, false, false);
      changed = entry != null;
    }

    if (entry == null) return changed;

    if (!string.Equals(entry.address, assetPath, System.StringComparison.Ordinal)) {
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
