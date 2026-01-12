using System.Collections;
using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public partial class GameCanvas : MonoBehaviour {
    [Header("左上房间信息")]
    [SerializeField] private TMP_Text ruleText; // 规则文本
    [SerializeField] private TMP_Text roomNowRoundText; // 当前轮数文本
    [SerializeField] private TMP_Text RandomSeedText; // 随机种子文本
    [SerializeField] private Button visibilityRandomSeedButton; // 显示随机种子按钮

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
    
    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // 获取tileCardPrefab的宽度 
        tileCardWidth = tileCardPrefab.GetComponent<RectTransform>().rect.width;
    }

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

        RandomSeedText.text = $"随机种子：{gameInfo.round_random_seed}"; // 左上角显示随机种子

        // 设置当前轮数文本
        if (gameInfo.current_round == 1){
            roomNowRoundText.text = "东一局";
        } else if (gameInfo.current_round == 2){
            roomNowRoundText.text = "东二局";
        } else if (gameInfo.current_round == 3){
            roomNowRoundText.text = "东三局";
        } else if (gameInfo.current_round == 4){
            roomNowRoundText.text = "东四局";
        } else if (gameInfo.current_round == 5){
            roomNowRoundText.text = "南一局";
        } else if (gameInfo.current_round == 6){
            roomNowRoundText.text = "南二局";
        } else if (gameInfo.current_round == 7){
            roomNowRoundText.text = "南三局";
        } else if (gameInfo.current_round == 8){
            roomNowRoundText.text = "南四局";
        } else if (gameInfo.current_round == 9){
            roomNowRoundText.text = "西一局";
        } else if (gameInfo.current_round == 10){
            roomNowRoundText.text = "西二局";
        } else if (gameInfo.current_round == 11){
            roomNowRoundText.text = "西三局";
        } else if (gameInfo.current_round == 12){
            roomNowRoundText.text = "西四局";
        } else if (gameInfo.current_round == 13){
            roomNowRoundText.text = "北一局";
        } else if (gameInfo.current_round == 14){
            roomNowRoundText.text = "北二局";
        } else if (gameInfo.current_round == 15){
            roomNowRoundText.text = "北三局";
        } else if (gameInfo.current_round == 16){
            roomNowRoundText.text = "北四局";
        } else {
            roomNowRoundText.text = "未知轮数";
        }

        // 设置规则文本
        string roomRoundText = "";
        if (gameInfo.room_type == "guobiao"){
            roomRoundText += "国标麻将:";
        } else {
            roomRoundText += "未知规则：";
        }
        if (gameInfo.max_round == 1){
            roomRoundText += "东风战";
        } else if (gameInfo.max_round == 2){
            roomRoundText += "东南战";
        } else if (gameInfo.max_round == 3){
            roomRoundText += "西风战";
        } else if (gameInfo.max_round == 4){
            roomRoundText += "全庄战";
        } else {Debug.LogError("最大轮数错误");}
        ruleText.text = roomRoundText;

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
