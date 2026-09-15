using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 日本麻将（立直）。对应服务端 game_riichi/RiichiGameState。
/// 与标准骨架的差别：本场/场供/宝牌场况、立直宣告与立直棒表现、立直锁手与立直选牌、途中流局标题、
/// 结算 extras（翻/符/宝牌/里宝/本场/供托）、荒牌流局听牌申报。错和为局终重开，不走局中续打。
/// </summary>
public class RiichiGameState : TurnBasedGameState {
    public const string RuleId = "riichi";

    public static RiichiGameState Active => RuleRegistry.ActiveGameState as RiichiGameState;

    // ---- 场况 ----
    public int Honba { get; private set; }
    public int RiichiSticks { get; private set; }
    public List<int> DoraIndicators { get; private set; } = new List<int>();
    public List<int> KanDoraIndicators { get; private set; } = new List<int>();
    /// <summary>和牌方式 head_bump / multi_ron / three_ron_abort。</summary>
    public string HepaiWay { get; private set; } = "multi_ron";
    public bool RedDora { get; private set; }
    public int DealerIndex { get; private set; }

    public static bool IsReadyLockTag(string tag) => tag == "riichi" || tag == "daburu_riichi";

    // =====================================================================
    // 开局
    // =====================================================================

    protected override void OnRoundStarted(GameInfo gameInfo) {
        Honba = gameInfo.honba ?? 0;
        RiichiSticks = gameInfo.riichi_sticks ?? 0;
        DoraIndicators = gameInfo.dora_indicators != null ? new List<int>(gameInfo.dora_indicators) : new List<int>();
        KanDoraIndicators = gameInfo.kan_dora_indicators != null ? new List<int>(gameInfo.kan_dora_indicators) : new List<int>();
        HepaiWay = gameInfo.hepai_way ?? "multi_ron";
        RedDora = gameInfo.red_dora ?? false;
        DealerIndex = gameInfo.dealer_index ?? 0;

        // 重连/初始化时：已立直的玩家直接放置立直棒（无飞行动画）
        if (gameInfo.players_info != null) {
            foreach (PlayerInfo player in gameInfo.players_info) {
                if (player?.tag_list == null || !Mirror.IndexToPosition.TryGetValue(player.player_index, out string seat)) continue;
                for (int i = 0; i < player.tag_list.Length; i++) {
                    if (IsReadyLockTag(player.tag_list[i])) {
                        Game3DManager.Instance.PlaceRiichiTenbouAt(seat);
                        break;
                    }
                }
            }
        }

        TipsContainer.Instance.ResetRyuukyokuTenpaiChoiceForRound();
        TipsContainer.Instance.HideRyuukyokuTenpaiChoice();
        RefreshSelfStatusIndicators();
    }

    // =====================================================================
    // 族专有广播
    // =====================================================================

