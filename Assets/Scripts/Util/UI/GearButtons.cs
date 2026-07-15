using System;
using System.Collections.Generic;
using UnityEngine;

public class GearButtons : ButtonGroup {
  readonly List<Action> actions = new();

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
    // Swap sprite to "active" variant
    button.GetComponent<ReferenceListGameObject>().Get(0).SetActive(true);
    button.GetComponent<ReferenceListGameObject>().Get(1).SetActive(false);
  }

  protected override void HandleInactiveState(GameObject button) {
    // Swap sprite to "idle" variant
    button.GetComponent<ReferenceListGameObject>().Get(0).SetActive(false);
    button.GetComponent<ReferenceListGameObject>().Get(1).SetActive(true);
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
    var newColor = ShaderColors.myColors[gearItem.gearColor];
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
        " color='" + (gearItem.gearColor ?? "") + "'"
      );
    }
  }
}
