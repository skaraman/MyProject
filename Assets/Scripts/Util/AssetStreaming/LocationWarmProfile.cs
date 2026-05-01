using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[Serializable]
public struct WarmPackContent {
  public List<string> spriteLibraries;
  public List<string> directAddresses;
  public List<string> addressableLabels;
  public List<string> assetAddresses;
  public List<string> assetLabels;
}

[Serializable]
public struct CombatPopulationWarmPackEntry {
  public string enemyType;
  public List<string> spriteLibraries;
  public List<string> directAddresses;
  public List<string> addressableLabels;
  public List<string> assetAddresses;
  public List<string> assetLabels;
}

[CreateAssetMenu(fileName = "LocationWarmProfile", menuName = "Sprite Streaming/Location Warm Profile")]
public class LocationWarmProfile : ScriptableObject {
  static readonly HashSet<string> autoPackDiagnosticsLoggedLocations = new(StringComparer.OrdinalIgnoreCase);
#if UNITY_EDITOR
  bool autoAssignLocationPrefabQueued;
#endif

  [SerializeField] string locationId = "DomeCity";
  [SerializeField] GameObject locationPrefab;
  // careful: these lists should only contain assets that are visible in the first few
  // seconds after the player spawns.  If designers flood them with unrelated content the
  // warm gate loses effectiveness and first‑play hitch risk rises.  An editor warning
  // will fire if a list grows unexpectedly large.
  [SerializeField] List<string> criticalSpriteLibraries = new();
  [SerializeField] List<string> criticalDirectAddresses = new();
  [SerializeField] List<string> criticalAddressableLabels = new();
  [SerializeField] List<string> criticalAssetAddresses = new();
  [SerializeField] List<string> criticalAssetLabels = new();
  [SerializeField] List<string> warmSpriteLibraries = new();
  [SerializeField] List<string> warmDirectAddresses = new();
  [SerializeField] List<string> warmAddressableLabels = new();
  [SerializeField] List<string> warmAssetAddresses = new();
  [SerializeField] List<string> warmAssetLabels = new();
  [SerializeField] List<string> warmUiDirectAddresses = new();
  [SerializeField] List<string> warmUiAddressableLabels = new();
  [SerializeField] List<string> warmUiAssetAddresses = new();
  [SerializeField] List<string> warmUiAssetLabels = new();
  [Header("Area Packs")]
  [SerializeField] WarmPackContent currentRoomPack;
  [SerializeField] WarmPackContent adjacentRoomPack;
  [SerializeField] List<CombatPopulationWarmPackEntry> combatPopulationPacks = new();

  public string LocationId => string.IsNullOrWhiteSpace(locationId) ? "" : locationId.Trim();
  public GameObject LocationPrefab => ResolveLocationPrefab();
  public IReadOnlyList<string> CriticalSpriteLibraries => criticalSpriteLibraries;
  public IReadOnlyList<string> CriticalDirectAddresses => criticalDirectAddresses;
  public IReadOnlyList<string> CriticalAddressableLabels => criticalAddressableLabels;
  public IReadOnlyList<string> CriticalAssetAddresses => criticalAssetAddresses;
  public IReadOnlyList<string> CriticalAssetLabels => criticalAssetLabels;
  public IReadOnlyList<string> WarmSpriteLibraries => warmSpriteLibraries;
  public IReadOnlyList<string> WarmDirectAddresses => warmDirectAddresses;
  public IReadOnlyList<string> WarmAddressableLabels => warmAddressableLabels;
  public IReadOnlyList<string> WarmAssetAddresses => warmAssetAddresses;
  public IReadOnlyList<string> WarmAssetLabels => warmAssetLabels;
  public IReadOnlyList<string> WarmUiDirectAddresses => warmUiDirectAddresses;
  public IReadOnlyList<string> WarmUiAddressableLabels => warmUiAddressableLabels;
  public IReadOnlyList<string> WarmUiAssetAddresses => warmUiAssetAddresses;
  public IReadOnlyList<string> WarmUiAssetLabels => warmUiAssetLabels;

