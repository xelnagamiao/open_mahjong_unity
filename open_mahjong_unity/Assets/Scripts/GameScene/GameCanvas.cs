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
    [SerializeField] private Text remianTimeText;        // 剩余时间文本(显示剩余时间[20+5])
    [SerializeField] public Transform ActionButtonContainer;  // 询问操作容器(显示吃,碰,杠,胡,补花,抢杠等按钮)
    [SerializeField] public Transform ActionBlockContenter;  // 询问操作内容提示(显示吃,碰,杠,胡,补花,抢杠等按钮的多种结果)

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
    private bool isArrangeHandCards = true; // 是否自动排列手牌
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

    // 手牌处理
    public void ChangeHandCards(string ChangeType,int tileId,int[] TilesList){
        // 将手牌处理任务加入队列
        changeHandCardQueue.Enqueue(() => {
            return StartCoroutine(ChangeHandCardsCoroutine(ChangeType,tileId,TilesList));
        });
        // 未启动执行队列则启动
        if (!isChangeHandCardProcessing){
            StartCoroutine(ProcessChangeHandCardQueue());
        }
    }
    
    // 处理手牌队列
    private System.Collections.IEnumerator ProcessChangeHandCardQueue(){
        // 手牌处理运行
        isChangeHandCardProcessing = true;
        while (changeHandCardQueue.Count > 0){
            // 拿取下一个任务
            System.Func<Coroutine> changeHandCardAction = changeHandCardQueue.Dequeue();
            // 执行任务
            Coroutine changeHandCardCoroutine = changeHandCardAction.Invoke();
            // 等待任务完成
            yield return changeHandCardCoroutine;
        }
        // 手牌处理结束
        isChangeHandCardProcessing = false;
    }
    
    // 手牌处理 
    public IEnumerator ChangeHandCardsCoroutine(string ChangeType,int tileId,int[] TilesList){

        Debug.Log($"手牌处理: {ChangeType}");

        // 初始化手牌 只初始化前13张
        if (ChangeType == "InitHandCards"){
            int cardCount = 0;
            foreach (int tile in TilesList){
                if (cardCount >= 13) break;
                // 创建手牌
                GameObject cardObj = Instantiate(tileCardPrefab, handCardsContainer);
                TileCard tileCard = cardObj.GetComponent<TileCard>();
                tileCard.SetTile(tile, false);
                // 设置位置：每张牌间隔一个宽度
                RectTransform cardRect = cardObj.GetComponent<RectTransform>();
                cardRect.anchoredPosition = new Vector2(cardCount * tileCardWidth, 0);
                // 计数增加
                cardCount++;
            }
        }

        // 摸牌 添加摸牌区手牌
        else if (ChangeType == "GetCard"){
            GameObject cardObj = Instantiate(tileCardPrefab, handCardsContainer);
            TileCard tileCard = cardObj.GetComponent<TileCard>();
            tileCard.SetTile(tileId, true);
            // 设置位置：手牌区右侧，间隔半个宽度
            RectTransform cardRect = cardObj.GetComponent<RectTransform>();
            int handCardCount = handCardsContainer.childCount - 1; // 减去刚添加的这张
            cardRect.anchoredPosition = new Vector2(handCardCount * tileCardWidth + tileCardWidth * 0.5f, 0);
        }

        // 摸切 删除摸牌区手牌
        else if (ChangeType == "RemoveGetCard"){
            // 删除最后添加的牌（摸牌区的牌）
            Transform lastCard = handCardsContainer.GetChild(handCardsContainer.childCount - 1);
            TileCard tileCard = lastCard.GetComponent<TileCard>();
            if (tileCard.tileId == tileId){
                Destroyer.Instance.AddToDestroyer(lastCard);
            }
        }

        // 手切 删除手牌区手牌
        else if (ChangeType == "RemoveHandCard"){
            foreach (Transform child in handCardsContainer){
                TileCard needToRemoveTileCard = child.GetComponent<TileCard>();
                if (needToRemoveTileCard.tileId == tileId){
                    Destroyer.Instance.AddToDestroyer(child);
                }
            }
        }

        // 补花 删除手牌区手牌
        else if (ChangeType == "RemoveBuhuaCard"){
            foreach (Transform child in handCardsContainer){
                TileCard needToRemoveTileCard = child.GetComponent<TileCard>();
                if (needToRemoveTileCard.tileId == tileId){
                    Destroyer.Instance.AddToDestroyer(child);
                }
            }
        }

        // 删除组合牌 在手牌中删除全部组合牌
        else if (ChangeType == "RemoveCombinationCard"){
            foreach (int tileToRemove in TilesList){
                foreach (Transform child in handCardsContainer){
                    TileCard needToRemoveTileCard = child.GetComponent<TileCard>();
                    if (needToRemoveTileCard.tileId == tileToRemove){
                        Destroyer.Instance.AddToDestroyer(child);
                    }
                }
            }
        }

        else if (ChangeType == "ReSetHandCards"){
            // 标志回合结束 只在自己其他人回合开始时调用 用以在未排序的情况下排序手牌
            if (isArranged){yield break;} // 如果手牌已经排序过 则不进行排序
        }

        // isArrangeed 用于监测玩家这次行为是否已经排序 例如补花和摸牌以后还可以执行操作,但是摸切和手切以后就不能执行操作了
        // 初始化手牌、摸牌、补花以后 手牌没有排序过, RemoveBuhuaCard以后固定会执行GetCard
        if (ChangeType == "GetCard" ){
            isArranged = false;
        }

        // 初始化卡牌、摸切、手切、单次补花、吃碰杠以后 进行卡牌排序 
        else if (ChangeType == "RemoveGetCard" || ChangeType == "RemoveHandCard" || ChangeType == "RemoveCombinationCard" || ChangeType == "RemoveBuhuaCard" || ChangeType == "InitHandCards"){
            isArranged = true;
            // 等待排序完成
            yield return StartCoroutine(RearrangeHandCardsWithAnimation());
        }
    }
    
    // 重新排列手牌位置
    private void RearrangeHandCards(){

        // 手牌恢复为非摸切状态
        Debug.Log($"排序时容器子对象数量: {handCardsContainer.childCount}");
        for (int i = 0; i < handCardsContainer.childCount; i++){
            Transform child = handCardsContainer.GetChild(i);
            Debug.Log($"处理子对象 {i}: {child.name}, 父对象: {child.parent.name}");
            TileCard tileCard = child.GetComponent<TileCard>();
            if (tileCard != null){
                tileCard.currentGetTile = false;
            }
        }

        // 如果需要排序
        if (isArrangeHandCards){
            // 按 tileId 排序
            List<TileCard> cards = new List<TileCard>();
            foreach (Transform child in handCardsContainer) {
                TileCard tileCard = child.GetComponent<TileCard>();
                if (tileCard != null) {
                    cards.Add(tileCard);
                }
            }
            
            // 按 tileId 排序
            cards.Sort((a, b) => a.tileId.CompareTo(b.tileId));
            
            // 重新设置位置和顺序
            for (int i = 0; i < cards.Count; i++) {
                // 设置层级顺序
                cards[i].transform.SetSiblingIndex(i);
                // 设置位置
                RectTransform cardRect = cards[i].GetComponent<RectTransform>();
                cardRect.anchoredPosition = new Vector2(i * tileCardWidth, 0);
            }
        }
        // 如果不排序
        else{
            for (int i = 0; i < handCardsContainer.childCount; i++){
                Transform child = handCardsContainer.GetChild(i);
                RectTransform cardRect = child.GetComponent<RectTransform>();
                cardRect.anchoredPosition = new Vector2(i * tileCardWidth, 0);
            }
        }
    }
    
    // 带动画的手牌重新排列
    private System.Collections.IEnumerator RearrangeHandCardsWithAnimation(){

        Debug.Log($"手牌重新排列");

        // 手牌恢复为非摸切状态
        for (int i = 0; i < handCardsContainer.childCount; i++){
            Transform child = handCardsContainer.GetChild(i);
            TileCard tileCard = child.GetComponent<TileCard>();
            if (tileCard != null){
                tileCard.currentGetTile = false;
            }
        }

        // 收集所有卡牌和它们的目标位置
        List<RectTransform> cards = new List<RectTransform>();
        List<Vector2> targetPositions = new List<Vector2>();
        
        if (isArrangeHandCards){
            // 按 tileId 排序
            List<TileCard> tileCards = new List<TileCard>();
            for (int i = 0; i < handCardsContainer.childCount; i++){
                Transform child = handCardsContainer.GetChild(i);
                TileCard tileCard = child.GetComponent<TileCard>();
                if (tileCard != null) {
                    tileCards.Add(tileCard);
                }
            }
            
            // 按 tileId 排序
            tileCards.Sort((a, b) => a.tileId.CompareTo(b.tileId));
            
            // 设置层级顺序并收集目标位置
            for (int i = 0; i < tileCards.Count; i++) {
                // 设置层级顺序
                tileCards[i].transform.SetSiblingIndex(i);
                // 收集卡牌和目标位置
                cards.Add(tileCards[i].GetComponent<RectTransform>());
                targetPositions.Add(new Vector2(i * tileCardWidth, 0));
            }
        }
        else{
            // 不排序，直接收集当前位置和目标位置
            for (int i = 0; i < handCardsContainer.childCount; i++){
                Transform child = handCardsContainer.GetChild(i);
                RectTransform cardRect = child.GetComponent<RectTransform>();
                cards.Add(cardRect);
                targetPositions.Add(new Vector2(i * tileCardWidth, 0));
            }
        }
        
        // 执行动画
        yield return StartCoroutine(AnimateCardsToPositions(cards, targetPositions));
    }
    
    // 卡牌移动动画协程
    private System.Collections.IEnumerator AnimateCardsToPositions(List<RectTransform> cards, List<Vector2> targetPositions){
        float animationDuration = 0.3f; // 动画持续时间
        float elapsedTime = 0f;
        
        // 记录起始位置
        List<Vector2> startPositions = new List<Vector2>();
        for (int i = 0; i < cards.Count; i++){
            startPositions.Add(cards[i].anchoredPosition);
        }
        
        // 动画循环
        while (elapsedTime < animationDuration){
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / animationDuration;
            
            // 使用平滑插值函数（easeOutCubic）
            float smoothProgress = 1f - Mathf.Pow(1f - progress, 3f);
            
            // 更新每张卡牌的位置
            for (int i = 0; i < cards.Count; i++){
                if (cards[i] != null){
                    Vector2 currentPos = Vector2.Lerp(startPositions[i], targetPositions[i], smoothProgress);
                    cards[i].anchoredPosition = currentPos;
                }
            }
            
            yield return null; // 等待下一帧
        }
        
        // 确保最终位置准确
        for (int i = 0; i < cards.Count; i++){
            if (cards[i] != null){
                cards[i].anchoredPosition = targetPositions[i];
            }
        }
    }

    // 显示可用行动按钮
    public void SetActionButton(List<string> action_list){
        // 用于跟踪吃牌按钮
        ActionButton chiButton = null;
        // 用于跟踪暗杠按钮
        ActionButton angangButton = null;
        // 用于跟踪加杠按钮
        ActionButton jiagangButton = null;
        
        for (int i = 0; i < action_list.Count; i++){

            Debug.Log($"询问操作: {action_list[i]}");

            // 碰牌
            if (action_list[i] == "peng"){
                Debug.Log($"碰牌");
                ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer); // 实例化按钮
                Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = "碰"; // 设置按钮文本
                Debug.Log($"碰牌按钮: {ActionButtonObj}");
                ActionButtonObj.actionTypeList.Add(action_list[i]); // 添加按钮对应的行动
            }
            // 杠牌
            else if (action_list[i] == "gang"){
                Debug.Log($"杠牌");
                ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer);
                Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = "杠";
                Debug.Log($"杠牌按钮: {ActionButtonObj}");
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            }
            // 胡牌
            else if (action_list[i] == "hu_self" || action_list[i] == "hu_first" || action_list[i] == "hu_second" || action_list[i] == "hu_third"){
                Debug.Log($"胡牌");
                ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer);
                Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = "胡";
                Debug.Log($"胡牌按钮: {ActionButtonObj}");
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            }
            // 补花
            else if (action_list[i] == "buhua"){
                Debug.Log($"补花");
                ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer);
                Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = "补花";
                Debug.Log($"补花按钮: {ActionButtonObj}");
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            } 
            // 暗杠加杠吃牌可能有多个选择的选项，将这些选项添加入单个按钮。
            // 暗杠
            else if (action_list[i] == "angang"){
                if (angangButton == null)
                {
                    angangButton = Instantiate(ActionButtonPrefab, ActionButtonContainer);
                    Text buttonText = angangButton.TextObject;
                    buttonText.text = "暗杠";
                    Debug.Log($"暗杠按钮: {angangButton}");
                    angangButton.actionTypeList.Add(action_list[i]);
                }
                angangButton.actionTypeList.Add(action_list[i]);
                Debug.Log($"添加暗杠选项: {action_list[i]}");
            }
            // 加杠
            else if (action_list[i] == "jiagang"){
                if (jiagangButton == null)
                {
                    jiagangButton = Instantiate(ActionButtonPrefab, ActionButtonContainer);
                    Text buttonText = jiagangButton.TextObject;
                    buttonText.text = "加杠";
                    Debug.Log($"加杠按钮: {jiagangButton}");
                    jiagangButton.actionTypeList.Add(action_list[i]);
                }
                jiagangButton.actionTypeList.Add(action_list[i]);
                Debug.Log($"添加加杠选项: {action_list[i]}");
            }
            // 吃牌
            else if (action_list[i] == "chi_left" || action_list[i] == "chi_right" || action_list[i] == "chi_mid"){
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
            // 取消
            else if (action_list[i] == "pass"){
                Debug.Log($"取消");
                ActionButton ActionButtonObj = Instantiate(ActionButtonPrefab, ActionButtonContainer);
                Text buttonText = ActionButtonObj.TextObject;
                buttonText.text = "取消";
                Debug.Log($"取消按钮: {ActionButtonObj}");
                ActionButtonObj.actionTypeList.Add(action_list[i]);
            }
        }
    }


    // 选择行动
    public void ChooseAction(string actionType,int targetTile){
        // 清空允许操作列表
        GameSceneManager.Instance.allowActionList.Clear();
        // 停止倒计时
        StopTimeRunning();
        // 发送行动
        NetworkManager.Instance.SendAction(actionType,targetTile);
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
        WaitForSeconds flashWait = new WaitForSeconds(0.05f);
        
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
                
                // 执行闪烁效果（使用安全的循环方式）
                yield return StartCoroutine(FlashWarningEffect());
            }
            else
            {
                remianTimeText.color = Color.white; // 正常时间显示白色
            }
            
            if (_currentRemainingTime == 0){
                remianTimeText.text = $""; // 如果剩余时间为0，则不显示剩余时间
                break; // 直接退出循环
            }
        }
        Debug.Log("倒计时结束！");
    }

    // 分离闪烁效果到独立的协程，避免死循环
    private IEnumerator FlashWarningEffect()
    {
        CanvasGroup canvasGroup = remianTimeText.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = remianTimeText.gameObject.AddComponent<CanvasGroup>();
        
        // 闪烁3次，使用整数循环避免浮点数精度问题
        for (int flashCount = 0; flashCount < 3; flashCount++)
        {
            // 渐隐：使用整数步数，避免浮点数精度问题
            for (int step = 0; step <= 7; step++) // 7步从1.0到0.3
            {
                float alpha = 1.0f - (step * 0.1f);
                canvasGroup.alpha = alpha;
                yield return new WaitForSeconds(0.05f);
            }
            
            // 渐显：使用整数步数，避免浮点数精度问题
            for (int step = 0; step <= 7; step++) // 7步从0.3到1.0
            {
                float alpha = 0.3f + (step * 0.1f);
                canvasGroup.alpha = alpha;
                yield return new WaitForSeconds(0.05f);
            }
        }
        
        // 确保最终透明度为1
        canvasGroup.alpha = 1f;
    }


    public void StopTimeRunning(){

        // 停止倒计时
        if (_countdownCoroutine != null) {
            StopCoroutine(_countdownCoroutine);
            _countdownCoroutine = null; // 设置为null以避免重复停止
        }
        remianTimeText.text = $""; // 隐藏倒计时文本
        Debug.Log("停止倒计时,删除所有操作按钮");

        // 删除所有操作按钮
        foreach (Transform child in ActionBlockContenter){
            Destroy(child.gameObject);
        }
        foreach (Transform child in ActionButtonContainer){
            Destroy(child.gameObject);
        }
        ActionBlockContainerState = "None";

    }
}
