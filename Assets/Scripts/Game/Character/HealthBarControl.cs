using System.Collections.Generic;
using CustomInspector;
using UnityEngine;
using UnityEngine.UI;

public class HealthBarControl : MonoBehaviour {
  const int NumericGlyphCapacity = 7;
  const float FullCircleDegrees = 360f;
  const string NormalAvatarExpression = "Normal";
  const string HurtAvatarExpression = "Disgust";
  const string DirectFormUiRoot = "FormUIs/";
  const string DirectFormUiSuffix = "UI";
  const string DirectFormUiDialogCategory = "Dialog";
  const string HealthTextPath = "Text/hpNum";
  const string EnergyTextPath = "Text/nrgNum";

  static readonly int ClipUvLeft = Shader.PropertyToID("_ClipUvLeft");
  static readonly int ClipUvRight = Shader.PropertyToID("_ClipUvRight");
  static readonly int ClipUvUp = Shader.PropertyToID("_ClipUvUp");
  static readonly int ClipUvDown = Shader.PropertyToID("_ClipUvDown");
  static readonly int ClipMode = Shader.PropertyToID("_ClipMode");
  static readonly int RadialCenter = Shader.PropertyToID("_RadialCenter");
  static readonly int RadialStartAngle = Shader.PropertyToID("_RadialStartAngle");
  static readonly int RadialFillAmount = Shader.PropertyToID("_RadialFillAmount");
  static readonly int RadialReverseDirection = Shader.PropertyToID("_RadialReverseDirection");
  static readonly int RadialInvert = Shader.PropertyToID("_RadialInvert");

  public string Form;
  [SerializeField] private List<GameObject> objectsToChange = new();
  private CharacterState characterState;
  [SerializeField] private FontText healthText;
  [SerializeField] private FontText nrgText;
  [Header("Damage Reaction")]
  [SerializeField] private SpriteWithNormals healthAvatar;
  [SerializeField, Min(0f)] private float hurtAvatarDuration = 0.45f;
  [Header("Meter Controls")]
  [Min(1f)] public float hpCurveMaximum = 9999f;
  [Min(1f)] public float hpExtendMinimum = 10000f;
  [Min(1f)] public float hpExtendMaximum = 99999f;
  [Range(0f, 1f)] public float hpExtendEmptyRightClip = 1f;
  [Range(0f, 1f)] public float hpExtendFullRightClip = 0.3f;
  [Min(1f)] public float nrgCurveMaximum = 999f;
  [Min(1f)] public float nrgExtendMinimum = 1000f;
  [Min(1f)] public float nrgExtendMaximum = 9999f;
  [Range(0f, 1f)] public float nrgExtendEmptyRightClip = 1f;
  [Range(0f, 1f)] public float nrgExtendFullRightClip = 0.3f;
  [Range(0f, 1f)] public float hpMinimumCurveFillAmount = 0.05f;
  [Range(0f, 1f)] public float hpMaximumCurveFillAmount = 0.25f;
  [Range(0f, FullCircleDegrees)] public float hpCurveFillStartAngle = 180f;
  public bool reverseHpCurveFillDirection;
  public bool invertHpCurveFill;
  [Range(0f, 1f)] public float nrgMinimumCurveFillAmount = 0.05f;
  [Range(0f, 1f)] public float nrgMaximumCurveFillAmount = 0.25f;
  [Range(0f, FullCircleDegrees)] public float nrgCurveFillStartAngle = 180f;
  public bool reverseNrgCurveFillDirection = true;
  public bool invertNrgCurveFill;

  [Header("Meter Shader Origins")]
  [SerializeField] private GameObject hpCurve;
  [SerializeField] private GameObject nrgCurve;
  [SerializeField] private GameObject hpExtend;
  [SerializeField] private GameObject nrgExtend;
  [Tooltip("HP radial center in normalized sprite UV coordinates. (0.5, 0.5) is the texture center.")]
  public Vector3 hpCurveLocalPosition = new(0.97f, 0.86f, 0f);
  [Tooltip("NRG radial center in normalized sprite UV coordinates. (0.5, 0.5) is the texture center.")]
  public Vector3 nrgCurveLocalPosition = new(0.97f, 0.16f, 0f);
  [Tooltip("Horizontal HP Extend clip start offset from the sprite's left edge. Only X is used.")]
  public Vector3 hpExtendLocalPosition;
  [Tooltip("Horizontal NRG Extend clip start offset from the sprite's left edge. Only X is used.")]
  public Vector3 nrgExtendLocalPosition;

