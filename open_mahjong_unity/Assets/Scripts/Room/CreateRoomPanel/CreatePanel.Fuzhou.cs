using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class CreatePanel {
    private TMP_Dropdown _fuzhouBaseScore;
    private static readonly int[] FuzhouBaseScores={10,15,20};
    private void BindFuzhouMainControls() {
        var root=RoomContent.Find("FuzhouSettings");
        _fuzhouBaseScore=root?.GetComponentInChildren<TMP_Dropdown>(true);
        if(!_fuzhouBaseScore) {Debug.LogError("福州麻将主页面设置尚未烘焙。",this);return;}
        _fuzhouBaseScore.onValueChanged.RemoveAllListeners();
        _fuzhouBaseScore.ClearOptions();_fuzhouBaseScore.AddOptions(FuzhouBaseScores.Select(x=>$"{x}分").ToList());
        _fuzhouBaseScore.SetValueWithoutNotify(1);RoomDropdownTemplateHeight(_fuzhouBaseScore);
        RefreshFuzhouControls();
    }
    private void RefreshFuzhouControls() {
        var root=RoomContent?RoomContent.Find("FuzhouSettings"):null;
        if(root) root.gameObject.SetActive(_ruleState==FuzhouGameState.RuleId);
    }
    private Dictionary<string,object> FuzhouConfigValues()=>new Dictionary<string,object> {
        {"base_score",FuzhouBaseScores[Mathf.Clamp(_fuzhouBaseScore?_fuzhouBaseScore.value:1,0,2)]},
    };
    private void ResetFuzhouControls() {if(_fuzhouBaseScore) _fuzhouBaseScore.SetValueWithoutNotify(1);}
#if UNITY_EDITOR
    public void BakeFuzhouMainControls() {
        var root=RoomContent.Find("FuzhouSettings") as RectTransform;
        if(!root) root=RoomRect("FuzhouSettings",RoomContent);
        root.SetAsLastSibling();root.SetSiblingIndex(RoomContent.Find("ToggleContainer").GetSiblingIndex()+1);
        var templateGrid=RoomContent.Find("ChioceDropdown").GetComponent<CreateRoomAdaptiveGrid>();
        var grid=RoomGet<CreateRoomAdaptiveGrid>(root.gameObject);
        grid.padding=new RectOffset(templateGrid.padding.left,templateGrid.padding.right,templateGrid.padding.top,templateGrid.padding.bottom);
        grid.spacing=templateGrid.spacing;grid.cellSize=templateGrid.cellSize;grid.minimumCellWidth=templateGrid.minimumCellWidth;grid.childAlignment=templateGrid.childAlignment;
        RoomGet<ContentSizeFitter>(root.gameObject).verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        var row=root.Find("BaseScore") as RectTransform;
        if(!row) row=RoomRect("BaseScore",root);
        var templateLabel=stepTimer.transform.parent.GetComponentsInChildren<TMP_Text>(true).First(t=>!t.GetComponentInParent<TMP_Dropdown>());
        var label=row.Find("Label")?.GetComponent<TMP_Text>();
        if(!label) label=RoomLabel("Label",row,"底分",28);
        label.text="底分";RoomText(label,templateLabel.fontSize);label.margin=templateLabel.margin;label.font=templateLabel.font;
        CopyHongKongFieldRect(templateLabel.rectTransform,label.rectTransform);
        var dropdown=row.GetComponentInChildren<TMP_Dropdown>(true);
        if(!dropdown) {dropdown=Instantiate(SubRuleDropdown,row);dropdown.name="Dropdown";}
        dropdown.onValueChanged=new TMP_Dropdown.DropdownEvent();
        dropdown.ClearOptions();dropdown.AddOptions(FuzhouBaseScores.Select(x=>$"{x}分").ToList());dropdown.SetValueWithoutNotify(1);
        CopyHongKongFieldRect((RectTransform)stepTimer.transform,(RectTransform)dropdown.transform);
        RoomStyleDropdown(dropdown,stepTimer.captionText.fontSizeMax);
        dropdown.captionText.font=stepTimer.captionText.font;dropdown.captionText.fontSize=stepTimer.captionText.fontSize;
        dropdown.captionText.enableAutoSizing=stepTimer.captionText.enableAutoSizing;
        dropdown.captionText.fontSizeMin=stepTimer.captionText.fontSizeMin;dropdown.captionText.fontSizeMax=stepTimer.captionText.fontSizeMax;
        if(dropdown.itemText) {
            dropdown.itemText.font=stepTimer.itemText.font;dropdown.itemText.fontSize=stepTimer.itemText.fontSize;
            dropdown.itemText.enableAutoSizing=stepTimer.itemText.enableAutoSizing;
            dropdown.itemText.fontSizeMin=stepTimer.itemText.fontSizeMin;dropdown.itemText.fontSizeMax=stepTimer.itemText.fontSizeMax;
        }
        root.gameObject.SetActive(false);UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
