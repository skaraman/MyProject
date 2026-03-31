using System.Collections.Generic;
using UnityEngine;

public class Projectile : MonoBehaviour {
  struct AuthoredSettings {
    public MovementType movementType;
    public float speed;
    public bool rotateToMovement;
    public float rotationOffsetDegrees;
    public bool loopAnimation;
    public string effectKeyOverride;
    public float lifetimeSeconds;
    public bool despawnOnHurtBoxHit;
    public bool despawnOnAnyCollision;
    public LayerMask collisionLayers;
    public bool ignoreSameRoot;
  }

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

  [Header("Spawn Offset Tuning")]
  public float runtimeSpawnOffsetX;
  public float runtimeSpawnOffsetY;

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
  private bool authoredSettingsCaptured;
  private AuthoredSettings authoredSettings;
  private float runtimeSpawnDirectionSign = 1f;
  private Vector3 runtimeSpawnBasePosition;
  private Vector3 runtimeSpawnResolvedPosition;
  private bool hasRuntimeSpawnOffsetState;
  private float lastAppliedRuntimeSpawnOffsetX;
  private float lastAppliedRuntimeSpawnOffsetY;
  private static readonly Dictionary<string, AnimData> EmptyAnimations = new();
  private static readonly Dictionary<string, Dictionary<string, string>> EmptyInterrupts = new();
  private static readonly Dictionary<string, Dictionary<string, List<HBox>>> EmptyHBoxData = new();

  public string PoolKey => poolKey;

  void Awake() {
    if (spriteTarget == null) spriteTarget = GetComponentInChildren<SpriteWithNormals>();
    if (hitboxCollider == null) hitboxCollider = GetComponentInChildren<PolygonCollider2D>();
    rb2d = GetComponent<Rigidbody2D>();
    CaptureAuthoredSettings();
    InitializeAnimationController();
  }

  void OnEnable() {
    ResetLifetime();
  }

  void Update() {
    var scaledDeltaTime = TimeScale.GetDeltaTime(this);
    animationController.Tick(scaledDeltaTime);
    UpdateLifetime(scaledDeltaTime);
    TryApplyRuntimeSpawnOffsetTuning("update");
  }

  void FixedUpdate() {
    Move(TimeScale.GetFixedDeltaTime(this));
  }

  void OnDisable() {
    animationController.Cleanup(false);
    lifetimeRemaining = 0f;
  }

  void OnValidate() {
    if (!Application.isPlaying) {
      return;
    }

    TryApplyRuntimeSpawnOffsetTuning("validate");
  }

