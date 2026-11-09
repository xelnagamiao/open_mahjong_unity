#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

public static class TileSurfaceValidation {
    public static object Run() {
        var checks = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        foreach (int suit in new[] { 10, 20, 30 }) for (int rank = 1; rank <= 9; rank++) {
            int expected = suit + rank % 9 + 1;
            Check(RiichiDoraHints.Matches(expected, new[] { suit + rank }, false, false), "dora successor " + (suit + rank));
            Check(!RiichiDoraHints.Matches(suit + rank, new[] { suit + rank }, false, false), "indicator is not its own dora " + (suit + rank));
        }
        foreach (var pair in new[] { (41,42),(42,43),(43,44),(44,41),(46,47),(47,45),(45,46) })
            Check(RiichiDoraHints.Matches(pair.Item2,new[]{pair.Item1},false,false), "honor successor " + pair.Item1);
        Check(RiichiDoraHints.Matches(19,new[]{11},true,false) && !RiichiDoraHints.Matches(12,new[]{11},true,false), "sanma 1m skips removed characters");
        Check(RiichiDoraHints.Matches(11,new[]{19},true,false), "sanma 9m wraps to 1m");
        foreach (int red in new[] { 105,205,305 }) {
            Check(RiichiDoraHints.Matches(red,Array.Empty<int>(),false,true), "red dora " + red);
            Check(!RiichiDoraHints.Matches(red,Array.Empty<int>(),false,false), "disabled red dora " + red);
        }
        Check(RiichiDoraHints.Matches(205,new[]{24},false,false), "red physical tile still matches ordinary dora");
        Check(RiichiDoraHints.Matches(2025,new[]{2024},false,false), "transparent physical values normalize");
        Check(!RiichiDoraHints.Matches(1,new[]{1},false,true) && !RiichiDoraHints.Matches(51,new[]{51},false,true), "backs and flowers never glow");

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/GameScene/3DCardNew2026.8.5.prefab");
        var mesh = prefab.GetComponentInChildren<MeshFilter>().sharedMesh;
        var points = mesh.vertices; var uv = mesh.uv; var normals = mesh.normals;
        var cap = Enumerable.Range(0,points.Length).Where(i=>normals[i].z<-.99f).ToArray();
        int left = cap.OrderBy(i=>points[i].x).First(), right = cap.OrderByDescending(i=>points[i].x).First();
        int bottom = cap.OrderBy(i=>points[i].y).First(), top = cap.OrderByDescending(i=>points[i].y).First();
        Check(uv[left].x > .9f && uv[right].x < .1f && uv[bottom].y > .9f && uv[top].y < .1f,
            "imported UV canvas matches physical face axes independently of seat");
        Check(!Resources.Load<Material>(CardBackManager.MaterialResourcePath).HasProperty("_FrontRotation"), "material no longer rotates front artwork");
        var sprite = Sprite.Create(Texture2D.whiteTexture,new Rect(0,0,1,1),Vector2.one*.5f);
        try {
            Vector4 tiling = Tile3D.ComputeSpriteTiling(sprite);
            Check(tiling.x > 0 && tiling.y > 0 && tiling.z == 0 && tiling.w == 0, "atlas rectangle is sampled directly");
        } finally { UnityEngine.Object.DestroyImmediate(sprite); }

        var atlas = new Texture2D(256,256);
        var region = Sprite.Create(atlas,new Rect(80,50,40,53),Vector2.one*.5f);
        try {
            Vector4 mapping=Tile3D.ComputeSpriteTiling(region);
            foreach(int i in cap.Take(12)) {
                Vector2 normalized=uv[i];
                Vector2 source=new Vector2(1-normalized.y,normalized.x);
                Vector2 legacyBeforeRotation=new Vector2(source.x*(53f/256)+1-(50f+53)/256,
                    source.y*(40f/256)+80f/256);
                Vector2 legacy=new Vector2(legacyBeforeRotation.y,1-legacyBeforeRotation.x);
                Vector2 actual=Vector2.Scale(normalized,new Vector2(mapping.x,mapping.y))+new Vector2(mapping.z,mapping.w);
                Check(Vector2.Distance(actual,legacy)<.00001f,"atlas migration preserves sampled artwork vertex "+i);
            }
            Vector2 expected=TileTextureLayout.ContainUvScale(.5f);
            var portrait=new Texture2D(100,200);
            try {
                Vector4 contain=TileTextureLayout.FrontContainTiling(portrait);
                Check(Mathf.Abs(contain.x-expected.x)<.00001f&&Mathf.Abs(contain.y-expected.y)<.00001f,"custom portrait artwork keeps its aspect on normalized face axes");
            } finally { UnityEngine.Object.DestroyImmediate(portrait); }
        } finally { UnityEngine.Object.DestroyImmediate(region);UnityEngine.Object.DestroyImmediate(atlas); }

