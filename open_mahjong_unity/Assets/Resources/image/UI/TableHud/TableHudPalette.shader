Shader "Mahjong/UI/SourceArtworkPalette" {
 Properties {
  [PerRendererData] _MainTex ("Sprite Texture", 2D)="white" {}
  _Color ("Tint", Color)=(1,1,1,1)
  _Low ("Shadow", Color)=(.05,.08,.12,1)
  _High ("Body", Color)=(.2,.3,.4,1)
  _Ink ("Highlight", Color)=(1,1,1,1)
  _StencilComp ("Stencil Comparison", Float)=8
  _Stencil ("Stencil ID", Float)=0
  _StencilOp ("Stencil Operation", Float)=0
  _StencilWriteMask ("Stencil Write Mask", Float)=255
  _StencilReadMask ("Stencil Read Mask", Float)=255
  _ColorMask ("Color Mask", Float)=15
  [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float)=0
 }
 SubShader {
  Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True"}
  Stencil {Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]}
  Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
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
   struct appdata {float4 vertex:POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
   struct v2f {float4 vertex:SV_POSITION;fixed4 color:COLOR;float2 uv:TEXCOORD0;float4 world:TEXCOORD1;UNITY_VERTEX_OUTPUT_STEREO};
   sampler2D _MainTex;fixed4 _Color,_TextureSampleAdd,_Low,_High,_Ink;float4 _ClipRect;
   v2f vert(appdata v){v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.world=v.vertex;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color*_Color;return o;}
   fixed4 frag(v2f i):SV_Target {
    fixed4 src=tex2D(_MainTex,i.uv)+_TextureSampleAdd;
    float3 sampled=src.rgb;
    #ifndef UNITY_COLORSPACE_GAMMA
    sampled=LinearToGammaSpace(sampled);
    #endif
    float white=smoothstep(.36,.70,min(sampled.r,min(sampled.g,sampled.b)));
    fixed3 body=lerp(_Low.rgb,_High.rgb,saturate((sampled.b-.30)/.57));
    fixed4 c=fixed4(lerp(body,_Ink.rgb,white),src.a)*i.color;
    #ifdef UNITY_UI_CLIP_RECT
    c.a*=UnityGet2DClipping(i.world.xy,_ClipRect);
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
