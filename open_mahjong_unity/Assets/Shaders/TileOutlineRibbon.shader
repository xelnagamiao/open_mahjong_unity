Shader "Hidden/Mahjong/TileOutlineRibbon"
{
    Properties
    {
        // The default also supports existing materials baked for the original tile mesh.
        [HideInInspector] _TileSourceWidthOS ("Source mesh width", Float) = 0.02
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "TileOutlineHull"
            Tags { "LightMode"="TileOutlineHull" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex RibbonVert
            #pragma fragment RibbonFrag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _TileHullOutlineColor;
            float _TileSilhouetteOutlineWidth;
            CBUFFER_START(UnityPerMaterial)
                float _TileSourceWidthOS;
            CBUFFER_END

            struct RibbonAttributes
            {
                float3 positionOS : POSITION;
                float3 normal0OS : NORMAL;
                float3 otherEndpointOS : TEXCOORD0;
                float3 normal1OS : TEXCOORD1;
                float2 ribbonSide : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct RibbonVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 coverageTimesW : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            RibbonVaryings RibbonVert(RibbonAttributes input)
            {
                RibbonVaryings output = (RibbonVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                if (_TileSilhouetteOutlineWidth <= 0.0 || _TileHullOutlineColor.a <= 0.0)
                {
                    output.positionCS = float4(2, 2, 1, 1);
                    return output;
                }

                float3 endpointWS = TransformObjectToWorld(input.positionOS);
                float3 otherWS = TransformObjectToWorld(input.otherEndpointOS);
                float3 midpointWS = (endpointWS + otherWS) * 0.5;
                float3 normal0WS = TransformObjectToWorldNormal(input.normal0OS, false);
                float3 normal1WS = TransformObjectToWorldNormal(input.normal1OS, false);
                float3 viewMidpointWS = GetWorldSpaceViewDir(midpointWS);
                float facing0 = dot(normal0WS, viewMidpointWS);
                float facing1 = dot(normal1WS, viewMidpointWS);
                bool front0 = facing0 > 0.0;
                bool front1 = facing1 > 0.0;
                // Most crease edges have two visible or two hidden faces.
                // Reject them before projecting any endpoints.
                if (front0 == front1)
                {
                    output.positionCS = float4(2, 2, 1, 1);
                    return output;
                }
                float4 endpointCS = TransformWorldToHClip(endpointWS);
                float4 otherCS = TransformWorldToHClip(otherWS);
                // Physical stroke width in render-target pixels.
                float outerWidth = max(_TileSilhouetteOutlineWidth, 0.0);

                // Midpoint classification is identical at all four vertices;
                // inactive edges collapse to one point outside the clip volume.
                if (outerWidth <= 0.0
                    || _TileHullOutlineColor.a <= 0.0 || endpointCS.w <= 1e-5 || otherCS.w <= 1e-5)
                {
                    output.positionCS = float4(2, 2, 1, 1);
                    return output;
                }

                float2 endpointNDC = endpointCS.xy / endpointCS.w;
                float2 otherNDC = otherCS.xy / otherCS.w;
                float2 edgePixel = (otherNDC - endpointNDC) * _ScaledScreenParams.xy;
                float edgeLengthSq = dot(edgePixel, edgePixel);
                if (edgeLengthSq < 1e-6)
                {
                    output.positionCS = float4(2, 2, 1, 1);
                    return output;
                }
                float2 screenPerpendicular = float2(-edgePixel.y, edgePixel.x) * rsqrt(edgeLengthSq);
                float4 centerCS = TransformObjectToHClip(float3(0, 0, 0));
                float4 midpointCS = (endpointCS + otherCS) * 0.5;
                float2 centerToEdgePixel = (midpointCS.xy / midpointCS.w
                    - centerCS.xy / centerCS.w) * _ScaledScreenParams.xy;
                // The canonical tile is convex and centered at its origin, so
                // its projected center always lies inside its silhouette.
                screenPerpendicular *= dot(screenPerpendicular, centerToEdgePixel) >= 0.0 ? 1.0 : -1.0;
                // Integrate the outside edge over the projected pixel footprint.
                // For narrow user-selected lines the center can be partially covered too.
                float footprint = abs(screenPerpendicular.x) + abs(screenPerpendicular.y);
                float signedOffset = (outerWidth + 0.5 * footprint) * (1.0 - input.ribbonSide.x);
                float coverage = (outerWidth - signedOffset) / footprint + 0.5;
                // Cancel perspective interpolation to keep the ramp linear in screen pixels.
                output.coverageTimesW = float2(coverage, 1.0) * endpointCS.w;
                float2 ribbonNDC = endpointNDC
                    + screenPerpendicular * (2.0 * signedOffset / _ScaledScreenParams.xy);

                endpointCS.xy = ribbonNDC * endpointCS.w;
                // An adjacent tile's bevel can cover parts of the support stroke.
                // Bound the depth adjustment by both the rendered tile and the pixel footprint.
                float tileWidthWS = _TileSourceWidthOS * length(TransformObjectToWorldDir(float3(1, 0, 0), false));
                float pixelHeightWS = 2.0 * endpointCS.w / (abs(UNITY_MATRIX_P._m11) * _ScaledScreenParams.y);
                float liftWS = min(0.05 * tileWidthWS, 1.65 * pixelHeightWS);
                // Project the shift toward the camera in view space, retaining the original
                // screen position and W. The projection handles forward and reversed depth.
                float4 shiftCS = mul(UNITY_MATRIX_P, float4(0, 0, liftWS, 0));
                float depthCS = endpointCS.z + (shiftCS.z * endpointCS.w - endpointCS.z * shiftCS.w)
                    / max(endpointCS.w + shiftCS.w, 1e-5);
                // Stop a visible endpoint at the near plane. Endpoints already clipped stay clipped.
                float nearCS = UNITY_NEAR_CLIP_VALUE * endpointCS.w;
                #if UNITY_REVERSED_Z
                    endpointCS.z = min(depthCS, max(endpointCS.z, nearCS));
                #else
                    endpointCS.z = max(depthCS, min(endpointCS.z, nearCS));
                #endif
                output.positionCS = endpointCS;
                return output;
            }

            half4 RibbonFrag(RibbonVaryings input) : SV_Target
            {
                half4 color = _TileHullOutlineColor;
                color.a *= saturate(input.coverageTimesW.x / input.coverageTimesW.y);
                return color;
            }
            ENDHLSL
        }
    }
}
