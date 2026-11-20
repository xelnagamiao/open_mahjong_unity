using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace Salasasa.ChatLab
{
    public sealed class ChatMessageRow : MonoBehaviour
    {
        public Button button, avatarButton, nameButton;
        public TMP_Text author, body, time;
        public RawImage portrait;
        public UnityEngine.UI.Image bubble;
        public RectTransform bubbleRect;
        public int boundIndex;

        public float LayoutBubble(float width, float top, bool mine)
        {
            // Authors, timestamps and bubbles share the same edge, just inside the optional portrait.
            float inset = portrait ? portrait.rectTransform.rect.width + 10f : 0f;
            float available = Mathf.Max(1f, width - inset);
            float maxWidth = Mathf.Clamp(width * .75f, 1f, available);
            float padding = Mathf.Min(10f, (maxWidth - 1f) * .5f);
            float textWidth = Mathf.Ceil(body.GetPreferredValues(body.text, maxWidth - 2f * padding, Mathf.Infinity).x);
            float bubbleWidth = Mathf.Min(maxWidth, Mathf.Max(48f, textWidth + 2f * padding + 1f));
            float textHeight = body.GetPreferredValues(body.text, bubbleWidth - 2f * padding, Mathf.Infinity).y;
            float bubbleHeight = Mathf.Max(34f, Mathf.Ceil(textHeight) + 14f);

            Vector2 nameSize = author.GetPreferredValues(author.text);
            Vector2 timeSize = time.GetPreferredValues(time.text);
            float headerHeight = Mathf.Ceil(Mathf.Max(nameSize.y, timeSize.y)) + 2f;
            float timeWidth = Mathf.Min(available, Mathf.Ceil(timeSize.x) + 1f);
            float gap = Mathf.Min(8f, available - timeWidth);
            float nameWidth = Mathf.Min(available - timeWidth - gap, Mathf.Ceil(nameSize.x) + 1f);
            float headerX = mine ? width - inset - timeWidth - gap - nameWidth : inset;
            Place(author.rectTransform, headerX, 0f, nameWidth, headerHeight);
            Place(time.rectTransform, headerX + nameWidth + gap, 0f, timeWidth, headerHeight);

            Place(bubbleRect, mine ? width - inset - bubbleWidth : inset, headerHeight, bubbleWidth, bubbleHeight);
            body.rectTransform.offsetMin = new Vector2(padding, 6f);
            body.rectTransform.offsetMax = new Vector2(-padding, -6f);
            if (portrait)
            {
                var avatar = portrait.rectTransform;
                avatar.anchoredPosition = new Vector2(mine ? width - avatar.rect.width : 0f, -headerHeight);
            }
            float height = headerHeight + Mathf.Max(bubbleHeight, portrait ? portrait.rectTransform.rect.height : 0f);
            Place((RectTransform)transform, 0f, top, 0f, height);
            return height;
        }

        static void Place(RectTransform rect, float x, float top, float width, float height)
        {
            rect.anchoredPosition = new Vector2(x, -top);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
