using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputProcessor : MonoBehaviour {
  const float ScalarDispatchEpsilon = 0.0001f;
  const float VectorDispatchEpsilonSqr = 0.0001f;
  [System.NonSerialized] public TestActions input;
  public string defaultMap;
  private string activeMap;
  Dictionary<InputAction, string> cachedNames = new();
  readonly Dictionary<InputAction, float> lastScalarDispatchValues = new();
  readonly Dictionary<InputAction, Vector2> lastVectorDispatchValues = new();
  public string ActiveMap => activeMap;

  void OnEnable() {
    input = new TestActions();
    SetupAllCalls();
    SwitchMap(defaultMap != "" ? defaultMap : "mainMenu");
  }

  void OnDisable() {
    if (input != null) RemoveAllCalls();
    lastScalarDispatchValues.Clear();
    lastVectorDispatchValues.Clear();
  }

  public void SwitchMap(string mapName) {
    DisableAllMaps();
    lastScalarDispatchValues.Clear();
    lastVectorDispatchValues.Clear();
    activeMap = mapName ?? activeMap;
    var map = input.asset.FindActionMap(activeMap);
    map?.Enable();
    //RuntimeLog.Log($"[InputProcessor] Switched to: {activeMap}");
  }

  void DisableAllMaps() {
    foreach (var map in input.asset.actionMaps) {
      map.Disable();
    }
  }

  void Process(InputAction.CallbackContext ctx) {
    if (ShouldSuppressShiftEscapeDispatch(ctx)) {
      return;
    }

    if (!cachedNames.TryGetValue(ctx.action, out var name)) {
      name = ctx.action.actionMap.name + "." + ctx.action.name;
      cachedNames[ctx.action] = name;
    }

    var isValueAction = ctx.action != null && ctx.action.type == InputActionType.Value;
    var isButtonAction = ctx.action != null && ctx.action.type == InputActionType.Button;
    var shouldDedupeValueDispatch =
      (isValueAction && ctx.performed) ||
      isButtonAction;
    var type = ctx.valueType;
    object value;
    if (type == typeof(Vector2)) {
      var vectorValue = ctx.ReadValue<Vector2>();
      if (shouldDedupeValueDispatch &&
          !ShouldDispatchVectorValue(ctx.action, vectorValue)) {
        return;
      }
      value = vectorValue;
    }
    else if (type == typeof(float)) {
      var scalarValue = ctx.ReadValue<float>();
      if (shouldDedupeValueDispatch &&
          !ShouldDispatchScalarValue(ctx.action, scalarValue)) {
        return;
      }
      value = scalarValue;
    }
    else if (type == typeof(int)) {
      var scalarValue = ctx.ReadValue<int>();
      if (shouldDedupeValueDispatch &&
          !ShouldDispatchScalarValue(ctx.action, scalarValue)) {
        return;
      }
      value = scalarValue;
    }
    else if (type == typeof(bool)) {
      var boolValue = ctx.ReadValue<float>() > 0.5f;
      var scalarValue = boolValue ? 1f : 0f;
      if (shouldDedupeValueDispatch &&
          !ShouldDispatchScalarValue(ctx.action, scalarValue)) {
        return;
      }
      value = boolValue;
    }
    else {
      value = ctx.ReadValueAsObject();
    }

    //RuntimeLog.Log($"[InputProcessor] {name} = {value}");
    MessageBus.Send(name, value);
  }

  static bool ShouldSuppressShiftEscapeDispatch(InputAction.CallbackContext ctx) {
    if (!IsKeyboardEscapeControl(ctx.control)) return false;
    if (!IsShiftHeld()) return false;

    var action = ctx.action;
    if (action == null || action.actionMap == null) return false;

    var mapName = action.actionMap.name;
    var actionName = action.name;
    var shouldSuppress =
      (string.Equals(mapName, "gameplay", System.StringComparison.Ordinal) &&
       string.Equals(actionName, "pause", System.StringComparison.Ordinal)) ||
      (string.Equals(mapName, "loadMenu", System.StringComparison.Ordinal) &&
       string.Equals(actionName, "cancel", System.StringComparison.Ordinal)) ||
      (string.Equals(mapName, "pauseMenu", System.StringComparison.Ordinal) &&
       string.Equals(actionName, "cancel", System.StringComparison.Ordinal));

    if (!shouldSuppress) return false;

    RuntimeLog.Log(
      "[InputProcessor] Suppressed shifted escape dispatch map=" + mapName +
      " action=" + actionName +
      " control=" + ctx.control.path
    );
    return true;
  }

  static bool IsKeyboardEscapeControl(InputControl control) {
    return control != null &&
           control.device is Keyboard &&
           (
             string.Equals(control.name, "escape", System.StringComparison.OrdinalIgnoreCase) ||
             string.Equals(control.path, "/Keyboard/escape", System.StringComparison.OrdinalIgnoreCase) ||
             string.Equals(control.path, "<Keyboard>/escape", System.StringComparison.OrdinalIgnoreCase)
           );
  }

  static bool IsShiftHeld() {
    var keyboard = Keyboard.current;
    if (keyboard == null) return false;
    return keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
  }

  bool ShouldDispatchVectorValue(InputAction action, Vector2 currentValue) {
    if (action == null) return true;
    if (lastVectorDispatchValues.TryGetValue(action, out var previousValue)) {
      if ((currentValue - previousValue).sqrMagnitude <= VectorDispatchEpsilonSqr) {
        return false;
      }
    }
    lastVectorDispatchValues[action] = currentValue;
    return true;
  }

  bool ShouldDispatchScalarValue(InputAction action, float currentValue) {
    if (action == null) return true;
    if (lastScalarDispatchValues.TryGetValue(action, out var previousValue)) {
      if (Mathf.Abs(currentValue - previousValue) <= ScalarDispatchEpsilon) {
        return false;
      }
    }
    lastScalarDispatchValues[action] = currentValue;
    return true;
  }

  private void SetupAllCalls() {
    foreach (var map in input.asset.actionMaps) {
      foreach (var action in map.actions) {
        action.performed += Process;
        action.canceled += Process;
      }
    }
    //RuntimeLog.Log("[InputProcessor] SetupAllCalls finished");
  }

  private void RemoveAllCalls() {
    foreach (var map in input.asset.actionMaps) {
      foreach (var action in map.actions) {
        action.canceled -= Process;
        action.performed -= Process;
      }
    }
    //RuntimeLog.Log("[InputProcessor] RemoveAllCalls finished");
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
