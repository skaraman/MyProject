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
  private string hitSoundId;
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
  private static readonly Dictionary<string, string[]> EmptyInterrupts = new();
  private static readonly Dictionary<string, Dictionary<string, List<HBox>>> EmptyHBoxData = new();
  private readonly GameObject[] spriteTargetObjects = new GameObject[1];
  private readonly GameObject[] hBoxTargetObjects = new GameObject[1];
  private readonly Dictionary<string, Dictionary<string, List<HBox>>> runtimeHBoxData = new();
  private readonly Dictionary<string, List<HBox>> runtimeHBoxSequences = new();
  private readonly Collider2D[] overlapResults = new Collider2D[128];
  private readonly HashSet<ulong> overlapHurtBoxIds = new();
  private bool hasOverlapScanTimestamp;
  private float lastOverlapScanFixedTime;
  private SpriteWithNormals configuredSpriteTarget;
  private PolygonCollider2D configuredHitboxCollider;
  private HitBox2D offensiveHitBox;
  private ProjectedSpriteShadowCaster2D shadowCaster;
  private EffectLight2D effectLight;
  private int lastHitSoundFrame = -1;

  public string PoolKey => poolKey;

  void Awake() {
    if (spriteTarget == null) spriteTarget = GetComponentInChildren<SpriteWithNormals>();
    if (hitboxCollider == null) hitboxCollider = GetComponentInChildren<PolygonCollider2D>();
    if (hitboxCollider != null) hitboxCollider.TryGetComponent(out offensiveHitBox);
    rb2d = GetComponent<Rigidbody2D>();
    effectLight = GetComponent<EffectLight2D>();
    CaptureAuthoredSettings();
    InitializeAnimationController();
    EnsureShadowCaster();
  }

  void EnsureShadowCaster() {
    shadowCaster = GetComponent<ProjectedSpriteShadowCaster2D>();
    if (shadowCaster == null) {
      shadowCaster = gameObject.AddComponent<ProjectedSpriteShadowCaster2D>();
      shadowCaster.IsGlowMode = true;
      shadowCaster.CastNearestLocalShadow = false;
    } else {
      shadowCaster.IsGlowMode = true;
    }
  }

  void OnEnable() {
    ResetLifetime();
    hasOverlapScanTimestamp = false;
    overlapHurtBoxIds.Clear();
    lastHitSoundFrame = -1;
  }

  void Update() {
    var scaledDeltaTime = TimeScale.GetDeltaTime(this);
    animationController.Tick(scaledDeltaTime);
    TryHitOverlappingHurtBoxes();
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

  public void Launch(
    ProjectileManager owner,
    string key,
    Vector3 direction,
    Transform target = null,
    float? speedOverride = null,
    Transform actorOwner = null
  ) {
    this.owner = owner;
    poolKey = ResolveKey(key);
    RestoreAuthoredSettings();
    hitSoundId = null;
    lastHitSoundFrame = -1;
    offensiveHitBox?.SetActorOwner(actorOwner);
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
    
    if (shadowCaster != null) {
      shadowCaster.ConfigureSources(animationController);
    }

    animationController.ForceLoop = loopAnimation;
    if (!string.IsNullOrEmpty(effectKey)) {
      animationController.PlayAnimation(effectKey, true, resolveInterrupts: false);
    }
  }

  public void Despawn() {
    effectLight?.LeaveLingeringLight();
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
    hitSoundId = data.hitSoundId;
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
      RuntimeLog.Log(
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
    animationController.Initialize(
      transform,
      ResolveSpriteTargetObjects(),
      null,
      ResolveHBoxTargetObjects(),
      EmptyAnimations,
      EmptyInterrupts,
      null,
      EmptyHBoxData,
      "",
      false
    );
    configuredSpriteTarget = spriteTarget;
    configuredHitboxCollider = hitboxCollider;
    animationControllerInitialized = true;
  }

  private void ConfigureAnimationController(string animationKey) {
    if (!animationControllerInitialized) {
      InitializeAnimationController();
    }
    if (configuredSpriteTarget != spriteTarget) {
      animationController.SetSpriteObjects(ResolveSpriteTargetObjects());
      configuredSpriteTarget = spriteTarget;
    }
    if (configuredHitboxCollider != hitboxCollider) {
      animationController.SetHBoxObjects(ResolveHBoxTargetObjects());
      configuredHitboxCollider = hitboxCollider;
    }
    var hboxes = ResolveHBoxData(animationKey);
    animationController.ConfigureData(animationData ?? EmptyAnimations, EmptyInterrupts, null, hboxes);
  }

  GameObject[] ResolveSpriteTargetObjects() {
    if (spriteTarget == null) return null;
    spriteTargetObjects[0] = spriteTarget.gameObject;
    return spriteTargetObjects;
  }

  GameObject[] ResolveHBoxTargetObjects() {
    if (hitboxCollider == null) return null;
    hBoxTargetObjects[0] = hitboxCollider.gameObject;
    return hBoxTargetObjects;
  }

  private Dictionary<string, Dictionary<string, List<HBox>>> ResolveHBoxData(string animationKey) {
    runtimeHBoxData.Clear();
    runtimeHBoxSequences.Clear();
    if (hitboxCollider == null || string.IsNullOrEmpty(animationKey)) return EmptyHBoxData;
    if (!HBoxes.EffectHit.TryGetValue(animationKey, out var sequence) || sequence == null || sequence.Count == 0) {
      return EmptyHBoxData;
    }

    runtimeHBoxSequences[animationKey] = sequence;
    runtimeHBoxData[hitboxCollider.gameObject.name] = runtimeHBoxSequences;
    return runtimeHBoxData;
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
    GlobalCollisionCooldown.TryApply(collision);
    HandleContact(collision.collider);
  }

  private void HandleContact(Collider2D other) {
    if (!isActiveAndEnabled || other == null) return;

    if (HurtBox2D.TryResolve(other, out var hurtBox)) {
      TryAcceptHurtBoxContact(other, hurtBox);
      return;
    }

    if (ignoreSameRoot &&
        (other.transform == transform || other.transform.IsChildOf(transform))) {
      return;
    }
    if (!despawnOnAnyCollision) return;
    if ((collisionLayers.value & (1 << other.gameObject.layer)) == 0) return;
    Despawn();
  }

  private void TryHitOverlappingHurtBoxes() {
    if (offensiveHitBox == null ||
        hitboxCollider == null ||
        !hitboxCollider.enabled) {
      return;
    }

    // Movement is driven by FixedUpdate and trigger callbacks remain
    // immediate. This fallback only needs one read per physics timestamp,
    // rather than repeating the same overlap query on every render frame.
    var fixedTime = Time.fixedTime;
    if (hasOverlapScanTimestamp && Mathf.Approximately(lastOverlapScanFixedTime, fixedTime)) {
      return;
    }
    lastOverlapScanFixedTime = fixedTime;
    hasOverlapScanTimestamp = true;

    var overlapCount = hitboxCollider.Overlap(ContactFilter2D.noFilter, overlapResults);
    var acceptedAny = false;
    overlapHurtBoxIds.Clear();
    for (var i = 0; i < overlapCount; i++) {
      var other = overlapResults[i];
      overlapResults[i] = null;
      if (!HurtBox2D.TryResolve(other, out var hurtBox)) continue;
      var hurtBoxId = ObjectEntityId.GetRawValue(hurtBox);
      if (!overlapHurtBoxIds.Add(hurtBoxId)) {
        continue;
      }
      if (TryAcceptHurtBoxContact(other, hurtBox, deferDespawn: true)) {
        acceptedAny = true;
      }
    }
    overlapHurtBoxIds.Clear();

    if (acceptedAny && despawnOnHurtBoxHit) {
      Despawn();
    }
  }

  private bool TryAcceptHurtBoxContact(
    Collider2D other,
    HurtBox2D hurtBox,
    bool deferDespawn = false
  ) {
    if (offensiveHitBox == null || other == null || hurtBox == null) return false;

    var accepted = offensiveHitBox.TryHit(other);
    var acceptedEarlierThisFrame = hurtBox.LastHitBox == offensiveHitBox &&
                                   hurtBox.LastHitFrame == Time.frameCount;
    if (!accepted && !acceptedEarlierThisFrame) return false;

    if (!string.IsNullOrWhiteSpace(hitSoundId) && lastHitSoundFrame != Time.frameCount) {
      SoundEffectPlayer.Play(hitSoundId);
      lastHitSoundFrame = Time.frameCount;
    }
    if (despawnOnHurtBoxHit && !deferDespawn) {
      Despawn();
    }
    return true;
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
