using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[DefaultExecutionOrder(-900)]
[DisallowMultipleComponent]
public sealed class SceneLighting2D : MonoBehaviour {
  public readonly struct ShadowProjection {
    public readonly Vector2 Direction;
    public readonly float Length;
    public readonly float Opacity;

    public ShadowProjection(Vector2 direction, float length, float opacity) {
      Direction = direction;
      Length = length;
      Opacity = opacity;
    }
  }

  const float MinimumLightIntensity = 0.001f;
  const int ShadowStencilReferenceCapacity = 64;
  const string DefaultShadowSortingLayer = "GameMG";

  static readonly Dictionary<ulong, ProjectedShadowLight2D> registeredLights = new();
  static readonly List<SceneLighting2D> registeredManagers = new();
  static readonly List<ulong> staleLightIds = new();
  static readonly bool[] reservedStencilReferences =
    new bool[ShadowStencilReferenceCapacity];
  static SceneLighting2D instance;

  [Header("Scene Lights")]
  [SerializeField] Light2D ambientLight;
  [SerializeField] Light2D sunLight;

  [Header("Day / Night")]
  [SerializeField] Color nightAmbientColor = new(0.18f, 0.25f, 0.45f, 1f);
  [SerializeField, Range(0f, 1f)] float nightAmbientIntensityMultiplier = 0.22f;
  [SerializeField] Color nightSunColor = new(0.28f, 0.38f, 0.65f, 1f);
  [SerializeField, Range(0f, 1f)] float nightSunIntensityMultiplier;
  [SerializeField, Range(0f, 1f)] float nightAmount;

  [Header("Ground Shadows")]
  [SerializeField] string shadowSortingLayer = DefaultShadowSortingLayer;
  [SerializeField] int shadowSortingOrder = 1000;
  [SerializeField] Vector2 sunShadowDirection = new(0.45f, -1f);
  [SerializeField, Range(0f, 1f)] float sunShadowOpacity = 0.28f;
  [SerializeField, Range(0f, 1f)] float localShadowOpacity = 0.24f;
  [SerializeField, Range(0f, 0.5f)] float localLightSwitchHysteresis = 0.15f;

  Transform shadowRoot;
  bool ambientBaselineCaptured;
  bool sunBaselineCaptured;
  float dayAmbientIntensity;
  float daySunIntensity;
  Color dayAmbientColor = Color.white;
  Color daySunColor = Color.white;
  float transitionStartAmount;
  float transitionTargetAmount;
  float transitionDuration;
  float transitionElapsed;
  bool transitionActive;
  int shadowSortingLayerId;

  public static SceneLighting2D Current {
    get {
      if (instance == null || !instance.isActiveAndEnabled) {
        return null;
      }
      return instance;
    }
  }

  public float NightAmount => nightAmount;

  public Vector2 SunShadowDirection {
    get => sunShadowDirection;
    set => sunShadowDirection = value;
  }

  public void SetNightAmountDirect(float amount) {
    nightAmount = Mathf.Clamp01(amount);
  }

  public Transform ShadowRoot {
    get {
      EnsureShadowRoot();
      return shadowRoot;
    }
  }

  public int ShadowSortingLayerId {
    get {
      if (shadowSortingLayerId == 0) {
        ResolveShadowSortingLayer();
      }
      return shadowSortingLayerId;
    }
  }

  public int ShadowSortingOrder => shadowSortingOrder;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetStatics() {
    registeredLights.Clear();
    registeredManagers.Clear();
    staleLightIds.Clear();
    Array.Clear(reservedStencilReferences, 0, reservedStencilReferences.Length);
    instance = null;
  }

  public static int ReserveShadowStencilReference() {
    for (var stencilReference = 1;
         stencilReference < reservedStencilReferences.Length;
         stencilReference++) {
      if (reservedStencilReferences[stencilReference]) {
        continue;
      }

      reservedStencilReferences[stencilReference] = true;
      return stencilReference;
    }

    return 0;
  }

  public static void ReleaseShadowStencilReference(int stencilReference) {
    if (stencilReference <= 0 ||
        stencilReference >= reservedStencilReferences.Length) {
      return;
    }

    reservedStencilReferences[stencilReference] = false;
  }

  public static void RegisterLight(ProjectedShadowLight2D source) {
    if (source == null) {
      return;
    }

    var sourceId = ObjectEntityId.GetRawValue(source);
    registeredLights[sourceId] = source;
  }

