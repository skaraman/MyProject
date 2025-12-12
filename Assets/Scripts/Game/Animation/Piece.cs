using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

public class Piece : MonoBehaviour {
  public float gravity = -20f;
  public float bounciness = 0.4f;
  public float minHeightVelocityToBounce = 1f;
  public float minPlanarSpeedToSleep = 0.05f;
  public float lifeTime = 8f;
  public float heightScale = 1f;
  public bool disableOnRest = true;
  public float bounceImpulseBoost = 1.5f;
  [Header("Launch Tuning")]
  public float launchGravityScale = 0.65f;
  public float launchDrag = 0.15f;
  public float launchAngularDrag = 0.05f;
  [Header("Post Bounce Tuning")]
  public float postBounceGravityScale = 0.85f;
  public float postBounceDrag = 3f;
  public float postBounceAngularDrag = 3f;
  public float postBounceVelocityDamp = 0.6f;
  public float bounceImpulseMultiplier = 0.75f;
  public float minFakeBounceImpulse = 0.7f;
  public float angularSlowdownRate = 360f;

  Rigidbody2D rb;
  CapsuleCollider2D _collider;
  DestructionManager _manager;
  float _height;
  float _heightVel;
  float _life;
  bool _isActive;
  bool _hasBounced;
  float _groundThresholdY = 0;
  Vector2 _bounceImpulseDir;
  string _lastBounceLabel;
  bool _lockHeightToGround;
  Coroutine _disableRoutine;
  Coroutine _angularSlowdownRoutine;
  WaitForSeconds _cachedDisableWait;
  float _cachedDisableDelay = -1f;
  bool alreadyDisabling = false;

  public bool IsAtRest => !_isActive;
  public Rigidbody2D Body => rb;
  public CapsuleCollider2D Collider => _collider;
  public bool HasBounced => _hasBounced;
  public float GroundThresholdY => _groundThresholdY;
  public string LastBounceLabel => _lastBounceLabel;
  public bool IsSimulatingPhysics => gameObject.activeSelf && rb != null && rb.simulated;

  private All1AnimatorScript animator;
  void Awake() {
    CacheComponents();
  }

  void OnEnable() {
    animator = GetComponent<All1AnimatorScript>();
    CacheComponents();
  }

  void CacheComponents() {
    if (!rb) rb = GetComponent<Rigidbody2D>();
    if (!_collider) _collider = GetComponent<CapsuleCollider2D>();
    if (animator == null) animator = GetComponent<All1AnimatorScript>();
    if (animator != null) animator.AddFloatAnim("fadeOut", "_FadeAmount", 0f, 1f, 2f);

  }

  public bool InitializeForDestruction(DestructionManager manager) {
    _manager = manager;
    CacheComponents();
    return rb != null && _collider != null;
  }

  public bool IsReadyForDestruction() {
    CacheComponents();
    return rb != null && _collider != null;
  }

  public void ResetTransformToOrigin() {
    var t = transform;
    t.localPosition = Vector3.zero;
    t.localRotation = Quaternion.identity;
    if (!gameObject.activeSelf) gameObject.SetActive(true);
  }

  public void SetupBounceDetection() {
    CacheComponents();
    _bounceImpulseDir = GetRandomUpwardDirection();
    _hasBounced = false;
  }

  public void ResetForLaunch(float initialHeight, float initialHeightVelocity) {
    CacheComponents();
    StopAngularSlowdown();
    _height = initialHeight;
    _heightVel = initialHeightVelocity;
    _life = lifeTime;
    _isActive = true;
    _hasBounced = false;
    _lockHeightToGround = false;
    _lastBounceLabel = null;
    gameObject.SetActive(true);
    if (rb) {
      rb.simulated = true;
      rb.bodyType = RigidbodyType2D.Dynamic;
      rb.gravityScale = launchGravityScale;
      rb.linearDamping = launchDrag;
      rb.angularDamping = launchAngularDrag;
      rb.linearVelocity = Vector2.zero;
      rb.angularVelocity = 0f;
    }
  }

