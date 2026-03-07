using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[Serializable]
public struct EnemyArchetypeProfileEntry {
  public string enemyType;
  public GameObject prefab;
}

[Serializable]
public struct WarmPackContent {
  public List<string> spriteLibraries;
  public List<string> directAddresses;
  public List<string> addressableLabels;
}

[Serializable]
public struct CombatPopulationWarmPackEntry {
  public string enemyType;
  public List<string> spriteLibraries;
  public List<string> directAddresses;
  public List<string> addressableLabels;
}

[CreateAssetMenu(fileName = "LocationWarmProfile", menuName = "Sprite Streaming/Location Warm Profile")]
public class LocationWarmProfile : ScriptableObject {
  [SerializeField] string locationId = "DomeCity";
  [SerializeField] GameObject locationPrefab;
  // careful: these lists should only contain assets that are visible in the first few
  // seconds after the player spawns.  If designers flood them with unrelated content the
  // warm gate loses effectiveness and first‑play hitch risk rises.  An editor warning
  // will fire if a list grows unexpectedly large.
  [SerializeField] List<string> criticalSpriteLibraries = new();
  [SerializeField] List<string> criticalDirectAddresses = new();
  [SerializeField] List<string> criticalAddressableLabels = new();
  [SerializeField] List<string> warmSpriteLibraries = new();
  [SerializeField] List<string> warmDirectAddresses = new();
  [SerializeField] List<string> warmAddressableLabels = new();
  [SerializeField] List<string> warmUiDirectAddresses = new();
  [SerializeField] List<string> warmUiAddressableLabels = new();
  [Header("Area Packs")]
  [SerializeField] WarmPackContent currentRoomPack;
  [SerializeField] WarmPackContent adjacentRoomPack;
  [SerializeField] List<CombatPopulationWarmPackEntry> combatPopulationPacks = new();
  [SerializeField] List<EnemyArchetypeProfileEntry> enemyArchetypes = new();

  public string LocationId => string.IsNullOrWhiteSpace(locationId) ? "" : locationId.Trim();
  public GameObject LocationPrefab => ResolveLocationPrefab();
  public IReadOnlyList<string> CriticalSpriteLibraries => criticalSpriteLibraries;
  public IReadOnlyList<string> CriticalDirectAddresses => criticalDirectAddresses;
  public IReadOnlyList<string> CriticalAddressableLabels => criticalAddressableLabels;
  public IReadOnlyList<string> WarmSpriteLibraries => warmSpriteLibraries;
  public IReadOnlyList<string> WarmDirectAddresses => warmDirectAddresses;
  public IReadOnlyList<string> WarmAddressableLabels => warmAddressableLabels;
  public IReadOnlyList<string> WarmUiDirectAddresses => warmUiDirectAddresses;
  public IReadOnlyList<string> WarmUiAddressableLabels => warmUiAddressableLabels;

  public Dictionary<string, GameObject> BuildEnemyArchetypePrefabMap() {
    var map = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
    if (enemyArchetypes == null || enemyArchetypes.Count <= 0) return map;

    for (var i = 0; i < enemyArchetypes.Count; i++) {
      var entry = enemyArchetypes[i];
      if (string.IsNullOrWhiteSpace(entry.enemyType) || entry.prefab == null) continue;
      map[entry.enemyType.Trim()] = entry.prefab;
    }
    return map;
  }

  public void CollectGameplayWarmLists(
    IEnumerable<string> combatEnemyTypes,
    List<string> outCriticalLibraries,
    List<string> outCriticalAddresses,
    List<string> outWarmLibraries,
    List<string> outWarmAddresses,
    List<string> outCriticalLabels = null,
    List<string> outWarmLabels = null
  ) {
    AddLocationPrefabWarmLists(outCriticalLibraries, outCriticalAddresses);
    // Explicit area packs come first so their room/combat ordering survives duplicate
    // elimination. Legacy extra lists remain as a compatibility tail until profiles are
    // fully migrated onto current-room / adjacent-room / combat declarations.
    AddPackUnique(outCriticalLibraries, outCriticalAddresses, outCriticalLabels, currentRoomPack);
    AddCombatPopulationPacks(combatEnemyTypes, outCriticalLibraries, outCriticalAddresses, outCriticalLabels);
    AddPackUnique(outWarmLibraries, outWarmAddresses, outWarmLabels, adjacentRoomPack);
    CollectExtraWarmLists(
      outCriticalLibraries,
      outCriticalAddresses,
      outWarmLibraries,
      outWarmAddresses,
      outCriticalLabels,
      outWarmLabels
    );
  }

