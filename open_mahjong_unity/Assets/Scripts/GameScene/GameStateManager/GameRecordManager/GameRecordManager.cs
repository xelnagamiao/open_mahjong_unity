using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;
using TMPro;

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
    [SerializeField] private TMP_Text currentRoundText;
    [SerializeField] private Button showXunmuContentButton;
    [SerializeField] private TMP_Text currentXunmuText;
    [SerializeField] private Button showTileListButton;
    [SerializeField] private Button showGameInfoButton;
    [SerializeField] private Button showRoundInfoButton;
    [SerializeField] private Button quitRecordButton;
    [SerializeField] private GameObject recordNodeItemPrefab;
    [SerializeField] private GameObject recordRoundItemPrefab;
    [SerializeField] private ScrollRect xunmuScrollView;
    [SerializeField] private Transform recordXunmuItemContainer;
    [SerializeField] private ScrollRect roundScrollView;
    [SerializeField] private Transform recordRoundItemContainer;
    [SerializeField] private Transform tileListContainer;
    [SerializeField] private GameObject tileListView;
    [SerializeField] private GameObject staticCardPrefab;
    [SerializeField] private GameObject gameInfoView;
    [SerializeField] private TMP_Text gameInfoText;
    [SerializeField] private GameObject roundInfoView;
    [SerializeField] private TMP_Text roundInfoText;

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
    
    // 牌山管理：当前牌山列表（动态变化）和原始牌山列表（不变）
    private List<int> currentTilesList = new List<int>();
    private List<int> originalTilesList = new List<int>();
    private int consumedFromFront = 0;  // 已从牌山头部摸走的张数
    private HashSet<int> consumedBackIndices = new HashSet<int>(); // 已从牌山尾部摸走的原始索引集合
    private List<int> currentOriginalIndices = new List<int>(); // currentTilesList 中每个位置对应的原始索引
    private string backwardTilesType = "double"; // 倒序摸牌状态：double=摸倒数第二张 single=摸倒数第一张

    // 牌谱短码 → 完整动作名
    private static readonly Dictionary<string, string> RecordToDisplay = new Dictionary<string, string> {
        {"bh", "buhua"}, {"c", "cut"}, {"ag", "angang"}, {"jg", "jiagang"},
        {"cl", "chi_left"}, {"cm", "chi_mid"}, {"cr", "chi_right"},
        {"p", "peng"}, {"g", "gang"},
    };
    private static string ToDisplayAction(string recordAction) {
        return RecordToDisplay.TryGetValue(recordAction, out string display) ? display : recordAction;
    }
    private List<StaticCard> tileListCards = new List<StaticCard>(); // 牌山视图中的卡牌组件引用

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
        quitRecordButton.onClick.AddListener(QuitRecord);
    }

    /// <summary>
    /// 结构化的牌谱数据
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
        GameSceneMouseInputController.Instance.SetState("recordstate");

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
        tileListView.SetActive(false);
        gameInfoView.SetActive(false);
        roundInfoView.SetActive(false);
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
                if (PlayerSetting.userId == Convert.ToInt32(gameRecord.gameTitle["p0_uid"])){
                    PlayerSetting.originalPlayerIndex = 0;
                }
                else if (PlayerSetting.userId == Convert.ToInt32(gameRecord.gameTitle["p1_uid"])){
                    PlayerSetting.originalPlayerIndex = 1;
                }
                else if (PlayerSetting.userId == Convert.ToInt32(gameRecord.gameTitle["p2_uid"])){
                    PlayerSetting.originalPlayerIndex = 2;
                }
                else if (PlayerSetting.userId == Convert.ToInt32(gameRecord.gameTitle["p3_uid"])){
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
        recordPlayerList.Add(new RecordPlayer{userId = Convert.ToInt32(gameRecord.gameTitle["p0_uid"]), originalPlayerIndex = 0});
        recordPlayerList.Add(new RecordPlayer{userId = Convert.ToInt32(gameRecord.gameTitle["p1_uid"]), originalPlayerIndex = 1});
        recordPlayerList.Add(new RecordPlayer{userId = Convert.ToInt32(gameRecord.gameTitle["p2_uid"]), originalPlayerIndex = 2});
        recordPlayerList.Add(new RecordPlayer{userId = Convert.ToInt32(gameRecord.gameTitle["p3_uid"]), originalPlayerIndex = 3});

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
                Debug.LogWarning("移动索引错误");
            }
        }
        
        // 初始化牌山列表（当前牌山和原始牌山）
        if (gameRecord.gameRound.rounds.TryGetValue(roundIndex, out Round roundData)) {
            originalTilesList = roundData.tilesList != null ? new List<int>(roundData.tilesList) : new List<int>();
            currentTilesList = new List<int>(originalTilesList);
            consumedFromFront = 0;
            consumedBackIndices = new HashSet<int>();
            currentOriginalIndices = new List<int>();
            for (int i = 0; i < originalTilesList.Count; i++) currentOriginalIndices.Add(i);
            backwardTilesType = "double";
        } else {
            originalTilesList = new List<int>();
            currentTilesList = new List<int>();
            consumedFromFront = 0;
            consumedBackIndices = new HashSet<int>();
            currentOriginalIndices = new List<int>();
            backwardTilesType = "double";
        }

        BuildTileListInContainer(); // 初始化牌山视图
        GotoSelectPlayer(); // 选择选中玩家
        InferAndMarkStartIndex(); // 推断并标记初始行动节点
        BuildXunmuToNodeAndCreateItems(); // 构建巡目到节点并创建巡目项
        RefreshCurrentRecordTexts(); // 刷新当前局数和巡目文本
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
            if (recordPlayer.playerIndex == 0){
                recordPlayer.tileList = new List<int>(gameRecord.gameRound.rounds[currentRoundIndex].p0Tiles);
            }
            else if (recordPlayer.playerIndex == 1){
                recordPlayer.tileList = new List<int>(gameRecord.gameRound.rounds[currentRoundIndex].p1Tiles);
            }
            else if (recordPlayer.playerIndex == 2){
                recordPlayer.tileList = new List<int>(gameRecord.gameRound.rounds[currentRoundIndex].p2Tiles);
            }
            else if (recordPlayer.playerIndex == 3){
                recordPlayer.tileList = new List<int>(gameRecord.gameRound.rounds[currentRoundIndex].p3Tiles);
            }
        }

        if (refreshView) {
            // 初始化玩家UI
            GameCanvas.Instance.InitializeUIInfoFromRecord(recordPlayerList, indexToPosition, userIdToUsername);

            // 初始化中心操作盘Controlpanel
            int startRemainTiles = 0;
            int displayRound = currentRoundIndex;
            if (gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round currentRoundData)) {
                startRemainTiles = currentRoundData.tilesList != null ? currentRoundData.tilesList.Count : 0;
                if (currentRoundData.currentRound > 0) {
                    displayRound = currentRoundData.currentRound;
                }
            }
            string roomType = gameRecord.gameTitle["rule"].ToString().Trim().Trim('"').ToLowerInvariant();
            BoardCanvas.Instance.InitializeBoardInfoFromRecord(recordPlayerList, indexToPosition, userIdToScore, roomType, displayRound, startRemainTiles);

            // 初始化左上局数面板RoundPanel
            GameCanvas.Instance.UpdateRoomInfoFromRecord(gameRecord, currentRoundIndex);

            // 初始化手牌
            GameCanvas.Instance.ChangeHandCards("InitHandCardsFromRecord",0,recordPlayer_to_info["self"].tileList.ToArray(),null);
            Game3DManager.Instance.Change3DTile("InitHandCardsFromRecord",0,0,null,false,null);
        }

        // 初始化当前action位置
        currentPlayerIndex = 0;
        lastDiscardPlayerIndex = -1;
        BoardCanvas.Instance.ShowCurrentPlayer(indexToPosition[currentPlayerIndex], currentTilesList.Count);    
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

    /// <summary>
    /// 切换到指定用户的牌谱视角，并保持在当前节点。
    /// </summary>
    public void SwitchRecordPerspectiveToUser(int userId) {
        if (userId == 0 || gameRecord == null) return;
        selectedPlayerUserid = userId;
        int targetNode = currentNode;
        GotoSelectPlayer(true);
        GotoAction(targetNode);
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
            UpdateCurrentXunmuText();
            return;
        }

        string action = tick[0];
        if (currentNode == startIndex) {
            currentPlayerIndex = 0;
            BoardCanvas.Instance.ShowCurrentPlayer(indexToPosition[currentPlayerIndex], currentTilesList.Count);
        }
        int previousPlayerIndex = currentPlayerIndex;
        int actingPlayerIndex = currentPlayerIndex;
        if (action == "bh" && tick.Count >= 3) {
            actingPlayerIndex = ParseTickInt(tick, 2);
        } else if ((action == "cl" || action == "cm" || action == "cr" || action == "p" || action == "g") && tick.Count >= 3) {
            actingPlayerIndex = ParseTickInt(tick, 2);
        } else if ((action == "hu_self" || action == "hu_first" || action == "hu_second" || action == "hu_third") && tick.Count >= 4) {
            actingPlayerIndex = ParseTickInt(tick, 3);
        }

        string currentPlayerPosition = indexToPosition[actingPlayerIndex];
        RecordPlayer currentRecordPlayer = recordPlayer_to_info[currentPlayerPosition];
        string displayAction = ToDisplayAction(action);
        SoundManager.Instance.PlayActionSound(currentPlayerPosition, displayAction);
        SoundManager.Instance.PlayPhysicsSound(displayAction);
        int nextPlayerIndex = currentPlayerIndex;

        if (action == "d" || action == "gd" || action == "bd") {
            int dealTile = ParseTickInt(tick, 1);
            currentRecordPlayer.tileList.Add(dealTile);
            
            if (currentTilesList.Count > 0) {
                if (action == "gd" || action == "bd") {
                    int removePos;
                    if (backwardTilesType == "double" && currentTilesList.Count > 1) {
                        removePos = currentTilesList.Count - 2;
                    } else {
                        removePos = currentTilesList.Count - 1;
                    }
                    int origIdx = currentOriginalIndices[removePos];
                    currentTilesList.RemoveAt(removePos);
                    currentOriginalIndices.RemoveAt(removePos);
                    consumedBackIndices.Add(origIdx);
                    backwardTilesType = backwardTilesType == "double" ? "single" : "double";
                } else {
                    currentTilesList.RemoveAt(0);
                    currentOriginalIndices.RemoveAt(0);
                    consumedFromFront++;
                }
            }
            
            if (currentPlayerPosition == "self") {
                GameCanvas.Instance.ChangeHandCards("GetCard", dealTile, null, null);
            } else {
                Game3DManager.Instance.Change3DTile("GetCard", dealTile, 0, currentPlayerPosition, false, null);
            }
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "c") {
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
        else if (action == "bh") {
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
        else if (action == "ag") {
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
        else if (action == "jg") {
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
        else if (action == "cl" || action == "cm" || action == "cr" || action == "p" || action == "g") {
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
            Game3DManager.Instance.Change3DTile(displayAction, 0, removedTiles.Count, currentPlayerPosition, false, combinationMask);
            GameCanvas.Instance.ShowActionDisplay(currentPlayerPosition, displayAction);
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "hu_self" || action == "hu_first" || action == "hu_second" || action == "hu_third") {
            int huScore = ParseTickInt(tick, 1);
            string[] huFan = ParseHuFanList(tick, 2);
            int hepaiPlayerIndex = tick.Count >= 4 ? ParseTickInt(tick, 3) : actingPlayerIndex;
            ShowRecordResult(action, huScore, huFan, hepaiPlayerIndex);
            GameSceneUIManager.Instance.UpdateScoreRecord();
        }
        else if (action == "liuju") {
            GameSceneUIManager.Instance.ShowEndLiuju();
            StartCoroutine(GotoNextRoundAfterDelay(2f));
        }

        currentPlayerIndex = nextPlayerIndex;
        currentNode++;
        BoardCanvas.Instance.ShowCurrentPlayer(indexToPosition[currentPlayerIndex], currentTilesList.Count);
        RefreshTileListViewIfVisible();
        UpdateCurrentXunmuText();
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


    /// <summary>
    /// 在 tileListContainer 中根据 originalTilesList 生成所有牌山卡牌（GameInit/InitGameRound 时调用）
    /// </summary>
    private void BuildTileListInContainer() {
        if (tileListContainer == null || staticCardPrefab == null) return;
        tileListCards.Clear();
        for (int i = tileListContainer.childCount - 1; i >= 0; i--) {
            Destroy(tileListContainer.GetChild(i).gameObject);
        }
        for (int i = 0; i < originalTilesList.Count; i++) {
            GameObject cardObj = Instantiate(staticCardPrefab, tileListContainer);
            StaticCard sc = cardObj.GetComponent<StaticCard>();
            if (sc != null) {
                sc.SetTileOnlyImage(originalTilesList[i]);
                tileListCards.Add(sc);
            }
        }
        UpdateTileListOpacity();
    }

    /// <summary>
    /// 根据已摸走的头部/尾部张数，对牌山视图中的卡牌设置不透明度（已摸走的变灰）
    /// </summary>
    private void UpdateTileListOpacity() {
        const float dimmedAlpha = 0.4f;
        for (int i = 0; i < tileListCards.Count; i++) {
            bool isConsumed = i < consumedFromFront || consumedBackIndices.Contains(i);
            tileListCards[i].SetOpacity(isConsumed ? dimmedAlpha : 1f);
        }
    }

    /// <summary>
    /// 若牌山视图当前显示，则刷新不透明度（步进/回退后调用）
    /// </summary>
    private void RefreshTileListViewIfVisible() {
        if (tileListView != null && tileListView.activeSelf) {
            UpdateTileListOpacity();
        }
    }

    /// <summary>
    /// 刷新当前局文本与当前巡文本
    /// </summary>
    private void RefreshCurrentRecordTexts() {
        UpdateCurrentRoundText();
        UpdateCurrentXunmuText();
    }

    /// <summary>
    /// 更新当前局显示文本（东风东、东风南……北风北）
    /// </summary>
    private void UpdateCurrentRoundText() {
        if (currentRoundText == null) return;
        int roundNo = currentRoundIndex;
        if (gameRecord != null &&
            gameRecord.gameRound != null &&
            gameRecord.gameRound.rounds != null &&
            gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData) &&
            roundData.currentRound > 0) {
            roundNo = roundData.currentRound;
        }
        string rule = ReadGameTitleString(gameRecord != null ? gameRecord.gameTitle : null, "rule", "guobiao").ToLowerInvariant();
        currentRoundText.text = FormatRoundText(rule, roundNo);
    }

    /// <summary>
    /// 更新当前巡显示文本（x巡）
    /// </summary>
    private void UpdateCurrentXunmuText() {
        if (currentXunmuText == null) return;
        int xunmu = 0;
        for (int i = 0; i < xunmuNodeList.Count; i++) {
            if (xunmuNodeList[i] <= currentNode) {
                xunmu = i;
            } else {
                break;
            }
        }
        currentXunmuText.text = $"{xunmu}巡";
    }

    private string FormatRoundText(string rule, int roundNo) {
        return RoundTextDictionary.GetRoundName(rule, roundNo);
    }

    /// <summary>
    /// 从 gameTitle 字典读取字符串
    /// </summary>
    private static string ReadGameTitleString(Dictionary<string, object> gameTitle, string key, string defaultValue = "") {
        if (gameTitle == null || !gameTitle.TryGetValue(key, out object value) || value == null)
            return defaultValue;
        return value.ToString().Trim().Trim('"');
    }

    /// <summary>
    /// 从 gameTitle 字典读取整数
    /// </summary>
    private static int ReadGameTitleInt(Dictionary<string, object> gameTitle, string key, int defaultValue = 0) {
        if (gameTitle == null || !gameTitle.TryGetValue(key, out object value) || value == null)
            return defaultValue;
        return int.TryParse(value.ToString(), out int v) ? v : defaultValue;
    }

    /// <summary>
    /// 从 gameTitle 字典读取布尔
    /// </summary>
    private static bool ReadGameTitleBool(Dictionary<string, object> gameTitle, string key, bool defaultValue = false) {
        if (gameTitle == null || !gameTitle.TryGetValue(key, out object value) || value == null)
            return defaultValue;
        string s = value.ToString().Trim().Trim('"').ToLowerInvariant();
        return s == "true" || s == "1";
    }

    /// <summary>
    /// 生成游戏信息文案（规则、局数、玩家ID、选项等）
    /// </summary>
    private string BuildGameInfoString() {
        if (gameRecord?.gameTitle == null) return "暂无游戏信息";
        var gt = gameRecord.gameTitle;
        string rule = "未知规则";
        if (!string.IsNullOrEmpty(ReadGameTitleString(gt, "rule", "guobiao"))) {
            rule = "国标";
        } else if (!string.IsNullOrEmpty(ReadGameTitleString(gt, "rule", "qingque"))) {
            rule = "青雀";
        } else if (!string.IsNullOrEmpty(ReadGameTitleString(gt, "rule", "riichi"))) {
            rule = "立直";
        } else {
            rule = "未知规则";
        }
        int maxRound = ReadGameTitleInt(gt, "max_round", 0);
        if (maxRound <= 0 && gameRecord.gameRound != null) {
            maxRound = Mathf.Max(1, gameRecord.gameRound.rounds?.Count ?? 0);
        }
        int p0 = ReadGameTitleInt(gt, "p0_uid", 0);
        int p1 = ReadGameTitleInt(gt, "p1_uid", 0);
        int p2 = ReadGameTitleInt(gt, "p2_uid", 0);
        int p3 = ReadGameTitleInt(gt, "p3_uid", 0);
        bool openCuohe = ReadGameTitleBool(gt, "open_cuohe", false);
        bool tips = ReadGameTitleBool(gt, "tips", false);
        bool isPlayerSetRandomSeed = ReadGameTitleBool(gt, "isPlayerSetRandomSeed", false);
        string p0Name = userIdToUsername.TryGetValue(p0, out string n0) ? n0 : p0.ToString();
        string p1Name = userIdToUsername.TryGetValue(p1, out string n1) ? n1 : p1.ToString();
        string p2Name = userIdToUsername.TryGetValue(p2, out string n2) ? n2 : p2.ToString();
        string p3Name = userIdToUsername.TryGetValue(p3, out string n3) ? n3 : p3.ToString();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("【游戏信息】");
        sb.AppendLine($"规则: {rule}");
        sb.AppendLine($"最大局数: {maxRound}局");
        sb.AppendLine($"玩家0: {p0Name} (ID:{p0})");
        sb.AppendLine($"玩家1: {p1Name} (ID:{p1})");
        sb.AppendLine($"玩家2: {p2Name} (ID:{p2})");
        sb.AppendLine($"玩家3: {p3Name} (ID:{p3})");
        sb.AppendLine($"错和: {(openCuohe ? "开" : "关")}");
        sb.AppendLine($"提示: {(tips ? "开" : "关")}");
        sb.AppendLine($"复式: {(isPlayerSetRandomSeed ? "开" : "关")}");
        sb.AppendLine($"随机种子: {gameRecord.gameTitle["game_random_seed"]}");
        sb.AppendLine($"开始时间: {gameRecord.gameTitle["start_time"]}");
        sb.AppendLine($"结束时间: {gameRecord.gameTitle["end_time"]}");
        return sb.ToString();
    }

    /// <summary>
    /// 生成当前局信息文案（局序号、当前圈、种子、手牌数、行动数等）
    /// </summary>
    private string BuildRoundInfoString() {
        if (gameRecord?.gameRound?.rounds == null) return "暂无局信息";
        if (!gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round round)) {
            return $"当前局索引 {currentRoundIndex} 无数据";
        }
        int handCount = (round.p0Tiles?.Count ?? 0) + (round.p1Tiles?.Count ?? 0) + (round.p2Tiles?.Count ?? 0) + (round.p3Tiles?.Count ?? 0);
        int actionCount = round.actionTicks?.Count ?? 0;
        int tileWallCount = round.tilesList?.Count ?? 0;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("【局信息】");
        sb.AppendLine($"局数序号: {round.roundIndex}");
        sb.AppendLine($"当前局数: {round.currentRound}");
        sb.AppendLine($"随机种子: {round.roundRandomSeed}");
        sb.AppendLine($"牌山张数: {tileWallCount}");
        sb.AppendLine($"行动总数: {actionCount}");
        sb.AppendLine($"当前行动: {currentNode} / {actionCount}");
        return sb.ToString();
    }

    private int ParseTickInt(List<string> tick, int index) {
        if (index >= tick.Count) return 0;
        return int.Parse(tick[index]);
    }

    private bool ParseTickBool(List<string> tick, int index) {
        if (index >= tick.Count) return false;
        string val = tick[index].ToLowerInvariant();
        return val == "true" || val == "t";
    }

    private string[] ParseHuFanList(List<string> tick, int index) {
        if (index >= tick.Count || string.IsNullOrEmpty(tick[index])) return new string[0];
        string raw = tick[index].Trim();
        if (raw.StartsWith("[") && raw.EndsWith("]") && raw.Length >= 2) {
            raw = raw.Substring(1, raw.Length - 2);
        }
        string[] split = raw.Split(',');
        List<string> fans = new List<string>();
        for (int i = 0; i < split.Length; i++) {
            string fan = split[i].Trim().Trim('"').Trim('\'');
            if (!string.IsNullOrEmpty(fan)) {
                fans.Add(fan);
            }
        }
        return fans.ToArray();
    }

    private void ShowRecordResult(string huClass, int huScore, string[] huFan, int hepaiPlayerIndex) {
        Dictionary<string, string> positionToUsername = new Dictionary<string, string>();
        foreach (var kv in recordPlayer_to_info) {
            int uid = kv.Value.userId;
            if (userIdToUsername.TryGetValue(uid, out string username)) {
                positionToUsername[kv.Key] = username;
            } else {
                positionToUsername[kv.Key] = uid.ToString();
            }
        }

        string roomType = ReadGameTitleString(gameRecord.gameTitle, "rule", "guobiao").ToLowerInvariant();
        if (indexToPosition.ContainsKey(hepaiPlayerIndex)) {
            string huPosition = indexToPosition[hepaiPlayerIndex];
            GameCanvas.Instance.ShowActionDisplay(huPosition, huClass);
        }
        GameSceneUIManager.Instance.ShowRecordResult(hepaiPlayerIndex, huScore, huFan, huClass, roomType, indexToPosition, positionToUsername);
    }

    /// <summary>
    /// 牌谱流局后延迟切换下一局
    /// </summary>
    private System.Collections.IEnumerator GotoNextRoundAfterDelay(float delaySeconds) {
        yield return new WaitForSeconds(delaySeconds);
        int nextRound = currentRoundIndex + 1;
        if (gameRecord != null &&
            gameRecord.gameRound != null &&
            gameRecord.gameRound.rounds != null &&
            gameRecord.gameRound.rounds.ContainsKey(nextRound)) {
            GotoSelectRound(nextRound);
        }
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
        if (action == "cl") return new List<int> { tileId - 1, tileId - 2 };
        if (action == "cm") return new List<int> { tileId - 1, tileId + 1 };
        if (action == "cr") return new List<int> { tileId + 1, tileId + 2 };
        if (action == "p") return new List<int> { tileId, tileId };
        return new List<int> { tileId, tileId, tileId }; // g
    }

    private string BuildCombinationTarget(string action, int tileId) {
        if (action == "cl") return $"s{tileId - 1}";
        if (action == "cm") return $"s{tileId}";
        if (action == "cr") return $"s{tileId + 1}";
        if (action == "p") return $"k{tileId}";
        return $"g{tileId}";
    }

    private int[] BuildMingpaiMask(string action, int tileId, int actionPlayerIndex, int currentDiscardPlayerIndex) {
        if (action == "cl") return new int[] { 1, tileId, 0, tileId - 1, 0, tileId - 2 };
        if (action == "cm") return new int[] { 1, tileId, 0, tileId - 1, 0, tileId + 1 };
        if (action == "cr") return new int[] { 1, tileId, 0, tileId + 1, 0, tileId + 2 };

        string relative = GetRelativePosition(actionPlayerIndex, currentDiscardPlayerIndex);
        if (action == "p") {
            if (relative == "left") return new int[] { 1, tileId, 0, tileId, 0, tileId };
            if (relative == "right") return new int[] { 0, tileId, 0, tileId, 1, tileId };
            return new int[] { 0, tileId, 1, tileId, 0, tileId }; // top
        }
        // g
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

            if (action == "c") {
                if (simulateCurrentPlayerIndex == selectedIndex && node > 0) {
                    xunmu++;
                    xunmuToNode[xunmu] = node;
                    xunmuNodeList.Add(node);
                }
                simulateCurrentPlayerIndex = (simulateCurrentPlayerIndex + 1) % 4;
            } else if (action == "cl" || action == "cm" || action == "cr" || action == "p" || action == "g") {
                simulateCurrentPlayerIndex = ParseTickInt(tick, 2);
            } else if (action == "bh" && tick.Count >= 3) {
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
        if (recordRoundItemPrefab == null) {
            Debug.LogError("BuildRoundNodeItems: recordRoundItemPrefab 未分配，无法创建局数按钮。");
            return;
        }
        
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
            RecordRoundItem item = itemObj.GetComponent<RecordRoundItem>();
            if (item != null) {
                item.Initialize(rule, round.roundIndex, round.currentRound, honba);
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
        if (firstTick == null || firstTick.Count == 0 || firstTick[0] != "bh") {
            return -1;
        }

        for (int i = 0; i < roundData.actionTicks.Count; i++) {
            List<string> tick = roundData.actionTicks[i];
            if (tick == null || tick.Count == 0) continue;
            string action = tick[0];
            if (action != "bh" && action != "bd") {
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

    /// <summary>
    /// 从牌谱构建带 score_history 的玩家数据并刷新计分表。按局顺序汇总四家 score_history（格式如 "+24","-8","0"），original_player_index 为 0～3。
    /// </summary>
    public void RefreshRecordScoreTable() {
        if (gameRecord?.gameRound?.rounds == null || GameScoreRecord.Instance == null) return;
        string rule = ReadGameTitleString(gameRecord.gameTitle, "rule", "guobiao").ToLowerInvariant();
        int p0Id = ReadGameTitleInt(gameRecord.gameTitle, "p0_uid", 0);
        int p1Id = ReadGameTitleInt(gameRecord.gameTitle, "p1_uid", 0);
        int p2Id = ReadGameTitleInt(gameRecord.gameTitle, "p2_uid", 0);
        int p3Id = ReadGameTitleInt(gameRecord.gameTitle, "p3_uid", 0);
        string name0 = userIdToUsername.TryGetValue(p0Id, out string n0) ? n0 : p0Id.ToString();
        string name1 = userIdToUsername.TryGetValue(p1Id, out string n1) ? n1 : p1Id.ToString();
        string name2 = userIdToUsername.TryGetValue(p2Id, out string n2) ? n2 : p2Id.ToString();
        string name3 = userIdToUsername.TryGetValue(p3Id, out string n3) ? n3 : p3Id.ToString();
        var hist0 = new List<string>();
        var hist1 = new List<string>();
        var hist2 = new List<string>();
        var hist3 = new List<string>();
        foreach (Round round in gameRecord.gameRound.GetRoundsList()) {
            if (round.scoreChanges != null && round.scoreChanges.Count >= 4) {
                hist0.Add(FormatScoreChange(round.scoreChanges[0]));
                hist1.Add(FormatScoreChange(round.scoreChanges[1]));
                hist2.Add(FormatScoreChange(round.scoreChanges[2]));
                hist3.Add(FormatScoreChange(round.scoreChanges[3]));
            } else {
                hist0.Add("0");
                hist1.Add("0");
                hist2.Add("0");
                hist3.Add("0");
            }
        }
        var player_to_info = new Dictionary<string, PlayerInfoClass> {
            { "self", new PlayerInfoClass { original_player_index = 0, username = name0, score_history = hist0 } },
            { "right", new PlayerInfoClass { original_player_index = 1, username = name1, score_history = hist1 } },
            { "top", new PlayerInfoClass { original_player_index = 2, username = name2, score_history = hist2 } },
            { "left", new PlayerInfoClass { original_player_index = 3, username = name3, score_history = hist3 } }
        };
        GameScoreRecord.Instance.UpdateScoreRecord(rule, player_to_info);
    }

    private static string FormatScoreChange(int delta) {
        if (delta > 0) return "+" + delta;
        if (delta < 0) return delta.ToString();
        return "0";
    }
}
