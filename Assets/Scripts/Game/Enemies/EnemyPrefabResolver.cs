using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public static class EnemyPrefabResolver {
  const string EnemyPrefabRoot = "Assets/Prefabs/Enemies/";
  const string PrefabExtension = ".prefab";

  static readonly Dictionary<string, GameObject> prefabCache = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, AsyncOperationHandle<GameObject>> prefabHandleCache = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> failedAddresses = new(StringComparer.OrdinalIgnoreCase);

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetRuntimeCache() {
    foreach (var handle in prefabHandleCache.Values) {
      if (handle.IsValid()) {
        Addressables.Release(handle);
      }
    }

    prefabCache.Clear();
    prefabHandleCache.Clear();
    failedAddresses.Clear();
  }

  public static bool TryResolve(string enemyType, out GameObject prefab) {
    prefab = null;
    var address = BuildAddress(enemyType);
    if (string.IsNullOrWhiteSpace(address)) return false;

    if (RuntimeAssetCache.TryGetLoaded<GameObject>(address, out prefab) && prefab != null) {
      return true;
    }

    if (prefabCache.TryGetValue(address, out prefab) && prefab != null) {
      return true;
    }

    if (failedAddresses.Contains(address)) return false;

    try {
      var handle = Addressables.LoadAssetAsync<GameObject>(address);
      var loadedPrefab = handle.WaitForCompletion();
      if (handle.Status == AsyncOperationStatus.Succeeded && loadedPrefab != null) {
        prefabCache[address] = loadedPrefab;
        prefabHandleCache[address] = handle;
        prefab = loadedPrefab;
        return true;
      }

      if (handle.IsValid()) {
        Addressables.Release(handle);
      }
    }
    catch (Exception ex) {
      Debug.LogError(
        "[EnemyPrefabResolver] Failed to load enemy prefab" +
        " enemy_type='" + NormalizeEnemyType(enemyType) + "'" +
        " address='" + address + "'" +
        " error='" + ex.Message + "'"
      );
    }

    failedAddresses.Add(address);
    Debug.LogError(
      "[EnemyPrefabResolver] Missing enemy prefab Addressables entry" +
      " enemy_type='" + NormalizeEnemyType(enemyType) + "'" +
      " address='" + address + "'"
    );
    return false;
  }

  static string BuildAddress(string enemyType) {
    var normalizedEnemyType = NormalizeEnemyType(enemyType);
    if (string.IsNullOrWhiteSpace(normalizedEnemyType) ||
        normalizedEnemyType.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) {
      return "";
    }

    return EnemyPrefabRoot + normalizedEnemyType + PrefabExtension;
  }

  static string NormalizeEnemyType(string enemyType) {
    return string.IsNullOrWhiteSpace(enemyType) ? "" : enemyType.Trim();
  }
}
