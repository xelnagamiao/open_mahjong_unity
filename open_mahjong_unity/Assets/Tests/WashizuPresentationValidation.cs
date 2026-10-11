#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Visual and pool lifecycle regressions; requires the initialized game scene.</summary>
public static class WashizuPresentationValidation {
    public static object Run() {
        var checks=new List<string>();
        void Check(bool ok,string label) {if(!ok)throw new Exception("Washizu visual: "+label); checks.Add(label);}
        var pool=MahjongObjectPool.Instance;
        Check(pool!=null,"pool available");
        var position=new Vector3(0,-2000,0);
        var opaque=pool.SpawnPresentationTile(11,position,Quaternion.identity);
        var state=opaque.GetComponent<Tile3D>();
        var body=opaque.GetComponentInChildren<MeshFilter>();
        var original=body.sharedMesh;
        var shadow=opaque.GetComponent<TileContactShadow>();
        var outline=opaque.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r=>r.sharedMaterials.Any(m=>m.shader.name=="Hidden/Mahjong/TileOutlineRibbon"));
        bool shadowEnabled=shadow!=null && shadow.enabled;
        bool outlineEnabled=outline!=null && outline.enabled;
        try {
            for(int i=0;i<3;i++) {
                pool.SetPresentationFace(opaque,2011);
                Check(state.GetTileId()==2011,"glass logical identity "+i);
                Check(state.TileMaterial.shader.name=="Custom/WashizuGlassTile","glass material "+i);
                Check(shadow==null || !shadow.enabled,"glass transmits contact region "+i);
                Check(outline==null || !outline.enabled,"no opaque support strokes "+i);
                pool.SetPresentationFace(opaque,11);
                Check(state.GetTileId()==11 && state.TileMaterial.shader.name=="Custom/ThreeDTiles","opaque texture restored "+i);
                Check(shadow==null || shadow.enabled==shadowEnabled,"opaque contact shadow restored "+i);
                Check(outline==null || outline.enabled==outlineEnabled,"opaque contour restored "+i);
                Check(body.sharedMesh==original,"glass never replaces physical tile mesh "+i);
            }
        } finally {pool.Return(-1,opaque);}
        var reading=pool.SpawnWashizuReadingTile(12,position,Quaternion.Euler(90,0,0));
        var readingBody=reading.GetComponentInChildren<MeshFilter>();
        try {
            Check(readingBody.sharedMesh.vertexCount==4 && readingBody.sharedMesh.bounds.size.z<.000001f,"reading aid has zero thickness");
            Check(reading.GetComponent<Tile3D>().TileMaterial.shader.name=="Custom/WashizuReadingFace","paper shader");
            pool.SetPresentationFace(reading,13);
            Check(reading.GetComponent<Tile3D>().TileMaterial.shader.name=="Custom/WashizuReadingFace","paper remains flat after face refresh");
            Check(reading.GetComponent<TileContactShadow>()?.enabled!=true,"paper does not project solid tile shadow");
            Check(reading.GetComponent<WashizuReadingSurface>().IsReadingSurface,"reading lease classified");
        } finally {pool.Return(-1,reading);}
        Check(readingBody.sharedMesh==original,"reading quad restores physical mesh on return");
        Check(!reading.GetComponent<WashizuReadingSurface>().IsReadingSurface,"reading lease cleared");
        var go=new GameObject("Washizu visual UI audit",typeof(RectTransform));
        var rect=(RectTransform)go.transform;rect.sizeDelta=new Vector2(96,150);
        var bgGo=new GameObject("Body",typeof(RectTransform),typeof(Image));bgGo.transform.SetParent(rect,false);
        var artGo=new GameObject("Art",typeof(RectTransform),typeof(Image));artGo.transform.SetParent(rect,false);
        var bg=bgGo.GetComponent<Image>();var art=artGo.GetComponent<Image>();
        var originalArtMaterial=art.material;
        try {
            bg.color=Color.white;art.color=Color.white;
            WashizuTile.ApplyHandVisual(rect,2011,bg,art);
            var glass=go.GetComponentsInChildren<Image>().First(x=>x.name=="WashizuGlassBody");
            Check(!glass.raycastTarget,"glass body never intercepts input");
            Check(glass.transform.GetSiblingIndex()<art.transform.GetSiblingIndex(),"ink above glass body");
            Check(bg.color.a==0 && glass.color==Color.white,"neutral glass replaces opaque body");
            Check(glass.material.shader.name=="UI/WashizuHandGlass","glass UI shader");
            bg.color=new Color(.5f,.5f,.5f,.4f);art.color=bg.color;
            WashizuTile.ApplyHandVisual(rect,2011,bg,art);
            Check(Mathf.Approximately(glass.color.a,.4f) && Mathf.Approximately(glass.color.r,.5f),"disabled tint and opacity retained");
            Check(go.GetComponentsInChildren<Image>().Count(x=>x.name=="WashizuGlassBody")==1,"repeated refresh reuses body");
            bg.color=Color.white;art.color=Color.white;
            WashizuTile.ApplyHandVisual(rect,11,bg,art);
            Check(!glass.gameObject.activeSelf && bg.color==Color.white,"opaque UI reuse restores original body");
            bg.enabled=false;art.color=Color.white;
            WashizuTile.ApplyHandVisual(rect,2011,bg,art);
            Check(art.material.shader.name=="UI/WashizuHandInk","white-backed skin separates ink");
            Check(glass.transform.GetSiblingIndex()<art.transform.GetSiblingIndex(),"complete skin ink stays above glass");
            WashizuTile.ApplyHandVisual(rect,2011,bg,art);
            Check(glass.transform.GetSiblingIndex()<art.transform.GetSiblingIndex(),"repeated complete skin refresh retains layer order");
            WashizuTile.ApplyHandVisual(rect,11,bg,art);
            Check(art.material==originalArtMaterial,"custom face material restored");
            bg.enabled=true;bg.color=Color.white;art.color=Color.white;
            WashizuTile.ApplyHandVisual(rect,2011,bg,art);
            Check(art.material==originalArtMaterial,"layered skin preserves its original artwork material");
        } finally {UnityEngine.Object.Destroy(go);}
        foreach(var path in new[]{"TransparentTile","ReadingFace","HandGlass","HandInk"}) {
            var shader=Resources.Load<Shader>("Washizu/"+path);
            Check(shader!=null && shader.isSupported,"packaged shader supported: "+path);
        }
        var result=new {passed=true,count=checks.Count,checks};
        return result;
    }
}
#endif
