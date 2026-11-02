using UnityEngine;

public partial class CreatePanel {
    [SerializeField] private UnityEngine.UI.Toggle GuangdongMinimumScoreToggle;
    [SerializeField] private UnityEngine.UI.Toggle TuidaoPingjiZimoNoChowToggle;
    private void RefreshGuangdongControls() {
        if (GuangdongMinimumScoreToggle) GuangdongMinimumScoreToggle.gameObject.SetActive(_ruleState=="guangdong" && GetSelectedSubRule()==GuangdongMilRules.SubRule);
        if (TuidaoPingjiZimoNoChowToggle) TuidaoPingjiZimoNoChowToggle.gameObject.SetActive(_ruleState=="guangdong" && GetSelectedSubRule()==TuidaoRuleBootstrap.SubRule);
    }
#if UNITY_EDITOR
    public void BakeGuangdongRoomControls() {
        GuangdongMinimumScoreToggle=EnsureClonedToggle(tipsToggle,GuangdongMinimumScoreToggle,"GuangdongMinimumScore","至少4分起和",true);
        GuangdongMinimumScoreToggle.group=null; GuangdongMinimumScoreToggle.onValueChanged=new UnityEngine.UI.Toggle.ToggleEvent();
        GuangdongMinimumScoreToggle.transform.SetAsLastSibling(); GuangdongMinimumScoreToggle.gameObject.SetActive(false);
        TuidaoPingjiZimoNoChowToggle=EnsureClonedToggle(tipsToggle,TuidaoPingjiZimoNoChowToggle,"TuidaoPingjiZimoNoChow","平鸡自摸禁吃",false);
        TuidaoPingjiZimoNoChowToggle.group=null; TuidaoPingjiZimoNoChowToggle.onValueChanged=new UnityEngine.UI.Toggle.ToggleEvent();
        var pingjiLabel=TuidaoPingjiZimoNoChowToggle.GetComponentInChildren<TMPro.TMP_Text>(true);
        pingjiLabel.enableAutoSizing=true; pingjiLabel.fontSizeMin=28; pingjiLabel.fontSizeMax=38;
        pingjiLabel.textWrappingMode=TMPro.TextWrappingModes.NoWrap;
        TuidaoPingjiZimoNoChowToggle.transform.SetAsLastSibling(); TuidaoPingjiZimoNoChowToggle.gameObject.SetActive(false);
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
