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
  static List<AssignedAsset> AssignPackAssets(List<PackDefinition> packDefinitions, List<string> errors) {
    var result = new List<AssignedAsset>();
    for (var packIndex = 0; packIndex < packDefinitions.Count; packIndex++) {
      var pack = packDefinitions[packIndex];
      if (pack == null || pack.assetDependencies == null) {
        continue;
      }

      for (var i = 0; i < pack.assetDependencies.Count; i++) {
        var assetPath = NormalizeAssetPath(pack.assetDependencies[i]);
        if (string.IsNullOrWhiteSpace(assetPath)) {
          continue;
        }
        if (assetPath.StartsWith("Assets/Generated/", StringComparison.OrdinalIgnoreCase)) {
          errors.Add(
            "Generated assets are not allowed in external packs." +
            " pack_id='" + pack.packId + "'" +
            " asset='" + assetPath + "'"
          );
          continue;
        }

        if (!File.Exists(GetPhysicalPath(assetPath))) {
          errors.Add(
            "Asset does not exist and cannot be exported." +
            " pack_id='" + pack.packId + "'" +
            " asset='" + assetPath + "'"
          );
          continue;
        }

        var originalGuid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrWhiteSpace(originalGuid)) {
          errors.Add(
            "Asset is missing a GUID and cannot be exported." +
            " pack_id='" + pack.packId + "'" +
            " asset='" + assetPath + "'"
          );
          continue;
        }

        var relativePath = ResolveExportRelativePath(pack, assetPath);
        result.Add(new AssignedAsset {
          assetPath = assetPath,
          originalGuid = originalGuid,
          newGuid = ComputeDeterministicExportGuid(pack.packId, assetPath),
          packId = pack.packId,
          externalAssetPath = NormalizeFullPath(Path.Combine(pack.externalRootPath, relativePath)),
          stageAssetPath = NormalizeAssetPath(pack.stageAssetRoot + "/" + relativePath)
        });
      }
    }

    return result;
  }

  static void WriteAssignedAssets(
    List<AssignedAsset> assignedAssets,
    List<string> errors,
    TransitionPipelineMode mode,
    ExportSyncStats stats
  ) {
    var guidMapsByPackId = BuildGuidMapsByPackId(assignedAssets);
    var orderedAssets = assignedAssets
      .OrderBy(asset => asset.externalAssetPath, StringComparer.OrdinalIgnoreCase)
      .ToList();

    for (var i = 0; i < orderedAssets.Count; i++) {
      var assigned = orderedAssets[i];
      if (!guidMapsByPackId.TryGetValue(assigned.packId, out var guidMap)) {
        errors.Add("Failed to build pack GUID map. pack_id='" + assigned.packId + "'.");
        continue;
      }

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

  static void ApplyAssignedRuntimeAddressMetadata(
    List<PackDefinition> packDefinitions,
    List<AssignedAsset> assignedAssets
  ) {
    if (packDefinitions == null) return;

    var packById = new Dictionary<string, PackDefinition>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < packDefinitions.Count; i++) {
      var pack = packDefinitions[i];
      if (pack == null) continue;

      pack.exportedAddresses.Clear();
      if (string.IsNullOrWhiteSpace(pack.packId)) continue;
      if (packById.ContainsKey(pack.packId)) continue;

      packById.Add(pack.packId, pack);
    }

    if (assignedAssets == null || assignedAssets.Count <= 0) return;

    var orderedAssets = assignedAssets
      .OrderBy(asset => asset.packId, StringComparer.OrdinalIgnoreCase)
      .ThenBy(asset => asset.stageAssetPath, StringComparer.OrdinalIgnoreCase)
      .ToList();

    for (var i = 0; i < orderedAssets.Count; i++) {
      var assigned = orderedAssets[i];
      if (assigned == null) continue;
      if (!packById.TryGetValue(assigned.packId, out var pack) || pack == null) continue;

      pack.exportedAddresses.Add(new ContentPackExportedAddressJson {
        sourceAssetPath = NormalizeAssetPath(assigned.assetPath),
        assetPath = NormalizeAssetPath(assigned.stageAssetPath),
        address = NormalizeAssetPath(assigned.stageAssetPath)
      });
    }
  }

  static Dictionary<string, Dictionary<string, string>> BuildGuidMapsByPackId(
    List<AssignedAsset> assignedAssets
  ) {
    var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < assignedAssets.Count; i++) {
      var assigned = assignedAssets[i];
      if (!result.TryGetValue(assigned.packId, out var guidMap)) {
        guidMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        result.Add(assigned.packId, guidMap);
      }

      guidMap[assigned.originalGuid] = assigned.newGuid;
    }

    return result;
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

    if (mode == TransitionPipelineMode.Smart &&
        File.Exists(targetFullPath) &&
        FilesHaveSameBinaryHash(sourceFullPath, targetFullPath)) {
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

  static bool FilesHaveSameBinaryHash(string sourceFullPath, string targetFullPath) {
    var sourceInfo = new FileInfo(sourceFullPath);
    var targetInfo = new FileInfo(targetFullPath);
    if (sourceInfo.Length != targetInfo.Length) {
      return false;
    }

    using var sourceStream = File.OpenRead(sourceFullPath);
    using var targetStream = File.OpenRead(targetFullPath);
    using var sourceHashAlgorithm = SHA256.Create();
    using var targetHashAlgorithm = SHA256.Create();

    var sourceHash = sourceHashAlgorithm.ComputeHash(sourceStream);
    var targetHash = targetHashAlgorithm.ComputeHash(targetStream);
    if (sourceHash.Length != targetHash.Length) {
      return false;
    }

    for (var i = 0; i < sourceHash.Length; i++) {
      if (sourceHash[i] != targetHash[i]) {
        return false;
      }
    }

    return true;
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
      var writesRuntimeCatalog = false;
      if (!string.Equals(pack.packId, CorePackId, StringComparison.OrdinalIgnoreCase)) {
        writesRuntimeCatalog = PackRequiresOptionalRuntimeCatalog(pack);
      }

      var catalogPath = "";
      if (writesRuntimeCatalog) {
        catalogPath = BuildRuntimeCatalogRelativePath();
      }

      var bundleRoot = "";
      if (writesRuntimeCatalog) {
        bundleRoot = BuildRuntimeCatalogBundleRootRelativePath();
      }

      var manifest = new ContentPackManifestJson {
        packId = pack.packId,
        type = pack.kind,
        kind = pack.kind,
        catalogPath = catalogPath,
        bundleRoot = bundleRoot,
        addressPrefix = NormalizeAssetPath(pack.stageAssetRoot),
        ownedRoots = new List<string>(pack.ownedRoots),
        ownedLocations = new List<string>(pack.ownedLocations),
        ownedEnemyTypes = new List<string>(pack.ownedEnemyTypes),
        dialogIds = new List<string>(pack.dialogIds),
        dependencies = new List<string>(pack.dependencies),
        exportedAddresses = CloneExportedAddresses(pack.exportedAddresses),
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
        normalAssetPath = NormalizeAssetPath(source.normalAssetPath),
        specularAssetPath = NormalizeAssetPath(source.specularAssetPath)
      });
    }

    return result;
  }

  static List<ContentPackExportedAddressJson> CloneExportedAddresses(List<ContentPackExportedAddressJson> addresses) {
    var result = new List<ContentPackExportedAddressJson>();
    if (addresses == null) return result;

    for (var i = 0; i < addresses.Count; i++) {
      var address = addresses[i];
      if (address == null) continue;

      result.Add(new ContentPackExportedAddressJson {
        sourceAssetPath = NormalizeAssetPath(address.sourceAssetPath),
        assetPath = NormalizeAssetPath(address.assetPath),
        address = NormalizeAssetPath(address.address)
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
