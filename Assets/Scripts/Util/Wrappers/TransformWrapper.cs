//using System.Numerics;
using UnityEngine;

[ExecuteAlways]
public class TransformWrapper : TimeScaledTransform {
  public float x, y, z, rx, ry, rz, sx, sy, sz;
  private Vector3 lastPos, lastRot, lastScale;

  void Start() {
    InitializeTimeScaleTracking();
    cachedTransform = transform;
    lastPos = cachedTransform.localPosition;
    lastRot = cachedTransform.localRotation.eulerAngles;
    lastScale = cachedTransform.localScale;
    x = lastPos.x; y = lastPos.y; z = lastPos.z;
    rx = lastRot.x; ry = lastRot.y; rz = lastRot.z;
    sx = lastScale.x; sy = lastScale.y; sz = lastScale.z;
  }

  [ForceUpdate]
  void Update() {
#if UNITY_EDITOR
    if (UnityEditor.BuildPipeline.isBuildingPlayer) return;
#endif
    if (cachedTransform == null) cachedTransform = transform;

    var targetPos = new Vector3(x, y, z);
    var currentPos = cachedTransform.localPosition;
    if (targetPos != lastPos || currentPos != lastPos) {
      if (targetPos != lastPos) {
        cachedTransform.localPosition = targetPos;
        lastPos = targetPos;
      }
      else {
        x = currentPos.x; y = currentPos.y; z = currentPos.z;
        lastPos = currentPos;
      }
    }

    var targetRot = new Vector3(rx, ry, rz);
    var currentRot = cachedTransform.localRotation.eulerAngles;
    if (targetRot != lastRot || currentRot != lastRot) {
      if (targetRot != lastRot) {
        cachedTransform.localRotation = Quaternion.Euler(targetRot);
        lastRot = targetRot;
      }
      else {
        rx = currentRot.x; ry = currentRot.y; rz = currentRot.z;
        lastRot = currentRot;
      }
    }

    var targetScale = new Vector3(sx, sy, sz);
    var currentScale = cachedTransform.localScale;
    if (targetScale != lastScale || currentScale != lastScale) {
      if (targetScale != lastScale) {
        cachedTransform.localScale = targetScale;
        lastScale = targetScale;
      }
      else {
        sx = currentScale.x; sy = currentScale.y; sz = currentScale.z;
        lastScale = currentScale;
      }
    }
  }
}

public class TimeScaledTransform : MonoBehaviour {
  [HideInInspector] public int timeScaleIndex = 1;
  Vector3 prevPosition;
  Vector3 prevRotation;
  Vector3 prevScale;
  Rigidbody2D cachedRigidbody2D;
  Rigidbody cachedRigidbody3D;
  bool hasResolvedDrivenRigidbody;
  bool hasDrivenRigidbody;
  bool timeScaleContextResolved;
  bool isManagedByTimeScale;
  SceneTimeScaleTarget cachedOwnerTarget;
  int cachedLayerIndex = 1;
  int cachedManagerStateVersion = int.MinValue;
  protected Transform cachedTransform;

  void OnEnable() {
    InitializeTimeScaleTracking();
  }

  void Start() {
    InitializeTimeScaleTracking();
  }

  void OnTransformParentChanged() {
    InvalidateTimeScaleContext();
    SyncPreviousState();
  }

  protected void InitializeTimeScaleTracking() {
    cachedTransform = transform;
    SyncPreviousState(cachedTransform.position, cachedTransform.eulerAngles, cachedTransform.localScale);
    InvalidateCachedRuntimeContext();
  }