  [Header("Default Values")]
  [Min(0f)] public float defaultHp = 10f;
  [Min(0f)] public float defaultNrg = 1f;
  [Button(nameof(SetDefaults), label = "Set Defaults", size = Size.small)][HideField] public bool setDefaultsButton;

  [Header("Fill Renderers")]
  [SerializeField] private SpriteRenderer hpCurveFill;
  [SerializeField] private SpriteRenderer nrgCurveFill;
  [SerializeField] private SpriteRenderer hpExtendFill;
  [SerializeField] private SpriteRenderer nrgExtendFill;

  private MaterialPropertyBlock hpCurveFillProperties;
  private MaterialPropertyBlock nrgCurveFillProperties;
  private MaterialPropertyBlock hpExtendFillProperties;
  private MaterialPropertyBlock nrgExtendFillProperties;
  private string lastLabelPrefix;
  private EndlessNumber lastHp;
  private EndlessNumber lastNrg;
  private System.Action offHitReceived;
  private Coroutine hurtAvatarCoroutine;

  void Awake() {
    ResolveVitalTextBindings();
    ClampMeterControls();
    hpCurveFillProperties = new MaterialPropertyBlock();
    nrgCurveFillProperties = new MaterialPropertyBlock();
    hpExtendFillProperties = new MaterialPropertyBlock();
    nrgExtendFillProperties = new MaterialPropertyBlock();
  }

  void OnValidate() {
    ClampMeterControls();
    if (Application.isPlaying) {
      lastHp = null;
      lastNrg = null;
      return;
    }
    lastLabelPrefix = Form;
  }

  void OnEnable() {
    if (Application.isPlaying) {
      offHitReceived?.Invoke();
      offHitReceived = MessageBus.On(CharacterMessageTopics.HitReceived, HandleCharacterHit);
      RestoreAvatarExpression();
      return;
    }
    lastLabelPrefix = Form;
    RefreshSprites(lastLabelPrefix);
  }

  void OnDisable() {
    if (!Application.isPlaying) {
      return;
    }

    offHitReceived?.Invoke();
    offHitReceived = null;
    if (hurtAvatarCoroutine != null) {
      StopCoroutine(hurtAvatarCoroutine);
      hurtAvatarCoroutine = null;
    }
    RestoreAvatarExpression();
  }

  void Start() {
    ResolveVitalTextBindings();
    ResolveCharacterState();
    ResolveHealthAvatar();
    healthText?.EnsureGlyphCapacity(NumericGlyphCapacity);
    nrgText?.EnsureGlyphCapacity(NumericGlyphCapacity);

    var activeForm = EsperanzaForms.GetActive();
    lastLabelPrefix = Form != null ? Form : activeForm;
    RefreshSprites(lastLabelPrefix);
    RefreshVitals(force: true);
  }

  void Update() {
    var desiredLabelPrefix = Form != null ? Form : EsperanzaForms.GetActive();
    if (desiredLabelPrefix != lastLabelPrefix) {
      lastLabelPrefix = desiredLabelPrefix;
      RefreshSprites(lastLabelPrefix);
    }

    RefreshVitals();
  }

  void RefreshVitals(bool force = false) {
    var hp = ResolveCurrentHealth();
    if (force || lastHp == null || lastHp != hp) {
      lastHp = hp.Copy();
      if (healthText != null) {
        healthText.content = FormatVitalAmount(hp);
        healthText.Generate();
      }
      ApplyMeter(
        hp,
        hpCurveMaximum,
        hpExtendMinimum,
        hpExtendMaximum,
        hpMinimumCurveFillAmount,
        hpMaximumCurveFillAmount,
        hpCurveFillStartAngle,
        reverseHpCurveFillDirection,
        invertHpCurveFill,
        hpExtendEmptyRightClip,
        hpExtendFullRightClip,
        hpCurveLocalPosition,
        hpExtendLocalPosition,
        hpCurve,
        hpExtend,
        hpCurveFill,
        hpExtendFill,
        hpCurveFillProperties,
        hpExtendFillProperties
      );
    }

    var nrg = GetTotalVitality("NRG");
    if (force || lastNrg == null || lastNrg != nrg) {
      lastNrg = nrg.Copy();
      if (nrgText != null) {
        nrgText.content = FormatVitalAmount(nrg);
        nrgText.Generate();
      }
      ApplyMeter(
        nrg,
        nrgCurveMaximum,
        nrgExtendMinimum,
        nrgExtendMaximum,
        nrgMinimumCurveFillAmount,
        nrgMaximumCurveFillAmount,
        nrgCurveFillStartAngle,
        reverseNrgCurveFillDirection,
        invertNrgCurveFill,
        nrgExtendEmptyRightClip,
        nrgExtendFullRightClip,
        nrgCurveLocalPosition,
        nrgExtendLocalPosition,
        nrgCurve,
        nrgExtend,
        nrgCurveFill,
        nrgExtendFill,
        nrgCurveFillProperties,
        nrgExtendFillProperties
      );
    }
  }

