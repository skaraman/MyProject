using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System.Reflection;

public class InputProcessor : MonoBehaviour {
  public TestActions input;
  public string defaultMap;
  private string activeMap;
  Dictionary<InputAction, string> cachedNames = new();

  void OnEnable() {
    input = new TestActions();
    SetupAllCalls();
    SwitchMap(defaultMap != "" ? defaultMap : "mainMenu");
  }

  void OnDisable() {
    if (input != null) RemoveAllCalls();
  }

  public void SwitchMap(string mapName) {
    DisableAllMaps();
    activeMap = mapName ?? activeMap;
    var map = input.asset.FindActionMap(activeMap);
    map?.Enable();
    //Debug.Log($"[InputProcessor] Switched to: {activeMap}");
  }

  void DisableAllMaps() {
    foreach (var map in input.asset.actionMaps) {
      map.Disable();
    }
  }

  void Process(InputAction.CallbackContext ctx) {
    var type = ctx.valueType;
    object value;
    if (type == typeof(Vector2)) value = ctx.ReadValue<Vector2>();
    else if (type == typeof(float)) value = ctx.ReadValue<float>();
    else if (type == typeof(int)) value = ctx.ReadValue<int>();
    else if (type == typeof(bool)) value = ctx.ReadValue<float>() > 0.5f;
    else value = ctx.ReadValueAsObject();

    if (!cachedNames.TryGetValue(ctx.action, out var name)) {
      name = ctx.action.actionMap.name + "." + ctx.action.name;
      cachedNames[ctx.action] = name;
    }

    //Debug.Log($"[InputProcessor] {name} = {value}");
    MessageBus.Send(name, value);
  }

  private void SetupAllCalls() {
    foreach (var map in input.asset.actionMaps) {
      foreach (var action in map.actions) {
        action.performed += Process;
        action.canceled += Process;
      }
    }
    //Debug.Log("[InputProcessor] SetupAllCalls finished");
  }

  private void RemoveAllCalls() {
    foreach (var map in input.asset.actionMaps) {
      foreach (var action in map.actions) {
        action.canceled -= Process;
        action.performed -= Process;
      }
    }
    //Debug.Log("[InputProcessor] RemoveAllCalls finished");
  }

  public void Rebind(string mapName, string actionName, List<string> bindings) {
    var map = input.asset.FindActionMap(mapName);
    var action = map?.FindAction(actionName);
    action?.ChangeBinding(0).Erase();
    if (bindings != null) {
      foreach (var bind in bindings) {
        action?.AddBinding(bind);
      }
    }
  }

  public string SaveBindings() {
    return input.SaveBindingOverridesAsJson();
  }

  public void LoadBindings(string json) {
    input.LoadBindingOverridesFromJson(json);
  }
}
