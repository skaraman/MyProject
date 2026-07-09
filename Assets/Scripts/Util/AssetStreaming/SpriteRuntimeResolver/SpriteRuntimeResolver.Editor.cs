using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static partial class SpriteRuntimeResolver {
#if UNITY_EDITOR
  static bool TryEnsureEditorManifestReady() {
    if (!Application.isEditor || !Application.isPlaying) return false;
    if (manifestLoadStarted || manifestParse != null) return false;

    var manifestAsset = LoadEditorManifestAsset();
    if (manifestAsset == null || string.IsNullOrWhiteSpace(manifestAsset.text)) return false;

    var parsed = ParseManifestRows(manifestAsset.text);
    if (parsed.Count <= 0) return false;

    manifestByNamepart.Clear();
    lookupHitCache.Clear();
    lookupMissCache.Clear();
    foreach (var pair in parsed) {
      manifestByNamepart[pair.Key] = pair.Value;
    }

    manifestReady = true;
    manifestFailed = false;
    DrainPendingWarmups();
    return true;
  }

  public static bool TryResolveEditor(SpriteLookupKey key, out SpriteAddressPair pair, Object logContext = null) {
    pair = default;
    var manifestAsset = LoadEditorManifestAsset();
    if (manifestAsset != null && !string.IsNullOrWhiteSpace(manifestAsset.text)) {
      var manifestRows = ParseManifestRows(manifestAsset.text);
      if (manifestRows.Count > 0) {
        if (TryResolveEditorFromManifestRows(manifestRows, key, out pair, logContext)) {
          return true;
        }

        if (TryBuildLegacyFormUiKey(key, out var formUiAliasKey) &&
            TryResolveEditorFromManifestRows(manifestRows, formUiAliasKey, out pair, logContext)) {
          return true;
        }
      }
    }

    return false;
  }

  static bool TryResolveEditorFromManifestRows(
    Dictionary<string, ManifestEntry> manifestRows,
    SpriteLookupKey key,
    out SpriteAddressPair pair,
    Object logContext = null
  ) {
    pair = default;
    if (manifestRows == null || manifestRows.Count <= 0) return false;

    var normalizedNamepart = NormalizeNamePart(key.namepart);
    if (!TryGetManifestEntryForNamepart(manifestRows, normalizedNamepart, out var shardEntry, logContext)) return false;
    if (string.IsNullOrWhiteSpace(shardEntry.assetPath)) return false;

    var shardAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(shardEntry.assetPath);
    if (shardAsset == null || string.IsNullOrWhiteSpace(shardAsset.text)) return false;

    var parsedShard = ParseShardRows(shardAsset.text);
    var rows = parsedShard.rows;
    if (rows == null || rows.Count <= 0) return false;

    var exactKey = BuildRowKey(key.labelPrefix, key.category, key.frame);
    if (rows.TryGetValue(exactKey, out pair)) return true;

    if (key.frame != 0) {
      var frameZeroKey = BuildRowKey(key.labelPrefix, key.category, 0);
      if (rows.TryGetValue(frameZeroKey, out pair)) return true;
    }

    return TryResolveNumericFormFallback(rows, key, out pair);
  }

  static TextAsset LoadEditorManifestAsset() {
    return AssetDatabase.LoadAssetAtPath<TextAsset>(RuntimeConfig.ManifestAssetPath);
  }

  public static bool TryLoadEditorSprite(string address, out Sprite sprite) {
    sprite = null;
    if (string.IsNullOrWhiteSpace(address)) return false;

    var normalizedAddress = NormalizeToken(address);
    var assetPath = normalizedAddress;
    var spriteName = "";

    var bracketIndex = normalizedAddress.LastIndexOf('[');
    if (bracketIndex > 0 && normalizedAddress.EndsWith("]", StringComparison.Ordinal)) {
      assetPath = normalizedAddress.Substring(0, bracketIndex);
      spriteName = normalizedAddress.Substring(bracketIndex + 1, normalizedAddress.Length - bracketIndex - 2);
    }

    assetPath = CollapseSlashes(assetPath.Replace('\\', '/'));
    if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
        !assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) return false;

    var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
    if (TryMatchEditorSprite(assets, spriteName, out sprite)) return true;

    var representations = AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath);
    if (TryMatchEditorSprite(representations, spriteName, out sprite)) return true;
    if (TryMatchEditorSpriteByMetaFileId(assetPath, spriteName, assets, representations, out sprite)) return true;

    if (editorSpriteLoadWarnings.Add(normalizedAddress)) {
      Debug.LogWarning(
        "[SpriteRuntimeResolver] Editor sprite load miss asset='" + assetPath +
        "' sprite='" + spriteName +
        "' assets_count=" + (assets?.Length ?? 0) +
        " representations_count=" + (representations?.Length ?? 0)
      );
    }

    return false;
  }
#endif

