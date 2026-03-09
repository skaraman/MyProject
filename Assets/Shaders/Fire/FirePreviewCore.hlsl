#ifndef FIRE_PREVIEW_CORE_INCLUDED
#define FIRE_PREVIEW_CORE_INCLUDED

inline float FirePreviewMask(float4 sampleColor) {
  float luminance = dot(sampleColor.rgb, float3(0.299, 0.587, 0.114));
  return saturate(max(sampleColor.a, luminance));
}

inline float FirePreviewSourceFeature(float4 sampleColor) {
  float luminance = dot(sampleColor.rgb, float3(0.299, 0.587, 0.114));
  float maxChannel = max(sampleColor.r, max(sampleColor.g, sampleColor.b));
  float minChannel = min(sampleColor.r, min(sampleColor.g, sampleColor.b));
  float chroma = maxChannel - minChannel;
  float hotBias = saturate((sampleColor.r * 0.75) + (sampleColor.g * 0.35) - (sampleColor.b * 0.45));
  float detail = smoothstep(0.16, 0.74, luminance);
  detail = max(detail, smoothstep(0.08, 0.42, chroma) * hotBias);
  return saturate((sampleColor.a * 0.18) + (detail * 0.82));
}

inline float FirePreviewPow01(float value, float exponent) {
  return pow(max(saturate(value), 1e-4), exponent);
}

inline float FirePreviewHash21(float2 value) {
  value = frac(value * float2(123.34, 456.21));
  value += dot(value, value + 45.32);
  return frac(value.x * value.y);
}

inline float FirePreviewRepeatedSourceFeature(float2 uv, float previewTime) {
  float repeatCount = max(_PatternRepeat, 1.0);
  float repeatedFeature = 0.0;

  [unroll]
  for (int layer = 0; layer < 3; layer++) {
    float layerValue = (float)layer;
    float repeatedX = (uv.x * repeatCount) + (layerValue * 0.37);
    float cellIndex = floor(repeatedX);
    float localX = frac(repeatedX);

    float cellHash = FirePreviewHash21(float2(cellIndex + (layerValue * 17.13), layerValue + 0.31));
    float cellHash2 = FirePreviewHash21(float2(cellIndex + 21.8, (layerValue * 7.4) + 1.7));
    float flipCell = step(0.5, cellHash);
    localX = lerp(localX, 1.0 - localX, flipCell);

    float widthScale = lerp(0.72, 1.22, cellHash2);
    localX = saturate(((localX - 0.5) / widthScale) + 0.5 + ((cellHash - 0.5) * 0.12));

    float verticalDrift = previewTime * _FlowSpeed * _SourceMotion * lerp(0.03, 0.09, cellHash2);
    float lateralWobble = sin((previewTime * (1.1 + (layerValue * 0.35))) + (cellIndex * 1.83))
      * _SourceMotion * 0.05;
    float localY = saturate(uv.y + ((cellHash2 - 0.5) * 0.18) - verticalDrift + lateralWobble);

    float layerFeature = FirePreviewSourceFeature(tex2D(_MainTex, float2(localX, localY)));
    layerFeature *= 1.0 - smoothstep(0.92, 1.0, uv.y);
    layerFeature *= lerp(0.82, 1.18, cellHash);
    repeatedFeature = max(repeatedFeature, layerFeature);
  }

  return saturate(repeatedFeature * _SourceFeatureBoost);
}

