using System.Threading;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.Rendering;
using System;


public class GameSceneMannager : MonoBehaviour
{
    public static GameSceneMannager Instance { get; private set; }
    
    [Header("主用户界面")]
    [SerializeField] private GameObject tileCardPrefab;    // 手牌预制体
    [SerializeField] private GameObject tile3DPrefab;    // 他人手牌预制体
    [SerializeField] private Transform handCardsContainer; // 手牌容器（水平布局组）
    [SerializeField] private Transform GetCardsContainer;  // 获得牌容器
    [SerializeField] private Text remianTimeText;        // 剩余时间文本

    [Header("3D对象生成位置")]
    [SerializeField] private Transform leftCardsPosition; // 左家手牌位置
    [SerializeField] private Transform topCardsPosition; // 对家手牌位置
    [SerializeField] private Transform rightCardsPosition; // 右家手牌位置
    [SerializeField] private Transform selfDiscardsPosition; // 自家弃牌位置
    [SerializeField] private Transform leftDiscardsPosition; // 上家弃牌位置
    [SerializeField] private Transform topDiscardsPosition; // 对家弃牌位置
    [SerializeField] private Transform rightDiscardsPosition; // 下家弃牌位置
    [SerializeField] private Transform selfCombinationsPosition; // 自家组合位置
    [SerializeField] private Transform leftCombinationsPosition; // 上家组合位置
    [SerializeField] private Transform topCombinationsPosition; // 对家组合位置
    [SerializeField] private Transform rightCombinationsPosition; // 下家组合位置


    [Header("房间ui界面")]
    [SerializeField] private Text roomRoundText;         // 房间轮数文本
    [SerializeField] private Text roomNowRoundText;      // 当前轮数文本
    [SerializeField] private Text remiansTilesText;      // 剩余牌数文本

    [Header("玩家信息")]

    [SerializeField] private Text player_self_name;        // 玩家名称文本
    [SerializeField] private Text player_self_score;       // 玩家分数文本
    [SerializeField] private Text player_self_index;       // 玩家索引文本
    [SerializeField] private Image player_self_current_image;    // 玩家回合标记

    [SerializeField] private Text player_left_name;          // 玩家名称文本
    [SerializeField] private Text player_left_score;         // 玩家分数文本
    [SerializeField] private Text player_left_index;         // 玩家索引文本
    [SerializeField] private Image player_left_current_image;      // 玩家回合标记

    [SerializeField] private Text player_top_name;           // 玩家名称文本
    [SerializeField] private Text player_top_score;          // 玩家分数文本
    [SerializeField] private Text player_top_index;          // 玩家索引文本
    [SerializeField] private Image player_top_current_image;       // 玩家回合标记

    [SerializeField] private Text player_right_name;         // 玩家名称文本
    [SerializeField] private Text player_right_score;        // 玩家分数文本
    [SerializeField] private Text player_right_index;        // 玩家索引文本
    [SerializeField] private Image player_right_current_image;     // 玩家回合标记



    private int SaveCount;
    private Coroutine _countdownCoroutine;
    private int _currentRemainingTime;
    private int _currentCutTime;

    public int selfCurrentIndex;
    public int NowCurrentIndex;
    public int currentCutTime;

    public List<string> selfDiscardslist = new List<string>();
    public List<string> leftDiscardslist = new List<string>();
    public List<string> topDiscardslist = new List<string>();
    public List<string> rightDiscardslist = new List<string>();

    // 存储玩家位置至字典
    public Dictionary<int, string> player_local_position = new Dictionary<int, string>();


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

    private void Start()
    {
        // Networkmannager 触发游戏开始事件则调用OnGameStart
        NetworkManager.Instance.GameStartResponse.AddListener(OnGameStart);
    }   // ↓                                             ↓

