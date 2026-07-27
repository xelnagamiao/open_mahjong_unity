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
        // RGB 直接叠加到相机颜色；alpha 保持目标原值。
        Blend SrcAlpha OneMinusSrcAlpha, Zero One

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
            TEXTURE2D_X(_TileOutlineMask);
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
                // 显式 LOD 避免在动态邻域循环中生成隐式梯度指令。
                float2 depthUv = ClampAndScaleUVForBilinear(
                    UnityStereoTransformScreenSpaceTex(uv),
                    _CameraDepthTexture_TexelSize.xy);
                float raw = SAMPLE_TEXTURE2D_X_LOD(
                    _CameraDepthTexture, sampler_PointClamp, depthUv, 0).r;
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

                // 调试模式绕过边缘 mask，保持完整牌面 ID 可见。
                if (_DebugVisualizeId > 0.5)
                {
                    float idC = SampleId(uv);
                    float thr = 0.5 / 255.0;
                    if (idC < thr)
                    {
                        clip(-1);
                    }
                    half3 red = half3(1, 0.05, 0.05);
                    return half4(red, 0.75);
                }

                // 空白桌面和牌面深处在任何 ObjectID/深度邻域采样之前退出。
                float edgeCandidate = SAMPLE_TEXTURE2D_X_LOD(
                    _TileOutlineMask, sampler_PointClamp, uv, 0).r;
                clip(edgeCandidate - 0.5);

                // ID、mask 与 activeColor 同分辨率；_ScreenParams 同时兼容动态分辨率和 XR。
                float2 px = rcp(max(_ScreenParams.xy, float2(1, 1)));

                float expandPx = max(_OutlineExpand, 1.0);
                float strokePx = clamp(_OutlineWidth, 0.5, expandPx);

                // 单次覆盖率（关闭 4x4 SSAA；仅影响描边，与 URP MSAA 无关）
                float cover = OutwardCoverage(uv, px, expandPx, strokePx);
                cover = smoothstep(0.08, 0.72, cover);

                return half4(_OutlineColor.rgb, cover);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
