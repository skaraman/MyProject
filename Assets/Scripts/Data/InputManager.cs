// using System;
// using System.Collections.Generic;
// using System.Linq;
// using UnityEngine;

// public class InputManager : MonoBehaviour
// {
//   public SaveLoad saveLoad = new SaveLoad();
//   private Dictionary<string, Action<float>> inputActions = new Dictionary<string, Action<float>>
//   {
//     ["Left"] = o => MessageBus.Send("Left", o),
//     ["Right"] = o => MessageBus.Send("Right", o),
//     ["Up"] = o => MessageBus.Send("Up", o),
//     ["Down"] = o => MessageBus.Send("Down", o),
//     ["Attack1"] = o => MessageBus.Send("Attack1", o),
//     ["Attack2"] = o => MessageBus.Send("Attack2", o),
//     ["Attack3"] = o => MessageBus.Send("Attack3", o),
//     ["Attack4"] = o => MessageBus.Send("Attack4", o),
//     ["Jump"] = o => MessageBus.Send("Jump", o),
//     ["Dash"] = o => MessageBus.Send("Dash", o),
//     ["Dodge"] = o => MessageBus.Send("Dodge", o),
//     ["Block"] = o => MessageBus.Send("Block", o),
//     ["Dance"] = o => MessageBus.Send("Dance", o),
//     ["Shift"] = o => MessageBus.Send("Shift", o)
//   };
//   public Dictionary<string, KeyCode> keyboardBindings = new Dictionary<string, KeyCode>();
//   public enum ControllerInputType
//   {
//     Button,
//     Axis,
//     AnalogStick
//   }
//   [System.Serializable]
//   public class ControllerBinding
//   {
//     public string inputName;
//     public ControllerInputType inputType;
//     public bool isPositiveAxis; // For axes that can be negative/positive
//     public ControllerBinding(string name, ControllerInputType type, bool positive = true)
//     {
//       inputName = name;
//       inputType = type;
//       isPositiveAxis = positive;
//     }
//   }
//   public Dictionary<string, ControllerBinding> controllerBindings = new Dictionary<string, ControllerBinding>();
//   [SerializeField] private float analogThreshold = 0.1f;
//   [SerializeField] private float triggerThreshold = 0.1f;
//   private Dictionary<string, bool> lastKeyStates = new Dictionary<string, bool>();
//   private Dictionary<string, bool> lastButtonStates = new Dictionary<string, bool>();
//   public void ResetKeyboardToDefaults()
//   {
//     keyboardBindings = new Dictionary<string, KeyCode>
//     {
//       { "Left",    KeyCode.S }, { "Right",   KeyCode.F },         { "Up",      KeyCode.E }, { "Down",    KeyCode.D },
//       { "Attack1", KeyCode.J }, { "Attack2", KeyCode.K },         { "Attack3", KeyCode.I }, { "Attack4", KeyCode.L },
//       { "Jump",    KeyCode.A }, { "Dash",    KeyCode.Space },     { "Dodge",   KeyCode.V }, { "Block",   KeyCode.B },
//       { "Dance",   KeyCode.Z }, { "Shift",   KeyCode.LeftShift },
//     };
//     SaveKeyboardBindings();
//     Debug.Log("Input bindings reset to defaults");
//   }
//   public void ResetControllerToDefaults()
//   {
//     controllerBindings = new Dictionary<string, ControllerBinding>()
//     {
//       // Movement via analog stick
//       { "Left",    new ControllerBinding("Horizontal", ControllerInputType.Axis, false) },
//       { "Right",   new ControllerBinding("Horizontal", ControllerInputType.Axis, true) },
//       { "Up",      new ControllerBinding("Vertical", ControllerInputType.Axis, true) },
//       { "Down",    new ControllerBinding("Vertical", ControllerInputType.Axis, false) },

