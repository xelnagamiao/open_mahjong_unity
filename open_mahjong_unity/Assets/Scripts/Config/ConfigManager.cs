using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;

public class ConfigManager : MonoBehaviour {
    public static ConfigManager Instance { get; private set; }

    public static bool Debug = true;
    
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
            releaseVersion = 4; // 发行版号(验证客户端-服务器版本是否一致)
        } else {
            // 生产环境接口地址
            gameUrl = "wss://salasasa.cn/game";
            chatUrl = "wss://salasasa.cn/chat";
            releaseVersion = 4;
        }
        // 官方服务器链接网址 用于访问转到 （不影响游戏进程）
        clientVersion = "0.4.60.4"; // 仅存储 [大版本号.发行版号.开发版本.开发小版本号]
        webUrl = "https://salasasa.cn"; // 访问转到
        documentUrl = "https://www.yuque.com/xelnaga-yjcgq/zkwfgr/lusmvid200iez36q?singleDoc#"; // 访问转到
        githubUrl = "https://github.com/xelnagamiao/open_mahjong_unity"; // 访问转到
    }

    // 头衔编号 => 头衔名称
    private static Dictionary<int, string> titleDictionary = new Dictionary<int, string>{
        { 1, "暂无头衔" },
        { 2, "hhmlb" }
    };

    private const string KEY_MASTER_VOLUME = "MasterVolume";
    private const string KEY_MUSIC_VOLUME = "MusicVolume";
    private const string KEY_SOUND_EFFECT_VOLUME = "SoundEffectVolume";
    private const string KEY_VOICE_VOLUME = "VoiceVolume";
    private const int DEFAULT_VOLUME = 100;

    private const string KEY_WHITE_DRAGON_FACE = "WhiteDragonFaceMode";
    private const string KEY_MOQIE_SHORTCUT = "MoqieShortcutMode";
    private const string KEY_ASK_OTHER_PASS_SHORTCUT = "AskOtherPassShortcutMode";
    private const string KEY_TARGET_FRAME_RATE = "TargetFrameRate";

    /// <summary>图集中空白/纯白牌面资源编号（与 2D CardFaceImage_xuefun 一致）。</summary>
    public const int BlankFaceImageId = 2;

    /// <summary>白板牌面：0 纯白（使用 BlankFaceImageId 图）1 回形（图集原图）</summary>
    public int WhiteDragonFaceMode { get; private set; }
    /// <summary>摸切快捷：0 双击 1 右键</summary>
    public int MoqieShortcutMode { get; private set; }
    /// <summary>鸣牌询问时过牌快捷：0 右键 1 无 2 双击</summary>
    public int AskOtherPassShortcutMode { get; private set; }
    /// <summary>目标帧率</summary>
    public int TargetFrameRate { get; private set; }

    /// <summary>与 RiichiTileUtil / 牌面资源一致：白板 id 为 46（47 为发）。</summary>
    public const int WhiteDragonTileId = 46;

    public int MasterVolume { get; private set; }
    public int MusicVolume { get; private set; }
    public int SoundEffectVolume { get; private set; }
    public int VoiceVolume { get; private set; }

    public static readonly int[] TargetFrameRateOptions = { 60, 90, 120, 180, 220, 300 };

#if UNITY_WEBGL
    private const int DefaultTargetFrameRate = 60;
#else
    private const int DefaultTargetFrameRate = 120;
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

        WhiteDragonFaceMode = PlayerPrefs.GetInt(KEY_WHITE_DRAGON_FACE, 1);
        MoqieShortcutMode = PlayerPrefs.GetInt(KEY_MOQIE_SHORTCUT, 0);
        AskOtherPassShortcutMode = PlayerPrefs.GetInt(KEY_ASK_OTHER_PASS_SHORTCUT, 0);
        TargetFrameRate = NormalizeTargetFrameRate(PlayerPrefs.GetInt(KEY_TARGET_FRAME_RATE, DefaultTargetFrameRate));
        
        QualitySettings.vSyncCount = 0;
        ApplyTargetFrameRate();
        Application.runInBackground = true;
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

    public float GetSoundEffectVolumeRatio() {
        return MasterVolume * SoundEffectVolume / 10000f;
    }

    public float GetVoiceVolumeRatio() {
        return MasterVolume * VoiceVolume / 10000f;
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

    public bool UseBlankWhiteDragonFace(int tileId) {
        return WhiteDragonFaceMode == 0 && tileId == WhiteDragonTileId;
    }

    public void SetWhiteDragonFaceMode(int mode) {
        WhiteDragonFaceMode = Mathf.Clamp(mode, 0, 1);
        PlayerPrefs.SetInt(KEY_WHITE_DRAGON_FACE, WhiteDragonFaceMode);
        PlayerPrefs.Save();
    }

    public void SetMoqieShortcutMode(int mode) {
        MoqieShortcutMode = Mathf.Clamp(mode, 0, 1);
        PlayerPrefs.SetInt(KEY_MOQIE_SHORTCUT, MoqieShortcutMode);
        PlayerPrefs.Save();
    }

    public void SetAskOtherPassShortcutMode(int mode) {
        AskOtherPassShortcutMode = Mathf.Clamp(mode, 0, 2);
        PlayerPrefs.SetInt(KEY_ASK_OTHER_PASS_SHORTCUT, AskOtherPassShortcutMode);
        PlayerPrefs.Save();
    }

    public void SetTargetFrameRate(int frameRate) {
        TargetFrameRate = NormalizeTargetFrameRate(frameRate);
        PlayerPrefs.SetInt(KEY_TARGET_FRAME_RATE, TargetFrameRate);
        PlayerPrefs.Save();
        ApplyTargetFrameRate();
    }

    private void ApplyTargetFrameRate() {
        Application.targetFrameRate = TargetFrameRate;
    }

    private static int NormalizeTargetFrameRate(int frameRate) {
        foreach (int option in TargetFrameRateOptions) {
            if (frameRate == option) return frameRate;
        }
        return DefaultTargetFrameRate;
    }
}
