using System.Collections.Generic;

public partial class GameRecordManager {
    /// <summary>应用补花牌谱中的可选花牌归属转移，并返回动画所需位置。</summary>
    private bool ApplyRecordBuhuaOwnership(
        List<string> tick,
        int actionPlayerIndex,
        int publishedTile,
        out string transferFromPosition,
        out string recipientPosition,
        out int transferTile) {
        int recipientIndex = GameRecordJsonDecoder.ResolveBuhuaRecipient(
            tick, actionPlayerIndex);
        recipientPosition = indexToPosition[recipientIndex];

        bool hasTransfer = GameRecordJsonDecoder.TryResolveBuhuaTransfer(
            tick, out int transferFromIndex, out transferTile);
        bool transfersPublishedFlower = hasTransfer
            && transferFromIndex == actionPlayerIndex
            && transferTile == publishedTile;
        int initialRecipientIndex = transfersPublishedFlower
            ? actionPlayerIndex
            : recipientIndex;
        recordPlayer_to_info[indexToPosition[initialRecipientIndex]]
            .huapaiList.Add(publishedTile);

        transferFromPosition = null;
        if (!hasTransfer) return false;

        transferFromPosition = indexToPosition[transferFromIndex];
        recordPlayer_to_info[transferFromPosition].huapaiList.Remove(transferTile);
        recordPlayer_to_info[recipientPosition].huapaiList.Add(transferTile);
        return true;
    }
}
