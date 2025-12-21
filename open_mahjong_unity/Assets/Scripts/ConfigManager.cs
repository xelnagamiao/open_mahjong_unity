using System.Collections;
using System.Collections.Generic;
using UnityEngine;
// 内部配置文件 用于选择测试环境或者正式环境
// web 路由 : mahjong.fit/443/web
// 外部路由 : unity 静态路由地址 mahjong.fit/443/game
// 服务器路由 : 服务器地址 mahjong.fit/server
// 聊天服务器路由 : 聊天服务器地址 mahjong.fit/chat

public class ConfigManager : MonoBehaviour
{
    public static ConfigManager Instance { get; private set; }

    // 静态配置
    public static string webUrl = "https://mahjong.fit/443/web"; // 项目网页地址
    public static string gameUrl = "https://mahjong.fit/443/game"; // 项目服务器地址
    public static string chatUrl = "https://mahjong.fit/chat"; // 项目聊天服务器地址
    public static string clientVersion = "0.0.16.2"; // 项目客户端版本(仅保存)
    public static int releaseVersion = 0; // 项目发布版本(服务器验证是否可以连接)
    public static string platformUrl = "https://www.yuque.com/xelnaga-yjcgq/zkwfgr/lusmvid200iez36q?singleDoc#"; // 项目文档地址
    public static string serverUrl = "https://github.com/xelnagamiao/open_mahjong_unity"; // github地址

    // 头衔ID到文本的映射字典
    private static Dictionary<int, string> titleDictionary = new Dictionary<int, string>
    {
        { 1, "暂无头衔" }
    };

    // 获取头衔文本（公共方法）
    public static string GetTitleText(int titleId)
    {
        if (titleDictionary.ContainsKey(titleId))
        {
            return titleDictionary[titleId];
        }
        // 如果找不到对应的头衔，返回默认值
        return titleDictionary.ContainsKey(1) ? titleDictionary[1] : "暂无头衔";
    }

    // 可变更的配置
    public float soundVolume = 1.0f; // 音量
    private int targetFrameRate = 60; // 目标帧率

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        
        
        // 设置目标帧率为 60
        Application.targetFrameRate = targetFrameRate;
    }

    public void SetUserConfig(int volume)
    {
        // 将音量从 0-100 转换为 0-1 范围
        this.soundVolume = Mathf.Clamp01(volume / 100f);
    }
}
