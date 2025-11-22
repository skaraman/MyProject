using System.Collections.Generic;
using UnityEngine;
using CustomInspector;

[RequireComponent(typeof(EnemyInfo))]
public class EnemyController : MonoBehaviour {
  [Button(nameof(_TogglePause), label = "un/pause", size = Size.small)] public bool slowDown;
  [Button(nameof(ForceAnimation), label = "Play", size = Size.small)] public bool forceLoop;

  [Header("Sprite Parts")]
  [Tooltip("Parts that carry SpriteWithNormals components. If left empty, children are auto-discovered.")]
  public GameObject[] spriteObjects;

  [Header("Hit Boxes")]
  [Tooltip("PolygonCollider2D objects that should be animated per frame.")]
  public GameObject[] hBoxObjects;

  [Header("Animation Data")]
  public string enemyType = "Imp";
  public string defaultAnimation = "Idle";
  [Tooltip("Animation to fall back to when a non-looping clip finishes.")]
  public string fallbackAnimation = "Idle";
  public bool playOnStart = true;

  private Dictionary<string, AnimData> animationData;
  private Dictionary<string, Dictionary<string, string>> interruptData;
  private Dictionary<string, Dictionary<string, List<HBox>>> hBoxData;
  private readonly List<SpriteWithNormals> spriteTargets = new();
  private readonly Dictionary<GameObject, List<int>> hBoxTweens = new();
  private readonly Dictionary<GameObject, Coroutine> hBoxCoroutines = new();

  public string CurrentAnimation => currentAnimation;

  private string currentAnimation;
  private string queuedAnimation;
  private int currentFrame;
  private float animationTimer;
  private bool isPlaying;
  private bool pingPong;
  private bool isFacingRight = true;
  private Vector3 originalScale;

  void Awake() {
    originalScale = transform.localScale;
    CacheSpriteTargets();
    LoadAnimationData();
  }

  void Start() {
    if (playOnStart && !string.IsNullOrEmpty(defaultAnimation)) {
      PlayAnimation(defaultAnimation, true);
    }
  }

  void Update() {
    AdvanceAnimation();
  }

  void OnDisable() {
    CancelAllHBoxTweens();
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
    if (spriteTargets.Count == 0) {
      spriteTargets.AddRange(GetComponentsInChildren<SpriteWithNormals>());
    }
  }

  private void LoadAnimationData() {
    animationData = Animations.Enemies.ContainsKey(enemyType) ? Animations.Enemies[enemyType] : null;
    if (animationData == null) {
      Debug.LogWarning($"[EnemyController] No animation data found for enemy type '{enemyType}'.");
    }
    interruptData = Interrupts.Enemies.ContainsKey(enemyType) ? Interrupts.Enemies[enemyType] : new Dictionary<string, Dictionary<string, string>>();
    hBoxData = HBoxes.Enemies.ContainsKey(enemyType) ? HBoxes.Enemies[enemyType] : null;
  }

  public bool PlayAnimation(string animationName, bool forceRestart = false) {
    if (animationData == null || !animationData.ContainsKey(animationName)) {
      Debug.LogWarning($"[EnemyController] Animation '{animationName}' missing for '{enemyType}'.");
      return false;
    }
    if (!forceRestart && isPlaying && animationName == currentAnimation) return true;

    if (!TryResolveInterrupt(animationName, out var resolvedAnimation, out var queued)) {
      return false;
    }

    currentAnimation = resolvedAnimation;
    queuedAnimation = queued;
    animationTimer = 0f;
    pingPong = false;
    isPlaying = true;
    currentFrame = animationData[currentAnimation].start - 1;

    var category = animationData[currentAnimation].To ? "To" : currentAnimation;
    SetAnimationCategory(category);
    UpdateSprites(currentFrame);
    SetHBoxes();
    return true;
  }

  public void PauseAnimation() {
    isPlaying = false;
    CancelAllHBoxTweens();
  }

  public void ResumeAnimation() {
    if (!string.IsNullOrEmpty(currentAnimation)) {
      isPlaying = true;
      SetHBoxes();
    }
  }

  public void StopAnimation(bool resetToDefault = false) {
    isPlaying = false;
    queuedAnimation = null;
    animationTimer = 0f;
    currentFrame = 0;
    CancelAllHBoxTweens();
    if (resetToDefault && !string.IsNullOrEmpty(defaultAnimation)) {
      PlayAnimation(defaultAnimation, true);
    }
  }

  public void _TogglePause() {
    TogglePause();
  }

