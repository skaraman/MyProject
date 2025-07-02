using System.Collections.Generic;
using UnityEngine;

public class Pool : MonoBehaviour {
  public GameObject prefab;
  public int poolSize = 10;
  public bool autoResize = true;

  Queue<GameObject> pool = new Queue<GameObject>();
  List<GameObject> active = new List<GameObject>();
  Transform container;

  public void Initialize() {
    if (prefab == null || poolSize <= 0) return;

    if (container != null) Destroy(container.gameObject);
    container = new GameObject($"{prefab.name}_PoolContainer").transform;
    container.SetParent(transform);

    pool.Clear();
    active.Clear();

    for (int i = 0; i < poolSize; i++) {
      var go = Instantiate(prefab, container);
      go.SetActive(false);
      pool.Enqueue(go);
    }

    Debug.Log($"[Pool] Initialized pool of {poolSize} for {prefab.name}");
  }

  public GameObject Spawn(Vector3 position, Quaternion rotation) {
    GameObject obj;

    if (pool.Count > 0) {
      obj = pool.Dequeue();
    }
    else if (autoResize) {
      obj = Instantiate(prefab, container);
      Debug.Log($"[Pool] Auto-resizing: instantiating new {prefab.name}");
    }
    else {
      obj = active[0];
      active.RemoveAt(0);
      Debug.Log($"[Pool] Reusing oldest object from active list");
    }

    obj.transform.SetPositionAndRotation(position, rotation);
    obj.SetActive(true);
    active.Add(obj);
    return obj;
  }

  public void Despawn(GameObject obj) {
    if (!active.Contains(obj)) return;
    obj.SetActive(false);
    obj.transform.SetParent(container);
    active.Remove(obj);
    pool.Enqueue(obj);
  }
}