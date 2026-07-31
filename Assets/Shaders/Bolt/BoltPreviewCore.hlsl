#ifndef BOLT_PREVIEW_CORE_INCLUDED
#define BOLT_PREVIEW_CORE_INCLUDED

inline float BoltPreviewInside01(float2 uv) {
  float2 lower = step(0.0, uv);
  float2 upper = step(uv, 1.0);
  return lower.x * lower.y * upper.x * upper.y;
}

inline float2 BoltPreviewAtlasUv(float2 sourceUv) {
  float2 halfTexel = _MainTex_TexelSize.xy * 0.5;
  float2 atlasMin = _SpriteUvRect.xy + halfTexel;
  float2 atlasMax = _SpriteUvRect.xy + _SpriteUvRect.zw - halfTexel;
  return lerp(atlasMin, atlasMax, saturate(sourceUv));
}

inline float BoltPreviewSourceAlpha(float2 sourceUv) {
  float inside = BoltPreviewInside01(sourceUv);
  return tex2D(_MainTex, BoltPreviewAtlasUv(sourceUv)).a * inside;
}

inline float BoltPreviewHash(float value) {
  return frac(sin(value * 127.1) * 43758.5453);
}

inline float BoltPreviewSmoothNoise(float coordinate, float seed) {
  float cell = floor(coordinate);
  float blend = frac(coordinate);
  blend = blend * blend * (3.0 - (2.0 * blend));
  float valueA = BoltPreviewHash(cell + seed);
  float valueB = BoltPreviewHash(cell + 1.0 + seed);
  return (lerp(valueA, valueB, blend) * 2.0) - 1.0;
}

inline float2 BoltPreviewRotate(float2 direction, float angle) {
  float sine = sin(angle);
  float cosine = cos(angle);
  return float2(
    (direction.x * cosine) - (direction.y * sine),
    (direction.x * sine) + (direction.y * cosine)
  );
}

inline float BoltPreviewArcOffset(
  float progress,
  float seed,
  float amplitude
) {
  float clampedProgress = saturate(progress);
  float segmentCount = lerp(5.0, 9.0, BoltPreviewHash(seed + 2.7));
  float jagged = BoltPreviewSmoothNoise(
    clampedProgress * segmentCount,
    seed + 11.3
  );
  float broadArc = sin(
    (clampedProgress * 3.14159265) +
    ((BoltPreviewHash(seed + 8.1) - 0.5) * 1.1)
  ) * ((BoltPreviewHash(seed + 4.6) * 2.0) - 1.0);
  float regularArc = sin(clampedProgress * 18.8495559);
  float endpointEnvelope = sin(clampedProgress * 3.14159265);
  float randomArc = (
    (jagged * 0.78) +
    (broadArc * 0.22)
  );
  return lerp(
    regularArc,
    randomArc,
    _Randomness
  ) * amplitude * endpointEnvelope;
}

inline void BoltPreviewArcMasks(
  float2 arcPosition,
  float2 direction,
  float length,
  float width,
  float seed,
  float bendAmount,
  out float coreMask,
  out float glowMask
) {
  float2 perpendicular = float2(-direction.y, direction.x);
  float along = dot(arcPosition, direction);
  float progress = along / max(length, 1e-4);
  float pathOffset = BoltPreviewArcOffset(progress, seed, bendAmount);
  float distanceToPath = abs(dot(arcPosition, perpendicular) - pathOffset);
  float pathGate = smoothstep(-0.025, 0.018, progress)
    * (1.0 - smoothstep(0.965, 1.025, progress));
  float taperedWidth = width * lerp(
    1.18,
    0.48,
    pow(saturate(progress), 0.72)
  );
  coreMask = (
    1.0 - smoothstep(
      taperedWidth,
      taperedWidth * 1.85,
      distanceToPath
    )
  ) * pathGate;
  glowMask = (
    1.0 - smoothstep(
      taperedWidth * 1.6,
      taperedWidth * 5.2,
      distanceToPath
    )
  ) * pathGate;
}

