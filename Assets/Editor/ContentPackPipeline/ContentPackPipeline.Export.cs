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
  static bool ExportPackSet(bool logResult, TransitionPipelineMode mode, ExportSyncStats stats) {
    var selection = LoadOrCreateSelectionAsset(logResult);
    if (selection == null) return false;

    var externalRoot = NormalizeFullPath(selection.ExternalRoot);
    Directory.CreateDirectory(externalRoot);

    var packDefinitions = BuildPackDefinitions(externalRoot);
    if (logResult) {
      LogAuthoringManifestSummary(packDefinitions);
    }
    var projectLibraries = DiscoverProjectLibraryPaths();
    var errors = new List<string>();

    for (var i = 0; i < packDefinitions.Count; i++) {
      PreparePackDependencies(packDefinitions[i], projectLibraries, errors);
    }

    if (errors.Count > 0) {
      LogErrors("export_dependency_discovery", errors);
      return false;
    }

    var assignedAssets = AssignPackAssets(packDefinitions, errors);
    if (errors.Count > 0) {
      LogErrors("export_assignment", errors);
      return false;
    }

    try {
      for (var i = 0; i < packDefinitions.Count; i++) {
        PreparePackDirectory(packDefinitions[i].externalRootPath, externalRoot, mode, stats);
      }

      WriteAssignedAssets(assignedAssets, errors, mode, stats);
      if (errors.Count > 0) {
        LogErrors("export_copy", errors);
        return false;
      }

      for (var i = 0; i < packDefinitions.Count; i++) {
        WriteGeneratedPackData(packDefinitions[i], errors, mode, stats);
      }

      if (errors.Count > 0) {
        LogErrors("export_pack_data", errors);
        return false;
      }

      for (var i = 0; i < packDefinitions.Count; i++) {
        WritePackManifest(packDefinitions[i], errors, mode, stats);
      }

      if (errors.Count > 0) {
        LogErrors("export_manifest", errors);
        return false;
      }

      if (logResult) {
        Debug.Log(
          "[ContentPackPipeline] Exported external pack content." +
          " mode='" + mode + "'" +
          " external_root='" + externalRoot + "'" +
          " pack_count=" + packDefinitions.Count +
          " asset_count=" + assignedAssets.Count +
          FormatExportStats(stats)
        );
      }

      return true;
    }
    catch (Exception ex) {
      Debug.LogError("[ContentPackPipeline] Export failed.\n" + ex);
      return false;
    }
  }

  static void LogAuthoringManifestSummary(List<PackDefinition> packDefinitions) {
    if (packDefinitions == null || packDefinitions.Count <= 0) return;

    for (var i = 0; i < packDefinitions.Count; i++) {
      var pack = packDefinitions[i];
      if (pack == null || !pack.loadedManifest) continue;

      var sourceCount = pack.authoringSources != null ? pack.authoringSources.Count : 0;
      Debug.Log(
        "[ContentPackPipeline] Loaded content pack manifest." +
        " pack_id='" + pack.packId + "'" +
        " kind='" + pack.kind + "'" +
        " authoring_sources=" + sourceCount +
        " external_root='" + pack.externalRootPath + "'"
      );
    }
  }
}
#endif
