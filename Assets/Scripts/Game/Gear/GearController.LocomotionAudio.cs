using System;
using UnityEngine;

public partial class GearController {
  const string RunFootstepSoundName = "footsteps";
  const string SprintFootstepSoundName = "footrun";
  const string IdleSoundName = "idle";
  const float IdleSoundPlayChance = 0.25f;

  string locomotionSoundLoopId;
  string idleSoundLoopId;
  string lastFootstepSurface;
  string runFootstepSurfaceSoundId;
  string sprintFootstepSurfaceSoundId;
  string idleSurfaceSoundId;
  ProjectedSpriteShadowCaster2D footstepGroundAnchor;

  void UpdateLocomotionSound() {
    if (animationController == null) {
      StopLocomotionSound();
      return;
    }

    var animation = animationController.CurrentAnimation;
    var isPlaying = animationController.IsPlaying;
    var surface = "";
    var hasSurface = isPlaying && FootstepSurface.TryResolveDenotation(
      ResolveFootstepGroundPosition(),
      out surface
    );
    var locomotionSoundId = ResolveLocomotionSoundId(animation, isPlaying, hasSurface, surface);
    var idleSoundId = ResolveIdleSoundId(animation, isPlaying, hasSurface, surface);
    if (string.IsNullOrEmpty(locomotionSoundLoopId)) {
      locomotionSoundLoopId = "esperanza.locomotion:" + ObjectEntityId.GetString(this);
    }
    if (string.IsNullOrEmpty(idleSoundLoopId)) {
      idleSoundLoopId = "esperanza.idle:" + ObjectEntityId.GetString(this);
    }

    SoundEffectPlayer.SetLoop(locomotionSoundLoopId, locomotionSoundId);
    SoundEffectPlayer.SetIntermittentLoop(
      idleSoundLoopId,
      idleSoundId,
      IdleSoundPlayChance
    );
  }

  string ResolveLocomotionSoundId(
    string animation,
    bool isPlaying,
    bool hasSurface,
    string surface
  ) {
    if (!isPlaying || !hasSurface) {
      return null;
    }

    if (string.Equals(animation, "Run", StringComparison.Ordinal)) {
      return ResolveSurfaceSoundId(RunFootstepSoundName, surface);
    }

    if (string.Equals(animation, "Sprint", StringComparison.Ordinal)) {
      return ResolveSurfaceSoundId(SprintFootstepSoundName, surface);
    }

    return null;
  }

  string ResolveIdleSoundId(
    string animation,
    bool isPlaying,
    bool hasSurface,
    string surface
  ) {
    if (!isPlaying) {
      return null;
    }

    if (!IsIdleSoundAnimation(animation)) {
      return null;
    }

    if (!hasSurface) {
      return null;
    }

    return ResolveSurfaceSoundId(IdleSoundName, surface);
  }

  static bool IsIdleSoundAnimation(string animation) {
    return string.Equals(animation, "Breathe", StringComparison.Ordinal) ||
           string.Equals(animation, "Stance", StringComparison.Ordinal) ||
           string.Equals(animation, "BreatheToWalk", StringComparison.Ordinal) ||
           string.Equals(animation, "BreatheToRun", StringComparison.Ordinal) ||
           string.Equals(animation, "BreatheToSprint", StringComparison.Ordinal) ||
           string.Equals(animation, "StanceToBreathe", StringComparison.Ordinal) ||
           string.Equals(animation, "StanceToWalk", StringComparison.Ordinal) ||
           string.Equals(animation, "StanceToRun", StringComparison.Ordinal) ||
           string.Equals(animation, "StanceToSprint", StringComparison.Ordinal) ||
           string.Equals(animation, "WalkToBreathe", StringComparison.Ordinal) ||
           string.Equals(animation, "RunToBreathe", StringComparison.Ordinal) ||
           string.Equals(animation, "SprintToBreathe", StringComparison.Ordinal);
  }

  string ResolveSurfaceSoundId(string soundName, string surface) {
    if (string.IsNullOrWhiteSpace(surface)) {
      return null;
    }

    if (!string.Equals(lastFootstepSurface, surface, StringComparison.Ordinal)) {
      lastFootstepSurface = surface;
      runFootstepSurfaceSoundId = RunFootstepSoundName + "_" + surface;
      sprintFootstepSurfaceSoundId = SprintFootstepSoundName + "_" + surface;
      idleSurfaceSoundId = IdleSoundName + "_" + surface;
    }

    if (string.Equals(soundName, RunFootstepSoundName, StringComparison.Ordinal)) {
      return runFootstepSurfaceSoundId;
    }

    if (string.Equals(soundName, SprintFootstepSoundName, StringComparison.Ordinal)) {
      return sprintFootstepSurfaceSoundId;
    }

    return idleSurfaceSoundId;
  }

  Vector2 ResolveFootstepGroundPosition() {
    if (footstepGroundAnchor == null) {
      footstepGroundAnchor = GetComponent<ProjectedSpriteShadowCaster2D>();
    }

    if (footstepGroundAnchor != null) {
      return footstepGroundAnchor.GroundPosition;
    }

    return transform.position;
  }

  void StopLocomotionSound() {
    if (string.IsNullOrEmpty(locomotionSoundLoopId)) {
      StopIdleSound();
      return;
    }

    SoundEffectPlayer.StopLoop(locomotionSoundLoopId);
    StopIdleSound();
  }

  void StopIdleSound() {
    if (string.IsNullOrEmpty(idleSoundLoopId)) {
      return;
    }

    SoundEffectPlayer.StopLoop(idleSoundLoopId);
  }
}
