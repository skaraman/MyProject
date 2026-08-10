using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class LootPickup : MonoBehaviour {
  const string PickupSortingLayer = "MyUI";
  const int PickupSortingOrder = 1000;
  const float PickupDuration = 1.05f;
  const float PickupArcAwayDistance = 4.25f;
  const float PickupArcHeight = 7.5f;
  const float BoundaryPushSpeed = 5f;
  const float BoundaryProbeRadius = 1.25f;

  public bool isGold;
  public string gemType;
  public int amount;
  
  private bool canCollect = false;
  private bool isCollecting = false;
  private float spawnTime;
  private const float collectDelay = 0.5f;
  
  private float groundY;
  private Rigidbody2D rb;
  private static readonly Collider2D[] boundaryProbeResults = new Collider2D[16];
  private static readonly ContactFilter2D boundaryProbeFilter = CreateBoundaryProbeFilter();

  private static AudioClip pickupSoundClip;

  void Start() {
    if (pickupSoundClip == null) {
      pickupSoundClip = Resources.Load<AudioClip>("SoundEffects/UI/pickup");
    }

    spawnTime = Time.time;
    // Determine the fake ground level slightly below where it spawned
    groundY = transform.position.y - Random.Range(0.1f, 0.4f);
    rb = GetComponent<Rigidbody2D>();
  }

  void Update() {
    // 2.5D Fake ground collision: stop falling when we hit groundY, but keep horizontal momentum
    if (rb != null && rb.gravityScale > 0f && rb.linearVelocity.y < 0 && transform.position.y <= groundY) {
      transform.position = new Vector3(transform.position.x, groundY, transform.position.z);
      
      // Keep horizontal velocity but dampen it, zero out vertical velocity
      rb.linearVelocity = new Vector2(rb.linearVelocity.x * 0.8f, 0f);
      
      // Turn off gravity and add damping so it slides to a halt
      rb.gravityScale = 0f;
      rb.linearDamping = 5f;
      // We keep it Dynamic so the physics engine handles the sliding
    }
    
    if (!canCollect && Time.time - spawnTime >= collectDelay) {
      canCollect = true;
    }
    
    if (canCollect && !isCollecting) {
      // Check distance to character manually as a fallback in case trigger doesn't hit
      var character = SingleSceneManager.ResolveGameplayCharacterState();
      if (character != null) {
        // Try to find the exact foottouch child, otherwise use character root.
        Transform targetFoot = character.transform;
        foreach (Transform child in character.GetComponentsInChildren<Transform>()) {
          if (string.Equals(child.name, "foottouch", System.StringComparison.OrdinalIgnoreCase)) {
            targetFoot = child;
            break;
          }
        }
        
        float dist = Vector2.Distance(transform.position, targetFoot.position);
        if (dist < 0.8f) {
          StartCoroutine(AnimatePickupAndCollect(character.transform));
        }
      }
    }
  }

  void FixedUpdate() {
    if (!isCollecting && IsNearLocationBoundary()) {
      PushTowardsCharacter();
    }
  }

  void OnCollisionEnter2D(Collision2D collision) {
    if (IsLocationBoundary(collision != null ? collision.collider : null)) {
      PushTowardsCharacter();
    }
  }

  void OnCollisionStay2D(Collision2D collision) {
    if (IsLocationBoundary(collision != null ? collision.collider : null)) {
      PushTowardsCharacter();
    }
  }

  bool IsNearLocationBoundary() {
    int count = Physics2D.OverlapCircle(
      transform.position,
      BoundaryProbeRadius,
      boundaryProbeFilter,
      boundaryProbeResults
    );

    for (int i = 0; i < count; i++) {
      if (IsLocationBoundary(boundaryProbeResults[i])) return true;
    }

    return false;
  }

  static ContactFilter2D CreateBoundaryProbeFilter() {
    var filter = new ContactFilter2D();
    filter.SetLayerMask(Physics2D.DefaultRaycastLayers);
    filter.useTriggers = false;
    return filter;
  }

  void PushTowardsCharacter() {
    if (isCollecting) return;

    var character = SingleSceneManager.ResolveGameplayCharacterState();
    if (character == null) return;

    Vector2 direction = (Vector2)character.transform.position - (Vector2)transform.position;
    if (direction.sqrMagnitude <= 0.0001f) return;
    direction.Normalize();

    if (rb == null) rb = GetComponent<Rigidbody2D>();
    if (rb != null && rb.simulated) {
      rb.WakeUp();
      rb.linearVelocity = direction * BoundaryPushSpeed;
    } else {
      transform.position += (Vector3)(direction * BoundaryPushSpeed * Time.fixedDeltaTime);
    }
  }

  static bool IsLocationBoundary(Collider2D collider) {
    if (collider == null || collider.isTrigger) return false;
    if (collider.CompareTag("Wall")) return true;

    for (Transform current = collider.transform; current != null; current = current.parent) {
      if (string.Equals(current.name, "Bounds", System.StringComparison.OrdinalIgnoreCase)) return true;
    }

    return false;
  }

  void OnTriggerEnter2D(Collider2D other) {
    if (!canCollect || isCollecting) return;
    
    // Check if the collider belongs to the player
    var character = other.GetComponentInParent<CharacterState>();
    if (character != null || other.CompareTag("Player")) {
      if (character == null) character = SingleSceneManager.ResolveGameplayCharacterState();
      if (character == null) return;

      string objName = other.gameObject.name;
      // We only want to react to the foottouch box, not the hurt box or attack box
      if (objName.IndexOf("hurt", System.StringComparison.OrdinalIgnoreCase) >= 0) return;
      if (objName.IndexOf("hit", System.StringComparison.OrdinalIgnoreCase) >= 0) return;
      
      // If it's the specific foot touch box or the main player root, collect it
      if (objName.IndexOf("foot", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
          objName.IndexOf("touch", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
          other.gameObject == other.transform.root.gameObject ||
          other.CompareTag("Player")) {
        
        StartCoroutine(AnimatePickupAndCollect(character.transform));
      }
    }
  }

  IEnumerator AnimatePickupAndCollect(Transform characterRoot) {
    isCollecting = true;

    PromotePickupSorting();

    // Disable physics and colliders during animation
    if (rb != null) rb.simulated = false;
    var colliders = GetComponents<Collider2D>();
    foreach (var col in colliders) col.enabled = false;

    Vector3 startPos = transform.position;
    Collider2D hurtCollider = ResolveHurtCollider(characterRoot);
    Vector3 pickupTarget = ResolvePickupTarget(characterRoot, hurtCollider);

    // Pull away first so the item traces a visible curve before flying to Esperanza.
    Vector3 awayDir = startPos - pickupTarget;
    awayDir.y = 0f;
    awayDir.Normalize();
    // If it's perfectly on top, pick a random direction
    if (awayDir.sqrMagnitude < 0.01f) {
      awayDir = new Vector3(Random.value > 0.5f ? 1f : -1f, 0f, 0f);
    }
    Vector3 controlPoint = startPos + awayDir * PickupArcAwayDistance + Vector3.up * PickupArcHeight;
    
    float elapsed = 0f;

    while (elapsed < PickupDuration) {
      elapsed += Time.deltaTime;
      float t = Mathf.Clamp01(elapsed / PickupDuration);
      
      // Ease in-out for smoother animation
      float easedT = t * t * (3f - 2f * t);

      // Quadratic Bezier curve
      // Follow the center of Esperanza's hurt collider as she moves.
      pickupTarget = ResolvePickupTarget(characterRoot, hurtCollider);

      // P0 = startPos, P1 = controlPoint, P2 = pickupTarget
      Vector3 pos = Mathf.Pow(1 - easedT, 2) * startPos 
                  + 2 * (1 - easedT) * easedT * controlPoint 
                  + Mathf.Pow(easedT, 2) * pickupTarget;

      transform.position = pos;
      yield return null;
    }
    
    CollectItem();
  }

  void PromotePickupSorting() {
    var zPoints = GetComponentsInChildren<Zpoint>(includeInactive: true);
    foreach (var zPoint in zPoints) {
      if (zPoint != null) zPoint.enabled = false;
    }

    var sortingGroups = GetComponentsInChildren<SortingGroup>(includeInactive: true);
    foreach (var sortingGroup in sortingGroups) {
      sortingGroup.sortingLayerName = PickupSortingLayer;
      sortingGroup.sortingOrder = PickupSortingOrder;
    }

    var spriteRenderers = GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
    foreach (var spriteRenderer in spriteRenderers) {
      spriteRenderer.sortingLayerName = PickupSortingLayer;
      if (sortingGroups.Length == 0) spriteRenderer.sortingOrder = PickupSortingOrder;
    }
  }

  static Collider2D ResolveHurtCollider(Transform characterRoot) {
    if (characterRoot == null) return null;

    var hurtBox = characterRoot.GetComponentInChildren<HurtBox2D>(includeInactive: true);
    return hurtBox != null ? hurtBox.GetComponent<Collider2D>() : null;
  }

  static Vector3 ResolvePickupTarget(Transform characterRoot, Collider2D hurtCollider) {
    if (hurtCollider != null) return hurtCollider.bounds.center;
    return characterRoot != null ? characterRoot.position : Vector3.zero;
  }

  void CollectItem() {
    if (pickupSoundClip != null) {
      GameObject go = new GameObject("PickupSound");
      AudioSource src = go.AddComponent<AudioSource>();
      src.clip = pickupSoundClip;
      src.volume = 0.6f;
      src.Play();
      Destroy(go, pickupSoundClip.length);
    }

    if (isGold) {
      Inventory.AddGold(amount);
    } else if (!string.IsNullOrEmpty(gemType)) {
      Inventory.AddGem(gemType, amount);
    }
    
    Destroy(gameObject);
  }
}
