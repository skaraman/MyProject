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
  [SerializeField] List<ContentSliceDefinition> slices = new();
  [SerializeField] List<ContentEpisodeDefinition> episodes = new();
  [SerializeField] List<ContentObjectiveDefinition> objectives = new();

  public bool ExternalContentActive => externalContentActive;
  public string DefaultLocationId => string.IsNullOrWhiteSpace(defaultLocationId) ? "" : defaultLocationId.Trim();
  public IReadOnlyList<string> ActivePackIds => activePackIds;
  public IReadOnlyList<string> StagedTextureRoots => stagedTextureRoots;
  public IReadOnlyList<string> StagedSpriteLibraryRoots => stagedSpriteLibraryRoots;
  public IReadOnlyList<string> CoreContentRoots => coreContentRoots;
  public IReadOnlyList<LocationInfo> Locations => locations;
  public IReadOnlyList<LocationDialogDefinition> Dialogs => dialogs;
  public IReadOnlyList<ContentSliceDefinition> Slices => slices;
  public IReadOnlyList<ContentEpisodeDefinition> Episodes => episodes;
  public IReadOnlyList<ContentObjectiveDefinition> Objectives => objectives;

  public void Configure(
    bool externalContentActive,
    string defaultLocationId,
    IList<string> activePackIds,
    IList<string> stagedTextureRoots,
    IList<string> stagedSpriteLibraryRoots,
    IList<string> coreContentRoots,
    IList<LocationInfo> locations,
    IList<LocationDialogDefinition> dialogs,
    IList<ContentSliceDefinition> slices,
    IList<ContentEpisodeDefinition> episodes,
    IList<ContentObjectiveDefinition> objectives
  ) {
    this.externalContentActive = externalContentActive;
    this.defaultLocationId = string.IsNullOrWhiteSpace(defaultLocationId) ? "" : defaultLocationId.Trim();
    CopyList(this.activePackIds, activePackIds, NormalizeToken);
    CopyList(this.stagedTextureRoots, stagedTextureRoots, NormalizeAssetPath);
    CopyList(this.stagedSpriteLibraryRoots, stagedSpriteLibraryRoots, NormalizeAssetPath);
    CopyList(this.coreContentRoots, coreContentRoots, NormalizeAssetPath);
    CopyLocations(this.locations, locations);
    CopyDialogs(this.dialogs, dialogs);
    CopySlices(this.slices, slices);
    CopyEpisodes(this.episodes, episodes);
    CopyObjectives(this.objectives, objectives);
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

  static void CopySlices(List<ContentSliceDefinition> target, IList<ContentSliceDefinition> source) {
    target.Clear();
    if (source == null || source.Count <= 0) return;

    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < source.Count; i++) {
      var clone = CloneSlice(source[i]);
      if (clone == null) continue;
      if (!seen.Add(clone.id)) continue;
      target.Add(clone);
    }
  }

  static void CopyEpisodes(List<ContentEpisodeDefinition> target, IList<ContentEpisodeDefinition> source) {
    target.Clear();
    if (source == null || source.Count <= 0) return;

    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < source.Count; i++) {
      var clone = CloneEpisode(source[i]);
      if (clone == null) continue;
      if (!seen.Add(clone.id)) continue;
      target.Add(clone);
    }
  }

  static void CopyObjectives(List<ContentObjectiveDefinition> target, IList<ContentObjectiveDefinition> source) {
    target.Clear();
    if (source == null || source.Count <= 0) return;

    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < source.Count; i++) {
      var clone = CloneObjective(source[i]);
      if (clone == null) continue;
      var key = clone.packId + "|" + clone.id + "|" + clone.objective;
      if (!seen.Add(key)) continue;
      target.Add(clone);
    }
  }

  public static ContentSliceDefinition CloneSlice(ContentSliceDefinition source) {
    if (source == null) return null;

    var id = NormalizeToken(source.id);
    if (string.IsNullOrWhiteSpace(id)) return null;

    return new ContentSliceDefinition(
      id,
      CloneStringList(source.ids)
    );
  }

  public static ContentEpisodeDefinition CloneEpisode(ContentEpisodeDefinition source) {
    if (source == null) return null;

    var id = NormalizeToken(source.id);
    if (string.IsNullOrWhiteSpace(id)) return null;

    return new ContentEpisodeDefinition(
      id,
      CloneStringList(source.slices)
    );
  }

  public static ContentObjectiveDefinition CloneObjective(ContentObjectiveDefinition source) {
    if (source == null) return null;

    var packId = NormalizeToken(source.packId);
    var id = NormalizeToken(source.id);
    if (string.IsNullOrWhiteSpace(packId) && string.IsNullOrWhiteSpace(id)) return null;

    return new ContentObjectiveDefinition(
      packId,
      id,
      source.title,
      source.description,
      NormalizeToken(source.objective),
      source.spawns,
      source.enemyLevels,
      source.respawns
    );
  }

  public static LocationInfo CloneLocation(LocationInfo source) {
    if (source == null) return null;

    return new LocationInfo(
      id: NormalizeToken(source.id),
      name: source.name ?? "",
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

[Serializable]
public sealed class ContentSliceDefinition {
  public string id;
  public List<string> ids = new();

  public ContentSliceDefinition() {
  }

  public ContentSliceDefinition(string id, IList<string> ids) {
    this.id = string.IsNullOrWhiteSpace(id) ? "" : id.Trim();
    CopyIds(this.ids, ids);
  }

  static void CopyIds(List<string> target, IList<string> source) {
    target.Clear();
    if (source == null) return;

    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < source.Count; i++) {
      var value = string.IsNullOrWhiteSpace(source[i]) ? "" : source[i].Trim();
      if (string.IsNullOrWhiteSpace(value)) continue;
      if (!seen.Add(value)) continue;
      target.Add(value);
    }
  }
}

[Serializable]
public sealed class ContentEpisodeDefinition {
  public string id;
  public List<string> slices = new();

  public ContentEpisodeDefinition() {
  }

  public ContentEpisodeDefinition(string id, IList<string> slices) {
    this.id = string.IsNullOrWhiteSpace(id) ? "" : id.Trim();
    CopySlices(this.slices, slices);
  }

  static void CopySlices(List<string> target, IList<string> source) {
    target.Clear();
    if (source == null) return;

    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < source.Count; i++) {
      var value = string.IsNullOrWhiteSpace(source[i]) ? "" : source[i].Trim();
      if (string.IsNullOrWhiteSpace(value)) continue;
      if (!seen.Add(value)) continue;
      target.Add(value);
    }
  }
}

