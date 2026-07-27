using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// HurtBox - a box that can receive hits and will validate that the HitBox contact is true.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class HurtBox2D : MonoBehaviour {
  [Serializable]
  public class HitBoxEvent : UnityEvent<HitBox2D> { }

  [Tooltip("If true, ignores hits that come from hitboxes under an EnemyInfo (useful for enemy hurtboxes).")]
  public bool ignoreEnemyHitBoxes;

  [Tooltip("Called when this hurtbox is hit by a HitBox2D and validates the hit.")]
  public HitBoxEvent OnHit = new();

  [Tooltip("If true, calls DestructionManager.LaunchRandom after hit logic.")]
  public bool launchRandomOnHit = true;

  public HitBox2D LastHitBox { get; private set; }
  public int LastHitFrame { get; private set; } = -1;
  private readonly List<Collider2D> registeredColliders = new(4);

  void Awake() {
    RegisterOwnedColliders();
  }

  void Reset() {
    var collider = GetComponent<Collider2D>();
    if (collider != null) collider.isTrigger = true;
  }

  void OnDisable() {
    UnregisterOwnedColliders();
    LastHitBox = null;
    LastHitFrame = -1;
  }

  void OnEnable() {
    RegisterOwnedColliders();
  }

  void OnDestroy() {
    UnregisterOwnedColliders();
  }

  private static readonly Dictionary<Collider2D, HurtBox2D> hurtBoxCache = new();

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  private static void ResetStatics() {
    hurtBoxCache.Clear();
  }

  public static bool TryResolve(Collider2D collider, out HurtBox2D hurtBox) {
    hurtBox = null;
    if (collider == null) return false;
    if (hurtBoxCache.TryGetValue(collider, out hurtBox)) {
      return hurtBox != null;
    }

    if (collider.TryGetComponent(out hurtBox)) {
      hurtBoxCache[collider] = hurtBox;
      return true;
    }

    var parent = collider.transform.parent;
    if (parent != null) {
      hurtBox = parent.GetComponentInParent<HurtBox2D>();
    }
    hurtBoxCache[collider] = hurtBox;
    return hurtBox != null;
  }

  public static void ClearHurtBoxCache() {
    hurtBoxCache.Clear();
  }

  private void RegisterOwnedColliders() {
    UnregisterOwnedColliders();
    GetComponentsInChildren(true, registeredColliders);
    for (var i = registeredColliders.Count - 1; i >= 0; i--) {
      var collider = registeredColliders[i];
      HurtBox2D owner = null;
      if (!collider.TryGetComponent(out owner)) {
        var parent = collider.transform.parent;
        if (parent != null) {
          owner = parent.GetComponentInParent<HurtBox2D>();
        }
      }

      if (!ReferenceEquals(owner, this)) {
        registeredColliders.RemoveAt(i);
        continue;
      }

      hurtBoxCache[collider] = this;
    }
  }

  private void UnregisterOwnedColliders() {
    for (var i = 0; i < registeredColliders.Count; i++) {
      var collider = registeredColliders[i];
      if (ReferenceEquals(collider, null)) continue;
      if (hurtBoxCache.TryGetValue(collider, out var owner) && ReferenceEquals(owner, this)) {
        hurtBoxCache.Remove(collider);
      }
    }
    registeredColliders.Clear();
  }

  /// <summary>
  /// Receives a hit from a HitBox and validates it.
  /// </summary>
  public bool TryReceiveHit(HitBox2D hitBox) {
    if (!isActiveAndEnabled || hitBox == null) return false;

    // Validate the hit is true (basic validation - hurtbox is active and hitbox is valid)
    if (!hitBox.isActiveAndEnabled) return false;

    if (ignoreEnemyHitBoxes && hitBox.IsEnemyOwned) return false;

    // Hit is validated as true - invoke event with context
    LastHitBox = hitBox;
    LastHitFrame = Time.frameCount;
    OnHit?.Invoke(hitBox);

    if (launchRandomOnHit) {
      LaunchRandom();
    }

    return true;
  }

  public void ReceiveHit(HitBox2D hitBox) {
    TryReceiveHit(hitBox);
  }

  private void LaunchRandom() {
    var destruction = GetComponentInParent<DestructionManager>();
    if (destruction != null) destruction.LaunchRandom();
  }

}
