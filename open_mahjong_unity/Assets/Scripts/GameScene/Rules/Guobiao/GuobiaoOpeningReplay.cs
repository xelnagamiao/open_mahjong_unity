using System.Collections.Generic;

public partial class GameRecordManager {
    internal RuleReplayModule CreateGuobiaoReplayModule() {
        RuleReplayModule module = IsWashizuRealtimeRecord()
            ? CreateRealtimeTransparentReplayModule() : CreateStandardReplayModule();
        var apply = module.ApplyBeforeMutation;
        module.ApplyBeforeMutation = tick => ApplyGuobiaoOpeningBoundary(tick) || apply?.Invoke(tick) == true;
        module.AfterSilentMutation = tick => {
            if (tick[0] != "reset") return;
            GameCanvas.Instance.ConsolidateHand(recordPlayer_to_info["self"].showHandDrawSlotActive);
            Game3DManager.Instance.ConsolidateAllHands();
        };
        module.TryHandleCustomActionTick = tick => {
            if (tick[0] == "blood" && IsBloodBattleRecord()) {
                HandleSichuanLiujuStepReplay(tick);
                return true;
            }
            return false;
        };
        module.PopulateRecordTipsContext = (ctx, selfPlayer, roundData) => {
            if (roundData != null) ctx.GuobiaoTiming = BuildGuobiaoRecordTiming(selfPlayer, roundData, ctx);
        };
        return module;
    }

    private bool ApplyGuobiaoOpeningBoundary(IReadOnlyList<string> tick) {
        // Guobiao already records one reset after its opening flower round.
        // The same event now owns this transition for seek and sequential playback.
        if (tick.Count < 2 || tick[0] != "reset" || !int.TryParse(tick[1], out int first)) return false;
        currentPlayerIndex = first;
        foreach (RecordPlayer player in recordPlayerList) player.MergeDrawSlot(player.playerIndex == first);
        return true;
    }
}
