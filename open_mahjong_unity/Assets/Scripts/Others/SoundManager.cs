using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SoundManager : MonoBehaviour {
    public static SoundManager Instance { get; private set; }
    
    [Header("音效配置")]
    [SerializeField] private AudioSource audioSource; // 环境音效播放器
    
    // 音色ID到文件路径的映射字典
    private Dictionary<int, string> voiceIdToPath = new Dictionary<int, string> {
        { 1, "ttsmaker_204_xiaoxiao" },
        { 2, "ttsmaker_1513_qiuqiu" }
    };
    
    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        // 如果没有AudioSource，自动添加一个
        if (audioSource == null) {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
    }
    
    
    // 播放操作音效的方法
    public void PlayActionSound(string playerPosition,string actionType) {
        // 根据玩家位置获取对应玩家的音色ID
        int voiceId = 1; // 默认音色ID
        if (GameSceneManager.Instance != null && GameSceneManager.Instance.player_to_info.ContainsKey(playerPosition)) {
            voiceId = GameSceneManager.Instance.player_to_info[playerPosition].voice_used;
        } else if (UserDataManager.Instance != null) {
            // 如果无法从GameSceneManager获取，则使用用户设置的音色ID作为后备
            voiceId = UserDataManager.Instance.VoiceId;
        }


        string voicePath = voiceIdToPath.ContainsKey(voiceId) ? voiceIdToPath[voiceId] : voiceIdToPath[1];
        string audioTarget;

        if (actionType == "hu_self"){
            audioTarget = $"Sound/{voicePath}/zimo";
        } else if (actionType == "hu_first" || actionType == "hu_second" || actionType == "hu_third"){
            audioTarget = $"Sound/{voicePath}/hu";
        } else if (actionType == "buhua"){
            audioTarget = $"Sound/{voicePath}/buhua";
        } else if (actionType == "chi_left" || actionType == "chi_mid" || actionType == "chi_right"){
            audioTarget = $"Sound/{voicePath}/chi";
        } else if (actionType == "angang" || actionType == "gang"){
            audioTarget = $"Sound/{voicePath}/gang";
        } else if (actionType == "jiagang"){
            audioTarget = $"Sound/{voicePath}/gang";
        } else if (actionType == "peng"){
            audioTarget = $"Sound/{voicePath}/peng";
        } else {
            Debug.LogWarning($"未找到音效文件: {actionType}");
            return;
        }
        
        AudioClip soundToPlay = Resources.Load<AudioClip>(audioTarget);
        
        if (soundToPlay != null) {
            float volume = ConfigManager.Instance != null ? ConfigManager.Instance.VoiceVolume / 100f : 1.0f;
            audioSource.PlayOneShot(soundToPlay, volume);
            Debug.Log($"播放音效: {actionType}, 音色: {voicePath}, 音量: {volume}");
        } else {
            Debug.LogWarning($"未找到音效文件: {audioTarget}");
        }
    }

    public void PlayPhysicsSound(/* Vector3 position, 物理音效发出位置 */string actionType){

        if (actionType == "cut"){
            actionType = "SFX_UI_Click_Organic_Plastic_Select_2";
        } else {
            return;
        }
        AudioClip soundToPlay = Resources.Load<AudioClip>("Sound/Physics/" + actionType);
        if (soundToPlay == null) {
            Debug.LogWarning($"未找到物理音效文件: {actionType}");
            return;
        }
        float volume = ConfigManager.Instance != null ? ConfigManager.Instance.SoundEffectVolume / 100f : 1.0f;
        audioSource.PlayOneShot(soundToPlay, volume);
        Debug.Log($"播放物理音效: {actionType}, 音量: {volume}");
    }
}

