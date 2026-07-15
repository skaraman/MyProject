#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

internal sealed class ContentPackPlayerDeployment : IPreprocessBuildWithReport, IPostprocessBuildWithReport {
  const string ManifestFileName = "ContentPackManifest.json";

  static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase) {
    ".git",
    ".hg",
    ".svn",
    "__pycache__",
    "AddressablesLink"
  };

  public int callbackOrder => int.MaxValue - 100;

  public void OnPreprocessBuild(BuildReport report) {
    if (!IsStandaloneBuild(report.summary.platform)) {
      return;
    }

    var selection = LoadSelection();
    if (selection == null) {
      throw new BuildFailedException(
        "[ContentPackDeployment] Missing content pack selection asset at '" +
        ContentPackPipeline.SelectionAssetPath + "'."
      );
    }

    if (!selection.ExternalContentEnabled) {
      return;
    }

    ValidateBuildTarget(report.summary.platform);
    ValidateDeploymentSource(selection, report.summary.platform);
  }

  public void OnPostprocessBuild(BuildReport report) {
    if (!IsStandaloneBuild(report.summary.platform)) {
      return;
    }

    var selection = LoadSelection();
    if (selection == null || !selection.ExternalContentEnabled) {
      return;
    }

    var sourceRoot = NormalizeFullPath(selection.ExternalRoot);
    var manifests = ValidateDeploymentSource(selection, report.summary.platform);
    var destinationRoot = ResolveDestinationRoot(report);

    RecreateDestinationRoot(report, destinationRoot);

    var copiedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var catalogCount = 0;
    for (var i = 0; i < manifests.Count; i++) {
      DeployManifest(sourceRoot, destinationRoot, manifests[i], copiedFiles);
      if (!string.IsNullOrWhiteSpace(manifests[i].manifest.catalogPath)) {
        catalogCount++;
      }
    }

    Debug.Log(
      "[ContentPackDeployment] Deployed standalone content packs." +
      " source_root='" + sourceRoot + "'" +
      " destination_root='" + destinationRoot + "'" +
      " manifest_count=" + manifests.Count +
      " catalog_count=" + catalogCount +
      " file_count=" + copiedFiles.Count
    );
  }

  static ContentPackSelection LoadSelection() {
    return AssetDatabase.LoadAssetAtPath<ContentPackSelection>(ContentPackPipeline.SelectionAssetPath);
  }

  static bool IsStandaloneBuild(BuildTarget platform) {
    return platform == BuildTarget.StandaloneWindows ||
      platform == BuildTarget.StandaloneWindows64 ||
      platform == BuildTarget.StandaloneLinux64 ||
      platform == BuildTarget.StandaloneOSX;
  }

  static void ValidateBuildTarget(BuildTarget platform) {
    if (EditorUserBuildSettings.activeBuildTarget == platform) {
      return;
    }

    throw new BuildFailedException(
      "[ContentPackDeployment] The active build target does not match the player target." +
      " active='" + EditorUserBuildSettings.activeBuildTarget + "'" +
      " player='" + platform + "'" +
      " action='Switch the Unity build target, then run Tools > Content Packs > Build Smart before building the player.'"
    );
  }

  static List<ManifestRecord> ValidateDeploymentSource(
    ContentPackSelection selection,
    BuildTarget platform
  ) {
    var sourceRoot = NormalizeFullPath(selection.ExternalRoot);
    if (!Directory.Exists(sourceRoot)) {
      throw new BuildFailedException(
        "[ContentPackDeployment] External content root is missing." +
        " expected_root='" + sourceRoot + "'" +
        " action='Run Tools > Content Packs > Build Smart before building the player.'"
      );
    }

    var manifestPaths = new List<string>();
    CollectVisibleFiles(sourceRoot, ManifestFileName, manifestPaths);
    manifestPaths.Sort(StringComparer.OrdinalIgnoreCase);

    if (manifestPaths.Count <= 0) {
      throw new BuildFailedException(
        "[ContentPackDeployment] No content pack manifests were found." +
        " source_root='" + sourceRoot + "'" +
        " action='Run Tools > Content Packs > Build Smart before building the player.'"
      );
    }

    var records = new List<ManifestRecord>();
    var recordsByPackId = new Dictionary<string, ManifestRecord>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < manifestPaths.Count; i++) {
      var record = ReadAndValidateManifest(manifestPaths[i], platform);
      if (record == null) continue;

      if (recordsByPackId.TryGetValue(record.manifest.packId, out var duplicate)) {
        throw new BuildFailedException(
          "[ContentPackDeployment] Duplicate content pack id." +
          " pack_id='" + record.manifest.packId + "'" +
          " first='" + duplicate.manifestPath + "'" +
          " second='" + record.manifestPath + "'"
        );
      }

      records.Add(record);
      recordsByPackId.Add(record.manifest.packId, record);
    }

    ValidateActiveCatalogs(recordsByPackId);
    return records;
  }

  static ManifestRecord ReadAndValidateManifest(string manifestPath, BuildTarget platform) {
    DeploymentManifest manifest;
    try {
      manifest = JsonUtility.FromJson<DeploymentManifest>(File.ReadAllText(manifestPath));
    }
    catch (Exception ex) {
      throw new BuildFailedException(
        "[ContentPackDeployment] Failed to read content pack manifest." +
        " path='" + NormalizeSlashes(manifestPath) + "'" +
        " error='" + ex.Message + "'"
      );
    }

    if (manifest == null || string.IsNullOrWhiteSpace(manifest.packId)) {
      throw new BuildFailedException(
        "[ContentPackDeployment] Content pack manifest has no packId." +
        " path='" + NormalizeSlashes(manifestPath) + "'"
      );
    }

    manifest.packId = manifest.packId.Trim();
    if (IsIgnoredPackId(manifest.packId)) {
      return null;
    }

    var hasCatalog = !string.IsNullOrWhiteSpace(manifest.catalogPath);
    var hasBundleRoot = !string.IsNullOrWhiteSpace(manifest.bundleRoot);
    if (hasCatalog != hasBundleRoot) {
      throw new BuildFailedException(
        "[ContentPackDeployment] Content pack manifest has an incomplete runtime catalog contract." +
        " pack_id='" + manifest.packId + "'" +
        " path='" + NormalizeSlashes(manifestPath) + "'" +
        " action='Run Tools > Content Packs > Build Smart.'"
      );
    }

    var record = new ManifestRecord {
      manifest = manifest,
      manifestPath = NormalizeFullPath(manifestPath),
      packRoot = NormalizeFullPath(Path.GetDirectoryName(manifestPath))
    };

    if (!hasCatalog) {
      return record;
    }

    record.catalogPath = ResolveContainedPath(record.packRoot, manifest.catalogPath, manifest.packId);
    record.bundleRoot = ResolveContainedPath(record.packRoot, manifest.bundleRoot, manifest.packId);

    if (!File.Exists(record.catalogPath)) {
      throw new BuildFailedException(
        "[ContentPackDeployment] Runtime catalog is missing." +
        " pack_id='" + manifest.packId + "'" +
        " expected_catalog='" + record.catalogPath + "'" +
        " action='Run Tools > Content Packs > Build Smart.'"
      );
    }

    if (!Directory.Exists(record.bundleRoot)) {
      throw new BuildFailedException(
        "[ContentPackDeployment] Runtime bundle root is missing." +
        " pack_id='" + manifest.packId + "'" +
        " expected_root='" + record.bundleRoot + "'" +
        " action='Run Tools > Content Packs > Build Smart.'"
      );
    }

    ValidateCatalogTarget(record, platform);
    ValidateCatalogFingerprint(record);
    ValidateBundlePayload(record);
    return record;
  }

  static void ValidateCatalogTarget(ManifestRecord record, BuildTarget platform) {
    var catalogDirectory = Path.GetDirectoryName(record.catalogPath);
    var catalogTarget = Path.GetFileName(catalogDirectory);
    var expectedTarget = platform.ToString();
    if (string.Equals(catalogTarget, expectedTarget, StringComparison.OrdinalIgnoreCase)) {
      return;
    }

    throw new BuildFailedException(
      "[ContentPackDeployment] Runtime catalog was built for a different target." +
      " pack_id='" + record.manifest.packId + "'" +
      " catalog_target='" + catalogTarget + "'" +
      " player_target='" + expectedTarget + "'" +
      " action='Switch the Unity build target, then run Tools > Content Packs > Build Smart.'"
    );
  }

  static void ValidateCatalogFingerprint(ManifestRecord record) {
    var fingerprintPath = Path.Combine(
      record.bundleRoot,
      ContentPackPipeline.RuntimeCatalogFingerprintFileName
    );
    if (File.Exists(fingerprintPath)) {
      return;
    }

    throw new BuildFailedException(
      "[ContentPackDeployment] Runtime catalog has no input fingerprint." +
      " pack_id='" + record.manifest.packId + "'" +
      " catalog='" + record.catalogPath + "'" +
      " action='Run Tools > Content Packs > Build Smart.'"
    );
  }

  static void ValidateBundlePayload(ManifestRecord record) {
    var bundleFiles = new List<string>();
    CollectVisibleFiles(record.bundleRoot, "*.bundle", bundleFiles);
    if (bundleFiles.Count > 0) {
      return;
    }

    throw new BuildFailedException(
      "[ContentPackDeployment] Runtime bundle root contains no asset bundles." +
      " pack_id='" + record.manifest.packId + "'" +
      " bundle_root='" + record.bundleRoot + "'" +
      " action='Run Tools > Content Packs > Build Smart.'"
    );
  }

  static void ValidateActiveCatalogs(Dictionary<string, ManifestRecord> recordsByPackId) {
    var activeCatalogs = ContentPackPipeline.GetActiveOptionalRuntimePackCatalogBuilds();
    for (var i = 0; i < activeCatalogs.Count; i++) {
      var activeCatalog = activeCatalogs[i];
      if (!recordsByPackId.TryGetValue(activeCatalog.packId, out var record)) {
        throw new BuildFailedException(
          "[ContentPackDeployment] Active content pack has no deployable manifest." +
          " pack_id='" + activeCatalog.packId + "'" +
          " action='Run Tools > Content Packs > Build Smart.'"
        );
      }

      var expectedCatalog = NormalizeSlashes(activeCatalog.catalogRelativePath);
      var actualCatalog = NormalizeSlashes(record.manifest.catalogPath);
      var expectedBundleRoot = NormalizeSlashes(activeCatalog.bundleRootRelativePath);
      var actualBundleRoot = NormalizeSlashes(record.manifest.bundleRoot);

      if (string.Equals(expectedCatalog, actualCatalog, StringComparison.OrdinalIgnoreCase) &&
          string.Equals(expectedBundleRoot, actualBundleRoot, StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      throw new BuildFailedException(
        "[ContentPackDeployment] Active content pack manifest does not match the current build target." +
        " pack_id='" + activeCatalog.packId + "'" +
        " expected_catalog='" + expectedCatalog + "'" +
        " actual_catalog='" + actualCatalog + "'" +
        " action='Run Tools > Content Packs > Build Smart.'"
      );
    }
  }

  static string ResolveDestinationRoot(BuildReport report) {
    var outputPath = NormalizeFullPath(report.summary.outputPath);
    string destinationParent;
    if (report.summary.platform == BuildTarget.StandaloneOSX) {
      destinationParent = outputPath;
    }
    else {
      destinationParent = Path.GetDirectoryName(outputPath);
    }

    if (string.IsNullOrWhiteSpace(destinationParent)) {
      throw new BuildFailedException(
        "[ContentPackDeployment] Could not resolve player output directory." +
        " output_path='" + outputPath + "'"
      );
    }

    return NormalizeFullPath(
      Path.Combine(destinationParent, ContentPackPipeline.DefaultExternalRootFolderName)
    );
  }

  static void RecreateDestinationRoot(BuildReport report, string destinationRoot) {
    var outputPath = NormalizeFullPath(report.summary.outputPath);
    var allowedParent = report.summary.platform == BuildTarget.StandaloneOSX
      ? outputPath
      : NormalizeFullPath(Path.GetDirectoryName(outputPath));
    var destinationParent = NormalizeFullPath(Path.GetDirectoryName(destinationRoot));
    var destinationName = Path.GetFileName(destinationRoot);

    if (!string.Equals(destinationParent, allowedParent, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(
          destinationName,
          ContentPackPipeline.DefaultExternalRootFolderName,
          StringComparison.Ordinal
        )) {
      throw new BuildFailedException(
        "[ContentPackDeployment] Refused to replace an unsafe deployment path." +
        " destination='" + destinationRoot + "'" +
        " allowed_parent='" + allowedParent + "'"
      );
    }

    if (Directory.Exists(destinationRoot)) {
      Directory.Delete(destinationRoot, recursive: true);
    }

    Directory.CreateDirectory(destinationRoot);
  }

  static void DeployManifest(
    string sourceRoot,
    string destinationRoot,
    ManifestRecord record,
    HashSet<string> copiedFiles
  ) {
    var relativeManifestPath = MakeRelativePath(sourceRoot, record.manifestPath);
    var destinationManifestPath = ResolveContainedPath(
      destinationRoot,
      relativeManifestPath,
      record.manifest.packId
    );
    CopyFile(record.manifestPath, destinationManifestPath, copiedFiles);

    if (string.IsNullOrWhiteSpace(record.catalogPath)) {
      return;
    }

    var destinationPackRoot = Path.GetDirectoryName(destinationManifestPath);
    var runtimeFiles = new List<string>();
    CollectVisibleFiles(record.bundleRoot, "*", runtimeFiles);
    for (var i = 0; i < runtimeFiles.Count; i++) {
      var sourceFile = runtimeFiles[i];
      if (!IsRuntimeDeploymentFile(sourceFile, record.catalogPath)) continue;

      var relativeRuntimePath = MakeRelativePath(record.packRoot, sourceFile);
      var destinationFile = ResolveContainedPath(
        destinationPackRoot,
        relativeRuntimePath,
        record.manifest.packId
      );
      CopyFile(sourceFile, destinationFile, copiedFiles);
    }

    var relativeCatalogPath = MakeRelativePath(record.packRoot, record.catalogPath);
    var destinationCatalogPath = ResolveContainedPath(
      destinationPackRoot,
      relativeCatalogPath,
      record.manifest.packId
    );
    CopyFile(record.catalogPath, destinationCatalogPath, copiedFiles);
  }

  static bool IsRuntimeDeploymentFile(string path, string catalogPath) {
    if (string.Equals(path, catalogPath, StringComparison.OrdinalIgnoreCase)) {
      return true;
    }

    var extension = Path.GetExtension(path);
    return string.Equals(extension, ".bundle", StringComparison.OrdinalIgnoreCase) ||
      string.Equals(extension, ".hash", StringComparison.OrdinalIgnoreCase);
  }

  static void CopyFile(string sourcePath, string destinationPath, HashSet<string> copiedFiles) {
    if (!copiedFiles.Add(destinationPath)) {
      return;
    }

    var destinationDirectory = Path.GetDirectoryName(destinationPath);
    if (!string.IsNullOrWhiteSpace(destinationDirectory)) {
      Directory.CreateDirectory(destinationDirectory);
    }

    File.Copy(sourcePath, destinationPath, overwrite: true);
  }

  static void CollectVisibleFiles(string root, string searchPattern, List<string> result) {
    if (result == null || string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) {
      return;
    }

    try {
      var files = Directory.GetFiles(root, searchPattern, SearchOption.TopDirectoryOnly);
      for (var i = 0; i < files.Length; i++) {
        result.Add(NormalizeFullPath(files[i]));
      }

      var directories = Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly);
      for (var i = 0; i < directories.Length; i++) {
        if (IsIgnoredDirectory(directories[i])) continue;
        CollectVisibleFiles(directories[i], searchPattern, result);
      }
    }
    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) {
      throw new BuildFailedException(
        "[ContentPackDeployment] Could not read content pack directory." +
        " path='" + NormalizeSlashes(root) + "'" +
        " error='" + ex.Message + "'"
      );
    }
  }

  static bool IsIgnoredDirectory(string path) {
    var name = Path.GetFileName(path);
    if (string.IsNullOrWhiteSpace(name)) return true;
    if (name.StartsWith(".", StringComparison.Ordinal)) return true;
    if (IgnoredDirectoryNames.Contains(name)) return true;

    try {
      var attributes = File.GetAttributes(path);
      return (attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0;
    }
    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) {
      return true;
    }
  }

  static bool IsIgnoredPackId(string packId) {
    if (string.IsNullOrWhiteSpace(packId)) return true;
    if (packId.StartsWith(".", StringComparison.Ordinal)) return true;
    return IgnoredDirectoryNames.Contains(packId);
  }

  static string ResolveContainedPath(string root, string relativePath, string packId) {
    if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) {
      throw new BuildFailedException(
        "[ContentPackDeployment] Content pack path must be relative." +
        " pack_id='" + (packId ?? "") + "'" +
        " path='" + (relativePath ?? "") + "'"
      );
    }

    var normalizedRoot = NormalizeFullPath(root);
    var combined = NormalizeFullPath(Path.Combine(normalizedRoot, relativePath));
    if (IsPathInsideRoot(combined, normalizedRoot)) {
      return combined;
    }

    throw new BuildFailedException(
      "[ContentPackDeployment] Content pack path escaped its root." +
      " pack_id='" + (packId ?? "") + "'" +
      " root='" + normalizedRoot + "'" +
      " path='" + (relativePath ?? "") + "'"
    );
  }

  static bool IsPathInsideRoot(string path, string root) {
    if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase)) {
      return true;
    }

    var rootWithSeparator = root.TrimEnd('/') + "/";
    return path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
  }

  static string MakeRelativePath(string root, string path) {
    var normalizedRoot = NormalizeFullPath(root);
    var normalizedPath = NormalizeFullPath(path);
    if (!IsPathInsideRoot(normalizedPath, normalizedRoot)) {
      throw new BuildFailedException(
        "[ContentPackDeployment] Could not make deployment path relative." +
        " root='" + normalizedRoot + "'" +
        " path='" + normalizedPath + "'"
      );
    }

    if (string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase)) {
      return "";
    }

    return normalizedPath.Substring(normalizedRoot.Length).TrimStart('/');
  }

  static string NormalizeFullPath(string path) {
    if (string.IsNullOrWhiteSpace(path)) {
      return "";
    }

    return Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
  }

  static string NormalizeSlashes(string path) {
    return string.IsNullOrWhiteSpace(path) ? "" : path.Trim().Replace('\\', '/');
  }

  [Serializable]
  sealed class DeploymentManifest {
    public string packId;
    public string catalogPath;
    public string bundleRoot;
  }

  sealed class ManifestRecord {
    public DeploymentManifest manifest;
    public string manifestPath;
    public string packRoot;
    public string catalogPath;
    public string bundleRoot;
  }
}
#endif
