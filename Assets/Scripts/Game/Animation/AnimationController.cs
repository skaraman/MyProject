#pragma warning disable CS0162 // Unreachable code detected
using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// Generic animation driver (non-MonoBehaviour). Host behaviours must call Tick/Cleanup and wire data/targets.
/// </summary>
public partial class AnimationController {
  static readonly ProfilerMarker TickProfilerMarker = new ProfilerMarker("AnimationController.Tick");
  static readonly ProfilerMarker TargetScanProfilerMarker = new ProfilerMarker("AnimationController.TargetScan");
  static readonly ProfilerMarker PinRefreshProfilerMarker = new ProfilerMarker("AnimationController.PinRefresh");
  static readonly ProfilerMarker PinCollectProfilerMarker = new ProfilerMarker("AnimationController.PinCollect");
  static readonly ProfilerMarker PinCacheUpdateProfilerMarker = new ProfilerMarker("AnimationController.PinCacheUpdate");
  static readonly ProfilerMarker AdvanceProfilerMarker = new ProfilerMarker("AnimationController.Advance");
  static readonly ProfilerMarker SpriteApplyProfilerMarker = new ProfilerMarker("AnimationController.SpriteApply");
  static readonly ProfilerMarker SwitchCategoryProfilerMarker = new ProfilerMarker("AnimationController.SwitchCategory");
  static readonly ProfilerMarker SwitchBounceProfilerMarker = new ProfilerMarker("AnimationController.SwitchBounce");
  static readonly ProfilerMarker SwitchEventsProfilerMarker = new ProfilerMarker("AnimationController.SwitchEvents");
  static readonly Dictionary<string, AnimData> EmptyAnimationData = new();
  static readonly Dictionary<string, Dictionary<string, string>> EmptyInterruptData = new();
  static readonly Dictionary<string, Dictionary<string, List<BounceFrame>>> EmptyBounceData = new();
  static readonly Dictionary<string, Dictionary<string, List<HBox>>> EmptyHBoxData = new();

  const bool EnableAttackTraceLogs = false;
  const bool SuppressRuntimeWarningLogsForPerfPass = true;
  const int MaxTargetsForGateReadinessChecks = 96;
  const int FirstPlayGateSampleTargetCount = 24;
  // Increased from 8 to 16 to ensure more initial frames are requested immediately on switch, reducing first-frame blanks.
  const int PrimeImmediateStartFrameBudget = 16;
  const int MinAppearancePinAddressBudget = 32;
  const int LoadingOverlayPinAddressCap = 640;
  const int TransitionPrimeWindowFrames = 4;
  const int FirstPlayTransitionPrimeMaxFrames = 16;

  public string defaultAnimation = "Idle";
  public bool playOnStart;
  public bool SlowDown { get; set; }
  public bool ForceLoop { get; set; }
  public float AttackSpeedSeconds { get; set; }

  public string CurrentAnimation => currentAnimation;
  public bool IsPlaying =>
    isPlaying ||
    hasPendingAnimationSwitch ||
    holdCurrentAnimationOnStartFrameUntilReady;
  public bool IsFacingRight => isFacingRight;
  public Action<string> OnEffectTriggered;
  public Action<string> OnProjectileTriggered;

  private Transform rootTransform;
  private Vector3 baseScale = Vector3.one;

  private Dictionary<string, AnimData> animationData = EmptyAnimationData;
  private Dictionary<string, Dictionary<string, string>> interruptData = EmptyInterruptData;
  private Dictionary<string, Dictionary<string, List<BounceFrame>>> bounceData = EmptyBounceData;
  private Dictionary<string, Dictionary<string, List<HBox>>> hBoxData = EmptyHBoxData;