    private void OnGameStart(bool success, string message, GameInfo gameInfo)
    {
        if (success)
        {
            // 添加详细的调试信息
            Debug.Log("=== GameInfo 完整数据 ===");
            Debug.Log(JsonUtility.ToJson(gameInfo, true));  // true 表示格式化输出

            // 检查关键字段
            Debug.Log($"player_positions 是否为 null: {gameInfo.player_positions == null}");
            if (gameInfo.player_positions != null)
            {
                Debug.Log($"player_positions 长度: {gameInfo.player_positions.Length}");
                foreach (var pos in gameInfo.player_positions)
                {
                    Debug.Log($"Position: {pos?.username} -> {pos?.position}");
                }
            }
            InitializeGame(gameInfo);
        }
        else
        {
            Debug.LogError($"游戏启动失败: {message}");
        }
    }

    public void CutCards(string tileId,int playerIndex,bool cut_class){
        Debug.Log($"{playerIndex},{selfCurrentIndex}");
        // 如果出牌玩家和当前玩家相同，并且满足NowCurrentIndex为-1（手动出牌）或出牌玩家和NowCurrentIndex相同（超时出牌）
        if (playerIndex == selfCurrentIndex && (NowCurrentIndex == -1 || playerIndex == NowCurrentIndex)){
            NowCurrentIndex = -2; // 等待服务器指示NowCurrentIndex
            if (cut_class){ // 摸切有两种可能 手动则卡牌自动消除 超时则需要验证消除
                if (GetCardsContainer.childCount > 0) {
                    GameObject GetCardObj = GetCardsContainer.GetChild(0).gameObject;
                    Destroy(GetCardObj);
                }
            }
            else{ // 手切则手动找到摸到的牌加入手牌
                GameObject GetCardObj = GetCardsContainer.GetChild(0).gameObject;
                Destroy(GetCardObj);
                GameObject cardObj = Instantiate(tileCardPrefab, handCardsContainer);
                TileCard tileCard = cardObj.GetComponent<TileCard>();
                tileCard.SetTile(tileId, false);
            }
            // 添加null检查，防止_countdownCoroutine为null时抛出异常
            if (_countdownCoroutine != null) {
                StopCoroutine(_countdownCoroutine);
                _countdownCoroutine = null; // 设置为null以避免重复停止
            }
            remianTimeText.text = $""; // 隐藏倒计时文本
            DisCardAnimation(tileId,selfDiscardsPosition,new Vector3(1,0,0),new Vector3(0,0,-1),"self",selfDiscardslist.Count);
            selfDiscardslist.Add(tileId);
        }
        if (selfCurrentIndex == 0){
            if (playerIndex == 1){
                DisCardAnimation(tileId,rightDiscardsPosition,new Vector3(0,0,1),new Vector3(1,0,0),"right",rightDiscardslist.Count);
                removeOtherHandCards(rightCardsPosition,cut_class);
                rightDiscardslist.Add(tileId);
            }
            else if (playerIndex == 2){
                DisCardAnimation(tileId,topDiscardsPosition,new Vector3(-1,0,0),new Vector3(0,0,1),"top",topDiscardslist.Count);
                removeOtherHandCards(topCardsPosition,cut_class);
                topDiscardslist.Add(tileId);
            }
            else if (playerIndex == 3){
                DisCardAnimation(tileId,leftDiscardsPosition,new Vector3(0,0,-1),new Vector3(-1,0,0),"left",leftDiscardslist.Count);
                removeOtherHandCards(leftCardsPosition,cut_class);
                leftDiscardslist.Add(tileId);
            }
        }
        else if (selfCurrentIndex == 1){
            if (playerIndex == 0){
                DisCardAnimation(tileId,leftDiscardsPosition,new Vector3(0,0,-1),new Vector3(-1,0,0),"left",leftDiscardslist.Count);
                removeOtherHandCards(leftCardsPosition,cut_class);
                leftDiscardslist.Add(tileId);
            }
            else if (playerIndex == 2){
                DisCardAnimation(tileId,rightDiscardsPosition,new Vector3(0,0,1),new Vector3(1,0,0),"right",rightDiscardslist.Count);
                removeOtherHandCards(rightCardsPosition,cut_class);
                rightDiscardslist.Add(tileId);
            }
            else if (playerIndex == 3){
                DisCardAnimation(tileId,topDiscardsPosition,new Vector3(-1,0,0),new Vector3(0,0,1),"top",topDiscardslist.Count);
                removeOtherHandCards(topCardsPosition,cut_class);
                topDiscardslist.Add(tileId);
            }
        }
        else if (selfCurrentIndex == 2){
            if (playerIndex == 0){
                DisCardAnimation(tileId,topDiscardsPosition,new Vector3(-1,0,0),new Vector3(0,0,1),"top",topDiscardslist.Count);
                removeOtherHandCards(topCardsPosition,cut_class);
                topDiscardslist.Add(tileId);
            }
            else if (playerIndex == 1){
                DisCardAnimation(tileId,leftDiscardsPosition,new Vector3(0,0,-1),new Vector3(-1,0,0),"left",leftDiscardslist.Count);
                removeOtherHandCards(leftCardsPosition,cut_class);
                leftDiscardslist.Add(tileId);
            }
            else if (playerIndex == 3){
                DisCardAnimation(tileId,rightDiscardsPosition,new Vector3(0,0,1),new Vector3(1,0,0),"right",rightDiscardslist.Count);
                removeOtherHandCards(rightCardsPosition,cut_class);
                rightDiscardslist.Add(tileId);
            }
        }
        else if (selfCurrentIndex == 3){
            if (playerIndex == 0){
                DisCardAnimation(tileId,rightDiscardsPosition,new Vector3(0,0,1),new Vector3(1,0,0),"right",rightDiscardslist.Count);
                removeOtherHandCards(rightCardsPosition,cut_class);
                rightDiscardslist.Add(tileId);
            }
            else if (playerIndex == 1){
                DisCardAnimation(tileId,topDiscardsPosition,new Vector3(-1,0,0),new Vector3(0,0,1),"top",topDiscardslist.Count);
                removeOtherHandCards(topCardsPosition,cut_class);
                topDiscardslist.Add(tileId);
            }
            else if (playerIndex == 2){
                DisCardAnimation(tileId,leftDiscardsPosition,new Vector3(0,0,-1),new Vector3(-1,0,0),"left",leftDiscardslist.Count);
                removeOtherHandCards(leftCardsPosition,cut_class);
                leftDiscardslist.Add(tileId);
            }
        }
    }

