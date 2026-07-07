#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

public static partial class ContentPackPipeline {
  static bool GenerateActiveRegistryAsset(
    Dictionary<string, PackDefinition> packById,
    List<string> activePackIds,
    bool logResult
  ) {
    var registry = AssetDatabase.LoadAssetAtPath<ActiveContentRegistry>(ActiveRegistryAssetPath);
    if (registry == null) {
      EnsureDirectoryAssetPath("Assets/Resources");
      registry = ScriptableObject.CreateInstance<ActiveContentRegistry>();
      AssetDatabase.CreateAsset(registry, ActiveRegistryAssetPath);
    }

    var stagedTextureRoots = new List<string>();
    var stagedSpriteLibraryRoots = new List<string>();
    var coreContentRoots = new List<string>();
    var locations = new List<LocationInfo>();
    var dialogs = new List<LocationDialogDefinition>();
    var objectives = new List<ContentObjectiveDefinition>();
    ReadContentManifestRuntimeDefinitions(out var slices, out var episodes);
    var defaultLocationId = "";

    for (var i = 0; i < activePackIds.Count; i++) {
      var packId = activePackIds[i];
      if (!packById.TryGetValue(packId, out var pack) || pack == null) continue;

      var stagedSpritesRoot = NormalizeAssetPath(pack.stageAssetRoot + "/Sprites");
      if (Directory.Exists(GetPhysicalPath(stagedSpritesRoot))) {
        AddUniquePath(stagedTextureRoots, stagedSpritesRoot);
      }

      var stagedSpriteLibraryRoot = NormalizeAssetPath(pack.stageAssetRoot + "/Sprites/SpriteLibraries");
      if (Directory.Exists(GetPhysicalPath(stagedSpriteLibraryRoot))) {
        AddUniquePath(stagedSpriteLibraryRoots, stagedSpriteLibraryRoot);
      }

      if (string.Equals(pack.packId, CorePackId, StringComparison.OrdinalIgnoreCase)) {
        for (var rootIndex = 0; rootIndex < pack.ownedRoots.Count; rootIndex++) {
          AddUniquePath(coreContentRoots, BuildStageAssetPath(pack, pack.ownedRoots[rootIndex]));
        }
        continue;
      }

      if (!string.IsNullOrWhiteSpace(pack.defaultLocationId) && string.IsNullOrWhiteSpace(defaultLocationId)) {
        defaultLocationId = pack.defaultLocationId;
      }

      if (TryReadLocationSnapshot(pack, out var locationSnapshot) && locationSnapshot != null) {
        locations.Add(locationSnapshot);
      }

      if (TryReadDialogSnapshot(pack, out var dialogSnapshot) && dialogSnapshot != null) {
        dialogs.Add(dialogSnapshot);
      }

      if (TryReadObjectiveSnapshot(pack, out var objectiveSnapshot) && objectiveSnapshot != null) {
        objectives.Add(objectiveSnapshot);
      }

    }

    registry.Configure(
      externalContentActive: activePackIds.Count > 0,
      defaultLocationId: defaultLocationId,
      activePackIds: activePackIds,
      stagedTextureRoots: stagedTextureRoots,
      stagedSpriteLibraryRoots: stagedSpriteLibraryRoots,
      coreContentRoots: coreContentRoots,
      locations: locations,
      dialogs: dialogs,
      slices: slices,
      episodes: episodes,
      objectives: objectives
    );

    EditorUtility.SetDirty(registry);
    ActiveContentRegistryRuntime.ForceReload();

    if (logResult) {
      var activeForm = EsperanzaForms.GetActive();
      Debug.Log(
        "[ContentPackPipeline] Generated active content registry." +
        " active_packs=" + string.Join(", ", activePackIds) +
        " active_form='" + (string.IsNullOrWhiteSpace(activeForm) ? "-" : activeForm) + "'" +
        " default_location='" + (string.IsNullOrWhiteSpace(defaultLocationId) ? "-" : defaultLocationId) + "'" +
        " staged_texture_roots=" + stagedTextureRoots.Count +
        " staged_library_roots=" + stagedSpriteLibraryRoots.Count +
        " slices=" + slices.Count +
        " episodes=" + episodes.Count +
        " objectives=" + objectives.Count
      );
    }

    return true;
  }

