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
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Profiling;


public static partial class SpriteIndexBuilder {
  static bool RebuildRuntimeIndexInternal(bool logResult, bool failOnError, string contextLabel, bool prepareSelectedPacks) {
    var addressableSettings = AddressableAssetSettingsDefaultObject.GetSettings(true);
    if (addressableSettings == null) {
      Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] Addressables settings were not found.");
      if (failOnError) throw new BuildFailedException("Addressables settings were not found.");
      return false;
    }

    if (prepareSelectedPacks) {
      if (!ContentPackPipeline.PrepareSelectedPacksForRuntimeIndex(contextLabel, logResult)) {
        Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] Content pack staging failed.");
        if (failOnError) throw new BuildFailedException("Content pack staging failed.");
        return false;
      }
    }
    else if (string.Equals(contextLabel, BuildContext.PlayerPrebuild, StringComparison.Ordinal)) {
      if (!ContentPackPipeline.PrepareSelectedPacksForRuntimeIndex(contextLabel, logResult)) {
        Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] Player-build content pack preparation failed.");
        if (failOnError) throw new BuildFailedException("Player-build content pack preparation failed.");
        return false;
      }
    }

    EnsureFolderExists(Path.GetDirectoryName(BuilderConfig.SettingsAssetPath));
    EnsureFolderExists(BuilderConfig.RuntimeIndexFolder);

    var streamingSettings = EnsureStreamingSettingsAsset();
    var includeAsset = EnsureIncludeAsset();
    var manifestAssetPath = EnsureManifestAssetPath();

    var textureGroup = EnsureAddressableGroup(addressableSettings, BuilderConfig.TextureAddressablesGroupName, contextLabel, logResult, out var textureSchemaRepairs);
    var indexGroup = EnsureAddressableGroup(addressableSettings, BuilderConfig.IndexAddressablesGroupName, contextLabel, logResult, out var indexSchemaRepairs);
    var state = new BuildState(addressableSettings, indexGroup, contextLabel, logResult);
    state.schemaRepairs = textureSchemaRepairs + indexSchemaRepairs;
    state.textureGroupsByName[textureGroup.Name] = textureGroup;
    if (!ValidateAddressableGroupsPreflight(state, contextLabel, failOnError)) {
      LogRuntimeIndexSummary(contextLabel, false, libraryNameCount: 0, shardCount: 0, schemaRepairs: state.schemaRepairs, errorCount: state.errors.Count);
      return false;
    }

    var librariesByKey = DiscoverLibraryPaths();
    ReportDuplicateShortNameAmbiguities(librariesByKey);
    var guidToLibraryName = DiscoverGuidToLibraryName(librariesByKey);
    var requestedLibraryReferences = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    var requestedLibraryNames = CollectRequestedLibraryNames(librariesByKey, guidToLibraryName, includeAsset, requestedLibraryReferences);
    BuildActiveTextureGuidIndex(state);
    var customSheetRowsByLibrary = DiscoverCustomSpriteSheetRows(state);
    AddRuntimePinnedTextureEntries(state);
    foreach (var libraryName in customSheetRowsByLibrary.Keys) {
      requestedLibraryNames.Add(libraryName);
    }

    var shardAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var manifestEntries = new List<ManifestRow>();
    var builtCanonicalLibraryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    var orderedLibraryNames = requestedLibraryNames.ToList();
    orderedLibraryNames.Sort(StringComparer.Ordinal);

    Debug.Log($"[SpriteIndexBuilder] RebuildRuntimeIndexInternal: Processing {orderedLibraryNames.Count} requested library names.");

    for (var i = 0; i < orderedLibraryNames.Count; i++) {
      var requestedLibraryName = orderedLibraryNames[i];
      var logLibraryProgress = state.logResult &&
        (i == 0 || (i + 1) % 10 == 0 || i == orderedLibraryNames.Count - 1);
      if (logLibraryProgress) {
        Debug.Log(
          "[SpriteIndexBuilder] Runtime index library progress." +
          " library=" + (i + 1) + "/" + orderedLibraryNames.Count +
          " requested='" + requestedLibraryName + "'" +
          " phase='start'"
        );
      }

      var libraryName = ResolveCanonicalLibraryName(requestedLibraryName, librariesByKey, state.runtimeAmbiguityWarnings, contextLabel);
      if (string.IsNullOrWhiteSpace(libraryName)) {
        var normalizedRequested = SpriteAddressResolver.NormalizeNamePart(requestedLibraryName);
        if (customSheetRowsByLibrary.ContainsKey(normalizedRequested)) {
          libraryName = normalizedRequested;
        }
      }
      if (string.IsNullOrWhiteSpace(libraryName)) {
        var error =
          "Missing color library or sprite sheet for requested libraryName '" + requestedLibraryName + "'." +
          BuildRequestedLibraryReferenceSuffix(requestedLibraryName, requestedLibraryReferences);
        Debug.LogError($"[SpriteIndexBuilder] Failed to resolve canonical library name for '{requestedLibraryName}'.");
        state.errors.Add(error);
        continue;
      }

      if (!builtCanonicalLibraryNames.Add(libraryName)) {
        continue;
      }

      var hasColorLibrary = librariesByKey.TryGetValue(libraryName, out var colorLibraryPath);
      customSheetRowsByLibrary.TryGetValue(libraryName, out var customSheetRows);
      var customSheetRowCount = customSheetRows != null ? customSheetRows.Count : 0;
      if (!hasColorLibrary && customSheetRowCount <= 0) {
        Debug.LogError($"[SpriteIndexBuilder] Library '{libraryName}' not found in librariesByKey or custom sheet rows. Expected path.");
        state.errors.Add(
          "Missing color library or sprite sheet for libraryName '" + libraryName + "' (requested '" + requestedLibraryName + "')." +
          BuildRequestedLibraryReferenceSuffix(requestedLibraryName, requestedLibraryReferences));
        continue;
      }

      var colorRows = hasColorLibrary
        ? ParseLibraryRows(colorLibraryPath, state.errors)
        : new Dictionary<string, SpriteRef>(StringComparer.Ordinal);
      if (state.logResult && colorRows.Count >= 50000) {
        Debug.Log(
          "[SpriteIndexBuilder] Processing large runtime index library." +
          " library=" + (i + 1) + "/" + orderedLibraryNames.Count +
          " libraryName='" + libraryName + "'" +
          " color_rows=" + colorRows.Count
        );
      }
      var normalLibraryName = libraryName + "N";
      var hasNormalLibrary = librariesByKey.TryGetValue(normalLibraryName, out var normalLibraryPath);
      if (hasColorLibrary && !hasNormalLibrary) {
        state.missingNormalLibraryCount++;
      }

      var normalRows = hasNormalLibrary
        ? ParseLibraryRows(normalLibraryPath, state.errors)
        : new Dictionary<string, SpriteRef>(StringComparer.Ordinal);
      if (colorRows.Count == 0 && customSheetRowCount <= 0) {
        state.skippedColorLibraryCount++;
        Debug.LogWarning(
          "[SpriteIndexBuilder] [" + contextLabel + "] Skipped sprite library because it produced zero color rows." +
          " libraryName='" + libraryName + "'" +
          " path='" + colorLibraryPath + "'" +
          " requested='" + requestedLibraryName + "'");
        continue;
      }

      var shardRows = new List<ShardRow>(colorRows.Count + customSheetRowCount);
      var skippedColorRowsForLibrary = 0;
      var failedResolveCount = 0;
      var failedValidateCount = 0;
      var processedColorRowCount = 0;
      var nextColorRowProgress = 50000;
      string sampleResolveContext = "";
      string sampleValidateFailure = "";
      string sampleValidateContext = "";
      foreach (var pair in colorRows) {
        processedColorRowCount++;
        if (state.logResult && processedColorRowCount >= nextColorRowProgress) {
          Debug.Log(
            "[SpriteIndexBuilder] Runtime index row progress." +
            " libraryName='" + libraryName + "'" +
            " processed=" + processedColorRowCount + "/" + colorRows.Count
          );
          nextColorRowProgress += 50000;
        }

        var separator = pair.Key.IndexOf('\u001f');
        if (separator <= 0 || separator >= pair.Key.Length - 1) {
          state.errors.Add("Invalid row key '" + pair.Key + "' in '" + colorLibraryPath + "'.");
          continue;
        }

        var category = pair.Key.Substring(0, separator);
        var label = pair.Key.Substring(separator + 1);
        ParseLabel(label, out var labelPrefix, out var frame);

        var colorContext = libraryName + "/" + category + ":" + label + " (color)";
        var colorAddress = ResolveSpriteAddress(state, pair.Value, colorContext, recordError: false);
        if (string.IsNullOrWhiteSpace(colorAddress)) {
          skippedColorRowsForLibrary++;
          failedResolveCount++;
          if (string.IsNullOrWhiteSpace(sampleResolveContext)) {
            sampleResolveContext = colorContext +
              " guid='" + (pair.Value.guid ?? "") + "'" +
              " fileId=" + pair.Value.fileId.ToString(CultureInfo.InvariantCulture);
          }
          continue;
        }
        if (!ValidateRuntimeAtlasAddress(state, colorAddress, colorContext, out var validationFailure, recordError: false)) {
          skippedColorRowsForLibrary++;
          failedValidateCount++;
          if (string.IsNullOrWhiteSpace(sampleValidateFailure)) {
            sampleValidateFailure = validationFailure ?? "";
            sampleValidateContext = colorContext + " address='" + colorAddress + "'";
          }
          continue;
        }

        // Color entries are the primary runtime rows, so their normal/specular
        // companions are not otherwise discovered from a separate library. Add
        // the exact convention-based companions before deriving their slice
        // addresses; without this, builds contain only atlas.png and glyph/UI
        // lighting requests for atlasN.png and atlasS.png miss the catalog.
        AddPairedAtlasTextureAssets(state, colorAddress);

        var normalAddress = "";
        var autoDerivedNormal = false;
        if (normalRows.TryGetValue(pair.Key, out var normalRef)) {
          normalAddress = ResolveSpriteAddress(state, normalRef, normalLibraryName + "/" + category + ":" + label + " (normal)", recordError: false);
          if (IsLegacyJpegSpriteAddress(normalAddress)) {
            normalAddress = "";
          }
          if (!string.IsNullOrWhiteSpace(normalAddress) &&
              !ValidateRuntimeAtlasAddress(state, normalAddress, normalLibraryName + "/" + category + ":" + label + " (normal)", recordError: false)) {
            normalAddress = "";
          }
        }

        if (string.IsNullOrWhiteSpace(normalAddress) &&
            TryResolveDerivedNormalAddress(state, colorAddress, out normalAddress)) {
          autoDerivedNormal = true;
        }

        if (!string.IsNullOrWhiteSpace(normalAddress) &&
            !ValidateRuntimeAtlasAddress(state, normalAddress, normalLibraryName + "/" + category + ":" + label + " (derived normal)", recordError: false)) {
          normalAddress = "";
          autoDerivedNormal = false;
        }

        if (autoDerivedNormal) {
          state.autoDerivedNormalAddressCount++;
        }
        else if (string.IsNullOrWhiteSpace(normalAddress)) {
          state.missingNormalAddressCount++;
        }

        var specularAddress = "";
        if (TryResolveDerivedSpecularAddress(state, colorAddress, out var resolvedSpecularAddress)) {
          if (ValidateRuntimeAtlasAddress(state, resolvedSpecularAddress, normalLibraryName + "/" + category + ":" + label + " (derived specular)", recordError: false)) {
            specularAddress = resolvedSpecularAddress;
          }
        }

        shardRows.Add(new ShardRow(labelPrefix, category, frame, colorAddress, normalAddress, specularAddress));
      }

      if (customSheetRows != null && customSheetRows.Count > 0) {
        shardRows.AddRange(customSheetRows);
      }

      if (skippedColorRowsForLibrary > 0) {
        state.skippedColorRowCount += skippedColorRowsForLibrary;
        TrackSkippedColorLibrarySummary(state, libraryName, requestedLibraryName, skippedColorRowsForLibrary, colorRows.Count);
      }

      if (shardRows.Count == 0) {
        state.skippedColorLibraryCount++;
        if (ShouldLogLibraryDiagnostics(libraryName, requestedLibraryName)) {
          Debug.LogError(
            "[SpriteIndexBuilder] Library produced zero shard rows." +
            " libraryName='" + libraryName + "'" +
            " requested='" + requestedLibraryName + "'" +
            " path='" + (colorLibraryPath ?? "") + "'" +
            " colorRows=" + colorRows.Count +
            " sheetRows=" + customSheetRowCount +
            " failedResolveCount=" + failedResolveCount +
            " failedValidateCount=" + failedValidateCount +
            (string.IsNullOrWhiteSpace(sampleResolveContext) ? "" : " sampleResolve='" + sampleResolveContext + "'") +
            (string.IsNullOrWhiteSpace(sampleValidateContext) ? "" : " sampleValidate='" + sampleValidateContext + "'") +
            (string.IsNullOrWhiteSpace(sampleValidateFailure) ? "" : " validateReason='" + sampleValidateFailure + "'")
          );
        }
        continue;
      }

      shardRows.Sort((left, right) => {
        var byCategory = string.Compare(left.category, right.category, StringComparison.Ordinal);
        if (byCategory != 0) return byCategory;
        var byLabelPrefix = string.Compare(left.labelPrefix, right.labelPrefix, StringComparison.Ordinal);
        if (byLabelPrefix != 0) return byLabelPrefix;
        return left.frame.CompareTo(right.frame);
      });

      var shardBody = BuildShardBody(shardRows);
      var shardPath = BuildShardAssetPath(libraryName);
      WriteIfChanged(shardPath, shardBody);
      shardAssetPaths.Add(NormalizePath(shardPath));

      var shardAddress = "SpriteRuntimeIndex/Shard/" + libraryName;
      EnsureAddressableEntry(addressableSettings, indexGroup, shardPath, shardAddress);

      manifestEntries.Add(new ManifestRow(
        libraryName,
        shardAddress,
        shardPath,
        shardRows.Count,
        ComputeHash(shardBody)
      ));

      if (logLibraryProgress) {
        Debug.Log(
          "[SpriteIndexBuilder] Runtime index library progress." +
          " library=" + (i + 1) + "/" + orderedLibraryNames.Count +
          " libraryName='" + libraryName + "'" +
          " rows=" + shardRows.Count +
          " phase='done'"
        );
      }
    }

    EnsureActiveStageTextureEntries(state);
    CleanupStaleTextureEntries(state);
    CleanupStaleShardAssets(shardAssetPaths);
    CleanupStaleIndexEntries(state, indexGroup, shardAssetPaths, manifestAssetPath, state.activeAtlasMetadataAssetPaths);

    manifestEntries.Sort((left, right) => string.Compare(left.libraryName, right.libraryName, StringComparison.Ordinal));
    if (librariesByKey.TryGetValue("UI/Fonts", out var uiFontsLibraryPath) &&
        manifestEntries.All(entry => !string.Equals(entry.libraryName, "UI/Fonts", StringComparison.OrdinalIgnoreCase))) {
      Debug.LogError(
        "[SpriteIndexBuilder] Missing manifest entry for UI/Fonts after runtime index rebuild." +
        " libraryPath='" + uiFontsLibraryPath + "'" +
        " requestedLibraryCount=" + orderedLibraryNames.Count +
        " activeTextureCount=" + state.activeTextureAssetPaths.Count
      );
    }
    WriteManifestTextAsset(manifestAssetPath, manifestEntries);

    var manifestAddress = streamingSettings == null || string.IsNullOrWhiteSpace(streamingSettings.manifestAddress)
      ? BuilderConfig.DefaultManifestAddress
      : streamingSettings.manifestAddress.Trim();
    EnsureAddressableEntry(addressableSettings, indexGroup, BuilderConfig.ManifestAssetPath, manifestAddress);

    if (streamingSettings != null) {
      EditorUtility.SetDirty(streamingSettings);
    }
    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh();

    ClearCachedTextureRoots();

    if (state.errors.Count > 0) {
      var limitedErrors = state.errors.Take(50).ToList();
      for (var i = 0; i < limitedErrors.Count; i++) {
        Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] " + limitedErrors[i]);
      }
      if (state.errors.Count > limitedErrors.Count) {
        Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] Additional errors omitted: " + (state.errors.Count - limitedErrors.Count));
      }
      LogSkippedColorRowSummary(contextLabel, state);
      LogSyntheticTextureLabelSummary(contextLabel, state);
      LogAtlasMetadataSummary(contextLabel, state);
      LogNormalAddressSummary(contextLabel, state);
      LogLocalIdSupplementSummary(contextLabel, state);
      LogGroupedAtlasBuildSurrogateSummary(contextLabel, state);
      LogRuntimeIndexSummary(contextLabel, false, manifestEntries.Count, shardAssetPaths.Count, state.schemaRepairs, state.errors.Count);

      if (failOnError) {
        throw new BuildFailedException("Sprite runtime index generation failed with " + state.errors.Count + " errors.");
      }
      return false;
    }

    LogSkippedColorRowSummary(contextLabel, state);
    LogSyntheticTextureLabelSummary(contextLabel, state);
    LogAtlasMetadataSummary(contextLabel, state);
    LogNormalAddressSummary(contextLabel, state);
    LogLocalIdSupplementSummary(contextLabel, state);
    LogGroupedAtlasBuildSurrogateSummary(contextLabel, state);
    LogRuntimeIndexSummary(contextLabel, true, manifestEntries.Count, shardAssetPaths.Count, state.schemaRepairs, 0);

    return true;
  }

}
#endif
