Shader "Custom/ClaimTileGlow"
{
    Properties
    {
        [HDR] _Color ("Tint", Color) = (0.3, 1.0, 0.5, 0.55)
        _Intensity ("Intensity", Range(0, 4)) = 1.0
        _PulseAmount ("Pulse Amount", Range(0, 0.5)) = 0.1
        _PulseSpeed ("Pulse Speed", Range(0, 8)) = 2.2
        _FootprintHalf ("Footprint Half (UV)", Vector) = (0.238, 0.238, 0, 0)
        _CornerRadius ("Corner Radius (UV)", Range(0, 0.4)) = 0.057
        _Spread ("Spread (UV)", Range(0, 0.5)) = 0.262
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+10"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ClaimTileGlow"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
                half _PulseAmount;
                half _PulseSpeed;
                float2 _FootprintHalf;
                float _CornerRadius;
                float _Spread;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            /// <summary>圆角矩形有向距离：p 为以牌中心为原点的 UV（四角为 ±0.5），d<0 在牌内、d=0 在轮廓上。</summary>
            float RoundedRectSDF(float2 p, float2 halfSize, float corner)
            {
                float2 q = abs(p) - (halfSize - corner);
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - corner;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv - 0.5;
                float d = RoundedRectSDF(p, _FootprintHalf, _CornerRadius);

                // 光环从牌轮廓（d=0）向外衰减，_Spread 控制扩散宽度
                float glow = 1.0 - saturate(d / max(_Spread, 1e-4));
                glow = smoothstep(0.0, 1.0, glow);

                half pulse = 1.0h + sin(_Time.y * _PulseSpeed) * _PulseAmount;
                half3 emission = _Color.rgb * (_Intensity * pulse) * glow * _Color.a;
                return half4(emission, 0.0h);
            }
            ENDHLSL
        }
    }
}
