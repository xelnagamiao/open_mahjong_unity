#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;

namespace Mahjong.SceneSettingsUI
{
public sealed partial class SceneSettingsSidebar
{
        public void ApplyCompactLayout()
        {
            var root = (RectTransform)transform;
            root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 792);
            var surface = transform.Find("FlatSurface") as RectTransform;
            if (surface != null) Stretch(surface, Vector2.zero, Vector2.zero);
            PlaceGroup("桌面", 102);
            PlaceGroup("装扮", 336);
            PlaceRow("TableClothButton", 130, 60);
            PlaceRow("TableEdgeButton", 194, 60);
            PlaceRow("CenterDisplayButton", 258, 60);
            PlaceRow("CardFaceButton", 364, 60);
            PlaceRow("CardBackButton", 428, 60);
            PlaceRow("CharacterButton", 492, 60);
            var rule = transform.Find("ActionRule") as RectTransform;
            if (rule != null) rule.anchoredPosition = new Vector2(rule.anchoredPosition.x, -576);
            PlaceRow("RandomButton", 592, 52);
            PlaceRow("HideButton", 654, 52);
            PlaceRow("RestoreAllDefaultsButton", 716, 52);
        }
        private void PlaceGroup(string name, float top)
        {
            var label = transform.Find(name + "Group") as RectTransform;
            var line = transform.Find(name + "Line") as RectTransform;
            if (label != null) label.anchoredPosition = new Vector2(label.anchoredPosition.x, -top);
            if (line != null) line.anchoredPosition = new Vector2(line.anchoredPosition.x, -top - 12);
        }
        private void PlaceRow(string name, float top, float height)
        {
            var rect = transform.Find(name) as RectTransform;
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(16, -top);
            rect.sizeDelta = new Vector2(292, height);
            var row = rect.GetComponent<SceneSettingsSidebarRow>();
            if (row == null) return;
            if (row.band != null) Stretch(row.band.rectTransform, Vector2.zero, Vector2.zero);
            if (row.label != null) Stretch(row.label.rectTransform, new Vector2(71, 0), new Vector2(-16, 0));
            if (row.icon != null) CenterIcon(row.icon.rectTransform, 23);
            if (row.marker != null) CenterIcon(row.marker.rectTransform, 7);
        }
        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = min; rect.offsetMax = max;
        }
        private static void CenterIcon(RectTransform rect, float left)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, .5f);
            rect.anchoredPosition = new Vector2(left, 0);
        }
}
}
#endif
