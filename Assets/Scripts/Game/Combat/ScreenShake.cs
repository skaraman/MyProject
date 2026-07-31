using UnityEngine;

[DisallowMultipleComponent]
public sealed class ScreenShake : MonoBehaviour {
  const float DefaultDurationSeconds = 0.16f;

  Vector3 appliedOffset;
  float remainingSeconds;
  float durationSeconds;
  float amplitude;

  public static void Play(
    EndlessNumber actualDamage,
    EndlessNumber maximumHealth,
    float shakeFactor
  ) {
    if (actualDamage == null ||
        !actualDamage.IsPositive ||
        maximumHealth == null ||
        !maximumHealth.IsPositive ||
        shakeFactor <= 0f) {
      return;
    }

    var damageRatio = actualDamage.RatioTo(maximumHealth);
    if (double.IsNaN(damageRatio) || damageRatio <= 0d) {
      return;
    }

    var mainCamera = Camera.main;
    if (mainCamera == null) {
      return;
    }

    var shake = mainCamera.GetComponent<ScreenShake>();
    if (shake == null) {
      shake = mainCamera.gameObject.AddComponent<ScreenShake>();
    }

    var hitAmplitude = shakeFactor * Mathf.Clamp01((float)damageRatio);
    shake.Begin(hitAmplitude, DefaultDurationSeconds);
  }

  void Begin(float hitAmplitude, float hitDurationSeconds) {
    amplitude = Mathf.Max(amplitude, hitAmplitude);
    durationSeconds = Mathf.Max(durationSeconds, hitDurationSeconds);
    remainingSeconds = Mathf.Max(remainingSeconds, hitDurationSeconds);
  }

  void LateUpdate() {
    var basePosition = transform.localPosition - appliedOffset;
    appliedOffset = Vector3.zero;

    if (remainingSeconds > 0f && amplitude > 0f) {
      remainingSeconds = Mathf.Max(0f, remainingSeconds - Time.unscaledDeltaTime);
      var strength = durationSeconds > 0f
        ? remainingSeconds / durationSeconds
        : 0f;
      appliedOffset = Random.insideUnitCircle * (amplitude * strength);
    } else {
      amplitude = 0f;
      durationSeconds = 0f;
    }

    transform.localPosition = basePosition + appliedOffset;
  }

  void OnDisable() {
    transform.localPosition -= appliedOffset;
    appliedOffset = Vector3.zero;
    remainingSeconds = 0f;
    durationSeconds = 0f;
    amplitude = 0f;
  }
}
