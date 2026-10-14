Shader "Custom/WashizuGlassTile"
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
        [HideInInspector] _InkPass ("Opaque paint layer", Float) = 0
        [HideInInspector] _Cull ("Cull", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 0

        _GlassBodyColor ("Glass Body Color", Color) = (0.30,0.43,0.56,1)
        _GlassPearlColor ("Glass Front Surface", Color) = (0.75,0.83,0.90,1)
        _GlassDensity ("Glass Optical Density", Range(0,1)) = 0.18
        _GlassFaceFrost ("Glass Front Surface Reflection", Range(0,1)) = 0.16
        _GlassFacetContrast ("Glass Facet Contrast", Range(0,0.5)) = 0.30

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
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass {
            Name "Glass"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            ZWrite [_ZWrite]
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _FrontRotation;
            float _TableFaceImageScale;
            float _FrontTexContain;
            float _InkPass;
            half4 _GlassBodyColor;
            half4 _GlassPearlColor;
            half _GlassDensity;
            half _GlassFaceFrost;
            half _GlassFacetContrast;
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
            // Turn within this tile's canvas, not around the whole sprite atlas.
            float2 faceUV=lerp(i.uv,1-i.uv,saturate(options.z));
            float2 canvas=(faceUV-.5)/max(scale,.1)+.5;
            half inside=step(0,canvas.x)*step(canvas.x,1)*step(0,canvas.y)*step(canvas.y,1);
            float2 uv=canvas*mapping.xy+mapping.zw;
            float angle=radians(_FrontRotation), c=cos(angle), s=sin(angle);
            uv=mul(float2x2(c,-s,s,c),uv-.5)+.5;
            half4 art=SAMPLE_TEXTURE2D(_FrontTex,sampler_FrontTex,uv);
            half contained=step(0,uv.x)*step(uv.x,1)*step(0,uv.y)*step(uv.y,1);
            coverage=inside*lerp(1.0h,contained,saturate(_FrontTexContain))*art.a;
            return art;
        }

            half4 Frag(Varyings i, FRONT_FACE_TYPE facing:FRONT_FACE_SEMANTIC):SV_Target {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half coverage;
                half4 art=Artwork(i,coverage);
                half4 tint=UNITY_ACCESS_INSTANCED_PROP(TilePerInstance,_FrontColor);
                half grayAmount=UNITY_ACCESS_INSTANCED_PROP(TilePerInstance,_TileInstanceParams).x;
                half printed=saturate(i.color.r);
                half paper=min(art.r,min(art.g,art.b));
                half ink=(1-smoothstep(.58h,.93h,paper))*coverage*printed;
                // Paint is a depth-writing surface drawn before the outer glass.
                // Both sides of the paint remain opaque, including inverted kongs.
                if (_InkPass > .5) {
                    clip(ink-.02h);
                    half3 paint=art.rgb*tint.rgb;
                    paint=lerp(paint,dot(paint,half3(.299,.587,.114)).xxx,saturate(grayAmount));
                    return half4(paint,ink*tint.a);
                }
                float3 n=normalize(i.normalWS), no=normalize(i.normalOS);
                float3 v=SafeNormalize(GetWorldSpaceViewDir(i.positionWS));
                half exterior=IS_FRONT_VFACE(facing,1.0h,0.0h);
                // Retain the far printed plane when looking through a standing
                // tile. Interior side/back faces would add a second fog layer.
                clip(exterior+printed-.001h);
                half grazing=pow(1-saturate(abs(dot(n,v))),4);
                // Bevels are identified in object space. World-space axes made the
                // old shader turn an entire rotated face blue instead of its edges.
                half bevel=smoothstep(.002h,.065h,1-max(abs(no.x),max(abs(no.y),abs(no.z))));
                float3 reflection=reflect(-v,n);
                half ceiling=pow(saturate(reflection.y*.5+.5),6);
                half strip=exp2(-32*pow(dot(reflection,normalize(float3(-.65,.72,.24)))-.72,2));
                half highlight=saturate(.22h+ceiling*.65h+strip*.42h);
                half lightSide=saturate(dot(n,normalize(float3(-.55,.85,-.25)))*.5h+.5h);
                half rim=saturate(bevel*(.18h+highlight*.52h+lightSide*.30h)+grazing*.18h);
                // Soft reflections follow each physical face and extend inward
                // beyond the bevel, independently of the glass body below.
                float3 local=i.positionOS/float3(.0100,.0133,.0065);
                float3 axis=abs(no);
                float2 face=axis.z>max(axis.x,axis.y) ? local.xy : (axis.y>axis.x ? local.xz : local.zy);
                float2 fromEdge=max(1-abs(face),0);
                half edgeBand=exp2(-min(fromEdge.x,fromEdge.y)*21);
                half broadRim=exp2(-min(fromEdge.x,fromEdge.y)*7);
                half sweep=exp2(-32*pow(face.x*.65+face.y*.34-.40,2));
                half directional=saturate(.38+dot(n,normalize(float3(-.55,.85,-.25)))*.55);
                // A closed piece of glass has visible volume across its broad
                // faces, not only along its bevel. Approximate the optical path
                // through the tile box so oblique views and thick side faces
                // carry more body color than a face-on view.
                float3 viewOS=SafeNormalize(TransformWorldToObjectDir(v));
                float3 toExit=(float3(.0100,.0133,.0065)+sign(viewOS)*i.positionOS)
                    /max(abs(viewOS),.0001);
                half thickness=clamp(min(toExit.x,min(toExit.y,toExit.z))/.013, .15, 2.8);
                half bodyAlpha=(1-exp2(-_GlassDensity*thickness))*exterior;
                half broadFace=smoothstep(.80h,.98h,abs(no.z));
                half faceHeight=saturate(local.y*.5+.5);
                half facetLight=saturate(n.y*.5h+.5h);
                half facetTone=lerp(1-_GlassFacetContrast,1,facetLight);
                half frostAlpha=_GlassFaceFrost*printed*(.72h+faceHeight*.28h);
                half3 bodyRgb=_GlassBodyColor.rgb*facetTone;
                half3 frostRgb=_GlassPearlColor.rgb*(.84h+lightSide*.16h);
                half alpha=1-(1-bodyAlpha)*(1-frostAlpha);
                half3 rgb=(bodyRgb*bodyAlpha*(1-frostAlpha)+frostRgb*frostAlpha)/max(alpha,.0001h);
                // A subdued lower thickness band separates the standing body
                // from the cloth. It follows the tile throughout a reveal.
                half lowerBand=exp2(-max(local.y+1,0)*13)*broadFace*(1-abs(n.y))*.16h*exterior;
                rgb=lerp(rgb,_GlassBodyColor.rgb*.52h,lowerBand);
                alpha=1-(1-alpha)*(1-lowerBand);
                half reflectionAlpha=saturate(rim*.60h+edgeBand*.22h+broadRim*.12h+sweep*.13h*directional)*lerp(.12h,1.0h,exterior);
                half3 reflectionRgb=lerp(half3(.50,.59,.68),half3(.94,.97,1.0),saturate(highlight+edgeBand*.45));
                rgb=(rgb*alpha*(1-reflectionAlpha)+reflectionRgb*reflectionAlpha)/max(alpha*(1-reflectionAlpha)+reflectionAlpha,.0001h);
                alpha=1-(1-alpha)*(1-reflectionAlpha);
                // The faint face surface is visible from either side at the
                // same physical depth as the opaque ink, never on the back shell.
                alpha*=1-ink;
                // Face-on paint lies on the outer surface; glass behind it fails
                // the depth test. Viewed from the back, the glass covers the paint.
                half combined=alpha;
                // A small, filtered refraction contribution softens cloth seen
                // through the glass. Most transmission still uses alpha blending,
                // preserving other transparent tiles behind this one. The current
                // game camera already requests its URP opaque texture; no global
                // pipeline setting or per-frame capture is added here.
                if (_CameraOpaqueTexture_TexelSize.z > 16) {
                    float2 screen=GetNormalizedScreenSpaceUV(i.positionCS);
                    float3 viewNormal=mul((float3x3)UNITY_MATRIX_V,n);
                    float2 pixel=_CameraOpaqueTexture_TexelSize.xy;
                    float2 refractedUV=screen+viewNormal.xy*pixel*(1.3+bevel*2.2);
                    half3 transmitted=SampleSceneColor(refractedUV)*.4h;
                    transmitted+=SampleSceneColor(refractedUV+pixel*float2(1.2,0))*.15h;
                    transmitted+=SampleSceneColor(refractedUV-pixel*float2(1.2,0))*.15h;
                    transmitted+=SampleSceneColor(refractedUV+pixel*float2(0,1.2))*.15h;
                    transmitted+=SampleSceneColor(refractedUV-pixel*float2(0,1.2))*.15h;
                    // Paint lies on the surface; refraction acts only on clear glass.
                    half transmission=(1-combined)*.10h*exterior*(1-ink);
                    rgb=(rgb*combined+transmitted*transmission)/max(combined+transmission,.0001h);
                    combined+=transmission;
                }
                rgb*=tint.rgb;
                rgb=lerp(rgb,dot(rgb,half3(.299,.587,.114)).xxx,saturate(grayAmount));
                return half4(rgb,combined*tint.a);
            }
            ENDHLSL
        }
    }
}
