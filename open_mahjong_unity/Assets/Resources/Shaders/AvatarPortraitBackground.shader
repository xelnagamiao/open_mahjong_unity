Shader "Mahjong/UI/Avatar Portrait Background" {
    Properties {
        [PerRendererData] _MainTex ("Portrait", 2D) = "white" {}
        _BackgroundColor ("Background", Color) = (0.1882353,0.1882353,0.1882353,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            struct Input { float4 vertex:POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; };
            struct Varyings { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; half4 mask:TEXCOORD1; };
            sampler2D _MainTex;
            fixed4 _BackgroundColor, _TextureSampleAdd;
            float4 _ClipRect;
            float _UIMaskSoftnessX, _UIMaskSoftnessY;
            Varyings vert(Input v) {
                Varyings o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                float2 pixelSize = o.vertex.w / abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 bounds = clamp(_ClipRect, -2e10, 2e10);
                o.mask = half4(v.vertex.xy * 2 - bounds.xy - bounds.zw,
                    0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize)));
                return o;
            }
            fixed4 frag(Varyings i):SV_Target {
                fixed4 portrait = tex2D(_MainTex,i.uv) + _TextureSampleAdd;
                fixed3 rgb = lerp(_BackgroundColor.rgb, portrait.rgb, portrait.a) * i.color.rgb;
                half alpha = i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                half2 mask = saturate((_ClipRect.zw - _ClipRect.xy - abs(i.mask.xy)) * i.mask.zw);
                alpha *= mask.x * mask.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - 0.001);
                #endif
                return fixed4(rgb * alpha, alpha);
            }
            ENDCG
        }
    }
}
