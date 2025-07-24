using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class LoadMenuGroup : ButtonGroup {
  int activeIndexLoadMenu = -1;
  List<Action> actions = new();

  public GameObject scrollWrap;

  bool dragging = false;
  float lastMouseY;

  void Start() {
    actions.Add(MessageBus.On("loadMenu.cancel", o => BackOut()));
    actions.Add(MessageBus.On("loadMenu.delete", o => DeleteDir()));
    actions.Add(MessageBus.On("loadMenu.down", o => MenuDown()));
    actions.Add(MessageBus.On("loadMenu.select", o => Select()));
    actions.Add(MessageBus.On("loadMenu.up", o => MenuUp()));
    actions.Add(MessageBus.On("mainMenu.button.hover", o => MouseHover(o)));
    actions.Add(MessageBus.On("mainMenu.button.click", o => ClickCheck(o)));
  }

  void OnDestroy() {
    for (int i = 0; i < actions.Count; i++) actions[i].Invoke();
    actions.Clear();
  }

  void Update() {
    if (dragging) {
      var pos = Mouse.current.position.ReadValue();
      var deltaY = pos.y - lastMouseY;
      lastMouseY = pos.y;
      var t = scrollWrap.transform;
      t.position += new Vector3(0, deltaY, 0);
    }

    if (Mouse.current.leftButton.wasReleasedThisFrame) dragging = false;
  }

  void BackOut() {
    MessageBus.Send("backToMainMenu");
  }

  void DeleteDir() {
    SaveSlotManager.Delete(activeIndexLoadMenu);
  }

  void MouseHover(object target) {
    activeIndexLoadMenu = buttons.IndexOf((GameObject)target);
  }

  void MenuDown() {
    if (activeIndexLoadMenu < 0) activeIndexLoadMenu = 0;
    else activeIndexLoadMenu += 1;
    if (activeIndexLoadMenu >= buttons.Count) activeIndexLoadMenu = 0;
    SetActiveIndex(activeIndexLoadMenu);
  }

  void Select() {
    SaveSlotManager.SetSlot(activeIndexLoadMenu.ToString());
  }

  void ClickCheck(object target) {
    if ((GameObject)target == scrollWrap) {
      dragging = true;
      lastMouseY = Mouse.current.position.ReadValue().y;
    }
  }

  void MenuUp() {
    if (activeIndexLoadMenu < 0) activeIndexLoadMenu = 0;
    else activeIndexLoadMenu -= 1;
    if (activeIndexLoadMenu < 0) activeIndexLoadMenu = buttons.Count - 1;
    SetActiveIndex(activeIndexLoadMenu);
  }
}
