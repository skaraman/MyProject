#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed partial class GroupAtlasWindow : EditorWindow {
  void ExportGroupedAtlases() {
    if (!EnsureScanAvailable(out var sourceFolderPath)) return;

    if (!TryGetOutputFolderPath(out var outputFolderPath, true)) return;
    var sanitizedOutputName = GetSanitizedOutputName();

    var failureLogs = new List<string>();
    var pendingImports = new List<PendingGroupedAtlasImport>();
    var exportedCandidateCount = 0;
    var exportedPageCount = 0;
    var cleanupSummary = new ExportCleanupSummary();
    var deferredWritePhaseStarted = false;

    try {
      BeginDeferredGroupedAtlasWritePhase(sourceFolderPath, scannedCandidates.Count);
      deferredWritePhaseStarted = true;

      for (var i = 0; i < scannedCandidates.Count; i++) {
        var candidate = scannedCandidates[i];
        if (!TryExportCandidate(outputFolderPath, candidate, sanitizedOutputName, pendingImports, out var pageCount, out var error)) {
          AddFailureLog(failureLogs, BuildCandidateLabel(candidate), error);
          continue;
        }

        exportedPageCount += pageCount;
      }

      if (pendingImports.Count > 0) {
        var exportedCandidates = CollectWrittenGroupedAtlasCandidates(pendingImports);
        exportedCandidateCount = exportedCandidates.Count;
      }
    }
    finally {
      if (deferredWritePhaseStarted) {
        EndDeferredGroupedAtlasWritePhase(sourceFolderPath, pendingImports.Count, failureLogs.Count);
      }
    }

    if (!ApplyDeferredGroupedImportMetadata(sourceFolderPath, pendingImports, out var importMetadataError)) {
      AddFailureLog(failureLogs, sourceFolderPath, importMetadataError);
    }
    else if (pendingImports.Count > 0) {
      var exportedCandidates = CollectWrittenGroupedAtlasCandidates(pendingImports);
      cleanupSummary = CleanupExportedSourceAssets(sourceFolderPath, exportedCandidates);
      exportedCandidateCount = exportedCandidates.Count;
    }

    Debug.Log(
      "[GearGroupAtlas] Export complete." +
      " source='" + sourceFolderPath + "'" +
      " exported_candidates=" + exportedCandidateCount +
      " exported_pages=" + exportedPageCount +
      " deleted_source_assets=" + cleanupSummary.deletedAssetCount +
      " deleted_source_folders=" + cleanupSummary.deletedFolderCount +
      " failures=" + failureLogs.Count +
      " deferred_import=True");

    for (var i = 0; i < failureLogs.Count; i++) {
      Debug.LogWarning("[GearGroupAtlas] " + failureLogs[i]);
    }
  }

  static void BeginDeferredGroupedAtlasWritePhase(string sourceFolderPath, int candidateCount) {
    AssetDatabase.StartAssetEditing();
    Debug.Log(
      "[GearGroupAtlas] Deferred import write phase started." +
      " source='" + sourceFolderPath + "'" +
      " candidates=" + candidateCount);
  }

  static void EndDeferredGroupedAtlasWritePhase(string sourceFolderPath, int pendingImportCount, int failureCount) {
    AssetDatabase.StopAssetEditing();
    Debug.Log(
      "[GearGroupAtlas] Deferred import write phase completed." +
      " source='" + sourceFolderPath + "'" +
      " pending_imports=" + pendingImportCount +
      " failures=" + failureCount);
  }

  static List<GroupCandidate> CollectWrittenGroupedAtlasCandidates(List<PendingGroupedAtlasImport> pendingImports) {
    var candidates = new List<GroupCandidate>();
    if (pendingImports == null || pendingImports.Count <= 0) return candidates;

    var seenCandidates = new HashSet<GroupCandidate>();
    for (var i = 0; i < pendingImports.Count; i++) {
      var candidate = pendingImports[i]?.candidate;
      if (candidate == null || !seenCandidates.Add(candidate)) continue;
      candidates.Add(candidate);
    }

    return candidates;
  }

  bool TryExportCandidate(
    string outputRootPath,
    GroupCandidate candidate,
    string sanitizedOutputName,
    List<PendingGroupedAtlasImport> pendingImports,
    out int exportedPageCount,
    out string error) {
    exportedPageCount = 0;
    error = "";
    if (candidate == null) {
      error = "Missing group candidate data.";
      return false;
    }

    if (!TryBuildPackedItems(candidate, out var items, out var representativeSourceAtlasPath, out error)) {
      return false;
    }
    var inheritedTrimMetadataCount = CountInheritedTrimMetadata(items);

    var outputFolderPath = BuildCandidateOutputFolderPath(outputRootPath, candidate, sanitizedOutputName);
    if (string.IsNullOrWhiteSpace(outputFolderPath)) {
      error = "Could not resolve an output folder for group '" + BuildCandidateLabel(candidate) + "'.";
      return false;
    }

    Directory.CreateDirectory(Path.GetFullPath(outputFolderPath));
    if (!TryBuildCandidatePages(outputFolderPath, candidate, items, out var pages, out var reusedPageCount, out error)) {
      return false;
    }

    var candidatePendingImports = new List<PendingGroupedAtlasImport>();

    for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++) {
      var page = pages[pageIndex];
      page.colorAtlasPath = BuildPageAtlasAssetPath(outputFolderPath, candidate, page.pageIndex, false);
      if (ExportNormalAtlases) {
        page.normalAtlasPath = BuildPageAtlasAssetPath(outputFolderPath, candidate, page.pageIndex, true);
      }
    }

    CleanupStaleCandidateOutputs(outputFolderPath, candidate, pages, ExportNormalAtlases);

    for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++) {
      var page = pages[pageIndex];

      if (!TryWritePageTexture(page.colorAtlasPath, page, false, out error)) {
        return false;
      }

      if (!TryWriteMetadata(page.colorAtlasPath, candidate, representativeSourceAtlasPath, page, false, out _, out var editorImportMetadataJson, out error)) {
        return false;
      }
      candidatePendingImports.Add(new PendingGroupedAtlasImport {
        candidate = candidate,
        atlasAssetPath = page.colorAtlasPath,
        editorImportMetadataJson = editorImportMetadataJson
      });

      if (ExportNormalAtlases) {
        if (!TryWritePageTexture(page.normalAtlasPath, page, true, out error)) {
          return false;
        }

        if (!TryWriteMetadata(page.normalAtlasPath, candidate, representativeSourceAtlasPath, page, true, out _, out editorImportMetadataJson, out error)) {
          return false;
        }
        candidatePendingImports.Add(new PendingGroupedAtlasImport {
          candidate = candidate,
          atlasAssetPath = page.normalAtlasPath,
          editorImportMetadataJson = editorImportMetadataJson
        });
      }
    }

    if (pendingImports != null && candidatePendingImports.Count > 0) {
      pendingImports.AddRange(candidatePendingImports);
    }

    exportedPageCount = pages.Count;
    Debug.Log(
      "[GearGroupAtlas] Exported group." +
      " group='" + BuildCandidateLabel(candidate) + "'" +
      " kind='" + (IsSkinCandidate(candidate) ? "skin" : "gear") + "'" +
      " reused_pages=" + reusedPageCount +
      " pages=" + pages.Count +
      " sprites=" + items.Count +
      " inherited_trim_metadata=" + inheritedTrimMetadataCount +
      " source_atlases=" + candidate.sourceAtlases.Count +
      " animations=" + candidate.sourceCategories.Count);
    return true;
  }

  static bool ApplyDeferredGroupedImportMetadata(string contextPath, List<PendingGroupedAtlasImport> pendingImports, out string error) {
    error = "";
    if (pendingImports == null || pendingImports.Count <= 0) {
      return true;
    }

    var changedAtlasPaths = new List<string>(pendingImports.Count);
    var seenChangedAtlasPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < pendingImports.Count; i++) {
      var pendingImport = pendingImports[i];
      if (pendingImport == null) {
        continue;
      }

      if (!GeneratedAtlasImportMetadataStore.TryWrite(
            pendingImport.atlasAssetPath,
            pendingImport.editorImportMetadataJson,
            forceReimport: false,
            out var metadataChanged,
            out error)) {
        error =
          "Failed to store grouped atlas import metadata for '" +
          (pendingImport.atlasAssetPath ?? contextPath ?? "") +
          "': " + error;
        return false;
      }

      if (!metadataChanged) {
        continue;
      }

      var normalizedAtlasPath = NormalizePath(pendingImport.atlasAssetPath);
      if (string.IsNullOrWhiteSpace(normalizedAtlasPath) || !seenChangedAtlasPaths.Add(normalizedAtlasPath)) {
        continue;
      }

      changedAtlasPaths.Add(normalizedAtlasPath);
    }

    if (!GeneratedAtlasImportMetadataStore.TryBatchReimport(contextPath, changedAtlasPaths, out error)) {
      return false;
    }

    return true;
  }

  bool TryBuildCandidatePages(
    string outputFolderPath,
    GroupCandidate candidate,
    List<PackedSpriteBuildItem> incomingItems,
    out List<AtlasPage> pages,
    out int reusedPageCount,
    out string error) {
    pages = new List<AtlasPage>();
    reusedPageCount = 0;
    error = "";
    if (incomingItems == null || incomingItems.Count <= 0) {
      error = "No grouped sprite items were available for packing.";
      return false;
    }

    if (!TryLoadExistingGroupedPages(outputFolderPath, candidate, incomingItems, out var existingPages, out error)) {
      return false;
    }

    reusedPageCount = existingPages.Count;
    var remainingItems = new List<PackedSpriteBuildItem>();
    var orderedIncomingItems = BuildGroupedPackSequence(incomingItems);

    for (var i = 0; i < orderedIncomingItems.Count; i++) {
      var item = orderedIncomingItems[i];
      if (!TryPlaceItemIntoExistingPages(existingPages, item)) {
        remainingItems.Add(item);
      }
    }

    existingPages = FilterAndSortPages(existingPages);

    for (var i = 0; i < existingPages.Count; i++) {
      RefreshPageBounds(existingPages[i], preserveExistingSize: true);
    }

    if (remainingItems.Count > 0) {
      if (!TryPackItemsIntoPages(remainingItems, out var newPages, out error)) {
        return false;
      }

      var pageIndexOffset = GetNextPageIndex(existingPages);
      for (var pageIndex = 0; pageIndex < newPages.Count; pageIndex++) {
        var page = newPages[pageIndex];
        if (page == null) continue;
        page.pageIndex += pageIndexOffset;
        for (var itemIndex = 0; itemIndex < page.items.Count; itemIndex++) {
          if (page.items[itemIndex] == null) continue;
          page.items[itemIndex].pageIndex = page.pageIndex;
        }

        existingPages.Add(page);
      }
    }

    pages = FilterAndSortPages(existingPages);
    Debug.Log(
      "[GearGroupAtlas] Prepared candidate pages." +
      " group='" + BuildCandidateLabel(candidate) + "'" +
      " incoming_sprites=" + incomingItems.Count +
      " reused_pages=" + reusedPageCount +
      " output_pages=" + pages.Count +
      " new_page_sprites=" + remainingItems.Count);
    return pages.Count > 0;
  }

  bool TryBuildPackedItems(
    GroupCandidate candidate,
    out List<PackedSpriteBuildItem> items,
    out string representativeSourceAtlasPath,
    out string error) {
    items = new List<PackedSpriteBuildItem>();
    representativeSourceAtlasPath = "";
    error = "";

    var loadedAtlases = new Dictionary<string, LoadedAtlas>(StringComparer.OrdinalIgnoreCase);
    try {
      var orderedRecords = candidate.sourceAtlases ?? new List<SourceAtlasRecord>();
      for (var i = 0; i < orderedRecords.Count; i++) {
        var record = orderedRecords[i];
        if (record == null) continue;

        if (string.IsNullOrWhiteSpace(representativeSourceAtlasPath)) {
          representativeSourceAtlasPath = record.atlasPath;
        }

        if (!TryGetOrLoadAtlas(record.atlasPath, loadedAtlases, out var colorAtlas, out error)) {
          return false;
        }

        LoadedAtlas normalAtlas = null;
        if (ExportNormalAtlases && !string.IsNullOrWhiteSpace(record.normalAtlasPath)) {
          if (!TryGetOrLoadAtlas(record.normalAtlasPath, loadedAtlases, out normalAtlas, out error)) {
            return false;
          }
        }

        if (colorAtlas.orderedSprites.Count <= 0) {
          error = "Atlas '" + record.atlasPath + "' has no sliced sprites.";
          return false;
        }

        for (var spriteIndex = 0; spriteIndex < colorAtlas.orderedSprites.Count; spriteIndex++) {
          var colorSprite = colorAtlas.orderedSprites[spriteIndex];
          if (colorSprite == null) continue;

          var normalSprite = ExportNormalAtlases ? FindMatchingSprite(normalAtlas, colorSprite.name) : null;
          if (!TryAnalyzeSourceSprite(record, colorAtlas, normalAtlas, colorSprite, normalSprite, out var item, out error)) {
            return false;
          }

          items.Add(item);
        }
      }
    }
    finally {
      foreach (var atlas in loadedAtlases.Values) {
        if (atlas?.texture != null) {
          DestroyImmediate(atlas.texture);
        }
      }
    }

    if (items.Count <= 0) {
      error = "No matching sliced source atlas sprites were found for '" + BuildCandidateLabel(candidate) + "'.";
      return false;
    }

    var disambiguatedCount = EnsureUniqueOutputSpriteNames(items);
    if (disambiguatedCount > 0) {
      Debug.LogWarning(
        "[GearGroupAtlas] Disambiguated duplicate grouped sprite names." +
        " group='" + BuildCandidateLabel(candidate) + "'" +
        " renamed=" + disambiguatedCount);
    }

    return true;
  }

  bool TryLoadExistingGroupedPages(
    string outputFolderPath,
    GroupCandidate candidate,
    List<PackedSpriteBuildItem> incomingItems,
    out List<AtlasPage> pages,
    out string error) {
    pages = new List<AtlasPage>();
    error = "";
    if (string.IsNullOrWhiteSpace(outputFolderPath)) return true;

    var fullOutputFolderPath = Path.GetFullPath(outputFolderPath);
    if (!Directory.Exists(fullOutputFolderPath)) return true;

    var incomingItemNames = new HashSet<string>(StringComparer.Ordinal);
    if (incomingItems != null) {
      for (var i = 0; i < incomingItems.Count; i++) {
        var itemName = incomingItems[i]?.outputSpriteName;
        if (string.IsNullOrWhiteSpace(itemName)) continue;
        incomingItemNames.Add(itemName);
      }
    }

    var metadataFullPaths = CollectExistingGroupedMetadataPaths(fullOutputFolderPath, candidate);
    if (metadataFullPaths.Count <= 0) return true;

    for (var i = 0; i < metadataFullPaths.Count; i++) {
      var metadataFullPath = metadataFullPaths[i];
      if (!TryConvertFullPathToAssetPath(metadataFullPath, out var metadataAssetPath)) continue;

      if (!TryLoadGroupedMetadataPayload(metadataAssetPath, out var payload, out error)) {
        error = "Failed to read grouped atlas metadata '" + metadataAssetPath + "': " + error;
        return false;
      }

      if (payload == null ||
          !string.Equals(payload.sourceKind, "color", StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      var atlasAssetPath = NormalizePath(Path.ChangeExtension(metadataAssetPath, ".png"));
      if (!TrimmedAtlasExporterWindow.TryLoadTextureFromDisk(atlasAssetPath, out var texture, out error)) {
        return false;
      }

      try {
        var pixels = texture.GetPixels32();
        var page = new AtlasPage {
          pageIndex = payload.pageIndex >= 0 ? payload.pageIndex : pages.Count,
          width = payload.atlasWidth > 0 ? payload.atlasWidth : texture.width,
          height = payload.atlasHeight > 0 ? payload.atlasHeight : texture.height
        };

        var metadataSprites = payload.sprites ?? new List<GroupedAtlasSpriteMetadata>();
        for (var spriteIndex = 0; spriteIndex < metadataSprites.Count; spriteIndex++) {
          var spriteMetadata = metadataSprites[spriteIndex];
          if (spriteMetadata == null || string.IsNullOrWhiteSpace(spriteMetadata.name)) continue;
          if (incomingItemNames.Contains(spriteMetadata.name)) continue;
          if (!TryBuildExistingPackedItem(spriteMetadata, pixels, texture.width, page.pageIndex, out var item, out error)) {
            return false;
          }

          page.items.Add(item);
        }

        if (page.items.Count > 0) {
          pages.Add(page);
        }
      }
      finally {
        DestroyImmediate(texture);
      }
    }

    return true;
  }

  bool TryBuildExistingPackedItem(
    GroupedAtlasSpriteMetadata spriteMetadata,
    Color32[] atlasPixels,
    int atlasWidth,
    int pageIndex,
    out PackedSpriteBuildItem item,
    out string error) {
    item = null;
    error = "";
    if (spriteMetadata == null) {
      error = "Missing grouped atlas sprite metadata.";
      return false;
    }

    var packedRect = spriteMetadata.packedRect;
    if (packedRect.width <= 0 || packedRect.height <= 0) {
      error = "Grouped sprite '" + (spriteMetadata.name ?? "") + "' has an invalid packed rect.";
      return false;
    }

    var pixels = CopyPackedPixels(atlasPixels, atlasWidth, packedRect, out error);
    if (pixels == null) return false;

    var normalizedSourceAtlasPath = NormalizePath(spriteMetadata.sourceAtlasAssetPath);
    item = new PackedSpriteBuildItem {
      outputSpriteName = spriteMetadata.name,
      sourceCategory = spriteMetadata.sourceCategory,
      colorSourceAtlasPath = normalizedSourceAtlasPath,
      normalSourceAtlasPath = normalizedSourceAtlasPath,
      sourceSpriteName = spriteMetadata.sourceSpriteName,
      sourcePartCode = spriteMetadata.sourcePartCode,
      empty = spriteMetadata.empty,
      trimRectInSourceSprite = spriteMetadata.trimRectInSourceSprite,
      packedRect = packedRect,
      offsetFromCellCenterPx = spriteMetadata.offsetFromCellCenterPx,
      colorPixels = pixels,
      pageIndex = pageIndex
    };
    return true;
  }

  bool TryGetOrLoadAtlas(
    string atlasPath,
    Dictionary<string, LoadedAtlas> cache,
    out LoadedAtlas loadedAtlas,
    out string error) {
    error = "";
    var normalizedAtlasPath = NormalizePath(atlasPath);
    if (cache.TryGetValue(normalizedAtlasPath, out loadedAtlas) && loadedAtlas != null) {
      return true;
    }

    if (!TrimmedAtlasExporterWindow.TryLoadTextureFromDisk(normalizedAtlasPath, out var texture, out error)) {
      loadedAtlas = null;
      return false;
    }

    var sprites = LoadGroupedAtlasSprites(normalizedAtlasPath);
    if (sprites.Count <= 0) {
      DestroyImmediate(texture);
      loadedAtlas = null;
      error = "Atlas '" + normalizedAtlasPath + "' is not sliced into sprites.";
      return false;
    }

    loadedAtlas = new LoadedAtlas {
      atlasPath = normalizedAtlasPath,
      texture = texture,
      pixels = texture.GetPixels32(),
      orderedSprites = sprites,
      trimmedSourceMetadataByName = LoadTrimmedSourceMetadataByName(normalizedAtlasPath)
    };

    for (var i = 0; i < sprites.Count; i++) {
      var sprite = sprites[i];
      if (sprite == null || string.IsNullOrWhiteSpace(sprite.name)) continue;
      loadedAtlas.spritesByName[sprite.name] = sprite;
    }

    cache[normalizedAtlasPath] = loadedAtlas;
    return true;
  }

  bool TryAnalyzeSourceSprite(
    SourceAtlasRecord record,
    LoadedAtlas colorAtlas,
    LoadedAtlas normalAtlas,
    Sprite colorSprite,
    Sprite normalSprite,
    out PackedSpriteBuildItem item,
    out string error) {
    item = null;
    error = "";
    if (record == null || colorAtlas == null || colorSprite == null) {
      error = "Missing atlas data while analyzing grouped sprite.";
      return false;
    }

    if (TryBuildItemFromTrimmedSourceMetadata(record, colorAtlas, normalAtlas, colorSprite, normalSprite, out item, out error)) {
      return true;
    }

    var sourceRect = ToPixelRect(colorSprite.rect);
    AnalyzeTrimmedSprite(colorAtlas, sourceRect, out var trimRect, out var offsetPx, out var colorTrimPixels, out var empty);
    item = new PackedSpriteBuildItem {
      outputSpriteName = BuildGroupedSpriteName(record.partCode, record.category, colorSprite.name),
      sourceCategory = record.category,
      colorSourceAtlasPath = NormalizePath(record.atlasPath),
      normalSourceAtlasPath = NormalizePath(!string.IsNullOrWhiteSpace(record.normalAtlasPath) ? record.normalAtlasPath : record.atlasPath),
      sourceSpriteName = colorSprite.name,
      sourcePartCode = record.partCode,
      empty = empty,
      trimRectInSourceSprite = trimRect,
      offsetFromCellCenterPx = offsetPx,
      colorPixels = colorTrimPixels
    };

    if (ExportNormalAtlases) {
      item.normalPixels = BuildNormalTrimPixels(colorTrimPixels, trimRect, sourceRect, normalAtlas, normalSprite);
    }

    return true;
  }

  bool TryBuildItemFromTrimmedSourceMetadata(
    SourceAtlasRecord record,
    LoadedAtlas colorAtlas,
    LoadedAtlas normalAtlas,
    Sprite colorSprite,
    Sprite normalSprite,
    out PackedSpriteBuildItem item,
    out string error) {
    item = null;
    error = "";
    if (record == null || colorAtlas == null || colorSprite == null) return false;
    if (!TryGetTrimmedSourceMetadata(colorAtlas, colorSprite.name, out var sourceMetadata)) return false;

    var sourcePackedRect = ResolveSourcePackedRect(colorSprite, sourceMetadata);
    var colorPixels = CopyPackedPixels(colorAtlas.pixels, colorAtlas.texture.width, sourcePackedRect, out var copyError);
    if (colorPixels == null) {
      Debug.LogWarning(
        "[GearGroupAtlas] Failed to reuse source trim metadata; falling back to pixel analysis." +
        " atlas='" + colorAtlas.atlasPath + "'" +
        " sprite='" + colorSprite.name + "'" +
        " error='" + copyError + "'");
      return false;
    }

    item = new PackedSpriteBuildItem {
      outputSpriteName = BuildGroupedSpriteName(record.partCode, record.category, colorSprite.name),
      sourceCategory = record.category,
      colorSourceAtlasPath = NormalizePath(record.atlasPath),
      normalSourceAtlasPath = NormalizePath(!string.IsNullOrWhiteSpace(record.normalAtlasPath) ? record.normalAtlasPath : record.atlasPath),
      sourceSpriteName = colorSprite.name,
      sourcePartCode = record.partCode,
      empty = sourceMetadata.empty,
      trimRectInSourceSprite = BuildInheritedTrimRect(sourceMetadata, sourcePackedRect),
      offsetFromCellCenterPx = sourceMetadata.offsetFromCellCenterPx,
      colorPixels = colorPixels,
      inheritedTrimMetadata = true
    };

    if (ExportNormalAtlases) {
      item.normalPixels = BuildNormalPixelsFromPackedSourceMetadata(colorPixels, normalAtlas, normalSprite);
    }

    return true;
  }

  static bool TryGetTrimmedSourceMetadata(
    LoadedAtlas atlas,
    string spriteName,
    out ExistingTrimmedAtlasSpriteMetadata sourceMetadata) {
    sourceMetadata = null;
    if (atlas?.trimmedSourceMetadataByName == null || string.IsNullOrWhiteSpace(spriteName)) return false;
    return atlas.trimmedSourceMetadataByName.TryGetValue(spriteName, out sourceMetadata) && sourceMetadata != null;
  }

  static PixelRect ResolveSourcePackedRect(Sprite sprite, ExistingTrimmedAtlasSpriteMetadata sourceMetadata) {
    var fallbackRect = sprite != null ? ToPixelRect(sprite.rect) : default;
    if (sourceMetadata == null) return fallbackRect;
    return sourceMetadata.packedRect.width > 0 && sourceMetadata.packedRect.height > 0
      ? sourceMetadata.packedRect
      : fallbackRect;
  }

  static PixelRect BuildInheritedTrimRect(ExistingTrimmedAtlasSpriteMetadata sourceMetadata, PixelRect sourcePackedRect) {
    var trimRect = sourceMetadata != null ? sourceMetadata.trimRectInCell : default;
    return new PixelRect(
      trimRect.x,
      trimRect.y,
      Math.Max(1, sourcePackedRect.width),
      Math.Max(1, sourcePackedRect.height));
  }

  Color32[] BuildNormalPixelsFromPackedSourceMetadata(Color32[] colorTrimPixels, LoadedAtlas normalAtlas, Sprite normalSprite) {
    if (colorTrimPixels == null || colorTrimPixels.Length <= 0) {
      return new[] { new Color32(128, 128, 255, 0) };
    }

    if (normalAtlas == null || normalSprite == null) {
      return BuildNeutralNormalPixels(colorTrimPixels);
    }

    TryGetTrimmedSourceMetadata(normalAtlas, normalSprite.name, out var normalMetadata);
    var normalPackedRect = ResolveSourcePackedRect(normalSprite, normalMetadata);
    var rawNormalPixels = CopyPackedPixels(normalAtlas.pixels, normalAtlas.texture.width, normalPackedRect, out var error);
    if (rawNormalPixels == null || rawNormalPixels.Length != colorTrimPixels.Length) {
      if (!string.IsNullOrWhiteSpace(error)) {
        Debug.LogWarning(
          "[GearGroupAtlas] Failed to reuse normal source trim metadata." +
          " atlas='" + normalAtlas.atlasPath + "'" +
          " sprite='" + normalSprite.name + "'" +
          " error='" + error + "'");
      }
      return BuildNeutralNormalPixels(colorTrimPixels);
    }

    var output = new Color32[rawNormalPixels.Length];
    for (var i = 0; i < rawNormalPixels.Length; i++) {
      var source = rawNormalPixels[i];
      output[i] = new Color32(source.r, source.g, source.b, colorTrimPixels[i].a);
    }

    return output;
  }

  void AnalyzeTrimmedSprite(
    LoadedAtlas atlas,
    PixelRect sourceRect,
    out PixelRect trimRect,
    out PixelPoint offsetPx,
    out Color32[] trimmedPixels,
    out bool empty) {
    var minX = sourceRect.width;
    var minY = sourceRect.height;
    var maxX = -1;
    var maxY = -1;
    empty = true;

    for (var localY = 0; localY < sourceRect.height; localY++) {
      for (var localX = 0; localX < sourceRect.width; localX++) {
        var atlasX = sourceRect.x + localX;
        var atlasY = sourceRect.y + localY;
        var color = atlas.pixels[(atlasY * atlas.texture.width) + atlasX];
        if (!IsVisible(color)) continue;
        if (localX < minX) minX = localX;
        if (localY < minY) minY = localY;
        if (localX > maxX) maxX = localX;
        if (localY > maxY) maxY = localY;
        empty = false;
      }
    }

    if (empty) {
      trimRect = new PixelRect(0, 0, 1, 1);
      offsetPx = new PixelPoint(0f, 0f);
      trimmedPixels = new[] { new Color32(0, 0, 0, 0) };
      return;
    }

    trimRect = new PixelRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    offsetPx = new PixelPoint(
      (float)Math.Round((minX + (trimRect.width * 0.5f)) - (sourceRect.width * 0.5f), 3),
      (float)Math.Round((minY + (trimRect.height * 0.5f)) - (sourceRect.height * 0.5f), 3));
    trimmedPixels = CopyTrimmedPixels(atlas.pixels, atlas.texture.width, sourceRect, trimRect);
  }

  Color32[] BuildNormalTrimPixels(
    Color32[] colorTrimPixels,
    PixelRect trimRect,
    PixelRect colorSourceRect,
    LoadedAtlas normalAtlas,
    Sprite normalSprite) {
    if (colorTrimPixels == null || colorTrimPixels.Length <= 0) {
      return new[] { new Color32(128, 128, 255, 0) };
    }

    if (normalAtlas == null || normalSprite == null) {
      return BuildNeutralNormalPixels(colorTrimPixels);
    }

    var normalSourceRect = ToPixelRect(normalSprite.rect);
    if (normalSourceRect.width != colorSourceRect.width || normalSourceRect.height != colorSourceRect.height) {
      Debug.LogWarning(
        "[GearGroupAtlas] Normal sprite rect mismatch." +
        " color='" + colorSourceRect.width + "x" + colorSourceRect.height + "'" +
        " normal='" + normalSourceRect.width + "x" + normalSourceRect.height + "'" +
        " atlas='" + normalAtlas.atlasPath + "'" +
        " sprite='" + normalSprite.name + "'");
      return BuildNeutralNormalPixels(colorTrimPixels);
    }

    var rawNormalPixels = CopyTrimmedPixels(normalAtlas.pixels, normalAtlas.texture.width, normalSourceRect, trimRect);
    if (rawNormalPixels.Length != colorTrimPixels.Length) {
      return BuildNeutralNormalPixels(colorTrimPixels);
    }

    var output = new Color32[rawNormalPixels.Length];
    for (var i = 0; i < rawNormalPixels.Length; i++) {
      var source = rawNormalPixels[i];
      output[i] = new Color32(source.r, source.g, source.b, colorTrimPixels[i].a);
    }

    return output;
  }

  static Color32[] BuildNeutralNormalPixels(Color32[] colorTrimPixels) {
    if (colorTrimPixels == null || colorTrimPixels.Length <= 0) {
      return new[] { new Color32(128, 128, 255, 0) };
    }

    var output = new Color32[colorTrimPixels.Length];
    for (var i = 0; i < colorTrimPixels.Length; i++) {
      output[i] = new Color32(128, 128, 255, colorTrimPixels[i].a);
    }

    return output;
  }

  Color32[] CopyPackedPixels(Color32[] sourcePixels, int atlasWidth, PixelRect packedRect, out string error) {
    error = "";
    if (sourcePixels == null || sourcePixels.Length <= 0) {
      error = "Missing grouped atlas pixels.";
      return null;
    }

    if (atlasWidth <= 0 || packedRect.width <= 0 || packedRect.height <= 0) {
      error = "Invalid grouped atlas packed rect.";
      return null;
    }

    var atlasHeight = sourcePixels.Length / atlasWidth;
    if (packedRect.x < 0 ||
        packedRect.y < 0 ||
        packedRect.x + packedRect.width > atlasWidth ||
        packedRect.y + packedRect.height > atlasHeight) {
      error = "Grouped atlas packed rect exceeds texture bounds.";
      return null;
    }

    return CopyTrimmedPixels(sourcePixels, atlasWidth, packedRect, new PixelRect(0, 0, packedRect.width, packedRect.height));
  }

  static int CountInheritedTrimMetadata(List<PackedSpriteBuildItem> items) {
    if (items == null || items.Count <= 0) {
      return 0;
    }

    var count = 0;
    for (var i = 0; i < items.Count; i++) {
      if (items[i] != null && items[i].inheritedTrimMetadata) {
        count++;
      }
    }

    return count;
  }

  static List<AtlasPage> FilterAndSortPages(List<AtlasPage> pages) {
    var filteredPages = new List<AtlasPage>();
    if (pages == null || pages.Count <= 0) {
      return filteredPages;
    }

    for (var i = 0; i < pages.Count; i++) {
      var page = pages[i];
      if (page == null || page.items == null || page.items.Count <= 0) {
        continue;
      }

      filteredPages.Add(page);
    }

    filteredPages.Sort(CompareAtlasPages);
    return filteredPages;
  }

  static int GetNextPageIndex(List<AtlasPage> pages) {
    if (pages == null || pages.Count <= 0) {
      return 0;
    }

    var maxPageIndex = -1;
    for (var i = 0; i < pages.Count; i++) {
      var page = pages[i];
      if (page == null || page.pageIndex <= maxPageIndex) {
        continue;
      }

      maxPageIndex = page.pageIndex;
    }

    return maxPageIndex + 1;
  }

  static int CompareAtlasPages(AtlasPage left, AtlasPage right) {
    if (ReferenceEquals(left, right)) return 0;
    if (left == null) return -1;
    if (right == null) return 1;
    return left.pageIndex.CompareTo(right.pageIndex);
  }

  static List<string> CollectExistingGroupedMetadataPaths(string fullOutputFolderPath, GroupCandidate candidate) {
    var metadataPaths = new List<string>();
    if (string.IsNullOrWhiteSpace(fullOutputFolderPath) || !Directory.Exists(fullOutputFolderPath)) {
      return metadataPaths;
    }

    var candidatePaths = Directory.GetFiles(fullOutputFolderPath, "*.json", SearchOption.TopDirectoryOnly);
    for (var i = 0; i < candidatePaths.Length; i++) {
      var candidatePath = candidatePaths[i];
      if (TrimmedAtlasExporterWindow.IsEditorMetadataAssetPath(candidatePath)) {
        continue;
      }
      if (!IsExistingGroupedPageMetadataPath(candidatePath, candidate)) {
        continue;
      }

      metadataPaths.Add(candidatePath);
    }

    metadataPaths.Sort(StringComparer.OrdinalIgnoreCase);
    return metadataPaths;
  }

  static List<Sprite> LoadGroupedAtlasSprites(string atlasAssetPath) {
    var assets = AssetDatabase.LoadAllAssetsAtPath(atlasAssetPath);
    var sprites = new List<Sprite>(assets.Length);
    for (var i = 0; i < assets.Length; i++) {
      if (assets[i] is Sprite sprite) {
        sprites.Add(sprite);
      }
    }

    sprites.Sort(CompareGroupedAtlasSprites);
    return sprites;
  }

  static int CompareGroupedAtlasSprites(Sprite left, Sprite right) {
    if (ReferenceEquals(left, right)) return 0;
    if (left == null) return -1;
    if (right == null) return 1;
    return SpriteSliceAddressUtility.NaturalStringComparer.Compare(left.name, right.name);
  }

  bool TryPlaceItemIntoExistingPages(List<AtlasPage> pages, PackedSpriteBuildItem item) {
    if (pages == null || item == null) return false;

    for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++) {
      var page = pages[pageIndex];
      if (page == null) continue;
      if (TryPlaceItemIntoExistingPage(page, item)) {
        return true;
      }
    }

    return false;
  }

  bool TryPlaceItemIntoExistingPage(AtlasPage page, PackedSpriteBuildItem item) {
    if (page == null || item == null) return false;

    var candidateXs = new SortedSet<int> { padding };
    var candidateYs = new SortedSet<int> { padding };
    for (var i = 0; i < page.items.Count; i++) {
      var existingItem = page.items[i];
      if (existingItem == null) continue;
      candidateXs.Add(existingItem.packedRect.x);
      candidateXs.Add(existingItem.packedRect.x + existingItem.packedRect.width + padding);
      candidateYs.Add(existingItem.packedRect.y);
      candidateYs.Add(existingItem.packedRect.y + existingItem.packedRect.height + padding);
    }

    foreach (var y in candidateYs) {
      foreach (var x in candidateXs) {
        if (!CanPlaceItemAt(page, item, x, y)) continue;
        item.packedRect = new PixelRect(x, y, item.Width, item.Height);
        item.pageIndex = page.pageIndex;
        page.items.Add(item);
        return true;
      }
    }

    return false;
  }

  bool CanPlaceItemAt(AtlasPage page, PackedSpriteBuildItem item, int x, int y) {
    if (page == null || item == null) return false;
    if (x < padding || y < padding) return false;
    if (x + item.Width + padding > maxAtlasSize) return false;
    if (y + item.Height + padding > maxAtlasSize) return false;

    var newOccupiedRect = new PixelRect(x, y, item.Width + padding, item.Height + padding);
    for (var i = 0; i < page.items.Count; i++) {
      var existingItem = page.items[i];
      if (existingItem == null) continue;
      var occupiedRect = new PixelRect(
        existingItem.packedRect.x,
        existingItem.packedRect.y,
        existingItem.packedRect.width + padding,
        existingItem.packedRect.height + padding);
      if (DoPixelRectsOverlap(occupiedRect, newOccupiedRect)) {
        return false;
      }
    }

    return true;
  }

  static bool DoPixelRectsOverlap(PixelRect left, PixelRect right) {
    return left.x < right.x + right.width &&
           left.x + left.width > right.x &&
           left.y < right.y + right.height &&
           left.y + left.height > right.y;
  }

  bool TryPackItemsIntoPages(List<PackedSpriteBuildItem> items, out List<AtlasPage> pages, out string error) {
    pages = new List<AtlasPage>();
    error = "";
    if (items == null || items.Count <= 0) {
      error = "No grouped sprite items were available for packing.";
      return false;
    }

    var ordered = BuildGroupedPackSequence(items);

    var currentPage = new AtlasPage { pageIndex = 0 };
    var x = padding;
    var y = padding;
    var rowHeight = 0;
    var usedWidth = 0;

    for (var i = 0; i < ordered.Count; i++) {
      var item = ordered[i];
      if (currentPage.items.Count > 0 && currentPage.items.Count >= maxSpritesPerAtlasPage) {
        CommitPage(pages, ref currentPage, ref x, ref y, ref rowHeight, ref usedWidth);
      }

      if (item.Width + (padding * 2) > maxAtlasSize || item.Height + (padding * 2) > maxAtlasSize) {
        error = "Sprite '" + item.outputSpriteName + "' exceeds the configured max atlas size " + maxAtlasSize + ".";
        return false;
      }

      if (x > padding && x + item.Width + padding > maxAtlasSize) {
        y += rowHeight + padding;
        x = padding;
        rowHeight = 0;
      }

      if (y + item.Height + padding > maxAtlasSize) {
        CommitPage(pages, ref currentPage, ref x, ref y, ref rowHeight, ref usedWidth);
      }

      if (y + item.Height + padding > maxAtlasSize) {
        error = "Sprite '" + item.outputSpriteName + "' could not fit inside a fresh atlas page.";
        return false;
      }

      item.pageIndex = currentPage.pageIndex;
      item.packedRect = new PixelRect(x, y, item.Width, item.Height);
      currentPage.items.Add(item);

      x += item.Width + padding;
      if (item.Height > rowHeight) rowHeight = item.Height;
      if (x > usedWidth) usedWidth = x;
    }

    FinalizePage(currentPage, usedWidth, y, rowHeight);
    pages.Add(currentPage);
    return true;
  }

  void CommitPage(
    List<AtlasPage> pages,
    ref AtlasPage currentPage,
    ref int x,
    ref int y,
    ref int rowHeight,
    ref int usedWidth) {
    if (pages == null || currentPage == null) return;

    FinalizePage(currentPage, usedWidth, y, rowHeight);
    pages.Add(currentPage);
    currentPage = new AtlasPage { pageIndex = pages.Count };
    x = padding;
    y = padding;
    rowHeight = 0;
    usedWidth = 0;
  }

  void FinalizePage(AtlasPage page, int usedWidth, int y, int rowHeight) {
    if (page == null) return;
    page.width = Mathf.Max(1, usedWidth);
    page.height = Mathf.Max(1, y + rowHeight + padding);
  }

  void RefreshPageBounds(AtlasPage page, bool preserveExistingSize) {
    if (page == null) return;

    var minWidth = preserveExistingSize ? Mathf.Max(1, page.width) : 1;
    var minHeight = preserveExistingSize ? Mathf.Max(1, page.height) : 1;
    var usedWidth = minWidth;
    var usedHeight = minHeight;

    for (var i = 0; i < page.items.Count; i++) {
      var item = page.items[i];
      if (item == null) continue;
      usedWidth = Math.Max(usedWidth, item.packedRect.x + item.packedRect.width + padding);
      usedHeight = Math.Max(usedHeight, item.packedRect.y + item.packedRect.height + padding);
    }

    page.width = Mathf.Clamp(usedWidth, 1, maxAtlasSize);
    page.height = Mathf.Clamp(usedHeight, 1, maxAtlasSize);
  }

  bool TryWritePageTexture(string atlasAssetPath, AtlasPage page, bool isNormalAtlas, out string error) {
    error = "";
    var texture = BuildPageTexture(page, isNormalAtlas);
    try {
      var fullPath = Path.GetFullPath(atlasAssetPath);
      Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? "");
      File.WriteAllBytes(fullPath, texture.EncodeToPNG());
      return true;
    }
    catch (Exception ex) {
      error = ex.Message;
      return false;
    }
    finally {
      DestroyImmediate(texture);
    }
  }

  Texture2D BuildPageTexture(AtlasPage page, bool isNormalAtlas) {
    var texture = new Texture2D(page.width, page.height, TextureFormat.RGBA32, false);
    texture.filterMode = FilterMode.Point;
    texture.wrapMode = TextureWrapMode.Clamp;
    texture.SetPixels32(BuildPagePixels(page, isNormalAtlas));
    texture.Apply(false, false);
    return texture;
  }

  static Color32[] BuildPagePixels(AtlasPage page, bool isNormalAtlas) {
    var width = Math.Max(1, page?.width ?? 1);
    var height = Math.Max(1, page?.height ?? 1);
    var background = isNormalAtlas ? new Color32(128, 128, 255, 0) : new Color32(0, 0, 0, 0);
    var pixels = new Color32[width * height];
    for (var i = 0; i < pixels.Length; i++) {
      pixels[i] = background;
    }

    if (page?.items == null || page.items.Count <= 0) {
      return pixels;
    }

    for (var itemIndex = 0; itemIndex < page.items.Count; itemIndex++) {
      var item = page.items[itemIndex];
      if (item == null) {
        continue;
      }

      var packedRect = item.packedRect;
      var itemPixels = isNormalAtlas ? item.normalPixels : item.colorPixels;
      if (itemPixels == null || packedRect.width <= 0 || packedRect.height <= 0) {
        continue;
      }

      for (var row = 0; row < packedRect.height; row++) {
        var srcIndex = row * packedRect.width;
        var dstIndex = ((packedRect.y + row) * width) + packedRect.x;
        Array.Copy(itemPixels, srcIndex, pixels, dstIndex, packedRect.width);
      }
    }

    return pixels;
  }

  static Sprite FindMatchingSprite(LoadedAtlas atlas, string spriteName) {
    if (atlas == null || string.IsNullOrWhiteSpace(spriteName)) return null;
    if (atlas.spritesByName.TryGetValue(spriteName, out var exact) && exact != null) {
      return exact;
    }

    foreach (var pair in atlas.spritesByName) {
      if (!SpriteSliceAddressUtility.HasEquivalentNumericLabel(pair.Key, spriteName)) continue;
      return pair.Value;
    }

    return null;
  }

  static PixelRect ToPixelRect(Rect rect) {
    return new PixelRect(
      Mathf.RoundToInt(rect.x),
      Mathf.RoundToInt(rect.y),
      Mathf.RoundToInt(rect.width),
      Mathf.RoundToInt(rect.height));
  }

  bool IsVisible(Color32 color) {
    if (color.a <= alphaThreshold) return false;
    if (!treatNearWhiteAsEmpty) return true;
    return color.r < nearWhiteThreshold || color.g < nearWhiteThreshold || color.b < nearWhiteThreshold;
  }

  static Color32[] CopyTrimmedPixels(Color32[] sourcePixels, int atlasWidth, PixelRect sourceRect, PixelRect trimRect) {
    var trimmedPixels = new Color32[Math.Max(1, trimRect.width * trimRect.height)];
    for (var y = 0; y < trimRect.height; y++) {
      var srcY = sourceRect.y + trimRect.y + y;
      var srcIndex = (srcY * atlasWidth) + sourceRect.x + trimRect.x;
      var dstIndex = y * trimRect.width;
      Array.Copy(sourcePixels, srcIndex, trimmedPixels, dstIndex, trimRect.width);
    }

    return trimmedPixels;
  }

  static int EnsureUniqueOutputSpriteNames(List<PackedSpriteBuildItem> items) {
    if (items == null || items.Count <= 1) return 0;

    var duplicateGroups = new Dictionary<string, List<PackedSpriteBuildItem>>(StringComparer.Ordinal);
    var reservedNames = new HashSet<string>(StringComparer.Ordinal);
    for (var i = 0; i < items.Count; i++) {
      var item = items[i];
      if (item == null || string.IsNullOrWhiteSpace(item.outputSpriteName)) {
        continue;
      }

      if (!duplicateGroups.TryGetValue(item.outputSpriteName, out var group)) {
        group = new List<PackedSpriteBuildItem>();
        duplicateGroups[item.outputSpriteName] = group;
      }

      group.Add(item);
    }

    var duplicateBaseNames = new List<string>();
    foreach (var pair in duplicateGroups) {
      if (pair.Value.Count > 1) {
        duplicateBaseNames.Add(pair.Key);
        continue;
      }

      reservedNames.Add(pair.Key);
    }
    if (duplicateBaseNames.Count <= 0) return 0;

    duplicateBaseNames.Sort(SpriteSliceAddressUtility.NaturalStringComparer);

    var renamedCount = 0;
    for (var groupIndex = 0; groupIndex < duplicateBaseNames.Count; groupIndex++) {
      var groupKey = duplicateBaseNames[groupIndex];
      var orderedItems = duplicateGroups[groupKey];
      orderedItems.Sort(ComparePackedSpriteBuildItemsForRename);

      for (var itemIndex = 0; itemIndex < orderedItems.Count; itemIndex++) {
        var item = orderedItems[itemIndex];
        if (item == null) continue;

        var candidateName = groupKey + "__" + BuildOutputSpriteDisambiguationSuffix(item);
        if (reservedNames.Contains(candidateName)) {
          var suffixIndex = 2;
          var disambiguatedBaseName = candidateName;
          while (reservedNames.Contains(candidateName)) {
            candidateName = disambiguatedBaseName + "_" + suffixIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
            suffixIndex++;
          }
        }

        item.outputSpriteName = candidateName;
        reservedNames.Add(candidateName);
        renamedCount++;
      }
    }

    return renamedCount;
  }

  static int ComparePackedSpriteBuildItemsForRename(PackedSpriteBuildItem left, PackedSpriteBuildItem right) {
    if (ReferenceEquals(left, right)) return 0;
    if (left == null) return -1;
    if (right == null) return 1;

    var atlasComparison = SpriteSliceAddressUtility.NaturalStringComparer.Compare(left.colorSourceAtlasPath, right.colorSourceAtlasPath);
    if (atlasComparison != 0) return atlasComparison;

    return SpriteSliceAddressUtility.NaturalStringComparer.Compare(left.sourceSpriteName, right.sourceSpriteName);
  }

  static string BuildOutputSpriteDisambiguationSuffix(PackedSpriteBuildItem item) {
    var normalizedAtlasPath = NormalizePath(item?.colorSourceAtlasPath);
    if (!string.IsNullOrWhiteSpace(normalizedAtlasPath)) {
      var guid = AssetDatabase.AssetPathToGUID(normalizedAtlasPath);
      if (!string.IsNullOrWhiteSpace(guid)) {
        return guid.Substring(0, Math.Min(8, guid.Length));
      }

      var fileBase = Path.GetFileNameWithoutExtension(normalizedAtlasPath);
      var sanitizedFileBase = SanitizeSpriteNameToken(fileBase);
      if (!string.IsNullOrWhiteSpace(sanitizedFileBase)) {
        return sanitizedFileBase;
      }
    }

    return "dup";
  }

  static string SanitizeSpriteNameToken(string value) {
    if (string.IsNullOrWhiteSpace(value)) return "";

    var buffer = new char[value.Length];
    var count = 0;
    for (var i = 0; i < value.Length; i++) {
      var c = value[i];
      if (char.IsLetterOrDigit(c)) {
        buffer[count++] = c;
        continue;
      }

      if (count > 0 && buffer[count - 1] == '_') continue;
      buffer[count++] = '_';
    }

    return new string(buffer, 0, count).Trim('_');
  }

  void CleanupStaleCandidateOutputs(string outputFolderPath, GroupCandidate candidate, List<AtlasPage> pages, bool includeNormalAtlases) {
    if (string.IsNullOrWhiteSpace(outputFolderPath) || candidate == null || pages == null) return;

    var cleanupPlan = new CleanupPlan {
      folderPath = outputFolderPath,
      filePrefix = HasExplicitOutputName(candidate) ? BuildOutputFilePrefix(candidate) : "",
      useNumberedPageNames = !HasExplicitOutputName(candidate),
      isSkinLibrary = IsSkinCandidate(candidate)
    };

    for (var i = 0; i < pages.Count; i++) {
      var page = pages[i];
      if (page == null) continue;

      if (!string.IsNullOrWhiteSpace(page.colorAtlasPath)) {
        cleanupPlan.keepAssetPaths.Add(page.colorAtlasPath);
        AddMetadataAssetPaths(cleanupPlan.keepAssetPaths, page.colorAtlasPath);
      }

      if (!includeNormalAtlases || string.IsNullOrWhiteSpace(page.normalAtlasPath)) continue;
      cleanupPlan.keepAssetPaths.Add(page.normalAtlasPath);
      AddMetadataAssetPaths(cleanupPlan.keepAssetPaths, page.normalAtlasPath);
    }

    var deletedCount = CleanupStaleOutputs(new List<CleanupPlan> { cleanupPlan });
    if (deletedCount > 0) {
      Debug.Log(
        "[GearGroupAtlas] Deleted stale output assets before overwrite." +
        " group='" + BuildCandidateLabel(candidate) + "'" +
        " deleted_assets=" + deletedCount);
    }
  }
}
#endif
