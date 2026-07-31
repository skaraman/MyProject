Shader "Hidden/Esperanza/ColdPreview" {
  Properties {
    [PerRendererData][NoScaleOffset] _MainTex("Sprite Texture", 2D) = "white" {}
    _PreviewTime("Preview Time", Float) = 0
    _SourceRectInEffect("Source Rect In Effect", Vector) = (0, 0, 1, 1)
    _SpriteUvRect("Sprite UV Rect", Vector) = (0, 0, 1, 1)

    _Freeze("Freeze", Range(0, 1)) = 0.72
    _IcicleLength("Icicle Length", Range(0.03, 0.45)) = 0.24
    _IcicleWidth("Icicle Width", Range(0.01, 0.1)) = 0.045
    _IcicleCount("Icicle Count", Range(4, 16)) = 10
    _CycleSpeed("Cycle Speed", Range(0.1, 2)) = 0.62
    _FrostScale("Frost Scale", Range(2, 10)) = 6
    _CrystalDetail("Crystal Detail", Range(0, 1)) = 0.66
    _SnowAmount("Snow Amount", Range(0, 1)) = 0.5
    _SurfaceOpacity("Surface Opacity", Range(0, 1)) = 0.13
    _IceOpacity("Ice Opacity", Range(0, 1)) = 0.85
    _Specular("Specular", Range(0, 2)) = 1.1
    _Brightness("Brightness", Range(0, 2)) = 1.17

    [HDR] _IceColor("Ice Color", Color) = (0.13, 0.58, 0.95, 1)
    [HDR] _HighlightColor("Frozen Highlight", Color) = (0.82, 0.97, 1, 1)
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
      Name "Cold Overlay"

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
      float _Freeze;
      float _IcicleLength;
      float _IcicleWidth;
      float _IcicleCount;
      float _CycleSpeed;
      float _FrostScale;
      float _CrystalDetail;
      float _SnowAmount;
      float _SurfaceOpacity;
      float _IceOpacity;
      float _Specular;
      float _Brightness;
      float4 _IceColor;
      float4 _HighlightColor;

      #include "Assets/Shaders/Cold/ColdPreviewCore.hlsl"

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
        ColdPreviewCore_float(effectUv, _PreviewTime, color, alpha);
        return float4(color * i.color.rgb, alpha * i.color.a);
      }
      ENDCG
    }
  }
}
