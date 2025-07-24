using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;
public class GearController : MonoBehaviour {
  // Equipped item IDs (set these via the Inspector or at runtime)
  public Dictionary<string, Dictionary<string, GearItem>> gearState = new Dictionary<string, Dictionary<string, GearItem>>() {
    ["Base"] = new Dictionary<string, GearItem> {
      { "Head", null }, { "Body", null }, { "Legs", null }, { "Feet", null }, { "Shoulders", null },
      { "Arms", null }, { "Belt", null }, { "Zemi", null }, { "Ring1", null }, { "Ring3", null },
      { "Ring4", null }, { "Ring5", null }, { "Ring6", null }, { "Ring7", null }, { "Ring8", null },
      { "Ring9", null }, { "Ring10", null }, { "Ring11", null }, { "Ring12", null }
    },
    ["Aqua"] = new Dictionary<string, GearItem> {
      { "Head", null }, { "Body", null }, { "Legs", null }, { "Feet", null }, { "Shoulders", null },
      { "Arms", null }, { "Belt", null }, { "Zemi", null }, { "Ring1", null }, { "Ring3", null },
      { "Ring4", null }, { "Ring5", null }, { "Ring6", null }, { "Ring7", null }, { "Ring8", null },
      { "Ring9", null }, { "Ring10", null }, { "Ring11", null }, { "Ring12", null }
    },
    ["Fire"] = new Dictionary<string, GearItem> {
      { "Head", null }, { "Body", null }, { "Legs", null }, { "Feet", null }, { "Shoulders", null },
      { "Arms", null }, { "Belt", null }, { "Zemi", null }, { "Ring1", null }, { "Ring3", null },
      { "Ring4", null }, { "Ring5", null }, { "Ring6", null }, { "Ring7", null }, { "Ring8", null },
      { "Ring9", null }, { "Ring10", null }, { "Ring11", null }, { "Ring12", null }
    },
    ["Bolt"] = new Dictionary<string, GearItem> {
      { "Head", null }, { "Body", null }, { "Legs", null }, { "Feet", null }, { "Shoulders", null },
      { "Arms", null }, { "Belt", null }, { "Zemi", null }, { "Ring1", null }, { "Ring3", null },
      { "Ring4", null }, { "Ring5", null }, { "Ring6", null }, { "Ring7", null }, { "Ring8", null },
      { "Ring9", null }, { "Ring10", null }, { "Ring11", null }, { "Ring12", null }
    },
    ["Cold"] = new Dictionary<string, GearItem> {
      { "Head", null }, { "Body", null }, { "Legs", null }, { "Feet", null }, { "Shoulders", null },
      { "Arms", null }, { "Belt", null }, { "Zemi", null }, { "Ring1", null }, { "Ring3", null },
      { "Ring4", null }, { "Ring5", null }, { "Ring6", null }, { "Ring7", null }, { "Ring8", null },
      { "Ring9", null }, { "Ring10", null }, { "Ring11", null }, { "Ring12", null }
    },
    ["Dark"] = new Dictionary<string, GearItem> {
      { "Head", null }, { "Body", null }, { "Legs", null }, { "Feet", null }, { "Shoulders", null },
      { "Arms", null }, { "Belt", null }, { "Zemi", null }, { "Ring1", null }, { "Ring3", null },
      { "Ring4", null }, { "Ring5", null }, { "Ring6", null }, { "Ring7", null }, { "Ring8", null },
      { "Ring9", null }, { "Ring10", null }, { "Ring11", null }, { "Ring12", null }
    }
  };
  public GameObject[] GearObjects;
  public GameObject[] HairObjects;
  public GameObject[] OtherBounceGearObjects;
  public GameObject[] SkinObjects;
  private GameObject[] combinedBounces;
  public GameObject HairSkin;
  public CharacterState EsperState;
  public Dictionary<string, Dictionary<string, GearItem>> lastGear = new Dictionary<string, Dictionary<string, GearItem>>();
  public string activeForm;
  private string currentAnimation = "Breathe";
  private string nextAnimation;
  private Action offBounces;
  private int currentFrame;
  private float _animationTimer = 0f;
  private bool pingPong = false;
  private bool isPlaying = true;
  public bool isFacingRight = true;

  [ForceUpdate]
  void Start() {
    combinedBounces = (HairObjects).Concat(OtherBounceGearObjects).ToArray();
    EsperState = GetComponent<CharacterState>();
    EsperState.RefreshSave();
    activeForm = EsperanzaForms.GetActive();
    lastGear = GetSavedGearState();
    SetGear(true);
  }

  void OnDisable() {
    offBounces?.Invoke();
  }

  public void SetItem(string slot, GearItem gearItem) {
    gearState[activeForm][slot] = gearItem;
  }

  public void SetGear(bool force) {
    var currentGear = GetCurrentGearState();
    if (force || !DictionariesEqual(currentGear, lastGear)) {
      RefreshGear();
      lastGear = new Dictionary<string, Dictionary<string, GearItem>>(currentGear);
    }
  }

