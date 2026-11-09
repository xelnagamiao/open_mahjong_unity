Shader "Mahjong/DoraSheenUI" {
    Properties {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "DoraSheenCommon.cginc"
            struct AppData { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            struct Varyings { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; float4 local:TEXCOORD1; float2 face:TEXCOORD2; };
            sampler2D _MainTex;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4x4 _WorldToFace;
            Varyings vert(AppData v) {
                Varyings o;
                o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color; o.local=v.vertex;
                o.face=mul(_WorldToFace,mul(unity_ObjectToWorld,v.vertex)).xy;
                return o;
            }
            fixed4 frag(Varyings i):SV_Target {
                fixed4 col=(tex2D(_MainTex,i.uv)+_TextureSampleAdd)*i.color;
                float amount=DoraStripe(i.face,_Time.y)*.654*max(i.color.r,max(i.color.g,i.color.b));
                col.rgb=DoraBrighten(col.rgb,amount);
                #ifdef UNITY_UI_CLIP_RECT
                col.a*=UnityGet2DClipping(i.local.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a-.001);
                #endif
                return col;
            }
            ENDCG
        }
    }
}
