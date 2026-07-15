using UnityEngine;

[RequireComponent(typeof(EnemyInfo))]
public class EnemyHealth : MonoBehaviour {
  const int SharedDamageNumberPoolSize = 24;
  const float DamageNumberLifetimeSeconds = 1.5f;
  const int HealthGlyphCapacity = 4;
  const int MaxHealthGlyphCapacity = 5;

  static readonly System.Collections.Generic.Dictionary<GameObject, FontText> damageTextByObject =
    new(SharedDamageNumberPoolSize);
  static readonly System.Collections.Generic.Dictionary<string, string> abilityXpSourceByEnemyType =
    new(System.StringComparer.Ordinal);

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetRuntimeCaches() {
    UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= ClearSceneObjectCaches;
    damageTextByObject.Clear();
    abilityXpSourceByEnemyType.Clear();
    UnityEngine.SceneManagement.SceneManager.sceneUnloaded += ClearSceneObjectCaches;
  }

  static void ClearSceneObjectCaches(UnityEngine.SceneManagement.Scene scene) {
    damageTextByObject.Clear();
  }

  EnemyInfo enemyInfo;
  HurtBox2D hurtBox;
  EnemyController enemyController;
  EnemyAIController enemyAiController;
  int appliedSpawnContextVersion = -1;
  bool deathInProgress;
  bool enemyAiWasEnabledBeforeDeath;
  Coroutine deathCoroutine;
  float displayedCurrentHp = float.NaN;
  float displayedMaxHp = float.NaN;
  CharacterState characterState;
  string abilityXpEnemyType;
  string abilityXpSource;
  UnityEngine.Events.UnityAction<HitBox2D> hurtListener;

  [Header("Visual Feedback")]
  [Tooltip("Prefab to spawn for showing damage numbers")]
  public GameObject damageNumberPrefab;

  [Tooltip("Stretch component used to scale the health bar horizontally")]
  public AnchoredSpriteStretch healthBarStretch;

  [Tooltip("FontText to display current health text")]
  public FontText healthText;

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
    healthText?.EnsureGlyphCapacity(HealthGlyphCapacity);
    maxHealthText?.EnsureGlyphCapacity(MaxHealthGlyphCapacity);
    EnsureDamageNumberPool();
  }

  void OnEnable() {
    RegisterHurtBoxListener();
  }

  void OnDisable() {
    deathCoroutine = null;
    if (hurtBox != null && hurtListener != null) {
      hurtBox.OnHit.RemoveListener(hurtListener);
    }
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
        " max_hp=" + enemyInfo.ResolveMaxHp().ToString("0.###") +
        " current_hp=" + enemyInfo.currentHp.ToString("0.###")
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
    var damageResult = CombatDamageResolver.ResolveEsperanzaHit(
      AllStatValues.Esperanza,
      defenderStats,
      abilityRawDamage
    );
    GrantAbilityHitXp(hitBox.hitId);
    ApplyDamage(damageResult, hitBox.hitId, abilityRawDamage);
  }

  void GrantAbilityHitXp(string abilityName) {
    if (!EsperanzaAbilities.TryResolveAbilityAnimation(abilityName, out var animationName)) {
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
      1,
      abilityXpSource
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

  void ApplyDamage(CombatDamageResult damageResult, string hitId, float abilityRawDamage) {
    if (enemyInfo == null) {
      return;
    }

    var maxHp = enemyInfo.ResolveMaxHp();
    enemyInfo.currentHp = Mathf.Clamp(enemyInfo.currentHp - damageResult.amount, 0f, maxHp);

    SpawnDamageNumber(damageResult.amount);
    UpdateVisuals();

    if (ShouldLogDebug()) {
      RuntimeLog.Log(
        "[EnemyHealth][ApplyDamage]" +
        " object='" + gameObject.name + "'" +
        " enemy_type='" + enemyInfo.enemyType + "'" +
        " damage_kind='" + damageResult.kind + "'" +
        " hit_id='" + (hitId ?? "") + "'" +
        " ability_damage=" + abilityRawDamage.ToString("0.###") +
        " base_damage=" + damageResult.baseDamage.ToString("0.###") +
        " armor_before_pen=" + damageResult.armorBeforePenetration.ToString("0.###") +
        " penetration_applied=" + damageResult.penetrationApplied.ToString("0.###") +
        " armor_applied=" + damageResult.armorApplied.ToString("0.###") +
        " evade_chance=" + damageResult.evadeChance.ToString("0.###") +
        " evade_roll=" + damageResult.evadeRoll.ToString("0.###") +
        " evaded=" + (damageResult.evaded ? 1 : 0) +
        " damage=" + damageResult.amount.ToString("0.###") +
        " cchc=" + damageResult.criticalChance.ToString("0.###") +
        " croll=" + damageResult.criticalRoll.ToString("0.###") +
        " lchc=" + damageResult.luckyChance.ToString("0.###") +
        " lroll=" + damageResult.luckyRoll.ToString("0.###") +
        " dchc=" + damageResult.directChance.ToString("0.###") +
        " droll=" + damageResult.directRoll.ToString("0.###") +
        " hp_remaining=" + enemyInfo.currentHp.ToString("0.###") +
        " hp_max=" + maxHp.ToString("0.###")
      );
    }

    if (enemyInfo.currentHp > 0f) {
      return;
    }

    BeginDeath(hitId);
  }

  Pool damageNumberPool;
  GameObject damageNumberPoolPrefab;

  void SpawnDamageNumber(float amount) {
    if (damageNumberPrefab == null || amount <= 0f) return;

    EnsureDamageNumberPool();
    if (damageNumberPool == null) {
      return;
    }

    var dmgObj = damageNumberPool.Acquire(transform.position, Quaternion.identity);
    if (dmgObj == null) {
      return;
    }

    if (!damageTextByObject.TryGetValue(dmgObj, out var fontText) || fontText == null) {
      fontText = dmgObj.GetComponentInChildren<FontText>();
      damageTextByObject[dmgObj] = fontText;
    }
    if (fontText != null) {
      fontText.content = IntegerTextCache.Get(Mathf.RoundToInt(amount));
    }

    damageNumberPool.Activate(dmgObj);
    damageNumberPool.DespawnAfter(dmgObj, DamageNumberLifetimeSeconds);
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
    var currentHpChanged = !Mathf.Approximately(displayedCurrentHp, enemyInfo.currentHp);
    var maxHpChanged = !Mathf.Approximately(displayedMaxHp, maxHp);
    displayedCurrentHp = enemyInfo.currentHp;
    displayedMaxHp = maxHp;

    if (currentHpChanged && healthText != null) {
      var displayedHp = Mathf.CeilToInt(Mathf.Max(enemyInfo.currentHp, 0f));
      healthText.content = IntegerTextCache.Get(displayedHp);
    }

    if (maxHpChanged && maxHealthText != null) {
      var displayedMax = Mathf.CeilToInt(Mathf.Max(maxHp, 0f));
      maxHealthText.content = IntegerTextCache.GetSlashPrefixed(displayedMax);
    }

    if ((currentHpChanged || maxHpChanged) && healthBarStretch != null) {
      healthBarStretch.stretchPercent.x = maxHp > 0f ? (enemyInfo.currentHp / maxHp) * 100f : 0f;
    }
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

  void BeginDeath(string finalHitId) {
    if (deathInProgress) {
      return;
    }

    deathInProgress = true;
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