  public Dictionary<string, Dictionary<string, GearItem>> GetCurrentGearState() {
    return gearState;
  }

  public Dictionary<string, Dictionary<string, GearItem>> GetSavedGearState() {
    foreach (var item in EquippedItems.AllGearForms[activeForm]) {
      if (item.Value == null) { continue; }
      Debug.Log($"slot: {item.Key} name: {item.Value.name} color: {item.Value.gearColor}");
      gearState[activeForm][item.Key] = item.Value;
    }
    return gearState;
  }

  public bool DictionariesEqual(
      Dictionary<string, Dictionary<string, GearItem>> dict1,
      Dictionary<string, Dictionary<string, GearItem>> dict2) {
    // Check if the outer dictionaries have the same number of keys.
    if (dict1.Count != dict2.Count)
      return false;

    // Loop through each key-value pair of the first dictionary.
    foreach (var outerPair in dict1) {
      // Try to get the corresponding nested dictionary from dict2.
      if (!dict2.TryGetValue(outerPair.Key, out Dictionary<string, GearItem> nestedDict2))
        return false;

      Dictionary<string, GearItem> nestedDict1 = outerPair.Value;

      // Ensure that the inner dictionaries have the same count.
      if (nestedDict1.Count != nestedDict2.Count)
        return false;

      // Loop through the nested dictionary and compare each GearItem.
      foreach (var innerPair in nestedDict1) {
        // Verify that the nested dictionary from dict2 contains the key.
        if (!nestedDict2.TryGetValue(innerPair.Key, out GearItem gearItem2))
          return false;

        // Use EqualityComparer to account for custom equality logic on GearItem.
        if (!EqualityComparer<GearItem>.Default.Equals(innerPair.Value, gearItem2))
          return false;
      }
    }
    return true;
  }

  public void RefreshGear() {
    UnequipGear();
    EquipGear();
  }

  public void UnequipGear() {
    if (GearObjects != null) {
      foreach (GameObject go in GearObjects) {
        var sn = go.GetComponent<SpriteWithNormals>();
        if (sn != null) {
          sn.labelPrefix = "";
        }
      }
    }
    foreach (GameObject bounceParent in combinedBounces) {
      foreach (Transform child in bounceParent.transform) {
        child.gameObject.SetActive(false);
      }
    }
  }

  public void EquipGear() {
    var equippedItems = GetCurrentGearState();
    foreach (KeyValuePair<string, GearItem> equip in equippedItems[activeForm]) {
      if (equip.Value == null && equip.Key != "Head") continue;
      var gearId = "";
      if (equip.Value == null && equip.Key == "Head") gearId = activeForm + "_no_Head";
      else gearId = $"{equip.Value.gearId}_{equip.Key}";      
      if (!EsperanzaGearParts.ContainsKey(gearId)) Debug.LogError($"No parts found for equipped gearId: {gearId}");
      var parts = EsperanzaGearParts.gearParts[gearId];
      if (parts == null) Debug.LogError($"Null parts list returned for gearId: {gearId}");
      foreach (GameObject go in GearObjects) {
        foreach (string part in parts) {
          if (go != null && go.name.Equals(part)) {
            var spriteWithNormals = go.GetComponent<SpriteWithNormals>();
            var shaderAnimator = go.GetComponent<AllIn1AnimatorInspector>();
            if (spriteWithNormals != null) spriteWithNormals.labelPrefix = equip.Value.gearId;
            else Debug.LogWarning($"GameObject {go.name} does not have a SpriteWithNormals component attached.");
            if (shaderAnimator != null) {
              var ncolor = ShaderColors.myColors[equip.Value.gearColor];
              shaderAnimator.SetKeyword("GLOW_ON", true);
              shaderAnimator.AddFloatSequence("_Glow", 4f, 4f, 1f);
              shaderAnimator.AddColorSequence("_GlowColor", ncolor, ncolor, 1f);
            }
            else {
              Debug.LogWarning($"GameObject {go.name} does not have a AllIn1AnimatorInspector component attached.");
            }
          }
        }
      }
      foreach (GameObject bounceParent in combinedBounces) {
        if (bounceParent == null) continue;
        foreach (Transform child in bounceParent.transform) {
          if (child != null && child.gameObject.name.Equals(gearId)) {
            child.gameObject.SetActive(true);
            var spriteRenderer = child.gameObject.GetComponent<SpriteRenderer>();
            var shaderAnimator = child.gameObject.GetComponent<AllIn1AnimatorInspector>();
            if (shaderAnimator != null && equip.Value != null || gearId == activeForm + "_no_Head") {
              var gearColor = "";
              if (gearId == activeForm + "_no_Head") {
                gearColor = ShaderColors.pairs[activeForm]["primary"]["color"];
                HairSkin.GetComponent<SpriteRenderer>().color = ShaderColors.myColors[gearColor];
              }
              else {
                gearColor = equip.Value.gearColor;
              }
              var ncolor = ShaderColors.myColors[gearColor];
              shaderAnimator.SetKeyword("GLOW_ON", true);
              shaderAnimator.AddFloatSequence("_Glow", 6f, 6f, 1f);
              shaderAnimator.AddColorSequence("_GlowColor", ncolor, ncolor, 1f);
              spriteRenderer.color = ncolor;
            }
          }
        }
      }
    }
  }

