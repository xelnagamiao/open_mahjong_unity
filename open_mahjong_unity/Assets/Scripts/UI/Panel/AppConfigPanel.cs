using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.IO;

public class AppConfigPanel : MonoBehaviour {
    public static AppConfigPanel Instance;

    [Header("音量设置")]
    [SerializeField] private ConfigSlider masterVolumeSlider;
    [SerializeField] private ConfigSlider musicVolumeSlider;
    [SerializeField] private ConfigSlider soundEffectVolumeSlider;
    [SerializeField] private ConfigSlider voiceVolumeSlider;

    [Header("语言")]
    [SerializeField] private TMP_Dropdown languageDropdown;

    [Header("对局显示与操作")]
    [SerializeField] private TMP_Dropdown whiteDragonFaceDropdown;
    [SerializeField] private TMP_Dropdown moqieShortcutDropdown;
    [SerializeField] private TMP_Dropdown askOtherPassShortcutDropdown;
    [SerializeField] private TMP_Dropdown targetFrameRateDropdown;
    [SerializeField] private TMP_Dropdown streamerModeDropdown;
    [SerializeField] private TMP_Dropdown handCutConfirmDropdown;
    [SerializeField] private TMP_Dropdown handSortSuitDropdown;
    [SerializeField] private TMP_Dropdown handSortHonorDropdown;
    [SerializeField] private TMP_Dropdown handSortDragonDropdown;
    [SerializeField] private TMP_Dropdown handSortRiichiDragonDropdown;
    [SerializeField] private TMP_Dropdown actionButtonColorDropdown;
    [SerializeField] private TMP_Dropdown openingAutoBuhuaDropdown;
    [SerializeField] private TMP_Dropdown meldSpacingDropdown;
    [SerializeField] private TMP_Dropdown vsyncDropdown;

    [Header("提示音效")]
    [SerializeField] private TMP_Dropdown gongHuSoundDropdown;
    [SerializeField] private TMP_Dropdown matchSuccessSoundDropdown;
    [SerializeField] private TMP_Dropdown tileOutlinePresetDropdown;

    private void Awake() {
        Instance = this;
        InitializeVolumeManager();
        EnsureLanguageDropdown();
        EnsureStreamerModeDropdown();
        EnsureHandCutConfirmDropdown();
        EnsureHandSortSuitDropdown();
        EnsureHandSortHonorDropdown();
        EnsureHandSortDragonDropdown();
        EnsureHandSortRiichiDragonDropdown();
        EnsureActionButtonColorDropdown();
        EnsureVsyncDropdown();
        InitializeGameplayDropdownOptions();
        if (languageDropdown != null) {
            languageDropdown.onValueChanged.AddListener(OnLanguageDropdownChanged);
        }
        whiteDragonFaceDropdown.onValueChanged.AddListener(OnWhiteDragonFaceDropdownChanged);
        moqieShortcutDropdown.onValueChanged.AddListener(OnMoqieShortcutDropdownChanged);
        askOtherPassShortcutDropdown.onValueChanged.AddListener(OnAskOtherPassShortcutDropdownChanged);
        if (streamerModeDropdown != null) {
            streamerModeDropdown.onValueChanged.AddListener(OnStreamerModeDropdownChanged);
        }
        if (handCutConfirmDropdown != null) {
            handCutConfirmDropdown.onValueChanged.AddListener(OnHandCutConfirmDropdownChanged);
        }
        if (handSortSuitDropdown != null) {
            handSortSuitDropdown.onValueChanged.AddListener(OnHandSortSuitDropdownChanged);
        }
        if (handSortHonorDropdown != null) {
            handSortHonorDropdown.onValueChanged.AddListener(OnHandSortHonorDropdownChanged);
        }
        if (handSortDragonDropdown != null) {
            handSortDragonDropdown.onValueChanged.AddListener(OnHandSortDragonDropdownChanged);
        }
        if (handSortRiichiDragonDropdown != null) {
            handSortRiichiDragonDropdown.onValueChanged.AddListener(OnHandSortRiichiDragonDropdownChanged);
        }
        if (actionButtonColorDropdown != null) {
            actionButtonColorDropdown.onValueChanged.AddListener(OnActionButtonColorDropdownChanged);
        }
        if (openingAutoBuhuaDropdown != null) {
            openingAutoBuhuaDropdown.onValueChanged.AddListener(OnOpeningAutoBuhuaDropdownChanged);
        }
        if (meldSpacingDropdown != null) {
            meldSpacingDropdown.onValueChanged.AddListener(OnMeldSpacingDropdownChanged);
        }
        if (vsyncDropdown != null) {
            vsyncDropdown.onValueChanged.AddListener(OnVsyncDropdownChanged);
        }
        if (gongHuSoundDropdown != null) {
            gongHuSoundDropdown.onValueChanged.AddListener(OnGongHuSoundDropdownChanged);
        }
        if (matchSuccessSoundDropdown != null) {
            matchSuccessSoundDropdown.onValueChanged.AddListener(OnMatchSuccessSoundDropdownChanged);
        }
        if (tileOutlinePresetDropdown != null) {
            tileOutlinePresetDropdown.onValueChanged.AddListener(OnTileOutlinePresetDropdownChanged);
        }
        ApplyTargetFrameRateDropdownVisibility();
    }

