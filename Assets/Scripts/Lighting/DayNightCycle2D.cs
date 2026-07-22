using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Controls a 24-hour Day/Night cycle drive for 2D Global Lights (Sun/Moon and Ambient).
/// Default duration is 24 real-time minutes (1 minute per in-game hour).
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class DayNightCycle2D : MonoBehaviour {
  [Header("Cycle Configuration")]
  [Tooltip("Total real-time minutes for a full 24-hour cycle. 24 minutes = 1 minute per in-game hour.")]
  [SerializeField, Min(0.1f)] float cycleDurationMinutes = 24f;

  [Tooltip("Initial time of day in hours (0.0 = Midnight, 6.0 = Dawn, 12.0 = Noon, 18.0 = Dusk).")]
  [SerializeField, Range(0f, 24f)] float currentHour = 12f;

  [Tooltip("Multiplier for time advancement speed.")]
  [SerializeField, Min(0f)] float timeSpeedMultiplier = 1f;

  [SerializeField] bool pauseTime;

  [Header("Light Targets")]
  [Tooltip("Primary celestial light (Sun). Handles both Sun & Moon if Moon Global Light is not assigned.")]
  [SerializeField] Light2D sunGlobalLight;
  [Tooltip("Optional separate Moon light. If assigned, Sun and Moon lights cross-fade at Dawn/Dusk.")]
  [SerializeField] Light2D moonGlobalLight;
  [SerializeField] Light2D ambientGlobalLight;
  [SerializeField] SceneLighting2D sceneLighting;

  [Header("Sun & Moon Settings")]
  [Tooltip("Color of the Sun during peak daytime (Noon).")]
  [SerializeField] Color daySunColor = new(1f, 0.96f, 0.88f, 1f);
  [SerializeField, Min(0f)] float daySunIntensity = 1.2f;

  [Tooltip("Color of the Moon / Night celestial light.")]
  [SerializeField] Color nightMoonColor = new(0.35f, 0.45f, 0.75f, 1f);
  [SerializeField, Min(0f)] float nightMoonIntensity = 0.3f;

  [Tooltip("Color tint during Dawn (05:00 - 07:00) and Dusk (17:00 - 19:00).")]
  [SerializeField] Color transitionSunColor = new(1f, 0.62f, 0.38f, 1f);

  [Header("Ambient Light Settings")]
  [Tooltip("Ambient light color during peak daytime.")]
  [SerializeField] Color dayAmbientColor = new(0.95f, 0.95f, 1f, 1f);
  [SerializeField, Min(0f)] float dayAmbientIntensity = 1f;

  [Tooltip("Dark blue/violet ambient light during nighttime.")]
  [SerializeField] Color nightAmbientColor = new(0.14f, 0.16f, 0.38f, 1f);
  [SerializeField, Min(0f)] float nightAmbientIntensity = 0.28f;

  [Tooltip("Ambient tint during Dawn / Dusk.")]
  [SerializeField] Color transitionAmbientColor = new(0.72f, 0.48f, 0.52f, 1f);

  [Header("Dynamic 2D Shadows")]
  [SerializeField] bool updateShadowDirection = true;
  [SerializeField] Vector2 morningShadowDir = new(1f, -0.4f);
  [SerializeField] Vector2 noonShadowDir = new(0f, -1f);
  [SerializeField] Vector2 eveningShadowDir = new(-1f, -0.4f);
  [SerializeField] Vector2 nightMoonShadowDir = new(-0.5f, -0.8f);

  // Time tracking
  int lastNotifiedHour = -1;
  int lastNotifiedMinute = -1;

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

  void Awake() {
    if (Application.isPlaying) {
      if (Instance != null && Instance != this) {
        Destroy(gameObject);
        return;
      }
      Instance = this;
    }

    AutoResolveReferences();
    ApplyLightingForCurrentTime();
  }

  void OnEnable() {
    if (Instance == null) {
      Instance = this;
    }
    AutoResolveReferences();
    ApplyLightingForCurrentTime();
  }

  void OnDestroy() {
    if (Instance == this) {
      Instance = null;
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

    AutoResolveReferences();
    ApplyLightingForCurrentTime();
  }

  public void SetTime(float hour) {
    CurrentHour = hour;
  }

  public void SetTime(int hour, int minute) {
    CurrentHour = Mathf.Clamp(hour, 0, 23) + (Mathf.Clamp(minute, 0, 59) / 60f);
  }

  public void Pause() {
    pauseTime = true;
  }

  public void Resume() {
    pauseTime = false;
  }

  void AutoResolveReferences() {
    if (sceneLighting == null) {
      sceneLighting = SceneLighting2D.Current ?? FindAnyObjectByType<SceneLighting2D>();
    }

    if (sunGlobalLight == null || ambientGlobalLight == null) {
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
    float moonIntensityFactor = 0f;
    float nightFactor;

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
      moonIntensityFactor = 1f;

      targetAmbientColor = nightAmbientColor;
      targetAmbientIntensity = nightAmbientIntensity;
      nightFactor = 1f;
    } else if (currentHour >= 5f && currentHour < 7f) {
      // Dawn (Sunrise)
      var t = (currentHour - 5f) / 2f; // 0 -> 1
      var e = SmoothStep(t);

      if (t < 0.5f) {
        var subT = t * 2f;
        targetSunColor = Color.Lerp(nightMoonColor, transitionSunColor, subT);
        targetAmbientColor = Color.Lerp(nightAmbientColor, transitionAmbientColor, subT);
      } else {
        var subT = (t - 0.5f) * 2f;
        targetSunColor = Color.Lerp(transitionSunColor, daySunColor, subT);
        targetAmbientColor = Color.Lerp(transitionAmbientColor, dayAmbientColor, subT);
      }

      if (moonGlobalLight != null) {
        targetSunIntensity = Mathf.Lerp(0f, daySunIntensity, e);
        moonIntensityFactor = 1f - e;
      } else {
        targetSunIntensity = Mathf.Lerp(nightMoonIntensity, daySunIntensity, e);
        moonIntensityFactor = 0f;
      }

      targetAmbientIntensity = Mathf.Lerp(nightAmbientIntensity, dayAmbientIntensity, e);
      nightFactor = 1f - e;
    } else if (currentHour >= 7f && currentHour < 17f) {
      // Day
      targetSunColor = daySunColor;
      targetSunIntensity = daySunIntensity;
      moonIntensityFactor = 0f;

      targetAmbientColor = dayAmbientColor;
      targetAmbientIntensity = dayAmbientIntensity;
      nightFactor = 0f;
    } else {
      // Dusk (17:00 - 19:00 Sunset)
      var t = (currentHour - 17f) / 2f; // 0 -> 1
      var e = SmoothStep(t);

      if (t < 0.5f) {
        var subT = t * 2f;
        targetSunColor = Color.Lerp(daySunColor, transitionSunColor, subT);
        targetAmbientColor = Color.Lerp(dayAmbientColor, transitionAmbientColor, subT);
      } else {
        var subT = (t - 0.5f) * 2f;
        targetSunColor = Color.Lerp(transitionSunColor, nightMoonColor, subT);
        targetAmbientColor = Color.Lerp(transitionAmbientColor, nightAmbientColor, subT);
      }

      if (moonGlobalLight != null) {
        targetSunIntensity = Mathf.Lerp(daySunIntensity, 0f, e);
        moonIntensityFactor = e;
      } else {
        targetSunIntensity = Mathf.Lerp(daySunIntensity, nightMoonIntensity, e);
        moonIntensityFactor = 0f;
      }

      targetAmbientIntensity = Mathf.Lerp(dayAmbientIntensity, nightAmbientIntensity, e);
      nightFactor = e;
    }

    // Apply to Sun Global Light
    if (sunGlobalLight != null) {
      sunGlobalLight.color = targetSunColor;
      sunGlobalLight.intensity = targetSunIntensity;
    }

    // Apply to Moon Global Light (if separate light object exists)
    if (moonGlobalLight != null) {
      moonGlobalLight.color = nightMoonColor;
      moonGlobalLight.intensity = nightMoonIntensity * moonIntensityFactor;
    }

    // Apply to Ambient Global Light
    if (ambientGlobalLight != null) {
      ambientGlobalLight.color = targetAmbientColor;
      ambientGlobalLight.intensity = targetAmbientIntensity;
    }

    // Dynamic 2D Sun Shadow Vector calculation
    Vector2 shadowDir = CalculateSunShadowDirection(currentHour);

    // Synchronize with SceneLighting2D manager if present
    if (sceneLighting != null) {
      sceneLighting.SunShadowDirection = shadowDir;
      sceneLighting.SetNightAmountDirect(nightFactor);
    }
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
