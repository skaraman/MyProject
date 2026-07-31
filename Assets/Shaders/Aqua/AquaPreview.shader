Shader "Hidden/Esperanza/AquaPreview" {
  Properties {
    [PerRendererData][NoScaleOffset] _MainTex("Sprite Texture", 2D) = "white" {}
    [NoScaleOffset] _NormalMap("Sprite Normal", 2D) = "bump" {}
    [NoScaleOffset] _NoiseTex("Bead Pattern", 2D) = "white" {}
    [NoScaleOffset] _FlowTex("Flow Pattern", 2D) = "gray" {}
    _PreviewTime("Preview Time", Float) = 0
    _SourceRectInEffect("Source Rect In Effect", Vector) = (0, 0, 1, 1)
    _SpriteUvRect("Sprite UV Rect", Vector) = (0, 0, 1, 1)
    [HideInInspector] _HasNormalMap("Has Normal Map", Float) = 0

    _Wetness("Wetness", Range(0, 1)) = 0.75
    _DripLength("Drip Length", Range(0.01, 0.4)) = 0.2
    _DripWidth("Drip Width", Range(0.005, 0.12)) = 0.045
    _DripCount("Drip Count", Range(2, 18)) = 9
    _FlowSpeed("Flow Speed", Range(0, 3)) = 0.8
    _Wobble("Wobble", Range(0, 0.1)) = 0.025
    _Beading("Beading", Range(0, 1)) = 0.65
    _NoiseScale("Noise Scale", Range(1, 10)) = 4.5
    _SurfaceOpacity("Surface Opacity", Range(0, 1)) = 0.14
    _DripOpacity("Drip Opacity", Range(0, 1)) = 0.78
    _Specular("Wet Shine", Range(0, 2)) = 0.72
    _Brightness("Brightness", Range(0, 3)) = 1.1

    [HDR] _WaterColor("Water Color", Color) = (0.04, 0.42, 0.92, 1)
    [HDR] _HighlightColor("Highlight Color", Color) = (0.54, 0.95, 1, 1)
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
      Name "Aqua Overlay"

      CGPROGRAM
      #pragma target 3.0
      #pragma vertex vert
      #pragma fragment frag

      #include "UnityCG.cginc"

      sampler2D _MainTex;
      sampler2D _NormalMap;
      sampler2D _NoiseTex;
      sampler2D _FlowTex;
      float4 _MainTex_TexelSize;

      float _PreviewTime;
      float4 _SourceRectInEffect;
      float4 _SpriteUvRect;
      float _HasNormalMap;
      float _Wetness;
      float _DripLength;
      float _DripWidth;
      float _DripCount;
      float _FlowSpeed;
      float _Wobble;
      float _Beading;
      float _NoiseScale;
      float _SurfaceOpacity;
      float _DripOpacity;
      float _Specular;
      float _Brightness;
      float4 _WaterColor;
      float4 _HighlightColor;

      #include "Assets/Shaders/Aqua/AquaPreviewCore.hlsl"

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
        float3 color;
        float alpha;
        float2 spriteUvSize = max(_SpriteUvRect.zw, float2(1e-4, 1e-4));
        float2 effectUv = (i.uv - _SpriteUvRect.xy) / spriteUvSize;
        AquaPreviewCore_float(effectUv, _PreviewTime, color, alpha);
        return float4(color * i.color.rgb, alpha * i.color.a);
      }
      ENDCG
    }
  }
}
