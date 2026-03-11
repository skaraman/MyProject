using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static class TrimmedSpriteOffsetResolver {
  [Serializable]
  sealed class TrimmedAtlasOffsetPayload {
    public List<TrimmedSpriteOffsetEntry> sprites = new();
  }

  [Serializable]
  sealed class TrimmedSpriteOffsetEntry {
    public string name;
    public PixelPoint offsetFromCellCenterPx;
  }

  [Serializable]
  struct PixelPoint {
    public float x;
    public float y;
  }

  sealed class AtlasOffsets {
    readonly Dictionary<string, Vector2> exactOffsetsBySpriteName = new(StringComparer.Ordinal);

    public void Set(string spriteName, Vector2 offsetPx) {
      if (string.IsNullOrWhiteSpace(spriteName)) return;
      exactOffsetsBySpriteName[spriteName] = offsetPx;
    }

    public bool TryGet(string spriteName, out Vector2 offsetPx) {
      offsetPx = Vector2.zero;
      var normalizedSpriteName = spriteName ?? "";
      if (string.IsNullOrWhiteSpace(normalizedSpriteName)) return false;
      if (exactOffsetsBySpriteName.TryGetValue(normalizedSpriteName, out offsetPx)) return true;

      foreach (var pair in exactOffsetsBySpriteName) {
        if (!SpriteSliceAddressUtility.HasEquivalentNumericLabel(pair.Key, normalizedSpriteName)) continue;
        offsetPx = pair.Value;
        return true;
      }

      return false;
    }
  }

  static readonly Dictionary<string, AtlasOffsets> loadedAtlasOffsets = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> missingAtlasOffsets = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, AsyncOperationHandle<TextAsset>> pendingLoads = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, List<Action>> pendingCallbacks = new(StringComparer.OrdinalIgnoreCase);

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetOnDomainReload() {
    foreach (var pair in pendingLoads) {
      if (!pair.Value.IsValid()) continue;
      Addressables.Release(pair.Value);
    }

    loadedAtlasOffsets.Clear();
    missingAtlasOffsets.Clear();
    pendingLoads.Clear();
    pendingCallbacks.Clear();
  }

  public static void InvalidateAtlas(string atlasAssetPath) {
    var normalizedAtlasPath = NormalizeAtlasPath(atlasAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedAtlasPath)) return;

    loadedAtlasOffsets.Remove(normalizedAtlasPath);
    missingAtlasOffsets.Remove(normalizedAtlasPath);

    if (pendingLoads.TryGetValue(normalizedAtlasPath, out var loadHandle)) {
      pendingLoads.Remove(normalizedAtlasPath);
      if (loadHandle.IsValid()) {
        Addressables.Release(loadHandle);
      }
    }

    pendingCallbacks.Remove(normalizedAtlasPath);
  }

  public static bool TryGetExactOffset(string sliceAddress, out Vector2 offsetPx, Action onReady = null) {
    offsetPx = Vector2.zero;
    if (!TryParseSliceAddress(sliceAddress, out var atlasAssetPath, out var spriteName)) return false;
    if (TryGetLoadedOffset(atlasAssetPath, spriteName, out offsetPx)) return true;
    if (missingAtlasOffsets.Contains(atlasAssetPath)) return false;

#if UNITY_EDITOR
    if (TryLoadEditorOffsets(atlasAssetPath)) {
      return TryGetLoadedOffset(atlasAssetPath, spriteName, out offsetPx);
    }

    if (Application.isEditor) return false;
#endif

    if (!Application.isPlaying) return false;
    RegisterPendingCallback(atlasAssetPath, onReady);
    StartRuntimeLoad(atlasAssetPath);
    return false;
  }

  public static bool TryGetExactLocalOffset(
    string sliceAddress,
    Sprite sprite,
    out Vector3 localOffset,
    bool flipX = false,
    bool flipY = false,
    Action onReady = null) {
    localOffset = Vector3.zero;
    if (sprite == null) return false;
    if (!TryGetExactOffset(sliceAddress, out var offsetPx, onReady)) return false;

    localOffset = ConvertOffsetPixelsToLocalUnits(offsetPx, sprite, flipX, flipY);
    return true;
  }

  public static Vector3 ConvertOffsetPixelsToLocalUnits(Vector2 offsetPx, Sprite sprite, bool flipX = false, bool flipY = false) {
    var pixelsPerUnit = sprite != null && sprite.pixelsPerUnit > 0f ? sprite.pixelsPerUnit : 100f;
    var x = offsetPx.x / pixelsPerUnit;
    var y = offsetPx.y / pixelsPerUnit;
    if (flipX) x = -x;
    if (flipY) y = -y;
    return new Vector3(x, y, 0f);
  }

  static bool TryGetLoadedOffset(string atlasAssetPath, string spriteName, out Vector2 offsetPx) {
    offsetPx = Vector2.zero;
    if (!loadedAtlasOffsets.TryGetValue(atlasAssetPath, out var atlasOffsets) || atlasOffsets == null) return false;
    return atlasOffsets.TryGet(spriteName, out offsetPx);
  }