    private void DisCardAnimation(string tileId,Transform DiscardPosition,Vector3 widthdirection,Vector3 heightdirection,string PlayerPosition,int discardCount){
        float cardWidth = tile3DPrefab.GetComponent<Renderer>().bounds.size.y;
        float cardHeight = tile3DPrefab.GetComponent<Renderer>().bounds.size.z;
        float widthSpacing = cardWidth * 1f; // 间距为卡片宽度的1倍
        float heightSpacing = cardHeight * 1f; // 间距为卡片高度的1倍

        Vector3 currentPosition = DiscardPosition.position;
        discardCount = discardCount + 1; // 由于弃牌是从0开始计数的，所以需要加1
        if (discardCount <= 6){
            currentPosition += widthdirection.normalized * widthSpacing * discardCount;
            currentPosition += heightdirection.normalized * heightSpacing * 0;
        }
        else if (discardCount <= 12){
            currentPosition += widthdirection.normalized * widthSpacing * (discardCount-6);
            currentPosition += heightdirection.normalized * heightSpacing * 1;
        }
        else if (discardCount <= 18){
            currentPosition += widthdirection.normalized * widthSpacing * (discardCount-12);
            currentPosition += heightdirection.normalized * heightSpacing * 2;
        }
        else if (discardCount <= 24){
            currentPosition += widthdirection.normalized * widthSpacing * (discardCount-18);
            currentPosition += heightdirection.normalized * heightSpacing * 3;
        }
        else if (discardCount <= 30){
            currentPosition += widthdirection.normalized * widthSpacing * (discardCount-24);
            currentPosition += heightdirection.normalized * heightSpacing * 4;
        }

        Quaternion rotation = Quaternion.identity;
        if (PlayerPosition == "self"){
            rotation = Quaternion.Euler(0, 0, -90); // 自身位置
        }
        else if (PlayerPosition == "left"){
            rotation = Quaternion.Euler(0, 90, -90); // 左侧玩家
        }
        else if (PlayerPosition == "top"){
            rotation = Quaternion.Euler(0, 180, -90); // 上方玩家
        }
        else if (PlayerPosition == "right"){
            rotation = Quaternion.Euler(0, 270, -90); // 右侧玩家
        }

        Debug.Log($"创建卡片 {discardCount}, 牌ID: {tileId}");
        // 创建麻将牌预制体
        GameObject cardObj = Instantiate(tile3DPrefab, currentPosition, rotation);
        
        // 设置父对象
        cardObj.transform.SetParent(DiscardPosition, worldPositionStays: true);
        cardObj.name = $"Card_{discardCount}";
        
        // 加载并应用材质
        ApplyCardTexture(cardObj, tileId);
    }
    