inline float FirePreviewSparkLayer(
  float2 uv,
  float xFuel,
  float flameHeight,
  float previewTime,
  float lateralOffset,
  float breakupLarge,
  float breakupDetail
) {
  float sparkRise = previewTime * (_SparkRiseSpeed + (max(_FlowSpeed, 0.0) * 1.4));
  float2 sparkUv = float2((uv.x - 0.5) * 2.0, uv.y);
  sparkUv.x += lateralOffset * _SparkDrift;
  sparkUv += float2((breakupLarge - 0.5) * 0.18, (breakupDetail - 0.5) * 0.06) * max(_SparkDrift, 0.01);
  sparkUv.y += sparkRise * 0.08;

  float2 sparkGrid = sparkUv * float2(max(_SparkGridX, 1.0), max(_SparkGridY, 1.0));
  sparkGrid.x += step(0.5, frac(floor(sparkGrid.y) * 0.5)) * 0.5;

  float2 sparkCell = floor(sparkGrid);
  float sparkSpawn = FirePreviewHash21(sparkCell + float2(3.1, 9.7));
  float sparkGate = step(_SparkThreshold, sparkSpawn);
  float sparkLife = saturate(1.0 - frac((sparkUv.y * max(_SparkLife, 0.2)) - (sparkRise * 0.35) + (sparkSpawn * 5.0)));
  float sparkSize = lerp(min(_SparkSizeMin, _SparkSizeMax), max(_SparkSizeMin, _SparkSizeMax), FirePreviewHash21(sparkCell + 55.1));
  float2 sparkOffset = (float2(
    FirePreviewHash21(sparkCell + 17.3),
    FirePreviewHash21(sparkCell + 31.7)) - 0.5) * lerp(0.1, 0.45, saturate(_SparkDrift * 0.5));
  float sparkDistance = length((frac(sparkGrid) - 0.5) + (sparkOffset * sparkLife));
  float sparkShape = FirePreviewPow01(saturate(1.0 - (sparkDistance / max(sparkSize, 1e-4))), 2.0);

  float sparkBandStart = min(_SparkBandStart, _SparkBandEnd);
  float sparkBandEnd = max(_SparkBandStart, _SparkBandEnd);
  float sparkBand = smoothstep(flameHeight * sparkBandStart, min(1.5, flameHeight * sparkBandEnd), uv.y)
    * (1.0 - smoothstep(0.96, 1.0, uv.y));
  float sparkEnvelope = sparkBand * pow(max(xFuel, 1e-4), max(_SparkEnvelopePower, 0.1));
  return sparkGate * sparkLife * sparkShape * sparkEnvelope * _SparkAmount;
}

