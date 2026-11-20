using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.Events
{
    public sealed class EventVenueView : MonoBehaviour
    {
        public string[] paletteNames = { "暖橙", "夜紫", "雾蓝", "酒红" };
        public int PaletteCount => paletteNames.Length;
        public GameObject participationSummary, registrationForm, registrationWithdrawal;
        public GameObject managementRail;
        public GameObject[] pages;
        public GameObject[] managementPages;
        public Salasasa.Venues.VenueTeamIcon teamIcon;
        public RectTransform introductionHeading, introductionDivider;
        public UnityEngine.UI.ScrollRect introductionScroll;
        public Color paper, ink, muted, accent, onAccent, border, field;

        void OnEnable()
        {
            if (!teamIcon) return;
            teamIcon.VisibilityChanged += SetTeamIconLayout;
            SetTeamIconLayout(teamIcon.image && teamIcon.image.enabled && teamIcon.image.sprite);
        }
        void OnDisable()
        {
            if (teamIcon) teamIcon.VisibilityChanged -= SetTeamIconLayout;
        }
        void SetTeamIconLayout(bool visible)
        {
            if (!introductionScroll) return;
            float width = visible ? 704 : 924;
            var scrollRect = (RectTransform)introductionScroll.transform;
            if (Mathf.Approximately(scrollRect.rect.width, width)) return;
            float position = introductionScroll.verticalNormalizedPosition;
            if (introductionHeading) introductionHeading.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            if (introductionDivider) introductionDivider.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            scrollRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            introductionScroll.viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width - 12);
            introductionScroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width - 12);
            if (introductionScroll.verticalScrollbar)
            {
                var scrollbar = (RectTransform)introductionScroll.verticalScrollbar.transform;
                scrollbar.anchoredPosition = new Vector2(width - 5, scrollbar.anchoredPosition.y);
            }
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(introductionScroll.content);
            introductionScroll.verticalNormalizedPosition = position;
        }

        public void ApplyPalette(int palette)
        {
            palette = Mathf.Clamp(palette, 0, PaletteCount - 1);
            foreach (var binding in GetComponentsInChildren<EventVenuePaletteBinding>(true)) binding.Apply(palette);
        }
#if UNITY_EDITOR
        public void CapturePalette(int palette)
        {
            foreach (var binding in GetComponentsInChildren<EventVenuePaletteBinding>(true)) binding.Capture(palette);
        }
#endif
    }
}
