using System.Collections;
using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.UI;

public class GameCanvas : MonoBehaviour
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
    [SerializeField] private Transform GetCardsContainer;  // 获得牌容器 (显示摸牌)
    [SerializeField] private Text remianTimeText;        // 剩余时间文本(显示剩余时间[20+5])
    [SerializeField] private Transform ActionButtonContainer;  // 询问操作容器(显示吃,碰,杠,胡,补花,抢杠等按钮)
    [SerializeField] private Transform ActionBlockContenter;  // 询问操作内容提示(显示吃,碰,杠,胡,补花,抢杠等按钮的多种结果)

    [Header("预制体")]
    [SerializeField] private ActionButton ActionButtonPrefab;  // 询问操作按钮预制体[吃,碰,杠,胡,补花,抢杠]
    [SerializeField] private GameObject ActionBlockPrefab;  // 静态牌容器块(在操作按钮有多种结果时显示)
    [SerializeField] private GameObject StaticCardPrefab;  // 静态牌预制体(包含在多种结果显示中的图像)
    [SerializeField] private GameObject tileCardPrefab;    // 手牌预制体(可以点击的出牌图像)

    [Header("游戏配置模块")]
    private bool isArrangeHandCards = true; // 是否排列手牌
    private bool isDontDoAction = false; // 是否不吃碰杠
    private bool isAutoHepai = false; // 是否自动胡牌
    private bool isAutoCutCard = false; // 是否自动出牌

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
    }

    public void InitializeUIInfo(GameInfo gameInfo,Dictionary<int, string> indexToPosition){
        foreach (var player in gameInfo.players_info){
            if (indexToPosition[player.player_index] == "self"){ // 通过player_index确定玩家位置
                player_self_name.text = player.username; // 设置玩家名称
                // 根据头像id设置头像
                // player_self_profile_picture.sprite = Resources.Load<Sprite>($"HeadIcon/{player.head_icon}");
            }
            else if (indexToPosition[player.player_index] == "left"){
                player_left_name.text = player.username;
                // player_self_profile_picture.sprite = Resources.Load<Sprite>($"HeadIcon/{player.head_icon}");
            }
            else if (indexToPosition[player.player_index] == "top"){
                player_top_name.text = player.username;
                // player_self_profile_picture.sprite = Resources.Load<Sprite>($"HeadIcon/{player.head_icon}");
            }
            else if (indexToPosition[player.player_index] == "right"){
                player_right_name.text = player.username;
                // player_self_profile_picture.sprite = Resources.Load<Sprite>($"HeadIcon/{player.head_icon}");
            }
        }
        roomRoundText.text = $"圈数：{gameInfo.current_round}"; // 左上角显示需要打的圈数
        roomNowRoundText.text = $"当前轮数：{gameInfo.current_round}"; // 左上角显示目前打的圈数
        RandomSeedText.text = $"随机种子：{gameInfo.random_seed}"; // 左上角显示随机种子
    }

    // 初始化手牌区域 
    public void InitializeHandCards(int[] handTiles, int currentPlayerIndex){
        // 清空现有手牌
        foreach (Transform child in handCardsContainer)
        {
            Destroy(child.gameObject);
        }
        // 实例化手牌 如果当前玩家是自己,则不实例化最后一张牌
        int cardCount = 0;  // 从0开始计数
        foreach (int tile in handTiles)
        {
            if (cardCount == Administrator.Instance.hand_tiles_count - 1 && GameSceneManager.Instance.selfIndex == currentPlayerIndex)
            {
                GameObject lastCardObj = Instantiate(tileCardPrefab, GetCardsContainer);
                TileCard lastTileCard = lastCardObj.GetComponent<TileCard>();
                lastTileCard.SetTile(tile, true);
                break;
            }
            // 创建牌物体
            GameObject cardObj = Instantiate(tileCardPrefab, handCardsContainer);
            
            // 设置牌面
            TileCard tileCard = cardObj.GetComponent<TileCard>();
            tileCard.SetTile(tile, false);
            cardCount++;
        }
    }

    public void GetCard(int tileId){
        GameObject cardObj = Instantiate(tileCardPrefab, GetCardsContainer);
        TileCard tileCard = cardObj.GetComponent<TileCard>();
        tileCard.SetTile(tileId, false);
    }

    public void ArrangeHandCards() {
        // 获取所有子对象并存入列表
        if (isArrangeHandCards){
            List<TileCard> cards = new List<TileCard>();
            foreach (Transform child in handCardsContainer) {
                TileCard tileCard = child.GetComponent<TileCard>();
                if (tileCard != null) {
                    cards.Add(tileCard);
                }
            }
            // 直接按tileId排序
            cards.Sort((a, b) => a.tileId.CompareTo(b.tileId));
            // 重新排列子对象
            for (int i = 0; i < cards.Count; i++) {
                cards[i].transform.SetSiblingIndex(i);
            }
        }
    }

        // 启动倒计时
    public void loadingRemianTime(int remainingTime, int cuttime){
        // 停止可能正在运行的倒计时协程
        if (_countdownCoroutine != null)
            StopCoroutine(_countdownCoroutine);
        
        // 保存初始时间值
        _currentRemainingTime = remainingTime;
        _currentCutTime = cuttime;
        
        // 更新UI显示
        if (_currentCutTime > 0){
            remianTimeText.text = $"剩余时间: {_currentRemainingTime}+{_currentCutTime}";
        }
        else{
            remianTimeText.text = $"剩余时间: {_currentRemainingTime}";
        }
        
        // 根据剩余时间改变文本颜色
        if (_currentRemainingTime <= 5 && _currentCutTime <= 0)
        {
            remianTimeText.color = Color.red; // 时间不多时显示红色
        }
        else
        {
            remianTimeText.color = Color.white; // 正常时间显示白色
        }
        if (_currentRemainingTime == 0){
            remianTimeText.text = $""; // 如果剩余时间为0，则不显示剩余时间
            StopTimeRunning();
        }
        
        // 启动新的倒计时协程
        _countdownCoroutine = StartCoroutine(CountdownTimer());
    }

    // 倒计时协程
    private IEnumerator CountdownTimer(){
        // 使用WaitForSeconds缓存，提高性能
        WaitForSeconds oneSecondWait = new WaitForSeconds(1.0f);
        
        while (_currentCutTime > 0 || _currentRemainingTime > 0)
        {
            // 等待1秒
            yield return oneSecondWait;
            
            // 先减少切牌时间
            if (_currentCutTime > 0){
                _currentCutTime--;
            }
            else if (_currentRemainingTime > 0){
                _currentRemainingTime--;
            }
            
            // 更新UI显示
            if (_currentCutTime > 0){
                remianTimeText.text = $"剩余时间: {_currentRemainingTime}+{_currentCutTime}";
            }
            else{
                remianTimeText.text = $"剩余时间: {_currentRemainingTime}";
            }
            
            // 根据剩余时间改变文本颜色
            if (_currentRemainingTime <= 5 && _currentCutTime <= 0)
            {
                remianTimeText.color = Color.red; // 时间不多时显示红色
            }
            else
            {
                remianTimeText.color = Color.white; // 正常时间显示白色
            }
            if (_currentRemainingTime == 0){
                remianTimeText.text = $""; // 如果剩余时间为0，则不显示剩余时间
            }
            
            // 可以添加时间快要结束时的警告效果
            if (_currentRemainingTime <= 5 && _currentCutTime <= 0)
            {
                // 检查是否已经在闪烁
                if (remianTimeText.GetComponent<CanvasGroup>()?.alpha >= 1)
                {
                    CanvasGroup canvasGroup = remianTimeText.gameObject.GetComponent<CanvasGroup>();
                    if (canvasGroup == null)
                        canvasGroup = remianTimeText.gameObject.AddComponent<CanvasGroup>();
                    
                    // 闪烁3次
                    for (int i = 0; i < 3; i++)
                    {
                        // 渐隐
                        for (float alpha = 1f; alpha >= 0.3f; alpha -= 0.1f)
                        {
                            canvasGroup.alpha = alpha;
                            yield return new WaitForSeconds(0.05f);
                        }
                        
                        // 渐显
                        for (float alpha = 0.3f; alpha <= 1f; alpha += 0.1f)
                        {
                            canvasGroup.alpha = alpha;
                            yield return new WaitForSeconds(0.05f);
                        }
                    }
                }
            }
        }
        Debug.Log("倒计时结束！");
    }


    public void StopTimeRunning(){
        if (_countdownCoroutine != null) {
            StopCoroutine(_countdownCoroutine);
            _countdownCoroutine = null; // 设置为null以避免重复停止
        }
        remianTimeText.text = $""; // 隐藏倒计时文本
        ClearActionContenter();
    }
}
