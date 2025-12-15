using UnityEngine;

public class Rigidbody2DWrapper : MonoBehaviour {
  public float linearVelocityX;
  public float linearVelocityY;
  public float angularVelocity;

  private Rigidbody2D rb;
  private Vector2 holder;

  void Start() {
    rb = GetComponent<Rigidbody2D>();
    linearVelocityX = rb.linearVelocity.x;
    linearVelocityY = rb.linearVelocity.y;
    angularVelocity = rb.angularVelocity;
  }

  [ForceUpdate]
  void Update() {
    if (rb.linearVelocity.x != linearVelocityX || rb.linearVelocity.y != linearVelocityY || rb.angularVelocity != angularVelocity) {
      holder.x = linearVelocityX;
      holder.y = linearVelocityY;
      rb.linearVelocity = holder;
      rb.angularVelocity = angularVelocity;

    }
  }
}