using UnityEngine;
using UnityEngine.Rendering;

public class Zpoint : MonoBehaviour
{
  public SortingGroup sortingGroup;

  void Update() {
    if (sortingGroup != null) {
      Vector3 pos = transform.position;
      Vector3 screenPoint = Camera.main.WorldToScreenPoint(pos);
      //Debug.Log($"Screen Point: {screenPoint}, ID: {gameObject.transform.name}");
      // Adjust to control the effect
      sortingGroup.sortingOrder = -(int)screenPoint.y;
    }
  }

}