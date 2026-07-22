using System;
using System.Collections.Generic;
using UnityEngine;
using SoftBone = EZhex1991.EZSoftBone.EZSoftBone;
using SoftBoneMaterial = EZhex1991.EZSoftBone.EZSoftBoneMaterial;

public partial class GearController {
  const float DefaultHairBounceSleepThreshold = 0.000001f;

  [Header("Bounce Dynamics")]
  [SerializeField] SoftBoneMaterial esperanzaHairBounceMaterial;
  [SerializeField] SoftBoneMaterial skywardHairBounceMaterial;
  [SerializeField, Min(0f)] float hairBounceSleepThreshold = DefaultHairBounceSleepThreshold;
  [SerializeField] Vector3 hairBounceGravity = new(0f, -0.012f, 0f);
  [SerializeField] Vector3 skywardHairBounceGravity = new(0f, 0.014f, 0f);
  [SerializeField, Min(0f)] float hairHurtHorizontalImpulse = 2.4f;
  [SerializeField, Min(0f)] float hairHurtUpwardImpulse = 0.9f;
  [SerializeField, Min(0.02f)] float hairAnimationGustInterval = 0.14f;
  [SerializeField, Min(0f)] float hairAnimationGustHorizontalImpulse = 0.14f;
  [SerializeField, Min(0f)] float hairAnimationGustVerticalImpulse = 0.05f;
  [SerializeField, Min(0f)] float hairAnimationMotionInfluence = 0.035f;
  [SerializeField, Min(0f)] float hairAnimationMotionMaxImpulse = 0.45f;
  [SerializeField, Min(0f)] float hairAnimationChangeImpulse = 0.22f;

  readonly List<SoftBone> hairSoftBones = new(40);
  readonly HashSet<SoftBone> hairSoftBoneSet = new();
  HurtBox2D bounceHurtBox;
  Vector3 previousHairAnchorPosition;
  Vector3 accumulatedHairAnchorDelta;
  float hairAnimationGustElapsed;
  int hairAnimationGustDirection = 1;
  string lastHairGustAnimation;
  bool hairGustAnchorInitialized;

  void ConfigureBounceDynamics() {
    hairSoftBones.Clear();
    hairSoftBoneSet.Clear();
    ResetHairAnimationGustTracking();

    foreach (var hairGroup in HairObjects ?? System.Array.Empty<GameObject>()) {
      if (hairGroup == null) continue;
      var softBones = hairGroup.GetComponentsInChildren<SoftBone>(includeInactive: true);
      for (var i = 0; i < softBones.Length; i++) {
        var softBone = softBones[i];
        if (softBone == null || !hairSoftBoneSet.Add(softBone)) continue;

        var usesSkywardDynamics = UsesSkywardHairDynamics(softBone);
        var bounceMaterial = usesSkywardDynamics
          ? skywardHairBounceMaterial
          : esperanzaHairBounceMaterial;
        if (bounceMaterial != null) {
          softBone.sharedMaterial = bounceMaterial;
        }
        softBone.sleepThreshold = Mathf.Max(0f, hairBounceSleepThreshold);
        softBone.gravity = usesSkywardDynamics
          ? skywardHairBounceGravity
          : hairBounceGravity;
        hairSoftBones.Add(softBone);
      }
    }

    bounceHurtBox = GetComponentInChildren<HurtBox2D>(includeInactive: true);
    if (bounceHurtBox != null) {
      bounceHurtBox.OnHit.RemoveListener(HandleBounceHurt);
      bounceHurtBox.OnHit.AddListener(HandleBounceHurt);
    }
  }

  static bool UsesSkywardHairDynamics(SoftBone softBone) {
    return softBone.name.StartsWith("Bolt", StringComparison.OrdinalIgnoreCase)
      || softBone.name.StartsWith("Aqua", StringComparison.OrdinalIgnoreCase);
  }

