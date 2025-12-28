using UnityEngine;

/// <summary>
/// Interface for managing hit contacts between HitBoxes and HurtBoxes.
/// </summary>
public interface IHitManager {
  /// <summary>
  /// Called when a HitBox makes contact with a HurtBox.
  /// </summary>
  /// <param name="hitBox">The HitBox that initiated the contact</param>
  /// <param name="hurtBox">The HurtBox that was contacted</param>
  void OnHitContact(HitBox2D hitBox, HurtBox2D hurtBox);
}
