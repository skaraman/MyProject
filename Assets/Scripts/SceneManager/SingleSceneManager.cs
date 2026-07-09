
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using CustomInspector;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public partial class SingleSceneManager : MonoBehaviour {
  static int nextPauseDialogResumeToken = 1;
  static int nextGameplayLoadFlowId = 1;
  static int activePauseDialogResumeToken;
  static int pendingPauseDialogResumeToken;
  static SingleSceneManager instance;

  enum WarmGateMode {
    StartGame = 0,
    LoadSave = 1,
    GearApplyReturn = 2
  }

  enum Section {
    None = 0,
    MainMenu = 1,
    LoadMenu = 2,
    SettingsMenu = 3,
    Gameplay = 4,
    Pause = 5
  }

  enum OptimalGameplayLoadingStage {
    Player = 0,
    Location = 1,
    Enemies = 2,
    Ui = 3,
    Dialog = 4,
    FinalizingReveal = 5
  }

  struct SectionDescriptor {
    public readonly string inputMap;
    public readonly bool sceneActiveByDefault;
    public readonly bool restoreSceneLightsByDefault;
    public readonly bool resetPauseAppearanceRevision;

    public SectionDescriptor(
      string inputMap,
      bool sceneActiveByDefault,
      bool restoreSceneLightsByDefault,
      bool resetPauseAppearanceRevision
    ) {
      this.inputMap = inputMap;
      this.sceneActiveByDefault = sceneActiveByDefault;
      this.restoreSceneLightsByDefault = restoreSceneLightsByDefault;
      this.resetPauseAppearanceRevision = resetPauseAppearanceRevision;
    }
  }

  struct SectionTransitionRequest {
    public readonly Section targetSection;
    public readonly string overlayTag;
    public readonly bool requestMainMenuLocation;
    public readonly bool waitForStreamingIdle;
    public readonly bool showProgressUi;
    public readonly bool showLoadingLight;
    public readonly bool switchInputMapToNone;

    public SectionTransitionRequest(
      Section targetSection,
      string overlayTag,
      bool requestMainMenuLocation,
      bool waitForStreamingIdle,
      bool showProgressUi,
      bool showLoadingLight = true,
      bool switchInputMapToNone = false
    ) {
      this.targetSection = targetSection;
      this.overlayTag = overlayTag;
      this.requestMainMenuLocation = requestMainMenuLocation;
      this.waitForStreamingIdle = waitForStreamingIdle;
      this.showProgressUi = showProgressUi;
      this.showLoadingLight = showLoadingLight;
      this.switchInputMapToNone = switchInputMapToNone;
    }
  }

  readonly struct OptimalGameplayLoadingProgress {
    public readonly bool IsActive;
    public readonly float TargetProgress;
    public readonly float ProgressCeiling;
    public readonly string Detail;

    public OptimalGameplayLoadingProgress(bool isActive, float targetProgress, float progressCeiling, string detail) {
      IsActive = isActive;
      TargetProgress = Mathf.Clamp01(targetProgress);
      ProgressCeiling = Mathf.Clamp(Mathf.Max(progressCeiling, targetProgress), 0f, 1f);
      Detail = string.IsNullOrWhiteSpace(detail) ? "" : detail.Trim();
    }
  }

  public InputProcessor inputProcessor;
  public MouseManager mouseManager;
  [FormerlySerializedAs("Blackscreen")]
  public GameObject LoadingScreen;
  private All1AnimatorScript blackscreen;
  private GameObject loadingBlackscreen;
  private SpriteRenderer loadingBlackscreenRenderer;
  private GameObject loadingLightObject;
  private GameObject loadingCircle;
  private GameObject loadingTextObject;
  private FontText loadingText;
  private bool loadingUiFeedbackActive;
  private bool loadingOverlayChildrenReady;
  private int loadingPercent = -1;
  private string loadingStatusDetail = "";
  private string loadingStatusOverride = "";
  private float loadingPercentDisplayValue;
  private bool loadingPercentDisplayInitialized;
  private int loadingProgressUiArmCheckFrame = -1;
  private int loadingProgressPeakOutstanding = -1;
  private int loadingProgressGoalTotal = -1;
  private int loadingProgressGoalBestRemaining = int.MaxValue;
  private float loadingProgressIdleStartedAt = -1f;
  private bool loadingProgressObservedWork;
  private float loadingProgressNextDebugLogAt = -1f;
  private float loadingHeartbeatStartedAt = -1f;
  private float loadingHeartbeatLastLoggedAt = -1f;
  private int loadingHeartbeatCount;
  private float loadingHeartbeatNextLogAt = -1f;
  private string loadingStageStallKey = "";
  private float loadingStageStallStartedAt = -1f;
  private float loadingStageStallNextLogAt = -1f;
  private int loadingBlockingReadyCount;
  private int loadingBlockingTotalCount;
  private bool loadingBlockingCriticalReady;
  private bool loadingBlockingHardBypassUsed;
  private bool loadingBlockingStateKnown;
  private int activeGameplayLoadFlowId;
  private float activeGameplayLoadFlowStartedAt = -1f;
  private string activeGameplayLoadFlowKind = "";
  private string activeGameplayLoadFlowTargetLocation = "";
  private Section activeGameplayLoadFlowOriginSection = Section.None;
  private bool activeGameplayLoadFlowIsNewGame;
  private int activeGameplayLoadFlowSlot = -1;
  private OptimalGameplayLoadingStage gameplayLoadingStageForLoad;
  private string gameplayLoadingStageLocationId = "";
  const float LoadingProgressCompletionSettleSeconds = 0.15f;
  const float LoadingProgressPreReadyCap = 0.95f;
  const float OptimalLoadingProgressPlayerFloor = 0.18f;
  const float OptimalLoadingProgressLocationFloor = 0.42f;
  const float OptimalLoadingProgressEnemiesFloor = 0.72f;
  const float OptimalLoadingProgressUiFloor = 0.86f;
  const float OptimalLoadingProgressDialogFloor = 0.94f;
  const float LoadingZeroPercentStallDelaySeconds = 1.5f;
  const float LoadingZeroPercentStallLogIntervalSeconds = 2.0f;
  const float LoadingWaitStateLogIntervalSeconds = 2.0f;
  const float LoadingStageStallDelaySeconds = 2.0f;
  const float LoadingStageStallLogIntervalSeconds = 2.0f;
  const float LoadingHeartbeatLogIntervalSeconds = 1.0f;
  const float LoadingHeartbeatAcceptableGapSeconds = 2.0f;
  const float GameplayWarmGatePrereqTimeoutSeconds = 4.0f;
  const float GameplayWarmGateEnemyArchetypeWaitSeconds = 0.5f;
  const float GameplayWarmGatePrereqLogIntervalSeconds = 1.0f;
  const int RevealOpaqueSettleFrames = 2;
  const float RevealOpaqueSettleTimeoutSeconds = 10.0f;
  const float RevealOpaqueSettleMinimumStableSeconds = 0.25f;
  const float PostRevealInputTraceWindowSeconds = 2.0f;

  public static int CurrentPauseDialogSuspendToken => activePauseDialogResumeToken;
  public static int ActiveGameplayLoadFlowId => instance != null ? instance.activeGameplayLoadFlowId : 0;
  public static string ActiveGameplayLoadFlowKind => instance != null ? instance.activeGameplayLoadFlowKind : "";
  public static string ActiveGameplayLoadFlowTargetLocation => instance != null ? instance.activeGameplayLoadFlowTargetLocation : "";
  public static bool HasActiveGameplayLoadFlow => instance != null && instance.activeGameplayLoadFlowId > 0;
  public static string ActiveInputMap => instance != null ? instance.activeInputMap : "";
  public static float GameplayRevealInputTraceAgeSeconds {
    get {
      if (instance == null || instance.lastGameplayRevealCompletedAt < 0f) return -1f;
      return Mathf.Max(Time.realtimeSinceStartup - instance.lastGameplayRevealCompletedAt, 0f);
    }
  }
  public static bool IsGameplayRevealInputTraceWindowActive {
    get {
      var age = GameplayRevealInputTraceAgeSeconds;
      return age >= 0f && age <= PostRevealInputTraceWindowSeconds;
    }
  }

  public GameObject MainMenu;
  public GameObject LoadMenu;
  public GameObject SettingsMenu;
  public GameObject GameplayInterface;
  public GameObject PauseMenu;
  public GameObject Scene;
  [SerializeField] GameObject sceneObjectLights;
  [SerializeField] GameObject damageEntitiesRoot;
  [SerializeField] ProjectileManager gameplayProjectileManager;

  public AutoSaver autoSaver;
  public SaveSlotView saveSlotView;
  [Header("Player Bootstrap")]
  [SerializeField] GameObject playerCharacterPrefab;
  [Header("Debug")]
  [SerializeField] bool debugMode;
  [SerializeField, HideInInspector] GameObject debugLocationPrefab;
  [SerializeField, ShowIf(nameof(debugMode))] string debugLocationId = "";
  const bool useScenarioWarmGate = true;
  const float startWarmTimeoutSeconds = 4.0f;
  const float startWarmHardTimeoutSeconds = 10.0f;
  const float startWarmRequiredRatio = 0.35f;
  const float loadSaveWarmRequiredRatioCap = 0.45f;
  const float loadSaveWarmRequiredRatioFloor = 0.25f;
  const int warmGateCriticalEnemyCount = 3;
  const float warmGateCriticalEnemyDistance = 25f;
  const bool warmGatePreloadCorePlayerEffects = true;
  const float gearReturnWarmTimeoutSeconds = 3.0f;
  const float gearReturnWarmHardTimeoutSeconds = 16.0f;

  static bool ShouldLogPauseDialogResumeDebug() {
    if (!SpriteStreamingRuntimeSettings.EnableVerboseRuntimeConsoleLogs) {
      return false;
    }
    return Application.isEditor || Debug.isDebugBuild;
  }

  static bool ShouldLogGameplayRuntimeDebug() {
    if (!SpriteStreamingRuntimeSettings.EnableVerboseRuntimeConsoleLogs) {
      return false;
    }
    return Application.isEditor || Debug.isDebugBuild;
  }

  bool IsEditorStartupDebugEnabled() {
    return Application.isEditor && debugMode;
  }

  public static bool TryConsumePauseDialogResumeToken(int token) {
    if (token <= 0 || pendingPauseDialogResumeToken != token) {
      return false;
    }

    pendingPauseDialogResumeToken = 0;
    if (ShouldLogPauseDialogResumeDebug()) {
      Debug.Log("[SingleSceneManager][PauseDialogResume] consumed_token=" + token);
    }
    return true;
  }

  static void ClearPauseDialogResumeState(string source) {
    if (activePauseDialogResumeToken <= 0 && pendingPauseDialogResumeToken <= 0) {
      return;
    }

    if (ShouldLogPauseDialogResumeDebug()) {
      Debug.Log(
        "[SingleSceneManager][PauseDialogResume] clear source='" + (source ?? "") +
        "' active_token=" + activePauseDialogResumeToken +
        " pending_token=" + pendingPauseDialogResumeToken
      );
    }

    activePauseDialogResumeToken = 0;
    pendingPauseDialogResumeToken = 0;
    MessageBus.Send("dialog.finished", "pause_resume_cleared:" + (source ?? ""));
  }
  const float gearReturnRequiredRatio = 0.95f;
  const bool allowHardTimeoutBypass = true;
  const float fadeLeadSeconds = 0.0f;
  const float fallbackTransitionSeconds = 3.0f;
  const float fadeToBlackSeconds = 2.0f;
  const float fadeFromBlackSeconds = 2.0f;
  const float loadingCircleSpinSpeedDegreesPerSecond = 360f;
  static readonly bool waitForStreamingIdleBeforeFadeOut = true;
  const float streamingIdleMinimumWaitSeconds = 0.25f;
  const float streamingIdleTimeoutSeconds = 6.0f;
  const bool allowStreamingIdleTimeoutBypass = false;
  const int streamingIdleStableFrames = 2;
  const int streamingIdleAllowedQueued = 32;
  const int streamingIdleAllowedInFlight = 4;
  const int streamingBlockingReadyMaxOutstandingDesktop = 36;
  const int streamingBlockingReadyMaxOutstandingMobile = 36;
  const int streamingBlockingReadyMaxInFlightDesktop = 4;
  const int streamingBlockingReadyMaxInFlightMobile = 4;
  const bool enforceZeroOutstandingBeforeUnlock = false;
  const float loadingPercentRisePerSecond = 55f;
  const bool enablePreUnlockVisibleSpritePrefetch = true;
  const int preUnlockPrefetchAnimationFrames = 3;
  const int preUnlockPrefetchLookAheadFrames = 3;
  const int preUnlockPrefetchMaxAddresses = 768;
  const int preUnlockPrefetchMinAddresses = 128;
  const int preUnlockPrefetchEnqueueBudgetPerFrame = 64;
  const int preUnlockPrefetchFrameJumpClamp = 4;
  const float preUnlockTargetCacheRefreshSeconds = 0.5f;
  const bool preUnlockPrefetchExpandAtlasSiblings = true;
  const int preUnlockPrefetchMaxAtlasSiblingsPerSeed = 24;
  const bool preUnlockPrefetchIncludeUiTargets = false;
  const bool enablePreUnlockControllerAnimationPrefetch = true;
  const int preUnlockPlayerAnimationStarts = 8;
  const int preUnlockEnemyAnimationStartsPerController = 4;
  const bool enablePreUnlockAnimationPlaybackWarmup = true;
  const int preUnlockAnimationPlaybackPasses = 1;
  const int preUnlockAnimationFramePreloadPasses = 1;
  const bool preUnlockReprefetchVisibleSpritesAfterAnimationWarmup = false;
  const float preUnlockWarmupQueueSettleTimeoutSeconds = 0.5f;
  const float preUnlockBlockingBudgetSeconds = 1.25f;
  static readonly bool enablePreUnlockResidentPinning = true;
  const int preUnlockResidentPinMaxAddresses = 768;
  const bool preUnlockWarmEnemyAnimationPlayback = true;
  const int preUnlockAnimationWarmupControllersPerFrame = 1;
  static readonly int preUnlockAnimationWarmupMaxEnemyControllers = 0;
  const float preUnlockAnimationWarmupEnemyDistance = 25f;
  const float startupFadeWatchdogSeconds = 1.0f;
  const bool enableLoadingStallEmergencyUnlock = true;
  const float loadingStallEmergencyUnlockSeconds = 12.0f;
  const float postUnlockPinReleaseDelaySeconds = 8.0f;
  const int postUnlockPinReleaseMaxOutstanding = 192;
  const float postUnlockPinReleaseTimeoutSeconds = 20.0f;
  const int startupLoadMenuPrewarmFrames = 2;
  static readonly WaitForSecondsRealtime FadeToBlackDelay = new(Mathf.Max(fadeToBlackSeconds, 0f));
  static readonly WaitForSecondsRealtime WarmGateLeadDelay = new(Mathf.Max(fadeLeadSeconds, 0f));
  static readonly WaitForSecondsRealtime FallbackTransitionDelay = new(Mathf.Max(fallbackTransitionSeconds, 0f));
  static readonly WaitForSecondsRealtime StartupFadeWatchdogDelay = new(Mathf.Max(startupFadeWatchdogSeconds, 0f));
  static readonly WaitForSecondsRealtime RevealCleanupDelay = new(Mathf.Max(fadeFromBlackSeconds + 0.15f, 0.5f));
  static readonly WaitForSecondsRealtime FadeFromBlackDelay = new(Mathf.Max(fadeFromBlackSeconds, 0f));
  static readonly WaitForSecondsRealtime PostUnlockPinReleaseDelay = new(Mathf.Max(postUnlockPinReleaseDelaySeconds, 0f));
  static readonly string defaultStartLocation = LocationEnemyData.DomeCityLocationId;
  const string mainMenuFlowLocationId = LocationEnemyData.MainMenuLocationId;
  const string gameplayFlowFallbackLocationId = LocationEnemyData.DomeCityLocationId;
  private List<Action> actions = new();

  private bool init;
  private Coroutine startGameRoutine;
  private Coroutine resumeGameplayRoutine;
  private Coroutine startupGameplayRoutine;
  private Coroutine startupMainMenuRevealRoutine;
  private Coroutine startupFadeWatchdogRoutine;
  private Coroutine unlockFadeFailSafeRoutine;
  private Coroutine sectionTransitionRoutine;
  private Coroutine deferredPostRevealWarmupRoutine;
  private GearController cachedPlayerGearController;
  private float lastPlayerResolveTime = -1f;
  readonly LocationPrefabData gameplayPlayerBootstrapPrefabData = new(assetPath: GameplayCoreAssetPaths.EsperanzaPrefabAssetPath);
  private CharacterState cachedPlayerCharacterState;
  private Spawner cachedSpawner;
  private GameplayDialogController cachedGameplayDialogController;
  private int pauseMenuOpenAppearanceRevision = -1;
  private bool holdBlackscreenOpaqueDuringLoad;
  private bool playerAnimationHeldForLoadingOverlay;
  private string lastPurgedLocationId = "";
  private string lastRuntimeSceneChangeKey = "";
  private float loadingStallStartedAt = -1f;
  private float loadingZeroPercentStartedAt = -1f;
  private float loadingZeroPercentNextLogAt = -1f;
  private Section settingsReturnTarget = Section.MainMenu;
  private Section currentSection = Section.None;
  private Section pendingRevealSection = Section.None;
  private bool dialogInputOverrideActive;
  private bool startupInDebugGameplay;
  private bool loadMenuStartupPrewarmed;
  private float lastGameplayRevealCompletedAt = -1f;
  private string startupDebugLocationId = "";
  private GameObject settingsCloseButton;
  private GameObject settingsHoveredTarget;
  private string activeInputMap = "";
  private string pendingGameplayLocationId = "";
  private string lastKnownGameplayLocationId = "";
  private string currentEnvironmentCacheLocationId = "";
  private string previousEnvironmentCacheLocationId = "";
  private bool gameplayReadyForSpawnsSentForLoad;
  private bool gameplayWarmGateStartedForLoad;
  private bool gameplayWarmGateCompletedForLoad;
  readonly List<string> preUnlockAddressScratch = new(4096);
  readonly HashSet<string> preUnlockSeenAddressScratch = new(StringComparer.OrdinalIgnoreCase);
  readonly List<string> preUnlockAtlasSiblingScratch = new(64);
  readonly List<AnimationController> preUnlockEnemyControllerScratch = new(32);
  readonly List<(float sqrDist, AnimationController controller)> preUnlockFilteredEnemyScratch = new(32);
  readonly List<(float sqrDist, EnemyController enemy)> warmGateCriticalEnemyScratch = new(32);
  readonly List<string> preUnlockResidentPinAddressScratch = new(16384);
  readonly List<string> preUnlockResidentPinReadyAddressScratch = new(16384);
  readonly HashSet<string> preUnlockResidentPinSeenAddressScratch = new(StringComparer.OrdinalIgnoreCase);
  readonly List<string> deferredPostRevealWarmupAddressScratch = new(4096);
  readonly HashSet<string> deferredPostRevealWarmupSeenAddressScratch = new(StringComparer.OrdinalIgnoreCase);
  readonly List<string> loadingTextRuntimeAddressScratch = new(4);
  readonly List<string> persistentAtlasAddressScratch = new(512);
  readonly HashSet<string> persistentAtlasSeenAddressScratch = new(StringComparer.OrdinalIgnoreCase);
  readonly List<string> environmentCacheLibraryScratch = new(256);
  readonly List<string> environmentCacheAddressScratch = new(4096);
  readonly List<string> environmentCacheAssetAddressScratch = new(64);
  readonly List<string> environmentCacheAssetLabelScratch = new(64);
  readonly Stack<Transform> findChildScratch = new(64);
  readonly Stack<IEnumerator> preUnlockEnumeratorStack = new(32);
  readonly List<string> warmRequestCriticalLibrariesScratch = new(64);
  readonly List<string> warmRequestCriticalAddressesScratch = new(512);
  readonly List<string> warmRequestWarmLibrariesScratch = new(128);
  readonly List<string> warmRequestWarmAddressesScratch = new(2048);
  readonly List<string> warmRequestCriticalLabelsScratch = new(64);
  readonly List<string> warmRequestWarmLabelsScratch = new(128);
  readonly List<string> warmRequestCriticalAssetAddressesScratch = new(64);
  readonly List<string> warmRequestWarmAssetAddressesScratch = new(128);
  readonly List<string> warmRequestCriticalAssetLabelsScratch = new(64);
  readonly List<string> warmRequestWarmAssetLabelsScratch = new(128);
  readonly List<string> combatPopulationTypesScratch = new(16);
  readonly List<string> combatPopulationProjectileKeysScratch = new(32);
  readonly HashSet<string> combatPopulationProjectileKeySeenScratch = new(StringComparer.OrdinalIgnoreCase);
  readonly List<string> criticalPlayerEffectKeysScratch = new(CorePlayerWarmAnimationKeys.Length);
  readonly List<string> playerBootstrapWarmAddressScratch = new(256);
  readonly HashSet<string> playerBootstrapWarmSeenAddressScratch = new(StringComparer.OrdinalIgnoreCase);
  readonly StringBuilder loadFlowLogBuilder = new(1024);
  MaterialPropertyBlock loadingBlackscreenPropertyBlock;
  EnemyController[] activeEnemyControllersCache = Array.Empty<EnemyController>();
  float activeEnemyControllersCacheRefreshedAt = -1f;
  GameplayInput cachedGameplayInput;
  float gameplayInputCacheRefreshedAt = -1f;
  int preUnlockLastPlayerAddressCount;
  SpriteWithNormals[] preUnlockVisibleSpriteTargetsCache = Array.Empty<SpriteWithNormals>();
  float preUnlockVisibleSpriteTargetsCacheRefreshedAt = -1f;
  const float ActiveEnemyControllersCacheRefreshSeconds = 0.2f;
  const float GameplayInputCacheRefreshSeconds = 0.2f;
  const long LocationPurgeAllThresholdBytes = 2L * 1024L * 1024L * 1024L;
  const string PreUnlockResidentPinOwnerId = "single_scene_manager.pre_unlock";
  const string LoadingTextFontPinOwnerId = "single_scene_manager.loading_text_font";
  const string PersistentFontAtlasPinOwnerId = "single_scene_manager.persistent_font_atlases";
  const string PersistentPlayerSkinAtlasPinOwnerId = "single_scene_manager.persistent_player_skin_atlases";
  const string PersistentPlayerEffectAtlasPinOwnerId = "single_scene_manager.persistent_player_effect_atlases";
  const string PersistentPlayerExpressionAtlasPinOwnerId = "single_scene_manager.persistent_player_expression_atlases";
  const string CurrentEnvironmentPinOwnerId = "single_scene_manager.environment_hot.current";
  const string PreviousEnvironmentPinOwnerId = "single_scene_manager.environment_hot.previous";
  const int EnvironmentHotCacheSlotCount = 2;
  const int PreUnlockResidentPinHardCapDesktop = 2048;
  const int PreUnlockResidentPinHardCapMobile = 1024;
  static string[] CorePlayerWarmAnimationKeys => GearController.CorePlayerWarmAnimationKeys;
  static readonly string[] PersistentFontAtlasNames = { "Hand", "Plate", "Walkway", "Vamp" };

  void Awake() {
    instance = this;
  }

  void OnDestroy() {
    if (ReferenceEquals(instance, this)) {
      instance = null;
    }
  }

  public static ProjectileManager ResolveGameplayProjectileManager() {
    if (instance == null) {
      instance = FindAnyObjectByType<SingleSceneManager>();
    }

    if (instance != null) {
      var resolved = instance.ResolveGameplayProjectileManagerInternal();
      if (resolved != null) {
        return resolved;
      }
    }

    return FindAnyObjectByType<ProjectileManager>();
  }

  public static GearController ResolveGameplayPlayerController() {
    if (instance == null) {
      instance = FindAnyObjectByType<SingleSceneManager>();
    }

    if (instance != null) {
      var resolved = instance.ResolvePlayerGearController();
      if (resolved != null) {
        return resolved;
      }
    }

    return FindAnyObjectByType<GearController>();
  }

  public static GameObject ResolveGameplayPlayerRoot() {
    if (instance == null) {
      instance = FindAnyObjectByType<SingleSceneManager>();
    }

    if (instance != null) {
      var resolved = instance.ResolveGameplayPlayerRootInternal();
      if (resolved != null) {
        return resolved;
      }
    }

    var fallbackGear = FindAnyObjectByType<GearController>();
    if (fallbackGear != null) {
      return fallbackGear.gameObject;
    }

    var fallbackCharacterState = FindAnyObjectByType<CharacterState>();
    return fallbackCharacterState != null ? fallbackCharacterState.gameObject : null;
  }

  public static CharacterState ResolveGameplayCharacterState() {
    if (instance == null) {
      instance = FindAnyObjectByType<SingleSceneManager>();
    }

    if (instance != null) {
      var resolved = instance.ResolvePlayerCharacterState();
      if (resolved != null) {
        return resolved;
      }
    }

    return FindAnyObjectByType<CharacterState>();
  }

  void Start() {
    RegisterMessageBusHandlers();
    InitializeLoadingScreenReferences();
    PrimePersistentFontAtlasPins("start");
    SetLoadingBlackscreenHold(true);
    ForceBlackscreenVisible(true);
    loadingOverlayChildrenReady = false;
    SetLoadingLightActive(false);
    SetLoadingProgressUiActive(false);
    SetLoadingText("");
    SetLoadingRootActive(true);
    ApplyConfiguredStartupMode();
    LogContentPackRuntimeSummary();
  }

  void OnValidate() {
    SyncDebugStartupLocationConfiguration();
  }

  void SyncDebugStartupLocationConfiguration() {
    var normalizedLocationId = LocationEnemyData.NormalizeLocationId(debugLocationId);
    if (!string.IsNullOrWhiteSpace(normalizedLocationId)) {
      if (!string.Equals(debugLocationId, normalizedLocationId, StringComparison.Ordinal)) {
        debugLocationId = normalizedLocationId;
      }
      return;
    }

    debugLocationId = "";
    if (!IsEditorStartupDebugEnabled()) {
      return;
    }

    if (debugLocationPrefab != null &&
        LocationEnemyData.TryGetLocationByPrefab(debugLocationPrefab, out var legacyLocationInfo) &&
        legacyLocationInfo != null) {
      var legacyLocationId = LocationEnemyData.NormalizeLocationId(legacyLocationInfo.id);
      if (IsGameplayLocation(legacyLocationId)) {
        debugLocationId = legacyLocationId;
        debugLocationPrefab = null;
        return;
      }
    }

    var fallbackLocationId = ResolveDefaultDebugLocationId();
    if (!string.IsNullOrWhiteSpace(fallbackLocationId)) {
      debugLocationId = fallbackLocationId;
    }

    debugLocationPrefab = null;
  }

  void LogContentPackRuntimeSummary() {
    if (!ShouldLogLoadFlowDebug()) {
      return;
    }

    var registry = ActiveContentRegistryRuntime.Registry;
    var activePackIds = registry != null && registry.ActivePackIds != null && registry.ActivePackIds.Count > 0
      ? string.Join(", ", registry.ActivePackIds)
      : "-";
    var playerPrefabPath = ResolveGameplayPlayerBootstrapAssetPath();

    Debug.Log(
      "[SingleSceneManager][ContentPack]" +
      " external_active=" + (ActiveContentRegistryRuntime.HasActiveExternalContent() ? 1 : 0) +
      " active_packs=" + activePackIds +
      " default_location='" + ResolveLoadFlowValue(ActiveContentRegistryRuntime.GetDefaultLocationId()) + "'" +
      " staged_texture_roots=" + (registry != null && registry.StagedTextureRoots != null ? registry.StagedTextureRoots.Count : 0) +
      " staged_sprite_library_roots=" + (registry != null && registry.StagedSpriteLibraryRoots != null ? registry.StagedSpriteLibraryRoots.Count : 0) +
      " player_prefab_path='" + ResolveLoadFlowValue(playerPrefabPath) + "'"
    );
  }

  void RegisterMessageBusHandlers() {
    actions.Add(MessageBus.On("startGame", o => StartGame()));
    actions.Add(MessageBus.On("openLoadMenu", o => OpenLoadMenu()));
    actions.Add(MessageBus.On("closeLoadMenu", o => CloseLoadMenu()));
    actions.Add(MessageBus.On("openSettingsMenu", o => OpenSettingsMenu()));
    actions.Add(MessageBus.On("backToMainMenu", o => OpenMainMenu()));
    actions.Add(MessageBus.On("settingsMenu.click", o => OnSettingsMenuClick(o)));
    actions.Add(MessageBus.On("settingsMenu.hover", o => OnSettingsMenuHover(o)));
    actions.Add(MessageBus.On("settingsMenu.unhover", o => OnSettingsMenuUnhover()));
    actions.Add(MessageBus.On("settingsMenu.select", o => { if (InputMessageValue.IsPressed(o)) OnSettingsMenuSelect(); }));
    actions.Add(MessageBus.On("settingsMenu.cancel", o => { if (InputMessageValue.IsPressed(o)) CloseSettingsMenu(); }));

    actions.Add(MessageBus.On("closePauseMenu", o => ClosePauseMenu()));
    actions.Add(MessageBus.On("openPauseMenu", o => OpenPauseMenu()));
    actions.Add(MessageBus.On("LocationUpdated", o => OnLocationUpdated(o)));
    actions.Add(MessageBus.On("runtimeSceneChanged", o => OnRuntimeSceneChanged(o)));
    actions.Add(MessageBus.On("dialog.started", o => OnDialogStarted(o)));
    actions.Add(MessageBus.On("dialog.finished", o => OnDialogFinished(o)));
    actions.Add(MessageBus.On("gearReady", o => RefreshPersistentPlayerBaselineAtlasPins("gear_ready")));
    actions.Add(MessageBus.On("enemy.defeated", o => OnEnemyDefeatedForEpisodeProgress(o)));
    actions.Add(MessageBus.On("episode.advance", o => AdvanceEpisodeSlice("message")));
  }

  void Update() {
    UpdateLoadingScreenFeedback();

    if (!init) {
      if (ShouldRunStartupGameplayWarmFlow()) {
        pendingRevealSection = Section.Gameplay;
        PrepareLoadingScreenCarrier();
        SetLoadingBlackscreenHold(true);
        if (startupGameplayRoutine == null) {
          startupGameplayRoutine = StartCoroutine(StartupGameplayFlowRoutine());
        }
        init = true;
        return;
      }
      if (startupMainMenuRevealRoutine == null) {
        startupMainMenuRevealRoutine = StartCoroutine(StartupMainMenuRevealRoutine());
      }
      init = true;
      return;
    }

    HandleDebugMainMenuShortcut();
  }
}