  private GameObject[] spriteObjects = Array.Empty<GameObject>();
  private GameObject[] bounceObjects = Array.Empty<GameObject>();
  private GameObject[] hBoxObjects = Array.Empty<GameObject>();
  private readonly List<SpriteWithNormals> spriteTargets = new();
  private readonly List<SpriteWithNormals> criticalSpriteTargets = new();
  private readonly List<SpriteWithNormals> spriteTargetScanBuffer = new(32);
  private readonly Dictionary<SpriteWithNormals, SpriteRenderer> spriteTargetRenderers = new();
  private readonly HashSet<SpriteWithNormals> startupVisualHoldSuppressedTargets = new();
  private readonly Dictionary<string, string> animationKeyLookup = new(64, StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, GameObject> bounceObjectByName = new(8, StringComparer.Ordinal);
  private readonly Dictionary<string, List<GameObject>> hBoxObjectsByName = new(8, StringComparer.Ordinal);
  private readonly Dictionary<GameObject, PolygonCollider2D> hBoxColliders = new(8);
  private readonly Dictionary<GameObject, HitBox2D> hBoxHitBoxes = new(8);
  private readonly HashSet<string> seenAnimationCategories = new(StringComparer.Ordinal);
  private readonly Dictionary<GameObject, List<int>> activeTweens = new(16);
  private readonly Dictionary<GameObject, BounceTweenState> bounceTweenStates = new(8);
  private readonly Dictionary<GameObject, HBoxTweenState> hBoxTweenStates = new(8);

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
  private int lastAppliedSpriteContentVersion = int.MinValue;
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
  private bool appearancePinsExternallyManaged;
  private string externalAppearancePinOwnerId = "";
  private int externalAppearancePinCount;
  private int externalAppearanceContentVersion = -1;
  private readonly List<string> appearancePinAddressBuffer = new(512);
  private readonly HashSet<string> appearancePinAddressSet = new(StringComparer.OrdinalIgnoreCase);
  private readonly HashSet<string> appliedAppearancePinAddressSet = new(StringComparer.OrdinalIgnoreCase);
  private readonly List<string> warmPlaybackAnimationScratch = new(16);
  private readonly HashSet<string> warmPlaybackAnimationSeenScratch = new(StringComparer.Ordinal);
  private readonly List<string> warmPlaybackAddressScratch = new(512);
  private readonly HashSet<string> warmPlaybackSeenAddressScratch = new(StringComparer.OrdinalIgnoreCase);
  private bool pinSnapshotStreamingEnabled;
  private string pinSnapshotCurrentAnimation;
  private bool pinSnapshotHasPendingSwitch;
  private string pinSnapshotPendingAnimation;
  private string pinSnapshotPendingCategory;
  private string pinSnapshotPendingQueuedAnimation;
  private int pinSnapshotPendingStartFrame = int.MinValue;
  private int pinSnapshotPendingReadyEndFrame = int.MinValue;
  private string pinSnapshotQueuedAnimation;
  private int pinSnapshotContentReloadVersion = int.MinValue;

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
    appearancePinAddressBuffer.Clear();
    appearancePinAddressSet.Clear();
    appliedAppearancePinAddressSet.Clear();
    activeSpriteCategory = null;
    lastAppliedSpriteContentVersion = ActiveContentRegistryRuntime.ReloadVersion;
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
    animationData = animations ?? EmptyAnimationData;
    interruptData = interrupts ?? EmptyInterruptData;
    bounceData = bounces ?? EmptyBounceData;
    hBoxData = hboxes ?? EmptyHBoxData;
    animationKeyLookup.Clear();
    foreach (var pair in animationData) {
      var key = pair.Key;
      if (string.IsNullOrWhiteSpace(key)) continue;
      animationKeyLookup[key] = key;
    }
    seenAnimationCategories.Clear();
    PrepareHBoxTweenStates();
    ClearStartFrameHold();
    InvalidateSpriteFrameCache();
    InvalidateAppearancePinSnapshot();
  }

  public void SetSpriteObjects(IEnumerable<GameObject> targets) {
    spriteObjects = ResolveTargetArray(targets);
    CacheSpriteTargets();
    activeSpriteCategory = null;
    InvalidateSpriteFrameCache();
    InvalidateAppearancePinSnapshot();
  }

  public void SetBounceObjects(IEnumerable<GameObject> targets) {
    bounceObjects = ResolveTargetArray(targets);
    RebuildBounceObjectLookup();
    PrepareBounceTweenStates();
  }

