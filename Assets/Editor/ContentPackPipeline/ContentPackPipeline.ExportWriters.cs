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
  static Dictionary<string, AssignedAsset> AssignPackAssets(List<PackDefinition> packDefinitions, List<string> errors) {
    var usageByAssetPath = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    var packById = packDefinitions.ToDictionary(pack => pack.packId, StringComparer.OrdinalIgnoreCase);

    foreach (var pack in packDefinitions) {
      if (pack == null || pack.assetDependencies == null) continue;
      for (var i = 0; i < pack.assetDependencies.Count; i++) {
        var assetPath = NormalizeAssetPath(pack.assetDependencies[i]);
        if (string.IsNullOrWhiteSpace(assetPath)) continue;
        if (!usageByAssetPath.TryGetValue(assetPath, out var usage)) {
          usage = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
          usageByAssetPath[assetPath] = usage;
        }
        usage.Add(pack.packId);
      }
    }

    var result = new Dictionary<string, AssignedAsset>(StringComparer.OrdinalIgnoreCase);
    foreach (var pair in usageByAssetPath) {
      var assetPath = pair.Key;
      if (assetPath.StartsWith("Assets/Generated/", StringComparison.OrdinalIgnoreCase)) {
        errors.Add("Generated assets are not allowed in external packs. asset='" + assetPath + "'");
        continue;
      }

      var assignedPackId = ResolveAssignedPackId(assetPath, pair.Value, packDefinitions);
      if (!packById.TryGetValue(assignedPackId, out var pack)) {
        errors.Add("Failed to resolve assigned pack for asset '" + assetPath + "'.");
        continue;
      }

      var originalGuid = AssetDatabase.AssetPathToGUID(assetPath);
      if (string.IsNullOrWhiteSpace(originalGuid)) {
        errors.Add("Asset is missing a GUID and cannot be exported. asset='" + assetPath + "'");
        continue;
      }

      var relativePath = ResolveExportRelativePath(pack, assetPath);
      result[assetPath] = new AssignedAsset {
        assetPath = assetPath,
        originalGuid = originalGuid,
        newGuid = ComputeDeterministicExportGuid(assignedPackId, assetPath),
        packId = assignedPackId,
        externalAssetPath = NormalizeFullPath(Path.Combine(pack.externalRootPath, relativePath)),
        stageAssetPath = NormalizeAssetPath(pack.stageAssetRoot + "/" + relativePath)
      };
    }

    return result;
  }

  static string ResolveAssignedPackId(string assetPath, HashSet<string> usage, List<PackDefinition> packDefinitions) {
    var ownedPackId = ResolveOwnedPackId(assetPath, packDefinitions);
    if (!string.IsNullOrWhiteSpace(ownedPackId)) {
      return ownedPackId;
    }

    if (usage == null || usage.Count <= 0) return CorePackId;
    if (usage.Contains(CorePackId) || usage.Count > 1) return CorePackId;
    foreach (var packId in usage) return packId;
    return CorePackId;
  }

  static string ResolveOwnedPackId(string assetPath, List<PackDefinition> packDefinitions) {
    var normalizedAssetPath = NormalizeAssetPath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath) || packDefinitions == null || packDefinitions.Count <= 0) {
      return "";
    }

    string bestPackId = "";
    var bestMatchLength = -1;

    for (var packIndex = 0; packIndex < packDefinitions.Count; packIndex++) {
      var pack = packDefinitions[packIndex];
      if (pack == null || pack.ownedRoots == null || pack.ownedRoots.Count <= 0) {
        continue;
      }

      for (var rootIndex = 0; rootIndex < pack.ownedRoots.Count; rootIndex++) {
        var ownedRoot = NormalizeAssetPath(pack.ownedRoots[rootIndex]);
        if (string.IsNullOrWhiteSpace(ownedRoot)) {
          continue;
        }

        var isDirectMatch = string.Equals(normalizedAssetPath, ownedRoot, StringComparison.OrdinalIgnoreCase);
        var isUnderRoot = normalizedAssetPath.StartsWith(ownedRoot + "/", StringComparison.OrdinalIgnoreCase);
        if (!isDirectMatch && !isUnderRoot) {
          continue;
        }

        if (ownedRoot.Length < bestMatchLength) {
          continue;
        }

        if (ownedRoot.Length == bestMatchLength &&
            string.Equals(bestPackId, CorePackId, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(pack.packId, CorePackId, StringComparison.OrdinalIgnoreCase)) {
          bestPackId = pack.packId;
          continue;
        }

        if (ownedRoot.Length > bestMatchLength) {
          bestMatchLength = ownedRoot.Length;
          bestPackId = pack.packId;
        }
      }
    }

    return bestPackId;
  }

  static void WriteAssignedAssets(
    Dictionary<string, AssignedAsset> assignedAssets,
    List<string> errors,
    TransitionPipelineMode mode,
    ExportSyncStats stats
  ) {
    var guidMap = BuildGuidMap(assignedAssets);
    var orderedAssets = assignedAssets.Values.OrderBy(asset => asset.externalAssetPath, StringComparer.OrdinalIgnoreCase).ToList();

    for (var i = 0; i < orderedAssets.Count; i++) {
      var assigned = orderedAssets[i];
      try {
        CopyAssetPayload(assigned.assetPath, assigned.externalAssetPath, guidMap, mode, stats);
        CopyMetaPayload(assigned.assetPath, assigned.externalAssetPath, guidMap, mode, stats);
      }
      catch (Exception ex) {
        errors.Add(
          "Failed to copy asset." +
          " asset='" + assigned.assetPath + "'" +
          " target='" + assigned.externalAssetPath + "'" +
          " error='" + ex.Message + "'"
        );
      }
    }
  }

  static Dictionary<string, string> BuildGuidMap(Dictionary<string, AssignedAsset> assignedAssets) {
    var guidMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var assigned in assignedAssets.Values) {
      guidMap[assigned.originalGuid] = assigned.newGuid;
    }
    return guidMap;
  }

  static string ComputeDeterministicExportGuid(string packId, string assetPath) {
    var normalizedPackId = NormalizeToken(packId);
    var normalizedAssetPath = NormalizeAssetPath(assetPath);
    var seed = normalizedPackId + "|" + normalizedAssetPath;
    using var sha = SHA256.Create();
    var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(seed));
    var builder = new StringBuilder(32);
    for (var i = 0; i < 16; i++) {
      builder.Append(hash[i].ToString("x2"));
    }

    return builder.ToString();
  }

  static void CopyAssetPayload(
    string sourceAssetPath,
    string targetFullPath,
    Dictionary<string, string> guidMap,
    TransitionPipelineMode mode,
    ExportSyncStats stats
  ) {
    var sourceFullPath = Path.GetFullPath(sourceAssetPath);
    EnsureDirectoryFullPath(Path.GetDirectoryName(targetFullPath));
    if (ShouldRewriteTextFile(sourceFullPath)) {
      var text = File.ReadAllText(sourceFullPath);
      var rewrittenText = RewriteGuids(text, guidMap);
      if (WriteTextIfChanged(targetFullPath, rewrittenText, mode)) {
        if (stats != null) {
          stats.assetPayloadsWritten++;
        }
      }
      else if (stats != null) {
        stats.assetPayloadsSkipped++;
      }
      return;
    }

    if (mode == TransitionPipelineMode.Smart && File.Exists(targetFullPath)) {
      if (stats != null) {
        stats.assetPayloadsSkipped++;
      }
      return;
    }

    File.Copy(sourceFullPath, targetFullPath, overwrite: true);
    if (stats != null) {
      stats.assetPayloadsWritten++;
    }
  }

  static string ResolveExportRelativePath(PackDefinition pack, string assetPath) {
    var normalizedAssetPath = NormalizeAssetPath(assetPath);
    if (pack != null &&
        pack.targetRelativePathByAssetPath != null &&
        pack.targetRelativePathByAssetPath.TryGetValue(normalizedAssetPath, out var relativePath) &&
        !string.IsNullOrWhiteSpace(relativePath)) {
      return NormalizeAssetPath(relativePath);
    }

    return normalizedAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
      ? normalizedAssetPath.Substring("Assets/".Length)
      : normalizedAssetPath;
  }

  static bool WriteTextIfChanged(string targetFullPath, string text, TransitionPipelineMode mode) {
    if (mode == TransitionPipelineMode.Smart && File.Exists(targetFullPath)) {
      var existingText = File.ReadAllText(targetFullPath);
      if (string.Equals(existingText, text ?? "", StringComparison.Ordinal)) {
        return false;
      }
    }

    File.WriteAllText(targetFullPath, text ?? "", new UTF8Encoding(false));
    return true;
  }

  static void CopyMetaPayload(
    string sourceAssetPath,
    string targetFullPath,
    Dictionary<string, string> guidMap,
    TransitionPipelineMode mode,
    ExportSyncStats stats
  ) {
    var sourceMetaFullPath = Path.GetFullPath(sourceAssetPath + ".meta");
    if (!File.Exists(sourceMetaFullPath)) {
      throw new FileNotFoundException("Missing meta file.", sourceMetaFullPath);
    }

    var targetMetaFullPath = targetFullPath + ".meta";
    var metaText = File.ReadAllText(sourceMetaFullPath);
    var rewrittenMetaText = RewriteGuids(metaText, guidMap);
    if (WriteTextIfChanged(targetMetaFullPath, rewrittenMetaText, mode)) {
      if (stats != null) {
        stats.metaPayloadsWritten++;
      }
    }
    else if (stats != null) {
      stats.metaPayloadsSkipped++;
    }
  }

  static string RewriteGuids(string text, Dictionary<string, string> guidMap) {
    if (string.IsNullOrWhiteSpace(text) || guidMap == null || guidMap.Count <= 0) {
      return text ?? "";
    }

    return GuidRegex.Replace(text, match => {
      var originalGuid = match.Groups[1].Value;
      if (!guidMap.TryGetValue(originalGuid, out var newGuid)) {
        return match.Value;
      }

      return match.Value.Replace(originalGuid, newGuid);
    });
  }

  static void WriteGeneratedPackData(PackDefinition pack, List<string> errors, TransitionPipelineMode mode, ExportSyncStats stats) {
    if (pack == null) return;

    try {
      if (string.Equals(pack.packId, CorePackId, StringComparison.OrdinalIgnoreCase)) {
        WriteEsperanzaSnapshot(pack, mode, stats);
      }
    }
    catch (Exception ex) {
      errors.Add(
        "Failed to write generated pack data." +
        " pack_id='" + pack.packId + "'" +
        " error='" + ex.Message + "'"
      );
    }
  }

  static void WritePackManifest(PackDefinition pack, List<string> errors, TransitionPipelineMode mode, ExportSyncStats stats) {
    if (pack == null) return;

    try {
      var manifest = new ContentPackManifestJson {
        packId = pack.packId,
        type = pack.kind,
        kind = pack.kind,
        ownedRoots = new List<string>(pack.ownedRoots),
        ownedLocations = new List<string>(pack.ownedLocations),
        ownedEnemyTypes = new List<string>(pack.ownedEnemyTypes),
        dialogIds = new List<string>(pack.dialogIds),
        authoringSources = CloneAuthoringSources(pack.authoringSources),
        exportedFromProject = new DirectoryInfo(GetProjectRoot()).Name,
        sourceRevision = TryGetGitRevision()
      };

      var manifestPath = Path.Combine(pack.externalRootPath, ManifestFileName);
      WriteJson(manifestPath, manifest, mode, stats, generatedFile: false);
    }
    catch (Exception ex) {
      errors.Add(
        "Failed to write pack manifest." +
        " pack_id='" + pack.packId + "'" +
        " error='" + ex.Message + "'"
      );
    }
  }

  static List<ContentPackAuthoringSourceJson> CloneAuthoringSources(List<ContentPackAuthoringSourceJson> sources) {
    var result = new List<ContentPackAuthoringSourceJson>();
    if (sources == null) return result;

    for (var i = 0; i < sources.Count; i++) {
      var source = sources[i];
      if (source == null) continue;
      result.Add(new ContentPackAuthoringSourceJson {
        sourceType = NormalizeToken(source.sourceType),
        assetPath = NormalizeAssetPath(source.assetPath),
        label = NormalizeToken(source.label),
        targetFolder = NormalizePackTargetFolder(source.targetFolder),
        libraryName = NormalizeAssetPath(source.libraryName),
        category = NormalizeToken(source.category),
        labelPrefix = NormalizeToken(source.labelPrefix),
        normalAssetPath = NormalizeAssetPath(source.normalAssetPath)
      });
    }

    return result;
  }

  static void WriteEsperanzaSnapshot(PackDefinition pack, TransitionPipelineMode mode, ExportSyncStats stats) {
    var snapshot = new ExportedEsperanzaSnapshotJson {
      generatedAtUtc = DateTime.UtcNow.ToString("O")
    };

    var sourcePaths = new List<string>();
    sourcePaths.Add("Assets/Scripts/Data/Stats.cs");
    var esperanzaDataDirectory = "Assets/Scripts/Data/Esperanza";
    if (Directory.Exists(Path.GetFullPath(esperanzaDataDirectory))) {
      var files = Directory.GetFiles(Path.GetFullPath(esperanzaDataDirectory), "*.cs", SearchOption.TopDirectoryOnly);
      Array.Sort(files, StringComparer.OrdinalIgnoreCase);
      for (var i = 0; i < files.Length; i++) {
        sourcePaths.Add(ToProjectAssetPath(files[i]));
      }
    }

    for (var i = 0; i < sourcePaths.Count; i++) {
      var assetPath = NormalizeAssetPath(sourcePaths[i]);
      if (string.IsNullOrWhiteSpace(assetPath)) continue;
      var fullPath = Path.GetFullPath(assetPath);
      if (!File.Exists(fullPath)) continue;

      var text = File.ReadAllText(fullPath);
      snapshot.sourceFiles.Add(new ExportedSourceFileJson {
        assetPath = assetPath,
        sha256 = ComputeSha256(text),
        text = text
      });
    }

    var outputPath = Path.Combine(pack.externalRootPath, PackDataFolderName, EsperanzaSnapshotFileName);
    WriteJson(outputPath, snapshot, mode, stats, generatedFile: true);
  }

  static void WriteDomeCitySnapshots(PackDefinition pack, TransitionPipelineMode mode, ExportSyncStats stats) {
    if (!LocationEnemyData.TryGetBuiltInLocation(LocationEnemyData.DomeCityLocationId, out var locationInfo) || locationInfo == null) {
      throw new InvalidOperationException("Built-in DomeCity location data was not found.");
    }

    if (!DialogData.TryGetBuiltInLocation(LocationEnemyData.DomeCityLocationId, out var dialogInfo) || dialogInfo == null) {
      throw new InvalidOperationException("Built-in DomeCity dialog data was not found.");
    }

    var locationSnapshot = new ExportedLocationJson {
      locationId = LocationEnemyData.NormalizeLocationId(locationInfo.id),
      name = locationInfo.name ?? "",
      enemies = locationInfo.enemies != null ? new List<string>(locationInfo.enemies) : new List<string>(),
      maxEnemies = locationInfo.maxEnemies,
      spawnInterval = locationInfo.spawnInterval,
      prefabAssetPath = BuildStageAssetPath(pack, "Assets/Prefabs/Locations/DomeCity.prefab"),
      localPosition = locationInfo.locationPrefabData != null ? locationInfo.locationPrefabData.localPosition : Vector3.zero,
      localEulerAngles = locationInfo.locationPrefabData != null ? locationInfo.locationPrefabData.localEulerAngles : Vector3.zero,
      localScale = locationInfo.locationPrefabData != null ? locationInfo.locationPrefabData.localScale : Vector3.one
    };

    if (locationInfo.objectives != null) {
      for (var i = 0; i < locationInfo.objectives.Count; i++) {
        var objective = locationInfo.objectives[i];
        if (objective == null) continue;
        locationSnapshot.objectives.Add(new ExportedLocationObjectiveJson {
          type = (int)objective.type,
          description = objective.description ?? "",
          targetCount = objective.targetCount,
          targetSeconds = objective.targetSeconds
        });
      }
    }

    var dialogSnapshot = new ExportedDialogJson {
      locationId = dialogInfo.locationId ?? ""
    };

    if (dialogInfo.speakers != null) {
      for (var i = 0; i < dialogInfo.speakers.Count; i++) {
        var speaker = dialogInfo.speakers[i];
        if (speaker == null) continue;

        var exportedSpeaker = new ExportedDialogSpeakerJson {
          speakerId = speaker.speakerId ?? "",
          speakerName = speaker.speakerName ?? "",
          portraitLibraryName = speaker.portraitLibraryName ?? "",
          speakerSide = (int)speaker.speakerSide
        };

        if (speaker.lines != null) {
          for (var lineIndex = 0; lineIndex < speaker.lines.Count; lineIndex++) {
            var line = speaker.lines[lineIndex];
            if (line == null) continue;
            exportedSpeaker.lines.Add(new ExportedDialogLineJson {
              lineNumber = line.lineNumber,
              text = line.text ?? "",
              emotion = line.emotion ?? "",
              trigger = line.trigger ?? "",
              speakerId = line.speakerId ?? "",
              speakerName = line.speakerName ?? "",
              speaker = (int)line.speaker,
              avatarForm = line.avatarForm ?? "",
              otherType = (int)line.otherType,
              portraitLibraryName = line.portraitLibraryName ?? "",
              locationId = line.locationId ?? ""
            });
          }
        }

        dialogSnapshot.speakers.Add(exportedSpeaker);
      }
    }

    WriteJson(Path.Combine(pack.externalRootPath, pack.snapshotRelativePath), locationSnapshot, mode, stats, generatedFile: true);
    WriteJson(Path.Combine(pack.externalRootPath, pack.dialogSnapshotRelativePath), dialogSnapshot, mode, stats, generatedFile: true);
  }
}
#endif
