using System;
using System.Collections.Generic;
using System.Linq;
using CustomInspector;
using UnityEngine;

/// <summary>
/// Generic animation driver (non-MonoBehaviour). Host behaviours must call Tick/Cleanup and wire data/targets.
/// </summary>
public class AnimationController {
  const int MaxTargetsForGateReadinessChecks = 96;
  const int MinAppearancePinAddressBudget = 32;

  public string defaultAnimation = "Idle";
  public bool playOnStart;
  public bool SlowDown { get; set; }
  public bool ForceLoop { get; set; }

  public string CurrentAnimation => currentAnimation;
  public bool IsPlaying => isPlaying;
  public bool IsFacingRight => isFacingRight;
  public Action<string> OnEffectTriggered;
  public Action<string> OnProjectileTriggered;

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
  private readonly List<SpriteWithNormals> criticalSpriteTargets = new();
  private readonly Dictionary<SpriteWithNormals, SpriteRenderer> spriteTargetRenderers = new();
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
  private int lastFrame = int.MinValue;
  private bool effectTriggered;
  private bool projectileTriggered;
  private bool hadEnabledSpriteTargetsLastTick;

  private bool hasPendingAnimationSwitch;
  private string pendingAnimation;
  private string pendingQueuedAnimation;
  private string pendingCategory;
  private int pendingStartFrame;
  private int pendingReadyEndFrame;
  private float pendingSwitchStartTime;
  private float pendingSwitchDeadline;
  private string appearanceOwnerId;
  private TextureResidencyCache.PinClass appearancePinClass = TextureResidencyCache.PinClass.Enemy;
  private int appearancePinRefreshOffset;
  private readonly List<string> appearancePinAddressBuffer = new(512);
  private readonly HashSet<string> appearancePinAddressSet = new(StringComparer.OrdinalIgnoreCase);
  private readonly HashSet<string> predictedAnimations = new(StringComparer.Ordinal);
  private bool pinSnapshotStreamingEnabled;
  private int pinSnapshotWindowFrames = int.MinValue;
  private int pinSnapshotPredictedAnimations = int.MinValue;
  private string pinSnapshotCurrentAnimation;
  private int pinSnapshotCurrentFrameBucket = int.MinValue;
  private bool pinSnapshotHasPendingSwitch;
  private string pinSnapshotPendingAnimation;
  private int pinSnapshotPendingStartFrame = int.MinValue;
  private int pinSnapshotPendingReadyEndFrame = int.MinValue;
  private string pinSnapshotQueuedAnimation;

