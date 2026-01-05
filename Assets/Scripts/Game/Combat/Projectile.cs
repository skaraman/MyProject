using System.Collections.Generic;
using UnityEngine;

public class Projectile : MonoBehaviour {
  public enum MovementType {
    Linear,
    Homing
  }

  [Header("Movement")]
  public MovementType movementType = MovementType.Linear;
  public float speed = 5f;
  public bool rotateToMovement = true;
  public float rotationOffsetDegrees;

  [Header("Animation")]
  public bool loopAnimation = true;
  public string effectKeyOverride;

  [Header("Despawn")]
  public float lifetimeSeconds = 2f;
  public bool despawnOnHurtBoxHit = true;
  public bool despawnOnAnyCollision;
  public LayerMask collisionLayers = ~0;
  public bool ignoreSameRoot = true;

  [Header("References")]
  public SpriteWithNormals spriteTarget;
  public PolygonCollider2D hitboxCollider;

  private Rigidbody2D rb2d;
  private ProjectileManager owner;
  private string poolKey;
  private string effectKey;
  private Vector3 direction;
  private Transform target;
  private AnimationController animationController = new();
  private Dictionary<string, AnimData> animationData;
  private bool animationControllerInitialized;
  private float lifetimeRemaining;
  private static readonly Dictionary<string, AnimData> EmptyAnimations = new();
  private static readonly Dictionary<string, Dictionary<string, string>> EmptyInterrupts = new();
  private static readonly Dictionary<string, Dictionary<string, List<HBox>>> EmptyHBoxData = new();

  public string PoolKey => poolKey;

  void Awake() {
    if (spriteTarget == null) spriteTarget = GetComponentInChildren<SpriteWithNormals>();
    if (hitboxCollider == null) hitboxCollider = GetComponentInChildren<PolygonCollider2D>();
    rb2d = GetComponent<Rigidbody2D>();
    InitializeAnimationController();
  }

  void OnEnable() {
    ResetLifetime();
  }

  void Update() {
    animationController.Tick(Time.deltaTime);
    UpdateLifetime(Time.deltaTime);
  }

  void FixedUpdate() {
    Move(Time.fixedDeltaTime);
  }

  void OnDisable() {
    animationController.Cleanup(false);
    lifetimeRemaining = 0f;
  }

  public void Launch(ProjectileManager owner, string key, Vector3 direction, Transform target = null, float? speedOverride = null) {
    this.owner = owner;
    poolKey = ResolveKey(key);
    effectKey = !string.IsNullOrEmpty(effectKeyOverride) ? NormalizeKey(effectKeyOverride) : poolKey;
    this.target = target;
    this.direction = direction.sqrMagnitude > 0f ? direction.normalized : transform.right;
    ResolveMovementTargets();
    if (speedOverride.HasValue) {
      speed = speedOverride.Value;
    }
    if (rb2d != null) {
      rb2d.linearVelocity = Vector2.zero;
      rb2d.angularVelocity = 0f;
    }
    ResetLifetime();
    ConfigureAnimationController(effectKey);
    animationController.ForceLoop = loopAnimation;
    if (!string.IsNullOrEmpty(effectKey)) {
      animationController.PlayAnimation(effectKey, true, resolveInterrupts: false);
    }
  }

  public void Despawn() {
    if (owner != null) {
      owner.DespawnProjectile(poolKey, gameObject);
    }
    else {
      gameObject.SetActive(false);
    }
  }

  private void Move(float dt) {
    if (speed <= 0f) return;
    Vector3 moveDir = direction;
    if (movementType == MovementType.Homing && target != null) {
      if (!target.gameObject.activeInHierarchy) {
        target = null;
      }
    }
    if (movementType == MovementType.Homing && target == null && owner != null) {
      target = owner.FindNearestEnemyTarget(transform.position);
    }
    if (movementType == MovementType.Homing && target != null) {
      moveDir = target.position - transform.position;
    }
    if (moveDir.sqrMagnitude <= 0.0001f) return;
    moveDir.Normalize();
    if (rotateToMovement) {
      ApplyRotation(moveDir);
    }
    var delta = moveDir * speed * dt;
    if (rb2d != null) {
      rb2d.MovePosition(rb2d.position + (Vector2)delta);
    }
    else {
      transform.position += delta;
    }
  }

