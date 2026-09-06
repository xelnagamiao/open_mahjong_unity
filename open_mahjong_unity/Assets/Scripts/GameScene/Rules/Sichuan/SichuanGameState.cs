using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 四川麻将（血战到底）。对应服务端 game_sichuan/SichuanGameState。
///
/// 与标准骨架的差别：
/// - 开局定缺询问 / 定缺完成广播；手牌仍含定缺花色时必须先打定缺牌；
/// - 刮风下雨：杠即时分变（动作后飘字）与退杠分；
/// - 血战续打：某家和牌后本盘不结束，收到 round_continues=true 的结算后挂起，待下次询问/标签刷新时关闭结算层恢复行牌
///   （行牌次序、跳过已和牌者均由服务端控制，客户端被动跟随）；
/// - 终局分步结算 reveal_hu / settle_hu / chajiao / cha_refund / final，计分板只写一行；简化计分板不写番种手牌。
/// </summary>
public class SichuanGameState : TurnBasedGameState {
    public const string RuleId = "sichuan";

    public static SichuanGameState Active => RuleRegistry.ActiveGameState as SichuanGameState;

    /// <summary>自家定缺花色（1万/2饼/3条，0未定）。供切牌置灰与听牌提示过滤。</summary>
    public int SelfDingqueSuit { get; set; }

    /// <summary>血战到底：收到 round_continues=true 的和牌结算后挂起，待下次询问时恢复行牌。</summary>
    public bool PendingContinueAfterResult { get; private set; }

    private readonly SichuanEndgameLedger ledger = new SichuanEndgameLedger();

    // =====================================================================
    // 开局
    // =====================================================================

    protected override void OnRoundStarted(GameInfo gameInfo) {
        SelfDingqueSuit = 0;
        PendingContinueAfterResult = false;
        ledger.Reset();
        GameCanvas.Instance.HideDingqueSelection();
        // 重连/进入局中时，从 players_info.dingque_suit 恢复各家定缺标记
        if (gameInfo?.players_info == null) return;
        var map = new Dictionary<int, int>();
        foreach (PlayerInfo p in gameInfo.players_info) {
            if (p == null) continue;
            map[p.player_index] = p.dingque_suit;
        }
        if (map.Count > 0) {
            GameCanvas.Instance.UpdatePlayerDingque(map);
            SetSelfDingqueFromMap(map);
        }
        RefreshSelfStatusIndicators();
    }

    // =====================================================================
    // 定缺
    // =====================================================================

