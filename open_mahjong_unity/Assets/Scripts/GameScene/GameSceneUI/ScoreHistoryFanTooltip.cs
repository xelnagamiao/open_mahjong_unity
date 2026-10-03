using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// 计分板主番悬停浮层：场景中编辑固定布局，牌张与和牌面板一致，全部竖直并排并适配宽度。
/// </summary>
public class ScoreHistoryFanTooltip : MonoBehaviour {
    public static ScoreHistoryFanTooltip Instance { get; private set; }

    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform panelRect;
    [SerializeField] private RectTransform tilesRoot;
    [SerializeField] private GameObject staticCardPrefab;
    [SerializeField] private GameObject hideSplitPrefab;
    [SerializeField] private TextMeshProUGUI allFansText;
    [SerializeField] private TextMeshProUGUI scoreSummaryText;
    [SerializeField] private float cardScale = 0.5f;

    private readonly List<GameObject> _spawnedObjects = new List<GameObject>();
    private Vector2 _cardBaseSize = Vector2.zero;
    private Vector2 _splitBaseSize = Vector2.zero;
    private ScoreHistoryMainFanCell _hoveredCell;
    private ScoreHistoryMainFanCell _pinnedCell;

    public bool IsPinned => _pinnedCell != null;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        ResolveReferences();
        HideImmediate();
    }

    private void OnEnable() {
        if (Instance == null || Instance == this) {
            Instance = this;
        }
    }

    private void ResolveReferences() {
        if (panelRect == null) {
            panelRect = transform as RectTransform;
        }
        if (canvasGroup == null) {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }
        if (tilesRoot == null && panelRect != null) {
            Transform found = panelRect.Find("TilesRoot");
            if (found != null) tilesRoot = found as RectTransform;
        }
        if (allFansText == null) {
            allFansText = FindTextByName("AllFansText");
        }
        if (scoreSummaryText == null) {
            scoreSummaryText = FindTextByName("ScoreSummaryText");
        }
        BootstrapFromEndResultPanel();
        CachePrefabSizes();
    }

    private void BootstrapFromEndResultPanel() {
        if (EndResultPanel.Instance == null) return;
        if (staticCardPrefab == null) staticCardPrefab = EndResultPanel.Instance.CardPrefab;
        if (hideSplitPrefab == null) hideSplitPrefab = EndResultPanel.Instance.HideSplitPrefab;
    }

    private void CachePrefabSizes() {
        if (_cardBaseSize == Vector2.zero && staticCardPrefab != null) {
            _cardBaseSize = staticCardPrefab.GetComponent<RectTransform>().sizeDelta;
        }
        if (_splitBaseSize == Vector2.zero && hideSplitPrefab != null) {
            _splitBaseSize = hideSplitPrefab.GetComponent<RectTransform>().sizeDelta;
        }
    }

    public static ScoreHistoryFanTooltip CreateUnderCanvas(Transform nearTransform) {
        Canvas canvas = nearTransform != null ? nearTransform.GetComponentInParent<Canvas>() : null;
        if (canvas == null) {
            canvas = Object.FindFirstObjectByType<Canvas>();
        }
        if (canvas == null) return null;

        var root = new GameObject("ScoreHistoryFanTooltip", typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(canvas.transform, false);
        var panel = new GameObject("Panel", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        panel.transform.SetParent(root.transform, false);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.sizeDelta = new Vector2(520f, 220f);
        var background = panel.GetComponent<UnityEngine.UI.Image>();
        background.color = new Color32(34, 43, 59, 255);
        background.raycastTarget = false;
        var border = panel.AddComponent<UnityEngine.UI.Outline>();
        border.effectColor = new Color32(95, 112, 135, 220);
        border.effectDistance = new Vector2(1f, -1f);
        border.useGraphicAlpha = false;

        var tilesRootGo = new GameObject("TilesRoot", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        tilesRootGo.transform.SetParent(panel.transform, false);
        var tilesRect = tilesRootGo.GetComponent<RectTransform>();
        tilesRect.anchorMin = new Vector2(0f, 1f);
        tilesRect.anchorMax = new Vector2(1f, 1f);
        tilesRect.pivot = new Vector2(0f, 1f);
        tilesRect.anchoredPosition = new Vector2(12f, -12f);
        tilesRect.sizeDelta = new Vector2(-24f, 80f);
        var tilesLayout = tilesRootGo.GetComponent<HorizontalLayoutGroup>();
        tilesLayout.childAlignment = TextAnchor.MiddleLeft;
        tilesLayout.spacing = 0f;
        tilesLayout.childControlWidth = false;
        tilesLayout.childControlHeight = false;
        tilesLayout.childForceExpandWidth = false;
        tilesLayout.childForceExpandHeight = false;

        var allFansGo = CreateTooltipText(panel.transform, "AllFansText", new Vector2(12f, -100f), 18);
        var scoreGo = CreateTooltipText(panel.transform, "ScoreSummaryText", new Vector2(12f, -150f), 16);

        var tooltip = root.AddComponent<ScoreHistoryFanTooltip>();
        tooltip.panelRect = panelRect;
        tooltip.tilesRoot = tilesRect;
        tooltip.allFansText = allFansGo;
        tooltip.scoreSummaryText = scoreGo;
        tooltip.canvasGroup = root.GetComponent<CanvasGroup>();
        root.SetActive(false);
        return tooltip;
    }

    private static TextMeshProUGUI CreateTooltipText(Transform parent, string name, Vector2 anchoredPos, float fontSize) {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = new Vector2(-24f, 40f);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.raycastTarget = false;
        return text;
    }

    private TextMeshProUGUI FindTextByName(string objectName) {
        if (panelRect == null) return null;
        Transform t = panelRect.Find(objectName);
        return t != null ? t.GetComponent<TextMeshProUGUI>() : null;
    }

    public void Show(RoundSettlementSnapshot snapshot, string subRule) {
        if (snapshot == null || !snapshot.CanShowTooltip) return;
        ResolveReferences();
        ClearSpawned();
        EnsureTilesLayout();
        PopulateTiles(snapshot);

        if (allFansText != null) {
            allFansText.text = ScoreHistorySettlementHelper.BuildAllFansText(subRule, snapshot);
        }
        if (scoreSummaryText != null) {
            scoreSummaryText.text = ScoreHistorySettlementHelper.BuildScoreSummaryText(subRule, snapshot);
        }

        FitTextContent();

        gameObject.SetActive(true);
        if (canvasGroup != null) {
            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }
    }

    public void PreviewCell(ScoreHistoryMainFanCell cell, RoundSettlementSnapshot snapshot, string subRule) {
        if (_pinnedCell != null) return;
        ClearCellSelection();
        _hoveredCell = cell;
        cell.SetHighlighted(true);
        Show(snapshot, subRule);
    }

    public void LeaveCell(ScoreHistoryMainFanCell cell) {
        if (_pinnedCell == null && _hoveredCell == cell) Hide();
    }

    public void TogglePinnedCell(ScoreHistoryMainFanCell cell, RoundSettlementSnapshot snapshot, string subRule) {
        if (_pinnedCell == cell) {
            Hide();
            return;
        }
        ClearCellSelection();
        _pinnedCell = cell;
        cell.SetHighlighted(true);
        Show(snapshot, subRule);
    }

    public void ReleaseCell(ScoreHistoryMainFanCell cell) {
        if (_pinnedCell == cell || _hoveredCell == cell) Hide();
    }

    private void ClearCellSelection() {
        if (_hoveredCell != null) _hoveredCell.SetHighlighted(false);
        if (_pinnedCell != null) _pinnedCell.SetHighlighted(false);
        _hoveredCell = null;
        _pinnedCell = null;
    }

    private void OnDisable() => ClearCellSelection();

    /// <summary>根据番种行数收紧面板高度，沿用场景中设置的文本间距和底部留白。</summary>
    private void FitTextContent() {
        if (panelRect == null || allFansText == null || scoreSummaryText == null) return;
        RectTransform fansRect = allFansText.rectTransform;
        RectTransform summaryRect = scoreSummaryText.rectTransform;
        float textGap = Mathf.Max(0f, fansRect.anchoredPosition.y - fansRect.rect.height - summaryRect.anchoredPosition.y);
        float bottomPadding = Mathf.Max(0f, panelRect.rect.height + summaryRect.anchoredPosition.y - summaryRect.rect.height);
        float fansHeight = Mathf.Ceil(Mathf.Max(allFansText.fontSize,
            allFansText.GetPreferredValues(allFansText.text, Mathf.Max(1f, fansRect.rect.width), Mathf.Infinity).y));
        float summaryHeight = Mathf.Ceil(Mathf.Max(scoreSummaryText.fontSize,
            scoreSummaryText.GetPreferredValues(scoreSummaryText.text, Mathf.Max(1f, summaryRect.rect.width), Mathf.Infinity).y));

        fansRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, fansHeight);
        summaryRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, summaryHeight);
        summaryRect.anchoredPosition = new Vector2(summaryRect.anchoredPosition.x,
            fansRect.anchoredPosition.y - fansHeight - textGap);
        panelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
            -summaryRect.anchoredPosition.y + summaryHeight + bottomPadding);
    }

    public void Hide() {
        ClearCellSelection();
        HideImmediate();
    }

    private void HideImmediate() {
        if (canvasGroup != null) {
            canvasGroup.alpha = 0f;
        }
        gameObject.SetActive(false);
    }

    private void EnsureTilesLayout() {
        if (tilesRoot == null) return;
        SettlementTileRowLayout.Ensure(tilesRoot.gameObject);
    }

    /// <summary>与 EndResultPanel.InitializeShowResult 相同顺序：手牌 | 副露 | 和牌张（最右侧）。</summary>
    private void PopulateTiles(RoundSettlementSnapshot snapshot) {
        if (staticCardPrefab == null || tilesRoot == null || !snapshot.CanShowHandPreview) return;
        if (snapshot.hepaiPlayerHand == null || snapshot.hepaiPlayerHand.Length == 0) return;

        int[] hand = (int[])snapshot.hepaiPlayerHand.Clone();
        int lastCard = hand[hand.Length - 1];
        // 与 EndResultPanel 完全对齐：和牌张取数组末位，剩余手牌过滤掉花牌（牌谱重放时花牌可能残留）后排序。
        var concealed = new List<int>(hand.Length - 1);
        for (int i = 0; i < hand.Length - 1; i++) {
            if (IsFlowerTile(hand[i]) && !GuangdongMilRules.IsMil(snapshot.subRule)) continue;
            concealed.Add(hand[i]);
        }
        concealed.Sort(TileIdOrder.Comparer);

        for (int i = 0; i < concealed.Count; i++) {
            SpawnTile(concealed[i]);
        }

        SpawnSplit();

        foreach (var group in SettlementMeldLayoutBuilder.Build(snapshot.combinationMask)) {
            foreach (var tile in group) {
                SpawnTile(tile.FaceDown ? 0 : tile.TileId);
                if (tile.StackedTileId.HasValue) SpawnTile(tile.StackedTileId.Value);
            }
        }
        SpawnSplit();
        SpawnTile(lastCard);
    }

    /// <summary>花牌 id 范围 51~58（春夏秋冬梅兰竹菊），手牌容器不渲染花牌，与 EndResultPanel 一致。</summary>
    private static bool IsFlowerTile(int tileId) {
        return tileId >= 51 && tileId <= 58;
    }

    private void SpawnTile(int tileId) {
        GameObject cardObj = Instantiate(staticCardPrefab, tilesRoot);
        ApplyScaledLayout(cardObj, _cardBaseSize);
        StaticCard card = cardObj.GetComponent<StaticCard>();
        if (card != null) card.SetTileOnlyImage(tileId);
        _spawnedObjects.Add(cardObj);
    }

    private void SpawnSplit() {
        if (hideSplitPrefab == null) return;
        GameObject split = Instantiate(hideSplitPrefab, tilesRoot);
        ApplyScaledLayout(split, _splitBaseSize);
        _spawnedObjects.Add(split);
    }

    private void ApplyScaledLayout(GameObject go, Vector2 baseSize) {
        if (go == null || baseSize == Vector2.zero) return;
        RectTransform rt = go.GetComponent<RectTransform>();
        if (rt == null) return;

        Vector2 scaled = baseSize * cardScale;
        rt.localScale = Vector3.one;
        rt.sizeDelta = scaled;

        LayoutElement layout = go.GetComponent<LayoutElement>();
        if (layout == null) {
            layout = go.AddComponent<LayoutElement>();
        }
        layout.preferredWidth = scaled.x;
        layout.preferredHeight = scaled.y;
        layout.minWidth = scaled.x;
        layout.minHeight = scaled.y;
    }

    private void ClearSpawned() {
        _spawnedObjects.Clear();
        if (tilesRoot != null) {
            for (int i = tilesRoot.childCount - 1; i >= 0; i--) {
                GameObject old = tilesRoot.GetChild(i).gameObject;
                // Destroy 在帧末执行；先禁用，避免快速切换记录时旧牌参与新一轮布局。
                old.SetActive(false);
                Destroy(old);
            }
        }
    }
}
