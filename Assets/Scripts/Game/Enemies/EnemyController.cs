using System;
using System.Collections.Generic;
using CustomInspector;
using UnityEngine;

[RequireComponent(typeof(EnemyInfo))]
public partial class EnemyController : MonoBehaviour {
  const float PlayerTransformRefreshSeconds = 0.5f;
  static readonly Dictionary<string, Dictionary<string, string>> EmptyInterruptData = new();
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

  private ProjectedSpriteShadowCaster2D shadowCaster;
  private AnimationController animationController = new();
  private AnimationController effectAnimationController = new();
  private readonly Dictionary<string, AnimData> effectAnimations = new();
  private readonly List<string> linkedEffectWarmKeyScratch = new();
  private readonly HashSet<string> linkedEffectWarmKeySeenScratch = new(StringComparer.OrdinalIgnoreCase);
  private readonly List<string> deathAnimationScratch = new();
  private bool effectControllerInitialized;
  private bool effectResetToEmptyPending;
  private Dictionary<string, AnimData> animationData;
  private Dictionary<string, Dictionary<string, string>> interruptData;
  private Dictionary<string, Dictionary<string, List<HBox>>> hBoxData;
  private EnemyInfo enemyInfo;
  private string appearanceOwnerId;
  private string effectAppearanceOwnerId;
  private SpriteRenderer[] cachedSpriteRenderers = Array.Empty<SpriteRenderer>();
  private Transform cachedPlayerTransform;
  private float cachedPlayerTransformRefreshedAt = -1f;
  private bool hasPinnedRuntimeResidency;
  static readonly HashSet<string> prewarmedEnemyTypes = new(StringComparer.OrdinalIgnoreCase);

  static readonly List<EnemyController> s_AllActive = new();
  int _activeListIndex = -1;
  static bool s_UpdateRegistered;
  static readonly Action s_UpdateCallback = UpdateAll;

  static void UpdateAll() {
    var remaining = s_AllActive.Count;
    var index = 0;
    while (index < s_AllActive.Count && remaining-- > 0) {
      var target = s_AllActive[index];
      target.ManagedUpdate();
      if (index < s_AllActive.Count && s_AllActive[index] == target) {
        index++;
      }
    }
  }

  static void EnsureUpdateRegistration() {
    if (s_UpdateRegistered || !Application.isPlaying) return;
    s_UpdateRegistered = true;
    RuntimeUpdateHub.Register(
      400,
      "RuntimeUpdateHub.EnemyController",
      s_UpdateCallback
    );
  }

  public bool isCulled { get; set; }
  public string CurrentAnimation => animationController != null ? animationController.CurrentAnimation : null;
  public bool IsFacingRight => animationController != null && animationController.IsFacingRight;

  internal static EnemyController[] CopyActiveSnapshot(EnemyController[] buffer) {
    var count = s_AllActive.Count;
    if (count <= 0) {
      return Array.Empty<EnemyController>();
    }
    if (buffer == null || buffer.Length != count) {
      buffer = new EnemyController[count];
    }
    for (var i = 0; i < count; i++) {
      buffer[i] = s_AllActive[i];
    }
    return buffer;
  }

  void Awake() {
    enemyInfo = GetComponent<EnemyInfo>();
    appearanceOwnerId = "enemy:" + ObjectEntityId.GetString(gameObject);
    effectAppearanceOwnerId = effectNode != null ? "effect:" + ObjectEntityId.GetString(effectNode) : "";
    ResetDebugPlaybackFlags();
    ResolveProjectileManagerReference("awake");
    ResolveEnemyTypeFromComponent();
    ConfigureEffectController();
    HookAnimationEvents();
    CacheSpriteRenderers();
  }

