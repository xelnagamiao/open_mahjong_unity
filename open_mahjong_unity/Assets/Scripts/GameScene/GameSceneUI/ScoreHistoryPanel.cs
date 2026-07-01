using System.Collections.Generic;
using UnityEngine;
using TMPro;
public class ScoreHistoryPanel : MonoBehaviour
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
    [SerializeField] private Color tsumoLossColor = Color.blue;

    private static readonly Dictionary<string, Dictionary<int, string>> RuleToRoundMap = new Dictionary<string, Dictionary<int, string>> {
        { "guobiao", RoundTextDictionary.CurrentRoundTextGB },
        { "qingque", RoundTextDictionary.CurrentRoundTextQingque },
        { "riichi", RoundTextDictionary.CurrentRoundTextRiichi },
        { "classical", RoundTextDictionary.CurrentRoundTextClassical },
        { "sichuan", RoundTextDictionary.CurrentRoundTextSichuan },
    };

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
    }

    private void OnEnable()
    {
        if (GameSceneUIManager.Instance == null) return;

        bool recordActive = GameRecordManager.Instance != null
            && GameRecordManager.Instance.gameObject.activeSelf
            && GameRecordManager.Instance.gameRecord != null;
        if (recordActive) {
            GameSceneUIManager.Instance.UpdateScoreRecord();
            return;
        }

        var mgr = NormalGameStateManager.Instance;
        if (mgr == null) return;
        if (!mgr.IsGameActive && mgr.roundSettlementHistory.Count == 0) return;
        GameSceneUIManager.Instance.UpdateScoreRecord();
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

    public void UpdateScoreRecord(string rule, IReadOnlyDictionary<string, PlayerInfoClass> player_to_info, IReadOnlyList<RoundSettlementSnapshot> roundSettlements, int totalRounds = 0, bool maskPlayerNames = false, int[] startingScoresByOriginal = null)
    {
        if (player_to_info == null || player_to_info.Count < 4) return;

        if (roundSettlements == null || roundSettlements.Count == 0) {
            roundSettlements = NormalGameStateManager.Instance?.roundSettlementHistory;
        }

        // 总局数（用于预测未来局名占位）：优先用调用方传入，其次回退到实时对局的 maxRound（风圈数）*4
        if (totalRounds <= 0) {
            var mgr = NormalGameStateManager.Instance;
            if (mgr != null && mgr.maxRound > 0) {
                totalRounds = mgr.maxRound * 4;
            }
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
            sorted[0].original_player_index, ResolveDisplayName(sorted[0]), sorted[0].score_history ?? new List<string>(),
            sorted[1].original_player_index, ResolveDisplayName(sorted[1]), sorted[1].score_history ?? new List<string>(),
            sorted[2].original_player_index, ResolveDisplayName(sorted[2]), sorted[2].score_history ?? new List<string>(),
            sorted[3].original_player_index, ResolveDisplayName(sorted[3]), sorted[3].score_history ?? new List<string>(),
            roundNumberHistory,
            roundSettlements,
            totalRounds,
            startingScoresByOriginal);
    }

    public void InitializeScoreRecord(
        string rule,
        int originIndex0, string username0, List<string> scoreHistory0,
        int originIndex1, string username1, List<string> scoreHistory1,
        int originIndex2, string username2, List<string> scoreHistory2,
        int originIndex3, string username3, List<string> scoreHistory3,
        List<int> roundNumberHistory = null,
        IReadOnlyList<RoundSettlementSnapshot> roundSettlements = null,
        int totalRounds = 0,
        int[] startingScoresByOriginal = null)
    {
        EnsureMainFanColumnSetup();
        if (RoundIndexContainer != null)
        {
            ClearContainer(RoundIndexContainer.transform);
        }
        ClearContainer(MainFanContainer);

        if (roundSettlements == null || roundSettlements.Count == 0) {
            roundSettlements = NormalGameStateManager.Instance?.roundSettlementHistory;
        }

        string baseRule = rule ?? "";
        foreach (var kv in RuleToRoundMap) {
            if (baseRule.StartsWith(kv.Key)) { baseRule = kv.Key; break; }
        }

        if (!RuleToRoundMap.TryGetValue(baseRule, out Dictionary<int, string> roundMap)) {
            Debug.LogError($"未知的规则类型: {rule}");
            return;
        }

        string subRule = ScoreHistorySettlementHelper.ResolveSubRule(
            rule,
            NormalGameStateManager.Instance != null ? NormalGameStateManager.Instance.subRule : null);

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
            GameObject textObj = Instantiate(Tmp_Text_Prefab, RoundIndexContainer.transform);
            TMP_Text text = textObj.GetComponent<TMP_Text>();
            if (text != null) {
                text.text = roundMap.TryGetValue(roundNumber, out string label) ? label : $"第{roundNumber}局";
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
            GameObject textObj = Instantiate(Tmp_Text_Prefab, RoundIndexContainer.transform);
            TMP_Text text = textObj.GetComponent<TMP_Text>();
            if (text != null) {
                string label = roundMap.TryGetValue(rn, out string mapped) ? mapped : $"第{rn}局";
                text.text = $"<color=#7A7A7A>{label}</color>";
                text.raycastTarget = false;
            }
            AddEmptyCell(MainFanContainer);
        }

        var players = new List<(int originIndex, string username, List<string> scoreHistory, TMP_Text userNameText, Transform roundScoreContainer, Transform gameScoreContainer)>
        {
            (originIndex0, username0, scoreHistory0 ?? new List<string>(), player0UserName, player0RoundScoreContainer, player0GameScoreContainer),
            (originIndex1, username1, scoreHistory1 ?? new List<string>(), player1UserName, player1RoundScoreContainer, player1GameScoreContainer),
            (originIndex2, username2, scoreHistory2 ?? new List<string>(), player2UserName, player2RoundScoreContainer, player2GameScoreContainer),
            (originIndex3, username3, scoreHistory3 ?? new List<string>(), player3UserName, player3RoundScoreContainer, player3GameScoreContainer)
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

            int startingScore = (startingScoresByOriginal != null
                && player.originIndex >= 0
                && player.originIndex < startingScoresByOriginal.Length)
                ? startingScoresByOriginal[player.originIndex]
                : 0;
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
                GameObject roundScoreObj = Instantiate(Tmp_Text_Prefab, player.roundScoreContainer.transform);
                TMP_Text roundScoreText = roundScoreObj.GetComponent<TMP_Text>();
                if (roundScoreText != null)
                {
                    roundScoreText.text = FormatColoredRoundScore(scoreValue, displayScoreChange, rowSnapshot, subRule);
                }

                cumulativeScore += scoreValue;
                GameObject gameScoreObj = Instantiate(Tmp_Text_Prefab, player.gameScoreContainer.transform);
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

    /// <summary>在指定列追加一个空白单元格，用于预测局/补齐行数时保持各列对齐。</summary>
    private void AddEmptyCell(Transform container)
    {
        if (container == null || Tmp_Text_Prefab == null) return;
        GameObject obj = Instantiate(Tmp_Text_Prefab, container);
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
            roundSettlements = NormalGameStateManager.Instance?.roundSettlementHistory;
        }

        RoundSettlementSnapshot snapshot = ScoreHistorySettlementHelper.ResolveSettlementForRow(
            roundIndex, scoreHistoryCount, roundSettlements);
        if (!string.IsNullOrEmpty(snapshot?.subRule)) {
            subRule = snapshot.subRule;
        }

        GameObject cellObj = Instantiate(Tmp_Text_Prefab, MainFanContainer);
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
            if (!IsSichuanSubRule(subRule) && IsTsumoLossRound(snapshot)) {
                lossColor = tsumoLossColor;
            }
            return $"<color={ColorToTmpHex(lossColor)}>{displayScoreChange}</color>";
        }
        return displayScoreChange;
    }

    private static bool IsSichuanSubRule(string subRule) {
        return subRule != null && subRule.StartsWith("sichuan");
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
