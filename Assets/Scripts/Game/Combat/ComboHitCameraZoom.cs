using UnityEngine;

[DisallowMultipleComponent]
public sealed class ComboHitCameraZoom : MonoBehaviour {
  const float PulseDurationSeconds = 0.2f;
  const float BaseZoomAmount = 0.5f;
  const float AdditionalZoomPerHit = 0.1f;

  static ComboHitCameraZoom instance;

  Camera targetCamera;
  float duration;
  float elapsed;
  float zoomAmount;
  float appliedZoom;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetRuntimeState() {
    instance = null;
  }

  public static void Play(int comboHitNumber) {
    if (!Application.isPlaying || comboHitNumber <= 0) return;

    var camera = Camera.main;
    if (camera == null) return;
    if (instance == null || instance.targetCamera != camera) {
      instance = camera.GetComponent<ComboHitCameraZoom>();
      if (instance == null) instance = camera.gameObject.AddComponent<ComboHitCameraZoom>();
      instance.targetCamera = camera;
    }

    instance.Begin(comboHitNumber);
  }

  void Awake() {
    targetCamera = GetComponent<Camera>();
    if (instance != null && instance != this) {
      Destroy(this);
      return;
    }
    instance = this;
  }

  void Begin(int comboHitNumber) {
    RestoreCamera();
    elapsed = 0f;
    duration = PulseDurationSeconds;
    zoomAmount = BaseZoomAmount + AdditionalZoomPerHit * Mathf.Max(0, comboHitNumber - 1);
    enabled = true;
  }

  void LateUpdate() {
    if (targetCamera == null || duration <= 0f) {
      enabled = false;
      return;
    }

    RestoreCamera();
    elapsed += Time.unscaledDeltaTime;
    var progress = Mathf.Clamp01(elapsed / duration);
    var pulse = Mathf.Sin(progress * Mathf.PI);
    ApplyZoom(zoomAmount * pulse);
    if (progress < 1f) return;

    RestoreCamera();
    enabled = false;
  }

  void ApplyZoom(float amount) {
    if (targetCamera.orthographic) {
      appliedZoom = Mathf.Min(amount, Mathf.Max(0f, targetCamera.orthographicSize - 0.01f));
      targetCamera.orthographicSize -= appliedZoom;
      return;
    }

    appliedZoom = Mathf.Min(amount * 4f, Mathf.Max(0f, targetCamera.fieldOfView - 1f));
    targetCamera.fieldOfView -= appliedZoom;
  }

  void RestoreCamera() {
    if (targetCamera == null || appliedZoom <= 0f) return;
    if (targetCamera.orthographic) {
      targetCamera.orthographicSize += appliedZoom;
    } else {
      targetCamera.fieldOfView += appliedZoom;
    }
    appliedZoom = 0f;
  }

  void OnDisable() {
    RestoreCamera();
  }

  void OnDestroy() {
    RestoreCamera();
    if (instance == this) instance = null;
  }
}
