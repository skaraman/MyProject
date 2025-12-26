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
  private const int maxAttackChainDepth = 3;

  void Awake() {
    rb = GetComponent<Rigidbody2D>();
    enemyController ??= GetComponent<EnemyController>();
    info = GetComponent<EnemyInfo>();
    closingDistance = ResolveClosingDistance();
    moveSpeed = ResolveMoveSpeed();
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

      float distance = Vector2.Distance(transform.position, player.position);
      FacePlayer();

      if (distance <= closingDistance) {
        yield return AttackSequence();
        yield return PostAttackAction(0);
      }
      else {
        yield return ApproachPlayer();
      }
      yield return null;
    }
  }

  private IEnumerator AttackSequence() {
    if (Time.time < nextAttackTime) yield break;
    nextAttackTime = Time.time + attackCooldown;

    StopMovement();
    
    // Use random attack animation if multiple exist, otherwise use "Attack"
    string attackAnim = "Attack";
    if (enemyController.PlayAnimation(attackAnim)) {
      float wait = enemyController.GetAnimationDurationSeconds(attackAnim);
      if (wait <= 0f) wait = 0.5f;
      
      // Apply animation-driven movement during attack
      yield return ApplyAnimationMovement(attackAnim, wait);
    }
    
    enemyController.PlayAnimation(enemyController.defaultAnimation);
  }

  private IEnumerator ApplyAnimationMovement(string animationName, float duration) {
    // Get animation data to check for movement sequence
    var animData = GetAnimationData(animationName);
    if (animData?.movementSequence != null && animData.movementSequence.Count > 0) {
      float elapsed = 0f;
      int currentFrame = 0;
      
      while (elapsed < duration && currentFrame < animData.movementSequence.Count) {
        var frame = animData.movementSequence[currentFrame];
        float frameTime = frame.time * duration; // frame.time is normalized 0.0-1.0
        
        if (elapsed >= frameTime) {
          // Apply movement from this frame
          Vector2 velocity = frame.velocity;
          if (!enemyController.IsFacingRight) {
            velocity.x *= -1; // Flip X velocity if facing left
          }
          
          if (rb != null) {
            rb.linearVelocity = velocity;
          }
          
          currentFrame++;
        }
        
        elapsed += Time.deltaTime;
        yield return null;
      }
      
      StopMovement();
    } else {
      // No movement sequence, just wait
      yield return new WaitForSeconds(duration);
    }
  }

  private IEnumerator PostAttackAction(int chainDepth) {
    // Random action after attack: stand still, run away, run towards, or another attack
    float roll = Random.value;
    
    if (roll < 0.25f) {
      // Stand still
      StopMovement();
      yield return new WaitForSeconds(Random.Range(waitRange.x, waitRange.y));
    }
    else if (roll < 0.5f) {
      // Run away from character
      var awayFromPlayer = ((Vector2)(transform.position - player.position)).normalized;
      yield return MoveInDirection(awayFromPlayer, Random.Range(backstepRange.x, backstepRange.y), 0.75f);
    }
    else if (roll < 0.75f) {
      // Run towards character
      var towardsPlayer = ((Vector2)(player.position - transform.position)).normalized;
      yield return MoveInDirection(towardsPlayer, Random.Range(lungeRange.x, lungeRange.y), 1.2f);
    }
    else {
      // Another random attack - loop back to attack sequence
      // Limit recursion depth to prevent stack overflow
      if (chainDepth < maxAttackChainDepth) {
        yield return AttackSequence();
        yield return PostAttackAction(chainDepth + 1); // Recursive call with incremented depth
      } else {
        // Max depth reached, just stand still instead
        StopMovement();
        yield return new WaitForSeconds(Random.Range(waitRange.x, waitRange.y));
      }
    }
  }

  private IEnumerator ApproachPlayer() {
    // Enemy uses Run animation to move towards the player
    var toPlayer = ((Vector2)(player.position - transform.position)).normalized;
    
    if (!enemyController.PlayAnimation("Run")) {
      yield break;
    }
    
    // Move towards player for a short duration
    yield return MoveInDirection(toPlayer, Random.Range(runStepRange.x, runStepRange.y), 1f);
    enemyController.PlayAnimation(enemyController.defaultAnimation);
  }

  private IEnumerator MoveInDirection(Vector2 direction, float duration, float speedMultiplier) {
    if (direction.sqrMagnitude > 0.001f) {
      enemyController.FaceDirection(direction.x);
    }
    
    if (!enemyController.PlayAnimation("Run")) {
      yield break;
    }
    
    yield return MoveForDuration(direction, duration, speedMultiplier);
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

  private float ResolveClosingDistance() {
    var type = enemyController != null ? enemyController.enemyType : info?.enemyType;
    if (!string.IsNullOrEmpty(type) && AllStatValues.Enemies.TryGetValue(type, out var stats) && stats.TryGetValue("CDST", out var cd)) {
      return cd;
    }
    return fallbackClosingDistance;
  }

  private float ResolveMoveSpeed() {
    var type = enemyController != null ? enemyController.enemyType : info?.enemyType;
    if (!string.IsNullOrEmpty(type) && AllStatValues.Enemies.TryGetValue(type, out var stats) && stats.TryGetValue("MVSP", out var ms)) {
      return ms;
    }
    return moveSpeed; // Return default if stat not found
  }

  private AnimData GetAnimationData(string animationName) {
    var type = enemyController != null ? enemyController.enemyType : info?.enemyType;
    if (!string.IsNullOrEmpty(type) && Animations.Enemies.TryGetValue(type, out var anims) && anims.TryGetValue(animationName, out var data)) {
      return data;
    }
    return null;
  }
}
