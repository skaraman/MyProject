using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(HurtBox2D))]
public sealed class EsperanzaHurtAudio : MonoBehaviour {
  const string HurtSound1Id = "esperanza.hurt1";
  const string HurtSound2Id = "esperanza.hurt2";

  HurtBox2D hurtBox;
  UnityAction<HitBox2D> hitListener;

  void Awake() {
    hurtBox = GetComponent<HurtBox2D>();
    hitListener = OnHit;
  }

  void OnEnable() {
    if (hurtBox == null) {
      hurtBox = GetComponent<HurtBox2D>();
    }
    if (hitListener == null) {
      hitListener = OnHit;
    }

    hurtBox?.OnHit.AddListener(hitListener);
  }

  void OnDisable() {
    if (hurtBox != null && hitListener != null) {
      hurtBox.OnHit.RemoveListener(hitListener);
    }
  }

  void OnHit(HitBox2D hitBox) {
    if (hitBox == null || !hitBox.IsEnemyOwned) {
      return;
    }

    SoundEffectPlayer.Play(Random.value < 0.5f ? HurtSound1Id : HurtSound2Id);
  }
}
