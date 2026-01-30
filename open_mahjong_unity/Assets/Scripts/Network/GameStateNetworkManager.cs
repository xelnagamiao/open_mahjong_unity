using UnityEngine;
using System;
using System.Threading.Tasks;
using NativeWebSocket;
using Newtonsoft.Json;

/// <summary>
/// 游戏状态网络管理器 - 处理所有游戏状态相关的网络通信
/// </summary>
public class GameStateNetworkManager : MonoBehaviour {
    
    public static GameStateNetworkManager Instance { get; private set; }
    
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
    /// 处理游戏状态相关的服务器响应消息
    /// </summary>
    public void HandleGameStateMessage(Response response) {
        switch (response.type) {
            case "gamestate/guobiao/game_start":
            case "gamestate/qingque/game_start":
                HandleGameStart(response);
                break;
            case "gamestate/guobiao/broadcast_hand_action":
            case "gamestate/qingque/broadcast_hand_action":
                HandleBroadcastHandAction(response);
                break;
            case "gamestate/guobiao/ask_other_action":
            case "gamestate/qingque/ask_other_action":
                HandleAskOtherAction(response);
                break;
            case "gamestate/guobiao/do_action":
            case "gamestate/qingque/do_action":
                HandleDoAction(response);
                break;
            case "gamestate/guobiao/show_result":
            case "gamestate/qingque/show_result":
                HandleShowResult(response);
                break;
            case "gamestate/guobiao/game_end":
            case "gamestate/qingque/game_end":
                HandleGameEnd(response);
                break;
            case "switch_seat":
                HandleSwitchSeat(response);
                break;
            case "refresh_player_tag_list":
                HandleRefreshPlayerTagList(response);
                break;
            default:
                Debug.LogWarning($"未知的游戏状态消息类型: {response.type}");
                break;
        }
    }
    
    /// <summary>
    /// 处理游戏开始响应
    /// </summary>
    private void HandleGameStart(Response response) {
        Debug.Log($"游戏开始: {response.message}");
        NormalGameStateManager.Instance.InitializeGame(response.success, response.message, response.game_info);
    }
    
    /// <summary>
    /// 处理手牌轮操作广播
    /// </summary>
    private void HandleBroadcastHandAction(Response response) {
        Debug.Log($"收到手牌轮操作信息: {response.ask_hand_action_info}");
        AskHandActionGBInfo handresponse = response.ask_hand_action_info;
        NormalGameStateManager.Instance.AskHandAction(
            handresponse.remaining_time,
            handresponse.player_index,
            handresponse.remain_tiles,
            handresponse.action_list
        );
    }
    
    /// <summary>
    /// 处理询问弃牌后操作
    /// </summary>
    private void HandleAskOtherAction(Response response) {
        Debug.Log($"收到询问弃牌后操作消息: {response.ask_other_action_info}");
        AskOtherActionGBInfo askresponse = response.ask_other_action_info;
        NormalGameStateManager.Instance.AskMingPaiAction(
            askresponse.remaining_time,
            askresponse.action_list,
            askresponse.cut_tile
        );
    }
    
    /// <summary>
    /// 处理执行操作
    /// </summary>
    private void HandleDoAction(Response response) {
        Debug.Log($"收到执行操作消息: {response.do_action_info}");
        DoActionInfo doresponse = response.do_action_info;
        NormalGameStateManager.Instance.DoAction(
            doresponse.action_list,
            doresponse.action_player,
            doresponse.cut_tile,
            doresponse.cut_tile_index,
            doresponse.cut_class,
            doresponse.deal_tile,
            doresponse.buhua_tile,
            doresponse.combination_mask,
            doresponse.combination_target
        );
    }
    
    /// <summary>
    /// 处理显示结算结果
    /// </summary>
    private void HandleShowResult(Response response) {
        Debug.Log($"收到显示结算结果消息: {response.show_result_info}");
        ShowResultInfo showresponse = response.show_result_info;
        NormalGameStateManager.Instance.ShowResult(
            showresponse.hepai_player_index,
            showresponse.player_to_score,
            showresponse.hu_score,
            showresponse.hu_fan,
            showresponse.hu_class,
            showresponse.hepai_player_hand,
            showresponse.hepai_player_huapai,
            showresponse.hepai_player_combination_mask
        );
    }
    
    /// <summary>
    /// 处理游戏结束
    /// </summary>
    private void HandleGameEnd(Response response) {
        Debug.Log($"收到游戏结束消息: {response.game_end_info}");
        GameEndInfo gameendresponse = response.game_end_info;
        NormalGameStateManager.Instance.GameEnd(
            gameendresponse.game_random_seed,
            gameendresponse.player_final_data
        );
    }
    
    /// <summary>
    /// 处理换位消息
    /// </summary>
    private void HandleSwitchSeat(Response response) {
        Debug.Log($"收到换位消息: {response.message}");
        NormalGameStateManager.Instance.HandleSwitchSeat(response.switch_seat_info.current_round);
    }
    
    /// <summary>
    /// 处理刷新玩家标签列表消息
    /// </summary>
    private void HandleRefreshPlayerTagList(Response response) {
        Debug.Log($"收到刷新玩家标签列表消息: {response.message}");
        RefreshPlayerTagListInfo tagInfo = response.refresh_player_tag_list_info;
        NormalGameStateManager.Instance.RefreshPlayerTagList(tagInfo.player_to_tag_list);
    }
    
    // ========== 游戏状态相关的发送方法 ==========
    
    /// <summary>
    /// 发送国标卡牌方法（切牌）
    /// </summary>
    public async void SendChineseGameTile(bool cutClass, int tileId, int cutIndex) {
        try {
            var request = new SendChineseGameTileRequest {
                type = "gamestate/GB/cut_tile",
                cutClass = cutClass,
                TileId = tileId,
                cutIndex = cutIndex,
                gamestate_id = UserDataManager.Instance.GamestateId
            };
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogError($"发送切牌消息失败: {e.Message}");
        }
    }
    
    /// <summary>
    /// 发送吃碰杠回应
    /// </summary>
    public async void SendAction(string action, int targetTile) {
        try {
            var request = new SendActionRequest {
                type = "gamestate/GB/send_action",
                gamestate_id = UserDataManager.Instance.GamestateId,
                action = action,
                targetTile = targetTile
            };
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogError($"发送操作消息失败: {e.Message}");
        }
    }
}

