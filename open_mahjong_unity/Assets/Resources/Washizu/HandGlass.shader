Shader "UI/WashizuHandGlass" {
    Properties {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass {
            Name "GlassBody"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float4 worldPosition:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            float4 _Color, _ClipRect;
            v2f vert(appdata v) {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition=v.vertex; o.vertex=UnityObjectToClipPos(v.vertex);
                o.uv=v.uv; o.color=v.color*_Color; return o;
            }
            float rounded(float2 p,float2 halfSize,float radius) {
                float2 q=abs(p)-halfSize+radius;
                return length(max(q,0))+min(max(q.x,q.y),0)-radius;
            }
            fixed4 frag(v2f i):SV_Target {
                float2 uv=i.uv;
                // Height is the unit: rounded full silhouette plus the slightly
                // inset front plate expose a dark transparent top and bottom bevel.
                float2 p=(uv-.5)*float2(.64,1);
                float d=rounded(p,float2(.319,.499),.046);
                float aa=max(fwidth(d),.0004);
                float silhouette=saturate(.5-(d+.0015)/(aa*1.8));
                float fd=rounded(p-float2(0,-.052),float2(.316,.447),.035);
                float front=saturate(.5-fd/aa);
                float topEdge=exp(-abs(d+.007)*155);
                float frontRim=exp(-abs(fd+.004)*155)*front;
                float topGlow=pow(saturate((uv.y-.86)/.14),4);
                float sideGlow=pow(saturate(abs(p.x)/.32),12);
                float foot=exp(-pow((uv.y-.027)/.012,2));
                float diagonal=exp(-pow((uv.x+uv.y*.8-.86)/.18,2));
                float3 frontColor=float3(.55,.62,.74) + (uv.y-.60)*.12;
                frontColor -= exp(-pow((uv.y-.053)/.040,2))*float3(.20,.20,.225);
                float3 rgb=lerp(float3(.08,.15,.31),frontColor,front);
                float alpha=lerp(.16,.57,front);
                float capY=saturate((uv.y-.88)/.12);
                float capReflection=exp(-pow((uv.x+uv.y*.20-.29)/.16,2))*.78*(.72+.28*capY)
                    +exp(-pow((uv.x-.97)/.20,2))*.34*(.85+.15*capY);
                float reflection=topEdge*(.14+topGlow*.38+sideGlow*.12)
                    +frontRim*(.24+uv.y*.18)+foot*.08+capReflection*(1-front)
                    +exp(-pow((uv.y-.878)/.009,2))*.40*front;
                rgb=lerp(rgb,float3(.92,.96,1),saturate(reflection));
                alpha=saturate(alpha+reflection*.27);
                fixed4 result=fixed4(rgb*i.color.rgb,alpha*silhouette*i.color.a);
                #ifdef UNITY_UI_CLIP_RECT
                    result.a*=UnityGet2DClipping(i.worldPosition.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                    clip(result.a-.001);
                #endif
                return result;
            }
            ENDCG
        }
    }
}
