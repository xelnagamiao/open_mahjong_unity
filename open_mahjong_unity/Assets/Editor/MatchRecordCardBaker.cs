using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Three authored alternatives sharing one data binder and one replay action.</summary>
public static class MatchRecordCardBaker {
    public static readonly string[] Paths = {
        "Assets/Prefabs/Record/MatchRecordRows.prefab",
        "Assets/Prefabs/Record/MatchRecordColumns.prefab",
        "Assets/Prefabs/Record/MatchRecordSeats.prefab"
    };
    [MenuItem("Tools/Mahjong/Records/Bake paired card designs")]
    public static void Bake() {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first");
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Record/RecordItem (3).prefab");
        var font = source.GetComponentInChildren<TMP_Text>(true).font;
        for (int variant = 0; variant < Paths.Length; variant++) Build(variant,font);
    }
    private static void Build(int variant,TMP_FontAsset font) {
        Color ink=C(38,54,73), muted=C(100,116,132);
        int height=variant==0?320:388;
        var root=new GameObject("MatchRecord"+new[]{"Rows","Columns","Seats"}[variant],typeof(RectTransform),typeof(CanvasRenderer),typeof(Image),typeof(LayoutElement),typeof(MatchRecordCard));
        try {
            root.GetComponent<RectTransform>().sizeDelta=new Vector2(1474,height);
            root.GetComponent<Image>().color=C(232,238,247);
            root.GetComponent<LayoutElement>().preferredHeight=height;
            root.GetComponent<LayoutElement>().minHeight=height;
            RectTransform Place(GameObject go,Transform parent,float x,float y,float w,float h) {
                var r=(RectTransform)go.transform;r.SetParent(parent,false);r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r;
            }
            Image Box(string name,float x,float y,float w,float h,Color color) {
                var go=new GameObject(name,typeof(RectTransform),typeof(Image));Place(go,root.transform,x,y,w,h);var image=go.GetComponent<Image>();image.color=color;image.raycastTarget=false;return image;
            }
            TMP_Text Text(string name,string value,float x,float y,float w,float h,int size,Color color,TextAlignmentOptions align=TextAlignmentOptions.MidlineLeft) {
                var go=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI));Place(go,root.transform,x,y,w,h);
                var t=go.GetComponent<TextMeshProUGUI>();t.font=font;t.text=value;t.fontSize=size;t.fontSizeMin=18;t.fontSizeMax=size;t.enableAutoSizing=true;
                t.fontWeight=FontWeight.Regular;t.fontStyle=FontStyles.Normal;t.richText=false;t.color=color;t.alignment=align;t.raycastTarget=false;t.overflowMode=TextOverflowModes.Ellipsis;return t;
            }
            Box("Header",0,0,1474,64,C(47,70,105));
            var rule=Text("Rule","双桌牌谱",24,10,610,44,29,Color.white);
            ConfigureRuleLabel(rule);
            var date=Text("Date","",1030,10,420,44,23,Color.white,TextAlignmentOptions.MidlineRight);
            var names=new TMP_Text[8];var scores=new TMP_Text[8];
            var teams=new TMP_Text[8];var badges=new Image[8];
            void Badge(int index,float x,float y) {
                badges[index]=Box("TeamBadge"+index,x,y,32,30,muted);
                teams[index]=Text("TeamLabel"+index,"—",x,y,32,30,19,Color.white,TextAlignmentOptions.Center);
            }
            string[] seats={"东","南","西","北"};
            if(variant==0) {
                for(int table=0;table<2;table++) {
                    int y=82+table*76;Color accent=ink;
                    Box("Room"+table,24,y,1426,68,C(242,246,251));
                    Text("RoomLabel"+table,table==0?"开室":"闭室",44,y,90,68,25,ink);
                    for(int seat=0;seat<4;seat++) {
                        int i=table*4+seat;float x=144+seat*320;
                        Badge(i,x,y+19);
                        names[i]=Text("Player"+i,"—",x+40,y+3,160,62,25,ink);
                        scores[i]=Text("Score"+i,"—",x+200,y+3,96,62,27,accent,TextAlignmentOptions.MidlineRight);
                    }
                }
            } else if(variant==1) {
                for(int table=0;table<2;table++) {
                    float x=24+table*726;Color accent=ink;
                    Box("Room"+table,x,82,700,224,C(242,246,251));
                    Text("RoomLabel"+table,table==0?"开室":"闭室",x+20,86,300,36,25,accent);
                    for(int seat=0;seat<4;seat++) {
                        int i=table*4+seat;int y=126+seat*43;
                        Badge(i,x+20,y+5);
                        names[i]=Text("Player"+i,"—",x+74,y,420,40,24,ink);
                        scores[i]=Text("Score"+i,"—",x+524,y,150,40,26,accent,TextAlignmentOptions.MidlineRight);
                    }
                }
            } else {
                for(int seat=0;seat<4;seat++) {
                    float x=24+seat*362;
                    Box("SeatCard"+seat,x,82,340,224,Color.white);
                    Text("SeatHeading"+seat,seats[seat]+"席",x+20,92,300,36,24,ink);
                    Box("Divider"+seat,x+20,139,300,1,C(219,227,235));
                    for(int table=0;table<2;table++) {
                        int i=table*4+seat;int y=156+table*72;Color accent=ink;
                        Text("RoomLabel"+i,table==0?"开室":"闭室",x+20,y,60,28,18,accent);
                        Badge(i,x+20,y+33);
                        names[i]=Text("Player"+i,"—",x+62,y+28,140,40,25,ink);
                        scores[i]=Text("Score"+i,"—",x+210,y+28,110,40,27,accent,TextAlignmentOptions.MidlineRight);
                    }
                }
            }
            var identity=Text("Identity","",24,height-64,780,44,20,muted);
            var buttonImage=Box("Replay",1194,height-64,256,48,C(59,90,129));buttonImage.raycastTarget=true;
            var original=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Record/RecordItem (3).prefab");
            foreach(var image in original.GetComponentsInChildren<Image>(true)) if(image.name=="ViewRecord") {buttonImage.sprite=image.sprite;buttonImage.type=image.type;break;}
            var button=buttonImage.gameObject.AddComponent<Button>();button.targetGraphic=buttonImage;
            var caption=Text("ReplayCaption","回放牌谱",1194,height-64,256,48,25,Color.white,TextAlignmentOptions.Center);
            caption.transform.SetParent(button.transform,true);
            var fields=new SerializedObject(root.GetComponent<MatchRecordCard>());
            void Assign(string name,UnityEngine.Object value)=>fields.FindProperty(name).objectReferenceValue=value;
            Assign("ruleLabel",rule);Assign("dateLabel",date);Assign("identityLabel",identity);Assign("replayButton",button);
            for(int i=0;i<8;i++) {
                fields.FindProperty("playerLabels").GetArrayElementAtIndex(i).objectReferenceValue=names[i];
                fields.FindProperty("scoreLabels").GetArrayElementAtIndex(i).objectReferenceValue=scores[i];
                fields.FindProperty("teamLabels").GetArrayElementAtIndex(i).objectReferenceValue=teams[i];
                fields.FindProperty("teamBadges").GetArrayElementAtIndex(i).objectReferenceValue=badges[i];
            }
            fields.ApplyModifiedPropertiesWithoutUndo();
            PrefabBaselineBaker.Save(root,Paths[variant]);
        } finally {UnityEngine.Object.DestroyImmediate(root);}
    }
    public const string SpectatorStylePath = "Assets/Prefabs/Record/MatchRecordSpectatorStyle.prefab";

    [MenuItem("Tools/Mahjong/Records/Bake spectator style card")]
    public static void BakeSpectatorStyle() {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first");
        var root = PrefabUtility.LoadPrefabContents(MatchSpectatorUiBaker.CardPath);
        try {
            root.name = "MatchRecordSpectatorStyle";
            var source = root.GetComponent<SpectatorPrefab>();
            var original = new SerializedObject(source);
            var rule = (TMP_Text)original.FindProperty("RuleText").objectReferenceValue;
            var identity = (TMP_Text)original.FindProperty("GamestateIdText").objectReferenceValue;
            var date = (TMP_Text)original.FindProperty("RoundProgressText").objectReferenceValue;
            var button = (UnityEngine.UI.Button)original.FindProperty("SpectateButton").objectReferenceValue;
            var names = new TMP_Text[8];
            var backgrounds = new UnityEngine.UI.Image[8];
            for (int i = 0; i < 4; i++) {
                names[i] = (TMP_Text)original.FindProperty("Player" + (i+1) + "NameText").objectReferenceValue;
                names[i+4] = (TMP_Text)original.FindProperty("closedPlayerNames").GetArrayElementAtIndex(i).objectReferenceValue;
                backgrounds[i] = (UnityEngine.UI.Image)original.FindProperty("playerTeamBackgrounds").GetArrayElementAtIndex(i).objectReferenceValue;
                backgrounds[i+4] = (UnityEngine.UI.Image)original.FindProperty("closedTeamBackgrounds").GetArrayElementAtIndex(i).objectReferenceValue;
            }
            UnityEngine.Object.DestroyImmediate(source);
            var binder = root.AddComponent<MatchRecordCard>();
            var fields = new SerializedObject(binder);
            fields.FindProperty("spectatorStyle").boolValue = true;
            fields.FindProperty("ruleLabel").objectReferenceValue = rule;
            fields.FindProperty("identityLabel").objectReferenceValue = identity;
            fields.FindProperty("dateLabel").objectReferenceValue = date;
            fields.FindProperty("replayButton").objectReferenceValue = button;
            button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            button.GetComponentInChildren<TMP_Text>(true).text = "回放";
            for (int i = 0; i < 8; i++) {
                var name = names[i];
                var score = UnityEngine.Object.Instantiate(name, name.transform.parent);
                score.name = "Score" + i;
                score.text = "—";
                score.alignment = TextAlignmentOptions.MidlineRight;
                var rect = score.rectTransform;
                rect.anchorMin = new Vector2((i%4+1)*.25f,0);
                rect.anchorMax = new Vector2((i%4+1)*.25f,1);
                rect.offsetMin = new Vector2(-76,0);
                rect.offsetMax = new Vector2(-14,0);
                name.rectTransform.offsetMax = new Vector2(-82,0);
                fields.FindProperty("playerLabels").GetArrayElementAtIndex(i).objectReferenceValue = name;
                fields.FindProperty("scoreLabels").GetArrayElementAtIndex(i).objectReferenceValue = score;
                fields.FindProperty("teamBadges").GetArrayElementAtIndex(i).objectReferenceValue = backgrounds[i];
            }
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(true)) {
                label.fontStyle = FontStyles.Normal;
                label.fontWeight = FontWeight.Regular;
                label.richText = false;
                var localized = label.GetComponent<LocalizedPlatformLabel>();
                if (localized != null && label != rule) UnityEngine.Object.DestroyImmediate(localized);
            }
            fields.ApplyModifiedPropertiesWithoutUndo();
            ConfigureHeaderSlot(rule, 0f, .47f, 12f);
            ConfigureHeaderSlot(identity, .47f, .78f, 10f);
            ConfigureHeaderSlot(date, .78f, 1f, 10f);
            ConfigureRuleLabel(rule);
            PrefabBaselineBaker.Save(root, SpectatorStylePath);
        } finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void ConfigureHeaderSlot(TMP_Text text, float left, float right, float minimumSize) {
        var slot = text.rectTransform;
        slot.anchorMin = new Vector2(left, slot.anchorMin.y);
        slot.anchorMax = new Vector2(right, slot.anchorMax.y);
        text.fontSizeMax = text.fontSize;
        text.fontSizeMin = minimumSize;
        text.enableAutoSizing = true;
    }

    private static void ConfigureRuleLabel(TMP_Text text) {
        var binding = text.GetComponent<LocalizedPlatformLabel>() ?? text.gameObject.AddComponent<LocalizedPlatformLabel>();
        binding.key = text.text;
        binding.followSourceChanges = true;
        binding.fitTranslation = true;
        binding.authoredSize = text.fontSizeMax;
        binding.minimumSize = text.fontSizeMin;
        binding.authoredAutoSize = true;
        binding.typographyBaked = true;
    }

    private static Color C(byte r,byte g,byte b)=>new Color32(r,g,b,255);
}
