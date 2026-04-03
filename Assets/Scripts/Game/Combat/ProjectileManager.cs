using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ProjectileManager : MonoBehaviour {
  [Header("Pooling")]
  public Transform poolContainer;
  public int defaultPoolSize = 10;
  public bool autoResize = true;
  public bool prewarmPools = true;

  [Header("Homing")]
  public Transform enemyRoot;

  [Header("Debug")]
  public bool pauseEditorAfterFirstProjectileSpawnFrame;

  private readonly Dictionary<string, Pool> pools = new();
  private readonly Dictionary<string, AnimData> effectAnimations = new();
  private bool hasQueuedEditorPauseAfterFirstProjectileSpawn;

  void Awake() {
    poolContainer = transform;
    hasQueuedEditorPauseAfterFirstProjectileSpawn = false;
    BuildEffectAnimations();
    if (prewarmPools) {
      PrewarmPools();
    }
  }

  private void PrewarmPools() {
    foreach (var entry in Projectiles.EnumerateAll()) {
      var key = NormalizeProjectileKey(entry.Key);
      if (string.IsNullOrWhiteSpace(key) || entry.Value == null) continue;
      TryGetPool(key, out _);
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
      if (!TryGetProjectilePrefab(key, out var prefab)) continue;
      CollectProjectileStartupAddresses(prefab, key, warmFrames, outAddresses, seenAddresses, maxUniqueAddresses);
    }

    return Mathf.Max(outAddresses.Count - beforeCount, 0);
  }

  public int CollectPersistentStartupAssetAddresses(
    IReadOnlyList<string> projectileKeys,
    List<string> outAddresses,
    HashSet<string> seenAddresses = null,
    int maxUniqueAddresses = int.MaxValue
  ) {
    if (projectileKeys == null || projectileKeys.Count <= 0 || outAddresses == null || maxUniqueAddresses <= 0) {
      return 0;
    }

    var beforeCount = outAddresses.Count;
    for (var i = 0; i < projectileKeys.Count; i++) {
      if (outAddresses.Count >= maxUniqueAddresses) {
        break;
      }

      var key = NormalizeProjectileKey(projectileKeys[i]);
      if (string.IsNullOrWhiteSpace(key)) continue;
      TryAddAssetAddress(key, outAddresses, seenAddresses, maxUniqueAddresses);
    }

    return Mathf.Max(outAddresses.Count - beforeCount, 0);
  }

  public GameObject SpawnProjectile(string key, Vector3 position, Vector3 direction, Transform target = null, float? speedOverride = null) {
    key = NormalizeProjectileKey(key);
    if (string.IsNullOrWhiteSpace(key)) {
      Debug.LogWarning("[ProjectileManager] SpawnProjectile called with null or empty key.");
      return null;
    }
    if (!TryGetPool(key, out var pool)) return null;

    var spawnPosition = ResolveSpawnPosition(key, position, direction);
    var obj = pool.Spawn(spawnPosition, Quaternion.identity);
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

    TryQueueEditorPauseAfterFirstProjectileSpawn(key, spawnPosition, direction);
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
    key = NormalizeProjectileKey(key);
    if (string.IsNullOrWhiteSpace(key)) {
      pool = null;
      return false;
    }

    if (pools.TryGetValue(key, out pool)) return true;

    if (!TryGetProjectilePrefab(key, out var prefab)) {
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
    Projectiles.ReleaseDirectLoads();
  }

  static bool ShouldLogSpawnOffsetDebug() {
    return Application.isEditor || Debug.isDebugBuild;
  }

  bool IsEditorPauseAfterFirstProjectileSpawnEnabled() {
    return Application.isEditor && pauseEditorAfterFirstProjectileSpawnFrame;
  }

  static string NormalizeProjectileKey(string key) {
    return string.IsNullOrWhiteSpace(key) ? "" : key.Trim();
  }

  static bool TryGetProjectilePrefab(string key, out GameObject prefab) {
    prefab = null;
    if (Projectiles.TryGetPrefab(key, out prefab) && prefab != null) {
      return true;
    }

    Debug.LogWarning("[ProjectileManager] No prefab available for key '" + key + "'.");
    return false;
  }

  static Vector3 ResolveSpawnPosition(string key, Vector3 basePosition, Vector3 direction) {
    if (!Projectiles.TryGet(key, out var data) || data == null) {
      return basePosition;
    }

    var offset = ResolveSpawnOffset(data, direction);
    if (offset.sqrMagnitude <= 0.0001f) {
      return basePosition;
    }

    var resolvedPosition = basePosition + offset;
    if (ShouldLogSpawnOffsetDebug()) {
      Debug.Log(
        "[ProjectileManager] AppliedSpawnOffset" +
        " key='" + key + "'" +
        " base=" + basePosition +
        " offset=" + offset +
        " direction=" + direction +
        " resolved=" + resolvedPosition
      );
    }
    return resolvedPosition;
  }

  static Vector3 ResolveSpawnOffset(ProjectileData data, Vector3 direction) {
    if (data == null) {
      return Vector3.zero;
    }

    var xSign = direction.x < 0f ? -1f : 1f;
    return new Vector3(data.spawnOffsetX * xSign, data.spawnOffsetY, 0f);
  }

  static void TryAddAssetAddress(
    string key,
    List<string> outAddresses,
    HashSet<string> seenAddresses,
    int maxUniqueAddresses
  ) {
    if (outAddresses == null || maxUniqueAddresses <= 0 || outAddresses.Count >= maxUniqueAddresses) {
      return;
    }

    if (!Projectiles.TryGetPrefabAddress(key, out var address) || string.IsNullOrWhiteSpace(address)) {
      return;
    }

    if (seenAddresses != null && !seenAddresses.Add(address)) {
      return;
    }

    if (seenAddresses == null && ContainsAddress(outAddresses, address)) {
      return;
    }

    outAddresses.Add(address);
  }

  static bool ContainsAddress(List<string> addresses, string address) {
    if (addresses == null || string.IsNullOrWhiteSpace(address)) {
      return false;
    }

    for (var i = 0; i < addresses.Count; i++) {
      if (string.Equals(addresses[i], address, System.StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
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

  void TryQueueEditorPauseAfterFirstProjectileSpawn(string key, Vector3 spawnPosition, Vector3 direction) {
#if UNITY_EDITOR
    if (!IsEditorPauseAfterFirstProjectileSpawnEnabled() ||
        hasQueuedEditorPauseAfterFirstProjectileSpawn ||
        !Application.isPlaying) {
      return;
    }

    hasQueuedEditorPauseAfterFirstProjectileSpawn = true;
    Debug.Log(
      "[ProjectileManager] QueuePauseAfterFirstProjectileSpawnFrame" +
      " key='" + key + "'" +
      " frame=" + Time.frameCount +
      " spawn=" + spawnPosition +
      " direction=" + direction
    );
    StartCoroutine(PauseEditorAfterFirstProjectileSpawnFrameRoutine(key, spawnPosition, direction));
#endif
  }

#if UNITY_EDITOR
  IEnumerator PauseEditorAfterFirstProjectileSpawnFrameRoutine(string key, Vector3 spawnPosition, Vector3 direction) {
    yield return new WaitForEndOfFrame();
    if (!IsEditorPauseAfterFirstProjectileSpawnEnabled()) {
      yield break;
    }

    Debug.Log(
      "[ProjectileManager] PauseAfterFirstProjectileSpawnFrame" +
      " key='" + key + "'" +
      " frame=" + Time.frameCount +
      " spawn=" + spawnPosition +
      " direction=" + direction
    );
    Debug.Break();
  }
#endif
}
