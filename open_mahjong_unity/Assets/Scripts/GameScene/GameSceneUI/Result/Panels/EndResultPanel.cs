using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System;
using UnityEngine.UI;

public class EndResultPanel : MonoBehaviour {
    public enum EndResultTileLayout {
        /// <summary>国标和牌：暗手 + 分隔 + 副露 + 分隔 + 和牌张。</summary>
        HuWithWinTile,
        /// <summary>查叫：手牌 + 分隔 + 副露（无和牌张）。</summary>
        ClosedHandWithMelds,
    }
    [SerializeField] private GameObject FanCountPrefab;
    [SerializeField] private Transform FanCountContainer;

    [SerializeField] private TextMeshProUGUI SelfUserName;
    [SerializeField] private TextMeshProUGUI SelfScore;
    [SerializeField] private Image SelfReady;
    [SerializeField] private TextMeshProUGUI LeftUserName;
    [SerializeField] private TextMeshProUGUI LeftScore;
    [SerializeField] private Image LeftReady;
    [SerializeField] private TextMeshProUGUI TopUserName;
    [SerializeField] private TextMeshProUGUI TopScore;
    [SerializeField] private Image TopReady;
    [SerializeField] private TextMeshProUGUI RightUserName;
    [SerializeField] private TextMeshProUGUI RightScore;
    [SerializeField] private Image RightReady;

    [SerializeField] private TextMeshProUGUI EndButtonText;
    [SerializeField] private Button EndButton;

    [SerializeField] private GameObject EndTilescontainer;
    [SerializeField] private GameObject StaticCardPrefab;
    [SerializeField] private GameObject HideSplit;

    [Header("总计面板")]
    [SerializeField] private GameObject FanCountTotalPanel;
    [SerializeField] private TextMeshProUGUI TotalFu;
    [SerializeField] private TextMeshProUGUI TotalFan;
    [SerializeField] private TextMeshProUGUI TotalScore;
    [SerializeField] private TextMeshProUGUI TotalLimitDisplay;

    [Header("立直麻将结算扩展（可选，仅 riichi 规则显示）")]
    [SerializeField] private GameObject RiichiPanel;
    [Tooltip("宝牌指示槽位（手动拖入 StaticCard）。未翻开位置显示牌背 0。")]
    [SerializeField] private StaticCard[] RiichiDoraSlots;
    [Tooltip("里宝牌指示槽位（手动拖入 StaticCard）。未翻开位置显示牌背 0。")]
    [SerializeField] private StaticCard[] RiichiUraDoraSlots;

    [Header("国标局终亮杠（默认隐藏）")]
    [SerializeField] private TextMeshProUGUI guobiaoAngangCheckText;

    [Header("玩家面板背景（按状态着色：正常蓝/准备橙/被检查深红）")]
    [Tooltip("各座位的面板背景 Image。未拖入时回退到旧版 Ready 图标显隐。")]
    [SerializeField] private Image SelfPanelBackground;
    [SerializeField] private Image LeftPanelBackground;
    [SerializeField] private Image TopPanelBackground;
    [SerializeField] private Image RightPanelBackground;

    [Header("玩家面板背景配色（可在 Inspector 自由调整）")]
    [Tooltip("正常状态：默认蓝")]
    [SerializeField] private Color SeatColorNormal = new Color(0.20f, 0.45f, 0.85f, 1f);
    [Tooltip("已准备状态：默认橙")]
    [SerializeField] private Color SeatColorReady = new Color(0.95f, 0.55f, 0.10f, 1f);
    [Tooltip("被检查状态（川麻查牌/查叫）：默认深红")]
    [SerializeField] private Color SeatColorChecked = new Color(0.62f, 0.09f, 0.11f, 1f);

    private enum SeatVisual { Normal, Ready, Checked }

    // 最近一次准备状态缓存（按 player_index），用于面板重建/服务端逐步驱动时保持准备色不丢失
    private readonly Dictionary<int, bool> cachedReadyStatus = new Dictionary<int, bool>();
    // 当前“被检查”的座位 player_index（川麻终局逐家查牌/查叫高亮）；-1 表示无
    private int checkedFocusSeat = -1;
    // 当前查叫面板是否含退税（用于补“退税”标签与延长停留）
    private bool chajiaoHasRefund = false;

    public static EndResultPanel Instance { get; private set; }
    public GameObject CardPrefab => StaticCardPrefab;
    public GameObject HideSplitPrefab => HideSplit;
    private const string StateNone = "";
    private const string StateGame = "gamestate";
    private const string StateRecord = "recordstate";
    private string currentState = StateNone;
    private Coroutine showResultCoroutine;

    public bool IsAwaitingRecordResultConfirm => currentState == StateRecord && gameObject.activeSelf;

