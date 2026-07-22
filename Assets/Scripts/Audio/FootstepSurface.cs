using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class FootstepSurface : MonoBehaviour {
  [SerializeField] string denotation;

  static readonly List<FootstepSurface> activeSurfaces = new();

  SpriteRenderer surfaceRenderer;

  void Awake() {
    surfaceRenderer = GetComponent<SpriteRenderer>();
  }

  void OnEnable() {
    if (surfaceRenderer == null) {
      surfaceRenderer = GetComponent<SpriteRenderer>();
    }

    if (!activeSurfaces.Contains(this)) {
      activeSurfaces.Add(this);
    }
  }

  void OnDisable() {
    activeSurfaces.Remove(this);
  }

  public static bool TryResolveDenotation(Vector2 worldPoint, out string value) {
    value = "";
    FootstepSurface closestSurface = null;
    var closestDistance = float.MaxValue;

    for (var i = activeSurfaces.Count - 1; i >= 0; i--) {
      var surface = activeSurfaces[i];
      if (surface == null) {
        activeSurfaces.RemoveAt(i);
        continue;
      }

      if (!surface.Contains(worldPoint)) {
        continue;
      }

      var distance = Vector2.SqrMagnitude(
        (Vector2)surface.surfaceRenderer.bounds.center - worldPoint
      );
      if (distance >= closestDistance) {
        continue;
      }

      closestSurface = surface;
      closestDistance = distance;
    }

    if (closestSurface == null) {
      return false;
    }

    value = closestSurface.denotation;
    return true;
  }

  bool Contains(Vector2 worldPoint) {
    if (string.IsNullOrWhiteSpace(denotation)) {
      return false;
    }

    if (surfaceRenderer == null || !surfaceRenderer.enabled) {
      return false;
    }

    return surfaceRenderer.bounds.Contains(worldPoint);
  }
}
