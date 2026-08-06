using UnityEngine;

[RequireComponent(typeof(EnemyInfo))]
public class EnemyHealth : MonoBehaviour {
  const int SharedDamageNumberPoolSize = 24;
  const float DamageNumberLifetimeSeconds = 1.5f;
  const int DamageNumberGlyphCapacity = 7;
  const int HealthGlyphCapacity = 7;
  const int MaxHealthGlyphCapacity = 8;

  static readonly System.Collections.Generic.Dictionary<GameObject, FontText> damageTextByObject =
    new(SharedDamageNumberPoolSize);
  static readonly System.Collections.Generic.Dictionary<GameObject, DamageNumberArcMotion> damageMotionByObject =
    new(SharedDamageNumberPoolSize);
  static readonly System.Collections.Generic.Dictionary<string, string> abilityXpSourceByEnemyType =
    new(System.StringComparer.Ordinal);

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetRuntimeCaches() {
    UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= ClearSceneObjectCaches;
    damageTextByObject.Clear();
    damageMotionByObject.Clear();
    abilityXpSourceByEnemyType.Clear();
    UnityEngine.SceneManagement.SceneManager.sceneUnloaded += ClearSceneObjectCaches;
  }

  static void ClearSceneObjectCaches(UnityEngine.SceneManagement.Scene scene) {
    damageTextByObject.Clear();
    damageMotionByObject.Clear();
  }

  EnemyInfo enemyInfo;
  HurtBox2D hurtBox;
  EnemyController enemyController;
  EnemyAIController enemyAiController;
  int appliedSpawnContextVersion = -1;
  bool deathInProgress;
  bool enemyAiWasEnabledBeforeDeath;
  Coroutine deathCoroutine;
  Coroutine fallbackHurtCoroutine;
  EndlessNumber displayedCurrentHp;
  EndlessNumber displayedMaxHp;
  CharacterState characterState;
  string abilityXpEnemyType;
  string abilityXpSource;
  UnityEngine.Events.UnityAction<HitBox2D> hurtListener;
  Transform facingInvariantHealthUiRoot;
  bool healthUiHierarchyResolved;
  int activeComboIndex = -1;
  int nextComboMoveIndex;
  float comboExpiresAt;

  [Header("Visual Feedback")]
  [Tooltip("Prefab to spawn for showing damage numbers")]
  public GameObject damageNumberPrefab;

  [Tooltip("Stretch component used to scale the health bar horizontally")]
  public AnchoredSpriteStretch healthBarStretch;

  [Tooltip("FontText to display current health text")]
  public FontText healthText;

  [SerializeField, Min(0f), Tooltip("Camera displacement when one hit removes 100% of this enemy's max health. Actual shake scales with the percentage removed.")]
  float screenShakeFactor = 0.65f;

  [Header("Combo Juggle")]
  [SerializeField, Min(0.1f)] float comboContinuationSeconds = 1.25f;
  [SerializeField, Min(0f)] float comboJuggleHorizontalOffset = 1.45f;
  [SerializeField] float comboJuggleVerticalOffset = 0.35f;

  FontText maxHealthText;

  static bool ShouldLogDebug() {
    return SpriteStreamingRuntimeSettings.EnableVerboseRuntimeConsoleLogs &&
           (Application.isEditor || Debug.isDebugBuild);
  }

  void Awake() {
    enemyInfo = GetComponent<EnemyInfo>();
    enemyController = GetComponent<EnemyController>();
    enemyAiController = GetComponent<EnemyAIController>();
    hurtBox = GetComponentInChildren<HurtBox2D>(includeInactive: true);
    hurtListener = HandleHit;
    ResolveHealthTextBindings();
    ResolveFacingInvariantHealthUi();
    healthText?.EnsureGlyphCapacity(HealthGlyphCapacity);
    maxHealthText?.EnsureGlyphCapacity(MaxHealthGlyphCapacity);
    EnsureDamageNumberPool();
  }

  void OnEnable() {
    ApplyFacingInvariantHealthUiScale();
    RegisterHurtBoxListener();
  }

  void LateUpdate() {
    ApplyFacingInvariantHealthUiScale();
  }

  void OnDisable() {
    deathCoroutine = null;
    fallbackHurtCoroutine = null;
    if (hurtBox != null && hurtListener != null) {
      hurtBox.OnHit.RemoveListener(hurtListener);
    }
    ResetComboProgress();
  }

