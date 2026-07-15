using UnityEngine;

[RequireComponent(typeof(EnemyController))]
public class EnemyAIController : MonoBehaviour {
  enum BehaviourState {
    Decide,
    Wait,
    Move,
    Attack
  }

  public Transform player;
  public EnemyController enemyController;
  public float moveSpeed = 2.5f;
  public Vector2 waitRange = new Vector2(0.15f, 0.45f);
  public Vector2 backstepRange = new Vector2(0.2f, 0.45f);
  public Vector2 lungeRange = new Vector2(0.3f, 0.65f);
  public Vector2 runStepRange = new Vector2(0.35f, 0.7f);
  public float attackCooldown = 1.1f;
  public float fallbackClosingDistance = 1.5f;

  private float closingDistance;
  private float runtimeMoveSpeed;
  private float runtimeAttackCooldown;
  private float baselineMoveSpeed;
  private float baselineAttackCooldown;
  private Rigidbody2D rb;
  private EnemyInfo info;
  private float nextAttackTime;
  private int cachedSpawnContextVersion = -1;
  private BehaviourState behaviourState;
  private float stateTimeRemaining;
  private Vector2 stateMoveDirection;
  private float stateMoveSpeedMultiplier;

  static bool ShouldLogSpawnDebug() {
    return SpriteStreamingRuntimeSettings.EnableVerboseRuntimeConsoleLogs &&
           (Application.isEditor || Debug.isDebugBuild);
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
    behaviourState = BehaviourState.Decide;
    stateTimeRemaining = 0f;
    enemyController?.ResumeAnimation();
  }

  void OnDisable() {
    StopMovement();
  }

  void Update() {
    if (player == null || enemyController == null) {
      return;
    }

    switch (behaviourState) {
      case BehaviourState.Decide:
        DecideNextBehaviour();
        break;
      case BehaviourState.Wait:
        TickWait();
        break;
      case BehaviourState.Move:
        TickMove();
        break;
      case BehaviourState.Attack:
        TickAttack();
        break;
    }
  }

  void DecideNextBehaviour() {
    RefreshResolvedCombatStats();
    var distance = Vector2.Distance(transform.position, player.position);
    FacePlayer();

    if (distance <= closingDistance) {
      BeginAttack();
      return;
    }

    BeginReposition();
  }

  void BeginAttack() {
    var now = TimeScale.GetNow(this);
    if (now < nextAttackTime) {
      return;
    }

    nextAttackTime = now + runtimeAttackCooldown;

    StopMovement();
    if (!enemyController.PlayAnimation("Attack")) {
      return;
    }

    stateTimeRemaining = enemyController.GetAnimationDurationSeconds("Attack");
    if (stateTimeRemaining <= 0f) {
      stateTimeRemaining = 0.5f;
    }
    behaviourState = BehaviourState.Attack;
  }

  void TickAttack() {
    stateTimeRemaining -= TimeScale.GetDeltaTime(this);
    if (stateTimeRemaining > 0f) {
      return;
    }

    enemyController.PlayAnimation(enemyController.defaultAnimation);
    behaviourState = BehaviourState.Decide;
  }

  void BeginReposition() {
    var toPlayer = ((Vector2)(player.position - transform.position)).normalized;
    var roll = Random.value;

    if (roll < 0.25f) {
      StopMovement();
      enemyController.PauseAnimation();
      stateTimeRemaining = Random.Range(waitRange.x, waitRange.y);
      behaviourState = BehaviourState.Wait;
      return;
    }

    var moveDirection = toPlayer;
    float duration;
    var speedMultiplier = 1f;

    if (roll < 0.55f) {
      moveDirection = -toPlayer;
      duration = Random.Range(backstepRange.x, backstepRange.y);
      speedMultiplier = 0.75f;
    }
    else if (roll < 0.85f) {
      duration = Random.Range(lungeRange.x, lungeRange.y);
      speedMultiplier = 1.6f;
    }
    else {
      duration = Random.Range(runStepRange.x, runStepRange.y);
    }

    if (!enemyController.PlayAnimation("Run")) {
      return;
    }

    if (moveDirection.sqrMagnitude > 0.001f) {
      enemyController.FaceDirection(moveDirection.x);
    }

    stateMoveDirection = moveDirection.normalized;
    stateMoveSpeedMultiplier = speedMultiplier;
    stateTimeRemaining = duration;
    behaviourState = BehaviourState.Move;
  }

  void TickWait() {
    stateTimeRemaining -= TimeScale.GetDeltaTime(this);
    if (stateTimeRemaining > 0f) {
      return;
    }

    enemyController.ResumeAnimation();
    behaviourState = BehaviourState.Decide;
  }

  void TickMove() {
    ApplyMovement(stateMoveDirection, stateMoveSpeedMultiplier);
    stateTimeRemaining -= TimeScale.GetDeltaTime(this);
    if (stateTimeRemaining > 0f) {
      return;
    }

    StopMovement();
    enemyController.PlayAnimation(enemyController.defaultAnimation);
    behaviourState = BehaviourState.Decide;
  }

  private void ApplyMovement(Vector2 dir, float speedMultiplier) {
    Vector2 velocity = dir * runtimeMoveSpeed * speedMultiplier;
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
    var multiplier = info != null ? info.GetResolvedStat("MVSP", 1f) : 1f;
    return baselineMoveSpeed * Mathf.Max(multiplier, 0f);
  }

  private float ResolveRuntimeAttackCooldown() {
    var attackSpeed = info != null ? info.GetResolvedStat("AKSP", 1f) : 1f;
    return baselineAttackCooldown / Mathf.Max(attackSpeed, 0.01f);
  }

  private float ResolveClosingDistance() {
    return info != null ? info.GetResolvedStat("CDST", fallbackClosingDistance) : fallbackClosingDistance;
  }
}
