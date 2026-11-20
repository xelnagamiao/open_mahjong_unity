using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Authors the paired card and controls in the real MainScene.</summary>
public static class MatchSpectatorUiBaker {
    public const string CardPath = "Assets/Prefabs/Room/MatchSpectatorCard.prefab";
    [MenuItem("Tools/Mahjong/Spectator/Bake Match Controls")]
    public static void Bake() {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/MainScene.unity") throw new InvalidOperationException("MainScene must be active");
        BakeCard();
        var all = scene.GetRootGameObjects();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CardPath).GetComponent<SpectatorPrefab>();
        foreach (var panel in all.SelectMany(r => r.GetComponentsInChildren<SpectatorPanel>(true))) {
            var fields = new SerializedObject(panel); Assign(fields, "pairedSpectatorPrefab", prefab); fields.ApplyModifiedProperties();
        }
        foreach (var venue in all.SelectMany(r => r.GetComponentsInChildren<Salasasa.Events.EventVenuePanel>(true))) {
            var fields = new SerializedObject(venue); Assign(fields, "pairedSpectatorPrefab", prefab); fields.ApplyModifiedProperties();
        }
        var manager = all.SelectMany(r => r.GetComponentsInChildren<GameRecordManager>(true)).Single();
        var roomPanel = all.SelectMany(r => r.GetComponentsInChildren<RoomPanel>(true)).Single();
        var dropdown = (TMP_Dropdown)new SerializedObject(roomPanel).FindProperty("botSpeedDropdown").objectReferenceValue;
        BakeControls(manager, dropdown);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    public static void BakeCard() {
        bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(CardPath) != null;
        var root = PrefabUtility.LoadPrefabContents(exists ? CardPath : SpectatorUiBaker.BasePath);
        try {
            root.name = "MatchSpectatorCard";
            var fields = new SerializedObject(root.GetComponent<SpectatorPrefab>());
            var rect = (RectTransform)root.transform;
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 136.26525f);
            var element = root.GetComponent<LayoutElement>(); element.minHeight = element.preferredHeight = 136.26525f;
            var header = (RectTransform)root.transform.Find("HeaderPanel");
            Stretch(header, new Vector2(0, 2f/3), Vector2.one);
            var title = (RectTransform)root.transform.Find("GameObject");
            Stretch(title, new Vector2(0, 2f/3), Vector2.one, 0, -152);
            var open = (RectTransform)root.transform.Find("GameObject (1)");
            Stretch(open, new Vector2(0, 1f/3), new Vector2(1, 2f/3), 0, -152);
            var closed = root.transform.Find("ClosedPlayers") as RectTransform;
            if (closed == null) { closed = UnityEngine.Object.Instantiate(open, root.transform); closed.name = "ClosedPlayers"; }
            Stretch(closed, Vector2.zero, new Vector2(1, 1f/3), 0, -152);
            Assign(fields, "openRoomLabel", null); Assign(fields, "closedRoomLabel", null);
            fields.ApplyModifiedPropertiesWithoutUndo();
            foreach (var row in new[] { open, closed })
                foreach (var name in new[] { "OpenRoom", "ClosedRoom" }) {
                    var label = row.Find(name);
                    if (label != null) UnityEngine.Object.DestroyImmediate(label.gameObject);
                }
            fields.Update();
            var names = fields.FindProperty("closedPlayerNames"); names.arraySize = 4;
            var colors = fields.FindProperty("closedTeamBackgrounds"); colors.arraySize = 4;
            for (int i = 0; i < 4; i++) {
                var text = (TextMeshProUGUI)fields.FindProperty("Player" + (i+1) + "NameText").objectReferenceValue;
                var twin = closed.Find(text.name).GetComponent<TextMeshProUGUI>();
                foreach (var label in new[] { text, twin }) {
                    Stretch(label.rectTransform, new Vector2(i*.25f,0), new Vector2((i+1)*.25f,1), 16, -14);
                    Normal(label); label.fontSize = 24; label.fontSizeMin = 18; label.fontSizeMax = 24; label.enableAutoSizing = true;
                }
                names.GetArrayElementAtIndex(i).objectReferenceValue = twin;
                var background = closed.Find("PlayerTeamColor"+i).GetComponent<Image>();
                var openBackground = (Image)fields.FindProperty("playerTeamBackgrounds").GetArrayElementAtIndex(i).objectReferenceValue;
                foreach (var image in new[] { background, openBackground })
                    Stretch(image.rectTransform, new Vector2(i*.25f,0), new Vector2((i+1)*.25f,1),1,-1);
                colors.GetArrayElementAtIndex(i).objectReferenceValue = background;
            }
            var rule = (TextMeshProUGUI)fields.FindProperty("RuleText").objectReferenceValue;
            var id = (TextMeshProUGUI)fields.FindProperty("GamestateIdText").objectReferenceValue;
            var progress = (TextMeshProUGUI)fields.FindProperty("RoundProgressText").objectReferenceValue;
            Stretch(rule.rectTransform, Vector2.zero,new Vector2(.25f,1),16,-10);
            Stretch(id.rectTransform,new Vector2(.25f,0),new Vector2(.75f,1),16,-12);
            Stretch(progress.rectTransform,new Vector2(.75f,0),Vector2.one,16,-12);
            Assign(fields,"pairedPlayersRow",closed.gameObject);
            fields.ApplyModifiedPropertiesWithoutUndo();
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true)) BindTypography(text);
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true)) Normal(text);
            PrefabUtility.SaveAsPrefabAsset(root,CardPath);
        } finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    [MenuItem("Tools/Mahjong/Spectator/Bake Replay Navigation")]
    public static void BakeReplayNavigation() {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/MainScene.unity") throw new InvalidOperationException("MainScene must be active");
        var all = scene.GetRootGameObjects();
        var manager = all.SelectMany(r => r.GetComponentsInChildren<GameRecordManager>(true)).Single();
        var room = all.SelectMany(r => r.GetComponentsInChildren<RoomPanel>(true)).Single();
        var dropdown = (TMP_Dropdown)new SerializedObject(room).FindProperty("botSpeedDropdown").objectReferenceValue;
        BakeControls(manager, dropdown);
        var button = (Button)new SerializedObject(manager).FindProperty("showRoundInfoButton").objectReferenceValue;
        foreach (var text in button.GetComponentsInChildren<TMP_Text>(true)) { text.text = "阅览状态"; BindTypography(text); }
        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static void BakeControls(GameRecordManager manager, TMP_Dropdown dropdownTemplate) {
        Undo.RegisterFullObjectHierarchyUndo(manager.gameObject,"Match replay controls");
        var fields = new SerializedObject(manager);
        var template = (Button)fields.FindProperty("showGameInfoButton").objectReferenceValue;
        var step = (Button)fields.FindProperty("nextStepButton").objectReferenceValue;
        var parent = step.transform.parent;
        var bar = parent.Find("MatchTableControls") as RectTransform;
        if (bar == null) bar = new GameObject("MatchTableControls",typeof(RectTransform),typeof(Image)).GetComponent<RectTransform>();
        bar.SetParent(parent,false); bar.anchorMin = bar.anchorMax = new Vector2(.5f,.5f); bar.pivot = new Vector2(.5f,.5f);
        bar.sizeDelta = new Vector2(1040,64); bar.anchoredPosition = new Vector2(0,80);
        bar.GetComponent<Image>().color = new Color32(28,32,36,244);
        Button MakeButton(string name,string caption,float x) {
            var existing = bar.Find(name); var button = existing ? existing.GetComponent<Button>() : UnityEngine.Object.Instantiate(template,bar);
            button.name=name; button.onClick=new Button.ButtonClickedEvent(); button.gameObject.SetActive(true);
            Place((RectTransform)button.transform,x,150,48);
            var label=button.GetComponentInChildren<TMP_Text>(true); label.text=caption; Normal(label); label.fontSize=25; label.fontSizeMin=22;label.fontSizeMax=25;
            return button;
        }
        var open = MakeButton("GoOpen","前往开室",-450);
        var closed = MakeButton("GoClosed","前往闭室",-316);
        Place((RectTransform)open.transform,-450,124,48);
        Place((RectTransform)closed.transform,-316,124,48);
        var toggleRect = bar.Find("CompareWall") as RectTransform;
        if (toggleRect == null) toggleRect=new GameObject("CompareWall",typeof(RectTransform),typeof(Toggle)).GetComponent<RectTransform>();
        toggleRect.SetParent(bar,false);Place(toggleRect,-109,270,48);
        var toggle = toggleRect.GetComponent<Toggle>(); toggle.onValueChanged=new Toggle.ToggleEvent();
        var box = ChildImage(toggleRect,"Background",new Color32(62,74,84,255));Place(box.rectTransform,0,270,48);
        var check = ChildImage(box.transform,"Checkmark",new Color32(59,108,146,255));Place(check.rectTransform,0,270,48);
        check.raycastTarget=false;
        toggle.targetGraphic=box;toggle.graphic=check;
        var textSource=template.GetComponentInChildren<TextMeshProUGUI>(true);
        var toggleLabel=Label(toggleRect,"Label",textSource,"切换到同牌山位置");Place(toggleLabel.rectTransform,0,258,44);toggleLabel.fontSize=23;toggleLabel.fontSizeMin=18;toggleLabel.fontSizeMax=23;toggleLabel.color=Color.white;toggleLabel.transform.SetAsLastSibling();
        var independent = MakeButton("IndependentProgress","独立阅览进度",138);
        Place((RectTransform)independent.transform,138,204,48);
        independent.GetComponentInChildren<TMP_Text>(true).color = Color.white;
        var phase = bar.Find("ComparePhase")?.GetComponent<TMP_Dropdown>() ?? UnityEngine.Object.Instantiate(dropdownTemplate,bar);
        phase.name="ComparePhase";Place((RectTransform)phase.transform,305,110,48); phase.gameObject.SetActive(true);
        phase.onValueChanged=new TMP_Dropdown.DropdownEvent();phase.ClearOptions();phase.AddOptions(new System.Collections.Generic.List<string>{"摸牌后","摸牌前"});
        foreach(var t in phase.GetComponentsInChildren<TMP_Text>(true)){Normal(t);t.fontSize=24;t.fontSizeMin=20;t.fontSizeMax=24;t.enableAutoSizing=true;}
        var status=Label(bar,"MatchStatus",textSource,"开室 · 1 副\n已暂停");Place(status.rectTransform,441,142,52);status.fontSize=20;status.fontSizeMin=16;status.fontSizeMax=20;status.enableAutoSizing=true;status.color=Color.white;
        Assign(fields,"matchTableControls",bar.gameObject);Assign(fields,"goOpenTableButton",open);Assign(fields,"goClosedTableButton",closed);
        Assign(fields,"compareWallToggle",toggle);Assign(fields,"comparePhaseDropdown",phase);Assign(fields,"matchTableStatus",status);
        Assign(fields,"independentProgressButton",independent);
        fields.ApplyModifiedProperties();bar.gameObject.SetActive(false);
        foreach(var text in bar.GetComponentsInChildren<TMP_Text>(true))BindTypography(text);
    }
    private static void BindTypography(TMP_Text text){var binding=text.GetComponent<LocalizedPlatformLabel>();if(!binding)return;binding.authoredSize=text.fontSize;binding.minimumSize=text.fontSizeMin;binding.authoredAutoSize=text.enableAutoSizing;binding.fitTranslation=true;binding.key=text.text;}
    private static void Normal(TMP_Text text){text.fontStyle=FontStyles.Normal;text.fontWeight=FontWeight.Regular;text.richText=false;}
    private static TextMeshProUGUI Label(Transform parent,string name,TextMeshProUGUI source,string value){var item=parent.Find(name);var label=item?item.GetComponent<TextMeshProUGUI>():UnityEngine.Object.Instantiate(source,parent);label.name=name;label.text=value;label.raycastTarget=false;Normal(label);return label;}
    private static Image ChildImage(Transform parent,string name,Color color){var child=parent.Find(name);var image=child?child.GetComponent<Image>():new GameObject(name,typeof(RectTransform),typeof(Image)).GetComponent<Image>();image.transform.SetParent(parent,false);image.color=color;return image;}
    private static void Assign(SerializedObject obj,string key,UnityEngine.Object value){obj.FindProperty(key).objectReferenceValue=value;}
    private static void Place(RectTransform rect,float x,float width,float height){rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=new Vector2(x,0);rect.sizeDelta=new Vector2(width,height);}
    private static void Stretch(RectTransform rect,Vector2 min,Vector2 max,float left=0,float right=0){rect.anchorMin=min;rect.anchorMax=max;rect.offsetMin=new Vector2(left,0);rect.offsetMax=new Vector2(right,0);}
}
