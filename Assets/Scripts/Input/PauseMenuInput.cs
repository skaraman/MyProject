using System;
using System.Collections.Generic;
using UnityEngine;

public class PauseMenuInput : MonoBehaviour {
  private int activeHoverIndex = -1;
  private int activeSelectedIndex = 2;
  private int formHoverIndex = -1;
  private readonly List<Action> actions = new();
  private bool sectionsInitialized;
  private bool hasLoggedFormsListVisibility;
  private bool lastFormsListVisible;
  private bool sceneLightsWereActiveBeforePause;
  private bool pauseMenuManagedSceneLights;
  private bool topMenuFocused = true;
  private CharacterState characterState;

  public MenuButtons menuButtons;
  public FormButtons formButtons;
  // public ButtonGroup StatsButtons;
  public GearButtons gearButtons;
  // public ButtonGroup AbilityButtons;
  // public ButtonGroup InventoryItems;

  public GameObject MapMenu;
  public GameObject CharacterMenu;
  public GameObject AbilityMenu;
  public GameObject InventoryMenu;
  public GameObject OptionsMenu;
  public GameObject FormsList;
  public GameObject SceneLights;
  public List<GameObject> sections = new();
  public List<GameObject> changingUI = new();
  public List<GameObject> primaryUIText = new();
  public List<GameObject> secondaryUIText = new();

  void Awake() {
    EnsureCharacterButtonNavigation();
  }

  void OnEnable() {
    topMenuFocused = true;
    EnsureCharacterButtonNavigation();
    EnsureSectionsInitialized();
    SyncSelectedMenuWithVisibleSection();
    ResolveFormsListReference();
    ResolveCharacterState();
    ApplyPauseLightingState(isPaused: true);
    RefreshFormsListVisibility();
    RefreshCurrentFormUi();
  }

  void OnDisable() {
    ApplyPauseLightingState(isPaused: false);
  }

  void Start() {
    EnsureSectionsInitialized();
    SyncSelectedMenuWithVisibleSection();
    ResolveFormsListReference();
    ResolveCharacterState();
    RefreshFormsListVisibility();
    RefreshCurrentFormUi(logSelection: true, source: "start");

    if (actions.Count > 0) return;
    actions.Add(MessageBus.On("pauseMenu.LeftTab", o => { if (InputMessageValue.IsPressed(o)) menuLeft(); }));
    actions.Add(MessageBus.On("pauseMenu.RightTab", o => { if (InputMessageValue.IsPressed(o)) menuRight(); }));
    actions.Add(MessageBus.On("pauseMenu.select", o => { if (InputMessageValue.IsPressed(o)) select(); }));
    actions.Add(MessageBus.On("pauseMenu.hover", o => hover(o)));
    actions.Add(MessageBus.On("pauseMenu.unhover", o => unhover()));
    actions.Add(MessageBus.On("pauseMenu.click", o => click(o)));
    actions.Add(MessageBus.On("pauseMenu.cancel", o => { if (InputMessageValue.IsPressed(o)) cancel(); }));
    actions.Add(MessageBus.On("pauseMenu.left", o => { if (InputMessageValue.IsPressed(o)) left(); }));
    actions.Add(MessageBus.On("pauseMenu.right", o => { if (InputMessageValue.IsPressed(o)) right(); }));
    actions.Add(MessageBus.On("pauseMenu.up", o => { if (InputMessageValue.IsPressed(o)) up(); }));
    actions.Add(MessageBus.On("pauseMenu.down", o => { if (InputMessageValue.IsPressed(o)) down(); }));
    actions.Add(MessageBus.On(
      PauseMenuCharacterButtonsInput.TopMenuFocusChangedMessage,
      OnCharacterTopMenuFocusChanged
    ));
    actions.Add(MessageBus.On(CharacterMessageTopics.FormChanged, form => OnFormChanged(form)));
  }

  void OnDestroy() {
    for (int i = 0; i < actions.Count; i++) {
      actions[i].Invoke();
    }
    actions.Clear();
  }