  public void SetHBoxObjects(IEnumerable<GameObject> targets) {
    hBoxObjects = ResolveTargetArray(targets);
    RebuildHBoxObjectLookup();
    PrepareHBoxTweenStates();
  }

  static GameObject[] ResolveTargetArray(IEnumerable<GameObject> targets) {
    if (targets == null) return Array.Empty<GameObject>();
    if (targets is GameObject[] array) return array;
    if (targets is ICollection<GameObject> collection) {
      if (collection.Count <= 0) return Array.Empty<GameObject>();
      var result = new GameObject[collection.Count];
      collection.CopyTo(result, 0);
      return result;
    }

    var list = new List<GameObject>();
    foreach (var target in targets) {
      list.Add(target);
    }
    return list.Count > 0 ? list.ToArray() : Array.Empty<GameObject>();
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
    foreach (var pair in hBoxObjectsByName) {
      pair.Value.Clear();
    }
    hBoxColliders.Clear();
    hBoxHitBoxes.Clear();
    if (hBoxObjects == null || hBoxObjects.Length == 0) return;
    for (var i = 0; i < hBoxObjects.Length; i++) {
      var go = hBoxObjects[i];
      if (go == null || string.IsNullOrWhiteSpace(go.name)) continue;
      if (!hBoxObjectsByName.TryGetValue(go.name, out var list)) {
        list = new List<GameObject>(1);
        hBoxObjectsByName[go.name] = list;
      }
      list.Add(go);
      if (go.TryGetComponent<PolygonCollider2D>(out var collider)) {
        hBoxColliders[go] = collider;
      }
      if (go.TryGetComponent<HitBox2D>(out var hitBox)) {
        hBoxHitBoxes[go] = hitBox;
      }
    }
  }

  public void Tick(float deltaTime) {
    TickProfilerMarker.Begin();
    TextureResidencyCache.PumpOncePerFrame();
    RefreshSpriteTargetsAfterContentReload();

    TargetScanProfilerMarker.Begin();
    var hasEnabledSpriteTargets = HasEnabledSpriteTargets();
    TargetScanProfilerMarker.End();
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
      PinRefreshProfilerMarker.Begin();
      RefreshAppearancePins();
      PinRefreshProfilerMarker.End();
    }

