using UnityEngine;

[RequireComponent(typeof(EnemyController))]
public class EnemyAIController : MonoBehaviour {
  const float PlayerTargetRefreshSeconds = 0.5f;
  const float ExactOverlapThreshold = 0.0001f;
  const float MaximumComboJuggleRepositionSeconds = 0.25f;
  const float MinimumComboJuggleRepositionSeconds = 0.01f;
  static readonly System.Collections.Generic.List<EnemyAIController> activeEnemies = new();
  static int lastEnemySortFrame = -1;
  static bool s_UpdateRegistered;
  static readonly System.Action s_UpdateCallback = UpdateAll;
  static readonly System.Comparison<EnemyAIController> s_PositionComparison =
    CompareCachedPosition;

  static void UpdateAll() {
    for (var i = activeEnemies.Count - 1; i >= 0; i--) {
      var target = activeEnemies[i];
      if (target == null) {
        activeEnemies.RemoveAt(i);
        continue;
      }
      target.cachedPosition = target.transform.position;
    }
    EnsureEnemiesSortedForFrame();
    var remaining = activeEnemies.Count;
    var index = 0;
    while (index < activeEnemies.Count && remaining-- > 0) {
      var target = activeEnemies[index];
      target.ManagedUpdate();
      if (index < activeEnemies.Count && activeEnemies[index] == target) {
        index++;
      }
    }
  }

  static void EnsureUpdateRegistration() {
    if (s_UpdateRegistered || !Application.isPlaying) return;
    s_UpdateRegistered = true;
    RuntimeUpdateHub.Register(
      300,
      "RuntimeUpdateHub.EnemyAI",
      s_UpdateCallback
    );
  }

  static void EnsureEnemiesSortedForFrame() {
    var frame = Time.frameCount;
    if (lastEnemySortFrame == frame) return;
    lastEnemySortFrame = frame;
    activeEnemies.Sort(s_PositionComparison);
    for (int i = 0; i < activeEnemies.Count; i++) {
      activeEnemies[i].sortedIndex = i;
    }
  }

  static int CompareCachedPosition(EnemyAIController left, EnemyAIController right) {
    if (left == null) return right == null ? 0 : 1;
    if (right == null) return -1;
    return left.cachedPosition.x.CompareTo(right.cachedPosition.x);
  }

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
    Hurt,
    Juggle
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
  private float runtimeJuggleMoveSpeed;
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
  private Vector2 juggleTargetPosition;
  private bool gameplayPursuitReleased;
  private int sortedIndex = -1;

