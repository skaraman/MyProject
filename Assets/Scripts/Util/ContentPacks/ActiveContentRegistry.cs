using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(fileName = "ActiveContentRegistry", menuName = "Content Packs/Active Content Registry")]
public sealed class ActiveContentRegistry : ScriptableObject {
  [SerializeField] bool externalContentActive;
  [SerializeField] string defaultLocationId = "";
  [SerializeField] List<string> activePackIds = new();
  [SerializeField] List<string> stagedTextureRoots = new();
  [SerializeField] List<string> stagedSpriteLibraryRoots = new();
  [SerializeField] List<string> coreContentRoots = new();
  [SerializeField] List<LocationInfo> locations = new();
  [SerializeField] List<LocationDialogDefinition> dialogs = new();
  [SerializeField] List<LocationWarmRegistryEntry> warmProfiles = new();

  public bool ExternalContentActive => externalContentActive;
  public string DefaultLocationId => string.IsNullOrWhiteSpace(defaultLocationId) ? "" : defaultLocationId.Trim();
  public IReadOnlyList<string> ActivePackIds => activePackIds;
  public IReadOnlyList<string> StagedTextureRoots => stagedTextureRoots;
  public IReadOnlyList<string> StagedSpriteLibraryRoots => stagedSpriteLibraryRoots;
  public IReadOnlyList<string> CoreContentRoots => coreContentRoots;
  public IReadOnlyList<LocationInfo> Locations => locations;
  public IReadOnlyList<LocationDialogDefinition> Dialogs => dialogs;
  public IReadOnlyList<LocationWarmRegistryEntry> WarmProfiles => warmProfiles;

  public void Configure(
    bool externalContentActive,
    string defaultLocationId,
    IList<string> activePackIds,
    IList<string> stagedTextureRoots,
    IList<string> stagedSpriteLibraryRoots,
    IList<string> coreContentRoots,
    IList<LocationInfo> locations,
    IList<LocationDialogDefinition> dialogs,
    IList<LocationWarmRegistryEntry> warmProfiles
  ) {
    this.externalContentActive = externalContentActive;
    this.defaultLocationId = string.IsNullOrWhiteSpace(defaultLocationId) ? "" : defaultLocationId.Trim();
    CopyList(this.activePackIds, activePackIds, NormalizeToken);
    CopyList(this.stagedTextureRoots, stagedTextureRoots, NormalizeAssetPath);
    CopyList(this.stagedSpriteLibraryRoots, stagedSpriteLibraryRoots, NormalizeAssetPath);
    CopyList(this.coreContentRoots, coreContentRoots, NormalizeAssetPath);
    CopyLocations(this.locations, locations);
    CopyDialogs(this.dialogs, dialogs);
    CopyWarmProfiles(this.warmProfiles, warmProfiles);
  }

  public bool TryGetLocation(string locationId, out LocationInfo locationInfo) {
    locationInfo = null;
    var normalized = NormalizeToken(locationId);
    if (string.IsNullOrWhiteSpace(normalized) || locations == null || locations.Count <= 0) return false;

    for (var i = 0; i < locations.Count; i++) {
      var candidate = locations[i];
      if (candidate == null) continue;
      if (!string.Equals(NormalizeToken(candidate.id), normalized, StringComparison.OrdinalIgnoreCase)) continue;
      locationInfo = candidate;
      return true;
    }

    return false;
  }

  public bool TryGetDialog(string locationId, out LocationDialogDefinition locationDialog) {
    locationDialog = null;
    var normalized = NormalizeToken(locationId);
    if (string.IsNullOrWhiteSpace(normalized) || dialogs == null || dialogs.Count <= 0) return false;

    for (var i = 0; i < dialogs.Count; i++) {
      var candidate = dialogs[i];
      if (candidate == null) continue;
      if (!string.Equals(NormalizeToken(candidate.locationId), normalized, StringComparison.OrdinalIgnoreCase)) continue;
      locationDialog = candidate;
      return true;
    }

    return false;
  }

