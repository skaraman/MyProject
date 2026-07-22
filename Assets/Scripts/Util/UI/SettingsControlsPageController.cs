using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class SettingsControlsPageController : MonoBehaviour {
  const string GameplayMapName = "gameplay";
  const string SettingsMapName = "settingsMenu";
  const string SelectionOutlineKeyword = "OUTBASE_ON";
  const int RowsPerPage = 6;
  const float RebindTimeoutSeconds = 10f;

  const float HeaderY = 2.68f;
  const float FirstRowY = 1.58f;
  const float RowSpacing = 0.88f;
  const float FooterY = -4.15f;
  const float PageLabelY = -4.78f;
  const float PromptY = -5.18f;

  enum BindingDevice {
    Keyboard,
    Gamepad
  }

  enum TargetKind {
    Binding,
    PreviousPage,
    Defaults,
    NextPage
  }

  sealed class BindingCell {
    public GameObject root;
    public SpriteRenderer background;
    public FontText valueText;
    public InputAction action;
    public int bindingIndex = -1;
    public BindingDevice device;
    public SelectableTarget selectable;
  }

  sealed class RowView {
    public GameObject root;
    public FontText actionText;
    public BindingCell keyboard;
    public BindingCell gamepad;
  }

  sealed class SelectableTarget {
    public TargetKind kind;
    public GameObject root;
    public SpriteRenderer background;
    public BindingCell binding;
    public int navigationRow;
    public int navigationColumn;
    public bool outlineEnabled;
  }

  [Header("Base Forms Atlas")]
  [SerializeField] Sprite bindingSprite;
  [SerializeField] Sprite dividerSprite;
  [SerializeField] Sprite previousPageSprite;
  [SerializeField] Sprite nextPageSprite;

  readonly List<InputAction> actions = new();
  readonly List<SelectableTarget> visibleTargets = new();
  readonly Dictionary<GameObject, SelectableTarget> targetsByRoot = new();
  readonly RowView[] rows = new RowView[RowsPerPage];

  Transform runtimeRoot;
  FontText fontTemplate;
  SpriteRenderer spriteTemplate;
  FontText pageText;
  FontText promptText;
  GameObject previousPageRoot;
  SpriteRenderer previousPageBackground;
  GameObject defaultsRoot;
  SpriteRenderer defaultsBackground;
  GameObject nextPageRoot;
  SpriteRenderer nextPageBackground;

  InputProcessor inputProcessor;
  InputActionMap gameplayMap;
  int pageIndex;
  int visibleRowCount;
  SelectableTarget selectedTarget;
  SelectableTarget hoveredTarget;
  BindingCell awaitingCell;

  InputActionRebindingExtensions.RebindingOperation rebindOperation;
  InputActionMap disabledSettingsMap;
  InputActionMap disabledTargetMap;
  bool settingsMapWasEnabled;
  bool targetMapWasEnabled;

  static readonly Color WaitingColor = new(0.58f, 1f, 0.68f, 1f);

  public bool IsRebinding => rebindOperation != null;

  void OnEnable() {
    Open();
  }

  void OnDisable() {
    CancelActiveRebind();
    hoveredTarget = null;
  }

  void OnDestroy() {
    CancelActiveRebind();
  }

  public void Open() {
    EnsureLayout();
    ResolveActions();
    RefreshPage();
  }

  public bool TryHandleClick(GameObject target) {
    if (rebindOperation != null) {
      CancelActiveRebind();
      return true;
    }

    var selectable = ResolveTarget(target);
    if (selectable == null) return false;

    selectedTarget = selectable;
    Activate(selectable);
    return true;
  }

  public bool TryHandleSelect(GameObject target) {
    if (rebindOperation != null) {
      CancelActiveRebind();
      return true;
    }

    var selectable = ResolveTarget(target) ?? selectedTarget;
    if (selectable == null) return false;

    selectedTarget = selectable;
    Activate(selectable);
    return true;
  }

  public bool TryNavigate(Vector2Int direction) {
    if (rebindOperation != null) return true;

    if (visibleTargets.Count == 0) return false;
    if (selectedTarget == null || !visibleTargets.Contains(selectedTarget)) {
      selectedTarget = visibleTargets[0];
      RefreshTargetColors();
      return true;
    }

    var next = selectedTarget;
    if (direction.y != 0) {
      next = FindVerticalTarget(selectedTarget, direction.y > 0 ? -1 : 1);
    }
    else if (direction.x != 0) {
      next = FindHorizontalTarget(selectedTarget, direction.x > 0 ? 1 : -1);
    }

    if (next == null || next == selectedTarget) return true;

    selectedTarget = next;
    MessageBus.Send(SoundEffectPlayer.PlayMessage, "menu.move");
    RefreshTargetColors();
    return true;
  }

  public void SetHoveredTarget(GameObject target) {
    hoveredTarget = ResolveTarget(target);
    if (hoveredTarget != null) {
      selectedTarget = hoveredTarget;
    }
    RefreshTargetColors();
  }

  public void ClearHoveredTarget() {
    hoveredTarget = null;
    RefreshTargetColors();
  }

  public bool CancelActiveRebind() {
    if (rebindOperation == null) return false;

    rebindOperation.Cancel();
    return true;
  }

  void EnsureLayout() {
    if (runtimeRoot != null) return;

    ResolveVisualTemplates();
    var runtime = new GameObject("Runtime");
    runtime.layer = gameObject.layer;
    runtime.transform.SetParent(transform, false);
    runtimeRoot = runtime.transform;

    var baseOrder = spriteTemplate != null ? spriteTemplate.sortingOrder : 42;
    CreateText(
      "ActionHeader",
      runtimeRoot,
      new Vector3(-5.85f, HeaderY, 0f),
      "Action",
      0.085f,
      "left",
      44f,
      baseOrder + 4
    );
    CreateText(
      "KeyboardHeader",
      runtimeRoot,
      new Vector3(0f, HeaderY, 0f),
      "Keyboard",
      0.078f,
      "center",
      48f,
      baseOrder + 4
    );
    CreateText(
      "ControllerHeader",
      runtimeRoot,
      new Vector3(4.85f, HeaderY, 0f),
      "Controller",
      0.078f,
      "center",
      48f,
      baseOrder + 4
    );
    CreateSprite(
      "HeaderDivider",
      runtimeRoot,
      new Vector3(0f, 2.2f, 0f),
      new Vector2(13.3f, 0.08f),
      dividerSprite,
      baseOrder + 2
    );

    for (var i = 0; i < rows.Length; i++) {
      rows[i] = CreateRow(i, baseOrder);
    }

    CreateFooter(baseOrder);
    pageText = CreateText(
      "Page",
      runtimeRoot,
      new Vector3(0f, PageLabelY, 0f),
      "Gameplay 1 / 1",
      0.06f,
      "center",
      80f,
      baseOrder + 4
    );
    promptText = CreateText(
      "Prompt",
      runtimeRoot,
      new Vector3(0f, PromptY, 0f),
      "Select a value to rebind",
      0.05f,
      "center",
      260f,
      baseOrder + 4
    );
  }

  RowView CreateRow(int index, int baseOrder) {
    var rowObject = new GameObject("Row" + (index + 1));
    rowObject.layer = gameObject.layer;
    rowObject.transform.SetParent(runtimeRoot, false);
    rowObject.transform.localPosition = new Vector3(0f, FirstRowY - index * RowSpacing, 0f);

    var row = new RowView {
      root = rowObject,
      actionText = CreateText(
        "Action",
        rowObject.transform,
        new Vector3(-5.85f, 0f, 0f),
        "Action",
        0.074f,
        "left",
        54f,
        baseOrder + 4
      )
    };

    row.keyboard = CreateBindingCell(
      rowObject.transform,
      "Keyboard",
      new Vector3(0f, 0f, 0f),
      BindingDevice.Keyboard,
      baseOrder
    );
    row.gamepad = CreateBindingCell(
      rowObject.transform,
      "Controller",
      new Vector3(4.85f, 0f, 0f),
      BindingDevice.Gamepad,
      baseOrder
    );
    return row;
  }

  BindingCell CreateBindingCell(
    Transform parent,
    string name,
    Vector3 position,
    BindingDevice device,
    int baseOrder
  ) {
    var root = CreateClickableRoot(name, parent, position, new Vector2(3.75f, 0.64f));
    var background = CreateSprite(
      "backing",
      root.transform,
      Vector3.zero,
      new Vector2(3.75f, 0.64f),
      bindingSprite,
      baseOrder + 3
    );
    PrepareSelectionHighlight(background);
    var valueText = CreateText(
      "value",
      root.transform,
      new Vector3(0f, -0.02f, 0f),
      "Unbound",
      0.057f,
      "center",
      65f,
      baseOrder + 4
    );

    return new BindingCell {
      root = root,
      background = background,
      valueText = valueText,
      device = device
    };
  }

  void CreateFooter(int baseOrder) {
    previousPageRoot = CreateClickableRoot(
      "PreviousPage",
      runtimeRoot,
      new Vector3(-4.9f, FooterY, 0f),
      new Vector2(0.76f, 0.76f)
    );
    previousPageBackground = CreateSprite(
      "backing",
      previousPageRoot.transform,
      Vector3.zero,
      new Vector2(0.76f, 0.76f),
      previousPageSprite,
      baseOrder + 3
    );
    PrepareSelectionHighlight(previousPageBackground);

    defaultsRoot = CreateClickableRoot(
      "Defaults",
      runtimeRoot,
      new Vector3(0f, FooterY, 0f),
      new Vector2(3.05f, 0.62f)
    );
    defaultsBackground = CreateSprite(
      "backing",
      defaultsRoot.transform,
      Vector3.zero,
      new Vector2(3.05f, 0.62f),
      bindingSprite,
      baseOrder + 3
    );
    PrepareSelectionHighlight(defaultsBackground);
    CreateText(
      "label",
      defaultsRoot.transform,
      new Vector3(0f, -0.02f, 0f),
      "Defaults",
      0.064f,
      "center",
      48f,
      baseOrder + 4
    );

    nextPageRoot = CreateClickableRoot(
      "NextPage",
      runtimeRoot,
      new Vector3(4.9f, FooterY, 0f),
      new Vector2(0.76f, 0.76f)
    );
    nextPageBackground = CreateSprite(
      "backing",
      nextPageRoot.transform,
      Vector3.zero,
      new Vector2(0.76f, 0.76f),
      nextPageSprite,
      baseOrder + 3
    );
    PrepareSelectionHighlight(nextPageBackground);
  }

  GameObject CreateClickableRoot(
    string name,
    Transform parent,
    Vector3 position,
    Vector2 size
  ) {
    var root = new GameObject(name);
    root.layer = gameObject.layer;
    root.transform.SetParent(parent, false);
    root.transform.localPosition = position;
    var collider = root.AddComponent<BoxCollider2D>();
    collider.isTrigger = true;
    collider.size = size;
    return root;
  }

  SpriteRenderer CreateSprite(
    string name,
    Transform parent,
    Vector3 position,
    Vector2 size,
    Sprite sprite,
    int sortingOrder
  ) {
    var visual = new GameObject(name);
    visual.layer = gameObject.layer;
    visual.transform.SetParent(parent, false);
    visual.transform.localPosition = position;

    var renderer = visual.AddComponent<SpriteRenderer>();
    renderer.sprite = sprite;
    ApplyRendererTemplate(renderer, sortingOrder);
    if (sprite != null) {
      var bounds = sprite.bounds.size;
      visual.transform.localScale = new Vector3(
        bounds.x > 0f ? size.x / bounds.x : 1f,
        bounds.y > 0f ? size.y / bounds.y : 1f,
        1f
      );
    }
    return renderer;
  }

  FontText CreateText(
    string name,
    Transform parent,
    Vector3 position,
    string content,
    float scale,
    string justify,
    float maxWidth,
    int sortingOrder
  ) {
    var textObject = new GameObject(name);
    textObject.layer = gameObject.layer;
    textObject.transform.SetParent(parent, false);
    textObject.transform.localPosition = position;
    textObject.transform.localScale = new Vector3(scale, scale, 1f);

    var hostRenderer = textObject.AddComponent<SpriteRenderer>();
    hostRenderer.enabled = false;
    ApplyRendererTemplate(hostRenderer, sortingOrder);

    var text = textObject.AddComponent<FontText>();
    CopyFontTemplate(fontTemplate, text);
    text.content = content;
    text.justifyX = justify;
    text.justifyY = "center";
    text.maxWidth = maxWidth;
    text.Generate();
    return text;
  }

  void ResolveVisualTemplates() {
    var settingsRoot = transform.parent;
    var ui = FindDirectChild(settingsRoot, "UI");
    var header = FindDirectChild(ui, "text");
    fontTemplate = header != null ? header.GetComponent<FontText>() : null;

    var backing = FindDirectChild(settingsRoot, "Backing");
    var backingVisual = FindDirectChild(backing, "backing");
    spriteTemplate = backingVisual != null
      ? backingVisual.GetComponent<SpriteRenderer>()
      : null;
  }

  void ApplyRendererTemplate(SpriteRenderer renderer, int sortingOrder) {
    if (renderer == null) return;

    if (spriteTemplate != null) {
      renderer.sharedMaterial = spriteTemplate.sharedMaterial;
      renderer.sortingLayerID = spriteTemplate.sortingLayerID;
      renderer.maskInteraction = spriteTemplate.maskInteraction;
    }
    renderer.sortingOrder = sortingOrder;
  }

  static void CopyFontTemplate(FontText source, FontText target) {
    if (target == null) return;

    if (source != null) {
      target.characterPrefab = source.characterPrefab;
      target.font = source.font;
      target.spaceWidth = source.spaceWidth;
      target.padding = source.padding;
      target.mono = source.mono;
      target.marginX = source.marginX;
      target.marginY = source.marginY;
      target.offsetX = source.offsetX;
      target.offsetY = source.offsetY;
      target.lineDirection = source.lineDirection;
      return;
    }

    target.font = "Plate";
    target.spaceWidth = 6.52f;
    target.padding = -0.25f;
  }

  void ResolveActions() {
    inputProcessor ??= FindAnyObjectByType<InputProcessor>();
    var asset = inputProcessor != null ? inputProcessor.Actions : null;
    gameplayMap = asset != null ? asset.FindActionMap(GameplayMapName) : null;

    actions.Clear();
    // Keep menu-navigation maps fixed so the rebinding screen cannot lock itself out.
    if (gameplayMap != null) {
      foreach (var action in gameplayMap.actions) {
        if (HasDeviceBinding(action, BindingDevice.Keyboard) ||
            HasDeviceBinding(action, BindingDevice.Gamepad)) {
          actions.Add(action);
        }
      }
    }

    var pageCount = ResolvePageCount();
    pageIndex = Mathf.Clamp(pageIndex, 0, pageCount - 1);
  }

  void RefreshPage() {
    if (runtimeRoot == null) return;

    var previousRow = selectedTarget != null ? selectedTarget.navigationRow : 0;
    var previousColumn = selectedTarget != null ? selectedTarget.navigationColumn : 0;
    visibleTargets.Clear();
    targetsByRoot.Clear();
    selectedTarget = null;
    hoveredTarget = null;

    var firstActionIndex = pageIndex * RowsPerPage;
    visibleRowCount = Mathf.Clamp(actions.Count - firstActionIndex, 0, RowsPerPage);
    for (var i = 0; i < rows.Length; i++) {
      var row = rows[i];
      var actionIndex = firstActionIndex + i;
      var visible = actionIndex < actions.Count;
      row.root.SetActive(visible);
      if (!visible) continue;

      var action = actions[actionIndex];
      SetText(row.actionText, GetActionLabel(action.name));
      ConfigureBindingCell(row.keyboard, action, i, 0);
      ConfigureBindingCell(row.gamepad, action, i, 1);
    }

    var pageCount = ResolvePageCount();
    var showPagination = pageCount > 1;
    previousPageRoot.SetActive(showPagination);
    nextPageRoot.SetActive(showPagination);
    if (showPagination) {
      RegisterTarget(new SelectableTarget {
        kind = TargetKind.PreviousPage,
        root = previousPageRoot,
        background = previousPageBackground,
        navigationRow = visibleRowCount,
        navigationColumn = 0
      });
    }

    RegisterTarget(new SelectableTarget {
      kind = TargetKind.Defaults,
      root = defaultsRoot,
      background = defaultsBackground,
      navigationRow = visibleRowCount,
      navigationColumn = showPagination ? 1 : 0
    });

    if (showPagination) {
      RegisterTarget(new SelectableTarget {
        kind = TargetKind.NextPage,
        root = nextPageRoot,
        background = nextPageBackground,
        navigationRow = visibleRowCount,
        navigationColumn = 2
      });
    }

    var pageLabel = actions.Count > 0
      ? "Gameplay " + (pageIndex + 1) + " / " + pageCount
      : "Gameplay controls unavailable";
    SetText(pageText, pageLabel);
    SetText(
      promptText,
      actions.Count > 0
        ? "Select a value to rebind"
        : "Input actions are not available"
    );

    selectedTarget = FindClosestTarget(previousRow, previousColumn) ??
                     (visibleTargets.Count > 0 ? visibleTargets[0] : null);
    RefreshTargetColors();
  }

  void ConfigureBindingCell(
    BindingCell cell,
    InputAction action,
    int navigationRow,
    int navigationColumn
  ) {
    cell.action = action;
    cell.bindingIndex = FindBindingIndex(action, cell.device);
    cell.root.SetActive(cell.bindingIndex >= 0);
    if (cell.bindingIndex < 0) return;

    SetText(cell.valueText, GetBindingDisplayString(action, cell.bindingIndex));
    var selectable = new SelectableTarget {
      kind = TargetKind.Binding,
      root = cell.root,
      background = cell.background,
      binding = cell,
      navigationRow = navigationRow,
      navigationColumn = navigationColumn
    };
    cell.selectable = selectable;
    RegisterTarget(selectable);
  }

  void RegisterTarget(SelectableTarget target) {
    if (target == null || target.root == null) return;

    SetSelectionOutline(target, false, force: true);
    visibleTargets.Add(target);
    targetsByRoot[target.root] = target;
  }

  void Activate(SelectableTarget target) {
    if (target == null) return;

    switch (target.kind) {
      case TargetKind.Binding:
        StartRebind(target.binding);
        break;
      case TargetKind.PreviousPage:
        pageIndex = WrapPage(pageIndex - 1);
        MessageBus.Send(SoundEffectPlayer.PlayMessage, SoundEffectPlayer.MenuSelectSoundId);
        RefreshPage();
        break;
      case TargetKind.Defaults:
        inputProcessor?.ResetBindingOverrides();
        MessageBus.Send(SoundEffectPlayer.PlayMessage, SoundEffectPlayer.MenuSelectSoundId);
        ResolveActions();
        RefreshPage();
        break;
      case TargetKind.NextPage:
        pageIndex = WrapPage(pageIndex + 1);
        MessageBus.Send(SoundEffectPlayer.PlayMessage, SoundEffectPlayer.MenuSelectSoundId);
        RefreshPage();
        break;
    }
  }

  void StartRebind(BindingCell cell) {
    if (cell == null || cell.action == null || cell.bindingIndex < 0) return;
    if (cell.bindingIndex >= cell.action.bindings.Count) return;

    CancelActiveRebind();
    var binding = cell.action.bindings[cell.bindingIndex];
    if (binding.id == Guid.Empty) {
      Debug.LogWarning(
        "[SettingsControlsPageController] Cannot safely rebind a binding without an ID.",
        this
      );
      return;
    }

    var asset = inputProcessor != null ? inputProcessor.Actions : null;
    disabledSettingsMap = asset != null ? asset.FindActionMap(SettingsMapName) : null;
    disabledTargetMap = cell.action.actionMap;
    settingsMapWasEnabled = disabledSettingsMap != null && disabledSettingsMap.enabled;
    targetMapWasEnabled = disabledTargetMap != null && disabledTargetMap.enabled;

    if (disabledTargetMap != null && disabledTargetMap.enabled) {
      disabledTargetMap.Disable();
    }
    if (disabledSettingsMap != null &&
        disabledSettingsMap != disabledTargetMap &&
        disabledSettingsMap.enabled) {
      disabledSettingsMap.Disable();
    }

    awaitingCell = cell;
    SetText(
      cell.valueText,
      cell.device == BindingDevice.Keyboard ? "Press a key" : "Press a control"
    );
    SetText(
      promptText,
      cell.device == BindingDevice.Keyboard
        ? "Waiting for keyboard input - Escape or click to cancel"
        : "Waiting for controller input - Escape or click to cancel"
    );
    RefreshTargetColors();

    try {
      var requiredDevice = cell.device == BindingDevice.Keyboard
        ? "<Keyboard>"
        : "<Gamepad>";
      var expectedControlLayout = InputControlPath.TryGetControlLayout(binding.path);
      // WithTargetBinding also imports this asset's combined Keyboard/Mouse/Gamepad
      // scheme as OR filters. The binding GUID mask targets only this device slot.
      var operation = cell.action.PerformInteractiveRebinding()
        .WithBindingMask(new InputBinding { id = binding.id })
        .WithControlsHavingToMatchPath(requiredDevice);
      if (!string.IsNullOrWhiteSpace(expectedControlLayout)) {
        operation.WithExpectedControlType(expectedControlLayout);
      }
      operation
        .WithCancelingThrough("<Keyboard>/escape")
        .WithActionEventNotificationsBeingSuppressed()
        .WithTimeout(RebindTimeoutSeconds)
        .OnCancel(FinishCanceledRebind)
        .OnComplete(FinishCompletedRebind);
      rebindOperation = operation;
      operation.Start();
    }
    catch (Exception exception) {
      Debug.LogWarning(
        "[SettingsControlsPageController] Could not start rebinding " +
        cell.action.name + ": " + exception.Message,
        this
      );
      FinishRebind(rebindOperation, save: false);
    }
  }

  void FinishCanceledRebind(
    InputActionRebindingExtensions.RebindingOperation operation
  ) {
    FinishRebind(operation, save: false);
  }

  void FinishCompletedRebind(
    InputActionRebindingExtensions.RebindingOperation operation
  ) {
    FinishRebind(operation, save: true);
  }

  void FinishRebind(
    InputActionRebindingExtensions.RebindingOperation operation,
    bool save
  ) {
    if (operation != null && rebindOperation != null && operation != rebindOperation) return;

    var activeOperation = rebindOperation ?? operation;
    rebindOperation = null;
    activeOperation?.Dispose();
    RestoreInputMaps();

    if (save) {
      inputProcessor?.PersistBindingOverrides();
      MessageBus.Send(SoundEffectPlayer.PlayMessage, SoundEffectPlayer.MenuSelectSoundId);
    }

    awaitingCell = null;
    if (!isActiveAndEnabled) return;

    ResolveActions();
    RefreshPage();
  }

  void RestoreInputMaps() {
    if (disabledTargetMap != null && disabledTargetMap == disabledSettingsMap) {
      if (targetMapWasEnabled || settingsMapWasEnabled) {
        disabledTargetMap.Enable();
      }
    }
    else {
      if (targetMapWasEnabled && disabledTargetMap != null) {
        disabledTargetMap.Enable();
      }
      if (settingsMapWasEnabled && disabledSettingsMap != null) {
        disabledSettingsMap.Enable();
      }
    }

    disabledTargetMap = null;
    disabledSettingsMap = null;
    targetMapWasEnabled = false;
    settingsMapWasEnabled = false;
  }

  SelectableTarget FindVerticalTarget(SelectableTarget current, int direction) {
    var footerRow = visibleRowCount;
    var rowCount = footerRow + 1;
    if (rowCount <= 0) return current;

    var row = current.navigationRow;
    for (var step = 0; step < rowCount; step++) {
      row = (row + direction + rowCount) % rowCount;
      var match = FindClosestTarget(row, current.navigationColumn);
      if (match != null) return match;
    }
    return current;
  }

  SelectableTarget FindHorizontalTarget(SelectableTarget current, int direction) {
    var sameRow = new List<SelectableTarget>();
    for (var i = 0; i < visibleTargets.Count; i++) {
      var candidate = visibleTargets[i];
      if (candidate.navigationRow == current.navigationRow) {
        sameRow.Add(candidate);
      }
    }
    if (sameRow.Count <= 1) return current;

    sameRow.Sort((a, b) => a.navigationColumn.CompareTo(b.navigationColumn));
    var index = sameRow.IndexOf(current);
    if (index < 0) return sameRow[0];
    index = (index + direction + sameRow.Count) % sameRow.Count;
    return sameRow[index];
  }

  SelectableTarget FindClosestTarget(int row, int column) {
    SelectableTarget best = null;
    var bestDistance = int.MaxValue;
    for (var i = 0; i < visibleTargets.Count; i++) {
      var candidate = visibleTargets[i];
      if (candidate.navigationRow != row) continue;

      var distance = Mathf.Abs(candidate.navigationColumn - column);
      if (distance >= bestDistance) continue;
      best = candidate;
      bestDistance = distance;
    }
    return best;
  }

  SelectableTarget ResolveTarget(GameObject target) {
    if (target == null) return null;

    var current = target.transform;
    while (current != null && current != transform.parent) {
      if (targetsByRoot.TryGetValue(current.gameObject, out var selectable)) {
        return selectable;
      }
      current = current.parent;
    }
    return null;
  }

  void RefreshTargetColors() {
    for (var i = 0; i < visibleTargets.Count; i++) {
      var target = visibleTargets[i];
      if (target.background == null) continue;

      var isWaiting = target.binding != null && target.binding == awaitingCell;
      var isSelected = target == selectedTarget || target == hoveredTarget;
      SetSelectionOutline(target, isWaiting || isSelected);
      target.background.color = isWaiting ? WaitingColor : Color.white;
    }
  }

  void PrepareSelectionHighlight(SpriteRenderer background) {
    if (background == null) return;

    var animator = background.GetComponent<AllIn1AnimatorInspector>() ??
                   background.gameObject.AddComponent<AllIn1AnimatorInspector>();
    animator.SetKeyword(SelectionOutlineKeyword, false);
  }

  void SetSelectionOutline(SelectableTarget target, bool enabled, bool force = false) {
    if (target == null || target.root == null) return;
    if (!force && target.outlineEnabled == enabled) return;

    ButtonShaderKeywords.ApplyToButton(target.root, SelectionOutlineKeyword, enabled);
    target.outlineEnabled = enabled;
  }

  int ResolvePageCount() {
    return Mathf.Max(1, Mathf.CeilToInt(actions.Count / (float)RowsPerPage));
  }

  int WrapPage(int value) {
    var count = ResolvePageCount();
    return (value % count + count) % count;
  }

  static bool HasDeviceBinding(InputAction action, BindingDevice device) {
    return FindBindingIndex(action, device) >= 0;
  }

  static int FindBindingIndex(InputAction action, BindingDevice device) {
    if (action == null) return -1;

    // The menu exposes one primary slot per device and leaves secondary mouse,
    // Escape, and platform-specific bindings intact.
    for (var i = 0; i < action.bindings.Count; i++) {
      var binding = action.bindings[i];
      if (binding.isComposite) continue;
      if (MatchesDevice(binding.path, device)) return i;
    }
    return -1;
  }

  static bool MatchesDevice(string path, BindingDevice device) {
    if (string.IsNullOrWhiteSpace(path)) return false;

    var layout = InputControlPath.TryGetDeviceLayout(path);
    if (string.IsNullOrWhiteSpace(layout)) return false;
    return device == BindingDevice.Keyboard
      ? InputSystem.IsFirstLayoutBasedOnSecond(layout, "Keyboard")
      : InputSystem.IsFirstLayoutBasedOnSecond(layout, "Gamepad");
  }

  static string GetBindingDisplayString(InputAction action, int bindingIndex) {
    if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count) {
      return "Unbound";
    }

    var display = action.GetBindingDisplayString(
      bindingIndex,
      InputBinding.DisplayStringOptions.DontUseShortDisplayNames
    );
    if (string.IsNullOrWhiteSpace(display)) return "Unbound";

    return display
      .Replace("Left Stick", "L Stick")
      .Replace("Right Stick", "R Stick")
      .Replace("Left Shoulder", "L Shoulder")
      .Replace("Right Shoulder", "R Shoulder")
      .Replace("Left Trigger", "L Trigger")
      .Replace("Right Trigger", "R Trigger");
  }

  static string GetActionLabel(string actionName) {
    switch (actionName) {
      case "charUp": return "Move Up";
      case "charLeft": return "Move Left";
      case "charRight": return "Move Right";
      case "charDown": return "Move Down";
      case "jump": return "Jump";
      case "dash": return "Dash";
      case "block": return "Block";
      case "dodge": return "Dodge";
      case "attack1": return "Attack 1";
      case "attack2": return "Attack 2";
      case "attack3": return "Attack 3";
      case "attack4": return "Attack 4";
      case "pause": return "Pause";
      case "dance": return "Dance";
      case "wheel": return "Ability Wheel";
      default: return Nicify(actionName);
    }
  }

  static string Nicify(string value) {
    if (string.IsNullOrWhiteSpace(value)) return "Action";

    var result = new System.Text.StringBuilder(value.Length + 8);
    for (var i = 0; i < value.Length; i++) {
      var character = value[i];
      if (i > 0 &&
          (char.IsUpper(character) ||
           (char.IsDigit(character) && !char.IsDigit(value[i - 1])))) {
        result.Append(' ');
      }
      result.Append(i == 0 ? char.ToUpperInvariant(character) : character);
    }
    return result.ToString();
  }

  static void SetText(FontText text, string value) {
    if (text == null || text.content == value) return;

    text.content = value;
    text.Generate();
  }

  static Transform FindDirectChild(Transform parent, string name) {
    if (parent == null) return null;

    for (var i = 0; i < parent.childCount; i++) {
      var child = parent.GetChild(i);
      if (child.name == name) return child;
    }
    return null;
  }
}
