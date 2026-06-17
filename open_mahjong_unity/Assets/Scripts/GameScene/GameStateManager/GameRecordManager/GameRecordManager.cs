using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using System;
using TMPro;
using Newtonsoft.Json.Linq;
using Unity.Entities.UniversalDelegates;

/// <summary>
/// 极简牌谱管理器：
/// - 所有 JSON 解析与巡目分割逻辑在 GameRecordJsonDecoder 中完成；
/// - 本类只保存解析结果数据，供其它系统读取与驱动回放。
/// </summary>
public partial class GameRecordManager : MonoBehaviour {
    public enum RecordManagerMode {
        Record = 0,
        Spectator = 1,
        RecordOnSpectator = 2
    }

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
    [SerializeField] private Button showSpectatorInfoButton;
    [SerializeField] private Button showRoundInfoButton;
    // 退出按钮统一由 ExitButtonManager 引用并控制可见性，此处保留 SerializeField 仅用于绑定 onClick
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
    [SerializeField] private GameObject spectatingPanel;

    public static GameRecordManager Instance { get; private set; }
    public RecordManagerMode CurrentMode { get; private set; } = RecordManagerMode.Record;
    /// <summary>牌谱手摸切灰显；旧牌谱无字段时默认开启。</summary>
    public bool ShowMoqieHint { get; private set; } = true;
    public bool IsSpectatorSession => CurrentMode == RecordManagerMode.Spectator || CurrentMode == RecordManagerMode.RecordOnSpectator;
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
    private int lastDiscardTileId = -1;
    // 荣和时供和牌张追加用：普通荣和=最后一张弃牌，抢杠和=被抢的加杠牌。
    // 与 lastDiscardTileId 分开维护，避免抢杠时误用早前过期的弃牌（如全数牌和牌里冒出一张西）。
    private int lastWinnableTileId = -1;
    // 5.当前局数
    public int currentRoundIndex;
    // 立直麻将当前局已翻开的宝牌指示牌（含杠宝牌），用于回放 hu_riichi 结算展示
    private List<int> recordRiichiDoraIndicators = new List<int>();
    
    [SerializeField] private List<RecordPlayer> recordPlayerList = new List<RecordPlayer>();
    // 6.用户ID到用户名的映射
    private Dictionary<int, string> userIdToUsername = new Dictionary<int, string>();
    // 7.用户ID到分数的映射（牌谱初始化中心盘使用，从0开始累加）
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

