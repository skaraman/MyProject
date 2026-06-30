using System;
using System.Collections.Generic;
using UnityEngine;

public partial class GearController {
  public void _TogglePause() {
    TogglePause();
  }

  public void TogglePause(string forcePause = null) {
    animationController?.TogglePause(forcePause);
    if (effectControllerInitialized) {
      effectAnimationController.TogglePause(forcePause);
    }
  }

  public void ForceAnimation() {
    if (animationController == null) return;
    animationController.ForceAnimation(string.IsNullOrEmpty(defaultAnimation) ? null : defaultAnimation);
  }

  public void PlayAnimation(string anim, bool forceRestart = false, bool resolveInterrupts = true) {
    if (string.IsNullOrEmpty(anim)) return;
    animationController?.PlayAnimation(anim, forceRestart, resolveInterrupts);
  }

  private void HookAnimationEvents() {
    if (animationController == null) return;
    animationController.OnEffectTriggered = HandleEffectTriggered;
    animationController.OnProjectileTriggered = HandleProjectileTriggered;
  }

  private void ConfigureEffectController() {
    if (effectNode == null) return;
    BuildEffectAnimations();
    effectAnimationController.Initialize(
      effectNode.transform,
      new[] { effectNode.gameObject },
      null,
      null,
      effectAnimations,
      new Dictionary<string, Dictionary<string, string>>(),
      null,
      new Dictionary<string, Dictionary<string, List<HBox>>>(),
      "",
      false,
      effectAppearanceOwnerId,
      TextureResidencyCache.PinClass.Effect
    );
    effectControllerInitialized = true;
    ResetEffectVisualToEmpty();
  }

  private void BuildEffectAnimations() {
    effectAnimations.Clear();
    AddEffectAnimations(Effects.Esperanza);
    AddEffectAnimations(Effects.Things);
    AddEffectAnimations(Effects.Imp);
  }

  private void AddEffectAnimations(Dictionary<string, EffectData> effects) {
    if (effects == null) return;
    foreach (var kvp in effects) {
      if (string.IsNullOrEmpty(kvp.Key) || kvp.Value == null) continue;
      effectAnimations[kvp.Key] = new AnimData {
        start = kvp.Value.start,
        end = kvp.Value.end,
        duration = kvp.Value.duration * 1000f
      };
    }
  }

  private void HandleEffectTriggered(string effectKey) {
    if (string.IsNullOrEmpty(effectKey) || effectNode == null) return;
    if (!effectControllerInitialized) {
      ConfigureEffectController();
      if (!effectControllerInitialized) return;
    }
    PrepareEffectVisualForPlayback();
    effectResetToEmptyPending = true;
    effectAnimationController.ForceLoop = false;
    effectAnimationController.PlayAnimation(effectKey, true, resolveInterrupts: false);
  }

  private void HandleProjectileTriggered(string projectileKey) {
    if (string.IsNullOrEmpty(projectileKey)) return;
    ResolveProjectileManagerReference("projectile_event");
    if (projectileManager == null) {
      Debug.LogWarning(
        "[GearController] MissingProjectileManager" +
        " object=" + gameObject.name +
        " projectile='" + projectileKey + "'"
      );
      return;
    }
    var spawnPosition = ResolveProjectileSpawnPosition();
    var direction = ResolveProjectileDirection();
    projectileManager.SpawnProjectile(projectileKey, spawnPosition, direction);
  }

  void PrepareEffectVisualForPlayback() {
    if (effectNode == null) return;
    effectNode.SetDoNotRender(false);
    if (!string.IsNullOrWhiteSpace(effectNode.labelPrefix)) {
      effectNode.SetLabelPrefix("");
    }
  }

  void TryFinalizeCompletedEffectAnimation() {
    if (!effectControllerInitialized || !effectResetToEmptyPending) return;
    if (effectAnimationController.IsPlaying) return;
    ResetEffectVisualToEmpty();
  }

  void ResetEffectVisualToEmpty() {
    if (effectNode == null) return;
    effectNode.SetDoNotRender(false);
    effectNode.SetLabelPrefix("Empty");
    effectNode.ForceUpdateSpriteAndNormal(0);
    effectNode.SetLabelPrefix("");
    effectResetToEmptyPending = false;
  }

  void ResolveProjectileManagerReference(string source) {
    if (projectileManager != null || !Application.isPlaying) return;
    projectileManager = SingleSceneManager.ResolveGameplayProjectileManager();
    if (projectileManager == null || !ShouldLogRuntimeInitDebug()) return;
    Debug.Log(
      "[GearController] ResolvedProjectileManager" +
      " source=" + NormalizeDebugValue(source) +
      " object=" + gameObject.name +
      " manager='" + projectileManager.gameObject.name + "'" +
      " path='" + GetTransformPath(projectileManager.transform) + "'"
    );
  }

  static string NormalizeDebugValue(string value) {
    return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
  }

  static string GetTransformPath(Transform current) {
    if (current == null) return "";
    if (current.parent == null) return current.name;
    return GetTransformPath(current.parent) + "/" + current.name;
  }

  private Vector3 ResolveProjectileSpawnPosition() {
    if (projectileSpawn != null) return projectileSpawn.position;
    if (effectNode != null) return effectNode.transform.position;
    return transform.position;
  }

  private Vector3 ResolveProjectileDirection() {
    if (useFacingDirection) {
      return IsFacingRight ? Vector3.right : Vector3.left;
    }
    if (projectileDirection.sqrMagnitude <= 0.0001f) return Vector3.right;
    var dir = projectileDirection.normalized;
    return new Vector3(dir.x, dir.y, 0f);
  }

  void MarkAppearanceRevision() {
    if (appearanceRevision == int.MaxValue) {
      appearanceRevision = 1;
      return;
    }
    appearanceRevision++;
  }
}
