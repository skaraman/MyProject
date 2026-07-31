using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(AllIn1AnimatorInspector))]
public sealed class RingSlotHoverOutline : MonoBehaviour {
  const string OutlineKeyword = "OUTBASE_ON";
  const string OnlyOutlineKeyword = "ONLYOUTLINE_ON";

  SpriteRenderer targetRenderer;
  AllIn1AnimatorInspector targetAnimator;

  void Awake() {
    ResolveComponents();
    SetHighlighted(false);
  }

  public void SetHighlighted(bool isHighlighted) {
    ResolveComponents();
    if (targetRenderer == null || targetAnimator == null) {
      return;
    }

    targetAnimator.SetKeyword(OnlyOutlineKeyword, true);
    targetAnimator.SetKeyword(OutlineKeyword, isHighlighted);
    targetRenderer.enabled = isHighlighted;
  }

  void ResolveComponents() {
    targetRenderer ??= GetComponent<SpriteRenderer>();
    targetAnimator ??= GetComponent<AllIn1AnimatorInspector>();
  }
}