    protected override bool HandleExtraMessage(string suffix, Response response) {
        switch (suffix) {
            case "ask_dingque":
                OnAskDingque(response);
                return true;
            case "dingque_done":
                OnDingqueDone(response);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// 定缺询问。仅本人收到该消息时弹出定缺面板（10 秒倒计时，超时自动选手牌最少花色）。
    /// 必须同步 LastAskActionTick，否则 SendAction("dingque") 会因旧 tick 被服务端丢弃，表现为定缺卡死。
    /// </summary>
    private void OnAskDingque(Response response) {
        Debug.Log($"收到定缺询问: {response.ask_hand_action_info}");
        if (response.ask_hand_action_info != null) {
            Clock.LastAskActionTick = response.ask_hand_action_info.action_tick;
        }
        if (Session.IsRealtimeSpectator) return;
        if (GameRecordManager.Instance.IsSpectating) return;
        GameCanvas.Instance.ClearActionButton();
        GameCanvas.Instance.ShowDingqueSelection(10);
    }

    /// <summary>定缺完成广播，按 player_to_dingque 同步各家头像旁的定缺标记。</summary>
    private void OnDingqueDone(Response response) {
        Debug.Log($"收到定缺完成: {response.show_result_info?.player_to_dingque}");
        if (response.show_result_info == null) return;
        GameCanvas.Instance.HideDingqueSelection();
        GameCanvas.Instance.UpdatePlayerDingque(response.show_result_info.player_to_dingque);
        SetSelfDingqueFromMap(response.show_result_info.player_to_dingque);
        // 定缺完成后若已有手牌且轮到操作，立刻刷新定缺置灰（不等 askHandAction）
        GameCanvas.Instance.RefreshHandTileSelectability();
    }

    private void SetSelfDingqueFromMap(Dictionary<int, int> playerToDingque) {
        if (playerToDingque != null && playerToDingque.TryGetValue(Session.SelfIndex, out int suit)) {
            SelfDingqueSuit = suit;
        }
    }

    public override int ExcludedSuit => SelfDingqueSuit >= 1 && SelfDingqueSuit <= 3 ? SelfDingqueSuit : 0;

    /// <summary>顺和标记：自家 tag 含 shunhe_N（不可点和 ≤N 番，听牌跳过和牌后生效，摸牌解除）。</summary>
    public override void RefreshSelfStatusIndicators() {
        GameCanvas canvas = GameCanvas.Instance;
        if (canvas == null) return;
        int? capFan = null;
        string[] tags = Mirror.Info("self")?.tag_list;
        if (tags != null) {
            foreach (string tag in tags) {
                if (tag != null && tag.StartsWith("shunhe_")
                    && int.TryParse(tag.Substring("shunhe_".Length), out int fan)
                    && (!capFan.HasValue || fan > capFan.Value)) {
                    capFan = fan;
                }
            }
        }
        canvas.SetSelfStatusIndicator(GameCanvas.StatusSlotShunhe, capFan.HasValue, capFan.HasValue ? $"顺和:{capFan.Value}番" : null);
    }

    public bool IsDingqueSuitTile(int tileId) {
        return SelfDingqueSuit >= 1 && SelfDingqueSuit <= 3 && tileId / 10 == SelfDingqueSuit;
    }

    /// <summary>手牌仍有定缺花色时须优先打出（类比日麻立直锁手仅可打摸入牌）。</summary>
    public bool MustCutDingqueFirst() {
        if (SelfDingqueSuit < 1 || SelfDingqueSuit > 3) return false;
        return GameCanvas.Instance.SelfHandHasDingqueSuitTile(SelfDingqueSuit);
    }

    public override bool CanCutTile(int tileId) {
        if (!base.CanCutTile(tileId)) return false;
        return !MustCutDingqueFirst() || IsDingqueSuitTile(tileId);
    }

    // =====================================================================
    // 血战续打
    // =====================================================================

    protected override void OnBeforeAsk() {
        TryResumeAfterContinue();
    }

    /// <summary>
    /// 血战续打恢复：关闭结算演出与和牌面板，恢复自家操作并重同步 3D 手牌。
    /// 不还原和牌者手牌（和牌者已退场）。和牌面板仍在倒计时展示时等面板自行结束（EndResultPanel 结束时会再调一次）。
    /// </summary>
    public void TryResumeAfterContinue() {
        if (!PendingContinueAfterResult) return;
        if (EndResultPanel.Instance != null && EndResultPanel.Instance.gameObject.activeSelf) {
            return;
        }
        PendingContinueAfterResult = false;

        RoundEndPresentation.Instance.StopActiveSequence();
        RoundEndPresentation.Instance.ResetSichuanEndgameQueue();
        if (EndResultPanel.Instance != null) {
            EndResultPanel.Instance.ClearEndResultPanel();
        }
        RoundEndPresentation.Instance.ShowSelfGameplayControlAndResyncHand3D();
    }

    // =====================================================================
    // 刮风下雨
    // =====================================================================

    private static bool ContainsGangAction(string[] words) {
        if (words == null) return false;
        for (int i = 0; i < words.Length; i++) {
            if (words[i] == "angang" || words[i] == "jiagang" || words[i] == "gang") return true;
        }
        return false;
    }

    /// <summary>杠即时分变：累加分数并飘字。</summary>
    protected override void OnActionPlayed(TableAction action) {
        if (action.Silent || !ContainsGangAction(action.Words)) return;
        if (!GameCanvas.HasNonZeroGangScoreChanges(action.GangScoreChanges)) return;
        Presenter.ApplyScoreDeltas(action.GangScoreChanges);
        GameCanvas.Instance.ShowGangScoreFloats(action.GangScoreChanges);
    }

    // =====================================================================
    // 结算
    // =====================================================================

    protected override void PresentSettlement(SettlementEnvelope env) {
        Manager.BeginSettlement();
        Presenter.BeginLifecycle(env);
        if (env.HuClass == "initial_hu") {
            Presenter.PresentInitialHu(env);
            return;
        }
        Presenter.CloseTableForSettlement();

        bool isMidGameHu = env.DeferScoreSettlement && env.IsHu;
        bool isEndgameScoreStep = SichuanEndgameLedger.IsEndgameScoreStep(env.LiujuStep);

        // 血战中途和牌/抢杠：杠上炮等即时退回刮风下雨分并飘字（流局 cha_refund 由面板分支单独处理）
        if (env.LiujuStep != "cha_refund" && GameCanvas.HasNonZeroGangScoreChanges(env.GangRefundChanges)) {
            Presenter.ApplyScoreDeltas(env.GangRefundChanges);
            GameCanvas.Instance.ShowGangScoreFloats(env.GangRefundChanges);
        }

        // 计分板：终局分步只写一行；中途和牌不写（终局统一结算）；其余走简化计分板
        if (isEndgameScoreStep) {
            if (env.LiujuStep == "reveal_hu") {
                ledger.Begin();
            } else if (env.LiujuStep == "settle_hu") {
                ledger.Accumulate(env.ScoreChanges);
                ledger.RecordHu();
            } else if (env.LiujuStep == "chajiao") {
                ledger.MarkChajiaoStep();
                ledger.Accumulate(env.ScoreChanges);
            }
            if (env.LiujuStatusFinal && ledger.TryFlushToHistory(Session.SubRule, Mirror.RoundSettlementHistory)) {
                GameSceneUIManager.Instance.UpdateScoreRecord();
            }
        } else if (!isMidGameHu) {
            AppendScoreboard(env);
        }

        if (env.IsLiuju) {
            PresentLiujuStep(env);
            return;
        }
        if (env.LiujuStep == "settle_hu") {
            RoundEndPresentation.Instance.EnqueueSichuanSettleHu(
                env.WinnerIndex, env.ScoresAfter, env.HuScore, env.FanLabels, env.HuClass,
                env.WinnerHand, env.WinnerMelds, env.ScoreChanges, env.LiujuStatusFinal);
            if (env.IsMatchEnd) {
                Manager.MarkAwaitingMatchEnd();
            }
            Presenter.ApplyScores(env.ScoresAfter);
            return;
        }

        // 抢杠番名也视作抢杠倒牌（服务端未显式给 is_qianggang 的旧包）
        if (!env.IsQianggang && FanLabelsContain(env.FanLabels, "抢杠")) {
            env.IsQianggang = true;
        }
        if (env.IsMatchEnd && !isMidGameHu) {
            Manager.MarkAwaitingMatchEnd();
        }
        Presenter.PresentHu(env);
    }

    /// <summary>终局流局子步骤：reveal_hu 亮和牌者手牌 → chajiao 逐家查叫 → cha_refund 退杠分 → final。</summary>
    private void PresentLiujuStep(SettlementEnvelope env) {
        switch (env.LiujuStep) {
            case "reveal_hu":
                if (env.LiujuHuHands != null && env.LiujuHuHands.Count > 0) {
                    RoundEndPresentation.Instance.ResetSichuanEndgameQueue();
                    RoundEndPresentation.Instance.EnqueueSichuanRevealHu(env.LiujuHuHands);
                }
                break;
            case "chajiao": {
                if (env.LiujuStatus == null) break;
                int focusIndex = env.WinnerIndex;
                string statusKey = "no_ting";
                int[] hand = null;
                if (env.LiujuStatus.TryGetValue(focusIndex, out string st)) {
                    statusKey = st;
                }
                if (env.LiujuHands != null && env.LiujuHands.TryGetValue(focusIndex, out int[] focusedHand)) {
                    hand = focusedHand;
                }
                RoundEndPresentation.Instance.EnqueueSichuanChajiao(
                    focusIndex, statusKey, hand, env.WinnerMelds, env.ScoresAfter, env.ScoreChanges, env.LiujuStatusFinal, env.LiujuRefund);
                if (env.IsMatchEnd) {
                    Manager.MarkAwaitingMatchEnd();
                }
                Presenter.ApplyScores(env.ScoresAfter);
                break;
            }
            case "cha_refund":
                Presenter.ApplyScores(env.ScoresAfter);
                if (GameCanvas.HasNonZeroGangScoreChanges(env.ScoreChanges)) {
                    GameCanvas.Instance.ShowGangScoreFloats(env.ScoreChanges);
                }
                RoundEndPresentation.Instance.EnqueueSichuanChaRefund(env.ScoresAfter, env.ScoreChanges, env.LiujuStatusFinal);
                break;
            case "final":
                Presenter.ApplyScores(env.ScoresAfter);
                break;
            default:
                // 服务端在 reveal_hu 之外用 liuju + 未知步骤时不弹通用"流局"字幕，与迁出前行为一致
                break;
        }
    }

    /// <summary>简化计分板：不写番种/手牌，主番留空。</summary>
    protected override void AppendScoreboard(SettlementEnvelope env) {
        Dictionary<int, int> scoreChanges = env.ScoreChanges;
        if (scoreChanges == null || scoreChanges.Count == 0) {
            scoreChanges = Presenter.BuildScoreChangesFromScoresAfter(env.ScoresAfter);
        }
        SichuanEndgameLedger.AppendSimpleScoreboard(Session.SubRule, Mirror.RoundSettlementHistory, env.HuClass, scoreChanges);
        GameSceneUIManager.Instance.UpdateScoreRecord();
    }

    /// <summary>血战到底：本盘未结束（仍有玩家继续行牌）→ 挂起结算层，待下次询问时关闭并续打。</summary>
    protected override void OnSettlementPresented(SettlementEnvelope env, ShowResultInfo info) {
        PendingContinueAfterResult = info.round_continues == true;
    }

    public override void OnSessionReset() {
        SelfDingqueSuit = 0;
        PendingContinueAfterResult = false;
        ledger.Reset();
        if (GameCanvas.Instance != null) GameCanvas.Instance.HideDingqueSelection();
    }
}