  public void RefreshFromEnemyInfo(string source) {
    if (enemyInfo == null) {
      enemyInfo = GetComponent<EnemyInfo>();
    }

    ResetDeathState();
    if (isActiveAndEnabled) {
      RegisterHurtBoxListener();
    }
    if (enemyInfo == null) {
      return;
    }

    enemyInfo.ResetHealthFromResolvedStats();
    appliedSpawnContextVersion = enemyInfo.SpawnContextVersion;
    if (characterState == null) {
      characterState = SingleSceneManager.ResolveGameplayCharacterState();
    }
    RefreshAbilityXpSource();
    UpdateVisuals();

    if (ShouldLogDebug()) {
      RuntimeLog.Log(
        "[EnemyHealth][Refresh]" +
        " source='" + (source ?? "") + "'" +
        " object='" + gameObject.name + "'" +
        " enemy_type='" + enemyInfo.enemyType + "'" +
        " max_hp=" + enemyInfo.ResolveMaxHp().ToDisplayString() +
        " current_hp=" + enemyInfo.currentHp.ToDisplayString()
      );
    }
  }

  void RegisterHurtBoxListener() {
    if (hurtBox == null) {
      hurtBox = GetComponentInChildren<HurtBox2D>(includeInactive: true);
    }
    if (hurtBox == null) {
      Debug.LogWarning("[EnemyHealth] Missing HurtBox2D on '" + gameObject.name + "'.");
      return;
    }

    if (hurtListener == null) {
      hurtListener = HandleHit;
    }
    hurtBox.launchRandomOnHit = false;
    hurtBox.OnHit.RemoveListener(hurtListener);
    hurtBox.OnHit.AddListener(hurtListener);
  }

  void HandleHit(HitBox2D hitBox) {
    if (hitBox == null || deathInProgress) {
      return;
    }

    EnsureSpawnContextApplied();
    if (hitBox.IsEnemyOwned) {
      return;
    }

    var defenderStats = enemyInfo != null ? enemyInfo.ResolvedStats : null;
    var abilityRawDamage = EsperanzaAbilities.GetRawDamage(hitBox.hitId);
    var abilityDamageMultiplier = EsperanzaAbilities.GetDamageMultiplier(hitBox.hitId);
    var isComboHit = TryResolveComboHit(
      hitBox.hitId,
      out var comboIndex,
      out var comboHitNumber,
      out var comboContinues
    );
    var comboDamageMultiplier = isComboHit ? comboHitNumber : 1;
    var damageResult = CombatDamageResolver.ResolveEsperanzaHit(
      AllStatValues.Esperanza,
      defenderStats,
      abilityRawDamage,
      abilityDamageMultiplier * comboDamageMultiplier
    );
    if (damageResult.amount != null && damageResult.amount.IsPositive) {
      if (isComboHit) {
        CommitComboHit(comboIndex, comboHitNumber, comboContinues);
        ComboHitCameraZoom.Play(comboHitNumber);
      }
      HitEmphasisBurst.Play(hurtBox, hitBox, isComboHit ? comboHitNumber : 0);
    }
    ApplyDamage(
      damageResult,
      hitBox,
      abilityRawDamage,
      isComboHit ? comboHitNumber : 0,
      comboContinues
    );
  }

  bool TryResolveComboHit(
    string hitId,
    out int comboIndex,
    out int comboHitNumber,
    out bool comboContinues
  ) {
    comboIndex = -1;
    comboHitNumber = 0;
    comboContinues = false;
    if (!EsperanzaAbilities.TryResolveAbilityAnimation(hitId, out var abilityAnimation)) {
      ResetComboProgress();
      return false;
    }

    var formName = EsperanzaForms.GetActive();
    var now = TimeScale.GetNow(this);
    if (activeComboIndex >= 0 &&
        now <= comboExpiresAt &&
        nextComboMoveIndex > 0 &&
        nextComboMoveIndex < EsperanzaComboLoadouts.MovesPerCombo &&
        string.Equals(
          EsperanzaComboLoadouts.GetMove(formName, activeComboIndex, nextComboMoveIndex),
          abilityAnimation,
          System.StringComparison.OrdinalIgnoreCase
        )) {
      comboIndex = activeComboIndex;
      comboHitNumber = nextComboMoveIndex + 1;
      comboContinues = comboHitNumber < EsperanzaComboLoadouts.MovesPerCombo;
      return true;
    }

    ResetComboProgress();
    for (var candidate = 0; candidate < EsperanzaComboLoadouts.ComboCount; candidate++) {
      if (!string.Equals(
            EsperanzaComboLoadouts.GetMove(formName, candidate, 0),
            abilityAnimation,
            System.StringComparison.OrdinalIgnoreCase
          )) {
        continue;
      }

      comboIndex = candidate;
      comboHitNumber = 1;
      comboContinues = EsperanzaComboLoadouts.MovesPerCombo > 1;
      return true;
    }
    return false;
  }

