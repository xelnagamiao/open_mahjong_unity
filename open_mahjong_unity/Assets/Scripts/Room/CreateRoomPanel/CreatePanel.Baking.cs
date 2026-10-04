#if UNITY_EDITOR
using System;
using System.Linq;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Authoring and migration only. Player code binds the objects saved in MainScene.
public partial class CreatePanel {
    private static readonly Color RoomLine = new Color32(136, 141, 156, 255);

    public void ValidateFixedRuleOptions() {
        ValidateHongKongMainControls();
        if (!ClaimProtectionToggle || ClaimProtectionToggle.transform.parent != TacticalCallToggle.transform.parent
            || ClaimProtectionToggle.transform.GetSiblingIndex() != TacticalCallToggle.transform.GetSiblingIndex() + 1)
            throw new InvalidOperationException(name + " 鸣牌保护应保存在创房选项的战术鸣牌后。");
        foreach (var key in new[] { "taiwan", "riichi" }) {
            DetailedConfigRegistry.TryGet(key, out var definition);
            var content = transform.Find("DetailedConfigPanel_" + key + "/Dialog/Create_Panel/ScrollArea/Viewport/Content");
            foreach (var option in definition.Options) {
                var dropdown = content.Find("DetailedConfig_" + option.Key)?.GetComponentInChildren<TMP_Dropdown>(true);
                if (!dropdown)
                    throw new InvalidOperationException(name + " 缺少已烘焙配置：" + key + "/" + option.Key);
                if (!dropdown.options.Select(o => o.text).SequenceEqual(option.Choices))
                    throw new InvalidOperationException(name + " 配置文案未同步：" + key + "/" + option.Key);
            }
        }
    }

    public void BakeFixedRoomUi() {
        if (!roomPresentationBaked) throw new InvalidOperationException("Create room layout must be authored first.");
        BakeClaimProtectionControl();
        EnsureHintToggles(); EnsureDuplicateWallControls(); EnsureGuobiaoFlowerControls(); EnsureTianDiRenHeControls();
        EnsureRiichiOptionToggles(); EnsureRiichiStartingScorePanel(); EnsureCuoheTypePanel();
        EnsureChangshaOptionControls(); EnsureWallOptionControls(); BakeRiichiDetailsPanel();
        BakeHongKongDetailsPanel();
        BakeYixingRoomControls();
        EnsureRuleDropdownOptions(); InitCuoheTypeDropdown();
        BakeRoomFooterAndHelp();
        RoomGet<CanvasGroup>(CuoheTypePanel);
        if (randomSeedInput.placeholder is TMP_Text seedPlaceholder)
            seedPlaceholder.text = "64位字符串随机种子";
        SyncDetailedConfigLabels("taiwan"); SyncDetailedConfigLabels("riichi");
        ConfigureRoomSettingsPresentation();
        var taiwan = transform.Find("DetailedConfigPanel_taiwan");
        foreach (var overlay in new[] { taiwan, taiwan.Find("FanTablePanel"), transform.Find("DetailedConfigPanel_riichi") }) {
            ConfigureSettingsDialog(overlay.gameObject);
            overlay.gameObject.SetActive(false);
        }
        _ruleState = "guobiao";
        chooseRule.SetValueWithoutNotify(CreateRoomRuleTextConfigCatalog.Rules.ToList().FindIndex(r => r.Rule == _ruleState));
        InitSubRuleDropdown(); ApplyRuleDefaults(_ruleState); ApplyGuobiaoTierPreset(0);
        RefreshVisibility(); RefreshSubRuleDescription(); ReflowRoomPresentation();
    }

    public void BakeClaimProtectionControl() {
        ClaimProtectionToggle = EnsureClonedToggle(TacticalCallToggle, ClaimProtectionToggle, "ClaimProtection", "鸣牌保护", true);
        ClaimProtectionToggle.group = null;
        ClaimProtectionToggle.onValueChanged = new UnityEngine.UI.Toggle.ToggleEvent();
        ClaimProtectionToggle.transform.SetParent(TacticalCallToggle.transform.parent, false);
        ClaimProtectionToggle.transform.SetAsLastSibling();
        ClaimProtectionToggle.transform.SetSiblingIndex(TacticalCallToggle.transform.GetSiblingIndex() + 1);
        UnityEditor.EditorUtility.SetDirty(this);
    }

    private void SyncDetailedConfigLabels(string rule) {
        DetailedConfigRegistry.TryGet(rule, out var definition);
        var content = transform.Find("DetailedConfigPanel_" + rule + "/Dialog/Create_Panel/ScrollArea/Viewport/Content");
        foreach (var option in definition.Options) {
            var row = content.Find("DetailedConfig_" + option.Key);
            row.Find("Label").GetComponent<TMP_Text>().text = option.Label;
            var dropdown = row.GetComponentInChildren<TMP_Dropdown>(true);
            int selected = dropdown.value;
            dropdown.ClearOptions(); dropdown.AddOptions(option.Choices.ToList());
            dropdown.SetValueWithoutNotify(Mathf.Clamp(selected, 0, option.Choices.Count - 1));
            dropdown.RefreshShownValue();
        }
    }

