using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public class GameplayDialogController : MonoBehaviour {
  const string DialogEsperPortraitLibrary = "Dialog/DialogEsper";
  const string DefaultPortraitLabelPrefix = "Base";
  const string PlayerNameColorGroup = "secondary";
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
  readonly List<SpriteRenderer> fontTextRendererBuffer = new();

  GameObject dialogRoot;
  GameObject dialogBoxObject;
  FontText dialogText;
  SpriteWithNormals dialogBoxBackground;
  bool dialogueActive;
  bool dialogStateReady;
  int currentNodeIndex = -1;
  string currentFullText = "";
  string pendingLocationId = "";
  string pendingLocationSource = "";
  int visibleCharacterCount;
  float typewriterCharacterProgress;
  readonly List<GameplayDialogNode> pendingLocationSequence = new();

  public bool IsDialogActive => dialogueActive;

  static bool ShouldLogDialogDebug() {
    return Application.isEditor || Debug.isDebugBuild;
  }

  void Awake() {
    ApplyDebugSeenOverride("awake");
  }

  void OnEnable() {
    ApplyDebugSeenOverride("enable");
    EnsureResolved();
    RegisterHandlers();
    dialogStateReady = DialogController.IsStateReadyForCurrentSlot;
    pendingLocationId = ResolveLocationId(LocationManager.currentLocation);
    SetDialogVisible(false, "enable");
    if (dialogStateReady) {
      QueueLocationDialogIfNeeded(pendingLocationId, "enable");
    }
  }

  void Start() {
    SetDialogVisible(false, "start");
  }

  void Update() {
    TickTypewriter();
    TryPlayPendingLocationSequence("update");
  }

  void OnDisable() {
    UnregisterHandlers();
    pendingLocationSequence.Clear();
    pendingLocationSource = "";
    pendingLocationId = "";
    dialogStateReady = false;
    if (debugTreatAllDialogAsUnseen) {
      DialogController.SetDebugTreatAllDialogAsUnseen(false, "disable");
    }
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

    ApplyDebugSeenOverride("validate");
  }

  void ApplyDebugSeenOverride(string source) {
    DialogController.SetDebugTreatAllDialogAsUnseen(debugTreatAllDialogAsUnseen, source);
    if (!ShouldLogDialogDebug()) {
      return;
    }

    Debug.Log(
      "[GameplayDialogController] Debug seen override enabled=" + (debugTreatAllDialogAsUnseen ? 1 : 0) +
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
      Debug.Log(
        "[GameplayDialogController] Assigned runtime sequence source='" + (source ?? "") +
        "' node_count=" + sequenceNodes.Count
      );
    }
    PlayAuthoredSequence(source);
  }

  public void StopDialogue(string reason = "runtime") {
    var wasActive = dialogueActive;
    dialogueActive = false;
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
      Debug.Log("[GameplayDialogController] Dialog finished reason='" + (reason ?? "") + "'");
    }
    MessageBus.Send("dialog.finished", reason);
    TryPlayPendingLocationSequence("dialog_finished");
  }

  void RegisterHandlers() {
    if (actions.Count > 0) {
      return;
    }

    actions.Add(MessageBus.On("dialog.progress", o => OnProgress(o)));
    actions.Add(MessageBus.On("dialog.playAuthored", o => PlayAuthoredSequence("message_bus")));
    actions.Add(MessageBus.On("dialog.play", o => PlayRequestedSequence(o)));
    actions.Add(MessageBus.On("dialog.stop", o => StopDialogue("message_bus")));
    actions.Add(MessageBus.On("formChanged", o => OnFormChanged(o)));
    actions.Add(MessageBus.On("LocationLoaded", o => OnLocationLoaded(o)));
    actions.Add(MessageBus.On("dialogStateReady", o => OnDialogStateReady(o)));
  }

  void UnregisterHandlers() {
    for (var i = 0; i < actions.Count; i++) {
      actions[i]?.Invoke();
    }
    actions.Clear();
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
      Debug.Log(
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
      Debug.Log(
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
        Debug.Log("[GameplayDialogController] Restarting active dialog source='" + (source ?? "") + "'");
      }
    }

    dialogueActive = true;
    currentNodeIndex = -1;
    currentFullText = "";
    visibleCharacterCount = 0;
    typewriterCharacterProgress = 0f;
    SetDialogVisible(true, source);

    if (ShouldLogDialogDebug()) {
      Debug.Log(
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
    pendingLocationId = ResolveLocationId(payload);
    if (string.IsNullOrWhiteSpace(pendingLocationId)) {
      return;
    }

    if (!dialogStateReady) {
      if (ShouldLogDialogDebug()) {
        Debug.Log(
          "[GameplayDialogController] Deferred location dialog until dialog state is ready" +
          " location='" + pendingLocationId + "'"
        );
      }
      return;
    }

    QueueLocationDialogIfNeeded(pendingLocationId, "location_loaded");
  }

  void OnDialogStateReady(object payload) {
    dialogStateReady = true;
    if (string.IsNullOrWhiteSpace(pendingLocationId)) {
      pendingLocationId = ResolveLocationId(LocationManager.currentLocation);
    }

    if (ShouldLogDialogDebug()) {
      Debug.Log(
        "[GameplayDialogController] Dialog state ready source='" + (payload != null ? payload.ToString() : "") +
        "' location='" + pendingLocationId + "'"
      );
    }
    QueueLocationDialogIfNeeded(pendingLocationId, "dialog_state_ready");
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
      Debug.Log(
        "[GameplayDialogController] Showing node index=" + currentNodeIndex +
        " source='" + (source ?? "") + "'" +
        " location='" + (node.locationId ?? "") + "'" +
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
      Debug.Log(
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
        Debug.Log(
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
      Debug.Log(
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
        Debug.Log(
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
      Debug.Log(
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
      Debug.Log(
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
    return TryGetFormColor(formName, PlayerNameColorGroup, out var color, out _) ? color : Color.white;
  }

  Color ResolveOtherNameColor(GameplayDialogNode node) {
    var requestedColorName = ResolveDialogOtherType(node) == DialogOtherType.Ally ? AllyColorName : EnemyColorName;
    return TryGetNamedColor(requestedColorName, out var color) ? color : Color.white;
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

  bool TryGetFormColor(string formName, string groupName, out Color color, out string colorName) {
    color = Color.white;
    colorName = null;
    if (string.IsNullOrWhiteSpace(formName) || string.IsNullOrWhiteSpace(groupName)) {
      return false;
    }

    if (!ShaderColors.pairs.TryGetValue(formName, out var formGroups) || formGroups == null) {
      return false;
    }
    if (!formGroups.TryGetValue(groupName, out var groupValues) || groupValues == null) {
      return false;
    }
    if (!groupValues.TryGetValue("color", out colorName) || string.IsNullOrWhiteSpace(colorName)) {
      return false;
    }

    return TryGetNamedColor(colorName, out color);
  }

  bool TryGetNamedColor(string colorName, out Color color) {
    color = Color.white;
    if (string.IsNullOrWhiteSpace(colorName)) {
      return false;
    }

    return ShaderColors.myColors.TryGetValue(colorName.Trim(), out color);
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

    Debug.Log(
      "[GameplayDialogController] Speaker widget state side='" + widgets.sideKey +
      "' expected_active=" + (expectedActive ? 1 : 0) +
      " name_bg=" + GetActiveSelf(widgets.nameBackgroundObject) +
      " name_text=" + GetActiveSelf(widgets.nameTextObject) +
      " avatar_bg=" + GetActiveSelf(widgets.avatarBackgroundObject) +
      " avatar=" + GetActiveSelf(widgets.portrait != null ? widgets.portrait.gameObject : widgets.portraitObject) +
      " source='" + (source ?? "") + "'"
    );
  }

  void QueueLocationDialogIfNeeded(string locationId, string source) {
    if (!dialogStateReady) {
      return;
    }

    var resolvedLocationId = ResolveLocationId(locationId);
    if (string.IsNullOrWhiteSpace(resolvedLocationId)) {
      return;
    }

    pendingLocationId = resolvedLocationId;
    if (!DialogController.TryBuildUnseenSequence(resolvedLocationId, resolvedLocationSequence) ||
        resolvedLocationSequence.Count <= 0) {
      pendingLocationSequence.Clear();
      pendingLocationSource = "";
      return;
    }

    if (pendingLocationSequence.Capacity < resolvedLocationSequence.Count) {
      pendingLocationSequence.Capacity = resolvedLocationSequence.Count;
    }
    pendingLocationSequence.Clear();
    pendingLocationSequence.AddRange(resolvedLocationSequence);
    pendingLocationSource = string.IsNullOrWhiteSpace(source) ? "location_dialog" : source;

    if (ShouldLogDialogDebug()) {
      Debug.Log(
        "[GameplayDialogController] Queued location dialog" +
        " location='" + resolvedLocationId +
        "' source='" + pendingLocationSource +
        "' line_count=" + pendingLocationSequence.Count +
        " overlay_active=" + (SpriteStreamingLoadingState.IsLoadingOverlayActive ? 1 : 0)
      );
    }
    TryPlayPendingLocationSequence(source);
  }

  void TryPlayPendingLocationSequence(string source) {
    if (!isActiveAndEnabled || dialogueActive) {
      return;
    }

    if (!dialogStateReady || pendingLocationSequence.Count <= 0) {
      return;
    }

    if (SpriteStreamingLoadingState.IsLoadingOverlayActive) {
      return;
    }

    var queuedSource = string.IsNullOrWhiteSpace(pendingLocationSource) ? source : pendingLocationSource;
    var locationId = pendingLocationId;
    ReplaceSequenceNodes(pendingLocationSequence);
    pendingLocationSequence.Clear();
    pendingLocationSource = "";

    if (ShouldLogDialogDebug()) {
      Debug.Log(
        "[GameplayDialogController] Starting queued location dialog" +
        " location='" + locationId +
        "' source='" + queuedSource +
        "' line_count=" + sequenceNodes.Count
      );
    }
    PlayAuthoredSequence(queuedSource);
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
