#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 把赛事/基地大厅与详情层画回 MainScene。运行时只填数据、克隆场景里的示例。
/// 菜单：Tools/赛事大厅/烘焙面板到场景
/// </summary>
public static class EventPanelBaker
{
    private const string MainScenePath = "Assets/Scenes/MainScene.unity";
    private static readonly Color PanelBg = new Color(0.008f, 0f, 0f, 1f);
    private static readonly Color CardBg = new Color(0.165f, 0.255f, 0.451f, 1f);
    private static readonly Color Accent = new Color(1f, 0.62f, 0.08f, 1f);
    private static readonly Color NavIdle = new Color(0.13f, 0.13f, 0.13f, 1f);
    private static readonly Color ButtonBg = new Color(0.17f, 0.21f, 0.30f, 1f);
    private static readonly Color LabelColor = new Color(0.90f, 0.92f, 0.95f, 1f);
    private static Sprite _whiteSprite;
    private static TMP_FontAsset _tmpFont;

    [MenuItem("Tools/赛事大厅/烘焙面板到场景")]
    public static void Bake()
    {
        bool ok = BakeCurrentScene();
        if (ok)
        {
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            EditorPrefs.SetBool("OMU.EventPanelRestored.v2", true);
        }
        if (!Application.isBatchMode)
        {
            EditorUtility.DisplayDialog(
                "赛事大厅烘焙",
                ok ? "已还原赛事/基地大厅、详情层与示例预制体，并已保存场景。" : "找不到 EventPanel，无法烘焙。",
                "好的");
        }
        else if (!ok)
        {
            throw new System.Exception("EventPanel bake failed: EventPanel not found.");
        }
    }

