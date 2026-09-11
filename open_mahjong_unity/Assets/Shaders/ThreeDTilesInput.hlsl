#ifndef THREE_D_TILES_INPUT_INCLUDED
#define THREE_D_TILES_INPUT_INCLUDED

// 所有 Pass 必须使用同一份 UnityPerMaterial，否则 SRP Batcher 会失效。
CBUFFER_START(UnityPerMaterial)
    float4 _FrontTex_ST;
    float4 _FrontBgTex_ST;
    float4 _BackTex_ST;
    float4 _SideTex_ST;
    float4 _SideTilingOffset;
    half _BackTexBlend;
    half _BackTexExtendEdge;
    half _FrontTexExtendEdge;
    half _FrontRotation;
    half _FrontBgBlend;
    half4 _FrontBgColor;
    half _FrontBgTexAspect;
    float _TableFaceAspect;
    float _TableFaceImageScale;
    half _FrontTexContain;
    half _TableBgCoverFace;
    half4 _TableFaceColor;
    half _TableFaceBlend;
    half4 _TableFaceFallbackColor;
    half _TableFaceFallbackEnabled;
    half4 _TileShadeTint;
    half _TileLightThreshold;
    half _TileLightTransition;
    half _TileShadowStrength;
    half _TileWhiteCompression;
CBUFFER_END

#endif
