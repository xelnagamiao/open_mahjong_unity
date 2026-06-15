using System.Collections.Generic;

/// <summary>
/// 客户端显示语言。繁体中文暂未接入翻译表，行为与简体中文相同。
/// </summary>
public enum AppLanguage {
    SimplifiedChinese = 0,
    TraditionalChinese = 1,
    English = 2,
}

/// <summary>
/// 顶部导航栏按钮文案（与 MainScene HeaderPanel 场景内原文一致）。
/// </summary>
public enum HeaderNavItem {
    Menu,
    Room,
    Friend,
    Spectator,
    PlayerData,
    Record,
    Match,
    SceneConfig,
    AboutUs,
    Notice,
    Config,
    BackToGame,
}

public static class AppLanguageTexts {
    private static readonly Dictionary<HeaderNavItem, (string zh, string en)> HeaderNavLabels =
        new Dictionary<HeaderNavItem, (string zh, string en)> {
            { HeaderNavItem.Menu, ("主菜单", "Menu") },
            { HeaderNavItem.Room, ("房间", "Room") },
            { HeaderNavItem.Friend, ("好友", "Friends") },
            { HeaderNavItem.Spectator, ("观战", "Watch") },
            { HeaderNavItem.PlayerData, ("数据", "Data") },
            { HeaderNavItem.Record, ("牌谱", "Record") },
            { HeaderNavItem.Match, ("匹配", "Match") },
            { HeaderNavItem.SceneConfig, ("场景设置", "Scene cfg") },
            { HeaderNavItem.AboutUs, ("关于我们", "About us") },
            { HeaderNavItem.Notice, ("通知", "Notice") },
            { HeaderNavItem.Config, ("设置", "Setup") },
            { HeaderNavItem.BackToGame, ("返回游戏", "Back") },
        };

    public static string GetHeaderNavLabel(HeaderNavItem item, AppLanguage language) {
        if (!HeaderNavLabels.TryGetValue(item, out var labels)) {
            return "";
        }
        return language == AppLanguage.English ? labels.en : labels.zh;
    }

    public static string GetHeaderNavLabel(HeaderNavItem item) {
        return GetHeaderNavLabel(item, ConfigManager.CurrentLanguage);
    }
}
