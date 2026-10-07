using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>牌谱头像旁的紧凑听牌条。只呈现快照，不读取对局状态或自行计番。</summary>
public sealed class RecordPlayerWaits : MonoBehaviour {
    public sealed class Entry {
        public int Tile;
        public int Remaining;
        public WaitTileHint Hint;
    }

    [SerializeField] private RectTransform tileContainer;
    [SerializeField] private RectTransform cellTemplate;
    [SerializeField] private TipsFanCount hintStyle;

    private const int MaxColumns = 3;
    private const float TileWidth = 24f;
    private const float CountSize = 24f;
    private const float CountGroupGap = 8f;
    private const float Gap = 3f;
    private readonly List<Cell> cells = new List<Cell>();
    private sealed class Cell {
        public RectTransform Root;
        public StaticCard Card;
        public TMP_Text Count;
        public UnityEngine.UI.Image CountBackground;
    }

    private void OnEnable() => TileFaceResolver.OnPackChanged += RefreshLayout;
    private void OnDisable() => TileFaceResolver.OnPackChanged -= RefreshLayout;
    public void Hide() => gameObject.SetActive(false);

    public void Show(IReadOnlyList<Entry> entries) {
        if (entries == null || entries.Count == 0) { Hide(); return; }
        gameObject.SetActive(true);
        for (int i = 0; i < entries.Count; i++) {
            if (i == cells.Count) {
                RectTransform root = Instantiate(cellTemplate, tileContainer);
                root.name = "WaitTile";
                cells.Add(new Cell {
                    Root = root,
                    Card = root.GetComponentInChildren<StaticCard>(true),
                    Count = root.GetComponentInChildren<TMP_Text>(true),
                    CountBackground = root.Find("CountBackground").GetComponent<UnityEngine.UI.Image>(),
                });
            }
            Cell cell = cells[i];
            Entry entry = entries[i];
            cell.Root.gameObject.SetActive(true);
            cell.Card.SetTileOnlyImage(entry.Tile);
            cell.Count.text = entry.Remaining.ToString();
            cell.Count.color = Color.white;
            string kind = entry.Hint?.Kind;
            cell.CountBackground.color = entry.Remaining <= 0 ? hintStyle.exhaustedColor
                : kind == WaitTileHint.KindRon ? hintStyle.dianheColor
                : kind == WaitTileHint.KindTsumoOnly ? hintStyle.zimoColor : hintStyle.wuyiColor;
        }
        for (int i = entries.Count; i < cells.Count; i++) cells[i].Root.gameObject.SetActive(false);
        RefreshLayout();
    }

    private void RefreshLayout() {
        int count = 0;
        float tileHeight = 0f;
        foreach (Cell cell in cells) {
            if (!cell.Root.gameObject.activeSelf) continue;
            var cardRect = (RectTransform)cell.Card.transform;
            cardRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, TileWidth);
            cell.Card.RefreshVisual();
            tileHeight = Mathf.Max(tileHeight, cardRect.rect.height);
            count++;
        }
        if (count == 0) return;
        float rowHeight = Mathf.Max(tileHeight, 36f);
        int columns = Mathf.Min(count, MaxColumns);
        float width = columns * (TileWidth + CountSize + 2f * Gap) - 2f * Gap + CountGroupGap;
        float height = Mathf.CeilToInt(count / (float)MaxColumns) * (rowHeight + Gap) - Gap;
        for (int i = 0; i < count; i++) {
            Cell cell = cells[i];
            int column = i % MaxColumns;
            int rowStart = i - column;
            int rowColumns = Mathf.Min(count - rowStart, MaxColumns);
            float cardX = column * (TileWidth + Gap);
            float countsX = rowColumns * (TileWidth + Gap) - Gap + CountGroupGap;
            cell.Root.anchoredPosition = new Vector2(cardX, -(i / MaxColumns) * (rowHeight + Gap));
            cell.Root.sizeDelta = new Vector2(TileWidth, rowHeight);
            var cardRect = (RectTransform)cell.Card.transform;
            cardRect.anchoredPosition = new Vector2(0f, -(rowHeight - cardRect.rect.height) * .5f);
            // 每行先排列牌面，再在右侧按相同顺序排列各牌余张（如 1m 4m 3 3）。
            cell.CountBackground.rectTransform.anchoredPosition = new Vector2(countsX + column * (CountSize + Gap) - cardX, -(rowHeight - CountSize) * .5f);
            cell.CountBackground.rectTransform.sizeDelta = new Vector2(CountSize, CountSize);
        }
        tileContainer.sizeDelta = new Vector2(width, height);
        ((RectTransform)transform).sizeDelta = new Vector2(width + 40f, height + 12f);
    }
}