  void CommitComboHit(int comboIndex, int comboHitNumber, bool comboContinues) {
    if (!comboContinues) {
      ResetComboProgress();
      return;
    }

    activeComboIndex = comboIndex;
    nextComboMoveIndex = comboHitNumber;
    comboExpiresAt = TimeScale.GetNow(this) + Mathf.Max(comboContinuationSeconds, 0.1f);
  }

  void ResetComboProgress() {
    activeComboIndex = -1;
    nextComboMoveIndex = 0;
    comboExpiresAt = 0f;
  }

  void GrantAbilityHitXp(string abilityName, EndlessNumber actualDamage) {
    if (!EsperanzaAbilities.TryResolveAbilityAnimation(abilityName, out var animationName)) {
      return;
    }

    var xpAmount = actualDamage != null ? actualDamage.ToInt32Clamped() : 0;
    if (xpAmount <= 0) {
      return;
    }

    if (characterState == null) {
      characterState = SingleSceneManager.ResolveGameplayCharacterState();
    }
    if (characterState == null) {
      return;
    }

    RefreshAbilityXpSource();

    characterState.GrantAbilityXp(
      animationName,
      xpAmount,
      abilityXpSource
    );
  }

  void GrantFormKillXp(EndlessNumber maxHp) {
    var xpAmount = maxHp != null ? maxHp.ToInt32Clamped() : 0;
    if (xpAmount <= 0) {
      return;
    }

    if (characterState == null) {
      characterState = SingleSceneManager.ResolveGameplayCharacterState();
    }
    if (characterState == null) {
      return;
    }

    characterState.GrantActiveFormXp(
      xpAmount,
      "enemy_kill:" + (enemyInfo != null ? enemyInfo.enemyType : "")
    );
  }

  void RefreshAbilityXpSource() {
    var enemyType = enemyInfo != null ? enemyInfo.enemyType : "";
    if (string.Equals(abilityXpEnemyType, enemyType, System.StringComparison.Ordinal)) {
      return;
    }

    abilityXpEnemyType = enemyType;
    if (!abilityXpSourceByEnemyType.TryGetValue(enemyType, out abilityXpSource)) {
      abilityXpSource = "enemy_hit:" + enemyType;
      abilityXpSourceByEnemyType[enemyType] = abilityXpSource;
    }
  }

  void EnsureSpawnContextApplied() {
    if (enemyInfo == null) {
      enemyInfo = GetComponent<EnemyInfo>();
    }
    if (enemyInfo == null) {
      return;
    }

    if (appliedSpawnContextVersion == enemyInfo.SpawnContextVersion) {
      return;
    }

    RefreshFromEnemyInfo("spawn_context_changed");
  }

