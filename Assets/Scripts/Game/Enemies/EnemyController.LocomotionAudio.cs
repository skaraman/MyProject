using System;
using UnityEngine;

public partial class EnemyController {
  const string ImpRunningRocksSoundId = "enemy.imp.running_rocks";

  string locomotionSoundLoopId;
  ProjectedSpriteShadowCaster2D locomotionGroundAnchor;

  void UpdateLocomotionSound() {
    var soundId = ResolveLocomotionSoundId();
    if (string.IsNullOrEmpty(soundId)) {
      StopLocomotionSound();
      return;
    }

    if (string.IsNullOrEmpty(locomotionSoundLoopId)) {
      locomotionSoundLoopId = "enemy.locomotion:" + ObjectEntityId.GetString(this);
    }

    SoundEffectPlayer.SetLoop(locomotionSoundLoopId, soundId);
  }

  string ResolveLocomotionSoundId() {
    if (!EnemyAudioLimiter.IsEligibleForAudio(this)) {
      return null;
    }

    if (!SingleSceneManager.IsGameplayActive ||
        !SingleSceneManager.IsBlackscreenFullyTransparent ||
        animationController == null ||
        !animationController.IsPlaying ||
        !string.Equals(enemyType, "Imp", StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(animationController.CurrentAnimation, "Run", StringComparison.Ordinal)) {
      return null;
    }

    if (!FootstepSurface.TryResolveDenotation(
          ResolveLocomotionGroundPosition(),
          out var surface
        ) ||
        !string.Equals(surface, "rocks", StringComparison.OrdinalIgnoreCase)) {
      return null;
    }

    return ImpRunningRocksSoundId;
  }

  Vector2 ResolveLocomotionGroundPosition() {
    if (locomotionGroundAnchor == null) {
      locomotionGroundAnchor = GetComponent<ProjectedSpriteShadowCaster2D>();
    }

    return locomotionGroundAnchor != null
      ? locomotionGroundAnchor.GroundPosition
      : (Vector2)transform.position;
  }

  void StopLocomotionSound() {
    if (!string.IsNullOrEmpty(locomotionSoundLoopId)) {
      SoundEffectPlayer.StopLoop(locomotionSoundLoopId);
    }
  }
}
