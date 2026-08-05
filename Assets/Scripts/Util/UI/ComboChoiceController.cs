using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ComboChoiceController : MonoBehaviour {
  const int ColumnCount = 3;
  const float ColumnSpacing = 2.2f;
  const float RowSpacing = -1.6f;
  const string HighlightKeyword = "OUTBASE_ON";

  sealed class ChoiceEntry {
    public GameObject root;
    public string animationName;
    public AllIn1AnimatorInspector highlight;
  }

  sealed class ComboItemEntry {
    public GameObject root;
    public GameObject choicesRoot;
    public GameObject choiceTemplate;
    public FontText selectedText;
    public AbilityManager ability;
    public AllIn1AnimatorInspector highlight;
    public readonly List<ChoiceEntry> choices = new();
  }

  static ComboChoiceController focusedController;

  readonly List<Action> actions = new();
  readonly List<ComboItemEntry> items = new();
  int comboIndex;
  int focusedItemIndex = -1;
  int openItemIndex = -1;
  int focusedChoiceIndex = -1;
  int ignoreDirectionsThroughFrame = -1;
  string renderedForm;

  void OnEnable() {
    ResolveItems();
    RegisterHandlers();
    RenderActiveForm();
  }

  void OnDisable() {
    if (focusedController == this) {
      focusedController = null;
    }
    ClearHighlights();
    CloseChoices();
    UnregisterHandlers();
  }

  void RegisterHandlers() {
    if (actions.Count > 0) return;

    actions.Add(MessageBus.On("pauseMenu.hover", OnHover));
    actions.Add(MessageBus.On("pauseMenu.unhover", _ => OnUnhover()));
    actions.Add(MessageBus.On("pauseMenu.click", OnClick));
    actions.Add(MessageBus.On("pauseMenu.select", value => {
      if (InputMessageValue.IsPressed(value)) OnSelect();
    }));
    actions.Add(MessageBus.On("pauseMenu.left", value => {
      if (InputMessageValue.IsPressed(value)) MoveHorizontal(-1);
    }));
    actions.Add(MessageBus.On("pauseMenu.right", value => {
      if (InputMessageValue.IsPressed(value)) MoveHorizontal(1);
    }));
    actions.Add(MessageBus.On("pauseMenu.up", value => {
      if (InputMessageValue.IsPressed(value)) MoveVertical(-1);
    }));
    actions.Add(MessageBus.On("pauseMenu.down", value => {
      if (InputMessageValue.IsPressed(value)) MoveVertical(1);
    }));
    actions.Add(MessageBus.On(CharacterMessageTopics.FormChanged, _ => RenderActiveForm()));
    actions.Add(MessageBus.On(CharacterMessageTopics.GearReady, _ => RenderActiveForm()));
    actions.Add(MessageBus.On(CharacterMessageTopics.AbilityLoadoutChanged, OnAbilityLoadoutChanged));
  }

  void UnregisterHandlers() {
    for (var i = 0; i < actions.Count; i++) {
      actions[i]?.Invoke();
    }
    actions.Clear();
  }

  void ResolveItems() {
    comboIndex = ResolveComboIndex();
    items.Clear();
    for (var i = 0; i < transform.childCount; i++) {
      var child = transform.GetChild(i);
      var choices = FindChildRecursive(child, "Choices");
      var template = choices != null ? FindChildRecursive(choices, "choice") : null;
      if (choices == null || template == null || template.GetComponent<BoxCollider2D>() == null) {
        continue;
      }

      var selectedTextNode = FindChildRecursive(child, "text");
      var entry = new ComboItemEntry {
        root = child.gameObject,
        choicesRoot = choices.gameObject,
        choiceTemplate = template.gameObject,
        selectedText = selectedTextNode != null ? selectedTextNode.GetComponent<FontText>() : null,
        ability = child.GetComponent<AbilityManager>(),
        highlight = EnsureHighlightAnimator(FindChildRecursive(child, "bg"))
      };
      EnsureItemCollider(entry);
      items.Add(entry);
    }
  }

  void RenderActiveForm() {
    var activeForm = EsperanzaForms.GetActive();
    var abilities = EsperanzaAbilityLoadouts.GetAbilitiesView(activeForm);

    for (var i = 0; i < items.Count; i++) {
      var item = items[i];
      BuildChoices(item, abilities);

      var selectedAnimation = EsperanzaComboLoadouts.GetMove(activeForm, comboIndex, i);
      if (!ContainsAbility(abilities, selectedAnimation) && abilities.Count > 0) {
        selectedAnimation = abilities[Mathf.Min(i, abilities.Count - 1)];
        EsperanzaComboLoadouts.SetMove(activeForm, comboIndex, i, selectedAnimation);
      }
      ApplySelection(item, selectedAnimation);
    }
    renderedForm = activeForm;

    if (openItemIndex >= 0) {
      OpenChoices(Mathf.Clamp(openItemIndex, 0, items.Count - 1));
    }
  }

  void OnAbilityLoadoutChanged(string changedForm) {
    if (string.Equals(changedForm, EsperanzaForms.GetActive(), StringComparison.OrdinalIgnoreCase)) {
      RenderActiveForm();
    }
  }

  void BuildChoices(ComboItemEntry item, IReadOnlyList<string> abilities) {
    item.choices.Clear();
    var templateTransform = item.choiceTemplate.transform;
    var templatePosition = templateTransform.localPosition;
    var requiredCount = abilities != null ? abilities.Count : 0;

    for (var i = item.choicesRoot.transform.childCount - 1; i >= requiredCount; i--) {
      var extra = item.choicesRoot.transform.GetChild(i);
      extra.gameObject.SetActive(false);
    }

    for (var i = 0; i < requiredCount; i++) {
      GameObject choiceObject;
      if (i == 0) {
        choiceObject = item.choiceTemplate;
      } else if (i < item.choicesRoot.transform.childCount) {
        choiceObject = item.choicesRoot.transform.GetChild(i).gameObject;
      } else {
        choiceObject = Instantiate(item.choiceTemplate, item.choicesRoot.transform);
      }

      choiceObject.name = i == 0 ? "choice" : "choice" + (i + 1);
      choiceObject.SetActive(true);
      var column = i % ColumnCount;
      var row = i / ColumnCount;
      choiceObject.transform.localPosition = templatePosition + new Vector3(
        column * ColumnSpacing,
        row * RowSpacing,
        0f
      );

      var choiceTextNode = FindChildRecursive(choiceObject.transform, "choiceText");
      var choiceText = choiceTextNode != null ? choiceTextNode.GetComponent<FontText>() : null;
      ApplyText(choiceText, GameplayXpNotificationView.ResolveAbilityAbbreviation(abilities[i]));

      var choiceBack = FindChildRecursive(choiceObject.transform, "choiceBack");
      item.choices.Add(new ChoiceEntry {
        root = choiceObject,
        animationName = abilities[i],
        highlight = EnsureHighlightAnimator(choiceBack)
      });
    }

    item.choicesRoot.SetActive(false);
  }

  void EnsureItemCollider(ComboItemEntry item) {
    if (item == null || item.root == null || item.root.GetComponent<BoxCollider2D>() != null) {
      return;
    }

    var background = FindChildRecursive(item.root.transform, "bg");
    var renderer = background != null ? background.GetComponent<SpriteRenderer>() : null;
    if (renderer == null) return;

    var bounds = renderer.bounds;
    var min = item.root.transform.InverseTransformPoint(bounds.min);
    var max = item.root.transform.InverseTransformPoint(bounds.max);
    var collider = item.root.AddComponent<BoxCollider2D>();
    collider.offset = (min + max) * 0.5f;
    collider.size = new Vector2(Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y));
  }

  void OnHover(object payload) {
    var target = payload as GameObject;
    if (!TryResolveTarget(target, out var itemIndex, out var choiceIndex)) return;

    focusedController = this;
    SetFocusedItem(itemIndex);
    if (choiceIndex >= 0) {
      SetFocusedChoice(choiceIndex);
    }
  }

  void OnUnhover() {
    if (focusedController != this) return;
    SetFocusedChoice(-1);
    if (openItemIndex < 0) {
      SetFocusedItem(-1);
    }
  }

  void OnClick(object payload) {
    var target = payload as GameObject;
    if (!TryResolveTarget(target, out var itemIndex, out var choiceIndex)) return;

    focusedController = this;
    SetFocusedItem(itemIndex);
    if (choiceIndex >= 0) {
      SetFocusedChoice(choiceIndex);
      CommitFocusedChoice();
      return;
    }

    OpenChoices(itemIndex);
  }

  void OnSelect() {
    if (focusedController != this) return;
    if (openItemIndex >= 0) {
      CommitFocusedChoice();
    } else if (focusedItemIndex >= 0) {
      OpenChoices(focusedItemIndex);
    }
  }

  void MoveHorizontal(int direction) {
    if (focusedController != this || Time.frameCount <= ignoreDirectionsThroughFrame) return;
    if (openItemIndex >= 0) {
      MoveChoiceHorizontal(direction);
      return;
    }
    if (items.Count <= 0) return;

    var current = focusedItemIndex >= 0 ? focusedItemIndex : 0;
    SetFocusedItem((current + direction + items.Count) % items.Count);
    MessageBus.Send(SoundEffectPlayer.PlayMessage, "menu.move");
  }

  void MoveVertical(int rowDirection) {
    if (focusedController != this || Time.frameCount <= ignoreDirectionsThroughFrame) return;
    if (openItemIndex >= 0) {
      MoveChoice(rowDirection * ColumnCount);
      return;
    }

    MoveComboRow(rowDirection);
  }

  public void FocusFirstItem() {
    if (!isActiveAndEnabled || items.Count <= 0) return;

    focusedController = this;
    ignoreDirectionsThroughFrame = Time.frameCount;
    SetFocusedItem(0);
    MessageBus.Send(PauseMenuCharacterButtonsInput.TopMenuFocusChangedMessage, false);
  }

  void MoveComboRow(int direction) {
    var parent = transform.parent;
    if (parent == null) return;

    var controllers = parent.GetComponentsInChildren<ComboChoiceController>(includeInactive: false);
    var currentIndex = Array.IndexOf(controllers, this);
    var nextIndex = currentIndex + direction;
    if (nextIndex >= 0 && nextIndex < controllers.Length) {
      SetFocusedItem(-1);
      controllers[nextIndex].FocusFirstItem();
      return;
    }

    if (direction < 0) {
      SetFocusedItem(-1);
      focusedController = null;
      transform.parent
        ?.GetComponentInParent<PauseMenuAbilitiesViewController>()
        ?.FocusSwitch();
    }
  }

  void MoveChoiceHorizontal(int direction) {
    var choices = items[openItemIndex].choices;
    if (choices.Count <= 0) return;

    var current = focusedChoiceIndex >= 0 ? focusedChoiceIndex : 0;
    var column = current % ColumnCount;
    if ((direction < 0 && column == 0) ||
        (direction > 0 && (column == ColumnCount - 1 || current + 1 >= choices.Count))) {
      return;
    }

    MoveChoice(direction < 0 ? -1 : 1);
  }

  void MoveChoice(int delta) {
    var choices = items[openItemIndex].choices;
    if (choices.Count <= 0) return;

    var current = focusedChoiceIndex >= 0 ? focusedChoiceIndex : 0;
    var next = Mathf.Clamp(current + delta, 0, choices.Count - 1);
    if (next == current) return;
    SetFocusedChoice(next);
    MessageBus.Send(SoundEffectPlayer.PlayMessage, "menu.move");
  }

  void OpenChoices(int itemIndex) {
    if (itemIndex < 0 || itemIndex >= items.Count) return;
    if (openItemIndex >= 0 && openItemIndex != itemIndex) {
      items[openItemIndex].choicesRoot.SetActive(false);
    }

    focusedController = this;
    openItemIndex = itemIndex;
    SetFocusedItem(itemIndex);
    items[itemIndex].choicesRoot.SetActive(true);
    MessageBus.Send(PauseMenuCharacterButtonsInput.TopMenuFocusChangedMessage, false);

    var selectedAnimation = items[itemIndex].ability != null
      ? items[itemIndex].ability.animationName
      : null;
    var selectedIndex = IndexOfAbility(items[itemIndex].choices, selectedAnimation);
    SetFocusedChoice(selectedIndex >= 0 ? selectedIndex : 0);
    MouseManager.Instance?.RefreshHoverTarget();
  }

  void CloseChoices() {
    if (openItemIndex >= 0 && openItemIndex < items.Count) {
      items[openItemIndex].choicesRoot.SetActive(false);
    }
    SetFocusedChoice(-1);
    openItemIndex = -1;
    MouseManager.Instance?.RefreshHoverTarget();
  }

  void CommitFocusedChoice() {
    if (openItemIndex < 0 || openItemIndex >= items.Count) return;
    var item = items[openItemIndex];
    if (focusedChoiceIndex < 0 || focusedChoiceIndex >= item.choices.Count) return;

    ApplySelection(item, item.choices[focusedChoiceIndex].animationName);
    var characterState = SingleSceneManager.ResolveGameplayCharacterState();
    if (characterState != null) {
      characterState.SetComboMove(
        renderedForm,
        comboIndex,
        openItemIndex,
        item.choices[focusedChoiceIndex].animationName,
        "pause_menu"
      );
    } else {
      EsperanzaComboLoadouts.SetMove(
        renderedForm,
        comboIndex,
        openItemIndex,
        item.choices[focusedChoiceIndex].animationName
      );
    }
    MessageBus.Send(SoundEffectPlayer.PlayMessage, "menu.select");
    CloseChoices();
  }

  void ApplySelection(ComboItemEntry item, string animationName) {
    if (item == null || !EsperanzaAbilities.TryResolveAbilityAnimation(animationName, out var resolved)) {
      if (item != null && item.ability != null) {
        item.ability.animationName = "";
      }
      ApplyText(item != null ? item.selectedText : null, "-");
      return;
    }

    if (item.ability != null) {
      item.ability.animationName = resolved;
    }
    ApplyText(item.selectedText, GameplayXpNotificationView.ResolveAbilityAbbreviation(resolved));
  }

  int ResolveComboIndex() {
    var parent = transform.parent;
    if (parent == null) return 0;
    var controllers = parent.GetComponentsInChildren<ComboChoiceController>(includeInactive: true);
    var index = Array.IndexOf(controllers, this);
    return Mathf.Clamp(index, 0, EsperanzaComboLoadouts.ComboCount - 1);
  }

  void SetFocusedItem(int index) {
    if (focusedItemIndex == index) return;
    if (focusedItemIndex >= 0 && focusedItemIndex < items.Count) {
      SetHighlight(items[focusedItemIndex].highlight, false);
    }
    focusedItemIndex = index;
    if (focusedItemIndex >= 0 && focusedItemIndex < items.Count) {
      SetHighlight(items[focusedItemIndex].highlight, true);
    }
  }

  void SetFocusedChoice(int index) {
    if (openItemIndex < 0 || openItemIndex >= items.Count) {
      focusedChoiceIndex = -1;
      return;
    }

    var choices = items[openItemIndex].choices;
    if (focusedChoiceIndex >= 0 && focusedChoiceIndex < choices.Count) {
      SetHighlight(choices[focusedChoiceIndex].highlight, false);
    }
    focusedChoiceIndex = index;
    if (focusedChoiceIndex >= 0 && focusedChoiceIndex < choices.Count) {
      SetHighlight(choices[focusedChoiceIndex].highlight, true);
    }
  }

  void ClearHighlights() {
    for (var i = 0; i < items.Count; i++) {
      SetHighlight(items[i].highlight, false);
      for (var j = 0; j < items[i].choices.Count; j++) {
        SetHighlight(items[i].choices[j].highlight, false);
      }
    }
    focusedItemIndex = -1;
    focusedChoiceIndex = -1;
  }

  bool TryResolveTarget(GameObject target, out int itemIndex, out int choiceIndex) {
    itemIndex = -1;
    choiceIndex = -1;
    if (target == null) return false;

    for (var i = 0; i < items.Count; i++) {
      var item = items[i];
      for (var j = 0; j < item.choices.Count; j++) {
        if (IsTargetWithin(target, item.choices[j].root)) {
          itemIndex = i;
          choiceIndex = j;
          return true;
        }
      }
      if (IsTargetWithin(target, item.root)) {
        itemIndex = i;
        return true;
      }
    }
    return false;
  }

  static bool IsTargetWithin(GameObject target, GameObject root) {
    return target != null && root != null &&
           (target == root || target.transform.IsChildOf(root.transform));
  }

  static bool ContainsAbility(IReadOnlyList<string> abilities, string animationName) {
    if (abilities == null || string.IsNullOrWhiteSpace(animationName)) return false;
    for (var i = 0; i < abilities.Count; i++) {
      if (string.Equals(abilities[i], animationName, StringComparison.OrdinalIgnoreCase)) return true;
    }
    return false;
  }

  static int IndexOfAbility(List<ChoiceEntry> choices, string animationName) {
    for (var i = 0; i < choices.Count; i++) {
      if (string.Equals(choices[i].animationName, animationName, StringComparison.OrdinalIgnoreCase)) return i;
    }
    return -1;
  }

  static void ApplyText(FontText text, string value) {
    if (text == null || text.content == value) return;
    text.content = value;
    text.Generate();
  }

  static AllIn1AnimatorInspector EnsureHighlightAnimator(Transform visual) {
    if (visual == null) return null;
    var animator = visual.GetComponent<AllIn1AnimatorInspector>();
    return animator != null ? animator : visual.gameObject.AddComponent<AllIn1AnimatorInspector>();
  }

  static void SetHighlight(AllIn1AnimatorInspector animator, bool highlighted) {
    animator?.SetKeyword(HighlightKeyword, highlighted);
  }

  static Transform FindChildRecursive(Transform root, string targetName) {
    if (root == null || string.IsNullOrWhiteSpace(targetName)) return null;
    if (string.Equals(root.name, targetName, StringComparison.OrdinalIgnoreCase)) return root;

    for (var i = 0; i < root.childCount; i++) {
      var match = FindChildRecursive(root.GetChild(i), targetName);
      if (match != null) return match;
    }
    return null;
  }
}
