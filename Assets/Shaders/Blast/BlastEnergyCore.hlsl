#ifndef ESPERANZA_BLAST_ENERGY_CORE_INCLUDED
#define ESPERANZA_BLAST_ENERGY_CORE_INCLUDED

TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);
TEXTURE2D(_NormalMap);
SAMPLER(sampler_NormalMap);

CBUFFER_START(UnityPerMaterial)
  float4 _PrimaryColor;
  float4 _SecondaryColor;
  float4 _SpriteUvRect;
  float _Speed;
  float _Swirl;
  float _Bands;
  float _GleamWidth;
  float _Intensity;
  float _PreviewTime;
  float _UsePreviewTime;
  float _SpriteEffectActive;
  float _NormalStrength;
  float _LightInfluence;
CBUFFER_END

struct BlastEnergyData {
  half3 color;
  half alpha;
  half sourceAlpha;
  float2 localUv;
};

half SampleBlastMask(float2 localUv) {
  float2 clampedUv = saturate(localUv);
  float2 atlasUv = _SpriteUvRect.xy + (clampedUv * _SpriteUvRect.zw);
  return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, atlasUv).a;
}

float2 RotateBlastPoint(float2 position, float angle) {
  float sine = sin(angle);
  float cosine = cos(angle);
  float x = (position.x * cosine) - (position.y * sine);
  float y = (position.x * sine) + (position.y * cosine);
  return float2(x, y);
}

void EvaluateBlastOrbit(
  float2 position,
  float rotation,
  float flattening,
  float radius,
  float strandWidth,
  float travel,
  float direction,
  float phase,
  out float strand,
  out float knot
) {
  float2 rotatedPoint = RotateBlastPoint(position, rotation);
  float safeFlattening = max(flattening, 0.12);
  float2 circlePoint = float2(rotatedPoint.x, rotatedPoint.y / safeFlattening);
  float orbitAngle = atan2(circlePoint.y, circlePoint.x);

  float wobbleFrequency = 2.0 + (_Swirl * 0.35);
  float wobble = sin((orbitAngle * wobbleFrequency) + travel + phase);
  wobble *= 0.012 + (_Swirl * 0.003);

  float orbitDistance = abs(length(circlePoint) - radius - wobble);
  float lineCore = 1.0 - smoothstep(strandWidth, strandWidth * 2.2, orbitDistance);
  float lineHalo = 1.0 - smoothstep(strandWidth * 2.0, strandWidth * 7.0, orbitDistance);

  float tiltDepth = sqrt(saturate(1.0 - (safeFlattening * safeFlattening)));
  float depth = circlePoint.y * tiltDepth;
  float normalizedDepth = depth / max(radius, 0.001);
  float frontAmount = saturate((normalizedDepth * 0.5) + 0.5);
  float depthBrightness = lerp(0.18, 1.2, frontAmount);

  float knotPhase = orbitAngle * max(_Bands, 1.0);
  knotPhase -= travel * direction * 1.7;
  knotPhase += phase;
  float knotWave = (sin(knotPhase) * 0.5) + 0.5;
  float knotCore = pow(saturate(knotWave), 18.0);

  strand = (lineCore + (lineHalo * 0.24)) * depthBrightness;
  knot = lineCore * knotCore * depthBrightness;
}

BlastEnergyData EvaluateBlastEnergy(float2 atlasUv, half4 vertexColor) {
  BlastEnergyData output;
  float2 rectSize = max(_SpriteUvRect.zw, float2(0.0001, 0.0001));
  output.localUv = (atlasUv - _SpriteUvRect.xy) / rectSize;
  output.sourceAlpha = SampleBlastMask(output.localUv);

  float sourcePresence = SampleBlastMask(float2(0.5, 0.5));
  sourcePresence = max(sourcePresence, SampleBlastMask(float2(0.25, 0.5)));
  sourcePresence = max(sourcePresence, SampleBlastMask(float2(0.75, 0.5)));
  sourcePresence = max(sourcePresence, SampleBlastMask(float2(0.5, 0.25)));
  sourcePresence = max(sourcePresence, SampleBlastMask(float2(0.5, 0.75)));
  sourcePresence = saturate(sourcePresence * 4.0);
  sourcePresence = max(sourcePresence, saturate(_SpriteEffectActive));

  float timeValue = lerp(_Time.y, _PreviewTime, saturate(_UsePreviewTime));
  float travel = timeValue * _Speed;
  float2 position = (output.localUv * 2.0) - 1.0;

  float strandA;
  float knotA;
  float rotationA = 0.15 + (sin(travel * 0.19) * 0.18);
  float flatteningA = 0.46 + (sin(travel * 0.31) * 0.08);
  EvaluateBlastOrbit(position, rotationA, flatteningA, 0.54, _GleamWidth, travel, 1.0, 0.0, strandA, knotA);

  float strandB;
  float knotB;
  float rotationB = 1.05 - (travel * 0.07);
  float flatteningB = 0.32 + (sin(travel * 0.23 + 1.7) * 0.07);
  EvaluateBlastOrbit(position, rotationB, flatteningB, 0.64, _GleamWidth * 0.82, travel, -1.0, 1.9, strandB, knotB);

  float strandC;
  float knotC;
  float rotationC = 2.1 + (travel * 0.045);
  float flatteningC = 0.58 + (sin(travel * 0.27 + 3.1) * 0.1);
  EvaluateBlastOrbit(position, rotationC, flatteningC, 0.73, _GleamWidth * 0.72, travel, 1.0, 3.7, strandC, knotC);

  float strandD;
  float knotD;
  float rotationD = -0.72 - (travel * 0.035);
  float flatteningD = 0.24 + (sin(travel * 0.17 + 4.2) * 0.06);
  EvaluateBlastOrbit(position, rotationD, flatteningD, 0.82, _GleamWidth * 0.62, travel, -1.0, 5.3, strandD, knotD);

  float strandEnergy = strandA + strandB + strandC + strandD;
  float knotEnergy = knotA + knotB + knotC + knotD;

  float volumeRadius = length(position * float2(0.92, 1.0));
  float volumeMask = 1.0 - smoothstep(0.82, 1.04, volumeRadius);
  volumeMask *= sourcePresence;
  float centerOpening = smoothstep(0.12, 0.38, volumeRadius);
  strandEnergy *= centerOpening;
  strandEnergy *= sourcePresence;
  knotEnergy *= centerOpening;
  knotEnergy *= sourcePresence;

  float sourceSeed = saturate(output.sourceAlpha * 1.4);
  float ambientVolume = volumeMask * 0.08;
  float whiteAmount = saturate(knotEnergy * 1.25);
  whiteAmount += saturate(strandEnergy * 0.2);
  whiteAmount += sourceSeed * 0.45;
  whiteAmount = saturate(whiteAmount);

  float brightness = ambientVolume;
  brightness += strandEnergy * 0.78;
  brightness += knotEnergy * 1.25;
  brightness += sourceSeed * 0.32;

  output.color = lerp(_SecondaryColor.rgb, _PrimaryColor.rgb, whiteAmount);
  output.color *= brightness * _Intensity;
  output.color *= vertexColor.rgb;

  output.alpha = output.sourceAlpha * 0.32;
  output.alpha += volumeMask * saturate(strandEnergy * 0.48);
  output.alpha += volumeMask * saturate(knotEnergy * 0.8);
  output.alpha += ambientVolume * 0.45;
  output.alpha = saturate(output.alpha) * vertexColor.a;
  return output;
}

#endif
