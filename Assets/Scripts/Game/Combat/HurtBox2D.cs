using System;
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

  void Reset() {
    var collider = GetComponent<Collider2D>();
    if (collider != null) collider.isTrigger = true;
  }

  public static bool TryResolve(Collider2D collider, out HurtBox2D hurtBox) {
    hurtBox = null;
    if (collider == null) return false;

    if (collider.TryGetComponent(out hurtBox)) {
      return true;
    }

    var parent = collider.transform.parent;
    if (parent == null) return false;

    hurtBox = parent.GetComponentInParent<HurtBox2D>();
    return hurtBox != null;
  }

  /// <summary>
  /// Receives a hit from a HitBox and validates it.
  /// </summary>
  public bool TryReceiveHit(HitBox2D hitBox) {
    if (!isActiveAndEnabled || hitBox == null) return false;

    // Validate the hit is true (basic validation - hurtbox is active and hitbox is valid)
    if (!hitBox.isActiveAndEnabled) return false;

    if (ignoreEnemyHitBoxes && hitBox.GetComponentInParent<EnemyInfo>() != null) return false;

    // Hit is validated as true - invoke event with context
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
