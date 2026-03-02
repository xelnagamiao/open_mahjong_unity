using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// GameRecordManager 观战模式扩展
/// - 直播观战：自动推进，显示 ask 按钮（仅展示不可点）
/// - 牌谱阅览：手动步进，忽略 ask
/// </summary>
public partial class GameRecordManager {
    public bool IsSpectating { get; private set; } = false;
    /// <summary>true=直播观战模式 false=牌谱阅览模式(观战中手动浏览历史节点)</summary>
    public bool IsLiveSpectatorMode { get; private set; } = true;

    private Coroutine autoPlayCoroutine;
    private float autoPlaySpeed = 1.0f;
    private bool waitingForMoreTicks = false;
    private readonly Queue<LiveAskEvent> liveAskQueue = new Queue<LiveAskEvent>();
    private bool PauseAutoPlay => !IsLiveSpectatorMode;

    [Header("观战模式面板（仅观战时显示）")]
    [SerializeField] private GameObject spectatorModePanel;
    [SerializeField] private TMP_Text spectatorModeText;
    [SerializeField] private Button spectatorBackLiveButton;
    [SerializeField] private Button spectatorExitButton;
    [SerializeField] private GameObject spectatorInfoView;
    [SerializeField] private TMP_Text spectatorInfoText;

    private struct LiveAskEvent {
        public int targetNode;
        public List<string> tick;
    }

    /// <summary>
    /// 开始观战：解析初始牌谱、剔除 ask 节点、快进到最新状态、启动自动播放
    /// </summary>
    public void StartSpectating(string recordJson) {
        IsSpectating = true;
        IsLiveSpectatorMode = true;
        waitingForMoreTicks = false;
        liveAskQueue.Clear();

        PlayerRecordInfo[] playersInfo = ExtractPlayersSettings(recordJson);
        LoadRecord(recordJson, playersInfo);

        // 观战中的 ask 只用于直播瞬时展示，不参与牌谱回放。
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
        liveAskQueue.Clear();
        ClearSpectatorAskUI();
        ShowSpectatorModePanel(false);
        UpdateModeUIVisibility();
        if (autoPlayCoroutine != null) {
            StopCoroutine(autoPlayCoroutine);
            autoPlayCoroutine = null;
        }
        Game3DManager.Instance.Clear3DTile();
        HideGameRecord();
    }

    /// <summary>切换到牌谱阅览模式：用于观战中手动切离最后节点。</summary>
    public void SwitchToRecordMode() {
        if (!IsSpectating) return;
        SetSpectatorModeFlags(false);
        liveAskQueue.Clear();
        ClearSpectatorAskUI();
    }

    /// <summary>切回直播观战模式：自动推进并对齐到当前最新状态。</summary>
    public void SwitchToLiveMode() {
        if (!IsSpectating) return;
        SetSpectatorModeFlags(true);
        liveAskQueue.Clear();
        ClearSpectatorAskUI();
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
            liveAskQueue.Clear();
            ClearSpectatorAskUI();
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
    /// 接收服务端推送的增量数据：
    /// - ask 节点：仅在直播模式加入瞬时队列，不入牌谱
    /// - 普通节点：追加到牌谱 actionTicks
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
                        if (IsLiveSpectatorMode && roundIndex == currentRoundIndex) {
                            liveAskQueue.Enqueue(new LiveAskEvent {
                                targetNode = targetNode,
                                tick = tick
                            });
                        }
                        continue;
                    }
                    roundData.actionTicks.Add(tick);
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
        string gamestateId = UserDataManager.Instance.GamestateId;
        if (!string.IsNullOrEmpty(gamestateId)) {
            GameStateNetworkManager.Instance.RemoveSpectator(gamestateId);
        }
        StopSpectating();
        WindowsManager.Instance.SwitchWindow("menu");
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

            if (TryDequeueAndShowLiveAsk(out float askDelay)) {
                yield return new WaitForSeconds(askDelay / autoPlaySpeed);
                continue;
            }

