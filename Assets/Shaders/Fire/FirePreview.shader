Shader "Hidden/Esperanza/FirePreview" {
  Properties {
    [PerRendererData][NoScaleOffset] _MainTex("Sprite Texture", 2D) = "white" {}
    [NoScaleOffset] _NoiseTex("Breakup", 2D) = "white" {}
    [NoScaleOffset] _FlowTex("Flow", 2D) = "gray" {}
    _PreviewTime("Preview Time", Float) = 0
    _FireSpeed("Fire Speed", Range(0, 5)) = 1.0
    _BurnAmount("Burn Amount", Range(0, 1)) = 0.5
    _FireSpread("Fire Spread", Range(0, 5)) = 1.0
    _Distortion("Distortion", Range(0, 1)) = 0.15
    _FireIntensity("Fire Intensity", Range(0, 3)) = 1.2
    _CoreColor("Core Color", Color) = (1, 0.95, 0.6, 1)
    _EdgeColor("Edge Color", Color) = (1, 0.4, 0.05, 1)
    _SmokeColor("Smoke Color", Color) = (0.1, 0.02, 0.01, 1)
  }

  SubShader {
    Tags {
      "Queue" = "Transparent"
      "RenderType" = "Transparent"
      "PreviewType" = "Plane"
      "CanUseSpriteAtlas" = "True"
      "IgnoreProjector" = "True"
    }

    Cull Off
    ZWrite Off
    Blend SrcAlpha OneMinusSrcAlpha

    Pass {
      CGPROGRAM
      #pragma vertex vert
      #pragma fragment frag

      #include "UnityCG.cginc"

      sampler2D _MainTex;
      sampler2D _NoiseTex;
      sampler2D _FlowTex;

      float _PreviewTime;
      float _FireSpeed;
      float _BurnAmount;
      float _FireSpread;
      float _Distortion;
      float _FireIntensity;
      float4 _CoreColor;
      float4 _EdgeColor;
      float4 _SmokeColor;

      #include "Assets/Shaders/Fire/FirePreviewCore.hlsl"

      struct appdata_t {
        float4 vertex : POSITION;
        float2 uv : TEXCOORD0;
        float4 color : COLOR;
      };

      struct v2f {
        float4 vertex : SV_POSITION;
        float2 uv : TEXCOORD0;
        float4 color : COLOR;
      };

      v2f vert(appdata_t v) {
        v2f o;
        o.vertex = UnityObjectToClipPos(v.vertex);
        o.uv = v.uv;
        o.color = v.color;
        return o;
      }

      fixed4 frag(v2f i) : SV_Target {
        float2 uv = i.uv;
        float previewTime = _PreviewTime;
        
        float3 color;
        float alpha;

        FirePreviewCore_float(
          uv,
          previewTime,
          color,
          alpha
        );

        return float4(color * i.color.rgb, alpha * i.color.a);
      }
      ENDCG
    }
  }
}
