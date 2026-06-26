using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// GameRecordManager 延时观战模式扩展
/// - 直播观战：自动推进，仅回放已发生的 action tick（不展示 ask 操作按钮）
/// - 牌谱阅览：手动步进
/// </summary>
public partial class GameRecordManager {
    public bool IsSpectating { get; private set; } = false;
    /// <summary>true=直播观战模式 false=牌谱阅览模式(观战中手动浏览历史节点)</summary>
    public bool IsLiveSpectatorMode { get; private set; } = true;

    /// <summary>
    /// 延时观战（牌谱流）专用 gamestate_id，与实时观战/正常对局使用的 UserDataManager.GamestateId 分离。
    /// 在 add_spectator 发出后、record_init 到达前也会暂存，用于互斥判断与退出时通知服务端。
    /// </summary>
    private string _delayedSpectatorGamestateId = "";

    /// <summary>已请求加入延时观战但尚未收到 record_init（或尚未进入 IsSpectating）。</summary>
    public bool HasPendingDelayedSpectatorSession =>
        !string.IsNullOrEmpty(_delayedSpectatorGamestateId) && !IsSpectating;

    private Coroutine autoPlayCoroutine;
    private float autoPlaySpeed = 1.0f;
    private bool waitingForMoreTicks = false;
    private bool PauseAutoPlay => !IsLiveSpectatorMode;

    [Header("观战模式面板（仅观战时显示）")]
    [SerializeField] private GameObject spectatorModePanel;
    [SerializeField] private TMP_Text spectatorModeText;
    [SerializeField] private Button spectatorBackLiveButton;
    [SerializeField] private Button spectatorExitButton;
    [SerializeField] private GameObject spectatorInfoView;
    [SerializeField] private TMP_Text spectatorInfoText;

    /// <summary>
    /// 延时观战：在发出 add_spectator 时绑定目标对局（纯牌谱阅览、实时观战不走此路径）。
    /// </summary>
    public void PrepareDelayedSpectatorSession(string gamestateId) {
        if (string.IsNullOrEmpty(gamestateId)) return;
        _delayedSpectatorGamestateId = gamestateId;
    }

    public void ClearDelayedSpectatorSession() {
        _delayedSpectatorGamestateId = "";
    }

    /// <summary>
    /// 客户端已放弃延时观战但服务端可能仍保留观战连接时，主动请求移除。
    /// </summary>
    public void AbandonDelayedSpectatorSessionOnServer() {
        if (string.IsNullOrEmpty(_delayedSpectatorGamestateId)) return;
        string gamestateId = _delayedSpectatorGamestateId;
        _delayedSpectatorGamestateId = "";
        _ = GameStateNetworkManager.Instance?.RemoveSpectator(gamestateId);
    }

    /// <summary>
    /// 开始延时观战：解析初始牌谱、剔除 ask 节点、快进到最新状态、启动自动播放。
    /// 已在延时观战中时（如对局结束推送完整牌谱）仅刷新牌谱，不重复做进入守卫。
    /// </summary>
    public void StartSpectating(string recordJson) {
        if (!IsSpectating && LobbyStateGuard.BlockIfInMatchQueueForSpectator()) return;
        if (!IsSpectating && !HasPendingDelayedSpectatorSession
            && GameSessionGuard.BlockIfExclusiveSession("进入延时观战")) return;

        IsSpectating = true;
        IsLiveSpectatorMode = true;
        waitingForMoreTicks = false;
        ClearSpectatorActionButtons();

        PlayerRecordInfo[] playersInfo = ExtractPlayersSettings(recordJson);
        LoadRecord(recordJson, playersInfo);

        // 服务端 ask_hand/ask_other 不入牌谱，仅回放已发生的操作。
        RemoveAskTicksFromAllRounds();
        SyncRoundInspectorAndRoundButtons();

        // 默认观战视角固定东家（可按既有牌谱逻辑切换视角）
        selectedPlayerIndex = 0;
        selectedPlayerUserid = gameRecord.gameRound.rounds.ContainsKey(1)
            ? gameRecord.gameRound.rounds[1].p0UserId : 0;

        JumpToLatestState();
        ShowSpectatorModePanel(true);
        BindSpectatorButtons();
        // LoadRecord 会将 CurrentMode 重置为 Record，这里统一回到观战直播模式
        SetSpectatorModeFlags(true);

        if (autoPlayCoroutine != null) {
            StopCoroutine(autoPlayCoroutine);
        }
        autoPlayCoroutine = StartCoroutine(AutoPlayCoroutine());
    }

