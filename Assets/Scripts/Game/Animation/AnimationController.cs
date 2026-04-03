using System;
using System.Collections.Generic;
using System.Linq;
using CustomInspector;
using UnityEngine;

/// <summary>
/// Generic animation driver (non-MonoBehaviour). Host behaviours must call Tick/Cleanup and wire data/targets.
/// </summary>
public class AnimationController {
  static readonly bool EnableAttackTraceLogs = false;
  static readonly bool SuppressRuntimeWarningLogsForPerfPass = true;
  const int MaxTargetsForGateReadinessChecks = 96;
  const int FirstPlayGateSampleTargetCount = 24;
  // Increased from 8 to 16 to ensure more initial frames are requested immediately on switch, reducing first-frame blanks.
  const int PrimeImmediateStartFrameBudget = 16;
  const int FirstPlayActionPrimeMaxFrames = 72;
  const int MinAppearancePinAddressBudget = 32;
  const int LoadingOverlayPinAddressCap = 640;
  const int TransitionPrimeWindowFrames = 4;
  const int FirstPlayTransitionPrimeMaxFrames = 16;

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
  private readonly List<SpriteWithNormals> spriteTargetScanBuffer = new(32);
  private readonly Dictionary<SpriteWithNormals, SpriteRenderer> spriteTargetRenderers = new();
  private readonly HashSet<SpriteWithNormals> startupVisualHoldSuppressedTargets = new();
  private readonly Dictionary<string, string> animationKeyLookup = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, GameObject> bounceObjectByName = new(StringComparer.Ordinal);
  private readonly Dictionary<string, List<GameObject>> hBoxObjectsByName = new(StringComparer.Ordinal);
  private readonly HashSet<string> seenAnimationCategories = new(StringComparer.Ordinal);
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
  private string activeSpriteCategory;
  private int lastAppliedSpriteFrame = int.MinValue;
  private bool activePunchTrace;
  private string activePunchTraceAnimation;
  private string activePunchTraceCategory;
  private float activePunchTraceStartRealtime;
  private int activePunchTracePreviousFrame = int.MinValue;
  private int activePunchTraceAdvancedFrames;
  private int activePunchTraceSkippedFrames;

