using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class ProjectileData {
  public string prefabAddress;
  public float spawnOffsetX;
  public float spawnOffsetY;
  public Projectile.MovementType movementType = Projectile.MovementType.Linear;
  public float speed = 5f;
  public bool rotateToMovement = true;
  public float rotationOffsetDegrees;
  public bool loopAnimation = true;
  public string effectKeyOverride;
  public float lifetimeSeconds = 2f;
  public bool despawnOnHurtBoxHit = true;
  public bool despawnOnAnyCollision;
  public LayerMask collisionLayers = ~0;
  public bool ignoreSameRoot = true;

  GameObject cachedPrefab;
  AsyncOperationHandle<GameObject> cachedPrefabHandle;
  bool prefabLoadAttempted;

  public bool TryGetPrefab(string key, out GameObject prefab) {
    prefab = null;
    if (!TryGetPrefabAddress(out var address)) {
      Debug.LogWarning("[Projectiles] Missing prefab address for key '" + key + "'.");
      return false;
    }

    if (RuntimeAssetCache.TryGetLoaded<GameObject>(address, out prefab) && prefab != null) {
      return true;
    }

    prefab = cachedPrefab;
    if (prefab != null) {
      return true;
    }

    if (prefabLoadAttempted) {
      return false;
    }

    prefabLoadAttempted = true;
    var startedAt = Time.realtimeSinceStartup;
    try {
      cachedPrefabHandle = Addressables.LoadAssetAsync<GameObject>(address);
      cachedPrefab = cachedPrefabHandle.WaitForCompletion();
      prefab = cachedPrefab;
      if (cachedPrefabHandle.Status != AsyncOperationStatus.Succeeded || prefab == null) {
        var status = cachedPrefabHandle.Status.ToString();
        var error = cachedPrefabHandle.OperationException != null ? cachedPrefabHandle.OperationException.Message : "none";
        if (cachedPrefabHandle.IsValid()) {
          Addressables.Release(cachedPrefabHandle);
        }
        cachedPrefabHandle = default;
        cachedPrefab = null;
        Debug.LogWarning(
          "[Projectiles] Failed to load prefab for key '" + key +
          "' from Addressables address '" + address +
          "' status=" + status +
          " error='" + error + "'."
        );
        return false;
      }
    }
    catch (System.Exception ex) {
      if (cachedPrefabHandle.IsValid()) {
        Addressables.Release(cachedPrefabHandle);
      }
      cachedPrefabHandle = default;
      cachedPrefab = null;
      Debug.LogWarning(
        "[Projectiles] Exception loading prefab for key '" + key +
        "' from Addressables address '" + address +
        "' error='" + ex.Message + "'."
      );
      return false;
    }

    if (Application.isEditor || Debug.isDebugBuild) {
      var loadSeconds = Time.realtimeSinceStartup - startedAt;
      Debug.Log(
        "[Projectiles] LoadedProjectilePrefab" +
        " key='" + key + "'" +
        " address='" + address + "'" +
        " prefab='" + prefab.name + "'" +
        " load_s=" + loadSeconds.ToString("0.0000")
      );
    }

    return true;
  }

  public bool TryGetPrefabAddress(out string address) {
    address = ActiveContentRegistryRuntime.ResolveActiveContentAssetPath(
      string.IsNullOrWhiteSpace(prefabAddress) ? "" : prefabAddress.Trim()
    );
    return !string.IsNullOrWhiteSpace(address);
  }

  public void ReleaseDirectLoad() {
    if (!cachedPrefabHandle.IsValid()) {
      cachedPrefab = null;
      prefabLoadAttempted = false;
      return;
    }

    Addressables.Release(cachedPrefabHandle);
    cachedPrefabHandle = default;
    cachedPrefab = null;
    prefabLoadAttempted = false;
  }

  public bool IsPrefabLoaded() {
    if (cachedPrefab != null) return true;
    if (!TryGetPrefabAddress(out var address) || string.IsNullOrWhiteSpace(address)) return false;
    return RuntimeAssetCache.TryGetLoaded<GameObject>(address, out var prefab) && prefab != null;
  }
}

public static class Projectiles {
  public static Dictionary<string, ProjectileData> Things { get; } = new Dictionary<string, ProjectileData> {
    ["BlastBall"] = new ProjectileData {
      prefabAddress = "Assets/Prefabs/Projectiles/BlastBall.prefab",
      spawnOffsetX = 3f,
      spawnOffsetY = -2.61f,
      movementType = Projectile.MovementType.Linear,
      speed = 10f,
      rotateToMovement = true,
      rotationOffsetDegrees = 0f,
      loopAnimation = true,
      effectKeyOverride = "",
      lifetimeSeconds = 2f,
      despawnOnHurtBoxHit = true,
      despawnOnAnyCollision = false,
      collisionLayers = 1 << 7,
      ignoreSameRoot = true
    }
  };

  public static bool TryGet(string key, out ProjectileData data) {
    data = null;
    if (string.IsNullOrWhiteSpace(key)) {
      return false;
    }
    return Things.TryGetValue(key.Trim(), out data);
  }

  public static bool TryGetPrefab(string key, out GameObject prefab) {
    prefab = null;
    if (!TryGet(key, out var data) || data == null) {
      return false;
    }
    return data.TryGetPrefab(key, out prefab);
  }

  public static bool TryGetPrefabAddress(string key, out string address) {
    address = "";
    if (!TryGet(key, out var data) || data == null) {
      return false;
    }
    return data.TryGetPrefabAddress(out address);
  }

  public static IEnumerable<KeyValuePair<string, ProjectileData>> EnumerateAll() {
    foreach (var entry in Things) {
      yield return entry;
    }
  }

  public static void ReleaseDirectLoads() {
    foreach (var entry in EnumerateAll()) {
      entry.Value?.ReleaseDirectLoad();
    }
  }
}
