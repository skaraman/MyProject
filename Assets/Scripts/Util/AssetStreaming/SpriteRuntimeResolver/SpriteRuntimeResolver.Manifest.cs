using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

public static partial class SpriteRuntimeResolver {
  static bool TryGetManifestEntryForNamepart(
    Dictionary<string, ManifestEntry> rows,
    string requestedNamepart,
    out ManifestEntry entry,
    Object logContext = null
  ) {
    entry = default;
    if (rows == null || rows.Count == 0) return false;
    if (string.IsNullOrWhiteSpace(requestedNamepart)) return false;

    if (rows.TryGetValue(requestedNamepart, out entry)) return true;

    if (TryResolveMovedFullPathAlias(rows, requestedNamepart, out entry, logContext)) return true;

    if (requestedNamepart.IndexOf('/') >= 0) return false;

    if (ambiguousShortNamepartMatches.TryGetValue(requestedNamepart, out var ambiguousMatches) &&
        ambiguousMatches != null &&
        ambiguousMatches.Count > 1) {
      RateLimitedWarning(
        BuildContextualWarningKey("shortkey-ambiguous-request:" + requestedNamepart, logContext),
        AppendWarningContext("[SpriteRuntimeResolver] " + BuildShortKeyAmbiguityError(requestedNamepart, ambiguousMatches), logContext),
        logContext
      );
    }

    return false;
  }

  static bool TryResolveMovedFullPathAlias(
    Dictionary<string, ManifestEntry> rows,
    string requestedNamepart,
    out ManifestEntry entry,
    Object logContext = null
  ) {
    entry = default;
    if (rows == null || rows.Count == 0 || string.IsNullOrWhiteSpace(requestedNamepart)) return false;

    var slash = requestedNamepart.LastIndexOf('/');
    if (slash < 0 || slash >= requestedNamepart.Length - 1) return false;

    var leafName = NormalizeNamePart(requestedNamepart.Substring(slash + 1));
    if (string.IsNullOrWhiteSpace(leafName)) return false;

    if (ambiguousShortNamepartMatches.TryGetValue(leafName, out var ambiguousMatches) &&
        ambiguousMatches != null &&
        ambiguousMatches.Count > 1) {
      RateLimitedWarning(
        BuildContextualWarningKey("moved-fullpath-ambiguous-request:" + requestedNamepart, logContext),
        AppendWarningContext("[SpriteRuntimeResolver] " + BuildShortKeyAmbiguityError(leafName, ambiguousMatches), logContext),
        logContext
      );
      return false;
    }

    if (!rows.TryGetValue(leafName, out entry)) return false;

    var canonicalNamepart = string.IsNullOrWhiteSpace(entry.namepart) ? leafName : entry.namepart;
    if (!string.Equals(canonicalNamepart, requestedNamepart, StringComparison.OrdinalIgnoreCase)) {
      RateLimitedWarning(
        BuildContextualWarningKey("moved-fullpath-remap:" + requestedNamepart, logContext),
        AppendWarningContext("[SpriteRuntimeResolver] Remapped missing namepart '" + requestedNamepart +
        "' to '" + canonicalNamepart +
        "' by unique short name '" + leafName + "'.", logContext),
        logContext
      );
    }

    return true;
  }

  static bool EnsureManifestReady() {
    if (manifestReady) return true;
    if (manifestFailed) return false;
#if UNITY_EDITOR
    if (TryEnsureEditorManifestReady()) return true;
#endif
    if (manifestParse != null) {
      if (!manifestParse.IsCompleted) return false;

      if (manifestParse.IsFaulted || manifestParse.IsCanceled) {
        manifestFailed = true;
        pendingWarmupNameparts.Clear();
        pendingWarmupNamepartsSet.Clear();
        RateLimitedLog("manifest:parse", "[SpriteRuntimeResolver] Failed to parse sprite index manifest.");
        manifestParse = null;
        return false;
      }

      manifestByNamepart.Clear();
      lookupHitCache.Clear();
      lookupMissCache.Clear();
      var parsed = manifestParse.Result;
      foreach (var pair in parsed) {
        manifestByNamepart[pair.Key] = pair.Value;
      }

      manifestParse = null;
      manifestReady = true;
      DrainPendingWarmups();
      return true;
    }

    if (!manifestLoadStarted) {
      manifestLoadStarted = true;
      var cfg = GetSettings();
      var manifestAddress = !string.IsNullOrWhiteSpace(cfg.manifestAddress)
        ? cfg.manifestAddress.Trim()
        : RuntimeConfig.DefaultManifestAddress;

      manifestLoad = Addressables.LoadAssetAsync<TextAsset>(manifestAddress);
      manifestLoad.Completed += operation => {
        if (operation.Status != AsyncOperationStatus.Succeeded || operation.Result == null || string.IsNullOrWhiteSpace(operation.Result.text)) {
          manifestFailed = true;
          pendingWarmupNameparts.Clear();
          pendingWarmupNamepartsSet.Clear();
          RateLimitedLog("manifest:" + manifestAddress, "[SpriteRuntimeResolver] Failed to load sprite index manifest at address '" + manifestAddress + "'.");
          if (operation.IsValid()) {
            Addressables.Release(operation);
          }
          return;
        }

        var manifestText = operation.Result.text ?? "";
        manifestParse = Task.Run(() => ParseManifestRows(manifestText, allowUnityLogging: false));
        if (operation.IsValid()) {
          Addressables.Release(operation);
        }
      };
    }

    return manifestReady;
  }

