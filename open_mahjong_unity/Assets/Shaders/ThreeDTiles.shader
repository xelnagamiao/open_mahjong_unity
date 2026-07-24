Shader "Custom/ThreeDTiles"
{
    Properties
    {
        _FrontTex ("Front Texture (牌面)", 2D) = "white" {}
        _FrontColor ("Front Tint", Color) = (1,1,1,1)
        _FrontTilingOffset ("Front Tiling & Offset", Vector) = (1,1,0,0)

        _BackTex ("Back Texture (牌背)", 2D) = "white" {}
        _BackColor ("Back Tint", Color) = (1,1,1,1)
        _BackTilingOffset ("Back Tiling & Offset", Vector) = (1,1,0,0)

        _SideTex ("Side Texture (侧面)", 2D) = "white" {}
        _SideColor ("Side Tint", Color) = (1,1,1,1)
        _SideTilingOffset ("Side Tiling & Offset", Vector) = (1,1,0,0)

        _GrayScale ("Gray Scale", Range(0, 1)) = 0.0
        _FrontRotation ("Front Rotation (度)", Range(0, 360)) = 0.0
        _OutlineId ("Outline Id", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 200

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_FrontTex); SAMPLER(sampler_FrontTex);
            TEXTURE2D(_BackTex);  SAMPLER(sampler_BackTex);
            TEXTURE2D(_SideTex);  SAMPLER(sampler_SideTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _FrontTex_ST;
                float4 _BackTex_ST;
                float4 _SideTex_ST;
                half4 _FrontColor;
                half4 _BackColor;
                half4 _SideColor;
                float4 _FrontTilingOffset;
                float4 _BackTilingOffset;
                float4 _SideTilingOffset;
                half _GrayScale;
                half _FrontRotation;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uvFront : TEXCOORD0;
                float2 uvBack : TEXCOORD1;
                float2 uvSide : TEXCOORD2;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float2 RotateUV(float2 uv, float angleDeg)
            {
                float rad = angleDeg * 0.017453292519943295;
                float c = cos(rad);
                float s = sin(rad);
                float2 centered = uv - 0.5;
                return float2(centered.x * c - centered.y * s, centered.x * s + centered.y * c) + 0.5;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uvFront = input.uv0;
                output.uvBack = input.uv1;
                output.uvSide = input.uv2;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 frontUV = input.uvFront * _FrontTilingOffset.xy + _FrontTilingOffset.zw;
                if (_FrontRotation != 0.0h)
                {
                    frontUV = RotateUV(frontUV, _FrontRotation);
                }

                half4 front = SAMPLE_TEXTURE2D(_FrontTex, sampler_FrontTex, frontUV) * _FrontColor;
                float2 backUV = input.uvBack * _BackTilingOffset.xy + _BackTilingOffset.zw;
                half4 back = SAMPLE_TEXTURE2D(_BackTex, sampler_BackTex, backUV) * _BackColor;
                float2 sideUV = input.uvSide * _SideTilingOffset.xy + _SideTilingOffset.zw;
                half4 side = SAMPLE_TEXTURE2D(_SideTex, sampler_SideTex, sideUV) * _SideColor;

                half4 col = front * input.color.r + back * input.color.g + side * input.color.b;

                if (_GrayScale > 0.0h)
                {
                    half gray = dot(col.rgb, half3(0.299h, 0.587h, 0.114h));
                    col.rgb = lerp(col.rgb, half3(gray, gray, gray), _GrayScale);
                }

                col.a = 1.0h;
                return col;
            }
            ENDHLSL
        }

        // 写 ObjectID（MaterialPropertyBlock._OutlineId），供全屏描边
        Pass
        {
            Name "TileId"
            Tags { "LightMode" = "TileId" }

            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex TileIdVert
            #pragma fragment TileIdFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _OutlineId;

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                nointerpolation float outlineId : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings TileIdVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.outlineId = _OutlineId;
                return output;
            }

            half4 TileIdFrag(Varyings input) : SV_Target
            {
                // R 通道存 id/255，供全屏边缘检测
                float id = saturate(max(input.outlineId, 0.0) * (1.0 / 255.0));
                return half4(id, 0, 0, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _FrontTex_ST;
                float4 _BackTex_ST;
                float4 _SideTex_ST;
                half4 _FrontColor;
                half4 _BackColor;
                half4 _SideColor;
                float4 _FrontTilingOffset;
                float4 _BackTilingOffset;
                float4 _SideTilingOffset;
                half _GrayScale;
                half _FrontRotation;
            CBUFFER_END

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 GetShadowPositionHClip(Attributes input)
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetShadowPositionHClip(input);
                return output;
            }

            half4 ShadowPassFragment(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _FrontTex_ST;
                float4 _BackTex_ST;
                float4 _SideTex_ST;
                half4 _FrontColor;
                half4 _BackColor;
                half4 _SideColor;
                float4 _FrontTilingOffset;
                float4 _BackTilingOffset;
                float4 _SideTilingOffset;
                half _GrayScale;
                half _FrontRotation;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthOnlyVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthOnlyFragment(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
