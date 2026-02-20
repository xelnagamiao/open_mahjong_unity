using System.Collections;
using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public partial class GameCanvas : MonoBehaviour {
    [Header("左上房间信息")]
    [SerializeField] private Button openScoreRecordPanelButton; // 点击打开计分板的面板/按钮
    [SerializeField] private TMP_Text openScoreRecordButtonText; // 按钮文本（可不填：会自动从按钮子节点找）
    [SerializeField] private RoundPanel roundPanel; // 轮数面板引用

    [Header("玩家信息面板")]
    [SerializeField] private GamePlayerPanel playerSelfPanel;    // 自己面板
    [SerializeField] private GamePlayerPanel playerLeftPanel;    // 左边玩家面板
    [SerializeField] private GamePlayerPanel playerTopPanel;     // 上边玩家面板
    [SerializeField] private GamePlayerPanel playerRightPanel;   // 右边玩家面板

    [Header("操作界面")]
    [SerializeField] private Transform handCardsContainer; // 手牌容器（显示手牌 水平布局组）
    [SerializeField] private TMP_Text remianTimeText;        // 剩余时间文本(显示剩余时间[20+5])
    [SerializeField] public Transform ActionButtonContainer;  // 询问操作容器(显示吃,碰,杠,胡,补花,抢杠等按钮)
    [SerializeField] public Transform ActionBlockContenter;  // 询问操作内容提示(显示吃,碰,杠,胡,补花,抢杠等按钮的多种结果)

    [Header("预制体")]
    [SerializeField] private ActionButton ActionButtonPrefab;  // 询问操作按钮预制体[吃,碰,杠,胡,补花,抢杠]
    [SerializeField] private GameObject ActionBlockPrefab;  // 静态牌容器块(在操作按钮有多种结果时显示)
    [SerializeField] private GameObject StaticCardPrefab;  // 静态牌预制体(包含在多种结果显示中的图像)
    [SerializeField] private GameObject tileCardPrefab;    // 手牌预制体(可以点击的出牌图像)

    [Header("ActionDisplayPos")]
    [SerializeField] private Transform LeftActionDisplayPos;  // 左询问操作显示位置
    [SerializeField] private Transform RightActionDisplayPos;  // 右询问操作显示位置
    [SerializeField] private Transform TopActionDisplayPos;  // 上询问操作显示位置
    [SerializeField] private Transform SelfActionDisplayPos;  // 自己询问操作显示位置
    [SerializeField] private GameObject ActionDisplayText;  // 询问操作显示预制体文本

    [Header("时间配置模块")]
    private Coroutine _countdownCoroutine;
    private int _currentRemainingTime;
    private int _currentCutTime;



    [Header("游戏配置模块")]
    private bool isArranged = false; // 是否已经排列过手牌
    
    // 手牌处理队列管理
    private Queue<System.Func<Coroutine>> changeHandCardQueue = new Queue<System.Func<Coroutine>>();
    private bool isChangeHandCardProcessing = false;
    public string ActionBlockContainerState = "None"; // 操作块容器状态

    private float tileCardWidth; // 手牌预制体宽度

    public static GameCanvas Instance { get; private set; }
    private bool _isScoreRecordOpen;
    
    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // 获取tileCardPrefab的宽度 
        tileCardWidth = tileCardPrefab.GetComponent<RectTransform>().rect.width;

        // 计分板开关按钮：点击切换打开/关闭，并同步按钮文本
        if (openScoreRecordButtonText == null) openScoreRecordButtonText = openScoreRecordPanelButton.GetComponentInChildren<TMP_Text>(true);
        openScoreRecordPanelButton.onClick.RemoveAllListeners();
        openScoreRecordPanelButton.onClick.AddListener(() => {
            SetScoreRecordOpen(!_isScoreRecordOpen); // 切换状态
            if (_isScoreRecordOpen) { GameScoreRecord.Instance.gameObject.SetActive(true); GameSceneUIManager.Instance.UpdateScoreRecord(); } // 打开并由 UIManager 传入数据刷新
            else { GameScoreRecord.Instance.Close(); } // 关闭并清理
        });
        SetScoreRecordOpen(false); // 初始化按钮文字与状态
    }

    // 计分板被外部关闭时调整
    public void SetScoreRecordOpen(bool open) { _isScoreRecordOpen = open; openScoreRecordButtonText.text = _isScoreRecordOpen ? "关闭计分板" : "打开计分板"; }

    // 初始化游戏UI
    public void InitializeUIInfo(GameInfo gameInfo,Dictionary<int, string> indexToPosition){
        // 清空手牌容器 - 倒序遍历避免SetParent影响
        for (int i = handCardsContainer.childCount - 1; i >= 0; i--){
            Transform child = handCardsContainer.GetChild(i);
            Destroyer.Instance.AddToDestroyer(child);
        }
        // 通过面板组件设置玩家信息
        foreach (var player in gameInfo.players_info){
            string position = indexToPosition[player.player_index];
            GamePlayerPanel targetPanel = null;
            // 根据位置获取对应的面板
            switch (position) {
                case "self":
                    targetPanel = playerSelfPanel;
                    break;
                case "right":
                    targetPanel = playerRightPanel;
                    break;
                case "top":
                    targetPanel = playerTopPanel;
                    break;
                case "left":
                    targetPanel = playerLeftPanel;
                    break;
            }

            // 调用面板的 SetPlayerInfo 方法
            if (targetPanel != null) {
                targetPanel.SetPlayerInfo(player);
            }
            else
            {
                Debug.LogWarning($"未找到位置 {position} 对应的玩家面板");
            }
        }

        // 更新轮数面板信息
        if (roundPanel != null) {
            string roomType = NormalGameStateManager.Instance != null ? NormalGameStateManager.Instance.roomType : gameInfo.room_type;
            roundPanel.UpdateRoomInfo(gameInfo, roomType);
        } else {
            Debug.LogWarning("RoundPanel reference is not set in GameCanvas!");
        }

    }

    // 从牌谱记录初始化游戏UI
    public void InitializeUIInfoFromRecord(List<GameRecordManager.RecordPlayer> recordPlayerList, Dictionary<int, string> indexToPosition, Dictionary<int, string> userIdToUsername) {
        // 清空手牌容器 - 倒序遍历避免SetParent影响
        for (int i = handCardsContainer.childCount - 1; i >= 0; i--){
            Transform child = handCardsContainer.GetChild(i);
            Destroyer.Instance.AddToDestroyer(child);
        }
        // 通过面板组件设置玩家信息
        foreach (var recordPlayer in recordPlayerList){
            string position = indexToPosition[recordPlayer.playerIndex];
            GamePlayerPanel targetPanel = null;
            // 根据位置获取对应的面板
            switch (position) {
                case "self":
                    targetPanel = playerSelfPanel;
                    break;
                case "right":
                    targetPanel = playerRightPanel;
                    break;
                case "top":
                    targetPanel = playerTopPanel;
                    break;
                case "left":
                    targetPanel = playerLeftPanel;
                    break;
            }

            // 调用面板的 SetPlayerInfo 方法
            if (targetPanel != null) {
                // 将 RecordPlayer 转换为 PlayerInfo
                PlayerInfo playerInfo = new PlayerInfo {
                    user_id = recordPlayer.userId,
                    username = userIdToUsername.ContainsKey(recordPlayer.userId) ? userIdToUsername[recordPlayer.userId] : $"玩家{recordPlayer.userId}",
                    player_index = recordPlayer.playerIndex,
                    original_player_index = recordPlayer.originalPlayerIndex,
                    title_used = recordPlayer.title_used,
                    profile_used = recordPlayer.profile_used,
                    character_used = recordPlayer.character_used,
                    voice_used = recordPlayer.voice_used,
                    hand_tiles_count = recordPlayer.tileList != null ? recordPlayer.tileList.Count : 0,
                    hand_tiles = recordPlayer.tileList != null ? recordPlayer.tileList.ToArray() : new int[0],
                    discard_tiles = new int[0],
                    discard_origin_tiles = new int[0],
                    combination_tiles = new string[0],
                    combination_mask = new int[0][],
                    huapai_list = new int[0],
                    remaining_time = 0,
                    score = 0,
                    score_history = new string[0],
                    tag_list = new string[0]
                };
                targetPanel.SetPlayerInfo(playerInfo);
            }
            else
            {
                Debug.LogWarning($"未找到位置 {position} 对应的玩家面板");
            }
        }
    }

    // 从牌谱记录更新左上房间信息
    public void UpdateRoomInfoFromRecord(GameRecord gameRecord, int currentRoundIndex) {
        string roomType = ReadStringValue(gameRecord.gameTitle, "rule", "guobiao");
        int maxRound = ReadIntValue(gameRecord.gameTitle, "max_round", 0);
        if (maxRound <= 0) {
            int roundCount = gameRecord.gameRound.rounds.Count;
            maxRound = Mathf.Clamp((roundCount + 3) / 4, 1, 4);
        }

        int currentRound = currentRoundIndex;
        long roundRandomSeed = 0;
        if (gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData)) {
            if (roundData.currentRound > 0) {
                currentRound = roundData.currentRound;
            }
            roundRandomSeed = roundData.roundRandomSeed;
        }

        GameInfo gameInfo = new GameInfo {
            room_type = roomType,
            max_round = maxRound,
            current_round = currentRound,
            round_random_seed = roundRandomSeed,
            open_cuohe = ReadBoolValue(gameRecord.gameTitle, "open_cuohe", false),
            tips = ReadBoolValue(gameRecord.gameTitle, "tips", false),
            isPlayerSetRandomSeed = ReadBoolValue(gameRecord.gameTitle, "isPlayerSetRandomSeed", false)
        };
        roundPanel.UpdateRoomInfo(gameInfo, roomType);
    }

    private static string ReadStringValue(Dictionary<string, object> source, string key, string defaultValue) {
        if (source == null || !source.TryGetValue(key, out object value) || value == null) {
            return defaultValue;
        }
        string text = value.ToString().Trim().Trim('"');
        return string.IsNullOrEmpty(text) ? defaultValue : text.ToLowerInvariant();
    }

    private static int ReadIntValue(Dictionary<string, object> source, string key, int defaultValue) {
        if (source == null || !source.TryGetValue(key, out object value) || value == null) {
            return defaultValue;
        }
        if (value is int directInt) {
            return directInt;
        }
        return int.TryParse(value.ToString(), out int parsed) ? parsed : defaultValue;
    }

    private static bool ReadBoolValue(Dictionary<string, object> source, string key, bool defaultValue) {
        if (source == null || !source.TryGetValue(key, out object value) || value == null) {
            return defaultValue;
        }
        if (value is bool directBool) {
            return directBool;
        }

        string text = value.ToString().Trim().Trim('"');
        if (bool.TryParse(text, out bool parsedBool)) {
            return parsedBool;
        }
        if (int.TryParse(text, out int parsedInt)) {
            return parsedInt != 0;
        }
        return defaultValue;
    }

    // 更新玩家标签列表
    public void UpdatePlayerTagList(Dictionary<int, string[]> player_to_tag_list) {
        foreach (var kvp in player_to_tag_list) {
            int player_index = kvp.Key;
            string[] tag_list = kvp.Value;
            
            // 根据 player_index 找到对应的玩家位置和面板
            if (NormalGameStateManager.Instance != null && NormalGameStateManager.Instance.indexToPosition.ContainsKey(player_index)) {
                string position = NormalGameStateManager.Instance.indexToPosition[player_index];
                GamePlayerPanel targetPanel = null;
                
                // 根据位置获取对应的面板
                switch (position) {
                    case "self":
                        targetPanel = playerSelfPanel;
                        break;
                    case "right":
                        targetPanel = playerRightPanel;
                        break;
                    case "top":
                        targetPanel = playerTopPanel;
                        break;
                    case "left":
                        targetPanel = playerLeftPanel;
                        break;
                }
                
                // 更新面板的标签列表
                if (targetPanel != null) {
                    targetPanel.UpdateTagList(tag_list);
                }
            }
        }
    }

    public void ClearActionButton(){
        ActionBlockContainerState = "None";
        foreach (Transform child in ActionBlockContenter){
            Destroy(child.gameObject);
        }
        foreach (Transform child in ActionButtonContainer){
            Destroy(child.gameObject);
        }
    }

    /// <summary>
    /// 查找并触发指定 TileCard 的点击（用于自动出牌）
    /// 优先查找摸切的牌（currentGetTile == true），如果没有则查找手切的牌
    /// </summary>
    /// <param name="tileId">要查找的牌ID</param>
    /// <returns>是否成功找到并触发了点击</returns>
    public bool TriggerTileCardClick(int tileId) {
        if (handCardsContainer == null) {
            Debug.LogWarning("手牌容器为空，无法触发自动出牌");
            return false;
        }

        TileCard targetTileCard = null;

        // 优先查找摸切的牌（currentGetTile == true 且 tileId 匹配）
        for (int i = handCardsContainer.childCount - 1; i >= 0; i--) {
            Transform child = handCardsContainer.GetChild(i);
            TileCard tileCard = child.GetComponent<TileCard>();
            if (tileCard != null && tileCard.tileId == tileId && tileCard.currentGetTile) {
                targetTileCard = tileCard;
                break;
            }
        }

        // 如果没有找到摸切的牌，查找任意匹配 tileId 的牌（手切）
        if (targetTileCard == null) {
            for (int i = handCardsContainer.childCount - 1; i >= 0; i--) {
                Transform child = handCardsContainer.GetChild(i);
                TileCard tileCard = child.GetComponent<TileCard>();
                if (tileCard != null && tileCard.tileId == tileId) {
                    targetTileCard = tileCard;
                    break;
                }
            }
        }

        // 如果找到目标牌，触发点击
        if (targetTileCard != null) {
            targetTileCard.TriggerClick();
            Debug.Log($"自动出牌：触发牌ID {tileId} 的点击，摸切={targetTileCard.currentGetTile}");
            return true;
        } else {
            Debug.LogWarning($"自动出牌失败：未找到牌ID {tileId} 的 TileCard");
            return false;
        }
    }



}
