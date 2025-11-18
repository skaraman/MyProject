//using System.Numerics;
using UnityEngine;

public class TransformWrapper : TimeScaledTransform {
  public float x, y, z, rx, ry, rz, sx, sy, sz;
  private Vector3 lastPos, lastRot, lastScale;
  private Transform cachedLocalTransform;

  void Start() {
    cachedLocalTransform = transform;
    lastPos = cachedLocalTransform.localPosition;
    lastRot = cachedLocalTransform.localRotation.eulerAngles;
    lastScale = cachedLocalTransform.localScale;
    x = lastPos.x; y = lastPos.y; z = lastPos.z;
    rx = lastRot.x; ry = lastRot.y; rz = lastRot.z;
    sx = lastScale.x; sy = lastScale.y; sz = lastScale.z;
  }

  [ForceUpdate]
  void Update() {
    if (cachedLocalTransform == null) cachedLocalTransform = transform;

    var targetPos = new Vector3(x, y, z);
    var currentPos = cachedLocalTransform.localPosition;
    if (targetPos != lastPos || currentPos != lastPos) {
      if (targetPos != lastPos) {
        cachedLocalTransform.localPosition = targetPos;
        lastPos = targetPos;
      }
      else {
        x = currentPos.x; y = currentPos.y; z = currentPos.z;
        lastPos = currentPos;
      }
    }

    var targetRot = new Vector3(rx, ry, rz);
    var currentRot = cachedLocalTransform.localRotation.eulerAngles;
    if (targetRot != lastRot || currentRot != lastRot) {
      if (targetRot != lastRot) {
        cachedLocalTransform.localRotation = Quaternion.Euler(targetRot);
        lastRot = targetRot;
      }
      else {
        rx = currentRot.x; ry = currentRot.y; rz = currentRot.z;
        lastRot = currentRot;
      }
    }

    var targetScale = new Vector3(sx, sy, sz);
    var currentScale = cachedLocalTransform.localScale;
    if (targetScale != lastScale || currentScale != lastScale) {
      if (targetScale != lastScale) {
        cachedLocalTransform.localScale = targetScale;
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
  public int timeScaleIndex = 1;
  Vector3 prevPosition;
  Vector3 prevRotation;
  Vector3 prevScale;
  private Transform cachedTransform;

  void Start() {
    cachedTransform = transform;
    prevPosition = cachedTransform.position;
    prevRotation = cachedTransform.eulerAngles;
    prevScale = cachedTransform.localScale;
  }

  void LateUpdate() {
    if (cachedTransform == null) cachedTransform = transform;
    if (!TimeScale.Factors.ContainsKey(timeScaleIndex)) return;
    var factor = TimeScale.Factors[timeScaleIndex];

    var posDiff = cachedTransform.position - prevPosition;
    var rotDiff = cachedTransform.eulerAngles - prevRotation;
    var scaleDiff = cachedTransform.localScale - prevScale;

    var newPos = prevPosition + posDiff * factor;
    var newRot = prevRotation + rotDiff * factor;
    var newScale = prevScale + scaleDiff * factor;

    //Debug.Log($"[TimeScale] Index: {timeScaleIndex}, Factor: {factor}, PosDiff: {posDiff}, NewPos: {newPos}");

    cachedTransform.position = newPos;
    cachedTransform.eulerAngles = newRot;
    cachedTransform.localScale = newScale;

    prevPosition = newPos;
    prevRotation = newRot;
    prevScale = newScale;
  }
}
