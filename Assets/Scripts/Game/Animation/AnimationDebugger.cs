using CustomInspector;
using UnityEngine;

/// <summary>
/// Simple MonoBehaviour helper to drive an AnimationController instance via inspector buttons.
/// </summary>
public class AnimationDebugger : MonoBehaviour {
  [Button(nameof(_TogglePause), label = "un/pause", size = Size.small)] public bool slowDown;
  [Button(nameof(ForceAnimation), label = "Play", size = Size.small)] public bool forceLoop;

  [Tooltip("Optional gear controller to drive.")]
  [SerializeField] private GearController gearController;
  [Tooltip("Optional enemy controller to drive.")]
  [SerializeField] private EnemyController enemyController;
  [Tooltip("If set, forces this animation when pressing Play; otherwise replays current/default.")]
  [SerializeField] private string animationName;

  private AnimationController TargetController {
    get {
      if (gearController != null) return gearController.Controller;
      if (enemyController != null) return enemyController.Controller;
      return null;
    }
  }

  void Reset() {
    if (gearController == null) gearController = GetComponent<GearController>();
    if (enemyController == null) enemyController = GetComponent<EnemyController>();
  }

  public void _TogglePause() {
    TargetController?.TogglePause();
  }

  public void ForceAnimation() {
    if (TargetController == null) Reset();
    if (!string.IsNullOrEmpty(animationName)) TargetController.ForceAnimation(animationName);
    else TargetController.ForceAnimation();
  }
}
