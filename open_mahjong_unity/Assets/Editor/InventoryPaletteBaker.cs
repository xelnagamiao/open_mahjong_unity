#if UNITY_EDITOR
using System;
using System.Linq;
using Salasasa.Home;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class InventoryPaletteBaker
{
    public static void Bake(Scene scene)
    {
        if (Application.isPlaying) throw new InvalidOperationException("背包配色只能在编辑模式下烘焙。");
        var plans = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<InterfacePlansController>(true)).Single();
        var palette = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<InventoryPalette>(true)).Single();
        Undo.RecordObject(palette, "Bake inventory palettes");
        palette.palette = InventoryPalette.Graphite();
        foreach (var binding in palette.bindings)
            if (binding.graphic && binding.graphic.name == "Details") binding.role = InventoryPalette.Role.Ink;
        palette.ApplyCurrent();
        BakeStructure(palette);
        palette.ApplyCurrent();
        RemovePaletteChoices(plans);
        foreach (var component in palette.GetComponentsInChildren<Component>(true))
            if (component) EditorUtility.SetDirty(component);
        EditorUtility.SetDirty(palette);
    }

    static void BakeStructure(InventoryPalette palette)
    {
        var panel = palette.GetComponent<InventoryPanel>();
        var layout = palette.GetComponent<InventoryLayoutVariants>();
        var root = panel.transform;
        Undo.RegisterFullObjectHierarchyUndo(panel.gameObject, "Refine inventory layout");
        foreach (string path in new[] { "Accent", "WarehouseEmblem", "Close/CloseGlyph" })
        {
            var decoration = root.Find(path);
            if (decoration) Undo.DestroyObjectImmediate(decoration.gameObject);
        }
        panel.BakeWarehouseCategories();
        var spec = layout.BakedVariants[0];
        var poses = spec.rects.Where(pose => pose.target).ToList();
        void Position(string path, float left, float top, float width, float height)
        {
            var rect = (RectTransform)root.Find(path);
            Place(rect, left, top, width, height);
            var pose = new InventoryLayoutVariants.RectPose {
                target = rect, anchorMin = rect.anchorMin, anchorMax = rect.anchorMax,
                pivot = rect.pivot, position = rect.anchoredPosition, size = rect.sizeDelta, scale = rect.localScale
            };
            int index = poses.FindIndex(value => value.target == rect);
            if (index < 0) poses.Add(pose); else poses[index] = pose;
        }
        Position("Header", 1, 1, 1278, 68);
        Position("Heading", 24, 1, 440, 68);
        Position("CategoryBackground", 1, 69, 152, 634);
        Position("Close", 1223, 13, 44, 44);
        Position("DetailBackground", 930, 69, 349, 634);
        Position("DetailName", 950, 90, 310, 42);
        Position("DetailType", 950, 136, 310, 29);
        Position("Preview", 1015, 184, 180, 180);
        Position("DetailCount", 950, 384, 310, 31);
        Position("DetailRule", 950, 428, 310, 1);
        Position("DetailsViewport", 950, 444, 310, 180);
        Position("Quantity", 950, 576, 310, 40);
        Position("Action", 950, 632, 310, 48);
        var panelData = new SerializedObject(panel);
        panelData.FindProperty("detailRule").objectReferenceValue = root.Find("DetailRule").GetComponent<UnityEngine.UI.Image>();
        panelData.ApplyModifiedPropertiesWithoutUndo();
        foreach (string path in new[] { "DetailName", "DetailType", "DetailCount", "DetailsViewport/Details" })
        {
            var label = root.Find(path).GetComponent<TMP_Text>();
            label.text = "";
            var localized = label.GetComponent<LocalizedPlatformLabel>();
            if (localized) { localized.key = ""; localized.followSourceChanges = true; }
        }
        var empty = root.Find("ScrollView/Viewport/Empty").GetComponent<TMP_Text>();
        empty.text = "没有物品";
        var emptyLocale = empty.GetComponent<LocalizedPlatformLabel>();
        if (emptyLocale) { emptyLocale.key = "没有物品"; emptyLocale.followSourceChanges = true; }
        var detailBackground = root.Find("DetailBackground").GetComponent<UnityEngine.UI.Image>();
        detailBackground.sprite = null;
        detailBackground.type = UnityEngine.UI.Image.Type.Simple;
        var close = root.Find("Close").GetComponent<UnityEngine.UI.Button>();
        var closeFill = close.GetComponent<UnityEngine.UI.Image>();
        closeFill.sprite = null;
        closeFill.type = UnityEngine.UI.Image.Type.Simple;
        closeFill.raycastTarget = true;
        close.targetGraphic = closeFill;
        close.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
        var state = UnityEngine.UI.ColorBlock.defaultColorBlock;
        state.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1);
        state.pressedColor = new Color(.8f, .8f, .8f, 1);
        state.selectedColor = Color.white;
        state.fadeDuration = .08f;
        close.colors = state;
        var heading = root.Find("Heading").GetComponent<TMP_Text>();
        heading.alignment = TextAlignmentOptions.MidlineLeft;
        Label(close.transform, "Label", "×", heading.font, 32, Color.white, 0, 0, 44, 44, TextAlignmentOptions.MidlineGeoAligned);
        Position("Close/Label", 0, 0, 44, 44);
        var bindings = palette.bindings.Where(binding => binding.graphic).ToList();
        void Bind(string path, InventoryPalette.Role role)
        {
            var graphic = root.Find(path).GetComponent<UnityEngine.UI.Graphic>();
            var binding = bindings.Find(value => value.graphic == graphic);
            if (binding == null) bindings.Add(new InventoryPalette.Binding { graphic = graphic, role = role });
            else binding.role = role;
        }
        Bind("Heading", InventoryPalette.Role.HeaderInk);
        Bind("Close", InventoryPalette.Role.Close);
        Bind("Close/Label", InventoryPalette.Role.CloseInk);
        palette.bindings = bindings.ToArray();
        spec.rects = poses.ToArray();
        spec.tints = Array.Empty<InventoryLayoutVariants.Tint>();
        EditorUtility.SetDirty(layout);
        layout.SelectVariant(0);
        panel.ValidateWarehouseUi();
    }

    static void RemovePaletteChoices(InterfacePlansController plans)
    {
        // 26 was the retired inventory palette option in serialized scenes.
        plans.choices = plans.choices.Where(group => (int)group.option != 26).ToArray();
        plans.presetDefaults = plans.presetDefaults.Where(choice => (int)choice.option != 26).ToArray();
        var page = plans.pages[1].transform;
        var row = page.Find("InventoryPalette");
        if (row) Undo.DestroyObjectImmediate(row.gameObject);
        float last = page.Cast<RectTransform>().Where(r => r.gameObject.activeSelf)
            .Select(r => -r.anchoredPosition.y + r.sizeDelta.y).DefaultIfEmpty(0).Max();
        plans.pageHeights[1] = last + 128;
        plans.Refresh();
        EditorUtility.SetDirty(plans);
    }

    static RectTransform Rect(Transform parent, string name)
    {
        var rect = parent.Find(name) as RectTransform;
        if (rect) return rect;
        rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(rect.gameObject, "Add inventory palette option");
        return rect;
    }

    static void Label(Transform parent, string name, string value, TMP_FontAsset font, float size, Color color,
        float left, float top, float width, float height, TextAlignmentOptions alignment, string localizationKey = null)
    {
        var rect = Rect(parent, name);
        Place(rect, left, top, width, height);
        var label = rect.GetComponent<TextMeshProUGUI>() ?? Undo.AddComponent<TextMeshProUGUI>(rect.gameObject);
        label.font = font;
        label.fontSharedMaterial = font.material;
        label.text = value;
        label.fontSize = label.fontSizeMax = size;
        label.fontSizeMin = 11;
        label.fontStyle = FontStyles.Normal;
        label.fontWeight = FontWeight.Regular;
        label.color = color;
        label.alignment = alignment;
        label.richText = false;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        var localized = rect.GetComponent<LocalizedPlatformLabel>() ?? Undo.AddComponent<LocalizedPlatformLabel>(rect.gameObject);
        localized.key = localizationKey ?? value;
        localized.followSourceChanges = false;
        localized.authoredSize = size;
        localized.minimumSize = 11;
        localized.typographyBaked = true;
        var typography = rect.GetComponent<PlatformFontBinding>() ?? Undo.AddComponent<PlatformFontBinding>(rect.gameObject);
        typography.authoredFont = font;
        typography.authoredMaterial = font.material;
        typography.authoredStyle = FontStyles.Normal;
        typography.authoredWeight = FontWeight.Regular;
        typography.styleCaptured = true;
    }

    static void Place(RectTransform rect, float left, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
        rect.localScale = Vector3.one;
    }
}
#endif
