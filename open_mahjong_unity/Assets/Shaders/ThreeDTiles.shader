Shader "Custom/ThreeDTiles"
{
    Properties
    {
        _FrontTex ("Front Texture (牌面)", 2D) = "white" {}
        _FrontColor ("Front Tint", Color) = (1,1,1,1)
        _FrontTilingOffset ("Front Tiling & Offset", Vector) = (1,1,0,0)

        _BackTex ("Back Texture (牌背)", 2D) = "white" {}
        _BackColor ("Back Tint", Color) = (1,1,1,1)
        _BackTilingOffset ("Back Tiling & Offset", Vector) = (1,1,0,0)

        _SideTex ("Side Texture (侧面)", 2D) = "white" {}
        _SideColor ("Side Tint", Color) = (1,1,1,1)
        _SideTilingOffset ("Side Tiling & Offset", Vector) = (1,1,0,0)

        _Alpha ("Alpha", Range(0, 1)) = 1.0
        _GrayScale ("Gray Scale", Range(0, 1)) = 0.0
        _FrontRotation ("Front Rotation (度)", Range(0, 360)) = 0.0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 200

        // 阴影投射 Pass（不变）
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"

            struct v2f { V2F_SHADOW_CASTER; };
            v2f vert(appdata_base v) { v2f o; TRANSFER_SHADOW_CASTER(o); return o; }
            float4 frag(v2f i) : SV_Target { SHADOW_CASTER_FRAGMENT(i); }
            ENDCG
        }

        Pass
        {
            Name "UnlitMain"
            Cull Back
            ZWrite On
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vertMain
            #pragma fragment fragMain
            #pragma target 3.0
            #pragma multi_compile_instancing  // 保持 Instancing 支持

            #include "UnityCG.cginc"

            sampler2D _FrontTex;
            sampler2D _BackTex;
            sampler2D _SideTex;

            fixed4 _FrontColor;
            fixed4 _BackColor;
            fixed4 _SideColor;

            float4 _FrontTilingOffset;
            float4 _BackTilingOffset;
            float4 _SideTilingOffset;

            half _Alpha;
            half _GrayScale;
            half _FrontRotation;

            // 只保留必要的 Instancing 宏（不重复定义 _FrontTilingOffset）
            // 如果你以后有其他 per-instance 属性，再加 Buffer

            struct appdata_main
            {
                float4 vertex : POSITION;
                float2 uv_FrontTex : TEXCOORD0;
                float2 uv2_BackTex : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f_main
            {
                float4 pos : SV_POSITION;
                float2 uvFront : TEXCOORD0;
                float2 uvBack : TEXCOORD1;
                float2 uvSide : TEXCOORD2;
                float4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float2 RotateUV(float2 uv, float angle)
            {
                float rad = angle * 3.14159265359 / 180.0;
                float cosAngle = cos(rad);
                float sinAngle = sin(rad);
                float2 centeredUV = uv - 0.5;
                float2 rotatedUV;
                rotatedUV.x = centeredUV.x * cosAngle - centeredUV.y * sinAngle;
                rotatedUV.y = centeredUV.x * sinAngle + centeredUV.y * cosAngle;
                return rotatedUV + 0.5;
            }

            v2f_main vertMain (appdata_main v)
            {
                v2f_main o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);
                o.uvFront = v.uv_FrontTex;
                o.uvBack = v.uv2_BackTex;
                o.uvSide = v.uv2;
                o.color = v.color;
                return o;
            }

            fixed4 fragMain (v2f_main i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float2 frontUV = i.uvFront * _FrontTilingOffset.xy + _FrontTilingOffset.zw;
                
                if (_FrontRotation != 0.0)
                {
                    frontUV = RotateUV(frontUV, _FrontRotation);
                }
                
                fixed4 front = tex2D(_FrontTex, frontUV) * _FrontColor;

                float2 backUV = i.uvBack * _BackTilingOffset.xy + _BackTilingOffset.zw;
                fixed4 back = tex2D(_BackTex, backUV) * _BackColor;

                float2 sideUV = i.uvSide * _SideTilingOffset.xy + _SideTilingOffset.zw;
                fixed4 side = tex2D(_SideTex, sideUV) * _SideColor;

                fixed4 col = front * i.color.r + back * i.color.g + side * i.color.b;

                if (_GrayScale > 0)
                {
                    float gray = dot(col.rgb, float3(0.299, 0.587, 0.114));
                    col.rgb = lerp(col.rgb, float3(gray, gray, gray), _GrayScale);
                }

                col.a *= _Alpha;
                return col;
            }
            ENDCG
        }
    }
}