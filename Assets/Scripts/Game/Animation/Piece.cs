using System;
using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

public class Piece : MonoBehaviour {
  private Rigidbody2D rb;
  private All1AnimatorScript all1;
  private bool launched;
  private bool hasCrossedZero;
  private bool done;
  private float timer;
  private Vector2 fakeBounceImpulse; // Smaller impulse for bounce
  private float lifeAfterFakeBounce;

  void Start() {
    rb = GetComponent<Rigidbody2D>();
    all1 = GetComponent<All1AnimatorScript>();
    all1.AddFloatAnim("fadeOut", "_FadeAmount", 0, 1, 2, autoPlay: false);
    all1.AddFloatAnim("resetFade", "_FadeAmount", 0, 1, .01f, autoPlay: false);
  }

  public void ResetPiece() {
    launched = false;
    hasCrossedZero = false;
    done = false;
    timer = 0f;
    transform.localPosition = Vector3.zero;
    if (all1 == null) Start();
    all1.Play("resetFade");
    if (rb != null) {
      rb.simulated = true;
      rb.linearVelocity = Vector2.zero;
      rb.angularVelocity = 0f;
      rb.WakeUp();
    }
  }

  public void Launch(Vector2 force, float torque) {
    if (rb == null) return;
    launched = true;
    fakeBounceImpulse = force * .9f;
    lifeAfterFakeBounce = 1f * Random.Range(0.5f, 1.5f);
    rb.AddForce(force, ForceMode2D.Impulse);
    rb.AddTorque(torque, ForceMode2D.Impulse);
  }

  void FixedUpdate() {
    if (!launched) return;
    if (transform.localPosition.y < 0f) {
      hasCrossedZero = true;
      fakeBounceImpulse *= .75f;
      if (fakeBounceImpulse.y > .1f) rb.AddForce(fakeBounceImpulse, ForceMode2D.Impulse);
    }
  }

  void Update() {
    if (!launched || done) return;
    if (hasCrossedZero) {
      timer += Time.deltaTime;
      if (timer >= lifeAfterFakeBounce) {
        done = true;
        StartCoroutine(FreezePhysicsAndFade());
      }
    }
  }

  IEnumerator FreezePhysicsAndFade() {
    if (rb != null) {
      rb.linearVelocity = Vector2.zero;
      rb.angularVelocity = 0f;
      rb.simulated = false;
    }
    all1.Play("fadeOut");
    yield return new WaitForSeconds(2);
    gameObject.SetActive(false);
  }
}