    private void EnsureHintToggles() {
        EnsureStepTimerOptions();
        var label = tipsToggle.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.text = "番数提示";
        countTipsToggle = EnsureClonedToggle(tipsToggle, countTipsToggle, "CountTips", "枚数提示", false);
        pointerTipsToggle = EnsureClonedToggle(tipsToggle, pointerTipsToggle, "PointerTips", "指针提示", true);
        pointerTipsToggle.group = null;
        pointerTipsToggle.onValueChanged = new Toggle.ToggleEvent();
        countTipsToggle.group = null;
        countTipsToggle.onValueChanged = new Toggle.ToggleEvent();
        countTipsToggle.transform.SetSiblingIndex(tipsToggle.transform.GetSiblingIndex() + 1);
    }

    private void EnsureRiichiOptionToggles() {
        if (RedDoraToggle == null) return;
        KuikaeToggle = EnsureClonedToggle(RedDoraToggle, KuikaeToggle, "UseKuikae", "禁止食替", true);
        Toggle xiruTemplate = KuikaeToggle != null ? KuikaeToggle : RedDoraToggle;
        XiruToggle = EnsureClonedToggle(xiruTemplate, XiruToggle, "UseXiru", "西入", true);
        Toggle tobiTemplate = XiruToggle != null ? XiruToggle : xiruTemplate;
        TobiToggle = EnsureClonedToggle(tobiTemplate, TobiToggle, "UseTobi", "击飞", true);
    }

    private static Toggle EnsureClonedToggle(Toggle template, Toggle existing, string goName, string labelText, bool defaultOn) {
        if (existing != null) return existing;
        if (template == null) return null;
        var clone = Instantiate(template, template.transform.parent);
        clone.name = goName;
        clone.isOn = defaultOn;
        var label = clone.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = labelText;
        return clone;
    }

    private void InitCuoheTypeDropdown() {
        if (CuoheTypeDropdown == null) return;
        CuoheTypeDropdown.ClearOptions();
        CuoheTypeDropdown.AddOptions(new List<string> {
            "-30/+10",
            "-40/0",
        });
        CuoheTypeDropdown.value = 0;
    }

    private void EnsureRiichiStartingScorePanel() {
        if (RiichiStartingScorePanel != null || InputHepaiLimitPlane == null) return;
        RiichiStartingScorePanel = Instantiate(InputHepaiLimitPlane, InputHepaiLimitPlane.transform.parent);
        RiichiStartingScorePanel.name = "RiichiStartingScorePanel";
        RiichiStartingScoreInput = RiichiStartingScorePanel.GetComponentInChildren<TMP_InputField>(true);
        bool titleSet = false;
        foreach (TMP_Text label in RiichiStartingScorePanel.GetComponentsInChildren<TMP_Text>(true)) {
            if (label.GetComponentInParent<TMP_InputField>(true) != null) continue;
            label.text = titleSet ? "1000–1000000 点，100 的倍数\n四家使用相同起始点数" : "起始点数";
            titleSet = true;
        }
        if (RiichiStartingScoreInput != null) {
            RiichiStartingScoreInput.onValueChanged = new TMP_InputField.OnChangeEvent();
            RiichiStartingScoreInput.onEndEdit = new TMP_InputField.SubmitEvent();
            RiichiStartingScoreInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            RiichiStartingScoreInput.characterLimit = 7;
            RiichiStartingScoreInput.text = "25000";
            if (RiichiStartingScoreInput.placeholder is TMP_Text placeholder) placeholder.text = "25000";
        }
        RiichiStartingScorePanel.SetActive(false);
    }

    private void EnsureCuoheTypePanel() {
        if (CuoheTypePanel != null && CuoheTypeDropdown != null) return;
        if (HepaiWayPanel == null || HepaiWayDropdown == null) return;

        Transform parent = InputHepaiLimitPlane != null
            ? InputHepaiLimitPlane.transform.parent
            : HepaiWayPanel.transform.parent;
        CuoheTypePanel = Instantiate(HepaiWayPanel, parent);
        CuoheTypePanel.name = "CuoheTypePanel";
        CuoheTypePanel.SetActive(false);
        CuoheTypeDropdown = CuoheTypePanel.GetComponentInChildren<TMP_Dropdown>(true);
        foreach (TMP_Text label in CuoheTypePanel.GetComponentsInChildren<TMP_Text>(true)) {
            if (label.GetComponentInParent<TMP_Dropdown>(true) != null) continue;
            label.text = "错和形式";
            break;
        }
    }

