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
  public static bool AuditActivePacks(bool logResult) {
    var selection = LoadOrCreateSelectionAsset(logResult: false);
    if (selection == null) {
      Debug.LogError("[ContentPackPipeline] Audit failed: selection asset is not loadable.");
      return false;
    }

    var externalRoot = NormalizeFullPath(selection.ExternalRoot);
    var packDefinitions = BuildPackDefinitions(externalRoot);
    var packById = packDefinitions.ToDictionary(pack => pack.packId, StringComparer.OrdinalIgnoreCase);
    var selectedPackIds = selection.GetNormalizedActivePackIds();
    var activePackIds = ResolveConcreteActivePackIds(selectedPackIds, packById);
    var stageErrors = CollectStageValidationErrors(packById, activePackIds);
    var stageCodeDependencies = CollectStageCodeDependencies(packById, activePackIds);
    var packPolicyErrors = CollectActivePackPolicyValidationErrors(packById, activePackIds);
    var gameplayCoreErrors = CollectGameplayCoreValidationErrors(selection, activePackIds);
    var generatedReferenceCount = CountLegacyGeneratedReferences();

    var summary = new StringBuilder();
    summary.Append("[ContentPackPipeline] Audit summary");
    summary.Append(" external_enabled=").Append(selection.ExternalContentEnabled);
    summary.Append(" selected_packs=").Append(selectedPackIds.Count <= 0 ? "-" : string.Join(", ", selectedPackIds));
    summary.Append(" active_packs=").Append(activePackIds.Count <= 0 ? "-" : string.Join(", ", activePackIds));
    summary.Append(" external_root='").Append(externalRoot).Append("'");
    summary.Append(" registry_present=").Append(AssetDatabase.LoadAssetAtPath<ActiveContentRegistry>(ActiveRegistryAssetPath) != null);
    summary.Append(" generated_refs=").Append(generatedReferenceCount);
    summary.Append(" stage_errors=").Append(stageErrors.Count);
    summary.Append(" stage_code_refs=").Append(stageCodeDependencies.Count);
    summary.Append(" pack_policy_errors=").Append(packPolicyErrors.Count);
    summary.Append(" gameplay_core_errors=").Append(gameplayCoreErrors.Count);
    Debug.Log(summary.ToString());

    if (stageCodeDependencies.Count > 0) {
      LogInfoBucket("StageCodeRefs", stageCodeDependencies);
    }

    for (var i = 0; i < selectedPackIds.Count; i++) {
      var packId = selectedPackIds[i];
      if (!packById.TryGetValue(packId, out var pack) || pack == null) {
        Debug.LogError("[ContentPackPipeline] Audit missing pack definition. pack_id='" + packId + "'");
        continue;
      }

      Debug.Log(
        "[ContentPackPipeline] Audit pack" +
        " pack_id='" + pack.packId + "'" +
        " external_exists=" + Directory.Exists(pack.externalRootPath) +
        " manifest_exists=" + File.Exists(Path.Combine(pack.externalRootPath, ManifestFileName)) +
        " stage_exists=" + Directory.Exists(Path.GetFullPath(pack.stageAssetRoot)) +
        " stage_root='" + pack.stageAssetRoot + "'"
      );
    }

    if (stageErrors.Count > 0) {
      LogErrors("audit_stage_validation", stageErrors);
    }

    if (packPolicyErrors.Count > 0) {
      LogErrors("audit_pack_policy_validation", packPolicyErrors);
    }

    if (gameplayCoreErrors.Count > 0) {
      LogErrors("audit_gameplay_core_validation", gameplayCoreErrors);
    }

    if (generatedReferenceCount > 0 && logResult) {
      Debug.LogWarning("[ContentPackPipeline] Audit found legacy Assets/Generated references. count=" + generatedReferenceCount);
    }

    return stageErrors.Count <= 0 && packPolicyErrors.Count <= 0 && gameplayCoreErrors.Count <= 0;
  }

  static bool ValidateStagedContent(
    ContentPackSelection selection,
    Dictionary<string, PackDefinition> packById,
    List<string> activePackIds
  ) {
    var stageErrors = CollectStageValidationErrors(packById, activePackIds);
    var stageCodeDependencies = CollectStageCodeDependencies(packById, activePackIds);
    var packPolicyErrors = CollectActivePackPolicyValidationErrors(packById, activePackIds);
    var gameplayCoreErrors = CollectGameplayCoreValidationErrors(selection, activePackIds);

    if (stageErrors.Count > 0) {
      LogErrors("stage_validation", stageErrors);
    }

    if (stageCodeDependencies.Count > 0) {
      LogInfoBucket("StageCodeRefs", stageCodeDependencies);
    }

    if (packPolicyErrors.Count > 0) {
      LogErrors("stage_pack_policy_validation", packPolicyErrors);
    }

    if (gameplayCoreErrors.Count > 0) {
      LogErrors("stage_gameplay_core_validation", gameplayCoreErrors);
    }

    if (stageErrors.Count > 0 || packPolicyErrors.Count > 0 || gameplayCoreErrors.Count > 0) {
      return false;
    }

    return true;
  }

  static List<string> CollectStageValidationErrors(Dictionary<string, PackDefinition> packById, List<string> activePackIds) {
    var errors = new List<string>();
    var stageRoots = BuildActiveStageRoots(packById, activePackIds);

    for (var i = 0; i < activePackIds.Count; i++) {
      if (!packById.TryGetValue(activePackIds[i], out var pack) || pack == null) continue;
      var stagedOwnedRoots = ExpandStagedOwnedRoots(pack);
      for (var rootIndex = 0; rootIndex < stagedOwnedRoots.Count; rootIndex++) {
        ValidateStagedRoot(stagedOwnedRoots[rootIndex], stageRoots, errors);
      }
    }

    return errors;
  }

  static List<string> CollectStageCodeDependencies(Dictionary<string, PackDefinition> packById, List<string> activePackIds) {
    var codeDependencies = new List<string>();
    var stageRoots = BuildActiveStageRoots(packById, activePackIds);

    for (var i = 0; i < activePackIds.Count; i++) {
      if (!packById.TryGetValue(activePackIds[i], out var pack) || pack == null) continue;
      var stagedOwnedRoots = ExpandStagedOwnedRoots(pack);
      for (var rootIndex = 0; rootIndex < stagedOwnedRoots.Count; rootIndex++) {
        CollectCodeDependenciesForStagedRoot(stagedOwnedRoots[rootIndex], stageRoots, codeDependencies);
      }
    }

    return codeDependencies;
  }

  static List<string> CollectGameplayCoreValidationErrors(ContentPackSelection selection, List<string> activePackIds) {
    var errors = new List<string>();
    if (selection == null || !selection.ExternalContentEnabled || activePackIds == null || activePackIds.Count <= 0) {
      return errors;
    }

    if (!activePackIds.Contains(CorePackId, StringComparer.OrdinalIgnoreCase)) {
      errors.Add("Active pack selection is missing the required Core pack.");
      return errors;
    }

    ValidateGameplayCoreAssetExists(GameplayCoreAssetPaths.EsperanzaPrefabAssetPath, "player_prefab", errors);

    foreach (var projectile in Projectiles.EnumerateAll()) {
      var projectileAssetPath = NormalizeAssetPath(projectile.Value?.prefabAddress);
      if (string.IsNullOrWhiteSpace(projectileAssetPath)) {
        errors.Add("Projectile '" + projectile.Key + "' is missing a prefab asset path.");
        continue;
      }

      ValidateActivePackAssetExists(projectileAssetPath, "projectile_prefab:" + projectile.Key, activePackIds, errors);
    }

    ValidateGameplayCoreAddressable(GameplayCoreAssetPaths.EsperanzaPrefabAssetPath, "player_prefab", errors);

    foreach (var projectile in Projectiles.EnumerateAll()) {
      var projectileAssetPath = NormalizeAssetPath(projectile.Value?.prefabAddress);
      if (string.IsNullOrWhiteSpace(projectileAssetPath)) continue;
      ValidateActivePackAddressable(projectileAssetPath, "projectile_prefab:" + projectile.Key, activePackIds, errors);
    }

    return errors;
  }

  static List<string> CollectActivePackPolicyValidationErrors(
    Dictionary<string, PackDefinition> packById,
    List<string> activePackIds
  ) {
    var errors = new List<string>();
    if (packById == null || activePackIds == null || activePackIds.Count <= 0) {
      return errors;
    }

    var defaultLocationPackIds = new List<string>();
    var defaultLocationIds = new List<string>();
    var hasGameplayLocations = false;

    for (var i = 0; i < activePackIds.Count; i++) {
      if (!packById.TryGetValue(activePackIds[i], out var pack) || pack == null) {
        continue;
      }

      var ownsLocations = pack.ownedLocations != null && pack.ownedLocations.Count > 0;
      if (ownsLocations) {
        hasGameplayLocations = true;
      }

      var defaultLocationId = NormalizeToken(pack.defaultLocationId);
      if (!string.IsNullOrWhiteSpace(defaultLocationId)) {
        defaultLocationPackIds.Add(pack.packId);
        defaultLocationIds.Add(defaultLocationId);
      }

      var hasLocationSnapshot = TryReadLocationSnapshot(pack, out var locationSnapshot) && locationSnapshot != null;
      var hasDialogSnapshot = TryReadDialogSnapshot(pack, out var dialogSnapshot) && dialogSnapshot != null;

      if (ownsLocations && !hasLocationSnapshot) {
        errors.Add(
          "Active pack is missing a staged location snapshot." +
          " pack_id='" + pack.packId + "'" +
          " snapshot_path='" + NormalizeAssetPath(pack.stageAssetRoot + "/" + pack.snapshotRelativePath) + "'"
        );
      }

      var snapshotLocationId = NormalizeToken(locationSnapshot != null ? locationSnapshot.id : "");
      if (ownsLocations && hasLocationSnapshot && !PackOwnsLocation(pack, snapshotLocationId)) {
        errors.Add(
          "Active pack location snapshot resolved to an unowned location." +
          " pack_id='" + pack.packId + "'" +
          " location_id='" + snapshotLocationId + "'"
        );
      }

      if (!string.IsNullOrWhiteSpace(defaultLocationId) && ownsLocations && !PackOwnsLocation(pack, defaultLocationId)) {
        errors.Add(
          "Pack defaultLocationId does not belong to its owned locations." +
          " pack_id='" + pack.packId + "'" +
          " default_location_id='" + defaultLocationId + "'"
        );
      }

      if (hasLocationSnapshot) {
        var prefabAssetPath = NormalizeAssetPath(locationSnapshot.locationPrefabData != null ? locationSnapshot.locationPrefabData.AssetPath : "");
        if (string.IsNullOrWhiteSpace(prefabAssetPath)) {
          errors.Add(
            "Location snapshot is missing a prefab asset path." +
            " pack_id='" + pack.packId + "'" +
            " location_id='" + (string.IsNullOrWhiteSpace(snapshotLocationId) ? "-" : snapshotLocationId) + "'"
          );
        }
        else {
          var stagedPrefabAssetPath = BuildStageAssetPath(pack, prefabAssetPath);
          if (string.IsNullOrWhiteSpace(stagedPrefabAssetPath) || !File.Exists(Path.GetFullPath(stagedPrefabAssetPath))) {
            errors.Add(
              "Location snapshot prefab is missing from the staged pack." +
              " pack_id='" + pack.packId + "'" +
              " location_id='" + (string.IsNullOrWhiteSpace(snapshotLocationId) ? "-" : snapshotLocationId) + "'" +
              " prefab_path='" + prefabAssetPath + "'" +
              " staged_prefab_path='" + stagedPrefabAssetPath + "'"
            );
          }
        }

        if ((locationSnapshot.enemies == null || locationSnapshot.enemies.Count <= 0) &&
            (pack.ownedEnemyTypes == null || pack.ownedEnemyTypes.Count <= 0)) {
          errors.Add(
            "Location snapshot is missing enemy ownership." +
            " pack_id='" + pack.packId + "'" +
            " location_id='" + (string.IsNullOrWhiteSpace(snapshotLocationId) ? "-" : snapshotLocationId) + "'"
          );
        }
      }

      if (pack.dialogIds != null && pack.dialogIds.Count > 0 && !hasDialogSnapshot) {
        errors.Add(
          "Active pack is missing a staged dialog snapshot." +
          " pack_id='" + pack.packId + "'" +
          " snapshot_path='" + NormalizeAssetPath(pack.stageAssetRoot + "/" + pack.dialogSnapshotRelativePath) + "'"
        );
      }

      if (hasDialogSnapshot) {
        var dialogLocationId = NormalizeToken(dialogSnapshot.locationId);
        if (ownsLocations && !PackOwnsLocation(pack, dialogLocationId)) {
          errors.Add(
            "Dialog snapshot resolved to an unowned location." +
            " pack_id='" + pack.packId + "'" +
            " location_id='" + dialogLocationId + "'"
          );
        }

        if (hasLocationSnapshot && !string.Equals(dialogLocationId, snapshotLocationId, StringComparison.OrdinalIgnoreCase)) {
          errors.Add(
            "Location and dialog snapshots disagree on the location id." +
            " pack_id='" + pack.packId + "'" +
            " location_snapshot='" + snapshotLocationId + "'" +
            " dialog_snapshot='" + dialogLocationId + "'"
          );
        }

        ValidateDialogSnapshot(pack, dialogSnapshot, errors);
      }


    }

    if (hasGameplayLocations && defaultLocationPackIds.Count <= 0) {
      errors.Add("Active gameplay packs are missing a defaultLocationId policy.");
    }

    if (defaultLocationPackIds.Count > 1) {
      errors.Add(
        "Multiple active packs define defaultLocationId." +
        " pack_ids='" + string.Join(", ", defaultLocationPackIds) + "'" +
        " location_ids='" + string.Join(", ", defaultLocationIds) + "'"
      );
    }

    return errors;
  }

  static void ValidateDialogSnapshot(PackDefinition pack, LocationDialogDefinition dialogSnapshot, List<string> errors) {
    if (pack == null || dialogSnapshot == null || errors == null) {
      return;
    }

    var dialogLocationId = NormalizeToken(dialogSnapshot.locationId);
    if (string.IsNullOrWhiteSpace(dialogLocationId)) {
      errors.Add(
        "Dialog snapshot is missing a location id." +
        " pack_id='" + pack.packId + "'"
      );
      return;
    }

    if (dialogSnapshot.speakers == null || dialogSnapshot.speakers.Count <= 0) {
      errors.Add(
        "Dialog snapshot is missing speaker chains." +
        " pack_id='" + pack.packId + "'" +
        " location_id='" + dialogLocationId + "'"
      );
      return;
    }

    var seenLocationLineNumbers = new HashSet<int>();
    for (var speakerIndex = 0; speakerIndex < dialogSnapshot.speakers.Count; speakerIndex++) {
      ValidateDialogSpeaker(pack, dialogLocationId, dialogSnapshot.speakers[speakerIndex], seenLocationLineNumbers, errors);
    }
  }

  static void ValidateDialogSpeaker(
    PackDefinition pack,
    string dialogLocationId,
    DialogSpeakerDefinition speaker,
    HashSet<int> seenLocationLineNumbers,
    List<string> errors
  ) {
    var speakerId = NormalizeToken(speaker != null ? speaker.speakerId : "");
    if (string.IsNullOrWhiteSpace(speakerId)) {
      errors.Add(
        "Dialog snapshot contains a speaker with no speaker id." +
        " pack_id='" + pack.packId + "'" +
        " location_id='" + dialogLocationId + "'"
      );
      return;
    }

    if (speaker == null || speaker.lines == null || speaker.lines.Count <= 0) {
      errors.Add(
        "Dialog speaker chain is empty." +
        " pack_id='" + pack.packId + "'" +
        " location_id='" + dialogLocationId + "'" +
        " speaker_id='" + speakerId + "'"
      );
      return;
    }

    var seenLineNumbers = new HashSet<int>();
    for (var lineIndex = 0; lineIndex < speaker.lines.Count; lineIndex++) {
      ValidateDialogLine(pack, dialogLocationId, speakerId, speaker.lines[lineIndex], seenLineNumbers, seenLocationLineNumbers, errors);
    }
  }

  static void ValidateDialogLine(
    PackDefinition pack,
    string dialogLocationId,
    string speakerId,
    GameplayDialogController.GameplayDialogNode line,
    HashSet<int> seenLineNumbers,
    HashSet<int> seenLocationLineNumbers,
    List<string> errors
  ) {
    if (line == null) {
      errors.Add(
        "Dialog speaker chain contains a null line." +
        " pack_id='" + pack.packId + "'" +
        " location_id='" + dialogLocationId + "'" +
        " speaker_id='" + speakerId + "'"
      );
      return;
    }

    var lineNumber = Mathf.Max(line.lineNumber, 0);
    if (lineNumber <= 0) {
      errors.Add(
        "Dialog line number must be greater than zero." +
        " pack_id='" + pack.packId + "'" +
        " location_id='" + dialogLocationId + "'" +
        " speaker_id='" + speakerId + "'"
      );
    }
    else if (seenLineNumbers != null && !seenLineNumbers.Add(lineNumber)) {
      errors.Add(
        "Dialog speaker chain reuses a seen line number." +
        " pack_id='" + pack.packId + "'" +
        " location_id='" + dialogLocationId + "'" +
        " speaker_id='" + speakerId + "'" +
        " line_number='" + lineNumber + "'"
      );
    }

    if (lineNumber > 0 && seenLocationLineNumbers != null && !seenLocationLineNumbers.Add(lineNumber)) {
      errors.Add(
        "Dialog location reuses a line number across speakers." +
        " pack_id='" + pack.packId + "'" +
        " location_id='" + dialogLocationId + "'" +
        " speaker_id='" + speakerId + "'" +
        " line_number='" + lineNumber + "'"
      );
    }

    if (string.IsNullOrWhiteSpace(line.text)) {
      errors.Add(
        "Dialog line is missing text." +
        " pack_id='" + pack.packId + "'" +
        " location_id='" + dialogLocationId + "'" +
        " speaker_id='" + speakerId + "'" +
        " line_number='" + lineNumber + "'"
      );
    }
  }
  static bool PackOwnsLocation(PackDefinition pack, string locationId) {
    var normalizedLocationId = NormalizeToken(locationId);
    if (pack == null || string.IsNullOrWhiteSpace(normalizedLocationId) || pack.ownedLocations == null) {
      return false;
    }

    for (var i = 0; i < pack.ownedLocations.Count; i++) {
      if (string.Equals(NormalizeToken(pack.ownedLocations[i]), normalizedLocationId, StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
  }

  static void ValidateGameplayCoreAssetExists(string projectAssetPath, string label, List<string> errors) {
    var stagedAssetPath = BuildCoreStageAssetPath(projectAssetPath);
    if (string.IsNullOrWhiteSpace(stagedAssetPath) || !File.Exists(Path.GetFullPath(stagedAssetPath))) {
      errors?.Add(
        "Missing staged gameplay core asset." +
        " label='" + label + "'" +
        " project_path='" + NormalizeAssetPath(projectAssetPath) + "'" +
        " staged_path='" + stagedAssetPath + "'"
      );
    }
  }

  static void ValidateGameplayCoreAddressable(string projectAssetPath, string label, List<string> errors) {
    var stagedAssetPath = BuildCoreStageAssetPath(projectAssetPath);
    if (string.IsNullOrWhiteSpace(stagedAssetPath)) return;

    var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
    if (settings == null) {
      errors?.Add("Addressables settings were not found while validating gameplay core asset '" + label + "'.");
      return;
    }

    var guid = AssetDatabase.AssetPathToGUID(stagedAssetPath);
    if (string.IsNullOrWhiteSpace(guid)) {
      errors?.Add(
        "Missing GUID for staged gameplay core asset." +
        " label='" + label + "'" +
        " staged_path='" + stagedAssetPath + "'"
      );
      return;
    }

    var entry = settings.FindAssetEntry(guid);
    if (entry == null) {
      errors?.Add(
        "Missing Addressables entry for staged gameplay core asset." +
        " label='" + label + "'" +
        " staged_path='" + stagedAssetPath + "'"
      );
      return;
    }

    if (!string.Equals(entry.address, stagedAssetPath, StringComparison.Ordinal)) {
      errors?.Add(
        "Addressables entry address mismatch for staged gameplay core asset." +
        " label='" + label + "'" +
        " staged_path='" + stagedAssetPath + "'" +
        " address='" + entry.address + "'"
      );
    }
  }

  static void ValidateActivePackAssetExists(string projectAssetPath, string label, List<string> activePackIds, List<string> errors) {
    var stagedAssetPath = ResolveStagedAssetPathForActivePacks(projectAssetPath, activePackIds);
    if (string.IsNullOrWhiteSpace(stagedAssetPath) || !File.Exists(Path.GetFullPath(stagedAssetPath))) {
      errors?.Add(
        "Missing staged active-pack asset." +
        " label='" + label + "'" +
        " project_path='" + NormalizeAssetPath(projectAssetPath) + "'" +
        " staged_path='" + stagedAssetPath + "'"
      );
    }
  }

  static void ValidateActivePackAddressable(string projectAssetPath, string label, List<string> activePackIds, List<string> errors) {
    var stagedAssetPath = ResolveStagedAssetPathForActivePacks(projectAssetPath, activePackIds);
    if (string.IsNullOrWhiteSpace(stagedAssetPath)) return;

    var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
    if (settings == null) {
      errors?.Add("Addressables settings were not found while validating active-pack asset '" + label + "'.");
      return;
    }

    var guid = AssetDatabase.AssetPathToGUID(stagedAssetPath);
    if (string.IsNullOrWhiteSpace(guid)) {
      errors?.Add(
        "Missing GUID for staged active-pack asset." +
        " label='" + label + "'" +
        " staged_path='" + stagedAssetPath + "'"
      );
      return;
    }

    var entry = settings.FindAssetEntry(guid);
    if (entry == null) {
      errors?.Add(
        "Missing Addressables entry for staged active-pack asset." +
        " label='" + label + "'" +
        " staged_path='" + stagedAssetPath + "'"
      );
      return;
    }

    if (!string.Equals(entry.address, stagedAssetPath, StringComparison.Ordinal)) {
      errors?.Add(
        "Addressables entry address mismatch for staged active-pack asset." +
        " label='" + label + "'" +
        " staged_path='" + stagedAssetPath + "'" +
        " address='" + entry.address + "'"
      );
    }
  }

  static string BuildCoreStageAssetPath(string projectAssetPath) {
    var normalizedProjectPath = NormalizeAssetPath(projectAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedProjectPath) ||
        !normalizedProjectPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) {
      return "";
    }

    return NormalizeAssetPath(StageCoreAssetPath + "/" + normalizedProjectPath.Substring("Assets/".Length));
  }

  static string ResolveStagedAssetPathForActivePacks(string projectAssetPath, List<string> activePackIds) {
    var normalizedProjectPath = NormalizeAssetPath(projectAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedProjectPath) ||
        !normalizedProjectPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
        activePackIds == null ||
        activePackIds.Count <= 0) {
      return "";
    }

    var relativePath = normalizedProjectPath.Substring("Assets/".Length);
    for (var i = 0; i < activePackIds.Count; i++) {
      var stageRoot = GetStageAssetRoot(activePackIds[i]);
      if (string.IsNullOrWhiteSpace(stageRoot)) continue;
      var stagedAssetPath = NormalizeAssetPath(stageRoot + "/" + relativePath);
      if (File.Exists(Path.GetFullPath(stagedAssetPath))) {
        return stagedAssetPath;
      }
    }

    return "";
  }

  static HashSet<string> BuildActiveStageRoots(Dictionary<string, PackDefinition> packById, List<string> activePackIds) {
    var stageRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
      NormalizeAssetPath(StageCoreAssetPath)
    };
    if (packById == null || activePackIds == null) return stageRoots;

    for (var i = 0; i < activePackIds.Count; i++) {
      if (packById.TryGetValue(activePackIds[i], out var pack) && pack != null) {
        stageRoots.Add(NormalizeAssetPath(pack.stageAssetRoot));
      }
    }

    return stageRoots;
  }

  static List<string> ExpandStagedOwnedRoots(PackDefinition pack) {
    var result = new List<string>();
    if (pack == null || pack.ownedRoots == null) return result;

    for (var i = 0; i < pack.ownedRoots.Count; i++) {
      var stageRoot = BuildStageAssetPath(pack, pack.ownedRoots[i]);
      var expanded = ExpandProjectRoots(new[] { stageRoot }, errors: null);
      for (var assetIndex = 0; assetIndex < expanded.Count; assetIndex++) {
        AddUniquePath(result, expanded[assetIndex]);
      }
    }

    return result;
  }

  static void ValidateStagedRoot(string stagedAssetPath, HashSet<string> stageRoots, List<string> errors) {
    if (string.IsNullOrWhiteSpace(stagedAssetPath) || stageRoots == null || errors == null) return;
    if (!File.Exists(Path.GetFullPath(stagedAssetPath))) return;

    var dependencies = AssetDatabase.GetDependencies(new[] { stagedAssetPath }, true);
    for (var i = 0; i < dependencies.Length; i++) {
      var dependency = NormalizeAssetPath(dependencies[i]);
      if (string.IsNullOrWhiteSpace(dependency)) continue;
      if (!dependency.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) continue;
      if (AssetDatabase.IsValidFolder(dependency)) continue;
      if (ShouldIgnoreDependency(dependency)) continue;
      ValidateDependencyUnderStageRoots(stagedAssetPath, dependency, stageRoots, errors);
    }

    var includeDependencies = CollectLocalTextIncludeDependencies(stagedAssetPath, errors);
    for (var i = 0; i < includeDependencies.Count; i++) {
      ValidateDependencyUnderStageRoots(stagedAssetPath, includeDependencies[i], stageRoots, errors);
    }
  }

  static void CollectCodeDependenciesForStagedRoot(string stagedAssetPath, HashSet<string> stageRoots, List<string> output) {
    if (string.IsNullOrWhiteSpace(stagedAssetPath) || stageRoots == null || output == null) return;
    if (!File.Exists(Path.GetFullPath(stagedAssetPath))) return;

    var dependencies = AssetDatabase.GetDependencies(new[] { stagedAssetPath }, true);
    for (var i = 0; i < dependencies.Length; i++) {
      var dependency = NormalizeAssetPath(dependencies[i]);
      if (string.IsNullOrWhiteSpace(dependency) ||
          string.Equals(dependency, stagedAssetPath, StringComparison.OrdinalIgnoreCase) ||
          !dependency.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
          AssetDatabase.IsValidFolder(dependency) ||
          !IsCodeDependency(dependency)) {
        continue;
      }

      output.Add("staged_asset='" + stagedAssetPath + "' dependency='" + dependency + "'");
      break;
    }
  }

  static void ValidateDependencyUnderStageRoots(
    string stagedAssetPath,
    string dependency,
    HashSet<string> stageRoots,
    List<string> errors
  ) {
    if (string.IsNullOrWhiteSpace(dependency) || stageRoots == null || errors == null) return;
    if (IsCodeDependency(dependency)) return;

    var isStaged = false;
    foreach (var stageRoot in stageRoots) {
      if (string.IsNullOrWhiteSpace(stageRoot)) continue;
      if (dependency.StartsWith(stageRoot + "/", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(stageRoot, dependency, StringComparison.OrdinalIgnoreCase)) {
        isStaged = true;
        break;
      }
    }

    if (isStaged) return;

    errors.Add(
      "Staged asset leaked an original project dependency." +
      " staged_asset='" + stagedAssetPath + "'" +
      " dependency='" + dependency + "'"
    );
  }
}
#endif
