// Mode B：屏幕空间 ObjectID 描边。
// _OutlineExpand = 外扩距离；_OutlineWidth = 描边线宽（≤外扩时为外环，≥外扩时实心外扩）。
Shader "Hidden/TileOutlineEdge"
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
            Name "TileOutlineEdge"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D_X(_TileIdTex);
            float4 _OutlineColor;
            float _OutlineWidth;
            float _OutlineExpand;
            float _DebugVisualizeId;

            float SampleId(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_TileIdTex, sampler_PointClamp, uv, 0).r;
            }

            float SampleLinearDepth(float2 uv)
            {
                float raw = SampleSceneDepth(uv);
                return LinearEyeDepth(raw, _ZBufferParams);
            }

            // dist ≈ 到前牌轮廓的像素距离；在 [expand-width, expand] 画环，width≥expand 则 0..expand 实心。
            float WeightAtDist(float dist, float expandPx, float strokePx)
            {
                if (dist > expandPx + 0.75)
                {
                    return 0.0;
                }

                if (strokePx >= expandPx - 0.01)
                {
                    return saturate(1.0 - (dist - 0.25) / max(expandPx + 0.25, 1.0));
                }

                float inner = expandPx - strokePx;
                float outerFall = saturate((expandPx + 0.5 - dist));
                float innerFall = saturate((dist - inner + 0.5));
                return min(outerFall, innerFall);
            }

            float OutwardCoverage(float2 uv, float2 px, float expandPx, float strokePx)
            {
                float thr = 0.5 / 255.0;
                float idC = SampleId(uv);
                float depthC = SampleLinearDepth(uv);

                int radius = (int)max(ceil(expandPx), 1.0);
                float best = 0.0;

                [loop]
                for (int dy = -radius; dy <= radius; dy++)
                {
                    [loop]
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (dx == 0 && dy == 0)
                        {
                            continue;
                        }

                        float dist = length(float2((float)dx, (float)dy));
                        if (dist > expandPx + 0.75)
                        {
                            continue;
                        }

                        float2 nUv = uv + float2((float)dx, (float)dy) * px;
                        float idN = SampleId(nUv);
                        if (idN < thr)
                        {
                            continue;
                        }

                        if (abs(idC - idN) < thr)
                        {
                            continue;
                        }

                        float depthN = SampleLinearDepth(nUv);
                        if (depthN > depthC + 1e-3)
                        {
                            continue;
                        }

                        best = max(best, WeightAtDist(dist, expandPx, strokePx));
                    }
                }

                return best;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                float2 px = abs(_BlitTexture_TexelSize.xy);
                if (px.x < 1e-8 || px.y < 1e-8)
                {
                    px = rcp(max(_ScreenParams.xy, float2(1, 1)));
                }

                float expandPx = max(_OutlineExpand, 1.0);
                float strokePx = clamp(_OutlineWidth, 0.5, expandPx);
                half4 scene = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);

                if (_DebugVisualizeId > 0.5)
                {
                    float idC = SampleId(uv);
                    float thr = 0.5 / 255.0;
                    if (idC < thr)
                    {
                        return scene;
                    }
                    half3 red = half3(1, 0.05, 0.05);
                    return half4(lerp(scene.rgb, red, 0.75), 1);
                }

                float cover = 0.0;
                [unroll]
                for (int iy = 0; iy < 4; iy++)
                {
                    [unroll]
                    for (int ix = 0; ix < 4; ix++)
                    {
                        float2 offset = float2(
                            ((ix + 0.5) * 0.25 - 0.5),
                            ((iy + 0.5) * 0.25 - 0.5));
                        cover += OutwardCoverage(uv + offset * px, px, expandPx, strokePx);
                    }
                }
                cover *= (1.0 / 16.0);
                cover = smoothstep(0.08, 0.72, cover);

                return half4(lerp(scene.rgb, _OutlineColor.rgb, cover), scene.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
