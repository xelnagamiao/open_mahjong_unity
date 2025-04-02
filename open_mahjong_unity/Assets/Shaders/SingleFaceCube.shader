Shader "Custom/SingleFaceCube"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 2
        _FaceIndex ("Face Index (0-5)", Range(0, 5)) = 0
        _RotationAngle ("Rotation Angle", Range(0, 360)) = 0
        _Scale ("Scale", Range(0.1, 5)) = 1
        _OffsetX ("Offset X", Range(-1, 1)) = 0
        _OffsetY ("Offset Y", Range(-1, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        Cull [_Cull]

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
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 worldNormal : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _FaceIndex;
            float _RotationAngle;
            float _Scale;
            float _OffsetX;
            float _OffsetY;

            // UV偏移函数
            float2 offsetUV(float2 uv, float2 offset)
            {
                return uv + offset;
            }

            // UV缩放函数
            float2 scaleUV(float2 uv, float scale)
            {
                float2 center = float2(0.5, 0.5);
                return center + (uv - center) / scale;
            }

            // UV旋转函数
            float2 rotateUV(float2 uv, float angle)
            {
                float2 pivot = float2(0.5, 0.5);
                float cosAngle = cos(angle);
                float sinAngle = sin(angle);
                float2 offset = uv - pivot;
                
                float2 rotated;
                rotated.x = offset.x * cosAngle - offset.y * sinAngle;
                rotated.y = offset.x * sinAngle + offset.y * cosAngle;
                
                return rotated + pivot;
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                
                // 应用纹理的基础缩放和偏移
                float2 uv = TRANSFORM_TEX(v.uv, _MainTex);
                
                // 应用自定义偏移
                uv = offsetUV(uv, float2(_OffsetX, _OffsetY));
                
                // 应用自定义缩放
                uv = scaleUV(uv, _Scale);
                
                // 应用旋转
                float angleInRad = _RotationAngle * UNITY_PI / 180.0;
                o.uv = rotateUV(uv, angleInRad);
                
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 定义六个面的法线方向
                float3 normals[6] = {
                    float3(1, 0, 0),   // 右面 (0)
                    float3(-1, 0, 0),  // 左面 (1)
                    float3(0, 1, 0),   // 上面 (2)
                    float3(0, -1, 0),  // 下面 (3)
                    float3(0, 0, 1),   // 前面 (4)
                    float3(0, 0, -1)   // 后面 (5)
                };

                // 计算当前片段的法线与目标面的法线的点积
                float dotProduct = dot(normalize(i.worldNormal), normals[_FaceIndex]);
                
                // 如果不是目标面，则丢弃该片段
                if (dotProduct < 0.7) // 使用0.7作为阈值，可以根据需要调整
                {
                    discard;
                }

                // 采样纹理并应用颜色
                fixed4 col = tex2D(_MainTex, i.uv) * _Color;
                return col;
            }
            ENDCG
        }
    }
}