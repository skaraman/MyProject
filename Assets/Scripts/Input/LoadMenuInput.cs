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
  bool slotSelectionLocked;

  void Start() {
    actions.Add(MessageBus.On("openLoadMenu", o => ResetSaveSlotInteractionLock()));
    actions.Add(MessageBus.On("loadMenu.cancel", o => BackOut()));
    actions.Add(MessageBus.On("loadMenu.delete", o => DeleteDir()));
    actions.Add(MessageBus.On("loadMenu.down", o => MenuDown()));
    actions.Add(MessageBus.On("loadMenu.select", o => Select()));
    actions.Add(MessageBus.On("loadMenu.up", o => MenuUp()));
    actions.Add(MessageBus.On("loadMenu.hover", o => MouseHover(o)));
    actions.Add(MessageBus.On("loadMenu.click", o => BeginClick()));
    ResetSaveSlotInteractionLock();
  }

  void OnDestroy() {
    for (int i = 0; i < actions.Count; i++) actions[i].Invoke();
    actions.Clear();
  }

  void Update() {
    HandleMouseInput();
  }

  void HandleMouseInput() {
    var mouse = Mouse.current;
    if (mouse == null) return;

    // Handle mouse press start
    if (mouse.leftButton.wasPressedThisFrame) {
      BeginClick();
    }

    // Handle dragging while pressed
    if (pressed && mouse.leftButton.isPressed) {
      Vector2 currentPos = mouse.position.ReadValue();
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
    if (mouse.leftButton.wasReleasedThisFrame && pressed) {
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
    if (slotSelectionLocked) return;
    var mouse = Mouse.current;
    if (mouse == null) return;

    pressPosition = mouse.position.ReadValue();
    pressed = true;
    dragging = false;
  }

  float ScreenToWorldY(float screenY) {
    return Camera.main.ScreenToWorldPoint(new Vector3(0, screenY, Camera.main.nearClipPlane)).y;
  }

  void DetectClickOnChild() {
    var mouse = Mouse.current;
    if (mouse == null) return;

    Vector2 screenPos = mouse.position.ReadValue();

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

    if (!slotSelectionLocked) {
      foreach (var hit in hits) {
        if (hit?.gameObject == null) continue;
        var hitIndex = ResolveButtonIndex(hit.gameObject);
        if (hitIndex >= 0) {
          activeIndexLoadMenu = hitIndex;
          SetActiveIndex(hitIndex);
          Select();
          return;
        }
      }
    }

    foreach (var hit in hits) {
      if (hit?.gameObject != null) {
        if (IsObjectOrChildOf(hit.gameObject, closeButton)) {
          BackOut();
        }
      }
    }
  }

  void BackOut() {
    MessageBus.Send("backToMainMenu");
  }

  void DeleteDir() {
    if (slotSelectionLocked) return;
    if (TryResolveSelectedSlotNumber(out var slotNumber)) {
      SaveSlotManager.Delete(slotNumber);
    }
  }

  void MouseHover(object target) {
    if (slotSelectionLocked) return;
    if (target is GameObject go) {
      activeIndexLoadMenu = ResolveButtonIndex(go);
      if (activeIndexLoadMenu >= 0) {
        SetActiveIndex(activeIndexLoadMenu);
      }
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
    if (slotSelectionLocked) return;
    if (buttons.Count == 0) return;

    if (activeIndexLoadMenu < 0) activeIndexLoadMenu = 0;
    else activeIndexLoadMenu = (activeIndexLoadMenu + 1) % buttons.Count;

    SetActiveIndex(activeIndexLoadMenu);
  }

  void MenuUp() {
    if (slotSelectionLocked) return;
    if (buttons.Count == 0) return;

    if (activeIndexLoadMenu < 0) activeIndexLoadMenu = buttons.Count - 1;
    else activeIndexLoadMenu = (activeIndexLoadMenu - 1 + buttons.Count) % buttons.Count;

    SetActiveIndex(activeIndexLoadMenu);
  }

  void Select() {
    if (slotSelectionLocked) return;
    if (TryResolveSelectedSlotNumber(out var slotNumber)) {
      LockSaveSlotInteraction();
      SaveSlotManager.SetSlot(slotNumber);
      ApplyLocationFromSelectedSlot();
      Debug.Log($"Slot set {SaveSlotManager.slot}");
      MessageBus.Send("startGame");
    }
  }

  void ResetSaveSlotInteractionLock() {
    slotSelectionLocked = false;
    SetSaveSlotInteractionEnabled(true);
  }

  void LockSaveSlotInteraction() {
    if (slotSelectionLocked) return;
    slotSelectionLocked = true;
    SetSaveSlotInteractionEnabled(false);
    pressed = false;
    dragging = false;
  }

  void SetSaveSlotInteractionEnabled(bool enabled) {
    for (int i = 0; i < buttons.Count; i++) {
      var button = buttons[i];
      if (button == null) continue;
      var colliders = button.GetComponentsInChildren<Collider2D>(includeInactive: true);
      for (int j = 0; j < colliders.Length; j++) {
        var collider = colliders[j];
        if (collider == null) continue;
        collider.enabled = enabled;
      }
    }
  }

  void ApplyLocationFromSelectedSlot() {
    var loadedSlot = SaveSlotManager.Load("slot");
    if (loadedSlot == null || !loadedSlot.TryGetValue("location", out var locationValue)) return;

    var requestedLocation = Convert.ToString(locationValue);
    if (string.IsNullOrWhiteSpace(requestedLocation)) return;

    var resolvedLocation = LocationEnemyData.ResolveRequestedOrDefault(requestedLocation);
    if (string.IsNullOrWhiteSpace(resolvedLocation)) return;

    LocationManager.UpdateLocation(resolvedLocation);
    MessageBus.Send("RequestLocationLoad", resolvedLocation);
  }

  int ResolveButtonIndex(GameObject hitObject) {
    if (hitObject == null) return -1;

    var directIndex = buttons.IndexOf(hitObject);
    if (directIndex >= 0) return directIndex;

    var hitTransform = hitObject.transform;
    for (int i = 0; i < buttons.Count; i++) {
      var button = buttons[i];
      if (button == null) continue;
      if (hitTransform.IsChildOf(button.transform)) {
        return i;
      }
    }

    return -1;
  }

  bool IsObjectOrChildOf(GameObject hitObject, GameObject targetRoot) {
    if (hitObject == null || targetRoot == null) return false;
    return hitObject == targetRoot || hitObject.transform.IsChildOf(targetRoot.transform);
  }

  bool TryResolveSelectedSlotNumber(out int slotNumber) {
    slotNumber = -1;

    if (activeIndexLoadMenu < 0 || activeIndexLoadMenu >= buttons.Count) return false;

    var selectedButton = buttons[activeIndexLoadMenu];
    if (selectedButton == null) return false;

    var slot = selectedButton.GetComponent<SaveSlot>();
    if (slot != null && int.TryParse(slot.saveNumber, out slotNumber)) {
      return true;
    }

    slotNumber = activeIndexLoadMenu + 1;
    return true;
  }
}