    private void removeOtherHandCards(Transform cardPosition,bool cut_class){
        Debug.Log($"移除其他玩家手牌 {cardPosition},{cut_class}");

        float cardWidth = tile3DPrefab.GetComponent<Renderer>().bounds.size.y;
        float spacing = cardWidth * 1f; // 间距为卡片宽度的1.0倍
        // 从其他玩家的手牌中移除该牌
        if (cut_class){
            if (cardPosition == rightCardsPosition){
                Transform cardTransform = rightCardsPosition.Find("Card_Current");
                if (cardTransform != null) {
                    Destroy(cardTransform.gameObject);
                }
            }
            else if (cardPosition == leftCardsPosition){
                Transform cardTransform = leftCardsPosition.Find("Card_Current");
                if (cardTransform != null) {
                    Destroy(cardTransform.gameObject);
                }
            }
            else if (cardPosition == topCardsPosition){
                Transform cardTransform = topCardsPosition.Find("Card_Current");
                if (cardTransform != null) {
                    Destroy(cardTransform.gameObject);
                }
            }
        }
        else{
            // 计算子物体数量
            int childCount = cardPosition.childCount;
            if (childCount > 0) {
                // 获取随机索引
                int randomIndex = UnityEngine.Random.Range(0, childCount);
                
                // 获取随机选中的子物体
                Transform randomChild = cardPosition.GetChild(randomIndex);
                
                // 删除随机选中的子物体
                if (randomChild != null) {
                    Debug.Log($"随机删除了索引为 {randomIndex} 的牌");
                    Destroy(randomChild.gameObject);
                    
                    // 确定方向向量
                    Vector3 direction = Vector3.zero;
                    if (cardPosition == rightCardsPosition)
                        direction = new Vector3(0, 0, 1); // 向前
                    else if (cardPosition == leftCardsPosition)
                        direction = new Vector3(0, 0, -1); // 向后
                    else if (cardPosition == topCardsPosition)
                        direction = new Vector3(-1, 0, 0); // 向左
                    
                    // 获取所有剩余子物体并按照名称索引排序
                    List<Transform> remainingCards = new List<Transform>();
                    for (int i = 0; i < cardPosition.childCount; i++) {
                        remainingCards.Add(cardPosition.GetChild(i));
                    }
                    
                    // 重新定位所有卡牌
                    Vector3 startPosition = cardPosition.position;
                    for (int i = 0; i < remainingCards.Count; i++) {
                        // 计算新位置
                        Vector3 newPosition = startPosition + direction * spacing * (i + 1);
                        // 设置新位置
                        remainingCards[i].position = newPosition;
                        // 重命名卡牌
                        remainingCards[i].name = $"Card_{i}";
                    }
                }
            }
        }
    }