  void EnsureSectionsInitialized() {
    if (sectionsInitialized) return;

    sections.Clear();
    sections.AddRange(new GameObject[] { MapMenu, CharacterMenu, AbilityMenu, InventoryMenu, OptionsMenu });
    sectionsInitialized = true;
  }

  void EnsureCharacterButtonNavigation() {
    if (CharacterMenu == null) {
      return;
    }

    if (CharacterMenu.GetComponent<PauseMenuCharacterButtonsInput>() == null) {
      CharacterMenu.AddComponent<PauseMenuCharacterButtonsInput>();
    }
  }

  void SyncSelectedMenuWithVisibleSection() {
    for (int i = 0; i < sections.Count; i++) {
      if (sections[i] == null || !sections[i].activeSelf) continue;
      activeSelectedIndex = i + 1;
      if (menuButtons != null && menuButtons.activeIndex != activeSelectedIndex) {
        menuButtons.SetActiveIndex(activeSelectedIndex);
      }
      return;
    }
  }

  GameObject ResolveFormsListReference() {
    if (FormsList != null) return FormsList;
    if (formButtons != null) {
      FormsList = formButtons.gameObject;
    }
    if (FormsList != null) return FormsList;

    var formsTransform = transform.Find("Forms");
    if (formsTransform != null) {
      FormsList = formsTransform.gameObject;
    }

    return FormsList;
  }

  GameObject ResolveSceneLightsReference() {
    return SceneLights;
  }

  CharacterState ResolveCharacterState() {
    if (characterState == null) {
      characterState = SingleSceneManager.ResolveGameplayCharacterState();
    }
    return characterState;
  }

  void ApplyPauseLightingState(bool isPaused) {
    var sceneLights = ResolveSceneLightsReference();
    if (sceneLights == null) {
      if (isPaused) {
        Debug.LogWarning("[PauseMenuInput] SceneLights reference is missing while opening pause menu.");
      }
      pauseMenuManagedSceneLights = false;
      return;
    }

    if (isPaused) {
      sceneLightsWereActiveBeforePause = sceneLights.activeSelf;
      pauseMenuManagedSceneLights = true;
      if (sceneLightsWereActiveBeforePause) {
        sceneLights.SetActive(false);
      }
      RuntimeLog.Log(
        "[PauseMenuInput] Pause lighting applied previous_active=" + (sceneLightsWereActiveBeforePause ? 1 : 0) +
        " current_active=" + (sceneLights.activeSelf ? 1 : 0)
      );
      return;
    }

    if (!pauseMenuManagedSceneLights) return;

    pauseMenuManagedSceneLights = false;
    if (sceneLights.activeSelf != sceneLightsWereActiveBeforePause) {
      sceneLights.SetActive(sceneLightsWereActiveBeforePause);
    }
    RuntimeLog.Log(
      "[PauseMenuInput] Pause lighting restored restored_active=" + (sceneLightsWereActiveBeforePause ? 1 : 0) +
      " current_active=" + (sceneLights.activeSelf ? 1 : 0)
    );
  }

  bool IsSectionIndex(int index) {
    return index > 0 && index <= sections.Count;
  }

  bool CanShowFormsList() {
    return (CharacterMenu != null && CharacterMenu.activeSelf) ||
           (AbilityMenu != null && AbilityMenu.activeSelf);
  }

  bool CanInteractWithFormsList() {
    var formsList = ResolveFormsListReference();
    return formButtons != null && formsList != null && formsList.activeSelf;
  }

  void RefreshFormsListVisibility(bool logVisibilityChange = false) {
    var formsList = ResolveFormsListReference();
    var shouldBeActive = CanShowFormsList();

    if (formsList != null && formsList.activeSelf != shouldBeActive) {
      formsList.SetActive(shouldBeActive);
    }

    if (!shouldBeActive) {
      formHoverIndex = -1;
      if (formButtons != null && formButtons.hoverIndex != -1) {
        formButtons.SetHoverIndex(-1);
      }
    }

    if (formsList == null) {
      if (logVisibilityChange) {
        Debug.LogWarning("[PauseMenuInput] Forms list reference is missing.");
      }
      return;
    }

    if (logVisibilityChange || !hasLoggedFormsListVisibility || lastFormsListVisible != shouldBeActive) {
      RuntimeLog.Log(
        "[PauseMenuInput] Forms list visibility active=" + (shouldBeActive ? 1 : 0) +
        " character_menu=" + ((CharacterMenu != null && CharacterMenu.activeSelf) ? 1 : 0) +
        " ability_menu=" + ((AbilityMenu != null && AbilityMenu.activeSelf) ? 1 : 0)
      );
      hasLoggedFormsListVisibility = true;
      lastFormsListVisible = shouldBeActive;
    }
  }

