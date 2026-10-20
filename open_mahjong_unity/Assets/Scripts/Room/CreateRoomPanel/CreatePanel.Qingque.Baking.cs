#if UNITY_EDITOR
using System.Linq;
using TMPro;
using UnityEngine;

public partial class CreatePanel {
    [ContextMenu("Bake Qingque Unlimited Rounds")]
    public void BakeQingqueUnlimitedRounds() {
        if (!gameTime4Button) throw new System.InvalidOperationException("缺少全庄战选项");
        var selected = RoomRoundToggles.FirstOrDefault(t => t && t.isOn);
        if (!unlimitedRoundsToggle) {
            unlimitedRoundsToggle = Instantiate(gameTime4Button, gameTime4Button.transform.parent);
            unlimitedRoundsToggle.name = "UnlimitedRounds";
            unlimitedRoundsToggle.onValueChanged = new UnityEngine.UI.Toggle.ToggleEvent();
            unlimitedRoundsToggle.SetIsOnWithoutNotify(false);
        }
        unlimitedRoundsToggle.group = gameTime4Button.group;
        if (selected) selected.SetIsOnWithoutNotify(true);
        unlimitedRoundsToggle.transform.SetSiblingIndex(gameTime4Button.transform.GetSiblingIndex() + 1);
        var label = unlimitedRoundsToggle.GetComponentInChildren<TMP_Text>(true);
        label.text = "无固定时限";
        label.enableAutoSizing = true;
        label.fontSizeMin = 18;
        label.fontSizeMax = 28;
        var help = unlimitedRoundsToggle.GetComponent<CreateRoomHelpTrigger>();
        if (!help) help = unlimitedRoundsToggle.gameObject.AddComponent<CreateRoomHelpTrigger>();
        help.Configure(this, "无固定时限：不限制整场局数，局时和步时照常生效。任一玩家可在右上角结束游戏，本局结束后进行总计结算；本局结束前可回归游戏撤销。其他玩家仍放弃时，仍会在本局后结束。");
        var root = gameTime4Button.transform.parent;
        root.GetComponent<CreateRoomRoundIndicator>().Configure(RoomRoundToggles.Where(t => t).ToArray(),
            root.Find("SelectionFill") as RectTransform, root.GetComponent<CanvasGroup>());
        unlimitedRoundsToggle.gameObject.SetActive(_ruleState == "qingque" && string.IsNullOrEmpty(_venueEventId));
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.EditorUtility.SetDirty(root.GetComponent<CreateRoomRoundIndicator>());
    }
}
#endif