  public void PlayAnimation(string anim) {
    //Debug.Log(anim);
    if (currentAnimation == anim) return; // TODO potentially a conflict, check for animations that can play on themselves
    if (currentAnimation == null) {
      currentAnimation = anim;
      nextAnimation = null;
    }
    else if (Interupts.interupts.ContainsKey(currentAnimation) && Interupts.interupts[currentAnimation].ContainsKey(anim)) {
      // Update current animation from interrupt mapping.
      currentAnimation = Interupts.interupts[currentAnimation][anim];
      nextAnimation = anim;
    }
    else {
      return;
    }
    var category = currentAnimation;
    if (EsperanzaAnimations.animations[currentAnimation].To) {
      category = "To";
    }
    // Update animation for Gear and Skin objects.
    foreach (GameObject go in GearObjects) {
      go.GetComponent<SpriteWithNormals>().SetAnimation(category);
    }
    foreach (GameObject go in SkinObjects) {
      go.GetComponent<SpriteWithNormals>().SetAnimation(category);
    }
    currentFrame = EsperanzaAnimations.animations[currentAnimation].start - 1;
    _animationTimer = 0f;
    pingPong = false;
    isPlaying = true;
    SetBounces();
  }

  void FixedUpdate() {
    if (!Application.isPlaying || !isPlaying || currentAnimation == null) return;
    var anim = EsperanzaAnimations.animations[currentAnimation];
    _animationTimer += Time.deltaTime * 1000f;
    float normalTime = _animationTimer / anim.duration;
    // Advance & wrap
    if (!pingPong) {
      int frameOffset = Mathf.FloorToInt((float)(anim.end - anim.start) * normalTime);
      currentFrame = anim.start + frameOffset;
      if (currentFrame >= anim.end) {
        if (!string.IsNullOrEmpty(nextAnimation)) {
          //Debug.Log(nextAnimation);
          currentAnimation = null;
          PlayAnimation(nextAnimation);
          return;
        }
        if (anim.loop) {
          currentFrame = anim.start - 1;
          pingPong = false;
          _animationTimer = 0f;
        }
        else {
          currentFrame = anim.end + 1;
          isPlaying = false;
          if (anim.pingPong) {
            _animationTimer = 0f;
            isPlaying = true;
            pingPong = true;
          }
        }
      }
    }
    else {
      int frameOffset = Mathf.FloorToInt((float)(anim.end - anim.start) * normalTime);
      currentFrame = anim.end - frameOffset;
      if (currentFrame <= anim.start) {
        isPlaying = true;
        currentFrame = anim.start - 1;
        pingPong = false;
        _animationTimer = 0f;
      }
    }
    //Debug.Log(currentAnimation + " | " + currentFrame);
    foreach (GameObject go in GearObjects) {
      go.GetComponent<SpriteWithNormals>().UpdateSpriteAndNormal(currentFrame);
    }
    foreach (GameObject go in SkinObjects) {
      go.GetComponent<SpriteWithNormals>().UpdateSpriteAndNormal(currentFrame);
    }
  }
  
  public void SetBounces() {
    Dictionary<string, GearItem> currentGearSlots = gearState[activeForm];
    foreach (KeyValuePair<string, GearItem> slotPair in currentGearSlots) {
      string slot = slotPair.Key;
      GearItem gearItem = slotPair.Value;
      string gearKey = "";
      if (slot == "Head" && gearItem == null) {
        gearKey = activeForm + "_no_Head";
      }
      else if (gearItem != null) {
        gearKey = $"{gearItem.gearId}_{slot}";
      }
      if (!BounceAdjustments.adjustments.ContainsKey(gearKey)) continue;
      var gearAdjustment = BounceAdjustments.adjustments[gearKey];

      foreach (KeyValuePair<string, Dictionary<string, Dictionary<string, float>>> partPair in gearAdjustment) {
        string partKey = partPair.Key;
        var animationDict = partPair.Value;
        //Debug.Log(currentAnimation);
        if (!animationDict.ContainsKey(currentAnimation)) continue;
        Dictionary<string, float> adjustments = animationDict[currentAnimation];
        foreach (GameObject bounceParent in combinedBounces) {
          if (bounceParent.name.Equals(partKey)) {
            //Debug.Log(bounceParent.name + ", " + adjustments["x"] + " | " + currentAnimation);
            float offset = 0;
            if (!isFacingRight) { offset = adjustments["offset"]; }
            Vector3 targetPos = new Vector3(
              adjustments["x"] + offset,
              adjustments["y"],
              bounceParent.transform.localPosition.z);
            bounceParent.transform.localPosition = targetPos;
            continue;
          }
          // Optionally apply the "offset" value for additional adjustments.
          // Example:
          // float newZ = baseDepth + adjustments["offset"];
          // child.localPosition = new Vector3(child.localPosition.x, child.localPosition.y, newZ);
        }
      }
    }
  }
}
