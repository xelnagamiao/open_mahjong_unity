using UnityEngine;

namespace Salasasa.Home
{
    [DisallowMultipleComponent]
    public sealed class EntranceDockAlignment : MonoBehaviour
    {
        public LobbyShowcase artwork;
        public RectTransform[] firstButtons, lastButtons;
        readonly Vector3[] corners = new Vector3[4];

        void OnEnable() { if (Application.isPlaying) Apply(); }
        void LateUpdate() { Apply(); }

        public void Apply()
        {
            if (!artwork || !(transform is RectTransform rect) || !(rect.parent is RectTransform parent)) return;
            int style = artwork.CurrentTheme;
            if (firstButtons == null || lastButtons == null || style >= firstButtons.Length || style >= lastButtons.Length) return;
            var first = firstButtons[style]; var last = lastButtons[style];
            if (!first || !last || rect.rect.width <= 0) return;
            first.GetWorldCorners(corners);
            float left = parent.InverseTransformPoint(corners[0]).x;
            last.GetWorldCorners(corners);
            float right = parent.InverseTransformPoint(corners[2]).x;
            float scale = (right - left) / rect.rect.width;
            if (scale <= 0) return;
            var position = rect.anchoredPosition;
            position.x = right - Mathf.Lerp(parent.rect.xMin, parent.rect.xMax, rect.anchorMax.x);
            if (rect.anchoredPosition != position) rect.anchoredPosition = position;
            var uniformScale = new Vector3(scale, scale, 1);
            if (rect.localScale != uniformScale) rect.localScale = uniformScale;
        }
    }
}