#if UNITY_EDITOR
  static bool TryLoadEditorOffsets(string atlasAssetPath) {
    if (loadedAtlasOffsets.ContainsKey(atlasAssetPath)) return true;
    if (missingAtlasOffsets.Contains(atlasAssetPath)) return false;

    var metadataAssetPath = BuildMetadataAssetPath(atlasAssetPath);
    var metadataAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(metadataAssetPath);
    if (metadataAsset == null || string.IsNullOrWhiteSpace(metadataAsset.text)) {
      missingAtlasOffsets.Add(atlasAssetPath);
      return false;
    }

    if (!TryParseAtlasOffsets(metadataAsset.text, out var atlasOffsets)) {
      missingAtlasOffsets.Add(atlasAssetPath);
      return false;
    }

    loadedAtlasOffsets[atlasAssetPath] = atlasOffsets;
    return true;
  }
#endif

  static void StartRuntimeLoad(string atlasAssetPath) {
    if (string.IsNullOrWhiteSpace(atlasAssetPath)) return;
    if (loadedAtlasOffsets.ContainsKey(atlasAssetPath)) return;
    if (missingAtlasOffsets.Contains(atlasAssetPath)) return;
    if (pendingLoads.ContainsKey(atlasAssetPath)) return;

    var metadataAssetPath = BuildMetadataAssetPath(atlasAssetPath);
    var loadHandle = Addressables.LoadAssetAsync<TextAsset>(metadataAssetPath);
    pendingLoads[atlasAssetPath] = loadHandle;
    loadHandle.Completed += operation => CompleteRuntimeLoad(atlasAssetPath, operation);
  }

  static void CompleteRuntimeLoad(string atlasAssetPath, AsyncOperationHandle<TextAsset> operation) {
    pendingLoads.Remove(atlasAssetPath);

    if (operation.Status == AsyncOperationStatus.Succeeded &&
        operation.Result != null &&
        TryParseAtlasOffsets(operation.Result.text, out var atlasOffsets)) {
      loadedAtlasOffsets[atlasAssetPath] = atlasOffsets;
      missingAtlasOffsets.Remove(atlasAssetPath);
    } else {
      missingAtlasOffsets.Add(atlasAssetPath);
    }

    if (operation.IsValid()) {
      Addressables.Release(operation);
    }

    NotifyPendingCallbacks(atlasAssetPath);
  }

  static void RegisterPendingCallback(string atlasAssetPath, Action onReady) {
    if (onReady == null || string.IsNullOrWhiteSpace(atlasAssetPath)) return;
    if (!pendingCallbacks.TryGetValue(atlasAssetPath, out var callbacks) || callbacks == null) {
      callbacks = new List<Action>();
      pendingCallbacks[atlasAssetPath] = callbacks;
    }

    for (var i = 0; i < callbacks.Count; i++) {
      if (callbacks[i] == onReady) return;
    }

    callbacks.Add(onReady);
  }

  static void NotifyPendingCallbacks(string atlasAssetPath) {
    if (!pendingCallbacks.TryGetValue(atlasAssetPath, out var callbacks) || callbacks == null || callbacks.Count <= 0) {
      pendingCallbacks.Remove(atlasAssetPath);
      return;
    }

    pendingCallbacks.Remove(atlasAssetPath);
    for (var i = 0; i < callbacks.Count; i++) {
      callbacks[i]?.Invoke();
    }
  }

  static bool TryParseSliceAddress(string sliceAddress, out string atlasAssetPath, out string spriteName) {
    atlasAssetPath = "";
    spriteName = "";
    if (!SpriteSliceAddressUtility.TryParseSliceAddress(sliceAddress, out var parsedAtlasAssetPath, out var parsedSpriteName)) return false;

    atlasAssetPath = NormalizeAtlasPath(parsedAtlasAssetPath);
    spriteName = parsedSpriteName ?? "";
    return !string.IsNullOrWhiteSpace(atlasAssetPath) && !string.IsNullOrWhiteSpace(spriteName);
  }

  static bool TryParseAtlasOffsets(string jsonText, out AtlasOffsets atlasOffsets) {
    atlasOffsets = null;
    if (string.IsNullOrWhiteSpace(jsonText)) return false;

    TrimmedAtlasOffsetPayload payload;
    try {
      payload = JsonUtility.FromJson<TrimmedAtlasOffsetPayload>(jsonText);
    }
    catch {
      return false;
    }

    if (payload == null) return false;

    atlasOffsets = new AtlasOffsets();
    if (payload.sprites == null) return true;

    for (var i = 0; i < payload.sprites.Count; i++) {
      var sprite = payload.sprites[i];
      if (sprite == null || string.IsNullOrWhiteSpace(sprite.name)) continue;
      atlasOffsets.Set(sprite.name, new Vector2(sprite.offsetFromCellCenterPx.x, sprite.offsetFromCellCenterPx.y));
    }

    return true;
  }

  static string BuildMetadataAssetPath(string atlasAssetPath) {
    return Path.ChangeExtension(atlasAssetPath, ".json").Replace("\\", "/");
  }

  static string NormalizeAtlasPath(string atlasAssetPath) {
    if (string.IsNullOrWhiteSpace(atlasAssetPath)) return "";
    return atlasAssetPath.Trim().Replace("\\", "/");
  }
}
