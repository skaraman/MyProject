#ifndef COLD_PREVIEW_CORE_INCLUDED
#define COLD_PREVIEW_CORE_INCLUDED

inline float ColdPreviewInside01(float2 uv) {
  float2 lower = step(0.0, uv);
  float2 upper = step(uv, 1.0);
  return lower.x * lower.y * upper.x * upper.y;
}

inline float2 ColdPreviewAtlasUv(float2 sourceUv) {
  float2 halfTexel = _MainTex_TexelSize.xy * 0.5;
  float2 atlasMin = _SpriteUvRect.xy + halfTexel;
  float2 atlasMax = _SpriteUvRect.xy + _SpriteUvRect.zw - halfTexel;
  return lerp(atlasMin, atlasMax, saturate(sourceUv));
}

inline float ColdPreviewSourceAlpha(float2 sourceUv) {
  float inside = ColdPreviewInside01(sourceUv);
  return tex2D(_MainTex, ColdPreviewAtlasUv(sourceUv)).a * inside;
}

inline float ColdPreviewHash(float value) {
  return frac(sin(value * 127.1) * 43758.5453);
}

inline float ColdPreviewHash21(float2 value) {
  return frac(sin(dot(value, float2(127.1, 311.7))) * 43758.5453);
}

inline float ColdPreviewNoise(float2 coordinate) {
  float2 cell = floor(coordinate);
  float2 blend = frac(coordinate);
  blend = blend * blend * (3.0 - (2.0 * blend));

  float lowerLeft = ColdPreviewHash21(cell);
  float lowerRight = ColdPreviewHash21(cell + float2(1.0, 0.0));
  float upperLeft = ColdPreviewHash21(cell + float2(0.0, 1.0));
  float upperRight = ColdPreviewHash21(cell + float2(1.0, 1.0));
  return lerp(
    lerp(lowerLeft, lowerRight, blend.x),
    lerp(upperLeft, upperRight, blend.x),
    blend.y
  );
}

inline float ColdPreviewEdge(float2 sourceUv, float sampleRadius) {
  float center = ColdPreviewSourceAlpha(sourceUv);
  float left = ColdPreviewSourceAlpha(sourceUv + float2(-sampleRadius, 0.0));
  float right = ColdPreviewSourceAlpha(sourceUv + float2(sampleRadius, 0.0));
  float down = ColdPreviewSourceAlpha(sourceUv + float2(0.0, -sampleRadius));
  float up = ColdPreviewSourceAlpha(sourceUv + float2(0.0, sampleRadius));
  float minimumNeighbor = min(min(left, right), min(down, up));
  return saturate(center - minimumNeighbor);
}

inline float ColdPreviewTraceBottomEdge(
  float2 sourceUv,
  float maxDistance,
  out float edgeFound
) {
  const float alphaThreshold = 0.06;
  float edgeDistance = 0.0;
  float previousDistance = 0.0;
  float previousAlpha = ColdPreviewSourceAlpha(sourceUv);
  edgeFound = 0.0;

  [unroll]
  for (int probeIndex = 0; probeIndex < 20; probeIndex++) {
    float probeDistance = ((probeIndex + 1.0) / 20.0) * maxDistance;
    float probeAlpha = ColdPreviewSourceAlpha(
      sourceUv + float2(0.0, probeDistance)
    );
    float crossing = (1.0 - edgeFound) * step(alphaThreshold, probeAlpha);
    float crossingBlend = saturate(
      (alphaThreshold - previousAlpha) /
      max(probeAlpha - previousAlpha, 1e-4)
    );
    float crossingDistance = lerp(
      previousDistance,
      probeDistance,
      crossingBlend
    );
    edgeDistance = lerp(edgeDistance, crossingDistance, crossing);
    edgeFound = max(edgeFound, crossing);
    previousDistance = probeDistance;
    previousAlpha = probeAlpha;
  }

  return edgeDistance;
}

inline float ColdPreviewLine(
  float2 localPosition,
  float2 direction,
  float halfLength,
  float width
) {
  float2 perpendicular = float2(-direction.y, direction.x);
  float along = abs(dot(localPosition, direction));
  float across = abs(dot(localPosition, perpendicular));
  float lengthGate = 1.0 - smoothstep(
    halfLength * 0.82,
    halfLength,
    along
  );
  float widthGate = 1.0 - smoothstep(width, width * 1.8, across);
  return lengthGate * widthGate;
}

