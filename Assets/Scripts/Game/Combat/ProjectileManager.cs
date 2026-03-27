using System.Collections.Generic;
using CustomInspector;
using UnityEngine;

public class ProjectileManager : MonoBehaviour {
  [Header("Prefabs")]
  public SerializableSortedDictionary<string, GameObject> projectilePrefabs = new();

  [Header("Pooling")]
  public Transform poolContainer;
  public int defaultPoolSize = 10;
  public bool autoResize = true;
  public bool prewarmPools = true;

  [Header("Homing")]
  public Transform enemyRoot;

  private readonly Dictionary<string, Pool> pools = new();
  private readonly Dictionary<string, AnimData> effectAnimations = new();

  void Awake() {
    poolContainer = transform;
    BuildEffectAnimations();
    if (prewarmPools) {
      PrewarmPools();
    }
  }

  private void PrewarmPools() {
    if (projectilePrefabs == null || projectilePrefabs.Count == 0) return;
    foreach (var (key, prefab) in projectilePrefabs) {
      if (string.IsNullOrEmpty(key) || prefab == null) continue;
      CreatePool(key, prefab);
    }
  }

  private void BuildEffectAnimations() {
    effectAnimations.Clear();
    AddEffectAnimations(Effects.Esperanza);
    AddEffectAnimations(Effects.Things);
    AddEffectAnimations(Effects.Imp);
  }

  private void AddEffectAnimations(Dictionary<string, EffectData> effects) {
    if (effects == null) return;
    foreach (var kvp in effects) {
      if (string.IsNullOrEmpty(kvp.Key) || kvp.Value == null) continue;
      effectAnimations[kvp.Key] = new AnimData {
        start = kvp.Value.start,
        end = kvp.Value.end,
        // Effect durations are in seconds; AnimationController uses milliseconds.
        duration = kvp.Value.duration * 1000f
      };
    }
  }

  public int EnsurePoolsReady(IReadOnlyList<string> projectileKeys) {
    if (projectileKeys == null || projectileKeys.Count <= 0) {
      return 0;
    }

    var preparedCount = 0;
    for (var i = 0; i < projectileKeys.Count; i++) {
      var key = NormalizeProjectileKey(projectileKeys[i]);
      if (string.IsNullOrWhiteSpace(key)) continue;
      if (!TryGetPool(key, out _)) continue;
      preparedCount++;
    }

    return preparedCount;
  }

  public int CollectPersistentStartupAddresses(
    IReadOnlyList<string> projectileKeys,
    List<string> outAddresses,
    HashSet<string> seenAddresses = null,
    int maxUniqueAddresses = int.MaxValue,
    int framesPerProjectile = 1
  ) {
    if (projectileKeys == null || projectileKeys.Count <= 0 || outAddresses == null || maxUniqueAddresses <= 0) {
      return 0;
    }

    var beforeCount = outAddresses.Count;
    var warmFrames = Mathf.Max(framesPerProjectile, 1);
    for (var i = 0; i < projectileKeys.Count; i++) {
      if (outAddresses.Count >= maxUniqueAddresses) {
        break;
      }

      var key = NormalizeProjectileKey(projectileKeys[i]);
      if (string.IsNullOrWhiteSpace(key)) continue;
      if (!projectilePrefabs.TryGetValue(key, out var prefab) || prefab == null) continue;
      CollectProjectileStartupAddresses(prefab, key, warmFrames, outAddresses, seenAddresses, maxUniqueAddresses);
    }

    return Mathf.Max(outAddresses.Count - beforeCount, 0);
  }