  static bool ShouldLogSpawnDebug() {
    return SpriteStreamingRuntimeSettings.EnableVerboseRuntimeConsoleLogs &&
           (Application.isEditor || Debug.isDebugBuild);
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetActiveEnemies() {
    activeEnemies.Clear();
    lastEnemySortFrame = -1;
    cameraCenter = default;
    cameraCullingSqrRadius = 0f;
    lastCameraUpdateFrame = -1;
    hasCameraBounds = false;
    s_UpdateRegistered = false;
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
    if (!Application.isPlaying) return;
    cachedPosition = transform.position;
    if (!activeEnemies.Contains(this)) {
      activeEnemies.Add(this);
    }
    EnsureUpdateRegistration();
    behaviourState = BehaviourState.Decide;
    gameplayPursuitReleased = false;
    stateTimeRemaining = 0f;
    nextAttackTime = 0f;
    ClearActiveAttack();
    TryResolvePlayer(force: true);
    enemyController?.ResumeAnimation();
  }

  void OnDisable() {
    if (Application.isPlaying) {
      activeEnemies.Remove(this);
    }
    ClearActiveAttack();
    StopMovement();
    gameplayPursuitReleased = false;
  }

  private Vector2 cachedPosition;

  static Vector2 cameraCenter;
  static float cameraCullingSqrRadius;
  static int lastCameraUpdateFrame = -1;
  static bool hasCameraBounds;

  static void UpdateCameraBounds() {
    if (lastCameraUpdateFrame == Time.frameCount) return;
    lastCameraUpdateFrame = Time.frameCount;
    var cam = Camera.main;
    if (cam == null) {
      hasCameraBounds = false;
      return;
    }
    hasCameraBounds = true;
    cameraCenter = cam.transform.position;
    var radius = Mathf.Max(cam.orthographicSize * cam.aspect, cam.orthographicSize) + 8f;
    cameraCullingSqrRadius = radius * radius;
  }

  internal void ManagedUpdate() {
    UpdateCameraBounds();
    var isCulled =
      hasCameraBounds &&
      (cachedPosition - cameraCenter).sqrMagnitude > cameraCullingSqrRadius;
    if (enemyController != null) {
      enemyController.isCulled = isCulled;
    }

    if (enemyController == null) {
      return;
    }

    if (!SingleSceneManager.IsBlackscreenFullyTransparent ||
        SingleSceneManager.IsGameplayDialogActive) {
      if (gameplayPursuitReleased) {
        StopMovement();
      }
      gameplayPursuitReleased = false;
      return;
    }

    if (!TryResolvePlayer(force: !gameplayPursuitReleased)) {
      return;
    }

    if (!gameplayPursuitReleased) {
      gameplayPursuitReleased = true;
      StopMovement();
      ClearActiveAttack();
      behaviourState = BehaviourState.Decide;
      stateTimeRemaining = 0f;
      nextAttackTime = 0f;
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
      case BehaviourState.Juggle:
        TickJuggle();
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

    if (string.Equals(ResolveEnemyType(), "Imp", System.StringComparison.OrdinalIgnoreCase)) {
      if (UnityEngine.Random.value > 0.5f) {
        SoundEffectPlayer.Play("enemy.imp.attack");
      }
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

  public bool TryBeginComboJuggle(
    HitBox2D hitBox,
    HurtBox2D hurtBox,
    string upcomingMove,
    float holdSeconds
  ) {
    var attacker = hitBox != null ? hitBox.ActorOwner : null;
    if (!isActiveAndEnabled || enemyController == null || attacker == null || hurtBox == null) {
      return false;
    }

    var hurtCollider = hurtBox.GetComponent<Collider2D>();
    var hurtBoxCenter = hurtCollider != null
      ? (Vector2)hurtCollider.bounds.center
      : (Vector2)hurtBox.transform.position;
    if (!TryResolveUpcomingHitBoxLocalCenter(
          upcomingMove,
          out var upcomingHitBoxCenter,
          out var authoredStrikeSeconds
        )) {
      return false;
    }

    StopMovement();
    ClearActiveAttack();
    nextAttackTime = Mathf.Max(nextAttackTime, TimeScale.GetNow(this) + runtimeAttackCooldown);
    if (!enemyController.PlayAnimation("Hurt", forceRestart: true)) {
      return false;
    }

    var enemySideSign = ResolveEnemySideSign(attacker, hurtBoxCenter.x);
    var attackerScale = attacker.lossyScale;
    // Offensive paths are authored to ESPER's right. Mirror the prediction to
    // the enemy's current side so a combo never pulls it through the player.
    var predictedHitPosition = new Vector2(
      attacker.position.x + enemySideSign * Mathf.Abs(upcomingHitBoxCenter.x * attackerScale.x),
      attacker.position.y + upcomingHitBoxCenter.y * Mathf.Abs(attackerScale.y)
    );

    var currentPosition = rb != null ? rb.position : (Vector2)transform.position;
    var offsetToPredictedHit = predictedHitPosition - hurtBoxCenter;
    juggleTargetPosition = currentPosition + offsetToPredictedHit;

    var distance = Vector2.Distance(currentPosition, juggleTargetPosition);
    var repositionSeconds = ResolveComboJuggleRepositionSeconds(
      upcomingMove,
      authoredStrikeSeconds
    );
    runtimeJuggleMoveSpeed = distance / repositionSeconds;

    enemyController.FaceDirection(-enemySideSign);
    enemyController.PauseAnimation(applyCurrentFrame: true);
    stateTimeRemaining = Mathf.Max(holdSeconds, 0.05f);
    behaviourState = BehaviourState.Juggle;
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

  void TickJuggle() {
    StopMovement();
    var deltaTime = TimeScale.GetDeltaTime(this);
    var currentPosition = rb != null ? rb.position : (Vector2)transform.position;
    var nextPosition = Vector2.MoveTowards(
      currentPosition,
      juggleTargetPosition,
      runtimeJuggleMoveSpeed * deltaTime
    );
    if (rb != null) {
      rb.MovePosition(nextPosition);
    } else {
      transform.position = new Vector3(nextPosition.x, nextPosition.y, transform.position.z);
    }

    stateTimeRemaining -= deltaTime;
    if (stateTimeRemaining > 0f) return;

    enemyController.PlayAnimation(enemyController.defaultAnimation, forceRestart: true);
    var recoveryTimeRemaining = Mathf.Max(0f, nextAttackTime - TimeScale.GetNow(this));
    if (recoveryTimeRemaining > 0f) {
      BeginAttackRecovery(recoveryTimeRemaining);
      return;
    }
    behaviourState = BehaviourState.Decide;
  }

  float ResolveEnemySideSign(Transform attacker, float enemyCenterX) {
    var horizontalDelta = enemyCenterX - attacker.position.x;
    if (Mathf.Abs(horizontalDelta) > ExactOverlapThreshold) {
      return Mathf.Sign(horizontalDelta);
    }

    var attackerController = attacker.GetComponentInParent<GearController>();
    if (attackerController != null) {
      return attackerController.IsFacingRight ? 1f : -1f;
    }
    return attacker.lossyScale.x < 0f ? -1f : 1f;
  }

  static bool TryResolveUpcomingHitBoxLocalCenter(
    string upcomingMove,
    out Vector2 center,
    out float authoredStrikeSeconds
  ) {
    center = default;
    authoredStrikeSeconds = 0f;
    if (!EsperanzaAbilities.TryResolveAbilityAnimation(upcomingMove, out var animationName) ||
        !HBoxes.EsperanzaHit1.TryGetValue(animationName, out var sequence) ||
        sequence == null) {
      return false;
    }

    var foundStrike = false;
    var furthestCenter = Vector2.zero;
    var elapsedSeconds = 0f;
    for (var frameIndex = 0; frameIndex < sequence.Count; frameIndex++) {
      var frame = sequence[frameIndex];
      elapsedSeconds += frame != null && frame.d > 0f ? frame.d : 0.2f;
      var points = frame?.points;
      if (points == null || points.Count == 0) continue;

      var minimum = points[0];
      var maximum = points[0];
      for (var pointIndex = 1; pointIndex < points.Count; pointIndex++) {
        minimum = Vector2.Min(minimum, points[pointIndex]);
        maximum = Vector2.Max(maximum, points[pointIndex]);
      }

      if ((maximum - minimum).sqrMagnitude <= ExactOverlapThreshold) continue;

      var candidate = (minimum + maximum) * 0.5f;
      // Zero-area frames turn the hit box off. Use the farthest real strike so
      // the correction does not pull the enemy unnecessarily close to ESPER.
      if (!foundStrike || candidate.x > furthestCenter.x) {
        foundStrike = true;
        furthestCenter = candidate;
        authoredStrikeSeconds = elapsedSeconds;
      }
    }

    center = furthestCenter;
    return foundStrike;
  }

  static float ResolveComboJuggleRepositionSeconds(
    string upcomingMove,
    float authoredStrikeSeconds
  ) {
    var durationScale = 1f;
    if (EsperanzaAbilities.TryResolveAbilityAnimation(upcomingMove, out var animationName) &&
        Animations.Esperanza.TryGetValue(animationName, out var animation) &&
        animation != null &&
        animation.duration > 0f) {
      var baseDurationSeconds = animation.duration / 1000f;
      var attackSpeedSeconds = AttackSpeedTiming.ResolveStatSeconds(AllStatValues.Esperanza);
      var playbackDurationSeconds = AttackSpeedTiming.ResolveMoveDurationSeconds(
        baseDurationSeconds,
        attackSpeedSeconds
      );
      durationScale = playbackDurationSeconds / baseDurationSeconds;
    }

    return Mathf.Clamp(
      authoredStrikeSeconds * durationScale,
      MinimumComboJuggleRepositionSeconds,
      MaximumComboJuggleRepositionSeconds
    );
  }

  void BeginApproach() {
    var moveDirection = ResolveApproachDirection();
    if (moveDirection.sqrMagnitude <= 0.001f) {
      StopMovement();
      return;
    }

    var runAlreadyActive = string.Equals(
      enemyController.CurrentAnimation,
      "Run",
      System.StringComparison.OrdinalIgnoreCase
    );
    if (!runAlreadyActive && !enemyController.PlayAnimation("Run")) {
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

    EnsureEnemiesSortedForFrame();

    var position = cachedPosition;
    var spacing = preferredEnemySpacing;
    var spacingSqr = spacing * spacing;
    var combinedDirection = Vector2.zero;

    var ownIndex = sortedIndex;
    if (ownIndex < 0 || ownIndex >= activeEnemies.Count || activeEnemies[ownIndex] != this) return Vector2.zero;

    // Search left
    for (var i = ownIndex - 1; i >= 0; i--) {
      var other = activeEnemies[i];
      if (other == null || !other.isActiveAndEnabled) continue;
      var dx = position.x - other.cachedPosition.x;
      if (dx >= spacing) break;

      var dy = position.y - other.cachedPosition.y;
      if (Mathf.Abs(dy) >= spacing) continue;

      var awayFromOther = new Vector2(dx, dy);
      var distanceSqr = awayFromOther.sqrMagnitude;
      if (distanceSqr >= spacingSqr) continue;

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

    // Search right
    for (var i = ownIndex + 1; i < activeEnemies.Count; i++) {
      var other = activeEnemies[i];
      if (other == null || !other.isActiveAndEnabled) continue;
      var dx = other.cachedPosition.x - position.x;
      if (dx >= spacing) break;

      var dy = position.y - other.cachedPosition.y;
      if (Mathf.Abs(dy) >= spacing) continue;

      var awayFromOther = new Vector2(-dx, dy);
      var distanceSqr = awayFromOther.sqrMagnitude;
      if (distanceSqr >= spacingSqr) continue;

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
