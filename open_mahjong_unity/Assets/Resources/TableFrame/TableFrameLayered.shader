Shader "TableFrame/Layered Thick Frame"
{
    Properties
    {
        [MainTexture] _BaseMap("Clean base atlas (UV0)", 2D) = "white" {}
        _BaseTone("Base tone / display sRGB multiplier", Vector) = (1,1,1,1)
        _TrimMap("Optional lines / Normal RGBA", 2D) = "black" {}
        _ShadowMap("Frame shadow / Multiply RGBA", 2D) = "white" {}
        _HighlightMap("Frame highlight / Screen RGBA", 2D) = "black" {}
        [HideInInspector] _HasTrim("Has lines texture", Float) = 0
        _LineIntensity("Lines opacity", Range(0,1)) = 0
        _ShadowLayerIntensity("PSD shadow opacity multiplier", Range(0,1)) = 1
        _HighlightIntensity("PSD highlight opacity multiplier", Range(0,1)) = 1
        _ShadowStrength("Optional received shadow", Range(0,1)) = 0
        _NormalInfluence("Optional live normal shading", Range(0,1)) = 0
        _ContactWidth("Inner contact line height / model units", Range(0,2)) = 0.5
        _ContactOpacity("Inner contact black opacity", Range(0,1)) = 0.98
        [Toggle] _ReviewGray("Review geometry normals", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Cull Back
        ZWrite On
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_TrimMap); SAMPLER(sampler_TrimMap);
        TEXTURE2D(_ShadowMap); SAMPLER(sampler_ShadowMap);
        TEXTURE2D(_HighlightMap); SAMPLER(sampler_HighlightMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseTone;
            half _HasTrim;
            half _LineIntensity;
            half _ShadowLayerIntensity;
            half _HighlightIntensity;
            half _ShadowStrength;
            half _NormalInfluence;
            float _ContactWidth;
            half _ContactOpacity;
            half _ReviewGray;
        CBUFFER_END
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 normalWS : TEXCOORD0;
            float2 uv : TEXCOORD1;
            float4 shadowCoord : TEXCOORD2;
            half fogFactor : TEXCOORD3;
            float2 contact : TEXCOORD4;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        float3 ToDisplaySrgb(float3 value)
        {
            #if defined(UNITY_COLORSPACE_GAMMA)
                return value;
            #else
                return LinearToSRGB(value);
            #endif
        }
        float3 FromDisplaySrgb(float3 value)
        {
            #if defined(UNITY_COLORSPACE_GAMMA)
                return value;
            #else
                return SRGBToLinear(value);
            #endif
        }
        Varyings Vert(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
            output.positionCS = p.positionCS;
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            output.shadowCoord = GetShadowCoord(p);
            output.fogFactor = ComputeFogFactor(p.positionCS.z);
            // The authored frame uses Y=0 at the cloth. Only inward-facing
            // walls receive this line; the outer body and horizontal top do not.
            output.contact = float2(input.positionOS.y,
                dot(input.positionOS.xz, input.normalOS.xz) < -0.5 ? 1.0 : 0.0);
            return output;
        }
        ENDHLSL
        Pass
        {
            Name "LayeredFrame"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                Light light = GetMainLight(input.shadowCoord);
                half ndotl = saturate(dot(normalize(input.normalWS), light.direction));
                half3 color;
                if (_ReviewGray > .5h)
                    color = .215h * (.28h + .72h * ndotl) * lerp(.7h, 1.h, light.shadowAttenuation);
                else
                {
                    // PSD order: clean base -> optional Normal lines -> Multiply
                    // shadow -> Screen highlight, all in display sRGB. Layer
                    // alpha already includes its original Photoshop opacity.
                    float3 composed = ToDisplaySrgb(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb);
                    composed *= _BaseTone.rgb;
                    if (_HasTrim > .5h && _LineIntensity > 0)
                    {
                        float4 trim = SAMPLE_TEXTURE2D(_TrimMap, sampler_TrimMap, input.uv);
                        composed = lerp(composed, ToDisplaySrgb(trim.rgb), saturate(trim.a * _LineIntensity));
                    }
                    float4 shadow = SAMPLE_TEXTURE2D(_ShadowMap, sampler_ShadowMap, input.uv);
                    composed *= lerp(float3(1, 1, 1), ToDisplaySrgb(shadow.rgb), saturate(shadow.a * _ShadowLayerIntensity));
                    float4 highlight = SAMPLE_TEXTURE2D(_HighlightMap, sampler_HighlightMap, input.uv);
                    float3 screened = 1.0 - (1.0 - composed) * (1.0 - ToDisplaySrgb(highlight.rgb));
                    composed = lerp(composed, screened, saturate(highlight.a * _HighlightIntensity));
                    // A narrow black joint at the cloth contact, independent
                    // of the textured brown wall and its existing PSD shading.
                    float aa = max(fwidth(input.contact.x) * 0.5, 0.001);
                    float contact = (1.0 - smoothstep(_ContactWidth - aa, _ContactWidth + aa, input.contact.x))
                        * saturate(input.contact.y) * step(0.001, _ContactWidth);
                    composed *= 1.0 - saturate(contact * _ContactOpacity);
                    // The shared shadow owns body shading. Do not multiply the
                    // former inner .65 or shoulder gain into the clean base.
                    color = FromDisplaySrgb(composed) * lerp(1.h, .65h + .35h * ndotl, _NormalInfluence)
                                  * lerp(1.h, light.shadowAttenuation, _ShadowStrength);
                }
                return half4(MixFog(color, input.fogFactor), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            half4 DepthFrag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment NormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing
            half4 NormalsFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 normalWS = normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(normalWS);
                    return half4(PackFloat2To888(saturate(oct * .5 + .5)), 0);
                #else
                    return half4(normalWS, 0);
                #endif
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            float3 _LightDirection;
            float3 _LightPosition;
            Varyings ShadowVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 direction = normalize(_LightPosition - positionWS);
                #else
                    float3 direction = _LightDirection;
                #endif
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, direction)));
                return output;
            }
            half4 ShadowFrag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback Off
}
