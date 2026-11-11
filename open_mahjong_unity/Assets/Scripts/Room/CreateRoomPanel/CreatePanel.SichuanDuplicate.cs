using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class CreatePanel {
    private static readonly int[] SichuanDuplicateBoardCounts = { 1, 4, 8, 12, 16 };
    private static readonly string[] SichuanWallSources = { "training", "key", "seed", "manual" };
    [SerializeField] private GameObject SichuanWallSourcePanel;
    [SerializeField] private Toggle[] SichuanWallSourceToggles;
    [SerializeField] private GameObject SichuanTrainingSeedPanel;
    [SerializeField] private TMP_InputField SichuanTrainingSeedInput;
    [SerializeField] private UnityEngine.UI.Button SichuanTrainingGenerateButton;
    [SerializeField] private UnityEngine.UI.Button SichuanTrainingCopyButton;
    [SerializeField] private TMP_Dropdown SichuanDuplicateRounds;
    [SerializeField] private Toggle SichuanDuplicateSyncToggle;
    [SerializeField] private UnityEngine.UI.Toggle SichuanRealTimeToggle;
    [SerializeField] private TMP_Dropdown SichuanSecondWaitDropdown;
    private const string SichuanRealTimeHelp = "真实时间：其他玩家出牌后，如果下一位应由你摸牌，碰、明杠或和牌使用正常局时与步时；否则使用第二等待时间。第二等待时间结束后，牌局不再专门等待你，但本张弃牌尚未处理完时，仍可操作。第二等待时间也用于战术鸣牌的抢断询问。血战有人和牌退场后，按仍在行牌的顺序判断。";
    private int SelectedSichuanSecondWait => SichuanLobby.SecondWaitSeconds[SichuanSecondWaitDropdown ? Mathf.Clamp(SichuanSecondWaitDropdown.value, 0, SichuanLobby.SecondWaitSeconds.Length - 1) : 1];
    [SerializeField] private UnityEngine.UI.Image creationHeader;
    [SerializeField] private UnityEngine.UI.Image creationFooter;
    [SerializeField] private Color ordinaryHeaderColor = new Color32(44, 58, 116, 255);
    [SerializeField] private Color ordinaryFooterColor = new Color32(43, 57, 116, 255);
    [SerializeField] private Color venueCreationColor = new Color32(43, 103, 79, 255);
    private string duplicateVenueDraftId;
    private bool IsSichuanDuplicate => _ruleState == "sichuan" && SichuanLobby.IsDuplicate(GetSelectedSubRule());
    private string SelectedSichuanWallSource {
        get {
            if (SichuanWallSourceToggles != null)
                for (int i = 0; i < SichuanWallSourceToggles.Length; i++)
                    if (SichuanWallSourceToggles[i] && SichuanWallSourceToggles[i].isOn) return SichuanWallSources[i];
            return "key";
        }
    }
    private bool IsSichuanTraining => IsSichuanDuplicate && SelectedSichuanWallSource == "training";

    private void ResetSichuanWallSource() {
        if (SichuanRealTimeToggle) SichuanRealTimeToggle.SetIsOnWithoutNotify(false);
        if (SichuanSecondWaitDropdown) SichuanSecondWaitDropdown.SetValueWithoutNotify(1);
        if (SichuanWallSourceToggles != null)
            for (int i = 0; i < SichuanWallSourceToggles.Length; i++)
                if (SichuanWallSourceToggles[i]) SichuanWallSourceToggles[i].SetIsOnWithoutNotify(i == 1);
        if (SichuanTrainingSeedInput) SichuanTrainingSeedInput.SetTextWithoutNotify("");
    }

    private void GenerateSichuanTrainingSeed() {
        var bytes = new byte[32];
        using (var random = System.Security.Cryptography.RandomNumberGenerator.Create()) random.GetBytes(bytes);
        if (bytes.All(value => value == 0)) bytes[0] = 1;
        SichuanTrainingSeedInput.text = System.BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    private void CopySichuanTrainingSeed() {
        if (MasterSeedInputValidator.TryNormalizeHex(SichuanTrainingSeedInput.text, out string seed, out string error)
            && seed != new string('0', MasterSeedInputValidator.HexLength)) {
            GUIUtility.systemCopyBuffer = seed;
            ShowRoomHelp("训练随机种子已复制。");
        } else ShowRoomHelp(error ?? "训练随机种子不能全为零");
    }

    private void ApplyCreationContextColors() {
        bool venue = context != CreationContext.Room && !string.IsNullOrEmpty(_venueEventId);
        if (creationHeader) creationHeader.color = venue ? venueCreationColor : ordinaryHeaderColor;
        if (creationFooter) creationFooter.color = venue ? venueCreationColor : ordinaryFooterColor;
    }

    private void RefreshSichuanDuplicateControls() {
        bool paired = IsSichuanDuplicate;
        bool training = IsSichuanTraining;
        if (SichuanWallSourcePanel) SichuanWallSourcePanel.SetActive(paired);
        if (SichuanTrainingSeedPanel) SichuanTrainingSeedPanel.SetActive(training);
        if (paired && DuplicateWallPanel) DuplicateWallPanel.SetActive(!training);
        if (SichuanDuplicateRounds) {
            SichuanDuplicateRounds.transform.parent.gameObject.SetActive(paired);
            SichuanDuplicateRounds.gameObject.SetActive(paired);
            SichuanDuplicateRounds.interactable = paired && string.IsNullOrWhiteSpace(SelectedDuplicateKey);
            if (paired && !SichuanDuplicateRounds.interactable)
                TableText.Set(SichuanDuplicateRounds.captionText, "跟随密钥", "sichuan");
            else SichuanDuplicateRounds.RefreshShownValue();
        }
        if (SichuanDuplicateSyncToggle) SichuanDuplicateSyncToggle.gameObject.SetActive(paired);
        if (SichuanRealTimeToggle) SichuanRealTimeToggle.gameObject.SetActive(paired);
        if (SichuanSecondWaitDropdown) {
            bool showWait = paired && SichuanRealTimeToggle && SichuanRealTimeToggle.isOn;
            if (!showWait) SichuanSecondWaitDropdown.Hide();
            SichuanSecondWaitDropdown.transform.parent.gameObject.SetActive(showWait);
            SichuanSecondWaitDropdown.gameObject.SetActive(showWait);
        }
        if (paired) {
            foreach (var toggle in RoomRoundToggles) if (toggle) toggle.gameObject.SetActive(false);
            var rounds = RoomContent.Find("RoundSelection");
            if (rounds) rounds.gameObject.SetActive(false);
            SetRandomSeedToggle.SetIsOnWithoutNotify(false);
            SetRandomSeedToggle.gameObject.SetActive(false);
            SetRandomSeedPanel.SetActive(false);
        } else SetRandomSeedToggle.gameObject.SetActive(true);
        if (DuplicateKeyInput && DuplicateKeyInput.placeholder is TMP_Text placeholder) {
            TableText.Set(placeholder, paired ? SelectedSichuanWallSource == "key" ? "留空自动生成，或输入已有密钥" : "输入" + DuplicateWallDisplay.TypeName(SelectedSichuanWallSource) + "密钥" : "牌山密钥", _ruleState);
            if (paired) placeholder.gameObject.SetActive(string.IsNullOrEmpty(DuplicateKeyInput.text));
        }
    }

    private void BindSichuanDuplicateControls() {
        if (SichuanRealTimeToggle) SichuanRealTimeToggle.onValueChanged.AddListener(_ => {
            RefreshSichuanDuplicateControls();
            RebuildCreateRoomLayoutHierarchy();
        });
        if (DuplicateKeyInput) DuplicateKeyInput.onValueChanged.AddListener(_ => RefreshSichuanDuplicateControls());
        if (SichuanWallSourceToggles != null)
            foreach (var toggle in SichuanWallSourceToggles.Where(t => t)) toggle.onValueChanged.AddListener(selected => {
                if (!selected) return;
                // Drafts can be edited while their overlay is inactive; ToggleGroup only normalizes active toggles.
                foreach (var other in SichuanWallSourceToggles.Where(t => t && t != toggle)) other.SetIsOnWithoutNotify(false);
                if (IsSichuanDuplicate) AllowSpectatorToggle.SetIsOnWithoutNotify(IsSichuanTraining);
                RefreshDuplicateWallOptions();
                RefreshRoomPresentation();
                RebuildCreateRoomLayoutHierarchy();
            });
        if (SichuanTrainingGenerateButton) SichuanTrainingGenerateButton.onClick.AddListener(GenerateSichuanTrainingSeed);
        if (SichuanTrainingCopyButton) SichuanTrainingCopyButton.onClick.AddListener(CopySichuanTrainingSeed);
    }

#if UNITY_EDITOR
    public void BakeSichuanDuplicateControls() {
        BakeSichuanRealTimeControls();
        BakeSichuanWallSourceControls();
        if (!SichuanDuplicateRounds) {
            var row = Instantiate(roundTimer.transform.parent.gameObject, roundTimer.transform.parent.parent);
            row.name = "SichuanDuplicateRounds";
            row.transform.SetSiblingIndex(roundTimer.transform.parent.GetSiblingIndex());
            SichuanDuplicateRounds = row.GetComponentInChildren<TMP_Dropdown>(true);
            SichuanDuplicateRounds.onValueChanged = new TMP_Dropdown.DropdownEvent();
            SichuanDuplicateRounds.ClearOptions();
            SichuanDuplicateRounds.AddOptions(SichuanDuplicateBoardCounts.Select(n => n + "副").ToList());
            SichuanDuplicateRounds.SetValueWithoutNotify(2);
            foreach (var text in row.GetComponentsInChildren<TMP_Text>(true)) {
                if (text.GetComponentInParent<TMP_Dropdown>(true)) continue;
                TableText.Set(text, "副数", "sichuan");
            }
            foreach (var help in row.GetComponentsInChildren<CreateRoomHelpTrigger>(true))
                help.Configure(this, "密钥留空时按所选副数生成；输入已有密钥时，副数跟随密钥。");
            row.SetActive(false);
        }
        if (!SichuanDuplicateSyncToggle) {
            SichuanDuplicateSyncToggle = Instantiate(DuplicateWallToggle, DuplicateWallToggle.transform.parent);
            SichuanDuplicateSyncToggle.name = "SichuanDuplicateSync";
            SichuanDuplicateSyncToggle.group = null;
            SichuanDuplicateSyncToggle.onValueChanged = new Toggle.ToggleEvent();
            SichuanDuplicateSyncToggle.SetIsOnWithoutNotify(false);
            TableText.Set(SichuanDuplicateSyncToggle.GetComponentInChildren<TMP_Text>(true), "同步模式", "sichuan");
            var help = SichuanDuplicateSyncToggle.GetComponent<CreateRoomHelpTrigger>();
            if (help) help.Configure(this, "同步模式：两室每副同时开始。非同步模式：各室可连续进行，全部结束后公开最终比分。");
            SichuanDuplicateSyncToggle.gameObject.SetActive(false);
        }
        if (!creationHeader) {
            creationHeader = transform.Find("HeaderPanel").GetComponent<UnityEngine.UI.Image>();
            ordinaryHeaderColor = creationHeader.color;
        }
        SichuanDuplicateRounds.gameObject.SetActive(true);
        if (!creationFooter) {
            creationFooter = transform.Find("FooterPanel").GetComponent<UnityEngine.UI.Image>();
            ordinaryFooterColor = creationFooter.color;
        }
        UnityEditor.EditorUtility.SetDirty(this);
    }

    public void BakeSichuanRealTimeControls() {
        if (!SichuanRealTimeToggle) {
            SichuanRealTimeToggle = Instantiate(DuplicateWallToggle, DuplicateWallToggle.transform.parent);
            SichuanRealTimeToggle.name = "SichuanRealTime";
            SichuanRealTimeToggle.group = null;
            SichuanRealTimeToggle.onValueChanged = new UnityEngine.UI.Toggle.ToggleEvent();
            SichuanRealTimeToggle.SetIsOnWithoutNotify(false);
            if (SichuanDuplicateSyncToggle)
                SichuanRealTimeToggle.transform.SetSiblingIndex(SichuanDuplicateSyncToggle.transform.GetSiblingIndex() + 1);
        }
        var toggleLabel = SichuanRealTimeToggle.GetComponentInChildren<TMP_Text>(true);
        TableText.Set(toggleLabel, "真实时间", "sichuan");
        toggleLabel.fontStyle = FontStyles.Normal;
        toggleLabel.fontWeight = FontWeight.Regular;
        var help = SichuanRealTimeToggle.GetComponent<CreateRoomHelpTrigger>();
        if (help) help.Configure(this, SichuanRealTimeHelp);
        if (!SichuanSecondWaitDropdown) {
            var row = Instantiate(stepTimer.transform.parent.gameObject, stepTimer.transform.parent.parent);
            row.name = "SichuanSecondWait";
            row.transform.SetSiblingIndex(stepTimer.transform.parent.GetSiblingIndex() + 1);
            SichuanSecondWaitDropdown = row.GetComponentInChildren<TMP_Dropdown>(true);
            SichuanSecondWaitDropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
            SichuanSecondWaitDropdown.ClearOptions();
            SichuanSecondWaitDropdown.AddOptions(SichuanLobby.SecondWaitSeconds.Select(seconds => seconds + "秒").ToList());
            SichuanSecondWaitDropdown.SetValueWithoutNotify(1);
        }
        foreach (var text in SichuanSecondWaitDropdown.transform.parent.GetComponentsInChildren<TMP_Text>(true)) {
            if (!text.GetComponentInParent<TMP_Dropdown>(true)) {
                TableText.Set(text, "第二等待时间", "sichuan");
                var labelLayout = text.GetComponent<LayoutElement>();
                if (labelLayout) DestroyImmediate(labelLayout);
                RoomBox(text.rectTransform, 0, 0, 184, 72);
            }
            text.fontStyle = FontStyles.Normal;
            text.fontWeight = FontWeight.Regular;
        }
        RoomFill((RectTransform)SichuanSecondWaitDropdown.transform, 192, 8, 0, 8);
        foreach (var trigger in SichuanSecondWaitDropdown.transform.parent.GetComponentsInChildren<CreateRoomHelpTrigger>(true))
            trigger.Configure(this, "非下一位摸牌者的首次响应等待与战术鸣牌抢断询问共用此时长，不扣局时。关闭真实时间会保留所选秒数，战术鸣牌开启时继续使用。首次等待结束仍可操作；抢断询问到期后执行已确定的动作。");
        RoomDropdownTemplateHeight(SichuanSecondWaitDropdown);
        SichuanRealTimeToggle.gameObject.SetActive(false);
        SichuanSecondWaitDropdown.transform.parent.gameObject.SetActive(false);
        UnityEditor.EditorUtility.SetDirty(this);
    }

    private void BakeSichuanWallSourceControls() {
        if (!SichuanWallSourcePanel) {
            SichuanWallSourcePanel = new GameObject("SichuanWallSourcePanel", typeof(RectTransform), typeof(VerticalLayoutGroup));
            SichuanWallSourcePanel.transform.SetParent(RoomContent, false);
            var stack = SichuanWallSourcePanel.GetComponent<VerticalLayoutGroup>();
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = true; stack.childForceExpandHeight = false; stack.spacing = 6;
            var title = Instantiate(SubRuleText, stack.transform);
            title.name = "Title"; TableText.Set(title, "牌山来源", "sichuan");
            title.fontSize = 28; title.fontStyle = FontStyles.Normal; title.enableAutoSizing = false; title.color = RoomMuted;
            RoomHeight(title.rectTransform, 40);
            var choices = new GameObject("Sources", typeof(RectTransform), typeof(CreateRoomAdaptiveGrid), typeof(ToggleGroup));
            choices.transform.SetParent(stack.transform, false);
            var grid = choices.GetComponent<CreateRoomAdaptiveGrid>();
            grid.cellSize = new Vector2(260, 64); grid.spacing = new Vector2(16, 8); grid.minimumCellWidth = 250;
            var group = choices.GetComponent<ToggleGroup>(); group.allowSwitchOff = false;
            SichuanWallSourceToggles = new Toggle[SichuanWallSources.Length];
            for (int i = 0; i < SichuanWallSources.Length; i++) {
                var toggle = Instantiate(DuplicateWallToggle, choices.transform);
                toggle.name = "WallSource_" + SichuanWallSources[i];
                toggle.group = null; toggle.onValueChanged = new Toggle.ToggleEvent();
                toggle.SetIsOnWithoutNotify(i == 1); toggle.group = group;
                var label = toggle.GetComponentInChildren<TMP_Text>(true);
                TableText.Set(label, DuplicateWallDisplay.TypeName(SichuanWallSources[i]), "sichuan");
                label.fontStyle = FontStyles.Normal;
                var help = toggle.GetComponent<CreateRoomHelpTrigger>();
                if (help) help.Configure(this, i == 0 ? "按随机种子生成两室相同牌山，不创建复式密钥。训练默认允许观战，可以关闭。" : "使用对应类型的复式密钥，副数跟随密钥，禁止观战。");
                toggle.gameObject.SetActive(true);
                SichuanWallSourceToggles[i] = toggle;
            }
        }
        SichuanWallSourcePanel.transform.SetSiblingIndex(RoomContent.Find("ChioceDropdown").GetSiblingIndex());
        if (!SichuanTrainingSeedPanel) {
            SichuanTrainingSeedPanel = new GameObject("SichuanTrainingSeedPanel", typeof(RectTransform), typeof(VerticalLayoutGroup));
            SichuanTrainingSeedPanel.transform.SetParent(RoomContent, false);
            var stack = SichuanTrainingSeedPanel.GetComponent<VerticalLayoutGroup>();
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = true; stack.childForceExpandHeight = false; stack.spacing = 8;
            var title = Instantiate(SubRuleText, stack.transform);
            title.name = "Title"; TableText.Set(title, "训练随机种子", "sichuan");
            title.fontSize = 28; title.fontStyle = FontStyles.Normal; title.enableAutoSizing = false; title.color = RoomMuted;
            RoomHeight(title.rectTransform, 40);
            var row = new GameObject("Seed", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(stack.transform, false);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false; layout.spacing = 12;
            SichuanTrainingSeedInput = Instantiate(randomSeedInput, row.transform);
            SichuanTrainingSeedInput.name = "TrainingSeed";
            SichuanTrainingSeedInput.onValueChanged = new TMP_InputField.OnChangeEvent();
            SichuanTrainingSeedInput.onEndEdit = new TMP_InputField.SubmitEvent();
            SichuanTrainingSeedInput.characterLimit = 66;
            SichuanTrainingSeedInput.SetTextWithoutNotify("");
            if (SichuanTrainingSeedInput.placeholder is TMP_Text placeholder) TableText.Set(placeholder, "粘贴64位十六进制种子，或点击随机生成", "sichuan");
            var inputSize = RoomHeight((RectTransform)SichuanTrainingSeedInput.transform, 64);
            inputSize.minWidth = 180; inputSize.preferredWidth = 520; inputSize.flexibleWidth = 1;
            UnityEngine.UI.Button MakeButton(string name, string caption) {
                var button = Instantiate(closeButton, row.transform);
                button.name = name; button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                var label = button.GetComponentInChildren<TMP_Text>(true);
                TableText.Set(label, caption, "sichuan"); label.fontStyle = FontStyles.Normal;
                var size = RoomHeight((RectTransform)button.transform, 64);
                size.minWidth = size.preferredWidth = 150; size.flexibleWidth = 0;
                button.gameObject.SetActive(true);
                return button;
            }
            SichuanTrainingGenerateButton = MakeButton("Generate", "随机生成");
            SichuanTrainingCopyButton = MakeButton("Copy", "复制");
        }
        SichuanTrainingSeedPanel.transform.SetSiblingIndex(RoomContent.Find("InputToggleValue").GetSiblingIndex() + 1);
        foreach (var panel in new[] { SichuanWallSourcePanel, SichuanTrainingSeedPanel }) {
            var title = panel.transform.Find("Title").GetComponent<TMP_Text>();
            title.color = RoomMuted;
        }
        SichuanWallSourcePanel.SetActive(false); SichuanTrainingSeedPanel.SetActive(false);
    }
#endif
}
