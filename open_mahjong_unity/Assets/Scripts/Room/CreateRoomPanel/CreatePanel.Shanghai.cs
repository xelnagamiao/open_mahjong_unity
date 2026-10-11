using UnityEngine;
using UnityEngine.UI;

public partial class CreatePanel {
    [SerializeField] private Toggle ShanghaiQing3Hun2Peng2Toggle;

    private void RefreshShanghaiControls() {
        if (ShanghaiQing3Hun2Peng2Toggle)
            ShanghaiQing3Hun2Peng2Toggle.gameObject.SetActive(IsShanghaiQiaoma);
    }

#if UNITY_EDITOR
    public void BakeShanghaiRoomControls() {
        ShanghaiQing3Hun2Peng2Toggle = EnsureClonedToggle(InputHepaiLimitToggle,
            ShanghaiQing3Hun2Peng2Toggle, "ShanghaiQing3Hun2Peng2", "清三混二碰二", false);
        ShanghaiQing3Hun2Peng2Toggle.group = null;
        ShanghaiQing3Hun2Peng2Toggle.onValueChanged = new Toggle.ToggleEvent();
        ShanghaiQing3Hun2Peng2Toggle.SetIsOnWithoutNotify(false);
        ShanghaiQing3Hun2Peng2Toggle.transform.SetSiblingIndex(InputHepaiLimitToggle.transform.GetSiblingIndex() + 1);
        RoomGet<CreateRoomHelpTrigger>(ShanghaiQing3Hun2Peng2Toggle.gameObject).Configure(this,
            "清三混二碰二：开启后清一色3番、混一色2番、碰碰和2番；关闭时分别为2番、1番、1番。其他番种、番种叠加和三番封顶保持原规则。");
        ShanghaiQing3Hun2Peng2Toggle.gameObject.SetActive(false);
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