    /// <summary>中断番种渐显/确认倒计时等局内结算协程（四川终局步间切换时调用）。</summary>
    public void StopActivePresentation() {
        if (showResultCoroutine != null) {
            StopCoroutine(showResultCoroutine);
            showResultCoroutine = null;
        }
    }

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // 非激活状态按钮
        EndButton.onClick.AddListener(EndButtonClick);
        EndButton.interactable = false;
    }

    public void StartShowResult(int hepai_player_index, Dictionary<int, int> player_to_score, int hu_score, string[] hu_fan, string hu_class, int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask, int? base_fu = null, string[] fu_fan_list = null, RiichiEndResultExtras riichiExtras = null) {
        if (showResultCoroutine != null) {
            StopCoroutine(showResultCoroutine);
            showResultCoroutine = null;
        }
        InitializeShowResult(hepai_player_index, player_to_score, hu_score, hu_fan, hu_class, hepai_player_hand, hepai_player_huapai, hepai_player_combination_mask, riichiExtras);
        showResultCoroutine = StartCoroutine(PlayShowResultRoutine(hu_score, hu_fan, base_fu, fu_fan_list, riichiExtras,
            RoundEndTiming.HuConfirmCountdownSeconds, resumeSichuanContinueAfterClose: false));
    }

    public void PrepareShowResult(int hepai_player_index, Dictionary<int, int> player_to_score, int hu_score, string[] hu_fan, string hu_class, int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask, RiichiEndResultExtras riichiExtras = null, Dictionary<int, int> scoreChanges = null, bool suppressHandReveal = false, EndResultTileLayout tileLayout = EndResultTileLayout.HuWithWinTile) {
        if (showResultCoroutine != null) {
            StopCoroutine(showResultCoroutine);
            showResultCoroutine = null;
        }
        InitializeShowResult(hepai_player_index, player_to_score, hu_score, hu_fan, hu_class, hepai_player_hand, hepai_player_huapai, hepai_player_combination_mask, riichiExtras, scoreChanges, suppressHandReveal, tileLayout);
    }

    public void PlayPreparedShowResult(int hu_score, string[] hu_fan, int? base_fu = null, string[] fu_fan_list = null, RiichiEndResultExtras riichiExtras = null) {
        PlayPreparedShowResult(hu_score, hu_fan, base_fu, fu_fan_list, riichiExtras,
            RoundEndTiming.HuConfirmCountdownSeconds, resumeSichuanContinueAfterClose: false);
    }

    public void PlayPreparedShowResult(int hu_score, string[] hu_fan, int? base_fu, string[] fu_fan_list,
        RiichiEndResultExtras riichiExtras, float confirmCountdownSeconds, bool resumeSichuanContinueAfterClose,
        bool allowConfirmClick = true) {
        if (showResultCoroutine != null) {
            StopCoroutine(showResultCoroutine);
            showResultCoroutine = null;
        }
        showResultCoroutine = StartCoroutine(PlayShowResultRoutine(
            hu_score, hu_fan, base_fu, fu_fan_list, riichiExtras, confirmCountdownSeconds,
            resumeSichuanContinueAfterClose, allowConfirmClick));
    }

    /// <summary>四川终局 settle_hu：准备面板（手牌+副露+和牌张，与国标容器一致）。</summary>
    public void PrepareSichuanSettleHuSingle(
        int hepaiPlayerIndex,
        Dictionary<int, int> player_to_score,
        int huScore,
        string[] huFan,
        string huClass,
        int[] hepaiPlayerHand,
        int[][] hepaiPlayerCombinationMask,
        Dictionary<int, int> scoreChanges) {
        InitializeShowResult(
            hepaiPlayerIndex, player_to_score, huScore, huFan, huClass,
            hepaiPlayerHand, null, hepaiPlayerCombinationMask, null, scoreChanges,
            suppressHandReveal: false, EndResultTileLayout.HuWithWinTile);
        // 川麻终局逐家查牌：高亮当前正在检查的和牌玩家面板（深红）
        SetCheckedFocusSeat(hepaiPlayerIndex);
    }

    public IEnumerator CoPlaySichuanSettleHuRoutine(int huScore, string[] huFan, bool isFinalPanel) {
        if (showResultCoroutine != null) {
            StopCoroutine(showResultCoroutine);
            showResultCoroutine = null;
        }
        showResultCoroutine = StartCoroutine(PlayShowResultRoutine(
            huScore, huFan, null, null, null,
            RoundEndTiming.HuConfirmCountdownSeconds,
            resumeSichuanContinueAfterClose: false,
            allowConfirmClick: isFinalPanel,
            skipConfirmCountdown: !isFinalPanel));
        yield return showResultCoroutine;
        showResultCoroutine = null;
    }

    /// <summary>四川查叫：准备面板（手牌+副露，无和牌张）。</summary>
    public void PrepareSichuanChajiaoSingle(
        int focusPlayerIndex,
        string statusKey,
        int[] hand,
        int[][] combinationMask,
        Dictionary<int, int> player_to_score,
        Dictionary<int, int> scoreChanges,
        bool isFinalPanel = false,
        bool hasRefund = false) {
        chajiaoHasRefund = hasRefund;
        InitializeShowResult(
            focusPlayerIndex, player_to_score, 0, System.Array.Empty<string>(), "liuju",
            hand, null, combinationMask, null, scoreChanges,
            suppressHandReveal: hand == null || hand.Length == 0,
            EndResultTileLayout.ClosedHandWithMelds);
        // 川麻终局逐家查叫：高亮当前正在检查的未和玩家面板（深红）
        SetCheckedFocusSeat(focusPlayerIndex);
        // 与面板显示同步：确认按钮只在末步出现，避免渐显期间“先现后隐”的闪烁
        EndButton.gameObject.SetActive(isFinalPanel);
        EndButton.interactable = false;
    }

    public IEnumerator CoPlaySichuanChajiaoRoutine(string statusKey, bool isFinalPanel, bool hasRefund = false) {
        chajiaoHasRefund = hasRefund;
        yield return CoPlaySichuanChajiaoStatusAndCountdown(statusKey, isFinalPanel);
    }

    public void PrepareSichuanChaRefundSingle(
        Dictionary<int, int> player_to_score,
        Dictionary<int, int> scoreChanges) {
        currentState = StateGame;
        gameObject.SetActive(true);
        EndButton.gameObject.SetActive(false);
        EndButton.interactable = false;
        EndButtonText.text = "确定";
        FanCountTotalPanel.SetActive(false);

        foreach (Transform child in EndTilescontainer.transform) Destroy(child.gameObject);
        foreach (Transform child in FanCountContainer) Destroy(child.gameObject);

        ShowRiichiExtrasPanel(NormalGameStateManager.Instance.subRule, null);
        GuobiaoAngangCheck.Clear(guobiaoAngangCheckText);
        SelfReady.gameObject.SetActive(false);
        LeftReady.gameObject.SetActive(false);
        TopReady.gameObject.SetActive(false);
        RightReady.gameObject.SetActive(false);

        ApplyScoreChangesToPanel(player_to_score, scoreChanges);

        GameObject statusFanInstance = Instantiate(FanCountPrefab, FanCountContainer);
        FanCount statusFanCount = statusFanInstance.GetComponent<FanCount>();
        if (statusFanCount != null) {
            statusFanCount.SetFanCount("刮风下雨", "退税");
            statusFanCount.ApplyFanColor();
        }
    }

    public IEnumerator CoPlaySichuanChaRefundRoutine(bool isFinalPanel) {
        yield return CoPlaySichuanChaRefundCountdown(isFinalPanel);
    }

    public void StartRecordResult(int hepai_player_index, int hu_score, string[] hu_fan, string hu_class, string roomType,
        Dictionary<int, string> indexToPosition, Dictionary<string, string> positionToUsername,
        int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask,
        Dictionary<int, int> player_to_score_before, Dictionary<int, int> player_to_score_after, bool isSpectator = false,
        int? base_fu = null, string[] fu_fan_list = null, RiichiEndResultExtras riichiExtras = null) {
        if (showResultCoroutine != null) {
            StopCoroutine(showResultCoroutine);
            showResultCoroutine = null;
        }
        DisplayRecordResult(hepai_player_index, hu_score, hu_fan, hu_class, roomType,
            indexToPosition, positionToUsername, hepai_player_hand, hepai_player_huapai, hepai_player_combination_mask,
            player_to_score_before, player_to_score_after, isSpectator, base_fu, fu_fan_list, riichiExtras);
    }

    public IEnumerator ShowResult(int hepai_player_index, Dictionary<int, int> player_to_score, int hu_score, string[] hu_fan, string hu_class, int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask, int? base_fu = null, string[] fu_fan_list = null, RiichiEndResultExtras riichiExtras = null) {
        InitializeShowResult(hepai_player_index, player_to_score, hu_score, hu_fan, hu_class, hepai_player_hand, hepai_player_huapai, hepai_player_combination_mask, riichiExtras);
        yield return PlayShowResultRoutine(hu_score, hu_fan, base_fu, fu_fan_list, riichiExtras,
            RoundEndTiming.HuConfirmCountdownSeconds, resumeSichuanContinueAfterClose: false);
    }

    public void InitializeShowResult(int hepai_player_index, Dictionary<int, int> player_to_score, int hu_score, string[] hu_fan, string hu_class, int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask, RiichiEndResultExtras riichiExtras = null, Dictionary<int, int> scoreChanges = null, bool suppressHandReveal = false, EndResultTileLayout tileLayout = EndResultTileLayout.HuWithWinTile) {
        currentState = StateGame;
        gameObject.SetActive(true);
        FanCountTotalPanel.SetActive(false);
        EndButton.gameObject.SetActive(true);
        EndButton.interactable = false;
        EndButtonText.text = "确定";

        foreach (Transform child in EndTilescontainer.transform) {
            Destroy(child.gameObject);
        }
        foreach (Transform child in FanCountContainer) {
            Destroy(child.gameObject);
        }

        // 立直规则：和牌画面出现的瞬间立刻翻开宝牌/里宝牌（含里宝来自立直家），其余槽位仍渲染牌背 0
        string roomRuleForFan = NormalGameStateManager.Instance.subRule;
        ShowRiichiExtrasPanel(roomRuleForFan, riichiExtras);
        GuobiaoAngangCheck.Apply(guobiaoAngangCheckText, NormalGameStateManager.Instance?.lastGuobiaoEndExtras, hu_fan);
        TryPlayGongHuSound(roomRuleForFan, hu_fan);

        // 面板重建：清除被检查高亮，按缓存准备状态重绘座位配色（川麻 Prepare 会随后设置被检查座位）
        checkedFocusSeat = -1;
        ApplySeatVisuals();

        bool showHandInPanel = !suppressHandReveal
            && hepai_player_hand != null && hepai_player_hand.Length > 0;

        if (showHandInPanel) {
            PopulateEndTilesContainer(hepai_player_hand, hepai_player_combination_mask, tileLayout);
        }

        // 显示玩家分数变化
        var effectiveScoreChanges = scoreChanges ?? riichiExtras?.ScoreChanges;
        foreach (var kvp in NormalGameStateManager.Instance.indexToPosition) {
            string pos = kvp.Value;
            int seatIdx = kvp.Key;
            if (!NormalGameStateManager.Instance.player_to_info.TryGetValue(pos, out var playerInfo)) continue;
            int origIdx = playerInfo.original_player_index;
            int beforeScore = playerInfo.score;
            ShowResultPlayerScoreResolver.ResolveScoreChange(
                beforeScore, seatIdx, origIdx,
                effectiveScoreChanges, player_to_score, out int afterScore);
            playerInfo.score = afterScore;
            string scoreText = FormatScoreWithDiff(beforeScore, afterScore);
            string displayName = StreamerModeHelper.FormatGamestatePlayerName(
                playerInfo.username, pos, playerInfo.userId);
            if (pos == "self") {
                SelfUserName.text = displayName;
                SelfScore.text = scoreText;
            } else if (pos == "left") {
                LeftUserName.text = displayName;
                LeftScore.text = scoreText;
            } else if (pos == "top") {
                TopUserName.text = displayName;
                TopScore.text = scoreText;
            } else if (pos == "right") {
                RightUserName.text = displayName;
                RightScore.text = scoreText;
            }
        }

        // 修改计分板
        if (player_to_score != null) {
            var scoreBySeat = new Dictionary<int, int>();
            foreach (var kvp in NormalGameStateManager.Instance.indexToPosition) {
                string pos = kvp.Value;
                int seatIdx = kvp.Key;
                if (!NormalGameStateManager.Instance.player_to_info.TryGetValue(pos, out var playerInfo)) continue;
                if (ShowResultPlayerScoreResolver.TryGetAfterScore(player_to_score, seatIdx, playerInfo.original_player_index, out int score)) {
                    scoreBySeat[seatIdx] = score;
                }
            }
            BoardCanvas.Instance.UpdatePlayerScores(scoreBySeat, NormalGameStateManager.Instance.indexToPosition);
        }
    }

    private IEnumerator PlayShowResultRoutine(int hu_score, string[] hu_fan, int? base_fu = null, string[] fu_fan_list = null,
        RiichiEndResultExtras riichiExtras = null, float confirmCountdownSeconds = -1f, bool resumeSichuanContinueAfterClose = false,
        bool allowConfirmClick = true, bool skipConfirmCountdown = false) {
        if (confirmCountdownSeconds < 0f) {
            confirmCountdownSeconds = RoundEndTiming.HuConfirmCountdownSeconds;
        }
        // 显示番数
        string roomRuleForFan = NormalGameStateManager.Instance.subRule;
        bool isClassical = roomRuleForFan == "classical/standard";
        bool isRiichi = roomRuleForFan != null && roomRuleForFan.StartsWith("riichi");

        if (isClassical && fu_fan_list != null) {
            // 古典麻将：先显示副番列表
            for (int i = 0; i < fu_fan_list.Length; i++) {
                yield return new WaitForSeconds(RoundEndTiming.HuFanRevealIntervalSeconds);
                string fuName = fu_fan_list[i];
                string fuDisplay = FanTextDictionary.GetFuDisplayText(fuName);
                string fuNameDisplay = FanTextDictionary.GetFuNameDisplayText(fuName);
                GameObject fuInstance = Instantiate(FanCountPrefab, FanCountContainer);
                FanCount fuCount = fuInstance.GetComponent<FanCount>();
                if (fuCount != null) {
                    fuCount.SetFanCount(fuNameDisplay, fuDisplay);
                    fuCount.ApplyFuColor();
                }
            }
        }

        for (int i = 0; i < hu_fan.Length; i++) {
            yield return new WaitForSeconds(RoundEndTiming.HuFanRevealIntervalSeconds);
            string fanKey = hu_fan[i];
            string fanDisplay = FanTextDictionary.GetFanDisplayText(roomRuleForFan, fanKey);
            string fanLabel = isRiichi
                ? FanTextDictionary.GetRiichiYakuDisplayName(fanKey)
                : FanTextDictionary.GetFanNameDisplayText(roomRuleForFan, fanKey);
            GameObject fanCountInstance = Instantiate(FanCountPrefab, FanCountContainer);
            FanCount fanCount = fanCountInstance.GetComponent<FanCount>();
            if (fanCount != null) {
                fanCount.SetFanCount(fanLabel, fanDisplay);
                fanCount.ApplyFanColor();
            }
        }

        yield return new WaitForSeconds(RoundEndTiming.HuBeforeTotalPanelSeconds);
        ShowTotalPanel(roomRuleForFan, hu_score, hu_fan, base_fu, riichiExtras);

        if (skipConfirmCountdown) {
            yield return CoPlayEndButtonCountdown(RoundEndTiming.SichuanMidPanelConfirmSeconds, allowConfirmClick: false);
            yield break;
        }

        yield return CoPlayEndButtonCountdown(confirmCountdownSeconds, allowConfirmClick);
        if (resumeSichuanContinueAfterClose && currentState == StateGame && NormalGameStateManager.Instance != null) {
            NormalGameStateManager.Instance.TryResumeAfterSichuanContinue();
        }
    }

    /// <summary>确定按钮倒计时：始终显示数字；仅 allowConfirmClick 时可点击。</summary>
    private IEnumerator CoPlayEndButtonCountdown(float confirmCountdownSeconds, bool allowConfirmClick) {
        EndButton.gameObject.SetActive(true);
        EndButton.interactable = false;
        int countdown = Mathf.Max(1, Mathf.RoundToInt(confirmCountdownSeconds));
        for (int i = countdown; i > 0; i--) {
            EndButtonText.text = $"确定({i})";
            EndButton.interactable = allowConfirmClick;
            yield return new WaitForSeconds(1f);
        }
        EndButtonText.text = "确定(0)";
        EndButton.interactable = false;
        // 可点击确认的对局结算：面板保留至下一局 game_start 由 InitGameStart 清理
        if (!allowConfirmClick) {
            gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 牌谱回放结算展示（不依赖对局内 player_to_score/手牌数据）：
    /// - 默认除和牌者外，其他玩家显示为已准备
    /// - 同步展示后显示「确认」按钮；观战模式下不显示确认，由 end tick 驱动下一局
    /// </summary>
    public void DisplayRecordResult(int hepai_player_index, int hu_score, string[] hu_fan, string hu_class, string roomType,
        Dictionary<int, string> indexToPosition, Dictionary<string, string> positionToUsername,
        int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask,
        Dictionary<int, int> player_to_score_before, Dictionary<int, int> player_to_score_after, bool isSpectator = false,
        int? base_fu = null, string[] fu_fan_list = null, RiichiEndResultExtras riichiExtras = null) {
        currentState = StateRecord;
        gameObject.SetActive(true);
        FanCountTotalPanel.SetActive(false);

        // 清空旧内容，避免与上一条结算叠加
        foreach (Transform child in EndTilescontainer.transform) {
            Destroy(child.gameObject);
        }
        foreach (Transform child in FanCountContainer) {
            Destroy(child.gameObject);
        }

        // 用户名
        SelfUserName.text = positionToUsername != null && positionToUsername.ContainsKey("self") ? positionToUsername["self"] : "";
        LeftUserName.text = positionToUsername != null && positionToUsername.ContainsKey("left") ? positionToUsername["left"] : "";
        TopUserName.text = positionToUsername != null && positionToUsername.ContainsKey("top") ? positionToUsername["top"] : "";
        RightUserName.text = positionToUsername != null && positionToUsername.ContainsKey("right") ? positionToUsername["right"] : "";

        // 回放模式默认全员未准备
        SelfReady.gameObject.SetActive(false);
        LeftReady.gameObject.SetActive(false);
        TopReady.gameObject.SetActive(false);
        RightReady.gameObject.SetActive(false);

        // 显示和牌玩家手牌（与实时对局 PopulateEndTilesContainer 一致）
        if (hepai_player_hand != null && hepai_player_hand.Length > 0) {
            PopulateEndTilesContainer(hepai_player_hand, hepai_player_combination_mask, EndResultTileLayout.HuWithWinTile);
        }

        // 显示各玩家分数变化
        if (player_to_score_after != null && player_to_score_after.Count > 0) {
            foreach (var player in player_to_score_after) {
                if (!indexToPosition.ContainsKey(player.Key)) continue;
                string position = indexToPosition[player.Key];
                int scoreBefore = player_to_score_before != null && player_to_score_before.ContainsKey(player.Key) ? player_to_score_before[player.Key] : 0;
                int scoreAfter = player.Value;
                string scoreText = FormatScoreWithDiff(scoreBefore, scoreAfter);

                if (position == "self") SelfScore.text = scoreText;
                else if (position == "left") LeftScore.text = scoreText;
                else if (position == "top") TopScore.text = scoreText;
                else if (position == "right") RightScore.text = scoreText;
            }
        } else {
            SelfScore.text = "";
            LeftScore.text = "";
            TopScore.text = "";
            RightScore.text = "";
        }

        bool isClassical = roomType == "classical/standard";
        bool isRiichi = roomType != null && roomType.StartsWith("riichi");

        // 古典麻将：显示副番列表
        if (isClassical && fu_fan_list != null) {
            for (int i = 0; i < fu_fan_list.Length; i++) {
                string fuName = fu_fan_list[i];
                string fuDisplay = FanTextDictionary.GetFuDisplayText(fuName);
                string fuNameDisplay = FanTextDictionary.GetFuNameDisplayText(fuName);
                GameObject fuInstance = Instantiate(FanCountPrefab, FanCountContainer);
                FanCount fuCount = fuInstance.GetComponent<FanCount>();
                if (fuCount != null) {
                    fuCount.SetFanCount(fuNameDisplay, fuDisplay);
                    fuCount.ApplyFuColor();
                }
            }
        }

        if (hu_fan != null) {
            for (int i = 0; i < hu_fan.Length; i++) {
                string fanKey = hu_fan[i];
                string fanDisplay = FanTextDictionary.GetFanDisplayText(roomType, fanKey);
                string fanLabel = isRiichi
                    ? FanTextDictionary.GetRiichiYakuDisplayName(fanKey)
                    : FanTextDictionary.GetFanNameDisplayText(roomType, fanKey);
                GameObject fanCountInstance = Instantiate(FanCountPrefab, FanCountContainer);
                FanCount fanCount = fanCountInstance.GetComponent<FanCount>();
                if (fanCount != null) {
                    fanCount.SetFanCount(fanLabel, fanDisplay);
                    fanCount.ApplyFanColor();
                }
            }
        }

        ShowTotalPanel(roomType, hu_score, hu_fan, base_fu, riichiExtras);
        ShowRiichiExtrasPanel(roomType, riichiExtras);
        GuobiaoAngangCheck.Apply(guobiaoAngangCheckText, null, hu_fan);
        TryPlayGongHuSound(roomType, hu_fan);

        // 回放模式仅显示确认按钮，点击后关闭并切到下一局；观战模式不显示确认，由 end tick 驱动
        EndButton.interactable = !isSpectator;
        EndButton.gameObject.SetActive(!isSpectator);
        EndButtonText.text = "确认";
    }

    /// <summary>
    /// 四川查叫：单家面板（有叫/没叫/花猪 + 合并分数变更）。
    /// </summary>
    [System.Obsolete("Use PrepareSichuanChajiaoSingle + CoPlaySichuanChajiaoRoutine")]
    public IEnumerator PlaySichuanChajiaoSingle(
        int focusPlayerIndex,
        string statusKey,
        int[] hand,
        Dictionary<int, int> player_to_score,
        Dictionary<int, int> scoreChanges,
        bool isFinalPanel) {
        PrepareSichuanChajiaoSingle(focusPlayerIndex, statusKey, hand, null, player_to_score, scoreChanges);
        yield return CoPlaySichuanChajiaoRoutine(statusKey, isFinalPanel);
    }

    private IEnumerator CoPlaySichuanChajiaoStatusAndCountdown(string statusKey, bool isFinalPanel) {
        EndButton.gameObject.SetActive(isFinalPanel);
        EndButton.interactable = false;
        EndButtonText.text = "确定";
        FanCountTotalPanel.SetActive(false);

        string statusLabel = StatusKeyToLabel(statusKey);
        GameObject statusFanInstance = Instantiate(FanCountPrefab, FanCountContainer);
        FanCount statusFanCount = statusFanInstance.GetComponent<FanCount>();
        if (statusFanCount != null) {
            statusFanCount.SetFanCount(statusLabel, "—");
            statusFanCount.ApplyFanColor();
        }

        // 没叫/花猪开杠者的刮风下雨退税并入本面板：补“退税”标签（分变已含在 score_changes 中）
        if (chajiaoHasRefund) {
            GameObject refundFanInstance = Instantiate(FanCountPrefab, FanCountContainer);
            FanCount refundFanCount = refundFanInstance.GetComponent<FanCount>();
            if (refundFanCount != null) {
                refundFanCount.SetFanCount("刮风下雨", "退税");
                refundFanCount.ApplyFanColor();
            }
        }

        yield return new WaitForSeconds(RoundEndTiming.SichuanChajiaoStatusHoldSeconds);

        if (isFinalPanel) {
            yield return CoPlayLiujuFinalConfirmCountdown();
        } else {
            float midHold = RoundEndTiming.SichuanMidPanelConfirmSeconds;
            if (chajiaoHasRefund) {
                midHold += RoundEndTiming.SichuanChajiaoRefundExtraSeconds;
            }
            yield return CoPlayEndButtonCountdown(midHold, allowConfirmClick: false);
        }
    }

    /// <summary>四川查叫退税：末步可确认进入下一局。</summary>
    [System.Obsolete("Use PrepareSichuanChaRefundSingle + CoPlaySichuanChaRefundRoutine")]
    public IEnumerator PlaySichuanChaRefundSingle(
        Dictionary<int, int> player_to_score,
        Dictionary<int, int> scoreChanges,
        bool isFinalPanel) {
        PrepareSichuanChaRefundSingle(player_to_score, scoreChanges);
        yield return CoPlaySichuanChaRefundRoutine(isFinalPanel);
    }

    private IEnumerator CoPlaySichuanChaRefundCountdown(bool isFinalPanel) {
        EndButton.gameObject.SetActive(isFinalPanel);
        EndButton.interactable = false;
        EndButtonText.text = "确定";
        FanCountTotalPanel.SetActive(false);

        if (isFinalPanel) {
            yield return CoPlayLiujuFinalConfirmCountdown();
        }
    }

    private IEnumerator CoPlayLiujuFinalConfirmCountdown() {
        yield return CoPlayEndButtonCountdown(RoundEndTiming.HuConfirmCountdownSeconds, allowConfirmClick: true);
    }

    private void PopulateEndTilesContainer(int[] hand, int[][] combinationMask, EndResultTileLayout layout) {
        if (hand == null || hand.Length == 0) return;

        if (layout == EndResultTileLayout.ClosedHandWithMelds) {
            int[] sortedHand = (int[])hand.Clone();
            System.Array.Sort(sortedHand, TileIdOrder.Comparer);
            for (int i = 0; i < sortedHand.Length; i++) {
                SpawnStaticTile(sortedHand[i]);
            }
            SpawnHideSplit();
            SpawnCombinationTiles(combinationMask);
            return;
        }

        int lastCard = hand[hand.Length - 1];
        int[] closedHand = (int[])hand.Clone();
        System.Array.Resize(ref closedHand, closedHand.Length - 1);
        System.Array.Sort(closedHand, TileIdOrder.Comparer);

        for (int i = 0; i < closedHand.Length; i++) {
            SpawnStaticTile(closedHand[i]);
        }
        SpawnHideSplit();
        SpawnCombinationTiles(combinationMask);
        SpawnHideSplit();
        SpawnStaticTile(lastCard);
    }

    private void SpawnStaticTile(int tileId) {
        GameObject staticCard = Instantiate(StaticCardPrefab, EndTilescontainer.transform);
        staticCard.transform.SetParent(EndTilescontainer.transform);
        staticCard.GetComponent<StaticCard>().SetTileOnlyImage(tileId);
    }

    private void SpawnHideSplit() {
        GameObject hideSplitInstance = Instantiate(HideSplit, EndTilescontainer.transform);
        hideSplitInstance.transform.SetParent(EndTilescontainer.transform);
    }

    private void SpawnCombinationTiles(int[][] combinationMask) {
        if (combinationMask == null) return;
        for (int list = 0; list < combinationMask.Length; list++) {
            for (int mask = 1; mask < combinationMask[list].Length; mask += 2) {
                int tileId = combinationMask[list][mask];
                if (tileId <= 10) continue;
                SpawnStaticTile(tileId);
            }
        }
    }

    private static string StatusKeyToLabel(string statusKey) {
        if (statusKey == "ting") return "有叫";
        if (statusKey == "hua_zhu_passive") return "被动花猪";
        if (statusKey == "hua_zhu_active") return "主动花猪";
        if (statusKey == "hua_zhu") return "花猪";
        return "没叫";
    }

    private void ApplyScoreChangesToPanel(Dictionary<int, int> player_to_score, Dictionary<int, int> scoreChanges) {
        var scoreBySeat = new Dictionary<int, int>();
        foreach (var kvp in NormalGameStateManager.Instance.indexToPosition) {
            string pos = kvp.Value;
            int seatIdx = kvp.Key;
            if (!NormalGameStateManager.Instance.player_to_info.TryGetValue(pos, out var playerInfo)) continue;
            int beforeScore = playerInfo.score;
            ShowResultPlayerScoreResolver.ResolveScoreChange(
                beforeScore, seatIdx, playerInfo.original_player_index,
                scoreChanges, player_to_score, out int afterScore);
            string scoreText = FormatScoreWithDiff(beforeScore, afterScore);
            string displayName = StreamerModeHelper.FormatGamestatePlayerName(playerInfo.username, pos, playerInfo.userId);
            if (pos == "self") { SelfUserName.text = displayName; SelfScore.text = scoreText; }
            else if (pos == "left") { LeftUserName.text = displayName; LeftScore.text = scoreText; }
            else if (pos == "top") { TopUserName.text = displayName; TopScore.text = scoreText; }
            else if (pos == "right") { RightUserName.text = displayName; RightScore.text = scoreText; }
            scoreBySeat[seatIdx] = afterScore;
        }
        BoardCanvas.Instance.UpdatePlayerScores(scoreBySeat, NormalGameStateManager.Instance.indexToPosition);
    }

    /// <summary>
    /// 四川流局（旧批量演出用，保留供牌谱等场景调用）。
    /// </summary>
    public void ShowSichuanLiujuStatus(int playerIndex, Dictionary<int, int> player_to_score, string statusLabel, int[] hand) {
        if (showResultCoroutine != null) {
            StopCoroutine(showResultCoroutine);
            showResultCoroutine = null;
        }
        currentState = StateGame;
        gameObject.SetActive(true);
        EndButton.gameObject.SetActive(false);

        foreach (Transform child in EndTilescontainer.transform) Destroy(child.gameObject);
        foreach (Transform child in FanCountContainer) Destroy(child.gameObject);

        ShowRiichiExtrasPanel(NormalGameStateManager.Instance.subRule, null);
        GuobiaoAngangCheck.Clear(guobiaoAngangCheckText);
        SelfReady.gameObject.SetActive(false);
        LeftReady.gameObject.SetActive(false);
        TopReady.gameObject.SetActive(false);
        RightReady.gameObject.SetActive(false);

        // 该玩家手牌整体显示（流局无和牌张，不做末张拆分）
        if (hand != null && hand.Length > 0) {
            int[] sorted = (int[])hand.Clone();
            Array.Sort(sorted, TileIdOrder.Comparer);
            for (int i = 0; i < sorted.Length; i++) {
                GameObject staticCard = Instantiate(StaticCardPrefab, EndTilescontainer.transform);
                staticCard.GetComponent<StaticCard>().SetTileOnlyImage(sorted[i]);
            }
        }

        // 全员分数（before=局前本地分，after=服务端最终分），并把计分板更新到最终分
        var scoreBySeat = new Dictionary<int, int>();
        foreach (var kvp in NormalGameStateManager.Instance.indexToPosition) {
            string pos = kvp.Value;
            int seatIdx = kvp.Key;
            if (!NormalGameStateManager.Instance.player_to_info.TryGetValue(pos, out var playerInfo)) continue;
            int beforeScore = playerInfo.score;
            ShowResultPlayerScoreResolver.ResolveScoreChange(
                beforeScore, seatIdx, playerInfo.original_player_index,
                null, player_to_score, out int afterScore);
            string scoreText = FormatScoreWithDiff(beforeScore, afterScore);
            string displayName = StreamerModeHelper.FormatGamestatePlayerName(playerInfo.username, pos, playerInfo.userId);
            if (pos == "self") { SelfUserName.text = displayName; SelfScore.text = scoreText; }
            else if (pos == "left") { LeftUserName.text = displayName; LeftScore.text = scoreText; }
            else if (pos == "top") { TopUserName.text = displayName; TopScore.text = scoreText; }
            else if (pos == "right") { RightUserName.text = displayName; RightScore.text = scoreText; }
            scoreBySeat[seatIdx] = afterScore;
        }
        BoardCanvas.Instance.UpdatePlayerScores(scoreBySeat, NormalGameStateManager.Instance.indexToPosition);

        // 状态番：statusLabel + 0番
        GameObject fanCountInstance = Instantiate(FanCountPrefab, FanCountContainer);
        FanCount fanCount = fanCountInstance.GetComponent<FanCount>();
        if (fanCount != null) {
            fanCount.SetFanCount(statusLabel, "0番");
            fanCount.ApplyFanColor();
        }

        // 总计：0番 0点
        FanCountTotalPanel.SetActive(true);
        TotalFu.gameObject.SetActive(false);
        TotalFan.text = "0番";
        TotalScore.text = "0点";
        TotalLimitDisplay.gameObject.SetActive(false);
    }

    private static string FormatScoreWithDiff(int before, int after) {
        int diff = after - before;
        if (diff > 0) {
            return $"{after}<color=green>+{diff}</color>";
        } else if (diff < 0) {
            return $"{after}<color=red>{diff}</color>";
        }
        return after.ToString();
    }

    // 按钮点击以后发送准备消息
    public void EndButtonClick(){
        if (!EndButton.interactable) {
            return;
        }
        EndButton.interactable = false;
        if (currentState == StateRecord) {
            HandleRecordStateConfirm();
            return;
        }
        if (currentState == StateGame) {
            HandleGameStateConfirm();
            return;
        }
        Debug.LogWarning("EndResultPanel 当前状态未知，忽略确认点击");
    }

    private void HandleGameStateConfirm() {
        GameStateNetworkManager.Instance.SendAction("ready", 0);
    }

    private void HandleRecordStateConfirm() {
        gameObject.SetActive(false);
        if (GameRecordManager.Instance != null) {
            GameRecordManager.Instance.AdvanceToNextAction();
        }
    }
    
    // 更新准备状态显示：缓存最新准备状态并按缓存重绘座位配色（修复机器人/罗伯特已准备却不显示准备色）
    public void UpdateReadyStatus(Dictionary<int, bool> playerToReady) {
        if (playerToReady != null) {
            foreach (var kvp in playerToReady) {
                cachedReadyStatus[kvp.Key] = kvp.Value;
            }
        }
        ApplySeatVisuals();
    }

    private Image SeatBackground(string position) {
        switch (position) {
            case "self": return SelfPanelBackground;
            case "left": return LeftPanelBackground;
            case "top": return TopPanelBackground;
            case "right": return RightPanelBackground;
            default: return null;
        }
    }

    private Image SeatReadyImage(string position) {
        switch (position) {
            case "self": return SelfReady;
            case "left": return LeftReady;
            case "top": return TopReady;
            case "right": return RightReady;
            default: return null;
        }
    }

    /// <summary>设置“被检查”的座位（川麻终局逐家查牌/查叫高亮深红），并立即重绘。</summary>
    private void SetCheckedFocusSeat(int playerIndex) {
        checkedFocusSeat = playerIndex;
        ApplySeatVisuals();
    }

    /// <summary>按 缓存准备状态 + 被检查座位 重绘所有座位面板配色。</summary>
    private void ApplySeatVisuals() {
        if (NormalGameStateManager.Instance == null) return;
        foreach (var kvp in NormalGameStateManager.Instance.indexToPosition) {
            int seat = kvp.Key;
            string position = kvp.Value;
            bool isReady = cachedReadyStatus.TryGetValue(seat, out bool r) && r;
            // 准备色优先于被检查色：末步等待准备时仍能直观看到谁已准备
            SeatVisual visual = isReady
                ? SeatVisual.Ready
                : (seat == checkedFocusSeat ? SeatVisual.Checked : SeatVisual.Normal);
            ApplySeatVisual(position, visual);
        }
    }

    private void ApplySeatVisual(string position, SeatVisual visual) {
        Image bg = SeatBackground(position);
        if (bg != null) {
            // 背景已配置：用颜色表达状态，并隐藏旧版 Ready 图标
            bg.color = visual == SeatVisual.Ready ? SeatColorReady
                : visual == SeatVisual.Checked ? SeatColorChecked
                : SeatColorNormal;
            Image readyImg = SeatReadyImage(position);
            if (readyImg != null) readyImg.gameObject.SetActive(false);
        } else {
            // 背景未配置（未在 Inspector 拖入）：回退到旧版 Ready 图标显隐
            Image readyImg = SeatReadyImage(position);
            if (readyImg != null) readyImg.gameObject.SetActive(visual == SeatVisual.Ready);
        }
    }

    /// <summary>
    /// 显示总计面板。古典麻将显示副数+番数+点数+满贯，国标/青雀仅显示番数+点数，
    /// 立直麻将显示番（han）+符（fu）+点数。
    /// </summary>
    private void ShowTotalPanel(string rule, int huScore, string[] huFan, int? baseFu, RiichiEndResultExtras riichiExtras = null) {
        FanCountTotalPanel.SetActive(true);
        bool isClassical = rule == "classical/standard";
        bool isRiichi = rule != null && rule.StartsWith("riichi");
        bool isSichuan = rule != null && rule.StartsWith("sichuan");

        if (isRiichi && riichiExtras != null) {
            TotalFu.gameObject.SetActive(true);
            TotalFu.text = $"{riichiExtras.Fu}符";
            TotalFan.text = $"{riichiExtras.Han}番";
            if (riichiExtras.LangyongMultiplier > 1) {
                TotalScore.text = $"{huScore}点*{riichiExtras.LangyongMultiplier}";
            } else {
                TotalScore.text = $"{huScore}点";
            }
            TotalLimitDisplay.gameObject.SetActive(false);
            return;
        }

        bool showFu = isClassical && baseFu.HasValue;
        TotalFu.gameObject.SetActive(showFu);
        if (showFu) TotalFu.text = $"{baseFu.Value}副";

        if (isClassical) {
            int fanTotal = CalculateClassicalFanTotal(huFan);
            TotalFan.text = fanTotal >= 0 ? $"{fanTotal}番" : "满贯";
        } else if (isSichuan) {
            TotalFan.text = $"{ScoreHistorySettlementHelper.CalculateSichuanFanTotal(rule, huFan)}番";
        } else {
            TotalFan.text = $"{huScore}番";
        }

        TotalScore.text = $"{huScore}点";

        bool showLimit = isClassical && huScore >= 300;
        TotalLimitDisplay.gameObject.SetActive(showLimit);
        if (showLimit) TotalLimitDisplay.text = "满贯";
    }

    /// <summary>
    /// 立直麻将结算扩展：赤宝牌数量文本、宝牌/里宝牌指示牌槽位。
    /// 本场棒 / 场供立直棒在 RoundPanel 中已有显示，此处不再重复。
    /// </summary>
    private void ShowRiichiExtrasPanel(string rule, RiichiEndResultExtras extras) {
        bool isRiichi = rule != null && rule.StartsWith("riichi");
        if (RiichiPanel != null) {
            RiichiPanel.SetActive(isRiichi);
        }
        if (!isRiichi || extras == null) {
            FillDoraSlots(RiichiDoraSlots, null);
            FillDoraSlots(RiichiUraDoraSlots, null);
            return;
        }

        FillDoraSlots(RiichiDoraSlots, extras.DoraIndicators);
        FillDoraSlots(RiichiUraDoraSlots, extras.UraDoraIndicators);
    }

    /// <summary>
    /// 将指示牌列表按顺序填入槽位。未提供的位置显示牌背（图集 id 0），翻开的位置显示真实牌面。
    /// 立直家未和牌时不下发里宝牌，故里宝槽位会全部留作牌背。
    /// </summary>
    private const int CardBackImageId = 0;
    private static void FillDoraSlots(StaticCard[] slots, List<int> indicators) {
        if (slots == null) return;
        for (int i = 0; i < slots.Length; i++) {
            if (slots[i] == null) continue;
            int tileId = (indicators != null && i < indicators.Count) ? indicators[i] : CardBackImageId;
            slots[i].SetTileOnlyImage(tileId);
        }
    }

    /// <summary>
    /// 计算古典麻将翻数总和。若包含"满贯"级别役种则返回 -1 表示满贯。
    /// </summary>
    private static void TryPlayGongHuSound(string rule, string[] huFan) {
        if (!FanTextDictionary.ShouldPlayGongHuSound(rule, huFan)) {
            return;
        }
        if (SoundManager.Instance != null) {
            SoundManager.Instance.PlayPhysicsSound("Gong_hu");
        }
    }

    private static int CalculateClassicalFanTotal(string[] huFan) {
        int total = 0;
        foreach (string fan in huFan) {
            string display = FanTextDictionary.GetFanDisplayText("classical/standard", fan);
            if (display == "满贯") return -1;
            if (display.EndsWith("翻") && int.TryParse(display.Replace("翻", ""), out int val)) {
                total += val;
            }
        }
        return total;
    }

    public void ClearEndResultPanel(){
        if (showResultCoroutine != null) {
            StopCoroutine(showResultCoroutine);
            showResultCoroutine = null;
        }
        currentState = StateNone;
        // 新一局：清空准备缓存与被检查高亮，座位配色复位
        cachedReadyStatus.Clear();
        checkedFocusSeat = -1;
        chajiaoHasRefund = false;

        gameObject.SetActive(false);
        FanCountTotalPanel.SetActive(false);
        if (RiichiPanel != null) {
            RiichiPanel.SetActive(false);
        }
        FillDoraSlots(RiichiDoraSlots, null);
        FillDoraSlots(RiichiUraDoraSlots, null);
        GuobiaoAngangCheck.Clear(guobiaoAngangCheckText);

        // 清空结算
        foreach (Transform child in EndTilescontainer.transform){
            Destroy(child.gameObject);
        }
        
        // 清空番数容器
        foreach (Transform child in FanCountContainer){
            Destroy(child.gameObject);
        }
        
        // 清空分数
        SelfUserName.text = "";
        SelfScore.text = "";
        LeftUserName.text = "";
        LeftScore.text = "";
        TopUserName.text = "";
        TopScore.text = "";
        RightUserName.text = "";
        RightScore.text = "";
        
        // 隐藏所有准备状态
        SelfReady.gameObject.SetActive(false);
        LeftReady.gameObject.SetActive(false);
        TopReady.gameObject.SetActive(false);
        RightReady.gameObject.SetActive(false);
    }
}