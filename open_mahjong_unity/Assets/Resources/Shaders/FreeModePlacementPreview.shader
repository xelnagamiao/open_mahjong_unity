Shader "Mahjong/FreeModePlacementPreview" {
    Properties {
        _Color ("Color", Color) = (1, 0.72, 0.22, 1)
        _RegionSize ("Region size", Vector) = (1, 1, 0, 0)
        _BorderWidth ("Border width in world units", Float) = 0.45
    }
    SubShader {
        // Draw with scene geometry before transparent/world-space UI and screen-space canvases.
        Tags { "Queue"="AlphaTest+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off
        Offset -1, -1
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            fixed4 _Color;
            float4 _RegionSize;
            float _BorderWidth;
            v2f vert(appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target {
                float2 edge = min(i.uv, 1.0 - i.uv) * _RegionSize.xy;
                float edgeDistance = min(edge.x, edge.y);
                float feather = max(fwidth(edgeDistance), 0.001);
                float border = 1.0 - smoothstep(_BorderWidth - feather, _BorderWidth + feather, edgeDistance);
                clip(border - 0.001);
                return fixed4(_Color.rgb, _Color.a * border);
            }
            ENDCG
        }
    }
}
