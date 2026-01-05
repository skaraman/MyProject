using System;
using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

public class Piece : MonoBehaviour {
  private Rigidbody2D rb;
  private All1AnimatorScript all1;
  private Coroutine freezeRoutine;
  private Vector3 initialLocalPosition;
  private Quaternion initialLocalRotation;
  private Vector3 initialLocalScale;
  private bool hasCachedTransform;
  private bool animationsInitialized;
  private bool launched;
  private bool hasCrossedZero;
  private bool done;
  private float timer;
  private Vector2 fakeBounceImpulse; // Smaller impulse for bounce
  private float lifeAfterFakeBounce;

  void Awake() {
    rb = GetComponent<Rigidbody2D>();
    all1 = GetComponent<All1AnimatorScript>();
    CacheInitialTransform();
  }

  void Start() {
    EnsureAnimations();
  }

  void CacheInitialTransform() {
    if (hasCachedTransform) return;
    initialLocalPosition = transform.localPosition;
    initialLocalRotation = transform.localRotation;
    initialLocalScale = transform.localScale;
    hasCachedTransform = true;
  }

  void EnsureAnimations() {
    if (animationsInitialized) return;
    if (all1 == null) all1 = GetComponent<All1AnimatorScript>();
    if (all1 == null) return;
    all1.AddFloatAnim("fadeOut", "_FadeAmount", 0, 1, 2, autoPlay: false);
    all1.AddFloatAnim("resetFade", "_FadeAmount", 0, 1, .01f, autoPlay: false);
    animationsInitialized = true;
  }

  public void ResetPiece() {
    EnsureAnimations();
    if (freezeRoutine != null) {
      StopCoroutine(freezeRoutine);
      freezeRoutine = null;
    }
    launched = false;
    hasCrossedZero = false;
    done = false;
    timer = 0f;
    fakeBounceImpulse = Vector2.zero;
    lifeAfterFakeBounce = 0f;
    if (!hasCachedTransform) CacheInitialTransform();
    transform.localPosition = initialLocalPosition;
    transform.localRotation = initialLocalRotation;
    transform.localScale = initialLocalScale;
    all1.Play("resetFade");
    if (rb == null) rb = GetComponent<Rigidbody2D>();
    if (rb != null) {
      rb.simulated = true;
      rb.linearVelocity = Vector2.zero;
      rb.angularVelocity = 0f;
      rb.position = transform.position;
      rb.rotation = transform.eulerAngles.z;
      rb.WakeUp();
    }
  }

  public void Launch(Vector2 force, float torque) {
    if (rb == null) return;
    rb.simulated = true;
    rb.WakeUp();
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
        freezeRoutine = StartCoroutine(FreezePhysicsAndFade());
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
    freezeRoutine = null;
  }
}
