using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class HealthBarControl : MonoBehaviour {
  const int NumericGlyphCapacity = 4;

    public string Form;
  [SerializeField] private List<GameObject> objectsToChange = new();
  private CharacterState characterState;
  [SerializeField] private FontText healthText;
  [SerializeField] private FontText nrgText;
  private string lastLabelPrefix;
  private float lastHp = -1f;
  private float lastNrg = -1f;

  void OnValidate() {
    if (Application.isPlaying) return;
    lastLabelPrefix = Form;
  }

  void OnEnable() {
    if (Application.isPlaying) return;
    lastLabelPrefix = Form;
    RefreshSprites(lastLabelPrefix);
  }

  void Start() {
    if (characterState == null) {
      characterState = GetComponentInParent<CharacterState>();
    }
    healthText?.EnsureGlyphCapacity(NumericGlyphCapacity);
    nrgText?.EnsureGlyphCapacity(NumericGlyphCapacity);

    var activeForm = EsperanzaForms.GetActive();
    lastLabelPrefix = Form != null ? Form : activeForm;
    RefreshSprites(lastLabelPrefix);
  }

  void Update() {
    var desiredLabelPrefix = Form != null ? Form : EsperanzaForms.GetActive();
    if (desiredLabelPrefix != lastLabelPrefix) {
      lastLabelPrefix = desiredLabelPrefix;
      RefreshSprites(lastLabelPrefix);
    }

    if (healthText != null) {
      if (!AllStatValues.Esperanza.TryGetValue("HP", out var hp)) hp = 0f;
      if (Mathf.Abs(hp - lastHp) > 0.01f) {
        lastHp = hp;
        healthText.content = IntegerTextCache.Get(Mathf.RoundToInt(hp));
      }
    }

    if (nrgText != null) {
      if (!AllStatValues.Esperanza.TryGetValue("NRG", out var nrg)) nrg = 0f;
      if (Mathf.Abs(nrg - lastNrg) > 0.01f) {
        lastNrg = nrg;
        nrgText.content = IntegerTextCache.Get(Mathf.RoundToInt(nrg));
      }
    }
  }

  void RefreshSprites(string labelPrefix) {
    for (int i = 0; i < objectsToChange.Count; i++) {
      var target = objectsToChange[i];
      if (target == null) continue;
      var sprite = target.GetComponent<SpriteWithNormals>();
      if (sprite == null) continue;
      sprite.labelPrefix = labelPrefix;
      sprite.ForceUpdateSpriteAndNormal();
    }
  }
}
