using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 回合制族的标准骨架（game_start → ask → do → show_result → ready → game_end），对应服务端以
/// wait_action / boardcast 驱动的 *GameState。国标/青雀/日麻/长沙/古典/简单/台湾/四川都继承它，
/// 只重写与标准不同的钩子；本类自身不含任何具体规则名。
///
/// 骨架只做三件事：把服务端消息拆成轮子调用（TableMirror / TurnClock / ActionPlayback / SettlementPresenter）、
/// 在固定位置留出族钩子、以及回答 TurnClock/GameCanvas 的通用查询（CanCutTile / IsSelfLocked）。
/// 族的跨局挂起状态（错和续打、血战续打、定缺……）放在各族子类的实例字段里，随 RuleRegistry 每场重建。
/// </summary>
public class TurnBasedGameState : GameStateBase {
    public override string StateId => RuleRegistry.Current?.RuleId ?? "turn_based";

    public override bool IsActive => Manager != null && Manager.IsGameActive;

    protected static NormalGameStateManager Manager => NormalGameStateManager.Instance;
    protected static GameSession Session => GameSession.Current;
    protected static TableMirror Mirror => TableMirror.Current;
    protected static TurnClock Clock => TurnClock.Current;
    protected static SettlementPresenter Presenter => SettlementPresenter.Current;

    // =====================================================================
    // 入站分发
    // =====================================================================

    public override bool HandleMessage(string suffix, Response response) {
        switch (suffix) {
            case "game_start":
                OnGameStartMessage(response);
                return true;
            case "broadcast_hand_action":
                OnAskHandAction(response);
                return true;
            case "ask_other_action":
                OnAskClaim(response);
                return true;
            case "do_action":
                OnDoAction(response);
                return true;
            case "show_result":
                OnShowResult(response);
                return true;
            case "game_end":
                OnGameEnd(response);
                return true;
            case "ready_status":
                OnReadyStatus(response);
                return true;
            default:
                return HandleExtraMessage(suffix, response);
        }
    }

    /// <summary>族专有后缀（定缺、立直宣告、数和尾……）。默认不认识任何额外后缀。</summary>
    protected virtual bool HandleExtraMessage(string suffix, Response response) => false;

    // =====================================================================
    // 开局
    // =====================================================================

    protected virtual void OnGameStartMessage(Response response) {
        Debug.Log($"游戏开始: {response.message}");
        AutoReconnect.OnGameRestored();
        // InitializeGame 末尾会回调 IGameState.OnGameStart(gameInfo) → OnRoundStarted
        Manager.InitializeGame(response.success, response.message, response.game_info);
    }

    /// <summary>核心完成 GameInfo 装载与通用表现初始化后回调；族在 <see cref="OnRoundStarted"/> 补自己的开局。</summary>
    public sealed override void OnGameStart(GameInfo gameInfo) {
        OnRoundStarted(gameInfo);
    }

    /// <summary>
    /// 一局开始（含重连/进局中）。族在此从 GameInfo 读取自己的字段（本场/宝牌、定缺、分数配置……）
    /// 并恢复自己的桌面表现（立直棒、定缺标记……）。同一场的每局 game_start 都会进来。
    /// </summary>
    protected virtual void OnRoundStarted(GameInfo gameInfo) { }

    // =====================================================================
    // 询问
    // =====================================================================

    protected virtual void OnAskHandAction(Response response) {
        Debug.Log($"收到手牌轮操作信息: {response.ask_hand_action_info}");
        AskHandActionGBInfo info = response.ask_hand_action_info;
        if (info == null) return;
        Clock.LastAskActionTick = info.action_tick;
        OnBeforeAsk();
        Mirror.RemainTiles = info.remain_tiles;
        Clock.SetCutConstraints(info.riichi_candidate_cuts, info.forbidden_cut_tiles, info.forced_cut_tiles);
        OnHandAskReceived(info);
        Manager.AskHandAction(info.remaining_time, info.player_index, info.action_list, info.deal_tile_type);
    }

    protected virtual void OnAskClaim(Response response) {
        Debug.Log($"收到询问弃牌后操作消息: {response.ask_other_action_info}");
        AskOtherActionGBInfo info = response.ask_other_action_info;
        if (info == null) return;
        Clock.LastAskActionTick = info.action_tick;
        OnBeforeAsk();
        // 鸣牌询问不属于任何一次手牌询问：清掉立直候选/禁切（强制切留给下一次手牌询问覆盖）
        Clock.RiichiCandidateCuts.Clear();
        Clock.ForbiddenCutTiles.Clear();
        Manager.AskMingPaiAction(info.remaining_time, info.action_list, info.cut_tile, info.chi_candidates,
            info.is_tactical_recheck == true);
    }

    /// <summary>
    /// 任意询问到达前。挂起"结算后续打"的族（错和 / 血战）在此关闭结算层并恢复操作区；
    /// 标签刷新（refresh_player_tag_list）也会触发同一钩子。
    /// </summary>
    protected virtual void OnBeforeAsk() { }

