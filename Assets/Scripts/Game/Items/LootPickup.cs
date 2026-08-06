using System.Collections;
using UnityEngine;

public class LootPickup : MonoBehaviour {
  public bool isGold;
  public string gemType;
  public int amount;
  
  private bool canCollect = false;
  private bool isCollecting = false;
  private float spawnTime;
  private const float collectDelay = 0.5f;
  
  private float groundY;
  private Rigidbody2D rb;

  void Start() {
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
        // Try to find the foottouch child, otherwise use character root
        Transform targetFoot = character.transform;
        foreach (Transform child in character.GetComponentsInChildren<Transform>()) {
          if (child.name.IndexOf("foot", System.StringComparison.OrdinalIgnoreCase) >= 0 || 
              child.name.IndexOf("touch", System.StringComparison.OrdinalIgnoreCase) >= 0) {
            targetFoot = child;
            break;
          }
        }
        
        float dist = Vector2.Distance(transform.position, targetFoot.position);
        if (dist < 0.8f) {
          StartCoroutine(AnimatePickupAndCollect(targetFoot));
        }
      }
    }
  }

  void OnTriggerEnter2D(Collider2D other) {
    if (!canCollect || isCollecting) return;
    
    // Check if the collider belongs to the player
    if (other.GetComponentInParent<CharacterState>() != null || other.CompareTag("Player")) {
      string objName = other.gameObject.name;
      // We only want to react to the foottouch box, not the hurt box or attack box
      if (objName.IndexOf("hurt", System.StringComparison.OrdinalIgnoreCase) >= 0) return;
      if (objName.IndexOf("hit", System.StringComparison.OrdinalIgnoreCase) >= 0) return;
      
      // If it's the specific foot touch box or the main player root, collect it
      if (objName.IndexOf("foot", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
          objName.IndexOf("touch", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
          other.gameObject == other.transform.root.gameObject ||
          other.CompareTag("Player")) {
        
        StartCoroutine(AnimatePickupAndCollect(other.transform));
      }
    }
  }

  IEnumerator AnimatePickupAndCollect(Transform target) {
    isCollecting = true;
    
    // Disable physics and colliders during animation
    if (rb != null) rb.simulated = false;
    var colliders = GetComponents<Collider2D>();
    foreach (var col in colliders) col.enabled = false;

    Vector3 startPos = transform.position;
    
    // P1 is the control point for our bezier curve (away from the player and up)
    Vector3 awayDir = (startPos - target.position).normalized;
    // If it's perfectly on top, pick a random direction
    if (awayDir.sqrMagnitude < 0.01f) {
      awayDir = new Vector3(Random.value > 0.5f ? 1f : -1f, 0f, 0f);
    }
    Vector3 controlPoint = startPos + awayDir * 1.5f + Vector3.up * 2f;
    
    float duration = 0.4f;
    float elapsed = 0f;

    while (elapsed < duration) {
      elapsed += Time.deltaTime;
      float t = Mathf.Clamp01(elapsed / duration);
      
      // Ease in-out for smoother animation
      float easedT = t * t * (3f - 2f * t);

      // Quadratic Bezier curve
      // P0 = startPos, P1 = controlPoint, P2 = target.position
      Vector3 pos = Mathf.Pow(1 - easedT, 2) * startPos 
                  + 2 * (1 - easedT) * easedT * controlPoint 
                  + Mathf.Pow(easedT, 2) * target.position;

      transform.position = pos;
      yield return null;
    }
    
    CollectItem();
  }

  void CollectItem() {
    if (isGold) {
      Inventory.Gold += amount;
    } else if (!string.IsNullOrEmpty(gemType)) {
      if (Inventory.Gems == null) {
        Inventory.Gems = new System.Collections.Generic.List<GemItem>();
      }
      Inventory.Gems.Add(new GemItem { Type = gemType, Amount = amount });
    }
    
    Destroy(gameObject);
  }
}
