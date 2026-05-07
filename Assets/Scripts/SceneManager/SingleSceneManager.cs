
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using CustomInspector;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public class SingleSceneManager : MonoBehaviour {
  static int nextPauseDialogResumeToken = 1;
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
  private int loadingBlockingReadyCount;
  private int loadingBlockingTotalCount;
  private bool loadingBlockingCriticalReady;
  private bool loadingBlockingHardBypassUsed;
  private bool loadingBlockingStateKnown;
  private OptimalGameplayLoadingStage gameplayLoadingStageForLoad;
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
  const float LoadingHeartbeatLogIntervalSeconds = 1.0f;
  const float LoadingHeartbeatAcceptableGapSeconds = 2.0f;
  const float GameplayWarmGateEnemyArchetypeWaitSeconds = 0.5f;
  const float GameplayWarmGatePrereqLogIntervalSeconds = 1.0f;
  const int RevealOpaqueSettleFrames = 2;
  const float RevealOpaqueSettleTimeoutSeconds = 3.0f;
  const float RevealOpaqueSettleMinimumStableSeconds = 0.25f;

  public static int CurrentPauseDialogSuspendToken => activePauseDialogResumeToken;

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
  const float startWarmTimeoutSeconds = 2.0f;
  const float startWarmHardTimeoutSeconds = 25.0f;
  const float startWarmRequiredRatio = 0.97f;
  const float loadSaveWarmRequiredRatioCap = 0.72f;
  const float loadSaveWarmRequiredRatioFloor = 0.62f;
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
  const float fadeLeadSeconds = 3.0f;
  const float fallbackTransitionSeconds = 3.0f;
  const float fadeToBlackSeconds = 2.0f;
  const float fadeFromBlackSeconds = 2.0f;
  const float loadingCircleSpinSpeedDegreesPerSecond = 360f;
  static readonly bool waitForStreamingIdleBeforeFadeOut = true;
  const float streamingIdleMinimumWaitSeconds = 3.0f;
  const float streamingIdleTimeoutSeconds = 20.0f;
  const bool allowStreamingIdleTimeoutBypass = false;
  const int streamingIdleStableFrames = 2;
  const int streamingIdleAllowedQueued = 0;
  const int streamingIdleAllowedInFlight = 0;
  const int streamingBlockingReadyMaxOutstandingDesktop = 384;
  const int streamingBlockingReadyMaxOutstandingMobile = 256;
  const int streamingBlockingReadyMaxInFlightDesktop = 48;
  const int streamingBlockingReadyMaxInFlightMobile = 32;
  const bool enforceZeroOutstandingBeforeUnlock = true;
  const float loadingPercentRisePerSecond = 55f;
  const bool enablePreUnlockVisibleSpritePrefetch = true;
  // Increase this if animations are choppy immediately after unlock (e.g. to 12 or 24).
  const int preUnlockPrefetchAnimationFrames = 6;
  const int preUnlockPrefetchLookAheadFrames = 6;
  const int preUnlockPrefetchMaxAddresses = 12288;
  const int preUnlockPrefetchMinAddresses = 512;
  const int preUnlockPrefetchEnqueueBudgetPerFrame = 200;
  const int preUnlockPrefetchFrameJumpClamp = 4;
  const float preUnlockTargetCacheRefreshSeconds = 0.5f;
  const bool preUnlockPrefetchExpandAtlasSiblings = true;
  const int preUnlockPrefetchMaxAtlasSiblingsPerSeed = 24;
  const bool preUnlockPrefetchIncludeUiTargets = false;
  const bool enablePreUnlockControllerAnimationPrefetch = true;
  const int preUnlockPlayerAnimationStarts = 64;
  const int preUnlockEnemyAnimationStartsPerController = 24;
  const bool enablePreUnlockAnimationPlaybackWarmup = true;
  const int preUnlockAnimationPlaybackPasses = 2;
  const int preUnlockAnimationFramePreloadPasses = 3;
  const bool preUnlockReprefetchVisibleSpritesAfterAnimationWarmup = true;
  const float preUnlockWarmupQueueSettleTimeoutSeconds = 2.0f;
  const float preUnlockBlockingBudgetSeconds = 1.25f;
  static readonly bool enablePreUnlockResidentPinning = true;
  const int preUnlockResidentPinMaxAddresses = 2048;
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
  readonly LocationPrefabData gameplayPlayerBootstrapPrefabData = new(assetPath: GameplayCoreAssetPaths.EsperanzaPrefabAssetPath);
  private CharacterState cachedPlayerCharacterState;
  private Spawner cachedSpawner;
  private GameplayDialogController cachedGameplayDialogController;
  private int pauseMenuOpenAppearanceRevision = -1;
  private bool holdBlackscreenOpaqueDuringLoad;
  private bool playerAnimationHeldForLoadingOverlay;
  private string lastPurgedLocationId = "";
  private float loadingStallStartedAt = -1f;
  private float loadingZeroPercentStartedAt = -1f;
  private float loadingZeroPercentNextLogAt = -1f;
  private Section settingsReturnTarget = Section.MainMenu;
  private Section currentSection = Section.None;
  private Section pendingRevealSection = Section.None;
  private bool dialogInputOverrideActive;
  private bool startupInDebugGameplay;
  private bool loadMenuStartupPrewarmed;
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
  static readonly string[] CorePlayerWarmAnimationKeys = { "Blast" };
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
    ForceBlackscreenVisible(true);
    loadingOverlayChildrenReady = false;
    SetLoadingLightActive(false);
    SetLoadingProgressUiActive(false);
    SetLoadingText("");
    SetLoadingRootActive(false);
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
    actions.Add(MessageBus.On("dialog.started", o => OnDialogStarted(o)));
    actions.Add(MessageBus.On("dialog.finished", o => OnDialogFinished(o)));
    actions.Add(MessageBus.On("gearReady", o => RefreshPersistentPlayerBaselineAtlasPins("gear_ready")));
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

  IEnumerator StartupMainMenuRevealRoutine() {
    PrepareLoadingScreenCarrier();
    SetLoadingBlackscreenHold(true);
    ForceBlackscreenVisible(true);
    QueueMenuRuntimeAssetWarmup("startup_main_menu", includeLocationProfile: false);
    yield return PrewarmLoadMenuForStartupRoutine();
    BeginStartupMainMenuReveal();
    startupMainMenuRevealRoutine = null;
  }

  IEnumerator PrewarmLoadMenuForStartupRoutine() {
    if (loadMenuStartupPrewarmed) yield break;

    if (LoadMenu == null) {
      loadMenuStartupPrewarmed = true;
      yield break;
    }

    var restoreSection = ResolveCurrentSection();
    if (restoreSection == Section.None) {
      restoreSection = Section.MainMenu;
    }

    var startedAt = Time.realtimeSinceStartup;
    QueueMenuRuntimeAssetWarmup("startup_load_menu_prewarm", includeLocationProfile: false);
    if (ShouldLogLoadFlowDebug()) {
      Debug.Log(
        "[SingleSceneManager][LoadMenuPrewarm] stage=begin" +
        " restore_section=" + restoreSection +
        " saves=" + (saveSlotView != null ? saveSlotView.SavesCount : -1) +
        " active_input_map=" + activeInputMap
      );
    }

    _SwitchMap("none");
    ApplySectionActivation(Section.LoadMenu);

    for (var i = 0; i < startupLoadMenuPrewarmFrames; i++) {
      yield return null;
    }

    ApplySectionActivation(restoreSection);
    ApplyInputForSection(restoreSection);
    loadMenuStartupPrewarmed = true;

    if (!ShouldLogLoadFlowDebug()) yield break;

    Debug.Log(
      "[SingleSceneManager][LoadMenuPrewarm] stage=complete" +
      " restore_section=" + restoreSection +
      " saves=" + (saveSlotView != null ? saveSlotView.SavesCount : -1) +
      " warm_frames=" + startupLoadMenuPrewarmFrames +
      " elapsed_ms=" + ((Time.realtimeSinceStartup - startedAt) * 1000f).ToString("0.0")
    );
  }

  void QueueMenuRuntimeAssetWarmup(string source, bool includeLocationProfile = true) {
    var globalLabelCount = CountWarmSources(SpriteStreamingRuntimeSettings.WarmUiRuntimeAssetLabels);
    if (globalLabelCount > 0) {
      RuntimeAssetCache.QueueWarmup(
        addresses: null,
        labels: SpriteStreamingRuntimeSettings.WarmUiRuntimeAssetLabels,
        scope: RuntimeAssetResidencyScope.GlobalUi,
        reason: source + ":global_ui"
      );
    }
  }

  static int CountWarmSources(IEnumerable<string> values) {
    if (values == null) return 0;
    var count = 0;
    foreach (var value in values) {
      if (!string.IsNullOrWhiteSpace(value)) count++;
    }
    return count;
  }

  void AddPersistentAtlasAddress(string address) {
    var normalized = string.IsNullOrWhiteSpace(address) ? "" : address.Trim();
    if (string.IsNullOrWhiteSpace(normalized)) {
      return;
    }

    if (!persistentAtlasSeenAddressScratch.Add(normalized)) {
      return;
    }

    persistentAtlasAddressScratch.Add(normalized);
  }

  void QueuePersistentAtlasMetadataWarmup(IList<string> addresses) {
    if (addresses == null || addresses.Count <= 0) {
      return;
    }

    for (var i = 0; i < addresses.Count; i++) {
      TrimmedSpriteOffsetResolver.RegisterWarmupMetadataCandidate(addresses[i]);
    }

    TrimmedSpriteOffsetResolver.QueueWarmupAtlasMetadataBatch(addresses, 0, addresses.Count);
  }

  void PrimePersistentFontAtlasPins(string source) {
    persistentAtlasAddressScratch.Clear();
    persistentAtlasSeenAddressScratch.Clear();

    for (var i = 0; i < PersistentFontAtlasNames.Length; i++) {
      var fontName = PersistentFontAtlasNames[i];
      if (string.IsNullOrWhiteSpace(fontName)) continue;
      AddPersistentAtlasAddress(ResolveFontAtlasAddress(fontName));
    }

    if (persistentAtlasAddressScratch.Count <= 0) {
      persistentAtlasSeenAddressScratch.Clear();
      return;
    }

    TextureResidencyCache.UpdateOwnerPins(
      PersistentFontAtlasPinOwnerId,
      TextureResidencyCache.PinClass.UI,
      persistentAtlasAddressScratch,
      TextureResidencyCache.LoadPriority.Warmup
    );
    QueuePersistentAtlasMetadataWarmup(persistentAtlasAddressScratch);

    if (ShouldLogLoadingProgressDebug()) {
      Debug.Log(
        "[SingleSceneManager][PersistentAtlasPins] source='" + (source ?? "") + "'" +
        " class=ui_fonts" +
        " addresses=" + persistentAtlasAddressScratch.Count
      );
    }

    persistentAtlasAddressScratch.Clear();
    persistentAtlasSeenAddressScratch.Clear();
  }

  void RefreshPersistentPlayerSkinAtlasPins(string source) {
    var player = ResolvePlayerGearController();
    if (player == null) {
      return;
    }

    persistentAtlasAddressScratch.Clear();
    persistentAtlasSeenAddressScratch.Clear();
    var maxPinnedAddresses = Math.Max(SpriteStreamingRuntimeSettings.PinBudgetPlayerAddresses, 1);
    var collectedCount = player.CollectPersistentSkinStartupAddresses(
      persistentAtlasAddressScratch,
      persistentAtlasSeenAddressScratch,
      maxPinnedAddresses
    );
    if (collectedCount <= 0 || persistentAtlasAddressScratch.Count <= 0) {
      persistentAtlasAddressScratch.Clear();
      persistentAtlasSeenAddressScratch.Clear();
      return;
    }

    TextureResidencyCache.UpdateOwnerPins(
      PersistentPlayerSkinAtlasPinOwnerId,
      TextureResidencyCache.PinClass.Player,
      persistentAtlasAddressScratch,
      TextureResidencyCache.LoadPriority.Warmup
    );
    QueuePersistentAtlasMetadataWarmup(persistentAtlasAddressScratch);

    if (ShouldLogLoadingProgressDebug()) {
      Debug.Log(
        "[SingleSceneManager][PersistentAtlasPins] source='" + (source ?? "") + "'" +
        " class=player_skin" +
        " addresses=" + persistentAtlasAddressScratch.Count +
        " active_form='" + EsperanzaForms.GetActive() + "'" +
        " player='" + player.gameObject.name + "'"
      );
    }

    persistentAtlasAddressScratch.Clear();
    persistentAtlasSeenAddressScratch.Clear();
  }

  void RefreshPersistentPlayerEffectAtlasPins(string source) {
    var player = ResolvePlayerGearController();
    if (player == null) {
      return;
    }

    persistentAtlasAddressScratch.Clear();
    persistentAtlasSeenAddressScratch.Clear();
    var maxPinnedAddresses = Math.Max(SpriteStreamingRuntimeSettings.PinBudgetPlayerAddresses, 1);
    var collectedCount = player.CollectPersistentEffectStartupAddresses(
      persistentAtlasAddressScratch,
      CorePlayerWarmAnimationKeys,
      persistentAtlasSeenAddressScratch,
      maxPinnedAddresses
    );
    if (collectedCount <= 0 || persistentAtlasAddressScratch.Count <= 0) {
      persistentAtlasAddressScratch.Clear();
      persistentAtlasSeenAddressScratch.Clear();
      return;
    }

    TextureResidencyCache.UpdateOwnerPins(
      PersistentPlayerEffectAtlasPinOwnerId,
      TextureResidencyCache.PinClass.Effect,
      persistentAtlasAddressScratch,
      TextureResidencyCache.LoadPriority.Warmup
    );
    QueuePersistentAtlasMetadataWarmup(persistentAtlasAddressScratch);

      if (ShouldLogLoadingProgressDebug()) {
        Debug.Log(
          "[SingleSceneManager][PersistentAtlasPins] source='" + (source ?? "") + "'" +
          " class=player_effects" +
          " addresses=" + persistentAtlasAddressScratch.Count +
          " warm_animations=" + CorePlayerWarmAnimationKeys.Length +
          " projectile_manager=" + (player.projectileManager != null ? 1 : 0) +
          " player='" + player.gameObject.name + "'"
        );
      }

    persistentAtlasAddressScratch.Clear();
    persistentAtlasSeenAddressScratch.Clear();
  }

  void RefreshPersistentPlayerExpressionAtlasPins(string source) {
    persistentAtlasAddressScratch.Clear();
    persistentAtlasSeenAddressScratch.Clear();

    AddPersistentAtlasAddress(ResolveEsperanzaExpressionAtlasAddress(".png"));
    AddPersistentAtlasAddress(ResolveEsperanzaExpressionAtlasAddress(".jpg"));
    if (persistentAtlasAddressScratch.Count <= 0) {
      persistentAtlasAddressScratch.Clear();
      persistentAtlasSeenAddressScratch.Clear();
      return;
    }

    TextureResidencyCache.UpdateOwnerPins(
      PersistentPlayerExpressionAtlasPinOwnerId,
      TextureResidencyCache.PinClass.UI,
      persistentAtlasAddressScratch,
      TextureResidencyCache.LoadPriority.Warmup
    );
    QueuePersistentAtlasMetadataWarmup(persistentAtlasAddressScratch);

    if (ShouldLogLoadingProgressDebug()) {
      Debug.Log(
        "[SingleSceneManager][PersistentAtlasPins] source='" + (source ?? "") + "'" +
        " class=player_expressions" +
        " addresses=" + persistentAtlasAddressScratch.Count
      );
    }

    persistentAtlasAddressScratch.Clear();
    persistentAtlasSeenAddressScratch.Clear();
  }

  void RefreshPersistentPlayerBaselineAtlasPins(string source) {
    RefreshPersistentPlayerSkinAtlasPins(source);
    RefreshPersistentPlayerEffectAtlasPins(source);
    RefreshPersistentPlayerExpressionAtlasPins(source);
  }

  int ResolveEnvironmentHotCacheAddressBudget() {
    var ownerCap = Math.Max(SpriteStreamingRuntimeSettings.MaxPinnedAddressesPerOwner, 128);
    return Math.Max(Math.Min(ownerCap, 4096), 128);
  }

  void CollectEnvironmentHotCacheSources(string locationId) {
    // Environment hot cache removed per performance goal.
  }

  void ApplyEnvironmentHotCacheSlot(string ownerId, string slotName, string locationId, string source) {
    if (!IsGameplayLocation(locationId)) {
      TextureResidencyCache.ReleaseOwnerPins(ownerId);
      return;
    }

    CollectEnvironmentHotCacheSources(locationId);
    if (environmentCacheLibraryScratch.Count > 0) {
      SpriteRuntimeResolver.WarmupLibraries(environmentCacheLibraryScratch);
    }

    if (environmentCacheAddressScratch.Count > 0) {
      TextureResidencyCache.UpdateOwnerPins(
        ownerId,
        TextureResidencyCache.PinClass.WarmGate,
        environmentCacheAddressScratch,
        TextureResidencyCache.LoadPriority.Warmup
      );
      QueuePersistentAtlasMetadataWarmup(environmentCacheAddressScratch);
    }
    else {
      TextureResidencyCache.ReleaseOwnerPins(ownerId);
    }

    if (!ShouldLogLoadingProgressDebug()) return;

    Debug.Log(
      "[SingleSceneManager][EnvironmentCache] stage=apply_slot" +
      " source='" + (source ?? "") + "'" +
      " slot=" + ResolveLoadFlowValue(slotName) +
      " location=" + ResolveLoadFlowValue(locationId) +
      " libraries=" + environmentCacheLibraryScratch.Count +
      " addresses=" + environmentCacheAddressScratch.Count +
      " asset_addresses=" + environmentCacheAssetAddressScratch.Count +
      " asset_labels=" + environmentCacheAssetLabelScratch.Count +
      " slot_budget=" + ResolveEnvironmentHotCacheAddressBudget()
    );
  }

  void RefreshEnvironmentHotCacheSlots(string source) {
    ApplyEnvironmentHotCacheSlot(
      CurrentEnvironmentPinOwnerId,
      "current",
      currentEnvironmentCacheLocationId,
      source
    );
    ApplyEnvironmentHotCacheSlot(
      PreviousEnvironmentPinOwnerId,
      "previous",
      previousEnvironmentCacheLocationId,
      source
    );

    if (!ShouldLogLoadingProgressDebug()) return;

    Debug.Log(
      "[SingleSceneManager][EnvironmentCache] stage=refresh_slots" +
      " source='" + (source ?? "") + "'" +
      " current=" + ResolveLoadFlowValue(currentEnvironmentCacheLocationId) +
      " previous=" + ResolveLoadFlowValue(previousEnvironmentCacheLocationId) +
      " slots=" + EnvironmentHotCacheSlotCount
    );
  }

  void TrackEnvironmentHotCacheLocation(string locationId, string source) {
    var normalized = LocationEnemyData.NormalizeLocationId(locationId);
    if (!IsGameplayLocation(normalized)) {
      return;
    }

    if (string.Equals(currentEnvironmentCacheLocationId, normalized, StringComparison.OrdinalIgnoreCase)) {
      RefreshEnvironmentHotCacheSlots(source + "_refresh");
      return;
    }

    if (string.Equals(previousEnvironmentCacheLocationId, normalized, StringComparison.OrdinalIgnoreCase)) {
      var displacedCurrent = currentEnvironmentCacheLocationId;
      currentEnvironmentCacheLocationId = normalized;
      previousEnvironmentCacheLocationId = displacedCurrent;
      RefreshEnvironmentHotCacheSlots(source + "_promote_previous");
      return;
    }

    previousEnvironmentCacheLocationId = currentEnvironmentCacheLocationId;
    currentEnvironmentCacheLocationId = normalized;
    RefreshEnvironmentHotCacheSlots(source + "_rotate");
  }

  string ResolveLoadingTextFontAtlasAddress() {
    if (loadingText == null) {
      return "";
    }

    var fontName = string.IsNullOrWhiteSpace(loadingText.font)
      ? ""
      : loadingText.font.Trim();
    if (string.IsNullOrWhiteSpace(fontName)) {
      return "";
    }

    return ResolveFontAtlasAddress(fontName);
  }

  static string ResolveFontAtlasAddress(string fontName) {
    var normalizedFontName = string.IsNullOrWhiteSpace(fontName) ? "" : fontName.Trim();
    if (string.IsNullOrWhiteSpace(normalizedFontName)) {
      return "";
    }

    var sourceAssetPath = "Assets/Sprites/Fonts/" + normalizedFontName + "/atlas.png";
    return ActiveContentRegistryRuntime.ResolveCoreAssetPath(sourceAssetPath);
  }

  static string ResolveEsperanzaExpressionAtlasAddress(string extension) {
    var normalizedExtension = string.IsNullOrWhiteSpace(extension) ? "" : extension.Trim();
    if (string.IsNullOrWhiteSpace(normalizedExtension)) {
      return "";
    }

    var sourceAssetPath = "Assets/Sprites/Characters/Esperanza/Expressions/Base/atlas" + normalizedExtension;
    return ActiveContentRegistryRuntime.ResolveCoreAssetPath(sourceAssetPath);
  }

  void PrimeLoadingTextRuntimeAssets(string source) {
    var atlasAddress = ResolveLoadingTextFontAtlasAddress();
    if (string.IsNullOrWhiteSpace(atlasAddress)) {
      return;
    }

    loadingTextRuntimeAddressScratch.Clear();
    loadingTextRuntimeAddressScratch.Add(atlasAddress);
    TextureResidencyCache.UpdateOwnerPins(
      LoadingTextFontPinOwnerId,
      TextureResidencyCache.PinClass.UI,
      loadingTextRuntimeAddressScratch,
      TextureResidencyCache.LoadPriority.Warmup
    );
    TrimmedSpriteOffsetResolver.RegisterWarmupMetadataCandidate(atlasAddress);
    TrimmedSpriteOffsetResolver.QueueWarmupAtlasMetadataBatch(
      loadingTextRuntimeAddressScratch,
      0,
      loadingTextRuntimeAddressScratch.Count
    );

    if (ShouldLogLoadingProgressDebug()) {
      Debug.Log(
        "[SingleSceneManager][LoadingTextWarmup] source='" + (source ?? "") + "'" +
        " font='" + (loadingText != null ? loadingText.font : "") + "'" +
        " atlas='" + atlasAddress + "'"
      );
    }

    loadingTextRuntimeAddressScratch.Clear();
  }

  void BeginStartupMainMenuReveal() {
    PrepareLoadingScreenCarrier();
    SetLoadingBlackscreenHold(false);
    ForceBlackscreenVisible(true);
    LogSectionTransitionState("startup_reveal_begin", Section.None, ResolveCurrentSection(), "MainMenuStartup", false);
    if (blackscreen != null) {
      PlayBlackscreen("alphaOut");
    }
    else {
      // Startup must never remain black if animator wiring is missing.
      SetLoadingBlackscreenHold(false);
      ForceBlackscreenVisible(false);
    }
    if (startupFadeWatchdogRoutine != null) {
      StopCoroutine(startupFadeWatchdogRoutine);
    }
    startupFadeWatchdogRoutine = StartCoroutine(StartupFadeWatchdogRoutine());
  }

  void InitializeLoadingScreenReferences() {
    loadingBlackscreen = null;
    loadingBlackscreenRenderer = null;
    blackscreen = null;
    loadingLightObject = null;
    loadingCircle = null;
    loadingTextObject = null;
    loadingText = null;
    loadingUiFeedbackActive = false;
    loadingOverlayChildrenReady = false;
    loadingPercent = -1;
    loadingStatusDetail = "";
    loadingStatusOverride = "";
    loadingPercentDisplayInitialized = false;
    loadingProgressUiArmCheckFrame = -1;
    loadingPercentDisplayValue = 0f;
    loadingProgressPeakOutstanding = -1;
    loadingProgressGoalTotal = -1;
    loadingProgressGoalBestRemaining = int.MaxValue;
    loadingProgressIdleStartedAt = -1f;
    loadingProgressObservedWork = false;
    loadingProgressNextDebugLogAt = -1f;
    ResetLoadingHeartbeatDebugState();
    ResetBlockingProgressState();

    if (LoadingScreen == null) {
      return;
    }

    var blackscreenTransform = FindChildByName(LoadingScreen.transform, "blackscreen");
    if (blackscreenTransform != null) {
      loadingBlackscreen = blackscreenTransform.gameObject;
      loadingBlackscreenRenderer = loadingBlackscreen.GetComponent<SpriteRenderer>();
      blackscreen = loadingBlackscreen.GetComponent<All1AnimatorScript>();
      if (blackscreen != null) {
        blackscreen.AddFloatAnim("alphaIn", "_Alpha", 0f, 1f, Mathf.Max(fadeToBlackSeconds, 0f));
        blackscreen.AddFloatAnim("alphaOut", "_Alpha", 1f, 0f, Mathf.Max(fadeFromBlackSeconds, 0f));
      }
    }

    var circleTransform = FindChildByName(LoadingScreen.transform, "circle");
    if (circleTransform != null) {
      loadingCircle = circleTransform.gameObject;
    }

    var lightTransform = FindChildByName(LoadingScreen.transform, "light");
    if (lightTransform != null) {
      loadingLightObject = lightTransform.gameObject;
    }

    var textTransform = FindChildByName(LoadingScreen.transform, "text");
    if (textTransform != null) {
      loadingTextObject = textTransform.gameObject;
      loadingText = textTransform.GetComponent<FontText>();
    }

    PrimeLoadingTextRuntimeAssets("initialize_loading_screen_references");
  }

  void UpdateLoadingScreenFeedback() {
    if (!PrepareLoadingScreenFeedbackState()) return;
    MaybeLogLoadingScreenHeartbeat();
    MaybeWarnLoadingProgressUiNotArmed();
    if (!loadingOverlayChildrenReady) return;
    var percent = CalculateLoadingPercentFromTextureQueue(out var statusDetail);
    UpdateLoadingScreenText(percent, statusDetail);
  }

  bool PrepareLoadingScreenFeedbackState() {
    var loadingActive = holdBlackscreenOpaqueDuringLoad || IsLoadingFlowActive();
    if (!loadingActive) {
      if (!loadingUiFeedbackActive &&
          !loadingOverlayChildrenReady &&
          loadingPercent < 0 &&
          !loadingPercentDisplayInitialized &&
          loadingHeartbeatStartedAt < 0f &&
          !loadingProgressObservedWork &&
          loadingProgressGoalTotal < 0 &&
          loadingProgressPeakOutstanding < 0 &&
          loadingProgressNextDebugLogAt < 0f &&
          !loadingBlockingStateKnown) {
        return false;
      }
      loadingUiFeedbackActive = false;
      loadingOverlayChildrenReady = false;
      loadingPercent = -1;
      loadingStatusDetail = "";
      loadingStatusOverride = "";
      loadingPercentDisplayInitialized = false;
      loadingProgressUiArmCheckFrame = -1;
      loadingPercentDisplayValue = 0f;
      loadingProgressPeakOutstanding = -1;
      loadingProgressGoalTotal = -1;
      loadingProgressGoalBestRemaining = int.MaxValue;
      loadingProgressIdleStartedAt = -1f;
      loadingProgressObservedWork = false;
      loadingProgressNextDebugLogAt = -1f;
      ResetLoadingHeartbeatDebugState();
      ResetBlockingProgressState();
      ResetGameplayLoadStageTracking();
      ResetZeroPercentStallDebugState();
      SetLoadingLightActive(false);
      SetLoadingProgressUiActive(false);
      SetLoadingText("");
      return false;
    }

    if (LoadingScreen != null && !LoadingScreen.activeSelf) {
      SetLoadingRootActive(true);
    }

    SetLoadingProgressUiActive(loadingOverlayChildrenReady);
    loadingUiFeedbackActive = true;

    if (loadingOverlayChildrenReady) {
      RotateLoadingCircle();
    }

    return true;
  }

  void ResetGameplayLoadStageTracking() {
    gameplayReadyForSpawnsSentForLoad = false;
    gameplayWarmGateStartedForLoad = false;
    gameplayWarmGateCompletedForLoad = false;
    gameplayLoadingStageForLoad = OptimalGameplayLoadingStage.Player;
  }

  bool IsGameplayLoadPipelineTrackingActive() {
    if (startGameRoutine != null ||
        resumeGameplayRoutine != null ||
        startupGameplayRoutine != null ||
        pendingRevealSection == Section.Gameplay) {
      return true;
    }

    if (!gameplayWarmGateStartedForLoad && !gameplayWarmGateCompletedForLoad) {
      return false;
    }

    var overlayReason = SpriteStreamingLoadingState.ActiveReason;
    return string.Equals(overlayReason, "StartGameFlow", StringComparison.Ordinal) ||
           string.Equals(overlayReason, "LoadGameFlow", StringComparison.Ordinal) ||
           string.Equals(overlayReason, "ResumeGameplayFlow", StringComparison.Ordinal) ||
           string.Equals(overlayReason, "StartupGameplayFlow", StringComparison.Ordinal) ||
           string.Equals(overlayReason, BuildSectionOverlayTag(Section.Gameplay), StringComparison.Ordinal);
  }

  bool IsGameplayPlayerBootstrapReady() {
    var player = ResolvePlayerGearController();
    return player != null &&
           player.gameObject != null &&
           player.gameObject.scene.IsValid() &&
           player.gameObject.activeInHierarchy;
  }

  GameplayDialogController ResolveGameplayDialogController() {
    if (cachedGameplayDialogController != null) {
      if (cachedGameplayDialogController.gameObject != null) {
        return cachedGameplayDialogController;
      }
      cachedGameplayDialogController = null;
    }

    if (GameplayInterface == null) {
      return null;
    }

    cachedGameplayDialogController = GameplayInterface.GetComponentInChildren<GameplayDialogController>(true);
    return cachedGameplayDialogController;
  }

  static bool ShouldExpectEnemyWarmStageForCurrentLocation() {
    return LocationEnemyData.TryGetLocation(LocationManager.currentLocation, out var locationInfo) &&
           locationInfo != null &&
           locationInfo.enemies != null &&
           locationInfo.enemies.Count > 0;
  }

  int ResolveCurrentLocationLoadingArchetypeCount() {
    var spawner = ResolveGameplaySpawner();
    return spawner != null ? spawner.GetCurrentLocationArchetypeWarmupCount() : 0;
  }

  bool IsGameplayUiReadyForLoadingProgress() {
    if (GameplayInterface == null || !GameplayInterface.activeInHierarchy) {
      return false;
    }

    var dialogController = ResolveGameplayDialogController();
    return dialogController != null && dialogController.HasResolvedUiReferencesForLoadingProgress;
  }

  bool IsGameplayDialogReadyForLoadingProgress() {
    var dialogController = ResolveGameplayDialogController();
    return dialogController != null && dialogController.IsReadyForLoadingProgress;
  }

  static float ResolveStagedLoadingProgress(float start, float end, float stageProgress) {
    return Mathf.Lerp(start, end, Mathf.Clamp01(stageProgress));
  }

  float ResolveGameplayPlayerStageProgress() {
    var player = ResolvePlayerGearController();
    if (player == null || player.gameObject == null) {
      return 0f;
    }

    if (!player.gameObject.scene.IsValid()) {
      return 0.45f;
    }

    if (!player.gameObject.activeInHierarchy) {
      return 0.8f;
    }

    if (!TryMeasurePlayerFirstFrameReadiness(out var readyCount, out var totalCount) || totalCount <= 0) {
      return IsPlayerFirstFrameReady() ? 1f : 0.86f;
    }

    var readiness = Mathf.Clamp01((float)readyCount / totalCount);
    return Mathf.Lerp(0.82f, 1f, readiness);
  }

  bool TryMeasurePlayerFirstFrameReadiness(out int readyCount, out int totalCount) {
    readyCount = 0;
    totalCount = 0;
    var player = ResolvePlayerGearController();
    if (player == null) return false;

    MeasureSpriteGroupFirstFrameReadiness(player.SkinObjects, ref readyCount, ref totalCount);
    MeasureSpriteGroupFirstFrameReadiness(player.GearObjects, ref readyCount, ref totalCount);
    return totalCount > 0;
  }

  void MeasureSpriteGroupFirstFrameReadiness(GameObject[] objects, ref int readyCount, ref int totalCount) {
    if (objects == null || objects.Length <= 0) return;

    for (var i = 0; i < objects.Length; i++) {
      var go = objects[i];
      if (go == null || !go.activeInHierarchy) continue;
      var sprite = go.GetComponent<SpriteWithNormals>();
      if (sprite == null || !sprite.enabled || sprite.DoNotRender) continue;
      var frame = ResolveSpriteReadinessFrame(sprite);
      totalCount++;
      if (sprite.IsFrameReady(frame, out _)) {
        readyCount++;
      }
    }
  }

  float ResolveGameplayLocationStageProgress(bool locationReady) {
    if (locationReady) {
      return 1f;
    }

    if (string.IsNullOrWhiteSpace(LocationManager.currentLocation)) {
      return 0.1f;
    }

    return 0.55f;
  }

  float ResolveGameplayUiStageProgress(GameplayDialogController dialogController, bool uiReady, bool locationDeferredPending) {
    if (uiReady) {
      return 1f;
    }

    var progress = 0f;
    var gameplayUiActive = GameplayInterface != null && GameplayInterface.activeInHierarchy;
    if (GameplayInterface != null) {
      progress = 0.2f;
    }

    if (gameplayUiActive) {
      progress = 0.55f;
    }

    if (dialogController != null && gameplayUiActive) {
      progress = 0.8f;
      if (dialogController.HasResolvedUiReferencesForLoadingProgress && !locationDeferredPending) {
        progress = 0.95f;
      }
    }

    return progress;
  }

  float ResolveGameplayDialogStageProgress(GameplayDialogController dialogController, bool dialogReady) {
    if (dialogReady) {
      return 1f;
    }

    if (dialogController == null) {
      return 0f;
    }

    if (!dialogController.isActiveAndEnabled) {
      return 0.35f;
    }

    return dialogController.HasResolvedUiReferencesForLoadingProgress ? 0.75f : 0.55f;
  }

  void AdvanceOptimalGameplayLoadingStageForLoad(
    bool playerReady,
    bool locationReady,
    bool enemiesReady,
    bool uiReady,
    bool dialogReady
  ) {
    var previousStage = gameplayLoadingStageForLoad;

    if (gameplayLoadingStageForLoad <= OptimalGameplayLoadingStage.Player && playerReady) {
      gameplayLoadingStageForLoad = OptimalGameplayLoadingStage.Location;
    }
    if (gameplayLoadingStageForLoad <= OptimalGameplayLoadingStage.Location && locationReady) {
      gameplayLoadingStageForLoad = OptimalGameplayLoadingStage.Enemies;
    }
    if (gameplayLoadingStageForLoad <= OptimalGameplayLoadingStage.Enemies && enemiesReady) {
      gameplayLoadingStageForLoad = OptimalGameplayLoadingStage.Ui;
    }
    if (gameplayLoadingStageForLoad <= OptimalGameplayLoadingStage.Ui && uiReady) {
      gameplayLoadingStageForLoad = OptimalGameplayLoadingStage.Dialog;
    }
    if (gameplayLoadingStageForLoad <= OptimalGameplayLoadingStage.Dialog && dialogReady) {
      gameplayLoadingStageForLoad = OptimalGameplayLoadingStage.FinalizingReveal;
    }

    if (previousStage == gameplayLoadingStageForLoad || !ShouldLogLoadFlowWarnings()) {
      return;
    }

    Debug.Log(
      "[SingleSceneManager][OptimalLoadingProgress] stage='" + gameplayLoadingStageForLoad +
      "' player_ready=" + (playerReady ? 1 : 0) +
      " location_ready=" + (locationReady ? 1 : 0) +
      " enemies_ready=" + (enemiesReady ? 1 : 0) +
      " ui_ready=" + (uiReady ? 1 : 0) +
      " dialog_ready=" + (dialogReady ? 1 : 0) +
      " current_location=" + ResolveLoadFlowValue(LocationManager.currentLocation)
    );
  }

  void AppendGameplayLoadPipelineFields(StringBuilder builder) {
    if (builder == null) {
      return;
    }

    var shouldExpectEnemyWarmStage = ShouldExpectEnemyWarmStageForCurrentLocation();
    var archetypeCount = shouldExpectEnemyWarmStage ? ResolveCurrentLocationLoadingArchetypeCount() : 0;
    var locationEnemyDefinitionCount = 0;
    if (LocationEnemyData.TryGetLocation(LocationManager.currentLocation, out var locationInfo) &&
        locationInfo != null &&
        locationInfo.enemies != null) {
      locationEnemyDefinitionCount = locationInfo.enemies.Count;
    }

    var registry = ActiveContentRegistryRuntime.Registry;
    var activePackCount = registry != null && registry.ActivePackIds != null ? registry.ActivePackIds.Count : 0;
    AppendLoadFlowBool(builder, "pipeline_player_ready", IsGameplayPlayerBootstrapReady());
    AppendLoadFlowBool(builder, "pipeline_location_ready", !LocationManager.HasPendingBlockingActivationWork);
    AppendLoadFlowBool(builder, "pipeline_enemies_ready", gameplayWarmGateCompletedForLoad);
    AppendLoadFlowBool(builder, "pipeline_ui_ready", IsGameplayUiReadyForLoadingProgress());
    AppendLoadFlowBool(builder, "pipeline_dialog_ready", IsGameplayDialogReadyForLoadingProgress());
    AppendLoadFlowBool(builder, "pipeline_expect_enemy_stage", shouldExpectEnemyWarmStage);
    AppendLoadFlowInt(builder, "pipeline_enemy_archetypes", archetypeCount);
    AppendLoadFlowInt(builder, "pipeline_location_enemy_defs", locationEnemyDefinitionCount);
    AppendLoadFlowField(builder, "pipeline_stage", gameplayLoadingStageForLoad.ToString());
    AppendLoadFlowBool(builder, "content_external_active", ActiveContentRegistryRuntime.HasActiveExternalContent());
    AppendLoadFlowInt(builder, "content_active_pack_count", activePackCount);
    AppendLoadFlowField(builder, "content_default_location", ResolveLoadFlowValue(ActiveContentRegistryRuntime.GetDefaultLocationId()));
  }

  static string ResolveGameplayWarmStageDetail(bool shouldExpectEnemyWarmStage) {
    return shouldExpectEnemyWarmStage ? "Preparing enemies" : "Warming gameplay";
  }

  OptimalGameplayLoadingProgress ResolveOptimalGameplayLoadingProgress(bool hasBlockingProgress, float blockingProgress) {
    if (!IsGameplayLoadPipelineTrackingActive()) {
      return default;
    }

    var playerReady = IsGameplayPlayerBootstrapReady();
    var locationReady = !LocationManager.HasPendingBlockingActivationWork;
    var shouldExpectEnemyWarmStage = ShouldExpectEnemyWarmStageForCurrentLocation();
    var archetypeCount = shouldExpectEnemyWarmStage ? ResolveCurrentLocationLoadingArchetypeCount() : 0;
    var enemyWarmInputsReady = !shouldExpectEnemyWarmStage || archetypeCount > 0;
    var locationDeferredPending = LocationManager.HasPendingDeferredActivationWork;
    var dialogController = ResolveGameplayDialogController();
    var enemiesReady = gameplayWarmGateCompletedForLoad;
    var uiReadyForProgress = IsGameplayUiReadyForLoadingProgress() && !locationDeferredPending;
    var dialogReadyForProgress = dialogController != null && dialogController.IsReadyForLoadingProgress;

    AdvanceOptimalGameplayLoadingStageForLoad(
      playerReady,
      locationReady,
      enemiesReady,
      uiReadyForProgress,
      dialogReadyForProgress
    );

    switch (gameplayLoadingStageForLoad) {
      case OptimalGameplayLoadingStage.Player:
        return new OptimalGameplayLoadingProgress(
          true,
          ResolveStagedLoadingProgress(0f, OptimalLoadingProgressPlayerFloor, ResolveGameplayPlayerStageProgress()),
          OptimalLoadingProgressPlayerFloor,
          "Preparing player"
        );
      case OptimalGameplayLoadingStage.Location:
        return new OptimalGameplayLoadingProgress(
          true,
          ResolveStagedLoadingProgress(
            OptimalLoadingProgressPlayerFloor,
            OptimalLoadingProgressLocationFloor,
            ResolveGameplayLocationStageProgress(locationReady)
          ),
          OptimalLoadingProgressLocationFloor,
          "Activating location"
        );
      case OptimalGameplayLoadingStage.Enemies:
        var enemyStageProgress = 0f;
        if (gameplayWarmGateStartedForLoad && hasBlockingProgress) {
          enemyStageProgress = Mathf.Clamp01(blockingProgress);
        }
        else if (gameplayReadyForSpawnsSentForLoad && enemyWarmInputsReady) {
          enemyStageProgress = 0.2f;
        }
        return new OptimalGameplayLoadingProgress(
          true,
          ResolveStagedLoadingProgress(OptimalLoadingProgressLocationFloor, OptimalLoadingProgressEnemiesFloor, enemyStageProgress),
          OptimalLoadingProgressEnemiesFloor,
          ResolveGameplayWarmStageDetail(shouldExpectEnemyWarmStage)
        );
      case OptimalGameplayLoadingStage.Ui:
        return new OptimalGameplayLoadingProgress(
          true,
          ResolveStagedLoadingProgress(
            OptimalLoadingProgressEnemiesFloor,
            OptimalLoadingProgressUiFloor,
            ResolveGameplayUiStageProgress(dialogController, uiReadyForProgress, locationDeferredPending)
          ),
          OptimalLoadingProgressUiFloor,
          "Preparing UI"
        );
      case OptimalGameplayLoadingStage.Dialog:
        return new OptimalGameplayLoadingProgress(
          true,
          ResolveStagedLoadingProgress(
            OptimalLoadingProgressUiFloor,
            OptimalLoadingProgressDialogFloor,
            ResolveGameplayDialogStageProgress(dialogController, dialogReadyForProgress)
          ),
          OptimalLoadingProgressDialogFloor,
          "Preparing dialog"
        );
      default:
        return new OptimalGameplayLoadingProgress(
          true,
          OptimalLoadingProgressDialogFloor,
          LoadingProgressPreReadyCap,
          "Finalizing reveal"
        );
    }
  }

  int GetRemainingStreamingWork(
    out TextureResidencyCache.QueueSnapshot queue,
    out TextureResidencyCache.SessionSnapshot session,
    out int outstanding,
    out int sessionRemaining
  ) {
    queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    session = TextureResidencyCache.GetSessionSnapshot();
    var deferredSnapshot = TextureResidencyCache.GetDeferredSnapshot();
    outstanding = Mathf.Max(queue.queuedCount + queue.inFlightCount + deferredSnapshot.pendingCount, 0);
    sessionRemaining = session.HasKnownTotal
      ? Mathf.Max(session.EffectiveTotal - session.completedTotal, 0)
      : 0;
    return Mathf.Max(outstanding, sessionRemaining);
  }

  void ResetBlockingProgressState() {
    loadingBlockingReadyCount = 0;
    loadingBlockingTotalCount = 0;
    loadingBlockingCriticalReady = false;
    loadingBlockingHardBypassUsed = false;
    loadingBlockingStateKnown = false;
  }

  void ResetZeroPercentStallDebugState() {
    loadingZeroPercentStartedAt = -1f;
    loadingZeroPercentNextLogAt = -1f;
  }

  void ResetLoadingHeartbeatDebugState() {
    loadingHeartbeatStartedAt = -1f;
    loadingHeartbeatLastLoggedAt = -1f;
    loadingHeartbeatCount = 0;
    loadingHeartbeatNextLogAt = -1f;
  }

  void CaptureBlockingProgressStateFromWarmResult(WarmResult result) {
    loadingBlockingTotalCount = Mathf.Max(result.criticalTotalCount, 0);
    loadingBlockingReadyCount = Mathf.Clamp(result.criticalReadyCount, 0, loadingBlockingTotalCount);
    loadingBlockingCriticalReady = result.playerCriticalReady;
    loadingBlockingHardBypassUsed = result.hardTimeoutBypassUsed;
    loadingBlockingStateKnown = loadingBlockingTotalCount > 0 || loadingBlockingCriticalReady || loadingBlockingHardBypassUsed;
  }

  bool TryGetBlockingProgressState(
    out float progress,
    out int readyCount,
    out int totalCount,
    out bool criticalReady,
    out bool hardBypassUsed
  ) {
    // Blocking scope comes from the warm "critical" addresses: player starters, selected
    // nearby enemies, core player effects, and location-declared critical content.
    if (StreamingWarmOrchestrator.TryGetActiveProgress(out var snapshot)) {
      loadingBlockingTotalCount = Mathf.Max(snapshot.criticalTotalCount, 0);
      loadingBlockingReadyCount = Mathf.Clamp(snapshot.criticalReadyCount, 0, loadingBlockingTotalCount);
      loadingBlockingCriticalReady = snapshot.criticalReady;
      loadingBlockingHardBypassUsed = false;
      loadingBlockingStateKnown = true;
    }

    if (!loadingBlockingStateKnown) {
      progress = 0f;
      readyCount = 0;
      totalCount = 0;
      criticalReady = false;
      hardBypassUsed = false;
      return false;
    }

    readyCount = loadingBlockingReadyCount;
    totalCount = loadingBlockingTotalCount;
    criticalReady = loadingBlockingCriticalReady;
    hardBypassUsed = loadingBlockingHardBypassUsed;
    progress = totalCount > 0
      ? (float)readyCount / totalCount
      : (criticalReady || hardBypassUsed ? 1f : 0f);
    progress = Mathf.Clamp01(progress);
    return true;
  }

  bool IsBlockingScopeReady(
    bool resolverIdle,
    bool playerReady,
    bool criticalReady,
    bool hardBypassUsed,
    TextureResidencyCache.QueueSnapshot queue
  ) {
    if (!resolverIdle || !playerReady) return false;
    if (!criticalReady && !hardBypassUsed) return false;
    var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
    return IsQueueWithinBlockingReadyThresholds(queue, deferredPending);
  }

  void ResolveBlockingReadyQueueThresholds(out int maxOutstanding, out int maxInFlight) {
    maxOutstanding = Application.isMobilePlatform
      ? Mathf.Max(streamingBlockingReadyMaxOutstandingMobile, 0)
      : Mathf.Max(streamingBlockingReadyMaxOutstandingDesktop, 0);
    maxInFlight = Application.isMobilePlatform
      ? Mathf.Max(streamingBlockingReadyMaxInFlightMobile, 0)
      : Mathf.Max(streamingBlockingReadyMaxInFlightDesktop, 0);

    // Blocking progress already scopes the required first-frame content.
    // Forcing the entire queue to zero here parks the overlay at 99% while
    // long-tail warm work drains, which is the opposite of the intended model.
    if (enforceZeroOutstandingBeforeUnlock && !loadingBlockingStateKnown) {
      maxOutstanding = 0;
      maxInFlight = 0;
    }
  }

  bool IsQueueWithinBlockingReadyThresholds(TextureResidencyCache.QueueSnapshot queue, int deferredPending) {
    ResolveBlockingReadyQueueThresholds(out var maxOutstanding, out var maxInFlight);
    var outstanding = Mathf.Max(queue.queuedCount + queue.inFlightCount + deferredPending, 0);
    if (outstanding > maxOutstanding) return false;
    if (queue.inFlightCount > maxInFlight) return false;
    return true;
  }

  int CalculateLoadingPercentFromTextureQueue(out string statusDetail) {
    var remainingWork = GetRemainingStreamingWork(
      out var queue,
      out var session,
      out var outstanding,
      out var sessionRemaining
    );
    if (outstanding > 0) {
      loadingProgressObservedWork = true;
      if (loadingProgressPeakOutstanding < 0) {
        loadingProgressPeakOutstanding = outstanding;
      }
      else if (outstanding > loadingProgressPeakOutstanding) {
        loadingProgressPeakOutstanding = outstanding;
      }
    }

    var rawSessionProgress = session.Progress;
    if (sessionRemaining > 0) {
      loadingProgressObservedWork = true;
    }

    if (loadingProgressGoalTotal <= 0 && remainingWork > 0) {
      loadingProgressGoalTotal = remainingWork;
      loadingProgressGoalBestRemaining = loadingProgressGoalTotal;
    }

    if (loadingProgressGoalTotal > 0) {
      if (remainingWork > loadingProgressGoalTotal) {
        var completedBeforeResize = Mathf.Clamp(loadingProgressGoalTotal - loadingProgressGoalBestRemaining, 0, loadingProgressGoalTotal);
        loadingProgressGoalTotal = remainingWork;
        loadingProgressGoalBestRemaining = Mathf.Clamp(loadingProgressGoalTotal - completedBeforeResize, 0, loadingProgressGoalTotal);
      }
      loadingProgressGoalBestRemaining = Mathf.Clamp(
        Mathf.Min(loadingProgressGoalBestRemaining, remainingWork),
        0,
        loadingProgressGoalTotal
      );
    }

    var resolverIdle = SpriteRuntimeResolver.IsWarmupIdle();
    var playerReady = IsPlayerFirstFrameReady();
    var hasBlockingProgress = TryGetBlockingProgressState(
      out var blockingProgress,
      out var blockingReadyCount,
      out var blockingTotalCount,
      out var blockingCriticalReady,
      out var blockingHardBypassUsed
    );
    var locationActivationPending = LocationManager.HasPendingBlockingActivationWork;
    var locationDeferredPending = LocationManager.HasPendingDeferredActivationWork;
    if (hasBlockingProgress && (blockingTotalCount > 0 || blockingProgress > 0f)) {
      loadingProgressObservedWork = true;
    }

    var blockingReady = hasBlockingProgress &&
      IsBlockingScopeReady(resolverIdle, playerReady, blockingCriticalReady, blockingHardBypassUsed, queue) &&
      !locationActivationPending &&
      !locationDeferredPending;

    var hasOutstandingWork = hasBlockingProgress
      ? !blockingReady
      : (remainingWork > 0 || !resolverIdle || !playerReady || locationActivationPending || locationDeferredPending);
    if (hasOutstandingWork) {
      loadingProgressIdleStartedAt = -1f;
    }
    else if (loadingProgressIdleStartedAt < 0f) {
      loadingProgressIdleStartedAt = Time.realtimeSinceStartup;
    }

    var completionSettled =
      !hasOutstandingWork &&
      loadingProgressIdleStartedAt >= 0f &&
      (Time.realtimeSinceStartup - loadingProgressIdleStartedAt) >= LoadingProgressCompletionSettleSeconds;

    var goalProgress = loadingProgressGoalTotal > 0
      ? 1f - ((float)loadingProgressGoalBestRemaining / loadingProgressGoalTotal)
      : 0f;
    goalProgress = Mathf.Clamp01(goalProgress);

    var queueProgress = 0f;
    if (loadingProgressPeakOutstanding > 0) {
      queueProgress = 1f - ((float)outstanding / loadingProgressPeakOutstanding);
    }
    queueProgress = Mathf.Clamp01(queueProgress);

    // Blocking progress should determine release gating, but the visible percentage
    // should still reflect real queue/session progress so startup does not appear frozen.
    var nonBlockingProgress = Mathf.Max(goalProgress, queueProgress);
    var targetProgress = hasBlockingProgress
      ? Mathf.Max(blockingProgress, Mathf.Min(nonBlockingProgress, 0.9f))
      : goalProgress;
    var optimalGameplayProgress = ResolveOptimalGameplayLoadingProgress(hasBlockingProgress, blockingProgress);
    if (optimalGameplayProgress.IsActive) {
      targetProgress = Mathf.Max(targetProgress, optimalGameplayProgress.TargetProgress);
      targetProgress = Mathf.Min(targetProgress, optimalGameplayProgress.ProgressCeiling);
    }
    if (!loadingProgressObservedWork && !hasBlockingProgress && !optimalGameplayProgress.IsActive) {
      targetProgress = 0f;
    }
    targetProgress = ClampLoadingTargetProgressForReveal(targetProgress, hasOutstandingWork, completionSettled);
    var targetPercent = Mathf.Clamp(targetProgress * 100f, 0f, 100f);
    var actualPercent = AdvanceLoadingPercentDisplay(targetPercent);
    statusDetail = ResolveLoadingStatusDetail(
      hasBlockingProgress,
      blockingProgress,
      resolverIdle,
      playerReady,
      locationActivationPending,
      locationDeferredPending,
      outstanding,
      hasOutstandingWork,
      completionSettled,
      optimalGameplayProgress
    );

    MaybeLogLoadingProgressDebug(
      session,
      rawSessionProgress,
      queue,
      outstanding,
      queueProgress,
      remainingWork,
      goalProgress,
      resolverIdle,
      playerReady,
      hasOutstandingWork,
      completionSettled,
      hasBlockingProgress,
      blockingReadyCount,
      blockingTotalCount,
      blockingProgress,
      blockingCriticalReady,
      blockingHardBypassUsed,
      blockingReady,
      targetProgress,
      actualPercent
    );
    MaybeLogZeroPercentStall(
      queue,
      session,
      outstanding,
      remainingWork,
      resolverIdle,
      playerReady,
      hasBlockingProgress,
      blockingReadyCount,
      blockingTotalCount,
      blockingProgress,
      blockingCriticalReady,
      blockingHardBypassUsed,
      blockingReady,
      targetProgress,
      actualPercent
    );

    return actualPercent;
  }

  string ResolveLoadingStatusDetail(
    bool hasBlockingProgress,
    float blockingProgress,
    bool resolverIdle,
    bool playerReady,
    bool locationActivationPending,
    bool locationDeferredPending,
    int outstanding,
    bool hasOutstandingWork,
    bool completionSettled,
    OptimalGameplayLoadingProgress optimalGameplayProgress
  ) {
    if (!string.IsNullOrWhiteSpace(loadingStatusOverride)) {
      return loadingStatusOverride;
    }
    if (optimalGameplayProgress.IsActive) {
      return optimalGameplayProgress.Detail;
    }
    if (!loadingProgressObservedWork && !hasBlockingProgress && outstanding <= 0) {
      return "Starting";
    }
    if (hasBlockingProgress && blockingProgress < 0.999f) {
      return "Warming critical";
    }
    if (locationActivationPending || locationDeferredPending) {
      return "Activating location";
    }
    if (!playerReady) {
      return "Preparing player";
    }
    if (!resolverIdle) {
      return "Resolving assets";
    }
    if (ShouldKeepFinalizingRevealStatus(outstanding, hasOutstandingWork)) {
      return "Finalizing reveal";
    }
    if (outstanding > 0 || hasOutstandingWork) {
      return "Draining queue";
    }
    if (!completionSettled) {
      return "Settling";
    }
    return "Finalizing reveal";
  }

  static bool ShouldKeepFinalizingRevealStatus(int outstanding, bool hasOutstandingWork) {
    return hasOutstandingWork && outstanding <= 1;
  }

  void SetLoadingStatusOverride(string detail) {
    loadingStatusOverride = string.IsNullOrWhiteSpace(detail) ? "" : detail.Trim();
  }

  void ClearLoadingStatusOverride() {
    loadingStatusOverride = "";
  }

  float ClampLoadingTargetProgressForReveal(float targetProgress, bool hasOutstandingWork, bool completionSettled) {
    // Keep the visible percent inside the active stage band until the explicit
    // release step owns 100%. Auto-promoting to 99% here creates a false stall:
    // any brief "ready" blip becomes sticky even if the pipeline still reports
    // "Preparing UI", "Preparing dialog", or a late reveal blocker afterward.
    return Mathf.Min(targetProgress, LoadingProgressPreReadyCap);
  }

  int AdvanceLoadingPercentDisplay(float targetPercent) {
    if (!loadingPercentDisplayInitialized) {
      loadingPercentDisplayInitialized = true;
      loadingPercentDisplayValue = Mathf.Max(loadingPercent >= 0 ? loadingPercent : 0, 0);
    }

    // Loading feedback should not move backwards when queue/session totals resize mid-flight.
    var clampedTargetPercent = Mathf.Max(targetPercent, loadingPercentDisplayValue);
    var riseRate = Mathf.Max(loadingPercentRisePerSecond, 1f);
    var dt = Mathf.Max(Time.unscaledDeltaTime, 0f);
    loadingPercentDisplayValue = Mathf.MoveTowards(
      loadingPercentDisplayValue,
      clampedTargetPercent,
      riseRate * dt
    );
    return Mathf.Clamp(Mathf.RoundToInt(loadingPercentDisplayValue), 0, 100);
  }

  static string BuildLoadingDisplayText(int percent, string detail) {
    if (string.IsNullOrWhiteSpace(detail)) {
      return percent + "%";
    }
    return percent + "% - " + detail.Trim();
  }

  void UpdateLoadingScreenText(int percent, string detail) {
    var normalizedDetail = string.IsNullOrWhiteSpace(detail) ? "" : detail.Trim();
    var detailChanged = !string.Equals(loadingStatusDetail, normalizedDetail, StringComparison.Ordinal);
    if (percent == loadingPercent && !detailChanged) return;
    loadingPercent = percent;
    loadingStatusDetail = normalizedDetail;
    SetLoadingText(BuildLoadingDisplayText(percent, normalizedDetail));
    if (!detailChanged || !ShouldLogLoadFlowWarnings()) return;

    var builder = BeginLoadFlowLog("[SingleSceneManager][LoadingStatus]");
    AppendLoadFlowInt(builder, "percent", percent);
    AppendLoadFlowField(builder, "detail", "'" + (string.IsNullOrWhiteSpace(normalizedDetail) ? "-" : normalizedDetail) + "'");
    AppendLoadFlowField(builder, "overlay_reason", ResolveLoadFlowValue(SpriteStreamingLoadingState.ActiveReason));
    AppendLoadFlowField(builder, "current_section", ResolveCurrentSection().ToString());
    AppendLoadFlowField(builder, "current_location", ResolveLoadFlowValue(LocationManager.currentLocation));
    AppendGameplayLoadPipelineFields(builder);
    Debug.Log(builder.ToString());
  }

  void ResetLoadingProgressForPhase(bool force = false) {
    var loadingActive = holdBlackscreenOpaqueDuringLoad || IsLoadingFlowActive();
    if (!loadingActive) return;
    if (!force && loadingPercent >= 0) return;

    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var outstanding = Mathf.Max(queue.queuedCount + queue.inFlightCount, 0);
    loadingProgressPeakOutstanding = outstanding > 0 ? outstanding : -1;
    loadingProgressGoalTotal = -1;
    loadingProgressGoalBestRemaining = int.MaxValue;
    loadingProgressIdleStartedAt = -1f;
    loadingProgressObservedWork = outstanding > 0;
    loadingProgressNextDebugLogAt = -1f;
    ResetBlockingProgressState();
    ResetZeroPercentStallDebugState();
    loadingPercent = -1;
    loadingStatusDetail = "Starting";
    loadingStatusOverride = "";
    loadingPercentDisplayInitialized = true;
    loadingPercentDisplayValue = 0f;
    SetLoadingText(BuildLoadingDisplayText(0, loadingStatusDetail));
    loadingPercent = 0;
  }

  void BeginLoadingProgressGoalAtFadeStart() {
    // Queue snapshot includes all load priorities (Immediate/Warmup/Background).
    var remainingWork = GetRemainingStreamingWork(
      out _,
      out _,
      out var outstanding,
      out _
    );
    loadingProgressGoalTotal = remainingWork > 0 ? remainingWork : -1;
    loadingProgressGoalBestRemaining = loadingProgressGoalTotal > 0 ? loadingProgressGoalTotal : int.MaxValue;
    loadingProgressPeakOutstanding = outstanding > 0 ? outstanding : -1;
    loadingProgressObservedWork = remainingWork > 0;
    loadingProgressNextDebugLogAt = -1f;
    if (ShouldLogLoadingProgressDebug()) {
      Debug.Log(
        "[SingleSceneManager][LoadingProgress] prime_at_fade_start goal_total=" + loadingProgressGoalTotal +
        " queue_outstanding=" + outstanding +
        " remaining_work=" + remainingWork
      );
    }
  }

  void BeginLoadingProgressUiAfterFadeIn() {
    var remainingWork = GetRemainingStreamingWork(
      out _,
      out _,
      out var outstanding,
      out _
    );

    if (loadingProgressGoalTotal <= 0 && remainingWork > 0) {
      loadingProgressGoalTotal = remainingWork;
      loadingProgressGoalBestRemaining = remainingWork;
    }
    if (loadingProgressGoalTotal > 0 && remainingWork > loadingProgressGoalTotal) {
      var completedBeforeResize = Mathf.Clamp(loadingProgressGoalTotal - loadingProgressGoalBestRemaining, 0, loadingProgressGoalTotal);
      loadingProgressGoalTotal = remainingWork;
      loadingProgressGoalBestRemaining = Mathf.Clamp(loadingProgressGoalTotal - completedBeforeResize, 0, loadingProgressGoalTotal);
    }

    if (loadingProgressGoalTotal > 0) {
      loadingProgressGoalBestRemaining = Mathf.Clamp(
        Mathf.Min(loadingProgressGoalBestRemaining, remainingWork),
        0,
        loadingProgressGoalTotal
      );
    }
    if (outstanding > 0) {
      loadingProgressPeakOutstanding = Mathf.Max(loadingProgressPeakOutstanding, outstanding);
    }
    loadingProgressObservedWork = loadingProgressObservedWork || remainingWork > 0;
    loadingProgressNextDebugLogAt = -1f;
    loadingPercent = -1;
    loadingStatusDetail = "Starting";
    loadingStatusOverride = "";
    loadingPercentDisplayInitialized = true;
    loadingPercentDisplayValue = 0f;
    UpdateLoadingScreenText(0, loadingStatusDetail);
    loadingOverlayChildrenReady = true;
    SetLoadingProgressUiActive(true);
    ScheduleLoadingProgressUiArmCheck(remainingWork > 0 || loadingProgressGoalTotal > 0);

    if (ShouldLogLoadingProgressDebug()) {
      Debug.Log(
        "[SingleSceneManager][LoadingProgress] activate_after_fade_in goal_total=" + loadingProgressGoalTotal +
        " queue_outstanding=" + outstanding +
        " remaining_work=" + remainingWork
      );
    }
  }

  void ScheduleLoadingProgressUiArmCheck(bool shouldCheck) {
    loadingProgressUiArmCheckFrame = shouldCheck ? Time.frameCount + 1 : -1;
  }

  void MaybeWarnLoadingProgressUiNotArmed() {
    if (loadingProgressUiArmCheckFrame < 0 || Time.frameCount < loadingProgressUiArmCheckFrame) return;
    loadingProgressUiArmCheckFrame = -1;
    if (IsLoadingProgressUiVisible() || !loadingOverlayChildrenReady) return;
    if (!(holdBlackscreenOpaqueDuringLoad || IsLoadingFlowActive())) return;
    if (!ShouldLogLoadFlowWarnings()) return;

    Debug.LogWarning(
      "[SingleSceneManager][LoadingProgressUi] arm_failed" +
      " loading_root=" + (LoadingScreen != null && LoadingScreen.activeSelf ? 1 : 0) +
      " progress_ui=" + (IsLoadingProgressUiVisible() ? 1 : 0) +
      " overlay_reason=" + ResolveLoadFlowValue(SpriteStreamingLoadingState.ActiveReason) +
      " current_section=" + ResolveCurrentSection() +
      " slot=" + SaveSlotManager.slot
    );
  }

  void MaybeLogLoadingProgressDebug(
    TextureResidencyCache.SessionSnapshot session,
    float rawSessionProgress,
    TextureResidencyCache.QueueSnapshot queue,
    int outstanding,
    float queueProgress,
    int remainingWork,
    float goalProgress,
    bool resolverIdle,
    bool playerReady,
    bool hasOutstandingWork,
    bool completionSettled,
    bool usingBlockingProgress,
    int blockingReadyCount,
    int blockingTotalCount,
    float blockingProgress,
    bool blockingCriticalReady,
    bool blockingHardBypassUsed,
    bool blockingReady,
    float targetProgress,
    int actualPercent
  ) {
    if (!ShouldLogLoadingProgressDebug()) return;
    var now = Time.realtimeSinceStartup;
    var logIntervalSeconds = Mathf.Max(SpriteStreamingRuntimeSettings.LoadingProgressLogIntervalMs, 100) / 1000f;
    var expectedPercent = Mathf.Clamp(Mathf.RoundToInt(targetProgress * 100f), 0, 100);
    var anomaly = expectedPercent >= 95 && actualPercent <= 5;
    if (!anomaly && now < loadingProgressNextDebugLogAt) return;
    loadingProgressNextDebugLogAt = now + logIntervalSeconds;
    var deferredSnapshot = TextureResidencyCache.GetDeferredSnapshot();
    var gameplayUiReady = IsGameplayUiReadyForLoadingProgress();
    var gameplayDialogReady = IsGameplayDialogReadyForLoadingProgress();

    Debug.Log(
      "[SingleSceneManager][LoadingProgress] expected=" + expectedPercent +
      " actual=" + actualPercent +
      " target=" + targetProgress.ToString("0.000") +
      " display=" + loadingPercentDisplayValue.ToString("0.00") +
      " raw_session=" + rawSessionProgress.ToString("0.000") +
      " session_expected=" + session.expectedTotal +
      " session_scheduled=" + session.scheduledTotal +
      " session_completed=" + session.completedTotal +
      " session_total=" + session.EffectiveTotal +
      " queue_queued=" + queue.queuedCount +
      " queue_in_flight=" + queue.inFlightCount +
      " queue_outstanding=" + outstanding +
      " queue_peak=" + loadingProgressPeakOutstanding +
      " queue_progress=" + queueProgress.ToString("0.000") +
      " goal_total=" + loadingProgressGoalTotal +
      " goal_remaining_best=" + loadingProgressGoalBestRemaining +
      " remaining_work=" + remainingWork +
      " goal_progress=" + goalProgress.ToString("0.000") +
      " blocking_mode=" + (usingBlockingProgress ? 1 : 0) +
      " blocking_ready_count=" + blockingReadyCount +
      " blocking_total_count=" + blockingTotalCount +
      " blocking_progress=" + blockingProgress.ToString("0.000") +
      " blocking_critical_ready=" + (blockingCriticalReady ? 1 : 0) +
      " blocking_hard_bypass=" + (blockingHardBypassUsed ? 1 : 0) +
      " blocking_ready=" + (blockingReady ? 1 : 0) +
      " observed_work=" + (loadingProgressObservedWork ? 1 : 0) +
      " pipeline_player_ready=" + (IsGameplayPlayerBootstrapReady() ? 1 : 0) +
      " pipeline_location_ready=" + (!LocationManager.HasPendingBlockingActivationWork ? 1 : 0) +
      " pipeline_enemies_ready=" + (gameplayWarmGateCompletedForLoad ? 1 : 0) +
      " pipeline_ui_ready=" + (gameplayUiReady ? 1 : 0) +
      " pipeline_dialog_ready=" + (gameplayDialogReady ? 1 : 0) +
      " resolver_idle=" + (resolverIdle ? 1 : 0) +
      " player_ready=" + (playerReady ? 1 : 0) +
      " has_work=" + (hasOutstandingWork ? 1 : 0) +
      " settled=" + (completionSettled ? 1 : 0) +
      " deferred_pending=" + deferredSnapshot.pendingCount +
      " deferred_flushed_frame=" + deferredSnapshot.flushedThisFrame +
      " deferred_total=" + deferredSnapshot.totalDeferredCount +
      " deferred_requests_total=" + deferredSnapshot.totalDeferralRequestCount +
      " deferred_promoted=" + deferredSnapshot.totalPromotedCount
    );
  }

  static bool ShouldLogLoadFlowWarnings() {
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }

  static bool ShouldLogLoadFlowDebug() {
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    if (!SpriteStreamingRuntimeSettings.EnableDiagnostics) return false;
    return ShouldLogLoadFlowWarnings();
  }

  StringBuilder BeginLoadFlowLog(string prefix) {
    var builder = loadFlowLogBuilder;
    builder.Clear();
    builder.Append(prefix);
    return builder;
  }

  static void AppendLoadFlowField(StringBuilder builder, string name, string value) {
    if (builder == null || string.IsNullOrWhiteSpace(name)) return;
    builder.Append(' ').Append(name).Append('=').Append(value ?? "");
  }

  static void AppendLoadFlowInt(StringBuilder builder, string name, int value) {
    if (builder == null || string.IsNullOrWhiteSpace(name)) return;
    builder.Append(' ').Append(name).Append('=').Append(value);
  }

  static void AppendLoadFlowFloat(StringBuilder builder, string name, float value, string format = "0.000") {
    if (builder == null || string.IsNullOrWhiteSpace(name)) return;
    builder.Append(' ').Append(name).Append('=').Append(value.ToString(format));
  }

  static void AppendLoadFlowBool(StringBuilder builder, string name, bool value) {
    if (builder == null || string.IsNullOrWhiteSpace(name)) return;
    builder.Append(' ').Append(name).Append('=').Append(value ? 1 : 0);
  }

  void LogGameplayLoadTiming(string stage, string result, float startedAt, string extraFields = "") {
    if (!ShouldLogLoadFlowWarnings()) return;
    var builder = BeginLoadFlowLog("[SingleSceneManager][LoadTiming]");
    AppendLoadFlowField(builder, "stage", ResolveLoadFlowValue(stage));
    AppendLoadFlowField(builder, "result", ResolveLoadFlowValue(result));
    AppendLoadFlowFloat(builder, "elapsed_ms", startedAt >= 0f ? Mathf.Max(Time.realtimeSinceStartup - startedAt, 0f) * 1000f : 0f, "0.0");
    AppendLoadFlowField(builder, "overlay_reason", ResolveLoadFlowValue(SpriteStreamingLoadingState.ActiveReason));
    AppendLoadFlowField(builder, "current_section", ResolveCurrentSection().ToString());
    AppendLoadFlowField(builder, "current_location", ResolveLoadFlowValue(LocationManager.currentLocation));
    if (!string.IsNullOrWhiteSpace(extraFields)) {
      builder.Append(' ').Append(extraFields.Trim());
    }
    Debug.Log(builder.ToString());
  }

  static string ResolveLoadFlowValue(string value, string fallback = "-") {
    return string.IsNullOrWhiteSpace(value) ? fallback : value;
  }

  void MaybeLogLoadingScreenHeartbeat() {
    var shouldLogWarnings = ShouldLogLoadFlowWarnings();
    var shouldLogVerbose = ShouldLogLoadFlowDebug();
    if (!shouldLogWarnings && !shouldLogVerbose) return;
    var now = Time.realtimeSinceStartup;
    if (loadingHeartbeatStartedAt < 0f) {
      loadingHeartbeatStartedAt = now;
      loadingHeartbeatLastLoggedAt = now;
      loadingHeartbeatCount = 0;
      loadingHeartbeatNextLogAt = now;
    }
    if (now < loadingHeartbeatNextLogAt) return;
    var scheduledAt = loadingHeartbeatNextLogAt;
    var gapSeconds = Mathf.Max(now - loadingHeartbeatLastLoggedAt, 0f);
    var overdueSeconds = Mathf.Max(now - scheduledAt, 0f);
    var missedBeats = overdueSeconds > 0f
      ? Mathf.FloorToInt(overdueSeconds / LoadingHeartbeatLogIntervalSeconds)
      : 0;
    if (gapSeconds > LoadingHeartbeatAcceptableGapSeconds && shouldLogWarnings) {
      var warningBuilder = BeginLoadFlowLog("[SingleSceneManager][LoadingHeartbeatGap]");
      AppendLoadFlowFloat(warningBuilder, "gap_s", gapSeconds);
      AppendLoadFlowFloat(warningBuilder, "acceptable_s", LoadingHeartbeatAcceptableGapSeconds);
      AppendLoadFlowInt(warningBuilder, "missed_beats", missedBeats);
      AppendLoadFlowInt(warningBuilder, "investigation_required", 1);
      AppendLoadFlowField(warningBuilder, "overlay_reason", ResolveLoadFlowValue(SpriteStreamingLoadingState.ActiveReason));
      AppendLoadFlowField(warningBuilder, "current_section", ResolveCurrentSection().ToString());
      AppendLoadFlowField(warningBuilder, "current_location", ResolveLoadFlowValue(LocationManager.currentLocation));
      Debug.LogWarning(warningBuilder.ToString());
    }
    loadingHeartbeatLastLoggedAt = now;
    loadingHeartbeatCount++;
    loadingHeartbeatNextLogAt = loadingHeartbeatStartedAt + (loadingHeartbeatCount * LoadingHeartbeatLogIntervalSeconds);
    while (loadingHeartbeatNextLogAt <= now) {
      loadingHeartbeatCount++;
      loadingHeartbeatNextLogAt = loadingHeartbeatStartedAt + (loadingHeartbeatCount * LoadingHeartbeatLogIntervalSeconds);
    }
    if (!shouldLogVerbose) return;

    var remainingWork = GetRemainingStreamingWork(
      out var queue,
      out var session,
      out var outstanding,
      out _
    );
    var deferredSnapshot = TextureResidencyCache.GetDeferredSnapshot();
    var resolverIdle = SpriteRuntimeResolver.IsWarmupIdle();
    var playerReady = IsPlayerFirstFrameReady();
    var blockerSummary = playerReady || !TryGetPlayerFirstFrameBlocker(out var blocker) ? "" : blocker;
    var hasBlockingProgress = TryGetBlockingProgressState(
      out var blockingProgress,
      out var blockingReadyCount,
      out var blockingTotalCount,
      out var blockingCriticalReady,
      out var blockingHardBypassUsed
    );
    var locationActivationPending = LocationManager.HasPendingBlockingActivationWork;

    var infoBuilder = BeginLoadFlowLog("[SingleSceneManager][LoadingHeartbeat]");
    AppendLoadFlowFloat(infoBuilder, "elapsed_s", now - loadingHeartbeatStartedAt);
    AppendLoadFlowFloat(infoBuilder, "gap_s", gapSeconds);
    AppendLoadFlowFloat(infoBuilder, "scheduled_at_s", Mathf.Max(scheduledAt - loadingHeartbeatStartedAt, 0f));
    AppendLoadFlowInt(infoBuilder, "missed_beats", missedBeats);
    AppendLoadFlowInt(infoBuilder, "frame", Time.frameCount);
    AppendLoadFlowFloat(infoBuilder, "dt_unscaled", Time.unscaledDeltaTime);
    AppendLoadFlowField(infoBuilder, "overlay_reason", ResolveLoadFlowValue(SpriteStreamingLoadingState.ActiveReason));
    AppendLoadFlowField(infoBuilder, "current_section", ResolveCurrentSection().ToString());
    AppendLoadFlowField(infoBuilder, "current_location", ResolveLoadFlowValue(LocationManager.currentLocation));
    AppendLoadFlowBool(infoBuilder, "loading_root", LoadingScreen != null && LoadingScreen.activeSelf);
    AppendLoadFlowBool(infoBuilder, "black_hold", holdBlackscreenOpaqueDuringLoad);
    AppendLoadFlowBool(infoBuilder, "black_visible", loadingBlackscreen != null && loadingBlackscreen.activeInHierarchy);
    AppendLoadFlowBool(infoBuilder, "progress_ui", IsLoadingProgressUiVisible());
    AppendLoadFlowBool(infoBuilder, "overlay_children_ready", loadingOverlayChildrenReady);
    AppendLoadFlowInt(infoBuilder, "percent", loadingPercent);
    AppendLoadFlowInt(infoBuilder, "session_completed", session.completedTotal);
    AppendLoadFlowInt(infoBuilder, "session_total", session.EffectiveTotal);
    AppendLoadFlowInt(infoBuilder, "remaining_work", remainingWork);
    AppendLoadFlowInt(infoBuilder, "queue_queued", queue.queuedCount);
    AppendLoadFlowInt(infoBuilder, "queue_in_flight", queue.inFlightCount);
    AppendLoadFlowInt(infoBuilder, "outstanding", outstanding);
    AppendLoadFlowInt(infoBuilder, "deferred_pending", deferredSnapshot.pendingCount);
    AppendLoadFlowBool(infoBuilder, "resolver_idle", resolverIdle);
    AppendLoadFlowBool(infoBuilder, "player_ready", playerReady);
    AppendLoadFlowBool(infoBuilder, "player_animation_held", playerAnimationHeldForLoadingOverlay);
    AppendLoadFlowField(infoBuilder, "player_blocker", playerReady ? "-" : ResolveLoadFlowValue(blockerSummary));
    AppendLoadFlowBool(infoBuilder, "blocking_mode", hasBlockingProgress);
    AppendLoadFlowInt(infoBuilder, "blocking_ready_count", blockingReadyCount);
    AppendLoadFlowInt(infoBuilder, "blocking_total_count", blockingTotalCount);
    AppendLoadFlowFloat(infoBuilder, "blocking_progress", blockingProgress);
    AppendLoadFlowBool(infoBuilder, "blocking_critical_ready", blockingCriticalReady);
    AppendLoadFlowBool(infoBuilder, "blocking_hard_bypass", blockingHardBypassUsed);
    AppendLoadFlowBool(infoBuilder, "location_activation_pending", locationActivationPending);
    AppendGameplayLoadPipelineFields(infoBuilder);
    Debug.Log(infoBuilder.ToString());
  }

  void LogStartGameRequest(bool isNewGame, SaveData loadedSlot) {
    if (!ShouldLogLoadFlowDebug()) return;
    var savedLocation = loadedSlot != null && loadedSlot.ContainsKey("location")
      ? Convert.ToString(loadedSlot["location"])
      : "";
    Debug.Log(
      "[SingleSceneManager][LoadStart] kind=" + (isNewGame ? "new_game" : "load_save") +
      " slot=" + SaveSlotManager.slot +
      " slot_exists=" + (SaveSlotManager.CurrentSlotExists() ? 1 : 0) +
      " save_key_count=" + (loadedSlot != null ? loadedSlot.Count : 0) +
      " saved_location=" + (string.IsNullOrWhiteSpace(savedLocation) ? "-" : savedLocation.Trim()) +
      " current_location=" + (string.IsNullOrWhiteSpace(LocationManager.currentLocation) ? "-" : LocationManager.currentLocation) +
      " current_section=" + ResolveCurrentSection() +
      " overlay_reason=" + (string.IsNullOrWhiteSpace(SpriteStreamingLoadingState.ActiveReason) ? "-" : SpriteStreamingLoadingState.ActiveReason)
    );
  }

  void LogLoadStateDispatch(string stage) {
    if (!ShouldLogLoadFlowDebug()) return;
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var deferredSnapshot = TextureResidencyCache.GetDeferredSnapshot();
    var playerReady = IsPlayerFirstFrameReady();
    Debug.Log(
      "[SingleSceneManager][LoadState] stage=" + (string.IsNullOrWhiteSpace(stage) ? "unspecified" : stage.Trim()) +
      " slot=" + SaveSlotManager.slot +
      " overlay_reason=" + (string.IsNullOrWhiteSpace(SpriteStreamingLoadingState.ActiveReason) ? "-" : SpriteStreamingLoadingState.ActiveReason) +
      " current_location=" + (string.IsNullOrWhiteSpace(LocationManager.currentLocation) ? "-" : LocationManager.currentLocation) +
      " queue_queued=" + queue.queuedCount +
      " queue_in_flight=" + queue.inFlightCount +
      " deferred_pending=" + deferredSnapshot.pendingCount +
      " player_ready=" + (playerReady ? 1 : 0) +
      " current_section=" + ResolveCurrentSection()
    );
  }

  void LogLocationLoadRequest(string requestedLocationId, string resolvedLocationId) {
    if (!ShouldLogLoadFlowDebug()) return;
    Debug.Log(
      "[SingleSceneManager][LocationLoad] requested=" + (string.IsNullOrWhiteSpace(requestedLocationId) ? "-" : requestedLocationId.Trim()) +
      " resolved=" + (string.IsNullOrWhiteSpace(resolvedLocationId) ? "-" : resolvedLocationId.Trim()) +
      " current_before=" + (string.IsNullOrWhiteSpace(LocationManager.currentLocation) ? "-" : LocationManager.currentLocation) +
      " overlay_reason=" + (string.IsNullOrWhiteSpace(SpriteStreamingLoadingState.ActiveReason) ? "-" : SpriteStreamingLoadingState.ActiveReason) +
      " current_section=" + ResolveCurrentSection()
    );
  }

  void LogLocationUpdate(string previousLocationId, string currentLocationId) {
    if (!ShouldLogLoadFlowDebug()) return;
    Debug.Log(
      "[SingleSceneManager][LocationLoad] updated previous=" + (string.IsNullOrWhiteSpace(previousLocationId) ? "-" : previousLocationId.Trim()) +
      " current=" + (string.IsNullOrWhiteSpace(currentLocationId) ? "-" : currentLocationId.Trim()) +
      " overlay_reason=" + (string.IsNullOrWhiteSpace(SpriteStreamingLoadingState.ActiveReason) ? "-" : SpriteStreamingLoadingState.ActiveReason) +
      " current_section=" + ResolveCurrentSection()
    );
  }

  void MaybeLogZeroPercentStall(
    TextureResidencyCache.QueueSnapshot queue,
    TextureResidencyCache.SessionSnapshot session,
    int outstanding,
    int remainingWork,
    bool resolverIdle,
    bool playerReady,
    bool usingBlockingProgress,
    int blockingReadyCount,
    int blockingTotalCount,
    float blockingProgress,
    bool blockingCriticalReady,
    bool blockingHardBypassUsed,
    bool blockingReady,
    float targetProgress,
    int actualPercent
  ) {
    if (!ShouldLogLoadFlowWarnings()) return;
    if (!loadingOverlayChildrenReady || actualPercent > 0) {
      ResetZeroPercentStallDebugState();
      return;
    }

    var overlayActive = holdBlackscreenOpaqueDuringLoad || SpriteStreamingLoadingState.IsLoadingOverlayActive;
    if (!overlayActive) {
      ResetZeroPercentStallDebugState();
      return;
    }

    var now = Time.realtimeSinceStartup;
    if (loadingZeroPercentStartedAt < 0f) {
      loadingZeroPercentStartedAt = now;
      loadingZeroPercentNextLogAt = now + LoadingZeroPercentStallDelaySeconds;
      return;
    }

    if (now < loadingZeroPercentNextLogAt) return;
    loadingZeroPercentNextLogAt = now + LoadingZeroPercentStallLogIntervalSeconds;
    var deferredSnapshot = TextureResidencyCache.GetDeferredSnapshot();
    var blockerSummary = playerReady || !TryGetPlayerFirstFrameBlocker(out var blocker) ? "" : blocker;
    var warningBuilder = BeginLoadFlowLog("[SingleSceneManager][LoadingZeroStall]");
    AppendLoadFlowFloat(warningBuilder, "elapsed_s", now - loadingZeroPercentStartedAt);
    AppendLoadFlowField(warningBuilder, "overlay_reason", ResolveLoadFlowValue(SpriteStreamingLoadingState.ActiveReason));
    AppendLoadFlowField(warningBuilder, "current_section", ResolveCurrentSection().ToString());
    AppendLoadFlowField(warningBuilder, "current_location", ResolveLoadFlowValue(LocationManager.currentLocation));
    AppendLoadFlowInt(warningBuilder, "slot", SaveSlotManager.slot);
    AppendLoadFlowFloat(warningBuilder, "target_progress", targetProgress);
    AppendLoadFlowInt(warningBuilder, "actual_percent", actualPercent);
    AppendLoadFlowInt(warningBuilder, "goal_total", loadingProgressGoalTotal);
    AppendLoadFlowInt(warningBuilder, "goal_remaining_best", loadingProgressGoalBestRemaining);
    AppendLoadFlowInt(warningBuilder, "remaining_work", remainingWork);
    AppendLoadFlowInt(warningBuilder, "session_expected", session.expectedTotal);
    AppendLoadFlowInt(warningBuilder, "session_scheduled", session.scheduledTotal);
    AppendLoadFlowInt(warningBuilder, "session_completed", session.completedTotal);
    AppendLoadFlowInt(warningBuilder, "session_total", session.EffectiveTotal);
    AppendLoadFlowInt(warningBuilder, "queue_queued", queue.queuedCount);
    AppendLoadFlowInt(warningBuilder, "queue_in_flight", queue.inFlightCount);
    AppendLoadFlowInt(warningBuilder, "deferred_pending", deferredSnapshot.pendingCount);
    AppendLoadFlowInt(warningBuilder, "outstanding", outstanding);
    AppendLoadFlowBool(warningBuilder, "blocking_mode", usingBlockingProgress);
    AppendLoadFlowInt(warningBuilder, "blocking_ready_count", blockingReadyCount);
    AppendLoadFlowInt(warningBuilder, "blocking_total_count", blockingTotalCount);
    AppendLoadFlowFloat(warningBuilder, "blocking_progress", blockingProgress);
    AppendLoadFlowBool(warningBuilder, "blocking_critical_ready", blockingCriticalReady);
    AppendLoadFlowBool(warningBuilder, "blocking_hard_bypass", blockingHardBypassUsed);
    AppendLoadFlowBool(warningBuilder, "blocking_ready", blockingReady);
    AppendLoadFlowBool(warningBuilder, "resolver_idle", resolverIdle);
    AppendLoadFlowBool(warningBuilder, "player_ready", playerReady);
    AppendLoadFlowField(warningBuilder, "player_blocker", playerReady ? "-" : ResolveLoadFlowValue(blockerSummary));
    AppendLoadFlowBool(warningBuilder, "progress_ui", IsLoadingProgressUiVisible());
    AppendLoadFlowBool(warningBuilder, "loading_light", loadingLightObject != null && loadingLightObject.activeSelf);
    AppendGameplayLoadPipelineFields(warningBuilder);
    Debug.LogWarning(warningBuilder.ToString());
  }

  void MaybeLogStreamingIdleWaitState(
    ref float nextLogAt,
    float elapsed,
    float minimumWaitSeconds,
    float timeoutSeconds,
    int stableFrames,
    int stableFramesRequired,
    TextureResidencyCache.QueueSnapshot queue,
    bool resolverIdle,
    bool playerReady,
    bool queueIdle,
    bool hasBlockingProgress,
    int blockingReadyCount,
    int blockingTotalCount,
    float blockingProgress,
    bool blockingCriticalReady,
    bool blockingHardBypassUsed,
    bool blockingReady,
    bool locationActivationPending,
    bool locationDeferredPending,
    bool warmupDone
  ) {
    if (!ShouldLogLoadFlowDebug()) return;
    var now = Time.realtimeSinceStartup;
    if (nextLogAt < 0f) {
      nextLogAt = now + LoadingWaitStateLogIntervalSeconds;
      return;
    }
    if (now < nextLogAt) return;
    nextLogAt = now + LoadingWaitStateLogIntervalSeconds;
    var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
    var blockerSummary = playerReady || !TryGetPlayerFirstFrameBlocker(out var blocker) ? "" : blocker;
    var playerController = ResolvePlayerAnimationController();
    var infoBuilder = BeginLoadFlowLog("[SingleSceneManager][WaitForIdle]");
    AppendLoadFlowFloat(infoBuilder, "elapsed_s", elapsed);
    AppendLoadFlowFloat(infoBuilder, "min_wait_s", minimumWaitSeconds);
    AppendLoadFlowFloat(infoBuilder, "timeout_s", timeoutSeconds);
    AppendLoadFlowInt(infoBuilder, "stable_frames", stableFrames);
    AppendLoadFlowInt(infoBuilder, "stable_required", stableFramesRequired);
    AppendLoadFlowField(infoBuilder, "overlay_reason", ResolveLoadFlowValue(SpriteStreamingLoadingState.ActiveReason));
    AppendLoadFlowField(infoBuilder, "current_section", ResolveCurrentSection().ToString());
    AppendLoadFlowField(infoBuilder, "current_location", ResolveLoadFlowValue(LocationManager.currentLocation));
    AppendLoadFlowInt(infoBuilder, "current_percent", loadingPercent);
    AppendLoadFlowBool(infoBuilder, "queue_idle", queueIdle);
    AppendLoadFlowBool(infoBuilder, "resolver_idle", resolverIdle);
    AppendLoadFlowBool(infoBuilder, "player_ready", playerReady);
    AppendLoadFlowBool(infoBuilder, "player_animation_held", playerAnimationHeldForLoadingOverlay);
    AppendLoadFlowField(
      infoBuilder,
      "player_animation",
      playerController != null && !string.IsNullOrWhiteSpace(playerController.CurrentAnimation)
        ? playerController.CurrentAnimation.Trim()
        : "-"
    );
    AppendLoadFlowField(infoBuilder, "player_blocker", playerReady ? "-" : ResolveLoadFlowValue(blockerSummary));
    AppendLoadFlowInt(infoBuilder, "queue_queued", queue.queuedCount);
    AppendLoadFlowInt(infoBuilder, "queue_in_flight", queue.inFlightCount);
    AppendLoadFlowInt(infoBuilder, "deferred_pending", deferredPending);
    AppendLoadFlowBool(infoBuilder, "blocking_mode", hasBlockingProgress);
    AppendLoadFlowInt(infoBuilder, "blocking_ready_count", blockingReadyCount);
    AppendLoadFlowInt(infoBuilder, "blocking_total_count", blockingTotalCount);
    AppendLoadFlowFloat(infoBuilder, "blocking_progress", blockingProgress);
    AppendLoadFlowBool(infoBuilder, "blocking_critical_ready", blockingCriticalReady);
    AppendLoadFlowBool(infoBuilder, "blocking_hard_bypass", blockingHardBypassUsed);
    AppendLoadFlowBool(infoBuilder, "blocking_ready", blockingReady);
    AppendLoadFlowBool(infoBuilder, "location_activation_pending", locationActivationPending);
    AppendLoadFlowBool(infoBuilder, "location_deferred_pending", locationDeferredPending);
    AppendLoadFlowBool(infoBuilder, "warmup_done", warmupDone);
    Debug.Log(infoBuilder.ToString());
  }

  static bool ShouldLogLoadingProgressDebug() {
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    if (!SpriteStreamingRuntimeSettings.EnableDiagnostics) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }

  void EnsureLoadingProgressForPhase() {
    if (loadingPercent >= 0) return;
    ResetLoadingProgressForPhase();
  }

  void FinalizeLoadingProgressForRelease() {
    loadingProgressIdleStartedAt = -1f;
    ResetZeroPercentStallDebugState();
    loadingProgressPeakOutstanding = Mathf.Max(loadingProgressPeakOutstanding, 1);
    loadingPercentDisplayInitialized = true;
    loadingPercentDisplayValue = 100f;
    loadingPercent = 100;
    loadingStatusDetail = "Ready";
    loadingStatusOverride = "";
    SetLoadingText(BuildLoadingDisplayText(100, loadingStatusDetail));
  }

  void SetLoadingText(string value) {
    if (loadingText == null) return;
    var textValue = value ?? "";
    if (string.Equals(loadingText.content, textValue, StringComparison.Ordinal)) return;
    loadingText.content = textValue;
    loadingText.Generate();
    if (ShouldLogLoadingProgressDebug()) {
      Debug.Log(
        "[SingleSceneManager][LoadingText] value='" + textValue +
        "' visible=" + (loadingTextObject != null && loadingTextObject.activeSelf ? 1 : 0) +
        " overlay_reason=" + ResolveLoadFlowValue(SpriteStreamingLoadingState.ActiveReason) +
        " current_section=" + ResolveCurrentSection()
      );
    }
  }

  void SetLoadingRootActive(bool active) {
    if (LoadingScreen == null || LoadingScreen.activeSelf == active) return;
    LoadingScreen.SetActive(active);
  }

  void SetLoadingLightActive(bool active) {
    if (loadingLightObject == null || loadingLightObject.activeSelf == active) return;
    loadingLightObject.SetActive(active);
  }

  void SetLoadingProgressUiActive(bool active) {
    if (loadingCircle != null && loadingCircle.activeSelf != active) {
      loadingCircle.SetActive(active);
    }
    if (loadingTextObject != null && loadingTextObject.activeSelf != active) {
      loadingTextObject.SetActive(active);
    }
  }

  bool IsLoadingProgressUiVisible() {
    return (loadingCircle != null && loadingCircle.activeSelf) ||
           (loadingTextObject != null && loadingTextObject.activeSelf);
  }

  void DisableLoadingUiFeedback(bool clearText = true, bool includeLoadingLight = true) {
    loadingUiFeedbackActive = false;
    loadingOverlayChildrenReady = false;
    loadingStatusOverride = "";
    loadingProgressUiArmCheckFrame = -1;
    if (includeLoadingLight) {
      SetLoadingLightActive(false);
    }
    SetLoadingProgressUiActive(false);
    if (clearText) {
      SetLoadingText("");
    }
  }

  void PrepareLoadingScreenCarrier(bool clearText = true) {
    loadingUiFeedbackActive = false;
    loadingOverlayChildrenReady = false;
    loadingProgressUiArmCheckFrame = -1;
    SetLoadingRootActive(true);
    PrimePersistentFontAtlasPins("prepare_loading_screen_carrier");
    PrimeLoadingTextRuntimeAssets("prepare_loading_screen_carrier");
    SetLoadingLightActive(false);
    SetLoadingProgressUiActive(false);
    if (clearText) {
      SetLoadingText("");
    }
  }

  void ReleaseLoadingScreenIfIdle() {
    if (holdBlackscreenOpaqueDuringLoad || SpriteStreamingLoadingState.IsLoadingOverlayActive) return;
    SetLoadingRootActive(false);
  }

  static bool ShouldLogSectionTransitionDebug() {
    return ShouldLogLoadFlowDebug();
  }

  void LogSectionTransitionState(string stage, Section fromSection, Section toSection, string overlayTag, bool showProgressUi) {
    if (!ShouldLogSectionTransitionDebug()) return;
    var loadingRootActive = LoadingScreen != null && LoadingScreen.activeSelf;
    var loadingLightActive = loadingLightObject != null && loadingLightObject.activeSelf;
    var sceneLights = ResolveSceneObjectLights();
    var sceneLightsActive = sceneLights != null && sceneLights.activeSelf;
    Debug.Log(
      "[SingleSceneManager][SectionTransition] stage=" + (string.IsNullOrWhiteSpace(stage) ? "unspecified" : stage.Trim()) +
      " from=" + fromSection +
      " to=" + toSection +
      " overlay=" + (string.IsNullOrWhiteSpace(overlayTag) ? "-" : overlayTag.Trim()) +
      " loading_root=" + (loadingRootActive ? 1 : 0) +
      " loading_light=" + (loadingLightActive ? 1 : 0) +
      " progress_ui=" + (IsLoadingProgressUiVisible() ? 1 : 0) +
      " requested_progress=" + (showProgressUi ? 1 : 0) +
      " black_hold=" + (holdBlackscreenOpaqueDuringLoad ? 1 : 0) +
      " black_visible=" + (loadingBlackscreen != null && loadingBlackscreen.activeInHierarchy ? 1 : 0) +
      " overlay_active=" + (SpriteStreamingLoadingState.IsLoadingOverlayActive ? 1 : 0) +
      " overlay_reason=" + (string.IsNullOrWhiteSpace(SpriteStreamingLoadingState.ActiveReason) ? "-" : SpriteStreamingLoadingState.ActiveReason) +
      " scene_lights=" + (sceneLightsActive ? 1 : 0) +
      " current_section=" + ResolveCurrentSection()
    );
  }

  void InvalidateCachedPlayerGearController(string reason = null) {
    if (cachedPlayerGearController == null) return;
    if (ShouldLogLoadFlowDebug()) {
      Debug.Log(
        "[SingleSceneManager][PlayerResolve] invalidate reason=" + (string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim()) +
        " previous=" + DescribeGearController(cachedPlayerGearController)
      );
    }
    cachedPlayerGearController = null;
    cachedPlayerCharacterState = null;
  }

  string DescribeGearController(GearController controller) {
    if (controller == null) return "player=-";
    var go = controller.gameObject;
    var inSceneRoot = Scene != null && go != null && go.transform.IsChildOf(Scene.transform);
    return "player=" + go.name +
           " enabled=" + (controller.enabled ? 1 : 0) +
           " active=" + (go.activeInHierarchy ? 1 : 0) +
           " scene='" + (go.scene.IsValid() ? go.scene.name : "-") + "'" +
           " in_scene_root=" + (inSceneRoot ? 1 : 0) +
           " appearance_rev=" + controller.AppearanceRevision;
  }

  bool IsPreferredGameplayPlayerController(GearController controller) {
    if (controller == null) return false;
    var go = controller.gameObject;
    if (go == null || !go.scene.IsValid()) return false;
    if ((go.hideFlags & HideFlags.HideAndDontSave) != 0) return false;
    if (!controller.enabled || !go.activeInHierarchy) return false;
    if (Scene != null && Scene.activeInHierarchy) {
      return go.transform.IsChildOf(Scene.transform);
    }
    return true;
  }

  GearController ResolveBestAvailablePlayerController() {
    var activeControllers = FindObjectsByType<GearController>(FindObjectsInactive.Exclude);
    for (var i = 0; i < activeControllers.Length; i++) {
      var candidate = activeControllers[i];
      if (IsPreferredGameplayPlayerController(candidate)) {
        return candidate;
      }
    }

    for (var i = 0; i < activeControllers.Length; i++) {
      var candidate = activeControllers[i];
      if (candidate == null) continue;
      var go = candidate.gameObject;
      if (go == null || !go.scene.IsValid()) continue;
      if ((go.hideFlags & HideFlags.HideAndDontSave) != 0) continue;
      return candidate;
    }

    var all = Resources.FindObjectsOfTypeAll<GearController>();
    GearController fallback = null;
    for (var i = 0; i < all.Length; i++) {
      var candidate = all[i];
      if (candidate == null) continue;
      var go = candidate.gameObject;
      if (go == null || !go.scene.IsValid()) continue;
      if ((go.hideFlags & HideFlags.HideAndDontSave) != 0) continue;
      if (IsPreferredGameplayPlayerController(candidate)) {
        return candidate;
      }
      fallback ??= candidate;
    }
    return fallback;
  }

  void EnsureGameplayPlayerBootstrap(string source) {
    var existing = FindScenePlayerController();
    if (existing != null) {
      EnsureGameplayPlayerEnabled(existing);
      ApplyGameplayPlayerReferences(existing.gameObject, source, instantiated: false);
      return;
    }

    var gameplayPlayerPrefab = ResolveGameplayPlayerBootstrapPrefab(source);
    if (gameplayPlayerPrefab == null) {
      Debug.LogWarning(
        "[SingleSceneManager][PlayerBootstrap] stage=missing_prefab" +
        " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
        " asset_path='" + ResolveGameplayPlayerBootstrapAssetPath() + "'"
      );
      return;
    }

    if (Scene == null) {
      Debug.LogWarning(
        "[SingleSceneManager][PlayerBootstrap] stage=missing_scene_root" +
        " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
        " prefab=" + gameplayPlayerPrefab.name
      );
      return;
    }

    var instance = Instantiate(gameplayPlayerPrefab, Scene.transform, false);
    instance.name = gameplayPlayerPrefab.name;
    var gear = instance.GetComponent<GearController>();
    if (gear == null) {
      Debug.LogWarning(
        "[SingleSceneManager][PlayerBootstrap] stage=missing_gear_controller" +
        " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
        " prefab=" + instance.name
      );
      return;
    }

    EnsureGameplayPlayerEnabled(gear);
    ApplyGameplayPlayerReferences(instance, source, instantiated: true);
  }

  IEnumerator PrewarmGameplayPlayerBootstrapAssets(string source) {
    if (FindScenePlayerController() != null) {
      yield break;
    }

    var gameplayPlayerPrefab = ResolveGameplayPlayerBootstrapPrefab(source);
    if (gameplayPlayerPrefab == null) {
      yield break;
    }

    var gear = gameplayPlayerPrefab.GetComponent<GearController>();
    if (gear == null) {
      yield break;
    }

    playerBootstrapWarmAddressScratch.Clear();
    playerBootstrapWarmSeenAddressScratch.Clear();
    var maxPinnedAddresses = Math.Max(SpriteStreamingRuntimeSettings.PinBudgetPlayerAddresses, 1);
    var collectedCount = gear.CollectBootstrapSkinStartupAddresses(
      playerBootstrapWarmAddressScratch,
      playerBootstrapWarmSeenAddressScratch,
      maxPinnedAddresses
    );
    if (collectedCount <= 0 || playerBootstrapWarmAddressScratch.Count <= 0) {
      playerBootstrapWarmAddressScratch.Clear();
      playerBootstrapWarmSeenAddressScratch.Clear();
      yield break;
    }

    TextureResidencyCache.UpdateOwnerPins(
      PersistentPlayerSkinAtlasPinOwnerId,
      TextureResidencyCache.PinClass.Player,
      playerBootstrapWarmAddressScratch,
      TextureResidencyCache.LoadPriority.Warmup
    );
    QueuePersistentAtlasMetadataWarmup(playerBootstrapWarmAddressScratch);
    yield return TextureResidencyCache.RequestLoadBatchThrottled(
      playerBootstrapWarmAddressScratch,
      TextureResidencyCache.LoadPriority.Warmup,
      allowAtlasExpansion: true,
      enqueueBudgetPerFrame: 96,
      warmGateManaged: false
    );
    yield return WaitForPlayerBootstrapReadiness(source, gear);

    if (ShouldLogLoadingProgressDebug()) {
      var readyCount = CountReadyPlayerBootstrapSamples(gear, out var totalReadySamples);
      Debug.Log(
        "[SingleSceneManager][PlayerBootstrap] stage=prewarm_complete" +
        " source='" + (source ?? "") + "'" +
        " addresses=" + playerBootstrapWarmAddressScratch.Count +
        " ready=" + readyCount +
        "/" + totalReadySamples
      );
    }

    playerBootstrapWarmAddressScratch.Clear();
    playerBootstrapWarmSeenAddressScratch.Clear();
  }

  IEnumerator WaitForPlayerBootstrapReadiness(string source, GearController gear) {
    if (playerBootstrapWarmAddressScratch.Count <= 0) {
      yield break;
    }

    var timeoutSeconds = 1.5f;
    var startedAt = Time.realtimeSinceStartup;
    var readyCount = CountReadyPlayerBootstrapSamples(gear, out var totalSampleCount);
    while (readyCount < totalSampleCount &&
           Time.realtimeSinceStartup - startedAt < timeoutSeconds) {
      SetLoadingStatusOverride("Preparing player");
      yield return null;
      readyCount = CountReadyPlayerBootstrapSamples(gear, out totalSampleCount);
    }
    ClearLoadingStatusOverride();

    if (!ShouldLogLoadingProgressDebug()) {
      yield break;
    }

    Debug.Log(
      "[SingleSceneManager][PlayerBootstrap] stage=prewarm_wait_complete" +
      " source='" + (source ?? "") + "'" +
      " ready=" + readyCount +
      "/" + totalSampleCount +
      " elapsed_ms=" + ((Time.realtimeSinceStartup - startedAt) * 1000f).ToString("0.0")
    );
  }

  int CountReadyPlayerBootstrapSamples(GearController gear, out int totalSampleCount) {
    totalSampleCount = 0;
    if (gear == null) {
      return 0;
    }
    return gear.CountBootstrapSkinStartupReadySamples(out totalSampleCount);
  }

  string ResolveGameplayPlayerBootstrapAssetPath() {
    return ActiveContentRegistryRuntime.ResolveCoreAssetPath(GameplayCoreAssetPaths.EsperanzaPrefabAssetPath);
  }

  GameObject ResolveGameplayPlayerBootstrapPrefab(string source) {
    var resolvedAssetPath = ResolveGameplayPlayerBootstrapAssetPath();
    if (!string.IsNullOrWhiteSpace(resolvedAssetPath)) {
      gameplayPlayerBootstrapPrefabData.assetPath = resolvedAssetPath;
      var resolvedPrefab = gameplayPlayerBootstrapPrefabData.ResolvePrefab();
      if (resolvedPrefab != null) {
        if (ShouldLogLoadFlowDebug()) {
          Debug.Log(
            "[SingleSceneManager][PlayerBootstrap] stage=resolved_prefab" +
            " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
            " asset_path='" + resolvedAssetPath + "'" +
            " prefab='" + resolvedPrefab.name + "'"
          );
        }
        return resolvedPrefab;
      }

      if (ShouldLogLoadFlowWarnings()) {
        Debug.LogWarning(
          "[SingleSceneManager][PlayerBootstrap] stage=resolved_prefab_unavailable" +
          " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
          " asset_path='" + resolvedAssetPath + "'" +
          " fallback_serialized=" + (playerCharacterPrefab != null ? 1 : 0)
        );
      }
    }

    if (playerCharacterPrefab != null && ShouldLogLoadFlowDebug()) {
      Debug.Log(
        "[SingleSceneManager][PlayerBootstrap] stage=fallback_serialized_prefab" +
        " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
        " prefab='" + playerCharacterPrefab.name + "'"
      );
    }

    return playerCharacterPrefab;
  }

  GearController FindScenePlayerController() {
    if (Scene == null) return null;
    var controllers = Scene.GetComponentsInChildren<GearController>(true);
    for (var i = 0; i < controllers.Length; i++) {
      var candidate = controllers[i];
      if (candidate == null) continue;
      var go = candidate.gameObject;
      if (go == null || !go.scene.IsValid()) continue;
      if ((go.hideFlags & HideFlags.HideAndDontSave) != 0) continue;
      return candidate;
    }
    return null;
  }

  void EnsureGameplayPlayerEnabled(GearController gear) {
    if (gear == null) return;

    var root = gear.gameObject;
    if (root != null && !root.activeSelf) {
      root.SetActive(true);
    }

    if (!gear.enabled) {
      gear.enabled = true;
    }

    var characterState = gear.GetComponent<CharacterState>();
    if (characterState != null && !characterState.enabled) {
      characterState.enabled = true;
    }
  }

  CharacterState ResolvePlayerCharacterState() {
    if (IsLiveSceneComponent(cachedPlayerCharacterState)) {
      return cachedPlayerCharacterState;
    }

    cachedPlayerCharacterState = null;
    var player = ResolvePlayerGearController();
    if (player != null) {
      cachedPlayerCharacterState = player.GetComponent<CharacterState>();
      if (IsLiveSceneComponent(cachedPlayerCharacterState)) {
        return cachedPlayerCharacterState;
      }
      cachedPlayerCharacterState = null;
    }

    cachedPlayerCharacterState = FindAnyObjectByType<CharacterState>();
    return cachedPlayerCharacterState;
  }

  GameObject ResolveGameplayPlayerRootInternal() {
    var player = ResolvePlayerGearController();
    if (player != null && IsLiveSceneObject(player.gameObject)) {
      return player.gameObject;
    }

    var characterState = ResolvePlayerCharacterState();
    if (IsLiveSceneComponent(characterState)) {
      return characterState.gameObject;
    }

    return null;
  }

  void ApplyGameplayPlayerReferences(GameObject playerRoot, string source, bool instantiated) {
    if (playerRoot == null) return;

    var gear = playerRoot.GetComponent<GearController>();
    var characterState = playerRoot.GetComponent<CharacterState>();
    var sharedProjectileManager = ResolveGameplayProjectileManagerInternal();
    if (gear != null) {
      if (sharedProjectileManager != null) {
        gear.projectileManager = sharedProjectileManager;
      }
      cachedPlayerGearController = gear;
    }
    cachedPlayerCharacterState = characterState;

    var gameplayInput = FindAnyObjectByType<GameplayInput>();
    if (gameplayInput != null) {
      gameplayInput.ApplyPlayerBootstrap(playerRoot, gear, characterState);
    }

    if (autoSaver != null && characterState != null) {
      autoSaver.characterState = characterState;
    }

    cachedGameplayInput = gameplayInput;
    gameplayInputCacheRefreshedAt = -1f;
    RefreshPersistentPlayerBaselineAtlasPins(string.IsNullOrWhiteSpace(source) ? "player_bootstrap_ready" : source + "_player_bootstrap_ready");

    if (!ShouldLogLoadFlowDebug()) return;
    Debug.Log(
      "[SingleSceneManager][PlayerBootstrap] stage=ready" +
      " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
      " action=" + (instantiated ? "instantiate" : "reuse") +
      " player=" + playerRoot.name +
      " gameplay_input=" + (gameplayInput != null ? 1 : 0) +
      " character_state=" + (characterState != null ? 1 : 0) +
      " projectile_manager=" + (sharedProjectileManager != null ? 1 : 0) +
      " parent=" + (playerRoot.transform.parent != null ? playerRoot.transform.parent.name : "-")
    );
  }

  int ResolveSpriteReadinessFrame(SpriteWithNormals sprite) {
    if (sprite == null) return 1;
    if (!sprite.IsAnimation) return 0;
    return Mathf.Max(sprite.LastRequestedFrame, 1);
  }

  bool TryDescribeFirstUnreadySprite(GameObject[] objects, string groupName, out string blockerSummary) {
    if (objects != null) {
      for (var i = 0; i < objects.Length; i++) {
        var go = objects[i];
        if (go == null) continue;
        var sprite = go.GetComponent<SpriteWithNormals>();
        if (sprite == null || !sprite.isActiveAndEnabled || sprite.DoNotRender) continue;
        var frame = ResolveSpriteReadinessFrame(sprite);
        if (sprite.IsFrameReady(frame, out var colorReadyOnly)) continue;
        blockerSummary =
          "group=" + groupName +
          " sprite=" + go.name +
          " lib=" + (string.IsNullOrWhiteSpace(sprite.libraryName) ? "-" : sprite.libraryName) +
          " label=" + (string.IsNullOrWhiteSpace(sprite.labelPrefix) ? "-" : sprite.labelPrefix) +
          " category=" + (string.IsNullOrWhiteSpace(sprite.category) ? "-" : sprite.category) +
          " frame=" + frame +
          " color_only=" + (colorReadyOnly ? 1 : 0) +
          " active=" + (go.activeInHierarchy ? 1 : 0);
        return true;
      }
    }

    blockerSummary = "";
    return false;
  }

  bool TryGetPlayerFirstFrameBlocker(out string blockerSummary) {
    var player = ResolvePlayerGearController();
    if (player == null) {
      blockerSummary = "player=-";
      return false;
    }

    if (Scene != null && Scene.activeInHierarchy && !player.gameObject.activeInHierarchy) {
      blockerSummary = DescribeGearController(player) + " inactive_under_scene";
      return true;
    }

    if (TryDescribeFirstUnreadySprite(player.SkinObjects, "skin", out var skinBlocker)) {
      blockerSummary = DescribeGearController(player) + " " + skinBlocker;
      return true;
    }
    if (TryDescribeFirstUnreadySprite(player.GearObjects, "gear", out var gearBlocker)) {
      blockerSummary = DescribeGearController(player) + " " + gearBlocker;
      return true;
    }

    blockerSummary = DescribeGearController(player) + " ready";
    return false;
  }

  void RotateLoadingCircle() {
    if (loadingCircle == null || !loadingCircle.activeInHierarchy) return;
    var spinSpeed = Mathf.Max(loadingCircleSpinSpeedDegreesPerSecond, 0f);
    if (spinSpeed <= 0f) return;
    var dt = Mathf.Max(Time.unscaledDeltaTime, 0f);
    if (dt <= 0f) return;
    loadingCircle.transform.Rotate(0f, 0f, -spinSpeed * dt, Space.Self);
  }

  void LateUpdate() {
    if (!holdBlackscreenOpaqueDuringLoad) return;
    ForceBlackscreenVisible(true);
  }

  void FixedUpdate() {
    TickLoadingStallEmergencyUnlock();
  }

  void StartGame() {
    ClearPauseDialogResumeState("start_game");
    ReleasePreUnlockResidentPins("start_game");
    StopStartupFadeWatchdog();
    StopStartupGameplayFlow();
    StopSectionTransition(clearLoadingOverlay: true, restoreVisibleState: true);
    InvalidateCachedPlayerGearController("start_game");
    SaveData loadedSlot = null;
    var isNewGame = _isNewGame();
    if (!isNewGame) {
      loadedSlot = SaveSlotManager.Load("slot");
      if (loadedSlot != null && loadedSlot.ContainsKey("playtimeHours") && loadedSlot.ContainsKey("playtimeMinutes") && loadedSlot.ContainsKey("playtimeSeconds")) {
        autoSaver.SetPlaytime((int)loadedSlot["playtimeHours"], (int)loadedSlot["playtimeMinutes"], (int)loadedSlot["playtimeSeconds"]);
      }
    }
    LogStartGameRequest(isNewGame, loadedSlot);
    autoSaver.enableTimeTracking = true;
    _SwitchMap("none");
    if (resumeGameplayRoutine != null) {
      StopCoroutine(resumeGameplayRoutine);
      resumeGameplayRoutine = null;
      SpriteStreamingLoadingState.ForceClearLoadingOverlay();
    }
    if (startGameRoutine != null) {
      StopCoroutine(startGameRoutine);
      startGameRoutine = null;
      SpriteStreamingLoadingState.ForceClearLoadingOverlay();
    }
    startGameRoutine = StartCoroutine(StartGameFlowRoutine(isNewGame, loadedSlot));
  }

  void OpenLoadMenu() {
    ClearPauseDialogResumeState("open_load_menu");
    QueueMenuRuntimeAssetWarmup("open_load_menu");
    SwitchSectionInstantly(Section.LoadMenu, "open_load_menu");
  }

  void CloseLoadMenu() {
    if (ResolveCurrentSection() != Section.LoadMenu) return;
    SwitchSectionInstantly(Section.MainMenu, "close_load_menu");
  }

  void OpenSettingsMenu() {
    var openedFromPause = ResolveCurrentSection() == Section.Pause;
    if (!openedFromPause) {
      ClearPauseDialogResumeState("open_settings_menu");
    }
    QueueMenuRuntimeAssetWarmup("open_settings_menu");
    PrepareSettingsMenuState(openedFromPause ? Section.Pause : Section.MainMenu);
    Debug.Log(
      "[SingleSceneManager][SettingsMenu] action=open" +
      " from=" + ResolveCurrentSection() +
      " return_target=" + settingsReturnTarget +
      " instant_switch=1"
    );
    SwitchSectionInstantly(Section.SettingsMenu, "open_settings_menu");
  }

  void CloseSettingsMenu() {
    if (SettingsMenu != null && !SettingsMenu.activeInHierarchy) return;
    var targetSection = settingsReturnTarget == Section.Pause ? Section.Pause : Section.MainMenu;
    Debug.Log(
      "[SingleSceneManager][SettingsMenu] action=close" +
      " to=" + targetSection +
      " instant_switch=1"
    );
    SwitchSectionInstantly(targetSection, "close_settings_menu");
  }

  void OnSettingsMenuClick(object payload) {
    var target = payload as GameObject;
    if (!IsSettingsCloseTarget(target)) return;
    CloseSettingsMenu();
  }

  void OnSettingsMenuHover(object payload) {
    settingsHoveredTarget = payload as GameObject;
  }

  void OnSettingsMenuUnhover() {
    settingsHoveredTarget = null;
  }

  void OnSettingsMenuSelect() {
    if (!IsSettingsCloseTarget(settingsHoveredTarget)) return;
    CloseSettingsMenu();
  }

  bool IsSettingsCloseTarget(GameObject target) {
    if (target == null) return false;

    var closeTarget = ResolveSettingsCloseButton();
    if (closeTarget != null) {
      var targetTransform = target.transform;
      var closeTransform = closeTarget.transform;
      return target == closeTarget ||
             targetTransform.IsChildOf(closeTransform) ||
             closeTransform.IsChildOf(targetTransform);
    }

    var current = target.transform;
    while (current != null) {
      if (string.Equals(current.name, "Close", StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
      current = current.parent;
    }

    return false;
  }

  GameObject ResolveSettingsCloseButton() {
    if (settingsCloseButton != null) return settingsCloseButton;
    if (SettingsMenu == null) return null;

    var found = FindChildByName(SettingsMenu.transform, "Close");
    if (found != null) {
      settingsCloseButton = found.gameObject;
    }
    return settingsCloseButton;
  }

  void PrepareSettingsMenuState(Section returnTarget) {
    settingsReturnTarget = returnTarget == Section.Pause ? Section.Pause : Section.MainMenu;
    settingsHoveredTarget = null;
    settingsCloseButton = null;
  }

  Transform FindChildByName(Transform root, string name) {
    if (root == null || string.IsNullOrWhiteSpace(name)) return null;

    findChildScratch.Clear();
    findChildScratch.Push(root);

    while (findChildScratch.Count > 0) {
      var current = findChildScratch.Pop();
      if (string.Equals(current.name, name, StringComparison.OrdinalIgnoreCase)) {
        findChildScratch.Clear();
        return current;
      }

      for (var i = 0; i < current.childCount; i++) {
        findChildScratch.Push(current.GetChild(i));
      }
    }

    return null;
  }

  static SectionDescriptor GetSectionDescriptor(Section section) {
    switch (section) {
      case Section.MainMenu:
        return new SectionDescriptor("mainMenu", sceneActiveByDefault: false, restoreSceneLightsByDefault: false, resetPauseAppearanceRevision: true);
      case Section.LoadMenu:
        return new SectionDescriptor("loadMenu", sceneActiveByDefault: false, restoreSceneLightsByDefault: false, resetPauseAppearanceRevision: false);
      case Section.SettingsMenu:
        return new SectionDescriptor("settingsMenu", sceneActiveByDefault: false, restoreSceneLightsByDefault: false, resetPauseAppearanceRevision: false);
      case Section.Gameplay:
        return new SectionDescriptor("gameplay", sceneActiveByDefault: true, restoreSceneLightsByDefault: true, resetPauseAppearanceRevision: true);
      case Section.Pause:
        return new SectionDescriptor("pauseMenu", sceneActiveByDefault: true, restoreSceneLightsByDefault: false, resetPauseAppearanceRevision: false);
      default:
        return new SectionDescriptor("", sceneActiveByDefault: false, restoreSceneLightsByDefault: false, resetPauseAppearanceRevision: false);
    }
  }

  Section ResolveActiveSectionFromHierarchy() {
    if (PauseMenu != null && PauseMenu.activeInHierarchy) return Section.Pause;
    if (SettingsMenu != null && SettingsMenu.activeInHierarchy) return Section.SettingsMenu;
    if (LoadMenu != null && LoadMenu.activeInHierarchy) return Section.LoadMenu;
    if (MainMenu != null && MainMenu.activeInHierarchy) return Section.MainMenu;
    if (GameplayInterface != null && GameplayInterface.activeInHierarchy) return Section.Gameplay;
    return Section.None;
  }

  Section ResolveCurrentSection() {
    var activeSection = ResolveActiveSectionFromHierarchy();
    if (activeSection != Section.None) {
      currentSection = activeSection;
      return activeSection;
    }
    return currentSection;
  }

  bool ShouldSectionKeepSceneActive(Section section) {
    var descriptor = GetSectionDescriptor(section);
    if (section == Section.SettingsMenu && settingsReturnTarget == Section.Pause) {
      return true;
    }
    return descriptor.sceneActiveByDefault;
  }

  bool ShouldRestoreSceneLightsForSection(Section section) {
    var descriptor = GetSectionDescriptor(section);
    if (section == Section.SettingsMenu && settingsReturnTarget == Section.Pause) {
      return true;
    }
    return descriptor.restoreSceneLightsByDefault;
  }

  void HideAllSectionsForTransition(Section targetSection) {
    SetActiveSafe(MainMenu, false);
    SetActiveSafe(LoadMenu, false);
    SetActiveSafe(SettingsMenu, false);
    SetActiveSafe(GameplayInterface, false);
    SetActiveSafe(PauseMenu, false);
    SetActiveSafe(Scene, ShouldSectionKeepSceneActive(targetSection));
  }

  void ApplySectionActivation(Section section) {
    currentSection = section;
    pendingRevealSection = Section.None;
    ApplySceneTimeForSection(section, "apply_section_activation");
    HideAllSectionsForTransition(section);
    if (ShouldSectionKeepSceneActive(section)) {
      EnsureDamageEntitiesRootEnabled("apply_section_activation:" + section);
    }

    switch (section) {
      case Section.MainMenu:
        SetActiveSafe(MainMenu, true);
        break;
      case Section.LoadMenu:
        SetActiveSafe(LoadMenu, true);
        break;
      case Section.SettingsMenu:
        SetActiveSafe(SettingsMenu, true);
        break;
      case Section.Gameplay:
        SetActiveSafe(GameplayInterface, true);
        pauseMenuOpenAppearanceRevision = -1;
        break;
      case Section.Pause:
        SetActiveSafe(PauseMenu, true);
        break;
    }

    if (GetSectionDescriptor(section).resetPauseAppearanceRevision) {
      pauseMenuOpenAppearanceRevision = -1;
    }
  }

  void ApplySceneTimeForSection(Section section, string reason) {
    var shouldFreezeSceneTime = ShouldFreezeSceneTimeForSection(section);
    TimeScale.SetSceneMultiplier(shouldFreezeSceneTime ? 0f : 1f, reason + ":" + section);
  }

  bool ShouldFreezeSceneTimeForSection(Section section) {
    return section == Section.Pause ||
      (section == Section.SettingsMenu && settingsReturnTarget == Section.Pause) ||
      (section == Section.Gameplay && dialogInputOverrideActive);
  }

  void RefreshSceneTimeForCurrentUiState(string reason) {
    var section = ResolveCurrentSection();
    if (section == Section.None) {
      section = currentSection;
    }

    ApplySceneTimeForSection(section, reason);
  }

  void ApplyInputForSection(Section section) {
    var inputMap = ResolveInputMapForSection(section);
    if (string.IsNullOrWhiteSpace(inputMap)) return;
    _SwitchMap(inputMap);
  }

  string ResolveInputMapForSection(Section section) {
    var inputMap = GetSectionDescriptor(section).inputMap;
    if (section == Section.Gameplay) {
      return ResolveGameplayInputMap();
    }
    return inputMap;
  }

  string ResolveGameplayInputMap() {
    if (dialogInputOverrideActive) {
      return "dialog";
    }

    var dialogController = ResolveGameplayDialogController();
    if (dialogController != null && dialogController.HasPendingLocationDialog) {
      return "none";
    }

    return GetSectionDescriptor(Section.Gameplay).inputMap;
  }

  GameObject ResolveSceneObjectLights() {
    if (sceneObjectLights != null) return sceneObjectLights;
    if (Scene == null) return null;
    var lights = FindChildByName(Scene.transform, "SCENEOBJECT LIGHTS");
    if (lights == null) return null;
    sceneObjectLights = lights.gameObject;
    return sceneObjectLights;
  }

  GameObject ResolveDamageEntitiesRoot() {
    if (IsLiveSceneObject(damageEntitiesRoot)) {
      return damageEntitiesRoot;
    }

    damageEntitiesRoot = null;
    if (Scene == null) {
      return null;
    }

    var sceneObjectsRoot = FindChildByName(Scene.transform, "SCENEOBJECTS");
    var damageEntities = sceneObjectsRoot != null
      ? FindChildByName(sceneObjectsRoot, "DAMAGEENTITIES")
      : FindChildByName(Scene.transform, "DAMAGEENTITIES");
    if (damageEntities == null) {
      return null;
    }

    damageEntitiesRoot = damageEntities.gameObject;
    return damageEntitiesRoot;
  }

  void EnsureDamageEntitiesRootEnabled(string source) {
    var damageRoot = ResolveDamageEntitiesRoot();
    if (damageRoot == null) {
      return;
    }

    var rootWasInactive = !damageRoot.activeSelf;
    if (rootWasInactive) {
      damageRoot.SetActive(true);
    }

    var manager = gameplayProjectileManager;
    if (!IsLiveSceneComponent(manager)) {
      manager = damageRoot.GetComponent<ProjectileManager>();
      if (manager == null) {
        manager = damageRoot.GetComponentInChildren<ProjectileManager>(true);
      }
      gameplayProjectileManager = manager;
    }

    var managerWasDisabled = manager != null && !manager.enabled;
    if (managerWasDisabled) {
      manager.enabled = true;
    }

    if ((rootWasInactive || managerWasDisabled) && ShouldLogLoadFlowDebug()) {
      Debug.Log(
        "[SingleSceneManager][ProjectileManager] stage=ensure_damage_root_enabled" +
        " source=" + ResolveLoadFlowValue(source) +
        " root_active=" + (damageRoot.activeSelf ? 1 : 0) +
        " scene_active=" + (Scene != null && Scene.activeInHierarchy ? 1 : 0) +
        " manager=" + (manager != null ? manager.gameObject.name : "-") +
        " manager_enabled=" + (manager != null && manager.enabled ? 1 : 0)
      );
    }
  }

  ProjectileManager ResolveGameplayProjectileManagerInternal() {
    EnsureDamageEntitiesRootEnabled("resolve_projectile_manager");
    if (IsLiveSceneComponent(gameplayProjectileManager)) {
      return gameplayProjectileManager;
    }

    var previousManager = gameplayProjectileManager;
    gameplayProjectileManager = null;
    var damageRoot = ResolveDamageEntitiesRoot();
    if (damageRoot != null) {
      gameplayProjectileManager = damageRoot.GetComponent<ProjectileManager>();
      if (gameplayProjectileManager == null) {
        gameplayProjectileManager = damageRoot.GetComponentInChildren<ProjectileManager>(true);
      }
    }

    if (gameplayProjectileManager == null && Scene != null) {
      gameplayProjectileManager = Scene.GetComponentInChildren<ProjectileManager>(true);
    }

    if (gameplayProjectileManager != null &&
        !ReferenceEquals(previousManager, gameplayProjectileManager) &&
        ShouldLogLoadFlowDebug()) {
      Debug.Log(
        "[SingleSceneManager][ProjectileManager] stage=resolved" +
        " manager=" + gameplayProjectileManager.gameObject.name +
        " root=" + (damageRoot != null ? damageRoot.name : "-")
      );
    }

    return gameplayProjectileManager;
  }

  static bool IsLiveSceneObject(GameObject candidate) {
    return candidate != null &&
           candidate.scene.IsValid() &&
           (candidate.hideFlags & HideFlags.HideAndDontSave) == 0;
  }

  static bool IsLiveSceneComponent(Component candidate) {
    return candidate != null && IsLiveSceneObject(candidate.gameObject);
  }

  Spawner ResolveGameplaySpawner() {
    if (IsLiveSceneComponent(cachedSpawner)) {
      return cachedSpawner;
    }

    cachedSpawner = null;
    if (Scene != null) {
      cachedSpawner = Scene.GetComponentInChildren<Spawner>(true);
    }
    if (cachedSpawner == null) {
      cachedSpawner = FindAnyObjectByType<Spawner>();
    }
    return cachedSpawner;
  }

  void SetSceneObjectLightsActive(bool active) {
    var lights = ResolveSceneObjectLights();
    if (lights == null || lights.activeSelf == active) return;
    lights.SetActive(active);
  }

  void RestoreSceneLightingForCurrentActivation() {
    // Safety net for aborted/forced transitions that skip the normal fade-out completion.
    var section = ResolveActiveSectionFromHierarchy();
    if (section == Section.None) {
      section = currentSection;
    }
    var shouldEnableLights = Scene != null &&
                             Scene.activeInHierarchy &&
                             ShouldRestoreSceneLightsForSection(section);
    SetSceneObjectLightsActive(shouldEnableLights);
  }

  void OpenGameplay() {
    ReleasePreUnlockResidentPins("open_gameplay");
    StopStartupFadeWatchdog();
    StopStartupGameplayFlow();
    StopSectionTransition(clearLoadingOverlay: true, restoreVisibleState: true);
    if (startGameRoutine != null) return;
    if (resumeGameplayRoutine != null) {
      StopCoroutine(resumeGameplayRoutine);
      resumeGameplayRoutine = null;
      SpriteStreamingLoadingState.ForceClearLoadingOverlay();
    }

    if (ShouldWarmGearReturn()) {
      resumeGameplayRoutine = StartCoroutine(ResumeGameplayFlowRoutine());
      return;
    }

    StartSectionTransition(new SectionTransitionRequest(
      Section.Gameplay,
      BuildSectionOverlayTag(Section.Gameplay),
      requestMainMenuLocation: false,
      waitForStreamingIdle: false,
      showProgressUi: false,
      switchInputMapToNone: true
    ));
  }

  void OpenMainMenu() {
    ClearPauseDialogResumeState("open_main_menu");
    ReleasePreUnlockResidentPins("open_main_menu");
    RuntimeAssetCache.ClearSessionScope("open_main_menu");
    QueueMenuRuntimeAssetWarmup("open_main_menu", includeLocationProfile: false);
    StartSectionTransition(new SectionTransitionRequest(
      Section.MainMenu,
      BuildSectionOverlayTag(Section.MainMenu),
      requestMainMenuLocation: true,
      waitForStreamingIdle: true,
      showProgressUi: true,
      switchInputMapToNone: true
    ));
  }

  void OpenPauseMenu() {
    var gear = ResolvePlayerGearController();
    pauseMenuOpenAppearanceRevision = gear != null ? gear.AppearanceRevision : -1;
    QueueMenuRuntimeAssetWarmup("open_pause_menu");
    if (dialogInputOverrideActive) {
      pendingPauseDialogResumeToken = 0;
      activePauseDialogResumeToken = nextPauseDialogResumeToken++;
      if (ShouldLogPauseDialogResumeDebug()) {
        Debug.Log(
          "[SingleSceneManager][PauseDialogResume] suspend_token=" + activePauseDialogResumeToken +
          " section=" + ResolveCurrentSection()
        );
      }
    }
    SwitchSectionInstantly(Section.Pause, "open_pause_menu");
  }

  void ClosePauseMenu() {
    if (ResolveCurrentSection() != Section.Pause) return;
    pendingPauseDialogResumeToken = activePauseDialogResumeToken;
    activePauseDialogResumeToken = 0;
    if (pendingPauseDialogResumeToken > 0 && ShouldLogPauseDialogResumeDebug()) {
      Debug.Log(
        "[SingleSceneManager][PauseDialogResume] resume_token=" + pendingPauseDialogResumeToken +
        " section=" + ResolveCurrentSection()
      );
    }
    SwitchSectionInstantly(Section.Gameplay, "close_pause_menu");
  }

  void OnDialogStarted(object payload) {
    dialogInputOverrideActive = true;
    RefreshSceneTimeForCurrentUiState("dialog_started");
    if (ShouldLogGameplayRuntimeDebug()) {
      Debug.Log(
        "[SingleSceneManager][DialogInput] active=1 source='" + (payload != null ? payload.ToString() : "") +
        "' current_section=" + ResolveCurrentSection() +
        " scene_multiplier=" + TimeScale.GetSceneMultiplier()
      );
    }
    ApplyInputMapForCurrentUiState(preferGameplayWhenNoUi: false);
  }

  void OnDialogFinished(object payload) {
    dialogInputOverrideActive = false;
    RefreshSceneTimeForCurrentUiState("dialog_finished");
    if (ShouldLogGameplayRuntimeDebug()) {
      Debug.Log(
        "[SingleSceneManager][DialogInput] active=0 source='" + (payload != null ? payload.ToString() : "") +
        "' current_section=" + ResolveCurrentSection() +
        " scene_multiplier=" + TimeScale.GetSceneMultiplier()
      );
    }
    ApplyInputMapForCurrentUiState(preferGameplayWhenNoUi: false);
  }

  void StartSectionTransition(SectionTransitionRequest request) {
    if (request.targetSection == Section.None) return;
    StopStartupFadeWatchdog();
    StopStartupGameplayFlow();
    StopSectionTransition(clearLoadingOverlay: true, restoreVisibleState: true);
    if (request.targetSection != Section.Gameplay) {
      ResetGameplayLoadStageTracking();
    }

    var current = ResolveCurrentSection();
    if (current == request.targetSection && !request.requestMainMenuLocation) {
      ApplySectionActivation(request.targetSection);
      ApplyInputForSection(request.targetSection);
      return;
    }

    pendingRevealSection = request.targetSection;
    sectionTransitionRoutine = StartCoroutine(SwitchSectionRoutine(request));
  }

  void StopSectionTransition(bool clearLoadingOverlay = true, bool restoreVisibleState = true) {
    if (sectionTransitionRoutine != null) {
      StopCoroutine(sectionTransitionRoutine);
      sectionTransitionRoutine = null;
    }
    if (unlockFadeFailSafeRoutine != null) {
      StopCoroutine(unlockFadeFailSafeRoutine);
      unlockFadeFailSafeRoutine = null;
    }

    DisableLoadingUiFeedback(clearText: true, includeLoadingLight: true);
    loadingStallStartedAt = -1f;
    pendingRevealSection = Section.None;

    if (clearLoadingOverlay) {
      SpriteStreamingLoadingState.ForceClearLoadingOverlay();
    }

    if (restoreVisibleState) {
      SetLoadingBlackscreenHold(false);
      ForceBlackscreenVisible(false);
      RestoreSceneLightingForCurrentActivation();
      ReleaseLoadingScreenIfIdle();
    }
  }

  void SwitchSectionInstantly(Section targetSection, string reason) {
    if (targetSection == Section.None) return;
    if (IsLoadingFlowActive()) {
      LogSectionTransitionState("instant_switch_skipped", ResolveCurrentSection(), targetSection, reason, false);
      return;
    }

    var previousSection = ResolveCurrentSection();
    StopSectionTransition(clearLoadingOverlay: false, restoreVisibleState: false);
    if (targetSection != Section.Gameplay) {
      ResetGameplayLoadStageTracking();
    }
    ApplySectionActivation(targetSection);
    ApplyInputForSection(targetSection);
    LogSectionTransitionState("instant_switch_complete", previousSection, targetSection, reason, false);
  }

  string BuildSectionOverlayTag(Section section) {
    return "Section_" + section;
  }

  void HandleDebugMainMenuShortcut() {
    if (!IsDebugMainMenuShortcutPressed()) return;
    var current = ResolveCurrentSection();
    if (ShouldLogLoadFlowDebug()) {
      Debug.Log(
        "[SingleSceneManager][DebugShortcut] action=return_to_main_menu" +
        " section=" + current +
        " scene_active=" + (Scene != null && Scene.activeInHierarchy ? 1 : 0) +
        " shift_pressed=1 escape_pressed=1"
      );
    }
    OpenMainMenu();
  }

  bool IsDebugMainMenuShortcutPressed() {
    if (IsLoadingFlowActive()) return false;
    var current = ResolveCurrentSection();
    if (current != Section.Gameplay && current != Section.Pause) return false;
    if (Scene == null || !Scene.activeInHierarchy) return false;

    var keyboard = Keyboard.current;
    if (keyboard == null) return false;
    if (!keyboard.escapeKey.wasPressedThisFrame) return false;
    return keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
  }

  IEnumerator SwitchSectionRoutine(SectionTransitionRequest request) {
    var previousSection = ResolveCurrentSection();
    var overlayTag = string.IsNullOrWhiteSpace(request.overlayTag)
      ? BuildSectionOverlayTag(request.targetSection)
      : request.overlayTag.Trim();

    LogSectionTransitionState("begin", previousSection, request.targetSection, overlayTag, request.showProgressUi);
    SpriteStreamingLoadingState.BeginLoadingOverlay(overlayTag);
    SpriteRuntimeResolver.WarmupLibraries(Array.Empty<string>());
    ResetLoadingProgressForPhase(force: true);
    if (request.switchInputMapToNone) {
      _SwitchMap("none");
    }

    yield return FadeToBlackBeforeLoadRoutine();

    HideAllSectionsForTransition(request.targetSection);
    LogSectionTransitionState("opaque_previous_hidden", previousSection, request.targetSection, overlayTag, request.showProgressUi);

    SetLoadingLightActive(request.showLoadingLight);
    if (request.requestMainMenuLocation) {
      RequestLocationLoadForMainMenu();
    }

    if (request.showProgressUi) {
      BeginLoadingProgressUiAfterFadeIn();
    }
    else {
      DisableLoadingUiFeedback(clearText: true, includeLoadingLight: false);
    }

    LogSectionTransitionState("loading_phase", previousSection, request.targetSection, overlayTag, request.showProgressUi);

    if (request.waitForStreamingIdle) {
      yield return WaitForStreamingIdleBeforeUnlock();
    }

    if (request.showProgressUi) {
      FinalizeLoadingProgressForRelease();
    }
    SetLoadingLightActive(false);
    DisableLoadingUiFeedback(clearText: true, includeLoadingLight: false);
    ApplySectionActivation(request.targetSection);
    ApplyInputForSection(request.targetSection);
    LogSectionTransitionState("ready_to_reveal", previousSection, request.targetSection, overlayTag, request.showProgressUi);

    yield return FadeFromBlackRoutine(overlayTag, request.targetSection);
    sectionTransitionRoutine = null;
  }

  private void _SwitchMap(string map) {
    if (string.IsNullOrWhiteSpace(map)) return;
    if (string.Equals(activeInputMap, map, StringComparison.Ordinal)) return;
    activeInputMap = map;
    if (inputProcessor != null) inputProcessor.SwitchMap(map);
    if (mouseManager != null) mouseManager.SwitchMap(map);
  }

  private bool _isNewGame() {
    return !SaveSlotManager.CurrentSlotExists();
  }

  IEnumerator StartGameFlowRoutine(bool isNewGame, SaveData loadedSlot) {
    var context = isNewGame ? WarmGateMode.StartGame : WarmGateMode.LoadSave;
    var overlayTag = isNewGame ? "StartGameFlow" : "LoadGameFlow";
    yield return RunGameplayFlowRoutine(
      overlayTag: overlayTag,
      warmContext: context,
      warmTimeoutSeconds: startWarmTimeoutSeconds,
      warmRequiredRatio: startWarmRequiredRatio,
      applyGameplayStateBeforeWarmGate: true,
      sendReadyForSpawns: true,
      switchInputMapToNone: false,
      resolveLocationForStart: true,
      isNewGame: isNewGame,
      loadedSlot: loadedSlot
    );
    startGameRoutine = null;
  }

  IEnumerator ResumeGameplayFlowRoutine() {
    yield return RunGameplayFlowRoutine(
      overlayTag: "ResumeGameplayFlow",
      warmContext: WarmGateMode.GearApplyReturn,
      warmTimeoutSeconds: gearReturnWarmTimeoutSeconds,
      warmRequiredRatio: gearReturnRequiredRatio,
      applyGameplayStateBeforeWarmGate: false,
      sendReadyForSpawns: false,
      switchInputMapToNone: true,
      resolveLocationForStart: false
    );
    resumeGameplayRoutine = null;
  }

  IEnumerator StartupGameplayFlowRoutine() {
    yield return RunGameplayFlowRoutine(
      overlayTag: "StartupGameplayFlow",
      warmContext: WarmGateMode.StartGame,
      warmTimeoutSeconds: startWarmTimeoutSeconds,
      warmRequiredRatio: startWarmRequiredRatio,
      applyGameplayStateBeforeWarmGate: true,
      sendReadyForSpawns: true,
      switchInputMapToNone: true,
      resolveLocationForStart: startupInDebugGameplay
    );
    startupGameplayRoutine = null;
  }

  IEnumerator RunGameplayFlowRoutine(
    string overlayTag,
    WarmGateMode warmContext,
    float warmTimeoutSeconds,
    float warmRequiredRatio,
    bool applyGameplayStateBeforeWarmGate,
    bool sendReadyForSpawns,
    bool switchInputMapToNone,
    bool resolveLocationForStart,
    bool isNewGame = true,
    SaveData loadedSlot = null
  ) {
    var previousSection = ResolveCurrentSection();
    var gameplayStateAppliedUnderBlack = false;
    pendingGameplayLocationId = "";
    ResetGameplayLoadStageTracking();
    StopDeferredPostRevealWarmup("begin_gameplay_flow");
    pendingRevealSection = Section.Gameplay;
    LogSectionTransitionState("begin", previousSection, Section.Gameplay, overlayTag, true);
    SpriteStreamingLoadingState.BeginLoadingOverlay(overlayTag);
    // Start resolver manifest work as soon as the overlay appears so shard warmup can
    // overlap the fade/black window instead of waiting for the first sprite request.
    SpriteRuntimeResolver.WarmupLibraries(Array.Empty<string>());
    ResetLoadingProgressForPhase(force: true);
    if (switchInputMapToNone) {
      _SwitchMap("none");
    }
    yield return FadeToBlackBeforeLoadRoutine();
    SetLoadingLightActive(true);
    BeginLoadingProgressUiAfterFadeIn();
    LogSectionTransitionState("loading_phase", previousSection, Section.Gameplay, overlayTag, true);

    if (resolveLocationForStart) {
      ResolveAndApplyLocationForStart(isNewGame, loadedSlot);
    }

    yield return PrewarmGameplayPlayerBootstrapAssets(overlayTag);
    EnsureGameplayPlayerBootstrap(overlayTag);

    if (!isNewGame) {
      if (applyGameplayStateBeforeWarmGate && !gameplayStateAppliedUnderBlack) {
        ApplyGameplayStateUnderBlack();
        gameplayStateAppliedUnderBlack = true;
      }
      LogLoadStateDispatch("before_load_game_message");
      ApplySavedGameplayStateUnderLoadingOverlay();
      LogLoadStateDispatch("after_load_game_message");
      yield return null;
      LogLoadStateDispatch("after_load_game_one_frame");
    }

    if (applyGameplayStateBeforeWarmGate) {
      if (!gameplayStateAppliedUnderBlack) {
        ApplyGameplayStateUnderBlack();
        gameplayStateAppliedUnderBlack = true;
      }
      if (isNewGame) {
        PrepareNewGameRuntimeStateUnderLoadingOverlay();
      }
      // Freeze the player on a stable first-contact frame while the overlay is up.
      // Otherwise the runtime loader chases advancing animation frames behind black.
      HoldPlayerAnimationForLoadingOverlay("warm_gate");
      yield return WaitForGameplayWarmGatePrerequisites(sendReadyForSpawns);
    }

    var allowGameplayUnlock = true;
    yield return RunScenarioWarmGate(
      warmContext,
      warmTimeoutSeconds,
      warmRequiredRatio,
      allow => allowGameplayUnlock = allow
    );
    if (!allowGameplayUnlock) {
      if (!applyGameplayStateBeforeWarmGate) {
        ApplyGameplayStateUnderBlack();
      }
      var revealHandoffStartedAt = Time.realtimeSinceStartup;
      LogRevealHandoff("warm_gate_release", revealHandoffStartedAt);
      yield return UnlockGameplayFromBlackRoutine(overlayTag, revealHandoffStartedAt);
      yield break;
    }

    if (!applyGameplayStateBeforeWarmGate) {
      ApplyGameplayStateUnderBlack();
      HoldPlayerAnimationForLoadingOverlay("wait_for_streaming_idle");
    }

    yield return WaitForStreamingIdleBeforeUnlock(prefetchVisibleSprites: true, warmAnimationsBeforeUnlock: true);
    var revealHandoffStartedAtAfterIdle = Time.realtimeSinceStartup;
    LogRevealHandoff("streaming_idle_complete", revealHandoffStartedAtAfterIdle);
    yield return UnlockGameplayFromBlackRoutine(overlayTag, revealHandoffStartedAtAfterIdle);
  }

  void ApplySavedGameplayStateUnderLoadingOverlay() {
    EnsureGameplayPlayerBootstrap("load_game");
    var player = ResolvePlayerGearController();
    var sceneActive = Scene != null && Scene.activeInHierarchy;
    var playerActive = player != null && player.gameObject.activeInHierarchy;
    if (!sceneActive || !playerActive) {
      if (ShouldLogLoadFlowWarnings()) {
        Debug.LogWarning(
          "[SingleSceneManager][LoadState] gameplay hierarchy inactive before save-state apply" +
          " scene_active=" + (sceneActive ? 1 : 0) +
          " player_active=" + (playerActive ? 1 : 0) +
          " player=" + (player != null ? player.gameObject.name : "-") +
          " current_section=" + ResolveCurrentSection() +
          " overlay_reason=" + (string.IsNullOrWhiteSpace(SpriteStreamingLoadingState.ActiveReason) ? "-" : SpriteStreamingLoadingState.ActiveReason)
        );
      }
      ApplyGameplayStateUnderBlack();
      EnsureGameplayPlayerBootstrap("load_game_after_activate");
    }

    var characterState = ResolvePlayerCharacterState();
    if (characterState != null) {
      LogLoadStateDispatch("direct_load_game");
      characterState.LoadState();
    }
    else {
      LogLoadStateDispatch("dispatch_load_game");
      MessageBus.Send("loadGame");
    }
    if (!ShouldLogLoadingProgressDebug()) return;
    Debug.Log(
      "[SingleSceneManager][LoadState] Applied save-state under loading overlay" +
      " overlay_active=" + (SpriteStreamingLoadingState.IsLoadingOverlayActive ? 1 : 0) +
      " warm_gate_running=" + (StreamingWarmOrchestrator.IsWarmGateRunning ? 1 : 0) +
      " slot=" + SaveSlotManager.slot
    );
  }

  void PrepareNewGameRuntimeStateUnderLoadingOverlay() {
    EnsureGameplayPlayerBootstrap("new_game");
    var player = ResolvePlayerGearController();
    var characterState = ResolvePlayerCharacterState();

    if (ShouldLogLoadFlowDebug()) {
      Debug.Log(
        "[SingleSceneManager][NewGameState] stage=begin" +
        " slot=" + SaveSlotManager.slot +
        " character_state=" + (characterState != null ? 1 : 0) +
        " " + DescribeGearController(player)
      );
    }

    if (characterState == null) {
      Debug.LogWarning(
        "[SingleSceneManager][NewGameState] stage=missing_character_state" +
        " slot=" + SaveSlotManager.slot +
        " " + DescribeGearController(player)
      );
      return;
    }

    characterState.InitializeRuntimeStateForNewGame();

    if (!ShouldLogLoadFlowDebug()) return;
    var playerReady = IsPlayerFirstFrameReady();
    var blockerSummary = playerReady || !TryGetPlayerFirstFrameBlocker(out var blocker) ? "-" : blocker;
    Debug.Log(
      "[SingleSceneManager][NewGameState] stage=complete" +
      " slot=" + SaveSlotManager.slot +
      " player_ready=" + (playerReady ? 1 : 0) +
      " player_blocker=" + blockerSummary
    );
  }

  void MaybeLogGameplayWarmGatePrereqState(
    ref float nextLogAt,
    string stage,
    bool playerBootstrapReady,
    bool locationActivationPending,
    bool shouldExpectEnemyWarmStage,
    int archetypeCount
  ) {
    if (!ShouldLogLoadFlowDebug()) return;
    var now = Time.realtimeSinceStartup;
    if (nextLogAt < 0f) {
      nextLogAt = now;
    }
    if (now < nextLogAt) return;
    nextLogAt = now + GameplayWarmGatePrereqLogIntervalSeconds;
    var infoBuilder = BeginLoadFlowLog("[SingleSceneManager][WarmGatePrereq]");
    AppendLoadFlowField(infoBuilder, "stage", string.IsNullOrWhiteSpace(stage) ? "-" : stage.Trim());
    AppendLoadFlowField(infoBuilder, "current_location", ResolveLoadFlowValue(LocationManager.currentLocation));
    AppendLoadFlowBool(infoBuilder, "player_bootstrap_ready", playerBootstrapReady);
    AppendLoadFlowBool(infoBuilder, "location_activation_pending", locationActivationPending);
    AppendLoadFlowBool(infoBuilder, "expect_enemy_stage", shouldExpectEnemyWarmStage);
    AppendLoadFlowInt(infoBuilder, "enemy_archetype_count", archetypeCount);
    AppendLoadFlowBool(infoBuilder, "ready_for_spawns_sent", gameplayReadyForSpawnsSentForLoad);
    AppendLoadFlowBool(infoBuilder, "warm_gate_started", gameplayWarmGateStartedForLoad);
    AppendLoadFlowBool(infoBuilder, "warm_gate_completed", gameplayWarmGateCompletedForLoad);
    Debug.Log(infoBuilder.ToString());
  }

  IEnumerator WaitForGameplayWarmGatePrerequisites(bool sendReadyForSpawns) {
    if (!Application.isPlaying) yield break;
    var nextLogAt = -1f;
    var enemyArchetypeWaitStartedAt = -1f;
    while (true) {
      var playerBootstrapReady = IsGameplayPlayerBootstrapReady();
      var locationActivationPending = LocationManager.HasPendingBlockingActivationWork;
      var shouldExpectEnemyWarmStage = ShouldExpectEnemyWarmStageForCurrentLocation();
      var archetypeCount = shouldExpectEnemyWarmStage ? ResolveCurrentLocationLoadingArchetypeCount() : 0;
      var shouldWaitForEnemyArchetypes = playerBootstrapReady &&
                                         !locationActivationPending &&
                                         shouldExpectEnemyWarmStage &&
                                         archetypeCount <= 0;
      if (!playerBootstrapReady) {
        enemyArchetypeWaitStartedAt = -1f;
        SetLoadingStatusOverride("Preparing player");
        MaybeLogGameplayWarmGatePrereqState(
          ref nextLogAt,
          "player",
          playerBootstrapReady,
          locationActivationPending,
          shouldExpectEnemyWarmStage,
          archetypeCount
        );
        yield return null;
        continue;
      }

      if (locationActivationPending) {
        enemyArchetypeWaitStartedAt = -1f;
        SetLoadingStatusOverride("Activating location");
        MaybeLogGameplayWarmGatePrereqState(
          ref nextLogAt,
          "location",
          playerBootstrapReady,
          locationActivationPending,
          shouldExpectEnemyWarmStage,
          archetypeCount
        );
        yield return null;
        continue;
      }

      if (shouldWaitForEnemyArchetypes) {
        if (enemyArchetypeWaitStartedAt < 0f) {
          enemyArchetypeWaitStartedAt = Time.realtimeSinceStartup;
        }
        var waitedSeconds = Time.realtimeSinceStartup - enemyArchetypeWaitStartedAt;
        if (waitedSeconds < GameplayWarmGateEnemyArchetypeWaitSeconds) {
          SetLoadingStatusOverride(ResolveGameplayWarmStageDetail(shouldExpectEnemyWarmStage));
          MaybeLogGameplayWarmGatePrereqState(
            ref nextLogAt,
            "enemies",
            playerBootstrapReady,
            locationActivationPending,
            shouldExpectEnemyWarmStage,
            archetypeCount
          );
          yield return null;
          continue;
        }
      }

      ClearLoadingStatusOverride();
      MaybeLogGameplayWarmGatePrereqState(
        ref nextLogAt,
        "ready",
        playerBootstrapReady,
        locationActivationPending,
        shouldExpectEnemyWarmStage,
        archetypeCount
      );
      break;
    }

    if (!sendReadyForSpawns || gameplayReadyForSpawnsSentForLoad) yield break;
    gameplayReadyForSpawnsSentForLoad = true;
    if (ShouldLogLoadFlowDebug()) {
      Debug.Log(
        "[SingleSceneManager][WarmGatePrereq] Dispatch ReadyForSpawns" +
        " current_location=" + ResolveLoadFlowValue(LocationManager.currentLocation) +
        " enemy_archetype_count=" + ResolveCurrentLocationLoadingArchetypeCount()
      );
    }
    MessageBus.Send("ReadyForSpawns");
  }

  void HoldPlayerAnimationForLoadingOverlay(string reason) {
    if (playerAnimationHeldForLoadingOverlay) return;

    var player = ResolvePlayerGearController();
    var controller = player != null ? player.Controller : null;
    if (controller == null) {
      if (ShouldLogLoadFlowDebug()) {
        Debug.Log(
          "[SingleSceneManager][PlayerAnimationHold] stage=skip_missing_controller" +
          " reason=" + (string.IsNullOrWhiteSpace(reason) ? "-" : reason.Trim()) +
          " " + DescribeGearController(player)
        );
      }
      return;
    }

    var targetAnimation = ResolvePlayerLoadingOverlayAnimation(player, controller);
    var restarted = false;
    if (!string.IsNullOrWhiteSpace(targetAnimation)) {
      restarted = controller.PlayAnimation(targetAnimation, forceRestart: true, resolveInterrupts: false);
    }

    if (!restarted && string.IsNullOrWhiteSpace(controller.CurrentAnimation)) {
      if (ShouldLogLoadFlowWarnings()) {
        Debug.LogWarning(
          "[SingleSceneManager][PlayerAnimationHold] stage=skip_missing_animation" +
          " reason=" + (string.IsNullOrWhiteSpace(reason) ? "-" : reason.Trim()) +
          " target_animation=" + (string.IsNullOrWhiteSpace(targetAnimation) ? "-" : targetAnimation.Trim()) +
          " " + DescribeGearController(player)
        );
      }
      return;
    }

    controller.PauseAnimation();
    playerAnimationHeldForLoadingOverlay = true;

    if (!ShouldLogLoadFlowDebug()) return;
    Debug.Log(
      "[SingleSceneManager][PlayerAnimationHold] stage=applied" +
      " reason=" + (string.IsNullOrWhiteSpace(reason) ? "-" : reason.Trim()) +
      " target_animation=" + (string.IsNullOrWhiteSpace(targetAnimation) ? "-" : targetAnimation.Trim()) +
      " current_animation=" + (string.IsNullOrWhiteSpace(controller.CurrentAnimation) ? "-" : controller.CurrentAnimation.Trim()) +
      " restarted=" + (restarted ? 1 : 0) +
      " " + DescribeGearController(player)
    );
  }

  void ResumePlayerAnimationAfterLoadingOverlay(string reason) {
    if (!playerAnimationHeldForLoadingOverlay) return;

    var player = ResolvePlayerGearController();
    var controller = player != null ? player.Controller : null;
    playerAnimationHeldForLoadingOverlay = false;
    if (controller == null) {
      if (ShouldLogLoadFlowDebug()) {
        Debug.Log(
          "[SingleSceneManager][PlayerAnimationHold] stage=release_missing_controller" +
          " reason=" + (string.IsNullOrWhiteSpace(reason) ? "-" : reason.Trim()) +
          " " + DescribeGearController(player)
        );
      }
      return;
    }

    controller.ResumeAnimation();
    if (!ShouldLogLoadFlowDebug()) return;
    Debug.Log(
      "[SingleSceneManager][PlayerAnimationHold] stage=released" +
      " reason=" + (string.IsNullOrWhiteSpace(reason) ? "-" : reason.Trim()) +
      " current_animation=" + (string.IsNullOrWhiteSpace(controller.CurrentAnimation) ? "-" : controller.CurrentAnimation.Trim()) +
      " " + DescribeGearController(player)
    );
  }

  static string ResolvePlayerLoadingOverlayAnimation(GearController player, AnimationController controller) {
    if (controller != null && !string.IsNullOrWhiteSpace(controller.CurrentAnimation)) {
      return controller.CurrentAnimation.Trim();
    }
    if (player != null && !string.IsNullOrWhiteSpace(player.defaultAnimation)) {
      return player.defaultAnimation.Trim();
    }
    return null;
  }

  IEnumerator FadeToBlackBeforeLoadRoutine() {
    SetSceneObjectLightsActive(false);
    SetLoadingBlackscreenHold(false);
    BeginLoadingProgressGoalAtFadeStart();
    PrepareLoadingScreenCarrier();
    if (blackscreen != null) {
      PlayBlackscreen("alphaIn");
    }
    else {
      ForceBlackscreenVisible(true);
    }
    var waitSeconds = Mathf.Max(fadeToBlackSeconds, 0f);
    if (waitSeconds > 0f) {
      yield return FadeToBlackDelay;
    }
    ForceBlackscreenVisible(true);
    SetLoadingBlackscreenHold(true);
  }

  IEnumerator RunScenarioWarmGate(
    WarmGateMode context,
    float timeoutSeconds,
    float requiredRatio,
    Action<bool> onComplete
  ) {
    var gateStartedAt = Time.realtimeSinceStartup;
    ResetBlockingProgressState();
    gameplayWarmGateStartedForLoad = false;
    gameplayWarmGateCompletedForLoad = false;
    var allowGameplayUnlock = true;
    var leadSeconds = Mathf.Max(fadeLeadSeconds, 0f);
    if (leadSeconds > 0f) {
      yield return WarmGateLeadDelay;
    }

    if (!useScenarioWarmGate || !Application.isPlaying) {
      gameplayWarmGateCompletedForLoad = true;
      if (fallbackTransitionSeconds > 0f) {
        yield return FallbackTransitionDelay;
      }
      LogGameplayLoadTiming(
        "warm_gate",
        "skipped",
        gateStartedAt,
        "context=" + context +
        " use_scenario_warm_gate=" + (useScenarioWarmGate ? 1 : 0) +
        " playing=" + (Application.isPlaying ? 1 : 0)
      );
      onComplete?.Invoke(allowGameplayUnlock);
      yield break;
    }

    var playerController = ResolvePlayerGearController();
    var activeEnemies = ResolveActiveEnemyControllers();
    var request = BuildWarmRequest(context, timeoutSeconds, requiredRatio, playerController, activeEnemies);
    var orchestrator = StreamingWarmOrchestrator.Instance;
    if (orchestrator == null) {
      gameplayWarmGateCompletedForLoad = true;
      if (fallbackTransitionSeconds > 0f) {
        yield return FallbackTransitionDelay;
      }
      LogGameplayLoadTiming(
        "warm_gate",
        "missing_orchestrator",
        gateStartedAt,
        "context=" + context
      );
      onComplete?.Invoke(allowGameplayUnlock);
      yield break;
    }

    var completed = false;
    var hasResult = false;
    WarmResult warmResult = default;
    gameplayWarmGateStartedForLoad = true;
    if (ShouldLogLoadFlowDebug()) {
      Debug.Log(
        "[SingleSceneManager][WarmGate] stage=begin" +
        " context=" + context +
        " current_location=" + ResolveLoadFlowValue(LocationManager.currentLocation) +
        " ready_for_spawns_sent=" + (gameplayReadyForSpawnsSentForLoad ? 1 : 0)
      );
    }
    orchestrator.Run(request, result => {
      warmResult = result;
      hasResult = true;
      completed = true;
    });

    while (!completed && orchestrator.IsRunning) {
      yield return null;
    }

    if (!completed && !hasResult) {
      gameplayWarmGateCompletedForLoad = true;
      if (fallbackTransitionSeconds > 0f) {
        yield return FallbackTransitionDelay;
      }
      LogGameplayLoadTiming(
        "warm_gate",
        "no_result",
        gateStartedAt,
        "context=" + context +
        " orchestrator_running=" + (orchestrator.IsRunning ? 1 : 0)
      );
      onComplete?.Invoke(allowGameplayUnlock);
      yield break;
    }

    CaptureBlockingProgressStateFromWarmResult(warmResult);
    gameplayWarmGateCompletedForLoad = true;
    if (ShouldLogLoadFlowDebug()) {
      Debug.Log(
        "[SingleSceneManager][WarmGate] stage=complete" +
        " context=" + context +
        " reached_ready=" + (warmResult.reachedReadyThreshold ? 1 : 0) +
        " hard_bypass=" + (warmResult.hardTimeoutBypassUsed ? 1 : 0) +
        " critical_ready=" + warmResult.criticalReadyCount +
        "/" + warmResult.criticalTotalCount
      );
    }
    allowGameplayUnlock = warmResult.reachedReadyThreshold || warmResult.hardTimeoutBypassUsed;
    LogGameplayLoadTiming(
      "warm_gate",
      allowGameplayUnlock ? "complete" : "blocked",
      gateStartedAt,
      "context=" + context +
      " orchestrator_ms=" + warmResult.elapsedMs.ToString("0.0") +
      " reached_ready=" + (warmResult.reachedReadyThreshold ? 1 : 0) +
      " hard_bypass=" + (warmResult.hardTimeoutBypassUsed ? 1 : 0) +
      " ready=" + warmResult.readyCount + "/" + warmResult.totalCount +
      " critical=" + warmResult.criticalReadyCount + "/" + warmResult.criticalTotalCount +
      " requested=" + warmResult.requestedAddressCount +
      " failure=" + ResolveLoadFlowValue(warmResult.failureReason)
    );
    onComplete?.Invoke(allowGameplayUnlock);
  }

  float ResolvePreUnlockBlockingDeadline() {
    var budgetSeconds = Mathf.Max(preUnlockBlockingBudgetSeconds, 0f);
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var outstanding = Mathf.Max(queue.queuedCount + queue.inFlightCount, 0);
    if (outstanding >= 2000) {
      budgetSeconds = Mathf.Max(budgetSeconds, 6f);
    }
    else if (outstanding >= 1200) {
      budgetSeconds = Mathf.Max(budgetSeconds, 4f);
    }
    else if (outstanding >= 600) {
      budgetSeconds = Mathf.Max(budgetSeconds, 2.5f);
    }
    if (budgetSeconds <= 0f) return float.PositiveInfinity;
    return Time.realtimeSinceStartup + budgetSeconds;
  }

  bool TryGetRemainingPreUnlockBlockingBudget(float deadline, out float remainingSeconds) {
    if (float.IsInfinity(deadline)) {
      remainingSeconds = float.PositiveInfinity;
      return true;
    }

    remainingSeconds = deadline - Time.realtimeSinceStartup;
    return remainingSeconds > 0f;
  }

  void LogPreUnlockBlockingBudget(string stage, float deadline, string state) {
    if (!ShouldLogLoadingProgressDebug()) return;
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var remainingText = float.IsInfinity(deadline)
      ? "inf"
      : Mathf.Max(deadline - Time.realtimeSinceStartup, 0f).ToString("0.000");
    Debug.Log(
      "[SingleSceneManager][PreUnlockBudget] stage=" + stage +
      " state=" + state +
      " remaining_s=" + remainingText +
      " queue_queued=" + queue.queuedCount +
      " queue_in_flight=" + queue.inFlightCount +
      " queue_outstanding=" + Mathf.Max(queue.queuedCount + queue.inFlightCount, 0)
    );
  }

  static void DisposeEnumerator(IEnumerator routine) {
    if (routine is IDisposable disposable) {
      disposable.Dispose();
    }
  }

  static void DisposeEnumeratorStack(Stack<IEnumerator> stack) {
    if (stack == null) return;
    while (stack.Count > 0) {
      DisposeEnumerator(stack.Pop());
    }
  }

  IEnumerator RunPreUnlockStepWithBudget(IEnumerator routine, float deadline, string stage) {
    if (routine == null) yield break;

    var stack = preUnlockEnumeratorStack;
    stack.Clear();
    stack.Push(routine);

    try {
      while (true) {
        if (stack.Count <= 0) {
          yield break;
        }

        if (!float.IsInfinity(deadline) && Time.realtimeSinceStartup >= deadline) {
          LogPreUnlockBlockingBudget(stage, deadline, "budget_exhausted");
          yield break;
        }

        var currentRoutine = stack.Peek();
        if (!currentRoutine.MoveNext()) {
          DisposeEnumerator(currentRoutine);
          stack.Pop();
          continue;
        }

        var yielded = currentRoutine.Current;
        if (yielded is IEnumerator nestedRoutine) {
          stack.Push(nestedRoutine);
          continue;
        }

        yield return yielded;
      }
    }
    finally {
      DisposeEnumeratorStack(stack);
      stack.Clear();
    }
  }

  void ResetPreUnlockResidentPins() {
    preUnlockResidentPinAddressScratch.Clear();
    preUnlockResidentPinReadyAddressScratch.Clear();
    preUnlockResidentPinSeenAddressScratch.Clear();
    if (!Application.isPlaying) return;
    TextureResidencyCache.ReleaseOwnerPins(PreUnlockResidentPinOwnerId);
  }

  int ResolvePreUnlockResidentPinAddressCap() {
    var hardCap = Application.isMobilePlatform ? PreUnlockResidentPinHardCapMobile : PreUnlockResidentPinHardCapDesktop;
    var memoryMb = Math.Max(SystemInfo.systemMemorySize, 0);
    if (memoryMb > 0 && memoryMb <= 4096) hardCap = Math.Min(hardCap, 768);
    else if (memoryMb > 0 && memoryMb <= 8192) hardCap = Math.Min(hardCap, 1536);
    return Mathf.Clamp(Math.Min(preUnlockResidentPinMaxAddresses, hardCap), 1, hardCap);
  }

  void AccumulatePreUnlockResidentPins(List<string> addresses) {
    AccumulatePreUnlockResidentPins(addresses, 0, addresses != null ? addresses.Count : 0);
  }

  void AccumulatePreUnlockResidentPins(List<string> addresses, int startInclusive, int count) {
    if (!enablePreUnlockResidentPinning) return;
    if (addresses == null || addresses.Count <= 0 || count <= 0) return;

    var maxAddresses = ResolvePreUnlockResidentPinAddressCap();
    var target = preUnlockResidentPinAddressScratch;
    var seen = preUnlockResidentPinSeenAddressScratch;
    var start = Mathf.Clamp(startInclusive, 0, addresses.Count);
    var endExclusive = Mathf.Clamp(start + Mathf.Max(count, 0), start, addresses.Count);

    for (var i = start; i < endExclusive; i++) {
      if (target.Count >= maxAddresses) break;
      var normalized = string.IsNullOrWhiteSpace(addresses[i]) ? "" : addresses[i].Trim();
      if (string.IsNullOrWhiteSpace(normalized)) continue;
      if (!seen.Add(normalized)) continue;
      target.Add(normalized);
    }
  }

  void CommitPreUnlockResidentPins(string stage) {
    if (!Application.isPlaying) return;
    var trackedAddresses = preUnlockResidentPinAddressScratch;
    if (!enablePreUnlockResidentPinning || trackedAddresses.Count <= 0) {
      TextureResidencyCache.ReleaseOwnerPins(PreUnlockResidentPinOwnerId);
      return;
    }

    var readyAddresses = preUnlockResidentPinReadyAddressScratch;
    readyAddresses.Clear();
    for (var i = 0; i < trackedAddresses.Count; i++) {
      var address = trackedAddresses[i];
      if (string.IsNullOrWhiteSpace(address)) continue;
      if (!TextureResidencyCache.IsReady(address, pump: false)) continue;
      readyAddresses.Add(address);
    }

    if (readyAddresses.Count <= 0) {
      TextureResidencyCache.ReleaseOwnerPins(PreUnlockResidentPinOwnerId);
      if (!ShouldLogLoadingProgressDebug()) return;
      Debug.Log(
        "[SingleSceneManager][PreUnlockPin] stage=" + stage +
        " tracked_addresses=" + trackedAddresses.Count +
        " pinned_ready_addresses=0 queue_only_skip=1"
      );
      return;
    }

    TextureResidencyCache.UpdateOwnerPins(
      PreUnlockResidentPinOwnerId,
      TextureResidencyCache.PinClass.WarmGate,
      readyAddresses,
      TextureResidencyCache.LoadPriority.Warmup
    );

    if (!ShouldLogLoadingProgressDebug()) return;
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    Debug.Log(
      "[SingleSceneManager][PreUnlockPin] stage=" + stage +
      " tracked_addresses=" + trackedAddresses.Count +
      " pinned_ready_addresses=" + readyAddresses.Count +
      " queue_queued=" + queue.queuedCount +
      " queue_in_flight=" + queue.inFlightCount +
      " resident_mb=" + (TextureResidencyCache.EstimatedResidentBytes / (1024f * 1024f)).ToString("0.0")
    );
  }

  void ReleasePreUnlockResidentPins(string reason) {
    var hadTrackedAddresses = preUnlockResidentPinAddressScratch.Count > 0;
    preUnlockResidentPinAddressScratch.Clear();
    preUnlockResidentPinReadyAddressScratch.Clear();
    preUnlockResidentPinSeenAddressScratch.Clear();
    if (!Application.isPlaying) return;
    TextureResidencyCache.ReleaseOwnerPins(PreUnlockResidentPinOwnerId);
    if (!hadTrackedAddresses || !ShouldLogLoadingProgressDebug()) return;
    Debug.Log("[SingleSceneManager][PreUnlockPin] release reason=" + (string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim()));
  }

  void StopDeferredPostRevealWarmup(string reason) {
    var hadQueuedAddresses = deferredPostRevealWarmupAddressScratch.Count > 0;
    deferredPostRevealWarmupAddressScratch.Clear();
    deferredPostRevealWarmupSeenAddressScratch.Clear();
    if (deferredPostRevealWarmupRoutine == null) {
      if (!hadQueuedAddresses || !ShouldLogLoadingProgressDebug()) return;
      Debug.Log("[SingleSceneManager][DeferredWarmup] stage=clear reason=" + (string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim()));
      return;
    }

    StopCoroutine(deferredPostRevealWarmupRoutine);
    deferredPostRevealWarmupRoutine = null;
    if (!ShouldLogLoadingProgressDebug()) return;
    Debug.Log("[SingleSceneManager][DeferredWarmup] stage=stop reason=" + (string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim()));
  }

  void QueueDeferredPostRevealWarmupAddresses(List<string> addresses, int startInclusive, int count, string stage) {
    if (!Application.isPlaying || addresses == null || addresses.Count <= 0 || count <= 0) return;
    var start = Mathf.Clamp(startInclusive, 0, addresses.Count);
    var endExclusive = Mathf.Clamp(start + Mathf.Max(count, 0), start, addresses.Count);
    var added = 0;
    for (var i = start; i < endExclusive; i++) {
      var normalized = string.IsNullOrWhiteSpace(addresses[i]) ? "" : addresses[i].Trim();
      if (string.IsNullOrWhiteSpace(normalized)) continue;
      if (!deferredPostRevealWarmupSeenAddressScratch.Add(normalized)) continue;
      deferredPostRevealWarmupAddressScratch.Add(normalized);
      added++;
    }
    if (added <= 0 || !ShouldLogLoadingProgressDebug()) return;
    Debug.Log(
      "[SingleSceneManager][DeferredWarmup] stage=queue" +
      " source=" + (string.IsNullOrWhiteSpace(stage) ? "-" : stage.Trim()) +
      " added=" + added +
      " pending=" + deferredPostRevealWarmupAddressScratch.Count
    );
  }

  void StartDeferredPostRevealWarmupIfNeeded(string reason) {
    if (!Application.isPlaying) return;
    if (deferredPostRevealWarmupRoutine != null) return;
    if (deferredPostRevealWarmupAddressScratch.Count <= 0) return;
    deferredPostRevealWarmupRoutine = StartCoroutine(RunDeferredPostRevealWarmupRoutine(reason));
  }

  IEnumerator RunDeferredPostRevealWarmupRoutine(string reason) {
    while (Application.isPlaying && SpriteStreamingLoadingState.IsLoadingOverlayActive) {
      yield return null;
    }
    if (!Application.isPlaying) {
      deferredPostRevealWarmupRoutine = null;
      yield break;
    }

    var processedAddressCount = deferredPostRevealWarmupAddressScratch.Count;

    if (ShouldLogLoadingProgressDebug()) {
      Debug.Log(
        "[SingleSceneManager][DeferredWarmup] stage=begin" +
        " reason=" + (string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim()) +
        " pending=" + processedAddressCount
      );
    }

    if (processedAddressCount > 0) {
      yield return PreloadAnimationAddressBatch(
        deferredPostRevealWarmupAddressScratch,
        0,
        processedAddressCount,
        resetLoadingProgress: false,
        trackResidentPins: false,
        settleAfterEnqueue: false
      );
    }

    if (ShouldLogLoadingProgressDebug()) {
      Debug.Log(
        "[SingleSceneManager][DeferredWarmup] stage=complete" +
        " processed=" + processedAddressCount
      );
    }

    deferredPostRevealWarmupAddressScratch.Clear();
    deferredPostRevealWarmupSeenAddressScratch.Clear();
    deferredPostRevealWarmupRoutine = null;
  }

  int ResolvePreUnlockBlockingPrefixCount(List<string> addresses, int playerAddressCount = -1) {
    if (addresses == null || addresses.Count <= 0) return 0;
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var clampedPlayerAddressCount = playerAddressCount >= 0
      ? Mathf.Clamp(playerAddressCount, 0, addresses.Count)
      : Mathf.Clamp(preUnlockLastPlayerAddressCount, 0, addresses.Count);
    return Mathf.Clamp(
      ResolveWarmupPriorityPrefixCount(addresses.Count, queue, clampedPlayerAddressCount),
      0,
      addresses.Count
    );
  }

  IEnumerator WaitForStreamingIdleBeforeUnlock(
    bool prefetchVisibleSprites = false,
    bool warmAnimationsBeforeUnlock = false
  ) {
    if (!Application.isPlaying) yield break;
    var preUnlockStartedAt = Time.realtimeSinceStartup;
    ResetPreUnlockResidentPins();
    if (!waitForStreamingIdleBeforeFadeOut) {
      var preUnlockBlockingDeadlineWithoutIdleWait = ResolvePreUnlockBlockingDeadline();
      if (warmAnimationsBeforeUnlock) {
        SetLoadingStatusOverride("Warming animations");
        if (TryGetRemainingPreUnlockBlockingBudget(preUnlockBlockingDeadlineWithoutIdleWait, out _)) {
          yield return RunPreUnlockStepWithBudget(
            RunPreUnlockAnimationWarmupSequence(prefetchVisibleSprites),
            preUnlockBlockingDeadlineWithoutIdleWait,
            "animation_warmup_no_idle_wait"
          );
        }
        else {
          LogPreUnlockBlockingBudget("animation_warmup_no_idle_wait", preUnlockBlockingDeadlineWithoutIdleWait, "skipped_no_budget");
        }
      }
      SetLoadingStatusOverride("Finalizing reveal");
      CommitPreUnlockResidentPins("no_idle_wait");
      var noIdleQueue = TextureResidencyCache.GetQueueSnapshot(pump: false);
      var noIdleDeferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
      LogGameplayLoadTiming(
        "pre_unlock",
        "no_idle_wait",
        preUnlockStartedAt,
        "queued=" + noIdleQueue.queuedCount +
        " in_flight=" + noIdleQueue.inFlightCount +
        " deferred=" + noIdleDeferredPending +
        " warmup_done=" + (warmAnimationsBeforeUnlock ? 1 : 0)
      );
      yield break;
    }
    if (LoadingScreen != null && !LoadingScreen.activeSelf) {
      SetLoadingRootActive(true);
    }
    SetLoadingBlackscreenHold(true);
    EnsureLoadingProgressForPhase();
    var preUnlockBlockingDeadline = ResolvePreUnlockBlockingDeadline();
    if (prefetchVisibleSprites) {
      if (TryGetRemainingPreUnlockBlockingBudget(preUnlockBlockingDeadline, out _)) {
        yield return RunPreUnlockStepWithBudget(
          PreloadVisibleSpriteWindowsUnderBlack(),
          preUnlockBlockingDeadline,
          "visible_prefetch"
        );
      }
      else {
        LogPreUnlockBlockingBudget("visible_prefetch", preUnlockBlockingDeadline, "skipped_no_budget");
      }
    }
    var stableFramesRequired = Mathf.Max(streamingIdleStableFrames, 1);
    // Legacy queue-idle fallback is retained for non-warm-gate transitions where
    // no blocking snapshot exists.
    var allowedQueued = Mathf.Max(streamingIdleAllowedQueued, 0);
    var allowedInFlight = Mathf.Max(streamingIdleAllowedInFlight, 0);
    var minimumWaitSeconds = Mathf.Max(streamingIdleMinimumWaitSeconds, 0f);
    var timeoutSeconds = Mathf.Max(streamingIdleTimeoutSeconds, 0f);
    var startedAt = Time.realtimeSinceStartup;
    var stableFrames = 0;
    var nextWaitStateLogAt = -1f;

    var warmupDone = false;

    while (true) {
      TextureResidencyCache.Pump();
      var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
      var resolverIdle = SpriteRuntimeResolver.IsWarmupIdle();
      var queueIdle = queue.queuedCount <= allowedQueued && queue.inFlightCount <= allowedInFlight;
      var elapsed = Time.realtimeSinceStartup - startedAt;
      var minimumWaitReached = elapsed >= minimumWaitSeconds;
      var playerReady = IsPlayerFirstFrameReady();
      var hasBlockingProgress = TryGetBlockingProgressState(
        out var blockingProgress,
        out var blockingReadyCount,
        out var blockingTotalCount,
        out var blockingCriticalReady,
        out var blockingHardBypassUsed
      );
      var locationActivationPending = LocationManager.HasPendingBlockingActivationWork;
      var locationDeferredPending = LocationManager.HasPendingDeferredActivationWork;
      var blockingReady = hasBlockingProgress
        ? IsBlockingScopeReady(resolverIdle, playerReady, blockingCriticalReady, blockingHardBypassUsed, queue) &&
          !locationActivationPending &&
          !locationDeferredPending
        : (queueIdle && resolverIdle && playerReady && !locationActivationPending && !locationDeferredPending);

      if (minimumWaitReached && blockingReady) {
        stableFrames++;
      }
      else {
        stableFrames = 0;
      }
      MaybeLogStreamingIdleWaitState(
        ref nextWaitStateLogAt,
        elapsed,
        minimumWaitSeconds,
        timeoutSeconds,
        stableFrames,
        stableFramesRequired,
        queue,
        resolverIdle,
        playerReady,
        queueIdle,
        hasBlockingProgress,
        blockingReadyCount,
        blockingTotalCount,
        blockingProgress,
        blockingCriticalReady,
        blockingHardBypassUsed,
        blockingReady,
        locationActivationPending,
        locationDeferredPending,
        warmupDone
      );

      if (stableFrames >= stableFramesRequired) {
        if (warmAnimationsBeforeUnlock && !warmupDone) {
          warmupDone = true;
          SetLoadingStatusOverride("Warming animations");
          if (TryGetRemainingPreUnlockBlockingBudget(preUnlockBlockingDeadline, out _)) {
            yield return RunPreUnlockStepWithBudget(
              RunPreUnlockAnimationWarmupSequence(prefetchVisibleSprites),
              preUnlockBlockingDeadline,
              "animation_warmup"
            );
          }
          else {
            LogPreUnlockBlockingBudget("animation_warmup", preUnlockBlockingDeadline, "skipped_no_budget");
          }
          ClearLoadingStatusOverride();
          stableFrames = 0;
          startedAt = Time.realtimeSinceStartup;
          continue;
        }
        SetLoadingStatusOverride("Finalizing reveal");
        CommitPreUnlockResidentPins("stable_ready");
        var stableDeferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
        LogGameplayLoadTiming(
          "pre_unlock",
          "stable_ready",
          preUnlockStartedAt,
          "queued=" + queue.queuedCount +
          " in_flight=" + queue.inFlightCount +
          " deferred=" + stableDeferredPending +
          " resolver_idle=" + (resolverIdle ? 1 : 0) +
          " player_ready=" + (playerReady ? 1 : 0) +
          " blocking_ready=" + (blockingReady ? 1 : 0) +
          " stable_frames=" + stableFrames +
          " warmup_done=" + (warmupDone ? 1 : 0)
        );
        yield break;
      }

      if (timeoutSeconds > 0f && elapsed >= timeoutSeconds) {
        var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
        var queueFullyDrained = queue.queuedCount <= 0 && queue.inFlightCount <= 0 && deferredPending <= 0;
        var forcedByBlockingReady = !allowStreamingIdleTimeoutBypass && hasBlockingProgress && blockingReady;
        var forcedByLegacyDrain =
          !allowStreamingIdleTimeoutBypass &&
          !hasBlockingProgress &&
          queueFullyDrained &&
          !locationActivationPending &&
          !locationDeferredPending;
        if (allowStreamingIdleTimeoutBypass || forcedByBlockingReady || forcedByLegacyDrain) {
          if (warmAnimationsBeforeUnlock && !warmupDone) {
            warmupDone = true;
            SetLoadingStatusOverride("Warming animations");
            if (TryGetRemainingPreUnlockBlockingBudget(preUnlockBlockingDeadline, out _)) {
              yield return RunPreUnlockStepWithBudget(
                RunPreUnlockAnimationWarmupSequence(prefetchVisibleSprites),
                preUnlockBlockingDeadline,
                "animation_warmup_after_timeout"
              );
            }
            else {
              LogPreUnlockBlockingBudget("animation_warmup_after_timeout", preUnlockBlockingDeadline, "skipped_no_budget");
            }
            ClearLoadingStatusOverride();
            stableFrames = 0;
            startedAt = Time.realtimeSinceStartup;
            continue;
          }
          SetLoadingStatusOverride("Finalizing reveal");
          CommitPreUnlockResidentPins("timeout_release");
          LogGameplayLoadTiming(
            "pre_unlock",
            "timeout_release",
            preUnlockStartedAt,
            "queued=" + queue.queuedCount +
            " in_flight=" + queue.inFlightCount +
            " deferred=" + deferredPending +
            " resolver_idle=" + (resolverIdle ? 1 : 0) +
            " player_ready=" + (playerReady ? 1 : 0) +
            " blocking_ready=" + (blockingReady ? 1 : 0) +
            " stable_frames=" + stableFrames +
            " warmup_done=" + (warmupDone ? 1 : 0)
          );
          yield break;
        }
      }

      yield return null;
    }
  }

  bool IsPlayerFirstFrameReady() {
    return !TryGetPlayerFirstFrameBlocker(out _);
  }

  static bool IsRevealActivationSettled(
    TextureResidencyCache.QueueSnapshot queue,
    int deferredPending,
    bool resolverIdle,
    bool playerReady,
    bool uiReady,
    bool dialogReady,
    bool locationActivationPending,
    bool locationDeferredPending
  ) {
    return queue.queuedCount <= 0 &&
           queue.inFlightCount <= 0 &&
           deferredPending <= 0 &&
           resolverIdle &&
           playerReady &&
           uiReady &&
           dialogReady &&
           !locationActivationPending &&
           !locationDeferredPending;
  }

  string ResolveRevealSettleStatusDetail(
    TextureResidencyCache.QueueSnapshot queue,
    int deferredPending,
    bool resolverIdle,
    bool playerReady,
    bool uiReady,
    bool dialogReady,
    bool locationActivationPending,
    bool locationDeferredPending
  ) {
    if (locationActivationPending || locationDeferredPending) {
      return "Activating gameplay";
    }
    if (!playerReady) {
      return "Preparing player";
    }
    if (!uiReady) {
      return "Preparing UI";
    }
    if (!dialogReady) {
      return "Preparing dialog";
    }
    if (!resolverIdle) {
      return "Resolving assets";
    }
    if (ShouldKeepFinalizingRevealStatus(queue, deferredPending)) {
      return "Finalizing reveal";
    }
    if (queue.queuedCount > 0 || queue.inFlightCount > 0 || deferredPending > 0) {
      return "Draining queue";
    }
    return "Finalizing reveal";
  }

  static bool ShouldKeepFinalizingRevealStatus(TextureResidencyCache.QueueSnapshot queue, int deferredPending) {
    return queue.queuedCount <= 0 &&
           deferredPending <= 0 &&
           queue.inFlightCount <= 1;
  }

  void MaybeLogRevealSettleState(
    ref string lastState,
    string state,
    float startedAt,
    TextureResidencyCache.QueueSnapshot queue,
    int deferredPending,
    bool resolverIdle,
    bool playerReady,
    bool uiReady,
    bool dialogReady,
    bool locationActivationPending,
    bool locationDeferredPending
  ) {
    if (!ShouldLogLoadFlowWarnings()) return;
    if (string.Equals(lastState, state, StringComparison.Ordinal)) return;

    lastState = state;
    var blockerSummary = playerReady || !TryGetPlayerFirstFrameBlocker(out var blocker) ? "-" : blocker;
    Debug.Log(
      "[SingleSceneManager][RevealSettle] state='" + state +
      "' elapsed_s=" + (Time.realtimeSinceStartup - startedAt).ToString("0.000") +
      " queued=" + queue.queuedCount +
      " in_flight=" + queue.inFlightCount +
      " deferred=" + deferredPending +
      " resolver_idle=" + (resolverIdle ? 1 : 0) +
      " player_ready=" + (playerReady ? 1 : 0) +
      " ui_ready=" + (uiReady ? 1 : 0) +
      " dialog_ready=" + (dialogReady ? 1 : 0) +
      " player_blocker='" + blockerSummary +
      "' location_activation_pending=" + (locationActivationPending ? 1 : 0) +
      " location_deferred_pending=" + (locationDeferredPending ? 1 : 0) +
      " current_section=" + ResolveCurrentSection() +
      " current_location=" + ResolveLoadFlowValue(LocationManager.currentLocation)
    );
  }

  void LogRevealHandoff(string stage, float handoffStartedAt, float stepStartedAt = -1f) {
    if (!ShouldLogLoadFlowWarnings()) return;
    var now = Time.realtimeSinceStartup;
    var builder = BeginLoadFlowLog("[SingleSceneManager][RevealHandoff]");
    AppendLoadFlowField(builder, "stage", ResolveLoadFlowValue(stage, "unspecified"));
    if (handoffStartedAt >= 0f) {
      AppendLoadFlowFloat(builder, "elapsed_s", Mathf.Max(now - handoffStartedAt, 0f));
    }
    if (stepStartedAt >= 0f) {
      AppendLoadFlowFloat(builder, "step_ms", Mathf.Max(now - stepStartedAt, 0f) * 1000f, "0.0");
    }
    AppendLoadFlowField(builder, "overlay_reason", ResolveLoadFlowValue(SpriteStreamingLoadingState.ActiveReason));
    AppendLoadFlowField(builder, "current_section", ResolveCurrentSection().ToString());
    AppendLoadFlowField(builder, "current_location", ResolveLoadFlowValue(LocationManager.currentLocation));
    Debug.Log(builder.ToString());
  }

  IEnumerator WaitForRevealActivationSettle() {
    var stableFramesRequired = Mathf.Max(RevealOpaqueSettleFrames, 1);
    var stableSecondsRequired = Mathf.Max(RevealOpaqueSettleMinimumStableSeconds, 0f);
    var timeoutSeconds = Mathf.Max(RevealOpaqueSettleTimeoutSeconds, 0f);
    var startedAt = Time.realtimeSinceStartup;
    var stableFrames = 0;
    var stableStartedAt = -1f;
    string lastLoggedState = null;

    while (true) {
      TextureResidencyCache.Pump();
      var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
      var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
      var resolverIdle = SpriteRuntimeResolver.IsWarmupIdle();
      var playerReady = IsPlayerFirstFrameReady();
      var locationActivationPending = LocationManager.HasPendingBlockingActivationWork;
      var locationDeferredPending = LocationManager.HasPendingDeferredActivationWork;
      var uiReady = IsGameplayUiReadyForLoadingProgress() && !locationDeferredPending;
      var dialogReady = IsGameplayDialogReadyForLoadingProgress();
      var statusDetail = ResolveRevealSettleStatusDetail(
        queue,
        deferredPending,
        resolverIdle,
        playerReady,
        uiReady,
        dialogReady,
        locationActivationPending,
        locationDeferredPending
      );

      SetLoadingStatusOverride(statusDetail);
      MaybeLogRevealSettleState(
        ref lastLoggedState,
        statusDetail,
        startedAt,
        queue,
        deferredPending,
        resolverIdle,
        playerReady,
        uiReady,
        dialogReady,
        locationActivationPending,
        locationDeferredPending
      );

      if (IsRevealActivationSettled(
            queue,
            deferredPending,
            resolverIdle,
            playerReady,
            uiReady,
            dialogReady,
            locationActivationPending,
            locationDeferredPending
          )) {
        if (stableFrames <= 0) {
          stableStartedAt = Time.realtimeSinceStartup;
        }
        stableFrames++;
        var stableElapsed = stableStartedAt >= 0f
          ? Time.realtimeSinceStartup - stableStartedAt
          : 0f;
        if (stableFrames >= stableFramesRequired && stableElapsed >= stableSecondsRequired) {
          LogGameplayLoadTiming(
            "reveal_settle",
            "exit",
            startedAt,
            "status=" + ResolveLoadFlowValue(statusDetail) +
            " queued=" + queue.queuedCount +
            " in_flight=" + queue.inFlightCount +
            " deferred=" + deferredPending +
            " resolver_idle=" + (resolverIdle ? 1 : 0) +
            " player_ready=" + (playerReady ? 1 : 0) +
            " ui_ready=" + (uiReady ? 1 : 0) +
            " dialog_ready=" + (dialogReady ? 1 : 0) +
            " location_activation_pending=" + (locationActivationPending ? 1 : 0) +
            " location_deferred_pending=" + (locationDeferredPending ? 1 : 0)
          );
          if (ShouldLogLoadFlowWarnings()) {
            Debug.Log(
              "[SingleSceneManager][RevealSettle] exit" +
              " elapsed_s=" + (Time.realtimeSinceStartup - startedAt).ToString("0.000") +
              " stable_s=" + stableElapsed.ToString("0.000") +
              " stable_frames=" + stableFrames
            );
          }
          yield break;
        }
      }
      else {
        stableFrames = 0;
        stableStartedAt = -1f;
      }

      var elapsed = Time.realtimeSinceStartup - startedAt;
      if (timeoutSeconds > 0f && elapsed >= timeoutSeconds) {
        LogGameplayLoadTiming(
          "reveal_settle",
          "timeout",
          startedAt,
          "status=" + ResolveLoadFlowValue(statusDetail) +
          " queued=" + queue.queuedCount +
          " in_flight=" + queue.inFlightCount +
          " deferred=" + deferredPending +
          " resolver_idle=" + (resolverIdle ? 1 : 0) +
          " player_ready=" + (playerReady ? 1 : 0) +
          " ui_ready=" + (uiReady ? 1 : 0) +
          " dialog_ready=" + (dialogReady ? 1 : 0) +
          " location_activation_pending=" + (locationActivationPending ? 1 : 0) +
          " location_deferred_pending=" + (locationDeferredPending ? 1 : 0)
        );
        if (ShouldLogLoadFlowWarnings()) {
          Debug.LogWarning(
            "[SingleSceneManager][RevealSettle] timeout" +
            " elapsed_s=" + elapsed.ToString("0.000") +
            " queued=" + queue.queuedCount +
            " in_flight=" + queue.inFlightCount +
            " deferred=" + deferredPending +
            " resolver_idle=" + (resolverIdle ? 1 : 0) +
            " player_ready=" + (playerReady ? 1 : 0) +
            " ui_ready=" + (uiReady ? 1 : 0) +
            " dialog_ready=" + (dialogReady ? 1 : 0) +
            " location_activation_pending=" + (locationActivationPending ? 1 : 0) +
            " location_deferred_pending=" + (locationDeferredPending ? 1 : 0) +
            " current_section=" + ResolveCurrentSection() +
            " current_location=" + ResolveLoadFlowValue(LocationManager.currentLocation)
          );
        }
        yield break;
      }

      yield return null;
    }
  }

  IEnumerator RunPreUnlockAnimationWarmupSequence(bool includeVisibleSpriteReprefetch) {
    var playerController = ResolvePlayerAnimationController();
    BuildEnemyAnimationControllerSnapshot(preUnlockEnemyControllerScratch);

    yield return WarmAnimationPlaybackBeforeUnlock(playerController, preUnlockEnemyControllerScratch);

    BuildControllerAnimationFrameAddressSnapshot(
      playerController,
      preUnlockEnemyControllerScratch,
      preUnlockAddressScratch
    );

    if (preUnlockAddressScratch.Count > 0) {
      var blockingPrefixCount = ResolvePreUnlockBlockingPrefixCount(preUnlockAddressScratch);
      if (blockingPrefixCount < preUnlockAddressScratch.Count) {
        QueueDeferredPostRevealWarmupAddresses(
          preUnlockAddressScratch,
          blockingPrefixCount,
          preUnlockAddressScratch.Count - blockingPrefixCount,
          "animation_frame_tail"
        );
      }
      var preloadPasses = Mathf.Max(preUnlockAnimationFramePreloadPasses, 1);
      if (blockingPrefixCount > 0) {
        for (var pass = 0; pass < preloadPasses; pass++) {
          yield return PreloadAnimationAddressBatch(
            preUnlockAddressScratch,
            0,
            blockingPrefixCount,
            resetLoadingProgress: pass == 0,
            trackResidentPins: true,
            settleAfterEnqueue: false
          );
          yield return WaitForPreUnlockWarmupQueueSettle();
          if (pass + 1 < preloadPasses) {
            yield return null;
          }
        }
      }
    }

    if (includeVisibleSpriteReprefetch && preUnlockReprefetchVisibleSpritesAfterAnimationWarmup) {
      yield return PreloadVisibleSpriteWindowsUnderBlack(preUnlockEnemyControllerScratch);
      yield return WaitForPreUnlockWarmupQueueSettle();
    }
  }

  IEnumerator WaitForPreUnlockWarmupQueueSettle() {
    var timeoutSeconds = Mathf.Max(preUnlockWarmupQueueSettleTimeoutSeconds, 0f);
    if (timeoutSeconds <= 0f) yield break;

    var startedAt = Time.realtimeSinceStartup;
    while (true) {
      TextureResidencyCache.Pump();
      var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
      var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
      if (queue.queuedCount <= 0 && queue.inFlightCount <= 0 && deferredPending <= 0) {
        yield break;
      }

      var resolverIdle = SpriteRuntimeResolver.IsWarmupIdle();
      var playerReady = IsPlayerFirstFrameReady();
      var hasBlockingProgress = TryGetBlockingProgressState(
        out _,
        out _,
        out _,
        out var blockingCriticalReady,
        out var blockingHardBypassUsed
      );
      var settledForBlockingReady =
        hasBlockingProgress &&
        resolverIdle &&
        playerReady &&
        (blockingCriticalReady || blockingHardBypassUsed) &&
        IsQueueWithinBlockingReadyThresholds(queue, deferredPending);
      if (settledForBlockingReady) {
        if (ShouldLogLoadingProgressDebug()) {
          ResolveBlockingReadyQueueThresholds(out var maxOutstanding, out var maxInFlight);
          Debug.Log(
            "[SingleSceneManager][PreUnlockSettle] early_release" +
            " queued=" + queue.queuedCount +
            " in_flight=" + queue.inFlightCount +
            " deferred=" + deferredPending +
            " max_outstanding=" + maxOutstanding +
            " max_in_flight=" + maxInFlight
          );
        }
        yield break;
      }

      if ((Time.realtimeSinceStartup - startedAt) >= timeoutSeconds) {
        yield break;
      }
      yield return null;
    }
  }

  IEnumerator PreloadVisibleSpriteWindowsUnderBlack(List<AnimationController> enemyControllers = null) {
    if (!enablePreUnlockVisibleSpritePrefetch || !Application.isPlaying) yield break;

    var targets = ResolvePreUnlockVisibleSpriteTargets();
    if (targets == null || targets.Length <= 0) yield break;

    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var maxAddresses = ResolvePreUnlockMaxAddresses(queue);
    var addresses = preUnlockAddressScratch;
    var seenAddresses = preUnlockSeenAddressScratch;
    addresses.Clear();
    seenAddresses.Clear();
    var animationFrames = Mathf.Max(preUnlockPrefetchAnimationFrames, 1);
    var lookAheadFrames = ResolvePreUnlockLookAheadFrames(queue);
    var frameJumpClamp = Mathf.Max(preUnlockPrefetchFrameJumpClamp, 1);
    SortTargetsByPriority(targets);

    for (var i = 0; i < targets.Length; i++) {
      var target = targets[i];
      if (target == null) continue;
      if (!target.isActiveAndEnabled || target.DoNotRender) continue;
      if (!preUnlockPrefetchIncludeUiTargets && target.IsUiTarget()) continue;

      var startFrame = target.IsAnimation
        ? Mathf.Max(Mathf.Max(target.LastRequestedFrame, 1) - (frameJumpClamp - 1), 1)
        : 0;
      var endFrame = target.IsAnimation ? Mathf.Max(startFrame + animationFrames - 1, startFrame) : 0;

      target.CollectAnimationWindowAddresses(
        target.category,
        startFrame,
        endFrame,
        lookAheadFrames,
        addresses,
        seenAddresses,
        maxAddresses
      );

      if (addresses.Count >= maxAddresses) break;
    }

    if (enablePreUnlockControllerAnimationPrefetch && addresses.Count < maxAddresses) {
      var playerController = ResolvePlayerAnimationController();
      if (playerController != null) {
        playerController.CollectAnimationStartAddresses(
          addresses,
          seenAddresses,
          framesPerAnimation: animationFrames,
          maxAnimations: Mathf.Max(preUnlockPlayerAnimationStarts, 1),
          maxAddresses: maxAddresses
        );
      }

      var enemyMaxAnimations = Mathf.Max(preUnlockEnemyAnimationStartsPerController, 0);
      if (enemyMaxAnimations > 0 && addresses.Count < maxAddresses) {
        if (enemyControllers != null && enemyControllers.Count > 0) {
          for (var i = 0; i < enemyControllers.Count; i++) {
            if (addresses.Count >= maxAddresses) break;
            var controller = enemyControllers[i];
            if (controller == null) continue;
            controller.CollectAnimationStartAddresses(
              addresses,
              seenAddresses,
              framesPerAnimation: animationFrames,
              maxAnimations: enemyMaxAnimations,
              maxAddresses: maxAddresses
            );
          }
        }
        else {
          var activeEnemies = ResolveActiveEnemyControllers();
          for (var i = 0; i < activeEnemies.Length; i++) {
            if (addresses.Count >= maxAddresses) break;
            var enemy = activeEnemies[i];
            if (enemy == null || enemy.Controller == null) continue;
            enemy.Controller.CollectAnimationStartAddresses(
              addresses,
              seenAddresses,
              framesPerAnimation: animationFrames,
              maxAnimations: enemyMaxAnimations,
              maxAddresses: maxAddresses
            );
          }
        }
      }
    }

    if (preUnlockPrefetchExpandAtlasSiblings && addresses.Count > 0 && addresses.Count < maxAddresses) {
      var maxSiblingsPerSeed = Mathf.Clamp(preUnlockPrefetchMaxAtlasSiblingsPerSeed, 1, 256);
      var siblingScratch = preUnlockAtlasSiblingScratch;
      if (siblingScratch.Capacity < maxSiblingsPerSeed) {
        siblingScratch.Capacity = maxSiblingsPerSeed;
      }
      var seedCount = addresses.Count;
      for (var i = 0; i < seedCount; i++) {
        if (addresses.Count >= maxAddresses) break;
        var seedAddress = addresses[i];
        if (string.IsNullOrWhiteSpace(seedAddress)) continue;

        siblingScratch.Clear();
        if (!SpriteRuntimeResolver.TryCollectAtlasSiblingAddresses(seedAddress, siblingScratch, maxSiblingsPerSeed)) continue;

        for (var s = 0; s < siblingScratch.Count; s++) {
          if (addresses.Count >= maxAddresses) break;
          var siblingAddress = siblingScratch[s];
          if (string.IsNullOrWhiteSpace(siblingAddress)) continue;
          if (!seenAddresses.Add(siblingAddress)) continue;
          addresses.Add(siblingAddress);
        }
      }
    }

    if (addresses.Count <= 0) yield break;

    yield return PreloadAnimationAddressBatch(addresses, resetLoadingProgress: false);
  }

  void SortTargetsByPriority(SpriteWithNormals[] targets) {
    if (targets == null || targets.Length <= 1) return;
    var player = ResolvePlayerGearController();
    var playerPos = player != null ? player.transform.position : Vector3.zero;
    var hasPlayer = player != null;

    Array.Sort(targets, (a, b) => {
      if (a == null && b == null) return 0;
      if (a == null) return 1;
      if (b == null) return -1;
      if (!hasPlayer) return 0;
      var distA = (a.transform.position - playerPos).sqrMagnitude;
      var distB = (b.transform.position - playerPos).sqrMagnitude;
      return distA.CompareTo(distB);
    });
  }

  IEnumerator WarmAnimationPlaybackBeforeUnlock(
    AnimationController playerController = null,
    List<AnimationController> enemyControllers = null
  ) {
    if (!enablePreUnlockAnimationPlaybackWarmup || !Application.isPlaying) yield break;

    var passes = ResolvePreUnlockPlaybackPasses();
    var controllersPerFrame = Mathf.Max(preUnlockAnimationWarmupControllersPerFrame, 1);
    var warmedControllers = 0;
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var maxAddresses = ResolvePreUnlockMaxAddresses(queue);
    var addresses = preUnlockAddressScratch;
    var seenAddresses = preUnlockSeenAddressScratch;
    addresses.Clear();
    seenAddresses.Clear();

    SetLoadingBlackscreenHold(true);

    if (playerController == null) {
      playerController = ResolvePlayerAnimationController();
    }

    if (playerController != null) {
      var playerMaxAnimations = Mathf.Max(preUnlockPlayerAnimationStarts, 1);
      playerController.CollectWarmPlaybackAddresses(
        addresses,
        seenAddresses,
        passCount: passes,
        maxAnimations: playerMaxAnimations,
        maxAddresses: maxAddresses
      );
      warmedControllers++;
      if (warmedControllers % controllersPerFrame == 0) {
        yield return null;
      }
    }
    var playerWarmPlaybackAddressCount = addresses.Count;

    if (preUnlockWarmEnemyAnimationPlayback) {
      if (enemyControllers == null || enemyControllers.Count <= 0) {
        BuildEnemyAnimationControllerSnapshot(preUnlockEnemyControllerScratch);
        enemyControllers = preUnlockEnemyControllerScratch;
      }

      var maxEnemies = ResolvePreUnlockEnemyControllerCap(enemyControllers != null ? enemyControllers.Count : 0);
      var enemyMaxAnimations = Mathf.Max(preUnlockEnemyAnimationStartsPerController, 0);
      var warmedEnemies = 0;
      for (var i = 0; i < enemyControllers.Count; i++) {
        if (maxEnemies > 0 && warmedEnemies >= maxEnemies) break;
        if (enemyMaxAnimations <= 0) break;
        var controller = enemyControllers[i];
        if (controller == null) continue;
        controller.CollectWarmPlaybackAddresses(
          addresses,
          seenAddresses,
          passCount: passes,
          maxAnimations: enemyMaxAnimations,
          maxAddresses: maxAddresses
        );
        warmedControllers++;
        warmedEnemies++;
        if (addresses.Count >= maxAddresses) break;
        if (warmedControllers % controllersPerFrame == 0) {
          yield return null;
        }
      }
    }

    if (addresses.Count > 0) {
      var blockingPrefixCount = ResolvePreUnlockBlockingPrefixCount(addresses, playerWarmPlaybackAddressCount);
      if (blockingPrefixCount < addresses.Count) {
        QueueDeferredPostRevealWarmupAddresses(
          addresses,
          blockingPrefixCount,
          addresses.Count - blockingPrefixCount,
          "animation_playback_tail"
        );
      }
      if (blockingPrefixCount > 0) {
        yield return PreloadAnimationAddressBatch(
          addresses,
          0,
          blockingPrefixCount,
          resetLoadingProgress: true,
          trackResidentPins: true,
          settleAfterEnqueue: false
        );
        yield return WaitForPreUnlockWarmupQueueSettle();
      }
    }
  }

  IEnumerator PreloadAllControllerAnimationFrames(
    AnimationController playerController = null,
    List<AnimationController> enemyControllers = null
  ) {
    if (!Application.isPlaying) yield break;

    if (playerController == null) {
      playerController = ResolvePlayerAnimationController();
    }

    if (enemyControllers == null) {
      BuildEnemyAnimationControllerSnapshot(preUnlockEnemyControllerScratch);
      enemyControllers = preUnlockEnemyControllerScratch;
    }

    BuildControllerAnimationFrameAddressSnapshot(
      playerController,
      enemyControllers,
      preUnlockAddressScratch
    );

    yield return PreloadAnimationAddressBatch(preUnlockAddressScratch, resetLoadingProgress: true);
  }

  AnimationController ResolvePlayerAnimationController() {
    var player = ResolvePlayerGearController();
    if (player == null) return null;
    return player.Controller;
  }

  void BuildEnemyAnimationControllerSnapshot(List<AnimationController> outControllers) {
    if (outControllers == null) return;
    outControllers.Clear();

    var activeEnemies = ResolveActiveEnemyControllers();
    if (activeEnemies.Length <= 0) return;

    var player = ResolvePlayerGearController();
    var hasPlayer = player != null;
    var playerPosition = hasPlayer ? player.transform.position : Vector3.zero;
    var maxDistance = Mathf.Max(preUnlockAnimationWarmupEnemyDistance, 0f);
    var maxDistanceSqr = maxDistance > 0f ? maxDistance * maxDistance : -1f;

    // Collect filtered enemies with their squared distances, then sort nearest-first so
    // their animation addresses land at the front of the preload list (deterministic ordering).
    var filteredScratch = preUnlockFilteredEnemyScratch;
    filteredScratch.Clear();
    for (var i = 0; i < activeEnemies.Length; i++) {
      var enemy = activeEnemies[i];
      if (enemy == null || enemy.Controller == null) continue;
      var sqrDist = 0f;
      if (hasPlayer) {
        var delta = enemy.transform.position - playerPosition;
        sqrDist = delta.sqrMagnitude;
        if (maxDistanceSqr > 0f && sqrDist > maxDistanceSqr) continue;
      }
      filteredScratch.Add((sqrDist, enemy.Controller));
    }

    if (hasPlayer && filteredScratch.Count > 1) {
      filteredScratch.Sort((a, b) => a.sqrDist.CompareTo(b.sqrDist));
    }

    for (var i = 0; i < filteredScratch.Count; i++) {
      outControllers.Add(filteredScratch[i].controller);
    }
  }

  void BuildControllerAnimationFrameAddressSnapshot(
    AnimationController playerController,
    List<AnimationController> enemyControllers,
    List<string> outAddresses
  ) {
    if (outAddresses == null) return;

    var maxAddresses = ResolvePreUnlockMaxAddresses(TextureResidencyCache.GetQueueSnapshot(pump: false));
    var seenAddresses = preUnlockSeenAddressScratch;
    outAddresses.Clear();
    seenAddresses.Clear();
    var animationFrames = Mathf.Max(preUnlockPrefetchAnimationFrames, 1);

    if (playerController != null) {
      playerController.CollectAnimationStartAddresses(
        outAddresses,
        seenAddresses,
        framesPerAnimation: animationFrames,
        maxAnimations: Mathf.Max(preUnlockPlayerAnimationStarts, 1),
        maxAddresses: maxAddresses
      );
    }
    preUnlockLastPlayerAddressCount = outAddresses.Count;

    if (outAddresses.Count < maxAddresses) {
      var enemyMaxAnimations = Mathf.Max(preUnlockEnemyAnimationStartsPerController, 0);
      if (enemyMaxAnimations <= 0) return;
      if (enemyControllers != null) {
        for (var i = 0; i < enemyControllers.Count; i++) {
          if (outAddresses.Count >= maxAddresses) break;
          var controller = enemyControllers[i];
          if (controller == null) continue;
          controller.CollectAnimationStartAddresses(
            outAddresses,
            seenAddresses,
            framesPerAnimation: animationFrames,
            maxAnimations: enemyMaxAnimations,
            maxAddresses: maxAddresses
          );
        }
      }
      else {
        var activeEnemies = ResolveActiveEnemyControllers();
        for (var i = 0; i < activeEnemies.Length; i++) {
          if (outAddresses.Count >= maxAddresses) break;
          var enemy = activeEnemies[i];
          if (enemy == null || enemy.Controller == null) continue;
          enemy.Controller.CollectAnimationStartAddresses(
            outAddresses,
            seenAddresses,
            framesPerAnimation: animationFrames,
            maxAnimations: enemyMaxAnimations,
            maxAddresses: maxAddresses
          );
        }
      }
    }
  }

  IEnumerator PreloadAnimationAddressBatch(List<string> addresses, bool resetLoadingProgress) {
    if (addresses == null || addresses.Count <= 0) yield break;
    yield return PreloadAnimationAddressBatch(
      addresses,
      0,
      addresses.Count,
      resetLoadingProgress,
      trackResidentPins: true,
      settleAfterEnqueue: true
    );
  }

  IEnumerator PreloadAnimationAddressBatch(
    List<string> addresses,
    int startInclusive,
    int entryCount,
    bool resetLoadingProgress,
    bool trackResidentPins,
    bool settleAfterEnqueue
  ) {
    if (addresses == null || addresses.Count <= 0 || entryCount <= 0) yield break;
    var start = Mathf.Clamp(startInclusive, 0, addresses.Count);
    var endExclusive = Mathf.Clamp(start + Mathf.Max(entryCount, 0), start, addresses.Count);
    var requestedCount = endExclusive - start;
    if (requestedCount <= 0) yield break;
    if (trackResidentPins) {
      AccumulatePreUnlockResidentPins(addresses, start, requestedCount);
    }

    if (resetLoadingProgress) {
      ResetLoadingProgressForPhase();
    }

    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var enqueueBudget = ResolvePreUnlockEnqueueBudget(queue);

    // Process in batches to avoid stalling the main thread while filling the request queue.
    // Matches Aguide.txt "Batched load" guidance (50-200).
    // Tune this value based on platform performance (lower for mobile).
    const int BatchSize = 100;
    var processedCount = 0;

    while (processedCount < requestedCount) {
      var remaining = requestedCount - processedCount;
      var chunkCount = Mathf.Min(BatchSize, remaining);
      var chunkStart = start + processedCount;
      QueuePreUnlockTrimmedMetadataWarmup(addresses, chunkStart, chunkCount);
      yield return TextureResidencyCache.RequestLoadBatchThrottled(
        addresses,
        chunkStart,
        chunkCount,
        TextureResidencyCache.LoadPriority.Warmup,
        // Atlas-first preload: ensure sibling slices from the same atlas are resident before gameplay unlock.
        allowAtlasExpansion: true,
        enqueueBudgetPerFrame: enqueueBudget
      );

      processedCount += chunkCount;
      if (settleAfterEnqueue) {
        yield return WaitForPreUnlockWarmupQueueSettle();
      }

      queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
      enqueueBudget = ResolvePreUnlockEnqueueBudget(queue);
    }
  }

  void QueuePreUnlockTrimmedMetadataWarmup(List<string> addresses, int startInclusive, int count) {
    if (addresses == null || addresses.Count <= 0 || count <= 0) return;
    var queuedAtlasMetadata = TrimmedSpriteOffsetResolver.QueueWarmupAtlasMetadataBatch(addresses, startInclusive, count);
    if (queuedAtlasMetadata <= 0) return;

    TrimmedSpriteOffsetResolver.PumpDeferredRuntimeLoads();
    if (!ShouldLogLoadingProgressDebug()) return;
    Debug.Log(
      "[SingleSceneManager][PreUnlockMetadataWarmup] start=" + startInclusive +
      " count=" + count +
      " queued_atlas_metadata=" + queuedAtlasMetadata
    );
  }

  SpriteWithNormals[] ResolvePreUnlockVisibleSpriteTargets() {
    var now = Time.realtimeSinceStartup;
    var refreshSeconds = Mathf.Max(preUnlockTargetCacheRefreshSeconds, 0f);
    var hasCache = preUnlockVisibleSpriteTargetsCache != null && preUnlockVisibleSpriteTargetsCache.Length > 0;
    var cacheExpired = refreshSeconds <= 0f ||
      preUnlockVisibleSpriteTargetsCacheRefreshedAt < 0f ||
      (now - preUnlockVisibleSpriteTargetsCacheRefreshedAt) >= refreshSeconds;
    if (hasCache && !cacheExpired) {
      return preUnlockVisibleSpriteTargetsCache;
    }

    preUnlockVisibleSpriteTargetsCache =
      FindObjectsByType<SpriteWithNormals>(FindObjectsInactive.Exclude) ??
      Array.Empty<SpriteWithNormals>();
    preUnlockVisibleSpriteTargetsCacheRefreshedAt = now;
    return preUnlockVisibleSpriteTargetsCache;
  }

  void InvalidatePreUnlockTargetCache() {
    preUnlockVisibleSpriteTargetsCache = Array.Empty<SpriteWithNormals>();
    preUnlockVisibleSpriteTargetsCacheRefreshedAt = -1f;
  }

  int ResolvePreUnlockMaxAddresses(TextureResidencyCache.QueueSnapshot queue) {
    var configuredMax = Mathf.Max(preUnlockPrefetchMaxAddresses, 1);
    var configuredMin = Mathf.Clamp(preUnlockPrefetchMinAddresses, 1, configuredMax);
    var scale = 1f;

    if (SystemInfo.systemMemorySize <= 8192) {
      scale = 0.45f;
    }
    else if (SystemInfo.systemMemorySize <= 12288) {
      scale = 0.65f;
    }
    else if (SystemInfo.systemMemorySize <= 16384) {
      scale = 0.8f;
    }

    if (queue.queuedCount >= 1400 || queue.inFlightCount >= 192) {
      scale *= 0.5f;
    }
    else if (queue.queuedCount >= 900 || queue.inFlightCount >= 128) {
      scale *= 0.7f;
    }

    var scaled = Mathf.RoundToInt(configuredMax * scale);
    return Mathf.Clamp(scaled, configuredMin, configuredMax);
  }

  int ResolvePreUnlockLookAheadFrames(TextureResidencyCache.QueueSnapshot queue) {
    var lookAhead = Mathf.Max(preUnlockPrefetchLookAheadFrames, 0);
    if (lookAhead <= 0) return 0;
    if (queue.queuedCount >= 1400 || queue.inFlightCount >= 192) return 0;
    if (queue.queuedCount >= 900 || queue.inFlightCount >= 128) return Mathf.Min(lookAhead, 1);
    return lookAhead;
  }

  int ResolvePreUnlockPlaybackPasses() {
    var passes = Mathf.Max(preUnlockAnimationPlaybackPasses, 1);
    if (passes <= 1) return 1;

    if (SystemInfo.systemMemorySize <= 12288) return 1;
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    if (queue.queuedCount >= 900 || queue.inFlightCount >= 128) return 1;
    return passes;
  }

  int ResolvePreUnlockEnemyControllerCap(int availableCount) {
    if (availableCount <= 0) return 0;
    if (preUnlockAnimationWarmupMaxEnemyControllers > 0) {
      return Mathf.Min(preUnlockAnimationWarmupMaxEnemyControllers, availableCount);
    }

    var autoCap = 6;
    if (SystemInfo.systemMemorySize <= 8192) {
      autoCap = 2;
    }
    else if (SystemInfo.systemMemorySize <= 12288) {
      autoCap = 4;
    }
    return Mathf.Min(autoCap, availableCount);
  }

  int ResolvePreUnlockEnqueueBudget(TextureResidencyCache.QueueSnapshot queue) {
    var budget = Mathf.Clamp(preUnlockPrefetchEnqueueBudgetPerFrame, 50, 200);
    if (queue.queuedCount >= 1400 || queue.inFlightCount >= 192) return Mathf.Min(budget, 60);
    if (queue.queuedCount >= 900 || queue.inFlightCount >= 128) return Mathf.Min(budget, 90);
    if (queue.queuedCount >= 500 || queue.inFlightCount >= 64) return Mathf.Min(budget, 120);
    return budget;
  }

  int ResolveWarmupPriorityPrefixCount(int totalAddressCount, TextureResidencyCache.QueueSnapshot queue, int playerAddressCount = 0) {
    if (totalAddressCount <= 0) return 0;
    // TODO(smooth-first-play): Replace static queue thresholds with observed first-frame miss rate
    // so prefix sizing adapts to real animation smoothness.
    int queueBasedCount;
    if (queue.queuedCount >= 1400 || queue.inFlightCount >= 192) {
      queueBasedCount = Mathf.Clamp(totalAddressCount / 3, 32, totalAddressCount);
    }
    else if (queue.queuedCount >= 900 || queue.inFlightCount >= 128) {
      queueBasedCount = Mathf.Clamp((totalAddressCount * 2) / 3, 64, totalAddressCount);
    }
    else {
      return totalAddressCount;
    }
    // Floor at player address count so all player-critical sprites always get Warmup priority
    // regardless of queue pressure, reflecting measured time-to-first-ready-frame for the player.
    return Mathf.Clamp(Mathf.Max(queueBasedCount, playerAddressCount), min: 0, max: totalAddressCount);
  }

  float ResolveRequiredWarmRatio(WarmGateMode context, float configuredRatio, EnemyController[] activeEnemies) {
    var ratio = Mathf.Clamp(configuredRatio, 0.5f, 0.99f);
    if (context != WarmGateMode.LoadSave) return ratio;

    var cap = Mathf.Clamp(Mathf.Max(loadSaveWarmRequiredRatioCap, 0.95f), 0.5f, 0.99f);
    var floor = Mathf.Clamp(Mathf.Max(loadSaveWarmRequiredRatioFloor, 0.9f), 0.5f, 0.99f);
    ratio = Mathf.Min(ratio, cap);
    if (SystemInfo.systemMemorySize <= 8192) {
      ratio -= 0.03f;
    }
    else if (SystemInfo.systemMemorySize <= 12288) {
      ratio -= 0.015f;
    }

    var enemyCount = activeEnemies != null ? activeEnemies.Length : 0;
    if (enemyCount >= 10) {
      ratio -= 0.03f;
    }
    else if (enemyCount >= 5) {
      ratio -= 0.015f;
    }

    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    if (queue.queuedCount >= 900 || queue.inFlightCount >= 128) {
      ratio -= 0.02f;
    }

    return Mathf.Clamp(ratio, floor, 0.99f);
  }

  void LogWarmRequestScope(
    WarmGateMode context,
    List<string> criticalLibraries,
    List<string> criticalAddresses,
    List<string> warmLibraries,
    List<string> warmAddresses,
    List<string> criticalLabels,
    List<string> warmLabels,
    List<string> criticalAssetAddresses,
    List<string> warmAssetAddresses,
    List<string> criticalAssetLabels,
    List<string> warmAssetLabels,
    EnemyController[] criticalEnemies,
    List<string> criticalPlayerEffectKeys,
    float requiredRatio
  ) {
    if (!ShouldLogLoadingProgressDebug()) return;
    Debug.Log(
      "[SingleSceneManager][WarmScope] context=" + context +
      " current_location=" + ResolveLoadFlowValue(LocationManager.currentLocation) +
      " blocking_libraries=" + (criticalLibraries != null ? criticalLibraries.Count : 0) +
      " blocking_addresses=" + (criticalAddresses != null ? criticalAddresses.Count : 0) +
      " blocking_labels=" + (criticalLabels != null ? criticalLabels.Count : 0) +
      " blocking_asset_addresses=" + (criticalAssetAddresses != null ? criticalAssetAddresses.Count : 0) +
      " blocking_asset_labels=" + (criticalAssetLabels != null ? criticalAssetLabels.Count : 0) +
      " background_libraries=" + (warmLibraries != null ? warmLibraries.Count : 0) +
      " background_addresses=" + (warmAddresses != null ? warmAddresses.Count : 0) +
      " background_labels=" + (warmLabels != null ? warmLabels.Count : 0) +
      " background_asset_addresses=" + (warmAssetAddresses != null ? warmAssetAddresses.Count : 0) +
      " background_asset_labels=" + (warmAssetLabels != null ? warmAssetLabels.Count : 0) +
      " critical_enemies=" + (criticalEnemies != null ? criticalEnemies.Length : 0) +
      " critical_player_effects=" + (criticalPlayerEffectKeys != null ? criticalPlayerEffectKeys.Count : 0) +
      " required_ratio=" + requiredRatio.ToString("0.000")
    );
  }

  WarmRequest BuildWarmRequest(
    WarmGateMode context,
    float timeoutSeconds,
    float requiredRatio,
    GearController playerController,
    EnemyController[] activeEnemies
  ) {
    warmRequestCriticalLibrariesScratch.Clear();
    var criticalLibraries = warmRequestCriticalLibrariesScratch;
    warmRequestCriticalAddressesScratch.Clear();
    var criticalAddresses = warmRequestCriticalAddressesScratch;
    warmRequestWarmLibrariesScratch.Clear();
    var warmLibraries = warmRequestWarmLibrariesScratch;
    warmRequestWarmAddressesScratch.Clear();
    var warmAddresses = warmRequestWarmAddressesScratch;
    warmRequestCriticalLabelsScratch.Clear();
    var criticalLabels = warmRequestCriticalLabelsScratch;
    warmRequestWarmLabelsScratch.Clear();
    var warmLabels = warmRequestWarmLabelsScratch;
    warmRequestCriticalAssetAddressesScratch.Clear();
    var criticalAssetAddresses = warmRequestCriticalAssetAddressesScratch;
    warmRequestWarmAssetAddressesScratch.Clear();
    var warmAssetAddresses = warmRequestWarmAssetAddressesScratch;
    warmRequestCriticalAssetLabelsScratch.Clear();
    var criticalAssetLabels = warmRequestCriticalAssetLabelsScratch;
    warmRequestWarmAssetLabelsScratch.Clear();
    var warmAssetLabels = warmRequestWarmAssetLabelsScratch;
    var archetypes = ResolveLocationArchetypePrefabs();
    var combatPopulationTypes = ResolveCombatPopulationWarmTypes(activeEnemies, archetypes, combatPopulationTypesScratch);
    var criticalEnemies = ResolveCriticalWarmEnemies(activeEnemies, playerController);
    var criticalPlayerEffectKeys = ResolveCriticalPlayerEffectKeys(playerController, criticalPlayerEffectKeysScratch);

    if (playerController != null) {
      playerController.CollectPersistentProjectileStartupAssetAddresses(
        criticalAssetAddresses,
        CorePlayerWarmAnimationKeys
      );
    }
    CollectCombatPopulationProjectileStartupAssetAddresses(combatPopulationTypes, criticalAssetAddresses);
    var token = StreamingWarmOrchestrator.BuildEnemyArchetypeToken(LocationManager.currentLocation, archetypes);
    var tunedRequiredRatio = ResolveRequiredWarmRatio(context, requiredRatio, activeEnemies);

    LogWarmRequestScope(
      context,
      criticalLibraries,
      criticalAddresses,
      warmLibraries,
      warmAddresses,
      criticalLabels,
      warmLabels,
      criticalAssetAddresses,
      warmAssetAddresses,
      criticalAssetLabels,
      warmAssetLabels,
      criticalEnemies,
      criticalPlayerEffectKeys,
      tunedRequiredRatio
    );

    if (context == WarmGateMode.LoadSave) {
      return WarmRequest.CreateLoadSave(
        playerController: playerController,
        criticalEnemyControllers: criticalEnemies,
        enemyControllers: activeEnemies,
        enemyArchetypePrefabsByType: archetypes,
        timeoutSeconds: timeoutSeconds,
        requiredReadyRatio: tunedRequiredRatio,

        hardTimeoutSeconds: Mathf.Max(startWarmHardTimeoutSeconds, timeoutSeconds, 3.0f),
        allowHardTimeoutBypass: allowHardTimeoutBypass,
        idempotencyToken: token,
        skipIfTokenAlreadyWarm: true,
        criticalPlayerEffectKeys: criticalPlayerEffectKeys,
        allowCriticalReadySoftTimeout: true
      );
    }

    if (context == WarmGateMode.GearApplyReturn) {
      return WarmRequest.CreateGearApplyReturn(
        playerController: playerController,
        timeoutSeconds: timeoutSeconds,
        requiredReadyRatio: tunedRequiredRatio,
        extraCriticalLibraries: criticalLibraries,
        extraCriticalAddresses: criticalAddresses,
        extraCriticalLabels: criticalLabels,
        extraCriticalAssetAddresses: criticalAssetAddresses,
        extraCriticalAssetLabels: criticalAssetLabels,
        extraWarmLibraries: warmLibraries,
        extraWarmAddresses: warmAddresses,
        extraWarmLabels: warmLabels,
        extraWarmAssetAddresses: warmAssetAddresses,
        extraWarmAssetLabels: warmAssetLabels,
        hardTimeoutSeconds: Mathf.Max(gearReturnWarmHardTimeoutSeconds, timeoutSeconds, 2.5f),
        allowHardTimeoutBypass: allowHardTimeoutBypass,
        idempotencyToken: "",
        skipIfTokenAlreadyWarm: false,
        criticalPlayerEffectKeys: criticalPlayerEffectKeys,
        allowCriticalReadySoftTimeout: true
      );
    }

    return WarmRequest.CreateStartGame(
      playerController: playerController,
      criticalEnemyControllers: criticalEnemies,
      enemyControllers: activeEnemies,
      enemyArchetypePrefabsByType: archetypes,
      timeoutSeconds: timeoutSeconds,
      requiredReadyRatio: tunedRequiredRatio,
      extraCriticalLibraries: criticalLibraries,
      extraCriticalAddresses: criticalAddresses,
      extraCriticalLabels: criticalLabels,
      extraCriticalAssetAddresses: criticalAssetAddresses,
      extraCriticalAssetLabels: criticalAssetLabels,
      extraWarmLibraries: warmLibraries,
      extraWarmAddresses: warmAddresses,
      extraWarmLabels: warmLabels,
      extraWarmAssetAddresses: warmAssetAddresses,
      extraWarmAssetLabels: warmAssetLabels,
      hardTimeoutSeconds: Mathf.Max(startWarmHardTimeoutSeconds, timeoutSeconds, 3.0f),
      allowHardTimeoutBypass: allowHardTimeoutBypass,
      idempotencyToken: token,
      skipIfTokenAlreadyWarm: true,
      criticalPlayerEffectKeys: criticalPlayerEffectKeys,
      allowCriticalReadySoftTimeout: true
    );
  }

  List<string> ResolveCombatPopulationWarmTypes(
    EnemyController[] activeEnemies,
    Dictionary<string, GameObject> archetypes,
    List<string> output
  ) {
    var enemyTypes = output ?? combatPopulationTypesScratch;
    enemyTypes.Clear();

    if (activeEnemies != null) {
      for (var i = 0; i < activeEnemies.Length; i++) {
        var enemy = activeEnemies[i];
        if (enemy == null) continue;
        AddUniqueCombatPopulationType(enemyTypes, enemy.enemyType);
      }
    }

    if (enemyTypes.Count <= 0 && archetypes != null && archetypes.Count > 0) {
      foreach (var pair in archetypes) {
        AddUniqueCombatPopulationType(enemyTypes, pair.Key);
      }
    }

    if (enemyTypes.Count <= 0 &&
        LocationEnemyData.TryGetLocation(LocationManager.currentLocation, out var locationInfo) &&
        locationInfo != null &&
        locationInfo.enemies != null) {
      for (var i = 0; i < locationInfo.enemies.Count; i++) {
        AddUniqueCombatPopulationType(enemyTypes, locationInfo.enemies[i]);
      }
    }

    return enemyTypes;
  }

  static void AddUniqueCombatPopulationType(List<string> output, string enemyType) {
    if (output == null || string.IsNullOrWhiteSpace(enemyType)) return;
    var normalized = enemyType.Trim();
    for (var i = 0; i < output.Count; i++) {
      if (string.Equals(output[i], normalized, StringComparison.OrdinalIgnoreCase)) {
        return;
      }
    }
    output.Add(normalized);
  }

  int CollectCombatPopulationProjectileStartupAssetAddresses(
    IReadOnlyList<string> combatPopulationTypes,
    List<string> outAddresses,
    int maxUniqueAddresses = int.MaxValue
  ) {
    if (combatPopulationTypes == null || combatPopulationTypes.Count <= 0 || outAddresses == null || maxUniqueAddresses <= 0) {
      return 0;
    }

    var beforeCount = outAddresses.Count;
    combatPopulationProjectileKeysScratch.Clear();
    combatPopulationProjectileKeySeenScratch.Clear();

    for (var i = 0; i < combatPopulationTypes.Count; i++) {
      if (outAddresses.Count >= maxUniqueAddresses) {
        break;
      }

      if (!TryGetEnemyAnimationManifest(combatPopulationTypes[i], out var animations)) {
        continue;
      }

      AnimationLinkUtility.CollectLinkedProjectileKeys(
        animations,
        null,
        combatPopulationProjectileKeysScratch,
        combatPopulationProjectileKeySeenScratch
      );
    }

    for (var i = 0; i < combatPopulationProjectileKeysScratch.Count; i++) {
      if (outAddresses.Count >= maxUniqueAddresses) {
        break;
      }

      TryAddProjectilePrefabWarmAddress(combatPopulationProjectileKeysScratch[i], outAddresses);
    }

    var addedCount = Mathf.Max(outAddresses.Count - beforeCount, 0);
    if (addedCount > 0 && ShouldLogLoadingProgressDebug()) {
      Debug.Log(
        "[SingleSceneManager][EnemyProjectileWarmup]" +
        " location=" + ResolveLoadFlowValue(LocationManager.currentLocation) +
        " enemy_types=" + combatPopulationTypes.Count +
        " projectile_keys=" + combatPopulationProjectileKeysScratch.Count +
        " asset_addresses_added=" + addedCount
      );
    }

    combatPopulationProjectileKeysScratch.Clear();
    combatPopulationProjectileKeySeenScratch.Clear();
    return addedCount;
  }

  static bool TryGetEnemyAnimationManifest(string enemyType, out Dictionary<string, AnimData> animations) {
    animations = null;
    if (string.IsNullOrWhiteSpace(enemyType)) {
      return false;
    }

    var normalized = enemyType.Trim();
    if (Animations.Enemies.TryGetValue(normalized, out animations) && animations != null) {
      return true;
    }

    foreach (var pair in Animations.Enemies) {
      if (!string.Equals(pair.Key, normalized, StringComparison.OrdinalIgnoreCase) || pair.Value == null) {
        continue;
      }

      animations = pair.Value;
      return true;
    }

    return false;
  }

  static bool TryAddProjectilePrefabWarmAddress(string projectileKey, List<string> outAddresses) {
    if (outAddresses == null || string.IsNullOrWhiteSpace(projectileKey)) {
      return false;
    }

    if (!Projectiles.TryGetPrefabAddress(projectileKey, out var address) || string.IsNullOrWhiteSpace(address)) {
      Debug.LogWarning(
        "[SingleSceneManager][EnemyProjectileWarmup] MissingProjectilePrefabAddress" +
        " projectile='" + projectileKey.Trim() + "'"
      );
      return false;
    }

    if (ContainsAddressIgnoreCase(outAddresses, address)) {
      return false;
    }

    outAddresses.Add(address);
    return true;
  }

  static bool ContainsAddressIgnoreCase(List<string> addresses, string address) {
    if (addresses == null || string.IsNullOrWhiteSpace(address)) {
      return false;
    }

    for (var i = 0; i < addresses.Count; i++) {
      if (string.Equals(addresses[i], address, StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
  }

  EnemyController[] ResolveCriticalWarmEnemies(EnemyController[] activeEnemies, GearController playerController) {
    var maxCriticalEnemies = Mathf.Max(warmGateCriticalEnemyCount, 0);
    if (maxCriticalEnemies <= 0 || activeEnemies == null || activeEnemies.Length <= 0) {
      return Array.Empty<EnemyController>();
    }

    var hasPlayer = playerController != null;
    var playerPosition = hasPlayer ? playerController.transform.position : Vector3.zero;
    var maxDistance = Mathf.Max(warmGateCriticalEnemyDistance, 0f);
    var maxDistanceSqr = maxDistance > 0f ? maxDistance * maxDistance : -1f;
    var filteredEnemies = warmGateCriticalEnemyScratch;
    filteredEnemies.Clear();

    for (var i = 0; i < activeEnemies.Length; i++) {
      var enemy = activeEnemies[i];
      if (enemy == null || enemy.Controller == null) continue;
      var sqrDist = 0f;
      if (hasPlayer) {
        var delta = enemy.transform.position - playerPosition;
        sqrDist = delta.sqrMagnitude;
        if (maxDistanceSqr > 0f && sqrDist > maxDistanceSqr) continue;
      }
      filteredEnemies.Add((sqrDist, enemy));
    }

    if (filteredEnemies.Count <= 0) {
      return Array.Empty<EnemyController>();
    }

    if (hasPlayer && filteredEnemies.Count > 1) {
      filteredEnemies.Sort((a, b) => a.sqrDist.CompareTo(b.sqrDist));
    }

    var count = Mathf.Min(maxCriticalEnemies, filteredEnemies.Count);
    var criticalEnemies = new EnemyController[count];
    for (var i = 0; i < count; i++) {
      criticalEnemies[i] = filteredEnemies[i].enemy;
    }
    return criticalEnemies;
  }

  List<string> ResolveCriticalPlayerEffectKeys(GearController playerController, List<string> output) {
    if (!warmGatePreloadCorePlayerEffects || playerController == null || playerController.effectNode == null) {
      return null;
    }

    var keys = output ?? criticalPlayerEffectKeysScratch;
    keys.Clear();
    AnimationLinkUtility.CollectLinkedEffectKeys(Animations.Esperanza, CorePlayerWarmAnimationKeys, keys);
    return keys.Count > 0 ? keys : null;
  }

  Dictionary<string, GameObject> ResolveLocationArchetypePrefabs() {
    var spawner = ResolveGameplaySpawner();
    if (spawner != null) {
      var map = spawner.BuildCurrentLocationArchetypeMapForWarmup();
      if (map != null && map.Count > 0) {
        Debug.Log(
          "[SingleSceneManager] Using active location prefab enemy archetypes for warmup" +
          " location='" + LocationManager.currentLocation + "'" +
          " archetypes=" + map.Count
        );
        return map;
      }
    }

    Debug.LogWarning(
      "[SingleSceneManager] No location prefab enemy archetypes available for warmup" +
      " location='" + LocationManager.currentLocation + "'" +
      " spawner=" + (spawner != null ? 1 : 0) +
      " location_activation_pending=" + (LocationManager.HasPendingBlockingActivationWork ? 1 : 0) +
      " location_deferred_pending=" + (LocationManager.HasPendingDeferredActivationWork ? 1 : 0) +
      " ready_for_spawns_sent=" + (gameplayReadyForSpawnsSentForLoad ? 1 : 0) + "."
    );
    return new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
  }

  GearController ResolvePlayerGearController() {
    if (cachedPlayerGearController != null) {
      var cachedGo = cachedPlayerGearController.gameObject;
      var cachedValid = cachedGo != null &&
                        cachedGo.scene.IsValid() &&
                        (cachedGo.hideFlags & HideFlags.HideAndDontSave) == 0;
      if (cachedValid) {
        if (Scene == null || !Scene.activeInHierarchy || IsPreferredGameplayPlayerController(cachedPlayerGearController)) {
          return cachedPlayerGearController;
        }

        var replacement = ResolveBestAvailablePlayerController();
        if (replacement == null || ReferenceEquals(replacement, cachedPlayerGearController)) {
          return cachedPlayerGearController;
        }

        InvalidateCachedPlayerGearController("cached_candidate_replaced");
      }
      else {
        InvalidateCachedPlayerGearController("cached_candidate_invalid");
      }
    }

    cachedPlayerGearController = ResolveBestAvailablePlayerController();
    if (ShouldLogLoadFlowDebug()) {
      Debug.Log(
        "[SingleSceneManager][PlayerResolve] resolved " +
        DescribeGearController(cachedPlayerGearController) +
        " scene_active=" + (Scene != null && Scene.activeInHierarchy ? 1 : 0)
      );
    }
    return cachedPlayerGearController;
  }

  bool ShouldWarmGearReturn() {
    if (!useScenarioWarmGate || !Application.isPlaying) return false;
    var gear = ResolvePlayerGearController();
    if (gear == null) return false;
    if (pauseMenuOpenAppearanceRevision < 0) return true;
    return gear.AppearanceRevision != pauseMenuOpenAppearanceRevision;
  }

  EnemyController[] ResolveActiveEnemyControllers() {
    var now = Time.unscaledTime;
    if (activeEnemyControllersCacheRefreshedAt >= 0f &&
        now - activeEnemyControllersCacheRefreshedAt < ActiveEnemyControllersCacheRefreshSeconds) {
      return activeEnemyControllersCache;
    }

    var enemies = FindObjectsByType<EnemyController>(FindObjectsInactive.Exclude);
    activeEnemyControllersCache = enemies != null && enemies.Length > 0 ? enemies : Array.Empty<EnemyController>();
    activeEnemyControllersCacheRefreshedAt = now;
    return activeEnemyControllersCache;
  }

  void InvalidateActiveEnemyControllersCache() {
    activeEnemyControllersCache = Array.Empty<EnemyController>();
    activeEnemyControllersCacheRefreshedAt = -1f;
  }

  void ApplyGameplayStateUnderBlack() {
    pendingRevealSection = Section.Gameplay;
    SetLoadingRootActive(true);
    InvalidateCachedPlayerGearController("apply_gameplay_state_under_black");
    RequestLocationLoadForGameplay(ConsumePendingGameplayLocationId("apply_gameplay_state_under_black"));
    SetLoadingBlackscreenHold(true);
    _SwitchMap("none");
    HideAllSectionsForTransition(Section.Gameplay);
    SetSceneObjectLightsActive(false);
    pauseMenuOpenAppearanceRevision = -1;
    LogSectionTransitionState("gameplay_under_black", ResolveCurrentSection(), Section.Gameplay, SpriteStreamingLoadingState.ActiveReason, IsLoadingProgressUiVisible());
  }

  IEnumerator UnlockGameplayFromBlackRoutine(string overlayTag, float revealHandoffStartedAt = -1f) {
    var previousSection = ResolveCurrentSection();
    SetLoadingLightActive(false);
    SetLoadingStatusOverride("Activating gameplay");
    var activationStartedAt = ShouldLogLoadFlowWarnings() ? Time.realtimeSinceStartup : -1f;
    ApplySectionActivation(Section.Gameplay);
    ApplyInputForSection(Section.Gameplay);
    LogRevealHandoff("gameplay_activation_applied", revealHandoffStartedAt, activationStartedAt);
    yield return WaitForRevealActivationSettle();
    RestoreSceneLightingForCurrentActivation();
    LogRevealHandoff("reveal_settle_complete", revealHandoffStartedAt);
    LogSectionTransitionState("ready_to_reveal", previousSection, Section.Gameplay, overlayTag, false);
    yield return FadeFromBlackRoutine(overlayTag, Section.Gameplay);
  }

  void PlayBlackscreen(string animationName) {
    if (blackscreen == null) return;
    if (holdBlackscreenOpaqueDuringLoad && string.Equals(animationName, "alphaOut", StringComparison.Ordinal)) {
      return;
    }
    if (string.Equals(animationName, "alphaOut", StringComparison.Ordinal)) {
      loadingOverlayChildrenReady = false;
      SetLoadingLightActive(false);
      SetLoadingProgressUiActive(false);
      SetLoadingText("");
    }
    if (loadingBlackscreenRenderer != null) {
      var c = loadingBlackscreenRenderer.color;
      if (!Mathf.Approximately(c.a, 1f)) {
        c.a = 1f;
        loadingBlackscreenRenderer.color = c;
      }
    }
    blackscreen.Play(animationName);
  }

  IEnumerator StartupFadeWatchdogRoutine() {
    var wait = Mathf.Max(startupFadeWatchdogSeconds, 0f);
    if (wait > 0f) {
      yield return StartupFadeWatchdogDelay;
    }
    if (IsLoadingFlowActive()) {
      startupFadeWatchdogRoutine = null;
      yield break;
    }
    if (ShouldRunStartupGameplayWarmFlow()) {
      if (startupGameplayRoutine == null) {
        startupGameplayRoutine = StartCoroutine(StartupGameplayFlowRoutine());
      }
      startupFadeWatchdogRoutine = null;
      yield break;
    }

    SetLoadingLightActive(false);
    SetLoadingProgressUiActive(false);
    SetLoadingText("");
    SetLoadingBlackscreenHold(false);
    ForceBlackscreenVisible(false);
    SpriteStreamingLoadingState.ForceClearLoadingOverlay();
    RestoreSceneLightingForCurrentActivation();
    ReleaseLoadingScreenIfIdle();
    LogSectionTransitionState("startup_reveal_complete", Section.None, ResolveCurrentSection(), "MainMenuStartup", false);
    ApplyStartupInputFallbackAfterWatchdog();
    startupFadeWatchdogRoutine = null;
  }

  bool IsLoadingFlowActive() {
    if (startGameRoutine != null || resumeGameplayRoutine != null || startupGameplayRoutine != null) return true;
    return SpriteStreamingLoadingState.IsLoadingOverlayActive;
  }

  void StopStartupFadeWatchdog() {
    if (startupFadeWatchdogRoutine == null) return;
    StopCoroutine(startupFadeWatchdogRoutine);
    startupFadeWatchdogRoutine = null;
  }

  void StopStartupGameplayFlow() {
    if (startupGameplayRoutine == null) return;
    StopCoroutine(startupGameplayRoutine);
    startupGameplayRoutine = null;
    pendingRevealSection = Section.None;
    StopDeferredPostRevealWarmup("stop_startup_gameplay_flow");
    ResumePlayerAnimationAfterLoadingOverlay("stop_startup_gameplay_flow");
    SpriteStreamingLoadingState.ForceClearLoadingOverlay();
  }

  void ApplyStartupInputFallbackAfterWatchdog() {
    if (startGameRoutine != null || resumeGameplayRoutine != null || startupGameplayRoutine != null) {
      return;
    }
    var section = ResolveCurrentSection();
    if (section != Section.None) {
      ApplyInputForSection(section);
      return;
    }
    ApplyInputMapForCurrentUiState(preferGameplayWhenNoUi: false);
  }

  bool ShouldRunStartupGameplayWarmFlow() {
    return startupInDebugGameplay;
  }

  void ApplyConfiguredStartupMode() {
    ResolveStartupMode();
    if (startupInDebugGameplay) {
      ApplyConfiguredDebugStartupMode();
      return;
    }

    ApplyConfiguredMainMenuStartupMode();
  }

  void ResolveStartupMode() {
    startupInDebugGameplay = false;
    startupDebugLocationId = "";

    if (!IsEditorStartupDebugEnabled()) {
      if (ShouldLogLoadFlowDebug()) {
        Debug.Log("[SingleSceneManager][StartupMode] mode=main_menu debug_mode=0 debug_location=-");
      }
      return;
    }

    var resolvedDebugLocationId = ResolveConfiguredDebugLocationId(out var debugLocationReason);
    if (!IsGameplayLocation(resolvedDebugLocationId) ||
        !LocationEnemyData.TryGetLocation(resolvedDebugLocationId, out var locationInfo) ||
        locationInfo == null) {
      if (ShouldLogLoadFlowWarnings()) {
        Debug.LogWarning(
          "[SingleSceneManager][StartupMode] mode=main_menu debug_mode=1" +
          " debug_location='" + ResolveLoadFlowValue(debugLocationId) + "'" +
          " legacy_debug_prefab='" + (debugLocationPrefab != null ? debugLocationPrefab.name : "-") + "'" +
          " reason=" + debugLocationReason
        );
      }
      return;
    }

    startupInDebugGameplay = true;
    startupDebugLocationId = LocationEnemyData.NormalizeLocationId(resolvedDebugLocationId);
    if (ShouldLogLoadFlowDebug()) {
      Debug.Log(
        "[SingleSceneManager][StartupMode] mode=debug_gameplay debug_mode=1" +
        " debug_location='" + ResolveLoadFlowValue(debugLocationId) + "'" +
        " resolved_location='" + startupDebugLocationId + "'" +
        " legacy_debug_prefab='" + (debugLocationPrefab != null ? debugLocationPrefab.name : "-") + "'" +
        " reason=" + debugLocationReason +
        " enemies=" + (locationInfo.enemies != null ? locationInfo.enemies.Count : 0) +
        " max_enemies=" + locationInfo.maxEnemies +
        " spawn_interval=" + locationInfo.spawnInterval.ToString("0.###")
      );
    }
  }

  void ApplyConfiguredDebugStartupMode() {
    ReleasePreUnlockResidentPins("startup_mode_debug_gameplay");
    StopDeferredPostRevealWarmup("startup_mode_debug_gameplay");
    pendingRevealSection = Section.None;
    currentSection = Section.None;
    if (autoSaver != null) {
      autoSaver.enableTimeTracking = true;
    }
    HideAllSectionsForTransition(Section.Gameplay);
    _SwitchMap("none");
    SetSceneObjectLightsActive(false);
    pauseMenuOpenAppearanceRevision = -1;
    SpriteStreamingLoadingState.EndLoadingOverlay("MainMenuStartup");
  }

  void ApplyConfiguredMainMenuStartupMode() {
    ReleasePreUnlockResidentPins("startup_mode_main_menu");
    StopDeferredPostRevealWarmup("startup_mode_main_menu");
    pendingRevealSection = Section.None;
    RequestLocationLoadForMainMenu();
    ApplySectionActivation(Section.MainMenu);
    ApplyInputForSection(Section.MainMenu);
    if (autoSaver != null) {
      autoSaver.enableTimeTracking = false;
    }
    SetSceneObjectLightsActive(false);
    pauseMenuOpenAppearanceRevision = -1;
    SpriteStreamingLoadingState.EndLoadingOverlay("MainMenuStartup");
  }

  void ApplyInputMapForCurrentUiState(bool preferGameplayWhenNoUi) {
    if (PauseMenu != null && PauseMenu.activeInHierarchy) {
      _SwitchMap("pauseMenu");
      return;
    }
    if (SettingsMenu != null && SettingsMenu.activeInHierarchy) {
      _SwitchMap("settingsMenu");
      return;
    }
    if (LoadMenu != null && LoadMenu.activeInHierarchy) {
      _SwitchMap("loadMenu");
      return;
    }
    if (MainMenu != null && MainMenu.activeInHierarchy) {
      _SwitchMap("mainMenu");
      return;
    }
    if (GameplayInterface != null && GameplayInterface.activeInHierarchy) {
      _SwitchMap(ResolveGameplayInputMap());
      return;
    }
    if (preferGameplayWhenNoUi && HasLiveGameplayInput()) {
      _SwitchMap(ResolveGameplayInputMap());
      return;
    }
    if (preferGameplayWhenNoUi) {
      _SwitchMap("mainMenu");
    }
  }

  bool HasLiveGameplayInput() {
    if (IsGameplayInputLive(cachedGameplayInput)) return true;

    var now = Time.unscaledTime;
    if (gameplayInputCacheRefreshedAt >= 0f &&
        now - gameplayInputCacheRefreshedAt < GameplayInputCacheRefreshSeconds) {
      return false;
    }

    gameplayInputCacheRefreshedAt = now;
    cachedGameplayInput = FindAnyObjectByType<GameplayInput>();
    return IsGameplayInputLive(cachedGameplayInput);
  }

  static bool IsGameplayInputLive(GameplayInput gameplayInput) {
    return gameplayInput != null &&
           gameplayInput.enabled &&
           gameplayInput.gameObject.activeInHierarchy;
  }

  string ConsumePendingGameplayLocationId(string source) {
    var locationId = pendingGameplayLocationId;
    pendingGameplayLocationId = "";
    if (ShouldLogLoadFlowDebug()) {
      Debug.Log(
        "[SingleSceneManager][GameplayLocation] stage=consume_pending" +
        " source=" + ResolveLoadFlowValue(source) +
        " pending=" + ResolveLoadFlowValue(locationId) +
        " current_location=" + ResolveLoadFlowValue(LocationManager.currentLocation) +
        " last_known_gameplay=" + ResolveLoadFlowValue(lastKnownGameplayLocationId)
      );
    }
    return locationId;
  }

  void ResolveAndApplyLocationForStart(bool isNewGame, SaveData loadedSlot) {
    var resolved = ResolveLocationForStart(isNewGame, loadedSlot);
    var previousLocation = LocationManager.currentLocation;
    pendingGameplayLocationId = resolved;
    RememberGameplayLocation(resolved, "resolve_start");

    if (!ShouldLogLoadFlowDebug()) return;

    Debug.Log(
      "[SingleSceneManager][StartLocation] resolved=" + resolved +
      " previous=" + previousLocation +
      " staged_for_gameplay=1" +
      " is_new_game=" + (isNewGame ? 1 : 0) +
      " debug_mode=" + (startupInDebugGameplay ? 1 : 0) +
      " debug_location='" + ResolveLoadFlowValue(debugLocationId) + "'" +
      " startup_debug_location='" + ResolveLoadFlowValue(startupDebugLocationId) + "'"
    );
  }

  bool IsKnownLocation(string location) {
    return LocationEnemyData.ContainsLocation(location);
  }

  static bool IsMainMenuLocation(string locationId) {
    return string.Equals(
      LocationEnemyData.NormalizeLocationId(locationId),
      mainMenuFlowLocationId,
      StringComparison.OrdinalIgnoreCase
    );
  }

  bool IsGameplayLocation(string locationId) {
    var normalized = LocationEnemyData.NormalizeLocationId(locationId);
    return !string.IsNullOrWhiteSpace(normalized) &&
           !IsMainMenuLocation(normalized) &&
           IsKnownLocation(normalized);
  }

  void RememberGameplayLocation(string locationId, string source) {
    var normalized = LocationEnemyData.NormalizeLocationId(locationId);
    if (!IsGameplayLocation(normalized)) return;
    TrackEnvironmentHotCacheLocation(normalized, source);
    if (string.Equals(lastKnownGameplayLocationId, normalized, StringComparison.OrdinalIgnoreCase)) return;

    lastKnownGameplayLocationId = normalized;
    if (!ShouldLogLoadFlowDebug()) return;

    Debug.Log(
      "[SingleSceneManager][GameplayLocation] stage=remember" +
      " source=" + ResolveLoadFlowValue(source) +
      " location=" + ResolveLoadFlowValue(normalized) +
      " current_location=" + ResolveLoadFlowValue(LocationManager.currentLocation)
    );
  }

  string ResolveGameplayLocationRequest(string preferredLocationId, out string source) {
    var preferred = LocationEnemyData.NormalizeLocationId(preferredLocationId);
    if (IsGameplayLocation(preferred)) {
      source = "preferred";
      return preferred;
    }

    var current = LocationEnemyData.NormalizeLocationId(LocationManager.currentLocation);
    if (IsGameplayLocation(current)) {
      source = "current";
      return current;
    }

    var lastKnown = LocationEnemyData.NormalizeLocationId(lastKnownGameplayLocationId);
    if (IsGameplayLocation(lastKnown)) {
      source = "last_known";
      return lastKnown;
    }

    source = "default";
    return ResolveDefaultLocation();
  }

  string ResolveDefaultLocation() {
    if (IsKnownLocation(defaultStartLocation)) return defaultStartLocation.Trim();
    if (IsKnownLocation(gameplayFlowFallbackLocationId)) return gameplayFlowFallbackLocationId;
    return LocationEnemyData.GetDefaultLocation();
  }

  string ResolveDefaultDebugLocationId() {
    foreach (var pair in LocationEnemyData.locations) {
      var candidateLocationId = LocationEnemyData.NormalizeLocationId(pair.Key);
      if (!IsGameplayLocation(candidateLocationId)) {
        continue;
      }

      return candidateLocationId;
    }

    return "";
  }

  string ResolveConfiguredDebugLocationId(out string reason) {
    var configuredLocationId = LocationEnemyData.NormalizeLocationId(debugLocationId);
    if (IsGameplayLocation(configuredLocationId)) {
      reason = "configured_location";
      return configuredLocationId;
    }

    if (string.IsNullOrWhiteSpace(configuredLocationId) &&
        debugLocationPrefab != null &&
        LocationEnemyData.TryGetLocationByPrefab(debugLocationPrefab, out var legacyLocationInfo) &&
        legacyLocationInfo != null) {
      var legacyLocationId = LocationEnemyData.NormalizeLocationId(legacyLocationInfo.id);
      if (IsGameplayLocation(legacyLocationId)) {
        reason = "legacy_prefab";
        return legacyLocationId;
      }
    }

    var fallbackLocationId = ResolveDefaultDebugLocationId();
    if (IsGameplayLocation(fallbackLocationId)) {
      reason = string.IsNullOrWhiteSpace(configuredLocationId)
        ? "default_first_location"
        : "fallback_unknown_debug_location";
      return fallbackLocationId;
    }

    reason = string.IsNullOrWhiteSpace(configuredLocationId)
      ? "missing_debug_location"
      : "unknown_debug_location";
    return "";
  }

  string ResolveLocationForStart(bool isNewGame, SaveData loadedSlot) {
    if (startupInDebugGameplay && IsKnownLocation(startupDebugLocationId)) {
      return startupDebugLocationId;
    }

    var resolved = ResolveDefaultLocation();
    if (!isNewGame && loadedSlot != null && loadedSlot.ContainsKey("location")) {
      var loadedLocation = Convert.ToString(loadedSlot["location"]);
      if (IsKnownLocation(loadedLocation)) {
        resolved = loadedLocation.Trim();
      }
    }

    return resolved;
  }

  void RequestLocationLoadForMainMenu() {
    if (!LocationEnemyData.TryGetLocation(mainMenuFlowLocationId, out var locationInfo) || locationInfo == null) {
      if (ShouldLogLoadFlowDebug()) {
        Debug.Log("[SingleSceneManager][MainMenuLocation] skip_request reason=missing_location");
      }
      return;
    }

    if (ShouldLogLoadFlowDebug()) {
      var prefabData = locationInfo.locationPrefabData;
      var clearsActiveLocation = prefabData == null ||
                                 (prefabData.prefab == null && string.IsNullOrWhiteSpace(prefabData.AssetPath));
      Debug.Log(
        "[SingleSceneManager][MainMenuLocation] request" +
        " location=" + mainMenuFlowLocationId +
        " clears_active_location=" + (clearsActiveLocation ? 1 : 0)
      );
    }
    RequestLocationLoad(mainMenuFlowLocationId);
  }

  void RequestLocationLoadForGameplay(string preferredLocationId) {
    var locationId = ResolveGameplayLocationRequest(preferredLocationId, out var source);
    RememberGameplayLocation(locationId, "request:" + source);

    if (ShouldLogLoadFlowDebug()) {
      Debug.Log(
        "[SingleSceneManager][GameplayLocation] stage=request" +
        " source=" + ResolveLoadFlowValue(source) +
        " preferred=" + ResolveLoadFlowValue(LocationEnemyData.NormalizeLocationId(preferredLocationId)) +
        " current_location=" + ResolveLoadFlowValue(LocationManager.currentLocation) +
        " last_known_gameplay=" + ResolveLoadFlowValue(lastKnownGameplayLocationId) +
        " resolved=" + ResolveLoadFlowValue(locationId)
      );
    }

    RequestLocationLoad(locationId);
  }

  void RequestLocationLoad(string locationId) {
    var resolved = LocationEnemyData.ResolveRequestedOrDefault(locationId);
    if (string.IsNullOrWhiteSpace(resolved)) return;
    LogLocationLoadRequest(locationId, resolved);
    ResetLoadingProgressForPhase();
    if (!string.Equals(LocationManager.currentLocation, resolved, StringComparison.OrdinalIgnoreCase)) {
      LocationManager.UpdateLocation(resolved);
      return;
    }
    MessageBus.Send("RequestLocationLoad", resolved);
  }

  void OnLocationUpdated(object payload) {
    if (!Application.isPlaying) return;
    InvalidatePreUnlockTargetCache();
    InvalidateActiveEnemyControllersCache();
    InvalidateCachedPlayerGearController("location_updated");
    cachedGameplayInput = null;
    gameplayInputCacheRefreshedAt = -1f;

    var locationId = payload as string;
    if (string.IsNullOrWhiteSpace(locationId)) {
      locationId = LocationManager.currentLocation;
    }
    locationId = string.IsNullOrWhiteSpace(locationId) ? "" : locationId.Trim();
    RememberGameplayLocation(locationId, "location_updated");
    if (string.Equals(lastPurgedLocationId, locationId, StringComparison.OrdinalIgnoreCase)) return;
    var previousLocationId = lastPurgedLocationId;
    lastPurgedLocationId = locationId;
    LogLocationUpdate(previousLocationId, locationId);
    HandleLocationCacheTransition(previousLocationId, locationId);
  }

  void HandleLocationCacheTransition(string previousLocationId, string currentLocationId) {
    if (string.IsNullOrWhiteSpace(previousLocationId)) return;
    if (string.Equals(previousLocationId, currentLocationId, StringComparison.OrdinalIgnoreCase)) return;

    // Location transition lifecycle: old environment/enemy/effect sets are no longer
    // required once switching to a different location. Evict completed unpinned entries.
    TextureResidencyCache.EvictAllUnpinnedCompleted();
  }

  void ForceBlackscreenVisible(bool visible) {
    if (loadingBlackscreen == null && LoadingScreen != null) {
      var blackscreenTransform = FindChildByName(LoadingScreen.transform, "blackscreen");
      if (blackscreenTransform != null) {
        loadingBlackscreen = blackscreenTransform.gameObject;
        loadingBlackscreenRenderer = loadingBlackscreen.GetComponent<SpriteRenderer>();
      }
    }
    if (loadingBlackscreen == null) return;
    var alpha = visible ? 1f : 0f;
    var spriteRenderer = loadingBlackscreenRenderer;
    if (spriteRenderer != null) {
      var c = spriteRenderer.color;
      if (!Mathf.Approximately(c.a, alpha)) {
        c.a = alpha;
        spriteRenderer.color = c;
      }

      if (loadingBlackscreenPropertyBlock == null) {
        loadingBlackscreenPropertyBlock = new MaterialPropertyBlock();
      }
      var block = loadingBlackscreenPropertyBlock;
      spriteRenderer.GetPropertyBlock(block);
      block.SetFloat("_Alpha", alpha);
      spriteRenderer.SetPropertyBlock(block);
    }
  }

  void SetLoadingBlackscreenHold(bool hold) {
    if (holdBlackscreenOpaqueDuringLoad == hold) return;
    holdBlackscreenOpaqueDuringLoad = hold;
    if (blackscreen != null) {
      blackscreen.enabled = !hold;
    }
    if (hold) {
      ForceBlackscreenVisible(true);
    }
  }

  IEnumerator EnsureBlackscreenClearsAfterUnlockRoutine(Section revealedSection, string overlayTag) {
    var waitSeconds = Mathf.Max(fadeFromBlackSeconds + 0.15f, 0.5f);
    if (waitSeconds > 0f) {
      yield return RevealCleanupDelay;
    }
    if (!holdBlackscreenOpaqueDuringLoad) {
      ForceBlackscreenVisible(false);
    }
    var sectionToReveal = revealedSection == Section.None ? ResolveCurrentSection() : revealedSection;
    var shouldEnableLights = Scene != null &&
                             Scene.activeInHierarchy &&
                             ShouldRestoreSceneLightsForSection(sectionToReveal);
    SetSceneObjectLightsActive(shouldEnableLights);
    if (sectionToReveal == Section.Gameplay) {
      ResumePlayerAnimationAfterLoadingOverlay("reveal_complete");
    }
    SpriteStreamingLoadingState.ReleaseOverlayProtection();
    if (!string.IsNullOrWhiteSpace(overlayTag)) {
      SpriteStreamingLoadingState.EndLoadingOverlay(overlayTag);
    }
    StartDeferredPostRevealWarmupIfNeeded("reveal_complete");
    DisableLoadingUiFeedback(clearText: true, includeLoadingLight: true);
    ReleaseLoadingScreenIfIdle();
    LogSectionTransitionState("reveal_complete", currentSection, sectionToReveal, overlayTag, false);
    var pinReleaseDelay = Mathf.Max(postUnlockPinReleaseDelaySeconds, 0f);
    if (pinReleaseDelay > 0f) {
      yield return PostUnlockPinReleaseDelay;
    }
    var releaseOutstandingCap = Mathf.Max(postUnlockPinReleaseMaxOutstanding, 0);
    var releaseTimeout = Mathf.Max(postUnlockPinReleaseTimeoutSeconds, 0f);
    var releaseStartedAt = Time.realtimeSinceStartup;
    while (true) {
      TextureResidencyCache.PumpOncePerFrame();
      var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
      var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
      var outstanding = Mathf.Max(queue.queuedCount + queue.inFlightCount + deferredPending, 0);
      if (outstanding <= releaseOutstandingCap) break;
      if (releaseTimeout > 0f && (Time.realtimeSinceStartup - releaseStartedAt) >= releaseTimeout) break;
      yield return null;
    }
    ReleasePreUnlockResidentPins("post_unlock");
    ReleaseLoadingScreenIfIdle();
    unlockFadeFailSafeRoutine = null;
  }

  void TickLoadingStallEmergencyUnlock() {
    if (!enableLoadingStallEmergencyUnlock || !Application.isPlaying) {
      loadingStallStartedAt = -1f;
      return;
    }

    var loadingActive = holdBlackscreenOpaqueDuringLoad || SpriteStreamingLoadingState.IsLoadingOverlayActive;
    if (!loadingActive) {
      loadingStallStartedAt = -1f;
      return;
    }

    TextureResidencyCache.Pump();
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
    var queueFullyDrained = queue.queuedCount <= 0 && queue.inFlightCount <= 0 && deferredPending <= 0;
    if (!queueFullyDrained) {
      loadingStallStartedAt = -1f;
      return;
    }

    if (loadingStallStartedAt < 0f) {
      loadingStallStartedAt = Time.realtimeSinceStartup;
      return;
    }

    var elapsed = Time.realtimeSinceStartup - loadingStallStartedAt;
    var timeout = Mathf.Max(loadingStallEmergencyUnlockSeconds, 1f);
    if (elapsed < timeout) return;

    ForceReleaseLoadingState();
    loadingStallStartedAt = -1f;
  }

  void ForceReleaseLoadingState() {
    if (sectionTransitionRoutine != null) {
      StopCoroutine(sectionTransitionRoutine);
      sectionTransitionRoutine = null;
    }
    if (startGameRoutine != null) {
      StopCoroutine(startGameRoutine);
      startGameRoutine = null;
    }
    if (resumeGameplayRoutine != null) {
      StopCoroutine(resumeGameplayRoutine);
      resumeGameplayRoutine = null;
    }
    if (startupGameplayRoutine != null) {
      StopCoroutine(startupGameplayRoutine);
      startupGameplayRoutine = null;
    }
    if (unlockFadeFailSafeRoutine != null) {
      StopCoroutine(unlockFadeFailSafeRoutine);
      unlockFadeFailSafeRoutine = null;
    }

    StopStartupFadeWatchdog();
    loadingStallStartedAt = -1f;

    var sectionToReveal = ResolveActiveSectionFromHierarchy();
    if (sectionToReveal == Section.None) {
      var fallbackSection = pendingRevealSection != Section.None ? pendingRevealSection : currentSection;
      if (fallbackSection != Section.None) {
        ApplySectionActivation(fallbackSection);
        sectionToReveal = fallbackSection;
      }
    }
    pendingRevealSection = Section.None;

    FinalizeLoadingProgressForRelease();
    DisableLoadingUiFeedback(clearText: true, includeLoadingLight: true);
    ReleasePreUnlockResidentPins("force_release");
    StopDeferredPostRevealWarmup("force_release");
    SetLoadingBlackscreenHold(false);
    ForceBlackscreenVisible(false);
    ResumePlayerAnimationAfterLoadingOverlay("force_release");
    SpriteStreamingLoadingState.ForceClearLoadingOverlay();
    RestoreSceneLightingForCurrentActivation();
    ReleaseLoadingScreenIfIdle();
    if (sectionToReveal != Section.None) {
      ApplyInputForSection(sectionToReveal);
      return;
    }
    ApplyInputMapForCurrentUiState(preferGameplayWhenNoUi: false);
  }

  IEnumerator FadeFromBlackRoutine(string overlayTag, Section revealedSection) {
    FinalizeLoadingProgressForRelease();
    SetLoadingBlackscreenHold(false);
    if (blackscreen != null) {
      PlayBlackscreen("alphaOut");
    }
    else {
      ForceBlackscreenVisible(false);
    }
    if (unlockFadeFailSafeRoutine != null) {
      StopCoroutine(unlockFadeFailSafeRoutine);
    }
    unlockFadeFailSafeRoutine = StartCoroutine(EnsureBlackscreenClearsAfterUnlockRoutine(revealedSection, overlayTag));
    var waitSeconds = Mathf.Max(fadeFromBlackSeconds, 0f);
    if (waitSeconds > 0f) {
      yield return FadeFromBlackDelay;
    }
  }

  void SetActiveSafe(GameObject target, bool active) {
    if (target == null) return;
    target.SetActive(active);
  }
}