  void LateUpdate() {
    if (cachedTransform == null) cachedTransform = transform;

    ResolveDrivenRigidbodyState();
    if (hasDrivenRigidbody) {
      SyncPreviousState();
      return;
    }

    var currentPosition = cachedTransform.position;
    var currentRotation = cachedTransform.eulerAngles;
    var currentScale = cachedTransform.localScale;

    var manager = SceneTimeScaleManager.Instance;
    ResolveTimeScaleContextIfNeeded(manager);
    if (!isManagedByTimeScale || manager == null) {
      SyncPreviousState(currentPosition, currentRotation, currentScale);
      return;
    }

    var resolvedLayerIndex = cachedOwnerTarget != null ? cachedOwnerTarget.LayerIndex : cachedLayerIndex;
    var factor = manager.GetEffectiveFactorForLayer(resolvedLayerIndex);
    if (Mathf.Abs(factor - 1f) <= 0.0001f) {
      SyncPreviousState(currentPosition, currentRotation, currentScale);
      return;
    }

    var posDiff = currentPosition - prevPosition;
    var rotDiff = currentRotation - prevRotation;
    var scaleDiff = currentScale - prevScale;
    if (posDiff.sqrMagnitude <= 0.0000001f &&
        rotDiff.sqrMagnitude <= 0.0000001f &&
        scaleDiff.sqrMagnitude <= 0.0000001f) {
      return;
    }

    var newPos = prevPosition + posDiff * factor;
    var newRot = prevRotation + rotDiff * factor;
    var newScale = prevScale + scaleDiff * factor;

    //RuntimeLog.Log($"[TimeScale] Index: {timeScaleIndex}, Factor: {factor}, PosDiff: {posDiff}, NewPos: {newPos}");

    cachedTransform.position = newPos;
    cachedTransform.eulerAngles = newRot;
    cachedTransform.localScale = newScale;

    SyncPreviousState(newPos, newRot, newScale);
  }

  void InvalidateCachedRuntimeContext() {
    cachedRigidbody2D = null;
    cachedRigidbody3D = null;
    hasResolvedDrivenRigidbody = false;
    hasDrivenRigidbody = false;
    InvalidateTimeScaleContext();
  }

  void InvalidateTimeScaleContext() {
    timeScaleContextResolved = false;
    isManagedByTimeScale = false;
    cachedOwnerTarget = null;
    cachedLayerIndex = 1;
    cachedManagerStateVersion = int.MinValue;
  }

  void ResolveDrivenRigidbodyState() {
    if (hasResolvedDrivenRigidbody) {
      if (!hasDrivenRigidbody) return;
      if (cachedRigidbody2D != null || cachedRigidbody3D != null) return;
      hasResolvedDrivenRigidbody = false;
    }

    hasResolvedDrivenRigidbody = true;
    cachedRigidbody2D = null;
    cachedRigidbody3D = null;
    if (cachedTransform != null) {
      cachedTransform.TryGetComponent(out cachedRigidbody2D);
      cachedTransform.TryGetComponent(out cachedRigidbody3D);
    }
    hasDrivenRigidbody = cachedRigidbody2D != null || cachedRigidbody3D != null;
  }

  void ResolveTimeScaleContextIfNeeded(SceneTimeScaleManager manager) {
    var managerStateVersion = SceneTimeScaleManager.StateVersion;
    if (timeScaleContextResolved && cachedManagerStateVersion == managerStateVersion) return;

    timeScaleContextResolved = true;
    cachedManagerStateVersion = managerStateVersion;
    isManagedByTimeScale = false;
    cachedOwnerTarget = null;
    cachedLayerIndex = 1;

    if (manager == null || cachedTransform == null) return;
    if (!manager.TryResolveLayerContext(cachedTransform, out cachedOwnerTarget, out cachedLayerIndex)) return;

    isManagedByTimeScale = true;
  }

  void SyncPreviousState() {
    if (cachedTransform == null) return;
    SyncPreviousState(cachedTransform.position, cachedTransform.eulerAngles, cachedTransform.localScale);
  }

  void SyncPreviousState(Vector3 position, Vector3 rotation, Vector3 scale) {
    prevPosition = position;
    prevRotation = rotation;
    prevScale = scale;
  }
}
