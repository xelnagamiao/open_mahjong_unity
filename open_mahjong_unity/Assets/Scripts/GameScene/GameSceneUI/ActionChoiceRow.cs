using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class ActionChoiceRow : MonoBehaviour, IPointerDownHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler {
    [SerializeField] private ActionBlock action;
    [SerializeField] private UnityEngine.UI.Image background;
    [SerializeField] private TMP_Text summary;
    [SerializeField] private TMP_Text details;
    [SerializeField] private RectTransform tiles;
    readonly List<StaticCard> cards = new List<StaticCard>();
    string pressedAction;
    Color rowColor, hoverColor;
    public int Index { get; private set; } = -1;
    public string ActionType => action.actionType;
    public TMP_Text Summary => summary;
    public TMP_Text Details => details;
    public int CreatedCardCount => cards.Count;
    public bool HasReferences => action != null && background != null && summary != null && details != null && tiles != null;
    public const float TileWidth = 76f, TileSpacing = 4f, GroupGap = 20f, SidePadding = 18f;

    public void Bind(int index, ActionCandidate candidate, GameObject cardPrefab, float width, float rowHeight, float detailsHeight) {
        Index = index; pressedAction = null;
        action.enabled = false; // This row validates the pressed identity before forwarding a click.
        action.actionType = candidate.ActionType; action.targetTile = candidate.TargetTile; action.chiComboIndex = -1;
        TableText.Set(summary, candidate.Caption ?? "", candidate.TextContext);
        TableText.Set(details, candidate.Details ?? "", candidate.TextContext);
        ConfigureText(summary, 24); ConfigureText(details, 18);
        SetRect((RectTransform)transform, 0, 0, width, rowHeight);
        SetRect(summary.rectTransform, SidePadding, 10, width - SidePadding * 2, 32);
        details.gameObject.SetActive(!string.IsNullOrEmpty(candidate.Details));
        SetRect(details.rectTransform, SidePadding, 44, width - SidePadding * 2, detailsHeight);
        SetRect(tiles, SidePadding, 46 + detailsHeight, width - SidePadding * 2, rowHeight - 58 - detailsHeight);
        int at = 0;
        float x = 0;
        foreach (var group in candidate.TileGroups ?? new[] { candidate.TileIds }) {
            foreach (int tile in group) {
                if (at == cards.Count) cards.Add(Instantiate(cardPrefab, tiles).GetComponent<StaticCard>());
                var card = cards[at++]; card.gameObject.SetActive(true);
                var rect = (RectTransform)card.transform;
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, TileWidth);
                card.SetTileOnlyImage(tile);
                SetRect(rect, x, 0, TileWidth, rect.rect.height);
                x += TileWidth + TileSpacing;
            }
            x += GroupGap - TileSpacing;
        }
        for (int i = at; i < cards.Count; i++) cards[i].gameObject.SetActive(false);
        background.color = rowColor;
    }

    public void Release() {
        Index = -1; pressedAction = null; action.actionType = null;
        gameObject.SetActive(false);
    }

    public void ApplyColors(Color normal, Color hover, Color title, Color body) {
        rowColor = normal; hoverColor = hover; background.color = normal;
        summary.color = title; details.color = body;
    }

    public void OnPointerDown(PointerEventData data) { pressedAction = action.actionType; }
    public void OnPointerClick(PointerEventData data) {
        if (data.button == PointerEventData.InputButton.Left && !data.dragging && !string.IsNullOrEmpty(pressedAction)
                && pressedAction == action.actionType) action.OnPointerClick(data);
        pressedAction = null;
    }
    public void OnPointerEnter(PointerEventData data) { background.color = hoverColor; }
    public void OnPointerExit(PointerEventData data) { background.color = rowColor; }

    public static void ConfigureText(TMP_Text text, float size) {
        text.fontStyle = FontStyles.Normal; text.fontWeight = FontWeight.Regular;
        text.fontSize = size; text.enableAutoSizing = false; text.richText = false;
        text.alignment = TextAlignmentOptions.TopLeft; text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.Normal; text.overflowMode = TextOverflowModes.Overflow;
        var localized = text.GetComponent<LocalizedPlatformLabel>();
        if (localized != null) { localized.authoredSize = size; localized.minimumSize = size; localized.authoredAutoSize = false; }
    }
    static void SetRect(RectTransform rect, float x, float y, float width, float height) {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); rect.localScale = Vector3.one;
    }
}
