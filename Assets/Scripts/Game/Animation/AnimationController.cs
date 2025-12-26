using System;
using System.Collections.Generic;
using System.Linq;
using CustomInspector;
using UnityEngine;

/// <summary>
/// Generic animation driver (non-MonoBehaviour). Host behaviours must call Tick/Cleanup and wire data/targets.
/// </summary>
public class AnimationController {
  public string defaultAnimation = "Idle";
  public bool playOnStart;
  public bool SlowDown { get; set; }
  public bool ForceLoop { get; set; }

  public string CurrentAnimation => currentAnimation;
  public bool IsPlaying => isPlaying;
  public bool IsFacingRight => isFacingRight;

  private Transform rootTransform;
  private Vector3 baseScale = Vector3.one;

  private Dictionary<string, AnimData> animationData = new();
  private Dictionary<string, Dictionary<string, string>> interruptData = new();
  private Dictionary<string, Dictionary<string, List<BounceFrame>>> bounceData = new();
  private Dictionary<string, Dictionary<string, List<HBox>>> hBoxData = new();

  private GameObject[] spriteObjects = Array.Empty<GameObject>();
  private GameObject[] bounceObjects = Array.Empty<GameObject>();
  private GameObject[] hBoxObjects = Array.Empty<GameObject>();
  private readonly List<SpriteWithNormals> spriteTargets = new();
  private readonly Dictionary<GameObject, List<int>> activeTweens = new();

  private string currentAnimation;
  private string queuedAnimation;
  private int currentFrame;
  public float animationTimer;
  private bool pingPong;
  private bool isPlaying;
  private bool isFacingRight = true;
  private bool pendingFlip;
  private bool hasResetLeanTween;

  private ComboManager comboManager;

  public void Initialize(
    Transform root,
    IEnumerable<GameObject> sprites,
    IEnumerable<GameObject> bounces,
    IEnumerable<GameObject> hBoxes,
    Dictionary<string, AnimData> animations,
    Dictionary<string, Dictionary<string, string>> interrupts,
    Dictionary<string, Dictionary<string, List<BounceFrame>>> bouncesData,
    Dictionary<string, Dictionary<string, List<HBox>>> hBoxesData,
    string defaultAnim,
    bool autoPlay
  ) {
    rootTransform = root;
    baseScale = root != null ? root.localScale : Vector3.one;
    SetSpriteObjects(sprites);
    SetBounceObjects(bounces);
    SetHBoxObjects(hBoxes);
    ConfigureData(animations, interrupts, bouncesData, hBoxesData);
    defaultAnimation = defaultAnim;
    playOnStart = autoPlay;
    if (playOnStart && !string.IsNullOrEmpty(defaultAnimation) && animationData.Count > 0) {
      PlayAnimation(defaultAnimation, true);
    }
  }

  public void ConfigureData(
    Dictionary<string, AnimData> animations,
    Dictionary<string, Dictionary<string, string>> interrupts,
    Dictionary<string, Dictionary<string, List<BounceFrame>>> bounces = null,
    Dictionary<string, Dictionary<string, List<HBox>>> hboxes = null
  ) {
    animationData = animations ?? new Dictionary<string, AnimData>();
    interruptData = interrupts ?? new Dictionary<string, Dictionary<string, string>>();
    bounceData = bounces ?? new Dictionary<string, Dictionary<string, List<BounceFrame>>>();
    hBoxData = hboxes ?? new Dictionary<string, Dictionary<string, List<HBox>>>();
    
    // Initialize combo manager if not already created
    if (comboManager == null) {
      comboManager = new ComboManager();
    }
    comboManager.Initialize(animationData);
  }

  public void SetSpriteObjects(IEnumerable<GameObject> targets) {
    spriteObjects = targets != null ? targets.ToArray() : Array.Empty<GameObject>();
    CacheSpriteTargets();
  }

  public void SetBounceObjects(IEnumerable<GameObject> targets) {
    bounceObjects = targets != null ? targets.ToArray() : Array.Empty<GameObject>();
  }

