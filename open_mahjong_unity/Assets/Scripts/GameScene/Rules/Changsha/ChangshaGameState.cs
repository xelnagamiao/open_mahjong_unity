using UnityEngine;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// 长沙麻将。对应服务端 game_changsha/ChangshaGameState。
/// 与标准骨架的差别：海底捞月（最后一张暗牌提示 + 海底切牌单独落位）、小胡/大胡分值配置（听牌提示用）。
/// </summary>
public class ChangshaGameState : TurnBasedGameState {
    public const string RuleId = "changsha";

    public static ChangshaGameState Active => RuleRegistry.ActiveGameState as ChangshaGameState;

    /// <summary>庄家不加底分。</summary>
    public bool BaseScoreNoDealer { get; private set; }
    public int SmallHuScore { get; private set; } = 2;
    public int BigHuScore { get; private set; } = 8;
    public ChangshaRuleSettings RuleSettings { get; private set; } = new ChangshaRuleSettings();
    public const string ExchangeSixSubRule = "changsha/exchange_six";
    public ChangshaExchangeSixState ExchangeSixSnapshot { get; private set; }

    public override bool TryChooseAction(string action) {
        if (Session.SubRule == ExchangeSixSubRule && ChangshaExchangeSixActions.TryChoose(action, ExchangeSixSnapshot)) return true;
        return base.TryChooseAction(action);
    }

    protected override bool HandleExtraMessage(string suffix, Response response) {
        if (suffix != "exchange_six_state") return base.HandleExtraMessage(suffix, response);
        var packet = response as ChangshaExchangeSixResponse;
        if (packet?.state?.private_state == null || packet.state.public_state == null) return true;
        if (packet.gamestate_id != Session.GamestateId || Session.SubRule != ExchangeSixSubRule) return true;
        GameCanvas.Instance?.QueueExchangeSixSnapshot(packet.state);
        return true;
    }

    public void ApplyExchangeSixSnapshot(ChangshaExchangeSixState snapshot) {
        if (ExchangeSixSnapshot != null && snapshot.revision < ExchangeSixSnapshot.revision) return;
        ExchangeSixSnapshot = snapshot;
        var hand = snapshot.private_state.hand_tiles ?? new int[0];
        bool changed = !Mirror.SelfHandTiles.SequenceEqual(hand);
        Mirror.SelfHandTiles = new List<int>(hand);
        Mirror.Self.hand_tiles = hand;
        Mirror.Self.hand_tiles_count = hand.Length;
        Mirror.RemainTiles = snapshot.public_state.remaining_tiles;
        if (snapshot.public_state.hand_counts != null) foreach (var entry in snapshot.public_state.hand_counts) {
            var player = Mirror.Info(Mirror.SeatOf(entry.Key));
            if (player != null) player.hand_tiles_count = entry.Value;
        }
        Clock.LastAskActionTick = snapshot.action_tick;
        Clock.AllowActionList = new List<string>(snapshot.private_state.action_list ?? new string[0]);
        Clock.IsSelfActionRequired = Clock.AllowActionList.Count > 0 && !Session.IsRealtimeSpectator;
        Clock.ForbiddenCutTiles.Clear();
        Clock.ForcedCutTiles.Clear();
        Clock.ApplyCurrentPlayerIndicator(Mirror.SeatOf(snapshot.public_state.current_player_index));
        GameSceneMouseInputController.Instance?.SetActionInputPhase(!Clock.IsSelfActionRequired
            ? GameSceneMouseInputController.InputPhaseNone
            : Clock.AllowActionList.Contains("cut") || snapshot.phase == "exchange"
                ? GameSceneMouseInputController.InputPhaseAskHand : GameSceneMouseInputController.InputPhaseAskOther);
        if (Clock.IsSelfActionRequired) GameCanvas.Instance?.LoadingRemianTime(snapshot.remaining_time, snapshot.step_remaining);
        else GameCanvas.Instance?.StopTimeRunning();
        GameCanvas.Instance?.ShowExchangeSixState(snapshot, changed);
        Game3DManager.Instance?.SyncChangshaExchangeHands(snapshot.public_state);
    }

    public override bool CanCutTile(int tileId) {
        if (Session.SubRule != ExchangeSixSubRule || ExchangeSixSnapshot == null) return base.CanCutTile(tileId);
        var own = ExchangeSixSnapshot.private_state;
        if (ExchangeSixSnapshot.phase == "exchange") return (own.action_list ?? new string[0]).Contains("changsha_exchange_six");
        return (own.action_list ?? new string[0]).Contains("cut") && (own.cut_tiles ?? new int[0]).Contains(tileId);
    }

    protected override void OnRoundStarted(GameInfo gameInfo) {
        ExchangeSixSnapshot = null;
        GameCanvas.Instance?.HideExchangeSixState();
        Game3DManager.Instance?.ClearChangshaExchangeHands();
        BaseScoreNoDealer = gameInfo.base_score_no_dealer ?? false;
        SmallHuScore = Mathf.Max(gameInfo.small_hu_score ?? 2, 1);
        BigHuScore = Mathf.Max(gameInfo.big_hu_score ?? 8, 1);
        RuleSettings = ChangshaRuleSettings.FromGameInfo(gameInfo);
        Game3DManager.Instance?.ClearChangshaOpenKongTilesImmediate();
        if (RuleSettings.open_kong_player.HasValue && RuleSettings.open_kong_tiles != null
            && RuleSettings.open_kong_tiles.Length > 0
            && Manager.indexToPosition.TryGetValue(RuleSettings.open_kong_player.Value, out string seat)) {
            Game3DManager.Instance?.ShowChangshaOpenKongTiles(seat, RuleSettings.open_kong_tiles, restore: true);
        }
    }