  public void CollectEnvironmentCacheLists(
    List<string> outLibraries,
    List<string> outAddresses,
    List<string> outAssetAddresses = null,
    List<string> outAssetLabels = null
  ) {
    AddAutoDerivedLocationStageWarmLists(
      outLibraries,
      outAddresses,
      outLibraries,
      outAddresses
    );
    AddPackUnique(
      outLibraries,
      outAddresses,
      null,
      outAssetAddresses,
      outAssetLabels,
      currentRoomPack
    );
    AddPackUnique(
      outLibraries,
      outAddresses,
      null,
      outAssetAddresses,
      outAssetLabels,
      adjacentRoomPack
    );
    AddRangeUnique(outLibraries, criticalSpriteLibraries);
    AddRangeUnique(outAddresses, criticalDirectAddresses);
    AddRangeUnique(outAssetAddresses, criticalAssetAddresses);
    AddRangeUnique(outAssetLabels, criticalAssetLabels);
    AddRangeUnique(outLibraries, warmSpriteLibraries);
    AddRangeUnique(outAddresses, warmDirectAddresses);
    AddRangeUnique(outAssetAddresses, warmAssetAddresses);
    AddRangeUnique(outAssetLabels, warmAssetLabels);
  }

  public void CollectGameplayWarmLists(
    IEnumerable<string> combatEnemyTypes,
    List<string> outCriticalLibraries,
    List<string> outCriticalAddresses,
    List<string> outWarmLibraries,
    List<string> outWarmAddresses,
    List<string> outCriticalLabels = null,
    List<string> outWarmLabels = null,
    List<string> outCriticalAssetAddresses = null,
    List<string> outWarmAssetAddresses = null,
    List<string> outCriticalAssetLabels = null,
    List<string> outWarmAssetLabels = null
  ) {
    AddAutoDerivedLocationStageWarmLists(outCriticalLibraries, outCriticalAddresses, outWarmLibraries, outWarmAddresses);
    // Explicit area packs come first so their room/combat ordering survives duplicate
    // elimination. Legacy extra lists remain as a compatibility tail until profiles are
    // fully migrated onto current-room / adjacent-room / combat declarations.
    AddPackUnique(
      outCriticalLibraries,
      outCriticalAddresses,
      outCriticalLabels,
      outCriticalAssetAddresses,
      outCriticalAssetLabels,
      currentRoomPack
    );
    AddCombatPopulationPacks(
      combatEnemyTypes,
      outCriticalLibraries,
      outCriticalAddresses,
      outCriticalLabels,
      outCriticalAssetAddresses,
      outCriticalAssetLabels
    );
    AddPackUnique(
      outWarmLibraries,
      outWarmAddresses,
      outWarmLabels,
      outWarmAssetAddresses,
      outWarmAssetLabels,
      adjacentRoomPack
    );
    CollectExtraWarmLists(
      outCriticalLibraries,
      outCriticalAddresses,
      outWarmLibraries,
      outWarmAddresses,
      outCriticalLabels,
      outWarmLabels,
      outCriticalAssetAddresses,
      outWarmAssetAddresses,
      outCriticalAssetLabels,
      outWarmAssetLabels
    );
  }