  void Start() {
    ResolveEnemyTypeFromComponent();
    PrimeEnemyAnimationWarmupOnce();
    if (playOnStart && animationData != null && !string.IsNullOrEmpty(defaultAnimation)) {
      PlayAnimation(defaultAnimation, true);
    }
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetStatics() {
    prewarmedEnemyTypes.Clear();
    for (var i = 0; i < s_AllActive.Count; i++) {
      var target = s_AllActive[i];
      if (target != null) {
        target._activeListIndex = -1;
      }
    }
    s_AllActive.Clear();
    s_UpdateRegistered = false;
  }

  void ResetDebugPlaybackFlags() {
    // Prevent accidentally persisted inspector debug flags from throttling runtime animation speed.
    slowDown = false;
    forceLoop = false;
  }

  internal void ManagedUpdate() {
    if (isCulled) {
      if (shadowCaster != null && shadowCaster.enabled) shadowCaster.enabled = false;
      StopLocomotionSound();
      return;
    }
    if (shadowCaster != null && !shadowCaster.enabled) shadowCaster.enabled = true;

    RefreshRuntimeResidency();

    animationController.SlowDown = slowDown;
    animationController.ForceLoop = forceLoop;
    var scaledDeltaTime = TimeScale.GetDeltaTime(this);
    animationController.Tick(scaledDeltaTime);
    if (effectControllerInitialized) {
      effectAnimationController.SlowDown = slowDown;
      effectAnimationController.ForceLoop = forceLoop;
      effectAnimationController.Tick(scaledDeltaTime);
      TryFinalizeCompletedEffectAnimation();
    }
    UpdateLocomotionSound();
  }

  void OnEnable() {
    if (Application.isPlaying && _activeListIndex < 0) {
      _activeListIndex = s_AllActive.Count;
      s_AllActive.Add(this);
      EnemyAudioLimiter.Register(this);
      EnsureUpdateRegistration();
    }
  }

  void OnDisable() {
    EnemyAudioLimiter.Unregister(this);
    if (Application.isPlaying && _activeListIndex >= 0) {
      var lastIndex = s_AllActive.Count - 1;
      var removeIndex =
        _activeListIndex <= lastIndex && s_AllActive[_activeListIndex] == this
          ? _activeListIndex
          : s_AllActive.IndexOf(this);
      if (removeIndex >= 0) {
        var last = s_AllActive[lastIndex];
        s_AllActive[removeIndex] = last;
        if (last != null) {
          last._activeListIndex = removeIndex;
        }
        s_AllActive.RemoveAt(lastIndex);
      }
      _activeListIndex = -1;
    }
    StopLocomotionSound();
    effectResetToEmptyPending = false;
    hasPinnedRuntimeResidency = false;
    animationController?.SetAppearancePinOwner("", TextureResidencyCache.PinClass.Enemy);
    if (effectControllerInitialized) {
      effectAnimationController.SetAppearancePinOwner("", TextureResidencyCache.PinClass.Effect);
    }
    animationController?.Cleanup(!Application.isPlaying);
    if (effectControllerInitialized) {
      effectAnimationController.Cleanup(!Application.isPlaying);
    }
    ResetEffectVisualToEmpty();
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

    if (!changed && animationData != null) {
      RefreshRuntimeResidency(force: true);
      if (playDefaultImmediately && !string.IsNullOrEmpty(defaultAnimation)) {
        return PlayAnimation(defaultAnimation, true);
      }
      return true;
    }

    if (changed) {
      animationController?.StopAnimation(false);
    }

    LoadAnimationData();
    ConfigureAnimationController();
    PrimeEnemyAnimationWarmupOnce();

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
      interruptData = EmptyInterruptData;
      hBoxData = null;
      return;
    }

    animationData = Animations.Enemies.TryGetValue(normalizedType, out var anims) ? anims : null;
    if (animationData == null) {
      Debug.LogWarning($"[EnemyController] No animation data found for enemy type '{normalizedType}'.");
    }

    interruptData = Interrupts.Enemies.TryGetValue(normalizedType, out var interrupts)
      ? interrupts
      : EmptyInterruptData;
    hBoxData = HBoxes.Enemies.TryGetValue(normalizedType, out var hboxes) ? hboxes : null;
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
      false,
      "",
      TextureResidencyCache.PinClass.Enemy
    );
    shadowCaster = ProjectedSpriteShadowCaster2D.Ensure(gameObject, animationController);
    RefreshRuntimeResidency(force: true);
  }

  void PrimeEnemyAnimationWarmupOnce() {
    if (!Application.isPlaying || !prewarmEnemyAnimationStarts) return;
    if (animationController == null || animationData == null || animationData.Count <= 0) return;

    var normalizedType = NormalizeEnemyType(enemyType);
    if (string.IsNullOrWhiteSpace(normalizedType)) return;
    if (!prewarmedEnemyTypes.Add(normalizedType)) return;

    var warmFrames = Mathf.Max(prewarmFramesPerAnimation, 1);
    animationController.PrimeAllAnimationStarts(warmFrames);
    PrimeLinkedEffectWarmup("startup");
  }

  public bool PlayAnimation(string animationName, bool forceRestart = false) {
    if (animationData == null || !animationData.ContainsKey(animationName)) {
      Debug.LogWarning($"[EnemyController] Animation '{animationName}' missing for '{enemyType}'.");
      return false;
    }
    var hurtWasAlreadyPlaying =
      !forceRestart &&
      HurtBloodSplatter.IsHurtAnimation(animationName) &&
      animationController != null &&
      animationController.IsPlaying &&
      string.Equals(
        animationController.CurrentAnimation,
        animationName,
        StringComparison.OrdinalIgnoreCase
      );
    var played = animationController != null && animationController.PlayAnimation(animationName, forceRestart);
    if (played && !hurtWasAlreadyPlaying && HurtBloodSplatter.IsHurtAnimation(animationName)) {
      HurtBloodSplatter.Play(transform, IsFacingRight);
    }
    return played;
  }

  public bool TryPlayDeathAnimation(
    string damageSubtype,
    out string selectedAnimation,
    out float durationSeconds
  ) {
    selectedAnimation = "";
    durationSeconds = 0f;
    deathAnimationScratch.Clear();

    var normalizedSubtype = string.IsNullOrWhiteSpace(damageSubtype)
      ? "Base"
      : damageSubtype.Trim();
    var categoryPrefix = "Death_" + normalizedSubtype + "_";

    if (animationData == null || animationData.Count <= 0) {
      return false;
    }

    foreach (var animation in animationData) {
      var category = animation.Value != null && !string.IsNullOrWhiteSpace(animation.Value.category)
        ? animation.Value.category.Trim()
        : animation.Key;
      if (!IsNumberedDeathAnimation(category, categoryPrefix)) {
        continue;
      }

      deathAnimationScratch.Add(animation.Key);
    }

    if (deathAnimationScratch.Count <= 0) {
      return false;
    }

    var selectedIndex = UnityEngine.Random.Range(0, deathAnimationScratch.Count);
    selectedAnimation = deathAnimationScratch[selectedIndex];
    if (!PlayAnimation(selectedAnimation, forceRestart: true)) {
      selectedAnimation = "";
      return false;
    }

    durationSeconds = GetAnimationDurationSeconds(selectedAnimation);
    return true;
  }

  static bool IsNumberedDeathAnimation(string category, string categoryPrefix) {
    if (string.IsNullOrWhiteSpace(category)) {
      return false;
    }
    if (!category.StartsWith(categoryPrefix, StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    var suffix = category.Substring(categoryPrefix.Length);
    if (!int.TryParse(suffix, out var animationNumber)) {
      return false;
    }

    return animationNumber > 0;
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
      ResetEffectVisualToEmpty();
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
      "",
      TextureResidencyCache.PinClass.Effect
    );
    effectControllerInitialized = true;
    ResetEffectVisualToEmpty();
    RefreshRuntimeResidency(force: true);
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
    PrepareEffectVisualForPlayback();
    effectResetToEmptyPending = true;
    effectAnimationController.ForceLoop = false;
    effectAnimationController.PlayAnimation(effectKey, true, resolveInterrupts: false);
  }

  private void HandleProjectileTriggered(string projectileKey) {
    if (string.IsNullOrEmpty(projectileKey)) return;
    ResolveProjectileManagerReference("projectile_event");
    if (projectileManager == null) {
      Debug.LogWarning(
        "[EnemyController] MissingProjectileManager" +
        " object=" + gameObject.name +
        " projectile='" + projectileKey + "'"
      );
      return;
    }
    var spawnPosition = ResolveProjectileSpawnPosition();
    var direction = ResolveProjectileDirection();
    projectileManager.SpawnProjectile(
      projectileKey,
      spawnPosition,
      direction,
      actorOwner: transform
    );
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

  void PrepareEffectVisualForPlayback() {
    if (effectNode == null) return;
    effectNode.SetDoNotRender(false);
    if (!string.IsNullOrWhiteSpace(effectNode.labelPrefix)) {
      effectNode.SetLabelPrefix("");
    }
  }

  void TryFinalizeCompletedEffectAnimation() {
    if (!effectControllerInitialized || !effectResetToEmptyPending) return;
    if (effectAnimationController.IsPlaying) return;
    ResetEffectVisualToEmpty();
  }

  void ResetEffectVisualToEmpty() {
    if (effectNode == null) return;
    effectNode.SetDoNotRender(false);
    effectNode.SetLabelPrefix("Empty");
    effectNode.ForceUpdateSpriteAndNormal(0);
    effectNode.SetAnimation("");
    effectNode.SetLabelPrefix("");
    effectResetToEmptyPending = false;
  }

  void ResolveProjectileManagerReference(string source) {
    if (projectileManager != null || !Application.isPlaying) return;
    projectileManager = SingleSceneManager.ResolveGameplayProjectileManager();
    if (projectileManager == null || !(Application.isEditor || Debug.isDebugBuild)) return;
    RuntimeLog.Log(
      "[EnemyController] ResolvedProjectileManager" +
      " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
      " object=" + gameObject.name +
      " manager='" + projectileManager.gameObject.name + "'"
    );
  }

  void PrimeLinkedEffectWarmup(string source) {
    if (!Application.isPlaying || !effectControllerInitialized || animationData == null || animationData.Count <= 0) {
      return;
    }

    linkedEffectWarmKeyScratch.Clear();
    linkedEffectWarmKeySeenScratch.Clear();
    AnimationLinkUtility.CollectLinkedEffectKeys(
      animationData,
      null,
      linkedEffectWarmKeyScratch,
      linkedEffectWarmKeySeenScratch
    );
    if (linkedEffectWarmKeyScratch.Count > 0) {
      effectAnimationController.PrimeAnimationStarts(linkedEffectWarmKeyScratch, 1);
    }

    if (linkedEffectWarmKeyScratch.Count > 0 && (Application.isEditor || Debug.isDebugBuild)) {
      RuntimeLog.Log(
        "[EnemyController] PrimedLinkedEffects" +
        " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
        " object=" + gameObject.name +
        " enemy_type='" + enemyType + "'" +
        " count=" + linkedEffectWarmKeyScratch.Count
      );
    }

    linkedEffectWarmKeyScratch.Clear();
    linkedEffectWarmKeySeenScratch.Clear();
  }

  static string NormalizeEnemyType(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }

  void CacheSpriteRenderers() {
    if (spriteObjects != null && spriteObjects.Length > 0) {
      var renderers = new List<SpriteRenderer>(spriteObjects.Length);
      for (var i = 0; i < spriteObjects.Length; i++) {
        var go = spriteObjects[i];
        if (go == null) continue;
        var renderer = go.GetComponent<SpriteRenderer>();
        if (renderer == null) renderer = go.GetComponentInChildren<SpriteRenderer>();
        if (renderer == null) continue;
        renderers.Add(renderer);
      }
      cachedSpriteRenderers = renderers.ToArray();
      return;
    }

    cachedSpriteRenderers = GetComponentsInChildren<SpriteRenderer>(includeInactive: false);
  }

  void RefreshRuntimeResidency(bool force = false) {
    if (!Application.isPlaying || animationController == null) return;
    if (!force) {
      var refreshInterval = Mathf.Max(SpriteStreamingRuntimeSettings.EnemyResidencyRefreshFrameInterval, 1);
      var frameBucket = ObjectEntityId.GetModulo(this, refreshInterval);
      if ((Time.frameCount % refreshInterval) != frameBucket) return;
    }

    var shouldPin = ShouldPinRuntimeResidency();
    if (!force && shouldPin == hasPinnedRuntimeResidency) return;

    hasPinnedRuntimeResidency = shouldPin;
    animationController.SetAppearancePinOwner(
      shouldPin ? appearanceOwnerId : "",
      TextureResidencyCache.PinClass.Enemy
    );
    if (effectControllerInitialized) {
      effectAnimationController.SetAppearancePinOwner(
        shouldPin ? effectAppearanceOwnerId : "",
        TextureResidencyCache.PinClass.Effect
      );
    }
  }

  bool ShouldPinRuntimeResidency() {
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive || StreamingWarmOrchestrator.IsWarmGateRunning) {
      return true;
    }

    if (!SpriteStreamingRuntimeSettings.EnableAppearanceSetStreaming ||
        !SpriteStreamingRuntimeSettings.EnablePinnedHotset) {
      return false;
    }

    if (!SpriteStreamingRuntimeSettings.EnableDynamicEnemyResidency) {
      return SpriteStreamingRuntimeSettings.PinAllSpawnedEnemies;
    }

    if (HasVisibleSpriteRenderer() && SpriteStreamingRuntimeSettings.PinVisibleEnemiesBeyondDistance) {
      return true;
    }

    var playerTransform = ResolvePlayerTransform();
    if (playerTransform == null) return false;

    var nearDistance = Mathf.Max(SpriteStreamingRuntimeSettings.EnemyPinNearDistance, 0f);
    var releaseDistance = Mathf.Max(SpriteStreamingRuntimeSettings.EnemyPinReleaseDistance, nearDistance);
    var distanceLimit = hasPinnedRuntimeResidency ? releaseDistance : nearDistance;
    if (distanceLimit <= 0f) return false;

    var delta = transform.position - playerTransform.position;
    return delta.sqrMagnitude <= distanceLimit * distanceLimit;
  }

  bool HasVisibleSpriteRenderer() {
    if (cachedSpriteRenderers == null || cachedSpriteRenderers.Length == 0) {
      CacheSpriteRenderers();
    }
    for (var i = 0; i < cachedSpriteRenderers.Length; i++) {
      var renderer = cachedSpriteRenderers[i];
      if (renderer == null) continue;
      if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
      if (renderer.isVisible) return true;
    }
    return false;
  }

  Transform ResolvePlayerTransform() {
    var now = Time.unscaledTime;
    if (cachedPlayerTransform != null && cachedPlayerTransform.gameObject != null) {
      if (cachedPlayerTransformRefreshedAt >= 0f &&
          now - cachedPlayerTransformRefreshedAt < PlayerTransformRefreshSeconds) {
        return cachedPlayerTransform;
      }
    }

    cachedPlayerTransformRefreshedAt = now;
    var playerGear = SingleSceneManager.ResolveGameplayPlayerController();
    cachedPlayerTransform = playerGear != null ? playerGear.transform : null;
    return cachedPlayerTransform;
  }
}
