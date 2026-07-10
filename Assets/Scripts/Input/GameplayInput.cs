using System;
using System.Collections.Generic;
using UnityEngine;

public class GameplayInput : MonoBehaviour {
  static readonly bool ForceDisableDebugLogsForPerfPass = true;

  private enum MoveMode {
    Run,
    Sprint
  }

  private readonly List<Action> actions = new();
  public GameObject cam;
  private GameObject EsperanzaParent;
  private Rigidbody2D erb;
  private Rigidbody2D cameraRB;
  private GearController gearController;
  private CharacterState characterState;
  public GameObject formsWheel;

  [Header("Movement")]
  [SerializeField] private float sprintMultiplier = 2f;
  [SerializeField] private float sprintSustainSeconds = 2f;
  [SerializeField] private float sprintResumeWindowSeconds = 2f;
  [SerializeField] private float cameraFollowUnitsPerSecond = 60f;

  [Header("Stance")]
  [SerializeField] private float stanceDurationSeconds = 2f;
  [SerializeField] private float stanceMoveMultiplier = 0.15f;

  [Header("Jump")]
  [SerializeField] private float jumpHeight = 7f;
  [SerializeField] private float jumpDuration = 2f;
  [SerializeField] private float airAttackBoostHeight = 1f;
  [SerializeField] private float airAttackBoostHangSeconds = 0.15f;
  [SerializeField] private int maxAirAttackBoostsPerJump = 1;

  [Header("Dash")]
  [SerializeField] private float dashDistance = 3f;

  [Header("Attack Combos")]
  [SerializeField] private float superAttackWindowSeconds = 0.2f;
  [SerializeField] private bool logAttackGate;
  [SerializeField] private bool logAttackFlow = false;

  private float rawLeft;
  private float rawRight;
  private float rawUp;
  private float rawDown;
  private bool leftIgnoredUntilRelease;
  private bool rightIgnoredUntilRelease;
  private int activeHorizontalDir;

  private MoveMode moveMode = MoveMode.Run;
  private float sustainedMoveSeconds;
  private float timeSinceMoveStopSeconds;
  private bool wasInputMoving;
  private bool wasSprintingWhenStopped;

  private bool isJumping = false;
  private bool isGrounded = true;
  private float stanceTimeRemainingSeconds;
  private float jumpGroundLocalY;
  private Vector2 jumpMomentum;
  private int airAttackBoostsUsed;

  private struct PendingSuperAttack {
    public bool IsActive;
    public int FirstAttackIndex;
    public float FirstPressTime;
  }

  private PendingSuperAttack pendingSuperAttack1; // attack1 + attack2 -> superattack1
  private PendingSuperAttack pendingSuperAttack2; // attack3 + attack4 -> superattack2
  private int lastAttackGateLogFrame = -1;
  private float nextPlayerReferenceResolveAt = -1f;

  void OnEnable() {
    RegisterInputHandlers();
  }

  void Start() {
    TryResolvePlayerReferences(force: true);
  }

  void RegisterInputHandlers() {
    if (actions.Count > 0) return;
    actions.Add(MessageBus.On("gameplay.attack1", o => { if (_IsPressed(o)) attack1(); }));
    actions.Add(MessageBus.On("gameplay.attack2", o => { if (_IsPressed(o)) attack2(); }));
    actions.Add(MessageBus.On("gameplay.attack3", o => { if (_IsPressed(o)) attack3(); }));
    actions.Add(MessageBus.On("gameplay.attack4", o => { if (_IsPressed(o)) attack4(); }));
    actions.Add(MessageBus.On("gameplay.block", o => { if (_IsPressed(o)) block(); }));
    actions.Add(MessageBus.On("gameplay.dash", o => { if (_IsPressed(o)) dash(); }));
    actions.Add(MessageBus.On("gameplay.dodge", o => { if (_IsPressed(o)) dodge(); }));
    actions.Add(MessageBus.On("gameplay.jump", o => { if (_IsPressed(o)) Jump(); }));
    actions.Add(MessageBus.On("gameplay.pause", o => { if (_IsPressed(o)) pause(); }));
    actions.Add(MessageBus.On("gameplay.dance", o => { if (_IsPressed(o)) dance(); }));
    actions.Add(MessageBus.On("gameplay.wheel", o => { if (_IsPressed(o)) ToggleFormsWheel(); }));
    actions.Add(MessageBus.On("gameplay.charUp", o => charUp(o)));
    actions.Add(MessageBus.On("gameplay.charDown", o => charDown(o)));
    actions.Add(MessageBus.On("gameplay.charLeft", o => charLeft(o)));
    actions.Add(MessageBus.On("gameplay.charRight", o => charRight(o)));
  }