    AdvanceProfilerMarker.Begin();
    AdvanceAnimation(deltaTime);
    AdvanceProfilerMarker.End();
    TickProfilerMarker.End();
  }

  void RefreshSpriteTargetsAfterContentReload() {
    var contentVersion = ActiveContentRegistryRuntime.ReloadVersion;
    if (lastAppliedSpriteContentVersion == contentVersion) return;

    lastAppliedSpriteContentVersion = contentVersion;
    InvalidateSpriteFrameCache();
    for (var i = 0; i < spriteTargets.Count; i++) {
      var target = spriteTargets[i];
      if (target == null) continue;
      target.ForceUpdateSpriteAndNormal(currentFrame);
    }
  }

  public bool PlayAnimation(string animationName, bool forceRestart = false, bool resolveInterrupts = true) {
    if (!TryGetAnimationKey(animationName, out var requestedAnimation)) {
      if (EnableAttackTraceLogs) LogAttackTrace("missing_requested", requestedAnimation: animationName, note: "TryGetAnimationKey failed");
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
    if (EnableAttackTraceLogs) {
      LogAttackTrace(
        "play_request",
        requestedAnimation: requestedAnimation,
        resolvedAnimation: resolvedAnimation,
        queuedAnimationName: queued,
        note: "forceRestart=" + (forceRestart ? 1 : 0) + " resolveInterrupts=" + (resolveInterrupts ? 1 : 0)
      );
    }

    // Prevent movement-loop requests from stomping pending action switches (e.g. attacks)
    // while readiness gating is still warming required frames.
    if (hasPendingAnimationSwitch &&
        IsLocomotionAnimation(resolvedAnimation) &&
        !IsLocomotionAnimation(pendingAnimation)) {
      if (EnableAttackTraceLogs) LogAttackTrace("skip_locomotion_during_pending", requestedAnimation: requestedAnimation, resolvedAnimation: resolvedAnimation, queuedAnimationName: queued);
      return true;
    }

    if (string.IsNullOrWhiteSpace(resolvedAnimation) || !animationData.ContainsKey(resolvedAnimation)) {
      if (EnableAttackTraceLogs) LogAttackTrace("missing_resolved", requestedAnimation: requestedAnimation, resolvedAnimation: resolvedAnimation, queuedAnimationName: queued);
      return false;
    }

    if (!forceRestart &&
        isPlaying &&
        string.Equals(resolvedAnimation, currentAnimation, StringComparison.Ordinal) &&
        !hasPendingAnimationSwitch) {
      if (EnableAttackTraceLogs) LogAttackTrace("skip_same_current", requestedAnimation: requestedAnimation, resolvedAnimation: resolvedAnimation, queuedAnimationName: queued);
      return true;
    }

    if (!forceRestart &&
        hasPendingAnimationSwitch &&
        string.Equals(resolvedAnimation, pendingAnimation, StringComparison.Ordinal)) {
      if (!string.IsNullOrWhiteSpace(queued)) {
        pendingQueuedAnimation = queued;
      }
      if (EnableAttackTraceLogs) LogAttackTrace("skip_same_pending", requestedAnimation: requestedAnimation, resolvedAnimation: resolvedAnimation, queuedAnimationName: queued);
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
    var shouldHoldRuntimePlayerStartFrame = ShouldHoldRuntimePlayerStartFrame(
      enabledTargetCount,
      category,
      anim
    );
    var shouldHoldEffectStartFrame = ShouldHoldEffectStartFrame(enabledTargetCount, category, anim);
    var shouldHoldStartFrame =
      shouldHoldInitialPlayerStartFrame ||
      shouldHoldRuntimePlayerStartFrame ||
      shouldHoldEffectStartFrame;

    if (gateMs <= 0) {
      // Global no-gate mode: commit immediately and let per-frame sync absorb streaming lag.
      if (isTransitionCategory) {
        PrimeTransitionAndQueuedWindows(category, anim, queued);
      }
      if (shouldHoldInitialPlayerStartFrame) {
        BeginStartupVisualHold();
      }
      if (EnableAttackTraceLogs) LogAttackTrace("commit_no_gate", requestedAnimation: requestedAnimation, resolvedAnimation: resolvedAnimation, queuedAnimationName: queued, category: category);
      ClearPendingAnimationSwitch();
      CommitAnimationSwitch(
        resolvedAnimation,
        queued,
        category,
        holdOnStartFrameUntilReady: shouldHoldStartFrame,
        deferInitialVisualApply: true
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
        if (EnableAttackTraceLogs) LogAttackTrace("begin_pending_gate", requestedAnimation: requestedAnimation, resolvedAnimation: resolvedAnimation, queuedAnimationName: queued, category: category);
        BeginPendingAnimationSwitch(resolvedAnimation, queued, category, anim.start, readinessEndFrame, gateMs);
        return true;
      }
      SpriteStreamingDiagnostics.RecordAnimationSwitchWait(0f, false);
    }
    else {
      ClearPendingAnimationSwitch();
    }

    if (EnableAttackTraceLogs) LogAttackTrace("commit_final", requestedAnimation: requestedAnimation, resolvedAnimation: resolvedAnimation, queuedAnimationName: queued, category: category);
    CommitAnimationSwitch(
      resolvedAnimation,
      queued,
      category,
      holdOnStartFrameUntilReady: shouldHoldEffectStartFrame
    );
    return true;
  }

  public void ForceAnimation(string animationName = null) {
    string anim = animationName ?? (!string.IsNullOrEmpty(CurrentAnimation) ? CurrentAnimation : defaultAnimation);
    if (string.IsNullOrEmpty(anim)) return;
    PlayAnimation(anim, forceRestart: true, resolveInterrupts: false);
  }
}
