using System.Collections.Generic;

public partial class GuobiaoGameState {
    private int hintDrawTile, hintRonTile, hintDiscarder = -1;
    private bool hintKongDraw, hintRobbingKong, hintInitialDraw;

    private void ResetHintTiming() {
        hintDrawTile = hintRonTile = 0;
        hintDiscarder = -1;
        hintKongDraw = hintRobbingKong = hintInitialDraw = false;
    }

    /// <summary>每次返回新快照，异步提示队列不会被后续动作改变。</summary>
    public GuobiaoHintTiming BuildHintTiming() {
        int total = 0, dealerDiscards = 0;
        bool meld = false;
        foreach (var pair in Mirror.PlayerToInfo) {
            var player = pair.Value;
            total += player?.discard_tiles?.Count ?? 0;
            meld |= player?.combination_tiles?.Count > 0;
            if (pair.Key == Mirror.SeatOf(Session.DealerIndex)) dealerDiscards = player?.discard_tiles?.Count ?? 0;
        }
        // 复式的个人牌山终张另有服务端定义，不能用公共余牌数推测。
        bool last = !Session.IsDuplicate && Mirror.RemainTiles == 0;
        return GuobiaoHintTiming.Create(Session.SubRule, Session.TianDiRenHe, Session.SelfIndex, Session.DealerIndex,
            Mirror.Self.discard_tiles?.Count ?? 0, total, dealerDiscards, meld,
            hintDrawTile, hintRonTile, hintDiscarder, hintKongDraw, hintRobbingKong, last, last, hintInitialDraw);
    }

    private void AcceptHintHandAsk(AskHandActionGBInfo info) {
        ResetHintTiming();
        if (info.player_index != Session.SelfIndex || Mirror.SelfHandTiles.Count % 3 != 2) return;
        // 此协议标记保持到首操作真正完成。之后主动补花仍可天和，但服务端开始计算独听。
        hintInitialDraw = info.opening_buhua_complete == true;
        hintDrawTile = Mirror.LastDealTileId;
        // 庄家初始14张未经过 deal 动作，询问仍是一次真实起手和牌窗口。
        if (hintDrawTile == 0 && info.player_index == Session.DealerIndex) {
            bool opening = true;
            foreach (var player in Mirror.PlayerToInfo.Values)
                opening &= (player?.discard_tiles?.Count ?? 0) == 0 && (player?.combination_tiles?.Count ?? 0) == 0;
            if (opening) hintDrawTile = Mirror.SelfHandTiles[Mirror.SelfHandTiles.Count - 1];
        }
        hintKongDraw = info.deal_tile_type == "deal_gang_tile";
    }

    protected override void OnAskClaim(Response response) {
        base.OnAskClaim(response);
        if (response.ask_other_action_info == null) return;
        hintDrawTile = 0;
        hintKongDraw = false;
        hintRonTile = response.ask_other_action_info.cut_tile;
        hintRobbingKong = Clock.IsQiangGangAsk;
        hintDiscarder = -1;
        foreach (var pair in Mirror.IndexToPosition)
            if (pair.Value == Mirror.LastDiscardPlayerPosition) hintDiscarder = pair.Key;
        // 重连的 game_start 有牌河但不含 LastDiscardPlayerPosition。只在首弃能够唯一确定时恢复来源。
        if (hintDiscarder < 0 && !hintRobbingKong) {
            int total = 0, onlyDiscarder = -1;
            bool anyMeld = false;
            foreach (var pair in Mirror.IndexToPosition) {
                var player = Mirror.Info(pair.Value);
                int count = player?.discard_tiles?.Count ?? 0;
                total += count;
                anyMeld |= player?.combination_tiles?.Count > 0;
                if (count == 1 && WashizuTile.Face(player.discard_tiles[0]) == WashizuTile.Face(hintRonTile))
                    onlyDiscarder = pair.Key;
            }
            if (total == 1 && !anyMeld) hintDiscarder = onlyDiscarder;
        }
        RefreshGuobiaoHintDisplay();
    }

    protected override void OnBeforeActionPlayed(TableAction action) {
        if (!action.IsClaim) ResetHintTiming();
    }

    protected override void OnActionPlayed(TableAction action) {
        if (action.HasWord("cut") && action.PlayerIndex != Session.SelfIndex) {
            hintRonTile = action.CutTile ?? 0;
            hintDiscarder = action.PlayerIndex;
        }
        RefreshGuobiaoHintDisplay();
    }

    private void RefreshGuobiaoHintDisplay() {
        if (!Session.Tips || TipsBlock.Instance == null
            || TipsContainer.Instance == null || Manager == null) return;
        TipsBlock.Instance.ShowTipsBlock(Mirror.SelfHandTiles, Mirror.Self.combination_tiles);
    }
}