  public static void UnregisterLight(ProjectedShadowLight2D source) {
    if (source == null) {
      return;
    }

    var sourceId = ObjectEntityId.GetRawValue(source);
    if (!registeredLights.TryGetValue(sourceId, out var registeredSource)) {
      return;
    }
    if (registeredSource != source) {
      return;
    }

    registeredLights.Remove(sourceId);
  }

  public static bool SetNightAmount(float amount, float transitionSeconds = 0f) {
    var current = Current;
    if (current == null) {
      return false;
    }

    current.BeginNightTransition(amount, transitionSeconds);
    return true;
  }

  public static bool TransitionToNight(float transitionSeconds = 2f) {
    return SetNightAmount(1f, transitionSeconds);
  }

  public static bool TransitionToDay(float transitionSeconds = 2f) {
    return SetNightAmount(0f, transitionSeconds);
  }

  void Awake() {
    ResolveLightReferences();
    CaptureDayLightingBaseline();
    ResolveShadowSortingLayer();
    EnsureShadowRoot();
    ApplyLighting();
  }

  void OnEnable() {
    if (!registeredManagers.Contains(this)) {
      registeredManagers.Add(this);
    }

    ClaimAsCurrentManager();

    ResolveLightReferences();
    CaptureDayLightingBaseline();
    EnsureShadowRoot();
    ApplyLighting();
  }

  void OnDisable() {
    registeredManagers.Remove(this);
    SetShadowRootActive(false);

    if (instance == this) {
      instance = ResolveFallbackManager();
      instance?.SetShadowRootActive(true);
    }
  }

  void Update() {
    if (!transitionActive) {
      return;
    }

    transitionElapsed += TimeScale.GetDeltaTime(this);
    var progress = transitionElapsed / transitionDuration;
    progress = Mathf.Clamp01(progress);
    var easedProgress = progress * progress * (3f - (2f * progress));
    nightAmount = Mathf.Lerp(transitionStartAmount, transitionTargetAmount, easedProgress);
    ApplyLighting();

    if (progress >= 1f) {
      transitionActive = false;
    }
  }

  void OnDestroy() {
    registeredManagers.Remove(this);
    if (instance == this) {
      instance = ResolveFallbackManager();
      instance?.SetShadowRootActive(true);
    }
  }

  void OnValidate() {
    nightAmount = Mathf.Clamp01(nightAmount);
    nightAmbientIntensityMultiplier = Mathf.Clamp01(nightAmbientIntensityMultiplier);
    nightSunIntensityMultiplier = Mathf.Clamp01(nightSunIntensityMultiplier);
    sunShadowOpacity = Mathf.Clamp01(sunShadowOpacity);
    localShadowOpacity = Mathf.Clamp01(localShadowOpacity);
    localLightSwitchHysteresis = Mathf.Clamp(localLightSwitchHysteresis, 0f, 0.5f);
    shadowSortingLayerId = 0;
  }

  public void CaptureCurrentLightingAsDay() {
    ambientBaselineCaptured = false;
    sunBaselineCaptured = false;
    CaptureDayLightingBaseline();
    ApplyLighting();
  }

  public bool TryGetSunShadow(int casterSortingLayerId, out ShadowProjection projection) {
    projection = default;
    var source = ResolveStrongestSun(casterSortingLayerId);
    if (source == null) {
      return false;
    }

    var daylight = 1f - ResolveSmoothedNightAmount();
    var opacity = sunShadowOpacity * source.ShadowStrength * daylight;
    if (opacity <= 0.001f) {
      return false;
    }

    Vector2 direction;
    if (!source.TryGetDirectionOverride(out direction)) {
      direction = sunShadowDirection;
    }
    if (direction.sqrMagnitude <= 0.0001f) {
      direction = Vector2.down;
    }

    direction.Normalize();
    projection = new ShadowProjection(direction, source.ProjectionLength, opacity);
    return true;
  }

  public ulong SelectNearestLocalLight(
    Vector2 receiverPosition,
    int casterSortingLayerId,
    ulong currentLightId
  ) {
    PurgeStaleLights();

    var bestLightId = 0UL;
    var bestDistanceSquared = float.MaxValue;
    foreach (var pair in registeredLights) {
      var source = pair.Value;
      if (!TryGetLocalDistanceSquared(
        source,
        receiverPosition,
        casterSortingLayerId,
        out var distanceSquared)) {
        continue;
      }
      if (distanceSquared >= bestDistanceSquared) {
        continue;
      }

      bestLightId = pair.Key;
      bestDistanceSquared = distanceSquared;
    }

    if (bestLightId == 0 || currentLightId == 0 || bestLightId == currentLightId) {
      return bestLightId;
    }
    if (!registeredLights.TryGetValue(currentLightId, out var currentSource)) {
      return bestLightId;
    }
    if (!TryGetLocalDistanceSquared(
      currentSource,
      receiverPosition,
      casterSortingLayerId,
      out var currentDistanceSquared)) {
      return bestLightId;
    }

    var switchFactor = 1f - localLightSwitchHysteresis;
    var switchThresholdSquared = currentDistanceSquared * switchFactor * switchFactor;
    if (bestDistanceSquared >= switchThresholdSquared) {
      return currentLightId;
    }

    return bestLightId;
  }

