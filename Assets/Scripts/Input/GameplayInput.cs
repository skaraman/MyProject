using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class GameplayInput : MonoBehaviour {
  private List<Action> actions = new();
  public GameObject cam;
  public GameObject EsperanzaParent;
  private Rigidbody2D erb;
  private Rigidbody2D cameraRB;
  public GearController gearController;
  public CharacterState characterState;
  public GameObject formsWheel;

  private float xVelocity = 0f;
  private float yVelocity = 0f;
  private float sprintShift = 1f;

  private float sprintTimeTracker = 0f;
  private float timeUntilSprint = 2f;
  private float baseTimeUntilSprint = 2f;
  private float idleTime = 0f;
  private bool wasMoving = false;
  private bool isJumping = false;
  private bool isFalling = false;
  private bool isGrounded = true;
  private float stanceTimer = 0f;

  void Start() {
    actions.Add(MessageBus.On("gameplay.attack1", o => attack1()));
    actions.Add(MessageBus.On("gameplay.attack2", o => attack2()));
    actions.Add(MessageBus.On("gameplay.attack3", o => attack3()));
    actions.Add(MessageBus.On("gameplay.attack4", o => attack4()));
    actions.Add(MessageBus.On("gameplay.block", o => block()));
    actions.Add(MessageBus.On("gameplay.dash", o => dash()));
    actions.Add(MessageBus.On("gameplay.dodge", o => dodge()));
    actions.Add(MessageBus.On("gameplay.jump", o => StartCoroutine(jump())));
    actions.Add(MessageBus.On("gameplay.charUp", o => charUp(o)));
    actions.Add(MessageBus.On("gameplay.charDown", o => charDown(o)));
    actions.Add(MessageBus.On("gameplay.charLeft", o => charLeft(o)));
    actions.Add(MessageBus.On("gameplay.charRight", o => charRight(o)));
    actions.Add(MessageBus.On("gameplay.pause", o => pause()));
    actions.Add(MessageBus.On("gameplay.dance", o => dance()));
    actions.Add(MessageBus.On("gameplay.wheel", o => formsWheel.SetActive(!formsWheel.activeSelf)));
    cameraRB = cam.GetComponent<Rigidbody2D>();
    erb = EsperanzaParent.GetComponent<Rigidbody2D>();
  }

  void OnDisable() {
    for (int i = 0; i < actions.Count; i++) {
      actions[i]();
    }
    actions.Clear();
  }

  void attack1() { }
  void attack2() { }
  void attack3() { }
  void attack4() { }
  void block() { }
  void dash() { }
  void dodge() { }

  System.Collections.IEnumerator jump() {
    if (isGrounded) {
      gearController.PlayAnimation("Jump");
      isGrounded = false;
      isJumping = true;
      isFalling = false;
      erb.linearVelocity = new Vector2(erb.linearVelocity.x, 0); // Reset vertical velocity before jump
      erb.AddForce(new Vector2(0, 400f + AllStatValues.allStats["MVSP"] * 20f));
      yield return new WaitForSeconds(1);
      isGrounded = true;
      isJumping = false;
      gearController.PlayAnimation("JumpLanding");
      yield return new WaitForSeconds(
        EsperanzaAnimations.animations[gearController.currentAnimation].duration / 1000
      );
      stanceTimer = 0f;
    }
  }

  void dance() {
    gearController.PlayAnimation("Dance");
  }

  void charUp(object value) {
    yVelocity = (float)value;
  }

  void charDown(object value) {
    yVelocity = -(float)value;
  }

  void charLeft(object value) {
    xVelocity = -(float)value;
  }

  void charRight(object value) {
    xVelocity = (float)value;
  }

  void pause() {
    MessageBus.Send("openPauseMenu", null);
  }

  void Update() {
    if (erb == null) return;
    _CameraFollow();

    if (isJumping) return;
    _SprintCheck();
    _ProcessMovementVelocity();
    _ProcessMovementAnimation();

  }



  void _CameraFollow() {
    Vector3 targetPos = erb.transform.localPosition;
    Vector3 currentPos = cameraRB.transform.localPosition; // Changed to localPosition
    Vector3 newPos = Vector3.MoveTowards(currentPos, targetPos, 1f);
    cameraRB.transform.localPosition = newPos; // Set localPosition instead of MovePosition
  }

  void _ProcessMovementVelocity() {
    if (gearController.currentAnimation == "Dance") return;
    if (gearController.currentAnimation == "Stance") return;
    erb.linearVelocityY = yVelocity * (10 + AllStatValues.allStats["MVSP"]) * sprintShift;
    erb.linearVelocityX = xVelocity * (10 + AllStatValues.allStats["MVSP"]) * sprintShift;
    if ((xVelocity < 0 && gearController.isFacingRight) ||
    (xVelocity > 0 && !gearController.isFacingRight)) {
      gearController.needsFlip = true;
    }
  }

  void _ProcessMovementAnimation() {
    var input = math.abs(xVelocity) + math.abs(yVelocity);
    stanceTimer += Time.deltaTime;
    if (input == 0) {
      if (stanceTimer > 2f) {
        gearController.PlayAnimation("Breathe");
      }
      else {
        gearController.PlayAnimation("Stance");
      }
    }
    else {
      if (stanceTimer > 2f || gearController.currentAnimation == "Breathe") {
        if (sprintShift > 1) {
          gearController.PlayAnimation("Sprint");
        }
        else {
          gearController.PlayAnimation("Run");
        }
      }
    }
  }

  void _SprintCheck() {
    var inputMoving = math.abs(xVelocity) + math.abs(yVelocity) > 0;
    if (inputMoving) {
      if (!wasMoving) {
        // Just started moving after being idle
        if (idleTime < baseTimeUntilSprint) {
          // If we were idle for less than the base sprint time,
          // set time until sprint to the idle time (shorter sprint activation)
          timeUntilSprint = math.max(0.1f, idleTime);
        }
        else {
          // If we were idle for longer than base sprint time, reset to full duration
          timeUntilSprint = baseTimeUntilSprint;
        }
        sprintTimeTracker = 0f;
      }
      sprintTimeTracker += Time.deltaTime;
      idleTime = 0f;
    }
    else {
      // Not moving
      if (wasMoving) {
        // Just stopped moving
        idleTime = 0f;
      }
      idleTime += Time.deltaTime;
      sprintTimeTracker = 0f;
    }
    sprintShift = sprintTimeTracker >= timeUntilSprint ? 2f : 1f;
    wasMoving = inputMoving;
  }

  // void _CameraFollow() {
  //     Vector3 targetPos = erb.transform.localPosition;
  //     cameraRB.gameObject.transform.localPosition = Vector3.Lerp(
  //         cameraRB.gameObject.transform.localPosition, 
  //         targetPos, 
  //         Time.deltaTime * 5f // followSpeed around 2-5f
  //     );
  // }
}
