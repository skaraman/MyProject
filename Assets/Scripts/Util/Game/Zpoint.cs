using UnityEngine;
using UnityEngine.Rendering;

public class Zpoint : MonoBehaviour {
  private SortingGroup sortingGroup;
  private Camera mainCamera;
  private Transform cachedTransform;

  void Awake() {
    cachedTransform = transform;
  }

  void Start() {
    ResolveReferences();
  }

  void ResolveReferences() {
    if (mainCamera == null || !mainCamera.isActiveAndEnabled) {
      mainCamera = Camera.main;
    }
    if (sortingGroup == null && cachedTransform.parent != null) {
      sortingGroup = cachedTransform.parent.GetComponent<SortingGroup>();
    }
  }

  void Update() {
    ResolveReferences();
    if (sortingGroup == null || mainCamera == null) return;

    Vector3 pos = cachedTransform.position;
    Vector3 screenPoint = mainCamera.WorldToScreenPoint(pos);
    sortingGroup.sortingOrder = -(int)screenPoint.y;
  }

}
