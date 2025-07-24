using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class MouseManager : MonoBehaviour {
  public static MouseManager Instance;
  GameObject lastHovered;
  string map = "default";
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
  Dictionary<string, (
    string hover, string exit, string click, string release,
    string rightclick, string rightrelease, string middleclick, string middlerelease,
    string scrollUp, string scrollDown
  )> cachedKeys = new();

  Vector3 lastPos;

  void Awake() {
    Instance = this;
    Swap("mainMenu");
  }

  void Update() {
    Vector3 screenPos = Mouse.current.position.ReadValue();
    if (screenPos == lastPos) return;
    lastPos = screenPos;

    var worldPos = Camera.main.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 1f));
    var hit = Physics2D.OverlapPoint(worldPos);
    var target = hit ? hit.gameObject : null;

    if (target != lastHovered) {
      if (lastHovered) MessageBus.Send(exitKey, lastHovered);
      if (target) MessageBus.Send(hoverKey, target);
      lastHovered = target;
    }

    if (target) {
      if (Mouse.current.leftButton.wasPressedThisFrame) MessageBus.Send(clickKey, target);
      if (Mouse.current.leftButton.wasReleasedThisFrame) MessageBus.Send(releaseKey, target);
      if (Mouse.current.rightButton.wasPressedThisFrame) MessageBus.Send(rightClickKey, target);
      if (Mouse.current.rightButton.wasReleasedThisFrame) MessageBus.Send(rightReleaseKey, target);
      if (Mouse.current.middleButton.wasPressedThisFrame) MessageBus.Send(middleClickKey, target);
      if (Mouse.current.middleButton.wasReleasedThisFrame) MessageBus.Send(middleReleaseKey, target);
    }

    var scroll = Mouse.current.scroll.ReadValue();
    if (scroll.y > 0) MessageBus.Send(scrollUpKey, scroll.y);
    else if (scroll.y < 0) MessageBus.Send(scrollDownKey, scroll.y);
  }

  public void Swap(string newMap) {
    map = newMap;
    if (!cachedKeys.TryGetValue(map, out var keys)) {
      keys = ( $"{map}.button.hover", $"{map}.button.exit", $"{map}.button.click", $"{map}.button.release",
        $"{map}.button.rightClick", $"{map}.button.rightRelease", $"{map}.button.middleClick", $"{map}.button.middleRelease",
        $"{map}.button.scrollUp", $"{map}.button.scrollDown"
      );
      cachedKeys[map] = keys;
    }
    hoverKey = keys.hover;
    exitKey = keys.exit;
    clickKey = keys.click;
    releaseKey = keys.release;
    rightClickKey = keys.rightclick;
    rightReleaseKey = keys.rightrelease;
    middleClickKey = keys.middleclick;
    middleReleaseKey = keys.middlerelease;
    scrollUpKey = keys.scrollUp;
    scrollDownKey = keys.scrollDown;
    Debug.Log($"[MouseManager] Swapped to: {map}");
  }
}
