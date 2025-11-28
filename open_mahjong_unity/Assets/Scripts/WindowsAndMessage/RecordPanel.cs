using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class RecordPanel : MonoBehaviour
{
    // 单例模式

    public static RecordPanel Instance { get; private set; }
    [SerializeField] private GameObject RecordItemPrefab;
    [SerializeField] private Transform dropdownContentTransform;

    [SerializeField] private Button BackMenuButton;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        BackMenuButton.onClick.AddListener(BackMenu);
    }

    private void BackMenu()
    {
        WindowsManager.Instance.SwitchWindow("main");
    }

    /// <summary>
    /// 加载游戏记录
    /// </summary>
    /// <param name="recordJson">游戏记录的 JSON 字符串</param>
    public void LoadRecord(string recordJson)
    {
        if (string.IsNullOrEmpty(recordJson))
        {
            Debug.LogError("游戏记录 JSON 字符串为空");
            return;
        }

        try
        {
            // 解析 JSON 字符串
            // 注意：Unity 的 JsonUtility 不能直接解析 Dictionary<string, object>
            // 如果需要解析复杂结构，可以使用 SimpleJSON 或其他 JSON 库
            Debug.Log($"加载游戏记录: {recordJson}");
            
            // TODO: 实现具体的记录加载逻辑
            // 例如：解析 JSON，恢复游戏状态，显示回放界面等
            
        }
        catch (System.Exception e)
        {
            Debug.LogError($"加载游戏记录失败: {e.Message}");
        }
    }
}
