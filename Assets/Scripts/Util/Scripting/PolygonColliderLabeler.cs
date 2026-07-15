using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(PolygonCollider2D))]
public class PolygonPointLabeler : MonoBehaviour {
  public Camera cam;
  public int pathIndex = 0;
  public float hoverRadius = 0.2f;
  public Vector2 guiOffset = new Vector2(10, -20);
  public bool clickToPin = true;
  public bool showAllWhenPinned = false;
  public KeyCode unpinKey = KeyCode.Escape;
  public string clickEvent = "PolygonPointClicked";
  public string hoverEvent = "PolygonPointHover";

  // new: color & width for the connecting lines
  public Color lineColor = Color.green;
  public float lineWidth = 2f; // pixel width for editor handles

  PolygonCollider2D poly;
  Vector2[] localPoints = Array.Empty<Vector2>();
  readonly List<Vector3> worldPoints = new();
  int nearestIndex = -1;
  float nearestDist = float.MaxValue;
  int pinnedIndex = -1;
  Vector3 lastMouseWorld;
  Vector2 lastMouseScreen;
  Rect lastLabelRect;

  void OnEnable() {
    poly = GetComponent<PolygonCollider2D>();
    cam = cam == null ? Camera.main : cam;
    LoadPoints();
    //RuntimeLog.Log($"[PolygonPointLabeler] Enabled. HasCollider={(poly!=null)}, Paths={poly.pathCount}, UsingPath={pathIndex}");
    // RuntimeLog.Log($"[PolygonPointLabeler] LoadedPoints={localPoints.Length}");
  }

  [ForceUpdate]
  public void LoadPoints() {
    if (poly == null) poly = GetComponent<PolygonCollider2D>();
    var pCount = poly != null && pathIndex < poly.pathCount ? poly.GetPath(pathIndex).Length : 0;
    localPoints = poly != null && pathIndex < poly.pathCount ? poly.GetPath(pathIndex) : Array.Empty<Vector2>();
    worldPoints.Clear();
    for (int i = 0; i < localPoints.Length; i++) worldPoints.Add(transform.TransformPoint(localPoints[i]));
    //RuntimeLog.Log($"[PolygonPointLabeler] LoadPoints PathIndex={pathIndex} CountLocal={pCount} CountWorld={worldPoints.Count}");
  }

  void Update() {
    if (poly == null) return;
#if UNITY_EDITOR
    if (!Application.isPlaying && !UnityEditorInternal.InternalEditorUtility.isApplicationActive) return;
#endif
    if (poly.pathCount == 0) return;
    cam = cam == null ? Camera.main : cam;

    // Check if collider's local points have changed and reload if needed
    Vector2[] currentLocal = (poly != null && pathIndex >= 0 && pathIndex < poly.pathCount) ? poly.GetPath(pathIndex) : Array.Empty<Vector2>();
    if (PointsDiffer(localPoints, currentLocal)) {
      localPoints = currentLocal;
      worldPoints.Clear();
      for (int i = 0; i < localPoints.Length; i++) worldPoints.Add(transform.TransformPoint(localPoints[i]));
      //RuntimeLog.Log($"[PolygonPointLabeler] Detected collider point change. Reloaded {localPoints.Length} points.");
#if UNITY_EDITOR
      // ensure scene view and editor labels/handles repaint immediately
      UnityEditor.SceneView.RepaintAll();
#endif
    }

    worldPoints.Clear();
    for (int i = 0; i < localPoints.Length; i++) worldPoints.Add(transform.TransformPoint(localPoints[i]));

    // draw runtime colored lines for all collider paths (visible in Game view while playing)
    if (poly != null && poly.pathCount > 0) {
      for (int p = 0; p < poly.pathCount; p++) {
        var path = poly.GetPath(p);
        if (path == null || path.Length < 2) continue;
        // draw closed loop
        for (int i = 0; i < path.Length; i++) {
          int j = (i + 1) % path.Length;
          var a = transform.TransformPoint(path[i]);
          var b = transform.TransformPoint(path[j]);
          Debug.DrawLine(a, b, lineColor);
        }
      }
    }

    var mouseScreen = Input.mousePosition;
    var mouseWorld = cam != null ? (Vector3)cam.ScreenToWorldPoint(new Vector3(mouseScreen.x, mouseScreen.y, Mathf.Abs(transform.position.z - (cam.orthographic ? cam.transform.position.z : 0f)))) : Vector3.zero;
    lastMouseWorld = mouseWorld;
    lastMouseScreen = mouseScreen;
    var idx = -1;
    var d = float.MaxValue;
    for (int i = 0; i < worldPoints.Count; i++) {
      var dist = Vector2.Distance(worldPoints[i], mouseWorld);
      if (dist < d) { d = dist; idx = i; }
    }
    if (idx != nearestIndex || Math.Abs(d - nearestDist) > 0.0001f) {
      nearestIndex = idx;
      nearestDist = d;
      //RuntimeLog.Log($"[PolygonPointLabeler] MouseWorld=({mouseWorld.x:F3},{mouseWorld.y:F3}) NearestIndex={nearestIndex} Dist={nearestDist:F3} HoverRadius={hoverRadius}");
      if (nearestIndex >= 0 && d <= hoverRadius) MessageBus.Send(hoverEvent, nearestIndex);
    }
    if (clickToPin && Input.GetMouseButtonDown(0)) {
      if (nearestIndex >= 0 && d <= hoverRadius) {
        pinnedIndex = nearestIndex;
        MessageBus.Send(clickEvent, pinnedIndex);
        // RuntimeLog.Log($"[PolygonPointLabeler] PinnedIndex={pinnedIndex}");
      }
    }
    if (clickToPin && Input.GetKeyDown(unpinKey)) {
      pinnedIndex = -1;
      //RuntimeLog.Log("[PolygonPointLabeler] Unpinned");
    }
  }

