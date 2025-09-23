using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.U2D.Animation;

public class SpriteWithNormals : MonoBehaviour {
  public SpriteLibraryAsset colorLibrary;
  public SpriteLibraryAsset normalLibrary;
  public string category = "Breathe";
  public string labelPrefix = "";

  private SpriteRenderer _renderer;
  private MaterialPropertyBlock _mpb;
  private StringBuilder label = new();

  void Awake() {
    _renderer = GetComponent<SpriteRenderer>();
    _mpb = new MaterialPropertyBlock();
    UpdateSpriteAndNormal(0);
  }

  public void SetAnimation(string name) {
    category = name;
  }

  [ForceUpdate]
  public void ForceUpdateSpriteAndNormal() {
    UpdateSpriteAndNormal(0);
  }

  string GetLabel(int frame) {
    label.Clear();
    label.Append(labelPrefix);
    if (frame != 0 && labelPrefix != "") {
      label.Append("_").Append(frame);
    }
    else if (frame != 0 && labelPrefix == "") {
      label.Append(frame);
    }
    var result = label.ToString();
    //Debug.Log($"[SpriteWithNormals] Generated label: '{result}'");
    return result;
  }

  public void UpdateSpriteAndNormal(int _frame) {
    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    var currentLabel = GetLabel(_frame);
    var objectName = gameObject.name;
    if (colorLibrary == null || normalLibrary == null) {
      Debug.LogError("Sprite libraries are not assigned! " + objectName + " " + gameObject.transform.parent?.name);
      return;
    }
    var colorSprite = colorLibrary.GetSprite(category, currentLabel);
    var normalSprite = normalLibrary.GetSprite(category, currentLabel);
    //Debug.Log($"[SpriteWithNormals] Fetching sprites for category: '{category}' and label: '{currentLabel}'");
    if (colorSprite == null) {
      // This gear piece doesn't have gear image
      //Debug.LogWarning($"[SpriteWithNormals] {objectName} Color sprite is null");
      return;
    }
    if (normalSprite == null) {
      Debug.LogError($"Normal sprite not found for category '{category}' with label '{currentLabel}' " + objectName);
      //return;
    }
    _renderer.sprite = colorSprite;
    _mpb ??= new MaterialPropertyBlock();
    _renderer.GetPropertyBlock(_mpb);
    if (normalSprite.texture != null) {
      _mpb.SetTexture("_NormalMap", normalSprite.texture);
    }
    else {
      Debug.LogError("Normal sprite found, but its texture is missing. " + objectName);
    }
    _renderer.SetPropertyBlock(_mpb);
  }

  public void FlipSprite(bool flip) {
    SpriteRenderer spriteRenderer = gameObject.GetComponent<SpriteRenderer>();
    spriteRenderer.flipX = flip;
  }
}
