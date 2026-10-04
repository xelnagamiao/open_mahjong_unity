using UnityEngine;

public partial class CreatePanel {
    [SerializeField] private UnityEngine.UI.Toggle GuangdongMinimumScoreToggle;
    private void RefreshGuangdongControls() {
        if (GuangdongMinimumScoreToggle) GuangdongMinimumScoreToggle.gameObject.SetActive(_ruleState=="guangdong" && GetSelectedSubRule()==GuangdongMilRules.SubRule);
    }
#if UNITY_EDITOR
    public void BakeGuangdongRoomControls() {
        GuangdongMinimumScoreToggle=EnsureClonedToggle(tipsToggle,GuangdongMinimumScoreToggle,"GuangdongMinimumScore","至少4分起和",true);
        GuangdongMinimumScoreToggle.group=null; GuangdongMinimumScoreToggle.onValueChanged=new UnityEngine.UI.Toggle.ToggleEvent();
        GuangdongMinimumScoreToggle.transform.SetAsLastSibling(); GuangdongMinimumScoreToggle.gameObject.SetActive(false);
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
