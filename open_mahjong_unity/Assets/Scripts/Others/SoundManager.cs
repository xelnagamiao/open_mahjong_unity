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
    
    
    public void PlayActionButtonAppearSound() {
        AudioClip soundToPlay = Resources.Load<AudioClip>("Sound/Effects/mixkit-modern-technology-select-3124");
        if (soundToPlay == null) {
            Debug.LogWarning("未找到操作按钮显示音效: Sound/Effects/mixkit-modern-technology-select-3124");
            return;
        }
        float volume = ConfigManager.Instance != null ? ConfigManager.Instance.GetSoundEffectVolumeRatio() : 1.0f;
        audioSource.PlayOneShot(soundToPlay, volume);
    }

    // 播放操作音效的方法
    public void PlayActionSound(string playerPosition, string actionType) {
        // 根据玩家位置获取对应玩家的音色ID
        int voiceId = 1; // 默认音色ID
        if (NormalGameStateManager.Instance != null && NormalGameStateManager.Instance.player_to_info.ContainsKey(playerPosition)) {
            voiceId = NormalGameStateManager.Instance.player_to_info[playerPosition].voice_used;
        } else if (UserDataManager.Instance != null) {
            // 如果无法从NormalGameStateManager获取，则使用用户设置的音色ID作为后备
            voiceId = UserDataManager.Instance.VoiceId;
        }


        string voicePath = voiceIdToPath.ContainsKey(voiceId) ? voiceIdToPath[voiceId] : voiceIdToPath[1];
        string audioTarget;

        string voiceKey = ResolveActionVoiceKey(actionType);
        if (voiceKey == null) {
            Debug.LogWarning($"未找到音效文件: {actionType}");
            return;
        }
        audioTarget = $"Sound/{voicePath}/{voiceKey}";
        AudioClip soundToPlay = LoadVoiceClipWithFallback(voicePath, voiceKey, out string loadedTarget);
        
        if (soundToPlay != null) {
            float volume = ConfigManager.Instance != null ? ConfigManager.Instance.GetVoiceVolumeRatio() : 1.0f;
            audioSource.PlayOneShot(soundToPlay, volume);
            Debug.Log($"播放音效: {actionType}, 音色: {voicePath}, 资源: {loadedTarget}, 音量: {volume}");
        } else {
            Debug.LogWarning($"未找到音效文件: {audioTarget}");
        }
    }

    private static string ResolveActionVoiceKey(string actionType) {
        GameRecordManager.ResolveActionRuleContext(null, null, out string roomRule, out string subRule);

        if (actionType == "hu_self") {
            if (GameRecordManager.IsGuobiaoRule(roomRule, subRule)) {
                return "hu";
            }
            return "zimo";
        }
        if (actionType == "hu" || actionType == "hu_first" || actionType == "hu_second" || actionType == "hu_third") {
            if (roomRule == "riichi" || (!string.IsNullOrEmpty(subRule) && subRule.StartsWith("riichi/"))) {
                return "rong";
            }
            return "hu";
        }
        if (actionType == "buhua") {
            return "buhua";
        }
        if (actionType == "chi_left" || actionType == "chi_mid" || actionType == "chi_right") {
            return "chi";
        }
        if (actionType == "angang" || actionType == "gang" || actionType == "jiagang") {
            return "gang";
        }
        if (actionType == "peng") {
            return "peng";
        }
        return null;
    }

    private AudioClip LoadVoiceClipWithFallback(string voicePath, string voiceKey, out string loadedTarget) {
        foreach (string key in GetVoiceFallbackKeys(voiceKey)) {
            string target = $"Sound/{voicePath}/{key}";
            AudioClip clip = Resources.Load<AudioClip>(target);
            if (clip != null) {
                loadedTarget = target;
                return clip;
            }
        }
        foreach (string key in GetVoiceFallbackKeys(voiceKey)) {
            string target = $"Sound/{voiceIdToPath[1]}/{key}";
            AudioClip clip = Resources.Load<AudioClip>(target);
            if (clip != null) {
                loadedTarget = target;
                return clip;
            }
        }
        loadedTarget = null;
        return null;
    }

    private static string[] GetVoiceFallbackKeys(string voiceKey) {
        if (voiceKey == "rong") return new[] { "rong", "dianhe", "hu" };
        if (voiceKey == "zimo") return new[] { "zimo", "hu" };
        if (voiceKey == "angang") return new[] { "angang", "gang" };
        if (voiceKey == "jiagang") return new[] { "jiagang", "gang" };
        return new[] { voiceKey };
    }

    /// <summary>
    /// 播放立直宣告语音；按指定方位玩家的音色挑选 riichi 资源，加载失败时回退默认音色。
    /// </summary>
    public void PlayRiichiVoice(string playerPosition, int? voiceIdOverride = null) {
        int voiceId = 1;
        if (voiceIdOverride.HasValue) {
            voiceId = voiceIdOverride.Value;
        } else if (NormalGameStateManager.Instance != null && NormalGameStateManager.Instance.player_to_info.ContainsKey(playerPosition)) {
            voiceId = NormalGameStateManager.Instance.player_to_info[playerPosition].voice_used;
        } else if (UserDataManager.Instance != null) {
            voiceId = UserDataManager.Instance.VoiceId;
        }
        string voicePath = voiceIdToPath.ContainsKey(voiceId) ? voiceIdToPath[voiceId] : voiceIdToPath[1];
        AudioClip clip = Resources.Load<AudioClip>($"Sound/{voicePath}/riichi");
        if (clip == null) clip = Resources.Load<AudioClip>($"Sound/{voiceIdToPath[1]}/riichi");
        if (clip == null) {
            Debug.LogWarning("未找到立直音效资源 Sound/<voice>/riichi");
            return;
        }
        float volume = ConfigManager.Instance != null ? ConfigManager.Instance.GetVoiceVolumeRatio() : 1.0f;
        audioSource.PlayOneShot(clip, volume);
    }

    public void PlayGameStartSound() {
        AudioClip soundToPlay = Resources.Load<AudioClip>("Sound/Physics/gamestart");
        if (soundToPlay == null) {
            Debug.LogWarning("未找到匹配成功音效: Sound/Physics/gamestart");
            return;
        }
        float volume = ConfigManager.Instance != null ? ConfigManager.Instance.GetSoundEffectVolumeRatio() : 1.0f;
        audioSource.PlayOneShot(soundToPlay, volume * 0.4f);
    }

    public void PlayPhysicsSound(/* Vector3 position, 物理音效发出位置 */string actionType){
        string fileName;
        if (actionType == "cut") {
            fileName = "SFX_UI_Click_Organic_Plastic_Select_2";
        } else if (actionType == "Gong_hu") {
            fileName = "Gong_hu";
        } else {
            return;
        }
        AudioClip soundToPlay = Resources.Load<AudioClip>("Sound/Physics/" + fileName);
        if (soundToPlay == null) {
            Debug.LogWarning($"未找到物理音效文件: {actionType}");
            return;
        }
        float volume = ConfigManager.Instance != null ? ConfigManager.Instance.GetSoundEffectVolumeRatio() : 1.0f;
        audioSource.PlayOneShot(soundToPlay, volume);
        Debug.Log($"播放物理音效: {actionType}, 音量: {volume}");
    }
}

