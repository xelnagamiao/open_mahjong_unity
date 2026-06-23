using System;
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
            releaseVersion = 9; // 发行版号(验证客户端-服务器版本是否一致)
        } else {
            // 生产环境接口地址
            gameUrl = "wss://salasasa.cn/game";
            chatUrl = "wss://salasasa.cn/chat";
            releaseVersion = 9;
        }
        // 官方服务器链接网址 用于访问转到 （不影响游戏进程）
        clientVersion = "0.4.68.2"; // 仅存储 [大版本号.发行版号.开发版本.开发小版本号]
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
    private const string KEY_ASK_OTHER_PASS_SHORTCUT_ORDER_V2 = "AskOtherPassShortcutOrderV2";
    private const string KEY_TARGET_FRAME_RATE = "TargetFrameRate";
    private const string KEY_STREAMER_MODE = "StreamerMode";
    private const string KEY_HAND_CUT_CONFIRM = "HandCutConfirmMode";
    private const string KEY_HAND_SORT_SUIT_ORDER = "HandSortSuitOrderMode";
    private const string KEY_HAND_SORT_HONOR_ORDER = "HandSortHonorOrderMode";
    private const string KEY_HAND_SORT_DRAGON_ORDER = "HandSortDragonOrderMode";
    private const string KEY_HAND_SORT_RIICHI_DRAGON_ORDER = "HandSortRiichiDragonOrderMode";
    private const string KEY_LANGUAGE = "AppLanguage";

    private static AppLanguage _languageMode = AppLanguage.SimplifiedChinese;
    public static event Action OnLanguageChanged;
    public static bool IsEnglish => _languageMode == AppLanguage.English;
    public static AppLanguage CurrentLanguage => _languageMode;

    /// <summary>图集中空白/纯白牌面资源编号（与 2D CardFaceImage_xuefun 一致）。</summary>
    public const int BlankFaceImageId = 2;

    /// <summary>白板牌面：0 纯白（使用 BlankFaceImageId 图）1 回形（图集原图）</summary>
    public int WhiteDragonFaceMode { get; private set; }
    /// <summary>摸切快捷：0 双击 1 右键 2 无</summary>
    public int MoqieShortcutMode { get; private set; }
    /// <summary>鸣牌询问时过牌快捷：0 右键 1 双击 2 无</summary>
    public int AskOtherPassShortcutMode { get; private set; }
    /// <summary>目标帧率</summary>
    public int TargetFrameRate { get; private set; }
    /// <summary>主播模式：0 关 1 开</summary>
    public bool StreamerModeEnabled { get; private set; }
    /// <summary>两次点击确认出牌：0 关 1 开</summary>
    public int HandCutConfirmMode { get; private set; }
    public bool IsHandCutConfirmEnabled => HandCutConfirmMode == 1;
    /// <summary>自动理牌花色顺序：索引对应 TileIdOrder.SuitOrderOptions（0-5，0 万饼条为默认）</summary>
    public int HandSortSuitOrderMode { get; private set; }
    /// <summary>自动理牌字牌位置：0 最后(默认) 1 第三 2 第二 3 最前（索引对应 TileIdOrder.HonorOrderOptions）</summary>
    public int HandSortHonorOrderMode { get; private set; }
    /// <summary>三元牌排序：0 中发白(45→47→46，默认)，索引对应 TileIdOrder.DragonOrderOptions（非日麻对局使用）</summary>
    public int HandSortDragonOrderMode { get; private set; }
    /// <summary>日麻三元牌排序：2 白发中(46→47→45，默认)，索引对应 TileIdOrder.RiichiDragonOrderOptions（日麻对局使用）</summary>
    public int HandSortRiichiDragonOrderMode { get; private set; }

    /// <summary>与 RiichiTileUtil / 牌面资源一致：白板 id 为 46（47 为发）。</summary>
    public const int WhiteDragonTileId = 46;

    public int MasterVolume { get; private set; }
    public int MusicVolume { get; private set; }
    public int SoundEffectVolume { get; private set; }
    public int VoiceVolume { get; private set; }
    public AppLanguage LanguageMode => _languageMode;

    public static readonly int[] TargetFrameRateOptions = { 60, 90, 120, 180, 220, 300 };
    public static readonly string[] LanguageOptionLabels = { "简体中文", "繁体中文", "English" };

