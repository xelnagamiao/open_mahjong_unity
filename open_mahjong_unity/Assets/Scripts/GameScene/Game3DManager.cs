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
    [SerializeField] private Transform selfBuhuaPosition; // 自家补花位置
    [SerializeField] private Transform leftBuhuaPosition; // 上家补花位置
    [SerializeField] private Transform topBuhuaPosition; // 对家补花位置
    [SerializeField] private Transform rightBuhuaPosition; // 下家补花位置

    private GameObject LastCutCard; // 最后一张弃牌

    private Vector3 selfSetCombinationsPoint; // 组合指针用于存储各家组合牌生成位置
    private Vector3 leftSetCombinationsPoint;
    private Vector3 topSetCombinationsPoint;
    private Vector3 rightSetCombinationsPoint;

    private GameObject lastCutTile; // 最后一张弃牌的3D对象

    private float cardWidth; // 卡片宽度
    private float cardHeight; // 卡片高度
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
        this.cardWidth = tile3DPrefab.GetComponent<Renderer>().bounds.size.x; // 卡片宽度
        this.cardHeight = tile3DPrefab.GetComponent<Renderer>().bounds.size.y; // 卡片高度
        this.widthSpacing = cardWidth * 1f; // 间距为卡片宽度的1倍
        this.heightSpacing = cardHeight * 1f; // 间距为卡片高度的1倍
    }

    // 出牌3D动画
    private void DisCardAnimation(int tileId,Transform DiscardPosition,Vector3 widthdirection,Vector3 heightdirection,string PlayerPosition,int discardCount){

        // 计算放置位置
        Vector3 currentPosition = DiscardPosition.position;
        // 假设每行最多放 6 张牌
        int cardsPerRow = 6;
        // 将 discardCount 映射到从 0 开始的索引（第1张是0，第6张是5，第7张是6 → 行1列0）
        int index = discardCount - 1;
        // 计算当前是第几行、第几列
        int row = index / cardsPerRow;     // 整除：行号
        int col = index % cardsPerRow;     // 取余：列号
        // 累加偏移
        currentPosition += widthdirection.normalized * widthSpacing * col;
        currentPosition += heightdirection.normalized * heightSpacing * row;

        // 计算放置角度
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
        LastCutCard = cardObj;
        
        // 设置父对象
        cardObj.transform.SetParent(DiscardPosition, worldPositionStays: true);
        cardObj.name = $"Card_{discardCount}";
        
        // 加载并应用材质
        ApplyCardTexture(cardObj, tileId);
    }

    // 补花3D动画
    public void BuhuaAnimation(string GetCardPlayer,int deal_tiles){
        if (GetCardPlayer == "self"){
            int buhuaCount = GameSceneManager.Instance.selfHuapaiList.Count;
            DisCardAnimation(deal_tiles,selfBuhuaPosition,new Vector3(1,0,0),new Vector3(0,0,-1),"self",buhuaCount);
        }
        else if (GetCardPlayer == "left"){
            int buhuaCount = GameSceneManager.Instance.leftHuapaiList.Count;
            DisCardAnimation(deal_tiles,leftBuhuaPosition,new Vector3(0,0,-1),new Vector3(-1,0,0),"left",buhuaCount);
            removeOtherHandCards(leftCardsPosition,false);
        }
        else if (GetCardPlayer == "top"){
            int buhuaCount = GameSceneManager.Instance.topHuapaiList.Count;
            DisCardAnimation(deal_tiles,topBuhuaPosition,new Vector3(-1,0,0),new Vector3(0,0,1),"top",buhuaCount);
            removeOtherHandCards(topCardsPosition,false);
        }
        else if (GetCardPlayer == "right"){
            int buhuaCount = GameSceneManager.Instance.rightHuapaiList.Count;
            DisCardAnimation(deal_tiles,rightBuhuaPosition,new Vector3(0,0,1),new Vector3(1,0,0),"right",buhuaCount);
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
            DisCardAnimation(tileId,selfDiscardsPosition,new Vector3(1,0,0),new Vector3(0,0,-1),"self",GameSceneManager.Instance.selfDiscardslist.Count);
            }
        else if (playerIndex == "left"){
            DisCardAnimation(tileId,rightDiscardsPosition,new Vector3(0,0,1),new Vector3(1,0,0),"right",GameSceneManager.Instance.rightDiscardslist.Count);
            removeOtherHandCards(leftCardsPosition,cut_class); // 如果是他家操作则移除他家手牌
            }
        else if (playerIndex == "top"){
            DisCardAnimation(tileId,leftDiscardsPosition,new Vector3(0,0,-1),new Vector3(-1,0,0),"left",GameSceneManager.Instance.leftDiscardslist.Count);
            removeOtherHandCards(topCardsPosition,cut_class);
            }
        else if (playerIndex == "right"){
            DisCardAnimation(tileId,topDiscardsPosition,new Vector3(-1,0,0),new Vector3(0,0,1),"top",GameSceneManager.Instance.topDiscardslist.Count);
            removeOtherHandCards(rightCardsPosition,cut_class);
            }
        }
    

    // 鸣牌3D动画
    public void ActionAnimation(string playerIndex,string actionType,int[]combination_mask){
        // 根据actionType执行动画
        Quaternion rotation = Quaternion.identity; // 卡牌旋转角度
        Vector3 SetDirection = Vector3.zero; // 放置方向
        Vector3 TempSetPosition = Vector3.zero; // 临时放置位置
        if (playerIndex == "self"){
            rotation = Quaternion.Euler(0, 0, -90); // 获取卡牌旋转角度 
            SetDirection = new Vector3(-1,0,0); // 获取放置方向 向左
            TempSetPosition = selfSetCombinationsPoint; // 获取放置指针
        }
        else if (playerIndex == "left"){
            rotation =  Quaternion.Euler(0, 90, -90); // 左侧玩家
            SetDirection = new Vector3(0,0,1); // 向上
            TempSetPosition = leftSetCombinationsPoint;
        }
        else if (playerIndex == "top"){
            rotation =  Quaternion.Euler(0, 180, -90); // 上方玩家
            SetDirection = new Vector3(1,0,0); // 向右
            TempSetPosition = topSetCombinationsPoint;
        }
        else if (playerIndex == "right"){
            rotation =  Quaternion.Euler(0, 270, -90); // 右侧玩家
            SetDirection = new Vector3(0,0,-1); // 向下
            TempSetPosition = rightSetCombinationsPoint;
        }
        // 获取了rotation(卡牌旋转角度) SetDirection(放置方向) 以及公共变量 $SetCombinationsPoint
        List<int> SetTileList = new List<int>();
        List<int> SignDirectionList = new List<int>();
        // 放置牌原理是从右向左放置,为了理解方便SetTileList和SignDirectionList从左到右放置随后倒转列表

        foreach (int tileId in combination_mask){
            if (tileId >= 10){
                SetTileList.Add(tileId);
            }
            else if (tileId < 5){
                SignDirectionList.Add(tileId);
            }
        }
        // 倒转SetTileList
        SetTileList.Reverse();
        // 倒转SignDirectionList
        SignDirectionList.Reverse();
        // 获得卡牌朝向rotation 和放置方向 SetDirection

        // 执行动画
        for (int i = 0; i < SetTileList.Count; i++) {
            // 声明变量在条件语句之前
            GameObject cardObj;
            
            // 如果卡牌横置,指针增加一个宽度单位
            if (SignDirectionList[i] == 1){
                Quaternion TurnWidthRotation = Quaternion.Euler(0,0,-90);
                cardObj = Instantiate(tile3DPrefab, TempSetPosition, TurnWidthRotation);
                TempSetPosition += SetDirection * widthSpacing;
            }
            // 如果卡牌竖置
            else{
                cardObj = Instantiate(tile3DPrefab, TempSetPosition, rotation);
                TempSetPosition += SetDirection * heightSpacing;
            }
            // 设置卡牌纹理
            ApplyCardTexture(cardObj, SetTileList[i]);
        }
        // 将更新后的指针位置赋值给公共变量
        if (playerIndex == "self"){
            selfSetCombinationsPoint = TempSetPosition;
        }
        else if (playerIndex == "left"){
            leftSetCombinationsPoint = TempSetPosition;
        }
        else if (playerIndex == "top"){
            topSetCombinationsPoint = TempSetPosition;
        }
        else if (playerIndex == "right"){
            rightSetCombinationsPoint = TempSetPosition;
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



}
