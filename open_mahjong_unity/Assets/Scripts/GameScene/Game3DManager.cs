using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Threading.Tasks;

public class Game3DManager : MonoBehaviour
{
    [SerializeField] private GameObject tile3DPrefab;    // 3D预制体
    [Header("3D对象生成位置")]
    [SerializeField] private Transform leftCardsPosition; // 左家手牌位置
    [SerializeField] private Transform leftDiscardsPosition; // 左家弃牌位置
    [SerializeField] private Transform leftBuhuaPosition; // 左家补花位置
    [SerializeField] private Transform leftCombinationsPosition; // 左家组合位置
    
    [SerializeField] private Transform topCardsPosition; // 对家手牌位置
    [SerializeField] private Transform topDiscardsPosition; // 对家弃牌位置
    [SerializeField] private Transform topBuhuaPosition; // 对家补花位置
    [SerializeField] private Transform topCombinationsPosition; // 对家组合位置
    
    [SerializeField] private Transform rightCardsPosition; // 右家手牌位置
    [SerializeField] private Transform rightDiscardsPosition; // 右家弃牌位置
    [SerializeField] private Transform rightBuhuaPosition; // 右家补花位置
    [SerializeField] private Transform rightCombinationsPosition; // 右家组合位置

    [SerializeField] private Transform selfCardsPosition; // 自家手牌位置
    [SerializeField] private Transform selfDiscardsPosition; // 自家弃牌位置
    [SerializeField] private Transform selfBuhuaPosition; // 自家补花位置
    [SerializeField] private Transform selfCombinationsPosition; // 自家组合位置

    private Vector3 selfSetCombinationsPoint; // 组合指针用于存储各家组合牌生成位置
    private Vector3 leftSetCombinationsPoint;
    private Vector3 topSetCombinationsPoint;
    private Vector3 rightSetCombinationsPoint;

    private GameObject lastCut3DObject; // 最后一张弃牌的3D对象
    private Dictionary<int,Vector3> pengToJiagangPosDict = new Dictionary<int,Vector3>(); // 碰牌的加杠预留指针

    private float cardWidth; // 卡片宽度
    private float cardHeight; // 卡片高度
    private float cardScale; // 卡片缩放
    private float widthSpacing; // 间距为卡片宽度的1倍
    private float heightSpacing; // 间距为卡片高度的1倍
    // private Quaternion baseRotation = Quaternion.Euler(0, 90, -90); // 卡牌默认平躺转角
    // private Quaternion leftRotation = Quaternion.Euler(0, 90, 0); // 左侧玩家转角
    // private Quaternion topRotation = Quaternion.Euler(0, 180, 0); // 上方玩家转角
    // private Quaternion rightRotation = Quaternion.Euler(0, 270, 0); // 右侧玩家转角
    // private Quaternion erectRotation = Quaternion.Euler(-90, 0, 0); // 竖立卡牌转角
    // private Quaternion TurnWidthRotation = Quaternion.Euler(0,90,0); // 平躺时横置转角