inline float BoltPreviewEdge(float2 sourceUv, float sampleRadius) {
  float center = BoltPreviewSourceAlpha(sourceUv);
  float left = BoltPreviewSourceAlpha(sourceUv + float2(-sampleRadius, 0.0));
  float right = BoltPreviewSourceAlpha(sourceUv + float2(sampleRadius, 0.0));
  float down = BoltPreviewSourceAlpha(sourceUv + float2(0.0, -sampleRadius));
  float up = BoltPreviewSourceAlpha(sourceUv + float2(0.0, sampleRadius));
  float minimumNeighbor = min(min(left, right), min(down, up));
  float maximumNeighbor = max(max(left, right), max(down, up));
  return saturate(
    max(center - minimumNeighbor, maximumNeighbor - center)
  );
}

void BoltPreviewCore_float(
  float2 effectUv,
  float previewTime,
  out float3 color,
  out float alpha
) {
  float2 sourceRectSize = max(_SourceRectInEffect.zw, float2(1e-4, 1e-4));
  float2 sourceUv = (effectUv - _SourceRectInEffect.xy) / sourceRectSize;
  float sourceAspect = max(
    (_MainTex_TexelSize.z * max(_SpriteUvRect.z, 1e-4)) /
    max(_MainTex_TexelSize.w * max(_SpriteUvRect.w, 1e-4), 1.0),
    0.01
  );
  float2 arcPosition = (sourceUv - 0.5) * float2(sourceAspect, 1.0);
  float2 sourceHalfExtents = float2(sourceAspect * 0.5, 0.5);

  float boltCore = 0.0;
  float boltGlow = 0.0;
  float branchCore = 0.0;
  float branchGlow = 0.0;

  [unroll]
  for (int boltIndex = 0; boltIndex < 8; boltIndex++) {
    float slot = (float)boltIndex;
    float regularSlotOffset = slot / max(_BoltCount, 1.0);
    float randomSlotOffset = BoltPreviewHash(slot + 2.4) * 3.7;
    float slotOffset = lerp(
      regularSlotOffset,
      randomSlotOffset,
      _Randomness
    );
    float boltClock = (previewTime * _Activity * 0.68) + slotOffset;
    float boltGeneration = floor(boltClock);
    float boltLife = frac(boltClock);
    float seed = (
      (slot * 97.3) +
      (boltGeneration * 31.7) +
      5.1
    );

    float countGate = step(slot + 0.5, _BoltCount);
    float chargeRandom = BoltPreviewHash(seed + 19.2);
    float randomChargeGate = smoothstep(
      chargeRandom - 0.12,
      chargeRandom + 0.12,
      _Charge
    );
    float chargeGate = lerp(_Charge, randomChargeGate, _Randomness);
    float lifeEnvelope = smoothstep(0.0, 0.055, boltLife)
      * (1.0 - smoothstep(0.52, 0.96, boltLife));
    float randomStrobe = lerp(
      0.48,
      1.0,
      step(
        0.32,
        BoltPreviewHash(
          seed +
          (floor(boltLife * 17.0) * 7.9)
        )
      )
    );
    float strobe = lerp(1.0, randomStrobe, _Randomness);
    float boltEnergy = countGate * chargeGate * lifeEnvelope * strobe;

    float regularAngle = (
      (slot + 0.5) /
      max(_BoltCount, 1.0)
    ) * 6.2831853;
    float randomAngle = BoltPreviewHash(seed + 3.8) * 6.2831853;
    float angle = lerp(regularAngle, randomAngle, _Randomness);
    float2 direction = float2(cos(angle), sin(angle));
    float boundaryDistance = min(
      sourceHalfExtents.x / max(abs(direction.x), 0.025),
      sourceHalfExtents.y / max(abs(direction.y), 0.025)
    );
    float randomLengthVariation = lerp(
      0.82,
      1.12,
      BoltPreviewHash(seed + 6.4)
    );
    float length = boundaryDistance
      * lerp(1.04, 1.58, _Reach)
      * lerp(1.0, randomLengthVariation, _Randomness);
    float randomWidthVariation = lerp(
      0.72,
      1.28,
      BoltPreviewHash(seed + 13.6)
    );
    float width = _BoltWidth * lerp(
      1.0,
      randomWidthVariation,
      _Randomness
    );
    float randomBendAmount = length * lerp(
      0.045,
      0.17,
      BoltPreviewHash(seed + 22.5)
    );
    float bendAmount = lerp(
      length * 0.085,
      randomBendAmount,
      _Randomness
    );

    float mainCore;
    float mainGlow;
    BoltPreviewArcMasks(
      arcPosition,
      direction,
      length,
      width,
      seed,
      bendAmount,
      mainCore,
      mainGlow
    );
    boltCore = max(boltCore, mainCore * boltEnergy);
    boltGlow = max(boltGlow, mainGlow * boltEnergy);

    float randomBranchStart = lerp(
      0.32,
      0.68,
      BoltPreviewHash(seed + 27.1)
    );
    float branchStart = lerp(0.5, randomBranchStart, _Randomness);
    float2 mainPerpendicular = float2(-direction.y, direction.x);
    float2 branchOrigin = (
      direction * (length * branchStart)
    ) + (
      mainPerpendicular *
      BoltPreviewArcOffset(branchStart, seed, bendAmount)
    );
    float regularBranchSide = (frac(slot * 0.5) * 4.0) - 1.0;
    float randomBranchSide = step(
      0.5,
      BoltPreviewHash(seed + 32.7)
    ) * 2.0 - 1.0;
    float branchSideChoice = lerp(
      regularBranchSide * 0.5 + 0.5,
      randomBranchSide * 0.5 + 0.5,
      _Randomness
    );
    float branchSide = step(0.5, branchSideChoice) * 2.0 - 1.0;
    float randomBranchAngle = lerp(
      0.34,
      1.02,
      BoltPreviewHash(seed + 38.2)
    );
    float branchAngle = branchSide * lerp(
      0.62,
      randomBranchAngle,
      _Randomness
    );
    float2 branchDirection = BoltPreviewRotate(direction, branchAngle);
    float randomBranchLength = lerp(
      0.2,
      0.46,
      BoltPreviewHash(seed + 41.9)
    );
    float branchLength = length * lerp(
      0.32,
      randomBranchLength,
      _Randomness
    );
    float secondaryCore;
    float secondaryGlow;
    BoltPreviewArcMasks(
      arcPosition - branchOrigin,
      branchDirection,
      branchLength,
      width * 0.62,
      seed + 53.4,
      bendAmount * 0.52,
      secondaryCore,
      secondaryGlow
    );
    float branchEnergy = boltEnergy
      * _Branching
      * lerp(
        0.82,
        lerp(0.58, 1.0, BoltPreviewHash(seed + 61.8)),
        _Randomness
      );
    branchCore = max(branchCore, secondaryCore * branchEnergy);
    branchGlow = max(branchGlow, secondaryGlow * branchEnergy);
  }

  float sourceAlpha = BoltPreviewSourceAlpha(sourceUv);
  float sourceEdge = BoltPreviewEdge(
    sourceUv,
    max(max(_MainTex_TexelSize.x, _MainTex_TexelSize.y) * 1.6, 0.003)
  );
  float centerDistance = length(arcPosition);
  float centerPulse = 0.72 + (
    0.28 * sin(
      (previewTime * _Activity * 5.2) +
      (centerDistance * 34.0)
    )
  );
  float centerCharge = (
    1.0 - smoothstep(
      0.025,
      lerp(0.12, 0.23, _Charge),
      centerDistance
    )
  ) * centerPulse;
  float surfaceCharge = sourceAlpha * saturate(
    (centerCharge * 0.78) +
    (sourceEdge * _Charge * 0.58)
  ) * _SurfaceOpacity;

  float combinedCore = saturate(boltCore + branchCore);
  float combinedGlow = saturate(
    boltGlow +
    branchGlow +
    (surfaceCharge * 0.72)
  );
  float coreAmount = saturate(
    (combinedCore * 1.45) +
    (surfaceCharge * 0.48)
  );
  color = lerp(
    _BoltColor.rgb,
    _CoreColor.rgb,
    coreAmount
  ) * _Glow * lerp(0.78, 1.12, coreAmount);
  alpha = saturate(
    (
      (combinedGlow * 0.52) +
      (combinedCore * 0.88)
    ) * _BoltOpacity +
    surfaceCharge
  );
}

#endif
