using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Represents a single combo - a sequence of animations that trigger special transitions
/// </summary>
[Serializable]
public class Combo {
  public string comboName;
  public List<string> animations = new List<string>();

  public Combo() { }

  public Combo(string name, List<string> anims) {
    comboName = name;
    animations = anims ?? new List<string>();
  }
}

/// <summary>
/// Manages combo detection and execution for character animations.
/// Watches PlayAnimation calls and triggers special combo transitions when sequences match.
/// </summary>
public class ComboManager {
  private const float MS_TO_SECONDS = 1000f;
  
  // All defined combos
  private List<Combo> combos = new List<Combo>();

  // Current combo tracking
  private string lastAnimation = null;
  private float lastAnimationTime = 0f;
  private int currentComboIndex = -1; // Index of the combo we're currently building
  private int currentComboStep = 0;   // How many animations in the combo we've matched

  // Reference to animation data to get durations
  private Dictionary<string, AnimData> animationData;

  /// <summary>
  /// Initialize the combo manager with animation data
  /// </summary>
  public void Initialize(Dictionary<string, AnimData> animData) {
    animationData = animData;
  }

  /// <summary>
  /// Set the list of combos
  /// </summary>
  public void SetCombos(List<Combo> newCombos) {
    combos = newCombos ?? new List<Combo>();
  }

  /// <summary>
  /// Get all combos
  /// </summary>
  public List<Combo> GetCombos() {
    return new List<Combo>(combos);
  }

  /// <summary>
  /// Add a new combo
  /// </summary>
  public void AddCombo(Combo combo) {
    if (combo != null && !string.IsNullOrEmpty(combo.comboName)) {
      // Check for duplicate combo names and replace if found
      int existingIndex = combos.FindIndex(c => c.comboName == combo.comboName);
      if (existingIndex >= 0) {
        combos[existingIndex] = combo;
        Debug.LogWarning($"[ComboManager] Replaced existing combo '{combo.comboName}'");
      }
      else {
        combos.Add(combo);
      }
    }
  }

  /// <summary>
  /// Remove a combo by name
  /// </summary>
  public bool RemoveCombo(string comboName) {
    int index = combos.FindIndex(c => c.comboName == comboName);
    if (index >= 0) {
      combos.RemoveAt(index);
      return true;
    }
    return false;
  }

  /// <summary>
  /// Edit an existing combo
  /// </summary>
  public bool EditCombo(string comboName, List<string> newAnimations) {
    var combo = combos.Find(c => c.comboName == comboName);
    if (combo != null) {
      combo.animations = newAnimations ?? new List<string>();
      return true;
    }
    return false;
  }

  /// <summary>
  /// Called when an animation is about to be played. Returns the transition animation if in a combo.
  /// </summary>
  /// <param name="requestedAnimation">The animation that was requested to play</param>
  /// <param name="currentTime">Current game time</param>
  /// <returns>The animation to actually play (might be a combo transition)</returns>
  public string ProcessAnimationRequest(string requestedAnimation, float currentTime) {
    if (string.IsNullOrEmpty(requestedAnimation)) return requestedAnimation;

    // Check if timing window has expired
    bool timingValid = false;
    if (!string.IsNullOrEmpty(lastAnimation) && animationData != null) {
      float timeSinceLastAnimation = currentTime - lastAnimationTime;
      if (animationData.TryGetValue(lastAnimation, out var lastAnimData)) {
        float animationDuration = lastAnimData.duration / MS_TO_SECONDS;
        timingValid = timeSinceLastAnimation <= animationDuration;
      }
    }

    // If timing expired, reset combo progress
    if (!timingValid) {
      currentComboIndex = -1;
      currentComboStep = 0;
    }

    // Check if we're continuing an existing combo
    if (currentComboIndex >= 0 && currentComboStep > 0) {
      var activeCombo = combos[currentComboIndex];
      if (currentComboStep < activeCombo.animations.Count &&
          activeCombo.animations[currentComboStep] == requestedAnimation &&
          timingValid) {
        // Continue the combo
        currentComboStep++;
        string transitionAnim = GetTransitionAnimation(lastAnimation, requestedAnimation);
        lastAnimation = requestedAnimation;
        lastAnimationTime = currentTime;
        
        // If we completed the combo, reset
        if (currentComboStep >= activeCombo.animations.Count) {
          currentComboIndex = -1;
          currentComboStep = 0;
        }
        
        return transitionAnim ?? requestedAnimation;
      }
      else {
        // Combo broken, check if this starts a new combo
        currentComboIndex = -1;
        currentComboStep = 0;
      }
    }

    // Check if this animation starts any combo
    for (int i = 0; i < combos.Count; i++) {
      var combo = combos[i];
      if (combo.animations.Count > 0 && combo.animations[0] == requestedAnimation) {
        // Starting a new combo
        currentComboIndex = i;
        currentComboStep = 1;
        
        // If we have a previous animation and timing is valid, use transition
        if (timingValid && !string.IsNullOrEmpty(lastAnimation)) {
          string transitionAnim = GetTransitionAnimation(lastAnimation, requestedAnimation);
          lastAnimation = requestedAnimation;
          lastAnimationTime = currentTime;
          return transitionAnim ?? requestedAnimation;
        }
        break;
      }
    }

    lastAnimation = requestedAnimation;
    lastAnimationTime = currentTime;
    return requestedAnimation;
  }

  /// <summary>
  /// Get the transition animation name from one animation to another
  /// </summary>
  private string GetTransitionAnimation(string fromAnim, string toAnim) {
    if (string.IsNullOrEmpty(fromAnim) || string.IsNullOrEmpty(toAnim)) return null;
    
    // Build transition name (e.g., "PunchRight" + "To" + "PunchLeft" = "PunchRightToPunchLeft")
    string transitionName = fromAnim + "To" + toAnim;
    
    // Check if this transition exists in animation data
    if (animationData != null && animationData.TryGetValue(transitionName, out var transitionData)) {
      return transitionName;
    }
    
    return null;
  }

  /// <summary>
  /// Reset combo tracking (useful when player takes damage, etc.)
  /// </summary>
  public void ResetCombo() {
    currentComboIndex = -1;
    currentComboStep = 0;
    lastAnimation = null;
    lastAnimationTime = 0f;
  }

  /// <summary>
  /// Save combos to save data
  /// </summary>
  public void SaveCombos(SaveData saveData) {
    if (saveData == null) return;
    saveData.SetComplex("combos", combos);
  }

  /// <summary>
  /// Load combos from save data
  /// </summary>
  public void LoadCombos(SaveData saveData) {
    if (saveData == null) return;
    try {
      var loadedCombos = saveData.GetComplex<List<Combo>>("combos");
      if (loadedCombos != null) {
        combos = loadedCombos;
      }
    }
    catch (Exception e) {
      Debug.LogWarning($"Failed to load combos: {e.Message}");
      combos = new List<Combo>();
    }
  }
}
