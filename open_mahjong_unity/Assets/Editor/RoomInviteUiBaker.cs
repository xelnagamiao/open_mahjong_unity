#if UNITY_EDITOR
using System;
using System.Linq;
using Salasasa.Home;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Authors the invitation UI into MainScene, following the existing modal system.</summary>
public static class RoomInviteUiBaker {
    static TMP_FontAsset font;
    static readonly Color Surface = new Color(.14f, .15f, .17f);
    static readonly Color Field = new Color(.09f, .10f, .12f);
    static readonly Color Ink = new Color(.94f, .94f, .94f);
    static readonly Color Muted = new Color(.72f, .74f, .77f);
    static readonly Color Accent = new Color(.77f, .64f, .39f);

    [MenuItem("Tools/Mahjong/Main Scene/Bake Room Invitation UI")]
    public static void BakeActive() => Bake(SceneManager.GetActiveScene());
    public static void Bake(Scene scene) {
        if (Application.isPlaying) throw new InvalidOperationException("请在编辑模式烘焙邀请界面。");
        var roots = scene.GetRootGameObjects();
        var room = roots.SelectMany(r => r.GetComponentsInChildren<RoomPanel>(true)).Single();
        var overlay = roots.SelectMany(r => r.GetComponentsInChildren<Canvas>(true)).Single(c => c.name == "OverlayCanvas").transform;
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Salasasa/Fonts/CompleteCityBody.asset")
            ?? room.GetComponentsInChildren<TMP_Text>(true).First(t => t.font).font;
        var root = Rect("RoomInvitePanel", overlay); Fill(root);
        // Remove the former recipient preview and its password/refresh controls.
        for (int i = root.childCount - 1; i >= 0; i--) Undo.DestroyObjectImmediate(root.GetChild(i).gameObject);
        UiCanvasLayers.Apply(Get<Canvas>(root), UiCanvasLayers.Modal, UiCanvasLayers.ModalOrder + 10);
        Get<UnityEngine.UI.GraphicRaycaster>(root);
        var panel = Get<RoomInvitePanel>(root); panel.fade = Get<BakedModalFade>(root);
        var shade = Box("DimBackground", root, new Color(0, 0, 0, .5f)); Fill(shade.rectTransform); shade.raycastTarget = true;
        var fitting = Rect("Fit", root); fitting.anchorMin = fitting.anchorMax = fitting.pivot = Vector2.one * .5f; fitting.sizeDelta = Vector2.zero;
        var surface = Box("Window", fitting, Surface); var frame = surface.rectTransform;
        frame.anchorMin = frame.anchorMax = frame.pivot = Vector2.one * .5f; frame.sizeDelta = new Vector2(900, 330); surface.raycastTarget = true;
        var fit = Get<ViewportPanelFit>(fitting); fit.panel = frame; fit.margin = 20;
        var header = Box("Header", frame, new Color(.19f, .20f, .22f)); Top(header.rectTransform, 0, 0, 900, 64);
        panel.title = Text("Title", header.transform, "邀请好友加入房间", 27, Ink); Top(panel.title.rectTransform, 28, 0, 650, 64);
        panel.close = Button("Close", header.transform, "×", Field, Ink); Top((RectTransform)panel.close.transform, 844, 12, 40, 40);
        panel.shareInput = Input("InvitationText", frame, "", 22, false); Top((RectTransform)panel.shareInput.transform, 30, 88, 840, 122);
        panel.shareInput.readOnly = true; panel.shareInput.lineType = TMP_InputField.LineType.MultiLineNewline;
        panel.shareInput.textComponent.textWrappingMode = TextWrappingModes.Normal;
        panel.hint = Text("Hint", frame, "房间解散后，邀请链接失效。", 17, Muted); Top(panel.hint.rectTransform, 30, 218, 840, 28);
        panel.status = Text("Status", frame, "", 17, Accent); Top(panel.status.rectTransform, 30, 246, 840, 26);
        panel.primary = Button("Primary", frame, "复制邀请", Accent, new Color(.10f, .11f, .12f)); Top((RectTransform)panel.primary.transform, 350, 276, 200, 42);
        panel.primaryLabel = panel.primary.GetComponentInChildren<TMP_Text>();

        var serialized = new SerializedObject(room);
        var roomId = (TMP_Text)serialized.FindProperty("roomIdText").objectReferenceValue;
        var entry = Button("InviteFriends", roomId.transform.parent, "邀请好友", new Color(.24f, .25f, .27f), Ink);
        var label = roomId.rectTransform; var entryRect = (RectTransform)entry.transform;
        entryRect.anchorMin = entryRect.anchorMax = label.anchorMin; entryRect.pivot = new Vector2(0, .5f);
        // Keep the entry within the room-number area, clear of the neighbouring room name.
        entryRect.anchoredPosition = label.anchoredPosition + new Vector2(label.sizeDelta.x * (1 - label.pivot.x) - 168, 0);
        entryRect.sizeDelta = new Vector2(150, 46);
        serialized.FindProperty("inviteButton").objectReferenceValue = entry;
        serialized.FindProperty("invitePanel").objectReferenceValue = panel;
        serialized.ApplyModifiedProperties();
        root.gameObject.SetActive(false);
        foreach (var component in root.GetComponentsInChildren<Component>(true)) if (component) EditorUtility.SetDirty(component);
        EditorUtility.SetDirty(room); EditorSceneManager.MarkSceneDirty(scene);
        var picker = roots.SelectMany(r => r.GetComponentsInChildren<FriendInvitePanel>(true)).FirstOrDefault();
        PartyUiBaker.BakeRoomInvitation(panel, picker);
        Validate(scene);
    }

