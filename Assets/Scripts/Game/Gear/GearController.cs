using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;
using CustomInspector;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class GearController : MonoBehaviour {
  [Button(nameof(_TogglePause), label = "un/pause", size = Size.small)] public bool slowDown;
  [Button(nameof(ForceAnimation), label = "Play", size = Size.small)] public bool forceLoop;
  public string defaultAnimation = "Breathe";

  public GameObject[] GearObjects;
  public GameObject[] HairObjects;
  public GameObject[] OtherBounceGearObjects;
  public GameObject[] SkinObjects;
  public GameObject[] HBoxObjects;
  public GameObject HairSkin;
  public Dictionary<string, Dictionary<string, GearItem>> lastGear = new Dictionary<string, Dictionary<string, GearItem>>();
  public bool needsFlip;
  private GameObject[] combinedBounces;
  private SaveData gameData = new();
  private AnimationController animationController = new();

  public bool IsFacingRight => animationController != null && animationController.IsFacingRight;

  void Awake() {
    combinedBounces = (HairObjects ?? Array.Empty<GameObject>()).Concat(OtherBounceGearObjects ?? Array.Empty<GameObject>()).ToArray();
    ConfigureAnimationController();
  }

  void Start() {
    if (Application.isPlaying) {
      LeanTween.reset();
      LeanTween.init(4000);
    }
    animationController.PlayAnimation(defaultAnimation, true);
  }

  void Update() {
    if (animationController == null) return;
    animationController.SlowDown = slowDown;
    animationController.ForceLoop = forceLoop;
    if (needsFlip) {
      animationController.QueueFlip();
      needsFlip = false;
    }
  }

  void FixedUpdate() {
    animationController?.Tick(Time.deltaTime);
  }

  private void ConfigureAnimationController() {
    if (animationController == null) return;
    var spriteTargets = (GearObjects ?? Array.Empty<GameObject>()).Concat(SkinObjects ?? Array.Empty<GameObject>());
    animationController.Initialize(
      transform,
      spriteTargets,
      combinedBounces,
      HBoxObjects,
      Animations.Esperanza,
      Interrupts.Esperanza,
      BounceAdjustments.Esperanza,
      HBoxes.Esperanza,
      defaultAnimation,
      false
    );
  }

  public void LoadGear() {
    GetSavedGearState();
    RefreshGear();
    MessageBus.Send("gearReady");
  }

  void OnDestroy() {
#if UNITY_EDITOR
    if (!Application.isPlaying) {
      Selection.activeObject = null;
    }
#endif
    animationController?.Cleanup(!Application.isPlaying);
  }

  void OnDisable() {
    animationController?.Cleanup(false);
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
              shaderAnimator.ResetActive();
              shaderAnimator.Reset();
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
              shaderAnimator.ResetActive();
              shaderAnimator.Reset();
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

  public string CurrentAnimation => animationController != null ? animationController.CurrentAnimation : null;

  public void _TogglePause() {
    TogglePause();
  }

  public void TogglePause(string forcePause = null) {
    animationController?.TogglePause(forcePause);
  }

  public void ForceAnimation() {
    if (animationController == null) return;
    animationController.ForceAnimation(defaultAnimation);
  }

  public void PlayAnimation(string anim) {
    animationController?.PlayAnimation(anim);
  }

  public AnimationController Controller => animationController;
}