    private void OnEnable() {
        ApplyTargetFrameRateDropdownVisibility();
        SyncVolumeSlidersFromConfig();
        SyncGameplayDropdownsFromConfig();
    }

    private void InitializeVolumeManager() {
        masterVolumeSlider.Init();
        musicVolumeSlider.Init();
        soundEffectVolumeSlider.Init();
        voiceVolumeSlider.Init();
    }

    private void SyncVolumeSlidersFromConfig() {
        masterVolumeSlider?.SyncFromConfig();
        musicVolumeSlider?.SyncFromConfig();
        soundEffectVolumeSlider?.SyncFromConfig();
        voiceVolumeSlider?.SyncFromConfig();
    }

    private void InitializeGameplayDropdownOptions() {
        if (languageDropdown != null) {
            languageDropdown.ClearOptions();
            languageDropdown.AddOptions(new List<string>(ConfigManager.LanguageOptionLabels));
        }
        whiteDragonFaceDropdown.ClearOptions();
        whiteDragonFaceDropdown.AddOptions(new List<string> { "纯白", "回形" });
        moqieShortcutDropdown.ClearOptions();
        moqieShortcutDropdown.AddOptions(new List<string> { "双击摸切", "右键摸切", "无快捷键" });
        askOtherPassShortcutDropdown.ClearOptions();
        askOtherPassShortcutDropdown.AddOptions(new List<string> { "右键取消", "双击取消", "无快捷键" });
        if (streamerModeDropdown != null) {
            streamerModeDropdown.ClearOptions();
            streamerModeDropdown.AddOptions(new List<string> { "关", "开" });
        }
        if (handCutConfirmDropdown != null) {
            handCutConfirmDropdown.ClearOptions();
            handCutConfirmDropdown.AddOptions(new List<string> { "关", "开" });
        }
        if (handSortSuitDropdown != null) {
            handSortSuitDropdown.ClearOptions();
            handSortSuitDropdown.AddOptions(new List<string>(TileIdOrder.SuitOrderOptions));
        }
        if (handSortHonorDropdown != null) {
            handSortHonorDropdown.ClearOptions();
            handSortHonorDropdown.AddOptions(new List<string>(TileIdOrder.HonorOrderOptions));
        }
        if (handSortDragonDropdown != null) {
            handSortDragonDropdown.ClearOptions();
            handSortDragonDropdown.AddOptions(new List<string>(TileIdOrder.DragonOrderOptions));
        }
        if (handSortRiichiDragonDropdown != null) {
            handSortRiichiDragonDropdown.ClearOptions();
            handSortRiichiDragonDropdown.AddOptions(new List<string>(TileIdOrder.RiichiDragonOrderOptions));
        }
        if (actionButtonColorDropdown != null) {
            actionButtonColorDropdown.ClearOptions();
            actionButtonColorDropdown.AddOptions(new List<string> { "关", "开" });
        }
        if (openingAutoBuhuaDropdown != null) {
            openingAutoBuhuaDropdown.ClearOptions();
            openingAutoBuhuaDropdown.AddOptions(new List<string> { "关", "开" });
        }
        if (meldSpacingDropdown != null) {
            meldSpacingDropdown.ClearOptions();
            meldSpacingDropdown.AddOptions(new List<string> { "关", "开" });
        }
        if (vsyncDropdown != null) {
            vsyncDropdown.ClearOptions();
            vsyncDropdown.AddOptions(new List<string> { "关", "开" });
        }
        if (gongHuSoundDropdown != null) {
            gongHuSoundDropdown.ClearOptions();
            gongHuSoundDropdown.AddOptions(new List<string> { "关", "开" });
        }
        if (matchSuccessSoundDropdown != null) {
            matchSuccessSoundDropdown.ClearOptions();
            matchSuccessSoundDropdown.AddOptions(new List<string> { "关", "开" });
        }
        if (tileOutlinePresetDropdown != null) {
            tileOutlinePresetDropdown.ClearOptions();
            tileOutlinePresetDropdown.AddOptions(new List<string>(ConfigManager.TileOutlinePresetLabels));
        }
    }

