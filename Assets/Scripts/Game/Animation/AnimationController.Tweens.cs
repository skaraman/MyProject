using System;
using System.Collections.Generic;
using UnityEngine;

public partial class AnimationController {
  sealed class BounceTweenState {
    readonly AnimationController owner;
    readonly GameObject target;
    readonly Action moveCompleteHandler;
    readonly Action scaleCompleteHandler;
    readonly Action delayCompleteHandler;

    List<BounceFrame> sequence;
    int frameIndex;
    int moveTweenId = -1;
    int scaleTweenId = -1;
    int delayTweenId = -1;
    bool active;

    public BounceTweenState(AnimationController owner, GameObject target) {
      this.owner = owner;
      this.target = target;
      moveCompleteHandler = OnMoveComplete;
      scaleCompleteHandler = OnScaleComplete;
      delayCompleteHandler = OnDelayComplete;
    }

    public void Start(List<BounceFrame> frames, int index) {
      sequence = frames;
      frameIndex = index;
      active = true;
      StartCurrentFrame();
    }

    public void Stop() {
      active = false;
      sequence = null;
      moveTweenId = -1;
      scaleTweenId = -1;
      delayTweenId = -1;
    }

    void StartCurrentFrame() {
      if (!active ||
          !owner.isPlaying ||
          sequence == null ||
          frameIndex >= sequence.Count ||
          target == null) {
        owner.ClearTweensFor(target);
        return;
      }

      var frame = sequence[frameIndex];
      if (frame == null) {
        frameIndex += 1;
        StartCurrentFrame();
        return;
      }

      var slowDown = owner.SlowDown ? 20f : 1f;
      var duration = frame.duration * slowDown;
      var targetPosition = new Vector3(
        frame.x,
        frame.y,
        target.transform.localPosition.z
      );

      var moveDescr = owner.TrackTween(
        LeanTween.moveLocal(target, targetPosition, duration).setEase(LeanTweenType.linear),
        duration
      );
      if (moveDescr == null) {
        owner.ClearTweensFor(target);
        return;
      }
      moveTweenId = moveDescr.id;
      owner.AddTweenId(target, moveTweenId);
      moveDescr.setOnComplete(moveCompleteHandler);

      var scaleDescr = owner.TrackTween(
        LeanTween.scaleX(target, frame.offset, duration).setEase(LeanTweenType.linear),
        duration
      );
      if (scaleDescr == null) {
        owner.ClearTweensFor(target);
        return;
      }
      scaleTweenId = scaleDescr.id;
      owner.AddTweenId(target, scaleTweenId);
      scaleDescr.setOnComplete(scaleCompleteHandler);

      var delayDescr = owner.TrackTween(
        LeanTween.delayedCall(target, duration, delayCompleteHandler),
        duration
      );
      if (delayDescr == null) {
        owner.ClearTweensFor(target);
        return;
      }
      delayTweenId = delayDescr.id;
      owner.AddTweenId(target, delayTweenId);
    }

    void OnMoveComplete() {
      if (!active) {
        return;
      }
      owner.RemoveTweenId(target, moveTweenId);
      moveTweenId = -1;
    }

    void OnScaleComplete() {
      if (!active) {
        return;
      }
      owner.RemoveTweenId(target, scaleTweenId);
      scaleTweenId = -1;
    }

    void OnDelayComplete() {
      if (!active) {
        return;
      }
      owner.RemoveTweenId(target, delayTweenId);
      delayTweenId = -1;
      frameIndex += 1;
      StartCurrentFrame();
    }
  }

  sealed class HBoxTweenState {
    readonly AnimationController owner;
    readonly GameObject target;
    readonly PolygonCollider2D collider;
    readonly List<Vector2> currentPath;
    readonly List<Vector2> startPoints;
    readonly List<Vector2> endPoints;
    readonly List<Vector2> lerpedPoints;
    readonly Action<float> updateHandler;
    readonly Action completeHandler;

    List<HBox> sequence;
    int frameIndex;
    int tweenId = -1;
    bool active;

    public HBoxTweenState(
      AnimationController owner,
      GameObject target,
      PolygonCollider2D collider,
      int pointCapacity
    ) {
      this.owner = owner;
      this.target = target;
      this.collider = collider;
      currentPath = new List<Vector2>(pointCapacity);
      startPoints = new List<Vector2>(pointCapacity);
      endPoints = new List<Vector2>(pointCapacity);
      lerpedPoints = new List<Vector2>(pointCapacity);
      updateHandler = OnUpdate;
      completeHandler = OnComplete;
    }

    public void EnsureCapacity(int pointCapacity) {
      EnsureListCapacity(currentPath, pointCapacity);
      EnsureListCapacity(startPoints, pointCapacity);
      EnsureListCapacity(endPoints, pointCapacity);
      EnsureListCapacity(lerpedPoints, pointCapacity);
    }