  void ApplyDamage(
    CombatDamageResult damageResult,
    HitBox2D hitBox,
    int abilityRawDamage,
    int comboHitNumber,
    bool comboContinues
  ) {
    if (enemyInfo == null) {
      return;
    }

    var hitId = hitBox != null ? hitBox.hitId : "";

    var maxHp = enemyInfo.ResolveMaxHp();
    var hpBefore = EndlessNumber.Min(
      EndlessNumber.Max(enemyInfo.currentHp ?? new EndlessNumber(), new EndlessNumber()),
      maxHp
    );
    var remainingHp = hpBefore - (damageResult.amount ?? new EndlessNumber());
    enemyInfo.currentHp.Set(EndlessNumber.Max(remainingHp, new EndlessNumber()));
    var actualDamage = hpBefore - enemyInfo.currentHp;

    GrantAbilityHitXp(hitId, actualDamage);
    ScreenShake.Play(actualDamage, maxHp, screenShakeFactor);

    SpawnDamageNumber(damageResult.amount, damageResult.kind);
    UpdateVisuals();

    if (ShouldLogDebug()) {
      RuntimeLog.Log(
        "[EnemyHealth][ApplyDamage]" +
        " object='" + gameObject.name + "'" +
        " enemy_type='" + enemyInfo.enemyType + "'" +
        " damage_kind='" + damageResult.kind + "'" +
        " hit_id='" + (hitId ?? "") + "'" +
        " combo_hit=" + comboHitNumber +
        " combo_multiplier=" + (comboHitNumber > 0 ? comboHitNumber : 1) +
        " ability_damage=" + abilityRawDamage +
        " flat_damage=" + damageResult.flatDamage.ToDisplayString() +
        " ability_multiplier=" + damageResult.abilityDamageMultiplier.ToString("0.###") +
        " range_multiplier=" + damageResult.damageRangeMultiplier.ToString("0.###") +
        " base_damage=" + damageResult.baseDamage.ToDisplayString() +
        " armor_before_pen=" + damageResult.armorBeforePenetration.ToDisplayString() +
        " penetration_applied=" + damageResult.penetrationApplied.ToDisplayString() +
        " armor_applied=" + damageResult.armorApplied.ToDisplayString() +
        " evade_chance=" + damageResult.evadeChance.ToString("0.###") +
        " evade_roll=" + damageResult.evadeRoll.ToString("0.###") +
        " evaded=" + (damageResult.evaded ? 1 : 0) +
        " damage=" + damageResult.amount.ToDisplayString() +
        " cchc=" + damageResult.criticalChance.ToString("0.###") +
        " croll=" + damageResult.criticalRoll.ToString("0.###") +
        " lchc=" + damageResult.luckyChance.ToString("0.###") +
        " lroll=" + damageResult.luckyRoll.ToString("0.###") +
        " dchc=" + damageResult.directChance.ToString("0.###") +
        " droll=" + damageResult.directRoll.ToString("0.###") +
        " hp_remaining=" + enemyInfo.currentHp.ToDisplayString() +
        " hp_max=" + maxHp.ToDisplayString()
      );
    }

    if (enemyInfo != null && string.Equals(enemyInfo.enemyType, "Imp", System.StringComparison.OrdinalIgnoreCase)) {
      Debug.Log("[EnemyHealth] Imp was hit! Calling SoundEffectPlayer.Play(\"enemy.imp.hurt\")");
      if (enemyController == null || EnemyAudioLimiter.IsEligibleForAudio(enemyController)) {
        SoundEffectPlayer.Play("enemy.imp.hurt");
      } else {
        Debug.Log("[EnemyHealth] Audio blocked by EnemyAudioLimiter.");
      }
    }

    if (enemyInfo.currentHp.IsPositive) {
      if (actualDamage.IsPositive) {
        if (comboHitNumber > 0 && comboContinues) {
          BeginComboJuggle(hitBox);
        } else {
          BeginHurtReaction();
        }
      }
      return;
    }

    GrantFormKillXp(maxHp);
    BeginDeath(hitId);
  }

  void BeginComboJuggle(HitBox2D hitBox) {
    if (enemyAiController == null) {
      enemyAiController = GetComponent<EnemyAIController>();
    }
    var attacker = hitBox != null ? hitBox.ActorOwner : null;
    if (enemyAiController != null && enemyAiController.TryBeginComboJuggle(
          attacker,
          comboJuggleHorizontalOffset,
          comboJuggleVerticalOffset,
          comboContinuationSeconds
        )) {
      CancelFallbackHurtReaction();
      return;
    }

    BeginHurtReaction();
  }

  void BeginHurtReaction() {
    if (enemyAiController == null) {
      enemyAiController = GetComponent<EnemyAIController>();
    }
    if (enemyAiController != null && enemyAiController.TryPlayHurtReaction()) {
      CancelFallbackHurtReaction();
      return;
    }

    if (enemyController == null) {
      enemyController = GetComponent<EnemyController>();
    }
    if (enemyController == null || !enemyController.PlayAnimation("Hurt", forceRestart: true)) {
      return;
    }

    CancelFallbackHurtReaction();
    var durationSeconds = enemyController.GetAnimationDurationSeconds("Hurt");
    if (durationSeconds <= 0f) {
      durationSeconds = 0.175f;
    }
    fallbackHurtCoroutine = StartCoroutine(CompleteFallbackHurtReaction(durationSeconds));
  }

