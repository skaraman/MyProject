#if UNITY_EDITOR
using UnityEditor;
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

  [MenuItem("Tools/Sprite Streaming/2) Sync Location Profiles (Prefab Bindings)")]
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
}
#endif