    public void Start(List<HBox> frames, int index) {
      sequence = frames;
      frameIndex = index;
      active = true;
      StartCurrentFrame();
    }

    public void Stop() {
      active = false;
      sequence = null;
      tweenId = -1;
    }

    void StartCurrentFrame() {
      if (!active ||
          !owner.isPlaying ||
          sequence == null ||
          frameIndex >= sequence.Count ||
          target == null ||
          collider == null) {
        owner.ClearTweensFor(target);
        return;
      }

      var targetPath = sequence[frameIndex];
      var authoredPoints = targetPath != null ? targetPath.points : null;
      var endCount = authoredPoints != null ? authoredPoints.Count : 0;
      if (endCount <= 0) {
        frameIndex += 1;
        StartCurrentFrame();
        return;
      }

      if (collider.pathCount == 0) {
        collider.pathCount = 1;
      }
      currentPath.Clear();
      collider.GetPath(0, currentPath);
      var startCount = currentPath.Count;
      var pointCount = Mathf.Max(1, Mathf.Max(startCount, endCount));
      EnsureCapacity(pointCount);
      EnsureListCount(startPoints, pointCount);
      EnsureListCount(endPoints, pointCount);
      EnsureListCount(lerpedPoints, pointCount);

      for (var i = 0; i < pointCount; i++) {
        startPoints[i] = startCount > 0
          ? currentPath[i % startCount]
          : authoredPoints[i % endCount];
        endPoints[i] = authoredPoints[i % endCount];
      }

      var slowDown = owner.SlowDown ? 20f : 1f;
      var duration = (targetPath.d > 0f ? targetPath.d : 0.2f) * slowDown;
      var descr = owner.TrackTween(
        LeanTween.value(target, 0f, 1f, duration).setEase(LeanTweenType.linear),
        duration
      );
      if (descr == null) {
        owner.ClearTweensFor(target);
        return;
      }

      tweenId = descr.id;
      owner.AddTweenId(target, tweenId);
      descr.setOnUpdate(updateHandler);
      descr.setOnComplete(completeHandler);
    }

    void OnUpdate(float value) {
      if (!active || collider == null) {
        return;
      }

      for (var i = 0; i < lerpedPoints.Count; i++) {
        lerpedPoints[i] = Vector2.Lerp(startPoints[i], endPoints[i], value);
      }
      collider.SetPath(0, lerpedPoints);
    }

    void OnComplete() {
      if (!active || collider == null) {
        return;
      }

      collider.SetPath(0, endPoints);
      owner.RemoveTweenId(target, tweenId);
      tweenId = -1;
      frameIndex += 1;
      StartCurrentFrame();
    }

    static void EnsureListCapacity(List<Vector2> points, int requiredCapacity) {
      if (points.Capacity < requiredCapacity) {
        points.Capacity = requiredCapacity;
      }
    }

    static void EnsureListCount(List<Vector2> points, int requiredCount) {
      while (points.Count < requiredCount) {
        points.Add(default);
      }
      if (points.Count > requiredCount) {
        points.RemoveRange(requiredCount, points.Count - requiredCount);
      }
    }
  }

  static readonly Vector2[] NeutralOffensiveHBoxPath = new Vector2[5];

