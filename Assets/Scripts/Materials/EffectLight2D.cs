using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
[RequireComponent(typeof(Light2D))]
public sealed class EffectLight2D : MonoBehaviour {
  [SerializeField] Light2D effectLight;
  [SerializeField] Color lightColor = Color.white;
  [SerializeField, Min(0f)] float baseIntensity = 1.15f;
  [SerializeField, Range(0f, 1f)] float intensityFlicker = 0.85f;
  [SerializeField, Min(0f)] float flickerFrequency = 22f;
  [SerializeField, Range(0f, 1f)] float radiusFlicker = 0.35f;
  [SerializeField, Min(0f)] float radiusPulseFrequency = 3f;
  [SerializeField, Min(0f)] float innerRadius = 0.08f;
  [SerializeField, Min(0.01f)] float outerRadius = 0.65f;
  [SerializeField, Range(0f, 1f)] float falloffIntensity = 0.8f;
  [SerializeField] float flickerSeed = 0.37f;
  [SerializeField] string[] targetSortingLayers = { "GameBG", "GameMG", "GameFG" };

  [Header("Lingering Light")]
  [SerializeField] bool leaveLingeringLightOnDespawn;
  [SerializeField, Min(0f)] float minimumLingeringLifetime = 0.25f;
  [SerializeField, Min(0f)] float maximumLingeringLifetime = 0.5f;

  void Reset() {
    CacheLight();
    ApplyStaticSettings();
  }

  void Awake() {
    CacheLight();
    ApplyStaticSettings();
  }

  void OnEnable() {
    CacheLight();
    ApplyStaticSettings();
  }

  void OnValidate() {
    radiusPulseFrequency = Mathf.Max(0f, radiusPulseFrequency);
    outerRadius = Mathf.Max(0.01f, outerRadius);
    innerRadius = Mathf.Clamp(innerRadius, 0f, outerRadius);
    minimumLingeringLifetime = Mathf.Max(0f, minimumLingeringLifetime);
    maximumLingeringLifetime = Mathf.Max(
      minimumLingeringLifetime,
      maximumLingeringLifetime
    );
    CacheLight();
    ApplyStaticSettings();
  }

  void OnDisable() {
    if (effectLight == null) return;
    effectLight.intensity = 0f;
  }

  void LateUpdate() {
    if (effectLight == null) return;

    var now = TimeScale.GetNow(this);
    var flicker = GetFlicker(now);
    var minimumIntensity = baseIntensity * (1f - intensityFlicker);
    var maximumIntensity = baseIntensity * (1f + intensityFlicker);
    effectLight.intensity = Mathf.Lerp(minimumIntensity, maximumIntensity, flicker);

    ApplyWorldRadii(GetRadiusScale(now));
  }

  public void LeaveLingeringLight() {
    if (!leaveLingeringLightOnDespawn) return;

    CacheLight();
    if (effectLight == null || maximumLingeringLifetime <= 0f) return;

    var lifetime = UnityEngine.Random.Range(
      minimumLingeringLifetime,
      maximumLingeringLifetime
    );
    if (lifetime <= 0f) return;

    var lingeringObject = new GameObject($"{name} Lingering Light");
    lingeringObject.layer = gameObject.layer;
    lingeringObject.transform.SetPositionAndRotation(transform.position, transform.rotation);

    var lingeringLight = lingeringObject.AddComponent<Light2D>();
    lingeringLight.lightType = Light2D.LightType.Point;
    lingeringLight.color = effectLight.color;
    lingeringLight.intensity = Mathf.Max(effectLight.intensity, baseIntensity * 0.65f);
    lingeringLight.falloffIntensity = effectLight.falloffIntensity;
    lingeringLight.pointLightInnerAngle = effectLight.pointLightInnerAngle;
    lingeringLight.pointLightOuterAngle = effectLight.pointLightOuterAngle;
    // The source light pulses its outer radius. Lingering lights must begin at
    // the same authored size regardless of the pulse phase at despawn.
    lingeringLight.pointLightInnerRadius = innerRadius;
    lingeringLight.pointLightOuterRadius = outerRadius;
    lingeringLight.overlapOperation = effectLight.overlapOperation;
    lingeringLight.shadowsEnabled = false;
    lingeringLight.targetSortingLayers = effectLight.targetSortingLayers;

    lingeringObject.AddComponent<LingeringLightFade2D>().Initialize(
      lingeringLight,
      lifetime
    );
  }

