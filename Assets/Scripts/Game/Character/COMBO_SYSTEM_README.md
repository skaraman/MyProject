# ComboManager System

## Overview

The ComboManager system allows the character to setup multiple combos, where each combo is a list of animations. The system watches animation calls and automatically plays transition animations when combo sequences are detected.

## Key Features

- **Combo Detection**: Automatically detects when a sequence of animations matches a defined combo
- **Timing Windows**: Uses animation duration as the timing window (e.g., 0.25 seconds for a 250ms animation)
- **Transition Animations**: Automatically plays transition animations like "PunchRightToPunchLeft" when valid
- **Partial Combos**: Supports partial combos that can start new combos if the animation is the first in any combo
- **Persistence**: Combos are saved to the save slot and can be edited by the player

## How It Works

### Combo Structure

A combo is defined as:
```csharp
var combo = new Combo("Triple Strike", new List<string> { 
    "PunchRight", 
    "PunchLeft", 
    "KickLeft" 
});
```

### Timing Window

When an animation plays (e.g., "PunchRight" with duration 120ms = 0.12 seconds), the combo system waits for the next animation. If the next animation in the combo sequence arrives within the animation duration:
- Valid: PunchRight at t=0, PunchLeft at t=0.10 → Combo continues
- Invalid: PunchRight at t=0, PunchLeft at t=0.13 (after 120ms) → Combo breaks

### Transition Animations

When a valid combo step is detected, the system automatically plays the transition animation:
- From "PunchRight" to "PunchLeft" → Plays "PunchRightToPunchLeft"
- If no transition exists, plays the requested animation directly

### Partial Combos

If a combo is broken, but the new animation is the first step of another combo, that new combo begins tracking:
- Combo 1: [PunchRight, PunchLeft, KickLeft]
- Combo 2: [PunchLeft, KickRight]
- Sequence: PunchRight → delay → PunchLeft
  - Combo 1 breaks due to timing
  - Combo 2 starts tracking from PunchLeft

## API Usage

### In GearController

```csharp
// Add a new combo
gearController.AddCombo("MyCombo", new List<string> { "PunchRight", "PunchLeft" });

// Remove a combo
gearController.RemoveCombo("MyCombo");

// Edit an existing combo
gearController.EditCombo("MyCombo", new List<string> { "PunchRight", "PunchLeft", "KickLeft" });

// Get all combos
List<Combo> combos = gearController.GetCombos();
```

### Default Combos

The system initializes with these default combos:
1. **Triple Strike**: PunchRight → PunchLeft → KickLeft
2. **Left Hook**: PunchLeft → PunchRight
3. **Right Combo**: PunchRight → KickRight
4. **Kick Combo**: KickLeft → KickRight

## Save/Load

Combos are automatically saved to the player's save slot:
- Saved to: `{persistentDataPath}/{slot}/combos.sav`
- Loaded when: `LoadGear()` is called
- Format: Binary serialization via SaveData system

## Example Usage

```csharp
// Setup in game
var gearController = GetComponent<GearController>();

// Player performs attacks
gearController.PlayAnimation("PunchRight");    // t=0.00s - First animation
// Wait 0.10s
gearController.PlayAnimation("PunchLeft");     // t=0.10s - Within timing window
// System automatically plays "PunchRightToPunchLeft" transition
// Wait 0.15s
gearController.PlayAnimation("KickLeft");      // t=0.25s - Within timing window
// System automatically plays "PunchLeftToKickLeft" transition
// "Triple Strike" combo completed!
```

## Integration Points

1. **AnimationController.PlayAnimation()**: Intercepts animation requests
2. **ComboManager.ProcessAnimationRequest()**: Checks for combo matches
3. **GearController.LoadGear()**: Loads saved combos
4. **SaveSlotManager**: Persists combo data

## Technical Details

- Combo timing uses `Time.time` for precise timing checks
- Animation durations are retrieved from `AnimData` (milliseconds converted to seconds)
- Combo state resets when timing window expires
- Thread-safe for single-threaded Unity execution
