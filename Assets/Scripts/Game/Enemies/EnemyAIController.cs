using UnityEngine;

[RequireComponent(typeof(EnemyController))]
public class EnemyAIController : MonoBehaviour {
  const float PlayerTargetRefreshSeconds = 0.5f;
  const float ExactOverlapThreshold = 0.0001f;
  static readonly System.Collections.Generic.List<EnemyAIController> activeEnemies = new();
  static readonly EnemyAttackDefinition LegacyAttack = new(
    "Attack",
    minimumDistance: 0f,
    maximumDistance: float.MaxValue
  );

  enum BehaviourState {
    Decide,
    Move,
    Attack,
    Recover,
    Hurt
  }

  public Transform player;
  public EnemyController enemyController;
  public float moveSpeed = 2.5f;
  public Vector2 approachStepRange = new Vector2(0.35f, 0.7f);
  [Tooltip("Seconds of recovery after an attack finishes before another attack can begin.")]
  public float attackCooldown = 1.1f;
  public float fallbackClosingDistance = 1.5f;

  [Header("Post-Attack Positioning")]
  public Vector2 frontStagingDistanceRange = new Vector2(1.35f, 2.1f);
  [Min(0f)] public float frontStagingVerticalLeeway = 0.55f;
  [Min(0f)] public float frontStagingArrivalRadius = 0.35f;
  [Min(0f)] public float maximumFrontStagingSeconds = 2.25f;
  [Min(0f)] public float frontStagingSpeedMultiplier = 0.8f;

  [Header("Enemy Spacing")]
  [Min(0f)] public float preferredEnemySpacing = 1.25f;
  [Range(0f, 1f)] public float separationInfluence = 0.75f;
  [Min(0f)] public float recoverySeparationSpeedMultiplier = 0.65f;

  private float closingDistance;
  private float runtimeMoveSpeed;
  private float runtimeAttackCooldown;
  private float baselineMoveSpeed;
  private float baselineAttackCooldown;
  private Rigidbody2D rb;
  private EnemyInfo info;
  private GearController playerController;
  private float nextAttackTime;
  private float nextPlayerTargetRefreshAt;
  private int cachedSpawnContextVersion = -1;
  private BehaviourState behaviourState;
  private float stateTimeRemaining;
  private Vector2 stateMoveDirection;
  private EnemyAttackDefinition activeAttack;
  private Vector2 attackStartPosition;
  private Vector2 attackLandingPosition;
  private float activeAttackDuration;
  private float activeAttackElapsed;
  private float recoveryFrontDistance;
  private float recoveryVerticalOffset;
  private float recoveryFacingSign = 1f;
  private float recoveryRepositionTimeRemaining;

