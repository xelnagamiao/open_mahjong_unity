using System.Collections;
using System.Collections.Generic;
using UnityEngine;
// 内部配置文件 用于选择测试环境或者正式环境
    // Start is called before the first frame update
// web 路由 : mahjong.fit/443/web
// 外部路由 : unity 静态路由地址 mahjong.fit/443/game
// 服务器路由 : 服务器地址 mahjong.fit/server
// 聊天服务器路由 : 聊天服务器地址 mahjong.fit/chat

// 测试环境




public class Config : MonoBehaviour
{
    public static string webUrl = "https://mahjong.fit/443/web"; // 项目网页地址
    public static string gameUrl = "https://mahjong.fit/443/game"; // 项目服务器地址
    public static string chatUrl = "https://mahjong.fit/chat"; // 项目聊天服务器地址
    public static string clientVersion = "0.0.16.2"; // 项目客户端版本
    public static string platformUrl = "https://www.yuque.com/xelnaga-yjcgq/zkwfgr/lusmvid200iez36q?singleDoc#"; // 项目文档地址
    public static string serverUrl = "https://github.com/xelnagamiao/open_mahjong_unity"; // github地址
    public static string soundConfig = "Soundmale1_normal"; // 音效配置
    public static float soundVolume = 1.0f; // 音量

    // Start is called before the first frame update
    void Start()
    {
        // 设置目标帧率为 60
        Application.targetFrameRate = 60;
        // 设置音效选择

    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
