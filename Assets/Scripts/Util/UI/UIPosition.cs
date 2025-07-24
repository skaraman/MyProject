using UnityEngine;

public class UIPosition : MonoBehaviour {
  public Camera cam;

  void Update() {
    if (cam == null) return;
    var pos = cam.transform.position;
    transform.position = new Vector3(pos.x, pos.y, transform.position.z);
    //Debug.Log($"[UIPosition] Camera pos: {pos}, Updated UI pos: {transform.position}");
  }
}
