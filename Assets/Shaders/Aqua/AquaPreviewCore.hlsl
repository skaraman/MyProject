#ifndef AQUA_PREVIEW_CORE_INCLUDED
#define AQUA_PREVIEW_CORE_INCLUDED

inline float AquaPreviewInside01(float2 uv) {
  float2 lower = step(0.0, uv);
  float2 upper = step(uv, 1.0);
  return lower.x * lower.y * upper.x * upper.y;
}

inline float2 AquaPreviewAtlasUv(float2 sourceUv) {
  float2 halfTexel = _MainTex_TexelSize.xy * 0.5;
  float2 atlasMin = _SpriteUvRect.xy + halfTexel;
  float2 atlasMax = _SpriteUvRect.xy + _SpriteUvRect.zw - halfTexel;
  return lerp(atlasMin, atlasMax, saturate(sourceUv));
}

inline float4 AquaPreviewSource(float2 sourceUv) {
  float inside = AquaPreviewInside01(sourceUv);
  float4 source = tex2D(_MainTex, AquaPreviewAtlasUv(sourceUv));
  source.a *= inside;
  return source;
}

inline float AquaPreviewSourceAlpha(float2 sourceUv) {
  return AquaPreviewSource(sourceUv).a;
}

inline float AquaPreviewHash(float value) {
  return frac(sin(value * 127.1) * 43758.5453);
}

inline float AquaPreviewTeardrop(float2 localPosition, float softness) {
  float verticalPosition = localPosition.y;
  float vertical01 = saturate((verticalPosition + 1.0) * 0.5);
  float roundedWidth = sqrt(saturate(1.0 - (verticalPosition * verticalPosition)));
  float taperedWidth = roundedWidth * lerp(1.05, 0.1, pow(vertical01, 0.82));
  float horizontalMask = 1.0 - smoothstep(
    taperedWidth,
    taperedWidth + softness,
    abs(localPosition.x)
  );
  float verticalMask = smoothstep(
    -1.0 - softness,
    -1.0 + softness,
    verticalPosition
  ) * (
    1.0 - smoothstep(
      1.0 - softness,
      1.0 + softness,
      verticalPosition
    )
  );
  return saturate(horizontalMask * verticalMask);
}

inline float AquaPreviewEdge(float2 sourceUv, float sourceAlpha, float sampleRadius) {
  float left = AquaPreviewSourceAlpha(sourceUv + float2(-sampleRadius, 0.0));
  float right = AquaPreviewSourceAlpha(sourceUv + float2(sampleRadius, 0.0));
  float down = AquaPreviewSourceAlpha(sourceUv + float2(0.0, -sampleRadius));
  float up = AquaPreviewSourceAlpha(sourceUv + float2(0.0, sampleRadius));
  float neighborMin = min(min(left, right), min(down, up));
  return saturate(sourceAlpha - neighborMin);
}

inline float AquaPreviewBottomEdge(float2 sourceUv, float sampleRadius) {
  float sourceAlpha = AquaPreviewSourceAlpha(sourceUv);
  float belowAlpha = AquaPreviewSourceAlpha(sourceUv + float2(0.0, -sampleRadius));
  return saturate(sourceAlpha - belowAlpha);
}

