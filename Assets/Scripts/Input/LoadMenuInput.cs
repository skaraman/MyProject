using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class LoadMenuInput : ButtonGroup {
  [SerializeField, Min(1f)] float dragScrollMultiplier = 45f;
  [SerializeField, Min(0.01f)] float mouseWheelScrollUnitsPerTick = 3f;
  [SerializeField, Min(0.01f)] float gamepadStickScrollUnitsPerSecond = 18f;
  [SerializeField, Range(0f, 0.99f)] float gamepadStickDeadzone = 0.25f;
  [SerializeField, Min(0f)] float keyboardScrollMargin = 0.5f;
  int activeIndexLoadMenu = -1;
  List<Action> actions = new();
  public GameObject scrollWrap;
  public SaveSlotView scrollView;
  public GameObject closeButton;
  public SaveSlot[] slots;
  private static readonly Collider2D[] _overlapHits = new Collider2D[32];

  bool pressed = false;
  bool dragging = false;
  Vector2 pressPosition;
  float lastRatioY;
  float dragThreshold = 5f;
  bool slotSelectionLocked;

  void Start() {
    actions.Add(MessageBus.On("openLoadMenu", o => ResetSaveSlotInteractionLock()));
    actions.Add(MessageBus.On("loadMenu.cancel", o => { if (InputMessageValue.IsPressed(o)) BackOut(); }));
    actions.Add(MessageBus.On("loadMenu.delete", o => { if (InputMessageValue.IsPressed(o)) RequestDeleteConfirm(); }));
    actions.Add(MessageBus.On("loadMenu.down", o => { if (InputMessageValue.IsPressed(o)) MenuDown(); }));
    actions.Add(MessageBus.On("loadMenu.scrollDown", o => ScrollFromMouseWheel(o, direction: -1f)));
    actions.Add(MessageBus.On("loadMenu.select", o => { if (InputMessageValue.IsPressed(o)) Select(); }));
    actions.Add(MessageBus.On("loadMenu.up", o => { if (InputMessageValue.IsPressed(o)) MenuUp(); }));
    actions.Add(MessageBus.On("loadMenu.scrollUp", o => ScrollFromMouseWheel(o, direction: 1f)));
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
    HandleGamepadScroll();
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
        ScrollContent(delta * dragScrollMultiplier);
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

  void ScrollFromMouseWheel(object payload, float direction) {
    if (slotSelectionLocked || dragging) return;
    var scrollValue = CoerceFloat(payload);
    var wheelSteps = Mathf.Abs(scrollValue) > Mathf.Epsilon
      ? Mathf.Max(1f, Mathf.Abs(scrollValue) / 120f)
      : 1f;
    ScrollContent(Mathf.Sign(direction) * wheelSteps * mouseWheelScrollUnitsPerTick);
  }

  void HandleGamepadScroll() {
    if (slotSelectionLocked || pressed || dragging) return;

    var gamepad = Gamepad.current;
    if (gamepad == null) return;

    var stickY = gamepad.rightStick.ReadValue().y;
    var magnitude = Mathf.Abs(stickY);
    if (magnitude <= gamepadStickDeadzone) return;

    var normalizedMagnitude = Mathf.InverseLerp(gamepadStickDeadzone, 1f, magnitude);
    var delta = Mathf.Sign(stickY) * normalizedMagnitude * gamepadStickScrollUnitsPerSecond * Time.unscaledDeltaTime;
    ScrollContent(delta);
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

  void ScrollContent(float deltaY) {
    if (Mathf.Abs(deltaY) <= Mathf.Epsilon) return;

    if (scrollView != null) {
      scrollView.ScrollBy(deltaY);
      return;
    }

    if (scrollWrap == null) return;

    var wrapTransform = scrollWrap.transform;
    for (int i = 0; i < wrapTransform.childCount; i++) {
      var child = wrapTransform.GetChild(i);
      child.localPosition = new Vector3(child.localPosition.x, child.localPosition.y + deltaY, child.localPosition.z);
    }
  }

  static float CoerceFloat(object value) {
    if (value is float f) return f;
    if (value is double d) return (float)d;
    if (value is int i) return i;
    if (value is bool b) return b ? 1f : 0f;
    return 0f;
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

    var filter = ContactFilter2D.noFilter;
    var hitCount = Physics2D.OverlapPoint(point2D, filter, _overlapHits);

    if (hitCount == 0) {
      return;
    }

    System.Array.Sort(_overlapHits, 0, hitCount, System.Collections.Generic.Comparer<Collider2D>.Create((a, b) => {
      if (a?.transform == null || b?.transform == null) return 0;
      return b.transform.position.z.CompareTo(a.transform.position.z);
    }));

    if (!slotSelectionLocked) {
      for (var i = 0; i < hitCount; i++) {
        var hit = _overlapHits[i];
        if (hit?.gameObject == null) continue;
        if (hit.gameObject.name == "Delete") {
          var slotIndex = ResolveButtonIndex(hit.gameObject);
          if (slotIndex >= 0) {
            activeIndexLoadMenu = slotIndex;
            SetActiveIndex(slotIndex);
            RequestDeleteConfirm();
            return;
          }
        }
      }

      for (var i = 0; i < hitCount; i++) {
        var hit = _overlapHits[i];
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

    for (var i = 0; i < hitCount; i++) {
      var hit = _overlapHits[i];
      if (hit?.gameObject != null) {
        if (IsObjectOrChildOf(hit.gameObject, closeButton)) {
          BackOut();
        }
      }
    }
  }

  void BackOut() {
    if (slotSelectionLocked) return;
    MessageBus.Send("closeLoadMenu");
  }

  void RequestDeleteConfirm() {
    if (slotSelectionLocked) return;
    if (TryResolveSelectedSlotNumber(out var slotNumber)) {
      if (!SaveSlotManager.SlotExists(slotNumber)) return;
      MessageBus.Send("loadMenu.deleteConfirmOpen", slotNumber);
    }
  }

  void MouseHover(object target) {
    if (slotSelectionLocked) return;
    if (target is GameObject go) {
      var resolvedIndex = ResolveButtonIndex(go);
      if (resolvedIndex >= 0 && resolvedIndex != activeIndexLoadMenu) {
        activeIndexLoadMenu = resolvedIndex;
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
    ScrollActiveSlotIntoView();
  }

  void MenuUp() {
    if (slotSelectionLocked) return;
    if (buttons.Count == 0) return;

    if (activeIndexLoadMenu < 0) activeIndexLoadMenu = buttons.Count - 1;
    else activeIndexLoadMenu = (activeIndexLoadMenu - 1 + buttons.Count) % buttons.Count;

    SetActiveIndex(activeIndexLoadMenu);
    ScrollActiveSlotIntoView();
  }

  void ScrollActiveSlotIntoView() {
    if (scrollWrap == null) return;
    if (activeIndexLoadMenu < 0 || activeIndexLoadMenu >= buttons.Count) return;

    if (scrollView != null &&
        scrollView.ScrollSlotIntoView(activeIndexLoadMenu, keyboardScrollMargin)) {
      return;
    }

    var selectedButton = buttons[activeIndexLoadMenu];
    if (selectedButton == null) return;

    if (!TryResolveViewportBounds(out var viewportBounds)) return;
    if (!TryResolveSlotBounds(selectedButton, out var selectedBounds)) return;

    var topLimit = viewportBounds.max.y - keyboardScrollMargin;
    var bottomLimit = viewportBounds.min.y + keyboardScrollMargin;
    var deltaY = 0f;

    if (selectedBounds.max.y > topLimit) {
      deltaY = topLimit - selectedBounds.max.y;
    }
    else if (selectedBounds.min.y < bottomLimit) {
      deltaY = bottomLimit - selectedBounds.min.y;
    }

    ScrollContent(deltaY);
  }

  bool TryResolveViewportBounds(out Bounds bounds) {
    bounds = default;
    if (scrollWrap == null) return false;

    var collider = scrollWrap.GetComponent<Collider2D>();
    if (collider == null) return false;

    bounds = collider.bounds;
    return true;
  }

  bool TryResolveSlotBounds(GameObject target, out Bounds bounds) {
    bounds = default;
    if (target == null) return false;

    var collider = target.GetComponent<Collider2D>();
    if (collider != null) {
      bounds = collider.bounds;
      return true;
    }

    bounds = new Bounds(target.transform.position, Vector3.zero);
    return true;
  }

  void Select() {
    if (slotSelectionLocked) return;
    if (TryResolveSelectedSlotNumber(out var slotNumber)) {
      LockSaveSlotInteraction();
      SaveSlotManager.SetSlot(slotNumber);
      RuntimeLog.Log($"Slot set {SaveSlotManager.slot}");
      MessageBus.Send("startGame");
    }
  }

  void ResetSaveSlotInteractionLock() {
    SetSlotSelectionLocked(false);
  }

  public void ResetSelection() {
    activeIndexLoadMenu = -1;
    SetActiveIndex(-1);
  }

  public void SetActiveSlotNumber(int slotNumber) {
    for (int i = 0; i < buttons.Count; i++) {
      var button = buttons[i];
      if (button == null) continue;

      var slot = button.GetComponent<SaveSlot>();
      if (slot == null) continue;
      if (!int.TryParse(slot.saveNumber, out var resolvedSlotNumber)) continue;
      if (resolvedSlotNumber != slotNumber) continue;

      activeIndexLoadMenu = i;
      SetActiveIndex(activeIndexLoadMenu);
      ScrollActiveSlotIntoView();
      return;
    }

    ResetSelection();
  }

  void LockSaveSlotInteraction() {
    if (slotSelectionLocked) return;
    SetSlotSelectionLocked(true);
  }

  public void SetSlotSelectionLocked(bool locked) {
    slotSelectionLocked = locked;
    SetSaveSlotInteractionEnabled(!locked);
    if (!locked) return;
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

  public bool TryResolveSelectedSlotNumber(out int slotNumber) {
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
