using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;

/// <summary>
/// 极简牌谱管理器：
/// - 所有 JSON 解析与巡目分割逻辑在 GameRecordJsonDecoder 中完成；
/// - 本类只保存解析结果数据，供其它系统读取与驱动回放。
/// </summary>
public class GameRecordManager : MonoBehaviour {
    [SerializeField] private Button nextXunmuButton;
    [SerializeField] private Button backXunmuButton;
    [SerializeField] private Button nextStepButton;
    [SerializeField] private Button backStepButton;
    [SerializeField] private Button showGameRoundContentButton;
    [SerializeField] private Button showXunmuContentButton;
    [SerializeField] private Button showTileListButton;
    [SerializeField] private Button showGameInfoButton;
    [SerializeField] private Button showRoundInfoButton;

    public static GameRecordManager Instance { get; private set; }
    // 1.自身玩家id 用来确定默认的选中玩家
    public int selfPlayerId { get; private set; }
    // 2.选中玩家的索引
    public int selectedPlayerIndex { get; private set; }
    public int selectedPlayerUserid { get; private set; }
    // 3.从索引获得玩家位置
    public Dictionary<int, string> indexToPosition { get; private set; } = new Dictionary<int, string>();
    // 4.从玩家位置获得玩家信息
    public Dictionary<string, RecordPlayer> recordPlayer_to_info { get; private set; } = new Dictionary<string, RecordPlayer>();
    // 5.当前操作节点
    public int currentNode;
    List<RecordPlayer> recordPlayerList = new List<RecordPlayer>();
    // 6.用户ID到用户名的映射
    private Dictionary<int, string> userIdToUsername = new Dictionary<int, string>();

    public class RecordPlayer {
        public int userId;
        public int originalPlayerIndex;
        public int playerIndex;
        public List<int> tileList;
        public int title_used;
        public int profile_used;
        public int character_used;
        public int voice_used;
    }

    public class PlayerSetting{
        public int userId;
        public int originalPlayerIndex;
        public int title_used;
        public int profile_used;
        public int character_used;
        public int voice_used;
    }

    // 存储四位玩家的设置信息
    public List<PlayerSetting> playersSettings { get; private set; } = new List<PlayerSetting>();

    private void Awake() {
        if (Instance == null) {
            Instance = this;
        } else {
            Destroy(gameObject);
        }

        // 初始化不同位置的玩家信息
        recordPlayer_to_info["self"] = new RecordPlayer();
        recordPlayer_to_info["left"] = new RecordPlayer();
        recordPlayer_to_info["top"] = new RecordPlayer();
        recordPlayer_to_info["right"] = new RecordPlayer();

        nextXunmuButton.onClick.AddListener(NextXunmu);
        backXunmuButton.onClick.AddListener(BackXunmu);
        nextStepButton.onClick.AddListener(NextStep);
        backStepButton.onClick.AddListener(BackStep);
        showGameRoundContentButton.onClick.AddListener(ShowGameRoundContent);
        showXunmuContentButton.onClick.AddListener(ShowXunmuContent);

        showTileListButton.onClick.AddListener(ShowTileList);
        showGameInfoButton.onClick.AddListener(ShowGameInfo);
        showRoundInfoButton.onClick.AddListener(ShowRoundInfo);
    }

    /// <summary>
    /// 结构化的牌谱数据（推荐使用）
    /// </summary>
    public GameRecord gameRecord { get; private set; }

    /// <summary>
    /// 用于在 Inspector 中显示牌谱数据（运行时可见）
    /// </summary>
    [SerializeField] private GameRecord _gameRecordInspector;

    /// <summary>
    /// 用于在 Inspector 中显示所有局（Dictionary 在 Inspector 中不可见，所以用 List）
    /// </summary>
    [SerializeField] private List<Round> _roundsListForInspector;

    /// <summary>
    /// 所有局的 ActionNode 列表（向后兼容，已废弃，请使用 gameRecord）。
    /// 外层索引：round 序号（从 0 开始，对应 round_index_1、2、...）。
    /// 内层元素：形如 ["cut", "55", "true"] 的字符串列表，对应一条 action_ticks 记录（不包含 "Next"）。
    /// </summary>
    [System.Obsolete("使用 gameRecord 属性获取结构化的牌谱数据")]
    public readonly List<List<List<string>>> gameRoundActionNodeList =
        new List<List<List<string>>>();

    public void HideGameRecord() {
        gameObject.SetActive(false);
    }

