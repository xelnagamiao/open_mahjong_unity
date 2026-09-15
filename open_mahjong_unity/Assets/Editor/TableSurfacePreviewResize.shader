Shader "Hidden/Mahjong/TableSurfacePreviewResize"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _PreviewTone ("Display RGB tone", Vector) = (1, 1, 1, 1)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _PreviewTone;
        float4 Premultiply(v2f_img input) : SV_Target
        {
            float4 color = tex2D(_MainTex, input.uv);
            // Source bytes are already display encoded. Tint once, before all downsampling.
            return float4(color.rgb * _PreviewTone.rgb * color.a, color.a);
        }
        float4 CopyPremultiplied(v2f_img input) : SV_Target
        {
            return tex2D(_MainTex, input.uv);
        }
        float4 Unpremultiply(v2f_img input) : SV_Target
        {
            float4 color = tex2D(_MainTex, input.uv);
            return float4(color.a > 0.0 ? color.rgb / color.a : float3(0, 0, 0), color.a);
        }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Premultiply
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment CopyPremultiplied
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Unpremultiply
            ENDCG
        }
    }
    Fallback Off
}
