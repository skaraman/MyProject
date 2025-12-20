using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class HitBox2D : MonoBehaviour {
  [Serializable]
  public class HurtBoxEvent : UnityEvent<HurtBox2D> { }

  [Serializable]
  public class HitBoxEvent : UnityEvent<HitBox2D> { }

  [Tooltip("If set, only objects on these layers can be hit.")]
  public LayerMask hittableLayers = ~0;

  [Tooltip("If true, ignores contacts with colliders on the same root object (prevents self-hits).")]
  public bool ignoreSameRoot = true;

  [Tooltip("If true, each HurtBox2D can only be hit once while this component is enabled.")]
  public bool hitEachHurtBoxOnce = true;

  [Tooltip("Called when this hitbox overlaps a HurtBox2D (a 'hit').")]
  public HurtBoxEvent OnHit = new();

  [Tooltip("Called when this hitbox overlaps a HurtBox2D (no args).")]
  public UnityEvent OnHitAny = new();

  [Tooltip("Called when this hitbox overlaps another HitBox2D (a 'clash').")]
  public HitBoxEvent OnClash = new();

  [Tooltip("Called when this hitbox overlaps another HitBox2D (no args).")]
  public UnityEvent OnClashAny = new();

  private readonly HashSet<int> hitHurtBoxIds = new();

  void Reset() {
    var collider = GetComponent<Collider2D>();
    if (collider != null) collider.isTrigger = true;
  }

  void OnEnable() {
    hitHurtBoxIds.Clear();
  }

  public void ResetHitCache() {
    hitHurtBoxIds.Clear();
  }

  void OnTriggerEnter2D(Collider2D other) {
    HandleContact(other);
  }

  void OnCollisionEnter2D(Collision2D collision) {
    HandleContact(collision.collider);
  }

  private void HandleContact(Collider2D other) {
    if (!isActiveAndEnabled || other == null) return;
    if ((hittableLayers.value & (1 << other.gameObject.layer)) == 0) return;
    if (ignoreSameRoot && other.transform.root == transform.root) return;

    var hurtBox = other.GetComponentInParent<HurtBox2D>();
    if (hurtBox != null) {
      if (hitEachHurtBoxOnce) {
        var id = hurtBox.GetInstanceID();
        if (hitHurtBoxIds.Contains(id)) return;
        hitHurtBoxIds.Add(id);
      }

      OnHit?.Invoke(hurtBox);
      OnHitAny?.Invoke();
      hurtBox.ReceiveHit(this);
      return;
    }

    var otherHitBox = other.GetComponentInParent<HitBox2D>();
    if (otherHitBox != null && otherHitBox.isActiveAndEnabled) {
      OnClash?.Invoke(otherHitBox);
      OnClashAny?.Invoke();
    }
  }
}