    public void GetCards(int remaining_time,string tileId,int playerIndex){

        float cardWidth = tile3DPrefab.GetComponent<Renderer>().bounds.size.y;
        float spacing = cardWidth * 1f; // 间距为卡片宽度的1倍

        Debug.Log($"获取牌 {tileId},{playerIndex}");
        NowCurrentIndex = playerIndex;

        string GetCardPlayer = player_local_position[playerIndex];
        // 如果玩家是自己，则将牌添加到手牌中
        if (GetCardPlayer == "self"){
            GameObject cardObj = Instantiate(tileCardPrefab, GetCardsContainer);
            TileCard tileCard = cardObj.GetComponent<TileCard>();
            tileCard.SetTile(tileId, true);
            // 开启倒计时
            loadingRemianTime(remaining_time, currentCutTime);
            player_self_current_image.enabled = true;
            player_left_current_image.enabled = false;
            player_right_current_image.enabled = false;
            player_top_current_image.enabled = false;
        }
        // 如果玩家是其他玩家，则将牌添加到他人手牌中
        else if (GetCardPlayer == "left"){
            Quaternion rotation = Quaternion.Euler(90, 0, 0);
            Vector3 SetPosition = leftCardsPosition.position + (leftCardsPosition.childCount+2) * spacing * new Vector3(0,0,-1);
            GameObject cardObj = Instantiate(tile3DPrefab, SetPosition, rotation);
            cardObj.transform.SetParent(leftCardsPosition, worldPositionStays: true);
            cardObj.name = $"Card_Current";
            player_self_current_image.enabled = true;
            player_left_current_image.enabled = false;
            player_right_current_image.enabled = false;
            player_top_current_image.enabled = false;
        }
        else if (GetCardPlayer == "top"){
            Quaternion rotation = Quaternion.Euler(90, 0, -90);
            Vector3 SetPosition = topCardsPosition.position + (topCardsPosition.childCount+2) * spacing * new Vector3(-1,0,0);
            GameObject cardObj = Instantiate(tile3DPrefab, SetPosition, rotation);
            cardObj.transform.SetParent(topCardsPosition, worldPositionStays: true);
            cardObj.name = $"Card_Current";
            player_self_current_image.enabled = true;
            player_left_current_image.enabled = false;
            player_right_current_image.enabled = false;
            player_top_current_image.enabled = false;
        }
        else if (GetCardPlayer == "right"){
            Quaternion rotation = Quaternion.Euler(90, 0, 180);
            Vector3 SetPosition = rightCardsPosition.position + (rightCardsPosition.childCount+2) * spacing * new Vector3(0,0,1);
            GameObject cardObj = Instantiate(tile3DPrefab, SetPosition, rotation);
            cardObj.transform.SetParent(rightCardsPosition, worldPositionStays: true);
            cardObj.name = $"Card_Current";
            player_self_current_image.enabled = true;
            player_left_current_image.enabled = false;
            player_right_current_image.enabled = false;
            player_top_current_image.enabled = false;
        }


    }


    // 新增方法：应用牌面纹理
    private void ApplyCardTexture(GameObject cardObj, string tileId)
    {

        // 从Resources加载对应ID的图片
        Texture2D texture = Resources.Load<Texture2D>($"image/CardMaterial/{tileId}");
        // 获取预制体的渲染器
        Renderer renderer = cardObj.GetComponent<Renderer>();
        // 克隆材质以避免修改原始材质
        Material[] materials = renderer.materials;        
        // 修改第二个材质的纹理（索引1）
        materials[1].mainTexture = texture;         
        // 应用修改后的材质
        renderer.materials = materials;
    }

