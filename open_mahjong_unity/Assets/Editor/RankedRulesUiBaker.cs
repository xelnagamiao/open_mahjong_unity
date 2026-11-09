using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Incremental authoring tool. Never replaces the original MainCanvas or header.</summary>
public static class RankedRulesUiBaker {
    public const string ScenePath="Assets/Scenes/MainScene.unity";
    const string SolidFontPath="Assets/font/思源黑体/SourceHanSansSC-Regular SDF.asset";
    static T Find<T>() where T:Component => Resources.FindObjectsOfTypeAll<T>().First(x=>x.gameObject.scene==SceneManager.GetActiveScene());
    static T Ref<T>(Object target,string field) where T:Object => (T)new SerializedObject(target).FindProperty(field).objectReferenceValue;
    static void Set(Object target,string field,Object value){var so=new SerializedObject(target);so.FindProperty(field).objectReferenceValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
    static void Array(Object target,string field,Object[] values){var so=new SerializedObject(target);var p=so.FindProperty(field);p.arraySize=values.Length;for(int i=0;i<values.Length;i++)p.GetArrayElementAtIndex(i).objectReferenceValue=values[i];so.ApplyModifiedPropertiesWithoutUndo();}
    static void Number(Object target,string field,int value){var so=new SerializedObject(target);so.FindProperty(field).intValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
    static RectTransform Rect(Transform parent,string name){var t=parent.Find(name) as RectTransform;if(t)return t;var go=new GameObject(name,typeof(RectTransform));go.layer=5;go.transform.SetParent(parent,false);return (RectTransform)go.transform;}
    static void Place(RectTransform r,float x,float y,float w,float h){r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);r.localScale=Vector3.one;}
    static void Stretch(RectTransform r){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;r.localScale=Vector3.one;}
    static TMP_FontAsset font;
    static TMP_Text Text(Transform parent,string name,string value,int size,Color color){var r=Rect(parent,name);var t=r.GetComponent<TextMeshProUGUI>()??r.gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.fontSharedMaterial=font.material;t.text=value;t.fontSize=size;t.enableAutoSizing=false;t.color=color;t.raycastTarget=false;return t;}
    static Button Button(Transform parent,string name,string caption,int size=24){var r=Rect(parent,name);var image=r.GetComponent<Image>()??r.gameObject.AddComponent<Image>();image.color=new Color(.22f,.24f,.28f,1);var b=r.GetComponent<Button>()??r.gameObject.AddComponent<Button>();b.targetGraphic=image;var label=Text(r,"Label",caption,size,Color.white);Stretch(label.rectTransform);label.alignment=TextAlignmentOptions.Center;return b;}
    static void Width(Transform t,float w){var le=t.GetComponent<LayoutElement>()??t.gameObject.AddComponent<LayoutElement>();le.minWidth=le.preferredWidth=w;le.flexibleWidth=0;}
    static void Connect(Button b,UnityEngine.Events.UnityAction action){b.onClick=new Button.ButtonClickedEvent();UnityEventTools.AddPersistentListener(b.onClick,action);}

    [MenuItem("Tools/Mahjong/Main Scene/Bake Ranked Rules UI")]
    public static void Bake(){
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=ScenePath)throw new InvalidOperationException("Open the main scene outside Play Mode first.");
        foreach(var root in scene.GetRootGameObjects())Undo.RegisterFullObjectHierarchyUndo(root,"Bake ranked rule UI");
        font=Find<UserContainer>().GetComponentsInChildren<TMP_Text>(true).First().font;
        var policy=BakePolicy();
        DataPageUiBaker.Bake(scene);BakeProfile();BakeMatch(policy);BakeHome();BakeEloHelp();
        foreach(var panel in Resources.FindObjectsOfTypeAll<RankChangePanel>().Where(x=>x.gameObject.scene==scene)){
            var heading=panel.GetComponentsInChildren<TMP_Text>(true).First(t=>t.text=="段位结算");Set(panel,"resultTitle",heading);
            var text=Ref<TMP_Text>(panel,"ptChangeText");text.enableAutoSizing=true;text.fontSizeMin=22;text.fontSizeMax=36;text.textWrappingMode=TextWrappingModes.Normal;
            foreach(string field in new[]{"ptChangeText","rankNameText","scoreText","resultTitle"}){var t=Ref<TMP_Text>(panel,field);t.font=font;t.fontSharedMaterial=font.material;}
        }
        Validate();
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        Debug.Log("Ranked rules baked into MainScene.");
    }

