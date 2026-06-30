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
  static List<PackDefinition> BuildPackDefinitions(string externalRoot) {
    var normalizedRoot = NormalizeFullPath(externalRoot);
    var result = DiscoverExternalPackDefinitions(normalizedRoot);
    AddContentManifestPackDefinitions(result, normalizedRoot);
    ApplyExistingPackManifests(result);
    return result;
  }

  static List<PackDefinition> DiscoverExternalPackDefinitions(string normalizedRoot) {
    var result = new List<PackDefinition>();
    AddExternalRootPackDefinitions(result, normalizedRoot);
    AddExistingPackDefinition(
      result,
      packId: CorePackId,
      kind: "core",
      externalRootPath: NormalizeFullPath(Path.Combine(normalizedRoot, "Core")),
      stageAssetRoot: StageCoreAssetPath
    );
    AddExternalPackDefinitions(result, normalizedRoot, "Forms", "form", StageFormsAssetPath);
    AddExternalPackDefinitions(result, normalizedRoot, "Gears", "gear", StageGearsAssetPath);
    AddExternalPackDefinitions(result, normalizedRoot, "Slices", "slice", StageSlicesAssetPath);
    AddExternalPackDefinitions(result, normalizedRoot, "Episodes", "episode", StageEpisodesAssetPath);
    return result;
  }

  static void AddExternalRootPackDefinitions(List<PackDefinition> result, string normalizedRoot) {
    if (!Directory.Exists(normalizedRoot)) return;
    var knownFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
      "Core",
      "Forms",
      "Gears",
      "Slices",
      "Episodes"
    };
    var directories = Directory.GetDirectories(normalizedRoot);
    Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < directories.Length; i++) {
      var packId = Path.GetFileName(directories[i]);
      if (string.IsNullOrWhiteSpace(packId) || knownFolders.Contains(packId)) continue;
      AddExistingPackDefinition(
        result,
        packId,
        "pack",
        NormalizeFullPath(directories[i]),
        StageRootAssetPath + "/" + packId
      );
    }
  }

  static void AddContentManifestPackDefinitions(List<PackDefinition> result, string normalizedRoot) {
    var packIds = ReadContentManifestPackIds();
    for (var i = 0; i < packIds.Count; i++) {
      var packId = NormalizeToken(packIds[i]);
      if (string.IsNullOrWhiteSpace(packId)) continue;
      var kind = InferManifestPackKind(packId);
      AddPackDefinition(
        result,
        packId,
        kind,
        ResolveManifestPackExternalRoot(normalizedRoot, kind, packId),
        ResolveManifestPackStageRoot(kind, packId)
      );
    }
  }

  static string InferManifestPackKind(string packId) {
    if (string.Equals(packId, CorePackId, StringComparison.OrdinalIgnoreCase)) return "core";
    if (packId.StartsWith("Form_", StringComparison.OrdinalIgnoreCase)) return "form";
    if (packId.StartsWith("Gear_", StringComparison.OrdinalIgnoreCase)) return "gear";
    if (packId.StartsWith("Episode_", StringComparison.OrdinalIgnoreCase)) return "episode";
    return "pack";
  }

  static string ResolveManifestPackExternalRoot(string normalizedRoot, string kind, string packId) {
    if (string.Equals(kind, "core", StringComparison.OrdinalIgnoreCase)) {
      return NormalizeFullPath(Path.Combine(normalizedRoot, "Core"));
    }
    if (string.Equals(kind, "form", StringComparison.OrdinalIgnoreCase)) {
      return NormalizeFullPath(Path.Combine(normalizedRoot, "Forms", packId));
    }
    if (string.Equals(kind, "gear", StringComparison.OrdinalIgnoreCase)) {
      return NormalizeFullPath(Path.Combine(normalizedRoot, "Gears", packId));
    }
    if (string.Equals(kind, "episode", StringComparison.OrdinalIgnoreCase)) {
      return NormalizeFullPath(Path.Combine(normalizedRoot, "Episodes", packId));
    }
    return NormalizeFullPath(Path.Combine(normalizedRoot, packId));
  }

  static string ResolveManifestPackStageRoot(string kind, string packId) {
    if (string.Equals(kind, "core", StringComparison.OrdinalIgnoreCase)) return StageCoreAssetPath;
    if (string.Equals(kind, "form", StringComparison.OrdinalIgnoreCase)) return StageFormsAssetPath + "/" + packId;
    if (string.Equals(kind, "gear", StringComparison.OrdinalIgnoreCase)) return StageGearsAssetPath + "/" + packId;
    if (string.Equals(kind, "episode", StringComparison.OrdinalIgnoreCase)) return StageEpisodesAssetPath + "/" + packId;
    return StageRootAssetPath + "/" + packId;
  }

  static List<string> ReadContentManifestPackIds() {
    var result = new List<string>();
    var path = Path.Combine(GetProjectRoot(), "Assets", "ContentManifest.json");
    if (!File.Exists(path)) return result;

    ContentManifestJson manifest;
    try {
      manifest = JsonUtility.FromJson<ContentManifestJson>(File.ReadAllText(path));
    }
    catch (Exception ex) {
      Debug.LogWarning("[ContentPackPipeline] Failed to read Assets/ContentManifest.json. error='" + ex.Message + "'");
      return result;
    }
    if (manifest == null) return result;

    if (manifest.slices == null) return result;
    for (var i = 0; i < manifest.slices.Count; i++) {
      AddListValues(result, manifest.slices[i]?.packs);
    }
    return result;
  }

  static void AddListValues(List<string> result, List<string> values) {
    if (result == null || values == null) return;
    for (var i = 0; i < values.Count; i++) {
      AddUniquePath(result, values[i]);
    }
  }

  static void AddExternalPackDefinitions(
    List<PackDefinition> result,
    string normalizedRoot,
    string folderName,
    string kind,
    string stageRoot
  ) {
    var root = NormalizeFullPath(Path.Combine(normalizedRoot, folderName));
    if (!Directory.Exists(root)) return;
    var directories = Directory.GetDirectories(root);
    Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < directories.Length; i++) {
      var packId = Path.GetFileName(directories[i]);
      if (string.IsNullOrWhiteSpace(packId)) continue;
      AddExistingPackDefinition(
        result,
        packId,
        kind,
        NormalizeFullPath(directories[i]),
        stageRoot + "/" + packId
      );
    }
  }

  static void AddExistingPackDefinition(
    List<PackDefinition> result,
    string packId,
    string kind,
    string externalRootPath,
    string stageAssetRoot
  ) {
    if (result == null || string.IsNullOrWhiteSpace(packId) || string.IsNullOrWhiteSpace(externalRootPath)) return;
    if (!Directory.Exists(externalRootPath) && !File.Exists(Path.Combine(externalRootPath, ManifestFileName))) return;
    AddPackDefinition(result, packId, kind, externalRootPath, stageAssetRoot);
  }

  static void AddPackDefinition(
    List<PackDefinition> result,
    string packId,
    string kind,
    string externalRootPath,
    string stageAssetRoot
  ) {
    if (result == null || string.IsNullOrWhiteSpace(packId) || string.IsNullOrWhiteSpace(externalRootPath)) return;
    if (result.Any(pack => string.Equals(pack?.packId, packId, StringComparison.OrdinalIgnoreCase))) return;
    var pack = new PackDefinition {
      packId = packId,
      kind = kind,
      externalRootPath = externalRootPath,
      stageAssetRoot = stageAssetRoot,
      defaultLocationId = ""
    };
    result.Add(pack);
  }

  static void ApplyExistingPackManifests(List<PackDefinition> packDefinitions) {
    if (packDefinitions == null || packDefinitions.Count <= 0) return;

    for (var i = 0; i < packDefinitions.Count; i++) {
      var pack = packDefinitions[i];
      if (pack == null || string.IsNullOrWhiteSpace(pack.externalRootPath)) continue;

      var manifestPath = Path.Combine(pack.externalRootPath, ManifestFileName);
      if (!File.Exists(manifestPath)) continue;

      ContentPackManifestJson manifest;
      try {
        manifest = JsonUtility.FromJson<ContentPackManifestJson>(File.ReadAllText(manifestPath));
      }
      catch (Exception ex) {
        Debug.LogWarning(
          "[ContentPackPipeline] Failed to read content pack manifest." +
          " pack_id='" + pack.packId + "'" +
          " path='" + manifestPath + "'" +
          " error='" + ex.Message + "'"
        );
        continue;
      }

      ApplyExistingPackManifest(pack, manifest);
    }
  }

  static void ApplyExistingPackManifest(PackDefinition pack, ContentPackManifestJson manifest) {
    if (pack == null || manifest == null) return;

    pack.loadedManifest = true;
    if (!string.IsNullOrWhiteSpace(manifest.kind)) {
      pack.kind = NormalizeToken(manifest.kind);
    }

    ReplaceListIfPresent(pack.ownedRoots, manifest.ownedRoots);
    ReplaceListIfPresent(pack.ownedLocations, manifest.ownedLocations);
    ReplaceListIfPresent(pack.ownedEnemyTypes, manifest.ownedEnemyTypes);
    ReplaceListIfPresent(pack.dialogIds, manifest.dialogIds);

    pack.authoringSources.Clear();
    if (manifest.authoringSources == null || manifest.authoringSources.Count <= 0) {
      return;
    }

    for (var i = 0; i < manifest.authoringSources.Count; i++) {
      var source = NormalizeAuthoringSource(manifest.authoringSources[i]);
      if (source == null) continue;
      pack.authoringSources.Add(source);
    }

    if (pack.authoringSources.Count <= 0) return;

    pack.seedRoots.Clear();
    pack.manualLibraryNames.Clear();
    pack.targetRelativePathByAssetPath.Clear();
    for (var i = 0; i < pack.authoringSources.Count; i++) {
      AddUniquePath(pack.seedRoots, pack.authoringSources[i].assetPath);
      AddUniquePath(pack.ownedRoots, pack.authoringSources[i].assetPath);
    }
  }

  static ContentPackAuthoringSourceJson NormalizeAuthoringSource(ContentPackAuthoringSourceJson source) {
    if (source == null) return null;

    var rawAssetPath = NormalizeAssetPath(source.assetPath);
    var assetPath = StripAuthoringSliceSuffix(rawAssetPath);
    var sourceType = NormalizeToken(source.sourceType).ToLowerInvariant();
    var targetFolder = NormalizePackTargetFolder(source.targetFolder);
    if (string.IsNullOrWhiteSpace(sourceType) ||
        string.IsNullOrWhiteSpace(assetPath) ||
        string.IsNullOrWhiteSpace(targetFolder)) {
      return null;
    }

    return new ContentPackAuthoringSourceJson {
      sourceType = sourceType,
      assetPath = assetPath,
      label = string.IsNullOrWhiteSpace(source.label) ? ExtractAuthoringSliceLabel(rawAssetPath) : NormalizeToken(source.label),
      targetFolder = targetFolder
    };
  }

  static string StripAuthoringSliceSuffix(string assetPath) {
    var normalized = NormalizeAssetPath(assetPath);
    var bracketIndex = normalized.IndexOf('[', StringComparison.Ordinal);
    return bracketIndex >= 0 ? normalized.Substring(0, bracketIndex) : normalized;
  }

  static string ExtractAuthoringSliceLabel(string assetPath) {
    var normalized = NormalizeAssetPath(assetPath);
    var openIndex = normalized.IndexOf('[', StringComparison.Ordinal);
    var closeIndex = normalized.LastIndexOf(']');
    if (openIndex < 0 || closeIndex <= openIndex) return "";
    return normalized.Substring(openIndex + 1, closeIndex - openIndex - 1).Trim();
  }

  static string NormalizePackTargetFolder(string targetFolder) {
    var normalized = NormalizeAssetPath(targetFolder).Trim('/');
    if (string.IsNullOrWhiteSpace(normalized)) return "";
    if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) {
      normalized = normalized.Substring("Assets/".Length);
    }
    var segments = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
    if (segments.Length > 1 && string.Equals(segments[0], CorePackId, StringComparison.OrdinalIgnoreCase)) {
      normalized = string.Join("/", segments.Skip(1));
    }
    else if (segments.Length > 2 &&
             (string.Equals(segments[0], "Forms", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(segments[0], "Gears", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(segments[0], "Slices", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(segments[0], "Episodes", StringComparison.OrdinalIgnoreCase))) {
      normalized = string.Join("/", segments.Skip(2));
    }
    return NormalizeAssetPath(normalized).Trim('/');
  }

  static void ReplaceListIfPresent(List<string> target, List<string> source) {
    if (target == null || source == null) return;
    target.Clear();
    for (var i = 0; i < source.Count; i++) {
      AddUniquePath(target, source[i]);
    }
  }

  static List<PackDefinition> DiscoverGearPackDefinitions(string normalizedExternalRoot) {
    var result = new List<PackDefinition>();
    var groupedGearFullPath = GetPhysicalPath(EsperanzaGroupedGearRoot);
    if (!Directory.Exists(groupedGearFullPath)) {
      return result;
    }

    var gearDirectories = Directory.GetDirectories(groupedGearFullPath);
    Array.Sort(gearDirectories, StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < gearDirectories.Length; i++) {
      var dirPath = gearDirectories[i].Replace('\\', '/');
      var packId = Path.GetFileName(dirPath);
      if (string.IsNullOrWhiteSpace(packId) || !packId.StartsWith("Gear_", StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      var stageAssetRoot = StageGearsAssetPath + "/" + packId;
      var pack = new PackDefinition {
        packId = packId,
        kind = "gear",
        externalRootPath = NormalizeFullPath(Path.Combine(normalizedExternalRoot, "Gears", packId)),
        stageAssetRoot = stageAssetRoot,
        defaultLocationId = ""
      };

      var stagedSpritesFolder = stageAssetRoot + "/Sprites";
      pack.seedRoots.Add(stagedSpritesFolder);
      pack.ownedRoots.Add(stagedSpritesFolder);
      result.Add(pack);
    }

    return result;
  }

  static void AddCoreUiOwnedRoots(List<string> output) {
    if (output == null) return;

    AddUniquePath(output, "Assets/Sprites/Fonts");
    AddUniquePath(output, "Assets/Sprites/GameInterface");
  }

  static void PreparePackDependencies(
    PackDefinition pack,
    Dictionary<string, string> projectLibraries,
    List<string> errors
  ) {
    if (pack == null) return;

    if (pack.authoringSources != null && pack.authoringSources.Count > 0) {
      PrepareAuthoringSourceDependencies(pack, errors);
      return;
    }

    var seedAssetPaths = ExpandProjectRoots(pack.seedRoots, errors);
    var libraryNames = CollectReferencedLibraryNamesFromAssets(seedAssetPaths);
    for (var i = 0; i < pack.manualLibraryNames.Count; i++) {
      AddUniqueLibraryName(libraryNames, pack.manualLibraryNames[i]);
    }

    var libraryAssetPaths = ResolveLibraryAssetPaths(libraryNames, projectLibraries, errors);
    var allSeedPaths = new List<string>(seedAssetPaths.Count + libraryAssetPaths.Count);
    allSeedPaths.AddRange(seedAssetPaths);
    for (var i = 0; i < libraryAssetPaths.Count; i++) {
      AddUniquePath(allSeedPaths, libraryAssetPaths[i]);
    }

    pack.assetDependencies = CollectPackDependencies(allSeedPaths, errors);
  }

  static void PrepareAuthoringSourceDependencies(PackDefinition pack, List<string> errors) {
    pack.assetDependencies.Clear();
    pack.targetRelativePathByAssetPath.Clear();

    for (var i = 0; i < pack.authoringSources.Count; i++) {
      var source = pack.authoringSources[i];
      if (!ValidateAuthoringSource(source, errors)) {
        continue;
      }

      var dependencies = CollectPackDependencies(new List<string> { source.assetPath }, errors);
      for (var dependencyIndex = 0; dependencyIndex < dependencies.Count; dependencyIndex++) {
        AddUniquePath(pack.assetDependencies, dependencies[dependencyIndex]);
      }

      var targetRelativePath = BuildAuthoringSourceTargetRelativePath(source);
      if (string.IsNullOrWhiteSpace(targetRelativePath)) continue;
      if (pack.targetRelativePathByAssetPath.TryGetValue(source.assetPath, out var existingTarget) &&
          !string.Equals(existingTarget, targetRelativePath, StringComparison.OrdinalIgnoreCase)) {
        errors?.Add(
          "Authoring source maps to multiple target folders." +
          " pack_id='" + pack.packId + "'" +
          " asset='" + source.assetPath + "'" +
          " first='" + existingTarget + "'" +
          " second='" + targetRelativePath + "'"
        );
        continue;
      }
      pack.targetRelativePathByAssetPath[source.assetPath] = targetRelativePath;
    }
  }

  static bool ValidateAuthoringSource(ContentPackAuthoringSourceJson source, List<string> errors) {
    if (source == null) return false;
    var assetPath = NormalizeAssetPath(source.assetPath);
    var sourceType = NormalizeToken(source.sourceType);
    if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(sourceType)) return false;
    if (!File.Exists(Path.GetFullPath(assetPath))) {
      errors?.Add("Missing authoring source asset '" + assetPath + "'.");
      return false;
    }

    var extension = Path.GetExtension(assetPath);
    if (string.Equals(sourceType, "sprite_library", StringComparison.OrdinalIgnoreCase)) {
      if (!string.Equals(extension, ".spriteLib", StringComparison.OrdinalIgnoreCase)) {
        errors?.Add("Sprite library authoring source must be a .spriteLib asset. asset='" + assetPath + "'");
        return false;
      }
      return true;
    }

    if (string.Equals(sourceType, "sprite_slice", StringComparison.OrdinalIgnoreCase)) {
      if (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)) {
        errors?.Add("Sprite slice authoring source must be a .png asset. asset='" + assetPath + "'");
        return false;
      }
      if (string.IsNullOrWhiteSpace(source.label)) {
        errors?.Add("Sprite slice authoring source requires a slice label. asset='" + assetPath + "'");
        return false;
      }
      if (!TextureContainsSpriteSlice(assetPath, source.label)) {
        errors?.Add(
          "Sprite slice authoring source label was not found." +
          " asset='" + assetPath + "'" +
          " label='" + source.label + "'"
        );
        return false;
      }
      return true;
    }

    errors?.Add("Unknown authoring source type '" + sourceType + "' for asset '" + assetPath + "'.");
    return false;
  }

  static bool TextureContainsSpriteSlice(string assetPath, string label) {
    var normalizedLabel = NormalizeToken(label);
    if (string.IsNullOrWhiteSpace(normalizedLabel)) return false;

    var mainAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
    if (mainAsset != null && string.Equals(mainAsset.name, normalizedLabel, StringComparison.OrdinalIgnoreCase)) {
      return true;
    }

    var subAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath);
    for (var i = 0; i < subAssets.Length; i++) {
      if (subAssets[i] == null) continue;
      if (string.Equals(subAssets[i].name, normalizedLabel, StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
  }

  static string BuildAuthoringSourceTargetRelativePath(ContentPackAuthoringSourceJson source) {
    if (source == null) return "";
    var assetPath = NormalizeAssetPath(source.assetPath);
    var targetFolder = NormalizePackTargetFolder(source.targetFolder);
    if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(targetFolder)) return "";
    return NormalizeAssetPath(targetFolder + "/" + Path.GetFileName(assetPath));
  }

  static List<string> ResolveConcreteActivePackIds(
    List<string> selectedPackIds,
    Dictionary<string, PackDefinition> packById
  ) {
    var resolved = new List<string>();
    if (selectedPackIds == null || selectedPackIds.Count <= 0 || packById == null || packById.Count <= 0) {
      return resolved;
    }

    void AddSelectedPack(string packId) {
      var normalizedPackId = NormalizeToken(packId);
      if (string.IsNullOrWhiteSpace(normalizedPackId)) {
        return;
      }

      if (!packById.TryGetValue(normalizedPackId, out var pack) || pack == null) {
        return;
      }

      if (pack.stageForRuntime &&
          !string.IsNullOrWhiteSpace(pack.stageAssetRoot) &&
          !resolved.Contains(pack.packId, StringComparer.OrdinalIgnoreCase)) {
        resolved.Add(pack.packId);
      }
    }

    for (var i = 0; i < selectedPackIds.Count; i++) {
      AddSelectedPack(selectedPackIds[i]);
    }

    return resolved;
  }

}
#endif