  static bool ShouldLogSpawnDebug() {
    return SpriteStreamingRuntimeSettings.EnableVerboseRuntimeConsoleLogs &&
           (Application.isEditor || Debug.isDebugBuild);
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetActiveEnemies() {
    activeEnemies.Clear();
  }

  void Awake() {
    baselineMoveSpeed = moveSpeed;
    baselineAttackCooldown = attackCooldown;
    rb = GetComponent<Rigidbody2D>();
    enemyController ??= GetComponent<EnemyController>();
    info = GetComponent<EnemyInfo>();
    RefreshResolvedCombatStats(force: true);
  }

  void OnEnable() {
    cachedPosition = transform.position;
    if (!activeEnemies.Contains(this)) {
      activeEnemies.Add(this);
    }
    behaviourState = BehaviourState.Decide;
    stateTimeRemaining = 0f;
    nextAttackTime = 0f;
    ClearActiveAttack();
    TryResolvePlayer(force: true);
    enemyController?.ResumeAnimation();
  }

  void OnDisable() {
    activeEnemies.Remove(this);
    ClearActiveAttack();
    StopMovement();
  }

  private Vector2 cachedPosition;

  void Update() {
    cachedPosition = transform.position;
    if (enemyController == null || !TryResolvePlayer()) {
      return;
    }

    switch (behaviourState) {
      case BehaviourState.Decide:
        DecideNextBehaviour();
        break;
      case BehaviourState.Move:
        TickMove();
        break;
      case BehaviourState.Attack:
        TickAttack();
        break;
      case BehaviourState.Recover:
        TickRecovery();
        break;
      case BehaviourState.Hurt:
        TickHurt();
        break;
    }
  }

  void DecideNextBehaviour() {
    RefreshResolvedCombatStats();
    var distance = Vector2.Distance(transform.position, player.position);
    FacePlayer();

    var enemyType = ResolveEnemyType();
    if (EnemyAttacks.TrySelectForDistance(enemyType, distance, out var attack)) {
      BeginAttack(attack);
      return;
    }

    if (!EnemyAttacks.HasDefinitions(enemyType) && distance <= closingDistance) {
      BeginAttack(LegacyAttack);
      return;
    }

    BeginApproach();
  }

  void BeginAttack(EnemyAttackDefinition attack) {
    if (attack == null || string.IsNullOrWhiteSpace(attack.AnimationName)) {
      return;
    }

    var now = TimeScale.GetNow(this);
    if (now < nextAttackTime) {
      return;
    }

    StopMovement();
    if (!enemyController.PlayAnimation(attack.AnimationName, forceRestart: true)) {
      return;
    }

    activeAttack = attack;
    attackStartPosition = rb != null ? rb.position : (Vector2)transform.position;
    attackLandingPosition = attack.LandAtTargetSnapshot && player != null
      ? (Vector2)player.position
      : attackStartPosition;
    activeAttackElapsed = 0f;
    stateTimeRemaining = enemyController.GetAnimationDurationSeconds(attack.AnimationName);
    if (stateTimeRemaining <= 0f) {
      stateTimeRemaining = 0.5f;
    }
    activeAttackDuration = stateTimeRemaining;
    behaviourState = BehaviourState.Attack;
  }

  void TickAttack() {
    var deltaTime = TimeScale.GetDeltaTime(this);
    activeAttackElapsed += deltaTime;
    stateTimeRemaining = Mathf.Max(0f, activeAttackDuration - activeAttackElapsed);

    if (activeAttack != null && activeAttack.LandAtTargetSnapshot) {
      MoveAttackTowardSnapshot(activeAttackElapsed / Mathf.Max(activeAttackDuration, 0.0001f));
    }

    if (stateTimeRemaining > 0f) {
      return;
    }

    ClearActiveAttack();
    StopMovement();
    enemyController.PlayAnimation(enemyController.defaultAnimation, forceRestart: true);
    BeginAttackRecovery(runtimeAttackCooldown);
  }

  void MoveAttackTowardSnapshot(float normalizedTime) {
    var t = Mathf.Clamp01(normalizedTime);
    var easedTime = t * t * (3f - (2f * t));
    var desiredPosition = Vector2.Lerp(attackStartPosition, attackLandingPosition, easedTime);

    if (rb != null) {
      rb.MovePosition(desiredPosition);
      return;
    }

    transform.position = new Vector3(
      desiredPosition.x,
      desiredPosition.y,
      transform.position.z
    );
  }

  public bool TryPlayHurtReaction() {
    if (!isActiveAndEnabled || enemyController == null) {
      return false;
    }

    var interruptedAttack = activeAttack != null;
    StopMovement();
    ClearActiveAttack();
    if (interruptedAttack) {
      nextAttackTime = Mathf.Max(nextAttackTime, TimeScale.GetNow(this) + runtimeAttackCooldown);
    }
    if (!enemyController.PlayAnimation("Hurt", forceRestart: true)) {
      return false;
    }

    stateTimeRemaining = enemyController.GetAnimationDurationSeconds("Hurt");
    if (stateTimeRemaining <= 0f) {
      stateTimeRemaining = 0.175f;
    }
    behaviourState = BehaviourState.Hurt;
    return true;
  }

  void TickHurt() {
    StopMovement();
    stateTimeRemaining -= TimeScale.GetDeltaTime(this);
    if (stateTimeRemaining > 0f) {
      return;
    }

    enemyController.PlayAnimation(enemyController.defaultAnimation, forceRestart: true);
    var recoveryTimeRemaining = Mathf.Max(0f, nextAttackTime - TimeScale.GetNow(this));
    if (recoveryTimeRemaining > 0f) {
      BeginAttackRecovery(recoveryTimeRemaining);
      return;
    }

    behaviourState = BehaviourState.Decide;
  }

  void BeginApproach() {
    var moveDirection = ResolveApproachDirection();
    if (moveDirection.sqrMagnitude <= 0.001f) {
      StopMovement();
      return;
    }

    if (!enemyController.PlayAnimation("Run")) {
      return;
    }

    enemyController.FaceDirection(moveDirection.x);

    stateMoveDirection = moveDirection;
    stateTimeRemaining = Random.Range(approachStepRange.x, approachStepRange.y);
    behaviourState = BehaviourState.Move;
  }

  void TickMove() {
    var updatedDirection = ResolveApproachDirection();
    if (updatedDirection.sqrMagnitude > 0.001f) {
      stateMoveDirection = updatedDirection;
      enemyController.FaceDirection(stateMoveDirection.x);
    }
    ApplyMovement(stateMoveDirection);
    stateTimeRemaining -= TimeScale.GetDeltaTime(this);
    if (stateTimeRemaining > 0f) {
      return;
    }

    StopMovement();
    enemyController.PlayAnimation(enemyController.defaultAnimation);
    behaviourState = BehaviourState.Decide;
  }

  void BeginAttackRecovery(float durationSeconds) {
    stateTimeRemaining = Mathf.Max(0f, durationSeconds);
    nextAttackTime = TimeScale.GetNow(this) + stateTimeRemaining;
    var minimumFrontDistance = Mathf.Max(
      0f,
      Mathf.Min(frontStagingDistanceRange.x, frontStagingDistanceRange.y)
    );
    var maximumFrontDistance = Mathf.Max(
      minimumFrontDistance,
      Mathf.Max(frontStagingDistanceRange.x, frontStagingDistanceRange.y)
    );
    recoveryFrontDistance = Random.Range(
      minimumFrontDistance,
      maximumFrontDistance
    );
    recoveryVerticalOffset = Random.Range(
      -frontStagingVerticalLeeway,
      frontStagingVerticalLeeway
    );
    recoveryFacingSign = ResolvePlayerFacingSign();
    recoveryRepositionTimeRemaining = Mathf.Max(
      stateTimeRemaining,
      maximumFrontStagingSeconds
    );
    behaviourState = recoveryRepositionTimeRemaining > 0f
      ? BehaviourState.Recover
      : BehaviourState.Decide;
  }

  void TickRecovery() {
    var recoveryDirection = ResolveRecoveryDirection(
      out var reachedFrontStagingPoint,
      out var separationPressure
    );
    if (recoveryDirection.sqrMagnitude > 0.001f) {
      FacePlayer();
      var speedMultiplier = reachedFrontStagingPoint
        ? separationPressure * recoverySeparationSpeedMultiplier
        : frontStagingSpeedMultiplier;
      ApplyMovement(recoveryDirection * speedMultiplier);
    }
    else {
      StopMovement();
    }

    var deltaTime = TimeScale.GetDeltaTime(this);
    stateTimeRemaining -= deltaTime;
    recoveryRepositionTimeRemaining -= deltaTime;
    if (stateTimeRemaining > 0f) {
      return;
    }
    if (!reachedFrontStagingPoint && recoveryRepositionTimeRemaining > 0f) {
      return;
    }

    StopMovement();
    behaviourState = BehaviourState.Decide;
  }

  Vector2 ResolveRecoveryDirection(
    out bool reachedFrontStagingPoint,
    out float separationPressure
  ) {
    var targetPosition = player != null
      ? (Vector2)player.position + new Vector2(
        recoveryFacingSign * recoveryFrontDistance,
        recoveryVerticalOffset
      )
      : (Vector2)transform.position;
    var toTarget = targetPosition - (Vector2)transform.position;
    var arrivalRadius = Mathf.Max(0f, frontStagingArrivalRadius);
    reachedFrontStagingPoint = toTarget.sqrMagnitude <= arrivalRadius * arrivalRadius;
    var stagingDirection = reachedFrontStagingPoint
      ? Vector2.zero
      : toTarget.normalized;

    var separationDirection = ResolveSeparationDirection(out separationPressure);
    if (separationDirection.sqrMagnitude <= 0.001f || separationPressure <= 0f) {
      return stagingDirection;
    }
    if (stagingDirection.sqrMagnitude <= 0.001f) {
      return separationDirection;
    }

    var separationBlend = Mathf.Clamp01(separationPressure * separationInfluence);
    var blendedDirection = Vector2.Lerp(
      stagingDirection,
      separationDirection,
      separationBlend
    );
    return blendedDirection.sqrMagnitude > 0.001f
      ? blendedDirection.normalized
      : separationDirection;
  }

  Vector2 ResolveApproachDirection() {
    var toPlayer = player != null
      ? ((Vector2)(player.position - transform.position)).normalized
      : Vector2.zero;
    var separationDirection = ResolveSeparationDirection(out var separationPressure);
    if (separationDirection.sqrMagnitude <= 0.001f || separationPressure <= 0f) {
      return toPlayer;
    }

    var separationBlend = Mathf.Clamp01(separationPressure * separationInfluence);
    var blendedDirection = Vector2.Lerp(toPlayer, separationDirection, separationBlend);
    return blendedDirection.sqrMagnitude > 0.001f
      ? blendedDirection.normalized
      : separationDirection;
  }

  Vector2 ResolveSeparationDirection(out float pressure) {
    pressure = 0f;
    if (preferredEnemySpacing <= 0f || activeEnemies.Count <= 1) {
      return Vector2.zero;
    }

    var position = cachedPosition;
    var spacing = preferredEnemySpacing;
    var spacingSqr = spacing * spacing;
    var combinedDirection = Vector2.zero;
    for (var i = 0; i < activeEnemies.Count; i++) {
      var other = activeEnemies[i];
      if (other == null || other == this || !other.isActiveAndEnabled) {
        continue;
      }

      var dx = position.x - other.cachedPosition.x;
      var dy = position.y - other.cachedPosition.y;
      if (Mathf.Abs(dx) >= spacing || Mathf.Abs(dy) >= spacing) {
        continue;
      }

      var awayFromOther = new Vector2(dx, dy);
      var distanceSqr = awayFromOther.sqrMagnitude;
      if (distanceSqr >= spacingSqr) {
        continue;
      }

      Vector2 direction;
      float distance;
      if (distanceSqr <= ExactOverlapThreshold) {
        direction = ResolveExactOverlapDirection(other);
        distance = 0f;
      }
      else {
        distance = Mathf.Sqrt(distanceSqr);
        direction = awayFromOther / distance;
      }

      var neighborPressure = 1f - Mathf.Clamp01(distance / preferredEnemySpacing);
      combinedDirection += direction * neighborPressure;
      pressure = Mathf.Max(pressure, neighborPressure);
    }

    return combinedDirection.sqrMagnitude > 0.001f
      ? combinedDirection.normalized
      : Vector2.zero;
  }

  Vector2 ResolveExactOverlapDirection(EnemyAIController other) {
    var ownId = ObjectEntityId.GetRawValue(this);
    var otherId = ObjectEntityId.GetRawValue(other);
    var lowId = System.Math.Min(ownId, otherId);
    var highId = System.Math.Max(ownId, otherId);
    var pairHash = (lowId * 397UL) ^ highId;
    var pairDirection = new Vector2(
      (pairHash & 1UL) == 0UL ? 1f : -1f,
      (pairHash & 2UL) == 0UL ? 0.35f : -0.35f
    ).normalized;
    return ownId <= otherId ? pairDirection : -pairDirection;
  }

  private void ApplyMovement(Vector2 dir) {
    Vector2 velocity = dir * runtimeMoveSpeed;
    if (rb != null) {
      rb.linearVelocity = velocity * TimeScale.GetEffectiveFactor(this);
    }
    else {
      transform.position += (Vector3)(velocity * TimeScale.GetDeltaTime(this));
    }
  }

  private void StopMovement() {
    if (rb != null) {
      rb.linearVelocity = Vector2.zero;
    }
  }

  private void ClearActiveAttack() {
    activeAttack = null;
    attackStartPosition = default;
    attackLandingPosition = default;
    activeAttackDuration = 0f;
    activeAttackElapsed = 0f;
  }

  private bool TryResolvePlayer(bool force = false) {
    if (player != null && player.gameObject.activeInHierarchy) {
      playerController ??= player.GetComponentInParent<GearController>();
      return true;
    }

    var now = Time.unscaledTime;
    if (!force && now < nextPlayerTargetRefreshAt) {
      return false;
    }

    nextPlayerTargetRefreshAt = now + PlayerTargetRefreshSeconds;
    playerController = SingleSceneManager.ResolveGameplayPlayerController();
    player = playerController != null ? playerController.transform : null;
    return player != null && player.gameObject.activeInHierarchy;
  }

  float ResolvePlayerFacingSign() {
    if (playerController != null) {
      return playerController.IsFacingRight ? 1f : -1f;
    }
    if (player != null && player.localScale.x < 0f) {
      return -1f;
    }
    return 1f;
  }

  private void FacePlayer() {
    if (player == null || enemyController == null) return;
    var delta = player.position.x - transform.position.x;
    enemyController.FaceDirection(delta);
  }

  public void RefreshResolvedCombatStats(bool force = false) {
    var contextVersion = info != null ? info.SpawnContextVersion : -1;
    if (!force && cachedSpawnContextVersion == contextVersion) return;

    cachedSpawnContextVersion = contextVersion;
    runtimeMoveSpeed = ResolveRuntimeMoveSpeed();
    runtimeAttackCooldown = ResolveRuntimeAttackCooldown();
    closingDistance = ResolveClosingDistance();

    if (ShouldLogSpawnDebug()) {
      RuntimeLog.Log(
        "[EnemyAIController][RefreshResolvedCombatStats]" +
        " object='" + gameObject.name + "'" +
        " enemy_type='" + ResolveEnemyType() + "'" +
        " move_speed=" + runtimeMoveSpeed.ToString("0.###") +
        " attack_cooldown=" + runtimeAttackCooldown.ToString("0.###") +
        " closing_distance=" + closingDistance.ToString("0.###")
      );
    }
  }

  private string ResolveEnemyType() {
    var type = enemyController != null ? enemyController.enemyType : info?.enemyType;
    return string.IsNullOrWhiteSpace(type) ? "" : type.Trim();
  }

  private float ResolveRuntimeMoveSpeed() {
    var multiplier = info != null ? info.GetResolvedEngineStat("MVSP", 1f) : 1f;
    return baselineMoveSpeed * Mathf.Max(multiplier, 0f);
  }

  private float ResolveRuntimeAttackCooldown() {
    var attackSpeed = info != null ? info.GetResolvedEngineStat("AKSP", 1f) : 1f;
    return baselineAttackCooldown / Mathf.Max(attackSpeed, 0.01f);
  }

  private float ResolveClosingDistance() {
    return info != null ? info.GetResolvedEngineStat("CDST", fallbackClosingDistance) : fallbackClosingDistance;
  }
}