  System.Collections.IEnumerator CompleteFallbackHurtReaction(float durationSeconds) {
    yield return TimeScale.WaitForSecondsScaled(durationSeconds, this);
    fallbackHurtCoroutine = null;

    if (deathInProgress || enemyInfo == null || !enemyInfo.currentHp.IsPositive) {
      yield break;
    }

    enemyController?.PlayAnimation(enemyController.defaultAnimation, forceRestart: true);
  }

  void CancelFallbackHurtReaction() {
    if (fallbackHurtCoroutine == null) {
      return;
    }

    StopCoroutine(fallbackHurtCoroutine);
    fallbackHurtCoroutine = null;
  }

  Pool damageNumberPool;
  GameObject damageNumberPoolPrefab;

  void SpawnDamageNumber(EndlessNumber amount, CombatDamageKind damageKind) {
    if (damageNumberPrefab == null || amount == null || !amount.IsPositive) return;

    EnsureDamageNumberPool();
    if (damageNumberPool == null) {
      return;
    }

    var dmgObj = damageNumberPool.Acquire(ResolveDamageNumberSpawnPosition(), Quaternion.identity);
    if (dmgObj == null) {
      return;
    }

    if (!damageTextByObject.TryGetValue(dmgObj, out var fontText) || fontText == null) {
      fontText = dmgObj.GetComponentInChildren<FontText>();
      fontText?.EnsureGlyphCapacity(DamageNumberGlyphCapacity);
      damageTextByObject[dmgObj] = fontText;
    }
    if (fontText != null) {
      fontText.content = FormatDamageAmount(amount);
    }

    if (!damageMotionByObject.TryGetValue(dmgObj, out var motion) || motion == null) {
      motion = dmgObj.GetComponent<DamageNumberArcMotion>();
      if (motion == null) {
        motion = dmgObj.AddComponent<DamageNumberArcMotion>();
      }
      damageMotionByObject[dmgObj] = motion;
    }
    motion.Play(DamageNumberLifetimeSeconds);

    damageNumberPool.Activate(dmgObj);
    motion.SetMainColor(fontText, CombatNumberPalette.ResolveDamage(damageKind));
    damageNumberPool.DespawnAfter(dmgObj, DamageNumberLifetimeSeconds);
  }

  Vector3 ResolveDamageNumberSpawnPosition() {
    if (healthBarStretch == null) {
      return transform.position;
    }

    var healthBarRenderer = healthBarStretch.GetComponent<SpriteRenderer>();
    if (healthBarRenderer != null && healthBarRenderer.sprite != null) {
      return healthBarRenderer.bounds.center;
    }

    return healthBarStretch.transform.position;
  }

  void EnsureDamageNumberPool() {
    if (damageNumberPrefab == null) {
      return;
    }
    if (damageNumberPool != null && damageNumberPoolPrefab == damageNumberPrefab) {
      return;
    }

    damageNumberPoolPrefab = damageNumberPrefab;
    damageNumberPool = Pool.GetShared(
      damageNumberPrefab,
      null,
      SharedDamageNumberPoolSize,
      false
    );
  }

  void UpdateVisuals() {
    if (enemyInfo == null) return;

    ResolveHealthTextBindings();

    var maxHp = enemyInfo.ResolveMaxHp();
    var currentHpChanged = displayedCurrentHp == null || displayedCurrentHp != enemyInfo.currentHp;
    var maxHpChanged = displayedMaxHp == null || displayedMaxHp != maxHp;
    displayedCurrentHp = enemyInfo.currentHp.Copy();
    displayedMaxHp = maxHp.Copy();

    if ((currentHpChanged || maxHpChanged) && healthText != null) {
      healthText.content = FormatHealthAmount(
        enemyInfo.currentHp,
        maxHp,
        slashPrefixed: false
      );
      healthText.Generate();
    }

    if (maxHpChanged && maxHealthText != null) {
      maxHealthText.content = FormatHealthAmount(maxHp, maxHp, slashPrefixed: true);
      maxHealthText.Generate();
    }

    if ((currentHpChanged || maxHpChanged) && healthBarStretch != null) {
      var healthPercent = maxHp.IsPositive
        ? Mathf.Clamp01((float)enemyInfo.currentHp.RatioTo(maxHp)) * 100f
        : 0f;
      healthBarStretch.stretchPercent = new Vector2(
        healthPercent,
        healthBarStretch.stretchPercent.y
      );
      healthBarStretch.RefreshStretch();
    }
  }