  static Dictionary<string, ManifestEntry> ParseManifestRows(string text, bool allowUnityLogging = true) {
    var rows = new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);
    if (string.IsNullOrWhiteSpace(text)) return rows;

    var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    for (var i = 0; i < lines.Length; i++) {
      var line = lines[i].TrimStart('\uFEFF');
      if (line.StartsWith("#", StringComparison.Ordinal)) continue;

      var cols = line.Split('\t');
      if (cols.Length < 3) continue;

      var normalizedNamepart = NormalizeNamePart(Unescape(cols[0]));
      var address = NormalizeToken(Unescape(cols[1]));
      var assetPath = NormalizeToken(Unescape(cols[2]));
      if (string.IsNullOrWhiteSpace(normalizedNamepart) || string.IsNullOrWhiteSpace(address)) continue;

      if (allowUnityLogging && rows.ContainsKey(normalizedNamepart)) {
        RateLimitedWarning(
          "manifest-duplicate:" + normalizedNamepart,
          "[SpriteRuntimeResolver] Duplicate manifest row for namepart '" + normalizedNamepart + "'. Last row wins."
        );
      }

      rows[normalizedNamepart] = new ManifestEntry {
        namepart = normalizedNamepart,
        address = address,
        assetPath = assetPath
      };
    }

    AddShortNamepartAliases(rows);
    return rows;
  }

  static void AddShortNamepartAliases(Dictionary<string, ManifestEntry> rows) {
    if (rows.Count == 0) return;
    ambiguousShortNamepartMatches.Clear();

    var canonicalKeys = new List<string>(rows.Keys);
    var aliasCandidates = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

    for (var i = 0; i < canonicalKeys.Count; i++) {
      var canonical = canonicalKeys[i];
      if (string.IsNullOrWhiteSpace(canonical)) continue;
      var slash = canonical.LastIndexOf('/');
      if (slash < 0 || slash >= canonical.Length - 1) continue;

      var alias = NormalizeNamePart(canonical.Substring(slash + 1));
      if (string.IsNullOrWhiteSpace(alias)) continue;
      if (rows.ContainsKey(alias)) continue;

      if (!aliasCandidates.TryGetValue(alias, out var candidates)) {
        candidates = new List<string>();
        aliasCandidates[alias] = candidates;
      }
      if (!ContainsIgnoreCase(candidates, canonical)) {
        candidates.Add(canonical);
      }
    }

    foreach (var pair in aliasCandidates) {
      var alias = pair.Key;
      var candidates = pair.Value;
      if (candidates == null || candidates.Count == 0) continue;

      candidates.Sort(StringComparer.OrdinalIgnoreCase);
      if (candidates.Count == 1) {
        if (!rows.TryGetValue(candidates[0], out var canonicalEntry)) continue;
        rows[alias] = canonicalEntry;
        continue;
      }

      ambiguousShortNamepartMatches[alias] = new List<string>(candidates);
    }
  }

  static bool ContainsIgnoreCase(List<string> values, string candidate) {
    if (values == null || string.IsNullOrWhiteSpace(candidate)) return false;
    for (var i = 0; i < values.Count; i++) {
      if (string.Equals(values[i], candidate, StringComparison.OrdinalIgnoreCase)) return true;
    }
    return false;
  }

  static string BuildShortKeyAmbiguityError(string shortKey, List<string> matches) {
    if (matches == null || matches.Count == 0) {
      return "Short name '" + shortKey + "' appears in multiple places.";
    }

    var folders = new List<string>();
    for (var i = 0; i < matches.Count; i++) {
      var match = matches[i];
      if (string.IsNullOrWhiteSpace(match)) continue;
      var slash = match.LastIndexOf('/');
      var folder = slash > 0 ? match.Substring(0, slash) : "(root)";
      if (!ContainsIgnoreCase(folders, folder)) {
        folders.Add(folder);
      }
    }

    folders.Sort(StringComparer.OrdinalIgnoreCase);
    var folderPhrase = JoinAsEnglishList(folders);
    var matchPhrase = "'" + string.Join("', '", matches) + "'";
    var canonicalOptions = string.Join(", ", matches);

    return "Short name '" + shortKey + "' appears in multiple places (" + folderPhrase + "). Matches " + matchPhrase + ". Use one of these canonical full-path nameparts: " + canonicalOptions + ".";
  }

  static string JoinAsEnglishList(List<string> values) {
    if (values == null || values.Count == 0) return "(none)";
    if (values.Count == 1) return values[0];
    if (values.Count == 2) return values[0] + " and " + values[1];
    return string.Join(", ", values.GetRange(0, values.Count - 1)) + ", and " + values[values.Count - 1];
  }
}
