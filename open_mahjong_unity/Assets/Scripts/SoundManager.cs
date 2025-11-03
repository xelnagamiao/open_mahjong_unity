using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }
    
    [Header("音效配置")]
    [SerializeField] private AudioSource audioSource; // 音效播放器
    [SerializeField] private AudioSource physicsAudio; // 物理音效播放器
    [SerializeField] private AudioSource selfAudio; // 自身位置音效播放器
    [SerializeField] private AudioSource leftAudio; // 左家位置音效播放器
    [SerializeField] private AudioSource rightAudio; // 右家位置音效播放器
    [SerializeField] private AudioSource topAudio; // 对家位置音效播放器
    
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
        
        // 设置AudioSource属性
        audioSource.volume = Config.soundVolume;
        audioSource.playOnAwake = false;
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

        AudioClip soundToPlay = Resources.Load<AudioClip>("Sound/" + Config.soundConfig + "/" + audioTarget);
        if (soundToPlay != null)
        {
            audioSource.PlayOneShot(soundToPlay, Config.soundVolume);
            Debug.Log($"播放音效: {playerPosition} {actionType}");
        }
        else
        {
            Debug.Log($"未找到音效文件: {actionType}");
        }
    }

    public void PlayPhysicsSound(/* Vector3 position, 物理音效发出位置 */string actionType){
        AudioClip soundToPlay = Resources.Load<AudioClip>("Sound/Physics/" + actionType);
        if (soundToPlay != null)
        {
            audioSource.PlayOneShot(soundToPlay, Config.soundVolume);
            Debug.Log($"播放物理音效: {actionType}");
        }
        else
        {
            Debug.Log($"未找到物理音效文件: {actionType}");
        }
    }
}

