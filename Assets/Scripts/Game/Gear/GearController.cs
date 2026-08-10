using System;
using System.Collections;
using System.Collections.Generic;
using CustomInspector;
using Unity.Profiling;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public partial class GearController : MonoBehaviour {
  static readonly ProfilerMarker PlayerTickProfilerMarker = new ProfilerMarker("GearController.PlayerTick");
  static readonly ProfilerMarker EffectTickProfilerMarker = new ProfilerMarker("GearController.EffectTick");
  const int MinimumPlayerWarmFramesAtStartup = 8;
  const int MinimumCoreEffectWarmFramesAtStartup = 24;
  const float StartupAppearanceAddressCollectionTimeoutSeconds = 0.5f;
  const string EsperanzaGearMaterialName = "EsperanzaGear";
  const string EsperanzaHairMaterialName = "EsperanzaHair";
  const string EsperanzaBodyMaterialName = "EsperanzaBody";
  const string EsperanzaGearShaderName = "AllIn1SpriteShader/AllIn1Urp2dRenderer";
  const float CharacterFormGlowStrength = 6f;
  static readonly string[] corePlayerWarmAnimationKeys = {
    "Blast",
    "Breathe",
    "Hurt",
    "Stance",
    "Walk",
    "StanceToJump",
    "Jump",
    "JumpToJumpFalling",
    "JumpFalling",
    "JumpFallingToJumpLanding",
    "JumpLanding"
  };
  static readonly string[] DefaultCharacterAnimationKeys = {
    "Walk",
    "Run",
    "Sprint",
    "Stance",
    "Breathe",
    "Jump",
    "JumpDouble",
    "JumpLanding",
    "JumpFalling",
    "Dance",
    "Block",
    "Dodge",
    "Hurt"
  };
  static readonly string[] CoreCombatWarmAnimationKeys = { "Blast" };
  public static string[] CorePlayerWarmAnimationKeys => corePlayerWarmAnimationKeys;

  [Button(nameof(_TogglePause), label = "un/pause", size = Size.small)] public bool slowDown;
  [Button(nameof(ForceAnimation), label = "Play", size = Size.small)] public bool forceLoop;
  [Button(nameof(LoadGear), label = "LoadGear", size = Size.small)]
  [ShowMethod(nameof(GetCurrentAnimationForInspector), label = "Current Animation")]
  [HideField]
  public bool _bool;
  [HideInInspector] public string defaultAnimation = "Breathe";
  public float timer;
  public float pseudoTimer;
  [Button(nameof(ResetPseudoTimer), label = "Reset", size = Size.small)][HideField] public bool resetPseudoTimerButton;

  public GameObject[] GearObjects;
  public GameObject[] HairObjects;
  public GameObject[] OtherBounceGearObjects;
  public GameObject[] SkinObjects;
  public GameObject[] HBoxObjects;
  [Header("Effects")]
  public SpriteWithNormals effectNode;
  [Header("Projectiles")]
  public ProjectileManager projectileManager;
  public Transform projectileSpawn;
  public bool useFacingDirection = true;
  public Vector2 projectileDirection = Vector2.right;
  public GameObject EyesSkin;
  public GameObject HairSkin;
  [SerializeField] Material esperanzaGearMaterial;
  [SerializeField] Material esperanzaHairMaterial;
  [SerializeField] Material esperanzaBodyMaterial;
  [Dictionary]
  public GearFormItemMap lastGear = new();
  public bool needsFlip;
  [Header("Streaming Warmup")]
  [SerializeField] bool prewarmAnimationStartsOnLoad = true;
  [SerializeField, Min(1)] int prewarmFramesPerAnimation = 1;
  [SerializeField] bool queueEquippedAnimationWarmup = true;
  [SerializeField, Min(1)] int equipWarmupFrameChunk = 24;
  [SerializeField, Min(1)] int equipWarmupChunksPerFrame = 6;
  [SerializeField, Min(50)] int equipWarmupEnqueueBudgetPerFrame = 96;
  [SerializeField] bool logEquipWarmupSummary;
  private GameObject[] combinedBounces;
  private AnimationController animationController = new();
  private AnimationController effectAnimationController = new();
  private readonly Dictionary<string, AnimData> effectAnimations = new();
  private readonly List<string> equipWarmupAddressScratch = new();
  private readonly HashSet<string> equipWarmupSeenAddressScratch = new(StringComparer.OrdinalIgnoreCase);
  private readonly List<string> equipWarmAnimationScratch = new(64);
  private readonly HashSet<string> equipWarmAnimationSeenScratch = new(StringComparer.OrdinalIgnoreCase);
  private readonly List<string> persistentWarmAnimationScratch = new(64);
  private readonly HashSet<string> persistentWarmAnimationSeenScratch = new(StringComparer.OrdinalIgnoreCase);
  private int persistentWarmPlanAppearanceRevision = -1;
  private int persistentWarmPlanContentVersion = -1;
  private string persistentWarmPlanForm = "";
  private readonly List<string> startupAppearanceWarmupAddressScratch = new();
  private readonly HashSet<string> startupAppearanceWarmupSeenAddressScratch = new(StringComparer.OrdinalIgnoreCase);
  private readonly List<string> coreEffectWarmupAddressScratch = new();
  private readonly HashSet<string> coreEffectWarmupSeenAddressScratch = new(StringComparer.OrdinalIgnoreCase);
  private readonly HashSet<string> spriteWarmupLibraryScratch = new(StringComparer.OrdinalIgnoreCase);
  private readonly List<string> linkedEffectWarmKeyScratch = new();
  private readonly HashSet<string> linkedEffectWarmKeySeenScratch = new(StringComparer.OrdinalIgnoreCase);
  private readonly List<string> linkedProjectileWarmKeyScratch = new();
  private readonly HashSet<string> linkedProjectileWarmKeySeenScratch = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, string> equipPartPrefixScratch = new(StringComparer.OrdinalIgnoreCase);
  private Dictionary<string, string> pendingEquipWarmupPartPrefixes;
  private Coroutine equipWarmupRoutine;
  private Coroutine startupAppearanceWarmupRoutine;
  private bool startupAppearanceWarmupPausedAnimation;
  private bool effectControllerInitialized;
  private bool effectResetToEmptyPending;
  private bool equippedStartupWarmupCompleted;
  private bool sceneAppearanceAtlasPinsManaged;
  private bool runtimeInitialized;
  private string appearanceOwnerId;
  private string effectAppearanceOwnerId;
  private string coreEffectWarmOwnerId;
  private string startupAppearanceWarmOwnerId;
  private Action offAbilityLoadoutChanged;
  private int appearanceRevision = 1;
  private bool runtimeGearMaterialWarningLogged;
  private bool runtimeHairMaterialWarningLogged;
  private bool runtimeBodyMaterialWarningLogged;

  public bool IsFacingRight => animationController != null && animationController.IsFacingRight;
  public int AppearanceRevision => appearanceRevision;
  public bool SceneAppearanceAtlasPinsManaged =>
    sceneAppearanceAtlasPinsManaged &&
    animationController != null &&
    animationController.AppearancePinsExternallyManaged;


  void OnEnable() {
    EnsureRuntimeInitialized("enable");
    ResetHairAnimationGustTracking();
    TryStartPendingEquipWarmup("enable");
  }

  void Start() {
    EnsureRuntimeInitialized("start");
  }

  static bool ShouldLogRuntimeInitDebug() {
    if (!SpriteStreamingRuntimeSettings.EnableVerboseRuntimeConsoleLogs) {
      return false;
    }
    return Application.isEditor || Debug.isDebugBuild;
  }

  void EnsureRuntimeInitialized(string source) {
    if (runtimeInitialized) return;
    runtimeInitialized = true;
    offAbilityLoadoutChanged = MessageBus.On(
      CharacterMessageTopics.AbilityLoadoutChanged,
      HandleAbilityLoadoutChanged
    );
    InitializeGearDamageLocationReset();
    ResetDebugPlaybackFlags();
    appearanceOwnerId = "player:" + ObjectEntityId.GetString(gameObject);
    effectAppearanceOwnerId = effectNode != null ? "effect:" + ObjectEntityId.GetString(effectNode) : "";
    coreEffectWarmOwnerId = "player_core_effects:" + ObjectEntityId.GetString(gameObject);
    startupAppearanceWarmOwnerId = "player_startup:" + ObjectEntityId.GetString(gameObject);
    combinedBounces = CombineGameObjectArrays(HairObjects, OtherBounceGearObjects);
    ConfigureBounceDynamics();
    NormalizeSkinSpriteDefaultsForRuntime();
    NormalizeGearDamageDefaultsForRuntime();
    ResolveProjectileManagerReference(source);
    PrimeSpriteStreamingWarmup();
    ConfigureAnimationController();
    ConfigureEffectController();
    PrimeControllerAnimationWarmup();
    PrimeCoreCombatEffectWarmup(source);
    HookAnimationEvents();
    if (Application.isPlaying) {
      LeanTween.reset();
      LeanTween.init(4000);
    }
    animationController.PlayAnimation(defaultAnimation, true);
    QueueStartupAppearanceWarmup(source, pauseUntilReady: ShouldPauseStartupAppearanceWarmupUntilReady());
    if (ShouldLogRuntimeInitDebug()) {
      RuntimeLog.Log(
        "[GearController] RuntimeInit" +
        " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
        " object=" + gameObject.name +
        " active=" + (gameObject.activeInHierarchy ? 1 : 0) +
        " combined_bounces=" + (combinedBounces != null ? combinedBounces.Length : 0)
      );
    }
  }

  void ResetDebugPlaybackFlags() {
    // Prevent accidentally persisted inspector debug flags from throttling runtime animation speed.
    slowDown = false;
    forceLoop = false;
  }

  void Update() {
    if (animationController == null) return;
    RefreshAttackSpeedTiming();
    ApplyPlaybackDebugFlags();
    QueuePendingFlipIfNeeded();

    var deltaTime = TimeScale.GetDeltaTime(this);
    var timerBeforeTick = animationController.animationTimer;
    TickControllers(deltaTime);
    TickGearDamageFade(deltaTime);
    UpdateHairAnimationGust(deltaTime);
    UpdateLocomotionSound();
    RefreshInspectorTimers(timerBeforeTick, deltaTime);
  }

  void TickControllers(float deltaTime) {
    if (deltaTime <= 0f) return;

    PlayerTickProfilerMarker.Begin();
    animationController?.Tick(deltaTime);
    PlayerTickProfilerMarker.End();
    if (effectControllerInitialized) {
      EffectTickProfilerMarker.Begin();
      effectAnimationController.Tick(deltaTime);
      TryFinalizeCompletedEffectAnimation();
      EffectTickProfilerMarker.End();
    }
  }

  void ApplyPlaybackDebugFlags() {
    animationController.SlowDown = slowDown;
    animationController.ForceLoop = forceLoop;
    if (!effectControllerInitialized) return;
    effectAnimationController.SlowDown = slowDown;
    effectAnimationController.ForceLoop = forceLoop;
  }

  void RefreshAttackSpeedTiming() {
    if (animationController == null) return;
    animationController.AttackSpeedSeconds = AttackSpeedTiming.ResolveStatSeconds(AllStatValues.Esperanza);
  }

  void QueuePendingFlipIfNeeded() {
    if (!needsFlip) return;
    animationController.QueueFlip();
    needsFlip = false;
  }

  void RefreshInspectorTimers(float timerBeforeTick, float deltaTime) {
    var timerAfterTick = animationController.animationTimer;
    timer = timerAfterTick;
    pseudoTimer += ResolvePseudoTimerDelta(timerBeforeTick, timerAfterTick, deltaTime);
  }

  float ResolvePseudoTimerDelta(float timerBeforeTick, float timerAfterTick, float deltaTime) {
    if (!ShouldAdvancePseudoTimer(deltaTime)) {
      return 0f;
    }

    var timerDelta = timerAfterTick - timerBeforeTick;
    if (timerDelta >= 0f) {
      return timerDelta;
    }

    return ResolveActiveTimerStep(deltaTime);
  }

  bool ShouldAdvancePseudoTimer(float deltaTime) {
    return deltaTime > 0f &&
           animationController != null &&
           animationController.IsPlaying &&
           !string.IsNullOrWhiteSpace(animationController.CurrentAnimation);
  }

  float ResolveActiveTimerStep(float deltaTime) {
    var slowFactor = animationController != null && animationController.SlowDown ? 20f : 1f;
    return (deltaTime * 1000f) / slowFactor;
  }

  public void ResetPseudoTimer() {
    var previousPseudoTimer = pseudoTimer;
    pseudoTimer = 0f;
    if (!ShouldLogRuntimeInitDebug()) return;
    RuntimeLog.Log(
      "[GearController] ResetPseudoTimer" +
      " object=" + gameObject.name +
      " previous_ms=" + previousPseudoTimer +
      " timer_ms=" + timer
    );
  }

  private void ConfigureAnimationController() {
    if (animationController == null) return;
    // Prioritize skin targets first so core body animation continuity is protected under pin budgets.
    var spriteTargets = CombineGameObjectArrays(SkinObjects, GearObjects);
    animationController.Initialize(
      transform,
      spriteTargets,
      combinedBounces,
      HBoxObjects,
      Animations.Esperanza,
      Interrupts.Esperanza,
      BounceAdjustments.Esperanza,
      HBoxes.Esperanza,
      defaultAnimation,
      false,
      appearanceOwnerId,
      TextureResidencyCache.PinClass.Player
    );
    RefreshAttackSpeedTiming();
    ProjectedSpriteShadowCaster2D.Ensure(
      gameObject,
      animationController,
      combinedBounces
    );
  }

  static GameObject[] CombineGameObjectArrays(GameObject[] first, GameObject[] second) {
    var firstCount = CountNonNullEntries(first);
    var secondCount = CountNonNullEntries(second);
    if (firstCount <= 0 && secondCount <= 0) {
      return Array.Empty<GameObject>();
    }

    var combined = new GameObject[firstCount + secondCount];
    var index = 0;
    CopyNonNullEntries(first, combined, ref index);
    CopyNonNullEntries(second, combined, ref index);
    return combined;
  }

  static int CountNonNullEntries(GameObject[] values) {
    if (values == null || values.Length <= 0) {
      return 0;
    }

    var count = 0;
    for (var i = 0; i < values.Length; i++) {
      if (values[i] != null) {
        count++;
      }
    }
    return count;
  }

  static void CopyNonNullEntries(GameObject[] source, GameObject[] destination, ref int destinationIndex) {
    if (source == null || source.Length <= 0 || destination == null) {
      return;
    }

    for (var i = 0; i < source.Length; i++) {
      var value = source[i];
      if (value == null) {
        continue;
      }
      destination[destinationIndex++] = value;
    }
  }

  public string CurrentAnimation => animationController != null ? animationController.CurrentAnimation : null;

  string GetCurrentAnimationForInspector() {
    return CurrentAnimation ?? "";
  }

  public AnimationController Controller => animationController;

  void OnDestroy() {
#if UNITY_EDITOR
    if (!Application.isPlaying) {
      Selection.activeObject = null;
    }
#endif
    StopStartupAppearanceWarmup();
    StopEquipWarmupQueue();
    DisposeBounceDynamics();
    offAbilityLoadoutChanged?.Invoke();
    offAbilityLoadoutChanged = null;
    DisposeGearDamageLocationReset();
    ReleaseCoreCombatEffectWarmupPins();
    ReleaseRuntimeGearMaterialHandle();
    animationController?.Cleanup(!Application.isPlaying);
    if (effectControllerInitialized) {
      effectAnimationController.Cleanup(!Application.isPlaying);
    }
  }

  void OnDisable() {
    ResetHairAnimationGustTracking();
    StopLocomotionSound();
    StopStartupAppearanceWarmup();
    StopEquipWarmupQueue();
    effectResetToEmptyPending = false;
    animationController?.Cleanup(false);
    if (effectControllerInitialized) {
      effectAnimationController.Cleanup(false);
    }
    ResetEffectVisualToEmpty();
  }
}