void FirePreviewCore_float(
  float2 uv,
  float mask,
  float sourceFeature,
  float breakupLarge,
  float breakupDetail,
  float flowX,
  float previewTime,
  out float3 color,
  out float alpha
) {
  float clampedHeight = max(_FlameHeight, 0.02);
  float normalizedHeight = saturate(uv.y / clampedHeight);
  float remaining = 1.0 - normalizedHeight;
  float taperExponent = max(_TaperExponent, 0.1);
  float tipFactor = pow(saturate(normalizedHeight), taperExponent);
  float xFuel = saturate(1.0 - abs((uv.x - 0.5) * 2.0));
  float yFalloff = saturate(max(_VerticalFalloff, 0.2) - (uv.y / clampedHeight));

  float lateralWave = sin((uv.y * _TongueFrequency * 6.2831853) - (previewTime * (_FlowSpeed * 2.4)));
  float lateralOffset = ((flowX * 0.65) + (lateralWave * 0.35)) * _TongueStrength * tipFactor;

  float taperWidth = lerp(_BodyWidth, max(_TipWidth, 0.01), pow(normalizedHeight, taperExponent));
  float centerDistance = abs(((uv.x - 0.5) * 2.0) + (lateralOffset * 0.85));
  float widthMask = 1.0 - smoothstep(taperWidth, taperWidth + max(0.001, _EdgeSoftness * 2.0), centerDistance);
  float featureTongues = saturate(sourceFeature * smoothstep(0.04, 0.82, normalizedHeight));

  float fuelMask = saturate(
    mask *
    widthMask *
    lerp(1.0, xFuel, 0.75) *
    lerp(0.78, 1.0 + (_SourceFeatureBoost * 0.18), saturate(sourceFeature)));

  float displace = (
    ((breakupLarge - 0.5) * 1.1) +
    ((breakupDetail - 0.5) * 0.45) +
    (flowX * 0.65) +
    (lateralOffset * 0.85)
  ) * _Breakup;

  float advectedNoise = saturate(
    ((breakupLarge * 0.68) + (breakupDetail * 0.32)) +
    displace +
    (remaining * 0.18) +
    (sourceFeature * 0.18));

  float flamePower = max(0.08, 0.3 * max(xFuel, 0.12));
  float flames = FirePreviewPow01(normalizedHeight, flamePower) * FirePreviewPow01(advectedNoise, flamePower);
  float flame = fuelMask * yFalloff * FirePreviewPow01(1.0 - (flames * flames * flames), 8.0);
  flame *= 1.0 - smoothstep(clampedHeight, clampedHeight + (_EdgeSoftness * 4.0), uv.y);
  flame = smoothstep(0.0, 1.0 - min(_EdgeSoftness, 0.95), flame);

  float innerWidth = max(taperWidth * _InnerWidthRatio, 0.01);
  float edgeSpan = max(taperWidth - innerWidth, 1e-4);
  float edgeDistance = saturate((centerDistance - innerWidth) / edgeSpan);

  float ribbonPhase = (
    (uv.y * _RibbonFrequency) +
    (flowX * 5.6) +
    ((breakupLarge - 0.5) * 11.0) +
    ((breakupDetail - 0.5) * 8.0) -
    (previewTime * _FlowSpeed * 5.4)
  );
  float ribbonWave = 0.5 + (0.5 * sin(ribbonPhase));
  float ribbonThresholdMin = min(_RibbonThresholdMin, _RibbonThresholdMax);
  float ribbonThresholdMax = max(_RibbonThresholdMin, _RibbonThresholdMax);
  float ribbonMask = smoothstep(ribbonThresholdMin, max(ribbonThresholdMin + 1e-4, ribbonThresholdMax), ribbonWave);
  ribbonMask *= FirePreviewPow01(saturate(advectedNoise + 0.05), 1.1);
  ribbonMask *= smoothstep(0.12, 0.9, normalizedHeight);
  ribbonMask = saturate(max(ribbonMask, featureTongues * _RibbonInfluence));

  float cavity = flame * FirePreviewPow01(saturate(1.0 - edgeDistance), max(_InnerSharpness, 0.2));
  float interior = cavity * saturate(0.62 + (remaining * 0.12) - ((breakupLarge - 0.5) * 0.18));

  float shell = flame * smoothstep(0.08, 0.82, edgeDistance);
  float edgeBoost = lerp(0.85, 2.4, saturate(_CoreIntensity / 2.5));
  float rim = flame * FirePreviewPow01(edgeDistance, max(_RimPower, 0.2));
  rim *= saturate(0.72 + (ribbonMask * 0.45) + (tipFactor * 0.18));

  float tipGlow = flame * FirePreviewPow01(normalizedHeight, 1.65) * saturate(advectedNoise + 0.12);
  float edgeHot = saturate((shell * 0.82) + (ribbonMask * 0.28) + (tipGlow * 0.14) + (featureTongues * 0.16));
  float edgeBright = saturate((rim * edgeBoost) + (tipGlow * 0.18 * edgeBoost) + (featureTongues * 0.12));
  interior *= saturate(1.0 - (edgeHot * 0.35) - (edgeBright * 0.25));

  float veilBase = saturate(
    (fuelMask * (0.42 + (remaining * 0.22)) * FirePreviewPow01(advectedNoise, max(_VeilExponent, 0.2))) -
    (flame * 0.72));
  float veilStart = min(_VeilStart, _VeilEnd);
  float veilEnd = max(_VeilStart, _VeilEnd);
  float veil = veilBase * smoothstep(veilStart, max(veilStart + 1e-4, veilEnd), normalizedHeight);
  veil *= 1.0 - smoothstep(0.96, 1.0, uv.y);
  veil *= _VeilStrength;

  float spark = FirePreviewSparkLayer(
    uv,
    xFuel,
    clampedHeight,
    previewTime,
    lateralOffset,
    breakupLarge,
    breakupDetail);

  float3 veilColor = lerp(_BodyColor.rgb, _BrightColor.rgb, 0.58);
  float3 flameColor =
    (_BodyColor.rgb * interior * _BodyIntensity) +
    (_HotColor.rgb * edgeHot * _HotIntensity) +
    (_BrightColor.rgb * edgeBright * _BrightIntensity);
  flameColor += veilColor * veil;
  flameColor += _HotColor.rgb * spark * _SparkHotIntensity;
  flameColor += _BrightColor.rgb * spark * _SparkBrightIntensity;

  color = flameColor;
  alpha = saturate(
    (((interior * 0.9) + (edgeHot * 0.5) + (edgeBright * 0.55) + (veil * 0.3)) * _Opacity) +
    (spark * _Opacity * 0.75));
}

#endif
