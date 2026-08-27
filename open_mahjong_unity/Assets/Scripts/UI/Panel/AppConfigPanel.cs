using System.Collections.Generic;
using UnityEngine;
using TMPro;

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
    [SerializeField] private TMP_Dropdown forcePassDropdown;
    [SerializeField] private TMP_Dropdown meldSpacingDropdown;
    [SerializeField] private TMP_Dropdown vsyncDropdown;

    [Header("提示音效")]
    [SerializeField] private TMP_Dropdown gongHuSoundDropdown;
    [SerializeField] private TMP_Dropdown matchSuccessSoundDropdown;
    [SerializeField] private TMP_Dropdown tileOutlinePresetDropdown;

    private void Awake() {
        Instance = this;
        masterVolumeSlider.Init();
        musicVolumeSlider.Init();
        soundEffectVolumeSlider.Init();
        voiceVolumeSlider.Init();
        InitializeGameplayDropdownOptions();
        languageDropdown.onValueChanged.AddListener(OnLanguageDropdownChanged);
        whiteDragonFaceDropdown.onValueChanged.AddListener(OnWhiteDragonFaceDropdownChanged);
        moqieShortcutDropdown.onValueChanged.AddListener(OnMoqieShortcutDropdownChanged);
        askOtherPassShortcutDropdown.onValueChanged.AddListener(OnAskOtherPassShortcutDropdownChanged);
        streamerModeDropdown.onValueChanged.AddListener(OnStreamerModeDropdownChanged);
        handCutConfirmDropdown.onValueChanged.AddListener(OnHandCutConfirmDropdownChanged);
        handSortSuitDropdown.onValueChanged.AddListener(OnHandSortSuitDropdownChanged);
        handSortHonorDropdown.onValueChanged.AddListener(OnHandSortHonorDropdownChanged);
        handSortDragonDropdown.onValueChanged.AddListener(OnHandSortDragonDropdownChanged);
        handSortRiichiDragonDropdown.onValueChanged.AddListener(OnHandSortRiichiDragonDropdownChanged);
        actionButtonColorDropdown.onValueChanged.AddListener(OnActionButtonColorDropdownChanged);
        openingAutoBuhuaDropdown.onValueChanged.AddListener(OnOpeningAutoBuhuaDropdownChanged);
        forcePassDropdown.onValueChanged.AddListener(OnForcePassDropdownChanged);
        meldSpacingDropdown.onValueChanged.AddListener(OnMeldSpacingDropdownChanged);
        vsyncDropdown.onValueChanged.AddListener(OnVsyncDropdownChanged);
        gongHuSoundDropdown.onValueChanged.AddListener(OnGongHuSoundDropdownChanged);
        matchSuccessSoundDropdown.onValueChanged.AddListener(OnMatchSuccessSoundDropdownChanged);
        tileOutlinePresetDropdown.onValueChanged.AddListener(OnTileOutlinePresetDropdownChanged);
        ApplyTargetFrameRateDropdownVisibility();
    }

    private void OnEnable() {
        ApplyTargetFrameRateDropdownVisibility();
        masterVolumeSlider.SyncFromConfig();
        musicVolumeSlider.SyncFromConfig();
        soundEffectVolumeSlider.SyncFromConfig();
        voiceVolumeSlider.SyncFromConfig();
        SyncGameplayDropdownsFromConfig();
    }

    private void InitializeGameplayDropdownOptions() {
        languageDropdown.ClearOptions();
        languageDropdown.AddOptions(new List<string>(ConfigManager.LanguageOptionLabels));
        whiteDragonFaceDropdown.ClearOptions();
        whiteDragonFaceDropdown.AddOptions(new List<string> { "纯白", "回形" });
        moqieShortcutDropdown.ClearOptions();
        moqieShortcutDropdown.AddOptions(new List<string> { "双击摸切", "右键摸切", "无快捷键" });
        askOtherPassShortcutDropdown.ClearOptions();
        askOtherPassShortcutDropdown.AddOptions(new List<string> { "右键取消", "双击取消", "无快捷键" });
        streamerModeDropdown.ClearOptions();
        streamerModeDropdown.AddOptions(new List<string> { "关", "开" });
        handCutConfirmDropdown.ClearOptions();
        handCutConfirmDropdown.AddOptions(new List<string> { "关", "开" });
        handSortSuitDropdown.ClearOptions();
        handSortSuitDropdown.AddOptions(new List<string>(TileIdOrder.SuitOrderOptions));
        handSortHonorDropdown.ClearOptions();
        handSortHonorDropdown.AddOptions(new List<string>(TileIdOrder.HonorOrderOptions));
        handSortDragonDropdown.ClearOptions();
        handSortDragonDropdown.AddOptions(new List<string>(TileIdOrder.DragonOrderOptions));
        handSortRiichiDragonDropdown.ClearOptions();
        handSortRiichiDragonDropdown.AddOptions(new List<string>(TileIdOrder.RiichiDragonOrderOptions));
        actionButtonColorDropdown.ClearOptions();
        actionButtonColorDropdown.AddOptions(new List<string> { "关", "开" });
        openingAutoBuhuaDropdown.ClearOptions();
        openingAutoBuhuaDropdown.AddOptions(new List<string> { "关", "开" });
        forcePassDropdown.ClearOptions();
        forcePassDropdown.AddOptions(new List<string> { "关", "开" });
        meldSpacingDropdown.ClearOptions();
        meldSpacingDropdown.AddOptions(new List<string> { "关", "开" });
        vsyncDropdown.ClearOptions();
        vsyncDropdown.AddOptions(new List<string> { "关", "开" });
        gongHuSoundDropdown.ClearOptions();
        gongHuSoundDropdown.AddOptions(new List<string> { "关", "开" });
        matchSuccessSoundDropdown.ClearOptions();
        matchSuccessSoundDropdown.AddOptions(new List<string> { "关", "开" });
        tileOutlinePresetDropdown.ClearOptions();
        tileOutlinePresetDropdown.AddOptions(new List<string>(ConfigManager.TileOutlinePresetLabels));
    }

    private void SyncGameplayDropdownsFromConfig() {
        languageDropdown.SetValueWithoutNotify((int)ConfigManager.Instance.LanguageMode);
        languageDropdown.RefreshShownValue();
        whiteDragonFaceDropdown.SetValueWithoutNotify(ConfigManager.Instance.WhiteDragonFaceMode);
        whiteDragonFaceDropdown.RefreshShownValue();
        moqieShortcutDropdown.SetValueWithoutNotify(ConfigManager.Instance.MoqieShortcutMode);
        moqieShortcutDropdown.RefreshShownValue();
        askOtherPassShortcutDropdown.SetValueWithoutNotify(ConfigManager.Instance.AskOtherPassShortcutMode);
        askOtherPassShortcutDropdown.RefreshShownValue();
        streamerModeDropdown.SetValueWithoutNotify(ConfigManager.Instance.StreamerModeEnabled ? 1 : 0);
        streamerModeDropdown.RefreshShownValue();
        handCutConfirmDropdown.SetValueWithoutNotify(ConfigManager.Instance.HandCutConfirmMode);
        handCutConfirmDropdown.RefreshShownValue();
        handSortSuitDropdown.SetValueWithoutNotify(ConfigManager.Instance.HandSortSuitOrderMode);
        handSortSuitDropdown.RefreshShownValue();
        handSortHonorDropdown.SetValueWithoutNotify(ConfigManager.Instance.HandSortHonorOrderMode);
        handSortHonorDropdown.RefreshShownValue();
        handSortDragonDropdown.SetValueWithoutNotify(ConfigManager.Instance.HandSortDragonOrderMode);
        handSortDragonDropdown.RefreshShownValue();
        handSortRiichiDragonDropdown.SetValueWithoutNotify(ConfigManager.Instance.HandSortRiichiDragonOrderMode);
        handSortRiichiDragonDropdown.RefreshShownValue();
        actionButtonColorDropdown.SetValueWithoutNotify(ConfigManager.Instance.ActionButtonColorEnabled ? 1 : 0);
        actionButtonColorDropdown.RefreshShownValue();
        openingAutoBuhuaDropdown.SetValueWithoutNotify(ConfigManager.Instance.OpeningAutoBuhuaEnabled ? 1 : 0);
        openingAutoBuhuaDropdown.RefreshShownValue();
        forcePassDropdown.SetValueWithoutNotify(ConfigManager.Instance.ForcePassEnabled ? 1 : 0);
        forcePassDropdown.RefreshShownValue();
        meldSpacingDropdown.SetValueWithoutNotify(ConfigManager.Instance.MeldSpacingEnabled ? 1 : 0);
        meldSpacingDropdown.RefreshShownValue();
        vsyncDropdown.SetValueWithoutNotify(ConfigManager.Instance.VsyncEnabled ? 1 : 0);
        vsyncDropdown.RefreshShownValue();
        gongHuSoundDropdown.SetValueWithoutNotify(ConfigManager.Instance.GongHuSoundEnabled ? 1 : 0);
        gongHuSoundDropdown.RefreshShownValue();
        matchSuccessSoundDropdown.SetValueWithoutNotify(ConfigManager.Instance.MatchSuccessSoundEnabled ? 1 : 0);
        matchSuccessSoundDropdown.RefreshShownValue();
        tileOutlinePresetDropdown.SetValueWithoutNotify(ConfigManager.Instance.TileOutlinePreset - 1);
        tileOutlinePresetDropdown.RefreshShownValue();
    }

    private void ApplyTargetFrameRateDropdownVisibility() {
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

    private void OnForcePassDropdownChanged(int value) {
        ConfigManager.Instance.SetForcePassEnabled(value == 1);
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
}
