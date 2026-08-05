using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Visual-only hit shards sourced from a destructible's authored BrokenPieces
/// sprites. The ParticleSystem never uses Rigidbody2D, Collider2D, gravity, or
/// collision modules, so the shards cannot affect gameplay physics.
/// </summary>
[DisallowMultipleComponent]
public sealed class DestructibleHitPieceParticles : MonoBehaviour {
  static readonly int MainColorPropertyId = Shader.PropertyToID("_Color");

  const int MaxParticles = 96;
  const int MinimumParticlesPerBurst = 7;
  const int MaximumParticlesPerBurst = 10;
  const float MinimumLifetimeSeconds = 0.4f;
  const float MaximumLifetimeSeconds = 0.7f;
  const float MinimumSpeed = 1f;
  const float MaximumSpeed = 1.8f;
  const float MinimumSize = 0.35f;
  const float MaximumSize = 0.7f;
  const int SortingOrderOffset = 12;
  const string BrokenPiecesName = "BrokenPieces";
  const string BrokenName = "Broken";

  sealed class ParticleSource {
    public SpriteRenderer renderer;
    public AllIn1AnimatorInspector animator;
  }

  readonly List<ParticleSource> particleSources = new(32);
  MaterialPropertyBlock sourcePropertyBlock;

  ParticleSystem particleSystem;
  ParticleSystemRenderer particleRenderer;
  Material particleMaterial;
  Transform sourceRoot;
  bool initialized;
  int sortingLayerId;
  int sortingOrder;
  Vector2 lastImpactDirection = Vector2.up;
  uint burstSequence;

  void Awake() {
    sourcePropertyBlock = new MaterialPropertyBlock();
  }

  public void Initialize(Transform brokenPiecesRoot) {
    if (initialized) {
      return;
    }

    sourceRoot = brokenPiecesRoot != null
      ? brokenPiecesRoot
      : transform.Find(BrokenPiecesName);
    if (sourceRoot == null) {
      sourceRoot = transform.Find(BrokenName);
    }
    if (sourceRoot == null) {
      return;
    }

    var sourceRenderers = sourceRoot.GetComponentsInChildren<SpriteRenderer>(true);
    Material sourceMaterial = null;
    SpriteRenderer sortingSource = null;
    for (var i = 0; i < sourceRenderers.Length; i++) {
      var sourceRenderer = sourceRenderers[i];
      if (sourceRenderer == null || sourceRenderer.sprite == null) {
        continue;
      }

      particleSources.Add(new ParticleSource {
        renderer = sourceRenderer,
        animator = sourceRenderer.GetComponent<AllIn1AnimatorInspector>()
      });

      if (sourceMaterial == null && sourceRenderer.sharedMaterial != null) {
        sourceMaterial = sourceRenderer.sharedMaterial;
      }

      if (sortingSource == null || sourceRenderer.sortingOrder > sortingSource.sortingOrder) {
        sortingSource = sourceRenderer;
      }
    }

    if (particleSources.Count == 0) {
      return;
    }

    CreateParticleSystem(sourceMaterial, sortingSource);
    initialized = particleSystem != null;
  }

