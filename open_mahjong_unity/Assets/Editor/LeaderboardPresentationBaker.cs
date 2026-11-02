#if UNITY_EDITOR
using System;
using System.Linq;
using Salasasa.Home;
using Salasasa.Leaderboards;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class LeaderboardPresentationBaker
{
    public static void Bake(Scene scene)
    {
        var plans = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<InterfacePlansController>(true)).Single();
        var controller = plans.ranking;
        var layout = controller.GetComponent<LeaderboardRuleLayout>();
        var view = controller.paletteEditor.view;
        var root = view.transform;
        var palette = controller.paletteEditor;
        var playerCard = controller.popup.transform.Find("PlayerDetails");
        if (playerCard) UnityEngine.Object.DestroyImmediate(playerCard.gameObject);
        foreach (var button in layout.sideButtons)
        {
            var mark = button.transform.Find("SelectedMark");
            if (mark) UnityEngine.Object.DestroyImmediate(mark.gameObject);
            var surface = button.GetComponent<SettingsShapeGraphic>();
            surface.shape = SettingsShapeGraphic.Shape.Square; surface.radius = 0; surface.borderWidth = 1;
        }
        foreach (string name in new[] { "RuleMenuButton", "RuleMenuOverlay", "RuleRail", "SidebarBackground", "Refresh" })
        {
            var obsolete = root.Find(name);
            if (obsolete) UnityEngine.Object.DestroyImmediate(obsolete.gameObject);
        }
        Func<string,RectTransform> node = p => (RectTransform)root.Find(p);
        var panel = (RectTransform)view.transform;
        panel.anchoredPosition = new Vector2(0,8); panel.sizeDelta = new Vector2(908,704);
        node("Panel").anchoredPosition = Vector2.zero; node("Panel").sizeDelta = panel.sizeDelta;
        node("Shadow").anchoredPosition = new Vector2(7,-10); node("Shadow").sizeDelta = panel.sizeDelta;
        Set(node("Header"),1,1,906,79);
        Set(node("HeaderRule"),20,80,868,1);
        Set(node("RankList"),212,96,676,496);
        Set(node("DataState"),212,96,660,496);
        Set(node("MyRank"),212,608,660,76);
        Set(node("Close"),844,18,44,44);
        Set(layout.sideRoot,20,96,168,496); layout.sideRoot.gameObject.SetActive(true);
        Set(layout.sideScroll.viewport,0,0,156,496);
        Set((RectTransform)layout.sideScroll.verticalScrollbar.transform,160,0,8,496);
        Set(layout.innerDivider.rectTransform,200,96,1,588); layout.innerDivider.gameObject.SetActive(true);
        foreach (var label in layout.sideLabels) Set(label.rectTransform,12,0,132,54);
        BakeTitle(node("Title").GetComponent<TMP_Text>());
        var sideGroup = layout.sideScroll.content.GetComponent<VerticalLayoutGroup>();
        sideGroup.spacing = 6;
        sideGroup.childControlWidth = true; sideGroup.childForceExpandWidth = true;
        sideGroup.childControlHeight = true; sideGroup.childForceExpandHeight = false;
        foreach (var button in layout.sideButtons)
        {
            var element = button.GetComponent<LayoutElement>();
            if (!element) element = button.gameObject.AddComponent<LayoutElement>();
            element.minHeight = element.preferredHeight = 54;
        }
        foreach (var label in layout.sideLabels) {Format(label,20,TextAlignmentOptions.MidlineLeft);label.textWrappingMode=TextWrappingModes.Normal;}
        Set(node("RankList/ScrollView"),0,0,676,496);
        Set(view.scroll.viewport,0,0,660,496);
        Set((RectTransform)view.scroll.verticalScrollbar.transform,668,0,8,496);
        Set(node("RankList/ScrollView/Scrollbar/TrackRail"),2,0,4,496);
        foreach (var row in view.rows)
        {
            ((RectTransform)row.root.transform).sizeDelta = new Vector2(660,64);
            var element=row.root.GetComponent<LayoutElement>();
            if(element){ element.minWidth=-1; element.preferredWidth=660; element.minHeight=element.preferredHeight=64; }
            Set(row.position.rectTransform,8,12,58,40); Format(row.position,26,TextAlignmentOptions.Midline);
            Set((RectTransform)row.avatar.transform.parent,76,10,44,44);
            Set(row.username.rectTransform,132,1,224,34); Format(row.username,22,TextAlignmentOptions.MidlineLeft);
            Set(row.uid.rectTransform,132,34,224,26); Format(row.uid,16,TextAlignmentOptions.MidlineLeft); row.uid.gameObject.SetActive(true);
            Set(row.detail.rectTransform,370,12,100,40); Format(row.detail,21,TextAlignmentOptions.Midline);
            Set(row.score.rectTransform,486,12,158,40); Format(row.score,21,TextAlignmentOptions.MidlineRight);
            row.showUnits=true;
            row.surface.shape=SettingsShapeGraphic.Shape.Square;row.surface.radius=0;
        }
        Set(view.myPosition.rectTransform,8,30,58,40);Format(view.myPosition,26,TextAlignmentOptions.Midline);
        Set(node("MyRank/MyRankLabel"),4,7,66,24);
        var myLabel=node("MyRank/MyRankLabel").GetComponent<TMP_Text>();Format(myLabel,16,TextAlignmentOptions.Midline); myLabel.enableAutoSizing=true;myLabel.fontSizeMin=12;myLabel.fontSizeMax=16;
        var myLocale=myLabel.GetComponent<LocalizedPlatformLabel>();if(myLocale)myLocale.minimumSize=10;
        Set(node("MyRank/PortraitPlate"),76,16,44,44);Set(view.myAvatar.rectTransform,76,16,44,44);
        Set(view.myName.rectTransform,132,7,224,34);Format(view.myName,22,TextAlignmentOptions.MidlineLeft);
        Set(view.myHint.rectTransform,132,40,224,26);Format(view.myHint,16,TextAlignmentOptions.MidlineLeft);view.myHint.gameObject.SetActive(true);
        Set(view.myGrade.rectTransform,370,18,100,40);Format(view.myGrade,21,TextAlignmentOptions.Midline);
        Set(view.myScore.rectTransform,486,18,158,40);Format(view.myScore,21,TextAlignmentOptions.MidlineRight);
        view.myPlayer=node("MyRank").GetComponent<Button>();
        if (!view.myPlayer) view.myPlayer=node("MyRank").gameObject.AddComponent<Button>();
        view.myPlayer.targetGraphic=node("MyRank").GetComponent<SettingsShapeGraphic>();
        view.myPlayer.targetGraphic.raycastTarget=true;
        var player=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<PlayerInfoPanel>(true)).Single();
        var playerCanvas=player.GetComponentInParent<Canvas>(true);UiCanvasLayers.Apply(playerCanvas, UiCanvasLayers.Overlay, UiCanvasLayers.PlayerInfoOrder);EditorUtility.SetDirty(playerCanvas);
        Set(node("MyRank/TopRule"),0,0,660,2);
        foreach (var t in new[]{view.stateTitle,view.stateDescription}) t.rectTransform.sizeDelta=new Vector2(620,t.rectTransform.sizeDelta.y);
        foreach (var shape in new[]{node("Panel"),node("Header"),node("Close"),node("MyRank")}.Select(t=>t.GetComponent<SettingsShapeGraphic>()))
        {shape.shape=SettingsShapeGraphic.Shape.Square;shape.radius=0;}
        var textBindings=palette.texts.Where(t=>t.text).ToList();
        textBindings.RemoveAll(t=>t.text==view.myHint);
        textBindings.Add(new LeaderboardPaletteEditor.TextBinding { text=view.myHint,fill=LeaderboardPaletteEditor.ColorSlot.Text,against=LeaderboardPaletteEditor.ColorSlot.Mine,readable=true,muted=true });
        palette.texts=textBindings.ToArray();
        palette.shapes=palette.shapes.Where(s=>s.graphic).ToArray();
        palette.SetBakedDefault();layout.ApplyColors();
        BakeOptions(plans);
        EditorUtility.SetDirty(controller);EditorUtility.SetDirty(view);EditorUtility.SetDirty(layout);EditorUtility.SetDirty(palette);
    }

    public static void BakeOptions(InterfacePlansController plans)
    {
        const int retiredLayoutOption = 8;
        var oldLayout = plans.pages[1].transform.Find("RankingLayout");
        if (oldLayout) UnityEngine.Object.DestroyImmediate(oldLayout.gameObject);
        plans.choices = plans.choices.Where(c => (int)c.option != retiredLayoutOption).ToArray();
        AddChoices(plans,DesignOption.RankingPalette,"排行榜配色",LeaderboardPaletteEditor.PresetNames,4,plans.SelectRankingPalette);
        var page=plans.pages[1].transform;
        var rows=plans.choices.Where(c=>c.buttons.Length>0&&c.buttons[0]&&c.buttons[0].transform.parent.parent==page)
            .Select(c=>c.buttons[0].transform.parent as RectTransform).Distinct().OrderBy(r=>r.GetSiblingIndex()).ToArray();
        float top=0;
        foreach(var row in rows){Set(row,24,top,552,row.sizeDelta.y);top+=row.sizeDelta.y;}
        plans.pageHeights[1]=top+128;
        var presets=plans.presetDefaults.Where(p=>(int)p.option!=retiredLayoutOption&&p.option!=DesignOption.RankingPalette).ToList();
        presets.Add(new DesignPresetChoice{option=DesignOption.RankingPalette,value=LeaderboardPaletteEditor.DefaultPreset});
        plans.presetDefaults=presets.OrderBy(p=>Array.IndexOf(InterfacePlansController.RestorableDesignOptions,p.option)).ToArray();EditorUtility.SetDirty(plans);
    }
    static void AddChoices(InterfacePlansController plans,DesignOption option,string title,string[] labels,int columns,UnityAction<int> callback)
    {
        var parent=plans.pages[1].transform;
        var previous=parent.Find(option.ToString());if(previous)UnityEngine.Object.DestroyImmediate(previous.gameObject);
        var row=new GameObject(option.ToString(),typeof(RectTransform)).GetComponent<RectTransform>();row.SetParent(parent,false);
        Set(row,24,0,552,labels.Length>columns?108:60);
        var template=plans.choices.First(c=>c.option==DesignOption.Settings).buttons[0];
        var sourceLabel=template.transform.parent.GetComponentsInChildren<TMP_Text>(true).First(t=>t.transform.parent==template.transform.parent);
        var caption=UnityEngine.Object.Instantiate(sourceLabel,row);caption.name="Title";Set(caption.rectTransform,0,5,118,40);SetLabel(caption,title);
        Format(caption,18,TextAlignmentOptions.MidlineLeft);
        var buttons=new Button[labels.Length];float width=columns==3?138:102;
        for(int i=0;i<buttons.Length;i++)
        {
            var b=UnityEngine.Object.Instantiate(template,row);b.name="Option"+i;b.gameObject.SetActive(true);
            Set((RectTransform)b.transform,122+i%columns*(width+6),5+i/columns*46,width,40);
            var text=b.GetComponentInChildren<TMP_Text>(true);SetLabel(text,labels[i]);
            Set(text.rectTransform,0,0,width,40);Format(text,18,TextAlignmentOptions.Midline);
            b.onClick=new Button.ButtonClickedEvent();UnityEventTools.AddIntPersistentListener(b.onClick,callback,i);buttons[i]=b;
        }
        plans.choices=plans.choices.Where(c=>c.option!=option).Concat(new[]{new DesignChoiceGroup{option=option,buttons=buttons,values=Enumerable.Range(0,labels.Length).ToArray()}}).ToArray();
    }
    static void SetLabel(TMP_Text text,string value){text.text=value;var label=text.GetComponent<LocalizedPlatformLabel>();if(label)label.key=value;}
    static void Format(TMP_Text text,float size,TextAlignmentOptions alignment)
    {
        text.fontSize=size;text.enableAutoSizing=false;text.fontStyle=FontStyles.Normal;text.fontWeight=FontWeight.Regular;
        text.textWrappingMode=TextWrappingModes.NoWrap;text.overflowMode=TextOverflowModes.Ellipsis;text.alignment=alignment;
        var locale=text.GetComponent<LocalizedPlatformLabel>();
        if(locale){locale.authoredSize=size;locale.minimumSize=Mathf.Min(size,14);locale.authoredAutoSize=false;}
        var font=text.GetComponent<PlatformFontBinding>();
        if(font){font.authoredStyle=FontStyles.Normal;font.authoredWeight=FontWeight.Regular;font.styleCaptured=true;}
    }
    static void BakeTitle(TMP_Text title)
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/font/思源黑体/SourceHanSansSC-Regular SDF.asset");
        if (!font) throw new InvalidOperationException("Leaderboard title font is missing.");
        Set(title.rectTransform,64,12,780,56);
        title.rectTransform.localScale = Vector3.one;
        title.rectTransform.localRotation = Quaternion.identity;
        title.font = font; title.fontSharedMaterial = font.material;
        title.margin = Vector4.zero; title.characterSpacing = 0; title.raycastTarget = false;
        Format(title,32,TextAlignmentOptions.MidlineGeoAligned);
        var binding = title.GetComponent<PlatformFontBinding>();
        if (!binding) binding = title.gameObject.AddComponent<PlatformFontBinding>();
        binding.authoredFont = font; binding.authoredMaterial = font.material;
        binding.authoredStyle = FontStyles.Normal; binding.authoredWeight = FontWeight.Regular; binding.styleCaptured = true;
        binding.preserveAuthoredTypeface = false; binding.preserveAuthoredChineseFace = false;
        binding.Apply(); EditorUtility.SetDirty(binding);
    }

    public static void Validate(Scene scene)
    {
        var controller = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LeaderboardController>(true)).Single();
        var view = controller.paletteEditor.view;
        var layout = controller.GetComponent<LeaderboardRuleLayout>();
        if (controller.views.Length != 1 || controller.views[0] != view || !layout)
            throw new InvalidOperationException("Leaderboard must have one compact rule rail.");
        foreach (string name in new[] { "RuleMenuButton", "RuleMenuOverlay", "RuleRail", "SidebarBackground", "Refresh" })
            if (view.transform.Find(name)) throw new InvalidOperationException("Obsolete leaderboard object remains: " + name);
        if (!layout.sideRoot.gameObject.activeSelf || !layout.innerDivider.gameObject.activeSelf
            || !layout.ruleIds.OrderBy(r=>r).SequenceEqual(LeaderboardRules.Ids.OrderBy(r=>r))
            || layout.sideButtons.Length != layout.ruleIds.Length || layout.sideButtons.Any(b=>!b))
            throw new InvalidOperationException("Leaderboard compact rule rail is incomplete.");
        var title = view.transform.Find("Title").GetComponent<TMP_Text>();
        if (title.rectTransform.anchoredPosition != new Vector2(64,-12) || title.rectTransform.sizeDelta != new Vector2(780,56)
            || title.fontStyle != FontStyles.Normal || title.fontWeight != FontWeight.Regular || title.fontSize != 32
            || title.alignment != TextAlignmentOptions.MidlineGeoAligned)
            throw new InvalidOperationException("Leaderboard title must be centered and use normal-weight text.");
    }
    static void Set(RectTransform rect,float x,float y,float w,float h){rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(w,h);}
}
#endif
