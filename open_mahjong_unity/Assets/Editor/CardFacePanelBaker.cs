#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 把「牌面设置」面板、导航按钮和全量预览槽写进场景。运行时不再创建任何 UI。
/// 菜单：Tools/牌面设置/烘焙面板到场景
/// </summary>
public static class CardFacePanelBaker
{
    private const string MainScenePath = "Assets/Scenes/MainScene.unity";
    private static readonly Color PanelBg = new Color(0.06f, 0.07f, 0.10f, 0.97f);
    private static readonly Color ButtonBg = new Color(0.17f, 0.21f, 0.30f, 1f);
    private static readonly Color Accent = new Color(0.28f, 0.48f, 0.92f, 1f);
    private static readonly Color LabelColor = new Color(0.82f, 0.85f, 0.90f, 1f);
    private static readonly Vector2 SlotCell = new Vector2(64f, 90f);

    private static Sprite _whiteSprite;
    private static TMP_FontAsset _tmpFont;

    [MenuItem("Tools/牌面设置/烘焙面板到场景")]
    public static void Bake()
    {
        bool ok = BakeCurrentScene();
        if (ok)
        {
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
        }
        if (!Application.isBatchMode)
        {
            EditorUtility.DisplayDialog(
                "牌面面板烘焙",
                ok
                    ? "已写入牌面面板、牌面背景面板与导航按钮，并已保存场景。"
                    : "当前场景找不到 SceneConfigPanel，无法烘焙。",
                "好的");
        }
        else if (!ok)
        {
            throw new System.Exception("CardFacePanel bake failed: SceneConfigPanel not found.");
        }
    }

    public static void BakeFromCommandLine()
    {
        EditorSceneManager.OpenScene(MainScenePath);
        Bake();
    }