    private void InitializeGame(GameInfo gameInfo)
    {
        // 1.存储房间信息
        InitializeSetInfo(gameInfo);
        // 2.初始化房间信息
        InitializeRoomInfo(gameInfo.tile_count, gameInfo.current_round, gameInfo.game_time);
        // 3.初始化玩家信息
        InitializePlayerInfo(gameInfo.player_positions, gameInfo.players_info);
        // 4.初始化手牌区域 由于手牌信息必定是单独发送的，所以这里直接初始化
        InitializeHandCards(gameInfo.self_hand_tiles,gameInfo.current_player_index,gameInfo.game_status);
        // 5.初始化他人手牌区域
        InitializeOtherCards(gameInfo.player_positions, gameInfo.players_info);
        // 6.初始化剩余时间,如果自己的index为0
        foreach (var player in gameInfo.players_info){
            if (player.username == Administrator.Instance.Username){
                if (player.current_player_index == 0){
                    loadingRemianTime(player.remaining_time, gameInfo.cuttime);
                }
            }
        }
        // 6.切换摄像头至GameSceneCamera
        WindowsMannager.Instance.GetWindowsSwitchResponse.Invoke("game");
        // $.初始化游戏结束
    }

    private void InitializeSetInfo(GameInfo gameInfo){
        foreach (var player in gameInfo.player_positions){
            if (player.username == Administrator.Instance.Username){
                selfCurrentIndex = player.position;
                break;
            }
        }
        NowCurrentIndex = gameInfo.current_player_index;
        currentCutTime = gameInfo.cuttime;
    }

    // 设置房间面板信息
    private void InitializeRoomInfo(int tile_count, int currentRound, int game_time)
    {
        // 剩余牌数
        remiansTilesText.text = $"剩余牌数: {tile_count}";
        // 房间轮数
        if (game_time == 1){
            roomRoundText.text = $"国标房间轮数: 1圈";
        }
        else if (game_time == 2){
            roomRoundText.text = $"国标房间轮数: 2圈";
        }
        else if (game_time == 3){
            roomRoundText.text = $"国标房间轮数: 3圈";
        }
        else if (game_time == 4){
            roomRoundText.text = $"国标房间轮数: 4圈";
        }
        // 当前轮数
        roomNowRoundText.text = $"当前轮数: {currentRound}";
    }

    // 初始化玩家信息 根据playlist的顺序判断玩家位置，并根据位置执行setplayerinfo方法
    private void InitializePlayerInfo(PlayerPosition[] playerPositions, PlayerInfo[] playersInfo)
    {
        foreach (var position in playerPositions)
        {
            if (position.username == Administrator.Instance.Username)
            {
                SaveCount = position.position;
                break;
            }
        }
        SetplayerInfo(playersInfo, SaveCount);
    }

