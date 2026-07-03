#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed partial class GroupAtlasWindow : EditorWindow {
  void RebindGroupedSpriteLibraries() {
    if (!TryGetRebindSourceFolderPath(out var sourceFolderPath, true)) return;

    if (!TryGetRebindSpriteLibraryFolderPath(out var libraryFolderPath, true)) return;

    var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
    var buildIndexStopwatch = System.Diagnostics.Stopwatch.StartNew();
    if (!TryBuildGroupedSpriteReplacementIndex(sourceFolderPath, out var replacementIndex, out var error)) {
      EditorUtility.DisplayDialog("Rebind Failed", error, "OK");
      return;
    }
    buildIndexStopwatch.Stop();

    AtlasAuthoringLog.Verbose(
      "[GearGroupAtlas] Prepared rebind index." +
      " grouped_root='" + sourceFolderPath + "'" +
      " library_root='" + libraryFolderPath + "'" +
      " metadata_files=" + replacementIndex.metadataFileCount +
      " indexed_sprites=" + replacementIndex.spritesByKey.Count +
      " duplicate_keys=" + replacementIndex.duplicateKeyCount +
      " filled_slice_gaps=" + replacementIndex.filledSliceGapCount +
      " build_ms=" + buildIndexStopwatch.ElapsedMilliseconds);

    LogGroupedSpriteReplacementDuplicateSummary(replacementIndex);

    var rebindStopwatch = new System.Diagnostics.Stopwatch();
    var cleanupStopwatch = new System.Diagnostics.Stopwatch();
    var deletedAssets = 0;
    var touchedLibraries = 0;
    var reboundEntries = 0;
    var deletedLabels = 0;
    var createdCategories = 0;
    var createdLabels = 0;
    var unchangedEntries = 0;
    var processedLibraryKinds = new HashSet<bool>();
    var assetEditingStarted = false;
    try {
      AssetDatabase.StartAssetEditing();
      assetEditingStarted = true;

      rebindStopwatch.Start();
      RebindSpriteLibraries(
        libraryFolderPath,
        replacementIndex,
        out touchedLibraries,
        out reboundEntries,
        out deletedLabels,
        out createdCategories,
        out createdLabels,
        out unchangedEntries,
        out processedLibraryKinds);
      rebindStopwatch.Stop();

      cleanupStopwatch.Start();
      var cleanupPlans = replacementIndex.cleanupPlans
        .Where(plan => plan != null && processedLibraryKinds.Contains(plan.isSkinLibrary))
        .ToList();
      deletedAssets = reboundEntries > 0 && cleanupPlans.Count > 0 ? CleanupStaleOutputs(cleanupPlans) : 0;
      cleanupStopwatch.Stop();
    }
    finally {
      if (assetEditingStarted) {
        AssetDatabase.StopAssetEditing();
      }
    }

    totalStopwatch.Stop();
    AtlasAuthoringLog.Info(
      "[GearGroupAtlas] Rebind complete." +
      " grouped_root='" + sourceFolderPath + "'" +
      " library_root='" + libraryFolderPath + "'" +
      " metadata_files=" + replacementIndex.metadataFileCount +
      " indexed_sprites=" + replacementIndex.spritesByKey.Count +
      " duplicate_keys=" + replacementIndex.duplicateKeyCount +
      " filled_slice_gaps=" + replacementIndex.filledSliceGapCount +
      " libraries=" + touchedLibraries +
      " rebound_entries=" + reboundEntries +
      " unchanged_entries=" + unchangedEntries +
      " deleted_labels=" + deletedLabels +
      " created_categories=" + createdCategories +
      " created_labels=" + createdLabels +
      " cleaned_assets=" + deletedAssets +
      " build_ms=" + buildIndexStopwatch.ElapsedMilliseconds +
      " rebind_ms=" + rebindStopwatch.ElapsedMilliseconds +
      " cleanup_ms=" + cleanupStopwatch.ElapsedMilliseconds +
      " total_ms=" + totalStopwatch.ElapsedMilliseconds +
      " deferred_import=True");
  }

  static void LogGroupedSpriteReplacementDuplicateSummary(GroupedSpriteReplacementIndex replacementIndex) {
    if (replacementIndex == null || replacementIndex.duplicateKeyCount <= 0) return;

    var summary =
      "[GearGroupAtlas] Duplicate grouped sprite replacement keys were collapsed." +
      " duplicates=" + replacementIndex.duplicateKeyCount +
      " sampled=" + replacementIndex.duplicateKeySamples.Count;
    if (replacementIndex.duplicateKeySamples.Count <= 0) {
      AtlasAuthoringLog.Warning(summary);
      return;
    }

    var samples = replacementIndex.duplicateKeySamples
      .Select(sample => "  " + sample)
      .ToList();
    AtlasAuthoringLog.WarningWithSamples(summary, samples);
  }

  bool TryBuildGroupedSpriteReplacementIndex(
    string sourceFolderPath,
    out GroupedSpriteReplacementIndex replacementIndex,
    out string error) {
    replacementIndex = new GroupedSpriteReplacementIndex();
    error = "";

    var groupedOutputRoot = NormalizePath(sourceFolderPath).TrimEnd('/');
    if (string.IsNullOrWhiteSpace(groupedOutputRoot)) {
      error = "Missing grouped atlas folder.";
      return false;
    }

    var sourceFolderFullPath = Path.GetFullPath(groupedOutputRoot);
    if (!Directory.Exists(sourceFolderFullPath)) {
      error = "Grouped atlas folder does not exist on disk: " + sourceFolderFullPath;
      return false;
    }

    var cleanupPlansByKey = new Dictionary<string, CleanupPlan>(StringComparer.OrdinalIgnoreCase);
    var sequencedPendingReplacementsByKey = new Dictionary<LibraryEntrySequenceKey, List<PendingGroupedSpriteReplacement>>();
    var directPendingReplacementsByKey = new Dictionary<LibraryEntryKey, List<PendingGroupedSpriteReplacement>>();
    var metadataFullPaths = Directory.GetFiles(sourceFolderFullPath, "*.json", SearchOption.AllDirectories)
      .Where(path => !TrimmedAtlasExporterWindow.IsEditorMetadataAssetPath(path))
      .ToArray();
    Array.Sort(metadataFullPaths, StringComparer.OrdinalIgnoreCase);

    for (var metadataIndex = 0; metadataIndex < metadataFullPaths.Length; metadataIndex++) {
      var metadataFullPath = metadataFullPaths[metadataIndex];
      if (!TryConvertFullPathToAssetPath(metadataFullPath, out var metadataAssetPath)) continue;

      metadataAssetPath = NormalizePath(metadataAssetPath);
      if (!metadataAssetPath.StartsWith(groupedOutputRoot + "/", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(metadataAssetPath, groupedOutputRoot, StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      if (!TryLoadGroupedMetadataPayload(metadataAssetPath, out var payload, out error)) {
        error = "Failed to read grouped atlas metadata '" + metadataAssetPath + "': " + error;
        return false;
      }

      if (payload == null || payload.sprites == null || payload.sprites.Count <= 0) continue;
      var isNormalAtlas = string.Equals(payload.sourceKind, "normal", StringComparison.OrdinalIgnoreCase);
      if (isNormalAtlas) continue;

      var atlasAssetPath = NormalizePath(Path.ChangeExtension(metadataAssetPath, ".png"));
      if (!File.Exists(Path.GetFullPath(atlasAssetPath))) {
        error = "Grouped atlas texture is missing for metadata '" + metadataAssetPath + "'. Expected '" + atlasAssetPath + "'.";
        return false;
      }

      var spriteReferencesByName = BuildSpriteReferenceLookupByName(atlasAssetPath);
      if (spriteReferencesByName.Count <= 0) {
        error = "Grouped atlas '" + atlasAssetPath + "' has no sliced sprites to rebind.";
        return false;
      }

      var isSkinLibrary = IsSkinGroupKey(payload.groupKey);
      replacementIndex.metadataFileCount++;
      if (TryBuildCleanupPlanKey(atlasAssetPath, out var cleanupFolderPath, out var cleanupFilePrefix, out var useNumberedPageNames)) {
        var cleanupKey = cleanupFolderPath + "|" + cleanupFilePrefix + "|" + useNumberedPageNames;
        if (!cleanupPlansByKey.TryGetValue(cleanupKey, out var cleanupPlan) || cleanupPlan == null) {
          cleanupPlan = new CleanupPlan {
            folderPath = cleanupFolderPath,
            filePrefix = cleanupFilePrefix,
            useNumberedPageNames = useNumberedPageNames,
            isSkinLibrary = isSkinLibrary
          };
          cleanupPlansByKey[cleanupKey] = cleanupPlan;
        }

        cleanupPlan.keepAssetPaths.Add(atlasAssetPath);
        AddMetadataAssetPaths(cleanupPlan.keepAssetPaths, atlasAssetPath);
      }

      for (var spriteIndex = 0; spriteIndex < payload.sprites.Count; spriteIndex++) {
        var groupedSprite = payload.sprites[spriteIndex];
        if (groupedSprite == null || string.IsNullOrWhiteSpace(groupedSprite.name)) continue;
        if (!spriteReferencesByName.TryGetValue(groupedSprite.name, out var replacementSprite) || !replacementSprite.IsValid) continue;

        var sourceCategory = string.IsNullOrWhiteSpace(groupedSprite.sourceCategory)
          ? (payload.category ?? "").Trim()
          : groupedSprite.sourceCategory.Trim();
        if (string.IsNullOrWhiteSpace(sourceCategory)) continue;

        var partCode = string.IsNullOrWhiteSpace(groupedSprite.sourcePartCode)
          ? (payload.partCode ?? "").Trim()
          : groupedSprite.sourcePartCode.Trim();
        if (string.IsNullOrWhiteSpace(partCode) && !TryExtractPartCode(groupedSprite.name, out partCode)) continue;

        var pendingReplacement = new PendingGroupedSpriteReplacement {
          replacementSprite = replacementSprite,
          atlasAssetPath = atlasAssetPath,
          groupedSpriteName = groupedSprite.name,
          sourceAtlasAssetPath = NormalizePath(groupedSprite.sourceAtlasAssetPath),
          sourceSpriteName = ResolveGroupedSpriteSourceSortName(groupedSprite),
          sourceCategory = sourceCategory,
          form = payload.form,
          variant = payload.variant
        };

        if (TryBuildLibraryEntrySequenceKey(
              payload,
              groupedSprite,
              isNormalAtlas,
              isSkinLibrary,
              sourceCategory,
              partCode,
              out var sequenceKey)) {
          if (!sequencedPendingReplacementsByKey.TryGetValue(sequenceKey, out var groupedPendingReplacements) || groupedPendingReplacements == null) {
            groupedPendingReplacements = new List<PendingGroupedSpriteReplacement>();
            sequencedPendingReplacementsByKey[sequenceKey] = groupedPendingReplacements;
          }

          groupedPendingReplacements.Add(pendingReplacement);
          continue;
        }

        var directLabel = BuildLibraryEntryLabel(payload, groupedSprite);
        if (string.IsNullOrWhiteSpace(directLabel)) continue;

        var directKey = new LibraryEntryKey(isNormalAtlas, isSkinLibrary, sourceCategory, partCode, directLabel);
        if (!directPendingReplacementsByKey.TryGetValue(directKey, out var directPendingReplacements) || directPendingReplacements == null) {
          directPendingReplacements = new List<PendingGroupedSpriteReplacement>();
          directPendingReplacementsByKey[directKey] = directPendingReplacements;
        }

        directPendingReplacements.Add(pendingReplacement);
      }
    }

    foreach (var pendingPair in sequencedPendingReplacementsByKey) {
      var sequenceKey = pendingPair.Key;
      var pendingGroup = pendingPair.Value;
      if (pendingGroup == null || pendingGroup.Count <= 0) continue;
      if (pendingGroup.Count > 1) {
        pendingGroup.Sort(ComparePendingGroupedSpriteReplacements);
      }

      var expandedPendingGroup = ExpandPendingGroupedSpriteSequenceBySourceSlices(
        pendingGroup,
        replacementIndex);
      for (var replacementIndexPosition = 0; replacementIndexPosition < expandedPendingGroup.Count; replacementIndexPosition++) {
        var pendingReplacement = expandedPendingGroup[replacementIndexPosition];
        if (pendingReplacement == null) continue;

        var label = BuildSequencedLibraryEntryLabel(sequenceKey, replacementIndexPosition + 1);
        if (string.IsNullOrWhiteSpace(label)) continue;

        var libraryKey = new LibraryEntryKey(
          sequenceKey.scopeKey.isNormal,
          sequenceKey.scopeKey.isSkinLibrary,
          sequenceKey.scopeKey.category,
          sequenceKey.scopeKey.partCode,
          label);

        TryAddGroupedSpriteReplacement(
          replacementIndex,
          libraryKey,
          pendingReplacement.replacementSprite,
          pendingReplacement.atlasAssetPath,
          pendingReplacement.groupedSpriteName,
          pendingReplacement.sourceSpriteName,
          pendingReplacement.sourceCategory,
          pendingReplacement.form,
          pendingReplacement.variant);

        RegisterOwnedRebindLabel(replacementIndex, libraryKey.scopeKey, label);
      }
    }

    foreach (var pendingPair in directPendingReplacementsByKey) {
      var directKey = pendingPair.Key;
      var pendingGroup = pendingPair.Value;
      if (pendingGroup == null || pendingGroup.Count <= 0) continue;
      if (pendingGroup.Count > 1) {
        pendingGroup.Sort(ComparePendingGroupedSpriteReplacements);
      }

      for (var replacementIndexPosition = 0; replacementIndexPosition < pendingGroup.Count; replacementIndexPosition++) {
        var pendingReplacement = pendingGroup[replacementIndexPosition];
        if (pendingReplacement == null) continue;

        TryAddGroupedSpriteReplacement(
          replacementIndex,
          directKey,
          pendingReplacement.replacementSprite,
          pendingReplacement.atlasAssetPath,
          pendingReplacement.groupedSpriteName,
          pendingReplacement.sourceSpriteName,
          pendingReplacement.sourceCategory,
          pendingReplacement.form,
          pendingReplacement.variant);

        RegisterOwnedRebindLabel(replacementIndex, directKey.scopeKey, directKey.label);
        break;
      }
    }

    replacementIndex.cleanupPlans = cleanupPlansByKey.Values
      .OrderBy(plan => plan.folderPath, StringComparer.OrdinalIgnoreCase)
      .ThenBy(plan => plan.filePrefix, StringComparer.OrdinalIgnoreCase)
      .ToList();

    if (replacementIndex.metadataFileCount <= 0) {
      error = "No grouped atlas metadata was found under '" + groupedOutputRoot + "'.";
      return false;
    }

    if (replacementIndex.spritesByKey.Count <= 0) {
      error = "Grouped atlas metadata was found, but no replacement sprites could be indexed for rebinding.";
      return false;
    }

    return true;
  }

  static void TryAddGroupedSpriteReplacement(
    GroupedSpriteReplacementIndex replacementIndex,
    LibraryEntryKey key,
    SpriteAssetReference replacementSprite,
    string atlasAssetPath,
    string groupedSpriteName,
    string sourceSpriteName,
    string sourceCategory,
    string form,
    string variant) {
    if (replacementIndex == null || !replacementSprite.IsValid) return;

    if (replacementIndex.spritesByKey.TryGetValue(key, out var existing) && existing.IsValid && !existing.Equals(replacementSprite)) {
      replacementIndex.duplicateKeyCount++;
      if (replacementIndex.duplicateKeySamples.Count < DuplicateRebindWarningSampleLimit) {
        replacementIndex.duplicateKeySamples.Add(
          "category='" + key.scopeKey.category + "'" +
          " part='" + key.scopeKey.partCode + "'" +
          " label='" + key.label + "'" +
          " normal=" + key.scopeKey.isNormal +
          " skin=" + key.scopeKey.isSkinLibrary +
          " source_category='" + (sourceCategory ?? "") + "'" +
          " form='" + (form ?? "") + "'" +
          " variant='" + (variant ?? "") + "'" +
          " source_sprite='" + (sourceSpriteName ?? "") + "'" +
          " existing='" + existing.assetPath + "[" + existing.spriteName + "]'" +
          " incoming='" + atlasAssetPath + "[" + groupedSpriteName + "]'");
      }
      return;
    }

    replacementIndex.spritesByKey[key] = replacementSprite;
    if (!replacementIndex.labelsByScope.TryGetValue(key.scopeKey, out var replacementsByLabel) || replacementsByLabel == null) {
      replacementsByLabel = new Dictionary<string, SpriteAssetReference>(StringComparer.OrdinalIgnoreCase);
      replacementIndex.labelsByScope[key.scopeKey] = replacementsByLabel;
    }

    replacementsByLabel[key.label] = replacementSprite;
    replacementIndex.indexedSpriteCount = replacementIndex.spritesByKey.Count;
  }

  static bool TryBuildLibraryEntrySequenceKey(
    GroupedAtlasMetadataPayload payload,
    GroupedAtlasSpriteMetadata sprite,
    bool isNormalAtlas,
    bool isSkinLibrary,
    string sourceCategory,
    string partCode,
    out LibraryEntrySequenceKey sequenceKey) {
    sequenceKey = default;

    if (isSkinLibrary) {
      sequenceKey = new LibraryEntrySequenceKey(isNormalAtlas, true, sourceCategory, partCode, "");
      return true;
    }

    var labelPrefix = BuildGearLabelPrefix(payload?.form, payload?.variant);
    if (string.IsNullOrWhiteSpace(labelPrefix)) {
      var fallbackLabel = BuildLibraryEntryLabel(payload, sprite);
      if (!TryExtractRebindLabelPrefix(fallbackLabel, out labelPrefix)) {
        return false;
      }
    }

    sequenceKey = new LibraryEntrySequenceKey(isNormalAtlas, false, sourceCategory, partCode, labelPrefix);
    return true;
  }

  static string BuildSequencedLibraryEntryLabel(LibraryEntrySequenceKey sequenceKey, int labelIndex) {
    if (labelIndex <= 0) return "";

    var indexText = labelIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
    if (sequenceKey.scopeKey.isSkinLibrary || string.IsNullOrWhiteSpace(sequenceKey.labelPrefix)) {
      return indexText;
    }

    return sequenceKey.labelPrefix + "_" + indexText;
  }

  static void RegisterOwnedRebindLabel(GroupedSpriteReplacementIndex replacementIndex, LibraryEntryScopeKey scopeKey, string label) {
    if (replacementIndex == null || string.IsNullOrWhiteSpace(label)) return;

    if (!replacementIndex.cleanupByScope.TryGetValue(scopeKey, out var cleanupPlan) || cleanupPlan == null) {
      cleanupPlan = new RebindLabelCleanupPlan();
      replacementIndex.cleanupByScope[scopeKey] = cleanupPlan;
    }

    cleanupPlan.expectedLabels.Add(label);
    if (scopeKey.isSkinLibrary) {
      cleanupPlan.deleteNumericLabels = true;
      return;
    }

    if (TryExtractRebindLabelPrefix(label, out var labelPrefix)) {
      cleanupPlan.ownedLabelPrefixes.Add(labelPrefix);
    }
  }

  static bool ShouldDeleteMissingRebindLabel(RebindLabelCleanupPlan cleanupPlan, string entryName) {
    if (cleanupPlan == null || string.IsNullOrWhiteSpace(entryName)) return false;
    if (cleanupPlan.expectedLabels.Contains(entryName)) return false;

    if (cleanupPlan.deleteNumericLabels &&
        int.TryParse(entryName.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _)) {
      return true;
    }

    return TryExtractRebindLabelPrefix(entryName, out var labelPrefix) &&
           cleanupPlan.ownedLabelPrefixes.Contains(labelPrefix);
  }

  static bool TryExtractRebindLabelPrefix(string label, out string labelPrefix) {
    labelPrefix = "";
    if (string.IsNullOrWhiteSpace(label)) return false;

    var normalizedLabel = label.Trim();
    var separatorIndex = normalizedLabel.LastIndexOf('_');
    if (separatorIndex <= 0 || separatorIndex >= normalizedLabel.Length - 1) return false;
    if (!int.TryParse(normalizedLabel.Substring(separatorIndex + 1), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _)) {
      return false;
    }

    labelPrefix = normalizedLabel.Substring(0, separatorIndex).Trim();
    return !string.IsNullOrWhiteSpace(labelPrefix);
  }

  void RebindSpriteLibraries(
    string libraryFolderPath,
    GroupedSpriteReplacementIndex replacementIndex,
    out int touchedLibraries,
    out int reboundEntries,
    out int deletedLabels,
    out int createdCategories,
    out int createdLabels,
    out int unchangedEntries,
    out HashSet<bool> processedLibraryKinds) {
    touchedLibraries = 0;
    reboundEntries = 0;
    deletedLabels = 0;
    createdCategories = 0;
    createdLabels = 0;
    unchangedEntries = 0;
    processedLibraryKinds = new HashSet<bool>();
    if (replacementIndex == null || replacementIndex.labelsByScope.Count <= 0) return;

    var libraryFullPath = Path.GetFullPath(libraryFolderPath);
    if (!Directory.Exists(libraryFullPath)) return;

    var libraryPaths = Directory.GetFiles(libraryFullPath, "*.spriteLib", SearchOption.AllDirectories);
    Array.Sort(libraryPaths, StringComparer.OrdinalIgnoreCase);
    var parsedLibraryCount = 0;
    var loadFailedCount = 0;
    var missingLibraryPropertyCount = 0;
    var matchedCategoryCount = 0;
    var skippedLibraryLogCount = 0;
    var failedLibraryCount = 0;

    for (var libraryIndex = 0; libraryIndex < libraryPaths.Length; libraryIndex++) {
      var libraryFullAssetPath = libraryPaths[libraryIndex];
      if (!TryConvertFullPathToAssetPath(libraryFullAssetPath, out var libraryPath)) continue;

      libraryPath = NormalizePath(libraryPath);
      if (!TryParseSpriteLibraryDescriptor(libraryPath, out var partCode, out var isNormalLibrary, out var isSkinLibrary)) continue;
      if (isNormalLibrary) continue;
      parsedLibraryCount++;
      processedLibraryKinds.Add(isSkinLibrary);

      if (!TryRebindSpriteLibraryText(
            libraryPath,
            partCode,
            isNormalLibrary,
            isSkinLibrary,
            replacementIndex,
            out var libraryChanged,
            out var libraryReboundEntries,
            out var libraryDeletedLabels,
            out var libraryCreatedCategories,
            out var libraryCreatedLabels,
            out var libraryUnchangedEntries,
            out var libraryMatchedCategoryCount,
            out var rebindError)) {
        loadFailedCount++;
        failedLibraryCount++;
        if (skippedLibraryLogCount < 8) {
          AtlasAuthoringLog.Warning(
            "[GearGroupAtlas] Rebind skipped sprite library because it could not be rewritten." +
            " path='" + libraryPath + "'" +
            " error='" + rebindError + "'");
          skippedLibraryLogCount++;
        }
        continue;
      }

      if (libraryMatchedCategoryCount <= 0 && skippedLibraryLogCount < 8) {
        AtlasAuthoringLog.Verbose(
          "[GearGroupAtlas] Rebind found no matching categories for sprite library." +
          " path='" + libraryPath + "'" +
          " part='" + partCode + "'" +
          " normal=" + isNormalLibrary +
          " skin=" + isSkinLibrary);
        skippedLibraryLogCount++;
      }

      matchedCategoryCount += libraryMatchedCategoryCount;
      unchangedEntries += libraryUnchangedEntries;

      if (libraryMatchedCategoryCount > 0 || libraryChanged) {
        AtlasAuthoringLog.Verbose(
          "[GearGroupAtlas] Rebind processed sprite library." +
          " index=" + (libraryIndex + 1) + "/" + libraryPaths.Length +
          " path='" + libraryPath + "'" +
          " matched_categories=" + libraryMatchedCategoryCount +
          " rebound_entries=" + libraryReboundEntries +
          " unchanged_entries=" + libraryUnchangedEntries +
          " deleted_labels=" + libraryDeletedLabels +
          " created_categories=" + libraryCreatedCategories +
          " created_labels=" + libraryCreatedLabels +
          " changed=" + libraryChanged);
      }

      if (!libraryChanged) continue;

      touchedLibraries++;
      reboundEntries += libraryReboundEntries;
      deletedLabels += libraryDeletedLabels;
      createdCategories += libraryCreatedCategories;
      createdLabels += libraryCreatedLabels;
    }

    if (touchedLibraries <= 0) {
      AtlasAuthoringLog.Warning(
        "[GearGroupAtlas] Rebind updated no sprite libraries." +
        " library_files=" + libraryPaths.Length +
        " parsed_libraries=" + parsedLibraryCount +
        " matched_categories=" + matchedCategoryCount +
        " unchanged_entries=" + unchangedEntries +
        " failed_libraries=" + failedLibraryCount +
        " load_failures=" + loadFailedCount +
        " missing_library_property=" + missingLibraryPropertyCount);
    }
  }

  bool TryRebindSpriteLibraryText(
    string libraryPath,
    string partCode,
    bool isNormalLibrary,
    bool isSkinLibrary,
    GroupedSpriteReplacementIndex replacementIndex,
    out bool libraryChanged,
    out int libraryReboundEntries,
    out int libraryDeletedLabels,
    out int libraryCreatedCategories,
    out int libraryCreatedLabels,
    out int libraryUnchangedEntries,
    out int libraryMatchedCategoryCount,
    out string error) {
    libraryChanged = false;
    libraryReboundEntries = 0;
    libraryDeletedLabels = 0;
    libraryCreatedCategories = 0;
    libraryCreatedLabels = 0;
    libraryUnchangedEntries = 0;
    libraryMatchedCategoryCount = 0;
    error = "";
    if (replacementIndex == null) return true;

    var libraryFullPath = Path.GetFullPath(libraryPath);
    if (!File.Exists(libraryFullPath)) {
      error = "Sprite library file does not exist on disk.";
      return false;
    }

    string originalText;
    try {
      originalText = File.ReadAllText(libraryFullPath);
    }
    catch (Exception ex) {
      error = ex.Message;
      return false;
    }

    var lineEnding = originalText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    var deleteGearSkinLabels =
      !isSkinLibrary &&
      originalText.IndexOf("Skin", StringComparison.OrdinalIgnoreCase) >= 0;
    var categoryPlansByName = BuildSpriteLibraryCategoryPlans(
      replacementIndex,
      isNormalLibrary,
      isSkinLibrary,
      partCode);
    if (categoryPlansByName.Count <= 0 && !deleteGearSkinLabels) {
      return true;
    }

    var lines = SplitTextIntoLines(originalText);
    var existingCategoryNames = CollectSpriteLibraryCategoryNames(lines);

    var rewritten = new System.Text.StringBuilder(originalText.Length + 256);
    var insideLibrary = false;
    var insideOverrideEntries = false;
    var currentCategoryAllowsEntryRewrite = false;
    var currentCategorySawOverrideEntries = false;
    var appendedMissingCategories = false;
    var pendingCategoryHashRewrite = false;
    var pendingCategoryHashName = "";
    SpriteLibraryCategoryPlan currentCategoryPlan = null;
    RebindLabelCleanupPlan currentCategoryCleanupPlan = null;
    HashSet<string> currentCategoryRetainedLabels = null;
    var seenCategoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++) {
      var line = lines[lineIndex];
      if (!insideLibrary) {
        AppendLine(rewritten, line, lineEnding);
        if (string.Equals(line, "  m_Library:", StringComparison.Ordinal)) {
          insideLibrary = true;
        }
        continue;
      }

      if (IsSpriteLibraryListBoundary(line)) {
        FinalizeSpriteLibraryCategoryRewrite(
          currentCategoryPlan,
          currentCategoryRetainedLabels,
          currentCategorySawOverrideEntries,
          rewritten,
          lineEnding,
          ref libraryChanged,
          ref libraryCreatedLabels);
        currentCategoryPlan = null;
        currentCategoryCleanupPlan = null;
        currentCategoryRetainedLabels = null;
        currentCategoryAllowsEntryRewrite = false;
        currentCategorySawOverrideEntries = false;
        pendingCategoryHashRewrite = false;
        pendingCategoryHashName = "";
        insideOverrideEntries = false;

        if (!appendedMissingCategories) {
          AppendMissingSpriteLibraryCategories(
            categoryPlansByName,
            seenCategoryNames,
            rewritten,
            lineEnding,
            ref libraryChanged,
            ref libraryMatchedCategoryCount,
            ref libraryCreatedCategories,
            ref libraryCreatedLabels);
          appendedMissingCategories = true;
        }

        insideLibrary = false;
        AppendLine(rewritten, line, lineEnding);
        continue;
      }

      if (line.StartsWith("  - m_Name: ", StringComparison.Ordinal)) {
        var categoryName = line.Substring("  - m_Name: ".Length).Trim();
        var resolvedCategoryName = categoryName;
        var resolvedCategoryPlan = (SpriteLibraryCategoryPlan)null;
        FinalizeSpriteLibraryCategoryRewrite(
          currentCategoryPlan,
          currentCategoryRetainedLabels,
          currentCategorySawOverrideEntries,
          rewritten,
          lineEnding,
          ref libraryChanged,
          ref libraryCreatedLabels);
        TryResolveSpriteLibraryCategoryPlan(
          categoryPlansByName,
          categoryName,
          out resolvedCategoryName,
          out resolvedCategoryPlan);
        var useCleanupOnlyRewrite =
          resolvedCategoryPlan != null &&
          !string.Equals(categoryName, resolvedCategoryName, StringComparison.OrdinalIgnoreCase) &&
          ContainsEquivalentSpriteLibraryCategory(existingCategoryNames, resolvedCategoryName);
        seenCategoryNames.Add(resolvedCategoryName);
        currentCategoryPlan = useCleanupOnlyRewrite ? null : resolvedCategoryPlan;
        currentCategoryCleanupPlan = resolvedCategoryPlan?.cleanupPlan;
        currentCategoryRetainedLabels = currentCategoryPlan != null || currentCategoryCleanupPlan != null
          ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
          : null;
        currentCategoryAllowsEntryRewrite =
          currentCategoryPlan != null ||
          currentCategoryCleanupPlan != null ||
          deleteGearSkinLabels;
        currentCategorySawOverrideEntries = false;
        pendingCategoryHashRewrite =
          !useCleanupOnlyRewrite &&
          !string.Equals(categoryName, resolvedCategoryName, StringComparison.Ordinal) &&
          !string.IsNullOrWhiteSpace(resolvedCategoryName);
        pendingCategoryHashName = pendingCategoryHashRewrite ? resolvedCategoryName : "";
        insideOverrideEntries = false;
        if (resolvedCategoryPlan != null) {
          libraryMatchedCategoryCount++;
        }

        if (pendingCategoryHashRewrite) {
          line = "  - m_Name: " + resolvedCategoryName;
          libraryChanged = true;
        }

        AppendLine(rewritten, line, lineEnding);
        continue;
      }

      if (pendingCategoryHashRewrite && line.StartsWith("    m_Hash: ", StringComparison.Ordinal)) {
        var rewrittenHashLine =
          "    m_Hash: " + GetSpriteLibraryStringHash(pendingCategoryHashName).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!string.Equals(line, rewrittenHashLine, StringComparison.Ordinal)) {
          line = rewrittenHashLine;
          libraryChanged = true;
        }

        pendingCategoryHashRewrite = false;
        pendingCategoryHashName = "";
        AppendLine(rewritten, line, lineEnding);
        continue;
      }

      if (currentCategoryAllowsEntryRewrite && string.Equals(line, "    m_OverrideEntries:", StringComparison.Ordinal)) {
        insideOverrideEntries = true;
        currentCategorySawOverrideEntries = true;
        AppendLine(rewritten, line, lineEnding);
        continue;
      }

      if (currentCategoryAllowsEntryRewrite && insideOverrideEntries && line.StartsWith("    - m_Name: ", StringComparison.Ordinal)) {
        var entryBlockEnd = FindSpriteLibraryEntryBlockEnd(lines, lineIndex + 1);
        RewriteSpriteLibraryEntryBlock(
          lines,
          lineIndex,
          entryBlockEnd,
          currentCategoryPlan?.replacementsByLabel,
          currentCategoryCleanupPlan,
          deleteGearSkinLabels,
          currentCategoryRetainedLabels,
          rewritten,
          lineEnding,
          ref libraryChanged,
          ref libraryReboundEntries,
          ref libraryDeletedLabels,
          ref libraryUnchangedEntries);
        lineIndex = entryBlockEnd - 1;
        continue;
      }

      AppendLine(rewritten, line, lineEnding);
    }

    if (insideLibrary) {
      FinalizeSpriteLibraryCategoryRewrite(
        currentCategoryPlan,
        currentCategoryRetainedLabels,
        currentCategorySawOverrideEntries,
        rewritten,
        lineEnding,
        ref libraryChanged,
        ref libraryCreatedLabels);
      if (!appendedMissingCategories) {
        AppendMissingSpriteLibraryCategories(
          categoryPlansByName,
          seenCategoryNames,
          rewritten,
          lineEnding,
          ref libraryChanged,
          ref libraryMatchedCategoryCount,
          ref libraryCreatedCategories,
          ref libraryCreatedLabels);
      }
    }

    if (!libraryChanged) return true;

    try {
      File.WriteAllText(libraryFullPath, rewritten.ToString());
      return true;
    }
    catch (Exception ex) {
      error = ex.Message;
      return false;
    }
  }

  static void RewriteSpriteLibraryEntryBlock(
    string[] lines,
    int startIndex,
    int endIndex,
    Dictionary<string, SpriteAssetReference> replacementsByLabel,
    RebindLabelCleanupPlan cleanupPlan,
    bool deleteGearSkinLabels,
    HashSet<string> retainedLabels,
    System.Text.StringBuilder output,
    string lineEnding,
    ref bool libraryChanged,
    ref int libraryReboundEntries,
    ref int libraryDeletedLabels,
    ref int libraryUnchangedEntries) {
    if (lines == null || output == null || startIndex < 0 || endIndex <= startIndex) return;

    var entryLine = lines[startIndex];
    var entryName = entryLine.StartsWith("    - m_Name: ", StringComparison.Ordinal)
      ? entryLine.Substring("    - m_Name: ".Length).Trim()
      : "";

    if (ShouldDeleteGearSkinLabel(deleteGearSkinLabels, entryName)) {
      libraryChanged = true;
      libraryDeletedLabels++;
      return;
    }

    if (!TryResolveLabelReplacement(replacementsByLabel, entryName, out var replacementSprite) || !replacementSprite.IsValid) {
      if (!ShouldDeleteMissingRebindLabel(cleanupPlan, entryName)) {
        retainedLabels?.Add(entryName);
        AppendLineRange(output, lines, startIndex, endIndex, lineEnding);
        return;
      }

      libraryChanged = true;
      libraryDeletedLabels++;
      return;
    }

    var entryChanged = false;
    var sawSprite = false;
    var sawSpriteOverride = false;
    for (var lineIndex = startIndex; lineIndex < endIndex; lineIndex++) {
      var line = lines[lineIndex];
      if (line.StartsWith("      m_Sprite: ", StringComparison.Ordinal)) {
        var rewrittenLine = BuildSpriteLibrarySpriteReferenceLine("m_Sprite", replacementSprite);
        if (!string.Equals(line, rewrittenLine, StringComparison.Ordinal)) {
          entryChanged = true;
          line = rewrittenLine;
        }

        sawSprite = true;
      }
      else if (line.StartsWith("      m_SpriteOverride: ", StringComparison.Ordinal)) {
        var rewrittenLine = BuildSpriteLibrarySpriteReferenceLine("m_SpriteOverride", replacementSprite);
        if (!string.Equals(line, rewrittenLine, StringComparison.Ordinal)) {
          entryChanged = true;
          line = rewrittenLine;
        }

        sawSpriteOverride = true;
      }

      AppendLine(output, line, lineEnding);
    }

    retainedLabels?.Add(entryName);
    if (entryChanged || !sawSprite || !sawSpriteOverride) {
      libraryChanged = true;
      libraryReboundEntries++;
      return;
    }

    libraryUnchangedEntries++;
  }

  static int FindSpriteLibraryEntryBlockEnd(string[] lines, int startIndex) {
    if (lines == null) return startIndex;

    for (var lineIndex = startIndex; lineIndex < lines.Length; lineIndex++) {
      var line = lines[lineIndex];
      if (line.StartsWith("    - m_Name: ", StringComparison.Ordinal) ||
          line.StartsWith("  - m_Name: ", StringComparison.Ordinal)) {
        return lineIndex;
      }
    }

    return lines.Length;
  }

  static void AppendLineRange(System.Text.StringBuilder output, string[] lines, int startIndex, int endIndex, string lineEnding) {
    if (output == null || lines == null) return;
    for (var lineIndex = startIndex; lineIndex < endIndex && lineIndex < lines.Length; lineIndex++) {
      AppendLine(output, lines[lineIndex], lineEnding);
    }
  }

  static void AppendLine(System.Text.StringBuilder output, string line, string lineEnding) {
    if (output == null) return;
    output.Append(line ?? "");
    output.Append(lineEnding);
  }

  static string[] SplitTextIntoLines(string text) {
    if (string.IsNullOrEmpty(text)) return Array.Empty<string>();

    var normalizedText = text
      .Replace("\r\n", "\n")
      .Replace('\r', '\n');
    if (normalizedText.EndsWith("\n", StringComparison.Ordinal)) {
      normalizedText = normalizedText.Substring(0, normalizedText.Length - 1);
    }

    return normalizedText.Length > 0
      ? normalizedText.Split('\n')
      : Array.Empty<string>();
  }

  static Dictionary<string, SpriteLibraryCategoryPlan> BuildSpriteLibraryCategoryPlans(
    GroupedSpriteReplacementIndex replacementIndex,
    bool isNormalLibrary,
    bool isSkinLibrary,
    string partCode) {
    var plansByName = new Dictionary<string, SpriteLibraryCategoryPlan>(StringComparer.OrdinalIgnoreCase);
    if (replacementIndex == null || replacementIndex.labelsByScope.Count <= 0 || string.IsNullOrWhiteSpace(partCode)) {
      return plansByName;
    }

    foreach (var pair in replacementIndex.labelsByScope) {
      var scopeKey = pair.Key;
      if (scopeKey.isNormal != isNormalLibrary ||
          scopeKey.isSkinLibrary != isSkinLibrary ||
          !string.Equals(scopeKey.partCode, partCode, StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      var replacementsByLabel = pair.Value;
      if (replacementsByLabel == null || replacementsByLabel.Count <= 0) continue;

      var categoryPlan = new SpriteLibraryCategoryPlan {
        replacementsByLabel = replacementsByLabel
      };
      replacementIndex.cleanupByScope.TryGetValue(scopeKey, out var cleanupPlan);
      categoryPlan.cleanupPlan = cleanupPlan;
      plansByName[scopeKey.category] = categoryPlan;
    }

    return plansByName;
  }

  static HashSet<string> CollectSpriteLibraryCategoryNames(string[] lines) {
    var categoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (lines == null || lines.Length <= 0) return categoryNames;

    for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++) {
      var line = lines[lineIndex];
      if (!line.StartsWith("  - m_Name: ", StringComparison.Ordinal)) continue;
      var categoryName = line.Substring("  - m_Name: ".Length).Trim();
      if (string.IsNullOrWhiteSpace(categoryName)) continue;
      categoryNames.Add(categoryName);
    }

    return categoryNames;
  }

  static bool ContainsEquivalentSpriteLibraryCategory(HashSet<string> existingCategoryNames, string categoryName) {
    if (existingCategoryNames == null || existingCategoryNames.Count <= 0 || string.IsNullOrWhiteSpace(categoryName)) {
      return false;
    }

    foreach (var existingCategoryName in existingCategoryNames) {
      if (string.Equals(existingCategoryName, categoryName, StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
  }

  static bool TryResolveSpriteLibraryCategoryPlan(
    Dictionary<string, SpriteLibraryCategoryPlan> categoryPlansByName,
    string categoryName,
    out string resolvedCategoryName,
    out SpriteLibraryCategoryPlan plan) {
    resolvedCategoryName = categoryName ?? "";
    plan = null;
    if (categoryPlansByName == null || categoryPlansByName.Count <= 0 || string.IsNullOrWhiteSpace(categoryName)) {
      return false;
    }

    if (categoryPlansByName.TryGetValue(categoryName, out plan) && plan != null) {
      resolvedCategoryName = categoryName;
      return true;
    }

    var normalizedCategoryName = NormalizeSpriteLibraryCategoryName(categoryName);
    if (string.IsNullOrWhiteSpace(normalizedCategoryName)) return false;

    foreach (var pair in categoryPlansByName) {
      if (pair.Value == null) continue;
      if (!string.Equals(
            NormalizeSpriteLibraryCategoryName(pair.Key),
            normalizedCategoryName,
            StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      resolvedCategoryName = pair.Key;
      plan = pair.Value;
      return true;
    }

    return false;
  }

  static string NormalizeSpriteLibraryCategoryName(string categoryName) {
    if (string.IsNullOrWhiteSpace(categoryName)) return "";

    var normalizedCategoryName = categoryName.Trim();
    if (string.Equals(normalizedCategoryName, "SuperBlast", StringComparison.OrdinalIgnoreCase)) {
      return "Blast";
    }

    return normalizedCategoryName;
  }

  static void FinalizeSpriteLibraryCategoryRewrite(
    SpriteLibraryCategoryPlan categoryPlan,
    HashSet<string> retainedLabels,
    bool sawOverrideEntries,
    System.Text.StringBuilder output,
    string lineEnding,
    ref bool libraryChanged,
    ref int libraryCreatedLabels) {
    if (categoryPlan == null || output == null) return;

    var createdLabelCount = AppendMissingSpriteLibraryEntries(
      categoryPlan.replacementsByLabel,
      retainedLabels,
      output,
      lineEnding,
      !sawOverrideEntries);
    if (createdLabelCount <= 0) return;

    libraryChanged = true;
    libraryCreatedLabels += createdLabelCount;
  }

  static void AppendMissingSpriteLibraryCategories(
    Dictionary<string, SpriteLibraryCategoryPlan> categoryPlansByName,
    HashSet<string> seenCategoryNames,
    System.Text.StringBuilder output,
    string lineEnding,
    ref bool libraryChanged,
    ref int libraryMatchedCategoryCount,
    ref int libraryCreatedCategories,
    ref int libraryCreatedLabels) {
    if (categoryPlansByName == null || categoryPlansByName.Count <= 0 || output == null) return;

    var missingCategoryNames = new List<string>();
    foreach (var pair in categoryPlansByName) {
      if (pair.Value?.replacementsByLabel == null || pair.Value.replacementsByLabel.Count <= 0) continue;
      if (seenCategoryNames != null && seenCategoryNames.Contains(pair.Key)) continue;
      missingCategoryNames.Add(pair.Key);
    }

    missingCategoryNames.Sort(CompareSpriteLibraryNames);
    for (var categoryIndex = 0; categoryIndex < missingCategoryNames.Count; categoryIndex++) {
      var categoryName = missingCategoryNames[categoryIndex];
      if (!categoryPlansByName.TryGetValue(categoryName, out var categoryPlan) ||
          categoryPlan?.replacementsByLabel == null ||
          categoryPlan.replacementsByLabel.Count <= 0) {
        continue;
      }

      AppendSpriteLibraryCategoryBlock(
        output,
        categoryName,
        categoryPlan.replacementsByLabel,
        lineEnding,
        ref libraryCreatedLabels);
      libraryChanged = true;
      libraryMatchedCategoryCount++;
      libraryCreatedCategories++;
    }
  }

  static void AppendSpriteLibraryCategoryBlock(
    System.Text.StringBuilder output,
    string categoryName,
    Dictionary<string, SpriteAssetReference> replacementsByLabel,
    string lineEnding,
    ref int createdLabels) {
    if (output == null ||
        string.IsNullOrWhiteSpace(categoryName) ||
        replacementsByLabel == null ||
        replacementsByLabel.Count <= 0) {
      return;
    }

    AppendLine(output, "  - m_Name: " + categoryName, lineEnding);
    AppendLine(
      output,
      "    m_Hash: " + GetSpriteLibraryStringHash(categoryName).ToString(System.Globalization.CultureInfo.InvariantCulture),
      lineEnding);
    AppendLine(output, "    m_CategoryList: []", lineEnding);
    AppendLine(output, "    m_OverrideEntries:", lineEnding);
    createdLabels += AppendMissingSpriteLibraryEntries(
      replacementsByLabel,
      null,
      output,
      lineEnding,
      false);
  }

  static int AppendMissingSpriteLibraryEntries(
    Dictionary<string, SpriteAssetReference> replacementsByLabel,
    HashSet<string> existingLabels,
    System.Text.StringBuilder output,
    string lineEnding,
    bool includeOverrideEntriesHeader) {
    if (replacementsByLabel == null || replacementsByLabel.Count <= 0 || output == null) return 0;

    var missingLabels = CollectMissingSpriteLibraryLabels(replacementsByLabel, existingLabels);
    if (missingLabels.Count <= 0) return 0;

    if (includeOverrideEntriesHeader) {
      AppendLine(output, "    m_OverrideEntries:", lineEnding);
    }

    for (var labelIndex = 0; labelIndex < missingLabels.Count; labelIndex++) {
      var label = missingLabels[labelIndex];
      if (!replacementsByLabel.TryGetValue(label, out var replacementSprite) || !replacementSprite.IsValid) continue;
      AppendSpriteLibraryEntryBlock(output, label, replacementSprite, lineEnding);
    }

    return missingLabels.Count;
  }

  static List<string> CollectMissingSpriteLibraryLabels(
    Dictionary<string, SpriteAssetReference> replacementsByLabel,
    HashSet<string> existingLabels) {
    var missingLabels = new List<string>();
    if (replacementsByLabel == null || replacementsByLabel.Count <= 0) return missingLabels;

    foreach (var pair in replacementsByLabel) {
      if (!pair.Value.IsValid) continue;
      if (ContainsEquivalentSpriteLibraryLabel(existingLabels, pair.Key)) continue;
      missingLabels.Add(pair.Key);
    }

    missingLabels.Sort(CompareSpriteLibraryNames);
    return missingLabels;
  }

  static bool ContainsEquivalentSpriteLibraryLabel(HashSet<string> existingLabels, string label) {
    if (existingLabels == null || existingLabels.Count <= 0 || string.IsNullOrWhiteSpace(label)) return false;
    if (existingLabels.Contains(label)) return true;

    foreach (var existingLabel in existingLabels) {
      if (SpriteSliceAddressUtility.HasEquivalentNumericLabel(existingLabel, label)) {
        return true;
      }
    }

    return false;
  }

  static void AppendSpriteLibraryEntryBlock(
    System.Text.StringBuilder output,
    string entryName,
    SpriteAssetReference replacementSprite,
    string lineEnding) {
    if (output == null || string.IsNullOrWhiteSpace(entryName) || !replacementSprite.IsValid) return;

    AppendLine(output, "    - m_Name: " + entryName, lineEnding);
    AppendLine(
      output,
      "      m_Hash: " + GetSpriteLibraryStringHash(entryName).ToString(System.Globalization.CultureInfo.InvariantCulture),
      lineEnding);
    AppendLine(output, BuildSpriteLibrarySpriteReferenceLine("m_Sprite", replacementSprite), lineEnding);
    AppendLine(output, "      m_FromMain: 0", lineEnding);
    AppendLine(output, BuildSpriteLibrarySpriteReferenceLine("m_SpriteOverride", replacementSprite), lineEnding);
  }

  static bool IsSpriteLibraryListBoundary(string line) {
    if (string.IsNullOrEmpty(line)) return false;

    return line.StartsWith("  ", StringComparison.Ordinal) &&
           !line.StartsWith("  - ", StringComparison.Ordinal) &&
           !line.StartsWith("    ", StringComparison.Ordinal);
  }

  static bool ShouldDeleteGearSkinLabel(bool deleteGearSkinLabels, string entryName) {
    return deleteGearSkinLabels &&
           !string.IsNullOrWhiteSpace(entryName) &&
           entryName.IndexOf("Skin", StringComparison.OrdinalIgnoreCase) >= 0;
  }

  static int CompareSpriteLibraryNames(string left, string right) {
    var normalizedLeft = left ?? "";
    var normalizedRight = right ?? "";
    var naturalCompare = SpriteSliceAddressUtility.CompareNaturally(normalizedLeft, normalizedRight);
    if (naturalCompare != 0) return naturalCompare;

    return StringComparer.OrdinalIgnoreCase.Compare(normalizedLeft, normalizedRight);
  }

  static int GetSpriteLibraryStringHash(string value) {
    const int bit30Mask = 0x3FFFFFFF;
    return Animator.StringToHash(value ?? "") & bit30Mask;
  }

  static string BuildSpriteLibrarySpriteReferenceLine(string propertyName, SpriteAssetReference spriteReference) {
    return "      " + propertyName + ": {fileID: " +
           spriteReference.localFileId.ToString(System.Globalization.CultureInfo.InvariantCulture) +
           ", guid: " + spriteReference.guid +
           ", type: 3}";
  }

  static bool TryParseSpriteLibraryDescriptor(string libraryPath, out string partCode, out bool isNormalLibrary, out bool isSkinLibrary) {
    partCode = "";
    isNormalLibrary = false;
    isSkinLibrary = false;

    var fileName = Path.GetFileNameWithoutExtension(libraryPath ?? "");
    if (string.IsNullOrWhiteSpace(fileName)) return false;

    isNormalLibrary = fileName.EndsWith("N", StringComparison.OrdinalIgnoreCase);
    var coreName = isNormalLibrary ? fileName.Substring(0, fileName.Length - 1) : fileName;
    string token;
    if (coreName.StartsWith("Skin", StringComparison.OrdinalIgnoreCase)) {
      isSkinLibrary = true;
      token = coreName.Substring("Skin".Length);
    }
    else if (coreName.StartsWith("Gear", StringComparison.OrdinalIgnoreCase)) {
      token = coreName.Substring("Gear".Length);
    }
    else {
      return false;
    }

    partCode = ResolvePartCode(token);
    return !string.IsNullOrWhiteSpace(partCode);
  }

  static bool TryResolveLabelReplacement(Dictionary<string, SpriteAssetReference> replacementsByLabel, string label, out SpriteAssetReference replacementSprite) {
    replacementSprite = default;
    var normalizedLabel = label ?? "";
    if (string.IsNullOrWhiteSpace(normalizedLabel) || replacementsByLabel == null || replacementsByLabel.Count <= 0) {
      return false;
    }

    if (replacementsByLabel.TryGetValue(normalizedLabel, out replacementSprite) && replacementSprite.IsValid) {
      return true;
    }

    foreach (var pair in replacementsByLabel) {
      if (!SpriteSliceAddressUtility.HasEquivalentNumericLabel(pair.Key, normalizedLabel)) continue;
      replacementSprite = pair.Value;
      return replacementSprite.IsValid;
    }

    return false;
  }

  static int ComparePendingGroupedSpriteReplacements(PendingGroupedSpriteReplacement left, PendingGroupedSpriteReplacement right) {
    var sourceAtlasCompare = SpriteSliceAddressUtility.CompareNaturally(left?.sourceAtlasAssetPath, right?.sourceAtlasAssetPath);
    if (sourceAtlasCompare != 0) return sourceAtlasCompare;

    var sourceSpriteCompare = SpriteSliceAddressUtility.CompareNaturally(left?.sourceSpriteName, right?.sourceSpriteName);
    if (sourceSpriteCompare != 0) return sourceSpriteCompare;

    var groupedNameCompare = SpriteSliceAddressUtility.CompareNaturally(left?.groupedSpriteName, right?.groupedSpriteName);
    if (groupedNameCompare != 0) return groupedNameCompare;

    return SpriteSliceAddressUtility.CompareNaturally(right?.atlasAssetPath, left?.atlasAssetPath);
  }

  static List<PendingGroupedSpriteReplacement> ExpandPendingGroupedSpriteSequenceBySourceSlices(
    List<PendingGroupedSpriteReplacement> pendingGroup,
    GroupedSpriteReplacementIndex replacementIndex) {
    var expandedSequence = new List<PendingGroupedSpriteReplacement>();
    if (pendingGroup == null || pendingGroup.Count <= 0) return expandedSequence;

    for (var pendingIndex = 0; pendingIndex < pendingGroup.Count; pendingIndex++) {
      var current = pendingGroup[pendingIndex];
      if (current == null) continue;

      expandedSequence.Add(current);
      if (pendingIndex >= pendingGroup.Count - 1) continue;

      var next = pendingGroup[pendingIndex + 1];
      if (!TryBuildPendingGroupedSpriteGapRange(current, next, out var gapStartInclusive, out var gapEndInclusive)) {
        continue;
      }

      for (var missingSliceNumber = gapStartInclusive; missingSliceNumber <= gapEndInclusive; missingSliceNumber++) {
        expandedSequence.Add(BuildFilledSliceGapReplacement(current, next, missingSliceNumber));
        if (replacementIndex != null) {
          replacementIndex.filledSliceGapCount++;
        }
      }
    }

    return expandedSequence;
  }

  static bool TryBuildPendingGroupedSpriteGapRange(
    PendingGroupedSpriteReplacement current,
    PendingGroupedSpriteReplacement next,
    out int gapStartInclusive,
    out int gapEndInclusive) {
    gapStartInclusive = 0;
    gapEndInclusive = -1;
    if (current == null || next == null) return false;
    if (!string.Equals(current.sourceAtlasAssetPath, next.sourceAtlasAssetPath, StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    if (!TryExtractPendingGroupedSpriteSliceNumber(current, out var currentSliceNumber) ||
        !TryExtractPendingGroupedSpriteSliceNumber(next, out var nextSliceNumber)) {
      return false;
    }

    if (nextSliceNumber <= currentSliceNumber + 1) return false;
    gapStartInclusive = currentSliceNumber + 1;
    gapEndInclusive = nextSliceNumber - 1;
    return true;
  }

  static PendingGroupedSpriteReplacement BuildFilledSliceGapReplacement(
    PendingGroupedSpriteReplacement left,
    PendingGroupedSpriteReplacement right,
    int missingSliceNumber) {
    if (left == null) return right;
    if (right == null) return left;

    if (!TryExtractPendingGroupedSpriteSliceNumber(left, out var leftSliceNumber) ||
        !TryExtractPendingGroupedSpriteSliceNumber(right, out var rightSliceNumber)) {
      return left;
    }

    var distanceToLeft = Math.Abs(missingSliceNumber - leftSliceNumber);
    var distanceToRight = Math.Abs(rightSliceNumber - missingSliceNumber);
    return distanceToLeft <= distanceToRight ? left : right;
  }

  static bool TryExtractPendingGroupedSpriteSliceNumber(PendingGroupedSpriteReplacement pendingReplacement, out int sliceNumber) {
    sliceNumber = 0;
    if (pendingReplacement == null) return false;
    if (!SpriteSliceAddressUtility.TryExtractNumericLabelValue(pendingReplacement.sourceSpriteName, out var numericLabelValue)) {
      return false;
    }

    return int.TryParse(numericLabelValue, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out sliceNumber) &&
           sliceNumber > 0;
  }

  static string ResolveGroupedSpriteSourceSortName(GroupedAtlasSpriteMetadata sprite) {
    var sourceSpriteName = sprite?.sourceSpriteName ?? "";
    if (!string.IsNullOrWhiteSpace(sourceSpriteName)) {
      return sourceSpriteName.Trim();
    }

    return TryExtractSourceSpriteName(sprite?.name, out sourceSpriteName)
      ? sourceSpriteName.Trim()
      : (sprite?.name ?? "").Trim();
  }

  static List<PackedSpriteBuildItem> BuildGroupedPackSequence(IEnumerable<PackedSpriteBuildItem> items) {
    if (items == null) return new List<PackedSpriteBuildItem>();

    var ordered = new List<PackedSpriteBuildItem>();
    foreach (var item in items) {
      if (item == null) continue;
      ordered.Add(item);
    }

    ordered.Sort(ComparePackedSpriteBuildItemsForPacking);
    return ordered;
  }

  static int ComparePackedSpriteBuildItemsForPacking(PackedSpriteBuildItem left, PackedSpriteBuildItem right) {
    if (ReferenceEquals(left, right)) return 0;
    if (left == null) return -1;
    if (right == null) return 1;

    var categoryCompare = SpriteSliceAddressUtility.NaturalStringComparer.Compare(left.sourceCategory, right.sourceCategory);
    if (categoryCompare != 0) return categoryCompare;

    var sourceSpriteCompare = SpriteSliceAddressUtility.NaturalStringComparer.Compare(left.sourceSpriteName, right.sourceSpriteName);
    if (sourceSpriteCompare != 0) return sourceSpriteCompare;

    var partCompare = SpriteSliceAddressUtility.NaturalStringComparer.Compare(left.sourcePartCode, right.sourcePartCode);
    if (partCompare != 0) return partCompare;

    var atlasCompare = SpriteSliceAddressUtility.NaturalStringComparer.Compare(left.colorSourceAtlasPath, right.colorSourceAtlasPath);
    if (atlasCompare != 0) return atlasCompare;

    return SpriteSliceAddressUtility.NaturalStringComparer.Compare(left.outputSpriteName, right.outputSpriteName);
  }

  static string BuildLibraryEntryLabel(GroupedAtlasMetadataPayload payload, GroupedAtlasSpriteMetadata sprite) {
    var sourceSpriteName = sprite?.sourceSpriteName ?? "";
    if (string.IsNullOrWhiteSpace(sourceSpriteName) && !TryExtractSourceSpriteName(sprite?.name, out sourceSpriteName)) {
      return "";
    }

    sourceSpriteName = sourceSpriteName.Trim();
    if (string.IsNullOrWhiteSpace(sourceSpriteName)) return "";

    if (IsSkinGroupKey(payload?.groupKey)) {
      if (SpriteSliceAddressUtility.TryExtractNumericLabelValue(sourceSpriteName, out var numericSkinLabel)) {
        return numericSkinLabel;
      }

      return sourceSpriteName;
    }

    var gearLabelPrefix = BuildGearLabelPrefix(payload?.form, payload?.variant);
    if (string.IsNullOrWhiteSpace(gearLabelPrefix)) {
      return sourceSpriteName;
    }

    if (sourceSpriteName.StartsWith(gearLabelPrefix + "_", StringComparison.OrdinalIgnoreCase)) {
      return sourceSpriteName;
    }

    if (SpriteSliceAddressUtility.TryExtractNumericLabelValue(sourceSpriteName, out var numericGearLabel)) {
      return gearLabelPrefix + "_" + numericGearLabel;
    }

    return gearLabelPrefix + "_" + sourceSpriteName;
  }

  static string BuildGearLabelPrefix(string form, string variant) {
    var normalizedForm = (form ?? "").Trim();
    var normalizedVariant = (variant ?? "").Trim();
    if (string.IsNullOrWhiteSpace(normalizedForm) || string.IsNullOrWhiteSpace(normalizedVariant)) {
      return "";
    }

    return normalizedForm + "_" + normalizedVariant;
  }

  static bool TryExtractPartCode(string groupedSpriteName, out string partCode) {
    partCode = "";
    if (string.IsNullOrWhiteSpace(groupedSpriteName)) return false;

    var separatorIndex = groupedSpriteName.IndexOf("__", StringComparison.Ordinal);
    if (separatorIndex <= 0) return false;
    partCode = groupedSpriteName.Substring(0, separatorIndex).Trim();
    return !string.IsNullOrWhiteSpace(partCode);
  }

  static bool TryExtractSourceSpriteName(string groupedSpriteName, out string sourceSpriteName) {
    sourceSpriteName = "";
    if (string.IsNullOrWhiteSpace(groupedSpriteName)) return false;

    var separatorIndex = groupedSpriteName.IndexOf("__", StringComparison.Ordinal);
    if (separatorIndex < 0 || separatorIndex >= groupedSpriteName.Length - 2) return false;
    sourceSpriteName = groupedSpriteName.Substring(separatorIndex + 2).Trim();
    return !string.IsNullOrWhiteSpace(sourceSpriteName);
  }

  static Dictionary<string, SpriteAssetReference> BuildSpriteReferenceLookupByName(string atlasAssetPath) {
    var result = new Dictionary<string, SpriteAssetReference>(StringComparer.Ordinal);
    var sprites = AssetDatabase.LoadAllAssetsAtPath(atlasAssetPath).OfType<Sprite>();
    foreach (var sprite in sprites) {
      if (sprite == null || string.IsNullOrWhiteSpace(sprite.name)) continue;
      if (!TryGetSpriteAssetReference(sprite, out var spriteReference)) continue;
      result[sprite.name] = spriteReference;
    }

    return result;
  }

  static bool TryGetSpriteAssetReference(Sprite sprite, out SpriteAssetReference spriteReference) {
    spriteReference = default;
    if (sprite == null) return false;
    if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out var guid, out long localFileId)) {
      return false;
    }

    spriteReference = new SpriteAssetReference(
      guid,
      localFileId,
      NormalizePath(AssetDatabase.GetAssetPath(sprite)),
      sprite.name);
    return spriteReference.IsValid;
  }

  static bool TryBuildCleanupPlanKey(string atlasAssetPath, out string folderPath, out string filePrefix, out bool useNumberedPageNames) {
    folderPath = NormalizePath(Path.GetDirectoryName(atlasAssetPath));
    filePrefix = "";
    useNumberedPageNames = false;

    var fileName = Path.GetFileNameWithoutExtension(atlasAssetPath ?? "");
    if (string.IsNullOrWhiteSpace(fileName)) return false;
    if (fileName.EndsWith("_N", StringComparison.OrdinalIgnoreCase)) {
      fileName = fileName.Substring(0, fileName.Length - 2);
    }

    var pageMarkerIndex = fileName.LastIndexOf("_p", StringComparison.OrdinalIgnoreCase);
    if (pageMarkerIndex <= 0 || pageMarkerIndex >= fileName.Length - 2) {
      useNumberedPageNames = IsNumberedPageFileBase(fileName);
      return useNumberedPageNames && !string.IsNullOrWhiteSpace(folderPath);
    }

    if (!int.TryParse(fileName.Substring(pageMarkerIndex + 2), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _)) {
      return false;
    }

    filePrefix = fileName.Substring(0, pageMarkerIndex);
    return !string.IsNullOrWhiteSpace(folderPath) && !string.IsNullOrWhiteSpace(filePrefix);
  }

  int CleanupStaleOutputs(List<CleanupPlan> cleanupPlans) {
    if (cleanupPlans == null || cleanupPlans.Count <= 0) return 0;

    var deletedAssets = 0;
    for (var i = 0; i < cleanupPlans.Count; i++) {
      var plan = cleanupPlans[i];
      if (plan == null || string.IsNullOrWhiteSpace(plan.folderPath)) continue;
      if (!plan.useNumberedPageNames && string.IsNullOrWhiteSpace(plan.filePrefix)) continue;

      var fullFolderPath = Path.GetFullPath(plan.folderPath);
      if (!Directory.Exists(fullFolderPath)) continue;

      var files = Directory.GetFiles(fullFolderPath, "*", SearchOption.TopDirectoryOnly);
      for (var fileIndex = 0; fileIndex < files.Length; fileIndex++) {
        if (!TryConvertFullPathToAssetPath(files[fileIndex], out var assetPath)) continue;
        var extension = Path.GetExtension(assetPath);
        if (!IsCleanupCandidateExtension(extension)) continue;

        var fileName = Path.GetFileNameWithoutExtension(assetPath);
        if (!IsCleanupAssetForPlan(plan, fileName)) continue;
        if (plan.keepAssetPaths.Contains(assetPath)) continue;
        if (!TrimmedAtlasExporterWindow.DeleteAssetFiles(assetPath)) continue;
        deletedAssets++;
      }
    }

    return deletedAssets;
  }

  static bool IsCleanupAssetForPlan(CleanupPlan plan, string fileName) {
    if (plan == null || string.IsNullOrWhiteSpace(fileName)) {
      return false;
    }

    if (!plan.useNumberedPageNames) {
      return fileName.StartsWith(plan.filePrefix + "_p", StringComparison.OrdinalIgnoreCase);
    }

    if (fileName.EndsWith("_N", StringComparison.OrdinalIgnoreCase)) {
      fileName = fileName.Substring(0, fileName.Length - 2);
    }

    return IsNumberedPageFileBase(fileName);
  }

  static bool IsCleanupCandidateExtension(string extension) {
    return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase);
  }

  static int DeleteSourceAssets(HashSet<string> sourceAssetPaths) {
    if (sourceAssetPaths == null || sourceAssetPaths.Count <= 0) return 0;

    var deletedAssetCount = 0;
    var orderedAssetPaths = sourceAssetPaths
      .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
      .ToList();

    for (var assetIndex = 0; assetIndex < orderedAssetPaths.Count; assetIndex++) {
      var assetPath = orderedAssetPaths[assetIndex];
      if (string.IsNullOrWhiteSpace(assetPath)) continue;
      if (!File.Exists(Path.GetFullPath(assetPath))) continue;
      if (!TrimmedAtlasExporterWindow.DeleteAssetFiles(assetPath)) {
        AtlasAuthoringLog.Warning("[GearGroupAtlas] Failed to delete exported source asset. asset='" + assetPath + "'");
        continue;
      }

      deletedAssetCount++;
    }

    return deletedAssetCount;
  }

  static int DeleteEmptySourceFolders(string sourceRootPath, HashSet<string> sourceAssetPaths) {
    var sourceFolderPaths = CollectSourceFolderPathsForCleanup(sourceRootPath, sourceAssetPaths);
    if (sourceFolderPaths.Count <= 0) return 0;

    var deletedFolderCount = 0;
    var orderedFolderPaths = sourceFolderPaths
      .OrderByDescending(path => path.Count(c => c == '/'))
      .ThenByDescending(path => path.Length)
      .ToList();

    for (var folderIndex = 0; folderIndex < orderedFolderPaths.Count; folderIndex++) {
      var folderPath = orderedFolderPaths[folderIndex];
      var fullFolderPath = Path.GetFullPath(folderPath);
      if (!Directory.Exists(fullFolderPath)) continue;
      if (!IsFolderEmptyForCleanup(folderPath)) continue;
      if (!DeleteFolderFiles(folderPath)) {
        AtlasAuthoringLog.Warning("[GearGroupAtlas] Failed to delete empty source folder. folder='" + folderPath + "'");
        continue;
      }

      deletedFolderCount++;
    }

    return deletedFolderCount;
  }

  static HashSet<string> CollectSourceFolderPathsForCleanup(string sourceRootPath, HashSet<string> sourceAssetPaths) {
    var folderPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var normalizedSourceRootPath = NormalizePath(sourceRootPath).TrimEnd('/');
    if (string.IsNullOrWhiteSpace(normalizedSourceRootPath) || sourceAssetPaths == null) return folderPaths;

    foreach (var assetPath in sourceAssetPaths) {
      var currentFolderPath = NormalizePath(Path.GetDirectoryName(assetPath));
      while (!string.IsNullOrWhiteSpace(currentFolderPath) &&
             currentFolderPath.StartsWith(normalizedSourceRootPath + "/", StringComparison.OrdinalIgnoreCase)) {
        folderPaths.Add(currentFolderPath);
        currentFolderPath = NormalizePath(Path.GetDirectoryName(currentFolderPath));
      }
    }

    return folderPaths;
  }

  static bool IsFolderEmptyForCleanup(string folderPath) {
    var fullFolderPath = Path.GetFullPath(folderPath);
    if (!Directory.Exists(fullFolderPath)) return false;

    var entries = Directory.GetFileSystemEntries(fullFolderPath, "*", SearchOption.TopDirectoryOnly);
    for (var entryIndex = 0; entryIndex < entries.Length; entryIndex++) {
      var entryName = Path.GetFileName(entries[entryIndex]);
      if (string.IsNullOrWhiteSpace(entryName)) continue;
      if (entryName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
      return false;
    }

    return true;
  }

  static bool DeleteFolderFiles(string folderPath) {
    var fullFolderPath = Path.GetFullPath(folderPath);
    if (!Directory.Exists(fullFolderPath)) {
      return false;
    }

    var metaPath = fullFolderPath + ".meta";
    Directory.Delete(fullFolderPath, false);
    if (File.Exists(metaPath)) {
      File.Delete(metaPath);
    }

    return true;
  }

  static HashSet<string> CollectExportedSourceAssetPaths(List<GroupCandidate> exportedCandidates) {
    var sourceAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (exportedCandidates == null) return sourceAssetPaths;

    for (var candidateIndex = 0; candidateIndex < exportedCandidates.Count; candidateIndex++) {
      var candidate = exportedCandidates[candidateIndex];
      if (candidate?.sourceAtlases == null) continue;

      foreach (var record in candidate.sourceAtlases) {
        if (record == null) continue;

        AddCleanupAssetPath(sourceAssetPaths, record.atlasPath);
        AddCleanupMetadataAssetPath(sourceAssetPaths, record.atlasPath);
        AddCleanupAssetPath(sourceAssetPaths, record.normalAtlasPath);
        AddCleanupMetadataAssetPath(sourceAssetPaths, record.normalAtlasPath);
      }
    }

    return sourceAssetPaths;
  }

  static void AddCleanupMetadataAssetPath(HashSet<string> sourceAssetPaths, string assetPath) {
    var normalizedAssetPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath)) return;
    AddMetadataAssetPaths(sourceAssetPaths, normalizedAssetPath);
  }

  static void AddCleanupAssetPath(HashSet<string> sourceAssetPaths, string assetPath) {
    var normalizedAssetPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath)) return;
    sourceAssetPaths.Add(normalizedAssetPath);
  }

  ExportCleanupSummary CleanupExportedSourceAssets(string sourceRootPath, List<GroupCandidate> exportedCandidates) {
    var summary = new ExportCleanupSummary();
    if (exportedCandidates == null || exportedCandidates.Count <= 0) return summary;

    var sourceAssetPaths = CollectExportedSourceAssetPaths(exportedCandidates);
    if (sourceAssetPaths.Count <= 0) return summary;

    summary.deletedAssetCount = DeleteSourceAssets(sourceAssetPaths);
    summary.deletedFolderCount = DeleteEmptySourceFolders(sourceRootPath, sourceAssetPaths);
    AtlasAuthoringLog.Verbose(
      "[GearGroupAtlas] Cleaned exported source assets." +
      " source_root='" + sourceRootPath + "'" +
      " scheduled_assets=" + sourceAssetPaths.Count +
      " deleted_assets=" + summary.deletedAssetCount +
      " deleted_folders=" + summary.deletedFolderCount);
    return summary;
  }

  static void AddFailureLog(List<string> failureLogs, string context, string error) {
    if (failureLogs == null || failureLogs.Count >= 30) return;
    failureLogs.Add((context ?? "<unknown>") + " :: " + (string.IsNullOrWhiteSpace(error) ? "Unknown export failure." : error));
  }
}
#endif