    public void StopSpectating() {
        if (!IsSpectating) return;
        IsSpectating = false;
        IsLiveSpectatorMode = true;
        CurrentMode = RecordManagerMode.Record;
        waitingForMoreTicks = false;
        ClearSpectatorActionButtons();
        ShowSpectatorModePanel(false);
        UpdateModeUIVisibility();
        if (autoPlayCoroutine != null) {
            StopCoroutine(autoPlayCoroutine);
            autoPlayCoroutine = null;
        }
        AbandonDelayedSpectatorSessionOnServer();
        GameSceneTeardown.ResetToIdle();
    }

    /// <summary>切换到牌谱阅览模式：用于观战中手动切离最后节点。</summary>
    public void SwitchToRecordMode() {
        if (!IsSpectating) return;
        SetSpectatorModeFlags(false);
        ClearSpectatorActionButtons();
    }

    /// <summary>切回直播观战模式：自动推进并对齐到当前最新状态。</summary>
    public void SwitchToLiveMode() {
        if (!IsSpectating) return;
        SetSpectatorModeFlags(true);
        ClearSpectatorActionButtons();
        JumpToLatestState();
    }

    /// <summary>
    /// 根据当前节点位置自动判定观战子模式：
    /// - 在最后一局最后节点：直播观战模式
    /// - 否则：牌谱阅览模式
    /// </summary>
    public void RefreshSpectatorModeByNodePosition() {
        if (!IsSpectating) return;
        if (IsAtSpectatorTail()) {
            SetSpectatorModeFlags(true);
        } else {
            SetSpectatorModeFlags(false);
            ClearSpectatorActionButtons();
        }
    }

    public void NotifyReachedLastAction() {
        NotificationManager.Instance.ShowTip("观战", false, "已经是最后一个行动了");
    }

    /// <summary>供按钮逻辑判断：当前局是否还能继续前进一步。</summary>
    public bool CanAdvanceCurrentRound() {
        return HasMoreSpectatorTicks();
    }

    public void ShowSpectatorInfo() {
        if (!IsSpectatorSession) return;
        bool nextState = !spectatorInfoView.activeSelf;
        if (nextState) {
            // 与普通信息面板一致：展示前关闭其它信息视图
            roundInfoView.SetActive(false);
            gameInfoView.SetActive(false);
            spectatorInfoText.text = BuildSpectatorInfoString();
            spectatorInfoView.SetActive(true);
        } else {
            spectatorInfoView.SetActive(false);
        }
    }

