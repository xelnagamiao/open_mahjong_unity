using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Build captioned choices; the shared popup owns wrapping and pagination.</summary>
public static class ActionCandidatePager {
    public static void Build(Transform parent, IReadOnlyList<ActionCandidate> choices, GameObject blockPrefab, GameObject cardPrefab, TMP_Text labelTemplate) {
        foreach (var choice in choices) {
            if (string.IsNullOrEmpty(choice.ActionType)) continue;
            var go = Object.Instantiate(blockPrefab, parent);
            go.name = "Candidate_" + choice.TargetTile;
            foreach (var layout in go.GetComponents<UnityEngine.UI.LayoutGroup>()) layout.enabled = false;
            foreach (var fitter in go.GetComponents<UnityEngine.UI.ContentSizeFitter>()) fitter.enabled = false;
            var block = go.GetComponent<ActionBlock>();
            block.actionType = choice.ActionType; block.targetTile = choice.TargetTile;
            float width = Mathf.Max(324f, choice.TileIds.Length * (ActionBlock.TileWidth + ActionBlock.TileSpacing) - ActionBlock.TileSpacing + 32f);
            float tileHeight = 0;
            var cards = new List<RectTransform>();
            for (int i = 0; i < choice.TileIds.Length; i++) {
                var card = block.AddTile(cardPrefab, choice.TileIds[i]);
                var r = (RectTransform)card.transform;
                tileHeight = Mathf.Max(tileHeight, r.rect.height);
                cards.Add(r);
                r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, 1);
                r.anchoredPosition = new Vector2((i - (choice.TileIds.Length - 1) * .5f) * (ActionBlock.TileWidth + ActionBlock.TileSpacing), -38);
            }
            var label = Object.Instantiate(labelTemplate, go.transform);
            label.name = "CandidateLabel"; label.text = choice.Caption;
            label.raycastTarget = false; label.enableAutoSizing = false; label.fontSize = 22;
            label.textWrappingMode = TextWrappingModes.Normal; label.overflowMode = TextOverflowModes.Overflow;
            label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(.5f, 1);
            float labelHeight = Mathf.Max(32, label.GetPreferredValues(choice.Caption ?? "", width - 12, Mathf.Infinity).y);
            label.rectTransform.sizeDelta = new Vector2(width - 12, labelHeight);
            label.rectTransform.anchoredPosition = new Vector2(0, -2);
            foreach (var card in cards) card.anchoredPosition = new Vector2(card.anchoredPosition.x, -labelHeight - 6);
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, tileHeight + labelHeight + 19);
        }
    }
}
