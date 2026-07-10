using UnityEngine;

[RequireComponent(typeof(EnemyInfo))]
public class EnemyHealth : MonoBehaviour {
  EnemyInfo enemyInfo;
  HurtBox2D hurtBox;
  int appliedSpawnContextVersion = -1;

  static bool ShouldLogDebug() {
    return SpriteStreamingRuntimeSettings.EnableVerboseRuntimeConsoleLogs &&
           (Application.isEditor || Debug.isDebugBuild);
  }

  void Awake() {
    enemyInfo = GetComponent<EnemyInfo>();
    hurtBox = GetComponentInChildren<HurtBox2D>(includeInactive: true);
  }

  void OnEnable() {
    RegisterHurtBoxListener();
  }

  void OnDisable() {
    if (hurtBox != null) {
      hurtBox.OnHit.RemoveListener(HandleHit);
    }
  }

  public void RefreshFromEnemyInfo(string source) {
    if (enemyInfo == null) {
      enemyInfo = GetComponent<EnemyInfo>();
    }

    RegisterHurtBoxListener();
    if (enemyInfo == null) {
      return;
    }

    enemyInfo.ResetHealthFromResolvedStats();
    appliedSpawnContextVersion = enemyInfo.SpawnContextVersion;

    if (ShouldLogDebug()) {
      Debug.Log(
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

    hurtBox.launchRandomOnHit = false;
    hurtBox.OnHit.RemoveListener(HandleHit);
    hurtBox.OnHit.AddListener(HandleHit);
  }

  void HandleHit(HitBox2D hitBox) {
    if (hitBox == null) {
      return;
    }

    EnsureSpawnContextApplied();
    if (hitBox.GetComponentInParent<EnemyInfo>() != null) {
      return;
    }

    var defenderStats = enemyInfo != null ? enemyInfo.ResolvedStats : null;
    var damageResult = CombatDamageResolver.ResolveEsperanzaHit(
      AllStatValues.Esperanza,
      defenderStats
    );
    ApplyDamage(damageResult, hitBox.hitId);
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

  void ApplyDamage(CombatDamageResult damageResult, string hitId) {
    if (enemyInfo == null) {
      return;
    }

    var maxHp = enemyInfo.ResolveMaxHp();
    enemyInfo.currentHp = Mathf.Clamp(enemyInfo.currentHp - damageResult.amount, 0f, maxHp);

    if (ShouldLogDebug()) {
      Debug.Log(
        "[EnemyHealth][ApplyDamage]" +
        " object='" + gameObject.name + "'" +
        " enemy_type='" + enemyInfo.enemyType + "'" +
        " damage_kind='" + damageResult.kind + "'" +
        " hit_id='" + (hitId ?? "") + "'" +
        " base_damage=" + damageResult.baseDamage.ToString("0.###") +
        " armor_applied=" + damageResult.armorApplied.ToString("0.###") +
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

    DespawnAfterDeath();
  }

  void DespawnAfterDeath() {
    MessageBus.Send(
      "enemy.defeated",
      new EnemyDefeatedEvent(
        enemyInfo != null ? enemyInfo.enemyType : "",
        LocationManager.currentLocation,
        gameObject
      )
    );

    if (ShouldLogDebug()) {
      Debug.Log(
        "[EnemyHealth][Death]" +
        " object='" + gameObject.name + "'" +
        " enemy_type='" + (enemyInfo != null ? enemyInfo.enemyType : "") + "'" +
        " reason='hp_depleted'"
      );
    }

    if (enemyInfo != null && enemyInfo.ownerSpawner != null) {
      enemyInfo.ownerSpawner.DespawnEnemy(gameObject);
      return;
    }

    Debug.LogWarning("[EnemyHealth] Missing owner spawner for '" + gameObject.name + "'. Disabling object directly.");
    gameObject.SetActive(false);
  }
}
