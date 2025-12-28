using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Basic implementation of IHitManager for handling hit contacts.
/// Attach this to characters and enemies to manage their hit/hurt interactions.
/// </summary>
public class HitManager : MonoBehaviour, IHitManager {
  [System.Serializable]
  public class HitContactEvent : UnityEvent<HitBox2D, HurtBox2D> { }

  [Tooltip("Called when any HitBox under this manager makes contact with a HurtBox")]
  public HitContactEvent OnHitContact = new();

  /// <summary>
  /// Called when a HitBox makes contact with a HurtBox.
  /// </summary>
  public void OnHitContact(HitBox2D hitBox, HurtBox2D hurtBox) {
    if (hitBox == null || hurtBox == null) return;
    
    // Invoke the event with details of the contact
    OnHitContact?.Invoke(hitBox, hurtBox);
  }
}
