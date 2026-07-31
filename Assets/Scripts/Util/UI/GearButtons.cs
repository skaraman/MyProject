using System;
using System.Collections.Generic;
using Esperanza.UI;
using UnityEngine;

public class GearButtons : ButtonGroup {
  const string BackingSiblingName = "backing";
  const string OutlineKeyword = "OUTBASE_ON";
  const string RingsContainerName = "RINGS";

  readonly List<Action> actions = new();

  [Header("Gear Preview")]
  [SerializeField] ItemCard itemCard;

  static bool ShouldLogRuntimeUiDebug() {
    if (!Application.isPlaying) return false;
    if (!SpriteStreamingRuntimeSettings.EnableVerboseRuntimeConsoleLogs) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }

  void OnEnable() {
    RegisterHandlers();
    OnGearReady(EsperanzaForms.GetActive());
  }

  void OnDisable() {
    itemCard?.Hide();
    UnregisterHandlers();
  }

  void RegisterHandlers() {
    if (actions.Count > 0) {
      return;
    }

    actions.Add(MessageBus.On(CharacterMessageTopics.GearReady, OnGearReady));
  }

  void UnregisterHandlers() {
    for (var i = 0; i < actions.Count; i++) {
      actions[i]?.Invoke();
    }
    actions.Clear();
  }

  protected override void HandleActiveState(GameObject button) {
    SetButtonVisualState(button, isActive: true);
    ShowItemCard(button);
  }

  protected override void HandleInactiveState(GameObject button) {
    SetButtonVisualState(button, isActive: false);
    if (GetActiveButton() == button) {
      itemCard?.Hide();
    }
  }

  void ShowItemCard(GameObject button) {
    if (itemCard == null || button == null) {
      return;
    }

    var formName = EsperanzaForms.GetActive();
    EquippedItems.EnsureForm(formName);
    if (!EquippedItems.AllGearForms.TryGetValue(formName, out var slots) ||
        slots == null ||
        !slots.TryGetValue(button.name, out var gearItem) ||
        gearItem == null) {
      itemCard.Hide();
      return;
    }

    itemCard.SetupGear(gearItem, button.GetComponent<SpriteWithNormals>());
  }

  static void SetButtonVisualState(GameObject button, bool isActive) {
    if (button == null) {
      return;
    }

    var parent = button.transform.parent;
    var isRingSlot = parent != null &&
      string.Equals(parent.name, RingsContainerName, StringComparison.Ordinal);
    if (isRingSlot) {
      SetBackingOutline(parent.Find(BackingSiblingName), isActive: false);
    }

    var independentOutline = button.GetComponentInChildren<RingSlotHoverOutline>(includeInactive: true);
    if (independentOutline != null) {
      independentOutline.SetHighlighted(isActive);
      return;
    }

    if (isRingSlot) {
      return;
    }

    SetBackingOutline(parent != null ? parent.Find(BackingSiblingName) : null, isActive);
  }

  static void SetBackingOutline(Transform backing, bool isActive) {
    if (backing == null) {
      return;
    }

    var backingAnimator = backing.GetComponent<AllIn1AnimatorInspector>();
    if (backingAnimator != null) {
      backingAnimator.SetKeyword(OutlineKeyword, isActive);
    }
  }

  public void OnGearReady(string form = null) {
    var resolvedForm = EsperanzaForms.ResolveFormKey(form) ?? EsperanzaForms.GetActive();
    EquippedItems.EnsureKnownForms();
    EquippedItems.EnsureForm(resolvedForm);

    var refreshedSlots = 0;
    for (var i = 0; i < buttons.Count; i++) {
      var button = buttons[i];
      if (button == null) {
        continue;
      }

      RefreshSlotButton(button, resolvedForm);
      refreshedSlots++;
    }

    if (ShouldLogRuntimeUiDebug()) {
      RuntimeLog.Log(
        "[GearButtons] Refreshed gear slot icons" +
        " form='" + resolvedForm + "'" +
        " slots=" + refreshedSlots
      );
    }
  }

  void RefreshSlotButton(GameObject button, string formName) {
    if (button == null || string.IsNullOrWhiteSpace(formName)) {
      return;
    }

    var sprite = button.GetComponent<SpriteWithNormals>();
    if (sprite == null) {
      Debug.LogWarning(
        "[GearButtons] Missing SpriteWithNormals" +
        " button='" + button.name + "'" +
        " form='" + formName + "'"
      );
      return;
    }

    var shaderAnimator = button.GetComponent<AllIn1AnimatorInspector>();
    EquippedItems.AllGearForms[formName].TryGetValue(button.name, out var gearItem);

    var nextLabelPrefix = gearItem != null ? gearItem.gearId ?? "" : "";
    sprite.SetDoNotRender(false);
    if (!string.Equals(sprite.labelPrefix ?? "", nextLabelPrefix, StringComparison.Ordinal)) {
      sprite.SetLabelPrefix(nextLabelPrefix);
    }
    sprite.ForceUpdateSpriteAndNormal();

    if (gearItem == null) {
      ResetSlotShader(button, shaderAnimator, formName);
      return;
    }

    ApplyGearColor(button, shaderAnimator, gearItem, formName);
  }

  static void ResetSlotShader(GameObject button, AllIn1AnimatorInspector shaderAnimator, string formName) {
    if (shaderAnimator != null) {
      shaderAnimator.ResetActive();
      shaderAnimator.Reset();
    }

    var spriteRenderer = button.GetComponent<SpriteRenderer>();
    if (spriteRenderer != null && spriteRenderer.color != Color.white) {
      spriteRenderer.color = Color.white;
    }

    if (ShouldLogRuntimeUiDebug()) {
      RuntimeLog.Log(
        "[GearButtons] Reset empty gear slot" +
        " form='" + formName + "'" +
        " slot='" + button.name + "'"
      );
    }
  }

  static void ApplyGearColor(GameObject button, AllIn1AnimatorInspector shaderAnimator, GearItem gearItem, string formName) {
    if (gearItem == null) {
      return;
    }

    var spriteRenderer = button.GetComponent<SpriteRenderer>();
    if (!ShaderColors.TryGetFormColor(
          formName,
          ShaderColors.PrimaryGroup,
          out var newColor,
          out var colorName
        )) {
      Debug.LogWarning(
        "[GearButtons] Unable to resolve active form color" +
        " form='" + (formName ?? "") + "'"
      );
      return;
    }
    if (shaderAnimator != null) {
      shaderAnimator.ResetActive();
      shaderAnimator.Reset();
      shaderAnimator.SetKeyword("GLOW_ON", true);
      shaderAnimator.AddFloatSequence("_Glow", 4f, 4f, 1f, replaceExisting: true);
      shaderAnimator.AddColorSequence("_GlowColor", newColor, newColor, 1f, replaceExisting: true);
      shaderAnimator.AddColorSequence("_Color", newColor, newColor, 1f, replaceExisting: true);
    }

    if (spriteRenderer != null && spriteRenderer.color != newColor) {
      spriteRenderer.color = newColor;
    }

    if (ShouldLogRuntimeUiDebug()) {
      RuntimeLog.Log(
        "[GearButtons] Applied gear slot icon" +
        " form='" + formName + "'" +
        " slot='" + button.name + "'" +
        " gear='" + (gearItem.gearId ?? "") + "'" +
        " color='" + (colorName ?? "") + "'"
      );
    }
  }
}
