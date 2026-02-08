// MIT License ... (保持原注释)

Shader "OutlinesBackFaceOutlines" {
    Properties {
        _Thickness ("Thickness", Float) = 1
        _Color ("Color", Color) = (1,1,1,1)
        [Toggle(USE_PRECALCULATED_OUTLINE_NORMALS)] _PrecalculateNormals("Use UV1 normals", Float) = 0
    }

    SubShader {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass {
            Name "Outlines"
            Cull Front

            HLSLPROGRAM
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma shader_feature USE_PRECALCULATED_OUTLINE_NORMALS
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
#ifdef USE_PRECALCULATED_OUTLINE_NORMALS
                float3 smoothNormalOS : TEXCOORD1;
#endif
            };

            struct VertexOutput {
                float4 positionCS : SV_POSITION;
            };

            float _Thickness;
            float4 _Color;

            VertexOutput Vertex(Attributes input) {
                VertexOutput output = (VertexOutput)0;

                float3 normalOS = input.normalOS;
#ifdef USE_PRECALCULATED_OUTLINE_NORMALS
                normalOS = input.smoothNormalOS;
#endif

                float3 posOS = input.positionOS.xyz + normalOS * _Thickness;
                output.positionCS = GetVertexPositionInputs(posOS).positionCS;

                return output;
            }

            float4 Fragment(VertexOutput input) : SV_Target {
                return _Color;
            }

            ENDHLSL
        }
    }
}