  public bool TryGetWarmProfile(string locationId, out LocationWarmProfile profile) {
    profile = null;
    var normalized = NormalizeToken(locationId);
    if (string.IsNullOrWhiteSpace(normalized) || warmProfiles == null || warmProfiles.Count <= 0) return false;

    for (var i = 0; i < warmProfiles.Count; i++) {
      var entry = warmProfiles[i];
      if (!string.Equals(NormalizeToken(entry.locationId), normalized, StringComparison.OrdinalIgnoreCase)) continue;
      if (entry.profile == null) return false;
      profile = entry.profile;
      return true;
    }

    return false;
  }

  static void CopyList(List<string> target, IList<string> source, Func<string, string> normalize) {
    target.Clear();
    if (source == null || source.Count <= 0) return;

    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < source.Count; i++) {
      var normalized = normalize != null ? normalize(source[i]) : source[i];
      if (string.IsNullOrWhiteSpace(normalized)) continue;
      if (!seen.Add(normalized)) continue;
      target.Add(normalized);
    }
  }

  static void CopyLocations(List<LocationInfo> target, IList<LocationInfo> source) {
    target.Clear();
    if (source == null || source.Count <= 0) return;

    for (var i = 0; i < source.Count; i++) {
      var clone = CloneLocation(source[i]);
      if (clone == null) continue;
      target.Add(clone);
    }
  }

  static void CopyDialogs(List<LocationDialogDefinition> target, IList<LocationDialogDefinition> source) {
    target.Clear();
    if (source == null || source.Count <= 0) return;

    for (var i = 0; i < source.Count; i++) {
      var clone = CloneDialog(source[i]);
      if (clone == null) continue;
      target.Add(clone);
    }
  }

  static void CopyWarmProfiles(List<LocationWarmRegistryEntry> target, IList<LocationWarmRegistryEntry> source) {
    target.Clear();
    if (source == null || source.Count <= 0) return;

    for (var i = 0; i < source.Count; i++) {
      var entry = source[i];
      var normalizedLocationId = NormalizeToken(entry.locationId);
      if (string.IsNullOrWhiteSpace(normalizedLocationId) || entry.profile == null) continue;
      target.Add(new LocationWarmRegistryEntry {
        locationId = normalizedLocationId,
        profile = entry.profile
      });
    }
  }

  public static LocationInfo CloneLocation(LocationInfo source) {
    if (source == null) return null;

    return new LocationInfo(
      id: NormalizeToken(source.id),
      name: source.name ?? "",
      enemies: CloneStringList(source.enemies),
      maxEnemies: source.maxEnemies,
      spawnInterval: source.spawnInterval,
      objectives: CloneObjectives(source.objectives),
      locationPrefabData: ClonePrefabData(source.locationPrefabData)
    );
  }

  public static LocationDialogDefinition CloneDialog(LocationDialogDefinition source) {
    if (source == null) return null;

    var clonedSpeakers = new DialogSpeakerDefinition[source.speakers != null ? source.speakers.Count : 0];
    for (var i = 0; i < clonedSpeakers.Length; i++) {
      clonedSpeakers[i] = CloneSpeaker(source.speakers[i]);
    }

    return new LocationDialogDefinition(NormalizeToken(source.locationId), clonedSpeakers);
  }

  static DialogSpeakerDefinition CloneSpeaker(DialogSpeakerDefinition source) {
    if (source == null) return null;

    var clonedLines = new GameplayDialogController.GameplayDialogNode[source.lines != null ? source.lines.Count : 0];
    for (var i = 0; i < clonedLines.Length; i++) {
      var line = source.lines[i];
      if (line == null) continue;
      clonedLines[i] = new GameplayDialogController.GameplayDialogNode {
        lineNumber = line.lineNumber,
        text = line.text ?? "",
        emotion = line.emotion ?? "",
        trigger = line.trigger ?? "",
        speakerId = line.speakerId ?? "",
        speakerName = line.speakerName ?? "",
        speaker = line.speaker,
        avatarForm = line.avatarForm ?? "",
        otherType = line.otherType,
        portraitLibraryName = line.portraitLibraryName ?? "",
        locationId = line.locationId ?? ""
      };
    }

    return new DialogSpeakerDefinition(
      speakerId: source.speakerId ?? "",
      speakerName: source.speakerName ?? "",
      portraitLibraryName: source.portraitLibraryName ?? "",
      speakerSide: source.speakerSide,
      lines: clonedLines
    );
  }

  static LocationPrefabData ClonePrefabData(LocationPrefabData source) {
    if (source == null) return new LocationPrefabData();

    return new LocationPrefabData(
      prefab: source.prefab,
      assetPath: NormalizeAssetPath(source.AssetPath),
      localPosition: source.localPosition,
      localEulerAngles: source.localEulerAngles,
      localScale: source.localScale
    );
  }

  static List<LocationObjective> CloneObjectives(List<LocationObjective> source) {
    var clone = new List<LocationObjective>();
    if (source == null || source.Count <= 0) return clone;

    for (var i = 0; i < source.Count; i++) {
      var objective = source[i];
      if (objective == null) continue;
      clone.Add(new LocationObjective(
        type: objective.type,
        description: objective.description ?? "",
        targetCount: objective.targetCount,
        targetSeconds: objective.targetSeconds
      ));
    }

    return clone;
  }

  static List<string> CloneStringList(List<string> source) {
    var clone = new List<string>();
    if (source == null || source.Count <= 0) return clone;

    for (var i = 0; i < source.Count; i++) {
      var normalized = NormalizeToken(source[i]);
      if (string.IsNullOrWhiteSpace(normalized)) continue;
      clone.Add(normalized);
    }

    return clone;
  }

  static string NormalizeToken(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }

  static string NormalizeAssetPath(string assetPath) {
    return string.IsNullOrWhiteSpace(assetPath)
      ? ""
      : assetPath.Trim().Replace('\\', '/');
  }
}