  void UpdateHairAnimationGust(float deltaTime) {
    if (HairSkin == null || hairSoftBones.Count == 0 || animationController == null) {
      ResetHairAnimationGustTracking();
      return;
    }

    var anchorPosition = HairSkin.transform.position;
    if (!hairGustAnchorInitialized) {
      previousHairAnchorPosition = anchorPosition;
      lastHairGustAnimation = animationController.CurrentAnimation;
      hairGustAnchorInitialized = true;
      return;
    }

    var anchorDelta = anchorPosition - previousHairAnchorPosition;
    previousHairAnchorPosition = anchorPosition;
    anchorDelta.z = 0f;

    var animation = animationController.CurrentAnimation;
    if (deltaTime <= 0f || !animationController.IsPlaying || string.IsNullOrWhiteSpace(animation)) {
      accumulatedHairAnchorDelta = Vector3.zero;
      hairAnimationGustElapsed = 0f;
      lastHairGustAnimation = animation;
      return;
    }

    // Ignore teleport-sized anchor jumps; regular locomotion and pose changes remain gust inputs.
    if (anchorDelta.sqrMagnitude <= 2.25f) {
      accumulatedHairAnchorDelta += anchorDelta;
    }

    if (!string.Equals(lastHairGustAnimation, animation, StringComparison.Ordinal)) {
      var trailingDirection = animationController.IsFacingRight ? -1f : 1f;
      ApplyHairImpulse(new Vector3(
        trailingDirection * hairAnimationChangeImpulse,
        hairAnimationGustVerticalImpulse,
        0f
      ));
      lastHairGustAnimation = animation;
    }

    hairAnimationGustElapsed += deltaTime;
    var gustInterval = Mathf.Max(0.02f, hairAnimationGustInterval);
    if (hairAnimationGustElapsed < gustInterval) return;

    var sampleDuration = Mathf.Max(hairAnimationGustElapsed, 0.001f);
    var anchorVelocity = accumulatedHairAnchorDelta / sampleDuration;
    var motionImpulse = Vector3.ClampMagnitude(
      -anchorVelocity * hairAnimationMotionInfluence,
      Mathf.Max(0f, hairAnimationMotionMaxImpulse)
    );
    motionImpulse.z = 0f;

    hairAnimationGustDirection = -hairAnimationGustDirection;
    var facingDirection = animationController.IsFacingRight ? 1f : -1f;
    var flutterImpulse = new Vector3(
      facingDirection * hairAnimationGustDirection * hairAnimationGustHorizontalImpulse,
      hairAnimationGustVerticalImpulse * (hairAnimationGustDirection > 0 ? 1f : 0.35f),
      0f
    );

    ApplyHairImpulse(motionImpulse + flutterImpulse);
    accumulatedHairAnchorDelta = Vector3.zero;
    hairAnimationGustElapsed = 0f;
  }

  void ResetHairAnimationGustTracking() {
    previousHairAnchorPosition = Vector3.zero;
    accumulatedHairAnchorDelta = Vector3.zero;
    hairAnimationGustElapsed = 0f;
    hairAnimationGustDirection = 1;
    lastHairGustAnimation = null;
    hairGustAnchorInitialized = false;
  }

  void DisposeBounceDynamics() {
    if (bounceHurtBox != null) {
      bounceHurtBox.OnHit.RemoveListener(HandleBounceHurt);
      bounceHurtBox = null;
    }
    hairSoftBones.Clear();
    hairSoftBoneSet.Clear();
    ResetHairAnimationGustTracking();
  }

  void HandleBounceHurt(HitBox2D hitBox) {
    if (hitBox == null || !hitBox.IsEnemyOwned) return;

    var source = hitBox.ActorOwner != null
      ? hitBox.ActorOwner.position
      : hitBox.transform.position;
    var horizontalDirection = Mathf.Sign(transform.position.x - source.x);
    if (Mathf.Approximately(horizontalDirection, 0f)) {
      horizontalDirection = transform.lossyScale.x < 0f ? 1f : -1f;
    }
    var impulse = new Vector3(
      horizontalDirection * hairHurtHorizontalImpulse,
      hairHurtUpwardImpulse,
      0f
    );

    ApplyHairImpulse(impulse);
  }

  void ApplyHairImpulse(Vector3 impulse) {
    if (impulse.sqrMagnitude <= Mathf.Epsilon) return;

    for (var i = 0; i < hairSoftBones.Count; i++) {
      var softBone = hairSoftBones[i];
      if (softBone == null || !softBone.isActiveAndEnabled) continue;
      softBone.AddImpulse(impulse);
    }
  }
}