//       // Face buttons (Xbox controller layout)
//       { "Jump",    new ControllerBinding("joystick button 0", ControllerInputType.Button) }, // A button
//       { "Attack1", new ControllerBinding("joystick button 2", ControllerInputType.Button) }, // X button
//       { "Attack2", new ControllerBinding("joystick button 3", ControllerInputType.Button) }, // Y button
//       { "Dodge",   new ControllerBinding("joystick button 1", ControllerInputType.Button) }, // B button

//       // Shoulder buttons
//       { "Block",   new ControllerBinding("joystick button 4", ControllerInputType.Button) }, // Left Bumper
//       { "Dash",    new ControllerBinding("joystick button 5", ControllerInputType.Button) }, // Right Bumper

//       // Triggers (analog)
//       { "Attack3", new ControllerBinding("joystick axis 9", ControllerInputType.Axis) },  // Left Trigger
//       { "Attack4", new ControllerBinding("joystick axis 10", ControllerInputType.Axis) }, // Right Trigger

//       // Special buttons
//       { "Dance",   new ControllerBinding("joystick button 6", ControllerInputType.Button) } // Back/Select button
//     };
//     SaveControllerBindings();
//   }
//   private void Start()
//   {
//     foreach (var action in inputActions.Keys)
//     {
//       lastKeyStates[action] = false;
//       lastButtonStates[action] = false;
//     }
//     LoadSavedBindings();
//   }
//   private void Update()
//   {
//     ProcessKeyboardInput();
//     ProcessControllerInput();
//   }
//   private void ProcessKeyboardInput()
//   {
//     foreach (var binding in keyboardBindings)
//     {
//       string action = binding.Key;
//       KeyCode key = binding.Value;
//       bool isPressed = Input.GetKey(key);
//       bool wasPressed = lastKeyStates[action];
//       if (isPressed && !wasPressed)
//       {
//         inputActions[action]?.Invoke(1f);
//       }
//       else if (isPressed && wasPressed)
//       {
//         inputActions[action]?.Invoke(1f);
//       }
//       else if (!isPressed && wasPressed)
//       {
//         inputActions[action]?.Invoke(0f);
//       }
//       lastKeyStates[action] = isPressed;
//     }
//   }
//   private void ProcessControllerInput()
//   {
//     if (Input.GetJoystickNames().Length == 0) return;
//     foreach (var binding in controllerBindings)
//     {
//       string action = binding.Key;
//       ControllerBinding ctrlBinding = binding.Value;
//       float intensity = 0f;
//       bool isActive = false;
//       switch (ctrlBinding.inputType)
//       {
//         case ControllerInputType.Button:
//           isActive = Input.GetButton(ctrlBinding.inputName);
//           intensity = isActive ? 1f : 0f;
//           break;
//         case ControllerInputType.Axis:
//           float axisValue = Input.GetAxis(ctrlBinding.inputName);
//           if (ctrlBinding.isPositiveAxis)
//           {
//             intensity = Mathf.Max(0, axisValue);
//           }
//           else
//           {
//             intensity = Mathf.Max(0, -axisValue);
//           }
//           isActive = intensity > analogThreshold;
//           break;
//       }
//       bool wasActive = lastButtonStates[action];
//       if (isActive && !wasActive)
//       {
//         inputActions[action]?.Invoke(intensity);
//       }
//       else if (isActive && wasActive)
//       {
//         if (IsMovementAction(action) || IsAnalogAction(action))
//         {
//           inputActions[action]?.Invoke(intensity);
//         }
//       }
//       lastButtonStates[action] = isActive;
//     }
//   }
//   private bool IsMovementAction(string action)
//   {
//     return action == "Left" || action == "Right" || action == "Up" || action == "Down";
//   }
//   private bool IsAnalogAction(string action)
//   {
//     return action == "Attack3" || action == "Attack4" || IsMovementAction(action);
//   }
//   public void RebindKeyboardKey(string actionName, KeyCode newKey)
//   {
//     if (!keyboardBindings.ContainsKey(actionName))
//     {
//       Debug.LogWarning($"Action '{actionName}' does not exist in keyboard bindings!");
//       return;
//     }

