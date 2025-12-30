Shader "Custom/MultiplyBlend3D"
{
    Properties
    {
        _MainTex ("Base Texture (图片B)", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _BlendTex ("Blend Texture (图片A)", 2D) = "white" {}
    }

    SubShader
    {
        Tags 
        { 
            "RenderType"="Transparent" 
            "Queue"="Transparent"
        }
        
        LOD 200
        
        Cull Off
        Lighting Off
        ZWrite On
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
                float4 color : COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float2 blendUV : TEXCOORD1;
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            sampler2D _BlendTex;
            float4 _MainTex_ST;
            float4 _BlendTex_ST;
            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.blendUV = TRANSFORM_TEX(v.uv, _BlendTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 采样主纹理（图片B）
                fixed4 mainColor = tex2D(_MainTex, i.uv) * i.color;
                
                // 采样混合纹理（图片A）
                fixed4 blendColor = tex2D(_BlendTex, i.blendUV);
                
                // Multiply 混合：主纹理RGB * 混合纹理RGB
                // 保留混合纹理的Alpha通道
                mainColor.rgb = mainColor.rgb * blendColor.rgb;
                mainColor.a *= blendColor.a;
                
                return mainColor;
            }
            ENDCG
        }
    }
    
    FallBack "Diffuse"
}

