using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;

/// <summary>
/// 极简牌谱管理器：
/// - 所有 JSON 解析与巡目分割逻辑在 GameRecordJsonDecoder 中完成；
/// - 本类只保存解析结果数据，供其它系统读取与驱动回放。
/// </summary>
public partial class GameRecordManager : MonoBehaviour {
    [SerializeField] private Button nextXunmuButton;
    [SerializeField] private Button backXunmuButton;
    [SerializeField] private Button nextStepButton;
    [SerializeField] private Button backStepButton;
    [SerializeField] private Button showGameRoundContentButton;
    [SerializeField] private Button showXunmuContentButton;
    [SerializeField] private Button showTileListButton;
    [SerializeField] private Button showGameInfoButton;
    [SerializeField] private Button showRoundInfoButton;
    [SerializeField] private GameObject recordNodeItemPrefab;
    [SerializeField] private GameObject recordRoundItemPrefab;
    [SerializeField] private ScrollRect xunmuScrollView;
    [SerializeField] private Transform recordXunmuItemContainer;
    [SerializeField] private ScrollRect roundScrollView;
    [SerializeField] private Transform recordRoundItemContainer;

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
    private int lastDiscardPlayerIndex = -1;
    // 5.当前局数
    public int currentRoundIndex;
    
    [SerializeField] private List<RecordPlayer> recordPlayerList = new List<RecordPlayer>();
    // 6.用户ID到用户名的映射
    private Dictionary<int, string> userIdToUsername = new Dictionary<int, string>();
    // 7.用户ID到分数的映射（牌谱初始化中心盘使用）
    private Dictionary<int, int> userIdToScore = new Dictionary<int, int>();
    private Dictionary<int, int> xunmuToNode = new Dictionary<int, int>();
    private List<int> xunmuNodeList = new List<int>();

    private int startIndex = -1;

    [Serializable]
    public class RecordPlayer {
        public int userId;
        public int originalPlayerIndex;
        public int playerIndex;
        public List<int> tileList = new List<int>();
        public List<int> discardTiles = new List<int>();
        public List<int> huapaiList = new List<int>();
        public List<string> combinationTiles = new List<string>();
        public List<int[]> combinationMasks = new List<int[]>();
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
        xunmuScrollView.gameObject.SetActive(false);
        roundScrollView.gameObject.SetActive(false);
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

        // 生成局数node列表
        BuildRoundNodeItems();
        // 初始化默认数据
        InitGameRound(currentRoundIndex);
    }

    // 初始化局数
    private void InitGameRound(int roundIndex) {
        // 清理3D桌面
        Game3DManager.Instance.Clear3DTile();

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
        
        int moveIndex = roundIndex % 4 - 1;
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
        GotoSelectPlayer();
        InferAndMarkStartIndex();
        BuildXunmuToNodeAndCreateItems();
    }

