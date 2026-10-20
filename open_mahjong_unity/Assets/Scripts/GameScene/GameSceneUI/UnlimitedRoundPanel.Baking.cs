#if UNITY_EDITOR
using TMPro;
using UnityEngine;
using UnityEditor;

public partial class UnlimitedRoundPanel {
    [ContextMenu("Bake Unlimited Round Controls")]
    public void BakeControls() {
        var menu = GetComponent<ExitButtonManager>();
        if (!menu) throw new System.InvalidOperationException("请将组件挂在 ExitButtonManager 上");
        var serializedMenu = new SerializedObject(menu);
        var template = (UnityEngine.UI.Button)serializedMenu.FindProperty("voteEndButton").objectReferenceValue;
        var button = (UnityEngine.UI.Button)serializedMenu.FindProperty("unlimitedEndButton").objectReferenceValue;
        if (!button) {
            var go = new GameObject("UnlimitedEndButton", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button), typeof(UnityEngine.UI.LayoutElement));
            go.transform.SetParent(menu.transform, false);
            button = go.GetComponent<UnityEngine.UI.Button>();
        }
        button.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
        var rect = (RectTransform)button.transform;
        rect.sizeDelta = new Vector2(160, 60);
        var layout = button.GetComponent<UnityEngine.UI.LayoutElement>();
        layout.minWidth = layout.preferredWidth = 160;
        layout.minHeight = layout.preferredHeight = 60;
        var buttonBackground = button.GetComponent<UnityEngine.UI.Image>();
        buttonBackground.color = new Color32(47, 61, 106, 245);
        buttonBackground.raycastTarget = true;
        button.targetGraphic = buttonBackground;
        var colors = button.colors;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
        colors.pressedColor = new Color(.75f, .75f, .75f);
        button.colors = colors;
        var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        // Use the project's Chinese UI font from the existing vote panel.
        var existingText = transform.parent.GetComponentInChildren<TMP_Text>(true);
        if (existingText) font = existingText.font;
        var label = BakedText(button.transform, "Label", font, 26);
        label.text = "结束游戏";
        label.alignment = TextAlignmentOptions.Center;
        Fill(label.rectTransform, 8, 4, 8, 4);
        serializedMenu.FindProperty("unlimitedEndButton").objectReferenceValue = button;
        serializedMenu.FindProperty("unlimitedEndLabel").objectReferenceValue = label;
        serializedMenu.ApplyModifiedPropertiesWithoutUndo();

        if (!noticeRoot) {
            noticeRoot = new GameObject("UnlimitedRoundNotice", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            noticeRoot.transform.SetParent(transform.parent, false);
        }
        var noticeRect = (RectTransform)noticeRoot.transform;
        noticeRect.anchorMin = noticeRect.anchorMax = new Vector2(.5f, 1);
        noticeRect.pivot = new Vector2(.5f, 1);
        noticeRect.anchoredPosition = new Vector2(-80, -110);
        noticeRect.sizeDelta = new Vector2(780, 104);
        background = noticeRoot.GetComponent<UnityEngine.UI.Image>();
        background.color = withdrawnColor;
        background.raycastTarget = false;
        messageText = BakedText(noticeRoot.transform, "Message", font, 28);
        messageText.text = "玩家已放弃游戏，游戏将在本局游戏后结束";
        messageText.richText = false;
        messageText.enableAutoSizing = true;
        messageText.fontSizeMin = 18;
        messageText.fontSizeMax = 28;
        messageText.textWrappingMode = TextWrappingModes.Normal;
        messageText.alignment = TextAlignmentOptions.Center;
        Fill(messageText.rectTransform, 24, 12, 100, 12);
        countdownText = BakedText(noticeRoot.transform, "Countdown", font, 26);
        countdownText.text = "5秒";
        countdownText.alignment = TextAlignmentOptions.Center;
        var countRect = countdownText.rectTransform;
        countRect.anchorMin = new Vector2(1, 0);
        countRect.anchorMax = Vector2.one;
        countRect.offsetMin = new Vector2(-94, 12);
        countRect.offsetMax = new Vector2(-12, -12);
        countdownText.gameObject.SetActive(false);
        noticeRoot.SetActive(false);
        button.gameObject.SetActive(false);
        EditorUtility.SetDirty(this);
        EditorUtility.SetDirty(menu);
    }

    private static TMP_Text BakedText(Transform parent, string name, TMP_FontAsset font, float size) {
        var child = parent.Find(name);
        if (!child) {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            child = go.transform;
        }
        var text = child.GetComponent<TMP_Text>();
        text.font = font;
        text.fontSize = size;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static void Fill(RectTransform rect, float left, float top, float right, float bottom) {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }
}
#endif
