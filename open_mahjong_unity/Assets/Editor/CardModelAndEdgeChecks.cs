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
        string[] floatKeys = { "CardBackBrightness", "TableFaceBrightness", "FrontEdgeBrightness", "BackEdgeBrightness" };
        var present = strings.Concat(ints).Concat(floatKeys).ToDictionary(k => k, PlayerPrefs.HasKey);
        var text = strings.ToDictionary(k => k, PlayerPrefs.GetString);
        var numbers = ints.ToDictionary(k => k, PlayerPrefs.GetInt);
        var decimals = floatKeys.ToDictionary(k => k, PlayerPrefs.GetFloat);
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
            var cardRoot = edge.transform.parent;
            var facePage = cardRoot.Find("TableFacePage");
            foreach (var block in new[] { facePage.Find("TableBackgroundImageDesign"), facePage.Find("TableFaceSolidColor"),
                edge.transform.Find("FrontEdgeDesign"), edge.transform.Find("BackEdgeDesign") }) {
                // Bounds of a block's own rectangle, excluding any hidden descendants.
                var corners = new Vector3[4]; ((RectTransform)block).GetWorldCorners(corners);
                var backCorners = new Vector3[4]; ((RectTransform)back.transform.Find("BackImageDesign")).GetWorldCorners(backCorners);
                Check(Mathf.Abs(corners[0].y-backCorners[0].y)<.01f && Mathf.Abs(corners[1].y-backCorners[1].y)<.01f,
                    block.name+" aligns with the back page's top and bottom edges");
            }
            var sharedPreview = allPreviews.Single();
            var sharedRoot = (RectTransform)cardRoot.Find("Actual3DPreview");
            Check(sharedPreview.transform.IsChildOf(sharedRoot) && facePage.Find("Actual3DPreview") == null,
                "one preview belongs to the common card root rather than an individual settings page");
            sharedPreview.RenderPreview();
            var sharedRig = Field<GameObject>(sharedPreview,"rig");
            var sharedCamera = Field<Camera>(sharedPreview,"previewCamera");
            var sharedTexture = Field<RenderTexture>(sharedPreview,"texture");
            var sharedMaterial = Field<Material>(sharedPreview,"material");
            Check(sharedRig != null && sharedCamera != null && sharedTexture != null && sharedMaterial != null,
                "shared preview creates one rig, camera, render texture and material");
            var modelCorners = new Vector3[4]; sharedRoot.GetWorldCorners(modelCorners);
            foreach (int tab in new[] { 0, 2, 1, 2, 0, 1 }) {
                CardDesignAggregationVerification.ReleasePreviewSprites(); tabs.ShowCard(tab);
                CardDesignAggregationVerification.ReleasePreviewSprites(); tabs.RefreshPanel();
                sharedPreview.RenderPreview();
                var currentCorners = new Vector3[4]; sharedRoot.GetWorldCorners(currentCorners);
                Check(sharedRoot.gameObject.activeInHierarchy
                    && modelCorners.Zip(currentCorners,(a,b)=>Vector3.Distance(a,b)).All(d=>d<.01f),
                    "card tab " + tab + " and refresh keep the shared preview visible at unchanged bounds");
                Check(back.gameObject.activeInHierarchy == (tab==0) && edge.gameObject.activeInHierarchy == (tab==1)
                    && facePage.gameObject.activeInHierarchy == (tab==2),
                    "card tab " + tab + " shows only its own lower settings page");
                Check(Field<GameObject>(sharedPreview,"rig")==sharedRig && Field<Camera>(sharedPreview,"previewCamera")==sharedCamera
                    && Field<RenderTexture>(sharedPreview,"texture")==sharedTexture && Field<Material>(sharedPreview,"material")==sharedMaterial,
                    "card tab " + tab + " reuses the same render resources without destroying or rebuilding them");
                capture("13_shared_preview_tab_"+tab,1280,720);
                CheckClickable(sharedRoot.Find("EditFace").GetComponent<Button>());
            }
            capture("05_main_edges",1280,720);
            var source = prefab.GetComponentInChildren<MeshFilter>(true);
            var backArtwork = Field<Image>(back,"previewArtwork");
            Check(backArtwork != null && !backArtwork.preserveAspect && !backArtwork.raycastTarget
                && backArtwork.rectTransform.anchorMin==Vector2.zero && backArtwork.rectTransform.anchorMax==Vector2.one,
                "back artwork stretches over the full card and composites separately from its base color");
            model.transform.rotation = source.transform.rotation;
            model.transform.localScale = source.transform.lossyScale;
            float actualAspect = renderer.bounds.size.x / renderer.bounds.size.y;
            foreach(var previewImage in new[] { Field<Image>(back,"previewImage"), Field<Image>(background,"tableBgPreview") }) {
                Vector2 size = previewImage.rectTransform.rect.size;
                Check(Mathf.Abs(size.x/size.y-actualAspect)<.001f && Mathf.Abs(size.y-186f)<.01f,
                    previewImage.name+" matches the actual tile mesh aspect "+actualAspect+" at the shared 186px height (actual "+size+")");
            }
            var legacySliderObject = new GameObject("Legacy brightness slider",typeof(RectTransform),typeof(Slider));
            try {
                var legacy = legacySliderObject.GetComponent<Slider>(); legacy.maxValue=255f; legacy.value=128f;
                SceneConfigColorUi.SyncBrightness(legacy,null,0f);
                Check(legacy.minValue==-100f && legacy.maxValue==100f && legacy.wholeNumbers && legacy.normalizedValue==.5f,
                    "legacy 0..255 brightness serialization is normalized to a centered -100..100 range");
            } finally { Object.DestroyImmediate(legacySliderObject); }
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
            void CheckFaceMode(bool solid) {
                Check(config.TableFaceUseSolidColor==solid && config.UseTableFaceBackground!=solid,
                    "background and solid settings are complementary");
                foreach (var pair in new[] { ("useTableFaceSolidButton",solid), ("noTableFaceSolidButton",!solid),
                    ("useTableBackgroundButton",!solid), ("noTableBackgroundButton",solid) })
                    Check(Same(Field<Button>(background,pair.Item1).GetComponent<Image>().color,
                        pair.Item2 ? SceneConfigUi.TabOn : SceneConfigUi.TabOff),pair.Item1+" has the linked selection highlight");
            }
            void CheckClickable(Selectable control) {
                Canvas.ForceUpdateCanvases();
                var rect=(RectTransform)control.transform;
                var point=RectTransformUtility.WorldToScreenPoint(canvas.worldCamera,rect.TransformPoint(rect.rect.center));
                var hits=new List<RaycastResult>();
                canvas.GetComponent<GraphicRaycaster>().Raycast(new PointerEventData(null){position=point},hits);
                Check(hits.Count>0 && hits[0].gameObject.GetComponentInParent<Selectable>()==control,
                    control.name+" receives actual pointer input without another control blocking it; hits="
                    +string.Join(",",hits.Take(3).Select(h=>h.gameObject.name)));
            }
            capture("10_linked_face_brightness",1280,720);
            CheckClickable(Field<Slider>(background,"tableFaceSliderBrightness"));
            foreach(var choice in new[] { ("noTableBackgroundButton",true), ("useTableBackgroundButton",false),
                ("useTableFaceSolidButton",true), ("noTableFaceSolidButton",false) }) {
                Field<Button>(background,choice.Item1).onClick.Invoke(); CheckFaceMode(choice.Item2);
            }
            CardBackManager.SetTableFaceBackgroundEnabled(false);
            background.RefreshSolidColorUi(); CheckFaceMode(true);
            var baseColor=new Color(.2f,.4f,.6f,1f);
            var shades=new[] { (-100,Color.black), (100,Color.white), (-50,new Color(.1f,.2f,.3f)),
                (50,new Color(.6f,.7f,.8f)), (0,baseColor) };
            void DragBrightness(Slider slider, int value) {
                Check(slider.minValue==-100f && slider.maxValue==100f && slider.wholeNumbers,
                    slider.name+" has the correct signed brightness range");
                var track=(RectTransform)slider.handleRect.parent;
                Vector2 local=new Vector2(Mathf.Lerp(track.rect.xMin,track.rect.xMax,(value+100f)/200f),track.rect.center.y);
                var screen=RectTransformUtility.WorldToScreenPoint(canvas.worldCamera,track.TransformPoint(local));
                slider.OnDrag(new PointerEventData(null) { position=screen, button=PointerEventData.InputButton.Left,
                    pointerPressRaycast=new RaycastResult { module=canvas.GetComponent<GraphicRaycaster>() } });
                Check(slider.value==value,slider.name+" dragging to "+((value+100)/2)+"% produces brightness "+value);
            }
            bool BaseControlsUnchanged(object panel,string prefix,string hexField) =>
                Field<TMP_InputField>(panel,hexField).text=="336699"
                && new[]{("R",51f),("G",102f),("B",153f)}.All(c=>Field<Slider>(panel,prefix+c.Item1).value==c.Item2);
            CardBackManager.SetTableFaceColor(baseColor); background.RefreshSolidColorUi();
            edge.SetFrontEdgeMode(CardEdgePanel.FrontEdgeMode.FollowTableBg);
            foreach(var shade in shades) {
                DragBrightness(Field<Slider>(background,"tableFaceSliderBrightness"),shade.Item1);
                Check(Same(config.TableFaceColor,baseColor) && BaseControlsUnchanged(background,"tableFaceSlider","tableFaceHexInput"),
                    "3D face brightness "+shade.Item1+" preserves RGB and HEX, including after black/white endpoints");
                Check(Same(template.GetColor("_TableFaceColor"),shade.Item2)
                    && Same(Field<Image>(background,"tableFaceColorPreview").color,shade.Item2)
                    && Same(CardBackManager.CurrentFrontEdgeColor,shade.Item2),
                    "3D face brightness updates the material, preview and following front edge");
            }
            Field<Slider>(background,"tableFaceSliderBrightness").value=50;
            Field<Slider>(background,"tableFaceSliderR").value=72;
            Check(config.TableFaceColor.r==72/255f && config.TableFaceColor.g==baseColor.g && config.TableFaceBrightness==.5f,
                "RGB editing preserves the independent face brightness");
            capture("10_linked_face_brightness",1280,720);
            tabs.ShowCard(0);
            capture("11_back_brightness",1280,720);
            CheckClickable(Field<Slider>(back,"sliderBrightness"));
            Field<TMP_InputField>(back,"hexInput").text="336699";
            Field<Button>(back,"hexApplyButton").onClick.Invoke();
            foreach(var shade in shades) {
                DragBrightness(Field<Slider>(back,"sliderBrightness"),shade.Item1);
                Check(Same(config.CardBackColor,baseColor) && BaseControlsUnchanged(back,"slider","hexInput")
                    && PlayerPrefs.GetString("CardBackColor")=="336699FF",
                    "card back brightness "+shade.Item1+" preserves RGB, HEX and saved base color");
                Check(Same(CardBackManager.CurrentColor,shade.Item2)
                    && Same(CardBackManager.CurrentBackEdgeColor,shade.Item2)
                    && Same(Field<Image>(back,"previewImage").color,shade.Item2),
                    "card back brightness updates its preview, material and following back edge");
            }
            Field<Slider>(back,"sliderBrightness").value=-50; back.ReloadSaved();
            Check(Field<Slider>(back,"sliderBrightness").value==-50 && BaseControlsUnchanged(back,"slider","hexInput"),
                "reopening card back controls restores independent brightness and RGB");
            capture("11_back_brightness",1280,720);
            tabs.ShowCard(1);
            var picker=Field<GameObject>(edge,"edgeColorPicker");
            Call(edge,"OnEnable");
            capture("05_main_edges",1280,720);
            Check(!picker.activeSelf,"edge picker starts closed");
            foreach(string target in new[]{"front","back"}) {
                var colorButton=Field<Button>(edge,target+"EdgeColorButton");
                CheckClickable(colorButton);
                colorButton.onClick.Invoke();
                Check(picker.activeSelf && picker.GetComponentsInChildren<Slider>().Length==4,
                    target+" edge opens exactly four color sliders");
                capture("12_"+target+"_edge_picker",1280,720);
                foreach(var control in picker.GetComponentsInChildren<Selectable>()) CheckClickable(control);
                Field<TMP_InputField>(edge,target+"EdgeHexInput").text="336699";
                Field<Button>(edge,target+"EdgeHexApplyButton").onClick.Invoke();
                foreach(var shade in shades) {
                    DragBrightness(Field<Slider>(edge,"edgeColorSliderBrightness"),shade.Item1);
                    Color stored=target=="front" ? config.FrontEdgeColor : config.BackEdgeColor;
                    Color rendered=target=="front" ? CardBackManager.CurrentFrontEdgeColor : CardBackManager.CurrentBackEdgeColor;
                    Check(Same(stored,baseColor) && BaseControlsUnchanged(edge,"edgeColorSlider",target+"EdgeHexInput"),
                        target+" edge brightness "+shade.Item1+" preserves independent RGB and HEX");
                    Check(Same(rendered,shade.Item2),target+" edge brightness changes its rendered color");
                }
                Field<Slider>(edge,"edgeColorSliderBrightness").value=50;
                Field<Slider>(edge,"edgeColorSliderG").value=96;
                Check((target=="front" ? config.FrontEdgeColor : config.BackEdgeColor).g==96/255f
                    && (target=="front" ? config.FrontEdgeBrightness : config.BackEdgeBrightness)==.5f,
                    target+" edge RGB editing preserves brightness");
                capture("12_"+target+"_edge_picker",1280,720);
                Field<Button>(edge,"edgeColorPickerCloseButton").onClick.Invoke();
                Check(!picker.activeSelf,"edge picker close button works");
                colorButton.onClick.Invoke();colorButton.onClick.Invoke();
                Check(!picker.activeSelf,"same color button toggles the picker closed");
            }
            Check(config.FrontEdgeMode==CardEdgePanel.FrontEdgeMode.Independent
                && config.BackEdgeMode==CardEdgePanel.BackEdgeMode.Independent,
                "editing an edge color selects its independent mode");
            foreach(string property in floatKeys) Set(config,property,0f);
            Call(config,"LoadCardColorBrightness");
            Check(config.CardBackBrightness==-.5f && config.TableFaceBrightness==.5f
                && config.FrontEdgeBrightness==.5f && config.BackEdgeBrightness==.5f,
                "all four brightness settings reload separately from saved RGB");
            CardBackManager.ApplySavedConfig();
            Check(Same(CardBackManager.CurrentColor,config.EffectiveCardBackColor)
                && Same(CardBackManager.CurrentFrontEdgeColor,config.EffectiveFrontEdgeColor)
                && Same(CardBackManager.CurrentBackEdgeColor,config.EffectiveBackEdgeColor),
                "reapplying saved configuration applies brightness once to all card surfaces");
            edge.SetBackEdgeMode(CardEdgePanel.BackEdgeMode.FollowFront);
            Check(Same(CardBackManager.CurrentBackEdgeColor,config.EffectiveFrontEdgeColor),"back edge follows the adjusted front color");
            edge.SetFrontEdgeMode(CardEdgePanel.FrontEdgeMode.FollowBackEdge);
            Check(Same(CardBackManager.CurrentFrontEdgeColor,config.EffectiveBackEdgeColor),"front edge follows the adjusted independent back color");
            frontRestore.onClick.Invoke(); backRestore.onClick.Invoke();
            Field<Button>(back,"restoreButton").onClick.Invoke();
            Field<Button>(background,"restoreTableFaceColorButton").onClick.Invoke();
            Check(floatKeys.All(key=>(float)typeof(ConfigManager).GetProperty(key).GetValue(config)==0f),
                "restore defaults resets all four brightness values to neutral");
            tabs.ShowCard(1);
            Set(config,"StandardTilePackId",TilePackIds.PackHkMahjong);
            foreach(int mode in new[]{0,1,0}) {
                Set(config,"WhiteDragonFaceMode",mode);
                int faceId=mode==0 ? 2 : 46;
                var handFace=Resources.Load<Sprite>(TilePackIds.BuiltinHandResource(TilePackIds.PackHkMahjong,faceId));
                var tableFace=Resources.Load<Texture2D>(TilePackIds.BuiltinTableResource(TilePackIds.PackHkMahjong,faceId));
                Check(handFace!=null && tableFace!=null && TileFaceResolver.LoadSprite(46)==handFace
                    && TileFaceResolver.LoadTableTexture(46)==tableFace,
                    "HK white dragon mode "+mode+" resolves its own hand and table face "+faceId);
                Call(pool,"ApplyCardTexture",model,46);
                Check(model.GetComponent<Tile3D>().SharedTileMaterial.GetTexture("_FrontTex")==tableFace,
                    "HK white dragon mode "+mode+" reaches the actual gameplay material");
            }
            Check(Resources.Load<Sprite>(TilePackIds.DefaultHandBgResource)!=null
                && Resources.Load<Sprite>(TilePackIds.HandHorizontalBgResource)!=null
                && Resources.Load<Sprite>(TilePackIds.DefaultHandBackResource)!=null,
                "all moved hand backgrounds and back resolve from the shared Cards directory");
            Check(Resources.LoadAll<Texture2D>(TilePackIds.HongqueHandRoot).Length==126
                && Resources.LoadAll<Texture2D>(TilePackIds.HongqueTableRoot).Length==126
                && HongqueTileVisual.LoadTexture(1001)!=null && HongqueTileVisual.LoadTableTexture(1001)!=null,
                "both moved Hongque packs retain all 126 loadable faces");
            foreach (string pack in new[]{TilePackIds.PackOfficial,TilePackIds.PackFluffy,TilePackIds.PackHkMahjong}) {
                Set(config,"StandardTilePackId",pack);
                Call(pool,"ApplyCardTexture",model,11);
                var tile=model.GetComponent<Tile3D>(); CardBackManager.ApplyInstanceVisuals(tile);
                renderer.GetPropertyBlock(mpb,0);
                var expected=mpb.GetVector("_FrontTilingOffset");
                foreach (var preview in allPreviews) {
                    // Verify the shared renderer without changing the page layout.
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
            sharedPreview.RenderPreview();
            renderer.GetPropertyBlock(mpb,0);
            Check(Vector4.Distance(Field<Material>(sharedPreview,"material").GetVector("_FrontTilingOffset"),mpb.GetVector("_FrontTilingOffset"))<.00001f,
                "the shared preview retains the correct model mapping on the 3D background tab");
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
            foreach(var key in floatKeys) { if(present[key])PlayerPrefs.SetFloat(key,decimals[key]);else PlayerPrefs.DeleteKey(key); }
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
