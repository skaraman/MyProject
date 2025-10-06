using UnityEngine;

public class Zpoint : MonoBehaviour
{
  private SpriteRenderer spriteR;
  
  void Start() {
    spriteR = GetComponent<SpriteRenderer>();
  }

  void Update() {
    if (spriteR != null) {
      Vector3 pos = spriteR.transform.position;
      Vector3 screenPoint = Camera.main.WorldToScreenPoint(pos);
      Debug.Log($"Screen Point: {screenPoint}, ID: {gameObject.transform.name}");

      // Adjust to control the effect


      spriteR.transform.position = pos;
    }
  }

}