  static bool runtimeSettingsLoaded;
  static int runtimeWarmupFrames = 24;
  static int runtimeSwitchGateMs = 120;
  const int MaxSwitchReadinessWindowFrames = 12;

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
    bool autoPlay,
    string appearanceOwnerId = null,
    TextureResidencyCache.PinClass appearancePinClass = TextureResidencyCache.PinClass.Enemy
  ) {
    rootTransform = root;
    baseScale = root != null ? root.localScale : Vector3.one;
    SetSpriteObjects(sprites);
    SetBounceObjects(bounces);
    SetHBoxObjects(hBoxes);
    ConfigureData(animations, interrupts, bouncesData, hBoxesData);
    defaultAnimation = defaultAnim;
    playOnStart = autoPlay;
    this.appearanceOwnerId = string.IsNullOrWhiteSpace(appearanceOwnerId) ? "" : appearanceOwnerId.Trim();
    this.appearancePinClass = appearancePinClass;
    appearancePinRefreshOffset = root != null ? Mathf.Abs(root.GetInstanceID()) : 0;
    appearancePinAddressBuffer.Clear();
    appearancePinAddressSet.Clear();
    predictedAnimations.Clear();
    InvalidateAppearancePinSnapshot();
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
    InvalidateAppearancePinSnapshot();
  }

  public void SetSpriteObjects(IEnumerable<GameObject> targets) {
    spriteObjects = targets != null ? targets.ToArray() : Array.Empty<GameObject>();
    CacheSpriteTargets();
    InvalidateAppearancePinSnapshot();
  }

  public void SetBounceObjects(IEnumerable<GameObject> targets) {
    bounceObjects = targets != null ? targets.ToArray() : Array.Empty<GameObject>();
  }

  public void SetHBoxObjects(IEnumerable<GameObject> targets) {
    hBoxObjects = targets != null ? targets.ToArray() : Array.Empty<GameObject>();
  }

  public void Tick(float deltaTime) {
    TextureResidencyCache.Pump();
    var hasEnabledSpriteTargets = HasEnabledSpriteTargets();
    if (!hasEnabledSpriteTargets) {
      if (hadEnabledSpriteTargetsLastTick) {
        ReleaseAppearancePins();
      }
      hadEnabledSpriteTargetsLastTick = false;
    }
    else {
      hadEnabledSpriteTargetsLastTick = true;
      RefreshAppearancePins();
    }
    AdvanceAnimation(deltaTime);
  }

  public bool PlayAnimation(string animationName, bool forceRestart = false, bool resolveInterrupts = true) {
    if (!TryGetAnimationKey(animationName, out var requestedAnimation)) {
      Debug.LogWarning($"[AnimationController] Animation '{animationName}' missing.");
      return false;
    }

    string resolvedAnimation = requestedAnimation;
    string queued = null;
    if (resolveInterrupts && !forceRestart) {
      if (!TryResolveInterrupt(requestedAnimation, out resolvedAnimation, out queued)) {
        return false;
      }
    }

    if (!TryGetAnimationKey(resolvedAnimation, out resolvedAnimation)) {
      resolvedAnimation = requestedAnimation;
    }

    if (string.IsNullOrWhiteSpace(resolvedAnimation) || !animationData.ContainsKey(resolvedAnimation)) {
      Debug.LogWarning($"[AnimationController] Animation '{resolvedAnimation}' missing. '{animationName}'");
      return false;
    }

    if (!forceRestart &&
        isPlaying &&
        string.Equals(resolvedAnimation, currentAnimation, StringComparison.Ordinal) &&
        !hasPendingAnimationSwitch) {
      return true;
    }

    if (!forceRestart &&
        hasPendingAnimationSwitch &&
        string.Equals(resolvedAnimation, pendingAnimation, StringComparison.Ordinal)) {
      if (!string.IsNullOrWhiteSpace(queued)) {
        pendingQueuedAnimation = queued;
      }
      return true;
    }

    EnsureRuntimeSwitchSettings();
    var anim = animationData[resolvedAnimation];
    var category = ResolveAnimationCategory(resolvedAnimation, anim);
    var enabledTargetCount = CountEnabledSpriteTargets();

    var canGate = Application.isPlaying &&
                  !forceRestart &&
                  !string.IsNullOrEmpty(currentAnimation) &&
                  !string.Equals(currentAnimation, resolvedAnimation, StringComparison.Ordinal) &&
                  enabledTargetCount > 0 &&
                  enabledTargetCount <= MaxTargetsForGateReadinessChecks;

    if (canGate) {
      PrimeTargetsForAnimation(category, anim.start, anim.end);
      var gateMs = Math.Max(runtimeSwitchGateMs, 0);
      var readinessEndFrame = CalculateSwitchReadinessEndFrame(anim.start, anim.end);
      var ready = AreTargetsReadyForWindow(category, anim.start, readinessEndFrame);
      if (!ready && gateMs > 0) {
        BeginPendingAnimationSwitch(resolvedAnimation, queued, category, anim.start, readinessEndFrame, gateMs);
        return true;
      }
      SpriteStreamingDiagnostics.RecordAnimationSwitchWait(0f, false);
    }
    else {
      ClearPendingAnimationSwitch();
    }

    CommitAnimationSwitch(resolvedAnimation, queued, category);
    return true;
  }

  public void ForceAnimation(string animationName = null) {
    string anim = animationName ?? (!string.IsNullOrEmpty(CurrentAnimation) ? CurrentAnimation : defaultAnimation);
    if (string.IsNullOrEmpty(anim)) return;
    PlayAnimation(anim, forceRestart: true, resolveInterrupts: false);
  }

  public void ReleaseAppearancePins() {
    if (string.IsNullOrWhiteSpace(appearanceOwnerId)) return;
    TextureResidencyCache.ReleaseOwnerPins(appearanceOwnerId);
    appearancePinAddressBuffer.Clear();
    appearancePinAddressSet.Clear();
    InvalidateAppearancePinSnapshot();
  }

  public void PauseAnimation() {
    isPlaying = false;
    ClearPendingAnimationSwitch();
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
    ClearPendingAnimationSwitch();
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
    ReleaseAppearancePins();
    ClearPendingAnimationSwitch();
    CancelAllTweens();
    if (resetLeanTweenManager && !hasResetLeanTween) {
      LeanTween.reset();
      hasResetLeanTween = true;
    }
  }

  private void AdvanceAnimation(float deltaTime) {
    TryFinalizePendingAnimationSwitch();
    if (!isPlaying || string.IsNullOrEmpty(currentAnimation) || animationData == null) return;
    if (!animationData.TryGetValue(currentAnimation, out var anim)) return;

    float slowFactor = SlowDown ? 20f : 1f;
    animationTimer += (deltaTime * 1000f) / slowFactor;
    float normalTime = animationTimer / Mathf.Max(1f, anim.duration);
    bool cycleReset = false;

    if (!pingPong) {
      int frameOffset = Mathf.FloorToInt((anim.end - anim.start) * normalTime);
      currentFrame = anim.start + frameOffset;
      if (currentFrame >= anim.end) {
        if (!string.IsNullOrEmpty(queuedAnimation)) {
          var next = queuedAnimation;
          queuedAnimation = null;
          currentAnimation = null;
          if (!PlayAnimation(next, resolveInterrupts: false)) {
            if (!string.IsNullOrWhiteSpace(defaultAnimation) && animationData.ContainsKey(defaultAnimation)) {
              PlayAnimation(defaultAnimation, forceRestart: true, resolveInterrupts: false);
            }
            else {
              isPlaying = false;
            }
          }
          return;
        }
        if (anim.loop || ForceLoop) {
          currentFrame = anim.start;
          pingPong = false;
          animationTimer = 0f;
          SetBounces();
          cycleReset = true;
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
        cycleReset = true;
      }
    }

    if (pendingFlip) {
      pendingFlip = false;
      isFacingRight = !isFacingRight;
      ApplyFlip();
    }

    UpdateSprites(currentFrame);
    if (cycleReset) {
      ResetAnimationEvents(anim);
    }
    TryTriggerFrameEvents(anim, lastFrame, currentFrame);
    lastFrame = currentFrame;
  }

  private bool TryResolveInterrupt(string requestedAnimation, out string resolvedAnimation, out string queued) {
    resolvedAnimation = requestedAnimation;
    queued = null;
    if (string.IsNullOrEmpty(currentAnimation)) return true;
    if (interruptData != null && interruptData.TryGetValue(currentAnimation, out var nextMap)) {
      if (!nextMap.TryGetValue(requestedAnimation, out var mappedAnimation)) return true;
      if (!TryGetAnimationKey(mappedAnimation, out resolvedAnimation)) {
        resolvedAnimation = requestedAnimation;
        return true;
      }
      if (!string.Equals(resolvedAnimation, requestedAnimation, StringComparison.Ordinal)) {
        queued = requestedAnimation;
      }
    }
    return true;
  }

  bool TryGetAnimationKey(string candidate, out string resolved) {
    resolved = "";
    if (animationData == null || animationData.Count == 0) return false;

    var normalized = NormalizeAnimationName(candidate);
    if (string.IsNullOrWhiteSpace(normalized)) return false;

    if (animationData.ContainsKey(normalized)) {
      resolved = normalized;
      return true;
    }

    foreach (var key in animationData.Keys) {
      if (!string.Equals(key, normalized, StringComparison.OrdinalIgnoreCase)) continue;
      resolved = key;
      return true;
    }

    return false;
  }

  static string NormalizeAnimationName(string value) {
    if (string.IsNullOrWhiteSpace(value)) return "";
    return value
      .Replace("\u200B", "")
      .Replace("\uFEFF", "")
      .Trim();
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetRuntimeSwitchSettingsCache() {
    runtimeSettingsLoaded = false;
    runtimeWarmupFrames = 24;
    runtimeSwitchGateMs = 120;
  }

  static void EnsureRuntimeSwitchSettings() {
    if (runtimeSettingsLoaded) return;
    runtimeSettingsLoaded = true;
    runtimeWarmupFrames = SpriteStreamingRuntimeSettings.AnimationWarmupFrames;
    runtimeSwitchGateMs = SpriteStreamingRuntimeSettings.AnimationSwitchGateMs;
  }

  void BeginPendingAnimationSwitch(
    string nextAnimation,
    string nextQueuedAnimation,
    string nextCategory,
    int startFrame,
    int readyEndFrame,
    int gateMs
  ) {
    if (hasPendingAnimationSwitch &&
        string.Equals(pendingAnimation, nextAnimation, StringComparison.Ordinal) &&
        string.Equals(pendingCategory, nextCategory, StringComparison.Ordinal) &&
        pendingStartFrame == startFrame &&
        pendingReadyEndFrame == readyEndFrame) {
      if (!string.IsNullOrWhiteSpace(nextQueuedAnimation)) {
        pendingQueuedAnimation = nextQueuedAnimation;
      }
      return;
    }

    hasPendingAnimationSwitch = true;
    pendingAnimation = nextAnimation ?? "";
    pendingQueuedAnimation = nextQueuedAnimation ?? "";
    pendingCategory = nextCategory ?? "";
    pendingStartFrame = startFrame;
    pendingReadyEndFrame = Math.Max(readyEndFrame, startFrame);
    pendingSwitchStartTime = Time.realtimeSinceStartup;
    pendingSwitchDeadline = pendingSwitchStartTime + Math.Max(gateMs, 0) / 1000f;
  }

  void TryFinalizePendingAnimationSwitch() {
    if (!hasPendingAnimationSwitch) return;
    if (animationData == null || string.IsNullOrWhiteSpace(pendingAnimation) || !animationData.ContainsKey(pendingAnimation)) {
      ClearPendingAnimationSwitch();
      return;
    }

    var now = Time.realtimeSinceStartup;
    PrimeTargetsForAnimation(pendingCategory, pendingStartFrame, pendingReadyEndFrame);
    var isReady = AreTargetsReadyForWindow(pendingCategory, pendingStartFrame, pendingReadyEndFrame);
    if (!isReady && now < pendingSwitchDeadline && isPlaying) return;

    var waitMs = Mathf.Max((now - pendingSwitchStartTime) * 1000f, 0f);
    SpriteStreamingDiagnostics.RecordAnimationSwitchWait(waitMs, waitMs > 0.1f);
    CommitAnimationSwitch(pendingAnimation, pendingQueuedAnimation, pendingCategory);
    ClearPendingAnimationSwitch();
  }

  void PrimeTargetsForAnimation(string targetCategory, int startFrame, int endFrame) {
    var warmupFrames = Math.Max(runtimeWarmupFrames, 1);
    var clampedStart = Math.Max(startFrame, 1);
    var clampedEnd = Math.Max(endFrame, clampedStart);
    var targetEnd = Math.Min(clampedEnd, clampedStart + warmupFrames - 1);

    foreach (var target in spriteTargets) {
      if (!IsSpriteTargetEnabled(target)) continue;
      target.PrimeAnimationWindow(targetCategory, clampedStart, targetEnd, 0);
    }
  }

  public void PrimeAllAnimationStarts(int framesPerAnimation = 1, int maxAnimations = 0) {
    if (!Application.isPlaying) return;
    if (animationData == null || animationData.Count == 0 || spriteTargets.Count == 0) return;

    var warmFrames = Math.Max(framesPerAnimation, 1);
    var primed = 0;
    foreach (var pair in animationData) {
      var animationName = pair.Key;
      var anim = pair.Value;
      if (anim == null || string.IsNullOrWhiteSpace(animationName)) continue;

      var categoryName = ResolveAnimationCategory(animationName, anim);
      var startFrame = Math.Max(anim.start, 1);
      var endFrame = Math.Max(startFrame, startFrame + warmFrames - 1);
      PrimeTargetsForAnimation(categoryName, startFrame, endFrame);

      primed++;
      if (maxAnimations > 0 && primed >= maxAnimations) break;
    }
  }

  bool AreTargetsReadyForFrame(string targetCategory, int frame) {
    return AreTargetsReadyForWindow(targetCategory, frame, frame);
  }

  bool AreTargetsReadyForWindow(string targetCategory, int startFrame, int endFrame) {
    var minFrame = Math.Max(startFrame, 1);
    var maxFrame = Math.Max(endFrame, minFrame);
    var hasCriticalTargets = false;
    for (var i = 0; i < criticalSpriteTargets.Count; i++) {
      var target = criticalSpriteTargets[i];
      if (!IsSpriteTargetEnabled(target)) continue;
      hasCriticalTargets = true;
      for (var frame = minFrame; frame <= maxFrame; frame++) {
        if (!target.IsFrameReady(frame, targetCategory)) return false;
      }
    }

    if (hasCriticalTargets) return true;

    foreach (var target in spriteTargets) {
      if (!IsSpriteTargetEnabled(target)) continue;
      for (var frame = minFrame; frame <= maxFrame; frame++) {
        if (!target.IsFrameReady(frame, targetCategory)) return false;
      }
    }
    return true;
  }

  static int CalculateSwitchReadinessEndFrame(int startFrame, int endFrame) {
    var clampedStart = Math.Max(startFrame, 1);
    var clampedClipEnd = Math.Max(endFrame, clampedStart);
    var readinessFrames = Math.Max(1, Math.Min(runtimeWarmupFrames, MaxSwitchReadinessWindowFrames));
    return Math.Min(clampedClipEnd, clampedStart + readinessFrames - 1);
  }

  void CommitAnimationSwitch(string nextAnimation, string nextQueuedAnimation, string targetCategory) {
    currentAnimation = nextAnimation;
    queuedAnimation = string.IsNullOrWhiteSpace(nextQueuedAnimation) ? null : nextQueuedAnimation;

    animationTimer = 0f;
    pingPong = false;
    isPlaying = true;
    var anim = animationData[currentAnimation];
    currentFrame = anim.start;

    SetAnimationCategory(targetCategory);
    UpdateSprites(currentFrame);
    SetBounces();
    ResetAnimationEvents(anim);
    TryTriggerFrameEvents(anim, lastFrame, currentFrame);
    lastFrame = currentFrame;
  }

  static string ResolveAnimationCategory(string animationName, AnimData anim) {
    return anim.To == 1 ? "To" : anim.To == 2 ? "To2" : animationName;
  }

  void ClearPendingAnimationSwitch() {
    hasPendingAnimationSwitch = false;
    pendingAnimation = null;
    pendingQueuedAnimation = null;
    pendingCategory = null;
    pendingStartFrame = 0;
    pendingReadyEndFrame = 0;
    pendingSwitchStartTime = 0f;
    pendingSwitchDeadline = 0f;
  }

  void RefreshAppearancePins() {
    if (!Application.isPlaying) return;
    if (string.IsNullOrWhiteSpace(appearanceOwnerId)) return;
    var pinWindowFrames = Math.Max(SpriteStreamingRuntimeSettings.PinWindowFrames, 1);
    var maxPredicted = Math.Max(SpriteStreamingRuntimeSettings.PinPredictedNextAnimations, 0);
    var pinRefreshBucketSize = Math.Max(SpriteStreamingRuntimeSettings.PinRefreshFrameBucketSize, 1);
    var maxPinAddresses = Math.Max(SpriteStreamingRuntimeSettings.MaxPinnedAddressesPerOwner, MinAppearancePinAddressBudget);
    var currentFrameBucket = Mathf.Max(Time.frameCount + appearancePinRefreshOffset, 0) / pinRefreshBucketSize;

    if (!SpriteStreamingRuntimeSettings.EnableAppearanceSetStreaming ||
        !SpriteStreamingRuntimeSettings.EnablePinnedHotset) {
      if (IsAppearancePinSnapshotCurrent(false, pinWindowFrames, maxPredicted, currentFrameBucket)) return;
      TextureResidencyCache.ReleaseOwnerPins(appearanceOwnerId);
      appearancePinAddressBuffer.Clear();
      appearancePinAddressSet.Clear();
      UpdateAppearancePinSnapshot(false, pinWindowFrames, maxPredicted, currentFrameBucket);
      return;
    }

    if (animationData == null || animationData.Count == 0 || spriteTargets.Count == 0) {
      if (IsAppearancePinSnapshotCurrent(true, pinWindowFrames, maxPredicted, currentFrameBucket)) return;
      TextureResidencyCache.ReleaseOwnerPins(appearanceOwnerId);
      appearancePinAddressBuffer.Clear();
      appearancePinAddressSet.Clear();
      UpdateAppearancePinSnapshot(true, pinWindowFrames, maxPredicted, currentFrameBucket);
      return;
    }

    if (IsAppearancePinSnapshotCurrent(true, pinWindowFrames, maxPredicted, currentFrameBucket)) return;

    appearancePinAddressBuffer.Clear();
    appearancePinAddressSet.Clear();

    if (!string.IsNullOrWhiteSpace(currentAnimation) && animationData.TryGetValue(currentAnimation, out var currentAnim)) {
      var currentCategory = ResolveAnimationCategory(currentAnimation, currentAnim);
      var currentStart = isPlaying ? Mathf.Clamp(currentFrame, currentAnim.start, currentAnim.end) : currentAnim.start;
      CollectWindowAddresses(currentCategory, currentStart, currentAnim.end, pinWindowFrames, maxPinAddresses);
      CollectPredictedInterruptWindows(currentAnimation, pinWindowFrames, maxPredicted, maxPinAddresses);
    }

    if (appearancePinAddressBuffer.Count < maxPinAddresses &&
        hasPendingAnimationSwitch &&
        !string.IsNullOrWhiteSpace(pendingAnimation) &&
        animationData.TryGetValue(pendingAnimation, out var pendingAnim)) {
      var pendingCategoryName = string.IsNullOrWhiteSpace(pendingCategory)
        ? ResolveAnimationCategory(pendingAnimation, pendingAnim)
        : pendingCategory;
      var pendingStart = Mathf.Clamp(Math.Max(pendingStartFrame, pendingAnim.start), pendingAnim.start, pendingAnim.end);
      CollectWindowAddresses(pendingCategoryName, pendingStart, pendingAnim.end, pinWindowFrames, maxPinAddresses);
    }

    if (appearancePinAddressBuffer.Count < maxPinAddresses &&
        !string.IsNullOrWhiteSpace(queuedAnimation) &&
        animationData.TryGetValue(queuedAnimation, out var queuedAnim)) {
      var queuedCategory = ResolveAnimationCategory(queuedAnimation, queuedAnim);
      CollectWindowAddresses(queuedCategory, queuedAnim.start, queuedAnim.end, pinWindowFrames, maxPinAddresses);
    }

    if (appearancePinAddressBuffer.Count <= 0) {
      TextureResidencyCache.ReleaseOwnerPins(appearanceOwnerId);
      UpdateAppearancePinSnapshot(true, pinWindowFrames, maxPredicted, currentFrameBucket);
      return;
    }

    TextureResidencyCache.UpdateOwnerPins(
      appearanceOwnerId,
      appearancePinClass,
      appearancePinAddressBuffer,
      TextureResidencyCache.LoadPriority.Warmup
    );
    UpdateAppearancePinSnapshot(true, pinWindowFrames, maxPredicted, currentFrameBucket);
  }

  void CollectWindowAddresses(string categoryName, int startFrame, int maxClipFrame, int pinWindowFrames, int maxPinAddresses) {
    if (appearancePinAddressBuffer.Count >= maxPinAddresses) return;
    var clampedStart = Math.Max(startFrame, 1);
    var clampedMax = Math.Max(maxClipFrame, clampedStart);
    var clampedEnd = Math.Min(clampedMax, clampedStart + pinWindowFrames - 1);

    // Frame-first + critical-first collection protects core body continuity (Skin*) during switch windows.
    for (var frame = clampedStart; frame <= clampedEnd; frame++) {
      CollectWindowAddressesForTargetSet(criticalSpriteTargets, categoryName, frame, maxPinAddresses);
      if (appearancePinAddressBuffer.Count >= maxPinAddresses) return;
      CollectWindowAddressesForTargetSet(spriteTargets, categoryName, frame, maxPinAddresses, skipCriticalTargets: true);
      if (appearancePinAddressBuffer.Count >= maxPinAddresses) return;
    }
  }

  void CollectWindowAddressesForTargetSet(
    List<SpriteWithNormals> targets,
    string categoryName,
    int frame,
    int maxPinAddresses,
    bool skipCriticalTargets = false
  ) {
    if (targets == null || targets.Count == 0) return;
    for (var i = 0; i < targets.Count; i++) {
      if (appearancePinAddressBuffer.Count >= maxPinAddresses) return;
      var target = targets[i];
      if (target == null) continue;
      if (skipCriticalTargets && IsCriticalSpriteTarget(target)) continue;
      if (!IsSpriteTargetEnabled(target)) continue;
      if (!target.TryGetFrameAddressPair(frame, out var pair, categoryName)) continue;

      AddAppearancePinAddress(pair.colorAddress, maxPinAddresses);
      AddAppearancePinAddress(pair.normalAddress, maxPinAddresses);
    }
  }

  void CollectPredictedInterruptWindows(string sourceAnimation, int pinWindowFrames, int maxPredicted, int maxPinAddresses) {
    if (maxPredicted <= 0) return;
    if (appearancePinAddressBuffer.Count >= maxPinAddresses) return;
    if (interruptData == null || !interruptData.TryGetValue(sourceAnimation, out var nextMap) || nextMap == null || nextMap.Count == 0) return;

    predictedAnimations.Clear();
    foreach (var pair in nextMap) {
      var predictedAnimationName = pair.Value;
      if (string.IsNullOrWhiteSpace(predictedAnimationName)) continue;
      if (!predictedAnimations.Add(predictedAnimationName)) continue;
      if (!animationData.TryGetValue(predictedAnimationName, out var predictedAnim)) continue;

      var predictedCategory = ResolveAnimationCategory(predictedAnimationName, predictedAnim);
      CollectWindowAddresses(predictedCategory, predictedAnim.start, predictedAnim.end, pinWindowFrames, maxPinAddresses);
      if (appearancePinAddressBuffer.Count >= maxPinAddresses) break;
      if (predictedAnimations.Count >= maxPredicted) break;
    }
  }

  void AddAppearancePinAddress(string address, int maxPinAddresses) {
    if (appearancePinAddressBuffer.Count >= maxPinAddresses) return;
    if (string.IsNullOrWhiteSpace(address)) return;
    var normalized = address;
    if (!appearancePinAddressSet.Add(normalized)) return;
    if (appearancePinAddressBuffer.Count >= maxPinAddresses) return;
    appearancePinAddressBuffer.Add(normalized);
  }

  bool IsAppearancePinSnapshotCurrent(bool streamingEnabled, int pinWindowFrames, int maxPredicted, int currentFrameBucket) {
    return pinSnapshotStreamingEnabled == streamingEnabled &&
           pinSnapshotWindowFrames == pinWindowFrames &&
           pinSnapshotPredictedAnimations == maxPredicted &&
           pinSnapshotCurrentFrameBucket == currentFrameBucket &&
           pinSnapshotHasPendingSwitch == hasPendingAnimationSwitch &&
           string.Equals(pinSnapshotCurrentAnimation, currentAnimation, StringComparison.Ordinal) &&
           string.Equals(pinSnapshotPendingAnimation, pendingAnimation, StringComparison.Ordinal) &&
           pinSnapshotPendingStartFrame == pendingStartFrame &&
           pinSnapshotPendingReadyEndFrame == pendingReadyEndFrame &&
           string.Equals(pinSnapshotQueuedAnimation, queuedAnimation, StringComparison.Ordinal);
  }

  void UpdateAppearancePinSnapshot(bool streamingEnabled, int pinWindowFrames, int maxPredicted, int currentFrameBucket) {
    pinSnapshotStreamingEnabled = streamingEnabled;
    pinSnapshotWindowFrames = pinWindowFrames;
    pinSnapshotPredictedAnimations = maxPredicted;
    pinSnapshotCurrentAnimation = currentAnimation;
    pinSnapshotCurrentFrameBucket = currentFrameBucket;
    pinSnapshotHasPendingSwitch = hasPendingAnimationSwitch;
    pinSnapshotPendingAnimation = pendingAnimation;
    pinSnapshotPendingStartFrame = pendingStartFrame;
    pinSnapshotPendingReadyEndFrame = pendingReadyEndFrame;
    pinSnapshotQueuedAnimation = queuedAnimation;
  }

  void InvalidateAppearancePinSnapshot() {
    pinSnapshotStreamingEnabled = false;
    pinSnapshotWindowFrames = int.MinValue;
    pinSnapshotPredictedAnimations = int.MinValue;
    pinSnapshotCurrentAnimation = null;
    pinSnapshotCurrentFrameBucket = int.MinValue;
    pinSnapshotHasPendingSwitch = false;
    pinSnapshotPendingAnimation = null;
    pinSnapshotPendingStartFrame = int.MinValue;
    pinSnapshotPendingReadyEndFrame = int.MinValue;
    pinSnapshotQueuedAnimation = null;
  }

  private void SetAnimationCategory(string category) {
    foreach (var target in spriteTargets) {
      if (target == null) continue;
      target.SetAnimation(category);
    }
  }

  private void ResetAnimationEvents(AnimData anim) {
    effectTriggered = false;
    projectileTriggered = false;
    lastFrame = anim != null ? anim.start - 1 : int.MinValue;
  }

  private void TryTriggerFrameEvents(AnimData anim, int previousFrame, int currentFrame) {
    if (anim == null) return;
    if (!effectTriggered && !string.IsNullOrEmpty(anim.effect) && anim.effectFrame > 0) {
      if (previousFrame < anim.effectFrame && currentFrame >= anim.effectFrame) {
        effectTriggered = true;
        OnEffectTriggered?.Invoke(anim.effect);
      }
    }
    if (!projectileTriggered && !string.IsNullOrEmpty(anim.projectile) && anim.projectileFrame > 0) {
      if (previousFrame < anim.projectileFrame && currentFrame >= anim.projectileFrame) {
        projectileTriggered = true;
        OnProjectileTriggered?.Invoke(anim.projectile);
      }
    }
  }

  private void UpdateSprites(int frame) {
    foreach (var target in spriteTargets) {
      if (!IsSpriteTargetEnabled(target)) continue;
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
    criticalSpriteTargets.Clear();
    spriteTargetRenderers.Clear();
    if (spriteObjects != null && spriteObjects.Length > 0) {
      foreach (var go in spriteObjects) {
        if (go == null) continue;
        var sprite = go.GetComponent<SpriteWithNormals>();
        if (sprite != null) {
          spriteTargets.Add(sprite);
          if (IsCriticalSpriteTarget(sprite)) criticalSpriteTargets.Add(sprite);
          spriteTargetRenderers[sprite] = go.GetComponent<SpriteRenderer>();
        }
      }
    }
    if (spriteTargets.Count == 0 && rootTransform != null) {
      foreach (var sprite in rootTransform.GetComponentsInChildren<SpriteWithNormals>()) {
        if (sprite != null) {
          spriteTargets.Add(sprite);
          if (IsCriticalSpriteTarget(sprite)) criticalSpriteTargets.Add(sprite);
          spriteTargetRenderers[sprite] = sprite.GetComponent<SpriteRenderer>();
        }
      }
    }
  }

  static bool IsCriticalSpriteTarget(SpriteWithNormals target) {
    if (target == null) return false;
    var lib = target.libraryName;
    if (string.IsNullOrWhiteSpace(lib)) return false;
    return lib.IndexOf("/Skin/", StringComparison.OrdinalIgnoreCase) >= 0;
  }

  bool HasEnabledSpriteTargets() {
    for (var i = 0; i < spriteTargets.Count; i++) {
      if (IsSpriteTargetEnabled(spriteTargets[i])) return true;
    }
    return false;
  }

  int CountEnabledSpriteTargets() {
    var count = 0;
    for (var i = 0; i < spriteTargets.Count; i++) {
      if (IsSpriteTargetEnabled(spriteTargets[i])) count++;
    }
    return count;
  }

  bool IsSpriteTargetEnabled(SpriteWithNormals target) {
    if (target == null || !target.isActiveAndEnabled || target.DoNotRender) return false;
    if (!spriteTargetRenderers.TryGetValue(target, out var renderer) || renderer == null) {
      renderer = target.GetComponent<SpriteRenderer>();
      spriteTargetRenderers[target] = renderer;
    }
    if (renderer == null) return target.gameObject.activeInHierarchy;
    return renderer.gameObject.activeInHierarchy;
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
