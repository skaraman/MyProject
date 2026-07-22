Shader "Esperanza/ProjectedSpriteShadow" {
  Properties {
    [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
    _ShadowColor("Shadow Color", Color) = (0, 0, 0, 0.28)
    _GroundPoint("Ground Point", Vector) = (0, 0, 0, 0)
    _ShadowDirection("Shadow Direction", Vector) = (0.45, -1, 0, 0)
    _ProjectionLength("Projection Length", Float) = 0.75
    [HideInInspector] _Color("Tint", Color) = (1, 1, 1, 1)
    [HideInInspector] _RendererColor("Renderer Color", Color) = (1, 1, 1, 1)
    [HideInInspector] _StencilRef("Stencil Reference", Float) = 0
    [HideInInspector] _StencilComp("Stencil Comparison", Float) = 8
    [HideInInspector] _StencilReadMask("Stencil Read Mask", Float) = 63
    [HideInInspector] _StencilWriteMask("Stencil Write Mask", Float) = 63
  }

  SubShader {
    Tags {
      "Queue" = "Transparent"
      "RenderType" = "Transparent"
      "RenderPipeline" = "UniversalPipeline"
      "CanUseSpriteAtlas" = "True"
    }

    Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
    Cull Off
    ZWrite Off

    Stencil {
      Ref [_StencilRef]
      ReadMask [_StencilReadMask]
      WriteMask [_StencilWriteMask]
      Comp [_StencilComp]
      Pass Replace
    }

    Pass {
      HLSLPROGRAM
      #pragma vertex ProjectedShadowVertex
      #pragma fragment ProjectedShadowFragment
      #pragma multi_compile_instancing

      #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

      struct Attributes {
        COMMON_2D_INPUTS
        half4 color : COLOR;
      };

      struct Varyings {
        COMMON_2D_OUTPUTS
        half4 color : COLOR;
      };

      TEXTURE2D(_MainTex);
      SAMPLER(sampler_MainTex);

      CBUFFER_START(UnityPerMaterial)
        half4 _Color;
        half4 _ShadowColor;
        float4 _GroundPoint;
        float4 _ShadowDirection;
        float _ProjectionLength;
      CBUFFER_END

      float4x4 _SourceLocalToWorld;

      Varyings ProjectedShadowVertex(Attributes input) {
        Varyings output = (Varyings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        SetUpSpriteInstanceProperties();

        input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
        float3 positionWS = mul(_SourceLocalToWorld, float4(input.positionOS, 1.0)).xyz;
        float height = max(positionWS.y - _GroundPoint.y, 0.0);
        float2 direction = normalize(_ShadowDirection.xy);
        positionWS.xy = float2(positionWS.x, _GroundPoint.y);
        positionWS.xy += direction * height * _ProjectionLength;

        output.positionCS = TransformWorldToHClip(positionWS);
        output.uv = input.uv;
        output.color = input.color * unity_SpriteColor * _ShadowColor;
        return output;
      }

      half4 ProjectedShadowFragment(Varyings input) : SV_Target {
        half sourceAlpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
        half finalAlpha = sourceAlpha * input.color.a;
        clip(finalAlpha - 0.0001h);
        return half4(input.color.rgb, finalAlpha);
      }
      ENDHLSL
    }
  }
}