    [MenuItem("Tools/Mahjong/Main Scene/Update Elo Help")]
    public static void UpdateEloHelp(){
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=ScenePath)throw new InvalidOperationException("Open the main scene outside Play Mode first.");
        Undo.RegisterFullObjectHierarchyUndo(Find<MatchLobbyView>().gameObject,"Bake Elo help");
        Undo.RegisterFullObjectHierarchyUndo(Find<RankPolicyPanel>().gameObject,"Update Elo description");
        font=Find<UserContainer>().GetComponentsInChildren<TMP_Text>(true).First().font;
        BakeEloHelp();
        Ref<GameObject>(Find<RankPolicyPanel>(),"eloContent").GetComponent<TMP_Text>().text=RankedRules.EloDescription;
        Validate();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
    }

    static void BakeEloHelp(){
        var lobby=Find<MatchLobbyView>();var summary=lobby.transform.Find("MatchLayout/HeaderBar/RankSummary");
        var points=Ref<TMP_Text>(lobby,"pointsText").rectTransform;
        points.anchorMax=new Vector2(.93f,points.anchorMax.y);
        var button=Button(summary,"EloHelp","?",26);
        var r=(RectTransform)button.transform;r.anchorMin=r.anchorMax=new Vector2(1,.65f);r.pivot=new Vector2(1,.5f);r.anchoredPosition=new Vector2(-5,0);r.sizeDelta=new Vector2(38,38);
        var help=button.GetComponent<RankedEloHelp>();if(!help)help=button.gameObject.AddComponent<RankedEloHelp>();
        MatchCardLayoutBaker.BakeClickHelp(lobby.gameObject.scene);
    }

    static RankPolicyPanel BakePolicy(){
        var overlay=SceneManager.GetActiveScene().GetRootGameObjects().First(x=>x.name=="OverlayCanvas").transform;
        var root=Rect(overlay,"RankPolicyPanel");Stretch(root);
        var shade=root.GetComponent<Image>()??root.gameObject.AddComponent<Image>();shade.color=new Color(0,0,0,.65f);shade.raycastTarget=true;
        var box=Rect(root,"Dialog");box.anchorMin=box.anchorMax=box.pivot=new Vector2(.5f,.5f);box.anchoredPosition=Vector2.zero;box.sizeDelta=new Vector2(1120,770);
        var bg=box.GetComponent<Image>()??box.gameObject.AddComponent<Image>();bg.color=new Color(.93f,.94f,.96f);
        var title=Text(box,"Title","段位与匹配说明",36,new Color(.12f,.15f,.2f));Place(title.rectTransform,40,28,930,60);
        var close=Button(box,"Close","关闭",24);Place((RectTransform)close.transform,976,30,104,52);
        var body=Text(box,"Policy",RankedRules.Policy,28,new Color(.15f,.18f,.23f));Place(body.rectTransform,44,112,1032,610);body.textWrappingMode=TextWrappingModes.Normal;body.lineSpacing=5;
        var elo=Text(box,"Elo",RankedRules.EloDescription,30,new Color(.15f,.18f,.23f));Place(elo.rectTransform,44,112,1032,610);elo.textWrappingMode=TextWrappingModes.Normal;elo.lineSpacing=8;
        var panel=root.GetComponent<RankPolicyPanel>()??root.gameObject.AddComponent<RankPolicyPanel>();
        Set(panel,"closeButton",close);Set(panel,"policyContent",body.gameObject);Set(panel,"eloContent",elo.gameObject);Set(panel,"title",title);
        body.gameObject.SetActive(true);elo.gameObject.SetActive(false);root.gameObject.SetActive(false);return panel;
    }

    static void BakeHome(){
        var user=Find<UserContainer>();Set(user,"rankSwitchButton",user.transform.Find("SwitchButton").GetComponent<Button>());
        var rank=Ref<TMP_Text>(user,"rankText");rank.text="国标麻将 · 10级";rank.fontSize=24;rank.enableAutoSizing=true;rank.fontSizeMin=20;rank.fontSizeMax=24;
        user.transform.Find("Text (TMP)").GetComponent<TMP_Text>().text="玩家段位";
        var score=Ref<TMP_Text>(user,"rankScoreText");score.text="0 / 20 PT";score.font=font;score.fontSharedMaterial=font.material;score.fontSize=20;score.enableAutoSizing=false;score.textWrappingMode=TextWrappingModes.Normal;
        // Elo replaces both the progress bar and its small PT caption with one prominent value.
        var elo=Text(user.transform,"EloScoreText","R 1500",32,score.color);
        var rect=elo.rectTransform;var source=score.rectTransform;
        rect.anchorMin=source.anchorMin;rect.anchorMax=source.anchorMax;rect.pivot=source.pivot;
        rect.anchoredPosition=new Vector2(source.anchoredPosition.x,-63);rect.sizeDelta=new Vector2(source.sizeDelta.x,70);
        elo.alignment=TextAlignmentOptions.Center;elo.enableAutoSizing=true;elo.fontSizeMin=24;elo.fontSizeMax=32;
        elo.textWrappingMode=TextWrappingModes.NoWrap;elo.overflowMode=TextOverflowModes.Ellipsis;
        elo.gameObject.SetActive(false);Set(user,"eloScoreText",elo);
    }

    [MenuItem("Tools/Mahjong/Main Scene/Update Home Rank Layout")]
    public static void UpdateHomeRankLayout(){
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=ScenePath)throw new InvalidOperationException("Open the main scene outside Play Mode first.");
        Undo.RegisterFullObjectHierarchyUndo(Find<UserContainer>().gameObject,"Update home rank layout");
        font=Find<UserContainer>().GetComponentsInChildren<TMP_Text>(true).First().font;
        BakeHome();
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
    }

    [MenuItem("Tools/Mahjong/Main Scene/Update Player Info Layout")]
    public static void UpdatePlayerInfoLayout(){
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=ScenePath)throw new InvalidOperationException("Open the main scene outside Play Mode first.");
        Undo.RegisterFullObjectHierarchyUndo(Find<PlayerInfoPanel>().gameObject,"Repair player info layout");
        font=Find<UserContainer>().GetComponentsInChildren<TMP_Text>(true).First().font;
        BakeProfile();
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
    }

    [MenuItem("Tools/Mahjong/Main Scene/Update Player Info Rule Picker")]
    public static void UpdatePlayerInfoRulePicker(){
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=ScenePath)throw new InvalidOperationException("Open the main scene outside Play Mode first.");
        var panel=Find<PlayerInfoPanel>();
        Undo.RegisterFullObjectHierarchyUndo(panel.gameObject,"Repair player info rule picker");
        BakeProfileRulePicker(panel,Ref<RectTransform>(panel,"rulesSection"));
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
    }

    static void BakeProfile(){
        var panel=Find<PlayerInfoPanel>();var rules=Ref<RectTransform>(panel,"rulesSection");
        AuthorTrendLabels(panel);
        var statistics=Ref<PlayerInfoStatistics>(panel,"statistics");var statContent=Ref<RectTransform>(statistics,"content");if(!statContent.GetComponent<LayoutElement>())statContent.gameObject.AddComponent<LayoutElement>();
        BakeProfileRulePicker(panel,rules);
        var hand=Ref<PlayerInfoHandView>(panel,"handView");var empty=Text(hand.transform,"NoRecentWin","无最近大和",24,new Color(.76f,.8f,.88f));Stretch(empty.rectTransform);empty.alignment=TextAlignmentOptions.MidlineLeft;Set(panel,"emptyWinText",empty);empty.gameObject.SetActive(false);
        var caption=Ref<TMP_Text>(panel,"winLabel");caption.rectTransform.anchorMin=new Vector2(0,1);caption.rectTransform.anchorMax=new Vector2(1,1);caption.rectTransform.pivot=new Vector2(.5f,1);caption.rectTransform.anchoredPosition=new Vector2(76,0);caption.rectTransform.sizeDelta=new Vector2(-152,32);caption.textWrappingMode=TextWrappingModes.NoWrap;caption.enableAutoSizing=true;caption.fontSizeMin=14;caption.fontSizeMax=24;
        var rank=Ref<TMP_Text>(panel,"rankText");rank.text="国标 · 10级";rank.fontSize=28;rank.enableAutoSizing=true;rank.fontSizeMin=22;rank.fontSizeMax=28;rank.textWrappingMode=TextWrappingModes.NoWrap;rank.overflowMode=TextOverflowModes.Ellipsis;
        var score=Ref<TMP_Text>(panel,"rankScoreText");score.text="0 / 20 PT";score.font=font;score.fontSharedMaterial=font.material;score.fontSize=24;score.enableAutoSizing=true;score.fontSizeMin=18;score.fontSizeMax=24;score.textWrappingMode=TextWrappingModes.NoWrap;score.overflowMode=TextOverflowModes.Ellipsis;
        PlayerInfoAppearanceBaker.BakeLayout(panel);
    }

    static void BakeProfileRulePicker(PlayerInfoPanel panel,RectTransform rules){
        var existing=rules.GetComponentsInChildren<Button>(true).Where(x=>x.name.StartsWith("Rule_")).ToList();
        foreach(var button in existing)button.gameObject.SetActive(false);
        var buttons=new List<Button>();
        for(int i=0;i<PlayerInfoRuleCatalog.PrimaryRules.Length;i++){
            string id=PlayerInfoRuleCatalog.PrimaryRules[i];
            var b=rules.Find("Rule_"+id)?.GetComponent<Button>();
            if(!b){b=Object.Instantiate(existing[0],rules,false);b.name="Rule_"+id;}
            buttons.Add(b);b.onClick=new Button.ButtonClickedEvent();b.gameObject.SetActive(true);
            Place((RectTransform)b.transform,i*128,0,120,44);
            var label=b.GetComponentInChildren<TMP_Text>(true);Stretch(label.rectTransform);
            label.rectTransform.offsetMin=new Vector2(12,0);label.rectTransform.offsetMax=new Vector2(-12,0);
            label.text=RankedRules.Name(id);label.fontSize=22;label.enableAutoSizing=false;
            label.alignment=TextAlignmentOptions.Center;label.textWrappingMode=TextWrappingModes.Normal;
            label.overflowMode=TextOverflowModes.Ellipsis;label.raycastTarget=false;
        }
        Array(panel,"ruleButtons",buttons.Cast<Object>().ToArray());
        var other=Ref<TMP_Dropdown>(panel,"otherRulesDropdown");Place((RectTransform)other.transform,512,0,240,44);
        foreach(var label in new[]{other.captionText,other.placeholder as TMP_Text}.Where(t=>t)){
            Stretch(label.rectTransform);label.rectTransform.offsetMin=new Vector2(14,0);label.rectTransform.offsetMax=new Vector2(-44,0);
            label.fontSize=22;label.enableAutoSizing=true;label.fontSizeMin=18;label.fontSizeMax=22;label.alignment=TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode=TextWrappingModes.NoWrap;label.overflowMode=TextOverflowModes.Ellipsis;
        }
        var template=other.template;template.anchorMin=new Vector2(0,0);template.anchorMax=new Vector2(1,0);template.pivot=new Vector2(.5f,1);
        template.anchoredPosition=new Vector2(0,-4);template.sizeDelta=new Vector2(0,template.sizeDelta.y);
        var dropdownScroll=template.GetComponent<ScrollRect>();dropdownScroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.Permanent;
        Stretch(dropdownScroll.viewport);dropdownScroll.viewport.offsetMax=new Vector2(-14,0);
        other.itemText.textWrappingMode=TextWrappingModes.NoWrap;other.itemText.overflowMode=TextOverflowModes.Ellipsis;
        other.itemText.enableAutoSizing=true;other.itemText.fontSizeMin=18;other.itemText.fontSizeMax=22;
        other.ClearOptions();other.AddOptions(new List<string>{"其他规则",PlayerInfoRuleCatalog.Name(RiichiSanmaRankConfig.Rule),"川麻血流换三张"});other.SetValueWithoutNotify(0);
        other.template.gameObject.SetActive(false);other.gameObject.SetActive(true);
        var policyButton=rules.Find("RankPolicyButton");if(policyButton)policyButton.gameObject.SetActive(false);
    }

    static void FitRankedTabLabel(TMP_Text label){label.enableAutoSizing=true;label.fontSizeMin=15;label.fontSizeMax=24;label.textWrappingMode=TextWrappingModes.NoWrap;}

    [MenuItem("Tools/Mahjong/Main Scene/Update Ranked Tab Sizing")]
    public static void UpdateRankedTabSizing(){
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=ScenePath)throw new InvalidOperationException("Open the main scene outside Play Mode first.");
        var tabs=Find<MatchLobbyView>().transform.Find("MatchLayout/HeaderBar/RuleTabs");
        foreach(Transform tab in tabs){var label=tab.Find("Label")?.GetComponent<TMP_Text>();if(!label)continue;Undo.RecordObject(label,"Fit ranked tab label");FitRankedTabLabel(label);}
        Validate();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
    }

    static void BakeMatch(RankPolicyPanel policy){
        var lobby=Find<MatchLobbyView>();var layout=lobby.transform.Find("MatchLayout");
        var tabs=layout.Find("HeaderBar/RuleTabs");var tab0=tabs.Find("Rule_0").GetComponent<Button>();
        var buttons=new Button[RankedRules.Ids.Length];var selected=new GameObject[RankedRules.Ids.Length];
        for(int i=0;i<buttons.Length;i++){
            buttons[i]=tabs.Find("Rule_"+i)?.GetComponent<Button>();if(!buttons[i]){buttons[i]=Object.Instantiate(tab0,tabs,false);buttons[i].name="Rule_"+i;}
            Width(buttons[i].transform,141);buttons[i].onClick=new Button.ButtonClickedEvent();var tabLabel=buttons[i].GetComponentInChildren<TMP_Text>(true);tabLabel.text=RankedRules.Ids[i]==RankedRules.XueliuExchangeRule?"血流换三张":RankedRules.Names[i];FitRankedTabLabel(tabLabel);
            selected[i]=buttons[i].transform.Find("SelectedBackground").gameObject;selected[i].SetActive(i==0);
        }
        Width(tabs,RankedRules.Ids.Length*147-6);Width(layout.Find("HeaderBar/RankSummary"),1512-(RankedRules.Ids.Length*147-6));
        Array(lobby,"rulePlayerCounts",buttons.Select(b=>b.transform.Find("PlayerCount/Count").GetComponent<TMP_Text>()).ToArray());
        var oldAbout=layout.Find("Title/RankPolicyButton");if(oldAbout)oldAbout.SetParent(layout,false);
        var groupedAbout=layout.Find("MatchTools/RankPolicyButton");if(groupedAbout)groupedAbout.SetParent(layout,false);
        var about=Button(layout,"RankPolicyButton","段位机制说明",22);Place((RectTransform)about.transform,1294,52,230,36);
        var aboutLayout=about.GetComponent<LayoutElement>()??about.gameObject.AddComponent<LayoutElement>();aboutLayout.ignoreLayout=true;Connect(about,policy.Show);
        var pages=layout.Find("RulePages");var national=pages.Find("Guobiao").gameObject;
        var riichi=pages.Find("Riichi")?.gameObject;if(!riichi){riichi=Object.Instantiate(national,pages,false);riichi.name="Riichi";}
        foreach(var card in riichi.GetComponentsInChildren<MatchButton>(true)){
            var so=new SerializedObject(card);int mode=so.FindProperty("gameType").intValue;int tier=so.FindProperty("tier").intValue;
            card.gameObject.SetActive(tier<3&&mode<2);Number(card,"rule",(int)MatchButton.MatchRule.Riichi);
            foreach(var t in card.GetComponentsInChildren<TMP_Text>(true))t.text=t.text.Replace("国标麻将","立直麻将");
            Set(card,"lobbyView",lobby);Set(card,"policyPanel",policy);
        }
        foreach(Transform column in riichi.transform.Find("Row")){
            if(column.GetComponentsInChildren<MatchButton>(true).All(c=>!c.gameObject.activeSelf))column.gameObject.SetActive(false);
            var group=column.GetComponent<VerticalLayoutGroup>();if(group)group.childAlignment=TextAnchor.UpperCenter;
        }
        var template=national.GetComponentsInChildren<MatchButton>(true).First();
        GameObject EloPage(string name,MatchButton.MatchRule rule){
            var page=Rect(pages,name);Stretch(page);var grid=Rect(page,"Cards");Place(grid,0,0,1140,180);
            var group=grid.GetComponent<HorizontalLayoutGroup>()??grid.gameObject.AddComponent<HorizontalLayoutGroup>();group.spacing=14;group.childControlWidth=group.childControlHeight=true;group.childForceExpandWidth=group.childForceExpandHeight=false;
            ConfigureEloCards(page,rule,lobby,policy,template);
            page.gameObject.SetActive(false);return page.gameObject;
        }
        var qingque=EloPage("Qingque",MatchButton.MatchRule.Qingque);var sichuan=EloPage("Sichuan",MatchButton.MatchRule.Sichuan);
        var exchange=EloPage("SichuanXueliuExchange",MatchButton.MatchRule.SichuanXueliuExchange);
        var hongque=EloPage("Hongque",MatchButton.MatchRule.Hongque);
        var doubles=EloPage("SichuanDoubles",MatchButton.MatchRule.SichuanDoubles);
        var sanma=pages.Find("RiichiSanma")?.gameObject;
        if(!sanma){sanma=Object.Instantiate(riichi,pages,false);sanma.name="RiichiSanma";}
        foreach(var card in sanma.GetComponentsInChildren<MatchButton>(true)){
            Number(card,"rule",(int)MatchButton.MatchRule.RiichiSanma);
            Set(card,"lobbyView",lobby);Set(card,"policyPanel",policy);
            foreach(var t in card.GetComponentsInChildren<TMP_Text>(true)) t.text=t.text.Replace("立直麻将","立直三麻");
        }
        sanma.SetActive(false);
        national.SetActive(true);riichi.SetActive(false);
        Array(lobby,"rulePages",new Object[]{national,riichi,qingque,sichuan,sanma,exchange,hongque,doubles});Array(lobby,"ruleButtons",buttons);Array(lobby,"selectedTabs",selected);
        var entries=new[]{national,riichi,qingque,sichuan,sanma,exchange,hongque,doubles}.SelectMany(p=>p.GetComponentsInChildren<MatchButton>(true)).Where(c=>c.IsAvailable).ToArray();Array(lobby,"entries",entries);Array(lobby.GetComponent<MatchPanel>(),"matchButtons",entries);
        foreach(var card in entries)Set(card,"policyPanel",policy);
        Ref<TMP_Text>(lobby,"rankText").text="国标麻将 · 10级";Ref<TMP_Text>(lobby,"pointsText").text="0 / 20 PT";
        BakeMatchNavigation();
    }

    [MenuItem("Tools/Mahjong/Main Scene/Update Ranked Rule Groups")]
    public static void UpdateMatchNavigation(){
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=ScenePath)throw new InvalidOperationException("Open the main scene outside Play Mode first.");
        Undo.RegisterFullObjectHierarchyUndo(Find<MatchLobbyView>().gameObject,"Group ranked rule navigation");
        BakeMatchNavigation();
        Validate();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
    }

    [MenuItem("Tools/Mahjong/Main Scene/Add Hongque Elo Match")]
    public static void UpdateHongqueMatch(){
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=ScenePath)throw new InvalidOperationException("Open the main scene outside Play Mode first.");
        var lobby=Find<MatchLobbyView>();var policy=Find<RankPolicyPanel>();
        Undo.RegisterFullObjectHierarchyUndo(lobby.gameObject,"Add Hongque Elo match");
        Undo.RegisterFullObjectHierarchyUndo(policy.gameObject,"Add Hongque Elo description");
        var pages=lobby.transform.Find("MatchLayout/RulePages");
        var hongque=pages.Find("Hongque");
        bool addedPage=!hongque;
        if(!hongque){hongque=Object.Instantiate(pages.Find("Qingque"),pages,false);hongque.name="Hongque";}
        ConfigureEloCards(hongque,MatchButton.MatchRule.Hongque,lobby,policy);
        // Keep only the two supported Hongque match lengths.
        foreach(var unused in hongque.GetComponentsInChildren<MatchButton>(true).Where(c=>!c.IsAvailable).ToArray())
            Undo.DestroyObjectImmediate(unused.gameObject);
        hongque.gameObject.SetActive(false);
        Array(lobby,"rulePages",new[]{"Guobiao","Riichi","Qingque","Sichuan","RiichiSanma","SichuanXueliuExchange","Hongque"}
            .Select(name=>(Object)pages.Find(name).gameObject).ToArray());
        var entries=pages.GetComponentsInChildren<MatchButton>(true).Where(c=>c.IsAvailable).ToArray();
        Array(lobby,"entries",entries);Array(lobby.GetComponent<MatchPanel>(),"matchButtons",entries);
        Ref<GameObject>(policy,"eloContent").GetComponent<TMP_Text>().text=RankedRules.EloDescription;
        if(addedPage)BakeMatchNavigation();
        Validate();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
    }

    static void BakeMatchNavigation(){
        var lobby=Find<MatchLobbyView>();var layout=lobby.transform.Find("MatchLayout");
        var tabs=layout.Find("HeaderBar/RuleTabs");
        var pageLayout=layout.GetComponent<VerticalLayoutGroup>();
        pageLayout.childAlignment=TextAnchor.UpperCenter;pageLayout.padding.top=52;
        font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SolidFontPath);
        if(!font)throw new Exception("匹配页常规字体缺失");
        var buttons=new Button[RankedRules.FamilyNames.Length];var selected=new GameObject[buttons.Length];
        for(int i=0;i<buttons.Length;i++){
            var button=tabs.Find("Rule_"+i)?.GetComponent<Button>();
            if(!button){button=Object.Instantiate(tabs.Find("Rule_0").GetComponent<Button>(),tabs,false);button.name="Rule_"+i;}
            buttons[i]=button;button.gameObject.SetActive(true);
            button.onClick=new Button.ButtonClickedEvent();Width(button.transform,186);
            var label=button.transform.Find("Label").GetComponent<TMP_Text>();
            label.text=RankedRules.FamilyNames[i];label.font=font;label.fontSharedMaterial=font.material;FitRankedTabLabel(label);
            selected[i]=button.transform.Find("SelectedBackground").gameObject;selected[i].SetActive(i==0);
        }
        foreach(Transform tab in tabs)if(!buttons.Any(b=>b.transform==tab))tab.gameObject.SetActive(false);
        float tabsWidth=buttons.Length*186+(buttons.Length-1)*6;
        Width(tabs,tabsWidth);Width(layout.Find("HeaderBar/RankSummary"),1512-tabsWidth);
        var rank=Ref<TMP_Text>(lobby,"rankText");
        rank.enableAutoSizing=true;rank.fontSizeMin=18;rank.fontSizeMax=26;rank.textWrappingMode=TextWrappingModes.NoWrap;
        var points=Ref<TMP_Text>(lobby,"pointsText");
        points.enableAutoSizing=true;points.fontSizeMin=18;points.fontSizeMax=21;points.textWrappingMode=TextWrappingModes.NoWrap;
        // Reserve a fixed gap for the help button when the summary gives space to another tab.
        points.rectTransform.anchorMax=new Vector2(1,points.rectTransform.anchorMax.y);
        points.rectTransform.offsetMax=new Vector2(-50,points.rectTransform.offsetMax.y);
        Array(lobby,"ruleButtons",buttons);Array(lobby,"selectedTabs",selected);
        var counts=buttons.Select(b=>b.transform.Find("PlayerCount/Count").GetComponent<TMP_Text>()).ToArray();
        foreach(var count in counts)count.text="—";
        Array(lobby,"rulePlayerCounts",counts);

        var tools=Rect(layout,"MatchTools");tools.SetSiblingIndex(0);
        var toolsElement=tools.GetComponent<LayoutElement>()??tools.gameObject.AddComponent<LayoutElement>();
        toolsElement.minHeight=toolsElement.preferredHeight=36;toolsElement.flexibleHeight=0;
        var toolsGroup=tools.GetComponent<HorizontalLayoutGroup>()??tools.gameObject.AddComponent<HorizontalLayoutGroup>();
        toolsGroup.childControlWidth=toolsGroup.childControlHeight=true;toolsGroup.childForceExpandWidth=false;toolsGroup.childForceExpandHeight=true;
        var spacer=Rect(tools,"Spacer");var spacerElement=spacer.GetComponent<LayoutElement>()??spacer.gameObject.AddComponent<LayoutElement>();spacerElement.flexibleWidth=1;
        var about=layout.Find("RankPolicyButton")??tools.Find("RankPolicyButton");about.SetParent(tools,false);
        var aboutElement=about.GetComponent<LayoutElement>();aboutElement.ignoreLayout=false;aboutElement.minHeight=aboutElement.preferredHeight=36;aboutElement.flexibleHeight=0;Width(about,230);

        var bar=Rect(layout,"VariantBar");bar.SetSiblingIndex(layout.Find("HeaderBar").GetSiblingIndex()+1);
        var element=bar.GetComponent<LayoutElement>()??bar.gameObject.AddComponent<LayoutElement>();
        element.minHeight=element.preferredHeight=52;element.flexibleHeight=0;
        var group=bar.GetComponent<HorizontalLayoutGroup>()??bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        group.spacing=8;group.padding=new RectOffset(0,0,2,2);group.childAlignment=TextAnchor.MiddleLeft;
        group.childControlWidth=group.childControlHeight=true;group.childForceExpandWidth=false;group.childForceExpandHeight=true;
        var variants=new Button[3];var variantSelected=new GameObject[3];var labels=new TMP_Text[3];var variantCounts=new TMP_Text[3];
        for(int i=0;i<variants.Length;i++){
            var button=bar.Find("Variant_"+i)?.GetComponent<Button>();
            if(!button){button=Object.Instantiate(buttons[0],bar,false);button.name="Variant_"+i;}
            variants[i]=button;button.gameObject.SetActive(true);button.onClick=new Button.ButtonClickedEvent();Width(button.transform,200);
            button.GetComponent<Image>().color=new Color(.94f,.95f,.95f);
            variantSelected[i]=button.transform.Find("SelectedBackground").gameObject;
            variantSelected[i].GetComponent<Image>().color=new Color(.77f,.83f,.80f);variantSelected[i].SetActive(i==0);
            labels[i]=button.transform.Find("Label").GetComponent<TMP_Text>();labels[i].text=i==0?"四人":"三人";
            labels[i].font=font;labels[i].fontSharedMaterial=font.material;labels[i].enableAutoSizing=true;labels[i].fontSizeMin=20;labels[i].fontSizeMax=24;
            variantCounts[i]=button.transform.Find("PlayerCount/Count").GetComponent<TMP_Text>();variantCounts[i].text="—";
        }
        var hint=Text(bar,"Hint",string.Empty,22,new Color(.28f,.35f,.32f));hint.gameObject.SetActive(false);
        hint.alignment=TextAlignmentOptions.MidlineLeft;hint.textWrappingMode=TextWrappingModes.NoWrap;hint.margin=new Vector4(18,0,0,0);
        var hintLayout=hint.GetComponent<LayoutElement>()??hint.gameObject.AddComponent<LayoutElement>();hintLayout.minWidth=300;hintLayout.flexibleWidth=1;
        Set(lobby,"variantBar",bar.gameObject);Set(lobby,"variantHint",hint);
        Array(lobby,"variantButtons",variants);Array(lobby,"selectedVariants",variantSelected);Array(lobby,"variantLabels",labels);Array(lobby,"variantPlayerCounts",variantCounts);
        bar.gameObject.SetActive(false);
        foreach(var card in lobby.GetComponentsInChildren<MatchButton>(true).Where(c=>c.IsElo))ApplyEloTitleFont(card);
        MatchCardLayoutBaker.Bake(lobby.gameObject.scene);
    }

    static void ApplyEloTitleFont(MatchButton card){
        var solid=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SolidFontPath);
        foreach(var title in card.GetComponentsInChildren<TMP_Text>(true).Where(t=>t.name=="Level")){
            title.font=solid;title.fontSharedMaterial=solid.material;title.fontStyle=FontStyles.Normal;
        }
    }

    static void ConfigureEloCards(Transform page,MatchButton.MatchRule rule,MatchLobbyView lobby,RankPolicyPanel policy,MatchButton template=null){
        var grid=page.Find("Cards");
        var cards=grid.GetComponentsInChildren<MatchButton>(true);
        var types=rule==MatchButton.MatchRule.Hongque
            ?new[]{MatchButton.MatchGameType.Banzhuang,MatchButton.MatchGameType.Quanzhuang}
            :rule==MatchButton.MatchRule.Qingque?new[]{MatchButton.MatchGameType.Quanzhuang,MatchButton.MatchGameType.Unlimited}
            :new[]{rule==MatchButton.MatchRule.Sichuan||rule==MatchButton.MatchRule.SichuanXueliuExchange||rule==MatchButton.MatchRule.SichuanDoubles?MatchButton.MatchGameType.Banzhuang:MatchButton.MatchGameType.Quanzhuang};
        foreach(var existing in cards)existing.gameObject.SetActive(false);
        foreach(var gameType in types){
            var card=cards.FirstOrDefault(c=>new SerializedObject(c).FindProperty("gameType").intValue==(int)gameType);
            if(!card){
                var source=cards.FirstOrDefault()??template;
                if(!source)throw new Exception("Elo 场次模板缺失");
                card=Object.Instantiate(source,grid,false);card.name="Elo_"+gameType;
            }
            card.gameObject.SetActive(true);card.transform.SetAsLastSibling();
            Number(card,"rule",(int)rule);Number(card,"gameType",(int)gameType);Number(card,"tier",0);
            Set(card,"lobbyView",lobby);Set(card,"policyPanel",policy);
            foreach(var t in card.GetComponentsInChildren<TMP_Text>(true)){
                if(t.name=="Level")t.text="Elo 匹配";
                if(t.name=="Round")t.text=RankedRules.Name(card.RuleId)+(rule==MatchButton.MatchRule.Hongque
                    ?(gameType==MatchButton.MatchGameType.Banzhuang?" · 8局战":" · 16局战")
                    :rule==MatchButton.MatchRule.Qingque?(gameType==MatchButton.MatchGameType.Unlimited?" · 无固定时限":" · 全庄战（16局）")
                    :rule==MatchButton.MatchRule.Sichuan||rule==MatchButton.MatchRule.SichuanXueliuExchange?" · 半庄战（8副）":" · 全庄战");
            }
            ApplyEloTitleFont(card);
            MatchCardLayoutBaker.BakeCard(card);
        }
        var note=page.Find("EloSummary");if(note)Undo.DestroyObjectImmediate(note.gameObject);
    }

    [MenuItem("Tools/Mahjong/Main Scene/Update Ranked Queue Options")]
    public static void UpdateMatchQueues(){
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=ScenePath)throw new InvalidOperationException("Open the main scene outside Play Mode first.");
        var lobby=Find<MatchLobbyView>();var policy=Find<RankPolicyPanel>();
        Undo.RegisterFullObjectHierarchyUndo(lobby.gameObject,"Update ranked queue options");
        Undo.RegisterFullObjectHierarchyUndo(policy.gameObject,"Update ranked queue options");
        var pages=lobby.transform.Find("MatchLayout/RulePages");
        ConfigureEloCards(pages.Find("Qingque"),MatchButton.MatchRule.Qingque,lobby,policy);
        ConfigureEloCards(pages.Find("Sichuan"),MatchButton.MatchRule.Sichuan,lobby,policy);
        ConfigureEloCards(pages.Find("SichuanXueliuExchange"),MatchButton.MatchRule.SichuanXueliuExchange,lobby,policy);
        var entries=pages.GetComponentsInChildren<MatchButton>(true).Where(c=>c.IsAvailable).ToArray();
        Array(lobby,"entries",entries);Array(lobby.GetComponent<MatchPanel>(),"matchButtons",entries);
        Ref<GameObject>(policy,"eloContent").GetComponent<TMP_Text>().text=RankedRules.EloDescription;
        foreach(var text in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<TMP_Text>(true)).Where(t=>t.text.Contains("有提示"))){
            Undo.RecordObject(text,"Update ranked hint wording");
            bool riichi=text.text.Contains("立直麻将")||text.transform.IsChildOf(pages.Find("Riichi"));
            text.text=text.text.Replace("有提示",riichi?"枚数提示":"番数提示");
        }
        var entering=Ref<TMP_Text>(Find<MatchFoundedPanel>(),"foundedCountdownText");
        Undo.RecordObject(entering,"Update match entry message");entering.text="正在进入对局…";
        Validate();
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
    }

    [MenuItem("Tools/Mahjong/Main Scene/Update Qingque Point Ratings")]
    public static void UpdateQingquePointRatings(){
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=ScenePath)throw new InvalidOperationException("Open the main scene outside Play Mode first.");
        var lobby=Find<MatchLobbyView>();var policy=Find<RankPolicyPanel>();
        Undo.RegisterFullObjectHierarchyUndo(lobby.gameObject,"Qingque point rating queues");
        ConfigureEloCards(lobby.transform.Find("MatchLayout/RulePages/Qingque"),MatchButton.MatchRule.Qingque,lobby,policy);
        var entries=lobby.transform.Find("MatchLayout/RulePages").GetComponentsInChildren<MatchButton>(true).Where(c=>c.IsAvailable).ToArray();
        Array(lobby,"entries",entries);Array(lobby.GetComponent<MatchPanel>(),"matchButtons",entries);
        Ref<GameObject>(policy,"eloContent").GetComponent<TMP_Text>().text=RankedRules.EloDescription;
        foreach(var help in lobby.GetComponentsInChildren<RankedEloHelp>(true)){
            help.SetRule("qingque");
        }
        MatchCardLayoutBaker.Bake(lobby.gameObject.scene);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
    }

    static void AuthorTrendLabels(PlayerInfoPanel panel){
        var chart=Ref<PlayerInfoTrendChart>(panel,"trendChart");
        var labels=Ref<RectTransform>(panel,"trendSection").GetComponentsInChildren<TMP_Text>(true);
        Array(chart,"rankLabels",Enumerable.Range(1,4).Select(i=>(Object)labels.Single(x=>x.text==i+"位")).ToArray());
    }

    [MenuItem("Tools/Mahjong/Main Scene/Update Ranked Trend Labels")]
    public static void UpdateTrendLabels(){
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=ScenePath)throw new InvalidOperationException("Open the main scene outside Play Mode first.");
        Undo.RegisterFullObjectHierarchyUndo(Find<PlayerInfoPanel>().gameObject,"Bind ranked trend labels");
        AuthorTrendLabels(Find<PlayerInfoPanel>());
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Mahjong/Main Scene/Add Three Player Riichi Ranked UI")]
    public static void UpdateSanmaRanks(){
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=ScenePath)throw new InvalidOperationException("Open the main scene outside Play Mode first.");
        var lobby=Find<MatchLobbyView>();var policy=Find<RankPolicyPanel>();var profile=Find<PlayerInfoPanel>();
        foreach(var target in new Component[]{lobby,policy,profile})Undo.RegisterFullObjectHierarchyUndo(target.gameObject,"Add three player Riichi ranked UI");
        font=Find<UserContainer>().GetComponentsInChildren<TMP_Text>(true).First().font;
        BakeMatch(policy);BakeProfile();
        Ref<GameObject>(policy,"policyContent").GetComponent<TMP_Text>().text=RankedRules.Policy;
        Validate();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
    }

    public static void Validate(){
        DataPageUiBaker.Validate(SceneManager.GetActiveScene());
        if(!Ref<Button>(Find<UserContainer>(),"rankSwitchButton")||!Ref<TMP_Text>(Find<UserContainer>(),"eloScoreText")||!Ref<TMP_Text>(Find<PlayerInfoPanel>(),"emptyWinText"))throw new Exception("玩家面板引用缺失");
        var lobby=Find<MatchLobbyView>();var so=new SerializedObject(lobby);if(so.FindProperty("rulePages").arraySize!=RankedRules.Ids.Length)throw new Exception("匹配规则页未烘焙");
        var cards=lobby.GetComponentsInChildren<MatchButton>(true).Where(c=>c.IsAvailable).ToArray();
        if(cards.Length!=30||cards.Select(c=>c.QueueType).Distinct().Count()!=30)throw new Exception("匹配卡片队列必须是 30 个唯一入口");
        if(cards.Count(c=>c.RuleId==RiichiSanmaRankConfig.Rule)!=6)throw new Exception("三人立直匹配入口缺失");
        foreach(string field in new[]{"ruleButtons","selectedTabs","rulePlayerCounts"})
            if(so.FindProperty(field).arraySize!=RankedRules.FamilyNames.Length)throw new Exception("匹配页必须有五个规则入口："+field);
        foreach(string field in new[]{"variantButtons","selectedVariants","variantLabels","variantPlayerCounts"}){
            var items=so.FindProperty(field);
            if(items.arraySize!=3||Enumerable.Range(0,3).Any(i=>!items.GetArrayElementAtIndex(i).objectReferenceValue))throw new Exception("匹配页内部切换引用缺失："+field);
        }
        if(!Ref<GameObject>(lobby,"variantBar")||!Ref<TMP_Text>(lobby,"variantHint"))throw new Exception("匹配页玩法切换行缺失");
        foreach(string rule in new[]{"qingque","sichuan",RankedRules.XueliuExchangeRule,"hongque",RankedRules.DoublesRule}){
            var eloCards=cards.Where(c=>c.RuleId==rule).ToArray();
            var expectedTypes=rule=="hongque"?new[]{1,2}:new[]{rule=="sichuan"||rule==RankedRules.XueliuExchangeRule||rule==RankedRules.DoublesRule?1:2};
            if(!eloCards.Select(c=>new SerializedObject(c).FindProperty("gameType").intValue).OrderBy(x=>x).SequenceEqual(expectedTypes)
                ||eloCards.Any(c=>!c.gameObject.activeSelf))throw new Exception(rule+" 匹配局制不正确");
            foreach(var title in eloCards.SelectMany(c=>c.GetComponentsInChildren<TMP_Text>(true)).Where(t=>t.name=="Level"))
                if(AssetDatabase.GetAssetPath(title.font)!=SolidFontPath||title.fontSharedMaterial!=title.font.material)throw new Exception("Elo 标题必须使用常规字体及材质");
        }
        foreach(var card in cards)if(!Ref<RankPolicyPanel>(card,"policyPanel")||!Ref<MatchLobbyView>(card,"lobbyView"))throw new Exception("匹配卡片引用缺失");
    }
}