            if (!HasMoreSpectatorTicks()) {
                int nextRound = currentRoundIndex + 1;
                if (gameRecord.gameRound.rounds.ContainsKey(nextRound)) {
                    liveAskQueue.Clear();
                    EndResultPanel.Instance.ClearEndResultPanel();
                    GotoSelectRound(nextRound, false);
                    continue;
                }
                waitingForMoreTicks = true;
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            string nextAction = PeekNextTickAction();
            float delay = GetSpectatorDelay(nextAction);
            yield return new WaitForSeconds(delay / autoPlaySpeed);
            if (!IsSpectating) yield break;
            SpectatorNextAction();
        }
    }

    private bool TryDequeueAndShowLiveAsk(out float delay) {
        delay = 0.5f;
        while (liveAskQueue.Count > 0 && liveAskQueue.Peek().targetNode < currentNode) {
            liveAskQueue.Dequeue();
        }
        if (liveAskQueue.Count == 0) return false;
        LiveAskEvent nextAsk = liveAskQueue.Peek();
        if (nextAsk.targetNode != currentNode) return false;
        List<string> tick = liveAskQueue.Dequeue().tick;
        if (tick == null || tick.Count == 0) return false;

        string action = tick[0];
        if (action == "ask_hand") {
            HandleSpectatorAskHand(tick);
            delay = GetSpectatorDelay("ask_hand");
            return true;
        }
        if (action == "ask_other") {
            HandleSpectatorAskOther(tick);
            delay = GetSpectatorDelay("ask_other");
            return true;
        }
        return false;
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
            case "ask_hand": return 0.8f;
            case "ask_other": return 0.6f;
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
            case "hu_self":
            case "hu_first":
            case "hu_second":
            case "hu_third": return 6.0f;
            case "liuju": return 4.0f;
            case "end": return 0.5f;
            default: return 0.3f;
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

        // 非 ask 行动一旦发生，清理 ask 按钮展示
        ClearSpectatorAskUI();

        if (action == "end") {
            EndResultPanel.Instance.ClearEndResultPanel();
            currentNode++;
            int nextRound = currentRoundIndex + 1;
            if (gameRecord.gameRound.rounds.ContainsKey(nextRound)) {
                GotoSelectRound(nextRound, false);
            }
        } else if (action == "liuju") {
            GameSceneUIManager.Instance.ShowEndLiuju();
            currentNode++;
            UpdateCurrentXunmuText();
        } else {
            NextAction();
        }
    }

    private void ClearSpectatorAskUI() {
        GameCanvas.Instance.ClearActionButton();
    }

    private void HandleSpectatorAskHand(List<string> tick) {
        if (tick.Count < 3) return;
        int askPlayerIndex = ParseTickInt(tick, 1);
        currentPlayerIndex = askPlayerIndex;
        if (indexToPosition.ContainsKey(askPlayerIndex)) {
            string pos = indexToPosition[askPlayerIndex];
            BoardCanvas.Instance.ShowCurrentPlayer(pos, currentTilesList.Count);
        }

        // 与正常对局一致：只有“当前选中视角玩家”可见自己的 ask_hand 操作按钮
        if (askPlayerIndex != selectedPlayerIndex) {
            ClearSpectatorAskUI();
            return;
        }

        List<string> options = FilterAskHandActions(ParseCommaSeparatedActions(tick[2]));
        if (options.Count > 0) {
            GameCanvas.Instance.ShowSpectatorActionButtons(options);
        } else {
            ClearSpectatorAskUI();
        }
    }

    private void HandleSpectatorAskOther(List<string> tick) {
        if (tick.Count < 3) return;
        string info = tick[2];
        if (string.IsNullOrEmpty(info)) return;

        List<string> selectedActions = null;
        string[] playerEntries = info.Split(';');
        foreach (string entry in playerEntries) {
            if (string.IsNullOrEmpty(entry)) continue;
            string[] parts = entry.Split(':');
            if (parts.Length < 2) continue;
            if (!int.TryParse(parts[0], out int playerIdx)) continue;
            if (playerIdx != selectedPlayerIndex) continue;
            selectedActions = ParseCommaSeparatedActions(parts[1]);
            break;
        }

        // 与正常对局一致：只显示“自己”（当前选中视角）可执行的 ask_other 按钮
        if (selectedActions == null || selectedActions.Count == 0) {
            ClearSpectatorAskUI();
            return;
        }

        List<string> options = FilterAskOtherActions(selectedActions);
        if (options.Count > 0) {
            GameCanvas.Instance.ShowSpectatorActionButtons(options);
        } else {
            ClearSpectatorAskUI();
        }
    }

    private static List<string> FilterAskHandActions(List<string> raw) {
        string[] allow = new[] { "cut", "buhua", "hu_self", "angang", "jiagang", "pass" };
        return FilterByWhitelist(raw, allow);
    }

    private static List<string> FilterAskOtherActions(List<string> raw) {
        string[] allow = new[] { "chi_left", "chi_mid", "chi_right", "peng", "gang", "hu_first", "hu_second", "hu_third", "pass" };
        return FilterByWhitelist(raw, allow);
    }

    private static List<string> FilterByWhitelist(List<string> raw, string[] allow) {
        var result = new List<string>();
        if (raw == null || allow == null) return result;
        var set = new HashSet<string>(allow);
        foreach (string a in raw) {
            if (set.Contains(a)) {
                result.Add(a);
            }
        }
        return result;
    }

    private static List<string> ParseCommaSeparatedActions(string commaSeparated) {
        var list = new List<string>();
        if (string.IsNullOrEmpty(commaSeparated)) return list;
        foreach (string s in commaSeparated.Split(',')) {
            string t = s?.Trim();
            if (!string.IsNullOrEmpty(t)) list.Add(t);
        }
        return list;
    }

    private void JumpToLatestState() {
        if (gameRecord?.gameRound?.rounds == null) return;
        int lastRound = 1;
        foreach (var kvp in gameRecord.gameRound.rounds) {
            if (kvp.Key > lastRound) lastRound = kvp.Key;
        }
        if (gameRecord.gameRound.rounds.TryGetValue(lastRound, out Round roundData) &&
            roundData.actionTicks != null && roundData.actionTicks.Count > 0) {
            currentRoundIndex = lastRound;
            InitGameRound(lastRound);
            GotoAction(roundData.actionTicks.Count);
        }
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
        long roundSeed = 0;
        if (gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round rd)) {
            currentRoundActionCount = rd.actionTicks?.Count ?? 0;
            tileWallCount = rd.tilesList?.Count ?? 0;
            currentRoundLabel = rd.currentRound;
            roundSeed = rd.roundRandomSeed;
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
        sb.AppendLine($"本局随机种子: {roundSeed}");
        sb.AppendLine($"直播询问缓存: {liveAskQueue.Count}");
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

        quitRecordButton.gameObject.SetActive(inRecord);
        quitSpectatorButton.gameObject.SetActive(inSpectator);
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
        round.roundIndex = roundData["round_index"]?.Value<int>() ?? roundIndex;
        round.roundRandomSeed = roundData["round_random_seed"]?.Value<long>() ?? 0;
        round.currentRound = roundData["current_round"]?.Value<int>() ?? 0;

        if (gameRecord?.gameTitle != null) {
            round.p0UserId = SafeGetInt(gameRecord.gameTitle, "p0_uid");
            round.p1UserId = SafeGetInt(gameRecord.gameTitle, "p1_uid");
            round.p2UserId = SafeGetInt(gameRecord.gameTitle, "p2_uid");
            round.p3UserId = SafeGetInt(gameRecord.gameTitle, "p3_uid");
        }

        round.p0Tiles = roundData["p0_tiles"]?.ToObject<List<int>>() ?? new List<int>();
        round.p1Tiles = roundData["p1_tiles"]?.ToObject<List<int>>() ?? new List<int>();
        round.p2Tiles = roundData["p2_tiles"]?.ToObject<List<int>>() ?? new List<int>();
        round.p3Tiles = roundData["p3_tiles"]?.ToObject<List<int>>() ?? new List<int>();
        round.tilesList = roundData["tiles_list"]?.ToObject<List<int>>() ?? new List<int>();

        round.actionTicks = new List<List<string>>();
        JArray actionTicks = roundData["action_ticks"] as JArray;
        if (actionTicks != null) {
            foreach (JToken tickToken in actionTicks) {
                JArray tickArr = tickToken as JArray;
                if (tickArr == null) continue;
                round.actionTicks.Add(ConvertTickArrayToList(tickArr));
            }
        }

        return round;
    }
}

