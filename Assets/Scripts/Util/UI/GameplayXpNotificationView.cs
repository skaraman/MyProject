using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class GameplayXpNotificationView : MonoBehaviour {
  const int MaximumAbilityLanes = 6;
  const string DirectFormUiRoot = "FormUIs/";
  const string DirectFormUiSuffix = "UI";
  const string CharUiCategory = "CharUI";
  const string XpBarBackLabel = "XPBarBack";
  const string XpBarFillLabel = "XPBarFill";

  sealed class Lane {
    public Transform root;
    public SpriteWithNormals backSprite;
    public SpriteWithNormals fillSprite;
    public AnchoredSpriteStretch fill;
    public FontText label;
    public SpriteRenderer[] renderers = Array.Empty<SpriteRenderer>();
    public Vector3 hiddenPosition;
    public Vector3 shownPosition;
    public string progressionId;
    public float lastGainTime = float.NegativeInfinity;
    public int visualLevel;
    public XpProgressGain pendingGain;
    public int revision;
    public Coroutine routine;
  }

  [SerializeField, Min(0.05f)] float revealDurationSeconds = 1f;
  [SerializeField, Min(0.05f)] float fillDurationSeconds = 0.65f;
  [SerializeField, Min(0f)] float visibleDurationSeconds = 1f;
  [SerializeField, Min(0.05f)] float dismissDurationSeconds = 1f;
  [SerializeField, Min(0.05f)] float recentAbilityWindowSeconds = 1f;
  [SerializeField, Min(0f)] float offscreenViewportPadding = 0.05f;

  readonly List<Action> actions = new();
  Lane formLane;
  Lane[] abilityLanes = Array.Empty<Lane>();
  bool hierarchyResolved;

  void OnEnable() {
    ResolveHierarchy();
    ApplyActiveFormTheme();
    ResetAllLanes();
    RegisterHandlers();
  }

  void OnDisable() {
    UnregisterHandlers();
    ResetAllLanes();
  }

  void RegisterHandlers() {
    if (actions.Count > 0) {
      return;
    }

    actions.Add(MessageBus.On(CharacterMessageTopics.FormXpGained, OnFormXpGained));
    actions.Add(MessageBus.On(CharacterMessageTopics.AbilityXpGained, OnAbilityXpGained));
    actions.Add(MessageBus.On(CharacterMessageTopics.FormChanged, OnFormChanged));
  }

  void UnregisterHandlers() {
    for (var i = 0; i < actions.Count; i++) {
      actions[i]?.Invoke();
    }
    actions.Clear();
  }

  void OnFormXpGained(XpProgressGain gain) {
    if (formLane == null ||
        !string.Equals(gain.ProgressionId, EsperanzaForms.GetActive(), StringComparison.OrdinalIgnoreCase)) {
      return;
    }

    var now = Time.unscaledTime;
    if (formLane.routine != null &&
        string.Equals(formLane.progressionId, gain.ProgressionId, StringComparison.OrdinalIgnoreCase)) {
      UpdateLane(formLane, gain, now);
      return;
    }

    AssignLane(formLane, gain, now, setAbilityLabel: false);
  }

  void OnAbilityXpGained(XpProgressGain gain) {
    if (abilityLanes == null || abilityLanes.Length == 0 ||
        string.IsNullOrWhiteSpace(gain.ProgressionId)) {
      return;
    }

    var now = Time.unscaledTime;
    for (var i = 0; i < abilityLanes.Length; i++) {
      var lane = abilityLanes[i];
      if (lane == null ||
          !string.Equals(lane.progressionId, gain.ProgressionId, StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      if (now - lane.lastGainTime <= recentAbilityWindowSeconds) {
        UpdateLane(lane, gain, now);
      } else {
        AssignLane(lane, gain, now, setAbilityLabel: true);
      }
      return;
    }

    Lane availableLane = null;
    var oldestGainTime = float.PositiveInfinity;
    for (var i = 0; i < abilityLanes.Length; i++) {
      var lane = abilityLanes[i];
      if (lane == null) {
        continue;
      }
      if (string.IsNullOrWhiteSpace(lane.progressionId)) {
        availableLane = lane;
        break;
      }
      if (now - lane.lastGainTime <= recentAbilityWindowSeconds || lane.lastGainTime >= oldestGainTime) {
        continue;
      }

      availableLane = lane;
      oldestGainTime = lane.lastGainTime;
    }

    if (availableLane != null) {
      AssignLane(availableLane, gain, now, setAbilityLabel: true);
    }
  }

  void OnFormChanged(string formName) {
    ResetAllLanes();
    ApplyActiveFormTheme(formName);
  }

  void AssignLane(Lane lane, XpProgressGain gain, float now, bool setAbilityLabel) {
    if (lane == null) {
      return;
    }

    StopLane(lane);
    lane.progressionId = gain.ProgressionId;
    lane.lastGainTime = now;
    lane.visualLevel = gain.PreviousLevel;
    lane.pendingGain = gain;
    lane.revision = 1;

    if (setAbilityLabel) {
      ApplyAbilityLabel(lane, gain.ProgressionId);
    } else {
      RefreshRenderers(lane);
    }

    lane.root.localPosition = lane.hiddenPosition;
    SetLaneAlpha(lane, 0f);
    SetFill(lane, ResolveProgressPercent(gain.PreviousCurrentXp, gain.PreviousNextLevelXp));
    lane.routine = StartCoroutine(PlayLane(lane, reveal: true));
  }

  void UpdateLane(Lane lane, XpProgressGain gain, float now) {
    lane.lastGainTime = now;
    lane.pendingGain = gain;
    lane.revision += 1;
    if (lane.routine == null) {
      lane.routine = StartCoroutine(PlayLane(lane, reveal: false));
    }
  }

  IEnumerator PlayLane(Lane lane, bool reveal) {
    if (reveal) {
      yield return RevealLane(lane, fromCurrentState: false);
    }

    while (true) {
      var revision = lane.revision;
      var gain = lane.pendingGain;
      yield return AnimateProgress(lane, gain);
      if (revision == lane.revision) {
        yield return HoldLane(lane, revision);
      }
      if (revision != lane.revision) {
        continue;
      }

      yield return DismissLane(lane, revision);
      if (revision != lane.revision) {
        yield return RevealLane(lane, fromCurrentState: true);
        continue;
      }

      lane.progressionId = null;
      lane.lastGainTime = float.NegativeInfinity;
      lane.revision = 0;
      lane.routine = null;
      yield break;
    }
  }

  IEnumerator RevealLane(Lane lane, bool fromCurrentState) {
    var duration = Mathf.Max(revealDurationSeconds, 0.05f);
    var startPosition = fromCurrentState ? lane.root.localPosition : lane.hiddenPosition;
    var startAlpha = fromCurrentState ? ResolveLaneAlpha(lane) : 0f;
    var elapsed = 0f;
    while (elapsed < duration) {
      elapsed += Time.unscaledDeltaTime;
      var progress = Mathf.Clamp01(elapsed / duration);
      var easedProgress = Mathf.SmoothStep(0f, 1f, progress);
      lane.root.localPosition = Vector3.LerpUnclamped(startPosition, lane.shownPosition, easedProgress);
      SetLaneAlpha(lane, Mathf.Lerp(startAlpha, 1f, easedProgress));
      yield return null;
    }

    lane.root.localPosition = lane.shownPosition;
    SetLaneAlpha(lane, 1f);
  }

  IEnumerator HoldLane(Lane lane, int revision) {
    var duration = Mathf.Max(visibleDurationSeconds, 0f);
    var elapsed = 0f;
    while (elapsed < duration && revision == lane.revision) {
      elapsed += Time.unscaledDeltaTime;
      yield return null;
    }
  }

  IEnumerator DismissLane(Lane lane, int revision) {
    var duration = Mathf.Max(dismissDurationSeconds, 0.05f);
    var startPosition = lane.root.localPosition;
    var startAlpha = ResolveLaneAlpha(lane);
    var elapsed = 0f;
    while (elapsed < duration && revision == lane.revision) {
      elapsed += Time.unscaledDeltaTime;
      var progress = Mathf.Clamp01(elapsed / duration);
      var easedProgress = Mathf.SmoothStep(0f, 1f, progress);
      lane.root.localPosition = Vector3.LerpUnclamped(startPosition, lane.hiddenPosition, easedProgress);
      SetLaneAlpha(lane, Mathf.Lerp(startAlpha, 0f, easedProgress));
      yield return null;
    }

    if (revision == lane.revision) {
      lane.root.localPosition = lane.hiddenPosition;
      SetLaneAlpha(lane, 0f);
    }
  }

  IEnumerator AnimateProgress(Lane lane, XpProgressGain gain) {
    var targetPercent = ResolveProgressPercent(gain.CurrentXp, gain.NextLevelXp);
    var startPercent = ResolveLaneFillPercent(lane);
    var levelsGained = Mathf.Max(0, gain.CurrentLevel - lane.visualLevel);
    if (levelsGained == 0) {
      yield return AnimateFill(lane, startPercent, targetPercent);
      lane.visualLevel = gain.CurrentLevel;
      yield break;
    }

    yield return AnimateFill(lane, startPercent, 100f);
    for (var levelIndex = 1; levelIndex < levelsGained; levelIndex++) {
      SetFill(lane, 0f);
      yield return AnimateFill(lane, 0f, 100f);
    }

    SetFill(lane, 0f);
    yield return AnimateFill(lane, 0f, targetPercent);
    lane.visualLevel = gain.CurrentLevel;
  }

  IEnumerator AnimateFill(Lane lane, float startPercent, float targetPercent) {
    startPercent = Mathf.Clamp(startPercent, 0f, 100f);
    targetPercent = Mathf.Clamp(targetPercent, 0f, 100f);
    if (Mathf.Approximately(startPercent, targetPercent)) {
      SetFill(lane, targetPercent);
      yield break;
    }

    var duration = Mathf.Max(fillDurationSeconds, 0.05f);
    var elapsed = 0f;
    while (elapsed < duration) {
      elapsed += Time.unscaledDeltaTime;
      var progress = Mathf.Clamp01(elapsed / duration);
      SetFill(lane, Mathf.Lerp(startPercent, targetPercent, Mathf.SmoothStep(0f, 1f, progress)));
      yield return null;
    }

    SetFill(lane, targetPercent);
  }

  void ResolveHierarchy() {
    if (hierarchyResolved) {
      return;
    }

    var formRoot = FindChildRecursive(transform, "formXP");
    var abilityRoots = new Transform[MaximumAbilityLanes];
    for (var i = 0; i < abilityRoots.Length; i++) {
      abilityRoots[i] = FindChildRecursive(transform, "ability" + (i + 1));
    }

    formLane = CreateLane(formRoot, hasLabel: false);

    var resolvedAbilityLanes = new List<Lane>(MaximumAbilityLanes);
    for (var i = 0; i < abilityRoots.Length; i++) {
      var root = abilityRoots[i];
      if (root == null) {
        continue;
      }

      resolvedAbilityLanes.Add(CreateLane(root, hasLabel: true));
    }
    abilityLanes = resolvedAbilityLanes.ToArray();

    var legacyNumericText = FindChildRecursive(transform, "xpgain");
    if (legacyNumericText != null) {
      legacyNumericText.gameObject.SetActive(false);
    }
    hierarchyResolved = true;
  }

  Lane CreateLane(Transform root, bool hasLabel) {
    if (root == null) {
      return null;
    }

    var shownPosition = root.localPosition;
    var backRoot = FindChildRecursive(root, XpBarBackLabel);
    var fillRoot = FindChildRecursive(root, XpBarFillLabel);
    var labelRoot = hasLabel ? FindChildRecursive(root, "labeltext") : null;
    var renderers = root.GetComponentsInChildren<SpriteRenderer>(includeInactive: true);

    return new Lane {
      root = root,
      backSprite = backRoot != null ? backRoot.GetComponent<SpriteWithNormals>() : null,
      fillSprite = fillRoot != null ? fillRoot.GetComponent<SpriteWithNormals>() : null,
      fill = fillRoot != null ? fillRoot.GetComponent<AnchoredSpriteStretch>() : null,
      label = labelRoot != null ? labelRoot.GetComponent<FontText>() : null,
      hiddenPosition = ResolveOffscreenPosition(root, shownPosition, renderers, exitRight: hasLabel),
      shownPosition = shownPosition,
      renderers = renderers
    };
  }

  void ApplyActiveFormTheme(string requestedForm = null) {
    var activeForm = EsperanzaForms.ResolveFormKey(requestedForm);
    if (string.IsNullOrWhiteSpace(activeForm)) {
      activeForm = EsperanzaForms.GetActive();
    }
    if (string.IsNullOrWhiteSpace(activeForm)) {
      return;
    }

    ApplyLaneTheme(formLane, activeForm, applyLabelColors: false);
    for (var i = 0; i < abilityLanes.Length; i++) {
      ApplyLaneTheme(abilityLanes[i], activeForm, applyLabelColors: true);
    }
  }

  static void ApplyLaneTheme(Lane lane, string formName, bool applyLabelColors) {
    if (lane == null) {
      return;
    }

    ApplySpriteTheme(lane.backSprite, formName, XpBarBackLabel);
    ApplySpriteTheme(lane.fillSprite, formName, XpBarFillLabel);
    RefreshRenderers(lane);

    if (applyLabelColors &&
        lane.label != null &&
        ShaderColors.TryGetFormPalette(
          formName,
          ShaderColors.PrimaryGroup,
          out var labelColor,
          out var labelOutlineColor,
          out _,
          out _
        )) {
      lane.label.SetShaderColors(labelColor, labelOutlineColor);
    }
  }

  static void ApplySpriteTheme(SpriteWithNormals sprite, string formName, string label) {
    if (sprite == null || string.IsNullOrWhiteSpace(formName) || string.IsNullOrWhiteSpace(label)) {
      return;
    }

    var libraryName = DirectFormUiRoot + formName + DirectFormUiSuffix;
    if (!string.Equals(sprite.libraryName, libraryName, StringComparison.OrdinalIgnoreCase)) {
      sprite.SetLibraryName(libraryName);
    }
    if (!string.Equals(sprite.labelPrefix, label, StringComparison.Ordinal)) {
      sprite.SetLabelPrefix(label);
    }
    if (!string.Equals(sprite.category, CharUiCategory, StringComparison.Ordinal)) {
      sprite.SetAnimation(CharUiCategory);
    }
    sprite.ForceUpdateSpriteAndNormal();
  }

  Vector3 ResolveOffscreenPosition(
    Transform root,
    Vector3 shownPosition,
    SpriteRenderer[] renderers,
    bool exitRight
  ) {
    var fallbackPosition = shownPosition;
    fallbackPosition.x += exitRight ? 40f : -40f;

    var notificationCamera = GetComponentInParent<Camera>();
    if (root == null || root.parent == null || notificationCamera == null ||
        renderers == null || renderers.Length == 0) {
      return fallbackPosition;
    }

    var hasBounds = false;
    var renderedBounds = new Bounds();
    for (var i = 0; i < renderers.Length; i++) {
      var renderer = renderers[i];
      if (renderer == null || !renderer.gameObject.activeInHierarchy) {
        continue;
      }

      if (!hasBounds) {
        renderedBounds = renderer.bounds;
        hasBounds = true;
      } else {
        renderedBounds.Encapsulate(renderer.bounds);
      }
    }
    if (!hasBounds) {
      return fallbackPosition;
    }

    var renderedCenterViewportPoint = notificationCamera.WorldToViewportPoint(renderedBounds.center);
    exitRight = renderedCenterViewportPoint.x >= 0.5f;
    var edgeWorldPoint = new Vector3(
      exitRight ? renderedBounds.min.x : renderedBounds.max.x,
      renderedBounds.center.y,
      renderedBounds.center.z
    );
    var edgeViewportPoint = notificationCamera.WorldToViewportPoint(edgeWorldPoint);
    var offscreenWorldPoint = notificationCamera.ViewportToWorldPoint(new Vector3(
      exitRight
        ? 1f + Mathf.Max(offscreenViewportPadding, 0f)
        : -Mathf.Max(offscreenViewportPadding, 0f),
      edgeViewportPoint.y,
      edgeViewportPoint.z
    ));
    var offscreenRootWorldPosition = root.position + (offscreenWorldPoint - edgeWorldPoint);
    return root.parent.InverseTransformPoint(offscreenRootWorldPosition);
  }

  void ResetAllLanes() {
    ResetLane(formLane);
    if (abilityLanes == null) {
      return;
    }

    for (var i = 0; i < abilityLanes.Length; i++) {
      ResetLane(abilityLanes[i]);
    }
  }

  void ResetLane(Lane lane) {
    if (lane == null) {
      return;
    }

    StopLane(lane);
    lane.progressionId = null;
    lane.lastGainTime = float.NegativeInfinity;
    lane.visualLevel = 0;
    lane.revision = 0;
    lane.root.localPosition = lane.hiddenPosition;
    SetLaneAlpha(lane, 0f);
    SetFill(lane, 0f);
  }

  void StopLane(Lane lane) {
    if (lane == null || lane.routine == null) {
      return;
    }

    StopCoroutine(lane.routine);
    lane.routine = null;
  }

  static void ApplyAbilityLabel(Lane lane, string animationName) {
    if (lane == null || lane.label == null) {
      return;
    }

    var abbreviation = ResolveAbilityAbbreviation(animationName);
    if (!string.Equals(lane.label.content, abbreviation, StringComparison.Ordinal)) {
      lane.label.content = abbreviation;
      lane.label.Generate();
    }
    RefreshRenderers(lane);
  }

  static string ResolveAbilityAbbreviation(string animationName) {
    var displayName = EsperanzaAbilities.GetDisplayName(animationName);
    if (TryFindAbbreviation(displayName, out var abbreviation)) {
      return abbreviation;
    }

    var normalizedDisplayName = NormalizeText(displayName);
    foreach (var entry in Abbreviations.all) {
      if (IsOneEditApart(normalizedDisplayName, NormalizeText(entry.Value))) {
        return entry.Key;
      }
    }

    if (TryFindAbbreviation("Super " + displayName, out abbreviation)) {
      return abbreviation;
    }

    var initials = BuildInitials(displayName);
    if (!string.IsNullOrWhiteSpace(initials) && Abbreviations.all.ContainsKey(initials)) {
      return initials;
    }

    var lastSpaceIndex = displayName.LastIndexOf(' ');
    if (lastSpaceIndex >= 0 &&
        TryFindAbbreviation("Super " + displayName.Substring(lastSpaceIndex + 1), out abbreviation)) {
      return abbreviation;
    }

    if (!string.IsNullOrWhiteSpace(initials)) {
      return initials;
    }
    if (string.IsNullOrWhiteSpace(displayName)) {
      return "?";
    }

    return displayName.Substring(0, Mathf.Min(2, displayName.Length)).ToUpperInvariant();
  }

  static bool TryFindAbbreviation(string displayName, out string abbreviation) {
    foreach (var entry in Abbreviations.all) {
      if (!string.Equals(entry.Value, displayName, StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      abbreviation = entry.Key;
      return true;
    }

    abbreviation = null;
    return false;
  }

  static string BuildInitials(string value) {
    if (string.IsNullOrWhiteSpace(value)) {
      return "";
    }

    var result = new StringBuilder();
    var takeNext = true;
    for (var i = 0; i < value.Length; i++) {
      var character = value[i];
      if (char.IsWhiteSpace(character)) {
        takeNext = true;
        continue;
      }
      if (!takeNext) {
        continue;
      }

      result.Append(char.ToUpperInvariant(character));
      takeNext = false;
    }
    return result.ToString();
  }

  static string NormalizeText(string value) {
    if (string.IsNullOrWhiteSpace(value)) {
      return "";
    }

    var normalized = new StringBuilder(value.Length);
    for (var i = 0; i < value.Length; i++) {
      if (char.IsLetterOrDigit(value[i])) {
        normalized.Append(char.ToUpperInvariant(value[i]));
      }
    }
    return normalized.ToString();
  }

  static bool IsOneEditApart(string left, string right) {
    if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right) ||
        Mathf.Abs(left.Length - right.Length) > 1) {
      return false;
    }

    if (left.Length > right.Length) {
      var swap = left;
      left = right;
      right = swap;
    }

    var leftIndex = 0;
    var rightIndex = 0;
    var edits = 0;
    while (leftIndex < left.Length && rightIndex < right.Length) {
      if (left[leftIndex] == right[rightIndex]) {
        leftIndex++;
        rightIndex++;
        continue;
      }

      edits++;
      if (edits > 1) {
        return false;
      }
      if (left.Length == right.Length) {
        leftIndex++;
      }
      rightIndex++;
    }

    if (rightIndex < right.Length || leftIndex < left.Length) {
      edits++;
    }
    return edits == 1;
  }

  static void RefreshRenderers(Lane lane) {
    if (lane != null && lane.root != null) {
      lane.renderers = lane.root.GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
      for (var i = 0; i < lane.renderers.Length; i++) {
        var renderer = lane.renderers[i];
        if (renderer == null) {
          continue;
        }

        var formEffect = renderer.GetComponent<FormSpriteEffect>();
        if (formEffect == null && Application.isPlaying) {
          formEffect = renderer.gameObject.AddComponent<FormSpriteEffect>();
        }
        formEffect?.SetEffect(FormSpriteEffect.EffectSelection.FollowCharacterForm);
      }
    }
  }

  static void SetLaneAlpha(Lane lane, float alpha) {
    if (lane == null || lane.renderers == null) {
      return;
    }

    var clampedAlpha = Mathf.Clamp01(alpha);
    for (var i = 0; i < lane.renderers.Length; i++) {
      var renderer = lane.renderers[i];
      if (renderer == null) {
        continue;
      }

      var color = renderer.color;
      color.a = clampedAlpha;
      renderer.color = color;
    }
  }

  static float ResolveLaneAlpha(Lane lane) {
    if (lane == null || lane.renderers == null) {
      return 0f;
    }

    for (var i = 0; i < lane.renderers.Length; i++) {
      if (lane.renderers[i] != null) {
        return lane.renderers[i].color.a;
      }
    }
    return 0f;
  }

  static float ResolveLaneFillPercent(Lane lane) {
    return lane != null && lane.fill != null
      ? lane.fill.stretchPercent.x
      : 0f;
  }

  static void SetFill(Lane lane, float percent) {
    if (lane == null || lane.fill == null) {
      return;
    }

    var clampedPercent = Mathf.Clamp(percent, 0f, 100f);
    if (Mathf.Approximately(lane.fill.stretchPercent.x, clampedPercent)) {
      return;
    }

    lane.fill.stretchPercent = new Vector2(clampedPercent, lane.fill.stretchPercent.y);
    lane.fill.RefreshStretch();
  }

  static float ResolveProgressPercent(int currentXp, int nextLevelXp) {
    return Mathf.Clamp01((float)Mathf.Max(currentXp, 0) / Mathf.Max(nextLevelXp, 1)) * 100f;
  }

  static Transform FindChildRecursive(Transform root, string targetName) {
    if (root == null || string.IsNullOrWhiteSpace(targetName)) {
      return null;
    }

    if (string.Equals(root.name, targetName, StringComparison.OrdinalIgnoreCase)) {
      return root;
    }

    for (var i = 0; i < root.childCount; i++) {
      var result = FindChildRecursive(root.GetChild(i), targetName);
      if (result != null) {
        return result;
      }
    }

    return null;
  }
}
