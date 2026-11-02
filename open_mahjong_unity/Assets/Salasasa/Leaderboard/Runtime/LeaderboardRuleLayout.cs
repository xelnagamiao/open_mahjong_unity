using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Salasasa.Home;

namespace Salasasa.Leaderboards
{
    public sealed class LeaderboardRuleLayout : MonoBehaviour
    {
        public LeaderboardController controller;
        public LeaderboardPaletteEditor palette;
        public RectTransform sideRoot;
        public ScrollRect sideScroll;
        public Button[] sideButtons;
        public SettingsShapeGraphic[] sideSurfaces;
        public TMP_Text[] sideLabels;
        public string[] ruleIds;
        public SettingsShapeGraphic scrollbarTrack, scrollbarHandle, innerDivider;
        bool initialized;
        int renderedRule = -1;

        void Awake() { if (Application.isPlaying) Initialize(); }
        public void Initialize()
        {
            if (initialized) return;
            initialized = true;
            for (int i = 0; i < sideButtons.Length; i++)
            {
                int tab = i;
                sideButtons[i].onClick.AddListener(() => SelectRule(tab));
            }
            palette.ColorsApplied += ApplyColors;
            palette.Initialize();
            ApplyColors();
        }
        void SelectRule(int tab)
        {
            int index = Array.IndexOf(LeaderboardRules.Ids, ruleIds[tab]);
            if (index < 0) return;
            controller.SelectRule(index);
            ApplyColors(); RevealSelectedRule();
        }
        void LateUpdate()
        {
            if (!initialized || !controller.IsOpen) return;
            if (renderedRule != controller.selectedRule) { ApplyColors(); RevealSelectedRule(); }
        }
        public void ApplyColors()
        {
            if (!palette) return;
            Color background = palette.GetAppliedColor((int)LeaderboardPaletteEditor.ColorSlot.Alternate);
            Color ink = palette.GetAppliedColor((int)LeaderboardPaletteEditor.ColorSlot.Text);
            Color accent = palette.GetAppliedColor((int)LeaderboardPaletteEditor.ColorSlot.Accent);
            Color line = palette.GetAppliedColor((int)LeaderboardPaletteEditor.ColorSlot.Line);
            string selected = LeaderboardRules.Ids[Mathf.Clamp(controller.selectedRule, 0, LeaderboardRules.Ids.Length - 1)];
            for (int i = 0; i < sideButtons.Length; i++)
            {
                bool active = ruleIds[i] == selected;
                Color fill = active ? accent : background;
                sideSurfaces[i].color = fill;
                sideSurfaces[i].borderColor = line;
                sideSurfaces[i].SetVerticesDirty();
                sideLabels[i].color = Readable(ink, fill);
            }
            scrollbarTrack.color = line; scrollbarHandle.color = accent;
            innerDivider.color = line;
            renderedRule = controller.selectedRule;
        }
        public void RevealSelectedRule()
        {
            if (!sideRoot.gameObject.activeInHierarchy) return;
            int index = Array.IndexOf(ruleIds, LeaderboardRules.Ids[controller.selectedRule]);
            if (index < 0) return;
            Canvas.ForceUpdateCanvases();
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(sideScroll.viewport, sideButtons[index].transform);
            var frame = sideScroll.viewport.rect;
            var position = sideScroll.content.anchoredPosition;
            if (bounds.max.y > frame.yMax) position.y -= bounds.max.y - frame.yMax;
            else if (bounds.min.y < frame.yMin) position.y += frame.yMin - bounds.min.y;
            position.y = Mathf.Clamp(position.y, 0, Mathf.Max(0, sideScroll.content.rect.height - frame.height));
            sideScroll.StopMovement(); sideScroll.content.anchoredPosition = position;
        }
        static Color Readable(Color ink, Color background)
        {
            float a = Luminance(ink), b = Luminance(background);
            if ((Mathf.Max(a, b) + .05f) / (Mathf.Min(a, b) + .05f) >= 4.5f) return ink;
            return 1.05f / (b + .05f) >= (b + .05f) / .05f ? Color.white : new Color32(24, 29, 35, 255);
        }
        static float Luminance(Color color) { var c = color.linear; return .2126f * c.r + .7152f * c.g + .0722f * c.b; }
        void OnDestroy() { if (palette) palette.ColorsApplied -= ApplyColors; }
    }
}