  public void CollectExtraWarmLists(
    List<string> outCriticalLibraries,
    List<string> outCriticalAddresses,
    List<string> outWarmLibraries,
    List<string> outWarmAddresses,
    List<string> outCriticalLabels = null,
    List<string> outWarmLabels = null,
    List<string> outCriticalAssetAddresses = null,
    List<string> outWarmAssetAddresses = null,
    List<string> outCriticalAssetLabels = null,
    List<string> outWarmAssetLabels = null
  ) {
    // priority order is intentionally explicit: critical entries must come before warm
    // entries, which come before UI warm.  AddRangeUnique respects that order because
    // duplicates are ignored and earlier groups are emitted first, keeping batching
    // deterministic for the orchestrator.
    AddRangeUnique(outCriticalLibraries, criticalSpriteLibraries);
    AddRangeUnique(outCriticalAddresses, criticalDirectAddresses);
    AddRangeUnique(outCriticalLabels, criticalAddressableLabels);
    AddRangeUnique(outCriticalAssetAddresses, criticalAssetAddresses);
    AddRangeUnique(outCriticalAssetLabels, criticalAssetLabels);
    AddRangeUnique(outWarmLibraries, warmSpriteLibraries);
    AddRangeUnique(outWarmAddresses, warmDirectAddresses);
    AddRangeUnique(outWarmLabels, warmAddressableLabels);
    AddRangeUnique(outWarmAssetAddresses, warmAssetAddresses);
    AddRangeUnique(outWarmAssetLabels, warmAssetLabels);
    AddRangeUnique(outWarmAddresses, warmUiDirectAddresses);
    AddRangeUnique(outWarmLabels, warmUiAddressableLabels);
    AddRangeUnique(outWarmAssetAddresses, warmUiAssetAddresses);
    AddRangeUnique(outWarmAssetLabels, warmUiAssetLabels);
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

  void AddAutoDerivedLocationStageWarmLists(
    List<string> outCriticalLibraries,
    List<string> outCriticalAddresses,
    List<string> outWarmLibraries,
    List<string> outWarmAddresses
  ) {
    var prefab = ResolveLocationPrefab();
    if (prefab == null) return;

    var criticalLibraryStart = outCriticalLibraries != null ? outCriticalLibraries.Count : 0;
    var criticalAddressStart = outCriticalAddresses != null ? outCriticalAddresses.Count : 0;
    var warmLibraryStart = outWarmLibraries != null ? outWarmLibraries.Count : 0;
    var warmAddressStart = outWarmAddresses != null ? outWarmAddresses.Count : 0;
    var root = prefab.transform;
    var bg = FindDirectChild(root, "BG");
    var fg = FindDirectChild(root, "FG");
    var bgTargetCount = 0;
    var fgStaticTargetCount = 0;
    var fgDynamicTargetCount = 0;
    var fgDestructTargetCount = 0;
    var fallbackTargetCount = 0;
    CollectLocationStageWarmTargets(bg, outCriticalLibraries, outCriticalAddresses, ref bgTargetCount);
    CollectLocationStageWarmTargets(FindDirectChild(fg, "Static"), outCriticalLibraries, outCriticalAddresses, ref fgStaticTargetCount);
    CollectLocationStageWarmTargets(FindDirectChild(fg, "Dynamic"), outWarmLibraries, outWarmAddresses, ref fgDynamicTargetCount);
    CollectLocationStageWarmTargets(FindDirectChild(fg, "Destruct"), outWarmLibraries, outWarmAddresses, ref fgDestructTargetCount);
    var criticalTargetCount = bgTargetCount + fgStaticTargetCount;
    var warmTargetCount = fgDynamicTargetCount + fgDestructTargetCount;

    var usedFallback = criticalTargetCount <= 0 && warmTargetCount <= 0;
    if (usedFallback) {
      CollectLocationStageWarmTargets(root, outCriticalLibraries, outCriticalAddresses, ref fallbackTargetCount);
      criticalTargetCount = fallbackTargetCount;
    }

    MaybeLogAutoDerivedStageWarmLists(
      usedFallback,
      bgTargetCount,
      fgStaticTargetCount,
      fgDynamicTargetCount,
      fgDestructTargetCount,
      fallbackTargetCount,
      criticalTargetCount,
      warmTargetCount,
      criticalLibrariesAdded: (outCriticalLibraries != null ? outCriticalLibraries.Count : 0) - criticalLibraryStart,
      criticalAddressesAdded: (outCriticalAddresses != null ? outCriticalAddresses.Count : 0) - criticalAddressStart,
      warmLibrariesAdded: (outWarmLibraries != null ? outWarmLibraries.Count : 0) - warmLibraryStart,
      warmAddressesAdded: (outWarmAddresses != null ? outWarmAddresses.Count : 0) - warmAddressStart
    );
  }

  void MaybeLogAutoDerivedStageWarmLists(
    bool usedFallback,
    int bgTargetCount,
    int fgStaticTargetCount,
    int fgDynamicTargetCount,
    int fgDestructTargetCount,
    int fallbackTargetCount,
    int criticalTargetCount,
    int warmTargetCount,
    int criticalLibrariesAdded,
    int criticalAddressesAdded,
    int warmLibrariesAdded,
    int warmAddressesAdded
  ) {
    if (!ShouldLogAutoPackDiagnostics()) return;
    var locationKey = string.IsNullOrWhiteSpace(LocationId) ? name : LocationId;
    if (string.IsNullOrWhiteSpace(locationKey)) locationKey = name;
    if (!autoPackDiagnosticsLoggedLocations.Add(locationKey)) return;

    Debug.Log(
      "[LocationWarmProfile][AutoPack] location='" + locationKey + "'" +
      " mode=" + (usedFallback ? "full_prefab_fallback" : "stage_derived") +
      " bg_targets=" + Math.Max(bgTargetCount, 0) +
      " fg_static_targets=" + Math.Max(fgStaticTargetCount, 0) +
      " fg_dynamic_targets=" + Math.Max(fgDynamicTargetCount, 0) +
      " fg_destruct_targets=" + Math.Max(fgDestructTargetCount, 0) +
      " fallback_targets=" + Math.Max(fallbackTargetCount, 0) +
      " critical_targets=" + Math.Max(criticalTargetCount, 0) +
      " warm_targets=" + Math.Max(warmTargetCount, 0) +
      " critical_libraries=" + Math.Max(criticalLibrariesAdded, 0) +
      " critical_addresses=" + Math.Max(criticalAddressesAdded, 0) +
      " warm_libraries=" + Math.Max(warmLibrariesAdded, 0) +
      " warm_addresses=" + Math.Max(warmAddressesAdded, 0)
    );
  }

  static bool ShouldLogAutoPackDiagnostics() {
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    if (!SpriteStreamingRuntimeSettings.EnableDiagnostics) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }

  static void CollectLocationStageWarmTargets(
    Transform root,
    List<string> outLibraries,
    List<string> outAddresses,
    ref int targetCount
  ) {
    if (root == null) return;

    var targets = root.GetComponentsInChildren<SpriteWithNormals>(true);
    for (var i = 0; i < targets.Length; i++) {
      var target = targets[i];
      if (!IsPrefabWarmable(target)) continue;

      targetCount++;
      AddUniqueValue(outLibraries, target.libraryName);

      var lookupFrame = target.IsAnimation ? 1 : 0;
      var categoryOverride = target.IsAnimation ? target.category : null;
      if (!target.TryGetFrameAddressPair(lookupFrame, out var pair, categoryOverride)) continue;
      AddUniqueValue(outAddresses, pair.StreamingColorAddress);
      AddUniqueValue(outAddresses, pair.StreamingNormalAddress);
    }
  }

  static Transform FindDirectChild(Transform parent, string childName) {
    if (parent == null || string.IsNullOrWhiteSpace(childName)) return null;
    for (var i = 0; i < parent.childCount; i++) {
      var child = parent.GetChild(i);
      if (child == null) continue;
      if (string.Equals(child.name, childName, StringComparison.OrdinalIgnoreCase)) return child;
    }
    return null;
  }

  void AddCombatPopulationPacks(
    IEnumerable<string> combatEnemyTypes,
    List<string> outLibraries,
    List<string> outAddresses,
    List<string> outLabels,
    List<string> outAssetAddresses,
    List<string> outAssetLabels
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
      AddRangeUnique(outAssetAddresses, pack.assetAddresses);
      AddRangeUnique(outAssetLabels, pack.assetLabels);
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
    List<string> outAssetAddresses,
    List<string> outAssetLabels,
    WarmPackContent pack
  ) {
    AddRangeUnique(outLibraries, pack.spriteLibraries);
    AddRangeUnique(outAddresses, pack.directAddresses);
    AddRangeUnique(outLabels, pack.addressableLabels);
    AddRangeUnique(outAssetAddresses, pack.assetAddresses);
    AddRangeUnique(outAssetLabels, pack.assetLabels);
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
    QueueAutoAssignLocationPrefabByName();
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
    CheckList(nameof(criticalAssetAddresses), criticalAssetAddresses);
    CheckList(nameof(criticalAssetLabels), criticalAssetLabels);
    CheckList(nameof(warmSpriteLibraries), warmSpriteLibraries);
    CheckList(nameof(warmDirectAddresses), warmDirectAddresses);
    CheckList(nameof(warmAddressableLabels), warmAddressableLabels);
    CheckList(nameof(warmAssetAddresses), warmAssetAddresses);
    CheckList(nameof(warmAssetLabels), warmAssetLabels);
    CheckList(nameof(warmUiDirectAddresses), warmUiDirectAddresses);
    CheckList(nameof(warmUiAddressableLabels), warmUiAddressableLabels);
    CheckList(nameof(warmUiAssetAddresses), warmUiAssetAddresses);
    CheckList(nameof(warmUiAssetLabels), warmUiAssetLabels);
  }

  void QueueAutoAssignLocationPrefabByName() {
    if (Application.isPlaying || autoAssignLocationPrefabQueued) return;
    autoAssignLocationPrefabQueued = true;
    EditorApplication.delayCall += HandleQueuedAutoAssignLocationPrefabByName;
  }

  void HandleQueuedAutoAssignLocationPrefabByName() {
    autoAssignLocationPrefabQueued = false;
    if (this == null) return;
    AutoAssignLocationPrefabByName();
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
      if (locationPrefab == candidate) return;
      locationPrefab = candidate;
      EditorUtility.SetDirty(this);
      return;
    }
  }
#endif
}