    public static Game3DManager Instance { get; private set; }
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        // 初始化配置
        this.cardScale = tile3DPrefab.transform.localScale.z; // 卡片缩放比例
        this.cardWidth = tile3DPrefab.GetComponent<Renderer>().bounds.size.x; // 卡片宽度
        this.cardHeight = tile3DPrefab.GetComponent<Renderer>().bounds.size.y; // 卡片高度
        this.widthSpacing = cardWidth * 1f; // 间距为卡片宽度的1倍
        this.heightSpacing = cardHeight * 1f; // 间距为卡片高度的1倍
        // 初始化放置组合牌指针
        selfSetCombinationsPoint = selfCombinationsPosition.position;
        leftSetCombinationsPoint = leftCombinationsPosition.position;
        topSetCombinationsPoint = topCombinationsPosition.position;
        rightSetCombinationsPoint = rightCombinationsPosition.position;
    }

    // 出牌3D动画
    private void Set3DTile(int tileId,Transform SetPosition,string SetType ,string PlayerPosition,int TileCount){

        // 获取放置位置
        Vector3 currentPosition = SetPosition.position;
        // 获取放置角度
        Quaternion rotation = Quaternion.identity;

        // 获取不同玩家的相对右侧和相对下侧
        Vector3 widthdirection = Vector3.zero;
        Vector3 heightdirection = Vector3.zero;
        if (PlayerPosition == "self"){
            widthdirection = new Vector3(1,0,0);
            heightdirection = new Vector3(0,0,-1);
            rotation = Quaternion.Euler(0, 0, -90);
        }
        else if (PlayerPosition == "left"){
            widthdirection = new Vector3(0,0,-1);
            heightdirection = new Vector3(-1,0,0);
            rotation = Quaternion.Euler(0, 90, -90);
        }
        else if (PlayerPosition == "top"){
            widthdirection = new Vector3(-1,0,0);
            heightdirection = new Vector3(0,0,1);
            rotation = Quaternion.Euler(0, 180, -90);
        }
        else if (PlayerPosition == "right"){
            widthdirection = new Vector3(0,0,1);
            heightdirection = new Vector3(1,0,0);
            rotation = Quaternion.Euler(0, 270, -90);
        }

        // 获取每行最多放多少张牌
        int cardsPerRow;
        // 弃牌每行最多放 6 张牌
        if (SetType == "discard"){
            cardsPerRow = 6;
        }
        // 补花每行最多放 8 张牌
        else if (SetType == "buhua"){
            cardsPerRow = 8;
        }
        else{
            Debug.LogError($"SetType {SetType} 错误");
            return;
        }
    
        // 将 discardCount 映射到从 0 开始的索引（第1张是0，第6张是5，第7张是6 → 行1列0）
        int index = TileCount - 1;
        // 计算当前是第几行、第几列
        int row = index / cardsPerRow;     // 整除：行号
        int col = index % cardsPerRow;     // 取余：列号

        currentPosition += widthdirection.normalized * widthSpacing * col;
        currentPosition += heightdirection.normalized * heightSpacing * row;


        Debug.Log($"创建卡片 {TileCount}, 牌ID: {tileId}");
        // 创建麻将牌预制体
        GameObject cardObj = Instantiate(tile3DPrefab, currentPosition, rotation);
        lastCut3DObject = cardObj;
        // 设置父对象
        cardObj.transform.SetParent(SetPosition, worldPositionStays: true);
        cardObj.name = $"Card_{TileCount}";
        
        // 加载并应用材质
        ApplyCardTexture(cardObj, tileId);
    }

    // 补花3D动画
    public void BuhuaAnimation(string GetCardPlayer,int deal_tiles){
        if (GetCardPlayer == "self"){
            int buhuaCount = GameSceneManager.Instance.selfHuapaiList.Count;
            Set3DTile(deal_tiles,selfBuhuaPosition,"buhua","self",buhuaCount);
        }
        else if (GetCardPlayer == "left"){
            int buhuaCount = GameSceneManager.Instance.leftHuapaiList.Count;
            Set3DTile(deal_tiles,leftBuhuaPosition,"buhua","left",buhuaCount);
            removeOtherHandCards(leftCardsPosition,false);
        }
        else if (GetCardPlayer == "top"){
            int buhuaCount = GameSceneManager.Instance.topHuapaiList.Count;
            Set3DTile(deal_tiles,topBuhuaPosition,"buhua","top",buhuaCount);
            removeOtherHandCards(topCardsPosition,false);
        }
        else if (GetCardPlayer == "right"){
            int buhuaCount = GameSceneManager.Instance.rightHuapaiList.Count;
            Set3DTile(deal_tiles,rightBuhuaPosition,"buhua","right",buhuaCount);
            removeOtherHandCards(rightCardsPosition,false);
        }
    }

    // 在其他玩家手牌右侧空一格的位置添加一张3Dcard 标识摸到的牌
    public void GetCard3D(string playerIndex){
        // 如果玩家是其他玩家，则将牌添加到他人手牌中
        if (playerIndex == "left"){
            Quaternion rotation = Quaternion.Euler(-90,0,0);
            Vector3 SetPosition = leftCardsPosition.position + (leftCardsPosition.childCount+2) * widthSpacing * new Vector3(0,0,-1); // 计算卡牌位置
            GameObject cardObj = Instantiate(tile3DPrefab, SetPosition, rotation);
            cardObj.transform.SetParent(leftCardsPosition, worldPositionStays: true);
            cardObj.name = $"Card_Current";
        }
        else if (playerIndex == "top"){
            Quaternion rotation = Quaternion.Euler(-90,0,90);
            Vector3 SetPosition = topCardsPosition.position + (topCardsPosition.childCount+2) * widthSpacing * new Vector3(-1,0,0);
            GameObject cardObj = Instantiate(tile3DPrefab, SetPosition, rotation);
            cardObj.transform.SetParent(topCardsPosition, worldPositionStays: true);
            cardObj.name = $"Card_Current";
        }
        else if (playerIndex == "right"){
            Quaternion rotation = Quaternion.Euler(-90,0,180);
            Vector3 SetPosition = rightCardsPosition.position + (rightCardsPosition.childCount+2) * widthSpacing * new Vector3(0,0,1);
            GameObject cardObj = Instantiate(tile3DPrefab, SetPosition, rotation);
            cardObj.transform.SetParent(rightCardsPosition, worldPositionStays: true);
            cardObj.name = $"Card_Current";
        }
    }

    public void CutCards(string playerIndex,int tileId,bool cut_class){
            // 显示切牌
        if (playerIndex == "self"){
            // 生成3D弃牌
            Set3DTile(tileId,selfDiscardsPosition,"discard","self",GameSceneManager.Instance.selfDiscardslist.Count);
            }
        else if (playerIndex == "left"){
            Set3DTile(tileId,leftDiscardsPosition,"discard","left",GameSceneManager.Instance.leftDiscardslist.Count);
            removeOtherHandCards(leftCardsPosition,cut_class); // 如果是他家操作则移除他家手牌
            }
        else if (playerIndex == "top"){
            Set3DTile(tileId,topDiscardsPosition,"discard","top",GameSceneManager.Instance.topDiscardslist.Count);
            removeOtherHandCards(topCardsPosition,cut_class);
            }
        else if (playerIndex == "right"){
            Set3DTile(tileId,rightDiscardsPosition,"discard","right",GameSceneManager.Instance.rightDiscardslist.Count);
            removeOtherHandCards(rightCardsPosition,cut_class);
            }
        }
    

    // 鸣牌3D动画
    public void ActionAnimation(string playerIndex,string actionType,int[]combination_mask,string action,string combination_target){
        // 根据actionType执行动画
        Quaternion rotation = Quaternion.identity; // 卡牌旋转角度
        Vector3 SetDirection = Vector3.zero; // 放置方向
        Vector3 SetPositionpoint = Vector3.zero; // 放置位置
        Vector3 JiagangDirection = Vector3.zero; // 加杠方向
        Transform SetParent = null; // 设置父对象
        if (playerIndex == "self"){
            rotation = Quaternion.Euler(0, 0, -90); // 获取卡牌旋转角度 
            SetDirection = new Vector3(-1,0,0); // 获取放置方向 向左
            JiagangDirection = new Vector3(0,0,1); // 自家加杠指针是向上
            SetPositionpoint = selfSetCombinationsPoint; // 获取放置指针
            SetParent = selfCombinationsPosition;
        }
        else if (playerIndex == "left"){
            rotation =  Quaternion.Euler(0, 90, -90); // 左侧玩家
            SetDirection = new Vector3(0,0,1); // 向上
            JiagangDirection = new Vector3(1,0,0); // 左侧玩家加杠指针是向右
            SetPositionpoint = leftSetCombinationsPoint;
            SetParent = leftCombinationsPosition;
        }
        else if (playerIndex == "top"){
            rotation =  Quaternion.Euler(0, 180, -90); // 上方玩家
            SetDirection = new Vector3(1,0,0); // 向右
            JiagangDirection = new Vector3(0,0,-1); // 上方玩家加杠指针是向下
            SetPositionpoint = topSetCombinationsPoint;
            SetParent = topCombinationsPosition;
        }
        else if (playerIndex == "right"){
            rotation =  Quaternion.Euler(0, 270, -90); // 右侧玩家
            SetDirection = new Vector3(0,0,-1); // 向下
            JiagangDirection = new Vector3(-1,0,0); // 右侧玩家加杠指针是向左
            SetPositionpoint = rightSetCombinationsPoint;
        }
        // 获取了rotation(卡牌旋转角度) SetDirection(放置方向) 以及公共变量 $SetCombinationsPoint
        List<int> SetTileList = new List<int>();
        List<int> SignDirectionList = new List<int>();

        // 解码combination_mask 获得需要放置的卡牌列表和卡牌朝向列表
        foreach (int tileId in combination_mask){
            if (tileId >= 10){
                SetTileList.Add(tileId);
            }
            else if (tileId < 5){
                SignDirectionList.Add(tileId);
            }
        }
        // 倒转SetTileList和SignDirectionList 因为卡牌的逻辑顺序是从左到右，但我们需要从右到左放置
        SetTileList.Reverse();
        SignDirectionList.Reverse();
        Debug.Log($"SetTileList: {SetTileList}");
        Debug.Log($"SignDirectionList: {SignDirectionList}");

        // 执行动画
        // 加杠
        if (action == "jiagang"){
             for (int i = 0; i < SetTileList.Count; i++) {
                if (SignDirectionList[i] == 3){
                    GameObject cardObj;
                    Vector3 TempPositionpoint = pengToJiagangPosDict[SetTileList[i]]; // 获取加杠位置
                    Quaternion TempRotation = Quaternion.Euler(0,90,0) * rotation; // 横
                    TempPositionpoint += JiagangDirection * widthSpacing; // 加杠向上一个宽度单位
                    cardObj = Instantiate(tile3DPrefab, TempPositionpoint, TempRotation);
                    ApplyCardTexture(cardObj, SetTileList[i]);
                    // 设置父对象
                    cardObj.transform.SetParent(SetParent, worldPositionStays: true);
                }
            }
        }
        // 正常放置卡牌
        else{
            for (int i = 0; i < SetTileList.Count; i++) {
                GameObject cardObj;
                Quaternion TempRotation = Quaternion.identity;
                Vector3 TempPositionpoint = SetPositionpoint;
                // 0代表竖 1代表横 2代表暗面 3代表上侧(加杠) 4代表空
                // 卡牌竖置 指针增加一个宽度单位
                if (SignDirectionList[i] == 0){
                    TempRotation = rotation; // 竖
                    TempPositionpoint += SetDirection * widthSpacing;
                    SetPositionpoint += SetDirection * widthSpacing; // 吃碰的竖置牌每张牌向左一个宽度单位
                }
                // 卡牌横置,放置角度叠加横置 指针增加一个高度单位
                else if (SignDirectionList[i] == 1){
                    TempRotation = Quaternion.Euler(0,90,0) * rotation; // 横
                    SetPositionpoint += SetDirection * heightSpacing;
                    TempPositionpoint += SetDirection * heightSpacing; // 吃碰的横置牌向左一个高度单位
                    if (action == "peng"){
                        pengToJiagangPosDict.Add(SetTileList[i],SetPositionpoint); // 碰牌的加杠预留指针 保存在碰牌int id的横置位置
                    }
                }
                // 卡牌暗面 指针增加一个宽度单位
                else if (SignDirectionList[i] == 2){
                    TempRotation = Quaternion.Euler(0,0,-180) * rotation; // 暗面
                    SetPositionpoint += SetDirection * widthSpacing;
                    TempPositionpoint += SetDirection * widthSpacing; // 暗杠每张牌向左一个宽度单位
                }
                // 卡牌加杠
                else if (SignDirectionList[i] == 3){
                    TempRotation = Quaternion.Euler(0,90,0) * rotation; // 横
                    TempPositionpoint += JiagangDirection * widthSpacing; // 加杠向上一个宽度单位
                }

                // 创建卡牌
                cardObj = Instantiate(tile3DPrefab, TempPositionpoint, TempRotation);
                // 设置卡牌纹理
                ApplyCardTexture(cardObj, SetTileList[i]);
                // 设置父对象
                cardObj.transform.SetParent(SetParent, worldPositionStays: true);
            }

            // 将更新后的指针位置赋值给公共变量
            if (playerIndex == "self"){
                selfSetCombinationsPoint = SetPositionpoint;
            }
            else if (playerIndex == "left"){
                leftSetCombinationsPoint = SetPositionpoint;
            }
            else if (playerIndex == "top"){
                topSetCombinationsPoint = SetPositionpoint;
            }
            else if (playerIndex == "right"){
                rightSetCombinationsPoint = SetPositionpoint;
            }
        }


        // 根据actionType删除手牌
        if (playerIndex == "self"){
            // 暗杠在手牌中删除所有生成的卡牌
            if (actionType == "angang"){
                foreach (int tileID in SetTileList){
                    GameSceneManager.Instance.handTiles.Remove(tileID);
                }
            }
            // 加杠在手牌中删除生成的第一张卡牌
            else if (actionType == "jiagang"){
                GameSceneManager.Instance.handTiles.Remove(SetTileList[0]);
            }
            // 吃碰杠在手牌中先添加最后一张弃牌，再删除生成的所有卡牌
            else if (actionType == "chi_left" || actionType == "chi_right" || actionType == "chi_mid" || actionType == "peng" || actionType == "gang"){
                GameSceneManager.Instance.handTiles.Add(GameSceneManager.Instance.lastCutCardID);
                GameCanvas.Instance.ChangeHandCards("RemoveCombinationCard",0,SetTileList.ToArray());
                foreach (int tileID in SetTileList){
                    GameSceneManager.Instance.handTiles.Remove(tileID);
                }
            }
        }
    }


    private async void removeOtherHandCards(Transform cardPosition,bool cut_class){
        Debug.Log($"移除其他玩家手牌 {cardPosition},{cut_class}");

        // 如果摸切就直接删除摸到的牌
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
        // 如果摸牌则随机删除一张主牌区的牌，将摸牌区的卡牌加入手牌区
        else{
            // 计算子物体数量，获取随机索引，随机删除选中的子物体
            int childCount = cardPosition.childCount;
                int randomIndex = UnityEngine.Random.Range(0, childCount);
                Transform randomChild = cardPosition.GetChild(randomIndex);
                    Debug.Log($"随机删除了索引为 {randomIndex} 的牌");
                    Destroy(randomChild.gameObject);

            // 等待1秒 模拟玩家将摸牌区卡牌放入手牌区
            await Task.Delay(1000);
                    
            // 确定方向向量 方向向量乘以spacing等于目标位置
                    Vector3 direction = Vector3.zero;
                    if (cardPosition == rightCardsPosition)
                        direction = new Vector3(0, 0, 1); // 向前
                    else if (cardPosition == leftCardsPosition)
                        direction = new Vector3(0, 0, -1); // 向后
                    else if (cardPosition == topCardsPosition)
                        direction = new Vector3(-1, 0, 0); // 向左
                    
            // 获取所有剩余子物体并按照名称索引排序[0,1,2,3,4,5,6,7,8,9,10,11,12]
                    List<Transform> remainingCards = new List<Transform>();
            int cardCount = cardPosition.childCount;
            for (int i = 0; i < cardCount; i++) {
                        remainingCards.Add(cardPosition.GetChild(i));
                    }
            // 拿取手牌初始位置，遍历卡牌乘以spacing移动到目标位置
                    Vector3 startPosition = cardPosition.position;
            for (int i = 0; i < cardCount; i++) {
                Vector3 newPosition = startPosition + direction * widthSpacing * (i + 0);
                        remainingCards[i].position = newPosition;
                remainingCards[i].name = $"ReSeTCard_{i}";
            }
        }
    }



    // 初始化其他玩家手牌3D动画
    public void InitializeOtherCards(PlayerInfo[] playersInfo){
        // 根据玩家位置生成手牌，使用世界坐标系的固定方向
        if (GameSceneManager.Instance.selfIndex == 0)
        {
            PlaceCards(leftCardsPosition, playersInfo[3].hand_tiles_count, widthSpacing, new Vector3(0,0,-1)); // 向后
            PlaceCards(topCardsPosition, playersInfo[2].hand_tiles_count, widthSpacing, new Vector3(-1,0,0)); // 向左
            PlaceCards(rightCardsPosition, playersInfo[1].hand_tiles_count, widthSpacing, new Vector3(0,0,1)); // 向前
        }
        else if (GameSceneManager.Instance.selfIndex == 1)
        {
            PlaceCards(leftCardsPosition, playersInfo[0].hand_tiles_count, widthSpacing, new Vector3(0,0,-1)); // 向后
            PlaceCards(topCardsPosition, playersInfo[3].hand_tiles_count, widthSpacing, new Vector3(-1,0,0)); // 向左
            PlaceCards(rightCardsPosition, playersInfo[2].hand_tiles_count, widthSpacing, new Vector3(0,0,1)); // 向前
        }
        else if (GameSceneManager.Instance.selfIndex == 2)
        {
            PlaceCards(leftCardsPosition, playersInfo[1].hand_tiles_count, widthSpacing, new Vector3(0,0,-1)); // 向后
            PlaceCards(topCardsPosition, playersInfo[0].hand_tiles_count, widthSpacing, new Vector3(-1,0,0)); // 向左
            PlaceCards(rightCardsPosition, playersInfo[3].hand_tiles_count, widthSpacing, new Vector3(0,0,1)); // 向前
        }
        else if (GameSceneManager.Instance.selfIndex == 3)
        {
            PlaceCards(leftCardsPosition, playersInfo[2].hand_tiles_count, widthSpacing, new Vector3(0,0,-1)); // 向后
            PlaceCards(topCardsPosition, playersInfo[1].hand_tiles_count, widthSpacing, new Vector3(-1,0,0)); // 向左
            PlaceCards(rightCardsPosition, playersInfo[0].hand_tiles_count, widthSpacing, new Vector3(0,0,1)); // 向前
        }
    }
    
    // 辅助方法：在指定位置放置卡牌
    private void PlaceCards(Transform startPosition, int cardCount, float spacing, Vector3 direction){
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
            rotation = Quaternion.Euler(-90,0,0); // 左侧玩家
        else if (direction == new Vector3(-1,0,0))
            rotation = Quaternion.Euler(-90,0,90); // 上方玩家
        else if (direction == new Vector3(0,0,1))
            rotation = Quaternion.Euler(-90,0,180); // 右侧玩家
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


    // 应用牌面纹理
    private void ApplyCardTexture(GameObject cardObj, int tileId) {

        // 从Resources加载对应ID的图片
        Texture2D texture = Resources.Load<Texture2D>($"image/CardFaceMaterial_xuefun/{tileId}");
        if (texture == null) {
            Debug.LogError($"无法加载纹理: image/CardFaceMaterial_xuefun/{tileId}");
            return;
        }
        
        // 获取预制体的渲染器
        Renderer renderer = cardObj.GetComponent<Renderer>();
        
        // 通过材质名称找到SetImage材质
        int setImageMaterialIndex = -1;
        for (int i = 0; i < renderer.materials.Length; i++) {
            if (renderer.materials[i].name.Contains("SetImage")) {
                setImageMaterialIndex = i;
                break;
            }
        }
        
        if (setImageMaterialIndex != -1) {
            renderer.materials[setImageMaterialIndex].mainTexture = texture;
            Debug.Log($"应用纹理到卡片 {tileId} 的SetImage材质完成");
        } else {
            Debug.LogError($"未找到SetImage材质，材质列表: {string.Join(", ", System.Array.ConvertAll(renderer.materials, m => m.name))}");
        }
    }

    public void Clear3DTile(){
        // 初始化配置
        this.cardScale = tile3DPrefab.transform.localScale.z; // 卡片缩放比例
        this.cardWidth = tile3DPrefab.GetComponent<Renderer>().bounds.size.x; // 卡片宽度
        this.cardHeight = tile3DPrefab.GetComponent<Renderer>().bounds.size.y; // 卡片高度
        this.widthSpacing = cardWidth * 1f; // 间距为卡片宽度的1倍
        this.heightSpacing = cardHeight * 1f; // 间距为卡片高度的1倍
        pengToJiagangPosDict.Clear();
        foreach (Transform child in leftCardsPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in topCardsPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in rightCardsPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in selfCardsPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in leftDiscardsPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in topDiscardsPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in rightDiscardsPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in selfDiscardsPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in leftBuhuaPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in topBuhuaPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in rightBuhuaPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in selfBuhuaPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in leftCombinationsPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in topCombinationsPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in rightCombinationsPosition){
            Destroy(child.gameObject);
        }
        foreach (Transform child in selfCombinationsPosition){
            Destroy(child.gameObject);
        }
    }

}
