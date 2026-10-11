using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class CreatePanel {
    [SerializeField] private Toggle YixingSevenPairsToggle;

#if UNITY_EDITOR
    public void BakeYixingRoomControls() {
        YixingSevenPairsToggle=EnsureClonedToggle(tipsToggle,YixingSevenPairsToggle,"YixingSevenPairs","七小对",false);
        YixingSevenPairsToggle.group=null;
        YixingSevenPairsToggle.onValueChanged=new Toggle.ToggleEvent();
        YixingSevenPairsToggle.transform.SetAsLastSibling();
        YixingSevenPairsToggle.gameObject.SetActive(false);
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
