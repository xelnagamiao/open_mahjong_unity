Shader "Hidden/Mahjong/TableSeamComposite"
{
    Properties
    {
        _MainTex ("Cloth", 2D) = "white" {}
        _SeamTex ("Seam", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Composite
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            sampler2D _SeamTex;

            struct Attributes
            {
                float4 position : POSITION;
                float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.position);
                output.uv = input.uv;
                return output;
            }

            float3 ToSrgb(float3 value)
            {
                return float3(LinearToGammaSpaceExact(value.r), LinearToGammaSpaceExact(value.g), LinearToGammaSpaceExact(value.b));
            }
            float3 ToLinear(float3 value)
            {
                return float3(GammaToLinearSpaceExact(value.r), GammaToLinearSpaceExact(value.g), GammaToLinearSpaceExact(value.b));
            }
            float4 Composite(Varyings input) : SV_Target
            {
                float4 cloth = tex2D(_MainTex, input.uv);
                float4 seam = tex2D(_SeamTex, input.uv);
                // Preserve the cloth sample exactly wherever the overlay is clear.
                if (seam.a <= 0.0) return cloth;
                #ifndef UNITY_COLORSPACE_GAMMA
                cloth.rgb = ToSrgb(cloth.rgb);
                seam.rgb = ToSrgb(seam.rgb);
                #endif
                float alpha = seam.a + cloth.a * (1.0 - seam.a);
                float3 rgb = (seam.rgb * seam.a + cloth.rgb * cloth.a * (1.0 - seam.a)) / max(alpha, 0.000001);
                #ifndef UNITY_COLORSPACE_GAMMA
                rgb = ToLinear(rgb);
                #endif
                return float4(rgb, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