  static void WriteInactiveRegistryAsset(bool logResult) {
    var registry = AssetDatabase.LoadAssetAtPath<ActiveContentRegistry>(ActiveRegistryAssetPath);
    if (registry == null) {
      EnsureDirectoryAssetPath("Assets/Resources");
      registry = ScriptableObject.CreateInstance<ActiveContentRegistry>();
      AssetDatabase.CreateAsset(registry, ActiveRegistryAssetPath);
    }

    registry.Configure(
      externalContentActive: false,
      defaultLocationId: "",
      activePackIds: Array.Empty<string>(),
      stagedTextureRoots: Array.Empty<string>(),
      stagedSpriteLibraryRoots: Array.Empty<string>(),
      coreContentRoots: Array.Empty<string>(),
      locations: Array.Empty<LocationInfo>(),
      dialogs: Array.Empty<LocationDialogDefinition>(),
      slices: Array.Empty<ContentSliceDefinition>(),
      episodes: Array.Empty<ContentEpisodeDefinition>(),
      objectives: Array.Empty<ContentObjectiveDefinition>()
    );

    EditorUtility.SetDirty(registry);
    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    ActiveContentRegistryRuntime.ForceReload();

    if (logResult) {
      Debug.Log("[ContentPackPipeline] Wrote inactive content registry fallback.");
    }
  }

  static void ReadContentManifestRuntimeDefinitions(
    out List<ContentSliceDefinition> slices,
    out List<ContentEpisodeDefinition> episodes
  ) {
    slices = new List<ContentSliceDefinition>();
    episodes = new List<ContentEpisodeDefinition>();

    var path = Path.Combine(GetProjectRoot(), "Assets", "ContentManifest.json");
    if (!File.Exists(path)) return;

    ContentManifestJson manifest;
    try {
      manifest = JsonUtility.FromJson<ContentManifestJson>(File.ReadAllText(path));
    }
    catch (Exception ex) {
      Debug.LogWarning("[ContentPackPipeline] Failed to read runtime content manifest definitions. error='" + ex.Message + "'");
      return;
    }

    if (manifest == null) return;
    AppendContentManifestSlices(slices, manifest);
    AppendContentManifestEpisodes(episodes, manifest);
  }

