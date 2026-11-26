using System.Collections.Generic;

public partial class GameRecordManager {
    internal RuleReplayModule CreateChangshaReplayModule() => new RuleReplayModule {
        TryHandleCustomDraw = (tick, player, position) => {
            if (!ChangshaRecordCodec.IsPublicKongDraw(tick)) return false;
            backwardTilesType = ChangshaRecordCodec.TailTypeAfterPublicDraw(tick, currentTilesList.Count, backwardTilesType);
            ChangshaRecordCodec.ConsumePublicKongDraw(tick, currentTilesList, currentOriginalIndices, consumedBackIndices);
            Game3DManager.Instance.ShowChangshaOpenKongTiles(position, ParseTickIntArray(tick, 5));
            if (position == "self") {
                GameCanvas.Instance.ChangeHandCards("GetCard", ParseTickInt(tick, 1), null, null);
            }
            return true;
        },
        TryApplyRecordRonHuHand = (tick, player) => {
            int changshaHandIndex = ChangshaRecordCodec.OpenKongSelfDrawHandIndex(tick);
            if (changshaHandIndex < 0) return false;
            player.tileList = new List<int>(ParseTickIntArray(tick, changshaHandIndex));
            player.showHandDrawSlotActive = true;
            return true;
        },
        TryBuildRecordWinningHand = (huPlayer, winTile) => {
            huPlayer.tileList = new List<int>(ChangshaRecordCodec.BuildWinningHand(huPlayer.tileList, huPlayer.hasRonWinningTile, winTile));
            huPlayer.hasRonWinningTile = huPlayer.tileList.Count % 3 == 2;
            return true;
        },
        GetHuBirdTiles = tick => ChangshaFanText.ResolveBirdTiles(ParseTickIntArray(tick, ChangshaRecordCodec.BirdIndex(tick))),
        GetHuBatchRon = tick => ChangshaRecordCodec.ParseBatchRon(tick),
        PrepareHuReveal = () => Game3DManager.Instance.PrepareChangshaOpenKongHuReveal(),
        OnSyncCustomRonDiscard = (discardPos, hepaiTile) => {
            if (IsExchangeSixRecord()) Game3DManager.Instance.RegisterChangshaPublicRiverTile(discardPos, hepaiTile);
        },
    };
}
