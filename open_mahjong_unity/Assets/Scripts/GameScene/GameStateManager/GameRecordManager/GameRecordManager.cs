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
    public int currentPlayerIndex;
    // 5.当前局数
    public int currentRoundIndex;
    
    [SerializeField] private List<RecordPlayer> recordPlayerList = new List<RecordPlayer>();
    // 6.用户ID到用户名的映射
    private Dictionary<int, string> userIdToUsername = new Dictionary<int, string>();
    // 7.用户ID到分数的映射（牌谱初始化中心盘使用）
    private Dictionary<int, int> userIdToScore = new Dictionary<int, int>();

    [Serializable]
    public class RecordPlayer {
        public int userId;
        public int originalPlayerIndex;
        public int playerIndex;
        public List<int> tileList = new List<int>();
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
        GameSceneUIManager.Instance.InitGameRecord();

        // 初始化selfPlayerId，如果selectedPlayerUserid没有值，后续使用selfPlayerId作为显示玩家的默认值
        selfPlayerId = 0;
        if (UserDataManager.Instance != null && UserDataManager.Instance.UserId != 0){
            selfPlayerId = UserDataManager.Instance.UserId;
        }
        selectedPlayerUserid = 0;
        currentNode = 0;
        currentRoundIndex = 1;
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
        userIdToScore.Clear();
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
                userIdToScore[player.user_id] = player.score;
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
        InitGameRound();
    }

    private void InitGameRound() {
        // 重置局内行动节点
        currentNode = 0;

        // 初始化四个空userid的局对象，稍后进行赋值
        recordPlayerList.Clear();
        recordPlayerList.Add(new RecordPlayer{userId = Convert.ToInt32(gameRecord.gameTitle["p0_user_id"]), originalPlayerIndex = 0});
        recordPlayerList.Add(new RecordPlayer{userId = Convert.ToInt32(gameRecord.gameTitle["p1_user_id"]), originalPlayerIndex = 1});
        recordPlayerList.Add(new RecordPlayer{userId = Convert.ToInt32(gameRecord.gameTitle["p2_user_id"]), originalPlayerIndex = 2});
        recordPlayerList.Add(new RecordPlayer{userId = Convert.ToInt32(gameRecord.gameTitle["p3_user_id"]), originalPlayerIndex = 3});

        // 根据规则和回合数确定玩家初始位置
        string rawRule = gameRecord?.gameTitle?["rule"]?.ToString() ?? "";
        string rule = rawRule.Trim().Trim('"').Trim().ToLowerInvariant();
        Debug.Log($"规则值: [{rule}]");
        foreach (var recordPlayer in recordPlayerList){
            if (rule == "guobiao") {
                if (currentRoundIndex >= 13) {
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
                else if (currentRoundIndex >= 9) {
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
                else if (currentRoundIndex >= 5) {
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
            else if (rule == "qingque"){
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
            else {
                Debug.LogError("规则错误");
            }
        
        int moveIndex = currentRoundIndex % 4 - 1;
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

        // 给玩家设置初始手牌
        foreach (var recordPlayer in recordPlayerList){
            if (recordPlayer.originalPlayerIndex == 0){
                recordPlayer.tileList = gameRecord.gameRound.rounds[currentRoundIndex].p0Tiles;
            }
            else if (recordPlayer.originalPlayerIndex == 1){
                recordPlayer.tileList = gameRecord.gameRound.rounds[currentRoundIndex].p1Tiles;
            }
            else if (recordPlayer.originalPlayerIndex == 2){
                recordPlayer.tileList = gameRecord.gameRound.rounds[currentRoundIndex].p2Tiles;
            }
            else if (recordPlayer.originalPlayerIndex == 3){
                recordPlayer.tileList = gameRecord.gameRound.rounds[currentRoundIndex].p3Tiles;
            }
        }

        // 初始化玩家UI
        GameCanvas.Instance.InitializeUIInfoFromRecord(recordPlayerList, indexToPosition, userIdToUsername);

        // 初始化中心操作盘Controlpanel
        int remainTiles = 0;
        int displayRound = currentRoundIndex;
        if (gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round currentRoundData)) {
            remainTiles = currentRoundData.tilesList != null ? currentRoundData.tilesList.Count : 0;
            if (currentRoundData.currentRound > 0) {
                displayRound = currentRoundData.currentRound;
            }
        }
        string roomType = gameRecord.gameTitle["rule"].ToString().Trim().Trim('"').ToLowerInvariant();
        BoardCanvas.Instance.InitializeBoardInfoFromRecord(recordPlayerList, indexToPosition, userIdToScore, roomType, displayRound, remainTiles);

        // 初始化左上局数面板RoundPanel
        GameCanvas.Instance.UpdateRoomInfoFromRecord(gameRecord, currentRoundIndex);

        // 初始化手牌
        GameCanvas.Instance.ChangeHandCards("InitHandCardsFromRecord",0,recordPlayer_to_info["self"].tileList.ToArray(),null);
        Game3DManager.Instance.Change3DTile("InitHandCardsFromRecord",0,0,null,false,null);

        // 初始化当前action位置
        currentPlayerIndex = 0;
        BoardCanvas.Instance.ShowCurrentPlayer(indexToPosition[currentPlayerIndex]);




        
        
    }
    
    
    
    
    
    private void NextAction() {
        if (!gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData)) {
            return;
        }
        if (roundData.actionTicks == null || currentNode >= roundData.actionTicks.Count) {
            return;
        }

        List<string> tick = roundData.actionTicks[currentNode];
        if (tick == null || tick.Count == 0) {
            currentNode++;
            return;
        }

        string action = tick[0];
        string currentPlayerPosition = indexToPosition[currentPlayerIndex];
        RecordPlayer currentRecordPlayer = recordPlayer_to_info[currentPlayerPosition];

        if (action == "deal") {
            int dealTile = ParseTickInt(tick, 1);
            currentRecordPlayer.tileList.Add(dealTile);
            if (currentPlayerPosition == "self") {
                GameCanvas.Instance.ChangeHandCards("GetCard", dealTile, null, null);
            } else {
                Game3DManager.Instance.Change3DTile("GetCard", dealTile, 0, currentPlayerPosition, false, null);
            }
        }
        else if (action == "cut") {
            int cutTile = ParseTickInt(tick, 1);
            bool isMoqie = ParseTickBool(tick, 2);
            int cutIndex = RemoveTileForCut(currentRecordPlayer.tileList, cutTile, isMoqie);

            if (currentPlayerPosition == "self") {
                if (isMoqie) {
                    GameCanvas.Instance.ChangeHandCards("RemoveGetCard", cutTile, null, null);
                } else {
                    GameCanvas.Instance.ChangeHandCards("RemoveHandCard", cutTile, null, cutIndex);
                }
            }
            Game3DManager.Instance.Change3DTile("Discard", cutTile, 0, currentPlayerPosition, isMoqie, null);
            currentPlayerIndex = (currentPlayerIndex + 1) % 4;
        }
        else if (action == "buhua") {
            int buhuaTile = ParseTickInt(tick, 1);
            if (tick.Count >= 3) {
                currentPlayerIndex = ParseTickInt(tick, 2);
                currentPlayerPosition = indexToPosition[currentPlayerIndex];
                currentRecordPlayer = recordPlayer_to_info[currentPlayerPosition];
            }
            RemoveOneTile(currentRecordPlayer.tileList, buhuaTile);
            if (currentPlayerPosition == "self") {
                GameCanvas.Instance.ChangeHandCards("RemoveBuhuaCard", buhuaTile, null, null);
            }
            Game3DManager.Instance.Change3DTile("Buhua", buhuaTile, 0, currentPlayerPosition, false, null);
            GameCanvas.Instance.ShowActionDisplay(currentPlayerPosition, "buhua");
        }
        else if (action == "angang") {
            int angangTile = ParseTickInt(tick, 1);
            RemoveNTiles(currentRecordPlayer.tileList, angangTile, 4);
            if (currentPlayerPosition == "self") {
                GameCanvas.Instance.ChangeHandCards("RemoveCombinationCard", 0, new int[] { angangTile, angangTile, angangTile, angangTile }, null);
            }
            GameCanvas.Instance.ShowActionDisplay(currentPlayerPosition, "angang");
        }
        else if (action == "jiagang") {
            int jiagangTile = ParseTickInt(tick, 1);
            RemoveNTiles(currentRecordPlayer.tileList, jiagangTile, 1);
            if (currentPlayerPosition == "self") {
                GameCanvas.Instance.ChangeHandCards("RemoveJiagangCard", jiagangTile, null, null);
            }
            GameCanvas.Instance.ShowActionDisplay(currentPlayerPosition, "jiagang");
        }
        else if (action == "chi_left" || action == "chi_mid" || action == "chi_right" || action == "peng" || action == "gang") {
            int mingpaiTile = ParseTickInt(tick, 1);
            int actionPlayerIndex = ParseTickInt(tick, 2);
            string actionPlayerPosition = indexToPosition[actionPlayerIndex];
            RecordPlayer actionRecordPlayer = recordPlayer_to_info[actionPlayerPosition];

            int removeCount = 2;
            if (action == "gang") {
                removeCount = 3;
            }
            RemoveNTiles(actionRecordPlayer.tileList, mingpaiTile, removeCount);
            if (actionPlayerPosition == "self") {
                int[] removeTiles = new int[removeCount];
                for (int i = 0; i < removeCount; i++) {
                    removeTiles[i] = mingpaiTile;
                }
                GameCanvas.Instance.ChangeHandCards("RemoveCombinationCard", 0, removeTiles, null);
            }
            GameCanvas.Instance.ShowActionDisplay(actionPlayerPosition, action);
            currentPlayerIndex = actionPlayerIndex;
        }
        else if (action == "hu_self" || action == "hu_first" || action == "hu_second" || action == "hu_third" || action == "liuju") {
            // 和牌/流局由后续面板逻辑接管，这里仅步进节点
        }

        currentNode++;
        BoardCanvas.Instance.ShowCurrentPlayer(indexToPosition[currentPlayerIndex]);
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
        NextAction();
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

    private int ParseTickInt(List<string> tick, int index) {
        if (index >= tick.Count) return 0;
        return int.Parse(tick[index]);
    }

    private bool ParseTickBool(List<string> tick, int index) {
        if (index >= tick.Count) return false;
        return tick[index].ToLowerInvariant() == "true";
    }

    private int RemoveTileForCut(List<int> tileList, int tileId, bool isMoqie) {
        if (tileList.Count == 0) return 0;
        if (isMoqie) {
            int lastIndex = tileList.Count - 1;
            if (tileList[lastIndex] == tileId) {
                tileList.RemoveAt(lastIndex);
                return lastIndex;
            }
        }
        int index = tileList.IndexOf(tileId);
        if (index < 0) {
            return 0;
        }
        tileList.RemoveAt(index);
        return index;
    }

    private void RemoveOneTile(List<int> tileList, int tileId) {
        int index = tileList.IndexOf(tileId);
        if (index >= 0) {
            tileList.RemoveAt(index);
        }
    }

    private void RemoveNTiles(List<int> tileList, int tileId, int count) {
        for (int i = 0; i < count; i++) {
            int index = tileList.IndexOf(tileId);
            if (index < 0) break;
            tileList.RemoveAt(index);
        }
    }
}
