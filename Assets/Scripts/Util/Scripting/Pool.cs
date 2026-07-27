using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Pool {
  readonly struct SharedPoolKey : IEquatable<SharedPoolKey> {
    readonly EntityId prefabInstanceId;
    readonly SceneHandle sceneHandle;

    public SceneHandle SceneHandle => sceneHandle;

    public SharedPoolKey(GameObject prefab, SceneHandle sceneHandle) {
      prefabInstanceId = prefab.GetEntityId();
      this.sceneHandle = sceneHandle;
    }

    public bool Equals(SharedPoolKey other) {
      return prefabInstanceId.Equals(other.prefabInstanceId) &&
             sceneHandle == other.sceneHandle;
    }

    public override bool Equals(object obj) {
      return obj is SharedPoolKey other && Equals(other);
    }

    public override int GetHashCode() {
      unchecked {
        return (prefabInstanceId.GetHashCode() * 397) ^ sceneHandle.GetHashCode();
      }
    }
  }

  static readonly Dictionary<SharedPoolKey, Pool> sharedPools = new();
  static readonly List<SharedPoolKey> sharedPoolKeyScratch = new(8);

  public GameObject prefab;
  public int poolSize { get; set; }
  public bool autoResize;

  Queue<GameObject> pool = new Queue<GameObject>();
  List<GameObject> active = new List<GameObject>();
  HashSet<GameObject> activeSet = new HashSet<GameObject>();
  Transform container;
  Action<GameObject> onInstanceCreated;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetSharedPools() {
    SceneManager.sceneUnloaded -= ReleaseSharedPoolsForScene;
    sharedPools.Clear();
    sharedPoolKeyScratch.Clear();
    SceneManager.sceneUnloaded += ReleaseSharedPoolsForScene;
  }

  static void ReleaseSharedPoolsForScene(Scene scene) {
    sharedPoolKeyScratch.Clear();
    foreach (var pair in sharedPools) {
      if (pair.Key.SceneHandle == scene.handle) {
        sharedPoolKeyScratch.Add(pair.Key);
      }
    }

    for (var i = 0; i < sharedPoolKeyScratch.Count; i++) {
      var key = sharedPoolKeyScratch[i];
      if (sharedPools.TryGetValue(key, out var sharedPool)) {
        sharedPool.Clear();
      }
      sharedPools.Remove(key);
    }
    sharedPoolKeyScratch.Clear();
  }

  public static Pool GetShared(
    GameObject prefab,
    Transform container,
    int poolSize = 10,
    bool autoResize = true
  ) {
    if (prefab == null || poolSize <= 0) {
      return null;
    }

    var sceneHandle = container != null
      ? container.gameObject.scene.handle
      : SceneManager.GetActiveScene().handle;
    var key = new SharedPoolKey(prefab, sceneHandle);
    if (sharedPools.TryGetValue(key, out var existingPool)) {
      return existingPool;
    }

    var sharedPool = new Pool();
    sharedPool.Initialize(prefab, container, poolSize, autoResize);
    sharedPools[key] = sharedPool;
    return sharedPool;
  }

  public static void PrepareForLoading() {
    if (!Application.isPlaying) {
      return;
    }

    PoolDespawnScheduler.Prepare();
  }

  public void EnsureCapacity(int requiredCapacity) {
    EnsureCapacityIncremental(requiredCapacity, int.MaxValue);
  }

  public bool EnsureCapacityIncremental(int requiredCapacity, int maxNewInstances) {
    if (prefab == null) {
      return false;
    }

    requiredCapacity = Mathf.Max(requiredCapacity, 0);
    if (requiredCapacity <= poolSize) {
      return true;
    }
    if (active.Capacity < requiredCapacity) {
      active.Capacity = requiredCapacity;
    }

    var additionalCount = Mathf.Min(requiredCapacity - poolSize, Mathf.Max(maxNewInstances, 0));
    for (var i = 0; i < additionalCount; i++) {
      var go = GameObject.Instantiate(prefab, container);
      go.SetActive(false);
      onInstanceCreated?.Invoke(go);
      pool.Enqueue(go);
    }
    poolSize += additionalCount;
    return poolSize >= requiredCapacity;
  }

  public void InitializeEmpty(
    GameObject prefab,
    Transform container,
    int reservedCapacity,
    bool autoResize = true,
    Action<GameObject> onInstanceCreated = null
  ) {
    if (prefab == null) return;

    this.prefab = prefab;
    poolSize = 0;
    this.autoResize = autoResize;
    this.container = container;
    this.onInstanceCreated = onInstanceCreated;

    if (Application.isPlaying) {
      PoolDespawnScheduler.Prepare();
    }

    pool.Clear();
    active.Clear();
    activeSet.Clear();
    reservedCapacity = Mathf.Max(reservedCapacity, 0);
    if (active.Capacity < reservedCapacity) {
      active.Capacity = reservedCapacity;
    }
  }

  public void Initialize(
    GameObject prefab,
    Transform container,
    int poolSize = 10,
    bool autoResize = true,
    Action<GameObject> onInstanceCreated = null
  ) {
    if (prefab == null || poolSize <= 0) return;

    InitializeEmpty(prefab, container, poolSize, autoResize, onInstanceCreated);
    EnsureCapacity(poolSize);
  }

  public GameObject Spawn(Vector3 position, Quaternion rotation) {
    var obj = Acquire(position, rotation);
    Activate(obj);
    return obj;
  }

  public GameObject Acquire(Vector3 position, Quaternion rotation) {
    if (prefab == null) {
      Debug.LogError("[Pool] Cannot acquire - prefab is null.");
      return null;
    }

    GameObject obj = null;
    while (pool.Count > 0 && obj == null) {
      obj = pool.Dequeue();
    }

    if (obj == null && autoResize) {
      obj = GameObject.Instantiate(prefab, container);
      obj.SetActive(false);
      onInstanceCreated?.Invoke(obj);
      poolSize += 1;
    }
    else if (obj == null) {
      while (active.Count > 0 && active[0] == null) {
        activeSet.Remove(active[0]);
        active.RemoveAt(0);
      }

      if (active.Count <= 0) {
        Debug.LogWarning("[Pool] No objects available and autoResize is disabled.");
        return null;
      }

      obj = active[0];
      active.RemoveAt(0);
      activeSet.Remove(obj);
      PoolDespawnScheduler.Cancel(this, obj);
    }

    if (obj.activeSelf) {
      obj.SetActive(false);
    }
    obj.transform.SetPositionAndRotation(position, rotation);
    active.Add(obj);
    activeSet.Add(obj);
    return obj;
  }

  public void Activate(GameObject obj) {
    if (obj == null || !activeSet.Contains(obj) || obj.activeSelf) {
      return;
    }

    obj.SetActive(true);
  }

  public void Despawn(GameObject obj) {
    if (obj == null || !activeSet.Contains(obj)) return;

    PoolDespawnScheduler.Cancel(this, obj);
    obj.SetActive(false);
    obj.transform.SetParent(container);
    active.Remove(obj);
    activeSet.Remove(obj);
    pool.Enqueue(obj);
  }

  public void DespawnAfter(GameObject obj, float seconds) {
    if (obj == null || !activeSet.Contains(obj)) {
      return;
    }
    if (seconds <= 0f) {
      Despawn(obj);
      return;
    }

    PoolDespawnScheduler.Schedule(this, obj, seconds);
  }

  public void Clear() {
    while (active.Count > 0) {
      var obj = active[0];
      active.RemoveAt(0);
      if (obj != null) {
        GameObject.Destroy(obj);
      }
    }

    while (pool.Count > 0) {
      var obj = pool.Dequeue();
      if (obj != null) {
        GameObject.Destroy(obj);
      }
    }

    active.Clear();
    activeSet.Clear();
    pool.Clear();
  }

  public int ActiveCount {
    get { return active.Count; }
  }

  public int PooledCount {
    get { return pool.Count; }
  }
}