  void OnGUI() {
    if (cam == null || worldPoints.Count == 0) return;
    if (Event.current.type != EventType.Repaint && Event.current.type != EventType.Layout) return;
    if (pinnedIndex >= 0) {
      if (showAllWhenPinned) {
        for (int i = 0; i < worldPoints.Count; i++) DrawLabelFor(i, "(pinned)");
      }
      else {
        DrawLabelFor(pinnedIndex, "(pinned)");
      }
      return;
    }
    if (nearestIndex >= 0 && nearestDist <= hoverRadius) DrawLabelFor(nearestIndex, "(hover)");
  }

  void DrawLabelFor(int index, string tag) {
    var wp = worldPoints[index];
    var sp = cam.WorldToScreenPoint(wp);
    var pos = new Vector2(sp.x, Screen.height - sp.y) + guiOffset;
    var text = $"Pt {index} {tag}";
    var content = new GUIContent(text);
    var size = GUI.skin.box.CalcSize(content);
    var rect = new Rect(pos.x, pos.y, size.x + 8, size.y + 4);
    GUI.Box(rect, text);
    lastLabelRect = rect;
    //RuntimeLog.Log($"[PolygonPointLabeler] GUI Label '{text}' @({rect.x:F1},{rect.y:F1}) Size=({rect.width:F1},{rect.height:F1}) ForWorld=({wp.x:F3},{wp.y:F3})");
  }

  void OnValidate() {
    if (poly == null) poly = GetComponent<PolygonCollider2D>();
    if (poly != null && pathIndex >= poly.pathCount) pathIndex = Mathf.Max(0, poly.pathCount - 1);
    LoadPoints();
  }

#if UNITY_EDITOR
  void OnDrawGizmos() {
    if (poly == null) poly = GetComponent<PolygonCollider2D>();
    if (poly == null) return;
    if (worldPoints.Count == 0 && poly.pathCount == 0) return;

    // draw point markers for the selected path (existing behavior)
    for (int i = 0; i < worldPoints.Count; i++) {
      var r = i == nearestIndex && nearestDist <= hoverRadius ? 0.075f : 0.05f;
      Gizmos.color = i == nearestIndex && nearestDist <= hoverRadius ? Color.yellow : Color.cyan;
      Gizmos.DrawSphere(worldPoints[i], r);
    }

    // draw colored lines connecting points for all collider paths (Gizmos fallback)
    Gizmos.color = lineColor;
    for (int p = 0; p < poly.pathCount; p++) {
      var path = poly.GetPath(p);
      if (path == null || path.Length < 2) continue;
      for (int i = 0; i < path.Length; i++) {
        int j = (i + 1) % path.Length;
        var a = transform.TransformPoint(path[i]);
        var b = transform.TransformPoint(path[j]);
        Gizmos.DrawLine(a, b);
      }
    }

    // use Handles for nicer anti-aliased/thicker lines in the Scene view and force them to render on top
    if (SceneView.lastActiveSceneView != null) {
      var svCam = SceneView.lastActiveSceneView.camera;
      if (svCam != null) {
        var prevZ = Handles.zTest;
        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        Handles.color = lineColor;

        for (int p = 0; p < poly.pathCount; p++) {
          var path = poly.GetPath(p);
          if (path == null || path.Length < 2) continue;
          var pts = new Vector3[path.Length + 1];
          for (int i = 0; i < path.Length; i++) pts[i] = transform.TransformPoint(path[i]);
          pts[path.Length] = pts[0]; // close loop
          Handles.DrawAAPolyLine(lineWidth, pts);
        }

        // labels for all paths/points (previously only selected path)
        for (int p = 0; p < poly.pathCount; p++) {
          var path = poly.GetPath(p);
          if (path == null || path.Length == 0) continue;
          for (int i = 0; i < path.Length; i++) {
            var worldPos = transform.TransformPoint(path[i]) + Vector3.up * 0.05f;
            // highlight only if this is the selected path and matches nearest/pinned index
            if (p == pathIndex && i == nearestIndex && nearestDist <= hoverRadius) {
              Handles.color = Color.yellow;
            }
            else if (p == pathIndex && i == pinnedIndex) {
              Handles.color = Color.yellow;
            }
            else {
              Handles.color = Color.white;
            }
            Handles.Label(worldPos, $"#{i}");
          }
        }

        Handles.zTest = prevZ;
      }
    }
  }
#endif

  public int GetNearestIndex(out float distance) {
    distance = nearestDist;
    return nearestIndex;
  }

  public int GetPinnedIndex() {
    return pinnedIndex;
  }

  public void SetPinnedIndex(int index) {
    pinnedIndex = Mathf.Clamp(index, -1, worldPoints.Count - 1);
    //RuntimeLog.Log($"[PolygonPointLabeler] SetPinnedIndex={pinnedIndex}");
  }

  public IReadOnlyList<Vector3> GetWorldPoints() {
    return worldPoints;
  }

  public void SetHoverRadius(float r) {
    hoverRadius = Mathf.Max(0.0001f, r);
    //RuntimeLog.Log($"[PolygonPointLabeler] SetHoverRadius={hoverRadius}");
  }

  // helper to compare local point arrays with a small tolerance
  bool PointsDiffer(Vector2[] a, Vector2[] b) {
    if (a == null && b == null) return false;
    if (a == null || b == null) return true;
    if (a.Length != b.Length) return true;
    const float eps = 1e-5f;
    for (int i = 0; i < a.Length; i++) {
      if (Mathf.Abs(a[i].x - b[i].x) > eps || Mathf.Abs(a[i].y - b[i].y) > eps) return true;
    }
    return false;
  }
}
