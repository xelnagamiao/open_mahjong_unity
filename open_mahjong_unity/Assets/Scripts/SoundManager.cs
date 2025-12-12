using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }
    
    [Header("音效配置")]
    [SerializeField] private AudioSource audioSource; // 环境音效播放器
    [SerializeField] private AudioSource physicsAudio; // 物理音效播放器
    [SerializeField] private AudioSource selfAudio; // 自身位置音效播放器
    [SerializeField] private AudioSource leftAudio; // 左家位置音效播放器
    [SerializeField] private AudioSource rightAudio; // 右家位置音效播放器
    [SerializeField] private AudioSource topAudio; // 对家位置音效播放器
    
    // 音色ID到文件路径的映射字典
    private Dictionary<int, string> voiceIdToPath = new Dictionary<int, string>
    {
        { 1, "ttsmaker_204_xiaoxiao" },
        { 2, "ttsmaker_1513_qiuqiu" }
    };
    
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        // 如果没有AudioSource，自动添加一个
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
    }
    
    
    // 播放操作音效的方法
    public void PlayActionSound(string playerPosition,string actionType)
    {
        string audioTarget = actionType;

        if (playerPosition == "hu_self"){
            audioTarget = "zimo";
        }
        else if (playerPosition == "hu_first" || playerPosition == "hu_second" || playerPosition == "hu_third"){
            audioTarget = "rong";
        }
        else if (playerPosition == "chi_left" || playerPosition == "chi_mid" || playerPosition == "chi_right"){
            audioTarget = "chi";
        }
        else if (playerPosition == "angang" || playerPosition == "jiagang" || playerPosition == "gang"){
            audioTarget = "gang";
        }

        // 根据用户设置的音色ID获取对应的文件路径
        int voiceId = UserDataManager.Instance != null ? UserDataManager.Instance.VoiceId : 1;
        string voicePath = voiceIdToPath.ContainsKey(voiceId) ? voiceIdToPath[voiceId] : voiceIdToPath[1];
        
        // 构建完整的资源路径
        string soundPath = $"Sound/{voicePath}/{audioTarget}";
        AudioClip soundToPlay = Resources.Load<AudioClip>(soundPath);
        
        if (soundToPlay != null)
        {
            // 使用 ConfigManager 的音量设置
            float volume = ConfigManager.Instance != null ? ConfigManager.Instance.soundVolume : 1.0f;
            audioSource.PlayOneShot(soundToPlay, volume);
            Debug.Log($"播放音效: {playerPosition} {actionType}, 音色: {voicePath}, 音量: {volume}");
        }
        else
        {
            Debug.Log($"未找到音效文件: {soundPath}");
        }
    }

    public void PlayPhysicsSound(/* Vector3 position, 物理音效发出位置 */string actionType){
        AudioClip soundToPlay = Resources.Load<AudioClip>("Sound/Physics/" + actionType);
        if (soundToPlay != null)
        {
            // 使用 ConfigManager 的音量设置，如果未初始化则使用默认值
            float volume = ConfigManager.Instance.soundVolume;
            audioSource.PlayOneShot(soundToPlay, volume);
            Debug.Log($"播放物理音效: {actionType}, 音量: {volume}");
        }
        else
        {
            Debug.Log($"未找到物理音效文件: {actionType}");
        }
    }
}

