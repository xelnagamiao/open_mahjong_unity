using System;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 一次性：只改赛事/基地面板与 Event 预制体。禁止改牌谱/通知等其它大厅页。
/// </summary>
public static class EventVenueSceneTool {
    const string RequestPath = @"d:\open_mahjong_unity\.om_workspace\tmp\event_venue.request";
    const string DonePath = @"d:\open_mahjong_unity\.om_workspace\tmp\event_venue.done";
    const string DumpPath = @"d:\open_mahjong_unity\.om_workspace\tmp\event_layout_dump.txt";
    const string FontPath = "Assets/Resources/font/Chinese/AlibabaPuHuiTi/AlibabaPuHuiTi-3-55-Regular SDF.asset";
    const string RestoreSceneAsset = "Assets/Scenes/_RestoreLobbySource.unity";
    const string RestoreBackupAbs = @"d:\open_mahjong_unity\.om_workspace\tmp\MainScene.before-venue-20260827-014208.unity";

    static readonly string[] EventDumpTargets = { "EventPanel" };

    static readonly string[] ForeignPanels = {
        "RecordPanel", "MatchPanel", "SpectatorPanel", "FriendPanel",
        "MenuPanel", "NoticePanel", "AboutUsPanel", "SceneConfigPanel", "ConfigPanel",
    };

    static readonly Color ListCard = new Color(0.137f, 0.244f, 0.426f, 1f);
    static readonly Color ItemCard = new Color(0.10f, 0.16f, 0.28f, 1f);
    static readonly Color HeaderStrip = new Color(0.07f, 0.12f, 0.22f, 1f);
    static readonly Color TabActive = new Color(1f, 0.62f, 0.08f, 1f);
    static readonly Color TabIdle = new Color(0.08f, 0.11f, 0.18f, 1f);
    static readonly Color Gold = new Color(1f, 0.62f, 0.08f, 1f);

    [InitializeOnLoad]
    static class AutoRun {
        static AutoRun() {
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        static void OnPlayMode(PlayModeStateChange state) {
            if (state == PlayModeStateChange.EnteredEditMode) Poll();
        }
    }

    static void Poll() {
        if (!File.Exists(RequestPath)) return;
        if (Application.isBatchMode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) {
            EditorApplication.isPlaying = false;
            return;
        }
        TryRun();
    }

