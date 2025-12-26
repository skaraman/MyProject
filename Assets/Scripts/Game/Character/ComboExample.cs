using System.Collections.Generic;
using UnityEngine;
using CustomInspector;

/// <summary>
/// Example script demonstrating the ComboManager system.
/// Attach this to a GameObject with a GearController to test combos.
/// </summary>
public class ComboExample : MonoBehaviour {
  [SerializeField] private GearController gearController;
  
  [Header("Test Controls")]
  [Button(nameof(TestTripleStrike), label = "Test: Triple Strike", size = Size.medium)]
  public bool testTripleStrike;
  
  [Button(nameof(TestLeftHook), label = "Test: Left Hook", size = Size.medium)]
  public bool testLeftHook;
  
  [Button(nameof(TestTimingFailure), label = "Test: Timing Failure", size = Size.medium)]
  public bool testTimingFailure;
  
  [Button(nameof(TestPartialCombo), label = "Test: Partial Combo", size = Size.medium)]
  public bool testPartialCombo;
  
  [Header("Combo Management")]
  [Button(nameof(ListCombos), label = "List All Combos", size = Size.medium)]
  public bool listCombos;
  
  [Button(nameof(AddCustomCombo), label = "Add Custom Combo", size = Size.medium)]
  public bool addCustomCombo;
  
  [SerializeField] private string customComboName = "Custom Combo";
  [SerializeField] private List<string> customComboAnimations = new List<string> { "PunchRight", "KickRight", "PunchLeft" };

  void Start() {
    if (gearController == null) {
      gearController = GetComponent<GearController>();
    }
    
    if (gearController == null) {
      Debug.LogError("[ComboExample] GearController not found!");
      return;
    }
    
    Debug.Log("[ComboExample] Ready! Use inspector buttons to test combos.");
  }

  /// <summary>
  /// Test the default "Triple Strike" combo: PunchRight -> PunchLeft -> KickLeft
  /// This should show smooth transitions between all three attacks.
  /// </summary>
  public void TestTripleStrike() {
    if (gearController == null) return;
    
    Debug.Log("[ComboExample] Testing Triple Strike combo...");
    StartCoroutine(ExecuteComboSequence(new List<string> { 
      "PunchRight",   // 120ms duration
      "PunchLeft",    // 150ms duration
      "KickLeft"      // 500ms duration
    }));
  }

  /// <summary>
  /// Test the "Left Hook" combo: PunchLeft -> PunchRight
  /// Should play PunchLeftToPunchRight transition.
  /// </summary>
  public void TestLeftHook() {
    if (gearController == null) return;
    
    Debug.Log("[ComboExample] Testing Left Hook combo...");
    StartCoroutine(ExecuteComboSequence(new List<string> { 
      "PunchLeft",    // 150ms duration
      "PunchRight"    // 120ms duration
    }));
  }

  /// <summary>
  /// Test timing failure: Play PunchRight, wait too long, then PunchLeft
  /// The combo should break and play StanceToPunchLeft instead of PunchRightToPunchLeft
  /// </summary>
  public void TestTimingFailure() {
    if (gearController == null) return;
    
    Debug.Log("[ComboExample] Testing timing failure...");
    StartCoroutine(TimingFailureSequence());
  }

  /// <summary>
  /// Test partial combo: Start one combo, break it, but the breaking move starts another combo
  /// Example: PunchRight (Combo 1 start) -> delay -> PunchLeft (Combo 1 breaks, Combo 2 starts)
  /// </summary>
  public void TestPartialCombo() {
    if (gearController == null) return;
    
    Debug.Log("[ComboExample] Testing partial combo...");
    StartCoroutine(PartialComboSequence());
  }

  /// <summary>
  /// List all defined combos in the console
  /// </summary>
  public void ListCombos() {
    if (gearController == null) return;
    
    var combos = gearController.GetCombos();
    Debug.Log($"[ComboExample] Total combos: {combos.Count}");
    
    for (int i = 0; i < combos.Count; i++) {
      var combo = combos[i];
      string animList = string.Join(" -> ", combo.animations);
      Debug.Log($"[ComboExample] {i + 1}. {combo.comboName}: {animList}");
    }
  }

  /// <summary>
  /// Add a custom combo using the inspector fields
  /// </summary>
  public void AddCustomCombo() {
    if (gearController == null) return;
    
    if (string.IsNullOrEmpty(customComboName)) {
      Debug.LogWarning("[ComboExample] Custom combo name is empty!");
      return;
    }
    
    if (customComboAnimations == null || customComboAnimations.Count == 0) {
      Debug.LogWarning("[ComboExample] Custom combo animations list is empty!");
      return;
    }
    
    gearController.AddCombo(customComboName, new List<string>(customComboAnimations));
    Debug.Log($"[ComboExample] Added combo '{customComboName}' with {customComboAnimations.Count} animations");
    ListCombos();
  }

  // Coroutine helpers for testing

  private System.Collections.IEnumerator ExecuteComboSequence(List<string> animations) {
    foreach (string anim in animations) {
      Debug.Log($"[ComboExample] Playing animation: {anim}");
      gearController.PlayAnimation(anim);
      
      // Wait for a short time (within the animation duration)
      // Using 0.08 seconds to ensure we're within timing windows
      yield return new WaitForSeconds(0.08f);
    }
    
    Debug.Log("[ComboExample] Combo sequence complete!");
  }

  private System.Collections.IEnumerator TimingFailureSequence() {
    Debug.Log("[ComboExample] Playing PunchRight...");
    gearController.PlayAnimation("PunchRight");
    
    // Wait longer than the animation duration (PunchRight is 120ms = 0.12s)
    Debug.Log("[ComboExample] Waiting 0.15 seconds (longer than animation duration)...");
    yield return new WaitForSeconds(0.15f);
    
    Debug.Log("[ComboExample] Playing PunchLeft (should break combo)...");
    gearController.PlayAnimation("PunchLeft");
    
    Debug.Log("[ComboExample] Timing failure test complete! Check if transition went through Stance.");
  }

  private System.Collections.IEnumerator PartialComboSequence() {
    // Assuming we have these combos:
    // Combo A: [PunchRight, PunchLeft, KickLeft]
    // Combo B: [PunchLeft, KickRight]
    
    Debug.Log("[ComboExample] Starting Combo A with PunchRight...");
    gearController.PlayAnimation("PunchRight");
    
    // Wait too long to break Combo A
    Debug.Log("[ComboExample] Waiting to break Combo A...");
    yield return new WaitForSeconds(0.15f);
    
    Debug.Log("[ComboExample] Playing PunchLeft (breaks Combo A, starts Combo B if it exists)...");
    gearController.PlayAnimation("PunchLeft");
    
    // Continue with Combo B
    yield return new WaitForSeconds(0.08f);
    
    Debug.Log("[ComboExample] Playing KickRight (continues Combo B if it exists)...");
    gearController.PlayAnimation("KickRight");
    
    Debug.Log("[ComboExample] Partial combo test complete!");
  }
}
