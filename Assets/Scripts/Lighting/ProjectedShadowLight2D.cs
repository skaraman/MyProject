using UnityEngine;
using UnityEngine.Rendering.Universal;

public enum ProjectedShadowLightRole {
  Sun = 0,
  Local = 1,
  Moon = 2
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Light2D))]
public sealed class ProjectedShadowLight2D : MonoBehaviour {
  [SerializeField] Light2D sourceLight;
  [SerializeField] ProjectedShadowLightRole lightRole = ProjectedShadowLightRole.Local;
  [SerializeField] bool castsCharacterShadows = true;
  [SerializeField, Min(0f)] float rangeOverride;
  [SerializeField, Range(0f, 2f)] float shadowStrength = 1f;
  [SerializeField, Min(0.01f)] float projectionLength = 0.75f;
  [SerializeField] bool useDirectionOverride;
  [SerializeField] Vector2 directionOverride = new(0.45f, -1f);

  public Light2D SourceLight => sourceLight;
  public ProjectedShadowLightRole LightRole => lightRole;
  public bool CastsCharacterShadows => castsCharacterShadows;
  public float ShadowStrength => shadowStrength;
  public float ProjectionLength => projectionLength;

  void Reset() {
    CacheLight();
  }

  void Awake() {
    CacheLight();
  }

  void OnEnable() {
    CacheLight();
    DayNightCycle2D.RegisterLight(this);
  }

  void OnDisable() {
    DayNightCycle2D.UnregisterLight(this);
  }

  void OnValidate() {
    rangeOverride = Mathf.Max(0f, rangeOverride);
    shadowStrength = Mathf.Max(0f, shadowStrength);
    projectionLength = Mathf.Max(0.01f, projectionLength);
    CacheLight();

    if (Application.isPlaying && isActiveAndEnabled) {
      DayNightCycle2D.RegisterLight(this);
    }
  }

  public float ResolveWorldRange() {
    if (rangeOverride > 0f) {
      return rangeOverride;
    }
    if (sourceLight == null) {
      return 0f;
    }
    if (sourceLight.lightType != Light2D.LightType.Point) {
      return 0f;
    }

    // URP Spot/Point light radii are world-space and do not inherit scale.
    return sourceLight.pointLightOuterRadius;
  }

  public bool AffectsSortingLayer(int sortingLayerId) {
    if (sourceLight == null) {
      return false;
    }

    var targetLayers = sourceLight.targetSortingLayers;
    if (targetLayers == null || targetLayers.Length == 0) {
      return false;
    }

    for (var i = 0; i < targetLayers.Length; i++) {
      if (targetLayers[i] == sortingLayerId) {
        return true;
      }
    }

    return false;
  }

  public bool TryGetDirectionOverride(out Vector2 direction) {
    direction = directionOverride;
    if (!useDirectionOverride) {
      return false;
    }
    if (direction.sqrMagnitude <= 0.0001f) {
      return false;
    }

    direction.Normalize();
    return true;
  }

  void CacheLight() {
    if (sourceLight == null) {
      sourceLight = GetComponent<Light2D>();
    }
  }
}
