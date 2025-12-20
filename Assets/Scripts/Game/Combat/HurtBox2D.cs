using System;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class HurtBox2D : MonoBehaviour {
  [Serializable]
  public class HitBoxEvent : UnityEvent<HitBox2D> { }

  [Serializable]
  public class HurtBoxEvent : UnityEvent<HurtBox2D> { }

  [Tooltip("If set, only objects on these layers are considered for contacts.")]
  public LayerMask contactLayers = ~0;

  [Tooltip("If true, ignores contacts with colliders on the same root object (prevents self-contacts).")]
  public bool ignoreSameRoot = true;

  [Tooltip("Called when this hurtbox is hit by a HitBox2D (damage/hit confirmation).")]
  public HitBoxEvent OnHit = new();

  [Tooltip("Called when this hurtbox overlaps another HurtBox2D (hurt↔hurt contact).")]
  public HurtBoxEvent OnHurtContact = new();

  [Tooltip("Called when this hurtbox overlaps any HitBox2D (contact only; does not apply damage by itself).")]
  public HitBoxEvent OnHitBoxContact = new();

  void Reset() {
    var collider = GetComponent<Collider2D>();
    if (collider != null) collider.isTrigger = true;
  }

  public void ReceiveHit(HitBox2D hitBox) {
    if (!isActiveAndEnabled) return;
    OnHit?.Invoke(hitBox);
  }

  void OnTriggerEnter2D(Collider2D other) {
    HandleContact(other);
  }

  void OnCollisionEnter2D(Collision2D collision) {
    HandleContact(collision.collider);
  }

  private void HandleContact(Collider2D other) {
    if (!isActiveAndEnabled || other == null) return;
    if ((contactLayers.value & (1 << other.gameObject.layer)) == 0) return;
    if (ignoreSameRoot && other.transform.root == transform.root) return;

    var otherHurtBox = other.GetComponentInParent<HurtBox2D>();
    if (otherHurtBox != null && otherHurtBox != this && otherHurtBox.isActiveAndEnabled) {
      OnHurtContact?.Invoke(otherHurtBox);
      return;
    }

    var otherHitBox = other.GetComponentInParent<HitBox2D>();
    if (otherHitBox != null && otherHitBox.isActiveAndEnabled) {
      OnHitBoxContact?.Invoke(otherHitBox);
    }
  }
}
