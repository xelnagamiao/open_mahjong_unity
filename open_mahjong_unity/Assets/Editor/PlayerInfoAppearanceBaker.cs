#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Role = PlayerInfoAppearance.Role;

public static class PlayerInfoAppearanceBaker {
    const string EntryPath = "Assets/Prefabs/Notification/PlayerInfo/PlayerInfoEntry.prefab";
    const string FieldPath = "Assets/Prefabs/Notification/PlayerInfo/PlayerInfoStatField.prefab";

    public static void Bake(Scene scene) {
        if (Application.isPlaying) throw new InvalidOperationException("Bake the player profile outside Play Mode.");
        var panel = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayerInfoPanel>(true)).Single();
        Undo.RegisterFullObjectHierarchyUndo(panel.gameObject, "Restyle player profile");
        BakeHeadingFont();
        var root = panel.transform;
        var appearance = panel.GetComponent<PlayerInfoAppearance>();
        if (!appearance) appearance = Undo.AddComponent<PlayerInfoAppearance>(panel.gameObject);
        Set(panel, "appearance", appearance);
        var bindings = new List<PlayerInfoAppearance.Binding>();
        var controls = new List<PlayerInfoAppearance.ControlBinding>();
        void Bind(Graphic graphic, Role role) {
            if (!graphic) throw new InvalidOperationException("Missing profile graphic for " + role);
            bindings.Add(new PlayerInfoAppearance.Binding { graphic = graphic, role = role });
        }
        void Ink(string path, Role role) => Bind(root.Find(path).GetComponent<Graphic>(), role);
        void Button(string path, Role fill, Role ink) {
            var control = root.Find(path).GetComponent<Selectable>();
            var image = control.GetComponent<Image>();
            image.sprite = null; image.type = Image.Type.Simple; image.raycastTarget = true; control.targetGraphic = image;
            controls.Add(new PlayerInfoAppearance.ControlBinding { control = control, label = control.GetComponentInChildren<TMP_Text>(true), fill = fill, ink = ink });
        }
        var surface = panel.GetComponent<Image>(); surface.sprite = null; surface.type = Image.Type.Simple;
        Bind(surface, Role.Surface);
        Place(Ref<Image>(panel, "profileImage").rectTransform, 28, 28, 104, 104);
        var header = Background(root, "HeaderBackground", 0, 0, 800, 152); header.transform.SetAsFirstSibling(); Bind(header, Role.Header);
        foreach (var path in new[] { "usernamePanel", "useridPanel", "titlePanel", "titlePanel (1)", "titlePanel (2)" }) {
            var image = root.Find(path).GetComponent<Image>(); image.color = Color.clear; image.sprite = null; image.raycastTarget = false;
        }
        foreach (var path in new[] { "usernamePanel/username", "useridPanel/userid", "titlePanel/title", "titlePanel (1)/title", "titlePanel (2)/title" }) Ink(path, Role.HeaderInk);
        Button("copyuserid", Role.Control, Role.ControlInk);
        Button("AddFriend", Role.Accent, Role.OnAccent);
        Button("ChangeTitle", Role.Control, Role.ControlInk);
        var rankSwitch = root.Find("titlePanel (1)/SwitchRank").GetComponent<Image>();
        controls.Add(new PlayerInfoAppearance.ControlBinding { control = rankSwitch.GetComponent<Button>(), fill = Role.HeaderInk });
        Ink("titlePanel (2)/Slider (1)/Background", Role.ProgressTrack);
        Ink("titlePanel (2)/Slider (1)/Fill Area/Fill", Role.Accent);
        var handle = root.Find("titlePanel (2)/Slider (1)/Handle Slide Area/Handle").GetComponent<Image>();
        handle.color = Color.clear; handle.raycastTarget = false;