    protected override bool HandleExtraMessage(string suffix, Response response) {
        switch (suffix) {
            case "declare_riichi":
                OnRiichiDeclared(response);
                return true;
            case "update_dora":
                Debug.Log($"收到宝牌更新: {response.message}");
                OnDoraUpdated(response.dora_indicators, response.kan_dora_indicators);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// 立直宣告广播：刷新玩家 tag_list、播放立直语音、立直棒从 outputPos 飞向 tenbouPos，
    /// 并把场供立直棒 +1 同步到 RoundPanel；服务端的供托结算在 _commit_pending_riichi 与和牌/抽水时处理，此处仅做表现。
    /// </summary>
    private void OnRiichiDeclared(Response response) {
        Debug.Log($"收到立直宣告: {response.message}");
        RefreshPlayerTagListInfo info = response.refresh_player_tag_list_info;
        RefreshTags(info?.player_to_tag_list);
        RiichiSticks += 1;
        RefreshRoundPanel();
        int? declarer = info?.riichi_declared_player_index;
        if (declarer.HasValue && Mirror.IndexToPosition.TryGetValue(declarer.Value, out string seat)) {
            SoundManager.Instance.PlayRiichiVoice(seat);
            Game3DManager.Instance.PlayRiichiTenbouFlight(seat);
        }
    }

    /// <summary>宝牌/杠宝牌翻开广播。</summary>
    private void OnDoraUpdated(int[] doraFromServer, int[] kanDoraFromServer) {
        if (doraFromServer != null) DoraIndicators = new List<int>(doraFromServer);
        if (kanDoraFromServer != null) KanDoraIndicators = new List<int>(kanDoraFromServer);
        RefreshRoundPanel();
    }

    private void RefreshRoundPanel() {
        if (RoundPanel.Instance != null) {
            RoundPanel.Instance.RefreshRiichi(Honba, RiichiSticks, DoraIndicators, KanDoraIndicators);
        }
    }

    // =====================================================================
    // 锁手 / 立直选牌
    // =====================================================================

    /// <summary>自家已立直：自动摸切只能打摸入牌，手牌区按此置灰。</summary>
    public override bool IsSelfLocked => SeatHasTag("self", IsReadyLockTag);

    public override void OnAskWindowClosed(AskCloseReason reason) {
        RiichiCutSelectionController.Instance?.ExitRiichiCutMode();
    }

    public override void OnPlayerTagsRefreshed() {
        base.OnPlayerTagsRefreshed();
        if (IsSelfLocked) {
            TipsContainer.Instance.ResetRyuukyokuTenpaiChoiceForRound();
            TipsContainer.Instance.HideRyuukyokuTenpaiChoice();
        }
    }

    /// <summary>振听标记（服务端只向本人同步 furiten）；浪涌子规则下任一家带 langyong_wave 即亮浪潮标记。</summary>
    public override void RefreshSelfStatusIndicators() {
        GameCanvas canvas = GameCanvas.Instance;
        if (canvas == null) return;
        canvas.SetSelfStatusIndicator(GameCanvas.StatusSlotFuriten, SeatHasTag("self", tag => tag == "furiten"));
        bool wave = Session.SubRule == "riichi/langyong" && AnySeatHasTag(tag => tag == "langyong_wave");
        canvas.SetSelfStatusIndicator(GameCanvas.StatusSlotLangyongWave, wave);
    }

    private static bool AnySeatHasTag(System.Func<string, bool> predicate) {
        foreach (string seat in new[] { "self", "right", "top", "left" }) {
            if (SeatHasTag(seat, predicate)) return true;
        }
        return false;
    }

    // =====================================================================
    // 结算
    // =====================================================================

    protected override SettlementEnvelope BuildEnvelope(ShowResultInfo info) {
        SettlementEnvelope env = base.BuildEnvelope(info);
        env.Extras = BuildExtras(info);
        return env;
    }

    /// <summary>从 show_result_info 提取立直扩展信息（han/fu/dora/里宝牌/本场/场供/赤宝牌数）；未携带相关字段时返回 null。</summary>
    public static RiichiEndResultExtras BuildExtras(ShowResultInfo info) {
        bool hasHuExtras = info.han != null || info.fu != null || info.ura_dora_indicators != null || info.honba != null;
        bool hasRyuuExtras = (info.tenpai_tiles != null && info.tenpai_tiles.Count > 0) || info.exhaustive_penalty != null;
        if (!hasHuExtras && !hasRyuuExtras) return null;
        return new RiichiEndResultExtras {
            Han = info.han ?? 0,
            Fu = info.fu ?? 0,
            AkaCount = info.aka_count ?? 0,
            DoraCount = info.dora_count ?? 0,
            UraDoraCount = info.ura_dora_count ?? 0,
            DoraIndicators = info.dora_indicators != null ? new List<int>(info.dora_indicators) : new List<int>(),
            UraDoraIndicators = info.ura_dora_indicators != null ? new List<int>(info.ura_dora_indicators) : new List<int>(),
            Honba = info.honba ?? 0,
            RiichiSticksCollected = info.riichi_sticks_collected ?? 0,
            ScoreChanges = info.score_changes,
            TenpaiTiles = info.tenpai_tiles,
            TenpaiHands = info.tenpai_hands,
            NotenPenaltyAfterDraw = info.exhaustive_penalty ?? false,
            LangyongScoredPoints = info.langyong_scored_points ?? 0,
            LangyongMultiplier = info.langyong_multiplier ?? 0,
        };
    }

    /// <summary>和牌：收供托、清立直棒并刷新场况；荒牌/途中流局：只清桌上立直棒，供托计数留场。</summary>
    protected override void OnTableClosedForSettlement(SettlementEnvelope env) {
        RiichiEndResultExtras extras = env.ExtrasAs<RiichiEndResultExtras>();
        if (extras != null && env.IsHu) {
            if (extras.RiichiSticksCollected > 0) {
                RiichiSticks = 0;
                Game3DManager.Instance.ClearAllRiichiTenbous();
                RefreshRoundPanel();
            }
        } else if (env.HuClass == "ryuukyoku" || SpecialLiujuCaptions.IsSpecialLiuju(env.HuClass)) {
            Game3DManager.Instance.ClearAllRiichiTenbous();
        }
    }

    protected override void AppendScoreboard(SettlementEnvelope env) {
        Presenter.AppendScoreboard(env, env.ExtrasAs<RiichiEndResultExtras>()?.ScoreChanges);
    }

    protected override string GetSpecialLiujuCaption(string huClass) {
        if (huClass == "jiuzhongjiupai") return SpecialLiujuCaptions.JiuzhongjiupaiCaption(RuleId);
        if (SpecialLiujuCaptions.IsRiichiAbort(huClass)) return SpecialLiujuCaptions.RiichiAbortCaption(huClass);
        return null;
    }

    protected override void OnGameEnd(Response response) {
        TipsContainer.Instance.HideRyuukyokuTenpaiChoice();
        base.OnGameEnd(response);
    }

    public override void OnSessionReset() {
        Honba = 0;
        RiichiSticks = 0;
        DoraIndicators = new List<int>();
        KanDoraIndicators = new List<int>();
        if (RiichiCutSelectionController.Instance != null && RiichiCutSelectionController.Instance.IsActive) {
            RiichiCutSelectionController.Instance.ExitRiichiCutMode();
        }
    }
}
