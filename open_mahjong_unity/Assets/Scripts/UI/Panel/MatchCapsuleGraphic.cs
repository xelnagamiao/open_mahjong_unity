using UnityEngine;
using UnityEngine.UI;

[AddComponentMenu("UI/Match Capsule Graphic")]
[ExecuteAlways]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class MatchCapsuleGraphic : MaskableGraphic {
    [SerializeField] private Color outlineColor = new Color(1f,.57f,.16f);
    [SerializeField, Min(0)] private float outlineWidth = 2.5f;
    protected override void OnPopulateMesh(VertexHelper mesh) {
        mesh.Clear();
        var r=GetPixelAdjustedRect();
        if(r.width<=0||r.height<=0)return;
        float radius=Mathf.Min(r.width,r.height)*.5f;
        float border=Mathf.Min(outlineWidth,radius);
        const int count=66;
        mesh.AddVert(r.center,color,Vector2.zero);
        for(int i=0;i<count;i++){
            bool right=i<33;
            float angle=(right?-90f:90f)+(right?i:i-33)*180f/32;
            var direction=new Vector2(Mathf.Cos(angle*Mathf.Deg2Rad),Mathf.Sin(angle*Mathf.Deg2Rad));
            var center=new Vector2(right?r.xMax-radius:r.xMin+radius,r.center.y);
            mesh.AddVert(center+direction*radius,outlineColor,Vector2.zero);
            mesh.AddVert(center+direction*(radius-border),color,Vector2.zero);
            mesh.AddVert(center+direction*(radius-border),outlineColor,Vector2.zero);
        }
        for(int i=0;i<count;i++){
            int a=1+i*3,b=1+((i+1)%count)*3;
            mesh.AddTriangle(0,a+1,b+1);
            mesh.AddTriangle(a,a+2,b+2);mesh.AddTriangle(a,b+2,b);
        }
    }
}