    private void EnsureChangshaOptionControls() {
        Toggle toggleTemplate = TacticalCallToggle != null ? TacticalCallToggle : RedDoraToggle;
        ChangshaInitialSiXiToggle = EnsureClonedToggle(toggleTemplate, ChangshaInitialSiXiToggle, "ChangshaInitialSiXi", "四喜", true);
        Toggle lastToggle = ChangshaInitialSiXiToggle != null ? ChangshaInitialSiXiToggle : toggleTemplate;
        ChangshaInitialBanBanHuToggle = EnsureClonedToggle(lastToggle, ChangshaInitialBanBanHuToggle, "ChangshaInitialBanBanHu", "板板胡", true);
        lastToggle = ChangshaInitialBanBanHuToggle != null ? ChangshaInitialBanBanHuToggle : lastToggle;
        ChangshaInitialQueYiSeToggle = EnsureClonedToggle(lastToggle, ChangshaInitialQueYiSeToggle, "ChangshaInitialQueYiSe", "缺一色", true);
        lastToggle = ChangshaInitialQueYiSeToggle != null ? ChangshaInitialQueYiSeToggle : lastToggle;
        ChangshaInitialLiuLiuShunToggle = EnsureClonedToggle(lastToggle, ChangshaInitialLiuLiuShunToggle, "ChangshaInitialLiuLiuShun", "六六顺", true);
        lastToggle = ChangshaInitialLiuLiuShunToggle != null ? ChangshaInitialLiuLiuShunToggle : lastToggle;
        ChangshaInitialSanTongToggle = EnsureClonedToggle(lastToggle, ChangshaInitialSanTongToggle, "ChangshaInitialSanTong", "三同", true);
        lastToggle = ChangshaInitialSanTongToggle != null ? ChangshaInitialSanTongToggle : lastToggle;
        ChangshaDealerBirdToggle = EnsureClonedToggle(lastToggle, ChangshaDealerBirdToggle, "ChangshaDealerBird", "定庄扎鸟", true);

        ChangshaOpenKongPanel = EnsureClonedDropdownPanel(HepaiWayPanel, ChangshaOpenKongPanel, "ChangshaOpenKongPanel", "开杠张数");
        ChangshaOpenKongDropdown = ChangshaOpenKongPanel != null
            ? ChangshaOpenKongPanel.GetComponentInChildren<TMP_Dropdown>(true)
            : null;
        if (ChangshaOpenKongDropdown != null) {
            ChangshaOpenKongDropdown.ClearOptions();
            ChangshaOpenKongDropdown.AddOptions(new List<string> { "1张", "2张", "3张", "4张" });
            SetChangshaOpenKongCount(2);
        }

        GameObject birdTemplate = ChangshaOpenKongPanel != null ? ChangshaOpenKongPanel : HepaiWayPanel;
        ChangshaBirdCountPanel = EnsureClonedDropdownPanel(birdTemplate, ChangshaBirdCountPanel, "ChangshaBirdCountPanel", "扎鸟张数");
        ChangshaBirdCountDropdown = ChangshaBirdCountPanel != null
            ? ChangshaBirdCountPanel.GetComponentInChildren<TMP_Dropdown>(true)
            : null;
        if (ChangshaBirdCountDropdown != null) {
            ChangshaBirdCountDropdown.ClearOptions();
            ChangshaBirdCountDropdown.AddOptions(new List<string> { "不扎鸟", "1鸟", "2鸟", "4鸟" });
            SetChangshaBirdCount(2);
        }

        EnsureChangshaScoreControls();

        SetChangshaOptionsVisible(false);
    }

    private void EnsureWallOptionControls() {
        Toggle template = TacticalCallToggle != null ? TacticalCallToggle : RedDoraToggle;
        WallWanToggle = EnsureClonedToggle(template, WallWanToggle, "WallWan", "万", true);
        Toggle last = WallWanToggle != null ? WallWanToggle : template;
        WallTongToggle = EnsureClonedToggle(last, WallTongToggle, "WallTong", "筒", true);
        last = WallTongToggle != null ? WallTongToggle : last;
        WallSuoToggle = EnsureClonedToggle(last, WallSuoToggle, "WallSuo", "索", true);
        last = WallSuoToggle != null ? WallSuoToggle : last;
        WallWindsToggle = EnsureClonedToggle(last, WallWindsToggle, "WallWinds", "四风", true);
        last = WallWindsToggle != null ? WallWindsToggle : last;
        WallDragonsToggle = EnsureClonedToggle(last, WallDragonsToggle, "WallDragons", "三元", true);
        last = WallDragonsToggle != null ? WallDragonsToggle : last;
        WallFlowersToggle = EnsureClonedToggle(last, WallFlowersToggle, "WallFlowers", "花牌", true);
        SetWallOptionsVisible(false);
    }

