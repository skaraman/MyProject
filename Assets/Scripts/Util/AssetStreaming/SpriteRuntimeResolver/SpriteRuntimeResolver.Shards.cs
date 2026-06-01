using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public static partial class SpriteRuntimeResolver {
  static bool TryGetShard(string namepart, ManifestEntry entry, out ShardData shard) {
    if (loadedShards.TryGetValue(namepart, out shard)) {
      shard.lastAccessTime = Time.realtimeSinceStartup;
      return true;
    }

    if (shardParses.TryGetValue(namepart, out var shardParseTask)) {
      if (!shardParseTask.IsCompleted) return false;

      shardParses.Remove(namepart);
      if (shardParseTask.IsFaulted || shardParseTask.IsCanceled) {
        RateLimitedLog("shardparse:" + namepart, "[SpriteRuntimeResolver] Failed to parse shard rows for '" + namepart + "'.");
        return false;
      }

      var parsedShard = shardParseTask.Result;
      loadedShards[namepart] = new ShardData {
        rows = parsedShard?.rows ?? new Dictionary<string, SpriteAddressPair>(StringComparer.Ordinal),
        addressesByAtlasPath = parsedShard?.addressesByAtlasPath,
        atlasLookupBuilt = parsedShard?.addressesByAtlasPath != null,
        lastAccessTime = Time.realtimeSinceStartup
      };
      EnforceShardBudget();
      shard = loadedShards[namepart];
      return true;
    }

    if (shardLoads.ContainsKey(namepart)) return false;
    if (string.IsNullOrWhiteSpace(entry.address)) return false;

    StartShardLoad(namepart, entry);
    return false;
  }

  static void StartShardLoad(string namepart, ManifestEntry entry) {
    if (string.IsNullOrWhiteSpace(namepart)) return;
    if (loadedShards.ContainsKey(namepart)) return;
    if (shardLoads.ContainsKey(namepart)) return;
    if (shardParses.ContainsKey(namepart)) return;
    if (string.IsNullOrWhiteSpace(entry.address)) return;

    var shardAddress = entry.address.Trim();
    shardLoadStartedAt[namepart] = Time.realtimeSinceStartup;
    var load = Addressables.LoadAssetAsync<TextAsset>(shardAddress);
    shardLoads[namepart] = load;
    load.Completed += operation => {
      shardLoads.Remove(namepart);
      RecordShardLoadLatency(namepart);

      if (operation.Status != AsyncOperationStatus.Succeeded || operation.Result == null) {
        RateLimitedLog("shardload:" + namepart, "[SpriteRuntimeResolver] Failed to load shard for '" + namepart + "' at address '" + shardAddress + "'.");
        if (operation.IsValid()) {
          Addressables.Release(operation);
        }
        return;
      }

      var shardText = operation.Result.text ?? "";
      shardParses[namepart] = Task.Run(() => ParseShardRows(shardText, allowUnityLogging: false));

      if (operation.IsValid()) {
        Addressables.Release(operation);
      }
    };
  }

  static void TryStartShardWarmup(string normalizedNamepart) {
    if (string.IsNullOrWhiteSpace(normalizedNamepart)) return;
    if (!manifestReady) {
      if (pendingWarmupNamepartsSet.Add(normalizedNamepart)) {
        pendingWarmupNameparts.Add(normalizedNamepart);
      }
      return;
    }
    if (!TryGetManifestEntryForNamepart(manifestByNamepart, normalizedNamepart, out var shardEntry)) return;

    var shardKey = string.IsNullOrWhiteSpace(shardEntry.namepart) ? normalizedNamepart : shardEntry.namepart;
    StartShardLoad(shardKey, shardEntry);
  }

  static void DrainPendingWarmups() {
    if (!manifestReady || pendingWarmupNameparts.Count == 0) return;

    if (pendingWarmupNameparts.Count > 1) {
      SortWarmupsByObservedLoadCost(pendingWarmupNameparts);
    }
    for (var i = 0; i < pendingWarmupNameparts.Count; i++) {
      TryStartShardWarmup(pendingWarmupNameparts[i]);
    }
    pendingWarmupNameparts.Clear();
    pendingWarmupNamepartsSet.Clear();
  }

  static void SortWarmupsByObservedLoadCost(List<string> warmups) {
    if (warmups == null || warmups.Count <= 1) return;
    warmups.Sort((a, b) => {
      var scoreA = ResolveWarmupPriorityScore(a);
      var scoreB = ResolveWarmupPriorityScore(b);
      var cmp = scoreB.CompareTo(scoreA); // slower shards first
      if (cmp != 0) return cmp;
      return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    });
  }

  static float ResolveWarmupPriorityScore(string normalizedNamepart) {
    if (string.IsNullOrWhiteSpace(normalizedNamepart)) return 0f;
    var shardKey = normalizedNamepart;
    if (manifestReady && TryGetManifestEntryForNamepart(manifestByNamepart, normalizedNamepart, out var shardEntry)) {
      shardKey = string.IsNullOrWhiteSpace(shardEntry.namepart) ? normalizedNamepart : shardEntry.namepart;
    }

    shardLoadEwmaMs.TryGetValue(shardKey, out var ewmaMs);
    shardSlowLoadHits.TryGetValue(shardKey, out var slowHits);
    return ewmaMs + (slowHits * SlowShardWarmupBonusMs);
  }

  static void RecordShardLoadLatency(string shardKey) {
    if (string.IsNullOrWhiteSpace(shardKey)) return;
    if (!shardLoadStartedAt.TryGetValue(shardKey, out var startedAt)) return;
    shardLoadStartedAt.Remove(shardKey);

    var loadMs = Mathf.Max((Time.realtimeSinceStartup - startedAt) * 1000f, 0f);
    shardLoadEwmaMs.TryGetValue(shardKey, out var previousEwmaMs);
    var ewmaMs = previousEwmaMs > 0f
      ? Mathf.Lerp(previousEwmaMs, loadMs, ShardLoadEwmaBlend)
      : loadMs;
    shardLoadEwmaMs[shardKey] = ewmaMs;

    shardSlowLoadHits.TryGetValue(shardKey, out var slowHits);
    if (loadMs >= SlowShardLoadThresholdMs) {
      slowHits = Mathf.Min(slowHits + 1, 8);
    }
    else if (slowHits > 0) {
      slowHits--;
    }
    shardSlowLoadHits[shardKey] = slowHits;
  }

  static void EnforceShardBudget() {
    var cfg = GetSettings();
    var maxLoaded = Math.Max(cfg.maxLoadedShards, 1);

    while (loadedShards.Count > maxLoaded) {
      string oldestKey = null;
      float oldestTime = float.MaxValue;

      foreach (var pair in loadedShards) {
        if (pair.Value == null) continue;
        if (pair.Value.lastAccessTime >= oldestTime) continue;
        oldestTime = pair.Value.lastAccessTime;
        oldestKey = pair.Key;
      }

      if (oldestKey == null) break;
      loadedShards.Remove(oldestKey);
    }
  }

  static void EnsureShardAtlasLookup(ShardData shard) {
    if (shard == null) return;
    if (shard.atlasLookupBuilt && shard.addressesByAtlasPath != null) return;

    shard.atlasLookupBuilt = true;
    shard.addressesByAtlasPath = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    if (shard.rows == null || shard.rows.Count <= 0) return;

    foreach (var pair in shard.rows) {
      AddAddressToAtlasLookup(shard.addressesByAtlasPath, pair.Value.colorAtlasAddress, pair.Value.colorAddress);
      AddAddressToAtlasLookup(shard.addressesByAtlasPath, pair.Value.normalAtlasAddress, pair.Value.normalAddress);
    }
  }

  static void AddAddressToAtlasLookup(Dictionary<string, List<string>> atlasMap, string atlasAssetPath, string sliceAddress) {
    if (atlasMap == null || string.IsNullOrWhiteSpace(sliceAddress)) return;
    var normalizedAtlasPath = atlasAssetPath;
    var normalizedSliceAddress = sliceAddress;
    if (string.IsNullOrWhiteSpace(normalizedAtlasPath)) {
      if (!SpriteSliceAddressUtility.TryParseSliceAddress(sliceAddress, out var parsedAtlasAssetPath, out _)) return;
      normalizedAtlasPath = NormalizeToken(parsedAtlasAssetPath);
      normalizedSliceAddress = NormalizeToken(sliceAddress);
    }
    if (string.IsNullOrWhiteSpace(normalizedAtlasPath) || string.IsNullOrWhiteSpace(normalizedSliceAddress)) return;

    if (!atlasMap.TryGetValue(normalizedAtlasPath, out var addresses) || addresses == null) {
      addresses = new List<string>();
      atlasMap[normalizedAtlasPath] = addresses;
    }

    // TODO: O(n) linear scan for dedup. For large atlases this scales poorly.
    // Replace the per-atlas List<string> with a HashSet<string> (OrdinalIgnoreCase) for O(1) contains,
    // or maintain a parallel HashSet alongside the list if ordered iteration is required.
    for (var i = 0; i < addresses.Count; i++) {
      if (string.Equals(addresses[i], normalizedSliceAddress, StringComparison.OrdinalIgnoreCase)) return;
    }
    addresses.Add(normalizedSliceAddress);
  }

  static void RemoveAddressFromAtlasLookup(Dictionary<string, List<string>> atlasMap, string atlasAssetPath, string sliceAddress) {
    if (atlasMap == null || string.IsNullOrWhiteSpace(sliceAddress)) return;
    var normalizedAtlasPath = atlasAssetPath;
    var normalizedSliceAddress = sliceAddress;
    if (string.IsNullOrWhiteSpace(normalizedAtlasPath)) {
      if (!SpriteSliceAddressUtility.TryParseSliceAddress(sliceAddress, out var parsedAtlasAssetPath, out _)) return;
      normalizedAtlasPath = NormalizeToken(parsedAtlasAssetPath);
      normalizedSliceAddress = NormalizeToken(sliceAddress);
    }
    if (string.IsNullOrWhiteSpace(normalizedAtlasPath) || string.IsNullOrWhiteSpace(normalizedSliceAddress)) return;
    if (!atlasMap.TryGetValue(normalizedAtlasPath, out var addresses) || addresses == null) return;

    for (var i = addresses.Count - 1; i >= 0; i--) {
      if (!string.Equals(addresses[i], normalizedSliceAddress, StringComparison.OrdinalIgnoreCase)) continue;
      addresses.RemoveAt(i);
      break;
    }

    if (addresses.Count <= 0) {
      atlasMap.Remove(normalizedAtlasPath);
    }
  }

  static ParsedShardData ParseShardRows(string text, bool allowUnityLogging = true) {
    var parsedShard = new ParsedShardData {
      rows = new Dictionary<string, SpriteAddressPair>(StringComparer.Ordinal),
      addressesByAtlasPath = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
    };
    if (string.IsNullOrWhiteSpace(text)) return parsedShard;

    var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    for (var i = 0; i < lines.Length; i++) {
      var line = lines[i].TrimStart('\uFEFF');
      if (line.StartsWith("#", StringComparison.Ordinal)) continue;

      var cols = line.Split('\t');
      if (cols.Length < 5) continue;

      var form = NormalizeToken(Unescape(cols[0]));
      var animation = NormalizeToken(Unescape(cols[1]));
      if (!int.TryParse(cols[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var frame)) continue;

      var key = BuildRowKey(form, animation, frame);
      if (allowUnityLogging && parsedShard.rows.ContainsKey(key)) {
        RateLimitedWarning(
          "shard-duplicate:" + key,
          "[SpriteRuntimeResolver] Duplicate shard row for key '" + key + "'. Last row wins."
        );
      }
      if (parsedShard.rows.TryGetValue(key, out var previousPair)) {
        RemoveAddressFromAtlasLookup(parsedShard.addressesByAtlasPath, previousPair.colorAtlasAddress, previousPair.colorAddress);
        RemoveAddressFromAtlasLookup(parsedShard.addressesByAtlasPath, previousPair.normalAtlasAddress, previousPair.normalAddress);
      }

      var spritePair = SpriteAddressPair.Create(
        NormalizeToken(Unescape(cols[3])),
        NormalizeToken(Unescape(cols[4]))
      );
      parsedShard.rows[key] = spritePair;
      AddAddressToAtlasLookup(parsedShard.addressesByAtlasPath, spritePair.colorAtlasAddress, spritePair.colorAddress);
      AddAddressToAtlasLookup(parsedShard.addressesByAtlasPath, spritePair.normalAtlasAddress, spritePair.normalAddress);
    }

    return parsedShard;
  }
}
