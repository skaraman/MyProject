using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class DamageNumberArcMotion : MonoBehaviour {
  static readonly int MainColorPropertyId = Shader.PropertyToID("_Color");

  const float MinimumLifetimeSeconds = 0.01f;
  const float MinimumArcHeight = 0.75f;
  const float MaximumArcHeight = 1.05f;
  const float MinimumHorizontalDistance = 0.28f;
  const float MaximumHorizontalDistance = 0.48f;
  const float MinimumFinalHeight = 0.14f;
  const float MaximumFinalHeight = 0.24f;
  const float BounceDurationPercent = 0.2f;
  const float BounceOvershootScale = 1.16f;
  const float FinalScale = 1.3f;

  Coroutine motion;
  readonly List<SpriteRenderer> textRenderers = new(8);
  MaterialPropertyBlock textPropertyBlock;
  Vector3 restingLocalScale;
  float lifetimeSeconds = 1.5f;

  void Awake() {
    textPropertyBlock = new MaterialPropertyBlock();
    restingLocalScale = transform.localScale;
  }

  void OnEnable() {
    StartMotion();
  }

  void OnDisable() {
    if (motion != null) {
      StopCoroutine(motion);
      motion = null;
    }
  }

  public void Play(float durationSeconds) {
    lifetimeSeconds = Mathf.Max(durationSeconds, MinimumLifetimeSeconds);
    if (isActiveAndEnabled) {
      StartMotion();
    }
  }

  public void SetMainColor(FontText fontText, Color color) {
    if (fontText == null) {
      return;
    }

    textRenderers.Clear();
    fontText.GetComponentsInChildren(true, textRenderers);
    for (var i = 0; i < textRenderers.Count; i++) {
      var renderer = textRenderers[i];
      if (renderer != null) {
        renderer.color = Color.white;
        textPropertyBlock.Clear();
        renderer.GetPropertyBlock(textPropertyBlock);
        textPropertyBlock.SetColor(MainColorPropertyId, color);
        renderer.SetPropertyBlock(textPropertyBlock);
      }
    }
  }

  void StartMotion() {
    if (motion != null) {
      StopCoroutine(motion);
    }

    transform.localScale = Vector3.zero;
    motion = StartCoroutine(Animate());
  }

  IEnumerator Animate() {
    var startPosition = transform.position;
    var horizontalDistance = Random.Range(MinimumHorizontalDistance, MaximumHorizontalDistance) *
      (Random.value < 0.5f ? -1f : 1f);
    var apexHeight = Random.Range(MinimumArcHeight, MaximumArcHeight);
    var endPosition = startPosition + new Vector3(
      horizontalDistance,
      Random.Range(MinimumFinalHeight, MaximumFinalHeight),
      0f
    );
    var arcControlPoint = startPosition + new Vector3(
      horizontalDistance * Random.Range(0.35f, 0.65f),
      apexHeight,
      0f
    );

    var elapsedSeconds = 0f;
    while (elapsedSeconds < lifetimeSeconds) {
      var normalizedTime = Mathf.Clamp01(elapsedSeconds / lifetimeSeconds);
      transform.position = EvaluateQuadraticBezier(startPosition, arcControlPoint, endPosition, normalizedTime);
      transform.localScale = restingLocalScale * ResolveScale(normalizedTime);

      elapsedSeconds += Time.deltaTime;
      yield return null;
    }

    transform.position = endPosition;
    transform.localScale = restingLocalScale * FinalScale;
    motion = null;
  }

  static Vector3 EvaluateQuadraticBezier(Vector3 start, Vector3 control, Vector3 end, float time) {
    var inverseTime = 1f - time;
    return inverseTime * inverseTime * start +
      2f * inverseTime * time * control +
      time * time * end;
  }

  static float ResolveScale(float normalizedTime) {
    if (normalizedTime <= BounceDurationPercent) {
      var bounceTime = normalizedTime / BounceDurationPercent;
      return EaseOutBack(bounceTime) * BounceOvershootScale;
    }

    var growthTime = (normalizedTime - BounceDurationPercent) / (1f - BounceDurationPercent);
    return Mathf.Lerp(1f, FinalScale, growthTime);
  }

  static float EaseOutBack(float time) {
    const float overshoot = 1.70158f;
    var inverseTime = time - 1f;
    return 1f + (overshoot + 1f) * inverseTime * inverseTime * inverseTime +
      overshoot * inverseTime * inverseTime;
  }
}
