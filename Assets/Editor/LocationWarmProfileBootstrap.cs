#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

[InitializeOnLoad]
public static class LocationWarmProfileBootstrap {
  const string ResourcesFolder = "Assets/Resources";
  const string RegistryAssetPath = "Assets/Resources/LocationWarmRegistry.asset";
  const string ProfileAssetPrefix = "LocationWarmProfile_";

  readonly struct LocationProfileBinding {
    public readonly string locationId;
    public readonly GameObject prefab;
    public readonly string assetPath;

    public LocationProfileBinding(string locationId, GameObject prefab, string assetPath) {
      this.locationId = locationId;
      this.prefab = prefab;
      this.assetPath = assetPath;
    }
  }

  static bool initialized;

  static LocationWarmProfileBootstrap() {
    if (initialized) return;
    initialized = true;
    EditorApplication.delayCall += () => EnsureLocationWarmAssets(logResult: false, saveAndRefresh: false);
  }

  [MenuItem("Tools/Sprite Streaming/2) Sync Location Profiles")]
  public static void InitializeLocationWarmProfilesMenu() {
    EnsureLocationWarmAssets(logResult: true, saveAndRefresh: true);
  }

  public static bool SyncLocationWarmAssets(bool logResult, bool saveAndRefresh) {
    return EnsureLocationWarmAssets(logResult, saveAndRefresh);
  }

  static bool EnsureLocationWarmAssets(bool logResult, bool saveAndRefresh) {
    EnsureFolderExists(ResourcesFolder);

    var changed = false;
    var bindings = CollectLocationProfileBindings();
    var profilesByLocationId = new Dictionary<string, LocationWarmProfile>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < bindings.Count; i++) {
      var binding = bindings[i];
      if (!EnsureLocationWarmProfile(binding, out var profile, out var profileChanged) || profile == null) {
        continue;
      }

      profilesByLocationId[binding.locationId] = profile;
      changed |= profileChanged;
    }

    var registry = AssetDatabase.LoadAssetAtPath<LocationWarmRegistry>(RegistryAssetPath);
    if (registry == null) {
      registry = ScriptableObject.CreateInstance<LocationWarmRegistry>();
      AssetDatabase.CreateAsset(registry, RegistryAssetPath);
      changed = true;
    }

