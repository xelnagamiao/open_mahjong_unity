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
    const string SolidFontPath="Assets/Resources/font/Chinese/AlibabaPuHuiTi/AlibabaPuHuiTi-3-55-Regular SDF.asset";
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
        BakeData();BakeProfile();BakeMatch(policy);BakeHome();BakeLeaderboard();BakeEloHelp();
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
        var box=Rect(r,"Tooltip");box.anchorMin=box.anchorMax=new Vector2(1,0);box.pivot=new Vector2(1,1);box.anchoredPosition=new Vector2(0,-12);box.sizeDelta=new Vector2(790,640);
        var bg=box.GetComponent<Image>();if(!bg)bg=box.gameObject.AddComponent<Image>();bg.color=new Color(.12f,.15f,.20f,1f);bg.raycastTarget=false;
        var group=box.GetComponent<CanvasGroup>();if(!group)group=box.gameObject.AddComponent<CanvasGroup>();group.interactable=false;group.blocksRaycasts=false;
        var label=Text(box,"Algorithm",RankedRules.EloAlgorithm,25,new Color(.94f,.95f,.98f));Stretch(label.rectTransform);label.rectTransform.offsetMin=new Vector2(24,22);label.rectTransform.offsetMax=new Vector2(-24,-22);label.textWrappingMode=TextWrappingModes.Normal;label.alignment=TextAlignmentOptions.TopLeft;label.lineSpacing=3;
        // Render above the later rule-page siblings without changing their hierarchy.
        var canvas=box.GetComponent<Canvas>();if(!canvas)canvas=box.gameObject.AddComponent<Canvas>();canvas.overrideSorting=true;canvas.sortingOrder=lobby.GetComponentInParent<Canvas>(true).sortingOrder+1;
        Set(help,"tooltip",box.gameObject);box.gameObject.SetActive(false);
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

    static void BakeData(){
        var panel=Find<DataPanel>();var search=panel.transform.Find("SreachPanel");
        var old=search.Find("GameObject");old.gameObject.SetActive(false);
        var dropdown=search.Find("DataRuleDropdown")?.GetComponent<TMP_Dropdown>();
        if(!dropdown){dropdown=Object.Instantiate(Ref<TMP_Dropdown>(Find<PlayerInfoPanel>(),"otherRulesDropdown"),search,false);dropdown.name="DataRuleDropdown";}
        dropdown.gameObject.SetActive(true);dropdown.onValueChanged=new TMP_Dropdown.DropdownEvent();
        dropdown.ClearOptions();dropdown.AddOptions(RankedRules.Names.ToList());dropdown.SetValueWithoutNotify(0);dropdown.RefreshShownValue();
        ConfigureDataRuleDropdown(dropdown,(RectTransform)old);
        Set(panel,"ruleDropdown",dropdown);
        var boardTitle=panel.transform.Find("LeaderBoardTitle/Text (TMP)").GetComponent<TMP_Text>();
        boardTitle.text="国标麻将 · 段位排行榜";boardTitle.fontSize=32;boardTitle.enableAutoSizing=false;
        var recordsTitle=panel.transform.Find("DataCount/GameObject/Panel/Text (TMP)").GetComponent<TMP_Text>();recordsTitle.text="国标麻将 · 最近对局";recordsTitle.fontSize=36;recordsTitle.alignment=TextAlignmentOptions.Center;
        Set(panel,"leaderboardTitle",boardTitle);Set(panel,"recordsTitle",recordsTitle);
        var board=Ref<Transform>(panel,"leaderboardContainer");var records=Ref<Transform>(panel,"ladderRecordContainer");
        var boardRect=(RectTransform)board;boardRect.sizeDelta=new Vector2(0,boardRect.sizeDelta.y);boardRect.anchoredPosition=new Vector2(0,boardRect.anchoredPosition.y);
        var boardLayout=board.GetComponent<VerticalLayoutGroup>();boardLayout.childControlWidth=true;boardLayout.childForceExpandWidth=true;
        foreach(Transform child in board)child.gameObject.SetActive(false);foreach(Transform child in records)child.gameObject.SetActive(false);
        var boardStatus=Text(board.parent,"RankedEmptyState","登录后查看排行榜",25,new Color(.9f,.93f,1));Place(boardStatus.rectTransform,20,30,470,100);boardStatus.alignment=TextAlignmentOptions.Top;
        var recordStatus=Text(records.parent,"RankedEmptyState","登录后查看最近对局",28,new Color(.22f,.25f,.3f));Place(recordStatus.rectTransform,24,30,1200,100);
        Set(panel,"leaderboardStatus",boardStatus);Set(panel,"recordsStatus",recordStatus);
        var refresh=Button(search,"RefreshRankedData","刷新数据",26);Place((RectTransform)refresh.transform,1100,16,180,66);Set(panel,"refreshButton",refresh);
    }

    static void ConfigureDataRuleDropdown(TMP_Dropdown dropdown,RectTransform source){
        var rect=(RectTransform)dropdown.transform;
        rect.anchorMin=source.anchorMin;rect.anchorMax=source.anchorMax;rect.pivot=source.pivot;
        rect.anchoredPosition=source.anchoredPosition;rect.sizeDelta=source.sizeDelta;rect.localScale=Vector3.one;
        // The profile selector's fixed 132 x 44 label must not follow it into the data page.
        void Caption(TMP_Text label){
            Stretch(label.rectTransform);label.rectTransform.offsetMin=new Vector2(16,4);label.rectTransform.offsetMax=new Vector2(-38,-4);
            label.fontSize=30;label.enableAutoSizing=false;label.alignment=TextAlignmentOptions.Center;label.textWrappingMode=TextWrappingModes.NoWrap;
        }
        Caption(dropdown.captionText);
        if(dropdown.placeholder is TMP_Text placeholder){Caption(placeholder);placeholder.gameObject.SetActive(false);}
        var arrow=(RectTransform)dropdown.transform.Find("Arrow");
        arrow.anchorMin=arrow.anchorMax=arrow.pivot=new Vector2(1,.5f);arrow.anchoredPosition=new Vector2(-12,0);
        var template=dropdown.template;
        template.anchorMin=Vector2.zero;template.anchorMax=new Vector2(1,0);template.pivot=new Vector2(.5f,1);
        template.anchoredPosition=new Vector2(0,-4);template.sizeDelta=new Vector2(0,256);
        var scroll=template.GetComponent<ScrollRect>();scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;
        scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;
        Stretch(scroll.viewport);scroll.viewport.offsetMin=new Vector2(2,2);scroll.viewport.offsetMax=new Vector2(-12,-2);
        var content=scroll.content;content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(.5f,1);
        content.anchoredPosition=Vector2.zero;content.sizeDelta=new Vector2(0,58);
        var item=(RectTransform)dropdown.itemText.transform.parent;
        item.anchorMin=new Vector2(0,1);item.anchorMax=Vector2.one;item.pivot=new Vector2(.5f,1);item.anchoredPosition=Vector2.zero;item.sizeDelta=new Vector2(0,58);
        Stretch(dropdown.itemText.rectTransform);dropdown.itemText.rectTransform.offsetMin=new Vector2(32,0);dropdown.itemText.rectTransform.offsetMax=new Vector2(-12,0);
        dropdown.itemText.fontSize=28;dropdown.itemText.enableAutoSizing=false;dropdown.itemText.alignment=TextAlignmentOptions.MidlineLeft;dropdown.itemText.textWrappingMode=TextWrappingModes.NoWrap;
        template.gameObject.SetActive(false);
    }

    [MenuItem("Tools/Mahjong/Main Scene/Update Data Panel Layout")]
    public static void UpdateDataPanelLayout(){
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=ScenePath)throw new InvalidOperationException("Open the main scene outside Play Mode first.");
        var panel=Find<DataPanel>();Undo.RegisterFullObjectHierarchyUndo(panel.gameObject,"Repair data panel layout");
        ConfigureDataRuleDropdown(Ref<TMP_Dropdown>(panel,"ruleDropdown"),(RectTransform)panel.transform.Find("SreachPanel/GameObject"));
        Ref<TMP_Text>(panel,"leaderboardTitle").text="国标麻将 · 段位排行榜";
        Ref<TMP_Text>(panel,"recordsTitle").text="国标麻将 · 最近对局";Ref<TMP_Text>(panel,"recordsTitle").alignment=TextAlignmentOptions.Center;
        BakeLeaderboard();
        foreach(var item in panel.GetComponentsInChildren<LeaderboardItem>(true))Ref<TMP_Text>(item,"scoreText").text="0.00/20";
        ValidateDataPanelLayout(panel);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
    }

    static void ValidateDataPanelLayout(DataPanel panel){
        var dropdown=Ref<TMP_Dropdown>(panel,"ruleDropdown");
        var source=(RectTransform)panel.transform.Find("SreachPanel/GameObject");var rect=(RectTransform)dropdown.transform;
        if(rect.anchoredPosition!=source.anchoredPosition||rect.sizeDelta!=source.sizeDelta)throw new Exception("数据页规则下拉框必须对齐原规则选择位置");
        var caption=dropdown.captionText.rectTransform;
        if(caption.anchorMin!=Vector2.zero||caption.anchorMax!=Vector2.one||dropdown.captionText.alignment!=TextAlignmentOptions.Center)throw new Exception("数据页规则名称必须在下拉框内居中");
        if(dropdown.template.sizeDelta.x!=0||dropdown.template.anchoredPosition.x!=0)throw new Exception("数据页规则列表必须与下拉框等宽对齐");
    }

    static void BakeHome(){
        var user=Find<UserContainer>();Set(user,"rankSwitchButton",user.transform.Find("SwitchButton").GetComponent<Button>());
        var rank=Ref<TMP_Text>(user,"rankText");rank.text="国标麻将 · 10级";rank.fontSize=24;rank.enableAutoSizing=true;rank.fontSizeMin=20;rank.fontSizeMax=24;
        user.transform.Find("Text (TMP)").GetComponent<TMP_Text>().text="玩家段位";
        var score=Ref<TMP_Text>(user,"rankScoreText");score.text="0 / 20 PT";score.font=font;score.fontSharedMaterial=font.material;score.fontSize=20;score.enableAutoSizing=false;score.textWrappingMode=TextWrappingModes.Normal;
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
        rules.anchoredPosition=new Vector2(24,-212);Ref<RectTransform>(panel,"recentSection").anchoredPosition=new Vector2(24,-276);
        var statistics=Ref<PlayerInfoStatistics>(panel,"statistics");var statContent=Ref<RectTransform>(statistics,"content");if(!statContent.GetComponent<LayoutElement>())statContent.gameObject.AddComponent<LayoutElement>();
        BakeProfileRulePicker(panel,rules);
        var hand=Ref<PlayerInfoHandView>(panel,"handView");var empty=Text(hand.transform,"NoRecentWin","无最近大和",24,new Color(.76f,.8f,.88f));Stretch(empty.rectTransform);empty.alignment=TextAlignmentOptions.MidlineLeft;Set(panel,"emptyWinText",empty);empty.gameObject.SetActive(false);
        var caption=Ref<TMP_Text>(panel,"winLabel");caption.rectTransform.anchorMin=new Vector2(0,1);caption.rectTransform.anchorMax=new Vector2(1,1);caption.rectTransform.pivot=new Vector2(.5f,1);caption.rectTransform.anchoredPosition=new Vector2(76,0);caption.rectTransform.sizeDelta=new Vector2(-152,32);caption.textWrappingMode=TextWrappingModes.NoWrap;caption.enableAutoSizing=true;caption.fontSizeMin=14;caption.fontSizeMax=24;
        var rank=Ref<TMP_Text>(panel,"rankText");rank.text="国标 · 10级";rank.fontSize=28;rank.enableAutoSizing=true;rank.fontSizeMin=22;rank.fontSizeMax=28;rank.textWrappingMode=TextWrappingModes.NoWrap;rank.overflowMode=TextOverflowModes.Ellipsis;
        var score=Ref<TMP_Text>(panel,"rankScoreText");score.text="0 / 20 PT";score.font=font;score.fontSharedMaterial=font.material;score.fontSize=24;score.enableAutoSizing=true;score.fontSizeMin=18;score.fontSizeMax=24;score.textWrappingMode=TextWrappingModes.NoWrap;score.overflowMode=TextOverflowModes.Ellipsis;
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
            label.alignment=TextAlignmentOptions.Center;label.textWrappingMode=TextWrappingModes.NoWrap;
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
        var sanma=pages.Find("RiichiSanma")?.gameObject;
        if(!sanma){sanma=Object.Instantiate(riichi,pages,false);sanma.name="RiichiSanma";}
        foreach(var card in sanma.GetComponentsInChildren<MatchButton>(true)){
            Number(card,"rule",(int)MatchButton.MatchRule.RiichiSanma);
            Set(card,"lobbyView",lobby);Set(card,"policyPanel",policy);
            foreach(var t in card.GetComponentsInChildren<TMP_Text>(true)) t.text=t.text.Replace("立直麻将","立直三麻");
        }
        sanma.SetActive(false);
        national.SetActive(true);riichi.SetActive(false);
        Array(lobby,"rulePages",new Object[]{national,riichi,qingque,sichuan,sanma,exchange});Array(lobby,"ruleButtons",buttons);Array(lobby,"selectedTabs",selected);
        var entries=new[]{national,riichi,qingque,sichuan,sanma,exchange}.SelectMany(p=>p.GetComponentsInChildren<MatchButton>(true)).Where(c=>c.IsAvailable).ToArray();Array(lobby,"entries",entries);Array(lobby.GetComponent<MatchPanel>(),"matchButtons",entries);
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

    static void BakeMatchNavigation(){
        var lobby=Find<MatchLobbyView>();var layout=lobby.transform.Find("MatchLayout");
        var tabs=layout.Find("HeaderBar/RuleTabs");
        var pageLayout=layout.GetComponent<VerticalLayoutGroup>();
        pageLayout.childAlignment=TextAnchor.UpperCenter;pageLayout.padding.top=52;
        font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SolidFontPath);
        if(!font)throw new Exception("匹配页常规字体缺失");
        var buttons=new Button[RankedRules.FamilyNames.Length];var selected=new GameObject[buttons.Length];
        for(int i=0;i<buttons.Length;i++){
            var button=tabs.Find("Rule_"+i).GetComponent<Button>();buttons[i]=button;button.gameObject.SetActive(true);
            button.onClick=new Button.ButtonClickedEvent();Width(button.transform,186);
            var label=button.transform.Find("Label").GetComponent<TMP_Text>();
            label.text=RankedRules.FamilyNames[i];label.font=font;label.fontSharedMaterial=font.material;FitRankedTabLabel(label);
            selected[i]=button.transform.Find("SelectedBackground").gameObject;selected[i].SetActive(i==0);
        }
        foreach(Transform tab in tabs)if(tab.name=="Rule_4"||tab.name=="Rule_5")tab.gameObject.SetActive(false);
        Width(tabs,762);Width(layout.Find("HeaderBar/RankSummary"),750);
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
        var variants=new Button[2];var variantSelected=new GameObject[2];var labels=new TMP_Text[2];var variantCounts=new TMP_Text[2];
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
        foreach(var help in lobby.GetComponentsInChildren<RankedEloHelp>(true))
            Ref<GameObject>(help,"tooltip").GetComponentInChildren<TMP_Text>(true).text=RankedRules.EloAlgorithm;
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
        var card=cards.FirstOrDefault(c=>new SerializedObject(c).FindProperty("gameType").intValue==(int)MatchButton.MatchGameType.Quanzhuang)??cards.FirstOrDefault();
        if(!card){if(!template)throw new Exception("Elo 场次模板缺失");card=Object.Instantiate(template,grid,false);card.name="Elo_0";}
        // Keep the authored cards recoverable, but only the full-length entry is available.
        foreach(var existing in cards)existing.gameObject.SetActive(existing==card);
        card.gameObject.SetActive(true);
        Number(card,"rule",(int)rule);Number(card,"gameType",(int)MatchButton.MatchGameType.Quanzhuang);Number(card,"tier",0);
        Set(card,"lobbyView",lobby);Set(card,"policyPanel",policy);
        foreach(var t in card.GetComponentsInChildren<TMP_Text>(true)){
            if(t.name=="Level")t.text="Elo 匹配";
            if(t.name=="Round")t.text=RankedRules.Name(card.RuleId)+" · 全庄战";
        }
        ApplyEloTitleFont(card);
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
        var lobby=Find<MatchLobbyView>();var policy=Find<RankPolicyPanel>();var profile=Find<PlayerInfoPanel>();var data=Find<DataPanel>();
        foreach(var target in new Component[]{lobby,policy,profile,data})Undo.RegisterFullObjectHierarchyUndo(target.gameObject,"Add three player Riichi ranked UI");
        font=Find<UserContainer>().GetComponentsInChildren<TMP_Text>(true).First().font;
        BakeMatch(policy);BakeProfile();
        var dropdown=Ref<TMP_Dropdown>(data,"ruleDropdown");
        dropdown.ClearOptions();dropdown.AddOptions(RankedRules.Names.ToList());dropdown.SetValueWithoutNotify(0);dropdown.RefreshShownValue();
        Ref<GameObject>(policy,"policyContent").GetComponent<TMP_Text>().text=RankedRules.Policy;
        Validate();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
    }

    static void BakeLeaderboard(){
        var prefab=Ref<LeaderboardItem>(Find<DataPanel>(),"leaderboardItemPrefab");var path=AssetDatabase.GetAssetPath(prefab);
        var root=PrefabUtility.LoadPrefabContents(path);
        try{var item=root.GetComponent<LeaderboardItem>();var avatar=Ref<Image>(item,"avatar");if(!avatar.GetComponent<ProfileOnClick>())avatar.gameObject.AddComponent<ProfileOnClick>();avatar.raycastTarget=true;
            void Column(RectTransform rect,bool right,float x,float y,float width,float height){rect.anchorMin=rect.anchorMax=new Vector2(right?1:0,.5f);rect.pivot=new Vector2(right?1:0,.5f);rect.anchoredPosition=new Vector2(x,y);rect.sizeDelta=new Vector2(width,height);}
            Column((RectTransform)Ref<TMP_Text>(item,"rankText").transform.parent,false,0,0,70,70);Column(avatar.rectTransform,false,78,0,50,50);
            foreach(string field in new[]{"usernameText","uidText"}){
                var label=Ref<TMP_Text>(item,field);float y=field=="usernameText"?15:-15;var rect=label.rectTransform;
                rect.anchorMin=new Vector2(0,.5f);rect.anchorMax=new Vector2(1,.5f);rect.offsetMin=new Vector2(140,y-15);rect.offsetMax=new Vector2(-166,y+15);
                label.enableAutoSizing=true;label.fontSizeMin=19;label.fontSizeMax=25;label.textWrappingMode=TextWrappingModes.NoWrap;label.overflowMode=TextOverflowModes.Ellipsis;
            }
            var rank=Ref<TMP_Text>(item,"rankNameText");Column(rank.rectTransform,true,-4,15,156,30);rank.enableAutoSizing=true;rank.fontSizeMin=14;rank.fontSizeMax=25;rank.textWrappingMode=TextWrappingModes.NoWrap;rank.overflowMode=TextOverflowModes.Ellipsis;
            var score=Ref<TMP_Text>(item,"scoreText");Column(score.rectTransform,true,-4,-15,156,30);score.enableAutoSizing=true;score.fontSizeMin=14;score.fontSizeMax=25;score.textWrappingMode=TextWrappingModes.NoWrap;score.overflowMode=TextOverflowModes.Ellipsis;score.text="0.00/20";
            PrefabUtility.SaveAsPrefabAsset(root,path);
        }finally{PrefabUtility.UnloadPrefabContents(root);}
    }

    public static void Validate(){
        var data=Find<DataPanel>();var dropdown=Ref<TMP_Dropdown>(data,"ruleDropdown");
        if(!dropdown||dropdown.options.Count!=RankedRules.Ids.Length||!dropdown.template||!dropdown.itemText)throw new Exception("数据页规则下拉框未烘焙");
        if(!Ref<Button>(Find<UserContainer>(),"rankSwitchButton")||!Ref<TMP_Text>(Find<PlayerInfoPanel>(),"emptyWinText"))throw new Exception("玩家面板引用缺失");
        var lobby=Find<MatchLobbyView>();var so=new SerializedObject(lobby);if(so.FindProperty("rulePages").arraySize!=RankedRules.Ids.Length)throw new Exception("匹配规则页未烘焙");
        var cards=lobby.GetComponentsInChildren<MatchButton>(true).Where(c=>c.IsAvailable).ToArray();
        if(cards.Length!=27||cards.Select(c=>c.QueueType).Distinct().Count()!=27)throw new Exception("匹配卡片队列必须是 27 个唯一入口");
        if(cards.Count(c=>c.RuleId==RiichiSanmaRankConfig.Rule)!=6)throw new Exception("三人立直匹配入口缺失");
        foreach(string field in new[]{"ruleButtons","selectedTabs","rulePlayerCounts"})
            if(so.FindProperty(field).arraySize!=RankedRules.FamilyNames.Length)throw new Exception("匹配页必须有四个规则入口："+field);
        foreach(string field in new[]{"variantButtons","selectedVariants","variantLabels","variantPlayerCounts"}){
            var items=so.FindProperty(field);
            if(items.arraySize!=2||Enumerable.Range(0,2).Any(i=>!items.GetArrayElementAtIndex(i).objectReferenceValue))throw new Exception("匹配页内部切换引用缺失："+field);
        }
        if(!Ref<GameObject>(lobby,"variantBar")||!Ref<TMP_Text>(lobby,"variantHint"))throw new Exception("匹配页玩法切换行缺失");
        foreach(string rule in new[]{"qingque","sichuan",RankedRules.XueliuExchangeRule}){
            var eloCards=cards.Where(c=>c.RuleId==rule).ToArray();
            if(eloCards.Length!=1||!eloCards[0].gameObject.activeSelf||new SerializedObject(eloCards[0]).FindProperty("gameType").intValue!=(int)MatchButton.MatchGameType.Quanzhuang)throw new Exception(rule+" 必须仅开放全庄战");
            foreach(var title in eloCards[0].GetComponentsInChildren<TMP_Text>(true).Where(t=>t.name=="Level"))
                if(AssetDatabase.GetAssetPath(title.font)!=SolidFontPath||title.fontSharedMaterial!=title.font.material)throw new Exception("Elo 标题必须使用常规字体及材质");
        }
        foreach(var card in cards)if(!Ref<RankPolicyPanel>(card,"policyPanel")||!Ref<MatchLobbyView>(card,"lobbyView"))throw new Exception("匹配卡片引用缺失");
    }
}
