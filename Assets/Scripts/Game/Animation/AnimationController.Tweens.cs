using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class AnimationController {
  private void SetBounces() {
    CancelAllTweens();
    if (bounceData == null || bounceData.Count == 0 || bounceObjects.Length == 0 || string.IsNullOrEmpty(currentAnimation)) {
      SetHBoxes();
      return;
    }
    foreach (KeyValuePair<string, Dictionary<string, List<BounceFrame>>> partPair in bounceData) {
      if (!isPlaying) break;
      string partKey = partPair.Key;
      var animationDict = partPair.Value;
      if (!animationDict.ContainsKey(currentAnimation)) continue;
      var frameSequence = animationDict[currentAnimation];
      if (!bounceObjectByName.TryGetValue(partKey, out var bounceParent)) continue;
      if (bounceParent == null) continue;
      LeanTween.cancel(bounceParent);
      TimeScale.UnregisterTweens(bounceParent);
      StartBounceSequence(bounceParent, frameSequence, 0);
    }
    SetHBoxes();
  }

  private void StartBounceSequence(GameObject bounceParent, List<BounceFrame> sequence, int index) {
    if (!isPlaying || sequence == null || index >= sequence.Count || bounceParent == null) {
      ClearTweensFor(bounceParent);
      if (SlowDown) TogglePause("true");
      return;
    }
    if (!activeTweens.ContainsKey(bounceParent)) {
      activeTweens[bounceParent] = new List<int>();
    }
    var fSlowDown = SlowDown ? 20f : 1f;
    BounceFrame frame = sequence[index];
    Vector3 targetPos = new Vector3(frame.x, frame.y, bounceParent.transform.localPosition.z);
    float duration = frame.duration * fSlowDown;

    var moveDescr = TrackTween(LeanTween.moveLocal(bounceParent, targetPos, duration).setEase(LeanTweenType.linear), duration);
    AddTweenId(bounceParent, moveDescr.id);
    moveDescr.setOnComplete(() => RemoveTweenId(bounceParent, moveDescr.id));

    var scaleDescr = TrackTween(LeanTween.scaleX(bounceParent, frame.offset, duration).setEase(LeanTweenType.linear), duration);
    AddTweenId(bounceParent, scaleDescr.id);
    scaleDescr.setOnComplete(() => RemoveTweenId(bounceParent, scaleDescr.id));

    LTDescr delayDescr = null;
    delayDescr = TrackTween(LeanTween.delayedCall(bounceParent, duration, () => {
      RemoveTweenId(bounceParent, delayDescr.id);
      StartBounceSequence(bounceParent, sequence, index + 1);
    }), duration);
    AddTweenId(bounceParent, delayDescr.id);
  }

  private void SetHBoxes() {
    if (hBoxData == null || hBoxObjects.Length == 0 || string.IsNullOrEmpty(currentAnimation)) return;
    foreach (var kvp in hBoxData) {
      string partKey = kvp.Key;
      var animDict = kvp.Value;
      if (!animDict.ContainsKey(currentAnimation)) continue;
      var hboxList = animDict[currentAnimation];
      if (!hBoxObjectsByName.TryGetValue(partKey, out var partObjects) || partObjects == null) continue;
      for (var i = 0; i < partObjects.Count; i++) {
        var go = partObjects[i];
        if (go == null) continue;
        var poly = go.GetComponent<PolygonCollider2D>();
        if (poly == null) continue;
        LeanTween.cancel(go);
        TimeScale.UnregisterTweens(go);
        StartHBoxSequence(go, poly, hboxList, 0);
      }
    }
  }

  private void StartHBoxSequence(GameObject go, PolygonCollider2D collider, List<HBox> sequence, int index) {
    if (!isPlaying || sequence == null || index >= sequence.Count || go == null || collider == null) {
      ClearTweensFor(go);
      if (SlowDown) TogglePause("true");
      return;
    }
    if (!activeTweens.ContainsKey(go)) {
      activeTweens[go] = new List<int>();
    }
    var fSlowDown = SlowDown ? 20f : 1f;
    var targetPath = sequence[index];
    if (collider.pathCount == 0) collider.pathCount = 1;
    Vector2[] startPoints = collider.GetPath(0);
    Vector2[] endPoints = targetPath.points.ToArray();
    if (endPoints.Length == 0) {
      StartHBoxSequence(go, collider, sequence, index + 1);
      return;
    }
    int startLen = startPoints?.Length ?? 0;
    int endLen = endPoints.Length;
    int len = Mathf.Max(1, Mathf.Max(startLen, endLen));
    Vector2[] s = new Vector2[len];
    Vector2[] e = new Vector2[len];
    Vector2[] lerped = new Vector2[len];
    for (int i = 0; i < len; i++) {
      s[i] = (startLen > 0) ? startPoints[i % startLen] : endPoints[i % endLen];
      e[i] = endPoints[i % endLen];
    }
    float duration = (targetPath.d > 0 ? targetPath.d : 0.2f) * fSlowDown;

    var descr = TrackTween(LeanTween.value(go, 0f, 1f, duration).setEase(LeanTweenType.linear), duration);
    AddTweenId(go, descr.id);
    descr.setOnUpdate((float v) => {
      for (int i = 0; i < len; i++) {
        lerped[i] = Vector2.Lerp(s[i], e[i], v);
      }
      collider.SetPath(0, lerped);
    });
    descr.setOnComplete(() => {
      collider.SetPath(0, e);
      RemoveTweenId(go, descr.id);
      StartHBoxSequence(go, collider, sequence, index + 1);
    });
  }

  private void CancelAllTweens() {
    foreach (var kvp in activeTweens) {
      var go = kvp.Key;
      if (go != null) {
        LeanTween.cancel(go);
        TimeScale.UnregisterTweens(go);
      }
    }
    activeTweens.Clear();
  }

  private void ClearTweensFor(GameObject go) {
    if (go == null) return;
    LeanTween.cancel(go);
    TimeScale.UnregisterTweens(go);
    if (activeTweens.ContainsKey(go)) {
      activeTweens[go].Clear();
      activeTweens.Remove(go);
    }
  }

  private void AddTweenId(GameObject go, int tweenId) {
    if (!activeTweens.ContainsKey(go)) {
      activeTweens[go] = new List<int>();
    }
    activeTweens[go].Add(tweenId);
  }

  private void RemoveTweenId(GameObject go, int tweenId) {
    TimeScale.UnregisterTween(tweenId);
    if (activeTweens.ContainsKey(go)) {
      activeTweens[go].Remove(tweenId);
    }
  }

  LTDescr TrackTween(LTDescr descr, float baseDuration) {
    return TimeScale.RegisterTween(rootTransform, descr, baseDuration);
  }
}
