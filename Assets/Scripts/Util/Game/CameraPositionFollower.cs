using UnityEngine;

[DisallowMultipleComponent]
public class CameraPositionFollower : MonoBehaviour {
  [SerializeField] Camera targetCamera;
  [SerializeField] bool useMainCameraWhenEmpty = true;
  [SerializeField] bool captureInitialOffset;
  [SerializeField] Vector3 offset;
  [SerializeField] bool followX = true;
  [SerializeField] bool followY = true;
  [SerializeField] bool followZ;
  [SerializeField, Min(0f)] float smoothTime = 0.1f;
  [SerializeField] bool logSetup;

  Transform cachedTransform;
  Camera resolvedCamera;
  Vector3 followVelocity;
  bool hasCapturedOffset;
  bool hasLoggedMissingCamera;

  void Awake() {
    cachedTransform = transform;
  }

  void Reset() {
    targetCamera = Camera.main;
    followX = true;
    followY = true;
    followZ = false;
    smoothTime = 0.1f;
  }

  void OnEnable() {
    EnsureTransform();
    ResolveCamera();
  }

  void LateUpdate() {
    EnsureTransform();
    if (!ResolveCamera()) return;

    var currentPosition = cachedTransform.position;
    var targetPosition = GetTargetPosition(currentPosition);
    cachedTransform.position = GetNextPosition(currentPosition, targetPosition);
  }

  void OnValidate() {
    if (smoothTime < 0f) smoothTime = 0f;
  }

  bool ResolveCamera() {
    var nextCamera = GetNextCamera();
    if (nextCamera == null) {
      LogMissingCameraOnce();
      return false;
    }

    hasLoggedMissingCamera = false;
    if (resolvedCamera == nextCamera) {
      CaptureOffsetIfNeeded("camera_active");
      return true;
    }

    resolvedCamera = nextCamera;
    targetCamera = nextCamera;
    followVelocity = Vector3.zero;
    hasCapturedOffset = false;
    CaptureOffsetIfNeeded("camera_resolved");
    LogResolvedCamera();
    return true;
  }

  Camera GetNextCamera() {
    if (targetCamera != null) return targetCamera;
    if (!useMainCameraWhenEmpty) return null;
    return Camera.main;
  }

  void CaptureOffsetIfNeeded(string reason) {
    if (!captureInitialOffset || hasCapturedOffset || resolvedCamera == null) return;

    offset = cachedTransform.position - resolvedCamera.transform.position;
    hasCapturedOffset = true;

    if (!ShouldLogSetup()) return;
    Debug.Log(
      "[CameraPositionFollower] Captured offset reason='" + reason +
      "' object='" + cachedTransform.name +
      "' camera='" + resolvedCamera.name +
      "' offset=" + offset,
      this
    );
  }

  Vector3 GetTargetPosition(Vector3 currentPosition) {
    var targetPosition = resolvedCamera.transform.position + offset;

    if (!followX) {
      targetPosition.x = currentPosition.x;
      followVelocity.x = 0f;
    }

    if (!followY) {
      targetPosition.y = currentPosition.y;
      followVelocity.y = 0f;
    }

    if (!followZ) {
      targetPosition.z = currentPosition.z;
      followVelocity.z = 0f;
    }

    return targetPosition;
  }

  Vector3 GetNextPosition(Vector3 currentPosition, Vector3 targetPosition) {
    if (smoothTime <= 0f) return targetPosition;
    return Vector3.SmoothDamp(currentPosition, targetPosition, ref followVelocity, smoothTime);
  }

  void EnsureTransform() {
    if (cachedTransform == null) cachedTransform = transform;
  }

  void LogResolvedCamera() {
    if (!ShouldLogSetup()) return;

    Debug.Log(
      "[CameraPositionFollower] Bound object='" + cachedTransform.name +
      "' camera='" + resolvedCamera.name +
      "' capture_initial_offset=" + (captureInitialOffset ? "1" : "0") +
      " offset=" + offset +
      " follow_xyz=" + (followX ? "1" : "0") + (followY ? "1" : "0") + (followZ ? "1" : "0") +
      " smooth_time=" + smoothTime,
      this
    );
  }

  void LogMissingCameraOnce() {
    if (hasLoggedMissingCamera) return;

    hasLoggedMissingCamera = true;
    Debug.LogWarning("[CameraPositionFollower] No target camera available for object='" + gameObject.name + "'.", this);
  }

  bool ShouldLogSetup() {
    return logSetup &&
           SpriteStreamingRuntimeSettings.EnableVerboseRuntimeConsoleLogs &&
           (Application.isEditor || Debug.isDebugBuild);
  }
}
