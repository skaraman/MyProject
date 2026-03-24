using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public class AnimatorTimeScaleAdapter : MonoBehaviour {
  [SerializeField] float baseSpeed = 1f;

  Animator animator;
  bool hasCapturedRuntimeBaseSpeed;

  void Awake() {
    animator = GetComponent<Animator>();
    CaptureBaseSpeed();
    ApplySpeed();
  }

  void OnEnable() {
    CaptureBaseSpeed();
    ApplySpeed();
  }

  void Update() {
    ApplySpeed();
  }

  void OnValidate() {
    if (!Application.isPlaying) return;
    animator = GetComponent<Animator>();
    ApplySpeed();
  }

  void CaptureBaseSpeed() {
    if (animator == null) return;
    if (hasCapturedRuntimeBaseSpeed) return;
    baseSpeed = animator.speed;
    hasCapturedRuntimeBaseSpeed = true;
  }

  void ApplySpeed() {
    if (animator == null) return;
    animator.speed = baseSpeed * TimeScale.GetEffectiveFactor(this);
  }
}
