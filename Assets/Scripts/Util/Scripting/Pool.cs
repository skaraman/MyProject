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
    if (prefab == null || poolSize <= 0) return;
    this.prefab = prefab;
    this.poolSize = poolSize;
    this.autoResize = autoResize;

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
    GameObject obj;
    if (pool.Count > 0) {
      obj = pool.Dequeue();
    }
    else if (autoResize) {
      obj = GameObject.Instantiate(prefab, container);
    }
    else {
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
    if (!activeSet.Contains(obj)) return;
    obj.SetActive(false);
    obj.transform.SetParent(container);
    active.Remove(obj);
    activeSet.Remove(obj);
    pool.Enqueue(obj);
  }

  public int ActiveCount {
    get { return active.Count; }
  }
}