    /// <summary>剩一张时亮出海底暗牌提示，否则收起。</summary>
    protected override void OnHandAskReceived(AskHandActionGBInfo info) {
        if (info.remain_tiles == 1) {
            Game3DManager.Instance?.ShowChangshaSeaBottomConcealedTile();
        } else {
            Game3DManager.Instance?.ClearChangshaSeaBottomConcealedTile();
        }
    }

    /// <summary>海底切牌：不走普通弃牌落位，单独摆到海底位置并记入镜像。</summary>
    protected override bool TryApplyFamilyWord(TableAction action, string word) {
        if (Session.SubRule == ExchangeSixSubRule && word == "changsha_batch_claim") {
            Game3DManager.Instance?.PrepareChangshaBatchClaim(Mirror.SeatOf(action.CutFromPlayer.Value), action.CutTile.Value, action.Seat);
            return true;
        }
        if (Session.SubRule == ExchangeSixSubRule && word == "changsha_kong_continue") {
            int continuationTile = action.CutTile.Value;
            Mirror.RemoveClaimedDiscard(action.Seat, continuationTile);
            Game3DManager.Instance?.ReturnChangshaContinuationTile(action.Seat, continuationTile);
            if (action.Seat == "self") {
                Mirror.SelfHandTiles.Add(continuationTile);
                GameCanvas.Instance?.ChangeHandCards("GetCard", continuationTile, null, null);
            } else {
                Mirror.Info(action.Seat).hand_tiles_count++;
                Game3DManager.Instance?.Change3DTile("GetCard", continuationTile, 0, action.Seat, false, null);
            }
            return true;
        }
        if (Session.SubRule == ExchangeSixSubRule && word == "cut" && action.CutTiles != null && action.CutTiles.Length > 0) {
            foreach (int replacementTile in action.CutTiles) {
                Mirror.AddDiscard(action.Seat, replacementTile, false);
                Game3DManager.Instance?.Change3DTile("SetDiscardWithoutAnimation", replacementTile, 0, action.Seat, true, null);
            }
            Mirror.LastCutCardID = action.CutTiles[action.CutTiles.Length - 1];
            Mirror.LastDiscardPlayerPosition = action.Seat;
            return true;
        }
        if (word == "deal_gang_tile" && action.DealTiles != null && action.DealTiles.Length > 0
            && System.Array.TrueForAll(action.DealTiles, ChangshaFanText.IsChangshaTileId)) {
            // 服务端仅对公开开杠发送真实数组；普通补张本家用标量，他家数组为0。
            Game3DManager.Instance?.ShowChangshaOpenKongTiles(action.Seat, action.DealTiles);
            if (action.Seat != "self") {
                Mirror.RemainTiles -= action.DealTiles.Length;
                Mirror.Info(action.Seat).hand_tiles_count += action.DealTiles.Length;
                return true;
            }
        }
        if (word != "cut" || !action.SeaBottomDiscard) return false;
        if (!action.CutTile.HasValue || action.CutTile.Value <= 0) {
            Debug.LogError("长沙海底弃牌缺少牌值");
            return true;
        }
        int tile = action.CutTile.Value;
        Game3DManager.Instance?.PlaceChangshaSeaBottomDiscardTile(tile, action.Seat, action.CutClass, action.IsRiichiHorizontal);
        Mirror.LastCutCardID = tile;
        Mirror.LastDiscardPlayerPosition = action.Seat;
        PlayerInfoClass info = Mirror.Info(action.Seat);
        if (info != null) {
            info.discard_tiles.Add(tile);
            info.discard_riichi_flags.Add(action.IsRiichiHorizontal);
        }
        return true;
    }

    protected override void OnTableClosedForSettlement(SettlementEnvelope env) {
        if (Session.SubRule == ExchangeSixSubRule && env.IsHu && env.HuClass != "hu_self" && env.RonDiscarderIndex.HasValue)
            Game3DManager.Instance?.RegisterChangshaPublicRiverTile(Mirror.SeatOf(env.RonDiscarderIndex.Value), env.WinTile);
        Game3DManager.Instance?.ClearChangshaExchangeHands();
        Game3DManager.Instance?.ClearChangshaSeaBottomConcealedTile();
        Game3DManager.Instance?.ClearChangshaOpenKongTiles();
    }

    protected override SettlementEnvelope BuildEnvelope(ShowResultInfo info) {
        SettlementEnvelope env = base.BuildEnvelope(info);
        env.BirdTiles = ChangshaFanText.ResolveBirdTiles(info.bird_tiles);
        env.Extras = null;
        return env;
    }

    protected override void AppendScoreboard(SettlementEnvelope env) {
        if (Session.SubRule != ExchangeSixSubRule) {
            base.AppendScoreboard(env);
            return;
        }
        // 四喜已经即时刷分。历史记完整局差，账面分数按服务端绝对分写入，不能再加一次。
        Presenter.AppendScoreboard(env, null, env.ScoreChanges);
    }

    public override void OnSessionReset() {
        ExchangeSixSnapshot = null;
        GameCanvas.Instance?.HideExchangeSixState();
        Game3DManager.Instance?.ClearChangshaExchangeHands();
        Game3DManager.Instance?.ClearChangshaSeaBottomConcealedTile();
        BaseScoreNoDealer = false;
        SmallHuScore = 2;
        BigHuScore = 8;
        RuleSettings = new ChangshaRuleSettings();
        Game3DManager.Instance?.ClearChangshaOpenKongTilesImmediate();
    }
}
