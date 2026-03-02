using System;
using System.Collections.Generic;
using UnityEngine;


public class Avatar : MonoBehaviour {
  public SpriteWithNormals spriteWithNormals;
  private List<Action> actions = new();

  void OnDestroy() {
    foreach (var action in actions) {
      action();
    }
    actions.Clear();
  }

  void Awake() {
    actions.Add(MessageBus.On("gearReady", o => UpdateSprite()));
  }

  private void UpdateSprite() {
    // Update the sprite based on the current gear
    spriteWithNormals.labelPrefix = EsperanzaForms.GetActive();
    spriteWithNormals.ForceUpdateSpriteAndNormal();
  }

}
