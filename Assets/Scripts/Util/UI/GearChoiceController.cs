using System;
using System.Collections.Generic;
using Esperanza.UI;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class GearChoiceController : MonoBehaviour {
  const string ItemLibrary = "Items/Items";
  const int VisibleRowCount = 4;
  const float RowSpacing = 2.35f;
  const int SortingOrderOffset = 30;

  sealed class Choice {
    public int inventoryIndex;
    public GearItem gearItem;
    public bool remove;
  }

  readonly List<Action> actions = new();
  readonly List<Choice> choices = new();
  readonly List<GameObject> rowPool = new();
  readonly Dictionary<GameObject, int> choiceIndexByRow = new();
  readonly List<Collider2D> lockedColliders = new();

  GearButtons owner;
  GameObject itemPrefab;
  ItemCard itemCard;
  FontText fontTemplate;
  FontText titleText;
  Transform poolRoot;
  GameObject sourceSlotButton;
  string slotName;
  int focusedChoiceIndex = -1;
  int scrollOffset;
  bool isOpen;
  bool committingChoice;

  public bool IsOpen => isOpen;

  public void Initialize(GearButtons choiceOwner, GameObject choiceItemPrefab, ItemCard previewCard) {
    owner = choiceOwner;
    itemPrefab = choiceItemPrefab;
    itemCard = previewCard;
    fontTemplate = ResolveFontTemplate();
    RegisterHandlers();
  }

  void OnDestroy() {
    UnlockUnderlyingColliders();
    for (var i = 0; i < actions.Count; i++) {
      actions[i]?.Invoke();
    }
    actions.Clear();
  }

  void RegisterHandlers() {
    if (actions.Count > 0) {
      return;
    }

    actions.Add(MessageBus.On("pauseMenu.scrollUp", _ => {
      if (isOpen) MoveSelection(-1);
    }));
    actions.Add(MessageBus.On("pauseMenu.scrollDown", _ => {
      if (isOpen) MoveSelection(1);
    }));
    actions.Add(MessageBus.On(Inventory.ChangedMessage, _ => {
      if (isOpen && !committingChoice) {
        Close(restoreSlotPreview: true);
      }
    }));
  }

  public bool Open(GameObject slotButton, string requestedSlot) {
    if (owner == null || itemPrefab == null || slotButton == null) {
      return false;
    }

    var formName = EsperanzaForms.GetActive();
    if (!EquippedItems.TryResolveSlot(formName, requestedSlot, out var resolvedSlot)) {
      return false;
    }

    BuildChoices(formName, resolvedSlot);
    if (choices.Count == 0) {
      return false;
    }

    if (isOpen) {
      Close(restoreSlotPreview: false);
    }

    sourceSlotButton = slotButton;
    slotName = resolvedSlot;
    focusedChoiceIndex = 0;
    scrollOffset = 0;
    PositionWindow(slotButton);
    EnsureRows(Mathf.Min(VisibleRowCount, choices.Count));
    ConfigureTitle(resolvedSlot);
    RefreshRows();
    LockUnderlyingColliders();

    isOpen = true;
    gameObject.SetActive(true);
    PreviewFocusedChoice();
    MouseManager.Instance?.RefreshHoverTarget();
    return true;
  }

  void BuildChoices(string formName, string resolvedSlot) {
    choices.Clear();
    var inventoryGear = Inventory.Gear;
    if (inventoryGear != null) {
      for (var i = 0; i < inventoryGear.Count; i++) {
        var item = inventoryGear[i];
        if (item == null || string.IsNullOrWhiteSpace(item.gearId) ||
            !EquippedItems.AreSlotsCompatible(item.slot, resolvedSlot)) {
          continue;
        }

        choices.Add(new Choice {
          inventoryIndex = i,
          gearItem = EquippedItems.CloneGearItem(item)
        });
      }
    }

    EquippedItems.EnsureForm(formName);
    var equippedItem = EquippedItems.AllGearForms[formName][resolvedSlot];
    if (equippedItem != null && !EquippedItems.IsDefaultGearSlot(formName, resolvedSlot)) {
      choices.Add(new Choice { inventoryIndex = -1, remove = true });
    }
  }

  public bool TryHandleHover(GameObject target) {
    if (!isOpen) {
      return false;
    }

    var choiceIndex = ResolveChoiceIndex(target);
    if (choiceIndex >= 0) {
      SetFocusedChoice(choiceIndex, playMoveSound: true);
    }
    return true;
  }

  public bool TryHandleClick(GameObject target) {
    if (!isOpen) {
      return false;
    }

    var choiceIndex = ResolveChoiceIndex(target);
    if (choiceIndex >= 0) {
      SetFocusedChoice(choiceIndex, playMoveSound: false);
      CommitFocusedChoice();
    }
    return true;
  }

  public bool TryMove(Vector2 direction) {
    if (!isOpen) {
      return false;
    }

    if (direction.y > 0.01f) {
      MoveSelection(-1);
    }
    else if (direction.y < -0.01f) {
      MoveSelection(1);
    }
    return true;
  }

  public bool TrySelect() {
    if (!isOpen) {
      return false;
    }

    CommitFocusedChoice();
    return true;
  }

  public bool TryCancel() {
    if (!isOpen) {
      return false;
    }

    Close(restoreSlotPreview: true);
    return true;
  }

  public void ClearHover() {
    if (isOpen) {
      PreviewFocusedChoice();
    }
  }

  public void Close(bool restoreSlotPreview) {
    if (!isOpen && !gameObject.activeSelf) {
      return;
    }

    var previousSourceButton = sourceSlotButton;
    isOpen = false;
    sourceSlotButton = null;
    slotName = null;
    focusedChoiceIndex = -1;
    choiceIndexByRow.Clear();
    itemCard?.Hide();
    gameObject.SetActive(false);
    UnlockUnderlyingColliders();
    owner?.OnChoiceWindowClosed(previousSourceButton, restoreSlotPreview);
    MouseManager.Instance?.RefreshHoverTarget();
  }

  void MoveSelection(int delta) {
    if (choices.Count == 0 || delta == 0) {
      return;
    }

    var nextIndex = Mathf.Clamp(focusedChoiceIndex + delta, 0, choices.Count - 1);
    SetFocusedChoice(nextIndex, playMoveSound: true);
  }

  void SetFocusedChoice(int choiceIndex, bool playMoveSound) {
    if (choiceIndex < 0 || choiceIndex >= choices.Count || focusedChoiceIndex == choiceIndex) {
      return;
    }

    var previousScrollOffset = scrollOffset;
    focusedChoiceIndex = choiceIndex;
    ScrollFocusedChoiceIntoView();
    if (scrollOffset != previousScrollOffset) {
      RefreshRows();
    }
    else {
      RefreshSelectionVisuals();
    }
    RefreshTitle();
    PreviewFocusedChoice();
    if (playMoveSound) {
      MessageBus.Send(SoundEffectPlayer.PlayMessage, "menu.move");
    }
  }

  void ScrollFocusedChoiceIntoView() {
    if (focusedChoiceIndex < scrollOffset) {
      scrollOffset = focusedChoiceIndex;
    }
    else if (focusedChoiceIndex >= scrollOffset + VisibleRowCount) {
      scrollOffset = focusedChoiceIndex - VisibleRowCount + 1;
    }

    scrollOffset = Mathf.Clamp(
      scrollOffset,
      0,
      Mathf.Max(0, choices.Count - VisibleRowCount)
    );
  }

  void CommitFocusedChoice() {
    if (focusedChoiceIndex < 0 || focusedChoiceIndex >= choices.Count) {
      return;
    }

    var state = CharacterState.Current;
    if (state == null) {
      Debug.LogWarning("[GearChoiceController] CharacterState is not available.");
      return;
    }

    var choice = choices[focusedChoiceIndex];
    committingChoice = true;
    var changed = false;
    try {
      changed = choice.remove
        ? state.TryUnequipGear(slotName, "pause_menu")
        : state.TryEquipInventoryGear(slotName, choice.inventoryIndex, "pause_menu");
    }
    finally {
      committingChoice = false;
    }
    if (!changed) {
      return;
    }

    MessageBus.Send(SoundEffectPlayer.PlayMessage, SoundEffectPlayer.MenuSelectSoundId);
    if (isOpen) {
      Close(restoreSlotPreview: true);
    }
  }

  void PreviewFocusedChoice() {
    if (itemCard == null || focusedChoiceIndex < 0 || focusedChoiceIndex >= choices.Count) {
      return;
    }

    var choice = choices[focusedChoiceIndex];
    if (choice.remove || choice.gearItem == null) {
      itemCard.Hide();
      return;
    }

    var visibleRow = focusedChoiceIndex - scrollOffset;
    var icon = visibleRow >= 0 && visibleRow < rowPool.Count
      ? FindDirectChild(rowPool[visibleRow].transform, "image")?.GetComponent<SpriteWithNormals>()
      : null;
    itemCard.SetupGear(choice.gearItem, icon);
  }

  void PositionWindow(GameObject slotButton) {
    var sourceCollider = slotButton.GetComponent<Collider2D>() ??
      slotButton.GetComponentInParent<Collider2D>(includeInactive: true);
    var sourceRenderer = slotButton.GetComponent<SpriteRenderer>() ??
      slotButton.GetComponentInChildren<SpriteRenderer>(includeInactive: true);
    var sourceWorldPosition = sourceCollider != null
      ? sourceCollider.bounds.center
      : sourceRenderer != null
        ? sourceRenderer.bounds.center
        : slotButton.transform.position;
    var sourceLocalPosition = owner.transform.InverseTransformPoint(sourceWorldPosition);
    var horizontalOffset = sourceLocalPosition.x >= 4.5f ? -3.7f : 3.7f;
    transform.localPosition = new Vector3(
      sourceLocalPosition.x + horizontalOffset,
      Mathf.Clamp(sourceLocalPosition.y, -0.25f, 0.25f),
      -0.15f
    );
    transform.localRotation = Quaternion.identity;
    transform.localScale = Vector3.one;
  }

  void EnsureRows(int requiredRows) {
    EnsurePoolRoot();
    while (rowPool.Count < requiredRows) {
      var row = Instantiate(itemPrefab, poolRoot, worldPositionStays: false);
      row.SetActive(false);
      RecenterPrefabRow(row);
      RaiseSortingOrder(row);
      row.transform.SetParent(transform, worldPositionStays: false);
      rowPool.Add(row);
    }
  }

  void EnsurePoolRoot() {
    if (poolRoot != null) {
      return;
    }

    var poolObject = new GameObject("GearChoicePool");
    poolObject.transform.SetParent(transform, worldPositionStays: false);
    poolObject.SetActive(false);
    poolRoot = poolObject.transform;
  }

  void RefreshRows() {
    choiceIndexByRow.Clear();
    for (var rowIndex = 0; rowIndex < rowPool.Count; rowIndex++) {
      var row = rowPool[rowIndex];
      var choiceIndex = scrollOffset + rowIndex;
      if (choiceIndex >= choices.Count) {
        row.SetActive(false);
        continue;
      }

      row.name = "GearChoice_" + choiceIndex;
      row.transform.localPosition = new Vector3(
        0f,
        (VisibleRowCount - 1) * RowSpacing * 0.5f - rowIndex * RowSpacing,
        0f
      );
      row.transform.localRotation = Quaternion.identity;
      row.transform.localScale = Vector3.one;
      ConfigureRow(row, choices[choiceIndex]);
      SetVisualState(row, choiceIndex == focusedChoiceIndex);
      choiceIndexByRow[row] = choiceIndex;
      if (!row.activeSelf) {
        row.SetActive(true);
      }
    }
  }

  void RefreshSelectionVisuals() {
    foreach (var entry in choiceIndexByRow) {
      SetVisualState(entry.Key, entry.Value == focusedChoiceIndex);
    }
  }

  void ConfigureRow(GameObject row, Choice choice) {
    var image = FindDirectChild(row.transform, "image");
    var icon = image != null ? image.GetComponent<SpriteWithNormals>() : null;
    var iconRenderer = image != null ? image.GetComponent<SpriteRenderer>() : null;
    var removeLabel = FindDirectChild(row.transform, "RemoveLabel");

    if (removeLabel != null) {
      removeLabel.gameObject.SetActive(choice.remove);
    }
    else if (choice.remove) {
      removeLabel = CreateRowLabel(row, "REMOVE").transform;
    }

    if (iconRenderer != null) {
      iconRenderer.enabled = !choice.remove && choice.gearItem != null;
    }
    if (icon == null || choice.remove || choice.gearItem == null) {
      icon?.SetDoNotRender(true);
      return;
    }

    icon.SetDoNotRender(false);
    icon.SetLibraryName(ItemLibrary);
    icon.SetLabelPrefix(choice.gearItem.gearId);
    icon.SetAnimation(ResolveIconCategory(choice.gearItem.slot));
    icon.SetIsAnimation(false);
    icon.ForceUpdateSpriteAndNormal(0);
  }

  GameObject CreateRowLabel(GameObject row, string content) {
    var labelObject = new GameObject("RemoveLabel");
    labelObject.layer = row.layer;
    labelObject.transform.SetParent(row.transform, worldPositionStays: false);
    labelObject.transform.localPosition = new Vector3(0f, -0.1f, -0.02f);
    labelObject.transform.localScale = Vector3.one * 0.06f;
    if (fontTemplate == null || fontTemplate.characterPrefab == null) {
      return labelObject;
    }

    var renderer = labelObject.AddComponent<SpriteRenderer>();
    CopyHighestSorting(row, renderer, 1);
    var text = labelObject.AddComponent<FontText>();
    CopyFontSettings(fontTemplate, text);
    text.justifyX = "center";
    text.justifyY = "center";
    text.content = content;
    text.Generate();
    return labelObject;
  }

  void ConfigureTitle(string resolvedSlot) {
    if (titleText == null) {
      titleText = CreateWindowText("GearChoiceTitle");
    }
    if (titleText == null) {
      return;
    }

    titleText.transform.localPosition = new Vector3(
      0f,
      (VisibleRowCount - 1) * RowSpacing * 0.5f + 1.45f,
      -0.02f
    );
    RefreshTitle();
  }

  void RefreshTitle() {
    if (titleText == null || string.IsNullOrWhiteSpace(slotName)) {
      return;
    }

    titleText.content = slotName.ToUpperInvariant() + " " +
      (focusedChoiceIndex + 1) + "/" + choices.Count;
    titleText.Generate();
  }

  FontText CreateWindowText(string objectName) {
    if (fontTemplate == null || fontTemplate.characterPrefab == null) {
      return null;
    }

    var textObject = new GameObject(objectName);
    textObject.layer = gameObject.layer;
    textObject.transform.SetParent(transform, worldPositionStays: false);
    textObject.transform.localScale = Vector3.one * 0.065f;
    var renderer = textObject.AddComponent<SpriteRenderer>();
    if (rowPool.Count > 0) {
      CopyHighestSorting(rowPool[0], renderer, 2);
    }
    else {
      renderer.sortingOrder = SortingOrderOffset + 10;
    }
    var text = textObject.AddComponent<FontText>();
    CopyFontSettings(fontTemplate, text);
    text.justifyX = "center";
    text.justifyY = "center";
    return text;
  }

  FontText ResolveFontTemplate() {
    if (itemCard == null) {
      return null;
    }

    var texts = itemCard.GetComponentsInChildren<FontText>(includeInactive: true);
    FontText fallback = null;
    for (var i = 0; i < texts.Length; i++) {
      var text = texts[i];
      if (text == null || text.characterPrefab == null) continue;
      fallback ??= text;
      if (string.Equals(text.font, "Plate", StringComparison.OrdinalIgnoreCase)) {
        return text;
      }
    }
    return fallback;
  }

  static void CopyFontSettings(FontText source, FontText target) {
    target.characterPrefab = source.characterPrefab;
    target.font = "Plate";
    target.spaceWidth = source.spaceWidth;
    target.padding = source.padding;
    target.maxWidth = source.maxWidth;
  }

  static void RecenterPrefabRow(GameObject row) {
    var image = FindDirectChild(row.transform, "image");
    if (image == null) {
      return;
    }

    var anchor = image.localPosition;
    for (var i = 0; i < row.transform.childCount; i++) {
      row.transform.GetChild(i).localPosition -= anchor;
    }
    var collider = row.GetComponent<BoxCollider2D>();
    if (collider != null) {
      collider.offset -= (Vector2)anchor;
      collider.isTrigger = true;
    }
  }

  static void RaiseSortingOrder(GameObject row) {
    var renderers = row.GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
    for (var i = 0; i < renderers.Length; i++) {
      renderers[i].sortingOrder += SortingOrderOffset;
    }
  }

  static void CopyHighestSorting(GameObject row, SpriteRenderer target, int offset) {
    var sourceRenderers = row.GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
    SpriteRenderer highest = null;
    for (var i = 0; i < sourceRenderers.Length; i++) {
      var source = sourceRenderers[i];
      if (source == null || source == target) continue;
      if (highest == null || source.sortingOrder > highest.sortingOrder) {
        highest = source;
      }
    }
    if (highest == null) return;
    target.sortingLayerID = highest.sortingLayerID;
    target.sortingOrder = highest.sortingOrder + offset;
    target.maskInteraction = highest.maskInteraction;
  }

  void LockUnderlyingColliders() {
    UnlockUnderlyingColliders();
    var navigation = GetComponentInParent<PauseMenuCharacterButtonsInput>(includeInactive: true);
    if (navigation == null) {
      return;
    }

    var colliders = navigation.GetComponentsInChildren<Collider2D>(includeInactive: true);
    for (var i = 0; i < colliders.Length; i++) {
      var collider = colliders[i];
      if (collider == null || !collider.enabled || collider.transform.IsChildOf(transform)) {
        continue;
      }
      lockedColliders.Add(collider);
      collider.enabled = false;
    }
  }

  void UnlockUnderlyingColliders() {
    for (var i = 0; i < lockedColliders.Count; i++) {
      if (lockedColliders[i] != null) {
        lockedColliders[i].enabled = true;
      }
    }
    lockedColliders.Clear();
  }

  int ResolveChoiceIndex(GameObject target) {
    if (target == null) {
      return -1;
    }

    foreach (var entry in choiceIndexByRow) {
      if (entry.Key == target || target.transform.IsChildOf(entry.Key.transform)) {
        return entry.Value;
      }
    }
    return -1;
  }

  static void SetVisualState(GameObject row, bool active) {
    var activeVisual = FindDirectChild(row.transform, "active");
    var inactiveVisual = FindDirectChild(row.transform, "inactive");
    if (activeVisual != null) activeVisual.gameObject.SetActive(active);
    if (inactiveVisual != null) inactiveVisual.gameObject.SetActive(!active);
  }

  static string ResolveIconCategory(string itemSlot) {
    return itemSlot != null && itemSlot.StartsWith("Ring", StringComparison.OrdinalIgnoreCase)
      ? "Ring"
      : itemSlot;
  }

  static Transform FindDirectChild(Transform parent, string childName) {
    if (parent == null) {
      return null;
    }

    for (var i = 0; i < parent.childCount; i++) {
      var child = parent.GetChild(i);
      if (child != null && child.name == childName) {
        return child;
      }
    }
    return null;
  }
}
