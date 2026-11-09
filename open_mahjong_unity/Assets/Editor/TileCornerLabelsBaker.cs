#if UNITY_EDITOR
using System.Linq;
using System.Reflection;
using Salasasa.Home;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Targeted, repeatable authoring of the real settings row and first-use modal.</summary>
public static class TileCornerLabelsBaker {
    private static readonly Color Ink = SettingsStyleCollection.Hex("263D58");
    private static readonly Color Blue = SettingsStyleCollection.Hex("41679E");

    [MenuItem("Tools/Mahjong/Bake tile corner labels")]
    public static void Bake() {
        var settings = Resources.FindObjectsOfTypeAll<SettingsStyleCollection>()
            .Single(s => s.gameObject.scene.path == "Assets/Scenes/MainScene.unity");
        if (settings.gameObject.scene.isDirty)
            throw new System.InvalidOperationException("Save MainScene changes before baking tile labels.");
        foreach (var page in settings.bakedPages.Where(p => p.category == 2)) AddSetting(page);
        // Keep the collection's serialized current-page references in sync too.
        int variant = settings.bakedPages.Single(p => p.stage == settings.stage).variant;
        settings.controls = settings.bakedPages.Where(p => p.variant == variant)
            .OrderBy(p => p.category).SelectMany(p => p.controls).ToArray();
        EditorUtility.SetDirty(settings);
        EditorSceneManager.MarkSceneDirty(settings.gameObject.scene);
        EditorSceneManager.SaveScene(settings.gameObject.scene);
        BakeModal();
        AssetDatabase.SaveAssets();
    }

    private static void AddSetting(SettingsBakedPage page) {
        if (page.controls.Any(f => f.key == "TileCornerLabelsRow")) return;
        var source = page.controls.Single(f => f.key == "ActionButtonColorRow");
        var root = Object.Instantiate(source.root, source.root.parent);
        root.name = "TileCornerLabelsRow";
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Add tile labels setting");
        var field = new SettingsStyleField();
        Component Remap(Component component) {
            if (!component) return null;
            string path = AnimationUtility.CalculateTransformPath(component.transform, source.root);
            var target = string.IsNullOrEmpty(path) ? root : root.Find(path);
            return target.GetComponent(component.GetType());
        }
        foreach (var member in typeof(SettingsStyleField).GetFields(BindingFlags.Public | BindingFlags.Instance)) {
            object value = member.GetValue(source);
            if (value is Component component) value = Remap(component);
            else if (value is Button[] buttons) value = buttons.Select(b => (Button)Remap(b)).ToArray();
            member.SetValue(field, value);
        }
        field.key = root.name;
        field.UseSegments = true;
        field.label.text = "卡牌角标";
        field.label.GetComponent<LocalizedPlatformLabel>()?.SetSource("卡牌角标");
        var first = page.controls.First(f => f.section == 1);
        float rowStep = first.root.rect.height + 4;
        root.anchoredPosition = first.root.anchoredPosition + Vector2.down * (2 * rowStep);
        page.controls = page.controls.Concat(new[] { field }).ToArray();
        var section = page.sections[1];
        section.root.sizeDelta += Vector2.up * rowStep;
        section.surface.rectTransform.sizeDelta += Vector2.up * rowStep;
        page.sections[2].root.anchoredPosition += Vector2.down * rowStep;
        page.content.sizeDelta += Vector2.up * rowStep;
        var presentation = page.GetComponent<SettingsPagePresentation>();
        var hint = root.GetComponent<SettingsHintTarget>() ?? root.gameObject.AddComponent<SettingsHintTarget>();
        hint.key = field.key;
        hint.description = TileCornerLabel.Restriction;
        hint.owner = presentation;
        if (presentation) {
            presentation.targets = presentation.targets.Concat(new[] { hint }).ToArray();
            EditorUtility.SetDirty(presentation);
        }
        EditorUtility.SetDirty(page);
    }

