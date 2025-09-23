using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;
using CustomInspector;
using System.Collections;

[ExecuteAlways]
public class GearController : MonoBehaviour {
  [Button(nameof(_TogglePause), label = "un/pause", size = Size.small)] public bool slowDown;
  [Button(nameof(ForceAnimation), label = "Play", size = Size.small)] public bool forceLoop;

  public GameObject[] GearObjects;
  public GameObject[] HairObjects;
  public GameObject[] OtherBounceGearObjects;
  public GameObject[] SkinObjects;
  private GameObject[] combinedBounces;
  public GameObject HairSkin;
  public Dictionary<string, Dictionary<string, GearItem>> lastGear = new Dictionary<string, Dictionary<string, GearItem>>();

  public string currentAnimation = "Breathe";
  private string nextAnimation;
  private int currentFrame;
  public float _animationTimer = 0f;
  private bool pingPong = false;
  private bool isPlaying = true;
  public bool isFacingRight = true;
  public bool needsFlip = false;
  private Vector3 scaleVector = new Vector3(1, 1, 1);
  private SaveData gameData = new();
  
  private Dictionary<GameObject, List<int>> bounceTweens = new Dictionary<GameObject, List<int>>();

  void Start() {
    combinedBounces = HairObjects.Concat(OtherBounceGearObjects).ToArray();
  }

  public void LoadGear() {
    GetSavedGearState();
    RefreshGear();
    MessageBus.Send("gearReady");
  }

  void OnDestroy() {
    CancelAllBounceTweens();
  }

  private void CancelAllBounceTweens() {
    foreach (var kvp in bounceTweens) {
      foreach (int tweenId in kvp.Value) {
        LeanTween.cancel(tweenId);
      }
      if (kvp.Key != null) {
        StopCoroutine(PlayBounceSequence(kvp.Key, null));
        StopAllCoroutines(); 
      }
    }
    bounceTweens.Clear();
  }

  public void GetSavedGearState() {
    var loaded = SaveSlotManager.Load("equippedGear");
    if (loaded.Keys.Count == 0) return;
    foreach (var form in loaded.GetComplex<Dictionary<string, Dictionary<string, GearItem>>>("allGear")) {
      foreach (var slot in form.Value) {
        if (slot.Value == null) { continue; }
        EquippedItems.AllGearForms[EsperanzaForms.GetActive()][slot.Key] = slot.Value;
      }
    }
  }

  public void SetGearIntoSlot(string slot, GearItem gearItem) {
    EquippedItems.AllGearForms[EsperanzaForms.GetActive()][slot] = gearItem;
    gameData.SetComplex("allGear", EquippedItems.AllGearForms);
    SaveSlotManager.Save("equippedGear", gameData);
  }