inline float ColdPreviewSnowflake(
  float2 localPosition,
  float radius,
  float width
) {
  float lineA = ColdPreviewLine(
    localPosition,
    float2(1.0, 0.0),
    radius,
    width
  );
  float lineB = ColdPreviewLine(
    localPosition,
    float2(0.5, 0.8660254),
    radius,
    width
  );
  float lineC = ColdPreviewLine(
    localPosition,
    float2(-0.5, 0.8660254),
    radius,
    width
  );
  float center = 1.0 - smoothstep(
    width * 1.25,
    width * 2.1,
    length(localPosition)
  );
  return saturate(max(max(lineA, lineB), max(lineC, center)));
}

void ColdPreviewCore_float(
  float2 effectUv,
  float previewTime,
  out float3 color,
  out float alpha
) {
  float2 sourceRectSize = max(_SourceRectInEffect.zw, float2(1e-4, 1e-4));
  float2 sourceUv = (effectUv - _SourceRectInEffect.xy) / sourceRectSize;
  float sourceAlpha = ColdPreviewSourceAlpha(sourceUv);
  float sourceAspect = max(
    (_MainTex_TexelSize.z * max(_SpriteUvRect.z, 1e-4)) /
    max(_MainTex_TexelSize.w * max(_SpriteUvRect.w, 1e-4), 1.0),
    0.01
  );

  float cycle = 0.5 - (
    0.5 * cos(
      (previewTime * _CycleSpeed * 2.15) + 1.85
    )
  );
  cycle = cycle * cycle * (3.0 - (2.0 * cycle));
  float formation = saturate(cycle * _Freeze);

  float broadNoise = ColdPreviewNoise(
    (sourceUv * _FrostScale) + float2(3.7, 8.2)
  );
  float detailNoise = ColdPreviewNoise(
    (sourceUv * (_FrostScale * 2.35)) + float2(17.1, 2.9)
  );
  float centerDistance = length((sourceUv - 0.5) * 1.72);
  float outsideIn = saturate(centerDistance + (broadNoise * 0.36));
  float growthThreshold = lerp(1.08, 0.02, formation);
  float growthMask = smoothstep(
    growthThreshold - 0.16,
    growthThreshold + 0.12,
    outsideIn
  );
  float edge = ColdPreviewEdge(
    sourceUv,
    max(max(_MainTex_TexelSize.x, _MainTex_TexelSize.y) * 1.8, 0.004)
  );
  growthMask = sourceAlpha * saturate(
    max(growthMask, edge * smoothstep(0.01, 0.2, formation))
  ) * smoothstep(0.01, 0.12, formation);

  float contourA = 1.0 - smoothstep(
    0.035,
    lerp(0.16, 0.075, _CrystalDetail),
    abs(broadNoise - 0.5)
  );
  float contourB = 1.0 - smoothstep(
    0.025,
    lerp(0.12, 0.055, _CrystalDetail),
    abs(detailNoise - 0.52)
  );
  float diagonalCrystal = pow(
    saturate(
      1.0 - abs(
        sin(
          (
            (sourceUv.x * 0.83) +
            sourceUv.y +
            (broadNoise * 0.18)
          ) * _FrostScale * 7.4
        )
      )
    ),
    lerp(6.0, 15.0, _CrystalDetail)
  );
  float crystalMask = growthMask * saturate(
    max(contourA * 0.78, contourB * _CrystalDetail) +
    (diagonalCrystal * 0.42) +
    (edge * 0.72)
  );

  float icicleFormation = smoothstep(0.14, 0.88, formation);
  const float maximumRandomLength = 1.18;
  float maximumTraceDistance = (
    _IcicleLength *
    maximumRandomLength *
    icicleFormation
  ) + 0.055;
  float edgeFound;
  float distanceBelowEdge = ColdPreviewTraceBottomEdge(
    sourceUv,
    maximumTraceDistance,
    edgeFound
  );
  float icicleCoordinate = sourceUv.x * max(_IcicleCount, 1.0);
  float icicleCell = floor(icicleCoordinate);
  float icicleSeed = ColdPreviewHash(icicleCell + 14.7);
  float icicleCenter = 0.5 + ((icicleSeed - 0.5) * 0.34);
  float icicleLocalX = (
    frac(icicleCoordinate) - icicleCenter
  ) / max(_IcicleCount, 1.0);
  float randomLength = lerp(
    0.35,
    maximumRandomLength,
    ColdPreviewHash(icicleCell + 29.4)
  );
  float randomBaseWidth = lerp(
    0.46,
    1.28,
    ColdPreviewHash(icicleCell + 68.1)
  );
  float icicleLength = _IcicleLength
    * icicleFormation
    * randomLength;
  float maximumBaseWidth = 0.48 / max(_IcicleCount, 1.0);
  float icicleBaseWidth = min(
    _IcicleWidth * randomBaseWidth,
    maximumBaseWidth
  );
  float normalizedIcicleDistance = distanceBelowEdge / max(icicleLength, 1e-4);
  float taperedWidth = icicleBaseWidth
    * lerp(1.0, 0.04, saturate(normalizedIcicleDistance));
  float icicleHorizontal = 1.0 - smoothstep(
    taperedWidth,
    taperedWidth + 0.006,
    abs(icicleLocalX)
  );
  float icicleVertical = 1.0 - smoothstep(
    icicleLength - 0.008,
    icicleLength + 0.006,
    distanceBelowEdge
  );
  float activeIcicle = step(
    0.2 + ((1.0 - _Freeze) * 0.38),
    ColdPreviewHash(icicleCell + 47.2)
  );
  float outsideSource = 1.0 - smoothstep(0.01, 0.16, sourceAlpha);
  float icicleMask = edgeFound
    * activeIcicle
    * icicleFormation
    * outsideSource
    * icicleHorizontal
    * icicleVertical;
  float icicleHighlight = icicleMask * (
    (1.0 - smoothstep(
      taperedWidth * 0.12,
      taperedWidth * 0.48,
      abs(icicleLocalX + (taperedWidth * 0.28))
    )) +
    (1.0 - saturate(normalizedIcicleDistance)) * 0.26
  );

  float2 physicalPosition = (
    sourceUv - 0.5
  ) * float2(sourceAspect, 1.0);
  float snowMask = 0.0;
  float snowGlow = 0.0;

  [unroll]
  for (int flakeIndex = 0; flakeIndex < 12; flakeIndex++) {
    float flakeSlot = (float)flakeIndex;
    float flakeSeed = ColdPreviewHash(flakeSlot + 5.3);
    float flakeActive = step(
      (flakeSlot + 0.5) / 12.0,
      _SnowAmount
    );
    float flakeX = lerp(
      -0.78 * sourceAspect,
      0.78 * sourceAspect,
      ColdPreviewHash(flakeSlot + 18.7)
    );
    flakeX += sin(
      (previewTime * lerp(0.35, 0.72, flakeSeed)) +
      (flakeSeed * 6.2831853)
    ) * lerp(0.018, 0.055, flakeSeed);
    float flakeTravel = frac(
      ColdPreviewHash(flakeSlot + 31.4) -
      (previewTime * lerp(0.025, 0.07, flakeSeed))
    );
    float flakeY = lerp(-0.5, 0.82, flakeTravel);
    float flakeRadius = lerp(
      0.009,
      0.021,
      ColdPreviewHash(flakeSlot + 43.8)
    );
    float2 flakeLocal = physicalPosition - float2(flakeX, flakeY);
    float flakeShape = ColdPreviewSnowflake(
      flakeLocal,
      flakeRadius,
      max(0.0012, flakeRadius * 0.115)
    );
    float flakePulse = lerp(
      0.62,
      1.0,
      0.5 + (
        0.5 * sin(
          (previewTime * 1.7) +
          (flakeSeed * 17.0)
        )
      )
    );
    snowMask = max(snowMask, flakeShape * flakeActive * flakePulse);
    snowGlow = max(
      snowGlow,
      (
        1.0 - smoothstep(
          flakeRadius,
          flakeRadius * 1.8,
          length(flakeLocal)
        )
      ) * flakeActive * flakePulse
    );
  }

  float snowOutside = 1.0 - smoothstep(0.01, 0.45, sourceAlpha);
  snowMask *= snowOutside;
  snowGlow *= snowOutside;

  float surfaceAlpha = growthMask * (
    _SurfaceOpacity +
    (crystalMask * _IceOpacity * 0.38)
  );
  float icicleAlpha = icicleMask * _IceOpacity;
  float snowAlpha = saturate(
    (snowMask * 0.9) +
    (snowGlow * 0.15)
  ) * _IceOpacity;
  float highlightAmount = saturate(
    (crystalMask * _Specular) +
    icicleHighlight +
    snowMask
  );

  color = lerp(
    _IceColor.rgb,
    _HighlightColor.rgb,
    highlightAmount
  ) * _Brightness;
  alpha = saturate(max(
    surfaceAlpha,
    max(icicleAlpha, snowAlpha)
  ));
}

#endif
