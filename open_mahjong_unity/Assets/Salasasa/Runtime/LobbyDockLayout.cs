using System;
using TMPro;
using UnityEngine;

namespace Salasasa.Home
{
    [DefaultExecutionOrder(22000), DisallowMultipleComponent]
    public sealed class LobbyDockLayout : MonoBehaviour
    {
        public RectTransform[] buttons;
        public TMP_Text[] captions;
        public float minimumWidth = 100, spacing = 12;
        public float referenceWidth = 548, referenceScale = 1;
        string signature;
        void OnEnable() { signature = null; Apply(); }
        void LateUpdate() { Apply(); }
        public void Apply()
        {
            if (buttons == null || captions == null || buttons.Length != captions.Length) return;
            var rect = (RectTransform)transform;
            var parent = (RectTransform)rect.parent;
            string state = ConfigManager.CurrentLanguage + ":" + parent.rect.width;
            foreach (var caption in captions) state += ":" + caption.text + ":" + caption.font.GetInstanceID();
            if (signature == state) return;
            signature = state;
            var widths = new float[buttons.Length];
            float total = spacing * Mathf.Max(0, buttons.Length - 1);
            for (int i = 0; i < buttons.Length; i++)
            {
                float size = captions[i].fontSize;
                var binding = captions[i].GetComponent<LocalizedPlatformLabel>();
                float intended = binding ? binding.authoredSize : size;
                bool autoSize = captions[i].enableAutoSizing;
                captions[i].enableAutoSizing = false;
                captions[i].fontSize = intended;
                widths[i] = Mathf.Max(minimumWidth, captions[i].GetPreferredValues(captions[i].text, Mathf.Infinity, Mathf.Infinity).x + 20);
                captions[i].fontSize = size;
                captions[i].enableAutoSizing = autoSize;
                total += widths[i];
            }
            float available = Mathf.Max(1, parent.rect.width - Mathf.Abs(rect.anchoredPosition.x) - 24);
            float scale = Mathf.Min(referenceScale, available / total);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, total);
            rect.localScale = Vector3.one * scale;
            float x = 0;
            for (int i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                button.anchorMin = button.anchorMax = button.pivot = new Vector2(0, 1);
                button.anchoredPosition = new Vector2(x, button.anchoredPosition.y);
                button.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, widths[i]);
                var caption = captions[i].rectTransform;
                caption.anchorMin = new Vector2(0, caption.anchorMin.y);
                caption.anchorMax = new Vector2(1, caption.anchorMax.y);
                caption.offsetMin = new Vector2(8, caption.offsetMin.y);
                caption.offsetMax = new Vector2(-8, caption.offsetMax.y);
                x += widths[i] + spacing;
            }
        }
    }
}