    /// <summary>
    /// 接收服务端推送的增量数据：ask 节点丢弃，仅将已发生的操作追加到 actionTicks。
    /// </summary>
    public void AppendSpectatorTicks(string updatesJson) {
        if (gameRecord == null || !IsSpectating) return;

        bool roundStructureChanged = false;
        bool currentRoundChanged = false;

        JArray updates = JArray.Parse(updatesJson);
        foreach (JToken update in updates) {
            string type = update["type"]?.Value<string>();

            if (type == "new_round") {
                int roundIndex = update["round_index"]?.Value<int>() ?? 0;
                JObject roundDataJson = update["round_data"] as JObject;
                if (roundDataJson == null || roundIndex <= 0) continue;

                Round newRound = ParseSpectatorRound(roundDataJson, roundIndex);
                RemoveAskTicksFromRound(newRound);
                gameRecord.gameRound.rounds[roundIndex] = newRound;
                roundStructureChanged = true;
            } else if (type == "ticks") {
                int roundIndex = update["round_index"]?.Value<int>() ?? 0;
                JArray newTicks = update["new_ticks"] as JArray;
                if (newTicks == null) continue;
                if (!gameRecord.gameRound.rounds.TryGetValue(roundIndex, out Round roundData)) continue;
                int targetNode = roundData.actionTicks.Count;

                foreach (JToken tickToken in newTicks) {
                    JArray tickArr = tickToken as JArray;
                    if (tickArr == null) continue;
                    List<string> tick = ConvertTickArrayToList(tickArr);
                    if (IsAskTick(tick)) {
                        continue;
                    }
                    roundData.actionTicks.Add(tick);
                    GameRecordJsonDecoder.ValidateKanMoGangTick(tick, $"round_index_{roundIndex} tick#{targetNode}");
                    // 观战实时累计分值变化，确保计分板分值列随结算更新而非恒为 0
                    GameRecordJsonDecoder.AccumulateScoreChangesFromTick(roundData, tick);
                    targetNode++;
                    if (roundIndex == currentRoundIndex) {
                        currentRoundChanged = true;
                    }
                }
            }
        }

        if (roundStructureChanged || currentRoundChanged) {
            SyncRoundInspectorAndRoundButtons();
            if (gameRecord.gameRound.rounds.ContainsKey(currentRoundIndex)) {
                BuildXunmuToNodeAndCreateItems();
                RefreshCurrentRecordTexts();
            }
            // 计分板若处于打开状态，随结算实时刷新分值列
            if (ScoreHistoryPanel.Instance != null && ScoreHistoryPanel.Instance.gameObject.activeSelf) {
                RefreshRecordScoreTable();
            }
        }

        if (waitingForMoreTicks) {
            waitingForMoreTicks = false;
        }

        // 若用户在牌谱阅览模式且已到最后一局最后节点，恢复直播模式
        if (!IsLiveSpectatorMode && gameRecord?.gameRound?.rounds != null) {
            int lastRound = 0;
            foreach (var kvp in gameRecord.gameRound.rounds) {
                if (kvp.Key > lastRound) lastRound = kvp.Key;
            }
            if (currentRoundIndex == lastRound &&
                gameRecord.gameRound.rounds.TryGetValue(lastRound, out Round lr) &&
                lr.actionTicks != null &&
                currentNode >= lr.actionTicks.Count) {
                SwitchToLiveMode();
            }
        }
    }

    private void BindSpectatorButtons() {
        spectatorBackLiveButton.onClick.RemoveAllListeners();
        spectatorBackLiveButton.onClick.AddListener(SwitchToLiveMode);
        spectatorExitButton.onClick.RemoveAllListeners();
        spectatorExitButton.onClick.AddListener(OnClickExitSpectator);
    }

    private void OnClickExitSpectator() {
        PostGameNavigator.ExitToSpectator();
    }

    private void ShowSpectatorModePanel(bool show) {
        spectatorModePanel.SetActive(show);
    }

    private void UpdateSpectatorModeText() {
        spectatorModeText.text = IsLiveSpectatorMode ? "直播观战模式" : "牌谱阅览模式";
        TMP_Text label = spectatorBackLiveButton.GetComponentInChildren<TMP_Text>(true);
        label.text = "回到直播";
    }

    private void SetSpectatorModeFlags(bool live) {
        IsLiveSpectatorMode = live;
        CurrentMode = live ? RecordManagerMode.Spectator : RecordManagerMode.RecordOnSpectator;
        UpdateSpectatorModeText();
        UpdateModeUIVisibility();
        if (live) spectatorInfoView.SetActive(false);
    }

