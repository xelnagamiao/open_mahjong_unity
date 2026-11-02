using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Authors only the approved footer, controls, utility row and replay selectors.</summary>
public static partial class TableHudUiBaker {
    private const string Source = "Assets/Resources/image/UI/TableHud/Source/";
    private static readonly List<TMP_Text> primary = new List<TMP_Text>();
    private static TMP_FontAsset font;

    [MenuItem("Tools/Mahjong/Table HUD/Style/雾蓝")] private static void Mist() => ApplyStyle(TableHudTheme.Style.MistBlue);
    [MenuItem("Tools/Mahjong/Table HUD/Style/青瓷")] private static void Jade() => ApplyStyle(TableHudTheme.Style.Jade);
    [MenuItem("Tools/Mahjong/Table HUD/Style/铜棕")] private static void Paper() => ApplyStyle(TableHudTheme.Style.PaperCinnabar);
    [MenuItem("Tools/Mahjong/Table HUD/Style/石墨")] private static void Graphite() => ApplyStyle(TableHudTheme.Style.GraphiteIce);
    [MenuItem("Tools/Mahjong/Table HUD/Style/夜蓝")] private static void Night() => ApplyStyle(TableHudTheme.Style.IndigoGold);
    [MenuItem("Tools/Mahjong/Table HUD/Style/紫晶")] private static void Violet() => ApplyStyle(TableHudTheme.Style.Violet);
    [MenuItem("Tools/Mahjong/Table HUD/Style/琥珀")] private static void Warm() => ApplyStyle(TableHudTheme.Style.WarmGray);
    [MenuItem("Tools/Mahjong/Table HUD/Style/晶蓝")] private static void Blue() => ApplyStyle(TableHudTheme.Style.OriginalBlue);
    private static void ApplyStyle(TableHudTheme.Style style) {
        var scene = SceneManager.GetActiveScene();
        foreach (var theme in scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<TableHudTheme>(true))) {
            Undo.RecordObject(theme,"Change table control palette"); theme.SetStyle(style); EditorUtility.SetDirty(theme);
        }
        if (!EditorApplication.isPlaying) EditorSceneManager.MarkSceneDirty(scene);
    }

    [MenuItem("Tools/Mahjong/Table HUD/1. Apply HUD Design")]
    public static void Bake() {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before authoring.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/MainScene.unity") throw new InvalidOperationException("Open MainScene first.");
        var root = scene.GetRootGameObjects().Single(x => x.name == "#GameCanvas").transform.Find("GamePanel");
        var rounds = root.Find("GameSceneUI/RoundPanel");
        var controls = root.Find("#GameRecordUI/ControlPanel");
        var utility = root.Find("GameSceneUI/GamePageControl");
        var exits = root.Find("GameSceneUI/#ExitButtonManager");
        foreach (var target in new[] { rounds, controls, utility, exits }) Undo.RegisterFullObjectHierarchyUndo(target.gameObject,"Author table controls");
        primary.Clear(); font = rounds.GetComponentInChildren<TMP_Text>(true).font;
        BakeFooter(rounds);
        TableHudTypographyBaker.Prepare();
        font = TableHudTypography.Body;
        BakeControl(controls);
        ReplayControlsBaker.Bake(scene);
        BakeUtility(utility, exits);
        BakePopup(controls, "GameRoundContent", true, controls.Find("GameRound").GetComponent<Button>());
        BakePopup(controls, "XunmuContent", false, controls.Find("Xunmu").GetComponent<Button>());
        BakeRecordItem("Assets/Prefabs/Record/RecordRoundItem.prefab", true);
        BakeRecordItem("Assets/Prefabs/Record/RecordNodeItem.prefab", false);
        var group = controls.GetComponent<TableHudPopupGroup>() ?? Undo.AddComponent<TableHudPopupGroup>(controls.gameObject);
        foreach (var popup in controls.GetComponentsInChildren<TableHudSelectorPopup>(true)) {
            if (popup.roundSelector) group.rounds = popup; else group.turns = popup;
        }
        var theme = root.GetComponent<TableHudTheme>() ?? Undo.AddComponent<TableHudTheme>(root.gameObject);
        var targets = new List<Transform> { controls };
        foreach (Transform child in utility) if (child.GetComponent<Button>()) targets.Add(child);
        foreach (Transform child in exits) if (child.GetComponent<Button>()) targets.Add(child);
        AssignArray(theme,"roots",targets); AssignArray(theme,"primaryTexts",primary.Distinct());
        AssignArray<TMP_Text>(theme,"mutedTexts",Array.Empty<TMP_Text>()); AssignArray<TMP_Text>(theme,"accentTexts",Array.Empty<TMP_Text>());
        var utilityLayout = root.GetComponent<TableHudUtilityLayout>() ?? Undo.AddComponent<TableHudUtilityLayout>(root.gameObject);
        utilityLayout.utility = (RectTransform)utility; utilityLayout.exits = (RectTransform)exits; EditorUtility.SetDirty(utilityLayout);
        theme.SetStyle(TableHudTheme.Style.OriginalBlue);
        utilityLayout.RefreshLayout();
        EditorUtility.SetDirty(group); EditorSceneManager.MarkSceneDirty(scene);
    }

    private static void BakeFooter(Transform rounds) {
        var venues = new List<TMP_Text>(); var formats = new List<TMP_Text>();
        var contents = new List<RectTransform>(); var strips = new List<RectTransform>();
        foreach (string name in new[] { "SimpleRoundPanel", "RiichiRoundPanel" }) {
            var panel = (RectTransform)rounds.Find(name);
            var header = (RectTransform)panel.Find("Header");
            var content = (RectTransform)panel.Find("Content");
            var format = (header.Find("GameRound") ?? panel.Find("MatchStrip/GameRound")).GetComponent<TMP_Text>();
            var strip = Rect(panel,"MatchStrip");
            foreach (Transform child in strip.Cast<Transform>().ToArray()) if (child != format.transform && child.name != "Venue" && child.name != "Separator") Undo.DestroyObjectImmediate(child.gameObject);
            var background = strip.GetComponent<Image>() ?? strip.gameObject.AddComponent<Image>();
            background.sprite = null; background.type = Image.Type.Simple; background.color = content.GetComponent<Image>().color; background.raycastTarget = false;
            var venue = Label(strip,"Venue","场次未标注",20);
            var separator = Label(strip,"Separator","·",20);
            format.transform.SetParent(strip,false); StyleText(format,20,TextAlignmentOptions.Center);
            foreach (var text in new[] { venue, separator, format }) { text.color = new Color32(40,47,54,255); text.enableAutoSizing = false; text.margin = Vector4.zero; }
            // Only the rule label's available width changes when the format moves to the footer.
            var rule = header.Find("Rule") as RectTransform;
            float horizontalPadding = header.GetComponent<HorizontalLayoutGroup>()?.padding.horizontal ?? 0;
            rule.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,header.rect.width - horizontalPadding);
            venues.Add(venue); formats.Add(format); contents.Add(content); strips.Add(strip);
        }
        var match = rounds.GetComponent<TableHudMatchInfo>() ?? Undo.AddComponent<TableHudMatchInfo>(rounds.gameObject);
        AssignArray(match,"venueLabels",venues); AssignArray(match,"formatLabels",formats); AssignArray(match,"contentRects",contents); AssignArray(match,"stripRects",strips);
        Assign(rounds.GetComponent<RoundPanel>(),"matchInfo",match); match.enabled = true; match.RefreshLayout();
    }

    private static void BakeControl(Transform controls) {
        var panel = (RectTransform)controls;
        Place(panel,new Vector2(.5f,1),new Vector2(.5f,.5f),70,-786,1040,88);
        RemoveNamed(controls,"SourceArtwork"); Surface(controls,TableHudTheme.SurfaceRole.Panel);
        for (int i = 0; i < 3; i++) RemoveNamed(controls,"Group_"+i);
        for (int i = 1; i <= 2; i++) { var r = Rect(controls,"Divider_"+i); Center(r); var s = r.GetComponent<TableHudSurface>() ?? r.gameObject.AddComponent<TableHudSurface>(); s.role=TableHudTheme.SurfaceRole.Divider;s.raycastTarget=false; }
        foreach (string name in new[] { "TileList","GameRound","LaterXunmu","Xunmu","NextXunmu","LaterStep","NextStep" }) {
            var button = controls.Find(name).GetComponent<Button>();
            RemoveNamed(button.transform,"SourceArtwork"); RemoveNamed(button.transform,"HudIcon");
            var surface = Surface(button.transform,TableHudTheme.SurfaceRole.Button); button.targetGraphic=surface; button.transition=Selectable.Transition.ColorTint;
            var label=button.GetComponentInChildren<TMP_Text>(true);StyleText(label,22,TextAlignmentOptions.Center);primary.Add(label);
            bool value=name=="TileList"||name=="GameRound"||name=="Xunmu";
            if (!value) {
                bool back=name=="LaterXunmu"||name=="LaterStep"; bool turn=name=="LaterXunmu"||name=="NextXunmu";
                SourceArrow(button.transform,back?"StepPrevious":"StepNext",turn);
                if (name=="NextStep" && !button.GetComponent<TableHudPlaybackArtwork>()) button.gameObject.AddComponent<TableHudPlaybackArtwork>();
            } else { RemoveNamed(button.transform,"SourceArrow");RemoveNamed(button.transform,"SourceArrowSecond"); }
            RemoveNamed(button.transform,"ReadingMark");
        }
        TableHudReplayLayout.Reflow(panel);
    }

    private static void BakeUtility(Transform utility, Transform exits) {
        // Parent transforms and the Delay child retain their original geometry.
        var layout=exits.GetComponent<HorizontalLayoutGroup>();if(layout)layout.enabled=false;
        foreach (var pair in new[] { ("SwitchToMeun",4),("OpenRuleBook",1),("OpenHistoryPanel",2) }) DecorateUtility(utility.Find(pair.Item1).GetComponent<Button>(),pair.Item2);
        foreach(Transform child in exits) { var button=child.GetComponent<Button>();if(button)DecorateUtility(button,child.name=="VotePause"?10:5); }
    }
    private static void DecorateUtility(Button button,int number) {
        RemoveNamed(button.transform,"HudSurface");RemoveNamed(button.transform,"HudIcon");
        foreach(var image in button.GetComponentsInChildren<Image>(true)) if(image.transform!=button.transform && image.name!="SourceArtwork")image.enabled=false;
        var hit=button.GetComponent<Image>();if(hit){hit.enabled=true;hit.sprite=null;hit.color=Color.clear;hit.raycastTarget=true;}
        var iconRect=Rect(button.transform,"SourceArtwork");Place(iconRect,new Vector2(0,.5f),new Vector2(0,.5f),0,0,56,56);
        var icon=iconRect.GetComponent<Image>()??iconRect.gameObject.AddComponent<Image>();icon.enabled=true;icon.sprite=CroppedIcon(number);icon.preserveAspect=true;icon.type=Image.Type.Simple;icon.raycastTarget=false;
        var supplied=iconRect.GetComponent<TableHudSourceGraphic>()??iconRect.gameObject.AddComponent<TableHudSourceGraphic>();supplied.utilityIcon=true;supplied.whiteLabel=false;supplied.glyph=false;supplied.transportBackdrop=false;
        var label=button.GetComponentInChildren<TMP_Text>(true);if(!label)label=Label(button.transform,"Label",button.name,18);
        Place(label.rectTransform,new Vector2(0,.5f),new Vector2(0,.5f),60,0,44,56);StyleText(label,18,TextAlignmentOptions.MidlineLeft);label.enableAutoSizing=true;label.fontSizeMin=16;label.textWrappingMode=TextWrappingModes.Normal;label.overflowMode=TextOverflowModes.Overflow;primary.Add(label);
        button.targetGraphic=icon;button.transition=Selectable.Transition.ColorTint;
    }
    private static Sprite CroppedIcon(int number) {
        const string folder="Assets/Resources/image/UI/TableHud/Derived";
        if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/Resources/image/UI/TableHud","Derived");
        string path=folder+"/Icon"+number+".asset";
        var existing=AssetDatabase.LoadAssetAtPath<Sprite>(path);if(existing)return existing;
        string sourceName = number switch {
            1 => "RulesIcon", 2 => "ScoreboardIcon", 3 => "HelpIcon", 4 => "HomeIcon",
            5 => "ExitIcon", 10 => "PauseVoteIcon",
            _ => throw new ArgumentOutOfRangeException(nameof(number), number, "Unknown table utility icon.")
        };
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(Source+sourceName+".png");
        // Measured solid bounds. Keep the actual circular icon, remove transparent halo padding.
        var rect=new Rect(23,23,texture.width-46,texture.height-46);
        var sprite=Sprite.Create(texture,rect,new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,Vector4.zero,false);
        sprite.name="Supplied icon "+number;AssetDatabase.CreateAsset(sprite,path);return sprite;
    }

    private static void BakePopup(Transform controls,string preferredName,bool round,Button trigger) {
        var manager=controls.GetComponentInParent<GameRecordManager>();
        if(!manager)manager=UnityEngine.Object.FindAnyObjectByType<GameRecordManager>(FindObjectsInactive.Include);
        var data=new SerializedObject(manager);var scroll=(ScrollRect)data.FindProperty(round?"roundScrollView":"xunmuScrollView").objectReferenceValue;
        if(!scroll)throw new InvalidOperationException("Missing replay selector: "+preferredName);
        var r=(RectTransform)scroll.transform;r.SetParent(controls,false);r.sizeDelta=new Vector2(round?276:248,300);
        var rootImage=r.GetComponent<Image>();if(rootImage){rootImage.enabled=true;rootImage.color=Color.clear;rootImage.sprite=null;rootImage.raycastTarget=true;}
        Surface(r,TableHudTheme.SurfaceRole.Panel);
        var heading=Label(r,"SelectorTitle",round?"选择小局":"选择巡目",20);heading.alignment=TextAlignmentOptions.MidlineLeft;Place(heading.rectTransform,new Vector2(0,1),new Vector2(0,1),14,0,220,40);primary.Add(heading);
        var viewport=scroll.viewport;Stretch(viewport,12,0,-12,-40);
        var mask=viewport.GetComponent<Mask>();if(mask)Undo.DestroyObjectImmediate(mask);
        if(!viewport.GetComponent<RectMask2D>())viewport.gameObject.AddComponent<RectMask2D>();
        var viewportImage=viewport.GetComponent<Image>();if(viewportImage){viewportImage.sprite=null;viewportImage.color=Color.clear;viewportImage.raycastTarget=true;}
        var content=scroll.content;content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(.5f,1);content.anchoredPosition=Vector2.zero;content.sizeDelta=new Vector2(0,100);
        var group=content.GetComponent<VerticalLayoutGroup>()??content.gameObject.AddComponent<VerticalLayoutGroup>();group.padding=new RectOffset(0,0,8,8);group.spacing=4;group.childAlignment=TextAnchor.UpperLeft;group.childControlWidth=true;group.childControlHeight=false;group.childForceExpandWidth=true;group.childForceExpandHeight=false;
        var fit=content.GetComponent<ContentSizeFitter>()??content.gameObject.AddComponent<ContentSizeFitter>();fit.horizontalFit=ContentSizeFitter.FitMode.Unconstrained;fit.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=24;scroll.inertia=true;scroll.decelerationRate=.08f;
        var sb=scroll.verticalScrollbar;
        if(!sb){var bar=Rect(r,"Scrollbar");sb=bar.gameObject.AddComponent<Scrollbar>();var handle=Rect(bar,"Handle");Stretch(handle);var image=handle.gameObject.AddComponent<Image>();sb.handleRect=handle;sb.targetGraphic=image;sb.direction=Scrollbar.Direction.BottomToTop;scroll.verticalScrollbar=sb;}
        var sr=(RectTransform)sb.transform;sr.anchorMin=new Vector2(1,0);sr.anchorMax=Vector2.one;sr.pivot=new Vector2(1,.5f);sr.offsetMin=new Vector2(-8,12);sr.offsetMax=new Vector2(-4,-44);
        scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;
        var empty=Label(r,"Empty","暂无可选内容",18);Stretch(empty.rectTransform,12,8,-12,-40);empty.gameObject.SetActive(false);primary.Add(empty);
        var popup=r.GetComponent<TableHudSelectorPopup>()??r.gameObject.AddComponent<TableHudSelectorPopup>();popup.roundSelector=round;popup.trigger=trigger;popup.heading=heading;popup.emptyLabel=empty;EditorUtility.SetDirty(popup);r.gameObject.SetActive(false);
    }
    private static void BakeRecordItem(string path,bool round) {
        var prefab=PrefabUtility.LoadPrefabContents(path);
        try {
            float height=round?52:44;var rect=(RectTransform)prefab.transform;rect.sizeDelta=new Vector2(round?252:224,height);
            var element=prefab.GetComponent<LayoutElement>()??prefab.AddComponent<LayoutElement>();element.minHeight=element.preferredHeight=height;element.flexibleHeight=0;
            var image=prefab.GetComponent<Image>()??prefab.AddComponent<Image>();image.sprite=null;image.color=Color.white;image.raycastTarget=true;
            var button=prefab.GetComponent<Button>()??prefab.AddComponent<Button>();button.targetGraphic=image;button.transition=Selectable.Transition.ColorTint;
            var binding=new SerializedObject(round?(UnityEngine.Object)prefab.GetComponent<RecordRoundItem>():prefab.GetComponent<RecordNodeItem>());
            var oldButton=(Button)binding.FindProperty(round?"roundButton":"nodeButton").objectReferenceValue;
            binding.FindProperty(round?"roundButton":"nodeButton").objectReferenceValue=button;binding.ApplyModifiedPropertiesWithoutUndo();
            if(oldButton&&oldButton!=button)UnityEngine.Object.DestroyImmediate(oldButton.gameObject);
            var main=(TMP_Text)binding.FindProperty(round?"roundNameText":"nodeText").objectReferenceValue;
            StyleText(main,round?22:20,TextAlignmentOptions.MidlineLeft);Stretch(main.rectTransform,16,0,round?-84:-12,0);main.enableAutoSizing=true;main.fontSizeMin=18;
            if(round){var extra=(TMP_Text)binding.FindProperty("roundExtraText").objectReferenceValue;StyleText(extra,17,TextAlignmentOptions.MidlineRight);Place(extra.rectTransform,new Vector2(1,.5f),new Vector2(1,.5f),-10,0,72,height);extra.enableAutoSizing=false;}
            var mark=Rect(prefab.transform,"CurrentMarker");Place(mark,new Vector2(0,.5f),new Vector2(0,.5f),0,0,3,height-16);var marker=mark.GetComponent<Image>()??mark.gameObject.AddComponent<Image>();marker.raycastTarget=false;mark.gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(prefab,path);
        } finally {PrefabUtility.UnloadPrefabContents(prefab);}
    }
    private static void SourceArrow(Transform parent,string spriteName,bool doubled) {
        var sprite=AssetDatabase.LoadAllAssetsAtPath(Source+"ReplayTransportSheet.png").OfType<Sprite>().Single(x=>x.name==spriteName);
        for(int i=0;i<2;i++){var r=Rect(parent,i==0?"SourceArrow":"SourceArrowSecond");Center(r);r.gameObject.SetActive(i==0||doubled);var image=r.GetComponent<Image>()??r.gameObject.AddComponent<Image>();image.sprite=sprite;image.preserveAspect=true;image.raycastTarget=false;var source=r.GetComponent<TableHudSourceGraphic>()??r.gameObject.AddComponent<TableHudSourceGraphic>();source.glyph=true;source.utilityIcon=false;source.whiteLabel=false;}
    }
    private static void RemoveNamed(Transform parent,string name){var child=parent.Find(name);if(child)Undo.DestroyObjectImmediate(child.gameObject);}
    private static void Assign(UnityEngine.Object target,string name,UnityEngine.Object value){var so=new SerializedObject(target);so.FindProperty(name).objectReferenceValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
    private static void AssignArray<T>(UnityEngine.Object target,string name,IEnumerable<T> values)where T:UnityEngine.Object{var all=values.ToArray();var so=new SerializedObject(target);var p=so.FindProperty(name);p.arraySize=all.Length;for(int i=0;i<all.Length;i++)p.GetArrayElementAtIndex(i).objectReferenceValue=all[i];so.ApplyModifiedPropertiesWithoutUndo();}
    private static RectTransform Rect(Transform parent,string name){var r=parent.Find(name)as RectTransform;if(!r){r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();Undo.RegisterCreatedObjectUndo(r.gameObject,"Table control element");r.SetParent(parent,false);}r.gameObject.layer=parent.gameObject.layer;return r;}
    private static void Place(RectTransform r,Vector2 anchor,Vector2 pivot,float x,float y,float w,float h){r.anchorMin=r.anchorMax=anchor;r.pivot=pivot;r.anchoredPosition=new Vector2(x,y);r.sizeDelta=new Vector2(w,h);r.localScale=Vector3.one;}
    private static void Center(RectTransform r){r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.localScale=Vector3.one;}
    private static void Stretch(RectTransform r,float l=0,float b=0,float right=0,float top=0){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.pivot=new Vector2(.5f,.5f);r.offsetMin=new Vector2(l,b);r.offsetMax=new Vector2(right,top);}
    private static TableHudSurface Surface(Transform parent,TableHudTheme.SurfaceRole role){var image=parent.GetComponent<Image>();if(image){image.sprite=null;image.color=Color.clear;}var r=Rect(parent,"HudSurface");Stretch(r);r.SetAsFirstSibling();var s=r.GetComponent<TableHudSurface>()??r.gameObject.AddComponent<TableHudSurface>();s.enabled=true;s.raycastTarget=false;s.role=role;return s;}
    private static TMP_Text Label(Transform parent,string name,string text,float size){var r=Rect(parent,name);var t=r.GetComponent<TextMeshProUGUI>()??r.gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.text=text;StyleText(t,size,TextAlignmentOptions.Center);return t;}
    private static void StyleText(TMP_Text t,float size,TextAlignmentOptions alignment){t.fontSize=t.fontSizeMax=size;t.fontSizeMin=Mathf.Min(16,size);t.enableAutoSizing=false;t.textWrappingMode=TextWrappingModes.NoWrap;t.overflowMode=TextOverflowModes.Overflow;t.alignment=alignment;t.margin=Vector4.zero;t.raycastTarget=false;}
}