[Serializable]
public sealed class ContentObjectiveDefinition {
  public string packId;
  public string id;
  public string title;
  public string description;
  public string objective;
  public List<string> spawns = new();
  public List<string> enemyLevels = new();
  public List<string> respawns = new();

  public ContentObjectiveDefinition() {
  }

  public ContentObjectiveDefinition(
    string packId,
    string id,
    string title,
    string description,
    string objective,
    IList<string> spawns,
    IList<string> enemyLevels,
    IList<string> respawns
  ) {
    this.packId = string.IsNullOrWhiteSpace(packId) ? "" : packId.Trim();
    this.id = string.IsNullOrWhiteSpace(id) ? "" : id.Trim();
    this.title = string.IsNullOrWhiteSpace(title) ? "" : title.Trim();
    this.description = string.IsNullOrWhiteSpace(description) ? "" : description.Trim();
    this.objective = string.IsNullOrWhiteSpace(objective) ? "" : objective.Trim();
    CopyRules(this.spawns, spawns);
    CopyRules(this.enemyLevels, enemyLevels);
    CopyRules(this.respawns, respawns);
  }

  static void CopyRules(List<string> target, IList<string> source) {
    target.Clear();
    if (source == null) return;

    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < source.Count; i++) {
      var rule = string.IsNullOrWhiteSpace(source[i]) ? "" : source[i].Trim();
      if (string.IsNullOrWhiteSpace(rule)) continue;
      if (!seen.Add(rule)) continue;
      target.Add(rule);
    }
  }
}

