Shader "Hidden/Hongque/ColourlessHand" {
    Properties { _MainTex ("Artwork", 2D) = "white" {} }
    SubShader {
        Cull Off ZWrite Off ZTest Always
        Pass {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _CanvasSize;

            fixed4 frag(v2f_img input) : SV_Target {
                float2 border = 10.0 / _CanvasSize.xy;
                if (any(input.uv < border) || any(input.uv > 1.0 - border))
                    return fixed4(0, 0, 0, 1);
                float2 glyph = (input.uv - 0.5) * _CanvasSize.xy / (_CanvasSize.y * 0.8) + 0.5;
                if (any(glyph < 0) || any(glyph > 1)) return fixed4(1, 1, 1, 1);
                float2 uv = (11.5 + glyph * 57.0) / 80.0;
                return tex2D(_MainTex, uv);
            }
            ENDCG
        }
    }
}
