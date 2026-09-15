Shader "Hidden/Mahjong/TableSeamComposite"
{
    Properties
    {
        _MainTex ("Cloth", 2D) = "white" {}
        _SeamTex ("Seam", 2D) = "black" {}
        _SourceLightTex ("Original PSD Light", 2D) = "black" {}
        _HasSeam ("Has Seam", Float) = 0
        _UseSolidColor ("Use Solid Color", Float) = 0
        _SolidColor ("Solid Color sRGB", Vector) = (1,1,1,1)
        _UseSourceLight ("Use Original Light", Float) = 0
        _ShadowParameters ("Edge Shadow", Vector) = (0,0,0,0)
        _FixedShadowParameters ("Fixed Planar Shadow", Vector) = (0,0,0,0)
        _LightParameters ("Center Light", Vector) = (1,1,2,0)
        _LightCenter ("Light Center", Vector) = (0.5,0.5,0,0)
        _ClothUvRect ("Visible Cloth UV", Vector) = (0.0702578,0.0702578,0.8594844,0.8594844)
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
            sampler2D _SourceLightTex;
            float _HasSeam, _UseSourceLight;
            float _UseSolidColor;
            float4 _SolidColor;
            float4 _ShadowParameters, _FixedShadowParameters, _LightParameters, _LightCenter, _ClothUvRect;

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
            float EdgeMask(float2 uv, float width)
            {
                float2 distance = min(uv, 1.0 - uv);
                float2 side = 1.0 - smoothstep(0.0, max(width, 0.00001), distance);
                // Side overlap gives softly deeper corners without circular vignette bands.
                return 1.0 - (1.0 - side.x) * (1.0 - side.y);
            }

            float4 Composite(Varyings input) : SV_Target
            {
                float4 cloth = tex2D(_MainTex, input.uv);
                if (_UseSolidColor > 0.5)
                {
                    cloth = _SolidColor;
                    #ifndef UNITY_COLORSPACE_GAMMA
                    cloth.rgb = ToLinear(cloth.rgb);
                    #endif
                }
                float4 seam = _HasSeam > 0.5 ? tex2D(_SeamTex, input.uv) : float4(0,0,0,0);
                // The no-effect path preserves original source sampling and color exactly.
                if (seam.a <= 0.0 && _ShadowParameters.y <= 0.0 && _FixedShadowParameters.z <= 0.0 && _LightParameters.w <= 0.0) return cloth;
                #ifndef UNITY_COLORSPACE_GAMMA
                cloth.rgb = ToSrgb(cloth.rgb);
                seam.rgb = ToSrgb(seam.rgb);
                #endif
                float alpha = seam.a + cloth.a * (1.0 - seam.a);
                float3 rgb = (seam.rgb * seam.a + cloth.rgb * cloth.a * (1.0 - seam.a)) / max(alpha, 0.000001);
                float2 clothUv = saturate((input.uv - _ClothUvRect.xy) / _ClothUvRect.zw);
                float gain = 0.0;
                if (_LightParameters.w > 0.0)
                {
                    if (_UseSourceLight > 0.5)
                    {
                        // The original Git PSD and texture span the entire atlas.
                        // This alpha already includes the layer's 20% opacity.
                        gain = tex2D(_SourceLightTex, input.uv).a;
                    }
                    else
                    {
                        float2 q = abs((clothUv - _LightCenter.xy) / max(_LightParameters.xy, 0.00001));
                        float p = _LightParameters.z;
                        float radius = pow(pow(q.x, p) + pow(q.y, p), 1.0 / p);
                        gain = _LightParameters.w * (1.0 - smoothstep(0.0, 1.0, radius));
                    }
                }
                // Match the original PSD's white Normal layer in sRGB. The other
                // presets use the same blend so independent PSD layers reproduce it.
                rgb = lerp(rgb, float3(1.0, 1.0, 1.0), saturate(gain));
                float shade;
                if (_FixedShadowParameters.w > 0.5)
                {
                    // Measure once from the nearest cloth edge in atlas units.
                    // Equal-distance corners and straight edges share the same opacity.
                    float2 distance = min(clothUv, 1.0 - clothUv) * _ClothUvRect.zw;
                    float inward = min(distance.x, distance.y);
                    float band = 1.0 - smoothstep(_FixedShadowParameters.x, _FixedShadowParameters.y, inward);
                    shade = 1.0 - _FixedShadowParameters.z * band;
                }
                else
                {
                    shade = 1.0 - _ShadowParameters.y * EdgeMask(clothUv, _ShadowParameters.x);
                    shade *= 1.0 - _ShadowParameters.w * EdgeMask(clothUv, _ShadowParameters.z);
                }
                rgb *= shade;
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
