#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static partial class SpriteIndexBuilder {

  static Dictionary<string, SpriteRef> ParseLibraryRows(
    string path,
    List<string> errors,
    Dictionary<string, string> activeTextureAssetPathByGuid = null
  ) {
    // Keep row keys case-sensitive so color/normal joins require exact label/category case.
    var rows = new Dictionary<string, SpriteRef>(StringComparer.Ordinal);
    if (!File.Exists(path)) {
      errors.Add("Missing sprite library file '" + path + "'.");
      return rows;
    }

    string currentCategory = null;
    bool insideOverrideEntries = false;
    string currentLabel = null;
    SpriteRef? currentSpriteRef = null;

    void ClearLabel() {
      currentLabel = null;
      currentSpriteRef = null;
    }

    void FlushLabel() {
      if (string.IsNullOrWhiteSpace(currentCategory) || string.IsNullOrWhiteSpace(currentLabel)) {
        ClearLabel();
        return;
      }

      if (!currentSpriteRef.HasValue) {
        ClearLabel();
        return;
      }

      var spriteRef = currentSpriteRef.Value;
      if (activeTextureAssetPathByGuid != null &&
          !activeTextureAssetPathByGuid.ContainsKey(spriteRef.guid ?? "")) {
        ClearLabel();
        return;
      }

      var key = currentCategory + "\u001f" + currentLabel;
      rows[key] = spriteRef;
      ClearLabel();
    }

    foreach (var rawLine in File.ReadLines(path)) {
      var line = rawLine ?? "";

      if (line.StartsWith("  - m_Name:", StringComparison.Ordinal)) {
        FlushLabel();
        currentCategory = DecodeScalar(line.Substring("  - m_Name:".Length));
        insideOverrideEntries = false;
        continue;
      }

      if (line.StartsWith("    m_OverrideEntries:", StringComparison.Ordinal)) {
        insideOverrideEntries = true;
        continue;
      }

      if (!insideOverrideEntries) continue;

      if (line.StartsWith("    - m_Name:", StringComparison.Ordinal)) {
        FlushLabel();
        currentLabel = DecodeScalar(line.Substring("    - m_Name:".Length));
        currentSpriteRef = null;
        continue;
      }

      if (string.IsNullOrWhiteSpace(currentLabel)) continue;

      // Regex guard: skip regex matching if the line doesn't contain m_Sprite
      var spriteMatch = line.Contains("m_Sprite") ? spriteRefRegex.Match(line) : Match.Empty;
      if (!spriteMatch.Success) continue;

      if (!long.TryParse(spriteMatch.Groups[1].Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fileId)) {
        continue;
      }

      var guid = spriteMatch.Groups[2].Value.Trim();
      currentSpriteRef = new SpriteRef(guid, fileId);
    }

    FlushLabel();
    return rows;
  }

  static string ResolveSpriteAddress(BuildState state, SpriteRef spriteRef, string context, bool recordError = true) {
    if (string.IsNullOrWhiteSpace(spriteRef.guid)) {
      if (recordError) {
        state.errors.Add("Missing GUID while resolving .");
      }
      return "";
    }

    var byFileId = GetAddressMapForGuid(state, spriteRef.guid, recordError);

    if (byFileId.TryGetValue(spriteRef.fileId, out var address)) {
      return address;
    }

    if (recordError) {
      Debug.LogWarning($"[SpriteIndexBuilder] ResolveSpriteAddress: Failed to resolve FileID {spriteRef.fileId} in GUID {spriteRef.guid}. Context: {context}. Map size: {byFileId.Count}.");
    }

    var targetUnsigned = unchecked((ulong)spriteRef.fileId);
    foreach (var pair in byFileId) {
      if (unchecked((ulong)pair.Key) != targetUnsigned) continue;
      return pair.Value;
    }

    var fallbackAddress = TryResolveSpriteAddressFromContext(byFileId, context);
    if (!string.IsNullOrWhiteSpace(fallbackAddress)) {
      return fallbackAddress;
    }

    if (recordError) {
      state.errors.Add("Could not resolve sprite fileID '" + spriteRef.fileId + "' for GUID '" + spriteRef.guid + "'.");
    }
    return "";
  }

  static bool ValidateRuntimeAtlasAddress(BuildState state, string sliceAddress, string context, bool recordError = true) {
    return ValidateRuntimeAtlasAddress(state, sliceAddress, context, out _, recordError);
  }

  static bool ValidateRuntimeAtlasAddress(
    BuildState state,
    string sliceAddress,
    string context,
    out string failureReason,
    bool recordError = true
  ) {
    failureReason = "";
    if (string.IsNullOrWhiteSpace(sliceAddress)) {
      failureReason = "Empty slice address.";
      if (recordError) state.errors.Add("Empty atlas address in context '" + context + "'.");
      return false;
    }

    // Runtime shards must point to the staged atlas owner plus exact sprite name.
    if (!SpriteSliceAddressUtility.TryParseSliceAddress(sliceAddress, out var parsedAtlasAssetPath, out var spriteName)) {
      failureReason = "Runtime sprite address must use Assets/.../atlas.png[spriteName] format.";
      if (recordError) {
        state.errors.Add(
          "Invalid runtime sprite address '" + sliceAddress + "'" +
          " in context '" + context + "'. " + failureReason
        );
      }
      return false;
    }

    var atlasAssetPath = NormalizePath(parsedAtlasAssetPath);
    if (string.IsNullOrWhiteSpace(spriteName) || string.IsNullOrWhiteSpace(atlasAssetPath)) {
      failureReason = "Runtime sprite address is missing an atlas path or sprite name.";
      if (recordError) {
        state.errors.Add(
          "Invalid runtime sprite address '" + sliceAddress + "'" +
          " in context '" + context + "'. " + failureReason
        );
      }
      return false;
    }

    if (!IsActiveRuntimeTextureAssetPath(atlasAssetPath)) {
      failureReason = "Atlas path is not under the active runtime texture roots.";
      if (recordError) {
        state.errors.Add(
          "Invalid runtime atlas path '" + atlasAssetPath + "'" +
          " in context '" + context + "'. " + failureReason
        );
      }
      return false;
    }

    if (state.activeTextureAssetPaths.Contains(atlasAssetPath)) return true;

    failureReason = "No staged texture entry found for atlas path '" + atlasAssetPath + "'.";
    if (recordError) {
      state.errors.Add(
        "Unresolved runtime atlas path '" + atlasAssetPath + "'" +
        " in context '" + context + "'."
      );
    }
    return false;
  }

  // ─── Address map cache ────────────────────────────────────────────────────

  static Dictionary<long, string> GetAddressMapForGuid(BuildState state, string guid, bool recordError) {
    if (state.addressCacheByGuid.TryGetValue(guid, out var cached)) return cached;

    var map = new Dictionary<long, string>();
    state.addressCacheByGuid[guid] = map;

    BuildActiveTextureGuidIndex(state);
    if (!state.activeTextureAssetPathByGuid.TryGetValue(guid, out var activeTextureAssetPath)) {
      if (recordError) state.errors.Add("GUID '" + guid + "' did not resolve to an asset path.");
      return map;
    }

    var assetPath = NormalizePath(activeTextureAssetPath);
    if (string.IsNullOrWhiteSpace(assetPath)) return map;
    if (!IsActiveRuntimeTextureAssetPath(assetPath)) return map;
    state.activeTextureAssetPaths.Add(assetPath);

    // High performance sub-asset loading: load ONLY sprite representations without textures
    var objects = AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath);
    var foundSprite = false;
    for (var i = 0; i < objects.Length; i++) {
      if (objects[i] is not UnityEngine.Sprite sprite) continue;
      foundSprite = true;
      if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out var objGuid, out long fileId)) continue;
      if (!string.Equals(objGuid, guid, StringComparison.OrdinalIgnoreCase)) continue;

      var spriteName = string.IsNullOrWhiteSpace(sprite.name) ? Path.GetFileNameWithoutExtension(assetPath) : sprite.name.Trim();
      var address = SpriteSliceAddressUtility.BuildSliceAddress(assetPath, spriteName);
      if (string.IsNullOrWhiteSpace(address)) continue;
      map[fileId] = address;
    }

    // Safe fallback to loading the main asset if it was imported as a single sprite
    if (!foundSprite) {
      var mainSprite = AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(assetPath);
      if (mainSprite != null) {
        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mainSprite, out var objGuid, out long fileId)) {
          if (string.Equals(objGuid, guid, StringComparison.OrdinalIgnoreCase)) {
            var spriteName = string.IsNullOrWhiteSpace(mainSprite.name) ? Path.GetFileNameWithoutExtension(assetPath) : mainSprite.name.Trim();
            var address = SpriteSliceAddressUtility.BuildSliceAddress(assetPath, spriteName);
            if (!string.IsNullOrWhiteSpace(address)) {
              map[fileId] = address;
            }
          }
        }
      }
    }

    // Check runtimeTextureAssetPathBySourceAssetPath for remapped paths
    if (state.runtimeTextureAssetPathBySourceAssetPath.TryGetValue(assetPath, out var remappedPath)) {
      var remappedGuid = AssetDatabase.AssetPathToGUID(remappedPath);
      if (!string.IsNullOrWhiteSpace(remappedGuid) && !state.addressCacheByGuid.ContainsKey(remappedGuid)) {
        // Populate remapped entry (don't recurse infinitely)
        state.addressCacheByGuid[remappedGuid] = map;
      }
    }

    return map;
  }

  static bool IsRuntimeTextureAssetPath(string assetPath) {
    var normalizedPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedPath)) return false;
    if (!normalizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return false;

    var extension = Path.GetExtension(normalizedPath);
    return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase);
  }

  static bool IsActiveRuntimeTextureAssetPath(string assetPath) {
    var normalizedPath = NormalizePath(assetPath);
    if (!IsRuntimeTextureAssetPath(normalizedPath)) return false;

    CacheTextureRoots();
    if (cachedTextureRootsSet == null) return false;

    foreach (var root in cachedTextureRootsSet) {
      if (string.Equals(normalizedPath, root, StringComparison.OrdinalIgnoreCase) ||
          normalizedPath.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
  }

  static string TryResolveSpriteAddressFromContext(Dictionary<long, string> byFileId, string context) {
    if (byFileId == null || byFileId.Count == 0 || string.IsNullOrWhiteSpace(context)) return "";
    var spaceIdx = context.IndexOf(' ');
    var trimmedContext = spaceIdx > 0 ? context.Substring(0, spaceIdx) : context;
    
    // Support both "category:label" and "category/label" format by extracting segment after the last colon or slash
    var lastColon = trimmedContext.LastIndexOf(':');
    var lastSlash = trimmedContext.LastIndexOf('/');
    var candidateIdx = Math.Max(lastColon, lastSlash);
    var candidate = candidateIdx >= 0 ? trimmedContext.Substring(candidateIdx + 1) : trimmedContext;

    foreach (var pair in byFileId) {
      if (string.IsNullOrWhiteSpace(pair.Value)) continue;
      var addrLeaf = pair.Value;
      if (!SpriteSliceAddressUtility.TryParseSliceAddress(addrLeaf, out _, out addrLeaf)) {
        var addrSlash = addrLeaf.LastIndexOf('/');
        if (addrSlash >= 0) addrLeaf = addrLeaf.Substring(addrSlash + 1);
      }
      if (string.Equals(addrLeaf, candidate, StringComparison.OrdinalIgnoreCase)) return pair.Value;
    }
    return "";
  }

  static bool TryResolveDerivedNormalAddress(BuildState state, string colorAddress, out string normalAddress) {
    normalAddress = "";
    if (string.IsNullOrWhiteSpace(colorAddress)) return false;

    if (state.derivedNormalAtlasPathByColorAtlas.TryGetValue(colorAddress, out var derivedNormal)) {
      if (!string.IsNullOrWhiteSpace(derivedNormal)) {
        normalAddress = derivedNormal;
        return true;
      }
      return false;
    }

    // Heuristic: replace "/Color/" or "_Color" with "/Normal/" or "_Normal" in address
    string candidate = null;
    if (colorAddress.Contains("/Color/", StringComparison.OrdinalIgnoreCase)) {
      candidate = colorAddress.Replace("/Color/", "/Normal/");
    }
    else if (colorAddress.EndsWith("_Color", StringComparison.OrdinalIgnoreCase)) {
      candidate = colorAddress.Substring(0, colorAddress.Length - "_Color".Length) + "_Normal";
    }

    if (!string.IsNullOrWhiteSpace(candidate)) {
      foreach (var guidEntry in state.addressCacheByGuid) {
        foreach (var pair in guidEntry.Value) {
          if (string.Equals(pair.Value, candidate, StringComparison.OrdinalIgnoreCase)) {
            state.derivedNormalAtlasPathByColorAtlas[colorAddress] = candidate;
            normalAddress = candidate;
            return true;
          }
        }
      }
    }

    state.derivedNormalAtlasPathByColorAtlas[colorAddress] = "";
    return false;
  }

}
#endif
