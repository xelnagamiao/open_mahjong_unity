using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
public class ScoreHistoryPanel : MonoBehaviour, IPointerClickHandler
{
    public static ScoreHistoryPanel Instance { get; private set; }
    [SerializeField] private GameObject Tmp_Text_Prefab;
    [SerializeField] private GameObject RoundIndexContainer;
    [SerializeField] private Transform MainFanContainer;
    [SerializeField] private ScoreHistoryFanTooltip fanTooltip;
    [SerializeField] private TMP_Text player0UserName;
    [SerializeField] private Transform player0RoundScoreContainer;
    [SerializeField] private Transform player0GameScoreContainer;
    [SerializeField] private TMP_Text player1UserName;
    [SerializeField] private Transform player1RoundScoreContainer;
    [SerializeField] private Transform player1GameScoreContainer;
    [SerializeField] private TMP_Text player2UserName;
    [SerializeField] private Transform player2RoundScoreContainer;
    [SerializeField] private Transform player2GameScoreContainer;
    [SerializeField] private TMP_Text player3UserName;
    [SerializeField] private Transform player3RoundScoreContainer;
    [SerializeField] private Transform player3GameScoreContainer;

    [Header("本局分差列颜色")]
    [SerializeField] private Color scoreGainColor = Color.green;
    [SerializeField] private Color scoreLossColor = Color.red;
    [SerializeField] private Color tsumoLossColor = new Color32(0, 128, 255, 255);
    private GameObject dismissArea;
    private Canvas tableCanvas;
    private float borderWidth = -1f;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        EnsureReferences();
        var scrollbar = GetComponentInChildren<UnityEngine.UI.ScrollRect>(true)?.verticalScrollbar;
        if (scrollbar != null) {
            var relay = scrollbar.GetComponent<ScoreHistoryBackgroundClickRelay>();
            if (relay == null) relay = scrollbar.gameObject.AddComponent<ScoreHistoryBackgroundClickRelay>();
            relay.Owner = this;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || eventData.dragging) return;
        if (fanTooltip != null && fanTooltip.IsPinned) fanTooltip.Hide();
    }

    private void OnEnable()
    {
        if (fanTooltip != null) fanTooltip.Hide();
        tableCanvas = GetComponentInParent<Canvas>();
        borderWidth = -1f;
        Canvas.preWillRenderCanvases += RefreshBorderWidths;
        ShowDismissArea();
        if (GameCanvas.Instance != null) GameCanvas.Instance.SetScoreRecordOpen(true);
        if (GameSceneUIManager.Instance == null) return;

        bool recordActive = GameRecordManager.Instance != null
            && GameRecordManager.Instance.gameObject.activeSelf
            && GameRecordManager.Instance.gameRecord != null;
        if (recordActive) {
            GameSceneUIManager.Instance.UpdateScoreRecord();
            return;
        }

        var mgr = NormalGameStateManager.Instance;
        if (mgr == null || (!mgr.IsGameActive && mgr.roundSettlementHistory.Count == 0)) return;
        GameSceneUIManager.Instance.UpdateScoreRecord();
    }

    private void OnDisable()
    {
        Canvas.preWillRenderCanvases -= RefreshBorderWidths;
        if (dismissArea != null) dismissArea.SetActive(false);
        if (fanTooltip != null) fanTooltip.Hide();
        if (GameCanvas.Instance != null) GameCanvas.Instance.SetScoreRecordOpen(false);
    }

    private void RefreshBorderWidths()
    {
        float width = ScoreHistoryCellVisuals.BorderWidth(tableCanvas);
        if (Mathf.Approximately(width, borderWidth)) return;
        borderWidth = width;
        ScoreHistoryCellVisuals.RefreshBorderWidths(transform, width);
    }

    private void OnDestroy()
    {
        if (dismissArea != null) Destroy(dismissArea);
        if (Instance == this) Instance = null;
    }

    private void ShowDismissArea()
    {
        if (transform.parent is not RectTransform) return;
        if (dismissArea == null) {
            dismissArea = new GameObject("ScoreHistoryDismissArea", typeof(RectTransform),
                typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button), typeof(UnityEngine.UI.LayoutElement));
            dismissArea.transform.SetParent(transform.parent, false);
            var rect = (RectTransform)dismissArea.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            dismissArea.GetComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
            var image = dismissArea.GetComponent<UnityEngine.UI.Image>();
            image.color = Color.clear;
            image.raycastTarget = true;
            var button = dismissArea.GetComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            button.transition = UnityEngine.UI.Selectable.Transition.None;
            button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            button.onClick.AddListener(Close);
        }
        // 放在计分板正下方：表内交互照常，表外点击只关闭面板，不穿透到牌桌快捷操作。
        dismissArea.transform.SetAsLastSibling();
        dismissArea.transform.SetSiblingIndex(transform.GetSiblingIndex());
        dismissArea.SetActive(true);
    }

    private void EnsureReferences()
    {
        EnsureMainFanColumnSetup();

        if (fanTooltip == null) {
            fanTooltip = ScoreHistoryFanTooltip.Instance;
        }
        if (fanTooltip == null) {
            fanTooltip = FindFirstObjectByType<ScoreHistoryFanTooltip>(FindObjectsInactive.Include);
        }
        if (fanTooltip == null) {
            fanTooltip = ScoreHistoryFanTooltip.CreateUnderCanvas(transform);
        }
    }

    private static Transform CreateMainFanColumn(Transform roundIndexColumn)
    {
        Transform parent = roundIndexColumn.parent;
        if (parent == null) return null;

        var columnGo = new GameObject("MainFan", typeof(RectTransform));
        Transform column = columnGo.transform;
        column.SetParent(parent, false);

        if (roundIndexColumn.TryGetComponent(out RectTransform srcRect)) {
            RectTransform dst = columnGo.GetComponent<RectTransform>();
            dst.anchorMin = srcRect.anchorMin;
            dst.anchorMax = srcRect.anchorMax;
            dst.pivot = srcRect.pivot;
            dst.sizeDelta = srcRect.sizeDelta;
            dst.anchoredPosition = srcRect.anchoredPosition + new Vector2(srcRect.sizeDelta.x, 0f);
        }

        if (roundIndexColumn.TryGetComponent(out UnityEngine.UI.GridLayoutGroup srcGrid)) {
            var grid = columnGo.AddComponent<UnityEngine.UI.GridLayoutGroup>();
            grid.padding = srcGrid.padding;
            grid.cellSize = srcGrid.cellSize;
            grid.spacing = srcGrid.spacing;
            grid.startCorner = srcGrid.startCorner;
            grid.startAxis = srcGrid.startAxis;
            grid.childAlignment = srcGrid.childAlignment;
            grid.constraint = srcGrid.constraint;
            grid.constraintCount = srcGrid.constraintCount;
        }

        if (roundIndexColumn.TryGetComponent(out UnityEngine.UI.LayoutElement srcLayout)) {
            var layout = columnGo.AddComponent<UnityEngine.UI.LayoutElement>();
            layout.minWidth = srcLayout.minWidth;
            layout.minHeight = srcLayout.minHeight;
            layout.preferredWidth = srcLayout.preferredWidth;
            layout.preferredHeight = srcLayout.preferredHeight;
            layout.flexibleWidth = srcLayout.flexibleWidth;
            layout.flexibleHeight = srcLayout.flexibleHeight;
            layout.ignoreLayout = srcLayout.ignoreLayout;
        }

        column.SetSiblingIndex(roundIndexColumn.GetSiblingIndex() + 1);
        return column;
    }

    /// <summary>
    /// 主番列必须与局数列并列；若误挂在局数列下，清空局数时会连主番格一起销毁。
    /// </summary>
    private void EnsureMainFanColumnSetup()
    {
        if (RoundIndexContainer == null) return;
        Transform roundColumn = RoundIndexContainer.transform;

        if (MainFanContainer != null && MainFanContainer.IsChildOf(roundColumn)) {
            Transform parent = roundColumn.parent;
            if (parent != null) {
                MainFanContainer.SetParent(parent, false);
                MainFanContainer.SetSiblingIndex(roundColumn.GetSiblingIndex() + 1);
            }
        }

        if (MainFanContainer == null) {
            Transform parent = roundColumn.parent;
            if (parent != null) {
                foreach (string name in new[] { "MainFan", "GameMainFan", "MainFanContainer", "主番" }) {
                    Transform found = parent.Find(name);
                    if (found != null && !found.IsChildOf(roundColumn)) {
                        MainFanContainer = found;
                        break;
                    }
                }
            }
            if (MainFanContainer == null) {
                MainFanContainer = CreateMainFanColumn(roundColumn);
            }
        }
    }

    public void Close()
    {
        EnsureMainFanColumnSetup();
        ClearContainer(MainFanContainer);

        if (RoundIndexContainer != null)
        {
            ClearContainer(RoundIndexContainer.transform);
        }

        ClearContainer(player0RoundScoreContainer);
        ClearContainer(player0GameScoreContainer);
        ClearContainer(player1RoundScoreContainer);
        ClearContainer(player1GameScoreContainer);
        ClearContainer(player2RoundScoreContainer);
        ClearContainer(player2GameScoreContainer);
        ClearContainer(player3RoundScoreContainer);
        ClearContainer(player3GameScoreContainer);

        fanTooltip?.Hide();
        gameObject.SetActive(false);
    }

    public void UpdateScoreRecord(string rule, IReadOnlyDictionary<string, PlayerInfoClass> player_to_info)
    {
        UpdateScoreRecord(rule, player_to_info, null);
    }

    public void UpdateScoreRecord(string rule, IReadOnlyDictionary<string, PlayerInfoClass> player_to_info, IReadOnlyList<RoundSettlementSnapshot> roundSettlements, int totalRounds = 0, bool maskPlayerNames = false, string subRuleFallback = null, IReadOnlyList<int> recordRoundIndices = null)
    {
        if (player_to_info == null || player_to_info.Count < 4) return;

        var mgr = NormalGameStateManager.Instance;
        if (roundSettlements == null || roundSettlements.Count == 0) {
            roundSettlements = mgr != null ? mgr.roundSettlementHistory : null;
        }

        // 总局数（用于预测未来局名占位）：优先用调用方传入，其次由实时对局的 maxRound 按族换算（风圈数×4 或本身即局数）
        if (totalRounds <= 0 && mgr != null) {
            totalRounds = RoundTextDictionary.ToTotalHands(mgr.roomRule, mgr.maxRound);
        }

        var sorted = new List<PlayerInfoClass>(player_to_info.Values);
        sorted.Sort((a, b) => a.original_player_index.CompareTo(b.original_player_index));
        List<int> roundNumberHistory = sorted[0].round_number_history ?? new List<int>();

        string ResolveDisplayName(PlayerInfoClass player) {
            string position = null;
            foreach (var kv in player_to_info) {
                if (kv.Value == player) {
                    position = kv.Key;
                    break;
                }
            }
            if (maskPlayerNames) {
                return StreamerModeHelper.FormatGamestatePlayerName(player.username, position, player.userId);
            }
            return player.username;
        }

        InitializeScoreRecord(rule,
            sorted[0].original_player_index, ResolveDisplayName(sorted[0]), sorted[0].score, sorted[0].score_history ?? new List<string>(),
            sorted[1].original_player_index, ResolveDisplayName(sorted[1]), sorted[1].score, sorted[1].score_history ?? new List<string>(),
            sorted[2].original_player_index, ResolveDisplayName(sorted[2]), sorted[2].score, sorted[2].score_history ?? new List<string>(),
            sorted[3].original_player_index, ResolveDisplayName(sorted[3]), sorted[3].score, sorted[3].score_history ?? new List<string>(),
            roundNumberHistory,
            roundSettlements,
            totalRounds,
            subRuleFallback,
            recordRoundIndices);
    }

    public void InitializeScoreRecord(
        string rule,
        int originIndex0, string username0, int absoluteScore0, List<string> scoreHistory0,
        int originIndex1, string username1, int absoluteScore1, List<string> scoreHistory1,
        int originIndex2, string username2, int absoluteScore2, List<string> scoreHistory2,
        int originIndex3, string username3, int absoluteScore3, List<string> scoreHistory3,
        List<int> roundNumberHistory = null,
        IReadOnlyList<RoundSettlementSnapshot> roundSettlements = null,
        int totalRounds = 0,
        string subRuleFallback = null,
        IReadOnlyList<int> recordRoundIndices = null)
    {
        EnsureMainFanColumnSetup();
        if (RoundIndexContainer != null)
        {
            ClearContainer(RoundIndexContainer.transform);
        }
        ClearContainer(MainFanContainer);

        var mgr = NormalGameStateManager.Instance;
        if (roundSettlements == null || roundSettlements.Count == 0) {
            roundSettlements = mgr != null ? mgr.roundSettlementHistory : null;
        }

        if (RuleRegistry.Resolve(rule, rule) == null) {
            Debug.LogError($"未知的规则类型: {rule}");
            return;
        }

        if (string.IsNullOrEmpty(subRuleFallback) && mgr != null) {
            subRuleFallback = mgr.subRule;
        }
        string subRule = ScoreHistorySettlementHelper.ResolveSubRule(rule, subRuleFallback);

        List<int> roundNumbers = roundNumberHistory ?? new List<int>();

        int scoreHistoryCount = scoreHistory0 != null ? scoreHistory0.Count : 0;
        if (scoreHistory1 != null) scoreHistoryCount = Mathf.Max(scoreHistoryCount, scoreHistory1.Count);
        if (scoreHistory2 != null) scoreHistoryCount = Mathf.Max(scoreHistoryCount, scoreHistory2.Count);
        if (scoreHistory3 != null) scoreHistoryCount = Mathf.Max(scoreHistoryCount, scoreHistory3.Count);

        int roundCount = scoreHistoryCount;

        int maxPlayedRoundNumber = 0;
        for (int i = 0; i < roundCount; i++) {
            int roundNumber = ScoreHistorySettlementHelper.ResolveRoundNumberForRow(i, scoreHistoryCount, roundNumbers);
            if (roundNumber > maxPlayedRoundNumber) maxPlayedRoundNumber = roundNumber;
            GameObject textObj = CreateCell(RoundIndexContainer.transform);
            TMP_Text text = textObj.GetComponent<TMP_Text>();
            if (text != null) {
                text.text = RoundTextDictionary.GetRoundName(rule, roundNumber);
                if (recordRoundIndices != null && i < recordRoundIndices.Count) {
                    textObj.AddComponent<ScoreHistoryRoundCell>().Bind(text, recordRoundIndices[i]);
                }
            }

            CreateMainFanCell(i, scoreHistoryCount, roundCount, subRule, roundSettlements);
        }

        // 预测局名占位：计分板尚未自动延伸到的后续局，用灰色局名 + 空白单元格补齐，
        // 与日麻"对局列表依次递增"一致（连庄/错和会出现同一局名多行，预测从已用最大局号+1 起）。
        var predictedRoundNumbers = new List<int>();
        if (totalRounds > 0) {
            for (int rn = maxPlayedRoundNumber + 1; rn <= totalRounds; rn++) {
                predictedRoundNumbers.Add(rn);
            }
        }
        foreach (int rn in predictedRoundNumbers) {
            GameObject textObj = CreateCell(RoundIndexContainer.transform);
            TMP_Text text = textObj.GetComponent<TMP_Text>();
            if (text != null) {
                text.text = $"<color=#C0C0C0>{RoundTextDictionary.GetRoundName(rule, rn)}</color>";
                text.raycastTarget = false;
            }
            AddEmptyCell(MainFanContainer);
        }

        var players = new List<(int originIndex, string username, int absoluteScore, List<string> scoreHistory, TMP_Text userNameText, Transform roundScoreContainer, Transform gameScoreContainer)>
        {
            (originIndex0, username0, absoluteScore0, scoreHistory0 ?? new List<string>(), player0UserName, player0RoundScoreContainer, player0GameScoreContainer),
            (originIndex1, username1, absoluteScore1, scoreHistory1 ?? new List<string>(), player1UserName, player1RoundScoreContainer, player1GameScoreContainer),
            (originIndex2, username2, absoluteScore2, scoreHistory2 ?? new List<string>(), player2UserName, player2RoundScoreContainer, player2GameScoreContainer),
            (originIndex3, username3, absoluteScore3, scoreHistory3 ?? new List<string>(), player3UserName, player3RoundScoreContainer, player3GameScoreContainer)
        };

        players.Sort((a, b) => a.originIndex.CompareTo(b.originIndex));

        foreach (var player in players)
        {
            if (player.userNameText != null)
            {
                player.userNameText.text = player.username;
            }

            ClearContainer(player.roundScoreContainer);
            ClearContainer(player.gameScoreContainer);

            int historySum = SumScoreHistoryDeltas(player.scoreHistory);
            // 当前绝对分已含起手分：起手分 = score - Σ(history)；日麻起手>0 时局差列显示绝对点
            int startingScore = player.absoluteScore - historySum;
            bool showAbsoluteRiichiScores = startingScore > 0;
            int cumulativeScore = startingScore;

            for (int i = 0; i < player.scoreHistory.Count; i++)
            {
                string scoreChange = player.scoreHistory[i];

                int scoreValue = 0;
                if (scoreChange.StartsWith("+"))
                {
                    int.TryParse(scoreChange.Substring(1), out scoreValue);
                }
                else if (scoreChange.StartsWith("-"))
                {
                    int.TryParse(scoreChange.Substring(1), out scoreValue);
                    scoreValue = -scoreValue;
                }
                else
                {
                    int.TryParse(scoreChange, out scoreValue);
                }

                string displayScoreChange = scoreChange;
                if (scoreChange.StartsWith("+") || scoreChange.StartsWith("-"))
                {
                    if (int.TryParse(scoreChange.Substring(1), out int absValue))
                    {
                        displayScoreChange = (scoreChange.StartsWith("+") ? "+" : "-") + absValue.ToString();
                    }
                }
                RoundSettlementSnapshot rowSnapshot = ScoreHistorySettlementHelper.ResolveSettlementForRow(
                    i, player.scoreHistory.Count, roundSettlements);
                GameObject roundScoreObj = CreateCell(player.roundScoreContainer.transform);
                TMP_Text roundScoreText = roundScoreObj.GetComponent<TMP_Text>();
                if (roundScoreText != null)
                {
                    roundScoreText.text = FormatColoredRoundScore(scoreValue, displayScoreChange, rowSnapshot, subRule);
                }

                cumulativeScore += scoreValue;
                GameObject gameScoreObj = CreateCell(player.gameScoreContainer.transform);
                TMP_Text gameScoreText = gameScoreObj.GetComponent<TMP_Text>();
                if (gameScoreText != null)
                {
                    if (showAbsoluteRiichiScores)
                    {
                        gameScoreText.text = cumulativeScore.ToString();
                    }
                    else if (cumulativeScore > 0)
                    {
                        gameScoreText.text = $"+{cumulativeScore}";
                    }
                    else if (cumulativeScore < 0)
                    {
                        gameScoreText.text = cumulativeScore.ToString();
                    }
                    else
                    {
                        gameScoreText.text = "0";
                    }
                }
            }

            // 该家历史短于已结算行数时补空白，保证各列行数一致（与局名列对齐）
            for (int i = player.scoreHistory.Count; i < roundCount; i++) {
                AddEmptyCell(player.roundScoreContainer);
                AddEmptyCell(player.gameScoreContainer);
            }

            // 预测局：分值列留空白占位，仅展示后续局名
            for (int p = 0; p < predictedRoundNumbers.Count; p++) {
                AddEmptyCell(player.roundScoreContainer);
                AddEmptyCell(player.gameScoreContainer);
            }
        }
    }

    private static int SumScoreHistoryDeltas(List<string> history) {
        if (history == null || history.Count == 0) return 0;
        int sum = 0;
        for (int i = 0; i < history.Count; i++) {
            string entry = history[i];
            if (string.IsNullOrEmpty(entry)) continue;
            if (entry.StartsWith("+") || entry.StartsWith("-")) {
                if (int.TryParse(entry.Substring(1), out int abs)) {
                    sum += entry.StartsWith("-") ? -abs : abs;
                }
            } else if (int.TryParse(entry, out int plain)) {
                sum += plain;
            }
        }
        return sum;
    }

    private GameObject CreateCell(Transform container)
    {
        GameObject cell = Instantiate(Tmp_Text_Prefab, container);
        ScoreHistoryCellVisuals.AddBorders(cell.transform);
        TMP_Text text = cell.GetComponent<TMP_Text>();
        if (text != null) {
            text.color = Color.white;
            text.fontSize = 22f;
            text.enableAutoSizing = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.margin = new Vector4(2f, 0f, 2f, 0f);
            text.raycastTarget = false;
        }
        return cell;
    }

    /// <summary>在指定列追加一个空白单元格，用于预测局/补齐行数时保持各列对齐。</summary>
    private void AddEmptyCell(Transform container)
    {
        if (container == null || Tmp_Text_Prefab == null) return;
        GameObject obj = CreateCell(container);
        TMP_Text text = obj.GetComponent<TMP_Text>();
        if (text != null) {
            text.text = "";
            text.raycastTarget = false;
        }
    }

    private void CreateMainFanCell(int roundIndex, int scoreHistoryCount, int roundCount, string subRule, IReadOnlyList<RoundSettlementSnapshot> roundSettlements)
    {
        if (MainFanContainer == null || Tmp_Text_Prefab == null) {
            EnsureMainFanColumnSetup();
            if (MainFanContainer == null || Tmp_Text_Prefab == null) return;
        }

        if (roundSettlements == null || roundSettlements.Count == 0) {
            var mgr = NormalGameStateManager.Instance;
            roundSettlements = mgr != null ? mgr.roundSettlementHistory : null;
        }

        RoundSettlementSnapshot snapshot = ScoreHistorySettlementHelper.ResolveSettlementForRow(
            roundIndex, scoreHistoryCount, roundSettlements);
        if (!string.IsNullOrEmpty(snapshot?.subRule)) {
            subRule = snapshot.subRule;
        }

        GameObject cellObj = CreateCell(MainFanContainer);
        string label = ScoreHistorySettlementHelper.GetMainFanColumnLabel(subRule, snapshot, roundIndex);
        bool canHover = snapshot != null && snapshot.CanShowTooltip;
        TMP_Text text = ScoreHistoryCellTextUtil.ApplyLabel(cellObj, label, canHover);
        if (text == null) return;

        ScoreHistoryFanTooltip tooltip = fanTooltip != null ? fanTooltip : ScoreHistoryFanTooltip.Instance;
        var cell = text.GetComponent<ScoreHistoryMainFanCell>();
        if (cell == null) {
            cell = text.gameObject.AddComponent<ScoreHistoryMainFanCell>();
        }
        cell.Bind(snapshot, label, subRule, tooltip);
    }

    private void ClearContainer(Transform container)
    {
        if (container == null) return;

        for (int i = container.childCount - 1; i >= 0; i--)
        {
            Transform child = container.GetChild(i);
            if (child != null)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }
    }

    private string FormatColoredRoundScore(int scoreValue, string displayScoreChange, RoundSettlementSnapshot snapshot, string subRule) {
        if (scoreValue > 0) {
            return $"<color={ColorToTmpHex(scoreGainColor)}>{displayScoreChange}</color>";
        }
        if (scoreValue < 0) {
            Color lossColor = scoreLossColor;
            if (RuleRegistry.Resolve(subRule, subRule)?.ScoreboardHighlightsTsumoLoss != false && IsTsumoLossRound(snapshot)) {
                lossColor = tsumoLossColor;
            }
            return $"<color={ColorToTmpHex(lossColor)}>{displayScoreChange}</color>";
        }
        return displayScoreChange;
    }

    private static bool IsTsumoLossRound(RoundSettlementSnapshot snapshot) {
        return snapshot != null
            && snapshot.hasWin
            && !snapshot.isLiuju
            && snapshot.huClass == "hu_self";
    }

    private static string ColorToTmpHex(Color color) {
        return "#" + ColorUtility.ToHtmlStringRGB(color);
    }
}

// Scrollbar 的按下事件由自身接收，补上同一对象的点击转发，不影响拖拽滚动。
internal sealed class ScoreHistoryBackgroundClickRelay : MonoBehaviour, IPointerClickHandler {
    public ScoreHistoryPanel Owner { get; set; }

    public void OnPointerClick(PointerEventData eventData) => Owner?.OnPointerClick(eventData);
}

internal static class ScoreHistoryCellTextUtil {
    public static TMP_Text ApplyLabel(GameObject cellRoot, string label, bool raycastTarget) {
        if (cellRoot == null) return null;
        TMP_Text text = cellRoot.GetComponent<TMP_Text>();
        if (text == null) text = cellRoot.GetComponentInChildren<TMP_Text>(true);
        if (text == null) {
            Debug.LogWarning($"[ScoreHistory] 单元格 prefab 上未找到 TMP_Text：{cellRoot.name}");
            return null;
        }
        text.text = label ?? "";
        text.raycastTarget = raycastTarget;
        text.enabled = true;
        if (text.color.a < 0.01f) {
            Color c = text.color;
            c.a = 1f;
            text.color = c;
        }
        return text;
    }
}
