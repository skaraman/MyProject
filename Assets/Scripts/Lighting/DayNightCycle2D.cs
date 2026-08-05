using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Owns the 24-hour 2D light cycle and projected character-shadow lighting.
/// Default duration is 12 real-time minutes (30 seconds per in-game hour).
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(-900)]
[DisallowMultipleComponent]
public sealed class DayNightCycle2D : MonoBehaviour {
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

  static readonly int GlobalSunLightColorId = Shader.PropertyToID("_GlobalSunLightColor");
  static readonly int GlobalMoonLightColorId = Shader.PropertyToID("_GlobalMoonLightColor");
  static readonly int GlobalSunLightDirectionId = Shader.PropertyToID("_GlobalSunLightDirection");
  static readonly int GlobalMoonLightDirectionId = Shader.PropertyToID("_GlobalMoonLightDirection");
  static readonly Dictionary<ulong, ProjectedShadowLight2D> registeredLights = new();
  static readonly List<ulong> staleLightIds = new();
  static readonly bool[] reservedStencilReferences =
    new bool[ShadowStencilReferenceCapacity];

  [Header("Cycle Configuration")]
  [Tooltip("Total real-time minutes for a full 24-hour cycle. 12 minutes = 30 seconds per in-game hour.")]
  [SerializeField, Min(0.1f)] float cycleDurationMinutes = 12f;

  [Tooltip("Initial time of day in hours (0.0 = Midnight, 6.0 = Dawn, 12.0 = Noon, 18.0 = Dusk).")]
  [SerializeField, Range(0f, 24f)] float currentHour = 12f;

  [Tooltip("Multiplier for time advancement speed.")]
  [SerializeField, Min(0f)] float timeSpeedMultiplier = 1f;

  [SerializeField] bool pauseTime;

  [Header("Light Targets")]
  [Tooltip("Primary celestial light (Sun). Handles both Sun & Moon if Moon Global Light is not assigned.")]
  [SerializeField] Light2D sunGlobalLight;
  [Tooltip("Optional separate Moon light. Moon contribution is kept outside daytime (06:00-18:00).")]
  [SerializeField] Light2D moonGlobalLight;
  [SerializeField] Light2D ambientGlobalLight;

  [Header("Sun & Moon Settings")]
  [Tooltip("Color of the Sun during peak daytime (Noon).")]
  [SerializeField] Color daySunColor = new(1f, 0.93f, 0.8f, 1f);
  [SerializeField, Min(0f)] float daySunIntensity = 1.2f;

  [Tooltip("Cool blue moonlight used to keep nighttime readable.")]
  [SerializeField] Color nightMoonColor = new(0.42f, 0.56f, 0.88f, 1f);
  [SerializeField, Min(0f)] float nightMoonIntensity = 0.38f;

  [Tooltip("Color tint during Dawn (05:00 - 07:00) and Dusk (17:00 - 19:00).")]
  [SerializeField] Color transitionSunColor = new(1f, 0.62f, 0.38f, 1f);

  [Header("Ambient Light Settings")]
  [Tooltip("Ambient light color during peak daytime.")]
  [SerializeField] Color dayAmbientColor = new(0.95f, 0.95f, 1f, 1f);
  [SerializeField, Min(0f)] float dayAmbientIntensity = 0.48f;

  [Tooltip("Readable blue ambient light during nighttime.")]
  [SerializeField] Color nightAmbientColor = new(0.24f, 0.34f, 0.58f, 1f);
  [SerializeField, Min(0f)] float nightAmbientIntensity = 0.42f;

  [Tooltip("Ambient tint during Dawn / Dusk.")]
  [SerializeField] Color transitionAmbientColor = new(0.72f, 0.48f, 0.52f, 1f);

  [Header("Dynamic 2D Shadows")]
  [SerializeField] bool updateShadowDirection = true;
  [SerializeField] Vector2 morningShadowDir = new(1f, -0.4f);
  [SerializeField] Vector2 noonShadowDir = new(0f, -1f);
  [SerializeField] Vector2 eveningShadowDir = new(-1f, -0.4f);
  [SerializeField] Vector2 nightMoonShadowDir = new(-0.5f, -0.8f);