  public void Play(Vector3 impactPosition, Vector2 impactDirection) {
    if (!initialized) {
      Initialize(sourceRoot);
    }

    if (!initialized || particleSystem == null || particleSources.Count == 0) {
      return;
    }

    if (impactDirection.sqrMagnitude > 0.0001f) {
      lastImpactDirection = impactDirection.normalized;
    }

    var baseAngle = Mathf.Atan2(lastImpactDirection.y, lastImpactDirection.x) * Mathf.Rad2Deg;
    var randomState = CreateRandomState();
    var particleCount = RandomRange(
      ref randomState,
      MinimumParticlesPerBurst,
      MaximumParticlesPerBurst + 1
    );

    for (var i = 0; i < particleCount; i++) {
      var sourceIndex = RandomRange(ref randomState, 0, particleSources.Count);
      var source = particleSources[sourceIndex];

      // Keep most shards on the impact-facing side while allowing enough
      // spread to read as a hand-authored cinematic burst.
      var angle = baseAngle + RandomRange(ref randomState, -82f, 82f);
      var direction = new Vector2(
        Mathf.Cos(angle * Mathf.Deg2Rad),
        Mathf.Sin(angle * Mathf.Deg2Rad)
      );

      var emitParams = new ParticleSystem.EmitParams {
        // The system uses world simulation space, so the emission position
        // must also be supplied in world space. A zero position otherwise
        // places the burst at the scene origin.
        position = impactPosition,
        velocity = direction * RandomRange(ref randomState, MinimumSpeed, MaximumSpeed),
        startLifetime = RandomRange(
          ref randomState,
          MinimumLifetimeSeconds,
          MaximumLifetimeSeconds
        ),
        startSize = RandomRange(ref randomState, MinimumSize, MaximumSize),
        rotation = RandomRange(ref randomState, -Mathf.PI, Mathf.PI),
        startColor = SampleMainColor(source),
        randomSeed = NextRandomUInt(ref randomState)
      };

      // Choose the authored sprite explicitly so its sampled Main Color stays
      // paired with the same BrokenPieces renderer.
      var textureSheet = particleSystem.textureSheetAnimation;
      textureSheet.startFrame = new ParticleSystem.MinMaxCurve(sourceIndex);
      particleSystem.Emit(emitParams, 1);
    }

    particleSystem.Play();
  }

  uint CreateRandomState() {
    burstSequence++;
    var state = (uint)Time.frameCount * 747796405u;
    state ^= (uint)ObjectEntityId.GetRawValue(this) * 2891336453u;
    state ^= burstSequence * 277803737u;
    return state == 0u ? 2463534242u : state;
  }

  static uint NextRandomUInt(ref uint state) {
    state ^= state << 13;
    state ^= state >> 17;
    state ^= state << 5;
    return state;
  }

  static float NextRandom01(ref uint state) {
    return (NextRandomUInt(ref state) & 0x00ffffffu) / 16777216f;
  }

  static float RandomRange(ref uint state, float minimum, float maximum) {
    return Mathf.Lerp(minimum, maximum, NextRandom01(ref state));
  }

  static int RandomRange(ref uint state, int minimum, int maximumExclusive) {
    return minimum + (int)(NextRandom01(ref state) * (maximumExclusive - minimum));
  }

  Color SampleMainColor(ParticleSource source) {
    var sourceRenderer = source.renderer;
    if (sourceRenderer == null) {
      return Color.white;
    }

    Color shaderColor;
    sourcePropertyBlock.Clear();
    sourceRenderer.GetPropertyBlock(sourcePropertyBlock);
    if (sourcePropertyBlock.HasColor(MainColorPropertyId)) {
      shaderColor = sourcePropertyBlock.GetColor(MainColorPropertyId);
    }
    else if (TryGetAuthoredMainColor(source.animator, out var authoredColor)) {
      // BrokenPieces commonly start inactive, so their animator has not yet
      // copied the inspector's _Color sequence into a property block.
      shaderColor = authoredColor;
    }
    else {
      var material = sourceRenderer.sharedMaterial;
      shaderColor = material != null && material.HasColor(MainColorPropertyId)
        ? material.GetColor(MainColorPropertyId)
        : Color.white;
    }

    return sourceRenderer.color * shaderColor;
  }

  static bool TryGetAuthoredMainColor(
    AllIn1AnimatorInspector animator,
    out Color color
  ) {
    color = Color.white;
    if (animator == null || animator.colorAnimations == null) {
      return false;
    }

    for (var i = 0; i < animator.colorAnimations.Count; i++) {
      var animation = animator.colorAnimations[i];
      if (animation == null || animation.prop != "_Color" ||
          animation.sequences == null || animation.sequences.Count == 0) {
        continue;
      }

      color = animation.sequences[0].from;
      return true;
    }

    return false;
  }

