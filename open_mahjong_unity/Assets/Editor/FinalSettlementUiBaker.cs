#if UNITY_EDITOR
using System;
using System.Linq;
using Salasasa.Home;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class FinalSettlementUiBaker {
    static TMP_FontAsset font;
    static readonly Color Ink = C("F4F1E9"), Muted = C("C0C0C0"), Gold = C("E4B976");
    static Color C(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var c); return c; }

    public static void Bake(Scene scene) {
        var panel = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<EndGamePanel>(true)).Single();
        var so = new SerializedObject(panel);
        var rows = so.FindProperty("rankDisplays");
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Salasasa/Fonts/CompleteCityBody.asset");
        if (!font) throw new InvalidOperationException("Settlement body font is missing.");
        Undo.RegisterFullObjectHierarchyUndo(panel.gameObject, "Refine final settlement");

        var roundPresentation = panel.GetComponentInParent<RoundEndPresentation>();
        if (roundPresentation) Undo.SetTransformParent(panel.transform, roundPresentation.transform.parent, "Separate match settlement");
        panel.name = "EndGamePanel";
        foreach (var press in panel.GetComponentsInChildren<PressHoldAlphaController>(true)) UnityEngine.Object.DestroyImmediate(press);
        var group = GetOrAdd<CanvasGroup>(panel.gameObject);
        group.alpha = 1; group.interactable = true; group.blocksRaycasts = true; group.ignoreParentGroups = false;
        var fade = GetOrAdd<CanvasGroupFadeIn>(panel.gameObject);
        so.FindProperty("fadeInEffect").objectReferenceValue = fade;
        var resultCanvas = GetOrAdd<Canvas>(panel.gameObject);
        UiCanvasLayers.Apply(resultCanvas, UiCanvasLayers.Board, UiCanvasLayers.SettlementOrder + 1);
        if (!panel.GetComponent<UnityEngine.UI.GraphicRaycaster>()) panel.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        var canvas = resultCanvas.rootCanvas;
        var canvasLayout = GetOrAdd<CanvasReferenceLayout>(panel.gameObject);
        canvasLayout.referenceArea = (RectTransform)canvas.transform;
        canvasLayout.scaleSettings = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
        if (!canvasLayout.scaleSettings) throw new InvalidOperationException("Settlement canvas scale settings are missing.");
        canvasLayout.Apply(true);
        var dim = GetOrAdd<UnityEngine.UI.Image>(panel.gameObject);
        dim.sprite = null; dim.type = UnityEngine.UI.Image.Type.Simple; dim.color = new Color(0, 0, 0, .58f); dim.raycastTarget = true;

        var host = Rect("ResultPosition", panel.transform); Center(host, 0, 0);
        var window = Rect("ResultWindow", host); Center(window, 1920, 1080);
        var fit = GetOrAdd<ViewportPanelFit>(host.gameObject);
        fit.panel = window; fit.authoredPosition = Vector2.zero; fit.margin = 24;
        Remove(window, "Surface");
        Remove(window, "Header");
        Remove(window, "TitleMark");
        Remove(window, "Title");
        Remove(window, "HeaderDivider");
        Remove(window, "FooterDivider");
        Remove(window, "Columns");
        Remove(window, "Verification");

        var list = Rect("Results", window); Top(list, 412, 74, 1026, 940);
        var layout = GetOrAdd<UnityEngine.UI.VerticalLayoutGroup>(list.gameObject);
        layout.spacing = 44; layout.padding = new RectOffset(); layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        for (int i = 0; i < rows.arraySize; i++) {
            var row = rows.GetArrayElementAtIndex(i);
            var name = Ref<TextMeshProUGUI>(row, "username");
            var rowRoot = (RectTransform)name.transform.parent;
            rowRoot.SetParent(list, false); rowRoot.name = "PlayerResult" + (i + 1); rowRoot.SetSiblingIndex(i);
            Top(rowRoot, 0, i * 246, 1026, 202);
            var element = GetOrAdd<UnityEngine.UI.LayoutElement>(rowRoot.gameObject);
            element.preferredHeight = element.minHeight = 202; element.flexibleHeight = 0;
            var surface = GetOrAdd<UnityEngine.UI.Image>(rowRoot.gameObject);
            surface.sprite = null; surface.type = UnityEngine.UI.Image.Type.Simple; surface.color = C("252525"); surface.raycastTarget = false;
            foreach (string retired in new[] { "RankAccent", "RankSurface", "PlayerCell", "ScoreCell", "RatingCell", "CurrencyCell" }) Remove(rowRoot, retired);
            PlaceValue(Ref<TMP_Text>(row, "rank"), 24, 46, 64, 110, 42, i == 0 ? Gold : Ink, TextAlignmentOptions.Center);
            foreach (string caption in new[] { "PlayerLabel", "ScoreLabel", "RatingLabel", "CurrencyLabel" }) Remove(rowRoot, caption);
            PlaceValue(name, 120, 26, 440, 50, 30, Ink, TextAlignmentOptions.MidlineLeft);
            name.richText = false;
            var score = Ref<TMP_Text>(row, "score");
            PlaceValue(score, 584, 26, 418, 50, 32, Ink, TextAlignmentOptions.MidlineLeft); score.richText = false;
            var points = Ref<TMP_Text>(row, "pt");
            PlaceValue(points, 120, 126, 440, 50, 28, Ink, TextAlignmentOptions.MidlineLeft); points.richText = true;
            points.fontSizeMin = 16;
            var amount = Ref<TextMeshProUGUI>(row, "currency");
            if (!amount) { amount = (TextMeshProUGUI)Label("Currency", rowRoot, 584, 137, 382, 50, "", 30, Gold); row.FindPropertyRelative("currency").objectReferenceValue = amount; }
            PlaceValue(amount, 584, 126, 382, 50, 30, Gold, TextAlignmentOptions.MidlineLeft);
            var icon = Ref<UnityEngine.UI.Image>(row, "currencyIcon");
            if (!icon) icon = Box("CurrencyIcon", amount.transform, 0, 0, 30, 30, Color.white);
            icon.transform.SetParent(amount.transform, false); Center(icon.rectTransform, 30, 30);
            icon.sprite = SalaCoinAppearance.SpriteAt(0); icon.color = Color.white; icon.preserveAspect = true; icon.raycastTarget = false;
            var follow = GetOrAdd<CurrencyLabelIcon>(amount.gameObject); follow.icon = icon; follow.gap = 8; follow.afterText = true;
            row.FindPropertyRelative("currencyIcon").objectReferenceValue = icon;
        }

        var seed = (TMP_Text)so.FindProperty("gameRandomSeed").objectReferenceValue;
        var proof = Rect("VerificationPanel", window); Top(proof, 1458, 74, 434, 740);
        var proofSurface = GetOrAdd<UnityEngine.UI.Image>(proof.gameObject);
        proofSurface.sprite = null; proofSurface.color = C("252525"); proofSurface.raycastTarget = true;
        Label("Title", proof, 24, 16, 386, 42, "牌局校验", 24, Ink);
        Remove(proof, "Close");
        var scrollRoot = Rect("Scroll", proof); Top(scrollRoot, 24, 76, 386, 574);
        var viewport = Rect("Viewport", scrollRoot); Fill(viewport);
        var scrollHit = GetOrAdd<UnityEngine.UI.Image>(viewport.gameObject);
        scrollHit.sprite = null; scrollHit.color = Color.clear; scrollHit.raycastTarget = true;
        if (!viewport.GetComponent<UnityEngine.UI.RectMask2D>()) viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        seed.transform.SetParent(viewport, false); seed.name = "Content";
        PlaceValue(seed, 0, 0, 386, 574, 20, Ink, TextAlignmentOptions.TopLeft);
        seed.rectTransform.anchorMin = new Vector2(0, 1); seed.rectTransform.anchorMax = Vector2.one;
        seed.rectTransform.sizeDelta = new Vector2(0, 574);
        seed.richText = false; seed.enableAutoSizing = false; seed.textWrappingMode = TextWrappingModes.Normal;
        seed.overflowMode = TextOverflowModes.Overflow;
        var contentFit = GetOrAdd<UnityEngine.UI.ContentSizeFitter>(seed.gameObject);
        contentFit.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        contentFit.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        var scroll = GetOrAdd<UnityEngine.UI.ScrollRect>(scrollRoot.gameObject);
        scroll.viewport = viewport; scroll.content = seed.rectTransform;
        scroll.horizontal = false; scroll.vertical = true; scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        scroll.inertia = true; scroll.scrollSensitivity = 30;
        var track = Box("Scrollbar", proof, 412, 76, 6, 574, C("404040")); track.raycastTarget = true;
        var thumb = Box("Thumb", track.transform, 0, 0, 6, 574, C("B0B0B0")); thumb.raycastTarget = true;
        Fill(thumb.rectTransform);
        var bar = GetOrAdd<UnityEngine.UI.Scrollbar>(track.gameObject);
        bar.handleRect = thumb.rectTransform; bar.targetGraphic = thumb; bar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = bar; scroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide;
        scroll.verticalNormalizedPosition = 1;
        var copy = (UnityEngine.UI.Button)so.FindProperty("copyMasterSeedButton").objectReferenceValue;
        copy.transform.SetParent(proof, false); copy.name = "CopyVerification";
        StyleButton(copy, 24, 664, 386, 52, C("4A4A4A"), Ink);
        SetLabel(copy.GetComponentInChildren<TMP_Text>(true), "复制校验码");
        proof.gameObject.SetActive(true);

        var exit = (UnityEngine.UI.Button)so.FindProperty("goHomeButton").objectReferenceValue;
        exit.transform.SetParent(window, false); exit.name = "Exit";
        StyleButton(exit, 1674, 942, 218, 62, Gold, C("211C15"));
        SetLabel(exit.GetComponentInChildren<TMP_Text>(true), "返回大厅");
        var back = (UnityEngine.UI.Button)so.FindProperty("returnToTableButton").objectReferenceValue;
        if (!back) { back = UnityEngine.Object.Instantiate(exit, window); back.onClick = new UnityEngine.UI.Button.ButtonClickedEvent(); so.FindProperty("returnToTableButton").objectReferenceValue = back; }
        back.transform.SetParent(window, false); back.name = "ReturnToTable";
        StyleButton(back, 1458, 942, 200, 62, C("4A4A4A"), Ink);
        SetLabel(back.GetComponentInChildren<TMP_Text>(true), "返回牌桌"); back.gameObject.SetActive(false);
        so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(panel);
        Validate(scene);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    public static void Validate(Scene scene) {
        var panel = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<EndGamePanel>(true)).Single();
        if (panel.GetComponentInParent<RoundEndPresentation>() || panel.GetComponentsInChildren<PressHoldAlphaController>(true).Length != 0)
            throw new InvalidOperationException("Final settlement must be independent of per-hand presentation and press opacity.");
        var canvas = panel.GetComponent<Canvas>();
        if (!canvas || !canvas.overrideSorting || canvas.sortingOrder != UiCanvasLayers.SettlementOrder + 1 || !panel.GetComponent<UnityEngine.UI.GraphicRaycaster>())
            throw new InvalidOperationException("Final settlement drawing and input layer is missing.");
        var window = panel.transform.Find("ResultPosition/ResultWindow");
        if (window.Find("Surface") || window.Find("Header") || window.Find("TitleMark") || window.Find("HeaderDivider") || window.Find("FooterDivider") || window.Find("Columns") || window.Find("Verification"))
            throw new InvalidOperationException("Retired final settlement controls remain.");
        var proof = window.Find("VerificationPanel");
        if (!proof || !proof.gameObject.activeSelf || proof.Find("Close") || !proof.Find("CopyVerification"))
            throw new InvalidOperationException("Final settlement verification must stay visible.");
        var scrollRoot = proof.Find("Scroll");
        var scroll = scrollRoot ? scrollRoot.GetComponent<UnityEngine.UI.ScrollRect>() : null;
        if (!scroll || !scroll.content || !scroll.viewport || !scroll.viewport.GetComponent<UnityEngine.UI.RectMask2D>())
            throw new InvalidOperationException("Final settlement verification text must be clipped and scrollable independently of its copy button.");
    }

    static T GetOrAdd<T>(GameObject target) where T : UnityEngine.Component {
        T component = target.GetComponent<T>();
        if (!component) component = target.AddComponent<T>();
        if (!component) throw new InvalidOperationException("Unable to author " + typeof(T).Name + " on " + target.name + ".");
        return component;
    }

    static void Remove(Transform parent, string name) { var child = parent.Find(name); if (child) UnityEngine.Object.DestroyImmediate(child.gameObject); }
    static T Ref<T>(SerializedProperty row, string key) where T : UnityEngine.Object => row.FindPropertyRelative(key).objectReferenceValue as T;
    static void PlaceValue(TMP_Text t, float x, float y, float w, float h, float size, Color c, TextAlignmentOptions alignment) {
        Top(t.rectTransform, x, y, w, h); Style(t, size, c, alignment);
        var binding = t.GetComponent<LocalizedPlatformLabel>(); if (binding) UnityEngine.Object.DestroyImmediate(binding);
        t.fontSizeMin = size - 5; t.fontSizeMax = size; t.enableAutoSizing = true;
    }
    internal static RectTransform Rect(string name, Transform parent) {
        var existing = parent.Find(name) as RectTransform;
        if (existing) return existing;
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); return (RectTransform)go.transform;
    }
    internal static void Top(RectTransform r, float x, float y, float w, float h) { r.localScale = Vector3.one; r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h); }
    internal static void Center(RectTransform r, float w, float h) { r.localScale = Vector3.one; r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f); r.anchoredPosition = Vector2.zero; r.sizeDelta = new Vector2(w, h); }
    internal static void Fill(RectTransform r) { r.localScale = Vector3.one; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
    static UnityEngine.UI.Image Box(string name, Transform parent, float x, float y, float w, float h, Color c) {
        var rect = Rect(name, parent); Top(rect, x, y, w, h);
        var image = GetOrAdd<UnityEngine.UI.Image>(rect.gameObject); image.color = c; image.raycastTarget = false; return image;
    }
    static TMP_Text Label(string name, Transform parent, float x, float y, float w, float h, string text, float size, Color c, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft) {
        var rect = Rect(name, parent); Top(rect, x, y, w, h);
        var label = GetOrAdd<TextMeshProUGUI>(rect.gameObject);
        Style(label, size, c, alignment); SetLabel(label, text); return label;
    }
    internal static void SetLabel(TMP_Text label, string text) {
        label.text = text;
        var localized = GetOrAdd<LocalizedPlatformLabel>(label.gameObject);
        localized.key = text; localized.authoredSize = label.fontSize; localized.minimumSize = Mathf.Max(12, label.fontSize - 5); localized.authoredAutoSize = true;
    }
    static void Style(TMP_Text t, float size, Color c, TextAlignmentOptions alignment) {
        t.font = font; t.fontSharedMaterial = font.material; t.fontStyle = FontStyles.Normal; t.fontWeight = FontWeight.Regular;
        t.fontSize = size; t.fontSizeMax = size; t.fontSizeMin = size - 5; t.enableAutoSizing = true; t.color = c; t.alignment = alignment;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis; t.raycastTarget = false; t.margin = Vector4.zero;
        var binding = GetOrAdd<PlatformFontBinding>(t.gameObject);
        binding.authoredFont = font; binding.authoredMaterial = font.material;
        binding.authoredStyle = FontStyles.Normal; binding.authoredWeight = FontWeight.Regular; binding.styleCaptured = true;
        binding.preserveAuthoredTypeface = binding.preserveAuthoredChineseFace = false;
        EditorUtility.SetDirty(binding); EditorUtility.SetDirty(t);
    }
    static void StyleButton(UnityEngine.UI.Button b, float x, float y, float w, float h, Color surface, Color ink) {
        Top((RectTransform)b.transform, x, y, w, h); var im = GetOrAdd<UnityEngine.UI.Image>(b.gameObject);
        im.sprite = null; im.color = surface; im.raycastTarget = true; b.targetGraphic = im; b.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
        var colors = UnityEngine.UI.ColorBlock.defaultColorBlock; colors.highlightedColor = new Color(1.14f, 1.14f, 1.14f); colors.pressedColor = new Color(.8f, .8f, .8f); b.colors = colors;
        var t = b.GetComponentInChildren<TMP_Text>(true); Fill(t.rectTransform); t.rectTransform.offsetMin = new Vector2(8, 0); t.rectTransform.offsetMax = new Vector2(-8, 0); Style(t, 20, ink, TextAlignmentOptions.Center);
    }
}
#endif
