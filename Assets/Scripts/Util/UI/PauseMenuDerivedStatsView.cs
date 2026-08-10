using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public sealed class PauseMenuDerivedStatsView : MonoBehaviour {
  const float DefaultStatRowSpacing = 0.35f;

  [FormerlySerializedAs("statHolderPrefab")]
  [SerializeField] GameObject m_statHolderPrefab;
  [SerializeField] Transform statsContainer;
  [SerializeField] float statRowSpacing = DefaultStatRowSpacing;

  readonly List<Action> actions = new();
  readonly List<GameObject> spawnedHolders = new();

  CharacterState characterState;
  SpriteWithNormals[] themedSprites = Array.Empty<SpriteWithNormals>();
  string selectedForm;

  void OnEnable() {
    EnsureResolved();
    RegisterHandlers();
    Refresh("enable");
  }

  void OnDisable() {
    UnregisterHandlers();
  }

  void EnsureResolved(bool force = false) {
    if (force || characterState == null) {
      characterState = SingleSceneManager.ResolveGameplayCharacterState();
    }
    if (statsContainer == null) {
      statsContainer = transform;
    }
    if (force || themedSprites == null || themedSprites.Length == 0) {
      themedSprites = GetComponentsInChildren<SpriteWithNormals>(includeInactive: true);
    }
  }

  void RegisterHandlers() {
    if (actions.Count > 0) {
      return;
    }
    actions.Add(MessageBus.On(CharacterMessageTopics.FormChanged, form => OnFormChanged(form)));
    actions.Add(MessageBus.On(CharacterMessageTopics.FormProgressChanged, _ => Refresh("form_progress_changed")));
    actions.Add(MessageBus.On(CharacterMessageTopics.FormStatsChanged, _ => Refresh("form_stats_changed")));
  }

  void UnregisterHandlers() {
    for (var i = 0; i < actions.Count; i++) {
      actions[i]?.Invoke();
    }
    actions.Clear();
  }

  void OnFormChanged(object payload) {
    var resolvedForm = EsperanzaForms.ResolveFormKey(payload as string);
    if (string.IsNullOrWhiteSpace(resolvedForm)) {
      resolvedForm = EsperanzaForms.GetActive();
    }
    Refresh("form_changed");
  }

  public void Refresh(string source = "manual") {
    EnsureResolved();
    var activeForm = EsperanzaForms.GetActive();
    if (characterState != null) {
      characterState.GatherAllStatValues();
    }

    var minorStats = Abbreviations.structure != null &&
                     Abbreviations.structure.TryGetValue("Minor", out var list) &&
                     list != null
      ? list
      : new List<string>();

    var derivedStats = AllStatValues.Esperanza;
    int statCount = 0;
    int spawnIndex = 0;

    if (derivedStats != null && m_statHolderPrefab != null) {
      for (var i = 0; i < minorStats.Count; i++) {
        var statKey = minorStats[i];
        if (string.IsNullOrWhiteSpace(statKey)) {
          continue;
        }

        var statValStr = derivedStats.TryGetValue(statKey, out var statVal) && statVal != null
          ? statVal.ToString()
          : "0";
        var holderObj = GetOrCreateHolder(spawnIndex);
        ApplyText(FindFontText(holderObj.transform, "names"), statKey);
        ApplyText(FindFontText(holderObj.transform, "values"), statValStr);
        ApplyText(FindFontText(holderObj.transform, "description"), Abbreviations.GetDescription(statKey));

        spawnIndex++;
        statCount++;
      }
    }

    for (var i = spawnIndex; i < spawnedHolders.Count; i++) {
      if (spawnedHolders[i] != null) {
        spawnedHolders[i].SetActive(false);
      }
    }

    themedSprites = GetComponentsInChildren<SpriteWithNormals>(includeInactive: true);
    selectedForm = null;
    ApplyTheme(activeForm);

    RuntimeLog.Log(
      "[PauseMenuDerivedStatsView] Refreshed source='" + (source ?? "") +
      "' form='" + activeForm +
      "' stat_count=" + statCount
    );
  }

  GameObject GetOrCreateHolder(int index) {
    GameObject holderObj = index < spawnedHolders.Count ? spawnedHolders[index] : null;
    if (holderObj == null) {
      if (m_statHolderPrefab == null) {
#if UNITY_EDITOR
        m_statHolderPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/StatHolder.prefab");
#endif
      }
      holderObj = Instantiate(m_statHolderPrefab, statsContainer, false);
      if (index < spawnedHolders.Count) {
        spawnedHolders[index] = holderObj;
      } else {
        spawnedHolders.Add(holderObj);
      }
    }

    holderObj.SetActive(true);
    holderObj.transform.SetSiblingIndex(index);
    var holderPosition = m_statHolderPrefab.transform.localPosition;
    holderPosition.y -= index * Mathf.Max(0f, statRowSpacing);
    holderObj.transform.localPosition = holderPosition;
    return holderObj;
  }

  void ApplyTheme(string activeForm) {
    var themeName = !string.IsNullOrWhiteSpace(activeForm) ? activeForm : "Base";
    if (string.Equals(themeName, selectedForm, StringComparison.OrdinalIgnoreCase)) {
      return;
    }

    selectedForm = themeName;
    for (var i = 0; i < themedSprites.Length; i++) {
      var themedSprite = themedSprites[i];
      if (themedSprite == null) {
        continue;
      }
      if (!string.Equals(themedSprite.libraryName, "UI/CharUI", StringComparison.OrdinalIgnoreCase)) {
        continue;
      }
      if (string.IsNullOrWhiteSpace(themedSprite.category)) {
        continue;
      }

      if (!string.Equals(themedSprite.labelPrefix, themeName, StringComparison.Ordinal)) {
        themedSprite.SetLabelPrefix(themeName);
        themedSprite.ForceUpdateSpriteAndNormal();
      }
    }
  }

  static void ApplyText(FontText fontText, string value) {
    if (fontText == null) {
      return;
    }
    if (fontText.content == value) {
      return;
    }
    fontText.content = value;
    fontText.Generate();
  }

  static FontText FindFontText(Transform root, string childName) {
    var child = FindChildRecursive(root, childName);
    return child != null ? child.GetComponent<FontText>() : null;
  }

  static Transform FindChildRecursive(Transform parent, string targetName) {
    if (parent == null || string.IsNullOrWhiteSpace(targetName)) {
      return null;
    }

    var count = parent.childCount;
    for (var i = 0; i < count; i++) {
      var child = parent.GetChild(i);
      if (string.Equals(child.name, targetName, StringComparison.OrdinalIgnoreCase)) {
        return child;
      }
    }

    for (var i = 0; i < count; i++) {
      var nested = FindChildRecursive(parent.GetChild(i), targetName);
      if (nested != null) {
        return nested;
      }
    }

    return null;
  }
}