    // 牌谱短码 → 显示/音效用的完整动作名
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
        public int score;
        public List<int> tileList = new List<int>();
        public List<int> discardTiles = new List<int>();
        public List<bool> discardIsMoqie = new List<bool>();
        /// <summary>立直规则：与 discardTiles 同序的横置标记，回放或跳到指定动作时复原立直横置弃牌。</summary>
        public List<bool> discardRiichiFlags = new List<bool>();
        /// <summary>立直规则：当本家上一张立直横置弃牌被吃/碰/明杠走后，下一张切牌仍需横置；执行 c 后归 false。</summary>
        public bool pendingRiichiHorizontal;
        /// <summary>立直规则：本局是否已宣告立直（含 daburu）。用于跳转回放时复原立直棒，不需要 4 段表达。</summary>
        public bool isRiichi;
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
        showSpectatorInfoButton.onClick.AddListener(ShowSpectatorInfo);
        showRoundInfoButton.onClick.AddListener(ShowRoundInfo);
    }

    private void Start() {
        if (ExitButtonManager.Instance != null) {
            if (ExitButtonManager.Instance.QuitRecordButton != null)
                ExitButtonManager.Instance.QuitRecordButton.onClick.AddListener(QuitRecord);
            if (ExitButtonManager.Instance.QuitSpectatorButton != null)
                ExitButtonManager.Instance.QuitSpectatorButton.onClick.AddListener(OnClickExitSpectator);
        }
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

    public void HideGameRecord() {
        gameObject.SetActive(false);
        CurrentMode = RecordManagerMode.Record;
        GameSceneMouseInputController.Instance.SetState(GameSceneMouseInputController.StateIdle);
        if (ExitButtonManager.Instance != null) ExitButtonManager.Instance.HideAll();
    }

    // 加载牌谱
    public void LoadRecord(string recordJson, PlayerRecordInfo[] players_info = null) {
        // 已在延时观战内的刷新（如对局结束重载完整牌谱）仍允许；其它互斥会话下禁止覆盖 UI
        if (!IsSpectating && GameSessionGuard.BlockIfExclusiveSession("阅览牌谱")) return;

        CurrentMode = RecordManagerMode.Record;
        // 清空临时面板
        GameSceneUIManager.Instance.InitGameRecord();
        GameSceneMouseInputController.Instance.SetState(GameSceneMouseInputController.StateRecord);

        // 初始化selfPlayerId，如果selectedPlayerUserid没有值，后续使用selfPlayerId作为显示玩家的默认值
        selfPlayerId = 0;
        if (UserDataManager.Instance.UserId != 0){
            selfPlayerId = UserDataManager.Instance.UserId;
        }
        selectedPlayerUserid = 0;
        currentNode = 0;
        currentRoundIndex = 1;
        // 解析记录

        // 显示记录管理器
        gameObject.SetActive(true);
        xunmuScrollView.gameObject.SetActive(false);
        roundScrollView.gameObject.SetActive(false);
        tileListView.SetActive(false);
        gameInfoView.SetActive(false);
        roundInfoView.SetActive(false);
        spectatorInfoView.SetActive(false);
        // 观战模式
        if (CurrentMode == RecordManagerMode.Spectator || CurrentMode == RecordManagerMode.RecordOnSpectator) {
            spectatingPanel.SetActive(true); // 显示观战面板
            showGameInfoButton.gameObject.SetActive(false); // 隐藏显示游戏信息按钮
            showSpectatorInfoButton.gameObject.SetActive(true); // 显示显示观战信息按钮
            if (ExitButtonManager.Instance != null) ExitButtonManager.Instance.ShowForSpectator();
        }
        // 牌谱模式
        else if (CurrentMode == RecordManagerMode.Record) {
            spectatingPanel.SetActive(false); // 隐藏观战面板
            showGameInfoButton.gameObject.SetActive(true); // 显示显示游戏信息按钮
            showSpectatorInfoButton.gameObject.SetActive(false); // 隐藏显示观战信息按钮
            if (ExitButtonManager.Instance != null) ExitButtonManager.Instance.ShowForRecord();
        }

        // 解析记录头
        gameRecord = GameRecordJsonDecoder.ParseGameRecord(recordJson);
        _gameRecordInspector = gameRecord;
        ShowMoqieHint = ReadGameTitleBool(gameRecord.gameTitle, "show_moqie_hint", true);
        // 解析记录局
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
                if (player == null) continue;
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
                userIdToScore[player.user_id] = 0;
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
        // 重新推理手牌前清空所有正在执行的3D动画，避免与重建画面冲突
        Game3DManager.Instance.StopAllRunningAnimations();
        // 清理3D桌面
        Game3DManager.Instance.Clear3DTile();

        // 重置局内行动节点
        currentNode = 0;

        // 计算截至当前局之前的累计分数（从0开始，累加前面所有局的 scoreChanges）
        int[] cumulativeByOrig = new int[4];
        if (gameRecord?.gameRound?.rounds != null) {
            for (int r = 1; r < roundIndex; r++) {
                if (gameRecord.gameRound.rounds.TryGetValue(r, out Round prevRound) &&
                    prevRound.scoreChanges != null && prevRound.scoreChanges.Count >= 4) {
                    for (int p = 0; p < 4; p++) {
                        cumulativeByOrig[p] += prevRound.scoreChanges[p];
                    }
                }
            }
        }

        // 初始化四个空userid的局对象（含累计分数），稍后进行赋值
        recordPlayerList.Clear();
        for (int p = 0; p < 4; p++) {
            recordPlayerList.Add(new RecordPlayer {
                userId = Convert.ToInt32(gameRecord.gameTitle[$"p{p}_uid"]),
                originalPlayerIndex = p,
                score = cumulativeByOrig[p]
            });
        }
        // 同步 userIdToScore
        foreach (var rp in recordPlayerList) {
            userIdToScore[rp.userId] = rp.score;
        }

        gameRecord.gameRound.rounds.TryGetValue(roundIndex, out Round roundDataForSeats);
        foreach (var recordPlayer in recordPlayerList) {
            recordPlayer.playerIndex = roundDataForSeats.seats[recordPlayer.originalPlayerIndex];
        }

        // 初始化牌山列表（当前牌山和原始牌山）
        if (roundDataForSeats != null) {
            originalTilesList = roundDataForSeats.tilesList != null ? new List<int>(roundDataForSeats.tilesList) : new List<int>();
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
            recordPlayer.discardIsMoqie.Clear();
            recordPlayer.discardRiichiFlags.Clear();
            recordPlayer.pendingRiichiHorizontal = false;
            recordPlayer.isRiichi = false;
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
            string roomRule = ReadGameTitleString(gameRecord.gameTitle, "rule", "").ToLowerInvariant();
            BoardCanvas.Instance.InitializeBoardInfoFromRecord(recordPlayerList, indexToPosition, userIdToScore, roomRule, displayRound, startRemainTiles);

            // 初始化左上局数面板RoundPanel
            GameCanvas.Instance.UpdateRoomInfoFromRecord(gameRecord, currentRoundIndex);

            // 初始化手牌
            GameCanvas.Instance.ChangeHandCards("InitHandCardsFromRecord",0,recordPlayer_to_info["self"].tileList.ToArray(),null);
            Game3DManager.Instance.Change3DTile("InitHandCardsFromRecord",0,0,null,false,null);
        }

        // 初始化当前行动玩家
        currentPlayerIndex = 0;
        if (gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundForStart)) {
            currentPlayerIndex = roundForStart.startPlayerIndex;
        }
        lastDiscardPlayerIndex = -1;
        lastDiscardTileId = -1;
        lastWinnableTileId = -1;
        recordRiichiDoraIndicators.Clear();
        BoardCanvas.Instance.ShowCurrentPlayer(indexToPosition[currentPlayerIndex], currentTilesList.Count);    
    }
    
    // 选择选中巡目
    public void GotoSelectNode(int nodeIndex, bool updateSpectatorMode = true) {
        if (!gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData)) {
            return;
        }
        if (roundData.actionTicks == null) {
            return;
        }
        int safeTargetNode = Mathf.Clamp(nodeIndex, 0, roundData.actionTicks.Count);
        GotoAction(safeTargetNode);
        if (IsSpectatorSession && updateSpectatorMode) {
            RefreshSpectatorModeByNodePosition();
        }
    }

    // 选择选中局
    public void GotoSelectRound(int roundIndex, bool fromUserAction = true) {
        if (!gameRecord.gameRound.rounds.ContainsKey(roundIndex)) {
            return;
        }
        // 观战中手动切局：进入牌谱阅览模式（不再显示直播模式）
        if (fromUserAction && IsSpectatorSession) {
            SwitchToRecordMode();
        }
        currentRoundIndex = roundIndex;
        InitGameRound(roundIndex);
        if (IsSpectatorSession) {
            // 手动切局时保持在牌谱阅览模式，不在这里自动回到直播
            if (!fromUserAction) {
                RefreshSpectatorModeByNodePosition();
            }
        }
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
            currentPlayerIndex = roundData.startPlayerIndex;
            BoardCanvas.Instance.ShowCurrentPlayer(indexToPosition[currentPlayerIndex], currentTilesList.Count);
        }
        int previousPlayerIndex = currentPlayerIndex;
        int actingPlayerIndex = currentPlayerIndex;
        if (action == "bh" && tick.Count >= 3) {
            actingPlayerIndex = ParseTickInt(tick, 2);
        } else if ((action == "cl" || action == "cm" || action == "cr" || action == "p" || action == "g") && tick.Count >= 3) {
            actingPlayerIndex = ParseTickInt(tick, 2);
        } else if ((action == "hu_self" || action == "hu_first" || action == "hu_second" || action == "hu_third") && tick.Count >= 2) {
            actingPlayerIndex = ParseTickInt(tick, 1);
        } else if (action == "riichi" && tick.Count >= 2) {
            actingPlayerIndex = ParseTickInt(tick, 1);
        }

        string currentPlayerPosition = indexToPosition[actingPlayerIndex];
        RecordPlayer currentRecordPlayer = recordPlayer_to_info[currentPlayerPosition];
        string displayAction = ToDisplayAction(action);
        if (action != "riichi") {
            SoundManager.Instance.PlayActionSound(currentPlayerPosition, displayAction);
            SoundManager.Instance.PlayPhysicsSound(displayAction);
        }
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
            // 牌谱第 4 段为可选 "H" 标识立直横置弃牌（含立直宣告与续横情况），缺省视为非横置
            bool isRiichiHorizontal = tick.Count > 3 && tick[3] == "H";
            int cutIndex = RemoveTileForCut(currentRecordPlayer.tileList, cutTile, isMoqie);
            currentRecordPlayer.discardTiles.Add(cutTile);
            currentRecordPlayer.discardIsMoqie.Add(isMoqie);
            currentRecordPlayer.discardRiichiFlags.Add(isRiichiHorizontal);

            if (currentPlayerPosition == "self") {
                if (isMoqie) {
                    GameCanvas.Instance.ChangeHandCards("RemoveGetCard", cutTile, null, null);
                } else {
                    GameCanvas.Instance.ChangeHandCards("RemoveHandCardRecord", cutTile, null, null);
                }
            }
            Game3DManager.Instance.Change3DTile("RecordDiscard", cutTile, 0, currentPlayerPosition, isMoqie, null, isRiichiHorizontal);
            lastDiscardPlayerIndex = actingPlayerIndex;
            lastDiscardTileId = cutTile;
            lastWinnableTileId = cutTile;
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
            bool isMoGang = GameRecordJsonDecoder.ParseKanMoGangFlag(tick);
            string rule = ReadGameTitleString(gameRecord.gameTitle, "rule", "").ToLowerInvariant();
            List<int> removedTiles = GameRecordMeldCodec.ResolveAngangRemovedTiles(
                tick, currentRecordPlayer.tileList, angangTile, isMoGang);
            int[] combinationMask = GameRecordMeldCodec.BuildAngangMaskFromRemoved(removedTiles, rule);
            currentRecordPlayer.combinationTiles.Add($"G{angangTile}");
            currentRecordPlayer.combinationMasks.Add(combinationMask);
            if (currentPlayerPosition == "self") {
                ApplyRecordAngangHandCardRemoval(removedTiles, isMoGang);
            }
            Game3DManager.Instance.Change3DTile("angang", angangTile, removedTiles.Count, currentPlayerPosition, false, combinationMask, isMoGang: isMoGang);
            GameCanvas.Instance.ShowActionDisplay(currentPlayerPosition, "angang");
            PlayRecordGangScoreChanges(tick);
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "jg") {
            int jiagangTile = ParseTickInt(tick, 1);
            bool isMoGang = GameRecordJsonDecoder.ParseKanMoGangFlag(tick);
            List<int> removedTiles = GameRecordMeldCodec.RemoveNTilesByNormalized(
                currentRecordPlayer.tileList, jiagangTile, 1, preferDrawSlotFirst: isMoGang);
            int actualJia = removedTiles.Count > 0 ? removedTiles[0] : jiagangTile;
            // 抢杠和：被抢的加杠牌即和牌张，登记为可和牌张供随后 hu_* 追加
            lastWinnableTileId = actualJia;
            int[] combinationMask = BuildJiagangMask(currentRecordPlayer, jiagangTile, actualJia);
            if (currentPlayerPosition == "self") {
                if (isMoGang) {
                    GameCanvas.Instance.ChangeHandCards("RemoveGetCard", actualJia, null, null);
                } else {
                    GameCanvas.Instance.ChangeHandCards("RemoveJiagangCard", actualJia, null, null);
                }
            }
            Game3DManager.Instance.Change3DTile("jiagang", actualJia, 1, currentPlayerPosition, isMoGang, combinationMask);
            GameCanvas.Instance.ShowActionDisplay(currentPlayerPosition, "jiagang");
            PlayRecordGangScoreChanges(tick);
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "cl" || action == "cm" || action == "cr" || action == "p" || action == "g") {
            int mingpaiTile = ParseTickInt(tick, 1);
            List<int> removedTiles = GameRecordMeldCodec.ResolveHandTiles(tick, action, mingpaiTile);
            foreach (int tileId in removedTiles) {
                RemoveOneTile(currentRecordPlayer.tileList, tileId);
            }
            if (lastDiscardPlayerIndex >= 0 && indexToPosition.ContainsKey(lastDiscardPlayerIndex)) {
                string discardPlayerPosition = indexToPosition[lastDiscardPlayerIndex];
                var dpRecord = recordPlayer_to_info[discardPlayerPosition];
                RemoveOneTile(dpRecord.discardTiles, mingpaiTile);
                // 同步剔除最后一张弃牌的横置标记；若被吃/碰走的恰是立直横置弃牌，
                // 则给该玩家挂起 pendingRiichiHorizontal，使其下一张切牌仍横置渲染
                if (dpRecord.discardRiichiFlags.Count > 0){
                    bool stolenHorizontal = dpRecord.discardRiichiFlags[dpRecord.discardRiichiFlags.Count - 1];
                    dpRecord.discardRiichiFlags.RemoveAt(dpRecord.discardRiichiFlags.Count - 1);
                    if (stolenHorizontal) dpRecord.pendingRiichiHorizontal = true;
                }
            }
            int discardPlayerIndex = lastDiscardPlayerIndex >= 0 ? lastDiscardPlayerIndex : previousPlayerIndex;
            string relative = GetRelativePosition(actingPlayerIndex, discardPlayerIndex);
            int[] combinationMask = GameRecordMeldCodec.BuildMingpaiMask(action, mingpaiTile, removedTiles, relative);
            currentRecordPlayer.combinationTiles.Add(GameRecordMeldCodec.BuildCombinationTarget(action, mingpaiTile));
            currentRecordPlayer.combinationMasks.Add(combinationMask);
            // 弃牌已被吃/碰/明杠取走，不再是可荣和牌张，清除避免后续误追加
            lastWinnableTileId = -1;
            if (currentPlayerPosition == "self") {
                GameCanvas.Instance.ChangeHandCards("RemoveCombinationCard", 0, removedTiles.ToArray(), null);
            }
            Game3DManager.Instance.Change3DTile(displayAction, 0, removedTiles.Count, currentPlayerPosition, false, combinationMask);
            GameCanvas.Instance.ShowActionDisplay(currentPlayerPosition, displayAction);
            if (action == "g") {
                PlayRecordGangScoreChanges(tick);
            }
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "hu_self" || action == "hu_first" || action == "hu_second" || action == "hu_third") {
            string ruleName = ReadGameTitleString(gameRecord.gameTitle, "rule", "").ToLowerInvariant();
            if (ruleName == "classical") {
                int hepaiPlayerIndexOnly = ParseTickInt(tick, 1);
                if (indexToPosition.ContainsKey(hepaiPlayerIndexOnly)) {
                    string classicalHuPosition = indexToPosition[hepaiPlayerIndexOnly];
                    GameCanvas.Instance.ShowActionDisplay(classicalHuPosition, action);
                    SoundManager.Instance.PlayActionSound(classicalHuPosition, action);
                }
                currentPlayerIndex = nextPlayerIndex;
                currentNode++;
                GameCanvas.Instance.ChangeHandCards("ReSetHandCards", 0, null, null);
                BoardCanvas.Instance.ShowCurrentPlayer(indexToPosition[currentPlayerIndex], currentTilesList.Count);
                RefreshTileListViewIfVisible();
                UpdateCurrentXunmuText();
                return;
            }

            // tick 格式: [hu_class, hepai_player_index, hu_score, hu_fan_json, score_changes_json, base_fu?, fu_fan_list?]
            int hepaiPlayerIndex = ParseTickInt(tick, 1);
            int huScore = (int)ParseTickDouble(tick, 2);
            string[] huFan = ParseHuFanList(tick, 3);
            int? baseFu = tick.Count > 5 ? (int?)ParseTickInt(tick, 5) : null;
            string[] fuFanList = tick.Count > 6 ? ParseHuFanList(tick, 6) : null;

            string huPosition = indexToPosition.ContainsKey(hepaiPlayerIndex) ? indexToPosition[hepaiPlayerIndex] : "self";
            RecordPlayer huPlayer = recordPlayer_to_info[huPosition];
            int[] hepaiPlayerHand = huPlayer.tileList.ToArray();
            // 荣和时，和牌张不在手牌中，需追加到末尾供 EndResultPanel 显示。
            // 用 lastWinnableTileId（弃牌或抢杠加杠牌）而非 lastDiscardTileId，避免抢杠时追加过期弃牌。
            if (action != "hu_self" && lastWinnableTileId >= 0) {
                int[] newHand = new int[hepaiPlayerHand.Length + 1];
                Array.Copy(hepaiPlayerHand, newHand, hepaiPlayerHand.Length);
                newHand[hepaiPlayerHand.Length] = lastWinnableTileId;
                hepaiPlayerHand = newHand;
            }
            int[] hepaiPlayerHuapai = huPlayer.huapaiList.ToArray();
            int[][] hepaiPlayerCombinationMask = huPlayer.combinationMasks.ToArray();

            var deltas = new Dictionary<int, int>();
            int[] tickScoreChanges = ParseTickScoreChanges(tick, 4);
            if (tickScoreChanges != null && tickScoreChanges.Length >= 4) {
                MapTickScoreChangesToDeltas(tickScoreChanges, deltas);
            } else {
                Round huRoundData = null;
                gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out huRoundData);
                string recordRule = ReadGameTitleString(gameRecord.gameTitle, "rule", "").ToLowerInvariant();
                int fangchong = lastDiscardPlayerIndex >= 0 ? lastDiscardPlayerIndex : currentPlayerIndex;
                deltas = ResolveScoreDeltas(huRoundData, recordRule, action, ParseTickDouble(tick, 2), hepaiPlayerIndex, fangchong);
            }
            ApplyScoreDeltas(deltas, out Dictionary<int, int> playerToScoreBefore, out Dictionary<int, int> playerToScoreAfter);

            TryParseSichuanHuTickExtras(tick, out int parsedHepaiTile, out bool multiRonFlag, out int? ronDiscarderIndex, out bool recycleDiscardFlag);
            int resolvedHepaiTile = ResolveRecordHepaiTile(action, hepaiPlayerIndex, parsedHepaiTile, huPlayer);
            bool isDeferredMidGameHu = IsSichuanBloodBattleRecord()
                && IsDeferredSichuanHuScore(huScore, tickScoreChanges);
            bool isQianggangHu = ContainsSichuanQianggangFan(huFan);

            if (isDeferredMidGameHu) {
                bool recycleDiscard = recycleDiscardFlag
                    || (!multiRonFlag && action != "hu_self");
                PlaySichuanMidGameHuRecord(
                    action, hepaiPlayerIndex, resolvedHepaiTile, multiRonFlag, ronDiscarderIndex,
                    recycleDiscard, isQianggangHu);
            } else {
                ShowRecordResult(action, huScore, huFan, hepaiPlayerIndex, hepaiPlayerHand, hepaiPlayerHuapai, hepaiPlayerCombinationMask, playerToScoreBefore, playerToScoreAfter, baseFu, fuFanList);
            }
            GameSceneUIManager.Instance.UpdateScoreRecord();
        }
        else if (action == "shuhewei") {
            string rule = ReadGameTitleString(gameRecord.gameTitle, "rule", "").ToLowerInvariant();
            if (rule == "classical") {
                int[] fuArray = ParseTickScoreChanges(tick, 1);
                int[] changesArray = ParseTickScoreChanges(tick, 2);
                string[][] fanArray = ParseTickFanLists(tick, 3);
                string[][] fuTypeArray = ParseTickFanLists(tick, 4);

                var playerFu = new Dictionary<int, int>();
                var scoreChanges = new Dictionary<int, int>();
                var deltas = new Dictionary<int, int>();
                var playerFan = new Dictionary<int, string[]>();
                var playerFuTypes = new Dictionary<int, string[]>();

                foreach (var rp in recordPlayerList) {
                    int origIdx = rp.originalPlayerIndex;
                    playerFu[rp.playerIndex] = (fuArray != null && origIdx < fuArray.Length) ? fuArray[origIdx] : 0;
                    int change = (changesArray != null && origIdx < changesArray.Length) ? changesArray[origIdx] : 0;
                    scoreChanges[rp.playerIndex] = change;
                    deltas[rp.playerIndex] = change;
                    playerFan[rp.playerIndex] = (fanArray != null && origIdx < fanArray.Length && fanArray[origIdx] != null) ? fanArray[origIdx] : Array.Empty<string>();
                    playerFuTypes[rp.playerIndex] = (fuTypeArray != null && origIdx < fuTypeArray.Length && fuTypeArray[origIdx] != null) ? fuTypeArray[origIdx] : Array.Empty<string>();
                }

                ApplyScoreDeltas(deltas, out _, out Dictionary<int, int> playerToScoreAfter);

                var player_to_info = new Dictionary<string, PlayerInfoClass>();
                foreach (var rp in recordPlayerList) {
                    string pos = indexToPosition[rp.playerIndex];
                    string username = userIdToUsername.TryGetValue(rp.userId, out string name) ? name : rp.userId.ToString();
                    player_to_info[pos] = new PlayerInfoClass { username = username };
                }

                EndResultPanel.Instance.ClearEndResultPanel();
                EndShuheWeiPanel.Instance.ShowShuhewei(playerFu, playerToScoreAfter, scoreChanges, playerFan, playerFuTypes, indexToPosition, player_to_info, true);
                GameSceneUIManager.Instance.UpdateScoreRecord();
                BoardCanvas.Instance.UpdatePlayerScores(playerToScoreAfter, indexToPosition);
                if (IsSpectating && IsLiveSpectatorMode) {
                    float revealWait = 0f;
                    foreach (var rp in recordPlayerList) {
                        int idx = rp.playerIndex;
                        int revealItems = (playerFuTypes.ContainsKey(idx) ? playerFuTypes[idx].Length : 0) + (playerFan.ContainsKey(idx) ? playerFan[idx].Length : 0);
                        revealWait += revealItems * 1.0f + 0.5f;
                    }
                    StartCoroutine(AutoNextActionAfterDelay(8f + revealWait));
                }
            }
        }
        else if (action == "gr") {
            PlayRecordGangRefundTick(tick);
            StartCoroutine(AutoNextActionAfterDelay(1.8f));
        }
        else if (action == "liuju") {
            if (tick.Count >= 2 && IsSichuanBloodBattleRecord()) {
                HandleSichuanLiujuStepReplay(tick);
            } else {
                RoundEndPresentation.Instance.PresentLiuju("流局", false);
                HideRecordRiichiSticksOnLiuju();
                StartCoroutine(AutoNextActionAfterDelay(2f));
            }
        }
        else if (action == "jiuzhongjiupai") {
            RoundEndPresentation.Instance.PresentLiuju("九老峰回", false);
            HideRecordRiichiSticksOnLiuju();
            StartCoroutine(AutoNextActionAfterDelay(2f));
        }
        else if (action == "riichi") {
            int riichiPlayer = ParseTickInt(tick, 1);
            if (indexToPosition.ContainsKey(riichiPlayer)) {
                string riichiPos = indexToPosition[riichiPlayer];
                GameCanvas.Instance.ShowActionDisplay(riichiPos, "riichi");
                SoundManager.Instance.PlayRiichiVoice(riichiPos, recordPlayer_to_info[riichiPos].voice_used);
                // 牌谱回放：直接放置立直棒（不播放飞行动画），与重连行为一致
                Game3DManager.Instance.PlaceRiichiTenbouAt(riichiPos);
            }
            // 标记该玩家本局已立直，跳转回放时用于一次性复原立直棒
            foreach (var rp in recordPlayerList){
                if (rp.playerIndex == riichiPlayer){ rp.isRiichi = true; break; }
            }
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "dora") {
            int doraTile = ParseTickInt(tick, 1);
            recordRiichiDoraIndicators.Add(doraTile);
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "ryuukyoku") {
            string reason = tick.Count > 3 ? tick[3] : "exhaustive";
            string text = reason switch {
                "exhaustive" => "流局",
                "sifuuu" => "四风连打",
                "suukaikan" => "四杠散了",
                "suurichi" => "四人立直",
                "sanchahou" => "三家和流局",
                _ => "流局",
            };
            int[] changes = ParseTickScoreChanges(tick, 2);
            if (changes != null) {
                var deltas = new Dictionary<int, int>();
                MapTickScoreChangesToDeltas(changes, deltas);
                ApplyScoreDeltas(deltas, out _, out Dictionary<int, int> after);
                BoardCanvas.Instance.UpdatePlayerScores(after, indexToPosition);
            }
            RoundEndPresentation.Instance.PresentLiuju(text, false);
            HideRecordRiichiSticksOnLiuju();
            StartCoroutine(AutoNextActionAfterDelay(2f));
        }
        else if (action == "hu_riichi") {
            HandleHuRiichiReplay(tick);
        }
        else if (action == "end") {
            StartCoroutine(GotoNextRoundAfterDelay(0.1f));
        }

        currentPlayerIndex = nextPlayerIndex;
        currentNode++;
        // 与对局侧一致：仅在回合流转（下一家行动）时整理手牌，摸牌后同一家继续行动不排序
        if (nextPlayerIndex != actingPlayerIndex) {
            GameCanvas.Instance.ChangeHandCards("ReSetHandCards", 0, null, null);
        }
        BoardCanvas.Instance.ShowCurrentPlayer(indexToPosition[currentPlayerIndex], currentTilesList.Count);
        RefreshTileListViewIfVisible();
        UpdateCurrentXunmuText();
    }

    /// <summary>
    /// 前进玩家索引（0->1->2->3->0）
    /// </summary>
    private int ForwardCurrentNum(int num) {
        if (num == 3) {
            return 0;
        }
        return num + 1;
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
        int honba = 0;
        if (gameRecord != null &&
            gameRecord.gameRound != null &&
            gameRecord.gameRound.rounds != null &&
            gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData)) {
            if (roundData.currentRound > 0) {
                roundNo = roundData.currentRound;
            }
            if (roundData.riichi != null) {
                honba = roundData.riichi.honba;
            }
        }
        string rule = ReadGameTitleString(gameRecord != null ? gameRecord.gameTitle : null, "rule", "").ToLowerInvariant();
        string roundText = FormatRoundText(rule, roundNo);
        currentRoundText.text = rule == "riichi" ? $"{roundText} {honba}本场" : roundText;
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

    /// <summary>对局信息追加一行 shuffle 前的玩家入场顺序（user_id[4]）。</summary>
    private static void AppendPlayerEntryOrderLine(System.Text.StringBuilder sb, Dictionary<string, object> gt) {
        if (gt == null || !gt.TryGetValue("player_entry_order", out object value)) return;
        if (!(value is JArray arr) || arr.Count != 4) return;
        sb.AppendLine($"对局玩家次序: {arr[0]}, {arr[1]}, {arr[2]}, {arr[3]}");
    }

    /// <summary>
    /// 生成游戏信息文案，有什么信息就展示什么，因为不同规则可能有不同牌谱头配置（规则、局数、玩家ID、选项等）
    /// </summary>
    private string BuildGameInfoString() {
        if (gameRecord?.gameTitle == null) return "暂无游戏信息";
        var gt = gameRecord.gameTitle;
        string ruleKey = ReadGameTitleString(gt, "rule", "").ToLowerInvariant();
        string subRule = ReadGameTitleString(gt, "sub_rule", "");
        string rule = RuleNameDictionary.GetWholeName(!string.IsNullOrEmpty(subRule) ? subRule : ruleKey);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("【游戏信息】");
        sb.AppendLine($"规则: {rule}");
        if (!string.IsNullOrEmpty(subRule)) sb.AppendLine($"子规则: {subRule}");
        AppendGameTitleOptionLines(sb, gt, ruleKey, gameRecord);
        AppendPlayerEntryOrderLine(sb, gt);
        int p0 = ReadGameTitleInt(gt, "p0_uid", 0), p1 = ReadGameTitleInt(gt, "p1_uid", 0), p2 = ReadGameTitleInt(gt, "p2_uid", 0), p3 = ReadGameTitleInt(gt, "p3_uid", 0);
        if (p0 != 0 || p1 != 0 || p2 != 0 || p3 != 0) {
            string p0Name = userIdToUsername.TryGetValue(p0, out string n0) ? n0 : ReadGameTitleString(gt, "p0_name", p0.ToString());
            string p1Name = userIdToUsername.TryGetValue(p1, out string n1) ? n1 : ReadGameTitleString(gt, "p1_name", p1.ToString());
            string p2Name = userIdToUsername.TryGetValue(p2, out string n2) ? n2 : ReadGameTitleString(gt, "p2_name", p2.ToString());
            string p3Name = userIdToUsername.TryGetValue(p3, out string n3) ? n3 : ReadGameTitleString(gt, "p3_name", p3.ToString());
            sb.AppendLine("随机座位分配 (original 0～3):");
            sb.AppendLine($"玩家0: {p0Name} (ID:{p0})");
            sb.AppendLine($"玩家1: {p1Name} (ID:{p1})");
            sb.AppendLine($"玩家2: {p2Name} (ID:{p2})");
            sb.AppendLine($"玩家3: {p3Name} (ID:{p3})");
        }
        AppendCommitmentSaltLines(sb, gt);
        if (gt.ContainsKey("start_time") && gt["start_time"] != null) sb.AppendLine($"开始时间: {gt["start_time"]}");
        if (gt.ContainsKey("end_time") && gt["end_time"] != null) sb.AppendLine($"结束时间: {gt["end_time"]}");
        return sb.ToString();
    }

    /// <summary>牌谱 game_title 中的房间/规则选项行（与 build_game_title_data 字段对应）。</summary>
    private static void AppendGameTitleOptionLines(System.Text.StringBuilder sb, Dictionary<string, object> gt, string ruleKey, GameRecord record) {
        string roomType = ReadGameTitleString(gt, "room_type", "");
        if (!string.IsNullOrEmpty(roomType)) {
            string roomTypeLabel = roomType switch {
                "custom" => "自定义房",
                "match" => "匹配房",
                _ => roomType,
            };
            sb.AppendLine($"房间类型: {roomTypeLabel}");
        }
        int maxRound = ReadGameTitleInt(gt, "max_round", 0);
        if (maxRound <= 0 && record?.gameRound != null) {
            maxRound = Mathf.Max(1, record.gameRound.rounds?.Count ?? 0);
        }
        if (maxRound > 0) sb.AppendLine($"最大局数: {maxRound}局");
        if (gt.ContainsKey("hepai_limit")) {
            sb.AppendLine($"起和番: {ReadGameTitleInt(gt, "hepai_limit", 0)}");
        }
        if (gt.ContainsKey("open_cuohe")) sb.AppendLine($"错和: {(ReadGameTitleBool(gt, "open_cuohe", false) ? "开" : "关")}");
        if (gt.ContainsKey("tips")) sb.AppendLine($"提示: {(ReadGameTitleBool(gt, "tips", false) ? "开" : "关")}");
        if (gt.ContainsKey("show_moqie_hint")) sb.AppendLine($"手摸切显示: {(ReadGameTitleBool(gt, "show_moqie_hint", true) ? "开" : "关")}");
        if (gt.ContainsKey("is_player_set_random_seed") || gt.ContainsKey("isPlayerSetRandomSeed")) {
            bool isSetSeed = ReadGameTitleBool(gt, "is_player_set_random_seed", false)
                || ReadGameTitleBool(gt, "isPlayerSetRandomSeed", false);
            sb.AppendLine($"复式: {(isSetSeed ? "开" : "关")}");
        }
        if (ruleKey == "riichi") {
            if (gt.ContainsKey("red_dora")) {
                sb.AppendLine($"赤宝牌: {(ReadGameTitleBool(gt, "red_dora", false) ? "开" : "关")}");
            }
            string recordSubRule = ReadGameTitleString(gt, "sub_rule", "");
            if (recordSubRule != "riichi/langyong" && gt.ContainsKey("allow_kuikae")) {
                sb.AppendLine($"食替: {(ReadGameTitleBool(gt, "allow_kuikae", false) ? "开" : "关")}");
            }
            string hepaiWay = ReadGameTitleString(gt, "hepai_way", "");
            if (!string.IsNullOrEmpty(hepaiWay)) {
                sb.AppendLine($"和牌方式: {FormatRecordHepaiWay(hepaiWay)}");
            }
            if (gt.ContainsKey("open_xiru")) {
                sb.AppendLine($"西入: {(ReadGameTitleBool(gt, "open_xiru", false) ? "开" : "关")}");
            }
            if (gt.ContainsKey("open_tobi")) {
                sb.AppendLine($"击飞: {(ReadGameTitleBool(gt, "open_tobi", false) ? "开" : "关")}");
            }
        }
    }

    private static string FormatRecordHepaiWay(string way) {
        return way switch {
            "head_bump" => "头跳",
            "multi_ron" => "允许多家和",
            "three_ron_abort" => "三家和了流局",
            _ => way,
        };
    }

    private static void AppendCommitmentSaltLines(System.Text.StringBuilder sb, Dictionary<string, object> gt) {
        if (gt == null) return;
        string commitment = CommitmentSaltDisplay.ReadCommitmentFromGameTitle(gt);
        string salt = CommitmentSaltDisplay.ReadSaltFromGameTitle(gt);
        if (!string.IsNullOrEmpty(commitment)) {
            sb.AppendLine($"承诺值: {CommitmentSaltDisplay.NormalizeCommitment(commitment)}");
        }
        if (!string.IsNullOrEmpty(salt)) {
            sb.AppendLine($"盐值: {salt}");
        }
        if (string.IsNullOrEmpty(commitment) && string.IsNullOrEmpty(salt)
            && gt.ContainsKey("game_random_seed") && gt["game_random_seed"] != null) {
            sb.AppendLine($"随机种子(旧格式): {gt["game_random_seed"]}");
        }
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
        sb.AppendLine($"牌山张数: {tileWallCount}");
        sb.AppendLine($"行动总数: {actionCount}");
        sb.AppendLine($"当前行动: {currentNode} / {actionCount}");
        return sb.ToString();
    }

    private int ParseTickInt(List<string> tick, int index) {
        if (index >= tick.Count) return 0;
        if (double.TryParse(tick[index], System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double dv))
            return (int)dv;
        return 0;
    }

    private double ParseTickDouble(List<string> tick, int index) {
        if (index >= tick.Count) return 0;
        if (double.TryParse(tick[index], System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double dv))
            return dv;
        return 0;
    }

    private bool ParseTickBool(List<string> tick, int index) {
        if (index >= tick.Count) return false;
        string val = tick[index].ToLowerInvariant();
        return val == "true" || val == "t";
    }

    /// <summary>
    /// 从牌谱 round.scoreChanges 读取本局分数变化（由服务端 tick 累加）。
    /// </summary>
    private Dictionary<int, int> ResolveScoreDeltas(Round roundData, string rule,
        string huClass, double huScore, int hepaiPlayerIndex, int fangchongPlayerIndex) {

        if (roundData?.scoreChanges != null && roundData.scoreChanges.Count >= 4) {
            var deltas = new Dictionary<int, int>();
            foreach (var rp in recordPlayerList) {
                deltas[rp.playerIndex] = roundData.scoreChanges[rp.originalPlayerIndex];
            }
            return deltas;
        }

        return new Dictionary<int, int> { {0,0}, {1,0}, {2,0}, {3,0} };
    }

    /// <summary>
    /// 将 tick 中的 score_changes 数组（player_index 顺序）映射为 playerIndex → delta。
    /// </summary>
    private void MapTickScoreChangesToDeltas(int[] tickScoreChanges, Dictionary<int, int> deltas) {
        if (tickScoreChanges == null || tickScoreChanges.Length < 4) return;
        foreach (var rp in recordPlayerList) {
            deltas[rp.playerIndex] = tickScoreChanges[rp.playerIndex];
        }
    }

    /// <summary>
    /// 将 playerIndex → delta 应用到 RecordPlayer.score 上，
    /// 并返回 before/after 字典和同步 userIdToScore。
    /// </summary>
    private void ApplyScoreDeltas(Dictionary<int, int> deltas,
        out Dictionary<int, int> before, out Dictionary<int, int> after) {
        before = new Dictionary<int, int>();
        after = new Dictionary<int, int>();
        foreach (var rp in recordPlayerList) {
            before[rp.playerIndex] = rp.score;
            rp.score += deltas.ContainsKey(rp.playerIndex) ? deltas[rp.playerIndex] : 0;
            after[rp.playerIndex] = rp.score;
            userIdToScore[rp.userId] = rp.score;
        }
    }

    private int[] ParseTickScoreChanges(List<string> tick, int index) {
        if (index >= tick.Count || string.IsNullOrEmpty(tick[index])) return null;
        try {
            string raw = tick[index].Trim();
            if (!raw.StartsWith("[")) return null;
            raw = raw.Substring(1, raw.Length - 2);
            string[] parts = raw.Split(',');
            int[] result = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) {
                int.TryParse(parts[i].Trim(), out result[i]);
            }
            return result;
        } catch {
            return null;
        }
    }

    private string[][] ParseTickFanLists(List<string> tick, int index) {
        if (index >= tick.Count || string.IsNullOrEmpty(tick[index])) return null;
        try {
            JArray arr = JArray.Parse(tick[index]);
            string[][] result = new string[arr.Count][];
            for (int i = 0; i < arr.Count; i++) {
                if (arr[i] is JArray subArr) {
                    List<string> fans = new List<string>();
                    for (int j = 0; j < subArr.Count; j++) {
                        string fan = subArr[j]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(fan)) {
                            fans.Add(fan);
                        }
                    }
                    result[i] = fans.ToArray();
                } else {
                    result[i] = Array.Empty<string>();
                }
            }
            return result;
        } catch {
            return null;
        }
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

    private void ShowRecordResult(string huClass, int huScore, string[] huFan, int hepaiPlayerIndex,
        int[] hepaiPlayerHand, int[] hepaiPlayerHuapai, int[][] hepaiPlayerCombinationMask,
        Dictionary<int, int> playerToScoreBefore, Dictionary<int, int> playerToScoreAfter,
        int? baseFu = null, string[] fuFanList = null) {
        if (huClass == "jiuzhongjiupai" || NormalGameStateManager.IsRiichiSpecialLiujuHuClass(huClass)) {
            if (playerToScoreAfter != null && playerToScoreAfter.Count > 0) {
                BoardCanvas.Instance.UpdatePlayerScores(playerToScoreAfter, indexToPosition);
            }
            RoundEndPresentation.Instance.PresentLiuju(NormalGameStateManager.GetRiichiSpecialLiujuCaption(huClass), false);
            return;
        }

        Dictionary<string, string> positionToUsername = new Dictionary<string, string>();
        foreach (var kv in recordPlayer_to_info) {
            int uid = kv.Value.userId;
            if (userIdToUsername.TryGetValue(uid, out string username)) {
                positionToUsername[kv.Key] = username;
            } else {
                positionToUsername[kv.Key] = uid.ToString();
            }
        }

        string roomType = ReadGameTitleString(gameRecord.gameTitle, "sub_rule", "").ToLowerInvariant();
        if (indexToPosition.ContainsKey(hepaiPlayerIndex)) {
            string huPosition = indexToPosition[hepaiPlayerIndex];
            GameCanvas.Instance.ShowActionDisplay(huPosition, huClass);
        }

        // 更新中心盘分数
        if (playerToScoreAfter != null && playerToScoreAfter.Count > 0) {
            BoardCanvas.Instance.UpdatePlayerScores(playerToScoreAfter, indexToPosition);
        }

        GameSceneUIManager.Instance.ShowRecordResult(hepaiPlayerIndex, huScore, huFan, huClass, roomType,
            indexToPosition, positionToUsername, hepaiPlayerHand, hepaiPlayerHuapai, hepaiPlayerCombinationMask,
            playerToScoreBefore, playerToScoreAfter, IsSpectating && IsLiveSpectatorMode, baseFu, fuFanList);
    }

    /// <summary>
    /// 处理立直麻将回放和牌节点：
    /// tick 格式 ["hu_riichi", hepai_player_index, hu_class, han, fu, yaku[],
    ///            score_changes[], dora_indicators[], ura_dora_indicators[], aka_count, honba, riichi_sticks_collected]
    /// </summary>
    private void HandleHuRiichiReplay(List<string> tick) {
        int hepaiPlayerIndex = ParseTickInt(tick, 1);
        string huClass = tick.Count > 2 ? tick[2] : "hu_self";
        int[] scoreChanges = tick.Count > 6 ? ParseTickScoreChanges(tick, 6) : null;
        if (huClass == "jiuzhongjiupai" || NormalGameStateManager.IsRiichiSpecialLiujuHuClass(huClass)) {
            if (scoreChanges != null) {
                var liujuDeltas = new Dictionary<int, int>();
                MapTickScoreChangesToDeltas(scoreChanges, liujuDeltas);
                ApplyScoreDeltas(liujuDeltas, out _, out Dictionary<int, int> after);
                BoardCanvas.Instance.UpdatePlayerScores(after, indexToPosition);
            }
            RoundEndPresentation.Instance.PresentLiuju(NormalGameStateManager.GetRiichiSpecialLiujuCaption(huClass), false);
            HideRecordRiichiSticksOnLiuju();
            StartCoroutine(AutoNextActionAfterDelay(2f));
            return;
        }
        int han = tick.Count > 3 ? ParseTickInt(tick, 3) : 0;
        int fu = tick.Count > 4 ? ParseTickInt(tick, 4) : 0;
        string[] yaku = tick.Count > 5 ? ParseHuFanList(tick, 5) : new string[0];
        int[] doraIndicators = tick.Count > 7 ? ParseTickScoreChanges(tick, 7) : null;
        int[] uraDoraIndicators = tick.Count > 8 ? ParseTickScoreChanges(tick, 8) : null;
        int akaCount = tick.Count > 9 ? ParseTickInt(tick, 9) : 0;
        int honba = tick.Count > 10 ? ParseTickInt(tick, 10) : 0;
        int riichiSticksCollected = tick.Count > 11 ? ParseTickInt(tick, 11) : 0;

        string huPosition = indexToPosition.ContainsKey(hepaiPlayerIndex) ? indexToPosition[hepaiPlayerIndex] : "self";
        RecordPlayer huPlayer = recordPlayer_to_info[huPosition];
        int[] hepaiPlayerHand = huPlayer.tileList.ToArray();
        // 槍槓（抢杠和）时和牌张为被抢的加杠牌，用 lastWinnableTileId 避免追加过期弃牌
        if (huClass != "hu_self" && lastWinnableTileId >= 0) {
            int[] newHand = new int[hepaiPlayerHand.Length + 1];
            Array.Copy(hepaiPlayerHand, newHand, hepaiPlayerHand.Length);
            newHand[hepaiPlayerHand.Length] = lastWinnableTileId;
            hepaiPlayerHand = newHand;
        }
        int[] hepaiPlayerHuapai = huPlayer.huapaiList.ToArray();
        int[][] hepaiPlayerCombinationMask = huPlayer.combinationMasks.ToArray();

        var deltas = new Dictionary<int, int>();
        int huScore = 0;
        if (scoreChanges != null && scoreChanges.Length >= 4) {
            MapTickScoreChangesToDeltas(scoreChanges, deltas);
            huScore = scoreChanges[hepaiPlayerIndex];
        }
        ApplyScoreDeltas(deltas, out Dictionary<int, int> playerToScoreBefore, out Dictionary<int, int> playerToScoreAfter);

        var extras = new RiichiEndResultExtras {
            Han = han,
            Fu = fu,
            AkaCount = akaCount,
            DoraCount = doraIndicators != null ? doraIndicators.Length : 0,
            UraDoraCount = uraDoraIndicators != null ? uraDoraIndicators.Length : 0,
            DoraIndicators = doraIndicators != null ? new List<int>(doraIndicators) : new List<int>(recordRiichiDoraIndicators),
            UraDoraIndicators = uraDoraIndicators != null ? new List<int>(uraDoraIndicators) : new List<int>(),
            Honba = honba,
            RiichiSticksCollected = riichiSticksCollected,
            ScoreChanges = deltas,
        };

        Dictionary<string, string> positionToUsername = new Dictionary<string, string>();
        foreach (var kv in recordPlayer_to_info) {
            int uid = kv.Value.userId;
            positionToUsername[kv.Key] = userIdToUsername.TryGetValue(uid, out string username) ? username : uid.ToString();
        }

        string roomType = "riichi/standard";
        GameCanvas.Instance.ShowActionDisplay(huPosition, huClass);
        SoundManager.Instance.PlayActionSound(huPosition, huClass);
        BoardCanvas.Instance.UpdatePlayerScores(playerToScoreAfter, indexToPosition);

        GameSceneUIManager.Instance.ShowRecordResult(hepaiPlayerIndex, huScore, yaku, huClass, roomType,
            indexToPosition, positionToUsername, hepaiPlayerHand, hepaiPlayerHuapai, hepaiPlayerCombinationMask,
            playerToScoreBefore, playerToScoreAfter, IsSpectating && IsLiveSpectatorMode, null, null, extras);
        if (riichiSticksCollected > 0) {
            Game3DManager.Instance.ClearAllRiichiTenbous();
        }
    }

    /// <summary>流局时清除 3D 立直棒；本场/供托由牌谱 round.riichi 元数据在切局时刷新 UI。</summary>
    private static void HideRecordRiichiSticksOnLiuju() {
        Game3DManager.Instance.ClearAllRiichiTenbous();
    }

    /// <summary>
    /// 牌谱流局后延迟切换下一局
    /// </summary>
    private System.Collections.IEnumerator GotoNextRoundAfterDelay(float delaySeconds) {
        yield return new WaitForSeconds(delaySeconds);
        int nextRound = currentRoundIndex + 1;
        if (gameRecord != null &&
            gameRecord.gameRound != null &&
            gameRecord.gameRound.rounds != null) {
            if (gameRecord.gameRound.rounds.ContainsKey(nextRound)) {
                GotoSelectRound(nextRound, false);
            } else {
                // 观战相关模式：到末局时不循环回第一局
                if (!IsSpectatorSession) {
                    GotoSelectRound(1, false);
                } else {
                    NotifyReachedLastAction();
                }
            }
        }
    }

    /// <summary>
    /// 牌谱和牌结算点击确认后，推进到下一个行动节点（可能是shuhewei或end）
    /// </summary>
    public void AdvanceToNextAction() {
        NextAction();
    }

    /// <summary>
    /// 牌谱和牌结算点击确认后，延迟0.1s切换下一局
    /// </summary>
    public void DelayedGotoNextRoundAfterConfirm(float delaySeconds) {
        StartCoroutine(DelayedGotoNextRoundAfterConfirmCoroutine(delaySeconds));
    }

    private System.Collections.IEnumerator DelayedGotoNextRoundAfterConfirmCoroutine(float delaySeconds) {
        yield return new WaitForSeconds(delaySeconds);
        if (gameRecord == null || gameRecord.gameRound?.rounds == null) yield break;
        int nextRound = currentRoundIndex + 1;
        if (gameRecord.gameRound.rounds.ContainsKey(nextRound)) {
            GotoSelectRound(nextRound, false);
        } else {
            // 观战牌谱模式：确认后到末局不循环
            if (!IsSpectatorSession) {
                GotoSelectRound(1, false);
            } else {
                NotifyReachedLastAction();
            }
        }
    }

    /// <summary>
    /// 流局动画结束后自动推进到下一个行动节点（end），实现自动跳转下一局
    /// </summary>
    private System.Collections.IEnumerator AutoNextActionAfterDelay(float delay) {
        yield return new WaitForSeconds(delay);
        if (gameRecord != null &&
            gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData) &&
            roundData.actionTicks != null &&
            currentNode < roundData.actionTicks.Count) {
            NextAction();
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

    private static void ApplyRecordAngangHandCardRemoval(List<int> removedTiles, bool isMoGang) {
        if (removedTiles == null || removedTiles.Count == 0) return;
        if (isMoGang) {
            GameCanvas.Instance.ChangeHandCards("RemoveGetCard", removedTiles[0], null, null);
            if (removedTiles.Count > 1) {
                GameCanvas.Instance.ChangeHandCards(
                    "RemoveCombinationCard", 0, removedTiles.Skip(1).ToArray(), null);
            }
        } else {
            GameCanvas.Instance.ChangeHandCards("RemoveCombinationCard", 0, removedTiles.ToArray(), null);
        }
    }

    private int[] BuildJiagangMask(RecordPlayer recordPlayer, int jiagangTile, int actualJiaTile) {
        for (int i = 0; i < recordPlayer.combinationTiles.Count; i++) {
            if (recordPlayer.combinationTiles[i] == $"k{jiagangTile}") {
                recordPlayer.combinationTiles[i] = $"g{jiagangTile}";
                List<int> updatedMask = new List<int>(recordPlayer.combinationMasks[i]);
                for (int j = 0; j < updatedMask.Count; j++) {
                    if (updatedMask[j] == 1) {
                        updatedMask.Insert(j, actualJiaTile);
                        updatedMask.Insert(j, 3);
                        break;
                    }
                }
                recordPlayer.combinationMasks[i] = updatedMask.ToArray();
                return recordPlayer.combinationMasks[i];
            }
        }
        int[] fallbackMask = new int[] { 0, jiagangTile, 3, actualJiaTile, 1, jiagangTile, 0, jiagangTile };
        recordPlayer.combinationTiles.Add($"g{jiagangTile}");
        recordPlayer.combinationMasks.Add(fallbackMask);
        return fallbackMask;
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

        int simulateCurrentPlayerIndex = roundData.startPlayerIndex;
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
        if (recordRoundItemPrefab == null) {
            Debug.LogError("BuildRoundNodeItems: recordRoundItemPrefab 未分配，无法创建局数按钮。");
            return;
        }
        
        foreach (Round round in _roundsListForInspector) {
            int honba = 0;
            if (rule == "riichi" && round.riichi != null) {
                honba = round.riichi.honba;
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

    private void InferAndMarkStartIndex() {
        startIndex = -1;
        if (!gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData)) {
            return;
        }
        if (roundData.actionTicks == null || roundData.actionTicks.Count == 0) {
            return;
        }
        for (int i = 0; i < roundData.actionTicks.Count; i++) {
            List<string> tick = roundData.actionTicks[i];
            if (tick == null || tick.Count == 0) continue;
            string action = tick[0];
            if (action == "bh" || action == "bd") continue;
            startIndex = i;
            break;
        }
    }

    /// <summary>
    /// 从牌谱构建带 score_history 的玩家数据并刷新计分表。按局顺序汇总四家 score_history（格式如 "+24","-8","0"），original_player_index 为 0～3。
    /// </summary>
    public void RefreshRecordScoreTable() {
        if (gameRecord?.gameRound?.rounds == null || ScoreHistoryPanel.Instance == null) return;
        string rule = ReadGameTitleString(gameRecord.gameTitle, "rule", "").ToLowerInvariant();
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
        // 每行对应的局号(current_round)，与日麻/国标对齐：连庄或错和会出现同一局号多行
        var roundNumberHistory = new List<int>();
        // 每次结算一行展开（国标同局多次错和各占一行），分值、局号、主番快照严格对齐
        var rows = ScoreHistoryRecordSettlementExtractor.ExtractScoreRows(gameRecord);
        var settlements = new List<RoundSettlementSnapshot>();
        foreach (var row in rows) {
            int[] sc = row.scoreChangesByOriginal;
            hist0.Add(FormatScoreChange(sc != null && sc.Length > 0 ? sc[0] : 0));
            hist1.Add(FormatScoreChange(sc != null && sc.Length > 1 ? sc[1] : 0));
            hist2.Add(FormatScoreChange(sc != null && sc.Length > 2 ? sc[2] : 0));
            hist3.Add(FormatScoreChange(sc != null && sc.Length > 3 ? sc[3] : 0));
            roundNumberHistory.Add(row.roundNumber > 0 ? row.roundNumber : roundNumberHistory.Count + 1);
            settlements.Add(row.snapshot);
        }
        var player_to_info = new Dictionary<string, PlayerInfoClass> {
            { "self", new PlayerInfoClass { original_player_index = 0, username = name0, score_history = hist0, round_number_history = new List<int>(roundNumberHistory) } },
            { "right", new PlayerInfoClass { original_player_index = 1, username = name1, score_history = hist1, round_number_history = new List<int>(roundNumberHistory) } },
            { "top", new PlayerInfoClass { original_player_index = 2, username = name2, score_history = hist2, round_number_history = new List<int>(roundNumberHistory) } },
            { "left", new PlayerInfoClass { original_player_index = 3, username = name3, score_history = hist3, round_number_history = new List<int>(roundNumberHistory) } }
        };
        // 总局数：牌谱标题 max_round（风圈数 1~4）* 4；缺省则用已记录局的最大局号兜底
        int recordMaxRound = ReadGameTitleInt(gameRecord.gameTitle, "max_round", 0);
        int totalRounds = recordMaxRound > 0 ? recordMaxRound * 4 : 0;
        ScoreHistoryPanel.Instance.UpdateScoreRecord(rule, player_to_info, settlements, totalRounds);
    }

    private static string FormatScoreChange(int delta) {
        if (delta > 0) return "+" + delta;
        if (delta < 0) return delta.ToString();
        return "0";
    }
}