        var categories = root.Find("TypeContainer"); Place((RectTransform)categories, 24, 164, 752, 40);
        int categoryIndex = 0;
        foreach (string name in new[] { "Match", "Custom" }) {
            var control = categories.Find(name).GetComponent<Button>();
            Place((RectTransform)control.transform, categoryIndex++ * 164, 0, 148, 40);
            control.image.sprite = null; control.image.type = Image.Type.Simple;
            var text = control.GetComponentInChildren<TMP_Text>(true); Place(text.rectTransform, 0, 0, 148, 40); Format(text, 24, TextAlignmentOptions.Midline);
            var line = Background(control.transform, "SelectedLine", 0, 38, 148, 2);
            Set(panel, name == "Match" ? "rankedTabIndicator" : "customTabIndicator", line);
        }
        var baseline = Background(categories, "Baseline", 0, 39, 752, 1); baseline.transform.SetAsFirstSibling(); Bind(baseline, Role.Divider);
        Place((RectTransform)root.Find("RuleContainer"), 24, 216, 752, 44);
        Place((RectTransform)root.Find("RecentBigWin"), 24, 280, 752, 112);
        foreach (var control in root.Find("RuleContainer").GetComponentsInChildren<Button>(true)) {
            control.image.sprite = null; control.image.type = Image.Type.Simple;
            var text = control.GetComponentInChildren<TMP_Text>(true); Format(text, 22, TextAlignmentOptions.Midline, true);
        }
        Place((RectTransform)root.Find("RuleContainer/Rule_sichuan"), 384, 0, 192, 44);
        Place((RectTransform)root.Find("RuleContainer/OtherRules"), 584, 0, 168, 44);
        var other = Ref<TMP_Dropdown>(panel, "otherRulesDropdown");
        TMP_Dropdown CustomFilter(string name, string field, string placeholder, float x, float width) {
            var node = other.transform.parent.Find(name);
            var dropdown = node ? node.GetComponent<TMP_Dropdown>() : Object.Instantiate(other, other.transform.parent);
            dropdown.name = name;
            dropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
            Place((RectTransform)dropdown.transform, x, 0, width, 44);
            dropdown.template.anchorMin = dropdown.template.anchorMax = new Vector2(1, 0);
            dropdown.template.pivot = new Vector2(1, 1);
            dropdown.template.anchoredPosition = new Vector2(0, -4);
            dropdown.template.sizeDelta = new Vector2(320, 288);
            dropdown.captionText.rectTransform.offsetMin = new Vector2(14, 0);
            dropdown.captionText.rectTransform.offsetMax = new Vector2(-30, 0);
            Text(dropdown.placeholder.GetComponent<TMP_Text>(), placeholder);
            Set(panel, field, dropdown);
            return dropdown;
        }
        var mainRules = CustomFilter("MainRules", "mainRulesDropdown", "主规则", 384, 144);
        var subRules = CustomFilter("SubRules", "subRulesDropdown", "子规则", 536, 216);
        Set(panel, "ruleCatalog", AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/DataLab/Data/statisticsRules.json"));
        foreach (var dropdown in new[] { other, mainRules, subRules }) {
            dropdown.image.sprite = null; dropdown.image.type = Image.Type.Simple;
            var template = dropdown.template;
            Bind(template.GetComponent<Image>(), Role.Control);
            foreach (var text in template.GetComponentsInChildren<TMP_Text>(true)) Bind(text, Role.ControlInk);
            foreach (var toggle in template.GetComponentsInChildren<Toggle>(true)) {
                controls.Add(new PlayerInfoAppearance.ControlBinding { control = toggle, fill = Role.Control, ink = Role.ControlInk });
                if (toggle.graphic) Bind(toggle.graphic, Role.Accent);
            }
            Bind(dropdown.placeholder, Role.ControlInk);
            var dropdownScroll = template.GetComponent<ScrollRect>();
            if (dropdownScroll.verticalScrollbar) {
                Bind(dropdownScroll.verticalScrollbar.GetComponent<Image>(), Role.Detail);
                controls.Add(new PlayerInfoAppearance.ControlBinding { control = dropdownScroll.verticalScrollbar, fill = Role.Divider });
            }
        }
        panel.RefreshRuleControls();

        foreach (string section in new[] { "RecentBigWin", "RecentTrend" }) {
            var parent = root.Find(section);
            var body = parent.GetComponent<Image>();
            if (!body) body = parent.gameObject.AddComponent<Image>();
            body.sprite = null; body.type = Image.Type.Simple; body.raycastTarget = false; Bind(body, Role.Detail);
            var band = Background(parent, "HeadingBackground", 0, 0, 752, 36); band.transform.SetAsFirstSibling(); Bind(band, Role.Section);
            var heading = parent.Find("Heading").GetComponent<TMP_Text>();
            Place(heading.rectTransform, 12, 0, section == "RecentBigWin" ? 180 : 728, 36); Format(heading, 24, TextAlignmentOptions.MidlineLeft);
            Bind(heading, Role.Ink);
        }
        var handCaption = Ref<TMP_Text>(panel, "winLabel"); Place(handCaption.rectTransform, 200, 0, 540, 36); Format(handCaption, 23, TextAlignmentOptions.MidlineRight, true); Bind(handCaption, Role.Ink);
        Ink("RecentBigWin/Hand/NoRecentWin", Role.Muted);
        foreach (var text in root.Find("RecentTrend").GetComponentsInChildren<TMP_Text>(true).Where(t => t.name.StartsWith("Place_"))) {
            text.rectTransform.sizeDelta = new Vector2(44,32);
            Format(text, 20, TextAlignmentOptions.MidlineLeft); Bind(text, Role.Muted);
        }
        var scroll = Ref<ScrollRect>(Ref<PlayerInfoStatistics>(panel, "statistics"), "scroll");
        Bind(scroll.verticalScrollbar.GetComponent<Image>(), Role.Detail);
        controls.Add(new PlayerInfoAppearance.ControlBinding { control = scroll.verticalScrollbar, fill = Role.Divider });
        Place((RectTransform)root.Find("RecentTrend"), 24, 400, 752, 180);
        Place((RectTransform)root.Find("RecentTrend/PlaceLine"), 56, 60, 696, 96);
        for (int i = 0; i < 4; i++)
            Place((RectTransform)root.Find("RecentTrend/Place_" + i), 6, 44 + i * 32, 44, 32);
        Place((RectTransform)scroll.transform, 24, 596, 752, 284);
        scroll.viewport.offsetMin = scroll.viewport.offsetMax = Vector2.zero;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        var scrollbarRect = (RectTransform)scroll.verticalScrollbar.transform;
        scrollbarRect.anchoredPosition = new Vector2(18, 0);

        var close = Ref<Button>(panel, "closeButton"); Place((RectTransform)close.transform, 744, 12, 44, 44);
        Button(close.name, Role.Close, Role.CloseInk);
        foreach (string stroke in new[] { "CloseStrokeA", "CloseStrokeB" }) {
            var image = close.transform.Find(stroke).GetComponent<Image>(); var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f,.5f); rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(22, 2.4f);
            image.raycastTarget = false; Bind(image, Role.CloseInk);
        }
        foreach (var text in panel.GetComponentsInChildren<TMP_Text>(true)) Normal(text);
        BakePrefabs();
        foreach (var entry in panel.GetComponentsInChildren<PlayerInfoEntry>(true)) BakeEntry(entry);
        foreach (var cell in panel.GetComponentsInChildren<PlayerInfoStatField>(true)) BakeField(cell);
        appearance.ConfigureBaked(panel, Ref<PlayerInfoStatistics>(panel, "statistics"), Ref<PlayerInfoTrendChart>(panel, "trendChart"), bindings.ToArray(), controls.ToArray());
        EditorUtility.SetDirty(panel); EditorUtility.SetDirty(appearance);
    }

    const string HeadingFontPath = "Assets/Salasasa/Fonts/PlayerInfoHeadingMedium.asset";
    static void BakeHeadingFont() {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(HeadingFontPath);
        string labels = "全庄战東西南风風副局番种達达成次数數总總计計無无固定时時限全莊戰番種" + new string(Enumerable.Range(32, 95).Select(c => (char)c).ToArray());
        if (font && labels.All(c => font.characterTable.Any(item => item.unicode == c))) return;
        bool created = !font;
        var source = AssetDatabase.LoadAssetAtPath<Font>(PlatformFontAutomation.FullSource("Medium"));
        if (created) font = TMP_FontAsset.CreateFontAsset(source, 72, 8, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDF16, 1024, 1024, AtlasPopulationMode.Dynamic, false);
        font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        var fontSource = new SerializedObject(font); fontSource.FindProperty("m_SourceFontFile").objectReferenceValue = source; fontSource.ApplyModifiedPropertiesWithoutUndo();
        font.ReadFontAssetDefinition();
        font.name = "PlayerInfoHeadingMedium";
        font.TryAddCharacters(new string(labels.Distinct().Where(c => !font.characterTable.Any(item => item.unicode == c)).ToArray()), out string missing);
        if (!string.IsNullOrEmpty(missing)) throw new InvalidOperationException("Profile heading font missing " + missing);
        font.atlasPopulationMode = AtlasPopulationMode.Static;
        font.normalStyle = font.boldStyle = 0;
        font.fallbackFontAssetTable = new List<TMP_FontAsset> { AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/font/思源黑体/SourceHanSansSC-Regular SDF.asset") };
        if (created) {
            AssetDatabase.CreateAsset(font, HeadingFontPath);
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var texture in font.atlasTextures) { texture.filterMode = FilterMode.Bilinear; AssetDatabase.AddObjectToAsset(texture, font); }
        }
        foreach (var texture in font.atlasTextures) EditorUtility.SetDirty(texture);
        var data = new SerializedObject(font); data.FindProperty("m_SourceFontFile").objectReferenceValue = null;
        data.FindProperty("m_ClearDynamicDataOnBuild").boolValue = false; data.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(font);
    }

    static void BakePrefabs() {
        var field = PrefabUtility.LoadPrefabContents(FieldPath);
        try { BakeField(field.GetComponent<PlayerInfoStatField>()); PrefabUtility.SaveAsPrefabAsset(field, FieldPath); }
        finally { PrefabUtility.UnloadPrefabContents(field); }
        var entry = PrefabUtility.LoadPrefabContents(EntryPath);
        try { BakeEntry(entry.GetComponent<PlayerInfoEntry>()); PrefabUtility.SaveAsPrefabAsset(entry, EntryPath); }
        finally { PrefabUtility.UnloadPrefabContents(entry); }
    }

    static void BakeField(PlayerInfoStatField field) {
        Set(field, "separator", field.transform.Find("Separator").GetComponent<Image>());
        foreach (var text in field.GetComponentsInChildren<TMP_Text>(true)) Normal(text);
        var label = Ref<TMP_Text>(field, "label"); Format(label, 22, TextAlignmentOptions.MidlineLeft, true); label.margin = new Vector4(10, 0, 0, 0);
        var value = Ref<TMP_Text>(field, "value"); Format(value, 22, TextAlignmentOptions.MidlineRight, true); value.margin = new Vector4(0, 0, 10, 0);
        field.ApplyAppearance(PlayerInfoAppearance.CreatePalette()); EditorUtility.SetDirty(field);
    }

    static void BakeEntry(PlayerInfoEntry entry) {
        Set(entry, "detailsBackground", entry.transform.Find("Details").GetComponent<Image>());
        var data = new SerializedObject(entry); var arrows = data.FindProperty("arrowStrokes");
        var strokes = Ref<Image>(entry,"expandArrow").GetComponentsInChildren<Image>(true).Where(g => g.name.EndsWith("Stroke")).ToArray();
        arrows.arraySize = strokes.Length; for (int i=0;i<strokes.Length;i++) arrows.GetArrayElementAtIndex(i).objectReferenceValue = strokes[i]; data.ApplyModifiedPropertiesWithoutUndo();
        var accent = Ref<Image>(entry,"expandedAccent").rectTransform;
        accent.anchorMin = new Vector2(0,1); accent.anchorMax = new Vector2(1,1); accent.pivot = new Vector2(.5f,1); accent.anchoredPosition = new Vector2(0,-62); accent.sizeDelta = new Vector2(0,2);
        foreach (var image in new[]{Ref<Button>(entry,"expandButton").targetGraphic as Image,Ref<Image>(entry,"detailsBackground")}) {image.sprite=null;image.type=Image.Type.Simple;}
        Set(entry, "headingMedium", AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(HeadingFontPath));
        foreach (string obsolete in new[] { "Button/Text (TMP)", "Header/BorderTop", "Header/BorderBottom", "Header/BorderLeft", "Header/BorderRight" }) {
            var node = entry.transform.Find(obsolete);
            if (node) Object.DestroyImmediate(node.gameObject);
        }
        foreach (var text in entry.GetComponentsInChildren<TMP_Text>(true)) Normal(text);
        entry.ApplyAppearance(PlayerInfoAppearance.CreatePalette()); EditorUtility.SetDirty(entry);
    }

    static Image Background(Transform parent, string name, float x,float y,float w,float h) {
        var node = parent.Find(name);
        if (!node) { var go = new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image)); go.layer=parent.gameObject.layer; node=go.transform; node.SetParent(parent,false); }
        var image=node.GetComponent<Image>();image.sprite=null;image.type=Image.Type.Simple;image.raycastTarget=false;Place(image.rectTransform,x,y,w,h);return image;
    }
    static T Ref<T>(Object target,string name) where T:Object => (T)new SerializedObject(target).FindProperty(name).objectReferenceValue;
    static void Set(Object target,string name,Object value) {var data=new SerializedObject(target);data.FindProperty(name).objectReferenceValue=value;data.ApplyModifiedPropertiesWithoutUndo();}
    static void Place(RectTransform r,float x,float y,float w,float h) {r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);r.localScale=Vector3.one;}
    static void Text(TMP_Text text,string value) {text.text=value;var locale=text.GetComponent<LocalizedPlatformLabel>();if(locale)locale.key=value;}
    static void Normal(TMP_Text text) {text.fontStyle=FontStyles.Normal;text.fontWeight=FontWeight.Regular;var binding=text.GetComponent<PlatformFontBinding>();if(binding){binding.authoredStyle=FontStyles.Normal;binding.authoredWeight=FontWeight.Regular;binding.styleCaptured=true;}}
    static void Format(TMP_Text text,float size,TextAlignmentOptions alignment,bool fit=false) {
        Normal(text);text.fontSize=text.fontSizeMax=size;text.fontSizeMin=14;text.enableAutoSizing=fit;text.textWrappingMode=TextWrappingModes.NoWrap;text.overflowMode=TextOverflowModes.Ellipsis;text.alignment=alignment;text.raycastTarget=false;
        var locale=text.GetComponent<LocalizedPlatformLabel>();if(locale){locale.authoredSize=size;locale.minimumSize=14;locale.authoredAutoSize=fit;}
    }
}
#endif