        var root = new GameObject("Tile surface validation"); root.SetActive(false);
        try {
            var face = UnityEngine.Object.Instantiate(prefab,root.transform).GetComponentInChildren<Renderer>(true);
            var current = typeof(RuleRegistry).GetProperty("Current"); var previous = current.GetValue(null);
            try {
                current.SetValue(null,new RuleManifest { RuleId="test",TileBadgeText=t=>t==11?"金":null });
                foreach (float angle in new[] { 0f,90f,180f,270f }) {
                    face.transform.rotation=Quaternion.Euler(90,angle,0);
                    foreach (bool halfTurn in new[]{false,true}) {
                        RuleTileBadge3D.Apply(face,11,true,halfTurn);
                        var label=face.GetComponentInChildren<TextMeshPro>(true);
                        Vector3 actualUp=face.transform.InverseTransformDirection(label.transform.up);
                        Check(Vector3.Dot(actualUp,halfTurn?Vector3.up:Vector3.down)>.999f,"face-attached orientation " + angle + "/" + halfTurn);
                        Vector3 center=label.transform.TransformPoint(label.mesh.bounds.center);
                        Vector3 relative=face.transform.InverseTransformPoint(center)-face.localBounds.center;
                        Check(halfTurn ? relative.x>0 && relative.y>0 : relative.x<0 && relative.y<0,"upper right follows artwork " + angle + "/" + halfTurn);
                    }
                }
                var pooledLabel=face.GetComponentInChildren<TextMeshPro>(true);
                float previousScale=pooledLabel.transform.localScale.x;
                Bounds previousBounds=pooledLabel.mesh.bounds;
                pooledLabel.mesh.bounds=new Bounds(Vector3.zero,Vector3.zero);
                RuleTileBadge3D.Apply(face,11,true,true);
                Check(Mathf.Abs(pooledLabel.transform.localScale.x-previousScale)<.000001f,
                    "inactive pooled TMP mesh bounds do not change glyph scale");
                pooledLabel.mesh.bounds=previousBounds;
                RuleTileBadge3D.Apply(face,11,false);
                Check(!face.GetComponentInChildren<TextMeshPro>(true).gameObject.activeSelf,"concealed face clears overlay");
            } finally { current.SetValue(null,previous); }
        } finally { UnityEngine.Object.DestroyImmediate(root); }

        var settings = Resources.FindObjectsOfTypeAll<Salasasa.Home.SettingsStyleCollection>().Single(s=>s.gameObject.scene.IsValid());
        foreach(var page in settings.bakedPages.Where(p=>p.category==2)) {
            var row=page.controls.Single(f=>f.key=="DoraHintsRow"); var neighbor=page.controls.Single(f=>f.key=="TileCornerLabelsRow");
            Check(row.options.All(b=>b.transform.IsChildOf(row.root)) && row.dropdown.transform.IsChildOf(row.root),"theme " + page.variant + " dora controls own references");
            Check(row.UseSegments,"theme " + page.variant + " dora setting paints its segmented selection");
            Check(row.root.anchoredPosition.x>=neighbor.root.anchoredPosition.x+neighbor.root.rect.width && Mathf.Abs(row.root.anchoredPosition.y-neighbor.root.anchoredPosition.y)<.01f,"theme " + page.variant + " settings do not overlap");
        }
        var canvas=Resources.FindObjectsOfTypeAll<GameCanvas>().Single(s=>s.gameObject.scene.IsValid());
        var control=(RectTransform)canvas.transform.Find("GamePanel/GameSceneUI/SelfControl");
        var hand=(RectTransform)control.Find("HandCardsContainer");
        Check(Mathf.Abs(control.anchoredPosition.y+hand.anchoredPosition.y-16)<.01f,"bottom controls retain 16 reference pixels of margin");
        return new { success=true,count=checks.Count,checks };
    }
}
#endif