public static class ActiveContentRegistryRuntime {
  const string ResourcePath = "ActiveContentRegistry";
  const string StageRootAssetPath = "Packages/com.skaraman.myprojectcontent";
  const string CoreStageRootAssetPath = "Packages/com.skaraman.myprojectcontent/Core";
  const string FormsStageRootAssetPath = "Packages/com.skaraman.myprojectcontent/Forms";
  const string GearsStageRootAssetPath = "Packages/com.skaraman.myprojectcontent/Gears";
  const string SlicesStageRootAssetPath = "Packages/com.skaraman.myprojectcontent/Slices";
  const string EpisodesStageRootAssetPath = "Packages/com.skaraman.myprojectcontent/Episodes";
  static bool loaded;
  static ActiveContentRegistry registry;
  static int reloadVersion;
  static bool runtimeRequestedPackIdsConfigured;
  static readonly List<string> runtimeRequestedPackIds = new();

  public static int ReloadVersion => reloadVersion + ContentPackCatalogLoader.ReadyVersion;
  public static ActiveContentRegistry Registry => LoadRegistry();

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetOnDomainReload() {
    runtimeRequestedPackIdsConfigured = false;
    runtimeRequestedPackIds.Clear();
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

  public static void ConfigureRuntimeRequestedPackIds(IEnumerable<string> packIds, string source = "") {
    var nextPackIds = NormalizePackIds(packIds);
    if (ArePackIdListsEqual(runtimeRequestedPackIds, nextPackIds) && runtimeRequestedPackIdsConfigured) {
      ContentPackCatalogLoader.RequestLoadPacks(nextPackIds, source);
      return;
    }

    ContentPackCatalogLoader.RequestLoadPacks(nextPackIds, source);
    runtimeRequestedPackIdsConfigured = true;
    runtimeRequestedPackIds.Clear();
    runtimeRequestedPackIds.AddRange(nextPackIds);
    reloadVersion++;

    RuntimeLog.Log(
      "[ActiveContentRegistryRuntime] Runtime requested packs updated" +
      " source='" + (source ?? "") + "'" +
      " count=" + runtimeRequestedPackIds.Count +
      " packs=" + string.Join(", ", runtimeRequestedPackIds)
    );
  }

  public static void ClearRuntimeRequestedPackIds(string source = "") {
    if (!runtimeRequestedPackIdsConfigured && runtimeRequestedPackIds.Count <= 0) {
      return;
    }

    runtimeRequestedPackIdsConfigured = false;
    runtimeRequestedPackIds.Clear();
    reloadVersion++;

    RuntimeLog.Log(
      "[ActiveContentRegistryRuntime] Runtime requested packs cleared" +
      " source='" + (source ?? "") + "'"
    );
  }

  public static IReadOnlyList<string> GetRuntimeActivePackIds() {
    return EnumerateRuntimeActivePackIds();
  }

  public static IReadOnlyList<string> GetAvailablePackIds() {
    var result = EnumerateRegistryPackIds();
    var seen = new HashSet<string>(result, StringComparer.OrdinalIgnoreCase);
    var manifestPackIds = ContentPackCatalogLoader.GetAvailablePackIds();
    for (var i = 0; i < manifestPackIds.Count; i++) {
      var normalized = NormalizePackId(manifestPackIds[i]);
      if (string.IsNullOrWhiteSpace(normalized)) {
        continue;
      }

      if (!seen.Add(normalized)) {
        continue;
      }

      result.Add(normalized);
    }

    return result;
  }

  public static bool IsPackAvailable(string packId) {
    var normalizedPackId = NormalizePackId(packId);
    if (string.IsNullOrWhiteSpace(normalizedPackId)) {
      return false;
    }

    if (ContentPackCatalogLoader.IsPackAvailable(normalizedPackId)) {
      return true;
    }

    var availablePackIds = EnumerateRegistryPackIds();
    for (var i = 0; i < availablePackIds.Count; i++) {
      if (string.Equals(availablePackIds[i], normalizedPackId, StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
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

  public static IReadOnlyList<string> GetStagedTextureRoots() {
    return FilterStagedRootsByRuntimePacks(Registry != null ? Registry.StagedTextureRoots : Array.Empty<string>());
  }

  public static IReadOnlyList<string> GetStagedSpriteLibraryRoots() {
    return FilterStagedRootsByRuntimePacks(Registry != null ? Registry.StagedSpriteLibraryRoots : Array.Empty<string>());
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

    var stagedCoreAssetPath = NormalizeAssetPath(
      CoreStageRootAssetPath + "/" + normalizedAssetPath.Substring("Assets/".Length)
    );

    if (ContentPackCatalogLoader.TryResolveExportedAddress(
      normalizedAssetPath,
      new[] { "Core" },
      out var coreAddress
    )) {
      return coreAddress;
    }

#if UNITY_EDITOR
    if (!AssetExistsAtPath(stagedCoreAssetPath)) {
      return normalizedAssetPath;
    }
#endif

    return stagedCoreAssetPath;
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

    var activePackIds = EnumerateRuntimeActivePackIds();

    if (ContentPackCatalogLoader.TryResolveExportedAddress(
      normalizedAssetPath,
      activePackIds,
      out var exportedAddress
    )) {
      return exportedAddress;
    }

#if UNITY_EDITOR
    var relativePath = normalizedAssetPath.Substring("Assets/".Length);
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
#endif

    return ResolveCoreAssetPath(normalizedAssetPath);
  }

  static List<string> EnumerateRuntimeActivePackIds() {
    var registryPackIds = EnumerateRegistryPackIds();
    if (!runtimeRequestedPackIdsConfigured) {
      return FilterPackIdsByCatalogReadiness(EnumerateDefaultRuntimePackIds(registryPackIds));
    }

    var result = new List<string>();
    var requested = new HashSet<string>(runtimeRequestedPackIds, StringComparer.OrdinalIgnoreCase);

    for (var i = 0; i < registryPackIds.Count; i++) {
      var normalized = NormalizePackId(registryPackIds[i]);
      if (string.IsNullOrWhiteSpace(normalized)) {
        continue;
      }

      if (!requested.Contains(normalized)) {
        continue;
      }

      result.Add(normalized);
    }

    return FilterPackIdsByCatalogReadiness(result);
  }

  static List<string> EnumerateDefaultRuntimePackIds(List<string> registryPackIds) {
    var result = new List<string>();
    if (ContainsPackId(registryPackIds, "Core")) {
      result.Add("Core");
    }

    return result;
  }

  static List<string> FilterPackIdsByCatalogReadiness(List<string> packIds) {
    if (!SpriteStreamingRuntimeSettings.EnableLocalContentPackCatalogs) {
      return packIds;
    }

    var result = new List<string>();
    if (packIds == null) {
      return result;
    }

    for (var i = 0; i < packIds.Count; i++) {
      var normalized = NormalizePackId(packIds[i]);
      if (string.IsNullOrWhiteSpace(normalized)) {
        continue;
      }

      if (!ContentPackCatalogLoader.IsPackReady(normalized)) {
        continue;
      }

      result.Add(normalized);
    }

    return result;
  }

  static List<string> EnumerateRegistryPackIds() {
    var result = new List<string>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var registryInstance = Registry;
    var registryPackIds = registryInstance != null ? registryInstance.ActivePackIds : Array.Empty<string>();

    for (var i = 0; i < registryPackIds.Count; i++) {
      var normalized = NormalizePackId(registryPackIds[i]);
      if (string.IsNullOrWhiteSpace(normalized)) {
        continue;
      }

      if (!seen.Add(normalized)) {
        continue;
      }

      result.Add(normalized);
    }

    return result;
  }

  static List<string> NormalizePackIds(IEnumerable<string> packIds) {
    var result = new List<string>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (packIds == null) {
      return result;
    }

    foreach (var packId in packIds) {
      var normalized = NormalizePackId(packId);
      if (string.IsNullOrWhiteSpace(normalized)) {
        continue;
      }

      if (!seen.Add(normalized)) {
        continue;
      }

      result.Add(normalized);
    }

    return result;
  }

  static bool ArePackIdListsEqual(List<string> left, List<string> right) {
    if (left == null || right == null) {
      return left == right;
    }

    if (left.Count != right.Count) {
      return false;
    }

    for (var i = 0; i < left.Count; i++) {
      if (!string.Equals(left[i], right[i], StringComparison.OrdinalIgnoreCase)) {
        return false;
      }
    }

    return true;
  }

  static IReadOnlyList<string> FilterStagedRootsByRuntimePacks(IReadOnlyList<string> roots) {
    if (roots == null || roots.Count <= 0 || !runtimeRequestedPackIdsConfigured) {
      return roots ?? Array.Empty<string>();
    }

    var result = new List<string>();
    var activePackIds = EnumerateRuntimeActivePackIds();
    for (var i = 0; i < roots.Count; i++) {
      var root = NormalizeAssetPath(roots[i]);
      if (string.IsNullOrWhiteSpace(root)) {
        continue;
      }

      if (IsRootInRuntimePack(root, activePackIds)) {
        result.Add(root);
      }
    }

    return result;
  }

  static bool IsRootInRuntimePack(string root, IReadOnlyList<string> activePackIds) {
    if (string.Equals(root, CoreStageRootAssetPath, StringComparison.OrdinalIgnoreCase) ||
        root.StartsWith(CoreStageRootAssetPath + "/", StringComparison.OrdinalIgnoreCase)) {
      return ContainsPackId(activePackIds, "Core");
    }

    for (var i = 0; i < activePackIds.Count; i++) {
      var packRoot = BuildStageRootForPack(activePackIds[i]);
      if (string.IsNullOrWhiteSpace(packRoot)) {
        continue;
      }

      if (string.Equals(root, packRoot, StringComparison.OrdinalIgnoreCase) ||
          root.StartsWith(packRoot + "/", StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
  }

  static bool ContainsPackId(IReadOnlyList<string> packIds, string packId) {
    for (var i = 0; i < packIds.Count; i++) {
      if (string.Equals(packIds[i], packId, StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
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
           string.Equals(assetPath, StageRootAssetPath, StringComparison.OrdinalIgnoreCase) ||
           assetPath.StartsWith(StageRootAssetPath + "/", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(assetPath, FormsStageRootAssetPath, StringComparison.OrdinalIgnoreCase) ||
           assetPath.StartsWith(FormsStageRootAssetPath + "/", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(assetPath, GearsStageRootAssetPath, StringComparison.OrdinalIgnoreCase) ||
           assetPath.StartsWith(GearsStageRootAssetPath + "/", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(assetPath, SlicesStageRootAssetPath, StringComparison.OrdinalIgnoreCase) ||
           assetPath.StartsWith(SlicesStageRootAssetPath + "/", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(assetPath, EpisodesStageRootAssetPath, StringComparison.OrdinalIgnoreCase) ||
           assetPath.StartsWith(EpisodesStageRootAssetPath + "/", StringComparison.OrdinalIgnoreCase);
  }

  static string BuildStageAssetPathForPack(string packId, string relativePath) {
    if (string.IsNullOrWhiteSpace(packId) || string.IsNullOrWhiteSpace(relativePath)) return "";
    return NormalizeAssetPath(BuildStageRootForPack(packId) + "/" + relativePath);
  }

  static string BuildStageRootForPack(string packId) {
    var normalizedPackId = NormalizePackId(packId);
    if (string.IsNullOrWhiteSpace(normalizedPackId)) {
      return "";
    }

    if (string.Equals(normalizedPackId, "Core", StringComparison.OrdinalIgnoreCase)) {
      return CoreStageRootAssetPath;
    }

    if (normalizedPackId.StartsWith("Form_", StringComparison.OrdinalIgnoreCase)) {
      return NormalizeAssetPath(FormsStageRootAssetPath + "/" + normalizedPackId);
    }

    if (normalizedPackId.StartsWith("Gear_", StringComparison.OrdinalIgnoreCase)) {
      return NormalizeAssetPath(GearsStageRootAssetPath + "/" + normalizedPackId);
    }

    if (normalizedPackId.StartsWith("Episode_", StringComparison.OrdinalIgnoreCase)) {
      return NormalizeAssetPath(EpisodesStageRootAssetPath + "/" + normalizedPackId);
    }

    return NormalizeAssetPath(StageRootAssetPath + "/" + normalizedPackId);
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

  static string NormalizePackId(string packId) {
    return string.IsNullOrWhiteSpace(packId) ? "" : packId.Trim();
  }
}

public static class RuntimeContentPackResolver {
  const string DefaultForm = "Base";
  static readonly HashSet<string> warnedMissingGearPackIds = new(StringComparer.OrdinalIgnoreCase);

  public static void ConfigureForGameplayStart(bool isNewGame, string source = "") {
    var activeForm = ResolveActiveFormForGameplayStart(isNewGame);
    var gearForms = ResolveGearFormsForGameplayStart(isNewGame);
    var resolvedSource = string.IsNullOrWhiteSpace(source) ? "gameplay_start" : source;
    ContentEpisodeProgression.ConfigureForGameplayStart(isNewGame, resolvedSource);
    var packIds = BuildRequestedPackIds(activeForm, gearForms);

    LogResolvedPackIds(isNewGame ? "new_game" : "load_game", resolvedSource, activeForm, packIds);
    ActiveContentRegistryRuntime.ConfigureRuntimeRequestedPackIds(packIds, resolvedSource);
  }

  public static void ConfigureForCurrentRuntimeState(string source = "") {
    var activeForm = ResolveFormOrDefault(EsperanzaForms.GetActive());
    EquippedItems.EnsureKnownForms();
    EquippedItems.EnsureForm(activeForm);

    var resolvedSource = string.IsNullOrWhiteSpace(source) ? "runtime_state" : source;
    ContentEpisodeProgression.ConfigureForCurrentRuntimeState(resolvedSource);
    var packIds = BuildRequestedPackIds(activeForm, EquippedItems.AllGearForms);

    LogResolvedPackIds("runtime_state", resolvedSource, activeForm, packIds);
    ActiveContentRegistryRuntime.ConfigureRuntimeRequestedPackIds(packIds, resolvedSource);
  }

  public static void ReloadForSceneChange(
    string previousSceneId,
    string currentSceneId,
    string reason = ""
  ) {
    var resolvedReason = NormalizeToken(reason);
    if (string.IsNullOrWhiteSpace(resolvedReason)) {
      resolvedReason = "scene_change";
    }

    RuntimeLog.Log(
      "[RuntimeContentPackResolver] Scene-change packs unloading" +
      " reason='" + resolvedReason + "'" +
      " previous='" + (previousSceneId ?? "") + "'" +
      " current='" + (currentSceneId ?? "") + "'"
    );

    ActiveContentRegistryRuntime.ClearRuntimeRequestedPackIds("scene_change_unload:" + resolvedReason);
    ConfigureForCurrentRuntimeState("scene_change_rederive:" + resolvedReason);
  }

  static List<string> BuildRequestedPackIds(
    string activeForm,
    Dictionary<string, Dictionary<string, GearItem>> gearForms
  ) {
    var result = new List<string>();
    AddBaselinePackIds(result);

    var saveDrivenPackIds = BuildSaveDrivenPackIds(
      activeForm,
      gearForms,
      ActiveContentRegistryRuntime.GetAvailablePackIds()
    );
    for (var i = 0; i < saveDrivenPackIds.Count; i++) {
      AddUniquePackId(result, saveDrivenPackIds[i]);
    }

    return result;
  }

  public static List<string> BuildSaveDrivenPackIds(
    string activeForm,
    Dictionary<string, Dictionary<string, GearItem>> gearForms,
    IEnumerable<string> availablePackIds
  ) {
    var result = new List<string>();
    var available = BuildAvailablePackSet(availablePackIds);
    activeForm = ResolveFormOrDefault(activeForm);

    AddAvailablePackId(result, BuildUiPackId(activeForm), available);

    if (gearForms == null) {
      return result;
    }

    if (!gearForms.TryGetValue(activeForm, out var slots) || slots == null) {
      return result;
    }

    foreach (var slotEntry in slots) {
      var slot = NormalizeToken(slotEntry.Key);
      if (string.IsNullOrWhiteSpace(slot)) {
        continue;
      }

      var gearItem = slotEntry.Value;
      if (gearItem == null) {
        var nullPackId = EquippedItems.BuildGearPackId(activeForm + "_no", slot);
        AddAvailablePackId(result, nullPackId, available);
        continue;
      }

      var gearId = NormalizeToken(gearItem.gearId);
      var packId = EquippedItems.BuildGearPackId(gearId, slot);
      if (!AddAvailablePackId(result, packId, available)) {
        WarnMissingGearPack(packId, activeForm, slot, gearId);
      }
    }

    return result;
  }

  static HashSet<string> BuildAvailablePackSet(IEnumerable<string> packIds) {
    var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (packIds == null) {
      return result;
    }

    foreach (var packId in packIds) {
      var normalized = NormalizeToken(packId);
      if (string.IsNullOrWhiteSpace(normalized)) {
        continue;
      }

      result.Add(normalized);
    }

    return result;
  }

  static bool AddAvailablePackId(
    List<string> result,
    string packId,
    HashSet<string> availablePackIds
  ) {
    var normalized = NormalizeToken(packId);
    if (string.IsNullOrWhiteSpace(normalized)) {
      return false;
    }

    if (availablePackIds == null) {
      return false;
    }

    if (!availablePackIds.Contains(normalized)) {
      return false;
    }

    AddUniquePackId(result, normalized);
    return true;
  }

  static void AddBaselinePackIds(List<string> result) {
    AddEpisodePackIds(result);

    var availablePackIds = ActiveContentRegistryRuntime.GetAvailablePackIds();
    var hasEpisodeProgression = ContentEpisodeProgression.HasRuntimeEpisodes();
    for (var i = 0; i < availablePackIds.Count; i++) {
      var packId = NormalizeToken(availablePackIds[i]);
      if (string.IsNullOrWhiteSpace(packId)) {
        continue;
      }

      if (IsSaveDrivenUiPackId(packId)) {
        continue;
      }

      if (IsSaveDrivenGearPackId(packId)) {
        continue;
      }

      if (hasEpisodeProgression && IsEpisodeScopedPackId(packId)) {
        continue;
      }

      AddUniquePackId(result, packId);
    }
  }

  static void AddEpisodePackIds(List<string> result) {
    var packIds = ContentEpisodeProgression.GetActivePackIds();
    if (packIds == null) return;

    for (var i = 0; i < packIds.Count; i++) {
      AddUniquePackId(result, packIds[i]);
    }
  }

  public static string ResolveActiveFormForGameplayStart(bool isNewGame) {
    if (isNewGame) {
      return DefaultForm;
    }

    var loadedForms = SaveSlotManager.Load(SaveKeys.Forms);
    if (loadedForms == null || loadedForms.Count <= 0 || !loadedForms.ContainsKey(SaveKeys.ActiveForm)) {
      return DefaultForm;
    }

    return ResolveFormOrDefault(Convert.ToString(loadedForms[SaveKeys.ActiveForm]));
  }

  public static Dictionary<string, Dictionary<string, GearItem>> ResolveGearFormsForGameplayStart(bool isNewGame) {
    var gearForms = EquippedItems.CreateDefaultGearFormsSnapshot();
    if (isNewGame) {
      return gearForms;
    }

    var loadedGear = SaveSlotManager.Load(SaveKeys.EquippedGear);
    ApplySavedGearForms(gearForms, loadedGear);
    return gearForms;
  }

  static void ApplySavedGearForms(
    Dictionary<string, Dictionary<string, GearItem>> gearForms,
    SaveData loadedGear
  ) {
    if (gearForms == null || loadedGear == null || loadedGear.Count <= 0) {
      return;
    }

    if (!loadedGear.HasPrefix(SaveKeys.AllGear)) {
      return;
    }

    var loadedForms = loadedGear.GetComplex<Dictionary<string, Dictionary<string, GearItem>>>(SaveKeys.AllGear);
    if (loadedForms == null) {
      return;
    }

    EquippedItems.ApplySavedGearForms(gearForms, loadedForms);
  }

  static string BuildUiPackId(string activeForm) {
    return "UI" + ResolveFormOrDefault(activeForm);
  }

  static bool IsSaveDrivenUiPackId(string packId) {
    var normalized = NormalizeToken(packId);
    if (!normalized.StartsWith("UI", StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    var form = normalized.Substring("UI".Length);
    return !string.IsNullOrWhiteSpace(EsperanzaForms.ResolveFormKey(form));
  }

  static bool IsSaveDrivenGearPackId(string packId) {
    var normalized = NormalizeToken(packId);
    if (!normalized.StartsWith("Gear", StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    foreach (var form in EsperanzaForms.KnownForms) {
      var prefix = "Gear" + form + "_";
      if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      var payload = normalized.Substring(prefix.Length);
      var parts = payload.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
      return parts.Length >= 2;
    }

    return false;
  }

  static bool IsEpisodeScopedPackId(string packId) {
    var normalized = NormalizeToken(packId);
    if (string.IsNullOrWhiteSpace(normalized)) {
      return false;
    }

    return normalized.StartsWith("Enemy", StringComparison.OrdinalIgnoreCase) ||
           normalized.StartsWith("Dialog", StringComparison.OrdinalIgnoreCase) ||
           normalized.StartsWith("Objective", StringComparison.OrdinalIgnoreCase) ||
           normalized.StartsWith("Env", StringComparison.OrdinalIgnoreCase) ||
           normalized.StartsWith("Environment", StringComparison.OrdinalIgnoreCase) ||
           normalized.StartsWith("Slice_", StringComparison.OrdinalIgnoreCase) ||
           normalized.StartsWith("Episode_", StringComparison.OrdinalIgnoreCase);
  }

  static string ResolveFormOrDefault(string formName) {
    var resolved = EsperanzaForms.ResolveFormKey(formName);
    return string.IsNullOrWhiteSpace(resolved) ? DefaultForm : resolved;
  }

  static void AddUniquePackId(List<string> result, string packId) {
    var normalized = NormalizeToken(packId);
    if (string.IsNullOrWhiteSpace(normalized)) {
      return;
    }

    for (var i = 0; i < result.Count; i++) {
      if (string.Equals(result[i], normalized, StringComparison.OrdinalIgnoreCase)) {
        return;
      }
    }

    result.Add(normalized);
  }

  static void WarnMissingGearPack(string packId, string activeForm, string slot, string gearId) {
    if (!ActiveContentRegistryRuntime.HasActiveExternalContent()) {
      return;
    }

    if (!warnedMissingGearPackIds.Add(packId)) {
      return;
    }

    Debug.LogWarning(
      "[RuntimeContentPackResolver] Missing equipped gear pack" +
      " pack_id='" + packId + "'" +
      " form='" + (activeForm ?? "") + "'" +
      " slot='" + (slot ?? "") + "'" +
      " gear_id='" + (gearId ?? "") + "'"
    );
  }

  static void LogResolvedPackIds(
    string mode,
    string source,
    string activeForm,
    IReadOnlyList<string> packIds
  ) {
    RuntimeLog.Log(
      "[RuntimeContentPackResolver] Save-derived packs loaded" +
      " mode='" + (mode ?? "") + "'" +
      " source='" + (source ?? "") + "'" +
      " active_form='" + (activeForm ?? "") + "'" +
      " count=" + (packIds != null ? packIds.Count : 0) +
      " packs=" + DescribePackIds(packIds)
    );
  }

  static string DescribePackIds(IReadOnlyList<string> packIds) {
    if (packIds == null || packIds.Count <= 0) {
      return "-";
    }

    return string.Join(", ", packIds);
  }

  static string NormalizeToken(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }
}
