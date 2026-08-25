using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 仅菜单执行：用 Editor API 在已打开的 MainScene 里绘制通知活动面板。
/// 禁止 InitializeOnLoad 自动跑。执行后必须手动保存场景。
/// </summary>
public static class NoticePanelBaker {
    private const string PrefabDir = "Assets/Prefabs/Notice";
    private const string ItemPrefabPath = PrefabDir + "/ActivityItem.prefab";
    private const string FontResource = "font/Chinese/AlibabaPuHuiTi/AlibabaPuHuiTi-3-55-Regular SDF";

    private static readonly Color CardBg = new Color(0.08f, 0.11f, 0.18f, 1f);
    private static readonly Color TitleGold = new Color(1f, 0.9f, 0.55f, 1f);
    private static readonly Color DescGray = new Color(0.78f, 0.8f, 0.84f, 1f);
    private static readonly Color HeaderGold = new Color(1f, 0.62f, 0.08f, 1f);

    private static readonly ExampleSpec[] Examples = {
        new ExampleSpec("春季公开赛", "4 月开赛，欢迎报名", new Color(0.16f, 0.28f, 0.48f, 1f)),
        new ExampleSpec("周末活动室", "长期开放的练习场", new Color(0.28f, 0.2f, 0.08f, 1f)),
        new ExampleSpec("新规则体验", "南雀规则试玩周", new Color(0.12f, 0.32f, 0.22f, 1f)),
        new ExampleSpec("平台维护通知", "周四凌晨例行维护", new Color(0.36f, 0.14f, 0.14f, 1f)),
    };

    private struct ExampleSpec {
        public readonly string Title;
        public readonly string Desc;
        public readonly Color Cover;

        public ExampleSpec(string title, string desc, Color cover) {
            Title = title;
            Desc = desc;
            Cover = cover;
        }
    }

    [MenuItem("Tools/Notice/重建通知活动面板")]
    public static void Build() {
        if (!BuildSilent()) {
            EditorUtility.DisplayDialog("通知面板", "当前打开的场景里找不到 NoticePanel。请先打开 MainScene。", "好的");
            return;
        }
        EditorUtility.DisplayDialog(
            "通知面板",
            "已在 NoticePanel 下绘制 Header、标准 Scroll View、4 条示例和详情窗。\n请立刻 Ctrl+S 保存场景，否则关闭时选 Don't Save 会全部丢掉。运行时会隐藏示例并加载 /activity-assets。",
            "好的"
        );
    }

    public static bool BuildSilent() {
        GameObject notice = FindInOpenScenes("NoticePanel");
        if (notice == null) return false;

        Undo.RegisterFullObjectHierarchyUndo(notice, "重建通知活动面板");
        GameObject itemPrefab = SaveActivityItemPrefab();
        Transform chrome = notice.transform.Find("Panel");
        if (chrome == null) chrome = notice.transform;
        WipeGenerated(chrome);
        HideLegacyText(chrome);

        TMP_Text headerTitle = BuildHeader(chrome);
        ScrollViewRefs list = BuildStandardScrollView(chrome, "Scroll View", new Vector2(24f, 20f), new Vector2(-24f, -72f));
        TMP_Text emptyHint = BuildEmptyHint(chrome);
        ActivityDetailPanel detail = BuildDetailPanel(chrome);
        PlaceExamples(list.Content, itemPrefab);

        NoticePanel panel = notice.GetComponent<NoticePanel>();
        if (panel == null) panel = Undo.AddComponent<NoticePanel>(notice);
        SerializedObject so = new SerializedObject(panel);
        so.FindProperty("listContent").objectReferenceValue = list.Content;
        so.FindProperty("itemTemplate").objectReferenceValue = itemPrefab;
        so.FindProperty("listRoot").objectReferenceValue = list.Root;
        so.FindProperty("detailPanel").objectReferenceValue = detail;
        so.FindProperty("emptyHint").objectReferenceValue = emptyHint;
        so.FindProperty("headerTitle").objectReferenceValue = headerTitle;
        so.ApplyModifiedPropertiesWithoutUndo();

        ClearImageSprites(notice);
        if (itemPrefab != null) ClearImageSprites(itemPrefab);

        EditorUtility.SetDirty(notice);
        EditorUtility.SetDirty(panel);
        if (notice.scene.IsValid()) EditorSceneManager.MarkSceneDirty(notice.scene);
        return true;
    }

