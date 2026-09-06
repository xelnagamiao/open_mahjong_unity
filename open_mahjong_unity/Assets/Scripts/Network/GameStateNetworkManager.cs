using UnityEngine;
using System;
using System.Threading.Tasks;
using NativeWebSocket;
using Newtonsoft.Json;

/// <summary>
/// 游戏状态网络管理器：<c>gamestate/*</c> 消息的前缀路由 + 回合制通用出站请求。
///
/// 入站：<c>gamestate/{rule}/{suffix}</c> 全部交给当前族 GameState（RuleRegistry.ActiveGameState）；
/// 本类不认识任何规则名，也不认识任何后缀语义。两段式公共消息（观战列表/表情/投票）在此直接处理。
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
        return NetworkManager.Instance.GetWebSocket();
    }

    /// <summary>
    /// 处理游戏状态相关的服务器响应消息。
    /// </summary>
    public void HandleGameStateMessage(Response response) {
        if (RuleRegistry.TryParseGameStateType(response.type, out string rule, out string suffix)) {
            IGameState state = ResolveGameStateForMessage(rule, suffix);
            if (state == null) {
                Debug.LogWarning($"没有可承载的族 GameState: {response.type}");
                return;
            }
            if (!state.HandleMessage(suffix, response)) {
                Debug.LogWarning($"族 {state.StateId} 未处理的游戏状态消息: {response.type}");
            }
            return;
        }

        switch (response.type) {
            case "switch_seat":
                HandleSwitchSeat(response);
                break;
            case "refresh_player_tag_list":
                HandleRefreshPlayerTagList(response);
                break;
            case "gamestate/get_spectator_list":
                HandleGetSpectatorListResponse(response);
                break;
            case "gamestate/broadcast_sticker":
                HandleBroadcastSticker(response);
                break;
            case "gamestate/vote_update":
                HandleVoteUpdate(response);
                break;
            case "gamestate/vote_end":
                HandleVoteEnd(response);
                break;
            default:
                Debug.LogWarning($"未知的游戏状态消息类型: {response.type}");
                break;
        }
    }

    /// <summary>
    /// 决定这条消息由哪个族实例处理：
    /// - game_start（新对局/重连/下一局）或尚无族实例：按消息里的 rule 段建/换族；
    /// - 消息属于另一个已注册族：换族；
    /// - 其余（含 GB/jiandan 等出站通道回包）沿用当前族，不因通道名切换实例。
    /// </summary>
    private static IGameState ResolveGameStateForMessage(string rule, string suffix) {
        IGameState active = RuleRegistry.ActiveGameState;
        if (active == null || suffix == "game_start") {
            return RuleRegistry.SetCurrent(rule);
        }
        if (RuleRegistry.TryResolve(rule, out RuleManifest manifest) && manifest != RuleRegistry.Current) {
            return RuleRegistry.SetCurrent(rule);
        }
        return active;
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

    /// <summary>
    /// 处理获取观战列表响应
    /// </summary>
    private void HandleGetSpectatorListResponse(Response response) {
        Debug.Log($"收到观战列表: {response.message}");
        SpectatorPanel.Instance?.GetSpectatorListResponse(response.success, response.message, response.spectator_list);
        EventDetailPanel.Instance?.OnSpectatorList(response.spectator_list);
    }

    // ========== 游戏状态相关的发送方法 ==========

    /// <summary>
    /// 发送国标卡牌方法（切牌）
    /// </summary>
    public async void SendChineseGameTile(bool cutClass, int tileId, int cutIndex) {
        if (NormalGameStateManager.Instance.IsRealtimeSpectator) return;
        try {
            var request = new SendChineseGameTileRequest {
                type = $"gamestate/{OutboundChannel}/cut_tile",
                cutClass = cutClass,
                TileId = tileId,
                cutIndex = cutIndex,
                gamestate_id = UserDataManager.Instance.GamestateId,
                action_tick = NormalGameStateManager.Instance != null
                    ? NormalGameStateManager.Instance.LastAskActionTick
                    : (int?)null
            };
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogError($"发送切牌消息失败: {e.Message}");
        }
    }

    /// <summary>
    /// 发送吃碰杠回应
    /// </summary>
    public async void SendAction(string action, int targetTile, int chiComboIndex = 0) {
        if (NormalGameStateManager.Instance.IsRealtimeSpectator) return;
        try {
            var request = new SendActionRequest {
                type = $"gamestate/{OutboundChannel}/send_action",
                gamestate_id = UserDataManager.Instance.GamestateId,
                action = action,
                targetTile = targetTile,
                chiComboIndex = chiComboIndex,
                action_tick = NormalGameStateManager.Instance.LastAskActionTick,
            };
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogError($"发送操作消息失败: {e.Message}");
        }
    }

    /// <summary>回合制出站通道段：由当前规则清单声明（默认 "GB"）。</summary>
    private static string OutboundChannel => RuleRegistry.Current?.OutboundChannel ?? "GB";

    public async void SetRyuukyokuTenpai(bool tenpai) {
        if (NormalGameStateManager.Instance.IsRealtimeSpectator) return;
        try {
            var request = new SetRyuukyokuTenpaiRequest {
                type = "gamestate/riichi/set_ryuukyoku_tenpai",
                gamestate_id = UserDataManager.Instance.GamestateId,
                tenpai = tenpai
            };
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogError($"发送流局听牌申报失败: {e.Message}");
        }
    }

    /// <summary>
    /// 发送对局表情包（格式 pack/id，如 turtle/3）。实时观战者不发送。
    /// </summary>
    public async void SendSticker(string sticker) {
        if (NormalGameStateManager.Instance.IsRealtimeSpectator) return;
        if (string.IsNullOrEmpty(sticker)) return;
        try {
            var request = new SendStickerRequest {
                type = "gamestate/send_sticker",
                gamestate_id = UserDataManager.Instance.GamestateId,
                sticker = sticker
            };
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogError($"发送表情包失败: {e.Message}");
        }
    }

    private void HandleBroadcastSticker(Response response) {
        if (response?.sticker_info == null) return;
        GameCanvas.Instance.ShowSticker(
            response.sticker_info.original_player_index,
            response.sticker_info.player_index,
            response.sticker_info.sticker);
    }

    // ========== 房间对局投票暂停/结束 ==========

    private void HandleVoteUpdate(Response response) {
        VotePanel.Instance?.ApplyState(response.vote_info);
        ClearGameTimerIfVoteRequires(response.vote_info);
    }

    private void HandleVoteEnd(Response response) {
        VotePanel.Instance?.Hide();
        ClearGameActionTimer();
        // 投票结束对局通过：直接回主菜单（强制清理对局场景）
        PostGameNavigator.ExitToLobby(forceTeardown: true);
    }

    /// <summary>
    /// 对局已真正挂起或即将结束时，清掉客户端步时/操作 UI。
    /// 注意：pause_pending 仍在等当前这一步做完，不可 ClearAction（否则人不能操作，只能耗时摸切）。
    /// </summary>
    private static void ClearGameTimerIfVoteRequires(VoteInfo info) {
        if (info == null || string.IsNullOrEmpty(info.phase)) return;
        switch (info.phase) {
            case "paused":
            case "resume_voting":
            case "resume_countdown":
                ClearGameActionTimer();
                break;
        }
    }

    private static void ClearGameActionTimer() {
        NormalGameStateManager.Instance.SwitchCurrentPlayer("None", "ClearAction", 0);
    }

    /// <summary>发起投票（vote_type: "pause" / "end"）。仅自定义房间对局真人玩家可发。</summary>
    public async void SendVoteStart(string voteType) {
        if (NormalGameStateManager.Instance.IsRealtimeSpectator) return;
        try {
            var request = new VoteStartRequest {
                type = "gamestate/vote_start",
                gamestate_id = UserDataManager.Instance.GamestateId,
                vote_type = voteType,
            };
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogError($"发起投票失败: {e.Message}");
        }
    }

    /// <summary>提交投票（vote: "agree" / "refuse"）。</summary>
    public async void SendVoteResponse(string vote) {
        if (NormalGameStateManager.Instance.IsRealtimeSpectator) return;
        try {
            var request = new VoteResponseRequest {
                type = "gamestate/vote_response",
                gamestate_id = UserDataManager.Instance.GamestateId,
                vote = vote,
            };
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogError($"提交投票失败: {e.Message}");
        }
    }

    /// <summary>请求解除暂停。</summary>
    public async void SendVoteResume() {
        if (NormalGameStateManager.Instance.IsRealtimeSpectator) return;
        try {
            var request = new VoteResumeRequest {
                type = "gamestate/vote_resume",
                gamestate_id = UserDataManager.Instance.GamestateId,
            };
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogError($"请求解除暂停失败: {e.Message}");
        }
    }

    /// <summary>
    /// 获取观战列表
    /// </summary>
    public async void GetSpectatorList() {
        try {
            var request = new GetSpectatorListRequest {
                type = "gamestate/get_spectator_list"
            };
            Debug.Log($"发送获取观战列表消息: {request.type}");
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogError($"获取观战列表失败: {e.Message}");
            SpectatorPanel.Instance.GetSpectatorListResponse(false, e.Message, null);
        }
    }

    /// <summary>
    /// 添加观战
    /// </summary>
    public async void AddSpectator(string gamestate_id) {
        if (LobbyStateGuard.BlockIfInMatchQueueForSpectator()) return;
        if (GameSessionGuard.BlockIfExclusiveSession("进入延时观战")) return;

        try {
            GameRecordManager.PrepareDelayedSpectatorSession(gamestate_id);
            var request = new AddSpectatorRequest {
                type = "gamestate/GB/add_spectator",
                gamestate_id = gamestate_id
            };
            Debug.Log($"发送添加观战消息: {request.type}, gamestate_id: {gamestate_id}");
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogError($"添加观战失败: {e.Message}");
            NotificationManager.Instance.ShowTip("观战", false, $"添加观战失败: {e.Message}");
        }
    }

    /// <summary>
    /// 移除观战
    /// </summary>
    public async System.Threading.Tasks.Task RemoveSpectator(string gamestate_id) {
        var request = new RemoveSpectatorRequest {
            type = "gamestate/GB/remove_spectator",
            gamestate_id = gamestate_id
        };
        Debug.Log($"发送移除观战消息: {request.type}, gamestate_id: {gamestate_id}");
        await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
    }

    /// <summary>
    /// 立直切牌请求
    /// </summary>
    public async void SendRiichiCut(bool cutClass, int tileId, int cutIndex) {
        if (NormalGameStateManager.Instance.IsRealtimeSpectator) return;
        try {
            var request = new SendChineseGameTileRequest {
                type = "gamestate/riichi/riichi_cut",
                cutClass = cutClass,
                TileId = tileId,
                cutIndex = cutIndex,
                gamestate_id = UserDataManager.Instance.GamestateId,
                action_tick = NormalGameStateManager.Instance != null
                    ? NormalGameStateManager.Instance.LastAskActionTick
                    : (int?)null
            };
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogError($"发送立直切牌消息失败: {e.Message}");
        }
    }
}
