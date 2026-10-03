using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class CreatePanel {
    [SerializeField, HideInInspector] private bool roomPresentationBaked;
    private bool roomPresentationBound;
    private string defaultHepaiLimitCaption;

    private void RefreshHepaiLimitCaption() {
        var label = InputHepaiLimitToggle.GetComponentInChildren<TMP_Text>(true);
        if (!label) return;
        if (defaultHepaiLimitCaption == null) defaultHepaiLimitCaption = label.text;
        label.text = IsShanghaiQiaoma ? "一番和" : defaultHepaiLimitCaption;
    }
    private const float RoomRuleCaptionFontSize = 28;
    private const float RoomRuleItemFontSize = 25;
    private const float RoomRuleDropdownWidth = 232;
    private static readonly Color RoomNavy = new Color32(44, 58, 116, 255);
    private static readonly Color RoomMuted = new Color32(83, 86, 99, 255);
    private RectTransform RoomContent => transform.Find("Create_Panel/Scroll View/Viewport/Content") as RectTransform;
    private Toggle[] RoomOptionToggles => new[] {
        tipsToggle, countTipsToggle, CuoHeheToggle, passwordToggle, TacticalCallToggle, ClaimProtectionToggle,
        TouristLimitToggle, InputHepaiLimitToggle, AllowSpectatorToggle, RedDoraToggle,
        KuikaeToggle, XiruToggle, TobiToggle, SetRandomSeedToggle, DuplicateWallToggle,
        GuobiaoFlowersToggle, TianDiRenHeToggle, pointerTipsToggle, BloodBattleToggle, ChangshaInitialSiXiToggle,
        ChangshaInitialBanBanHuToggle, ChangshaInitialQueYiSeToggle, ChangshaInitialLiuLiuShunToggle,
        ChangshaInitialSanTongToggle, ChangshaDealerBirdToggle, ChangshaBaseScoreNoDealerToggle,
        WallWanToggle, WallTongToggle, WallSuoToggle, WallWindsToggle, WallDragonsToggle, WallFlowersToggle, YixingSevenPairsToggle, GuangdongMinimumScoreToggle
    };
    private Toggle[] RoomRoundToggles => new[] { gameTime1Button, gameTime2Button, gameTime3Button, gameTime4Button };


    private void BindFixedCreateControls() {
        DuplicateWallToggle.onValueChanged.AddListener(isOn => {
            if (isOn) SetRandomSeedToggle.isOn = false;
            DuplicateWallPanel.SetActive(isOn);
            RefreshDuplicateWallOptions();
            RebuildCreateRoomLayoutHierarchy();
        });
        ChangshaBaseScoreNoDealerToggle.onValueChanged.AddListener(OnChangshaBaseScoreModeChanged);
    }

    private void InitializeRoomPresentation() {
        if (!roomPresentationBaked || roomPresentationBound) return;
        roomPresentationBound = true;
        BindTierPresets();
        restoreDefaultsButton.onClick.AddListener(() => ConfirmSettingsReset(
            "确定恢复当前规则的默认设置？当前填写的房间信息和自定义配置将被重置。", ResetRoomDefaults));
        stepTimer.onValueChanged.AddListener(_ => RefreshTierPresets());
        roundTimer.onValueChanged.AddListener(_ => RefreshTierPresets());
        CuoheTypeDropdown.onValueChanged.AddListener(_ => RefreshTierPresets());
        HepaiWayDropdown.onValueChanged.AddListener(_ => RefreshTierPresets());
        RiichiStartingScoreInput.onValueChanged.AddListener(_ => RefreshTierPresets());
        foreach (var toggle in RoomRoundToggles.Where(t => t))
            toggle.onValueChanged.AddListener(_ => RefreshTierPresets());
        foreach (var toggle in RoomOptionToggles.Where(t => t))
            toggle.onValueChanged.AddListener(_ => RefreshRoomPresentation(true));
        RefreshRoomPresentation(false); ReflowRoomPresentation();
    }

    private void RefreshRoomPresentation(bool animate = false) {
        if (!roomPresentationBaked) return;
        RefreshTierPresets();
        SubRuleText.text = "子规则"; SubRuleText.gameObject.SetActive(true); SubRuleDropdown.gameObject.SetActive(true);
        RoomFieldState(PasswordPanel, passwordInput, true, passwordToggle.isOn);
        RoomFieldState(SetRandomSeedPanel, randomSeedInput, true, SetRandomSeedToggle.isOn);
        RoomFieldState(InputHepaiLimitPlane, HepaiLimitInput, InputHepaiLimitToggle.gameObject.activeSelf && !IsShanghaiQiaoma, InputHepaiLimitToggle.isOn);
        RoomFieldState(DuplicateWallPanel, DuplicateKeyInput, _ruleState == "guobiao", DuplicateWallToggle && DuplicateWallToggle.isOn);
        bool cuoheVisible = DefaultsOf(_ruleState).ContainsKey(CreateRoomKeys.CuoheType) && CuoHeheToggle.gameObject.activeSelf;
        if (CuoheTypePanel) CuoheTypePanel.SetActive(cuoheVisible);
        if (CuoheTypeDropdown) CuoheTypeDropdown.interactable = CuoHeheToggle.isOn;
        if (CuoheTypePanel) {
            var group = CuoheTypePanel.GetComponent<CanvasGroup>();
            if (group) group.alpha = CuoHeheToggle.isOn ? 1 : .32f;
        }
        if (stepTimer) stepTimer.transform.parent.gameObject.SetActive(stepTimer.gameObject.activeSelf);
        if (roundTimer) roundTimer.transform.parent.gameObject.SetActive(roundTimer.gameObject.activeSelf);
        var rounds = RoomContent.Find("RoundSelection"); if (rounds) rounds.gameObject.SetActive(gameTime1Button.gameObject.activeSelf);
        if (GuobiaoFlowersLabel) GuobiaoFlowersLabel.text = "花牌";
        bool changsha = _ruleState == "changsha";
        if (ChangshaScoreRow) {
            ChangshaScoreRow.SetActive(changsha);
            if (changsha) {
                ChangshaSmallHuScorePanel.SetActive(true); ChangshaBigHuScorePanel.SetActive(true);
                ChangshaScoreRow.GetComponent<CreateRoomReveal>()?.SetExpanded(ChangshaBaseScoreNoDealerToggle.isOn, animate);
            }
        }
    }
    private static void RoomFieldState(GameObject panel, TMP_InputField input, bool visible, bool enabled) {
        if (!panel || !input) return;
        if (panel.activeSelf != visible) panel.SetActive(visible);
        input.interactable = enabled;
        var title = panel.GetComponentsInChildren<TMP_Text>().FirstOrDefault(t => !t.GetComponentInParent<TMP_InputField>(true));
        if (title) title.color = enabled ? Color.black : RoomMuted;
    }

    public void ReflowRoomPresentation() {
        if (!roomPresentationBaked) return;
        float width = ((RectTransform)transform).rect.width;
        bool narrow = width < 1700;
        float headerHeight = narrow ? 196 : 128;
        var header = (RectTransform)transform.Find("HeaderPanel"); RoomFill(header); header.anchorMin = new Vector2(0, 1); header.offsetMin = new Vector2(0, -headerHeight);
        var title = header.Find("TitleText").GetComponent<TMP_Text>(); title.fontSize = SubRuleText.fontSize = narrow ? 40 : 44;
        title.margin = Vector4.zero; SubRuleText.margin = Vector4.zero;
        float titleWidth = Mathf.Ceil(title.GetPreferredValues().x), ruleX = narrow ? 36 : 36 + titleWidth + 16;
        float subX = ruleX + RoomRuleDropdownWidth + 32, subWidth = Mathf.Ceil(SubRuleText.GetPreferredValues().x);
        RoomBox(title.rectTransform, 36, narrow ? 0 : 16, titleWidth, 96);
        RoomBox((RectTransform)chooseRule.transform, ruleX, narrow ? 112 : 36, RoomRuleDropdownWidth, 56);
        RoomBox(SubRuleText.rectTransform, subX, narrow ? 100 : 24, subWidth, 80);
        RoomBox((RectTransform)SubRuleDropdown.transform, subX + subWidth + 12, narrow ? 112 : 36, _ruleState=="hongkong" ? 420 : 326, 56);
        foreach (var dropdown in new[] { chooseRule, SubRuleDropdown }) {
            RoomDropdownTemplateHeight(dropdown);
            dropdown.captionText.enableAutoSizing = false;
            dropdown.captionText.fontSize = RoomRuleCaptionFontSize;
            dropdown.captionText.alignment = TextAlignmentOptions.Center;
            dropdown.captionText.margin = Vector4.zero;
            var arrow = (RectTransform)dropdown.transform.Find("Arrow");
            float arrowAreaWidth = -2 * arrow.anchoredPosition.x;
            RoomFill(dropdown.captionText.rectTransform, 0, 0, arrowAreaWidth, 0);
            if (dropdown.itemText) {
                dropdown.itemText.enableAutoSizing = false;
                dropdown.itemText.fontSize = RoomRuleItemFontSize;
            }
        }
        var add = (RectTransform)addRuleButton.transform; RoomBox(add, 0, 16, 410, 92); add.anchorMin = add.anchorMax = Vector2.one; add.pivot = Vector2.one; add.anchoredPosition = new Vector2(-36, -16);
        var body = (RectTransform)transform.Find("Create_Panel"); RoomFill(body, 0, headerHeight, 0, 116);
        float margin = Mathf.Max(36, (width - 1440) / 2);
        float rightMargin = Mathf.Max(margin, RoomHelpWidth(width) + RoomHelpGutter);
        if (margin < 210) margin = Mathf.Max(36, width - 1440 - rightMargin);
        var scroll = body.Find("Scroll View").GetComponent<ScrollRect>(); RoomFill((RectTransform)scroll.transform, margin, 16, rightMargin, 20);
        LayoutTierPresets(width, margin, rightMargin, scroll);
        if (scroll.verticalScrollbar) {
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            var bar = (RectTransform)scroll.verticalScrollbar.transform;
            RoomFill(bar); bar.anchorMin = new Vector2(1, 0); bar.offsetMin = new Vector2(19, -10); bar.offsetMax = new Vector2(33, 10);
        }
        RoomFill(scroll.viewport, 0, 0, 12, 0);
        var content = RoomContent; content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1); content.offsetMin = new Vector2(0, content.offsetMin.y); content.offsetMax = new Vector2(0, content.offsetMax.y);
        float textWidth = width - margin - rightMargin - 12 - 52;
        RoomHeight((RectTransform)SubRuleDescriptionText.transform.parent, SubRuleDescriptionText.GetPreferredValues(Mathf.Max(200, textWidth), 0).y + 32);
        var footer = (RectTransform)transform.Find("FooterPanel"); RoomFill(footer); footer.anchorMax = new Vector2(1, 0); footer.offsetMax = new Vector2(0, 116);
        RoomFooterButton(createButton, 36); RoomFooterButton(closeButton, 406);
        if (restoreDefaultsButton) {
            var reset = (RectTransform)restoreDefaultsButton.transform;
            reset.anchorMin = reset.anchorMax = reset.pivot = new Vector2(1, .5f);
            reset.anchoredPosition = new Vector2(-778, 0); reset.sizeDelta = new Vector2(238, 78);
        }
        ReflowRoomHelp();
        RefreshHongKongMainControls();
        var details = transform.Find("DetailedConfigPanel_taiwan");
        foreach (var overlay in new[] { details, details ? details.Find("FanTablePanel") : null, transform.Find("DetailedConfigPanel_riichi") }) {
            if (!overlay) continue;
            var dialog = (RectTransform)overlay.Find("Dialog");
            dialog.anchorMin = new Vector2(.5f, 0); dialog.anchorMax = new Vector2(.5f, 1); dialog.pivot = new Vector2(.5f, .5f);
            dialog.anchoredPosition = new Vector2(0, 24); dialog.sizeDelta = new Vector2(Mathf.Min(1480, width - 80), -64);
        }
        LayoutRebuilder.MarkLayoutForRebuild(content);
    }
    private static void RoomFooterButton(Button button, float right) {
        var rect = (RectTransform)button.transform; rect.anchorMin = rect.anchorMax = new Vector2(1, .5f); rect.pivot = new Vector2(1, .5f); rect.anchoredPosition = new Vector2(-right, 0); rect.sizeDelta = new Vector2(330, 78);
    }

    private static void RoomFill(RectTransform rect, float left = 0, float top = 0, float right = 0, float bottom = 0) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(-right, -top); }
    private static void RoomBox(RectTransform rect, float x, float y, float width, float height) { rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); }
    private static LayoutElement RoomHeight(RectTransform rect, float height) {
        var layout = rect.GetComponent<LayoutElement>();
#if UNITY_EDITOR
        if (!layout && !Application.isPlaying) layout = rect.gameObject.AddComponent<LayoutElement>();
#endif
        layout.minHeight = 0; layout.preferredHeight = height; layout.flexibleHeight = 0; return layout;
    }
}