    private void EnsureChangshaScoreControls() {
        // 长沙计分模式及自定义分值独占一行，避免与开杠、扎鸟配置挤在同一行。
        Transform parent = InputHepaiLimitPlane != null
            ? InputHepaiLimitPlane.transform.parent
            : transform;
        if (ChangshaScoreRow == null) {
            ChangshaScoreRow = new GameObject("ChangshaScoreRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            ChangshaScoreRow.transform.SetParent(parent, false);
            HorizontalLayoutGroup layout = ChangshaScoreRow.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            ChangshaScoreRow.GetComponent<LayoutElement>().minHeight = 44f;
        }

        Toggle toggleTemplate = ChangshaDealerBirdToggle != null ? ChangshaDealerBirdToggle : TacticalCallToggle;
        ChangshaBaseScoreNoDealerToggle = EnsureClonedToggle(
            toggleTemplate,
            ChangshaBaseScoreNoDealerToggle,
            "ChangshaBaseScoreNoDealer",
            "不区分庄闲",
            false);

        ChangshaSmallHuScorePanel = EnsureChangshaScoreInputPanel(
            ChangshaSmallHuScorePanel,
            "ChangshaSmallHuScorePanel",
            "小胡分数",
            out ChangshaSmallHuScoreInput);
        ChangshaBigHuScorePanel = EnsureChangshaScoreInputPanel(
            ChangshaBigHuScorePanel,
            "ChangshaBigHuScorePanel",
            "大胡分数",
            out ChangshaBigHuScoreInput);
        if (ChangshaSmallHuScoreInput != null) ChangshaSmallHuScoreInput.text = "2";
        if (ChangshaBigHuScoreInput != null) ChangshaBigHuScoreInput.text = "8";
        RefreshChangshaScoreInputVisibility();
    }

    private GameObject EnsureChangshaScoreInputPanel(
        GameObject existing,
        string objectName,
        string labelText,
        out TMP_InputField input) {
        if (existing == null && InputHepaiLimitPlane != null) {
            existing = Instantiate(InputHepaiLimitPlane, ChangshaScoreRow.transform);
            existing.name = objectName;
            foreach (TMP_Text label in existing.GetComponentsInChildren<TMP_Text>(true)) {
                if (label.GetComponentInParent<TMP_InputField>(true) != null) continue;
                label.text = labelText;
                break;
            }
        }
        input = existing != null ? existing.GetComponentInChildren<TMP_InputField>(true) : null;
        if (input != null) input.contentType = TMP_InputField.ContentType.IntegerNumber;
        return existing;
    }

    private GameObject EnsureClonedDropdownPanel(GameObject template, GameObject existing, string goName, string labelText) {
        if (existing != null) return existing;
        if (template == null) return null;
        GameObject clone = Instantiate(template, template.transform.parent);
        clone.name = goName;
        SetPanelLabel(clone, labelText);
        clone.SetActive(false);
        return clone;
    }

    private static void SetPanelLabel(GameObject panel, string labelText) {
        if (panel == null) return;
        foreach (TMP_Text label in panel.GetComponentsInChildren<TMP_Text>(true)) {
            if (label.GetComponentInParent<TMP_Dropdown>(true) != null) continue;
            label.text = labelText;
            return;
        }
    }

    private void EnsureDuplicateWallControls() {
        if (SetRandomSeedToggle == null || SetRandomSeedPanel == null) return;
        var seedLabel = SetRandomSeedToggle.GetComponentInChildren<TMP_Text>(true);
        if (seedLabel != null) seedLabel.text = "场景复现";
        foreach (TMP_Text label in SetRandomSeedPanel.GetComponentsInChildren<TMP_Text>(true)) {
            if (label.GetComponentInParent<TMP_InputField>(true) == null)
                label.text = label.text.Replace("复式", "场景复现");
        }
        DuplicateWallToggle = EnsureClonedToggle(SetRandomSeedToggle, DuplicateWallToggle, "DuplicateWall", "复式", false);
        DuplicateWallToggle.group = null;
        DuplicateWallToggle.onValueChanged = new Toggle.ToggleEvent();
        if (DuplicateWallPanel == null) DuplicateWallPanel = Instantiate(SetRandomSeedPanel, SetRandomSeedPanel.transform.parent);
        DuplicateWallPanel.name = "DuplicateWallPanel";
        DuplicateKeyInput = DuplicateWallPanel.GetComponentInChildren<TMP_InputField>(true);
        bool titleSet = false;
        foreach (TMP_Text label in DuplicateWallPanel.GetComponentsInChildren<TMP_Text>(true)) {
            if (label.GetComponentInParent<TMP_InputField>(true) != null) continue;
            label.text = titleSet ? "局数和花牌跟随密钥设置。房间不显示密钥，解锁后可在牌谱中查看。" : "复式密钥";
            titleSet = true;
        }
        if (DuplicateKeyInput != null) {
            DuplicateKeyInput.onValueChanged = new TMP_InputField.OnChangeEvent();
            DuplicateKeyInput.onEndEdit = new TMP_InputField.SubmitEvent();
            DuplicateKeyInput.contentType = TMP_InputField.ContentType.Standard;
            DuplicateKeyInput.characterLimit = 80;
            DuplicateKeyInput.text = "";
            if (DuplicateKeyInput.placeholder is TMP_Text placeholder) placeholder.text = "牌山密钥";
        }
        DuplicateWallPanel.SetActive(false);
    }

    private void EnsureGuobiaoFlowerControls() {
        if (GuobiaoFlowersToggle != null) return;
        GuobiaoFlowersToggle = EnsureClonedToggle(TacticalCallToggle, null, "GuobiaoUseFlowers", "花牌", true);
        if (GuobiaoFlowersToggle == null) return;
        GuobiaoFlowersToggle.group = null;
        GuobiaoFlowersToggle.onValueChanged = new Toggle.ToggleEvent();
        GuobiaoFlowersLabel = GuobiaoFlowersToggle.GetComponentInChildren<TMP_Text>(true);
    }

    private void EnsureTianDiRenHeControls() {
        if (TianDiRenHeToggle != null) return;
        TianDiRenHeToggle = EnsureClonedToggle(TacticalCallToggle, null, "TianDiRenHe", "天地人和", false);
        if (TianDiRenHeToggle == null) return;
        TianDiRenHeToggle.group = null;
        TianDiRenHeToggle.onValueChanged = new UnityEngine.UI.Toggle.ToggleEvent();
        TianDiRenHeToggle.transform.SetSiblingIndex(GuobiaoFlowersToggle.transform.GetSiblingIndex() + 1);
    }

    private void RoomStyleDetailedDialog(Transform overlay) {
        var dialog = (RectTransform)overlay.Find("Dialog");
        RoomRemoveLayouts(dialog);
        var header = (RectTransform)dialog.Find("HeaderPanel");
        RoomRemoveLayouts(header); RoomImage(header, RoomNavy);
        RoomFill(header); header.anchorMin = new Vector2(0, 1); header.offsetMin = new Vector2(0, -88);
        var title = header.GetComponentInChildren<TMP_Text>(true);
        RoomText(title, 40, Color.white); RoomFill(title.rectTransform, 28, 0, 28, 0);
        var body = (RectTransform)dialog.Find("Create_Panel"); RoomRemoveLayouts(body); RoomFill(body, 0, 88);
        var footer = (RectTransform)body.Find("Footer"); RoomRemoveLayouts(footer);
        RoomFill(footer, 32, 0, 32, 16); footer.anchorMax = new Vector2(1, 0); footer.offsetMax = new Vector2(-32, 80);
        var buttons = footer.gameObject.AddComponent<HorizontalLayoutGroup>(); buttons.spacing = 24;
        buttons.childControlWidth = buttons.childControlHeight = true; buttons.childForceExpandWidth = buttons.childForceExpandHeight = true;
        foreach (var button in footer.GetComponentsInChildren<Button>(true)) {
            RoomStyleButton(button, 30); var element = RoomHeight((RectTransform)button.transform, 64); element.flexibleWidth = 1;
        }
        var scroll = body.Find("ScrollArea").GetComponent<ScrollRect>();
        RoomFill((RectTransform)scroll.transform, 32, 16, 56, 100); RoomFill(scroll.viewport, 0, 0, 12, 0);
        scroll.scrollSensitivity = 40;
        if (!scroll.verticalScrollbar) {
            var track = RoomRect("Scrollbar", scroll.transform); RoomImage(track, RoomMuted).raycastTarget = true;
            var slide = RoomRect("Sliding Area", track); RoomFill(slide);
            var thumb = RoomRect("Handle", slide); RoomFill(thumb);
            var graphic = RoomImage(thumb, RoomMuted); graphic.raycastTarget = true;
            var scrollbar = RoomGet<Scrollbar>(track.gameObject);
            scrollbar.handleRect = thumb; scrollbar.targetGraphic = graphic; scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = scrollbar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }
        if (scroll.verticalScrollbar) {
            var bar = (RectTransform)scroll.verticalScrollbar.transform;
            RoomFill(bar); bar.anchorMin = new Vector2(1, 0); bar.offsetMin = new Vector2(19, -8); bar.offsetMax = new Vector2(33, 8);
        }
        var content = scroll.content; RoomRemoveLayouts(content);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1);
        content.anchoredPosition = Vector2.zero; content.sizeDelta = Vector2.zero;
        content.gameObject.AddComponent<CreateRoomDetailsLayout>();
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        foreach (RectTransform row in content) {
            RoomRemoveLayouts(row);
            var dropdown = row.GetComponentInChildren<TMP_Dropdown>(true);
            if (dropdown) {
                var label = row.Find("Label").GetComponent<TMP_Text>(); RoomText(label, 28); label.textWrappingMode = TextWrappingModes.Normal;
                bool stacked = row.name.StartsWith("DetailedConfig_") || row.name.StartsWith("FanTai_");
                var field = (RectTransform)dropdown.transform;
                if (stacked) {
                    RoomFill(label.rectTransform, 14, 0, 14, 0); label.rectTransform.anchorMin = new Vector2(0, 1); label.rectTransform.offsetMin = new Vector2(14, -44);
                    RoomFill(field, 0, 48, 0, 8);
                } else {
                    RoomBox(label.rectTransform, 0, 0, 150, 72); RoomFill(field, 158, 8, 0, 8);
                }
                RoomStyleDropdown(dropdown, 26);
            } else if (row.TryGetComponent<TMP_Text>(out var label)) {
                RoomText(label, row.name.StartsWith("Section_") ? 28 : 24, RoomMuted);
                label.textWrappingMode = TextWrappingModes.Normal;
                if (row.name.StartsWith("Section_")) { label.fontStyle = FontStyles.Normal; label.color = RoomNavy; }
            }
        }
    }

