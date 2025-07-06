using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

    private int buhuaSelfCount; // 补花计数
    private int buhuaLeftCount;
    private int buhuaTopCount;
    private int buhuaRightCount;
    
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
    }

    // 出牌3D动画
    private void DisCardAnimation(int tileId,Transform DiscardPosition,Vector3 widthdirection,Vector3 heightdirection,string PlayerPosition,int discardCount){
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
        LastCutCard = cardObj;
        
        // 设置父对象
        cardObj.transform.SetParent(DiscardPosition, worldPositionStays: true);
        cardObj.name = $"Card_{discardCount}";
        
        // 加载并应用材质
        ApplyCardTexture(cardObj, tileId);
    }

    // 补花3D动画
    public void BuhuaAnimation(int playerIndex,int deal_tiles,int remain_tiles){
        Debug.Log($"补花动画 {playerIndex},{deal_tiles},{remain_tiles}");
        remiansTilesText.text = $"余: {remain_tiles}";
        string GetCardPlayer = player_local_position[playerIndex];
        if (GetCardPlayer == "self"){
            DisCardAnimation(deal_tiles,selfBuhuaPosition,new Vector3(1,0,0),new Vector3(0,0,-1),"self",buhuaSelfCount);
            buhuaSelfCount++;
        }
        else if (GetCardPlayer == "left"){
            DisCardAnimation(deal_tiles,leftBuhuaPosition,new Vector3(0,0,-1),new Vector3(-1,0,0),"left",buhuaLeftCount);
            buhuaLeftCount++;
        }
        else if (GetCardPlayer == "top"){
            DisCardAnimation(deal_tiles,topBuhuaPosition,new Vector3(-1,0,0),new Vector3(0,0,1),"top",buhuaTopCount);
            buhuaTopCount++;
        }
        else if (GetCardPlayer == "right"){
            DisCardAnimation(deal_tiles,rightBuhuaPosition,new Vector3(0,0,1),new Vector3(1,0,0),"right",buhuaRightCount);
            buhuaRightCount++;
        }
    }

    // 鸣牌3D动画
    public void ActionAnimation(string actionType,int tileId,string playerPosition,string DoActionFrom){
        // 根据actionType执行动画
        Quaternion rotation = Quaternion.identity; // 卡牌旋转角度
        Vector3 SetDirection = Vector3.zero; // 放置方向
        Vector3 TempSetPosition = Vector3.zero; // 临时放置位置
        if (playerPosition == "self"){
            rotation = Quaternion.Euler(0, 0, -90); // 获取卡牌旋转角度 
            SetDirection = new Vector3(-1,0,0); // 获取放置方向 向左
            TempSetPosition = selfSetCombinationsPoint; // 获取放置指针
        }
        else if (playerPosition == "left"){
            rotation = Quaternion.Euler(0, 90, -90); // 左侧玩家
            SetDirection = new Vector3(0,0,1); // 向上
            TempSetPosition = leftSetCombinationsPoint;
        }
        else if (playerPosition == "top"){
            rotation = Quaternion.Euler(0, 180, -90); // 上方玩家
            SetDirection = new Vector3(1,0,0); // 向右
            TempSetPosition = topSetCombinationsPoint;
        }
        else if (playerPosition == "right"){
            rotation = Quaternion.Euler(0, 270, -90); // 右侧玩家
            SetDirection = new Vector3(0,0,-1); // 向下
            TempSetPosition = rightSetCombinationsPoint;
        }
        // 获取了rotation(卡牌旋转角度) SetDirection(放置方向) 以及公共变量 $SetCombinationsPoint
        List<int> SetTileList = new List<int>();
        List<int> SignDirectionList = new List<int>();
        // 放置牌原理是从右向左放置,为了理解方便SetTileList和SignDirectionList从左到右放置随后倒转列表
        if (actionType == "chi_left"){ 
            SetTileList.Add(tileId);
            SetTileList.Add(tileId-2); 
            SetTileList.Add(tileId-1);
            // [5,3,4]
            SignDirectionList = new List<int>{1,0,0};
        }
        else if (actionType == "chi_mid"){
            SetTileList.Add(tileId);
            SetTileList.Add(tileId-1);
            SetTileList.Add(tileId+1);
            // [5,4,6]
            SignDirectionList = new List<int> {1,0,0};
        }
        else if (actionType == "chi_right"){
            SetTileList.Add(tileId);
            SetTileList.Add(tileId+1);
            SetTileList.Add(tileId+2);
            // [5,6,7]
            SignDirectionList = new List<int> {1,0,0};
        }
        else if (actionType == "peng" || actionType == "gang"){
            SetTileList.Add(tileId);
            SetTileList.Add(tileId);
            SetTileList.Add(tileId);
            // [5,5,5]
            SignDirectionList = new List<int> {1,1,1};

            if (playerPosition == "self"){
                if (DoActionFrom == "left"){
                    SignDirectionList = new List<int> {1,0,0};
                }
                else if (DoActionFrom == "top"){
                    SignDirectionList = new List<int> {0,1,0};
                }
                else if (DoActionFrom == "right"){
                    SignDirectionList = new List<int> {0,0,1};
                }
            }
            else if (playerPosition == "left"){
                if (DoActionFrom == "self"){
                    SignDirectionList = new List<int> {0,0,1};
                }
                else if (DoActionFrom == "right"){
                    SignDirectionList = new List<int> {0,1,0};
                }
                else if (DoActionFrom == "top"){
                    SignDirectionList = new List<int> {1,0,0};
                }
            }
            else if (playerPosition == "top"){
                if (DoActionFrom == "self"){
                    SignDirectionList = new List<int> {0,1,0};
                }
                else if (DoActionFrom == "right"){
                    SignDirectionList = new List<int> {1,0,0};
                }
                else if (DoActionFrom == "left"){
                    SignDirectionList = new List<int> {0,0,1};
                }
            }
            else if (playerPosition == "right"){
                if (DoActionFrom == "self"){
                    SignDirectionList = new List<int> {1,0,0};
                }
                else if (DoActionFrom == "left"){
                    SignDirectionList = new List<int> {0,1,0};
                }
                else if (DoActionFrom == "top"){
                    SignDirectionList = new List<int> {0,0,1};
                }
            }
            if (actionType == "gang"){
                SetTileList.Add(tileId);
                SignDirectionList.Insert(2, 0);
            }
        }

        // 倒转SetTileList
        SetTileList.Reverse();
        // 倒转SignDirectionList
        SignDirectionList.Reverse();
        // 获得卡牌朝向rotation 和放置方向 SetDirection
        float cardWidth = tile3DPrefab.GetComponent<Renderer>().bounds.size.y;
        float cardHeight = tile3DPrefab.GetComponent<Renderer>().bounds.size.z;
        float widthSpacing = cardWidth * 1f; // 间距为卡片宽度的1倍
        float heightSpacing = cardHeight * 1f; // 间距为卡片高度的1倍
        // 执行动画
        for (int i = 0; i < SetTileList.Count; i++) {
            // 声明变量在条件语句之前
            GameObject cardObj;
            
            // 如果卡牌横置,指针增加一个宽度单位
            if (SignDirectionList[i] == 1){
                Quaternion TurnWidthRotation = Quaternion.Euler(0,90,0) * rotation;
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
        if (playerPosition == "self"){
            selfSetCombinationsPoint = TempSetPosition;
        }
        else if (playerPosition == "left"){
            leftSetCombinationsPoint = TempSetPosition;
        }
        else if (playerPosition == "top"){
            topSetCombinationsPoint = TempSetPosition;
        }
        else if (playerPosition == "right"){
            rightSetCombinationsPoint = TempSetPosition;
        }
    }

    // 初始化其他玩家手牌3D动画
    public void InitializeOtherCards(PlayerInfo[] playersInfo){
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


    // 应用牌面纹理
    private void ApplyCardTexture(GameObject cardObj, int tileId) {
        // 从Resources加载对应ID的图片
        Texture2D texture = Resources.Load<Texture2D>($"image/CardMaterial/{tileId}");
        
        if (texture == null) {
            Debug.LogError($"无法加载纹理: image/CardMaterial/{tileId}");
            return;
        }
        
        // 获取预制体的渲染器
        Renderer renderer = cardObj.GetComponent<Renderer>();
        
        // 克隆材质以避免修改原始材质
        Material[] materials = renderer.materials;        
        
        // 修改第二个材质的纹理及相关设置
        if (materials.Length > 1) {
            // 设置纹理
        materials[1].mainTexture = texture;         
            
            // 设置纹理属性
            materials[1].mainTextureScale = new Vector2(1, 1);          // 设置缩放为1:1
            materials[1].mainTextureOffset = new Vector2(0, 0);         // 设置偏移为0
            
            // 设置纹理包裹模式为Clamp，防止拉伸
            texture.wrapMode = TextureWrapMode.Clamp;
            
            // 设置过滤模式为点过滤，保持像素锐利度
            texture.filterMode = FilterMode.Bilinear;
            
        // 应用修改后的材质
        renderer.materials = materials;
            
            Debug.Log($"应用纹理到卡片 {tileId} 完成");
        } else {
            Debug.LogError($"材质数组长度不足: {materials.Length}");
        }
    }



}
