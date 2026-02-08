using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 观战游戏状态管理器 - 处理观战模式下的游戏状态
/// </summary>
public class SpectatorGameStateManager : MonoBehaviour {
    public static SpectatorGameStateManager Instance { get; private set; }

    // 玩家位置信息 int[0,1,2,3] → string[self,left,top,right]
    public Dictionary<int, string> indexToPosition = new Dictionary<int, string>();

    // 房间信息
    public int roomId; // 房间ID
    public string gamestateId; // 游戏状态ID
    public string roomType; // 房间规则类型
    public int selfIndex = 0; // 观战模式下，固定为0（东位视角）
    public int roomStepTime; // 步时
    public int roomRoundTime; // 局时
    public int remainTiles; // 剩余牌数
    public int currentRound; // 当前轮数
    public bool tips; // 提示
    public bool isOpenCuoHe; // 是否开启错和
    public bool isSetRandomSeed; // 是否设置随机种子

    // 玩家信息（观战模式下可以看到所有玩家的手牌）
    public Dictionary<string, PlayerInfoClass> player_to_info = new Dictionary<string, PlayerInfoClass>();

    // 调试用 于编辑器显示玩家信息列表
    [SerializeField]
    public List<PlayerInfoClass> playerInfoList = new List<PlayerInfoClass>();

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        player_to_info["self"] = new PlayerInfoClass();
        player_to_info["left"] = new PlayerInfoClass();
        player_to_info["top"] = new PlayerInfoClass();
        player_to_info["right"] = new PlayerInfoClass();
        // 调试用 显示玩家信息列表
        playerInfoList.Add(player_to_info["self"]);
        playerInfoList.Add(player_to_info["left"]);
        playerInfoList.Add(player_to_info["top"]);
        playerInfoList.Add(player_to_info["right"]);
    }

    /// <summary>
    /// 处理观战相关的服务器响应消息
    /// </summary>
    public void HandleSpectatorMessage(Response response) {
        switch (response.type) {
            case "spectator/guobiao/game_start":
            case "spectator/qingque/game_start":
                HandleGameStart(response);
                break;
            case "spectator/guobiao/broadcast_hand_action":
            case "spectator/qingque/broadcast_hand_action":
                HandleBroadcastHandAction(response);
                break;
            case "spectator/guobiao/ask_other_action":
            case "spectator/qingque/ask_other_action":
                HandleAskOtherAction(response);
                break;
            case "spectator/guobiao/do_action":
            case "spectator/qingque/do_action":
                HandleDoAction(response);
                break;
            case "spectator/guobiao/show_result":
            case "spectator/qingque/show_result":
                HandleShowResult(response);
                break;
            case "spectator/guobiao/game_end":
            case "spectator/qingque/game_end":
                HandleGameEnd(response);
                break;
            case "spectator/guobiao/full_game_record":
            case "spectator/qingque/full_game_record":
                HandleFullGameRecord(response);
                break;
            case "spectator/switch_seat":
                HandleSwitchSeat(response);
                break;
            case "spectator/refresh_player_tag_list":
                HandleRefreshPlayerTagList(response);
                break;
            case "spectator/add_spectator":
                HandleAddSpectator(response);
                break;
            case "spectator/remove_spectator":
                HandleRemoveSpectator(response);
                break;
            default:
                Debug.LogWarning($"未知的观战消息类型: {response.type}");
                break;
        }
    }

    /// <summary>
    /// 处理游戏开始响应
    /// </summary>
    private void HandleGameStart(Response response) {
        Debug.Log($"观战模式 - 游戏开始: {response.message}");
        if (response.game_info != null) {
            InitializeGame(response.success, response.message, response.game_info);
        }
    }

    /// <summary>
    /// 初始化游戏（观战模式）
    /// </summary>
    public void InitializeGame(bool success, string message, GameInfo gameInfo) {
        // 保存gamestate_id
        UserDataManager.Instance.SetGamestateId(gameInfo.gamestate_id);
        gamestateId = gameInfo.gamestate_id;
        
        // 切换到游戏场景
        WindowsManager.Instance.SwitchWindow("game");
        
        // 使用UI管理器清空临时面板
        GameSceneUIManager.Instance.ClearTemporaryPanels();
        Game3DManager.Instance.Clear3DTile(); // 清空3D手牌

        InitializeSetInfo(gameInfo); // 初始化对局数据
        GameCanvas.Instance.InitializeUIInfo(gameInfo, indexToPosition); // 初始化面板信息
        BoardCanvas.Instance.InitializeBoardInfo(gameInfo, indexToPosition); // 初始化桌面信息

        // 观战模式下，可以看到所有玩家的手牌，但以第一个玩家（索引0）的视角显示
        // 获取第一个玩家的手牌信息
        if (gameInfo.players_info != null && gameInfo.players_info.Length > 0) {
            PlayerInfo firstPlayerInfo = gameInfo.players_info[0];
            if (firstPlayerInfo.hand_tiles != null) {
                int[] handTilesArray = firstPlayerInfo.hand_tiles;
                // 初始化手牌区域（观战模式，显示第一个玩家的手牌）
                GameCanvas.Instance.ChangeHandCards("InitHandCards", 0, handTilesArray, null);
                if (handTilesArray.Length == 14) {
                    GameCanvas.Instance.ChangeHandCards("GetCard", handTilesArray[handTilesArray.Length - 1], null, null);
                }
            }
        }

        // 初始化他人手牌区域
        Game3DManager.Instance.Change3DTile("InitHandCards", 0, 0, null, false, null);

        // 根据对局信息生成所有玩家的3D卡牌（弃牌、副露、花牌）
        GenerateOtherPlayers3DTiles(gameInfo);
    }

    /// <summary>
    /// 处理手牌轮操作广播（观战模式下不显示操作按钮）
    /// </summary>
    private void HandleBroadcastHandAction(Response response) {
        Debug.Log($"观战模式 - 收到手牌轮操作信息");
        if (response.ask_hand_action_info != null) {
            AskHandActionGBInfo handresponse = response.ask_hand_action_info;
            // 观战模式下只更新UI，不显示操作按钮
            SwitchCurrentPlayer(indexToPosition[handresponse.player_index], "askHandAction", handresponse.remaining_time);
        }
    }

    /// <summary>
    /// 处理询问弃牌后操作（观战模式下不显示操作按钮）
    /// </summary>
    private void HandleAskOtherAction(Response response) {
        Debug.Log($"观战模式 - 收到询问弃牌后操作消息");
        // 观战模式下不显示操作按钮，只更新UI状态
    }

    /// <summary>
    /// 处理执行操作
    /// </summary>
    private void HandleDoAction(Response response) {
        Debug.Log($"观战模式 - 收到执行操作消息");
        if (response.do_action_info != null) {
            DoActionInfo doresponse = response.do_action_info;
            // 观战模式下使用NormalGameStateManager的DoAction方法，但不处理操作按钮
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
    }

    /// <summary>
    /// 处理显示结算结果
    /// </summary>
    private void HandleShowResult(Response response) {
        Debug.Log($"观战模式 - 收到显示结算结果消息");
        if (response.show_result_info != null) {
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
    }

    /// <summary>
    /// 处理游戏结束
    /// </summary>
    private void HandleGameEnd(Response response) {
        Debug.Log($"观战模式 - 收到游戏结束消息");
        if (response.game_end_info != null) {
            GameEndInfo gameendresponse = response.game_end_info;
            NormalGameStateManager.Instance.GameEnd(
                gameendresponse.game_random_seed,
                gameendresponse.player_final_data
            );
        }
    }

    /// <summary>
    /// 处理完整游戏记录
    /// </summary>
    private void HandleFullGameRecord(Response response) {
        Debug.Log($"观战模式 - 收到完整游戏记录");
        if (response.message_info != null && !string.IsNullOrEmpty(response.message_info.content)) {
            try {
                // 解析JSON字符串
                var recordData = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(response.message_info.content);
                Debug.Log($"完整游戏记录已接收，gamestate_id: {recordData?.GetValueOrDefault("gamestate_id")}");
                // TODO: 可以在这里处理完整游戏记录，比如保存或显示
            } catch (System.Exception e) {
                Debug.LogError($"解析完整游戏记录失败: {e.Message}");
            }
        }
    }

    /// <summary>
    /// 处理换位消息
    /// </summary>
    private void HandleSwitchSeat(Response response) {
        Debug.Log($"观战模式 - 收到换位消息");
        if (response.switch_seat_info != null) {
            NormalGameStateManager.Instance.HandleSwitchSeat(response.switch_seat_info.current_round);
        }
    }

    /// <summary>
    /// 处理刷新玩家标签列表消息
    /// </summary>
    private void HandleRefreshPlayerTagList(Response response) {
        Debug.Log($"观战模式 - 收到刷新玩家标签列表消息");
        if (response.refresh_player_tag_list_info != null) {
            RefreshPlayerTagListInfo tagInfo = response.refresh_player_tag_list_info;
            NormalGameStateManager.Instance.RefreshPlayerTagList(tagInfo.player_to_tag_list);
        }
    }

    /// <summary>
    /// 处理添加观战响应
    /// </summary>
    private void HandleAddSpectator(Response response) {
        if (response.success) {
            Debug.Log($"成功加入观战: {response.message}");
            NotificationManager.Instance?.ShowTip("观战", true, response.message);
        } else {
            Debug.LogError($"加入观战失败: {response.message}");
            NotificationManager.Instance?.ShowTip("观战", false, response.message);
            // 如果失败，返回观战面板
            WindowsManager.Instance?.SwitchWindow("spectator");
        }
    }

    /// <summary>
    /// 处理移除观战响应
    /// </summary>
    private void HandleRemoveSpectator(Response response) {
        Debug.Log($"退出观战: {response.message}");
        // 退出观战后返回观战面板
        WindowsManager.Instance?.SwitchWindow("spectator");
    }

    /// <summary>
    /// 切换玩家状态（观战模式下简化处理）
    /// </summary>
    public void SwitchCurrentPlayer(string GetCardPlayer, string SwitchType, int remaining_time) {
        if (SwitchType == "askHandAction") {
            // 只显示当前玩家，不显示操作按钮
            BoardCanvas.Instance.ShowCurrentPlayer(GetCardPlayer);
        } else if (SwitchType == "doAction") {
            // 只更新UI状态
        } else if (SwitchType == "ClearAction") {
            // 清空状态
        }
    }

    /// <summary>
    /// 生成其他玩家的3D卡牌（弃牌、副露、花牌）
    /// </summary>
    private void GenerateOtherPlayers3DTiles(GameInfo gameInfo) {
        // 遍历所有玩家
        foreach (var player in gameInfo.players_info) {
            if (!indexToPosition.ContainsKey(player.player_index)) continue;
            string position = indexToPosition[player.player_index];
            
            // 1. 生成弃牌
            if (player.discard_tiles != null && player.discard_tiles.Length > 0) {
                foreach (int tileId in player.discard_tiles) {
                    Game3DManager.Instance.Change3DTile("SetDiscardWithoutAnimation", tileId, 0, position, false, null);
                }
            }
            
            // 2. 生成花牌
            if (player.huapai_list != null && player.huapai_list.Length > 0) {
                foreach (int tileId in player.huapai_list) {
                    Game3DManager.Instance.Change3DTile("SetBuhuacardWithoutAnimation", tileId, 0, position, false, null);
                }
            }
            
            // 3. 生成副露（组合牌）
            if (player.combination_tiles != null && player.combination_tiles.Length > 0 &&
                player.combination_mask != null && player.combination_mask.Length > 0) {
                for (int i = 0; i < player.combination_tiles.Length && i < player.combination_mask.Length; i++) {
                    string combinationStr = player.combination_tiles[i];
                    if (string.IsNullOrEmpty(combinationStr) || combinationStr.Length < 2) continue;
                    
                    int[] combinationMask = player.combination_mask[i];
                    if (combinationMask == null || combinationMask.Length == 0) continue;
                    
                    int jiagangCount = 0;
                    foreach (int value in combinationMask) {
                        if (value == 3) jiagangCount++;
                    }
                    
                    if (combinationStr.Contains("k")) {
                        Game3DManager.Instance.ActionAnimation(position, "peng", combinationMask, false);
                    } else if (jiagangCount > 0) {
                        Game3DManager.Instance.ActionAnimation(position, "peng", combinationMask, false);
                        Game3DManager.Instance.ActionAnimation(position, "jiagang", combinationMask, false);
                    } else {
                        Game3DManager.Instance.ActionAnimation(position, "None", combinationMask, false);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 设置游戏信息
    /// </summary>
    private void InitializeSetInfo(GameInfo gameInfo) {
        // 观战模式下，固定以第一个玩家（索引0）的视角
        selfIndex = 0;
        roomId = gameInfo.room_id;
        roomType = gameInfo.room_type;
        roomStepTime = gameInfo.step_time;
        roomRoundTime = gameInfo.round_time;
        remainTiles = gameInfo.tile_count;
        currentRound = gameInfo.current_round;
        tips = gameInfo.tips;
        isOpenCuoHe = gameInfo.open_cuohe;
        isSetRandomSeed = gameInfo.isPlayerSetRandomSeed;

        // 根据自身索引确定其他玩家位置（固定为0）
            indexToPosition[0] = "self";
            indexToPosition[1] = "right";
            indexToPosition[2] = "top";
            indexToPosition[3] = "left";

        // 初始化玩家信息（观战模式下可以看到所有手牌）
        foreach (var player in gameInfo.players_info) {
            string position = indexToPosition[player.player_index];
            if (player_to_info.ContainsKey(position)) {
                player_to_info[position].username = player.username;
                player_to_info[position].userId = player.user_id;
                player_to_info[position].score = player.score;
                player_to_info[position].hand_tiles_count = player.hand_tiles_count;
                // 观战模式下可以看到所有手牌
                if (player.hand_tiles != null) {
                    player_to_info[position].hand_tiles = player.hand_tiles;
                }
                player_to_info[position].discard_tiles = player.discard_tiles?.ToList() ?? new List<int>();
                player_to_info[position].discard_origin_tiles = player.discard_origin_tiles?.ToList() ?? new List<int>();
                player_to_info[position].combination_tiles = player.combination_tiles?.ToList() ?? new List<string>();
                player_to_info[position].huapai_list = player.huapai_list?.ToList() ?? new List<int>();
                player_to_info[position].title_used = player.title_used;
                player_to_info[position].profile_used = player.profile_used;
                player_to_info[position].character_used = player.character_used;
                player_to_info[position].voice_used = player.voice_used;
                player_to_info[position].score_history = player.score_history?.ToList() ?? new List<string>();
                player_to_info[position].original_player_index = player.original_player_index;
                player_to_info[position].tag_list = player.tag_list;
            }
        }
    }
}
