#if UNITY_EDITOR
using System;
using System.Linq;
using Salasasa.Home;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Restores the authored room column and matching navigation from the original main scene.</summary>
public static class RoomMatchPageLayoutBaker
{
    public const string LayoutCommit = "8c8c51df";
    const float RoomActionWidth = 256f;
    const float RoomActionGap = 12f;
    static readonly string[] LabelFields = { "usernameText", "titleText", "rankText", "rankScoreText", "eloScoreText" };

    public static void Bake(Scene scene)
    {
        BakeRooms(scene);
        foreach (var match in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MatchPanel>(true))) RestoreMatching(match);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    }

    public static void BakeRooms(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        var user = roots.SelectMany(r => r.GetComponentsInChildren<UserContainer>(true)).Single();
        var owner = roots.SelectMany(r => r.GetComponentsInChildren<CompleteLobbyController>(true)).Single();
        foreach (var room in roots.SelectMany(r => r.GetComponentsInChildren<RoomListPanel>(true))) RestoreRooms(room, user, owner);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    }

    static void RestoreRooms(RoomListPanel room, UserContainer source, CompleteLobbyController owner)
    {
        var root = (RectTransform)room.transform;
        // The old MeunPanel supplied this black backing; the new shared home root stays transparent.
        var backdrop = Need<UnityEngine.UI.Image>(root);
        backdrop.enabled = true; backdrop.color = Color.black; backdrop.sprite = null;
        backdrop.material = null; backdrop.type = UnityEngine.UI.Image.Type.Simple;
        backdrop.raycastTarget = true; EditorUtility.SetDirty(backdrop);
        var column = root.Find("RoomOverview") as RectTransform ?? root.Find("Overview") as RectTransform ?? Node(root, "RoomOverview");
        column.name = "RoomOverview";
        var title = column.Find("Title") ?? root.Find("Text (TMP)");
        if (title) { title.SetParent(column, false); title.name = "Text (TMP)"; }
        var profile = column.Find("UserContainer") as RectTransform ?? column.Find("UserSummary") as RectTransform ?? Node(column, "UserContainer");
        profile.name = "UserContainer";
        var stats = room.GetComponentsInChildren<NowPlayer>(true).Single();
        stats.transform.SetParent(column, false);
        var retiredStatsVisibility = stats.GetComponent<CanvasGroup>();
        if (retiredStatsVisibility) Object.DestroyImmediate(retiredStatsVisibility);
        var description = column.Find("Describe/Text"); if (description) description.name = "Link";
        var switchOverlay = profile.Find("SwitchButton/DesignedIcon_refresh");
        if (switchOverlay) Object.DestroyImmediate(switchOverlay.gameObject);
        foreach (var canvas in profile.GetComponentsInChildren<Canvas>(true)) Object.DestroyImmediate(canvas);
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/font/思源黑体/SourceHanSansSC-Regular SDF.asset");
        if (!font) throw new InvalidOperationException("The current Source Han body font is missing.");
        RestoreRoomDefaults(column, font);
        AlignStatistics(stats);

        var slider = Need<UnityEngine.UI.Slider>(profile.Find("Slider"));
        slider.fillRect = (RectTransform)profile.Find("Slider/Fill Area/Fill");
        slider.handleRect = (RectTransform)profile.Find("Slider/Handle Slide Area/Handle");
        slider.targetGraphic = slider.handleRect.GetComponent<UnityEngine.UI.Image>();
        slider.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Automatic };
        slider.transition = UnityEngine.UI.Selectable.Transition.None;
        slider.interactable = false; slider.minValue = 0; slider.maxValue = 1; slider.wholeNumbers = false;
        slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
        slider.onValueChanged = new UnityEngine.UI.Slider.SliderEvent();

        var summary = Need<RoomProfileSummary>(profile);
        summary.pageVisibility = room.GetComponent<CanvasGroup>();
        summary.sourceLabels = LabelFields.Select(field => Ref<TMP_Text>(source, field)).ToArray();
        summary.labels = new[] { "UserName", "Title", "RankText", "ScoreText", "EloScoreText" }.Select(name => profile.Find(name).GetComponent<TMP_Text>()).ToArray();
        foreach (var label in summary.labels) RemoveLocalization(label);
        summary.sourcePortrait = Ref<UnityEngine.UI.Image>(source, "profileImage");
        summary.portrait = profile.Find("SelfProfileImage").GetComponent<UnityEngine.UI.Image>();
        summary.sourceProgress = Ref<UnityEngine.UI.Slider>(source, "rankProgressBar"); summary.progress = slider;
        summary.sourceProfile = summary.sourcePortrait.GetComponent<ProfileOnClick>();
        summary.profile = Need<ProfileOnClick>(summary.portrait.transform);
        var hover = Need<HoverBrighten>(summary.portrait.transform);
        var hoverData = new SerializedObject(hover);
        hoverData.FindProperty("targetImage").objectReferenceValue = summary.portrait;
        hoverData.FindProperty("highlightAmount").floatValue = .2f;
        hoverData.FindProperty("transitionSpeed").floatValue = 30;
        hoverData.ApplyModifiedPropertiesWithoutUndo();
        var changeRule = profile.Find("SwitchButton").GetComponent<UnityEngine.UI.Button>();
        changeRule.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        UnityEventTools.AddPersistentListener(changeRule.onClick, source.CycleRule);
        var matchCount = column.Find("Panel/Text (TMP) (1)").GetComponent<TMP_Text>();
        owner.matchCounts = (owner.matchCounts ?? System.Array.Empty<TMP_Text>())
            .Where(t => t && !t.transform.IsChildOf(column)).Concat(new[] { matchCount }).ToArray();
        matchCount.text = PlatformText.Format("匹配人数({0})", owner.sourceMenu.GetComponent<MeunPanel>().MatchPlayerCount);
        RemoveLocalization(matchCount); EditorUtility.SetDirty(owner);
        var matchButton = column.Find("Panel").GetComponent<UnityEngine.UI.Button>();
        matchButton.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        UnityEventTools.AddStringPersistentListener(matchButton.onClick, owner.Open, "ranked");
        EditorUtility.SetDirty(matchButton);
        summary.Refresh(); EditorUtility.SetDirty(summary);
        foreach (string entry in new[] { "InventoryButton", "FanTableButton" })
        {
            var redundant = column.Find(entry);
            if (redundant) Object.DestroyImmediate(redundant.gameObject);
        }

        // Preserve the original presenters and network bindings; only their page composition moves.
        var content = Node(root, "RoomContent");
        Apply(content, "{\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":1,\"y\":1},\"m_AnchoredPosition\":{\"x\":214,\"y\":0},\"m_SizeDelta\":{\"x\":-468,\"y\":-28},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        var list = root.Find("RoomList") as RectTransform ?? content.Find("RoomList") as RectTransform;
        var actions = root.Find("GameObject") as RectTransform ?? content.Find("GameObject") as RectTransform;
        if (!list || !actions) throw new InvalidOperationException("Original room list/actions are missing.");
        list.SetParent(content, false); actions.SetParent(content, false);
        list.anchorMin = Vector2.zero; list.anchorMax = Vector2.one; list.pivot = new Vector2(.5f, .5f);
        float reservedWidth = RoomActionWidth + RoomActionGap;
        list.anchoredPosition = new Vector2(-reservedWidth * .5f, 7.6030273f);
        list.sizeDelta = new Vector2(-reservedWidth, -73.474f); list.localScale = Vector3.one;
        actions.anchorMin = actions.anchorMax = actions.pivot = new Vector2(1, 0);
        actions.anchoredPosition = new Vector2(0, list.offsetMin.y);
        actions.sizeDelta = new Vector2(RoomActionWidth, 384.92f); actions.localScale = Vector3.one;
        foreach (RectTransform action in actions) action.localScale = Vector3.one;
        var actionLayout = Need<UnityEngine.UI.VerticalLayoutGroup>(actions);
        actionLayout.padding = new RectOffset(); actionLayout.spacing = 0; actionLayout.childAlignment = TextAnchor.LowerRight;
        actionLayout.childControlWidth = true; actionLayout.childControlHeight = false;
        actionLayout.childForceExpandWidth = actionLayout.childForceExpandHeight = true;
        actionLayout.childScaleWidth = actionLayout.childScaleHeight = false;
        actionLayout.reverseArrangement = false;
        foreach (string name in new[] { "RoomFilters", "NavigateBarPanel" })
        { var unused = root.Find(name); if (unused) Object.DestroyImmediate(unused.gameObject); }
        column.SetSiblingIndex(0); content.SetSiblingIndex(1);
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(column);
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(actions);
    }

    public static void BakeStatistics(Scene scene)
    {
        foreach (var room in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RoomListPanel>(true)))
            AlignStatistics(room.GetComponentInChildren<NowPlayer>(true));
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    }

    static void AlignStatistics(NowPlayer stats)
    {
        string[] names = { "OnlineCount", "ActiveLobby", "RunningLobby", "MatchPlayingGames" };
        for (int i = 0; i < names.Length; i++)
        {
            var label = stats.transform.Find(names[i]).GetComponent<TMP_Text>();
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = Vector2.up;
            rect.pivot = Vector2.one * .5f;
            rect.anchoredPosition = new Vector2(i % 2 == 0 ? 109.14f : 287.94f, i < 2 ? -29f : -63.2f);
            rect.sizeDelta = new Vector2(178.8f, 27.893f);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.margin = Vector4.zero;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.enableAutoSizing = true;
            label.fontSize = label.fontSizeMax = 22;
            label.fontSizeMin = 14;
            var localized = label.GetComponent<LocalizedPlatformLabel>();
            if (localized)
            {
                localized.authoredSize = 22; localized.minimumSize = 14;
                localized.authoredAutoSize = true; localized.fitTranslation = true; localized.typographyBaked = true;
                EditorUtility.SetDirty(localized);
            }
            label.SetAllDirty(); EditorUtility.SetDirty(label); EditorUtility.SetDirty(rect);
        }
    }

    static void RestoreRoomDefaults(RectTransform column, TMP_FontAsset font)
    {
        RectTransform node;
        node = Node(column, "");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":1},\"m_AnchorMax\":{\"x\":0,\"y\":1},\"m_AnchoredPosition\":{\"x\":24.7875,\"y\":-22.5575},\"m_SizeDelta\":{\"x\":398.425,\"y\":882.885},\"m_Pivot\":{\"x\":0,\"y\":1}}");
        node.gameObject.SetActive(true);
        Apply(Need<UnityEngine.UI.VerticalLayoutGroup>(node), "{\"m_Enabled\":true,\"m_ChildAlignment\":0,\"m_Spacing\":20,\"m_ChildForceExpandWidth\":false,\"m_ChildForceExpandHeight\":false,\"m_ChildControlWidth\":false,\"m_ChildControlHeight\":false,\"m_ChildScaleWidth\":true,\"m_ChildScaleHeight\":false,\"m_ReverseArrangement\":false,\"m_Padding\":{\"m_Left\":0,\"m_Right\":0,\"m_Top\":0,\"m_Bottom\":0}}");
        node = Node(column, "Text (TMP)");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":0,\"y\":0},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":388.88,\"y\":81.299},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        Text(node, "{\"m_text\":\"房间列表\",\"m_fontColor\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1},\"m_enableVertexGradient\":false,\"m_fontSize\":58.05,\"m_fontSizeBase\":36,\"m_fontWeight\":400,\"m_enableAutoSizing\":true,\"m_fontSizeMin\":18,\"m_fontSizeMax\":72,\"m_fontStyle\":0,\"m_HorizontalAlignment\":2,\"m_VerticalAlignment\":256,\"m_textAlignment\":65535,\"m_characterSpacing\":0,\"m_wordSpacing\":0,\"m_lineSpacing\":0,\"m_paragraphSpacing\":0,\"m_charWidthMaxAdj\":0,\"m_TextWrappingMode\":1,\"m_wordWrappingRatios\":0.4,\"m_overflowMode\":0,\"m_enableKerning\":true,\"m_isRichText\":true,\"m_parseCtrlCharacters\":true,\"m_isOrthographic\":true,\"m_isCullingEnabled\":false,\"m_horizontalMapping\":0,\"m_verticalMapping\":0,\"m_geometrySortingOrder\":0,\"m_IsTextObjectScaleStatic\":false,\"m_VertexBufferAutoSizeReduction\":false,\"m_useMaxVisibleDescender\":true,\"m_pageToDisplay\":1,\"m_margin\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"m_isUsingLegacyAnimationComponent\":false,\"m_RaycastTarget\":true,\"m_Maskable\":true}", font, true);
        node.SetSiblingIndex(0);
        node = Node(column, "UserContainer");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":0,\"y\":0},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":388.8794,\"y\":235.1794},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        Image(node, "{\"m_Enabled\":true,\"m_Color\":{\"r\":0.690566,\"g\":0.690566,\"b\":0.690566,\"a\":0.09019608},\"m_RaycastTarget\":true,\"m_RaycastPadding\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"m_Maskable\":true,\"m_Type\":1,\"m_PreserveAspect\":false,\"m_FillCenter\":true,\"m_FillMethod\":4,\"m_FillAmount\":1,\"m_FillClockwise\":true,\"m_FillOrigin\":0,\"m_UseSpriteMesh\":false,\"m_PixelsPerUnitMultiplier\":1}", Builtin(10907));
        node.SetSiblingIndex(1);
        node = Node(column, "UserContainer/UserName");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0.38700002,\"y\":0.70736045},\"m_AnchorMax\":{\"x\":0.92530614,\"y\":0.9231625},\"m_AnchoredPosition\":{\"x\":-60.724,\"y\":10},\"m_SizeDelta\":{\"x\":179.54,\"y\":-2.603},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        Text(node, "{\"m_fontColor\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1},\"m_enableVertexGradient\":false,\"m_fontSize\":34.35,\"m_fontSizeBase\":50,\"m_fontWeight\":400,\"m_enableAutoSizing\":true,\"m_fontSizeMin\":18,\"m_fontSizeMax\":72,\"m_fontStyle\":0,\"m_HorizontalAlignment\":2,\"m_VerticalAlignment\":256,\"m_textAlignment\":65535,\"m_characterSpacing\":0,\"m_wordSpacing\":0,\"m_lineSpacing\":0,\"m_paragraphSpacing\":0,\"m_charWidthMaxAdj\":0,\"m_TextWrappingMode\":1,\"m_wordWrappingRatios\":0.4,\"m_overflowMode\":0,\"m_enableKerning\":true,\"m_isRichText\":true,\"m_parseCtrlCharacters\":true,\"m_isOrthographic\":true,\"m_isCullingEnabled\":false,\"m_horizontalMapping\":0,\"m_verticalMapping\":0,\"m_geometrySortingOrder\":0,\"m_IsTextObjectScaleStatic\":false,\"m_VertexBufferAutoSizeReduction\":false,\"m_useMaxVisibleDescender\":true,\"m_pageToDisplay\":1,\"m_margin\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"m_isUsingLegacyAnimationComponent\":false,\"m_RaycastTarget\":true,\"m_Maskable\":true}", font, false);
        node.SetSiblingIndex(0);
        node = Node(column, "UserContainer/SelfProfileImage");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0.057000004,\"y\":0.4285398},\"m_AnchorMax\":{\"x\":0.32900003,\"y\":0.90962267},\"m_AnchoredPosition\":{\"x\":5.956604,\"y\":-33},\"m_SizeDelta\":{\"x\":-11.913147,\"y\":-17.65033},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        Image(node, "{\"m_Enabled\":true,\"m_Color\":{\"r\":0.6528301,\"g\":0.6528301,\"b\":0.6528301,\"a\":1},\"m_RaycastTarget\":true,\"m_RaycastPadding\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"m_Maskable\":true,\"m_Type\":0,\"m_PreserveAspect\":false,\"m_FillCenter\":true,\"m_FillMethod\":4,\"m_FillAmount\":1,\"m_FillClockwise\":true,\"m_FillOrigin\":0,\"m_UseSpriteMesh\":false,\"m_PixelsPerUnitMultiplier\":1}", null);
        node.SetSiblingIndex(1);
        node = Node(column, "UserContainer/Title");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0.38700002,\"y\":0.70736045},\"m_AnchorMax\":{\"x\":0.92530614,\"y\":0.9231625},\"m_AnchoredPosition\":{\"x\":-168,\"y\":-139},\"m_SizeDelta\":{\"x\":-37.303223,\"y\":-4.762085},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        Text(node, "{\"m_fontColor\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1},\"m_enableVertexGradient\":false,\"m_fontSize\":32.85,\"m_fontSizeBase\":50,\"m_fontWeight\":400,\"m_enableAutoSizing\":true,\"m_fontSizeMin\":18,\"m_fontSizeMax\":72,\"m_fontStyle\":0,\"m_HorizontalAlignment\":2,\"m_VerticalAlignment\":256,\"m_textAlignment\":65535,\"m_characterSpacing\":0,\"m_wordSpacing\":0,\"m_lineSpacing\":0,\"m_paragraphSpacing\":0,\"m_charWidthMaxAdj\":0,\"m_TextWrappingMode\":1,\"m_wordWrappingRatios\":0.4,\"m_overflowMode\":0,\"m_enableKerning\":true,\"m_isRichText\":true,\"m_parseCtrlCharacters\":true,\"m_isOrthographic\":true,\"m_isCullingEnabled\":false,\"m_horizontalMapping\":0,\"m_verticalMapping\":0,\"m_geometrySortingOrder\":0,\"m_IsTextObjectScaleStatic\":false,\"m_VertexBufferAutoSizeReduction\":false,\"m_useMaxVisibleDescender\":true,\"m_pageToDisplay\":1,\"m_margin\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"m_isUsingLegacyAnimationComponent\":false,\"m_RaycastTarget\":true,\"m_Maskable\":true}", font, false);
        node.SetSiblingIndex(2);
        node = Node(column, "UserContainer/Text (TMP)");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0.5,\"y\":0.5},\"m_AnchorMax\":{\"x\":0.5,\"y\":0.5},\"m_AnchoredPosition\":{\"x\":89.4,\"y\":24.3},\"m_SizeDelta\":{\"x\":200,\"y\":50},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        Text(node, "{\"m_text\":\"玩家段位\",\"m_fontColor\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1},\"m_enableVertexGradient\":false,\"m_fontSize\":36,\"m_fontSizeBase\":36,\"m_fontWeight\":400,\"m_enableAutoSizing\":false,\"m_fontSizeMin\":18,\"m_fontSizeMax\":72,\"m_fontStyle\":0,\"m_HorizontalAlignment\":2,\"m_VerticalAlignment\":256,\"m_textAlignment\":65535,\"m_characterSpacing\":0,\"m_wordSpacing\":0,\"m_lineSpacing\":0,\"m_paragraphSpacing\":0,\"m_charWidthMaxAdj\":0,\"m_TextWrappingMode\":1,\"m_wordWrappingRatios\":0.4,\"m_overflowMode\":0,\"m_enableKerning\":true,\"m_isRichText\":true,\"m_parseCtrlCharacters\":true,\"m_isOrthographic\":true,\"m_isCullingEnabled\":false,\"m_horizontalMapping\":0,\"m_verticalMapping\":0,\"m_geometrySortingOrder\":0,\"m_IsTextObjectScaleStatic\":false,\"m_VertexBufferAutoSizeReduction\":false,\"m_useMaxVisibleDescender\":true,\"m_pageToDisplay\":1,\"m_margin\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"m_isUsingLegacyAnimationComponent\":false,\"m_RaycastTarget\":true,\"m_Maskable\":true}", font, true);
        node.SetSiblingIndex(3);
        node = Node(column, "UserContainer/RankText");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0.5,\"y\":0.5},\"m_AnchorMax\":{\"x\":0.5,\"y\":0.5},\"m_AnchoredPosition\":{\"x\":89.40005,\"y\":-17.561987},\"m_SizeDelta\":{\"x\":200,\"y\":50},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        Text(node, "{\"m_fontColor\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1},\"m_enableVertexGradient\":false,\"m_fontSize\":24,\"m_fontSizeBase\":24,\"m_fontWeight\":400,\"m_enableAutoSizing\":true,\"m_fontSizeMin\":20,\"m_fontSizeMax\":24,\"m_fontStyle\":0,\"m_HorizontalAlignment\":2,\"m_VerticalAlignment\":256,\"m_textAlignment\":65535,\"m_characterSpacing\":0,\"m_wordSpacing\":0,\"m_lineSpacing\":0,\"m_paragraphSpacing\":0,\"m_charWidthMaxAdj\":0,\"m_TextWrappingMode\":1,\"m_wordWrappingRatios\":0.4,\"m_overflowMode\":0,\"m_enableKerning\":true,\"m_isRichText\":true,\"m_parseCtrlCharacters\":true,\"m_isOrthographic\":true,\"m_isCullingEnabled\":false,\"m_horizontalMapping\":0,\"m_verticalMapping\":0,\"m_geometrySortingOrder\":0,\"m_IsTextObjectScaleStatic\":false,\"m_VertexBufferAutoSizeReduction\":false,\"m_useMaxVisibleDescender\":true,\"m_pageToDisplay\":1,\"m_margin\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"m_isUsingLegacyAnimationComponent\":false,\"m_RaycastTarget\":true,\"m_Maskable\":true}", font, false);
        node.SetSiblingIndex(4);
        node = Node(column, "UserContainer/Slider");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0.5,\"y\":0.5},\"m_AnchorMax\":{\"x\":0.5,\"y\":0.5},\"m_AnchoredPosition\":{\"x\":89.245026,\"y\":-48.782986},\"m_SizeDelta\":{\"x\":178.49,\"y\":27.29},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        node.SetSiblingIndex(5);
        node = Node(column, "UserContainer/Slider/Background");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0.25},\"m_AnchorMax\":{\"x\":1,\"y\":0.75},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":0,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        Image(node, "{\"m_Enabled\":true,\"m_Color\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1},\"m_RaycastTarget\":true,\"m_RaycastPadding\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"m_Maskable\":true,\"m_Type\":1,\"m_PreserveAspect\":false,\"m_FillCenter\":true,\"m_FillMethod\":4,\"m_FillAmount\":1,\"m_FillClockwise\":true,\"m_FillOrigin\":0,\"m_UseSpriteMesh\":false,\"m_PixelsPerUnitMultiplier\":1}", null);
        node.SetSiblingIndex(0);
        node = Node(column, "UserContainer/Slider/Fill Area");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0.25},\"m_AnchorMax\":{\"x\":1,\"y\":0.75},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":0,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        node.SetSiblingIndex(1);
        node = Node(column, "UserContainer/Slider/Fill Area/Fill");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":0,\"y\":0},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":0,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        Image(node, "{\"m_Enabled\":true,\"m_Color\":{\"r\":1,\"g\":0.4200189,\"b\":0,\"a\":1},\"m_RaycastTarget\":true,\"m_RaycastPadding\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"m_Maskable\":true,\"m_Type\":1,\"m_PreserveAspect\":false,\"m_FillCenter\":true,\"m_FillMethod\":4,\"m_FillAmount\":1,\"m_FillClockwise\":true,\"m_FillOrigin\":0,\"m_UseSpriteMesh\":false,\"m_PixelsPerUnitMultiplier\":1}", null);
        node.SetSiblingIndex(0);
        node = Node(column, "UserContainer/Slider/Handle Slide Area");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":1,\"y\":1},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":-20,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        node.SetSiblingIndex(2);
        node = Node(column, "UserContainer/Slider/Handle Slide Area/Handle");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":0,\"y\":0},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":20,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        Image(node, "{\"m_Enabled\":true,\"m_Color\":{\"r\":1,\"g\":1,\"b\":1,\"a\":0},\"m_RaycastTarget\":true,\"m_RaycastPadding\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"m_Maskable\":true,\"m_Type\":0,\"m_PreserveAspect\":false,\"m_FillCenter\":true,\"m_FillMethod\":4,\"m_FillAmount\":1,\"m_FillClockwise\":true,\"m_FillOrigin\":0,\"m_UseSpriteMesh\":false,\"m_PixelsPerUnitMultiplier\":1}", Builtin(10913));
        node.SetSiblingIndex(0);
        node = Node(column, "UserContainer/ScoreText");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0.5,\"y\":0.5},\"m_AnchorMax\":{\"x\":0.5,\"y\":0.5},\"m_AnchoredPosition\":{\"x\":89.4,\"y\":-75.7},\"m_SizeDelta\":{\"x\":200,\"y\":50},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        Text(node, "{\"m_fontColor\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1},\"m_enableVertexGradient\":false,\"m_fontSize\":20,\"m_fontSizeBase\":19,\"m_fontWeight\":400,\"m_enableAutoSizing\":false,\"m_fontSizeMin\":16,\"m_fontSizeMax\":19,\"m_fontStyle\":0,\"m_HorizontalAlignment\":2,\"m_VerticalAlignment\":256,\"m_textAlignment\":65535,\"m_characterSpacing\":0,\"m_wordSpacing\":0,\"m_lineSpacing\":0,\"m_paragraphSpacing\":0,\"m_charWidthMaxAdj\":0,\"m_TextWrappingMode\":1,\"m_wordWrappingRatios\":0.4,\"m_overflowMode\":0,\"m_enableKerning\":true,\"m_isRichText\":true,\"m_parseCtrlCharacters\":true,\"m_isOrthographic\":true,\"m_isCullingEnabled\":false,\"m_horizontalMapping\":0,\"m_verticalMapping\":0,\"m_geometrySortingOrder\":0,\"m_IsTextObjectScaleStatic\":false,\"m_VertexBufferAutoSizeReduction\":false,\"m_useMaxVisibleDescender\":true,\"m_pageToDisplay\":1,\"m_margin\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"m_isUsingLegacyAnimationComponent\":false,\"m_RaycastTarget\":true,\"m_Maskable\":true}", font, false);
        node.SetSiblingIndex(6);
        node = Node(column, "UserContainer/SwitchButton");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0.5,\"y\":0.5},\"m_AnchorMax\":{\"x\":0.5,\"y\":0.5},\"m_AnchoredPosition\":{\"x\":-31.8,\"y\":-3},\"m_SizeDelta\":{\"x\":40,\"y\":40},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        Image(node, "{\"m_Enabled\":true,\"m_Color\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1},\"m_RaycastTarget\":true,\"m_RaycastPadding\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"m_Maskable\":true,\"m_Type\":0,\"m_PreserveAspect\":false,\"m_FillCenter\":true,\"m_FillMethod\":4,\"m_FillAmount\":1,\"m_FillClockwise\":true,\"m_FillOrigin\":0,\"m_UseSpriteMesh\":false,\"m_PixelsPerUnitMultiplier\":1}", AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath("e5a8fb5800fe4a44aba31c8f8a044694")));
        Button(node, "{\"m_Enabled\":true,\"m_Transition\":1,\"m_Interactable\":true,\"m_Colors\":{\"m_NormalColor\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1},\"m_HighlightedColor\":{\"r\":0.9607843,\"g\":0.9607843,\"b\":0.9607843,\"a\":1},\"m_PressedColor\":{\"r\":0.78431374,\"g\":0.78431374,\"b\":0.78431374,\"a\":1},\"m_SelectedColor\":{\"r\":0.9607843,\"g\":0.9607843,\"b\":0.9607843,\"a\":1},\"m_DisabledColor\":{\"r\":0.78431374,\"g\":0.78431374,\"b\":0.78431374,\"a\":0.5019608},\"m_ColorMultiplier\":1,\"m_FadeDuration\":0.1},\"m_Navigation\":{\"m_Mode\":3,\"m_WrapAround\":false}}");
        node.SetSiblingIndex(7);
        node = Node(column, "UserContainer/EloScoreText");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0.5,\"y\":0.5},\"m_AnchorMax\":{\"x\":0.5,\"y\":0.5},\"m_AnchoredPosition\":{\"x\":89.4,\"y\":-63},\"m_SizeDelta\":{\"x\":200,\"y\":70},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(false);
        Text(node, "{\"m_fontColor\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1},\"m_enableVertexGradient\":false,\"m_fontSize\":32,\"m_fontSizeBase\":32,\"m_fontWeight\":400,\"m_enableAutoSizing\":true,\"m_fontSizeMin\":24,\"m_fontSizeMax\":32,\"m_fontStyle\":0,\"m_HorizontalAlignment\":2,\"m_VerticalAlignment\":512,\"m_textAlignment\":65535,\"m_characterSpacing\":0,\"m_wordSpacing\":0,\"m_lineSpacing\":0,\"m_paragraphSpacing\":0,\"m_charWidthMaxAdj\":0,\"m_TextWrappingMode\":0,\"m_wordWrappingRatios\":0.4,\"m_overflowMode\":1,\"m_enableKerning\":false,\"m_isRichText\":true,\"m_parseCtrlCharacters\":true,\"m_isOrthographic\":true,\"m_isCullingEnabled\":false,\"m_horizontalMapping\":0,\"m_verticalMapping\":0,\"m_geometrySortingOrder\":0,\"m_IsTextObjectScaleStatic\":false,\"m_VertexBufferAutoSizeReduction\":false,\"m_useMaxVisibleDescender\":true,\"m_pageToDisplay\":1,\"m_margin\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"m_isUsingLegacyAnimationComponent\":false,\"m_RaycastTarget\":false,\"m_Maskable\":true}", font, false);
        node.SetSiblingIndex(8);
        node = Node(column, "NowPlayer");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":0,\"y\":0},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":388.88,\"y\":85.0982},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        Image(node, "{\"m_Enabled\":true,\"m_Color\":{\"r\":0.690566,\"g\":0.690566,\"b\":0.690566,\"a\":0.09019608},\"m_RaycastTarget\":true,\"m_RaycastPadding\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"m_Maskable\":true,\"m_Type\":1,\"m_PreserveAspect\":false,\"m_FillCenter\":true,\"m_FillMethod\":4,\"m_FillAmount\":1,\"m_FillClockwise\":true,\"m_FillOrigin\":0,\"m_UseSpriteMesh\":false,\"m_PixelsPerUnitMultiplier\":1}", Builtin(10907));
        node.SetSiblingIndex(2);
        node = Node(column, "NowPlayer/OnlineCount");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":1},\"m_AnchorMax\":{\"x\":0,\"y\":1},\"m_AnchoredPosition\":{\"x\":109.14,\"y\":-29},\"m_SizeDelta\":{\"x\":178.8,\"y\":27.893},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        Text(node, "{\"m_fontColor\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1},\"m_enableVertexGradient\":false,\"m_fontSize\":22,\"m_fontSizeBase\":22,\"m_fontWeight\":400,\"m_enableAutoSizing\":false,\"m_fontSizeMin\":18,\"m_fontSizeMax\":72,\"m_fontStyle\":0,\"m_HorizontalAlignment\":1,\"m_VerticalAlignment\":512,\"m_textAlignment\":65535,\"m_characterSpacing\":0,\"m_wordSpacing\":0,\"m_lineSpacing\":0,\"m_paragraphSpacing\":0,\"m_charWidthMaxAdj\":0,\"m_TextWrappingMode\":1,\"m_wordWrappingRatios\":0.4,\"m_overflowMode\":0,\"m_enableKerning\":true,\"m_isRichText\":true,\"m_parseCtrlCharacters\":true,\"m_isOrthographic\":true,\"m_isCullingEnabled\":false,\"m_horizontalMapping\":0,\"m_verticalMapping\":0,\"m_geometrySortingOrder\":0,\"m_IsTextObjectScaleStatic\":false,\"m_VertexBufferAutoSizeReduction\":false,\"m_useMaxVisibleDescender\":true,\"m_pageToDisplay\":1,\"m_margin\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"m_isUsingLegacyAnimationComponent\":false,\"m_RaycastTarget\":true,\"m_Maskable\":true}", font, false);
        node.SetSiblingIndex(0);
        node = Node(column, "NowPlayer/ActiveLobby");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":1},\"m_AnchorMax\":{\"x\":0,\"y\":1},\"m_AnchoredPosition\":{\"x\":287.94,\"y\":-29},\"m_SizeDelta\":{\"x\":178.81,\"y\":27.893},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        Text(node, "{\"m_fontColor\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1},\"m_enableVertexGradient\":false,\"m_fontSize\":22,\"m_fontSizeBase\":22,\"m_fontWeight\":400,\"m_enableAutoSizing\":false,\"m_fontSizeMin\":18,\"m_fontSizeMax\":72,\"m_fontStyle\":0,\"m_HorizontalAlignment\":1,\"m_VerticalAlignment\":512,\"m_textAlignment\":65535,\"m_characterSpacing\":0,\"m_wordSpacing\":0,\"m_lineSpacing\":0,\"m_paragraphSpacing\":0,\"m_charWidthMaxAdj\":0,\"m_TextWrappingMode\":1,\"m_wordWrappingRatios\":0.4,\"m_overflowMode\":0,\"m_enableKerning\":true,\"m_isRichText\":true,\"m_parseCtrlCharacters\":true,\"m_isOrthographic\":true,\"m_isCullingEnabled\":false,\"m_horizontalMapping\":0,\"m_verticalMapping\":0,\"m_geometrySortingOrder\":0,\"m_IsTextObjectScaleStatic\":false,\"m_VertexBufferAutoSizeReduction\":false,\"m_useMaxVisibleDescender\":true,\"m_pageToDisplay\":1,\"m_margin\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"m_isUsingLegacyAnimationComponent\":false,\"m_RaycastTarget\":true,\"m_Maskable\":true}", font, false);
        node.SetSiblingIndex(1);
        node = Node(column, "NowPlayer/RunningLobby");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":1},\"m_AnchorMax\":{\"x\":0,\"y\":1},\"m_AnchoredPosition\":{\"x\":107.54,\"y\":-63.2},\"m_SizeDelta\":{\"x\":182,\"y\":27.893},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        Text(node, "{\"m_fontColor\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1},\"m_enableVertexGradient\":false,\"m_fontSize\":22,\"m_fontSizeBase\":22,\"m_fontWeight\":400,\"m_enableAutoSizing\":false,\"m_fontSizeMin\":18,\"m_fontSizeMax\":72,\"m_fontStyle\":0,\"m_HorizontalAlignment\":1,\"m_VerticalAlignment\":512,\"m_textAlignment\":65535,\"m_characterSpacing\":0,\"m_wordSpacing\":0,\"m_lineSpacing\":0,\"m_paragraphSpacing\":0,\"m_charWidthMaxAdj\":0,\"m_TextWrappingMode\":1,\"m_wordWrappingRatios\":0.4,\"m_overflowMode\":0,\"m_enableKerning\":true,\"m_isRichText\":true,\"m_parseCtrlCharacters\":true,\"m_isOrthographic\":true,\"m_isCullingEnabled\":false,\"m_horizontalMapping\":0,\"m_verticalMapping\":0,\"m_geometrySortingOrder\":0,\"m_IsTextObjectScaleStatic\":false,\"m_VertexBufferAutoSizeReduction\":false,\"m_useMaxVisibleDescender\":true,\"m_pageToDisplay\":1,\"m_margin\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"m_isUsingLegacyAnimationComponent\":false,\"m_RaycastTarget\":true,\"m_Maskable\":true}", font, false);
        node.SetSiblingIndex(2);
        node = Node(column, "NowPlayer/MatchPlayingGames");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":1},\"m_AnchorMax\":{\"x\":0,\"y\":1},\"m_AnchoredPosition\":{\"x\":287.94,\"y\":-63.2},\"m_SizeDelta\":{\"x\":178.8,\"y\":27.893},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        Text(node, "{\"m_fontColor\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1},\"m_enableVertexGradient\":false,\"m_fontSize\":22,\"m_fontSizeBase\":22,\"m_fontWeight\":400,\"m_enableAutoSizing\":false,\"m_fontSizeMin\":18,\"m_fontSizeMax\":72,\"m_fontStyle\":0,\"m_HorizontalAlignment\":1,\"m_VerticalAlignment\":512,\"m_textAlignment\":65535,\"m_characterSpacing\":0,\"m_wordSpacing\":0,\"m_lineSpacing\":0,\"m_paragraphSpacing\":0,\"m_charWidthMaxAdj\":0,\"m_TextWrappingMode\":1,\"m_wordWrappingRatios\":0.4,\"m_overflowMode\":0,\"m_enableKerning\":true,\"m_isRichText\":true,\"m_parseCtrlCharacters\":true,\"m_isOrthographic\":true,\"m_isCullingEnabled\":false,\"m_horizontalMapping\":0,\"m_verticalMapping\":0,\"m_geometrySortingOrder\":0,\"m_IsTextObjectScaleStatic\":false,\"m_VertexBufferAutoSizeReduction\":false,\"m_useMaxVisibleDescender\":true,\"m_pageToDisplay\":1,\"m_margin\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"m_isUsingLegacyAnimationComponent\":false,\"m_RaycastTarget\":true,\"m_Maskable\":true}", font, false);
        node.SetSiblingIndex(3);
        node = Node(column, "Describe");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":0,\"y\":0},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":384.9,\"y\":131.415},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        Image(node, "{\"m_Enabled\":true,\"m_Color\":{\"r\":0.6901961,\"g\":0.6901961,\"b\":0.6901961,\"a\":0.09019608},\"m_RaycastTarget\":true,\"m_RaycastPadding\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"m_Maskable\":true,\"m_Type\":1,\"m_PreserveAspect\":false,\"m_FillCenter\":true,\"m_FillMethod\":4,\"m_FillAmount\":1,\"m_FillClockwise\":true,\"m_FillOrigin\":0,\"m_UseSpriteMesh\":false,\"m_PixelsPerUnitMultiplier\":1}", Builtin(10907));
        node.SetSiblingIndex(3);
        node = Node(column, "Describe/Link");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1.0000306,\"y\":1.0000305,\"z\":1.0000305},\"m_AnchorMin\":{\"x\":0,\"y\":1},\"m_AnchorMax\":{\"x\":0,\"y\":1},\"m_AnchoredPosition\":{\"x\":202.20247,\"y\":-64.99582},\"m_SizeDelta\":{\"x\":365.5957,\"y\":118.458496},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        Text(node, "{\"m_text\":\"如Web端遇异常卡顿、GPU没有正常启用的情况，请检查浏览器图形加速是否开启或系统的电源设置是否为高性能。\",\"m_fontColor\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1},\"m_enableVertexGradient\":false,\"m_fontSize\":22,\"m_fontSizeBase\":22,\"m_fontWeight\":400,\"m_enableAutoSizing\":false,\"m_fontSizeMin\":18,\"m_fontSizeMax\":72,\"m_fontStyle\":0,\"m_HorizontalAlignment\":1,\"m_VerticalAlignment\":512,\"m_textAlignment\":65535,\"m_characterSpacing\":0,\"m_wordSpacing\":0,\"m_lineSpacing\":-14.9,\"m_paragraphSpacing\":0,\"m_charWidthMaxAdj\":0,\"m_TextWrappingMode\":1,\"m_wordWrappingRatios\":0.4,\"m_overflowMode\":0,\"m_enableKerning\":true,\"m_isRichText\":true,\"m_parseCtrlCharacters\":true,\"m_isOrthographic\":true,\"m_isCullingEnabled\":false,\"m_horizontalMapping\":0,\"m_verticalMapping\":0,\"m_geometrySortingOrder\":0,\"m_IsTextObjectScaleStatic\":false,\"m_VertexBufferAutoSizeReduction\":false,\"m_useMaxVisibleDescender\":true,\"m_pageToDisplay\":1,\"m_margin\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"m_isUsingLegacyAnimationComponent\":false,\"m_RaycastTarget\":true,\"m_Maskable\":true}", font, true);
        node.SetSiblingIndex(0);
        node = Node(column, "Links");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":4},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":1},\"m_AnchorMax\":{\"x\":0,\"y\":1},\"m_AnchoredPosition\":{\"x\":192.45001,\"y\":-650.4916},\"m_SizeDelta\":{\"x\":384.9,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        Image(node, "{\"m_Enabled\":true,\"m_Color\":{\"r\":0.690566,\"g\":0.690566,\"b\":0.690566,\"a\":0.09019608},\"m_RaycastTarget\":true,\"m_RaycastPadding\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"m_Maskable\":true,\"m_Type\":1,\"m_PreserveAspect\":false,\"m_FillCenter\":true,\"m_FillMethod\":4,\"m_FillAmount\":1,\"m_FillClockwise\":true,\"m_FillOrigin\":0,\"m_UseSpriteMesh\":false,\"m_PixelsPerUnitMultiplier\":1}", Builtin(10907));
        Apply(Need<UnityEngine.UI.VerticalLayoutGroup>(node), "{\"m_Enabled\":true,\"m_ChildAlignment\":0,\"m_Spacing\":5,\"m_ChildForceExpandWidth\":true,\"m_ChildForceExpandHeight\":true,\"m_ChildControlWidth\":false,\"m_ChildControlHeight\":false,\"m_ChildScaleWidth\":false,\"m_ChildScaleHeight\":false,\"m_ReverseArrangement\":false,\"m_Padding\":{\"m_Left\":20,\"m_Right\":0,\"m_Top\":10,\"m_Bottom\":10}}");
        Apply(Need<UnityEngine.UI.ContentSizeFitter>(node), "{\"m_Enabled\":true,\"m_HorizontalFit\":0,\"m_VerticalFit\":1}");
        node.SetSiblingIndex(4);
        node = Node(column, "Links/OnlineCount");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":0,\"y\":0},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":313.01,\"y\":25},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        Text(node, "{\"m_text\":\"交流测试群:906497522\",\"m_fontColor\":{\"r\":1,\"g\":0.72638464,\"b\":0,\"a\":1},\"m_enableVertexGradient\":false,\"m_fontSize\":22,\"m_fontSizeBase\":22,\"m_fontWeight\":400,\"m_enableAutoSizing\":false,\"m_fontSizeMin\":18,\"m_fontSizeMax\":72,\"m_fontStyle\":0,\"m_HorizontalAlignment\":1,\"m_VerticalAlignment\":512,\"m_textAlignment\":65535,\"m_characterSpacing\":0,\"m_wordSpacing\":0,\"m_lineSpacing\":0,\"m_paragraphSpacing\":0,\"m_charWidthMaxAdj\":0,\"m_TextWrappingMode\":1,\"m_wordWrappingRatios\":0.4,\"m_overflowMode\":0,\"m_enableKerning\":true,\"m_isRichText\":true,\"m_parseCtrlCharacters\":true,\"m_isOrthographic\":true,\"m_isCullingEnabled\":false,\"m_horizontalMapping\":0,\"m_verticalMapping\":0,\"m_geometrySortingOrder\":0,\"m_IsTextObjectScaleStatic\":false,\"m_VertexBufferAutoSizeReduction\":false,\"m_useMaxVisibleDescender\":true,\"m_pageToDisplay\":1,\"m_margin\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"m_isUsingLegacyAnimationComponent\":false,\"m_RaycastTarget\":true,\"m_Maskable\":true}", font, true);
        node.SetSiblingIndex(0);
        node = Node(column, "Links/ActiveLobby");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":0,\"y\":0},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":313.014,\"y\":25},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        Text(node, "{\"m_text\":\"平台网址:salasasa.cn\",\"m_fontColor\":{\"r\":0.19339621,\"g\":0.6744863,\"b\":1,\"a\":1},\"m_enableVertexGradient\":false,\"m_fontSize\":22,\"m_fontSizeBase\":22,\"m_fontWeight\":400,\"m_enableAutoSizing\":false,\"m_fontSizeMin\":18,\"m_fontSizeMax\":72,\"m_fontStyle\":0,\"m_HorizontalAlignment\":1,\"m_VerticalAlignment\":512,\"m_textAlignment\":65535,\"m_characterSpacing\":0,\"m_wordSpacing\":0,\"m_lineSpacing\":0,\"m_paragraphSpacing\":0,\"m_charWidthMaxAdj\":0,\"m_TextWrappingMode\":1,\"m_wordWrappingRatios\":0.4,\"m_overflowMode\":0,\"m_enableKerning\":true,\"m_isRichText\":true,\"m_parseCtrlCharacters\":true,\"m_isOrthographic\":true,\"m_isCullingEnabled\":false,\"m_horizontalMapping\":0,\"m_verticalMapping\":0,\"m_geometrySortingOrder\":0,\"m_IsTextObjectScaleStatic\":false,\"m_VertexBufferAutoSizeReduction\":false,\"m_useMaxVisibleDescender\":true,\"m_pageToDisplay\":1,\"m_margin\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"m_isUsingLegacyAnimationComponent\":false,\"m_RaycastTarget\":true,\"m_Maskable\":true}", font, true);
        node.SetSiblingIndex(1);
        node = Node(column, "Panel");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":0,\"y\":0},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":384.9,\"y\":77.155},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        Image(node, "{\"m_Enabled\":true,\"m_Color\":{\"r\":0.34509802,\"g\":0.8103814,\"b\":1,\"a\":1},\"m_RaycastTarget\":true,\"m_RaycastPadding\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"m_Maskable\":true,\"m_Type\":1,\"m_PreserveAspect\":false,\"m_FillCenter\":true,\"m_FillMethod\":4,\"m_FillAmount\":1,\"m_FillClockwise\":true,\"m_FillOrigin\":0,\"m_UseSpriteMesh\":false,\"m_PixelsPerUnitMultiplier\":1}", Builtin(10907));
        Button(node, "{\"m_Enabled\":true,\"m_Transition\":1,\"m_Interactable\":true,\"m_Colors\":{\"m_NormalColor\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1},\"m_HighlightedColor\":{\"r\":0.9607843,\"g\":0.9607843,\"b\":0.9607843,\"a\":1},\"m_PressedColor\":{\"r\":0.78431374,\"g\":0.78431374,\"b\":0.78431374,\"a\":1},\"m_SelectedColor\":{\"r\":0.9607843,\"g\":0.9607843,\"b\":0.9607843,\"a\":1},\"m_DisabledColor\":{\"r\":0.78431374,\"g\":0.78431374,\"b\":0.78431374,\"a\":0.5019608},\"m_ColorMultiplier\":1,\"m_FadeDuration\":0.1},\"m_Navigation\":{\"m_Mode\":3,\"m_WrapAround\":false}}");
        node.SetSiblingIndex(5);
        node = Node(column, "Panel/Text (TMP)");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":1,\"y\":1},\"m_AnchoredPosition\":{\"x\":-104.132,\"y\":0},\"m_SizeDelta\":{\"x\":-208.263,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        Text(node, "{\"m_text\":\"进入匹配\",\"m_fontColor\":{\"r\":0,\"g\":0,\"b\":0,\"a\":1},\"m_enableVertexGradient\":false,\"m_fontSize\":36,\"m_fontSizeBase\":36,\"m_fontWeight\":400,\"m_enableAutoSizing\":false,\"m_fontSizeMin\":18,\"m_fontSizeMax\":72,\"m_fontStyle\":0,\"m_HorizontalAlignment\":2,\"m_VerticalAlignment\":512,\"m_textAlignment\":65535,\"m_characterSpacing\":0,\"m_wordSpacing\":0,\"m_lineSpacing\":0,\"m_paragraphSpacing\":0,\"m_charWidthMaxAdj\":0,\"m_TextWrappingMode\":1,\"m_wordWrappingRatios\":0.4,\"m_overflowMode\":0,\"m_enableKerning\":false,\"m_isRichText\":true,\"m_parseCtrlCharacters\":true,\"m_isOrthographic\":true,\"m_isCullingEnabled\":false,\"m_horizontalMapping\":0,\"m_verticalMapping\":0,\"m_geometrySortingOrder\":0,\"m_IsTextObjectScaleStatic\":false,\"m_VertexBufferAutoSizeReduction\":false,\"m_useMaxVisibleDescender\":true,\"m_pageToDisplay\":1,\"m_margin\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"m_isUsingLegacyAnimationComponent\":false,\"m_RaycastTarget\":true,\"m_Maskable\":true}", font, true);
        node.SetSiblingIndex(0);
        node = Node(column, "Panel/Text (TMP) (1)");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":1,\"y\":1},\"m_AnchoredPosition\":{\"x\":90.308,\"y\":0},\"m_SizeDelta\":{\"x\":-180.62,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node.gameObject.SetActive(true);
        Text(node, "{\"m_fontColor\":{\"r\":0,\"g\":0,\"b\":0,\"a\":1},\"m_enableVertexGradient\":false,\"m_fontSize\":30,\"m_fontSizeBase\":30,\"m_fontWeight\":400,\"m_enableAutoSizing\":false,\"m_fontSizeMin\":18,\"m_fontSizeMax\":72,\"m_fontStyle\":0,\"m_HorizontalAlignment\":2,\"m_VerticalAlignment\":512,\"m_textAlignment\":65535,\"m_characterSpacing\":0,\"m_wordSpacing\":0,\"m_lineSpacing\":0,\"m_paragraphSpacing\":0,\"m_charWidthMaxAdj\":0,\"m_TextWrappingMode\":1,\"m_wordWrappingRatios\":0.4,\"m_overflowMode\":0,\"m_enableKerning\":false,\"m_isRichText\":true,\"m_parseCtrlCharacters\":true,\"m_isOrthographic\":true,\"m_isCullingEnabled\":false,\"m_horizontalMapping\":0,\"m_verticalMapping\":0,\"m_geometrySortingOrder\":0,\"m_IsTextObjectScaleStatic\":false,\"m_VertexBufferAutoSizeReduction\":false,\"m_useMaxVisibleDescender\":true,\"m_pageToDisplay\":1,\"m_margin\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"m_isUsingLegacyAnimationComponent\":false,\"m_RaycastTarget\":true,\"m_Maskable\":true}", font, false);
        node.SetSiblingIndex(1);
    }

    static void RestoreMatching(MatchPanel panel)
    {
        var lobby = Ref<MatchLobbyView>(panel, "lobbyView");
        var layout = lobby.transform.Find("MatchLayout") as RectTransform ?? lobby.transform.Find("OriginalMatchPanel/MatchLayout") as RectTransform;
        if (!layout) throw new InvalidOperationException("Matching layout is missing.");
        // Historic TMP-generated children and the replaced Elo tooltip are not authored navigation.
        foreach (var stale in layout.GetComponentsInChildren<Transform>(true).Where(t => t.name.Contains("TMP SubMeshUI") && t.name.Contains("AlibabaPuHuiTi")).ToArray())
            Object.DestroyImmediate(stale.gameObject);
        var staleTooltip = layout.Find("HeaderBar/RankSummary/EloHelp/Tooltip");
        if (staleTooltip) Object.DestroyImmediate(staleTooltip.gameObject);
        RestoreMatchingGeometry(layout);
        var tabs = layout.Find("HeaderBar/RuleTabs"); tabs.gameObject.SetActive(true);
        var buttons = new UnityEngine.UI.Button[RankedRules.FamilyNames.Length];
        var selected = new GameObject[buttons.Length]; var counts = new TMP_Text[buttons.Length];
        for (int i = 0; i < buttons.Length; i++)
        {
            var tab = tabs.Find("Rule_" + i); tab.gameObject.SetActive(true);
            buttons[i] = tab.GetComponent<UnityEngine.UI.Button>(); buttons[i].onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            selected[i] = tab.Find("SelectedBackground").gameObject; selected[i].SetActive(i == 0);
            counts[i] = tab.Find("PlayerCount/Count").GetComponent<TMP_Text>();
            var label = tab.Find("Label").GetComponent<TMP_Text>();
            label.text = RankedRules.FamilyNames[i]; var localization = label.GetComponent<LocalizedPlatformLabel>();
            if (localization) localization.SetSource(RankedRules.FamilyNames[i]);
        }
        foreach (Transform tab in tabs) if (!buttons.Any(b => b.transform == tab)) tab.gameObject.SetActive(false);
        int[] displayOrder = { 0, 1, 3, 2, 4 };
        for (int i = 0; i < displayOrder.Length; i++) buttons[displayOrder[i]].transform.SetSiblingIndex(i);
        Array(lobby, "ruleButtons", buttons); Array(lobby, "selectedTabs", selected); Array(lobby, "rulePlayerCounts", counts);
        var serialized = new SerializedObject(lobby); serialized.FindProperty("showStandardVariant").boolValue = false; serialized.ApplyModifiedPropertiesWithoutUndo();
        Ref<GameObject>(lobby, "variantBar").SetActive(RankedRules.FamilyRuleIndices[lobby.ActiveFamily].Length > 1);
        var sidebar = layout.parent.Find("NavigateBarPanel"); if (sidebar) Object.DestroyImmediate(sidebar.gameObject);
        EditorUtility.SetDirty(lobby);
    }

    static void RestoreMatchingGeometry(RectTransform layout)
    {
        RectTransform node;
        node = Node(layout, "");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1.12,\"y\":1.12,\"z\":1},\"m_AnchorMin\":{\"x\":0.5,\"y\":0},\"m_AnchorMax\":{\"x\":0.5,\"y\":1},\"m_AnchoredPosition\":{\"x\":0,\"y\":12},\"m_SizeDelta\":{\"x\":1524,\"y\":-60},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        Apply(Need<UnityEngine.UI.VerticalLayoutGroup>(node), "{\"m_Enabled\":true,\"m_ChildAlignment\":1,\"m_Spacing\":12,\"m_ChildForceExpandWidth\":true,\"m_ChildForceExpandHeight\":false,\"m_ChildControlWidth\":true,\"m_ChildControlHeight\":true,\"m_ChildScaleWidth\":false,\"m_ChildScaleHeight\":false,\"m_ReverseArrangement\":false,\"m_Padding\":{\"m_Left\":0,\"m_Right\":0,\"m_Top\":52,\"m_Bottom\":0}}");
        node = Node(layout, "MatchTools");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":0,\"y\":0},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":0,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        Apply(Need<UnityEngine.UI.LayoutElement>(node), "{\"m_Enabled\":true,\"m_IgnoreLayout\":false,\"m_MinWidth\":-1,\"m_MinHeight\":36,\"m_PreferredWidth\":-1,\"m_PreferredHeight\":36,\"m_FlexibleWidth\":-1,\"m_FlexibleHeight\":0,\"m_LayoutPriority\":1}");
        Apply(Need<UnityEngine.UI.HorizontalLayoutGroup>(node), "{\"m_Enabled\":true,\"m_ChildAlignment\":0,\"m_Spacing\":0,\"m_ChildForceExpandWidth\":false,\"m_ChildForceExpandHeight\":true,\"m_ChildControlWidth\":true,\"m_ChildControlHeight\":true,\"m_ChildScaleWidth\":false,\"m_ChildScaleHeight\":false,\"m_ReverseArrangement\":false,\"m_Padding\":{\"m_Left\":0,\"m_Right\":0,\"m_Top\":0,\"m_Bottom\":0}}");
        node = Node(layout, "HeaderBar");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":0,\"y\":0},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":0,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        Apply(Need<UnityEngine.UI.LayoutElement>(node), "{\"m_Enabled\":true,\"m_IgnoreLayout\":false,\"m_MinWidth\":-1,\"m_MinHeight\":70,\"m_PreferredWidth\":-1,\"m_PreferredHeight\":70,\"m_FlexibleWidth\":-1,\"m_FlexibleHeight\":0,\"m_LayoutPriority\":1}");
        Apply(Need<UnityEngine.UI.HorizontalLayoutGroup>(node), "{\"m_Enabled\":true,\"m_ChildAlignment\":0,\"m_Spacing\":12,\"m_ChildForceExpandWidth\":false,\"m_ChildForceExpandHeight\":true,\"m_ChildControlWidth\":true,\"m_ChildControlHeight\":true,\"m_ChildScaleWidth\":false,\"m_ChildScaleHeight\":false,\"m_ReverseArrangement\":false,\"m_Padding\":{\"m_Left\":0,\"m_Right\":0,\"m_Top\":0,\"m_Bottom\":0}}");
        node = Node(layout, "HeaderBar/RuleTabs");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":0,\"y\":0},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":0,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        Apply(Need<UnityEngine.UI.LayoutElement>(node), "{\"m_Enabled\":true,\"m_IgnoreLayout\":false,\"m_MinWidth\":954,\"m_MinHeight\":70,\"m_PreferredWidth\":954,\"m_PreferredHeight\":70,\"m_FlexibleWidth\":0,\"m_FlexibleHeight\":0,\"m_LayoutPriority\":1}");
        Apply(Need<UnityEngine.UI.HorizontalLayoutGroup>(node), "{\"m_Enabled\":true,\"m_ChildAlignment\":0,\"m_Spacing\":6,\"m_ChildForceExpandWidth\":true,\"m_ChildForceExpandHeight\":true,\"m_ChildControlWidth\":true,\"m_ChildControlHeight\":true,\"m_ChildScaleWidth\":false,\"m_ChildScaleHeight\":false,\"m_ReverseArrangement\":false,\"m_Padding\":{\"m_Left\":0,\"m_Right\":0,\"m_Top\":0,\"m_Bottom\":0}}");
        node = Node(layout, "HeaderBar/RankSummary");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":0,\"y\":0},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":0,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        Apply(Need<UnityEngine.UI.LayoutElement>(node), "{\"m_Enabled\":true,\"m_IgnoreLayout\":false,\"m_MinWidth\":558,\"m_MinHeight\":70,\"m_PreferredWidth\":558,\"m_PreferredHeight\":70,\"m_FlexibleWidth\":0,\"m_FlexibleHeight\":0,\"m_LayoutPriority\":1}");
        node = Node(layout, "VariantBar");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0.5,\"y\":0.5},\"m_AnchorMax\":{\"x\":0.5,\"y\":0.5},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":100,\"y\":100},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        Apply(Need<UnityEngine.UI.LayoutElement>(node), "{\"m_Enabled\":true,\"m_IgnoreLayout\":false,\"m_MinWidth\":-1,\"m_MinHeight\":52,\"m_PreferredWidth\":-1,\"m_PreferredHeight\":52,\"m_FlexibleWidth\":-1,\"m_FlexibleHeight\":0,\"m_LayoutPriority\":1}");
        Apply(Need<UnityEngine.UI.HorizontalLayoutGroup>(node), "{\"m_Enabled\":true,\"m_ChildAlignment\":3,\"m_Spacing\":8,\"m_ChildForceExpandWidth\":false,\"m_ChildForceExpandHeight\":true,\"m_ChildControlWidth\":true,\"m_ChildControlHeight\":true,\"m_ChildScaleWidth\":false,\"m_ChildScaleHeight\":false,\"m_ReverseArrangement\":false,\"m_Padding\":{\"m_Left\":0,\"m_Right\":0,\"m_Top\":2,\"m_Bottom\":2}}");
        node = Node(layout, "RulePages");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":0,\"y\":0},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":0,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        Apply(Need<UnityEngine.UI.LayoutElement>(node), "{\"m_Enabled\":true,\"m_IgnoreLayout\":false,\"m_MinWidth\":-1,\"m_MinHeight\":520,\"m_PreferredWidth\":-1,\"m_PreferredHeight\":520,\"m_FlexibleWidth\":-1,\"m_FlexibleHeight\":0,\"m_LayoutPriority\":1}");
        node = Node(layout, "HeaderBar/RuleTabs/Rule_0");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":0,\"y\":0},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":0,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        Apply(Need<UnityEngine.UI.LayoutElement>(node), "{\"m_Enabled\":true,\"m_IgnoreLayout\":false,\"m_MinWidth\":186,\"m_MinHeight\":-1,\"m_PreferredWidth\":186,\"m_PreferredHeight\":-1,\"m_FlexibleWidth\":0,\"m_FlexibleHeight\":-1,\"m_LayoutPriority\":1}");
        node = Node(layout, "HeaderBar/RuleTabs/Rule_0/SelectedBackground");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":1,\"y\":1},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":0,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node = Node(layout, "HeaderBar/RuleTabs/Rule_0/Label");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":1,\"y\":1},\"m_AnchoredPosition\":{\"x\":-24,\"y\":0},\"m_SizeDelta\":{\"x\":-60,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node = Node(layout, "HeaderBar/RuleTabs/Rule_0/PlayerCount");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":1,\"y\":0.5},\"m_AnchorMax\":{\"x\":1,\"y\":0.5},\"m_AnchoredPosition\":{\"x\":-10,\"y\":0},\"m_SizeDelta\":{\"x\":36,\"y\":32},\"m_Pivot\":{\"x\":1,\"y\":0.5}}");
        node = Node(layout, "HeaderBar/RuleTabs/Rule_0/PlayerCount/Count");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":1,\"y\":1},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":-4,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node = Node(layout, "HeaderBar/RuleTabs/Rule_1");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":0,\"y\":0},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":0,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        Apply(Need<UnityEngine.UI.LayoutElement>(node), "{\"m_Enabled\":true,\"m_IgnoreLayout\":false,\"m_MinWidth\":186,\"m_MinHeight\":-1,\"m_PreferredWidth\":186,\"m_PreferredHeight\":-1,\"m_FlexibleWidth\":0,\"m_FlexibleHeight\":-1,\"m_LayoutPriority\":1}");
        node = Node(layout, "HeaderBar/RuleTabs/Rule_1/SelectedBackground");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":1,\"y\":1},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":0,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node = Node(layout, "HeaderBar/RuleTabs/Rule_1/Label");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":1,\"y\":1},\"m_AnchoredPosition\":{\"x\":-24,\"y\":0},\"m_SizeDelta\":{\"x\":-60,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node = Node(layout, "HeaderBar/RuleTabs/Rule_1/PlayerCount");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":1,\"y\":0.5},\"m_AnchorMax\":{\"x\":1,\"y\":0.5},\"m_AnchoredPosition\":{\"x\":-10,\"y\":0},\"m_SizeDelta\":{\"x\":36,\"y\":32},\"m_Pivot\":{\"x\":1,\"y\":0.5}}");
        node = Node(layout, "HeaderBar/RuleTabs/Rule_1/PlayerCount/Count");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":1,\"y\":1},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":-4,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node = Node(layout, "HeaderBar/RuleTabs/Rule_2");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":0,\"y\":0},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":0,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        Apply(Need<UnityEngine.UI.LayoutElement>(node), "{\"m_Enabled\":true,\"m_IgnoreLayout\":false,\"m_MinWidth\":186,\"m_MinHeight\":-1,\"m_PreferredWidth\":186,\"m_PreferredHeight\":-1,\"m_FlexibleWidth\":0,\"m_FlexibleHeight\":-1,\"m_LayoutPriority\":1}");
        node = Node(layout, "HeaderBar/RuleTabs/Rule_2/SelectedBackground");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":1,\"y\":1},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":0,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node = Node(layout, "HeaderBar/RuleTabs/Rule_2/Label");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":1,\"y\":1},\"m_AnchoredPosition\":{\"x\":-24,\"y\":0},\"m_SizeDelta\":{\"x\":-60,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node = Node(layout, "HeaderBar/RuleTabs/Rule_2/PlayerCount");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":1,\"y\":0.5},\"m_AnchorMax\":{\"x\":1,\"y\":0.5},\"m_AnchoredPosition\":{\"x\":-10,\"y\":0},\"m_SizeDelta\":{\"x\":36,\"y\":32},\"m_Pivot\":{\"x\":1,\"y\":0.5}}");
        node = Node(layout, "HeaderBar/RuleTabs/Rule_2/PlayerCount/Count");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":1,\"y\":1},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":-4,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node = Node(layout, "HeaderBar/RuleTabs/Rule_3");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":0,\"y\":0},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":0,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        Apply(Need<UnityEngine.UI.LayoutElement>(node), "{\"m_Enabled\":true,\"m_IgnoreLayout\":false,\"m_MinWidth\":186,\"m_MinHeight\":-1,\"m_PreferredWidth\":186,\"m_PreferredHeight\":-1,\"m_FlexibleWidth\":0,\"m_FlexibleHeight\":-1,\"m_LayoutPriority\":1}");
        node = Node(layout, "HeaderBar/RuleTabs/Rule_3/SelectedBackground");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":1,\"y\":1},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":0,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node = Node(layout, "HeaderBar/RuleTabs/Rule_3/Label");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":1,\"y\":1},\"m_AnchoredPosition\":{\"x\":-24,\"y\":0},\"m_SizeDelta\":{\"x\":-60,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node = Node(layout, "HeaderBar/RuleTabs/Rule_3/PlayerCount");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":1,\"y\":0.5},\"m_AnchorMax\":{\"x\":1,\"y\":0.5},\"m_AnchoredPosition\":{\"x\":-10,\"y\":0},\"m_SizeDelta\":{\"x\":36,\"y\":32},\"m_Pivot\":{\"x\":1,\"y\":0.5}}");
        node = Node(layout, "HeaderBar/RuleTabs/Rule_3/PlayerCount/Count");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":1,\"y\":1},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":-4,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node = Node(layout, "HeaderBar/RuleTabs/Rule_4");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":0,\"y\":0},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":0,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        Apply(Need<UnityEngine.UI.LayoutElement>(node), "{\"m_Enabled\":true,\"m_IgnoreLayout\":false,\"m_MinWidth\":186,\"m_MinHeight\":-1,\"m_PreferredWidth\":186,\"m_PreferredHeight\":-1,\"m_FlexibleWidth\":0,\"m_FlexibleHeight\":-1,\"m_LayoutPriority\":1}");
        node = Node(layout, "HeaderBar/RuleTabs/Rule_4/SelectedBackground");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":1,\"y\":1},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":0,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node = Node(layout, "HeaderBar/RuleTabs/Rule_4/Label");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":1,\"y\":1},\"m_AnchoredPosition\":{\"x\":-24,\"y\":0},\"m_SizeDelta\":{\"x\":-60,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node = Node(layout, "HeaderBar/RuleTabs/Rule_4/PlayerCount");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":1,\"y\":0.5},\"m_AnchorMax\":{\"x\":1,\"y\":0.5},\"m_AnchoredPosition\":{\"x\":-10,\"y\":0},\"m_SizeDelta\":{\"x\":36,\"y\":32},\"m_Pivot\":{\"x\":1,\"y\":0.5}}");
        node = Node(layout, "HeaderBar/RuleTabs/Rule_4/PlayerCount/Count");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":1,\"y\":1},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":-4,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node = Node(layout, "HeaderBar/RankSummary/Divider");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0.95},\"m_AnchorMax\":{\"x\":1,\"y\":1},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":0,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node = Node(layout, "HeaderBar/RankSummary/Rank");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0.015,\"y\":0},\"m_AnchorMax\":{\"x\":0.5,\"y\":0.94},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":0,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node = Node(layout, "HeaderBar/RankSummary/Points");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0.52,\"y\":0.34},\"m_AnchorMax\":{\"x\":1,\"y\":0.95},\"m_AnchoredPosition\":{\"x\":-25,\"y\":0},\"m_SizeDelta\":{\"x\":-50,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node = Node(layout, "HeaderBar/RankSummary/ProgressTrack");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0.63,\"y\":0.2},\"m_AnchorMax\":{\"x\":0.985,\"y\":0.26},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":0,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node = Node(layout, "HeaderBar/RankSummary/ProgressTrack/ProgressFill");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":1,\"y\":1},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":0,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
        node = Node(layout, "HeaderBar/RankSummary/EloHelp");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":1,\"y\":0.65},\"m_AnchorMax\":{\"x\":1,\"y\":0.65},\"m_AnchoredPosition\":{\"x\":-5,\"y\":0},\"m_SizeDelta\":{\"x\":38,\"y\":38},\"m_Pivot\":{\"x\":1,\"y\":0.5}}");
        node = Node(layout, "HeaderBar/RankSummary/EloHelp/Label");
        Apply(node, "{\"m_LocalRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"m_LocalPosition\":{\"x\":0,\"y\":0,\"z\":0},\"m_LocalScale\":{\"x\":1,\"y\":1,\"z\":1},\"m_AnchorMin\":{\"x\":0,\"y\":0},\"m_AnchorMax\":{\"x\":1,\"y\":1},\"m_AnchoredPosition\":{\"x\":0,\"y\":0},\"m_SizeDelta\":{\"x\":0,\"y\":0},\"m_Pivot\":{\"x\":0.5,\"y\":0.5}}");
    }

    public static void Validate(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        if (roots.SelectMany(r => r.GetComponentsInChildren<UserContainer>(true)).Count() != 1 ||
            roots.SelectMany(r => r.GetComponentsInChildren<NowPlayer>(true)).Count() != 1)
            throw new InvalidOperationException("Room restoration must retain one account and one statistics presenter.");
        foreach (var room in roots.SelectMany(r => r.GetComponentsInChildren<RoomListPanel>(true)))
        {
            var column = room.transform.Find("RoomOverview");
            string[] expected = { "Text (TMP)", "UserContainer", "NowPlayer", "Describe", "Links", "Panel" };
            if (!column || !column.Cast<Transform>().Select(t => t.name).SequenceEqual(expected) || room.transform.Find("RoomFilters") || room.transform.Find("NavigateBarPanel"))
                throw new InvalidOperationException("The complete original room overview has not been restored.");
            var summary = room.GetComponentInChildren<RoomProfileSummary>(true);
            if (!summary || summary.sourceLabels.Length != 5 || summary.labels.Length != 5 || !summary.portrait || !summary.progress)
                throw new InvalidOperationException("Room account bindings are incomplete.");
            if (column.Find("NowPlayer").GetComponentsInChildren<TMP_Text>(true).Any(t => !t.gameObject.activeSelf))
                throw new InvalidOperationException("All four original server-statistic labels must be visible.");
            if (column.GetComponentsInChildren<TMP_Text>(true).Any(t => !t.enabled))
                throw new InvalidOperationException("A restored room label still has its renderer disabled.");
            if (column.GetComponentsInChildren<UnityEngine.UI.Image>(true).Any(image => !image.enabled))
                throw new InvalidOperationException("A restored room image still has its renderer disabled.");
            foreach (var label in column.Find("NowPlayer").GetComponentsInChildren<TMP_Text>(true))
            {
                float alpha = label.color.a * label.canvasRenderer.GetAlpha();
                for (Transform parent = label.transform; parent && parent != column.parent; parent = parent.parent)
                    foreach (var group in parent.GetComponents<CanvasGroup>())
                        if (group.enabled) alpha *= group.alpha;
                if (alpha <= .99f) throw new InvalidOperationException("A restored server-statistic label is transparent: " + label.name);
            }
            var background = room.GetComponent<UnityEngine.UI.Image>();
            if (!background || !background.enabled || background.color != Color.black || background.sprite)
                throw new InvalidOperationException("Room page must retain the original opaque black backing.");
            var listBackground = room.transform.Find("RoomContent/RoomList").GetComponent<UnityEngine.UI.Image>();
            if (Mathf.Abs(listBackground.color.a - .12941177f) > .0001f)
                throw new InvalidOperationException("The original room list background transparency has changed.");
        }
        foreach (var panel in roots.SelectMany(r => r.GetComponentsInChildren<MatchPanel>(true)))
        {
            var lobby = Ref<MatchLobbyView>(panel, "lobbyView");
            if (lobby.GetComponentsInChildren<Transform>(true).Any(t => t.name == "NavigateBarPanel"))
                throw new InvalidOperationException("Matching sidebar remains.");
            var buttons = new SerializedObject(lobby).FindProperty("ruleButtons");
            if (buttons.arraySize != 5 || Enumerable.Range(0, 5).Any(i => !buttons.GetArrayElementAtIndex(i).objectReferenceValue || !((UnityEngine.UI.Button)buttons.GetArrayElementAtIndex(i).objectReferenceValue).gameObject.activeSelf))
                throw new InvalidOperationException("Matching must expose all five rule families in its top bar.");
        }
    }

    static T Ref<T>(Object value, string property) where T : Object => (T)new SerializedObject(value).FindProperty(property).objectReferenceValue;
    static void Array(Object value, string field, Object[] values) { var data = new SerializedObject(value); var array = data.FindProperty(field); array.arraySize = values.Length; for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i]; data.ApplyModifiedPropertiesWithoutUndo(); }
    static T Need<T>(Transform node) where T : Component => node.GetComponent<T>() ?? node.gameObject.AddComponent<T>();
    static RectTransform Node(Transform root, string path)
    {
        if (string.IsNullOrEmpty(path)) return (RectTransform)root;
        var existing = root.Find(path) as RectTransform; if (existing) return existing;
        int slash = path.LastIndexOf('/');
        var parent = slash < 0 ? root : Node(root, path.Substring(0, slash));
        var go = new GameObject(path.Substring(slash + 1), typeof(RectTransform)); go.layer = root.gameObject.layer;
        go.transform.SetParent(parent, false); return (RectTransform)go.transform;
    }
    static void Apply(Object target, string json)
    {
        string root = target is MonoBehaviour ? "MonoBehaviour" : target.GetType().Name;
        EditorJsonUtility.FromJsonOverwrite("{\"" + root + "\":" + json + "}", target);
        EditorUtility.SetDirty(target);
    }
    static Sprite Builtin(int id) => AssetDatabase.GetBuiltinExtraResource<Sprite>(id == 10907 ? "UI/Skin/Background.psd" : id == 10905 ? "UI/Skin/UISprite.psd" : "UI/Skin/Knob.psd");
    static void Image(Transform node, string json, Sprite sprite)
    {
        var image = Need<UnityEngine.UI.Image>(node); Apply(image, json); image.enabled = true;
        image.sprite = sprite; image.material = null; image.SetAllDirty();
    }
    static void Text(Transform node, string json, TMP_FontAsset font, bool localize)
    {
        var label = Need<TextMeshProUGUI>(node); Apply(label, json);
        label.enabled = true;
        label.font = font; label.fontSharedMaterial = font.material; label.fontStyle = FontStyles.Normal; label.fontWeight = FontWeight.Regular;
        var binding = Need<PlatformFontBinding>(node);
        binding.authoredFont = font; binding.authoredMaterial = font.material; binding.authoredStyle = FontStyles.Normal;
        binding.authoredWeight = FontWeight.Regular; binding.styleCaptured = true;
        binding.preserveAuthoredTypeface = binding.preserveAuthoredChineseFace = false;
        var localized = label.GetComponent<LocalizedPlatformLabel>();
        if (localize && !localized) localized = Need<LocalizedPlatformLabel>(node);
        if (localized)
        {
            if (localize) localized.key = label.text;
            localized.authoredSize = label.enableAutoSizing ? label.fontSizeMax : label.fontSize;
            localized.authoredAutoSize = label.enableAutoSizing;
            localized.minimumSize = label.fontSizeMin; localized.fitTranslation = true;
            localized.typographyBaked = true;
        }
        label.SetAllDirty();
    }
    static void Button(Transform node, string json)
    {
        var button = Need<UnityEngine.UI.Button>(node); Apply(button, json); button.targetGraphic = node.GetComponent<UnityEngine.UI.Image>();
    }
    static void RemoveLocalization(TMP_Text label)
    { var binding = label.GetComponent<LocalizedPlatformLabel>(); if (binding) Object.DestroyImmediate(binding); }
}
#endif