  static string FormatVitalAmount(EndlessNumber amount) {
    return amount != null && amount.IsPositive
      ? amount.ToGlyphString()
      : IntegerTextCache.Get(0);
  }

  static EndlessNumber GetTotalVitality(string statId) {
    if (!AllStatValues.Esperanza.TryGetValue(statId, out var total) ||
        total == null ||
        total.IsPercentage ||
        total.EndlessValue == null ||
        !total.EndlessValue.IsPositive) {
      return new EndlessNumber();
    }
    return total.EndlessValue;
  }

  EndlessNumber ResolveCurrentHealth() {
    var state = ResolveCharacterState();
    return state != null ? state.CurrentHealth : GetTotalVitality("HP");
  }

  CharacterState ResolveCharacterState() {
    if (characterState != null) {
      return characterState;
    }

    characterState = GetComponentInParent<CharacterState>();
    if (characterState == null) {
      characterState = SingleSceneManager.ResolveGameplayCharacterState();
    }
    return characterState;
  }

  void ResolveVitalTextBindings() {
    var resolvedHealthText = transform.Find(HealthTextPath)?.GetComponent<FontText>();
    if (resolvedHealthText != null) {
      healthText = resolvedHealthText;
    }

    var resolvedEnergyText = transform.Find(EnergyTextPath)?.GetComponent<FontText>();
    if (resolvedEnergyText != null) {
      nrgText = resolvedEnergyText;
    }
  }

  static void ApplyMeter(
    EndlessNumber total,
    float curveMaximum,
    float extendMinimum,
    float extendMaximum,
    float minimumCurveFill,
    float maximumCurveFill,
    float curveStartAngle,
    bool reverseCurveFillDirection,
    bool invertCurveFill,
    float extendEmptyRightClip,
    float extendFullRightClip,
    Vector3 curveOriginLocalPosition,
    Vector3 extendOriginLocalPosition,
    GameObject curve,
    GameObject extend,
    SpriteRenderer curveFill,
    SpriteRenderer extendFill,
    MaterialPropertyBlock curveFillProperties,
    MaterialPropertyBlock extendFillProperties
  ) {
    SetActive(curve, true);

    var curveMaximumValue = new EndlessNumber(curveMaximum);
    var normalizedCurveFill = Mathf.Clamp01((float)total.RatioTo(curveMaximumValue));
    var curveFillAmount = !total.IsPositive
      ? 0f
      : Mathf.Clamp(normalizedCurveFill * maximumCurveFill, minimumCurveFill, maximumCurveFill);
    ApplyRadialFill(
      curveFill,
      curveFillProperties,
      curveFillAmount,
      curveStartAngle,
      reverseCurveFillDirection,
      invertCurveFill,
      curveOriginLocalPosition
    );

    var shouldExtend = total > curveMaximumValue;
    SetActive(extend, shouldExtend);

    var resolvedExtendMaximum = Mathf.Max(extendMaximum, extendMinimum);
    var extendFillAmount = shouldExtend
      ? ResolveRangeFill(total, curveMaximum, resolvedExtendMaximum)
      : 0f;
    ApplyHorizontalFill(
      extendFill,
      extendFillProperties,
      extendFillAmount,
      extendEmptyRightClip,
      extendFullRightClip,
      extendOriginLocalPosition
    );
  }

  static void ApplyRadialFill(
    SpriteRenderer renderer,
    MaterialPropertyBlock properties,
    float amount,
    float startAngle,
    bool reverseDirection,
    bool invertFill,
    Vector3 radialOriginLocalPosition
  ) {
    if (renderer == null) return;

    amount = Mathf.Clamp01(amount);
    renderer.enabled = amount > 0f;
    renderer.GetPropertyBlock(properties);
    properties.SetFloat(ClipMode, 1f);
    properties.SetVector(RadialCenter, ResolveRadialCenter(radialOriginLocalPosition));
    properties.SetFloat(RadialStartAngle, startAngle);
    properties.SetFloat(RadialFillAmount, amount);
    properties.SetFloat(RadialReverseDirection, reverseDirection ? 1f : 0f);
    properties.SetFloat(RadialInvert, invertFill ? 1f : 0f);
    renderer.SetPropertyBlock(properties);
  }

