using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class GameplayInput : MonoBehaviour {
  private enum MoveMode {
    Run,
    Sprint
  }

  private readonly List<Action> actions = new();
  public GameObject cam;
  public GameObject EsperanzaParent;
  private Rigidbody2D erb;
  private Rigidbody2D cameraRB;
  public GearController gearController;
  public CharacterState characterState;
  public GameObject formsWheel;

  [Header("Movement")]
  [SerializeField] private float sprintMultiplier = 2f;
  [SerializeField] private float sprintSustainSeconds = 2f;
  [SerializeField] private float sprintResumeWindowSeconds = 2f;

  [Header("Stance")]
  [SerializeField] private float stanceDurationSeconds = 2f;
  [SerializeField] private float stanceMoveMultiplier = 0.15f;

  [Header("Jump")]
  [SerializeField] private float jumpHeight = 5f;
  [SerializeField] private float jumpDuration = 1f;
  [SerializeField] private float airAttackBoostHeight = 1f;
  [SerializeField] private float airAttackBoostHangSeconds = 0.15f;
  [SerializeField] private int maxAirAttackBoostsPerJump = 1;

  [Header("Dash")]
  [SerializeField] private float dashDistance = 3f;

  [Header("Attack Combos")]
  [SerializeField] private float superAttackWindowSeconds = 0.2f;

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

  void Start() {
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
    actions.Add(MessageBus.On("gameplay.wheel", o => { if (_IsPressed(o)) formsWheel.SetActive(!formsWheel.activeSelf); }));
    actions.Add(MessageBus.On("gameplay.charUp", o => charUp(o)));
    actions.Add(MessageBus.On("gameplay.charDown", o => charDown(o)));
    actions.Add(MessageBus.On("gameplay.charLeft", o => charLeft(o)));
    actions.Add(MessageBus.On("gameplay.charRight", o => charRight(o)));
    cameraRB = cam.GetComponent<Rigidbody2D>();
    erb = EsperanzaParent.GetComponent<Rigidbody2D>();
  }

  bool _IsPressed(object o) {
    if (o == null) return true;
    if (o is bool b) return b;
    if (o is float f) return f > 0.5f;
    if (o is int i) return i != 0;
    if (o is Vector2 v) return v.sqrMagnitude > 0.25f;
    if (o is Vector3 v3) return v3.sqrMagnitude > 0.25f;
    return true;
  }

  void OnDisable() {
    for (int i = 0; i < actions.Count; i++) {
      actions[i]();
    }
    actions.Clear();
  }

  void Update() {
    if (erb == null || gearController == null) return;
    _CameraFollow();
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
    LeanTween.cancel(erb.gameObject);
    LeanTween.sequence()
      .append(LeanTween.moveLocalY(erb.gameObject, peakY, halfDuration).setEaseOutQuad())
      .append(LeanTween.moveLocalY(erb.gameObject, jumpGroundLocalY, halfDuration).setEaseInQuad())
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
    rawUp = (float)value;
  }

  void charDown(object value) {
    rawDown = (float)value;
  }

  void charLeft(object value) {
    float newValue = (float)value;
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
    float newValue = (float)value;
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

  void _CameraFollow() {
    Vector3 targetPos = erb.transform.localPosition;
    Vector3 currentPos = cameraRB.transform.localPosition; // Changed to localPosition
    Vector3 newPos = Vector3.MoveTowards(currentPos, targetPos, 1f);
    cameraRB.transform.localPosition = newPos; // Set localPosition instead of MovePosition
  }

  void _ProcessMovementVelocity(Vector2 moveInput) {
    if (gearController == null) return;
    if (gearController.CurrentAnimation == "Dance") return;

    float speed = (10 + AllStatValues.Esperanza["MVSP"]);
    float moveMultiplier = stanceTimeRemainingSeconds > 0f ? stanceMoveMultiplier : (moveMode == MoveMode.Sprint ? sprintMultiplier : 1f);

    erb.linearVelocityX = moveInput.x * speed * moveMultiplier;
    erb.linearVelocityY = moveInput.y * speed * moveMultiplier;

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
    var isLocomotion = current == "Breathe" || current == "Walk" || current == "Run" || current == "Sprint" || current == "Stance";
    if (!isLocomotion && isPlaying) return;

    if (stanceTimeRemainingSeconds > 0f) {
      _TryPlayLocomotion("Stance");
      return;
    }

    var input = math.abs(moveInput.x) + math.abs(moveInput.y);
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
    bool inputMoving = math.abs(moveInput.x) + math.abs(moveInput.y) > 0f;

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
        sustainedMoveSeconds += Time.deltaTime;
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
        timeSinceMoveStopSeconds += Time.deltaTime;
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
    stanceTimeRemainingSeconds = Mathf.Max(0f, stanceTimeRemainingSeconds - Time.deltaTime);
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
    erb.linearVelocity = jumpMomentum;
  }

  void _RecoverToStanceWhenActionEnds() {
    if (gearController == null || gearController.Controller == null) return;
    if (gearController.Controller.IsPlaying) return;
    var current = gearController.CurrentAnimation;
    if (string.IsNullOrEmpty(current)) return;
    if (current == "Breathe" || current == "Walk" || current == "Run" || current == "Sprint" || current == "Stance") return;

    if (stanceTimeRemainingSeconds <= 0f) {
      _EnterStance(resetTimer: true);
    }
    _TryPlayLocomotion("Stance");
  }

  bool _TryPlayMappedAction(string actionKey, string fallback, bool forceRestart = false) {
    if (gearController == null) return false;
    var anim = _GetMappedAnimation(actionKey) ?? fallback;
    if (string.IsNullOrEmpty(anim)) return false;
    if (!Animations.Esperanza.ContainsKey(anim)) anim = fallback;
    if (string.IsNullOrEmpty(anim)) return false;
    if (!Animations.Esperanza.ContainsKey(anim)) return false;
    if (gearController.Controller != null) return gearController.Controller.PlayAnimation(anim, forceRestart);
    gearController.PlayAnimation(anim);
    return true;
  }

  string _GetMappedAnimation(string actionKey) {
    if (string.IsNullOrEmpty(actionKey)) return null;
    if (!AttacksMapToForms.all.TryGetValue(EsperanzaForms.GetActive(), out var map)) return null;
    if (!map.TryGetValue(actionKey, out var anim)) return null;
    return anim;
  }

  void _TryPlayLocomotion(string anim) {
    if (gearController == null || string.IsNullOrEmpty(anim)) return;
    if (!Animations.Esperanza.ContainsKey(anim)) return;
    gearController.PlayAnimation(anim);
  }

  void _HandleAttackPress(int index) {
    float now = Time.time;
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

    if (isJumping) {
      _TryBoostJumpForAirAttack();
    }

    _EnterStance(resetTimer: true);
    _TryPlayMappedAction(actionKey, fallback: null);
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

  void _TryBoostJumpForAirAttack() {
    if (!isJumping || erb == null) return;
    if (airAttackBoostsUsed >= maxAirAttackBoostsPerJump) return;
    airAttackBoostsUsed++;

    float currentY = erb.transform.localPosition.y;
    float boostedPeakY = currentY + airAttackBoostHeight;
    LeanTween.cancel(erb.gameObject);
    LeanTween.sequence()
      .append(LeanTween.moveLocalY(erb.gameObject, boostedPeakY, 0.12f).setEaseOutQuad())
      .append(LeanTween.delayedCall(erb.gameObject, airAttackBoostHangSeconds, () => { }))
      .append(LeanTween.moveLocalY(erb.gameObject, jumpGroundLocalY, 0.18f).setEaseInQuad())
      .append(() => {
        isGrounded = true;
        isJumping = false;
        HandleLanding();
      });
  }
}