  [Header("Projected Ground Shadows")]
  [SerializeField] string shadowSortingLayer = DefaultShadowSortingLayer;
  [SerializeField] int shadowSortingOrder = 1000;
  [SerializeField, Range(0f, 1f)] float sunShadowOpacity = 0.28f;
  [SerializeField, Range(0f, 1f)] float moonShadowOpacity = 0.1f;
  [SerializeField, Range(0f, 1f)] float localShadowOpacity = 0.24f;
  [SerializeField, Range(0f, 0.5f)] float localLightSwitchHysteresis = 0.15f;

  [Header("Celestial Movement")]
  [Tooltip("Enable Sun and Moon moving across the sky based on time.")]
  [SerializeField] bool enableCelestialMovement = true;
  [Tooltip("The center point of the arc. Defaults to Main Camera if not set.")]
  [SerializeField] Transform skyCenter;
  [Tooltip("Radius of the Sun/Moon arc.")]
  [SerializeField, Min(0f)] float celestialRadius = 20f;
  [Tooltip("Start angle of the Sun (Sunrise). 180 is left, 0 is right.")]
  [SerializeField] float sunRiseAngle = 180f;
  [Tooltip("End angle of the Sun (Sunset).")]
  [SerializeField] float sunSetAngle = 0f;
  [Tooltip("Start angle of the Moon (Dusk).")]
  [SerializeField] float moonRiseAngle = 180f;
  [Tooltip("End angle of the Moon (Dawn).")]
  [SerializeField] float moonSetAngle = 0f;

  // Time tracking
  int lastNotifiedHour = -1;
  int lastNotifiedMinute = -1;
  Action unsubscribeLocationUpdates;
  Transform shadowRoot;
  float daylightFactor;
  float moonlightFactor;
  Vector2 currentSunShadowDirection = Vector2.down;
  int shadowSortingLayerId;

  [SerializeField, Min(1)] int dayCount = 1;

  public static DayNightCycle2D Instance { get; private set; }

  public float CycleDurationMinutes {
    get => cycleDurationMinutes;
    set => cycleDurationMinutes = Mathf.Max(0.1f, value);
  }

  public float CurrentHour {
    get => currentHour;
    set {
      currentHour = Mathf.Repeat(value, 24f);
      ApplyLightingForCurrentTime();
    }
  }

  public int DayCount {
    get => dayCount;
    set => dayCount = Mathf.Max(1, value);
  }

  public float NormalizedTime => currentHour / 24f;
  public int Hour => Mathf.FloorToInt(currentHour) % 24;
  public int Minute => Mathf.FloorToInt((currentHour - Mathf.Floor(currentHour)) * 60f) % 60;
  public bool IsDay => currentHour >= 6f && currentHour < 18f;
  public bool IsNight => !IsDay;

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
  public bool IsPaused {
    get => pauseTime;
    set => pauseTime = value;
  }

  public float SpeedMultiplier {
    get => timeSpeedMultiplier;
    set => timeSpeedMultiplier = Mathf.Max(0f, value);
  }

  /// <summary>
  /// Event triggered when the day count advances (Midnight rollover).
  /// </summary>
  public event Action<int> OnDayChanged;

  /// <summary>
  /// Event triggered whenever the in-game hour changes (0-23).
  /// </summary>
  public event Action<int> OnHourChanged;

  /// <summary>
  /// Event triggered whenever the in-game time (Hour, Minute) updates.
  /// </summary>
  public event Action<int, int> OnTimeChanged;

  /// <summary>
  /// Event triggered when transitioning between Day (06:00-18:00) and Night.
  /// </summary>
  public event Action<bool> OnDayNightStateChanged;

  public string GetPhaseName() {
    if (currentHour >= 5f && currentHour < 7f) return "DAWN";
    if (currentHour >= 7f && currentHour < 17f) return "DAY";
    if (currentHour >= 17f && currentHour < 19f) return "DUSK";
    return "NIGHT";
  }

