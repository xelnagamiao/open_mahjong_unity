using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class RecordPanel : MonoBehaviour {
    // 单例模式

    public static RecordPanel Instance { get; private set; }
    [SerializeField] private RecordPrefab RecordPrefab;
    [SerializeField] private Transform dropdownContentTransform;

    [SerializeField] private Button BackMenuButton;

    private void Awake() {
        if (Instance == null) {
            Instance = this;
        } else {
            Destroy(gameObject);
            return;
        }
        BackMenuButton.onClick.AddListener(BackMenu);
    }

    private void BackMenu() {
        WindowsManager.Instance.SwitchWindow("menu");
    }

    /// <summary>
    /// 处理获取记录列表的响应
    /// </summary>
    public void GetRecordListResponse(bool success, string message, RecordInfo[] recordList) {
        if (!success) {
            Debug.LogError($"获取记录列表失败: {message}");
            return;
        }

        if (recordList == null || recordList.Length == 0) {
            Debug.Log("没有游戏记录");
            return;
        }

        // 清空现有记录项
        foreach (Transform child in dropdownContentTransform) {
            Destroy(child.gameObject);
        }

        // 为每条记录创建 RecordItem
        foreach (var record in recordList) {
            // 检查玩家信息是否有效
            if (record.players == null || record.players.Length == 0) {
                Debug.LogWarning($"游戏 {record.game_id} 没有玩家信息，跳过");
                continue;
            }

            // 从 record 字典中提取游戏头部信息
            var gameTitle = record.record.ContainsKey("game_title") 
                ? record.record["game_title"] as Dictionary<string, object> 
                : null;

            // 获取规则信息
            string mainRule = record.rule ?? (gameTitle != null && gameTitle.ContainsKey("rule") ? gameTitle["rule"].ToString() : "GB");
            string subRule = gameTitle != null && gameTitle.ContainsKey("max_round") ? $"{gameTitle["max_round"]}局" : "";

            // 获取时间信息
            string recordedTime = record.created_at;

            // 确定是否有完整记录（检查是否有 game_round）
            string hasRecord = record.record.ContainsKey("game_round") ? "有" : "无";

            // 玩家信息已经按排名排序（从服务器端返回时已排序）
            // 确保有4个玩家，不足的用空字符串填充
            string username1 = record.players.Length > 0 ? record.players[0].username : "";
            string username2 = record.players.Length > 1 ? record.players[1].username : "";
            string username3 = record.players.Length > 2 ? record.players[2].username : "";
            string username4 = record.players.Length > 3 ? record.players[3].username : "";

            string score1 = record.players.Length > 0 ? record.players[0].score.ToString() : "0";
            string score2 = record.players.Length > 1 ? record.players[1].score.ToString() : "0";
            string score3 = record.players.Length > 2 ? record.players[2].score.ToString() : "0";
            string score4 = record.players.Length > 3 ? record.players[3].score.ToString() : "0";

            // 将 record 字典序列化为 JSON 字符串
            string recordJson = Newtonsoft.Json.JsonConvert.SerializeObject(record.record);

            // 创建 RecordItem 实例
            RecordPrefab item = Instantiate(RecordPrefab, dropdownContentTransform);

            item.InitializeRecordItem(
                username1, username2, username3, username4,
                score1, score2, score3, score4,
                hasRecord, mainRule, subRule, recordedTime,
                recordJson
            );
        }

        Debug.Log($"成功加载 {recordList.Length} 条游戏记录");
    }

    /// <summary>
    /// 加载游戏记录
    /// </summary>
    /// <param name="recordJson">游戏记录的 JSON 字符串</param>
    public void LoadRecord(string recordJson) {
        if (string.IsNullOrEmpty(recordJson)) {
            Debug.LogError("游戏记录 JSON 字符串为空");
            return;
        }

        try {
            // 解析 JSON 字符串
            // 注意：Unity 的 JsonUtility 不能直接解析 Dictionary<string, object>
            // 如果需要解析复杂结构，可以使用 SimpleJSON 或其他 JSON 库
            Debug.Log($"加载游戏记录: {recordJson}");
            
            // TODO: 实现具体的记录加载逻辑
            // 例如：解析 JSON，恢复游戏状态，显示回放界面等
            
        } catch (System.Exception e) {
            Debug.LogError($"加载游戏记录失败: {e.Message}");
        }
    }
}
