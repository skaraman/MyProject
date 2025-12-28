using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// HitBox - a box that sends a contact of HitTrue to a general manager 
/// with the details of itself and the collider it hit.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class HitBox2D : MonoBehaviour {
  [Serializable]
  public class HurtBoxEvent : UnityEvent<HurtBox2D> { }

  [Tooltip("If true, ignores contacts with colliders on the same root object (prevents self-hits).")]
  public bool ignoreSameRoot = true;

  [Tooltip("If true, each HurtBox2D can only be hit once while this component is enabled.")]
  public bool hitEachHurtBoxOnce = true;

  [Tooltip("Called when this hitbox makes contact with a HurtBox2D.")]
  public HurtBoxEvent OnHit = new();

  private readonly HashSet<int> hitHurtBoxIds = new();
  private IHitManager hitManager;

  void Reset() {
    var collider = GetComponent<Collider2D>();
    if (collider != null) collider.isTrigger = true;
  }

  void Awake() {
    // Find hit manager in parent hierarchy
    hitManager = GetComponentInParent<IHitManager>();
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
    if (ignoreSameRoot && other.transform.root == transform.root) return;

    var hurtBox = other.GetComponent<HurtBox2D>();
    if (hurtBox != null && hurtBox.isActiveAndEnabled) {
      if (hitEachHurtBoxOnce) {
        var id = hurtBox.GetInstanceID();
        if (hitHurtBoxIds.Contains(id)) return;
        hitHurtBoxIds.Add(id);
      }

      // Send contact to manager
      if (hitManager != null) {
        hitManager.OnHitContact(this, hurtBox);
      }

      // Notify local event listeners
      OnHit?.Invoke(hurtBox);

      // Let the HurtBox validate and receive the hit
      hurtBox.ReceiveHit(this);
    }
  }
}
