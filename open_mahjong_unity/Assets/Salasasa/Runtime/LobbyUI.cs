using System;
using TMPro;
using UnityEngine;

namespace Salasasa.Home
{
    internal static class LobbyUI
    {
        public static RectTransform Rect(Transform p, string name, float x, float y, float w, float h)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(p, false);
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h); return r;
        }
        public static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
        public static LobbyShape Shape(Transform p, string name, float x, float y, float w, float h, Color c, LobbyShape.Kind kind = LobbyShape.Kind.Rect, float radius = 0, Color? outline = null)
        {
            var shape = Rect(p, name, x, y, w, h).gameObject.AddComponent<LobbyShape>(); shape.color = c; shape.kind = kind;
            shape.radius = radius; shape.outlineColor = outline ?? Color.clear; shape.raycastTarget = false; return shape;
        }
        public static TMP_Text Text(Transform p, string name, string value, float x, float y, float w, float h, float size, Color color, TMP_FontAsset font, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var text = Rect(p, name, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>(); text.font = font; text.text = value;
            text.fontSize = size; text.color = color; text.alignment = align; text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Overflow; text.extraPadding = true;
            return text;
        }
        public static UnityEngine.UI.Button Button(Transform p, string name, string label, float x, float y, float w, float h, LobbyTheme theme, Action click, bool primary = false)
        {
            var shape = Shape(p, name, x, y, w, h, primary ? theme.accent : Color.Lerp(theme.surface, theme.line, .12f), theme.shape, theme.corner, primary ? null : theme.line);
            shape.raycastTarget = true;
            var button = shape.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = shape;
            var colors = button.colors; colors.highlightedColor = new Color(.92f, .95f, 1, 1); colors.pressedColor = new Color(.78f, .84f, .93f, 1); colors.fadeDuration = .1f; button.colors = colors;
            button.onClick.AddListener(() => click());
            Text(shape.transform, "Label", label, 10, 0, w - 20, h, 18, primary ? theme.accentInk : theme.ink, theme.font, TextAlignmentOptions.Center);
            return button;
        }
        public static InkGraphic Icon(Transform p, string name, InkGraphic.Form form, float x, float y, float w, float h, Color color, float stroke = 1.8f)
        {
            var icon = Rect(p, name, x, y, w, h).gameObject.AddComponent<InkGraphic>(); icon.form = form; icon.color = color; icon.stroke = stroke; icon.raycastTarget = false; return icon;
        }

    }
}