public static class ActiveContentRegistryRuntime {
  const string ResourcePath = "ActiveContentRegistry";
  const string CoreStageRootAssetPath = "Assets/ContentStage/Core";
  const string FormsStageRootAssetPath = "Assets/ContentStage/Forms";
  const string GearsStageRootAssetPath = "Assets/ContentStage/Gears";
  const string SlicesStageRootAssetPath = "Assets/ContentStage/Slices";
  static bool loaded;
  static ActiveContentRegistry registry;
  static int reloadVersion;

  public static int ReloadVersion => reloadVersion;
  public static ActiveContentRegistry Registry => LoadRegistry();

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetOnDomainReload() {
    ForceReload();
  }

  public static void ForceReload() {
    loaded = false;
    registry = null;
    reloadVersion++;
  }

  public static bool HasActiveExternalContent() {
    return Registry != null && Registry.ExternalContentActive;
  }

  public static string GetDefaultLocationId() {
    return Registry != null ? Registry.DefaultLocationId : "";
  }

  public static string GetDefaultLocation() {
    return GetDefaultLocationId();
  }

  public static bool TryGetLocation(string locationId, out LocationInfo locationInfo) {
    locationInfo = null;
    if (Registry == null || !Registry.TryGetLocation(locationId, out var resolved) || resolved == null) {
      return false;
    }

    locationInfo = ActiveContentRegistry.CloneLocation(resolved);
    return locationInfo != null;
  }

  public static bool TryGetDialog(string locationId, out LocationDialogDefinition locationDialog) {
    locationDialog = null;
    if (Registry == null || !Registry.TryGetDialog(locationId, out var resolved) || resolved == null) {
      return false;
    }

    locationDialog = ActiveContentRegistry.CloneDialog(resolved);
    return locationDialog != null;
  }

  public static bool TryGetWarmProfile(string locationId, out LocationWarmProfile profile) {
    profile = null;
    return Registry != null && Registry.TryGetWarmProfile(locationId, out profile);
  }

  public static IReadOnlyList<string> GetStagedTextureRoots() {
    return Registry != null ? Registry.StagedTextureRoots : Array.Empty<string>();
  }

  public static IReadOnlyList<string> GetStagedSpriteLibraryRoots() {
    return Registry != null ? Registry.StagedSpriteLibraryRoots : Array.Empty<string>();
  }

