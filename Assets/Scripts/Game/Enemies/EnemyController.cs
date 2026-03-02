using System;
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
  [Header("Streaming Warmup")]
  [SerializeField] bool prewarmEnemyAnimationStarts = true;
  [SerializeField, Min(1)] int prewarmFramesPerAnimation = 1;

  private AnimationController animationController = new();
  private AnimationController effectAnimationController = new();
  private readonly Dictionary<string, AnimData> effectAnimations = new();
  private bool effectControllerInitialized;
  private Dictionary<string, AnimData> animationData;
  private Dictionary<string, Dictionary<string, string>> interruptData;
  private Dictionary<string, Dictionary<string, List<HBox>>> hBoxData;
  private EnemyInfo enemyInfo;
  private string appearanceOwnerId;
  private string effectAppearanceOwnerId;
  static readonly HashSet<string> prewarmedEnemyTypes = new(StringComparer.OrdinalIgnoreCase);

  public string CurrentAnimation => animationController != null ? animationController.CurrentAnimation : null;
  public bool IsFacingRight => animationController != null && animationController.IsFacingRight;

  void Awake() {
    enemyInfo = GetComponent<EnemyInfo>();
    appearanceOwnerId = "enemy:" + gameObject.GetInstanceID().ToString();
    effectAppearanceOwnerId = effectNode != null ? "effect:" + effectNode.GetInstanceID().ToString() : "";
    ResetDebugPlaybackFlags();
    ResolveEnemyTypeFromComponent();
    ConfigureEffectController();
    HookAnimationEvents();
  }

  void Start() {
    ResolveEnemyTypeFromComponent();
    PrimeEnemyAnimationWarmupOnce();
    if (playOnStart && animationData != null && !string.IsNullOrEmpty(defaultAnimation)) {
      PlayAnimation(defaultAnimation, true);
    }
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetWarmupCacheOnDomainReload() {
    prewarmedEnemyTypes.Clear();
  }

  void ResetDebugPlaybackFlags() {
    // Prevent accidentally persisted inspector debug flags from throttling runtime animation speed.
    slowDown = false;
    forceLoop = false;
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

  public bool SetEnemyType(string value, bool playDefaultImmediately = false) {
    var normalized = NormalizeEnemyType(value);
    var changed = !string.Equals(enemyType, normalized, StringComparison.OrdinalIgnoreCase);
    enemyType = normalized;

    if (enemyInfo == null) {
      enemyInfo = GetComponent<EnemyInfo>();
    }
    if (enemyInfo != null && !string.Equals(enemyInfo.enemyType, enemyType, StringComparison.OrdinalIgnoreCase)) {
      enemyInfo.enemyType = enemyType;
    }

    if (changed) {
      animationController?.StopAnimation(false);
    }

    LoadAnimationData();
    ConfigureAnimationController();

    if (playDefaultImmediately && animationData != null && !string.IsNullOrEmpty(defaultAnimation)) {
      return PlayAnimation(defaultAnimation, true);
    }

    return animationData != null;
  }

  private void ResolveEnemyTypeFromComponent() {
    var current = NormalizeEnemyType(enemyType);
    if (!string.IsNullOrWhiteSpace(current)) {
      SetEnemyType(current, playDefaultImmediately: false);
      return;
    }

    if (enemyInfo == null) {
      enemyInfo = GetComponent<EnemyInfo>();
    }

    var fromInfo = enemyInfo != null ? NormalizeEnemyType(enemyInfo.enemyType) : "";
    SetEnemyType(fromInfo, playDefaultImmediately: false);
  }

  private void LoadAnimationData() {
    var normalizedType = NormalizeEnemyType(enemyType);
    enemyType = normalizedType;

    if (string.IsNullOrWhiteSpace(normalizedType)) {
      animationData = null;
      interruptData = new Dictionary<string, Dictionary<string, string>>();
      hBoxData = null;
      return;
    }

    animationData = Animations.Enemies.TryGetValue(normalizedType, out var anims) ? anims : null;
    if (animationData == null) {
      Debug.LogWarning($"[EnemyController] No animation data found for enemy type '{normalizedType}'.");
    }

    interruptData = Interrupts.Enemies.TryGetValue(normalizedType, out var interrupts)
      ? interrupts
      : new Dictionary<string, Dictionary<string, string>>();
    hBoxData = HBoxes.Enemies.TryGetValue(normalizedType, out var hboxes) ? hboxes : null;
  }

  private void ConfigureAnimationController() {
    if (animationController == null) return;
    var enemyOwnerId = SpriteStreamingRuntimeSettings.PinAllSpawnedEnemies ? appearanceOwnerId : "";
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
      false,
      enemyOwnerId,
      TextureResidencyCache.PinClass.Enemy
    );
  }

  void PrimeEnemyAnimationWarmupOnce() {
    if (!Application.isPlaying || !prewarmEnemyAnimationStarts) return;
    if (animationController == null || animationData == null || animationData.Count <= 0) return;

    var normalizedType = NormalizeEnemyType(enemyType);
    if (string.IsNullOrWhiteSpace(normalizedType)) return;
    if (!prewarmedEnemyTypes.Add(normalizedType)) return;

    var warmFrames = Mathf.Max(prewarmFramesPerAnimation, 1);
    animationController.PrimeAllAnimationStarts(warmFrames);
    if (effectControllerInitialized) {
      effectAnimationController.PrimeAllAnimationStarts(1);
    }
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
      false,
      effectAppearanceOwnerId,
      TextureResidencyCache.PinClass.Effect
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

  static string NormalizeEnemyType(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }
}
