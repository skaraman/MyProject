using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class AbilityMenuManager : MonoBehaviour {
  const int ColumnCount = 2;
  const float ColumnSpacing = 11.62f;
  const float RowSpacing = -2.58f;

  readonly List<Action> actions = new();
  readonly List<AbilityManager> cards = new();

  void OnEnable() {
    RegisterHandlers();
    ResolveCards();
    EnsureCardCount(EsperanzaAbilityLoadouts.MaximumAbilitiesPerForm);
    RenderActiveForm();
  }

  void OnDisable() {
    UnregisterHandlers();
  }

  void RegisterHandlers() {
    if (actions.Count > 0) {
      return;
    }

    actions.Add(MessageBus.On(CharacterMessageTopics.FormChanged, _ => RenderActiveForm()));
    actions.Add(MessageBus.On(CharacterMessageTopics.GearReady, _ => RenderActiveForm()));
    actions.Add(MessageBus.On(CharacterMessageTopics.AbilityLoadoutChanged, HandleLoadoutChanged));
  }

  void UnregisterHandlers() {
    for (var i = 0; i < actions.Count; i++) {
      actions[i]?.Invoke();
    }
    actions.Clear();
  }

  void HandleLoadoutChanged(string formName) {
    var activeForm = EsperanzaForms.GetActive();
    if (!string.Equals(formName, activeForm, StringComparison.OrdinalIgnoreCase)) {
      return;
    }

    RenderActiveForm();
  }

  public void RenderActiveForm() {
    var activeForm = EsperanzaForms.GetActive();
    var abilities = EsperanzaAbilityLoadouts.GetAbilitiesView(activeForm);
    Render(activeForm, abilities);
  }

  void Render(string formName, IReadOnlyList<string> abilities) {
    ResolveCards();
    var abilityCount = abilities != null
      ? Mathf.Min(abilities.Count, EsperanzaAbilityLoadouts.MaximumAbilitiesPerForm)
      : 0;
    EnsureCardCount(abilityCount);

    for (var i = 0; i < cards.Count; i++) {
      var card = cards[i];
      if (card == null) {
        continue;
      }

      var shouldShow = i < abilityCount;
      if (!shouldShow) {
        if (card.gameObject.activeSelf) {
          card.gameObject.SetActive(false);
        }
        continue;
      }

      ApplyGridPosition(card.transform, i);
      card.SetAbility(abilities[i], formName);
      if (!card.gameObject.activeSelf) {
        card.gameObject.SetActive(true);
      }
    }
  }

  void ResolveCards() {
    cards.Clear();
    for (var i = 0; i < transform.childCount; i++) {
      var child = transform.GetChild(i);
      var card = child.GetComponent<AbilityManager>();
      if (card != null) {
        cards.Add(card);
      }
    }
  }

  void EnsureCardCount(int requiredCount) {
    if (requiredCount <= cards.Count || cards.Count == 0) {
      return;
    }

    var template = cards[0];
    while (cards.Count < requiredCount) {
      var cardObject = Instantiate(template.gameObject, transform);
      var card = cardObject.GetComponent<AbilityManager>();
      if (card == null) {
        Destroy(cardObject);
        return;
      }

      cardObject.name = "Ability " + (cards.Count + 1);
      cards.Add(card);
    }
  }

  static void ApplyGridPosition(Transform cardTransform, int index) {
    var column = index % ColumnCount;
    var row = index / ColumnCount;
    cardTransform.localPosition = new Vector3(
      column * ColumnSpacing,
      row * RowSpacing,
      0f
    );
  }
}
