#ifndef DARK_PREVIEW_CORE_INCLUDED
#define DARK_PREVIEW_CORE_INCLUDED

inline float DarkPreviewInside01(float2 uv) {
  float2 lower = step(0.0, uv);
  float2 upper = step(uv, 1.0);
  return lower.x * lower.y * upper.x * upper.y;
}

inline float2 DarkPreviewAtlasUv(float2 sourceUv) {
  float2 halfTexel = _MainTex_TexelSize.xy * 0.5;
  float2 atlasMin = _SpriteUvRect.xy + halfTexel;
  float2 atlasMax = _SpriteUvRect.xy + _SpriteUvRect.zw - halfTexel;
  return lerp(atlasMin, atlasMax, saturate(sourceUv));
}

inline float DarkPreviewSourceAlpha(float2 sourceUv) {
  float inside = DarkPreviewInside01(sourceUv);
  return tex2D(_MainTex, DarkPreviewAtlasUv(sourceUv)).a * inside;
}

inline float DarkPreviewHash(float value) {
  return frac(sin(value * 127.1) * 43758.5453);
}

inline float DarkPreviewHash21(float2 value) {
  return frac(sin(dot(value, float2(127.1, 311.7))) * 43758.5453);
}

inline float DarkPreviewNoise(float2 coordinate) {
  float2 cell = floor(coordinate);
  float2 blend = frac(coordinate);
  blend = blend * blend * (3.0 - (2.0 * blend));

  float lowerLeft = DarkPreviewHash21(cell);
  float lowerRight = DarkPreviewHash21(cell + float2(1.0, 0.0));
  float upperLeft = DarkPreviewHash21(cell + float2(0.0, 1.0));
  float upperRight = DarkPreviewHash21(cell + float2(1.0, 1.0));
  return lerp(
    lerp(lowerLeft, lowerRight, blend.x),
    lerp(upperLeft, upperRight, blend.x),
    blend.y
  );
}

inline float DarkPreviewEdge(float2 sourceUv, float sampleRadius) {
  float centerAlpha = DarkPreviewSourceAlpha(sourceUv);
  float leftAlpha = DarkPreviewSourceAlpha(
    sourceUv + float2(-sampleRadius, 0.0)
  );
  float rightAlpha = DarkPreviewSourceAlpha(
    sourceUv + float2(sampleRadius, 0.0)
  );
  float downAlpha = DarkPreviewSourceAlpha(
    sourceUv + float2(0.0, -sampleRadius)
  );
  float upAlpha = DarkPreviewSourceAlpha(
    sourceUv + float2(0.0, sampleRadius)
  );
  float diagonalRadius = sampleRadius * 0.7071068;
  float lowerLeftAlpha = DarkPreviewSourceAlpha(
    sourceUv + float2(-diagonalRadius, -diagonalRadius)
  );
  float lowerRightAlpha = DarkPreviewSourceAlpha(
    sourceUv + float2(diagonalRadius, -diagonalRadius)
  );
  float upperLeftAlpha = DarkPreviewSourceAlpha(
    sourceUv + float2(-diagonalRadius, diagonalRadius)
  );
  float upperRightAlpha = DarkPreviewSourceAlpha(
    sourceUv + float2(diagonalRadius, diagonalRadius)
  );
  float edgeDifference = max(
    max(
      max(abs(centerAlpha - leftAlpha), abs(centerAlpha - rightAlpha)),
      max(abs(centerAlpha - downAlpha), abs(centerAlpha - upAlpha))
    ),
    max(
      max(abs(centerAlpha - lowerLeftAlpha), abs(centerAlpha - lowerRightAlpha)),
      max(abs(centerAlpha - upperLeftAlpha), abs(centerAlpha - upperRightAlpha))
    )
  );
  return saturate(edgeDifference);
}

inline float DarkPreviewAngleDifference(float angleA, float angleB) {
  return atan2(sin(angleA - angleB), cos(angleA - angleB));
}

