Shader "Hidden/Esperanza/FirePreview" {
  Properties {
    [PerRendererData][NoScaleOffset] _MainTex("Sprite Texture", 2D) = "white" {}
    [NoScaleOffset] _NoiseTex("Breakup Pattern", 2D) = "white" {}
    [NoScaleOffset] _FlowTex("Flow Pattern", 2D) = "gray" {}
    _PreviewTime("Preview Time", Float) = 0
    _SourceRectInEffect("Source Rect In Effect", Vector) = (0, 0, 1, 1)
    _SpriteUvRect("Sprite UV Rect", Vector) = (0, 0, 1, 1)

    _FlameCoverage("Flame Coverage", Range(0, 1)) = 0.82
    _FlameHeight("Flame Height", Range(0.01, 0.4)) = 0.19
    _TongueWidth("Tongue Width", Range(0.005, 0.15)) = 0.065
    _TongueCount("Tongue Count", Range(2, 18)) = 8
    _FlowSpeed("Flow Speed", Range(0, 3)) = 1.25
    _Sway("Sway", Range(0, 0.12)) = 0.035
    _Breakup("Breakup", Range(0, 1)) = 0.42
    _NoiseScale("Noise Scale", Range(1, 10)) = 4
    _SurfaceOpacity("Surface Opacity", Range(0, 1)) = 0.25
    _FlameOpacity("Flame Opacity", Range(0, 1)) = 0.88
    _Brightness("Brightness", Range(0, 3)) = 1.65

    [HDR] _HotColor("Hot Center", Color) = (1, 0.94, 0.58, 1)
    [HDR] _FlameColor("Outer Flame", Color) = (1, 0.26, 0.015, 1)
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
      Name "Fire Overlay"

      CGPROGRAM
      #pragma target 3.0
      #pragma vertex vert
      #pragma fragment frag

      #include "UnityCG.cginc"

      sampler2D _MainTex;
      sampler2D _NoiseTex;
      sampler2D _FlowTex;
      float4 _MainTex_TexelSize;

      float _PreviewTime;
      float4 _SourceRectInEffect;
      float4 _SpriteUvRect;
      float _FlameCoverage;
      float _FlameHeight;
      float _TongueWidth;
      float _TongueCount;
      float _FlowSpeed;
      float _Sway;
      float _Breakup;
      float _NoiseScale;
      float _SurfaceOpacity;
      float _FlameOpacity;
      float _Brightness;
      float4 _HotColor;
      float4 _FlameColor;

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
        float3 color;
        float alpha;
        float2 spriteUvSize = max(_SpriteUvRect.zw, float2(1e-4, 1e-4));
        float2 effectUv = (i.uv - _SpriteUvRect.xy) / spriteUvSize;
        FirePreviewCore_float(effectUv, _PreviewTime, color, alpha);
        return float4(color * i.color.rgb, alpha * i.color.a);
      }
      ENDCG
    }
  }
}
