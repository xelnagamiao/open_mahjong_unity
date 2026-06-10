using UnityEngine;

public partial class NormalGameStateManager {
    private static bool HuFanContainsCuohe(string[] huFan) {
        if (huFan == null) return false;
        for (int i = 0; i < huFan.Length; i++) {
            if (huFan[i] == "错和") return true;
        }
        return false;
    }

    private void MarkPendingCuoheContinue(int hepaiPlayerIndex, string[] huFan) {
        if (!HuFanContainsCuohe(huFan)) return;
        // 国标：局中续打；日麻：局终重开，由 game_start 重置，不走此恢复流程
        if (!IsGuobiaoRule()) return;
        pendingCuoheContinueAfterReady = true;
        pendingCuoheWinnerIndex = hepaiPlayerIndex;
    }

    private static bool IsGuobiaoRule() {
        var gsm = Instance;
        if (gsm == null) return false;
        if (gsm.roomRule == "guobiao") return true;
        return !string.IsNullOrEmpty(gsm.subRule) && gsm.subRule.StartsWith("guobiao");
    }

    public void ClearPendingCuoheContinue() {
        pendingCuoheContinueAfterReady = false;
        pendingCuoheWinnerIndex = -1;
    }

    /// <summary>错和 ready 结束、服务端恢复本局后：关闭结算层并还原 3D/2D 手牌区。</summary>
    public void TryResumeAfterCuoheContinue() {
        if (!pendingCuoheContinueAfterReady) return;
        pendingCuoheContinueAfterReady = false;

        // RoundEndPresentation 仅是承载各结算子面板的容器节点（无自身可见图形），正常对局全程都保持 active。
        // 这里只需停止正在播放的结算演出协程，真正的和牌面板由 EndResultPanel.ClearEndResultPanel() 自行隐藏；
        // 不要把容器整个 SetActive(false)，否则后续在未激活节点上 StartCoroutine 会失败，导致错和后的下一次和牌面板不显示。
        if (RoundEndPresentation.Instance != null) {
            RoundEndPresentation.Instance.StopActiveSequence();
        }
        if (EndResultPanel.Instance != null) {
            EndResultPanel.Instance.ClearEndResultPanel();
        }

        if (pendingCuoheWinnerIndex >= 0
            && indexToPosition.TryGetValue(pendingCuoheWinnerIndex, out string winnerPos)
            && Game3DManager.Instance != null) {
            Game3DManager.Instance.RestoreMidGameHandAfterCuoheRonReveal(winnerPos);
        }
        pendingCuoheWinnerIndex = -1;

        RoundEndPresentation.Instance?.ShowSelfGameplayControlAndResyncHand3D();
    }
}
