using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class CreatePanel {
    private TMP_Dropdown _fuzhouBaseScore;
    private RectTransform fuzhouView;
    private int fuzhouBaseScoreIndex = 1;
    private static readonly int[] FuzhouBaseScores = {10,15,20};

    private void BindFuzhouMainControls(GameObject view) {
        fuzhouView = (RectTransform)view.transform;
        _fuzhouBaseScore = view.GetComponentInChildren<TMP_Dropdown>(true);
        _fuzhouBaseScore.onValueChanged.RemoveAllListeners();
        _fuzhouBaseScore.ClearOptions(); _fuzhouBaseScore.AddOptions(FuzhouBaseScores.Select(x=>$"{x}分").ToList());
        _fuzhouBaseScore.SetValueWithoutNotify(fuzhouBaseScoreIndex);
        _fuzhouBaseScore.onValueChanged.AddListener(index => fuzhouBaseScoreIndex = index);
        RoomDropdownTemplateHeight(_fuzhouBaseScore);
        RefreshFuzhouControls();
    }
    private void RefreshFuzhouControls() {
        if (!Application.isPlaying && !fuzhouView) fuzhouView = RoomContent.Find("FuzhouSettings") as RectTransform;
        bool selected = _ruleState == FuzhouGameState.RuleId;
        if (fuzhouView) fuzhouView.gameObject.SetActive(selected);
        else if (selected && Application.isPlaying && isActiveAndEnabled) RequestDeferredPanel("FuzhouSettings", null);
    }
    private Dictionary<string,object> FuzhouConfigValues() => new Dictionary<string,object> {
        {"base_score", FuzhouBaseScores[Mathf.Clamp(fuzhouBaseScoreIndex,0,2)]},
    };
    private void ResetFuzhouControls() { fuzhouBaseScoreIndex = 1; if(_fuzhouBaseScore) _fuzhouBaseScore.SetValueWithoutNotify(1); }
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
