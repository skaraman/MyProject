Shader "Hidden/Esperanza/BoltPreview" {
  Properties {
    [PerRendererData][NoScaleOffset] _MainTex("Sprite Texture", 2D) = "white" {}
    _PreviewTime("Preview Time", Float) = 0
    _SourceRectInEffect("Source Rect In Effect", Vector) = (0, 0, 1, 1)
    _SpriteUvRect("Sprite UV Rect", Vector) = (0, 0, 1, 1)

    _Charge("Charge", Range(0, 1)) = 0.72
    _BoltCount("Bolt Count", Range(1, 8)) = 6
    _Reach("Reach", Range(0, 1)) = 0.62
    _BoltWidth("Bolt Width", Range(0.001, 0.025)) = 0.007
    _Activity("Activity", Range(0.1, 3)) = 1.4
    _Randomness("Bolt Randomness", Range(0, 1)) = 0.78
    _Branching("Branching", Range(0, 1)) = 0.58
    _SurfaceOpacity("Surface Opacity", Range(0, 1)) = 0.12
    _BoltOpacity("Bolt Opacity", Range(0, 1)) = 0.9
    _Glow("Glow", Range(0, 4)) = 2

    [HDR] _CoreColor("Electric Core", Color) = (0.84, 1, 0.38, 1)
    [HDR] _BoltColor("Lime Glow", Color) = (0.22, 1, 0.015, 1)
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
      Name "Bolt Overlay"

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
      float _Charge;
      float _BoltCount;
      float _Reach;
      float _BoltWidth;
      float _Activity;
      float _Randomness;
      float _Branching;
      float _SurfaceOpacity;
      float _BoltOpacity;
      float _Glow;
      float4 _CoreColor;
      float4 _BoltColor;

      #include "Assets/Shaders/Bolt/BoltPreviewCore.hlsl"

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
        BoltPreviewCore_float(effectUv, _PreviewTime, color, alpha);
        return float4(color * i.color.rgb, alpha * i.color.a);
      }
      ENDCG
    }
  }
}
