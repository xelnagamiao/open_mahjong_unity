using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Authors the spectator UI with the existing lobby fonts and reusable controls.</summary>
public static class SpectatorUiBaker {
    public const string BasePath = "Assets/Prefabs/Room/SpectatoItem.prefab";
    public const string HeaderPath = "Assets/Prefabs/Room/SpectatorTierHeader.prefab";
    public const string SoftPath = "Assets/Prefabs/Room/SpectatorSoftCard.prefab";

    [MenuItem("Tools/Mahjong/Spectator/Bake Room Cards")]
    public static void BakeCards() {
        var root = PrefabUtility.LoadPrefabContents(BasePath);
        try {
            var binder = root.GetComponent<SpectatorPrefab>();
            var serialized = new SerializedObject(binder);
            var rect = (RectTransform)root.transform;
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 90.8435f);
            var element = root.GetComponent<LayoutElement>();
            element.minHeight = element.preferredHeight = 90.8435f;
            element.flexibleHeight = 0;
            root.GetComponent<Image>().raycastTarget = true;
            var header = root.transform.Find("HeaderPanel").GetComponent<Image>();
            Stretch(header.rectTransform, new Vector2(0, .5f), Vector2.one, Vector2.zero, Vector2.zero);
            var title = (RectTransform)root.transform.Find("GameObject");
            Stretch(title, new Vector2(0, .5f), Vector2.one, Vector2.zero, new Vector2(-152, 0));
            var players = (RectTransform)root.transform.Find("GameObject (1)");
            Stretch(players, Vector2.zero, new Vector2(1, .5f), Vector2.zero, new Vector2(-152, 0));
            var rule = (TextMeshProUGUI)serialized.FindProperty("RuleText").objectReferenceValue;
            var id = (TextMeshProUGUI)serialized.FindProperty("GamestateIdText").objectReferenceValue;
            Stretch(rule.rectTransform, Vector2.zero, new Vector2(.49f, 1), new Vector2(16, 0), new Vector2(-10, 0));
            TextStyle(rule, 25, TextAlignmentOptions.MidlineLeft);
            Stretch(id.rectTransform, new Vector2(.49f, 0), Vector2.one, new Vector2(8, 0), new Vector2(-12, 0));
            TextStyle(id, 23, TextAlignmentOptions.MidlineLeft);
            // Remove only the extra nodes created by the earlier design proposals.
            foreach (var path in new[] { "GameObject/RoomBadge", "GameObject (1)/BlueTeam", "GameObject (1)/RedTeam",
                "GameObject (1)/BlueTeamBackground", "GameObject (1)/RedTeamBackground" }) {
                var extra = root.transform.Find(path);
                if (extra != null) UnityEngine.Object.DestroyImmediate(extra.gameObject);
            }
            var rail = ImageChild(root.transform, "TierAccent", Color.white);
            Stretch(rail.rectTransform, Vector2.zero, new Vector2(0, 1), Vector2.zero, new Vector2(5, 0));
            rail.gameObject.SetActive(false);
            Assign(serialized, "cardBackground", root.GetComponent<Image>());
            Assign(serialized, "headerBackground", header);
            Assign(serialized, "playersBackground", players.GetComponent<Image>());
            Assign(serialized, "accentRail", rail);
            serialized.FindProperty("colorStyle").enumValueIndex = 0;
            int playerIndex = 0;
            foreach (var text in new[] { "Player1NameText", "Player2NameText", "Player3NameText", "Player4NameText" }) {
                var player = (TextMeshProUGUI)serialized.FindProperty(text).objectReferenceValue;
                Stretch(player.rectTransform, new Vector2(playerIndex * .25f, 0), new Vector2((playerIndex + 1) * .25f, 1), new Vector2(16, 0), new Vector2(-14, 0));
                TextStyle(player, 24, TextAlignmentOptions.MidlineLeft);
                player.richText = false;
                playerIndex++;
            }
            var teamBackgrounds = serialized.FindProperty("playerTeamBackgrounds");
            teamBackgrounds.arraySize = 4;
            for (int player = 0; player < 4; player++) {
                var background = ImageChild(players, "PlayerTeamColor" + player, Color.clear);
                Stretch(background.rectTransform, new Vector2(player * .25f, 0), new Vector2((player + 1) * .25f, 1),
                    new Vector2(1, 1), new Vector2(-1, -1));
                background.transform.SetAsFirstSibling();
                teamBackgrounds.GetArrayElementAtIndex(player).objectReferenceValue = background;
                background.gameObject.SetActive(false);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, BasePath);
        } finally { PrefabUtility.UnloadPrefabContents(root); }
        BakeVariant(HeaderPath, SpectatorPrefab.ColorStyle.TierHeader);
        BakeVariant(SoftPath, SpectatorPrefab.ColorStyle.SoftCard);
    }

    private static void BakeVariant(string path, SpectatorPrefab.ColorStyle style) {
        bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
        var root = existing ? PrefabUtility.LoadPrefabContents(path)
            : (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BasePath));
        try {
            var serialized = new SerializedObject(root.GetComponent<SpectatorPrefab>());
            serialized.FindProperty("colorStyle").enumValueIndex = (int)style;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
        } finally {
            if (existing) PrefabUtility.UnloadPrefabContents(root);
            else UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [MenuItem("Tools/Mahjong/Spectator/Bake Filters And Scrolling")]
    public static void BakeActiveScene() {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出播放模式。");
        var scene = SceneManager.GetActiveScene();
        var panel = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SpectatorPanel>(true)).Single();
        var sourcePanel = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<RoomPanel>(true)).Single();
        var dropdownSource = (TMP_Dropdown)new SerializedObject(sourcePanel).FindProperty("botSpeedDropdown").objectReferenceValue;
        Undo.RegisterFullObjectHierarchyUndo(panel.gameObject, "Spectator filters and scrolling");
        BakePanel(panel, dropdownSource);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    public static void BakePanel(SpectatorPanel panel, TMP_Dropdown dropdownSource) {
        var serialized = new SerializedObject(panel);
        var scroll = (ScrollRect)serialized.FindProperty("spectatorScrollRect").objectReferenceValue;
        var container = (RectTransform)scroll.transform.parent;
        var bar = RectChild(container, "FilterBar");
        Stretch(bar, new Vector2(0, 1), Vector2.one, new Vector2(0, -58), new Vector2(0, 0));
        var room = Dropdown(bar, "RoomFilter", dropdownSource, 0, .326f, new[] { "全部房间", "自建房", "排位房" });
        var tier = Dropdown(bar, "TierFilter", dropdownSource, .337f, .663f, new[] { "全部场次", "MCRPL", "高级场", "中级场", "初级场", "Elo 匹配" });
        var format = Dropdown(bar, "FormatFilter", dropdownSource, .674f, 1, new[] { "全部赛制", "个人对局", "2v2 双打" });
        foreach (var path in new[] { "FilterBar/Refresh", "ListSummary", "EmptyHint" }) {
            var extra = container.Find(path);
            if (extra != null) UnityEngine.Object.DestroyImmediate(extra.gameObject);
        }
        Stretch((RectTransform)scroll.transform, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, -68));
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 90;
        var layout = scroll.content.GetComponent<VerticalLayoutGroup>();
        layout.childControlHeight = true; layout.childControlWidth = true;
        layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
        layout.spacing = 8;
        var fit = scroll.content.GetComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content.anchorMin = new Vector2(0, 1); scroll.content.anchorMax = Vector2.one;
        scroll.content.pivot = new Vector2(.5f, 1); scroll.content.anchoredPosition = Vector2.zero;
        scroll.viewport.GetComponent<Image>().raycastTarget = true;
        var scrollbar = scroll.verticalScrollbar;
        if (scrollbar != null) {
            var rect = (RectTransform)scrollbar.transform;
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 16);
            scrollbar.GetComponent<Image>().raycastTarget = true;
            Stretch(scroll.viewport, Vector2.zero, Vector2.one, new Vector2(7, 7), new Vector2(-29, -7));
        }
        Assign(serialized, "roomFilterDropdown", room);
        Assign(serialized, "tierFilterDropdown", tier);
        Assign(serialized, "formatFilterDropdown", format);
        Assign(serialized, "SpectatorPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(HeaderPath).GetComponent<SpectatorPrefab>());
        serialized.ApplyModifiedPropertiesWithoutUndo();
        foreach (var component in panel.GetComponentsInChildren<Component>(true)) if (component) EditorUtility.SetDirty(component);
        Validate(panel);
    }

    public static void Validate(SpectatorPanel panel) {
        var serialized = new SerializedObject(panel);
        foreach (string field in new[] { "SpectatorPrefab", "contentTransform", "spectatorScrollRect",
            "roomFilterDropdown", "tierFilterDropdown", "formatFilterDropdown" })
            if (!serialized.FindProperty(field).objectReferenceValue) throw new InvalidOperationException("观战面板引用缺失：" + field);
        var scroll = (ScrollRect)serialized.FindProperty("spectatorScrollRect").objectReferenceValue;
        if (!scroll.vertical || scroll.horizontal || scroll.content.GetComponent<ContentSizeFitter>().verticalFit != ContentSizeFitter.FitMode.PreferredSize)
            throw new InvalidOperationException("观战滚动区域配置错误");
    }

    private static TMP_Dropdown Dropdown(Transform parent, string name, TMP_Dropdown source, float left, float right, string[] options) {
        var dropdown = parent.Find(name)?.GetComponent<TMP_Dropdown>();
        if (dropdown == null) { dropdown = UnityEngine.Object.Instantiate(source, parent); dropdown.name = name; }
        dropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
        dropdown.MultiSelect = false;
        dropdown.ClearOptions(); dropdown.AddOptions(options.ToList()); dropdown.SetValueWithoutNotify(0);
        dropdown.interactable = true; dropdown.gameObject.SetActive(true);
        Stretch((RectTransform)dropdown.transform, new Vector2(left, 0), new Vector2(right, 1), Vector2.zero, Vector2.zero);
        dropdown.captionText.fontSize = 26;
        dropdown.template.pivot = new Vector2(.5f, 1);
        dropdown.template.anchorMin = Vector2.zero; dropdown.template.anchorMax = new Vector2(1, 0);
        dropdown.template.anchoredPosition = new Vector2(0, -4);
        dropdown.template.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Min(300, options.Length * 50 + 12));
        var toggle = dropdown.template.GetComponentInChildren<Toggle>(true);
        if (toggle != null) { toggle.toggleTransition = Toggle.ToggleTransition.None; toggle.SetIsOnWithoutNotify(false); }
        dropdown.template.gameObject.SetActive(false);
        return dropdown;
    }
    private static RectTransform RectChild(Transform parent, string name) {
        var existing = parent.Find(name) as RectTransform;
        if (existing != null) return existing;
        var go = new GameObject(name, typeof(RectTransform)); go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false); return (RectTransform)go.transform;
    }
    private static Image ImageChild(Transform parent, string name, Color color) {
        var rect = RectChild(parent, name); var image = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
        image.color = color; image.raycastTarget = false; return image;
    }
    private static void TextStyle(TextMeshProUGUI label, float size, TextAlignmentOptions alignment) {
        label.fontSize = size; label.enableAutoSizing = false; label.textWrappingMode = TextWrappingModes.NoWrap;
        label.margin = Vector4.zero;
        label.overflowMode = TextOverflowModes.Ellipsis; label.raycastTarget = false; label.alignment = alignment;
    }
    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax) {
        rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax;
    }
    private static void Assign(SerializedObject serialized, string name, UnityEngine.Object value)
        => serialized.FindProperty(name).objectReferenceValue = value;
}