  public static string ResolveCoreAssetPath(string assetPath) {
    var normalizedAssetPath = NormalizeAssetPath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath) || !HasActiveExternalContent()) {
      return normalizedAssetPath;
    }

    if (string.Equals(normalizedAssetPath, CoreStageRootAssetPath, StringComparison.OrdinalIgnoreCase) ||
        normalizedAssetPath.StartsWith(CoreStageRootAssetPath + "/", StringComparison.OrdinalIgnoreCase)) {
      return normalizedAssetPath;
    }

    if (!normalizedAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) {
      return normalizedAssetPath;
    }

    return NormalizeAssetPath(
      CoreStageRootAssetPath + "/" + normalizedAssetPath.Substring("Assets/".Length)
    );
  }

  public static string ResolveActiveContentAssetPath(string assetPath) {
    var normalizedAssetPath = NormalizeAssetPath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath) || !HasActiveExternalContent()) {
      return normalizedAssetPath;
    }

    if (!normalizedAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) {
      return normalizedAssetPath;
    }

    if (IsAlreadyStagedAssetPath(normalizedAssetPath)) {
      return normalizedAssetPath;
    }

    var relativePath = normalizedAssetPath.Substring("Assets/".Length);
    var activePackIds = EnumerateRuntimeActivePackIds();

    for (var i = 0; i < activePackIds.Count; i++) {
      var packId = NormalizeAssetPath(activePackIds[i]);
      if (string.IsNullOrWhiteSpace(packId) || string.Equals(packId, "Core", StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      var stagedPath = BuildStageAssetPathForPack(packId, relativePath);
      if (AssetExistsAtPath(stagedPath)) {
        return stagedPath;
      }
    }

    return ResolveCoreAssetPath(normalizedAssetPath);
  }

  static List<string> EnumerateRuntimeActivePackIds() {
    var result = new List<string>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    var registryInstance = Registry;
    var registryPackIds = registryInstance != null ? registryInstance.ActivePackIds : Array.Empty<string>();
    for (var i = 0; i < registryPackIds.Count; i++) {
      var normalized = NormalizeAssetPath(registryPackIds[i]);
      if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized)) {
        continue;
      }

      result.Add(normalized);
    }

    return result;
  }

  static ActiveContentRegistry LoadRegistry() {
    if (loaded) return registry;
    loaded = true;
    registry = Resources.Load<ActiveContentRegistry>(ResourcePath);
    return registry;
  }

  static bool IsAlreadyStagedAssetPath(string assetPath) {
    return string.Equals(assetPath, CoreStageRootAssetPath, StringComparison.OrdinalIgnoreCase) ||
           assetPath.StartsWith(CoreStageRootAssetPath + "/", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(assetPath, FormsStageRootAssetPath, StringComparison.OrdinalIgnoreCase) ||
           assetPath.StartsWith(FormsStageRootAssetPath + "/", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(assetPath, GearsStageRootAssetPath, StringComparison.OrdinalIgnoreCase) ||
           assetPath.StartsWith(GearsStageRootAssetPath + "/", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(assetPath, SlicesStageRootAssetPath, StringComparison.OrdinalIgnoreCase) ||
           assetPath.StartsWith(SlicesStageRootAssetPath + "/", StringComparison.OrdinalIgnoreCase);
  }

  static string BuildStageAssetPathForPack(string packId, string relativePath) {
    if (string.IsNullOrWhiteSpace(packId) || string.IsNullOrWhiteSpace(relativePath)) return "";
    if (string.Equals(packId, "Core", StringComparison.OrdinalIgnoreCase)) {
      return NormalizeAssetPath(CoreStageRootAssetPath + "/" + relativePath);
    }

    var stageRoot = packId.StartsWith("Form_", StringComparison.OrdinalIgnoreCase)
      ? FormsStageRootAssetPath + "/" + packId
      : packId.StartsWith("Gear_", StringComparison.OrdinalIgnoreCase)
        ? GearsStageRootAssetPath + "/" + packId
        : SlicesStageRootAssetPath + "/" + packId;
    return NormalizeAssetPath(stageRoot + "/" + relativePath);
  }

  static bool AssetExistsAtPath(string assetPath) {
    if (string.IsNullOrWhiteSpace(assetPath)) return false;
#if UNITY_EDITOR
    return !string.IsNullOrWhiteSpace(AssetDatabase.AssetPathToGUID(assetPath));
#else
    return false;
#endif
  }

  static string NormalizeAssetPath(string assetPath) {
    return string.IsNullOrWhiteSpace(assetPath) ? "" : assetPath.Trim().Replace('\\', '/');
  }
}
