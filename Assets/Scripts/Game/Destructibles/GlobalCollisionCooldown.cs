using UnityEngine;
using System.Collections;

/// <summary>
/// Attach this to any object to prevent super-bouncing and knock-on calculations.
/// When it collides with something, it ignores further physics collisions with that specific object for a short time.
/// </summary>
public class GlobalCollisionCooldown : MonoBehaviour
{
    [Tooltip("How long to disable collisions between the two objects after they hit")]
    public float cooldownDuration = 0.05f;

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.collider != null && collision.otherCollider != null)
        {
            StartCoroutine(TemporarilyIgnoreCollision(collision.collider, collision.otherCollider, cooldownDuration));
        }
    }

    IEnumerator TemporarilyIgnoreCollision(Collider2D c1, Collider2D c2, float time)
    {
        // Disable collision between these two specific colliders
        Physics2D.IgnoreCollision(c1, c2, true);
        
        yield return new WaitForSeconds(time);
        
        // Re-enable collision if both objects still exist
        if (c1 != null && c2 != null)
        {
            Physics2D.IgnoreCollision(c1, c2, false);
        }
    }
}
