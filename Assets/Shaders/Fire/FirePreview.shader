Shader "Hidden/Esperanza/FirePreview" {
  Properties {
    [PerRendererData][NoScaleOffset] _MainTex("Sprite Texture", 2D) = "white" {}
    [NoScaleOffset] _NoiseTex("Breakup", 2D) = "white" {}
    [NoScaleOffset] _FlowTex("Flow", 2D) = "gray" {}
    _PreviewTime("Preview Time", Float) = 0
    _Opacity("Opacity", Range(0, 1.5)) = 0.92
    _FlameHeight("Flame Height", Range(0.02, 1.5)) = 0.68
    _BodyWidth("Body Width", Range(0.02, 1.5)) = 0.46
    _TipWidth("Tip Width", Range(0.01, 1)) = 0.08
    _TaperExponent("Taper Exponent", Range(0.1, 4)) = 1.15
    _InnerWidthRatio("Inner Width Ratio", Range(0.05, 0.95)) = 0.32
    _InnerSharpness("Inner Sharpness", Range(0.2, 6)) = 1.9
    _VerticalFalloff("Vertical Falloff", Range(0.2, 4)) = 2
    _EdgeSoftness("Edge Softness", Range(0.001, 1)) = 0.09
    _Breakup("Breakup", Range(0, 4)) = 0.6
    _NoiseScale("Noise Scale", Range(0.05, 20)) = 2.2
    _DetailScale("Detail Scale", Range(0.1, 40)) = 6
    _FlowSpeed("Flow Speed", Range(-2, 8)) = 1.1
    _TongueStrength("Tongue Strength", Range(0, 1)) = 0.18
    _TongueFrequency("Tongue Frequency", Range(0, 40)) = 7.5
    _DistortionStrength("Distortion Strength", Range(0, 1)) = 0.12
    _SourceMotion("Source Motion", Range(0, 1)) = 0.12
    _PatternRepeat("Pattern Repeat", Range(1, 20)) = 5
    _SourceFeatureBoost("Source Feature Boost", Range(0, 3)) = 1
    _RibbonFrequency("Ribbon Frequency", Range(0, 80)) = 26
    _RibbonThresholdMin("Ribbon Threshold Min", Range(0, 1)) = 0.54
    _RibbonThresholdMax("Ribbon Threshold Max", Range(0, 1)) = 0.9
    _RibbonInfluence("Ribbon Influence", Range(0, 2)) = 0.82
    _CoreIntensity("Edge Brightness", Range(0, 6)) = 1.6
    _RimPower("Rim Power", Range(0.2, 8)) = 3.4
    _BodyIntensity("Body Intensity", Range(0, 4)) = 1.35
    _HotIntensity("Hot Intensity", Range(0, 4)) = 0.95
    _BrightIntensity("Bright Intensity", Range(0, 4)) = 0.78
    _VeilStrength("Veil Strength", Range(0, 3)) = 0.38
    _VeilExponent("Veil Exponent", Range(0.2, 4)) = 1.55
    _VeilStart("Veil Start", Range(0, 1)) = 0.08
    _VeilEnd("Veil End", Range(0, 1)) = 0.9
    _SparkAmount("Spark Amount", Range(0, 4)) = 1
    _SparkThreshold("Spark Threshold", Range(0, 1)) = 0.84
    _SparkSizeMin("Spark Size Min", Range(0.005, 0.25)) = 0.06
    _SparkSizeMax("Spark Size Max", Range(0.01, 0.5)) = 0.18
    _SparkRiseSpeed("Spark Rise Speed", Range(0, 12)) = 3.6
    _SparkDrift("Spark Drift", Range(0, 2)) = 0.65
    _SparkGridX("Spark Grid X", Range(1, 40)) = 10
    _SparkGridY("Spark Grid Y", Range(1, 60)) = 18
    _SparkLife("Spark Life", Range(0.2, 6)) = 1.5
    _SparkBandStart("Spark Band Start", Range(0, 1)) = 0.15
    _SparkBandEnd("Spark Band End", Range(0.2, 2)) = 1.26
    _SparkEnvelopePower("Spark Envelope Power", Range(0.1, 6)) = 2.4
    _SparkHotIntensity("Spark Hot Intensity", Range(0, 4)) = 0.45
    _SparkBrightIntensity("Spark Bright Intensity", Range(0, 6)) = 1.35
    _BrightColor("Bright Color", Color) = (1, 0.98, 0.9, 1)
    _HotColor("Hot Color", Color) = (1, 0.63, 0.2, 1)
    _BodyColor("Body Color", Color) = (0.19, 0.04, 0.01, 1)
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
      float _Opacity;
      float _FlameHeight;
      float _BodyWidth;
      float _TipWidth;
      float _TaperExponent;
      float _InnerWidthRatio;
      float _InnerSharpness;
      float _VerticalFalloff;
      float _EdgeSoftness;
      float _Breakup;
      float _NoiseScale;
      float _DetailScale;
      float _FlowSpeed;
      float _TongueStrength;
      float _TongueFrequency;
      float _DistortionStrength;
      float _SourceMotion;
      float _PatternRepeat;
      float _SourceFeatureBoost;
      float _RibbonFrequency;
      float _RibbonThresholdMin;
      float _RibbonThresholdMax;
      float _RibbonInfluence;
      float _CoreIntensity;
      float _RimPower;
      float _BodyIntensity;
      float _HotIntensity;
      float _BrightIntensity;
      float _VeilStrength;
      float _VeilExponent;
      float _VeilStart;
      float _VeilEnd;
      float _SparkAmount;
      float _SparkThreshold;
      float _SparkSizeMin;
      float _SparkSizeMax;
      float _SparkRiseSpeed;
      float _SparkDrift;
      float _SparkGridX;
      float _SparkGridY;
      float _SparkLife;
      float _SparkBandStart;
      float _SparkBandEnd;
      float _SparkEnvelopePower;
      float _SparkHotIntensity;
      float _SparkBrightIntensity;
      float4 _BrightColor;
      float4 _HotColor;
      float4 _BodyColor;

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
        float clampedHeight = max(_FlameHeight, 0.02);
        float normalizedHeight = saturate(uv.y / clampedHeight);

        float2 flowUv = uv * 1.35 + float2(0.0, -previewTime * _FlowSpeed * 0.12);
        float2 flowVec = (tex2D(_FlowTex, frac(flowUv)).rg * 2.0) - 1.0;

        float lateralWave = sin((uv.y * _TongueFrequency * 6.2831853) - (previewTime * (_FlowSpeed * 2.4)));
        float tipFactor = pow(saturate(normalizedHeight), 1.25);
        float lateralOffset = ((flowVec.x * 0.65) + (lateralWave * 0.35)) * _TongueStrength * tipFactor;

        float2 flameUv = uv;
        flameUv.x += lateralOffset;
        flameUv.y -= previewTime * _FlowSpeed * 0.28;
        flameUv += flowVec * float2(_DistortionStrength, _DistortionStrength * 0.35);

        float breakupLarge = tex2D(
          _NoiseTex,
          frac((flameUv * float2(_NoiseScale, _NoiseScale * 1.35)) + float2(0.0, previewTime * _FlowSpeed * 0.42))
        ).r;

        float breakupDetail = tex2D(
          _FlowTex,
          frac((flameUv * float2(_DetailScale * 0.7, _DetailScale * 1.45)) + float2(-previewTime * _FlowSpeed * 0.18, previewTime * _FlowSpeed * 0.73))
        ).r;

        float4 sourceSample = tex2D(_MainTex, uv);
        float mask = FirePreviewMask(sourceSample);
        float sourceFeature = FirePreviewSourceFeature(sourceSample);
        float repeatedSourceFeature = FirePreviewRepeatedSourceFeature(uv, previewTime);
        sourceFeature = saturate(max(sourceFeature, repeatedSourceFeature));
        float3 color;
        float alpha;

        FirePreviewCore_float(
          uv,
          mask,
          sourceFeature,
          breakupLarge,
          breakupDetail,
          flowVec.x,
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