  public void SetHBoxObjects(IEnumerable<GameObject> targets) {
    hBoxObjects = targets != null ? targets.ToArray() : Array.Empty<GameObject>();
  }

  public void Tick(float deltaTime) {
    AdvanceAnimation(deltaTime);
  }

  public bool PlayAnimation(string animationName, bool forceRestart = false, bool resolveInterrupts = true) {
    if (animationData == null || !animationData.ContainsKey(animationName)) {
      Debug.LogWarning($"[AnimationController] Animation '{animationName}' missing.");
      return false;
    }
    if (!forceRestart && isPlaying && animationName == currentAnimation) return true;

    // Process combo detection if combo manager is available
    string processedAnimationName = animationName;
    if (comboManager != null && resolveInterrupts && !forceRestart) {
      string comboResult = comboManager.ProcessAnimationRequest(animationName, Time.time);
      if (!string.IsNullOrEmpty(comboResult) && animationData.ContainsKey(comboResult)) {
        processedAnimationName = comboResult;
      }
    }

    if (!resolveInterrupts || forceRestart) {
      currentAnimation = processedAnimationName;
      queuedAnimation = null;
    }
    else {
      if (TryResolveInterrupt(processedAnimationName, out var resolvedAnimation, out var queued)) {
        currentAnimation = resolvedAnimation;
        queuedAnimation = queued;

      }
      else {
        return false;
      }
    }


    animationTimer = 0f;
    pingPong = false;
    isPlaying = true;
    var anim = animationData[currentAnimation];
    currentFrame = anim.start;

    var category = anim.To == 1 ? "To" : anim.To == 2 ? "To2" : currentAnimation;
    SetAnimationCategory(category);
    UpdateSprites(currentFrame);
    SetBounces();
    return true;
  }

  public void ForceAnimation(string animationName = null) {
    string anim = animationName ?? (!string.IsNullOrEmpty(CurrentAnimation) ? CurrentAnimation : defaultAnimation);
    if (string.IsNullOrEmpty(anim)) return;
    PlayAnimation(anim, forceRestart: true, resolveInterrupts: false);
  }

  public void PauseAnimation() {
    isPlaying = false;
    CancelAllTweens();
  }

  public void ResumeAnimation() {
    if (!string.IsNullOrEmpty(currentAnimation)) {
      isPlaying = true;
      SetBounces();
    }
  }

  public void StopAnimation(bool resetToDefault = false) {
    isPlaying = false;
    queuedAnimation = null;
    animationTimer = 0f;
    currentFrame = 0;
    CancelAllTweens();
    if (resetToDefault && !string.IsNullOrEmpty(defaultAnimation)) {
      PlayAnimation(defaultAnimation, true);
    }
  }

  public void TogglePause(string forcePause = null) {
    isPlaying = forcePause != null ? false : !isPlaying;
    foreach (var kvp in activeTweens) {
      foreach (int tweenId in kvp.Value) {
        if (isPlaying) {
          LeanTween.resume(tweenId);
        }
        else {
          LeanTween.pause(tweenId);
        }
      }
    }
  }

  public float GetAnimationDurationSeconds(string animationName) {
    if (animationData != null && animationData.TryGetValue(animationName, out var anim) && anim.duration > 0) {
      return anim.duration / 1000f;
    }
    return 0f;
  }

  public ComboManager GetComboManager() {
    return comboManager;
  }

  public void QueueFlip() {
    pendingFlip = true;
  }

  public void SetFacingDirection(float xDirection) {
    if (Mathf.Approximately(xDirection, 0f)) return;
    var faceRight = xDirection >= 0f;
    if (faceRight == isFacingRight) return;
    isFacingRight = faceRight;
    ApplyFlip();
  }

  public void Cleanup(bool resetLeanTweenManager) {
    CancelAllTweens();
    if (resetLeanTweenManager && !hasResetLeanTween) {
      LeanTween.reset();
      hasResetLeanTween = true;
    }
  }

