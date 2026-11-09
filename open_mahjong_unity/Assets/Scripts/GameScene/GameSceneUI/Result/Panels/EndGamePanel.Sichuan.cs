using System.Collections.Generic;
using UnityEngine;

public partial class EndGamePanel {
    private SichuanSettlementPanel sichuanMatchPanel;
    private readonly Dictionary<GameObject,bool> sichuanOriginalChildren=new Dictionary<GameObject,bool>();
    private bool? sichuanOriginalBackground;

    private void RestoreSichuanMatchChildren() {
        if(sichuanMatchPanel!=null)sichuanMatchPanel.gameObject.SetActive(false);
        foreach(var child in sichuanOriginalChildren)if(child.Key!=null)child.Key.SetActive(child.Value);
        sichuanOriginalChildren.Clear();
        var background=GetComponent<UnityEngine.UI.Image>();
        if(background!=null&&sichuanOriginalBackground.HasValue)background.enabled=sichuanOriginalBackground.Value;
        sichuanOriginalBackground=null;
    }
    private void ShowSichuanMatch(SichuanMatchSettlementInfo data) {
        if(data==null)return;
        var background=GetComponent<UnityEngine.UI.Image>();
        if(background!=null){sichuanOriginalBackground=background.enabled;background.enabled=false;}
        if(sichuanMatchPanel==null){
            var root=new GameObject("SichuanMatchSettlement",typeof(RectTransform));root.transform.SetParent(transform,false);
            sichuanMatchPanel=root.AddComponent<SichuanSettlementPanel>();
            sichuanMatchPanel.Build(gameRandomSeed,EndResultPanel.Instance.CardPrefab,EndResultPanel.Instance.HideSplitPrefab);
        }
        foreach(Transform child in transform){
            if(child==sichuanMatchPanel.transform)continue;
            sichuanOriginalChildren[child.gameObject]=child.gameObject.activeSelf;
            child.gameObject.SetActive(false);
        }
        sichuanMatchPanel.ShowMatch(data,OnGoHomeButtonClick,IsRecordResult ? "退出" : "返回主菜单",
            IsRecordResult ? () => recordReturnAction?.Invoke() : null);
        sichuanMatchPanel.transform.SetAsLastSibling();
    }
}
