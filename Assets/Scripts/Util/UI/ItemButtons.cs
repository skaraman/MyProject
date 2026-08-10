using System;
using System.Collections.Generic;
using Esperanza.UI;
using UnityEngine;

public class ItemButtons : ButtonGroup {
  enum InventoryCategory {
    Gear,
    Gems,
    Consume,
    Quest
  }

  const string ItemLibrary = "Items/Items";

  public GameObject itemPrefab;
  public GameObject itemsParent;
  public GameObject itemViewer;

  [Header("Grid")]
  [SerializeField, Min(1)] int columnCount = 7;
  [SerializeField] Vector2 firstItemLocalPosition = new(2.09f, -0.46f);
  [SerializeField, Min(0.01f)] float columnSpacing = 3.1f;
  [SerializeField, Min(0.01f)] float rowSpacing = 2.35f;

  [Header("Stack Count")]
  [SerializeField] Vector2 countOffsetFromIcon = new(1.15f, -0.78f);
  [SerializeField, Min(0.001f)] float countTextScale = 0.06f;

  [Header("Tooltip")]
  [SerializeField] Vector2 tooltipOffsetFromItem = new(0f, 1.25f);
  [SerializeField, Min(0.001f)] float tooltipTextScale = 0.075f;

  readonly List<Action> actions = new();
  readonly List<GameObject> itemPool = new();
  readonly Dictionary<GameObject, GearItem> gearByButton = new();
  readonly Dictionary<GameObject, string> displayNameByButton = new();

  InventoryCategory category = InventoryCategory.Gear;
  Transform inactivePoolRoot;
  ItemCard itemCard;
  FontText countTextTemplate;
  FontText tooltipText;
  bool legacyTemplateResolved;
  bool missingPrefabWarningLogged;
  bool missingFontWarningLogged;

  void OnEnable() {
    ResolveReferences();
    DeactivateLegacyTemplate();
    RegisterHandlers();
    RefreshItems();
  }

  void OnDisable() {
    UnregisterHandlers();
    ResetSelection();
  }

  public void ShowCategory(string categoryName) {
    var resolvedCategory = ResolveCategory(categoryName);
    if (category != resolvedCategory) {
      category = resolvedCategory;
    }
    RefreshItems();
  }

  public void RefreshItems() {
    ResolveReferences();
    ResetSelection();
    gearByButton.Clear();
    displayNameByButton.Clear();
    buttons.Clear();

    var itemCount = GetItemCount();
    if (itemPrefab == null) {
      if (!missingPrefabWarningLogged) {
        missingPrefabWarningLogged = true;
        Debug.LogWarning("[ItemButtons] Inventory item prefab is missing.");
      }
      DeactivateUnusedItems(0);
      RefreshNavigation();
      return;
    }

    EnsurePoolCapacity(itemCount);
    for (var i = 0; i < itemCount; i++) {
      var itemObject = itemPool[i];
      itemObject.SetActive(false);
      itemObject.name = category.ToString().ToUpperInvariant() + "_" + i;
      PositionItem(itemObject, i);
      ConfigureItem(itemObject, i);
      SetVisualState(itemObject, isActive: false);
      buttons.Add(itemObject);
      itemObject.SetActive(true);
    }
    DeactivateUnusedItems(itemCount);
    RefreshNavigation();
  }

  protected override void HandleActiveState(GameObject button) {
    SetVisualState(button, isActive: true);
    if (button != null && gearByButton.TryGetValue(button, out var gearItem)) {
      HideTooltip();
      var icon = FindDirectChild(button.transform, "image")?.GetComponent<SpriteWithNormals>();
      itemCard?.SetupGear(gearItem, icon);
      return;
    }
    itemCard?.Hide();
    ShowTooltip(button);
  }

  protected override void HandleInactiveState(GameObject button) {
    SetVisualState(button, isActive: false);
    if (GetActiveButton() == button) {
      itemCard?.Hide();
      HideTooltip();
    }
  }

  protected override void HandleHoverState(GameObject button) {
    SetVisualState(button, isActive: true);
    if (displayNameByButton.ContainsKey(button)) {
      ShowTooltip(button);
    }
    else {
      HideTooltip();
    }
  }

