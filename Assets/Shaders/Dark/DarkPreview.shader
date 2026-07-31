Shader "Hidden/Esperanza/DarkPreview" {
  Properties {
    [PerRendererData][NoScaleOffset] _MainTex("Sprite Texture", 2D) = "white" {}
    _PreviewTime("Preview Time", Float) = 0
    _SourceRectInEffect("Source Rect In Effect", Vector) = (0, 0, 1, 1)
    _SpriteUvRect("Sprite UV Rect", Vector) = (0, 0, 1, 1)

    _Presence("Dark Presence", Range(0, 1)) = 0.74
    _TendrilCount("Tendril Count", Range(1, 8)) = 6
    _EdgeWidth("Edge Width", Range(0.002, 0.08)) = 0.032
    _EdgeOpacity("Edge Opacity", Range(0, 1)) = 0.75
    _TendrilReach("Tendril Reach", Range(0.05, 0.8)) = 0.48
    _TendrilWidth("Tendril Width", Range(0.003, 0.04)) = 0.016
    _Movement("Movement", Range(0.05, 2)) = 0.7
    _VeinAmount("Vein Amount", Range(0, 1)) = 0.66
    _VeinScale("Vein Scale", Range(2, 12)) = 7
    _SurfaceOpacity("Surface Opacity", Range(0, 1)) = 0.12
    _DarkOpacity("Dark Opacity", Range(0, 1)) = 0.84
    _Glow("Purple Glow", Range(0, 2)) = 1.15

    [HDR] _PurpleColor("Dark Purple", Color) = (0.22, 0.015, 0.36, 1)
    _AbyssColor("Black Highlight", Color) = (0.004, 0, 0.012, 1)
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
      Name "Dark Overlay"

      CGPROGRAM
      #pragma target 3.0
      #pragma vertex vert
      #pragma fragment frag

      #include "UnityCG.cginc"

      sampler2D _MainTex;
      float4 _MainTex_TexelSize;

      float _PreviewTime;
      float4 _SourceRectInEffect;
      float4 _SpriteUvRect;
      float _Presence;
      float _TendrilCount;
      float _EdgeWidth;
      float _EdgeOpacity;
      float _TendrilReach;
      float _TendrilWidth;
      float _Movement;
      float _VeinAmount;
      float _VeinScale;
      float _SurfaceOpacity;
      float _DarkOpacity;
      float _Glow;
      float4 _PurpleColor;
      float4 _AbyssColor;

      #include "Assets/Shaders/Dark/DarkPreviewCore.hlsl"

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

      float4 frag(v2f i) : SV_Target {
        float3 color;
        float alpha;
        float2 spriteUvSize = max(_SpriteUvRect.zw, float2(1e-4, 1e-4));
        float2 effectUv = (i.uv - _SpriteUvRect.xy) / spriteUvSize;
        DarkPreviewCore_float(effectUv, _PreviewTime, color, alpha);
        return float4(color * i.color.rgb, alpha * i.color.a);
      }
      ENDCG
    }
  }
}