    private static bool BakeCurrentScene()
    {
        SceneConfigPanel scp = Object.FindFirstObjectByType<SceneConfigPanel>();
        if (scp == null)
        {
            return false;
        }

        Transform root = scp.transform;
        Transform oldPanel = root.Find("CardFacePanel");
        if (oldPanel != null)
        {
            Object.DestroyImmediate(oldPanel.gameObject);
        }

        EnsureNavButton(scp);

        RectTransform panelRt = NewRect("CardFacePanel", root);
        panelRt.anchorMin = new Vector2(0f, 1f);
        panelRt.anchorMax = new Vector2(0f, 1f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.anchoredPosition = new Vector2(1169f, -490f);
        panelRt.sizeDelta = new Vector2(900f, 820f);
        Image bg = panelRt.gameObject.AddComponent<Image>();
        bg.color = PanelBg;

        TMP_Text title = NewText(panelRt, "Title", "牌面设置", 22, Color.white, TextAnchor.MiddleCenter);
        StretchTop(title.rectTransform, 0f, 40f, 0f);

        Button closeBtn = NewButton(panelRt, "CloseButton", "关闭", ButtonBg, Color.white);
        RectTransform closeRt = (RectTransform)closeBtn.transform;
        closeRt.anchorMin = new Vector2(1f, 1f);
        closeRt.anchorMax = new Vector2(1f, 1f);
        closeRt.pivot = new Vector2(1f, 1f);
        closeRt.anchoredPosition = new Vector2(-8f, -6f);
        closeRt.sizeDelta = new Vector2(56f, 30f);

        Button tabStd = NewButton(panelRt, "TabStandard", "标准麻将牌", Accent, Color.white);
        PlaceTop(tabStd.transform as RectTransform, 20f, 56f, 160f, 36f);
        Button tabHq = NewButton(panelRt, "TabHongque", "虹雀麻将牌", ButtonBg, Color.white);
        PlaceTop(tabHq.transform as RectTransform, 188f, 56f, 160f, 36f);

        TMP_Text status = NewText(panelRt, "StatusText", "当前：官方标准牌面（雪风）", 15, LabelColor, TextAnchor.MiddleLeft);
        StretchTop(status.rectTransform, 20f, 24f, 100f);
        status.enableWordWrapping = true;
        status.overflowMode = TextOverflowModes.Ellipsis;

        RectTransform actions = NewRect("StandardActions", panelRt);
        StretchTop(actions, 20f, 40f, 132f);
        Button upload = NewButton(actions, "UploadButton", "上传自定义", Accent, Color.white);
        PlaceLeft(upload.transform as RectTransform, 0f, 0f, 120f, 36f);
        Button restore = NewButton(actions, "RestoreButton", "官方", ButtonBg, Color.white);
        PlaceLeft(restore.transform as RectTransform, 128f, 0f, 88f, 36f);
        Button fluffy = NewButton(actions, "PackFluffyButton", "Fluffy", ButtonBg, Color.white);
        PlaceLeft(fluffy.transform as RectTransform, 224f, 0f, 110f, 36f);
        Button hk = NewButton(actions, "PackHkButton", "香港麻将", ButtonBg, Color.white);
        PlaceLeft(hk.transform as RectTransform, 342f, 0f, 100f, 36f);

        RectTransform viewActions = NewRect("StandardViewActions", panelRt);
        StretchTop(viewActions, 20f, 40f, 176f);
        Button showHand = NewButton(viewActions, "ShowHandButton", "显示手牌牌面", Accent, Color.white);
        PlaceLeft(showHand.transform as RectTransform, 0f, 0f, 140f, 36f);
        Button showTable = NewButton(viewActions, "ShowTableButton", "显示3D牌面", ButtonBg, Color.white);
        PlaceLeft(showTable.transform as RectTransform, 148f, 0f, 130f, 36f);
        Button useBg = NewButton(viewActions, "UseBgButton", "使用牌面背景", ButtonBg, Color.white);
        PlaceLeft(useBg.transform as RectTransform, 286f, 0f, 140f, 36f);
        Button noBg = NewButton(viewActions, "NoBgButton", "不使用牌面背景", ButtonBg, Color.white);
        PlaceLeft(noBg.transform as RectTransform, 434f, 0f, 160f, 36f);

        GameObject standardScroll = BuildPreviewScroll(
            panelRt,
            "StandardPreviewScroll",
            "StandardPreviewContent",
            TilePackIds.StandardFaceIds,
            assignOfficialSprites: true);
        StretchTop(standardScroll.GetComponent<RectTransform>(), 20f, 440f, 224f);
        GameObject hongqueScroll = BuildPreviewScroll(
            panelRt,
            "HongquePreviewScroll",
            "HongquePreviewContent",
            TilePackIds.HongqueFaceIds,
            assignOfficialSprites: false);
        hongqueScroll.SetActive(false);

        BuildHelpScroll(panelRt, out TMP_Text help);

        CardFaceConfigPanel panel = panelRt.gameObject.AddComponent<CardFaceConfigPanel>();
        SerializedObject so = new SerializedObject(panel);
        so.FindProperty("tabStandardButton").objectReferenceValue = tabStd;
        so.FindProperty("tabHongqueButton").objectReferenceValue = tabHq;
        so.FindProperty("uploadButton").objectReferenceValue = upload;
        so.FindProperty("restoreButton").objectReferenceValue = restore;
        so.FindProperty("packFluffyButton").objectReferenceValue = fluffy;
        so.FindProperty("packHkButton").objectReferenceValue = hk;
        so.FindProperty("useBackgroundButton").objectReferenceValue = useBg;
        so.FindProperty("noBackgroundButton").objectReferenceValue = noBg;
        so.FindProperty("showHandButton").objectReferenceValue = showHand;
        so.FindProperty("showTableButton").objectReferenceValue = showTable;
        so.FindProperty("closeButton").objectReferenceValue = closeBtn;
        so.FindProperty("statusText").objectReferenceValue = status;
        so.FindProperty("helpText").objectReferenceValue = help;
        so.FindProperty("standardActions").objectReferenceValue = actions.gameObject;
        so.FindProperty("standardViewActions").objectReferenceValue = viewActions.gameObject;
        so.FindProperty("standardPreviewRoot").objectReferenceValue = standardScroll;
        so.FindProperty("hongquePreviewRoot").objectReferenceValue = hongqueScroll;
        so.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject sceneSo = new SerializedObject(scp);
        sceneSo.FindProperty("cardFacePanel").objectReferenceValue = panel;
        Button faceNav = FindNavButton(scp, "CardFace", "牌面", panelRt);
        if (faceNav != null) sceneSo.FindProperty("ShowCardFacePanelButton").objectReferenceValue = faceNav;
        CardBackConfigPanel backPanel = scp.GetComponentInChildren<CardBackConfigPanel>(true);
        if (backPanel != null) sceneSo.FindProperty("cardBackPanel").objectReferenceValue = backPanel;
        CardEdgePanel edgePanel = scp.GetComponentInChildren<CardEdgePanel>(true);
        if (edgePanel != null) sceneSo.FindProperty("cardEdgePanel").objectReferenceValue = edgePanel;
        Button backNav = FindNavButton(scp, "CardBack", "牌背", backPanel != null ? backPanel.transform : null);
        if (backNav != null) sceneSo.FindProperty("ShowCardBackPanelButton").objectReferenceValue = backNav;
        Button edgeNav = FindNavButton(scp, "CardEdge", "牌边", edgePanel != null ? edgePanel.transform : null);
        if (edgeNav != null) sceneSo.FindProperty("ShowCardEdgePanelButton").objectReferenceValue = edgeNav;
        sceneSo.ApplyModifiedPropertiesWithoutUndo();

        panelRt.gameObject.SetActive(false);
        BakeFaceBgPanel(scp);
        EditorSceneManager.MarkSceneDirty(scp.gameObject.scene);
        return true;
    }

    private static void BakeFaceBgPanel(SceneConfigPanel scp)
    {
        Transform root = scp.transform;
        Transform oldPanel = root.Find("CardFaceBackgroundPanel");
        if (oldPanel != null)
        {
            Object.DestroyImmediate(oldPanel.gameObject);
        }

        EnsureFaceBgNavButton(scp);

        RectTransform panelRt = NewRect("CardFaceBackgroundPanel", root);
        panelRt.anchorMin = new Vector2(0f, 1f);
        panelRt.anchorMax = new Vector2(0f, 1f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.anchoredPosition = new Vector2(1169f, -490f);
        panelRt.sizeDelta = new Vector2(900f, 820f);
        Image bg = panelRt.gameObject.AddComponent<Image>();
        bg.color = PanelBg;

        TMP_Text title = NewText(panelRt, "Title", "牌面背景", 22, Color.white, TextAnchor.MiddleCenter);
        StretchTop(title.rectTransform, 0f, 40f, 0f);

        Button closeBtn = NewButton(panelRt, "CloseButton", "关闭", ButtonBg, Color.white);
        RectTransform closeRt = (RectTransform)closeBtn.transform;
        closeRt.anchorMin = new Vector2(1f, 1f);
        closeRt.anchorMax = new Vector2(1f, 1f);
        closeRt.pivot = new Vector2(1f, 1f);
        closeRt.anchoredPosition = new Vector2(-8f, -6f);
        closeRt.sizeDelta = new Vector2(56f, 30f);

        TMP_Text handLabel = NewText(panelRt, "HandBgLabel", "手牌牌面背景", 16, LabelColor, TextAnchor.MiddleLeft);
        PlaceTop(handLabel.rectTransform, 40f, 56f, 360f, 28f);
        Image handPreview = NewPreview(panelRt, "HandBgPreview", 40f, 92f, 200f, 286f);

        TMP_Text backLabel = NewText(panelRt, "CardBackLabel", "手牌牌背", 16, LabelColor, TextAnchor.MiddleLeft);
        PlaceTop(backLabel.rectTransform, 460f, 56f, 360f, 28f);
        Image backPreview = NewPreview(panelRt, "CardBackPreview", 460f, 92f, 200f, 286f);

        Button uploadHand = NewButton(panelRt, "UploadHandBgButton", "上传手牌背景", Accent, Color.white);
        PlaceTop(uploadHand.transform as RectTransform, 40f, 390f, 160f, 36f);
        Button restoreHand = NewButton(panelRt, "RestoreHandBgButton", "恢复默认背景", ButtonBg, Color.white);
        PlaceTop(restoreHand.transform as RectTransform, 210f, 390f, 140f, 36f);
        Button uploadBack = NewButton(panelRt, "UploadCardBackButton", "上传手牌牌背", Accent, Color.white);
        PlaceTop(uploadBack.transform as RectTransform, 460f, 390f, 160f, 36f);
        Button clearBack = NewButton(panelRt, "ClearCardBackButton", "恢复默认牌背", ButtonBg, Color.white);
        PlaceTop(clearBack.transform as RectTransform, 630f, 390f, 140f, 36f);
        Button uploadPair = NewButton(panelRt, "UploadPairZipButton", "上传成对 zip", ButtonBg, Color.white);
        PlaceTop(uploadPair.transform as RectTransform, 40f, 436f, 200f, 36f);

        TMP_Text status = NewText(panelRt, "StatusText", "手牌背景：默认　手牌牌背：默认", 15, LabelColor, TextAnchor.MiddleLeft);
        StretchTop(status.rectTransform, 20f, 24f, 484f);

        TMP_Text help = NewText(panelRt, "HelpText", CardFaceBackgroundPanel.FormatHelp, 14, LabelColor, TextAnchor.UpperLeft);
        help.rectTransform.anchorMin = new Vector2(0f, 0f);
        help.rectTransform.anchorMax = new Vector2(1f, 0f);
        help.rectTransform.pivot = new Vector2(0.5f, 0f);
        help.rectTransform.anchoredPosition = new Vector2(0f, 16f);
        help.rectTransform.sizeDelta = new Vector2(-40f, 140f);
        help.enableWordWrapping = true;
        help.overflowMode = TextOverflowModes.Overflow;

        CardFaceBackgroundPanel panel = panelRt.gameObject.AddComponent<CardFaceBackgroundPanel>();
        SerializedObject so = new SerializedObject(panel);
        so.FindProperty("handBgPreview").objectReferenceValue = handPreview;
        so.FindProperty("cardBackPreview").objectReferenceValue = backPreview;
        so.FindProperty("uploadHandBgButton").objectReferenceValue = uploadHand;
        so.FindProperty("uploadCardBackButton").objectReferenceValue = uploadBack;
        so.FindProperty("uploadPairZipButton").objectReferenceValue = uploadPair;
        so.FindProperty("restoreHandBgButton").objectReferenceValue = restoreHand;
        so.FindProperty("clearCardBackButton").objectReferenceValue = clearBack;
        so.FindProperty("closeButton").objectReferenceValue = closeBtn;
        so.FindProperty("statusText").objectReferenceValue = status;
        so.FindProperty("helpText").objectReferenceValue = help;
        so.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject sceneSo = new SerializedObject(scp);
        sceneSo.FindProperty("cardFaceBgPanel").objectReferenceValue = panel;
        Button bgNav = FindNavButton(scp, "CardFaceBg", "牌面背景", panelRt);
        if (bgNav != null) sceneSo.FindProperty("ShowCardFaceBgPanelButton").objectReferenceValue = bgNav;
        sceneSo.ApplyModifiedPropertiesWithoutUndo();
        panelRt.gameObject.SetActive(false);
    }

    private static Image NewPreview(RectTransform parent, string name, float x, float y, float w, float h)
    {
        RectTransform rt = NewRect(name, parent);
        PlaceTop(rt, x, y, w, h);
        Image image = rt.gameObject.AddComponent<Image>();
        image.color = new Color(0.18f, 0.20f, 0.24f, 1f);
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    private static GameObject BuildPreviewScroll(
        RectTransform panelRt,
        string scrollName,
        string contentName,
        int[] tileIds,
        bool assignOfficialSprites)
    {
        RectTransform host = NewRect(scrollName, panelRt);
        StretchTop(host, 20f, 480f, 184f);
        Image hostBg = host.gameObject.AddComponent<Image>();
        hostBg.color = new Color(0.10f, 0.12f, 0.16f, 1f);
        hostBg.raycastTarget = true;

        ScrollRect scroll = host.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.elasticity = 0.1f;
        scroll.inertia = true;
        scroll.decelerationRate = 0.135f;
        scroll.scrollSensitivity = 40f;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        scroll.verticalScrollbarSpacing = -3f;

        RectTransform viewport = NewRect("Viewport", host);
        Stretch(viewport);
        viewport.offsetMax = new Vector2(-20f, 0f);
        Image viewportImg = viewport.gameObject.AddComponent<Image>();
        viewportImg.color = Color.white;
        viewportImg.raycastTarget = true;
        Mask mask = viewport.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        RectTransform content = NewRect(contentName, viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0f, 480f);
        GridLayoutGroup grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = SlotCell;
        grid.spacing = new Vector2(8f, 8f);
        grid.padding = new RectOffset(12, 12, 12, 12);
        grid.childAlignment = TextAnchor.UpperLeft;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 9;
        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Scrollbar bar = NewVerticalScrollbar(host);
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.verticalScrollbar = bar;

        for (int i = 0; i < tileIds.Length; i++)
        {
            AddPreviewSlot(content, tileIds[i], assignOfficialSprites);
        }
        return host.gameObject;
    }

    private static void BuildHelpScroll(RectTransform panelRt, out TMP_Text help)
    {
        RectTransform host = NewRect("HelpScroll", panelRt);
        host.anchorMin = new Vector2(0f, 0f);
        host.anchorMax = new Vector2(1f, 0f);
        host.pivot = new Vector2(0.5f, 0f);
        host.anchoredPosition = new Vector2(0f, 12f);
        host.sizeDelta = new Vector2(-40f, 140f);
        Image hostBg = host.gameObject.AddComponent<Image>();
        hostBg.color = new Color(0.10f, 0.12f, 0.16f, 1f);

        ScrollRect scroll = host.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.elasticity = 0.1f;
        scroll.inertia = true;
        scroll.decelerationRate = 0.135f;
        scroll.scrollSensitivity = 40f;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        scroll.verticalScrollbarSpacing = -3f;

        RectTransform viewport = NewRect("Viewport", host);
        Stretch(viewport);
        viewport.offsetMax = new Vector2(-20f, 0f);
        Image viewportImg = viewport.gameObject.AddComponent<Image>();
        viewportImg.color = Color.white;
        Mask mask = viewport.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        RectTransform helpContent = NewRect("HelpContent", viewport);
        helpContent.anchorMin = new Vector2(0f, 1f);
        helpContent.anchorMax = new Vector2(1f, 1f);
        helpContent.pivot = new Vector2(0.5f, 1f);
        helpContent.anchoredPosition = Vector2.zero;
        helpContent.sizeDelta = new Vector2(0f, 140f);
        ContentSizeFitter helpFit = helpContent.gameObject.AddComponent<ContentSizeFitter>();
        helpFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        help = NewText(helpContent, "HelpText", CardFaceConfigPanel.FormatHelp, 14, LabelColor, TextAnchor.UpperLeft);
        Stretch(help.rectTransform);
        help.enableWordWrapping = true;
        help.overflowMode = TextOverflowModes.Overflow;

        Scrollbar bar = NewVerticalScrollbar(host);
        scroll.content = helpContent;
        scroll.viewport = viewport;
        scroll.verticalScrollbar = bar;
    }

    private static Scrollbar NewVerticalScrollbar(RectTransform parent)
    {
        RectTransform barRt = NewRect("Scrollbar Vertical", parent);
        barRt.anchorMin = new Vector2(1f, 0f);
        barRt.anchorMax = new Vector2(1f, 1f);
        barRt.pivot = new Vector2(1f, 1f);
        barRt.anchoredPosition = Vector2.zero;
        barRt.sizeDelta = new Vector2(16f, 0f);
        Image barImg = barRt.gameObject.AddComponent<Image>();
        barImg.color = new Color(0.18f, 0.20f, 0.24f, 1f);
        barImg.sprite = BuiltInUiSprite("UI/Skin/Background.psd");
        barImg.type = Image.Type.Sliced;

        Scrollbar bar = barRt.gameObject.AddComponent<Scrollbar>();
        bar.direction = Scrollbar.Direction.BottomToTop;
        bar.transition = Selectable.Transition.ColorTint;

        RectTransform sliding = NewRect("Sliding Area", barRt);
        Stretch(sliding);
        sliding.offsetMin = new Vector2(2f, 2f);
        sliding.offsetMax = new Vector2(-2f, -2f);

        RectTransform handle = NewRect("Handle", sliding);
        Stretch(handle);
        Image handleImg = handle.gameObject.AddComponent<Image>();
        handleImg.color = new Color(0.62f, 0.66f, 0.74f, 1f);
        handleImg.sprite = BuiltInUiSprite("UI/Skin/UISprite.psd");
        handleImg.type = Image.Type.Sliced;

        bar.targetGraphic = handleImg;
        bar.handleRect = handle;
        return bar;
    }

    private static Sprite BuiltInUiSprite(string path)
    {
        Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>(path);
        return sprite != null ? sprite : WhiteSprite();
    }

    private static void AddPreviewSlot(RectTransform parent, int tileId, bool assignOfficialSprites)
    {
        RectTransform rt = NewRect("Slot_" + tileId, parent);
        Image img = rt.gameObject.AddComponent<Image>();
        img.preserveAspect = true;
        img.raycastTarget = false;
        img.color = Color.white;
        if (assignOfficialSprites)
        {
            img.sprite = Resources.Load<Sprite>("image/CardFaceImage_xuefun/" + tileId);
        }
        LayoutElement layout = rt.gameObject.AddComponent<LayoutElement>();
        layout.preferredWidth = SlotCell.x;
        layout.preferredHeight = SlotCell.y;
        CardFacePreviewSlot slot = rt.gameObject.AddComponent<CardFacePreviewSlot>();
        slot.tileId = tileId;
        slot.image = img;
    }

    private static void EnsureNavButton(SceneConfigPanel scp)
    {
        Button[] buttons = scp.GetComponentsInChildren<Button>(true);
        Transform facePanel = scp.transform.Find("CardFacePanel");
        Transform backPanel = scp.transform.Find("CardBackPanel");
        Button backButton = null;
        foreach (Button button in buttons)
        {
            if (button == null) continue;
            string name = button.gameObject.name;
            if (name.Contains("CardFace") || name == "牌面" || name.Contains("牌面"))
            {
                if (name.Contains("Bg") || name.Contains("背景")) continue;
                if (facePanel != null && button.transform.IsChildOf(facePanel)) continue;
                if (name == "CardFacePanel") continue;
                return;
            }
            if (backButton == null && (name.Contains("CardBack") || name.Contains("牌背")))
            {
                if (backPanel != null && button.transform.IsChildOf(backPanel)) continue;
                backButton = button;
            }
        }

        if (backButton == null)
        {
            return;
        }

        GameObject clone = Object.Instantiate(backButton.gameObject, backButton.transform.parent);
        clone.name = "CardFaceButton";
        clone.transform.SetSiblingIndex(backButton.transform.GetSiblingIndex() + 1);
        if (backButton.transform.parent == null || backButton.transform.parent.GetComponent<LayoutGroup>() == null)
        {
            RectTransform cloneRt = clone.GetComponent<RectTransform>();
            RectTransform srcRt = backButton.GetComponent<RectTransform>();
            if (cloneRt != null && srcRt != null)
            {
                cloneRt.anchoredPosition = srcRt.anchoredPosition + new Vector2(0f, -srcRt.sizeDelta.y - 8f);
            }
        }
        TMP_Text tmp = clone.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null) tmp.text = "牌面";
        clone.GetComponent<Button>().onClick = new Button.ButtonClickedEvent();
    }

    private static void EnsureFaceBgNavButton(SceneConfigPanel scp)
    {
        if (FindNavButton(scp, "CardFaceBg", "牌面背景", scp.transform.Find("CardFaceBackgroundPanel")) != null)
        {
            return;
        }

        Button faceButton = FindNavButton(scp, "CardFace", "牌面", scp.transform.Find("CardFacePanel"));
        if (faceButton == null)
        {
            return;
        }

        GameObject clone = Object.Instantiate(faceButton.gameObject, faceButton.transform.parent);
        clone.name = "CardFaceBgButton";
        clone.transform.SetSiblingIndex(faceButton.transform.GetSiblingIndex() + 1);
        TMP_Text tmp = clone.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null)
        {
            tmp.text = "牌面背景";
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 28f;
            tmp.fontSizeMax = 50f;
        }
        clone.GetComponent<Button>().onClick = new Button.ButtonClickedEvent();
    }

    private static Button FindNavButton(SceneConfigPanel scp, string namePart, string label, Transform excludePanel)
    {
        Button[] buttons = scp.GetComponentsInChildren<Button>(true);
        Button fallback = null;
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null) continue;
            string name = button.gameObject.name;
            if (excludePanel != null && button.transform.IsChildOf(excludePanel)) continue;
            if (name == namePart || name == namePart + "Button") return button;
            if (namePart == "CardFace" && (name.Contains("Bg") || name.Contains("背景"))) continue;
            if (label == "牌面" && (name.Contains("背景") || name.Contains("Bg"))) continue;
            TMP_Text tmp = button.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null && tmp.text == label) return button;
            if (fallback == null && (name.Contains(namePart) || name.Contains(label)))
            {
                fallback = button;
            }
        }
        return fallback;
    }

    private static Sprite WhiteSprite()
    {
        if (_whiteSprite == null)
        {
            _whiteSprite = Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f));
        }
        return _whiteSprite;
    }

    private static TMP_FontAsset TmpFont()
    {
        if (_tmpFont != null) return _tmpFont;
        _tmpFont = Resources.Load<TMP_FontAsset>("font/Chinese/AlibabaPuHuiTi/AlibabaPuHuiTi-3-55-Regular SDF");
        if (_tmpFont == null) _tmpFont = TMP_Settings.defaultFontAsset;
        return _tmpFont;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    private static TMP_Text NewText(RectTransform parent, string name, string content, int size, Color color, TextAnchor anchor)
    {
        RectTransform rt = NewRect(name, parent);
        TMP_Text text = rt.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = TmpFont();
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.alignment = TmpAlignment(anchor);
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static TextAlignmentOptions TmpAlignment(TextAnchor anchor)
    {
        switch (anchor)
        {
            case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
            case TextAnchor.MiddleLeft: return TextAlignmentOptions.MidlineLeft;
            case TextAnchor.MiddleCenter: return TextAlignmentOptions.Midline;
            default: return TextAlignmentOptions.Midline;
        }
    }

    private static Button NewButton(RectTransform parent, string name, string label, Color bg, Color textColor)
    {
        RectTransform rt = NewRect(name, parent);
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = bg;
        img.sprite = WhiteSprite();
        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = img;
        button.transition = Selectable.Transition.None;
        if (!string.IsNullOrEmpty(label))
        {
            TMP_Text text = NewText(rt, "Label", label, 15, textColor, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform);
        }
        return button;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void StretchTop(RectTransform rt, float left, float height, float y)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -y);
        rt.sizeDelta = new Vector2(-left * 2f, height);
    }

    private static void PlaceTop(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    private static void PlaceLeft(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }
}
#endif

