using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;

public class ConfigManager : MonoBehaviour {
    public static ConfigManager Instance { get; private set; }

    public static bool Debug = false;
    
    public static string webUrl;
    public static string gameUrl;
    public static string chatUrl;
    public static string clientVersion;
    public static int releaseVersion;
    public static string githubUrl;
    public static string documentUrl;

    static ConfigManager() {
        if (Debug) {
            // 开发接口地址
            gameUrl = "ws://localhost:8081/game"; // 游戏服务器地址(连接到OMU服务器)
            chatUrl = "ws://localhost:8083/chat"; // 聊天服务器地址(连接到OMUChat服务器)
            releaseVersion = 2; // 发行版号(验证客户端-服务器版本是否一致)
        } else {
            // 生产环境接口地址
            gameUrl = "wss://salasasa.cn/game";
            chatUrl = "wss://salasasa.cn/chat";
            releaseVersion = 2;
        }
        // 官方服务器链接网址 用于访问转到 （不影响游戏进程）
        clientVersion = "0.2.40.1"; // 仅存储 [大版本号.发行版号.开发版本.开发小版本号]
        webUrl = "https://salasasa.cn"; // 访问转到
        documentUrl = "https://www.yuque.com/xelnaga-yjcgq/zkwfgr/lusmvid200iez36q?singleDoc#"; // 访问转到
        githubUrl = "https://github.com/xelnagamiao/open_mahjong_unity"; // 访问转到
    }

    // 头衔编号 => 头衔名称
    private static Dictionary<int, string> titleDictionary = new Dictionary<int, string>{
        { 1, "暂无头衔" }
    };

    private const string KEY_MASTER_VOLUME = "MasterVolume";
    private const string KEY_MUSIC_VOLUME = "MusicVolume";
    private const string KEY_SOUND_EFFECT_VOLUME = "SoundEffectVolume";
    private const string KEY_VOICE_VOLUME = "VoiceVolume";
    private const int DEFAULT_VOLUME = 100;

    public int MasterVolume { get; private set; }
    public int MusicVolume { get; private set; }
    public int SoundEffectVolume { get; private set; }
    public int VoiceVolume { get; private set; }

    private const int ForegroundFrameRate = 60;
    private const int BackgroundFrameRate = 10;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void PageVisibility_Setup();
#endif

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // 供 WebGL 的 JS 插件 SendMessage 定位用
        gameObject.name = "GlobalConfig";
        
        // 加载用户配置
        MasterVolume = PlayerPrefs.GetInt(KEY_MASTER_VOLUME, DEFAULT_VOLUME);
        MusicVolume = PlayerPrefs.GetInt(KEY_MUSIC_VOLUME, DEFAULT_VOLUME);
        SoundEffectVolume = PlayerPrefs.GetInt(KEY_SOUND_EFFECT_VOLUME, DEFAULT_VOLUME);
        VoiceVolume = PlayerPrefs.GetInt(KEY_VOICE_VOLUME, DEFAULT_VOLUME);
        
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = ForegroundFrameRate;

#if UNITY_WEBGL && !UNITY_EDITOR
        PageVisibility_Setup();
#endif
    }

    // WebGL: 由 Assets/Plugins/PageVisibility.jslib 调用
    public void OnApplicationVisibilityChanged(int isVisible) {
        Application.targetFrameRate = (isVisible == 1) ? ForegroundFrameRate : BackgroundFrameRate;
    }

    public void SetUserConfig(int volume) {
        MasterVolume = Mathf.Clamp(volume, 0, 100);
    }

    public void SetMasterVolume(int volume) {
        MasterVolume = Mathf.Clamp(volume, 0, 100);
        PlayerPrefs.SetInt(KEY_MASTER_VOLUME, MasterVolume);
        PlayerPrefs.Save();
    }

    public void SetMusicVolume(int volume) {
        MusicVolume = Mathf.Clamp(volume, 0, 100);
        PlayerPrefs.SetInt(KEY_MUSIC_VOLUME, MusicVolume);
        PlayerPrefs.Save();
    }

    public void SetSoundEffectVolume(int volume) {
        SoundEffectVolume = Mathf.Clamp(volume, 0, 100);
        PlayerPrefs.SetInt(KEY_SOUND_EFFECT_VOLUME, SoundEffectVolume);
        PlayerPrefs.Save();
    }

    public void SetVoiceVolume(int volume) {
        VoiceVolume = Mathf.Clamp(volume, 0, 100);
        PlayerPrefs.SetInt(KEY_VOICE_VOLUME, VoiceVolume);
        PlayerPrefs.Save();
    }

    // 保存桌布选择
    public void SetSelectedTableCloth(string path, bool isCustom) {
        PlayerPrefs.SetString("SelectedTableClothPath", path);
        PlayerPrefs.SetInt("SelectedTableClothIsCustom", isCustom ? 1 : 0);
        PlayerPrefs.Save();
    }

    // 保存桌边选择
    public void SetSelectedTableEdge(string path, bool isCustom) {
        PlayerPrefs.SetString("SelectedTableEdgePath", path);
        PlayerPrefs.SetInt("SelectedTableEdgeIsCustom", isCustom ? 1 : 0);
        PlayerPrefs.Save();
    }

    // 获取桌布选择
    public (string path, bool isCustom) GetSelectedTableCloth() {
        string path = PlayerPrefs.GetString("SelectedTableClothPath", "");
        bool isCustom = PlayerPrefs.GetInt("SelectedTableClothIsCustom", 0) == 1;
        return (path, isCustom);
    }

    // 获取桌边选择
    public (string path, bool isCustom) GetSelectedTableEdge() {
        string path = PlayerPrefs.GetString("SelectedTableEdgePath", "");
        bool isCustom = PlayerPrefs.GetInt("SelectedTableEdgeIsCustom", 0) == 1;
        return (path, isCustom);
    }

    public static string GetTitleText(int titleId) {
        return titleDictionary.ContainsKey(titleId) ? titleDictionary[titleId] : titleDictionary[1];
    }
}