  //[ForceUpdate]
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
    var activeForm = EsperanzaForms.GetActive();
    var equippedItems = EquippedItems.AllGearForms;
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
              var newColor = ShaderColors.myColors[equip.Value.gearColor];
              shaderAnimator.SetKeyword("GLOW_ON", true);
              shaderAnimator.AddFloatSequence("_Glow", 4f, 4f, 1f);
              shaderAnimator.AddColorSequence("_GlowColor", newColor, newColor, 1f);
              shaderAnimator.AddColorSequence("_Color", newColor, newColor, 1f);
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
              var newColor = ShaderColors.myColors[gearColor];
              shaderAnimator.SetKeyword("GLOW_ON", true);
              shaderAnimator.AddFloatSequence("_Glow", 6f, 6f, 1f);
              shaderAnimator.AddColorSequence("_GlowColor", newColor, newColor, 1f);
              shaderAnimator.AddColorSequence("_Color", newColor, newColor, 1f);
              spriteRenderer.color = newColor;
            }
          }
        }
      }
    }
  }

  public void _TogglePause() {
    TogglePause();
  }

  public void TogglePause(string forcePause = null) {
    isPlaying = forcePause != null ? false : !isPlaying;
    foreach (var kvp in bounceTweens) {
      foreach (int tweenId in kvp.Value) {
        if (isPlaying) {
          LeanTween.resume(tweenId);
        }
        else {
          LeanTween.pause(tweenId);
        }
      }
    }
  }

  public void ForceAnimation() {
    string temp = currentAnimation;
    currentAnimation = null;
    PlayAnimation(temp);
  }

  public void PlayAnimation(string anim) {
    if (currentAnimation == anim) return; // TODO potentially a conflict, check for animations that can play on themselves
    if (currentAnimation == null) {
      currentAnimation = anim;
      nextAnimation = null;
    }
    else if (Interrupts.interrupts.ContainsKey(currentAnimation) && Interrupts.interrupts[currentAnimation].ContainsKey(anim)) {
      currentAnimation = Interrupts.interrupts[currentAnimation][anim];
      nextAnimation = anim;
    }
    else {
      return;
    }
    CancelAllBounceTweens();
    var category = currentAnimation;
    if (EsperanzaAnimations.animations[currentAnimation].To) {
      category = "To";
    }
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
    if (!isPlaying || currentAnimation == null) return;
    if (!EsperanzaAnimations.animations.ContainsKey(currentAnimation)) return;
    var anim = EsperanzaAnimations.animations[currentAnimation];
    var fSlowDown = slowDown ? 10f : 1f;
    _animationTimer += (Time.deltaTime * 1000f) / fSlowDown;
    float normalTime = _animationTimer / anim.duration;
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
        if (anim.loop || forceLoop) {
          currentFrame = anim.start;
          pingPong = false;
          _animationTimer = 0f;
          SetBounces();
        }
        else {
          currentFrame = anim.end;
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
    if (needsFlip) {
      var direction = 1; // right
      needsFlip = false;
      if (isFacingRight) {
        isFacingRight = false;
        direction = -1;
      }
      else {
        isFacingRight = true;
        direction = 1;
      }
      scaleVector.x = direction;
      gameObject.transform.localScale = scaleVector;
      SetBounces();
    }
    foreach (GameObject go in GearObjects) {
      go.GetComponent<SpriteWithNormals>().UpdateSpriteAndNormal(currentFrame);
    }
    foreach (GameObject go in SkinObjects) {
      go.GetComponent<SpriteWithNormals>().UpdateSpriteAndNormal(currentFrame);
    }
  }

  public void SetBounces() {
    CancelAllBounceTweens();
    foreach (KeyValuePair<string, Dictionary<string, List<BounceFrame>>> partPair in BounceAdjustments.adjustments) {
      string partKey = partPair.Key; // e.g., "Hair", "HairBack", "HairLeft"
      var animationDict = partPair.Value; // Dictionary of animations for this part
      if (!animationDict.ContainsKey(currentAnimation)) continue;
      var frameSequence = animationDict[currentAnimation];
      foreach (GameObject bounceParent in combinedBounces) {
        if (!isPlaying) break;
        if (bounceParent.name.Equals(partKey)) {
          StartCoroutine(PlayBounceSequence(bounceParent, frameSequence));
          break; // Found the matching bounce parent, no need to continue this inner loop
        }
        
      }
    }
  }

  private IEnumerator PlayBounceSequence(GameObject bounceParent, List<BounceFrame> sequence) {
    if (!bounceTweens.ContainsKey(bounceParent)) {
      bounceTweens[bounceParent] = new List<int>();
    }
    foreach (BounceFrame frame in sequence) {
      if (!isPlaying) break;
      var fSlowDown = slowDown ? 10f : 1f;
      Vector3 targetPos = new Vector3(frame.x, frame.y, bounceParent.transform.localPosition.z);
      int tweenId = LeanTween.moveLocal(bounceParent, targetPos, frame.duration * fSlowDown)
        .setEase(LeanTweenType.linear).id;
      int tweenId2 = LeanTween.scaleX(bounceParent, frame.offset, frame.duration * fSlowDown)
        .setEase(LeanTweenType.linear).id;
      bounceTweens[bounceParent].Add(tweenId);
      bounceTweens[bounceParent].Add(tweenId2);
      yield return new WaitForSeconds(frame.duration * fSlowDown);
    }
    if (bounceTweens.ContainsKey(bounceParent)) {
      bounceTweens[bounceParent].Clear();
    }
    if (slowDown) {
      Debug.Log("Toggling pause after bounce sequence");
      TogglePause("true");
    }
  }
}