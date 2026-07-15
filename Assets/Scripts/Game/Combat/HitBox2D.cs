using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// HitBox - a box that detects contacts with HurtBox2D and forwards them for validation.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class HitBox2D : MonoBehaviour {
  [Tooltip("If true, ignores contacts with colliders owned by the same actor (prevents self-hits).")]
  public bool ignoreSameRoot = true;

  [Tooltip("If true, each HurtBox2D can only be hit once while this component is enabled.")]
  public bool hitEachHurtBoxOnce = true;

  [Tooltip("Minimum time in seconds between successful hits. 0 disables the cooldown.")]
  public float hitCooldown = 0f;

  [Tooltip("Optional identifier for filtering hit reactions (attack name/type).")]
  public string hitId;

  private readonly HashSet<ulong> hitHurtBoxIds = new();
  private float nextHitTime;
  private Transform actorOwner;
  public Transform ActorOwner => actorOwner;
  public bool IsEnemyOwned { get; private set; }

  void Awake() {
    actorOwner = ResolveActorOwner(transform, out var isEnemy);
    IsEnemyOwned = isEnemy;
  }

  void Reset() {
    var collider = GetComponent<Collider2D>();
    if (collider != null) collider.isTrigger = true;
  }

  void OnEnable() {
    hitHurtBoxIds.Clear();
    nextHitTime = 0f;
  }

  public void ResetHitCache() {
    hitHurtBoxIds.Clear();
  }

  public void ResetHitCooldown() {
    nextHitTime = 0f;
  }

  void OnTriggerEnter2D(Collider2D other) {
    HandleContact(other);
  }

  void OnCollisionEnter2D(Collision2D collision) {
    HandleContact(collision.collider);
  }

  private void HandleContact(Collider2D other) {
    if (!isActiveAndEnabled || other == null) return;
    if (ignoreSameRoot && IsOwnedBySameActor(other)) return;
    var now = TimeScale.GetNow(this);
    if (hitCooldown > 0f && now < nextHitTime) return;

    if (!HurtBox2D.TryResolve(other, out var hurtBox)) return;
    if (!hurtBox.isActiveAndEnabled) return;

    var hurtBoxId = 0UL;
    if (hitEachHurtBoxOnce) {
      hurtBoxId = ObjectEntityId.GetRawValue(hurtBox);
      if (hitHurtBoxIds.Contains(hurtBoxId)) return;
    }

    if (!hurtBox.TryReceiveHit(this)) return;

    if (hitEachHurtBoxOnce) {
      hitHurtBoxIds.Add(hurtBoxId);
    }

    if (hitCooldown > 0f) {
      nextHitTime = now + hitCooldown;
    }
  }

  private bool IsOwnedBySameActor(Collider2D other) {
    var otherOwner = ResolveActorOwner(other);
    if (actorOwner != null && otherOwner != null) {
      return actorOwner == otherOwner;
    }

    if (other.transform == transform) return true;
    if (other.transform.IsChildOf(transform)) return true;
    return transform.IsChildOf(other.transform);
  }

  private struct OwnerCacheEntry {
    public EntityId instanceId;
    public Transform owner;
  }
  private static readonly OwnerCacheEntry[] ColliderOwnerCache = new OwnerCacheEntry[2048];

  private static Transform ResolveActorOwner(Collider2D source) {
    if (source == null) return null;
    EntityId instanceId = source.GetEntityId();
    int cacheIndex = (instanceId.GetHashCode() & 0x7FFFFFFF) % ColliderOwnerCache.Length;
    var entry = ColliderOwnerCache[cacheIndex];
    if (entry.instanceId.Equals(instanceId)) {
      return entry.owner;
    }
    var owner = ResolveActorOwnerUncached(source.transform, out _);
    ColliderOwnerCache[cacheIndex] = new OwnerCacheEntry { instanceId = instanceId, owner = owner };
    return owner;
  }

  private static Transform ResolveActorOwner(Transform source, out bool isEnemy) {
    isEnemy = false;
    if (source == null) return null;
    return ResolveActorOwnerUncached(source, out isEnemy);
  }

  private static Transform ResolveActorOwnerUncached(Transform source, out bool isEnemy) {
    isEnemy = false;
    var enemyController = source.GetComponentInParent<EnemyController>();
    if (enemyController != null) {
      isEnemy = true;
      return enemyController.transform;
    }

    var enemy = source.GetComponentInParent<EnemyInfo>();
    if (enemy != null) {
      isEnemy = true;
      return enemy.transform;
    }

    var player = source.GetComponentInParent<GearController>();
    if (player != null) return player.transform;

    var body = source.GetComponentInParent<Rigidbody2D>();
    if (body != null) return body.transform;

    return null;
  }
}
