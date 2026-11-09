Shader "Mahjong/DoraSheen" {
    Properties {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [HideInInspector] _ZTest ("Depth test", Float) = 4
        [HideInInspector] _Strength ("White stripe strength", Float) = 1.635
    }
    SubShader {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off ZWrite Off ZTest [_ZTest]
        Blend One One
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
            struct Varyings { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; float4 local:TEXCOORD1; float4 screen:TEXCOORD2; };
            float4 _ClipRect;
            float _Strength;
            sampler2D _CameraOpaqueTexture;
            float4 _CameraOpaqueTexture_TexelSize;
            Varyings vert(AppData v) {
                Varyings o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color; o.local=v.vertex;
                o.screen=ComputeScreenPos(o.vertex); return o;
            }
            fixed4 frag(Varyings i):SV_Target {
                float amount=DoraStripe(i.uv,_Time.y)*_Strength*i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                amount*=UnityGet2DClipping(i.local.xy,_ClipRect);
                #endif
                clip(amount-.001);
                // The table camera already supplies the opaque color for glass
                // tiles. Reuse it to add the same display-space light on 3D ink.
                float3 underneath=tex2D(_CameraOpaqueTexture,i.screen.xy/i.screen.w).rgb;
                float3 addition=DoraBrighten(underneath,amount)-underneath;
                if (_CameraOpaqueTexture_TexelSize.z < 16)
                    addition=DoraFromDisplay(saturate(amount)).xxx;
                return fixed4(max(addition,0),0);
            }
            ENDCG
        }
    }
}