  public void Launch(ProjectileManager owner, string key, Vector3 direction, Transform target = null, float? speedOverride = null) {
    this.owner = owner;
    poolKey = ResolveKey(key);
    RestoreAuthoredSettings();
    ApplyProjectileData(poolKey);
    effectKey = !string.IsNullOrEmpty(effectKeyOverride) ? NormalizeKey(effectKeyOverride) : poolKey;
    this.target = target;
    this.direction = direction.sqrMagnitude > 0f ? direction.normalized : transform.right;
    ResolveMovementTargets();
    CaptureRuntimeSpawnOffsetState();
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

  void CaptureAuthoredSettings() {
    authoredSettings = new AuthoredSettings {
      movementType = movementType,
      speed = speed,
      rotateToMovement = rotateToMovement,
      rotationOffsetDegrees = rotationOffsetDegrees,
      loopAnimation = loopAnimation,
      effectKeyOverride = effectKeyOverride,
      lifetimeSeconds = lifetimeSeconds,
      despawnOnHurtBoxHit = despawnOnHurtBoxHit,
      despawnOnAnyCollision = despawnOnAnyCollision,
      collisionLayers = collisionLayers,
      ignoreSameRoot = ignoreSameRoot
    };
    authoredSettingsCaptured = true;
  }

  void RestoreAuthoredSettings() {
    if (!authoredSettingsCaptured) {
      CaptureAuthoredSettings();
    }

    movementType = authoredSettings.movementType;
    speed = authoredSettings.speed;
    rotateToMovement = authoredSettings.rotateToMovement;
    rotationOffsetDegrees = authoredSettings.rotationOffsetDegrees;
    loopAnimation = authoredSettings.loopAnimation;
    effectKeyOverride = authoredSettings.effectKeyOverride;
    lifetimeSeconds = authoredSettings.lifetimeSeconds;
    despawnOnHurtBoxHit = authoredSettings.despawnOnHurtBoxHit;
    despawnOnAnyCollision = authoredSettings.despawnOnAnyCollision;
    collisionLayers = authoredSettings.collisionLayers;
    ignoreSameRoot = authoredSettings.ignoreSameRoot;
  }

  void ApplyProjectileData(string key) {
    if (!Projectiles.TryGet(key, out var data) || data == null) {
      return;
    }

    movementType = data.movementType;
    speed = data.speed;
    rotateToMovement = data.rotateToMovement;
    rotationOffsetDegrees = data.rotationOffsetDegrees;
    loopAnimation = data.loopAnimation;
    effectKeyOverride = data.effectKeyOverride;
    lifetimeSeconds = data.lifetimeSeconds;
    despawnOnHurtBoxHit = data.despawnOnHurtBoxHit;
    despawnOnAnyCollision = data.despawnOnAnyCollision;
    collisionLayers = data.collisionLayers;
    ignoreSameRoot = data.ignoreSameRoot;
  }

  void CaptureRuntimeSpawnOffsetState() {
    runtimeSpawnDirectionSign = ResolveRuntimeSpawnDirectionSign();
    if (Projectiles.TryGet(poolKey, out var data) && data != null) {
      runtimeSpawnOffsetX = data.spawnOffsetX;
      runtimeSpawnOffsetY = data.spawnOffsetY;
    }
    else {
      runtimeSpawnOffsetX = 0f;
      runtimeSpawnOffsetY = 0f;
    }

    runtimeSpawnResolvedPosition = transform.position;
    runtimeSpawnBasePosition = runtimeSpawnResolvedPosition - ResolveRuntimeSpawnOffsetVector();
    lastAppliedRuntimeSpawnOffsetX = runtimeSpawnOffsetX;
    lastAppliedRuntimeSpawnOffsetY = runtimeSpawnOffsetY;
    hasRuntimeSpawnOffsetState = true;
  }

  void TryApplyRuntimeSpawnOffsetTuning(string source) {
    if (!hasRuntimeSpawnOffsetState) {
      return;
    }

    if (Mathf.Approximately(lastAppliedRuntimeSpawnOffsetX, runtimeSpawnOffsetX) &&
        Mathf.Approximately(lastAppliedRuntimeSpawnOffsetY, runtimeSpawnOffsetY)) {
      return;
    }

    runtimeSpawnDirectionSign = ResolveRuntimeSpawnDirectionSign();
    var resolvedPosition = runtimeSpawnBasePosition + ResolveRuntimeSpawnOffsetVector();
    if (rb2d != null) {
      rb2d.position = resolvedPosition;
      rb2d.linearVelocity = Vector2.zero;
      rb2d.angularVelocity = 0f;
    }
    else {
      transform.position = resolvedPosition;
    }

    runtimeSpawnResolvedPosition = resolvedPosition;
    lastAppliedRuntimeSpawnOffsetX = runtimeSpawnOffsetX;
    lastAppliedRuntimeSpawnOffsetY = runtimeSpawnOffsetY;
    if (Application.isEditor || Debug.isDebugBuild) {
      Debug.Log(
        "[Projectile] AppliedRuntimeSpawnOffsetTuning" +
        " source=" + source +
        " key='" + poolKey + "'" +
        " base=" + runtimeSpawnBasePosition +
        " authored_offset=(" + runtimeSpawnOffsetX.ToString("0.###") + ", " + runtimeSpawnOffsetY.ToString("0.###") + ")" +
        " resolved=" + runtimeSpawnResolvedPosition
      );
    }
  }

  float ResolveRuntimeSpawnDirectionSign() {
    return direction.x < 0f ? -1f : 1f;
  }

  Vector3 ResolveRuntimeSpawnOffsetVector() {
    return new Vector3(runtimeSpawnOffsetX * runtimeSpawnDirectionSign, runtimeSpawnOffsetY, 0f);
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
