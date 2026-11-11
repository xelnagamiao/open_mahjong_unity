using TMPro;
using UnityEngine;

public partial class RoundEndPresentation {
    private SichuanDuplicateResultPanel sichuanDuplicateResult;
    private DuplicateMatchInfo pendingSichuanDuplicateInfo;
    private double? pendingSichuanDuplicateReceivedAt;
    private bool sichuanDuplicateReady;

    public bool OpenPendingSichuanDuplicateResult() {
        var info=pendingSichuanDuplicateInfo;
        if(info==null)return false;
        bool ready=sichuanDuplicateReady;
        var receivedAt=pendingSichuanDuplicateReceivedAt;
        pendingSichuanDuplicateInfo=null;
        EndResultPanel.Instance.ClearEndResultPanel();
        ShowSichuanDuplicateResult(info, receivedAt);
        if(ready)sichuanDuplicateResult?.EnableReady();
        return true;
    }


    public void ShowSichuanDuplicateResult(DuplicateMatchInfo info, double? receivedAt = null) {
        if(EndResultPanel.Instance?.IsSichuanBoardVisible==true && SichuanDuplicateResultPanel.ShouldShow(info)) {
            pendingSichuanDuplicateInfo=info;
            pendingSichuanDuplicateReceivedAt=receivedAt;
            // The server starts comparison only after this table finishes local ready.
            // Do not gate it on UI readiness: timeout, bots and reconnect also reach this phase.
            if(info.waiting_for=="comparison"
                || (info.waiting_for=="ready" && EndResultPanel.Instance.AllSichuanPlayersReady))
                OpenPendingSichuanDuplicateResult();
            return;
        }
        if (!SichuanDuplicateResultPanel.ShouldShow(info)) {
            HideSichuanDuplicateResult();
            return;
        }
        ResetSichuanEndgameQueue();
        StopActiveSequence();
        EndResultPanel.Instance?.ClearEndResultPanel();
        EndShuheWeiPanel.Instance?.ClearEndShuheWeiPanel();
        HideSelfGameplayControl(false);
        PreparePresentationRoot(false);
        AutoAction.Instance?.DismissTimeoutReturnIfAny();
        if (sichuanDuplicateResult == null) {
            var host = new GameObject("SichuanDuplicateResult", typeof(RectTransform));
            host.transform.SetParent(transform, false);
            sichuanDuplicateResult = host.AddComponent<SichuanDuplicateResultPanel>();
            sichuanDuplicateResult.Build(EndResultPanel.Instance.GetComponentInChildren<TMP_Text>(true));
        }
        sichuanDuplicateResult.transform.SetAsLastSibling();
        sichuanDuplicateResult.Show(info, receivedAt);
    }

    public void EnableSichuanDuplicateReady() {
        sichuanDuplicateReady=true;
        sichuanDuplicateResult?.EnableReady();
    }
    public void HideSichuanDuplicateResult() {
        pendingSichuanDuplicateInfo=null;
        sichuanDuplicateReady=false;
        pendingSichuanDuplicateReceivedAt=null;
        if (sichuanDuplicateResult != null) sichuanDuplicateResult.gameObject.SetActive(false);
    }
}

