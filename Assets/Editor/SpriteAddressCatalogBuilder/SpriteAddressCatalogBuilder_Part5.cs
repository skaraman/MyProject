#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static partial class SpriteIndexBuilder {
  static HashSet<string> CollectRequestedLibraryNames(
    Dictionary<string, string> librariesByKey,
    Dictionary<string, string> guidToLibraryName,
    SpriteStreamingInclude includeAsset,
    Dictionary<string, List<string>> requestedLibraryReferences
  ) {
    var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (librariesByKey != null) {
      foreach (var key in librariesByKey.Keys) {
        if (string.IsNullOrWhiteSpace(key)) continue;
        if (IsNormalVariantLibraryName(key, librariesByKey)) continue;
        var normalizedLibraryLibraryName = SpriteAddressResolver.NormalizeNamePart(key);
        if (!string.IsNullOrWhiteSpace(normalizedLibraryLibraryName)) {
          result.Add(normalizedLibraryLibraryName);
        }
      }
    }

    var spriteWithNormalsGuid = AssetDatabase.AssetPathToGUID(BuilderConfig.SpriteWithNormalsScriptPath);

    var activeStageRoots = ContentPackPipeline.GetActiveStageAssetRoots();
    if (activeStageRoots.Count > 0) {
      for (var i = 0; i < activeStageRoots.Count; i++) {
        CollectLibraryNamesFromFiles(activeStageRoots[i], "*.unity", spriteWithNormalsGuid, guidToLibraryName, result, requestedLibraryReferences);
        CollectLibraryNamesFromFiles(activeStageRoots[i], "*.prefab", spriteWithNormalsGuid, guidToLibraryName, result, requestedLibraryReferences);
      }
    }
    else {
      CollectLibraryNamesFromFiles("Assets", "*.unity", spriteWithNormalsGuid, guidToLibraryName, result, requestedLibraryReferences);
      CollectLibraryNamesFromFiles("Assets", "*.prefab", spriteWithNormalsGuid, guidToLibraryName, result, requestedLibraryReferences);
    }

    if (includeAsset != null && includeAsset.libraryNames != null) {
      for (var i = 0; i < includeAsset.libraryNames.Count; i++) {
        var normalized = SpriteAddressResolver.NormalizeNamePart(includeAsset.libraryNames[i]);
        if (!string.IsNullOrWhiteSpace(normalized) && IsKnownActiveLibraryName(normalized, librariesByKey)) {
          result.Add(normalized);
          AddRequestedLibraryReference(
            requestedLibraryReferences,
            normalized,
            "SpriteStreamingInclude entry in '" + NormalizePath(BuilderConfig.IncludeAssetPath) + "'");
        }
      }
    }

    return result;
  }

  static bool IsNormalVariantLibraryName(string key, Dictionary<string, string> librariesByKey) {
    if (string.IsNullOrWhiteSpace(key) || librariesByKey == null) return false;
    if (!key.EndsWith("N", StringComparison.OrdinalIgnoreCase)) return false;
    if (key.Length <= 1) return false;

    var candidateColorLibraryName = key.Substring(0, key.Length - 1);
    return librariesByKey.ContainsKey(candidateColorLibraryName);
  }

  static bool IsKnownActiveLibraryName(string libraryName, Dictionary<string, string> librariesByKey) {
    var normalized = SpriteAddressResolver.NormalizeNamePart(libraryName);
    if (string.IsNullOrWhiteSpace(normalized) || librariesByKey == null) return false;
    if (librariesByKey.ContainsKey(normalized)) return true;

    var slash = normalized.LastIndexOf('/');
    if (slash < 0 || slash >= normalized.Length - 1) return false;
    var leafName = normalized.Substring(slash + 1);
    return librariesByKey.ContainsKey(leafName);
  }

  static void CollectLibraryNamesFromFiles(
    string rootPath,
    string pattern,
    string spriteWithNormalsGuid,
    Dictionary<string, string> guidToLibraryName,
    HashSet<string> target,
    Dictionary<string, List<string>> requestedLibraryReferences
  ) {
    var physicalRootPath = ContentPackPipeline.GetPhysicalPath(rootPath);
    if (string.IsNullOrWhiteSpace(physicalRootPath) || !Directory.Exists(physicalRootPath)) return;
    var files = Directory.GetFiles(physicalRootPath, pattern, SearchOption.AllDirectories);
    Array.Sort(files, StringComparer.Ordinal);

    for (var i = 0; i < files.Length; i++) {
      var assetPath = ContentPackPipeline.ToProjectAssetPath(files[i]);
      CollectLibraryNamesFromSerializedFile(NormalizePath(assetPath), spriteWithNormalsGuid, guidToLibraryName, target, requestedLibraryReferences);
    }
  }

  static void CollectLibraryNamesFromSerializedFile(
    string path,
    string spriteWithNormalsGuid,
    Dictionary<string, string> guidToLibraryName,
    HashSet<string> target,
    Dictionary<string, List<string>> requestedLibraryReferences
  ) {
    var physicalPath = ContentPackPipeline.GetPhysicalPath(path);
    if (string.IsNullOrWhiteSpace(physicalPath) || !File.Exists(physicalPath)) return;

    // Fast pre-filter using memory/CPU efficient contains
    var text = File.ReadAllText(physicalPath);
    if ((string.IsNullOrEmpty(spriteWithNormalsGuid) || !text.Contains(spriteWithNormalsGuid)) &&
        !text.Contains("SpriteWithNormals")) {
      return;
    }

    var gameObjectNameByFileId = new Dictionary<string, string>(StringComparer.Ordinal);
    var insideMonoBehaviour = false;
    var insideSpriteWithNormals = false;
    var currentMonoBehaviourGameObjectFileId = "";
    var pendingGameObjectFileId = "";
    var pendingLibraryName = "";
    var pendingColorKey = "";
    var pendingColorLibraryGuid = "";

    var insideGameObject = false;
    var currentGameObjectFileId = "";

    void BeginSpriteWithNormalsBlock() {
      insideSpriteWithNormals = true;
      pendingGameObjectFileId = currentMonoBehaviourGameObjectFileId;
      pendingLibraryName = "";
      pendingColorKey = "";
      pendingColorLibraryGuid = "";
    }

    void FlushPending() {
      if (!insideSpriteWithNormals) return;
      insideSpriteWithNormals = false;

      var resolved = pendingLibraryName;
      if (string.IsNullOrWhiteSpace(resolved)) resolved = pendingColorKey;
      if (string.IsNullOrWhiteSpace(resolved) &&
          !string.IsNullOrWhiteSpace(pendingColorLibraryGuid) &&
          guidToLibraryName.TryGetValue(pendingColorLibraryGuid, out var mappedLibraryName)) {
        resolved = mappedLibraryName;
      }

      var normalized = SpriteAddressResolver.NormalizeNamePart(resolved);
      if (!string.IsNullOrWhiteSpace(normalized)) {
        target.Add(normalized);
        AddRequestedLibraryReference(
          requestedLibraryReferences,
          normalized,
          BuildRequestedLibraryReference(path, pendingGameObjectFileId, gameObjectNameByFileId));
      }

      pendingGameObjectFileId = "";
      pendingLibraryName = "";
      pendingColorKey = "";
      pendingColorLibraryGuid = "";
    }

    using (var reader = new StreamReader(physicalPath, Encoding.UTF8)) {
      string line;
      while ((line = reader.ReadLine()) != null) {
        if (line.StartsWith("--- !u!", StringComparison.Ordinal)) {
          FlushPending();
          
          if (TryReadSerializedObjectHeader(line, out var classId, out var fileId)) {
            insideGameObject = (classId == 1);
            currentGameObjectFileId = insideGameObject ? fileId : "";
            
            insideMonoBehaviour = (classId == 114);
            currentMonoBehaviourGameObjectFileId = "";
          } else {
            insideGameObject = false;
            currentGameObjectFileId = "";
            insideMonoBehaviour = false;
            currentMonoBehaviourGameObjectFileId = "";
          }
          continue;
        }

        if (insideGameObject && !string.IsNullOrEmpty(currentGameObjectFileId)) {
          var trimmed = line.Trim();
          if (TryReadScalar(trimmed, "m_Name", out var gameObjectName)) {
            if (!string.IsNullOrWhiteSpace(gameObjectName)) {
              gameObjectNameByFileId[currentGameObjectFileId] = gameObjectName;
            }
            insideGameObject = false;
            currentGameObjectFileId = "";
          }
          continue;
        }

        if (!insideMonoBehaviour) continue;

        var trimmedLine = line.Trim();

        if (TryReadFileIdReference(trimmedLine, "m_GameObject", out var gameObjectFileId)) {
          currentMonoBehaviourGameObjectFileId = gameObjectFileId;
        }

        if (!insideSpriteWithNormals &&
            !string.IsNullOrWhiteSpace(spriteWithNormalsGuid) &&
            trimmedLine.Contains("guid:") &&
            trimmedLine.StartsWith("m_Script:", StringComparison.Ordinal)) {
          var scriptGuidMatch = guidRegex.Match(trimmedLine);
          if (scriptGuidMatch.Success &&
              string.Equals(scriptGuidMatch.Groups[1].Value, spriteWithNormalsGuid, StringComparison.OrdinalIgnoreCase)) {
            BeginSpriteWithNormalsBlock();
            continue;
          }
        }

        if (!insideSpriteWithNormals &&
            trimmedLine.StartsWith("m_EditorClassIdentifier:", StringComparison.Ordinal) &&
            trimmedLine.Contains("SpriteWithNormals", StringComparison.Ordinal)) {
          BeginSpriteWithNormalsBlock();
          continue;
        }

        if (!insideSpriteWithNormals) continue;

        if (TryReadScalar(trimmedLine, "libraryName", out var libraryNameValue) ||
            TryReadScalar(trimmedLine, "LibraryName", out libraryNameValue) ||
            TryReadScalar(trimmedLine, "_libraryName", out libraryNameValue)) {
          pendingLibraryName = libraryNameValue;
          continue;
        }

        if (TryReadScalar(trimmedLine, "colorKey", out var colorKeyValue)) {
          pendingColorKey = colorKeyValue;
          continue;
        }

        if (trimmedLine.Contains("guid:") &&
            trimmedLine.StartsWith("colorLibrary:", StringComparison.Ordinal)) {
          var guidMatch = guidRegex.Match(trimmedLine);
          if (guidMatch.Success) {
            pendingColorLibraryGuid = guidMatch.Groups[1].Value;
          }
        }
      }
    }

    FlushPending();
  }

  static void AddRequestedLibraryReference(
    Dictionary<string, List<string>> requestedLibraryReferences,
    string requestedLibraryName,
    string reference
  ) {
    if (requestedLibraryReferences == null ||
        string.IsNullOrWhiteSpace(requestedLibraryName) ||
        string.IsNullOrWhiteSpace(reference)) {
      return;
    }

    if (!requestedLibraryReferences.TryGetValue(requestedLibraryName, out var references)) {
      references = new List<string>();
      requestedLibraryReferences[requestedLibraryName] = references;
    }

    if (!ContainsIgnoreCase(references, reference)) {
      references.Add(reference);
    }
  }

  static string BuildRequestedLibraryReference(string assetPath, string gameObjectFileId, Dictionary<string, string> gameObjectNameByFileId) {
    var normalizedPath = NormalizePath(assetPath);
    if (!string.IsNullOrWhiteSpace(gameObjectFileId) &&
        gameObjectNameByFileId != null &&
        gameObjectNameByFileId.TryGetValue(gameObjectFileId, out var gameObjectName) &&
        !string.IsNullOrWhiteSpace(gameObjectName)) {
      return "GameObject '" + gameObjectName + "' in '" + normalizedPath + "'";
    }

    if (!string.IsNullOrWhiteSpace(gameObjectFileId)) {
      return "GameObject fileID '" + gameObjectFileId + "' in '" + normalizedPath + "'";
    }

    return "SpriteWithNormals reference in '" + normalizedPath + "'";
  }

  static string BuildRequestedLibraryReferenceSuffix(
    string requestedLibraryName,
    Dictionary<string, List<string>> requestedLibraryReferences
  ) {
    if (requestedLibraryReferences == null ||
        string.IsNullOrWhiteSpace(requestedLibraryName) ||
        !requestedLibraryReferences.TryGetValue(requestedLibraryName, out var references) ||
        references == null ||
        references.Count == 0) {
      return "";
    }

    references.Sort(StringComparer.OrdinalIgnoreCase);
    var shownCount = Math.Min(3, references.Count);
    var shownReferences = references.Take(shownCount).ToList();
    var summary = " Referenced by " + string.Join("; ", shownReferences);
    if (references.Count > shownCount) {
      summary += "; and " + (references.Count - shownCount) + " more";
    }

    return summary + ".";
  }

}
#endif