  void menuLeft() {
    FocusTopMenu();
    MoveTopMenuSelection(-1);
  }

  void menuRight() {
    FocusTopMenu();
    MoveTopMenuSelection(1);
  }

  void FocusTopMenu() {
    topMenuFocused = true;
    if (CharacterMenu == null) {
      return;
    }

    CharacterMenu.GetComponent<PauseMenuCharacterButtonsInput>()?.FocusTopMenu();
  }

  void OnCharacterTopMenuFocusChanged(object payload) {
    if (payload is bool isFocused) {
      topMenuFocused = isFocused;
    }
  }

  void MoveTopMenuSelection(int direction) {
    EnsureSectionsInitialized();
    if (menuButtons == null) return;

    // TopMenu reserves index 0 and its last index for the left/right arrow buttons.
    var tabCount = Mathf.Min(sections.Count, Mathf.Max(0, menuButtons.buttons.Count - 2));
    if (tabCount <= 0) return;

    var currentIndex = activeSelectedIndex;
    if (currentIndex < 1 || currentIndex > tabCount) {
      currentIndex = menuButtons.activeIndex;
    }
    if (currentIndex < 1 || currentIndex > tabCount) {
      currentIndex = 1;
    }

    var nextIndex = currentIndex + (direction < 0 ? -1 : 1);
    if (nextIndex < 1) {
      nextIndex = tabCount;
    }
    else if (nextIndex > tabCount) {
      nextIndex = 1;
    }

    SelectMenuIndex(nextIndex);
  }

  void select() {
    if (CharacterMenu != null && CharacterMenu.activeSelf) {
      var characterButtonsInput = CharacterMenu.GetComponent<PauseMenuCharacterButtonsInput>();
      if (characterButtonsInput != null && characterButtonsInput.HasFocusedButton) {
        return;
      }
    }

    if (activeHoverIndex != -1) {
      SelectMenuIndex(activeHoverIndex);
      return;
    }
    if (formHoverIndex != -1) {
      SelectFormIndex(formHoverIndex);
    }
  }

  void hover(object target) {
    var targetObject = target as GameObject;

    activeHoverIndex = ResolveButtonIndex(menuButtons, targetObject);
    if (activeHoverIndex >= 0) {
      if (menuButtons.hoverIndex != activeHoverIndex) {
        menuButtons.SetHoverIndex(activeHoverIndex);
      }
    }
    else if (menuButtons != null && menuButtons.hoverIndex != -1) {
      menuButtons.SetHoverIndex(-1);
    }

    if (!CanInteractWithFormsList()) {
      formHoverIndex = -1;
      if (formButtons != null && formButtons.hoverIndex != -1) {
        formButtons.SetHoverIndex(-1);
      }
      return;
    }

    formHoverIndex = ResolveButtonIndex(formButtons, targetObject);
    if (formHoverIndex >= 0) {
      CharacterMenu
        ?.GetComponent<PauseMenuCharacterButtonsInput>()
        ?.FocusTopMenu();
      if (formButtons.hoverIndex != formHoverIndex) {
        formButtons.SetHoverIndex(formHoverIndex);
      }
    }
    else if (formButtons.hoverIndex != -1) {
      formButtons.SetHoverIndex(-1);
    }
  }

  void unhover() {
    activeHoverIndex = -1;
    formHoverIndex = -1;
    if (menuButtons != null) {
      menuButtons.SetHoverIndex(-1);
    }
    if (formButtons != null) {
      formButtons.SetHoverIndex(-1);
    }
  }

