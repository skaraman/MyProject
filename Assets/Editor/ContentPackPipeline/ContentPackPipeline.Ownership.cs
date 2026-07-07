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
  static OwnershipAnalysisReport AnalyzeOwnershipAndDuplicates(bool logResult) {
    var selection = LoadOrCreateSelectionAsset(logResult: false);
    var externalRoot = selection != null ? NormalizeFullPath(selection.ExternalRoot) : NormalizeFullPath(DefaultExternalRoot);
    var report = new OwnershipAnalysisReport {
      authoritativeExternalRoot = externalRoot,
      legacyGeneratedReferenceCount = CountLegacyGeneratedReferences(),
      spriteDuplicateCount = CountSpriteExternalDuplicates(externalRoot)
    };

    var packDefinitions = BuildPackDefinitions(externalRoot);
    for (var i = 0; i < packDefinitions.Count; i++) {
      AnalyzePackOwnership(packDefinitions[i], report);
    }

    var stageRoots = BuildStageRoots(packDefinitions);
    var contentPackOwnedRoots = BuildContentPackOwnedAssetRoots(packDefinitions);
    var mainBuildAssets = BuildMainBuildAssetDependencies(stageRoots, contentPackOwnedRoots);

    report.stagedProjectTreeDependencyCount = CountStageDependenciesOutsideStageRoots(packDefinitions, mainBuildAssets);
    report.stagedDependencyLeaks.Clear();
    CollectStageDependencyLeaks(packDefinitions, report.stagedDependencyLeaks, mainBuildAssets);
    report.mainBuildDependencyCount = CountStageMainBuildDependencies(packDefinitions, mainBuildAssets);
    report.mainBuildDependencies.Clear();
    CollectStageMainBuildDependencies(packDefinitions, report.mainBuildDependencies, mainBuildAssets);
    report.stagedCodeDependencyCount = CountStageCodeDependenciesOutsideStageRoots(packDefinitions);
    report.stagedCodeDependencies.Clear();
    CollectStageCodeDependencies(packDefinitions, report.stagedCodeDependencies);
    report.ownershipViolationCount =
      report.coreFindings.Count +
      report.formFindings.Count +
      report.gearFindings.Count +
      report.sliceFindings.Count +
      report.episodeFindings.Count +
      report.legacyFindings.Count +
      report.unknownFindings.Count;

    if (logResult) {
      LogOwnershipAnalysisReport(report);
    }

    return report;
  }

  static bool AuditLegacyDependencies(bool logResult) {
    return AuditLegacyDependencies(report: null, logResult);
  }

  static bool AuditLegacyDependencies(OwnershipAnalysisReport report, bool logResult) {
    report ??= AnalyzeOwnershipAndDuplicates(logResult);
    var auditOk = AuditActivePacks(logResult);
    var analysisOk =
      report.legacyGeneratedReferenceCount <= 0 &&
      report.stagedProjectTreeDependencyCount <= 0 &&
      report.ownershipViolationCount <= 0;

    if (logResult && report.spriteDuplicateCount > 0) {
      Debug.Log(
        "[ContentPackPipeline] Duplicate sprite assets remain as transition debt. " +
        "duplicate_assets=" + report.spriteDuplicateCount +
        " duplicate assets are reported but do not block the migration pass.");
    }

    return auditOk && analysisOk;
  }

  static int CountLegacyGeneratedReferences() {
    var count = CountTextOccurrences("Assets/AddressableAssetsData/AssetGroups/SpriteRuntimeIndex.asset", "Assets/Generated/");

    var runtimeIndexRoot = "Assets/Sprites/SpriteLibraries/RuntimeIndex";
    var runtimeIndexFullPath = Path.GetFullPath(runtimeIndexRoot);
    if (!Directory.Exists(runtimeIndexFullPath)) return count;

    var files = Directory.GetFiles(runtimeIndexFullPath, "*", SearchOption.AllDirectories);
    for (var i = 0; i < files.Length; i++) {
      count += CountTextOccurrences(ToProjectAssetPath(files[i]), "Assets/Generated/");
    }

    return count;
  }

  static int CountSpriteExternalDuplicates(string authoritativeExternalRoot) {
    var spritesRoot = NormalizeFullPath("Assets/Sprites");
    var externalRoot = NormalizeFullPath(authoritativeExternalRoot);
    if (!Directory.Exists(spritesRoot) || !Directory.Exists(externalRoot)) return 0;

    var externalSpriteRoots = BuildPackDefinitions(externalRoot)
      .Where(pack => pack != null && !string.IsNullOrWhiteSpace(pack.externalRootPath))
      .Select(pack => NormalizeFullPath(Path.Combine(pack.externalRootPath, "Sprites")))
      .Where(Directory.Exists)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();
    if (externalSpriteRoots.Count <= 0) return 0;

    var files = Directory.GetFiles(spritesRoot, "*", SearchOption.AllDirectories);
    var duplicateCount = 0;
    for (var i = 0; i < files.Length; i++) {
      var fullPath = NormalizeFullPath(files[i]);
      if (Directory.Exists(fullPath)) continue;
      var relativePath = fullPath.Substring(spritesRoot.Length).TrimStart('/');
      if (string.IsNullOrWhiteSpace(relativePath)) continue;

      for (var rootIndex = 0; rootIndex < externalSpriteRoots.Count; rootIndex++) {
        var externalMatch = NormalizeFullPath(Path.Combine(externalSpriteRoots[rootIndex], relativePath));
        if (!File.Exists(externalMatch)) continue;
        duplicateCount++;
        break;
      }
    }

    return duplicateCount;
  }

  static void AnalyzePackOwnership(PackDefinition pack, OwnershipAnalysisReport report) {
    if (pack == null || report == null) return;

    var findings = GetOwnershipFindingsBucket(pack, report);
    if (findings == null) return;

    if (pack.ownedRoots == null || pack.ownedRoots.Count <= 0) {
      findings.Add("Pack has no declared owned roots. pack_id='" + pack.packId + "'");
    }

    if (string.Equals(pack.kind, "slice", StringComparison.OrdinalIgnoreCase)) {
      if (pack.ownedLocations == null || pack.ownedLocations.Count <= 0) {
        findings.Add("Slice has no owned location. pack_id='" + pack.packId + "'");
      }
      if (pack.dialogIds == null || pack.dialogIds.Count <= 0) {
        findings.Add("Slice has no dialog ownership declared. pack_id='" + pack.packId + "'");
      }
    }

  }

  static List<string> GetOwnershipFindingsBucket(PackDefinition pack, OwnershipAnalysisReport report) {
    if (pack == null || report == null) return null;
    if (string.Equals(pack.packId, CorePackId, StringComparison.OrdinalIgnoreCase)) return report.coreFindings;
    if (string.Equals(pack.kind, "form", StringComparison.OrdinalIgnoreCase)) return report.formFindings;
    if (string.Equals(pack.kind, "gear", StringComparison.OrdinalIgnoreCase)) return report.gearFindings;
    if (string.Equals(pack.kind, "slice", StringComparison.OrdinalIgnoreCase)) return report.sliceFindings;
    if (string.Equals(pack.kind, "episode", StringComparison.OrdinalIgnoreCase)) return report.episodeFindings;
    return report.unknownFindings;
  }

  static int CountStageDependenciesOutsideStageRoots(
    List<PackDefinition> packDefinitions,
    HashSet<string> mainBuildAssets
  ) {
    var stageRoots = BuildStageRoots(packDefinitions);
    var stageRootFullPath = GetPhysicalPath(StageRootAssetPath);
    if (!Directory.Exists(stageRootFullPath)) return 0;

    var files = Directory.GetFiles(stageRootFullPath, "*", SearchOption.AllDirectories);
    var count = 0;
    for (var i = 0; i < files.Length; i++) {
      var assetPath = ToProjectAssetPath(files[i]);
      if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) continue;
      if (AssetDatabase.IsValidFolder(assetPath)) continue;
      var dependencies = AssetDatabase.GetDependencies(new[] { assetPath }, true);
      for (var dependencyIndex = 0; dependencyIndex < dependencies.Length; dependencyIndex++) {
        var dependency = NormalizeAssetPath(dependencies[dependencyIndex]);
        if (string.IsNullOrWhiteSpace(dependency) ||
            string.Equals(dependency, assetPath, StringComparison.OrdinalIgnoreCase) ||
            !dependency.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
            IsCodeDependency(dependency)) {
          continue;
        }

        var underStage = false;
        foreach (var stageRoot in stageRoots) {
          if (dependency.StartsWith(stageRoot + "/", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(dependency, stageRoot, StringComparison.OrdinalIgnoreCase)) {
            underStage = true;
            break;
          }
        }

        if (!underStage) {
          if (IsMainBuildAssetDependency(dependency, mainBuildAssets)) {
            continue;
          }

          count++;
          break;
        }
      }
    }

    return count;
  }

  static int CountStageCodeDependenciesOutsideStageRoots(List<PackDefinition> packDefinitions) {
    var stageRoots = BuildStageRoots(packDefinitions);
    var stageRootFullPath = GetPhysicalPath(StageRootAssetPath);
    if (!Directory.Exists(stageRootFullPath)) return 0;

    var files = Directory.GetFiles(stageRootFullPath, "*", SearchOption.AllDirectories);
    var count = 0;
    for (var i = 0; i < files.Length; i++) {
      var assetPath = ToProjectAssetPath(files[i]);
      if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) continue;
      if (AssetDatabase.IsValidFolder(assetPath)) continue;
      var dependencies = AssetDatabase.GetDependencies(new[] { assetPath }, true);
      for (var dependencyIndex = 0; dependencyIndex < dependencies.Length; dependencyIndex++) {
        var dependency = NormalizeAssetPath(dependencies[dependencyIndex]);
        if (string.IsNullOrWhiteSpace(dependency) ||
            string.Equals(dependency, assetPath, StringComparison.OrdinalIgnoreCase) ||
            !dependency.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
            !IsCodeDependency(dependency)) {
          continue;
        }

        if (IsUnderStageRoots(dependency, stageRoots)) continue;

        count++;
        break;
      }
    }

    return count;
  }

  static void CollectStageDependencyLeaks(
    List<PackDefinition> packDefinitions,
    List<string> output,
    HashSet<string> mainBuildAssets
  ) {
    if (output == null) return;

    var stageRoots = BuildStageRoots(packDefinitions);
    var stageRootFullPath = GetPhysicalPath(StageRootAssetPath);
    if (!Directory.Exists(stageRootFullPath)) return;

    var files = Directory.GetFiles(stageRootFullPath, "*", SearchOption.AllDirectories);
    for (var i = 0; i < files.Length; i++) {
      var assetPath = ToProjectAssetPath(files[i]);
      if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) continue;
      if (AssetDatabase.IsValidFolder(assetPath)) continue;
      var dependencies = AssetDatabase.GetDependencies(new[] { assetPath }, true);
      for (var dependencyIndex = 0; dependencyIndex < dependencies.Length; dependencyIndex++) {
        var dependency = NormalizeAssetPath(dependencies[dependencyIndex]);
        if (string.IsNullOrWhiteSpace(dependency) ||
            string.Equals(dependency, assetPath, StringComparison.OrdinalIgnoreCase) ||
            !dependency.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
            IsCodeDependency(dependency)) {
          continue;
        }

        if (IsUnderStageRoots(dependency, stageRoots)) continue;
        if (IsMainBuildAssetDependency(dependency, mainBuildAssets)) continue;

        output.Add("staged_asset='" + assetPath + "' dependency='" + dependency + "'");
        break;
      }
    }
  }

  static int CountStageMainBuildDependencies(
    List<PackDefinition> packDefinitions,
    HashSet<string> mainBuildAssets
  ) {
    var output = new List<string>();
    CollectStageMainBuildDependencies(packDefinitions, output, mainBuildAssets);
    return output.Count;
  }

  static void CollectStageMainBuildDependencies(
    List<PackDefinition> packDefinitions,
    List<string> output,
    HashSet<string> mainBuildAssets
  ) {
    if (output == null) return;
    if (mainBuildAssets == null || mainBuildAssets.Count <= 0) return;

    var stageRoots = BuildStageRoots(packDefinitions);
    var stageRootFullPath = GetPhysicalPath(StageRootAssetPath);
    if (!Directory.Exists(stageRootFullPath)) return;

    var files = Directory.GetFiles(stageRootFullPath, "*", SearchOption.AllDirectories);
    for (var i = 0; i < files.Length; i++) {
      var assetPath = ToProjectAssetPath(files[i]);
      if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) continue;
      if (AssetDatabase.IsValidFolder(assetPath)) continue;

      var dependencies = AssetDatabase.GetDependencies(new[] { assetPath }, true);
      for (var dependencyIndex = 0; dependencyIndex < dependencies.Length; dependencyIndex++) {
        var dependency = NormalizeAssetPath(dependencies[dependencyIndex]);
        if (string.IsNullOrWhiteSpace(dependency)) continue;
        if (string.Equals(dependency, assetPath, StringComparison.OrdinalIgnoreCase)) continue;
        if (!dependency.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) continue;
        if (AssetDatabase.IsValidFolder(dependency)) continue;
        if (IsCodeDependency(dependency)) continue;
        if (IsUnderStageRoots(dependency, stageRoots)) continue;
        if (!IsMainBuildAssetDependency(dependency, mainBuildAssets)) continue;

        output.Add("staged_asset='" + assetPath + "' main_build_dependency='" + dependency + "'");
        break;
      }
    }
  }

  static void CollectStageCodeDependencies(List<PackDefinition> packDefinitions, List<string> output) {
    if (output == null) return;

    var stageRoots = BuildStageRoots(packDefinitions);
    var stageRootFullPath = GetPhysicalPath(StageRootAssetPath);
    if (!Directory.Exists(stageRootFullPath)) return;

    var files = Directory.GetFiles(stageRootFullPath, "*", SearchOption.AllDirectories);
    for (var i = 0; i < files.Length; i++) {
      var assetPath = ToProjectAssetPath(files[i]);
      if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) continue;
      if (AssetDatabase.IsValidFolder(assetPath)) continue;
      var dependencies = AssetDatabase.GetDependencies(new[] { assetPath }, true);
      for (var dependencyIndex = 0; dependencyIndex < dependencies.Length; dependencyIndex++) {
        var dependency = NormalizeAssetPath(dependencies[dependencyIndex]);
        if (string.IsNullOrWhiteSpace(dependency) ||
            string.Equals(dependency, assetPath, StringComparison.OrdinalIgnoreCase) ||
            !dependency.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
            !IsCodeDependency(dependency)) {
          continue;
        }

        if (IsUnderStageRoots(dependency, stageRoots)) continue;

        output.Add("staged_asset='" + assetPath + "' dependency='" + dependency + "'");
        break;
      }
    }
  }

  static HashSet<string> BuildStageRoots(List<PackDefinition> packDefinitions) {
    var stageRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
      NormalizeAssetPath(StageCoreAssetPath)
    };
    if (packDefinitions == null) return stageRoots;

    for (var i = 0; i < packDefinitions.Count; i++) {
      var pack = packDefinitions[i];
      if (pack == null || string.IsNullOrWhiteSpace(pack.stageAssetRoot)) continue;
      stageRoots.Add(NormalizeAssetPath(pack.stageAssetRoot));
    }

    return stageRoots;
  }

  static bool IsUnderStageRoots(string dependency, HashSet<string> stageRoots) {
    if (string.IsNullOrWhiteSpace(dependency) || stageRoots == null || stageRoots.Count <= 0) return false;
    foreach (var stageRoot in stageRoots) {
      if (dependency.StartsWith(stageRoot + "/", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(dependency, stageRoot, StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
  }

  static void LogOwnershipAnalysisReport(OwnershipAnalysisReport report) {
    if (report == null) return;

    Debug.Log(
      "[ContentPackPipeline] [TransitionAnalysis] legacy_generated_refs=" + report.legacyGeneratedReferenceCount +
      " sprite_duplicates=" + report.spriteDuplicateCount +
      " staged_project_tree_dependencies=" + report.stagedProjectTreeDependencyCount +
      " staged_code_dependencies=" + report.stagedCodeDependencyCount +
      " main_build_dependencies=" + report.mainBuildDependencyCount +
      " ownership_findings=" + report.ownershipViolationCount +
      " placeholder_exemptions=" + report.placeholderExemptionCount +
      " authoritative_external_root='" + NormalizeFullPath(report.authoritativeExternalRoot) + "'" +
      " stage_root='" + NormalizeAssetPath(StageRootAssetPath) + "'"
    );
    LogFindingBucket("Core", report.coreFindings);
    LogFindingBucket("Form", report.formFindings);
    LogFindingBucket("Gear", report.gearFindings);
    LogFindingBucket("Slice", report.sliceFindings);
    LogFindingBucket("Episode", report.episodeFindings);
    LogFindingBucket("Legacy/Unknown", report.legacyFindings.Concat(report.unknownFindings).ToList());
    LogInfoBucket("Placeholder", report.placeholderFindings);
    LogInfoBucket("StageLeaks", report.stagedDependencyLeaks);
    LogInfoBucket("StageCodeRefs", report.stagedCodeDependencies);
    LogInfoBucket("MainBuildDeps", report.mainBuildDependencies);
  }

  static void LogFindingBucket(string label, List<string> findings) {
    if (findings == null || findings.Count <= 0) return;
    for (var i = 0; i < findings.Count; i++) {
      Debug.LogWarning("[ContentPackPipeline] [TransitionAnalysis][" + label + "] " + findings[i]);
    }
  }

  static void LogInfoBucket(string label, List<string> findings) {
    if (findings == null || findings.Count <= 0) return;
    for (var i = 0; i < findings.Count; i++) {
      Debug.Log("[ContentPackPipeline] [TransitionAnalysis][" + label + "] " + findings[i]);
    }
  }
}
#endif
