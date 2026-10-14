using UnityEngine;
using UnityEngine.UI;

public partial class CreatePanel {
    [SerializeField] private Toggle ShanghaiQing3Hun2Peng2Toggle;
    [SerializeField] private Toggle ShanghaiTianQiaoToggle;
    [SerializeField] private Toggle ShanghaiSelfDrawExtraBaseToggle;

    private void RefreshShanghaiControls() {
        if (ShanghaiQing3Hun2Peng2Toggle)
            ShanghaiQing3Hun2Peng2Toggle.gameObject.SetActive(IsShanghaiQiaoma);
        if (ShanghaiTianQiaoToggle)
            ShanghaiTianQiaoToggle.gameObject.SetActive(IsShanghaiQiaoma);
        if (ShanghaiSelfDrawExtraBaseToggle)
            ShanghaiSelfDrawExtraBaseToggle.gameObject.SetActive(_ruleState == "shanghai" && GetSelectedSubRule() == LajiheHandCalculator.SubRule);
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
        ShanghaiTianQiaoToggle = EnsureClonedToggle(InputHepaiLimitToggle,
            ShanghaiTianQiaoToggle, "ShanghaiTianQiao", "开启天敲", false);
        ShanghaiTianQiaoToggle.group = null;
        ShanghaiTianQiaoToggle.onValueChanged = new Toggle.ToggleEvent();
        ShanghaiTianQiaoToggle.SetIsOnWithoutNotify(false);
        ShanghaiTianQiaoToggle.transform.SetSiblingIndex(ShanghaiQing3Hun2Peng2Toggle.transform.GetSiblingIndex() + 1);
        RoomGet<CreateRoomHelpTrigger>(ShanghaiTianQiaoToggle.gameObject).Configure(this,
            "开启天敲：首次出牌时报敲，且此前无人吃、碰、杠（包括暗杠），和牌加2番。补花不打断资格；与其他番种叠加，仍按三番封顶计分。");
        ShanghaiTianQiaoToggle.gameObject.SetActive(false);
        BakeLajiheRoomControls();
        UnityEditor.EditorUtility.SetDirty(this);
    }

    public void BakeLajiheRoomControls() {
        ShanghaiSelfDrawExtraBaseToggle = EnsureClonedToggle(InputHepaiLimitToggle,
            ShanghaiSelfDrawExtraBaseToggle, "ShanghaiSelfDrawExtraBase", "自摸再加一底", false);
        ShanghaiSelfDrawExtraBaseToggle.group = null;
        ShanghaiSelfDrawExtraBaseToggle.onValueChanged = new Toggle.ToggleEvent();
        ShanghaiSelfDrawExtraBaseToggle.SetIsOnWithoutNotify(false);
        ShanghaiSelfDrawExtraBaseToggle.transform.SetSiblingIndex(ShanghaiTianQiaoToggle.transform.GetSiblingIndex() + 1);
        RoomGet<CreateRoomHelpTrigger>(ShanghaiSelfDrawExtraBaseToggle.gameObject).Configure(this,
            "自摸再加一底：默认关闭。开启后自摸额外再加5分，垃圾和自摸加分为10分，其他牌型为15分，再按番数翻倍。抢杠按自摸计分。关闭时仍为5分／10分。");
        ShanghaiSelfDrawExtraBaseToggle.gameObject.SetActive(false);
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