  public bool TryGetLocalShadow(
    ulong lightId,
    Vector2 receiverPosition,
    Vector2 groundPosition,
    int casterSortingLayerId,
    out ShadowProjection projection
  ) {
    projection = default;
    if (lightId == 0) {
      return false;
    }
    if (!registeredLights.TryGetValue(lightId, out var source)) {
      return false;
    }
    if (!TryGetLocalDistanceSquared(
      source,
      receiverPosition,
      casterSortingLayerId,
      out var distanceSquared)) {
      return false;
    }

    var sourceLight = source.SourceLight;
    var range = source.ResolveWorldRange();
    var distance = Mathf.Sqrt(distanceSquared);
    var normalizedDistance = range > 0f ? distance / range : 1f;
    var attenuation = Mathf.SmoothStep(1f, 0f, normalizedDistance);
    var intensity = Mathf.Clamp01(sourceLight.intensity);
    var opacity = localShadowOpacity * source.ShadowStrength * intensity * attenuation;
    if (opacity <= 0.001f) {
      return false;
    }

    Vector2 direction;
    if (!source.TryGetDirectionOverride(out direction)) {
      var lightPosition = (Vector2)sourceLight.transform.position;
      direction = groundPosition - lightPosition;
    }
    if (direction.sqrMagnitude <= 0.0001f) {
      direction = Vector2.down;
    }

    direction.Normalize();
    projection = new ShadowProjection(direction, source.ProjectionLength, opacity);
    return true;
  }

  void BeginNightTransition(float amount, float transitionSeconds) {
    var clampedAmount = Mathf.Clamp01(amount);
    if (transitionSeconds <= 0f) {
      transitionActive = false;
      nightAmount = clampedAmount;
      ApplyLighting();
      return;
    }

    transitionStartAmount = nightAmount;
    transitionTargetAmount = clampedAmount;
    transitionDuration = Mathf.Max(transitionSeconds, 0.0001f);
    transitionElapsed = 0f;
    transitionActive = true;
  }

  void ResolveLightReferences() {
    if (ambientLight != null && sunLight != null) {
      return;
    }

    var childLights = GetComponentsInChildren<Light2D>(true);
    for (var i = 0; i < childLights.Length; i++) {
      var candidate = childLights[i];
      if (candidate == null) {
        continue;
      }
      if (ambientLight == null && candidate.lightType == Light2D.LightType.Global) {
        ambientLight = candidate;
      }
      if (sunLight == null && IsSunLight(candidate)) {
        sunLight = candidate;
      }
    }
  }

  static bool IsSunLight(Light2D candidate) {
    var marker = candidate.GetComponent<ProjectedShadowLight2D>();
    if (marker != null && marker.LightRole == ProjectedShadowLightRole.Sun) {
      return true;
    }

    return candidate.gameObject.name.IndexOf("Sun", StringComparison.OrdinalIgnoreCase) >= 0;
  }

  void CaptureDayLightingBaseline() {
    if (!ambientBaselineCaptured && ambientLight != null) {
      dayAmbientColor = ambientLight.color;
      dayAmbientIntensity = ambientLight.intensity;
      ambientBaselineCaptured = true;
    }
    if (!sunBaselineCaptured && sunLight != null) {
      daySunColor = sunLight.color;
      daySunIntensity = sunLight.intensity;
      sunBaselineCaptured = true;
    }
  }

  void ApplyLighting() {
    var smoothedNightAmount = ResolveSmoothedNightAmount();
    if (ambientBaselineCaptured && ambientLight != null) {
      ambientLight.color = Color.Lerp(dayAmbientColor, nightAmbientColor, smoothedNightAmount);
      var ambientMultiplier = Mathf.Lerp(1f, nightAmbientIntensityMultiplier, smoothedNightAmount);
      ambientLight.intensity = dayAmbientIntensity * ambientMultiplier;
    }
    if (sunBaselineCaptured && sunLight != null) {
      sunLight.color = Color.Lerp(daySunColor, nightSunColor, smoothedNightAmount);
      var sunMultiplier = Mathf.Lerp(1f, nightSunIntensityMultiplier, smoothedNightAmount);
      sunLight.intensity = daySunIntensity * sunMultiplier;
    }
  }

