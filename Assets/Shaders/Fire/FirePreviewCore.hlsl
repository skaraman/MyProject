#ifndef FIRE_PREVIEW_CORE_INCLUDED
#define FIRE_PREVIEW_CORE_INCLUDED

inline float FirePreviewInside01(float2 uv) {
  float2 lower = step(0.0, uv);
  float2 upper = step(uv, 1.0);
  return lower.x * lower.y * upper.x * upper.y;
}

inline float2 FirePreviewAtlasUv(float2 sourceUv) {
  float2 halfTexel = _MainTex_TexelSize.xy * 0.5;
  float2 atlasMin = _SpriteUvRect.xy + halfTexel;
  float2 atlasMax = _SpriteUvRect.xy + _SpriteUvRect.zw - halfTexel;
  return lerp(atlasMin, atlasMax, saturate(sourceUv));
}

inline float4 FirePreviewSource(float2 sourceUv) {
  float inside = FirePreviewInside01(sourceUv);
  float4 source = tex2D(_MainTex, FirePreviewAtlasUv(sourceUv));
  source.a *= inside;
  return source;
}

inline float FirePreviewSourceAlpha(float2 sourceUv) {
  return FirePreviewSource(sourceUv).a;
}

inline float FirePreviewHash(float value) {
  return frac(sin(value * 127.1) * 43758.5453);
}

inline float FirePreviewCoverage(float2 sourceUv, float previewTime) {
  float2 noiseUv = sourceUv * float2(_NoiseScale, _NoiseScale * 0.72);
  noiseUv += float2(previewTime * _FlowSpeed * 0.035, -previewTime * _FlowSpeed * 0.18);
  float largeNoise = tex2D(_NoiseTex, frac(noiseUv)).r;

  float2 detailUv = sourceUv * float2(_NoiseScale * 1.83, _NoiseScale * 1.28);
  detailUv += float2(-previewTime * _FlowSpeed * 0.055, -previewTime * _FlowSpeed * 0.29);
  float detailNoise = tex2D(_FlowTex, frac(detailUv)).g;

  float pattern = lerp(largeNoise, detailNoise, 0.25 + (_Breakup * 0.45));
  float threshold = lerp(0.78, 0.16, _FlameCoverage);
  float softness = lerp(0.06, 0.2, _Breakup);
  return smoothstep(threshold - softness, threshold + softness, pattern)
    * smoothstep(0.0, 0.04, _FlameCoverage);
}

inline float FirePreviewEdge(float2 sourceUv, float sourceAlpha, float sampleRadius) {
  float left = FirePreviewSourceAlpha(sourceUv + float2(-sampleRadius, 0.0));
  float right = FirePreviewSourceAlpha(sourceUv + float2(sampleRadius, 0.0));
  float down = FirePreviewSourceAlpha(sourceUv + float2(0.0, -sampleRadius));
  float up = FirePreviewSourceAlpha(sourceUv + float2(0.0, sampleRadius));
  float neighborMin = min(min(left, right), min(down, up));
  return saturate(sourceAlpha - neighborMin);
}

inline float FirePreviewOuterEdge(float2 sourceUv, float sourceAlpha, float sampleRadius) {
  float left = FirePreviewSourceAlpha(sourceUv + float2(-sampleRadius, 0.0));
  float right = FirePreviewSourceAlpha(sourceUv + float2(sampleRadius, 0.0));
  float down = FirePreviewSourceAlpha(sourceUv + float2(0.0, -sampleRadius));
  float up = FirePreviewSourceAlpha(sourceUv + float2(0.0, sampleRadius));
  float neighborMax = max(max(left, right), max(down, up));
  return saturate(neighborMax - sourceAlpha);
}

