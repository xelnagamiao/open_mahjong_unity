using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AboutUsPanel : MonoBehaviour {
    public Salasasa.Community.PlatformContentClient contentClient;
    private TMP_Text creditsText;
    private Salasasa.Community.PlatformContentDocument displayedDocument;
    private ScrollRect scroll;
    private readonly List<Section> sections = new();

    // The scene already stores a height for each chapter. Keep that space for the
    // scrollbar and navigation while its text is outside the reading area.
    private sealed class Section {
        public RectTransform rect;
        public TMP_Text text;
        public ContentSizeFitter sectionFit, textFit;
        public VerticalLayoutGroup layout;
        public string preparedText;
        public TMP_FontAsset preparedFont;
        public int cursor;

        public void Show(bool visible) {
            text.enabled = visible;
            textFit.enabled = visible;
            layout.enabled = visible;
            sectionFit.enabled = visible;
        }

        public bool Prepare(double deadline) {
            var value = text.text;
            if (preparedText != value || preparedFont != text.font) {
                preparedText = value;
                preparedFont = text.font;
                cursor = 0;
            }
            while (cursor < value.Length) {
                uint code = value[cursor++];
                if (char.IsHighSurrogate((char)code) && cursor < value.Length && char.IsLowSurrogate(value[cursor]))
                    code = (uint)char.ConvertToUtf32((char)code, value[cursor++]);
                if (code > 32)
                    TMP_FontAssetUtilities.GetCharacterFromFontAsset(code, preparedFont, true, text.fontStyle, text.fontWeight, out _);
                if (Time.realtimeSinceStartupAsDouble >= deadline) return false;
            }
            return true;
        }
    }
    private void Awake() {
        scroll = GetComponentInChildren<ScrollRect>(true);
        foreach (RectTransform rect in scroll.content) {
            if (rect.childCount != 1 || !rect.TryGetComponent(out ContentSizeFitter fit) ||
                !rect.TryGetComponent(out VerticalLayoutGroup layout)) continue;
            var child = rect.GetChild(0);
            if (!child.TryGetComponent(out TMP_Text text) || !child.TryGetComponent(out ContentSizeFitter textFit)) continue;
            var section = new Section { rect = rect, text = text, textFit = textFit, sectionFit = fit, layout = layout };
            section.Show(false);
            sections.Add(section);
        }
        creditsText = transform.Find("ContentPanel/Scroll View/Viewport/Content/ThanksContent/Text (TMP)")?.GetComponent<TMP_Text>();
        TextAsset credits = IosResourceCache.Load<TextAsset>("Text/AboutUs/Thanks");
        if (creditsText == null || credits == null) {
            Debug.LogError("关于我们鸣谢文本或内容资源缺失", this);
            return;
        }

        creditsText.text = credits.text.TrimEnd();
        creditsText.richText = false;
        var documentFont = PlatformTypography.ChineseRegular;
        if (documentFont) {
            if (creditsText.TryGetComponent(out PlatformFontBinding binding)) binding.SetDocumentFont(documentFont);
            else creditsText.font = documentFont;
        }
    }
    private void OnEnable() {
        if (!contentClient) return;
        contentClient.EnsureLoaded();
        contentClient.Changed += RefreshCredits;
        RefreshCredits();
        contentClient.Refresh();
    }

    private void OnDisable() {
        if (contentClient) contentClient.Changed -= RefreshCredits;
        foreach (var section in sections) section.Show(false);
    }

    private void RefreshCredits() {
        var document = contentClient.Document;
        if (!creditsText || document == null || document == displayedDocument) return;
        displayedDocument = document;
        string value = document.CreditsText;
        if (creditsText.text == value) return;
        foreach (var section in sections)
            if (section.text == creditsText) section.Show(false);
        creditsText.text = value;
    }

    private void LateUpdate() {
        var viewport = scroll.viewport;
        var bounds = viewport.rect;
        float margin = bounds.height * 0.2f;
        double deadline = Time.realtimeSinceStartupAsDouble + .002;
        foreach (var section in sections) {
            var rect = section.rect;
            float top = viewport.InverseTransformPoint(rect.TransformPoint(0, rect.rect.yMax, 0)).y;
            float bottom = viewport.InverseTransformPoint(rect.TransformPoint(0, rect.rect.yMin, 0)).y;
            bool near = top >= bounds.yMin - margin && bottom <= bounds.yMax + margin;
            if (section.text.enabled && (!near || section.preparedText != section.text.text || section.preparedFont != section.text.font))
                section.Show(false);
            if (near && !section.text.enabled && Time.realtimeSinceStartupAsDouble < deadline && section.Prepare(deadline))
                section.Show(true);
        }
    }
}
