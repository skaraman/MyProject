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
  static void PreparePackDirectory(string packRootPath, string externalRoot, TransitionPipelineMode mode, ExportSyncStats stats) {
    var normalizedPackRoot = NormalizeFullPath(packRootPath);
    var normalizedExternalRoot = NormalizeFullPath(externalRoot);

    if (!normalizedPackRoot.StartsWith(normalizedExternalRoot + "/", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(normalizedPackRoot, normalizedExternalRoot, StringComparison.OrdinalIgnoreCase)) {
      throw new InvalidOperationException("Pack root escaped external root. pack='" + normalizedPackRoot + "'");
    }

    if (mode == TransitionPipelineMode.Clean &&
        Directory.Exists(normalizedPackRoot) &&
        !IsPackageBackedExternalPackPath(normalizedPackRoot, normalizedExternalRoot)) {
      Directory.Delete(normalizedPackRoot, recursive: true);
      if (stats != null) {
        stats.packDirectoriesRecreated++;
      }
    }

    if (!Directory.Exists(normalizedPackRoot)) {
      Directory.CreateDirectory(normalizedPackRoot);
      if (stats != null) {
        stats.packDirectoriesCreated++;
      }
    }
  }

  static bool IsPackageBackedExternalPackPath(string normalizedPackRoot, string normalizedExternalRoot) {
    if (!StageRootAssetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) return false;
    if (string.IsNullOrWhiteSpace(normalizedPackRoot) || string.IsNullOrWhiteSpace(normalizedExternalRoot)) return false;
    return string.Equals(normalizedPackRoot, normalizedExternalRoot, StringComparison.OrdinalIgnoreCase);
  }

  static void EnsureDirectoryAssetPath(string assetPath) {
    if (string.IsNullOrWhiteSpace(assetPath)) return;
    var fullPath = Path.GetFullPath(assetPath);
    Directory.CreateDirectory(fullPath);
  }

  static void EnsureDirectoryFullPath(string fullPath) {
    if (string.IsNullOrWhiteSpace(fullPath)) return;
    Directory.CreateDirectory(fullPath);
  }

  static void WriteJson<T>(string fullPath, T payload, TransitionPipelineMode mode, ExportSyncStats stats, bool generatedFile) {
    EnsureDirectoryFullPath(Path.GetDirectoryName(fullPath));
    File.WriteAllText(fullPath, JsonUtility.ToJson(payload, prettyPrint: true), new UTF8Encoding(false));
    if (stats != null) {
      if (generatedFile) {
        stats.generatedFilesWritten++;
      }
      else {
        stats.manifestsWritten++;
      }
    }
  }

  static string NormalizeLibraryName(string value) {
    var normalized = NormalizeAssetPath(value);
    if (string.IsNullOrWhiteSpace(normalized)) return "";
    if (normalized.EndsWith(".spriteLib", StringComparison.OrdinalIgnoreCase)) {
      normalized = normalized.Substring(0, normalized.Length - ".spriteLib".Length);
    }

    var root = NormalizeAssetPath(SpriteStreamingConfig.SourceRootFolder);
    if (string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase)) return "";
    if (normalized.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)) {
      normalized = normalized.Substring(root.Length + 1);
    }

    var localRoot = "Assets/Sprites/SpriteLibraries";
    if (string.Equals(normalized, localRoot, StringComparison.OrdinalIgnoreCase)) return "";
    if (normalized.StartsWith(localRoot + "/", StringComparison.OrdinalIgnoreCase)) {
      normalized = normalized.Substring(localRoot.Length + 1);
    }

    return normalized.Trim('/');
  }

  static void AddUniquePath(List<string> paths, string value) {
    if (paths == null) return;
    var normalized = NormalizeAssetPath(value);
    if (string.IsNullOrWhiteSpace(normalized)) return;

    for (var i = 0; i < paths.Count; i++) {
      if (string.Equals(paths[i], normalized, StringComparison.OrdinalIgnoreCase)) return;
    }

    paths.Add(normalized);
  }

  static string RemoveExtension(string assetPath) {
    var normalized = NormalizeAssetPath(assetPath);
    return string.IsNullOrWhiteSpace(normalized) ? "" : Path.ChangeExtension(normalized, null)?.Replace('\\', '/') ?? "";
  }

  static string NormalizeToken(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }

  static string NormalizeAssetPath(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim().Replace('\\', '/');
  }

  public static string GetPhysicalPath(string path) {
    if (string.IsNullOrWhiteSpace(path)) return "";
    var normalized = NormalizeAssetPath(path);
    if (normalized.StartsWith(StageRootAssetPath + "/", StringComparison.OrdinalIgnoreCase)) {
      var relative = normalized.Substring(StageRootAssetPath.Length + 1);
      return Path.GetFullPath(Path.Combine(GetConfiguredExternalRoot(), relative)).Replace('\\', '/');
    }
    if (string.Equals(normalized, StageRootAssetPath, StringComparison.OrdinalIgnoreCase)) {
      return GetConfiguredExternalRoot();
    }

    return Path.GetFullPath(normalized).Replace('\\', '/');
  }

  static string NormalizeFullPath(string value) {
    if (string.IsNullOrWhiteSpace(value)) return "";
    var normalized = NormalizeAssetPath(value);
    if (string.Equals(normalized, StageRootAssetPath, StringComparison.OrdinalIgnoreCase) ||
        normalized.StartsWith(StageRootAssetPath + "/", StringComparison.OrdinalIgnoreCase)) {
      return GetPhysicalPath(normalized);
    }

    return Path.GetFullPath(value).Replace('\\', '/');
  }

  public static string ToProjectAssetPath(string fullPath) {
    var normalizedFullPath = NormalizeFullPath(fullPath);
    var externalRoot = GetConfiguredExternalRoot();
    if (string.Equals(normalizedFullPath, externalRoot, StringComparison.OrdinalIgnoreCase)) {
      return StageRootAssetPath;
    }
    if (normalizedFullPath.StartsWith(externalRoot + "/", StringComparison.OrdinalIgnoreCase)) {
      var relative = normalizedFullPath.Substring(externalRoot.Length + 1);
      return NormalizeAssetPath(StageRootAssetPath + "/" + relative);
    }

    var projectRoot = NormalizeFullPath(GetProjectRoot());
    if (normalizedFullPath.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase)) {
      return normalizedFullPath.Substring(projectRoot.Length + 1);
    }
    return normalizedFullPath;
  }

  static string GetConfiguredExternalRoot() {
    var selection = AssetDatabase.LoadAssetAtPath<ContentPackSelection>(SelectionAssetPath);
    var configuredRoot = selection != null && !string.IsNullOrWhiteSpace(selection.ExternalRoot)
      ? selection.ExternalRoot
      : ResolveDefaultExternalRoot();
    return Path.GetFullPath(configuredRoot).Replace('\\', '/').TrimEnd('/');
  }

  public static string ResolveDefaultExternalRoot() {
    var manifestRoot = "";
    try {
      manifestRoot = ResolvePackageFileDependencyRoot();
    }
    catch (Exception ex) {
      Debug.LogWarning("[ContentPackPipeline] Failed to resolve package external root from manifest. fallback='" + DefaultExternalRootFallback + "' error='" + ex.Message + "'");
    }
    return string.IsNullOrWhiteSpace(manifestRoot) ? DefaultExternalRootFallback : manifestRoot;
  }

  static string ResolveDefaultExternalRootFallback() {
    var projectRoot = GetProjectRoot();
    var parent = Directory.GetParent(projectRoot)?.FullName;
    var root = string.IsNullOrWhiteSpace(parent)
      ? Path.Combine(projectRoot, "..", DefaultExternalRootFolderName)
      : Path.Combine(parent, DefaultExternalRootFolderName);
    return Path.GetFullPath(root).Replace('\\', '/').TrimEnd('/');
  }

  static string ResolvePackageFileDependencyRoot() {
    var projectRoot = GetProjectRoot();
    var manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
    if (!File.Exists(manifestPath)) return "";

    var manifestText = File.ReadAllText(manifestPath);
    var match = ContentPackageDependencyRegex.Match(manifestText);
    if (!match.Success) return "";

    var packagePath = Uri.UnescapeDataString(match.Groups[1].Value).Replace('\\', '/').Trim();
    if (string.IsNullOrWhiteSpace(packagePath)) return "";

    var packageRoot = Path.IsPathRooted(packagePath)
      ? packagePath
      : Path.Combine(projectRoot, "Packages", packagePath);
    return Path.GetFullPath(packageRoot).Replace('\\', '/').TrimEnd('/');
  }

  static string GetProjectRoot() {
    return Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();
  }

  static void LogTransitionRunSummary(string label, TransitionRunSummary summary) {
    if (summary == null) return;

    Debug.Log(
      "[ContentPackPipeline] [TransitionSummary] label='" + label + "'" +
      " mode='" + summary.mode + "'" +
      " stage=" + (summary.stageCompleted ? 1 : 0) +
      " audit=" + (summary.auditCompleted ? 1 : 0) +
      " runtime_index=" + (summary.runtimeIndexCompleted ? 1 : 0) +
      " unified_import=" + (summary.unifiedImportCompleted ? 1 : 0) +
      " hotset=" + (summary.hotsetCompleted ? 1 : 0) +
      " addressables=" + (summary.addressablesCompleted ? 1 : 0) +
      FormatExportStats(summary.export) +
      FormatAnalysisStats(summary.analysis)
    );
  }

  static string FormatExportStats(ExportSyncStats stats) {
    if (stats == null) return "";
    return
      " pack_dirs_created=" + stats.packDirectoriesCreated +
      " pack_dirs_recreated=" + stats.packDirectoriesRecreated +
      " asset_writes=" + stats.assetPayloadsWritten +
      " asset_skips=" + stats.assetPayloadsSkipped +
      " meta_writes=" + stats.metaPayloadsWritten +
      " meta_skips=" + stats.metaPayloadsSkipped +
      " generated_writes=" + stats.generatedFilesWritten +
      " manifest_writes=" + stats.manifestsWritten;
  }

  static string FormatAnalysisStats(OwnershipAnalysisReport report) {
    if (report == null) return "";
    return
      " duplicate_assets=" + report.spriteDuplicateCount +
      " legacy_generated_refs=" + report.legacyGeneratedReferenceCount +
      " staged_project_tree_dependencies=" + report.stagedProjectTreeDependencyCount +
      " staged_code_dependencies=" + report.stagedCodeDependencyCount +
      " ownership_findings=" + report.ownershipViolationCount;
  }

  static int CountTextOccurrences(string assetPath, string pattern) {
    var fullPath = Path.GetFullPath(assetPath);
    if (!File.Exists(fullPath) || string.IsNullOrWhiteSpace(pattern)) return 0;

    var text = File.ReadAllText(fullPath);
    var count = 0;
    var index = 0;
    while (true) {
      index = text.IndexOf(pattern, index, StringComparison.OrdinalIgnoreCase);
      if (index < 0) break;
      count++;
      index += pattern.Length;
    }

    return count;
  }

  static string ComputeSha256(string text) {
    var bytes = Encoding.UTF8.GetBytes(text ?? "");
    using var sha = SHA256.Create();
    var hashBytes = sha.ComputeHash(bytes);
    var builder = new StringBuilder(hashBytes.Length * 2);
    for (var i = 0; i < hashBytes.Length; i++) {
      builder.Append(hashBytes[i].ToString("x2"));
    }
    return builder.ToString();
  }

  static string TryGetGitRevision() {
    try {
      var startInfo = new ProcessStartInfo {
        FileName = "git",
        Arguments = "rev-parse HEAD",
        CreateNoWindow = true,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        WorkingDirectory = GetProjectRoot()
      };

      using var process = Process.Start(startInfo);
      var output = process.StandardOutput.ReadToEnd().Trim();
      process.WaitForExit();
      return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output : "unknown";
    }
    catch {
      return "unknown";
    }
  }

  static void LogErrors(string stage, List<string> errors) {
    if (errors == null || errors.Count <= 0) return;
    for (var i = 0; i < errors.Count; i++) {
      Debug.LogError("[ContentPackPipeline] [" + stage + "] " + errors[i]);
    }
  }
}
#endif
