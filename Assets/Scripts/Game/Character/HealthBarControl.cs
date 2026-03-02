using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class HealthBarControl : MonoBehaviour {
    public string Form;
  [SerializeField] private List<GameObject> objectsToChange = new();
  [SerializeField] private CharacterState characterState;
  [SerializeField] private FontText healthText;
  [SerializeField] private FontText nrgText;
  private string lastLabelPrefix;

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
      healthText.content = hp.ToString("0");
    }

    if (nrgText != null) {
      if (!AllStatValues.Esperanza.TryGetValue("NRG", out var nrg)) nrg = 0f;
      nrgText.content = nrg.ToString("0");
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
