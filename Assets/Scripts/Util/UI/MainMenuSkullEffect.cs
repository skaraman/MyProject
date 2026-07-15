using System;
using System.Collections.Generic;
using System.Globalization;
using CustomInspector;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MainMenuSkullEffect : MonoBehaviour {
  [SerializeField, Min(0.1f)] float minimumLifetime = 4.5f;
  [SerializeField, Min(0.1f)] float maximumLifetime = 6.5f;
  [SerializeField, Min(0f)] float minimumRespawnDelay = 2.5f;
  [SerializeField, Min(0f)] float maximumRespawnDelay = 5f;
  [SerializeField, Min(0f)] float startScaleMultiplier = 1.5f;
  [SerializeField, Min(0f)] float endScaleMultiplier = 3.75f;

  Skull[] skulls = Array.Empty<Skull>();
  Action blackscreenTransparentOff;
  bool isPlaying;

  sealed class Skull {
    public TransformWrapper wrapper;
    public SpriteRenderer renderer;
    public Vector3 origin;
    public Vector3 baseScale;
    public Vector2 configuredDrift;
    public Vector2 randomDrift;
    public Vector2 cycleDrift;
    public float visibleAlpha;
    public float lifetime;
    public float timer;
    public float waitDuration;
    public bool isVisible;
  }

  void Awake() {
    CacheSkulls();
  }

  void OnEnable() {
    if (skulls.Length == 0) {
      CacheSkulls();
    }

    StopEffect();
    blackscreenTransparentOff = MessageBus.On(
      SingleSceneManager.BlackscreenFullyTransparentTopic,
      StartEffect
    );

    if (SingleSceneManager.IsBlackscreenFullyTransparent) {
      StartEffect();
    }
  }

  void OnDisable() {
    blackscreenTransparentOff?.Invoke();
    blackscreenTransparentOff = null;
    StopEffect();
  }

  void Update() {
    if (!isPlaying) return;

    var deltaTime = TimeScale.GetDeltaTime(this);
    for (var i = 0; i < skulls.Length; i++) {
      UpdateSkull(skulls[i], deltaTime);
    }
  }

  void CacheSkulls() {
    var animations = GetComponentsInChildren<AnimateFields>(true);
    var foundSkulls = new List<Skull>(animations.Length);

    for (var i = 0; i < animations.Length; i++) {
      var animation = animations[i];
      var wrapper = animation.target as TransformWrapper;
      if (wrapper == null) continue;

      var renderer = animation.GetComponent<SpriteRenderer>();
      if (renderer == null) continue;

      var skull = BuildSkull(animation, wrapper, renderer);
      animation.enabled = false;
      foundSkulls.Add(skull);
    }

    skulls = foundSkulls.ToArray();
  }

  Skull BuildSkull(
    AnimateFields animation,
    TransformWrapper wrapper,
    SpriteRenderer renderer
  ) {
    var origin = wrapper.transform.localPosition;
    origin.x = ReadValue(animation.fromValues, "x", origin.x);
    origin.y = ReadValue(animation.fromValues, "y", origin.y);

    var baseScale = wrapper.transform.localScale;
    baseScale.x = ReadValue(animation.fromValues, "sx", baseScale.x);
    baseScale.y = ReadValue(animation.fromValues, "sy", baseScale.y);

    var configuredDrift = Vector2.zero;
    var randomDrift = Vector2.zero;
    if (animation.sequence != null && animation.sequence.Count > 0) {
      var step = animation.sequence[0];
      if (step != null) {
        configuredDrift.x = ReadValue(step.props, "x", 0f);
        configuredDrift.y = ReadValue(step.props, "y", 0f);
        randomDrift.x = ReadPositiveValue(step.randomProps, "x");
        randomDrift.y = ReadPositiveValue(step.randomProps, "y");
      }
    }

    return new Skull {
      wrapper = wrapper,
      renderer = renderer,
      origin = origin,
      baseScale = baseScale,
      configuredDrift = configuredDrift,
      randomDrift = randomDrift,
      visibleAlpha = renderer.color.a
    };
  }

  void StartEffect() {
    if (isPlaying) return;

    isPlaying = true;
    ScheduleInitialSpawns();
  }

  void StopEffect() {
    isPlaying = false;

    for (var i = 0; i < skulls.Length; i++) {
      ApplyHiddenState(skulls[i]);
    }
  }

  void ScheduleInitialSpawns() {
    var initialSpawnWindow = maximumLifetime + maximumRespawnDelay;
    initialSpawnWindow = Mathf.Max(initialSpawnWindow, 0f);

    for (var i = 0; i < skulls.Length; i++) {
      var skull = skulls[i];
      BeginWait(skull);
      skull.waitDuration = UnityEngine.Random.Range(0f, initialSpawnWindow);
    }

    if (skulls.Length == 0) return;

    var firstSkullIndex = UnityEngine.Random.Range(0, skulls.Length);
    skulls[firstSkullIndex].waitDuration = 0f;
  }

  void UpdateSkull(Skull skull, float deltaTime) {
    skull.timer += deltaTime;

    if (skull.isVisible) {
      UpdateVisibleSkull(skull);
      return;
    }

    UpdateWaitingSkull(skull);
  }

  void UpdateVisibleSkull(Skull skull) {
    if (skull.timer < skull.lifetime) {
      ApplyVisibleState(skull);
      return;
    }

    BeginWait(skull);
  }

  void UpdateWaitingSkull(Skull skull) {
    if (skull.timer < skull.waitDuration) return;

    BeginVisibleCycle(skull);
    ApplyVisibleState(skull);
  }

  void BeginVisibleCycle(Skull skull) {
    skull.isVisible = true;
    skull.timer = 0f;
    skull.lifetime = RandomLifetime();
    skull.cycleDrift = skull.configuredDrift;
    skull.cycleDrift.x += UnityEngine.Random.Range(0f, skull.randomDrift.x);
    skull.cycleDrift.y += UnityEngine.Random.Range(0f, skull.randomDrift.y);
  }

  void BeginWait(Skull skull) {
    skull.isVisible = false;
    skull.timer = 0f;
    skull.waitDuration = RandomRespawnDelay();
    ApplyHiddenState(skull);
  }

  void ApplyVisibleState(Skull skull) {
    var progress = Mathf.Clamp01(skull.timer / skull.lifetime);
    var fadeProgress = Mathf.SmoothStep(0f, 1f, progress);
    var scaleMultiplier = Mathf.Lerp(
      startScaleMultiplier,
      endScaleMultiplier,
      fadeProgress
    );

    var position = skull.origin;
    position.x += skull.cycleDrift.x * progress;
    position.y += skull.cycleDrift.y * progress;

    var scale = skull.baseScale;
    scale.x *= scaleMultiplier;
    scale.y *= scaleMultiplier;

    var color = skull.renderer.color;
    color.a = Mathf.Lerp(skull.visibleAlpha, 0f, fadeProgress);

    ApplyTransform(skull, position, scale);
    skull.renderer.color = color;
  }

  void ApplyHiddenState(Skull skull) {
    var scale = skull.baseScale;
    scale.x *= startScaleMultiplier;
    scale.y *= startScaleMultiplier;

    var color = skull.renderer.color;
    color.a = 0f;

    ApplyTransform(skull, skull.origin, scale);
    skull.renderer.color = color;
  }

  static void ApplyTransform(Skull skull, Vector3 position, Vector3 scale) {
    skull.wrapper.x = position.x;
    skull.wrapper.y = position.y;
    skull.wrapper.z = position.z;
    skull.wrapper.sx = scale.x;
    skull.wrapper.sy = scale.y;
    skull.wrapper.sz = scale.z;
    skull.wrapper.transform.localPosition = position;
    skull.wrapper.transform.localScale = scale;
  }

  float RandomLifetime() {
    var minimum = Mathf.Max(minimumLifetime, 0.1f);
    var maximum = Mathf.Max(maximumLifetime, minimum);
    return UnityEngine.Random.Range(minimum, maximum);
  }

  float RandomRespawnDelay() {
    var minimum = Mathf.Max(minimumRespawnDelay, 0f);
    var maximum = Mathf.Max(maximumRespawnDelay, minimum);
    return UnityEngine.Random.Range(minimum, maximum);
  }

  static float ReadPositiveValue(
    SerializableSortedDictionary<string, string> values,
    string key
  ) {
    var value = ReadValue(values, key, 0f);
    return Mathf.Max(value, 0f);
  }

  static float ReadValue(
    SerializableSortedDictionary<string, string> values,
    string key,
    float fallback
  ) {
    if (values == null) return fallback;
    if (!values.TryGetValue(key, out var textValue)) return fallback;

    var style = NumberStyles.Float;
    if (!float.TryParse(textValue, style, CultureInfo.InvariantCulture, out var value)) {
      return fallback;
    }

    return value;
  }
}
