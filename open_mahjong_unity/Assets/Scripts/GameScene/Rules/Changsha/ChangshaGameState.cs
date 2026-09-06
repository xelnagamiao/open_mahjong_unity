using UnityEngine;

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

    protected override void OnRoundStarted(GameInfo gameInfo) {
        BaseScoreNoDealer = gameInfo.base_score_no_dealer ?? false;
        SmallHuScore = Mathf.Max(gameInfo.small_hu_score ?? 2, 1);
        BigHuScore = Mathf.Max(gameInfo.big_hu_score ?? 8, 1);
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
        Game3DManager.Instance?.ClearChangshaSeaBottomConcealedTile();
    }

    protected override SettlementEnvelope BuildEnvelope(ShowResultInfo info) {
        SettlementEnvelope env = base.BuildEnvelope(info);
        env.Extras = ChangshaFanText.BuildBirdExtras(info.bird_tiles, info.hu_fan);
        return env;
    }

    public override void OnSessionReset() {
        Game3DManager.Instance?.ClearChangshaSeaBottomConcealedTile();
        BaseScoreNoDealer = false;
        SmallHuScore = 2;
        BigHuScore = 8;
    }
}
