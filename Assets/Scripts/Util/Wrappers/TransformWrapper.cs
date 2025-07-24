//using System.Numerics;
using UnityEngine;

[ExecuteAlways]
public class TransformWrapper : TimeScaledTransform {
  public float x, y, z, rx, ry, rz, sx, sy, sz;
  private Vector3 lastPos, lastRot, lastScale;

  void Start() {
    lastPos = transform.localPosition;
    lastRot = transform.localRotation.eulerAngles;
    lastScale = transform.localScale;
    x = lastPos.x; y = lastPos.y; z = lastPos.z;
    rx = lastRot.x; ry = lastRot.y; rz = lastRot.z;
    sx = lastScale.x; sy = lastScale.y; sz = lastScale.z;
  }

  [ForceUpdate]
  void Update() {
    var targetPos = new Vector3(x, y, z);
    var currentPos = transform.localPosition;
    if (targetPos != lastPos || currentPos != lastPos) {
      if (targetPos != lastPos) {
        transform.localPosition = targetPos;
        lastPos = targetPos;
      }
      else {
        x = currentPos.x; y = currentPos.y; z = currentPos.z;
        lastPos = currentPos;
      }
    }

    var targetRot = new Vector3(rx, ry, rz);
    var currentRot = transform.localRotation.eulerAngles;
    if (targetRot != lastRot || currentRot != lastRot) {
      if (targetRot != lastRot) {
        transform.localRotation = Quaternion.Euler(targetRot);
        lastRot = targetRot;
      }
      else {
        rx = currentRot.x; ry = currentRot.y; rz = currentRot.z;
        lastRot = currentRot;
      }
    }

    var targetScale = new Vector3(sx, sy, sz);
    var currentScale = transform.localScale;
    if (targetScale != lastScale || currentScale != lastScale) {
      if (targetScale != lastScale) {
        transform.localScale = targetScale;
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

  void Start() {
    prevPosition = transform.position;
    prevRotation = transform.eulerAngles;
    prevScale = transform.localScale;
  }

  void LateUpdate() {
    if (!TimeScale.Factors.ContainsKey(timeScaleIndex)) return;
    var factor = TimeScale.Factors[timeScaleIndex];

    var posDiff = transform.position - prevPosition;
    var rotDiff = transform.eulerAngles - prevRotation;
    var scaleDiff = transform.localScale - prevScale;

    var newPos = prevPosition + posDiff * factor;
    var newRot = prevRotation + rotDiff * factor;
    var newScale = prevScale + scaleDiff * factor;

    //Debug.Log($"[TimeScale] Index: {timeScaleIndex}, Factor: {factor}, PosDiff: {posDiff}, NewPos: {newPos}");

    transform.position = newPos;
    transform.eulerAngles = newRot;
    transform.localScale = newScale;

    prevPosition = newPos;
    prevRotation = newRot;
    prevScale = newScale;
  }
}