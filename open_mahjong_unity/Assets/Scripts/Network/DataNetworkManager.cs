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
            case "data/get_record_by_id":
                HandleGetRecordByIdResponse(response);
                break;
            case "data/get_guobiao_stats":
                HandleGetGuobiaoStatsResponse(response);
                break;
            case "data/get_riichi_stats":
                HandleGetRiichiStatsResponse(response);
                break;
            case "data/get_qingque_stats":
                HandleGetQingqueStatsResponse(response);
                break;
            default:
                Debug.LogWarning($"未知的数据消息类型: {response.type}");
                break;
        }
    }
    
    private void HandleGetRecordListResponse(Response response) {
        Debug.Log($"收到游戏记录列表: {response.message}");
        RecordPanel.Instance.GetRecordListResponse(response.success, response.message, response.record_list);
    }
    
    private void HandleGetRecordByIdResponse(Response response) {
        Debug.Log($"收到牌谱详情: {response.message}");
        if (!response.success) {
            NotificationManager.Instance.ShowTip("牌谱", false, response.message);
            return;
        }
        RecordPanel.Instance.OnRecordDetailReceived(response.record_detail);
    }
    
    /// <summary>
    /// 处理获取国标统计数据响应
    /// </summary>
    private void HandleGetGuobiaoStatsResponse(Response response) {
        if (PlayerInfoPanel.Instance == null) return;
        
        // 如果响应中包含玩家信息，先显示玩家信息
        if (response.player_info != null) {
            PlayerInfoPanel.Instance.ShowPlayerInfo(response.player_info);
        }
        
        // 处理统计数据
        PlayerInfoPanel.Instance.OnGuobiaoStatsReceived(response.success, response.message, response.rule_stats);
    }
    
    /// <summary>
    /// 处理获取立直统计数据响应
    /// </summary>
    private void HandleGetRiichiStatsResponse(Response response) {
        if (PlayerInfoPanel.Instance == null) return;
        
        // 如果响应中包含玩家信息，先显示玩家信息
        if (response.player_info != null) {
            PlayerInfoPanel.Instance.ShowPlayerInfo(response.player_info);
        }
        
        // 处理统计数据
        PlayerInfoPanel.Instance.OnRiichiStatsReceived(response.success, response.message, response.rule_stats);
    }
    
    /// <summary>
    /// 处理获取青雀统计数据响应
    /// </summary>
    private void HandleGetQingqueStatsResponse(Response response) {
        if (PlayerInfoPanel.Instance == null) return;
        
        if (response.player_info != null) {
            PlayerInfoPanel.Instance.ShowPlayerInfo(response.player_info);
        }
        
        PlayerInfoPanel.Instance.OnQingqueStatsReceived(response.success, response.message, response.rule_stats);
    }
    
    /// <summary>
    /// 处理获取观战列表响应
    /// </summary>
    private void HandleGetSpectatorListResponse(Response response) {
        Debug.Log($"收到观战列表: {response.message}");
        SpectatorPanel.Instance?.GetSpectatorListResponse(response.success, response.message, response.spectator_list);
    }
    
    // ========== 数据相关的发送方法 ==========
    
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
    
    public async void GetRecordById(string gameId) {
        try {
            var request = new { type = "data/get_record_by_id", game_id = gameId };
            Debug.Log($"发送获取牌谱详情消息: game_id={gameId}");
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogError($"获取牌谱详情失败: {e.Message}");
            NotificationManager.Instance.ShowTip("牌谱", false, $"获取牌谱失败: {e.Message}");
        }
    }
    
    /// <summary>
    /// 获取国标统计数据
    /// </summary>
    public async void GetGuobiaoStats(string userid, bool need_player_info = false) {
        try {
            var request = new GetGuobiaoStatsRequest {
                type = "data/get_guobiao_stats",
                userid = userid,
                need_player_info = need_player_info
            };
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogError($"获取国标统计数据失败: {e.Message}");
            PlayerInfoPanel.Instance?.OnGuobiaoStatsReceived(false, e.Message, null);
        }
    }

    /// <summary>
    /// 获取立直统计数据
    /// </summary>
    public async void GetRiichiStats(string userid, bool need_player_info = false) {
        try {
            var request = new GetRiichiStatsRequest {
                type = "data/get_riichi_stats",
                userid = userid,
                need_player_info = need_player_info
            };
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogError($"获取立直统计数据失败: {e.Message}");
            PlayerInfoPanel.Instance.OnRiichiStatsReceived(false, e.Message, null);
        }
    }

    /// <summary>
    /// 获取青雀统计数据
    /// </summary>
    public async void GetQingqueStats(string userid, bool need_player_info = false) {
        try {
            var request = new GetQingqueStatsRequest {
                type = "data/get_qingque_stats",
                userid = userid,
                need_player_info = need_player_info
            };
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogError($"获取青雀统计数据失败: {e.Message}");
            PlayerInfoPanel.Instance?.OnQingqueStatsReceived(false, e.Message, null);
        }
    }
}

