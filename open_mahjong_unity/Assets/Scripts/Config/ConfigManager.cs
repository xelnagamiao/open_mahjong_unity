using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class ConfigManager : MonoBehaviour {
    public static ConfigManager Instance { get; private set; }

    public static bool Debug = false;

    /// <summary>Steam 构建开关：为 true 时，场景中挂载 SteamBuildHider 的物体列表会被隐藏。</summary>
    public static bool BuildForSteam = true;

    public static string webUrl;
    public static string webApiUrl;
    public static string gameUrl;
    public static string chatUrl;
    public static string clientVersion;
    public static int releaseVersion;
    public static string githubUrl;
    public static string documentUrl;
    public static string mobileDownloadUrl;

    static ConfigManager() {
        if (Debug) {
            // 开发接口地址
            gameUrl = "ws://localhost:8081/game"; // 游戏服务器地址(连接到OMU服务器)
            chatUrl = "ws://localhost:8083/chat"; // 聊天服务器地址(连接到OMUChat服务器)
            webApiUrl = "http://localhost:3000"; // 活动专栏 / 平台 HTTP（通知、牌谱公开接口）
            releaseVersion = 22; // 发行版号(验证客户端-服务器版本是否一致)
        } else {
            // 生产环境接口地址
            gameUrl = "wss://salasasa.cn/game";
            chatUrl = "wss://salasasa.cn/chat";
            webApiUrl = "https://salasasa.cn";
            releaseVersion = 22;
        }
        // 官方服务器链接网址 用于访问转到 （不影响游戏进程）
        clientVersion = "0.4.75.28"; // 仅存储 [大版本号.发行版号.开发版本.开发小版本号]
        webUrl = "https://salasasa.cn"; // 访问转到
        mobileDownloadUrl = "https://salasasa.cn/mobile-download"; // Android APK 版本更新下载页
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
    private const string KEY_VSYNC_ENABLED = "VsyncEnabled";
    private const string KEY_STREAMER_MODE = "StreamerMode";
    private const string KEY_HAND_CUT_CONFIRM = "HandCutConfirmMode";
    private const string KEY_HAND_SORT_SUIT_ORDER = "HandSortSuitOrderMode";
    private const string KEY_HAND_SORT_HONOR_ORDER = "HandSortHonorOrderMode";
    private const string KEY_HAND_SORT_DRAGON_ORDER = "HandSortDragonOrderMode";
    private const string KEY_HAND_SORT_RIICHI_DRAGON_ORDER = "HandSortRiichiDragonOrderMode";
    private const string KEY_LANGUAGE = "AppLanguage";
    private const string KEY_ACTION_BUTTON_COLOR_ENABLED = "ActionButtonColorEnabled";
    private const string KEY_GONG_HU_SOUND_ENABLED = "GongHuSoundEnabled";
    private const string KEY_MATCH_SUCCESS_SOUND_ENABLED = "MatchSuccessSoundEnabled";
    private const string KEY_OPENING_AUTO_BUHUA_ENABLED = "OpeningAutoBuhuaEnabled";
    private const string KEY_FORCE_PASS_ENABLED = "ForcePassEnabled";
    private const string KEY_MELD_SPACING_ENABLED = "MeldSpacingEnabled";
    private const string KEY_TILE_OUTLINE_PRESET = "TileOutlinePreset";
    private const string KEY_CARD_BACK_COLOR = "CardBackColor";
    private const string KEY_CARD_BACK_IMAGE_PATH = "CardBackImagePath";
    private const string KEY_CARD_BACK_IMAGE_IS_CUSTOM = "CardBackImageIsCustom";
    private const string KEY_SIDE_COLOR = "SideColor";
    private const string KEY_BACK_EDGE_COLOR = "BackEdgeColor";
    private const string KEY_BACK_EDGE_SYNC = "BackEdgeSync";
    private const string KEY_BACK_EDGE_MODE = "BackEdgeMode";
    private const string KEY_CUSTOM_STANDARD_TILE_PACK = "CustomStandardTilePack";
    private const string KEY_STANDARD_TILE_PACK_ID = "StandardTilePackId";
    private const string KEY_CUSTOM_TILE_PACK_FILE_NAME = "CustomTilePackFileName";
    private const string KEY_HAND_BG_PATH = "HandBgImagePath";
    private const string KEY_HAND_BG_IS_CUSTOM = "HandBgImageIsCustom";
    private const string KEY_HAND_BACK_PATH = "HandBackImagePath";
    private const string KEY_HAND_BACK_IS_CUSTOM = "HandBackImageIsCustom";
    private const string KEY_USE_HAND_FACE_BACKGROUND = "UseHandFaceBackground";
    private const string KEY_TABLE_BG_PATH = "TableBgImagePath";
    private const string KEY_TABLE_BG_IS_CUSTOM = "TableBgImageIsCustom";
    private const string KEY_USE_TABLE_FACE_BACKGROUND = "UseTableFaceBackground";
    private const string KEY_FRONT_TEX_EXTEND_EDGE = "FrontTexExtendEdge";
    private const string KEY_FRONT_EDGE_COLOR = "FrontEdgeColor";
    private const string KEY_FRONT_EDGE_SYNC = "FrontEdgeSync";
    private const string KEY_FRONT_EDGE_MODE = "FrontEdgeMode";
    private const string KEY_FRONT_TEX_FOLLOW_TABLE_BG = "FrontTexFollowTableBg";
    private const string KEY_TABLE_FACE_COLOR = "TableFaceColor";
    private const string KEY_TABLE_FACE_USE_SOLID = "TableFaceUseSolidColor";
    private const string KEY_FRONT_TEX_FOLLOW_TABLE_BG_TO_EDGE = "FrontTexFollowTableBgToEdge";

    /// <summary>3D card back default color (same as 3DTile.mat _BackColor).</summary>
    public static readonly Color DefaultCardBackColor = new Color(0.218f, 0.372f, 0.66f, 1f);
    /// <summary>正面侧边默认颜色（与 3DTile.mat _SideColor 一致，浅灰）。</summary>
    public static readonly Color DefaultSideColor = new Color(0.7132075f, 0.7132075f, 0.7132075f, 1f);
    /// <summary>背面侧边默认颜色：默认与牌背颜色同步（跟随 DefaultCardBackColor）。</summary>
    public static readonly Color DefaultBackEdgeColor = DefaultCardBackColor;
    /// <summary>3D 牌面纯色默认：与牌面兜底色相同（245, 246, 247）。</summary>
    public static readonly Color DefaultTableFaceFallbackColor = new Color(0.961f, 0.965f, 0.969f, 1f);
    /// <summary>3D 牌面纯色默认，与 <see cref="DefaultTableFaceFallbackColor"/> 一致。</summary>
    public static readonly Color DefaultTableFaceColor = DefaultTableFaceFallbackColor;

    private static AppLanguage _languageMode = AppLanguage.SimplifiedChinese;
    public static event Action OnLanguageChanged;
    public static bool IsEnglish => _languageMode == AppLanguage.English;
    public static AppLanguage CurrentLanguage => _languageMode;

    /// <summary>图集中空白/纯白牌面资源编号（与 2D CardFaceImage_xuefun 一致）。</summary>
    public const int BlankFaceImageId = 2;
    /// <summary>2D 手牌暗面（里宝未翻开等），对应图集 id 0。</summary>
    public const int HandBackImageId = 0;

    /// <summary>白板牌面：0 纯白（使用 BlankFaceImageId 图）1 回形（图集原图）</summary>
    public int WhiteDragonFaceMode { get; private set; }
    /// <summary>摸切快捷：0 双击 1 右键 2 无</summary>
    public int MoqieShortcutMode { get; private set; }
    /// <summary>鸣牌询问时过牌快捷：0 右键 1 双击 2 无</summary>
    public int AskOtherPassShortcutMode { get; private set; }
    /// <summary>目标帧率</summary>
    public int TargetFrameRate { get; private set; }
    public bool VsyncEnabled { get; private set; }
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
    /// <summary>操作按钮分色：关时全部使用 GameCanvas 的 fallback 配色</summary>
    public bool ActionButtonColorEnabled { get; private set; }
    /// <summary>高番敲锣音效：默认开启</summary>
    public bool GongHuSoundEnabled { get; private set; }
    /// <summary>匹配成功音效：默认开启</summary>
    public bool MatchSuccessSoundEnabled { get; private set; }
    public bool OpeningAutoBuhuaEnabled { get; private set; }
    /// <summary>国标战术鸣牌显示「放弃」：默认关，打开后认领 force_pass。</summary>
    public bool ForcePassEnabled { get; private set; }
    /// <summary>副露间距：0 关（默认） 1 开</summary>
    public bool MeldSpacingEnabled { get; private set; }
    /// <summary>3D 牌描边预设：1=标准纯黑(2/2)，2=粗深黑(3/3)，默认 1</summary>
    public int TileOutlinePreset { get; private set; }
    /// <summary>3D card back color (default deep blue).</summary>
    public Color CardBackColor { get; private set; } = DefaultCardBackColor;
    /// <summary>3D 牌正面侧边颜色（浅灰）。</summary>
    public Color SideColor { get; private set; } = DefaultSideColor;
    /// <summary>3D 牌背面侧边颜色：BackEdgeSyncEnabled 开启时跟随牌背颜色。</summary>
    public Color BackEdgeColor { get; private set; } = DefaultBackEdgeColor;
    /// <summary>背面侧边颜色是否与牌背颜色同步（默认开启）。</summary>
    public bool BackEdgeSyncEnabled { get; private set; } = true;
    /// <summary>背面边缘颜色模式：独立 / 跟随牌背 / 跟随正面边缘。</summary>
    public CardEdgePanel.BackEdgeMode BackEdgeMode { get; private set; } = CardEdgePanel.BackEdgeMode.FollowBack;
    /// <summary>标准麻将牌面套装：official / fluffy / hkmahjong / custom。虹雀始终用官方图。</summary>
    public string StandardTilePackId { get; private set; } = TilePackIds.PackOfficial;
    /// <summary>最近一次上传的自定义牌面 zip 文件名（不含路径）。新上传覆盖。</summary>
    public string CustomTilePackFileName { get; private set; } = "";
    /// <summary>是否使用非官方标准牌面（分层预装或自定义 zip）。</summary>
    public bool CustomStandardTilePackEnabled => TilePackIds.IsLayeredPack(StandardTilePackId);
    /// <summary>2D 手牌是否在花纹下叠手牌牌面背景。官方整图默认关；透明花纹套装默认开。</summary>
    public bool UseHandFaceBackground { get; private set; }
    /// <summary>3D 牌正面是否在花纹下叠 3D 牌面背景。独立开关，默认关（保持现有贴图行为）。</summary>
    public bool UseTableFaceBackground { get; private set; }
    /// <summary>3D 牌正面侧边颜色（默认跟随 _FrontColor=白）。</summary>
    public Color FrontEdgeColor { get; private set; } = Color.white;
    /// <summary>3D 牌正面侧边颜色是否与牌面背景同步（默认关）。</summary>
    public bool FrontEdgeSyncEnabled { get; private set; }
    /// <summary>3D 牌正面侧边模式：独立颜色 / 拉伸牌面到侧面 / 跟随背面独立边缘色。</summary>
    public CardEdgePanel.FrontEdgeMode FrontEdgeMode { get; private set; } = CardEdgePanel.FrontEdgeMode.Independent;
    /// <summary>3D 牌面纯色：与「3D 牌面背景」互斥。开启时牌面渲染该纯色，关闭时按 _FrontTex/_FrontBgTex 行为渲染。</summary>
    public Color TableFaceColor { get; private set; } = DefaultTableFaceColor;
    /// <summary>是否使用 3D 牌面纯色（开启后「使用 3D 牌面背景」自动关闭）。</summary>
    public bool TableFaceUseSolidColor { get; private set; }

    public static readonly string[] TileOutlinePresetLabels = {
        "预设1",
        "预设2",
    };
    /// <summary>描边预设：细(低性能消耗) / 粗(高性能消耗) </summary>

    /// <summary>与 RiichiTileUtil / 牌面资源一致：白板 id 为 46（47 为发）。</summary>
    public const int WhiteDragonTileId = 46;

    public int MasterVolume { get; private set; }
    public int MusicVolume { get; private set; }
    public int SoundEffectVolume { get; private set; }
    public int VoiceVolume { get; private set; }
    public AppLanguage LanguageMode => _languageMode;

    public static readonly string[] LanguageOptionLabels = { "简体中文", "繁体中文", "English" };

    // 帧率全平台统一 60：网页由浏览器 vsync 决定，桌面/安卓由 vSyncCount + targetFrameRate 共同约束。
    private const int LockedFrameRate = 60;
    public static bool IsTargetFrameRateLocked => true;

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
        ActionButtonColorEnabled = PlayerPrefs.GetInt(KEY_ACTION_BUTTON_COLOR_ENABLED, 0) == 1;
        GongHuSoundEnabled = PlayerPrefs.GetInt(KEY_GONG_HU_SOUND_ENABLED, 1) == 1;
        MatchSuccessSoundEnabled = PlayerPrefs.GetInt(KEY_MATCH_SUCCESS_SOUND_ENABLED, 1) == 1;
        OpeningAutoBuhuaEnabled = PlayerPrefs.GetInt(KEY_OPENING_AUTO_BUHUA_ENABLED, 1) == 1;
        ForcePassEnabled = PlayerPrefs.GetInt(KEY_FORCE_PASS_ENABLED, 0) == 1;
        MeldSpacingEnabled = PlayerPrefs.GetInt(KEY_MELD_SPACING_ENABLED, 0) == 1;
        TileOutlinePreset = Mathf.Clamp(PlayerPrefs.GetInt(KEY_TILE_OUTLINE_PRESET, 1), 1, 2);
        CardBackColor = LoadCardBackColor();
        SideColor = LoadSideColor();
        BackEdgeColor = LoadBackEdgeColor();
        BackEdgeSyncEnabled = PlayerPrefs.GetInt(KEY_BACK_EDGE_SYNC, 1) == 1;
        BackEdgeMode = (CardEdgePanel.BackEdgeMode)Mathf.Clamp(
            PlayerPrefs.GetInt(KEY_BACK_EDGE_MODE, BackEdgeSyncEnabled ? 1 : 0), 0, 2);
        FrontEdgeColor = LoadFrontEdgeColor();
        FrontEdgeSyncEnabled = PlayerPrefs.GetInt(KEY_FRONT_EDGE_SYNC, 0) == 1;
        FrontEdgeMode = (CardEdgePanel.FrontEdgeMode)Mathf.Clamp(
            PlayerPrefs.GetInt(KEY_FRONT_EDGE_MODE, FrontEdgeSyncEnabled ? 1 : 0), 0, 2);
        MigrateLegacyFrontTexFollowFlags();
        TableFaceColor = LoadTableFaceColor();
        TableFaceUseSolidColor = PlayerPrefs.GetInt(KEY_TABLE_FACE_USE_SOLID, 0) == 1;
        UseTableFaceBackground = LoadUseTableFaceBackground();
        StandardTilePackId = LoadStandardTilePackId();
        CustomTilePackFileName = PlayerPrefs.GetString(KEY_CUSTOM_TILE_PACK_FILE_NAME, "");
        UseHandFaceBackground = LoadUseHandFaceBackground(StandardTilePackId);
        TileIdOrder.SetSortRule(HandSortSuitOrderMode, HandSortHonorOrderMode, HandSortDragonOrderMode, HandSortRiichiDragonOrderMode);
        VsyncEnabled = PlayerPrefs.GetInt(KEY_VSYNC_ENABLED, 1) == 1;
        TargetFrameRate = LockedFrameRate;

        ApplyVsync();
        ApplyTargetFrameRate();
        Application.runInBackground = true;
        ApplyAntialiasingByPlatform();
    }

    private void Start() {
        ApplyCameraAntialiasingByPlatform();
        ApplyTileOutlinePreset();
        UnityAssetIdb.EnsureReady(() => {
            TileFaceResolver.EnsureLoaded();
            if (Desktop.Instance != null) {
                Desktop.Instance.RefreshTablecloth();
                Desktop.Instance.RefreshEdge();
            }
            CardBackManager.ApplySavedConfig();
            if (CardBackConfigPanel.Instance != null) {
                CardBackConfigPanel.Instance.ReloadSaved();
            }
        });
    }

    public void SetCustomStandardTilePackEnabled(bool enabled) {
        SetStandardTilePackId(enabled ? TilePackIds.PackCustom : TilePackIds.PackOfficial);
    }

    public void SetStandardTilePackId(string packId) {
        StandardTilePackId = TilePackIds.NormalizePackId(packId);
        PlayerPrefs.SetString(KEY_STANDARD_TILE_PACK_ID, StandardTilePackId);
        PlayerPrefs.SetInt(KEY_CUSTOM_STANDARD_TILE_PACK, CustomStandardTilePackEnabled ? 1 : 0);
        SetUseHandFaceBackground(TilePackIds.DefaultUseHandFaceBackground(StandardTilePackId));
    }

    public void SetCustomTilePackFileName(string fileName) {
        CustomTilePackFileName = string.IsNullOrEmpty(fileName)
            ? ""
            : System.IO.Path.GetFileName(fileName);
        PlayerPrefs.SetString(KEY_CUSTOM_TILE_PACK_FILE_NAME, CustomTilePackFileName);
        PlayerPrefs.Save();
    }

    public void SetUseHandFaceBackground(bool enabled) {
        UseHandFaceBackground = enabled;
        PlayerPrefs.SetInt(KEY_USE_HAND_FACE_BACKGROUND, enabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void SetUseTableFaceBackground(bool enabled) {
        UseTableFaceBackground = enabled;
        PlayerPrefs.SetInt(KEY_USE_TABLE_FACE_BACKGROUND, enabled ? 1 : 0);
        // 与「使用 3D 牌面纯色」互斥
        if (enabled) {
            TableFaceUseSolidColor = false;
            PlayerPrefs.SetInt(KEY_TABLE_FACE_USE_SOLID, 0);
        }
        PlayerPrefs.Save();
    }

    private static bool LoadUseTableFaceBackground() {
        if (!PlayerPrefs.HasKey(KEY_USE_TABLE_FACE_BACKGROUND)) return false;
        return PlayerPrefs.GetInt(KEY_USE_TABLE_FACE_BACKGROUND, 0) == 1;
    }

    /// <summary>
    /// 旧版「跟随 3D 牌面背景 / 拉伸到边缘」独立开关已并入 FrontEdgeMode.FollowTableBg。
    /// </summary>
    private void MigrateLegacyFrontTexFollowFlags()
    {
        bool legacyStretch = PlayerPrefs.GetInt(KEY_FRONT_TEX_FOLLOW_TABLE_BG, 0) == 1
            || PlayerPrefs.GetInt(KEY_FRONT_TEX_FOLLOW_TABLE_BG_TO_EDGE, 0) == 1
            || PlayerPrefs.GetInt(KEY_FRONT_TEX_EXTEND_EDGE, 0) == 1;
        if (!legacyStretch || FrontEdgeMode != CardEdgePanel.FrontEdgeMode.Independent) return;
        FrontEdgeMode = CardEdgePanel.FrontEdgeMode.FollowTableBg;
        FrontEdgeSyncEnabled = true;
        PlayerPrefs.SetInt(KEY_FRONT_EDGE_MODE, (int)FrontEdgeMode);
        PlayerPrefs.SetInt(KEY_FRONT_EDGE_SYNC, 1);
        PlayerPrefs.Save();
    }

    /// <summary>设置 3D 牌面纯色（与「使用 3D 牌面背景」互斥）。开启后背景自动关闭。</summary>
    public void SetTableFaceColor(Color color) {
        TableFaceColor = color;
        PlayerPrefs.SetString(KEY_TABLE_FACE_COLOR, ColorUtility.ToHtmlStringRGBA(color));
        PlayerPrefs.Save();
    }

    public void SetTableFaceUseSolidColor(bool enabled) {
        TableFaceUseSolidColor = enabled;
        PlayerPrefs.SetInt(KEY_TABLE_FACE_USE_SOLID, enabled ? 1 : 0);
        // 与「使用 3D 牌面背景」互斥
        if (enabled) {
            UseTableFaceBackground = false;
            PlayerPrefs.SetInt(KEY_USE_TABLE_FACE_BACKGROUND, 0);
        }
        PlayerPrefs.Save();
    }

    public void SetFrontEdgeColor(Color color) {
        FrontEdgeColor = color;
        PlayerPrefs.SetString(KEY_FRONT_EDGE_COLOR, ColorUtility.ToHtmlStringRGBA(color));
        PlayerPrefs.Save();
    }

    public void SetFrontEdgeMode(CardEdgePanel.FrontEdgeMode mode) {
        FrontEdgeMode = mode;
        FrontEdgeSyncEnabled = mode == CardEdgePanel.FrontEdgeMode.FollowTableBg;
        PlayerPrefs.SetInt(KEY_FRONT_EDGE_MODE, (int)mode);
        PlayerPrefs.SetInt(KEY_FRONT_EDGE_SYNC, FrontEdgeSyncEnabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void SetSelectedTableBackground(string path, bool isCustom) {
        PlayerPrefs.SetString(KEY_TABLE_BG_PATH, path ?? "");
        PlayerPrefs.SetInt(KEY_TABLE_BG_IS_CUSTOM, isCustom ? 1 : 0);
        PlayerPrefs.Save();
    }

    public (string path, bool isCustom) GetSelectedTableBackground() {
        string path = PlayerPrefs.GetString(KEY_TABLE_BG_PATH, "");
        bool isCustom = PlayerPrefs.GetInt(KEY_TABLE_BG_IS_CUSTOM, 0) == 1;
        return (path, isCustom);
    }

    private static bool LoadUseHandFaceBackground(string packId) {
        if (!PlayerPrefs.HasKey(KEY_USE_HAND_FACE_BACKGROUND)) {
            return TilePackIds.DefaultUseHandFaceBackground(packId);
        }
        return PlayerPrefs.GetInt(KEY_USE_HAND_FACE_BACKGROUND, 0) == 1;
    }

    private static string LoadStandardTilePackId() {
        string packId = PlayerPrefs.GetString(KEY_STANDARD_TILE_PACK_ID, "");
        if (string.IsNullOrEmpty(packId)) {
            packId = PlayerPrefs.GetInt(KEY_CUSTOM_STANDARD_TILE_PACK, 0) == 1
                ? TilePackIds.PackCustom
                : TilePackIds.PackOfficial;
        }
        return TilePackIds.NormalizePackId(packId);
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

    /// <summary>Set and persist the 3D card back color.</summary>
    public void SetCardBackColor(Color color) {
        CardBackColor = color;
        PlayerPrefs.SetString(KEY_CARD_BACK_COLOR, ColorUtility.ToHtmlStringRGBA(color));
        PlayerPrefs.Save();
    }

    /// <summary>Save card back image selection (path, or IndexedDB key on WebGL).</summary>
    public void SetSelectedCardBackImage(string path, bool isCustom) {
        PlayerPrefs.SetString(KEY_CARD_BACK_IMAGE_PATH, path ?? "");
        PlayerPrefs.SetInt(KEY_CARD_BACK_IMAGE_IS_CUSTOM, isCustom ? 1 : 0);
        PlayerPrefs.Save();
    }

    /// <summary>Set and persist the 3D front-side edge color.</summary>
    public void SetSideColor(Color color) {
        SideColor = color;
        PlayerPrefs.SetString(KEY_SIDE_COLOR, ColorUtility.ToHtmlStringRGBA(color));
        PlayerPrefs.Save();
    }

    /// <summary>Set and persist the 3D back-side edge color.</summary>
    public void SetBackEdgeColor(Color color) {
        BackEdgeColor = color;
        PlayerPrefs.SetString(KEY_BACK_EDGE_COLOR, ColorUtility.ToHtmlStringRGBA(color));
        PlayerPrefs.Save();
    }

    /// <summary>Set and persist whether the back-side edge color syncs with the card back color.</summary>
    public void SetBackEdgeSyncEnabled(bool enabled) {
        BackEdgeSyncEnabled = enabled;
        BackEdgeMode = enabled ? CardEdgePanel.BackEdgeMode.FollowBack : CardEdgePanel.BackEdgeMode.Independent;
        PlayerPrefs.SetInt(KEY_BACK_EDGE_SYNC, enabled ? 1 : 0);
        PlayerPrefs.SetInt(KEY_BACK_EDGE_MODE, (int)BackEdgeMode);
        PlayerPrefs.Save();
    }

    /// <summary>Set and persist the back edge color mode (independent / follow back / follow front).</summary>
    public void SetBackEdgeMode(CardEdgePanel.BackEdgeMode mode) {
        BackEdgeMode = mode;
        BackEdgeSyncEnabled = mode == CardEdgePanel.BackEdgeMode.FollowBack;
        PlayerPrefs.SetInt(KEY_BACK_EDGE_MODE, (int)mode);
        PlayerPrefs.SetInt(KEY_BACK_EDGE_SYNC, BackEdgeSyncEnabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    /// <summary>Get card back image selection.</summary>
    public (string path, bool isCustom) GetSelectedCardBackImage() {
        string path = PlayerPrefs.GetString(KEY_CARD_BACK_IMAGE_PATH, "");
        bool isCustom = PlayerPrefs.GetInt(KEY_CARD_BACK_IMAGE_IS_CUSTOM, 0) == 1;
        return (path, isCustom);
    }

    public void SetSelectedHandBackground(string path, bool isCustom) {
        PlayerPrefs.SetString(KEY_HAND_BG_PATH, path ?? "");
        PlayerPrefs.SetInt(KEY_HAND_BG_IS_CUSTOM, isCustom ? 1 : 0);
        PlayerPrefs.Save();
    }

    public (string path, bool isCustom) GetSelectedHandBackground() {
        string path = PlayerPrefs.GetString(KEY_HAND_BG_PATH, "");
        bool isCustom = PlayerPrefs.GetInt(KEY_HAND_BG_IS_CUSTOM, 0) == 1;
        return (path, isCustom);
    }

    public void SetSelectedHandBack(string path, bool isCustom) {
        PlayerPrefs.SetString(KEY_HAND_BACK_PATH, path ?? "");
        PlayerPrefs.SetInt(KEY_HAND_BACK_IS_CUSTOM, isCustom ? 1 : 0);
        PlayerPrefs.Save();
    }

    public (string path, bool isCustom) GetSelectedHandBack() {
        string path = PlayerPrefs.GetString(KEY_HAND_BACK_PATH, "");
        bool isCustom = PlayerPrefs.GetInt(KEY_HAND_BACK_IS_CUSTOM, 0) == 1;
        return (path, isCustom);
    }

    private static Color LoadCardBackColor() {
        string hex = PlayerPrefs.GetString(KEY_CARD_BACK_COLOR, "");
        if (!string.IsNullOrEmpty(hex)) {
            string normalized = hex.StartsWith("#") ? hex : "#" + hex;
            if (ColorUtility.TryParseHtmlString(normalized, out Color color)) {
                return color;
            }
        }
        return DefaultCardBackColor;
    }

    private static Color LoadSideColor() {
        string hex = PlayerPrefs.GetString(KEY_SIDE_COLOR, "");
        if (!string.IsNullOrEmpty(hex)) {
            string normalized = hex.StartsWith("#") ? hex : "#" + hex;
            if (ColorUtility.TryParseHtmlString(normalized, out Color color)) {
                return color;
            }
        }
        return DefaultSideColor;
    }

    private static Color LoadFrontEdgeColor() {
        string hex = PlayerPrefs.GetString(KEY_FRONT_EDGE_COLOR, "");
        if (!string.IsNullOrEmpty(hex)) {
            string normalized = hex.StartsWith("#") ? hex : "#" + hex;
            if (ColorUtility.TryParseHtmlString(normalized, out Color color)) {
                return color;
            }
        }
        return Color.white;
    }

    private static Color LoadBackEdgeColor() {
        string hex = PlayerPrefs.GetString(KEY_BACK_EDGE_COLOR, "");
        if (!string.IsNullOrEmpty(hex)) {
            string normalized = hex.StartsWith("#") ? hex : "#" + hex;
            if (ColorUtility.TryParseHtmlString(normalized, out Color color)) {
                return color;
            }
        }
        return DefaultBackEdgeColor;
    }

    private static Color LoadTableFaceColor() {
        string hex = PlayerPrefs.GetString(KEY_TABLE_FACE_COLOR, "");
        if (!string.IsNullOrEmpty(hex)) {
            string normalized = hex.StartsWith("#") ? hex : "#" + hex;
            if (ColorUtility.TryParseHtmlString(normalized, out Color color)) {
                return color;
            }
        }
        return DefaultTableFaceColor;
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

    public void SetActionButtonColorEnabled(bool enabled) {
        ActionButtonColorEnabled = enabled;
        PlayerPrefs.SetInt(KEY_ACTION_BUTTON_COLOR_ENABLED, enabled ? 1 : 0);
        PlayerPrefs.Save();
        if (GameCanvas.Instance != null) {
            GameCanvas.Instance.RefreshActionButtonColors();
        }
    }

    public void SetGongHuSoundEnabled(bool enabled) {
        GongHuSoundEnabled = enabled;
        PlayerPrefs.SetInt(KEY_GONG_HU_SOUND_ENABLED, enabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void SetMatchSuccessSoundEnabled(bool enabled) {
        MatchSuccessSoundEnabled = enabled;
        PlayerPrefs.SetInt(KEY_MATCH_SUCCESS_SOUND_ENABLED, enabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void SetOpeningAutoBuhuaEnabled(bool enabled) {
        OpeningAutoBuhuaEnabled = enabled;
        PlayerPrefs.SetInt(KEY_OPENING_AUTO_BUHUA_ENABLED, enabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void SetForcePassEnabled(bool enabled) {
        ForcePassEnabled = enabled;
        PlayerPrefs.SetInt(KEY_FORCE_PASS_ENABLED, enabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void SetMeldSpacingEnabled(bool enabled) {
        MeldSpacingEnabled = enabled;
        PlayerPrefs.SetInt(KEY_MELD_SPACING_ENABLED, enabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    /// <summary>下拉索引 0/1 → 预设 1/2；默认预设 2。</summary>
    public void SetTileOutlinePresetFromDropdown(int dropdownIndex) {
        SetTileOutlinePreset(dropdownIndex + 1);
    }

    public void SetTileOutlinePreset(int preset) {
        TileOutlinePreset = Mathf.Clamp(preset, 1, 2);
        PlayerPrefs.SetInt(KEY_TILE_OUTLINE_PRESET, TileOutlinePreset);
        PlayerPrefs.Save();
        ApplyTileOutlinePreset();
    }

    /// <summary>
    /// 预设1：宽2/外扩2/纯黑；预设2：宽3/外扩3/深黑。
    /// </summary>
    public void ApplyTileOutlinePreset() {
        if (!TileOutline.TryGetFeature(out _)) {
            return;
        }
        if (TileOutlinePreset == 1) {
            TileOutline.SetWidth(2f);
            TileOutline.SetExpand(2f);
            TileOutline.SetColor(Color.black); // 纯黑
        } else {
            TileOutline.SetWidth(3f);
            TileOutline.SetExpand(3f);
            TileOutline.SetColor(new Color(0.12f, 0.12f, 0.12f, 1f)); // 深黑
        }
        TileOutline.Enabled = true;
    }

    // 应用排序规则到 TileIdOrder，并在对局中开启自动理牌时立即按新规则重排当前手牌。
    private void ApplyHandSortRule() {
        TileIdOrder.SetSortRule(HandSortSuitOrderMode, HandSortHonorOrderMode, HandSortDragonOrderMode, HandSortRiichiDragonOrderMode);
        if (GameCanvas.Instance != null && AutoAction.Instance != null && AutoAction.Instance.IsAutoArrangeHandCards) {
            GameCanvas.Instance.SortMainHandByTileIdIfNeeded();
        }
    }

    private void ApplyTargetFrameRate() {
        Application.targetFrameRate = LockedFrameRate;
    }

    /// <summary>垂直同步开关（默认开启；WebGL 由浏览器接管，此设置仅在桌面/安卓生效）。</summary>
    public void SetVsyncEnabled(bool enabled) {
        VsyncEnabled = enabled;
        PlayerPrefs.SetInt(KEY_VSYNC_ENABLED, enabled ? 1 : 0);
        PlayerPrefs.Save();
        ApplyVsync();
    }

    private void ApplyVsync() {
        QualitySettings.vSyncCount = VsyncEnabled ? 1 : 0;
    }

    // Windows / WebGL / iOS / Editor：URP MSAA 4x；Android：MSAA 关 + 相机 FXAA 兜底。
    private const int MsaaSampleCountHigh = 4;
    // Android 保留 6/28 PR#50 规避：MSAA 会强制中间 render pass，Mali Valhall Vulkan 上触发 Canvas 丢失。
    private const int MsaaSampleCountDisabled = 1;

    private static void ApplyAntialiasingByPlatform() {
        if (!(UniversalRenderPipeline.asset is UniversalRenderPipelineAsset urpAsset)) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        int target = MsaaSampleCountDisabled;
#else
        int target = MsaaSampleCountHigh;
#endif
        // 仅在值变化时写入，避免无意义脏标记触发管线重建
        if (urpAsset.msaaSampleCount != target) {
            urpAsset.msaaSampleCount = target;
        }
    }

    private static void ApplyCameraAntialiasingByPlatform() {
#if UNITY_ANDROID && !UNITY_EDITOR
        ApplyCameraFxaa(true);
#else
        ApplyCameraFxaa(false);
#endif
    }

    private static void ApplyCameraFxaa(bool enable) {
        var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        foreach (var camera in cameras) {
            if (camera == null || !camera.isActiveAndEnabled) continue;
            if (!camera.TryGetComponent<UniversalAdditionalCameraData>(out var cameraData)) continue;
            if (enable) {
                cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
                cameraData.antialiasingQuality = AntialiasingQuality.High;
            } else {
                cameraData.antialiasing = AntialiasingMode.None;
            }
        }
    }

}