    // 加载牌谱
    public void LoadRecord(string recordJson, PlayerRecordInfo[] players_info = null) {
        // 清空临时面板
        GameSceneUIManager.Instance.ClearTemporaryPanels();

        // 初始化selfPlayerId，如果selectedPlayerUserid没有值，后续使用selfPlayerId作为显示玩家的默认值
        selfPlayerId = 0;
        if (UserDataManager.Instance != null && UserDataManager.Instance.UserId != 0){
            selfPlayerId = UserDataManager.Instance.UserId;
        }
        selectedPlayerUserid = 0;
        currentNode = 0;

        // 解析牌谱
        // 显示牌谱管理器
        gameObject.SetActive(true);
        // 解析牌谱头
        gameRecord = GameRecordJsonDecoder.ParseGameRecord(recordJson);
        _gameRecordInspector = gameRecord;
        // 解析牌谱局
        _roundsListForInspector = gameRecord.gameRound.GetRoundsList();
        foreach (var round in _roundsListForInspector) {
            round.UpdateActionTicksDisplay();
        }
        
        // 解析玩家信息
        playersSettings.Clear();
        userIdToUsername.Clear();
        if (players_info != null && players_info.Length > 0) {
            // 解析玩家信息
            foreach (var player in players_info) {
                PlayerSetting setting = new PlayerSetting {
                    userId = player.user_id,
                    title_used = player.title_used ?? 1,
                    profile_used = player.profile_used ?? 1,
                    character_used = player.character_used ?? 1,
                    voice_used = player.voice_used ?? 1
                };
                playersSettings.Add(setting);
                // 保存用户ID到用户名的映射
                userIdToUsername[player.user_id] = player.username;
            }
            // 根据玩家信息确定玩家原始位置
            foreach (var PlayerSetting in playersSettings){
                if (PlayerSetting.userId == Convert.ToInt32(gameRecord.gameTitle["p0_user_id"])){
                    PlayerSetting.originalPlayerIndex = 0;
                }
                else if (PlayerSetting.userId == Convert.ToInt32(gameRecord.gameTitle["p1_user_id"])){
                    PlayerSetting.originalPlayerIndex = 1;
                }
                else if (PlayerSetting.userId == Convert.ToInt32(gameRecord.gameTitle["p2_user_id"])){
                    PlayerSetting.originalPlayerIndex = 2;
                }
                else if (PlayerSetting.userId == Convert.ToInt32(gameRecord.gameTitle["p3_user_id"])){
                    PlayerSetting.originalPlayerIndex = 3;
                }
            }
        }

        // 初始化默认数据
        InitGameRound(1);
    }

    private void InitGameRound(int roundIndex) {
        // 重置局内行动节点
        currentNode = 0;

        // 初始化四个空userid的局对象，稍后进行赋值
        recordPlayerList.Clear();
        recordPlayerList.Add(new RecordPlayer{userId = Convert.ToInt32(gameRecord.gameTitle["p0_user_id"]), originalPlayerIndex = 0});
        recordPlayerList.Add(new RecordPlayer{userId = Convert.ToInt32(gameRecord.gameTitle["p1_user_id"]), originalPlayerIndex = 1});
        recordPlayerList.Add(new RecordPlayer{userId = Convert.ToInt32(gameRecord.gameTitle["p2_user_id"]), originalPlayerIndex = 2});
        recordPlayerList.Add(new RecordPlayer{userId = Convert.ToInt32(gameRecord.gameTitle["p3_user_id"]), originalPlayerIndex = 3});

        // 根据规则和回合数确定玩家初始位置
        foreach (var recordPlayer in recordPlayerList){
            string rule = Convert.ToString(gameRecord.gameTitle["rule"]).Trim('"');
            Debug.Log($"规则值: [{rule}]");
            if (rule == "guobiao" || rule == "qingque") {
                if (roundIndex >= 13) {
                    if (recordPlayer.originalPlayerIndex == 0) {
                        recordPlayer.playerIndex = 2;
                    }
                    else if (recordPlayer.originalPlayerIndex == 1) {
                        recordPlayer.playerIndex = 3;
                    }
                    else if (recordPlayer.originalPlayerIndex == 2) {
                        recordPlayer.playerIndex = 1;
                    }
                    else if (recordPlayer.originalPlayerIndex == 3) {
                        recordPlayer.playerIndex = 0;
                    }
                }
                else if (roundIndex >= 9) {
                    if (recordPlayer.originalPlayerIndex == 0) {
                        recordPlayer.playerIndex = 3;
                    }
                    else if (recordPlayer.originalPlayerIndex == 1) {
                        recordPlayer.playerIndex = 2;
                    }
                    else if (recordPlayer.originalPlayerIndex == 2) {
                        recordPlayer.playerIndex = 0;
                    }
                    else if (recordPlayer.originalPlayerIndex == 3) {
                        recordPlayer.playerIndex = 1;
                    }
                }
                else if (roundIndex >= 5) {
                    if (recordPlayer.originalPlayerIndex == 0) {
                        recordPlayer.playerIndex = 1;
                    }
                    else if (recordPlayer.originalPlayerIndex == 1) {
                        recordPlayer.playerIndex = 0;
                    }
                    else if (recordPlayer.originalPlayerIndex == 2) {
                        recordPlayer.playerIndex = 3;
                    }
                    else if (recordPlayer.originalPlayerIndex == 3) {
                        recordPlayer.playerIndex = 2;
                    }
                else {
                    if (recordPlayer.originalPlayerIndex == 0) {
                        recordPlayer.playerIndex = 0;
                    }
                    else if (recordPlayer.originalPlayerIndex == 1) {
                        recordPlayer.playerIndex = 1;
                    }
                    else if (recordPlayer.originalPlayerIndex == 2) {
                        recordPlayer.playerIndex = 2;
                    }
                    else if (recordPlayer.originalPlayerIndex == 3) {
                        recordPlayer.playerIndex = 3;
                    }
                }
            }
            else {
                Debug.LogError("规则错误");
            }
        }

        int moveIndex = roundIndex% 4 - 1;
        if (moveIndex == 0) {
            recordPlayer.playerIndex = recordPlayer.originalPlayerIndex;
        }
        else if (moveIndex == 1) {
            recordPlayer.playerIndex = BackCurrentNum(recordPlayer.originalPlayerIndex);
        }
        else if (moveIndex == 2) {
            recordPlayer.playerIndex = BackCurrentNum(BackCurrentNum(recordPlayer.originalPlayerIndex));
        }
        else if (moveIndex == 3) {
            recordPlayer.playerIndex = BackCurrentNum(BackCurrentNum(BackCurrentNum(recordPlayer.originalPlayerIndex)));
        }
        else {
            Debug.LogError("移动索引错误");
        }
    }

        refreshSelectNode();
    }

