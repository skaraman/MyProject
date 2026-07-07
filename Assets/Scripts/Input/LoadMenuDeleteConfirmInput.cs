using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class LoadMenuDeleteConfirmInput : ButtonGroup {
  const int YesIndex = 0;
  const int NoIndex = 1;

  [SerializeField] GameObject deleteConfirmRoot;
  [SerializeField] GameObject yesButton;
  [SerializeField] GameObject noButton;
  [SerializeField] LoadMenuInput loadMenuInput;
  [SerializeField] int defaultIndex = NoIndex;

  readonly List<Action> actions = new();
  TestActions input;
  Coroutine unlockRoutine;
  int activeIndexDeleteConfirm = -1;
  int selectedSlot = -1;
  bool isOpen;

  void Awake() {
    ResolveReferences();
    RebuildButtons();
    SetConfirmVisible(false);
  }

  void OnEnable() {
    ResolveReferences();
    RebuildButtons();
    RegisterMessageBus();
    EnableKeyboardInput();
    CloseConfirm(unlockSlots: true, immediateUnlock: true);
  }

  void OnDisable() {
    CloseConfirm(unlockSlots: true, immediateUnlock: true);
    UnregisterMessageBus();
    DisableKeyboardInput();
  }

  void RegisterMessageBus() {
    if (actions.Count > 0) return;
    actions.Add(MessageBus.On("loadMenu.deleteConfirmOpen", OpenConfirm));
    actions.Add(MessageBus.On("loadMenu.hover", MouseHover));
    actions.Add(MessageBus.On("loadMenu.click", MouseClick));
    actions.Add(MessageBus.On("loadMenu.select", SelectFromInput));
    actions.Add(MessageBus.On("loadMenu.cancel", CancelFromInput));
  }

  void UnregisterMessageBus() {
    for (int i = 0; i < actions.Count; i++) {
      actions[i].Invoke();
    }
    actions.Clear();
  }

  void EnableKeyboardInput() {
    if (input != null) return;
    input = new TestActions();
    input.mainMenu.left.performed += OnLeft;
    input.mainMenu.right.performed += OnRight;
    input.mainMenu.left.Enable();
    input.mainMenu.right.Enable();
  }

  void DisableKeyboardInput() {
    if (input == null) return;
    input.mainMenu.left.performed -= OnLeft;
    input.mainMenu.right.performed -= OnRight;
    input.Disable();
    input.Dispose();
    input = null;
  }

  void OnLeft(InputAction.CallbackContext context) {
    if (!isOpen) return;
    MoveSelection(-1);
  }

  void OnRight(InputAction.CallbackContext context) {
    if (!isOpen) return;
    MoveSelection(1);
  }

  void OpenConfirm(object payload) {
    if (!ResolveSlotNumber(payload, out selectedSlot)) return;

    ResolveReferences();
    RebuildButtons();
    if (buttons.Count <= 0) return;

    StopUnlockRoutine();
    loadMenuInput?.SetSlotSelectionLocked(true);
    isOpen = true;
    SetConfirmVisible(true);
    SetConfirmCollidersEnabled(true);
    SetSelectedIndex(Mathf.Clamp(defaultIndex, 0, buttons.Count - 1));
  }

  void CancelFromInput(object payload) {
    if (!isOpen) return;
    if (!InputMessageValue.IsPressed(payload)) return;
    Cancel();
  }

  void SelectFromInput(object payload) {
    if (!isOpen) return;
    if (!InputMessageValue.IsPressed(payload)) return;
    SelectActive();
  }

  void MouseHover(object payload) {
    if (!isOpen) return;
    var target = payload as GameObject;
    var index = ResolveButtonIndex(target);
    if (index < 0) return;
    SetSelectedIndex(index);
  }

  void MouseClick(object payload) {
    if (!isOpen) return;
    var target = payload as GameObject;
    var index = ResolveButtonIndex(target);
    if (index < 0) return;
    SetSelectedIndex(index);
    SelectActive();
  }

  void MoveSelection(int direction) {
    if (buttons.Count <= 0) return;

    var nextIndex = activeIndexDeleteConfirm;
    if (nextIndex < 0) {
      nextIndex = Mathf.Clamp(defaultIndex, 0, buttons.Count - 1);
    }
    else {
      nextIndex += direction;
    }

    if (nextIndex < 0) {
      nextIndex = buttons.Count - 1;
    }

    if (nextIndex >= buttons.Count) {
      nextIndex = 0;
    }

    SetSelectedIndex(nextIndex);
  }

  void SelectActive() {
    if (activeIndexDeleteConfirm == YesIndex) {
      ConfirmDelete();
      return;
    }

    Cancel();
  }

  void ConfirmDelete() {
    if (selectedSlot > 0) {
      SaveSlotManager.Delete(selectedSlot);
      MessageBus.Send("loadMenu.deleteConfirmed", selectedSlot);
    }

    CloseConfirm(unlockSlots: true, immediateUnlock: false);
  }

  void Cancel() {
    CloseConfirm(unlockSlots: true, immediateUnlock: false);
  }

  void CloseConfirm(bool unlockSlots, bool immediateUnlock) {
    isOpen = false;
    selectedSlot = -1;
    activeIndexDeleteConfirm = -1;
    ClearHighlights();
    SetActiveIndex(-1);
    SetConfirmCollidersEnabled(false);
    SetConfirmVisible(false);

    if (!unlockSlots) return;
    if (immediateUnlock) {
      StopUnlockRoutine();
      loadMenuInput?.SetSlotSelectionLocked(false);
      return;
    }

    UnlockSlotsNextFrame();
  }

  void UnlockSlotsNextFrame() {
    if (!isActiveAndEnabled) {
      loadMenuInput?.SetSlotSelectionLocked(false);
      return;
    }

    StopUnlockRoutine();
    unlockRoutine = StartCoroutine(UnlockSlotsAfterInputDispatch());
  }

  IEnumerator UnlockSlotsAfterInputDispatch() {
    yield return null;
    loadMenuInput?.SetSlotSelectionLocked(false);
    unlockRoutine = null;
  }

  void StopUnlockRoutine() {
    if (unlockRoutine == null) return;
    StopCoroutine(unlockRoutine);
    unlockRoutine = null;
  }

  void SetSelectedIndex(int index) {
    if (index == activeIndexDeleteConfirm) return;
    activeIndexDeleteConfirm = index;
    SetActiveIndex(index);
  }

  void SetConfirmVisible(bool visible) {
    if (deleteConfirmRoot == null) return;
    if (deleteConfirmRoot.activeSelf == visible) return;
    deleteConfirmRoot.SetActive(visible);
  }

  void SetConfirmCollidersEnabled(bool enabled) {
    if (deleteConfirmRoot == null) return;
    var colliders = deleteConfirmRoot.GetComponentsInChildren<Collider2D>(includeInactive: true);
    for (int i = 0; i < colliders.Length; i++) {
      if (colliders[i] == null) continue;
      colliders[i].enabled = enabled;
    }
  }

  void ResolveReferences() {
    if (loadMenuInput == null) {
      loadMenuInput = GetComponent<LoadMenuInput>();
    }

    if (deleteConfirmRoot == null) {
      var root = transform.Find("deleteConfirm");
      if (root != null) {
        deleteConfirmRoot = root.gameObject;
      }
    }

    if (deleteConfirmRoot == null) return;

    if (yesButton == null) {
      yesButton = FindChildByName(deleteConfirmRoot.transform, "yes");
    }

    if (noButton == null) {
      noButton = FindChildByName(deleteConfirmRoot.transform, "no");
    }
  }

  void RebuildButtons() {
    buttons.Clear();

    if (yesButton != null) {
      buttons.Add(yesButton);
    }

    if (noButton != null) {
      buttons.Add(noButton);
    }
  }

  bool ResolveSlotNumber(object payload, out int slotNumber) {
    slotNumber = -1;

    if (payload is int intValue) {
      slotNumber = intValue;
      return slotNumber > 0;
    }

    if (payload is string stringValue && int.TryParse(stringValue, out slotNumber)) {
      return slotNumber > 0;
    }

    if (loadMenuInput == null) {
      return false;
    }

    return loadMenuInput.TryResolveSelectedSlotNumber(out slotNumber);
  }

  int ResolveButtonIndex(GameObject target) {
    if (target == null) return -1;

    var directIndex = buttons.IndexOf(target);
    if (directIndex >= 0) return directIndex;

    var targetTransform = target.transform;
    for (int i = 0; i < buttons.Count; i++) {
      var button = buttons[i];
      if (button == null) continue;
      if (targetTransform.IsChildOf(button.transform)) {
        return i;
      }
    }

    return -1;
  }

  static GameObject FindChildByName(Transform root, string childName) {
    if (root == null) return null;

    for (int i = 0; i < root.childCount; i++) {
      var child = root.GetChild(i);
      if (string.Equals(child.name, childName, StringComparison.OrdinalIgnoreCase)) {
        return child.gameObject;
      }

      var match = FindChildByName(child, childName);
      if (match != null) {
        return match;
      }
    }

    return null;
  }

  protected override void HandleActiveState(GameObject button) {
    ApplyHighlight(button, true);
  }

  protected override void HandleInactiveState(GameObject button) {
    ApplyHighlight(button, false);
  }

  static void ApplyHighlight(GameObject button, bool isActive) {
    ButtonShaderKeywords.ApplyToButton(button, "OUTBASE_ON", isActive);
    ButtonShaderKeywords.ApplyToButton(button, "SHINE_ON", isActive);
  }

  void ClearHighlights() {
    for (int i = 0; i < buttons.Count; i++) {
      var button = buttons[i];
      if (button == null) continue;
      ApplyHighlight(button, false);
    }
  }
}
