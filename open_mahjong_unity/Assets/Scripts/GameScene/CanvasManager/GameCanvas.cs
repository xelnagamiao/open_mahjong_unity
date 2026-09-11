using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
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

    public GamePlayerPanel PlayerSelfPanel => playerSelfPanel;
    public GamePlayerPanel PlayerLeftPanel => playerLeftPanel;
    public GamePlayerPanel PlayerTopPanel => playerTopPanel;
    public GamePlayerPanel PlayerRightPanel => playerRightPanel;

    [Header("操作界面")]
    [SerializeField] private Transform handCardsContainer; // 手牌容器（显示手牌 水平布局组）
    [SerializeField] private HandCardDragController handCardDragController;
    [SerializeField] private HandCardSelectionController handCardSelectionController;
    [SerializeField] private TMP_Text remianTimeText;        // 剩余时间文本(显示剩余时间[20+5])
    [SerializeField] public Transform ActionButtonContainer;  // 询问操作容器(显示吃,碰,杠,胡,补花,抢杠等按钮)
    [SerializeField] private Transform ExtraActionButton; // 场景常驻槽；当前仅供虹雀“补牌”使用
    [SerializeField] private Transform DedicatedActionButtonContainer; // 独立布局容器；清单 UsesDedicatedActionButtonContainer 时使用
    [SerializeField] public Transform ActionBlockContenter;  // 询问操作内容提示(显示吃,碰,杠,胡,补花,抢杠等按钮的多种结果)

    [Header("本家状态标记槽（族按槽名点亮，见 SetSelfStatusIndicator）")]
    [SerializeField] private GameObject selfFuritenIndicator;     // 槽 furiten：振听（日麻）
    [SerializeField] private GameObject langyongWaveIndicator;    // 槽 langyong_wave：浪潮模式（浪涌）
    [SerializeField] private GameObject selfShunheIndicator;      // 槽 shunhe：顺和跳过番（四川）
    [SerializeField] private TMP_Text selfShunheText;

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
    private Coroutine _processChangeHandCardQueueCoroutine;
    public string ActionBlockContainerState = "None"; // 操作块容器状态

    private float tileCardWidth; // 手牌预制体宽度

    public static GameCanvas Instance { get; private set; }
    public Transform HandCardsContainer => handCardsContainer;
    public RectTransform HandCardsContainerRect => handCardsContainer as RectTransform;
    public bool IsChangeHandCardProcessing => isChangeHandCardProcessing;
    public bool IsHandReflowAnimating => _handReflowAnimDepth > 0;
    public void SetHandArranged(bool value) { isArranged = value; }

    private int _handReflowAnimDepth;
    private int _handLayoutAnimEpoch;
    private Coroutine _sortMainHandCoroutine;
    private Coroutine _discardLayoutCoroutine;
    private bool _isScoreRecordOpen;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (handCardDragController == null) {
            handCardDragController = GetComponent<HandCardDragController>();
        }
        if (handCardDragController == null) {
            handCardDragController = gameObject.AddComponent<HandCardDragController>();
        }
        if (handCardSelectionController == null) {
            handCardSelectionController = GetComponent<HandCardSelectionController>();
        }
        if (handCardSelectionController == null) {
            handCardSelectionController = gameObject.AddComponent<HandCardSelectionController>();
        }
        // 获取tileCardPrefab的宽度
        tileCardWidth = tileCardPrefab.GetComponent<RectTransform>().rect.width;

        // 计分板开关按钮：点击切换打开/关闭，并同步按钮文本
        if (openScoreRecordButtonText == null) openScoreRecordButtonText = openScoreRecordPanelButton.GetComponentInChildren<TMP_Text>(true);
        openScoreRecordPanelButton.onClick.RemoveAllListeners();
        openScoreRecordPanelButton.onClick.AddListener(() => {
            SetScoreRecordOpen(!_isScoreRecordOpen); // 切换状态
            if (_isScoreRecordOpen) { ScoreHistoryPanel.Instance.gameObject.SetActive(true); GameSceneUIManager.Instance.UpdateScoreRecord(); } // 打开并由 UIManager 传入数据刷新
            else { ScoreHistoryPanel.Instance.Close(); } // 关闭并清理
        });
        SetScoreRecordOpen(false); // 初始化按钮文字与状态
        remianTimeText.text = ""; // 清空剩余时间文本
        InitializeStickerUi();
    }

    // 计分板被外部关闭时调整
    public void SetScoreRecordOpen(bool open) {
        _isScoreRecordOpen = open;
        if (openScoreRecordButtonText != null) {
            openScoreRecordButtonText.text = _isScoreRecordOpen ? "关闭计分板" : "打开计分板";
        }
    }

    /// <summary>
    /// 停止手牌处理协程并清空队列。在重连/下一局等清空手牌前调用，避免对已销毁的 RectTransform 继续执行动画。
    /// </summary>
    private void StopAndClearChangeHandCardQueue()
    {
        while (changeHandCardQueue.Count > 0) changeHandCardQueue.Dequeue();
        if (isChangeHandCardProcessing && _processChangeHandCardQueueCoroutine != null)
        {
            StopCoroutine(_processChangeHandCardQueueCoroutine);
            _processChangeHandCardQueueCoroutine = null;
            isChangeHandCardProcessing = false;
        }
        _handReflowAnimDepth = 0;
        ClearPendingLocalCuts();
    }

    public void ClearHandCardQueue() {
        StopAndClearChangeHandCardQueue();
    }

    /// <summary>发切前锁定本张；回包删牌优先认它。</summary>
    public void MarkPendingLocalCut(TileCard card) {
        ClearPendingLocalCuts();
        if (card != null) {
            card.pendingLocalCut = true;
        }
    }

    public void ClearPendingLocalCuts() {
        if (handCardsContainer == null) {
            return;
        }
        for (int i = 0; i < handCardsContainer.childCount; i++) {
            TileCard tc = handCardsContainer.GetChild(i).GetComponent<TileCard>();
            if (tc != null) {
                tc.pendingLocalCut = false;
            }
        }
    }

    /// <summary>
    /// 退出对局/牌谱/观战后还原 2D 玩家信息与手牌区。
    /// </summary>
    public void ResetForExit() {
        ClearHandCardQueue();
        HandCardSelectionController.Instance.DisarmAll();
        for (int i = handCardsContainer.childCount - 1; i >= 0; i--) {
            Destroyer.Instance.AddToDestroyer(handCardsContainer.GetChild(i));
        }
        playerSelfPanel?.Clear();
        playerLeftPanel?.Clear();
        playerTopPanel?.Clear();
        playerRightPanel?.Clear();
        RestoreSeatPanels();
        StopTimeRunning();
        ClearActionButton();
        // 退出前在 SetActive(false) 之前销毁残留的操作文本，避免「补花」等字卡死下次进入
        ClearActionDisplay();
        HideStickerPanel();
        ClearAllStickers();
        HideAllPlayerActionMenus();
        SetScoreRecordOpen(false);
        HideDingqueSelection();
        playerSelfPanel?.SetDingque(0);
        playerLeftPanel?.SetDingque(0);
        playerTopPanel?.SetDingque(0);
        playerRightPanel?.SetDingque(0);
        ClearSelfStatusIndicators();
        gameObject.SetActive(false);
    }

    // 初始化游戏UI
    public void InitializeUIInfo(GameInfo gameInfo,Dictionary<int, string> indexToPosition){
        gameObject.SetActive(true);
        ConfigureHandLayoutForRule(gameInfo?.room_rule, gameInfo?.sub_rule);
        StopAndClearChangeHandCardQueue();
        ClearActionDisplay();
        ClearActionButton();
        SetActionButtonContainerVisible(true);
        HideStickerPanel();
        ClearAllStickers();
        HideAllPlayerActionMenus();
        SetStickerUiForRecordMode(false);
        HideDingqueSelection();
        ApplyOccupiedSeatPanels(indexToPosition);
        // 新一局开始先清空各家定缺标记，待服务端定缺同步后再显示
        playerSelfPanel?.SetDingque(0);
        playerLeftPanel?.SetDingque(0);
        playerTopPanel?.SetDingque(0);
        playerRightPanel?.SetDingque(0);
        HandCardSelectionController.Instance.DisarmAll();
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
                targetPanel.SetPlayerInfo(player, "gamestate", position);
            }
            else
            {
                Debug.LogWarning($"未找到位置 {position} 对应的玩家面板");
            }
        }

        // 更新轮数面板信息
        if (roundPanel != null) {
            var gm = NormalGameStateManager.Instance;
            string ruleForPanel = !string.IsNullOrEmpty(gm.subRule) ? gm.subRule : gameInfo?.sub_rule;
            roundPanel.UpdateRoomInfo(gameInfo, ruleForPanel);
        } else {
            Debug.LogWarning("RoundPanel reference is not set in GameCanvas!");
        }

        // 族的 OnGameStart 随后再按重连状态点亮自己的标记
        ClearSelfStatusIndicators();
    }

    // 从牌谱记录初始化游戏UI
    public void InitializeUIInfoFromRecord(List<GameRecordManager.RecordPlayer> recordPlayerList, Dictionary<int, string> indexToPosition, Dictionary<int, string> userIdToUsername) {
        gameObject.SetActive(true);
        StopAndClearChangeHandCardQueue();
        ClearActionDisplay();
        ClearActionButton();
        SetActionButtonContainerVisible(true);
        HideStickerPanel();
        ClearAllStickers();
        HideAllPlayerActionMenus();
        SetStickerUiForRecordMode(true);
        HideDingqueSelection();
        playerSelfPanel?.SetDingque(0);
        playerLeftPanel?.SetDingque(0);
        playerTopPanel?.SetDingque(0);
        playerRightPanel?.SetDingque(0);
        HandCardSelectionController.Instance.DisarmAll();
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
                targetPanel.SetPlayerInfo(playerInfo, "record");
            }
            else
            {
                Debug.LogWarning($"未找到位置 {position} 对应的玩家面板");
            }
        }
        ApplyOccupiedSeatPanels(indexToPosition);
        ClearSelfStatusIndicators();
    }

    // 从牌谱记录更新左上房间信息
    public void UpdateRoomInfoFromRecord(GameRecord gameRecord, int currentRoundIndex) {
        string roomRule = ReadStringValue(gameRecord.gameTitle, "rule", "");
        string subRule = ReadStringValue(gameRecord.gameTitle, "sub_rule", "");
        string recordRoomType = ReadStringValue(gameRecord.gameTitle, "room_type", "");
        ConfigureHandLayoutForRule(roomRule, subRule);
        string ruleForPanel = subRule;
        int maxRound = ReadIntValue(gameRecord.gameTitle, "max_round", 0);

        int currentRound = currentRoundIndex;
        int honba = 0;
        int riichiSticks = 0;
        if (gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData)) {
            if (roundData.currentRound > 0) {
                currentRound = roundData.currentRound;
            }
            if (roundData.riichi != null) {
                honba = roundData.riichi.honba;
                riichiSticks = roundData.riichi.riichiSticks;
            }
        }

        GameInfo gameInfo = new GameInfo {
            room_type = recordRoomType,
            room_rule = roomRule,
            sub_rule = subRule,
            hepai_limit = ReadIntValue(gameRecord.gameTitle, "hepai_limit", 0),
            max_round = maxRound,
            current_round = currentRound,
            commitment = CommitmentSaltDisplay.ReadCommitmentFromGameTitle(gameRecord.gameTitle),
            salt = CommitmentSaltDisplay.ReadSaltFromGameTitle(gameRecord.gameTitle),
            honba = honba,
            riichi_sticks = riichiSticks,
            open_cuohe = ReadBoolValue(gameRecord.gameTitle, "open_cuohe", false),
            tips = ReadBoolValue(gameRecord.gameTitle, "tips", false),
            isPlayerSetRandomSeed = ReadBoolValue(gameRecord.gameTitle, "is_player_set_random_seed", false)
                || ReadBoolValue(gameRecord.gameTitle, "isPlayerSetRandomSeed", false)
        };
        roundPanel.UpdateRoomInfo(
            gameInfo,
            ruleForPanel,
            CommitmentSaltDisplay.ReadMasterSeedFromGameTitle(gameRecord.gameTitle));
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
    public void UpdatePlayerTagList(
        Dictionary<int, string[]> player_to_tag_list,
        IReadOnlyDictionary<int, string> player_positions = null,
        string roomRule = null
    ) {
        bool useLiveContext = player_positions == null;
        if (useLiveContext) {
            player_positions = NormalGameStateManager.Instance.indexToPosition;
        }
        if (player_to_tag_list == null || player_positions == null) return;

        foreach (var kvp in player_to_tag_list) {
            int player_index = kvp.Key;
            string[] tag_list = kvp.Value;

            // 根据 player_index 找到对应的玩家位置和面板
            if (player_positions.TryGetValue(player_index, out string position)) {
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
                    targetPanel.UpdateTagList(tag_list, roomRule);
                }
            }
        }
        if (useLiveContext) RefreshSelfStatusIndicators();
    }

    // ---------------------------------------------------------------------
    // 本家状态标记槽：场景里预置若干小标记（振听 / 顺和 / 浪潮……），核心只管清空与按槽名点亮，
    // 由族在 OnGameStart / OnPlayerTagsRefreshed 里决定亮哪个、写什么字。
    // ---------------------------------------------------------------------

    /// <summary>状态标记槽名。</summary>
    public const string StatusSlotFuriten = "furiten";
    public const string StatusSlotShunhe = "shunhe";
    public const string StatusSlotLangyongWave = "langyong_wave";

    private struct StatusSlot {
        public GameObject Root;
        public TMP_Text Text;
    }

    private Dictionary<string, StatusSlot> statusSlots;

    private Dictionary<string, StatusSlot> StatusSlots {
        get {
            if (statusSlots == null) {
                statusSlots = new Dictionary<string, StatusSlot> {
                    { StatusSlotFuriten, new StatusSlot { Root = selfFuritenIndicator } },
                    { StatusSlotShunhe, new StatusSlot { Root = selfShunheIndicator, Text = selfShunheText } },
                    { StatusSlotLangyongWave, new StatusSlot { Root = langyongWaveIndicator } },
                };
            }
            return statusSlots;
        }
    }

    /// <summary>收起全部状态标记。标签刷新 / 开局 / 退出时调用，之后由族按需再点亮。</summary>
    public void ClearSelfStatusIndicators() {
        foreach (StatusSlot slot in StatusSlots.Values) {
            if (slot.Root != null) slot.Root.SetActive(false);
        }
    }

    /// <summary>点亮 / 收起一个状态标记；text 非空时写入槽内文字。未配置的槽静默忽略。</summary>
    public void SetSelfStatusIndicator(string slotName, bool visible, string text = null) {
        if (!StatusSlots.TryGetValue(slotName, out StatusSlot slot) || slot.Root == null) return;
        slot.Root.SetActive(visible);
        if (visible && text != null && slot.Text != null) slot.Text.text = text;
    }

    /// <summary>清空后让当前族重新点亮自己的标记（对局路径；牌谱路径只清空）。</summary>
    public void RefreshSelfStatusIndicators() {
        ClearSelfStatusIndicators();
        RuleRegistry.ActiveGameState?.RefreshSelfStatusIndicators();
    }

    public void ClearActionButton() {
        ActionBlockContainerState = "None";
        foreach (Transform child in ActionBlockContenter){
            Destroy(child.gameObject);
        }
        ClearSpawnedActionButtons(ActionButtonContainer);
        ClearSpawnedActionButtons(DedicatedActionButtonContainer);
        SetPersistentActionButton(null);
        // 吃碰杠询问结束（执行/跳过/超时/轮到下家）：隐藏可操作牌底部的光圈。
        Game3DManager.Instance?.HideClaimGlow();
    }

    /// <summary>
    /// 立直选牌模式下隐藏/恢复所有操作按钮容器。
    /// 清单声明独立容器时只亮那个；默认容器仍给虹雀常驻槽使用。
    /// </summary>
    public void SetActionButtonContainerVisible(bool visible) {
        Transform host = ActionButtonHost;
        if (ActionButtonContainer != null && ActionButtonContainer != host) {
            ActionButtonContainer.gameObject.SetActive(false);
        }
        if (DedicatedActionButtonContainer != null && DedicatedActionButtonContainer != host) {
            DedicatedActionButtonContainer.gameObject.SetActive(false);
        }
        if (host != null) host.gameObject.SetActive(visible);
    }

    internal Transform ActionButtonHost {
        get {
            if (RuleRegistry.Current != null
                && RuleRegistry.Current.UsesDedicatedActionButtonContainer
                && DedicatedActionButtonContainer != null) {
                return DedicatedActionButtonContainer;
            }
            return ActionButtonContainer;
        }
    }

    private void ClearSpawnedActionButtons(Transform container) {
        if (container == null) return;
        foreach (Transform child in container) {
            if (child != ExtraActionButton) Destroy(child.gameObject);
        }
    }

    /// <summary>没有玩家的座位隐藏面板；有人的座位显示。不写规则名。</summary>
    public void ApplyOccupiedSeatPanels(Dictionary<int, string> indexToPosition) {
        var occupied = OccupiedSeats(indexToPosition);
        SetSeatPanelActive(playerSelfPanel, occupied.Contains("self"));
        SetSeatPanelActive(playerLeftPanel, occupied.Contains("left"));
        SetSeatPanelActive(playerTopPanel, occupied.Contains("top"));
        SetSeatPanelActive(playerRightPanel, occupied.Contains("right"));
    }

    private void RestoreSeatPanels() {
        SetSeatPanelActive(playerSelfPanel, true);
        SetSeatPanelActive(playerLeftPanel, true);
        SetSeatPanelActive(playerTopPanel, true);
        SetSeatPanelActive(playerRightPanel, true);
    }

    private static void SetSeatPanelActive(GamePlayerPanel panel, bool active) {
        if (panel == null) return;
        panel.gameObject.SetActive(active);
        if (!active) panel.Clear();
    }

    internal static HashSet<string> OccupiedSeats(Dictionary<int, string> indexToPosition) {
        var occupied = new HashSet<string>();
        if (indexToPosition == null) return occupied;
        foreach (string seat in indexToPosition.Values) {
            if (!string.IsNullOrEmpty(seat)) occupied.Add(seat);
        }
        return occupied;
    }

    /// <summary>四川：UI 手牌中是否仍含定缺花色（与 selfHandTiles 双源校验，避免列表不同步导致无法置灰）。</summary>
    public bool SelfHandHasDingqueSuitTile(int dingqueSuit) {
        if (handCardsContainer == null || dingqueSuit < 1 || dingqueSuit > 3) return false;
        for (int i = 0; i < handCardsContainer.childCount; i++) {
            TileCard tc = handCardsContainer.GetChild(i).GetComponent<TileCard>();
            if (tc != null && tc.tileId / 10 == dingqueSuit) return true;
        }
        return false;
    }

    /// <summary>
    /// 根据当前立直/食替/已立直锁手/四川定缺状态统一刷新自家手牌的可点状态：
    /// - 处于立直选牌模式：仅 riichi_candidate_cuts 中存在的 tile_id 可点；
    /// - 自家已立直/报听：仅最右摸入牌（currentGetTile=true）可点，其余全部置灰；
    /// - 四川定缺：手牌仍含定缺花色时仅定缺花色可点（须优先打出）；
    /// - 强制出牌阶段：仅服务端指定的摸入牌可点；
    /// - 普通切牌阶段：按服务端下发的 forbidden_cut_tiles 禁点（食替）。
    /// </summary>
    public void RefreshHandTileSelectability() {
        if (handCardsContainer == null) return;
        bool inRiichiCutMode = RiichiCutSelectionController.Instance.IsActive;
        var candidates = NormalGameStateManager.Instance.selfRiichiCandidateCuts;
        var forbidden = NormalGameStateManager.Instance.selfForbiddenCutTiles;
        var forced = NormalGameStateManager.Instance.selfForcedCutTiles;
        bool hasForcedCut = forced != null
            && forced.Count > 0
            && NormalGameStateManager.Instance.allowActionList.Contains("cut");
        // 锁手（日麻立直 / 台麻报听）与规则切牌限制（四川定缺先打）由族回答
        IGameState state = RuleRegistry.ActiveGameState;
        bool selfReadyLocked = state?.IsSelfLocked == true;
        for (int i = 0; i < handCardsContainer.childCount; i++) {
            TileCard tc = handCardsContainer.GetChild(i).GetComponent<TileCard>();
            if (tc == null) continue;
            bool selectable;
            if (hasForcedCut) {
                selectable = tc.currentGetTile && forced.Contains(tc.tileId);
            } else if (inRiichiCutMode) {
                selectable = candidates.ContainsKey(tc.tileId);
            } else if (selfReadyLocked) {
                selectable = tc.currentGetTile;
            } else {
                selectable = !forbidden.Contains(tc.tileId) && (state == null || state.CanCutTile(tc.tileId));
            }
            tc.SetSelectable(selectable);
        }
    }

    /// <summary>
    /// 指针是否落在自家手牌容器内的 TileCard 上（含牌面/槽位等子 UI）。
    /// </summary>
    public bool IsPointerOverSelfHandCard(Vector2 screenPosition) {
        if (handCardsContainer == null || EventSystem.current == null) {
            return false;
        }
        PointerEventData pointerData = new PointerEventData(EventSystem.current) {
            position = screenPosition
        };
        List<RaycastResult> results = new List<RaycastResult>(8);
        EventSystem.current.RaycastAll(pointerData, results);
        for (int i = 0; i < results.Count; i++) {
            if (results[i].gameObject == null) {
                continue;
            }
            TileCard tileCard = results[i].gameObject.GetComponentInParent<TileCard>();
            if (tileCard != null && tileCard.transform.IsChildOf(handCardsContainer)) {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 摸切快捷 / 自动出牌：优先打出摸牌张，否则打出 handSortIndex 最大的手牌；
    /// 只在族允许打出（IGameState.CanCutTile，如四川定缺先打）的牌里选。
    /// </summary>
    public bool TriggerMoqieHandCardClick() {
        HandCardSelectionController.Instance.DisarmAll();
        if (handCardsContainer == null) {
            Debug.LogWarning("手牌容器为空，无法触发自动出牌");
            return false;
        }

        IGameState state = RuleRegistry.ActiveGameState;
        bool restricted = false;

        TileCard drawTileCard = null;
        TileCard rightmostTileCard = null;
        int maxHandSortIndex = -1;

        for (int i = 0; i < handCardsContainer.childCount; i++) {
            TileCard tileCard = handCardsContainer.GetChild(i).GetComponent<TileCard>();
            if (tileCard == null) continue;
            if (state != null && !state.CanCutTile(tileCard.tileId)) {
                restricted = true;
                continue;
            }
            if (tileCard.currentGetTile) {
                drawTileCard = tileCard;
            }
            if (tileCard.handSortIndex > maxHandSortIndex) {
                maxHandSortIndex = tileCard.handSortIndex;
                rightmostTileCard = tileCard;
            }
        }

        TileCard targetTileCard = drawTileCard ?? rightmostTileCard;
        if (targetTileCard == null || !targetTileCard.IsSelectableForCut()) {
            Debug.LogWarning("自动出牌失败：手牌容器中没有可出的牌");
            return false;
        }

        targetTileCard.TriggerClick();
        Debug.Log($"自动出牌：触发牌ID {targetTileCard.tileId}，摸切={targetTileCard.currentGetTile}，排序位置={targetTileCard.handSortIndex}，规则限切={restricted}");
        return true;
    }

}
