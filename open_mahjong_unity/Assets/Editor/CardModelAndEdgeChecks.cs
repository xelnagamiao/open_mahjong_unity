using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.U2D;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Exercise the main scene's actual buttons and compare its preview to the gameplay pool.</summary>
public static class CardModelAndEdgeChecks
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Flags).GetValue(target);
    static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags).Invoke(target, args);
    static void Set(object target, string name, object value) => target.GetType().GetProperty(name, Flags).SetValue(target, value);

    public static string Run(CardEdgePanel edge, CardBackConfigPanel back, CardFaceBackgroundPanel background,
        CardDesignTabs tabs, Action<string,int,int> capture, string output)
    {
        var report = new StringBuilder();
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); report.AppendLine("PASS " + message); }
        bool Same(Color a, Color b) => Vector4.Distance(a,b) < .008f;
        string[] strings = { "SideColor", "FrontEdgeColor", "BackEdgeColor", "CardBackColor", "CardBackImagePath", "TableFaceColor", "StandardTilePackId" };
        string[] ints = { "SideColorLightingVersion", "FrontEdgeSync", "FrontEdgeMode", "BackEdgeSync", "BackEdgeMode", "CardBackImageIsCustom", "TableFaceUseSolidColor", "UseTableFaceBackground", "CustomStandardTilePack", "UseHandFaceBackground" };
        var present = strings.Concat(ints).ToDictionary(k => k, PlayerPrefs.HasKey);
        var text = strings.ToDictionary(k => k, PlayerPrefs.GetString);
        var numbers = ints.ToDictionary(k => k, PlayerPrefs.GetInt);
        var oldConfig = ConfigManager.Instance;
        var staticState = typeof(CardBackManager).GetFields(Flags).Where(f => f.IsStatic && !f.IsLiteral && !f.IsInitOnly).ToDictionary(f => f, f => f.GetValue(null));
        var template = Resources.Load<Material>(CardBackManager.MaterialResourcePath);
        var savedMaterial = new Material(template);
        var testRoot = new GameObject("Isolated model and edge checks"); testRoot.SetActive(false);
        SceneManager.MoveGameObjectToScene(testRoot,edge.gameObject.scene);
        var config = testRoot.AddComponent<ConfigManager>();
        typeof(ConfigManager).GetProperty("Instance",Flags).SetValue(null,config);
        var pool = testRoot.AddComponent<MahjongObjectPool>();
        var model = new GameObject("Gameplay reference",typeof(MeshFilter),typeof(MeshRenderer));
        model.transform.SetParent(testRoot.transform,false);
        var gameplayMaterial = new Material(template);
        var renderer = model.GetComponent<MeshRenderer>(); renderer.sharedMaterial = gameplayMaterial;
        var allPreviews = tabs.GetComponentsInChildren<CardDesignModelPreview>(true);
        var prefab = Field<GameObject>(allPreviews[0],"tilePrefab");
        model.GetComponent<MeshFilter>().sharedMesh = prefab.GetComponentInChildren<MeshFilter>(true).sharedMesh;
        var mpb = new MaterialPropertyBlock();
        try {
            Set(config,"StandardTilePackId",TilePackIds.PackOfficial);
            Set(config,"UseTableFaceBackground",false); Set(config,"TableFaceUseSolidColor",false);
            CardBackManager.Apply(ConfigManager.DefaultCardBackColor,null);
            Call(pool,"CacheAllSprites",Resources.Load<SpriteAtlas>(TilePackIds.OfficialAtlasResource));
            tabs.ShowCard(1); Call(edge,"LoadSavedIntoUI");
            var frontInput=Field<TMP_InputField>(edge,"frontEdgeHexInput");
            var frontApply=Field<Button>(edge,"frontEdgeHexApplyButton");
            var frontRestore=Field<Button>(edge,"restoreFrontEdgeButton");
            var backRestore=Field<Button>(edge,"restoreBackEdgeButton");
            var frontColor=Field<Image>(edge,"frontSidePreview");
            Canvas.ForceUpdateCanvases();
            var canvas=edge.GetComponentInParent<Canvas>();
            foreach(var button in new[]{frontApply,frontRestore,backRestore}) {
                var rect=(RectTransform)button.transform;
                var point=RectTransformUtility.WorldToScreenPoint(canvas.worldCamera,rect.TransformPoint(rect.rect.center));
                var hits=new List<RaycastResult>();
                canvas.GetComponent<GraphicRaycaster>().Raycast(new PointerEventData(null){position=point},hits);
                Check(hits.Count>0 && hits[0].gameObject.GetComponentInParent<Button>()==button,
                    button.name+": actual UI raycast reaches the button without an overlay blocking it");
            }
            Check(frontInput==Field<TMP_InputField>(edge,"sideHexInput") && frontApply==Field<Button>(edge,"sideHexApplyButton"),
                "test covers the main scene's aliased legacy/front controls");
            config.SetSideColor(new Color(.2f,.05f,.1f)); CardBackManager.ApplySideColor(config.SideColor);
            config.SetFrontEdgeColor(new Color(.4f,.3f,.2f)); Call(edge,"LoadSavedIntoUI");
            frontRestore.onClick.Invoke();
            Check(Same(config.SideColor,ConfigManager.DefaultSideColor) && Same(config.FrontEdgeColor,Color.white)
                && Same(template.GetColor("_SideColor")*template.GetColor("_FrontEdgeColor"),Color.white),
                "front restore clears both legacy tint and front tint on the actual tile material");
            Check(frontInput.text=="FFFFFF" && Same(frontColor.color,Color.white)
                && config.FrontEdgeMode==CardEdgePanel.FrontEdgeMode.Independent,"front restore updates HEX, preview and mode together");
            edge.SetFrontEdgeMode(CardEdgePanel.FrontEdgeMode.FollowTableBg);
            Field<Button[]>(edge,"sideSwatches")[1].onClick.Invoke();
            Check(Same(config.FrontEdgeColor,SceneConfigUi.PresetColors[1]) && Same(config.SideColor,Color.white)
                && config.FrontEdgeMode==CardEdgePanel.FrontEdgeMode.Independent
                && Same(frontColor.color,config.FrontEdgeColor),"front swatch selects an independent color and matches its displayed value");
            foreach(string hex in new[]{"800020","001040","303030"}) {
                frontInput.text=hex; frontApply.onClick.Invoke();
                ColorUtility.TryParseHtmlString("#"+hex,out var expected);
                Check(Same(config.FrontEdgeColor,expected) && Same(CardBackManager.CurrentFrontEdgeColor,expected)
                    && Same(frontColor.color,expected) && frontInput.text==hex,
                    "front HEX applies exactly once and preserves " + hex);
            }
            edge.SetBackEdgeMode(CardEdgePanel.BackEdgeMode.FollowFront);
            frontRestore.onClick.Invoke();
            Check(Same(CardBackManager.CurrentBackEdgeColor,Color.white),"front restore updates the back edge when it follows the independent front color");
            var backInput=Field<TMP_InputField>(edge,"backEdgeHexInput"); backInput.text="172842";
            Field<Button>(edge,"backEdgeHexApplyButton").onClick.Invoke();
            Check(ColorUtility.ToHtmlStringRGB(config.BackEdgeColor)=="172842"
                && config.BackEdgeMode==CardEdgePanel.BackEdgeMode.Independent,"back HEX still applies and selects independent mode");
            edge.SetFrontEdgeMode(CardEdgePanel.FrontEdgeMode.FollowBackEdge);
            backRestore.onClick.Invoke();
            Check(config.BackEdgeMode==CardEdgePanel.BackEdgeMode.FollowBack && Same(config.BackEdgeColor,ConfigManager.DefaultBackEdgeColor)
                && Same(CardBackManager.CurrentBackEdgeColor,CardBackManager.CurrentColor)
                && Same(CardBackManager.CurrentFrontEdgeColor,ConfigManager.DefaultBackEdgeColor),"back restore and the front independent-color follower both refresh correctly");
            frontRestore.onClick.Invoke();
            tabs.ShowCard(0);
            Field<TMP_InputField>(back,"hexInput").text="771122";
            Field<Button>(back,"hexApplyButton").onClick.Invoke();
            Field<Button>(back,"restoreButton").onClick.Invoke();
            Check(Same(config.CardBackColor,ConfigManager.DefaultCardBackColor)
                && Same(CardBackManager.CurrentColor,config.CardBackColor) && CardBackManager.CurrentTexture==null
                && Same(CardBackManager.CurrentBackEdgeColor,config.CardBackColor),"card back restore resets color/image and the following back edge");
            CardDesignAggregationVerification.ReleasePreviewSprites(); tabs.ShowCard(2);
            CardBackManager.SetTableFaceColor(Color.red); CardBackManager.SetTableFaceSolidColorEnabled(true);
            Field<Button>(background,"restoreTableFaceColorButton").onClick.Invoke();
            Check(Same(config.TableFaceColor,ConfigManager.DefaultTableFaceColor) && !config.TableFaceUseSolidColor
                && template.GetFloat("_TableFaceBlend")==0,"3D background solid-color restore resets its color and mode");
            tabs.ShowCard(1);
            foreach (string pack in new[]{TilePackIds.PackOfficial,TilePackIds.PackFluffy,TilePackIds.PackHkMahjong}) {
                Set(config,"StandardTilePackId",pack);
                Call(pool,"ApplyCardTexture",model,11);
                var tile=model.GetComponent<Tile3D>(); CardBackManager.ApplyInstanceVisuals(tile);
                renderer.GetPropertyBlock(mpb,0);
                var expected=mpb.GetVector("_FrontTilingOffset");
                foreach (var preview in allPreviews) {
                    // Each region has the same renderer; verify both without changing the page layout.
                    bool wasActive=preview.gameObject.activeInHierarchy;
                    if(!wasActive) continue;
                    preview.RenderPreview();
                    var actual=Field<Material>(preview,"material");
                    Check(actual.GetTexture("_FrontTex")==tile.SharedTileMaterial.GetTexture("_FrontTex")
                        && Vector4.Distance(expected,actual.GetVector("_FrontTilingOffset"))<.00001f
                        && actual.GetFloat("_FrontRotation")==tile.SharedTileMaterial.GetFloat("_FrontRotation"),
                        pack+": preview texture, crop, stretch and rotation match MahjongObjectPool/Tile3D");
                    Save(Field<RenderTexture>(preview,"texture"),Path.Combine(output,"model_"+pack+".png"));
                }
                capture("08_fixed_edges_"+pack,1280,720);
            }
            CardDesignAggregationVerification.ReleasePreviewSprites(); tabs.ShowCard(2);
            var second=allPreviews.Single(p=>p.gameObject.activeInHierarchy); second.RenderPreview();
            renderer.GetPropertyBlock(mpb,0);
            Check(Vector4.Distance(Field<Material>(second,"material").GetVector("_FrontTilingOffset"),mpb.GetVector("_FrontTilingOffset"))<.00001f,
                "the relocated 3D background page uses the same corrected model mapping");
            capture("09_fixed_3d_background",1280,720);
            return report.ToString();
        } finally {
            // Do not leave test colors, pack selections, materials or PlayerPrefs in the main editor.
            foreach(var p in allPreviews) Call(p,"Release");
            foreach(string cache in new[]{"hongqueMaterialCache","customStandardMaterialCache"})
                foreach(var m in Field<Dictionary<int,Material>>(pool,cache).Values) Object.DestroyImmediate(m);
            foreach(var s in Field<Dictionary<int,Sprite>>(pool,"spriteCache").Values) Object.DestroyImmediate(s);
            template.CopyPropertiesFromMaterial(savedMaterial);
            foreach(var pair in staticState) pair.Key.SetValue(null,pair.Value);
            typeof(ConfigManager).GetProperty("Instance",Flags).SetValue(null,oldConfig);
            foreach(var key in strings) { if(present[key])PlayerPrefs.SetString(key,text[key]);else PlayerPrefs.DeleteKey(key); }
            foreach(var key in ints) { if(present[key])PlayerPrefs.SetInt(key,numbers[key]);else PlayerPrefs.DeleteKey(key); }
            PlayerPrefs.Save();
            Object.DestroyImmediate(testRoot); Object.DestroyImmediate(savedMaterial); Object.DestroyImmediate(gameplayMaterial);
            Call(edge,"LoadSavedIntoUI");
        }

    }

    static void Save(RenderTexture texture,string path) {
        var old=RenderTexture.active; RenderTexture.active=texture;
        var image=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false);
        image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0); image.Apply();
        File.WriteAllBytes(path,image.EncodeToPNG()); RenderTexture.active=old; Object.DestroyImmediate(image);
    }
}
