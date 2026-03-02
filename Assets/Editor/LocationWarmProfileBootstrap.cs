#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class LocationWarmProfileBootstrap {
  const string ResourcesFolder = "Assets/Resources";
  const string RegistryAssetPath = "Assets/Resources/LocationWarmRegistry.asset";
  const string DomeCityProfilePath = "Assets/Resources/LocationWarmProfile_DomeCity.asset";

  static bool initialized;

  static LocationWarmProfileBootstrap() {
    if (initialized) return;
    initialized = true;
    EditorApplication.delayCall += () => EnsureLocationWarmAssets(logResult: false);
  }

  [MenuItem("Tools/Sprite Streaming/5) Initialize Location Profiles")]
  public static void InitializeLocationWarmProfilesMenu() {
    EnsureLocationWarmAssets(logResult: true);
  }

  static void EnsureLocationWarmAssets(bool logResult) {
    EnsureFolderExists(ResourcesFolder);

    var profile = AssetDatabase.LoadAssetAtPath<LocationWarmProfile>(DomeCityProfilePath);
    if (profile == null) {
      profile = ScriptableObject.CreateInstance<LocationWarmProfile>();
      AssetDatabase.CreateAsset(profile, DomeCityProfilePath);
    }

    var profileSo = new SerializedObject(profile);
    profileSo.FindProperty("locationId").stringValue = "DomeCity";
    profileSo.ApplyModifiedPropertiesWithoutUndo();
    EditorUtility.SetDirty(profile);

    var registry = AssetDatabase.LoadAssetAtPath<LocationWarmRegistry>(RegistryAssetPath);
    if (registry == null) {
      registry = ScriptableObject.CreateInstance<LocationWarmRegistry>();
      AssetDatabase.CreateAsset(registry, RegistryAssetPath);
    }

    var registrySo = new SerializedObject(registry);
    registrySo.FindProperty("defaultProfile").objectReferenceValue = profile;

    var locationsProperty = registrySo.FindProperty("locations");
    var hasDomeCity = false;
    for (var i = 0; i < locationsProperty.arraySize; i++) {
      var entry = locationsProperty.GetArrayElementAtIndex(i);
      var locationIdProperty = entry.FindPropertyRelative("locationId");
      var profileProperty = entry.FindPropertyRelative("profile");
      if (!string.Equals(locationIdProperty.stringValue, "DomeCity", System.StringComparison.OrdinalIgnoreCase)) continue;
      profileProperty.objectReferenceValue = profile;
      hasDomeCity = true;
      break;
    }

    if (!hasDomeCity) {
      locationsProperty.arraySize++;
      var entry = locationsProperty.GetArrayElementAtIndex(locationsProperty.arraySize - 1);
      entry.FindPropertyRelative("locationId").stringValue = "DomeCity";
      entry.FindPropertyRelative("profile").objectReferenceValue = profile;
    }

    registrySo.ApplyModifiedPropertiesWithoutUndo();
    EditorUtility.SetDirty(registry);

    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh();

    if (logResult) {
      Debug.Log(
        "[LocationWarmProfileBootstrap] Initialized location warm assets: " +
        "profile='" + DomeCityProfilePath + "', registry='" + RegistryAssetPath + "'."
      );
    }
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
