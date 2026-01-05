using System.Collections.Generic;
using UnityEngine;

public class Pool {
  public GameObject prefab;
  public int poolSize { get; set; }
  public bool autoResize;

  Queue<GameObject> pool = new Queue<GameObject>();
  List<GameObject> active = new List<GameObject>();
  HashSet<GameObject> activeSet = new HashSet<GameObject>();
  Transform container;

  public void Initialize(GameObject prefab, Transform container, int poolSize = 10, bool autoResize = true) {
    if (prefab == null || container == null || poolSize <= 0) return;

    this.prefab = prefab;
    this.poolSize = poolSize;
    this.autoResize = autoResize;
    this.container = container;

    pool.Clear();
    active.Clear();
    activeSet.Clear();

    for (int i = 0; i < poolSize; i++) {
      var go = GameObject.Instantiate(prefab, container);
      go.SetActive(false);
      pool.Enqueue(go);
    }
  }

  public GameObject Spawn(Vector3 position, Quaternion rotation) {
    if (prefab == null || container == null) {
      Debug.LogError("[Pool] Cannot spawn - prefab or container is null.");
      return null;
    }

    GameObject obj;
    if (pool.Count > 0) {
      obj = pool.Dequeue();
    }
    else if (autoResize) {
      obj = GameObject.Instantiate(prefab, container);
    }
    else {
      if (active.Count == 0) {
        Debug.LogWarning("[Pool] No objects available and autoResize is disabled.");
        return null;
      }
      obj = active[0];
      active.RemoveAt(0);
      activeSet.Remove(obj);
    }

    obj.transform.SetPositionAndRotation(position, rotation);
    obj.SetActive(true);
    active.Add(obj);
    activeSet.Add(obj);
    return obj;
  }

  public void Despawn(GameObject obj) {
    if (obj == null || !activeSet.Contains(obj)) return;

    obj.SetActive(false);
    obj.transform.SetParent(container);
    active.Remove(obj);
    activeSet.Remove(obj);
    pool.Enqueue(obj);
  }

  public void Clear() {
    // Despawn all active objects first
    while (active.Count > 0) {
      var obj = active[0];
      if (obj != null) {
        obj.SetActive(false);
        active.RemoveAt(0);
      }
    }

    // Destroy all pooled objects
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