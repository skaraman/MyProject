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

  [Header("Effects")]
  public SpriteWithNormals effectNode;

  [Header("Projectiles")]
  public ProjectileManager projectileManager;
  public Transform projectileSpawn;
  public bool useFacingDirection = true;
  public Vector2 projectileDirection = Vector2.right;

  [Header("Animation Data")]
  public string enemyType;
  public string defaultAnimation = "Idle";
  public bool playOnStart = true;

  private AnimationController animationController = new();
  private AnimationController effectAnimationController = new();
  private readonly Dictionary<string, AnimData> effectAnimations = new();
  private bool effectControllerInitialized;
  private Dictionary<string, AnimData> animationData;
  private Dictionary<string, Dictionary<string, string>> interruptData;
  private Dictionary<string, Dictionary<string, List<HBox>>> hBoxData;

  public string CurrentAnimation => animationController != null ? animationController.CurrentAnimation : null;
  public bool IsFacingRight => animationController != null && animationController.IsFacingRight;

  void Awake() {
    LoadAnimationData();
    ConfigureAnimationController();
    ConfigureEffectController();
    HookAnimationEvents();
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
    if (effectControllerInitialized) {
      effectAnimationController.SlowDown = slowDown;
      effectAnimationController.ForceLoop = forceLoop;
      effectAnimationController.Tick(Time.deltaTime);
    }
  }

  void OnDisable() {
    animationController?.Cleanup(!Application.isPlaying);
    if (effectControllerInitialized) {
      effectAnimationController.Cleanup(!Application.isPlaying);
    }
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
    if (effectControllerInitialized) {
      effectAnimationController.PauseAnimation();
    }
  }

  public void ResumeAnimation() {
    animationController?.ResumeAnimation();
    if (effectControllerInitialized) {
      effectAnimationController.ResumeAnimation();
    }
  }

  public void StopAnimation(bool resetToDefault = false) {
    animationController?.StopAnimation(resetToDefault);
    if (effectControllerInitialized) {
      effectAnimationController.StopAnimation(false);
    }
  }

  public void _TogglePause() {
    TogglePause();
  }

  public void TogglePause(string forcePause = null) {
    animationController?.TogglePause(forcePause);
    if (effectControllerInitialized) {
      effectAnimationController.TogglePause(forcePause);
    }
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

  private void HookAnimationEvents() {
    if (animationController == null) return;
    animationController.OnEffectTriggered = HandleEffectTriggered;
    animationController.OnProjectileTriggered = HandleProjectileTriggered;
  }

  private void ConfigureEffectController() {
    if (effectNode == null) return;
    BuildEffectAnimations();
    effectAnimationController.Initialize(
      effectNode.transform,
      new[] { effectNode.gameObject },
      null,
      null,
      effectAnimations,
      new Dictionary<string, Dictionary<string, string>>(),
      null,
      new Dictionary<string, Dictionary<string, List<HBox>>>(),
      "",
      false
    );
    effectControllerInitialized = true;
  }

  private void BuildEffectAnimations() {
    effectAnimations.Clear();
    AddEffectAnimations(Effects.Esperanza);
    AddEffectAnimations(Effects.Things);
    AddEffectAnimations(Effects.Imp);
  }

  private void AddEffectAnimations(Dictionary<string, EffectData> effects) {
    if (effects == null) return;
    foreach (var kvp in effects) {
      if (string.IsNullOrEmpty(kvp.Key) || kvp.Value == null) continue;
      effectAnimations[kvp.Key] = new AnimData {
        start = kvp.Value.start,
        end = kvp.Value.end,
        duration = kvp.Value.duration * 1000f
      };
    }
  }

  private void HandleEffectTriggered(string effectKey) {
    if (string.IsNullOrEmpty(effectKey) || effectNode == null) return;
    if (!effectControllerInitialized) {
      ConfigureEffectController();
      if (!effectControllerInitialized) return;
    }
    effectAnimationController.ForceLoop = false;
    effectAnimationController.PlayAnimation(effectKey, true, resolveInterrupts: false);
  }

  private void HandleProjectileTriggered(string projectileKey) {
    if (string.IsNullOrEmpty(projectileKey) || projectileManager == null) return;
    var spawnPosition = ResolveProjectileSpawnPosition();
    var direction = ResolveProjectileDirection();
    projectileManager.SpawnProjectile(projectileKey, spawnPosition, direction);
  }

  private Vector3 ResolveProjectileSpawnPosition() {
    if (projectileSpawn != null) return projectileSpawn.position;
    if (effectNode != null) return effectNode.transform.position;
    return transform.position;
  }

  private Vector3 ResolveProjectileDirection() {
    if (useFacingDirection) {
      return IsFacingRight ? Vector3.right : Vector3.left;
    }
    if (projectileDirection.sqrMagnitude <= 0.0001f) return Vector3.right;
    var dir = projectileDirection.normalized;
    return new Vector3(dir.x, dir.y, 0f);
  }
}