    private void RoomStyleButton(Button button, float size) {
        var image = RoomImage((RectTransform)button.transform, Color.white); image.raycastTarget = true; button.targetGraphic = image;
        var text = button.GetComponentInChildren<TMP_Text>(true); RoomText(text, size); RoomFill(text.rectTransform, 10, 0, 10, 0); text.alignment = TextAlignmentOptions.Center;
    }

    private static void RoomRemoveLayouts(RectTransform root) {
        foreach (var c in root.GetComponents<LayoutGroup>()) UnityEngine.Object.DestroyImmediate(c);
        foreach (var c in root.GetComponents<ContentSizeFitter>()) UnityEngine.Object.DestroyImmediate(c);
        var element = root.GetComponent<LayoutElement>(); if (element) { element.minWidth = element.preferredWidth = -1; element.flexibleWidth = 0; }
    }

    private static T RoomGet<T>(GameObject target) where T : Component { var existing = target.GetComponent<T>(); return existing ? existing : target.AddComponent<T>(); }

    private static RectTransform RoomRect(string name, Transform parent) { var existing = parent.Find(name) as RectTransform; if (existing) return existing; var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false); return rect; }

    private static UnityEngine.UI.Image RoomImage(RectTransform rect, Color color, Sprite sprite = null) { var image = RoomGet<UnityEngine.UI.Image>(rect.gameObject); image.sprite = sprite; image.type = sprite ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple; image.color = color; image.raycastTarget = false; return image; }

    private void RoomText(TMP_Text text, float size, Color? color = null) { if (!text) return; text.font = SubRuleDescriptionText.font; text.fontSize = size; text.enableAutoSizing = false; text.color = color ?? Color.black; text.fontStyle = FontStyles.Normal; text.alignment = TextAlignmentOptions.MidlineLeft; text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Ellipsis; text.raycastTarget = false; }

    private TMP_Text RoomLabel(string name, Transform parent, string text, float size) { var rect = RoomRect(name, parent); var label = RoomGet<TextMeshProUGUI>(rect.gameObject); RoomText(label, size); label.text = text; return label; }

    private void EnsureStepTimerOptions() {
        if (!stepTimer || stepTimer.options.Count != 5) return;
        int previous = stepTimer.value;
        stepTimer.options.Insert(2, new TMP_Dropdown.OptionData("8 秒"));
        stepTimer.SetValueWithoutNotify(previous >= 2 ? previous + 1 : previous);
        stepTimer.RefreshShownValue();
    }

    public void BakeRiichiDetailsPanel() {
        if (transform.Find("DetailedConfigPanel_riichi")) return;
        var source = transform.Find("DetailedConfigPanel_taiwan");
        if (!source) throw new InvalidOperationException("Taiwan rule panel template missing");
        var copy = Instantiate(source.gameObject, transform);
        copy.name = "DetailedConfigPanel_riichi";
        try {
            copy.SetActive(false);
            var fanPanel = copy.transform.Find("FanTablePanel");
            if (fanPanel) DestroyImmediate(fanPanel.gameObject);
            var header = copy.transform.Find("Dialog/HeaderPanel");
            header.GetComponentInChildren<TMP_Text>(true).text = "立直麻将 · 高级设置";
            var scroll = copy.transform.Find("Dialog/Create_Panel/ScrollArea").GetComponent<ScrollRect>();
            var content = scroll.content;
            foreach (Transform child in content.Cast<Transform>().ToArray()) DestroyImmediate(child.gameObject);
            DetailedConfigRegistry.TryGet("riichi", out var definition);
            void AddRow(string name, string title, IEnumerable<string> labels) {
                var row = RoomRect(name, content);
                RoomLabel("Label", row, title, 28);
                var field = Instantiate(SubRuleDropdown, row);
                field.name = "Dropdown"; field.onValueChanged = new TMP_Dropdown.DropdownEvent();
                field.ClearOptions(); field.AddOptions(labels.ToList()); field.SetValueWithoutNotify(0);
            }
            AddRow("DetailedConfigPreset", "规则预设", RiichiRoomRules.Presets.Select(p => p.Label).Concat(new[] { "自定义规则" }));
            string section = null;
            foreach (var option in definition.Options) {
                if (section != option.Section) { section = option.Section; RoomLabel("Section_" + section, content, section, 28); }
                AddRow("DetailedConfig_" + option.Key, option.Label, option.Choices);
            }
            RoomStyleDetailedDialog(copy.transform);
            var nav = header.Find("SectionJump").GetComponent<TMP_Dropdown>();
            nav.onValueChanged = new TMP_Dropdown.DropdownEvent();
            var anchors = new[] { content }.Concat(content.Cast<RectTransform>().Where(r => r.name.StartsWith("Section_"))).ToArray();
            nav.ClearOptions(); nav.AddOptions(anchors.Select((r,i) => i==0 ? "全部配置" : r.GetComponent<TMP_Text>().text).ToList());
            RoomGet<CreateRoomSectionNavigator>(nav.gameObject).Configure(nav, scroll, anchors);
            RoomFill(header.GetComponentInChildren<TMP_Text>(true).rectTransform, 28, 0, 390, 0);
            var footer = copy.transform.Find("Dialog/Create_Panel/Footer");
            foreach (var button in footer.GetComponentsInChildren<Button>(true)) {
                button.onClick = new Button.ButtonClickedEvent();
                button.GetComponentInChildren<TMP_Text>(true).text = button.name == "Confirm" ? "应用规则" : button.name == "Cancel" ? "取消" : "恢复默认设置";
            }
            RoomGet<CreateRoomScrollbarSize>(scroll.gameObject);
            ConfigureSettingsDialog(copy);
        } catch { DestroyImmediate(copy); throw; }
    }

    private void ConfigureRoomSettingsPresentation() {
        RoomPrimaryButton(createButton);
        var stack = RoomContent.GetComponent<VerticalLayoutGroup>();
        if (stack) stack.padding.bottom = 64;
        foreach (var dropdown in GetComponentsInChildren<TMP_Dropdown>(true))
            RoomStyleDropdown(dropdown, dropdown.captionText.fontSizeMax);
    }

    private void ConfigureSettingsDialog(GameObject overlay) {
        if (!overlay) return;
        RoomGet<CreateRoomPopupMotion>(overlay);
        var dialog = overlay.transform.Find("Dialog");
        var headerTitle = dialog.Find("HeaderPanel").GetComponentInChildren<TMP_Text>(true);
        var titleRect = headerTitle.rectTransform;
        titleRect.offsetMin = new Vector2(48, titleRect.offsetMin.y);
        headerTitle.margin = Vector4.zero;
        var scroll = dialog.Find("Create_Panel/ScrollArea").GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        var viewportGraphic = scroll.viewport.GetComponent<Graphic>();
        if (viewportGraphic) viewportGraphic.raycastTarget = true;
        scroll.scrollSensitivity = 110;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        var layout = scroll.content.GetComponent<CreateRoomDetailsLayout>();
        if (layout) {
            layout.Columns = overlay.name == "DetailedConfigPanel_riichi" ? 4 : 2;
            layout.padding.bottom = 72;
        }
        foreach (RectTransform row in scroll.content) {
            var dropdown = row.GetComponentInChildren<TMP_Dropdown>(true);
            if (!dropdown) {
                if (row.name.StartsWith("Section_") && row.TryGetComponent<TMP_Text>(out var section))
                    section.margin = new Vector4(16, 0, 0, 0);
                continue;
            }
            var label = row.Find("Label")?.GetComponent<TMP_Text>();
            if (!label) continue;
            bool stacked = row.name.StartsWith("DetailedConfig_") || row.name.StartsWith("FanTai_");
            if (stacked) {
                RoomText(label, 26);
                label.margin = Vector4.zero;
                RoomFill(label.rectTransform, 16, 0, 16, 0);
                label.rectTransform.anchorMin = new Vector2(0, 1);
                label.rectTransform.offsetMin = new Vector2(16, -40);
                RoomFill((RectTransform)dropdown.transform, 0, 42, 0, 8);
            } else if (row.name == "DetailedConfigPreset") {
                label.margin = Vector4.zero;
                RoomBox(label.rectTransform, 16, 0, 142, 72);
                var field = (RectTransform)dropdown.transform;
                RoomFill(field, 158, 8, 12, 8);
                field.anchorMax = new Vector2(.5f, 1);
            }
            RoomStyleDropdown(dropdown, 24);
        }
        var footer = dialog.Find("Create_Panel/Footer");
        var reset = footer.Find("Reset")?.GetComponent<Button>();
        if (reset) reset.GetComponentInChildren<TMP_Text>(true).text = "恢复默认设置";
        RoomPrimaryButton(footer.Find("Confirm")?.GetComponent<Button>());
        if (overlay.name == "DetailedConfigPanel_riichi")
            dialog.Find("HeaderPanel").GetComponentInChildren<TMP_Text>(true).text = "立直麻将 · 高级设置";
    }

    private void RoomStyleDropdown(TMP_Dropdown dropdown, float fontSize) {
        var image = RoomImage((RectTransform)dropdown.transform, Color.white);
        image.raycastTarget = true;
        dropdown.targetGraphic = image;
        var border = RoomGet<Outline>(dropdown.gameObject);
        border.effectColor = RoomLine;
        border.effectDistance = new Vector2(1, -1);
        var colors = dropdown.colors;
        colors.normalColor = colors.selectedColor = Color.white;
        colors.highlightedColor = new Color(.96f, .96f, .96f);
        colors.disabledColor = new Color(.90f, .90f, .92f, 1);
        dropdown.colors = colors;
        var textColor = new Color32(50, 50, 50, 255);
        RoomText(dropdown.captionText, fontSize, textColor);
        RoomFill(dropdown.captionText.rectTransform, 16, 1, 42, 1);
        dropdown.captionText.margin = Vector4.zero;
        dropdown.captionText.enableAutoSizing = true;
        dropdown.captionText.fontSizeMin = Mathf.Min(24, fontSize);
        dropdown.captionText.fontSizeMax = fontSize;
        foreach (var label in dropdown.GetComponentsInChildren<TMP_Text>(true)) {
            var background = label.GetComponent<UnityEngine.UI.Image>();
            if (background) background.enabled = false;
        }
        var arrow = RoomRect("Arrow", dropdown.transform);
        var oldArrow = arrow.GetComponent<Graphic>();
        if (oldArrow) oldArrow.enabled = false;
        arrow.anchorMin = arrow.anchorMax = new Vector2(1, .5f);
        arrow.pivot = new Vector2(.5f, .5f);
        arrow.anchoredPosition = new Vector2(-22, 0);
        arrow.sizeDelta = new Vector2(13, 8);
        var indicator = RoomRect("Indicator", arrow);
        RoomFill(indicator);
        var graphic = RoomGet<CreateRoomDropdownArrow>(indicator.gameObject);
        graphic.color = RoomMuted;
        graphic.raycastTarget = false;
        if (!dropdown.template) return;
        RoomImage(dropdown.template, Color.white);
        dropdown.template.anchorMin = Vector2.zero;
        dropdown.template.anchorMax = new Vector2(1, 0);
        dropdown.template.pivot = new Vector2(.5f, 1);
        dropdown.template.anchoredPosition = new Vector2(0, -5);
        RoomDropdownTemplateHeight(dropdown);
        var shadow = RoomGet<Shadow>(dropdown.template.gameObject);
        shadow.effectColor = new Color(.12f, .2f, .4f, .16f);
        shadow.effectDistance = new Vector2(0, -4);
        var scroll = dropdown.template.GetComponent<ScrollRect>();
        if (scroll) {
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 60;
            if (scroll.viewport) RoomFill(scroll.viewport, 4, 4, 4, 4);
            if (scroll.content) {
                scroll.content.anchorMin = new Vector2(0, 1);
                scroll.content.anchorMax = Vector2.one;
                scroll.content.pivot = new Vector2(.5f, 1);
                scroll.content.anchoredPosition = Vector2.zero;
                scroll.content.sizeDelta = new Vector2(0, 52);
            }
        }
        var item = dropdown.template.GetComponentInChildren<Toggle>(true);
        if (item) {
            var rect = (RectTransform)item.transform;
            rect.anchorMin = new Vector2(0, .5f);
            rect.anchorMax = new Vector2(1, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0, 52);
            if (item.targetGraphic is UnityEngine.UI.Image background) {
                background.sprite = null;
                background.color = new Color32(245, 245, 245, 255);
                RoomFill(background.rectTransform);
            }
            if (item.graphic is UnityEngine.UI.Image mark) {
                mark.color = Color.white;
                var rectMark = mark.rectTransform;
                rectMark.anchorMin = rectMark.anchorMax = new Vector2(0, .5f);
                rectMark.pivot = new Vector2(.5f, .5f);
                rectMark.anchoredPosition = new Vector2(30.5f, 0);
                rectMark.sizeDelta = new Vector2(33, 32);
            }
        }
        if (dropdown.itemText) {
            RoomText(dropdown.itemText, Mathf.Min(25, fontSize), textColor);
            RoomFill(dropdown.itemText.rectTransform, 54, 0, 10, 0);
        }
    }
}
#endif
