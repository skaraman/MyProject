using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// HitBox - a box that detects contacts with HurtBox2D and forwards them for validation.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class HitBox2D : MonoBehaviour {
  [Tooltip("If true, ignores contacts with colliders on the same root object (prevents self-hits).")]
  public bool ignoreSameRoot = true;

  [Tooltip("If true, each HurtBox2D can only be hit once while this component is enabled.")]
  public bool hitEachHurtBoxOnce = true;

  [Tooltip("Optional identifier for filtering hit reactions (attack name/type).")]
  public string hitId;

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
    if (ignoreSameRoot && other.transform.root == transform.root) return;

    var hurtBox = other.GetComponentInParent<HurtBox2D>();
    if (hurtBox != null && hurtBox.isActiveAndEnabled) {
      if (hitEachHurtBoxOnce) {
        var id = hurtBox.GetInstanceID();
        if (hitHurtBoxIds.Contains(id)) return;
        hitHurtBoxIds.Add(id);
      }

      // Let the HurtBox validate and receive the hit
      hurtBox.ReceiveHit(this);
    }
  }
}