  private void AdvanceAnimation(float deltaTime) {
    if (!isPlaying || string.IsNullOrEmpty(currentAnimation) || animationData == null) return;
    if (!animationData.TryGetValue(currentAnimation, out var anim)) return;

    float slowFactor = SlowDown ? 20f : 1f;
    animationTimer += (deltaTime * 1000f) / slowFactor;
    float normalTime = animationTimer / Mathf.Max(1f, anim.duration);

    if (!pingPong) {
      int frameOffset = Mathf.FloorToInt((anim.end - anim.start) * normalTime);
      currentFrame = anim.start + frameOffset;
      if (currentFrame >= anim.end) {
        if (!string.IsNullOrEmpty(queuedAnimation)) {
          var next = queuedAnimation;
          queuedAnimation = null;
          currentAnimation = null;
          PlayAnimation(next);
          return;
        }
        if (anim.loop || ForceLoop) {
          currentFrame = anim.start;
          pingPong = false;
          animationTimer = 0f;
          SetBounces();
        }
        else {
          currentFrame = anim.end;
          isPlaying = false;
          if (anim.pingPong) {
            animationTimer = 0f;
            isPlaying = true;
            pingPong = true;
          }
        }
      }
    }
    else {
      int frameOffset = Mathf.FloorToInt((anim.end - anim.start) * normalTime);
      currentFrame = anim.end - frameOffset;
      if (currentFrame <= anim.start) {
        isPlaying = true;
        currentFrame = anim.start - 1;
        pingPong = false;
        animationTimer = 0f;
        SetBounces();
      }
    }

    if (pendingFlip) {
      pendingFlip = false;
      isFacingRight = !isFacingRight;
      ApplyFlip();
    }

    UpdateSprites(currentFrame);
  }

  private bool TryResolveInterrupt(string requestedAnimation, out string resolvedAnimation, out string queued) {
    resolvedAnimation = requestedAnimation;
    queued = null;
    if (string.IsNullOrEmpty(currentAnimation)) return true;
    if (interruptData != null && interruptData.TryGetValue(currentAnimation, out var nextMap)) {
      if (!nextMap.TryGetValue(requestedAnimation, out resolvedAnimation)) {
        return false;
      }
      if (resolvedAnimation != requestedAnimation) {
        queued = requestedAnimation;
      }
    }
    return true;
  }

  private void SetAnimationCategory(string category) {
    foreach (var target in spriteTargets) {
      if (target == null) continue;
      target.SetAnimation(category);
    }
  }

  private void UpdateSprites(int frame) {
    foreach (var target in spriteTargets) {
      if (target == null) continue;
      target.UpdateSpriteAndNormal(frame);
    }
  }

