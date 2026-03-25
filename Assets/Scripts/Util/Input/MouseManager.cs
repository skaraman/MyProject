using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class MouseManager : MonoBehaviour {
  public string defaultMap;
  public static MouseManager Instance;
  readonly List<Collider2D> overlapResults = new();
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
  string currentMap;

  private Camera mainCamera;
  private Mouse mouse;
  ContactFilter2D overlapFilter;

  void Awake() {
    Instance = this;
    overlapFilter.useLayerMask = false;
    overlapFilter.useDepth = false;
    overlapFilter.useOutsideDepth = false;
    overlapFilter.useNormalAngle = false;
    overlapFilter.useOutsideNormalAngle = false;
    overlapFilter.useTriggers = true;
    SwitchMap(defaultMap != "" ? defaultMap : "mainMenu");
  }

  void Start() {
    mainCamera = Camera.main;
    mouse = Mouse.current;
  }

  void Update() {
    clickCacheTimer -= Time.unscaledDeltaTime;
    if (clickCacheTimer < 0f) clickCacheTimer = 0f;

    if (!mainCamera || mouse == null) return;

    var screenPos = mouse.position.ReadValue();
    var worldPos = mainCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 1f));
    var target = ResolvePointTarget(worldPos);

    if (target != lastHovered) {
      UpdateHoverTarget(target);
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
    if (lastHovered) {
      MessageBus.Send(exitKey, lastHovered);
    }
    lastHovered = null;
    lastClickedTarget = null;
    clickCacheTimer = 0f;
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
    currentMap = string.IsNullOrWhiteSpace(newMap) ? "" : newMap.Trim();
    Debug.Log($"[MouseManager] Swapped to: {newMap}");
  }

  GameObject ResolvePointTarget(Vector3 worldPos) {
    overlapResults.Clear();
    Physics2D.OverlapPoint((Vector2)worldPos, overlapFilter, overlapResults);
    if (overlapResults.Count <= 0) {
      return null;
    }

    if (overlapResults.Count == 1) {
      var collider = overlapResults[0];
      return collider != null ? collider.gameObject : null;
    }

    var bestTarget = overlapResults[0] != null ? overlapResults[0].gameObject : null;
    var bestPriority = ResolveTargetPriority(bestTarget);
    for (var i = 1; i < overlapResults.Count; i++) {
      var candidateCollider = overlapResults[i];
      var candidateTarget = candidateCollider != null ? candidateCollider.gameObject : null;
      var candidatePriority = ResolveTargetPriority(candidateTarget);
      if (candidatePriority <= bestPriority) {
        continue;
      }

      bestPriority = candidatePriority;
      bestTarget = candidateTarget;
    }

    return bestTarget;
  }

  int ResolveTargetPriority(GameObject target) {
    if (target == null) {
      return int.MinValue;
    }

    var priority = 0;
    if (IsTargetInCurrentUiRoot(target)) {
      priority += 1000;
    }

    if (target.layer == 6) {
      priority += 100;
    }
    else if (target.layer == 8) {
      priority += 90;
    }

    return priority;
  }

  bool IsTargetInCurrentUiRoot(GameObject target) {
    if (target == null) {
      return false;
    }

    switch (currentMap) {
      case "mainMenu":
        return HasAncestorNamed(target.transform, "MainMenu");
      case "loadMenu":
        return HasAncestorNamed(target.transform, "LoadMenu");
      case "settingsMenu":
        return HasAncestorNamed(target.transform, "SettingsMenu");
      case "pauseMenu":
        return HasAncestorNamed(target.transform, "PauseMenu");
      default:
        return false;
    }
  }

  static bool HasAncestorNamed(Transform target, string rootName) {
    if (target == null || string.IsNullOrWhiteSpace(rootName)) {
      return false;
    }

    var current = target;
    while (current != null) {
      if (string.Equals(current.name, rootName, System.StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
      current = current.parent;
    }

    return false;
  }

  void UpdateHoverTarget(GameObject target) {
    if (lastHovered) {
      MessageBus.Send(exitKey, lastHovered);
    }
    if (target) {
      MessageBus.Send(hoverKey, target);
    }
    lastHovered = target;
  }
}