void DarkPreviewCore_float(
  float2 effectUv,
  float previewTime,
  out float3 color,
  out float alpha
) {
  float2 sourceRectSize = max(_SourceRectInEffect.zw, float2(1e-4, 1e-4));
  float2 sourceUv = (effectUv - _SourceRectInEffect.xy) / sourceRectSize;
  float sourceAlpha = DarkPreviewSourceAlpha(sourceUv);
  float outsideSource = 1.0 - smoothstep(0.015, 0.32, sourceAlpha);
  float sourceAspect = max(
    (_MainTex_TexelSize.z * max(_SpriteUvRect.z, 1e-4)) /
    max(_MainTex_TexelSize.w * max(_SpriteUvRect.w, 1e-4), 1.0),
    0.01
  );

  float minimumEdgeRadius = max(
    max(_MainTex_TexelSize.x, _MainTex_TexelSize.y) * 1.6,
    0.003
  );
  float tightEdge = DarkPreviewEdge(
    sourceUv,
    max(minimumEdgeRadius, _EdgeWidth * 0.18)
  );
  float middleEdge = DarkPreviewEdge(
    sourceUv,
    max(minimumEdgeRadius * 1.8, _EdgeWidth * 0.52)
  );
  float wideEdge = DarkPreviewEdge(
    sourceUv,
    max(minimumEdgeRadius * 2.8, _EdgeWidth)
  );
  float edgeCore = saturate((tightEdge * 0.9) + (middleEdge * 0.42));
  float edgeAura = saturate(
    (tightEdge * 0.28) +
    (middleEdge * 0.58) +
    (wideEdge * 0.68)
  ) * _Presence;

  float2 veinWarpUv = (
    sourceUv * (_VeinScale * 0.52)
  ) + float2(
    previewTime * _Movement * 0.018,
    -previewTime * _Movement * 0.013
  );
  float veinWarp = DarkPreviewNoise(veinWarpUv + float2(5.2, 13.7));
  float2 veinUv = (
    sourceUv * _VeinScale
  ) + float2(
    veinWarp * 1.35,
    -veinWarp * 0.92
  );
  float broadVeinNoise = DarkPreviewNoise(veinUv + float2(17.3, 4.1));
  float fineVeinNoise = DarkPreviewNoise(
    (veinUv * 2.15) + float2(3.8, 21.4)
  );
  float veinThreshold = lerp(0.035, 0.095, _VeinAmount);
  float broadVeins = 1.0 - smoothstep(
    veinThreshold,
    veinThreshold + 0.055,
    abs(broadVeinNoise - 0.5)
  );
  float fineVeins = 1.0 - smoothstep(
    veinThreshold * 0.7,
    veinThreshold + 0.038,
    abs(fineVeinNoise - 0.5)
  );
  float veinPulse = 0.72 + (
    0.28 * sin(
      (previewTime * _Movement * 1.8) +
      (broadVeinNoise * 8.0)
    )
  );
  float veinMask = sourceAlpha
    * saturate(max(broadVeins, fineVeins * 0.62))
    * _VeinAmount
    * veinPulse;
  float surfaceNoise = DarkPreviewNoise(
    (sourceUv * (_VeinScale * 0.7)) +
    float2(
      -previewTime * _Movement * 0.025,
      previewTime * _Movement * 0.019
    )
  );
  float surfaceMask = sourceAlpha
    * _Presence
    * lerp(0.42, 1.0, surfaceNoise)
    * _SurfaceOpacity;

  float2 tendrilPosition = (
    sourceUv - 0.5
  ) * float2(sourceAspect, 1.0);
  float tendrilRadius = length(tendrilPosition);
  float tendrilAngle = atan2(tendrilPosition.y, tendrilPosition.x);
  float tendrilCore = 0.0;
  float tendrilAura = 0.0;

  [unroll]
  for (int tendrilIndex = 0; tendrilIndex < 8; tendrilIndex++) {
    float tendrilSlot = (float)tendrilIndex;
    float seed = DarkPreviewHash(tendrilSlot + 2.6);
    float countGate = step(tendrilSlot + 0.5, _TendrilCount);
    float orbitDirection = (
      step(0.5, DarkPreviewHash(tendrilSlot + 11.4)) * 2.0
    ) - 1.0;
    float startAngle = (
      DarkPreviewHash(tendrilSlot + 19.7) * 6.2831853
    ) + (
      orbitDirection * previewTime * _Movement * 0.14
    ) + (
      sin(
        (previewTime * _Movement * 0.46) +
        (seed * 11.0)
      ) * 0.24
    );
    float2 tendrilDirection = float2(cos(startAngle), sin(startAngle));
    float2 sourceHalfExtents = float2(sourceAspect * 0.5, 0.5);
    float boundaryDistance = min(
      sourceHalfExtents.x / max(abs(tendrilDirection.x), 0.025),
      sourceHalfExtents.y / max(abs(tendrilDirection.y), 0.025)
    );
    float startRadius = boundaryDistance * lerp(
      0.62,
      0.9,
      DarkPreviewHash(tendrilSlot + 27.1)
    );
    float tendrilLength = _TendrilReach * lerp(
      0.58,
      1.18,
      DarkPreviewHash(tendrilSlot + 36.5)
    );
    float tendrilProgress = (
      tendrilRadius - startRadius
    ) / max(tendrilLength, 1e-4);
    float clampedProgress = saturate(tendrilProgress);
    float curlDirection = (
      step(0.5, DarkPreviewHash(tendrilSlot + 44.8)) * 2.0
    ) - 1.0;
    float curlAmount = curlDirection * lerp(
      0.42,
      1.42,
      DarkPreviewHash(tendrilSlot + 53.2)
    );
    float movingWobble = sin(
      (clampedProgress * lerp(7.0, 13.0, seed)) +
      (previewTime * _Movement * lerp(0.7, 1.25, seed)) +
      (seed * 9.0)
    ) * sin(clampedProgress * 3.14159265) * 0.2;
    float targetAngle = startAngle
      + (curlAmount * pow(clampedProgress, 1.22))
      + movingWobble;
    float distanceToTendril = abs(
      DarkPreviewAngleDifference(tendrilAngle, targetAngle)
    ) * max(tendrilRadius, 0.2);
    float randomWidth = lerp(
      0.72,
      1.3,
      DarkPreviewHash(tendrilSlot + 61.9)
    );
    float taperedWidth = _TendrilWidth
      * randomWidth
      * lerp(1.36, 0.08, pow(clampedProgress, 0.72));
    float progressGate = smoothstep(-0.025, 0.035, tendrilProgress)
      * (1.0 - smoothstep(0.9, 1.025, tendrilProgress));
    float slowPulse = lerp(
      0.68,
      1.0,
      0.5 + (
        0.5 * sin(
          (previewTime * _Movement * 0.82) +
          (seed * 18.0)
        )
      )
    );
    float tendrilEnergy = countGate * _Presence * progressGate * slowPulse;
    float currentCore = 1.0 - smoothstep(
      taperedWidth,
      taperedWidth * 1.85,
      distanceToTendril
    );
    float currentAura = 1.0 - smoothstep(
      taperedWidth * 1.4,
      taperedWidth * 4.6,
      distanceToTendril
    );
    tendrilCore = max(tendrilCore, currentCore * tendrilEnergy);
    tendrilAura = max(tendrilAura, currentAura * tendrilEnergy);
  }

  tendrilCore *= outsideSource;
  tendrilAura *= outsideSource;

  float purpleAmount = saturate(
    (edgeAura * _EdgeOpacity) +
    (tendrilAura * 0.72) +
    (surfaceMask * 0.62)
  );
  float abyssAmount = saturate(
    (edgeCore * _EdgeOpacity * 0.62) +
    tendrilCore +
    veinMask +
    (surfaceMask * 0.36)
  );
  float totalAmount = max(purpleAmount, abyssAmount);
  float abyssBlend = saturate(
    abyssAmount / max(purpleAmount + abyssAmount, 1e-4)
  );

  color = lerp(
    _PurpleColor.rgb * _Glow,
    _AbyssColor.rgb,
    abyssBlend
  );
  alpha = saturate(
    totalAmount *
    lerp(_EdgeOpacity, _DarkOpacity, abyssBlend)
  );
}

#endif
