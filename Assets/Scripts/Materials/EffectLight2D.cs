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

    var flicker = GetFlicker(Time.time);
    var minimumIntensity = baseIntensity * (1f - intensityFlicker);
    var maximumIntensity = baseIntensity * (1f + intensityFlicker);
    effectLight.intensity = Mathf.Lerp(minimumIntensity, maximumIntensity, flicker);

    var radiusScale = Mathf.Lerp(1f - radiusFlicker, 1f + radiusFlicker, flicker);
    ApplyWorldRadii(radiusScale);
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

  void ApplyWorldRadii(float radiusScale) {
    if (effectLight == null) return;

    var transformScale = GetMaximumWorldScale();
    effectLight.pointLightInnerRadius = (innerRadius * radiusScale) / transformScale;
    effectLight.pointLightOuterRadius = (outerRadius * radiusScale) / transformScale;
  }

  float GetMaximumWorldScale() {
    var worldScale = transform.lossyScale;
    var horizontalScale = Mathf.Abs(worldScale.x);
    var verticalScale = Mathf.Abs(worldScale.y);
    var maximumScale = Mathf.Max(horizontalScale, verticalScale);
    return Mathf.Max(0.0001f, maximumScale);
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
