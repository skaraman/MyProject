using UnityEngine;

public class UIRefresher : MonoBehaviour {
  public Camera cam;
  private Renderer[] renderers;

  void Start() {
    cam = Camera.main;
    renderers = GetComponents<Renderer>();
  }

  void Update() {
    // Ensure all renderers on this GameObject and its children are not culled
    if (renderers == null || renderers.Length == 0) return;
    
    foreach (var r in renderers) {
      if (r == null) continue;
      r.forceRenderingOff = false;
      // Optionally, ensure the renderer's layer is visible to the camera
      if (cam != null && (cam.cullingMask & (1 << r.gameObject.layer)) == 0) {
        // Set to default layer (0) if current layer is not visible to the camera
        r.gameObject.layer = 0;
      }
    }
  }
}
