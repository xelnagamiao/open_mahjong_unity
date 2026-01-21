using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;

public class AppConfigPanel : MonoBehaviour {
    public static AppConfigPanel Instance;

    [Header("音量设置")]
    [SerializeField] private ConfigSlider masterVolumeSlider;
    [SerializeField] private ConfigSlider musicVolumeSlider;
    [SerializeField] private ConfigSlider soundEffectVolumeSlider;
    [SerializeField] private ConfigSlider voiceVolumeSlider;

    private void Awake() {
        Instance = this;
        InitializeVolumeManager();
    }

    private void InitializeVolumeManager(){
        masterVolumeSlider.Init();
        musicVolumeSlider.Init();
        soundEffectVolumeSlider.Init();
        voiceVolumeSlider.Init();
    }


}
