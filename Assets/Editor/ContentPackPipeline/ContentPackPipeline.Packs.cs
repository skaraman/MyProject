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
    ApplyCorePackDefaults(result);
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
      var directory = directories[i];
      var packId = Path.GetFileName(directory);
      if (string.IsNullOrWhiteSpace(packId)) {
        continue;
      }

      if (IsIgnoredExternalRootFolderName(packId)) {
        continue;
      }

      if (knownFolders.Contains(packId)) {
        continue;
      }

      if (!IsExternalRootPackCandidateDirectory(directory)) {
        continue;
      }

      AddExistingPackDefinition(
        result,
        packId,
        "pack",
        NormalizeFullPath(directory),
        StageRootAssetPath + "/" + packId
      );
    }
  }

  static bool IsExternalRootPackCandidateDirectory(string directory) {
    if (string.IsNullOrWhiteSpace(directory)) {
      return false;
    }

    if (IsIgnoredExternalRootFolderName(Path.GetFileName(directory))) {
      return false;
    }

    if (!Directory.Exists(directory)) {
      return false;
    }

    var manifestPath = Path.Combine(directory, ManifestFileName);
    if (File.Exists(manifestPath)) {
      return true;
    }

    try {
      var files = Directory.GetFiles(directory);
      for (var i = 0; i < files.Length; i++) {
        var fileName = Path.GetFileName(files[i]);
        if (fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) {
          continue;
        }

        return true;
      }

      var childDirectories = Directory.GetDirectories(directory);
      return childDirectories.Length > 0;
    }
    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) {
      Debug.LogWarning(
        "[ContentPackPipeline] Ignored unreadable external pack folder." +
        " path='" + directory + "'" +
        " error='" + ex.Message + "'"
      );
      return false;
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
    if (packId.StartsWith("Gear", StringComparison.OrdinalIgnoreCase)) return "gear";
    if (packId.StartsWith("Enemy", StringComparison.OrdinalIgnoreCase)) return "enemy";
    if (packId.StartsWith("Environment", StringComparison.OrdinalIgnoreCase)) return "environment";
    if (packId.StartsWith("Destructible", StringComparison.OrdinalIgnoreCase)) return "destructible";
    if (packId.StartsWith("Dialog", StringComparison.OrdinalIgnoreCase)) return "dialog";
    if (packId.EndsWith("UI", StringComparison.OrdinalIgnoreCase) ||
        packId.StartsWith("UI", StringComparison.OrdinalIgnoreCase)) return "ui";
    if (packId.StartsWith("Objective", StringComparison.OrdinalIgnoreCase)) return "objective";
    return "pack";
  }

  static string ResolveManifestPackExternalRoot(string normalizedRoot, string kind, string packId) {
    if (string.Equals(kind, "core", StringComparison.OrdinalIgnoreCase)) {
      return NormalizeFullPath(Path.Combine(normalizedRoot, "Core"));
    }
    return NormalizeFullPath(Path.Combine(normalizedRoot, packId));
  }

  static string ResolveManifestPackStageRoot(string kind, string packId) {
    if (string.Equals(kind, "core", StringComparison.OrdinalIgnoreCase)) return StageCoreAssetPath;
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
    var sliceById = new Dictionary<string, ContentManifestSliceJson>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < manifest.slices.Count; i++) {
      var slice = manifest.slices[i];
      var sliceId = NormalizeToken(slice?.id);
      if (string.IsNullOrWhiteSpace(sliceId)) continue;
      if (sliceById.ContainsKey(sliceId)) continue;
      sliceById.Add(sliceId, slice);
    }

    for (var i = 0; i < manifest.slices.Count; i++) {
      AddContentManifestSlicePackIds(
        result,
        manifest.slices[i],
        sliceById,
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
      );
    }
    return result;
  }

  static void AddContentManifestSlicePackIds(
    List<string> result,
    ContentManifestSliceJson slice,
    Dictionary<string, ContentManifestSliceJson> sliceById,
    HashSet<string> stack
  ) {
    if (result == null || slice == null || sliceById == null || stack == null) return;

    var sliceId = NormalizeToken(slice.id);
    if (string.IsNullOrWhiteSpace(sliceId)) return;
    if (stack.Contains(sliceId)) return;

    stack.Add(sliceId);
    var ids = GetContentManifestSliceIds(slice);
    for (var i = 0; i < ids.Count; i++) {
      var manifestId = NormalizeToken(ids[i]);
      if (string.IsNullOrWhiteSpace(manifestId)) continue;
      if (
        !string.Equals(manifestId, sliceId, StringComparison.OrdinalIgnoreCase) &&
        sliceById.TryGetValue(manifestId, out var childSlice)
      ) {
        AddContentManifestSlicePackIds(result, childSlice, sliceById, stack);
        continue;
      }
      AddUniquePath(result, manifestId);
    }
    stack.Remove(sliceId);
  }

  static List<string> GetContentManifestSliceIds(ContentManifestSliceJson slice) {
    if (slice == null) return new List<string>();
    if (slice.ids != null) return slice.ids;
    return slice.packs ?? new List<string>();
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
      if (IsIgnoredExternalRootFolderName(packId)) continue;
      AddExistingPackDefinition(
        result,
        packId,
        kind,
        NormalizeFullPath(directories[i]),
        stageRoot + "/" + packId
      );
    }
  }

  static bool IsIgnoredExternalRootFolderName(string folderName) {
    var normalized = NormalizeToken(folderName);
    if (string.IsNullOrWhiteSpace(normalized)) {
      return true;
    }

    if (IgnoredExternalRootFolderNames.Contains(normalized)) {
      return true;
    }

    return normalized.StartsWith(".", StringComparison.Ordinal);
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
    var manifestKind = NormalizeToken(string.IsNullOrWhiteSpace(manifest.type) ? manifest.kind : manifest.type);
    if (!string.IsNullOrWhiteSpace(manifestKind) &&
        !string.Equals(manifestKind, "pack", StringComparison.OrdinalIgnoreCase)) {
      pack.kind = manifestKind;
    }
    else {
      pack.kind = InferManifestPackKind(pack.packId);
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
      AddUniquePath(pack.seedRoots, pack.authoringSources[i].normalAssetPath);
      AddUniquePath(pack.ownedRoots, pack.authoringSources[i].normalAssetPath);
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
      targetFolder = targetFolder,
      libraryName = NormalizeAssetPath(source.libraryName),
      category = NormalizeToken(source.category),
      labelPrefix = NormalizeToken(source.labelPrefix),
      normalAssetPath = StripAuthoringSliceSuffix(NormalizeAssetPath(source.normalAssetPath))
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

  static void ApplyCorePackDefaults(List<PackDefinition> packDefinitions) {
    if (packDefinitions == null) return;

    for (var i = 0; i < packDefinitions.Count; i++) {
      var pack = packDefinitions[i];
      if (pack == null) continue;
      if (!string.Equals(pack.packId, CorePackId, StringComparison.OrdinalIgnoreCase)) continue;

      AddCoreGameplayMaterialOwnedRoots(pack.seedRoots);
      AddCoreGameplayMaterialOwnedRoots(pack.ownedRoots);
      return;
    }
  }

  static void AddCoreGameplayMaterialOwnedRoots(List<string> output) {
    if (output == null) return;

    AddUniquePath(output, GameplayCoreAssetPaths.EsperanzaGearMaterialAssetPath);
    AddUniquePath(output, GameplayCoreAssetPaths.EsperanzaHairMaterialAssetPath);
    AddUniquePath(output, GameplayCoreAssetPaths.EsperanzaBodyMaterialAssetPath);
  }

  static void PreparePackDependencies(
    PackDefinition pack,
    Dictionary<string, string> projectLibraries,
    List<string> errors
  ) {
    if (pack == null) return;

    if (pack.authoringSources != null && pack.authoringSources.Count > 0) {
      PrepareAuthoringSourceDependencies(pack, errors);
      AddSeedRootDependencies(pack, projectLibraries, errors);
    }
    else {
      pack.assetDependencies.Clear();
      AddSeedRootDependencies(pack, projectLibraries, errors);
    }

  }

  static void AddSeedRootDependencies(
    PackDefinition pack,
    Dictionary<string, string> projectLibraries,
    List<string> errors
  ) {
    if (pack == null) return;

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

    var dependencies = CollectPackDependencies(allSeedPaths, errors);
    for (var i = 0; i < dependencies.Count; i++) {
      AddUniquePath(pack.assetDependencies, dependencies[i]);
    }
  }

  static void PrepareAuthoringSourceDependencies(PackDefinition pack, List<string> errors) {
    pack.assetDependencies.Clear();
    pack.targetRelativePathByAssetPath.Clear();

    for (var i = 0; i < pack.authoringSources.Count; i++) {
      var source = pack.authoringSources[i];
      if (!ValidateAuthoringSource(source, errors)) {
        continue;
      }

      AddUniquePath(pack.assetDependencies, source.assetPath);
      var dependencies = CollectPackDependencies(new List<string> { source.assetPath }, errors);
      for (var dependencyIndex = 0; dependencyIndex < dependencies.Count; dependencyIndex++) {
        AddUniquePath(pack.assetDependencies, dependencies[dependencyIndex]);
      }

      RegisterAuthoringSourceTarget(pack, source.assetPath, source, errors);

      if (!string.IsNullOrWhiteSpace(source.normalAssetPath)) {
        AddUniquePath(pack.assetDependencies, source.normalAssetPath);
        var normalDependencies = CollectPackDependencies(new List<string> { source.normalAssetPath }, errors);
        for (var dependencyIndex = 0; dependencyIndex < normalDependencies.Count; dependencyIndex++) {
          AddUniquePath(pack.assetDependencies, normalDependencies[dependencyIndex]);
        }

        RegisterAuthoringSourceTarget(pack, source.normalAssetPath, source, errors);
      }
    }
  }

  static void RegisterAuthoringSourceTarget(
    PackDefinition pack,
    string assetPath,
    ContentPackAuthoringSourceJson source,
    List<string> errors
  ) {
    if (pack == null || source == null || string.IsNullOrWhiteSpace(assetPath)) return;

    var targetRelativePath = BuildAuthoringSourceTargetRelativePath(source, assetPath);
    if (string.IsNullOrWhiteSpace(targetRelativePath)) return;
    if (pack.targetRelativePathByAssetPath.TryGetValue(assetPath, out var existingTarget) &&
        !string.Equals(existingTarget, targetRelativePath, StringComparison.OrdinalIgnoreCase)) {
      errors?.Add(
        "Authoring source maps to multiple target folders." +
        " pack_id='" + pack.packId + "'" +
        " asset='" + assetPath + "'" +
        " first='" + existingTarget + "'" +
        " second='" + targetRelativePath + "'"
      );
      return;
    }

    pack.targetRelativePathByAssetPath[assetPath] = targetRelativePath;
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
    if (string.Equals(sourceType, "sprite_sheet", StringComparison.OrdinalIgnoreCase)) {
      if (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)) {
        errors?.Add("Sprite sheet authoring source must be a .png asset. asset='" + assetPath + "'");
        return false;
      }
      if (string.IsNullOrWhiteSpace(source.libraryName) ||
          string.IsNullOrWhiteSpace(source.category) ||
          string.IsNullOrWhiteSpace(source.labelPrefix)) {
        errors?.Add("Sprite sheet authoring source requires libraryName, category, and labelPrefix. asset='" + assetPath + "'");
        return false;
      }
      if (!string.IsNullOrWhiteSpace(source.normalAssetPath)) {
        var normalAssetPath = NormalizeAssetPath(source.normalAssetPath);
        if (!File.Exists(Path.GetFullPath(normalAssetPath))) {
          errors?.Add("Missing sprite sheet normal asset '" + normalAssetPath + "'.");
          return false;
        }
        if (!string.Equals(Path.GetExtension(normalAssetPath), ".png", StringComparison.OrdinalIgnoreCase)) {
          errors?.Add("Sprite sheet normal authoring source must be a .png asset. asset='" + normalAssetPath + "'");
          return false;
        }
      }
      return true;
    }

    if (string.Equals(sourceType, "sprite_library", StringComparison.OrdinalIgnoreCase)) {
      if (!string.Equals(extension, SpriteStreamingConfig.CustomSpriteLibraryExtension, StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(extension, SpriteStreamingConfig.LegacySpriteLibraryExtension, StringComparison.OrdinalIgnoreCase)) {
        errors?.Add("Sprite library authoring source must be a " + SpriteStreamingConfig.CustomSpriteLibraryExtension + " asset. asset='" + assetPath + "'");
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

    if (string.Equals(sourceType, "text_asset", StringComparison.OrdinalIgnoreCase)) {
      if (!string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase)) {
        errors?.Add("Text authoring source must be a .json or .txt asset. asset='" + assetPath + "'");
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

  static string BuildAuthoringSourceTargetRelativePath(ContentPackAuthoringSourceJson source, string assetPathOverride = null) {
    if (source == null) return "";
    var assetPath = NormalizeAssetPath(string.IsNullOrWhiteSpace(assetPathOverride) ? source.assetPath : assetPathOverride);
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

    ReadContentManifestSelectionMaps(out var sliceById, out var episodeById);

    void AddSelectedPack(string packId, HashSet<string> stack) {
      var normalizedPackId = NormalizeToken(packId);
      if (string.IsNullOrWhiteSpace(normalizedPackId)) {
        return;
      }

      if (episodeById.TryGetValue(normalizedPackId, out var episode) && episode != null) {
        AddEpisode(episode, stack);
        return;
      }

      if (sliceById.TryGetValue(normalizedPackId, out var slice) && slice != null) {
        AddSlice(slice, stack);
        return;
      }

      if (!packById.TryGetValue(normalizedPackId, out var pack) || pack == null) {
        return;
      }

      AddResolvedRuntimePackId(resolved, pack);
    }

    void AddEpisode(ContentManifestEpisodeJson episode, HashSet<string> stack) {
      var episodeId = NormalizeToken(episode?.id);
      if (string.IsNullOrWhiteSpace(episodeId)) {
        return;
      }

      if (!stack.Add("episode:" + episodeId)) {
        return;
      }

      var slices = episode.slices ?? new List<string>();
      for (var i = 0; i < slices.Count; i++) {
        AddSelectedPack(slices[i], stack);
      }

      stack.Remove("episode:" + episodeId);
    }

    void AddSlice(ContentManifestSliceJson slice, HashSet<string> stack) {
      var sliceId = NormalizeToken(slice?.id);
      if (string.IsNullOrWhiteSpace(sliceId)) {
        return;
      }

      if (!stack.Add("slice:" + sliceId)) {
        return;
      }

      var ids = GetContentManifestSliceIds(slice);
      for (var i = 0; i < ids.Count; i++) {
        AddSelectedPack(ids[i], stack);
      }

      stack.Remove("slice:" + sliceId);
    }

    for (var i = 0; i < selectedPackIds.Count; i++) {
      AddSelectedPack(selectedPackIds[i], new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    AddSaveDrivenRuntimePackIds(resolved, packById);
    return resolved;
  }

  static void AddSaveDrivenRuntimePackIds(
    List<string> resolved,
    Dictionary<string, PackDefinition> packById
  ) {
    if (resolved == null || packById == null || packById.Count <= 0) {
      return;
    }

    if (!IsGameplayContentRequested(packById, resolved)) {
      return;
    }

    var packIds = new List<string>();
    foreach (var entry in packById) {
      var pack = entry.Value;
      if (!IsSaveDrivenRuntimePack(pack)) {
        continue;
      }

      var packId = NormalizeToken(pack.packId);
      if (string.IsNullOrWhiteSpace(packId)) {
        continue;
      }

      if (packIds.Contains(packId, StringComparer.OrdinalIgnoreCase)) {
        continue;
      }

      packIds.Add(packId);
    }

    packIds.Sort(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < packIds.Count; i++) {
      if (!packById.TryGetValue(packIds[i], out var pack)) {
        continue;
      }

      AddResolvedRuntimePackId(resolved, pack);
    }
  }

  static bool IsSaveDrivenRuntimePack(PackDefinition pack) {
    if (pack == null) {
      return false;
    }

    var packId = NormalizeToken(pack.packId);
    if (string.IsNullOrWhiteSpace(packId)) {
      return false;
    }

    if (IsFormUiRuntimePackId(packId)) {
      return true;
    }

    if (string.Equals(NormalizeToken(pack.kind), "gear", StringComparison.OrdinalIgnoreCase)) {
      return true;
    }

    return packId.StartsWith("Gear", StringComparison.OrdinalIgnoreCase);
  }

  static bool IsFormUiRuntimePackId(string packId) {
    var normalizedPackId = NormalizeToken(packId);
    if (!normalizedPackId.EndsWith("UI", StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    var form = normalizedPackId.Substring(0, normalizedPackId.Length - "UI".Length);
    return !string.IsNullOrWhiteSpace(EsperanzaForms.ResolveFormKey(form));
  }

  static void AddResolvedRuntimePackId(List<string> resolved, PackDefinition pack) {
    if (resolved == null || pack == null) {
      return;
    }

    var packId = NormalizeToken(pack.packId);
    if (string.IsNullOrWhiteSpace(packId)) {
      return;
    }

    if (!pack.stageForRuntime) {
      return;
    }

    if (string.IsNullOrWhiteSpace(pack.stageAssetRoot)) {
      return;
    }

    if (resolved.Contains(packId, StringComparer.OrdinalIgnoreCase)) {
      return;
    }

    resolved.Add(packId);
  }

  static bool IsContentManifestSelectionId(string id) {
    var normalizedId = NormalizeToken(id);
    if (string.IsNullOrWhiteSpace(normalizedId)) {
      return false;
    }

    ReadContentManifestSelectionMaps(out var sliceById, out var episodeById);
    return sliceById.ContainsKey(normalizedId) || episodeById.ContainsKey(normalizedId);
  }

  static void ReadContentManifestSelectionMaps(
    out Dictionary<string, ContentManifestSliceJson> sliceById,
    out Dictionary<string, ContentManifestEpisodeJson> episodeById
  ) {
    sliceById = new Dictionary<string, ContentManifestSliceJson>(StringComparer.OrdinalIgnoreCase);
    episodeById = new Dictionary<string, ContentManifestEpisodeJson>(StringComparer.OrdinalIgnoreCase);

    var path = Path.Combine(GetProjectRoot(), "Assets", "ContentManifest.json");
    if (!File.Exists(path)) {
      return;
    }

    ContentManifestJson manifest;
    try {
      manifest = JsonUtility.FromJson<ContentManifestJson>(File.ReadAllText(path));
    }
    catch (Exception ex) {
      Debug.LogWarning("[ContentPackPipeline] Failed to read content manifest selection maps. error='" + ex.Message + "'");
      return;
    }

    if (manifest == null) {
      return;
    }

    if (manifest.slices != null) {
      for (var i = 0; i < manifest.slices.Count; i++) {
        var slice = manifest.slices[i];
        var sliceId = NormalizeToken(slice?.id);
        if (string.IsNullOrWhiteSpace(sliceId)) continue;
        if (sliceById.ContainsKey(sliceId)) continue;

        sliceById.Add(sliceId, slice);
      }
    }

    if (manifest.episodes != null) {
      for (var i = 0; i < manifest.episodes.Count; i++) {
        var episode = manifest.episodes[i];
        var episodeId = NormalizeToken(episode?.id);
        if (string.IsNullOrWhiteSpace(episodeId)) continue;
        if (episodeById.ContainsKey(episodeId)) continue;

        episodeById.Add(episodeId, episode);
      }
    }
  }

}
#endif