    public static void ClearImageSprites(GameObject root) {
        if (root == null) return;
        Image[] images = root.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++) {
            if (images[i] == null) continue;
            images[i].sprite = null;
            EditorUtility.SetDirty(images[i]);
        }
    }

    private static GameObject SaveActivityItemPrefab() {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder(PrefabDir)) AssetDatabase.CreateFolder("Assets/Prefabs", "Notice");

        GameObject source = BuildActivityItemObject(null);
        source.name = "ActivityItem";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, ItemPrefabPath);
        Object.DestroyImmediate(source);
        AssetDatabase.SaveAssets();
        return prefab;
    }

    private static GameObject BuildActivityItemObject(Transform parent) {
        GameObject root = CreateUi("ActivityItem", parent);
        Image bg = root.AddComponent<Image>();
        bg.sprite = null;
        bg.color = CardBg;
        bg.raycastTarget = true;
        Button button = root.AddComponent<Button>();
        button.targetGraphic = bg;
        button.transition = Selectable.Transition.ColorTint;

        VerticalLayoutGroup layout = root.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = root.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        LayoutElement rootLe = root.AddComponent<LayoutElement>();
        rootLe.minHeight = 188f;
        rootLe.flexibleWidth = 1f;

        GameObject coverGo = CreateUi("Cover", root.transform);
        RawImage cover = coverGo.AddComponent<RawImage>();
        cover.color = new Color(0.14f, 0.18f, 0.26f, 1f);
        cover.raycastTarget = false;
        LayoutElement coverLe = coverGo.AddComponent<LayoutElement>();
        coverLe.preferredHeight = 148f;
        coverLe.minHeight = 120f;
        coverLe.flexibleWidth = 1f;

        TMP_Text placeholder = AddTmp(CreateUi("Placeholder", coverGo.transform), "封面", 22, new Color(1f, 1f, 1f, 0.35f));
        placeholder.alignment = TextAlignmentOptions.Center;
        Stretch(placeholder.rectTransform);

        TMP_Text title = AddTmp(CreateUi("Title", root.transform), "活动名称", 24, TitleGold);
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.enableWordWrapping = true;
        title.overflowMode = TextOverflowModes.Overflow;
        LayoutElement titleLe = title.gameObject.AddComponent<LayoutElement>();
        titleLe.minHeight = 32f;
        ContentSizeFitter titleFit = title.gameObject.AddComponent<ContentSizeFitter>();
        titleFit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        titleFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TMP_Text desc = AddTmp(CreateUi("Desc", root.transform), "活动简介", 18, DescGray);
        desc.alignment = TextAlignmentOptions.TopLeft;
        desc.enableWordWrapping = true;
        LayoutElement descLe = desc.gameObject.AddComponent<LayoutElement>();
        descLe.minHeight = 22f;
        ContentSizeFitter descFit = desc.gameObject.AddComponent<ContentSizeFitter>();
        descFit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        descFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ActivityItem item = root.AddComponent<ActivityItem>();
        SerializedObject so = new SerializedObject(item);
        so.FindProperty("coverImage").objectReferenceValue = cover;
        so.FindProperty("titleText").objectReferenceValue = title;
        so.FindProperty("descText").objectReferenceValue = desc;
        so.FindProperty("placeholderText").objectReferenceValue = placeholder;
        so.FindProperty("button").objectReferenceValue = button;
        so.ApplyModifiedPropertiesWithoutUndo();
        return root;
    }

    private static void PlaceExamples(Transform content, GameObject prefab) {
        for (int i = 0; i < Examples.Length; i++) {
            ExampleSpec spec = Examples[i];
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, content);
            instance.name = "Example_" + (i + 1);
            Undo.RegisterCreatedObjectUndo(instance, "活动示例");
            TMP_Text title = instance.transform.Find("Title")?.GetComponent<TMP_Text>();
            TMP_Text desc = instance.transform.Find("Desc")?.GetComponent<TMP_Text>();
            RawImage cover = instance.transform.Find("Cover")?.GetComponent<RawImage>();
            if (title != null) title.text = spec.Title;
            if (desc != null) {
                desc.text = spec.Desc;
                desc.gameObject.SetActive(true);
            }
            if (cover != null) {
                cover.texture = null;
                cover.color = spec.Cover;
            }
            EditorUtility.SetDirty(instance);
        }
    }

    private static TMP_Text BuildHeader(Transform chrome) {
        GameObject header = CreateUi("Header", chrome);
        RectTransform rt = header.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, 64f);

        TMP_Text title = AddTmp(CreateUi("Title", header.transform), "活动", 32, TitleGold);
        title.alignment = TextAlignmentOptions.MidlineLeft;
        Stretch(title.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(28f, 0f), new Vector2(-28f, 0f));
        return title;
    }

    private static TMP_Text BuildEmptyHint(Transform chrome) {
        GameObject go = CreateUi("EmptyHint", chrome);
        Stretch(go.GetComponent<RectTransform>(), new Vector2(0.2f, 0.4f), new Vector2(0.8f, 0.6f), Vector2.zero, Vector2.zero);
        TMP_Text text = AddTmp(go, "暂无活动", 24, new Color(0.7f, 0.72f, 0.76f, 1f));
        text.alignment = TextAlignmentOptions.Center;
        go.SetActive(false);
        return text;
    }

    private static ActivityDetailPanel BuildDetailPanel(Transform chrome) {
        GameObject root = CreateUi("ActivityDetailPanel", chrome);
        Stretch(root.GetComponent<RectTransform>());
        Image dim = root.AddComponent<Image>();
        dim.sprite = null;
        dim.color = new Color(0.05f, 0.06f, 0.08f, 0.98f);
        dim.raycastTarget = true;

        GameObject header = CreateUi("Header", root.transform);
        RectTransform headerRt = header.GetComponent<RectTransform>();
        headerRt.anchorMin = new Vector2(0f, 1f);
        headerRt.anchorMax = new Vector2(1f, 1f);
        headerRt.pivot = new Vector2(0.5f, 1f);
        headerRt.anchoredPosition = Vector2.zero;
        headerRt.sizeDelta = new Vector2(0f, 72f);

        TMP_Text title = AddTmp(CreateUi("Title", header.transform), "活动名称", 30, Color.white);
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.enableWordWrapping = false;
        title.overflowMode = TextOverflowModes.Ellipsis;
        Stretch(title.rectTransform, new Vector2(0f, 0.38f), new Vector2(1f, 1f), new Vector2(28f, 0f), new Vector2(-100f, 0f));

        TMP_Text status = AddTmp(CreateUi("Status", header.transform), "活动已结束", 20, HeaderGold);
        status.alignment = TextAlignmentOptions.TopLeft;
        Stretch(status.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.42f), new Vector2(28f, 8f), new Vector2(-100f, 0f));
        status.gameObject.SetActive(false);

        GameObject closeGo = CreateUi("Close", header.transform);
        RectTransform closeRt = closeGo.GetComponent<RectTransform>();
        closeRt.anchorMin = new Vector2(1f, 0.5f);
        closeRt.anchorMax = new Vector2(1f, 0.5f);
        closeRt.pivot = new Vector2(1f, 0.5f);
        closeRt.anchoredPosition = new Vector2(-20f, 0f);
        closeRt.sizeDelta = new Vector2(72f, 40f);
        Image closeBg = closeGo.AddComponent<Image>();
        closeBg.sprite = null;
        closeBg.color = HeaderGold;
        Button closeBtn = closeGo.AddComponent<Button>();
        closeBtn.targetGraphic = closeBg;
        TMP_Text closeLabel = AddTmp(CreateUi("Label", closeGo.transform), "关闭", 20, new Color(0.12f, 0.06f, 0.02f, 1f));
        closeLabel.alignment = TextAlignmentOptions.Center;
        Stretch(closeLabel.rectTransform);

        ScrollViewRefs bodyScroll = BuildStandardScrollView(root.transform, "Scroll View", new Vector2(24f, 20f), new Vector2(-24f, -80f));

        GameObject bodyGo = CreateUi("BodyText", bodyScroll.Content);
        TMP_Text bodyText = AddTmp(bodyGo, "正文", 22, new Color(0.92f, 0.92f, 0.92f, 1f));
        bodyText.alignment = TextAlignmentOptions.TopLeft;
        bodyText.enableWordWrapping = true;
        ContentSizeFitter bodyFit = bodyGo.AddComponent<ContentSizeFitter>();
        bodyFit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        bodyFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        LayoutElement bodyLe = bodyGo.AddComponent<LayoutElement>();
        bodyLe.minHeight = 40f;
        bodyLe.flexibleWidth = 1f;

        GameObject imageGo = CreateUi("ImageTemplate", bodyScroll.Content);
        RawImage raw = imageGo.AddComponent<RawImage>();
        raw.color = Color.white;
        raw.raycastTarget = false;
        LayoutElement imageLe = imageGo.AddComponent<LayoutElement>();
        imageLe.preferredHeight = 220f;
        imageLe.flexibleWidth = 1f;
        imageGo.SetActive(false);

        ActivityDetailPanel panel = root.AddComponent<ActivityDetailPanel>();
        SerializedObject so = new SerializedObject(panel);
        so.FindProperty("titleText").objectReferenceValue = title;
        so.FindProperty("statusText").objectReferenceValue = status;
        so.FindProperty("bodyText").objectReferenceValue = bodyText;
        so.FindProperty("closeButton").objectReferenceValue = closeBtn;
        so.FindProperty("bodyContent").objectReferenceValue = bodyScroll.Content;
        so.FindProperty("imageTemplate").objectReferenceValue = imageGo;
        so.FindProperty("bodyScroll").objectReferenceValue = bodyScroll.Root.GetComponent<ScrollRect>();
        so.ApplyModifiedPropertiesWithoutUndo();
        root.SetActive(false);
        return panel;
    }

    private struct ScrollViewRefs {
        public GameObject Root;
        public Transform Content;
    }

    private static ScrollViewRefs BuildStandardScrollView(Transform parent, string name, Vector2 offsetMin, Vector2 offsetMax) {
        GameObject root = CreateUi(name, parent);
        Stretch(root.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, offsetMin, offsetMax);
        Image rootImage = root.AddComponent<Image>();
        rootImage.sprite = null;
        rootImage.color = new Color(1f, 1f, 1f, 0.04f);
        rootImage.raycastTarget = true;
        ScrollRect scroll = root.AddComponent<ScrollRect>();

        GameObject viewport = CreateUi("Viewport", root.transform);
        Stretch(viewport.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-20f, 0f));
        Image viewportImage = viewport.AddComponent<Image>();
        viewportImage.sprite = null;
        viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
        viewportImage.raycastTarget = true;
        viewport.AddComponent<RectMask2D>();

        GameObject content = CreateUi("Content", viewport.transform);
        RectTransform contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0f, 0f);
        VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 8, 24);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject barGo = CreateUi("Scrollbar Vertical", root.transform);
        RectTransform barRt = barGo.GetComponent<RectTransform>();
        barRt.anchorMin = new Vector2(1f, 0f);
        barRt.anchorMax = new Vector2(1f, 1f);
        barRt.pivot = new Vector2(1f, 0.5f);
        barRt.anchoredPosition = Vector2.zero;
        barRt.sizeDelta = new Vector2(16f, 0f);
        Image barBg = barGo.AddComponent<Image>();
        barBg.sprite = null;
        barBg.color = new Color(0.2f, 0.22f, 0.26f, 0.7f);
        Scrollbar bar = barGo.AddComponent<Scrollbar>();
        bar.direction = Scrollbar.Direction.BottomToTop;

        GameObject sliding = CreateUi("Sliding Area", barGo.transform);
        Stretch(sliding.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(2f, 2f), new Vector2(-2f, -2f));
        GameObject handle = CreateUi("Handle", sliding.transform);
        Stretch(handle.GetComponent<RectTransform>());
        Image handleImage = handle.AddComponent<Image>();
        handleImage.sprite = null;
        handleImage.color = new Color(1f, 0.62f, 0.08f, 0.85f);
        bar.targetGraphic = handleImage;
        bar.handleRect = handle.GetComponent<RectTransform>();

        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = contentRt;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 28f;
        scroll.inertia = true;
        scroll.decelerationRate = 0.135f;
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        scroll.verticalScrollbarSpacing = -3f;
        return new ScrollViewRefs { Root = root, Content = content.transform };
    }

    private static void WipeGenerated(Transform chrome) {
        string[] names = {
            "TitleBar", "ListRoot", "Header", "Scroll View", "ScrollView",
            "EmptyHint", "ActivityDetailPanel"
        };
        for (int i = chrome.childCount - 1; i >= 0; i--) {
            Transform child = chrome.GetChild(i);
            bool wipe = false;
            for (int n = 0; n < names.Length; n++) {
                if (child.name == names[n]) wipe = true;
            }
            if (wipe) Undo.DestroyObjectImmediate(child.gameObject);
        }
    }

    private static void HideLegacyText(Transform chrome) {
        for (int i = 0; i < chrome.childCount; i++) {
            Transform child = chrome.GetChild(i);
            if (child.name == "Text (TMP)" || child.name == "暂无通知") {
                child.gameObject.SetActive(false);
            }
        }
    }

    private static GameObject CreateUi(string name, Transform parent) {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        if (parent != null) go.transform.SetParent(parent, false);
        return go;
    }

    private static TMP_Text AddTmp(GameObject go, string text, float size, Color color) {
        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        if (tmp == null) tmp = go.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset font = Resources.Load<TMP_FontAsset>(FontResource);
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void Stretch(RectTransform rt) {
        Stretch(rt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
    }

    private static void Stretch(RectTransform rt, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax) {
        if (rt == null) return;
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    private static Sprite UiSprite() {
        return null;
    }

    private static GameObject FindInOpenScenes(string name) {
        for (int i = 0; i < SceneManager.sceneCount; i++) {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++) {
                Transform found = FindDeep(roots[r].transform, name);
                if (found != null) return found.gameObject;
            }
        }
        return null;
    }

    private static Transform FindDeep(Transform root, string name) {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++) {
            Transform nested = FindDeep(root.GetChild(i), name);
            if (nested != null) return nested;
        }
        return null;
    }
}
