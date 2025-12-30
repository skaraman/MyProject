using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class MouseManager : MonoBehaviour {
  public string defaultMap;
  public static MouseManager Instance;
  GameObject lastHovered;
  GameObject lastClickedTarget;
  float clickCacheTimer;
  const float clickCacheDuration = 0.1f;

  string hoverKey;
  string exitKey;
  string clickKey;
  string releaseKey;
  string rightClickKey;
  string rightReleaseKey;
  string middleClickKey;
  string middleReleaseKey;
  string scrollUpKey;
  string scrollDownKey;
  Vector3 lastScreenPos;

  private Camera mainCamera;
  private Mouse mouse;

  void Awake() {
    Instance = this;
    SwitchMap(defaultMap != "" ? defaultMap : "mainMenu");
  }

  void Start() {
    mainCamera = Camera.main;
    mouse = Mouse.current;
  }

  void Update() {
    clickCacheTimer -= Time.unscaledDeltaTime;

    if (!mainCamera || mouse == null) return;

    var screenPos = mouse.position.ReadValue();
    var worldPos = mainCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 1f));
    var hit = Physics2D.OverlapPoint(worldPos);
    var target = hit ? hit.gameObject : null;

    if (target != lastHovered) {
      if (lastHovered) MessageBus.Send(exitKey, lastHovered);
      if (target) MessageBus.Send(hoverKey, target);
      lastHovered = target;
    }
    else if (target) {
      MessageBus.Send(hoverKey, target);
    }

    if (target) {
      if (mouse.leftButton.wasPressedThisFrame) {
        lastClickedTarget = target;
        clickCacheTimer = clickCacheDuration;
        MessageBus.Send(clickKey, target);
        // Debug.Log($"[MouseManager] Left Click on: {target.name}");
      }

      if (mouse.leftButton.wasReleasedThisFrame) {
        var releaseTarget = clickCacheTimer > 0 ? lastClickedTarget : target;
        MessageBus.Send(releaseKey, releaseTarget);
        // Debug.Log($"[MouseManager] Left Release on: {releaseTarget?.name}");
      }

      if (mouse.rightButton.wasPressedThisFrame) {
        MessageBus.Send(rightClickKey, target);
        // Debug.Log($"[MouseManager] Right Click on: {target.name}");
      }

      if (mouse.rightButton.wasReleasedThisFrame) {
        MessageBus.Send(rightReleaseKey, target);
        // Debug.Log($"[MouseManager] Right Release on: {target.name}");
      }

      if (mouse.middleButton.wasPressedThisFrame) {
        MessageBus.Send(middleClickKey, target);
        // Debug.Log($"[MouseManager] Middle Click on: {target.name}");
      }

      if (mouse.middleButton.wasReleasedThisFrame) {
        MessageBus.Send(middleReleaseKey, target);
        // Debug.Log($"[MouseManager] Middle Release on: {target.name}");
      }
    }

    var scroll = mouse.scroll.ReadValue();
    if (scroll.y > 0) MessageBus.Send(scrollUpKey, scroll.y);
    else if (scroll.y < 0) MessageBus.Send(scrollDownKey, scroll.y);
  }

  public void SwitchMap(string newMap) {
    hoverKey = $"{newMap}.hover";
    exitKey = $"{newMap}.unhover";
    clickKey = $"{newMap}.click";
    releaseKey = $"{newMap}.release";
    rightClickKey = $"{newMap}.rightClick";
    rightReleaseKey = $"{newMap}.rightRelease";
    middleClickKey = $"{newMap}.middleClick";
    middleReleaseKey = $"{newMap}.middleRelease";
    scrollUpKey = $"{newMap}.scrollUp";
    scrollDownKey = $"{newMap}.scrollDown";
    Debug.Log($"[MouseManager] Swapped to: {newMap}");
  }
}