  void click(object target) {
    var targetObject = target as GameObject;
    if (targetObject == null) return;

    activeHoverIndex = ResolveButtonIndex(menuButtons, targetObject);
    if (activeHoverIndex >= 0) {
      SelectMenuIndex(activeHoverIndex);
      return;
    }

    if (!CanInteractWithFormsList()) return;

    formHoverIndex = ResolveButtonIndex(formButtons, targetObject);
    if (formHoverIndex >= 0) {
      SelectFormIndex(formHoverIndex);
    }
  }

  void SelectMenuIndex(int index) {
    if (menuButtons == null) return;
    if (index == 0) {
      menuLeft();
      return;
    }
    if (index == menuButtons.buttons.Count - 1) {
      menuRight();
      return;
    }
    if (!IsSectionIndex(index)) return;

    activeSelectedIndex = index;
    if (menuButtons.activeIndex != activeSelectedIndex) {
      menuButtons.SetActiveIndex(activeSelectedIndex);
    }
    SetVisibleSection(activeSelectedIndex);
    FocusTopMenu();
    RefreshFormsListVisibility(logVisibilityChange: true);
    RefreshCurrentFormUi(source: "menu_select");
  }

  void SetVisibleSection(int selectedIndex) {
    for (int i = 0; i < sections.Count; i++) {
      var section = sections[i];
      if (section == null) continue;
      section.SetActive(i == selectedIndex - 1);
    }
  }

  void SelectFormIndex(int index) {
    if (!CanInteractWithFormsList()) return;
    if (formButtons == null || index < 0 || index >= formButtons.buttons.Count) return;

    var button = formButtons.buttons[index];
    if (!IsUnlockedFormButton(button)) {
      RuntimeLog.Log("[PauseMenuInput] Ignored locked form button='" + (button != null ? button.name : "-") + "'");
      return;
    }

    var state = ResolveCharacterState();
    if (state == null) {
      Debug.LogWarning("[PauseMenuInput] CharacterState was not found for form selection.");
      return;
    }

    state.SetActiveForm(button.name, "pause_menu");
  }

  bool IsUnlockedFormButton(GameObject button) {
    if (button == null) return false;
    return EsperanzaForms.IsUnlocked(button.name);
  }

  void RefreshCurrentFormUi(bool logSelection = false, string source = "sync") {
    var activeForm = EsperanzaForms.GetActive();
    if (string.IsNullOrWhiteSpace(activeForm)) return;
    ApplyFormVisualState(activeForm, syncButtonIndex: true, logSelection: logSelection, source: source);
  }

  void ApplyFormVisualState(string formName, bool syncButtonIndex, bool logSelection, string source) {
    var resolvedForm = ResolveFormName(formName);
    if (string.IsNullOrWhiteSpace(resolvedForm)) {
      Debug.LogWarning("[PauseMenuInput] Unable to resolve form selection='" + (formName ?? "") + "'");
      return;
    }

    if (formButtons != null) {
      formButtons.RefreshUnlockedVisuals();
    }

    if (syncButtonIndex) {
      SyncActiveFormButton(resolvedForm);
    }

    ApplyChangingUiLabels(resolvedForm);
    var primaryColorName = ApplyTextColorGroup(
      primaryUIText,
      resolvedForm,
      ShaderColors.PrimaryGroup,
      out var primaryOutlineColorName
    );
    var secondaryColorName = ApplyTextColorGroup(
      secondaryUIText,
      resolvedForm,
      ShaderColors.SecondaryGroup,
      out var secondaryOutlineColorName
    );

    if (gearButtons != null) {
      gearButtons.OnGearReady(resolvedForm);
    }

    if (logSelection) {
      RuntimeLog.Log(
        "[PauseMenuInput] Applied form='" + resolvedForm +
        "' primary_color='" + (string.IsNullOrWhiteSpace(primaryColorName) ? "-" : primaryColorName) +
        "' primary_outline='" + (string.IsNullOrWhiteSpace(primaryOutlineColorName) ? "-" : primaryOutlineColorName) +
        "' secondary_color='" + (string.IsNullOrWhiteSpace(secondaryColorName) ? "-" : secondaryColorName) +
        "' secondary_outline='" + (string.IsNullOrWhiteSpace(secondaryOutlineColorName) ? "-" : secondaryOutlineColorName) +
        "' source='" + (source ?? "") + "'"
      );
    }
  }

