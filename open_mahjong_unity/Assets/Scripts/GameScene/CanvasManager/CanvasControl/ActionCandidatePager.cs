using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

/// <summary>Grouped settlement choices use the baked scrolling panel; other captioned choices retain their shared popup.</summary>
public static class ActionCandidatePager {
    public static bool Build(Transform parent, IReadOnlyList<ActionCandidate> choices, GameObject blockPrefab, GameObject cardPrefab, TMP_Text labelTemplate, RectTransform anchor = null) {
        var offered = choices.Where(choice => !string.IsNullOrEmpty(choice.ActionType)).ToArray();
        if (offered.Any(choice => choice.TileGroups != null)) {
            var panel = GameCanvas.Instance?.ChoicePanel;
            if (panel == null) throw new System.InvalidOperationException("Bake ActionChoicePanel into MainScene before showing grouped choices");
            panel.Show(offered, cardPrefab, anchor);
            return true;
        }
        foreach (var choice in offered) BuildChoice(parent, choice, blockPrefab, cardPrefab, labelTemplate);
        return false;
    }

    static int[][] Groups(ActionCandidate choice) => choice.TileGroups != null && choice.TileGroups.Length > 0
        ? choice.TileGroups : new[] { choice.TileIds };

    static float TilesWidth(ActionCandidate choice) {
        var groups = Groups(choice);
        return groups.Sum(group => Mathf.Max(0, group.Length * (ActionBlock.TileWidth + ActionBlock.TileSpacing) - ActionBlock.TileSpacing))
            + Mathf.Max(0, groups.Length - 1) * 22f;
    }

    static float ChoiceWidth(ActionCandidate choice) => Mathf.Max(324f, TilesWidth(choice) + 32f);

    static RectTransform BuildChoice(Transform parent, ActionCandidate choice, GameObject blockPrefab, GameObject cardPrefab, TMP_Text labelTemplate) {
            var go = Object.Instantiate(blockPrefab, parent);
            go.name = "Candidate_" + choice.ActionType;
            foreach (var layout in go.GetComponents<UnityEngine.UI.LayoutGroup>()) layout.enabled = false;
            foreach (var fitter in go.GetComponents<UnityEngine.UI.ContentSizeFitter>()) fitter.enabled = false;
            var block = go.GetComponent<ActionBlock>();
            block.actionType = choice.ActionType; block.targetTile = choice.TargetTile;
            var groups = Groups(choice);
            const float groupGap = 22f;
            float tilesWidth = TilesWidth(choice);
            float width = ChoiceWidth(choice);
            float tileHeight = 0;
            var cards = new List<RectTransform>();
            float x = -tilesWidth * .5f + ActionBlock.TileWidth * .5f;
            foreach (var group in groups) {
                foreach (int tileId in group) {
                    var card = block.AddTile(cardPrefab, tileId);
                    var r = (RectTransform)card.transform;
                    tileHeight = Mathf.Max(tileHeight, r.rect.height);
                    cards.Add(r);
                    r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, 1);
                    r.anchoredPosition = new Vector2(x, -38);
                    x += ActionBlock.TileWidth + ActionBlock.TileSpacing;
                }
                x += groupGap - ActionBlock.TileSpacing;
            }
            var label = Object.Instantiate(labelTemplate, go.transform);
            label.name = "CandidateLabel"; label.text = choice.Caption;
            var localized = label.GetComponent<LocalizedPlatformLabel>();
            if (localized != null) {
                localized.authoredSize = 22;
                localized.minimumSize = 18;
                localized.authoredAutoSize = false;
                localized.SetSource(choice.Caption);
            }
            else label.text = PlatformText.Get(choice.Caption);
            label.raycastTarget = false; label.enableAutoSizing = false; label.fontSize = 22;
            if (choice.TileGroups != null) {
                label.fontStyle = FontStyles.Normal;
                label.fontWeight = FontWeight.Regular;
            }
            label.textWrappingMode = TextWrappingModes.Normal; label.overflowMode = TextOverflowModes.Overflow;
            label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(.5f, 1);
            float labelHeight = choice.TileGroups != null ? 32f
                : Mathf.Max(32, label.GetPreferredValues(label.text ?? "", width - 12, Mathf.Infinity).y);
            label.rectTransform.sizeDelta = new Vector2(width - 12, labelHeight);
            label.rectTransform.anchoredPosition = new Vector2(0, -2);
            foreach (var card in cards) card.anchoredPosition = new Vector2(card.anchoredPosition.x, -labelHeight - 6);
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, tileHeight + labelHeight + 19);
            return (RectTransform)go.transform;
    }
}
