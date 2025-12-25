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

    public static bool Debug = true;
    
    // 静态配置
    public static string webUrl; // 项目网页地址
    public static string gameUrl; // 项目服务器地址
    public static string chatUrl; // 项目聊天服务器地址
    public static string clientVersion; // 项目客户端版本(仅保存)
    public static int releaseVersion; // 项目发布版本(服务器验证是否可以连接)
    public static string platformUrl; // 项目文档地址
    public static string githubUrl; // github地址

    // 静态构造函数，根据 Debug 值初始化配置
    static ConfigManager()
    {
        if (Debug)
        {
            // Debug 环境配置
            webUrl = "http://localhost:8080/443/web";
            gameUrl = "http://localhost:8081/game";
            chatUrl = "http://localhost:8083/chat";
            clientVersion = "0.0.31.2";
            releaseVersion = 1;
            platformUrl = "https://www.yuque.com/xelnaga-yjcgq/zkwfgr/lusmvid200iez36q?singleDoc#";
            githubUrl = "https://github.com/xelnagamiao/open_mahjong_unity";
        }
        else
        {
            // 生产环境配置
            webUrl = "https://mahjong.fit/443/web";
            gameUrl = "https://mahjong.fit/443/game";
            chatUrl = "https://mahjong.fit/chat";
            clientVersion = "0.0.31.0";
            releaseVersion = 1;
            platformUrl = "https://www.yuque.com/xelnaga-yjcgq/zkwfgr/lusmvid200iez36q?singleDoc#";
            githubUrl = "https://github.com/xelnagamiao/open_mahjong_unity";
        }
    }

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