  private void SetBounces(bool resetOffensiveHBoxes = false) {
    CancelAllTweens();
    if (bounceData == null || bounceData.Count == 0 || bounceObjects.Length == 0 || string.IsNullOrEmpty(currentAnimation)) {
      SetHBoxes(resetOffensiveHBoxes);
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
    SetHBoxes(resetOffensiveHBoxes);
  }

  private void StartBounceSequence(GameObject bounceParent, List<BounceFrame> sequence, int index) {
    if (!isPlaying || sequence == null || index >= sequence.Count || bounceParent == null) {
      ClearTweensFor(bounceParent);
      return;
    }
    var state = GetOrCreateBounceTweenState(bounceParent);
    state.Start(sequence, index);
  }

  void PrepareBounceTweenStates() {
    if (bounceObjects == null || bounceObjects.Length <= 0) {
      return;
    }

    for (var i = 0; i < bounceObjects.Length; i++) {
      var target = bounceObjects[i];
      if (target == null) {
        continue;
      }
      GetOrCreateBounceTweenState(target);
    }
  }

  BounceTweenState GetOrCreateBounceTweenState(GameObject target) {
    GetOrCreateActiveTweenIds(target);
    if (!bounceTweenStates.TryGetValue(target, out var state) || state == null) {
      state = new BounceTweenState(this, target);
      bounceTweenStates[target] = state;
    }
    return state;
  }

  private void SetHBoxes(bool resetOffensiveHBoxes) {
    if (resetOffensiveHBoxes) {
      ResetOffensiveHBoxes();
    }
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
        if (!hBoxColliders.TryGetValue(go, out var poly) || poly == null) continue;
        if (hBoxHitBoxes.TryGetValue(go, out var hitBox) && hitBox != null) {
          if (hboxList == null || hboxList.Count == 0) continue;
          if (EsperanzaAbilities.TryResolveAbilityAnimation(currentAnimation, out var abilityAnimation)) {
            hitBox.hitId = abilityAnimation;
          }
          poly.enabled = true;
        }
        LeanTween.cancel(go);
        TimeScale.UnregisterTweens(go);
        StartHBoxSequence(go, poly, hboxList, 0);
      }
    }
  }

  private void ResetOffensiveHBoxes() {
    for (var i = 0; i < hBoxObjects.Length; i++) {
      var go = hBoxObjects[i];
      if (go == null) continue;

      if (!hBoxHitBoxes.TryGetValue(go, out var hitBox) || hitBox == null) continue;

      if (hBoxColliders.TryGetValue(go, out var poly) && poly != null) {
        poly.enabled = false;
        poly.pathCount = 1;
        poly.SetPath(0, NeutralOffensiveHBoxPath);
      }
      hitBox.ResetHitCache();
    }
  }

  private void StartHBoxSequence(GameObject go, PolygonCollider2D collider, List<HBox> sequence, int index) {
    if (!isPlaying || sequence == null || index >= sequence.Count || go == null || collider == null) {
      ClearTweensFor(go);
      return;
    }
    var state = GetOrCreateHBoxTweenState(
      go,
      collider,
      NeutralOffensiveHBoxPath.Length
    );
    state.Start(sequence, index);
  }

  void PrepareHBoxTweenStates() {
    if (hBoxObjects == null || hBoxObjects.Length <= 0) {
      return;
    }

    var pointCapacity = ResolveMaxHBoxPointCount();
    for (var i = 0; i < hBoxObjects.Length; i++) {
      var go = hBoxObjects[i];
      if (go == null) {
        continue;
      }

      if (!hBoxColliders.TryGetValue(go, out var collider) || collider == null) {
        continue;
      }
      GetOrCreateHBoxTweenState(go, collider, pointCapacity);
    }
  }

  HBoxTweenState GetOrCreateHBoxTweenState(
    GameObject go,
    PolygonCollider2D collider,
    int pointCapacity
  ) {
    GetOrCreateActiveTweenIds(go);
    if (!hBoxTweenStates.TryGetValue(go, out var state) || state == null) {
      state = new HBoxTweenState(this, go, collider, pointCapacity);
      hBoxTweenStates[go] = state;
      return state;
    }

    state.EnsureCapacity(pointCapacity);
    return state;
  }

  int ResolveMaxHBoxPointCount() {
    var pointCount = NeutralOffensiveHBoxPath.Length;
    if (hBoxData == null) {
      return pointCount;
    }

    foreach (var part in hBoxData) {
      var animations = part.Value;
      if (animations == null) {
        continue;
      }
      foreach (var animation in animations) {
        var frames = animation.Value;
        if (frames == null) {
          continue;
        }
        for (var i = 0; i < frames.Count; i++) {
          var frame = frames[i];
          if (frame == null || frame.points == null) {
            continue;
          }
          pointCount = Mathf.Max(pointCount, frame.points.Count);
        }
      }
    }
    return pointCount;
  }

  private void CancelAllTweens() {
    foreach (var kvp in activeTweens) {
      var go = kvp.Key;
      if (go != null) {
        if (bounceTweenStates.TryGetValue(go, out var bounceState)) {
          bounceState.Stop();
        }
        if (hBoxTweenStates.TryGetValue(go, out var hBoxState)) {
          hBoxState.Stop();
        }
        LeanTween.cancel(go);
        TimeScale.UnregisterTweens(go);
      }
    }
    foreach (var pair in activeTweens) {
      pair.Value.Clear();
    }
  }

  private void ClearTweensFor(GameObject go) {
    if (go == null) return;
    if (bounceTweenStates.TryGetValue(go, out var bounceState)) {
      bounceState.Stop();
    }
    if (hBoxTweenStates.TryGetValue(go, out var hBoxState)) {
      hBoxState.Stop();
    }
    LeanTween.cancel(go);
    TimeScale.UnregisterTweens(go);
    if (activeTweens.ContainsKey(go)) {
      activeTweens[go].Clear();
    }
  }

  private void AddTweenId(GameObject go, int tweenId) {
    var tweenIds = GetOrCreateActiveTweenIds(go);
    tweenIds.Add(tweenId);
  }

  List<int> GetOrCreateActiveTweenIds(GameObject go) {
    if (!activeTweens.TryGetValue(go, out var tweenIds) || tweenIds == null) {
      tweenIds = new List<int>(3);
      activeTweens[go] = tweenIds;
    }
    return tweenIds;
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
