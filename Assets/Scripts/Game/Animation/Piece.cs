using UnityEngine;
using Random = UnityEngine.Random;

public class Piece : MonoBehaviour {
  const float GroundSlideLinearDamping = 2.5f;
  const float GroundSlideAngularDamping = 3f;
  const float MinimumSlideSeconds = 0.35f;
  const float MaximumSlideSeconds = 2f;
  const float SettledSpeedSquared = 0.15f * 0.15f;
  const float MinimumPileLifetimeSeconds = 3.5f;
  const float MaximumPileLifetimeSeconds = 5.5f;
  const float SortingUnitsPerWorldUnit = 100f;
  const int SortingOrderJitter = 3;
  const float VisibleFadeAmount = 0f;
  const float InvisibleFadeAmount = 1f;

  private Rigidbody2D rb;
  private SpriteRenderer spriteRenderer;
  private All1AnimatorScript all1;
  private Vector3 initialLocalPosition;
  private Quaternion initialLocalRotation;
  private Vector3 initialLocalScale;
  private int initialSortingOrder;
  private bool hasCachedTransform;
  private bool animationsInitialized;
  private bool launched;
  private bool hasSettled;
  private bool done;
  private float timer;
  private float fadeTimer;
  private bool isFading;
  private float pileLifetime;
  private float lastSortedY = float.MinValue;
  private int sortingOrderJitter;

  void Awake() {
    rb = GetComponent<Rigidbody2D>();
    spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
    if (spriteRenderer != null) {
      initialSortingOrder = spriteRenderer.sortingOrder;
    }
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
    all1.AddFloatAnim(
      "fadeOut",
      "_FadeAmount",
      VisibleFadeAmount,
      InvisibleFadeAmount,
      2f,
      autoPlay: false
    );
    all1.AddFloatAnim(
      "resetFade",
      "_FadeAmount",
      VisibleFadeAmount,
      VisibleFadeAmount,
      .01f,
      autoPlay: false
    );
    animationsInitialized = true;
  }

  public void ResetPiece() {
    EnsureAnimations();
    launched = false;
    hasSettled = false;
    done = false;
    timer = 0f;
    fadeTimer = 0f;
    isFading = false;
    pileLifetime = 0f;
    lastSortedY = float.MinValue;
    sortingOrderJitter = 0;
    if (!hasCachedTransform) CacheInitialTransform();
    transform.localPosition = initialLocalPosition;
    transform.localRotation = initialLocalRotation;
    transform.localScale = initialLocalScale;
    if (spriteRenderer == null) {
      spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
    }
    if (spriteRenderer != null) {
      spriteRenderer.sortingOrder = initialSortingOrder;
    }
    if (all1 != null) {
      all1.Play("resetFade");
    }
    if (rb == null) rb = GetComponent<Rigidbody2D>();
    if (rb != null) {
      rb.simulated = true;
      rb.linearVelocity = Vector2.zero;
      rb.angularVelocity = 0f;
      rb.gravityScale = 0f;
      rb.linearDamping = GroundSlideLinearDamping;
      rb.angularDamping = GroundSlideAngularDamping;
      rb.position = transform.position;
      rb.rotation = transform.eulerAngles.z;
      rb.WakeUp();
    }
  }

  public void Launch(Vector2 force, float torque) {
    if (rb == null) return;
    rb.simulated = true;
    rb.gravityScale = 0f;
    rb.linearDamping = GroundSlideLinearDamping;
    rb.angularDamping = GroundSlideAngularDamping;
    rb.WakeUp();
    launched = true;
    hasSettled = false;
    timer = 0f;
    pileLifetime = Random.Range(MinimumPileLifetimeSeconds, MaximumPileLifetimeSeconds);
    sortingOrderJitter = Random.Range(-SortingOrderJitter, SortingOrderJitter + 1);
    UpdatePileSorting(force: true);
    rb.AddForce(force, ForceMode2D.Impulse);
    rb.AddTorque(torque, ForceMode2D.Impulse);
  }

  void Update() {
    if (!launched || done) return;

    UpdatePileSorting(force: false);

    if (!hasSettled && !isFading) {
      timer += TimeScale.GetDeltaTime(this);
      var speedSquared = rb != null ? rb.linearVelocity.sqrMagnitude : 0f;
      if ((timer >= MinimumSlideSeconds && speedSquared <= SettledSpeedSquared) ||
          timer >= MaximumSlideSeconds) {
        hasSettled = true;
        timer = 0f;
      }
    }
    else if (hasSettled && !isFading) {
      timer += TimeScale.GetDeltaTime(this);
      if (timer >= pileLifetime) {
        isFading = true;
        fadeTimer = 0f;
        if (rb != null) {
          rb.linearVelocity = Vector2.zero;
          rb.angularVelocity = 0f;
          rb.simulated = false;
        }
        if (all1 != null) {
          all1.Play("fadeOut");
        }
      }
    }
    else if (isFading) {
      fadeTimer += TimeScale.GetDeltaTime(this);
      if (fadeTimer >= 2f) {
        done = true;
        gameObject.SetActive(false);
      }
    }
  }

  void UpdatePileSorting(bool force) {
    if (spriteRenderer == null) {
      return;
    }

    var worldY = transform.position.y;
    if (!force && Mathf.Abs(worldY - lastSortedY) < 0.005f) {
      return;
    }

    lastSortedY = worldY;
    spriteRenderer.sortingOrder =
      Mathf.RoundToInt(-worldY * SortingUnitsPerWorldUnit) + sortingOrderJitter;
  }
}
