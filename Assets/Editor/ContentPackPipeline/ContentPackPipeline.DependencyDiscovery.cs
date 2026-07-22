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
  static List<string> ExpandProjectRoots(IEnumerable<string> roots, List<string> errors) {
    var result = new List<string>();
    if (roots == null) return result;

    foreach (var root in roots) {
      var normalizedRoot = ResolveExistingSpriteLibraryAssetPath(root);
      if (string.IsNullOrWhiteSpace(normalizedRoot)) continue;

      var fullPath = GetPhysicalPath(normalizedRoot);
      if (File.Exists(fullPath)) {
        AddUniquePath(result, normalizedRoot);
        continue;
      }

      if (!Directory.Exists(fullPath)) {
        errors?.Add("Missing asset root '" + normalizedRoot + "'.");
        continue;
      }

      var guids = AssetDatabase.FindAssets("", new[] { normalizedRoot });
      Array.Sort(guids, StringComparer.OrdinalIgnoreCase);
      for (var i = 0; i < guids.Length; i++) {
        var assetPath = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guids[i]));
        if (string.IsNullOrWhiteSpace(assetPath) || AssetDatabase.IsValidFolder(assetPath)) continue;
        AddUniquePath(result, assetPath);
      }
    }

    return result;
  }

  static HashSet<string> CollectReferencedLibraryNamesFromAssets(IEnumerable<string> assetPaths) {
    var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (assetPaths == null) return result;

    foreach (var assetPath in assetPaths) {
      var normalizedAssetPath = NormalizeAssetPath(assetPath);
      if (string.IsNullOrWhiteSpace(normalizedAssetPath)) continue;
      if (!ShouldRewriteTextFile(normalizedAssetPath)) continue;

      var fullPath = Path.GetFullPath(normalizedAssetPath);
      if (!File.Exists(fullPath)) continue;
      var text = File.ReadAllText(fullPath);

      AddLibraryNamesFromRegex(result, text, LibraryNameRegex);
      AddLibraryNamesFromRegex(result, text, PortraitLibraryNameRegex);
    }

    return result;
  }

  static void AddLibraryNamesFromRegex(HashSet<string> output, string text, Regex regex) {
    if (output == null || string.IsNullOrWhiteSpace(text) || regex == null) return;

    var matches = regex.Matches(text);
    for (var i = 0; i < matches.Count; i++) {
      if (!matches[i].Success) continue;
      AddUniqueLibraryName(output, matches[i].Groups[1].Value);
    }
  }

  static void AddUniqueLibraryName(HashSet<string> output, string value) {
    if (output == null) return;
    var normalized = NormalizeLibraryName(value);
    if (string.IsNullOrWhiteSpace(normalized)) return;
    output.Add(normalized);
  }

  static List<string> ResolveLibraryAssetPaths(
    HashSet<string> libraryNames,
    Dictionary<string, string> librariesByKey,
    List<string> errors
  ) {
    var result = new List<string>();
    if (libraryNames == null) return result;

    foreach (var libraryName in libraryNames.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)) {
      if (!librariesByKey.TryGetValue(libraryName, out var libraryPath)) {
        errors?.Add("Missing sprite library '" + libraryName + "'.");
        continue;
      }

      AddUniquePath(result, libraryPath);

      var normalLibraryName = libraryName + "N";
      if (librariesByKey.TryGetValue(normalLibraryName, out var normalLibraryPath)) {
        AddUniquePath(result, normalLibraryPath);
      }
    }

    return result;
  }

  static Dictionary<string, string> DiscoverProjectLibraryPaths() {
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var root = "Assets/Sprites/SpriteLibraries";
    var fullRoot = GetPhysicalPath(root);
    if (!Directory.Exists(fullRoot)) return result;

    var customFiles = Directory.GetFiles(fullRoot, "*" + SpriteStreamingConfig.CustomSpriteLibraryExtension, SearchOption.AllDirectories);
    var legacyFiles = Directory.GetFiles(fullRoot, "*" + SpriteStreamingConfig.LegacySpriteLibraryExtension, SearchOption.AllDirectories);
    Array.Sort(customFiles, StringComparer.OrdinalIgnoreCase);
    Array.Sort(legacyFiles, StringComparer.OrdinalIgnoreCase);

    var files = new List<string>(customFiles.Length + legacyFiles.Length);
    files.AddRange(customFiles);
    files.AddRange(legacyFiles);

    for (var i = 0; i < files.Count; i++) {
      var assetPath = ToProjectAssetPath(files[i]);
      var relativePath = assetPath.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)
        ? assetPath.Substring(root.Length + 1)
        : assetPath;
      var key = RemoveExtension(relativePath);
      if (!result.ContainsKey(key)) {
        result[key] = assetPath;
      }
    }

    return result;
  }

  static List<string> CollectPackDependencies(List<string> seedAssetPaths, List<string> errors) {
    var result = new List<string>();
    if (seedAssetPaths == null || seedAssetPaths.Count <= 0) return result;

    for (var i = 0; i < seedAssetPaths.Count; i++) {
      TryAddExportableDependency(result, seedAssetPaths[i], errors);
    }

    var dependencies = AssetDatabase.GetDependencies(seedAssetPaths.ToArray(), true);
    Array.Sort(dependencies, StringComparer.OrdinalIgnoreCase);

    for (var i = 0; i < dependencies.Length; i++) {
      TryAddExportableDependency(result, dependencies[i], errors);
    }

    CollectSupplementalTextDependencies(result, errors);
    CollectPairedNormalMapDependencies(result, errors);
    CollectAtlasMetadataDependencies(result, errors);
    return result;
  }

  static void CollectPairedNormalMapDependencies(List<string> result, List<string> errors) {
    if (result == null || result.Count <= 0) return;

    var pairedNormalMapPaths = new List<string>();
    for (var i = 0; i < result.Count; i++) {
      var normalMapAssetPath = ResolvePairedNormalMapAssetPath(result[i]);
      if (string.IsNullOrWhiteSpace(normalMapAssetPath)) continue;
      AddUniquePath(pairedNormalMapPaths, normalMapAssetPath);
    }

    for (var i = 0; i < pairedNormalMapPaths.Count; i++) {
      TryAddExportableDependency(result, pairedNormalMapPaths[i], errors);
    }
  }

  static string ResolvePairedNormalMapAssetPath(string colorAssetPath) {
    var normalizedColorAssetPath = NormalizeAssetPath(colorAssetPath);
    if (!string.Equals(Path.GetExtension(normalizedColorAssetPath), ".png", StringComparison.OrdinalIgnoreCase)) {
      return "";
    }

    var jpgAssetPath = NormalizeAssetPath(Path.ChangeExtension(normalizedColorAssetPath, ".jpg"));
    if (File.Exists(Path.GetFullPath(jpgAssetPath))) {
      return jpgAssetPath;
    }

    var jpegAssetPath = NormalizeAssetPath(Path.ChangeExtension(normalizedColorAssetPath, ".jpeg"));
    if (File.Exists(Path.GetFullPath(jpegAssetPath))) {
      return jpegAssetPath;
    }

    return "";
  }

  static void CollectAtlasMetadataDependencies(List<string> result, List<string> errors) {
    if (result == null || result.Count <= 0) return;

    var newDependencies = new List<string>();
    for (var i = 0; i < result.Count; i++) {
      var assetPath = result[i];
      if (assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
          assetPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
          assetPath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)) {
        var metadataAssetPath = Path.ChangeExtension(assetPath, ".json");
        if (File.Exists(Path.GetFullPath(metadataAssetPath))) {
          newDependencies.Add(metadataAssetPath);
        }
      }
    }

    for (var i = 0; i < newDependencies.Count; i++) {
      TryAddExportableDependency(result, newDependencies[i], errors);
    }
  }

  static bool TryAddExportableDependency(List<string> result, string assetPath, List<string> errors) {
    var dependency = ResolveExistingSpriteLibraryAssetPath(assetPath);
    if (string.IsNullOrWhiteSpace(dependency)) return false;
    if (!dependency.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return false;
    if (AssetDatabase.IsValidFolder(dependency)) return false;
    if (ShouldIgnoreDependency(dependency)) return false;
    if (dependency.StartsWith(StageRootAssetPath + "/", StringComparison.OrdinalIgnoreCase)) return false;
    if (dependency.StartsWith("Assets/Generated/", StringComparison.OrdinalIgnoreCase)) {
      errors?.Add("Generated asset dependency detected '" + dependency + "'.");
      return false;
    }

    return TryAddUniquePath(result, dependency);
  }

  static void CollectSupplementalTextDependencies(List<string> result, List<string> errors) {
    if (result == null || result.Count <= 0) return;

    var pending = new Queue<string>(result);
    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    while (pending.Count > 0) {
      var currentAssetPath = NormalizeAssetPath(pending.Dequeue());
      if (string.IsNullOrWhiteSpace(currentAssetPath)) continue;
      if (!visited.Add(currentAssetPath)) continue;

      EnqueueSupplementalDependencies(result, pending, CollectLocalTextIncludeDependencies(currentAssetPath, errors), errors);
      EnqueueSupplementalDependencies(result, pending, CollectTextGuidDependencies(currentAssetPath, errors), errors);
    }
  }

  static void EnqueueSupplementalDependencies(
    List<string> result,
    Queue<string> pending,
    List<string> dependencies,
    List<string> errors
  ) {
    if (result == null || pending == null || dependencies == null) return;

    for (var i = 0; i < dependencies.Count; i++) {
      if (!TryAddExportableDependency(result, dependencies[i], errors)) continue;
      pending.Enqueue(NormalizeAssetPath(dependencies[i]));
    }
  }

  static List<string> CollectTextGuidDependencies(string assetPath, List<string> errors) {
    var result = new List<string>();
    if (!ShouldRewriteTextFile(assetPath)) return result;

    var fullPath = Path.GetFullPath(assetPath);
    if (!File.Exists(fullPath)) {
      errors?.Add("Missing text dependency source asset '" + assetPath + "'.");
      return result;
    }

    var text = File.ReadAllText(fullPath);
    var matches = GuidRegex.Matches(text);
    for (var i = 0; i < matches.Count; i++) {
      if (!matches[i].Success) continue;
      var guid = matches[i].Groups[1].Value;
      var dependency = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guid));
      if (string.IsNullOrWhiteSpace(dependency)) continue;
      AddUniquePath(result, dependency);
    }

    return result;
  }

  static List<string> CollectLocalTextIncludeDependencies(string assetPath, List<string> errors) {
    var result = new List<string>();
    if (!ShouldScanLocalIncludes(assetPath)) return result;

    var pending = new Queue<string>();
    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    pending.Enqueue(NormalizeAssetPath(assetPath));

    while (pending.Count > 0) {
      var currentAssetPath = NormalizeAssetPath(pending.Dequeue());
      if (string.IsNullOrWhiteSpace(currentAssetPath) || !visited.Add(currentAssetPath)) continue;
      if (!ShouldScanLocalIncludes(currentAssetPath)) continue;

      var currentFullPath = Path.GetFullPath(currentAssetPath);
      if (!File.Exists(currentFullPath)) {
        errors?.Add("Missing include source asset '" + currentAssetPath + "'.");
        continue;
      }

      var text = File.ReadAllText(currentFullPath);
      var matches = LocalIncludeRegex.Matches(text);
      for (var i = 0; i < matches.Count; i++) {
        if (!matches[i].Success) continue;
        var includeAssetPath = ResolveLocalIncludeAssetPath(currentAssetPath, matches[i].Groups[1].Value, errors);
        if (string.IsNullOrWhiteSpace(includeAssetPath)) continue;
        AddUniquePath(result, includeAssetPath);
        if (ShouldScanLocalIncludes(includeAssetPath)) {
          pending.Enqueue(includeAssetPath);
        }
      }
    }

    return result;
  }

  static string ResolveLocalIncludeAssetPath(string sourceAssetPath, string includePath, List<string> errors) {
    var normalizedInclude = NormalizeAssetPath(includePath);
    if (string.IsNullOrWhiteSpace(normalizedInclude)) return "";
    if (normalizedInclude.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) return "";

    string resolvedAssetPath;
    if (normalizedInclude.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) {
      resolvedAssetPath = normalizedInclude;
    }
    else {
      var sourceDirectory = Path.GetDirectoryName(sourceAssetPath) ?? "";
      resolvedAssetPath = NormalizeAssetPath(Path.Combine(sourceDirectory, normalizedInclude));
    }

    if (!resolvedAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) {
      return "";
    }

    if (!File.Exists(Path.GetFullPath(resolvedAssetPath))) {
      errors?.Add(
        "Missing local include dependency." +
        " source='" + sourceAssetPath + "'" +
        " include='" + includePath + "'" +
        " resolved='" + resolvedAssetPath + "'"
      );
      return "";
    }

    if (ShouldIgnoreDependency(resolvedAssetPath)) return "";
    if (resolvedAssetPath.StartsWith("Assets/Generated/", StringComparison.OrdinalIgnoreCase)) {
      errors?.Add("Generated include dependency detected '" + resolvedAssetPath + "'.");
      return "";
    }

    return resolvedAssetPath;
  }

  static bool ShouldScanLocalIncludes(string assetPath) {
    if (string.IsNullOrWhiteSpace(assetPath)) return false;
    var extension = Path.GetExtension(assetPath);
    return !string.IsNullOrWhiteSpace(extension) && LocalIncludeExtensions.Contains(extension);
  }

  static bool ShouldIgnoreDependency(string assetPath) {
    if (string.IsNullOrWhiteSpace(assetPath)) return true;
    if (assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) return true;

    var extension = Path.GetExtension(assetPath);
    return !string.IsNullOrWhiteSpace(extension) && IgnoredDependencyExtensions.Contains(extension);
  }

  static bool IsCodeDependency(string assetPath) {
    if (string.IsNullOrWhiteSpace(assetPath)) return false;
    var extension = Path.GetExtension(assetPath);
    return !string.IsNullOrWhiteSpace(extension) &&
           (string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".asmdef", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".asmref", StringComparison.OrdinalIgnoreCase));
  }

  static bool ShouldRewriteTextFile(string pathOrAssetPath) {
    var extension = Path.GetExtension(pathOrAssetPath);
    if (string.IsNullOrWhiteSpace(extension)) return false;
    return TextRewriteExtensions.Contains(extension);
  }

  static string BuildStageAssetPath(PackDefinition pack, string projectAssetPath) {
    if (pack == null || string.IsNullOrWhiteSpace(projectAssetPath)) return "";
    var normalizedProjectPath = NormalizeAssetPath(projectAssetPath);
    if (!string.IsNullOrWhiteSpace(pack.stageAssetRoot) &&
        normalizedProjectPath.StartsWith(NormalizeAssetPath(pack.stageAssetRoot) + "/", StringComparison.OrdinalIgnoreCase)) {
      return normalizedProjectPath;
    }
    var relativePath = ResolveExportRelativePath(pack, normalizedProjectPath);
    if (!string.IsNullOrWhiteSpace(relativePath) &&
        pack.targetRelativePathByAssetPath != null &&
        pack.targetRelativePathByAssetPath.ContainsKey(normalizedProjectPath)) {
      return NormalizeAssetPath(pack.stageAssetRoot + "/" + relativePath);
    }
    if (!normalizedProjectPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return normalizedProjectPath;
    return NormalizeAssetPath(pack.stageAssetRoot + "/" + normalizedProjectPath.Substring("Assets/".Length));
  }
}
#endif
