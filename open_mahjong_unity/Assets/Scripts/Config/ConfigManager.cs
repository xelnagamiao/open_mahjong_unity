using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ConfigManager : MonoBehaviour
{
    public static ConfigManager Instance { get; private set; }

    public static bool Debug = true;
    
    public static string webUrl;
    public static string gameUrl;
    public static string chatUrl;
    public static string clientVersion;
    public static int releaseVersion;
    public static string githubUrl;
    public static string documentUrl;

    static ConfigManager(){
        if (Debug)
        {
            // 开发接口地址
            gameUrl = "http://localhost:8081/game"; // 游戏服务器地址(连接到OMU服务器)
            chatUrl = "http://localhost:8083/chat"; // 聊天服务器地址(连接到OMUChat服务器)
            releaseVersion = 1; // 发行版号(验证客户端-服务器版本是否一致)
        }
        else
        {
            // 开发环境接口地址
            gameUrl = "https://salasasa.cn/443/game";
            chatUrl = "https://salasasa.cn/443/chat";
            releaseVersion = 1;
        }
        // 官方服务器链接网址 用于访问转到 （不影响游戏进程）
        clientVersion = "0.1.35.1"; // 仅存储 [大版本号.发行版号.开发版本.开发小版本号]
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

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        
        MasterVolume = PlayerPrefs.GetInt(KEY_MASTER_VOLUME, DEFAULT_VOLUME);
        MusicVolume = PlayerPrefs.GetInt(KEY_MUSIC_VOLUME, DEFAULT_VOLUME);
        SoundEffectVolume = PlayerPrefs.GetInt(KEY_SOUND_EFFECT_VOLUME, DEFAULT_VOLUME);
        VoiceVolume = PlayerPrefs.GetInt(KEY_VOICE_VOLUME, DEFAULT_VOLUME);
        
        Application.targetFrameRate = 60;
    }

    public void SetUserConfig(int volume)
    {
        MasterVolume = Mathf.Clamp(volume, 0, 100);
    }

    public void SetMasterVolume(int volume)
    {
        MasterVolume = Mathf.Clamp(volume, 0, 100);
        PlayerPrefs.SetInt(KEY_MASTER_VOLUME, MasterVolume);
        PlayerPrefs.Save();
    }

    public void SetMusicVolume(int volume)
    {
        MusicVolume = Mathf.Clamp(volume, 0, 100);
        PlayerPrefs.SetInt(KEY_MUSIC_VOLUME, MusicVolume);
        PlayerPrefs.Save();
    }

    public void SetSoundEffectVolume(int volume)
    {
        SoundEffectVolume = Mathf.Clamp(volume, 0, 100);
        PlayerPrefs.SetInt(KEY_SOUND_EFFECT_VOLUME, SoundEffectVolume);
        PlayerPrefs.Save();
    }

    public void SetVoiceVolume(int volume)
    {
        VoiceVolume = Mathf.Clamp(volume, 0, 100);
        PlayerPrefs.SetInt(KEY_VOICE_VOLUME, VoiceVolume);
        PlayerPrefs.Save();
    }

    public static string GetTitleText(int titleId)
    {
        return titleDictionary.ContainsKey(titleId) ? titleDictionary[titleId] : titleDictionary[1];
    }
}
