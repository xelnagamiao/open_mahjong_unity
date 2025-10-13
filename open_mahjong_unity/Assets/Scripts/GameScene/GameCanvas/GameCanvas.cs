using System.Collections;
using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public partial class GameCanvas : MonoBehaviour
{
    [Header("左上房间信息")]
    [SerializeField] private Text roomRoundText; // 房间轮数文本
    [SerializeField] private Text roomNowRoundText; // 当前轮数文本
    [SerializeField] private Text RandomSeedText; // 随机种子文本
    [SerializeField] private Button visibilityRandomSeedButton; // 显示随机种子按钮

    [Header("玩家信息")]
    [SerializeField] private Text player_self_name;        // 玩家名称文本
    [SerializeField] private Image player_self_profile_picture;       // 玩家头像
    [SerializeField] private Text player_left_name;          // 玩家名称文本
    [SerializeField] private Image player_left_profile_picture;       // 玩家头像
    [SerializeField] private Text player_top_name;           // 玩家名称文本
    [SerializeField] private Image player_top_profile_picture;       // 玩家头像
    [SerializeField] private Text player_right_name;         // 玩家名称文本
    [SerializeField] private Image player_right_profile_picture;       // 玩家头像

    [Header("操作界面")]
    [SerializeField] private Transform handCardsContainer; // 手牌容器（显示手牌 水平布局组）
    [SerializeField] private Text remianTimeText;        // 剩余时间文本(显示剩余时间[20+5])
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
    private bool isAutoArrangeHandCards = true; // 是否自动排列手牌
    private bool isArranged = false; // 是否已经排列过手牌
    
    // 手牌处理队列管理
    private Queue<System.Func<Coroutine>> changeHandCardQueue = new Queue<System.Func<Coroutine>>();
    private bool isChangeHandCardProcessing = false;
    public string ActionBlockContainerState = "None"; // 操作块容器状态
    public bool isDontDoAction = false; // 是否不吃碰杠
    public bool isAutoHepai = false; // 是否自动胡牌
    public bool isAutoCutCard = false; // 是否自动出牌

    private float tileCardWidth; // 手牌预制体宽度

    public static GameCanvas Instance { get; private set; }
    
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
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
        foreach (var player in gameInfo.players_info){
            if (indexToPosition[player.player_index] == "self"){ // 通过player_index确定玩家位置
                player_self_name.text = player.username; // 设置玩家名称
                // 根据头像id设置头像
                // player_self_profile_picture.sprite = Resources.Load<Sprite>($"HeadIcon/{player.head_icon}");
            }
            else if (indexToPosition[player.player_index] == "right"){
                player_right_name.text = player.username;
                // player_self_profile_picture.sprite = Resources.Load<Sprite>($"HeadIcon/{player.head_icon}");
            }
            else if (indexToPosition[player.player_index] == "top"){
                player_top_name.text = player.username;
                // player_self_profile_picture.sprite = Resources.Load<Sprite>($"HeadIcon/{player.head_icon}");
            }
            else if (indexToPosition[player.player_index] == "left"){
                player_left_name.text = player.username;
                // player_self_profile_picture.sprite = Resources.Load<Sprite>($"HeadIcon/{player.head_icon}");
            }
        }
        roomRoundText.text = $"圈数：{gameInfo.current_round}"; // 左上角显示需要打的圈数
        roomNowRoundText.text = $"当前轮数：{gameInfo.current_round}"; // 左上角显示目前打的圈数
        RandomSeedText.text = $"随机种子：{gameInfo.random_seed}"; // 左上角显示随机种子
    }



}