    private void SyncGameplayDropdownsFromConfig() {
        if (languageDropdown != null) {
            languageDropdown.SetValueWithoutNotify((int)ConfigManager.Instance.LanguageMode);
            languageDropdown.RefreshShownValue();
        }
        whiteDragonFaceDropdown.SetValueWithoutNotify(ConfigManager.Instance.WhiteDragonFaceMode);
        whiteDragonFaceDropdown.RefreshShownValue();
        moqieShortcutDropdown.SetValueWithoutNotify(ConfigManager.Instance.MoqieShortcutMode);
        moqieShortcutDropdown.RefreshShownValue();
        askOtherPassShortcutDropdown.SetValueWithoutNotify(ConfigManager.Instance.AskOtherPassShortcutMode);
        askOtherPassShortcutDropdown.RefreshShownValue();
        if (streamerModeDropdown != null) {
            streamerModeDropdown.SetValueWithoutNotify(ConfigManager.Instance.StreamerModeEnabled ? 1 : 0);
            streamerModeDropdown.RefreshShownValue();
        }
        if (handCutConfirmDropdown != null) {
            handCutConfirmDropdown.SetValueWithoutNotify(ConfigManager.Instance.HandCutConfirmMode);
            handCutConfirmDropdown.RefreshShownValue();
        }
        if (handSortSuitDropdown != null) {
            handSortSuitDropdown.SetValueWithoutNotify(ConfigManager.Instance.HandSortSuitOrderMode);
            handSortSuitDropdown.RefreshShownValue();
        }
        if (handSortHonorDropdown != null) {
            handSortHonorDropdown.SetValueWithoutNotify(ConfigManager.Instance.HandSortHonorOrderMode);
            handSortHonorDropdown.RefreshShownValue();
        }
        if (handSortDragonDropdown != null) {
            handSortDragonDropdown.SetValueWithoutNotify(ConfigManager.Instance.HandSortDragonOrderMode);
            handSortDragonDropdown.RefreshShownValue();
        }
        if (handSortRiichiDragonDropdown != null) {
            handSortRiichiDragonDropdown.SetValueWithoutNotify(ConfigManager.Instance.HandSortRiichiDragonOrderMode);
            handSortRiichiDragonDropdown.RefreshShownValue();
        }
        if (actionButtonColorDropdown != null) {
            actionButtonColorDropdown.SetValueWithoutNotify(ConfigManager.Instance.ActionButtonColorEnabled ? 1 : 0);
            actionButtonColorDropdown.RefreshShownValue();
        }
        if (openingAutoBuhuaDropdown != null) {
            openingAutoBuhuaDropdown.SetValueWithoutNotify(ConfigManager.Instance.OpeningAutoBuhuaEnabled ? 1 : 0);
            openingAutoBuhuaDropdown.RefreshShownValue();
        }
        if (meldSpacingDropdown != null) {
            meldSpacingDropdown.SetValueWithoutNotify(ConfigManager.Instance.MeldSpacingEnabled ? 1 : 0);
            meldSpacingDropdown.RefreshShownValue();
        }
        if (vsyncDropdown != null) {
            vsyncDropdown.SetValueWithoutNotify(ConfigManager.Instance.VsyncEnabled ? 1 : 0);
            vsyncDropdown.RefreshShownValue();
        }
        if (gongHuSoundDropdown != null) {
            gongHuSoundDropdown.SetValueWithoutNotify(ConfigManager.Instance.GongHuSoundEnabled ? 1 : 0);
            gongHuSoundDropdown.RefreshShownValue();
        }
        if (matchSuccessSoundDropdown != null) {
            matchSuccessSoundDropdown.SetValueWithoutNotify(ConfigManager.Instance.MatchSuccessSoundEnabled ? 1 : 0);
            matchSuccessSoundDropdown.RefreshShownValue();
        }
        if (tileOutlinePresetDropdown != null) {
            tileOutlinePresetDropdown.SetValueWithoutNotify(ConfigManager.Instance.TileOutlinePreset - 1);
            tileOutlinePresetDropdown.RefreshShownValue();
        }
    }

