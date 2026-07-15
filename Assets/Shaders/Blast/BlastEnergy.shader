Shader "Esperanza/Effects/BlastEnergy" {
  Properties {
    [PerRendererData][NoScaleOffset] _MainTex("Sprite Texture", 2D) = "white" {}
    [PerRendererData][NoScaleOffset] _NormalMap("Normal Map", 2D) = "bump" {}
    _PrimaryColor("Primary Color", Color) = (1, 1, 1, 1)
    _SecondaryColor("Secondary Color", Color) = (0.05, 0.35, 1, 1)
    _Speed("Swirl Speed", Range(0, 8)) = 2.4
    _Swirl("Orbit Wobble", Range(0, 12)) = 6
    _Bands("Light Knots", Range(1, 12)) = 5
    _GleamWidth("Strand Width", Range(0.005, 0.12)) = 0.035
    _Intensity("Intensity", Range(0, 4)) = 1.45
    _NormalStrength("Normal Strength", Range(0, 4)) = 1
    _LightInfluence("Light Influence", Range(0, 1)) = 0.75
    _PreviewTime("Preview Time", Float) = 0
    _UsePreviewTime("Use Preview Time", Float) = 0
    [PerRendererData] _SpriteUvRect("Sprite UV Rect", Vector) = (0, 0, 1, 1)
    [PerRendererData] _SpriteEffectActive("Sprite Effect Active", Float) = 0
  }

  SubShader {
    Tags {
      "Queue" = "Transparent"
      "RenderType" = "Transparent"
      "RenderPipeline" = "UniversalPipeline"
      "PreviewType" = "Plane"
      "CanUseSpriteAtlas" = "True"
      "IgnoreProjector" = "True"
    }

    Cull Off
    ZWrite Off
    Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

    Pass {
      Name "Blast Lit"
      Tags { "LightMode" = "Universal2D" }

      HLSLPROGRAM
      #pragma vertex BlastLitVertex
      #pragma fragment BlastLitFragment
      #pragma multi_compile_instancing

      #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
      #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"
      #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"
      #include "Assets/Shaders/Blast/BlastEnergyCore.hlsl"

      struct BlastAttributes {
        float3 positionOS : POSITION;
        float2 uv : TEXCOORD0;
        half4 color : COLOR;
        UNITY_VERTEX_INPUT_INSTANCE_ID
      };

      struct BlastLitVaryings {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        half4 color : COLOR;
        half2 lightingUV : TEXCOORD1;
        UNITY_VERTEX_OUTPUT_STEREO
      };

      BlastLitVaryings BlastLitVertex(BlastAttributes input) {
        BlastLitVaryings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        SetUpSpriteInstanceProperties();
        input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
        output.positionCS = TransformObjectToHClip(input.positionOS);
        output.uv = input.uv;
        output.color = input.color;
        float4 clipPosition = output.positionCS / output.positionCS.w;
        output.lightingUV = ComputeScreenPos(clipPosition).xy;
        return output;
      }

      half4 BlastLitFragment(BlastLitVaryings input) : SV_Target {
        BlastEnergyData energy = EvaluateBlastEnergy(input.uv, input.color);

        SurfaceData2D surfaceData;
        InitializeSurfaceData(
          energy.color,
          energy.alpha,
          half4(1, 1, 1, 1),
          half3(0, 0, 1),
          surfaceData);

        InputData2D inputData;
        InitializeInputData(input.uv, input.lightingUV, inputData);

        half4 litColor = CombinedShapeLightShared(surfaceData, inputData);
        half3 finalColor = lerp(energy.color, litColor.rgb, saturate(_LightInfluence));
        return half4(finalColor, energy.alpha);
      }
      ENDHLSL
    }

    Pass {
      Name "Blast Normals"
      Tags { "LightMode" = "NormalsRendering" }

      HLSLPROGRAM
      #pragma vertex BlastNormalsVertex
      #pragma fragment BlastNormalsFragment
      #pragma multi_compile_instancing

      #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
      #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/NormalsRenderingShared.hlsl"
      #include "Assets/Shaders/Blast/BlastEnergyCore.hlsl"

      struct BlastNormalAttributes {
        float3 positionOS : POSITION;
        float2 uv : TEXCOORD0;
        half4 color : COLOR;
        half4 tangent : TANGENT;
        UNITY_VERTEX_INPUT_INSTANCE_ID
      };

      struct BlastNormalVaryings {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        half4 color : COLOR;
        half3 normalWS : TEXCOORD1;
        half3 tangentWS : TEXCOORD2;
        half3 bitangentWS : TEXCOORD3;
        UNITY_VERTEX_OUTPUT_STEREO
      };

      BlastNormalVaryings BlastNormalsVertex(BlastNormalAttributes input) {
        BlastNormalVaryings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        SetUpSpriteInstanceProperties();
        input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
        output.positionCS = TransformObjectToHClip(input.positionOS);
        output.uv = input.uv;
        output.color = input.color;
        output.normalWS = -GetViewForwardDir();
        output.tangentWS = TransformObjectToWorldDir(input.tangent.xyz);
        output.bitangentWS = cross(output.normalWS, output.tangentWS) * input.tangent.w;
        return output;
      }

      half4 BlastNormalsFragment(BlastNormalVaryings input) : SV_Target {
        SetUpSpriteInstanceProperties();
        BlastEnergyData energy = EvaluateBlastEnergy(input.uv, input.color);
        half4 normalSample = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv);
        half3 mappedNormal = UnpackNormal(normalSample);
        mappedNormal.xy *= _NormalStrength;
        mappedNormal = normalize(mappedNormal);

        float2 spherePoint = (energy.localUv * 2.0) - 1.0;
        half sphereHeight = sqrt(saturate(1.0 - dot(spherePoint, spherePoint)));
        half3 volumeNormal = normalize(half3(spherePoint.x, spherePoint.y, sphereHeight));
        half mapWeight = saturate(energy.sourceAlpha * 2.0);
        half3 normalTS = normalize(lerp(volumeNormal, mappedNormal, mapWeight));

        half4 normalColor = half4(1, 1, 1, energy.alpha);
        return NormalsRenderingShared(
          normalColor,
          normalTS,
          input.tangentWS,
          input.bitangentWS,
          input.normalWS);
      }
      ENDHLSL
    }

    Pass {
      Name "Blast Forward"
      Tags { "LightMode" = "UniversalForward" }

      HLSLPROGRAM
      #pragma vertex BlastForwardVertex
      #pragma fragment BlastForwardFragment

      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
      #include "Assets/Shaders/Blast/BlastEnergyCore.hlsl"

      struct BlastForwardAttributes {
        float3 positionOS : POSITION;
        float2 uv : TEXCOORD0;
        half4 color : COLOR;
      };

      struct BlastForwardVaryings {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        half4 color : COLOR;
      };

      BlastForwardVaryings BlastForwardVertex(BlastForwardAttributes input) {
        BlastForwardVaryings output;
        output.positionCS = TransformObjectToHClip(input.positionOS);
        output.uv = input.uv;
        output.color = input.color;
        return output;
      }

      half4 BlastForwardFragment(BlastForwardVaryings input) : SV_Target {
        BlastEnergyData energy = EvaluateBlastEnergy(input.uv, input.color);
        return half4(energy.color, energy.alpha);
      }
      ENDHLSL
    }
  }
}