  private void ApplyRotation(Vector3 moveDir) {
    float angle = Mathf.Atan2(moveDir.y, moveDir.x) * Mathf.Rad2Deg + rotationOffsetDegrees;
    if (rb2d != null) {
      rb2d.MoveRotation(angle);
    }
    else {
      transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }
  }

  private void ResolveMovementTargets() {
    if (movementType == MovementType.Linear) {
      target = null;
      float x = Mathf.Abs(direction.x) > 0.0001f ? direction.x : 1f;
      direction = new Vector3(x >= 0f ? 1f : -1f, 0f, 0f);
      return;
    }

    if (movementType == MovementType.Homing && target == null && owner != null) {
      target = owner.FindNearestEnemyTarget(transform.position);
    }
  }

  public void ConfigureAnimationData(Dictionary<string, AnimData> animations) {
    animationData = animations;
  }

  private void InitializeAnimationController() {
    if (animationControllerInitialized) return;
    var spriteObjects = spriteTarget != null ? new[] { spriteTarget.gameObject } : null;
    var hBoxObjects = hitboxCollider != null ? new[] { hitboxCollider.gameObject } : null;
    animationController.Initialize(
      transform,
      spriteObjects,
      null,
      hBoxObjects,
      EmptyAnimations,
      EmptyInterrupts,
      null,
      EmptyHBoxData,
      "",
      false
    );
    animationControllerInitialized = true;
  }

  private void ConfigureAnimationController(string animationKey) {
    if (!animationControllerInitialized) {
      InitializeAnimationController();
    }
    animationController.SetSpriteObjects(spriteTarget != null ? new[] { spriteTarget.gameObject } : null);
    animationController.SetHBoxObjects(hitboxCollider != null ? new[] { hitboxCollider.gameObject } : null);
    var hboxes = BuildHBoxData(animationKey) ?? EmptyHBoxData;
    animationController.ConfigureData(animationData ?? EmptyAnimations, EmptyInterrupts, null, hboxes);
  }

  private Dictionary<string, Dictionary<string, List<HBox>>> BuildHBoxData(string animationKey) {
    if (hitboxCollider == null || string.IsNullOrEmpty(animationKey)) return null;
    if (!HBoxes.EffectHit.TryGetValue(animationKey, out var sequence) || sequence == null || sequence.Count == 0) {
      return null;
    }
    return new Dictionary<string, Dictionary<string, List<HBox>>> {
      [hitboxCollider.gameObject.name] = new Dictionary<string, List<HBox>> {
        [animationKey] = sequence
      }
    };
  }

  private void ResetLifetime() {
    lifetimeRemaining = lifetimeSeconds > 0f ? lifetimeSeconds : 0f;
  }

  private void UpdateLifetime(float dt) {
    if (lifetimeRemaining <= 0f) return;
    lifetimeRemaining -= dt;
    if (lifetimeRemaining <= 0f) {
      Despawn();
    }
  }

  void OnTriggerEnter2D(Collider2D other) {
    HandleContact(other);
  }

  void OnCollisionEnter2D(Collision2D collision) {
    HandleContact(collision.collider);
  }

  private void HandleContact(Collider2D other) {
    if (!isActiveAndEnabled || other == null) return;
    if (ignoreSameRoot && other.transform.root == transform.root) return;

    var hurtBox = other.GetComponentInParent<HurtBox2D>();
    if (hurtBox != null) {
      if (despawnOnHurtBoxHit) Despawn();
      return;
    }

    if (!despawnOnAnyCollision) return;
    if ((collisionLayers.value & (1 << other.gameObject.layer)) == 0) return;
    Despawn();
  }

  private string ResolveKey(string key) {
    if (!string.IsNullOrEmpty(key)) return NormalizeKey(key);
    return NormalizeKey(name);
  }

  private static string NormalizeKey(string raw) {
    if (string.IsNullOrEmpty(raw)) return raw;
    const string suffix = "(Clone)";
    return raw.EndsWith(suffix) ? raw.Substring(0, raw.Length - suffix.Length) : raw;
  }
}