  static string FormatDamageAmount(EndlessNumber amount) {
    return amount != null && amount.IsPositive
      ? amount.ToGlyphString()
      : IntegerTextCache.Get(0);
  }

  static string FormatHealthAmount(
    EndlessNumber amount,
    EndlessNumber maximumAmount,
    bool slashPrefixed
  ) {
    var formattedAmount = IntegerTextCache.Get(0);
    if (amount != null && amount.IsPositive) {
      var amountSuffix = amount.CompactSuffix;
      var maximumSuffix = maximumAmount != null ? maximumAmount.CompactSuffix : "";
      var sharesMaximumSuffix = !slashPrefixed &&
                                !string.IsNullOrEmpty(maximumSuffix) &&
                                string.Equals(
                                  amountSuffix,
                                  maximumSuffix,
                                  System.StringComparison.Ordinal
                                );
      formattedAmount = sharesMaximumSuffix
        ? amount.ToCompactMantissaString()
        : amount.ToGlyphString();
    }
    return slashPrefixed ? "/" + formattedAmount : formattedAmount;
  }

  void ResolveHealthTextBindings() {
    if (healthText != null && maxHealthText != null) {
      return;
    }

    var healthRoot = FindDescendant(transform, "HEALTH");
    if (healthRoot == null) {
      return;
    }

    if (healthText == null) {
      healthText = ResolveFontText(healthRoot, "current");
    }

    if (maxHealthText == null) {
      maxHealthText = ResolveFontText(healthRoot, "max");
    }
  }

  static FontText ResolveFontText(Transform parent, string childName) {
    if (parent == null) {
      return null;
    }

    var child = parent.Find(childName);
    if (child == null) {
      return null;
    }

    return child.GetComponent<FontText>();
  }

  static Transform FindDescendant(Transform parent, string childName) {
    if (parent == null) {
      return null;
    }

    for (var i = 0; i < parent.childCount; i++) {
      var child = parent.GetChild(i);
      if (child.name == childName) {
        return child;
      }

      var match = FindDescendant(child, childName);
      if (match != null) {
        return match;
      }
    }

    return null;
  }

  void ResolveFacingInvariantHealthUi() {
    if (healthUiHierarchyResolved) {
      return;
    }

    healthUiHierarchyResolved = true;
    var healthBarRoot = ResolveDirectChildUnderEnemy(
      healthBarStretch != null ? healthBarStretch.transform : null
    );
    var healthNumbersRoot = ResolveDirectChildUnderEnemy(FindDescendant(transform, "HEALTH"));
    if (healthBarRoot == null && healthNumbersRoot == null) {
      return;
    }

    var container = new GameObject("FacingInvariantHealthUI");
    container.layer = gameObject.layer;
    facingInvariantHealthUiRoot = container.transform;
    facingInvariantHealthUiRoot.SetParent(transform, worldPositionStays: false);
    facingInvariantHealthUiRoot.localPosition = Vector3.zero;
    facingInvariantHealthUiRoot.localRotation = Quaternion.identity;
    facingInvariantHealthUiRoot.localScale = Vector3.one;

    if (healthBarRoot != null) {
      healthBarRoot.SetParent(facingInvariantHealthUiRoot, worldPositionStays: false);
    }
    if (healthNumbersRoot != null && healthNumbersRoot != healthBarRoot) {
      healthNumbersRoot.SetParent(facingInvariantHealthUiRoot, worldPositionStays: false);
    }

    ApplyFacingInvariantHealthUiScale();
  }

  Transform ResolveDirectChildUnderEnemy(Transform candidate) {
    while (candidate != null && candidate != transform) {
      if (candidate.parent == transform) {
        return candidate;
      }
      candidate = candidate.parent;
    }

    return null;
  }

  void ApplyFacingInvariantHealthUiScale() {
    if (!healthUiHierarchyResolved) {
      ResolveFacingInvariantHealthUi();
    }
    if (facingInvariantHealthUiRoot == null) {
      return;
    }

    var facingSign = transform.localScale.x < 0f ? -1f : 1f;
    var currentScale = facingInvariantHealthUiRoot.localScale;
    if (Mathf.Approximately(currentScale.x, facingSign)) {
      return;
    }

    currentScale.x = facingSign;
    facingInvariantHealthUiRoot.localScale = currentScale;
  }

