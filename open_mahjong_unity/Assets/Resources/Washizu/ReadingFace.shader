Shader "Custom/WashizuReadingFace"
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
        // 上传图的宽高比（0 表示未设置 → 不动 UV）。CardBackManager 在 PersistTableBackground 时写入。
        _FrontBgTexAspect ("Front Bg Tex Aspect (w/h)", Range(0, 16)) = 0
        // 与 TileTextureLayout.TableAspect 一致；此值由 CardBackManager 同步。
        _TableFaceAspect ("Table Face UV Canvas Aspect (w/h)", Float) = 0.7518797
        _TableFaceImageScale ("Complete Image Area Scale", Range(0.1, 1)) = 0.86
        _FrontTexContain ("Standalone Front Contain (0=atlas 1=complete image)", Float) = 0
        // 3D 牌面背景铺满模式：0=完整等比居中 1=铺到整张牌面+侧面边缘（拉伸覆盖）。
        _TableBgCoverFace ("Table Bg Cover Face (0=中央 1=整张)", Range(0, 1)) = 0
        // 3D 牌面纯色（与 3D 牌面背景互斥）：纯色铺底，花纹按 alpha 保留。
        _TableFaceColor ("Table Face Color (纯色)", Color) = (1,1,1,1)
        _TableFaceBlend ("Table Face Blend (0=前景 1=纯色)", Range(0, 1)) = 0
        // 3D 牌面前景无 alpha 通道区域（fluffy/hkmahjong 已透明化的米色背景）的 fallback 颜色；
        // 当 _FrontBgBlend=0 时显示该色，保证原图视觉一致；启用 3D 牌面背景时该色被底图取代。
        _TableFaceFallbackColor ("Table Face Fallback Color (前景透明区)", Color) = (0.961, 0.965, 0.969, 1)
        // 是否启用 fallback 色（仅在不使用 3D 牌面背景时生效）：1=启用，0=前景完全透传（露出底层几何/侧面）。
        _TableFaceFallbackEnabled ("Table Face Fallback Enabled", Range(0, 1)) = 1

        _BackTex ("Back Texture (牌背)", 2D) = "white" {}
        _BackColor ("Back Tint", Color) = (1,1,1,1)
        _BackTexBlend ("Back Texture Blend", Range(0, 1)) = 0
        _BackTexExtendEdge ("Back Tex Extend Edge", Range(0, 1)) = 0
        _BackTilingOffset ("Back Tiling & Offset", Vector) = (1,1,0,0)
        _BackEdgeColor ("Back Edge Color (背面侧边)", Color) = (0.218, 0.372, 0.66, 1)

        _SideTex ("Side Texture (侧面)", 2D) = "white" {}
        _SideColor ("Side Tint", Color) = (0.9019608,0.9019608,0.8941176,1)
        _SideTilingOffset ("Side Tiling & Offset", Vector) = (1,1,0,0)
        _FrontEdgeColor ("Front Edge Color (正面边缘)", Color) = (1,1,1,1)
        _FrontTexExtendEdge ("Front Tex Extend Edge", Range(0, 1)) = 0

        _GrayScale ("Gray Scale", Range(0, 1)) = 0.0
        _FrontRotation ("Front Rotation (度)", Range(0, 360)) = 0.0
        [HideInInspector] _TileInstanceParams ("Tile Instance Params", Vector) = (0,0,0,0)

        // Matte cel shading: two albedo-multiplied tones, without additive
        // highlights, reflections, or a separately brightened bevel/face mask.
        _TileShadeTint ("Tile Shadow Tone", Color) = (0.81132,0.81132,0.81132,1)
        _TileLightThreshold ("Tile Light Threshold", Range(-1,1)) = 0.08
        _TileLightTransition ("Tile Tone Transition", Range(0.001,0.2)) = 0.1
        // Direction toward the light in tabletop coordinates: +Y up, +Z away from self.
        _TileLightDirection ("Tile Light Direction (table space)", Vector) = (0,0.89377,0.44853,0)
        _TileLightDirectionBlend ("Tile Light Direction Blend", Range(0,1)) = 1
        [HideInInspector] _TileLightUseTableFrame ("Use Table Light Frame", Float) = 1
        _TileWhiteCompression ("White Softening (paper white)", Range(0,0.4)) = 0.1
    }

    SubShader {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" }
        Pass {
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _FrontRotation;
            float _TableFaceImageScale;
            float _FrontTexContain;
        CBUFFER_END
        TEXTURE2D(_FrontTex); SAMPLER(sampler_FrontTex);
        UNITY_INSTANCING_BUFFER_START(TilePerInstance)
            UNITY_DEFINE_INSTANCED_PROP(float4, _FrontTilingOffset)
            UNITY_DEFINE_INSTANCED_PROP(float4, _FrontColor)
            UNITY_DEFINE_INSTANCED_PROP(float4, _TileInstanceParams)
        UNITY_INSTANCING_BUFFER_END(TilePerInstance)
        struct Attributes {
            float4 positionOS:POSITION; float3 normalOS:NORMAL;
            float2 uv:TEXCOORD0; half4 color:COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings {
            float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0;
            float3 normalWS:TEXCOORD1; float3 positionWS:TEXCOORD2;
            float3 normalOS:TEXCOORD3; float3 positionOS:TEXCOORD4;
            half4 color:COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings Vert(Attributes input) {
            Varyings o=(Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input); UNITY_TRANSFER_INSTANCE_ID(input,o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionWS=TransformObjectToWorld(input.positionOS.xyz);
            o.positionCS=TransformWorldToHClip(o.positionWS);
            o.normalWS=TransformObjectToWorldNormal(input.normalOS);
            o.normalOS=input.normalOS; o.positionOS=input.positionOS.xyz;
            o.uv=input.uv; o.color=input.color;
            return o;
        }
        half4 Artwork(Varyings i, out half coverage) {
            float4 mapping=UNITY_ACCESS_INSTANCED_PROP(TilePerInstance,_FrontTilingOffset);
            float4 options=UNITY_ACCESS_INSTANCED_PROP(TilePerInstance,_TileInstanceParams);
            float scale=options.y>0 ? options.y : _TableFaceImageScale;
            float2 canvas=(i.uv-.5)/max(scale,.1)+.5;
            half inside=step(0,canvas.x)*step(canvas.x,1)*step(0,canvas.y)*step(canvas.y,1);
            float2 uv=canvas*mapping.xy+mapping.zw;
            float angle=radians(_FrontRotation), c=cos(angle), s=sin(angle);
            uv=mul(float2x2(c,-s,s,c),uv-.5)+.5;
            half4 art=SAMPLE_TEXTURE2D(_FrontTex,sampler_FrontTex,uv);
            half contained=step(0,uv.x)*step(uv.x,1)*step(0,uv.y)*step(uv.y,1);
            coverage=inside*lerp(1.0h,contained,saturate(_FrontTexContain))*art.a;
            return art;
        }

            half4 Frag(Varyings i):SV_Target {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p=(i.uv-.5)*float2(1,.75188);
                float2 q=abs(p)-float2(.5-.045,.37594-.045);
                float distance=length(max(q,0))+min(max(q.x,q.y),0)-.045;
                half mask=saturate(.5-distance/max(fwidth(distance),.0001));
                half coverage;
                half4 art=Artwork(i,coverage);
                half4 tint=UNITY_ACCESS_INSTANCED_PROP(TilePerInstance,_FrontColor);
                half grayAmount=UNITY_ACCESS_INSTANCED_PROP(TilePerInstance,_TileInstanceParams).x;
                half3 rgb=lerp(half3(.78,.80,.80),art.rgb,coverage)*tint.rgb;
                rgb=lerp(rgb,dot(rgb,half3(.299,.587,.114)).xxx,saturate(grayAmount));
                return half4(rgb,mask*tint.a);
            }
            ENDHLSL
        }
    }
}