  public void Fire(Vector2 planarVelocity, float initialHeight, float initialHeightVelocity, float torque) {
    ResetForLaunch(initialHeight, initialHeightVelocity);
    if (rb) {
      rb.linearVelocity = planarVelocity;
      rb.angularVelocity = torque;
    }
  }

  public void ResetForPool(bool disableObject) {
    StopDisableRoutine();
    StopAngularSlowdown();
    CacheComponents();
    _isActive = false;
    _life = 0f;
    _height = 0f;
    _heightVel = 0f;
    _lastBounceLabel = null;
    _lockHeightToGround = false;
    if (rb) {
      rb.linearVelocity = Vector2.zero;
      rb.angularVelocity = 0f;
      rb.bodyType = RigidbodyType2D.Static;
      rb.simulated = false;
    }
    _hasBounced = false;
    if (disableObject && !alreadyDisabling) {
      alreadyDisabling = true;
      //animator.Play("fadeOut");
      StartCoroutine(DisableAfterDelayCoroutine(2f));
    }
  }

  IEnumerator DisableAfterDelayCoroutine(float delay) {
    yield return new WaitForSeconds(delay);
    gameObject.SetActive(false);
  }

  void Update() {
    if (!_isActive) return;
    var dt = Time.deltaTime;
    _life -= dt;
    if (_life <= 0f) {
      Rest("life_expired");
      return;
    }
    if (_lockHeightToGround) {
      var lockedPos = transform.localPosition;
      lockedPos.y = _groundThresholdY;
      transform.localPosition = lockedPos;
      if (_disableRoutine != null && rb != null && _angularSlowdownRoutine == null) {
        rb.angularVelocity = Mathf.MoveTowards(rb.angularVelocity, 0f, angularSlowdownRate * dt);
      }
      return;
    }
    _heightVel += gravity * dt;
    _height += _heightVel * dt;
    var localPos = transform.localPosition;
    var predictedLocalY = localPos.y + _height * heightScale;
    var relativeGroundHeight = heightScale != 0f
      ? (_groundThresholdY - localPos.y) / heightScale
      : 0f;
    if (predictedLocalY <= _groundThresholdY) {
      _height = relativeGroundHeight;
      if (_hasBounced) {
        _heightVel = 0f;
      }
      else if (_heightVel < 0f && Mathf.Abs(_heightVel) > minHeightVelocityToBounce) {
        _heightVel = -_heightVel * bounciness;
      }
      else {
        _heightVel = 0f;
        var minPlanarSpeedToSleepSqr = minPlanarSpeedToSleep * minPlanarSpeedToSleep;
        if (rb == null || rb.linearVelocity.sqrMagnitude <= minPlanarSpeedToSleepSqr) {
          Rest("stopped");
          return;
        }
      }
      predictedLocalY = _groundThresholdY;
    }
    localPos.y = predictedLocalY;
    transform.localPosition = localPos;
    if (_disableRoutine != null && rb != null && _angularSlowdownRoutine == null) {
      rb.angularVelocity = Mathf.MoveTowards(rb.angularVelocity, 0f, angularSlowdownRate * dt);
    }
  }

  void Rest(string reason) {
    if (!_isActive) return;
    MessageBus.Send("PieceRest", this);
    ResetForPool(disableOnRest);
  }

  public bool ShouldRegisterBounce() {
    if (_hasBounced || !_isActive || rb == null || !rb.simulated) return false;
    var localY = transform.localPosition.y;
    return localY <= _groundThresholdY && rb.linearVelocity.y <= 0f;
  }

