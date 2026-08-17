Shader "Custom/ThreeDTiles"
{
    Properties
    {
        _FrontTex ("Front Texture (牌面)", 2D) = "white" {}
        _FrontColor ("Front Tint", Color) = (1,1,1,1)
        _FrontTilingOffset ("Front Tiling & Offset", Vector) = (1,1,0,0)

        // 3D 牌面背景：可由「牌面背景」页上传；前景花纹（_FrontTex）按 alpha 叠加在上面。
        _FrontBgTex ("Front Bg Texture (3D 牌面背景)", 2D) = "white" {}
        _FrontBgBlend ("Front Bg Blend (0=整面 _FrontTex, 1=底图+前景)", Range(0, 1)) = 0
        _FrontBgColor ("Front Bg Tint", Color) = (1,1,1,1)
        _FrontBgTilingOffset ("Front Bg Tiling & Offset", Vector) = (1,1,0,0)

        _BackTex ("Back Texture (牌背)", 2D) = "white" {}
        _BackColor ("Back Tint", Color) = (1,1,1,1)
        _BackTexBlend ("Back Texture Blend", Range(0, 1)) = 0
        _BackTexExtendEdge ("Back Tex Extend Edge", Range(0, 1)) = 0
        _BackTilingOffset ("Back Tiling & Offset", Vector) = (1,1,0,0)
        _BackEdgeColor ("Back Edge Color (背面侧边)", Color) = (0.218, 0.372, 0.66, 1)

        _SideTex ("Side Texture (侧面)", 2D) = "white" {}
        _SideColor ("Side Tint", Color) = (1,1,1,1)
        _SideTilingOffset ("Side Tiling & Offset", Vector) = (1,1,0,0)

        _GrayScale ("Gray Scale", Range(0, 1)) = 0.0
        _FrontRotation ("Front Rotation (度)", Range(0, 360)) = 0.0
        [HideInInspector] _TileInstanceParams ("Tile Instance Params", Vector) = (0,0,0,0)
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
            TEXTURE2D(_FrontBgTex); SAMPLER(sampler_FrontBgTex);
            TEXTURE2D(_BackTex);  SAMPLER(sampler_BackTex);
            TEXTURE2D(_SideTex);  SAMPLER(sampler_SideTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _FrontTex_ST;
                float4 _FrontBgTex_ST;
                float4 _BackTex_ST;
                float4 _SideTex_ST;
                float4 _SideTilingOffset;
                half _BackTexBlend;
                half _BackTexExtendEdge;
                half _FrontRotation;
                half _FrontBgBlend;
                half4 _FrontBgColor;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(TilePerInstance)
                UNITY_DEFINE_INSTANCED_PROP(float4, _FrontTilingOffset)
                // 牌背贴图逐牌旋转角（度）：按牌当前世界朝向与相机统一校正，保证背图朝镜头正向；0 = 默认
                UNITY_DEFINE_INSTANCED_PROP(float, _BackRotation)
                UNITY_DEFINE_INSTANCED_PROP(float4, _FrontColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BackColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _SideColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BackEdgeColor)
                // x = gray scale, y = outline ObjectID
                UNITY_DEFINE_INSTANCED_PROP(float4, _TileInstanceParams)
            UNITY_INSTANCING_BUFFER_END(TilePerInstance)

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

                float4 frontTilingOffset =
                    UNITY_ACCESS_INSTANCED_PROP(TilePerInstance, _FrontTilingOffset);
                float backRotation =
                    UNITY_ACCESS_INSTANCED_PROP(TilePerInstance, _BackRotation);
                half4 frontColor =
                    UNITY_ACCESS_INSTANCED_PROP(TilePerInstance, _FrontColor);
                half4 backColor =
                    UNITY_ACCESS_INSTANCED_PROP(TilePerInstance, _BackColor);
                half4 sideColor =
                    UNITY_ACCESS_INSTANCED_PROP(TilePerInstance, _SideColor);
                half4 backEdgeColor =
                    UNITY_ACCESS_INSTANCED_PROP(TilePerInstance, _BackEdgeColor);
                float4 instanceParams =
                    UNITY_ACCESS_INSTANCED_PROP(TilePerInstance, _TileInstanceParams);

                float2 frontUV =
                    input.uvFront * frontTilingOffset.xy + frontTilingOffset.zw;
                if (_FrontRotation != 0.0h)
                {
                    frontUV = RotateUV(frontUV, _FrontRotation);
                }

                // _FrontBgTex：当 _FrontBgBlend=1 时，_FrontBgTex 作为底图铺满牌面，
                // _FrontTex（前景花纹）按其 RGB 与 alpha 覆盖在底图上方（无花纹处仍透底图色），
                // 与手牌牌面背景的「透明花纹叠在底图上」完全一致。
                // 默认 _FrontBgBlend=0 时整面保持原 _FrontTex 行为。
                half4 front = SAMPLE_TEXTURE2D(_FrontTex, sampler_FrontTex, frontUV) * frontColor;
                if (saturate(_FrontBgBlend) > 0.0h)
                {
                    float4 frontBgTilingOffset = float4(1.0h, 1.0h, 0.0h, 0.0h);
                    float2 frontBgUV = input.uvFront * frontBgTilingOffset.xy + frontBgTilingOffset.zw;
                    if (_FrontRotation != 0.0h)
                    {
                        frontBgUV = RotateUV(frontBgUV, _FrontRotation);
                    }
                    half4 bgSample = SAMPLE_TEXTURE2D(_FrontBgTex, sampler_FrontBgTex, frontBgUV);
                    // 底图 RGB 铺底；前景花纹 RGB 按其 alpha 覆盖在底图上方（alpha=0 时仍透底图）。
                    // _FrontBgBlend 仅作启用开关：0 = 关闭底图（前景占满），1 = 启用底图（与「使用/不使用 3D 牌面背景」toggle 对应）。
                    half bgAlpha = bgSample.a * saturate(_FrontBgBlend);
                    half3 bgRgb = lerp(front.rgb, bgSample.rgb, bgAlpha);
                    // 前景有花纹处（front.a > 0）显示花纹 RGB，否则透出底图 RGB。
                    half3 composedRgb = lerp(bgRgb, front.rgb, saturate(front.a));
                    half composedA = max(saturate(front.a), bgAlpha);
                    front = half4(composedRgb, composedA);
                }
                float2 backUV = input.uvBack;
                if (backRotation != 0.0f)
                {
                    backUV = RotateUV(backUV, backRotation);
                }
                // 牌背大面：颜色打底，图片按自身 alpha 叠加在上方（不乘算颜色）。
                // 无图片时 _BackTexBlend=0，整面为纯 _BackColor。
                half4 backSample = SAMPLE_TEXTURE2D(_BackTex, sampler_BackTex, backUV);
                half3 backRgb = lerp(backColor.rgb, backSample.rgb, saturate(_BackTexBlend) * backSample.a);
                half4 back = half4(backRgb, 1.0h);
                float2 sideUV = input.uvSide * _SideTilingOffset.xy + _SideTilingOffset.zw;
                half4 side = SAMPLE_TEXTURE2D(_SideTex, sampler_SideTex, sideUV) * sideColor;

                // 背侧边顶点色为黑色(RGB 和<1)时显示 _BackColor 底色；
                // 该写法兼容当前模型(首颜色层 alpha=1)与清理后的 alpha 方案(背侧边仍为黑色)。
                half backEdgeMask = saturate(1.0h - (input.color.r + input.color.g + input.color.b));
                // 默认背面边缘用纯色；开启延伸后把牌背图铺到背部边缘，方便整张渐变图。
                half extendEdge = saturate(_BackTexExtendEdge) * saturate(_BackTexBlend) * backSample.a;
                half3 edgeRgb = lerp(backEdgeColor.rgb, backSample.rgb, extendEdge);
                half4 col = front * input.color.r + back * input.color.g
                          + side * input.color.b
                          + half4(edgeRgb, 1.0h) * backEdgeMask;

                half grayScale = (half)instanceParams.x;
                if (grayScale > 0.0h)
                {
                    half gray = dot(col.rgb, half3(0.299h, 0.587h, 0.114h));
                    col.rgb = lerp(col.rgb, half3(gray, gray, gray), grayScale);
                }

                col.a = 1.0h;
                return col;
            }
            ENDHLSL
        }

        // 写逐实例 ObjectID（_TileInstanceParams.y），供全屏描边
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

            UNITY_INSTANCING_BUFFER_START(TilePerInstance)
                // x = gray scale, y = outline ObjectID
                UNITY_DEFINE_INSTANCED_PROP(float4, _TileInstanceParams)
            UNITY_INSTANCING_BUFFER_END(TilePerInstance)

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
                output.outlineId =
                    UNITY_ACCESS_INSTANCED_PROP(TilePerInstance, _TileInstanceParams).y;
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
                float4 _FrontBgTex_ST;
                float4 _BackTex_ST;
                float4 _SideTex_ST;
                float4 _SideTilingOffset;
                half _FrontRotation;
                half4 _FrontBgColor;
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
                float4 _FrontBgTex_ST;
                float4 _BackTex_ST;
                float4 _SideTex_ST;
                float4 _SideTilingOffset;
                half _FrontRotation;
                half4 _FrontBgColor;
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