    // 选择选中玩家
    private void GotoSelectPlayer(bool refreshView = true){
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
            recordPlayer.discardTiles.Clear();
            recordPlayer.huapaiList.Clear();
            recordPlayer.combinationTiles.Clear();
            recordPlayer.combinationMasks.Clear();
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
                recordPlayer.tileList = new List<int>(gameRecord.gameRound.rounds[currentRoundIndex].p0Tiles);
            }
            else if (recordPlayer.originalPlayerIndex == 1){
                recordPlayer.tileList = new List<int>(gameRecord.gameRound.rounds[currentRoundIndex].p1Tiles);
            }
            else if (recordPlayer.originalPlayerIndex == 2){
                recordPlayer.tileList = new List<int>(gameRecord.gameRound.rounds[currentRoundIndex].p2Tiles);
            }
            else if (recordPlayer.originalPlayerIndex == 3){
                recordPlayer.tileList = new List<int>(gameRecord.gameRound.rounds[currentRoundIndex].p3Tiles);
            }
        }

        if (refreshView) {
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
        }

        // 初始化当前action位置
        currentPlayerIndex = 0;
        lastDiscardPlayerIndex = -1;
        BoardCanvas.Instance.ShowCurrentPlayer(indexToPosition[currentPlayerIndex]);    
    }
    
    // 选择选中巡目
    public void GotoSelectNode(int nodeIndex) {
        if (!gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData)) {
            return;
        }
        if (roundData.actionTicks == null) {
            return;
        }
        int safeTargetNode = Mathf.Clamp(nodeIndex, 0, roundData.actionTicks.Count);
        GotoAction(safeTargetNode);
    }

    // 选择选中局
    public void GotoSelectRound(int roundIndex) {
        if (!gameRecord.gameRound.rounds.ContainsKey(roundIndex)) {
            return;
        }
        currentRoundIndex = roundIndex;
        InitGameRound(roundIndex);
    }
    
    // 执行下一步行动
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
        if (currentNode == startIndex) {
            currentPlayerIndex = 0;
            BoardCanvas.Instance.ShowCurrentPlayer(indexToPosition[currentPlayerIndex]);
        }
        int previousPlayerIndex = currentPlayerIndex;
        int actingPlayerIndex = currentPlayerIndex;
        if (action == "buhua" && tick.Count >= 3) {
            actingPlayerIndex = ParseTickInt(tick, 2);
        } else if ((action == "chi_left" || action == "chi_mid" || action == "chi_right" || action == "peng" || action == "gang") && tick.Count >= 3) {
            actingPlayerIndex = ParseTickInt(tick, 2);
        }

        string currentPlayerPosition = indexToPosition[actingPlayerIndex];
        RecordPlayer currentRecordPlayer = recordPlayer_to_info[currentPlayerPosition];
        SoundManager.Instance.PlayActionSound(currentPlayerPosition, action);
        SoundManager.Instance.PlayPhysicsSound(action);
        int nextPlayerIndex = currentPlayerIndex;

        if (action == "deal") {
            int dealTile = ParseTickInt(tick, 1);
            currentRecordPlayer.tileList.Add(dealTile);
            if (currentPlayerPosition == "self") {
                GameCanvas.Instance.ChangeHandCards("GetCard", dealTile, null, null);
            } else {
                Game3DManager.Instance.Change3DTile("GetCard", dealTile, 0, currentPlayerPosition, false, null);
            }
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "cut") {
            int cutTile = ParseTickInt(tick, 1);
            bool isMoqie = ParseTickBool(tick, 2);
            int cutIndex = RemoveTileForCut(currentRecordPlayer.tileList, cutTile, isMoqie);
            currentRecordPlayer.discardTiles.Add(cutTile);

            if (currentPlayerPosition == "self") {
                if (isMoqie) {
                    GameCanvas.Instance.ChangeHandCards("RemoveGetCard", cutTile, null, null);
                } else {
                    GameCanvas.Instance.ChangeHandCards("RemoveHandCard", cutTile, null, cutIndex);
                }
            }
            Game3DManager.Instance.Change3DTile("Discard", cutTile, 0, currentPlayerPosition, isMoqie, null);
            lastDiscardPlayerIndex = actingPlayerIndex;
            nextPlayerIndex = (actingPlayerIndex + 1) % 4;
        }
        else if (action == "buhua") {
            int buhuaTile = ParseTickInt(tick, 1);
            RemoveOneTile(currentRecordPlayer.tileList, buhuaTile);
            currentRecordPlayer.huapaiList.Add(buhuaTile);
            if (currentPlayerPosition == "self") {
                GameCanvas.Instance.ChangeHandCards("RemoveBuhuaCard", buhuaTile, null, null);
            }
            Game3DManager.Instance.Change3DTile("Buhua", buhuaTile, 0, currentPlayerPosition, false, null);
            GameCanvas.Instance.ShowActionDisplay(currentPlayerPosition, "buhua");
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "angang") {
            int angangTile = ParseTickInt(tick, 1);
            RemoveNTiles(currentRecordPlayer.tileList, angangTile, 4);
            int[] combinationMask = new int[] { 2, angangTile, 2, angangTile, 2, angangTile, 2, angangTile };
            currentRecordPlayer.combinationTiles.Add($"G{angangTile}");
            currentRecordPlayer.combinationMasks.Add(combinationMask);
            if (currentPlayerPosition == "self") {
                GameCanvas.Instance.ChangeHandCards("RemoveCombinationCard", 0, new int[] { angangTile, angangTile, angangTile, angangTile }, null);
            }
            Game3DManager.Instance.Change3DTile("angang", 0, 4, currentPlayerPosition, false, combinationMask);
            GameCanvas.Instance.ShowActionDisplay(currentPlayerPosition, "angang");
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "jiagang") {
            int jiagangTile = ParseTickInt(tick, 1);
            RemoveNTiles(currentRecordPlayer.tileList, jiagangTile, 1);
            int[] combinationMask = BuildJiagangMask(currentRecordPlayer, jiagangTile);
            if (currentPlayerPosition == "self") {
                GameCanvas.Instance.ChangeHandCards("RemoveJiagangCard", jiagangTile, null, null);
            }
            Game3DManager.Instance.Change3DTile("jiagang", jiagangTile, 1, currentPlayerPosition, false, combinationMask);
            GameCanvas.Instance.ShowActionDisplay(currentPlayerPosition, "jiagang");
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "chi_left" || action == "chi_mid" || action == "chi_right" || action == "peng" || action == "gang") {
            int mingpaiTile = ParseTickInt(tick, 1);
            List<int> removedTiles = BuildRemovedTilesForMingpai(action, mingpaiTile);
            foreach (int tileId in removedTiles) {
                RemoveOneTile(currentRecordPlayer.tileList, tileId);
            }
            if (lastDiscardPlayerIndex >= 0 && indexToPosition.ContainsKey(lastDiscardPlayerIndex)) {
                string discardPlayerPosition = indexToPosition[lastDiscardPlayerIndex];
                RemoveOneTile(recordPlayer_to_info[discardPlayerPosition].discardTiles, mingpaiTile);
            }
            int discardPlayerIndex = lastDiscardPlayerIndex >= 0 ? lastDiscardPlayerIndex : previousPlayerIndex;
            int[] combinationMask = BuildMingpaiMask(action, mingpaiTile, actingPlayerIndex, discardPlayerIndex);
            currentRecordPlayer.combinationTiles.Add(BuildCombinationTarget(action, mingpaiTile));
            currentRecordPlayer.combinationMasks.Add(combinationMask);
            if (currentPlayerPosition == "self") {
                GameCanvas.Instance.ChangeHandCards("RemoveCombinationCard", 0, removedTiles.ToArray(), null);
            }
            Game3DManager.Instance.Change3DTile(action, 0, removedTiles.Count, currentPlayerPosition, false, combinationMask);
            GameCanvas.Instance.ShowActionDisplay(currentPlayerPosition, action);
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "hu_self" || action == "hu_first" || action == "hu_second" || action == "hu_third" || action == "liuju") {
            // 和牌/流局由后续面板逻辑接管，这里仅步进节点
        }

        currentPlayerIndex = nextPlayerIndex;
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

    // 跳转到下一巡目
    private void NextXunmu() {
        if (xunmuNodeList.Count == 0) return;
        int targetNode = -1;
        for (int i = 0; i < xunmuNodeList.Count; i++) {
            int node = xunmuNodeList[i];
            if (node > currentNode) {
                targetNode = node;
                break;
            }
        }
        if (targetNode < 0) return;
        GotoSelectNode(targetNode);
    }

    // 跳转到上一巡目
    private void BackXunmu() {
        if (xunmuNodeList.Count == 0) return;
        int targetNode = -1;
        for (int i = xunmuNodeList.Count - 1; i >= 0; i--) {
            int node = xunmuNodeList[i];
            if (node < currentNode) {
                targetNode = node;
                break;
            }
        }
        if (targetNode < 0) return;
        GotoSelectNode(targetNode);
    }

    // 跳转到下一步行动
    private void NextStep() {
        NextAction();
    }

    // 跳转到上一步行动
    private void BackStep() {
        GotoAction(currentNode - 1);
    }

    // 显示局数内容
    private void ShowGameRoundContent() {
        bool shouldOpenRound = !roundScrollView.gameObject.activeSelf;
        roundScrollView.gameObject.SetActive(shouldOpenRound);
        if (shouldOpenRound) {
            xunmuScrollView.gameObject.SetActive(false);
        }
    }

    // 显示巡目内容
    private void ShowXunmuContent() {
        bool shouldOpenXunmu = !xunmuScrollView.gameObject.activeSelf;
        xunmuScrollView.gameObject.SetActive(shouldOpenXunmu);
        if (shouldOpenXunmu) {
            roundScrollView.gameObject.SetActive(false);
        }
    }

    // 显示牌山
    private void ShowTileList() {
        //gameRecord.ShowTileList();
        // 直接获取全部牌山，按剩余卡牌数量计算多少卡牌需要变灰就行了
    }

    // 显示游戏信息
    private void ShowGameInfo() {
        //gameRecord.ShowGameInfo();
    }
    // 显示局数信息
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

    private int[] BuildJiagangMask(RecordPlayer recordPlayer, int jiagangTile) {
        for (int i = 0; i < recordPlayer.combinationTiles.Count; i++) {
            if (recordPlayer.combinationTiles[i] == $"k{jiagangTile}") {
                recordPlayer.combinationTiles[i] = $"g{jiagangTile}";
                List<int> updatedMask = new List<int>(recordPlayer.combinationMasks[i]);
                for (int j = 0; j < updatedMask.Count; j++) {
                    if (updatedMask[j] == 1) {
                        updatedMask.Insert(j, jiagangTile);
                        updatedMask.Insert(j, 3);
                        break;
                    }
                }
                recordPlayer.combinationMasks[i] = updatedMask.ToArray();
                return recordPlayer.combinationMasks[i];
            }
        }
        int[] fallbackMask = new int[] { 0, jiagangTile, 3, jiagangTile, 1, jiagangTile, 0, jiagangTile };
        recordPlayer.combinationTiles.Add($"g{jiagangTile}");
        recordPlayer.combinationMasks.Add(fallbackMask);
        return fallbackMask;
    }

    private List<int> BuildRemovedTilesForMingpai(string action, int tileId) {
        if (action == "chi_left") return new List<int> { tileId - 1, tileId - 2 };
        if (action == "chi_mid") return new List<int> { tileId - 1, tileId + 1 };
        if (action == "chi_right") return new List<int> { tileId + 1, tileId + 2 };
        if (action == "peng") return new List<int> { tileId, tileId };
        return new List<int> { tileId, tileId, tileId }; // gang
    }

    private string BuildCombinationTarget(string action, int tileId) {
        if (action == "chi_left") return $"s{tileId - 1}";
        if (action == "chi_mid") return $"s{tileId}";
        if (action == "chi_right") return $"s{tileId + 1}";
        if (action == "peng") return $"k{tileId}";
        return $"g{tileId}";
    }

    private int[] BuildMingpaiMask(string action, int tileId, int actionPlayerIndex, int currentDiscardPlayerIndex) {
        if (action == "chi_left") return new int[] { 1, tileId, 0, tileId - 1, 0, tileId - 2 };
        if (action == "chi_mid") return new int[] { 1, tileId, 0, tileId - 1, 0, tileId + 1 };
        if (action == "chi_right") return new int[] { 1, tileId, 0, tileId + 1, 0, tileId + 2 };

        string relative = GetRelativePosition(actionPlayerIndex, currentDiscardPlayerIndex);
        if (action == "peng") {
            if (relative == "left") return new int[] { 1, tileId, 0, tileId, 0, tileId };
            if (relative == "right") return new int[] { 0, tileId, 0, tileId, 1, tileId };
            return new int[] { 0, tileId, 1, tileId, 0, tileId }; // top
        }
        // gang
        if (relative == "left") return new int[] { 1, tileId, 0, tileId, 0, tileId, 0, tileId };
        if (relative == "right") return new int[] { 0, tileId, 0, tileId, 0, tileId, 1, tileId };
        return new int[] { 0, tileId, 1, tileId, 0, tileId, 0, tileId }; // top
    }

    private string GetRelativePosition(int selfIndex, int otherIndex) {
        if (selfIndex == 0) {
            if (otherIndex == 1) return "right";
            if (otherIndex == 2) return "top";
            if (otherIndex == 3) return "left";
            return "self";
        }
        if (selfIndex == 1) {
            if (otherIndex == 0) return "left";
            if (otherIndex == 2) return "right";
            if (otherIndex == 3) return "top";
            return "self";
        }
        if (selfIndex == 2) {
            if (otherIndex == 0) return "top";
            if (otherIndex == 1) return "left";
            if (otherIndex == 3) return "right";
            return "self";
        }
        if (selfIndex == 3) {
            if (otherIndex == 0) return "right";
            if (otherIndex == 1) return "top";
            if (otherIndex == 2) return "left";
            return "self";
        }
        return "self";
    }

    private void BuildXunmuToNodeAndCreateItems() {
        xunmuToNode.Clear();
        xunmuNodeList.Clear();
        ClearRecordNodeItems();

        if (!gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData) || roundData.actionTicks == null) {
            recordXunmuItemContainer.gameObject.SetActive(false);
            return;
        }

        int simulateCurrentPlayerIndex = 0;
        int xunmu = 0;
        int selectedIndex = selectedPlayerIndex;

        // 固定将 node=0 作为第0巡
        xunmuToNode[0] = 0;
        xunmuNodeList.Add(0);

        for (int node = 0; node < roundData.actionTicks.Count; node++) {
            List<string> tick = roundData.actionTicks[node];
            if (tick == null || tick.Count == 0) continue;
            string action = tick[0];

            if (action == "cut") {
                // node=0 已经作为第0巡，后续巡目从 node>0 开始累计
                if (simulateCurrentPlayerIndex == selectedIndex && node > 0) {
                    xunmu++;
                    xunmuToNode[xunmu] = node;
                    xunmuNodeList.Add(node);
                }
                simulateCurrentPlayerIndex = (simulateCurrentPlayerIndex + 1) % 4;
            } else if (action == "chi_left" || action == "chi_mid" || action == "chi_right" || action == "peng" || action == "gang") {
                simulateCurrentPlayerIndex = ParseTickInt(tick, 2);
            } else if (action == "buhua" && tick.Count >= 3) {
                simulateCurrentPlayerIndex = ParseTickInt(tick, 2);
            }
        }

        for (int i = 0; i < xunmuNodeList.Count; i++) {
            int xunmuIndex = i;
            int targetNode = xunmuNodeList[i];
            GameObject itemObj = Instantiate(recordNodeItemPrefab, recordXunmuItemContainer);
            RecordNodeItem item = itemObj.GetComponent<RecordNodeItem>();
            if (item != null) {
                item.Initialize(xunmuIndex, targetNode);
            }
        }
        recordXunmuItemContainer.gameObject.SetActive(xunmuNodeList.Count > 0);
    }

    private void ClearRecordNodeItems() {
        for (int i = recordXunmuItemContainer.childCount - 1; i >= 0; i--) {
            Destroy(recordXunmuItemContainer.GetChild(i).gameObject);
        }
    }

    private void BuildRoundNodeItems() {
        ClearRecordRoundItems();
        if (_roundsListForInspector == null || _roundsListForInspector.Count == 0) {
            recordRoundItemContainer.gameObject.SetActive(false);
            return;
        }
        string rule = gameRecord.gameTitle["rule"].ToString().Trim().Trim('"').ToLowerInvariant();
        Dictionary<int, int> roundToHonba = new Dictionary<int, int>();
        foreach (Round round in _roundsListForInspector) {
            int honba = 0;
            if (rule == "riichi") {
                if (!roundToHonba.ContainsKey(round.currentRound)) {
                    roundToHonba[round.currentRound] = 0;
                } else {
                    roundToHonba[round.currentRound]++;
                }
                honba = roundToHonba[round.currentRound];
            }
            GameObject itemObj = Instantiate(recordRoundItemPrefab, recordRoundItemContainer);
            Component item = itemObj.GetComponent("RecordRoundItem");
            if (item != null) {
                item.SendMessage("InitializeFromManager", $"{rule}|{round.roundIndex}|{round.currentRound}|{honba}");
            }
        }
        recordRoundItemContainer.gameObject.SetActive(true);
    }

    private void ClearRecordRoundItems() {
        for (int i = recordRoundItemContainer.childCount - 1; i >= 0; i--) {
            Destroy(recordRoundItemContainer.GetChild(i).gameObject);
        }
    }

    private int AnalyzeInitialBuhuaResetNode(Round roundData) {
        if (roundData == null || roundData.actionTicks == null || roundData.actionTicks.Count == 0) {
            return -1;
        }
        // 仅在首条行为是补花时判定“初始补花轮”
        List<string> firstTick = roundData.actionTicks[0];
        if (firstTick == null || firstTick.Count == 0 || firstTick[0] != "buhua") {
            return -1;
        }

        for (int i = 0; i < roundData.actionTicks.Count; i++) {
            List<string> tick = roundData.actionTicks[i];
            if (tick == null || tick.Count == 0) continue;
            string action = tick[0];
            if (action != "buhua" && action != "deal") {
                return i;
            }
        }
        return -1;
    }

    private void InferAndMarkStartIndex() {
        startIndex = -1;
        if (!gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData)) {
            return;
        }

        string rule = gameRecord.gameTitle["rule"].ToString().Trim().Trim('"').ToLowerInvariant();
        if (rule == "guobiao") {
            startIndex = AnalyzeInitialBuhuaResetNode(roundData);
            return;
        }
        if (rule == "qingque" || rule == "riichi") {
            return; // 暂不处理，后续规则扩展在这里接入
        }
    }
}
