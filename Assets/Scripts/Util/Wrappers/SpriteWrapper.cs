using UnityEngine;

public class SpriteWrapper : MonoBehaviour {
  public float r, g, b, a;
  private SpriteRenderer spriteRenderer;

  void Start() {
    spriteRenderer = GetComponent<SpriteRenderer>();
  }

  [ForceUpdate]
  void Update() {
    if (spriteRenderer == null) Start();
    var color = spriteRenderer.color;
    if (color.r != r) color.r = r;
    if (color.g != g) color.g = g;
    if (color.b != b) color.b = b;
    if (color.a != a) color.a = a;
    spriteRenderer.color = color;
  }

}