    private IEnumerator AutoPlayCoroutine() {
        while (IsSpectating) {
            if (PauseAutoPlay) {
                yield return new WaitForSeconds(0.3f);
                continue;
            }

            // 直播观战由 AutoPlay + end tick 驱动推进；和牌面板会置 IsAwaitingRecordResultConfirm，
            // 若在此阻塞则永远无法执行 end tick 切局。停留时长由 GetSpectatorEndHoldDelay 控制。
            if (BlocksRecordNavigation && !IsLiveSpectatorMode) {
                yield return new WaitForSeconds(0.1f);
                continue;
            }

            if (!HasMoreSpectatorTicks()) {
                int nextRound = currentRoundIndex + 1;
                if (gameRecord.gameRound.rounds.ContainsKey(nextRound)) {
                    EndResultPanel.Instance.ClearEndResultPanel();
                    GotoSelectRound(nextRound, false);
                    continue;
                }
                waitingForMoreTicks = true;
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            string nextAction = PeekNextTickAction();
            // end tick 会清除上一条结算（和牌/流局）面板。这里需按结算面板的完整演出时长保持，
            // 否则会出现“和牌面板一闪而过就进入下一局”的问题（应与真实玩家 8 秒确认倒计时一致）。
            float delay = nextAction == "end" ? GetSpectatorEndHoldDelay() : GetSpectatorDelay(nextAction);
            yield return new WaitForSeconds(delay / autoPlaySpeed);
            if (!IsSpectating) yield break;
            SpectatorNextAction();
        }
    }

    private bool HasMoreSpectatorTicks() {
        if (gameRecord?.gameRound?.rounds == null) return false;
        if (!gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData)) return false;
        return roundData.actionTicks != null && currentNode < roundData.actionTicks.Count;
    }