  void BeginDeath(string finalHitId) {
    if (deathInProgress) {
      return;
    }

    deathInProgress = true;
    ResetComboProgress();
    CancelFallbackHurtReaction();
    DisableCombatForDeath();

    var damageSubtype = EsperanzaAbilities.ResolveDamageSubtype(finalHitId);
    if (enemyController == null) {
      enemyController = GetComponent<EnemyController>();
    }

    var deathAnimation = "";
    var durationSeconds = 0f;
    var animationPlayed = false;
    if (enemyController != null) {
      animationPlayed = enemyController.TryPlayDeathAnimation(
        damageSubtype,
        out deathAnimation,
        out durationSeconds
      );
    }

    if (ShouldLogDebug()) {
      RuntimeLog.Log(
        "[EnemyHealth][DeathAnimation]" +
        " object='" + gameObject.name + "'" +
        " enemy_type='" + (enemyInfo != null ? enemyInfo.enemyType : "") + "'" +
        " final_hit='" + (finalHitId ?? "") + "'" +
        " damage_subtype='" + (damageSubtype ?? "") + "'" +
        " animation='" + (deathAnimation ?? "") + "'" +
        " played=" + (animationPlayed ? 1 : 0)
      );
    }

    if (!animationPlayed || durationSeconds <= 0f) {
      CompleteDeath();
      return;
    }

    deathCoroutine = StartCoroutine(CompleteDeathAfterAnimation(durationSeconds));
  }

  void DisableCombatForDeath() {
    if (hurtBox != null) {
      hurtBox.OnHit.RemoveListener(HandleHit);
      hurtBox.enabled = false;
    }

    if (enemyAiController == null) {
      enemyAiController = GetComponent<EnemyAIController>();
    }
    if (enemyAiController == null) {
      return;
    }

    enemyAiWasEnabledBeforeDeath = enemyAiController.enabled;
    enemyAiController.enabled = false;
  }

  void ResetDeathState() {
    CancelFallbackHurtReaction();
    ResetComboProgress();
    if (deathCoroutine != null) {
      StopCoroutine(deathCoroutine);
      deathCoroutine = null;
    }

    if (hurtBox != null) {
      hurtBox.enabled = true;
    }

    if (deathInProgress && enemyAiController != null) {
      enemyAiController.enabled = enemyAiWasEnabledBeforeDeath;
    }

    deathInProgress = false;
  }

  System.Collections.IEnumerator CompleteDeathAfterAnimation(float durationSeconds) {
    yield return TimeScale.WaitForSecondsScaled(durationSeconds, this);
    deathCoroutine = null;
    CompleteDeath();
  }

  void CompleteDeath() {
    var defeatedEvent = new EnemyDefeatedEvent(
      enemyInfo != null ? enemyInfo.enemyType : "",
      LocationManager.currentLocation,
      gameObject
    );

    var destructionManager = GetComponent<DestructionManager>();
    if (destructionManager != null) {
      destructionManager.LaunchRandom();
    }

    HurtBloodSplatter.PlayDeathDecals(transform);
    
    LootSpawner.DropLoot(transform.position, enemyInfo != null ? enemyInfo.ResolveMaxHp().ToInt32Clamped() : 10);

    if (enemyInfo != null && string.Equals(enemyInfo.enemyType, "Imp", System.StringComparison.OrdinalIgnoreCase)) {
      if (enemyController == null || EnemyAudioLimiter.IsEligibleForAudio(enemyController)) {
        SoundEffectPlayer.Play("enemy.imp.death");
      }
    }

    if (ShouldLogDebug()) {
      RuntimeLog.Log(
        "[EnemyHealth][Death]" +
        " object='" + gameObject.name + "'" +
        " enemy_type='" + (enemyInfo != null ? enemyInfo.enemyType : "") + "'" +
        " reason='hp_depleted'"
      );
    }

    if (enemyInfo != null && enemyInfo.ownerSpawner != null) {
      enemyInfo.ownerSpawner.DespawnEnemy(gameObject);
    }
    else {
      Debug.LogWarning("[EnemyHealth] Missing owner spawner for '" + gameObject.name + "'. Disabling object directly.");
      gameObject.SetActive(false);
    }

    MessageBus.Send("enemy.defeated", defeatedEvent);
  }
}