  public void SetDefaults() {
    ClampMeterControls();
    if (!Application.isPlaying) return;

    if (AllStatValues.Esperanza.TryGetValue("HP", out var hp) && hp != null) {
      hp.Set(defaultHp);
    }
    if (AllStatValues.Esperanza.TryGetValue("NRG", out var nrg) && nrg != null) {
      nrg.Set(defaultNrg);
    }
    ResolveCharacterState()?.RestoreHealthToMaximum();
    RefreshVitals(force: true);
  }

  void HandleCharacterHit(CharacterDamageEvent damageEvent) {
    RefreshVitals(force: true);
    var avatar = ResolveHealthAvatar();
    if (avatar == null) {
      return;
    }

    SetAvatarExpression(HurtAvatarExpression);
    if (hurtAvatarCoroutine != null) {
      StopCoroutine(hurtAvatarCoroutine);
    }
    hurtAvatarCoroutine = StartCoroutine(RestoreAvatarExpressionAfterDelay());
  }

  System.Collections.IEnumerator RestoreAvatarExpressionAfterDelay() {
    yield return TimeScale.WaitForSecondsScaled(hurtAvatarDuration, this);
    hurtAvatarCoroutine = null;
    RestoreAvatarExpression();
  }

  SpriteWithNormals ResolveHealthAvatar() {
    if (healthAvatar != null) {
      return healthAvatar;
    }

    for (var i = 0; i < objectsToChange.Count; i++) {
      var target = objectsToChange[i];
      if (target == null || !string.Equals(target.name, "avatar", System.StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      healthAvatar = target.GetComponent<SpriteWithNormals>();
      if (healthAvatar != null) {
        return healthAvatar;
      }
    }

    var sprites = GetComponentsInChildren<SpriteWithNormals>(includeInactive: true);
    for (var i = 0; i < sprites.Length; i++) {
      var candidate = sprites[i];
      if (candidate == null || !string.Equals(candidate.name, "avatar", System.StringComparison.OrdinalIgnoreCase)) {
        continue;
      }
      healthAvatar = candidate;
      return healthAvatar;
    }

    return null;
  }

  void RestoreAvatarExpression() {
    SetAvatarExpression(NormalAvatarExpression);
  }

  void SetAvatarExpression(string expression) {
    var avatar = ResolveHealthAvatar();
    if (avatar == null) {
      return;
    }

    var changed = false;
    if (IsDirectFormUiLibrary(avatar.libraryName)) {
      if (!string.Equals(avatar.category, DirectFormUiDialogCategory, System.StringComparison.Ordinal)) {
        avatar.SetAnimation(DirectFormUiDialogCategory);
        changed = true;
      }
      if (!string.Equals(avatar.labelPrefix, expression, System.StringComparison.Ordinal)) {
        avatar.SetLabelPrefix(expression);
        changed = true;
      }
    }
    else if (!string.Equals(avatar.category, expression, System.StringComparison.Ordinal)) {
      avatar.SetAnimation(expression);
      changed = true;
    }

    if (!changed) {
      return;
    }

    if (avatar.isActiveAndEnabled && avatar.gameObject.activeInHierarchy) {
      avatar.ForceUpdateSpriteAndNormal();
    }
  }

  static float ResolveRangeFill(EndlessNumber value, float minimum, float maximum) {
    var minimumValue = new EndlessNumber(minimum);
    if (value <= minimumValue) {
      return 0f;
    }

    var maximumValue = new EndlessNumber(maximum);
    if (value >= maximumValue) {
      return 1f;
    }

    return value.TryToDouble(out var finiteValue)
      ? Mathf.InverseLerp(minimum, maximum, (float)finiteValue)
      : 1f;
  }

  void ClampMeterControls() {
    hpCurveMaximum = Mathf.Max(hpCurveMaximum, 1f);
    hpExtendMinimum = Mathf.Max(hpExtendMinimum, hpCurveMaximum);
    hpExtendMaximum = Mathf.Max(hpExtendMaximum, hpExtendMinimum);
    hpExtendEmptyRightClip = Mathf.Clamp01(hpExtendEmptyRightClip);
    hpExtendFullRightClip = Mathf.Clamp01(hpExtendFullRightClip);
    nrgCurveMaximum = Mathf.Max(nrgCurveMaximum, 1f);
    nrgExtendMinimum = Mathf.Max(nrgExtendMinimum, nrgCurveMaximum);
    nrgExtendMaximum = Mathf.Max(nrgExtendMaximum, nrgExtendMinimum);
    nrgExtendEmptyRightClip = Mathf.Clamp01(nrgExtendEmptyRightClip);
    nrgExtendFullRightClip = Mathf.Clamp01(nrgExtendFullRightClip);
    hpMaximumCurveFillAmount = Mathf.Clamp01(hpMaximumCurveFillAmount);
    hpMinimumCurveFillAmount = Mathf.Clamp(hpMinimumCurveFillAmount, 0f, hpMaximumCurveFillAmount);
    hpCurveFillStartAngle = Mathf.Repeat(hpCurveFillStartAngle, FullCircleDegrees);
    nrgMaximumCurveFillAmount = Mathf.Clamp01(nrgMaximumCurveFillAmount);
    nrgMinimumCurveFillAmount = Mathf.Clamp(nrgMinimumCurveFillAmount, 0f, nrgMaximumCurveFillAmount);
    nrgCurveFillStartAngle = Mathf.Repeat(nrgCurveFillStartAngle, FullCircleDegrees);
    defaultHp = Mathf.Max(defaultHp, 0f);
    defaultNrg = Mathf.Max(defaultNrg, 0f);
    hurtAvatarDuration = Mathf.Max(hurtAvatarDuration, 0f);
  }

  static void ApplyHorizontalFill(
    SpriteRenderer renderer,
    MaterialPropertyBlock properties,
    float amount,
    float emptyRightClip,
    float fullRightClip,
    Vector3 clipOriginLocalPosition
  ) {
    if (renderer == null) return;

    amount = Mathf.Clamp01(amount);
    var clipStart = ResolveHorizontalClipStart(renderer, clipOriginLocalPosition.x);
    var clipRight = Mathf.Lerp(emptyRightClip, fullRightClip, amount);
    renderer.enabled = amount > 0f;
    renderer.GetPropertyBlock(properties);
    properties.SetFloat(ClipMode, 0f);
    properties.SetFloat(ClipUvLeft, clipStart);
    properties.SetFloat(ClipUvRight, clipRight);
    properties.SetFloat(ClipUvUp, 0f);
    properties.SetFloat(ClipUvDown, 0f);
    renderer.SetPropertyBlock(properties);
  }

  static Vector4 ResolveRadialCenter(Vector3 position) {
    return new Vector4(position.x, position.y, 0f, 0f);
  }

  static float ResolveHorizontalClipStart(SpriteRenderer renderer, float localPositionX) {
    var sprite = renderer.sprite;
    if (sprite == null || Mathf.Abs(sprite.bounds.size.x) <= Mathf.Epsilon) {
      return Mathf.Clamp01(localPositionX);
    }

    return Mathf.Clamp01(localPositionX / sprite.bounds.size.x);
  }

  static void SetActive(GameObject target, bool active) {
    if (target != null && target.activeSelf != active) {
      target.SetActive(active);
    }
  }

  void RefreshSprites(string labelPrefix) {
    for (int i = 0; i < objectsToChange.Count; i++) {
      var target = objectsToChange[i];
      if (target == null) continue;
      var sprite = target.GetComponent<SpriteWithNormals>();
      if (sprite == null) continue;
      if (TryBuildDirectFormUiLibraryName(sprite.libraryName, labelPrefix, out var libraryName)) {
        if (!string.Equals(sprite.libraryName, libraryName, System.StringComparison.OrdinalIgnoreCase)) {
          sprite.SetLibraryName(libraryName);
        }
      }
      else if (!string.Equals(sprite.labelPrefix, labelPrefix, System.StringComparison.Ordinal)) {
        sprite.SetLabelPrefix(labelPrefix);
      }
      sprite.ForceUpdateSpriteAndNormal();
    }
  }

  static bool TryBuildDirectFormUiLibraryName(string currentLibraryName, string form, out string libraryName) {
    libraryName = "";
    if (!IsDirectFormUiLibrary(currentLibraryName) || string.IsNullOrWhiteSpace(form)) {
      return false;
    }

    libraryName = DirectFormUiRoot + form.Trim() + DirectFormUiSuffix;
    return true;
  }

  static bool IsDirectFormUiLibrary(string libraryName) {
    if (string.IsNullOrWhiteSpace(libraryName)) {
      return false;
    }

    var normalized = libraryName.Trim().Replace('\\', '/');
    return normalized.StartsWith(DirectFormUiRoot, System.StringComparison.OrdinalIgnoreCase) &&
           normalized.EndsWith(DirectFormUiSuffix, System.StringComparison.OrdinalIgnoreCase);
  }
}
