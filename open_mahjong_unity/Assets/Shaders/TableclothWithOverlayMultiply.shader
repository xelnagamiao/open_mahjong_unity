// 着色器：将覆盖纹理叠加在桌布上方，使替换桌布后正确显示边框阴影并且保留线条。
Shader "Custom/TableclothWithOverlayMultiply"
{
    Properties
    {
        [MainTexture]
        _TableclothTex ("桌布纹理", 2D) = "white" {}
        _OverlayTex ("覆盖纹理（带透明通道）", 2D) = "white" {}
    }

    SubShader
    {
        Tags 
        { 
            "RenderType"="Transparent" 
            "Queue"="Transparent" 
            "IgnoreProjector"="True"
        }

        LOD 100
        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

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
            float4 _OverlayTex_ST;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _TableclothTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 采样桌布纹理
                fixed4 tablecloth = tex2D(_TableclothTex, i.uv);
                
                // 采样覆盖纹理（带透明通道）
                fixed4 overlay = tex2D(_OverlayTex, i.uv);
                
                // Multiply混合：桌布RGB * 覆盖纹理RGB
                fixed4 finalColor;
                finalColor.rgb = tablecloth.rgb * overlay.rgb;
                // 使用覆盖纹理的Alpha通道
                finalColor.a = overlay.a;
                
                return finalColor;
            }
            ENDCG
        }
    }
}
