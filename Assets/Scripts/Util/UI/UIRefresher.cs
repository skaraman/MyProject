using UnityEngine;

public class UIRefresher : MonoBehaviour {
  public Camera cam;

  void Start() {
    cam = Camera.main;
  }

  void Update() {
    // Ensure all renderers on this GameObject and its children are not culled
    var renderers = GetComponents<Renderer>();
    foreach (var r in renderers) {
      r.forceRenderingOff = false;
      // Optionally, ensure the renderer's layer is visible to the camera
      if (cam != null && (cam.cullingMask & (1 << r.gameObject.layer)) == 0) {
        // Set to default layer (0) if current layer is not visible to the camera
        r.gameObject.layer = 0;
      }
    }
  }
}
