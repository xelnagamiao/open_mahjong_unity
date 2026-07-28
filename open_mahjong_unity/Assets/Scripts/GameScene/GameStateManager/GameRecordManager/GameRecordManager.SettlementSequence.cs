using System.Collections.Generic;

public partial class GameRecordManager {
    private bool ShouldHoldPreviousHuResult(string nextAction) {
        if (!IsRecordHuResultTick(nextAction)) return false;
        if (!gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData)
            || roundData.actionTicks == null
            || currentNode <= 0
            || currentNode > roundData.actionTicks.Count) {
            return false;
        }
        return IsRecordHuResultTick(GetTickAction(roundData.actionTicks, currentNode - 1));
    }

    private static bool IsRecordHuResultTick(string action) {
        return action == "hu_self" || action == "hu_first"
            || action == "hu_second" || action == "hu_third";
    }

    /// <summary>保留尚未跟随 end 的结算帧，让延时观战正常播放结算。</summary>
    private static int PreserveTrailingUnfinishedSettlements(
        List<List<string>> ticks,
        int catchUpNode) {
        if (ticks == null || catchUpNode != ticks.Count) return catchUpNode;
        while (catchUpNode > 0
            && IsSpectatorSettlementTick(GetTickAction(ticks, catchUpNode - 1))) {
            catchUpNode--;
        }
        return catchUpNode;
    }
}