    private void refreshSelectNode(){

        // 如果没有选择userid，就使用selfuserid作为默认显示
        if (selectedPlayerUserid == 0){
            foreach(var recordPlayer in recordPlayerList){
                if (recordPlayer.userId == selfPlayerId){
                    selectedPlayerIndex = recordPlayer.playerIndex;
                }
            }
        }
        else{
            foreach(var recordPlayer in recordPlayerList){
                if (recordPlayer.userId == selectedPlayerUserid){
                    selectedPlayerIndex = recordPlayer.playerIndex;
                }
            }
        }

        // 根据选中玩家的playerindex设置每个玩家的相对位置
        if (selectedPlayerIndex == 0) {
            indexToPosition[0] = "self";
            indexToPosition[1] = "right";
            indexToPosition[2] = "top";
            indexToPosition[3] = "left";
        } else if (selectedPlayerIndex == 1) {
            indexToPosition[1] = "self";
            indexToPosition[2] = "right";
            indexToPosition[3] = "top";
            indexToPosition[0] = "left";
        } else if (selectedPlayerIndex == 2) {
            indexToPosition[2] = "self";
            indexToPosition[3] = "right";
            indexToPosition[0] = "top";
            indexToPosition[1] = "left";
        } else if (selectedPlayerIndex == 3) {
            indexToPosition[3] = "self";
            indexToPosition[0] = "right";
            indexToPosition[1] = "top";
            indexToPosition[2] = "left";
        }
        
        // 将牌谱玩家信息存储到字典中
        foreach (var recordPlayer in recordPlayerList) {
            if (indexToPosition[recordPlayer.playerIndex] == "self") {
                recordPlayer_to_info["self"] = recordPlayer;
            } else if (indexToPosition[recordPlayer.playerIndex] == "right") {
                recordPlayer_to_info["right"] = recordPlayer;
            } else if (indexToPosition[recordPlayer.playerIndex] == "top") {
                recordPlayer_to_info["top"] = recordPlayer;
            } else if (indexToPosition[recordPlayer.playerIndex] == "left") {
                recordPlayer_to_info["left"] = recordPlayer;
            }
            // 给牌谱玩家的设置赋值
            foreach (var PlayerSetting in playersSettings){
                if (PlayerSetting.userId == recordPlayer.userId){
                    recordPlayer.title_used = PlayerSetting.title_used;
                    recordPlayer.voice_used = PlayerSetting.voice_used;
                    recordPlayer.profile_used = PlayerSetting.profile_used;
                    recordPlayer.character_used = PlayerSetting.character_used;
                }
            }
        }

        // 初始化玩家UI
        if (GameCanvas.Instance != null) {
            GameCanvas.Instance.InitializeUIInfoFromRecord(recordPlayerList, indexToPosition, userIdToUsername);
        }


        
        
    }
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    /// <summary>
    /// 倒退玩家索引 用于实现回合数前进 可放心使用
    /// </summary>
    /// <param name="num">当前玩家索引 (0-3)</param>
    /// <returns>上一个玩家索引 (0-3)</returns>
    private int BackCurrentNum(int num) {
        if (num == 0) {
            return 3;
        } else {
            return num - 1;
        }
    }





    private void NextXunmu() {
        //gameRecord.NextXunmu();
    }

    private void BackXunmu() {
        //gameRecord.BackXunmu();
    }

    private void NextStep() {
        //gameRecord.NextStep();
    }

    private void BackStep() {
        //gameRecord.BackStep();
    }

    private void ShowGameRoundContent() {
        //gameRecord.ShowGameRoundContent();
    }

    private void ShowXunmuContent() {
        //gameRecord.ShowXunmuContent();
    }

    private void ShowTileList() {
        //gameRecord.ShowTileList();
    }
    
    private void ShowGameInfo() {
        //gameRecord.ShowGameInfo();
    }

    private void ShowRoundInfo() {
        //gameRecord.ShowRoundInfo();
    }
}
