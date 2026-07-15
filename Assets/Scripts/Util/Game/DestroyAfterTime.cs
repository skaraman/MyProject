using UnityEngine;

public class DestroyAfterTime : MonoBehaviour {
  [Tooltip("Time in seconds before the object is destroyed")]
  public float lifetime = 1.5f;

  void Start() {
    Destroy(gameObject, lifetime);
  }
}