  private void SetBounces() {
    CancelAllTweens();
    if (bounceData == null || bounceData.Count == 0 || bounceObjects.Length == 0 || string.IsNullOrEmpty(currentAnimation)) {
      SetHBoxes();
      return;
    }
    foreach (KeyValuePair<string, Dictionary<string, List<BounceFrame>>> partPair in bounceData) {
      string partKey = partPair.Key;
      var animationDict = partPair.Value;
      if (!animationDict.ContainsKey(currentAnimation)) continue;
      var frameSequence = animationDict[currentAnimation];
      foreach (GameObject bounceParent in bounceObjects) {
        if (!isPlaying) break;
        if (bounceParent == null) continue;
        if (bounceParent.name.Equals(partKey)) {
          LeanTween.cancel(bounceParent);
          StartBounceSequence(bounceParent, frameSequence, 0);
          break;
        }
      }
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

    var moveDescr = LeanTween.moveLocal(bounceParent, targetPos, duration).setEase(LeanTweenType.linear);
    AddTweenId(bounceParent, moveDescr.id);
    moveDescr.setOnComplete(() => RemoveTweenId(bounceParent, moveDescr.id));

    var scaleDescr = LeanTween.scaleX(bounceParent, frame.offset, duration).setEase(LeanTweenType.linear);
    AddTweenId(bounceParent, scaleDescr.id);
    scaleDescr.setOnComplete(() => RemoveTweenId(bounceParent, scaleDescr.id));

    LTDescr delayDescr = null;
    delayDescr = LeanTween.delayedCall(bounceParent, duration, () => {
      RemoveTweenId(bounceParent, delayDescr.id);
      StartBounceSequence(bounceParent, sequence, index + 1);
    });
    AddTweenId(bounceParent, delayDescr.id);
  }

  private void SetHBoxes() {
    if (hBoxData == null || hBoxObjects.Length == 0 || string.IsNullOrEmpty(currentAnimation)) return;
    foreach (var kvp in hBoxData) {
      string partKey = kvp.Key;
      var animDict = kvp.Value;
      if (!animDict.ContainsKey(currentAnimation)) continue;
      var hboxList = animDict[currentAnimation];
      foreach (GameObject go in hBoxObjects) {
        if (go == null || !go.name.Equals(partKey)) continue;
        var poly = go.GetComponent<PolygonCollider2D>();
        if (poly == null) continue;
        LeanTween.cancel(go);
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
    for (int i = 0; i < len; i++) {
      s[i] = (startLen > 0) ? startPoints[i % startLen] : endPoints[i % endLen];
      e[i] = endPoints[i % endLen];
    }
    float duration = (targetPath.d > 0 ? targetPath.d : 0.2f) * fSlowDown;

    var descr = LeanTween.value(go, 0f, 1f, duration).setEase(LeanTweenType.linear);
    AddTweenId(go, descr.id);
    descr.setOnUpdate((float v) => {
      Vector2[] lerped = new Vector2[len];
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
      }
    }
    activeTweens.Clear();
  }

  private void ClearTweensFor(GameObject go) {
    if (go == null) return;
    LeanTween.cancel(go);
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
    if (activeTweens.ContainsKey(go)) {
      activeTweens[go].Remove(tweenId);
    }
  }

  private void CacheSpriteTargets() {
    spriteTargets.Clear();
    if (spriteObjects != null && spriteObjects.Length > 0) {
      foreach (var go in spriteObjects) {
        if (go == null) continue;
        var sprite = go.GetComponent<SpriteWithNormals>();
        if (sprite != null) spriteTargets.Add(sprite);
      }
    }
    if (spriteTargets.Count == 0 && rootTransform != null) {
      foreach (var sprite in rootTransform.GetComponentsInChildren<SpriteWithNormals>()) {
        if (sprite != null) spriteTargets.Add(sprite);
      }
    }
  }

  private void ApplyFlip() {
    if (rootTransform == null) return;
    var scale = baseScale;
    scale.x = Mathf.Abs(scale.x) * (isFacingRight ? 1f : -1f);
    rootTransform.localScale = scale;
    SetBounces();
  }
}

/// <summary>
/// Simple MonoBehaviour helper to drive an AnimationController instance via inspector buttons.
/// </summary>
public class AnimationDebugger : MonoBehaviour {
  [Button(nameof(_TogglePause), label = "un/pause", size = Size.small)] public bool slowDown;
  [Button(nameof(ForceAnimation), label = "Play", size = Size.small)] public bool forceLoop;

  [Tooltip("Optional gear controller to drive.")]
  [SerializeField] private GearController gearController;
  [Tooltip("Optional enemy controller to drive.")]
  [SerializeField] private EnemyController enemyController;
  [Tooltip("If set, forces this animation when pressing Play; otherwise replays current/default.")]
  [SerializeField] private string animationName;

  private AnimationController TargetController {
    get {
      if (gearController != null) return gearController.Controller;
      if (enemyController != null) return enemyController.Controller;
      return null;
    }
  }

  void Reset() {
    if (gearController == null) gearController = GetComponent<GearController>();
    if (enemyController == null) enemyController = GetComponent<EnemyController>();
  }

  public void _TogglePause() {
    TargetController?.TogglePause();
  }

  public void ForceAnimation() {
    if (TargetController == null) Reset();
    if (!string.IsNullOrEmpty(animationName)) TargetController.ForceAnimation(animationName);
    else TargetController.ForceAnimation();
  }
}
