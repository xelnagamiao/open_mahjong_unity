#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 在已打开的 MainScene 上给通知/好友顶栏和申请页签右上角加未读圆点。
/// 只新增子物体，不改父按钮锚点或宽高。不读写 YAML。
/// </summary>
[InitializeOnLoad]
public static class UnreadBadgeScenePainter {
    private const string BadgeName = "UnreadBadge";
    private const string CountName = "Count";
    private const string MenuPath = "Tools/UnreadBadge/Paint In Open Scene";
    private static readonly Color DefaultBadgeColor = new Color(0.91f, 0.22f, 0.22f, 1f);
    private static int _compileWait;
    // ActivityItem 圆点：30x30、(-15,-15)、ignoreLayout，避免被 VerticalLayoutGroup 挤走。

    static UnreadBadgeScenePainter() {
        EditorApplication.delayCall += TryRunRequest;
    }

    [MenuItem(MenuPath)]
    public static void PaintFromMenu() {
        Paint(writeResult: false);
    }

    [MenuItem("Tools/UnreadBadge/Paint ActivityItem Prefab")]
    public static void PaintActivityPrefabFromMenu() {
        PaintActivityItemPrefab();
    }

    private static void TryRunRequest() {
        string activityMarker = ActivityRequestPath();
        if (File.Exists(activityMarker)) {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) {
                EditorApplication.delayCall += TryRunRequest;
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode) {
                WriteResult("playing; exit Play Mode then click Tools/UnreadBadge/Paint ActivityItem Prefab");
                return;
            }
            try {
                string msg = PaintActivityItemPrefab();
                if (msg != null && msg.Contains("not compiled")) {
                    EditorApplication.delayCall += TryRunRequest;
                    return;
                }
                File.Delete(activityMarker);
                WriteResult(msg);
            } catch (System.Exception e) {
                WriteResult("error: " + e);
                Debug.LogError("[UnreadBadgeScenePainter] " + e);
            }
            return;
        }
        string marker = RequestPath();
        if (!File.Exists(marker)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) {
            _compileWait++;
            if (_compileWait > 120) {
                File.Delete(marker);
                WriteResult("timeout waiting for compile");
                return;
            }
            EditorApplication.delayCall += TryRunRequest;
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode) {
            WriteResult("playing; exit Play Mode then click Tools/UnreadBadge/Paint In Open Scene");
            return;
        }
        try {
            File.Delete(marker);
            Paint(writeResult: true);
        } catch (System.Exception e) {
            WriteResult("error: " + e);
            Debug.LogError("[UnreadBadgeScenePainter] " + e);
        }
    }

    public static void Paint(bool writeResult) {
        if (EditorApplication.isPlayingOrWillChangePlaymode) {
            Finish(writeResult, "playing; exit Play Mode and run Tools/UnreadBadge/Paint In Open Scene");
            return;
        }

        HeaderPanel header = FindInOpenScene<HeaderPanel>();
        FriendPanel friendPanel = FindInOpenScene<FriendPanel>();
        if (header == null || friendPanel == null) {
            Finish(writeResult, "HeaderPanel or FriendPanel not found in open scene");
            return;
        }

        var headerSo = new SerializedObject(header);
        var notice = headerSo.FindProperty("noticeButton").objectReferenceValue as HeaderButton;
        var friend = headerSo.FindProperty("friendButton").objectReferenceValue as HeaderButton;
        if (notice == null || friend == null) {
            Finish(writeResult, "noticeButton/friendButton missing on HeaderPanel");
            return;
        }

        var friendSo = new SerializedObject(friendPanel);
        var requestsTab = friendSo.FindProperty("requestsTabButton").objectReferenceValue as Button;
        if (requestsTab == null) {
            Finish(writeResult, "requestsTabButton missing on FriendPanel");
            return;
        }

        TMP_FontAsset font = FindHeaderFont(notice.transform);
        PaintHeaderButton(notice, font);
        PaintHeaderButton(friend, font);
        PaintFriendRequestTab(friendPanel, friendSo, requestsTab.transform as RectTransform, font);

        EditorSceneManager.MarkSceneDirty(header.gameObject.scene);
        EditorSceneManager.MarkSceneDirty(friendPanel.gameObject.scene);
        EditorSceneManager.SaveOpenScenes();
        string prefabMsg = PaintActivityItemPrefab();
        Finish(writeResult, string.IsNullOrEmpty(prefabMsg) ? "ok" : "ok; " + prefabMsg);
    }

    public static string PaintActivityItemPrefab() {
        const string prefabPath = "Assets/Prefabs/Notice/ActivityItem.prefab";
        GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
        if (contents == null) return "ActivityItem prefab missing";
        try {
            ActivityItem item = contents.GetComponent<ActivityItem>()
                ?? contents.GetComponentInChildren<ActivityItem>(true);
            if (item == null) return "ActivityItem component missing";
            var so = new SerializedObject(item);
            if (so.FindProperty("badgeRoot") == null) {
                return "ActivityItem.badgeRoot not compiled yet";
            }
            TMP_FontAsset font = FindHeaderFont(contents.transform);
            var created = CreateOrGetBadge(
                contents.transform as RectTransform,
                font,
                DefaultBadgeColor,
                new Vector2(30f, 30f),
                new Vector2(-15f, -15f),
                hideCount: true
            );
            so.FindProperty("badgeRoot").objectReferenceValue = created.root;
            so.FindProperty("badgeImage").objectReferenceValue = created.image;
            so.FindProperty("badgeColor").colorValue = DefaultBadgeColor;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            return "ActivityItem prefab painted";
        } finally {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static void PaintHeaderButton(HeaderButton button, TMP_FontAsset font) {
        var so = new SerializedObject(button);
        if (so.FindProperty("badgeImage") == null) {
            EditorApplication.delayCall += TryRunRequest;
            throw new System.InvalidOperationException("HeaderButton.badgeImage not compiled yet");
        }
        var created = CreateOrGetBadge(button.transform as RectTransform, font, DefaultBadgeColor, default, default, false);
        so.FindProperty("badgeRoot").objectReferenceValue = created.root;
        so.FindProperty("badgeImage").objectReferenceValue = created.image;
        so.FindProperty("badgeText").objectReferenceValue = created.text;
        so.FindProperty("badgeColor").colorValue = DefaultBadgeColor;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(button);
    }

    private static void PaintFriendRequestTab(
        FriendPanel panel,
        SerializedObject friendSo,
        RectTransform tab,
        TMP_FontAsset font
    ) {
        if (tab == null) return;
        if (friendSo.FindProperty("requestTabBadgeImage") == null) {
            throw new System.InvalidOperationException("FriendPanel.requestTabBadgeImage not compiled yet");
        }
        var created = CreateOrGetBadge(tab, font, DefaultBadgeColor, default, default, false);
        friendSo.FindProperty("requestTabBadge").objectReferenceValue = created.root;
        friendSo.FindProperty("requestTabBadgeImage").objectReferenceValue = created.image;
        friendSo.FindProperty("requestTabBadgeText").objectReferenceValue = created.text;
        friendSo.FindProperty("requestTabBadgeColor").colorValue = DefaultBadgeColor;
        friendSo.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(panel);
    }

    private static (GameObject root, Image image, TMP_Text text) CreateOrGetBadge(
        RectTransform parent,
        TMP_FontAsset font,
        Color color,
        Vector2 size,
        Vector2 anchoredPos,
        bool hideCount
    ) {
        Transform existing = parent.Find(BadgeName);
        GameObject root;
        if (existing != null) {
            root = existing.gameObject;
        } else {
            if (size == default) size = new Vector2(30f, 30f);
            if (anchoredPos == default) anchoredPos = new Vector2(-15f, -15f);
            root = new GameObject(BadgeName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Undo.RegisterCreatedObjectUndo(root, "Create UnreadBadge");
            root.layer = parent.gameObject.layer;
            var rt = root.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
        }

        var image = root.GetComponent<Image>();
        if (image == null) image = Undo.AddComponent<Image>(root);
        Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        if (knob != null) image.sprite = knob;
        image.color = color;
        image.raycastTarget = false;
        image.preserveAspect = true;

        var layout = root.GetComponent<LayoutElement>();
        if (layout == null) layout = Undo.AddComponent<LayoutElement>(root);
        layout.ignoreLayout = true;
        root.transform.SetAsLastSibling();

        TMP_Text text = null;
        Transform countTf = root.transform.Find(CountName);
        if (countTf != null) text = countTf.GetComponent<TMP_Text>();
        if (text == null) text = root.GetComponentInChildren<TMP_Text>(true);
        if (text == null) {
            var countGo = new GameObject(CountName, typeof(RectTransform), typeof(CanvasRenderer));
            Undo.RegisterCreatedObjectUndo(countGo, "Create UnreadBadge Count");
            countGo.layer = root.layer;
            var trt = countGo.GetComponent<RectTransform>();
            trt.SetParent(root.transform, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            text = countGo.AddComponent<TextMeshProUGUI>();
        }
        if (font != null) text.font = font;
        text.text = hideCount ? "" : "1";
        text.fontSize = 13f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.gameObject.SetActive(!hideCount);
        root.SetActive(true);
        return (root, image, text);
    }

    private static TMP_FontAsset FindHeaderFont(Transform headerButton) {
        TMP_Text[] texts = headerButton.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++) {
            if (texts[i] != null && texts[i].font != null && texts[i].transform.name != CountName) {
                return texts[i].font;
            }
        }
        return null;
    }

    private static T FindInOpenScene<T>() where T : Component {
        T[] all = Resources.FindObjectsOfTypeAll<T>();
        for (int i = 0; i < all.Length; i++) {
            T item = all[i];
            if (item == null) continue;
            Scene scene = item.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded) continue;
            return item;
        }
        return null;
    }

    private static string WorkspaceRoot() {
        return Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
    }

    private static string RequestPath() {
        return Path.Combine(WorkspaceRoot(), ".om_workspace", "tmp", "unread_badge_paint.request");
    }

    private static string ActivityRequestPath() {
        return Path.Combine(WorkspaceRoot(), ".om_workspace", "tmp", "unread_badge_activity.request");
    }

    private static string ResultPath() {
        return Path.Combine(WorkspaceRoot(), ".om_workspace", "tmp", "unread_badge_paint.result");
    }

    private static void Finish(bool writeResult, string message) {
        Debug.Log("[UnreadBadgeScenePainter] " + message);
        if (writeResult) WriteResult(message);
    }

    private static void WriteResult(string message) {
        string dir = Path.GetDirectoryName(ResultPath());
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(ResultPath(), message);
    }
}
#endif