    public static void TryRun() {
        if (!File.Exists(RequestPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        string cmd = File.ReadAllText(RequestPath).Trim();
        File.Delete(RequestPath);
        string result;
        try {
            if (string.Equals(cmd, "dump", StringComparison.OrdinalIgnoreCase)) {
                result = Dump();
            } else if (string.Equals(cmd, "restore_foreign", StringComparison.OrdinalIgnoreCase)) {
                result = RestoreForeignPanels();
            } else {
                result = Rebuild();
            }
        } catch (Exception e) {
            result = "ERROR\n" + e;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(DonePath));
        File.WriteAllText(DonePath, result, Encoding.UTF8);
        Debug.Log("[EventVenueSceneTool] " + result);
    }

    [MenuItem("Tools/Event/Dump lobby layout")]
    public static void MenuDump() {
        EditorUtility.DisplayDialog("Dump", Dump(), "OK");
    }

    [MenuItem("Tools/Event/Rebuild venue panels")]
    public static void MenuRebuild() {
        EditorUtility.DisplayDialog("Rebuild", Rebuild(), "OK");
    }

    public static string Dump() {
        var sb = new StringBuilder();
        foreach (string name in EventDumpTargets) {
            GameObject go = Find(name);
            if (go == null) {
                sb.AppendLine("MISSING " + name);
                continue;
            }
            RectTransform canvasRt = go.GetComponentInParent<Canvas>()?.rootCanvas?.transform as RectTransform;
            DumpNode(sb, go.transform, 0, canvasRt);
            sb.AppendLine();
        }
        Directory.CreateDirectory(Path.GetDirectoryName(DumpPath));
        File.WriteAllText(DumpPath, sb.ToString(), Encoding.UTF8);
        return "dumped " + sb.Length + " chars -> " + DumpPath;
    }

    static void DumpNode(StringBuilder sb, Transform t, int depth, RectTransform canvasRt) {
        if (depth > 3) return;
        var rt = t as RectTransform;
        string indent = new string(' ', depth * 2);
        if (rt != null) {
            sb.AppendLine(string.Format(
                "{0}{1} active={2} anchor=({3:0.###},{4:0.###})-({5:0.###},{6:0.###}) pos=({7:0.#},{8:0.#}) size=({9:0.#},{10:0.#}) pivot=({11:0.##},{12:0.##})",
                indent, t.name, t.gameObject.activeSelf,
                rt.anchorMin.x, rt.anchorMin.y, rt.anchorMax.x, rt.anchorMax.y,
                rt.anchoredPosition.x, rt.anchoredPosition.y, rt.sizeDelta.x, rt.sizeDelta.y,
                rt.pivot.x, rt.pivot.y));
            if (canvasRt != null && depth <= 2) {
                string overflow = OverflowMark(rt, canvasRt);
                if (overflow != null) sb.AppendLine(indent + "  " + overflow);
            }
        } else {
            sb.AppendLine(indent + t.name);
        }
        if (depth == 0) {
            foreach (var mb in t.GetComponents<MonoBehaviour>()) {
                if (mb == null) continue;
                SerializedObject so = new SerializedObject(mb);
                SerializedProperty it = so.GetIterator();
                bool enter = true;
                while (it.NextVisible(enter)) {
                    enter = false;
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue == null
                        && it.name != "m_Script" && !it.name.StartsWith("m_", StringComparison.Ordinal)) {
                        sb.AppendLine(indent + "  MISSING " + mb.GetType().Name + "." + it.name);
                    }
                }
            }
        }
        for (int i = 0; i < t.childCount; i++) DumpNode(sb, t.GetChild(i), depth + 1, canvasRt);
    }

    static string OverflowMark(RectTransform rt, RectTransform canvasRt) {
        Vector3[] corners = new Vector3[4];
        Vector3[] canvas = new Vector3[4];
        rt.GetWorldCorners(corners);
        canvasRt.GetWorldCorners(canvas);
        float left = canvas[0].x;
        float right = canvas[2].x;
        float bottom = canvas[0].y;
        float top = canvas[1].y;
        for (int i = 0; i < 4; i++) {
            if (corners[i].x > right + 4f) return "OVERFLOW_RIGHT";
            if (corners[i].x < left - 4f) return "OVERFLOW_LEFT";
            if (corners[i].y > top + 4f) return "OVERFLOW_TOP";
            if (corners[i].y < bottom - 4f) return "OVERFLOW_BOTTOM";
        }
        return null;
    }

    public static string Rebuild() {
        var log = new StringBuilder();
        log.AppendLine(Dump());
        log.AppendLine(RestylePrefabs());
        log.AppendLine(RebuildEventPanel());
        Scene scene = Find("EventPanel") != null ? Find("EventPanel").scene : default;
        if (scene.IsValid()) {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssets();
        log.AppendLine(Dump());
        return log.ToString();
    }

    static string RestylePrefabs() {
        int n = 0;
        n += RestyleEventItem();
        n += RestylePrefab("Assets/Prefabs/Event/EventActionRow.prefab", 72f, new Color(0.12f, 0.18f, 0.30f, 1f));
        n += RestylePrefab("Assets/Prefabs/Event/EventEmptyRow.prefab", 56f, new Color(0.08f, 0.10f, 0.16f, 0.4f));
        n += RestylePrefab("Assets/Prefabs/Event/EventReadyRow.prefab", 56f, new Color(0.12f, 0.18f, 0.30f, 1f));
        return "prefabs " + n;
    }

    static Sprite BuiltinUiSprite() {
        Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
        if (sprite == null) sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Background.psd");
        return sprite;
    }

    static int RestylePrefab(string path, float height, Color color) {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        if (root == null) return 0;
        ApplyCardRect(root, height, color);
        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
        return 1;
    }

    static int RestyleEventItem() {
        const string path = "Assets/Prefabs/Event/EventItem.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        if (root == null) return 0;
        ApplyCardRect(root, 168f, ItemCard);
        Transform header = root.transform.Find("Header");
        if (header != null) {
            var headerImage = header.GetComponent<Image>();
            if (headerImage != null) {
                headerImage.color = HeaderStrip;
                ApplySliced(headerImage);
            }
            Stretch(header.GetComponent<RectTransform>(), 0f, 1f, 1f, 1f);
            var headerRt = header.GetComponent<RectTransform>();
            headerRt.pivot = new Vector2(0.5f, 1f);
            headerRt.anchoredPosition = Vector2.zero;
            headerRt.sizeDelta = new Vector2(0f, 56f);
            RestyleTmp(header.Find("Name"), Color.white, 30, TextAlignmentOptions.MidlineLeft, new Vector2(16f, 0f), new Vector2(-220f, -8f));
            PlaceRightLabel(header.Find("Kind"), -108f, 88f, 32f);
            PlaceRightLabel(header.Find("Status"), -20f, 88f, 32f);
        }
        Transform body = root.transform.Find("Body");
        if (body != null) {
            var bodyRt = body.GetComponent<RectTransform>();
            bodyRt.anchorMin = new Vector2(0f, 0f);
            bodyRt.anchorMax = new Vector2(1f, 1f);
            bodyRt.offsetMin = new Vector2(16f, 12f);
            bodyRt.offsetMax = new Vector2(-16f, -60f);
            RestyleTmp(body.Find("Desc"), new Color(0.82f, 0.88f, 0.96f, 0.92f), 22, TextAlignmentOptions.TopLeft, Vector2.zero, new Vector2(-196f, 0f));
            Transform enter = body.Find("Enter");
            if (enter != null) {
                var enterRt = enter.GetComponent<RectTransform>();
                enterRt.anchorMin = new Vector2(1f, 0.5f);
                enterRt.anchorMax = new Vector2(1f, 0.5f);
                enterRt.pivot = new Vector2(1f, 0.5f);
                enterRt.anchoredPosition = new Vector2(0f, 0f);
                enterRt.sizeDelta = new Vector2(176f, 56f);
                var enterImage = enter.GetComponent<Image>();
                if (enterImage != null) {
                    enterImage.color = Gold;
                    ApplySliced(enterImage);
                }
                RestyleTmp(enter.Find("Label"), new Color(0.12f, 0.06f, 0.02f, 1f), 24, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
            }
        }
        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
        return 1;
    }

    static void ApplyCardRect(GameObject root, float height, Color color) {
        var rt = root.GetComponent<RectTransform>();
        if (rt != null) {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, height);
            rt.anchoredPosition = Vector2.zero;
        }
        var image = root.GetComponent<Image>();
        if (image != null) {
            image.color = color;
            ApplySliced(image);
        }
        var le = root.GetComponent<LayoutElement>() ?? root.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
        le.flexibleWidth = 1f;
    }

    static void ApplySliced(Image image) {
        Sprite sprite = BuiltinUiSprite();
        if (sprite != null) image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 1f;
    }

    static void PlaceRightLabel(Transform t, float x, float width, float height) {
        if (t == null) return;
        var rt = t.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = new Vector2(x, 0f);
        rt.sizeDelta = new Vector2(width, height);
        RestyleTmp(t, new Color(1f, 0.9f, 0.55f, 1f), 20, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
    }

    static void RestyleTmp(Transform t, Color color, float size, TextAlignmentOptions align, Vector2 pos, Vector2 sizeDelta) {
        if (t == null) return;
        var tmp = t.GetComponent<TextMeshProUGUI>();
        if (tmp != null) {
            tmp.color = color;
            tmp.fontSize = size;
            tmp.alignment = align;
            tmp.enableWordWrapping = align != TextAlignmentOptions.Center;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
        }
        var rt = t.GetComponent<RectTransform>();
        if (rt == null) return;
        if (align == TextAlignmentOptions.MidlineLeft || align == TextAlignmentOptions.TopLeft) {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = sizeDelta;
        }
    }

    static string RebuildEventPanel() {
        GameObject eventPanel = Find("EventPanel");
        if (eventPanel == null) return "no EventPanel";
        Undo.RegisterFullObjectHierarchyUndo(eventPanel, "Rebuild EventPanel");

        StretchFull(eventPanel.GetComponent<RectTransform>());
        SetImageColor(eventPanel, Color.black);

        Transform duplicateRoomList = null;
        for (int i = 0; i < eventPanel.transform.childCount; i++) {
            Transform child = eventPanel.transform.GetChild(i);
            if (child.name == "RoomListPanel") duplicateRoomList = child;
        }
        if (duplicateRoomList != null) Undo.DestroyObjectImmediate(duplicateRoomList.gameObject);

        GameObject eventTab = FindChild(eventPanel.transform, "TitlePanel");
        GameObject baseTab = FindChild(eventPanel.transform, "TitlePanel (1)");
        GameObject sideNav = EnsureEventSideNav(eventPanel, eventTab, baseTab);

        GameObject listRoot = FindChild(eventPanel.transform, "EventListRoot");
        if (listRoot != null) {
            Stretch(listRoot.GetComponent<RectTransform>(), 0.30f, 0.06f, 0.80f, 0.94f);
            SetImageColor(listRoot, ListCard);
            ApplySlicedMaybe(listRoot.GetComponent<Image>());
            StretchNamedChild(listRoot.transform, "Scroll View");
        }

        GameObject detail = FindChild(eventPanel.transform, "EventDetailRoot");
        if (detail != null) RebuildDetail(detail);

        GameObject venueCreate = FindChild(eventPanel.transform, "VenueCreateRoomPanel");
        if (venueCreate == null) {
            GameObject source = Find("CreateRoomPanel");
            if (source != null) {
                venueCreate = UnityEngine.Object.Instantiate(source, eventPanel.transform);
                venueCreate.name = "VenueCreateRoomPanel";
                Undo.RegisterCreatedObjectUndo(venueCreate, "VenueCreateRoomPanel");
            }
        }
        if (venueCreate != null) {
            StretchFull(venueCreate.GetComponent<RectTransform>());
            venueCreate.SetActive(false);
            venueCreate.transform.SetAsLastSibling();
            for (int i = 0; i < venueCreate.transform.childCount; i++) {
                Transform child = venueCreate.transform.GetChild(i);
                if (child.name.StartsWith("DetailedConfigPanel", StringComparison.Ordinal)) {
                    child.gameObject.SetActive(false);
                }
            }
        }

        BindEventScripts(eventPanel, listRoot, detail, eventTab, baseTab, venueCreate, sideNav);
        return "event panel rebuilt nav=" + (sideNav != null) + " create=" + (venueCreate != null);
    }

    static GameObject EnsureEventSideNav(GameObject eventPanel, GameObject eventTab, GameObject baseTab) {
        GameObject sideNav = FindChild(eventPanel.transform, "EventSideNav");
        if (sideNav == null) {
            sideNav = new GameObject("EventSideNav", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            sideNav.layer = eventPanel.layer;
            sideNav.transform.SetParent(eventPanel.transform, false);
            Undo.RegisterCreatedObjectUndo(sideNav, "EventSideNav");
        }
        Stretch(sideNav.GetComponent<RectTransform>(), 0.10f, 0f, 0.30f, 1f);
        var image = sideNav.GetComponent<Image>();
        image.color = new Color(0.05f, 0.07f, 0.12f, 1f);
        ApplySlicedMaybe(image);
        var vg = sideNav.GetComponent<VerticalLayoutGroup>() ?? sideNav.AddComponent<VerticalLayoutGroup>();
        vg.padding = new RectOffset(16, 16, 32, 32);
        vg.spacing = 16f;
        vg.childAlignment = TextAnchor.UpperCenter;
        vg.childControlWidth = true;
        vg.childControlHeight = false;
        vg.childForceExpandWidth = true;
        vg.childForceExpandHeight = false;
        StyleSideTab(eventTab, sideNav.transform, TabActive);
        StyleSideTab(baseTab, sideNav.transform, TabIdle);
        if (eventTab != null) eventTab.transform.SetSiblingIndex(0);
        if (baseTab != null) baseTab.transform.SetSiblingIndex(1);
        return sideNav;
    }

    static void StyleSideTab(GameObject tab, Transform parent, Color color) {
        if (tab == null) return;
        tab.transform.SetParent(parent, false);
        var rt = tab.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, 80f);
        var le = tab.GetComponent<LayoutElement>() ?? tab.AddComponent<LayoutElement>();
        le.minHeight = 80f;
        le.preferredHeight = 80f;
        le.flexibleWidth = 1f;
        SetImageColor(tab, color);
        ApplySlicedMaybe(tab.GetComponent<Image>());
        var btn = tab.GetComponent<Button>() ?? tab.AddComponent<Button>();
        btn.targetGraphic = tab.GetComponent<Image>();
        btn.transition = Selectable.Transition.None;
        var label = tab.GetComponentInChildren<TMP_Text>(true);
        if (label != null) {
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 28;
            StretchFull(label.rectTransform);
        }
        LayoutRebuilder.ForceRebuildLayoutImmediate(parent as RectTransform);
    }

    static void RebuildDetail(GameObject detail) {
        bool detailWasActive = detail.activeSelf;
        detail.SetActive(true);
        StretchFull(detail.GetComponent<RectTransform>());
        SetImageColor(detail, new Color(0.02f, 0.02f, 0.04f, 0.98f));
        Transform pageTitle = detail.transform.Find("PageTitle");
        if (pageTitle != null) Undo.DestroyObjectImmediate(pageTitle.gameObject);

        Transform nav = detail.transform.Find("NavigateBarPanel");
        Transform pages = detail.transform.Find("ContentPanel");
        Transform back = detail.transform.Find("Back");
        if (back == null && nav != null) back = nav.Find("Back");
        if (nav != null) {
            Stretch(nav.GetComponent<RectTransform>(), 0.10f, 0.06f, 0.30f, 0.94f);
            var vg = nav.GetComponent<VerticalLayoutGroup>();
            if (vg == null) vg = nav.gameObject.AddComponent<VerticalLayoutGroup>();
            vg.padding = new RectOffset(12, 12, 16, 16);
            vg.spacing = 12f;
            vg.childAlignment = TextAnchor.UpperCenter;
            vg.childControlWidth = true;
            vg.childControlHeight = false;
            vg.childForceExpandWidth = true;
            vg.childForceExpandHeight = false;
            if (back != null) {
                back.SetParent(nav, false);
                back.SetAsFirstSibling();
                var backRt = back.GetComponent<RectTransform>();
                backRt.anchorMin = new Vector2(0f, 1f);
                backRt.anchorMax = new Vector2(1f, 1f);
                backRt.pivot = new Vector2(0.5f, 1f);
                backRt.anchoredPosition = Vector2.zero;
                backRt.sizeDelta = new Vector2(0f, 64f);
            }
            for (int i = 0; i < nav.childCount; i++) {
                var childRt = nav.GetChild(i) as RectTransform;
                if (childRt == null) continue;
                childRt.anchorMin = new Vector2(0f, 1f);
                childRt.anchorMax = new Vector2(1f, 1f);
                childRt.pivot = new Vector2(0.5f, 1f);
                childRt.anchoredPosition = Vector2.zero;
                childRt.sizeDelta = new Vector2(0f, 64f);
                var le = childRt.GetComponent<LayoutElement>() ?? childRt.gameObject.AddComponent<LayoutElement>();
                le.minHeight = 64f;
                le.preferredHeight = 64f;
                le.flexibleWidth = 1f;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(nav.GetComponent<RectTransform>());
            Canvas.ForceUpdateCanvases();
        } else if (back != null) {
            var rt = back.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.10f, 1f);
            rt.anchorMax = new Vector2(0.30f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -24f);
            rt.sizeDelta = new Vector2(0f, 56f);
        }
        if (pages != null) {
            Stretch(pages.GetComponent<RectTransform>(), 0.30f, 0.06f, 0.80f, 0.94f);
            for (int i = 0; i < pages.childCount; i++) {
                StretchFull(pages.GetChild(i) as RectTransform);
            }
        }

        Transform popup = detail.transform.Find("RegisterPopup");
        if (popup != null) {
            StretchFull(popup as RectTransform);
            Transform box = popup.Find("Box");
            if (box != null) {
                var boxRt = box.GetComponent<RectTransform>();
                boxRt.anchorMin = new Vector2(0.5f, 0.5f);
                boxRt.anchorMax = new Vector2(0.5f, 0.5f);
                boxRt.pivot = new Vector2(0.5f, 0.5f);
                boxRt.anchoredPosition = Vector2.zero;
                if (boxRt.sizeDelta.x < 400f) boxRt.sizeDelta = new Vector2(560f, 480f);
            }
        }

        EnsureWaitingCount(detail);
        detail.SetActive(detailWasActive);
    }

    static void EnsureWaitingCount(GameObject detail) {
        var panel = detail.GetComponent<EventDetailPanel>();
        if (panel == null) return;
        SerializedObject so = new SerializedObject(panel);
        var readyProp = so.FindProperty("readyButton");
        Button ready = readyProp != null ? readyProp.objectReferenceValue as Button : null;
        if (ready == null) return;
        Transform parent = ready.transform.parent;
        Transform existing = parent.Find("WaitingCount");
        TMP_Text label;
        if (existing == null) {
            var go = new GameObject("WaitingCount", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(go, "WaitingCount");
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 8f);
            rt.sizeDelta = new Vector2(0f, 36f);
            label = go.GetComponent<TextMeshProUGUI>();
            label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            label.fontSize = 22;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.text = "等待玩家：0";
            label.raycastTarget = false;
        } else {
            label = existing.GetComponent<TMP_Text>();
        }
        so.FindProperty("waitingCountText").objectReferenceValue = label;
        var readyImage = ready.targetGraphic as Image;
        if (so.FindProperty("readyImage") != null) {
            so.FindProperty("readyImage").objectReferenceValue = readyImage;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void BindEventScripts(
        GameObject eventPanel, GameObject listRoot, GameObject detail,
        GameObject eventTab, GameObject baseTab, GameObject venueCreate, GameObject sideNav) {
        var lobby = eventPanel.GetComponent<EventLobbyPanel>();
        if (lobby != null) {
            SerializedObject so = new SerializedObject(lobby);
            if (eventTab != null) {
                so.FindProperty("eventTab").objectReferenceValue = eventTab.GetComponent<Button>();
                so.FindProperty("eventTabImage").objectReferenceValue = eventTab.GetComponent<Image>();
            }
            if (baseTab != null) {
                so.FindProperty("baseTab").objectReferenceValue = baseTab.GetComponent<Button>();
                so.FindProperty("baseTabImage").objectReferenceValue = baseTab.GetComponent<Image>();
            }
            if (listRoot != null) so.FindProperty("listRoot").objectReferenceValue = listRoot;
            if (detail != null) so.FindProperty("detailPanel").objectReferenceValue = detail.GetComponent<EventDetailPanel>();
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(lobby);
        }
        if (detail == null) return;
        var detailPanel = detail.GetComponent<EventDetailPanel>();
        if (detailPanel == null) return;
        SerializedObject dso = new SerializedObject(detailPanel);
        if (venueCreate != null) {
            dso.FindProperty("venueCreatePanel").objectReferenceValue = venueCreate.GetComponent<CreatePanel>();
        }
        Transform back = detail.transform.Find("Back");
        if (back == null) {
            Transform nav = detail.transform.Find("NavigateBarPanel");
            if (nav != null) back = nav.Find("Back");
        }
        if (back != null) {
            dso.FindProperty("backButton").objectReferenceValue = back.GetComponent<Button>();
        }
        dso.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(detailPanel);
        if (sideNav != null) EditorUtility.SetDirty(sideNav);
    }

    [MenuItem("Tools/Event/Restore non-event lobby panels")]
    public static void MenuRestoreForeign() {
        EditorUtility.DisplayDialog("Restore", RestoreForeignPanels(), "OK");
    }

    public static string RestoreForeignPanels() {
        var log = new StringBuilder();
        string destAbs = Path.Combine(Application.dataPath, "Scenes/_RestoreLobbySource.unity");
        if (!File.Exists(RestoreBackupAbs)) return "missing backup " + RestoreBackupAbs;
        File.Copy(RestoreBackupAbs, destAbs, true);
        AssetDatabase.ImportAsset(RestoreSceneAsset, ImportAssetOptions.ForceUpdate);
        Scene live = default;
        for (int i = 0; i < SceneManager.sceneCount; i++) {
            Scene s = SceneManager.GetSceneAt(i);
            if (s.path == "Assets/Scenes/MainScene.unity") live = s;
        }
        if (!live.IsValid()) live = SceneManager.GetActiveScene();
        Scene srcScene = EditorSceneManager.OpenScene(RestoreSceneAsset, OpenSceneMode.Additive);
        try {
            int panels = 0;
            int nodes = 0;
            foreach (string name in ForeignPanels) {
                GameObject src = FindInScene(srcScene, name);
                GameObject dst = FindInScene(live, name);
                if (src == null || dst == null) {
                    log.AppendLine("skip " + name + " src=" + (src != null) + " dst=" + (dst != null));
                    continue;
                }
                Undo.RegisterFullObjectHierarchyUndo(dst, "Restore " + name);
                int copied = RestoreTree(src.transform, dst.transform);
                nodes += copied;
                panels++;
                EditorUtility.SetDirty(dst);
                log.AppendLine("restored " + name + " nodes=" + copied);
            }
            log.AppendLine("panels=" + panels + " nodes=" + nodes);
        } finally {
            EditorSceneManager.CloseScene(srcScene, true);
        }
        if (live.IsValid()) {
            EditorSceneManager.MarkSceneDirty(live);
            EditorSceneManager.SaveScene(live);
        }
        AssetDatabase.DeleteAsset(RestoreSceneAsset);
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    static GameObject FindInScene(Scene scene, string name) {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (GameObject root in scene.GetRootGameObjects()) {
            GameObject hit = FindRecursive(root.transform, name);
            if (hit != null) return hit;
        }
        return null;
    }

    static int RestoreTree(Transform src, Transform dst) {
        int n = 1;
        CopyRt(src as RectTransform, dst as RectTransform);
        CopyGraphic(src, dst);
        if (src.childCount == dst.childCount) {
            for (int i = 0; i < src.childCount; i++) {
                n += RestoreTree(src.GetChild(i), dst.GetChild(i));
            }
            return n;
        }
        for (int i = 0; i < src.childCount; i++) {
            Transform srcChild = src.GetChild(i);
            Transform dstChild = dst.Find(srcChild.name);
            if (dstChild == null) {
                for (int j = 0; j < dst.childCount; j++) {
                    if (dst.GetChild(j).name == srcChild.name) {
                        dstChild = dst.GetChild(j);
                        break;
                    }
                }
            }
            if (dstChild == null) continue;
            n += RestoreTree(srcChild, dstChild);
        }
        return n;
    }

    static void CopyRt(RectTransform src, RectTransform dst) {
        if (src == null || dst == null) return;
        dst.anchorMin = src.anchorMin;
        dst.anchorMax = src.anchorMax;
        dst.pivot = src.pivot;
        dst.anchoredPosition3D = src.anchoredPosition3D;
        dst.sizeDelta = src.sizeDelta;
        dst.localRotation = src.localRotation;
        dst.localScale = src.localScale;
    }

    static void CopyGraphic(Transform src, Transform dst) {
        var srcTmp = src.GetComponent<TextMeshProUGUI>();
        var dstTmp = dst.GetComponent<TextMeshProUGUI>();
        if (srcTmp != null && dstTmp != null) {
            dstTmp.alignment = srcTmp.alignment;
            dstTmp.fontSize = srcTmp.fontSize;
            dstTmp.color = srcTmp.color;
            dstTmp.overflowMode = srcTmp.overflowMode;
            dstTmp.enableWordWrapping = srcTmp.enableWordWrapping;
        }
        var srcImg = src.GetComponent<Image>();
        var dstImg = dst.GetComponent<Image>();
        if (srcImg != null && dstImg != null) {
            dstImg.color = srcImg.color;
            dstImg.sprite = srcImg.sprite;
            dstImg.type = srcImg.type;
            dstImg.pixelsPerUnitMultiplier = srcImg.pixelsPerUnitMultiplier;
        }
    }

    static void ApplySlicedMaybe(Image image) {
        if (image == null) return;
        ApplySliced(image);
    }

    static void StretchNamedChild(Transform parent, string name) {
        Transform child = parent.Find(name);
        if (child == null) {
            for (int i = 0; i < parent.childCount; i++) {
                if (parent.GetChild(i).name == name) {
                    child = parent.GetChild(i);
                    break;
                }
            }
        }
        if (child != null) StretchFull(child as RectTransform);
    }

    static void SetImageColor(GameObject go, Color color) {
        if (go == null) return;
        var image = go.GetComponent<Image>();
        if (image != null) image.color = color;
    }

    static void StretchFull(RectTransform rt) {
        if (rt == null) return;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
    }

    static void Stretch(RectTransform rt, float xMin, float yMin, float xMax, float yMax) {
        if (rt == null) return;
        rt.anchorMin = new Vector2(xMin, yMin);
        rt.anchorMax = new Vector2(xMax, yMax);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    static GameObject Find(string name) {
        for (int s = 0; s < SceneManager.sceneCount; s++) {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            foreach (GameObject root in scene.GetRootGameObjects()) {
                GameObject hit = FindRecursive(root.transform, name);
                if (hit != null) return hit;
            }
        }
        return GameObject.Find(name);
    }

    static GameObject FindChild(Transform parent, string name) {
        Transform t = parent.Find(name);
        if (t != null) return t.gameObject;
        for (int i = 0; i < parent.childCount; i++) {
            if (parent.GetChild(i).name == name) return parent.GetChild(i).gameObject;
        }
        return null;
    }

    static GameObject FindRecursive(Transform root, string name) {
        if (root.name == name) return root.gameObject;
        for (int i = 0; i < root.childCount; i++) {
            GameObject hit = FindRecursive(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }
}
