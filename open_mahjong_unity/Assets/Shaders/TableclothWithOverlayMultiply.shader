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
            "Queue"="Geometry"
        }

        LOD 100
        Cull Back
        ZWrite On

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            // 必須包含這行才能產生 SHADOWS_SCREEN 變體
            #pragma multi_compile_fwdbase_fullshadows
            #pragma multi_compile_fog
            
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 pos : SV_POSITION;           // ← 關鍵！必須叫 pos，否則 TRANSFER_SHADOW 會報錯
                float3 worldNormal : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                SHADOW_COORDS(3)                    // 陰影座標用 3
            };

            sampler2D _TableclothTex;
            sampler2D _OverlayTex;
            float4 _TableclothTex_ST;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);  // ← 這裡賦值給 o.pos
                o.uv = TRANSFORM_TEX(v.uv, _TableclothTex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                
                TRANSFER_SHADOW(o);  // 現在不會報錯
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 tablecloth = tex2D(_TableclothTex, i.uv);
                fixed4 overlay    = tex2D(_OverlayTex, i.uv);

                // 經典乘法疊加
                fixed3 albedo = tablecloth.rgb * overlay.rgb;

                // 光照計算（Lambert + 環境光）
                fixed3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                fixed3 normal = normalize(i.worldNormal);
                fixed NdotL = max(0, dot(normal, lightDir));

                fixed3 diffuse = NdotL * _LightColor0.rgb;
                fixed3 ambient = UNITY_LIGHTMODEL_AMBIENT.rgb;

                // 陰影衰減（現在能正常工作）
                fixed shadow = SHADOW_ATTENUATION(i);
                
                fixed3 finalRGB = albedo * (ambient + diffuse * shadow);

                UNITY_APPLY_FOG(i.fogCoord, finalRGB);

                return fixed4(finalRGB, 1.0);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}