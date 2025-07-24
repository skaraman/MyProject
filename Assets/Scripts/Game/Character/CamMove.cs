using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CamMove : MonoBehaviour {
  [SerializeField] public float speed = 600;
  private Vector2 input = Vector3.zero;
  private Rigidbody2D cameraRB;
  public GameObject characterGO;
  private Rigidbody2D characterRB;
  private Transform characterT;
  private GearController gearController;
  private bool isFacingRight = true;
  private List<Action> offs = new List<Action>();
  private bool shiftIsPressed = false;

  public void OnDisable() {
    for (int i = 0; i < offs.Count; i++) {
      offs[i]();
    }
  }
  public void Start() {
    cameraRB = GetComponent<Rigidbody2D>();
    characterRB = characterGO.GetComponent<Rigidbody2D>();
    characterT = characterGO.GetComponent<Transform>();
    gearController = characterGO.GetComponent<GearController>();

    offs.Add(MessageBus.On("Left", (o) => input.x = -(float)o));
    offs.Add(MessageBus.On("Right", (o) => input.x = (float)o));
    offs.Add(MessageBus.On("Up", (o) => input.y = (float)o));
    offs.Add(MessageBus.On("Down", (o) => input.y = -(float)o));
    offs.Add(MessageBus.On("Shift", (o) => {
      var oo = (float)o;
      if (oo == 0) {
        shiftIsPressed = false;
      }
      else {
        shiftIsPressed = true;
      }
    }));
  }

  public void FixedUpdate() {
    //input.x = Input.GetAxis("Horizontal");
    //input.y = Input.GetAxis("Vertical");
    if (input.x < 0 && isFacingRight) {
      gearController.isFacingRight = false;
      isFacingRight = false;
      FlipAllChildrenSprites(true);
      gearController.SetBounces();
    }
    if (input.x > 0 && !isFacingRight) {
      gearController.isFacingRight = true;
      isFacingRight = true;
      FlipAllChildrenSprites(false);
      gearController.SetBounces();
    }
    cameraRB.linearVelocity = input * speed * Time.fixedDeltaTime;
    characterRB.linearVelocity = input * speed * Time.fixedDeltaTime;

    if (input.x == 0 && input.y == 0) {
      gearController.PlayAnimation("Breathe");
    }
    else {
      if (shiftIsPressed) {
        gearController.PlayAnimation("Sprint");
      }
      else {
        gearController.PlayAnimation("Run");
      }
    }
  }
  public void FlipAllChildrenSprites(bool flip) {
    SpriteRenderer[] spriteRenderers = characterGO.GetComponentsInChildren<SpriteRenderer>();
    foreach (SpriteRenderer sr in spriteRenderers) {
      sr.flipX = flip;
    }
  }
}