using TMPro;
using UnityEngine;

public partial class RoundEndPresentation {
    private SichuanDuplicateResultPanel sichuanDuplicateResult;
    private DuplicateMatchInfo pendingSichuanDuplicateInfo;
    private bool sichuanDuplicateReady;

    public bool OpenPendingSichuanDuplicateResult() {
        var info=pendingSichuanDuplicateInfo;
        if(info==null)return false;
        bool ready=sichuanDuplicateReady;
        pendingSichuanDuplicateInfo=null;
        EndResultPanel.Instance.ClearEndResultPanel();
        ShowSichuanDuplicateResult(info);
        if(ready)sichuanDuplicateResult?.EnableReady();
        return true;
    }


    public void ShowSichuanDuplicateResult(DuplicateMatchInfo info) {
        if(EndResultPanel.Instance?.IsSichuanBoardVisible==true && SichuanDuplicateResultPanel.ShouldShow(info)) {
            pendingSichuanDuplicateInfo=info;
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
        sichuanDuplicateResult.Show(info);
    }

    public void EnableSichuanDuplicateReady() {
        sichuanDuplicateReady=true;
        sichuanDuplicateResult?.EnableReady();
    }
    public void HideSichuanDuplicateResult() {
        pendingSichuanDuplicateInfo=null;
        sichuanDuplicateReady=false;
        if (sichuanDuplicateResult != null) sichuanDuplicateResult.gameObject.SetActive(false);
    }
}