  public void CollectExtraWarmLists(
    List<string> outCriticalLibraries,
    List<string> outCriticalAddresses,
    List<string> outWarmLibraries,
    List<string> outWarmAddresses,
    List<string> outCriticalLabels = null,
    List<string> outWarmLabels = null
  ) {
    // priority order is intentionally explicit: critical entries must come before warm
    // entries, which come before UI warm.  AddRangeUnique respects that order because
    // duplicates are ignored and earlier groups are emitted first, keeping batching
    // deterministic for the orchestrator.
    AddRangeUnique(outCriticalLibraries, criticalSpriteLibraries);
    AddRangeUnique(outCriticalAddresses, criticalDirectAddresses);
    AddRangeUnique(outCriticalLabels, criticalAddressableLabels);
    AddRangeUnique(outWarmLibraries, warmSpriteLibraries);
    AddRangeUnique(outWarmAddresses, warmDirectAddresses);
    AddRangeUnique(outWarmLabels, warmAddressableLabels);
    AddRangeUnique(outWarmAddresses, warmUiDirectAddresses);
    AddRangeUnique(outWarmLabels, warmUiAddressableLabels);
  }

  GameObject ResolveLocationPrefab() {
    if (locationPrefab != null) return locationPrefab;

    if (!string.IsNullOrWhiteSpace(LocationId) &&
        LocationEnemyData.TryGetLocation(LocationId, out var locationInfo) &&
        locationInfo != null &&
        locationInfo.locationPrefabData != null) {
      var resolved = locationInfo.locationPrefabData.ResolvePrefab();
      if (resolved != null) {
        locationPrefab = resolved;
        return locationPrefab;
      }
    }

    return null;
  }

  void AddLocationPrefabWarmLists(List<string> outLibraries, List<string> outAddresses) {
    var prefab = ResolveLocationPrefab();
    if (prefab == null) return;

    var targets = prefab.GetComponentsInChildren<SpriteWithNormals>(true);
    for (var i = 0; i < targets.Length; i++) {
      var target = targets[i];
      if (!IsPrefabWarmable(target)) continue;

      AddUniqueValue(outLibraries, target.libraryName);

      var lookupFrame = target.IsAnimation ? 1 : 0;
      var categoryOverride = target.IsAnimation ? target.category : null;
      if (!target.TryGetFrameAddressPair(lookupFrame, out var pair, categoryOverride)) continue;
      AddUniqueValue(outAddresses, pair.RuntimeColorAddress);
      AddUniqueValue(outAddresses, pair.RuntimeNormalAddress);
    }
  }

  void AddCombatPopulationPacks(
    IEnumerable<string> combatEnemyTypes,
    List<string> outLibraries,
    List<string> outAddresses,
    List<string> outLabels
  ) {
    if (combatPopulationPacks == null || combatPopulationPacks.Count <= 0) return;
    var normalizedEnemyTypes = BuildNormalizedEnemyTypeSet(combatEnemyTypes);
    for (var i = 0; i < combatPopulationPacks.Count; i++) {
      var pack = combatPopulationPacks[i];
      var normalizedEnemyType = NormalizeToken(pack.enemyType);
      if (!string.IsNullOrWhiteSpace(normalizedEnemyType) &&
          (normalizedEnemyTypes == null || !normalizedEnemyTypes.Contains(normalizedEnemyType))) {
        continue;
      }
      AddRangeUnique(outLibraries, pack.spriteLibraries);
      AddRangeUnique(outAddresses, pack.directAddresses);
      AddRangeUnique(outLabels, pack.addressableLabels);
    }
  }

