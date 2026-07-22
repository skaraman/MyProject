
using UnityEngine;

public class SettingsButtons : ButtonGroup {
  const string ActiveOutlineKeyword = "OUTBASE_ON";

  protected override void HandleActiveState(GameObject button) {
    ButtonShaderKeywords.ApplyToButton(button, ActiveOutlineKeyword, true);
  }

  protected override void HandleInactiveState(GameObject button) {
    ButtonShaderKeywords.ApplyToButton(button, ActiveOutlineKeyword, false);
  }
}
