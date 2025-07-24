using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D.Animation;

public class SpriteWithNormals : MonoBehaviour {
  public SpriteLibraryAsset colorLibrary;
  public SpriteLibraryAsset normalLibrary;
  public string category = "Breathe";
  public string labelPrefix = "";
  public SpriteRenderer _renderer;
  public MaterialPropertyBlock _mpb;
  public Sprite ColorSprite => colorLibrary?.GetSprite(category, labelCache.TryGetValue(0, out var label) ? label : ""); 
  public Sprite NormalSprite => normalLibrary?.GetSprite(category, labelCache.TryGetValue(0, out var label) ? label : "");
  
  Dictionary<int, string> labelCache = new();

  void Awake() {
    _renderer = GetComponent<SpriteRenderer>();
    _mpb = new MaterialPropertyBlock();
    UpdateSpriteAndNormal(0);
  }

  public void SetAnimation(string name) {
    category = name;
  }

  [ForceUpdate]
  public void UpdateSpriteAndNormal(int _frame) {
    if (!labelCache.TryGetValue(_frame, out var label)) {
      label = (labelPrefix != "" ? labelPrefix : "") + (_frame == 0 ? "" : "_" + _frame);
      labelCache[_frame] = label;
    }

    var objectName = gameObject.name;
    if (colorLibrary == null || normalLibrary == null) {
      Debug.LogError("Sprite libraries are not assigned! " + objectName);
      return;
    }

    var colorSprite = colorLibrary.GetSprite(category, label);
    var normalSprite = normalLibrary.GetSprite(category, label);

    if (colorSprite == null) return;

    if (normalSprite == null) {
      Debug.LogError($"Normal sprite not found for category '{category}' with label '{label}' " + objectName);
      return;
    }

    _renderer.sprite = colorSprite;
    _renderer.GetPropertyBlock(_mpb);
    if (normalSprite.texture != null) {
      _mpb.SetTexture("_NormalMap", normalSprite.texture);
    } else {
      Debug.LogError("Normal sprite found, but its texture is missing. " + objectName);
    }
    _renderer.SetPropertyBlock(_mpb);
  }
}
