using System.Collections.Generic;
using TMPro;
using UnityEngine;

public partial class EndResultPanel {
    private SichuanSettlementPanel sichuanBoardPanel;
    private readonly Dictionary<GameObject,bool> sichuanLegacyVisibility=new Dictionary<GameObject,bool>();
    public bool IsSichuanBoardVisible => sichuanBoardPanel!=null&&sichuanBoardPanel.gameObject.activeInHierarchy;

    public void PromoteSichuanBoardToMatchEnd() {
        if(!IsSichuanBoardVisible)return;
        matchEndMode=true;
        endButtonConfirmed=false;
        EndButton.interactable=true;
    }

    public void PrepareSichuanBoard(SichuanBoardSettlementInfo info,string nextStatus,bool record=false) {
        StopActivePresentation();
        gameObject.SetActive(true);
        currentState=record?StateRecord:StateGame;
        matchEndMode=!record&&nextStatus=="match_end";
        gameResultLifecycleActive=!record;
        endButtonConfirmed=!record&&!matchEndMode&&SettlementSync.SelfReady;
        EndButton.interactable=!endButtonConfirmed;
        if(sichuanBoardPanel==null){
            var root=new GameObject("SichuanBoardSettlement",typeof(RectTransform));root.transform.SetParent(transform,false);
            sichuanBoardPanel=root.AddComponent<SichuanSettlementPanel>();
            sichuanBoardPanel.Build(SelfUserName,StaticCardPrefab,HideSplit);
        }
        foreach(Transform child in transform){
            if(child==sichuanBoardPanel.transform)continue;
            if(!sichuanLegacyVisibility.ContainsKey(child.gameObject))sichuanLegacyVisibility.Add(child.gameObject,child.gameObject.activeSelf);
            child.gameObject.SetActive(false);
        }
        sichuanBoardPanel.transform.SetAsLastSibling();
        sichuanBoardPanel.ShowBoard(info,()=> {
            if(!record&&RoundEndPresentation.Instance.OpenPendingSichuanDuplicateResult())return;
            EndButtonClick();
        },
            ()=>record?"确认":matchEndMode?"确定":SettlementSync.SelfReady?"已准备":SettlementSync.RemainingSeconds is int seconds?$"确定（{seconds}）":"确定",
            ()=>EndButton.interactable, animate:!SettlementSync.IsRestoring,
            remaining:()=>!record&&!matchEndMode&&!SettlementSync.SelfReady?SettlementSync.RemainingSeconds:null);
    }

    private void RestoreSichuanBoardChildren() {
        if(sichuanBoardPanel!=null)sichuanBoardPanel.gameObject.SetActive(false);
        foreach(var item in sichuanLegacyVisibility)if(item.Key!=null)item.Key.SetActive(item.Value);
        sichuanLegacyVisibility.Clear();
    }
}
