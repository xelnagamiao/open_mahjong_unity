Shader "Hidden/Duocai/InkColor" {
    Properties { _MainTex("Face",2D)="white" {} _Ink("Ink",Color)=(1,0,0,1) _Rect("Rect",Vector)=(0,0,1,1) }
    SubShader { Pass {
        ZTest Always Cull Off ZWrite Off
        CGPROGRAM
        #pragma vertex vert_img
        #pragma fragment frag
        #include "UnityCG.cginc"
        sampler2D _MainTex; float4 _Ink, _Rect;
        fixed4 frag(v2f_img input):SV_Target {
            fixed4 color=tex2D(_MainTex,input.uv*_Rect.zw+_Rect.xy);
            float strength=1-min(color.r,min(color.g,color.b));
            return fixed4(lerp(1,_Ink.rgb,strength),color.a);
        }
        ENDCG
    } }
}
