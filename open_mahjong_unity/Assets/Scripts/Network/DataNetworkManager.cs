using UnityEngine;
using System;
using System.Threading.Tasks;
using NativeWebSocket;
using Newtonsoft.Json;

/// <summary>
/// 数据网络管理器 - 处理所有数据相关的网络通信（游戏记录、统计数据等）
/// </summary>
public class DataNetworkManager : MonoBehaviour {
    
    public static DataNetworkManager Instance { get; private set; }
    
    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
    
    /// <summary>
    /// 获取 websocket 连接（通过 NetworkManager）
    /// </summary>
    private WebSocket GetWebSocket() {
        return NetworkManager.Instance?.GetWebSocket();
    }
    
    /// <summary>
    /// 处理数据相关的服务器响应消息
    /// </summary>
    public void HandleDataMessage(Response response) {
        switch (response.type) {
            case "data/get_record_list":
                HandleGetRecordListResponse(response);
                break;
            case "data/get_guobiao_stats":
                HandleGetGuobiaoStatsResponse(response);
                break;
            case "data/get_riichi_stats":
                HandleGetRiichiStatsResponse(response);
                break;
            default:
                Debug.LogWarning($"未知的数据消息类型: {response.type}");
                break;
        }
    }
    
    /// <summary>
    /// 处理获取游戏记录列表响应
    /// </summary>
    private void HandleGetRecordListResponse(Response response) {
        Debug.Log($"收到游戏记录列表: {response.message}");
        RecordPanel.Instance.GetRecordListResponse(response.success, response.message, response.record_list);
    }
    
    /// <summary>
    /// 处理获取国标统计数据响应
    /// </summary>
    private void HandleGetGuobiaoStatsResponse(Response response) {
        Debug.Log($"收到国标统计数据: {response.message}");
        if (PlayerInfoPanel.Instance != null) {
            PlayerInfoPanel.Instance.OnGuobiaoStatsReceived(response.success, response.message, response.rule_stats);
        }
    }
    
    /// <summary>
    /// 处理获取立直统计数据响应
    /// </summary>
    private void HandleGetRiichiStatsResponse(Response response) {
        Debug.Log($"收到立直统计数据: {response.message}");
        if (PlayerInfoPanel.Instance != null) {
            PlayerInfoPanel.Instance.OnRiichiStatsReceived(response.success, response.message, response.rule_stats);
        }
    }
    
    // ========== 数据相关的发送方法 ==========
    
    /// <summary>
    /// 获取游戏记录列表
    /// </summary>
    public async void GetRecordList() {
        try {
            var request = new GetRecordListRequest {
                type = "data/get_record_list"
            };
            Debug.Log($"发送获取游戏记录列表消息: {request.type}");
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogError($"获取游戏记录列表失败: {e.Message}");
            RecordPanel.Instance?.GetRecordListResponse(false, e.Message, null);
        }
    }
    
    /// <summary>
    /// 获取国标统计数据
    /// </summary>
    public async void GetGuobiaoStats(string userid) {
        try {
            var request = new GetGuobiaoStatsRequest {
                type = "data/get_guobiao_stats",
                userid = userid
            };
            Debug.Log($"发送获取国标统计数据消息: {request.type}, userid: {userid}");
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogError($"获取国标统计数据失败: {e.Message}");
            if (PlayerInfoPanel.Instance != null) {
                PlayerInfoPanel.Instance.OnGuobiaoStatsReceived(false, e.Message, null);
            }
        }
    }
    
    /// <summary>
    /// 获取立直统计数据
    /// </summary>
    public async void GetRiichiStats(string userid) {
        try {
            var request = new GetRiichiStatsRequest {
                type = "data/get_riichi_stats",
                userid = userid
            };
            Debug.Log($"发送获取立直统计数据消息: {request.type}, userid: {userid}");
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogError($"获取立直统计数据失败: {e.Message}");
            if (PlayerInfoPanel.Instance != null) {
                PlayerInfoPanel.Instance.OnRiichiStatsReceived(false, e.Message, null);
            }
        }
    }
}

