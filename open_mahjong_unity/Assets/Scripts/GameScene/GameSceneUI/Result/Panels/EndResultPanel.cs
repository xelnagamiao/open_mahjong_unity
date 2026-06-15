using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System;
using UnityEngine.UI;

public class EndResultPanel : MonoBehaviour {
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

    public static EndResultPanel Instance { get; private set; }
    public GameObject CardPrefab => StaticCardPrefab;
    public GameObject HideSplitPrefab => HideSplit;
    private const string StateNone = "";
    private const string StateGame = "gamestate";
    private const string StateRecord = "recordstate";
    private string currentState = StateNone;
    private Coroutine showResultCoroutine;

    public bool IsAwaitingRecordResultConfirm => currentState == StateRecord && gameObject.activeSelf;

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
        showResultCoroutine = StartCoroutine(PlayShowResultRoutine(hu_score, hu_fan, base_fu, fu_fan_list, riichiExtras));
    }

    public void PrepareShowResult(int hepai_player_index, Dictionary<int, int> player_to_score, int hu_score, string[] hu_fan, string hu_class, int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask, RiichiEndResultExtras riichiExtras = null) {
        if (showResultCoroutine != null) {
            StopCoroutine(showResultCoroutine);
            showResultCoroutine = null;
        }
        InitializeShowResult(hepai_player_index, player_to_score, hu_score, hu_fan, hu_class, hepai_player_hand, hepai_player_huapai, hepai_player_combination_mask, riichiExtras);
    }

    public void PlayPreparedShowResult(int hu_score, string[] hu_fan, int? base_fu = null, string[] fu_fan_list = null, RiichiEndResultExtras riichiExtras = null) {
        if (showResultCoroutine != null) {
            StopCoroutine(showResultCoroutine);
            showResultCoroutine = null;
        }
        showResultCoroutine = StartCoroutine(PlayShowResultRoutine(hu_score, hu_fan, base_fu, fu_fan_list, riichiExtras));
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
        yield return PlayShowResultRoutine(hu_score, hu_fan, base_fu, fu_fan_list, riichiExtras);
    }

    public void InitializeShowResult(int hepai_player_index, Dictionary<int, int> player_to_score, int hu_score, string[] hu_fan, string hu_class, int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask, RiichiEndResultExtras riichiExtras = null) {
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
        ShowRiichiExtrasPanel(NormalGameStateManager.Instance.subRule, riichiExtras);
        GuobiaoAngangCheck.Apply(guobiaoAngangCheckText, NormalGameStateManager.Instance?.lastGuobiaoEndExtras, hu_fan);

        SelfReady.gameObject.SetActive(false);
        LeftReady.gameObject.SetActive(false);
        TopReady.gameObject.SetActive(false);
        RightReady.gameObject.SetActive(false);

        // 获取手牌列表最后一个int 并且删除最后一个int
        int lastCard = hepai_player_hand[hepai_player_hand.Length - 1];
        Array.Resize(ref hepai_player_hand, hepai_player_hand.Length - 1);

        // 显示手牌

        // 对剩余手牌排序
        Array.Sort(hepai_player_hand, TileIdOrder.Comparer);
        
        Debug.Log("hepai_player_hand: " + hepai_player_hand.Length);
        for (int i = 0; i < hepai_player_hand.Length; i++){
            GameObject staticCard = Instantiate(StaticCardPrefab, EndTilescontainer.transform);
            staticCard.transform.SetParent(EndTilescontainer.transform);
            staticCard.GetComponent<StaticCard>().SetTileOnlyImage(hepai_player_hand[i]);
        }

        GameObject hideSplitInstance = Instantiate(HideSplit, EndTilescontainer.transform); // 分割
        hideSplitInstance.transform.SetParent(EndTilescontainer.transform);

        // 显示组合牌
        Debug.Log("hepai_player_combination_mask: " + hepai_player_combination_mask.Length);
        for (int list = 0; list < hepai_player_combination_mask.Length; list++){
            for (int mask = 1; mask < hepai_player_combination_mask[list].Length; mask+=2){
                GameObject staticCard = Instantiate(StaticCardPrefab, EndTilescontainer.transform);
                staticCard.transform.SetParent(EndTilescontainer.transform);
                staticCard.GetComponent<StaticCard>().SetTileOnlyImage(hepai_player_combination_mask[list][mask]);
            }
        }

        GameObject hideSplitInstance2 = Instantiate(HideSplit, EndTilescontainer.transform); // 分割
        hideSplitInstance2.transform.SetParent(EndTilescontainer.transform);

        // 显示花牌 未来将花牌单独放在一个容器显示，目前先注释。
        /*
        Debug.Log("hepai_player_huapai: " + hepai_player_huapai.Length);
        for (int i = 0; i < hepai_player_huapai.Length; i++){
            GameObject staticCard = Instantiate(StaticCardPrefab, EndTilescontainer.transform);
            staticCard.transform.SetParent(EndTilescontainer.transform);
            staticCard.GetComponent<StaticCard>().SetTileOnlyImage(hepai_player_huapai[i]);
        }

        GameObject hideSplitInstance3 = Instantiate(HideSplit, EndTilescontainer.transform); // 分割
        hideSplitInstance3.transform.SetParent(EndTilescontainer.transform);
        */

        // 显示和牌张
        Debug.Log("lastCard: " + lastCard);
        GameObject LastCard = Instantiate(StaticCardPrefab, EndTilescontainer.transform);
        LastCard.transform.SetParent(EndTilescontainer.transform);
        LastCard.GetComponent<StaticCard>().SetTileOnlyImage(lastCard);

        
        // 显示玩家分数变化
        foreach (var kvp in NormalGameStateManager.Instance.indexToPosition) {
            string pos = kvp.Value;
            int seatIdx = kvp.Key;
            if (!NormalGameStateManager.Instance.player_to_info.TryGetValue(pos, out var playerInfo)) continue;
            int origIdx = playerInfo.original_player_index;
            int beforeScore = playerInfo.score;
            ShowResultPlayerScoreResolver.ResolveScoreChange(
                beforeScore, seatIdx, origIdx,
                riichiExtras?.ScoreChanges, player_to_score, out int afterScore);
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

    private IEnumerator PlayShowResultRoutine(int hu_score, string[] hu_fan, int? base_fu = null, string[] fu_fan_list = null, RiichiEndResultExtras riichiExtras = null) {
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

        EndButton.interactable = true;
        int countdown = Mathf.RoundToInt(RoundEndTiming.HuConfirmCountdownSeconds);
        for (int i = countdown; i > 0; i--) {
            EndButtonText.text = $"确定({i})";
            yield return new WaitForSeconds(1f);
        }
        EndButtonText.text = "确定(0)";
        EndButton.interactable = false;
        gameObject.SetActive(false);
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

        // 显示和牌玩家手牌（同 ShowResult 的逻辑）
        if (hepai_player_hand != null && hepai_player_hand.Length > 0) {
            int lastCard = hepai_player_hand[hepai_player_hand.Length - 1];
            int[] handWithoutLast = new int[hepai_player_hand.Length - 1];
            Array.Copy(hepai_player_hand, handWithoutLast, handWithoutLast.Length);
            Array.Sort(handWithoutLast, TileIdOrder.Comparer);

            for (int i = 0; i < handWithoutLast.Length; i++) {
                GameObject staticCard = Instantiate(StaticCardPrefab, EndTilescontainer.transform);
                staticCard.GetComponent<StaticCard>().SetTileOnlyImage(handWithoutLast[i]);
            }

            GameObject hideSplitInstance = Instantiate(HideSplit, EndTilescontainer.transform);

            // 显示组合牌
            if (hepai_player_combination_mask != null) {
                for (int list = 0; list < hepai_player_combination_mask.Length; list++) {
                    for (int mask = 1; mask < hepai_player_combination_mask[list].Length; mask += 2) {
                        GameObject staticCard = Instantiate(StaticCardPrefab, EndTilescontainer.transform);
                        staticCard.GetComponent<StaticCard>().SetTileOnlyImage(hepai_player_combination_mask[list][mask]);
                    }
                }
            }

            GameObject hideSplitInstance2 = Instantiate(HideSplit, EndTilescontainer.transform);

            // 显示和牌张
            GameObject LastCard = Instantiate(StaticCardPrefab, EndTilescontainer.transform);
            LastCard.GetComponent<StaticCard>().SetTileOnlyImage(lastCard);
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

        // 回放模式仅显示确认按钮，点击后关闭并切到下一局；观战模式不显示确认，由 end tick 驱动
        EndButton.interactable = !isSpectator;
        EndButton.gameObject.SetActive(!isSpectator);
        EndButtonText.text = "确认";
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
        // 对局状态：发送准备消息到服务器
        GameStateNetworkManager.Instance.SendAction("ready", 0);
    }

    private void HandleRecordStateConfirm() {
        gameObject.SetActive(false);
        if (GameRecordManager.Instance != null) {
            GameRecordManager.Instance.AdvanceToNextAction();
        }
    }
    
    // 更新准备状态显示
    public void UpdateReadyStatus(Dictionary<int, bool> playerToReady) {
        foreach (var kvp in playerToReady) {
            int playerIndex = kvp.Key;
            bool isReady = kvp.Value;
            
            // 根据玩家索引找到对应的位置
            string position = NormalGameStateManager.Instance.indexToPosition.ContainsKey(playerIndex) 
                ? NormalGameStateManager.Instance.indexToPosition[playerIndex] 
                : null;
            
            if (position == "self") {
                SelfReady.gameObject.SetActive(isReady);
            } else if (position == "left") {
                LeftReady.gameObject.SetActive(isReady);
            } else if (position == "top") {
                TopReady.gameObject.SetActive(isReady);
            } else if (position == "right") {
                RightReady.gameObject.SetActive(isReady);
            }
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