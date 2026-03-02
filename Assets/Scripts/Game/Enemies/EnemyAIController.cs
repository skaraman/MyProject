using System.Collections;
using UnityEngine;

[RequireComponent(typeof(EnemyController))]
public class EnemyAIController : MonoBehaviour {
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
  private Rigidbody2D rb;
  private EnemyInfo info;
  private Coroutine aiRoutine;
  private float nextAttackTime;
  private string cachedEnemyType = "";

  void Awake() {
    rb = GetComponent<Rigidbody2D>();
    enemyController ??= GetComponent<EnemyController>();
    info = GetComponent<EnemyInfo>();
    RefreshClosingDistance(force: true);
  }

  void OnEnable() {
    if (aiRoutine != null) StopCoroutine(aiRoutine);
    aiRoutine = StartCoroutine(BehaviourLoop());
  }

  void OnDisable() {
    if (aiRoutine != null) StopCoroutine(aiRoutine);
    aiRoutine = null;
    StopMovement();
  }

  private IEnumerator BehaviourLoop() {
    while (true) {
      if (player == null || enemyController == null) {
        yield return null;
        continue;
      }
      RefreshClosingDistance();

      float distance = Vector2.Distance(transform.position, player.position);
      FacePlayer();

      if (distance <= closingDistance) {
        yield return AttackSequence();
      }
      else {
        yield return RepositionSequence();
      }
      yield return null;
    }
  }

  private IEnumerator AttackSequence() {
    if (Time.time < nextAttackTime) yield break;
    nextAttackTime = Time.time + attackCooldown;

    StopMovement();
    if (enemyController.PlayAnimation("Attack")) {
      float wait = enemyController.GetAnimationDurationSeconds("Attack");
      if (wait <= 0f) wait = 0.5f;
      yield return new WaitForSeconds(wait);
    }
    enemyController.PlayAnimation(enemyController.defaultAnimation);
  }

  private IEnumerator RepositionSequence() {
    var toPlayer = ((Vector2)(player.position - transform.position)).normalized;
    float roll = Random.value;

    if (roll < 0.25f) {
      StopMovement();
      enemyController.PauseAnimation();
      yield return new WaitForSeconds(Random.Range(waitRange.x, waitRange.y));
      enemyController.ResumeAnimation();
      yield break;
    }

    Vector2 moveDir = toPlayer;
    float duration;
    float speedMultiplier = 1f;

    if (roll < 0.55f) {
      moveDir = -toPlayer;
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
      yield break;
    }
    yield return MoveForDuration(moveDir, duration, speedMultiplier);
    enemyController.PlayAnimation(enemyController.defaultAnimation);
  }

  private IEnumerator MoveForDuration(Vector2 direction, float duration, float speedMultiplier) {
    if (direction.sqrMagnitude > 0.001f) {
      enemyController.FaceDirection(direction.x);
    }
    direction = direction.normalized;
    float elapsed = 0f;
    while (elapsed < duration) {
      ApplyMovement(direction, speedMultiplier);
      elapsed += Time.deltaTime;
      yield return null;
    }
    StopMovement();
  }

  private void ApplyMovement(Vector2 dir, float speedMultiplier) {
    Vector2 velocity = dir * moveSpeed * speedMultiplier;
    if (rb != null) {
      rb.linearVelocity = velocity;
    }
    else {
      transform.position += (Vector3)(velocity * Time.deltaTime);
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

  private void RefreshClosingDistance(bool force = false) {
    var resolvedType = ResolveEnemyType();
    if (!force && string.Equals(cachedEnemyType, resolvedType, System.StringComparison.OrdinalIgnoreCase)) return;

    cachedEnemyType = resolvedType;
    closingDistance = ResolveClosingDistanceForType(resolvedType);
  }

  private string ResolveEnemyType() {
    var type = enemyController != null ? enemyController.enemyType : info?.enemyType;
    return string.IsNullOrWhiteSpace(type) ? "" : type.Trim();
  }

  private float ResolveClosingDistanceForType(string type) {
    if (!string.IsNullOrEmpty(type) && AllStatValues.Enemies.TryGetValue(type, out var stats) && stats.TryGetValue("CDST", out var cd)) {
      return cd;
    }
    return fallbackClosingDistance;
  }
}
