// 内部配置文件 用于选择测试环境或者正式环境

// web 路由 : mahjong.fit/443/web
// 外部路由 : unity 静态路由地址 mahjong.fit/443/game
// 服务器路由 : 服务器地址 mahjong.fit/server
// 聊天服务器路由 : 聊天服务器地址 mahjong.fit/chat

// 测试环境
public class TestConfig{
    public static string webUrl = "https://mahjong.fit/443/web";
    public static string gameUrl = "https://mahjong.fit/443/game";
    public static string serverUrl = "https://mahjong.fit/server";
    public static string chatUrl = "https://mahjong.fit/chat";
}

