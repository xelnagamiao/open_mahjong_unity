using System.Collections;
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

    [Header("对局显示与操作")]
    [SerializeField] private TMP_Dropdown whiteDragonFaceDropdown;
    [SerializeField] private TMP_Dropdown moqieShortcutDropdown;
    [SerializeField] private TMP_Dropdown askOtherPassShortcutDropdown;
    [SerializeField] private TMP_Dropdown targetFrameRateDropdown;

    private void Awake() {
        Instance = this;
        InitializeVolumeManager();
        InitializeGameplayDropdownOptions();
        whiteDragonFaceDropdown.onValueChanged.AddListener(OnWhiteDragonFaceDropdownChanged);
        moqieShortcutDropdown.onValueChanged.AddListener(OnMoqieShortcutDropdownChanged);
        askOtherPassShortcutDropdown.onValueChanged.AddListener(OnAskOtherPassShortcutDropdownChanged);
        targetFrameRateDropdown.onValueChanged.AddListener(OnTargetFrameRateDropdownChanged);
    }

    private void OnEnable() {
        SyncGameplayDropdownsFromConfig();
    }

    private void InitializeVolumeManager() {
        masterVolumeSlider.Init();
        musicVolumeSlider.Init();
        soundEffectVolumeSlider.Init();
        voiceVolumeSlider.Init();
    }

    private void InitializeGameplayDropdownOptions() {
        whiteDragonFaceDropdown.ClearOptions();
        whiteDragonFaceDropdown.AddOptions(new List<string> { "纯白", "回形" });
        moqieShortcutDropdown.ClearOptions();
        moqieShortcutDropdown.AddOptions(new List<string> { "双击摸切", "右键摸切" });
        askOtherPassShortcutDropdown.ClearOptions();
        askOtherPassShortcutDropdown.AddOptions(new List<string> { "右键取消", "无快捷键", "双击取消" });
        targetFrameRateDropdown.ClearOptions();
        List<string> frameRateOptions = new List<string>();
        foreach (int frameRate in ConfigManager.TargetFrameRateOptions) {
            frameRateOptions.Add(frameRate.ToString());
        }
        targetFrameRateDropdown.AddOptions(frameRateOptions);
    }

    private void SyncGameplayDropdownsFromConfig() {
        whiteDragonFaceDropdown.SetValueWithoutNotify(ConfigManager.Instance.WhiteDragonFaceMode);
        whiteDragonFaceDropdown.RefreshShownValue();
        moqieShortcutDropdown.SetValueWithoutNotify(ConfigManager.Instance.MoqieShortcutMode);
        moqieShortcutDropdown.RefreshShownValue();
        askOtherPassShortcutDropdown.SetValueWithoutNotify(ConfigManager.Instance.AskOtherPassShortcutMode);
        askOtherPassShortcutDropdown.RefreshShownValue();
        int frameRateIndex = System.Array.IndexOf(ConfigManager.TargetFrameRateOptions, ConfigManager.Instance.TargetFrameRate);
        targetFrameRateDropdown.SetValueWithoutNotify(frameRateIndex >= 0 ? frameRateIndex : 0);
        targetFrameRateDropdown.RefreshShownValue();
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

    private void OnTargetFrameRateDropdownChanged(int value) {
        ConfigManager.Instance.SetTargetFrameRate(ConfigManager.TargetFrameRateOptions[value]);
    }
}
