using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static partial class TextureResidencyCache {
  static bool HasAnyLoadedSprite(IList<Sprite> sprites) {
    return TryGetFirstLoadedSprite(sprites, out _);
  }

  static bool TryGetFirstLoadedSprite(IList<Sprite> sprites, out Sprite sprite) {
    sprite = null;
    if (sprites == null || sprites.Count <= 0) return false;
    for (var i = 0; i < sprites.Count; i++) {
      if (sprites[i] == null) continue;
      sprite = sprites[i];
      return true;
    }
    return false;
  }

  static bool TryGetSpriteFromReadyEntry(CacheEntry entry, string spriteName, out Sprite sprite) {
    return TryGetSpriteFromEntry(entry, spriteName, out sprite);
  }

  static bool HasLoadedAtlasSubassetSet(CacheEntry entry) {
    if (entry == null) return false;
    if (!string.Equals(entry.requestStrategy, "atlas_backed", StringComparison.Ordinal)) return false;
    if (!entry.handle.IsValid()) return false;
    return CountLoadedSprites(entry.handle.Result) > 1;
  }

  static bool ShouldLogAtlasNameDiagnostics(string address) {
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    if (!SpriteStreamingRuntimeSettings.EnableDiagnostics) return false;
    if (string.IsNullOrWhiteSpace(address)) return false;

    var normalizedAddress = NormalizeAddress(address);
    return normalizedAddress.IndexOf("/MainMenu/", StringComparison.OrdinalIgnoreCase) >= 0 ||
           normalizedAddress.IndexOf("/Fonts/", StringComparison.OrdinalIgnoreCase) >= 0;
  }

  static bool TryCollectExpectedAtlasSpriteNames(string atlasAddress, List<string> spriteNames, int maxCount = 512) {
    if (spriteNames == null) return false;
    spriteNames.Clear();
    if (string.IsNullOrWhiteSpace(atlasAddress)) return false;

    atlasSiblingAddressScratch.Clear();
    if (!SpriteRuntimeResolver.TryCollectAtlasSiblingAddresses(atlasAddress, atlasSiblingAddressScratch, Math.Max(maxCount, 1))) {
      atlasSiblingAddressScratch.Clear();
      return false;
    }

    for (var i = 0; i < atlasSiblingAddressScratch.Count; i++) {
      var siblingAddress = atlasSiblingAddressScratch[i];
      if (!SpriteSliceAddressUtility.TryParseSliceAddress(siblingAddress, out _, out var spriteName)) continue;
      if (string.IsNullOrWhiteSpace(spriteName)) continue;
      spriteNames.Add(spriteName.Trim());
    }

    atlasSiblingAddressScratch.Clear();
    return spriteNames.Count > 0;
  }

  static int RegisterSpriteMapKey(CacheEntry entry, string key, Sprite sprite) {
    if (entry == null || sprite == null || string.IsNullOrWhiteSpace(key)) return 0;
    var normalizedKey = key.Trim();
    if (string.IsNullOrWhiteSpace(normalizedKey)) return 0;
    if (entry.spritesByName.TryGetValue(normalizedKey, out var existing) && existing == sprite) return 0;
    entry.spritesByName[normalizedKey] = sprite;
    return 1;
  }

  static string DescribeSpriteNames(IList<Sprite> loadedSprites, int maxCount = 24) {
    if (loadedSprites == null || loadedSprites.Count <= 0) return "";
    var limit = Math.Min(Math.Max(maxCount, 1), loadedSprites.Count);
    var builder = new System.Text.StringBuilder();
    for (var i = 0; i < limit; i++) {
      var sprite = loadedSprites[i];
      if (builder.Length > 0) builder.Append(",");
      builder.Append(sprite != null ? (sprite.name ?? "") : "<null>");
    }
    if (loadedSprites.Count > limit) builder.Append(",...");
    return builder.ToString();
  }

  static string DescribeExpectedSpriteNames(string atlasAddress, int maxCount = 24) {
    if (!TryCollectExpectedAtlasSpriteNames(atlasAddress, atlasSiblingSpriteNameScratch, maxCount)) return "";
    var limit = Math.Min(Math.Max(maxCount, 1), atlasSiblingSpriteNameScratch.Count);
    var builder = new System.Text.StringBuilder();
    for (var i = 0; i < limit; i++) {
      if (builder.Length > 0) builder.Append(",");
      builder.Append(atlasSiblingSpriteNameScratch[i]);
    }
    if (atlasSiblingSpriteNameScratch.Count > limit) builder.Append(",...");
    atlasSiblingSpriteNameScratch.Clear();
    return builder.ToString();
  }

  static void LogAtlasSpriteMapBuildDiagnostics(CacheEntry entry, IList<Sprite> loadedSprites, string mapSource, int aliasCount) {
    if (entry == null) return;
    if (!ShouldLogAtlasNameDiagnostics(entry.address)) return;

    var requestedAddress = string.IsNullOrWhiteSpace(entry.lastRequestedAddress) ? entry.address : entry.lastRequestedAddress;
    Debug.Log(
      "[TextureResidencyCache] Atlas sprite map build" +
      " requested='" + (requestedAddress ?? "") + "'" +
      " atlas='" + (entry.address ?? "") + "'" +
      " expected_label='" + (entry.requestedSpriteNameHint ?? "") + "'" +
      " map_source='" + (mapSource ?? "") + "'" +
      " alias_count=" + aliasCount +
      " loaded_names='" + DescribeSpriteNames(loadedSprites) + "'" +
      " expected_names='" + DescribeExpectedSpriteNames(entry.address) + "'"
    );
  }

  static int CountLoadedSprites(IList<Sprite> sprites) {
    if (sprites == null || sprites.Count <= 0) return 0;
    var count = 0;
    for (var i = 0; i < sprites.Count; i++) {
      if (sprites[i] != null) count++;
    }
    return count;
  }

  static bool ShouldUseAtlasOwnerSubassetLoad(CacheEntry entry) {
    if (entry == null) return false;
    if (!string.Equals(entry.requestStrategy, "atlas_backed", StringComparison.Ordinal)) return false;
    return !entry.atlasFallbackToDirect;
  }

  static string ResolveDirectSpriteLoadAddress(CacheEntry entry) {
    if (entry == null) return "";
    return entry.address;
  }

  static void LogIncompleteAtlasSpriteMap(CacheEntry entry, int expectedSiblingSliceCount, int resourceLocationCount, int loadedSpriteCount) {
    if (entry == null || !entry.isSuccess) return;
    if (expectedSiblingSliceCount <= 0) return;
    if (!entry.spriteMapMaterialized) return;
    if (entry.spritesByName.Count > 1) return;
    if (!incompleteAtlasLoadWarnings.Add(entry.address)) return;

    Debug.LogWarning(
      "[TextureResidencyCache] Atlas load completed with an incomplete sprite map" +
      " address='" + entry.address + "'" +
      " expected_slices=" + expectedSiblingSliceCount +
      " location_count=" + resourceLocationCount +
      " loaded_count=" + loadedSpriteCount +
      " mapped_count=" + entry.spritesByName.Count
    );
  }

#if UNITY_EDITOR
  static bool ShouldLogVerboseEditorSupplementDiagnostics() {
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    if (!SpriteStreamingRuntimeSettings.EnableDiagnostics) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }

  static void EnqueueEditorAtlasSpriteMapSupplement(CacheEntry entry, int loadedSpriteCount) {
    if (entry == null) return;
    if (entry.spritesByName.Count > 1) return;
    if (string.IsNullOrWhiteSpace(entry.address)) return;
    if (loadedSpriteCount > 1) return;
    if (entry.editorAtlasSupplementPending) return;
    entry.editorAtlasSupplementPending = true;
    pendingEditorAtlasSupplementQueue.Enqueue(entry);
  }

  static int ResolveEditorAtlasSupplementBudgetPerFrame() {
    if (ShouldUseStrictSerialLoadingDebounce()) {
      return StrictSerialLoadingBudgetPerFrame;
    }
    if (IsProtectedLoadingScreenStreamingContextActive()) {
      return EditorAtlasSupplementOverlayBudgetPerFrame;
    }
    if (queuedEntryCount > 0 || inFlightLoads > 0 || deferredRequests.Count > 0) {
      return EditorAtlasSupplementLoadingBudgetPerFrame;
    }
    return EditorAtlasSupplementGameplayBudgetPerFrame;
  }

  static void ProcessPendingEditorAtlasSupplements(float deadlineAt) {
    if (ShouldUseOverlayExactSliceSupplement()) return;
    var budget = Math.Max(ResolveEditorAtlasSupplementBudgetPerFrame(), 1);
    var processed = 0;
    while (processed < budget && pendingEditorAtlasSupplementQueue.Count > 0) {
      if (!HasCompletionFollowupBudgetRemaining(deadlineAt)) break;
      var entry = pendingEditorAtlasSupplementQueue.Dequeue();
      if (entry == null) continue;
      entry.editorAtlasSupplementPending = false;
      if (entry.isEvicted || !entry.isDone || !entry.isSuccess) continue;
      if (entry.spritesByName.Count > 1) continue;
      TrySupplementEntrySpriteMapFromEditor(entry);
      processed++;
    }
  }

  static void TrySupplementEntrySpriteMapFromEditor(CacheEntry entry) {
    if (entry == null) return;
    if (entry.spritesByName.Count > 1) return;
    if (string.IsNullOrWhiteSpace(entry.address)) return;

    entry.editorAtlasSupplementAttempted = true;
    if (!TryLoadEditorImportedAtlasSprites(entry.address, out var importedSprites)) return;
    var addedSpriteCount = MergeImportedSpritesIntoEntry(entry, importedSprites);

    if (entry.spritesByName.Count <= 1) return;
    if (!editorAtlasSupplementWarnings.Add(entry.address)) return;
    if (!ShouldLogVerboseEditorSupplementDiagnostics()) return;

    Debug.LogWarning(
      "[TextureResidencyCache] Supplemented editor atlas sprite map address='" + entry.address +
      "' addressables_count=1" +
      " editor_count=" + entry.spritesByName.Count +
      " added=" + addedSpriteCount
    );
  }

  static bool TryGetSpriteFromEntryWithEditorSupplement(CacheEntry entry, string spriteName, out Sprite sprite) {
    sprite = null;
    if (entry == null || string.IsNullOrWhiteSpace(spriteName)) return false;
    if (entry.editorAtlasSupplementAttempted) return false;

    entry.editorAtlasSupplementAttempted = true;
    var mappedBefore = entry.spritesByName.Count;
    TrySupplementEntrySpriteMapFromEditor(entry);
    var resolved = TryGetSpriteFromEntry(entry, spriteName, out sprite);
    if (resolved) {
      if (ShouldLogVerboseEditorSupplementDiagnostics()) {
        Debug.LogWarning(
          "[TextureResidencyCache] On-demand editor atlas supplement resolved sprite" +
          " address='" + entry.address + "'" +
          " sprite='" + spriteName.Trim() + "'" +
          " mapped_before=" + mappedBefore +
          " mapped_after=" + entry.spritesByName.Count
        );
      }
      return true;
    }

    if (ShouldLogVerboseEditorSupplementDiagnostics()) {
      Debug.LogWarning(
        "[TextureResidencyCache] On-demand editor atlas supplement did not resolve sprite" +
        " address='" + entry.address + "'" +
        " sprite='" + spriteName.Trim() + "'" +
        " mapped_before=" + mappedBefore +
        " mapped_after=" + entry.spritesByName.Count
      );
    }

    return false;
  }
#endif

  static bool ShouldUseOverlayExactSliceSupplement() {
    if (!Application.isEditor) return false;
    return IsProtectedLoadingScreenStreamingContextActive();
  }

  static void QueueExactSliceSupplementRequest(CacheEntry entry, string sliceOrAtlasAddress, string reason) {
    if (entry == null || entry.isEvicted || !entry.isDone || !entry.isSuccess) return;
    if (string.IsNullOrWhiteSpace(sliceOrAtlasAddress)) return;
    if (!SpriteSliceAddressUtility.TryParseSliceAddress(sliceOrAtlasAddress, out _, out _)) return;

    var normalizedSliceAddress = sliceOrAtlasAddress.Trim();
    if (entry.pendingExactSliceSupplementAddresses.Contains(normalizedSliceAddress)) return;
    if (entry.failedExactSliceSupplementAddresses.Contains(normalizedSliceAddress)) return;

    entry.pendingExactSliceSupplementAddresses.Add(normalizedSliceAddress);
    pendingExactSliceSupplementQueue.Enqueue(new ExactSliceSupplementRequest(entry, normalizedSliceAddress));
    if (!ShouldLogAtlasNameDiagnostics(entry.address)) return;
    Debug.Log(
      "[TextureResidencyCache] Queue exact slice supplement" +
      " requested='" + normalizedSliceAddress + "'" +
      " atlas='" + (entry.address ?? "") + "'" +
      " reason='" + (reason ?? "") + "'" +
      " sprite_map_materialized=" + (entry.spriteMapMaterialized ? 1 : 0) +
      " deferred_map=" + (entry.deferredSpriteMapMaterialization ? 1 : 0) +
      " load_result_count=" + ((entry.handle.IsValid() && entry.handle.Result != null) ? entry.handle.Result.Count : 0)
    );
  }

  static void EnsureOverlayExactSliceSupplement(CacheEntry entry, string sliceOrAtlasAddress) {
    if (!ShouldUseOverlayExactSliceSupplement()) return;
    QueueExactSliceSupplementRequest(entry, sliceOrAtlasAddress, "editor_overlay");
  }

  static bool ShouldUseRuntimeExactSliceSupplement(CacheEntry entry, string requestedAddress) {
    if (Application.isEditor) return false;
    if (entry == null || entry.isEvicted || !entry.isDone || !entry.isSuccess) return false;
    if (string.IsNullOrWhiteSpace(requestedAddress)) return false;
    if (!string.Equals(entry.requestStrategy, "atlas_backed", StringComparison.Ordinal)) return false;
    if (!SpriteSliceAddressUtility.TryParseSliceAddress(requestedAddress, out _, out var spriteName)) return false;
    if (string.IsNullOrWhiteSpace(spriteName)) return false;
    if (TryGetSpriteFromEntryWithoutMaterialization(entry, spriteName, out _)) return false;
    if (entry.spriteMapMaterialized) return true;
    if (!CanMaterializeEntrySpriteMapOnDemand(entry)) return true;
    if (entry.handle.IsValid() && entry.handle.Result != null && entry.handle.Result.Count <= 1) return true;
    return false;
  }

  static void EnsureRequestedSliceSupplement(CacheEntry entry, string requestedAddress) {
    if (entry == null || string.IsNullOrWhiteSpace(requestedAddress)) return;
    if (ShouldUseOverlayExactSliceSupplement()) {
      if (!entry.editorAtlasSupplementPending) return;
      QueueExactSliceSupplementRequest(entry, requestedAddress, "editor_overlay");
      return;
    }

    if (!ShouldUseRuntimeExactSliceSupplement(entry, requestedAddress)) return;
    QueueExactSliceSupplementRequest(entry, requestedAddress, "runtime_exact_slice");
  }

  static int ResolveExactSliceSupplementBudgetPerFrame() {
    if (ShouldUseStrictSerialLoadingDebounce()) {
      return StrictSerialLoadingBudgetPerFrame;
    }
#if UNITY_EDITOR
    if (ShouldUseOverlayExactSliceSupplement()) {
      return ExactSliceSupplementOverlayBudgetPerFrame;
    }
    if (pendingExactSliceSupplementQueue.Count > ExactSliceSupplementGameplayBudgetPerFrame ||
        queuedEntryCount > 0 ||
        inFlightLoads > 0 ||
        deferredRequests.Count > 0) {
      return ExactSliceSupplementLoadingBudgetPerFrame;
    }
    return ExactSliceSupplementGameplayBudgetPerFrame;
#else
    return 4;
#endif
  }

  static bool SpriteMatchesRequestedSlice(Sprite sprite, string requestedSpriteName) {
    if (sprite == null || string.IsNullOrWhiteSpace(requestedSpriteName)) return false;
    if (string.Equals(sprite.name, requestedSpriteName, StringComparison.Ordinal)) return true;
    if (!SpriteSliceAddressUtility.CanUseNumericLabelFallback(requestedSpriteName)) return false;
    return SpriteSliceAddressUtility.HasEquivalentNumericLabel(sprite.name, requestedSpriteName);
  }

  static void LogExactSliceSupplementMismatchOnce(CacheEntry entry, string sliceAddress, string expectedName, Sprite sprite) {
    var key = "exact_slice_mismatch|" + (sliceAddress ?? "") + "|" + (sprite != null ? sprite.name : "");
    if (!incompleteAtlasLoadWarnings.Add(key)) return;
    Debug.LogWarning(
      "[TextureResidencyCache] Exact slice supplement mismatch" +
      " atlas='" + (entry != null ? entry.address : "") + "'" +
      " requested='" + (sliceAddress ?? "") + "'" +
      " expected='" + (expectedName ?? "") + "'" +
      " loaded='" + (sprite != null ? (sprite.name ?? "") : "") + "'"
    );
  }


}
