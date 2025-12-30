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
    public static string platformUrl;
    public static string githubUrl;

    static ConfigManager(){
        if (Debug)
        {
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
            webUrl = "https://mahjong.fit/443/web";
            gameUrl = "https://mahjong.fit/443/game";
            chatUrl = "https://mahjong.fit/chat";
            clientVersion = "0.0.31.0";
            releaseVersion = 1;
            platformUrl = "https://www.yuque.com/xelnaga-yjcgq/zkwfgr/lusmvid200iez36q?singleDoc#";
            githubUrl = "https://github.com/xelnagamiao/open_mahjong_unity";
        }
    }

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