  bool _IsPressed(object o) {
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive) {
      Debug.Log("[GameplayInput] Input blocked during loading overlay.");
      return false;
    }
    return InputMessageValue.IsPressed(o);
  }

  float _ReadDirectionalValue(object value, bool horizontalAxis, bool positiveDirection) {
    if (value == null) return 0f;
    if (value is Vector2 v2) {
      float axis = horizontalAxis ? v2.x : v2.y;
      return Mathf.Clamp01(positiveDirection ? axis : -axis);
    }
    if (value is Vector3 v3) {
      float axis = horizontalAxis ? v3.x : v3.y;
      return Mathf.Clamp01(positiveDirection ? axis : -axis);
    }
    return Mathf.Clamp01(InputMessageValue.CoerceFloat(value));
  }

  void OnDisable() {
    for (int i = 0; i < actions.Count; i++) {
      actions[i]();
    }
    actions.Clear();
  }

  void Update() {
    TryResolvePlayerReferences();
    if (erb == null || gearController == null) return;
    _CameraFollow();
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive) {
      return;
    }
    var moveInput = _ResolveMoveInput();
    _UpdateRunSprint(moveInput);
    _TickStanceTimer();
    if (isJumping) {
      _ApplyJumpMomentum();
      return;
    }
    _ProcessMovementVelocity(moveInput);
    _RecoverToStanceWhenActionEnds();
    _ProcessMovementAnimation(moveInput);
  }

  public void ApplyPlayerBootstrap(GameObject playerRoot, GearController gear = null, CharacterState state = null) {
    EsperanzaParent = playerRoot;
    gearController = gear;
    characterState = state;
    RefreshPlayerReferencesFromRoot(playerRoot);
    nextPlayerReferenceResolveAt = -1f;
  }

  void TryResolvePlayerReferences(bool force = false) {
    if (!force && HasResolvedPlayerReferences()) {
      return;
    }

    var now = Time.unscaledTime;
    if (!force && nextPlayerReferenceResolveAt >= 0f && now < nextPlayerReferenceResolveAt) {
      return;
    }

    nextPlayerReferenceResolveAt = now + 0.25f;

    if (cameraRB == null && cam != null) {
      cameraRB = cam.GetComponent<Rigidbody2D>();
    }

    var playerRoot = ResolvePlayerRoot();
    if (!ReferenceEquals(EsperanzaParent, playerRoot)) {
      EsperanzaParent = playerRoot;
    }
    RefreshPlayerReferencesFromRoot(playerRoot);
  }

  bool HasResolvedPlayerReferences() {
    return (cam == null || cameraRB != null) &&
           IsLivePlayerRoot(EsperanzaParent) &&
           IsComponentOnPlayerRoot(gearController, EsperanzaParent) &&
           IsComponentOnPlayerRoot(characterState, EsperanzaParent) &&
           IsComponentOnPlayerRoot(erb, EsperanzaParent);
  }

  GameObject ResolvePlayerRoot() {
    if (IsLivePlayerRoot(EsperanzaParent)) {
      return EsperanzaParent;
    }

    EsperanzaParent = ResolveRootFromComponent(gearController);
    if (EsperanzaParent != null) {
      return EsperanzaParent;
    }

    EsperanzaParent = ResolveRootFromComponent(characterState);
    if (EsperanzaParent != null) {
      return EsperanzaParent;
    }

    EsperanzaParent = SingleSceneManager.ResolveGameplayPlayerRoot();
    return EsperanzaParent;
  }

  void RefreshPlayerReferencesFromRoot(GameObject playerRoot) {
    if (!IsLivePlayerRoot(playerRoot)) {
      gearController = null;
      characterState = null;
      erb = null;
      return;
    }

    if (!IsComponentOnPlayerRoot(gearController, playerRoot)) {
      gearController = playerRoot.GetComponent<GearController>();
    }
    if (!IsComponentOnPlayerRoot(characterState, playerRoot)) {
      characterState = playerRoot.GetComponent<CharacterState>();
    }
    if (!IsComponentOnPlayerRoot(erb, playerRoot)) {
      erb = playerRoot.GetComponent<Rigidbody2D>();
    }
  }

  static GameObject ResolveRootFromComponent(Component component) {
    if (component == null) {
      return null;
    }

    var candidate = component.gameObject;
    return IsLivePlayerRoot(candidate) ? candidate : null;
  }

  static bool IsComponentOnPlayerRoot(Component component, GameObject playerRoot) {
    return component != null &&
           playerRoot != null &&
           ReferenceEquals(component.gameObject, playerRoot);
  }

  static bool IsLivePlayerRoot(GameObject candidate) {
    return candidate != null &&
           candidate.scene.IsValid() &&
           (candidate.hideFlags & HideFlags.HideAndDontSave) == 0;
  }

  void attack1() { _HandleAttackPress(1); }
  void attack2() { _HandleAttackPress(2); }
  void attack3() { _HandleAttackPress(3); }
  void attack4() { _HandleAttackPress(4); }

  void block() {
    _TryPlayMappedAction("block", fallback: "Block");
  }

  void dash() {
    if (erb == null) return;
    float dir = gearController != null && gearController.IsFacingRight ? 1f : -1f;
    var pos = erb.transform.localPosition;
    pos.x += dashDistance * dir;
    erb.transform.localPosition = pos;
    _TryPlayMappedAction("dash", fallback: "Dodge");
  }

  void dodge() {
    _TryPlayMappedAction("dodge", fallback: "Dodge");
  }

  void Jump() {
    if (!isGrounded || gearController == null || erb == null) return;

    stanceTimeRemainingSeconds = 0f;
    _TryPlayMappedAction("jump", fallback: "Jump");
    isGrounded = false;
    isJumping = true;
    airAttackBoostsUsed = 0;
    jumpGroundLocalY = erb.transform.localPosition.y;
    jumpMomentum = new Vector2(erb.linearVelocity.x, 0f);

    float peakY = jumpGroundLocalY + jumpHeight;
    float halfDuration = Mathf.Max(0.01f, jumpDuration * 0.5f);
    CancelPlayerTweens();
    LeanTween.sequence()
      .append(RegisterPlayerTween(LeanTween.moveLocalY(erb.gameObject, peakY, halfDuration).setEaseOutQuad(), halfDuration))
      .append(RegisterPlayerTween(LeanTween.moveLocalY(erb.gameObject, jumpGroundLocalY, halfDuration).setEaseInQuad(), halfDuration))
      .append(() => {
        isGrounded = true;
        isJumping = false;
        HandleLanding();
      });
  }

  void HandleLanding() {
    if (erb != null) {
      erb.linearVelocity = Vector2.zero;
    }
    _EnterStance();
    _TryPlayLocomotion("JumpLanding");
  }

  void dance() {
    if (gearController == null) return;
    gearController.PlayAnimation("Dance");
  }

  void charUp(object value) {
    rawUp = _ReadDirectionalValue(value, horizontalAxis: false, positiveDirection: true);
  }

  void charDown(object value) {
    rawDown = _ReadDirectionalValue(value, horizontalAxis: false, positiveDirection: false);
  }

  void charLeft(object value) {
    float newValue = _ReadDirectionalValue(value, horizontalAxis: true, positiveDirection: false);
    bool wasPressed = rawLeft > 0f;
    rawLeft = newValue;

    if (rawLeft <= 0f) {
      leftIgnoredUntilRelease = false;
      if (activeHorizontalDir == -1) activeHorizontalDir = 0;
      return;
    }

    if (leftIgnoredUntilRelease) return;
    if (wasPressed) return;

    activeHorizontalDir = -1;
    if (rawRight > 0f) rightIgnoredUntilRelease = true;
  }

  void charRight(object value) {
    float newValue = _ReadDirectionalValue(value, horizontalAxis: true, positiveDirection: true);
    bool wasPressed = rawRight > 0f;
    rawRight = newValue;

    if (rawRight <= 0f) {
      rightIgnoredUntilRelease = false;
      if (activeHorizontalDir == 1) activeHorizontalDir = 0;
      return;
    }

    if (rightIgnoredUntilRelease) return;
    if (wasPressed) return;

    activeHorizontalDir = 1;
    if (rawLeft > 0f) leftIgnoredUntilRelease = true;
  }

  void pause() {
    MessageBus.Send("openPauseMenu", null);
  }

  void ToggleFormsWheel() {
    if (formsWheel == null) return;
    formsWheel.SetActive(!formsWheel.activeSelf);
  }

  void _CameraFollow() {
    if (erb == null || cameraRB == null) return;

    Vector3 targetPos = erb.transform.localPosition;
    Vector3 currentPos = cameraRB.transform.localPosition;
    var followSpeed = Mathf.Max(cameraFollowUnitsPerSecond, 0f);
    var followStep = followSpeed * Mathf.Max(GetSceneDeltaTime(), 0f);
    Vector3 newPos = Vector3.MoveTowards(currentPos, targetPos, followStep);
    cameraRB.transform.localPosition = newPos;
  }

  void _ProcessMovementVelocity(Vector2 moveInput) {
    if (gearController == null) return;
    if (gearController.CurrentAnimation == "Dance") return;

    float speed = (10 + AllStatValues.Esperanza["MVSP"]);
    float moveMultiplier = stanceTimeRemainingSeconds > 0f ? stanceMoveMultiplier : (moveMode == MoveMode.Sprint ? sprintMultiplier : 1f);
    var sceneFactor = GetSceneTimeFactor();

    erb.linearVelocityX = moveInput.x * speed * moveMultiplier * sceneFactor;
    erb.linearVelocityY = moveInput.y * speed * moveMultiplier * sceneFactor;

    if (stanceTimeRemainingSeconds > 0f) return;

    if (gearController.Controller != null) {
      gearController.Controller.SetFacingDirection(moveInput.x);
    }
    else if ((moveInput.x < 0f && gearController.IsFacingRight) ||
    (moveInput.x > 0f && !gearController.IsFacingRight)) {
      gearController.needsFlip = true;
    }
  }

  void _ProcessMovementAnimation(Vector2 moveInput) {
    if (gearController == null) return;
    var current = gearController.CurrentAnimation;
    var isPlaying = gearController.Controller != null && gearController.Controller.IsPlaying;
    var isLocomotion = _IsLocomotionAnimation(current);
    if (!isLocomotion && isPlaying) return;

    if (stanceTimeRemainingSeconds > 0f) {
      _TryPlayLocomotion("Stance");
      return;
    }

    var input = Mathf.Abs(moveInput.x) + Mathf.Abs(moveInput.y);
    if (input <= 0f) {
      _TryPlayLocomotion("Breathe");
      return;
    }

    _TryPlayLocomotion(moveMode == MoveMode.Sprint ? "Sprint" : "Run");
  }

  Vector2 _ResolveMoveInput() {
    float x = 0f;
    if (activeHorizontalDir == -1) {
      if (!leftIgnoredUntilRelease && rawLeft > 0f) x = -rawLeft;
    }
    else if (activeHorizontalDir == 1) {
      if (!rightIgnoredUntilRelease && rawRight > 0f) x = rawRight;
    }
    float y = rawUp - rawDown;
    return new Vector2(x, y);
  }

  void _UpdateRunSprint(Vector2 moveInput) {
    bool inputMoving = Mathf.Abs(moveInput.x) + Mathf.Abs(moveInput.y) > 0f;

    if (inputMoving) {
      if (!wasInputMoving) {
        if (wasSprintingWhenStopped && timeSinceMoveStopSeconds < sprintResumeWindowSeconds) {
          moveMode = MoveMode.Sprint;
          sustainedMoveSeconds = sprintSustainSeconds;
        }
        else {
          moveMode = MoveMode.Run;
          sustainedMoveSeconds = 0f;
        }
      }

      if (moveMode == MoveMode.Run) {
        sustainedMoveSeconds += GetSceneDeltaTime();
        if (sustainedMoveSeconds >= sprintSustainSeconds) {
          moveMode = MoveMode.Sprint;
        }
      }

      timeSinceMoveStopSeconds = 0f;
    }
    else {
      sustainedMoveSeconds = 0f;
      if (wasInputMoving) {
        timeSinceMoveStopSeconds = 0f;
        wasSprintingWhenStopped = moveMode == MoveMode.Sprint;
      }
      else {
        timeSinceMoveStopSeconds += GetSceneDeltaTime();
        if (timeSinceMoveStopSeconds >= sprintResumeWindowSeconds) {
          wasSprintingWhenStopped = false;
          moveMode = MoveMode.Run;
        }
      }
    }

    wasInputMoving = inputMoving;
  }

  void _TickStanceTimer() {
    if (stanceTimeRemainingSeconds <= 0f) return;
    stanceTimeRemainingSeconds = Mathf.Max(0f, stanceTimeRemainingSeconds - GetSceneDeltaTime());
  }

  void _EnterStance(bool resetTimer = true) {
    if (resetTimer) stanceTimeRemainingSeconds = stanceDurationSeconds;
    moveMode = MoveMode.Run;
    sustainedMoveSeconds = 0f;
    wasSprintingWhenStopped = false;
    timeSinceMoveStopSeconds = sprintResumeWindowSeconds;
  }

  void _ApplyJumpMomentum() {
    if (erb == null) return;
    erb.linearVelocity = jumpMomentum * GetSceneTimeFactor();
  }

  void _RecoverToStanceWhenActionEnds() {
    if (gearController == null || gearController.Controller == null) return;
    if (gearController.Controller.IsPlaying) return;
    var current = gearController.CurrentAnimation;
    if (string.IsNullOrEmpty(current)) return;
    if (_IsLocomotionAnimation(current)) return;

    if (stanceTimeRemainingSeconds <= 0f) {
      _EnterStance(resetTimer: true);
    }
    _TryPlayLocomotion("Stance");
  }

  bool _TryPlayMappedAction(string actionKey, string fallback, bool forceRestart = false) {
    if (gearController == null) return false;
    var anim = _ResolveMappedAnimation(actionKey, fallback);
    return _TryPlayResolvedAnimation(anim, forceRestart, resolveInterrupts: true);
  }

  string _ResolveMappedAnimation(string actionKey, string fallback) {
    var anim = _GetMappedAnimation(actionKey) ?? fallback;
    if (string.IsNullOrEmpty(anim)) return null;
    if (!Animations.Esperanza.ContainsKey(anim)) anim = fallback;
    if (string.IsNullOrEmpty(anim) || !Animations.Esperanza.ContainsKey(anim)) return null;
    return anim;
  }

  bool _TryPlayResolvedAnimation(string anim, bool forceRestart = false, bool resolveInterrupts = true) {
    if (gearController == null || string.IsNullOrEmpty(anim)) return false;
    if (gearController.Controller != null) return gearController.Controller.PlayAnimation(anim, forceRestart, resolveInterrupts);
    gearController.PlayAnimation(anim, forceRestart, resolveInterrupts);
    return true;
  }

  string _GetMappedAnimation(string actionKey) {
    if (string.IsNullOrEmpty(actionKey)) return null;
    if (!AttacksMapToForms.all.TryGetValue(EsperanzaForms.GetActive(), out var map)) return null;
    if (!map.TryGetValue(actionKey, out var anim)) return null;
    return anim;
  }

  void _TryPlayLocomotion(string anim) {
    _TryPlayResolvedAnimation(anim, forceRestart: false, resolveInterrupts: true);
  }

  void _HandleAttackPress(int index) {
    float now = GetSceneNow();
    var actionKey = index == 1 ? "attack1"
      : index == 2 ? "attack2"
      : index == 3 ? "attack3"
      : index == 4 ? "attack4"
      : "";
    _LogAttackFlow("press", actionKey: actionKey, note: "index=" + index);

    if (index == 1 || index == 2) {
      _HandleSuperPairAttack(ref pendingSuperAttack1, index, now, "superattack1");
    }
    else if (index == 3 || index == 4) {
      _HandleSuperPairAttack(ref pendingSuperAttack2, index, now, "superattack2");
    }
  }

  void _HandleSuperPairAttack(ref PendingSuperAttack pending, int pressedIndex, float now, string superActionKey) {
    bool hasPending = pending.IsActive;
    bool pendingStillValid = hasPending && (now - pending.FirstPressTime) <= superAttackWindowSeconds;
    bool isCompletingPair = pendingStillValid && pending.FirstAttackIndex != pressedIndex;

    if (isCompletingPair) {
      pending.IsActive = false;
      _ExecuteSuperAttackAction(superActionKey, pressedIndex);
      return;
    }

    if (hasPending && !pendingStillValid) {
      pending.IsActive = false;
    }

    pending.IsActive = true;
    pending.FirstAttackIndex = pressedIndex;
    pending.FirstPressTime = now;
    _ExecuteAttackAction(pressedIndex == 1 ? "attack1"
      : pressedIndex == 2 ? "attack2"
      : pressedIndex == 3 ? "attack3"
      : pressedIndex == 4 ? "attack4"
      : null);
  }

  void _ExecuteAttackAction(string actionKey) {
    if (string.IsNullOrEmpty(actionKey)) return;
    _LogAttackFlow("execute_start", actionKey: actionKey);
    var mappedAttackAnimation = _ResolveMappedAnimation(actionKey, fallback: null);
    if (string.IsNullOrWhiteSpace(mappedAttackAnimation)) {
      _LogAttackFlow("missing_map", actionKey: actionKey, note: "resolve returned null/empty");
      _LogAttackGate("missing_map", gearController != null ? gearController.CurrentAnimation : "", actionKey);
      _LogPostRevealAttackProbe("missing_map", actionKey, mappedAttackAnimation, hasPlayedResult: true, played: false);
      return;
    }
    _LogPostRevealAttackProbe("execute_start", actionKey, mappedAttackAnimation, hasPlayedResult: false, played: false);
    PunchLeftTraceGate.OpenFromClick(
      actionKey,
      mappedAttackAnimation,
      gearController != null ? gearController.CurrentAnimation : ""
    );
    if (_IsBlockingActionPlaybackActive(mappedAttackAnimation)) {
      _LogPostRevealAttackProbe("gate_blocked", actionKey, mappedAttackAnimation, hasPlayedResult: true, played: false);
      return;
    }

    if (isJumping) {
      _TryBoostJumpForAirAttack();
    }

    _EnterStance(resetTimer: true);
    _LogAttackFlow("play_request", actionKey: actionKey, mappedAnimation: mappedAttackAnimation, note: "resolveInterrupts=0");
    // One click should play the full attack clip; skip interrupt transition categories here.
    var played = _TryPlayResolvedAnimation(mappedAttackAnimation, forceRestart: false, resolveInterrupts: false);
    PunchLeftTraceGate.LogClickDispatchResult(
      actionKey,
      mappedAttackAnimation,
      played,
      gearController != null ? gearController.CurrentAnimation : ""
    );
    _LogPostRevealAttackProbe("play_result", actionKey, mappedAttackAnimation, hasPlayedResult: true, played: played);
    _LogAttackFlow("play_result", actionKey: actionKey, mappedAnimation: mappedAttackAnimation, note: "played=" + (played ? 1 : 0));
    if (!played) {
      _LogAttackGate("play_rejected", gearController != null ? gearController.CurrentAnimation : "", mappedAttackAnimation);
    }
  }

  void _LogPostRevealAttackProbe(
    string stage,
    string actionKey,
    string mappedAnimation,
    bool hasPlayedResult,
    bool played
  ) {
    if (!SingleSceneManager.ShouldLogGameplayPostRevealInputTrace()) return;
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var deferred = TextureResidencyCache.GetDeferredSnapshot();
    var controller = gearController != null ? gearController.Controller : null;
    var current = gearController != null ? gearController.CurrentAnimation : "";
    var resolvedAnimation = "-";
    var category = "-";
    var startFrame = -1;
    var readinessEndFrame = -1;
    var enabledTargetCount = -1;
    var firstFrameReady = false;
    var readinessWindowReady = false;
    var hasReadiness = controller != null &&
                       controller.TryGetAnimationReadinessForDiagnostics(
                         mappedAnimation,
                         out resolvedAnimation,
                         out category,
                         out startFrame,
                         out readinessEndFrame,
                         out enabledTargetCount,
                         out firstFrameReady,
                         out readinessWindowReady
                       );
    Debug.Log(
      "[GameplayInput][PostRevealAttack] stage='" + (stage ?? "") +
      "' action='" + (actionKey ?? "") +
      "' mapped='" + (mappedAnimation ?? "") +
      "' resolved='" + (hasReadiness ? resolvedAnimation : "-") +
      "' category='" + (hasReadiness ? category : "-") +
      "' current='" + (current ?? "") +
      "' played=" + (hasPlayedResult ? (played ? 1 : 0) : -1) +
      " overlay_active=" + (SpriteStreamingLoadingState.IsLoadingOverlayActive ? 1 : 0) +
      " overlay_protected=" + (SpriteStreamingLoadingState.IsProtectedLoadingOverlayActive ? 1 : 0) +
      " reveal_age_s=" + SingleSceneManager.GameplayRevealInputTraceAgeSeconds.ToString("0.000") +
      " active_input_map='" + SingleSceneManager.ActiveInputMap +
      "' reveal_critical_ready=" + (SingleSceneManager.IsCriticalScopeReadyForRevealStatic() ? 1 : 0) +
      " controller_present=" + (controller != null ? 1 : 0) +
      " controller_playing=" + (controller != null && controller.IsPlaying ? 1 : 0) +
      " start_frame=" + (hasReadiness ? startFrame : -1) +
      " readiness_end_frame=" + (hasReadiness ? readinessEndFrame : -1) +
      " enabled_targets=" + (hasReadiness ? enabledTargetCount : -1) +
      " first_frame_ready=" + (hasReadiness && firstFrameReady ? 1 : 0) +
      " readiness_window_ready=" + (hasReadiness && readinessWindowReady ? 1 : 0) +
      " queue_queued=" + queue.queuedCount +
      " queue_in_flight=" + queue.inFlightCount +
      " deferred_pending=" + deferred.pendingCount +
      " resolver_idle=" + (SpriteRuntimeResolver.IsWarmupIdle() ? 1 : 0)
    );
  }

  void _ExecuteSuperAttackAction(string superActionKey, int fallbackAttackIndex) {
    if (string.IsNullOrEmpty(superActionKey)) return;

    if (isJumping) {
      _TryBoostJumpForAirAttack();
    }

    _EnterStance(resetTimer: true);

    bool playedSuper = _TryPlayMappedAction(superActionKey, fallback: null, forceRestart: true);
    if (!playedSuper) {
      string fallbackActionKey = fallbackAttackIndex == 1 ? "attack1"
        : fallbackAttackIndex == 2 ? "attack2"
        : fallbackAttackIndex == 3 ? "attack3"
        : fallbackAttackIndex == 4 ? "attack4"
        : null;
      if (!string.IsNullOrEmpty(fallbackActionKey)) {
        _TryPlayMappedAction(fallbackActionKey, fallback: null);
      }
    }

    MessageBus.Send($"gameplay.{superActionKey}", null);
  }

  bool _IsBlockingActionPlaybackActive(string requestedAnimation) {
    if (gearController == null || gearController.Controller == null) return false;
    if (!gearController.Controller.IsPlaying) return false;

    var current = gearController.CurrentAnimation;
    if (string.IsNullOrWhiteSpace(current)) return false;
    if (_IsLocomotionAnimation(current)) return false;
    if (_IsTransitionAnimation(current)) return false;

    if (string.Equals(current, requestedAnimation, StringComparison.Ordinal)) {
      _LogAttackFlow("gate_duplicate", mappedAnimation: requestedAnimation, note: "current=" + (current ?? ""));
      _LogAttackGate("duplicate", current, requestedAnimation);
      return true;
    }

    _LogAttackFlow("gate_busy", mappedAnimation: requestedAnimation, note: "current=" + (current ?? ""));
    _LogAttackGate("busy", current, requestedAnimation);
    return true;
  }

  bool _IsLocomotionAnimation(string animationName) {
    if (string.IsNullOrWhiteSpace(animationName)) return false;

    var controller = gearController != null ? gearController.Controller : null;
    if (controller != null) {
      return controller.IsLocomotionAnimation(animationName);
    }

    if (!Animations.Esperanza.TryGetValue(animationName, out var animation)) return false;
    return animation != null && animation.isLocomotion;
  }

  static bool _IsTransitionAnimation(string animationName) {
    if (string.IsNullOrWhiteSpace(animationName)) return false;
    if (!Animations.Esperanza.TryGetValue(animationName, out var anim) || anim == null) return false;
    return anim.To == 1 || anim.To == 2;
  }

  void _LogAttackGate(string reason, string currentAnimation, string requestedAnimation) {
    if (ForceDisableDebugLogsForPerfPass) return;
    if (!logAttackGate) return;
    if (lastAttackGateLogFrame == Time.frameCount) return;
    lastAttackGateLogFrame = Time.frameCount;
    Debug.Log(
      "[GameplayInput] attack gate reason='" + reason +
      "' current='" + (currentAnimation ?? "") +
      "' requested='" + (requestedAnimation ?? "") + "'"
    );
  }

  void _LogAttackFlow(
    string stage,
    string actionKey = null,
    string mappedAnimation = null,
    string note = null
  ) {
    if (ForceDisableDebugLogsForPerfPass) return;
    if (!logAttackFlow) return;
    if (!ShouldTraceAttackFlow(actionKey, mappedAnimation)) return;
    var current = gearController != null ? gearController.CurrentAnimation : "";
    var controllerPlaying = gearController != null &&
      gearController.Controller != null &&
      gearController.Controller.IsPlaying;
    Debug.Log(
      "[GameplayInput][AttackTrace] stage='" + (stage ?? "") +
      "' action='" + (actionKey ?? "") +
      "' mapped='" + (mappedAnimation ?? "") +
      "' current='" + (current ?? "") +
      "' playing=" + (controllerPlaying ? 1 : 0) +
      " note='" + (note ?? "") + "'"
    );
  }

  static bool ShouldTraceAttackFlow(string actionKey, string mappedAnimation) {
    var isAttack1Action = string.Equals(actionKey, "attack1", StringComparison.Ordinal);
    var isPunchLeftAnimation =
      !string.IsNullOrWhiteSpace(mappedAnimation) &&
      mappedAnimation.IndexOf("PunchLeft", StringComparison.OrdinalIgnoreCase) >= 0;
    return isAttack1Action || isPunchLeftAnimation;
  }

  void _TryBoostJumpForAirAttack() {
    if (!isJumping || erb == null) return;
    if (airAttackBoostsUsed >= maxAirAttackBoostsPerJump) return;
    airAttackBoostsUsed++;

    float currentY = erb.transform.localPosition.y;
    float boostedPeakY = currentY + airAttackBoostHeight;
    CancelPlayerTweens();
    LeanTween.sequence()
      .append(RegisterPlayerTween(LeanTween.moveLocalY(erb.gameObject, boostedPeakY, 0.12f).setEaseOutQuad(), 0.12f))
      .append(RegisterPlayerTween(LeanTween.delayedCall(erb.gameObject, airAttackBoostHangSeconds, () => { }), airAttackBoostHangSeconds))
      .append(RegisterPlayerTween(LeanTween.moveLocalY(erb.gameObject, jumpGroundLocalY, 0.18f).setEaseInQuad(), 0.18f))
      .append(() => {
        isGrounded = true;
        isJumping = false;
        HandleLanding();
      });
  }

  void CancelPlayerTweens() {
    if (erb == null) return;
    LeanTween.cancel(erb.gameObject);
    TimeScale.UnregisterTweens(erb.gameObject);
  }

  LTDescr RegisterPlayerTween(LTDescr descr, float baseDuration) {
    return TimeScale.RegisterTween(ResolveSceneTimeContextTransform(), descr, baseDuration);
  }

  Transform ResolveSceneTimeContextTransform() {
    if (erb != null) return erb.transform;
    if (EsperanzaParent != null) return EsperanzaParent.transform;
    return null;
  }

  float GetSceneTimeFactor() {
    return TimeScale.GetEffectiveFactor(ResolveSceneTimeContextTransform());
  }

  float GetSceneDeltaTime() {
    return TimeScale.GetDeltaTime(ResolveSceneTimeContextTransform());
  }

  float GetSceneNow() {
    return TimeScale.GetNow(ResolveSceneTimeContextTransform());
  }
}