  public GameObject SpawnProjectile(string key, Vector3 position, Vector3 direction, Transform target = null, float? speedOverride = null) {
    if (string.IsNullOrEmpty(key)) {
      Debug.LogWarning("[ProjectileManager] SpawnProjectile called with null or empty key.");
      return null;
    }
    if (!TryGetPool(key, out var pool)) return null;

    var obj = pool.Spawn(position, Quaternion.identity);
    if (obj == null) {
      Debug.LogError($"[ProjectileManager] Failed to spawn projectile from pool '{key}'.");
      return null;
    }

    var projectile = obj.GetComponent<Projectile>();
    if (projectile != null) {
      projectile.ConfigureAnimationData(effectAnimations);
      projectile.Launch(this, key, direction, target, speedOverride);
    }
    else {
      Debug.LogWarning($"[ProjectileManager] Spawned prefab '{key}' without Projectile component.");
    }
    return obj;
  }

  public void DespawnProjectile(Projectile projectile) {
    if (projectile == null) return;
    DespawnProjectile(projectile.PoolKey, projectile.gameObject);
  }

  public void DespawnProjectile(string key, GameObject projectile) {
    if (projectile == null) return;

    if (string.IsNullOrEmpty(key)) {
      projectile.SetActive(false);
      return;
    }

    if (!pools.TryGetValue(key, out var pool)) {
      projectile.SetActive(false);
      return;
    }

    pool.Despawn(projectile);
  }

  private bool TryGetPool(string key, out Pool pool) {
    if (pools.TryGetValue(key, out pool)) return true;

    if (projectilePrefabs == null || !projectilePrefabs.TryGetValue(key, out var prefab) || prefab == null) {
      Debug.LogWarning($"[ProjectileManager] No prefab registered for key '{key}'.");
      return false;
    }

    pool = CreatePool(key, prefab);
    return pool != null;
  }

  private Pool CreatePool(string key, GameObject prefab) {
    if (poolContainer == null) {
      Debug.LogError("[ProjectileManager] Pool container is null when creating pool.");
      return null;
    }

    var pool = new Pool();
    pool.Initialize(prefab, poolContainer, defaultPoolSize, autoResize);
    pools[key] = pool;
    return pool;
  }

  public void ClearAllPools() {
    foreach (var pool in pools.Values) {
      pool.Clear();
    }
    pools.Clear();
  }

  private void OnDestroy() {
    ClearAllPools();
  }

  static string NormalizeProjectileKey(string key) {
    return string.IsNullOrWhiteSpace(key) ? "" : key.Trim();
  }

  static void CollectProjectileStartupAddresses(
    GameObject prefab,
    string key,
    int warmFrames,
    List<string> outAddresses,
    HashSet<string> seenAddresses,
    int maxUniqueAddresses
  ) {
    if (prefab == null || outAddresses == null || maxUniqueAddresses <= 0) {
      return;
    }

    var spriteTarget = prefab.GetComponentInChildren<SpriteWithNormals>(true);
    if (spriteTarget == null) {
      return;
    }

    var startFrame = spriteTarget.IsAnimation ? 1 : 0;
    var endFrame = spriteTarget.IsAnimation ? Mathf.Max(startFrame, startFrame + warmFrames - 1) : 0;
    var category = spriteTarget.IsAnimation ? key : spriteTarget.category;
    spriteTarget.CollectAnimationWindowAddresses(
      category,
      startFrame,
      endFrame,
      0,
      outAddresses,
      seenAddresses,
      maxUniqueAddresses
    );
  }

  public Transform FindNearestEnemyTarget(Vector3 origin) {
    if (enemyRoot == null) return null;
    EnemyInfo[] enemies = enemyRoot.GetComponentsInChildren<EnemyInfo>(false);
    if (enemies == null || enemies.Length == 0) return null;

    Transform closest = null;
    float closestSqr = float.MaxValue;
    foreach (var info in enemies) {
      if (info == null || !info.gameObject.activeInHierarchy) continue;
      var t = info.transform;
      var diff = t.position - origin;
      float sqr = diff.sqrMagnitude;
      if (sqr < closestSqr) {
        closestSqr = sqr;
        closest = t;
      }
    }
    return closest;
  }
}