  string ResolveFormName(string formName) {
    return EsperanzaForms.ResolveFormKey(formName);
  }

  void SyncActiveFormButton(string formName) {
    if (formButtons == null) return;

    for (int i = 0; i < formButtons.buttons.Count; i++) {
      var button = formButtons.buttons[i];
      if (button == null) continue;
      if (!string.Equals(button.name, formName, StringComparison.OrdinalIgnoreCase)) continue;
      if (formButtons.activeIndex != i) {
        formButtons.SetActiveIndex(i);
      }
      return;
    }
  }

  void OnFormChanged(object payload) {
    var requestedForm = payload as string;
    var resolvedForm = ResolveFormName(requestedForm);
    if (string.IsNullOrWhiteSpace(resolvedForm)) {
      resolvedForm = EsperanzaForms.GetActive();
    }
    ApplyFormVisualState(resolvedForm, syncButtonIndex: true, logSelection: true, source: "form_changed");
  }

  void ApplyChangingUiLabels(string formName) {
    for (int i = 0; i < changingUI.Count; i++) {
      var target = changingUI[i];
      if (target == null) continue;

      var spriteWithNormals = target.GetComponent<SpriteWithNormals>();
      if (spriteWithNormals == null) continue;

      spriteWithNormals.SetLabelPrefix(formName);
      spriteWithNormals.ForceUpdateSpriteAndNormal();
    }
  }

  string ApplyTextColorGroup(
    List<GameObject> targets,
    string formName,
    string groupName,
    out string outlineColorName
  ) {
    outlineColorName = null;
    if (!ShaderColors.TryGetFormPalette(
          formName,
          groupName,
          out var color,
          out var outlineColor,
          out var colorName,
          out outlineColorName
        )) {
      return null;
    }

    for (int i = 0; i < targets.Count; i++) {
      ApplyFontTextColors(targets[i], color, outlineColor);
    }

    return colorName;
  }

  void ApplyFontTextColors(GameObject target, Color color, Color outlineColor) {
    if (target == null) return;

    var fontText = target.GetComponent<FontText>();
    if (fontText != null) {
      fontText.SetShaderColors(color, outlineColor);
      return;
    }

    var animator = target.GetComponent<AllIn1AnimatorInspector>();
    if (animator == null) return;

    animator.AddColorSequence("_Color", color, color, 1f, replaceExisting: true);
    animator.AddColorSequence("_OutlineColor", outlineColor, outlineColor, 1f, replaceExisting: true);
    animator.Refresh();
  }

  static int ResolveButtonIndex(ButtonGroup buttonGroup, GameObject target) {
    if (buttonGroup == null || target == null) {
      return -1;
    }

    var directIndex = buttonGroup.buttons.IndexOf(target);
    if (directIndex >= 0) {
      return directIndex;
    }

    var targetTransform = target.transform;
    for (var i = 0; i < buttonGroup.buttons.Count; i++) {
      var button = buttonGroup.buttons[i];
      if (button == null) {
        continue;
      }
      if (targetTransform.IsChildOf(button.transform)) {
        return i;
      }
    }

    return -1;
  }

  void cancel() {
    MessageBus.Send("closePauseMenu", null);
  }

  void left() {
    if (topMenuFocused) {
      MoveTopMenuSelection(-1);
    }
  }

  void right() {
    if (topMenuFocused) {
      MoveTopMenuSelection(1);
    }
  }

  void up() { }

  void down() {
    if (!topMenuFocused) {
      return;
    }

    if (CharacterMenu != null && CharacterMenu.activeSelf) {
      CharacterMenu.GetComponent<PauseMenuCharacterButtonsInput>()?.FocusTopmostButton();
      return;
    }

    if (AbilityMenu != null && AbilityMenu.activeSelf) {
      AbilityMenu.GetComponent<PauseMenuAbilitiesViewController>()?.FocusSwitch();
    }
  }

}
