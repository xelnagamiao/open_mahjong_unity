/// <summary>牌谱国标错和：结算确认或观战续打后恢复和牌者 3D/2D 手牌区。</summary>
public partial class GameRecordManager {
    private string _pendingRecordCuoheWinnerPosition;

    private static bool HuFanContainsCuohe(string[] huFan) {
        if (huFan == null) return false;
        for (int i = 0; i < huFan.Length; i++) {
            if (huFan[i] == "错和") return true;
        }
        return false;
    }

    private bool IsGuobiaoRecordRule() {
        if (gameRecord?.gameTitle == null) return false;
        string subRule = ReadGameTitleString(gameRecord.gameTitle, "sub_rule", "").ToLowerInvariant();
        if (HepaiRevealDirector.IsGuobiaoRuleKey(subRule)) return true;
        string rule = ReadGameTitleString(gameRecord.gameTitle, "rule", "").ToLowerInvariant();
        return HepaiRevealDirector.IsGuobiaoRuleKey(rule);
    }

    private void MarkPendingRecordCuoheContinue(string huPosition, string[] huFan) {
        if (!HuFanContainsCuohe(huFan) || !IsGuobiaoRecordRule()) return;
        _pendingRecordCuoheWinnerPosition = huPosition;
    }

    private void ClearPendingRecordCuoheContinue() {
        _pendingRecordCuoheWinnerPosition = null;
    }

    /// <summary>错和结算确认后、本局继续打牌前：还原和牌者手牌展示（与实时对局 TryResumeAfterCuoheContinue 对齐）。</summary>
    public void TryResumeAfterRecordCuoheContinue() {
        if (string.IsNullOrEmpty(_pendingRecordCuoheWinnerPosition)) return;
        string winnerPos = _pendingRecordCuoheWinnerPosition;
        _pendingRecordCuoheWinnerPosition = null;
        Game3DManager.Instance?.RestoreRecordPlayerHandAfterCuoheReveal(winnerPos);
    }
}
