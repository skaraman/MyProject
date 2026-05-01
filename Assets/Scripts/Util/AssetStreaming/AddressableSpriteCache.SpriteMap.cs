#if false
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AddressableSpriteCacheAssetStreaming {

public static partial class TextureResidencyCache {

  static void PopulateEntrySpriteMap(CacheEntry entry, IList<Sprite> loadedSprites) {
    if (entry == null || loadedSprites == null) return;
    var preferredPrimary = entry.primarySprite;
    var preferredPrimaryName = preferredPrimary?.name ?? "";
    entry.spritesByName.Clear();
    entry.primarySprite = null;
    entry.spriteMapMaterialized = false;
    entry.deferredSpriteMapMaterialization = false;
    entry.editorAtlasSupplementPending = false;
    entry.editorAtlasSupplementAttempted = false;
    
    Sprite fallbackPrimary = null;
    foreach (var sprite in loadedSprites) {
      if (sprite == null) continue;
      if (fallbackPrimary == null) fallbackPrimary = sprite;
      if (entry.primarySprite == null && preferredPrimary != null &&
          (ReferenceEquals(preferredPrimary, sprite) ||
           (!string.IsNullOrWhiteSpace(preferredPrimaryName) &&
            string.Equals(preferredPrimaryName, sprite.name, StringComparison.Ordinal)))) {
        entry.primarySprite = sprite;
      }
      if (!string.IsNullOrWhiteSpace(sprite.name)) {
        entry.spritesByName[sprite.name] = sprite;
      }
    }
    
    if (entry.primarySprite == null) entry.primarySprite = fallbackPrimary;
    var aliasCount = RegisterSpriteAliasesFromExpectedOrder(entry, loadedSprites);
    var loadedSpriteCount = CountLoadedSprites(loadedSprites);
    entry.spriteMapMaterialized = ShouldMarkSpriteMapMaterialized(entry, loadedSpriteCount);
    entry.deferredSpriteMapMaterialization = !entry.spriteMapMaterialized && CanMaterializeEntrySpriteMapOnDemand(entry);
    LogAtlasSpriteMapBuildDiagnostics(entry, loadedSprites, aliasCount > 0 ? "runtime_index_aliases+sprite_names" : "sprite_names", aliasCount);
#if UNITY_EDITOR
    if (!IsGroupedGeneratedAtlasSurrogateAddress(entry.address)) {
      EnqueueEditorAtlasSpriteMapSupplement(entry, loadedSprites.Count);
    }
#endif
  }

  static int RegisterSpriteAliasesFromExpectedOrder(CacheEntry entry, IList<Sprite> loadedSprites) {
    if (entry == null || loadedSprites == null || loadedSprites.Count <= 0) return 0;
    if (entry.requestStrategy != "atlas_backed") return 0;
    if (!TryCollectExpectedAtlasSpriteNames(entry.address, atlasSiblingSpriteNameScratch, loadedSprites.Count)) return 0;
    
    var aliasCount = Math.Min(atlasSiblingSpriteNameScratch.Count, loadedSprites.Count);
    var added = 0;
    for (var i = 0; i < aliasCount; i++) {
      var sprite = loadedSprites[i];
      if (sprite == null) continue;
      added += RegisterSpriteMapKey(entry, atlasSiblingSpriteNameScratch[i], sprite);
    }
    atlasSiblingSpriteNameScratch.Clear();
    return added;
  }

  static int RegisterSpriteMapKey(CacheEntry entry, string key, Sprite sprite) {
    if (entry == null || sprite == null || string.IsNullOrWhiteSpace(key)) return 0;
    var normalizedKey = key.Trim();
    if (string.IsNullOrWhiteSpace(normalizedKey)) return 0;
    if (entry.spritesByName.TryGetValue(normalizedKey, out var existing) && existing == sprite) return 0;
    entry.spritesByName[normalizedKey] = sprite;
    return 1;
  }

  static bool TryCollectExpectedAtlasSpriteNames(string atlasAddress, List<string> spriteNames, int maxCount = 512) {
    if (spriteNames == null || string.IsNullOrWhiteSpace(atlasAddress)) return false;
    spriteNames.Clear();
    atlasSiblingAddressScratch.Clear();
    if (!SpriteRuntimeResolver.TryCollectAtlasSiblingAddresses(atlasAddress, atlasSiblingAddressScratch, Math.Max(maxCount, 1))) {
      atlasSiblingAddressScratch.Clear();
      return false;
    }
    foreach (var siblingAddress in atlasSiblingAddressScratch) {
      if (!SpriteSliceAddressUtility.TryParseSliceAddress(siblingAddress, out _, out var spriteName)) continue;
      if (!string.IsNullOrWhiteSpace(spriteName)) spriteNames.Add(spriteName.Trim());
    }
    atlasSiblingAddressScratch.Clear();
    return spriteNames.Count > 0;
  }

  static bool TryGetSpriteFromEntry(CacheEntry entry, string spriteName, out Sprite sprite) {
    sprite = null;
    if (entry == null || !entry.isDone || !entry.isSuccess) return false;
    if (string.IsNullOrWhiteSpace(spriteName)) {
      sprite = entry.primarySprite;
      return sprite != null;
    }
    var normalizedName = spriteName.Trim();
    if (entry.spritesByName.TryGetValue(normalizedName, out sprite) && sprite != null) return true;
    if (!entry.spriteMapMaterialized && TryEnsureEntrySpriteMapMaterialized(entry)) {
      if (entry.spritesByName.TryGetValue(normalizedName, out sprite) && sprite != null) return true;
    }
    if (!SpriteSliceAddressUtility.TryExtractNumericLabelValue(normalizedName, out var numericLabelValue)) return false;
    return TryGetSpriteByNumericLabel(entry, numericLabelValue, out sprite);
  }

  static bool TryGetSpriteByNumericLabel(CacheEntry entry, string numericLabelValue, out Sprite sprite) {
    sprite = null;
    if (entry == null || string.IsNullOrWhiteSpace(numericLabelValue)) return false;
    if (entry.spritesByName == null || entry.spritesByName.Count <= 0) {
      if (!TryEnsureEntrySpriteMapMaterialized(entry)) return false;
    }
    Sprite match = null;
    foreach (var pair in entry.spritesByName) {
      if (!SpriteSliceAddressUtility.TryExtractNumericLabelValue(pair.Key, out var candidateNumericValue)) continue;
      if (!string.Equals(candidateNumericValue, numericLabelValue, StringComparison.Ordinal)) continue;
      if (match != null && match != pair.Value) return false;
      match = pair.Value;
    }
    sprite = match;
    return sprite != null;
  }

  static bool TryEnsureEntrySpriteMapMaterialized(CacheEntry entry) {
    if (entry == null || !entry.isDone || !entry.isSuccess || entry.primarySprite == null) return false;
    if (entry.spriteMapMaterialized) return true;
    if (!entry.deferredSpriteMapMaterialization) return false;
    if (entry.groupedAtlasTextureHandle.IsValid() || entry.groupedMetadataHandle.IsValid() ||
        entry.metadataAtlasTextureHandle.IsValid() || entry.metadataAtlasMetadataHandle.IsValid()) {
      if (!TryMaterializeDeferredGeneratedSpriteMap(entry)) return false;
      PopulateEntrySpriteMap(entry, entry.generatedSprites);
      return entry.spriteMapMaterialized && entry.spritesByName.Count > 0;
    }
    if (!entry.handle.IsValid() || entry.handle.Result == null || entry.handle.Result.Count <= 0) return false;
    PopulateEntrySpriteMap(entry, entry.handle.Result);
    return entry.spriteMapMaterialized && entry.spritesByName.Count > 0;
  }

  static bool TryMaterializeDeferredGeneratedSpriteMap(CacheEntry entry) {
    if (entry == null) return false;
    if (entry.generatedSpriteSetComplete && entry.generatedSprites.Count > 0) return true;
    if (entry.groupedAtlasTextureHandle.IsValid() && entry.groupedMetadataHandle.IsValid()) {
      var atlasTexture = entry.groupedAtlasTextureHandle.Result;
      var metadataAsset = entry.groupedMetadataHandle.Result;
      if (atlasTexture == null || metadataAsset == null) return false;
      if (!GeneratedAtlasSpriteSynthesisUtility.TryCreateGroupedSurrogateSprites(atlasTexture, metadataAsset, out var groupedSprites)) return false;
      MergeMaterializedGeneratedSprites(entry, groupedSprites);
      entry.generatedSpriteSetComplete = entry.generatedSprites.Count > 0;
      return entry.generatedSpriteSetComplete;
    }
    if (entry.metadataAtlasTextureHandle.IsValid() && entry.metadataAtlasMetadataHandle.IsValid()) {
      var atlasTexture = entry.metadataAtlasTextureHandle.Result;
      var metadataAsset = entry.metadataAtlasMetadataHandle.Result;
      if (atlasTexture == null || metadataAsset == null) return false;
      if (!GeneratedAtlasSpriteSynthesisUtility.TryCreateSpritesFromMetadata(atlasTexture, 100f, SpriteMeshType.FullRect, metadataAsset, out var generatedSprites, out _)) return false;
      MergeMaterializedGeneratedSprites(entry, generatedSprites);
      entry.generatedSpriteSetComplete = entry.generatedSprites.Count > 0;
      return entry.generatedSpriteSetComplete;
    }
    return entry.generatedSpriteSetComplete && entry.generatedSprites.Count > 0;
  }

  static void MergeMaterializedGeneratedSprites(CacheEntry entry, List<Sprite> materializedSprites) {
    if (materializedSprites == null || materializedSprites.Count <= 0) return;
    if (entry == null) {
      GeneratedAtlasSpriteSynthesisUtility.DestroySprites(materializedSprites);
      return;
    }
    var primaryName = entry.primarySprite?.name ?? "";
    foreach (var sprite in materializedSprites) {
      if (sprite == null) continue;
      if (!string.IsNullOrWhiteSpace(primaryName) && string.Equals(primaryName, sprite.name, StringComparison.Ordinal)) {
        DestroyGeneratedSprite(sprite);
        continue;
      }
      entry.generatedSprites.Add(sprite);
    }
    materializedSprites.Clear();
  }

  static void DestroyGeneratedSprite(Sprite sprite) {
    if (sprite == null) return;
    if (Application.isPlaying) UnityEngine.Object.Destroy(sprite);
    else UnityEngine.Object.DestroyImmediate(sprite);
  }

  static int CountLoadedSprites(IList<Sprite> sprites) {
    if (sprites == null || sprites.Count <= 0) return 0;
    var count = 0;
    foreach (var s in sprites) if (s != null) count++;
    return count;
  }

  static bool HasAnyLoadedSprite(IList<Sprite> sprites) => TryGetFirstLoadedSprite(sprites, out _);
  static bool TryGetFirstLoadedSprite(IList<Sprite> sprites, out Sprite sprite) {
    sprite = null;
    if (sprites == null || sprites.Count <= 0) return false;
    foreach (var s in sprites) {
      if (s != null) { sprite = s; return true; }
    }
    return false;
  }

  static bool ShouldMarkSpriteMapMaterialized(CacheEntry entry, int loadedSpriteCount) {
    if (entry == null || entry.primarySprite == null) return false;
    if (entry.requestStrategy != "atlas_backed") return true;
    return loadedSpriteCount > 1;
  }

  static string DescribeSpriteNames(IList<Sprite> loadedSprites, int maxCount = 24) {
    if (loadedSprites == null || loadedSprites.Count <= 0) return "";
    var limit = Math.Min(Math.Max(maxCount, 1), loadedSprites.Count);
    var builder = new StringBuilder();
    for (var i = 0; i < limit; i++) {
      if (builder.Length > 0) builder.Append(",");
      builder.Append(loadedSprites[i] != null ? (loadedSprites[i].name ?? "") : "<null>");
    }
    if (loadedSprites.Count > limit) builder.Append(",...");
    return builder.ToString();
  }

  static string DescribeExpectedSpriteNames(string atlasAddress, int maxCount = 24) {
    if (!TryCollectExpectedAtlasSpriteNames(atlasAddress, atlasSiblingSpriteNameScratch, maxCount)) return "";
    var limit = Math.Min(Math.Max(maxCount, 1), atlasSiblingSpriteNameScratch.Count);
    var builder = new StringBuilder();
    for (var i = 0; i < limit; i++) {
      if (builder.Length > 0) builder.Append(",");
      builder.Append(atlasSiblingSpriteNameScratch[i]);
    }
    if (atlasSiblingSpriteNameScratch.Count > limit) builder.Append(",...");
    atlasSiblingSpriteNameScratch.Clear();
    return builder.ToString();
  }

  static void LogAtlasSpriteMapBuildDiagnostics(CacheEntry entry, IList<Sprite> loadedSprites, string mapSource, int aliasCount) {
    if (entry == null || !ShouldLogAtlasNameDiagnostics(entry.address)) return;
    var requestedAddress = string.IsNullOrWhiteSpace(entry.lastRequestedAddress) ? entry.address : entry.lastRequestedAddress;
    Debug.Log("[TextureResidencyCache] Atlas sprite map build requested='" + requestedAddress + "' atlas='" + entry.address + "' expected_label='" + entry.requestedSpriteNameHint + "' map_source='" + mapSource + "' alias_count=" + aliasCount + " loaded_names='" + DescribeSpriteNames(loadedSprites) + "' expected_names='" + DescribeExpectedSpriteNames(entry.address) + "'");
  }

  static bool ShouldLogAtlasNameDiagnostics(string address) {
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs || !SpriteStreamingRuntimeSettings.EnableDiagnostics || string.IsNullOrWhiteSpace(address)) return false;
    var normalizedAddress = NormalizeAddress(address);
    return normalizedAddress.IndexOf("/MainMenu/", StringComparison.OrdinalIgnoreCase) >= 0 ||
           normalizedAddress.IndexOf("/Fonts/", StringComparison.OrdinalIgnoreCase) >= 0;
  }

}
}
#endif