  float ResolveSmoothedNightAmount() {
    var value = Mathf.Clamp01(nightAmount);
    return value * value * (3f - (2f * value));
  }

  ProjectedShadowLight2D ResolveStrongestSun(int casterSortingLayerId) {
    PurgeStaleLights();

    var sceneSun = sunLight != null
      ? sunLight.GetComponent<ProjectedShadowLight2D>()
      : null;
    if (IsEligibleSource(
      sceneSun,
      ProjectedShadowLightRole.Sun,
      casterSortingLayerId
    )) {
      return sceneSun;
    }

    ProjectedShadowLight2D strongestSource = null;
    var strongestIntensity = MinimumLightIntensity;
    foreach (var pair in registeredLights) {
      var source = pair.Value;
      if (!IsEligibleSource(source, ProjectedShadowLightRole.Sun, casterSortingLayerId)) {
        continue;
      }

      var intensity = source.SourceLight.intensity * source.ShadowStrength;
      if (intensity <= strongestIntensity) {
        continue;
      }

      strongestSource = source;
      strongestIntensity = intensity;
    }

    return strongestSource;
  }

  static bool TryGetLocalDistanceSquared(
    ProjectedShadowLight2D source,
    Vector2 receiverPosition,
    int casterSortingLayerId,
    out float distanceSquared
  ) {
    distanceSquared = float.MaxValue;
    if (!IsEligibleSource(source, ProjectedShadowLightRole.Local, casterSortingLayerId)) {
      return false;
    }

    var range = source.ResolveWorldRange();
    if (range <= 0f) {
      return false;
    }

    var lightPosition = (Vector2)source.SourceLight.transform.position;
    var difference = receiverPosition - lightPosition;
    distanceSquared = difference.sqrMagnitude;
    return distanceSquared <= range * range;
  }

  static bool IsEligibleSource(
    ProjectedShadowLight2D source,
    ProjectedShadowLightRole requiredRole,
    int casterSortingLayerId
  ) {
    if (source == null || !source.isActiveAndEnabled) {
      return false;
    }
    if (!source.CastsCharacterShadows || source.LightRole != requiredRole) {
      return false;
    }

    var sourceLight = source.SourceLight;
    if (sourceLight == null || !sourceLight.isActiveAndEnabled) {
      return false;
    }
    if (sourceLight.intensity <= MinimumLightIntensity) {
      return false;
    }

    return source.AffectsSortingLayer(casterSortingLayerId);
  }

  static void PurgeStaleLights() {
    staleLightIds.Clear();
    foreach (var pair in registeredLights) {
      if (pair.Value == null) {
        staleLightIds.Add(pair.Key);
      }
    }

    for (var i = 0; i < staleLightIds.Count; i++) {
      registeredLights.Remove(staleLightIds[i]);
    }
    staleLightIds.Clear();
  }

  void ResolveShadowSortingLayer() {
    var layerName = string.IsNullOrWhiteSpace(shadowSortingLayer)
      ? DefaultShadowSortingLayer
      : shadowSortingLayer.Trim();
    shadowSortingLayerId = SortingLayer.NameToID(layerName);
  }

  void EnsureShadowRoot() {
    if (shadowRoot != null) {
      return;
    }

    var rootObject = new GameObject("__ProjectedCharacterShadows");
    rootObject.hideFlags = HideFlags.DontSave;
    shadowRoot = rootObject.transform;
    shadowRoot.SetParent(transform, false);
  }

  void ClaimAsCurrentManager() {
    if (instance != null && instance != this) {
      instance.SetShadowRootActive(false);
    }

    instance = this;
    SetShadowRootActive(true);
  }

  void SetShadowRootActive(bool active) {
    if (active) {
      EnsureShadowRoot();
    }
    if (shadowRoot == null) {
      return;
    }

    shadowRoot.gameObject.SetActive(active);
  }

  static SceneLighting2D ResolveFallbackManager() {
    for (var i = registeredManagers.Count - 1; i >= 0; i--) {
      var candidate = registeredManagers[i];
      if (candidate == null) {
        registeredManagers.RemoveAt(i);
        continue;
      }
      if (candidate.isActiveAndEnabled) {
        return candidate;
      }
    }

    return null;
  }
}