    private string PeekNextTickAction() {
        if (!gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData)) return "";
        if (roundData.actionTicks == null || currentNode >= roundData.actionTicks.Count) return "";
        List<string> tick = roundData.actionTicks[currentNode];
        return (tick != null && tick.Count > 0) ? tick[0] : "";
    }

    private float GetSpectatorDelay(string action) {
        switch (action) {
            case "d":
            case "gd":
            case "bd": return 0.3f;
            case "c": return 0.4f;
            case "bh": return 0.3f;
            case "ag":
            case "jg": return 0.6f;
            case "cl":
            case "cm":
            case "cr":
            case "p":
            case "g": return 0.6f;
            // 和牌前仅短暂停留以展示和牌张/摸切张，真正的“面板停留”由其后的 end tick 保持（见 GetSpectatorEndHoldDelay）。
            case "hu_self":
            case "hu_first":
            case "hu_second":
            case "hu_third":
            case "hu_riichi": return 1.0f;
            case "liuju":
            case "jiuzhongjiupai": return 2.0f;
            case "shuhewei": return 5.0f;
            case "end": return 0.5f;
            default: return 0.3f;
        }
    }

    /// <summary>
    /// 计算 end tick 的保持时长：end 会清除上一条结算面板，需保持到结算演出完成。
    /// - 和牌：按番/符数量计算完整面板演出时长（含确认倒计时），与真实玩家观感一致；
    /// - 流局/九种九牌：保持流局提示停留时间；
    /// - 其它：沿用默认短延时。
    /// </summary>
    private float GetSpectatorEndHoldDelay() {
        const float defaultDelay = 0.5f;
        if (gameRecord?.gameRound?.rounds == null) return defaultDelay;
        if (!gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData)) return defaultDelay;
        if (roundData.actionTicks == null || currentNode <= 0 || currentNode > roundData.actionTicks.Count) return defaultDelay;

        List<string> prevTick = roundData.actionTicks[currentNode - 1];
        if (prevTick == null || prevTick.Count == 0) return defaultDelay;

        string prev = prevTick[0];
        switch (prev) {
            case "hu_self":
            case "hu_first":
            case "hu_second":
            case "hu_third": {
                // tick: [hu_class, hepai_index, hu_score, hu_fan_json, score_changes_json, base_fu?, fu_fan_list?]
                int fanCount = ParseHuFanList(prevTick, 3)?.Length ?? 0;
                int fuFanCount = prevTick.Count > 6 ? (ParseHuFanList(prevTick, 6)?.Length ?? 0) : 0;
                return RoundEndTiming.GetHuResultPanelDuration(fanCount, fuFanCount, 0f);
            }
            case "hu_riichi": {
                // tick: [hu_riichi, hepai_index, hu_class, han, fu, yaku[], ...]
                string huClass = prevTick.Count > 2 ? prevTick[2] : "hu_self";
                if (huClass == "jiuzhongjiupai" || NormalGameStateManager.IsRiichiSpecialLiujuHuClass(huClass)) {
                    return 2.0f;
                }
                int yakuCount = ParseHuFanList(prevTick, 5)?.Length ?? 0;
                return RoundEndTiming.GetHuResultPanelDuration(yakuCount, 0, 0f);
            }
            case "liuju":
            case "jiuzhongjiupai":
            case "ryuukyoku":
                return 2.0f;
            default:
                return defaultDelay;
        }
    }

    private void SpectatorNextAction() {
        if (!gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData)) return;
        if (roundData.actionTicks == null || currentNode >= roundData.actionTicks.Count) return;

        List<string> tick = roundData.actionTicks[currentNode];
        if (tick == null || tick.Count == 0) {
            currentNode++;
            return;
        }

        string action = tick[0];

        if (action == "end") {
            EndResultPanel.Instance.ClearEndResultPanel();
            currentNode++;
            int nextRound = currentRoundIndex + 1;
            if (gameRecord.gameRound.rounds.ContainsKey(nextRound)) {
                GotoSelectRound(nextRound, false);
            }
        } else if (action == "liuju") {
            if (tick.Count >= 2 && IsSichuanBloodBattleRecord()) {
                HandleSichuanLiujuStepReplay(tick);
            } else {
                RoundEndPresentation.Instance.PresentLiuju("流局", false);
            }
            currentNode++;
            UpdateCurrentXunmuText();
        } else if (action == "jiuzhongjiupai") {
            TryGetActiveRecordRuleContext(out string roomRule, out _);
            RoundEndPresentation.Instance.PresentLiuju(NormalGameStateManager.GetJiuzhongjiupaiCaption(roomRule), false);
            currentNode++;
            UpdateCurrentXunmuText();
        } else {
            // 国标错和不结束本局、不产生 end tick，其结算面板没有关闭时机。
            // 当本局继续打牌的行动 tick 到来、且仍残留着上一条（错和）结算面板时，先关闭面板，避免观战画面被一直盖住而“卡住”。
            if (IsContinuingPlayTickAction(action) && EndResultPanel.Instance != null
                && EndResultPanel.Instance.IsAwaitingRecordResultConfirm) {
                TryResumeAfterRecordCuoheContinue();
                EndResultPanel.Instance.ClearEndResultPanel();
            }
            NextAction();
        }
    }

    /// <summary>本局内继续推进牌局的行动 tick（摸/切/补花/吃碰杠/立直）；和牌/数和尾等结算 tick 不在其列。</summary>
    private static bool IsContinuingPlayTickAction(string action) {
        switch (action) {
            case "d":
            case "gd":
            case "bd":
            case "c":
            case "bh":
            case "ag":
            case "jg":
            case "cl":
            case "cm":
            case "cr":
            case "p":
            case "g":
            case "riichi":
                return true;
            default:
                return false;
        }
    }

    private static void ClearSpectatorActionButtons() {
        if (GameCanvas.Instance == null) return;
        GameCanvas.Instance.ClearActionButton();
    }

    private void JumpToLatestState() {
        if (gameRecord?.gameRound?.rounds == null) return;
        int lastRound = 1;
        foreach (var kvp in gameRecord.gameRound.rounds) {
            if (kvp.Key > lastRound) lastRound = kvp.Key;
        }
        currentRoundIndex = lastRound;
        InitGameRound(lastRound);
        SyncSpectatorLiveToRoundTail(lastRound);
    }

    /// <summary>
    /// 直播观战自动切局：快进到该局已有 tick 末尾并保持直播模式（避免 node=0 被误判为牌谱阅览）。
    /// </summary>
    private void SyncSpectatorLiveToRoundTail(int roundIndex) {
        if (!IsSpectating) return;
        int tickCount = 0;
        if (gameRecord.gameRound.rounds.TryGetValue(roundIndex, out Round roundData) && roundData.actionTicks != null) {
            tickCount = roundData.actionTicks.Count;
        }
        if (tickCount > 0) {
            GotoAction(tickCount);
        }
        SetSpectatorModeFlags(true);
        waitingForMoreTicks = tickCount == 0;
    }

    private string BuildSpectatorInfoString() {
        if (gameRecord?.gameRound?.rounds == null || gameRecord.gameRound.rounds.Count == 0) {
            return "暂无观战信息";
        }

        int lastRound = currentRoundIndex;
        foreach (int r in gameRecord.gameRound.rounds.Keys) {
            if (r > lastRound) lastRound = r;
        }

        int currentRoundActionCount = 0;
        int tileWallCount = 0;
        int currentRoundLabel = 0;
        if (gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round rd)) {
            currentRoundActionCount = rd.actionTicks?.Count ?? 0;
            tileWallCount = rd.tilesList?.Count ?? 0;
            currentRoundLabel = rd.currentRound;
        }

        int p0 = ReadGameTitleInt(gameRecord.gameTitle, "p0_uid", 0);
        int p1 = ReadGameTitleInt(gameRecord.gameTitle, "p1_uid", 0);
        int p2 = ReadGameTitleInt(gameRecord.gameTitle, "p2_uid", 0);
        int p3 = ReadGameTitleInt(gameRecord.gameTitle, "p3_uid", 0);
        string p0Name = userIdToUsername.TryGetValue(p0, out string n0) ? n0 : p0.ToString();
        string p1Name = userIdToUsername.TryGetValue(p1, out string n1) ? n1 : p1.ToString();
        string p2Name = userIdToUsername.TryGetValue(p2, out string n2) ? n2 : p2.ToString();
        string p3Name = userIdToUsername.TryGetValue(p3, out string n3) ? n3 : p3.ToString();

        string modeText = CurrentMode == RecordManagerMode.Spectator ? "直播观战模式" :
            (CurrentMode == RecordManagerMode.RecordOnSpectator ? "牌谱阅览模式(观战内)" : "牌谱模式");
        string liveText = IsLiveSpectatorMode ? "是" : "否";
        string atTailText = IsAtSpectatorTail() ? "是" : "否";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("【观战信息】");
        sb.AppendLine($"模式: {modeText}");
        sb.AppendLine($"直播推进: {liveText}");
        sb.AppendLine($"是否在最新节点: {atTailText}");
        sb.AppendLine($"当前局: {currentRoundIndex}/{lastRound} (局标识:{currentRoundLabel})");
        sb.AppendLine($"当前节点: {currentNode}/{currentRoundActionCount}");
        sb.AppendLine($"当前行动玩家Index: {currentPlayerIndex}");
        sb.AppendLine($"当前视角玩家Index: {selectedPlayerIndex} (UID:{selectedPlayerUserid})");
        sb.AppendLine($"牌山剩余: {tileWallCount}");
        AppendCommitmentSaltLines(sb, gameRecord.gameTitle);
        sb.AppendLine($"玩家0: {p0Name} (ID:{p0})");
        sb.AppendLine($"玩家1: {p1Name} (ID:{p1})");
        sb.AppendLine($"玩家2: {p2Name} (ID:{p2})");
        sb.AppendLine($"玩家3: {p3Name} (ID:{p3})");
        return sb.ToString();
    }

    private bool IsAtSpectatorTail() {
        if (gameRecord?.gameRound?.rounds == null || gameRecord.gameRound.rounds.Count == 0) {
            return true;
        }
        int lastRound = 0;
        foreach (int r in gameRecord.gameRound.rounds.Keys) {
            if (r > lastRound) lastRound = r;
        }
        if (!gameRecord.gameRound.rounds.TryGetValue(lastRound, out Round lastRoundData) || lastRoundData.actionTicks == null) {
            return currentRoundIndex >= lastRound;
        }
        int tailNode = lastRoundData.actionTicks.Count;
        return currentRoundIndex == lastRound && currentNode >= tailNode;
    }

    private void SyncRoundInspectorAndRoundButtons() {
        _roundsListForInspector = gameRecord.gameRound.GetRoundsList();
        foreach (Round round in _roundsListForInspector) {
            round.UpdateActionTicksDisplay();
        }
        BuildRoundNodeItems();
    }

    private void UpdateModeUIVisibility() {
        bool inRecord = CurrentMode == RecordManagerMode.Record;
        bool inSpectator = CurrentMode == RecordManagerMode.Spectator || CurrentMode == RecordManagerMode.RecordOnSpectator;

        if (ExitButtonManager.Instance != null) {
            if (inSpectator) ExitButtonManager.Instance.ShowForSpectator();
            else if (inRecord) ExitButtonManager.Instance.ShowForRecord();
            else ExitButtonManager.Instance.HideAll();
        }
        showGameInfoButton.gameObject.SetActive(inRecord);
        showSpectatorInfoButton.gameObject.SetActive(inSpectator);
        if (inSpectator) gameInfoView.SetActive(false);
        if (inRecord) spectatorInfoView.SetActive(false);
    }

    private static bool IsAskTick(List<string> tick) {
        return tick != null && tick.Count > 0 && (tick[0] == "ask_hand" || tick[0] == "ask_other");
    }

    private static List<string> ConvertTickArrayToList(JArray tickArr) {
        var tick = new List<string>();
        foreach (JToken elem in tickArr) {
            tick.Add(elem is JArray arr ? arr.ToString() : (elem?.ToString() ?? ""));
        }
        return tick;
    }

    private void RemoveAskTicksFromAllRounds() {
        if (gameRecord?.gameRound?.rounds == null) return;
        foreach (Round round in gameRecord.gameRound.rounds.Values) {
            RemoveAskTicksFromRound(round);
        }
    }

    private static void RemoveAskTicksFromRound(Round round) {
        if (round?.actionTicks == null) return;
        round.actionTicks.RemoveAll(IsAskTick);
    }

    private static int SafeGetInt(Dictionary<string, object> dict, string key) {
        if (dict != null && dict.TryGetValue(key, out object val) && val != null) {
            try { return Convert.ToInt32(val); } catch { return 0; }
        }
        return 0;
    }

    private static PlayerRecordInfo[] ExtractPlayersSettings(string recordJson) {
        try {
            JObject root = JObject.Parse(recordJson);
            JArray settingsArr = root["players_settings"] as JArray;
            if (settingsArr == null || settingsArr.Count == 0) return null;

            PlayerRecordInfo[] result = new PlayerRecordInfo[settingsArr.Count];
            for (int i = 0; i < settingsArr.Count; i++) {
                JObject ps = settingsArr[i] as JObject;
                if (ps == null) continue;
                result[i] = new PlayerRecordInfo {
                    user_id = ps["user_id"]?.Value<int>() ?? 0,
                    username = ps["username"]?.Value<string>() ?? "",
                    title_used = ps["title_used"]?.Value<int>(),
                    profile_used = ps["profile_used"]?.Value<int>(),
                    character_used = ps["character_used"]?.Value<int>(),
                    voice_used = ps["voice_used"]?.Value<int>(),
                };
            }
            return result;
        } catch {
            return null;
        }
    }

    private Round ParseSpectatorRound(JObject roundData, int roundIndex) {
        Round round = new Round();
        if (gameRecord?.gameTitle != null) {
            var playerUserIds = new Dictionary<int, int> {
                [0] = SafeGetInt(gameRecord.gameTitle, "p0_uid"),
                [1] = SafeGetInt(gameRecord.gameTitle, "p1_uid"),
                [2] = SafeGetInt(gameRecord.gameTitle, "p2_uid"),
                [3] = SafeGetInt(gameRecord.gameTitle, "p3_uid"),
            };
            GameRecordJsonDecoder.ApplyPlayerUserIds(round, playerUserIds);
        }
        GameRecordJsonDecoder.ApplyRoundHeader(round, roundData, roundIndex);

        round.actionTicks = new List<List<string>>();
        JArray actionTicks = roundData["action_ticks"] as JArray;
        if (actionTicks != null) {
            foreach (JToken tickToken in actionTicks) {
                JArray tickArr = tickToken as JArray;
                if (tickArr == null) continue;
                List<string> tick = ConvertTickArrayToList(tickArr);
                round.actionTicks.Add(tick);
                GameRecordJsonDecoder.ValidateKanMoGangTick(tick, $"round_index_{roundIndex}");
                // 观战同样累计分值变化，避免计分板分值列全为 0
                GameRecordJsonDecoder.AccumulateScoreChangesFromTick(round, tick);
            }
        }

        return round;
    }
}