    changed |= EnsureLocationWarmRegistryEntries(registry, profilesByLocationId);

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
        "profile_count=" + profilesByLocationId.Count +
        " default_profile='" + ResolveDefaultProfileLabel(profilesByLocationId) + "'" +
        " registry='" + RegistryAssetPath + "'" +
        " changed=" + changed + "."
      );
    }

    return changed;
  }

  static List<LocationProfileBinding> CollectLocationProfileBindings() {
    var bindings = new List<LocationProfileBinding>();
    foreach (var pair in LocationEnemyData.locations) {
      var locationId = LocationEnemyData.NormalizeLocationId(pair.Key);
      if (string.IsNullOrWhiteSpace(locationId)) continue;

      var prefab = ResolveLocationPrefab(pair.Value);
      if (prefab == null) continue;

      var assetPath = BuildLocationWarmProfileAssetPath(locationId);
      bindings.Add(new LocationProfileBinding(locationId, prefab, assetPath));
    }

    bindings.Sort((left, right) => string.Compare(left.locationId, right.locationId, StringComparison.OrdinalIgnoreCase));
    return bindings;
  }

  static GameObject ResolveLocationPrefab(LocationInfo locationInfo) {
    if (locationInfo?.locationPrefabData == null) return null;
    if (locationInfo.locationPrefabData.prefab != null) {
      return locationInfo.locationPrefabData.prefab;
    }

    var assetPath = NormalizeAssetPath(locationInfo.locationPrefabData.AssetPath);
    if (string.IsNullOrWhiteSpace(assetPath)) return null;
    return AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
  }

  static bool EnsureLocationWarmProfile(
    LocationProfileBinding binding,
    out LocationWarmProfile profile,
    out bool changed
  ) {
    profile = null;
    changed = false;
    if (string.IsNullOrWhiteSpace(binding.locationId) || string.IsNullOrWhiteSpace(binding.assetPath)) {
      return false;
    }

    profile = AssetDatabase.LoadAssetAtPath<LocationWarmProfile>(binding.assetPath);
    if (profile == null) {
      profile = ScriptableObject.CreateInstance<LocationWarmProfile>();
      AssetDatabase.CreateAsset(profile, binding.assetPath);
      changed = true;
    }

    if (profile == null) return false;

    var profileSo = new SerializedObject(profile);
    var locationIdProperty = profileSo.FindProperty("locationId");
    if (!string.Equals(locationIdProperty.stringValue, binding.locationId, StringComparison.Ordinal)) {
      locationIdProperty.stringValue = binding.locationId;
      changed = true;
    }

    var locationPrefabProperty = profileSo.FindProperty("locationPrefab");
    if (locationPrefabProperty.objectReferenceValue != binding.prefab) {
      locationPrefabProperty.objectReferenceValue = binding.prefab;
      changed = true;
    }

    if (profileSo.ApplyModifiedPropertiesWithoutUndo()) {
      changed = true;
      EditorUtility.SetDirty(profile);
    }

    return true;
  }

  static bool EnsureLocationWarmRegistryEntries(
    LocationWarmRegistry registry,
    Dictionary<string, LocationWarmProfile> profilesByLocationId
  ) {
    if (registry == null) return false;

    var changed = false;
    var registrySo = new SerializedObject(registry);
    var defaultProfileProperty = registrySo.FindProperty("defaultProfile");
    var defaultProfile = ResolveDefaultProfile(profilesByLocationId);
    if (defaultProfileProperty.objectReferenceValue != defaultProfile) {
      defaultProfileProperty.objectReferenceValue = defaultProfile;
      changed = true;
    }

    var locationsProperty = registrySo.FindProperty("locations");
    var orderedLocationIds = new List<string>(profilesByLocationId.Keys);
    orderedLocationIds.Sort(StringComparer.OrdinalIgnoreCase);
    if (locationsProperty.arraySize != orderedLocationIds.Count) {
      locationsProperty.arraySize = orderedLocationIds.Count;
      changed = true;
    }

    for (var i = 0; i < orderedLocationIds.Count; i++) {
      var locationId = orderedLocationIds[i];
      var entry = locationsProperty.GetArrayElementAtIndex(i);
      var entryLocationIdProperty = entry.FindPropertyRelative("locationId");
      var profileProperty = entry.FindPropertyRelative("profile");
      var profile = profilesByLocationId[locationId];

      if (!string.Equals(entryLocationIdProperty.stringValue, locationId, StringComparison.Ordinal)) {
        entryLocationIdProperty.stringValue = locationId;
        changed = true;
      }
      if (profileProperty.objectReferenceValue != profile) {
        profileProperty.objectReferenceValue = profile;
        changed = true;
      }
    }

    if (registrySo.ApplyModifiedPropertiesWithoutUndo()) {
      changed = true;
      EditorUtility.SetDirty(registry);
    }

    return changed;
  }

  static LocationWarmProfile ResolveDefaultProfile(Dictionary<string, LocationWarmProfile> profilesByLocationId) {
    if (profilesByLocationId == null || profilesByLocationId.Count <= 0) return null;

    var defaultLocationId = LocationEnemyData.GetDefaultLocation();
    if (!string.IsNullOrWhiteSpace(defaultLocationId) &&
        profilesByLocationId.TryGetValue(defaultLocationId, out var defaultProfile) &&
        defaultProfile != null) {
      return defaultProfile;
    }

    foreach (var pair in profilesByLocationId) {
      if (pair.Value != null) return pair.Value;
    }

    return null;
  }

  static string ResolveDefaultProfileLabel(Dictionary<string, LocationWarmProfile> profilesByLocationId) {
    var profile = ResolveDefaultProfile(profilesByLocationId);
    return profile != null ? profile.name : "-";
  }

  static string BuildLocationWarmProfileAssetPath(string locationId) {
    var sanitizedId = SanitizeFileToken(locationId);
    return ResourcesFolder + "/" + ProfileAssetPrefix + sanitizedId + ".asset";
  }

  static string SanitizeFileToken(string value) {
    var normalized = string.IsNullOrWhiteSpace(value) ? "Location" : value.Trim();
    var invalidChars = Path.GetInvalidFileNameChars();
    var chars = normalized.ToCharArray();
    for (var i = 0; i < chars.Length; i++) {
      if (Array.IndexOf(invalidChars, chars[i]) >= 0 || char.IsWhiteSpace(chars[i])) {
        chars[i] = '_';
      }
    }

    var sanitized = new string(chars).Trim('_');
    return string.IsNullOrWhiteSpace(sanitized) ? "Location" : sanitized;
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
