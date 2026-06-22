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
            case "gamestate/classical/game_start":
            case "gamestate/riichi/game_start":
            case "gamestate/sichuan/game_start":
                HandleGameStart(response);
                break;
            case "gamestate/guobiao/broadcast_hand_action":
            case "gamestate/qingque/broadcast_hand_action":
            case "gamestate/classical/broadcast_hand_action":
            case "gamestate/riichi/broadcast_hand_action":
            case "gamestate/sichuan/broadcast_hand_action":
                HandleBroadcastHandAction(response);
                break;
            case "gamestate/guobiao/ask_other_action":
            case "gamestate/qingque/ask_other_action":
            case "gamestate/classical/ask_other_action":
            case "gamestate/riichi/ask_other_action":
            case "gamestate/sichuan/ask_other_action":
                HandleAskOtherAction(response);
                break;
            case "gamestate/guobiao/do_action":
            case "gamestate/qingque/do_action":
            case "gamestate/classical/do_action":
            case "gamestate/riichi/do_action":
            case "gamestate/sichuan/do_action":
                HandleDoAction(response);
                break;
            case "gamestate/guobiao/show_result":
            case "gamestate/qingque/show_result":
            case "gamestate/classical/show_result":
            case "gamestate/riichi/show_result":
            case "gamestate/sichuan/show_result":
                HandleShowResult(response);
                break;
            case "gamestate/guobiao/game_end":
            case "gamestate/qingque/game_end":
            case "gamestate/classical/game_end":
            case "gamestate/riichi/game_end":
            case "gamestate/sichuan/game_end":
                HandleGameEnd(response);
                break;
            case "gamestate/sichuan/ask_dingque":
                HandleDingqueAsk(response);
                break;
            case "gamestate/sichuan/dingque_done":
                HandleDingqueDone(response);
                break;
            case "gamestate/riichi/declare_riichi":
                HandleRiichiDeclare(response);
                break;
            case "gamestate/riichi/update_dora":
                HandleRiichiUpdateDora(response);
                break;
            case "switch_seat":
                HandleSwitchSeat(response);
                break;
            case "refresh_player_tag_list":
                HandleRefreshPlayerTagList(response);
                break;
            case "gamestate/get_spectator_list":
                HandleGetSpectatorListResponse(response);
                break;
            case "gamestate/guobiao/ready_status":
            case "gamestate/qingque/ready_status":
            case "gamestate/classical/ready_status":
            case "gamestate/riichi/ready_status":
            case "gamestate/sichuan/ready_status":
                HandleReadyStatus(response);
                break;
            case "gamestate/classical/show_shuhewei":
                HandleShowShuhewei(response);
                break;
            case "gamestate/broadcast_sticker":
                HandleBroadcastSticker(response);
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
        AutoReconnect.OnGameRestored();
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
            handresponse.action_list,
            handresponse.riichi_candidate_cuts,
            handresponse.forbidden_cut_tiles
        );
    }
    
    /// <summary>
    /// 四川麻将：处理定缺询问。仅本人收到该消息时弹出定缺面板（10 秒倒计时，超时自动选手牌最少花色）。
    /// </summary>
    private void HandleDingqueAsk(Response response) {
        Debug.Log($"收到定缺询问: {response.ask_hand_action_info}");
        if (NormalGameStateManager.Instance != null && NormalGameStateManager.Instance.IsRealtimeSpectator) return;
        if (GameRecordManager.Instance != null && GameRecordManager.Instance.IsSpectating) return;
        GameCanvas.Instance?.ShowDingqueSelection(10);
    }

    /// <summary>
    /// 四川麻将：处理定缺完成广播，按 player_to_dingque 同步各家头像旁的定缺标记。
    /// </summary>
    private void HandleDingqueDone(Response response) {
        Debug.Log($"收到定缺完成: {response.show_result_info?.player_to_dingque}");
        if (response.show_result_info == null) return;
        GameCanvas.Instance?.HideDingqueSelection();
        GameCanvas.Instance?.UpdatePlayerDingque(response.show_result_info.player_to_dingque);
        NormalGameStateManager.Instance?.SetSelfDingqueFromMap(response.show_result_info.player_to_dingque);
        // 定缺完成后若已有手牌且轮到操作，立刻刷新定缺置灰（不等 askHandAction）
        GameCanvas.Instance?.RefreshHandTileSelectability();
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
            askresponse.cut_tile,
            askresponse.chi_candidates
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
            doresponse.combination_target,
            doresponse.is_riichi_horizontal,
            doresponse.is_claim == true,
            doresponse.silent == true,
            doresponse.is_mo_gang,
            doresponse.gang_score_changes,
            doresponse.is_mo_buhua
        );
    }
    
    /// <summary>
    /// 处理显示结算结果
    /// </summary>
    private void HandleShowResult(Response response) {
        Debug.Log($"收到显示结算结果消息: {response.show_result_info}");
        ShowResultInfo showresponse = response.show_result_info;
        if (showresponse == null) return;
        RiichiEndResultExtras riichiExtras = BuildRiichiExtrasIfAny(showresponse);
        GuobiaoEndResultExtras guobiaoExtras = BuildGuobiaoExtrasIfAny(showresponse);
        // 四川流局：由服务端逐条 show_result 驱动（reveal / status），不再批量写入 extras
        NormalGameStateManager.Instance.ShowResult(
            showresponse.hepai_player_index,
            showresponse.player_to_score,
            showresponse.hu_score,
            showresponse.hu_fan,
            showresponse.hu_class,
            showresponse.hepai_player_hand,
            showresponse.hepai_player_huapai,
            showresponse.hepai_player_combination_mask,
            showresponse.base_fu,
            showresponse.fu_fan_list,
            riichiExtras,
            showresponse.score_changes,
            showresponse.silent == true,
            guobiaoExtras,
            showresponse.liuju_step,
            showresponse.liuju_status,
            showresponse.liuju_hands,
            showresponse.liuju_status_final,
            showresponse.hepai_tile,
            showresponse.multi_ron,
            showresponse.suppress_hand_reveal,
            showresponse.liuju_hu_hands,
            showresponse.defer_score_settlement,
            showresponse.cha_payer_index,
            showresponse.ron_discarder_index,
            showresponse.recycle_discard,
            showresponse.gang_refund_changes,
            showresponse.is_qianggang,
            showresponse.liuju_refund
        );
        // 四川·血战到底：本盘未结束（仍有玩家继续行牌）→ 挂起结算层，待下次询问时关闭并续打
        if (NormalGameStateManager.Instance != null && NormalGameStateManager.Instance.IsSichuanRule()) {
            if (showresponse.round_continues == true) {
                NormalGameStateManager.Instance.MarkPendingSichuanContinue();
            } else {
                NormalGameStateManager.Instance.ClearPendingSichuanContinue();
            }
        }
    }

    private static GuobiaoEndResultExtras BuildGuobiaoExtrasIfAny(ShowResultInfo info) {
        if (info.revealed_angang_masks == null || info.revealed_angang_masks.Count == 0) return null;
        return new GuobiaoEndResultExtras { RevealedAngangMasks = info.revealed_angang_masks };
    }

    /// <summary>
    /// 从 show_result_info 中提取立直麻将扩展信息（han/fu/dora/里宝牌/本场/场供/赤宝牌数）。
    /// 非日麻或未携带相关字段时返回 null。
    /// </summary>
    private static RiichiEndResultExtras BuildRiichiExtrasIfAny(ShowResultInfo info) {
        bool hasHuExtras = info.han != null || info.fu != null || info.ura_dora_indicators != null || info.honba != null;
        bool hasRyuuExtras = (info.tenpai_tiles != null && info.tenpai_tiles.Count > 0) || info.exhaustive_penalty != null;
        if (!hasHuExtras && !hasRyuuExtras) {
            return null;
        }
        return new RiichiEndResultExtras {
            Han = info.han ?? 0,
            Fu = info.fu ?? 0,
            AkaCount = info.aka_count ?? 0,
            DoraCount = info.dora_count ?? 0,
            UraDoraCount = info.ura_dora_count ?? 0,
            DoraIndicators = info.dora_indicators != null ? new System.Collections.Generic.List<int>(info.dora_indicators) : new System.Collections.Generic.List<int>(),
            UraDoraIndicators = info.ura_dora_indicators != null ? new System.Collections.Generic.List<int>(info.ura_dora_indicators) : new System.Collections.Generic.List<int>(),
            Honba = info.honba ?? 0,
            RiichiSticksCollected = info.riichi_sticks_collected ?? 0,
            ScoreChanges = info.score_changes,
            TenpaiTiles = info.tenpai_tiles,
            TenpaiHands = info.tenpai_hands,
            NotenPenaltyAfterDraw = info.exhaustive_penalty ?? false,
            LangyongScoredPoints = info.langyong_scored_points ?? 0,
            LangyongMultiplier = info.langyong_multiplier ?? 0,
        };
    }
    
    /// <summary>
    /// 处理游戏结束
    /// </summary>
    private void HandleGameEnd(Response response) {
        Debug.Log($"收到游戏结束消息: {response.game_end_info}");
        GameEndInfo gameendresponse = response.game_end_info;
        NormalGameStateManager.Instance.GameEnd(
            gameendresponse.master_seed,
            gameendresponse.commitment,
            gameendresponse.salt,
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
    
    /// <summary>
    /// 处理获取观战列表响应
    /// </summary>
    private void HandleGetSpectatorListResponse(Response response) {
        Debug.Log($"收到观战列表: {response.message}");
        SpectatorPanel.Instance?.GetSpectatorListResponse(response.success, response.message, response.spectator_list);
    }
    
    // ========== 游戏状态相关的发送方法 ==========
    
    /// <summary>
    /// 发送国标卡牌方法（切牌）
    /// </summary>
    public async void SendChineseGameTile(bool cutClass, int tileId, int cutIndex) {
        if (NormalGameStateManager.Instance != null && NormalGameStateManager.Instance.IsRealtimeSpectator) return;
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
    public async void SendAction(string action, int targetTile, int chiComboIndex = 0) {
        if (NormalGameStateManager.Instance != null && NormalGameStateManager.Instance.IsRealtimeSpectator) return;
        try {
            var request = new SendActionRequest {
                type = "gamestate/GB/send_action",
                gamestate_id = UserDataManager.Instance.GamestateId,
                action = action,
                targetTile = targetTile,
                chiComboIndex = chiComboIndex,
            };
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogError($"发送操作消息失败: {e.Message}");
        }
    }

    public async void SetRyuukyokuTenpai(bool tenpai) {
        if (NormalGameStateManager.Instance != null && NormalGameStateManager.Instance.IsRealtimeSpectator) return;
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
        if (NormalGameStateManager.Instance != null && NormalGameStateManager.Instance.IsRealtimeSpectator) return;
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
        GameCanvas.Instance?.ShowSticker(
            response.sticker_info.original_player_index,
            response.sticker_info.player_index,
            response.sticker_info.sticker);
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
            GameRecordManager.Instance?.PrepareDelayedSpectatorSession(gamestate_id);
            var request = new AddSpectatorRequest {
                type = "gamestate/GB/add_spectator",
                gamestate_id = gamestate_id
            };
            Debug.Log($"发送添加观战消息: {request.type}, gamestate_id: {gamestate_id}");
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogError($"添加观战失败: {e.Message}");
            NotificationManager.Instance?.ShowTip("观战", false, $"添加观战失败: {e.Message}");
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
    /// 处理数和尾结算
    /// </summary>
    private void HandleShowShuhewei(Response response) {
        Debug.Log($"收到数和尾结算消息: {response.show_shuhewei_info}");
        ShowShuheWeiInfo info = response.show_shuhewei_info;
        NormalGameStateManager.Instance.ShowShuhewei(
            info.player_fu,
            info.player_to_score,
            info.score_changes,
            info.player_fan,
            info.player_fu_types,
            info.hu_class,
            info.hepai_player_index,
            info.hepai_player_hand,
            info.hepai_player_combination_mask
        );
    }

    /// <summary>
    /// 处理立直宣告广播：仅 tag_list/score 同步由其他广播负责，这里主要用于客户端动效播放。
    /// </summary>
    private void HandleRiichiDeclare(Response response) {
        Debug.Log($"收到立直宣告: {response.message}");
        var info = response.refresh_player_tag_list_info;
        NormalGameStateManager.Instance.OnRiichiDeclared(info?.player_to_tag_list, info?.riichi_declared_player_index);
    }

    /// <summary>
    /// 处理宝牌/杠宝牌翻开广播。
    /// </summary>
    private void HandleRiichiUpdateDora(Response response) {
        Debug.Log($"收到宝牌更新: {response.message}");
        NormalGameStateManager.Instance.OnDoraUpdated(response.dora_indicators, response.kan_dora_indicators);
    }

    /// <summary>
    /// 立直切牌请求
    /// </summary>
    public async void SendRiichiCut(bool cutClass, int tileId, int cutIndex) {
        if (NormalGameStateManager.Instance != null && NormalGameStateManager.Instance.IsRealtimeSpectator) return;
        try {
            var request = new SendChineseGameTileRequest {
                type = "gamestate/riichi/riichi_cut",
                cutClass = cutClass,
                TileId = tileId,
                cutIndex = cutIndex,
                gamestate_id = UserDataManager.Instance.GamestateId
            };
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogError($"发送立直切牌消息失败: {e.Message}");
        }
    }

    /// <summary>
    /// 处理准备状态更新
    /// </summary>
    private void HandleReadyStatus(Response response) {
        Debug.Log($"收到准备状态更新: {response.message}");
        if (response.ready_status_info != null) {
            // 始终缓存准备状态（即使面板此刻未激活，例如川麻终局步间切换），重建面板时按缓存重绘准备色
            if (EndResultPanel.Instance != null) {
                EndResultPanel.Instance.UpdateReadyStatus(response.ready_status_info.player_to_ready);
            }
            if (EndShuheWeiPanel.Instance != null && EndShuheWeiPanel.Instance.gameObject.activeSelf) {
                EndShuheWeiPanel.Instance.UpdateReadyStatus(response.ready_status_info.player_to_ready);
            }
        }
    }
}

