using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public class GameplayDialogController : MonoBehaviour {
  const string AutoDialogTrigger = "auto";
  const string DialogEsperPortraitLibrary = "Dialog/DialogEsper";
  const string DefaultPortraitLabelPrefix = "Base";
  const string PlayerNameColorGroup = ShaderColors.SecondaryGroup;
  const string AllyColorName = "Green";
  const string EnemyColorName = "Red";

  static readonly string[] DialogBoxBackgroundNames = { "dialogBoxBack", "dialogBox" };
  static readonly string[] PlayerNameBackgroundNames = { "playerNameBack", "playerName" };
  static readonly string[] PlayerNameTextNames = { "playerNameText" };
  static readonly string[] PlayerAvatarBackgroundNames = { "playerAvatarBack" };
  static readonly string[] PlayerPortraitNames = { "playerAvatar" };
  static readonly string[] OtherNameBackgroundNames = { "otherNameBack", "enemyName" };
  static readonly string[] OtherNameTextNames = { "otherNameText", "enemyNameText" };
  static readonly string[] OtherAvatarBackgroundNames = { "otherAvatarBack", "enemyAvatarBack" };
  static readonly string[] OtherPortraitNames = { "otherAvatar", "enemyAvatar" };
  static readonly HashSet<string> ValidEmotions = new(StringComparer.OrdinalIgnoreCase) {
    "Angry",
    "Disgust",
    "Happy",
    "Laugh",
    "Love",
    "Normal",
    "Sad",
    "Surprise"
  };
  static readonly Dictionary<string, PortraitSpeakerConfig> KnownPortraitSpeakerConfigs = new(StringComparer.OrdinalIgnoreCase) {
    ["Alchemist"] = new PortraitSpeakerConfig("Dialog/DialogAlchemist", DialogOtherType.Ally),
    ["Blacksmith"] = new PortraitSpeakerConfig("Dialog/DialogBlacksmith", DialogOtherType.Ally),
    ["Gemcrafter"] = new PortraitSpeakerConfig("Dialog/DialogGemcrafter", DialogOtherType.Ally),
    ["Gem Crafter"] = new PortraitSpeakerConfig("Dialog/DialogGemcrafter", DialogOtherType.Ally),
    ["Master"] = new PortraitSpeakerConfig("Dialog/DialogMaster", DialogOtherType.Ally)
  };

  readonly struct PortraitSpeakerConfig {
    public readonly string libraryName;
    public readonly DialogOtherType otherType;

    public PortraitSpeakerConfig(string libraryName, DialogOtherType otherType) {
      this.libraryName = libraryName ?? "";
      this.otherType = otherType;
    }
  }

  public enum DialogSpeakerSide {
    Esperanza = 0,
    Enemy = 1
  }

  public enum DialogOtherType {
    Auto = 0,
    Ally = 1,
    Enemy = 2
  }

  [Serializable]
  public class GameplayDialogNode {
    public string locationId = "";
    public string speakerId = "";
    public int lineNumber;
    public string trigger = "";
    public DialogSpeakerSide speaker = DialogSpeakerSide.Esperanza;
    public DialogOtherType otherType = DialogOtherType.Auto;
    public string speakerName = "Esperanza";
    public string avatarForm = "";
    public string portraitLibraryName = "";
    public string emotion = "Normal";
    [TextArea(2, 6)]
    public string text = "";
  }

  [Serializable]
  public class GameplayDialogPlayRequest {
    public string source = "runtime";
    public List<GameplayDialogNode> nodes = new();
  }

  sealed class PendingDialogRequest {
    public string locationId = "";
    public string trigger = "";
    public string source = "";
  }

  sealed class SpeakerWidgets {
    public readonly string sideKey;
    public readonly string defaultName;
    public GameObject nameBackgroundObject;
    public GameObject nameTextObject;
    public GameObject avatarBackgroundObject;
    public GameObject portraitObject;
    public FontText nameText;
    public SpriteWithNormals nameBackground;
    public SpriteWithNormals avatarBackground;
    public SpriteWithNormals portrait;

    public SpeakerWidgets(string sideKey, string defaultName) {
      this.sideKey = sideKey ?? "";
      this.defaultName = defaultName ?? "";
    }
  }

  [Header("Typewriter")]
  [SerializeField, Min(1f)] float charactersPerSecond = 72f;
  [Header("Runtime Children")]
  [SerializeField] Vector3 portraitLocalPosition = Vector3.zero;
  [SerializeField] Vector3 portraitLocalScale = Vector3.one;
  [SerializeField] int portraitSortingOrderOffset = 1;
  [SerializeField] Vector3 speakerNameTextLocalPosition = Vector3.zero;
  [SerializeField] Vector3 speakerNameTextLocalScale = Vector3.one;
  [SerializeField] int speakerNameSortingOrderOffset = 1;
  [Header("Debug")]
  [SerializeField] bool debugTreatAllDialogAsUnseen;

  readonly List<Action> actions = new();
  readonly SpeakerWidgets playerWidgets = new("player", "Esperanza");
  readonly SpeakerWidgets otherWidgets = new("other", "Other");
  readonly List<GameplayDialogNode> sequenceNodes = new();
  readonly List<GameplayDialogNode> resolvedLocationSequence = new();
  readonly List<Action> locationTriggerActions = new();
  readonly List<string> resolvedLocationTriggers = new();
  readonly List<PendingDialogRequest> pendingDialogRequests = new();
  readonly List<SpriteRenderer> fontTextRendererBuffer = new();

  GameObject dialogRoot;
  GameObject dialogBoxObject;
  FontText dialogText;
  SpriteWithNormals dialogBoxBackground;
  bool dialogueActive;
  bool dialogStateReady;
  int currentNodeIndex = -1;
  string currentFullText = "";
  string activeLocationDialogId = "";
  int visibleCharacterCount;
  float typewriterCharacterProgress;
  int pauseDialogSuspendToken;
  bool debugSeenOverrideInitialized;
  bool appliedDebugSeenOverride;
  string suppressedAutoDialogLocationId = "";

  public bool IsDialogActive => dialogueActive;
  public bool HasResolvedUiReferencesForLoadingProgress {
    get {
      EnsureResolved();
      return dialogRoot != null && dialogText != null;
    }
  }
  public string ResolvedUiReferencesBlockerSummary {
    get {
      EnsureResolved();
      if (dialogRoot == null && dialogText == null) return "dialogRoot_and_dialogText_Null";
      if (dialogRoot == null) return "dialogRoot_Null";
      if (dialogText == null) return "dialogText_Null";
      return "None";
    }
  }
  public bool IsReadyForLoadingProgress =>
    enabled &&
    HasResolvedUiReferencesForLoadingProgress &&
    IsDialogStateReadyForLoadingProgress();
  public bool HasPendingLocationDialog => HasPendingDialogRequestForActiveLocation() || HasPendingAutoLocationDialog();
  public bool IsBlockingGameplayInput => dialogueActive || HasPendingLocationDialog;

  static bool ShouldLogDialogDebug() {
    if (!SpriteStreamingRuntimeSettings.EnableVerboseRuntimeConsoleLogs) {
      return false;
    }
    return Application.isEditor || Debug.isDebugBuild;
  }

  bool IsDialogStateReadyForLoadingProgress() {
    if (!dialogStateReady && DialogController.IsStateReadyForCurrentSlot) {
      dialogStateReady = true;
    }
    return dialogStateReady;
  }

  bool IsEditorDebugSeenOverrideEnabled() {
    return Application.isEditor && debugTreatAllDialogAsUnseen;
  }

  bool ShouldSuspendDialogueForPause() {
    if (!dialogueActive) {
      return false;
    }

    pauseDialogSuspendToken = SingleSceneManager.CurrentPauseDialogSuspendToken;
    return pauseDialogSuspendToken > 0;
  }

  bool TryResumeDialogueAfterPause(string source) {
    if (!dialogueActive || pauseDialogSuspendToken <= 0) {
      return false;
    }

    var resumeToken = pauseDialogSuspendToken;
    pauseDialogSuspendToken = 0;
    if (!SingleSceneManager.TryConsumePauseDialogResumeToken(resumeToken)) {
      if (ShouldLogDialogDebug()) {
        RuntimeLog.Log(
          "[GameplayDialogController] Dropped suspended dialog because pause resume token was not available" +
          " token=" + resumeToken +
          " source='" + (source ?? "") + "'"
        );
      }
      ResetDialogueWithoutNotification("pause_resume_missed");
      return false;
    }

    if (currentNodeIndex < 0 || currentNodeIndex >= sequenceNodes.Count) {
      if (ShouldLogDialogDebug()) {
        Debug.LogWarning(
          "[GameplayDialogController] Suspended dialog had an invalid node index" +
          " token=" + resumeToken +
          " node_index=" + currentNodeIndex +
          " node_count=" + sequenceNodes.Count
        );
      }
      ResetDialogueWithoutNotification("pause_resume_invalid_index");
      return false;
    }

    SetDialogVisible(true, source + "_pause_resume");
    RefreshCurrentNode(source + "_pause_resume");
    ApplyVisibleText();

    if (ShouldLogDialogDebug()) {
      RuntimeLog.Log(
        "[GameplayDialogController] Resumed active dialog after pause" +
        " token=" + resumeToken +
        " node_index=" + currentNodeIndex +
        " visible_chars=" + visibleCharacterCount +
        " total_chars=" + currentFullText.Length
      );
    }

    return true;
  }

  void ResetPauseDialogueResumeState() {
    pauseDialogSuspendToken = 0;
  }

  void Awake() {
    SyncDebugSeenOverride("awake", force: true);
  }

  void OnEnable() {
    EnsureResolved();
    RegisterHandlers();
    dialogStateReady = DialogController.IsStateReadyForCurrentSlot;
    activeLocationDialogId = ResolveLocationId(LocationManager.currentLocation);
    ResetAutoDialogRetryState();
    RegisterLocationTriggerHandlers(activeLocationDialogId, "enable");
    if (TryResumeDialogueAfterPause("enable")) {
      return;
    }
    SetDialogVisible(false, "enable");
    if (dialogStateReady) {
      TryPlayPendingDialogRequest("enable");
    }
  }

  void Start() {
    SetDialogVisible(false, "start");
  }

  void Update() {
    TickTypewriter();
    TryPlayPendingDialogRequest("update");
  }

  void OnDisable() {
    var suspendForPause = ShouldSuspendDialogueForPause();
    UnregisterHandlers();
    if (suspendForPause) {
      SetDialogVisible(false, "pause_suspend");
      if (ShouldLogDialogDebug()) {
        RuntimeLog.Log(
          "[GameplayDialogController] Suspended active dialog for pause" +
          " token=" + pauseDialogSuspendToken +
          " node_index=" + currentNodeIndex +
          " visible_chars=" + visibleCharacterCount +
          " total_chars=" + currentFullText.Length
        );
      }
      return;
    }

    ResetPauseDialogueResumeState();
    ResetAutoDialogRetryState();
    ClearPendingDialogRequests();
    ClearLocationTriggerHandlers();
    activeLocationDialogId = "";
    dialogStateReady = false;
    if (dialogueActive) {
      StopDialogue("disable");
      return;
    }

    SetDialogVisible(false, "disable");
  }

  void OnTransformChildrenChanged() {
    if (!isActiveAndEnabled) {
      return;
    }

    EnsureResolved(force: true);
    if (dialogueActive) {
      RefreshCurrentNode("children_changed");
    }
  }

  void OnValidate() {
    if (!Application.isPlaying) {
      return;
    }

    SyncDebugSeenOverride("validate");
  }

  void SyncDebugSeenOverride(string source, bool force = false) {
    var effectiveDebugSeenOverride = IsEditorDebugSeenOverrideEnabled();
    if (!force &&
        debugSeenOverrideInitialized &&
        appliedDebugSeenOverride == effectiveDebugSeenOverride) {
      return;
    }

    DialogController.SetDebugTreatAllDialogAsUnseen(effectiveDebugSeenOverride, source);
    ResetAutoDialogRetryState();
    appliedDebugSeenOverride = effectiveDebugSeenOverride;
    debugSeenOverrideInitialized = true;
    if (!ShouldLogDialogDebug()) {
      return;
    }

    RuntimeLog.Log(
      "[GameplayDialogController] Debug seen override configured=" + (debugTreatAllDialogAsUnseen ? 1 : 0) +
      " enabled=" + (effectiveDebugSeenOverride ? 1 : 0) +
      " source='" + (source ?? "") + "'"
    );
  }

  public void PlayAuthoredSequence(string source = "runtime") {
    if (sequenceNodes.Count == 0) {
      Debug.LogWarning("[GameplayDialogController] Ignored play request because no dialog sequence nodes exist.");
      StopDialogue(source + "_empty");
      return;
    }

    StartDialogue(source, restartExisting: true);
  }

  public void PlaySequence(List<GameplayDialogNode> nodes, string source = "runtime") {
    ReplaceSequenceNodes(nodes);
    if (ShouldLogDialogDebug()) {
      RuntimeLog.Log(
        "[GameplayDialogController] Assigned runtime sequence source='" + (source ?? "") +
        "' node_count=" + sequenceNodes.Count
      );
    }
    PlayAuthoredSequence(source);
  }

  public void StopDialogue(string reason = "runtime") {
    var wasActive = dialogueActive;
    dialogueActive = false;
    ResetPauseDialogueResumeState();
    ResetAutoDialogRetryState();
    currentNodeIndex = -1;
    currentFullText = "";
    visibleCharacterCount = 0;
    typewriterCharacterProgress = 0f;
    ApplyText(dialogText, "");
    ClearSpeakerState();
    SetDialogVisible(false, reason);

    if (!wasActive) {
      return;
    }

    if (ShouldLogDialogDebug()) {
      RuntimeLog.Log("[GameplayDialogController] Dialog finished reason='" + (reason ?? "") + "'");
    }
    MessageBus.Send("dialog.finished", reason);
    RegisterLocationTriggerHandlers(activeLocationDialogId, "dialog_finished");
    TryPlayPendingDialogRequest("dialog_finished");
  }

  void ResetDialogueWithoutNotification(string source) {
    ResetPauseDialogueResumeState();
    ResetAutoDialogRetryState();
    dialogueActive = false;
    currentNodeIndex = -1;
    currentFullText = "";
    visibleCharacterCount = 0;
    typewriterCharacterProgress = 0f;
    sequenceNodes.Clear();
    ClearPendingDialogRequests();
    ClearLocationTriggerHandlers();
    activeLocationDialogId = "";
    ApplyText(dialogText, "");
    ClearSpeakerState();
    SetDialogVisible(false, source);
  }

  void RegisterHandlers() {
    if (actions.Count > 0) {
      return;
    }

    actions.Add(MessageBus.On("dialog.progress", o => OnProgress(o)));
    actions.Add(MessageBus.On("dialog.playAuthored", o => PlayAuthoredSequence("message_bus")));
    actions.Add(MessageBus.On("dialog.play", o => PlayRequestedSequence(o)));
    actions.Add(MessageBus.On("dialog.stop", o => StopDialogue("message_bus")));
    actions.Add(MessageBus.On(CharacterMessageTopics.FormChanged, form => OnFormChanged(form)));
    actions.Add(MessageBus.On("LocationLoaded", o => OnLocationLoaded(o)));
    actions.Add(MessageBus.On(CharacterMessageTopics.DialogStateReady, source => OnDialogStateReady(source)));
  }

  void UnregisterHandlers() {
    for (var i = 0; i < actions.Count; i++) {
      actions[i]?.Invoke();
    }
    actions.Clear();
    ClearLocationTriggerHandlers();
  }

  void EnsureResolved(bool force = false) {
    if (force || dialogRoot == null) {
      dialogRoot = FindChildRecursive(transform, "Dialog")?.gameObject;
    }
    if (force || dialogBoxObject == null) {
      dialogBoxObject = dialogRoot != null ? FindFirstChildRecursive(dialogRoot.transform, DialogBoxBackgroundNames)?.gameObject : null;
    }
    if (force || dialogText == null) {
      dialogText = dialogRoot != null ? FindFontText(dialogRoot.transform, "dialogText") : null;
    }
    if (force || dialogBoxBackground == null) {
      dialogBoxBackground = dialogBoxObject != null ? dialogBoxObject.GetComponent<SpriteWithNormals>() : null;
    }

    ResolveSpeakerWidgets(
      playerWidgets,
      force,
      PlayerNameBackgroundNames,
      PlayerNameTextNames,
      PlayerAvatarBackgroundNames,
      PlayerPortraitNames
    );
    ResolveSpeakerWidgets(
      otherWidgets,
      force,
      OtherNameBackgroundNames,
      OtherNameTextNames,
      OtherAvatarBackgroundNames,
      OtherPortraitNames
    );
  }

  void ResolveSpeakerWidgets(
    SpeakerWidgets widgets,
    bool force,
    string[] nameBackgroundNames,
    string[] nameTextNames,
    string[] avatarBackgroundNames,
    string[] portraitNames
  ) {
    if (widgets == null || dialogRoot == null) {
      return;
    }

    if (force || widgets.nameBackgroundObject == null) {
      widgets.nameBackgroundObject = FindFirstChildRecursive(dialogRoot.transform, nameBackgroundNames)?.gameObject;
    }
    if (force || widgets.nameTextObject == null) {
      widgets.nameTextObject = FindFirstChildRecursive(dialogRoot.transform, nameTextNames)?.gameObject;
    }
    if (force || widgets.avatarBackgroundObject == null) {
      widgets.avatarBackgroundObject = FindFirstChildRecursive(dialogRoot.transform, avatarBackgroundNames)?.gameObject;
    }
    if (force || widgets.portraitObject == null) {
      widgets.portraitObject = FindFirstChildRecursive(dialogRoot.transform, portraitNames)?.gameObject;
    }
    if (force || widgets.nameBackground == null) {
      widgets.nameBackground = GetSpriteWithNormals(widgets.nameBackgroundObject);
    }
    if (force || widgets.avatarBackground == null) {
      widgets.avatarBackground = GetSpriteWithNormals(widgets.avatarBackgroundObject);
    }
    if (force || widgets.nameText == null) {
      widgets.nameText = EnsureSpeakerNameText(widgets);
    }
    if (force || widgets.portrait == null) {
      widgets.portrait = EnsureSpeakerPortrait(widgets);
    }
  }

  FontText EnsureSpeakerNameText(SpeakerWidgets widgets) {
    if (widgets == null) {
      return null;
    }

    if (widgets.nameTextObject != null) {
      var directText = widgets.nameTextObject.GetComponent<FontText>();
      if (directText != null) {
        return directText;
      }

      var nestedText = widgets.nameTextObject.GetComponentInChildren<FontText>(includeInactive: true);
      if (nestedText != null) {
        widgets.nameTextObject = nestedText.gameObject;
        return nestedText;
      }
    }

    var existing = widgets.nameBackgroundObject != null
      ? widgets.nameBackgroundObject.GetComponentInChildren<FontText>(includeInactive: true)
      : null;
    if (existing != null) {
      widgets.nameTextObject = existing.gameObject;
      return existing;
    }

    if (dialogText == null || dialogText.characterPrefab == null) {
      Debug.LogWarning(
        "[GameplayDialogController] Missing dialog text template while creating speaker name text side='" +
        widgets.sideKey + "'"
      );
      return null;
    }

    var parentTransform = widgets.nameBackgroundObject != null
      ? widgets.nameBackgroundObject.transform
      : dialogRoot != null ? dialogRoot.transform : null;
    if (parentTransform == null) {
      return null;
    }

    var textObject = new GameObject(widgets.sideKey + "NameText");
    textObject.transform.SetParent(parentTransform, false);
    textObject.transform.localPosition = speakerNameTextLocalPosition;
    textObject.transform.localScale = speakerNameTextLocalScale;
    widgets.nameTextObject = textObject;

    var parentRenderer = widgets.nameBackgroundObject != null ? widgets.nameBackgroundObject.GetComponent<SpriteRenderer>() : null;
    var textRenderer = textObject.AddComponent<SpriteRenderer>();
    if (parentRenderer != null) {
      textRenderer.sortingLayerID = parentRenderer.sortingLayerID;
      textRenderer.sortingOrder = parentRenderer.sortingOrder + speakerNameSortingOrderOffset;
      textRenderer.maskInteraction = parentRenderer.maskInteraction;
      textRenderer.sharedMaterial = parentRenderer.sharedMaterial;
      textRenderer.color = new Color(1f, 1f, 1f, 0f);
    }

    var fontText = textObject.AddComponent<FontText>();
    CopyFontTemplate(dialogText, fontText);
    fontText.justifyX = "center";
    fontText.justifyY = "center";
    fontText.content = "";
    fontText.Generate();

    if (ShouldLogDialogDebug()) {
      RuntimeLog.Log(
        "[GameplayDialogController] Created runtime speaker name text side='" + widgets.sideKey +
        "' name_root='" + (widgets.nameBackgroundObject != null ? widgets.nameBackgroundObject.name : parentTransform.name) + "'"
      );
    }
    return fontText;
  }

  SpriteWithNormals EnsureSpeakerPortrait(SpeakerWidgets widgets) {
    if (widgets == null) {
      return null;
    }

    if (widgets.portraitObject != null) {
      var directPortrait = widgets.portraitObject.GetComponent<SpriteWithNormals>();
      if (directPortrait != null) {
        return directPortrait;
      }
    }

    var existing = widgets.portraitObject != null ? FindPortraitSprite(widgets.portraitObject.transform) : null;
    if (existing != null) {
      widgets.portraitObject = existing.gameObject;
      return existing;
    }

    var parentTransform = widgets.avatarBackgroundObject != null
      ? widgets.avatarBackgroundObject.transform
      : dialogRoot != null ? dialogRoot.transform : null;
    if (parentTransform == null) {
      return null;
    }

    var portraitObject = new GameObject(widgets.sideKey + "Portrait");
    portraitObject.transform.SetParent(parentTransform, false);
    portraitObject.transform.localPosition = portraitLocalPosition;
    portraitObject.transform.localScale = portraitLocalScale;
    widgets.portraitObject = portraitObject;

    var parentRenderer = widgets.avatarBackgroundObject != null ? widgets.avatarBackgroundObject.GetComponent<SpriteRenderer>() : null;
    var portraitRenderer = portraitObject.AddComponent<SpriteRenderer>();
    if (parentRenderer != null) {
      portraitRenderer.sortingLayerID = parentRenderer.sortingLayerID;
      portraitRenderer.sortingOrder = parentRenderer.sortingOrder + portraitSortingOrderOffset;
      portraitRenderer.maskInteraction = parentRenderer.maskInteraction;
      portraitRenderer.sharedMaterial = parentRenderer.sharedMaterial;
      portraitRenderer.color = Color.white;
    }

    var portrait = portraitObject.AddComponent<SpriteWithNormals>();
    portrait.SetLibraryName(DialogEsperPortraitLibrary);
    portrait.SetLabelPrefix(EsperanzaForms.GetActive());
    portrait.SetAnimation("Normal");
    portrait.SetIsAnimation(false);
    portrait.ForceUpdateSpriteAndNormal(0);

    if (ShouldLogDialogDebug()) {
      RuntimeLog.Log(
        "[GameplayDialogController] Created runtime speaker portrait side='" + widgets.sideKey +
        "' avatar_root='" + (widgets.avatarBackgroundObject != null ? widgets.avatarBackgroundObject.name : parentTransform.name) + "'"
      );
    }
    return portrait;
  }

  SpriteWithNormals FindPortraitSprite(Transform root) {
    if (root == null) {
      return null;
    }

    var spriteAtRoot = root.GetComponent<SpriteWithNormals>();
    if (spriteAtRoot != null) {
      return spriteAtRoot;
    }

    for (var i = 0; i < root.childCount; i++) {
      var childPortrait = FindPortraitSprite(root.GetChild(i));
      if (childPortrait != null) {
        return childPortrait;
      }
    }

    return null;
  }

  void StartDialogue(string source, bool restartExisting) {
    EnsureResolved();
    if (dialogRoot == null || dialogText == null) {
      Debug.LogWarning(
        "[GameplayDialogController] Missing dialog UI references root=" + (dialogRoot != null ? 1 : 0) +
        " dialog_text=" + (dialogText != null ? 1 : 0)
      );
      return;
    }

    if (dialogueActive && !restartExisting) {
      return;
    }

    if (dialogueActive) {
      if (ShouldLogDialogDebug()) {
        RuntimeLog.Log("[GameplayDialogController] Restarting active dialog source='" + (source ?? "") + "'");
      }
    }

    dialogueActive = true;
    currentNodeIndex = -1;
    currentFullText = "";
    visibleCharacterCount = 0;
    typewriterCharacterProgress = 0f;
    SetDialogVisible(true, source);

    if (ShouldLogDialogDebug()) {
      RuntimeLog.Log(
        "[GameplayDialogController] Dialog started source='" + (source ?? "") +
        "' node_count=" + sequenceNodes.Count +
        " active_form='" + EsperanzaForms.GetActive() + "'"
      );
    }
    MessageBus.Send("dialog.started", source);
    AdvanceNode(source + "_start");
  }

  void OnProgress(object payload) {
    if (!dialogueActive || !IsPressed(payload)) {
      return;
    }

    if (IsTyping()) {
      CompleteTypewriter("progress");
      return;
    }

    AdvanceNode("progress");
  }

  void OnFormChanged(object payload) {
    if (!dialogueActive) {
      return;
    }

    RefreshCurrentNode("form_changed");
  }

  void OnLocationLoaded(object payload) {
    var previousLocationDialogId = activeLocationDialogId;
    activeLocationDialogId = ResolveLocationId(payload);
    ResetAutoDialogRetryState();
    if (string.IsNullOrWhiteSpace(activeLocationDialogId)) {
      ClearPendingDialogRequests();
      ClearLocationTriggerHandlers();
      return;
    }

    ClearPendingDialogRequestsForLocation(activeLocationDialogId, previousLocationDialogId, "location_loaded");
    DialogController.BeginLocationDialogSession(activeLocationDialogId, "location_loaded");
    RegisterLocationTriggerHandlers(activeLocationDialogId, "location_loaded");
    if (!dialogStateReady) {
      if (ShouldLogDialogDebug()) {
        RuntimeLog.Log(
          "[GameplayDialogController] Deferred location dialog until dialog state is ready" +
          " location='" + activeLocationDialogId + "'"
        );
      }
      return;
    }

    TryPlayPendingDialogRequest("location_loaded");
  }

  void OnDialogStateReady(object payload) {
    dialogStateReady = true;
    if (string.IsNullOrWhiteSpace(activeLocationDialogId)) {
      activeLocationDialogId = ResolveLocationId(LocationManager.currentLocation);
    }
    ResetAutoDialogRetryState();

    if (ShouldLogDialogDebug()) {
      RuntimeLog.Log(
        "[GameplayDialogController] Dialog state ready source='" + (payload != null ? payload.ToString() : "") +
        "' location='" + activeLocationDialogId + "'"
      );
    }
    RegisterLocationTriggerHandlers(activeLocationDialogId, "dialog_state_ready");
    TryPlayPendingDialogRequest("dialog_state_ready");
  }

  void PlayRequestedSequence(object payload) {
    if (payload is GameplayDialogPlayRequest request) {
      PlaySequence(request.nodes, string.IsNullOrWhiteSpace(request.source) ? "message_bus" : request.source);
      return;
    }

    if (payload is List<GameplayDialogNode> nodeList) {
      PlaySequence(nodeList, "message_bus");
      return;
    }

    if (payload is GameplayDialogNode[] nodeArray) {
      ReplaceSequenceNodes(nodeArray);
      PlayAuthoredSequence("message_bus");
      return;
    }

    PlayAuthoredSequence(payload != null ? payload.ToString() : "message_bus");
  }

  void AdvanceNode(string source) {
    if (!dialogueActive) {
      return;
    }

    currentNodeIndex += 1;
    if (currentNodeIndex < 0 || currentNodeIndex >= sequenceNodes.Count) {
      StopDialogue(source + "_complete");
      return;
    }

    ShowNode(sequenceNodes[currentNodeIndex], source);
  }

  void RefreshCurrentNode(string source) {
    if (!dialogueActive || currentNodeIndex < 0 || currentNodeIndex >= sequenceNodes.Count) {
      return;
    }

    ApplyNodeVisuals(sequenceNodes[currentNodeIndex], source);
    ApplyVisibleText();
  }

  void ShowNode(GameplayDialogNode node, string source) {
    if (node == null) {
      StopDialogue(source + "_null_node");
      return;
    }

    var activeForm = ResolveNodeForm(node);
    var emotion = ResolveEmotion(node.emotion);
    var otherType = ResolveDialogOtherType(node);
    var hasPortrait = TryResolvePortraitPresentation(node, activeForm, out var portraitLibraryName, out _);
    currentFullText = node.text ?? "";
    visibleCharacterCount = 0;
    typewriterCharacterProgress = 0f;
    ApplyNodeVisuals(node, source);
    ApplyVisibleText();
    var markedSeen = DialogController.MarkSeen(node, source + "_show");

    if (ShouldLogDialogDebug()) {
      RuntimeLog.Log(
        "[GameplayDialogController] Showing node index=" + currentNodeIndex +
        " source='" + (source ?? "") + "'" +
        " location='" + (node.locationId ?? "") + "'" +
        " trigger='" + ResolveDialogTrigger(node.trigger) + "'" +
        " speaker='" + ResolveSpeakerName(node) + "'" +
        " speaker_id='" + (node.speakerId ?? "") + "'" +
        " side='" + node.speaker + "'" +
        " other_type='" + otherType + "'" +
        " line=" + node.lineNumber +
        " emotion='" + emotion + "'" +
        " text_length=" + currentFullText.Length +
        " form='" + activeForm + "'" +
        " portrait_library='" + (hasPortrait ? portraitLibraryName : "-") + "'" +
        " marked_seen=" + (markedSeen ? 1 : 0)
      );
    }
  }

  void ApplyNodeVisuals(GameplayDialogNode node, string source) {
    if (node == null) {
      return;
    }

    var activeForm = ResolveNodeForm(node);
    var emotion = ResolveEmotion(node.emotion);
    var speakerName = ResolveSpeakerName(node);
    var otherSpeakerActive = node.speaker == DialogSpeakerSide.Enemy;
    var playerSpeakerActive = !otherSpeakerActive;

    ApplyDialogTheme(activeForm);
    SetSpeakerActive(
      playerWidgets,
      playerSpeakerActive,
      playerSpeakerActive ? node : null,
      activeForm,
      emotion,
      playerSpeakerActive ? speakerName : "",
      source
    );
    SetSpeakerActive(
      otherWidgets,
      otherSpeakerActive,
      otherSpeakerActive ? node : null,
      activeForm,
      emotion,
      otherSpeakerActive ? speakerName : "",
      source
    );
    LogSpeakerWidgetState(playerWidgets, playerSpeakerActive, source);
    LogSpeakerWidgetState(otherWidgets, otherSpeakerActive, source);
  }

  void ApplyDialogTheme(string formName) {
    ApplyChangingUiLabel(dialogBoxBackground, formName, "dialog_box");
    ApplyChangingUiLabel(playerWidgets.nameBackground, formName, "player_name_bg");
    ApplyChangingUiLabel(otherWidgets.nameBackground, formName, "other_name_bg");
    ApplyChangingUiLabel(playerWidgets.avatarBackground, formName, "player_avatar_bg");
    ApplyChangingUiLabel(otherWidgets.avatarBackground, formName, "other_avatar_bg");
  }

  void SetSpeakerActive(
    SpeakerWidgets widgets,
    bool active,
    GameplayDialogNode node,
    string formName,
    string emotion,
    string speakerName,
    string source
  ) {
    if (widgets == null) {
      return;
    }

    SetGameObjectActive(widgets.nameBackgroundObject, active);
    SetGameObjectActive(widgets.nameTextObject, active);
    SetGameObjectActive(widgets.avatarBackgroundObject, active);
    if (widgets.nameText != null) {
      ApplyText(widgets.nameText, active ? speakerName : "");
      if (active) {
        ApplySpeakerNameColor(widgets, node, formName);
      }
    }
    if (widgets.portrait != null) {
      var portraitVisible = active &&
        TryApplyPortraitSprite(widgets.portrait, node, formName, emotion, widgets.sideKey, source);
      SetGameObjectActive(widgets.portrait.gameObject, portraitVisible);
    }
  }

  void ApplyChangingUiLabel(SpriteWithNormals background, string formName, string debugKey) {
    if (background == null || string.IsNullOrWhiteSpace(formName)) {
      return;
    }

    var changed = false;
    if (!string.Equals(background.labelPrefix, formName, StringComparison.Ordinal)) {
      background.SetLabelPrefix(formName);
      changed = true;
    }
    if (background.DoNotRender) {
      background.SetDoNotRender(false);
      changed = true;
    }
    if (!changed) {
      return;
    }

    background.ForceUpdateSpriteAndNormal();
    if (ShouldLogDialogDebug()) {
      RuntimeLog.Log(
        "[GameplayDialogController] Updated dialog UI sprite key='" + debugKey +
        "' form='" + formName + "'"
      );
    }
  }

  bool TryApplyPortraitSprite(
    SpriteWithNormals portrait,
    GameplayDialogNode node,
    string formName,
    string emotion,
    string speakerSide,
    string source
  ) {
    if (portrait == null || node == null) {
      return false;
    }

    if (!TryResolvePortraitPresentation(node, formName, out var libraryName, out var labelPrefix)) {
      if (ShouldLogDialogDebug()) {
        RuntimeLog.Log(
          "[GameplayDialogController] No portrait library resolved side='" + (speakerSide ?? "") +
          "' source='" + (source ?? "") +
          "' speaker_id='" + (node.speakerId ?? "") +
          "' speaker_name='" + ResolveSpeakerName(node) + "'"
        );
      }
      return false;
    }

    var changed = false;
    if (!string.Equals(portrait.libraryName, libraryName, StringComparison.OrdinalIgnoreCase)) {
      portrait.SetLibraryName(libraryName);
      changed = true;
    }
    if (!string.Equals(portrait.labelPrefix, labelPrefix, StringComparison.Ordinal)) {
      portrait.SetLabelPrefix(labelPrefix);
      changed = true;
    }
    if (!string.Equals(portrait.category, emotion, StringComparison.Ordinal)) {
      portrait.SetAnimation(emotion);
      changed = true;
    }
    portrait.SetIsAnimation(false);
    if (portrait.DoNotRender) {
      portrait.SetDoNotRender(false);
      changed = true;
    }
    if (!changed) {
      return true;
    }

    portrait.ForceUpdateSpriteAndNormal(0);
    if (ShouldLogDialogDebug()) {
      RuntimeLog.Log(
          "[GameplayDialogController] Updated portrait side='" + (speakerSide ?? "") +
          "' source='" + (source ?? "") +
          "' library='" + libraryName +
          "' label='" + labelPrefix +
          "' emotion='" + emotion + "'"
      );
    }
    return true;
  }

  void TickTypewriter() {
    if (!dialogueActive || !IsTyping()) {
      return;
    }

    if (string.IsNullOrEmpty(currentFullText)) {
      CompleteTypewriter("empty_text");
      return;
    }

    typewriterCharacterProgress += Time.unscaledDeltaTime * Mathf.Max(charactersPerSecond, 1f);
    var nextVisibleCount = Mathf.Clamp(Mathf.FloorToInt(typewriterCharacterProgress), 0, currentFullText.Length);
    if (nextVisibleCount <= visibleCharacterCount) {
      return;
    }

    visibleCharacterCount = nextVisibleCount;
    ApplyVisibleText();
    if (visibleCharacterCount >= currentFullText.Length) {
      if (ShouldLogDialogDebug()) {
        RuntimeLog.Log(
          "[GameplayDialogController] Typewriter complete index=" + currentNodeIndex +
          " text_length=" + currentFullText.Length
        );
      }
    }
  }

  void CompleteTypewriter(string source) {
    if (!dialogueActive) {
      return;
    }

    visibleCharacterCount = currentFullText != null ? currentFullText.Length : 0;
    typewriterCharacterProgress = visibleCharacterCount;
    ApplyVisibleText();
    if (ShouldLogDialogDebug()) {
      RuntimeLog.Log(
        "[GameplayDialogController] Typewriter skipped source='" + (source ?? "") +
        "' index=" + currentNodeIndex +
        " text_length=" + visibleCharacterCount
      );
    }
  }

  void ApplyVisibleText() {
    if (dialogText == null) {
      return;
    }

    if (string.IsNullOrEmpty(currentFullText)) {
      ApplyText(dialogText, "");
      dialogText.SetVisibleCharacterCount(-1);
      return;
    }

    var clampedCount = Mathf.Clamp(visibleCharacterCount, 0, currentFullText.Length);
    if (!string.Equals(dialogText.content, currentFullText, StringComparison.Ordinal)) {
      dialogText.content = currentFullText;
      dialogText.Generate();
    }
    dialogText.SetVisibleCharacterCount(clampedCount);
  }

  bool IsTyping() {
    return !string.IsNullOrEmpty(currentFullText) && visibleCharacterCount < currentFullText.Length;
  }

  void ClearSpeakerState() {
    SetSpeakerActive(playerWidgets, false, null, EsperanzaForms.GetActive(), "Normal", "", "clear");
    SetSpeakerActive(otherWidgets, false, null, EsperanzaForms.GetActive(), "Normal", "", "clear");
  }

  void SetDialogVisible(bool visible, string source) {
    if (dialogRoot == null) {
      return;
    }

    if (dialogRoot.activeSelf == visible) {
      return;
    }

    dialogRoot.SetActive(visible);
    if (ShouldLogDialogDebug()) {
      RuntimeLog.Log(
        "[GameplayDialogController] Dialog root visibility visible=" + (visible ? 1 : 0) +
        " source='" + (source ?? "") + "'"
      );
    }
  }

  string ResolveNodeForm(GameplayDialogNode node) {
    var requestedForm = node != null ? EsperanzaForms.ResolveFormKey(node.avatarForm) : null;
    return !string.IsNullOrWhiteSpace(requestedForm) ? requestedForm : EsperanzaForms.GetActive();
  }

  string ResolveEmotion(string emotion) {
    if (string.IsNullOrWhiteSpace(emotion)) {
      return "Normal";
    }

    foreach (var knownEmotion in ValidEmotions) {
      if (string.Equals(knownEmotion, emotion.Trim(), StringComparison.OrdinalIgnoreCase)) {
        return knownEmotion;
      }
    }

    Debug.LogWarning(
      "[GameplayDialogController] Unknown dialog emotion='" + emotion +
      "'. Falling back to 'Normal'."
    );
    return "Normal";
  }

  string ResolveSpeakerName(GameplayDialogNode node) {
    if (node == null) {
      return "";
    }

    if (!string.IsNullOrWhiteSpace(node.speakerName)) {
      return node.speakerName.Trim();
    }

    return node.speaker == DialogSpeakerSide.Enemy ? otherWidgets.defaultName : playerWidgets.defaultName;
  }

  void ApplyText(FontText fontText, string value) {
    if (fontText == null) {
      return;
    }

    if (fontText.content == value) {
      return;
    }

    fontText.content = value ?? "";
    fontText.Generate();
  }

  void CopyFontTemplate(FontText source, FontText target) {
    if (source == null || target == null) {
      return;
    }

    target.characterPrefab = source.characterPrefab;
    target.font = source.font;
    target.spaceWidth = source.spaceWidth;
    target.padding = source.padding;
    target.mono = source.mono;
    target.maxWidth = source.maxWidth;
    target.marginX = source.marginX;
    target.marginY = source.marginY;
    target.offsetX = source.offsetX;
    target.offsetY = source.offsetY;
    target.justifyX = source.justifyX;
    target.justifyY = source.justifyY;
    target.lineDirection = source.lineDirection;
  }

  Color ResolveSpeakerNameColor(SpeakerWidgets widgets, GameplayDialogNode node, string formName) {
    if (widgets == null) {
      return Color.white;
    }

    if (string.Equals(widgets.sideKey, playerWidgets.sideKey, StringComparison.OrdinalIgnoreCase)) {
      return ResolvePlayerNameColor(formName);
    }

    return ResolveOtherNameColor(node);
  }

  Color ResolvePlayerNameColor(string formName) {
    return ShaderColors.TryGetFormColor(formName, PlayerNameColorGroup, out var color, out _)
      ? color
      : Color.white;
  }

  Color ResolveOtherNameColor(GameplayDialogNode node) {
    var requestedColorName = ResolveDialogOtherType(node) == DialogOtherType.Ally ? AllyColorName : EnemyColorName;
    return ShaderColors.TryGetNamedColor(requestedColorName, out var color) ? color : Color.white;
  }

  DialogOtherType ResolveDialogOtherType(GameplayDialogNode node) {
    if (node == null || node.speaker != DialogSpeakerSide.Enemy) {
      return DialogOtherType.Auto;
    }

    if (node.otherType != DialogOtherType.Auto) {
      return node.otherType;
    }

    if (TryResolveKnownPortraitSpeakerConfig(node.speakerId, out var config) ||
        TryResolveKnownPortraitSpeakerConfig(node.speakerName, out config)) {
      return config.otherType;
    }

    return DialogOtherType.Enemy;
  }

  bool TryResolvePortraitPresentation(
    GameplayDialogNode node,
    string formName,
    out string libraryName,
    out string labelPrefix
  ) {
    libraryName = "";
    labelPrefix = DefaultPortraitLabelPrefix;
    if (node == null) {
      return false;
    }

    if (!string.IsNullOrWhiteSpace(node.portraitLibraryName)) {
      libraryName = node.portraitLibraryName.Trim();
      labelPrefix = ResolvePortraitLabelPrefix(libraryName, formName);
      return true;
    }

    if (node.speaker == DialogSpeakerSide.Esperanza) {
      libraryName = DialogEsperPortraitLibrary;
      labelPrefix = ResolvePortraitLabelPrefix(libraryName, formName);
      return true;
    }

    if (!string.IsNullOrWhiteSpace(node.speakerId)) {
      libraryName = "Dialog/Dialog" + node.speakerId.Trim();
      labelPrefix = ResolvePortraitLabelPrefix(libraryName, formName);
      return true;
    }

    if (TryResolveKnownPortraitSpeakerConfig(node.speakerId, out var config) ||
        TryResolveKnownPortraitSpeakerConfig(node.speakerName, out config)) {
      libraryName = config.libraryName;
      labelPrefix = ResolvePortraitLabelPrefix(libraryName, formName);
      return true;
    }

    return false;
  }

  bool TryResolveKnownPortraitSpeakerConfig(string speakerToken, out PortraitSpeakerConfig config) {
    config = default;
    if (string.IsNullOrWhiteSpace(speakerToken)) {
      return false;
    }

    return KnownPortraitSpeakerConfigs.TryGetValue(speakerToken.Trim(), out config);
  }

  string ResolvePortraitLabelPrefix(string libraryName, string formName) {
    return string.Equals(libraryName, DialogEsperPortraitLibrary, StringComparison.OrdinalIgnoreCase)
      ? formName
      : DefaultPortraitLabelPrefix;
  }

  void ApplySpeakerNameColor(SpeakerWidgets widgets, GameplayDialogNode node, string formName) {
    if (widgets == null) {
      return;
    }

    var color = ResolveSpeakerNameColor(widgets, node, formName);
    if (TryApplyAnimatorColor(widgets.nameTextObject, color)) {
      return;
    }

    ApplyFontTextColorFallback(widgets.nameText, color);
  }

  bool TryApplyAnimatorColor(GameObject target, Color color) {
    if (target == null) {
      return false;
    }

    var animator = target.GetComponent<AllIn1AnimatorInspector>();
    if (animator == null) {
      return false;
    }

    animator.AddColorSequence("_Color", color, color, 1f, replaceExisting: true);
    return true;
  }

  void ApplyFontTextColorFallback(FontText fontText, Color color) {
    if (fontText == null) {
      return;
    }

    var hostRenderer = fontText.GetComponent<SpriteRenderer>();
    if (hostRenderer != null && hostRenderer.color != color) {
      hostRenderer.color = color;
    }

    var componentPropagator = fontText.GetComponent<ComponentPropagator>();
    if (componentPropagator != null) {
      componentPropagator.ForcePropagation();
      return;
    }

    fontTextRendererBuffer.Clear();
    fontText.GetComponentsInChildren(true, fontTextRendererBuffer);
    for (var i = 0; i < fontTextRendererBuffer.Count; i++) {
      var childRenderer = fontTextRendererBuffer[i];
      if (childRenderer == null || childRenderer == hostRenderer) {
        continue;
      }
      if (childRenderer.color != color) {
        childRenderer.color = color;
      }
    }
    fontTextRendererBuffer.Clear();
  }

  void LogSpeakerWidgetState(SpeakerWidgets widgets, bool expectedActive, string source) {
    if (!ShouldLogDialogDebug() || widgets == null) {
      return;
    }

    RuntimeLog.Log(
      "[GameplayDialogController] Speaker widget state side='" + widgets.sideKey +
      "' expected_active=" + (expectedActive ? 1 : 0) +
      " name_bg=" + GetActiveSelf(widgets.nameBackgroundObject) +
      " name_text=" + GetActiveSelf(widgets.nameTextObject) +
      " avatar_bg=" + GetActiveSelf(widgets.avatarBackgroundObject) +
      " avatar=" + GetActiveSelf(widgets.portrait != null ? widgets.portrait.gameObject : widgets.portraitObject) +
      " source='" + (source ?? "") + "'"
    );
  }

  void RegisterLocationTriggerHandlers(string locationId, string source) {
    ClearLocationTriggerHandlers();

    var resolvedLocationId = ResolveLocationId(locationId);
    activeLocationDialogId = resolvedLocationId;
    if (string.IsNullOrWhiteSpace(resolvedLocationId)) {
      return;
    }

    DialogController.CollectPendingTriggers(resolvedLocationId, resolvedLocationTriggers);
    for (var i = 0; i < resolvedLocationTriggers.Count; i++) {
      var trigger = ResolveDialogTrigger(resolvedLocationTriggers[i]);
      if (string.IsNullOrWhiteSpace(trigger)) {
        continue;
      }

      var subscribedTrigger = trigger;
      locationTriggerActions.Add(MessageBus.On(subscribedTrigger, payload => OnDialogTriggerReceived(subscribedTrigger, payload)));
    }

    if (!ShouldLogDialogDebug()) {
      return;
    }

    RuntimeLog.Log(
      "[GameplayDialogController] Registered location dialog triggers" +
      " location='" + resolvedLocationId +
      "' source='" + (source ?? "") +
      "' trigger_count=" + resolvedLocationTriggers.Count +
      " triggers='" + string.Join(", ", resolvedLocationTriggers) + "'"
    );
  }

  void ClearLocationTriggerHandlers() {
    for (var i = 0; i < locationTriggerActions.Count; i++) {
      locationTriggerActions[i]?.Invoke();
    }
    locationTriggerActions.Clear();
    resolvedLocationTriggers.Clear();
  }

  void OnDialogTriggerReceived(string trigger, object payload) {
    if (string.IsNullOrWhiteSpace(activeLocationDialogId)) {
      return;
    }

    QueueTriggeredDialog(activeLocationDialogId, trigger, "message_bus:" + ResolveDialogTrigger(trigger));
  }

  void QueueTriggeredDialog(string locationId, string trigger, string source) {
    var resolvedLocationId = ResolveLocationId(locationId);
    var resolvedTrigger = ResolveDialogTrigger(trigger);
    if (string.IsNullOrWhiteSpace(resolvedLocationId) ||
        string.Equals(resolvedTrigger, AutoDialogTrigger, StringComparison.OrdinalIgnoreCase)) {
      return;
    }

    for (var i = 0; i < pendingDialogRequests.Count; i++) {
      var existing = pendingDialogRequests[i];
      if (existing == null) {
        continue;
      }
      if (!string.Equals(existing.locationId, resolvedLocationId, StringComparison.OrdinalIgnoreCase) ||
          !string.Equals(existing.trigger, resolvedTrigger, StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      if (ShouldLogDialogDebug()) {
        RuntimeLog.Log(
          "[GameplayDialogController] Ignored duplicate pending dialog trigger" +
          " location='" + resolvedLocationId +
          "' trigger='" + resolvedTrigger + "'"
        );
      }
      return;
    }

    pendingDialogRequests.Add(new PendingDialogRequest {
      locationId = resolvedLocationId,
      trigger = resolvedTrigger,
      source = string.IsNullOrWhiteSpace(source) ? "message_bus:" + resolvedTrigger : source
    });

    if (ShouldLogDialogDebug()) {
      RuntimeLog.Log(
        "[GameplayDialogController] Queued triggered dialog" +
        " location='" + resolvedLocationId +
        "' trigger='" + resolvedTrigger +
        "' source='" + (source ?? "") +
        "' pending_count=" + pendingDialogRequests.Count +
        " overlay_active=" + (SpriteStreamingLoadingState.IsLoadingOverlayActive ? 1 : 0)
      );
    }

    TryPlayPendingDialogRequest("trigger_queue");
  }

  void ClearPendingDialogRequests() {
    pendingDialogRequests.Clear();
  }

  void ClearPendingDialogRequestsForLocation(string activeLocationId, string previousLocationId, string source) {
    var resolvedActiveLocationId = ResolveLocationId(activeLocationId);
    var removedCount = 0;
    for (var i = pendingDialogRequests.Count - 1; i >= 0; i--) {
      var request = pendingDialogRequests[i];
      if (request == null ||
          !string.Equals(ResolveLocationId(request.locationId), resolvedActiveLocationId, StringComparison.OrdinalIgnoreCase)) {
        pendingDialogRequests.RemoveAt(i);
        removedCount += 1;
      }
    }

    if (removedCount <= 0 || !ShouldLogDialogDebug()) {
      return;
    }

    RuntimeLog.Log(
      "[GameplayDialogController] Cleared stale pending dialog requests" +
      " source='" + (source ?? "") +
      "' previous_location='" + ResolveLocationId(previousLocationId) +
      "' active_location='" + resolvedActiveLocationId +
      "' removed_count=" + removedCount
    );
  }

  bool HasPendingDialogRequestForActiveLocation() {
    if (pendingDialogRequests.Count <= 0 || string.IsNullOrWhiteSpace(activeLocationDialogId)) {
      return false;
    }

    for (var i = 0; i < pendingDialogRequests.Count; i++) {
      var request = pendingDialogRequests[i];
      if (request == null) {
        continue;
      }

      if (string.Equals(request.locationId, activeLocationDialogId, StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
  }

  bool HasPendingAutoLocationDialog() {
    return !dialogueActive &&
           dialogStateReady &&
           !string.IsNullOrWhiteSpace(activeLocationDialogId) &&
           DialogController.HasPendingTriggeredSequence(activeLocationDialogId, AutoDialogTrigger);
  }

  void TryPlayPendingDialogRequest(string source) {
    if (!isActiveAndEnabled || dialogueActive || !dialogStateReady || SpriteStreamingLoadingState.IsLoadingOverlayActive) {
      return;
    }

    if (TryPlayAutoLocationDialog(source)) {
      return;
    }

    for (var i = 0; i < pendingDialogRequests.Count; i++) {
      var request = pendingDialogRequests[i];
      if (request == null) {
        pendingDialogRequests.RemoveAt(i);
        i--;
        continue;
      }

      if (!string.Equals(request.locationId, activeLocationDialogId, StringComparison.OrdinalIgnoreCase)) {
        pendingDialogRequests.RemoveAt(i);
        i--;
        continue;
      }

      if (TryBuildTriggeredDialogSequence(request.locationId, request.trigger)) {
        pendingDialogRequests.RemoveAt(i);
        PlayResolvedLocationDialog(request.locationId, request.trigger, request.source);
        return;
      }

      if (ShouldLogDialogDebug()) {
        RuntimeLog.Log(
          "[GameplayDialogController] Dropped pending dialog trigger because no matching unseen chunk was available" +
          " location='" + request.locationId +
          "' trigger='" + request.trigger +
          "' source='" + request.source + "'"
        );
      }
      pendingDialogRequests.RemoveAt(i);
      i--;
    }
  }

  bool TryPlayAutoLocationDialog(string source) {
    if (ShouldSkipAutoDialogRetry(activeLocationDialogId)) {
      return false;
    }

    if (!TryBuildTriggeredDialogSequence(activeLocationDialogId, AutoDialogTrigger)) {
      SuppressAutoDialogRetry(activeLocationDialogId);
      return false;
    }

    PlayResolvedLocationDialog(activeLocationDialogId, AutoDialogTrigger, source);
    return true;
  }

  void ResetAutoDialogRetryState() {
    suppressedAutoDialogLocationId = "";
  }

  bool ShouldSkipAutoDialogRetry(string locationId) {
    return !string.IsNullOrWhiteSpace(locationId) &&
           string.Equals(suppressedAutoDialogLocationId, locationId, StringComparison.OrdinalIgnoreCase);
  }

  void SuppressAutoDialogRetry(string locationId) {
    if (string.IsNullOrWhiteSpace(locationId) ||
        string.Equals(suppressedAutoDialogLocationId, locationId, StringComparison.OrdinalIgnoreCase)) {
      return;
    }

    suppressedAutoDialogLocationId = locationId;
  }

  bool TryBuildTriggeredDialogSequence(string locationId, string trigger) {
    return !string.IsNullOrWhiteSpace(locationId) &&
           DialogController.TryBuildTriggeredSequence(locationId, trigger, resolvedLocationSequence) &&
           resolvedLocationSequence.Count > 0;
  }

  void PlayResolvedLocationDialog(string locationId, string trigger, string source) {
    ReplaceSequenceNodes(resolvedLocationSequence);

    if (ShouldLogDialogDebug()) {
      RuntimeLog.Log(
        "[GameplayDialogController] Starting location dialog chunk" +
        " location='" + locationId +
        "' trigger='" + ResolveDialogTrigger(trigger) +
        "' source='" + (source ?? "") +
        "' line_count=" + sequenceNodes.Count
      );
    }

    PlayAuthoredSequence(BuildDialogSource(source, trigger));
  }

  void ReplaceSequenceNodes(IList<GameplayDialogNode> nodes) {
    sequenceNodes.Clear();
    if (nodes == null) {
      return;
    }

    if (sequenceNodes.Capacity < nodes.Count) {
      sequenceNodes.Capacity = nodes.Count;
    }
    for (var i = 0; i < nodes.Count; i++) {
      var node = nodes[i];
      if (node == null) {
        continue;
      }
      sequenceNodes.Add(node);
    }
  }

  void ReplaceSequenceNodes(GameplayDialogNode[] nodes) {
    sequenceNodes.Clear();
    if (nodes == null) {
      return;
    }

    if (sequenceNodes.Capacity < nodes.Length) {
      sequenceNodes.Capacity = nodes.Length;
    }
    for (var i = 0; i < nodes.Length; i++) {
      var node = nodes[i];
      if (node == null) {
        continue;
      }
      sequenceNodes.Add(node);
    }
  }

  static FontText FindFontText(Transform root, string targetName) {
    var target = FindChildRecursive(root, targetName);
    return target != null ? target.GetComponentInChildren<FontText>(includeInactive: true) : null;
  }

  static SpriteWithNormals GetSpriteWithNormals(GameObject target) {
    return target != null ? target.GetComponent<SpriteWithNormals>() : null;
  }

  static Transform FindFirstChildRecursive(Transform root, string[] targetNames) {
    if (root == null || targetNames == null) {
      return null;
    }

    for (var i = 0; i < targetNames.Length; i++) {
      var target = FindChildRecursive(root, targetNames[i]);
      if (target != null) {
        return target;
      }
    }

    return null;
  }

  static Transform FindChildRecursive(Transform root, string targetName) {
    if (root == null || string.IsNullOrWhiteSpace(targetName)) {
      return null;
    }

    if (string.Equals(root.name, targetName, StringComparison.OrdinalIgnoreCase)) {
      return root;
    }

    for (var i = 0; i < root.childCount; i++) {
      var result = FindChildRecursive(root.GetChild(i), targetName);
      if (result != null) {
        return result;
      }
    }

    return null;
  }

  static void SetGameObjectActive(GameObject target, bool active) {
    if (target == null || target.activeSelf == active) {
      return;
    }

    target.SetActive(active);
  }

  static int GetActiveSelf(GameObject target) {
    return target != null && target.activeSelf ? 1 : 0;
  }

  static string ResolveDialogTrigger(string value) {
    if (string.IsNullOrWhiteSpace(value) ||
        string.Equals(value.Trim(), AutoDialogTrigger, StringComparison.OrdinalIgnoreCase)) {
      return AutoDialogTrigger;
    }

    return value.Trim();
  }

  static string BuildDialogSource(string source, string trigger) {
    var resolvedSource = string.IsNullOrWhiteSpace(source) ? "location_dialog" : source.Trim();
    var resolvedTrigger = ResolveDialogTrigger(trigger);
    return string.Equals(resolvedTrigger, AutoDialogTrigger, StringComparison.OrdinalIgnoreCase)
      ? resolvedSource + ":auto"
      : resolvedSource + ":trigger:" + resolvedTrigger;
  }

  static string ResolveLocationId(object payload) {
    if (payload is LocationInfo locationInfo) {
      return string.IsNullOrWhiteSpace(locationInfo.id) ? "" : locationInfo.id.Trim();
    }

    if (payload is string locationId) {
      return string.IsNullOrWhiteSpace(locationId) ? "" : locationId.Trim();
    }

    return string.IsNullOrWhiteSpace(LocationManager.currentLocation)
      ? ""
      : LocationManager.currentLocation.Trim();
  }

  static bool IsPressed(object payload) {
    if (payload == null) {
      return true;
    }
#if ENABLE_INPUT_SYSTEM
    if (payload is InputAction.CallbackContext context) {
      if (context.valueType == typeof(Vector2)) return context.ReadValue<Vector2>().sqrMagnitude > 0.25f;
      if (context.valueType == typeof(Vector3)) return context.ReadValue<Vector3>().sqrMagnitude > 0.25f;
      if (context.valueType == typeof(float)) return context.ReadValue<float>() > 0.5f;
      if (context.valueType == typeof(int)) return context.ReadValue<int>() != 0;
      return IsPressed(context.ReadValueAsObject());
    }
#endif
    if (payload is bool booleanValue) {
      return booleanValue;
    }
    if (payload is float floatValue) {
      return floatValue > 0.5f;
    }
    if (payload is double doubleValue) {
      return doubleValue > 0.5d;
    }
    if (payload is int intValue) {
      return intValue != 0;
    }
    return true;
  }
}
