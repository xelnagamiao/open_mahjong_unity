#if UNITY_EDITOR
using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ActionChoicePanelBaker {
    public static void Bake(Scene scene) {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Bake the choice panel outside Play Mode");
        var canvas = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<GameCanvas>(true)).Single();
        var canvasData = new SerializedObject(canvas);
        var source = (RectTransform)canvas.ActionBlockContenter;
        var button = (ActionButton)canvasData.FindProperty("ActionButtonPrefab").objectReferenceValue;
        var block = (GameObject)canvasData.FindProperty("ActionBlockPrefab").objectReferenceValue;
        var font = button.TextObject.font;
        var image = block.GetComponent<UnityEngine.UI.Image>();
        var panel = canvas.ChoicePanel;
        if (panel == null) {
            var existing = source.parent.Find("ActionChoicePanel");
            panel = existing != null ? existing.GetComponent<ActionChoicePanel>() : null;
        }
        if (panel == null) {
            var root = New("ActionChoicePanel", source.parent);
            Undo.RegisterCreatedObjectUndo(root, "Bake action choice panel");
            panel = root.AddComponent<ActionChoicePanel>();
            var rect = (RectTransform)root.transform;
            rect.anchorMin = source.anchorMin; rect.anchorMax = source.anchorMax;
            rect.pivot = new Vector2(.5f, 0); rect.anchoredPosition = source.anchoredPosition;
        }
        Undo.RegisterFullObjectHierarchyUndo(panel.gameObject, "Bake action choice panel");
        Get<UnityEngine.UI.LayoutElement>(panel.gameObject).ignoreLayout = true;
        var background = Get<UnityEngine.UI.Image>(panel.gameObject);
        CopyImage(background, image); background.raycastTarget = true;
        var outline = Get<UnityEngine.UI.Outline>(panel.gameObject);
        outline.effectDistance = new Vector2(1, -1); outline.useGraphicAlpha = true;
        var scroll = Get<UnityEngine.UI.ScrollRect>(panel.gameObject);
        var viewport = Child(panel.transform, "Viewport");
        var maskImage = Get<UnityEngine.UI.Image>(viewport.gameObject);
        maskImage.color = Color.white; maskImage.raycastTarget = true;
        Get<UnityEngine.UI.Mask>(viewport.gameObject).showMaskGraphic = false;
        var content = Child(viewport, "Content");
        var track = Child(panel.transform, "Scrollbar");
        var trackImage = Get<UnityEngine.UI.Image>(track.gameObject); PlainImage(trackImage);
        var bar = Get<UnityEngine.UI.Scrollbar>(track.gameObject);
        var slide = Child(track, "SlidingArea"); Stretch(slide, 3);
        var handle = Child(slide, "Handle"); Stretch(handle, 0);
        var handleImage = Get<UnityEngine.UI.Image>(handle.gameObject); PlainImage(handleImage);
        bar.handleRect = handle; bar.targetGraphic = handleImage;
        bar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
        bar.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        scroll.inertia = true; scroll.decelerationRate = .12f; scroll.scrollSensitivity = 50;
        scroll.verticalScrollbar = bar; scroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.Permanent;
        var rowRoot = Child(panel.transform, "RowTemplate");
        var row = Get<ActionChoiceRow>(rowRoot.gameObject);
        var action = Get<ActionBlock>(rowRoot.gameObject); action.enabled = false;
        var rowImage = Get<UnityEngine.UI.Image>(rowRoot.gameObject); CopyImage(rowImage, image); rowImage.raycastTarget = true;
        var summary = Label(rowRoot, "Summary", font, 24);
        var details = Label(rowRoot, "Details", font, 18);
        var tiles = Child(rowRoot, "Tiles");
        var rowData = new SerializedObject(row);
        Assign(rowData, "action", action); Assign(rowData, "background", rowImage); Assign(rowData, "summary", summary);
        Assign(rowData, "details", details); Assign(rowData, "tiles", tiles); rowData.ApplyModifiedPropertiesWithoutUndo();
        var data = new SerializedObject(panel);
        Assign(data, "background", background); Assign(data, "border", outline); Assign(data, "scroll", scroll);
        Assign(data, "viewport", viewport); Assign(data, "content", content); Assign(data, "scrollbar", bar);
        Assign(data, "scrollbarTrack", trackImage); Assign(data, "scrollbarHandle", handleImage); Assign(data, "rowTemplate", row);
        data.ApplyModifiedPropertiesWithoutUndo();
        Assign(canvasData, "actionChoicePanel", panel); canvasData.ApplyModifiedPropertiesWithoutUndo();
        panel.ApplyPalette(); rowRoot.gameObject.SetActive(false); panel.gameObject.SetActive(false);
        foreach (var component in panel.GetComponentsInChildren<Component>(true)) if (component) EditorUtility.SetDirty(component);
        EditorUtility.SetDirty(canvas); EditorSceneManager.MarkSceneDirty(scene);
        Validate(scene);
    }

    public static void Validate(Scene scene) {
        foreach (var canvas in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<GameCanvas>(true))) {
            var panel = canvas.ChoicePanel;
            if (panel == null || !panel.HasReferences || panel.transform.parent != canvas.ActionBlockContenter.parent)
                throw new InvalidOperationException("The reusable action choice panel has not been baked or bound");
            var scroll = panel.GetComponent<UnityEngine.UI.ScrollRect>();
            if (scroll.viewport == null || scroll.content == null || scroll.viewport.GetComponent<UnityEngine.UI.Mask>() == null
                || scroll.verticalScrollbar == null || scroll.horizontal)
                throw new InvalidOperationException("The action choice viewport and vertical scrollbar must be wired");
            if (panel.GetComponentsInChildren<ActionChoiceRow>(true).Length != 1)
                throw new InvalidOperationException("The scene must contain one reusable row template, no preview choices");
            foreach (var label in panel.GetComponentsInChildren<TMP_Text>(true))
                if (label.fontStyle != FontStyles.Normal || label.fontWeight != FontWeight.Regular)
                    throw new InvalidOperationException("Choice panel text must retain regular weight");
        }
    }

    static GameObject New(string name, Transform parent) {
        var go = new GameObject(name, typeof(RectTransform)); go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false); return go;
    }
    static RectTransform Child(Transform parent, string name) {
        var found = parent.Find(name);
        return found != null ? (RectTransform)found : (RectTransform)New(name, parent).transform;
    }
    static T Get<T>(GameObject go) where T : Component => go.GetComponent<T>() ?? go.AddComponent<T>();
    static void Assign(SerializedObject data, string name, UnityEngine.Object value) { data.FindProperty(name).objectReferenceValue = value; }
    static void CopyImage(UnityEngine.UI.Image target, UnityEngine.UI.Image source) {
        target.sprite = source != null ? source.sprite : null;
        target.type = target.sprite != null && target.sprite.border != Vector4.zero ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
    }
    static void PlainImage(UnityEngine.UI.Image target) {
        target.sprite = null; target.overrideSprite = null;
        target.type = UnityEngine.UI.Image.Type.Simple; target.preserveAspect = false;
    }
    static void Stretch(RectTransform rect, float inset) {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.one * inset; rect.offsetMax = Vector2.one * -inset;
    }
    static TMP_Text Label(Transform parent, string name, TMP_FontAsset font, float size) {
        var root = Child(parent, name); var text = Get<TextMeshProUGUI>(root.gameObject);
        text.font = font; text.text = "";
        var binding = Get<LocalizedPlatformLabel>(root.gameObject); binding.fitTranslation = false;
        ActionChoiceRow.ConfigureText(text, size);
        return text;
    }
}
#endif