  void CreateParticleSystem(Material sourceMaterial, SpriteRenderer sortingSource) {
    var particleObject = new GameObject("Broken Piece Hit Particles");
    particleObject.transform.SetParent(transform, false);
    particleObject.layer = gameObject.layer;

    particleSystem = particleObject.AddComponent<ParticleSystem>();
    // Unity can begin the newly added system before its MainModule is fully
    // configured. Stop and clear it before changing duration or lifetime.
    particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    particleRenderer = particleObject.GetComponent<ParticleSystemRenderer>();
    if (particleRenderer == null) {
      particleRenderer = particleObject.AddComponent<ParticleSystemRenderer>();
    }

    var main = particleSystem.main;
    main.loop = false;
    main.playOnAwake = false;
    main.duration = MaximumLifetimeSeconds;
    main.startLifetime = new ParticleSystem.MinMaxCurve(
      MinimumLifetimeSeconds,
      MaximumLifetimeSeconds
    );
    main.startSpeed = 0f;
    main.startSize = new ParticleSystem.MinMaxCurve(MinimumSize, MaximumSize);
    main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
    main.startColor = Color.white;
    main.gravityModifier = 0f;
    main.simulationSpace = ParticleSystemSimulationSpace.World;
    main.maxParticles = MaxParticles;

    var emission = particleSystem.emission;
    emission.enabled = false;
    var shape = particleSystem.shape;
    shape.enabled = false;
    var velocityOverLifetime = particleSystem.velocityOverLifetime;
    velocityOverLifetime.enabled = false;
    var forceOverLifetime = particleSystem.forceOverLifetime;
    forceOverLifetime.enabled = false;
    var noise = particleSystem.noise;
    noise.enabled = false;
    var collision = particleSystem.collision;
    collision.enabled = false;
    var trigger = particleSystem.trigger;
    trigger.enabled = false;

    var colorOverLifetime = particleSystem.colorOverLifetime;
    colorOverLifetime.enabled = true;
    var fade = new Gradient();
    fade.SetKeys(
      new[] {
        new GradientColorKey(Color.white, 0f),
        new GradientColorKey(Color.white, 1f)
      },
      new[] {
        new GradientAlphaKey(1f, 0f),
        new GradientAlphaKey(0f, 1f)
      }
    );
    colorOverLifetime.color = new ParticleSystem.MinMaxGradient(fade);

    var sizeOverLifetime = particleSystem.sizeOverLifetime;
    sizeOverLifetime.enabled = true;
    sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
      1f,
      AnimationCurve.EaseInOut(0f, 1f, 1f, 0.45f)
    );

    var textureSheet = particleSystem.textureSheetAnimation;
    textureSheet.enabled = true;
    textureSheet.mode = ParticleSystemAnimationMode.Sprites;
    textureSheet.startFrame = new ParticleSystem.MinMaxCurve(0f);
    textureSheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
    for (var i = 0; i < particleSources.Count; i++) {
      textureSheet.AddSprite(particleSources[i].renderer.sprite);
    }

    particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
    particleRenderer.alignment = ParticleSystemRenderSpace.View;
    // Authored destructible materials use the AllIn1 sprite shader, which is
    // not guaranteed to support ParticleSystem texture-sheet rendering. Use
    // the built-in sprite shader for reliable visibility, with the authored
    // material retained only as a fallback if the shader is unavailable.
    // These sprites rely on the sprite shader's alpha handling. Prefer it
    // over the particle shaders so transparent pixels do not render as a
    // solid quad around each texture-sheet frame.
    var particleShader = Shader.Find("Sprites/Default") ??
                         Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                         Shader.Find("Particles/Standard Unlit");
    if (particleShader != null) {
      particleMaterial = new Material(particleShader) {
        hideFlags = HideFlags.HideAndDontSave
      };
      particleRenderer.sharedMaterial = particleMaterial;
    }
    else if (sourceMaterial != null) {
      particleRenderer.sharedMaterial = sourceMaterial;
    }
    if (sortingSource != null) {
      sortingLayerId = sortingSource.sortingLayerID;
      sortingOrder = sortingSource.sortingOrder + SortingOrderOffset;
      particleRenderer.sortingLayerID = sortingLayerId;
      particleRenderer.sortingOrder = sortingOrder;
    }
  }

  void OnDestroy() {
    if (particleMaterial != null) {
      Destroy(particleMaterial);
      particleMaterial = null;
    }
  }
}