    private void ApplyTargetFrameRateDropdownVisibility() {
        if (targetFrameRateDropdown == null) {
            return;
        }

        bool showFrameRateSetting = !ConfigManager.IsTargetFrameRateLocked;
        targetFrameRateDropdown.gameObject.SetActive(showFrameRateSetting);
        targetFrameRateDropdown.interactable = showFrameRateSetting;
    }

    private void OnWhiteDragonFaceDropdownChanged(int value) {
        ConfigManager.Instance.SetWhiteDragonFaceMode(value);
    }

    private void OnMoqieShortcutDropdownChanged(int value) {
        ConfigManager.Instance.SetMoqieShortcutMode(value);
    }

    private void OnAskOtherPassShortcutDropdownChanged(int value) {
        ConfigManager.Instance.SetAskOtherPassShortcutMode(value);
    }

    private void OnVsyncDropdownChanged(int value) {
        ConfigManager.Instance.SetVsyncEnabled(value == 1);
    }

    private void OnStreamerModeDropdownChanged(int value) {
        ConfigManager.Instance.SetStreamerModeEnabled(value == 1);
    }

    private void OnHandCutConfirmDropdownChanged(int value) {
        ConfigManager.Instance.SetHandCutConfirmMode(value);
    }

    private void OnHandSortSuitDropdownChanged(int value) {
        ConfigManager.Instance.SetHandSortSuitOrderMode(value);
    }

    private void OnHandSortHonorDropdownChanged(int value) {
        ConfigManager.Instance.SetHandSortHonorOrderMode(value);
    }

    private void OnHandSortDragonDropdownChanged(int value) {
        ConfigManager.Instance.SetHandSortDragonOrderMode(value);
    }

    private void OnHandSortRiichiDragonDropdownChanged(int value) {
        ConfigManager.Instance.SetHandSortRiichiDragonOrderMode(value);
    }

    private void OnActionButtonColorDropdownChanged(int value) {
        ConfigManager.Instance.SetActionButtonColorEnabled(value == 1);
    }

    private void OnOpeningAutoBuhuaDropdownChanged(int value) {
        ConfigManager.Instance.SetOpeningAutoBuhuaEnabled(value == 1);
    }

    private void OnMeldSpacingDropdownChanged(int value) {
        ConfigManager.Instance.SetMeldSpacingEnabled(value == 1);
    }

    private void OnGongHuSoundDropdownChanged(int value) {
        ConfigManager.Instance.SetGongHuSoundEnabled(value == 1);
    }

    private void OnMatchSuccessSoundDropdownChanged(int value) {
        ConfigManager.Instance.SetMatchSuccessSoundEnabled(value == 1);
    }

    private void OnTileOutlinePresetDropdownChanged(int value) {
        ConfigManager.Instance.SetTileOutlinePresetFromDropdown(value);
    }

