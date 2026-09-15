Shader "Mahjong/Tile Contact Shadow"
{
    Properties
    {
        [PerRendererData] _ContactShape("Half size / radius / feather", Vector) = (6, 7.98, 0.9, 1.02)
        [PerRendererData] _ContactOpacity("Opacity", Vector) = (0.36, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "TileContactShadow"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma instancing_options forcemaxcount:128
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            UNITY_INSTANCING_BUFFER_START(Contact)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ContactShape)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ContactOpacity)
            UNITY_INSTANCING_BUFFER_END(Contact)
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 position : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input) {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                float4 shape = UNITY_ACCESS_INSTANCED_PROP(Contact, _ContactShape);
                output.position = input.positionOS.xz * (shape.xy + shape.ww);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target {
                UNITY_SETUP_INSTANCE_ID(input);
                float4 shape = UNITY_ACCESS_INSTANCED_PROP(Contact, _ContactShape);
                float2 q = abs(input.position) - shape.xy + shape.zz;
                float distance = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - shape.z;
                float alpha = (1.0 - smoothstep(-shape.w, shape.w, distance))
                    * UNITY_ACCESS_INSTANCED_PROP(Contact, _ContactOpacity).x;
                return half4(0, 0, 0, alpha);
            }
            ENDHLSL
        }
    }
}
