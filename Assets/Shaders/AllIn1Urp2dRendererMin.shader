Shader "AllIn1SpriteShader/OptimizedAllIn1Urp2dRenderer" {
  Properties {
    _MainTex ("Main Texture", 2D) = "white" {}
    _Color("Main Color", Color) = (1,1,1,1)
    _Alpha("General Alpha", Range(0,1)) = 1
    _MaskTex("Mask Texture (A)", 2D) = "white" {}
    _LitAmount("Lit Amount", Range(0,1)) = 0
    _GlowAffectLight("Glow Affect Light", Range(0,1)) = 0

    // Explosion Properties
    _Explosions("Number of Explosions", Int) = 0
    _ExplosionSize("Explosion Size", Range(0,1)) = 0.1
    _ExplosionStrength("Explosion Strength", Range(0,1)) = 0.1
    _ExplosionSpeed("Explosion Speed", Range(0,10)) = 1.0
    _ExplosionDistance("Explosion Distance", Range(0,1)) = 0.5
    _ExplosionCenters("Explosion Centers", Vector) = (0,0,0,0) // Extendable via script for multiple centers

    // Existing Properties
    _GlowColor("Glow Color", Color) = (1,1,1,1)
    _GlowAmount("Glow Amount", Range(0,10)) = 0
    _GlowTexture("Glow Texture", 2D) = "white" {}
    _Fade("Fade Amount", Range(0,1)) = 0
    _FadeBurnWidth("Fade Burn Width", Range(0,0.2)) = 0.05
    _FadeBurnColor("Fade Burn Color", Color) = (1,1,0,1)
    _FadeTexture("Fade Texture", 2D) = "white" {}
    _FadeTextureSize("Fade Texture Size", Vector) = (1,1,0,0)
    _FadeTextureSpeed("Fade Texture Speed", Vector) = (0,0,0,0)
    _FadeTextureStrength("Fade Texture Strength", Range(0,1)) = 0
    _OutColor("Outline Color", Color) = (1,1,1,1)
    _OutDist("Outline Distance", Range(0,0.01)) = 0.002
    _OutAmount("Outline Amount", Range(0,1)) = 0
    _OutTex("Outline Texture", 2D) = "white" {}
    _OutTexTile("Outline Texture Tile", Vector) = (1,1,0,0)
    _OutTexStrength("Outline Texture Strength", Range(0,1)) = 0
    _OutDistortion("Outline Distortion", 2D) = "white" {}
    _OutDistortionTile("Outline Distortion Tile", Vector) = (1,1,0,0)
    _OutDistortionSpeed("Outline Distortion Speed", Vector) = (0,0,0,0)
    _OutDistortionStrength("Outline Distortion Strength", Range(0,0.1)) = 0
    _InnerOutlineColor("Inner Outline Color", Color) = (1,1,1,1)
    _InnerOutlineWidth("Inner Outline Width", Range(0,0.1)) = 0
    _InnerOutlineAmount("Inner Outline Amount", Range(0,1)) = 0
    _InnerOutlineAlpha("Inner Outline Alpha", Range(0,1)) = 1
    _GradientColor1("Gradient Color 1", Color) = (1,1,1,1)
    _GradientColor2("Gradient Color 2", Color) = (1,1,1,1)
    _GradientTexture("Gradient Texture", 2D) = "white" {}
    _GradientRotation("Gradient Rotation", Range(0,360)) = 0
    _GradientScale("Gradient Scale", Range(0,10)) = 1
    _ColorSwapTex("Color Swap Texture", 2D) = "white" {}
    _ColorSwapAmount("Color Swap Amount", Range(0,1)) = 0
    _ColorSwapR("Color Swap Red", Color) = (1,0,0,1)
    _ColorSwapG("Color Swap Green", Color) = (0,1,0,1)
    _ColorSwapB("Color Swap Blue", Color) = (0,0,1,1)
    _ColorSwapTolerance("Color Swap Tolerance", Range(0,1)) = 0.2
    _ColorSwapSmoothness("Color Swap Smoothness", Range(0,1)) = 0.1
    _HsvShift("Hue Shift", Range(0,360)) = 0
    _HsvSaturation("Saturation Shift", Range(0,2)) = 1
    _HsvValue("Value Shift", Range(0,2)) = 1
    _HitEffectColor("Hit Effect Color", Color) = (1,1,1,1)
    _HitEffectAmount("Hit Effect Amount", Range(0,1)) = 0
    _HitEffectBlend("Hit Effect Blend", Range(0,1)) = 0
    _NegativeAmount("Negative Amount", Range(0,1)) = 0
    _PixelateAmount("Pixelate Amount", Range(0,0.1)) = 0
    _ColorRampTex("Color Ramp Texture", 2D) = "white" {}
    _ColorRampAmount("Color Ramp Amount", Range(0,1)) = 0
    _ColorRampAffectOutline("Color Ramp Affect Outline", Range(0,1)) = 0
    _PosterizeLevels("Posterize Levels", Range(1,256)) = 8
    _PosterizeAmount("Posterize Amount", Range(0,1)) = 0
    _PosterizeOutline("Posterize Outline", Range(0,1)) = 0
    _BlurAmount("Blur Amount", Range(0,0.01)) = 0
    _BlurHD("Blur HD", Range(0,1)) = 0
    _MotionBlurAmount("Motion Blur Amount", Range(0,0.1)) = 0
    _MotionBlurAngle("Motion Blur Angle", Range(0,360)) = 0
    _GhostAmount("Ghost Amount", Range(0,1)) = 0
    _GhostAngle("Ghost Angle", Range(0,360)) = 0
    _HologramFrequency("Hologram Frequency", Range(0,100)) = 10
    _HologramSpeed("Hologram Speed", Range(0,10)) = 1
    _HologramAmount("Hologram Amount", Range(0,1)) = 0
    _HologramColor("Hologram Color", Color) = (1,1,1,1)
    _HologramDistortion("Hologram Distortion", Range(0,0.1)) = 0
    _ChromaticAberrationAmount("Chromatic Aberration Amount", Range(0,0.1)) = 0
    _ChromaticAberrationSpeed("Chromatic Aberration Speed", Range(0,10)) = 1
    _GlitchAmount("Glitch Amount", Range(0,1)) = 0
    _FlickerAmount("Flicker Amount", Range(0,1)) = 0
    _FlickerSpeed("Flicker Speed", Range(0,10)) = 1
    _FlickerRandomness("Flicker Randomness", Range(0,1)) = 0.5
    _ShineColor("Shine Color", Color) = (1,1,1,1)
    _ShineSpeed("Shine Speed", Range(0,10)) = 1
    _ShineWidth("Shine Width", Range(0,1)) = 0.2
    _ShineAngle("Shine Angle", Range(0,360)) = 0
    _ShineAmount("Shine Amount", Range(0,1)) = 0
    _ShineBlend("Shine Blend", Range(0,1)) = 0
    _Contrast("Contrast", Range(0,2)) = 1
    _Brightness("Brightness", Range(0,2)) = 1
    _OverlayTex("Overlay Texture", 2D) = "white" {}
    _OverlayTile("Overlay Tile", Vector) = (1,1,0,0)
    _OverlaySpeed("Overlay Speed", Vector) = (0,0,0,0)
    _OverlayAmount("Overlay Amount", Range(0,1)) = 0
    _OverlayAlpha("Overlay Alpha", Range(0,1)) = 1
    _AlphaCutoff("Alpha Cutoff", Range(0,1)) = 0
    _AlphaRound("Alpha Round", Range(0,1)) = 0
    _DoodleAmount("Doodle Amount", Range(0,0.1)) = 0
    _DoodleSpeed("Doodle Speed", Range(0,10)) = 1
    _WindSpeed("Wind Speed", Range(0,10)) = 1
    _WindAmount("Wind Amount", Range(0,1)) = 0
    _WindManual("Wind Manual", Range(0,1)) = 0
    _WindForce("Wind Force", Range(-1,1)) = 0
    _WaveAmplitude("Wave Amplitude", Range(0,0.1)) = 0
    _WaveFrequency("Wave Frequency", Range(0,100)) = 10
    _WaveSpeed("Wave Speed", Range(0,10)) = 1
    _WaveDirection("Wave Direction", Range(0,360)) = 0
    _WaveAmount("Wave Amount", Range(0,1)) = 0
    _RoundWaveAmplitude("Round Wave Amplitude", Range(0,0.1)) = 0
    _RoundWaveFrequency("Round Wave Frequency", Range(0,100)) = 10
    _OffsetX("Offset X", Range(-1,1)) = 0
    _OffsetY("Offset Y", Range(-1,1)) = 0
    _ClipTop("Clip Top", Range(0,1)) = 1
    _ClipBottom("Clip Bottom", Range(0,1)) = 0
    _ClipLeft("Clip Left", Range(0,1)) = 0
    _ClipRight("Clip Right", Range(0,1)) = 1
    _RadialClipCenter("Radial Clip Center", Vector) = (0.5,0.5,0,0)
    _RadialClipRadius("Radial Clip Radius", Range(0,1)) = 0.5
    _RadialClipAmount("Radial Clip Amount", Range(0,1)) = 0
    _TextureScrollSpeedX("Texture Scroll Speed X", Range(-10,10)) = 0
    _TextureScrollSpeedY("Texture Scroll Speed Y", Range(-10,10)) = 0
    _ZoomAmount("Zoom Amount", Range(0.1,10)) = 1
    _DistortionTex("Distortion Texture", 2D) = "white" {}
    _DistortionTile("Distortion Tile", Vector) = (1,1,0,0)
    _DistortionSpeed("Distortion Speed", Vector) = (0,0,0,0)
    _DistortionAmount("Distortion Amount", Range(0,0.1)) = 0
    _TwistCenter("Twist Center", Vector) = (0.5,0.5,0,0)
    _TwistAmount("Twist Amount", Range(-1,1)) = 0
    _TwistSpeed("Twist Speed", Range(0,10)) = 1
    _TwistScale("Twist Scale", Range(0,10)) = 1
    _RotateAmount("Rotate Amount", Range(-1,1)) = 0
    _FishEyeAmount("Fish Eye Amount", Range(-1,1)) = 0
    _PinchAmount("Pinch Amount", Range(-1,1)) = 0
    _ShakeAmplitude("Shake Amplitude", Range(0,0.1)) = 0
    _ShakeFrequency("Shake Frequency", Range(0,100)) = 10
    _ShakeSpeed("Shake Speed", Range(0,10)) = 1
    _BillboardY("Billboard Y", Range(0,1)) = 0
    _GradientColorRamp("Gradient Color Ramp", 2D) = "white" {}
    _GradientColorRampAmount("Gradient Color Ramp Amount", Range(0,1)) = 0
    _ShineWidth2("Shine Width 2", Range(0,1)) = 0.2
    _GhostOffset("Ghost Offset", Range(0,0.1)) = 0
    _GlitchFrequency("Glitch Frequency", Range(0,100)) = 10
    _HologramOffset("Hologram Offset", Range(0,0.1)) = 0
    _EditorDrawers("Editor Drawers", Int) = 14
  }

  HLSLINCLUDE
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
  #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/LightingUtility.hlsl"
  ENDHLSL

  SubShader {
    Tags {
      "Queue" = "Transparent"
      "RenderType" = "Transparent"
      "RenderPipeline" = "UniversalPipeline"
      "CanUseSpriteAtlas" = "True"
      "PreviewType" = "Plane"
    }
    Blend SrcAlpha OneMinusSrcAlpha
    Cull Off
    ZWrite Off
    ZTest LEqual

    Pass {
      Tags { "LightMode" = "Universal2D" "RenderPipeline" = "UniversalPipeline" }
      HLSLPROGRAM
      #pragma prefer_hlslcc gles
      #pragma vertex CombinedShapeLightVertex
      #pragma fragment CombinedShapeLightFragment
      #pragma multi_compile_instancing
      #pragma multi_compile USE_SHAPE_LIGHT_TYPE_0 __
      #pragma multi_compile USE_SHAPE_LIGHT_TYPE_1 __
      #pragma multi_compile USE_SHAPE_LIGHT_TYPE_2 __
      #pragma multi_compile USE_SHAPE_LIGHT_TYPE_3 __
      #pragma multi_compile GLOW_ON __
      #pragma multi_compile FADE_ON __
      #pragma multi_compile OUTBASE_ON __
      #pragma multi_compile INNEROUTLINE_ON __
      #pragma multi_compile GRADIENT_ON __
      #pragma multi_compile COLORSWAP_ON __
      #pragma multi_compile HSV_ON __
      #pragma multi_compile COLORRAMP_ON __
      #pragma multi_compile HITEFFECT_ON __
      #pragma multi_compile NEGATIVE_ON __
      #pragma multi_compile PIXELATE_ON __
      #pragma multi_compile POSTERIZE_ON __
      #pragma multi_compile BLUR_ON __
      #pragma multi_compile MOTIONBLUR_ON __
      #pragma multi_compile GHOST_ON __
      #pragma multi_compile HOLOGRAM_ON __
      #pragma multi_compile CHROMABERR_ON __
      #pragma multi_compile GLITCH_ON __
      #pragma multi_compile FLICKER_ON __
      #pragma multi_compile SHINE_ON __
      #pragma multi_compile CONTRAST_ON __
      #pragma multi_compile OVERLAY_ON __
      #pragma multi_compile ALPHACUTOFF_ON __
      #pragma multi_compile ALPHAROUND_ON __
      #pragma multi_compile DOODLE_ON __
      #pragma multi_compile WIND_ON __
      #pragma multi_compile WAVEUV_ON __
      #pragma multi_compile ROUNDWAVEUV_ON __
      #pragma multi_compile OFFSETUV_ON __
      #pragma multi_compile CLIPPING_ON __
      #pragma multi_compile RADIALCLIPPING_ON __
      #pragma multi_compile TEXTURESCROLL_ON __
      #pragma multi_compile ZOOMUV_ON __
      #pragma multi_compile DISTORT_ON __
      #pragma multi_compile TWISTUV_ON __
      #pragma multi_compile ROTATEUV_ON __
      #pragma multi_compile FISHEYE_ON __
      #pragma multi_compile PINCH_ON __
      #pragma multi_compile SHAKEUV_ON __
      #pragma multi_compile BILBOARD_ON __
      #pragma multi_compile ATLAS_ON __
      #pragma shader_feature_local EXPLOSION_ON

      struct Attributes {
        float4 vertex : POSITION;
        float2 uv : TEXCOORD0;
        half4 color : COLOR;
        float2 lightingUV : TEXCOORD1;
        UNITY_VERTEX_INPUT_INSTANCE_ID
      };

      struct Varyings {
        float2 uv : TEXCOORD0;
        float4 vertex : SV_POSITION;
        half4 color : COLOR;
        float2 lightingUV : TEXCOORD1;
        UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
      };

      // Texture and base uniforms
      TEXTURE2D(_MainTex);
      SAMPLER(sampler_MainTex);
      TEXTURE2D(_MaskTex);
      SAMPLER(sampler_MaskTex);
      half4 _Color;
      half _Alpha;
      half _LitAmount;
      half _GlowAffectLight;

      // Explosion uniforms
      #if EXPLOSION_ON
      int _Explosions;
      half _ExplosionSize, _ExplosionStrength, _ExplosionSpeed, _ExplosionDistance;
      half4 _ExplosionCenters[10]; // Max 10 explosions; adjustable
      #endif

      // Existing uniforms
      half4 _GlowColor;
      half _GlowAmount;
      TEXTURE2D(_GlowTexture);
      SAMPLER(sampler_GlowTexture);
      half _Fade, _FadeBurnWidth;
      half4 _FadeBurnColor;
      TEXTURE2D(_FadeTexture);
      SAMPLER(sampler_FadeTexture);
      half2 _FadeTextureSize, _FadeTextureSpeed;
      half _FadeTextureStrength;
      half4 _OutColor;
      half _OutDist, _OutAmount;
      TEXTURE2D(_OutTex);
      SAMPLER(sampler_OutTex);
      half2 _OutTexTile;
      half _OutTexStrength;
      TEXTURE2D(_OutDistortion);
      SAMPLER(sampler_OutDistortion);
      half2 _OutDistortionTile, _OutDistortionSpeed;
      half _OutDistortionStrength;
      half4 _InnerOutlineColor;
      half _InnerOutlineWidth, _InnerOutlineAmount, _InnerOutlineAlpha;
      half4 _GradientColor1, _GradientColor2;
      TEXTURE2D(_GradientTexture);
      SAMPLER(sampler_GradientTexture);
      half _GradientRotation, _GradientScale;
      TEXTURE2D(_ColorSwapTex);
      SAMPLER(sampler_ColorSwapTex);
      half _ColorSwapAmount;
      half4 _ColorSwapR, _ColorSwapG, _ColorSwapB;
      half _ColorSwapTolerance, _ColorSwapSmoothness;
      half _HsvShift, _HsvSaturation, _HsvValue;
      half4 _HitEffectColor;
      half _HitEffectAmount, _HitEffectBlend;
      half _NegativeAmount;
      half _PixelateAmount;
      TEXTURE2D(_ColorRampTex);
      SAMPLER(sampler_ColorRampTex);
      half _ColorRampAmount, _ColorRampAffectOutline;
      half _PosterizeLevels, _PosterizeAmount, _PosterizeOutline;
      half _BlurAmount, _BlurHD;
      half _MotionBlurAmount, _MotionBlurAngle;
      half _GhostAmount, _GhostAngle;
      half _HologramFrequency, _HologramSpeed, _HologramAmount;
      half4 _HologramColor;
      half _HologramDistortion;
      half _ChromaticAberrationAmount, _ChromaticAberrationSpeed;
      half _GlitchAmount;
      half _FlickerAmount, _FlickerSpeed, _FlickerRandomness;
      half4 _ShineColor;
      half _ShineSpeed, _ShineWidth, _ShineAngle, _ShineAmount, _ShineBlend;
      half _Contrast, _Brightness;
      TEXTURE2D(_OverlayTex);
      SAMPLER(sampler_OverlayTex);
      half2 _OverlayTile, _OverlaySpeed;
      half _OverlayAmount, _OverlayAlpha;
      half _AlphaCutoff, _AlphaRound;
      half _DoodleAmount, _DoodleSpeed;
      half _WindSpeed, _WindAmount, _WindManual, _WindForce;
      half _WaveAmplitude, _WaveFrequency, _WaveSpeed, _WaveDirection, _WaveAmount;
      half _RoundWaveAmplitude, _RoundWaveFrequency;
      half _OffsetX, _OffsetY;
      half _ClipTop, _ClipBottom, _ClipLeft, _ClipRight;
      half2 _RadialClipCenter;
      half _RadialClipRadius, _RadialClipAmount;
      half _TextureScrollSpeedX, _TextureScrollSpeedY;
      half _ZoomAmount;
      TEXTURE2D(_DistortionTex);
      SAMPLER(sampler_DistortionTex);
      half2 _DistortionTile, _DistortionSpeed;
      half _DistortionAmount;
      half2 _TwistCenter;
      half _TwistAmount, _TwistSpeed, _TwistScale;
      half _RotateAmount;
      half _FishEyeAmount, _PinchAmount;
      half _ShakeAmplitude, _ShakeFrequency, _ShakeSpeed;
      half _BillboardY;
      TEXTURE2D(_GradientColorRamp);
      SAMPLER(sampler_GradientColorRamp);
      half _GradientColorRampAmount;
      half _ShineWidth2, _GhostOffset, _GlitchFrequency, _HologramOffset;

      #if USE_SHAPE_LIGHT_TYPE_0
      SHAPE_LIGHT(0)
      #endif

      #if USE_SHAPE_LIGHT_TYPE_1
      SHAPE_LIGHT(1)
      #endif

      #if USE_SHAPE_LIGHT_TYPE_2
      SHAPE_LIGHT(2)
      #endif

      #if USE_SHAPE_LIGHT_TYPE_3
      SHAPE_LIGHT(3)
      #endif

      Varyings CombinedShapeLightVertex(Attributes v) {
        Varyings o;
        UNITY_SETUP_INSTANCE_ID(v);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
        o.vertex = TransformObjectToHClip(v.vertex.xyz);
        o.uv = v.uv;
        o.color = v.color * _Color;
        o.lightingUV = v.lightingUV;
        return o;
      }

      // Explosion function
      #if EXPLOSION_ON
      half2 explosion(half2 uv, half time, half2 center, half eSize, half strength) {
        half2 dir = uv - center;
        half dist = length(dir);
        dir = normalize(dir);
        half waveTime = time * _ExplosionSpeed;
        half innerRadius = eSize * waveTime;
        half outerRadius = innerRadius + strength;
        if (dist > innerRadius && dist < outerRadius) {
          half percent = 1.0 - smoothstep(innerRadius, outerRadius, dist);
          uv += dir * percent * strength;
        }
        return uv;
      }
      #endif

      half3 HsvToRgb(half3 c) {
        half4 K = half4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
        half3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
        return c.z * lerp(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
      }

      half3 RgbToHsv(half3 c) {
        half4 K = half4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
        half4 p = lerp(half4(c.bg, K.wz), half4(c.gb, K.xy), step(c.b, c.g));
        half4 q = lerp(half4(p.xyw, c.r), half4(c.r, p.yzx), step(p.x, c.r));
        half d = q.x - min(q.w, q.y);
        half e = 1.0e-10;
        return half3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
      }

      #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

      half4 CombinedShapeLightFragment(Varyings i) : SV_Target {
        UNITY_SETUP_INSTANCE_ID(i);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

        half2 uv = i.uv;

        // Apply explosion effect
        #if EXPLOSION_ON
        for (int exp = 0; exp < _Explosions; exp++) {
          uv = explosion(uv, _Time.y, _ExplosionCenters[exp].xy, _ExplosionSize, _ExplosionStrength);
        }
        #endif

        // Sample texture
        half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * i.color;
        half originalAlpha = col.a;

        // Apply glow
        #if GLOW_ON
        half4 glow = _GlowColor * _GlowAmount;
        #if GLOWTEX_ON
        glow *= SAMPLE_TEXTURE2D(_GlowTexture, sampler_GlowTexture, uv);
        #endif
        col.rgb += glow.rgb;
        #endif

        // Apply fade
        #if FADE_ON
        half fade = _Fade;
        half4 fadeTex = SAMPLE_TEXTURE2D(_FadeTexture, sampler_FadeTexture, uv * _FadeTextureSize + _Time.y * _FadeTextureSpeed);
        fade += fadeTex.r * _FadeTextureStrength;
        half burn = smoothstep(fade - _FadeBurnWidth, fade, uv.y);
        col.rgb = lerp(col.rgb, _FadeBurnColor.rgb, burn * _FadeBurnColor.a);
        col.a *= 1.0 - burn;
        #endif

        // Apply outline
        #if OUTBASE_ON
        half4 outCol = _OutColor * _OutAmount;
        #if OUTTEX_ON
        outCol *= SAMPLE_TEXTURE2D(_OutTex, sampler_OutTex, uv * _OutTexTile);
        #endif
        #if OUTDIST_ON
        half2 distUV = uv + SAMPLE_TEXTURE2D(_OutDistortion, sampler_OutDistortion, uv * _OutDistortionTile + _Time.y * _OutDistortionSpeed).rg * _OutDistortionStrength;
        #else
        half2 distUV = uv;
        #endif
        half4 outline = half4(0,0,0,0);
        #if OUTBASE8DIR_ON
        for (int j = 0; j < 8; j++) {
          half2 offset = half2(cos(j * 0.785398), sin(j * 0.785398)) * _OutDist;
          outline += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, distUV + offset);
        }
        outline /= 8.0;
        #else
        outline += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, distUV + half2(_OutDist, 0));
        outline += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, distUV + half2(-_OutDist, 0));
        outline += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, distUV + half2(0, _OutDist));
        outline += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, distUV + half2(0, -_OutDist));
        outline /= 4.0;
        #endif
        half outlineAlpha = outline.a * outCol.a;
        #if ONLYOUTLINE_ON
        col = half4(outCol.rgb, outlineAlpha);
        #else
        col = lerp(col, outCol, outlineAlpha * (1.0 - originalAlpha));
        #endif
        #endif

        // Apply inner outline
        #if INNEROUTLINE_ON
        half4 innerOutline = _InnerOutlineColor * _InnerOutlineAmount;
        half innerOutlineAlpha = 0;
        for (int k = 0; k < 4; k++) {
          half2 offset = half2(cos(k * 1.570796), sin(k * 1.570796)) * _InnerOutlineWidth;
          innerOutlineAlpha += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + offset).a;
        }
        innerOutlineAlpha /= 4.0;
        innerOutlineAlpha *= _InnerOutlineAlpha;
        #if ONLYINNEROUTLINE_ON
        col = half4(innerOutline.rgb, innerOutlineAlpha);
        #else
        col = lerp(col, innerOutline, innerOutlineAlpha * (1.0 - originalAlpha));
        #endif
        #endif

        // Apply gradient
        #if GRADIENT_ON
        half2 gradUV = uv;
        half rad = radians(_GradientRotation);
        gradUV = half2(gradUV.x * cos(rad) - gradUV.y * sin(rad), gradUV.x * sin(rad) + gradUV.y * cos(rad));
        gradUV *= _GradientScale;
        #if RADIALGRADIENT_ON
        half gradient = length(gradUV - 0.5);
        #else
        half gradient = gradUV.y;
        #endif
        #if GRADIENT2COL_ON
        col.rgb = lerp(_GradientColor1.rgb, _GradientColor2.rgb, gradient);
        #else
        col.rgb = lerp(col.rgb, SAMPLE_TEXTURE2D(_GradientTexture, sampler_GradientTexture, half2(gradient, 0)).rgb, _GradientScale);
        #endif
        #endif

        // Apply color swap
        #if COLORSWAP_ON
        half3 colorSwap = SAMPLE_TEXTURE2D(_ColorSwapTex, sampler_ColorSwapTex, uv).rgb;
        half3 delta = abs(col.rgb - _ColorSwapR.rgb);
        half rMask = step(dot(delta, half3(1,1,1)), _ColorSwapTolerance);
        col.rgb = lerp(col.rgb, _ColorSwapR.rgb, rMask * _ColorSwapAmount);
        delta = abs(col.rgb - _ColorSwapG.rgb);
        half gMask = step(dot(delta, half3(1,1,1)), _ColorSwapTolerance);
        col.rgb = lerp(col.rgb, _ColorSwapG.rgb, gMask * _ColorSwapAmount);
        delta = abs(col.rgb - _ColorSwapB.rgb);
        half bMask = step(dot(delta, half3(1,1,1)), _ColorSwapTolerance);
        col.rgb = lerp(col.rgb, _ColorSwapB.rgb, bMask * _ColorSwapAmount);
        #endif

        // Apply hue shift
        #if HSV_ON
        half3 hsv = RgbToHsv(col.rgb);
        hsv.x += _HsvShift / 360.0;
        hsv.y *= _HsvSaturation;
        hsv.z *= _HsvValue;
        col.rgb = HsvToRgb(hsv);
        #endif

        // Apply color ramp
        #if COLORRAMP_ON
        half luminance = dot(col.rgb, half3(0.299, 0.587, 0.114));
        #if GRADIENTCOLORRAMP_ON
        col.rgb = lerp(col.rgb, SAMPLE_TEXTURE2D(_GradientColorRamp, sampler_GradientColorRamp, half2(luminance, 0)).rgb, _GradientColorRampAmount);
        #else
        col.rgb = lerp(col.rgb, SAMPLE_TEXTURE2D(_ColorRampTex, sampler_ColorRampTex, half2(luminance, 0)).rgb, _ColorRampAmount);
        #endif
        #if COLORRAMPOUTLINE_ON
        col.rgb = lerp(col.rgb, outCol.rgb, _ColorRampAffectOutline);
        #endif
        #endif

        // Apply hit effect
        #if HITEFFECT_ON
        col.rgb = lerp(col.rgb, _HitEffectColor.rgb, _HitEffectAmount * _HitEffectBlend);
        #endif

        // Apply negative
        #if NEGATIVE_ON
        col.rgb = lerp(col.rgb, 1.0 - col.rgb, _NegativeAmount);
        #endif

        // Apply pixelate
        #if PIXELATE_ON
        half2 pixelSize = _PixelateAmount;
        uv = floor(uv / pixelSize) * pixelSize;
        col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * i.color;
        #endif

        // Apply posterize
        #if POSTERIZE_ON
        col.rgb = lerp(col.rgb, floor(col.rgb * _PosterizeLevels) / _PosterizeLevels, _PosterizeAmount);
        #if POSTERIZEOUTLINE_ON
        col.rgb = lerp(col.rgb, outCol.rgb, _PosterizeOutline);
        #endif
        #endif

        // Apply blur
        #if BLUR_ON
        half4 blurCol = half4(0,0,0,0);
        #if BLURISHD_ON
        for (int b = -2; b <= 2; b++) {
          for (int a = -2; a <= 2; a++) {
            blurCol += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + half2(a, b) * _BlurAmount);
          }
        }
        blurCol /= 25.0;
        #else
        blurCol += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + half2(_BlurAmount, 0));
        blurCol += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + half2(-_BlurAmount, 0));
        blurCol += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + half2(0, _BlurAmount));
        blurCol += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + half2(0, -_BlurAmount));
        blurCol /= 4.0;
        #endif
        col = lerp(col, blurCol, _BlurAmount);
        #endif

        // Apply motion blur
        #if MOTIONBLUR_ON
        half4 motionBlurCol = half4(0,0,0,0);
        half2 motionDir = half2(cos(radians(_MotionBlurAngle)), sin(radians(_MotionBlurAngle)));
        for (int mb = -2; mb <= 2; mb++) {
          motionBlurCol += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + motionDir * _MotionBlurAmount * mb);
        }
        motionBlurCol /= 5.0;
        col = lerp(col, motionBlurCol, _MotionBlurAmount);
        #endif

        // Apply ghost
        #if GHOST_ON
        half2 ghostOffset = half2(cos(radians(_GhostAngle)), sin(radians(_GhostAngle))) * _GhostOffset;
        half4 ghostCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + ghostOffset);
        col = lerp(col, ghostCol, _GhostAmount);
        #endif

        // Apply hologram
        #if HOLOGRAM_ON
        half hologram = sin(_Time.y * _HologramSpeed + uv.y * _HologramFrequency) * _HologramAmount;
        col.rgb += _HologramColor.rgb * hologram;
        uv += half2(hologram * _HologramDistortion, 0);
        col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * i.color;
        #endif

        // Apply chromatic aberration
        #if CHROMABERR_ON
        half2 caOffset = half2(_ChromaticAberrationAmount * sin(_Time.y * _ChromaticAberrationSpeed), 0);
        col.r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + caOffset).r;
        col.g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).g;
        col.b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - caOffset).b;
        #endif

        // Apply glitch
        #if GLITCH_ON
        half glitch = sin(_Time.y * _GlitchFrequency) * _GlitchAmount;
        uv += half2(glitch, 0);
        col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * i.color;
        #endif

        // Apply flicker
        #if FLICKER_ON
        half flicker = lerp(1.0, sin(_Time.y * _FlickerSpeed + _FlickerRandomness * 10.0), _FlickerAmount);
        col.rgb *= flicker;
        #endif

        // Apply shine
        #if SHINE_ON
        half shine = cos(uv.x * cos(radians(_ShineAngle)) + uv.y * sin(radians(_ShineAngle)) + _Time.y * _ShineSpeed);
        shine = smoothstep(1.0 - _ShineWidth, 1.0, shine);
        col.rgb = lerp(col.rgb, _ShineColor.rgb, shine * _ShineAmount * _ShineBlend);
        #endif

        // Apply contrast and brightness
        #if CONTRAST_ON
        col.rgb = (col.rgb - 0.5) * _Contrast + 0.5 + _Brightness - 1.0;
        #endif

        // Apply overlay
        #if OVERLAY_ON
        half4 overlay = SAMPLE_TEXTURE2D(_OverlayTex, sampler_OverlayTex, uv * _OverlayTile + _Time.y * _OverlaySpeed);
        #if OVERLAYMULT_ON
        col.rgb *= lerp(half3(1,1,1), overlay.rgb, _OverlayAmount * _OverlayAlpha);
        #else
        col.rgb += overlay.rgb * _OverlayAmount * _OverlayAlpha;
        #endif
        #endif

        // Apply alpha cutoff
        #if ALPHACUTOFF_ON
        col.a = col.a > _AlphaCutoff ? 1.0 : 0.0;
        #endif

        // Apply alpha round
        #if ALPHAROUND_ON
        col.a = round(col.a + _AlphaRound);
        #endif

        // Apply doodle
        #if DOODLE_ON
        uv += half2(sin(_Time.y * _DoodleSpeed + uv.y * 10.0), cos(_Time.y * _DoodleSpeed + uv.x * 10.0)) * _DoodleAmount;
        col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * i.color;
        #endif

        // Apply wind
        #if WIND_ON
        half wind = sin(_Time.y * _WindSpeed + uv.y * 10.0) * _WindAmount;
        #if MANUALWIND_ON
        wind += _WindForce;
        #endif
        uv.x += wind;
        col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * i.color;
        #endif

        // Apply wave
        #if WAVEUV_ON
        half wave = sin(_Time.y * _WaveSpeed + (uv.x * cos(radians(_WaveDirection)) + uv.y * sin(radians(_WaveDirection))) * _WaveFrequency) * _WaveAmplitude;
        uv += half2(wave, wave);
        col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * i.color;
        col.a *= _WaveAmount;
        #endif

        // Apply round wave
        #if ROUNDWAVEUV_ON
        half roundWave = sin(_Time.y * _RoundWaveFrequency + length(uv - 0.5) * 10.0) * _RoundWaveAmplitude;
        uv += half2(roundWave, roundWave);
        col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * i.color;
        #endif

        // Apply offset
        #if OFFSETUV_ON
        uv += half2(_OffsetX, _OffsetY);
        col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * i.color;
        #endif

        // Apply clipping
        #if CLIPPING_ON
        half clip = step(_ClipBottom, uv.y) * step(uv.y, _ClipTop) * step(_ClipLeft, uv.x) * step(uv.x, _ClipRight);
        col.a *= clip;
        #endif

        // Apply radial clipping
        #if RADIALCLIPPING_ON
        half radialClip = step(length(uv - _RadialClipCenter), _RadialClipRadius);
        col.a *= lerp(1.0, radialClip, _RadialClipAmount);
        #endif

        // Apply texture scroll
        #if TEXTURESCROLL_ON
        uv += _Time.y * half2(_TextureScrollSpeedX, _TextureScrollSpeedY);
        col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * i.color;
        #endif

        // Apply zoom
        #if ZOOMUV_ON
        uv = (uv - 0.5) / _ZoomAmount + 0.5;
        col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * i.color;
        #endif

        // Apply distortion
        #if DISTORT_ON
        half2 distort = SAMPLE_TEXTURE2D(_DistortionTex, sampler_DistortionTex, uv * _DistortionTile + _Time.y * _DistortionSpeed).rg;
        uv += distort * _DistortionAmount;
        col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * i.color;
        #endif

        // Apply twist
        #if TWISTUV_ON
        half2 twistUV = uv - _TwistCenter;
        half twist = sin(_Time.y * _TwistSpeed) * _TwistAmount;
        half2x2 rot = half2x2(cos(twist), -sin(twist), sin(twist), cos(twist));
        twistUV = mul(rot, twistUV) * _TwistScale + _TwistCenter;
        col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, twistUV) * i.color;
        #endif

        // Apply rotate
        #if ROTATEUV_ON
        half2 rotUV = uv - 0.5;
        half rot = _RotateAmount * _Time.y;
        half2x2 rotMat = half2x2(cos(rot), -sin(rot), sin(rot), cos(rot));
        rotUV = mul(rotMat, rotUV) + 0.5;
        col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, rotUV) * i.color;
        #endif

        // Apply fish eye
        #if FISHEYE_ON
        half2 fishUV = uv - 0.5;
        half fish = length(fishUV);
        fishUV *= (fish + _FishEyeAmount) / fish;
        fishUV += 0.5;
        col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, fishUV) * i.color;
        #endif

        // Apply pinch
        #if PINCH_ON
        half2 pinchUV = uv - 0.5;
        half pinch = length(pinchUV);
        pinchUV *= pinch / (pinch + _PinchAmount);
        pinchUV += 0.5;
        col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, pinchUV) * i.color;
        #endif

        // Apply shake
        #if SHAKEUV_ON
        half shake = sin(_Time.y * _ShakeSpeed + uv.y * _ShakeFrequency) * _ShakeAmplitude;
        uv += half2(shake, shake);
        col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * i.color;
        #endif

        // Apply billboard
        #if BILBOARD_ON
        #if BILBOARDY_ON
        float3 viewDir = normalize(GetWorldSpaceViewDir(i.vertex.xyz));
        uv += half2(viewDir.x, viewDir.y);
        #else
        float3 viewDir = normalize(GetWorldSpaceViewDir(i.vertex.xyz));
        uv += half2(viewDir.x, 0);
        #endif
        col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * i.color;
        #endif

        // Apply atlas
        #if ATLAS_ON
        // Requires SpriteAtlasUV component; placeholder for UV adjustment
        col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * i.color;
        #endif

        // Apply general alpha
        col.a *= _Alpha;

        // Apply 2D lighting
        half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.lightingUV);
        SurfaceData2D surfaceData;
        surfaceData.albedo = col.rgb;
        surfaceData.alpha = col.a;
        surfaceData.mask = mask;
        InputData2D inputData = { i.uv, i.lightingUV };
        col.rgb = lerp(col.rgb, CombinedShapeLightShared(surfaceData, inputData).rgb, _LitAmount);
        #if GLOWLIGHT_ON
        col.rgb += glow.rgb * _GlowAffectLight;
        #endif

        return col;
      }
      ENDHLSL
    }
  }
  CustomEditor "AllIn1MinMaterialInspector"
}