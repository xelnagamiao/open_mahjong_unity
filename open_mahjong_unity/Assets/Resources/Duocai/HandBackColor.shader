Shader "Hidden/Duocai/HandBackColor" {
    Properties { _MainTex("Body",2D)="white" {} _BackMask("Back Mask",2D)="white" {} _BackColor("Back",Color)=(1,0,0,1) _Rect("Rect",Vector)=(0,0,1,1) }
    SubShader { Pass {
        ZTest Always Cull Off ZWrite Off
        CGPROGRAM
        #pragma vertex vert_img
        #pragma fragment frag
        #include "UnityCG.cginc"
        sampler2D _MainTex, _BackMask; float4 _BackColor, _Rect;
        fixed4 frag(v2f_img input):SV_Target {
            fixed4 color=tex2D(_MainTex,input.uv*_Rect.zw+_Rect.xy);
            fixed4 region=tex2D(_BackMask,input.uv);
            float high=max(region.r,max(region.g,region.b));
            float low=min(region.r,min(region.g,region.b));
            float mask=smoothstep(.15,.4,(high-low)/max(high,.001))*region.a;
            #ifdef UNITY_COLORSPACE_GAMMA
                float shade=.82+.18*saturate(high/.42);
            #else
                float shade=.82+.18*saturate(high/.15);
            #endif
            return fixed4(lerp(color.rgb,_BackColor.rgb*shade,mask),color.a);
        }
        ENDCG
    } }
}