#if UNITY_EDITOR
  static bool TryMatchEditorSprite(UnityEngine.Object[] assets, string spriteName, out Sprite sprite) {
    sprite = null;
    if (assets == null || assets.Length == 0) return false;

    var allowNumericFallback = SpriteSliceAddressUtility.CanUseNumericLabelFallback(spriteName);
    Sprite numericMatch = null;
    for (var i = 0; i < assets.Length; i++) {
      var candidate = assets[i] as Sprite;
      if (candidate == null) continue;
      if (string.IsNullOrEmpty(spriteName) || string.Equals(candidate.name, spriteName, StringComparison.Ordinal)) {
        sprite = candidate;
        return true;
      }

      if (!allowNumericFallback) continue;
      if (!SpriteSliceAddressUtility.HasEquivalentNumericLabel(candidate.name, spriteName)) continue;
      if (numericMatch != null && numericMatch != candidate) {
        numericMatch = null;
        break;
      }
      numericMatch = candidate;
    }

    if (numericMatch == null) return false;
    sprite = numericMatch;
    return true;
  }

  static bool TryMatchEditorSpriteByMetaFileId(
    string assetPath,
    string spriteName,
    UnityEngine.Object[] assets,
    UnityEngine.Object[] representations,
    out Sprite sprite) {
    sprite = null;
    if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(spriteName)) return false;
    if (!TryResolveEditorSpriteLocalFileId(assetPath, spriteName, out var localFileId)) return false;

    if (TryMatchEditorSpriteByLocalFileId(assets, localFileId, out sprite)) return true;
    if (TryMatchEditorSpriteByLocalFileId(representations, localFileId, out sprite)) return true;
    return false;
  }

  static bool TryMatchEditorSpriteByLocalFileId(UnityEngine.Object[] assets, long localFileId, out Sprite sprite) {
    sprite = null;
    if (assets == null || assets.Length == 0) return false;

    for (var i = 0; i < assets.Length; i++) {
      var candidate = assets[i] as Sprite;
      if (candidate == null) continue;
      if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate, out _, out long candidateLocalFileId)) continue;
      if (candidateLocalFileId != localFileId) continue;
      sprite = candidate;
      return true;
    }

    return false;
  }

  static bool TryResolveEditorSpriteLocalFileId(string assetPath, string spriteName, out long localFileId) {
    localFileId = 0;
    if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(spriteName)) return false;

    if (!editorMetaSpriteIdsByAssetPath.TryGetValue(assetPath, out var spriteIdsByName)) {
      spriteIdsByName = BuildEditorMetaSpriteIdMap(assetPath);
      editorMetaSpriteIdsByAssetPath[assetPath] = spriteIdsByName;
    }

    return spriteIdsByName != null && spriteIdsByName.TryGetValue(spriteName, out localFileId);
  }

  static string ResolvePhysicalPath(string assetPath) {
    if (string.IsNullOrWhiteSpace(assetPath)) return "";
    if (assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) {
      var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
      if (packageInfo != null) {
        var prefix = packageInfo.assetPath;
        if (assetPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
          var relativePath = assetPath.Substring(prefix.Length).TrimStart('/', '\\');
          return Path.Combine(packageInfo.resolvedPath, relativePath).Replace('\\', '/');
        }
      }
    }
    return assetPath;
  }

  static Dictionary<string, long> BuildEditorMetaSpriteIdMap(string assetPath) {
    var spriteIdsByName = new Dictionary<string, long>(StringComparer.Ordinal);
    if (string.IsNullOrWhiteSpace(assetPath)) return spriteIdsByName;

    var physicalPath = ResolvePhysicalPath(assetPath);
    var metaPath = physicalPath + ".meta";
    if (!File.Exists(metaPath)) return spriteIdsByName;

    var inTable = false;
    var tableIndent = -1;
    foreach (var rawLine in File.ReadLines(metaPath)) {
      var line = rawLine ?? "";
      var trimmed = line.Trim();
      var indent = line.Length - line.TrimStart().Length;

      if (!inTable) {
        if (trimmed.StartsWith("nameFileIdTable:", StringComparison.Ordinal)) {
          inTable = true;
          tableIndent = indent;
        }
        continue;
      }

      if (string.IsNullOrWhiteSpace(trimmed)) continue;

      if (indent <= tableIndent) {
        inTable = false;
        if (trimmed.StartsWith("nameFileIdTable:", StringComparison.Ordinal)) {
          inTable = true;
          tableIndent = indent;
        }
        continue;
      }

      var separatorIndex = trimmed.IndexOf(':');
      if (separatorIndex <= 0 || separatorIndex >= trimmed.Length - 1) continue;

      var name = DecodeEditorMetaScalar(trimmed.Substring(0, separatorIndex));
      if (string.IsNullOrWhiteSpace(name)) continue;

      var idText = trimmed.Substring(separatorIndex + 1).Trim();
      if (!long.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId)) continue;
      spriteIdsByName[name] = parsedId;
    }

    return spriteIdsByName;
  }

  static string DecodeEditorMetaScalar(string value) {
    if (string.IsNullOrWhiteSpace(value)) return "";
    var trimmed = value.Trim();
    if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[trimmed.Length - 1] == '\'') {
      return trimmed.Substring(1, trimmed.Length - 2).Replace("''", "'");
    }

    return trimmed;
  }
#endif
}
