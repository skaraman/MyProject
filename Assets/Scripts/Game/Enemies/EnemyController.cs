using System.Collections.Generic;
using CustomInspector;
using UnityEngine;

[RequireComponent(typeof(EnemyInfo))]
public class EnemyController : MonoBehaviour {
  [Button(nameof(_TogglePause), label = "un/pause", size = Size.small)] public bool slowDown;
  [Button(nameof(ForceAnimation), label = "Play", size = Size.small)] public bool forceLoop;

  [Header("Sprite Parts")]
  [Tooltip("Parts that carry SpriteWithNormals components. If left empty, children are auto-discovered.")]
  public GameObject[] spriteObjects;

  [Header("Hit Boxes")]
  [Tooltip("PolygonCollider2D objects that should be animated per frame.")]
  public GameObject[] hBoxObjects;

  [Header("Animation Data")]
  public string enemyType;
  public string defaultAnimation = "Idle";
  public bool playOnStart = true;

  private AnimationController animationController = new();
  private Dictionary<string, AnimData> animationData;
  private Dictionary<string, Dictionary<string, string>> interruptData;
  private Dictionary<string, Dictionary<string, List<HBox>>> hBoxData;

  public string CurrentAnimation => animationController != null ? animationController.CurrentAnimation : null;
  public bool IsFacingRight => animationController != null && animationController.IsFacingRight;

  void Awake() {
    LoadAnimationData();
    ConfigureAnimationController();
  }

  void Start() {
    enemyType = GetComponent<EnemyInfo>().enemyType;
    if (playOnStart && !string.IsNullOrEmpty(defaultAnimation)) {
      PlayAnimation(defaultAnimation, true);
    }
  }

  void Update() {
    animationController.SlowDown = slowDown;
    animationController.ForceLoop = forceLoop;
    animationController.Tick(Time.deltaTime);
  }

  void OnDisable() {
    animationController?.Cleanup(!Application.isPlaying);
  }

  private void LoadAnimationData() {
    animationData = Animations.Enemies.ContainsKey(enemyType) ? Animations.Enemies[enemyType] : null;
    if (animationData == null) {
      Debug.LogWarning($"[EnemyController] No animation data found for enemy type '{enemyType}'.");
    }
    interruptData = Interrupts.Enemies.ContainsKey(enemyType) ? Interrupts.Enemies[enemyType] : new Dictionary<string, Dictionary<string, string>>();
    hBoxData = HBoxes.Enemies.ContainsKey(enemyType) ? HBoxes.Enemies[enemyType] : null;
    ConfigureAnimationController();
  }

  private void ConfigureAnimationController() {
    if (animationController == null) return;
    animationController.Initialize(
      transform,
      spriteObjects,
      null,
      hBoxObjects,
      animationData,
      interruptData,
      null,
      hBoxData,
      defaultAnimation,
      false
    );
  }

  public bool PlayAnimation(string animationName, bool forceRestart = false) {
    if (animationData == null || !animationData.ContainsKey(animationName)) {
      Debug.LogWarning($"[EnemyController] Animation '{animationName}' missing for '{enemyType}'.");
      return false;
    }
    return animationController != null && animationController.PlayAnimation(animationName, forceRestart);
  }

  public void PauseAnimation() {
    animationController?.PauseAnimation();
  }

  public void ResumeAnimation() {
    animationController?.ResumeAnimation();
  }

  public void StopAnimation(bool resetToDefault = false) {
    animationController?.StopAnimation(resetToDefault);
  }

  public void _TogglePause() {
    TogglePause();
  }

  public void TogglePause(string forcePause = null) {
    animationController?.TogglePause(forcePause);
  }

  public void ForceAnimation() {
    animationController?.ForceAnimation(defaultAnimation);
  }

  public float GetAnimationDurationSeconds(string animationName) {
    return animationController != null ? animationController.GetAnimationDurationSeconds(animationName) : 0f;
  }

  public void FaceDirection(float xDirection) {
    animationController?.SetFacingDirection(xDirection);
  }

  public AnimationController Controller => animationController;
}
