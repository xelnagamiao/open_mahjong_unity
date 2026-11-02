#if UNITY_EDITOR
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class RoundPanelUiBaker
{
    public static void ApplyTypography(Scene scene)
    {
        foreach (var round in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<RoundPanel>(true)))
            foreach (var label in round.GetComponentsInChildren<TMP_Text>(true))
            {
                if (!PlatformTypographyBaker.IsTextFont(label.font)) continue;
                TableHudTypography.Apply(label);
                EditorUtility.SetDirty(label);
                var binding = label.GetComponent<PlatformFontBinding>();
                if (binding) EditorUtility.SetDirty(binding);
            }
    }

    public static void Bake(Scene scene)
    {
        var rounds = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<RoundPanel>(true)).Single();
        foreach (string name in new[] { "SimpleRoundPanel", "RiichiRoundPanel" })
        {
            var panel = rounds.transform.Find(name);
            var header = panel.Find("Header").GetComponent<HorizontalLayoutGroup>();
            header.padding = new RectOffset(18, 18, 0, 4);
            header.childAlignment = TextAnchor.MiddleCenter;
            header.childControlWidth = header.childForceExpandWidth = true;
            header.childControlHeight = header.childForceExpandHeight = true;
            var rule = panel.Find("Header/Rule").GetComponent<TMP_Text>();
            TableHudTypography.Apply(rule);
            rule.color = Color.black;
            rule.alignment = TextAlignmentOptions.Center;
            rule.margin = Vector4.zero;
            rule.fontSize = rule.fontSizeMax = 26;
            rule.fontSizeMin = 20;
            rule.enableAutoSizing = true;
            rule.textWrappingMode = TextWrappingModes.NoWrap;
            foreach (var label in panel.Find("Content").GetComponentsInChildren<TMP_Text>(true))
                TableHudTypography.Apply(label);
            foreach (var label in panel.Find("MatchStrip").GetComponentsInChildren<TMP_Text>(true))
                TableHudTypography.Apply(label);
            EditorUtility.SetDirty(header);
        }
        foreach (var card in rounds.GetComponentsInChildren<StaticCard>(true))
        {
            var rect = (RectTransform)card.transform;
            float ratio = rect.rect.height / rect.rect.width;
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 46);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 46 * ratio);
            foreach (var image in card.GetComponentsInChildren<Image>(true))
            {
                var artwork = image.rectTransform;
                if (artwork != rect && artwork.anchorMin == artwork.anchorMax) artwork.sizeDelta = rect.rect.size;
            }
        }
        ApplyTypography(scene);
        rounds.GetComponent<TableHudMatchInfo>().RefreshLayout();
        foreach (var component in rounds.GetComponentsInChildren<Component>(true))
            if (component) EditorUtility.SetDirty(component);
    }
}
#endif
