// Two-pass separable ObjectID range test.
// Pass 0 writes horizontal min/max IDs to RG8.
// Pass 1 reduces the vertical range and emits an R8 edge-candidate mask.
Shader "Hidden/TileOutlineMask"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        Pass
        {
            Name "TileOutlineMinMaxHorizontal"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment FragHorizontal

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _MaskRadius;

            half4 FragHorizontal(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float2 stepUv = float2(
                    rcp(max(_ScreenParams.x, 1.0)),
                    0.0);
                int radius = (int)clamp(ceil(_MaskRadius), 1.0, 4.0);
                float minId = 1.0;
                float maxId = 0.0;

                [unroll]
                for (int offset = -4; offset <= 4; offset++)
                {
                    if (abs(offset) <= radius)
                    {
                        float id = SAMPLE_TEXTURE2D_X_LOD(
                            _BlitTexture,
                            sampler_PointClamp,
                            uv + stepUv * (float)offset,
                            0).r;
                        minId = min(minId, id);
                        maxId = max(maxId, id);
                    }
                }

                return half4(minId, maxId, 0, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "TileOutlineMinMaxVertical"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment FragVertical

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _MaskRadius;

            half4 FragVertical(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float2 stepUv = float2(
                    0.0,
                    rcp(max(_ScreenParams.y, 1.0)));
                int radius = (int)clamp(ceil(_MaskRadius), 1.0, 4.0);
                float minId = 1.0;
                float maxId = 0.0;

                [unroll]
                for (int offset = -4; offset <= 4; offset++)
                {
                    if (abs(offset) <= radius)
                    {
                        float2 idRange = SAMPLE_TEXTURE2D_X_LOD(
                            _BlitTexture,
                            sampler_PointClamp,
                            uv + stepUv * (float)offset,
                            0).rg;
                        minId = min(minId, idRange.r);
                        maxId = max(maxId, idRange.g);
                    }
                }

                // R8 IDs differ by at least 1/255. A half-step rejects precision noise.
                half edgeCandidate = step(0.5 / 255.0, maxId - minId);
                return half4(edgeCandidate, 0, 0, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
