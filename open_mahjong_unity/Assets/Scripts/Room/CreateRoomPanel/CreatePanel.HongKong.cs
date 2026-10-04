using System;
using System.Linq;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class CreatePanel {
    private string _hongKongControlsSubRule;

    private void CreateHongKongRoom() {
        var config=new HongKong_Create_RoomConfig {
            RoomName=roomNameInput.text.Trim(), GameRound=GetSelectedGameTime(), Rule="hongkong",SubRule=GetSelectedSubRule(),
            Password=passwordToggle.isOn?passwordInput.text.Trim():"",RandomSeed=SetRandomSeedToggle.isOn?randomSeedInput.text.Trim():"",
            RoundTimer=GetSelectedRoundTimer(),StepTimer=GetSelectedStepTimer(),Tips=tipsToggle.isOn,
            CountTips=countTipsToggle.isOn,PointerTips=pointerTipsToggle.isOn,TouristLimit=TouristLimitToggle.isOn,
            AllowSpectator=AllowSpectatorToggle.isOn,EventId=_venueEventId,DetailedConfig=BuildDetailedConfigValues("hongkong"),
        };
        if (!config.Validate(out string error,passwordToggle.isOn,SetRandomSeedToggle.isOn)) {
            NotificationManager.Instance.ShowTip("create_room",false,error); return;
        }
        RoomNetworkManager.Instance.Create_HongKong_Room(config);
    }

    private void BindHongKongMainControls() {
        var root=RoomContent.Find("HongKongSettings") as RectTransform;
        if (!root) { Debug.LogError("香港麻将主页面设置尚未烘焙。",this); return; }
        string subRule=_ruleState=="hongkong" ? GetSelectedSubRule() : HongKongGameState.Qingzhang;
        var state=new DetailedConfigState(HongKong_Create_RoomConfig.Definition(subRule)) {
            Panel=root.gameObject, Content=root, Scroll=RoomContent.GetComponentInParent<ScrollRect>(),
        };
        _detailedConfigStates["hongkong"]=state;
        foreach (var option in state.Definition.Options) {
            var dropdown=root.Find("DetailedConfig_"+option.Key).GetComponentInChildren<TMP_Dropdown>(true);
            state.Dropdowns[option.Key]=dropdown;
            dropdown.onValueChanged.RemoveAllListeners();
            dropdown.ClearOptions();
            dropdown.AddOptions(option.Choices.ToList());
            RoomDropdownTemplateHeight(dropdown);
            dropdown.SetValueWithoutNotify(option.DefaultIndex);
            string key=option.Key;
            dropdown.onValueChanged.AddListener(index=>OnDetailedConfigOptionChanged(state,key,index));
        }
        _hongKongControlsSubRule=subRule;
        RefreshHongKongMainControls();
    }

    private void RefreshHongKongMainControls() {
        var root=RoomContent ? RoomContent.Find("HongKongSettings") : null;
        if (!root) return;
        root.gameObject.SetActive(_ruleState=="hongkong");
        if (_ruleState!="hongkong") return;
        string subRule=GetSelectedSubRule();
        if (_hongKongControlsSubRule!=subRule || GetDetailedConfigState("hongkong")==null) {
            BindHongKongMainControls(); return;
        }
        var values=BuildDetailedConfigValues("hongkong");
        foreach (var option in GetDetailedConfigState("hongkong").Definition.Options) {
            var row=root.Find("DetailedConfig_"+option.Key);
            if (row) row.gameObject.SetActive(HongKong_Create_RoomConfig.OptionVisible(option.Key,subRule,values));
        }
        LayoutRebuilder.MarkLayoutForRebuild((RectTransform)root);
    }

    private void ResetHongKongMainControls() {
        RefreshHongKongMainControls();
        var state=GetDetailedConfigState("hongkong");
        if (state==null) return;
        foreach (var option in state.Definition.Options)
            SetDetailedConfigDropdownValue(state,option,option.DefaultIndex);
        RefreshHongKongMainControls();
    }

#if UNITY_EDITOR
    public void ValidateHongKongMainControls() {
        var root=RoomContent.Find("HongKongSettings");
        foreach (var option in HongKong_Create_RoomConfig.Definition().Options) {
            var dropdown=root ? root.Find("DetailedConfig_"+option.Key)?.GetComponentInChildren<TMP_Dropdown>(true) : null;
            if (!dropdown || !dropdown.options.Select(o=>o.text).SequenceEqual(option.Choices))
                throw new InvalidOperationException(name+" 香港麻将主页面配置未同步："+option.Key);
        }
    }

    /// <summary>复用主建房页的滚动布局；运行时不生成固定控件。</summary>
    public void BakeHongKongDetailsPanel() {
        var root=RoomContent.Find("HongKongSettings") as RectTransform;
        if (!root) root=RoomRect("HongKongSettings",RoomContent);
        var toggles=RoomContent.Find("ToggleContainer");
        // Remove the old leading row from the index calculation first.
        root.SetAsLastSibling();
        root.SetSiblingIndex(toggles.GetSiblingIndex()+1);
        // Use the same grid and field metrics as the main room timer fields.
        var detailsLayout=root.GetComponent<CreateRoomDetailsLayout>();
        if (detailsLayout) DestroyImmediate(detailsLayout);
        var templateGrid=RoomContent.Find("ChioceDropdown").GetComponent<CreateRoomAdaptiveGrid>();
        var grid=RoomGet<CreateRoomAdaptiveGrid>(root.gameObject);
        grid.padding=new RectOffset(templateGrid.padding.left,templateGrid.padding.right,templateGrid.padding.top,templateGrid.padding.bottom);
        grid.spacing=templateGrid.spacing; grid.cellSize=templateGrid.cellSize;
        grid.minimumCellWidth=templateGrid.minimumCellWidth;
        grid.childAlignment=templateGrid.childAlignment;
        RoomGet<ContentSizeFitter>(root.gameObject).verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        var obsolete=root.Find("DetailedConfig_new13_version");
        if (obsolete) DestroyImmediate(obsolete.gameObject);
        var templateLabel=stepTimer.transform.parent.GetComponentsInChildren<TMP_Text>(true).First(t=>!t.GetComponentInParent<TMP_Dropdown>());
        foreach (var option in HongKong_Create_RoomConfig.Definition().Options) {
            var row=root.Find("DetailedConfig_"+option.Key) as RectTransform;
            if (!row) row=RoomRect("DetailedConfig_"+option.Key,root);
            var label=row.Find("Label")?.GetComponent<TMP_Text>();
            if (!label) label=RoomLabel("Label",row,option.Label,28);
            label.text=option.Label; RoomText(label,templateLabel.fontSize); label.margin=templateLabel.margin;
            CopyHongKongFieldRect(templateLabel.rectTransform,label.rectTransform);
            // The main timer labels are only two characters. Reserve a
            // proportional label column for longer house-rule names while
            // retaining the same horizontal field style and row spacing.
            label.rectTransform.anchorMin=new Vector2(0,label.rectTransform.anchorMin.y);
            label.rectTransform.anchorMax=new Vector2(.42f,label.rectTransform.anchorMax.y);
            label.rectTransform.offsetMin=new Vector2(0,label.rectTransform.offsetMin.y);
            label.rectTransform.offsetMax=new Vector2(-8,label.rectTransform.offsetMax.y);
            label.enableAutoSizing=true; label.fontSizeMin=18; label.fontSizeMax=templateLabel.fontSize;
            var dropdown=row.GetComponentInChildren<TMP_Dropdown>(true);
            if (!dropdown) { dropdown=Instantiate(SubRuleDropdown,row); dropdown.name="Dropdown"; }
            dropdown.onValueChanged=new TMP_Dropdown.DropdownEvent();
            dropdown.ClearOptions(); dropdown.AddOptions(option.Choices.ToList()); dropdown.SetValueWithoutNotify(option.DefaultIndex);
            CopyHongKongFieldRect((RectTransform)stepTimer.transform,(RectTransform)dropdown.transform);
            var field=(RectTransform)dropdown.transform;
            field.anchorMin=new Vector2(.42f,field.anchorMin.y);
            field.anchorMax=new Vector2(1,field.anchorMax.y);
            field.offsetMin=new Vector2(0,field.offsetMin.y);
            field.offsetMax=new Vector2(0,field.offsetMax.y);
            RoomStyleDropdown(dropdown,stepTimer.captionText.fontSizeMax);
            dropdown.captionText.font=stepTimer.captionText.font;
            dropdown.captionText.fontSize=stepTimer.captionText.fontSizeMax;
            dropdown.captionText.enableAutoSizing=true;
            dropdown.captionText.fontSizeMin=18; dropdown.captionText.fontSizeMax=stepTimer.captionText.fontSizeMax;
            dropdown.captionText.textWrappingMode=TextWrappingModes.Normal;
            if (dropdown.itemText) {
                dropdown.itemText.font=stepTimer.itemText.font;
                dropdown.itemText.fontSize=stepTimer.itemText.fontSize;
                dropdown.itemText.textWrappingMode=TextWrappingModes.Normal;
                dropdown.itemText.enableAutoSizing=true;
                dropdown.itemText.fontSizeMin=18; dropdown.itemText.fontSizeMax=stepTimer.itemText.fontSize;
            }
        }
        root.gameObject.SetActive(false);
        var old=transform.Find("DetailedConfigPanel_hongkong");
        if (old) DestroyImmediate(old.gameObject);
        UnityEditor.EditorUtility.SetDirty(this);
    }

    private static void CopyHongKongFieldRect(RectTransform source,RectTransform target) {
        target.anchorMin=source.anchorMin; target.anchorMax=source.anchorMax; target.pivot=source.pivot;
        target.offsetMin=source.offsetMin; target.offsetMax=source.offsetMax;
    }
#endif
}