#if UNITY_WEBGL && !UNITY_EDITOR
    private const int DefaultTargetFrameRate = 60;
    private const int WebLockedFrameRate = 60;
    public static bool IsTargetFrameRateLocked => true;
#else
    private const int DefaultTargetFrameRate = 120;
    public static bool IsTargetFrameRateLocked => false;
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
    private const int DefaultHandCutConfirmMode = 1;
#else
    private const int DefaultHandCutConfirmMode = 0;
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
        AskOtherPassShortcutMode = LoadAskOtherPassShortcutMode();
        StreamerModeEnabled = PlayerPrefs.GetInt(KEY_STREAMER_MODE, 0) == 1;
        HandCutConfirmMode = PlayerPrefs.GetInt(KEY_HAND_CUT_CONFIRM, DefaultHandCutConfirmMode);
        HandSortSuitOrderMode = Mathf.Clamp(PlayerPrefs.GetInt(KEY_HAND_SORT_SUIT_ORDER, 0), 0, TileIdOrder.SuitOrderOptions.Length - 1);
        HandSortHonorOrderMode = Mathf.Clamp(PlayerPrefs.GetInt(KEY_HAND_SORT_HONOR_ORDER, 0), 0, TileIdOrder.HonorOrderOptions.Length - 1);
        HandSortDragonOrderMode = Mathf.Clamp(PlayerPrefs.GetInt(KEY_HAND_SORT_DRAGON_ORDER, 0), 0, TileIdOrder.DragonOrderOptions.Length - 1);
        HandSortRiichiDragonOrderMode = Mathf.Clamp(PlayerPrefs.GetInt(KEY_HAND_SORT_RIICHI_DRAGON_ORDER, 2), 0, TileIdOrder.RiichiDragonOrderOptions.Length - 1);
        _languageMode = (AppLanguage)Mathf.Clamp(PlayerPrefs.GetInt(KEY_LANGUAGE, (int)AppLanguage.SimplifiedChinese), 0, 2);
        TileIdOrder.SetSortRule(HandSortSuitOrderMode, HandSortHonorOrderMode, HandSortDragonOrderMode, HandSortRiichiDragonOrderMode);
#if UNITY_WEBGL && !UNITY_EDITOR
        TargetFrameRate = WebLockedFrameRate;
#else
        TargetFrameRate = NormalizeTargetFrameRate(PlayerPrefs.GetInt(KEY_TARGET_FRAME_RATE, DefaultTargetFrameRate));
