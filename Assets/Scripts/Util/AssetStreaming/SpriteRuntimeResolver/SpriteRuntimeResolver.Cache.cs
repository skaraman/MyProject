using System.Collections.Generic;

public static partial class SpriteRuntimeResolver {
  static void CacheLookupHit(LookupCacheKey key, SpriteAddressPair pair) {
    if (lookupHitCache.Count + lookupMissCache.Count >= MaxLookupCacheEntries) {
      lookupHitCache.Clear();
      lookupMissCache.Clear();
    }
    lookupMissCache.Remove(key);
    lookupHitCache[key] = pair;
  }

  static void CacheLookupMiss(LookupCacheKey key) {
    if (lookupHitCache.Count + lookupMissCache.Count >= MaxLookupCacheEntries) {
      lookupHitCache.Clear();
      lookupMissCache.Clear();
    }
    if (lookupHitCache.ContainsKey(key)) return;
    lookupMissCache.Add(key);
  }

  static void InvalidateLookupCacheEntry(LookupCacheKey key) {
    lookupHitCache.Remove(key);
    lookupMissCache.Remove(key);
  }

  static string ResolveShardKey(string normalizedNamepart) {
    if (string.IsNullOrWhiteSpace(normalizedNamepart)) return "";
    if ((manifestReady || EnsureManifestReady()) &&
        TryGetManifestEntryForNamepart(manifestByNamepart, normalizedNamepart, out var shardEntry)) {
      return string.IsNullOrWhiteSpace(shardEntry.namepart) ? normalizedNamepart : shardEntry.namepart;
    }
    return normalizedNamepart;
  }
}
