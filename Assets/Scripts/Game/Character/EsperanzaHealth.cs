using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
[RequireComponent(typeof(HurtBox2D))]
public sealed class EsperanzaHealth : MonoBehaviour {
  const string HurtAnimation = "Hurt";
  const int SharedDamageNumberPoolSize = 24;
  const float DamageNumberLifetimeSeconds = 1.5f;
  const int DamageNumberGlyphCapacity = 7;

  HurtBox2D hurtBox;
  CharacterState characterState;
  GearController gearController;
  UnityAction<HitBox2D> hitListener;
  Pool damageNumberPool;
  GameObject damageNumberPoolPrefab;

  [Header("Visual Feedback")]
  [SerializeField, Tooltip("Prefab used to show red damage numbers over ESPER.")]
  GameObject damageNumberPrefab;

  [SerializeField, Min(0f), Tooltip("Camera displacement when one hit removes 100% of ESPER's max health. Actual shake scales with the percentage removed.")]
  float screenShakeFactor = 0.65f;

  static bool ShouldLogCombatDebug() {
    return SpriteStreamingRuntimeSettings.EnableVerboseRuntimeConsoleLogs &&
           (Application.isEditor || Debug.isDebugBuild);
  }

  void Awake() {
    hurtBox = GetComponent<HurtBox2D>();
    hitListener = HandleHit;
    ResolveCharacterState();
    ResolveGearController();
  }

  void OnEnable() {
    hurtBox ??= GetComponent<HurtBox2D>();
    hitListener ??= HandleHit;
    hurtBox?.OnHit.RemoveListener(hitListener);
    hurtBox?.OnHit.AddListener(hitListener);
  }

  void OnDisable() {
    if (hurtBox != null && hitListener != null) {
      hurtBox.OnHit.RemoveListener(hitListener);
    }
  }

  void HandleHit(HitBox2D hitBox) {
    if (hitBox == null || !hitBox.IsEnemyOwned) {
      return;
    }

    var enemyInfo = ResolveEnemyInfo(hitBox);
    var state = ResolveCharacterState();
    if (enemyInfo == null || state == null) {
      return;
    }

    var damageResult = CombatDamageResolver.ResolveEnemyDamage(
      enemyInfo.ResolvedStats,
      AllStatValues.Esperanza
    );
    var actualDamage = state.ApplyDamage(damageResult.amount, hitBox.hitId);
    var currentHealth = state.CurrentHealth;
    var maximumHealth = state.MaximumHealth;
    if (actualDamage != null && actualDamage.IsPositive) {
      HitEmphasisBurst.Play(hurtBox, hitBox);
      ScreenShake.Play(actualDamage, maximumHealth, screenShakeFactor);
      ResolveGearController()?.ApplyGearHitDamage(actualDamage, maximumHealth);
    }
    SpawnDamageNumber(actualDamage);
    ResolveGearController()?.TryPlayAnimation(
      HurtAnimation,
      forceRestart: true,
      resolveInterrupts: false
    );

    if (ShouldLogCombatDebug()) {
      RuntimeLog.Log(
        "[EsperanzaHealth][EnemyHit]" +
        " enemy_type='" + (enemyInfo.enemyType ?? "") + "'" +
        " hit_id='" + (hitBox.hitId ?? "") + "'" +
        " evaded=" + (damageResult.evaded ? 1 : 0) +
        " damage=" + actualDamage.ToDisplayString() +
        " hp_remaining=" + currentHealth.ToDisplayString() +
        " hp_max=" + maximumHealth.ToDisplayString()
      );
    }
  }

  void SpawnDamageNumber(EndlessNumber amount) {
    if (damageNumberPrefab == null || amount == null || !amount.IsPositive) {
      return;
    }

    EnsureDamageNumberPool();
    if (damageNumberPool == null) {
      return;
    }

    var damageObject = damageNumberPool.Acquire(ResolveDamageNumberSpawnPosition(), Quaternion.identity);
    if (damageObject == null) {
      return;
    }

    var fontText = damageObject.GetComponentInChildren<FontText>(includeInactive: true);
    if (fontText != null) {
      fontText.EnsureGlyphCapacity(DamageNumberGlyphCapacity);
      fontText.content = amount.ToGlyphString();
    }

    var motion = damageObject.GetComponent<DamageNumberArcMotion>();
    if (motion == null) {
      motion = damageObject.AddComponent<DamageNumberArcMotion>();
    }
    motion.Play(DamageNumberLifetimeSeconds);

    damageNumberPool.Activate(damageObject);
    motion.SetMainColor(fontText, CombatNumberPalette.PlayerDamage);
    damageNumberPool.DespawnAfter(damageObject, DamageNumberLifetimeSeconds);
  }

  Vector3 ResolveDamageNumberSpawnPosition() {
    var hurtCollider = hurtBox != null ? hurtBox.GetComponent<Collider2D>() : null;
    return hurtCollider != null ? hurtCollider.bounds.center : transform.position;
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

  GearController ResolveGearController() {
    if (gearController != null) {
      return gearController;
    }

    gearController = GetComponentInParent<GearController>();
    if (gearController == null) {
      gearController = SingleSceneManager.ResolveGameplayPlayerController();
    }
    return gearController;
  }

  static EnemyInfo ResolveEnemyInfo(HitBox2D hitBox) {
    if (hitBox == null) {
      return null;
    }

    if (hitBox.ActorOwner != null) {
      var ownerInfo = hitBox.ActorOwner.GetComponent<EnemyInfo>();
      if (ownerInfo != null) {
        return ownerInfo;
      }
    }

    return hitBox.GetComponentInParent<EnemyInfo>();
  }
}
