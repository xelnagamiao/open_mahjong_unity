Shader "UI/Salasasa Unified Icon Shadow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _ShadowColor ("Shadow", Color) = (.15,.22,.31,.4)
        _ShadowOffset ("Shadow Offset", Vector) = (0,-3,0,0)
        _Blur ("Blur Radius", Float) = 1.5
        _OutlineWidth ("Fine Outline", Float) = .35
        _OutlineOpacity ("Outline Opacity", Float) = .24
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; float4 position:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            fixed4 _ShadowColor;
            float4 _ShadowOffset;
            float _Blur, _OutlineWidth, _OutlineOpacity;
            float SampleAlpha(float2 uv) { return (tex2D(_MainTex,uv)+_TextureSampleAdd).a; }
            float SoftAlpha(float2 uv,float2 radius)
            {
                radius*=1.2;
                float a=SampleAlpha(uv)*36;
                a+=(SampleAlpha(uv+float2(radius.x,0))+SampleAlpha(uv-float2(radius.x,0))+SampleAlpha(uv+float2(0,radius.y))+SampleAlpha(uv-float2(0,radius.y)))*30;
                a+=(SampleAlpha(uv+radius)+SampleAlpha(uv-radius)+SampleAlpha(uv+float2(radius.x,-radius.y))+SampleAlpha(uv+float2(-radius.x,radius.y)))*25;
                return a/256;
            }
            v2f vert(appdata v)
            {
                v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.position=v.vertex;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color*_Color;return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                float2 pixel=abs(ddx(i.uv))+abs(ddy(i.uv));
                float baseAlpha=SampleAlpha(i.uv);
                float2 edge=pixel*_OutlineWidth;
                float ring=max(max(SampleAlpha(i.uv+float2(edge.x,0)),SampleAlpha(i.uv-float2(edge.x,0))),max(SampleAlpha(i.uv+float2(0,edge.y)),SampleAlpha(i.uv-float2(0,edge.y))));
                ring=saturate(ring-baseAlpha)*_OutlineOpacity;
                float shade=SoftAlpha(i.uv-pixel*_ShadowOffset.xy,pixel*_Blur)*_ShadowColor.a;
                fixed4 c=fixed4(_ShadowColor.rgb,(shade+ring*(1-shade))*i.color.a);
                #ifdef UNITY_UI_CLIP_RECT
                c.a*=UnityGet2DClipping(i.position.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(c.a-.001);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
