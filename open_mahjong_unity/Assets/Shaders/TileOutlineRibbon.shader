Shader "Hidden/Mahjong/TileOutlineRibbon"
{
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
                // Keep this support stroke inside the existing outer hull width.
                // The Renderer Feature supplies this width for all pooled instances.
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
                float signedOffset = outerWidth * (1.0 - input.ribbonSide.x);
                float2 ribbonNDC = endpointNDC
                    + screenPerpendicular * (2.0 * signedOffset / _ScaledScreenParams.xy);

                // Expand only outside the true visible silhouette and retain
                // the physical edge's clip depth. No depth bias or ray-plane
                // extrapolation, and no inverse projection dependency.
                endpointCS.xy = ribbonNDC * endpointCS.w;
                output.positionCS = endpointCS;
                return output;
            }

            half4 RibbonFrag(RibbonVaryings input) : SV_Target
            {
                return _TileHullOutlineColor;
            }
            ENDHLSL
        }
    }
}