void FirePreviewCore_float(
  float2 effectUv,
  float previewTime,
  out float3 color,
  out float alpha
) {
  float2 sourceRectSize = max(_SourceRectInEffect.zw, float2(1e-4, 1e-4));
  float2 sourceUv = (effectUv - _SourceRectInEffect.xy) / sourceRectSize;
  float sourceAlpha = FirePreviewSourceAlpha(sourceUv);
  float coverage = FirePreviewCoverage(sourceUv, previewTime);
  float outsideFactor = 1.0 - smoothstep(0.02, 0.35, sourceAlpha);

  float2 flowUv = sourceUv * float2(_NoiseScale * 0.42, _NoiseScale * 0.7);
  flowUv += float2(0.0, -previewTime * _FlowSpeed * 0.14);
  float2 flow = (tex2D(_FlowTex, frac(flowUv)).rg * 2.0) - 1.0;

  float surfaceFlicker = tex2D(
    _NoiseTex,
    frac((sourceUv * float2(_NoiseScale * 1.2, _NoiseScale * 1.75)) +
      float2(flow.x * 0.08, -previewTime * _FlowSpeed * 0.34))
  ).r;
  float surface = sourceAlpha * coverage * lerp(0.6, 1.0, surfaceFlicker);

  float tongue = 0.0;
  float tongueProgress = 0.0;
  float tongueCoverage = 0.0;

  [unroll]
  for (int sampleIndex = 0; sampleIndex < 8; sampleIndex++) {
    float progress = (sampleIndex + 0.35) / 8.0;
    float rise = progress * _FlameHeight;
    float phase = (
      (sourceUv.x * _TongueCount * 6.2831853) +
      (progress * 4.1) -
      (previewTime * _FlowSpeed * 2.15)
    );
    float lateral = (
      (sin(phase) * 0.62) +
      (flow.x * 0.38)
    ) * _Sway * (0.25 + (progress * 0.95));

    float2 fuelUv = sourceUv - float2(lateral, rise);
    float fuel = FirePreviewSourceAlpha(fuelUv);
    float fuelCoverage = FirePreviewCoverage(fuelUv, previewTime - (progress * 0.22));

    float cellCoordinate = (fuelUv.x + (flow.x * _Sway * 0.35)) * _TongueCount;
    float cell = floor(cellCoordinate);
    float cellFraction = frac(cellCoordinate);
    float smoothCellFraction = cellFraction * cellFraction * (3.0 - (2.0 * cellFraction));
    float cellPosition = abs((cellFraction * 2.0) - 1.0);
    float randomHeight = lerp(
      FirePreviewHash(cell + 17.3),
      FirePreviewHash(cell + 18.3),
      smoothCellFraction);
    randomHeight = lerp(0.5, 1.08, randomHeight);
    float movingHeight = tex2D(
      _NoiseTex,
      frac(float2(
        cellCoordinate / max(_TongueCount, 1.0),
        (previewTime * _FlowSpeed * 0.09) + (cellCoordinate * 0.173)))
    ).r;
    float heightLimit = randomHeight * lerp(0.78, 1.2, movingHeight);
    float heightGate = 1.0 - smoothstep(heightLimit - (0.12 + (_Breakup * 0.08)), heightLimit, progress);

    float tipWidth = saturate(
      lerp(1.0, max(0.08, _TongueWidth * _TongueCount * 0.35), pow(progress, 0.72)));
    float tongueShape = 1.0 - smoothstep(tipWidth, min(1.0, tipWidth + 0.18), cellPosition);
    float tongueSeparation = outsideFactor * smoothstep(0.18, 0.82, progress);
    heightGate = lerp(1.0, heightGate, tongueSeparation);
    tongueShape = lerp(1.0, tongueShape, tongueSeparation);
    float breakupNoise = tex2D(
      _NoiseTex,
      frac((fuelUv * float2(_NoiseScale * 1.4, _NoiseScale * 2.1)) +
        float2(-previewTime * _FlowSpeed * 0.07, -previewTime * _FlowSpeed * 0.31))
    ).r;
    float breakupGate = smoothstep(
      0.18 + (_Breakup * 0.34),
      0.4 + (_Breakup * 0.28),
      breakupNoise + ((1.0 - progress) * 0.26));

    float candidate = fuel
      * fuelCoverage
      * heightGate
      * tongueShape
      * lerp(1.0, breakupGate, _Breakup)
      * (1.0 - (progress * 0.58));
    float replaceBest = step(tongue, candidate);
    tongueProgress = lerp(tongueProgress, progress, replaceBest);
    tongueCoverage = lerp(tongueCoverage, fuelCoverage, replaceBest);
    tongue = max(tongue, candidate);
  }

  float edgeRadius = max(_TongueWidth * 0.55, 0.008);
  float hotEdge = FirePreviewEdge(sourceUv, sourceAlpha, edgeRadius) * coverage;
  float outerEdge = FirePreviewOuterEdge(sourceUv, sourceAlpha, edgeRadius * 1.15)
    * FirePreviewCoverage(sourceUv, previewTime - 0.08);
  float tongueOutside = tongue * outsideFactor;
  float tongueInside = tongue * (1.0 - outsideFactor);

  float flameMask = saturate(
    (surface * _SurfaceOpacity) +
    tongueOutside +
    (tongueInside * (_SurfaceOpacity + 0.12)) +
    (hotEdge * 0.42) +
    (outerEdge * 0.58));

  float heightHeat = 1.0 - saturate(tongueProgress);
  float hotness = saturate(
    (heightHeat * 0.72) +
    (hotEdge * 0.5) +
    (outerEdge * 0.18) +
    (sourceAlpha * 0.24) +
    (tongueCoverage * 0.12));
  float3 emberColor = _FlameColor.rgb * float3(0.42, 0.2, 0.12);
  float3 outerColor = lerp(emberColor, _FlameColor.rgb, saturate(flameMask * 1.8));
  float3 flameGradient = lerp(outerColor, _HotColor.rgb, pow(hotness, 1.45));
  float pulse = lerp(0.88, 1.08, surfaceFlicker);

  color = flameGradient * _Brightness * pulse;
  alpha = saturate(flameMask * _FlameOpacity);
}

#endif
