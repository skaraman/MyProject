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

  /// <summary>
  /// Receives a hit from a HitBox and validates it.
  /// </summary>
  public void ReceiveHit(HitBox2D hitBox) {
    if (!isActiveAndEnabled || hitBox == null) return;

    // Validate the hit is true (basic validation - hurtbox is active and hitbox is valid)
    if (!hitBox.isActiveAndEnabled) return;

    if (ignoreEnemyHitBoxes && hitBox.GetComponentInParent<EnemyInfo>() != null) return;

    // Hit is validated as true - invoke event with context
    OnHit?.Invoke(hitBox);

    if (launchRandomOnHit) {
      LaunchRandom();
    }
  }

  private void LaunchRandom() {
    var destruction = GetComponentInParent<DestructionManager>();
    if (destruction != null) destruction.LaunchRandom();
  }

}