  void CacheLight() {
    if (effectLight != null) return;
    effectLight = GetComponent<Light2D>();
  }

  void ApplyStaticSettings() {
    if (effectLight == null) return;

    effectLight.lightType = Light2D.LightType.Point;
    effectLight.color = lightColor;
    effectLight.intensity = baseIntensity;
    effectLight.falloffIntensity = falloffIntensity;
    effectLight.pointLightInnerAngle = 360f;
    effectLight.pointLightOuterAngle = 360f;
    effectLight.overlapOperation = Light2D.OverlapOperation.Additive;
    effectLight.shadowsEnabled = false;
    effectLight.targetSortingLayers = ResolveSortingLayerIds();
    ApplyWorldRadii(1f);
  }

  void ApplyWorldRadii(float outerRadiusScale) {
    if (effectLight == null) return;

    // URP Spot/Point light radii are already world-space values and ignore
    // Transform scale. Compensating for scale makes scaled effects too small.
    effectLight.pointLightInnerRadius = innerRadius;
    effectLight.pointLightOuterRadius = outerRadius * outerRadiusScale;
  }

  float GetRadiusScale(float timeValue) {
    if (radiusFlicker <= 0f || radiusPulseFrequency <= 0f) return 1f;

    var phase = (timeValue * radiusPulseFrequency + flickerSeed) * Mathf.PI * 2f;
    var pulse = (Mathf.Sin(phase) + 1f) * 0.5f;
    return Mathf.Lerp(1f - radiusFlicker, 1f + radiusFlicker, pulse);
  }

  float GetFlicker(float timeValue) {
    var sampleTime = timeValue * flickerFrequency;
    var slowNoise = Mathf.PerlinNoise(flickerSeed, sampleTime);
    var fastNoise = Mathf.PerlinNoise(flickerSeed + 13.7f, sampleTime * 1.91f);
    return Mathf.Clamp01((slowNoise * 0.62f) + (fastNoise * 0.38f));
  }

  int[] ResolveSortingLayerIds() {
    if (targetSortingLayers == null || targetSortingLayers.Length == 0) {
      return Array.Empty<int>();
    }

    var availableLayers = SortingLayer.layers;
    var resolvedIds = new System.Collections.Generic.List<int>();

    foreach (var targetLayer in targetSortingLayers) {
      if (string.IsNullOrWhiteSpace(targetLayer)) continue;

      foreach (var availableLayer in availableLayers) {
        if (!string.Equals(targetLayer, availableLayer.name, StringComparison.Ordinal)) continue;
        resolvedIds.Add(availableLayer.id);
        break;
      }
    }

    return resolvedIds.ToArray();
  }
}

sealed class LingeringLightFade2D : MonoBehaviour {
  Light2D targetLight;
  float initialIntensity;
  float lifetime;
  float elapsed;

  public void Initialize(Light2D light, float duration) {
    targetLight = light;
    initialIntensity = light != null ? light.intensity : 0f;
    lifetime = Mathf.Max(0.01f, duration);
  }

  void Update() {
    elapsed += TimeScale.GetDeltaTime(this);
    var progress = Mathf.Clamp01(elapsed / lifetime);
    if (targetLight != null) {
      targetLight.intensity = Mathf.SmoothStep(initialIntensity, 0f, progress);
    }

    if (progress >= 1f) {
      Destroy(gameObject);
    }
  }
}