    private void OnLanguageDropdownChanged(int value) {
        ConfigManager.Instance.SetLanguageMode(value);
    }

    private void EnsureLanguageDropdown() {
        if (languageDropdown != null) {
            return;
        }
        TMP_Dropdown templateDropdown = whiteDragonFaceDropdown != null
            ? whiteDragonFaceDropdown
            : moqieShortcutDropdown;
        if (templateDropdown == null) {
            return;
        }

        Transform templateRow = templateDropdown.transform.parent;
        Transform rowContainer = templateRow != null ? templateRow.parent : null;
        if (templateRow == null || rowContainer == null) {
            return;
        }

        Transform existingRow = rowContainer.Find("LanguageRow");
        if (existingRow != null) {
            languageDropdown = existingRow.GetComponentInChildren<TMP_Dropdown>(true);
            return;
        }

        Transform newRowTransform = Instantiate(templateRow, rowContainer);
        newRowTransform.name = "LanguageRow";
        newRowTransform.SetSiblingIndex(0);

        languageDropdown = newRowTransform.GetComponentInChildren<TMP_Dropdown>(true);
        if (languageDropdown == null) {
            return;
        }
        languageDropdown.onValueChanged.RemoveAllListeners();

        TMP_Text[] labels = newRowTransform.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < labels.Length; i++) {
            TMP_Dropdown dropdown = labels[i].GetComponentInParent<TMP_Dropdown>();
            if (dropdown == languageDropdown) {
                continue;
            }
            labels[i].text = "语言";
            break;
        }
    }

    private void EnsureStreamerModeDropdown() {
        if (streamerModeDropdown != null) {
            return;
        }
        TMP_Dropdown templateDropdown = moqieShortcutDropdown != null
            ? moqieShortcutDropdown
            : targetFrameRateDropdown;
        if (templateDropdown == null) {
            return;
        }

        Transform templateRow = templateDropdown.transform.parent;
        Transform rowContainer = templateRow != null ? templateRow.parent : null;
        if (templateRow == null || rowContainer == null) {
            return;
        }

        Transform existingRow = rowContainer.Find("StreamerModeRow");
        if (existingRow != null) {
            streamerModeDropdown = existingRow.GetComponentInChildren<TMP_Dropdown>(true);
            return;
        }

        Transform newRowTransform = Instantiate(templateRow, rowContainer);
        newRowTransform.name = "StreamerModeRow";
        newRowTransform.SetSiblingIndex(templateRow.GetSiblingIndex() + 1);

        streamerModeDropdown = newRowTransform.GetComponentInChildren<TMP_Dropdown>(true);
        if (streamerModeDropdown == null) {
            return;
        }

        TMP_Text[] labels = newRowTransform.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < labels.Length; i++) {
            TMP_Dropdown dropdown = labels[i].GetComponentInParent<TMP_Dropdown>();
            if (dropdown == streamerModeDropdown) {
                continue;
            }
            labels[i].text = "主播模式";
            break;
        }
    }

    private void EnsureHandCutConfirmDropdown() {
        if (handCutConfirmDropdown != null) {
            return;
        }
        TMP_Dropdown templateDropdown = streamerModeDropdown != null
            ? streamerModeDropdown
            : moqieShortcutDropdown;
        if (templateDropdown == null) {
            return;
        }

        Transform templateRow = templateDropdown.transform.parent;
        Transform rowContainer = templateRow != null ? templateRow.parent : null;
        if (templateRow == null || rowContainer == null) {
            return;
        }

        Transform existingRow = rowContainer.Find("HandCutConfirmRow");
        if (existingRow != null) {
            handCutConfirmDropdown = existingRow.GetComponentInChildren<TMP_Dropdown>(true);
            return;
        }

        Transform newRowTransform = Instantiate(templateRow, rowContainer);
        newRowTransform.name = "HandCutConfirmRow";
        newRowTransform.SetSiblingIndex(templateRow.GetSiblingIndex() + 1);

        handCutConfirmDropdown = newRowTransform.GetComponentInChildren<TMP_Dropdown>(true);
        if (handCutConfirmDropdown == null) {
            return;
        }

        TMP_Text[] labels = newRowTransform.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < labels.Length; i++) {
            TMP_Dropdown dropdown = labels[i].GetComponentInParent<TMP_Dropdown>();
            if (dropdown == handCutConfirmDropdown) {
                continue;
            }
            labels[i].text = "两次点击确认出牌";
            break;
        }
    }

    private void EnsureHandSortSuitDropdown() {
        handSortSuitDropdown = CloneDropdownRow(
            handSortSuitDropdown,
            handCutConfirmDropdown != null ? handCutConfirmDropdown : moqieShortcutDropdown,
            "HandSortSuitRow",
            "自动理牌花色顺序");
    }

    private void EnsureHandSortHonorDropdown() {
        handSortHonorDropdown = CloneDropdownRow(
            handSortHonorDropdown,
            handSortSuitDropdown != null ? handSortSuitDropdown : moqieShortcutDropdown,
            "HandSortHonorRow",
            "自动理牌字牌位置");
    }

    private void EnsureHandSortDragonDropdown() {
        handSortDragonDropdown = CloneDropdownRow(
            handSortDragonDropdown,
            handSortHonorDropdown != null ? handSortHonorDropdown : handSortSuitDropdown,
            "HandSortDragonRow",
            "三元牌排序方式");
    }

    private void EnsureHandSortRiichiDragonDropdown() {
        handSortRiichiDragonDropdown = CloneDropdownRow(
            handSortRiichiDragonDropdown,
            handSortDragonDropdown != null ? handSortDragonDropdown : handSortHonorDropdown,
            "HandSortRiichiDragonRow",
            "日麻排序方式");
    }

    private void EnsureActionButtonColorDropdown() {
        actionButtonColorDropdown = CloneDropdownRow(
            actionButtonColorDropdown,
            handSortRiichiDragonDropdown != null ? handSortRiichiDragonDropdown : handSortDragonDropdown,
            "ActionButtonColorRow",
            "启用按钮颜色");
    }

    /// <summary>垂直同步开关行（默认开启）。未在场景中挂接时克隆一行“开/关”下拉。</summary>
    private void EnsureVsyncDropdown() {
        TMP_Dropdown template = meldSpacingDropdown != null ? meldSpacingDropdown
            : (actionButtonColorDropdown != null ? actionButtonColorDropdown : moqieShortcutDropdown);
        vsyncDropdown = CloneDropdownRow(vsyncDropdown, template, "VsyncRow", "垂直同步");
    }

    /// <summary>
    /// 若下拉框未在 Prefab 中挂好，则克隆一行已有的下拉行（含标签与下拉框），改名与改标签文字后返回新行内的下拉框。
    /// </summary>
    private TMP_Dropdown CloneDropdownRow(TMP_Dropdown current, TMP_Dropdown templateDropdown, string rowName, string labelText) {
        if (current != null) {
            return current;
        }
        if (templateDropdown == null) {
            return null;
        }

        Transform templateRow = templateDropdown.transform.parent;
        Transform rowContainer = templateRow != null ? templateRow.parent : null;
        if (templateRow == null || rowContainer == null) {
            return null;
        }

        Transform existingRow = rowContainer.Find(rowName);
        if (existingRow != null) {
            return existingRow.GetComponentInChildren<TMP_Dropdown>(true);
        }

        Transform newRowTransform = Instantiate(templateRow, rowContainer);
        newRowTransform.name = rowName;
        newRowTransform.SetSiblingIndex(templateRow.GetSiblingIndex() + 1);

        TMP_Dropdown newDropdown = newRowTransform.GetComponentInChildren<TMP_Dropdown>(true);
        if (newDropdown == null) {
            return null;
        }
        newDropdown.onValueChanged.RemoveAllListeners();

        TMP_Text[] labels = newRowTransform.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < labels.Length; i++) {
            TMP_Dropdown dropdown = labels[i].GetComponentInParent<TMP_Dropdown>();
            if (dropdown == newDropdown) {
                continue;
            }
            labels[i].text = labelText;
            break;
        }
        return newDropdown;
    }
}
