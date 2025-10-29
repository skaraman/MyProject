using UnityEngine;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using CustomInspector;

public class SaveCollider : MonoBehaviour {
  [Button(nameof(LogPointsCsv), label = "Log", size = Size.small)] public bool slowDown;
  public bool useWorldSpace = false; // set true to output world-space coordinates
  public int pathIndex = -1; // -1 = include all paths, otherwise 0-based path index


  public void LogPointsCsv() {
    Debug.Log(GetPointsCsv());
  }

  public string GetPointsCsv() {
    var poly = GetComponent<PolygonCollider2D>();
    if (poly == null) return "No PolygonCollider2D found.";

    var pts = new List<Vector2>();

    if (pathIndex >= 0 && pathIndex < poly.pathCount) {
      pts.AddRange(poly.GetPath(pathIndex));
    }
    else {
      for (int i = 0; i < poly.pathCount; i++) {
        pts.AddRange(poly.GetPath(i));
      }
    }

    if (pts.Count == 0) return "No points in polygon collider.";

    if (useWorldSpace) {
      for (int i = 0; i < pts.Count; i++)
        pts[i] = transform.TransformPoint(pts[i]);
    }

    // format each point as: new(x.xx f, y.yy f) -> e.g. new(1.23f, 4.56f)
    var formatted = pts.Select(p =>
      "new(" +
      p.x.ToString("F2", CultureInfo.InvariantCulture) + "f, " +
      p.y.ToString("F2", CultureInfo.InvariantCulture) + "f" +
      ")"
    );

    return string.Join(", ", formatted);
  }
}