    private static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height) {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        SettingsStyleCollection.Place(rect, x, y, width, height);
        return rect;
    }
    private static void Surface(RectTransform rect, Color fill, Color line) {
        rect.gameObject.AddComponent<SettingsShapeGraphic>().Paint(fill, line, 1, SettingsShapeGraphic.Shape.Round, 10);
    }
    private static TMP_Text Label(string name, Transform parent, string source, TMP_FontAsset font,
        float x, float y, float width, float height, float size, Color color) {
        var label = Rect(name, parent, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.fontStyle = FontStyles.Normal;
        label.fontSize = size;
        label.color = color;
        label.text = source;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
        return label;
    }
    [MenuItem("Tools/Mahjong/Bake English tile label choice")]
    public static void BakeModal() {
        var root = new GameObject("TileCornerLabelChoice", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(Image));
        try {
            var stretch = (RectTransform)root.transform;
            stretch.anchorMin = Vector2.zero; stretch.anchorMax = Vector2.one;
            stretch.offsetMin = stretch.offsetMax = Vector2.zero;
            root.GetComponent<Image>().color = new Color(.04f, .07f, .12f, .78f);
            UiCanvasLayers.Apply(root.GetComponent<Canvas>(), UiCanvasLayers.Modal, UiCanvasLayers.ModalOrder + 100);
            var font = PlatformTypography.Resolve(PlatformTypography.ChineseRegular, "Tile labels") ?? TMP_Settings.defaultFontAsset;
            var panel = Rect("Panel", stretch, 0, 0, 1100, 550);
            panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.one * .5f;
            Surface(panel, SettingsStyleCollection.Hex("F9FCFD"), SettingsStyleCollection.Hex("BCC9D8"));
            Label("Title", panel, "Choose your tiles", PlatformTypography.LatinBold ?? font, 48, 28, 930, 50, 34, Ink);
            var close = Rect("Close", panel, 1004, 26, 48, 48);
            Surface(close, Color.clear, SettingsStyleCollection.Hex("BCC9D8"));
            var closeButton = close.gameObject.AddComponent<Button>();
            closeButton.targetGraphic = close.GetComponent<SettingsShapeGraphic>();
            closeButton.colors = SettingsStyleCollection.InteractionColors();
            Label("Icon", close, "×", font, 0, 0, 48, 48, 30, Ink).alignment = TextAlignmentOptions.Center;
            Transform[] previews = new Transform[2];
            Button[] buttons = new Button[2];
            for (int i = 0; i < 2; i++) {
                bool labels = i == 1;
                var column = Rect(labels ? "WithLabels" : "WithoutLabels", panel, 48 + i * 514, 106, 490, 364);
                Surface(column, SettingsStyleCollection.Hex("EDF2F7"), SettingsStyleCollection.Hex("D5DFE9"));
                var row = Rect("Examples", column, 30, 24, 430, 242);
                var layout = row.gameObject.AddComponent<GridLayoutGroup>();
                layout.cellSize = new Vector2(82, 116);
                layout.spacing = new Vector2(18, 10);
                layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                layout.constraintCount = 4;
                layout.childAlignment = TextAnchor.MiddleCenter;
                previews[i] = row;
                var button = Rect("Choose", column, 30, 286, 430, 54);
                Surface(button, labels ? Blue : Color.white, Blue);
                buttons[i] = button.gameObject.AddComponent<Button>();
                buttons[i].targetGraphic = button.GetComponent<SettingsShapeGraphic>();
                buttons[i].colors = SettingsStyleCollection.InteractionColors();
                var caption = Label("Label", button, labels ? "With labels" : "Without labels", font, 12, 0, 406, 54, 22, labels ? Color.white : Ink);
                caption.alignment = TextAlignmentOptions.Center;
            }
            Label("Restriction", panel, TileCornerLabel.EnglishRestriction, font, 48, 494, 1004, 30, 20, Ink);
            var choice = root.AddComponent<TileCornerLabelChoice>();
            var serialized = new SerializedObject(choice);
            serialized.FindProperty("panel").objectReferenceValue = panel;
            serialized.FindProperty("enabledPreview").objectReferenceValue = previews[1];
            serialized.FindProperty("disabledPreview").objectReferenceValue = previews[0];
            serialized.FindProperty("enableButton").objectReferenceValue = buttons[1];
            serialized.FindProperty("disableButton").objectReferenceValue = buttons[0];
            serialized.FindProperty("closeButton").objectReferenceValue = closeButton;
            serialized.FindProperty("tilePrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/GameScene/StaticTileCard.prefab").GetComponent<StaticCard>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, "Assets/Resources/TileCornerLabelChoice.prefab");
        } finally { Object.DestroyImmediate(root); }
    }
}
#endif