    /// 初始化手牌区域 
    private void InitializeHandCards(string[] handTiles, int currentPlayerIndex, string gameStatus)
    {
        // 清空现有手牌
        foreach (Transform child in handCardsContainer)
        {
            Destroy(child.gameObject);
        }

        int cardCount = 0;  // 从0开始计数
        // 实例化新手牌 如果手牌数量等于自己的手牌数量，且当前玩家是自己，且游戏状态是playing，则不再实例化最后一张牌
        foreach (string tile in handTiles)
        {
            if (cardCount == Administrator.Instance.hand_tiles_count - 1 && SaveCount == currentPlayerIndex && gameStatus == "playing")
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

    private void InitializeOtherCards(PlayerPosition[] playerPositions, PlayerInfo[] playersInfo)
    {
        // 获取3D预制体的实际大小
        float cardWidth = tile3DPrefab.GetComponent<Renderer>().bounds.size.y;
        float spacing = cardWidth * 1f; // 间距为卡片宽度的1.2倍
        
        // 根据玩家位置生成手牌，使用世界坐标系的固定方向
        if (SaveCount == 0)
        {
            PlaceCards(leftCardsPosition, playersInfo[3].hand_tiles_count, spacing, new Vector3(0,0,-1)); // 向后
            PlaceCards(topCardsPosition, playersInfo[2].hand_tiles_count, spacing, new Vector3(-1,0,0)); // 向左
            PlaceCards(rightCardsPosition, playersInfo[1].hand_tiles_count, spacing, new Vector3(0,0,1)); // 向前
        }
        else if (SaveCount == 1)
        {
            PlaceCards(leftCardsPosition, playersInfo[0].hand_tiles_count, spacing, new Vector3(0,0,-1)); // 向后
            PlaceCards(topCardsPosition, playersInfo[3].hand_tiles_count, spacing, new Vector3(-1,0,0)); // 向左
            PlaceCards(rightCardsPosition, playersInfo[2].hand_tiles_count, spacing, new Vector3(0,0,1)); // 向前
        }
        else if (SaveCount == 2)
        {
            PlaceCards(leftCardsPosition, playersInfo[1].hand_tiles_count, spacing, new Vector3(0,0,-1)); // 向后
            PlaceCards(topCardsPosition, playersInfo[0].hand_tiles_count, spacing, new Vector3(-1,0,0)); // 向左
            PlaceCards(rightCardsPosition, playersInfo[3].hand_tiles_count, spacing, new Vector3(0,0,1)); // 向前
        }
        else if (SaveCount == 3)
        {
            PlaceCards(leftCardsPosition, playersInfo[2].hand_tiles_count, spacing, new Vector3(0,0,-1)); // 向后
            PlaceCards(topCardsPosition, playersInfo[1].hand_tiles_count, spacing, new Vector3(-1,0,0)); // 向左
            PlaceCards(rightCardsPosition, playersInfo[0].hand_tiles_count, spacing, new Vector3(0,0,1)); // 向前
        }
    }

    // 辅助方法：在指定位置放置卡牌
    private void PlaceCards(Transform startPosition, int cardCount, float spacing, Vector3 direction)
    {
        // 清除该位置现有的卡牌
        foreach (Transform child in startPosition)
        {
            Destroy(child.gameObject);
        }
        
        // 获取起始位置
        Vector3 currentPosition = startPosition.position;
        
        // 调整牌的朝向，使其面向对应的玩家
        Quaternion rotation = Quaternion.identity;
        if (direction == new Vector3(0,0,-1))
            rotation = Quaternion.Euler(90, 0, 0); // 左侧玩家
        else if (direction == new Vector3(-1,0,0))
            rotation = Quaternion.Euler(90, 0, -90); // 上方玩家
        else if (direction == new Vector3(0,0,1))
            rotation = Quaternion.Euler(90, 0, 180); // 右侧玩家
        // 生成卡牌
        for (int i = 0; i < cardCount; i++)
        {
            if (i == 13){
                currentPosition += direction.normalized * spacing;
                GameObject cardObj = Instantiate(tile3DPrefab, currentPosition, rotation);
                cardObj.transform.SetParent(startPosition, worldPositionStays: true);
                cardObj.name = $"Card_Current";
                Debug.Log($"创建卡片 {cardObj.name}");
            }
            else{
                // 创建麻将牌预制体
                GameObject cardObj = Instantiate(tile3DPrefab, currentPosition, rotation);
                // 设置父对象
                cardObj.transform.SetParent(startPosition, worldPositionStays: true);
                cardObj.name = $"Card_{i}";
                // 向指定方向移动以放置下一张牌
                currentPosition += direction.normalized * spacing;
                Debug.Log($"创建卡片 {cardObj.name}");
            }
        }
    }

    // 启动倒计时
    private void loadingRemianTime(int remainingTime, int cuttime){
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
        }
        
        // 启动新的倒计时协程
        _countdownCoroutine = StartCoroutine(CountdownTimer());
    }

    // 倒计时协程
    private IEnumerator CountdownTimer()
    {
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

    private void SetplayerInfo(PlayerInfo[] playersInfo,int SaveCount){
        Debug.Log($"{SaveCount}");
        if (SaveCount == 0)
        {
            // 设定名字和分数
            player_self_name.text = playersInfo[0].username;
            player_self_score.text = playersInfo[0].score.ToString();
            player_self_index.text = "东";
            player_self_current_image.enabled = false;
            player_local_position[0] = "self";
            // 这一变量用于判断服务器传参的手牌长度，决定list尾部的手牌属于手切或摸切
            Administrator.Instance.hand_tiles_count = playersInfo[0].hand_tiles_count; 
            player_left_name.text = playersInfo[3].username;
            player_left_score.text = playersInfo[3].score.ToString();
            player_left_index.text = "北";
            player_left_current_image.enabled = false;
            player_local_position[3] = "left";
            player_top_name.text = playersInfo[2].username;
            player_top_score.text = playersInfo[2].score.ToString();
            player_top_index.text = "西";
            player_top_current_image.enabled = false;
            player_local_position[2] = "top";
            player_right_name.text = playersInfo[1].username;
            player_right_score.text = playersInfo[1].score.ToString();
            player_right_index.text = "南";
            player_right_current_image.enabled = false;
            player_local_position[1] = "right";
        }
        else if (SaveCount == 1)
        {
            // 设定名字和分数
            player_self_name.text = playersInfo[1].username;
            player_self_score.text = playersInfo[1].score.ToString();
            player_self_index.text = "南";
            player_self_current_image.enabled = false;
            Administrator.Instance.hand_tiles_count = playersInfo[1].hand_tiles_count; 
            player_local_position[1] = "self";
            player_left_name.text = playersInfo[0].username;
            player_left_score.text = playersInfo[0].score.ToString();
            player_left_index.text = "东";
            player_left_current_image.enabled = false;
            player_local_position[0] = "left";
            player_top_name.text = playersInfo[3].username;
            player_top_score.text = playersInfo[3].score.ToString();
            player_top_index.text = "北";
            player_top_current_image.enabled = false;
            player_local_position[3] = "top";
            player_right_name.text = playersInfo[2].username;
            player_right_score.text = playersInfo[2].score.ToString();
            player_right_index.text = "西";
            player_right_current_image.enabled = false;
            player_local_position[2] = "right";
        }
        else if (SaveCount == 2)
        {
            // 设定名字和分数
            player_self_name.text = playersInfo[2].username;
            player_self_score.text = playersInfo[2].score.ToString();
            player_self_index.text = "西";
            player_self_current_image.enabled = false;
            Administrator.Instance.hand_tiles_count = playersInfo[2].hand_tiles_count; 
            player_local_position[2] = "self";
            player_left_name.text = playersInfo[1].username;
            player_left_score.text = playersInfo[1].score.ToString();
            player_left_index.text = "南";
            player_left_current_image.enabled = false;
            player_local_position[1] = "left";
            player_top_name.text = playersInfo[0].username;
            player_top_score.text = playersInfo[0].score.ToString();
            player_top_index.text = "东";
            player_top_current_image.enabled = false;
            player_local_position[0] = "top";
            player_right_name.text = playersInfo[3].username;
            player_right_score.text = playersInfo[3].score.ToString();
            player_right_index.text = "北";
            player_right_current_image.enabled = false;
            player_local_position[3] = "right";
        }
        else if (SaveCount == 3)
        {
            // 设定名字和分数
            player_self_name.text = playersInfo[3].username;
            player_self_score.text = playersInfo[3].score.ToString();
            player_self_index.text = "北";
            player_self_current_image.enabled = false;
            Administrator.Instance.hand_tiles_count = playersInfo[3].hand_tiles_count; 
            player_local_position[3] = "self";
            player_left_name.text = playersInfo[2].username;
            player_left_score.text = playersInfo[2].score.ToString();
            player_left_index.text = "西";
            player_left_current_image.enabled = false;
            player_local_position[2] = "left";
            player_top_name.text = playersInfo[1].username;
            player_top_score.text = playersInfo[1].score.ToString();
            player_top_index.text = "南";
            player_top_current_image.enabled = false;
            player_local_position[1] = "top";
            player_right_name.text = playersInfo[0].username;
            player_right_score.text = playersInfo[0].score.ToString();
            player_right_index.text = "东";
            player_right_current_image.enabled = false;
            player_local_position[0] = "right";
        }
    }
}