    /// <summary>手牌轮询问的族附加字段（听牌资格、海底暗牌提示……）。</summary>
    protected virtual void OnHandAskReceived(AskHandActionGBInfo info) { }

    /// <summary>公共 refresh_player_tag_list 已写入镜像/头像后由核心回调。</summary>
    public override void OnPlayerTagsRefreshed() {
        OnBeforeAsk();
    }

    // =====================================================================
    // 动作落桌
    // =====================================================================

    protected virtual void OnDoAction(Response response) {
        Debug.Log($"收到执行操作消息: {response.do_action_info}");
        DoActionInfo info = response.do_action_info;
        if (info == null) return;
        // 服务器侧已消除乱序源头：受保护观众的实际鸣牌按序 await（cut flush 先发 -> meld -> 下一巡 cut），
        // 此处直接派发即可保证逻辑顺序。鸣牌认走的打牌者+牌张由服务器必填下发 cut_from_player / cut_tile。
        PlayAction(TableAction.From(info, Mirror));
    }

    /// <summary>
    /// 回合制动作落桌管线：族前置 → 战术申请帧只发声 → ActionPlayback（族可拦截词）→ 族后置。
    /// </summary>
    protected void PlayAction(TableAction action) {
        AutoActionPolicy.Current.RegisterSelfTimeoutCut(action.Words, action.PlayerIndex, action.IsTimeoutAction);
        OnBeforeActionPlayed(action);
        if (action.IsClaim) {
            ActionPlayback.Current.AnnounceClaim(action);
            return;
        }
        ActionPlayback.Current.Play(action, TryApplyFamilyWord);
        OnActionPlayed(action);
    }

    /// <summary>动作落桌前（含战术申请帧）。</summary>
    protected virtual void OnBeforeActionPlayed(TableAction action) { }

    /// <summary>族对单个动作词的落桌拦截；返回 true 表示已处理，ActionPlayback 不再走通用落桌。</summary>
    protected virtual bool TryApplyFamilyWord(TableAction action, string word) => false;

    /// <summary>动作已落桌（非战术申请帧）。</summary>
    protected virtual void OnActionPlayed(TableAction action) { }

    // =====================================================================
    // 结算
    // =====================================================================

    protected virtual void OnShowResult(Response response) {
        Debug.Log($"收到显示结算结果消息: {response.show_result_info}");
        ShowResultInfo info = response.show_result_info;
        if (info == null) return;
        SettlementEnvelope env = BuildEnvelope(info);
        PresentSettlement(env);
        OnSettlementPresented(env, info);
    }

    /// <summary>把服务端 show_result 装成结算信封。族重写时先调 base 再挂自己的 Extras。</summary>
    protected virtual SettlementEnvelope BuildEnvelope(ShowResultInfo info) {
        return new SettlementEnvelope {
            WinnerIndex = info.hepai_player_index,
            HuClass = info.hu_class,
            ScoresAfter = info.player_to_score,
            ScoreChanges = info.score_changes,
            HuScore = info.hu_score,
            FanLabels = info.hu_fan,
            BaseFu = info.base_fu,
            FuFanList = info.fu_fan_list,
            WinnerHand = info.hepai_player_hand,
            WinnerFlowers = info.hepai_player_huapai,
            WinnerMelds = info.hepai_player_combination_mask,
            BirdTiles = info.bird_tiles,
            WinTile = info.hepai_tile ?? 0,
            MultiRon = info.multi_ron == true,
            SimultaneousHuHands = info.simultaneous_hu_hands,
            Silent = info.silent == true,
            SuppressHandReveal = info.suppress_hand_reveal == true,
            SkipHandReveal = info.skip_hand_reveal == true,
            RecycleDiscard = info.recycle_discard,
            RonDiscarderIndex = info.ron_discarder_index,
            IsQianggang = info.is_qianggang == true,
            DeferScoreSettlement = info.defer_score_settlement == true,
            NextStatus = info.next_status,
            LiujuStep = info.liuju_step,
            LiujuStatus = info.liuju_status,
            LiujuHands = info.liuju_hands,
            LiujuStatusFinal = info.liuju_status_final,
            LiujuHuHands = info.liuju_hu_hands,
            ChaPayerIndex = info.cha_payer_index,
            GangRefundChanges = info.gang_refund_changes,
            LiujuRefund = info.liuju_refund,
        };
    }

