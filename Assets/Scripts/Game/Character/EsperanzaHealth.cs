using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
[RequireComponent(typeof(HurtBox2D))]
public sealed class EsperanzaHealth : MonoBehaviour {
  const string HurtAnimation = "Hurt";

  HurtBox2D hurtBox;
  CharacterState characterState;
  GearController gearController;
  UnityAction<HitBox2D> hitListener;

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
    MessageBus.Send(
      CharacterMessageTopics.HitReceived,
      new CharacterDamageEvent(actualDamage, currentHealth, maximumHealth, hitBox.hitId)
    );
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
