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
        lookupRows = parsedShard?.lookupRows ?? new Dictionary<RowLookupKey, SpriteAddressPair>(),
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
    shard.addressesByAtlasPath = BuildAtlasAddressLookup(shard.rows);
  }

  static Dictionary<string, List<string>> BuildAtlasAddressLookup(Dictionary<string, SpriteAddressPair> rows) {
    var atlasMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    if (rows == null || rows.Count <= 0) return atlasMap;

    var seenByAtlasPath = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    var rowKeys = new List<string>(rows.Keys);
    rowKeys.Sort(StringComparer.Ordinal);

    for (var i = 0; i < rowKeys.Count; i++) {
      var pair = rows[rowKeys[i]];
      AddAddressToAtlasLookup(
        atlasMap,
        seenByAtlasPath,
        pair.colorAtlasAddress,
        pair.colorAddress
      );
      AddAddressToAtlasLookup(
        atlasMap,
        seenByAtlasPath,
        pair.normalAtlasAddress,
        pair.normalAddress
      );
      AddAddressToAtlasLookup(
        atlasMap,
        seenByAtlasPath,
        pair.specularAtlasAddress,
        pair.specularAddress
      );
    }
    return atlasMap;
  }

  static void AddAddressToAtlasLookup(
    Dictionary<string, List<string>> atlasMap,
    Dictionary<string, HashSet<string>> seenByAtlasPath,
    string atlasAssetPath,
    string sliceAddress
  ) {
    if (atlasMap == null || string.IsNullOrWhiteSpace(sliceAddress)) return;
    if (seenByAtlasPath == null) return;

    var normalizedAtlasPath = NormalizeTokenUncached(atlasAssetPath);
    var normalizedSliceAddress = NormalizeTokenUncached(sliceAddress);
    if (string.IsNullOrWhiteSpace(normalizedAtlasPath)) {
      if (!SpriteSliceAddressUtility.TryParseSliceAddress(normalizedSliceAddress, out var parsedAtlasAssetPath, out _)) return;
      normalizedAtlasPath = NormalizeTokenUncached(parsedAtlasAssetPath);
    }
    if (string.IsNullOrWhiteSpace(normalizedAtlasPath) || string.IsNullOrWhiteSpace(normalizedSliceAddress)) return;

    if (!atlasMap.TryGetValue(normalizedAtlasPath, out var addresses) || addresses == null) {
      addresses = new List<string>();
      atlasMap[normalizedAtlasPath] = addresses;
    }

    if (!seenByAtlasPath.TryGetValue(normalizedAtlasPath, out var seenAddresses) || seenAddresses == null) {
      seenAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      seenByAtlasPath[normalizedAtlasPath] = seenAddresses;
    }
    if (!seenAddresses.Add(normalizedSliceAddress)) return;
    addresses.Add(normalizedSliceAddress);
  }

  static ParsedShardData ParseShardRows(string text, bool allowUnityLogging = true) {
    var parsedShard = new ParsedShardData {
      rows = new Dictionary<string, SpriteAddressPair>(StringComparer.Ordinal),
      lookupRows = new Dictionary<RowLookupKey, SpriteAddressPair>(),
      addressesByAtlasPath = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
    };
    if (string.IsNullOrWhiteSpace(text)) return parsedShard;

    var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    for (var i = 0; i < lines.Length; i++) {
      var line = lines[i].TrimStart('\uFEFF');
      if (line.StartsWith("#", StringComparison.Ordinal)) continue;

      var cols = line.Split('\t');
      if (cols.Length < 5) continue;

      var form = NormalizeTokenUncached(Unescape(cols[0]));
      var animation = NormalizeTokenUncached(Unescape(cols[1]));
      if (!int.TryParse(cols[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var frame)) continue;

      var key = BuildRowKeyUncached(form, animation, frame);
      if (allowUnityLogging && parsedShard.rows.ContainsKey(key)) {
        RateLimitedWarning(
          "shard-duplicate:" + key,
          "[SpriteRuntimeResolver] Duplicate shard row for key '" + key + "'. Last row wins."
        );
      }
      var specularAddress = cols.Length >= 6 ? NormalizeTokenUncached(Unescape(cols[5])) : "";
      var spritePair = SpriteAddressPair.Create(
        NormalizeTokenUncached(Unescape(cols[3])),
        NormalizeTokenUncached(Unescape(cols[4])),
        specularAddress
      );
      var lookupKey = new RowLookupKey(form, animation, frame);
      parsedShard.rows[key] = spritePair;
      parsedShard.lookupRows[lookupKey] = spritePair;
    }

    parsedShard.addressesByAtlasPath = BuildAtlasAddressLookup(parsedShard.rows);
    return parsedShard;
  }
}
