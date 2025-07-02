using System.Linq;
using UnityEngine;
using UnityEngine.U2D.Animation;

public class SpriteWithNormals : MonoBehaviour {
  public SpriteLibraryAsset colorLibrary;
  public SpriteLibraryAsset normalLibrary;
  public string category = "Breathe";
  public string animationName = "Breathe";
  public string labelPrefix = "";
  public SpriteRenderer _renderer;
  public MaterialPropertyBlock _mpb;

  void Awake() {
    _renderer = GetComponent<SpriteRenderer>();
    _mpb = new MaterialPropertyBlock();
    UpdateSpriteAndNormal(1);  // Ensure it's updated in editor
  }

  public void SetAnimation(string name) {
    category = name;
  }

  [ForceUpdate]
  public void UpdateSpriteAndNormal(int _frame) {
    string label = $"{(labelPrefix == "" ? "" : labelPrefix + "_")}{_frame}";
    string objectName = gameObject.name;
    if (colorLibrary == null || normalLibrary == null) {
      Debug.LogError("Sprite libraries are not assigned! " + objectName);
      return;
    }
    var colorSprite = colorLibrary.GetSprite(category, label);
    var normalSprite = normalLibrary.GetSprite(category, label);
    if (colorSprite == null) {
      //Debug.Log($"Color sprite not found for category '{category}' with label '{label}' " + objectName);
      return;
    }
    if (normalSprite == null) {
      Debug.LogError($"Normal sprite not found for category '{category}' with label '{label}' " + objectName);
      return;
    }
    _renderer.sprite = colorSprite;
    _renderer.GetPropertyBlock(_mpb);
    // Check if normal sprite has a valid texture before assigning.
    if (normalSprite.texture != null) {
      _mpb.SetTexture("_NormalMap", normalSprite.texture);
    }
    else {
      Debug.LogError("Normal sprite found, but its texture is missing. " + objectName);
    }
    _renderer.SetPropertyBlock(_mpb);
  }
}

