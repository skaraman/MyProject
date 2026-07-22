using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
[RequireComponent(typeof(Light2D))]
public sealed class EffectLight2D : MonoBehaviour {
  [SerializeField] Light2D effectLight;
  [SerializeField] Color lightColor = Color.white;
  [SerializeField, Min(0f)] float baseIntensity = 1.15f;
  [SerializeField, Range(0f, 1f)] float intensityFlicker = 0.55f;
  [SerializeField, Min(0f)] float flickerFrequency = 22f;
  [SerializeField, Range(0f, 0.5f)] float radiusFlicker = 0.12f;
  [SerializeField, Min(0f)] float radiusPulseFrequency = 3f;
  [SerializeField, Min(0f)] float innerRadius = 0.08f;
  [SerializeField, Min(0.01f)] float outerRadius = 0.65f;
  [SerializeField, Range(0f, 1f)] float falloffIntensity = 0.8f;
  [SerializeField] float flickerSeed = 0.37f;
  [SerializeField] string[] targetSortingLayers = { "GameBG", "GameMG", "GameFG" };

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