  private bool hasPendingAnimationSwitch;
  private string pendingAnimation;
  private string pendingQueuedAnimation;
  private string pendingCategory;
  private int pendingStartFrame;
  private int pendingReadyEndFrame;
  private float pendingSwitchStartTime;
  private float pendingSwitchDeadline;
  private bool holdCurrentAnimationOnStartFrameUntilReady;
  private string holdCategory;
  private int holdFrame;
  private int pendingVisualFrame = int.MinValue;
  private string pendingVisualCategory;
  private float pendingVisualFrameStartedAt;
  private int visualSyncTimeoutLogFrame = -1;
  private bool startupVisualHoldActive;
  private float startupVisualHoldStartedAt;
  private string appearanceOwnerId;
  private TextureResidencyCache.PinClass appearancePinClass = TextureResidencyCache.PinClass.Enemy;
  private int appearancePinRefreshOffset;
  private readonly List<string> appearancePinAddressBuffer = new(512);
  private readonly HashSet<string> appearancePinAddressSet = new(StringComparer.OrdinalIgnoreCase);
  private readonly HashSet<string> predictedAnimations = new(StringComparer.Ordinal);
  private readonly List<string> warmPlaybackAnimationScratch = new(16);
  private readonly HashSet<string> warmPlaybackAnimationSeenScratch = new(StringComparer.Ordinal);
  private readonly List<string> warmPlaybackAddressScratch = new(512);
  private readonly HashSet<string> warmPlaybackSeenAddressScratch = new(StringComparer.OrdinalIgnoreCase);
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
  // Global switch gating is disabled by default; frame-level sync handles readiness without blocking state changes.
  static int runtimeSwitchGateMs = 0;
  const int MaxSwitchReadinessWindowFrames = 12;
  // Keep switch fail-open tight so playback does not freeze while waiting on streaming.
  const float MaxSwitchHardTimeoutSeconds = 0.35f;
  // Keep per-frame visual lockstep waits short, then fail open to avoid hard animation stalls.
  const float MaxVisualFrameSyncHoldSeconds = 0.12f;
  const float MaxGameplayVisualFrameSyncHoldSeconds = 0.05f;
  const float MaxStartupPlayerVisualFrameSyncHoldSeconds = 12f;

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
    appearancePinRefreshOffset = root != null ? ObjectEntityId.GetModulo(root, int.MaxValue) : 0;
    appearancePinAddressBuffer.Clear();
    appearancePinAddressSet.Clear();
    predictedAnimations.Clear();
    activeSpriteCategory = null;
    InvalidateSpriteFrameCache();
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
    animationKeyLookup.Clear();
    foreach (var pair in animationData) {
      var key = pair.Key;
      if (string.IsNullOrWhiteSpace(key)) continue;
      animationKeyLookup[key] = key;
    }
    seenAnimationCategories.Clear();
    ClearStartFrameHold();
    InvalidateSpriteFrameCache();
    InvalidateAppearancePinSnapshot();
  }

  public void SetSpriteObjects(IEnumerable<GameObject> targets) {
    spriteObjects = targets != null ? targets.ToArray() : Array.Empty<GameObject>();
    CacheSpriteTargets();
    activeSpriteCategory = null;
    InvalidateSpriteFrameCache();
    InvalidateAppearancePinSnapshot();
  }

  public void SetBounceObjects(IEnumerable<GameObject> targets) {
    bounceObjects = targets != null ? targets.ToArray() : Array.Empty<GameObject>();
    RebuildBounceObjectLookup();
  }

  public void SetHBoxObjects(IEnumerable<GameObject> targets) {
    hBoxObjects = targets != null ? targets.ToArray() : Array.Empty<GameObject>();
    RebuildHBoxObjectLookup();
  }

  void RebuildBounceObjectLookup() {
    bounceObjectByName.Clear();
    if (bounceObjects == null || bounceObjects.Length == 0) return;
    for (var i = 0; i < bounceObjects.Length; i++) {
      var go = bounceObjects[i];
      if (go == null || string.IsNullOrWhiteSpace(go.name)) continue;
      if (bounceObjectByName.ContainsKey(go.name)) continue;
      bounceObjectByName[go.name] = go;
    }
  }

  void RebuildHBoxObjectLookup() {
    hBoxObjectsByName.Clear();
    if (hBoxObjects == null || hBoxObjects.Length == 0) return;
    for (var i = 0; i < hBoxObjects.Length; i++) {
      var go = hBoxObjects[i];
      if (go == null || string.IsNullOrWhiteSpace(go.name)) continue;
      if (!hBoxObjectsByName.TryGetValue(go.name, out var list)) {
        list = new List<GameObject>(1);
        hBoxObjectsByName[go.name] = list;
      }
      list.Add(go);
    }
  }

  public void Tick(float deltaTime) {
    TextureResidencyCache.PumpOncePerFrame();
    var hasEnabledSpriteTargets = HasEnabledSpriteTargets();
    if (!hasEnabledSpriteTargets) {
      if (hadEnabledSpriteTargetsLastTick) {
        ReleaseAppearancePins();
      }
      hadEnabledSpriteTargetsLastTick = false;
    }
    else {
      if (!hadEnabledSpriteTargetsLastTick) {
        InvalidateSpriteFrameCache();
      }
      hadEnabledSpriteTargetsLastTick = true;
      RefreshAppearancePins();
    }
    AdvanceAnimation(deltaTime);
  }

  public bool PlayAnimation(string animationName, bool forceRestart = false, bool resolveInterrupts = true) {
    if (!TryGetAnimationKey(animationName, out var requestedAnimation)) {
      LogAttackTrace("missing_requested", requestedAnimation: animationName, note: "TryGetAnimationKey failed");
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
    LogAttackTrace(
      "play_request",
      requestedAnimation: requestedAnimation,
      resolvedAnimation: resolvedAnimation,
      queuedAnimationName: queued,
      note: "forceRestart=" + (forceRestart ? 1 : 0) + " resolveInterrupts=" + (resolveInterrupts ? 1 : 0)
    );

    // Prevent movement-loop requests from stomping pending action switches (e.g. attacks)
    // while readiness gating is still warming required frames.
    if (hasPendingAnimationSwitch &&
        IsLocomotionAnimationName(resolvedAnimation) &&
        !IsLocomotionAnimationName(pendingAnimation)) {
      LogAttackTrace("skip_locomotion_during_pending", requestedAnimation: requestedAnimation, resolvedAnimation: resolvedAnimation, queuedAnimationName: queued);
      return true;
    }

    if (string.IsNullOrWhiteSpace(resolvedAnimation) || !animationData.ContainsKey(resolvedAnimation)) {
      LogAttackTrace("missing_resolved", requestedAnimation: requestedAnimation, resolvedAnimation: resolvedAnimation, queuedAnimationName: queued);
      return false;
    }

    if (!forceRestart &&
        isPlaying &&
        string.Equals(resolvedAnimation, currentAnimation, StringComparison.Ordinal) &&
        !hasPendingAnimationSwitch) {
      LogAttackTrace("skip_same_current", requestedAnimation: requestedAnimation, resolvedAnimation: resolvedAnimation, queuedAnimationName: queued);
      return true;
    }

    if (!forceRestart &&
        hasPendingAnimationSwitch &&
        string.Equals(resolvedAnimation, pendingAnimation, StringComparison.Ordinal)) {
      if (!string.IsNullOrWhiteSpace(queued)) {
        pendingQueuedAnimation = queued;
      }
      LogAttackTrace("skip_same_pending", requestedAnimation: requestedAnimation, resolvedAnimation: resolvedAnimation, queuedAnimationName: queued);
      return true;
    }

    EnsureRuntimeSwitchSettings();
    var anim = animationData[resolvedAnimation];
    var category = ResolveAnimationCategory(resolvedAnimation, anim);
    var isTransitionCategory = IsTransitionCategory(category);
    var enabledTargetCount = CountEnabledSpriteTargets();
    var loadingOverlayWarmGateActive =
      SpriteStreamingLoadingState.IsLoadingOverlayActive &&
      StreamingWarmOrchestrator.IsWarmGateRunning;
    var gateMs = loadingOverlayWarmGateActive ? Math.Max(runtimeSwitchGateMs, 0) : 0;
    var shouldHoldInitialPlayerStartFrame = ShouldHoldInitialPlayerStartFrame(enabledTargetCount, category);

    if (gateMs <= 0) {
      // Global no-gate mode: commit immediately and let per-frame sync absorb streaming lag.
      if (isTransitionCategory) {
        PrimeTransitionAndQueuedWindows(category, anim, queued);
      }
      else if (shouldHoldInitialPlayerStartFrame) {
        PrimeTargetsForAnimation(category, anim.start, anim.start);
      }
      else {
        var shouldPrimeFullWindow =
          !HasSeenAnimationCategory(category) &&
          !IsLocomotionAnimationName(resolvedAnimation);
        PrimeTargetsForAnimation(category, anim.start, anim.end, primeFullWindow: shouldPrimeFullWindow);
      }
      if (shouldHoldInitialPlayerStartFrame) {
        BeginStartupVisualHold();
      }
      LogAttackTrace("commit_no_gate", requestedAnimation: requestedAnimation, resolvedAnimation: resolvedAnimation, queuedAnimationName: queued, category: category);
      ClearPendingAnimationSwitch();
      CommitAnimationSwitch(
        resolvedAnimation,
        queued,
        category,
        holdOnStartFrameUntilReady: shouldHoldInitialPlayerStartFrame
      );
      return true;
    }

    var canGate = Application.isPlaying &&
                  !forceRestart &&
                  !string.IsNullOrEmpty(currentAnimation) &&
                  !string.Equals(currentAnimation, resolvedAnimation, StringComparison.Ordinal) &&
                  enabledTargetCount > 0 &&
                  enabledTargetCount <= MaxTargetsForGateReadinessChecks;
    var shouldUseSampledFirstPlayGate =
      Application.isPlaying &&
      !forceRestart &&
      !string.IsNullOrEmpty(currentAnimation) &&
      !string.Equals(currentAnimation, resolvedAnimation, StringComparison.Ordinal) &&
      enabledTargetCount > MaxTargetsForGateReadinessChecks &&
      !HasSeenAnimationCategory(category);

    if (isTransitionCategory) {
      // Transition slices ("To"/"To2") are short and frequent.
      // Gating every transition creates visible stall loops when these frames are not yet resident.
      PrimeTransitionAndQueuedWindows(category, anim, queued);
    }

    if (!isTransitionCategory && (canGate || shouldUseSampledFirstPlayGate)) {
      if (shouldUseSampledFirstPlayGate) {
        PrimeSampledTargetsForAnimation(category, anim.start, anim.end, FirstPlayGateSampleTargetCount);
      }
      else {
        PrimeTargetsForAnimation(category, anim.start, anim.end);
      }
      // First-play can gate a short readiness window; repeat plays should only gate start frame.
      var readinessEndFrame = HasSeenAnimationCategory(category)
        ? Math.Max(anim.start, 1)
        : CalculateSwitchReadinessEndFrame(anim.start, anim.end);
      var ready = shouldUseSampledFirstPlayGate
        ? AreSampledTargetsReadyForWindow(category, anim.start, readinessEndFrame, FirstPlayGateSampleTargetCount)
        : AreTargetsReadyForWindow(category, anim.start, readinessEndFrame);
      if (!ready && gateMs > 0) {
        LogAttackTrace("begin_pending_gate", requestedAnimation: requestedAnimation, resolvedAnimation: resolvedAnimation, queuedAnimationName: queued, category: category);
        BeginPendingAnimationSwitch(resolvedAnimation, queued, category, anim.start, readinessEndFrame, gateMs);
        return true;
      }
      SpriteStreamingDiagnostics.RecordAnimationSwitchWait(0f, false);
    }
    else {
      ClearPendingAnimationSwitch();
    }

    LogAttackTrace("commit_final", requestedAnimation: requestedAnimation, resolvedAnimation: resolvedAnimation, queuedAnimationName: queued, category: category);
    CommitAnimationSwitch(resolvedAnimation, queued, category);
    return true;
  }

  public void ForceAnimation(string animationName = null) {
    string anim = animationName ?? (!string.IsNullOrEmpty(CurrentAnimation) ? CurrentAnimation : defaultAnimation);
    if (string.IsNullOrEmpty(anim)) return;
    PlayAnimation(anim, forceRestart: true, resolveInterrupts: false);
  }

  bool ShouldHoldInitialPlayerStartFrame(int enabledTargetCount, string category) {
    return Application.isPlaying &&
           appearancePinClass == TextureResidencyCache.PinClass.Player &&
           !string.IsNullOrWhiteSpace(category) &&
           !string.IsNullOrEmpty(defaultAnimation) &&
           string.IsNullOrEmpty(currentAnimation) &&
           enabledTargetCount > 1 &&
           HasMixedVisibleSpriteTargets();
  }

  void BeginStartupVisualHold() {
    startupVisualHoldActive = true;
    startupVisualHoldStartedAt = Time.realtimeSinceStartup;
    HideStartupVisualTargetsForHold();
    if (!ShouldLogStartupVisualHold()) return;
    Debug.Log(
      "[AnimationController][StartupSync] stage=begin" +
      " animation='" + (defaultAnimation ?? "") + "'" +
      " targets=" + spriteTargets.Count +
      " enabled_targets=" + CountEnabledSpriteTargets()
    );
  }

  public void ReleaseAppearancePins() {
    if (string.IsNullOrWhiteSpace(appearanceOwnerId)) return;
    TextureResidencyCache.ReleaseOwnerPins(appearanceOwnerId);
    appearancePinAddressBuffer.Clear();
    appearancePinAddressSet.Clear();
    InvalidateAppearancePinSnapshot();
  }

  public void SetAppearancePinOwner(
    string newOwnerId,
    TextureResidencyCache.PinClass newPinClass = TextureResidencyCache.PinClass.Enemy
  ) {
    var normalizedOwnerId = string.IsNullOrWhiteSpace(newOwnerId) ? "" : newOwnerId.Trim();
    if (string.Equals(appearanceOwnerId, normalizedOwnerId, StringComparison.Ordinal) &&
        appearancePinClass == newPinClass) {
      return;
    }

    if (!string.IsNullOrWhiteSpace(appearanceOwnerId)) {
      TextureResidencyCache.ReleaseOwnerPins(appearanceOwnerId);
    }

    appearanceOwnerId = normalizedOwnerId;
    appearancePinClass = newPinClass;
    appearancePinAddressBuffer.Clear();
    appearancePinAddressSet.Clear();
    InvalidateAppearancePinSnapshot();
  }

  public void InvalidateSpriteFrameCache() {
    lastAppliedSpriteFrame = int.MinValue;
  }

  public void PauseAnimation() {
    EndActivePunchTrace("paused", currentFrame);
    isPlaying = false;
    ClearPendingAnimationSwitch();
    ClearStartFrameHold();
    CancelAllTweens();
  }

  public void ResumeAnimation() {
    if (!string.IsNullOrEmpty(currentAnimation)) {
      isPlaying = true;
      SetBounces();
    }
  }

  public void StopAnimation(bool resetToDefault = false) {
    EndActivePunchTrace("stopped", currentFrame);
    isPlaying = false;
    queuedAnimation = null;
    ClearPendingAnimationSwitch();
    ClearStartFrameHold();
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
    EndActivePunchTrace("cleanup", currentFrame);
    ReleaseAppearancePins();
    ClearPendingAnimationSwitch();
    ClearStartFrameHold();
    CancelAllTweens();
    if (resetLeanTweenManager && !hasResetLeanTween) {
      LeanTween.reset();
      hasResetLeanTween = true;
    }
  }

  private void AdvanceAnimation(float deltaTime) {
    var deltaMs = deltaTime * 1000f;
    TryFinalizePendingAnimationSwitch();
    if (holdCurrentAnimationOnStartFrameUntilReady) {
      if (string.IsNullOrWhiteSpace(currentAnimation) || !animationData.ContainsKey(currentAnimation)) {
        ClearStartFrameHold();
      }
      else if (!AreAllTargetsReadyForWindow(holdCategory, holdFrame, holdFrame)) {
        PrimeTargetsForAnimation(holdCategory, holdFrame, holdFrame);
        TraceActivePunchFrameStep(holdFrame, deltaMs);
        return;
      }
      else {
        UpdateSprites(holdFrame);
        CompleteStartupVisualHold("ready");
        isPlaying = true;
        ClearStartFrameHold();
      }
    }
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
          TraceActivePunchFrameStep(anim.end, deltaMs);
          EndActivePunchTrace("queued_next", anim.end);
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

    var renderCategory = string.IsNullOrWhiteSpace(activeSpriteCategory)
      ? ResolveAnimationCategory(currentAnimation, anim)
      : activeSpriteCategory;
    var frameToApply = ResolveVisualFrameForApply(currentFrame, renderCategory);
    UpdateSprites(frameToApply);
    if (cycleReset) {
      ResetAnimationEvents(anim);
    }
    TryTriggerFrameEvents(anim, lastFrame, currentFrame);
    lastFrame = currentFrame;
    TraceActivePunchFrameStep(currentFrame, deltaMs);
    if (!isPlaying && !anim.loop && !anim.pingPong && currentFrame >= anim.end) {
      EndActivePunchTrace("completed", currentFrame);
    }
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

    if (animationKeyLookup.TryGetValue(normalized, out var keyed)) {
      resolved = keyed;
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
    runtimeSwitchGateMs = 0;
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
    RefreshAppearancePins();
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

    if (!isReady) {
      if (isPlaying && now < pendingSwitchDeadline) return;

      var isFirstFrameReady = AreTargetsReadyForWindow(pendingCategory, pendingStartFrame, pendingStartFrame);
      if (!isFirstFrameReady && now < pendingSwitchStartTime + MaxSwitchHardTimeoutSeconds) return;

      var timeoutWaitMs = Mathf.Max((now - pendingSwitchStartTime) * 1000f, 0f);
      SpriteStreamingDiagnostics.RecordAnimationSwitchWait(timeoutWaitMs, timeoutWaitMs > 0.1f);

      if (!isFirstFrameReady &&
          IsTransitionCategory(pendingCategory) &&
          !string.IsNullOrWhiteSpace(pendingQueuedAnimation) &&
          TryGetAnimationKey(pendingQueuedAnimation, out var queuedResolved) &&
          animationData.TryGetValue(queuedResolved, out var queuedAnim) &&
          queuedAnim != null) {
        var queuedCategory = ResolveAnimationCategory(queuedResolved, queuedAnim);
        if (!SuppressRuntimeWarningLogsForPerfPass) {
          Debug.LogWarning(
            "[AnimationController] Transition gate timed out; skipping transition. " +
            "transition='" + pendingAnimation +
            "' queued='" + queuedResolved +
            "' wait_ms=" + timeoutWaitMs.ToString("0.0")
          );
        }
        CommitAnimationSwitch(
          queuedResolved,
          nextQueuedAnimation: null,
          queuedCategory,
          holdOnStartFrameUntilReady: false
        );
        ClearPendingAnimationSwitch();
        return;
      }

      if (!isFirstFrameReady) {
        if (!SuppressRuntimeWarningLogsForPerfPass) {
          Debug.LogWarning(
            "[AnimationController] Switch gate timed out; committing without start-frame hold. " +
            "animation='" + pendingAnimation +
            "' category='" + pendingCategory +
            "' wait_ms=" + timeoutWaitMs.ToString("0.0") +
            " start_frame=" + pendingStartFrame +
            " ready_end_frame=" + pendingReadyEndFrame
          );
        }
      }
      CommitAnimationSwitch(
        pendingAnimation,
        pendingQueuedAnimation,
        pendingCategory,
        holdOnStartFrameUntilReady: false
      );
      ClearPendingAnimationSwitch();
      return;
    }

    var waitMs = Mathf.Max((now - pendingSwitchStartTime) * 1000f, 0f);
    SpriteStreamingDiagnostics.RecordAnimationSwitchWait(waitMs, waitMs > 0.1f);
    CommitAnimationSwitch(pendingAnimation, pendingQueuedAnimation, pendingCategory);
    ClearPendingAnimationSwitch();
  }

  void PrimeTargetsForAnimation(string targetCategory, int startFrame, int endFrame, bool primeFullWindow = false) {
    var warmupFrames = Math.Max(runtimeWarmupFrames, 1);
    var firstPlayActionFrames = Math.Max(warmupFrames, FirstPlayActionPrimeMaxFrames);
    var clampedStart = Math.Max(startFrame, 1);
    var clampedEnd = Math.Max(endFrame, clampedStart);
    var primeWindowFrames = primeFullWindow ? firstPlayActionFrames : warmupFrames;
    var targetEnd = Math.Min(clampedEnd, clampedStart + primeWindowFrames - 1);
    var immediateBudget = PrimeImmediateStartFrameBudget;
    PrimeTargetsForAnimationSet(
      criticalSpriteTargets,
      targetCategory,
      clampedStart,
      targetEnd,
      skipCriticalTargets: false,
      maxTargets: int.MaxValue,
      ref immediateBudget
    );
    PrimeTargetsForAnimationSet(
      spriteTargets,
      targetCategory,
      clampedStart,
      targetEnd,
      skipCriticalTargets: true,
      maxTargets: int.MaxValue,
      ref immediateBudget
    );
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

  public void PrimeAnimationStarts(IReadOnlyList<string> animationKeys, int framesPerAnimation = 1, int maxAnimations = 0) {
    if (!Application.isPlaying) return;
    if (animationData == null || animationData.Count == 0 || spriteTargets.Count == 0) return;
    if (animationKeys == null || animationKeys.Count <= 0) {
      PrimeAllAnimationStarts(framesPerAnimation, maxAnimations);
      return;
    }

    var warmFrames = Math.Max(framesPerAnimation, 1);
    var primed = 0;
    for (var i = 0; i < animationKeys.Count; i++) {
      if (maxAnimations > 0 && primed >= maxAnimations) {
        break;
      }

      if (!TryGetAnimationKey(animationKeys[i], out var animationName)) {
        continue;
      }

      if (!animationData.TryGetValue(animationName, out var anim) || anim == null) {
        continue;
      }

      var categoryName = ResolveAnimationCategory(animationName, anim);
      var startFrame = Math.Max(anim.start, 1);
      var endFrame = Math.Max(startFrame, startFrame + warmFrames - 1);
      PrimeTargetsForAnimation(categoryName, startFrame, endFrame);
      primed++;
    }
  }

  public int WarmAllAnimationPlayback(
    int passCount = 1,
    int maxAnimations = 12,
    int maxFramesPerAnimation = MaxSwitchReadinessWindowFrames
  ) {
    if (!Application.isPlaying) return 0;
    var addresses = warmPlaybackAddressScratch;
    var seenAddresses = warmPlaybackSeenAddressScratch;
    addresses.Clear();
    seenAddresses.Clear();
    var added = CollectWarmPlaybackAddresses(addresses, seenAddresses, passCount, maxAnimations, maxFramesPerAnimation);
    if (addresses.Count > 0) {
      TextureResidencyCache.RequestLoadBatch(
        addresses,
        TextureResidencyCache.LoadPriority.Warmup,
        allowAtlasExpansion: true
      );
    }
    addresses.Clear();
    seenAddresses.Clear();
    return added;
  }

  public int CollectWarmPlaybackAddresses(
    List<string> outAddresses,
    HashSet<string> seenAddresses,
    int passCount = 1,
    int maxAnimations = 12,
    int maxFramesPerAnimation = MaxSwitchReadinessWindowFrames,
    int maxAddresses = int.MaxValue
  ) {
    if (animationData == null || animationData.Count == 0 || spriteTargets.Count == 0) return 0;
    if (outAddresses == null) return 0;

    var beforeCount = outAddresses.Count;
    var passes = Math.Max(passCount, 1);
    var cappedAnimations = maxAnimations > 0 ? maxAnimations : animationData.Count;
    cappedAnimations = Math.Max(cappedAnimations, 1);
    var frameLimit = Math.Max(maxFramesPerAnimation, 1);
    var prioritizedAnimations = CollectWarmPlaybackAnimations(cappedAnimations);

    for (var pass = 0; pass < passes; pass++) {
      for (var i = 0; i < prioritizedAnimations.Count; i++) {
        if (outAddresses.Count >= maxAddresses) return Math.Max(outAddresses.Count - beforeCount, 0);
        var animationName = prioritizedAnimations[i];
        if (!animationData.TryGetValue(animationName, out var anim) || anim == null) continue;
        if (string.IsNullOrWhiteSpace(animationName)) continue;

        var categoryName = ResolveAnimationCategory(animationName, anim);
        var startFrame = Math.Max(anim.start, 1);
        var clipEnd = Math.Max(anim.end, startFrame);
        var endFrame = Math.Min(clipEnd, startFrame + frameLimit - 1);

        CollectAnimationStartAddressesForTargetSet(
          criticalSpriteTargets,
          categoryName,
          startFrame,
          endFrame,
          outAddresses,
          seenAddresses,
          maxAddresses,
          skipCriticalTargets: false
        );
        if (outAddresses.Count >= maxAddresses) return Math.Max(outAddresses.Count - beforeCount, 0);
        CollectAnimationStartAddressesForTargetSet(
          spriteTargets,
          categoryName,
          startFrame,
          endFrame,
          outAddresses,
          seenAddresses,
          maxAddresses,
          skipCriticalTargets: true
        );
      }
    }

    return Math.Max(outAddresses.Count - beforeCount, 0);
  }

  List<string> CollectWarmPlaybackAnimations(int maxAnimations) {
    maxAnimations = Math.Max(maxAnimations, 1);
    var ordered = warmPlaybackAnimationScratch;
    var seen = warmPlaybackAnimationSeenScratch;
    ordered.Clear();
    seen.Clear();

    var targetCapacity = Math.Min(maxAnimations, animationData.Count);
    if (targetCapacity > ordered.Capacity) {
      ordered.Capacity = targetCapacity;
    }

    TryAddWarmPlaybackAnimation(currentAnimation, ordered, seen, maxAnimations);
    TryAddWarmPlaybackAnimation(defaultAnimation, ordered, seen, maxAnimations);
    TryAddWarmPlaybackAnimation(pendingAnimation, ordered, seen, maxAnimations);
    TryAddWarmPlaybackAnimation(queuedAnimation, ordered, seen, maxAnimations);

    if (ordered.Count < maxAnimations) {
      foreach (var pair in animationData) {
        if (ordered.Count >= maxAnimations) break;
        if (!IsLocomotionAnimationName(pair.Key)) continue;
        TryAddWarmPlaybackAnimation(pair.Key, ordered, seen, maxAnimations);
      }
    }

    if (ordered.Count < maxAnimations) {
      foreach (var pair in animationData) {
        if (ordered.Count >= maxAnimations) break;
        TryAddWarmPlaybackAnimation(pair.Key, ordered, seen, maxAnimations);
      }
    }

    return ordered;
  }

  void TryAddWarmPlaybackAnimation(
    string candidate,
    List<string> ordered,
    HashSet<string> seen,
    int maxAnimations
  ) {
    if (ordered == null || seen == null) return;
    if (ordered.Count >= maxAnimations) return;
    if (!TryGetAnimationKey(candidate, out var resolved)) return;
    if (!seen.Add(resolved)) return;
    ordered.Add(resolved);
  }

  public int CollectAnimationStartAddresses(
    List<string> outAddresses,
    HashSet<string> seenAddresses,
    int framesPerAnimation = 1,
    int maxAnimations = 0,
    int maxAddresses = int.MaxValue
  ) {
    if (!Application.isPlaying) return 0;
    if (outAddresses == null) return 0;
    if (animationData == null || animationData.Count == 0 || spriteTargets.Count == 0) return 0;

    maxAddresses = Math.Max(maxAddresses, 1);
    if (outAddresses.Count >= maxAddresses) return 0;
    var warmFrames = Math.Max(framesPerAnimation, 1);
    var collectedAnimations = 0;
    var beforeCount = outAddresses.Count;

    foreach (var pair in animationData) {
      if (outAddresses.Count >= maxAddresses) break;
      var animationName = pair.Key;
      var anim = pair.Value;
      if (anim == null || string.IsNullOrWhiteSpace(animationName)) continue;

      var categoryName = ResolveAnimationCategory(animationName, anim);
      var startFrame = Math.Max(anim.start, 1);
      var endFrame = Math.Max(startFrame, startFrame + warmFrames - 1);

      CollectAnimationStartAddressesForTargetSet(
        criticalSpriteTargets,
        categoryName,
        startFrame,
        endFrame,
        outAddresses,
        seenAddresses,
        maxAddresses
      );
      if (outAddresses.Count < maxAddresses) {
        CollectAnimationStartAddressesForTargetSet(
          spriteTargets,
          categoryName,
          startFrame,
          endFrame,
          outAddresses,
          seenAddresses,
          maxAddresses,
          skipCriticalTargets: true
        );
      }

      collectedAnimations++;
      if (maxAnimations > 0 && collectedAnimations >= maxAnimations) break;
    }

    return Math.Max(outAddresses.Count - beforeCount, 0);
  }

  public int CollectAllAnimationFrameAddresses(
    List<string> outAddresses,
    HashSet<string> seenAddresses,
    int maxAddresses = int.MaxValue
  ) {
    if (!Application.isPlaying) return 0;
    if (outAddresses == null) return 0;
    if (animationData == null || animationData.Count == 0 || spriteTargets.Count == 0) return 0;

    maxAddresses = Math.Max(maxAddresses, 1);
    if (outAddresses.Count >= maxAddresses) return 0;
    var beforeCount = outAddresses.Count;

    foreach (var pair in animationData) {
      if (outAddresses.Count >= maxAddresses) break;
      var animationName = pair.Key;
      var anim = pair.Value;
      if (anim == null || string.IsNullOrWhiteSpace(animationName)) continue;

      var categoryName = ResolveAnimationCategory(animationName, anim);
      var startFrame = Math.Max(anim.start, 1);
      var endFrame = Math.Max(anim.end, startFrame);

      CollectAnimationStartAddressesForTargetSet(
        criticalSpriteTargets,
        categoryName,
        startFrame,
        endFrame,
        outAddresses,
        seenAddresses,
        maxAddresses
      );
      if (outAddresses.Count < maxAddresses) {
        CollectAnimationStartAddressesForTargetSet(
          spriteTargets,
          categoryName,
          startFrame,
          endFrame,
          outAddresses,
          seenAddresses,
          maxAddresses,
          skipCriticalTargets: true
        );
      }
    }

    return Math.Max(outAddresses.Count - beforeCount, 0);
  }

  void CollectAnimationStartAddressesForTargetSet(
    List<SpriteWithNormals> targets,
    string categoryName,
    int startFrame,
    int endFrame,
    List<string> outAddresses,
    HashSet<string> seenAddresses,
    int maxAddresses,
    bool skipCriticalTargets = false
  ) {
    if (targets == null || targets.Count == 0) return;
    if (outAddresses == null || outAddresses.Count >= maxAddresses) return;

    for (var i = 0; i < targets.Count; i++) {
      if (outAddresses.Count >= maxAddresses) return;
      var target = targets[i];
      if (target == null) continue;
      if (skipCriticalTargets && IsCriticalSpriteTarget(target)) continue;
      if (!IsSpriteTargetEnabled(target)) continue;

      target.CollectAnimationWindowAddresses(
        categoryName,
        startFrame,
        endFrame,
        lookAheadFrames: 0,
        outAddresses,
        seenAddresses,
        maxAddresses
      );
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
        if (!target.IsFrameReady(frame, out _, targetCategory)) return false;
      }
    }

    if (hasCriticalTargets) return true;

    // TODO: foreach on spriteTargets allocates an enumerator on every call â€” this runs per-frame
    // during active gating. Replace with an indexed for-loop (same pattern as criticalSpriteTargets above).
    for (var i = 0; i < spriteTargets.Count; i++) {
      var target = spriteTargets[i];
      if (!IsSpriteTargetEnabled(target)) continue;
      for (var frame = minFrame; frame <= maxFrame; frame++) {
        if (!target.IsFrameReady(frame, out _, targetCategory)) return false;
      }
    }
    return true;
  }

  bool AreAllTargetsReadyForFrame(string targetCategory, int frame) {
    return AreAllTargetsReadyForWindow(targetCategory, frame, frame);
  }

  bool AreAllTargetsReadyForWindow(string targetCategory, int startFrame, int endFrame) {
    var minFrame = Math.Max(startFrame, 1);
    var maxFrame = Math.Max(endFrame, minFrame);
    for (var i = 0; i < spriteTargets.Count; i++) {
      var target = spriteTargets[i];
      if (!IsSpriteTargetEnabled(target)) continue;
      for (var frame = minFrame; frame <= maxFrame; frame++) {
        if (!target.IsFrameReady(frame, out _, targetCategory)) return false;
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

  static int CalculateTransitionPrimeEndFrame(int startFrame, int endFrame) {
    var clampedStart = Math.Max(startFrame, 1);
    var clampedClipEnd = Math.Max(endFrame, clampedStart);
    var primeFrames = Math.Max(1, Math.Min(runtimeWarmupFrames, TransitionPrimeWindowFrames));
    return Math.Min(clampedClipEnd, clampedStart + primeFrames - 1);
  }

  void PrimeTransitionAndQueuedWindows(string transitionCategory, AnimData transitionAnim, string queuedAnimationName) {
    if (transitionAnim == null) return;
    var transitionPrimeEnd = CalculateTransitionPrimeEndFrame(transitionAnim.start, transitionAnim.end);
    if (!HasSeenAnimationCategory(transitionCategory)) {
      var transitionClipEnd = Math.Max(transitionAnim.end, transitionAnim.start);
      var firstPlayTransitionEnd = transitionAnim.start + FirstPlayTransitionPrimeMaxFrames - 1;
      transitionPrimeEnd = Math.Min(transitionClipEnd, Math.Max(transitionPrimeEnd, firstPlayTransitionEnd));
    }
    PrimeTargetsForAnimation(transitionCategory, transitionAnim.start, transitionPrimeEnd);

    if (string.IsNullOrWhiteSpace(queuedAnimationName)) return;
    if (!TryGetAnimationKey(queuedAnimationName, out var queuedResolved)) return;
    if (!animationData.TryGetValue(queuedResolved, out var queuedAnim) || queuedAnim == null) return;

    var queuedCategory = ResolveAnimationCategory(queuedResolved, queuedAnim);
    var queuedPrimeEnd = CalculateTransitionPrimeEndFrame(queuedAnim.start, queuedAnim.end);
    var shouldPrimeQueuedFullWindow =
      !HasSeenAnimationCategory(queuedCategory) &&
      !IsLocomotionAnimationName(queuedResolved) &&
      !IsTransitionCategory(queuedCategory);
    if (shouldPrimeQueuedFullWindow) {
      PrimeTargetsForAnimation(queuedCategory, queuedAnim.start, queuedAnim.end, primeFullWindow: true);
      return;
    }
    PrimeTargetsForAnimation(queuedCategory, queuedAnim.start, queuedPrimeEnd);
  }

  void CommitAnimationSwitch(
    string nextAnimation,
    string nextQueuedAnimation,
    string targetCategory,
    bool holdOnStartFrameUntilReady = false
  ) {
    EndActivePunchTrace("interrupted_by_switch", currentFrame);
    currentAnimation = nextAnimation;
    queuedAnimation = string.IsNullOrWhiteSpace(nextQueuedAnimation) ? null : nextQueuedAnimation;

    animationTimer = 0f;
    pingPong = false;
    isPlaying = !holdOnStartFrameUntilReady;
    var anim = animationData[currentAnimation];
    currentFrame = anim.start;

    if (holdOnStartFrameUntilReady) {
      holdCurrentAnimationOnStartFrameUntilReady = true;
      holdCategory = targetCategory;
      holdFrame = Math.Max(anim.start, 1);
    }
    else {
      ClearStartFrameHold();
    }

    SetAnimationCategory(targetCategory);
    if (holdOnStartFrameUntilReady) {
      PrimeTargetsForAnimation(targetCategory, currentFrame, currentFrame);
    }
    else {
      UpdateSprites(currentFrame);
    }
    SetBounces();
    ResetAnimationEvents(anim);
    TryTriggerFrameEvents(anim, lastFrame, currentFrame);
    lastFrame = currentFrame;
    MarkAnimationCategorySeen(targetCategory);
    BeginActivePunchTrace(anim, targetCategory);
  }

  static string ResolveAnimationCategory(string animationName, AnimData anim) {
    return anim.To == 1 ? "To" : anim.To == 2 ? "To2" : animationName;
  }

  static bool ShouldLogStartupVisualHold() {
    return Application.isEditor || Debug.isDebugBuild;
  }

  void CompleteStartupVisualHold(string stage) {
    if (!startupVisualHoldActive) return;
    if (ShouldLogStartupVisualHold()) {
      Debug.Log(
        "[AnimationController][StartupSync] stage=" + (string.IsNullOrWhiteSpace(stage) ? "complete" : stage.Trim()) +
        " animation='" + (currentAnimation ?? "") + "'" +
        " hold_frame=" + holdFrame +
        " elapsed_ms=" + ((Time.realtimeSinceStartup - startupVisualHoldStartedAt) * 1000f).ToString("0.0")
      );
    }
    ResetStartupVisualHoldState();
  }

  void ResetStartupVisualHoldState() {
    RevealStartupVisualTargetsAfterHold();
    startupVisualHoldActive = false;
    startupVisualHoldStartedAt = 0f;
  }

  static bool IsTransitionCategory(string category) {
    return string.Equals(category, "To", StringComparison.Ordinal) ||
           string.Equals(category, "To2", StringComparison.Ordinal);
  }

  // TODO: hardcoded locomotion names. Adding a new locomotion animation requires a code change.
  // Consider marking locomotion in AnimData (e.g. a bool isLocomotive flag) so this list is data-driven.
  static bool IsLocomotionAnimationName(string animationName) {
    if (string.IsNullOrWhiteSpace(animationName)) return false;
    return string.Equals(animationName, "Breathe", StringComparison.Ordinal) ||
           string.Equals(animationName, "Walk", StringComparison.Ordinal) ||
           string.Equals(animationName, "Run", StringComparison.Ordinal) ||
           string.Equals(animationName, "Sprint", StringComparison.Ordinal) ||
           string.Equals(animationName, "Stance", StringComparison.Ordinal);
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

  void ClearStartFrameHold() {
    ResetStartupVisualHoldState();
    holdCurrentAnimationOnStartFrameUntilReady = false;
    holdCategory = null;
    holdFrame = 0;
    ClearVisualFrameHold();
  }

  void ClearVisualFrameHold() {
    pendingVisualFrame = int.MinValue;
    pendingVisualCategory = null;
    pendingVisualFrameStartedAt = 0f;
  }

  int ResolveVisualFrameForApply(int desiredFrame, string targetCategory) {
    if (!Application.isPlaying) {
      ClearVisualFrameHold();
      return desiredFrame;
    }
    if (spriteTargets.Count <= 1 || string.IsNullOrWhiteSpace(targetCategory)) {
      ClearVisualFrameHold();
      return desiredFrame;
    }
    var loadingOverlayWarmGateActive = SpriteStreamingLoadingState.IsLoadingOverlayActive &&
                                       StreamingWarmOrchestrator.IsWarmGateRunning;
    var gameplayMixedAppearanceSync =
      !loadingOverlayWarmGateActive &&
      HasMixedVisibleSpriteTargets() &&
      spriteTargets.Count <= MaxTargetsForGateReadinessChecks;
    var startupPlayerVisualHold =
      holdCurrentAnimationOnStartFrameUntilReady &&
      startupVisualHoldActive &&
      appearancePinClass == TextureResidencyCache.PinClass.Player;
    if (!loadingOverlayWarmGateActive && !gameplayMixedAppearanceSync) {
      ClearVisualFrameHold();
      return desiredFrame;
    }
    if (AreAllTargetsReadyForFrame(targetCategory, desiredFrame)) {
      ClearVisualFrameHold();
      return desiredFrame;
    }
    // Request the blocked frame across visible parts and keep the actor coherent briefly.
    PrimeTargetsForAnimation(targetCategory, desiredFrame, desiredFrame);
    // Keep the first blocked-frame timestamp stable so timeout can elapse even as desiredFrame advances.
    if (pendingVisualFrame == int.MinValue ||
        !string.Equals(pendingVisualCategory, targetCategory, StringComparison.Ordinal)) {
      pendingVisualFrame = desiredFrame;
      pendingVisualCategory = targetCategory;
      pendingVisualFrameStartedAt = Time.realtimeSinceStartup;
    }

    var waitSeconds = Time.realtimeSinceStartup - pendingVisualFrameStartedAt;
    var maxHoldSeconds = startupPlayerVisualHold
      ? MaxStartupPlayerVisualFrameSyncHoldSeconds
      : (loadingOverlayWarmGateActive
        ? MaxVisualFrameSyncHoldSeconds
        : MaxGameplayVisualFrameSyncHoldSeconds);
    if (waitSeconds >= maxHoldSeconds) {
      if (visualSyncTimeoutLogFrame != Time.frameCount) {
        visualSyncTimeoutLogFrame = Time.frameCount;
        if (!SuppressRuntimeWarningLogsForPerfPass) {
          Debug.LogWarning(
            "[AnimationController] Visual frame sync timeout; applying unsynchronized frame. " +
            "animation='" + currentAnimation +
            "' category='" + targetCategory +
            "' hold_frame=" + pendingVisualFrame +
            " frame=" + desiredFrame +
            " wait_ms=" + (waitSeconds * 1000f).ToString("0.0")
          );
        }
      }
      if (startupPlayerVisualHold) {
        CompleteStartupVisualHold("timeout");
      }
      ClearVisualFrameHold();
      return desiredFrame;
    }

    if (lastAppliedSpriteFrame == int.MinValue) {
      return int.MinValue;
    }

    return lastAppliedSpriteFrame;
  }

  bool HasMixedVisibleSpriteTargets() {
    var hasVisibleCritical = false;
    var hasVisibleNonCritical = false;
    for (var i = 0; i < spriteTargets.Count; i++) {
      var target = spriteTargets[i];
      if (!IsSpriteTargetEnabled(target)) continue;
      if (IsCriticalSpriteTarget(target)) hasVisibleCritical = true;
      else hasVisibleNonCritical = true;
      if (hasVisibleCritical && hasVisibleNonCritical) return true;
    }
    return false;
  }

  void HideStartupVisualTargetsForHold() {
    startupVisualHoldSuppressedTargets.Clear();
    for (var i = 0; i < spriteTargets.Count; i++) {
      var target = spriteTargets[i];
      if (!IsSpriteTargetEnabled(target)) continue;
      target.SetExternalVisualSuppressed(true);
      startupVisualHoldSuppressedTargets.Add(target);
    }
  }

  void RevealStartupVisualTargetsAfterHold() {
    if (startupVisualHoldSuppressedTargets.Count <= 0) return;
    foreach (var target in startupVisualHoldSuppressedTargets) {
      if (target == null) continue;
      target.SetExternalVisualSuppressed(false);
    }
    startupVisualHoldSuppressedTargets.Clear();
  }

  void RefreshAppearancePins() {
    if (!Application.isPlaying) return;
    if (string.IsNullOrWhiteSpace(appearanceOwnerId)) return;
    var loadingOverlayWarmGateActive = SpriteStreamingLoadingState.IsLoadingOverlayActive &&
                                       StreamingWarmOrchestrator.IsWarmGateRunning;
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var queueBusy = queue.queuedCount > 0 || queue.inFlightCount > 0;
    var keepLoadedForSession = SpriteStreamingRuntimeSettings.KeepLoadedSpritesForSession;
    var pinWindowFrames = Math.Max(SpriteStreamingRuntimeSettings.PinWindowFrames, 1);
    var maxPredicted = Math.Max(SpriteStreamingRuntimeSettings.PinPredictedNextAnimations, 0);
    if (!loadingOverlayWarmGateActive) {
      // Gameplay mode: keep pin churn light to avoid trigger-time spikes.
      pinWindowFrames = Math.Min(pinWindowFrames, 8);
      maxPredicted = 0;
    }
    var pinRefreshBucketSize = Math.Max(SpriteStreamingRuntimeSettings.PinRefreshFrameBucketSize, 1);
    if (loadingOverlayWarmGateActive) {
      pinRefreshBucketSize = Math.Max(pinRefreshBucketSize, 64);
    }
    else if (keepLoadedForSession && !queueBusy && !hasPendingAnimationSwitch) {
      pinRefreshBucketSize = Math.Max(pinRefreshBucketSize, 48);
      pinWindowFrames = Math.Min(pinWindowFrames, 12);
      maxPredicted = Math.Min(maxPredicted, 1);
    }
    else {
      pinRefreshBucketSize = Math.Max(pinRefreshBucketSize, 16);
    }
    var maxPinAddresses = Math.Max(SpriteStreamingRuntimeSettings.MaxPinnedAddressesPerOwner, MinAppearancePinAddressBudget);
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive || StreamingWarmOrchestrator.IsWarmGateRunning) {
      maxPinAddresses = Math.Max(Math.Min(maxPinAddresses, LoadingOverlayPinAddressCap), MinAppearancePinAddressBudget);
    }
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
      if (loadingOverlayWarmGateActive) {
        CollectPredictedInterruptWindows(currentAnimation, pinWindowFrames, maxPredicted, maxPinAddresses);
      }
    }

    if (appearancePinAddressBuffer.Count < maxPinAddresses &&
        loadingOverlayWarmGateActive &&
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
        loadingOverlayWarmGateActive &&
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

      AddAppearancePinAddress(pair.RuntimeColorAddress, maxPinAddresses);
      AddAppearancePinAddress(pair.RuntimeNormalAddress, maxPinAddresses);
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

  void PrimeSampledTargetsForAnimation(string targetCategory, int startFrame, int endFrame, int maxSampleTargets) {
    var warmupFrames = Math.Max(runtimeWarmupFrames, 1);
    var clampedStart = Math.Max(startFrame, 1);
    var clampedEnd = Math.Max(endFrame, clampedStart);
    var targetEnd = Math.Min(clampedEnd, clampedStart + warmupFrames - 1);
    var sampleBudget = Math.Max(maxSampleTargets, 1);
    var immediateBudget = PrimeImmediateStartFrameBudget;
    var sampled = PrimeTargetsForAnimationSet(
      criticalSpriteTargets,
      targetCategory,
      clampedStart,
      targetEnd,
      skipCriticalTargets: false,
      maxTargets: sampleBudget,
      ref immediateBudget
    );
    if (sampled >= sampleBudget) return;
    PrimeTargetsForAnimationSet(
      spriteTargets,
      targetCategory,
      clampedStart,
      targetEnd,
      skipCriticalTargets: true,
      maxTargets: sampleBudget - sampled,
      ref immediateBudget
    );
  }

  int PrimeTargetsForAnimationSet(
    List<SpriteWithNormals> targets,
    string targetCategory,
    int startFrame,
    int endFrame,
    bool skipCriticalTargets,
    int maxTargets,
    ref int immediateBudget
  ) {
    if (targets == null || targets.Count <= 0 || maxTargets <= 0) return 0;
    var primed = 0;
    for (var i = 0; i < targets.Count; i++) {
      if (primed >= maxTargets) break;
      var target = targets[i];
      if (target == null) continue;
      if (skipCriticalTargets && IsCriticalSpriteTarget(target)) continue;
      if (!IsSpriteTargetEnabled(target)) continue;

      target.PrimeAnimationWindow(targetCategory, startFrame, endFrame, 0);
      PrimeImmediateStartFrame(target, targetCategory, startFrame, ref immediateBudget);
      primed++;
    }
    return primed;
  }

  void PrimeImmediateStartFrame(SpriteWithNormals target, string targetCategory, int frame, ref int immediateBudget) {
    if (target == null) return;
    var clampedFrame = Math.Max(frame, 1);
    if (!target.TryGetFrameAddressPair(clampedFrame, out var pair, targetCategory)) return;
    var loadingContextActive = SpriteStreamingLoadingState.IsLoadingOverlayActive || StreamingWarmOrchestrator.IsWarmGateRunning;
    var colorPriority = loadingContextActive ? TextureResidencyCache.LoadPriority.Warmup : TextureResidencyCache.LoadPriority.Immediate;

    if (!string.IsNullOrWhiteSpace(pair.RuntimeColorAddress)) {
      if (immediateBudget > 0) {
        TextureResidencyCache.RequestLoad(pair.RuntimeColorAddress, colorPriority);
        immediateBudget--;
      }
      else if (!loadingContextActive) {
        // If immediate budget is exhausted in gameplay, still enqueue as warmup.
        TextureResidencyCache.RequestLoad(pair.RuntimeColorAddress, TextureResidencyCache.LoadPriority.Warmup);
      }
    }
    if (!string.IsNullOrWhiteSpace(pair.RuntimeNormalAddress)) {
      if (loadingContextActive) {
        if (immediateBudget > 0) {
          TextureResidencyCache.RequestLoad(pair.RuntimeNormalAddress, TextureResidencyCache.LoadPriority.Warmup);
          immediateBudget--;
        }
      }
      else {
        // Keep normal maps warmup-priority to reduce immediate queue pressure.
        TextureResidencyCache.RequestLoad(pair.RuntimeNormalAddress, TextureResidencyCache.LoadPriority.Warmup);
      }
    }
  }

  bool AreSampledTargetsReadyForWindow(string targetCategory, int startFrame, int endFrame, int maxSampleTargets) {
    var sampleBudget = Math.Max(maxSampleTargets, 1);
    var sampled = 0;
    sampled += CountReadySampleTargetsForSet(
      criticalSpriteTargets,
      targetCategory,
      startFrame,
      endFrame,
      skipCriticalTargets: false,
      maxTargets: sampleBudget
    );
    if (sampled < 0) return false;
    if (sampled >= sampleBudget) return true;
    var secondaryReady = CountReadySampleTargetsForSet(
      spriteTargets,
      targetCategory,
      startFrame,
      endFrame,
      skipCriticalTargets: true,
      maxTargets: sampleBudget - sampled
    );
    if (secondaryReady < 0) return false;
    return true;
  }

  int CountReadySampleTargetsForSet(
    List<SpriteWithNormals> targets,
    string targetCategory,
    int startFrame,
    int endFrame,
    bool skipCriticalTargets,
    int maxTargets
  ) {
    if (targets == null || targets.Count <= 0 || maxTargets <= 0) return 0;
    var minFrame = Math.Max(startFrame, 1);
    var maxFrame = Math.Max(endFrame, minFrame);
    var sampled = 0;
    for (var i = 0; i < targets.Count; i++) {
      if (sampled >= maxTargets) break;
      var target = targets[i];
      if (target == null) continue;
      if (skipCriticalTargets && IsCriticalSpriteTarget(target)) continue;
      if (!IsSpriteTargetEnabled(target)) continue;
      for (var frame = minFrame; frame <= maxFrame; frame++) {
        if (!target.IsFrameReady(frame, out _, targetCategory)) return -1;
      }
      sampled++;
    }
    return sampled;
  }

  bool HasSeenAnimationCategory(string category) {
    if (string.IsNullOrWhiteSpace(category)) return false;
    return seenAnimationCategories.Contains(category);
  }

  void MarkAnimationCategorySeen(string category) {
    if (string.IsNullOrWhiteSpace(category)) return;
    seenAnimationCategories.Add(category);
  }

  private void SetAnimationCategory(string category) {
    var normalizedCategory = category ?? "";
    if (string.Equals(activeSpriteCategory, normalizedCategory, StringComparison.Ordinal)) return;
    LogAttackTrace("set_category", category: normalizedCategory, note: "targets=" + spriteTargets.Count);
    activeSpriteCategory = normalizedCategory;
    foreach (var target in spriteTargets) {
      if (target == null) continue;
      target.SetAnimation(normalizedCategory);
    }
    InvalidateSpriteFrameCache();
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
    if (frame == int.MinValue) return;
    if (frame == lastAppliedSpriteFrame) return;
    lastAppliedSpriteFrame = frame;
    foreach (var target in spriteTargets) {
      if (!IsSpriteTargetEnabled(target)) continue;
      target.UpdateSpriteAndNormal(frame);
    }
  }

  void BeginActivePunchTrace(AnimData anim, string category) {
    if (anim == null || string.IsNullOrWhiteSpace(currentAnimation)) {
      ResetActivePunchTraceState();
      return;
    }
    if (!PunchLeftTraceGate.ShouldTraceAnimation(currentAnimation, category)) {
      ResetActivePunchTraceState();
      return;
    }
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    activePunchTrace = true;
    activePunchTraceAnimation = currentAnimation;
    activePunchTraceCategory = category ?? "";
    activePunchTraceStartRealtime = Time.realtimeSinceStartup;
    activePunchTracePreviousFrame = currentFrame;
    activePunchTraceAdvancedFrames = 0;
    activePunchTraceSkippedFrames = 0;
    PunchLeftTraceGate.LogAnimationStart(
      currentAnimation,
      category,
      anim.start,
      anim.end,
      queuedAnimation,
      queue.queuedCount,
      queue.inFlightCount
    );
  }

  void TraceActivePunchFrameStep(int targetFrame, float deltaMs) {
    if (!activePunchTrace) return;
    if (activePunchTracePreviousFrame == int.MinValue) {
      activePunchTracePreviousFrame = targetFrame;
      return;
    }
    var fromFrame = activePunchTracePreviousFrame;
    var toFrame = targetFrame;
    var delta = toFrame - fromFrame;
    if (delta > 0) {
      activePunchTraceAdvancedFrames += delta;
      if (delta > 1) {
        activePunchTraceSkippedFrames += delta - 1;
      }
    }
    PunchLeftTraceGate.LogFrameAdvance(
      activePunchTraceAnimation,
      activePunchTraceCategory,
      fromFrame,
      toFrame,
      deltaMs
    );
    activePunchTracePreviousFrame = toFrame;
  }

  void EndActivePunchTrace(string reason, int finalFrame) {
    if (!activePunchTrace) return;
    var elapsedMs = Mathf.Max((Time.realtimeSinceStartup - activePunchTraceStartRealtime) * 1000f, 0f);
    PunchLeftTraceGate.LogAnimationEnd(
      activePunchTraceAnimation,
      activePunchTraceCategory,
      finalFrame,
      reason,
      elapsedMs,
      activePunchTraceAdvancedFrames,
      activePunchTraceSkippedFrames
    );
    ResetActivePunchTraceState();
  }

  void ResetActivePunchTraceState() {
    activePunchTrace = false;
    activePunchTraceAnimation = null;
    activePunchTraceCategory = null;
    activePunchTraceStartRealtime = 0f;
    activePunchTracePreviousFrame = int.MinValue;
    activePunchTraceAdvancedFrames = 0;
    activePunchTraceSkippedFrames = 0;
  }

  void LogAttackTrace(
    string stage,
    string requestedAnimation = null,
    string resolvedAnimation = null,
    string queuedAnimationName = null,
    string category = null,
    string note = null
  ) {
    if (!ShouldLogAttackTrace(requestedAnimation, resolvedAnimation, queuedAnimationName, category)) return;
    Debug.Log(
      "[AnimationController][AttackTrace] stage='" + (stage ?? "") +
      "' requested='" + (requestedAnimation ?? "") +
      "' resolved='" + (resolvedAnimation ?? "") +
      "' queued='" + (queuedAnimationName ?? "") +
      "' category='" + (category ?? "") +
      "' current='" + (currentAnimation ?? "") +
      "' pending='" + (pendingAnimation ?? "") +
      "' has_pending=" + (hasPendingAnimationSwitch ? 1 : 0) +
      " playing=" + (isPlaying ? 1 : 0) +
      " frame=" + currentFrame +
      " note='" + (note ?? "") + "'"
    );
  }

  bool ShouldLogAttackTrace(
    string requestedAnimation,
    string resolvedAnimation,
    string queuedAnimationName,
    string category
  ) {
    if (!EnableAttackTraceLogs) return false;
    return IsPunchLeftTraceToken(requestedAnimation) ||
           IsPunchLeftTraceToken(resolvedAnimation) ||
           IsPunchLeftTraceToken(queuedAnimationName) ||
           IsPunchLeftTraceToken(category) ||
           IsPunchLeftTraceToken(currentAnimation) ||
           IsPunchLeftTraceToken(pendingAnimation) ||
           IsPunchLeftTraceToken(queuedAnimation);
  }

  static bool IsPunchLeftTraceToken(string value) {
    if (string.IsNullOrWhiteSpace(value)) return false;
    return value.IndexOf("PunchLeft", StringComparison.OrdinalIgnoreCase) >= 0;
  }

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
      spriteTargetScanBuffer.Clear();
      rootTransform.GetComponentsInChildren(true, spriteTargetScanBuffer);
      for (var i = 0; i < spriteTargetScanBuffer.Count; i++) {
        var sprite = spriteTargetScanBuffer[i];
        if (sprite != null) {
          spriteTargets.Add(sprite);
          if (IsCriticalSpriteTarget(sprite)) criticalSpriteTargets.Add(sprite);
          spriteTargetRenderers[sprite] = sprite.GetComponent<SpriteRenderer>();
        }
      }
      spriteTargetScanBuffer.Clear();
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

  SpriteRenderer ResolveSpriteTargetRenderer(SpriteWithNormals target) {
    if (target == null) return null;
    if (!spriteTargetRenderers.TryGetValue(target, out var renderer) || renderer == null) {
      renderer = target.GetComponent<SpriteRenderer>();
      spriteTargetRenderers[target] = renderer;
    }
    return renderer;
  }

  bool IsSpriteTargetEnabled(SpriteWithNormals target) {
    if (target == null || !target.isActiveAndEnabled || target.DoNotRender) return false;
    var renderer = ResolveSpriteTargetRenderer(target);
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
