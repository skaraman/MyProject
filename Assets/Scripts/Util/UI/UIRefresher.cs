using UnityEngine;

public class UIRefresher : MonoBehaviour {
  public Camera cam;
  private Renderer[] renderers;

  void Start() {
    Refresh();
  }

  void OnEnable() {
    Refresh();
  }

  public void Refresh() {
    if (cam == null) {
      cam = Camera.main;
    }

    renderers = GetComponentsInChildren<Renderer>(true);
    if (renderers == null || renderers.Length == 0) return;

    foreach (var r in renderers) {
      if (r == null) continue;
      r.forceRenderingOff = false;
      if (cam != null && (cam.cullingMask & (1 << r.gameObject.layer)) == 0) {
        r.gameObject.layer = 0;
      }
    }
  }
}
