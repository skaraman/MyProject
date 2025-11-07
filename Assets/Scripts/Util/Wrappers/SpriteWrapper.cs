using UnityEngine;

public class SpriteWrapper : MonoBehaviour {
  public float r, g, b, a;
  private SpriteRenderer spriteRenderer;
  private Color currentColor;
  private bool isDirty = true;

  void Start() {
    spriteRenderer = GetComponent<SpriteRenderer>();
    if (spriteRenderer != null) {
      currentColor = spriteRenderer.color;
      r = currentColor.r;
      g = currentColor.g;
      b = currentColor.b;
      a = currentColor.a;
      isDirty = false;
    }
  }

  [ForceUpdate]
  void Update() {
    if (spriteRenderer == null) {
      spriteRenderer = GetComponent<SpriteRenderer>();
      if (spriteRenderer == null) return;
      // Initialize current color when component is first found
      currentColor = spriteRenderer.color;
      r = currentColor.r;
      g = currentColor.g;
      b = currentColor.b;
      a = currentColor.a;
      isDirty = false;
      return;
    }
    
    // Only update if values have changed
    if (currentColor.r != r || currentColor.g != g || currentColor.b != b || currentColor.a != a || isDirty) {
      currentColor.r = r;
      currentColor.g = g;
      currentColor.b = b;
      currentColor.a = a;
      spriteRenderer.color = currentColor;
      isDirty = false;
    }
  }

}