  public void TogglePause(string forcePause = null) {
    isPlaying = forcePause != null ? false : !isPlaying;
    foreach (var kvp in hBoxTweens) {
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

  public void ForceAnimation() {
    if (string.IsNullOrEmpty(currentAnimation)) return;
    string anim = currentAnimation;
    currentAnimation = null;
    queuedAnimation = null;
    PlayAnimation(anim, true);
  }

  public float GetAnimationDurationSeconds(string animationName) {
    if (animationData != null && animationData.TryGetValue(animationName, out var anim) && anim.duration > 0) {
      return anim.duration / 1000f;
    }
    return 0f;
  }

  public void FaceDirection(float xDirection) {
    if (Mathf.Approximately(xDirection, 0f)) return;
    var faceRight = xDirection >= 0f;
    if (faceRight == isFacingRight) return;
    isFacingRight = faceRight;
    var scale = originalScale;
    scale.x = Mathf.Abs(scale.x) * (isFacingRight ? 1f : -1f);
    transform.localScale = scale;
  }

  private void AdvanceAnimation() {
    if (!isPlaying || string.IsNullOrEmpty(currentAnimation) || animationData == null) return;
    if (!animationData.TryGetValue(currentAnimation, out var anim)) return;

    float slowFactor = slowDown ? 10f : 1f;
    animationTimer += (Time.deltaTime * 1000f) / slowFactor;
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
        if (anim.loop || forceLoop) {
          currentFrame = anim.start;
          pingPong = false;
          animationTimer = 0f;
          SetHBoxes();
        }
        else {
          currentFrame = anim.end;
          isPlaying = false;
          if (anim.pingPong) {
            animationTimer = 0f;
            isPlaying = true;
            pingPong = true;
          }
          else if (!string.IsNullOrEmpty(fallbackAnimation) && fallbackAnimation != currentAnimation) {
            PlayAnimation(fallbackAnimation, true);
            return;
          }
        }
      }
    }
    else {
      int frameOffset = Mathf.FloorToInt((anim.end - anim.start) * normalTime);
      currentFrame = anim.end - frameOffset;
      if (currentFrame <= anim.start) {
        currentFrame = anim.start;
        pingPong = false;
        animationTimer = 0f;
        SetHBoxes();
      }
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

  private void SetHBoxes() {
    CancelAllHBoxTweens();
    if (hBoxData == null || hBoxObjects == null || hBoxObjects.Length == 0 || string.IsNullOrEmpty(currentAnimation)) return;
    foreach (var kvp in hBoxData) {
      string partKey = kvp.Key;
      var animDict = kvp.Value;
      if (!animDict.TryGetValue(currentAnimation, out var hboxList) || hboxList == null) continue;
      foreach (GameObject go in hBoxObjects) {
        if (go == null || !go.name.Equals(partKey)) continue;
        var poly = go.GetComponent<PolygonCollider2D>();
        if (poly == null) continue;
        LeanTween.cancel(go);
        if (hBoxCoroutines.ContainsKey(go) && hBoxCoroutines[go] != null) {
          StopCoroutine(hBoxCoroutines[go]);
        }
        var coro = StartCoroutine(AnimateHBox(go, poly, hboxList));
        hBoxCoroutines[go] = coro;
      }
    }
  }

  private System.Collections.IEnumerator AnimateHBox(GameObject go, PolygonCollider2D collider, List<HBox> sequence) {
    if (!hBoxTweens.ContainsKey(go)) {
      hBoxTweens[go] = new List<int>();
    }
    float slowFactor = slowDown ? 10f : 1f;
    foreach (var targetPath in sequence) {
      if (!isPlaying) break;
      if (collider.pathCount == 0) collider.pathCount = 1;
      Vector2[] startPoints = collider.GetPath(0);
      Vector2[] endPoints = targetPath.points.ToArray();
      if (endPoints.Length == 0) continue;
      int startLen = startPoints?.Length ?? 0;
      int endLen = endPoints.Length;
      int len = Mathf.Max(1, Mathf.Max(startLen, endLen));
      Vector2[] s = new Vector2[len];
      Vector2[] e = new Vector2[len];
      for (int i = 0; i < len; i++) {
        s[i] = (startLen > 0) ? startPoints[i % startLen] : endPoints[i % endLen];
        e[i] = endPoints[i % endLen];
      }
      float duration = (targetPath.d > 0 ? targetPath.d : 0.2f) * slowFactor;

      var descr = LeanTween.value(go, 0f, 1f, duration).setEase(LeanTweenType.linear);
      int tweenId = descr.id;
      hBoxTweens[go].Add(tweenId);
      descr.setOnUpdate((float v) => {
        Vector2[] lerped = new Vector2[len];
        for (int i = 0; i < len; i++) {
          lerped[i] = Vector2.Lerp(s[i], e[i], v);
        }
        collider.SetPath(0, lerped);
      });
      descr.setOnComplete(() => {
        collider.SetPath(0, e);
        if (hBoxTweens.ContainsKey(go)) hBoxTweens[go].Remove(tweenId);
      });

      yield return new WaitForSeconds(duration);
      if (!isPlaying) break;
    }
    if (hBoxTweens.ContainsKey(go)) {
      hBoxTweens[go].Clear();
    }
    if (hBoxCoroutines.ContainsKey(go)) hBoxCoroutines.Remove(go);
    if (slowDown) {
      TogglePause("true");
    }
  }

  private void CancelAllHBoxTweens() {
    foreach (var kvp in hBoxTweens) {
      var go = kvp.Key;
      if (go != null) {
        LeanTween.cancel(go);
      }
      if (hBoxCoroutines.TryGetValue(go, out var coro) && coro != null) {
        StopCoroutine(coro);
      }
    }
    hBoxTweens.Clear();
    hBoxCoroutines.Clear();
  }
}
