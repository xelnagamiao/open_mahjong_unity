Shader "Custom/TableclothWithOverlay"
{
    Properties
    {
        [MainTexture] _TableclothTex ("桌布纹理", 2D) = "white" {}
        _OverlayTex ("覆盖纹理（带透明通道）", 2D) = "white" {}
    }

    SubShader
    {
        Tags 
        { 
            "RenderType"="Opaque" 
            "Queue"="Geometry"      // ← 关键：改成 Geometry（不透明队列）
        }

        LOD 100
        Cull Back
        ZWrite On                   // ← 关键：开启深度写入

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _TableclothTex;
            sampler2D _OverlayTex;
            float4 _TableclothTex_ST;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _TableclothTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 tablecloth = tex2D(_TableclothTex, i.uv);
                fixed4 overlay    = tex2D(_OverlayTex, i.uv);

                // 方式A：经典乘法叠加（阴影/暗边效果最好）
                fixed3 finalRGB = tablecloth.rgb * overlay.rgb;

                // 方式B：更柔和的叠加（如果想要白色 overlay 不影响）
                // fixed3 finalRGB = lerp(tablecloth.rgb, tablecloth.rgb * overlay.rgb, overlay.a);

                // 最终输出：始终保持不透明
                return fixed4(finalRGB, 1.0);   // ← 关键！alpha 固定为1
            }
            ENDCG
        }
    }
}