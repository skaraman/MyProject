using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class LoadMenuInput : ButtonGroup {
  int activeIndexLoadMenu = -1;
  List<Action> actions = new();
  public GameObject scrollWrap;
  public SaveSlotView scrollView;
  public GameObject closeButton;

  bool pressed = false;
  bool dragging = false;
  Vector2 pressPosition;
  float lastRatioY;
  float dragThreshold = 5f;

  void Start() {
    actions.Add(MessageBus.On("loadMenu.cancel", o => BackOut()));
    actions.Add(MessageBus.On("loadMenu.delete", o => DeleteDir()));
    actions.Add(MessageBus.On("loadMenu.down", o => MenuDown()));
    actions.Add(MessageBus.On("loadMenu.select", o => Select()));
    actions.Add(MessageBus.On("loadMenu.up", o => MenuUp()));
    actions.Add(MessageBus.On("loadMenu.hover", o => MouseHover(o)));
    actions.Add(MessageBus.On("loadMenu.click", o => BeginClick()));

  }

  void OnDestroy() {
    for (int i = 0; i < actions.Count; i++) actions[i].Invoke();
    actions.Clear();
  }

  void Update() {
    HandleMouseInput();
  }

  void HandleMouseInput() {
    // Handle mouse press start
    if (Mouse.current.leftButton.wasPressedThisFrame) {
      BeginClick();
    }

    // Handle dragging while pressed
    if (pressed && Mouse.current.leftButton.isPressed) {
      var currentPos = Mouse.current.position.ReadValue();
      var dist = Vector2.Distance(currentPos, pressPosition);

      if (!dragging && dist > dragThreshold) {
        // Start dragging
        dragging = true;
        var worldY = ScreenToWorldY(currentPos.y);
        var yh = scrollView.GetVisualHeight();
        lastRatioY = worldY / yh;
      }

      if (dragging) {
        // Continue dragging
        var worldY = ScreenToWorldY(currentPos.y);
        var yh = scrollView.GetVisualHeight();
        var currentRatioY = worldY / yh;
        var delta = currentRatioY - lastRatioY;
        lastRatioY = currentRatioY;
        
        for (int i = 0; i < scrollWrap.transform.childCount; i++) {
          var child = scrollWrap.transform.GetChild(i);
          child.localPosition = new Vector3(0, child.localPosition.y + delta * 25f, 0);
        }
      }
    }

    // Handle mouse release
    if (Mouse.current.leftButton.wasReleasedThisFrame && pressed) {
      if (!dragging) {
        // Only trigger click if we weren't dragging
        DetectClickOnChild();
      }

      // Reset states
      pressed = false;
      dragging = false;
    }
  }

  void BeginClick() {
    pressPosition = Mouse.current.position.ReadValue();
    pressed = true;
    dragging = false;
  }

  float ScreenToWorldY(float screenY) {
    return Camera.main.ScreenToWorldPoint(new Vector3(0, screenY, Camera.main.nearClipPlane)).y;
  }

  void DetectClickOnChild() {
    var screenPos = Mouse.current.position.ReadValue();

    if (Camera.main == null) {
      Debug.LogError("Camera.main is null - cannot convert screen to world position");
      return;
    }

    var worldPos = Camera.main.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, Camera.main.nearClipPlane));
    var point2D = new Vector2(worldPos.x, worldPos.y);

    var hits = Physics2D.OverlapPointAll(point2D);

    if (hits.Length == 0) {
      return;
    }

    System.Array.Sort(hits, (a, b) => {
      if (a?.transform == null || b?.transform == null) return 0;
      return b.transform.position.z.CompareTo(a.transform.position.z);
    });

    foreach (var hit in hits) {
      if (hit?.gameObject == null) continue;
      var hitIndex = buttons.FindIndex(b => b != null && b == hit.gameObject);
      if (hitIndex >= 0) {
        activeIndexLoadMenu = hitIndex;
        SetActiveIndex(hitIndex);
        Select();
        return;
      }
    }

    foreach (var hit in hits) {
      if (hit?.gameObject != null) {
        if (hit.gameObject == closeButton) {
          BackOut();
        }
      }
    }
  }

  void BackOut() {
    MessageBus.Send("backToMainMenu");
  }

  void DeleteDir() {
    if (activeIndexLoadMenu >= 0) {
      SaveSlotManager.Delete(activeIndexLoadMenu);
    }
  }

  void MouseHover(object target) {
    if (target is GameObject go) {
      activeIndexLoadMenu = buttons.IndexOf(go);
    }
  }

  protected override void HandleActiveState(GameObject button) {
    var shader = button.GetComponent<ReferenceListAllIn1AnimatorInspector>().Get(0);
    shader.SetKeyword("OUTBASE_ON", true);
  }

  protected override void HandleInactiveState(GameObject button) {
    var shader = button.GetComponent<ReferenceListAllIn1AnimatorInspector>().Get(0);
    shader.SetKeyword("OUTBASE_ON", false);
  }

  void MenuDown() {
    if (buttons.Count == 0) return;

    if (activeIndexLoadMenu < 0) activeIndexLoadMenu = 0;
    else activeIndexLoadMenu = (activeIndexLoadMenu + 1) % buttons.Count;

    SetActiveIndex(activeIndexLoadMenu);
  }

  void MenuUp() {
    if (buttons.Count == 0) return;

    if (activeIndexLoadMenu < 0) activeIndexLoadMenu = buttons.Count - 1;
    else activeIndexLoadMenu = (activeIndexLoadMenu - 1 + buttons.Count) % buttons.Count;

    SetActiveIndex(activeIndexLoadMenu);
  }

  void Select() {
    if (activeIndexLoadMenu >= 0) {
      SaveSlotManager.SetSlot(activeIndexLoadMenu + 1);
      Debug.Log($"Slot set {SaveSlotManager.slot}");
      MessageBus.Send("loadGame");
      MessageBus.Send("startGame");
    }
  }
}