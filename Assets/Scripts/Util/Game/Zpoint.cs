using UnityEngine;
using UnityEngine.Rendering;

public class Zpoint : MonoBehaviour
{
  public SortingGroup sortingGroup;
  private Camera mainCamera;
  private Transform cachedTransform;

  void Awake() {
    cachedTransform = transform;
  }

  void Start() {
    mainCamera = Camera.main;
  }

  void Update() {
    if (sortingGroup != null && mainCamera != null) {
      Vector3 pos = cachedTransform.position;
      Vector3 screenPoint = mainCamera.WorldToScreenPoint(pos);
      //Debug.Log($"Screen Point: {screenPoint}, ID: {gameObject.transform.name}");
      // Adjust to control the effect
      sortingGroup.sortingOrder = -(int)screenPoint.y;
    }
  }

}