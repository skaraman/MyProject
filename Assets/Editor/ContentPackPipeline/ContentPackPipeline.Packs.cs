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

    var core = new PackDefinition {
      packId = CorePackId,
      kind = "core",
      externalRootPath = NormalizeFullPath(Path.Combine(normalizedRoot, "Core")),
      stageAssetRoot = StageCoreAssetPath,
      defaultLocationId = "",
    };
    core.seedRoots.Add("Assets/Prefabs/Characters/ESPER.prefab");
    core.seedRoots.Add("Assets/Sprites/Characters/Esperanza/GroupedGearAtlases/Skin");
    core.seedRoots.Add("Assets/Sprites/Characters/Esperanza/Expressions/Base");
    core.seedRoots.Add("Assets/Sprites/Characters/Esperanza/_Bounces");
    core.seedRoots.Add("Assets/Prefabs/Fonts/FontCharacter.prefab");
    AddCoreUiOwnedRoots(core.seedRoots);
    foreach (var projectile in Projectiles.EnumerateAll()) {
      var projectilePrefabPath = NormalizeAssetPath(projectile.Value?.prefabAddress);
      if (string.IsNullOrWhiteSpace(projectilePrefabPath) ||
          string.Equals(projectilePrefabPath, "Assets/Prefabs/Projectiles/BlastBall.prefab", StringComparison.OrdinalIgnoreCase)) {
        continue;
      }
      AddUniquePath(core.seedRoots, projectilePrefabPath);
    }
    core.manualLibraryNames.Add("Dialog/DialogEsper");
    core.manualLibraryNames.Add("Dialog/DialogUI");
    core.manualLibraryNames.Add("Items/Items");
    core.manualLibraryNames.Add("MainMenu/MainMenu");
    core.manualLibraryNames.Add("UI/CharUI");
    core.manualLibraryNames.Add("UI/MapMenus");
    core.manualLibraryNames.Add("UI/SelectMenus");
    core.ownedRoots.AddRange(core.seedRoots);

    var baseForm = new PackDefinition {
      packId = BaseFormPackId,
      kind = "form",
      externalRootPath = NormalizeFullPath(Path.Combine(normalizedRoot, "Forms", BaseFormPackId)),
      stageAssetRoot = StageFormsAssetPath + "/" + BaseFormPackId,
      defaultLocationId = ""
    };
    baseForm.requiredPackIds.Add(CorePackId);
    baseForm.seedRoots.Add("Assets/Prefabs/Projectiles/BlastBall.prefab");
    baseForm.seedRoots.Add("Assets/Sprites/Characters/Esperanza/Effects");
    baseForm.ownedRoots.AddRange(baseForm.seedRoots);

    var gearPacks = DiscoverGearPackDefinitions(normalizedRoot);

    var slice = new PackDefinition {
      packId = SlicePackId,
      kind = "slice",
      externalRootPath = NormalizeFullPath(Path.Combine(normalizedRoot, "Slices", SlicePackId)),
      stageAssetRoot = StageSlicesAssetPath + "/" + SlicePackId,
      defaultLocationId = LocationEnemyData.DomeCityLocationId,
      snapshotRelativePath = PackDataFolderName + "/" + DomeCityLocationSnapshotFileName,
      dialogSnapshotRelativePath = PackDataFolderName + "/" + DomeCityDialogSnapshotFileName
    };
    slice.requiredPackIds.Add(CorePackId);
    slice.requiredPackIds.Add(BaseFormPackId);
    slice.seedRoots.Add("Assets/Prefabs/Locations/DomeCity.prefab");
    slice.seedRoots.Add("Assets/Prefabs/Enemies/Imp.prefab");
    slice.seedRoots.Add("Assets/Sprites/Characters/Enemies/Imp");
    slice.manualLibraryNames.Add("Dialog/DialogImp");
    slice.ownedRoots.AddRange(slice.seedRoots);
    slice.ownedLocations.Add(LocationEnemyData.DomeCityLocationId);
    slice.ownedEnemyTypes.Add("Imp");
    slice.dialogIds.Add(LocationEnemyData.DomeCityLocationId);

    var homebase = new PackDefinition {
      packId = HomebaseSlicePackId,
      kind = "slice",
      externalRootPath = NormalizeFullPath(Path.Combine(normalizedRoot, "Slices", HomebaseSlicePackId)),
      stageAssetRoot = StageSlicesAssetPath + "/" + HomebaseSlicePackId,
      stageForRuntime = false
    };
    homebase.requiredPackIds.Add(CorePackId);

    var sunkenCave = new PackDefinition {
      packId = SunkenCaveSlicePackId,
      kind = "slice",
      externalRootPath = NormalizeFullPath(Path.Combine(normalizedRoot, "Slices", SunkenCaveSlicePackId)),
      stageAssetRoot = StageSlicesAssetPath + "/" + SunkenCaveSlicePackId,
      stageForRuntime = false
    };
    sunkenCave.requiredPackIds.Add(CorePackId);

    var episode = new PackDefinition {
      packId = EpisodePackId,
      kind = "episode",
      externalRootPath = NormalizeFullPath(Path.Combine(normalizedRoot, "Episodes", EpisodePackId)),
      stageAssetRoot = StageRootAssetPath + "/Episodes/" + EpisodePackId,
      stageForRuntime = false,
      defaultLocationId = LocationEnemyData.DomeCityLocationId,
    };
    episode.requiredPackIds.Add(SlicePackId);
    episode.requiredPackIds.Add(HomebaseSlicePackId);
    episode.requiredPackIds.Add(SunkenCaveSlicePackId);

    var result = new List<PackDefinition> { core, baseForm };
    result.AddRange(gearPacks);
    result.Add(slice);
    result.Add(homebase);
    result.Add(sunkenCave);
    result.Add(episode);
    return result;
  }

  static List<PackDefinition> DiscoverGearPackDefinitions(string normalizedExternalRoot) {
    var result = new List<PackDefinition>();
    var groupedGearRoot = NormalizeAssetPath(EsperanzaGroupedGearRoot);
    var groupedGearFullPath = Path.GetFullPath(groupedGearRoot);
    if (!Directory.Exists(groupedGearFullPath)) {
      return result;
    }

    var formDirectories = Directory.GetDirectories(groupedGearFullPath);
    Array.Sort(formDirectories, StringComparer.OrdinalIgnoreCase);
    for (var formIndex = 0; formIndex < formDirectories.Length; formIndex++) {
      var formAssetPath = ToProjectAssetPath(formDirectories[formIndex]);
      var formName = Path.GetFileName(formAssetPath);
      if (string.IsNullOrWhiteSpace(formName) ||
          string.Equals(formName, "Skin", StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      var gearDirectories = Directory.GetDirectories(formDirectories[formIndex]);
      Array.Sort(gearDirectories, StringComparer.OrdinalIgnoreCase);
      for (var gearIndex = 0; gearIndex < gearDirectories.Length; gearIndex++) {
        var gearAssetPath = ToProjectAssetPath(gearDirectories[gearIndex]);
        var gearCode = Path.GetFileName(gearAssetPath);
        if (string.IsNullOrWhiteSpace(gearCode)) {
          continue;
        }

        var leafDirectories = Directory.GetDirectories(gearDirectories[gearIndex]);
        Array.Sort(leafDirectories, StringComparer.OrdinalIgnoreCase);
        for (var leafIndex = 0; leafIndex < leafDirectories.Length; leafIndex++) {
          var leafAssetPath = ToProjectAssetPath(leafDirectories[leafIndex]);
          var leafCode = Path.GetFileName(leafAssetPath);
          var packId = EquippedItems.BuildGearPackId(formName + "_" + gearCode, leafCode);
          if (string.IsNullOrWhiteSpace(packId)) {
            continue;
          }

          var pack = new PackDefinition {
            packId = packId,
            kind = "gear",
            externalRootPath = NormalizeFullPath(Path.Combine(normalizedExternalRoot, "Gears", packId)),
            stageAssetRoot = StageGearsAssetPath + "/" + packId,
            defaultLocationId = ""
          };
          pack.requiredPackIds.Add(CorePackId);
          pack.seedRoots.Add(leafAssetPath);
          pack.ownedRoots.Add(leafAssetPath);
          result.Add(pack);
        }
      }
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

  static List<string> ResolveConcreteActivePackIds(
    List<string> selectedPackIds,
    Dictionary<string, PackDefinition> packById
  ) {
    var resolved = new List<string>();
    if (selectedPackIds == null || selectedPackIds.Count <= 0 || packById == null || packById.Count <= 0) {
      return resolved;
    }

    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    void Visit(string packId) {
      var normalizedPackId = NormalizeToken(packId);
      if (string.IsNullOrWhiteSpace(normalizedPackId) || !visited.Add(normalizedPackId)) {
        return;
      }

      if (!packById.TryGetValue(normalizedPackId, out var pack) || pack == null) {
        return;
      }

      if (pack.requiredPackIds != null) {
        for (var i = 0; i < pack.requiredPackIds.Count; i++) {
          Visit(pack.requiredPackIds[i]);
        }
      }

      if (pack.stageForRuntime && !string.IsNullOrWhiteSpace(pack.stageAssetRoot)) {
        resolved.Add(pack.packId);
      }
    }

    for (var i = 0; i < selectedPackIds.Count; i++) {
      Visit(selectedPackIds[i]);
    }

    var equippedGearPackIds = ResolveEquippedGearPackIds(packById);
    for (var i = 0; i < equippedGearPackIds.Count; i++) {
      Visit(equippedGearPackIds[i]);
    }

    return resolved;
  }

  static List<string> ResolveEquippedGearPackIds(Dictionary<string, PackDefinition> packById) {
    var result = new List<string>();
    if (packById == null || packById.Count <= 0) {
      return result;
    }

    var equippedGearIds = EquippedItems.GetEquippedGearIds();
    if (equippedGearIds.Count <= 0) {
      return result;
    }

    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var pair in packById) {
      var pack = pair.Value;
      if (pack == null || !string.Equals(pack.kind, "gear", StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      if (!EquippedItems.TryParseGearPackId(pack.packId, out var gearForm, out var gearCode, out _)) {
        continue;
      }

      var equippedGearId = NormalizeToken(gearForm + "_" + gearCode);
      if (string.IsNullOrWhiteSpace(equippedGearId) ||
          !equippedGearIds.Contains(equippedGearId, StringComparer.OrdinalIgnoreCase) ||
          !seen.Add(pack.packId)) {
        continue;
      }

      result.Add(pack.packId);
    }

    result.Sort(StringComparer.OrdinalIgnoreCase);
    return result;
  }
}
#endif