  static HashSet<string> BuildNormalizedEnemyTypeSet(IEnumerable<string> enemyTypes) {
    if (enemyTypes == null) return null;
    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var enemyType in enemyTypes) {
      var normalized = NormalizeToken(enemyType);
      if (string.IsNullOrWhiteSpace(normalized)) continue;
      set.Add(normalized);
    }
    return set.Count > 0 ? set : null;
  }

  static void AddPackUnique(
    List<string> outLibraries,
    List<string> outAddresses,
    List<string> outLabels,
    WarmPackContent pack
  ) {
    AddRangeUnique(outLibraries, pack.spriteLibraries);
    AddRangeUnique(outAddresses, pack.directAddresses);
    AddRangeUnique(outLabels, pack.addressableLabels);
  }

  static void AddRangeUnique(List<string> output, List<string> input) {
    if (output == null || input == null || input.Count <= 0) return;
    for (var i = 0; i < input.Count; i++) {
      var value = string.IsNullOrWhiteSpace(input[i]) ? "" : input[i].Trim();
      if (string.IsNullOrWhiteSpace(value)) continue;
      var exists = false;
      for (var j = 0; j < output.Count; j++) {
        if (!string.Equals(output[j], value, StringComparison.OrdinalIgnoreCase)) continue;
        exists = true;
        break;
      }
      if (!exists) output.Add(value);
    }
  }

  static void AddUniqueValue(List<string> output, string value) {
    if (output == null || string.IsNullOrWhiteSpace(value)) return;
    var normalized = value.Trim();
    for (var i = 0; i < output.Count; i++) {
      if (string.Equals(output[i], normalized, StringComparison.OrdinalIgnoreCase)) return;
    }
    output.Add(normalized);
  }

  static bool IsPrefabWarmable(SpriteWithNormals target) {
    return target != null && target.enabled && !target.DoNotRender;
  }

  static string NormalizeToken(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }

#if UNITY_EDITOR
  void OnValidate() {
    AutoAssignLocationPrefabByName();
    const int kWarningThreshold = 64;
    void CheckList(string name, List<string> list) {
      if (list != null && list.Count > kWarningThreshold) {
        // TODO: warning body is empty — designers never see the alert when lists grow too large.
        // Add: Debug.LogWarning($"[LocationWarmProfile] '{name}' has {list.Count} entries (>{kWarningThreshold}). Oversized warm lists reduce gate effectiveness.", this);
      }
    }
    CheckList(nameof(criticalSpriteLibraries), criticalSpriteLibraries);
    CheckList(nameof(criticalDirectAddresses), criticalDirectAddresses);
    CheckList(nameof(criticalAddressableLabels), criticalAddressableLabels);
    CheckList(nameof(warmSpriteLibraries), warmSpriteLibraries);
    CheckList(nameof(warmDirectAddresses), warmDirectAddresses);
    CheckList(nameof(warmAddressableLabels), warmAddressableLabels);
    CheckList(nameof(warmUiDirectAddresses), warmUiDirectAddresses);
    CheckList(nameof(warmUiAddressableLabels), warmUiAddressableLabels);
  }

  void AutoAssignLocationPrefabByName() {
    var normalizedLocationId = LocationId;
    if (string.IsNullOrWhiteSpace(normalizedLocationId)) return;
    if (locationPrefab != null &&
        string.Equals(locationPrefab.name, normalizedLocationId, StringComparison.OrdinalIgnoreCase)) {
      return;
    }

    var guids = AssetDatabase.FindAssets(normalizedLocationId + " t:Prefab", new[] { "Assets/Prefabs/Locations" });
    for (var i = 0; i < guids.Length; i++) {
      var path = AssetDatabase.GUIDToAssetPath(guids[i]);
      if (string.IsNullOrWhiteSpace(path)) continue;
      var candidate = AssetDatabase.LoadAssetAtPath<GameObject>(path);
      if (candidate == null) continue;
      if (!string.Equals(candidate.name, normalizedLocationId, StringComparison.OrdinalIgnoreCase)) continue;
      locationPrefab = candidate;
      EditorUtility.SetDirty(this);
      return;
    }
  }
#endif
}