  public string GetFormattedTime(bool use24Hour = true) {
    var h = Hour;
    var m = Minute;
    if (use24Hour) {
      return $"{h:D2}:{m:D2}";
    }
    var displayHour = h % 12;
    if (displayHour == 0) displayHour = 12;
    var suffix = h >= 12 ? "PM" : "AM";
    return $"{displayHour}:{m:D2} {suffix}";
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetStatics() {
    registeredLights.Clear();
    staleLightIds.Clear();
    Array.Clear(reservedStencilReferences, 0, reservedStencilReferences.Length);
    Instance = null;
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

    registeredLights[ObjectEntityId.GetRawValue(source)] = source;
  }

  public static void UnregisterLight(ProjectedShadowLight2D source) {
    if (source == null) {
      return;
    }

    var sourceId = ObjectEntityId.GetRawValue(source);
    if (!registeredLights.TryGetValue(sourceId, out var registeredSource) ||
        registeredSource != source) {
      return;
    }

    registeredLights.Remove(sourceId);
  }

  void Awake() {
    if (Application.isPlaying) {
      if (Instance != null && Instance != this) {
        Destroy(gameObject);
        return;
      }
      Instance = this;
    }

    AutoResolveReferences();
    ResolveShadowSortingLayer();
    EnsureShadowRoot();
    ApplyLightingForCurrentTime();
  }

  void Start() {
    if (Application.isPlaying) {
      var locId = LocationEnemyData.NormalizeLocationId(LocationManager.currentLocation);
      if (LocationEnemyData.ContainsLocation(locId) &&
          !string.Equals(locId, LocationEnemyData.MainMenuLocationId, StringComparison.OrdinalIgnoreCase)) {
        RandomizeTime();
      }
    }
  }

  void OnEnable() {
    if (Instance == null) {
      Instance = this;
    }
    if (Application.isPlaying && unsubscribeLocationUpdates == null) {
      unsubscribeLocationUpdates = MessageBus.On("LocationUpdated", OnLocationUpdated);
    }
    AutoResolveReferences();
    ResolveShadowSortingLayer();
    SetShadowRootActive(true);
    ApplyLightingForCurrentTime();
  }

  void OnDisable() {
    unsubscribeLocationUpdates?.Invoke();
    unsubscribeLocationUpdates = null;
    SetShadowRootActive(false);
    if (Instance == this) {
      Instance = null;
      ClearCelestialSpecularGlobals();
    }
  }

  void OnDestroy() {
    unsubscribeLocationUpdates?.Invoke();
    unsubscribeLocationUpdates = null;
    if (Instance == this) {
      Instance = null;
      ClearCelestialSpecularGlobals();
    }
  }

  void Update() {
    if (!Application.isPlaying) {
      return;
    }

    if (pauseTime || cycleDurationMinutes <= 0f) {
      return;
    }

    // 24 hours total per cycle.
    // 1 cycle = cycleDurationMinutes * 60 seconds.
    var totalCycleSeconds = cycleDurationMinutes * 60f;
    var hoursPerSecond = 24f / totalCycleSeconds;

    var dt = TimeScale.GetDeltaTime(this);
    var nextHour = currentHour + (dt * hoursPerSecond * timeSpeedMultiplier);
    if (nextHour >= 24f) {
      dayCount += Mathf.FloorToInt(nextHour / 24f);
      OnDayChanged?.Invoke(dayCount);
    }
    currentHour = Mathf.Repeat(nextHour, 24f);

    ApplyLightingForCurrentTime();
    CheckTimeEvents();
  }

  void OnValidate() {
    currentHour = Mathf.Repeat(currentHour, 24f);
    cycleDurationMinutes = Mathf.Max(0.1f, cycleDurationMinutes);
    timeSpeedMultiplier = Mathf.Max(0f, timeSpeedMultiplier);
    sunShadowOpacity = Mathf.Clamp01(sunShadowOpacity);
    moonShadowOpacity = Mathf.Clamp01(moonShadowOpacity);
    localShadowOpacity = Mathf.Clamp01(localShadowOpacity);
    localLightSwitchHysteresis = Mathf.Clamp(localLightSwitchHysteresis, 0f, 0.5f);
    shadowSortingLayerId = 0;

    AutoResolveReferences();
    ApplyLightingForCurrentTime();
  }

  public void SetTime(float hour) {
    CurrentHour = hour;
  }

  public void SetTime(int hour, int minute) {
    CurrentHour = Mathf.Clamp(hour, 0, 23) + (Mathf.Clamp(minute, 0, 59) / 60f);
  }

  public void RandomizeTime() {
    SetTime(UnityEngine.Random.Range(0f, 24f));
    CheckTimeEvents();
  }

  public void Pause() {
    pauseTime = true;
  }

  public void Resume() {
    pauseTime = false;
  }

  void OnLocationUpdated(object payload) {
    var locationId = LocationEnemyData.NormalizeLocationId(Convert.ToString(payload));

    if (!LocationEnemyData.ContainsLocation(locationId) ||
        string.Equals(locationId, LocationEnemyData.MainMenuLocationId, StringComparison.OrdinalIgnoreCase)) {
      return;
    }

    RandomizeTime();
  }

  void AutoResolveReferences() {
    if (sunGlobalLight == null ||
        moonGlobalLight == null ||
        ambientGlobalLight == null) {
      var lights = FindObjectsByType<Light2D>();
      foreach (var light in lights) {
        if (light == null) continue;
        if (sunGlobalLight == null && light.gameObject.name.IndexOf("Sun", StringComparison.OrdinalIgnoreCase) >= 0) {
          sunGlobalLight = light;
        } else if (moonGlobalLight == null && light.gameObject.name.IndexOf("Moon", StringComparison.OrdinalIgnoreCase) >= 0) {
          moonGlobalLight = light;
        } else if (ambientGlobalLight == null && light.lightType == Light2D.LightType.Global && light != sunGlobalLight && light != moonGlobalLight) {
          ambientGlobalLight = light;
        }
      }
    }
  }

  public void ApplyLightingForCurrentTime() {
    Color targetSunColor;
    float targetSunIntensity;
    Color targetAmbientColor;
    float targetAmbientIntensity;

    EvaluateCelestialFactors(currentHour, out daylightFactor, out moonlightFactor);

    // Time ranges:
    // 00:00 - 05:00: Deep Night
    // 05:00 - 07:00: Dawn / Sunrise transition
    // 07:00 - 17:00: Daytime (Peak at 12:00)
    // 17:00 - 19:00: Dusk / Sunset transition
    // 19:00 - 24:00: Deep Night

    if (currentHour < 5f || currentHour >= 19f) {
      // Night
      targetSunColor = nightMoonColor;
      targetSunIntensity = moonGlobalLight != null ? 0f : nightMoonIntensity;

      targetAmbientColor = nightAmbientColor;
      targetAmbientIntensity = nightAmbientIntensity;
    } else if (currentHour >= 5f && currentHour < 7f) {
      // Dawn (Sunrise)
      var t = (currentHour - 5f) / 2f; // 0 -> 1
      var e = SmoothStep(t);

      targetSunColor = BlendThreeColors(
        nightMoonColor,
        transitionSunColor,
        daySunColor,
        t
      );
      targetAmbientColor = BlendThreeColors(
        nightAmbientColor,
        transitionAmbientColor,
        dayAmbientColor,
        t
      );

      if (moonGlobalLight != null) {
        targetSunIntensity = daySunIntensity * daylightFactor;
      } else {
        targetSunIntensity = Mathf.Lerp(nightMoonIntensity, daySunIntensity, e);
      }

      targetAmbientIntensity = Mathf.Lerp(nightAmbientIntensity, dayAmbientIntensity, e);
    } else if (currentHour >= 7f && currentHour < 17f) {
      // Day
      targetSunColor = daySunColor;
      targetSunIntensity = daySunIntensity;

      targetAmbientColor = dayAmbientColor;
      targetAmbientIntensity = dayAmbientIntensity;
    } else {
      // Dusk (17:00 - 19:00 Sunset)
      var t = (currentHour - 17f) / 2f; // 0 -> 1
      var e = SmoothStep(t);

      targetSunColor = BlendThreeColors(
        daySunColor,
        transitionSunColor,
        nightMoonColor,
        t
      );
      targetAmbientColor = BlendThreeColors(
        dayAmbientColor,
        transitionAmbientColor,
        nightAmbientColor,
        t
      );

      if (moonGlobalLight != null) {
        targetSunIntensity = daySunIntensity * daylightFactor;
      } else {
        targetSunIntensity = Mathf.Lerp(daySunIntensity, nightMoonIntensity, e);
      }

      targetAmbientIntensity = Mathf.Lerp(dayAmbientIntensity, nightAmbientIntensity, e);
    }

    // Apply to Sun Global Light
    if (sunGlobalLight != null) {
      sunGlobalLight.color = targetSunColor;
      sunGlobalLight.intensity = targetSunIntensity;
    }

    var appliedMoonIntensity = nightMoonIntensity * moonlightFactor;

    // Apply to Moon Global Light (if separate light object exists)
    if (moonGlobalLight != null) {
      moonGlobalLight.color = nightMoonColor;
      moonGlobalLight.intensity = appliedMoonIntensity;
    }

    // Apply to Ambient Global Light
    if (ambientGlobalLight != null) {
      ambientGlobalLight.color = targetAmbientColor;
      ambientGlobalLight.intensity = targetAmbientIntensity;
    }

    // Dynamic celestial shadow direction.
    currentSunShadowDirection = CalculateSunShadowDirection(currentHour);
    SetCelestialSpecularGlobals(
      targetSunColor,
      targetSunIntensity,
      appliedMoonIntensity,
      currentSunShadowDirection
    );

    UpdateCelestialPositions();
  }

  static void EvaluateCelestialFactors(
    float hour,
    out float sunFactor,
    out float moonFactor
  ) {
    if (hour < 5f || hour >= 19f) {
      sunFactor = 0f;
      moonFactor = 1f;
      return;
    }

    if (hour < 7f) {
      sunFactor = SmoothStep((hour - 5f) / 2f);
    } else if (hour < 17f) {
      sunFactor = 1f;
    } else {
      sunFactor = 1f - SmoothStep((hour - 17f) / 2f);
    }

    // Keep moonlight completely out of the authored daytime window. It fades
    // out during 05:00-06:00 and only starts fading in after 18:00.
    if (hour < 6f) {
      moonFactor = 1f - SmoothStep(hour - 5f);
    } else if (hour < 18f) {
      moonFactor = 0f;
    } else {
      moonFactor = SmoothStep(hour - 18f);
    }
  }

  static Color BlendThreeColors(Color start, Color middle, Color end, float t) {
    if (t < 0.5f) {
      return Color.Lerp(start, middle, SmoothStep(t * 2f));
    }

    return Color.Lerp(middle, end, SmoothStep((t - 0.5f) * 2f));
  }

  Vector2 CalculateSunShadowDirection(float hour) {
    if (!updateShadowDirection) {
      return noonShadowDir;
    }

    if (hour >= 6f && hour <= 18f) {
      // Daylight hours (06:00 to 18:00)
      var dayProgress = (hour - 6f) / 12f; // 0 at dawn, 0.5 at noon, 1 at dusk
      if (dayProgress < 0.5f) {
        var t = dayProgress * 2f;
        return Vector2.Lerp(morningShadowDir, noonShadowDir, SmoothStep(t)).normalized;
      } else {
        var t = (dayProgress - 0.5f) * 2f;
        return Vector2.Lerp(noonShadowDir, eveningShadowDir, SmoothStep(t)).normalized;
      }
    } else {
      // Night hours - Moon shadow direction
      return nightMoonShadowDir.normalized;
    }
  }

  void SetCelestialSpecularGlobals(
    Color sunColor,
    float sunIntensity,
    float moonIntensity,
    Vector2 sunShadowDirection
  ) {
    // Projected shadows travel away from their light source, so negate the shadow
    // direction to get the surface-to-light direction used by the BRDF.
    var sunDirection = new Vector3(-sunShadowDirection.x, -sunShadowDirection.y, 1f).normalized;
    var moonDirection2D = -nightMoonShadowDir.normalized;
    var moonDirection = new Vector3(moonDirection2D.x, moonDirection2D.y, 1f).normalized;
    Shader.SetGlobalColor(GlobalSunLightColorId, sunColor * Mathf.Max(0f, sunIntensity));
    Shader.SetGlobalColor(GlobalMoonLightColorId, nightMoonColor * Mathf.Max(0f, moonIntensity));
    Shader.SetGlobalVector(GlobalSunLightDirectionId, sunDirection);
    Shader.SetGlobalVector(GlobalMoonLightDirectionId, moonDirection);
  }

  static void ClearCelestialSpecularGlobals() {
    Shader.SetGlobalColor(GlobalSunLightColorId, Color.black);
    Shader.SetGlobalColor(GlobalMoonLightColorId, Color.black);
    Shader.SetGlobalVector(GlobalSunLightDirectionId, Vector3.forward);
    Shader.SetGlobalVector(GlobalMoonLightDirectionId, Vector3.forward);
  }

  void UpdateCelestialPositions() {
    if (!enableCelestialMovement) return;

    Vector3 centerPos = Vector3.zero;
    if (skyCenter != null) {
      centerPos = skyCenter.position;
    } else if (Camera.main != null) {
      centerPos = Camera.main.transform.position;
    }

    if (sunGlobalLight != null) {
      float sunT = 0f;
      if (currentHour >= 6f && currentHour <= 18f) {
        sunT = (currentHour - 6f) / 12f;
      } else if (currentHour > 18f) {
        sunT = 1f;
      } else {
        sunT = 0f;
      }

      float sunAngle = Mathf.Lerp(sunRiseAngle, sunSetAngle, sunT) * Mathf.Deg2Rad;
      Vector3 sunOffset = new Vector3(Mathf.Cos(sunAngle), Mathf.Sin(sunAngle), 0f) * celestialRadius;
      sunGlobalLight.transform.position = centerPos + sunOffset;
    }

    if (moonGlobalLight != null) {
      float moonT = 0f;
      if (currentHour >= 18f) {
        moonT = (currentHour - 18f) / 12f;
      } else if (currentHour <= 6f) {
        moonT = (currentHour + 6f) / 12f;
      } else {
        moonT = 1f;
      }

      float moonAngle = Mathf.Lerp(moonRiseAngle, moonSetAngle, moonT) * Mathf.Deg2Rad;
      Vector3 moonOffset = new Vector3(Mathf.Cos(moonAngle), Mathf.Sin(moonAngle), 0f) * celestialRadius;
      moonGlobalLight.transform.position = centerPos + moonOffset;
    }
  }

  public bool TryGetCelestialShadow(
    Vector2 groundPosition,
    int casterSortingLayerId,
    out ShadowProjection projection
  ) {
    projection = default;
    var sunSource = ResolveStrongestCelestial(
      ProjectedShadowLightRole.Sun,
      casterSortingLayerId
    );
    var moonSource = ResolveStrongestCelestial(
      ProjectedShadowLightRole.Moon,
      casterSortingLayerId
    );
    var sunOpacity = sunSource != null
      ? sunShadowOpacity * sunSource.ShadowStrength * daylightFactor
      : 0f;
    var moonOpacity = moonSource != null
      ? moonShadowOpacity * moonSource.ShadowStrength * moonlightFactor
      : 0f;
    if (sunOpacity <= MinimumLightIntensity &&
        moonOpacity <= MinimumLightIntensity) {
      return false;
    }

    if (sunOpacity <= MinimumLightIntensity) {
      projection = CreateCelestialProjection(
        moonSource,
        moonOpacity,
        true,
        groundPosition
      );
      return true;
    }
    if (moonOpacity <= MinimumLightIntensity) {
      projection = CreateCelestialProjection(
        sunSource,
        sunOpacity,
        false,
        groundPosition
      );
      return true;
    }

    var totalOpacity = sunOpacity + moonOpacity;
    var moonWeight = moonOpacity / totalOpacity;
    var sunDirection = ResolveCelestialShadowDirection(
      sunSource,
      false,
      groundPosition
    );
    var moonDirection = ResolveCelestialShadowDirection(
      moonSource,
      true,
      groundPosition
    );
    var blendedDirection = Vector2.Lerp(sunDirection, moonDirection, moonWeight);
    if (blendedDirection.sqrMagnitude <= 0.0001f) {
      blendedDirection = moonWeight >= 0.5f ? moonDirection : sunDirection;
    }

    projection = new ShadowProjection(
      blendedDirection.normalized,
      Mathf.Lerp(sunSource.ProjectionLength, moonSource.ProjectionLength, moonWeight),
      Mathf.Lerp(sunOpacity, moonOpacity, moonWeight)
    );
    return true;
  }

  ShadowProjection CreateCelestialProjection(
    ProjectedShadowLight2D source,
    float opacity,
    bool useMoon,
    Vector2 groundPosition
  ) {
    var direction = ResolveCelestialShadowDirection(source, useMoon, groundPosition);
    return new ShadowProjection(direction, source.ProjectionLength, opacity);
  }

  Vector2 ResolveCelestialShadowDirection(
    ProjectedShadowLight2D source,
    bool useMoon,
    Vector2 groundPosition
  ) {
    if (!source.TryGetDirectionOverride(out var direction)) {
      direction = useMoon
        ? groundPosition - (Vector2)source.SourceLight.transform.position
        : currentSunShadowDirection;
    }
    if (direction.sqrMagnitude <= 0.0001f) {
      direction = Vector2.down;
    }

    return direction.normalized;
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
    return bestDistanceSquared < switchThresholdSquared
      ? bestLightId
      : currentLightId;
  }

  public bool TryGetLocalShadow(
    ulong lightId,
    Vector2 receiverPosition,
    Vector2 groundPosition,
    int casterSortingLayerId,
    out ShadowProjection projection
  ) {
    projection = default;
    if (lightId == 0 || !registeredLights.TryGetValue(lightId, out var source)) {
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
    if (opacity <= MinimumLightIntensity) {
      return false;
    }

    Vector2 direction;
    if (!source.TryGetDirectionOverride(out direction)) {
      direction = groundPosition - (Vector2)sourceLight.transform.position;
    }
    if (direction.sqrMagnitude <= 0.0001f) {
      direction = Vector2.down;
    }

    direction.Normalize();
    projection = new ShadowProjection(direction, source.ProjectionLength, opacity);
    return true;
  }

  ProjectedShadowLight2D ResolveStrongestCelestial(
    ProjectedShadowLightRole lightRole,
    int casterSortingLayerId
  ) {
    PurgeStaleLights();

    Light2D preferredLight = null;
    if (lightRole == ProjectedShadowLightRole.Sun) {
      preferredLight = sunGlobalLight;
    } else if (lightRole == ProjectedShadowLightRole.Moon) {
      preferredLight = moonGlobalLight;
    }

    var preferredSource = preferredLight != null
      ? preferredLight.GetComponent<ProjectedShadowLight2D>()
      : null;
    if (IsEligibleSource(preferredSource, lightRole, casterSortingLayerId)) {
      return preferredSource;
    }

    ProjectedShadowLight2D strongestSource = null;
    var strongestIntensity = MinimumLightIntensity;
    foreach (var pair in registeredLights) {
      var source = pair.Value;
      if (!IsEligibleSource(source, lightRole, casterSortingLayerId)) {
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

    var difference = receiverPosition - (Vector2)source.SourceLight.transform.position;
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
    if (sourceLight == null ||
        !sourceLight.isActiveAndEnabled ||
        sourceLight.intensity <= MinimumLightIntensity) {
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

  void SetShadowRootActive(bool active) {
    if (active) {
      EnsureShadowRoot();
    }
    if (shadowRoot != null) {
      shadowRoot.gameObject.SetActive(active);
    }
  }

  void CheckTimeEvents() {
    var h = Hour;
    var m = Minute;

    if (h != lastNotifiedHour) {
      var wasDay = (lastNotifiedHour >= 6 && lastNotifiedHour < 18);
      var isDayNow = IsDay;
      if (wasDay != isDayNow && lastNotifiedHour != -1) {
        OnDayNightStateChanged?.Invoke(isDayNow);
      }

      lastNotifiedHour = h;
      OnHourChanged?.Invoke(h);
    }

    if (m != lastNotifiedMinute) {
      lastNotifiedMinute = m;
      OnTimeChanged?.Invoke(h, m);
    }
  }

  static float SmoothStep(float t) {
    t = Mathf.Clamp01(t);
    return t * t * (3f - 2f * t);
  }
}
