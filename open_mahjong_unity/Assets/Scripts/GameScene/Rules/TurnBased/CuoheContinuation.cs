/// <summary>
/// 错和续打（国标/台麻）：某家错和被罚后本盘不结束，服务端在 ready 结束后继续驱动其余玩家行牌。
/// 客户端在错和结算呈现时挂起，等下一次询问 / 标签刷新到来再关闭结算层、还原错和者手牌并恢复本家操作区。
/// 由需要局中续打的族 GameState 持有一个实例；日麻的错和是局终重开，不走这里。
/// </summary>
public sealed class CuoheContinuation {
    public const string FanLabel = "错和";

    /// <summary>错和结算已展示，等待 ready 结束后再恢复手牌与操作区。</summary>
    public bool Pending { get; private set; }
    private int winnerIndex = -1;

    /// <summary>和牌面板呈现前调用：番名含错和则挂起。</summary>
    public void MarkIfCuohe(int hepaiPlayerIndex, string[] fanLabels) {
        if (fanLabels == null) return;
        for (int i = 0; i < fanLabels.Length; i++) {
            if (fanLabels[i] != FanLabel) continue;
            Pending = true;
            winnerIndex = hepaiPlayerIndex;
            return;
        }
    }

    public void Clear() {
        Pending = false;
        winnerIndex = -1;
    }

    /// <summary>错和 ready 结束、服务端恢复本局后：关闭结算层并还原 3D/2D 手牌区。</summary>
    public void TryResume() {
        if (!Pending) return;
        Pending = false;

        // RoundEndPresentation 仅是承载各结算子面板的容器节点（无自身可见图形），正常对局全程都保持 active。
        // 这里只需停止正在播放的结算演出协程，真正的和牌面板由 EndResultPanel.ClearEndResultPanel() 自行隐藏；
        // 不要把容器整个 SetActive(false)，否则后续在未激活节点上 StartCoroutine 会失败，导致错和后的下一次和牌面板不显示。
        RoundEndPresentation.Instance.StopActiveSequence();

        if (EndResultPanel.Instance != null) {
            EndResultPanel.Instance.ClearEndResultPanel();
        }

        if (winnerIndex >= 0
            && TableMirror.Current.IndexToPosition.TryGetValue(winnerIndex, out string winnerPos)) {
            Game3DManager.Instance.RestoreMidGameHandAfterCuoheRonReveal(winnerPos);
        }
        winnerIndex = -1;

        RoundEndPresentation.Instance.ShowSelfGameplayControlAndResyncHand3D();
    }
}