    [InitializeOnLoadMethod]
    private static void AutoRestoreMissingDetail()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.delayCall += RestoreIfMissing;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += RestoreIfMissing;
    }

    public static void RestoreIfMissing()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling) return;
        if (FindSceneObject("EventDetailRoot") != null)
        {
            EditorPrefs.SetBool("OMU.EventPanelRestored.v2", true);
            return;
        }
        if (FindSceneObject("EventPanel") == null) return;
        if (!BakeCurrentScene()) return;
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        EditorPrefs.SetBool("OMU.EventPanelRestored.v2", true);
        Debug.Log("[EventPanelBaker] 已把赛事/基地大厅与详情层重新画回 MainScene。");
    }

    public static void BakeFromCommandLine()
    {
        EditorSceneManager.OpenScene(MainScenePath);
        Bake();
    }

    private static bool BakeCurrentScene()
    {
        GameObject eventPanel = FindSceneObject("EventPanel");
        if (eventPanel == null) return false;

        EnsureNetwork();
        WireWindows(eventPanel);
        WireHeader();

        Transform root = eventPanel.transform;
        Button eventTab = EnsureTabButton(root, "TitlePanel", "比赛");
        Button baseTab = EnsureTabButton(root, "TitlePanel (1)", "基地");
        Transform listRoot = EnsureListRoot(root);
        HideRogueRoomList(root);

        GameObject oldDetail = null;
        Transform old = root.Find("EventDetailRoot");
        if (old != null) oldDetail = old.gameObject;
        if (oldDetail != null) Object.DestroyImmediate(oldDetail);

        BuiltDetail detail = BuildDetail(root);

        GameObject itemTemplate = PlaceListTemplate(listRoot);
        TMP_Text emptyHint = EnsureEmptyHint(listRoot);

        EventLobbyPanel lobby = eventPanel.GetComponent<EventLobbyPanel>() ?? eventPanel.AddComponent<EventLobbyPanel>();
        SerializedObject lobbySo = new SerializedObject(lobby);
        lobbySo.FindProperty("eventTab").objectReferenceValue = eventTab;
        lobbySo.FindProperty("baseTab").objectReferenceValue = baseTab;
        lobbySo.FindProperty("eventTabImage").objectReferenceValue = eventTab != null ? eventTab.GetComponent<Image>() : null;
        lobbySo.FindProperty("baseTabImage").objectReferenceValue = baseTab != null ? baseTab.GetComponent<Image>() : null;
        lobbySo.FindProperty("listContent").objectReferenceValue = FindListContent(listRoot);
        lobbySo.FindProperty("itemTemplate").objectReferenceValue = itemTemplate;
        lobbySo.FindProperty("listRoot").objectReferenceValue = listRoot != null ? listRoot.gameObject : null;
        lobbySo.FindProperty("detailPanel").objectReferenceValue = detail.panel;
        lobbySo.FindProperty("emptyHint").objectReferenceValue = emptyHint;
        lobbySo.ApplyModifiedPropertiesWithoutUndo();

        if (eventPanel.GetComponent<CanvasGroup>() == null) eventPanel.AddComponent<CanvasGroup>();
        eventPanel.SetActive(false);

        EditorSceneManager.MarkSceneDirty(eventPanel.scene);
        return true;
    }

    private static void EnsureNetwork()
    {
        GameObject nm = FindSceneObject("NetworkManager");
        if (nm == null) return;
        if (nm.GetComponent<EventNetworkManager>() == null) nm.AddComponent<EventNetworkManager>();
    }

    private static void WireWindows(GameObject eventPanel)
    {
        WindowsManager wm = Object.FindFirstObjectByType<WindowsManager>();
        if (wm == null) return;
        SerializedObject so = new SerializedObject(wm);
        so.FindProperty("eventPanel").objectReferenceValue = eventPanel;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void WireHeader()
    {
        GameObject eventBtnGo = FindSceneObject("Event");
        if (eventBtnGo != null) eventBtnGo.SetActive(true);
        HeaderPanel header = Object.FindFirstObjectByType<HeaderPanel>();
        if (header == null || eventBtnGo == null) return;
        HeaderButton hb = eventBtnGo.GetComponent<HeaderButton>();
        SerializedObject so = new SerializedObject(header);
        so.FindProperty("eventButton").objectReferenceValue = hb;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Button EnsureTabButton(Transform root, string name, string label)
    {
        Transform tab = root.Find(name);
        if (tab == null) return null;
        Image image = tab.GetComponent<Image>();
        Button button = tab.GetComponent<Button>();
        if (button == null) button = tab.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;
        TMP_Text text = tab.GetComponentInChildren<TMP_Text>(true);
        if (text != null) text.text = label;
        return button;
    }

    private static Transform EnsureListRoot(Transform root)
    {
        Transform list = root.Find("EventListRoot");
        if (list == null)
        {
            foreach (Transform child in root)
            {
                if (child.GetComponentInChildren<ScrollRect>(true) != null && child.name != "RoomListPanel")
                {
                    child.name = "EventListRoot";
                    list = child;
                    break;
                }
            }
        }
        if (list != null)
        {
            ScrollRect scroll = list.GetComponentInChildren<ScrollRect>(true);
            if (scroll != null)
            {
                scroll.horizontal = false;
                scroll.vertical = true;
            }
        }
        return list;
    }

    private static Transform FindListContent(Transform listRoot)
    {
        if (listRoot == null) return null;
        ScrollRect scroll = listRoot.GetComponentInChildren<ScrollRect>(true);
        if (scroll != null && scroll.content != null) return scroll.content;
        Transform named = listRoot.Find("Content");
        return named != null ? named : listRoot;
    }

    private static void HideRogueRoomList(Transform root)
    {
        Transform rogue = root.Find("RoomListPanel");
        if (rogue != null) rogue.gameObject.SetActive(false);
    }

    private static GameObject PlaceListTemplate(Transform listRoot)
    {
        Transform content = FindListContent(listRoot);
        if (content == null) return null;

        VerticalLayoutGroup vlg = content.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(16, 16, 12, 12);
        vlg.spacing = 12f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        ContentSizeFitter fit = content.GetComponent<ContentSizeFitter>();
        if (fit == null) fit = content.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        foreach (Transform child in content)
        {
            if (child.name == "EventItem" || child.name.StartsWith("EventItem")) child.gameObject.SetActive(false);
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Event/EventItem.prefab");
        GameObject template = content.Find("EventItemTemplate") != null
            ? content.Find("EventItemTemplate").gameObject
            : null;
        if (template == null && prefab != null)
        {
            template = (GameObject)PrefabUtility.InstantiatePrefab(prefab, content);
            template.name = "EventItemTemplate";
        }
        if (template != null) template.SetActive(false);
        return template;
    }

    private static TMP_Text EnsureEmptyHint(Transform listRoot)
    {
        Transform content = FindListContent(listRoot);
        if (content == null) return null;
        Transform existing = content.Find("EmptyHint");
        if (existing != null) return existing.GetComponent<TMP_Text>();
        TMP_Text hint = NewText(content.GetComponent<RectTransform>(), "EmptyHint", "暂无赛事", 22, LabelColor, TextAnchor.MiddleCenter);
        RectTransform rt = hint.rectTransform;
        rt.sizeDelta = new Vector2(0f, 80f);
        LayoutElement le = hint.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 80f;
        hint.gameObject.SetActive(false);
        return hint;
    }

    private class BuiltDetail
    {
        public EventDetailPanel panel;
    }

    private static BuiltDetail BuildDetail(Transform eventRoot)
    {
        RectTransform root = NewRect("EventDetailRoot", eventRoot);
        Stretch(root);
        Image bg = root.gameObject.AddComponent<Image>();
        bg.color = PanelBg;

        RectTransform top = NewRect("TopBar", root);
        StretchTop(top, 0f, 88f, 0f);
        Image topBg = top.gameObject.AddComponent<Image>();
        topBg.color = CardBg;

        Button back = NewButton(top, "BackButton", "返回", ButtonBg, Color.white);
        PlaceTop(back.transform as RectTransform, 20f, 18f, 140f, 52f);
        TMP_Text pageTitle = NewText(top, "PageTitle", "赛事详情", 28, Color.white, TextAnchor.MiddleCenter);
        Stretch(pageTitle.rectTransform);
        pageTitle.rectTransform.offsetMin = new Vector2(180f, 0f);
        pageTitle.rectTransform.offsetMax = new Vector2(-20f, 0f);

        RectTransform nav = NewRect("NavigateBar", root);
        nav.anchorMin = new Vector2(0f, 0f);
        nav.anchorMax = new Vector2(0f, 1f);
        nav.pivot = new Vector2(0f, 0.5f);
        nav.anchoredPosition = new Vector2(0f, -44f);
        nav.sizeDelta = new Vector2(200f, -88f);
        Image navBg = nav.gameObject.AddComponent<Image>();
        navBg.color = new Color(0.07f, 0.08f, 0.12f, 1f);
        VerticalLayoutGroup navLayout = nav.gameObject.AddComponent<VerticalLayoutGroup>();
        navLayout.padding = new RectOffset(0, 0, 24, 24);
        navLayout.spacing = 14f;
        navLayout.childAlignment = TextAnchor.UpperCenter;
        navLayout.childControlWidth = true;
        navLayout.childControlHeight = false;
        navLayout.childForceExpandWidth = true;
        navLayout.childForceExpandHeight = false;

        Button homeNav = NewNavButton(nav, "HomeNav", "描述", true);
        Button roomsNav = NewNavButton(nav, "RoomsNav", "加入房间", false);
        Button spectateNav = NewNavButton(nav, "SpectateNav", "观战", false);
        Button recordsNav = NewNavButton(nav, "RecordsNav", "牌谱", false);

        RectTransform pages = NewRect("Pages", root);
        pages.anchorMin = Vector2.zero;
        pages.anchorMax = Vector2.one;
        pages.offsetMin = new Vector2(200f, 0f);
        pages.offsetMax = new Vector2(0f, -88f);

        GameObject homePage = BuildHomePage(pages, out TMP_Text titleText, out TMP_Text descText, out TMP_Text statusText, out TMP_Text announceText, out Button registerButton, out Button readyButton, out TMP_Text registerLabel, out TMP_Text readyLabel);
        GameObject roomsPage = BuildRoomsPage(pages, out Transform roomContent, out Transform readyContent, out GameObject adminBar, out Button createRoomButton, out Button seatButton, out GameObject roomExample);
        GameObject spectatePage = BuildScrollPage(pages, "SpectatePage", "SpectateContent", out Transform spectateContent);
        GameObject recordsPage = BuildScrollPage(pages, "RecordsPage", "RecordContent", out Transform recordContent);
        homePage.SetActive(true);
        roomsPage.SetActive(false);
        spectatePage.SetActive(false);
        recordsPage.SetActive(false);

        GameObject actionExample = PlacePrefab(spectateContent, "Assets/Prefabs/Event/EventActionRow.prefab", "EventActionRowExample");
        GameObject readyExample = PlacePrefab(readyContent, "Assets/Prefabs/Event/EventReadyRow.prefab", "EventReadyRowExample");
        GameObject emptyExample = PlacePrefab(spectateContent, "Assets/Prefabs/Event/EventEmptyRow.prefab", "EventEmptyRowExample");
        GameObject recordExample = PlacePrefab(recordContent, "Assets/Prefabs/Record/RecordItem (3).prefab", "RecordItemExample");
        if (recordExample != null) recordExample.transform.localScale = Vector3.one * 0.85f;
        if (roomExample != null) roomExample.transform.localScale = Vector3.one * 0.85f;

        GameObject popup = BuildRegisterPopup(root, out TMP_InputField contact, out TMP_InputField remark, out TMP_InputField joinCode, out Button submit, out Button cancel);

        RectTransform templates = NewRect("Templates", root);
        templates.gameObject.SetActive(false);
        GameObject actionTemplate = PlacePrefab(templates, "Assets/Prefabs/Event/EventActionRow.prefab", "EventActionRowTemplate");
        GameObject readyTemplate = PlacePrefab(templates, "Assets/Prefabs/Event/EventReadyRow.prefab", "EventReadyRowTemplate");
        GameObject emptyTemplate = PlacePrefab(templates, "Assets/Prefabs/Event/EventEmptyRow.prefab", "EventEmptyRowTemplate");
        GameObject roomPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Room/RoomItem.prefab");
        GameObject recordPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Record/RecordItem (3).prefab");

        EventDetailPanel panel = root.gameObject.AddComponent<EventDetailPanel>();
        SerializedObject so = new SerializedObject(panel);
        so.FindProperty("backButton").objectReferenceValue = back;
        so.FindProperty("pageTitle").objectReferenceValue = pageTitle;
        so.FindProperty("homeNav").objectReferenceValue = homeNav;
        so.FindProperty("roomsNav").objectReferenceValue = roomsNav;
        so.FindProperty("spectateNav").objectReferenceValue = spectateNav;
        so.FindProperty("recordsNav").objectReferenceValue = recordsNav;
        so.FindProperty("homeNavImage").objectReferenceValue = homeNav.GetComponent<Image>();
        so.FindProperty("roomsNavImage").objectReferenceValue = roomsNav.GetComponent<Image>();
        so.FindProperty("spectateNavImage").objectReferenceValue = spectateNav.GetComponent<Image>();
        so.FindProperty("recordsNavImage").objectReferenceValue = recordsNav.GetComponent<Image>();
        so.FindProperty("homePage").objectReferenceValue = homePage;
        so.FindProperty("roomsPage").objectReferenceValue = roomsPage;
        so.FindProperty("spectatePage").objectReferenceValue = spectatePage;
        so.FindProperty("recordsPage").objectReferenceValue = recordsPage;
        so.FindProperty("titleText").objectReferenceValue = titleText;
        so.FindProperty("descText").objectReferenceValue = descText;
        so.FindProperty("statusText").objectReferenceValue = statusText;
        so.FindProperty("announceText").objectReferenceValue = announceText;
        so.FindProperty("registerButton").objectReferenceValue = registerButton;
        so.FindProperty("readyButton").objectReferenceValue = readyButton;
        so.FindProperty("registerLabel").objectReferenceValue = registerLabel;
        so.FindProperty("readyLabel").objectReferenceValue = readyLabel;
        so.FindProperty("roomContent").objectReferenceValue = roomContent;
        so.FindProperty("readyContent").objectReferenceValue = readyContent;
        so.FindProperty("adminBar").objectReferenceValue = adminBar;
        so.FindProperty("createRoomButton").objectReferenceValue = createRoomButton;
        so.FindProperty("seatButton").objectReferenceValue = seatButton;
        so.FindProperty("roomItemPrefab").objectReferenceValue = roomPrefab != null ? roomPrefab : roomExample;
        so.FindProperty("spectateContent").objectReferenceValue = spectateContent;
        so.FindProperty("recordContent").objectReferenceValue = recordContent;
        so.FindProperty("recordItemPrefab").objectReferenceValue = recordPrefab != null ? recordPrefab : recordExample;
        so.FindProperty("actionRowTemplate").objectReferenceValue = actionTemplate != null ? actionTemplate : actionExample;
        so.FindProperty("readyRowTemplate").objectReferenceValue = readyTemplate != null ? readyTemplate : readyExample;
        so.FindProperty("emptyRowTemplate").objectReferenceValue = emptyTemplate != null ? emptyTemplate : emptyExample;
        so.FindProperty("registerPopup").objectReferenceValue = popup;
        so.FindProperty("contactInput").objectReferenceValue = contact;
        so.FindProperty("remarkInput").objectReferenceValue = remark;
        so.FindProperty("joinCodeInput").objectReferenceValue = joinCode;
        so.FindProperty("submitRegisterButton").objectReferenceValue = submit;
        so.FindProperty("cancelRegisterButton").objectReferenceValue = cancel;
        so.ApplyModifiedPropertiesWithoutUndo();

        root.gameObject.SetActive(false);
        return new BuiltDetail { panel = panel };
    }

    private static GameObject BuildHomePage(
        RectTransform pages,
        out TMP_Text titleText,
        out TMP_Text descText,
        out TMP_Text statusText,
        out TMP_Text announceText,
        out Button registerButton,
        out Button readyButton,
        out TMP_Text registerLabel,
        out TMP_Text readyLabel)
    {
        RectTransform page = NewRect("HomePage", pages);
        Stretch(page);

        titleText = NewText(page, "TitleText", "赛事名称", 32, Color.white, TextAnchor.UpperLeft);
        StretchTop(titleText.rectTransform, 28f, 48f, 20f);

        statusText = NewText(page, "StatusText", "状态：", 18, LabelColor, TextAnchor.UpperLeft);
        StretchTop(statusText.rectTransform, 28f, 72f, 72f);
        statusText.enableWordWrapping = true;

        descText = NewText(page, "DescText", "暂无介绍", 18, LabelColor, TextAnchor.UpperLeft);
        StretchTop(descText.rectTransform, 28f, 180f, 152f);
        descText.enableWordWrapping = true;
        descText.overflowMode = TextOverflowModes.Overflow;

        announceText = NewText(page, "AnnounceText", "暂无公告", 16, LabelColor, TextAnchor.UpperLeft);
        StretchTop(announceText.rectTransform, 28f, 160f, 348f);
        announceText.enableWordWrapping = true;
        announceText.overflowMode = TextOverflowModes.Overflow;

        RectTransform actions = NewRect("HomeActions", page);
        actions.anchorMin = new Vector2(1f, 1f);
        actions.anchorMax = new Vector2(1f, 1f);
        actions.pivot = new Vector2(1f, 1f);
        actions.anchoredPosition = new Vector2(-28f, -20f);
        actions.sizeDelta = new Vector2(280f, 120f);
        VerticalLayoutGroup layout = actions.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        registerButton = NewButton(actions, "RegisterButton", "报名比赛", Accent, new Color(0.12f, 0.06f, 0.02f, 1f));
        (registerButton.transform as RectTransform).sizeDelta = new Vector2(280f, 48f);
        registerLabel = registerButton.GetComponentInChildren<TMP_Text>(true);
        readyButton = NewButton(actions, "ReadyButton", "加入准备", ButtonBg, Color.white);
        (readyButton.transform as RectTransform).sizeDelta = new Vector2(280f, 48f);
        readyLabel = readyButton.GetComponentInChildren<TMP_Text>(true);
        return page.gameObject;
    }

    private static GameObject BuildRoomsPage(
        RectTransform pages,
        out Transform roomContent,
        out Transform readyContent,
        out GameObject adminBar,
        out Button createRoomButton,
        out Button seatButton,
        out GameObject roomExample)
    {
        RectTransform page = NewRect("RoomsPage", pages);
        Stretch(page);

        RectTransform bar = NewRect("AdminBar", page);
        StretchTop(bar, 16f, 56f, 12f);
        HorizontalLayoutGroup h = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 12f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = false;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;
        createRoomButton = NewButton(bar, "CreateRoomButton", "创建房间", Accent, new Color(0.12f, 0.06f, 0.02f, 1f));
        (createRoomButton.transform as RectTransform).sizeDelta = new Vector2(160f, 44f);
        seatButton = NewButton(bar, "SeatButton", "组桌", ButtonBg, Color.white);
        (seatButton.transform as RectTransform).sizeDelta = new Vector2(120f, 44f);
        adminBar = bar.gameObject;

        RectTransform roomHost = NewRect("RoomScroll", page);
        roomHost.anchorMin = new Vector2(0f, 0.38f);
        roomHost.anchorMax = new Vector2(1f, 1f);
        roomHost.offsetMin = new Vector2(16f, 8f);
        roomHost.offsetMax = new Vector2(-16f, -76f);
        roomContent = BuildScrollContent(roomHost, "RoomContent");
        roomExample = PlacePrefab(roomContent, "Assets/Prefabs/Room/RoomItem.prefab", "RoomItemExample");

        RectTransform readyHost = NewRect("ReadyScroll", page);
        readyHost.anchorMin = new Vector2(0f, 0f);
        readyHost.anchorMax = new Vector2(1f, 0.38f);
        readyHost.offsetMin = new Vector2(16f, 16f);
        readyHost.offsetMax = new Vector2(-16f, -8f);
        Image readyBg = readyHost.gameObject.AddComponent<Image>();
        readyBg.color = new Color(0.10f, 0.12f, 0.16f, 1f);
        readyContent = BuildScrollContent(readyHost, "ReadyContent");

        return page.gameObject;
    }

    private static GameObject BuildScrollPage(RectTransform pages, string pageName, string contentName, out Transform content)
    {
        RectTransform page = NewRect(pageName, pages);
        Stretch(page);
        RectTransform host = NewRect(pageName + "Scroll", page);
        Stretch(host);
        host.offsetMin = new Vector2(16f, 16f);
        host.offsetMax = new Vector2(-16f, -16f);
        content = BuildScrollContent(host, contentName);
        return page.gameObject;
    }

    private static Transform BuildScrollContent(RectTransform host, string contentName)
    {
        Image hostBg = host.GetComponent<Image>();
        if (hostBg == null)
        {
            hostBg = host.gameObject.AddComponent<Image>();
            hostBg.color = new Color(0.10f, 0.12f, 0.16f, 1f);
        }
        ScrollRect scroll = host.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 40f;

        RectTransform viewport = NewRect("Viewport", host);
        Stretch(viewport);
        Image vpImg = viewport.gameObject.AddComponent<Image>();
        vpImg.color = Color.white;
        Mask mask = viewport.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        RectTransform content = NewRect(contentName, viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0f, 200f);
        VerticalLayoutGroup vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(12, 12, 12, 12);
        vlg.spacing = 10f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        ContentSizeFitter fit = content.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.content = content;
        scroll.viewport = viewport;
        return content;
    }

    private static GameObject BuildRegisterPopup(
        RectTransform root,
        out TMP_InputField contact,
        out TMP_InputField remark,
        out TMP_InputField joinCode,
        out Button submit,
        out Button cancel)
    {
        RectTransform overlay = NewRect("RegisterPopup", root);
        Stretch(overlay);
        Image dim = overlay.gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);

        RectTransform box = NewRect("Box", overlay);
        box.anchorMin = new Vector2(0.5f, 0.5f);
        box.anchorMax = new Vector2(0.5f, 0.5f);
        box.pivot = new Vector2(0.5f, 0.5f);
        box.sizeDelta = new Vector2(520f, 420f);
        Image boxBg = box.gameObject.AddComponent<Image>();
        boxBg.color = new Color(0.10f, 0.12f, 0.16f, 1f);

        TMP_Text title = NewText(box, "Title", "报名", 22, Color.white, TextAnchor.MiddleCenter);
        StretchTop(title.rectTransform, 16f, 40f, 16f);

        contact = CreateInput(box, "ContactInput", "联系方式");
        StretchTop(contact.GetComponent<RectTransform>(), 24f, 44f, 72f);
        remark = CreateInput(box, "RemarkInput", "备注（可选）");
        StretchTop(remark.GetComponent<RectTransform>(), 24f, 44f, 128f);
        joinCode = CreateInput(box, "JoinCodeInput", "加入口令（如需要）");
        StretchTop(joinCode.GetComponent<RectTransform>(), 24f, 44f, 184f);

        RectTransform row = NewRect("PopupActions", box);
        StretchTop(row, 24f, 48f, 248f);
        HorizontalLayoutGroup h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 16f;
        h.childControlWidth = true;
        h.childForceExpandWidth = true;
        cancel = NewButton(row, "CancelRegisterButton", "取消", ButtonBg, Color.white);
        submit = NewButton(row, "SubmitRegisterButton", "提交", Accent, new Color(0.12f, 0.06f, 0.02f, 1f));

        overlay.gameObject.SetActive(false);
        return overlay.gameObject;
    }

    private static Button NewNavButton(RectTransform parent, string name, string label, bool active)
    {
        Button button = NewButton(parent, name, label, active ? Accent : NavIdle, active ? new Color(0.12f, 0.06f, 0.02f, 1f) : Color.white);
        LayoutElement le = button.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 56f;
        le.minHeight = 56f;
        return button;
    }

    private static GameObject PlacePrefab(Transform parent, string path, string name)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null || parent == null) return null;
        Transform existing = parent.Find(name);
        if (existing != null) return existing.gameObject;
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = name;
        return go;
    }

    private static GameObject FindSceneObject(string name)
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t != null && t.name == name && t.gameObject.scene.IsValid()) return t.gameObject;
        }
        return null;
    }

    private static Sprite WhiteSprite()
    {
        if (_whiteSprite == null)
        {
            _whiteSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            if (_whiteSprite == null)
                _whiteSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
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
        TextMeshProUGUI text = rt.gameObject.AddComponent<TextMeshProUGUI>();
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

    private static TMP_InputField CreateInput(RectTransform parent, string name, string placeholderText)
    {
        RectTransform rt = NewRect(name, parent);
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(0.13f, 0.15f, 0.20f, 1f);
        TMP_Text text = NewText(rt, "Text", "", 16, Color.white, TextAnchor.MiddleLeft);
        Stretch(text.rectTransform);
        text.rectTransform.offsetMin = new Vector2(8f, 0f);
        text.rectTransform.offsetMax = new Vector2(-8f, 0f);
        TMP_Text ph = NewText(rt, "Placeholder", placeholderText, 16, new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleLeft);
        Stretch(ph.rectTransform);
        ph.rectTransform.offsetMin = new Vector2(8f, 0f);
        ph.rectTransform.offsetMax = new Vector2(-8f, 0f);
        TMP_InputField input = rt.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = img;
        input.textComponent = text;
        input.placeholder = ph;
        return input;
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
            TMP_Text text = NewText(rt, "Label", label, 18, textColor, TextAnchor.MiddleCenter);
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
}
#endif
