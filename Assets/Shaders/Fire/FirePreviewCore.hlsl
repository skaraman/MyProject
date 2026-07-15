#ifndef FIRE_PREVIEW_CORE_INCLUDED
#define FIRE_PREVIEW_CORE_INCLUDED

void FirePreviewCore_float(
  float2 uv,
  float previewTime,
  out float3 color,
  out float alpha
) {
  float panTime = previewTime * _FireSpeed;
  
  // Sample distortion/flow map
  float2 flowUv = uv + float2(0.0, -panTime * 0.8);
  float2 flowVec = (tex2D(_FlowTex, frac(flowUv)).rg * 2.0) - 1.0;
  
  // Calculate distorted UV for the sprite
  // Distortion increases towards the top of the sprite
  float2 distortedUv = uv + (flowVec * _Distortion * uv.y * 0.5);
  
  // Sample the main sprite with distortion
  float4 sourceSample = tex2D(_MainTex, distortedUv);
  
  // Noise sampling for fire
  float2 noiseUv = distortedUv * 2.0 + float2(0.0, -panTime * 1.2);
  float noise = tex2D(_NoiseTex, frac(noiseUv)).r;
  
  // Calculate heat/burn based on vertical position, noise, and burn amount
  float burnProgress = _BurnAmount * 1.5;
  float noiseEffect = (noise - 0.5) * _FireSpread;
  float heat = saturate((distortedUv.y - 1.1 + burnProgress) * 1.5 + noiseEffect);
  heat *= smoothstep(0.0, 0.05, _BurnAmount); // Ensure no fire when burn is strictly 0
  
  // Fire gradients
  float fireEdge = smoothstep(0.0, 0.3, heat);
  float fireCore = smoothstep(0.3, 0.7, heat);
  float ash = smoothstep(0.7, 1.0, heat);
  
  // Mix colors
  float3 fireColor = lerp(_SmokeColor.rgb, _EdgeColor.rgb, fireEdge);
  fireColor = lerp(fireColor, _CoreColor.rgb, fireCore);
  
  // Combine with original sprite
  float dissolveAlpha = 1.0 - ash;
  float3 finalColor = lerp(sourceSample.rgb, fireColor * _FireIntensity, fireEdge);
  
  color = finalColor;
  alpha = sourceSample.a * dissolveAlpha;
}

#endif