    /// <summary>
    /// 标准结算呈现：起手和 → 只弹面板；否则收桌 → 记计分板 → 按 hu_class 走 流局 / 荒牌 / 特殊流局 / 和牌。
    /// 走法完全不同的族（血战分步结算）整体重写本方法；只差一两步的族重写下面的细钩子。
    /// </summary>
    protected virtual void PresentSettlement(SettlementEnvelope env) {
        Manager.BeginSettlement();
        Presenter.BeginLifecycle(env);
        if (env.HuClass == "initial_hu") {
            Presenter.PresentInitialHu(env);
            return;
        }
        Presenter.CloseTableForSettlement();
        OnTableClosedForSettlement(env);
        AppendScoreboard(env);

        if (env.IsLiuju) {
            Presenter.PresentLiuju("流局");
            return;
        }
        if (env.HuClass == "ryuukyoku") {
            Presenter.PresentDrawWall("荒牌流局", env);
            return;
        }
        string specialCaption = GetSpecialLiujuCaption(env.HuClass);
        if (specialCaption != null) {
            Presenter.ApplyScores(env.ScoresAfter);
            Presenter.PresentLiuju(specialCaption);
            return;
        }
        OnBeforeHuPresented(env);
        if (env.IsMatchEnd) {
            Manager.MarkAwaitingMatchEnd();
        }
        Presenter.PresentHu(env);
    }

    /// <summary>收桌之后、记计分板之前（清族桌面表现：立直棒、海底暗牌……）。</summary>
    protected virtual void OnTableClosedForSettlement(SettlementEnvelope env) { }

    /// <summary>写一行计分板。默认通用快照；计分板形态不同的族重写。</summary>
    protected virtual void AppendScoreboard(SettlementEnvelope env) {
        Presenter.AppendScoreboard(env, null);
    }

    /// <summary>非 liuju/ryuukyoku 的特殊流局（九种九牌、四风连打……）标题；返回 null 表示按和牌处理。</summary>
    protected virtual string GetSpecialLiujuCaption(string huClass) => null;

    /// <summary>和牌面板呈现前（标记续打挂起、修正 IsQianggang……）。</summary>
    protected virtual void OnBeforeHuPresented(SettlementEnvelope env) { }

    /// <summary>结算已交呈现层之后的族后置。</summary>
    protected virtual void OnSettlementPresented(SettlementEnvelope env, ShowResultInfo info) { }

    // =====================================================================
    // 终局 / 准备
    // =====================================================================

    protected virtual void OnGameEnd(Response response) {
        Debug.Log($"收到游戏结束消息: {response.game_end_info}");
        GameEndInfo info = response.game_end_info;
        if (info == null) return;
        if (!Session.IsRealtimeSpectator) {
            LocalRecordStore.SavePushedDetail(info.record_detail);
        }
        Manager.GameEnd(info.master_seed, info.commitment, info.salt, info.player_final_data);
    }

    protected virtual void OnReadyStatus(Response response) {
        Debug.Log($"收到准备状态更新: {response.message}");
        if (response.ready_status_info == null) return;
        // EndResultPanel 只在当前对局结算生命周期内接收；分步结算的族步骤间即使面板暂时隐藏也会保留本轮状态。
        if (EndResultPanel.Instance != null) {
            EndResultPanel.Instance.UpdateReadyStatus(response.ready_status_info.player_to_ready);
        }
        if (EndShuheWeiPanel.Instance != null && EndShuheWeiPanel.Instance.gameObject.activeSelf) {
            EndShuheWeiPanel.Instance.UpdateReadyStatus(response.ready_status_info.player_to_ready);
        }
    }

    // =====================================================================
    // 查询（TurnClock / GameCanvas 问族）
    // =====================================================================

    /// <summary>
    /// 通用切牌约束：强制切阶段只允许指定摸入牌；否则按服务端禁切表。族在此之上叠加自己的限制（定缺优先、锁手……）。
    /// </summary>
    public override bool CanCutTile(int tileId) {
        if (Clock.ForcedCutTiles.Count > 0 && Clock.AllowActionList.Contains("cut")) {
            return Clock.ForcedCutTiles.Contains(tileId);
        }
        return !Clock.ForbiddenCutTiles.Contains(tileId);
    }

    public override void OnSessionReset() {
        // 通用运行时状态由 NormalGameStateManager.ResetForExit / TurnClock.ResetForExit 清理；族私有状态由子类重写。
    }

    // =====================================================================
    // 子类工具
    // =====================================================================

    /// <summary>某家 tag_list 是否含指定标签。</summary>
    protected static bool SeatHasTag(string seat, System.Func<string, bool> predicate) {
        PlayerInfoClass info = Mirror.Info(seat);
        string[] tags = info?.tag_list;
        if (tags == null) return false;
        for (int i = 0; i < tags.Length; i++) {
            if (predicate(tags[i])) return true;
        }
        return false;
    }

    /// <summary>番名列表是否含某番。</summary>
    protected static bool FanLabelsContain(string[] fanLabels, string label) {
        if (fanLabels == null) return false;
        for (int i = 0; i < fanLabels.Length; i++) {
            if (fanLabels[i] == label) return true;
        }
        return false;
    }

    /// <summary>刷新各家 tag_list（族广播里夹带的 player_to_tag_list）。</summary>
    protected static void RefreshTags(Dictionary<int, string[]> playerToTagList) {
        if (playerToTagList != null) {
            Manager.RefreshPlayerTagList(playerToTagList);
        }
    }
}
