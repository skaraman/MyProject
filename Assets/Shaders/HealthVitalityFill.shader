Shader "Esperanza/UI/Health Vitality Fill"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        [PerRendererData] _SpriteUvRect("Sprite UV Rect", Vector) = (0, 0, 1, 1)
        _Color("Tint", Color) = (1, 1, 1, 1)

        [Header(Four Point Gradient)]
        _GradBlend("Gradient Blend", Range(0, 1)) = 1
        _GradTopLeftCol("Top Left", Color) = (1, 1, 1, 1)
        _GradTopRightCol("Top Right", Color) = (1, 1, 1, 1)
        _GradBotLeftCol("Bottom Left", Color) = (1, 1, 1, 1)
        _GradBotRightCol("Bottom Right", Color) = (1, 1, 1, 1)
        _GradBoostX("Gradient X Power", Range(0.1, 5)) = 1
        _GradBoostY("Gradient Y Power", Range(0.1, 5)) = 1

        [Header(Clipping)]
        [Enum(Rectangular, 0, Radial, 1)] _ClipMode("Clip Mode", Float) = 0
        _ClipUvLeft("Rect Clip Left", Range(0, 1)) = 0
        _ClipUvRight("Rect Clip Right", Range(0, 1)) = 0
        _ClipUvDown("Rect Clip Bottom", Range(0, 1)) = 0
        _ClipUvUp("Rect Clip Top", Range(0, 1)) = 0
        _RadialCenter("Radial Center", Vector) = (0.5, 0.5, 0, 0)
        _RadialStartAngle("Radial Start Angle", Range(0, 360)) = 90
        _RadialFillAmount("Radial Fill Amount", Range(0, 1)) = 1
        [Toggle] _RadialReverseDirection("Reverse Radial Direction", Float) = 0
        [Toggle] _RadialInvert("Invert Radial Visibility", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _SpriteUvRect;
                half4 _Color;
                half4 _GradTopLeftCol;
                half4 _GradTopRightCol;
                half4 _GradBotLeftCol;
                half4 _GradBotRightCol;
                half _GradBlend;
                half _GradBoostX;
                half _GradBoostY;
                half _ClipMode;
                half _ClipUvLeft;
                half _ClipUvRight;
                half _ClipUvDown;
                half _ClipUvUp;
                half4 _RadialCenter;
                half _RadialStartAngle;
                half _RadialFillAmount;
                half _RadialReverseDirection;
                half _RadialInvert;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 rectSize = max(abs(_SpriteUvRect.zw), float2(0.00001, 0.00001));
                float2 localUv = (input.uv - _SpriteUvRect.xy) / rectSize;

                if (_ClipMode < 0.5h)
                {
                    clip(localUv.x - _ClipUvLeft);
                    clip((1.0h - _ClipUvRight) - localUv.x);
                    clip(localUv.y - _ClipUvDown);
                    clip((1.0h - _ClipUvUp) - localUv.y);
                }
                else
                {
                    float2 radialVector = localUv - _RadialCenter.xy;
                    float angle = degrees(atan2(radialVector.y, radialVector.x));
                    angle = angle < 0.0 ? angle + 360.0 : angle;

                    float relativeAngle = _RadialReverseDirection >= 0.5h
                        ? _RadialStartAngle - angle
                        : angle - _RadialStartAngle;
                    relativeAngle -= floor(relativeAngle / 360.0) * 360.0;

                    half insideArc = step(relativeAngle, saturate(_RadialFillAmount) * 360.0h);
                    half visible = _RadialInvert >= 0.5h ? 1.0h - insideArc : insideArc;
                    clip(visible - 0.5h);
                }

                half4 textureColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half2 gradientUv = saturate(localUv);
                half gradientX = saturate(pow(gradientUv.x, max(_GradBoostX, 0.001h)));
                half gradientY = saturate(pow(gradientUv.y, max(_GradBoostY, 0.001h)));
                half4 gradientBottom = lerp(_GradBotLeftCol, _GradBotRightCol, gradientX);
                half4 gradientTop = lerp(_GradTopLeftCol, _GradTopRightCol, gradientX);
                half4 gradient = lerp(gradientBottom, gradientTop, gradientY);

                half gradientBlend = saturate(_GradBlend);
                half4 tint = input.color * _Color;
                half3 surfaceColor = lerp(textureColor.rgb, gradient.rgb, gradientBlend) * tint.rgb;
                half alpha = textureColor.a * tint.a * lerp(1.0h, gradient.a, gradientBlend);
                return half4(surfaceColor * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