inline float AquaPreviewTraceBottomEdge(
  float2 sourceUv,
  float maxDistance,
  out float edgeFound
) {
  const float alphaThreshold = 0.06;
  float edgeDistance = 0.0;
  float previousDistance = 0.0;
  float previousAlpha = AquaPreviewSourceAlpha(sourceUv);
  edgeFound = 0.0;

  [unroll]
  for (int probeIndex = 0; probeIndex < 16; probeIndex++) {
    float probeDistance = ((probeIndex + 1.0) / 16.0) * maxDistance;
    float probeAlpha = AquaPreviewSourceAlpha(
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

inline float AquaPreviewCoverage(float2 sourceUv, float previewTime) {
  float2 broadUv = sourceUv * float2(_NoiseScale * 0.72, _NoiseScale * 1.18);
  broadUv += float2(0.0, previewTime * _FlowSpeed * 0.13);
  float broadNoise = tex2D(_NoiseTex, frac(broadUv)).r;

  float2 detailUv = sourceUv * float2(_NoiseScale * 1.5, _NoiseScale * 2.1);
  detailUv += float2(-previewTime * _FlowSpeed * 0.025, previewTime * _FlowSpeed * 0.28);
  float detailNoise = tex2D(_FlowTex, frac(detailUv)).g;

  float pattern = lerp(broadNoise, detailNoise, 0.25 + (_Beading * 0.28));
  float threshold = lerp(0.78, 0.18, _Wetness);
  float softness = lerp(0.08, 0.2, _Wetness);
  return smoothstep(threshold - softness, threshold + softness, pattern);
}

void AquaPreviewCore_float(
  float2 effectUv,
  float previewTime,
  out float3 color,
  out float alpha
) {
  float2 sourceRectSize = max(_SourceRectInEffect.zw, float2(1e-4, 1e-4));
  float2 sourceUv = (effectUv - _SourceRectInEffect.xy) / sourceRectSize;
  float sourceAlpha = AquaPreviewSourceAlpha(sourceUv);
  float outsideFactor = 1.0 - smoothstep(0.02, 0.35, sourceAlpha);

  float2 flowUv = sourceUv * float2(_NoiseScale * 0.58, _NoiseScale * 1.35);
  flowUv += float2(0.0, previewTime * _FlowSpeed * 0.2);
  float2 flowVector = (tex2D(_FlowTex, frac(flowUv)).rg * 2.0) - 1.0;

  float coverage = AquaPreviewCoverage(sourceUv + float2(flowVector.x * 0.018, 0.0), previewTime);
  float surfaceNoise = tex2D(
    _NoiseTex,
    frac(
      (sourceUv * float2(_NoiseScale * 1.12, _NoiseScale * 1.72)) +
      float2(flowVector.x * 0.06, previewTime * _FlowSpeed * 0.31)
    )
  ).r;
  float rivulet = smoothstep(
    lerp(0.52, 0.7, _Beading),
    lerp(0.78, 0.9, _Beading),
    (surfaceNoise * 0.62) + (flowVector.y * 0.2) + 0.28
  );

  float flowLineCount = max(
    3.0,
    _DripCount * lerp(0.58, 0.82, _Wetness)
  );
  float flowLineCoordinate = (
    sourceUv.x +
    (flowVector.x * _Wobble * 0.16)
  ) * flowLineCount;
  float flowLineCell = floor(flowLineCoordinate);
  float flowLineLocalX = (frac(flowLineCoordinate) * 2.0) - 1.0;
  float flowPathCoordinate = sourceUv.y * lerp(4.2, 7.4, _Beading);
  float flowPathSegment = floor(flowPathCoordinate);
  float flowPathBlend = frac(flowPathCoordinate);
  flowPathBlend = flowPathBlend * flowPathBlend * (3.0 - (2.0 * flowPathBlend));
  float flowPathA = AquaPreviewHash(
    (flowLineCell * 29.7) +
    (flowPathSegment * 17.9) +
    4.3
  );
  float flowPathB = AquaPreviewHash(
    (flowLineCell * 29.7) +
    ((flowPathSegment + 1.0) * 17.9) +
    4.3
  );
  float flowPathCenter = (
    (lerp(flowPathA, flowPathB, flowPathBlend) * 2.0) -
    1.0
  ) * 0.52;
  flowPathCenter += sin(
    (sourceUv.y * 8.7) +
    (AquaPreviewHash(flowLineCell + 7.4) * 6.2831853)
  ) * 0.07;
  float flowLineWidth = lerp(
    0.065,
    0.15,
    AquaPreviewHash(flowLineCell + 21.6)
  ) * lerp(1.08, 0.82, _Beading);
  float flowLineShape = 1.0 - smoothstep(
    flowLineWidth,
    flowLineWidth + 0.095,
    abs(flowLineLocalX - flowPathCenter)
  );
  float flowLineChance = smoothstep(
    0.68 - (_Wetness * 0.48),
    0.78 - (_Wetness * 0.28),
    AquaPreviewHash(flowLineCell + 33.2)
  );
  float flowLineTravel = frac(
    (sourceUv.y * 2.7) +
    (previewTime * _FlowSpeed * 0.24) +
    AquaPreviewHash(flowLineCell + 12.8)
  );
  float flowLineHead = 1.0 - smoothstep(
    0.08,
    0.34,
    abs(flowLineTravel - 0.5)
  );
  float flowLineBreakup = tex2D(
    _FlowTex,
    frac(float2(
      (flowLineCell * 0.137) + (sourceUv.y * 0.31),
      (sourceUv.y * _NoiseScale * 0.72) +
      (previewTime * _FlowSpeed * 0.14)
    ))
  ).g;
  float wanderingFlowLine = flowLineShape
    * flowLineChance
    * lerp(0.46, 1.0, flowLineHead)
    * lerp(0.68, 1.0, flowLineBreakup);

  float surfaceCellCoordinate = (
    sourceUv.x +
    (flowVector.x * _Wobble * 0.45)
  ) * max(_DripCount, 1.0);
  float surfaceCell = floor(surfaceCellCoordinate);
  float beadCoordinate = (
    (sourceUv.y * lerp(3.2, 5.8, _Beading)) +
    (previewTime * _FlowSpeed * 0.22) +
    AquaPreviewHash(surfaceCell + 3.7)
  );
  float beadRow = floor(beadCoordinate);
  float beadTravel = frac(beadCoordinate);
  float beadPositionOffset = (
    AquaPreviewHash(
      (surfaceCell * 37.1) +
      (beadRow * 17.3) +
      8.9
    ) - 0.5
  ) * 0.72;
  float surfaceCellLocalX = (
    (frac(surfaceCellCoordinate) * 2.0) -
    1.0 -
    beadPositionOffset
  );
  float beadWidth = lerp(0.82, 0.58, _Beading);
  float surfaceBead = AquaPreviewTeardrop(
    float2(
      surfaceCellLocalX / max(beadWidth, 0.05),
      (beadTravel - 0.5) * 2.0
    ),
    lerp(0.12, 0.07, _Beading)
  );
  float surfaceDetail = max(
    max(rivulet, wanderingFlowLine),
    surfaceBead * _Beading
  );
  float surfaceMask = sourceAlpha
    * coverage
    * lerp(0.48, 1.0, surfaceDetail)
    * lerp(0.78, 1.08, surfaceNoise);
  float flowLineMask = sourceAlpha
    * coverage
    * wanderingFlowLine
    * lerp(0.72, 1.0, _Wetness);

  float edgeSampleRadius = max(_DripWidth * 0.48, 0.008);
  float dripReach = min(max(_DripLength * 1.18, 0.16), 0.32);
  float edgeFound = 0.0;
  float distanceBelowEdge = AquaPreviewTraceBottomEdge(
    sourceUv,
    dripReach,
    edgeFound
  );
  float2 anchorUv = sourceUv + float2(0.0, distanceBelowEdge);
  float cellCoordinate = (
    anchorUv.x +
    (flowVector.x * _Wobble * 0.18)
  ) * max(_DripCount, 1.0);
  float cell = floor(cellCoordinate);
  float cellRandom = AquaPreviewHash(cell + 14.3);
  float dropClock = (
    (previewTime * _FlowSpeed * 0.34) +
    (cellRandom * 1.37)
  );
  float dropCycle = frac(dropClock);
  float dropGeneration = floor(dropClock);
  float dropPositionOffset = (
    AquaPreviewHash(
      (cell * 41.7) +
      (dropGeneration * 23.9) +
      6.2
    ) - 0.5
  ) * 0.7;
  float dropSizeVariation = lerp(
    0.7,
    1.32,
    AquaPreviewHash(
      (cell * 19.3) +
      (dropGeneration * 31.1) +
      2.8
    )
  );
  float fallDirection = (
    AquaPreviewHash(
      (cell * 13.7) +
      (dropGeneration * 43.3) +
      11.4
    ) * 2.0
  ) - 1.0;
  float solidDropGate = edgeFound * lerp(
    0.86,
    1.0,
    AquaPreviewHash(
      (cell * 53.1) +
      (dropGeneration * 7.9) +
      18.4
    )
  );
  float cellLocalX = (
    (frac(cellCoordinate) * 2.0) -
    1.0 -
    dropPositionOffset
  );
  float cellPosition = abs(cellLocalX);

  float gatherProgress = smoothstep(0.0, 1.0, saturate(dropCycle / 0.48));
  float attachedGate = 1.0 - smoothstep(0.46, 0.56, dropCycle);
  float randomizedDripWidth = _DripWidth * dropSizeVariation;
  float hangingLength = min(
    dripReach * 0.92,
    _DripLength
    * lerp(0.16, lerp(0.7, 0.98, cellRandom), gatherProgress)
    * dropSizeVariation
  );
  float hangingProgress = saturate(
    distanceBelowEdge / max(hangingLength, 0.001)
  );
  float lengthGate = 1.0 - smoothstep(
    hangingLength - max(randomizedDripWidth * 0.8, 0.008),
    hangingLength + max(randomizedDripWidth * 0.28, 0.003),
    distanceBelowEdge
  );
  float widthRatio = saturate(
    randomizedDripWidth *
    max(_DripCount, 1.0) *
    lerp(0.72, 0.28, pow(hangingProgress, 0.72))
  );
  float tailShape = 1.0 - smoothstep(
    widthRatio,
    min(1.0, widthRatio + 0.2),
    cellPosition
  );
  float dripMask = solidDropGate
    * lengthGate
    * tailShape
    * attachedGate
    * (1.0 - (hangingProgress * 0.18))
    * outsideFactor;

  float attachedDropHalfHeight = max(randomizedDripWidth * 1.65, 0.014);
  float attachedDropWidth = max(
    randomizedDripWidth * max(_DripCount, 1.0) * 1.52,
    0.055
  );
  float dripTip = AquaPreviewTeardrop(
    float2(
      cellLocalX / attachedDropWidth,
      (hangingLength - distanceBelowEdge) / attachedDropHalfHeight
    ),
    0.09
  ) * solidDropGate
    * attachedGate
    * smoothstep(0.12, 0.42, gatherProgress)
    * outsideFactor;

  float fallProgress = saturate((dropCycle - 0.5) / 0.5);
  float fallVisibility = smoothstep(0.0, 0.08, fallProgress)
    * (1.0 - smoothstep(0.9, 1.0, fallProgress));
  float gravityProgress = fallProgress * fallProgress;
  float fallDistance = lerp(
    max(randomizedDripWidth * 1.1, _DripLength * 0.18),
    dripReach * 0.94,
    gravityProgress
  );
  float fallingDropHalfHeight = max(randomizedDripWidth * 1.72, 0.016)
    * lerp(1.18, 0.88, fallProgress);
  float fallingDropWidth = max(
    randomizedDripWidth * max(_DripCount, 1.0) * 1.62,
    0.06
  );
  float fallDrift = fallDirection * 0.34 * gravityProgress;
  float fallingDropMask = AquaPreviewTeardrop(
    float2(
      (cellLocalX - fallDrift) / fallingDropWidth,
      (fallDistance - distanceBelowEdge) / fallingDropHalfHeight
    ),
    0.08
  ) * solidDropGate
    * fallVisibility
    * outsideFactor;
  float dripProgress = hangingProgress;

  float sourceEdge = AquaPreviewEdge(sourceUv, sourceAlpha, edgeSampleRadius * 0.86);
  float bottomGlisten = AquaPreviewBottomEdge(sourceUv, edgeSampleRadius * 1.1);

  float3 wetNormal = UnpackNormal(tex2D(_NormalMap, AquaPreviewAtlasUv(sourceUv)));
  float3 wetLightDirection = normalize(float3(-0.36, 0.52, 0.77));
  float normalSpecular = pow(
    saturate(dot(wetNormal, wetLightDirection)),
    lerp(18.0, 7.0, saturate(_Specular))
  );
  float movingShine = 0.5 + (
    0.5 * sin(
      (sourceUv.x * 7.2) -
      (sourceUv.y * 5.4) +
      (previewTime * _FlowSpeed * 0.75) +
      (flowVector.x * 2.1)
    )
  );
  normalSpecular *= sourceAlpha
    * coverage
    * smoothstep(0.35, 0.92, movingShine)
    * saturate(_Specular)
    * saturate(_HasNormalMap);

  float surfaceAlpha = surfaceMask * _SurfaceOpacity * saturate(_WaterColor.a);
  float flowLineAlpha = flowLineMask
    * max(_SurfaceOpacity * 0.68, _DripOpacity * 0.18)
    * saturate(_WaterColor.a);
  float hangingAlpha = dripMask * _DripOpacity * saturate(_WaterColor.a);
  float fallingAlpha = fallingDropMask * _DripOpacity * saturate(_WaterColor.a);
  float edgeAlpha = sourceEdge
    * coverage
    * (0.08 + (_Specular * 0.08))
    * saturate(_HighlightColor.a);
  float tipAlpha = dripTip
    * _DripOpacity
    * 0.82
    * saturate(_HighlightColor.a);
  float wetMask = saturate(
    surfaceAlpha +
    flowLineAlpha +
    hangingAlpha +
    fallingAlpha +
    edgeAlpha +
    tipAlpha
  );

  float highlightAmount = saturate(
    (normalSpecular * 0.72) +
    (flowLineMask * _Specular * lerp(0.34, 0.62, flowLineHead)) +
    (sourceEdge * coverage * _Specular * 0.34) +
    (bottomGlisten * _Specular * 0.26) +
    (dripTip * 0.9) +
    (fallingDropMask * 0.82) +
    (surfaceDetail * surfaceMask * _Specular * 0.18) +
    ((1.0 - dripProgress) * dripMask * 0.08)
  );

  float3 deepWater = _WaterColor.rgb * float3(0.55, 0.72, 0.92);
  float3 bodyWater = lerp(deepWater, _WaterColor.rgb, saturate(wetMask * 1.8));
  color = lerp(bodyWater, _HighlightColor.rgb, highlightAmount) * _Brightness;
  alpha = wetMask;
}

#endif
