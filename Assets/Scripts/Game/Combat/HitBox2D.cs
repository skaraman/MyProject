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
  private readonly List<Collider2D> registeredColliders = new(4);
  private float nextHitTime;
  private Transform actorOwner;
  public Transform ActorOwner => actorOwner;
  public bool IsEnemyOwned { get; private set; }

  void Awake() {
    actorOwner = ResolveActorOwner(transform, out var isEnemy);
    IsEnemyOwned = isEnemy;
    RegisterOwnedColliders();
  }

  private static readonly Dictionary<Collider2D, HitBox2D> hitBoxLookupCache = new();

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  private static void ResetStatics() {
    hitBoxLookupCache.Clear();
    ColliderOwnerCache.Clear();
  }

  public static bool TryGet(Collider2D collider, out HitBox2D hitBox) {
    hitBox = null;
    if (collider == null) return false;
    if (hitBoxLookupCache.TryGetValue(collider, out hitBox)) {
      return hitBox != null;
    }
    hitBox = collider.GetComponent<HitBox2D>() ?? collider.GetComponentInParent<HitBox2D>();
    hitBoxLookupCache[collider] = hitBox;
    return hitBox != null;
  }

  public static void ClearHitBoxCache() {
    hitBoxLookupCache.Clear();
  }

  void Reset() {
    var collider = GetComponent<Collider2D>();
    if (collider != null) collider.isTrigger = true;
  }

  void OnEnable() {
    hitHurtBoxIds.Clear();
    nextHitTime = 0f;
    RegisterOwnedColliders();
  }

  void OnDisable() {
    UnregisterOwnedColliders();
  }

  void OnDestroy() {
    UnregisterOwnedColliders();
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
    TryHit(other);
  }

  public bool TryHit(Collider2D other) {
    if (!isActiveAndEnabled || other == null) return false;
    if (ignoreSameRoot && IsOwnedBySameActor(other)) return false;
    var now = TimeScale.GetNow(this);
    if (hitCooldown > 0f && now < nextHitTime) return false;

    if (!HurtBox2D.TryResolve(other, out var hurtBox)) return false;
    if (!hurtBox.isActiveAndEnabled) return false;

    var hurtBoxId = 0UL;
    if (hitEachHurtBoxOnce) {
      hurtBoxId = ObjectEntityId.GetRawValue(hurtBox);
      if (hitHurtBoxIds.Contains(hurtBoxId)) return false;
    }

    if (!hurtBox.TryReceiveHit(this)) return false;

    if (hitEachHurtBoxOnce) {
      hitHurtBoxIds.Add(hurtBoxId);
    }

    if (hitCooldown > 0f) {
      nextHitTime = now + hitCooldown;
    }

    return true;
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

  private static readonly Dictionary<ulong, Transform> ColliderOwnerCache = new();

  private void RegisterOwnedColliders() {
    UnregisterOwnedColliders();
    GetComponentsInChildren(true, registeredColliders);
    for (var i = registeredColliders.Count - 1; i >= 0; i--) {
      var collider = registeredColliders[i];
      var owner = collider.GetComponent<HitBox2D>() ?? collider.GetComponentInParent<HitBox2D>();
      if (!ReferenceEquals(owner, this)) {
        registeredColliders.RemoveAt(i);
        continue;
      }

      hitBoxLookupCache[collider] = this;
    }
  }

  private void UnregisterOwnedColliders() {
    for (var i = 0; i < registeredColliders.Count; i++) {
      var collider = registeredColliders[i];
      if (ReferenceEquals(collider, null)) continue;
      if (hitBoxLookupCache.TryGetValue(collider, out var owner) && ReferenceEquals(owner, this)) {
        hitBoxLookupCache.Remove(collider);
      }
    }
    registeredColliders.Clear();
  }

  private static Transform ResolveActorOwner(Collider2D source) {
    if (source == null) return null;
    ulong instanceId = ObjectEntityId.GetRawValue(source);
    if (ColliderOwnerCache.TryGetValue(instanceId, out var owner)) {
      return owner;
    }
    owner = ResolveActorOwnerUncached(source.transform, out _);
    ColliderOwnerCache[instanceId] = owner;
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
