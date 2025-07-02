using UnityEngine;

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
    if (!TimeScaleController.TimeScaleFactor.ContainsKey(timeScaleIndex)) return;
    var factor = TimeScaleController.TimeScaleFactor[timeScaleIndex];

    var posDiff = transform.position - prevPosition;
    var rotDiff = transform.eulerAngles - prevRotation;
    var scaleDiff = transform.localScale - prevScale;

    var newPos = prevPosition + posDiff * factor;
    var newRot = prevRotation + rotDiff * factor;
    var newScale = prevScale + scaleDiff * factor;

    Debug.Log($"[TimeScale] Index: {timeScaleIndex}, Factor: {factor}, PosDiff: {posDiff}, NewPos: {newPos}");

    transform.position = newPos;
    transform.eulerAngles = newRot;
    transform.localScale = newScale;

    prevPosition = newPos;
    prevRotation = newRot;
    prevScale = newScale;
  }
}