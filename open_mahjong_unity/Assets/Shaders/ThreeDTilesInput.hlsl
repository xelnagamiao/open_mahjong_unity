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
    float4 _TileLightDirection;
    half _TileLightDirectionBlend;
    half _TileLightUseTableFrame;
    half _TileWhiteCompression;
CBUFFER_END

// Game3DManager publishes the table's rotation, independently of each tile's pose.
// A zero/unset matrix falls back to the authored world frame (e.g. asset previews).
float4x4 _TileLightingTableToWorld;

float3 ResolveTileLightDirection(float3 mainDirection)
{
    float mainLength = dot(mainDirection, mainDirection);
    mainDirection = mainLength > 0.000001
        ? mainDirection * rsqrt(max(mainLength, 0.000001)) : float3(0, 1, 0);
    float3 authored = _TileLightDirection.xyz;
    float3 tableDirection = mul((float3x3)_TileLightingTableToWorld, authored);
    if (_TileLightUseTableFrame > 0.5h && dot(tableDirection, tableDirection) > 0.000001)
        authored = tableDirection;
    float authoredLength = dot(authored, authored);
    authored = authoredLength > 0.000001
        ? authored * rsqrt(max(authoredLength, 0.000001)) : mainDirection;
    float3 mixed = lerp(mainDirection, authored, saturate(_TileLightDirectionBlend));
    float mixedLength = dot(mixed, mixed);
    // Opposite directions cancel at blend=.5; never normalize that zero vector.
    return mixedLength > 0.000001
        ? mixed * rsqrt(max(mixedLength, 0.000001)) : authored;
}

#endif