  protected override void HandleUnhoverState(GameObject button) {
    SetVisualState(button, GetActiveButton() == button);
    var activeButton = GetActiveButton();
    if (activeButton != null && displayNameByButton.ContainsKey(activeButton)) {
      ShowTooltip(activeButton);
    }
    else {
      HideTooltip();
    }
  }

  void RegisterHandlers() {
    if (actions.Count == 0) {
      actions.Add(MessageBus.On(Inventory.ChangedMessage, _ => RefreshItems()));
    }
  }

  void UnregisterHandlers() {
    for (var i = 0; i < actions.Count; i++) {
      actions[i]?.Invoke();
    }
    actions.Clear();
  }

  void ResolveReferences() {
    itemsParent ??= gameObject;
    if (itemCard == null) {
      itemCard = ResolveItemCard();
    }
    if (countTextTemplate == null) {
      countTextTemplate = ResolveCountTextTemplate();
    }
  }

  ItemCard ResolveItemCard() {
    if (itemViewer != null) {
      var configuredCard = itemViewer.GetComponent<ItemCard>() ??
        itemViewer.GetComponentInChildren<ItemCard>(includeInactive: true);
      if (configuredCard != null) {
        return configuredCard;
      }
    }

    var pauseMenu = GetComponentInParent<PauseMenuInput>(includeInactive: true);
    return pauseMenu != null
      ? pauseMenu.GetComponentInChildren<ItemCard>(includeInactive: true)
      : null;
  }

  FontText ResolveCountTextTemplate() {
    var cardTemplate = FindFontTemplate(itemCard != null ? itemCard.transform : null);
    if (cardTemplate != null) {
      return cardTemplate;
    }

    var pauseMenu = GetComponentInParent<PauseMenuInput>(includeInactive: true);
    return FindFontTemplate(pauseMenu != null ? pauseMenu.transform : transform.root);
  }

