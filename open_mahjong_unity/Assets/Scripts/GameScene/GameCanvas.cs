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

    [Header("时间配置模块")]
    private Coroutine _countdownCoroutine;
    private int _currentRemainingTime;
    private int _currentCutTime;

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

    // 初始化游戏UI
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
    public void InitializeHandCards(int[] handTiles){
        // 清空现有手牌
        foreach (Transform child in handCardsContainer)
        {
            Destroy(child.gameObject);
        }
        // 实例化手牌 如果当前玩家是自己,则不实例化最后一张牌
        int cardCount = 1;  // 从1开始计数
        foreach (int tile in handTiles)
        {
            // 如果卡牌是第十四张,则不创建,因为手牌应当创建在摸牌区
            if (cardCount == 14){break;}
            // 创建牌进入手牌区
            GameObject cardObj = Instantiate(tileCardPrefab, handCardsContainer);
            TileCard tileCard = cardObj.GetComponent<TileCard>();
            tileCard.SetTile(tile, false);
            cardCount++;
        }
    }
    
    // 摸牌 手切区域
    public void GetCard(int tileId){
        GameObject cardObj = Instantiate(tileCardPrefab, GetCardsContainer); // 实例化手牌
        TileCard tileCard = cardObj.GetComponent<TileCard>(); // 获取手牌组件
        tileCard.SetTile(tileId, true); // 设置手牌 牌id,是否是刚摸到的牌bool
    }

    // 显示可用行动按钮
    public void SetActionButton(string[] action_list){
    for (int i = 0; i < action_list.Length; i++){
        // 用于跟踪吃牌按钮
        ActionButton chiButton = null;
        Debug.Log($"询问操作: {action_list[i]}");
        if (action_list[i] == "chi_left" || action_list[i] == "chi_right" || action_list[i] == "chi_mid"){
            if (chiButton == null)
            {
                // 第一次遇到吃牌选项时创建按钮
                chiButton = Instantiate(ActionButtonPrefab, ActionButtonContainer);
                Text buttonText = chiButton.TextObject;
                buttonText.text = "吃";
                Debug.Log($"创建吃牌按钮: {chiButton}");
            }
            // 将当前的吃牌选项添加到已存在的吃牌按钮中
            chiButton.actionTypeList.Add(action_list[i]);
            Debug.Log($"添加吃牌选项: {action_list[i]}");
        }
        else if (action_list[i] == "peng"){
            Debug.Log($"碰牌");
            ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer); // 实例化按钮
            Text buttonText = ActionButtonObj.TextObject;
            buttonText.text = "碰"; // 设置按钮文本
            Debug.Log($"碰牌按钮: {ActionButtonObj}");
            ActionButtonObj.actionTypeList.Add(action_list[i]); // 添加按钮对应的行动
        }
        else if (action_list[i] == "gang"){
            Debug.Log($"杠牌");
            ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer);
            Text buttonText = ActionButtonObj.TextObject;
            buttonText.text = "杠";
            Debug.Log($"杠牌按钮: {ActionButtonObj}");
            ActionButtonObj.actionTypeList.Add(action_list[i]);
        }
        else if (action_list[i] == "hu"){
            Debug.Log($"胡牌");
            ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer);
            Text buttonText = ActionButtonObj.TextObject;
            buttonText.text = "胡";
            Debug.Log($"胡牌按钮: {ActionButtonObj}");
            ActionButtonObj.actionTypeList.Add(action_list[i]);
        }
        else if (action_list[i] == "buhua"){
            Debug.Log($"补花");
            ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer);
            Text buttonText = ActionButtonObj.TextObject;
            buttonText.text = "补花";
            Debug.Log($"补花按钮: {ActionButtonObj}");
            ActionButtonObj.actionTypeList.Add(action_list[i]);
        } 
        else if (action_list[i] == "angang"){
            Debug.Log($"暗杠");
            ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer);
            Text buttonText = ActionButtonObj.TextObject;
            buttonText.text = "暗杠";
            Debug.Log($"暗杠按钮: {ActionButtonObj}");
            ActionButtonObj.actionTypeList.Add(action_list[i]);
        }
        else if (action_list[i] == "jiagang"){
            Debug.Log($"加杠");
            ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer);
            Text buttonText = ActionButtonObj.TextObject;
            buttonText.text = "加杠";
            Debug.Log($"加杠按钮: {ActionButtonObj}");
            ActionButtonObj.actionTypeList.Add(action_list[i]);
        }
    }
    if (action_list.Length > 0 && action_list[0] != "cut"){ // 添加取消
        Debug.Log($"取消");
        ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer);
        Text buttonText = ActionButtonObj.TextObject;
        buttonText.text = "取消";
        Debug.Log($"取消按钮: {ActionButtonObj}");
        ActionButtonObj.actionTypeList.Add("pass");
    }
}

    // 选择行动以后打开次级菜单
    public void ShowAvailableAction(List<int> tiles,string actionType) {
        // 清空可选行动容器
        foreach (Transform child in ActionBlockContenter)
        {
            Destroy(child.gameObject);
        }
        GameObject containerBlockObj = Instantiate(ActionBlockPrefab, ActionBlockContenter);
        ActionBlock blockClick = containerBlockObj.GetComponent<ActionBlock>();
        blockClick.actionType = actionType;
        foreach (int tile in tiles){
            GameObject cardObj = Instantiate(StaticCardPrefab, containerBlockObj.transform);
            cardObj.GetComponent<StaticCard>().SetTileOnlyImage(tile);
        }
    }

    // 自动排序
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

    public void ChooseAction(List<string> actionTypeList){
        List<int> TipsCardsList = new List<int>();
        int lastCutTile = GameSceneManager.Instance.lastCutCardID;
        // 根据行动类型设置提示牌
        foreach (string actionType in actionTypeList){
            switch (actionType){
                // 吃碰杠显示吃碰杠列表
                case "chi_left": 
                    TipsCardsList.Add(lastCutTile-2);
                    TipsCardsList.Add(lastCutTile-1);
                    CreateActionCards(TipsCardsList, actionType);
                    break;
                case "chi_mid":
                    TipsCardsList.Clear();
                    TipsCardsList.Add(lastCutTile-1);
                    TipsCardsList.Add(lastCutTile+1);
                    CreateActionCards(TipsCardsList, actionType);
                    break;
                case "chi_right":
                    TipsCardsList.Clear();
                    TipsCardsList.Add(lastCutTile+1);
                    TipsCardsList.Add(lastCutTile+2);
                    CreateActionCards(TipsCardsList, actionType);
                    break;
                case "peng":
                    TipsCardsList.Clear();
                    TipsCardsList.Add(lastCutTile);
                    TipsCardsList.Add(lastCutTile);
                    CreateActionCards(TipsCardsList, actionType);
                    break;
                case "gang":
                    TipsCardsList.Clear();
                    TipsCardsList.Add(lastCutTile);
                    TipsCardsList.Add(lastCutTile);
                    TipsCardsList.Add(lastCutTile);
                    CreateActionCards(TipsCardsList, actionType);
                    break;
                case "pass": // 如果点击取消则停止计时 发送pass请求
                    StopTimeRunning();
                    NetworkManager.Instance.SendAction("pass");
                    break;
                case "hu":
                    StopTimeRunning();
                    NetworkManager.Instance.SendAction("hu");
                    break;
                case "buhua":
                    StopTimeRunning();
                    NetworkManager.Instance.SendAction("buhua");
                    break;
                case "jiagang":
                    StopTimeRunning();
                    NetworkManager.Instance.SendAction("jiagang");
                    break;
                case "angang":
                    StopTimeRunning();
                    NetworkManager.Instance.SendAction("angang");
                    break;
            }
        }
    }

    private void CreateActionCards(List<int> tiles,string actionType) {
        // 清空现有提示牌
        foreach (Transform child in ActionBlockContenter)
        {
            Destroy(child.gameObject);
        }
        GameObject containerBlockObj = Instantiate(ActionBlockPrefab, ActionBlockContenter);
        ActionBlock blockClick = containerBlockObj.GetComponent<ActionBlock>();
        blockClick.actionType = actionType;
        foreach (int tile in tiles){
            GameObject cardObj = Instantiate(StaticCardPrefab, containerBlockObj.transform);
            cardObj.GetComponent<StaticCard>().SetTileOnlyImage(tile);
        }
    }


    public void RemoveCutCard(int tileId,bool cut_class){
        if (cut_class){ // 摸切有两种可能 手动则卡牌自动消除 超时则需要验证消除
            if (GetCardsContainer.childCount > 0) {
                GameObject GetCardObj = GetCardsContainer.GetChild(0).gameObject;
                Destroy(GetCardObj);
            }
        }
        else{ 
            MoveAllGetCardsToHandCards();
            // 再删除tildId对应的卡牌
            foreach (Transform child in handCardsContainer){
                TileCard needToRemoveTileCard = child.GetComponent<TileCard>();
                if (needToRemoveTileCard.tileId == tileId){
                    Destroy(child.gameObject);
                    break;
                }
            }
            ArrangeHandCards();
        }
        // 添加null检查，防止_countdownCoroutine为null时抛出异常
        if (_countdownCoroutine != null) {
            StopCoroutine(_countdownCoroutine);
            _countdownCoroutine = null; // 设置为null以避免重复停止
        }
        remianTimeText.text = $""; // 隐藏倒计时文本
    }

public void MoveAllGetCardsToHandCards(){
        // 遍历GetCardsContainer中的所有子元素
        while (GetCardsContainer.childCount > 0) {
            // 获取第一个子元素
            Transform child = GetCardsContainer.GetChild(0);
            GameObject getCardObj = child.gameObject;
            
            // 获取卡牌信息
            TileCard getTileCard = getCardObj.GetComponent<TileCard>();
            int tileId = getTileCard.tileId;
            
            // 在HandCardsContainer中创建新的手牌
            GameObject handCardObj = Instantiate(tileCardPrefab, handCardsContainer);
            TileCard handTileCard = handCardObj.GetComponent<TileCard>();
            handTileCard.SetTile(tileId, false);
            
            // 销毁GetCardsContainer中的原始卡牌
            Destroy(getCardObj);
        }
        
        // 重新排列手牌
        ArrangeHandCards();
    }


    public void ClearActionContenter(){
        foreach (Transform child in ActionBlockContenter){
            Destroy(child.gameObject);
        }
        foreach (Transform child in ActionButtonContainer){
            Destroy(child.gameObject);
        }
    }

    // 显示倒计时
    public void LoadingRemianTime(int remainingTime, int cuttime){
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