  public void RegisterBounce(float impulseMin, float impulseMax, float disableDelay, string label = null) {
    _hasBounced = true;
    _lastBounceLabel = label;
    var targetImpulse = GetBounceImpulse(impulseMin, impulseMax);
    var disableTimer = label == "fakeBounce"
      ? Random.Range(0.25f, 1f)
      : disableDelay;
    if (targetImpulse < minFakeBounceImpulse) {
      ApplyPostBounceDamping(true);
      StartAngularSlowdown(disableTimer);
      StartDisableAfterDelay(disableTimer);
      return;
    }
    ApplyBounceImpulse(targetImpulse);
    ApplyPostBounceDamping(false);
    StartAngularSlowdown(disableTimer);
    StartDisableAfterDelay(disableTimer);
  }

  public void StopDisableRoutine() {
    if (_disableRoutine == null) return;
    StopCoroutine(_disableRoutine);
    _disableRoutine = null;
  }

  public void StopAngularSlowdown() {
    if (_angularSlowdownRoutine == null) return;
    StopCoroutine(_angularSlowdownRoutine);
    _angularSlowdownRoutine = null;
  }

  public void StartAngularSlowdown(float duration) {
    StopAngularSlowdown();
    if (duration <= 0f) {
      if (rb != null) rb.angularVelocity = 0f;
      return;
    }
    _angularSlowdownRoutine = StartCoroutine(SlowAngularVelocityToZero(duration));
  }

  IEnumerator SlowAngularVelocityToZero(float duration) {
    if (rb == null) {
      _angularSlowdownRoutine = null;
      yield break;
    }
    var initialAngularVelocity = rb.angularVelocity;
    float elapsed = 0f;
    while (elapsed < duration && rb != null) {
      var t = elapsed / duration;
      rb.angularVelocity = Mathf.Lerp(initialAngularVelocity, 0f, t);
      elapsed += Time.deltaTime;
      yield return null;
    }
    if (rb != null) rb.angularVelocity = 0f;
    _angularSlowdownRoutine = null;
  }

  public void StartDisableAfterDelay(float delay) {
    StopDisableRoutine();
    _disableRoutine = StartCoroutine(DisableAfterDelay(GetDisableWait(delay)));
  }

  IEnumerator DisableAfterDelay(WaitForSeconds wait) {
    yield return wait;
    ResetForPool(true);
    _disableRoutine = null;
  }

  WaitForSeconds GetDisableWait(float delay) {
    if (_cachedDisableWait == null || !Mathf.Approximately(_cachedDisableDelay, delay)) {
      _cachedDisableDelay = delay;
      _cachedDisableWait = new WaitForSeconds(delay);
    }
    return _cachedDisableWait;
  }

  Vector2 GetRandomUpwardDirection() {
    var dir = Random.insideUnitCircle;
    dir.y = Mathf.Abs(dir.y);
    if (dir.sqrMagnitude < 0.001f) dir = Vector2.up;
    return dir.normalized;
  }

  float GetBounceImpulse(float impulseMin, float impulseMax) {
    var impulse = Random.Range(impulseMin, impulseMax);
    return impulse * bounceImpulseBoost * bounceImpulseMultiplier;
  }

  void ApplyPostBounceDamping(bool lockHeight) {
    if (rb != null) {
      rb.gravityScale = postBounceGravityScale;
      rb.linearDamping = postBounceDrag;
      rb.angularDamping = postBounceAngularDrag;
      rb.linearVelocity *= postBounceVelocityDamp;
      if (lockHeight) {
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
      }
    }
    _height = 0f;
    _heightVel = 0f;
    _lockHeightToGround = lockHeight;
  }

  void ApplyBounceImpulse(float impulseMagnitude) {
    if (rb == null) return;
    var dir = _bounceImpulseDir;
    if (dir == Vector2.zero) dir = GetRandomUpwardDirection();
    rb.AddForce(dir * impulseMagnitude, ForceMode2D.Impulse);
  }

  void OnCollisionEnter2D(Collision2D collision) {
    if (_manager == null || collision == null) return;
    _manager.HandlePieceCollision(this, collision);
  }
}