sealed class PoolDespawnScheduler : MonoBehaviour {
  struct Entry {
    public Pool pool;
    public GameObject target;
    public float expiresAt;
  }

  static PoolDespawnScheduler instance;
  readonly List<Entry> entries = new(64);

  public static void Prepare() {
    EnsureInstance();
  }

  public static void Schedule(Pool pool, GameObject target, float seconds) {
    if (pool == null || target == null) {
      return;
    }

    EnsureInstance();
    var expiresAt = Time.time + seconds;
    for (var i = 0; i < instance.entries.Count; i++) {
      var entry = instance.entries[i];
      if (entry.pool != pool || entry.target != target) {
        continue;
      }

      entry.expiresAt = expiresAt;
      instance.entries[i] = entry;
      return;
    }

    instance.entries.Add(new Entry {
      pool = pool,
      target = target,
      expiresAt = expiresAt
    });
  }

  public static void Cancel(Pool pool, GameObject target) {
    if (instance == null || pool == null || target == null) {
      return;
    }

    for (var i = instance.entries.Count - 1; i >= 0; i--) {
      var entry = instance.entries[i];
      if (entry.pool != pool || entry.target != target) {
        continue;
      }

      instance.RemoveAtSwapBack(i);
    }
  }

  static void EnsureInstance() {
    if (instance != null) {
      return;
    }

    var runner = new GameObject("Pool Despawn Scheduler");
    UnityEngine.Object.DontDestroyOnLoad(runner);
    instance = runner.AddComponent<PoolDespawnScheduler>();
  }

  void Update() {
    var now = Time.time;
    for (var i = entries.Count - 1; i >= 0; i--) {
      var entry = entries[i];
      if (entry.target == null) {
        RemoveAtSwapBack(i);
        continue;
      }
      if (now < entry.expiresAt) {
        continue;
      }

      RemoveAtSwapBack(i);
      entry.pool.Despawn(entry.target);
    }
  }

  void RemoveAtSwapBack(int index) {
    var lastIndex = entries.Count - 1;
    entries[index] = entries[lastIndex];
    entries.RemoveAt(lastIndex);
  }
}