    public static void Validate(Scene scene) {
        var panel = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RoomInvitePanel>(true)).Single();
        if (!panel.fade || !panel.shareInput || !panel.primary || !panel.close)
            throw new InvalidOperationException("邀请界面引用缺失。");
        if (!panel.shareInput.readOnly || panel.GetComponentsInChildren<TMP_InputField>(true).Length != 1)
            throw new InvalidOperationException("分享界面应只有只读邀请文本。");
        if (panel.GetComponent<Canvas>().sortingLayerName != UiCanvasLayers.Modal)
            throw new InvalidOperationException("邀请弹窗层级不正确。");
        foreach (var text in panel.GetComponentsInChildren<TMP_Text>(true))
            if ((text.fontStyle & FontStyles.Bold) != 0 || text.fontWeight != FontWeight.Regular)
                throw new InvalidOperationException("邀请界面应使用正常字重。");
    }
    static T Get<T>(Transform t) where T : Component {
        var component = t.GetComponent<T>();
        return component ? component : t.gameObject.AddComponent<T>();
    }
    static RectTransform Rect(string name, Transform parent) {
        var found = parent.Find(name); if (found) return (RectTransform)found;
        var node = new GameObject(name, typeof(RectTransform)); Undo.RegisterCreatedObjectUndo(node, "Create room invitation UI");
        node.layer = parent.gameObject.layer; var rect = (RectTransform)node.transform; rect.SetParent(parent, false); return rect;
    }
    static UnityEngine.UI.Image Box(string name, Transform parent, Color color) {
        var rect = Rect(name, parent); var image = Get<UnityEngine.UI.Image>(rect); image.color = color; image.raycastTarget = false; return image;
    }
    static TMP_Text Text(string name, Transform parent, string value, float size, Color color, bool localize = true) {
        var rect = Rect(name, parent); var text = Get<TextMeshProUGUI>(rect);
        text.font = font; text.text = value; text.fontSize = size; text.color = color;
        text.fontStyle = FontStyles.Normal; text.fontWeight = FontWeight.Regular; text.richText = false; text.raycastTarget = false;
        text.alignment = TextAlignmentOptions.MidlineLeft; text.textWrappingMode = TextWrappingModes.NoWrap;
        if (localize) {
            var language = Get<LocalizedPlatformLabel>(rect); language.key = value; language.authoredSize = size;
            language.minimumSize = 14; language.typographyBaked = true;
        }
        var binding = Get<PlatformFontBinding>(rect); binding.authoredFont = font; binding.authoredMaterial = font.material;
        binding.authoredStyle = FontStyles.Normal; binding.authoredWeight = FontWeight.Regular; binding.styleCaptured = true;
        return text;
    }
    static UnityEngine.UI.Button Button(string name, Transform parent, string text, Color surface, Color ink) {
        var image = Box(name, parent, surface); image.raycastTarget = true;
        var button = Get<UnityEngine.UI.Button>(image.transform); button.targetGraphic = image;
        var label = Text("Label", button.transform, text, 21, ink); Fill(label.rectTransform); label.alignment = TextAlignmentOptions.Center;
        return button;
    }
    static TMP_InputField Input(string name, Transform parent, string hint, float size, bool password) {
        var image = Box(name, parent, Field); image.raycastTarget = true;
        var input = Get<TMP_InputField>(image.transform); input.targetGraphic = image;
        var viewport = Rect("TextArea", input.transform); Stretch(viewport, 14, 8, 14, 8); Get<UnityEngine.UI.RectMask2D>(viewport);
        var text = Text("Text", viewport, "", size, Ink, false); Fill(text.rectTransform);
        var placeholder = Text("Placeholder", viewport, hint, size, Muted); Fill(placeholder.rectTransform);
        input.textViewport = viewport; input.textComponent = (TextMeshProUGUI)text; input.placeholder = placeholder;
        input.contentType = password ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
        input.characterLimit = password ? 256 : 4096; input.customCaretColor = true; input.caretColor = Ink;
        input.selectionColor = new Color(.77f, .64f, .39f, .35f);
        Get<WebGLSupport.WebGLInput>(input.transform);
        return input;
    }
    static void Fill(RectTransform rect) => Stretch(rect, 0, 0, 0, 0);
    static void Stretch(RectTransform rect, float left, float top, float right, float bottom) {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(-right, -top);
    }
    static void Top(RectTransform rect, float x, float y, float width, float height) {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
    }
}
#endif