  static void AppendContentManifestSlices(List<ContentSliceDefinition> target, ContentManifestJson manifest) {
    if (target == null || manifest == null || manifest.slices == null) return;

    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < manifest.slices.Count; i++) {
      var slice = manifest.slices[i];
      var sliceId = NormalizeToken(slice?.id);
      if (string.IsNullOrWhiteSpace(sliceId)) continue;
      if (!seen.Add(sliceId)) continue;

      target.Add(new ContentSliceDefinition(sliceId, GetContentManifestSliceIds(slice)));
    }
  }

  static void AppendContentManifestEpisodes(List<ContentEpisodeDefinition> target, ContentManifestJson manifest) {
    if (target == null || manifest == null || manifest.episodes == null) return;

    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < manifest.episodes.Count; i++) {
      var episode = manifest.episodes[i];
      var episodeId = NormalizeToken(episode?.id);
      if (string.IsNullOrWhiteSpace(episodeId)) continue;
      if (!seen.Add(episodeId)) continue;

      target.Add(new ContentEpisodeDefinition(episodeId, episode.slices));
    }
  }

  [Serializable]
  sealed class ExportedObjectiveJson {
    public string id;
    public string objective;
  }

  static bool TryReadObjectiveSnapshot(PackDefinition pack, out ContentObjectiveDefinition objectiveInfo) {
    objectiveInfo = null;
    if (pack == null) return false;
    if (!string.Equals(pack.kind, "objective", StringComparison.OrdinalIgnoreCase)) return false;
    if (pack.ownedRoots == null || pack.ownedRoots.Count <= 0) return false;

    for (var i = 0; i < pack.ownedRoots.Count; i++) {
      var root = NormalizeAssetPath(pack.ownedRoots[i]);
      if (string.IsNullOrWhiteSpace(root)) continue;
      if (!root.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;

      var snapshotAssetPath = BuildStageAssetPath(pack, root);
      var snapshotFullPath = GetPhysicalPath(snapshotAssetPath);
      if (!File.Exists(snapshotFullPath)) continue;

      var json = JsonUtility.FromJson<ExportedObjectiveJson>(File.ReadAllText(snapshotFullPath));
      if (json == null || string.IsNullOrWhiteSpace(json.objective)) continue;

      objectiveInfo = new ContentObjectiveDefinition(
        pack.packId,
        json.id,
        json.objective
      );
      return true;
    }

    return false;
  }

  static bool TryReadLocationSnapshot(PackDefinition pack, out LocationInfo locationInfo) {
    locationInfo = null;
    if (pack == null || string.IsNullOrWhiteSpace(pack.snapshotRelativePath)) return false;

    var snapshotAssetPath = NormalizeAssetPath(pack.stageAssetRoot + "/" + pack.snapshotRelativePath);
    var snapshotFullPath = GetPhysicalPath(snapshotAssetPath);
    if (!File.Exists(snapshotFullPath)) return false;

    var json = JsonUtility.FromJson<ExportedLocationJson>(File.ReadAllText(snapshotFullPath));
    if (json == null || string.IsNullOrWhiteSpace(json.locationId)) return false;

    var objectives = new List<LocationObjective>();
    if (json.objectives != null) {
      for (var i = 0; i < json.objectives.Count; i++) {
        var objective = json.objectives[i];
        if (objective == null) continue;
        objectives.Add(new LocationObjective(
          (LocationObjectiveType)Mathf.Clamp(objective.type, 0, (int)LocationObjectiveType.Custom),
          objective.description ?? "",
          objective.targetCount,
          objective.targetSeconds
        ));
      }
    }

    locationInfo = new LocationInfo(
      id: json.locationId,
      name: json.name,
      enemies: json.enemies != null ? new List<string>(json.enemies) : new List<string>(),
      maxEnemies: json.maxEnemies,
      spawnInterval: json.spawnInterval,
      objectives: objectives,
      locationPrefabData: new LocationPrefabData(
        prefab: null,
        assetPath: json.prefabAssetPath,
        localPosition: json.localPosition,
        localEulerAngles: json.localEulerAngles,
        localScale: json.localScale
      )
    );
    return true;
  }

  static bool TryReadDialogSnapshot(PackDefinition pack, out LocationDialogDefinition dialogInfo) {
    dialogInfo = null;
    if (pack == null || string.IsNullOrWhiteSpace(pack.dialogSnapshotRelativePath)) return false;

    var snapshotAssetPath = NormalizeAssetPath(pack.stageAssetRoot + "/" + pack.dialogSnapshotRelativePath);
    var snapshotFullPath = GetPhysicalPath(snapshotAssetPath);
    if (!File.Exists(snapshotFullPath)) return false;

    var json = JsonUtility.FromJson<ExportedDialogJson>(File.ReadAllText(snapshotFullPath));
    if (json == null || string.IsNullOrWhiteSpace(json.locationId)) return false;

    var speakers = new List<DialogSpeakerDefinition>();
    if (json.speakers != null) {
      for (var i = 0; i < json.speakers.Count; i++) {
        var speaker = json.speakers[i];
        if (speaker == null) continue;

        var lines = new List<GameplayDialogController.GameplayDialogNode>();
        if (speaker.lines != null) {
          for (var lineIndex = 0; lineIndex < speaker.lines.Count; lineIndex++) {
            var line = speaker.lines[lineIndex];
            if (line == null) continue;
            lines.Add(new GameplayDialogController.GameplayDialogNode {
              lineNumber = line.lineNumber,
              text = line.text ?? "",
              emotion = line.emotion ?? "",
              trigger = line.trigger ?? "",
              speakerId = line.speakerId ?? "",
              speakerName = line.speakerName ?? "",
              speaker = (GameplayDialogController.DialogSpeakerSide)line.speaker,
              avatarForm = line.avatarForm ?? "",
              otherType = (GameplayDialogController.DialogOtherType)line.otherType,
              portraitLibraryName = line.portraitLibraryName ?? "",
              locationId = line.locationId ?? ""
            });
          }
        }

        speakers.Add(new DialogSpeakerDefinition(
          speakerId: speaker.speakerId ?? "",
          speakerName: speaker.speakerName ?? "",
          portraitLibraryName: speaker.portraitLibraryName ?? "",
          speakerSide: (GameplayDialogController.DialogSpeakerSide)speaker.speakerSide,
          lines: lines.ToArray()
        ));
      }
    }

    dialogInfo = new LocationDialogDefinition(json.locationId, speakers.ToArray());
    return true;
  }
}
#endif
