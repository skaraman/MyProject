#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

static class GeneratedAtlasImportMetadataStore {
  const string UserDataPrefix = "[GeneratedAtlasImportMetadata]\n";

  public static bool TryRead(string atlasAssetPath, out string jsonText) {
    jsonText = "";
    var normalizedAtlasAssetPath = TrimmedAtlasExporterWindow.NormalizeAssetPath(atlasAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedAtlasAssetPath)) {
      return false;
    }

    var importer = AssetImporter.GetAtPath(normalizedAtlasAssetPath);
    if (importer == null) {
      return false;
    }

    return TryExtract(importer.userData, out jsonText);
  }

  public static bool TryReadForRuntimeMetadata(string runtimeMetadataAssetPath, out string jsonText) {
    jsonText = "";
    if (!TryResolveAtlasAssetPath(runtimeMetadataAssetPath, out var atlasAssetPath)) {
      return false;
    }

    return TryRead(atlasAssetPath, out jsonText);
  }

  public static bool TryWrite(string atlasAssetPath, string jsonText, bool forceReimport, out string error) {
    return TryWrite(atlasAssetPath, jsonText, forceReimport, out _, out error);
  }

  public static bool TryWrite(string atlasAssetPath, string jsonText, bool forceReimport, out bool changed, out string error) {
    changed = false;
    error = "";
    var normalizedAtlasAssetPath = TrimmedAtlasExporterWindow.NormalizeAssetPath(atlasAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedAtlasAssetPath)) {
      error = "Missing atlas asset path.";
      return false;
    }

    var importer = ResolveImporterForWrite(normalizedAtlasAssetPath);
    if (importer == null) {
      error = "Could not resolve atlas importer: " + normalizedAtlasAssetPath;
      return false;
    }

    var storedValue = BuildStoredValue(jsonText);
    changed = !string.Equals(importer.userData, storedValue, StringComparison.Ordinal);
    if (changed) {
      importer.userData = storedValue;
    }

    if (forceReimport) {
      if (changed) {
        importer.SaveAndReimport();
      }
      return true;
    }

    if (changed) {
      AssetDatabase.WriteImportSettingsIfDirty(normalizedAtlasAssetPath);
    }

    return true;
  }

  static AssetImporter ResolveImporterForWrite(string atlasAssetPath) {
    var importer = AssetImporter.GetAtPath(atlasAssetPath);
    if (importer != null) {
      return importer;
    }

    var fullAtlasPath = Path.GetFullPath(atlasAssetPath);
    if (!File.Exists(fullAtlasPath)) {
      return null;
    }

    AssetDatabase.ImportAsset(
      atlasAssetPath,
      ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

    return AssetImporter.GetAtPath(atlasAssetPath);
  }

  public static bool TryBatchReimport(string contextPath, List<string> atlasAssetPaths, out string error) {
    error = "";
    if (atlasAssetPaths == null || atlasAssetPaths.Count <= 0) {
      return true;
    }

    var assetEditingStarted = false;
    try {
      AssetDatabase.StartAssetEditing();
      assetEditingStarted = true;

      for (var i = 0; i < atlasAssetPaths.Count; i++) {
        var atlasAssetPath = TrimmedAtlasExporterWindow.NormalizeAssetPath(atlasAssetPaths[i]);
        if (string.IsNullOrWhiteSpace(atlasAssetPath)) {
          continue;
        }

        AssetDatabase.ImportAsset(
          atlasAssetPath,
          ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
      }
    }
    catch (Exception ex) {
      error =
        "Failed to batch reimport generated atlases for '" +
        (contextPath ?? "") +
        "': " + ex.Message;
      return false;
    }
    finally {
      if (assetEditingStarted) {
        AssetDatabase.StopAssetEditing();
      }
    }

    return true;
  }

  static bool TryResolveAtlasAssetPath(string runtimeMetadataAssetPath, out string atlasAssetPath) {
    atlasAssetPath = "";
    var normalizedRuntimeMetadataAssetPath = TrimmedAtlasExporterWindow.ResolveRuntimeMetadataAssetPath(runtimeMetadataAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedRuntimeMetadataAssetPath)) {
      return false;
    }

    var pngAtlasAssetPath = TrimmedAtlasExporterWindow.NormalizeAssetPath(Path.ChangeExtension(normalizedRuntimeMetadataAssetPath, ".png"));
    if (AssetImporter.GetAtPath(pngAtlasAssetPath) != null || File.Exists(Path.GetFullPath(pngAtlasAssetPath))) {
      atlasAssetPath = pngAtlasAssetPath;
      return true;
    }

    var jpgAtlasAssetPath = TrimmedAtlasExporterWindow.NormalizeAssetPath(Path.ChangeExtension(normalizedRuntimeMetadataAssetPath, ".jpg"));
    if (AssetImporter.GetAtPath(jpgAtlasAssetPath) != null || File.Exists(Path.GetFullPath(jpgAtlasAssetPath))) {
      atlasAssetPath = jpgAtlasAssetPath;
      return true;
    }

    var jpegAtlasAssetPath = TrimmedAtlasExporterWindow.NormalizeAssetPath(Path.ChangeExtension(normalizedRuntimeMetadataAssetPath, ".jpeg"));
    if (AssetImporter.GetAtPath(jpegAtlasAssetPath) != null || File.Exists(Path.GetFullPath(jpegAtlasAssetPath))) {
      atlasAssetPath = jpegAtlasAssetPath;
      return true;
    }

    return false;
  }

  static string BuildStoredValue(string jsonText) {
    return UserDataPrefix + (jsonText ?? "");
  }

  static bool TryExtract(string userData, out string jsonText) {
    jsonText = "";
    if (string.IsNullOrWhiteSpace(userData)) {
      return false;
    }

    if (!userData.StartsWith(UserDataPrefix, StringComparison.Ordinal)) {
      return false;
    }

    jsonText = userData.Substring(UserDataPrefix.Length);
    return !string.IsNullOrWhiteSpace(jsonText);
  }
}
#endif