#endif

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
        MoqieShortcutMode = Mathf.Clamp(mode, 0, 2);
        PlayerPrefs.SetInt(KEY_MOQIE_SHORTCUT, MoqieShortcutMode);
        PlayerPrefs.Save();
    }

    public void SetAskOtherPassShortcutMode(int mode) {
        AskOtherPassShortcutMode = Mathf.Clamp(mode, 0, 2);
        PlayerPrefs.SetInt(KEY_ASK_OTHER_PASS_SHORTCUT, AskOtherPassShortcutMode);
        PlayerPrefs.SetInt(KEY_ASK_OTHER_PASS_SHORTCUT_ORDER_V2, 1);
        PlayerPrefs.Save();
    }

    private static int LoadAskOtherPassShortcutMode() {
        int mode = PlayerPrefs.GetInt(KEY_ASK_OTHER_PASS_SHORTCUT, 0);
        if (PlayerPrefs.GetInt(KEY_ASK_OTHER_PASS_SHORTCUT_ORDER_V2, 0) == 0) {
            // 旧顺序：0 右键 1 无 2 双击 → 新顺序：0 右键 1 双击 2 无
            if (mode == 1) mode = 2;
            else if (mode == 2) mode = 1;
            PlayerPrefs.SetInt(KEY_ASK_OTHER_PASS_SHORTCUT, mode);
            PlayerPrefs.SetInt(KEY_ASK_OTHER_PASS_SHORTCUT_ORDER_V2, 1);
            PlayerPrefs.Save();
        }
        return Mathf.Clamp(mode, 0, 2);
    }

    public void SetStreamerModeEnabled(bool enabled) {
        StreamerModeEnabled = enabled;
        PlayerPrefs.SetInt(KEY_STREAMER_MODE, enabled ? 1 : 0);
        PlayerPrefs.Save();
        StreamerModeHelper.NotifyChanged();
    }

    public void SetHandCutConfirmMode(int mode) {
        HandCutConfirmMode = Mathf.Clamp(mode, 0, 1);
        PlayerPrefs.SetInt(KEY_HAND_CUT_CONFIRM, HandCutConfirmMode);
        PlayerPrefs.Save();
        if (HandCardSelectionController.Instance != null) {
            HandCardSelectionController.Instance.DisarmAll();
        }
    }

    public void SetHandSortSuitOrderMode(int mode) {
        HandSortSuitOrderMode = Mathf.Clamp(mode, 0, TileIdOrder.SuitOrderOptions.Length - 1);
        PlayerPrefs.SetInt(KEY_HAND_SORT_SUIT_ORDER, HandSortSuitOrderMode);
        PlayerPrefs.Save();
        ApplyHandSortRule();
    }

    public void SetHandSortHonorOrderMode(int mode) {
        HandSortHonorOrderMode = Mathf.Clamp(mode, 0, TileIdOrder.HonorOrderOptions.Length - 1);
        PlayerPrefs.SetInt(KEY_HAND_SORT_HONOR_ORDER, HandSortHonorOrderMode);
        PlayerPrefs.Save();
        ApplyHandSortRule();
    }

    public void SetHandSortDragonOrderMode(int mode) {
        HandSortDragonOrderMode = Mathf.Clamp(mode, 0, TileIdOrder.DragonOrderOptions.Length - 1);
        PlayerPrefs.SetInt(KEY_HAND_SORT_DRAGON_ORDER, HandSortDragonOrderMode);
        PlayerPrefs.Save();
        ApplyHandSortRule();
    }

    public void SetHandSortRiichiDragonOrderMode(int mode) {
        HandSortRiichiDragonOrderMode = Mathf.Clamp(mode, 0, TileIdOrder.RiichiDragonOrderOptions.Length - 1);
        PlayerPrefs.SetInt(KEY_HAND_SORT_RIICHI_DRAGON_ORDER, HandSortRiichiDragonOrderMode);
        PlayerPrefs.Save();
        ApplyHandSortRule();
    }

    public void SetLanguageMode(int mode) {
        var language = (AppLanguage)Mathf.Clamp(mode, 0, 2);
        if (_languageMode == language) {
            return;
        }
        _languageMode = language;
        PlayerPrefs.SetInt(KEY_LANGUAGE, (int)language);
        PlayerPrefs.Save();
        OnLanguageChanged?.Invoke();
    }

    // 应用排序规则到 TileIdOrder，并在对局中开启自动理牌时立即按新规则重排当前手牌。
    private void ApplyHandSortRule() {
        TileIdOrder.SetSortRule(HandSortSuitOrderMode, HandSortHonorOrderMode, HandSortDragonOrderMode, HandSortRiichiDragonOrderMode);
        if (GameCanvas.Instance != null && AutoAction.Instance != null && AutoAction.Instance.IsAutoArrangeHandCards) {
            GameCanvas.Instance.SortMainHandByTileIdIfNeeded();
        }
    }

    public void SetTargetFrameRate(int frameRate) {
#if UNITY_WEBGL && !UNITY_EDITOR
        return;
#else
        TargetFrameRate = NormalizeTargetFrameRate(frameRate);
        PlayerPrefs.SetInt(KEY_TARGET_FRAME_RATE, TargetFrameRate);
        PlayerPrefs.Save();
        ApplyTargetFrameRate();
#endif
    }

    private void ApplyTargetFrameRate() {
#if UNITY_WEBGL && !UNITY_EDITOR
        Application.targetFrameRate = WebLockedFrameRate;
#else
        Application.targetFrameRate = TargetFrameRate;
#endif
    }

    private static int NormalizeTargetFrameRate(int frameRate) {
        foreach (int option in TargetFrameRateOptions) {
            if (frameRate == option) return frameRate;
        }
        return DefaultTargetFrameRate;
    }
}