//     keyboardBindings[actionName] = newKey;
//     SaveKeyboardBindings();
//     Debug.Log($"Rebound {actionName} to {newKey}");
//   }
//   public void RebindControllerInput(string actionName, string inputName, ControllerInputType inputType, bool isPositiveAxis = true)
//   {
//     ControllerBinding newBinding = new ControllerBinding(inputName, inputType, isPositiveAxis);

//     if (controllerBindings.ContainsKey(actionName))
//     {
//       controllerBindings[actionName] = newBinding;
//     }
//     else
//     {
//       controllerBindings.Add(actionName, newBinding);
//     }

//     SaveControllerBindings();
//     Debug.Log($"Rebound {actionName} to {inputName} ({inputType})");
//   }
//   private void SaveKeyboardBindings()
//   {
//     var anyDict = new Dictionary<string, string>();
//     foreach (var binding in keyboardBindings)
//     {
//       anyDict[binding.Key] = binding.Value.ToString();
//     }
//     // add Wrappers
//     saveLoad.Write("Resources/KeyboardInput", anyDict);
//   }
//   private void SaveControllerBindings()
//   {
//     Dictionary<string, string> anyDict = new Dictionary<string, string>();
//     foreach (var binding in controllerBindings)
//     {
//       // Save controller binding data as a formatted string.
//       // For example: "joystick button 0|Button|True"
//       string bindingData = $"{binding.Value.inputName}|{binding.Value.inputType}|{binding.Value.isPositiveAxis}";
//       anyDict[binding.Key] = bindingData;
//     }
//     saveLoad.Write("Resources/ControllerInput", anyDict);
//   }
//   private void LoadSavedBindings()
//   {
//     try
//     {
//       Dictionary<string,object> keyboardData = saveLoad.Read<Dictionary<string,object>>("Resources/KeyboardInput");
//       if (keyboardData != null)
//       {
//         foreach (var key in keyboardBindings.Keys.ToArray())
//         {
//           string savedKey = keyboardData.GetValue<string>(key);
//           if (!string.IsNullOrEmpty(savedKey) && System.Enum.TryParse(savedKey, out KeyCode keyCode))
//           {
//             keyboardBindings[key] = keyCode;
//           }
//         }
//       }
//       else
//       {
//         ResetKeyboardToDefaults();
//       }
//     }
//     catch (System.Exception e)
//     {
//       Debug.LogWarning($"Could not load keyboard bindings: {e.Message}");
//     }
//     try
//     {
//       Dictionary<string,object> controllerData = saveLoad.Read<Dictionary<string,object>>("Resources/ControllerInput");
//       if (controllerData != null)
//       {
//         foreach (var key in controllerBindings.Keys.ToArray())
//         {
//           string savedBinding = controllerData.GetValue<string>(key);
//           if (!string.IsNullOrEmpty(savedBinding))
//           {
//             string[] parts = savedBinding.Split('|');
//             if (parts.Length == 3)
//             {
//               string inputName = parts[0];
//               if (System.Enum.TryParse(parts[1], out ControllerInputType inputType) &&
//                   bool.TryParse(parts[2], out bool isPositiveAxis))
//               {
//                 controllerBindings[key] = new ControllerBinding(inputName, inputType, isPositiveAxis);
//               }
//             }
//           }
//         }
//       }
//       else
//       {
//         ResetControllerToDefaults();
//       }
//     }
//     catch (System.Exception e)
//     {
//       Debug.LogWarning($"Could not load controller bindings: {e.Message}");
//     }
//   }
//   public bool IsControllerConnected()
//   {
//     return Input.GetJoystickNames().Length > 0 && !string.IsNullOrEmpty(Input.GetJoystickNames()[0]);
//   }

// }