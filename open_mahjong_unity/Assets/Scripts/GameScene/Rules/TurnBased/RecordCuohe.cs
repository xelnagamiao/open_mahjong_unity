/// <summary>牌谱局中错和续打。共用机制，放在 TurnBased；仍是 GameRecordManager partial。</summary>
public partial class GameRecordManager {
    private string _pendingRecordCuoheWinnerPosition;

    private static bool HuFanContainsCuohe(string[] huFan) {
        if (huFan == null) return false;
        for (int i = 0; i < huFan.Length; i++) {
            if (huFan[i] == "错和") return true;
        }
        return false;
    }

    private bool UsesMidGameCuoheRecord() {
        if (gameRecord?.gameTitle == null) return false;
        string rule = ReadGameTitleString(gameRecord.gameTitle, "rule", "").ToLowerInvariant();
        string subRule = ReadGameTitleString(gameRecord.gameTitle, "sub_rule", "").ToLowerInvariant();
        return RuleRegistry.Resolve(rule, subRule)?.MidGameCuoheContinues == true;
    }

    private void MarkPendingRecordCuoheContinue(string huPosition, string[] huFan) {
        if (!HuFanContainsCuohe(huFan) || !UsesMidGameCuoheRecord()) return;
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
        Game3DManager.Instance.RestoreRecordPlayerHandAfterCuoheReveal(winnerPos);
    }
}