  static FontText FindFontTemplate(Transform root) {
    if (root == null) {
      return null;
    }

    var texts = root.GetComponentsInChildren<FontText>(includeInactive: true);
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

  void DeactivateLegacyTemplate() {
    if (legacyTemplateResolved || itemsParent == null) {
      return;
    }

    legacyTemplateResolved = true;
    for (var i = 0; i < itemsParent.transform.childCount; i++) {
      var child = itemsParent.transform.GetChild(i);
      if (child != null && string.Equals(child.name, "item", StringComparison.OrdinalIgnoreCase)) {
        child.gameObject.SetActive(false);
      }
    }
  }

  void EnsurePoolCapacity(int requiredCount) {
    EnsureInactivePoolRoot();
    while (itemPool.Count < requiredCount) {
      var itemObject = Instantiate(itemPrefab, inactivePoolRoot, worldPositionStays: false);
      itemObject.SetActive(false);
      itemObject.transform.SetParent(itemsParent.transform, worldPositionStays: false);
      itemPool.Add(itemObject);
    }
  }

  void EnsureInactivePoolRoot() {
    if (inactivePoolRoot != null) {
      return;
    }

    var poolObject = new GameObject("InventoryItemPool");
    poolObject.transform.SetParent(itemsParent.transform, worldPositionStays: false);
    poolObject.SetActive(false);
    inactivePoolRoot = poolObject.transform;
  }

  void DeactivateUnusedItems(int usedCount) {
    for (var i = usedCount; i < itemPool.Count; i++) {
      itemPool[i].SetActive(false);
    }
  }

  void PositionItem(GameObject itemObject, int index) {
    var columns = Mathf.Max(1, columnCount);
    var column = index % columns;
    var row = index / columns;
    itemObject.transform.localPosition = new Vector3(
      firstItemLocalPosition.x + column * columnSpacing,
      firstItemLocalPosition.y - row * rowSpacing,
      itemPrefab.transform.localPosition.z
    );
    itemObject.transform.localRotation = Quaternion.identity;
    itemObject.transform.localScale = itemPrefab.transform.localScale;
  }

  int GetItemCount() {
    return category switch {
      InventoryCategory.Gear => Inventory.Gear != null ? Inventory.Gear.Count : 0,
      InventoryCategory.Gems => Inventory.Gems != null ? Inventory.Gems.Count : 0,
      InventoryCategory.Consume => Inventory.Consumables != null ? Inventory.Consumables.Count : 0,
      InventoryCategory.Quest => Inventory.Quest != null ? Inventory.Quest.Count : 0,
      _ => 0
    };
  }

  void ConfigureItem(GameObject itemObject, int index) {
    switch (category) {
      case InventoryCategory.Gear:
        ConfigureGear(itemObject, Inventory.Gear[index]);
        break;
      case InventoryCategory.Gems:
        ConfigureGem(itemObject, Inventory.Gems[index]);
        break;
      case InventoryCategory.Consume:
        ConfigureConsumable(itemObject, Inventory.Consumables[index]);
        break;
      case InventoryCategory.Quest:
        ConfigureQuestItem(itemObject, Inventory.Quest[index]);
        break;
    }
  }

  void ConfigureGear(GameObject itemObject, GearItem gearItem) {
    if (gearItem != null) {
      gearByButton[itemObject] = gearItem;
      ConfigureIcon(itemObject, ItemLibrary, gearItem.gearId, gearItem.slot);
    }
    else {
      ConfigureIcon(itemObject, null, null, null);
    }
    ConfigureCount(itemObject, 1, showCount: false);
  }

  void ConfigureGem(GameObject itemObject, GemItem gemItem) {
    ConfigureIcon(
      itemObject,
      ItemLibrary,
      gemItem != null ? ResolveGemIconId(gemItem.Type) : null,
      "Gems"
    );
    ConfigureCount(itemObject, gemItem != null ? gemItem.Amount : 0, showCount: true);
    RegisterDisplayName(itemObject, ResolveGemDisplayName(gemItem?.Type));
  }

  void ConfigureConsumable(GameObject itemObject, ConsumableItem consumable) {
    ConfigureIcon(
      itemObject,
      FirstValue(consumable?.IconLibrary, ItemLibrary),
      FirstValue(consumable?.IconId, consumable?.Type),
      consumable?.IconCategory
    );
    ConfigureCount(itemObject, consumable != null ? consumable.Amount : 0, showCount: true);
    RegisterDisplayName(itemObject, ResolveDisplayName(consumable?.Name, consumable?.Type));
  }

  void ConfigureQuestItem(GameObject itemObject, QuestItem questItem) {
    ConfigureIcon(
      itemObject,
      FirstValue(questItem?.IconLibrary, ItemLibrary),
      FirstValue(questItem?.IconId, questItem?.Type),
      questItem?.IconCategory
    );
    ConfigureCount(itemObject, questItem != null ? questItem.Amount : 0, showCount: true);
    RegisterDisplayName(itemObject, ResolveDisplayName(questItem?.Name, questItem?.Type));
  }

  static void ConfigureIcon(
    GameObject itemObject,
    string libraryName,
    string labelPrefix,
    string iconCategory
  ) {
    var image = FindDirectChild(itemObject.transform, "image");
    var icon = image != null ? image.GetComponent<SpriteWithNormals>() : null;
    var renderer = image != null ? image.GetComponent<SpriteRenderer>() : null;
    var hasIcon = icon != null &&
      !string.IsNullOrWhiteSpace(labelPrefix) &&
      !string.IsNullOrWhiteSpace(iconCategory);

    if (renderer != null) {
      renderer.enabled = hasIcon;
      renderer.color = Color.white;
      renderer.flipX = false;
      renderer.flipY = false;
    }
    if (icon == null) {
      return;
    }

    icon.SetDoNotRender(!hasIcon);
    if (!hasIcon) {
      return;
    }

    icon.SetLibraryName(FirstValue(libraryName, ItemLibrary));
    icon.SetLabelPrefix(labelPrefix.Trim());
    icon.SetAnimation(iconCategory.Trim());
    icon.SetIsAnimation(false);
    icon.ForceUpdateSpriteAndNormal(0);
  }

  void ConfigureCount(GameObject itemObject, int amount, bool showCount) {
    var countTransform = FindDirectChild(itemObject.transform, "Count");
    if (!showCount) {
      countTransform?.gameObject.SetActive(false);
      return;
    }

    var countText = countTransform != null ? countTransform.GetComponent<FontText>() : null;
    countText ??= CreateCountText(itemObject);
    if (countText == null) {
      return;
    }

    countText.gameObject.SetActive(true);
    countText.content = IntegerTextCache.Get(Mathf.Max(0, amount));
    countText.Generate();
  }

  FontText CreateCountText(GameObject itemObject) {
    countTextTemplate ??= ResolveCountTextTemplate();
    if (countTextTemplate == null || countTextTemplate.characterPrefab == null) {
      if (!missingFontWarningLogged) {
        missingFontWarningLogged = true;
        Debug.LogWarning("[ItemButtons] No FontText character prefab was available for inventory text.");
      }
      return null;
    }

    var countObject = new GameObject("Count");
    countObject.layer = itemObject.layer;
    countObject.transform.SetParent(itemObject.transform, worldPositionStays: false);

    var image = FindDirectChild(itemObject.transform, "image");
    var imagePosition = image != null ? image.localPosition : Vector3.zero;
    countObject.transform.localPosition = imagePosition + new Vector3(
      countOffsetFromIcon.x,
      countOffsetFromIcon.y,
      -0.01f
    );
    countObject.transform.localScale = Vector3.one * countTextScale;

    var hostRenderer = countObject.AddComponent<SpriteRenderer>();
    var iconRenderer = image != null ? image.GetComponent<SpriteRenderer>() : null;
    if (iconRenderer != null) {
      hostRenderer.sortingLayerID = iconRenderer.sortingLayerID;
      hostRenderer.sortingOrder = iconRenderer.sortingOrder + 1;
      hostRenderer.maskInteraction = iconRenderer.maskInteraction;
    }

    var countText = countObject.AddComponent<FontText>();
    countText.characterPrefab = countTextTemplate.characterPrefab;
    countText.font = "Plate";
    countText.spaceWidth = countTextTemplate.spaceWidth;
    countText.padding = countTextTemplate.padding;
    countText.justifyX = "right";
    countText.justifyY = "bottom";
    return countText;
  }

  void RegisterDisplayName(GameObject itemObject, string displayName) {
    if (itemObject != null && !string.IsNullOrWhiteSpace(displayName)) {
      displayNameByButton[itemObject] = displayName;
    }
  }

  void ShowTooltip(GameObject itemObject) {
    if (itemObject == null ||
        !displayNameByButton.TryGetValue(itemObject, out var displayName) ||
        string.IsNullOrWhiteSpace(displayName)) {
      HideTooltip();
      return;
    }

    tooltipText ??= CreateTooltipText(itemObject);
    if (tooltipText == null) {
      return;
    }

    var image = FindDirectChild(itemObject.transform, "image");
    var anchorWorldPosition = image != null ? image.position : itemObject.transform.position;
    var anchorLocalPosition = itemsParent.transform.InverseTransformPoint(anchorWorldPosition);
    tooltipText.transform.localPosition = anchorLocalPosition + new Vector3(
      tooltipOffsetFromItem.x,
      tooltipOffsetFromItem.y,
      -0.02f
    );

    var hostRenderer = tooltipText.GetComponent<SpriteRenderer>();
    var iconRenderer = image != null ? image.GetComponent<SpriteRenderer>() : null;
    if (hostRenderer != null && iconRenderer != null) {
      hostRenderer.sortingLayerID = iconRenderer.sortingLayerID;
      hostRenderer.sortingOrder = iconRenderer.sortingOrder + 2;
      hostRenderer.maskInteraction = iconRenderer.maskInteraction;
    }

    var contentChanged = !string.Equals(tooltipText.content, displayName, StringComparison.Ordinal);
    tooltipText.content = displayName;
    if (!tooltipText.gameObject.activeSelf) {
      tooltipText.gameObject.SetActive(true);
    }
    else if (contentChanged) {
      tooltipText.Generate();
    }
  }

  FontText CreateTooltipText(GameObject itemObject) {
    countTextTemplate ??= ResolveCountTextTemplate();
    if (countTextTemplate == null || countTextTemplate.characterPrefab == null) {
      if (!missingFontWarningLogged) {
        missingFontWarningLogged = true;
        Debug.LogWarning("[ItemButtons] No FontText character prefab was available for inventory text.");
      }
      return null;
    }

    var tooltipObject = new GameObject("ItemTooltip");
    tooltipObject.SetActive(false);
    tooltipObject.layer = itemObject.layer;
    tooltipObject.transform.SetParent(itemsParent.transform, worldPositionStays: false);
    tooltipObject.transform.localScale = Vector3.one * tooltipTextScale;

    tooltipObject.AddComponent<SpriteRenderer>();
    var text = tooltipObject.AddComponent<FontText>();
    text.characterPrefab = countTextTemplate.characterPrefab;
    text.font = "Plate";
    text.spaceWidth = countTextTemplate.spaceWidth;
    text.padding = countTextTemplate.padding;
    text.maxWidth = countTextTemplate.maxWidth;
    text.justifyX = "center";
    text.justifyY = "bottom";
    return text;
  }

  void HideTooltip() {
    if (tooltipText != null) {
      tooltipText.gameObject.SetActive(false);
    }
  }

  void ResetSelection() {
    SetHoverButton(null);
    SetActiveButton(null);
    hoverIndex = -1;
    activeIndex = -1;
    itemCard?.Hide();
    HideTooltip();
  }

  void RefreshNavigation() {
    GetComponentInParent<PauseMenuCharacterButtonsInput>(includeInactive: true)?.RefreshButtons();
  }

  static void SetVisualState(GameObject button, bool isActive) {
    if (button == null) {
      return;
    }

    var activeVisual = FindDirectChild(button.transform, "active");
    var inactiveVisual = FindDirectChild(button.transform, "inactive");
    if (activeVisual != null) {
      activeVisual.gameObject.SetActive(isActive);
    }
    if (inactiveVisual != null) {
      inactiveVisual.gameObject.SetActive(!isActive);
    }
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

  static InventoryCategory ResolveCategory(string categoryName) {
    if (string.Equals(categoryName, "GEMS", StringComparison.OrdinalIgnoreCase)) {
      return InventoryCategory.Gems;
    }
    if (string.Equals(categoryName, "CONSUME", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(categoryName, "CONSUMABLES", StringComparison.OrdinalIgnoreCase)) {
      return InventoryCategory.Consume;
    }
    if (string.Equals(categoryName, "QUEST", StringComparison.OrdinalIgnoreCase)) {
      return InventoryCategory.Quest;
    }
    return InventoryCategory.Gear;
  }

  static string ResolveGemIconId(string gemType) {
    if (string.Equals(gemType, "Base", StringComparison.OrdinalIgnoreCase)) return "amber";
    if (string.Equals(gemType, "Aqua", StringComparison.OrdinalIgnoreCase)) return "sapphire";
    if (string.Equals(gemType, "Bolt", StringComparison.OrdinalIgnoreCase)) return "emerald";
    if (string.Equals(gemType, "Cold", StringComparison.OrdinalIgnoreCase)) return "opal";
    if (string.Equals(gemType, "Dark", StringComparison.OrdinalIgnoreCase)) return "amethyst";
    if (string.Equals(gemType, "Fire", StringComparison.OrdinalIgnoreCase)) return "ruby";
    return gemType;
  }

  static string ResolveGemDisplayName(string gemType) {
    if (string.Equals(gemType, "Base", StringComparison.OrdinalIgnoreCase)) return "Amber";
    if (string.Equals(gemType, "Aqua", StringComparison.OrdinalIgnoreCase)) return "Sapphire";
    if (string.Equals(gemType, "Bolt", StringComparison.OrdinalIgnoreCase)) return "Emerald";
    if (string.Equals(gemType, "Cold", StringComparison.OrdinalIgnoreCase)) return "Opal";
    if (string.Equals(gemType, "Dark", StringComparison.OrdinalIgnoreCase)) return "Amethyst";
    if (string.Equals(gemType, "Fire", StringComparison.OrdinalIgnoreCase)) return "Ruby";
    return string.IsNullOrWhiteSpace(gemType) ? null : gemType.Trim();
  }

  static string ResolveDisplayName(string authoredName, string fallbackType) {
    if (!string.IsNullOrWhiteSpace(authoredName)) {
      return authoredName.Trim();
    }
    return string.IsNullOrWhiteSpace(fallbackType) ? null : fallbackType.Trim();
  }

  static string FirstValue(string value, string fallback) {
    return string.IsNullOrWhiteSpace(value) ? fallback : value;
  }
}
